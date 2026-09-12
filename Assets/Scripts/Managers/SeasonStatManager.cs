using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Engine;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>타자 1명의 시즌 누적 원시 기록. 실책/희생플라이/사구는 이 엔진이 모델링하지 않아 제외했다.</summary>
    public class BatterSeasonStats
    {
        public int AtBats;
        public int Hits;
        public int HomeRuns;
        public int Walks;

        public int PlateAppearances => AtBats + Walks;
        public float BattingAverage => AtBats == 0 ? 0f : (float)Hits / AtBats;
    }

    /// <summary>투수 1명의 시즌 누적 원시 기록. 승/패는 "그 경기 그 팀의 선발이 결정 투수"라는 단순화
    /// 규칙을 쓴다(TODO: 실제 KBO 승/패 귀속 규칙(구원승, 5이닝 이상 등)은 후속 과제).</summary>
    public class PitcherSeasonStats
    {
        public int OutsRecorded;
        public int EarnedRuns;
        public int Wins;
        public int Losses;

        public float InningsPitched => OutsRecorded / 3f;
        /// <summary>ERA = 자책점 * 27 / 아웃카운트 (= 자책점 / 이닝 * 9). 아웃카운트가 0이면 0으로 취급.</summary>
        public float EarnedRunAverage => OutsRecorded == 0 ? 0f : EarnedRuns * 27f / OutsRecorded;
    }

    /// <summary>
    /// 타자/투수 시즌 누적 기록을 관리하는 싱글톤. GDD 원문은 "MatchEngine 이벤트를 구독"하라고
    /// 명시하지만, MatchEngine은 MonoBehaviour가 아닌 순수 C# 클래스라 C# event를 노출하지 않는다
    /// (씬/프레임 오버헤드 없이 여러 경기를 즉시 계산하기 위해 의도적으로 그렇게 설계됐다 - MatchEngine.cs
    /// 클래스 주석 참고).
    ///
    /// [TASK-KBO-047] 과거에는 PlayBallController.OnAtBatEnd를 구독해 매 타석마다 기록했으나, 그
    /// 이벤트가 TASK-KBO-041 이후 죽은 이벤트가 되어(CS0067 경고 유발) 구독 자체를 삭제했다. 이제는
    /// PlayBallController.RecordSeasonStatsFromEvents()가 재생용 PlayEvent 큐를 역변환해
    /// RecordAtBat()(아래, public)을 직접 호출하는 경로 하나만 남았다. 경기 종료(OnMatchCompleted)는
    /// 여전히 살아있는 이벤트라 PlayBallController를 그대로 구독한다(InGameUIController/
    /// MatchRewardManager와 동일한 패턴).
    /// </summary>
    public class SeasonStatManager : MonoBehaviour
    {
        public static SeasonStatManager Instance { get; private set; }

        // GDD 규정 타석/이닝: 팀 전체 소화 경기 수(N) 기준으로 "N * 이 배수"를 채워야 랭킹에 집계된다.
        public const float QualifyingPlateAppearancesPerGame = 3.1f; // 규정 타석
        public const float QualifyingInningsPerGame = 1.0f;          // 규정 이닝

        [SerializeField] private PlayBallController playBallController;

        private readonly Dictionary<Player, BatterSeasonStats> batterStatsByPlayer = new Dictionary<Player, BatterSeasonStats>();
        private readonly Dictionary<Player, PitcherSeasonStats> pitcherStatsByPlayer = new Dictionary<Player, PitcherSeasonStats>();

        // 이번 경기에서 각 팀이 "처음 마운드에 올린" 투수 = 그 팀의 선발(승/패 귀속용).
        private readonly Dictionary<Team, Player> startersThisGame = new Dictionary<Team, Player>();
        // 하프이닝이 바뀌며 State.Outs가 0으로 리셋되기 전의 "직전 아웃카운트" - 이번 타석에 새로 기록된
        // 아웃 수(병살 포함)를 델타로 구하는 데 쓴다.
        private int previousOutsInHalfInning;

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

        private void OnEnable()
        {
            if (playBallController != null)
            {
                playBallController.OnMatchCompleted += HandleMatchCompleted;
            }
        }

        private void OnDisable()
        {
            if (playBallController != null)
            {
                playBallController.OnMatchCompleted -= HandleMatchCompleted;
            }
        }

        public BatterSeasonStats GetBatterStats(Player player) =>
            player != null && batterStatsByPlayer.TryGetValue(player, out var stats) ? stats : null;

        public PitcherSeasonStats GetPitcherStats(Player player) =>
            player != null && pitcherStatsByPlayer.TryGetValue(player, out var stats) ? stats : null;

        /// <summary>새 시즌 시작 시 호출한다(스토브리그 -> 다음 시즌 개막). 누적 기록을 전부 비운다.</summary>
        public void ResetSeason()
        {
            batterStatsByPlayer.Clear();
            pitcherStatsByPlayer.Clear();
            startersThisGame.Clear();
            previousOutsInHalfInning = 0;
        }

        // ----- 랭킹 조회 (규정 타석/이닝 필터링 포함) -----

        /// <summary>타율 TOP N. teamGamesPlayed(팀 전체 소화 경기 수) * 3.1 이상 타석을 채운 타자만 대상이다 -
        /// 그렇지 않으면 1타수 1안타(타율 1.000) 같은 표본이 극히 적은 백업 선수가 랭킹 최상단을 차지하는
        /// 왜곡이 생긴다.</summary>
        public List<(Player Player, BatterSeasonStats Stats)> GetBattingAverageLeaders(int teamGamesPlayed, int top = 3)
        {
            float minPlateAppearances = teamGamesPlayed * QualifyingPlateAppearancesPerGame;
            return batterStatsByPlayer
                .Where(kv => kv.Value.PlateAppearances >= minPlateAppearances)
                .OrderByDescending(kv => kv.Value.BattingAverage)
                .Take(top)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }

        /// <summary>홈런 TOP N. 규정 타석 제한 없음(누적 카운팅 스탯이라 표본 왜곡 문제가 없다).</summary>
        public List<(Player Player, BatterSeasonStats Stats)> GetHomeRunLeaders(int top = 3)
        {
            return batterStatsByPlayer
                .OrderByDescending(kv => kv.Value.HomeRuns)
                .Take(top)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }

        /// <summary>다승 TOP N. 규정 이닝 제한 없음(누적 카운팅 스탯).</summary>
        public List<(Player Player, PitcherSeasonStats Stats)> GetWinLeaders(int top = 3)
        {
            return pitcherStatsByPlayer
                .OrderByDescending(kv => kv.Value.Wins)
                .Take(top)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }

        /// <summary>방어율(ERA) TOP N(낮을수록 상위). teamGamesPlayed * 1.0 이상 이닝을 던진 투수만 대상이다 -
        /// 그렇지 않으면 1이닝 무실점(방어율 0.00) 같은 표본이 극히 적은 투수가 랭킹 최상단을 차지한다.</summary>
        public List<(Player Player, PitcherSeasonStats Stats)> GetEraLeaders(int teamGamesPlayed, int top = 3)
        {
            float minInnings = teamGamesPlayed * QualifyingInningsPerGame;
            return pitcherStatsByPlayer
                .Where(kv => kv.Value.InningsPitched >= minInnings)
                .OrderBy(kv => kv.Value.EarnedRunAverage)
                .Take(top)
                .Select(kv => (kv.Key, kv.Value))
                .ToList();
        }

        // ----- PlayBallController 이벤트 핸들러 -----

        /// <summary>
        /// [TASK-KBO-047] PlayBallController.RecordSeasonStatsFromEvents()(재생용 PlayEvent 큐를 역변환)와
        /// LeagueManager의 헤드리스 시즌 스킵(144경기 즉시 시뮬레이션)이 MatchEngine을 직접 구동하는
        /// 경로 양쪽 모두가 매 타석마다 호출하는 공개 진입점. 호출부가 "지금 진행 중인 PlayBallController
        /// 경기"라는 개념을 갖고 있지 않을 수도 있어(헤드리스 스킵), homeTeam/awayTeam을 인자로 직접 받는다.
        /// </summary>
        public void RecordAtBat(AtBatStepResult step, Team homeTeam, Team awayTeam)
        {
            if (step?.Batter == null || step.Pitcher == null || step.State == null) return;

            RecordBatterResult(step);
            RecordPitcherWorkload(step);
            RecordTodaysStarter(step, homeTeam, awayTeam);
        }

        private void RecordBatterResult(AtBatStepResult step)
        {
            var stats = GetOrCreateBatterStats(step.Batter);
            bool isWalk = step.Result == AtBatResult.Walk;
            bool isHit = step.Result == AtBatResult.Single || step.Result == AtBatResult.Double
                || step.Result == AtBatResult.Triple || step.Result == AtBatResult.HomeRun;

            if (!isWalk) stats.AtBats++;
            if (isHit) stats.Hits++;
            if (step.Result == AtBatResult.HomeRun) stats.HomeRuns++;
            if (isWalk) stats.Walks++;
        }

        private void RecordPitcherWorkload(AtBatStepResult step)
        {
            var stats = GetOrCreatePitcherStats(step.Pitcher);

            int outsThisPlay = step.State.Outs - previousOutsInHalfInning;
            if (outsThisPlay < 0) outsThisPlay = step.State.Outs; // 하프이닝이 막 새로 시작된 경우 보정
            previousOutsInHalfInning = step.HalfInningEnded ? 0 : step.State.Outs;

            stats.OutsRecorded += Mathf.Max(0, outsThisPlay);
            stats.EarnedRuns += step.RunsScoredThisPlay; // 단순화: 모든 실점을 자책점으로 취급(실책 미모델링)
        }

        private void RecordTodaysStarter(AtBatStepResult step, Team homeTeam, Team awayTeam)
        {
            var pitchingTeam = step.IsTopHalf ? homeTeam : awayTeam;
            if (pitchingTeam == Team.None || startersThisGame.ContainsKey(pitchingTeam)) return;

            startersThisGame[pitchingTeam] = step.Pitcher;
        }

        /// <summary>LeagueManager의 헤드리스 스킵 경로가 경기 1건이 끝날 때마다 호출하는 공개 진입점.
        /// MatchResult가 이미 HomeTeamName/AwayTeamName/WinnerTeamName을 자체적으로 담고 있어
        /// PlayBallController 참조 없이도 완전히 동작한다.</summary>
        public void RecordMatchCompleted(MatchResult result) => HandleMatchCompleted(result);

        private void HandleMatchCompleted(MatchResult result)
        {
            if (result?.WinnerTeamName != null)
            {
                string losingTeamName = result.WinnerTeamName == result.HomeTeamName ? result.AwayTeamName : result.HomeTeamName;
                ApplyDecision(result.WinnerTeamName, isWin: true);
                ApplyDecision(losingTeamName, isWin: false);
            }

            startersThisGame.Clear();
            previousOutsInHalfInning = 0;
        }

        private void ApplyDecision(string teamName, bool isWin)
        {
            if (!Enum.TryParse(teamName, out Team team)) return;
            if (!startersThisGame.TryGetValue(team, out var starter)) return;

            var stats = GetOrCreatePitcherStats(starter);
            if (isWin) stats.Wins++; else stats.Losses++;
        }

        private BatterSeasonStats GetOrCreateBatterStats(Player player)
        {
            if (!batterStatsByPlayer.TryGetValue(player, out var stats))
            {
                stats = new BatterSeasonStats();
                batterStatsByPlayer[player] = stats;
            }
            return stats;
        }

        private PitcherSeasonStats GetOrCreatePitcherStats(Player player)
        {
            if (!pitcherStatsByPlayer.TryGetValue(player, out var stats))
            {
                stats = new PitcherSeasonStats();
                pitcherStatsByPlayer[player] = stats;
            }
            return stats;
        }
    }
}
