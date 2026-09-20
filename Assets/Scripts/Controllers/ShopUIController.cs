using System.Linq;
using KBOManager.Data;
using KBOManager.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 상점 화면. 일반 재화(GameManager.GameGold)로 "스킬 변경권"을 구매하는 화면.
    ///
    /// [TASK-KBO-129, 삭제] 구 "특수 확정 패키지"(GOLDEN_GLOVE 확정)와 "프리미엄 팩(10연뽑, ALLSTAR
    /// 확정)"을 완전히 삭제했다 - 이 두 상품은 GDD "뽑기(가챠) > 선수 영입" 절 어디에도 없는, AI가
    /// 임의로 차용한 양산형 모바일 가챠 게임의 관습이었다(명령서 5항 "타 모바일 게임의 관습... 임의로
    /// 덧붙이지 마십시오" 정면 위반 사례로 확인). 같은 기능(GOLDEN_GLOVE/ALLSTAR 이상 확정 발급)은
    /// GDD가 실제로 정의한 "선수 영입 > 프리미엄 영입"(ScoutUIController.premiumSignatureButton/
    /// premiumTitleHolderButton, 싸인볼/트로피 소모)으로 이미 대체되었다 - 이 화면에는 남기지 않는다.
    /// </summary>
    public class ShopUIController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("스킬 변경권 템플릿을 찾아 발급하는 데 쓴다.")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("스킬 변경권 구매 (일반 재화 소모)")]
        [SerializeField] private int skillTicketPrice = 150;
        [SerializeField] private Button buySkillTicketButton;

        [SerializeField] private Text gameGoldText;
        [Tooltip("구매 결과(성공/실패 사유)를 보여주는 텍스트. 비워두면 표시를 생략한다.")]
        [SerializeField] private Text purchaseResultText;

        [Tooltip("[TASK-KBO-113] 상점 화면을 닫고 로비로 돌아가는 버튼. UIManager.ShowScreen()만 호출한다.")]
        [SerializeField] private Button closeButton;

        private void Awake()
        {
            if (buySkillTicketButton != null) buySkillTicketButton.onClick.AddListener(BuySkillChangeTicket);
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
        }

        private void OnEnable()
        {
            RefreshCurrencyDisplay();
        }

        private void RefreshCurrencyDisplay()
        {
            if (GameManager.Instance == null) return;

            if (gameGoldText != null) gameGoldText.text = $"보유 게임 머니: {GameManager.Instance.GameGold}";
        }

        /// <summary>
        /// [스킬 변경권 구매] 버튼 OnClick. 일반 재화(GameGold)를 소모해 ItemDatabase에서
        /// ItemCategory.SkillChangeTicket 템플릿을 찾아 1장 발급한다.
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
