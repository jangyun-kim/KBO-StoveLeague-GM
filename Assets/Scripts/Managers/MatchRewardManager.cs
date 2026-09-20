using System;
using System.Collections.Generic;
using KBOManager.Controllers;
using KBOManager.Engine;
using KBOManager.Models;
using UnityEngine;
using Random = UnityEngine.Random;

namespace KBOManager.Managers
{
    /// <summary>경기 1회에 지급된 보상 내역. InGameUIController가 결과창에 표시하는 데 쓴다.</summary>
    public class MatchRewardResult
    {
        public bool Won;
        public bool Draw;
        public int ScoutTicketGained;
        public List<Item> ItemsGained = new List<Item>();
    }

    /// <summary>
    /// 경기 종료(PlayBallController.OnMatchCompleted) 시 승/무/패에 따라 차등 보상을 즉시
    /// GameManager(ScoutTicket, ItemInventory)에 지급하는 싱글톤. [경기 -> 재화/아이템 획득] 구간을
    /// 담당하는 코어 루프의 한 축이다.
    /// </summary>
    public class MatchRewardManager : MonoBehaviour
    {
        public static MatchRewardManager Instance { get; private set; }

        [Header("References")]
        [Tooltip("보상 지급의 트리거가 되는 PlayBallController.OnMatchCompleted를 구독한다.")]
        [SerializeField] private PlayBallController playBallController;
        [Tooltip("무작위 아이템 드롭 시 후보 템플릿을 가져올 DB.")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Reward Amounts")]
        [SerializeField] private int winScoutTicketReward = 100;
        [SerializeField] private int loseOrDrawScoutTicketReward = 30;
        [SerializeField] private int winMinItemDrop = 1;
        [SerializeField] private int winMaxItemDrop = 3;
        [SerializeField] private int loseOrDrawItemDrop = 1;

        [Header("Fan Sentiment (TASK-KBO-049, GDD 미확정 - 임시값)")]
        [Tooltip("연속 패배가 이 값 이상이 되면(포함) 패배할 때마다 팬심을 깎는다.")]
        [SerializeField] private int losingStreakThresholdForFanSentimentDrop = 3;
        [Tooltip("연패 조건 충족 시 1경기당 기본 팬심 하락치(치어리더 SentimentDefense로 방어되기 전 값).")]
        [SerializeField] private int baseFanSentimentDropOnLosingStreak = 5;

        // [TASK-KBO-050 확정 수치] 15_team_power_policy.md "치어리더 B안" 절 참고 - GDD 미확정 임시값이
        // 아니라 이번 명령서가 못박은 고정 규칙이므로 인스펙터로 노출하지 않는다.
        private const int FanSentimentPenaltyThreshold = 50;
        private const float FanSentimentPenaltyMultiplier = 0.8f;

        /// <summary>보상 지급이 끝날 때마다 발생. InGameUIController 등이 구독해 결과창에 표시한다.</summary>
        public event Action<MatchRewardResult> OnRewardGranted;

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

        private void HandleMatchCompleted(MatchResult result)
        {
            GrantRewardForMatch(result);
        }

        /// <summary>
        /// LeagueManager.Instance.UserTeam 기준으로 승/무/패를 판별해 즉시 보상을 지급한다.
        /// 승리: 영입권 100 + 강화 재료 1~3장. 무승부/패배: 30 + 1장(치어리더/팬심 배율 적용 전 기준값).
        /// [TASK-KBO-049] 치어리더 B안(상시 경제/멘탈 효과, "스토브리그 로비 연산")을 이 결산 지점에
        /// 연동한다 - 유저 팀이 홈 경기에서 승리했다면 EconomicBonusRate를 재화 보상에 곱하고(경제),
        /// 유저 팀이 패배했다면 연패 카운트를 갱신하고 임계치 이상이면 SentimentDefense로 방어된
        /// 팬심 하락을 적용한다(멘탈).
        /// [TASK-KBO-050] 팬심(FanSentiment)이 50 미만이면 유저 팀의 모든 홈 경기(승/무/패 무관)
        /// 기본 보상에 0.8배 페널티가 추가로 적용된다(ApplyCheerleaderEconomicBonus() 참고) - 치어리더의
        /// "연패 시 팬심 방어" 효과가 이 페널티를 간접적으로 막아주는 경제적 이득으로 이어진다.
        /// MatchEngine의 확률/스탯 산출과는 전혀 무관한, 순수 결산 단계의 후처리다(가드레일 준수).
        /// </summary>
        public MatchRewardResult GrantRewardForMatch(MatchResult result)
        {
            if (result == null || GameManager.Instance == null) return null;

            bool isDraw = result.WinnerTeamName == null;
            bool won = !isDraw && LeagueManager.Instance != null
                && result.WinnerTeamName == LeagueManager.Instance.UserTeam.ToString();
            bool isUserTeamHome = LeagueManager.Instance != null
                && result.HomeTeamName == LeagueManager.Instance.UserTeam.ToString();

            int scoutReward = won ? winScoutTicketReward : loseOrDrawScoutTicketReward;
            scoutReward = ApplyCheerleaderEconomicBonus(scoutReward, won, isUserTeamHome);
            GameManager.Instance.ScoutTicket += scoutReward;

            UpdateLosingStreakAndFanSentiment(won, isDraw);

            int itemCount = won ? Random.Range(winMinItemDrop, winMaxItemDrop + 1) : loseOrDrawItemDrop;
            var grantedItems = new List<Item>();

            for (int i = 0; i < itemCount; i++)
            {
                var item = RollRandomItem();
                if (item == null) continue;

                GameManager.Instance.AddItemToInventory(item);
                grantedItems.Add(item);
            }

            var rewardResult = new MatchRewardResult
            {
                Won = won,
                Draw = isDraw,
                ScoutTicketGained = scoutReward,
                ItemsGained = grantedItems,
            };

            OnRewardGranted?.Invoke(rewardResult);
            return rewardResult;
        }

        /// <summary>
        /// [TASK-KBO-049][경제 효과][TASK-KBO-050 확장] 홈 경기 보상에만 적용되는 두 배율을 하나로
        /// 합산해 baseReward에 곱한다(원정 경기면 둘 다 적용되지 않고 baseReward를 그대로 반환).
        /// ① 팬심 페널티 - 유저 팀의 홈 경기라면 승/무/패와 무관하게, FanSentiment가
        /// FanSentimentPenaltyThreshold(50) 미만일 때 FanSentimentPenaltyMultiplier(0.8배)가 적용된다
        /// (TASK-KBO-050, 정확히 50이면 미적용 - 7항 경계 조건).
        /// ② 치어리더 경제 증폭 - 유저 팀이 "홈 경기에서 승리"했을 때만
        /// GameManager.EquippedCheerleader.EconomicBonusRate가 곱해진다(미장착/조건 미충족이면 1.0f,
        /// TASK-KBO-049 원래 규칙 그대로 유지).
        /// 연산 순서(명령서 3항 고정): 최종 보상 = RoundToInt((기본 보상 * 팬심 페널티 배율) *
        /// 치어리더 경제 증폭 배율) - 중간에 별도로 반올림하지 않고 한 번만 RoundToInt를 적용해
        /// 오차 누적/재화 증발을 막는다(가드레일).
        /// </summary>
        private int ApplyCheerleaderEconomicBonus(int baseReward, bool won, bool isUserTeamHome)
        {
            if (!isUserTeamHome) return baseReward;

            int fanSentiment = GameManager.Instance.FanSentiment;
            float sentimentPenaltyMultiplier = fanSentiment < FanSentimentPenaltyThreshold ? FanSentimentPenaltyMultiplier : 1.0f;

            float economicBonusRate = won ? (GameManager.Instance.EquippedCheerleader?.EconomicBonusRate ?? 1.0f) : 1.0f;

            int finalReward = Mathf.RoundToInt((baseReward * sentimentPenaltyMultiplier) * economicBonusRate);

#if UNITY_EDITOR
            Debug.Log($"[TASK-KBO-049/050][경제 효과] 홈 경기 보상 결산 - 적용 전(기본 보상): {baseReward}, " +
                $"FanSentiment: {fanSentiment}(페널티 배율: {sentimentPenaltyMultiplier}), " +
                $"EconomicBonusRate: {economicBonusRate}(승리 여부: {won}), 최종 보상: {finalReward}");
#endif

            return finalReward;
        }

        /// <summary>
        /// [TASK-KBO-049][멘탈 효과] 유저 팀이 패배하면 GameManager.LosingStreak을 1 증가시키고,
        /// 그 값이 losingStreakThresholdForFanSentimentDrop(기본 3) 이상이면
        /// Mathf.Max(0, baseFanSentimentDropOnLosingStreak - SentimentDefense)만큼 팬심을 깎는다
        /// (승리 또는 무승부면 연패가 끊긴 것으로 보고 0으로 리셋 - 하락도 없음). 치어리더 미장착 시
        /// SentimentDefense는 0으로 취급되어 기존 밸런스와 동일하게 동작한다(7항 경계 조건). "연패
        /// (예: 3연패 이상)" 트리거 해석 - 정확한 발동 방식이 GDD에 없어, 임계치 도달 이후 매 패배마다
        /// (3연패째, 4연패째, ...) 반복 적용하는 방식을 잠정 채택했다([TBD], 완료 보고서 F 섹션 참고).
        /// </summary>
        private void UpdateLosingStreakAndFanSentiment(bool won, bool isDraw)
        {
            if (won || isDraw)
            {
                GameManager.Instance.LosingStreak = 0;
                return;
            }

            GameManager.Instance.LosingStreak++;

            if (GameManager.Instance.LosingStreak < losingStreakThresholdForFanSentimentDrop) return;

            int sentimentDefense = GameManager.Instance.EquippedCheerleader?.SentimentDefense ?? 0;
            int actualDrop = Mathf.Max(0, baseFanSentimentDropOnLosingStreak - sentimentDefense);

#if UNITY_EDITOR
            Debug.Log($"[TASK-KBO-049][멘탈 효과] {GameManager.Instance.LosingStreak}연패 팬심 결산 - " +
                $"적용 전 하락폭: {baseFanSentimentDropOnLosingStreak}, SentimentDefense: {sentimentDefense}, " +
                $"적용 후 하락폭: {actualDrop}");
#endif

            GameManager.Instance.FanSentiment -= actualDrop;
        }

        private Item RollRandomItem()
        {
            if (itemDatabase == null || itemDatabase.AllTemplates.Count == 0) return null;

            var template = itemDatabase.AllTemplates[Random.Range(0, itemDatabase.AllTemplates.Count)];
            return new Item(Guid.NewGuid().ToString(), template);
        }
    }
}
