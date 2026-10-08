using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Simulation;
using UnityEngine;

namespace KBOManager.Services
{
    /// <summary>[TASK-GM-01] 한 구단의 단장 모드 상태(28인 로스터 · 재정 · 치어리더 풀/엔트리 · 스토리 플래그).</summary>
    public class GMTeamState
    {
        public string TeamCode;
        public Team Team;
        public string DisplayName;
        public bool IsUserTeam;

        public readonly List<Player> Roster = new List<Player>();
        /// <summary>[TASK-GM-08] 퓨처스 핵심 유망주 풀(10~15명, 실제 선수) - 경기에는 나서지 않고 콜업 · 보호 명단 대상이다.</summary>
        public readonly List<Player> Futures = new List<Player>();
        /// <summary>[TASK-GM-08] 익명 육성 슬롯 인원(개별 선수 데이터 없음).</summary>
        public int DevelopmentSlots = GMRosterTiers.DefaultDevelopmentSlots;
        /// <summary>[TASK-GM-08] 보류선수 전체(1군 + 퓨처스) - FA 보상 보호 명단 대상.</summary>
        public IEnumerable<Player> ReservePlayers => Roster.Concat(Futures);
        public IEnumerable<Player> Batters => Roster.Where(p => !p.IsPitcher);
        public IEnumerable<Player> Pitchers => Roster.Where(p => p.IsPitcher);

        // 재정(단위: 만 원)
        public long Budget;
        public int PayrollCap;
        public int Payroll => Roster.Sum(p => p.Salary);

        // 치어리더 - 구단 풀 최대 15명, 경기 엔트리 4~6명(기본 5명)
        public readonly List<Cheerleader> CheerleaderPool = new List<Cheerleader>();
        public int CheerEntrySize = GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_DEFAULT;
        public IEnumerable<Cheerleader> CheerEntry => CheerleaderPool.Take(CheerEntrySize);
        /// <summary>[TASK-GM-05] 엔트리 ① 단장 리더십(체력 효율 반영) → 팀워크 +1~+12.</summary>
        public int CheerLeadershipBuff => GMCheerleaderRoster.LeadershipTeamworkBonus(CheerEntry);
        /// <summary>[TASK-GM-05] 체력 30 미만 인원이 생기면 같은 인원수로 [최적 컨디션 자동 편성].
        /// [TASK-GM-06] GM-05 D.2 - 내 구단도 기본 켬(GMCheerleaderRoster.AutoRotateUserCheerleaders), 단장이 화면에서 끄고 수동 편성할 수 있다.</summary>
        public bool CheerAutoRotate = true;
        /// <summary>[TASK-GM-06] 지시서 표기 - 내 구단 자동 로테이션(= CheerAutoRotate).</summary>
        public bool AutoRotateUserCheerleaders { get => CheerAutoRotate; set => CheerAutoRotate = value; }
        /// <summary>[TASK-GM-06] 스토리 안건 · 단장 결단으로 생긴 팀워크 가감(-20 ~ +20). 응원단 리더십과 함께 TeamChemistryEngine에 더해진다.</summary>
        public int AgendaTeamworkBonus;
        /// <summary>[TASK-GM-11] 선수단의 단장 신뢰도 0~100(기본 60) - 구단주 신임도(OwnerTrust)와 별개. 협상 진행 가능성 베이스라인 · 약속 위반 페널티(2단계) 대상.</summary>
        public int LockerRoomTrust = DefaultLockerRoomTrust;
        public const int DefaultLockerRoomTrust = 60;
        /// <summary>[TASK-GM-07] 단장 모드 연도 시너지 - 같은 시즌 · 같은 구단 출신 동료 그룹 팀워크 가산(0~+8, GMYearSynergy).</summary>
        public int YearSynergyBonus => GMYearSynergy.TeamworkBonus(AvailableRoster);
        /// <summary>[TASK-GM-06] 팀워크 가산 합계 = 응원단 단장 리더십 + 스토리 안건 보정 + [TASK-GM-07] 연도 시너지.</summary>
        public int TeamworkBuff => CheerLeadershipBuff + AgendaTeamworkBonus + YearSynergyBonus;
        /// <summary>[TASK-GM-05] 전담 응원 매칭(최대 2쌍).</summary>
        public readonly List<GMCheerDedication> CheerDedications = new List<GMCheerDedication>();
        /// <summary>[TASK-GM-05] 홈 흥행 누적 관중 수익(팬 지지율 환산 잔여분, 만 원).</summary>
        public int CheerFanPoints;

        // 스토리 캠페인 「꼴찌 구단의 겨울」
        public int ConsecutiveLastPlaceSeasons;
        public bool OwnerPostseasonPressure;
        public string TradeRequestPlayerId; // 트레이드를 요구한 선수(RealPlayerId)

        // [TASK-GM-04] 구단 팬 지지율(0~100) - 내 구단 선수 월간 수상 시 상승
        public int FanSupport = GMTeamFan.DefaultSupport;

        // [TASK-GM-02] 단장 지정 라인업(부상 시 [라인업 직접 관리] 대체 선수 핀) - 경기 엔진 타순에 반영된다.
        public LineupAssignment Lineup = new LineupAssignment();
        /// <summary>[TASK-GM-02] 부상자를 뺀 출전 가능 선수.</summary>
        public List<Player> AvailableRoster => Roster.Where(p => p.InjuryRemainingDays <= 0).ToList();
    }

    /// <summary>[TASK-GM-01] 단장 모드 리그 전체 상태 + 시즌 연도 상태 머신(시상식 종료 → 다음 해 스토브리그).</summary>
    public class GMLeagueState
    {
        public GMStartMode Mode;
        public int SeasonYear = GMFeatureFlags.DEFAULT_START_YEAR;
        public GMSeasonPhase Phase = GMSeasonPhase.StoveLeague;
        public string SelectedTeamCode;
        public bool UseVirtualNames;
        public readonly Dictionary<string, GMTeamState> Teams = new Dictionary<string, GMTeamState>();
        public readonly List<Player> FreeAgents = new List<Player>();

        public GMTeamState UserTeam => SelectedTeamCode != null && Teams.TryGetValue(SelectedTeamCode, out var t) ? t : null;

        /// <summary>[TASK-GM-14] 단장 재임 시즌 번호(1부터) - 헤더 표기 「KBO 시즌 N」. 모든 모드가 2026에서 시작한다.</summary>
        public int SeasonNumber => Math.Max(1, SeasonYear - GMFeatureFlags.DEFAULT_START_YEAR + 1);
        public static string SeasonTitle(int seasonNumber) => $"KBO 시즌 {Math.Max(1, seasonNumber)}";

        // [TASK-GM-02] 정규시즌 진행 상태 - 경기 진행 인덱스(0~144) · 구단 성적 · 선수 누적 기록 · 최신 소식.
        public int GamesPlayed;
        public int Seed = 20260328;
        public readonly Dictionary<string, GMTeamRecord> Records = new Dictionary<string, GMTeamRecord>();
        public readonly Dictionary<string, GMPlayerSeasonStats> Stats = new Dictionary<string, GMPlayerSeasonStats>();
        public readonly List<GMNewsItem> News = new List<GMNewsItem>();
        public const int MaxNews = 120;

        // [TASK-GM-04] 이번 시즌 시상 묶음(월간 · 올스타 · 포스트시즌 · KBO 시상식 · 골든글러브) + 역대 시즌 수상 기록(SeasonAwardsHistory)
        public SeasonAwardCeremonyBundle Awards = new SeasonAwardCeremonyBundle { SeasonYear = GMFeatureFlags.DEFAULT_START_YEAR };
        public readonly List<SeasonAwardCeremonyBundle> SeasonAwardsHistory = new List<SeasonAwardCeremonyBundle>();

        public IEnumerable<Player> AllPlayers => Teams.Values.SelectMany(t => t.Roster);

        // [TASK-GM-06] 프런트 오피스(구단주 · 목표 · 난이도 · 하우스 룰 · 시즌 이력 · 스토리 안건 · 엔딩) + 신인 드래프트 유망주 풀
        public GMFrontOfficeState FrontOffice = new GMFrontOfficeState();
        public readonly List<Player> DraftPool = new List<Player>();
        // [TASK-GM-08] FA 원 소속 · 등급(InstanceId → 항목) · 정산 대기 보상 · 자동 보호 대상(올해 FA 계약 · 신인) · 단장 수동 보호 명단
        public readonly Dictionary<string, GMFaOriginEntry> FAOrigins = new Dictionary<string, GMFaOriginEntry>();
        public readonly List<GMPendingCompensation> PendingCompensations = new List<GMPendingCompensation>();
        public readonly List<string> FASignedThisYear = new List<string>();
        public readonly List<string> RookiesThisYear = new List<string>();
        public readonly List<string> UserProtectedIds = new List<string>();
        // [TASK-GM-09] 글로벌 대회(WBC · 아시안게임 · 프리미어 12) 일정 · 결과
        public readonly List<GMTournamentRecord> Tournaments = new List<GMTournamentRecord>();
        /// <summary>[TASK-GM-09] 직전 정규시즌 개막 준비(AI FA 입찰 · WBC)의 AI 영입 기록.</summary>
        public readonly List<GMFreeAgencyCycle.AiSigning> LastAiSignings = new List<GMFreeAgencyCycle.AiSigning>();
        // [TASK-GM-10] 원 소속 우선 협상 명단(내 구단 계약 만료자 InstanceId · 세이브 v21) · 직전 보류명단 제외 기록
        public readonly List<string> PriorityNegotiationIds = new List<string>();
        public readonly List<GMReserveList.Release> LastReserveReleases = new List<GMReserveList.Release>();
        // [TASK-GM-07] 내 구단 정규시즌 경기 결과(시즌 일정 캘린더 · 세이브 v18)
        public readonly List<GMGameResultEntry> UserResults = new List<GMGameResultEntry>();
        public GMGameResultEntry ResultOn(int day) => UserResults.FirstOrDefault(r => r.Day == day);

        public GMTeamRecord RecordOf(string teamCode)
        {
            if (!Records.TryGetValue(teamCode, out var record)) Records[teamCode] = record = new GMTeamRecord { TeamCode = teamCode };
            return record;
        }

        public GMPlayerSeasonStats StatsOf(Player player, string teamCode)
        {
            if (!Stats.TryGetValue(player.InstanceId, out var stats))
                Stats[player.InstanceId] = stats = new GMPlayerSeasonStats
                {
                    PlayerId = player.InstanceId, TeamCode = teamCode, IsPitcher = player.IsPitcher,
                    DefRating = player.IsPitcher ? 50 : player.DefenseStat, // [TASK-GM-04] 수비 기여 점수 기준값
                };
            return stats;
        }

        public void AddNews(GMNewsItem item)
        {
            if (item == null) return;
            News.Insert(0, item); // 최신순
            if (News.Count > MaxNews) News.RemoveRange(MaxNews, News.Count - MaxNews);
        }

        // [TASK-GM-03] 내 구단 경기 박스스코어 - 고속 진행 중 · 직후에도 직전 경기 상세를 볼 수 있게 최근 10경기를 보관(최신순).
        public const int MaxRecentBoxScores = 10;
        public readonly List<GMMatchBoxScoreData> RecentUserBoxScores = new List<GMMatchBoxScoreData>();
        public GMMatchBoxScoreData LastUserMatchBoxScore => RecentUserBoxScores.Count > 0 ? RecentUserBoxScores[0] : null;

        public void AddBoxScore(GMMatchBoxScoreData box)
        {
            if (box == null) return;
            RecentUserBoxScores.Insert(0, box);
            if (RecentUserBoxScores.Count > MaxRecentBoxScores) RecentUserBoxScores.RemoveRange(MaxRecentBoxScores, RecentUserBoxScores.Count - MaxRecentBoxScores);
        }

        // [TASK-GM-08] 리그 타격 밸런스(세이브 제외 - 시즌 첫 경기에 1군 로스터로 다시 만든다, 환경 배수는 경기일마다 재수렴)
        public KBOManager.Engine.GMBattingBalance BattingBalance;

        public KBOManager.Engine.GMBattingBalance EnsureBattingBalance() =>
            BattingBalance ?? (BattingBalance = KBOManager.Engine.GMBattingBalance.FromRosters(Teams.Values.Select(t => (IEnumerable<Player>)t.Roster)));

        public Player FindPlayer(string instanceId) => AllPlayers.FirstOrDefault(p => p.InstanceId == instanceId);
        public string TeamCodeOf(Player player) => Teams.Values.FirstOrDefault(t => t.Roster.Contains(player))?.TeamCode;

        /// <summary>
        /// 다음 단계로 진행한다: 스토브리그 → 정규시즌 → 포스트시즌 → 시상식 → (해 넘김) 다음 해 스토브리그.
        /// 해가 넘어갈 때 전원 나이 +1, 잔여 계약 -1(0 하한), 부상 일수 초기화. 반환값 = 해가 넘어갔는지.
        /// [TASK-GM-04] 이번 시즌 시상 묶음은 SeasonAwardsHistory로 옮기고(수상 이력 · 성향은 선수에 영구 보존) 새 시즌 묶음을 연다.
        /// </summary>
        public bool AdvancePhase()
        {
            if (Phase != GMSeasonPhase.AwardsCeremony)
            {
                Phase = (GMSeasonPhase)((int)Phase + 1);
                return false;
            }

            if (Awards != null && Awards.HasAny) SeasonAwardsHistory.Add(Awards);
            SeasonYear++;
            Awards = new SeasonAwardCeremonyBundle { SeasonYear = SeasonYear };
            Phase = GMSeasonPhase.StoveLeague;
            GamesPlayed = 0; // [TASK-GM-02] 새 시즌 - 성적 · 기록 초기화(소식 피드는 유지)
            Records.Clear();
            Stats.Clear();
            RecentUserBoxScores.Clear();
            UserResults.Clear(); // [TASK-GM-07]
            BattingBalance = null; // [TASK-GM-08] 새 시즌 - 로스터 기준 재계산
            FASignedThisYear.Clear(); // [TASK-GM-08] 자동 보호(올해 FA 계약 · 신인)는 1년 한정
            RookiesThisYear.Clear();
            foreach (var p in Teams.Values.SelectMany(t => t.ReservePlayers).Concat(FreeAgents))
            {
                p.Age = Math.Min(Player.MaxAge, p.Age + 1);
                p.ContractYears = Math.Max(0, p.ContractYears - 1);
                p.InjuryRemainingDays = 0;
                if (p.MaxStamina > 0) p.CurrentStamina = p.MaxStamina;
            }
            return true;
        }
    }

    /// <summary>
    /// [TASK-GM-01] 3대 시작 모드 로스터 로더. 10개 구단 28인(타자 15 · 투수 13) 로스터와 구단 재정, 치어리더 풀을 초기화한다.
    ///   - RealCurrent2026: 현역(players.csv active) 선수를 현 소속(team_id) 기준으로 배치, 2026 스토브리그 시작.
    ///   - AllTimeDream: 1986~2026 카드(발급 구단 = 그 시즌 계보)에서 선수별 최고 시즌을 골라 구단 계보별 드림 로스터 + FA 시장.
    ///   - StoryCampaign: 현역 로스터 + 선택 구단을 4년 연속 최하위 위기(예산 20% 삭감, Ego 5 35세 노장 4번 타자 트레이드 요구, 구단주 압박)로.
    /// 구단 후보가 모자라면 아직 배정되지 않은 다른 선수로 채워 28인을 보장한다(같은 선수가 두 구단에 들어가지 않는다).
    /// </summary>
    public static class GMRosterLoader
    {
        public const int BatterCount = 15;
        public const int PitcherCount = 13;
        public const int StartingPitcherCount = 5;
        public const int FreeAgentMarketSize = 30;
        // KBO 샐러리캡(상위 40인 기준 137억 원대)을 28인 기준 가정값으로 둔다 - 단위 만 원.
        public const int DefaultPayrollCap = 1370000;
        public const int BudgetToCapPercent = 130;  // 운영 예산 = 샐러리캡의 130%
        public const int StoryBudgetPercent = 80;   // 스토리 캠페인 예산 20% 삭감
        public const int StoryVeteranAge = 35;

        /// <summary>
        /// [TASK-GM-09] 재현 가능한 InstanceId - 같은 키는 항상 같은 GUID(MD5). 로더 · 퓨처스 · FA 공시가 Guid.NewGuid 대신 쓴다.
        /// 같은 모드 · 구단 · 시드로 시작하면 시즌 결과가 매번 같아진다(밸런스 검증 재현성).
        /// </summary>
        public static string StableId(string key)
        {
            using (var md5 = System.Security.Cryptography.MD5.Create())
                return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key ?? ""))).ToString();
        }

        private static readonly BatterPosition[] StarterPositions =
        {
            BatterPosition.Catcher, BatterPosition.FirstBase, BatterPosition.SecondBase, BatterPosition.ThirdBase, BatterPosition.ShortStop,
            BatterPosition.LeftField, BatterPosition.CenterField, BatterPosition.RightField, BatterPosition.DesignatedHitter,
        };

        /// <summary>지시서 시그니처 - 씬의 PlayerDatabase와 치어리더 카탈로그를 쓴다.</summary>
        public static GMLeagueState LoadModeRoster(GMStartMode mode, string selectedTeamCode, bool useVirtualNames)
        {
            var db = PlayerDatabase.Instance != null ? PlayerDatabase.Instance : UnityEngine.Object.FindAnyObjectByType<PlayerDatabase>(FindObjectsInactive.Include);
            if (db == null)
            {
                Debug.LogError("[GMRosterLoader] PlayerDatabase가 없어 로스터를 만들 수 없습니다.");
                return null;
            }
            // [TASK-GM-16] 실제 새 게임 = 스타터 덱(매번 다른 시드 - 리세마라). 테스트 · 툴 오버로드는 시드를 넘길 때만 쓴다.
            int? starterSeed = StarterDeckApplies(mode) ? GMStarterDeck.NewSeed() : (int?)null;
            return LoadModeRoster(mode, selectedTeamCode, useVirtualNames, db.AllTemplates, AllCheerleaderTemplates(), starterSeed);
        }

        /// <summary>[TASK-GM-16] 스타터 덱 적용 모드 - 현역 · 스토리(올타임 드림은 계보별 최고 시즌 드림 로스터 유지).</summary>
        public static bool StarterDeckApplies(GMStartMode mode) => GMFeatureFlags.IsStarterDeckEnabled && mode != GMStartMode.AllTimeDream;

        /// <summary>테스트/툴용 - 템플릿과 치어리더 카탈로그를 직접 넘긴다.</summary>
        public static GMLeagueState LoadModeRoster(GMStartMode mode, string selectedTeamCode, bool useVirtualNames,
            IReadOnlyList<PlayerTemplate> templates, IReadOnlyList<Cheerleader> cheerCatalog, int? starterDeckSeed = null)
        {
            string userCode = NameAliasTable.ResolveCanonicalTeamCode(selectedTeamCode) ?? NameAliasTable.SAM;
            NameAliasTable.ApplyDisplayNames(templates, useVirtualNames);

            var state = new GMLeagueState
            {
                Mode = mode,
                SeasonYear = GMFeatureFlags.DEFAULT_START_YEAR,
                Phase = GMSeasonPhase.StoveLeague,
                SelectedTeamCode = userCode,
                UseVirtualNames = useVirtualNames,
            };

            var all = (templates ?? Array.Empty<PlayerTemplate>()).Where(t => t != null && !string.IsNullOrEmpty(t.RealPlayerId)).ToList();
            var byPerson = all.GroupBy(t => t.RealPlayerId).ToDictionary(g => g.Key, g => g.ToList());
            var debutYear = byPerson.ToDictionary(p => p.Key, p => p.Value.Min(t => t.SeasonYear));
            var awards = byPerson.ToDictionary(p => p.Key, p => AwardsOf(p.Value));

            // 모드별 후보: 구단 코드 → (대표 템플릿, 평가 점수, 기준 시즌 연도)
            var candidates = mode == GMStartMode.AllTimeDream ? DreamCandidates(byPerson) : CurrentCandidates(byPerson);
            var used = new HashSet<string>();
            // [TASK-GM-16] 스타터 덱 - 10구단 28인을 계보 역대 전 연도 카드에서 등급 확률(4/10/25/45/16%)로 추첨
            bool starter = starterDeckSeed.HasValue && starterDeckSeed.Value != 0 && mode != GMStartMode.AllTimeDream;
            var starterPicks = starter ? GMStarterDeck.DrawAll(all, starterDeckSeed.Value, used) : null;
            if (!starter) GMStarterDeck.LastDraws.Clear();

            foreach (var code in NameAliasTable.CanonicalTeamCodes)
            {
                var team = new GMTeamState
                {
                    TeamCode = code,
                    Team = NameAliasTable.ToTeam(code),
                    DisplayName = NameAliasTable.DisplayTeamName(code),
                    IsUserTeam = code == userCode,
                    PayrollCap = DefaultPayrollCap,
                };
                team.Budget = (long)team.PayrollCap * BudgetToCapPercent / 100;

                var picks = starter ? starterPicks[code]
                    : PickRoster(candidates.TryGetValue(code, out var list) ? list : new List<Candidate>(), candidates.Values.SelectMany(c => c), used);
                foreach (var c in picks)
                {
                    var player = new Player(StableId(starter ? $"{mode}|{userCode}|{code}|{c.Template.TemplateId}|DECK{starterDeckSeed.Value}" : $"{mode}|{userCode}|{code}|{c.Template.TemplateId}"), c.Template);
                    player.CareerAwardIds = new List<string>(awards[c.Template.RealPlayerId]);
                    player.InitializeGMAttributesFromStats(c.SeasonYear, debutYear[c.Template.RealPlayerId]);
                    team.Roster.Add(player);
                }
                // 알파독 판정은 "팀 내 최상위 OVR"이 정해진 뒤 다시 한 번 한다(상위 3인).
                foreach (var top in team.Roster.OrderByDescending(p => p.BaseOverall).Take(3).ToList())
                {
                    var c = picks.First(x => x.Template == top.Template);
                    top.InitializeGMAttributesFromStats(c.SeasonYear, debutYear[top.Template.RealPlayerId], isTeamTopOverall: true);
                }
                if (!team.IsUserTeam) AppointCaptain(team); // AI 구단은 자동 주장 · 유저 구단은 단장이 임명한다
                team.CheerAutoRotate = GMCheerleaderRoster.AutoRotateUserCheerleaders || !team.IsUserTeam; // [TASK-GM-06] D.2 내 구단도 기본 자동 로테이션

                LoadCheerleaderPool(team, cheerCatalog, mode == GMStartMode.AllTimeDream ? 0 : state.SeasonYear, useVirtualNames);
                state.Teams[code] = team;
            }

            BuildFreeAgentMarket(state, candidates, used, debutYear, awards);
            if (mode == GMStartMode.StoryCampaign) ApplyStoryCampaign(state.UserTeam);
            BuildDraftPool(state, byPerson, used, debutYear, mode == GMStartMode.AllTimeDream ? 0 : GMFeatureFlags.DEFAULT_START_YEAR); // [TASK-GM-06] · [TASK-GM-07] 현역 모드는 2026 카드만
            GMRosterTiers.BuildFuturesPools(state, byPerson, used, debutYear);                  // [TASK-GM-08] 퓨처스 핵심 유망주 풀(구단당 12명)
            GMFaCompensation.RegisterMarketOrigins(state);                                     // [TASK-GM-08] FA 원 소속 · 등급
            GMFrontOffice.Initialize(state);                 // [TASK-GM-06] 구단주 · 목표 · 시즌 이력 · 스토리 안건
            if (starter)
            {
                GMFrontOffice.Ensure(state).StarterDeckSeed = starterDeckSeed.Value; // [TASK-GM-16]
                var mine = GMStarterDeck.LastDraws.Where(d => d.TeamCode == userCode).ToList();
                var stars = mine.Where(d => d.Actual <= GMStarterTier.A).OrderBy(d => d.Actual).ThenByDescending(d => d.Card.GetBaseOverall()).Take(4).ToList();
                state.AddNews(new GMNewsItem
                {
                    GameIndex = 0, DateLabel = $"{state.SeasonYear} 스토브리그", Kind = GMNewsKind.Season, IsUserTeam = true, IsMajor = true,
                    Title = $"[스타터 덱] {NameAliasTable.DisplayTeamName(userCode)} 첫 선수단 지급 - {GMStarterDeck.Summary(mine)}",
                    Body = stars.Count > 0 ? "레전드 합류: " + string.Join(" · ", stars.Select(d => $"{d.Card.SeasonYear} {d.Card.PlayerName}({GMStarterDeck.TierName(d.Actual)}급)")) : "이번 덱에는 S · A급이 없습니다 - 새 게임으로 다시 뽑을 수 있습니다.",
                });
            }
            return state;
        }

        // ================================================================== 후보

        public sealed class Candidate
        {
            public PlayerTemplate Template;
            public int Score;
            public int SeasonYear;
        }

        /// <summary>
        /// 현역 선수 - [TASK-GM-07] 2026 시즌 카드(SeasonYear = 2026)가 있는 선수만, 그 2026 카드(같은 해 카드가 여럿이면 상위 등급)와 2026 소속 구단 기준으로 뽑는다.
        /// 예전에는 선수별 "최신 시즌" 카드를 써서 2026 카드가 없는 은퇴 · 과거 선수(다른 연도 카드)가 모자란 자리를 채웠다 - 이제 2026 데이터만 쓴다.
        /// </summary>
        public static Dictionary<string, List<Candidate>> CurrentCandidates(Dictionary<string, List<PlayerTemplate>> byPerson, int year = GMFeatureFlags.DEFAULT_START_YEAR)
        {
            var result = NameAliasTable.CanonicalTeamCodes.ToDictionary(c => c, c => new List<Candidate>());
            foreach (var person in byPerson.Values)
            {
                var card = person.Where(t => t.SeasonYear == year).OrderByDescending(t => (int)t.Grade).ThenByDescending(t => t.GetBaseOverall()).FirstOrDefault();
                if (card == null) continue;
                var home = card.Team != Team.None ? card.Team : card.CurrentTeam;
                string code = NameAliasTable.ToCode(home);
                if (code == null) continue;
                result[code].Add(new Candidate { Template = card, Score = card.GetBaseOverall(), SeasonYear = year });
            }
            return result;
        }

        /// <summary>올타임 드림 - 선수별 최고 시즌(등급 → 연도 순) 카드를 그 시즌 발급 구단 계보에 넣는다. 점수 = OVR + 등급 가중.</summary>
        private static Dictionary<string, List<Candidate>> DreamCandidates(Dictionary<string, List<PlayerTemplate>> byPerson)
        {
            var result = NameAliasTable.CanonicalTeamCodes.ToDictionary(c => c, c => new List<Candidate>());
            foreach (var person in byPerson.Values)
            {
                var best = person.OrderByDescending(t => (int)t.Grade).ThenByDescending(t => t.SeasonYear).First();
                string code = NameAliasTable.ToCode(best.Team != Team.None ? best.Team : best.CurrentTeam);
                if (code == null) continue;
                result[code].Add(new Candidate { Template = best, Score = best.GetBaseOverall() + (int)best.Grade * 3, SeasonYear = best.SeasonYear });
            }
            return result;
        }

        /// <summary>9개 수비 위치 주전 → 벤치 6 → 선발 5 → 불펜 8. 구단 후보가 모자라면 전체 미배정 후보로 채운다.</summary>
        private static List<Candidate> PickRoster(List<Candidate> own, IEnumerable<Candidate> global, HashSet<string> used)
        {
            var picks = new List<Candidate>();
            var ownSorted = own.OrderByDescending(c => c.Score).ToList();
            List<Candidate> globalSorted = null;

            Candidate Take(Func<Candidate, bool> filter)
            {
                var c = ownSorted.FirstOrDefault(x => !used.Contains(x.Template.RealPlayerId) && filter(x));
                if (c == null)
                {
                    if (globalSorted == null) globalSorted = global.OrderByDescending(x => x.Score).ToList();
                    c = globalSorted.FirstOrDefault(x => !used.Contains(x.Template.RealPlayerId) && filter(x));
                }
                if (c != null) { used.Add(c.Template.RealPlayerId); picks.Add(c); }
                return c;
            }

            foreach (var pos in StarterPositions)
            {
                if (Take(c => !c.Template.IsPitcher && c.Template.BatterPosition == pos) == null) Take(c => !c.Template.IsPitcher);
            }
            while (picks.Count(c => !c.Template.IsPitcher) < BatterCount && Take(c => !c.Template.IsPitcher) != null) { }

            for (int i = 0; i < StartingPitcherCount; i++)
            {
                if (Take(c => c.Template.IsPitcher && c.Template.PitcherRole == PitcherRole.StartingPitcher) == null) Take(c => c.Template.IsPitcher);
            }
            if (Take(c => c.Template.IsPitcher && c.Template.PitcherRole == PitcherRole.Closer) == null) Take(c => c.Template.IsPitcher);
            while (picks.Count(c => c.Template.IsPitcher) < PitcherCount)
            {
                if (Take(c => c.Template.IsPitcher && c.Template.PitcherRole != PitcherRole.StartingPitcher) == null && Take(c => c.Template.IsPitcher) == null) break;
            }
            return picks;
        }

        /// <summary>카드 등급에서 수상 이력 태그를 만든다(타이틀 홀더 · 골든글러브 · 왕조 = 한국시리즈 우승).</summary>
        private static List<string> AwardsOf(List<PlayerTemplate> person)
        {
            var tags = new List<string>();
            foreach (var t in person.OrderBy(t => t.SeasonYear))
            {
                switch (t.Grade)
                {
                    case Grade.TITLE_HOLDER: tags.Add($"TITLE_HOLDER_{t.SeasonYear}"); break;
                    case Grade.GOLDEN_GLOVE: tags.Add($"GOLDEN_GLOVE_{t.SeasonYear}"); break;
                    case Grade.DYNASTY: tags.Add($"KS_CHAMPION_{t.SeasonYear}"); break;
                }
            }
            return tags.Distinct().ToList();
        }

        private static void AppointCaptain(GMTeamState team)
        {
            var captain = team.Roster.Where(p => p.RoleArchetype == LockerRoomRole.DugoutLeader).OrderByDescending(p => p.Age).FirstOrDefault()
                          ?? team.Roster.Where(p => !p.IsPitcher).OrderByDescending(p => p.Age).FirstOrDefault();
            if (captain != null) captain.IsCaptain = true;
        }

        private static void BuildFreeAgentMarket(GMLeagueState state, Dictionary<string, List<Candidate>> candidates, HashSet<string> used,
            Dictionary<string, int> debutYear, Dictionary<string, List<string>> awards)
        {
            var pool = candidates.Values.SelectMany(c => c).Where(c => !used.Contains(c.Template.RealPlayerId) && c.Score > 0)
                .OrderByDescending(c => c.Score).Take(FreeAgentMarketSize);
            foreach (var c in pool)
            {
                var player = new Player(StableId($"{state.Mode}|{state.SelectedTeamCode}|FA|{c.Template.TemplateId}"), c.Template);
                player.CareerAwardIds = new List<string>(awards[c.Template.RealPlayerId]);
                player.InitializeGMAttributesFromStats(c.SeasonYear, debutYear[c.Template.RealPlayerId]);
                player.ContractYears = 0; // FA = 계약 만료
                state.FreeAgents.Add(player);
            }
        }

        /// <summary>
        /// [TASK-GM-06] 신인 드래프트 유망주 풀(10명) - 아직 어느 구단 · FA에도 없는 선수 중 데뷔가 가장 최근인(경력이 짧은) 선수의 최신 시즌 카드.
        /// 19~22세 · 신인 계약(최저연봉 3,000만 원 · 5년) · 유망주 성향 · Ego 1로 재설정한다. 모자라면 남은 미배정 선수로 채운다.
        /// </summary>
        public static void BuildDraftPool(GMLeagueState state, Dictionary<string, List<PlayerTemplate>> byPerson, HashSet<string> used, Dictionary<string, int> debutYear, int onlyYear = 0)
        {
            state.DraftPool.Clear();
            var taken = new HashSet<string>(used);
            foreach (var fa in state.FreeAgents) if (fa.Template != null) taken.Add(fa.Template.RealPlayerId);
            // [TASK-GM-07] 현역 모드 - 2026 카드가 있는 선수가 드래프트 풀(10명)을 채울 만큼 남아 있으면 그 선수들만 쓴다.
            bool restrict = onlyYear > 0 && byPerson.Count(p => !taken.Contains(p.Key) && p.Value.Any(t => t.SeasonYear == onlyYear)) >= GMStoveLeagueMarket.DraftPoolSize;
            var picks = byPerson.Where(p => !taken.Contains(p.Key) && (!restrict || p.Value.Any(t => t.SeasonYear == onlyYear)))
                .Select(p => p.Value.OrderByDescending(t => t.SeasonYear).ThenByDescending(t => (int)t.Grade).First())
                .OrderByDescending(t => debutYear.TryGetValue(t.RealPlayerId, out int d) ? d : 0)
                .ThenByDescending(t => t.GetBaseOverall())
                .Take(GMStoveLeagueMarket.DraftPoolSize).ToList();
            int i = 0;
            foreach (var t in picks)
            {
                var p = new Player(StableId($"{state.Mode}|{state.SelectedTeamCode}|{state.SeasonYear}|DRAFT|{t.TemplateId}"), t);
                p.InitializeGMAttributesFromStats(state.SeasonYear, debutYear.TryGetValue(t.RealPlayerId, out int d) ? d : state.SeasonYear);
                GMStoveLeagueMarket.MakeRookie(p, 19 + (i++ % 4));
                state.DraftPool.Add(p);
            }
        }

        /// <summary>「꼴찌 구단의 겨울」 - 4년 연속 최하위 · 예산 20% 삭감 · 35세 Ego 5 노장 4번 타자 트레이드 요구 · 구단주 포스트시즌 압박.</summary>
        private static void ApplyStoryCampaign(GMTeamState team)
        {
            if (team == null) return;
            team.ConsecutiveLastPlaceSeasons = 4;
            team.OwnerPostseasonPressure = true;
            team.Budget = team.Budget * StoryBudgetPercent / 100;

            var cleanup = team.Batters.OrderByDescending(p => p.Template.BatterStats.Power).ThenByDescending(p => p.BaseOverall).FirstOrDefault();
            if (cleanup == null) return;
            cleanup.Age = StoryVeteranAge;
            cleanup.EgoLevel = 5;
            cleanup.RoleArchetype = LockerRoomRole.AlphaDog;
            cleanup.PersonalMorale = Math.Min(cleanup.PersonalMorale, 35);
            team.TradeRequestPlayerId = cleanup.Template.RealPlayerId;
        }

        // ================================================================== 치어리더

        /// <summary>카탈로그 전 등급 원본(중복 인물 포함).</summary>
        public static List<Cheerleader> AllCheerleaderTemplates()
        {
            var list = new List<Cheerleader>();
            foreach (CheerleaderGrade grade in Enum.GetValues(typeof(CheerleaderGrade))) list.AddRange(CheerleaderCatalog.GetCheerleadersByGrade(grade));
            return list;
        }

        /// <summary>
        /// 구단 소속 치어리더를 인물 단위(동명 = 같은 사람)로 최대 15명 뽑는다 - 그 시즌 활동 중인 카드 우선, 같은 사람이면 높은 등급.
        /// seasonYear가 0이면(올타임 드림) 활동 시기를 가리지 않는다. 엔트리는 앞 5명(기본, 4~6명 가변).
        /// </summary>
        public static void LoadCheerleaderPool(GMTeamState team, IReadOnlyList<Cheerleader> catalog, int seasonYear, bool useVirtualNames)
        {
            team.CheerleaderPool.Clear();
            if (catalog == null) return;
            var people = catalog.Where(c => c != null && c.Team == team.Team && !string.IsNullOrEmpty(c.Name))
                .GroupBy(c => c.Name.Trim())
                .Select(g => g.OrderByDescending(c => seasonYear > 0 && CheerleaderActivePeriod.Contains(c.ActivePeriod, seasonYear))
                              .ThenByDescending(c => (int)c.Grade).First())
                .OrderByDescending(c => seasonYear > 0 && CheerleaderActivePeriod.Contains(c.ActivePeriod, seasonYear))
                .ThenByDescending(c => (int)c.Grade).ThenBy(c => c.Name)
                .Take(GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX);

            foreach (var t in people)
            {
                team.CheerleaderPool.Add(new Cheerleader(
                    instanceId: StableId($"{team.TeamCode}|CHEER|{t.CatalogId}|{t.Name}"),
                    name: t.Name, // [TASK-GM-02] 실명 보존 - 표시는 Cheerleader.DisplayName(NameAliasTable)
                    grade: t.Grade,
                    conditionBuff: t.ConditionBuff,
                    clutchMultiplier: t.ClutchMultiplier,
                    economicBonusRate: t.EconomicBonusRate,
                    sentimentDefense: t.SentimentDefense,
                    catalogId: t.CatalogId,
                    team: t.Team,
                    activePeriod: t.ActivePeriod));
            }
            GMCheerleaderRoster.FillPool(team, seasonYear > 0 ? seasonYear : GMFeatureFlags.DEFAULT_START_YEAR); // [TASK-GM-05] 15인 풀 보장
            team.CheerEntrySize = GMCheerleaderRules.ClampEntrySize(Math.Min(GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_DEFAULT, team.CheerleaderPool.Count));
        }
    }
}
