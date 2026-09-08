using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 상점 화면. 프리미엄 재화를 소모해 "특수 확정 패키지"(GOLDEN_GLOVE 이상 카드 1장 확정권)를
    /// 구매하는 뼈대 수준 구현. UIManager.ScreenType.Shop 화면 루트에 붙인다.
    ///
    /// 이 컨트롤러는 오직 [상점 화면 자체]만 변경해 추가됐다 - UIManager/LeagueDashboardUIController에
    /// 버튼 1개, enum 값 1개를 더한 것 외에 InGameUIController/InventoryUIController/RosterUIController/
    /// ScoutUIController 등 기존 화면 컨트롤러는 단 한 줄도 수정하지 않았다(개방-폐쇄 원칙 검증).
    /// </summary>
    public class ShopUIController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("확정권 발급에 사용할 ScoutManager.RollGuaranteed()를 호출한다.")]
        [SerializeField] private ScoutManager scoutManager;

        [Header("특수 확정 패키지 (GOLDEN_GLOVE 이상 1장 확정)")]
        [SerializeField] private Grade guaranteedMinimumGrade = Grade.GOLDEN_GLOVE;
        [SerializeField] private int guaranteedPackagePrice = 300;
        [SerializeField] private Button buyGuaranteedPackageButton;
        [Tooltip("현재 보유 프리미엄 재화를 보여주는 텍스트. 비워두면 표시를 생략한다.")]
        [SerializeField] private Text premiumCurrencyText;
        [Tooltip("구매 결과(성공/실패 사유)를 보여주는 텍스트. 비워두면 표시를 생략한다.")]
        [SerializeField] private Text purchaseResultText;

        private void Awake()
        {
            if (buyGuaranteedPackageButton != null)
            {
                buyGuaranteedPackageButton.onClick.AddListener(BuyGuaranteedPackage);
            }
        }

        private void OnEnable()
        {
            RefreshCurrencyDisplay();
        }

        private void RefreshCurrencyDisplay()
        {
            if (premiumCurrencyText == null) return;

            int currentCurrency = GameManager.Instance != null ? GameManager.Instance.PremiumCurrency : 0;
            premiumCurrencyText.text = $"보유 프리미엄 재화: {currentCurrency}";
        }

        /// <summary>
        /// [특수 확정 패키지 구매] 버튼 OnClick. 프리미엄 재화가 충분하면 차감하고, ScoutManager로
        /// GOLDEN_GLOVE 이상 카드 1장을 확정 발급해 인벤토리에 추가한다. 재화가 부족하거나 발급에
        /// 실패하면(예: PlayerDatabase에 해당 등급 이상 카드가 하나도 없음) 아무 것도 차감하지 않는다.
        /// </summary>
        public void BuyGuaranteedPackage()
        {
            if (GameManager.Instance == null || scoutManager == null) return;

            if (GameManager.Instance.PremiumCurrency < guaranteedPackagePrice)
            {
                ShowResult($"프리미엄 재화가 부족합니다. (필요 {guaranteedPackagePrice} / 보유 {GameManager.Instance.PremiumCurrency})");
                return;
            }

            var player = scoutManager.RollGuaranteed(guaranteedMinimumGrade);
            if (player == null)
            {
                ShowResult("확정권 발급에 실패했습니다. (해당 등급 이상의 카드가 아직 등록되지 않음)");
                return;
            }

            GameManager.Instance.PremiumCurrency -= guaranteedPackagePrice;
            GameManager.Instance.AddPlayerToInventory(player);

            RefreshCurrencyDisplay();
            ShowResult($"{player.Template.PlayerName} ({player.Template.Grade}) 획득! 인벤토리에 추가되었습니다.");
        }

        private void ShowResult(string message)
        {
            if (purchaseResultText != null) purchaseResultText.text = message;
            else Debug.Log($"[ShopUIController] {message}");
        }
    }
}
