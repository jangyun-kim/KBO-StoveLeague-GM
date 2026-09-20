using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>강화(Item 재료)와 각성(Player 재료)은 후보 데이터 타입 자체가 다르므로 팝업 내부에서 모드를 나눈다.</summary>
    public enum MaterialSelectMode
    {
        Enhance,
        Awaken
    }

    /// <summary>
    /// 인벤토리 상세 패널의 [강화하기]/[각성하기] 버튼이 여는 재료 다중 선택 팝업.
    ///
    /// 강화는 GameManager.ItemInventory에서 강화 재료(Item)를 최대 UpgradeManager.MaxEnhanceMaterials(5)장,
    /// 각성은 GameManager.Inventory에서 타겟과 RealPlayerId가 같은(GDD 3절 "동일 선수") Player 카드를
    /// 개수 제한 없이 다중 토글로 선택하게 한다. 각성 후보 목록은 CardPoolManager로 PlayerCardUI를
    /// 재사용하며, 타겟 카드 자신은 항상 후보 목록에서 제외한다(원본을 실수로 재료로 넣는 사고 방지).
    /// 확정(Confirm) 시에만 실제로 GameActionController.ExecuteEnhance/ExecuteAwaken을 호출한다.
    /// </summary>
    public class MaterialSelectUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameActionController gameActionController;

        [Header("Popup Root")]
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private Text titleText;
        [SerializeField] private Text selectionCountText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;

        [Header("Awaken Mode - Player Card List (CardPoolManager로 재사용)")]
        [SerializeField] private GameObject playerListPanel;
        [SerializeField] private Transform playerListContainer;
        [Tooltip("Button 컴포넌트가 포함된 PlayerCardUI 프리팹. 인벤토리 화면과 같은 프리팹을 쓰면 풀을 공유한다.")]
        [SerializeField] private PlayerCardUI playerCardPrefab;

        [Header("Enhance Mode - Item List (단순 버튼 목록)")]
        [SerializeField] private GameObject itemListPanel;
        [SerializeField] private Transform itemListContainer;
        [SerializeField] private Button itemEntryPrefab;
        [SerializeField] private Color itemSelectedColor = new Color(1f, 0.85f, 0.3f);
        [SerializeField] private Color itemDefaultColor = Color.white;

        /// <summary>확정(성공/실패 무관) 직후 발생. InventoryUIController 등이 구독해 화면을 다시 그린다.</summary>
        public event Action OnActionCompleted;

        private MaterialSelectMode mode;
        private Player target;

        private readonly List<PlayerCardUI> spawnedPlayerCards = new List<PlayerCardUI>();
        private readonly Dictionary<Player, PlayerCardUI> cardByPlayer = new Dictionary<Player, PlayerCardUI>();
        private readonly HashSet<Player> selectedPlayers = new HashSet<Player>();

        private readonly List<GameObject> spawnedItemEntries = new List<GameObject>();
        private readonly Dictionary<Item, Image> imageByItem = new Dictionary<Item, Image>();
        private readonly HashSet<Item> selectedItems = new HashSet<Item>();

        private void Awake()
        {
            if (confirmButton != null) confirmButton.onClick.AddListener(HandleConfirm);
            if (cancelButton != null) cancelButton.onClick.AddListener(ClosePopup);

            ClosePopup();
        }

        /// <summary>강화 모드로 팝업을 연다. 인벤토리의 강화 재료(Item)를 최대 5장까지 골라 선택한다.</summary>
        public void OpenForEnhance(Player targetPlayer)
        {
            if (targetPlayer == null) return;

            mode = MaterialSelectMode.Enhance;
            target = targetPlayer;
            selectedPlayers.Clear();
            selectedItems.Clear();

            if (titleText != null) titleText.text = $"강화 재료 선택 (최대 {UpgradeManager.MaxEnhanceMaterials}장)";
            if (playerListPanel != null) playerListPanel.SetActive(false);
            if (itemListPanel != null) itemListPanel.SetActive(true);

            PopulateItemList();
            UpdateSelectionCountText();

            // [TASK-KBO-125] 이 팝업(popupRoot == 이 컴포넌트의 GameObject 자신)은 Canvas 하위에서
            // InventoryPanel보다 sibling index가 낮아(TASK-109가 먼저 생성) 인벤토리 화면이 활성
            // 상태일 때 SetActive(true)해도 화면 뒤로 가려 안 보였다 - 열릴 때마다 무조건 가장 마지막
            // sibling(=최상단 렌더링)으로 끌어올린다.
            transform.SetAsLastSibling();
            if (popupRoot != null) popupRoot.SetActive(true);
        }

        /// <summary>각성 모드로 팝업을 연다. 타겟과 RealPlayerId가 같은 인벤토리 카드만 후보로 노출한다(타겟 자신은 제외).</summary>
        public void OpenForAwaken(Player targetPlayer)
        {
            if (targetPlayer?.Template == null) return;

            mode = MaterialSelectMode.Awaken;
            target = targetPlayer;
            selectedPlayers.Clear();
            selectedItems.Clear();

            if (titleText != null) titleText.text = "각성 재료 선택 (동일 선수 카드만 표시됨)";
            if (itemListPanel != null) itemListPanel.SetActive(false);
            if (playerListPanel != null) playerListPanel.SetActive(true);

            PopulatePlayerList();
            UpdateSelectionCountText();

            // [TASK-KBO-125] OpenForEnhance()와 동일한 이유로 최상단으로 끌어올린다.
            transform.SetAsLastSibling();
            if (popupRoot != null) popupRoot.SetActive(true);
        }

        // ----- 각성: Player 카드 목록 (풀링) -----

        private void PopulatePlayerList()
        {
            ClearPlayerCards();

            if (GameManager.Instance == null || playerCardPrefab == null || playerListContainer == null) return;

            // GDD 3절: 타겟과 완전히 같은 선수(RealPlayerId)만 재료 후보. 타겟 자신은 반드시 제외한다.
            var candidates = GameManager.Instance.Inventory
                .Where(p => p != target && p?.Template != null && p.Template.RealPlayerId == target.Template.RealPlayerId);

            foreach (var candidate in candidates)
            {
                SpawnPlayerCard(candidate);
            }
        }

        private void SpawnPlayerCard(Player candidate)
        {
            var card = CardPoolManager.Instance != null
                ? CardPoolManager.Instance.Get(playerCardPrefab, playerListContainer)
                : Instantiate(playerCardPrefab, playerListContainer);

            card.Setup(candidate);
            card.SetSelected(selectedPlayers.Contains(candidate));

            var button = card.GetComponent<Button>();
            if (button != null)
            {
                // 풀링된 카드는 같은 Button이 재사용되므로 이전 대여의 리스너를 먼저 지운다.
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ToggleSelectPlayer(candidate));
            }

            spawnedPlayerCards.Add(card);
            cardByPlayer[candidate] = card;
        }

        private void ToggleSelectPlayer(Player candidate)
        {
            if (selectedPlayers.Contains(candidate))
            {
                selectedPlayers.Remove(candidate);
            }
            else
            {
                selectedPlayers.Add(candidate);
            }

            if (cardByPlayer.TryGetValue(candidate, out var card))
            {
                card.SetSelected(selectedPlayers.Contains(candidate));
            }

            UpdateSelectionCountText();
        }

        private void ClearPlayerCards()
        {
            foreach (var card in spawnedPlayerCards)
            {
                if (card == null) continue;

                if (CardPoolManager.Instance != null) CardPoolManager.Instance.Release(card);
                else Destroy(card.gameObject);
            }
            spawnedPlayerCards.Clear();
            cardByPlayer.Clear();
        }

        // ----- 강화: Item 목록 (단순 버튼) -----

        private void PopulateItemList()
        {
            ClearItemEntries();

            if (GameManager.Instance == null || itemEntryPrefab == null || itemListContainer == null) return;

            // GameManager.ItemInventory는 강화 재료(EnhanceMaterial)와 스킬 변경권(SkillChangeTicket)이
            // 함께 섞여 있는 하나의 리스트다 - 강화 재료 선택 팝업에는 강화 재료만 노출해야 한다
            // (그렇지 않으면 스킬 변경권이 강화 재료로 잘못 소모되거나, UpgradeProbabilityDB에 없는
            // MaterialType으로 조회돼 0% 취급되는 등 강화 계산이 오염된다).
            foreach (var item in GameManager.Instance.ItemInventory)
            {
                if (item?.Template == null || item.Template.Category != ItemCategory.EnhanceMaterial) continue;
                SpawnItemEntry(item);
            }
        }

        private void SpawnItemEntry(Item item)
        {
            var button = Instantiate(itemEntryPrefab, itemListContainer);
            button.gameObject.SetActive(true);

            var label = button.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = item.Template != null && !string.IsNullOrEmpty(item.Template.DisplayName)
                    ? item.Template.DisplayName
                    : item.Template?.MaterialType.ToString() ?? "알 수 없는 재료";
            }

            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = itemDefaultColor;
                imageByItem[item] = image;
            }

            button.onClick.AddListener(() => ToggleSelectItem(item));
            spawnedItemEntries.Add(button.gameObject);
        }

        private void ToggleSelectItem(Item item)
        {
            if (selectedItems.Contains(item))
            {
                selectedItems.Remove(item);
            }
            else
            {
                if (selectedItems.Count >= UpgradeManager.MaxEnhanceMaterials) return; // 최대 5장 제한
                selectedItems.Add(item);
            }

            if (imageByItem.TryGetValue(item, out var image))
            {
                image.color = selectedItems.Contains(item) ? itemSelectedColor : itemDefaultColor;
            }

            UpdateSelectionCountText();
        }

        private void ClearItemEntries()
        {
            foreach (var entry in spawnedItemEntries)
            {
                if (entry != null) Destroy(entry);
            }
            spawnedItemEntries.Clear();
            imageByItem.Clear();
        }

        // ----- 공통 -----

        private void UpdateSelectionCountText()
        {
            if (selectionCountText == null) return;

            selectionCountText.text = mode == MaterialSelectMode.Enhance
                ? $"선택: {selectedItems.Count} / {UpgradeManager.MaxEnhanceMaterials}"
                : $"선택: {selectedPlayers.Count}";
        }

        private void HandleConfirm()
        {
            if (gameActionController == null || target == null)
            {
                ClosePopup();
                return;
            }

            if (mode == MaterialSelectMode.Enhance)
            {
                gameActionController.ClearEnhanceSelection();
                gameActionController.SetEnhanceTarget(target);
                foreach (var item in selectedItems)
                {
                    gameActionController.AddEnhanceMaterial(item);
                }
                gameActionController.ExecuteEnhance();
            }
            else
            {
                gameActionController.ClearAwakenSelection();
                gameActionController.SetAwakenTarget(target);
                foreach (var player in selectedPlayers)
                {
                    gameActionController.AddAwakenMaterial(player);
                }
                gameActionController.ExecuteAwaken();
            }

            ClosePopup();
            OnActionCompleted?.Invoke();
        }

        /// <summary>취소 버튼 OnClick. 아무 것도 실행하지 않고 팝업만 닫는다.</summary>
        public void ClosePopup()
        {
            ClearPlayerCards();
            ClearItemEntries();
            selectedPlayers.Clear();
            selectedItems.Clear();
            target = null;

            if (popupRoot != null) popupRoot.SetActive(false);
        }
    }
}
