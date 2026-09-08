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
        [Tooltip("스킬 변경권을 소모해 첫 번째 보유 스킬(AcquiredSkillIds[0])을 재추첨한다. " +
                 "SkillRerollManager.Instance를 정적으로 참조하므로 별도 인스펙터 연결이 필요 없다.")]
        [SerializeField] private Button skillChangeButton;
        [Tooltip("스킬 변경 결과(성공/실패/F등급 재확인 대기)를 보여주는 텍스트. 비워두면 표시를 생략한다.")]
        [SerializeField] private Text skillRerollResultText;

        private readonly List<PlayerCardUI> spawnedCards = new List<PlayerCardUI>();
        private Player selectedPlayer;

        /// <summary>SkillRerollManager에 F등급 확정 대기가 걸려 있는 선수. 다른 카드를 선택하거나 패널을
        /// 닫으면 자동으로 취소한다(보류 상태가 다른 선수로 잘못 이어지는 것을 방지).</summary>
        private Player pendingRerollTarget;

        private void Awake()
        {
            if (enhanceButton != null) enhanceButton.onClick.AddListener(OnClickEnhance);
            if (awakenButton != null) awakenButton.onClick.AddListener(OnClickAwaken);
            if (skillChangeButton != null) skillChangeButton.onClick.AddListener(OnClickSkillChange);

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
            // 다른 선수를 새로 선택하면(=재조회가 아니면) 이전 선수의 F등급 확정 대기를 취소한다 -
            // 보류된 재추첨 결과가 엉뚱한 선수에게 잘못 적용되는 사고를 막는다.
            if (pendingRerollTarget != null && pendingRerollTarget != player)
            {
                SkillRerollManager.Instance?.CancelPendingReroll(pendingRerollTarget);
                pendingRerollTarget = null;
                if (skillRerollResultText != null) skillRerollResultText.text = "";
            }

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
            if (skillChangeButton != null) skillChangeButton.interactable = player.AcquiredSkillIds.Count > 0;
        }

        /// <summary>상세 패널의 닫기 버튼 OnClick.</summary>
        public void CloseDetail()
        {
            if (pendingRerollTarget != null)
            {
                SkillRerollManager.Instance?.CancelPendingReroll(pendingRerollTarget);
                pendingRerollTarget = null;
            }

            selectedPlayer = null;
            if (detailPanelRoot != null) detailPanelRoot.SetActive(false);
        }

        // ----- 스킬 변경 버튼 브릿지 -----

        /// <summary>
        /// [스킬 변경] 버튼 OnClick. 항상 첫 번째 보유 스킬(AcquiredSkillIds[0])을 재추첨 대상으로 삼는다
        /// (현재 유저 카드는 ScoutManager.AttachInitialSkill()로 스킬을 최대 1개만 보유하므로 충분하다 -
        /// 추후 스킬 슬롯이 여러 개로 늘어나면 선택 UI를 추가하고 이 인덱스를 그 선택값으로 바꾸면 된다).
        ///
        /// 직전 시도가 F등급 확정 대기 상태였다면(이 버튼을 다시 눌렀다는 것은 "그래도 적용" 의사로
        /// 해석한다) 새로 재추첨하지 않고 ConfirmPendingReroll()로 그 결과를 그대로 확정 적용한다.
        /// </summary>
        private void OnClickSkillChange()
        {
            if (selectedPlayer == null || SkillRerollManager.Instance == null) return;

            RerollResult result = pendingRerollTarget == selectedPlayer
                ? SkillRerollManager.Instance.ConfirmPendingReroll(selectedPlayer)
                : SkillRerollManager.Instance.TryRerollSkill(selectedPlayer, 0);

            pendingRerollTarget = result.Outcome == RerollOutcome.PendingDowngradeConfirmation ? selectedPlayer : null;

            ShowSkillRerollResult(result);

            if (result.Outcome == RerollOutcome.Applied)
            {
                ShowDetail(selectedPlayer); // 갱신된 AcquiredSkillIds를 패널에 다시 반영
            }
        }

        private void ShowSkillRerollResult(RerollResult result)
        {
            if (skillRerollResultText == null) return;

            skillRerollResultText.text = result.Outcome switch
            {
                RerollOutcome.Applied => $"'{result.OldSkillName}' -> '{result.NewSkillName}' ({result.NewSkillTier}) 변경 완료!",
                RerollOutcome.NoTicket => "스킬 변경권이 없습니다.",
                RerollOutcome.NoSkillAvailable => "뽑을 수 있는 스킬이 없습니다.",
                RerollOutcome.PendingDowngradeConfirmation =>
                    $"새로 뽑힌 스킬이 최하위 F등급입니다 ('{result.NewSkillName}'). 그래도 적용하려면 [스킬 변경]을 한 번 더 눌러주세요.",
                _ => "스킬 변경에 실패했습니다.",
            };
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
