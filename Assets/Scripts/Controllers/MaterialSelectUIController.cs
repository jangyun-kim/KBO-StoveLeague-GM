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
    /// <summary>강화와 각성은 재료 후보 필터가 다르므로(강화: 타겟 제외 전원, 각성: 동일 선수만) 팝업
    /// 내부에서 모드를 나눈다. [TASK-KBO-138] 이전에는 강화가 Item 재료를 썼으나, EXP 누적 확정 강화로
    /// 개편되며 각성과 동일하게 Player 카드 재료를 쓰도록 바뀌었다(아래 클래스 주석 참고).</summary>
    public enum MaterialSelectMode
    {
        Enhance,
        Awaken
    }

    /// <summary>
    /// 인벤토리 상세 패널의 [강화하기]/[각성하기] 버튼이 여는 재료 다중 선택 팝업.
    ///
    /// [TASK-KBO-138, 전면 개편] 강화 재료가 "GameManager.ItemInventory의 강화 재료(Item)"에서
    /// "GameManager.Inventory의 아무 보유 선수 카드(타겟 자신 제외, 동일 선수 제한 없음)"로 바뀌었다 -
    /// 사용자 GDD 지시에 따른 "EXP 누적 확정 강화" 개편(UpgradeManager.TryEnhance() 참고)의 일부다.
    /// 그 결과 강화도 각성과 동일하게 `playerListPanel`(CardPoolManager로 재사용하는 PlayerCardUI
    /// 목록)을 공유해서 쓴다 - 최대 선택 개수(UpgradeManager.MaxEnhanceMaterials, 5장)만 강화 모드에서
    /// 추가로 강제한다(각성은 원래부터 개수 제한이 없다). 기존 Item 기반 목록(`itemListPanel`/
    /// `PopulateItemList()` 등)은 명령서 5항("UI 디자인 자체를 갈아엎지 마라")에 따라 코드를 그대로
    /// 남겨 뒀지만(SetupUpgradeUI.cs가 계속 조립·바인딩함), 이제 어떤 모드에서도 호출되지 않는
    /// 휴면 코드다.
    ///
    /// 각성은 GameManager.Inventory에서 타겟과 RealPlayerId가 같은(GDD 3절 "동일 선수") Player 카드를
    /// 개수 제한 없이 다중 토글로 선택하게 한다. 타겟 카드 자신은 두 모드 모두 항상 후보 목록에서
    /// 제외한다(원본을 실수로 재료로 넣는 사고 방지). 확정(Confirm) 시에만 실제로
    /// GameActionController.ExecuteEnhance/ExecuteAwaken을 호출한다.
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
        [Tooltip("[TASK-KBO-133] 동일 선수 후보(중복 카드)가 0장일 때만 활성화되는 안내 문구.")]
        [SerializeField] private Text playerListEmptyText;

        [Header("Enhance Mode - Item List (단순 버튼 목록)")]
        [SerializeField] private GameObject itemListPanel;
        [SerializeField] private Transform itemListContainer;
        [SerializeField] private Button itemEntryPrefab;
        [SerializeField] private Color itemSelectedColor = new Color(1f, 0.85f, 0.3f);
        [SerializeField] private Color itemDefaultColor = Color.white;
        [Tooltip("[TASK-KBO-133] 강화 재료(Item)가 0개일 때만 활성화되는 안내 문구.")]
        [SerializeField] private Text itemListEmptyText;

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

        /// <summary>[TASK-KBO-138] 강화 모드로 팝업을 연다. 인벤토리의 보유 선수 카드(타겟 자신 제외,
        /// 동일 선수 제한 없음)를 최대 5장까지 골라 선택한다 - EXP 누적 확정 강화 재료.</summary>
        public void OpenForEnhance(Player targetPlayer)
        {
            if (targetPlayer == null) return;

            mode = MaterialSelectMode.Enhance;
            target = targetPlayer;
            selectedPlayers.Clear();
            selectedItems.Clear();

            if (titleText != null) titleText.text = $"강화 재료 선택 (최대 {UpgradeManager.MaxEnhanceMaterials}장)";
            if (itemListPanel != null) itemListPanel.SetActive(false);
            if (playerListPanel != null) playerListPanel.SetActive(true);

            PopulatePlayerList();
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

            if (titleText != null) titleText.text = "각성 재료 선택 (동일 선수·동일 시즌: 같은 연도 +3각 / 다른 연도 +1각)";
            if (itemListPanel != null) itemListPanel.SetActive(false);
            if (playerListPanel != null) playerListPanel.SetActive(true);

            PopulatePlayerList();
            UpdateSelectionCountText();

            // [TASK-KBO-125] OpenForEnhance()와 동일한 이유로 최상단으로 끌어올린다.
            transform.SetAsLastSibling();
            if (popupRoot != null) popupRoot.SetActive(true);
        }

        // ----- 강화/각성 공통: Player 카드 목록 (풀링) -----

        /// <summary>[TASK-KBO-138] 강화/각성 모드에 따라 서로 다른 필터로 재료 후보 Player 카드를
        /// 채운다 - 강화는 타겟 자신만 제외한 "모든 보유 카드"(동일 선수 제한 삭제), 각성은 기존대로
        /// RealPlayerId가 타겟과 같은 카드만(GDD 3절 "동일 선수").</summary>
        private void PopulatePlayerList()
        {
            ClearPlayerCards();

            if (GameManager.Instance != null && playerCardPrefab != null && playerListContainer != null)
            {
                IEnumerable<Player> candidates = mode == MaterialSelectMode.Enhance
                    ? GameManager.Instance.Inventory.Where(p => p != target && p?.Template != null)
                    : GameManager.Instance.Inventory.Where(p => p != target && p?.Template != null
                        && CardGrowthRules.AwakenGainFor(target, p) > 0) // [TASK-KBO-185] 동일 선수 + 동일 시즌 등급
                        .OrderByDescending(p => CardGrowthRules.AwakenGainFor(target, p));

                foreach (var candidate in candidates)
                {
                    SpawnPlayerCard(candidate);
                }
            }

            // [TASK-KBO-133] 후보(중복 카드)가 0장이면 팝업이 빈 흰 배경만 보이는 것처럼 느껴지므로
            // 안내 문구를 대신 노출한다.
            if (playerListEmptyText != null) playerListEmptyText.gameObject.SetActive(spawnedPlayerCards.Count == 0);
        }

        private void SpawnPlayerCard(Player candidate)
        {
            var card = CardPoolManager.Instance != null
                ? CardPoolManager.Instance.Get(playerCardPrefab, playerListContainer)
                : Instantiate(playerCardPrefab, playerListContainer);

            // [TASK-KBO-133] 풀링/이동 과정에서 localScale이 어긋나면 카드가 보이지 않거나 비정상
            // 크기로 렌더링될 수 있어 매 스폰마다 명시적으로 1로 고정한다.
            card.transform.localScale = Vector3.one;

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
                // [TASK-KBO-138] 강화 재료는 최대 5장(UpgradeManager.MaxEnhanceMaterials)까지만 선택
                // 가능하다(기존 Item 기반 강화의 제한을 그대로 계승) - 각성 재료는 원래대로 개수 제한이 없다.
                if (mode == MaterialSelectMode.Enhance && selectedPlayers.Count >= UpgradeManager.MaxEnhanceMaterials) return;
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

            if (GameManager.Instance != null && itemEntryPrefab != null && itemListContainer != null)
            {
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

            // [TASK-KBO-133] 강화 재료가 0개면 팝업이 빈 흰 배경만 보이는 것처럼 느껴지므로 안내
            // 문구를 대신 노출한다.
            if (itemListEmptyText != null) itemListEmptyText.gameObject.SetActive(spawnedItemEntries.Count == 0);
        }

        private void SpawnItemEntry(Item item)
        {
            var button = Instantiate(itemEntryPrefab, itemListContainer);
            button.gameObject.SetActive(true);
            // [TASK-KBO-133] localScale이 어긋나면 카드/버튼이 보이지 않거나 비정상 크기로 렌더링될
            // 수 있어 매 스폰마다 명시적으로 1로 고정한다.
            button.transform.localScale = Vector3.one;

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

            // [TASK-KBO-138] 강화도 이제 selectedPlayers를 쓴다(과거 selectedItems 대체) - 최대 개수
            // 표시만 모드별로 다르다(강화 5장 제한, 각성 무제한).
            selectionCountText.text = mode == MaterialSelectMode.Enhance
                ? $"선택: {selectedPlayers.Count} / {UpgradeManager.MaxEnhanceMaterials}"
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
                // [TASK-KBO-138] 강화 재료도 이제 selectedPlayers를 쓴다(과거 selectedItems 대체).
                gameActionController.ClearEnhanceSelection();
                gameActionController.SetEnhanceTarget(target);
                foreach (var player in selectedPlayers)
                {
                    gameActionController.AddEnhanceMaterial(player);
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
