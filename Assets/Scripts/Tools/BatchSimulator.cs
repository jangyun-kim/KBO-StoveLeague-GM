using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Tools
{
    /// <summary>
    /// UI 없이 144경기(1시즌)를 즉시 연속 실행해 체력/로테이션 밸런스(StaminaCostPerBatterFaced,
    /// LeagueManager.StarterStaminaRecoveryPerDay/BullpenStaminaRecoveryPerDay/RestDayRecoveryBonus,
    /// MatchEngine.MinStaminaPercentToPitch 등)를 빠르게 검증하기 위한 디버그 도구.
    ///
    /// Editor 전용 API(UnityEditor)를 전혀 쓰지 않는 순수 MonoBehaviour다 - "Assets/Scripts/Editor"가
    /// 아니라 "Assets/Scripts/Tools"에 위치해야 하므로(빌드에서 자동 제외되지 않는 위치), 이 파일 자체가
    /// 빌드에 포함돼도 컴파일 에러가 나지 않도록 EditorWindow 대신 이 방식을 택했다. 씬의 빈
    /// GameObject에 붙이고 인스펙터 우클릭 -> "Run 144-Game Season Simulation"으로 실행한다.
    ///
    /// "우리 팀" 28인 로스터는 게임 저장 데이터를 전혀 건드리지 않고 이 클래스 안에서 절차적으로
    /// 새로 생성한다(LeagueManager.GenerateTeamRoster와 동일한 포지션/롤 배분을 의도적으로 중복
    /// 구현했다 - 그 메서드가 private이기도 하고, 이 도구가 실제 세이브/DB에 전혀 의존하지 않아야
    /// 아무 씬에서나 독립적으로 돌려볼 수 있기 때문이다). 상대 팀은 매 경기 새로 절차적으로 생성해
    /// 던져 버린다 - 이 도구가 추적하는 것은 오직 "우리 팀 투수진"의 체력 궤적이다.
    /// </summary>
    public class BatchSimulator : MonoBehaviour
    {
        [Header("Optional References")]
        [Tooltip("스킬 효과까지 포함해 검증하고 싶으면 연결한다. 비워두면 스킬 없이 순수 스탯만으로 시뮬레이션한다.")]
        [SerializeField] private SkillDB skillDB;
        [SerializeField] private EngineConfig engineConfig;

        [Header("Simulation Settings")]
        [SerializeField] private int opponentCount = LeagueManager.OpponentCount;       // 9
        [SerializeField] private int gamesPerOpponent = LeagueManager.GamesPerOpponent; // 16 (총 144)
        [Tooltip("두 팀 모두 이 스탯 수준으로 생성한다(밸런스 테스트 목적상 대칭 - 실력 차이가 아니라 체력/로테이션만 검증하기 위함).")]
        [SerializeField] private int baseStatLevel = 65;
        [Tooltip("지정하면 매 실행마다 동일한(재현 가능한) 시즌이 나온다. 비워두면 매번 다른 결과.")]
        [SerializeField] private bool useFixedSeed = false;
        [SerializeField] private int fixedSeed = 12345;

        private static readonly BatterPosition[] AllBatterPositions = (BatterPosition[])Enum.GetValues(typeof(BatterPosition));
        private const int BenchBatterCount = 6;

        private static readonly (PitcherRole role, int count)[] PitcherRoleQuota =
        {
            (PitcherRole.StartingPitcher, 5),
            (PitcherRole.WinningReliever, 2),
            (PitcherRole.MopUpReliever, 4),
            (PitcherRole.LongReliever, 1),
            (PitcherRole.Closer, 1),
        };

        private class PitcherSeasonStats
        {
            public int BattersFaced;
            public int Appearances;
            public int LowStaminaAppearances; // 등판 시작 시점 체력이 30% 미만이었던 횟수
        }

        // RunSeasonSimulation() 실행 중에만 유효하다 - HandleCalendarDayAdvanced()가 참조할 "지금 회복시켜야
        // 할 우리 팀 로스터"를 담아 둔다(캘린더 이벤트 핸들러는 인자로 로스터를 받지 않으므로 필드로 전달).
        private List<Player> activeOurTeamRoster;

        [ContextMenu("Run 144-Game Season Simulation")]
        public void RunSeasonSimulation()
        {
            int totalGames = opponentCount * gamesPerOpponent;
            var ourTeam = GenerateRoster("OUR_TEAM", baseStatLevel);
            var ourPitchers = ourTeam.Where(p => p.Template.IsPitcher).ToList();
            var statsByPitcher = ourPitchers.ToDictionary(p => p, _ => new PitcherSeasonStats());

            // 이 시뮬레이터 전용 임시 캘린더. 씬에 이미 LeagueCalendar가 있으면 그 싱글톤을 그대로
            // 재사용하고(Awake()의 중복 인스턴스 파괴 로직이 알아서 처리), 없으면 이 GameObject에
            // 새로 붙여 즉석에서 만든다 - 실제 LeagueManager.HandleDayAdvanced와 동일한 회복 공식
            // (LeagueManager.StarterStaminaRecoveryPerDay 등 public 상수)을 그대로 검증하기 위함이다.
            var calendar = LeagueCalendar.Instance != null ? LeagueCalendar.Instance : gameObject.AddComponent<LeagueCalendar>();
            calendar.InitializeSeason(DateTime.Now.Year);

            activeOurTeamRoster = ourTeam;
            calendar.OnDayAdvanced += HandleCalendarDayAdvanced;

            try
            {
                for (int gameIndex = 0; gameIndex < totalGames; gameIndex++)
                {
                    var opponentRoster = GenerateRoster($"OPPONENT_{gameIndex}", baseStatLevel);
                    bool ourTeamIsHome = gameIndex % 2 == 0;

                    int? seed = useFixedSeed ? fixedSeed + gameIndex : (int?)null;
                    // [TASK-KBO-037] 이 도구는 절차적으로 생성한 대칭(동일 baseStatLevel) 로스터로 체력/
                    // 로테이션 궤적만 검증하는 밸런스 도구다 - GameManager/세이브 데이터와 무관하므로
                    // 팀 버프는 양쪽 모두 TeamPowerModifiers.None(버프 없음)으로 고정한다.
                    var engine = new MatchEngine(
                        ourTeamIsHome ? ourTeam : opponentRoster,
                        ourTeamIsHome ? opponentRoster : ourTeam,
                        TeamPowerModifiers.None, TeamPowerModifiers.None,
                        skillDB, engineConfig, seed);

                    SimulateOneGame(engine, ourTeamIsHome, statsByPitcher);

                    // MatchEngine.PlayFullMatch()와 동일한 진행 규칙(BeginMatch + PlayNextAtBat 반복)을
                    // 위 SimulateOneGame()이 직접 수행한다 - PlayFullMatch() 자체는 MatchResult만 반환하고
                    // 중간 타석 결과를 노출하지 않아, 투수별 등판/체력 통계를 뽑으려면 이 스텝 API가 필요하다.

                    calendar.AdvanceToNextGameDay(); // HandleCalendarDayAdvanced가 실제 회복을 적용한다
                }
            }
            finally
            {
                calendar.OnDayAdvanced -= HandleCalendarDayAdvanced;
                activeOurTeamRoster = null;
            }

            LogReport(ourPitchers, statsByPitcher, totalGames);
        }

        /// <summary>
        /// LeagueManager.HandleDayAdvanced()와 동일한 회복 공식을 그대로 재사용한다 - 상수를 별도로
        /// 복제하지 않고 LeagueManager.StarterStaminaRecoveryPerDay 등 public 상수를 직접 참조하므로,
        /// 실제 게임 플레이에서 쓰이는 값이 바뀌면 이 시뮬레이터의 결과도 자동으로 함께 바뀐다.
        /// </summary>
        private void HandleCalendarDayAdvanced(DateTime newDate, bool passedRestDay)
        {
            if (activeOurTeamRoster == null) return;

            foreach (var player in activeOurTeamRoster)
            {
                if (player?.Template == null || !player.Template.IsPitcher) continue;

                int recoveryAmount = player.Template.PitcherRole == PitcherRole.StartingPitcher
                    ? LeagueManager.StarterStaminaRecoveryPerDay
                    : LeagueManager.BullpenStaminaRecoveryPerDay;
                if (passedRestDay) recoveryAmount += LeagueManager.RestDayRecoveryBonus;

                player.RecoverStamina(recoveryAmount);
            }
        }

        private void SimulateOneGame(MatchEngine engine, bool ourTeamIsHome, Dictionary<Player, PitcherSeasonStats> statsByPitcher)
        {
            engine.BeginMatch("Home", "Away", isPostSeason: false);

            Player lastPitcherSeen = null;

            while (!engine.IsGameOver)
            {
                var step = engine.PlayNextAtBat();
                if (step?.Pitcher == null) continue;

                // step.IsTopHalf == true면 원정이 타격 중이었다는 뜻 = 그때 던진 투수는 홈 소속.
                bool pitcherWasOnHomeSide = step.IsTopHalf;
                bool pitcherWasOurs = ourTeamIsHome ? pitcherWasOnHomeSide : !pitcherWasOnHomeSide;
                if (!pitcherWasOurs || !statsByPitcher.TryGetValue(step.Pitcher, out var stats)) continue;

                stats.BattersFaced++;

                bool isNewAppearance = step.Pitcher != lastPitcherSeen;
                if (isNewAppearance)
                {
                    stats.Appearances++;
                    if (step.Pitcher.IsLowStamina) stats.LowStaminaAppearances++;
                }

                lastPitcherSeen = step.Pitcher;
            }
        }

        private void LogReport(List<Player> ourPitchers, Dictionary<Player, PitcherSeasonStats> statsByPitcher, int totalGames)
        {
            var starters = ourPitchers.Where(p => p.Template.PitcherRole == PitcherRole.StartingPitcher).ToList();
            var bullpen = ourPitchers.Where(p => p.Template.PitcherRole != PitcherRole.StartingPitcher).ToList();

            // "1선발" = 로스터 생성 시점 기준 최고 OVR 선발. 실제로는 체력에 따라 5명이 로테이션되므로
            // 시즌 내내 이 한 명만 등판하는 것은 아니다 - 그가 실제로 얼마나 던졌는지를 보는 지표다.
            var aceStarter = starters.OrderByDescending(p => p.CalculateOVR(false)).FirstOrDefault();

            var sb = new StringBuilder();
            sb.AppendLine($"[BatchSimulator] {totalGames}경기 시즌 시뮬레이션 완료.");

            if (aceStarter != null && statsByPitcher.TryGetValue(aceStarter, out var aceStats))
            {
                float estimatedInnings = aceStats.BattersFaced / 3f; // 아웃 3개 = 1이닝 근사치(볼넷/실책 등은 무시한 추정값)
                sb.AppendLine($"- 1선발({aceStarter.Template.PlayerName}) 총 상대 타자 {aceStats.BattersFaced}명, " +
                               $"추정 투구 이닝 {estimatedInnings:F1}이닝, 등판 {aceStats.Appearances}회 " +
                               $"(그중 체력 30% 미만 등판 {aceStats.LowStaminaAppearances}회)");
            }

            sb.AppendLine("- 선발진 5명 상세:");
            foreach (var starter in starters.OrderByDescending(p => p.CalculateOVR(false)))
            {
                var s = statsByPitcher[starter];
                sb.AppendLine($"    {starter.Template.PlayerName}: 상대 타자 {s.BattersFaced}명 " +
                               $"(추정 {s.BattersFaced / 3f:F1}이닝), 등판 {s.Appearances}회, 저체력 등판 {s.LowStaminaAppearances}회");
            }

            int bullpenTotalAppearances = bullpen.Sum(p => statsByPitcher[p].Appearances);
            int bullpenLowStaminaAppearances = bullpen.Sum(p => statsByPitcher[p].LowStaminaAppearances);
            float avgLowStaminaAppearancesPerBullpenPitcher = bullpen.Count > 0
                ? (float)bullpenLowStaminaAppearances / bullpen.Count
                : 0f;

            sb.AppendLine($"- 불펜 {bullpen.Count}명 총 등판 {bullpenTotalAppearances}회, " +
                           $"그중 체력 30% 미만 등판 {bullpenLowStaminaAppearances}회 " +
                           $"(투수 1인당 평균 {avgLowStaminaAppearancesPerBullpenPitcher:F2}회)");

            sb.AppendLine("- 불펜 상세:");
            foreach (var reliever in bullpen.OrderByDescending(p => p.CalculateOVR(false)))
            {
                var s = statsByPitcher[reliever];
                sb.AppendLine($"    {reliever.Template.PlayerName} ({reliever.Template.PitcherRole}): " +
                               $"등판 {s.Appearances}회, 저체력 등판 {s.LowStaminaAppearances}회");
            }

            Debug.Log(sb.ToString());
        }

        // ----- 절차적 28인 로스터 생성 (LeagueManager.GenerateTeamRoster와 동일한 배분 규칙) -----

        private List<Player> GenerateRoster(string teamLabel, int targetStatLevel)
        {
            var roster = new List<Player>(GameManager.RequiredRosterSize);

            foreach (var position in AllBatterPositions)
            {
                roster.Add(CreatePlayer(teamLabel, false, position, null, targetStatLevel));
            }

            for (int i = 0; i < BenchBatterCount; i++)
            {
                var randomPosition = AllBatterPositions[UnityEngine.Random.Range(0, AllBatterPositions.Length)];
                roster.Add(CreatePlayer(teamLabel, false, randomPosition, null, targetStatLevel));
            }

            foreach (var (role, count) in PitcherRoleQuota)
            {
                for (int i = 0; i < count; i++)
                {
                    roster.Add(CreatePlayer(teamLabel, true, null, role, targetStatLevel));
                }
            }

            return roster;
        }

        private Player CreatePlayer(string teamLabel, bool isPitcher, BatterPosition? batterPosition, PitcherRole? pitcherRole, int targetStatLevel)
        {
            var template = ScriptableObject.CreateInstance<PlayerTemplate>();

            string roleLabel = isPitcher ? (pitcherRole ?? PitcherRole.StartingPitcher).ToString() : (batterPosition ?? BatterPosition.DesignatedHitter).ToString();
            template.TemplateId = $"SIM_{teamLabel}_{roleLabel}_{Guid.NewGuid():N}";
            template.RealPlayerId = template.TemplateId;
            template.PlayerName = $"{teamLabel} {roleLabel}";
            template.Team = Team.None;
            template.Grade = Grade.LIVE_NORMAL;
            template.IsPitcher = isPitcher;
            template.Cost = 1;

            // [TASK-KBO-089] Speed/Defense/Stamina도 동일한 targetStatLevel로 채운다.
            if (isPitcher)
            {
                template.PitcherRole = pitcherRole ?? PitcherRole.StartingPitcher;
                // [TASK-KBO-180] 평준화 버그 수정 - 같은 OVR이라도 포지션/보직 프로파일로 세부 스탯 편차를 준다(평균 = 목표 레벨 유지).
                template.PitcherStats = StatProfiles.SpreadPitcher(targetStatLevel, template.PitcherRole, template.TemplateId?.GetHashCode() ?? 0);
            }
            else
            {
                template.BatterPosition = batterPosition ?? BatterPosition.DesignatedHitter;
                template.BatterStats = StatProfiles.SpreadBatter(targetStatLevel, template.BatterPosition, template.TemplateId?.GetHashCode() ?? 0);
            }

            return new Player(Guid.NewGuid().ToString(), template);
        }
    }
}
