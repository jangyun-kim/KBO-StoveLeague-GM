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
    ///
    /// [TASK-KBO-135, 사실 정정] 명령서는 상세 패널을 여는 메서드 이름을 `OpenDetail()`로 가정했으나,
    /// 이 클래스에는 그런 이름의 메서드가 없다 - 실제로 상세 패널을 여는 메서드는 `ShowDetail(Player)`
    /// (188행)이며 닫는 메서드는 `CloseDetail()`(230행)이 맞다. 명령서 4항이 요구한 "메인 닫기 버튼
    /// 숨김/복원"은 이 두 실제 메서드에 구현했다.
    ///
    /// [TASK-KBO-138, 사실 정정] 명령서는 `changeSkillButton` 필드를 "선언/바인딩"하라고 지시했으나,
    /// 이 클래스에는 이미 동일한 역할의 `skillChangeButton` 필드(원문)가 `Awake()`에서 리스너까지
    /// 연결돼 있었다(DCL-102 Part 1에서도 이미 확인된 사실). 실제 "미작동" 원인은 필드 부재가 아니라
    /// (1) 보유 스킬이 0개면 버튼 자체를 `interactable = false`로 꺼 버려 클릭 이벤트가 아예 발생하지
    /// 않았던 점, (2) `SkillRerollManager.Instance == null`이면 `OnClickSkillChange()`가 아무 피드백
    /// 없이 조용히 반환했던 점 2가지였다 - 버튼을 항상 활성 상태로 두고, 두 케이스 각각에 `Debug.Log`
    /// + `skillRerollResultText` 안내 문구를 추가했다(명령서 4항, 재추첨 로직 자체는 무수정).
    /// </summary>
    public class InventoryUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MaterialSelectUIController materialSelectUIController;
        [Tooltip("ExecuteEnhance()의 성공/실패를 구독해 VFXController 연출을 트리거하는 데 쓴다.")]
        [SerializeField] private GameActionController gameActionController;

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
        [Tooltip("강화 성공/실패 시 VFXController가 반짝이는 색으로 재생할 카드 배경/테두리 Image. 비워두면 색 연출만 생략된다.")]
        [SerializeField] private Image detailCardFlashImage;

        [Header("Close Buttons")]
        [Tooltip("[TASK-KBO-111] 인벤토리 화면을 닫고 로비로 돌아가는 버튼. UIManager.ShowScreen()만 호출한다.")]
        [SerializeField] private Button closeButton;
        [Tooltip("[TASK-KBO-111] 상세 정보 패널만 닫는 버튼. CloseDetail()을 호출한다.")]
        [SerializeField] private Button closeDetailButton;

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
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
            if (closeDetailButton != null) closeDetailButton.onClick.AddListener(CloseDetail);

            CloseDetail();
        }

        private void OnEnable()
        {
            // [TASK-KBO-121] UIManager.ShowScreen()이 이 패널을 SetActive(true)할 때마다 최신
            // GameManager.Instance.Inventory로 카드 목록을 다시 그린다 - 기존에는 이 호출이 없어
            // 화면에 처음 진입하면(또는 재진입해도) 카드가 갱신되지 않고 비어 보였다.
            RefreshInventory();

            if (materialSelectUIController != null)
            {
                materialSelectUIController.OnActionCompleted += HandleMaterialActionCompleted;
            }

            if (gameActionController != null)
            {
                gameActionController.OnEnhanceCompleted += HandleEnhanceCompleted;
            }
        }

        private void OnDisable()
        {
            // [TASK-KBO-124] 상세 패널을 연 채로 화면을 나가면(다른 화면 버튼 클릭 등으로 이 패널이
            // SetActive(false)됨) detailPanelRoot가 활성 상태로 남아 있다가, 다음 재진입 시 이전에
            // 선택했던 선수의 상세 패널이 그대로 다시 보이는 상태 누수가 있었다 - CloseDetail()로
            // 선택 상태(selectedPlayer/pendingRerollTarget)까지 함께 초기화한다.
            CloseDetail();

            if (materialSelectUIController != null)
            {
                materialSelectUIController.OnActionCompleted -= HandleMaterialActionCompleted;
            }

            if (gameActionController != null)
            {
                gameActionController.OnEnhanceCompleted -= HandleEnhanceCompleted;
            }
        }

        private void HandleMaterialActionCompleted()
        {
            RefreshInventory();
            if (selectedPlayer != null) ShowDetail(selectedPlayer); // 최신 강화/각성 수치로 패널 다시 그림
        }

        /// <summary>GameActionController.OnEnhanceCompleted 핸들러. 지금 상세 패널에 열려 있는 카드가
        /// 방금 강화를 시도한 그 카드일 때만(다른 화면에서 강화가 일어났을 가능성은 없지만 방어적으로 확인)
        /// VFXController에 성공/실패 연출을 위임한다.</summary>
        private void HandleEnhanceCompleted(Player target, bool success)
        {
            if (target == null || target != selectedPlayer || detailPreviewCard == null) return;

            if (success)
            {
                Transform reinforceTextTransform = detailReinforceText != null ? detailReinforceText.transform : null;
                VFXController.Instance?.PlayEnhanceSuccess(detailCardFlashImage, reinforceTextTransform);
            }
            else
            {
                VFXController.Instance?.PlayEnhanceFailure(detailPreviewCard.transform, detailCardFlashImage);
            }
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

            // [TASK-KBO-121] 방어 코드 - 풀링/템플릿 유래로 스케일이 흐트러진 카드가 눈에 안 보이는
            // 크기로 렌더링되는 사고를 막는다(cardContainer의 GridLayoutGroup이 위치/크기는 통제하지만
            // localScale까지는 건드리지 않는다).
            card.transform.localScale = Vector3.one;

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
            // [TASK-KBO-135] 상세 패널이 열려 있는 동안은 메인 인벤토리 닫기 버튼(로비로 돌아가기)을
            // 숨겨, 우측 상단에 두 닫기 버튼이 겹쳐 보이는 UX 결함을 막는다 - CloseDetail()에서 되돌린다.
            if (closeButton != null) closeButton.gameObject.SetActive(false);
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
            // [TASK-KBO-138] 과거에는 보유 스킬이 0개면 버튼 자체를 비활성화해 눌러도 아무 반응이
            // 없었다("미작동"으로 보이는 원인 중 하나) - 이제 항상 클릭 가능하게 두고, OnClickSkillChange()
            // 내부에서 0개/매니저 부재 케이스마다 최소한의 피드백을 낸다(명령서 4항).
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
            // [TASK-KBO-135] ShowDetail()에서 숨긴 메인 닫기 버튼을 되돌린다.
            if (closeButton != null) closeButton.gameObject.SetActive(true);
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
            if (selectedPlayer == null) return;

            // [TASK-KBO-138] 클릭했는데 아무 반응이 없어 "미작동"으로 보이던 두 경로(보유 스킬 0개,
            // SkillRerollManager 미존재)에 명시적 피드백을 추가한다(명령서 4항 - 최소한 Debug.Log/안내
            // 텍스트). 기존 재추첨 로직(TryRerollSkill/ConfirmPendingReroll) 자체는 무수정이다.
            if (selectedPlayer.AcquiredSkillIds.Count == 0)
            {
                Debug.Log("[InventoryUIController] 보유 스킬이 없어 변경할 스킬이 없습니다.");
                if (skillRerollResultText != null) skillRerollResultText.text = "보유 스킬이 없습니다.";
                return;
            }

            if (SkillRerollManager.Instance == null)
            {
                Debug.LogWarning("[InventoryUIController] 스킬 변경 시스템은 개발 중입니다. (SkillRerollManager 없음)");
                if (skillRerollResultText != null) skillRerollResultText.text = "스킬 변경 시스템은 개발 중입니다.";
                return;
            }

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
