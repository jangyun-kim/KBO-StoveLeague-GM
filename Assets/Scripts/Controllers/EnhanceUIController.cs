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
    /// [TASK-KBO-145] 선수 관리 허브의 [강화] 타일을 누르면 진입하는 강화 전용 풀스크린. 기존
    /// `MaterialSelectUIController`의 투박한 "강화 재료 선택" 목록 팝업을 대체한다(각성 모드는 이번
    /// 작업 범위 밖이라 `MaterialSelectUIController` 자체는 그대로 두고 손대지 않았다).
    ///
    /// TASK-138이 확정한 EXP 누적 강화 공식(`UpgradeManager.TryEnhance()`/`UpgradeConstants`)은 절대
    /// 수정하지 않는다(명령서 5항) - 이 클래스는 그 공식이 이미 공개해 둔 순수 계산 함수
    /// (`UpgradeConstants.GetRequiredExp()`/`GetMaterialExp()`)와 공개 상수(`RequiredExpBaseByGrade`/
    /// `RequiredExpLevelMultiplierStep`/`RequiredExpFallback`)만 읽어서, 재료를 실제로 적용하기 전에
    /// "적용하면 어떻게 될지"를 미리 계산해 보여주는 로컬 시뮬레이션(`SimulatePreview()`)만 추가했다 -
    /// `Player`/`UpgradeManager` 어느 쪽도 실제로 변형(mutate)하지 않는다. 실제 강화 실행은 기존
    /// 인벤토리 경로와 동일하게 `GameActionController.SetEnhanceTarget()`/`AddEnhanceMaterial()`/
    /// `ExecuteEnhance()`를 그대로 호출한다.
    /// </summary>
    public class EnhanceUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameActionController gameActionController;

        [Header("Target Card Info (상단)")]
        [SerializeField] private PlayerCardUI targetPreviewCard;
        [SerializeField] private Text targetGradeText;
        [SerializeField] private Text targetLevelText;

        [Header("EXP Bar (중앙, Image.Type=Filled)")]
        [SerializeField] private Image expFillImage;
        [SerializeField] private Text expText;
        [SerializeField] private Color normalFillColor = new Color(0.3f, 0.55f, 0.95f);
        [Tooltip("재료를 선택해 미리보기 중일 때 EXP 바 색상.")]
        [SerializeField] private Color previewFillColor = new Color(1f, 0.85f, 0.3f);

        [Header("Material List (하단, ScrollRect + GridLayoutGroup, 최대 5장)")]
        [SerializeField] private Transform materialListContainer;
        [SerializeField] private PlayerCardUI materialCardPrefab;
        [SerializeField] private Text materialListEmptyText;
        [SerializeField] private Text selectionCountText;

        [Header("최하단 버튼")]
        [SerializeField] private Button clearAllButton;
        [SerializeField] private Button executeButton;
        [SerializeField] private Text resultText;

        [SerializeField] private Button closeButton;

        private Player target;
        private readonly List<PlayerCardUI> spawnedMaterialCards = new List<PlayerCardUI>();
        private readonly Dictionary<Player, PlayerCardUI> cardByMaterial = new Dictionary<Player, PlayerCardUI>();
        private readonly HashSet<Player> selectedMaterials = new HashSet<Player>();

        private void Awake()
        {
            if (clearAllButton != null) clearAllButton.onClick.AddListener(OnClickClearAll);
            if (executeButton != null) executeButton.onClick.AddListener(OnClickExecuteEnhance);
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void OnEnable()
        {
            if (gameActionController != null) gameActionController.OnEnhanceCompleted += HandleEnhanceCompleted;
        }

        private void OnDisable()
        {
            if (gameActionController != null) gameActionController.OnEnhanceCompleted -= HandleEnhanceCompleted;

            ClearMaterialCards();
            selectedMaterials.Clear();
            target = null;
        }

        /// <summary>선수 관리 허브의 [강화] 타일이 호출한다. 타겟을 고정하고 화면을 띄운다.
        /// [TASK-KBO-146, 명령서 6항] 이 메서드까지 호출이 도달했는지 자체를 증명하는 로그를 남긴다 -
        /// `PlayerManagementUIController.OnClickEnhance()`의 로그는 보이는데 이 로그가 안 보이면
        /// `UIManager.ShowScreen(ScreenType.Enhance)` 호출 이전 단계(이 메서드 진입 자체)에 문제가
        /// 있다는 뜻이고, 이 로그까지는 보이는데 화면이 안 뜨면 `ScreenType.Enhance`가
        /// `UIManager.screens`에 등록되지 않았다는 뜻이다(`ShowScreen()`이 그 경우 자체적으로
        /// "화면이 등록되어 있지 않습니다" 경고를 남긴다).</summary>
        public void Show(Player targetPlayer)
        {
            if (targetPlayer?.Template == null)
            {
                Debug.LogWarning("[EnhanceUIController] Show() 호출됐지만 targetPlayer/Template이 없어 화면을 열지 않습니다.");
                return;
            }

            Debug.Log($"[EnhanceUIController] Show() 진입 - target={targetPlayer.Template.PlayerName}, " +
                "UIManager.ShowScreen(ScreenType.Enhance) 호출 시도.");

            target = targetPlayer;
            selectedMaterials.Clear();
            if (resultText != null) resultText.text = "";

            RefreshTargetDisplay();
            PopulateMaterialList();
            UpdateSelectionCountText();

            UIManager.Instance?.ShowScreen(ScreenType.Enhance);

            // [명령서 6항] 최상단 캔버스 계층에서 호출될 때 Z-Order 문제가 생기지 않도록 매번 강제한다.
            transform.SetAsLastSibling();
        }

        private void Close()
        {
            UIManager.Instance?.ShowScreen(ScreenType.PlayerManagementHub);
        }

        // ----- 상단: 타겟 카드 정보 -----

        private void RefreshTargetDisplay()
        {
            if (target == null) return;

            if (targetPreviewCard != null) targetPreviewCard.Setup(target);
            if (targetGradeText != null) targetGradeText.text = CardGrowthRules.DisplayName(target.Template.Grade);
            if (executeButton != null) executeButton.interactable = target.ReinforceLevel < Player.MaxReinforceLevel;

            UpdateExpBar();
        }

        /// <summary>재료 미선택 상태(실제 현재 값) 기준으로 EXP 바를 그린다.</summary>
        private void UpdateExpBar()
        {
            if (target == null) return;

            if (targetLevelText != null) targetLevelText.text = $"{target.ReinforceLevel}강";

            bool maxed = target.ReinforceLevel >= Player.MaxReinforceLevel;
            int required = maxed ? 0 : UpgradeConstants.GetRequiredExp(target);
            float ratio = maxed ? 1f : Mathf.Clamp01((float)target.ReinforceExp / required);

            if (expFillImage != null)
            {
                expFillImage.fillAmount = ratio;
                expFillImage.color = normalFillColor;
            }

            if (expText != null)
            {
                expText.text = maxed ? "MAX" : $"{target.ReinforceExp} / {required}";
            }
        }

        /// <summary>
        /// [명령서 4항 - EXP Preview] 선택된 재료를 "적용하면" 몇 강까지 오르고 EXP가 얼마나 쌓일지
        /// `UpgradeManager.TryEnhance()`(TASK-138)의 while 누적 로직과 동일한 규칙으로 로컬 시뮬레이션한다
        /// - target/UpgradeManager 어느 쪽도 변형하지 않는다(순수 로컬 변수 계산). `UpgradeConstants`가
        /// 공개해 둔 값만 읽으므로 그 공식 자체를 수정하는 것은 아니다.
        /// </summary>
        private void UpdateExpPreview()
        {
            if (target == null) return;

            if (selectedMaterials.Count == 0)
            {
                UpdateExpBar();
                return;
            }

            int gainedExpTotal = selectedMaterials.Sum(m =>
                UpgradeConstants.GetMaterialExp(m.Template.Grade, target.Template.Grade));

            int baseExpForGrade = UpgradeConstants.RequiredExpBaseByGrade.TryGetValue(target.Template.Grade, out var baseValue)
                ? baseValue
                : UpgradeConstants.RequiredExpFallback;

            int previewLevel = target.ReinforceLevel;
            int remainingExp = target.ReinforceExp + gainedExpTotal;

            while (previewLevel < Player.MaxReinforceLevel)
            {
                int required = baseExpForGrade * (previewLevel + UpgradeConstants.RequiredExpLevelMultiplierStep);
                if (remainingExp < required) break;

                remainingExp -= required;
                previewLevel++;
            }

            bool maxed = previewLevel >= Player.MaxReinforceLevel;
            int requiredAtPreviewLevel = maxed ? 0 : baseExpForGrade * (previewLevel + UpgradeConstants.RequiredExpLevelMultiplierStep);
            float ratio = maxed ? 1f : Mathf.Clamp01((float)remainingExp / requiredAtPreviewLevel);

            if (expFillImage != null)
            {
                expFillImage.fillAmount = ratio;
                expFillImage.color = previewFillColor;
            }

            if (expText != null)
            {
                expText.text = maxed
                    ? $"MAX (+{gainedExpTotal} 예상)"
                    : $"{remainingExp} / {requiredAtPreviewLevel} (+{gainedExpTotal} 예상)";
            }

            if (targetLevelText != null)
            {
                targetLevelText.text = previewLevel != target.ReinforceLevel
                    ? $"{target.ReinforceLevel}강 -> {previewLevel}강 (예상)"
                    : $"{target.ReinforceLevel}강";
            }
        }

        // ----- 하단: 재료 목록 -----

        /// <summary>[TASK-KBO-138] MaterialSelectUIController.PopulatePlayerList()의 강화 모드 필터
        /// (타겟 자신만 제외한 모든 보유 카드)를 그대로 재사용한다.</summary>
        private void PopulateMaterialList()
        {
            ClearMaterialCards();

            if (GameManager.Instance == null || materialCardPrefab == null || materialListContainer == null || target == null)
            {
                if (materialListEmptyText != null) materialListEmptyText.gameObject.SetActive(true);
                return;
            }

            foreach (var candidate in GameManager.Instance.Inventory)
            {
                if (candidate == target || candidate?.Template == null) continue;
                SpawnMaterialCard(candidate);
            }

            if (materialListEmptyText != null) materialListEmptyText.gameObject.SetActive(spawnedMaterialCards.Count == 0);
        }

        private void SpawnMaterialCard(Player candidate)
        {
            var card = CardPoolManager.Instance != null
                ? CardPoolManager.Instance.Get(materialCardPrefab, materialListContainer)
                : Instantiate(materialCardPrefab, materialListContainer);

            card.transform.localScale = Vector3.one;
            card.Setup(candidate);
            card.SetSelected(selectedMaterials.Contains(candidate));

            var button = card.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => ToggleMaterial(candidate));
            }

            spawnedMaterialCards.Add(card);
            cardByMaterial[candidate] = card;
        }

        private void ToggleMaterial(Player candidate)
        {
            if (selectedMaterials.Contains(candidate))
            {
                selectedMaterials.Remove(candidate);
            }
            else
            {
                if (selectedMaterials.Count >= UpgradeManager.MaxEnhanceMaterials) return;
                selectedMaterials.Add(candidate);
            }

            if (cardByMaterial.TryGetValue(candidate, out var card))
            {
                card.SetSelected(selectedMaterials.Contains(candidate));
            }

            UpdateSelectionCountText();
            UpdateExpPreview();
        }

        private void ClearMaterialCards()
        {
            foreach (var card in spawnedMaterialCards)
            {
                if (card == null) continue;

                if (CardPoolManager.Instance != null) CardPoolManager.Instance.Release(card);
                else Destroy(card.gameObject);
            }
            spawnedMaterialCards.Clear();
            cardByMaterial.Clear();
        }

        private void UpdateSelectionCountText()
        {
            if (selectionCountText != null)
            {
                selectionCountText.text = $"선택 재료: {selectedMaterials.Count} / {UpgradeManager.MaxEnhanceMaterials}";
            }
        }

        // ----- 최하단 버튼 -----

        private void OnClickClearAll()
        {
            selectedMaterials.Clear();
            foreach (var card in spawnedMaterialCards)
            {
                if (card != null) card.SetSelected(false);
            }

            UpdateSelectionCountText();
            UpdateExpPreview();
        }

        private void OnClickExecuteEnhance()
        {
            if (target == null || gameActionController == null) return;

            if (selectedMaterials.Count == 0)
            {
                if (resultText != null) resultText.text = "재료를 1장 이상 선택하세요.";
                return;
            }

            gameActionController.ClearEnhanceSelection();
            gameActionController.SetEnhanceTarget(target);
            foreach (var material in selectedMaterials)
            {
                gameActionController.AddEnhanceMaterial(material);
            }

            // GameActionController.ExecuteEnhance()는 OnEnhanceCompleted를 동기적으로 발생시키므로,
            // 이 호출이 끝나는 시점에는 이미 HandleEnhanceCompleted()가 화면을 갱신한 뒤다.
            gameActionController.ExecuteEnhance();
        }

        private void HandleEnhanceCompleted(Player completedTarget, bool success)
        {
            if (completedTarget != target) return;

            if (resultText != null)
            {
                resultText.text = success
                    ? "강화 완료!"
                    : "강화에 실패했습니다. (선택한 재료를 다시 확인하세요)";
            }

            selectedMaterials.Clear();
            RefreshTargetDisplay();
            PopulateMaterialList();
            UpdateSelectionCountText();
        }
    }
}
