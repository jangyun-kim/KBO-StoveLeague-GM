using KBOManager.Controllers;
using KBOManager.Managers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-145] `EnhanceUIController`(강화 전용 풀스크린 - 타겟 카드 정보 + EXP 바 + 재료 선택 +
    /// 실행 버튼)를 씬에 조립·배선하는 에디터 자동화. 기존 `MaterialSelectUIController`의 "강화 재료
    /// 선택" 목록 팝업을 대체하는 별도 화면이라 `ScreenType.Enhance`로 등록한다.
    ///
    /// 재료 목록은 `SetupInventoryUI.FindOrCreateCardListPanel()`/`SetupUpgradeUI.FindOrCreateScrollList()`와
    /// 동일한 표준 스크롤 뷰 계층(Panel(ScrollRect) -&gt; Viewport(RectMask2D) -&gt; Content
    /// (GridLayoutGroup+ContentSizeFitter))을 재사용한다 - 이 코드베이스의 모든 카드 목록 화면이 이미
    /// 이 패턴을 쓰고 있어(가로 스크롤을 새로 만드는 대신) 그대로 따르는 편이 안전하다.
    ///
    /// `SetupInventoryUI.cs`(TASK-142)가 확립한 "매 실행마다 직속 자식 전부 DestroyImmediate 후 처음부터
    /// 재조립" 패턴을 그대로 따른다(명령서 0항 - UI 에디터 스크립트는 완벽한 초기화를 선행할 것).
    /// </summary>
    public static class SetupEnhanceUI
    {
        private const string PanelName = "EnhancePanel";
        private const string TargetPreviewCardName = "TargetPreviewCard";
        private const string TargetGradeTextName = "TargetGradeText";
        private const string TargetLevelTextName = "TargetLevelText";
        private const string ExpBarBackgroundName = "ExpBarBackground";
        private const string ExpFillImageName = "ExpFillImage";
        private const string ExpTextName = "ExpText";
        private const string MaterialListPanelName = "MaterialListPanel";
        private const string ViewportName = "Viewport";
        private const string MaterialListContainerName = "MaterialListContainer";
        private const string SelectionCountTextName = "SelectionCountText";
        private const string ClearAllButtonName = "ClearAllButton";
        private const string ExecuteButtonName = "ExecuteButton";
        private const string ResultTextName = "ResultText";
        private const string CloseButtonName = "CloseButton";
        private const string TemplatesHolderName = "_Templates";
        private const string PlayerCardTemplateName = "PlayerCardTemplate";

        [MenuItem("KBO Manager/Setup/Auto-Connect Enhance UI")]
        public static void AutoConnectEnhanceUI()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[SetupEnhanceUI] 씬에서 Canvas를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Initialize Current Scene'을 실행하십시오.");
                return;
            }

            var controller = FindOrCreateEnhancePanel(canvas.transform);

            // [명령서 0항 - UI 에디터 스크립트는 DestroyImmediate를 통한 완벽한 초기화를 선행]
            for (int i = controller.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(controller.transform.GetChild(i).gameObject);
            }

            var cardTemplate = FindOrCreatePlayerCardTemplate(canvas.transform);

            var targetPreviewCard = FindOrCreateTargetPreviewCard(controller.transform, cardTemplate);
            var targetGradeText = FindOrCreateText(controller.transform, TargetGradeTextName, "",
                new Vector2(0.27f, 0.88f), new Vector2(0.95f, 0.96f), 24);
            var targetLevelText = FindOrCreateText(controller.transform, TargetLevelTextName, "",
                new Vector2(0.27f, 0.80f), new Vector2(0.95f, 0.88f), 22);

            var (expFillImage, expText) = FindOrCreateExpBar(controller.transform);

            var (materialListContainer, materialListEmptyText) = FindOrCreateMaterialListPanel(controller.transform,
                new Vector2(140f, 200f), new Vector2(10f, 10f));
            var selectionCountText = FindOrCreateText(controller.transform, SelectionCountTextName, "",
                new Vector2(0.05f, 0.14f), new Vector2(0.5f, 0.19f), 18);

            var clearAllButton = FindOrCreateButton(controller.transform, ClearAllButtonName, "모두 비우기",
                new Vector2(0.05f, 0.02f), new Vector2(0.45f, 0.12f));
            var executeButton = FindOrCreateButton(controller.transform, ExecuteButtonName, "강화 실행",
                new Vector2(0.55f, 0.02f), new Vector2(0.95f, 0.12f));
            var resultText = FindOrCreateText(controller.transform, ResultTextName, "",
                new Vector2(0.05f, 0.19f), new Vector2(0.95f, 0.24f), 18);

            var closeButton = FindOrCreateCloseButton(controller.transform, CloseButtonName, 64f);
            closeButton.transform.SetAsLastSibling();

            var gameActionController = Object.FindAnyObjectByType<GameActionController>(FindObjectsInactive.Include);
            if (gameActionController == null)
            {
                Debug.LogWarning("[SetupEnhanceUI] 씬에서 GameActionController를 찾지 못해 " +
                    "gameActionController 바인딩을 건너뜁니다.");
            }

            BindController(controller, gameActionController, targetPreviewCard, targetGradeText, targetLevelText,
                expFillImage, expText, materialListContainer, cardTemplate, materialListEmptyText,
                selectionCountText, clearAllButton, executeButton, resultText, closeButton);

            EditorUtility.SetDirty(controller);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning("[SetupEnhanceUI] 씬에서 UIManager를 찾지 못해 screens 등록을 건너뜁니다.");
            }
            else
            {
                RegisterScreen(uiManager, ScreenType.Enhance, controller.gameObject);
                EditorUtility.SetDirty(uiManager);
            }

            var scene = controller.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            // [TASK-KBO-146, 명령서 6항] 등록을 "시도했다"가 아니라 방금 쓴 값을 다시 읽어 실제로
            // 등록됐는지 검증한다 - `UIManager.GetScreenRoot()`는 런타임 `Awake()`가 채우는 딕셔너리를
            // 보므로 에디터 타임에는 항상 비어 있어 검증에 쓸 수 없다(`screens` SerializedProperty를
            // 직접 다시 읽어야 한다).
            bool enhanceScreenRegistered = uiManager != null && FindRegisteredScreenRoot(uiManager) == controller.gameObject;
            Debug.Log(enhanceScreenRegistered
                ? "[SetupEnhanceUI] EnhanceUI 렌더러 등록 성공 - UIManager.screens에 ScreenType.Enhance -> " +
                    $"{controller.gameObject.name} 확인."
                : "[SetupEnhanceUI] EnhanceUI 렌더러 등록 실패 - UIManager.screens에서 ScreenType.Enhance를 " +
                    "확인하지 못했습니다. UIManager가 씬에 있는지 다시 확인하십시오.");

            Debug.Log("[SetupEnhanceUI] 강화 전용 UI 자동 배선 완료.");
        }

        /// <summary>[TASK-KBO-146] `RegisterScreen()`이 방금 쓴 값을 그대로 다시 읽어 검증하는 읽기 전용
        /// 헬퍼.</summary>
        private static GameObject FindRegisteredScreenRoot(UIManager uiManager)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("Type").intValue == (int)ScreenType.Enhance)
                {
                    return element.FindPropertyRelative("Root").objectReferenceValue as GameObject;
                }
            }

            return null;
        }

        private static EnhanceUIController FindOrCreateEnhancePanel(Transform canvasTransform)
        {
            var existing = canvasTransform.Find(PanelName);
            if (existing != null)
            {
                var existingController = existing.GetComponent<EnhanceUIController>();
                if (existingController != null) return existingController;
            }

            var panelObject = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {PanelName}");
            panelObject.transform.SetParent(canvasTransform, false);

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panelObject.GetComponent<Image>().color = new Color(0.12f, 0.12f, 0.16f);

            return panelObject.AddComponent<EnhanceUIController>();
        }

        /// <summary>[명령서 6항 - 템플릿 재사용] `SetupScoutUI.cs`가 만든 `_Templates/PlayerCardTemplate`을
        /// 그대로 재사용한다.</summary>
        private static PlayerCardUI FindOrCreatePlayerCardTemplate(Transform canvasTransform)
        {
            var holderTransform = canvasTransform.Find(TemplatesHolderName);
            var existingCard = holderTransform != null ? holderTransform.Find(PlayerCardTemplateName) : null;
            if (existingCard == null)
            {
                Debug.LogWarning($"[SetupEnhanceUI] '_Templates/{PlayerCardTemplateName}'를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Setup/Auto-Connect Scout UI'를 실행해 카드 템플릿을 생성하십시오. " +
                    "카드 관련 바인딩을 건너뜁니다.");
                return null;
            }

            return existingCard.GetComponent<PlayerCardUI>();
        }

        private static PlayerCardUI FindOrCreateTargetPreviewCard(Transform parent, PlayerCardUI cardTemplate)
        {
            var existing = parent.Find(TargetPreviewCardName);
            if (existing != null)
            {
                var existingCard = existing.GetComponent<PlayerCardUI>();
                if (existingCard != null) return existingCard;
            }

            if (cardTemplate == null) return null;

            var instance = Object.Instantiate(cardTemplate, parent);
            Undo.RegisterCreatedObjectUndo(instance.gameObject, $"Create {TargetPreviewCardName}");
            instance.gameObject.name = TargetPreviewCardName;

            var rect = (RectTransform)instance.transform;
            rect.anchorMin = new Vector2(0.05f, 0.80f);
            rect.anchorMax = new Vector2(0.25f, 0.96f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return instance;
        }

        /// <summary>[명령서 4항 - 강화 경험치 바] Image.Type=Filled(Horizontal) 방식을 쓴다(Slider보다
        /// 조립이 단순하고, 이 UI에서는 드래그 입력이 필요 없는 순수 표시용이라 명령서가 허용한 두 방식
        /// 중 이쪽을 골랐다 - PlayerCardUI.staminaFillImage와 동일한 관례).</summary>
        private static (Image fill, Text text) FindOrCreateExpBar(Transform parent)
        {
            var existingBg = parent.Find(ExpBarBackgroundName);
            GameObject bgObject;
            if (existingBg != null)
            {
                bgObject = existingBg.gameObject;
            }
            else
            {
                bgObject = new GameObject(ExpBarBackgroundName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(bgObject, $"Create {ExpBarBackgroundName}");
                bgObject.transform.SetParent(parent, false);
            }

            var bgRect = (RectTransform)bgObject.transform;
            bgRect.anchorMin = new Vector2(0.05f, 0.62f);
            bgRect.anchorMax = new Vector2(0.95f, 0.72f);
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            bgObject.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f);

            var fillTransform = bgObject.transform.Find(ExpFillImageName);
            GameObject fillObject;
            if (fillTransform != null)
            {
                fillObject = fillTransform.gameObject;
            }
            else
            {
                fillObject = new GameObject(ExpFillImageName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(fillObject, $"Create {ExpFillImageName}");
                fillObject.transform.SetParent(bgObject.transform, false);
            }

            var fillRect = (RectTransform)fillObject.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var fillImage = fillObject.GetComponent<Image>();
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.color = new Color(0.3f, 0.55f, 0.95f);
            fillImage.raycastTarget = false;

            var expText = FindOrCreateText(bgObject.transform, ExpTextName, "", Vector2.zero, Vector2.one, 18);
            expText.alignment = TextAnchor.MiddleCenter;
            expText.color = Color.white;
            expText.raycastTarget = false;

            return (fillImage, expText);
        }

        /// <summary>[TASK-KBO-138] 재료 후보 목록. `SetupInventoryUI.FindOrCreateCardListPanel()`과 동일한
        /// 표준 스크롤 뷰 계층(Panel(ScrollRect) -&gt; Viewport(RectMask2D) -&gt; Content
        /// (GridLayoutGroup+ContentSizeFitter))을 새로 조립한다(각 Setup*.cs가 자체 헬퍼를 갖는 관례).</summary>
        private static (Transform content, Text emptyText) FindOrCreateMaterialListPanel(Transform parent,
            Vector2 cellSize, Vector2 spacing)
        {
            var panelTransform = parent.Find(MaterialListPanelName);
            GameObject panelObject;
            if (panelTransform != null)
            {
                panelObject = panelTransform.gameObject;
            }
            else
            {
                panelObject = new GameObject(MaterialListPanelName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(panelObject, $"Create {MaterialListPanelName}");
                panelObject.transform.SetParent(parent, false);
            }

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = new Vector2(0.05f, 0.26f);
            rect.anchorMax = new Vector2(0.95f, 0.60f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var viewportTransform = panelObject.transform.Find(ViewportName);
            GameObject viewportObject;
            if (viewportTransform != null)
            {
                viewportObject = viewportTransform.gameObject;
            }
            else
            {
                viewportObject = new GameObject(ViewportName, typeof(RectTransform), typeof(RectMask2D));
                Undo.RegisterCreatedObjectUndo(viewportObject, $"Create {ViewportName}");
                viewportObject.transform.SetParent(panelObject.transform, false);

                var viewportRect = (RectTransform)viewportObject.transform;
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = Vector2.zero;
                viewportRect.offsetMax = Vector2.zero;
            }

            var contentTransform = FindOrCreateGridContent(viewportObject.transform, MaterialListContainerName, cellSize, spacing);

            if (!panelObject.TryGetComponent<ScrollRect>(out var scrollRect))
            {
                scrollRect = panelObject.AddComponent<ScrollRect>();
            }
            scrollRect.viewport = (RectTransform)viewportObject.transform;
            scrollRect.content = (RectTransform)contentTransform;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var emptyText = FindOrCreateText(panelObject.transform, "MaterialListEmptyText",
                "강화 재료로 쓸 다른 카드가 없습니다.", Vector2.zero, Vector2.one, 20);
            emptyText.alignment = TextAnchor.MiddleCenter;
            emptyText.gameObject.SetActive(false);

            return (contentTransform, emptyText);
        }

        private static Transform FindOrCreateGridContent(Transform parent, string name, Vector2 cellSize, Vector2 spacing)
        {
            var existing = parent.Find(name);
            GameObject containerObject;
            if (existing != null)
            {
                containerObject = existing.gameObject;
            }
            else
            {
                containerObject = new GameObject(name, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(containerObject, $"Create {name}");
                containerObject.transform.SetParent(parent, false);
            }

            var rect = (RectTransform)containerObject.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            if (!containerObject.TryGetComponent<GridLayoutGroup>(out var grid))
            {
                grid = containerObject.AddComponent<GridLayoutGroup>();
            }
            grid.cellSize = cellSize;
            grid.spacing = spacing;
            grid.childAlignment = TextAnchor.UpperCenter;

            if (!containerObject.TryGetComponent<ContentSizeFitter>(out var fitter))
            {
                fitter = containerObject.AddComponent<ContentSizeFitter>();
            }
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return containerObject.transform;
        }

        private static Button FindOrCreateButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingButton = existingChild.GetComponent<Button>();
                if (existingButton != null)
                {
                    var existingRect = (RectTransform)existingButton.transform;
                    existingRect.anchorMin = anchorMin;
                    existingRect.anchorMax = anchorMax;
                    ApplyButtonLabel(existingButton, label);
                    return existingButton;
                }
            }

            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
            buttonObject.transform.SetParent(parent, false);

            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.9f, 0.9f, 0.9f);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(labelObject, $"Create {name} Label");
            labelObject.transform.SetParent(buttonObject.transform, false);

            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = labelObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 20;
            text.font = KBOFonts.Default;
            text.raycastTarget = false;
            ApplyButtonLabel(button, label);

            return button;
        }

        private static void ApplyButtonLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>(true);
            if (text == null) return;

            text.text = label;
            text.color = Color.black;
            text.resizeTextForBestFit = true;
        }

        private static Text FindOrCreateText(Transform parent, string name, string defaultText,
            Vector2 anchorMin, Vector2 anchorMax, int fontSize)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null && existingChild.TryGetComponent<Text>(out var existingText)) return existingText;

            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
            textObject.transform.SetParent(parent, false);

            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<Text>();
            text.text = defaultText;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.fontSize = fontSize;
            text.font = KBOFonts.Default;
            text.raycastTarget = false;

            return text;
        }

        /// <summary>[TASK-KBO-136 관례 재사용] 우측 상단 모서리 고정 픽셀 크기 'X' 버튼.</summary>
        private static Button FindOrCreateCloseButton(Transform parent, string name, float size)
        {
            var existingChild = parent.Find(name);
            Button button;
            GameObject buttonObject;
            if (existingChild != null && existingChild.TryGetComponent<Button>(out var existingButton))
            {
                button = existingButton;
                buttonObject = existingChild.gameObject;
            }
            else
            {
                buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
                buttonObject.transform.SetParent(parent, false);

                var image = buttonObject.GetComponent<Image>();
                image.color = new Color(0.9f, 0.9f, 0.9f);

                button = buttonObject.GetComponent<Button>();
                button.targetGraphic = image;
            }

            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(-12f, -12f);

            if (!buttonObject.TryGetComponent<LayoutElement>(out var layoutElement))
            {
                layoutElement = buttonObject.AddComponent<LayoutElement>();
            }
            layoutElement.ignoreLayout = true;

            var labelTransform = buttonObject.transform.Find("Label");
            Text text;
            if (labelTransform != null && labelTransform.TryGetComponent<Text>(out var existingText))
            {
                text = existingText;
            }
            else
            {
                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
                Undo.RegisterCreatedObjectUndo(labelObject, $"Create {name} Label");
                labelObject.transform.SetParent(buttonObject.transform, false);

                var labelRect = (RectTransform)labelObject.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                text = labelObject.GetComponent<Text>();
                text.font = KBOFonts.Default;
                text.raycastTarget = false;
            }

            text.text = "X";
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 28;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 18;
            text.resizeTextMaxSize = 40;

            return button;
        }

        private static void RegisterScreen(UIManager uiManager, ScreenType screenType, GameObject root)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                var typeProperty = element.FindPropertyRelative("Type");
                if (typeProperty.intValue == (int)screenType)
                {
                    element.FindPropertyRelative("Root").objectReferenceValue = root;
                    serializedManager.ApplyModifiedProperties();
                    return;
                }
            }

            int newIndex = screensProperty.arraySize;
            screensProperty.arraySize++;

            var newElement = screensProperty.GetArrayElementAtIndex(newIndex);
            newElement.FindPropertyRelative("Type").intValue = (int)screenType;
            newElement.FindPropertyRelative("Root").objectReferenceValue = root;

            serializedManager.ApplyModifiedProperties();
        }

        private static void BindController(EnhanceUIController controller, GameActionController gameActionController,
            PlayerCardUI targetPreviewCard, Text targetGradeText, Text targetLevelText,
            Image expFillImage, Text expText, Transform materialListContainer, PlayerCardUI materialCardPrefab,
            Text materialListEmptyText, Text selectionCountText, Button clearAllButton, Button executeButton,
            Text resultText, Button closeButton)
        {
            var serialized = new SerializedObject(controller);

            if (gameActionController != null) serialized.FindProperty("gameActionController").objectReferenceValue = gameActionController;

            if (targetPreviewCard != null) serialized.FindProperty("targetPreviewCard").objectReferenceValue = targetPreviewCard;
            serialized.FindProperty("targetGradeText").objectReferenceValue = targetGradeText;
            serialized.FindProperty("targetLevelText").objectReferenceValue = targetLevelText;

            serialized.FindProperty("expFillImage").objectReferenceValue = expFillImage;
            serialized.FindProperty("expText").objectReferenceValue = expText;

            serialized.FindProperty("materialListContainer").objectReferenceValue = materialListContainer;
            if (materialCardPrefab != null) serialized.FindProperty("materialCardPrefab").objectReferenceValue = materialCardPrefab;
            serialized.FindProperty("materialListEmptyText").objectReferenceValue = materialListEmptyText;
            serialized.FindProperty("selectionCountText").objectReferenceValue = selectionCountText;

            serialized.FindProperty("clearAllButton").objectReferenceValue = clearAllButton;
            serialized.FindProperty("executeButton").objectReferenceValue = executeButton;
            serialized.FindProperty("resultText").objectReferenceValue = resultText;

            serialized.FindProperty("closeButton").objectReferenceValue = closeButton;

            serialized.ApplyModifiedProperties();
        }
    }
}
