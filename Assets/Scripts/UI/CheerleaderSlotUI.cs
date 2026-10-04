using System;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-058] 치어리더 인벤토리 한 슬롯의 표시 + 장착/해제 버튼 컴포넌트. PlayerCardUI.Setup()과
    /// 동일한 관례로 시뮬레이션/저장 로직은 전혀 갖지 않고, Initialize()로 주입된 데이터만 그린다.
    /// </summary>
    public class CheerleaderSlotUI : MonoBehaviour
    {
        [Header("Text")]
        [SerializeField] private Text nameText;
        [SerializeField] private Text gradeText;
        [Tooltip("[TASK-KBO-071] A안 경기 조건부 전력 보정(ConditionBuff)만 표시한다 - 이전에는" +
                 " ClutchMultiplier도 함께 표시해 clutchText와 중복되던 것을 정리했다.")]
        [SerializeField] private Text buffText;
        [Tooltip("B안 상시 경제 효과(EconomicBonusRate).")]
        [SerializeField] private Text economicRateText;
        [Tooltip("[TASK-KBO-070] C안 득점권 클러치 배율(ClutchMultiplier) 표시. [TASK-KBO-071] " +
                 "buffText와의 중복 표시 문제는 buffText 쪽에서 ClutchMultiplier 표기를 제거해 " +
                 "해소했다 - 이제 클러치 배율은 이 필드에서만 보여준다.")]
        [SerializeField] private Text clutchText;
        [Tooltip("[TASK-KBO-070] B안 팬심 방어(SentimentDefense) 표시.")]
        [SerializeField] private Text sentimentText;

        [Header("Equip/Unequip")]
        [Tooltip("장착/해제를 한 버튼으로 겸한다 - 현재 장착 여부에 따라 라벨/동작이 바뀐다.")]
        [SerializeField] private Button equipButton;
        [SerializeField] private Text equipButtonLabel;

        [Header("Card Visual (TASK-KBO-177 - 비워두면 생략, 구 슬롯 템플릿 호환)")]
        [Tooltip("티어 뱃지 문구(LIVE / ICON / LEGEND).")]
        [SerializeField] private Text tierBadgeText;
        [Tooltip("티어 뱃지 배경 - 티어별 색으로 칠한다.")]
        [SerializeField] private Image tierBadgeImage;
        [Tooltip("소속 구단 + 활동 기간(예: 'KIA · 2020~2021').")]
        [SerializeField] private Text teamPeriodText;
        [Tooltip("카드 배경 - 장착 중이면 강조색.")]
        [SerializeField] private Image cardBackground;
        [SerializeField] private Color equippedBackgroundColor = new Color(1f, 0.95f, 0.75f);
        [SerializeField] private Color normalBackgroundColor = Color.white;

        public Cheerleader BoundCheerleader { get; private set; }

        /// <summary>[TASK-KBO-177] 뱃지 표기 - LIVE_NORMAL은 "LIVE". [TASK-KBO-178] LIVE_EPIC도 "LIVE"로 통일
        /// (CheerleaderGradeLabels.Display에 위임).</summary>
        public static string TierLabel(CheerleaderGrade grade) => grade.Display();

        public static Color TierColor(CheerleaderGrade grade) => grade switch
        {
            CheerleaderGrade.ICON => new Color(0.55f, 0.35f, 0.85f),
            CheerleaderGrade.LEGEND => new Color(0.95f, 0.7f, 0.1f),
            CheerleaderGrade.SEASON_LIMITED => new Color(0.85f, 0.25f, 0.3f),
            _ => new Color(0.45f, 0.55f, 0.65f)
        };

        /// <summary>슬롯 데이터를 채우고 버튼을 연결한다. data가 null이면 빈 슬롯으로 표시한다.
        /// isEquipped가 true면 버튼 라벨이 "해제"로 바뀌고 클릭 시 onUnequip이, false면 "장착"으로
        /// 바뀌고 클릭 시 onEquip(이 슬롯의 Cheerleader)이 호출된다.</summary>
        public void Initialize(Cheerleader data, bool isEquipped, Action<Cheerleader> onEquip, Action onUnequip)
        {
            BoundCheerleader = data;

            if (data == null)
            {
                Clear();
                return;
            }

            if (nameText != null) nameText.text = $"{data.Name} <color=#FFD54A>{CheerGrowth.StarBadge(data)}</color>{(data.ReinforceLevel > 0 ? $" +{CheerGrowth.Reinforce(data)}강" : "")}"; // [TASK-KBO-187] ★ 각성 · 강화 배지
            // [TASK-KBO-175] 단일 연도 대신 "소속 구단 + 활동 기간"을 등급 옆에 표기(예: "LEGEND · KIA 2020~2021").
            if (gradeText != null)
            {
                string affiliation = data.AffiliationLabel;
                gradeText.text = string.IsNullOrEmpty(affiliation) ? data.Grade.Display() : $"{data.Grade.Display()} · {affiliation}";
            }
            if (buffText != null) buffText.text = $"전력 보정: +{data.ConditionBuff}";
            if (economicRateText != null) economicRateText.text = $"관중 수익 x{data.EconomicBonusRate:F2}";
            if (clutchText != null) clutchText.text = $"클러치: {data.ClutchMultiplier}x";
            if (sentimentText != null) sentimentText.text = $"팬심 방어: +{data.SentimentDefense}";

            if (tierBadgeText != null) tierBadgeText.text = TierLabel(data.Grade);
            if (tierBadgeImage != null) tierBadgeImage.color = TierColor(data.Grade);
            if (teamPeriodText != null)
            {
                teamPeriodText.text = data.Team == Team.None
                    ? "구단 정보 없음"
                    : string.IsNullOrEmpty(data.ActivePeriod) ? data.Team.ToString() : $"{data.Team} · {data.ActivePeriod}";
            }
            if (cardBackground != null) cardBackground.color = isEquipped ? equippedBackgroundColor : normalBackgroundColor;

            if (equipButtonLabel != null) equipButtonLabel.text = isEquipped ? "해제" : "장착";

            if (equipButton != null)
            {
                equipButton.onClick.RemoveAllListeners();
                equipButton.onClick.AddListener(() =>
                {
                    if (isEquipped) onUnequip?.Invoke();
                    else onEquip?.Invoke(BoundCheerleader);
                });
            }
        }

        /// <summary>빈 슬롯으로 되돌린다(PlayerCardUI.Clear()와 동일한 관례 - 재사용/풀링 대비).</summary>
        public void Clear()
        {
            BoundCheerleader = null;
            if (nameText != null) nameText.text = "";
            if (gradeText != null) gradeText.text = "";
            if (buffText != null) buffText.text = "";
            if (economicRateText != null) economicRateText.text = "";
            if (clutchText != null) clutchText.text = "";
            if (sentimentText != null) sentimentText.text = "";
            if (equipButtonLabel != null) equipButtonLabel.text = "";
            if (equipButton != null) equipButton.onClick.RemoveAllListeners();
            if (tierBadgeText != null) tierBadgeText.text = "";
            if (teamPeriodText != null) teamPeriodText.text = "";
        }
    }
}
