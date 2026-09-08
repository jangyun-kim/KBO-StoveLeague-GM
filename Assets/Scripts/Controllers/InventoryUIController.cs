using System.Collections.Generic;
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
    /// 버튼은 MaterialSelectUIController의 재료 다중 선택 팝업을 연다 - 실제 GameActionController 호출과
    /// 인벤토리 소모는 그 팝업의 확정(Confirm) 시점에 일어난다. 카드 목록은 CardPoolManager로 재사용한다.
    /// </summary>
    public class InventoryUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MaterialSelectUIController materialSelectUIController;

        [Header("Card List")]
        [Tooltip("카드가 나열될 부모(Scroll View의 Content). GridLayoutGroup을 붙여 정렬한다.")]
        [SerializeField] private Transform cardContainer;
        [Tooltip("Button 컴포넌트가 반드시 포함된 PlayerCardUI 프리팹. 재료 선택 팝업과 같은 프리팹을 " +
                 "연결하면 CardPoolManager 풀을 공유해 카드 인스턴스를 재사용한다.")]
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

        private void OnEnable()
        {
            if (materialSelectUIController != null)
            {
                materialSelectUIController.OnActionCompleted += HandleMaterialActionCompleted;
            }
        }

        private void OnDisable()
        {
            if (materialSelectUIController != null)
            {
                materialSelectUIController.OnActionCompleted -= HandleMaterialActionCompleted;
            }
        }

        private void HandleMaterialActionCompleted()
        {
            RefreshInventory();
            if (selectedPlayer != null) ShowDetail(selectedPlayer); // 최신 강화/각성 수치로 패널 다시 그림
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
            var card = CardPoolManager.Instance != null
                ? CardPoolManager.Instance.Get(cardPrefab, cardContainer)
                : Instantiate(cardPrefab, cardContainer);

            card.Setup(player);

            var button = card.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogWarning("[InventoryUIController] 카드 프리팹에 Button 컴포넌트가 없어 클릭 이벤트를 연결하지 못했습니다.");
            }
            else
            {
                // 풀링된 카드는 같은 Button 인스턴스가 재사용되므로, 이전 대여에서 붙은 리스너(다른 player를
                // 가리키는 클로저)가 쌓이지 않도록 먼저 전부 제거한 뒤 새로 연결한다.
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ShowDetail(player));
            }

            spawnedCards.Add(card);
        }

        private void ClearCards()
        {
            foreach (var card in spawnedCards)
            {
                if (card == null) continue;

                if (CardPoolManager.Instance != null) CardPoolManager.Instance.Release(card);
                else Destroy(card.gameObject);
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

        /// <summary>[강화하기] 버튼 OnClick. 재료(강화 카드) 다중 선택 팝업을 연다.</summary>
        private void OnClickEnhance()
        {
            if (selectedPlayer == null || materialSelectUIController == null) return;
            materialSelectUIController.OpenForEnhance(selectedPlayer);
        }

        /// <summary>[각성하기] 버튼 OnClick. 동일 선수 카드만 노출되는 재료 다중 선택 팝업을 연다.</summary>
        private void OnClickAwaken()
        {
            if (selectedPlayer?.Template == null || materialSelectUIController == null) return;
            materialSelectUIController.OpenForAwaken(selectedPlayer);
        }
    }
}
