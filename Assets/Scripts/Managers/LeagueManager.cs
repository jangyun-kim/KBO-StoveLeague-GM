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

        /// <summary>스케줄의 다음 미진행 경기를 시뮬레이션하고 결과를 기록한다. 더 진행할 경기가 없으면 null.</summary>
        public MatchFixture PlayNextMatch()
        {
            if (nextFixtureIndex >= schedule.Count) return null;

            var fixture = schedule[nextFixtureIndex];
            nextFixtureIndex++;
            SimulateFixture(fixture);
            UpdatePhaseAfterFixture(fixture);

            return fixture;
        }

        /// <summary>
        /// 72경기 지점에서 REGULAR_OPEN -> REGULAR_LOCKED로 전환하고, 144경기(전체 스케줄)가 끝나면
        /// GDD 5절 규칙(5위 이내 = 포스트시즌 진출)에 따라 POST_PREP 또는 STOVE_LEAGUE로 확정한다.
        /// </summary>
        private void UpdatePhaseAfterFixture(MatchFixture fixture)
        {
            if (fixture.GameNumber == RegularOpenGames && CurrentPhase == LeaguePhase.REGULAR_OPEN)
            {
                CurrentPhase = LeaguePhase.REGULAR_LOCKED;
            }

            if (fixture.GameNumber == TotalUserGames)
            {
                FinalizeSeason();
            }
        }

        private void FinalizeSeason()
        {
            var finalStandings = GetStandings();
            int rank = finalStandings.FindIndex(t => t.Team == userTeam) + 1; // 1-based, 못 찾으면 0
            UserFinalRank = rank > 0 ? rank : (int?)null;

            CurrentPhase = IsUserPlayoffEligible ? LeaguePhase.POST_PREP : LeaguePhase.STOVE_LEAGUE;
        }

        /// <summary>지정한 GameNumber까지(포함) 남은 스케줄을 한 번에 시뮬레이션한다. (빠른 진행용)</summary>
        public void PlayUntil(int gameNumber)
        {
            while (nextFixtureIndex < schedule.Count && schedule[nextFixtureIndex].GameNumber <= gameNumber)
            {
                PlayNextMatch();
            }
        }

        private void SimulateFixture(MatchFixture fixture)
        {
            var home = standings[fixture.HomeTeam];
            var away = standings[fixture.AwayTeam];

            var homeRoster = fixture.HomeTeam == userTeam && GameManager.Instance != null
                ? GameManager.Instance.Roster.ToList()
                : home.Roster;
            var awayRoster = fixture.AwayTeam == userTeam && GameManager.Instance != null
                ? GameManager.Instance.Roster.ToList()
                : away.Roster;

            var engine = new MatchEngine(skillDB, engineConfig);
            bool isPostSeason = fixture.Phase == LeaguePhase.POST_SEASON;
            var result = engine.PlayFullMatch(homeRoster, awayRoster, fixture.HomeTeam.ToString(), fixture.AwayTeam.ToString(), isPostSeason);

            fixture.Result = result;
            fixture.IsPlayed = true;

            RecordResult(home, away, result);
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
