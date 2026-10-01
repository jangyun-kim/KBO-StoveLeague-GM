using System;
using System.Collections.Generic;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>시즌 결산 리포트 1건. SeasonEndReportUIController가 이 데이터를 그대로 화면에 그린다.</summary>
    public class SeasonEndReport
    {
        public int FinalRank;
        public int LiveNormalTicketGained;
        public int GameGoldGained;
        /// <summary>확정팩을 받았다면 그 최소 등급 이름(예: "SIGNATURE"), 없으면 null.</summary>
        public string GuaranteedPackGrade;
        /// <summary>(부문명, 선수 이름, 기록값) 목록 - 타이틀 홀더(투타 각 부문 1위) 명단.</summary>
        public List<(string Category, string PlayerName, string ValueLabel)> TitleHolders = new List<(string, string, string)>();
    }

    /// <summary>
    /// 한국시리즈 우승팀이 가려지면(PostSeasonManager.OnChampionDecided) 유저 팀의 최종 순위를 산출하고
    /// 순위에 따라 차등 보상을 지급하는 싱글톤. 포스트시즌에 진출하지 못한 시즌(유저가 6~10위)에도
    /// LeagueManager.OnSeasonFinalized를 별도로 구독해 동일한 보상 로직을 태운다 - "포스트시즌에 못
    /// 나가면 시즌 결산 자체가 없다"는 허점을 만들지 않기 위함이다.
    /// </summary>
    public class SeasonRewardManager : MonoBehaviour
    {
        public static SeasonRewardManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private LeagueManager leagueManager;
        [SerializeField] private PostSeasonManager postSeasonManager;
        [SerializeField] private SeasonStatManager seasonStatManager;
        [Tooltip("확정팩 발급에 ScoutManager.RollGuaranteed()를 재사용한다.")]
        [SerializeField] private ScoutManager scoutManager;

        [Header("Reward Table (GDD 미확정 - 임시값)")]
        [Tooltip("1위(우승) 보상. [TASK-KBO-129] GDD 재화 절이 \"라이브 일반 영입권\"의 정식 수급처로 " +
                 "\"리그 모드 반복과제 보상\"을 명시하고 있어(수급 난이도 하) 시즌 순위 보상도 이 재화로 " +
                 "맞췄다(구 ScoutTicket 폐기에 따른 최소 연쇄 수정).")]
        [SerializeField] private int championLiveNormalTicket = 5000;
        [SerializeField] private Grade championGuaranteedGrade = Grade.SIGNATURE;
        [Tooltip("2~9위 보상은 1위 값에서 이 값까지 순위에 비례해 선형으로 줄어든다(확정팩 없음).")]
        [SerializeField] private int midTierMinimumLiveNormalTicket = 500;
        [Tooltip("10위(꼴찌) 위로금 - 프리미엄이 아닌 일반 재화(GameGold)로 지급한다.")]
        [SerializeField] private int lastPlaceConsolationGameGold = 3000;
        [Tooltip("꼴찌에게 지급하는 '슈퍼 루키 확정팩'의 최소 등급 - 우승팩보다는 낮게 잡는다.")]
        [SerializeField] private Grade lastPlaceGuaranteedGrade = Grade.GOLDEN_GLOVE;

        private const int TotalTeamCount = 10;

        /// <summary>시즌 결산 리포트가 준비될 때마다 발생한다. SeasonEndReportUIController가 이를 구독해 팝업을 띄운다.</summary>
        public event Action<SeasonEndReport> OnSeasonEndReportReady;

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

        private void Start()
        {
            // 둘 다 DontDestroyOnLoad 싱글톤이라 Awake() 순서가 보장되지 않으므로 Start()에서 구독한다
            // (이 세션에서 이미 확립된 패턴 - LeagueCalendar/PostSeasonManager와 동일).
            if (postSeasonManager != null) postSeasonManager.OnChampionDecided += HandleChampionDecided;
            if (leagueManager != null) leagueManager.OnSeasonFinalized += HandleSeasonFinalizedWithoutPlayoffs;
        }

        private void OnDestroy()
        {
            if (postSeasonManager != null) postSeasonManager.OnChampionDecided -= HandleChampionDecided;
            if (leagueManager != null) leagueManager.OnSeasonFinalized -= HandleSeasonFinalizedWithoutPlayoffs;
        }

        private void HandleChampionDecided(Team champion) => GrantSeasonEndReward();

        /// <summary>유저가 포스트시즌에 진출하지 못했으면(6~10위) PostSeasonManager가 아예 동작하지 않으므로
        /// OnChampionDecided가 발생하지 않는다 - 그 경우를 위해 정규시즌 종료 시점에도 별도로 확인한다.</summary>
        private void HandleSeasonFinalizedWithoutPlayoffs()
        {
            if (leagueManager != null && !leagueManager.IsUserPlayoffEligible)
            {
                GrantSeasonEndReward();
            }
        }

        /// <summary>유저 팀의 최종 순위를 산출해 보상을 지급하고 리포트를 발행한다.</summary>
        public void GrantSeasonEndReward()
        {
            if (GameManager.Instance == null || leagueManager == null) return;

            int regularSeasonRank = leagueManager.UserFinalRank ?? TotalTeamCount;
            int finalRank = postSeasonManager != null
                ? postSeasonManager.GetFinalRank(leagueManager.UserTeam, regularSeasonRank)
                : regularSeasonRank;

            var report = new SeasonEndReport { FinalRank = finalRank };

            if (finalRank <= 1)
            {
                report.LiveNormalTicketGained = championLiveNormalTicket;
                report.GuaranteedPackGrade = CardGrowthRules.DisplayName(championGuaranteedGrade);
                GameManager.Instance.LiveNormalTicket += championLiveNormalTicket;
                GrantGuaranteedPack(championGuaranteedGrade);
            }
            else if (finalRank >= TotalTeamCount)
            {
                report.GameGoldGained = lastPlaceConsolationGameGold;
                report.GuaranteedPackGrade = CardGrowthRules.DisplayName(lastPlaceGuaranteedGrade);
                GameManager.Instance.GameGold += lastPlaceConsolationGameGold;
                GrantGuaranteedPack(lastPlaceGuaranteedGrade);
            }
            else
            {
                // 2위~9위: 순위가 낮아질수록 championLiveNormalTicket에서 midTierMinimumLiveNormalTicket까지 선형으로 줄어든다.
                float t = (finalRank - 1) / (float)(TotalTeamCount - 2);
                report.LiveNormalTicketGained = Mathf.RoundToInt(Mathf.Lerp(championLiveNormalTicket, midTierMinimumLiveNormalTicket, t));
                GameManager.Instance.LiveNormalTicket += report.LiveNormalTicketGained;
            }

            PopulateTitleHolders(report);

            OnSeasonEndReportReady?.Invoke(report);
        }

        private void GrantGuaranteedPack(Grade minimumGrade)
        {
            if (scoutManager == null) return;

            var player = scoutManager.RollGuaranteed(minimumGrade);
            if (player != null) GameManager.Instance.AddPlayerToInventory(player);
        }

        private void PopulateTitleHolders(SeasonEndReport report)
        {
            if (seasonStatManager == null || leagueManager == null) return;

            int teamGamesPlayed = leagueManager.PlayedGameCount;

            AddLeaderIfAny(report, "타율", seasonStatManager.GetBattingAverageLeaders(teamGamesPlayed, 1), s => $"{s.BattingAverage:F3}");
            AddLeaderIfAny(report, "홈런", seasonStatManager.GetHomeRunLeaders(1), s => $"{s.HomeRuns}개");
            AddLeaderIfAny(report, "다승", seasonStatManager.GetWinLeaders(1), s => $"{s.Wins}승");
            AddLeaderIfAny(report, "방어율", seasonStatManager.GetEraLeaders(teamGamesPlayed, 1), s => $"{s.EarnedRunAverage:F2}");
        }

        private static void AddLeaderIfAny<TStats>(SeasonEndReport report, string category,
            List<(Player Player, TStats Stats)> leaders, Func<TStats, string> formatValue)
        {
            if (leaders == null || leaders.Count == 0) return;

            var (player, stats) = leaders[0];
            string playerName = player?.Template != null ? player.Template.PlayerName : "알 수 없음";
            report.TitleHolders.Add((category, playerName, formatValue(stats)));
        }
    }
}
