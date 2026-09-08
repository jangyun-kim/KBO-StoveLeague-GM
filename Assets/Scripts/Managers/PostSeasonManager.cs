using System;
using System.Collections.Generic;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>KBO 스텝래더 포스트시즌의 4라운드.</summary>
    public enum PostSeasonRound
    {
        WildCard,
        SemiPlayoff,
        Playoff,
        KoreanSeries
    }

    /// <summary>
    /// 시리즈 1개(라운드 1개)의 진행 상태. "4위 팀 1승 어드밴티지" 같은 라운드별 특례는 이 클래스의
    /// WinsRequiredForHigherSeed/LowerSeed 두 숫자로만 표현된다 - 승자 판정 로직(IsDecided/Winner)은
    /// 라운드가 무엇이든 완전히 동일하게 동작하므로, 와일드카드만을 위한 별도 분기가 코드 어디에도 없다.
    /// </summary>
    public class PostSeasonSeries
    {
        public PostSeasonRound Round;
        public Team HigherSeed; // 정규시즌 순위가 더 높은(어드밴티지를 가질 수 있는) 쪽
        public Team LowerSeed;
        public int WinsRequiredForHigherSeed;
        public int WinsRequiredForLowerSeed;
        public int WinsHigherSeed;
        public int WinsLowerSeed;

        public bool IsDecided => WinsHigherSeed >= WinsRequiredForHigherSeed || WinsLowerSeed >= WinsRequiredForLowerSeed;
        public Team? Winner => WinsHigherSeed >= WinsRequiredForHigherSeed ? HigherSeed
            : WinsLowerSeed >= WinsRequiredForLowerSeed ? LowerSeed : (Team?)null;
    }

    /// <summary>
    /// 정규시즌(144경기) 종료 후 순위표 1~5위로 KBO 스텝래더 포스트시즌(와일드카드 -&gt; 준플레이오프 -&gt;
    /// 플레이오프 -&gt; 한국시리즈)을 진행하는 싱글톤. LeagueManager.OnSeasonFinalized를 구독해 유저가
    /// 5위 이내(IsUserPlayoffEligible)일 때만 자동으로 브래킷을 시작한다.
    ///
    /// 범위에 대한 의도적 예외: LeagueManager는 "유저 팀이 참여하는 경기만 실제로 시뮬레이션한다"는
    /// 범위 제한을 정규시즌 내내 지키지만(클래스 주석 참고), 포스트시즌은 스텝래더 구조상 유저가 몇 위로
    /// 진출하든 그보다 앞선 라운드(예: 유저가 3위라면 와일드카드전)가 먼저 끝나야 다음 상대가 정해진다.
    /// 그래서 이 매니저만은 4개 시리즈 전부(유저가 관여하지 않는 라운드 포함)를 MatchEngine으로 직접
    /// 시뮬레이션한다 - 유한(최대 4개 시리즈)하고 스텝래더 완성에 반드시 필요한 예외다.
    ///
    /// 포스트시즌 경기는 SeasonStatManager가 구독하는 PlayBallController를 거치지 않고
    /// MatchEngine.PlayFullMatch()를 직접 호출한다(LeagueManager.SimulateFixture()와 동일한 패턴) -
    /// 그 결과 포스트시즌 개인 기록은 정규시즌 타이틀 홀더 집계에 섞이지 않는다. 이는 버그가 아니라
    /// 실제 KBO도 정규시즌/포스트시즌 개인 기록을 별도로 취급하는 것과 일치하는 의도된 동작이다.
    /// </summary>
    public class PostSeasonManager : MonoBehaviour
    {
        public static PostSeasonManager Instance { get; private set; }

        [SerializeField] private LeagueManager leagueManager;
        [SerializeField] private SkillDB skillDB;
        [SerializeField] private EngineConfig engineConfig;

        // 라운드별 (상위시드 필요 승수, 하위시드 필요 승수). 와일드카드만 비대칭(1승 vs 2승)이고
        // 나머지는 실제 KBO 규정(준PO/PO 5전3선승, 한국시리즈 7전4선승)대로 대칭이다.
        private static readonly Dictionary<PostSeasonRound, (int higherSeedWinsNeeded, int lowerSeedWinsNeeded)> RoundFormat =
            new Dictionary<PostSeasonRound, (int, int)>
            {
                { PostSeasonRound.WildCard, (1, 2) },
                { PostSeasonRound.SemiPlayoff, (3, 3) },
                { PostSeasonRound.Playoff, (3, 3) },
                { PostSeasonRound.KoreanSeries, (4, 4) },
            };

        // 라운드에서 패배했을 때 확정되는 최종 순위. 챔피언(한국시리즈 승자)은 별도로 1위 처리한다.
        private static readonly Dictionary<PostSeasonRound, int> LoserFinalRank = new Dictionary<PostSeasonRound, int>
        {
            { PostSeasonRound.WildCard, 5 },
            { PostSeasonRound.SemiPlayoff, 4 },
            { PostSeasonRound.Playoff, 3 },
            { PostSeasonRound.KoreanSeries, 2 },
        };

        private Team[] seeds = new Team[5]; // [0]=1위 ... [4]=5위
        private readonly Dictionary<Team, int> finalRankByTeam = new Dictionary<Team, int>();

        public PostSeasonSeries CurrentSeries { get; private set; }
        public PostSeasonRound CurrentRound { get; private set; }
        public Team? ChampionTeam { get; private set; }
        public bool IsPostSeasonActive => CurrentSeries != null;

        /// <summary>시리즈 경기 1건이 끝날 때마다 발생한다(UI가 진행 상황을 갱신하는 데 쓴다).</summary>
        public event Action<PostSeasonSeries> OnSeriesGameCompleted;
        /// <summary>라운드가 끝나 다음 라운드로 넘어갈 때(또는 챔피언이 확정될 때) 발생한다.</summary>
        public event Action<PostSeasonRound> OnRoundAdvanced;
        /// <summary>한국시리즈 우승팀이 확정됐을 때 발생한다. SeasonRewardManager가 이를 구독해 시즌 결산을 시작한다.</summary>
        public event Action<Team> OnChampionDecided;

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

        /// <summary>LeagueCalendar와 동일한 이유(둘 다 DontDestroyOnLoad 싱글톤이라 Awake() 순서 미보장)로
        /// Start()에서 구독한다.</summary>
        private void Start()
        {
            if (leagueManager != null)
            {
                leagueManager.OnSeasonFinalized += HandleSeasonFinalized;
            }
        }

        private void OnDestroy()
        {
            if (leagueManager != null)
            {
                leagueManager.OnSeasonFinalized -= HandleSeasonFinalized;
            }
        }

        private void HandleSeasonFinalized()
        {
            if (leagueManager != null && leagueManager.IsUserPlayoffEligible)
            {
                BeginPostSeason();
            }
        }

        /// <summary>순위표 1~5위를 시드로 확정하고 와일드카드 결정전부터 브래킷을 시작한다.</summary>
        public void BeginPostSeason()
        {
            if (leagueManager == null) return;

            finalRankByTeam.Clear();
            ChampionTeam = null;

            var standings = leagueManager.GetStandings();
            for (int i = 0; i < seeds.Length; i++)
            {
                seeds[i] = i < standings.Count ? standings[i].Team : Team.None;
            }

            leagueManager.EnterPostSeason();
            StartSeries(PostSeasonRound.WildCard, seeds[3], seeds[4]);
        }

        private void StartSeries(PostSeasonRound round, Team higherSeed, Team lowerSeed)
        {
            var (higherNeeded, lowerNeeded) = RoundFormat[round];
            CurrentRound = round;
            CurrentSeries = new PostSeasonSeries
            {
                Round = round,
                HigherSeed = higherSeed,
                LowerSeed = lowerSeed,
                WinsRequiredForHigherSeed = higherNeeded,
                WinsRequiredForLowerSeed = lowerNeeded,
            };
        }

        /// <summary>
        /// 현재 시리즈의 다음 경기 1건을 즉시 시뮬레이션한다("빠른 진행" 방식 -
        /// LeagueManager.SimulateFixture()와 동일하게 MatchEngine.PlayFullMatch를 직접 호출한다).
        /// 시리즈가 이미 끝났거나 진행 중인 시리즈가 없으면 아무 일도 하지 않는다.
        /// </summary>
        public void PlayNextSeriesGame()
        {
            if (leagueManager == null || CurrentSeries == null || CurrentSeries.IsDecided) return;

            var homeRoster = leagueManager.ResolveRosterForTeam(CurrentSeries.HigherSeed);
            var awayRoster = leagueManager.ResolveRosterForTeam(CurrentSeries.LowerSeed);

            var engine = new MatchEngine(homeRoster, awayRoster, skillDB, engineConfig);
            var result = engine.PlayFullMatch(CurrentSeries.HigherSeed.ToString(), CurrentSeries.LowerSeed.ToString(), isPostSeason: true);

            if (result.WinnerTeamName == CurrentSeries.HigherSeed.ToString()) CurrentSeries.WinsHigherSeed++;
            else if (result.WinnerTeamName == CurrentSeries.LowerSeed.ToString()) CurrentSeries.WinsLowerSeed++;
            // 무승부(연장 상한 도달)는 어느 쪽 승수도 올리지 않는다 - 다음 PlayNextSeriesGame() 호출이
            // 같은 시리즈를 이어가므로 사실상 "재경기"로 처리된다.

            OnSeriesGameCompleted?.Invoke(CurrentSeries);

            if (CurrentSeries.IsDecided)
            {
                AdvanceAfterSeriesDecided();
            }
        }

        /// <summary>시리즈가 끝날 때까지 PlayNextSeriesGame()을 반복한다(빠른 진행 편의 메서드).</summary>
        public void PlayCurrentSeriesToCompletion()
        {
            while (CurrentSeries != null && !CurrentSeries.IsDecided)
            {
                PlayNextSeriesGame();
            }
        }

        private void AdvanceAfterSeriesDecided()
        {
            var winner = CurrentSeries.Winner.Value;
            var loser = winner == CurrentSeries.HigherSeed ? CurrentSeries.LowerSeed : CurrentSeries.HigherSeed;
            finalRankByTeam[loser] = LoserFinalRank[CurrentRound];

            switch (CurrentRound)
            {
                case PostSeasonRound.WildCard:
                    StartSeries(PostSeasonRound.SemiPlayoff, seeds[2], winner);
                    break;
                case PostSeasonRound.SemiPlayoff:
                    StartSeries(PostSeasonRound.Playoff, seeds[1], winner);
                    break;
                case PostSeasonRound.Playoff:
                    StartSeries(PostSeasonRound.KoreanSeries, seeds[0], winner);
                    break;
                case PostSeasonRound.KoreanSeries:
                    finalRankByTeam[winner] = 1;
                    ChampionTeam = winner;
                    CurrentSeries = null;
                    leagueManager?.EnterStoveLeague();
                    OnChampionDecided?.Invoke(winner);
                    return; // 다음 라운드가 없으므로 OnRoundAdvanced를 발생시키지 않는다
            }

            OnRoundAdvanced?.Invoke(CurrentRound);
        }

        /// <summary>team의 최종 순위를 반환한다. 포스트시즌에서 탈락/우승이 확정된 팀만 기록돼 있으므로,
        /// 없으면(포스트시즌에 아예 진출하지 못한 6~10위 팀) regularSeasonRankFallback을 그대로 반환한다.</summary>
        public int GetFinalRank(Team team, int regularSeasonRankFallback) =>
            finalRankByTeam.TryGetValue(team, out var rank) ? rank : regularSeasonRankFallback;

        /// <summary>다음 시즌을 위해 브래킷 상태를 전부 비운다. SeasonRollover가 시즌 전환 시 호출한다.</summary>
        public void ResetForNextSeason()
        {
            CurrentSeries = null;
            ChampionTeam = null;
            finalRankByTeam.Clear();
            Array.Clear(seeds, 0, seeds.Length);
        }
    }
}
