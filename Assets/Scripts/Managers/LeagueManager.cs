using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>한 팀(유저 팀 또는 AI 팀)의 시즌 성적/로스터.</summary>
    public class TeamInfo
    {
        public Team Team;
        public bool IsUserTeam;
        public int Wins;
        public int Draws;
        public int Losses;
        public List<Player> Roster = new List<Player>();

        /// <summary>KBO 승률 규칙: 승 / (승+패). 무승부는 분모에서 제외한다.</summary>
        public float WinRate => (Wins + Losses) == 0 ? 0f : (float)Wins / (Wins + Losses);
    }

    /// <summary>유저 팀 시즌 캘린더의 경기 한 건.</summary>
    public class MatchFixture
    {
        public int GameNumber; // 1~144
        public Team HomeTeam;
        public Team AwayTeam;
        public LeaguePhase Phase; // GameNumber 1~72 = REGULAR_OPEN, 73~144 = REGULAR_LOCKED
        public bool IsPlayed;
        public MatchResult Result;
    }

    /// <summary>
    /// 유저 팀 1개 + KBO 나머지 9개 구단(AI)으로 리그를 구성하고, 유저 팀 기준 144경기(9개 상대 x 16경기)
    /// 스케줄을 생성/진행하며 각 팀의 승/무/패와 순위를 관리하는 싱글톤.
    ///
    /// 범위 한정: 마스터 GDD가 "경기(Match)"를 "유저의 로스터와 AI 팀 간의 대결"로 정의하고 있어(AI 팀 간의
    /// 대결은 게임 정의에 없음), 이 매니저는 유저 팀이 참여하는 144경기만 MatchEngine으로 실제 시뮬레이션한다.
    /// 따라서 AI 팀들의 순위표 성적은 유저와 치른 경기 결과만 반영하며, AI 팀 간의 가상 대결까지 포함한
    /// "완전한" 10팀 리그 시뮬레이션은 아니다.
    /// </summary>
    public class LeagueManager : MonoBehaviour
    {
        public static LeagueManager Instance { get; private set; }

        public const int OpponentCount = 9;
        public const int GamesPerOpponent = 16;                              // 9 x 16 = 144
        public const int TotalUserGames = OpponentCount * GamesPerOpponent;  // 144
        public const int RegularOpenGames = 72;                              // 1~72경기
        // 73~144경기 = RegularLockedGames (TotalUserGames - RegularOpenGames)

        // GDD 4절: 정규시즌 순위 5위 이내면 포스트시즌(가을야구) 진출.
        public const int PlayoffQualifyingRank = 5;

        // 상대 팀 1개당 시리즈 구성: 3연전 x 5 + 단판 1경기 = 16경기. (144를 9개 상대에게 균등 배분하기 위한 근사치.
        // 실제 KBO는 3연전 위주에 일부 다른 길이의 시리즈를 섞어 16경기를 맞춘다 - 여기서는 그 스타일을 단순화했다.)
        private static readonly int[] SeriesLengths = { 3, 3, 3, 3, 3, 1 };

        [Header("User Team")]
        [SerializeField] private Team userTeam = Team.Doosan;

        [Header("Match Simulation")]
        [Tooltip("MatchEngine에 전달할 SkillDB (선택 사항)")]
        [SerializeField] private SkillDB skillDB;
        [Tooltip("MatchEngine에 전달할 EngineConfig (선택 사항, 없으면 코드 기본값 사용)")]
        [SerializeField] private EngineConfig engineConfig;

        [Header("AI Roster Generation")]
        [Tooltip("있으면 AI 로스터 생성 시 이 DB에 등록된 실제 PlayerTemplate을 우선 사용한다. " +
                 "없거나 특정 포지션/롤에 해당 구단 템플릿이 부족하면 절차적(더미) 템플릿으로 대체한다.")]
        [SerializeField] private PlayerDatabase playerDatabase;
        [Tooltip("더미 스탯 생성 시 목표 OVR 대비 허용하는 무작위 편차(±)")]
        [SerializeField] private int aiOvrVarianceRange = 10;

        private const int DefaultAiBaseStatLevel = 65; // 유저 로스터가 비어 있을 때 쓰는 기본 스탯 수준

        private readonly Dictionary<Team, TeamInfo> standings = new Dictionary<Team, TeamInfo>();
        private readonly List<MatchFixture> schedule = new List<MatchFixture>();
        private int nextFixtureIndex;

        public Team UserTeam => userTeam;
        public IReadOnlyList<MatchFixture> Schedule => schedule;

        /// <summary>지금까지 진행된(결과가 기록된) 경기 수. SaveManager가 저장/복원 지점으로 사용한다.</summary>
        public int PlayedGameCount => nextFixtureIndex;

        /// <summary>현재 리그 진행 단계. 144경기가 모두 끝나면 POST_PREP(포스트시즌 진출) 또는
        /// STOVE_LEAGUE(시즌 종료)로 자동 전환된다.</summary>
        public LeaguePhase CurrentPhase { get; private set; } = LeaguePhase.STOVE_LEAGUE;

        /// <summary>정규시즌 144경기를 모두 마친 뒤의 유저 팀 최종 순위(1위=1). 시즌 진행 중이거나 시작 전이면 null.</summary>
        public int? UserFinalRank { get; private set; }

        /// <summary>UserFinalRank가 PlayoffQualifyingRank(5위) 이내인지.</summary>
        public bool IsUserPlayoffEligible => UserFinalRank.HasValue && UserFinalRank.Value <= PlayoffQualifyingRank;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// LeagueCalendar.Instance는 둘 다 DontDestroyOnLoad 싱글톤이라 Awake() 호출 순서가 보장되지
        /// 않는다 - Start()에서 구독해야, "모든 오브젝트의 Awake()가 끝난 뒤에야 Start()가 호출된다"는
        /// 유니티의 보장에 기대어 LeagueCalendar.Instance가 항상 준비된 상태로 구독을 걸 수 있다.
        /// </summary>
        private void Start()
        {
            if (LeagueCalendar.Instance != null)
            {
                LeagueCalendar.Instance.OnDayAdvanced += HandleDayAdvanced;
            }
        }

        private void OnDestroy()
        {
            if (LeagueCalendar.Instance != null)
            {
                LeagueCalendar.Instance.OnDayAdvanced -= HandleDayAdvanced;
            }
        }

        /// <summary>
        /// 유저 팀 + KBO 나머지 9개 구단으로 리그를 초기화하고 유저의 144경기 스케줄을 생성한다.
        /// 유저 팀의 로스터는 GameManager.Roster를 그대로 참조한다(스냅샷 아님 - 매 경기 시뮬레이션 시점의 현재 로스터를 사용).
        /// </summary>
        public void InitializeLeague(Team userTeamValue)
        {
            userTeam = userTeamValue;
            standings.Clear();
            schedule.Clear();
            nextFixtureIndex = 0;
            CurrentPhase = LeaguePhase.REGULAR_OPEN;
            UserFinalRank = null;

            foreach (Team team in Enum.GetValues(typeof(Team)))
            {
                if (team == Team.None) continue;
                standings[team] = new TeamInfo { Team = team, IsUserTeam = team == userTeam };
            }

            BuildUserSchedule();
            GenerateAiRosters();
            LeagueCalendar.Instance?.InitializeSeason(DateTime.Now.Year);
        }

        /// <summary>
        /// AI 팀(또는 유저 팀)의 28인 로스터를 수동으로 덮어쓴다. InitializeLeague()가 자동으로
        /// GenerateAiRosters()를 호출해 9개 AI 팀을 채우므로 보통 직접 호출할 필요는 없다 - 특정 팀의
        /// 로스터를 수동으로 커스터마이징하고 싶을 때만 사용한다. 유저 팀은 매 경기 시뮬레이션 시점에
        /// GameManager.Roster를 직접 참조하므로 이 메서드로 설정해도 실제 경기에는 반영되지 않는다.
        /// </summary>
        public void SetTeamRoster(Team team, List<Player> roster)
        {
            if (standings.TryGetValue(team, out var info))
            {
                info.Roster = roster ?? new List<Player>();
            }
        }

        /// <summary>
        /// 9개 AI 팀 전체의 28인(타자 15 + 투수 13) 로스터를 자동 생성한다.
        /// 유저 팀 로스터의 평균 OVR을 목표치로 삼아 ±aiOvrVarianceRange 범위에서 편차를 준 더미 스탯으로
        /// 채우므로, 유저 로스터가 강해지면 AI들도 대략 비슷한 수준으로 스케일링된다.
        /// playerDatabase에 해당 구단·포지션/롤의 실제 PlayerTemplate이 있으면 그것을 우선 사용한다.
        /// </summary>
        public void GenerateAiRosters()
        {
            int targetStatLevel = EstimateTargetStatLevel();

            foreach (var team in standings.Keys.Where(t => t != userTeam).ToList())
            {
                standings[team].Roster = GenerateTeamRoster(team, targetStatLevel);
            }
        }

        private int EstimateTargetStatLevel()
        {
            if (GameManager.Instance == null || GameManager.Instance.Roster.Count == 0)
            {
                return DefaultAiBaseStatLevel;
            }

            // Player.CalculateOVR()은 이미 세부 스탯의 평균이므로, 그 평균을 그대로 AI의 목표 스탯 수준으로 쓴다.
            return Mathf.RoundToInt((float)GameManager.Instance.Roster.Average(p => p.CalculateOVR(false)));
        }

        private static readonly BatterPosition[] AllBatterPositions = (BatterPosition[])Enum.GetValues(typeof(BatterPosition));
        private const int AiBenchBatterCount = 6;

        // RosterManager와 동일한 투수 13명 배분(선발5/승리조2/추격조4/롱릴리프1/마무리1).
        private static readonly (PitcherRole role, int count)[] AiPitcherRoleQuota =
        {
            (PitcherRole.StartingPitcher, 5),
            (PitcherRole.WinningReliever, 2),
            (PitcherRole.MopUpReliever, 4),
            (PitcherRole.LongReliever, 1),
            (PitcherRole.Closer, 1),
        };

        private List<Player> GenerateTeamRoster(Team team, int targetStatLevel)
        {
            var roster = new List<Player>(GameManager.RequiredRosterSize);

            foreach (var position in AllBatterPositions)
            {
                roster.Add(CreateAiPlayer(team, false, position, null, targetStatLevel));
            }

            for (int i = 0; i < AiBenchBatterCount; i++)
            {
                // 벤치는 포지션 편중 없이 무작위 포지션으로 채운다 (단순화)
                var randomPosition = AllBatterPositions[UnityEngine.Random.Range(0, AllBatterPositions.Length)];
                roster.Add(CreateAiPlayer(team, false, randomPosition, null, targetStatLevel));
            }

            foreach (var (role, count) in AiPitcherRoleQuota)
            {
                for (int i = 0; i < count; i++)
                {
                    roster.Add(CreateAiPlayer(team, true, null, role, targetStatLevel));
                }
            }

            return roster;
        }

        private Player CreateAiPlayer(Team team, bool isPitcher, BatterPosition? batterPosition, PitcherRole? pitcherRole, int targetStatLevel)
        {
            var template = FindDatabaseTemplate(team, isPitcher, batterPosition, pitcherRole)
                ?? CreateProceduralTemplate(team, isPitcher, batterPosition, pitcherRole, targetStatLevel);

            return new Player(Guid.NewGuid().ToString(), template);
        }

        /// <summary>
        /// playerDatabase에 등록된 실제 카드 중 조건에 맞는 첫 항목을 사용한다.
        /// TODO: 같은 템플릿이 여러 슬롯에 중복 배정될 수 있다(예: 벤치 두 자리가 같은 카드) - 후속 과제.
        /// </summary>
        private PlayerTemplate FindDatabaseTemplate(Team team, bool isPitcher, BatterPosition? batterPosition, PitcherRole? pitcherRole)
        {
            if (playerDatabase == null) return null;

            return playerDatabase.AllTemplates.FirstOrDefault(t =>
                t.Team == team && t.IsPitcher == isPitcher &&
                (isPitcher ? t.PitcherRole == pitcherRole : t.BatterPosition == batterPosition));
        }

        /// <summary>
        /// 실제 카드가 없을 때 즉석에서 만드는 더미 템플릿(.asset으로 저장되지 않는 런타임 전용 인스턴스).
        /// 4개(투수) 또는 3개(타자) 세부 스탯을 모두 targetStatLevel ± aiOvrVarianceRange로 동일하게 채운다.
        /// </summary>
        private PlayerTemplate CreateProceduralTemplate(Team team, bool isPitcher, BatterPosition? batterPosition,
            PitcherRole? pitcherRole, int targetStatLevel)
        {
            var template = ScriptableObject.CreateInstance<PlayerTemplate>();

            string roleLabel = isPitcher ? pitcherRole.ToString() : batterPosition.ToString();
            template.TemplateId = $"AI_{team}_{roleLabel}_{Guid.NewGuid():N}";
            template.RealPlayerId = template.TemplateId;
            template.PlayerName = $"{team} AI ({roleLabel})";
            template.Team = team;
            template.Grade = Grade.LIVE_NORMAL;
            template.IsPitcher = isPitcher;
            template.Cost = 1;

            if (isPitcher)
            {
                template.PitcherRole = pitcherRole ?? PitcherRole.StartingPitcher;
            }
            else
            {
                template.BatterPosition = batterPosition ?? BatterPosition.DesignatedHitter;
            }

            int variance = UnityEngine.Random.Range(-aiOvrVarianceRange, aiOvrVarianceRange + 1);
            int statLevel = Mathf.Max(10, targetStatLevel + variance);

            if (isPitcher)
            {
                template.PitcherStats = new PitcherStats(statLevel, statLevel, statLevel, statLevel);
            }
            else
            {
                template.BatterStats = new BatterStats(statLevel, statLevel, statLevel);
            }

            return template;
        }

        /// <summary>스케줄 전체(144경기)를 끝까지 시뮬레이션한다. 시즌이 에러 없이 완주되는지 확인하는 용도로도 쓸 수 있다.</summary>
        public void PlaySeasonToCompletion() => PlayUntil(TotalUserGames);

        public TeamInfo GetTeamInfo(Team team) => standings.TryGetValue(team, out var info) ? info : null;

        /// <summary>승률(무승부 제외) 내림차순, 동률 시 승수 내림차순으로 정렬한 순위표.</summary>
        public List<TeamInfo> GetStandings()
        {
            return standings.Values
                .OrderByDescending(t => t.WinRate)
                .ThenByDescending(t => t.Wins)
                .ToList();
        }

        /// <summary>
        /// 유저 팀 기준 144경기 스케줄을 생성한다. 9개 상대 팀과 각각 16경기(3연전 x 5 + 단판 1경기)씩 만나며,
        /// 같은 상대와의 시리즈가 연속으로 몰리지 않도록 상대팀 순서를 라운드로빈으로 섞는다.
        /// </summary>
        private void BuildUserSchedule()
        {
            var opponents = standings.Keys.Where(t => t != userTeam).ToList(); // 9개 팀
            var seriesIndex = new int[opponents.Count];
            int gameNumber = 0;
            bool anyLeft = true;

            while (anyLeft)
            {
                anyLeft = false;
                for (int i = 0; i < opponents.Count; i++)
                {
                    if (seriesIndex[i] >= SeriesLengths.Length) continue;
                    anyLeft = true;

                    int length = SeriesLengths[seriesIndex[i]];
                    bool userIsHome = seriesIndex[i] % 2 == 0; // 시리즈 인덱스가 짝수면 유저 홈, 홀수면 원정
                    var opponent = opponents[i];
                    seriesIndex[i]++;

                    for (int g = 0; g < length; g++)
                    {
                        gameNumber++;
                        schedule.Add(new MatchFixture
                        {
                            GameNumber = gameNumber,
                            HomeTeam = userIsHome ? userTeam : opponent,
                            AwayTeam = userIsHome ? opponent : userTeam,
                            Phase = gameNumber <= RegularOpenGames ? LeaguePhase.REGULAR_OPEN : LeaguePhase.REGULAR_LOCKED,
                        });
                    }
                }
            }
        }

        /// <summary>스케줄의 다음 미진행 경기를 조회만 한다(진행 포인터를 소모하지 않음). 더 없으면 null.
        /// PlayBallController처럼 외부에서 직접 경기를 진행할 주체가 "이번엔 누구와 붙는지" 미리 알아야 할 때 쓴다.</summary>
        public MatchFixture PeekNextFixture() => nextFixtureIndex < schedule.Count ? schedule[nextFixtureIndex] : null;

        /// <summary>team의 로스터를 반환한다. userTeam이면 GameManager.Roster를 실시간으로, 아니면 저장된 AI 로스터를 반환한다.</summary>
        public List<Player> ResolveRosterForTeam(Team team)
        {
            if (team == userTeam && GameManager.Instance != null)
            {
                return GameManager.Instance.Roster.ToList();
            }

            return standings.TryGetValue(team, out var info) ? info.Roster : new List<Player>();
        }

        /// <summary>스케줄의 다음 미진행 경기를 자체 MatchEngine으로 즉시 시뮬레이션하고 결과를 기록한다. (UI 없는 빠른 진행/시즌 스킵용)</summary>
        public MatchFixture PlayNextMatch()
        {
            var fixture = PeekNextFixture();
            if (fixture == null) return null;

            SimulateFixture(fixture);
            FinalizeFixtureBookkeeping(fixture);

            return fixture;
        }

        /// <summary>
        /// PeekNextFixture()가 가리키는 경기를 외부(PlayBallController 등)에서 이미 자체 MatchEngine으로
        /// 진행했을 때, 그 결과를 리그 기록(승/무/패, 다음 경기 포인터, 시즌 단계 전환)에 반영한다.
        /// LeagueManager 자신은 시뮬레이션을 다시 수행하지 않는다(중복 시뮬레이션 방지).
        /// </summary>
        public void CompleteNextFixture(MatchResult result)
        {
            var fixture = PeekNextFixture();
            if (fixture == null || result == null) return;

            fixture.Result = result;
            fixture.IsPlayed = true;
            FinalizeFixtureBookkeeping(fixture);
        }

        /// <summary>
        /// 다음 경기 포인터를 전진시키고 승/무/패를 기록한 뒤, 72경기 지점에서 REGULAR_OPEN ->
        /// REGULAR_LOCKED로 전환하며, 144경기(전체 스케줄)가 끝나면 GDD 5절 규칙(5위 이내 = 포스트시즌
        /// 진출)에 따라 POST_PREP 또는 STOVE_LEAGUE로 확정한다.
        /// </summary>
        private void FinalizeFixtureBookkeeping(MatchFixture fixture)
        {
            nextFixtureIndex++;
            RecordResult(standings[fixture.HomeTeam], standings[fixture.AwayTeam], fixture.Result);

            // 체력 회복은 더 이상 "경기 1건이 끝났다"는 사실에 직접 반응하지 않는다 - LeagueCalendar가
            // 날짜를 넘기고, 그 결과로 발생하는 OnDayAdvanced 이벤트(HandleDayAdvanced)가 회복/컨디션
            // 갱신을 담당한다. 여기서는 오직 "경기 1건 = 하루 경과"를 캘린더에 통지만 한다.
            LeagueCalendar.Instance?.AdvanceToNextGameDay();

            if (fixture.GameNumber == RegularOpenGames && CurrentPhase == LeaguePhase.REGULAR_OPEN)
            {
                CurrentPhase = LeaguePhase.REGULAR_LOCKED;
            }

            if (fixture.GameNumber == TotalUserGames)
            {
                FinalizeSeason();
            }
        }

        // 하루가 지날 때마다 회복되는 체력(평일 기준). GDD 미명시 - StaminaCostPerBatterFaced(4)로
        // 소모된 체력을 다음 경기까지 대략 회복하도록 역산한 임시값이다. BatchSimulator가 동일 상수를
        // 참조해 밸런스를 검증하므로 public으로 노출한다.
        public const int StarterStaminaRecoveryPerDay = 25;
        public const int BullpenStaminaRecoveryPerDay = 15;
        // 월요일(휴식일)을 건너뛴 날에는 평소 회복량 위에 이만큼을 추가로 더 회복한다 - "대량 회복".
        public const int RestDayRecoveryBonus = 50;

        // 하루가 지날 때 선수 1명의 컨디션이 실제로(오르든 내리든) 변할 확률. GDD 미명시 - 매일 절반
        // 정도는 변화가 있어야 "컨디션을 관리하는 재미"가 체감된다고 보고 잡은 임시값.
        private const float ConditionChangeChance = 0.5f;

        /// <summary>
        /// LeagueCalendar.OnDayAdvanced 핸들러. 날짜가 넘어갈 때마다(경기 1건 완료 = 보통 하루,
        /// 월요일을 건너뛴 경우 passedRestDay=true) 리그 전체 10개 구단 로스터의 체력 회복과 컨디션
        /// 갱신을 함께 처리한다. 유저 팀은 standings[userTeam].Roster가 아니라
        /// GameManager.Instance.Roster가 실제 로스터이므로(GenerateAiRosters가 유저 팀은 건너뛰어
        /// standings 쪽은 항상 비어 있다) 별도로 처리한다.
        /// </summary>
        private void HandleDayAdvanced(DateTime newDate, bool passedRestDay)
        {
            if (GameManager.Instance != null)
            {
                RecoverRosterStamina(GameManager.Instance.Roster, passedRestDay);
                UpdateRosterConditions(GameManager.Instance.Roster);
            }

            foreach (var info in standings.Values)
            {
                if (info.IsUserTeam) continue; // 유저 로스터는 위에서 이미 처리함
                RecoverRosterStamina(info.Roster, passedRestDay);
                UpdateRosterConditions(info.Roster);
            }
        }

        private static void RecoverRosterStamina(IReadOnlyList<Player> roster, bool restDayBonus)
        {
            if (roster == null) return;

            foreach (var player in roster)
            {
                if (player?.Template == null || !player.Template.IsPitcher) continue;

                int recoveryAmount = player.Template.PitcherRole == PitcherRole.StartingPitcher
                    ? StarterStaminaRecoveryPerDay
                    : BullpenStaminaRecoveryPerDay;
                if (restDayBonus) recoveryAmount += RestDayRecoveryBonus;

                player.RecoverStamina(recoveryAmount);
            }
        }

        /// <summary>
        /// 로스터의 모든 선수(타자+투수) 컨디션을 확률적으로 갱신한다. Player.ShiftCondition()이 이미
        /// 인접 단계로만 이동시키므로, 여기서는 "이번에 변화가 있는지"와 "오를지 내릴지"만 결정한다.
        /// </summary>
        private static void UpdateRosterConditions(IReadOnlyList<Player> roster)
        {
            if (roster == null) return;

            foreach (var player in roster)
            {
                if (player == null) continue;
                if (UnityEngine.Random.value > ConditionChangeChance) continue; // 이번엔 변화 없음

                player.ShiftCondition(UnityEngine.Random.value < 0.5f);
            }
        }

        /// <summary>144경기(정규시즌)가 모두 끝났을 때 발생한다. PostSeasonManager가 이 이벤트를 구독해
        /// IsUserPlayoffEligible이면 포스트시즌 브래킷을 시작하고, 아니면 아무 것도 하지 않는다
        /// (그 경우 CurrentPhase는 이미 STOVE_LEAGUE로 확정돼 있다).</summary>
        public event Action OnSeasonFinalized;

        private void FinalizeSeason()
        {
            var finalStandings = GetStandings();
            int rank = finalStandings.FindIndex(t => t.Team == userTeam) + 1; // 1-based, 못 찾으면 0
            UserFinalRank = rank > 0 ? rank : (int?)null;

            CurrentPhase = IsUserPlayoffEligible ? LeaguePhase.POST_PREP : LeaguePhase.STOVE_LEAGUE;
            OnSeasonFinalized?.Invoke();
        }

        /// <summary>PostSeasonManager.BeginPostSeason()이 브래킷을 시작할 때 호출해 단계를 POST_SEASON으로 넘긴다.</summary>
        public void EnterPostSeason() => CurrentPhase = LeaguePhase.POST_SEASON;

        /// <summary>PostSeasonManager가 한국시리즈까지 마쳤을 때(또는 유저가 포스트시즌에 진출하지 못해
        /// 곧바로) 호출해 스토브리그 단계로 넘긴다.</summary>
        public void EnterStoveLeague() => CurrentPhase = LeaguePhase.STOVE_LEAGUE;

        /// <summary>
        /// 스토브리그 처리(AI 로스터 자동 성장)까지 마친 뒤 다음 시즌을 개막한다. POST_PREP(포스트시즌
        /// 대기) 또는 STOVE_LEAGUE(포스트시즌 탈락) 단계, 즉 FinalizeSeason()이 이미 호출된 뒤에만
        /// 의미가 있다. UI(LeagueDashboardUIController 등)가 "다음 시즌 시작" 버튼에 연결하는 진입점이다.
        ///
        /// InitializeLeague()와 달리 AI 로스터를 처음부터 다시 생성하지 않는다 - GenerateAiRosters()를
        /// 다시 부르면 매번 새 Player 인스턴스를 절차적으로 찍어내므로, 방금 StoveLeagueManager로 키워둔
        /// ReinforceLevel/스킬이 통째로 사라져 버린다. 대신 기존 TeamInfo.Roster(9개 AI 팀)는 그대로 둔
        /// 채 StoveLeagueManager로 성장(강화/스킬) + 세대교체(최하위 은퇴 -> 유저 평균 OVR에 러버밴딩된
        /// 신인 영입)시키고, 승/무/패 기록과 스케줄만 새 시즌 기준으로 초기화한다.
        ///
        /// CreateAiPlayer를 메서드 그룹으로 그대로 넘겨 StoveLeagueManager가 신인을 생성할 때도 초기
        /// AI 로스터 생성과 동일한 경로(PlayerDatabase 실카드 우선, 없으면 procedural)를 타도록 한다.
        /// </summary>
        public StoveLeagueManager.StoveLeagueReport AdvanceToNextSeason()
        {
            var aiTeams = standings.Values.Where(t => !t.IsUserTeam).ToList();
            int userAverageOvr = EstimateTargetStatLevel();
            var report = StoveLeagueManager.ProcessStoveLeague(aiTeams, skillDB, userAverageOvr, CreateAiPlayer);

            foreach (var info in standings.Values)
            {
                info.Wins = 0;
                info.Draws = 0;
                info.Losses = 0;
            }

            schedule.Clear();
            nextFixtureIndex = 0;
            UserFinalRank = null;
            CurrentPhase = LeaguePhase.REGULAR_OPEN;

            BuildUserSchedule();

            return report;
        }

        /// <summary>지정한 GameNumber까지(포함) 남은 스케줄을 한 번에 시뮬레이션한다. (빠른 진행용)</summary>
        public void PlayUntil(int gameNumber)
        {
            while (nextFixtureIndex < schedule.Count && schedule[nextFixtureIndex].GameNumber <= gameNumber)
            {
                PlayNextMatch();
            }
        }

        /// <summary>
        /// PlayFullMatch()를 그대로 호출하지 않고 BeginMatch() + PlayNextAtBat() 반복으로 직접 풀어서
        /// 진행한다 - 결과(MatchResult)만 나오는 PlayFullMatch()와 달리, 타석 단위 AtBatStepResult를
        /// SeasonStatManager.RecordAtBat()에 그때그때 넘겨야 헤드리스 스킵 중에도 시즌 기록(타율/방어율
        /// 등)이 정상적으로 누적된다. PlayBallController를 거치지 않으므로 OnAtBatEnd/OnMatchCompleted
        /// 같은 UI용 이벤트는 전혀 발생하지 않는다 - 애니메이션/연출과는 처음부터 완전히 분리된 경로다.
        /// </summary>
        private void SimulateFixture(MatchFixture fixture)
        {
            var homeRoster = ResolveRosterForTeam(fixture.HomeTeam);
            var awayRoster = ResolveRosterForTeam(fixture.AwayTeam);

            var homeModifiers = BuildTeamPowerModifiers(fixture.HomeTeam, homeRoster, isHome: true);
            var awayModifiers = BuildTeamPowerModifiers(fixture.AwayTeam, awayRoster, isHome: false);
            var engine = new MatchEngine(homeRoster, awayRoster, homeModifiers, awayModifiers, skillDB, engineConfig);
            bool isPostSeason = fixture.Phase == LeaguePhase.POST_SEASON;

            engine.BeginMatch(fixture.HomeTeam.ToString(), fixture.AwayTeam.ToString(), isPostSeason);
            while (!engine.IsGameOver)
            {
                var step = engine.PlayNextAtBat();
                SeasonStatManager.Instance?.RecordAtBat(step, fixture.HomeTeam, fixture.AwayTeam);
            }

            fixture.Result = engine.Result;
            fixture.IsPlayed = true;
            SeasonStatManager.Instance?.RecordMatchCompleted(fixture.Result);
        }

        /// <summary>
        /// [TASK-KBO-037] 경기 시작 전 MatchEngine에 주입할 "경기 적용 전력" 보정치를 구성한다.
        /// team이 유저 팀(userTeam)이면 GameManager.Instance.FavoriteTeam을 기준으로, 그 외(AI 팀)는
        /// favoriteTeam 없이(null) GameManager.CalculateSynergy()를 호출해 로스터 내 최다 구단 기준으로
        /// 판정한다.
        /// [TASK-KBO-039] ConditionBuff는 홈팀이면 TeamPowerModifiers.HomeAdvantageConditionBuff(+2),
        /// 원정팀이면 0이다.
        /// [TASK-KBO-048] 치어리더 효과(ConditionBuff 추가 가산 + ClutchMultiplier)는 "유저 팀이면서
        /// 홈경기"(isUserTeamHome)일 때만 합산된다 - AI 팀이거나 유저 팀이라도 원정 경기면 치어리더의
        /// 어떠한 버프도 적용되지 않는다(요구사항 6항). 정규 시즌은 홈/원정이 고정 대진표(BuildUserSchedule())
        /// 로 명확히 갈리므로 중립 구장 개념이 없다 - isHome 판별에 모호함이 없다.
        /// </summary>
        private TeamPowerModifiers BuildTeamPowerModifiers(Team team, List<Player> roster, bool isHome)
        {
            bool isUserTeam = team == userTeam;
            bool isUserTeamWithFavorite = isUserTeam && GameManager.Instance != null
                && GameManager.Instance.FavoriteTeam != Team.None;
            string favoriteTeam = isUserTeamWithFavorite ? GameManager.Instance.FavoriteTeam.ToString() : null;

            int synergy = GameManager.CalculateSynergy(roster, favoriteTeam);

            bool isUserTeamHome = isUserTeam && isHome;
            var equippedCheerleader = GameManager.Instance?.EquippedCheerleader;
            int conditionBuff = (isHome ? TeamPowerModifiers.HomeAdvantageConditionBuff : 0)
                + GameManager.ResolveCheerleaderConditionBuff(isUserTeamHome, equippedCheerleader);
            float clutchMultiplier = GameManager.ResolveCheerleaderClutchMultiplier(isUserTeamHome, equippedCheerleader);

            return new TeamPowerModifiers(synergy, conditionBuff, clutchMultiplier);
        }

        /// <summary>
        /// SaveManager 전용 복원 진입점. 리그를 새로 초기화(스케줄/로스터 재생성)한 뒤 저장된 누적
        /// 승/무/패, 진행 지점(PlayedGameCount), 시즌 단계로 덮어쓴다.
        /// 주의: 과거에 치른 개별 경기의 MatchResult(이닝별 스코어 등 boxscore)는 복원하지 않는다 -
        /// 재시작 시 AI 로스터도 (PlayerDatabase 미보유분은) 절차적으로 새로 생성되므로 완전히 동일하지 않을 수 있다.
        /// </summary>
        public void RestoreFromSave(Team savedUserTeam, int playedGameCount, LeaguePhase phase,
            int userFinalRankOrNegativeOne, IEnumerable<(Team team, int wins, int draws, int losses)> standingsData)
        {
            InitializeLeague(savedUserTeam);

            if (standingsData != null)
            {
                foreach (var saved in standingsData)
                {
                    if (standings.TryGetValue(saved.team, out var info))
                    {
                        info.Wins = saved.wins;
                        info.Draws = saved.draws;
                        info.Losses = saved.losses;
                    }
                }
            }

            nextFixtureIndex = Mathf.Clamp(playedGameCount, 0, schedule.Count);
            CurrentPhase = phase;
            UserFinalRank = userFinalRankOrNegativeOne >= 0 ? userFinalRankOrNegativeOne : (int?)null;
        }

        private static void RecordResult(TeamInfo home, TeamInfo away, MatchResult result)
        {
            if (result.WinnerTeamName == null)
            {
                home.Draws++;
                away.Draws++;
            }
            else if (result.WinnerTeamName == home.Team.ToString())
            {
                home.Wins++;
                away.Losses++;
            }
            else
            {
                away.Wins++;
                home.Losses++;
            }
        }
    }
}
