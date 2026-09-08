using System.Linq;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 상점 화면. 프리미엄 재화(GameManager.PremiumCurrency)로 "특수 확정 패키지"와 "프리미엄 팩(10연뽑)"을,
    /// 일반 재화(GameManager.GameGold)로 "스킬 변경권"을 구매하는 3종 상품 구현. UIManager.ScreenType.Shop
    /// 화면 루트에 붙인다.
    ///
    /// 이 컨트롤러는 오직 [상점 화면 자체]만 변경해 추가/확장됐다 - UIManager/LeagueDashboardUIController에
    /// 버튼 1개, enum 값 1개를 더한 것 외에 InGameUIController/InventoryUIController/RosterUIController/
    /// ScoutUIController 등 기존 화면 컨트롤러는 단 한 줄도 수정하지 않았다(개방-폐쇄 원칙 검증).
    /// </summary>
    public class ShopUIController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("확정권/프리미엄 팩 발급에 사용할 ScoutManager.RollGuaranteed()/RollPremiumTen()을 호출한다.")]
        [SerializeField] private ScoutManager scoutManager;
        [Tooltip("스킬 변경권 템플릿을 찾아 발급하는 데 쓴다.")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("특수 확정 패키지 (GOLDEN_GLOVE 이상 1장 확정)")]
        [SerializeField] private Grade guaranteedMinimumGrade = Grade.GOLDEN_GLOVE;
        [SerializeField] private int guaranteedPackagePrice = 300;
        [SerializeField] private Button buyGuaranteedPackageButton;

        [Header("프리미엄 팩 (10연뽑, ALLSTAR 이상 1장 확정 + 나머지 9장은 일반 확률)")]
        [SerializeField] private Grade premiumTenPullMinimumGrade = Grade.ALLSTAR;
        [SerializeField] private int premiumTenPullPrice = 1000;
        [SerializeField] private Button buyPremiumTenPullButton;

        [Header("스킬 변경권 구매 (일반 재화 소모)")]
        [SerializeField] private int skillTicketPrice = 150;
        [SerializeField] private Button buySkillTicketButton;

        [Tooltip("현재 보유 프리미엄 재화/일반 재화를 보여주는 텍스트. 비워두면 표시를 생략한다.")]
        [SerializeField] private Text premiumCurrencyText;
        [SerializeField] private Text gameGoldText;
        [Tooltip("구매 결과(성공/실패 사유)를 보여주는 텍스트. 비워두면 표시를 생략한다.")]
        [SerializeField] private Text purchaseResultText;

        private void Awake()
        {
            if (buyGuaranteedPackageButton != null) buyGuaranteedPackageButton.onClick.AddListener(BuyGuaranteedPackage);
            if (buyPremiumTenPullButton != null) buyPremiumTenPullButton.onClick.AddListener(BuyPremiumTenPull);
            if (buySkillTicketButton != null) buySkillTicketButton.onClick.AddListener(BuySkillChangeTicket);
        }

        private void OnEnable()
        {
            RefreshCurrencyDisplay();
        }

        private void RefreshCurrencyDisplay()
        {
            if (GameManager.Instance == null) return;

            if (premiumCurrencyText != null) premiumCurrencyText.text = $"보유 프리미엄 재화: {GameManager.Instance.PremiumCurrency}";
            if (gameGoldText != null) gameGoldText.text = $"보유 게임 머니: {GameManager.Instance.GameGold}";
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

        /// <summary>
        /// [프리미엄 팩(10연뽑)] 버튼 OnClick. 프리미엄 재화가 충분하면 차감하고
        /// ScoutManager.RollPremiumTen()으로 10장(그중 1장은 premiumTenPullMinimumGrade 이상 확정)을
        /// 발급해 인벤토리에 전부 추가한다.
        /// </summary>
        public void BuyPremiumTenPull()
        {
            if (GameManager.Instance == null || scoutManager == null) return;

            if (GameManager.Instance.PremiumCurrency < premiumTenPullPrice)
            {
                ShowResult($"프리미엄 재화가 부족합니다. (필요 {premiumTenPullPrice} / 보유 {GameManager.Instance.PremiumCurrency})");
                return;
            }

            var players = scoutManager.RollPremiumTen(premiumTenPullMinimumGrade);
            if (players == null || players.Count == 0)
            {
                ShowResult("프리미엄 팩 발급에 실패했습니다. (등록된 카드가 아직 없음)");
                return;
            }

            GameManager.Instance.PremiumCurrency -= premiumTenPullPrice;
            foreach (var player in players)
            {
                GameManager.Instance.AddPlayerToInventory(player);
            }

            RefreshCurrencyDisplay();
            ShowResult($"프리미엄 팩에서 {players.Count}장 획득! ({premiumTenPullMinimumGrade} 이상 1장 확정 포함)");
        }

        /// <summary>
        /// [스킬 변경권 구매] 버튼 OnClick. 일반 재화(GameGold)를 소모해 ItemDatabase에서
        /// ItemCategory.SkillChangeTicket 템플릿을 찾아 1장 발급한다. 프리미엄 팩/확정 패키지와 달리
        /// 반복 소모되는 유틸리티 상품이라 상대적으로 저렴한 일반 재화 쪽을 소모하도록 했다.
        /// </summary>
        public void BuySkillChangeTicket()
        {
            if (GameManager.Instance == null || itemDatabase == null) return;

            if (GameManager.Instance.GameGold < skillTicketPrice)
            {
                ShowResult($"게임 머니가 부족합니다. (필요 {skillTicketPrice} / 보유 {GameManager.Instance.GameGold})");
                return;
            }

            var ticketTemplate = itemDatabase.AllTemplates.FirstOrDefault(t => t.Category == ItemCategory.SkillChangeTicket);
            if (ticketTemplate == null)
            {
                ShowResult("스킬 변경권 템플릿이 아직 등록되지 않았습니다. (KBO Manager/Generate Default Items 메뉴 실행 필요)");
                return;
            }

            var ticket = itemDatabase.CreateItemInstance(ticketTemplate.TemplateId);
            if (ticket == null)
            {
                ShowResult("스킬 변경권 발급에 실패했습니다.");
                return;
            }

            GameManager.Instance.GameGold -= skillTicketPrice;
            GameManager.Instance.AddItemToInventory(ticket);

            RefreshCurrencyDisplay();
            ShowResult("스킬 변경권 1장을 구매했습니다.");
        }

        private void ShowResult(string message)
        {
            if (purchaseResultText != null) purchaseResultText.text = message;
            else Debug.Log($"[ShopUIController] {message}");
        }
    }
}
