using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-145] 인벤토리에서 선수 카드를 클릭하면 뜨는 "선수 관리 집합소(Player Management Hub)".
    /// 상단에 타겟 선수 미니 카드/이름을 보여주고, 중앙 GridLayoutGroup에 훈련/강화/한계돌파/스킬 변경/
    /// 각성 5개 사각형 메뉴 타일을 배치한다. [TASK-KBO-183] 5개 타일이 전부 실제로 동작한다(구 "준비 중입니다" 3종 개통):
    ///   [강화] EnhanceUIController(EXP 누적) · [한계 돌파] 10강 후 보관 카드 1장 소모(CardGrowthActions) ·
    ///   [훈련(특훈)] 볼 소모(CardGrowthActions) · [각성] MaterialSelectUIController.OpenForAwaken(동일 선수 재료) · [스킬 변경].
    /// 상단 growthInfoText에 [기본 OVR / 현재 성장(+N) / 시너지(+M) / 최대 잠재 OVR]과 항목별 진행도를 표시하고, 결과는 세이브에 즉시 기록한다.
    ///
    /// 화면 전환은 `UIManager.ShowScreen()`(ScreenType.PlayerManagementHub/Inventory)을 쓴다 - 이
    /// 허브는 인벤토리를 완전히 덮는 별도 풀스크린이지 모달 팝업이 아니므로(명령서 "화면을 덮는 새
    /// 패널"), `MaterialSelectUIController`류의 직접 SetActive 모달과 달리 다른 화면들과 동일하게
    /// ScreenType으로 등록한다.
    ///
    /// [스킬 변경] 로직은 `InventoryUIController.OnClickSkillChange()`(TASK-138)의 SkillRerollManager
    /// 호출 흐름을 그대로 이 클래스 안에 다시 구현했다 - 별도의 재사용 가능한 컴포넌트가 원래 없었고,
    /// `InventoryUIController` 쪽 구현은 명령서 0항에 따라 손대지 않고 그대로 남겨 뒀다(현재는 이
    /// 허브가 카드 클릭의 유일한 진입점이라 그쪽은 휴면 코드가 됐다).
    ///
    /// [TASK-KBO-148, 사실 정정] 명령서는 `InventoryUIController.Instance.ShowDetail(currentPlayer)`를
    /// 호출하라고 지시했으나, `InventoryUIController`에는 정적 `Instance` 싱글톤이 없고(원래 화면 전환은
    /// 전부 `UIManager.ShowScreen()`으로만 하는 구조라 직접 참조 자체가 설계에 없었다) `ShowDetail()`도
    /// `private`이다 - 존재하지 않는 API를 그대로 호출하면 컴파일이 깨진다. 명령서 0항("범위 밖 리팩토링
    /// 금지")에 따라 `InventoryUIController`를 굳이 싱글톤으로 바꾸는(더 큰 구조 변경) 대신, TASK-147이
    /// 실제로 만든 탭 UI 컨트롤러 `PlayerDetailUIController`를 `enhanceUIController`와 동일한 패턴으로
    /// 직접 참조해 `Show(currentPlayer)`를 호출한다 - 사용자에게 보이는 결과(초상화 클릭 → 4탭 상세 창)는
    /// 명령서 의도와 완전히 동일하다.
    /// </summary>
    public class PlayerManagementUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private EnhanceUIController enhanceUIController;
        [Tooltip("[TASK-KBO-148] 타겟 카드(초상화) 클릭 시 여는 4탭 상세 창(TASK-147).")]
        [SerializeField] private PlayerDetailUIController playerDetailUIController;

        [Header("Target Card Preview")]
        [SerializeField] private PlayerCardUI targetPreviewCard;
        [SerializeField] private Text targetNameText;
        [Tooltip("[TASK-KBO-148] targetPreviewCard(초상화)에 부착된 클릭 감지용 Button. 클릭하면 4탭 " +
                 "상세 창을 연다.")]
        [SerializeField] private Button cardClickButton;

        [Header("Grid Menu (명령서 4항 - 훈련/강화/한계돌파/스킬 변경/각성)")]
        [SerializeField] private Button trainButton;
        [SerializeField] private Button enhanceButton;
        [SerializeField] private Button breakthroughButton;
        [SerializeField] private Button skillChangeButton;
        [SerializeField] private Button awakenButton;

        [Tooltip("스킬 변경/한계 돌파/특훈/각성 결과를 보여주는 텍스트. 비워두면 표시를 생략한다.")]
        [SerializeField] private Text skillChangeResultText;

        [Header("TASK-KBO-183 - 4대 성장")]
        [Tooltip("[기본 OVR / 현재 성장(+N) / 시너지(+M) / 최대 잠재 OVR] + 강화·한계 돌파·특훈·각성 진행도.")]
        [SerializeField] private Text growthInfoText;
        [Tooltip("[각성] 타일이 여는 재료 선택 팝업. 비우면 씬에서 찾는다.")]
        [SerializeField] private MaterialSelectUIController materialSelectUIController;

        [SerializeField] private Button closeButton;

        [Header("TASK-KBO-184 - 성장 센터")]
        [Tooltip("하단 [선수 관리] 탭이 곧바로 여는 성장 전용 허브(강화/각성/한계 돌파/훈련·특훈 + 대상 변경 + 재료 선택).")]
        [SerializeField] private GrowthCenterView growthCenter;

        private Player currentPlayer;

        /// <summary>SkillRerollManager에 F등급 확정 대기가 걸려 있는 선수. InventoryUIController.
        /// pendingRerollTarget과 동일한 목적(보류 상태가 다른 선수로 잘못 이어지는 것을 방지).</summary>
        private Player pendingRerollTarget;

        private void Awake()
        {
            if (trainButton != null) trainButton.onClick.AddListener(OnClickTrain);
            if (enhanceButton != null) enhanceButton.onClick.AddListener(OnClickEnhance);
            if (breakthroughButton != null) breakthroughButton.onClick.AddListener(OnClickBreakthrough);
            if (skillChangeButton != null) skillChangeButton.onClick.AddListener(OnClickSkillChange);
            if (awakenButton != null) awakenButton.onClick.AddListener(OnClickAwaken);
            var materialSelect = MaterialSelect;
            if (materialSelect != null) materialSelect.OnActionCompleted += RefreshTarget;
            if (cardClickButton != null) cardClickButton.onClick.AddListener(OnClickCardPortrait);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void OnEnable()
        {
            // [TASK-KBO-145, 명령서 6항] 강화 화면을 다녀오는 등 다른 화면을 거쳐 다시 활성화될 때마다
            // 최신 상태로 미리보기 카드를 다시 그린다. currentPlayer가 아직 없으면(최초 비활성 상태로
            // 씬 로드 직후) 아무 것도 하지 않는다.
            if (currentPlayer != null) RefreshTarget();
        }

        private void OnDestroy()
        {
            if (materialSelectUIController != null) materialSelectUIController.OnActionCompleted -= RefreshTarget;
        }

        private MaterialSelectUIController MaterialSelect
        {
            get
            {
                if (materialSelectUIController == null)
                    materialSelectUIController = FindAnyObjectByType<MaterialSelectUIController>(FindObjectsInactive.Include);
                return materialSelectUIController;
            }
        }

        /// <summary>[TASK-KBO-183] 미리보기 카드 + 성장 요약을 최신 상태로 다시 그린다(강화/한계 돌파/특훈/각성 직후).</summary>
        private void RefreshTarget()
        {
            if (currentPlayer == null) return;
            if (targetPreviewCard != null) targetPreviewCard.Setup(currentPlayer);
            if (growthInfoText != null)
            {
                int synergy = GameManager.Instance != null ? GameManager.Instance.CurrentTeamSynergyOvr : 0;
                growthInfoText.text = CardGrowthActions.GrowthSummary(currentPlayer, synergy);
            }
        }

        private void OnDisable()
        {
            CancelPendingRerollIfAny();
        }

        /// <summary>
        /// [TASK-KBO-184] 어디서든(로비 하단 [선수 관리] 탭 · 메인 홈 [선수단 강화] 타일 · 라인업 트레이 [선수 관리] · 상세 정보 [선수 관리])
        /// 성장 센터를 연다. player가 null이면 대표 선수(라인업 최고 OVR)가 기본 성장 대상이다.
        /// </summary>
        public static void OpenGrowthHub(Player player = null)
        {
            var controller = FindAnyObjectByType<PlayerManagementUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[PlayerManagementUIController] 선수 관리(성장 센터) 화면이 씬에 없습니다.");
                return;
            }
            controller.Show(player);
        }

        /// <summary>대상 선수를 고정하고 선수 관리(성장 센터) 화면을 띄운다. [TASK-KBO-184] 성장 센터가 있으면 그쪽이 화면 전체를 맡는다.</summary>
        public void Show(Player player)
        {
            var gm = GameManager.Instance;
            if (player?.Template == null) player = GrowthCenterRules.DefaultTarget(gm?.Roster, gm?.Inventory);
            if (growthCenter == null) growthCenter = GetComponent<GrowthCenterView>();
            if (growthCenter != null)
            {
                var from = UIManager.Instance != null ? UIManager.Instance.CurrentScreen : ScreenType.Lobby;
                currentPlayer = player;
                UIManager.Instance?.ShowScreen(ScreenType.PlayerManagementHub);
                growthCenter.Open(player, from);
                return;
            }
            if (player?.Template == null) return;

            if (pendingRerollTarget != null && pendingRerollTarget != player)
            {
                CancelPendingRerollIfAny();
            }

            currentPlayer = player;
            if (targetNameText != null) targetNameText.text = player.Template.PlayerName;
            if (skillChangeResultText != null) skillChangeResultText.text = "";
            RefreshTarget();

            UIManager.Instance?.ShowScreen(ScreenType.PlayerManagementHub);

            // [명령서 6항] 최상단 캔버스 계층에서 호출될 때 Z-Order 문제가 생기지 않도록 매번 강제한다.
            transform.SetAsLastSibling();
        }

        private void Close()
        {
            CancelPendingRerollIfAny();
            currentPlayer = null;
            UIManager.Instance?.ShowScreen(ScreenType.Lobby); // [TASK-KBO-184] 보유 리스트는 라인업 [보관 선수] 탭으로 옮겼다
        }

        private void CancelPendingRerollIfAny()
        {
            if (pendingRerollTarget == null) return;

            SkillRerollManager.Instance?.CancelPendingReroll(pendingRerollTarget);
            pendingRerollTarget = null;
        }

        /// <summary>[TASK-KBO-146, 명령서 3/4항] 기존 코드는 `currentPlayer`/`enhanceUIController` 둘 중
        /// 하나라도 null이면 로그 한 줄 없이 조용히 반환했다 - 사용자가 보고한 "[강화] 버튼 클릭 시
        /// 어떠한 에러 로그도 없이 무반응"의 정확한 원인이었다(각성/스킬 변경 등 다른 타일은 이미
        /// Debug.Log/LogWarning을 남기고 있어 상대적으로 [강화]만 완전히 침묵하는 것처럼 보였다). 클릭
        /// 이벤트 자체가 발생하는지부터 증명하는 로그를 맨 앞에 추가하고, 두 null 케이스 각각에 원인을
        /// 특정하는 로그를 남기도록 고쳤다.</summary>
        private void OnClickEnhance()
        {
            Debug.Log("[PlayerManagement] 강화 버튼 클릭됨, EnhanceUI 호출 시도");

            if (currentPlayer == null)
            {
                Debug.LogWarning("[PlayerManagementUIController] currentPlayer가 없어 강화 화면을 열 수 없습니다.");
                return;
            }

            if (enhanceUIController == null)
            {
                Debug.LogError("[PlayerManagementUIController] enhanceUIController가 바인딩되지 않아 강화 화면을 " +
                    "열 수 없습니다. 'KBO Manager/Setup/Auto-Connect Player Management UI'를 다시 실행하십시오.");
                return;
            }

            enhanceUIController.Show(currentPlayer);
        }

        /// <summary>[TASK-KBO-148] 상단 타겟 카드(초상화) 클릭 시 4탭 상세 창을 연다.
        /// [명령서 6항 - Z-Order 점검] `PlayerDetailUIController`(TASK-147)의 패널 실체는
        /// `SetupInventoryUI.cs`가 `InventoryPanel`의 자식으로 조립해 둔 `DetailPanel`이다 - 즉 이
        /// 허브(`PlayerManagementHubPanel`)와는 별개의 화면(`ScreenType.Inventory`) 소속이라, 허브가
        /// 활성 상태인 동안 `Show()`만 호출하면 부모 `InventoryPanel` 자체가 `UIManager`에 의해 비활성
        /// 상태로 남아 있어 화면에 아무것도 그려지지 않는다. `UIManager.ShowScreen(ScreenType.Inventory)`로
        /// 먼저 부모 화면을 활성화한 뒤 `Show()`를 호출해야 실제로 보인다 - `ShowScreen()`이 나머지 모든
        /// 화면(허브 포함)을 배타적으로 끄므로 허브와 겹쳐 보일 위험 자체가 없고, `Show()` 내부에서도
        /// `panelRoot.transform.SetAsLastSibling()`을 이미 강제해(TASK-147) `InventoryPanel`의 카드
        /// 목록보다 항상 위에 그려진다.</summary>
        private void OnClickCardPortrait()
        {
            if (currentPlayer == null) return;

            if (playerDetailUIController == null)
            {
                Debug.LogWarning("[PlayerManagementUIController] playerDetailUIController가 바인딩되지 않아 " +
                    "상세 창을 열 수 없습니다. 'KBO Manager/Setup/Auto-Connect Player Management UI'를 다시 실행하십시오.");
                return;
            }

            // [TASK-KBO-182] 상세 정보 패널은 캔버스 최상위라 화면 전환 없이 바로 뜬다(구 InventoryPanel 전환 제거 - TASK-184).
            playerDetailUIController.Show(currentPlayer);
        }

        /// <summary>[스킬 변경] 타일 OnClick. InventoryUIController.OnClickSkillChange()(TASK-138)와
        /// 동일한 로직 - 항상 첫 번째 보유 스킬(AcquiredSkillIds[0])을 재추첨 대상으로 삼는다.</summary>
        private void OnClickSkillChange()
        {
            if (currentPlayer == null) return;

            if (currentPlayer.AcquiredSkillIds.Count == 0)
            {
                Debug.Log("[PlayerManagementUIController] 보유 스킬이 없어 변경할 스킬이 없습니다.");
                if (skillChangeResultText != null) skillChangeResultText.text = "보유 스킬이 없습니다.";
                return;
            }

            if (SkillRerollManager.Instance == null)
            {
                Debug.LogWarning("[PlayerManagementUIController] 스킬 변경 시스템은 개발 중입니다. (SkillRerollManager 없음)");
                if (skillChangeResultText != null) skillChangeResultText.text = "스킬 변경 시스템은 개발 중입니다.";
                return;
            }

            RerollResult result = pendingRerollTarget == currentPlayer
                ? SkillRerollManager.Instance.ConfirmPendingReroll(currentPlayer)
                : SkillRerollManager.Instance.TryRerollSkill(currentPlayer, 0);

            pendingRerollTarget = result.Outcome == RerollOutcome.PendingDowngradeConfirmation ? currentPlayer : null;

            ShowSkillRerollResult(result);

            if (result.Outcome == RerollOutcome.Applied && targetPreviewCard != null)
            {
                targetPreviewCard.Setup(currentPlayer);
            }
        }

        private void ShowSkillRerollResult(RerollResult result)
        {
            if (skillChangeResultText == null) return;

            skillChangeResultText.text = result.Outcome switch
            {
                RerollOutcome.Applied => $"'{result.OldSkillName}' -> '{result.NewSkillName}' ({result.NewSkillTier}) 변경 완료!",
                RerollOutcome.NoTicket => "스킬 변경권이 없습니다.",
                RerollOutcome.NoSkillAvailable => "뽑을 수 있는 스킬이 없습니다.",
                RerollOutcome.PendingDowngradeConfirmation =>
                    $"새로 뽑힌 스킬이 최하위 F등급입니다 ('{result.NewSkillName}'). 그래도 적용하려면 [스킬 변경]을 한 번 더 눌러주세요.",
                _ => "스킬 변경에 실패했습니다.",
            };
        }

        // ----- [TASK-KBO-183] 한계 돌파 / 훈련(특훈) / 각성 -----

        private void ShowResult(string message)
        {
            Debug.Log($"[PlayerManagement] {message}");
            if (skillChangeResultText != null) skillChangeResultText.text = message;
        }

        /// <summary>[한계 돌파] 10강 카드에 보관 카드 1장(동일 선수 우선 → 낮은 등급·OVR 순 자동 선택)을 소모해 OVR +1.</summary>
        private void OnClickBreakthrough()
        {
            var gm = GameManager.Instance;
            if (currentPlayer == null || gm == null) return;

            if (!CardGrowthActions.CanLimitBreak(currentPlayer, out var reason)) { ShowResult(reason); return; }
            var material = CardGrowthActions.PickLimitBreakMaterial(currentPlayer, gm.Inventory, gm.Roster);
            if (!CardGrowthActions.TryLimitBreak(currentPlayer, material, out var message)) { ShowResult(message); return; }

            gm.RemovePlayerFromInventory(material);
            SaveManager.Instance?.TrySaveCareer();
            ShowResult(message);
            RefreshTarget();
        }

        /// <summary>[훈련(특훈)] 볼(GameGold)을 소모해 OVR +1(등급별 상한).</summary>
        private void OnClickTrain()
        {
            var gm = GameManager.Instance;
            if (currentPlayer == null || gm == null) return;

            if (!CardGrowthActions.TryTrain(currentPlayer, gm.GameGold, out int spent, out var message)) { ShowResult(message); return; }

            gm.GameGold -= spent;
            SaveManager.Instance?.TrySaveCareer();
            ShowResult(message);
            RefreshTarget();
        }

        /// <summary>[각성] 동일 선수 재료 선택 팝업(기존 TryAwaken 경로). 완료되면 OnActionCompleted로 요약을 갱신한다.</summary>
        private void OnClickAwaken()
        {
            if (currentPlayer == null) return;
            if (currentPlayer.AwakenLevel >= currentPlayer.MaxAwakenLevelForGrade)
            {
                ShowResult($"각성 최대 단계({currentPlayer.AwakenLabel})입니다.");
                return;
            }

            var materialSelect = MaterialSelect;
            if (materialSelect == null)
            {
                ShowResult("각성 재료 선택 화면(MaterialSelectUIController)을 찾지 못했습니다.");
                return;
            }
            materialSelect.OpenForAwaken(currentPlayer);
        }
    }
}
