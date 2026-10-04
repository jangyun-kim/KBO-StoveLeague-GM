using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>[TASK-KBO-189] 리그 단계 비례 시즌 재료 보상(포인트 · 성장 코인 · 특훈권 · 트로피).</summary>
        public LeagueMaterialReward MaterialReward;
        /// <summary>(부문명, 선수 이름, 기록값) 목록 - 타이틀 홀더(투타 각 부문 1위) 명단.</summary>
        public List<(string Category, string PlayerName, string ValueLabel)> TitleHolders = new List<(string, string, string)>();

        // ---- [TASK-KBO-190] 시즌 완주 결산
        /// <summary>개인 타이틀 6부문(타격왕 · 홈런왕 · 타점왕 · 다승왕 · 평균자책점 · 탈삼진) - SeasonAwardRules.</summary>
        public List<SeasonTitle> Titles = new List<SeasonTitle>();
        /// <summary>우리 구단 수상 보너스(1건당 트로피 +1 · 성장 코인 +100 · 포인트 +30,000).</summary>
        public LeagueMaterialReward TitleBonus;
        public int RegularSeasonRank;
        public Team Champion = Team.None;
        public LeagueTier Tier;
        /// <summary>다음 시즌 승격 예정(정규시즌 1위 또는 한국시리즈 우승) - [다음 시즌 시작]에서 실제 적용.</summary>
        public bool PromotionEarned;
        public LeagueTier NextTier;
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

        /// <summary>[TASK-KBO-190] 이번 시즌 결산(보상 지급)을 이미 했는지 - 이벤트 · 시즌 완주 화면 중복 지급 방지.</summary>
        public bool HasGrantedThisSeason { get; private set; }
        public SeasonEndReport LastReport { get; private set; }

        /// <summary>[TASK-KBO-190] 시즌 결산을 한 번만 지급한다(이미 지급했으면 그 리포트 반환).</summary>
        public SeasonEndReport GrantSeasonEndRewardOnce()
        {
            if (!HasGrantedThisSeason) return GrantSeasonEndReward();
            // 세이브 복원 등으로 지급 표시만 있고 리포트가 없으면 - 지급 없이 결산 화면용 리포트만 다시 만든다.
            return LastReport ??= BuildReport(apply: false);
        }

        /// <summary>[TASK-KBO-190] 새 시즌 전환 시(SeasonRollover) 결산 상태를 비운다.</summary>
        public void ResetForNextSeason()
        {
            HasGrantedThisSeason = false;
            LastReport = null;
        }

        /// <summary>[TASK-KBO-190] 세이브 복원 - 결산까지 마친 시즌이면 중복 지급하지 않게 표시만 복원한다(리포트 본문은 다시 집계하지 않는다).</summary>
        public void RestoreGrantedFlag(bool granted) => HasGrantedThisSeason = granted;

        private void HandleChampionDecided(Team champion) => GrantSeasonEndRewardOnce();

        /// <summary>유저가 포스트시즌에 진출하지 못했으면(6~10위) PostSeasonManager가 아예 동작하지 않으므로
        /// OnChampionDecided가 발생하지 않는다 - 그 경우를 위해 정규시즌 종료 시점에도 별도로 확인한다.</summary>
        private void HandleSeasonFinalizedWithoutPlayoffs()
        {
            if (leagueManager != null && !leagueManager.IsUserPlayoffEligible)
            {
                GrantSeasonEndRewardOnce();
            }
        }

        /// <summary>유저 팀의 최종 순위를 산출해 보상을 지급하고 리포트를 발행한다.</summary>
        public SeasonEndReport GrantSeasonEndReward() => BuildReport(apply: true);

        /// <summary>[TASK-KBO-190] apply = false면 재화는 건드리지 않고 결산 리포트(순위 · 타이틀 · 승격 예정)만 만든다.</summary>
        private SeasonEndReport BuildReport(bool apply)
        {
            if (GameManager.Instance == null || leagueManager == null) return null;

            int regularSeasonRank = leagueManager.UserFinalRank ?? TotalTeamCount;
            int finalRank = postSeasonManager != null
                ? postSeasonManager.GetFinalRank(leagueManager.UserTeam, regularSeasonRank)
                : regularSeasonRank;

            var champion = postSeasonManager != null ? postSeasonManager.ChampionTeam : null;
            var report = new SeasonEndReport
            {
                FinalRank = finalRank,
                RegularSeasonRank = regularSeasonRank,
                Champion = champion ?? Team.None,
                Tier = leagueManager.CurrentTier,
                PromotionEarned = SeasonCycle.ShouldPromote(regularSeasonRank, champion, leagueManager.UserTeam, leagueManager.CurrentTier),
            };
            report.NextTier = report.PromotionEarned ? LeagueTierTable.Next(report.Tier) : report.Tier;

            if (finalRank <= 1)
            {
                report.LiveNormalTicketGained = championLiveNormalTicket;
                report.GuaranteedPackGrade = CardGrowthRules.DisplayName(championGuaranteedGrade);
                if (apply)
                {
                    GameManager.Instance.LiveNormalTicket += championLiveNormalTicket;
                    GrantGuaranteedPack(championGuaranteedGrade);
                }
            }
            else if (finalRank >= TotalTeamCount)
            {
                report.GameGoldGained = lastPlaceConsolationGameGold;
                report.GuaranteedPackGrade = CardGrowthRules.DisplayName(lastPlaceGuaranteedGrade);
                if (apply)
                {
                    GameManager.Instance.GameGold += lastPlaceConsolationGameGold;
                    GrantGuaranteedPack(lastPlaceGuaranteedGrade);
                }
            }
            else
            {
                // 2위~9위: 순위가 낮아질수록 championLiveNormalTicket에서 midTierMinimumLiveNormalTicket까지 선형으로 줄어든다.
                float t = (finalRank - 1) / (float)(TotalTeamCount - 2);
                report.LiveNormalTicketGained = Mathf.RoundToInt(Mathf.Lerp(championLiveNormalTicket, midTierMinimumLiveNormalTicket, t));
                if (apply) GameManager.Instance.LiveNormalTicket += report.LiveNormalTicketGained;
            }

            // [TASK-KBO-189] 리그 단계 비례 시즌 재료 보상(우승 시 트로피 · 성장 코인 · 특훈권 대량)
            var material = LeagueMaterialRewards.ForSeason(leagueManager.CurrentTier, finalRank);
            report.MaterialReward = material;
            if (apply)
            {
                var gm = GameManager.Instance;
                gm.GameGold += material.Points;
                gm.GrowthCoin += material.GrowthCoin;
                gm.TrainingTicket += material.TrainingTicket;
                gm.Trophy += material.Trophy;
                gm.SkillChangeTicket += material.SkillChangeTicket; // [TASK-KBO-190]
                gm.PremiumSkillChangeTicket += material.PremiumSkillChangeTicket;
            }

            PopulateTitleHolders(report, apply);

            HasGrantedThisSeason = true;
            LastReport = report;
            if (apply) OnSeasonEndReportReady?.Invoke(report);
            return report;
        }

        private void GrantGuaranteedPack(Grade minimumGrade)
        {
            if (scoutManager == null) return;

            var player = scoutManager.RollGuaranteed(minimumGrade);
            if (player != null) GameManager.Instance.AddPlayerToInventory(player);
        }

        private void PopulateTitleHolders(SeasonEndReport report, bool apply)
        {
            if (seasonStatManager == null || leagueManager == null) return;

            int teamGamesPlayed = leagueManager.PlayedGameCount;

            // [TASK-KBO-190] 6부문 타이틀 시상식 - 우리 구단(보유 카드) 수상 1건마다 트로피 +1 · 성장 코인 +100 · 포인트 +30,000
            var gm = GameManager.Instance;
            report.Titles = SeasonAwardRules.Compute(seasonStatManager.AllBatterStats, seasonStatManager.AllPitcherStats, teamGamesPlayed,
                p => gm != null && (gm.Roster.Contains(p) || gm.Inventory.Contains(p)));
            foreach (var title in report.Titles) report.TitleHolders.Add((title.Category, title.PlayerName, title.ValueLabel));
            report.TitleBonus = SeasonAwardRules.UserBonus(report.Titles);
            if (apply && gm != null && !report.TitleBonus.IsEmpty)
            {
                gm.Trophy += report.TitleBonus.Trophy;
                gm.GrowthCoin += report.TitleBonus.GrowthCoin;
                gm.GameGold += report.TitleBonus.Points;
            }
        }
    }
}
