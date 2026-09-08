using System.Collections.Generic;
using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// GameManager.Instance.Inventory 전체를 PlayerCardUI 목록으로 그리고, 카드를 클릭하면 상세 정보
    /// 패널(강화/각성 상태, 보유 스킬)을 띄우는 인벤토리 메인 허브. 상세 패널의 [강화하기]/[각성하기]
    /// 버튼은 GameActionController를 직접 호출하며, 재료는 인벤토리에서 조건에 맞는 카드를 자동으로
    /// 골라 주입하는 단순화된 방식으로 연동한다(재료를 하나씩 고르는 UI는 후속 과제).
    /// </summary>
    public class InventoryUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameActionController gameActionController;

        [Header("Card List")]
        [Tooltip("카드가 나열될 부모(Scroll View의 Content). GridLayoutGroup을 붙여 정렬한다.")]
        [SerializeField] private Transform cardContainer;
        [Tooltip("Button 컴포넌트가 반드시 포함된 PlayerCardUI 프리팹.")]
        [SerializeField] private PlayerCardUI cardPrefab;

        [Header("Detail Panel")]
        [SerializeField] private GameObject detailPanelRoot;
        [Tooltip("상세 패널 상단에 선택한 카드를 미리보기로 다시 그릴 때 쓴다(선택 사항).")]
        [SerializeField] private PlayerCardUI detailPreviewCard;
        [SerializeField] private Text detailReinforceText;
        [SerializeField] private Text detailAwakenText;
        [SerializeField] private Text detailSkillsText;
        [SerializeField] private Button enhanceButton;
        [SerializeField] private Button awakenButton;

        private readonly List<PlayerCardUI> spawnedCards = new List<PlayerCardUI>();
        private Player selectedPlayer;

        private void Awake()
        {
            if (enhanceButton != null) enhanceButton.onClick.AddListener(OnClickEnhance);
            if (awakenButton != null) awakenButton.onClick.AddListener(OnClickAwaken);

            CloseDetail();
        }

        /// <summary>GameManager.Instance.Inventory 전체를 다시 읽어 카드 리스트를 새로 그린다.</summary>
        public void RefreshInventory()
        {
            ClearCards();

            if (GameManager.Instance == null || cardPrefab == null || cardContainer == null) return;

            foreach (var player in GameManager.Instance.Inventory)
            {
                SpawnCard(player);
            }
        }

        private void SpawnCard(Player player)
        {
            var card = Instantiate(cardPrefab, cardContainer);
            card.gameObject.SetActive(true);
            card.Setup(player);

            var button = card.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogWarning("[InventoryUIController] 카드 프리팹에 Button 컴포넌트가 없어 클릭 이벤트를 연결하지 못했습니다.");
            }
            else
            {
                button.onClick.AddListener(() => ShowDetail(player));
            }

            spawnedCards.Add(card);
        }

        private void ClearCards()
        {
            foreach (var card in spawnedCards)
            {
                if (card != null) Destroy(card.gameObject);
            }
            spawnedCards.Clear();
        }

        // ----- 상세 정보 패널 -----

        private void ShowDetail(Player player)
        {
            selectedPlayer = player;
            if (player?.Template == null) return;

            if (detailPanelRoot != null) detailPanelRoot.SetActive(true);
            if (detailPreviewCard != null) detailPreviewCard.Setup(player);

            if (detailReinforceText != null)
            {
                detailReinforceText.text = $"강화 {player.ReinforceLevel} / {Player.MaxReinforceLevel}";
            }

            if (detailAwakenText != null)
            {
                detailAwakenText.text = player.CanAwaken
                    ? $"각성 {player.AwakenLevel} / {Player.MaxAwakenLevel}"
                    : "각성 불가 (LIVE 등급)";
            }

            if (detailSkillsText != null)
            {
                detailSkillsText.text = player.AcquiredSkillIds.Count > 0
                    ? string.Join(", ", player.AcquiredSkillIds)
                    : "보유 스킬 없음";
            }

            if (enhanceButton != null) enhanceButton.interactable = player.ReinforceLevel < Player.MaxReinforceLevel;
            if (awakenButton != null) awakenButton.interactable = player.CanAwaken && player.AwakenLevel < Player.MaxAwakenLevel;
        }

        /// <summary>상세 패널의 닫기 버튼 OnClick.</summary>
        public void CloseDetail()
        {
            selectedPlayer = null;
            if (detailPanelRoot != null) detailPanelRoot.SetActive(false);
        }

        // ----- 강화/각성 버튼 브릿지 -----

        /// <summary>[강화하기] 버튼 OnClick. 인벤토리의 강화 재료(Item)를 자동으로 최대 개수만큼 채워 넣는다.</summary>
        private void OnClickEnhance()
        {
            if (selectedPlayer == null || gameActionController == null || GameManager.Instance == null) return;

            gameActionController.ClearEnhanceSelection();
            gameActionController.SetEnhanceTarget(selectedPlayer);

            foreach (var item in GameManager.Instance.ItemInventory.Take(UpgradeManager.MaxEnhanceMaterials))
            {
                gameActionController.AddEnhanceMaterial(item);
            }

            gameActionController.ExecuteEnhance();

            RefreshInventory();
            ShowDetail(selectedPlayer); // 성공/실패와 무관하게 최신 수치로 패널을 다시 그린다
        }

        /// <summary>
        /// [각성하기] 버튼 OnClick. 인벤토리에서 대상과 RealPlayerId(동일 선수)가 일치하는 카드를
        /// 전부 재료로 자동 주입한다 - 무관한 카드를 섞어 넣지 않도록 클라이언트에서 미리 걸러 둔다.
        /// </summary>
        private void OnClickAwaken()
        {
            if (selectedPlayer?.Template == null || gameActionController == null || GameManager.Instance == null) return;

            gameActionController.ClearAwakenSelection();
            gameActionController.SetAwakenTarget(selectedPlayer);

            var materials = GameManager.Instance.Inventory
                .Where(p => p != selectedPlayer && p?.Template != null
                    && p.Template.RealPlayerId == selectedPlayer.Template.RealPlayerId);

            foreach (var material in materials)
            {
                gameActionController.AddAwakenMaterial(material);
            }

            gameActionController.ExecuteAwaken();

            RefreshInventory();
            ShowDetail(selectedPlayer);
        }
    }
}
