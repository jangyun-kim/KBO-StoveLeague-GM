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

        public Cheerleader BoundCheerleader { get; private set; }

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

            if (nameText != null) nameText.text = data.Name;
            if (gradeText != null) gradeText.text = data.Grade.ToString();
            if (buffText != null) buffText.text = $"전력 보정: +{data.ConditionBuff}";
            if (economicRateText != null) economicRateText.text = $"관중 수익 x{data.EconomicBonusRate:F2}";
            if (clutchText != null) clutchText.text = $"클러치: {data.ClutchMultiplier}x";
            if (sentimentText != null) sentimentText.text = $"팬심 방어: +{data.SentimentDefense}";

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
        }
    }
}
