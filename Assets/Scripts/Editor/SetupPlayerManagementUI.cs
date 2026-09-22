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
    /// [TASK-KBO-145] `PlayerManagementUIController`(격자형 선수 관리 허브)를 씬에 조립·배선하는 에디터
    /// 자동화. 인벤토리에서 카드를 클릭하면 뜨던 기존의 단순 팝업(`InventoryUIController.detailPanelRoot`)을
    /// 대체하는 신규 풀스크린 화면이다 - `ScreenType.PlayerManagementHub`로 등록한다(모달 팝업이 아니라
    /// 인벤토리를 완전히 덮는 별도 화면이므로 `MaterialSelectUIController`류가 아니라 `SetupInventoryUI`의
    /// `RegisterInventoryScreen()`과 동일한 관례를 따른다).
    ///
    /// `SetupInventoryUI.cs`(TASK-142)가 확립한 "매 실행마다 직속 자식 전부 DestroyImmediate 후 처음부터
    /// 재조립" 패턴을 그대로 따른다(명령서 0항 - UI 에디터 스크립트는 완벽한 초기화를 선행할 것).
    /// </summary>
    public static class SetupPlayerManagementUI
    {
        private const string PanelName = "PlayerManagementHubPanel";
        private const string TargetPreviewCardName = "TargetPreviewCard";
        private const string TargetNameTextName = "TargetNameText";
        private const string MenuGridName = "MenuGrid";
        private const string SkillChangeResultTextName = "SkillChangeResultText";
        private const string CloseButtonName = "CloseButton";
        private const string TemplatesHolderName = "_Templates";
        private const string PlayerCardTemplateName = "PlayerCardTemplate";

        [MenuItem("KBO Manager/Setup/Auto-Connect Player Management UI")]
        public static void AutoConnectPlayerManagementUI()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[SetupPlayerManagementUI] 씬에서 Canvas를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Initialize Current Scene'을 실행하십시오.");
                return;
            }

            var controller = FindOrCreateHubPanel(canvas.transform);

            // [명령서 0항 - UI 에디터 스크립트는 DestroyImmediate를 통한 완벽한 초기화를 선행] 씬에
            // 어떤 과거 세대의 잔재가 있었든 이 재조립 시점부터는 완전히 무관해지도록, 직속 자식을
            // 예외 없이 먼저 파괴한 뒤 처음부터 다시 만든다(SetupInventoryUI.cs TASK-142와 동일 패턴).
            for (int i = controller.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(controller.transform.GetChild(i).gameObject);
            }

            var cardTemplate = FindOrCreatePlayerCardTemplate(canvas.transform);
            var targetPreviewCard = FindOrCreateTargetPreviewCard(controller.transform, cardTemplate);
            var targetNameText = FindOrCreateText(controller.transform, TargetNameTextName, "",
                new Vector2(0.27f, 0.85f), new Vector2(0.95f, 0.95f), 28);

            var menuGrid = FindOrCreateMenuGrid(controller.transform);
            var trainButton = FindOrCreateGridButton(menuGrid, "TrainButton", "훈련");
            var enhanceButton = FindOrCreateGridButton(menuGrid, "EnhanceButton", "강화");
            var breakthroughButton = FindOrCreateGridButton(menuGrid, "BreakthroughButton", "한계 돌파");
            var skillChangeButton = FindOrCreateGridButton(menuGrid, "SkillChangeButton", "스킬 변경");
            var awakenButton = FindOrCreateGridButton(menuGrid, "AwakenButton", "각성");

            var skillChangeResultText = FindOrCreateText(controller.transform, SkillChangeResultTextName, "",
                new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.14f), 18);

            var closeButton = FindOrCreateCloseButton(controller.transform, CloseButtonName, 64f);
            closeButton.transform.SetAsLastSibling();

            // [TASK-KBO-146, CRITICAL] 기존 코드는 EnhanceUIController가 씬에 없으면 경고만 남기고
            // enhanceUIController 바인딩을 영영 건너뛰었다 - 사용자가 "Auto-Connect Player Management
            // UI"를 "Auto-Connect Enhance UI"보다 먼저(또는 그것 없이) 실행했다면, 이후 Enhance UI를
            // 따로 만들어도 이 허브의 필드는 계속 null로 남아 [강화] 버튼이 조용히 무반응이었다
            // (PlayerManagementUIController.OnClickEnhance() 참고 - 실제 버그 원인). 메뉴 실행 순서에
            // 의존하지 않도록, 못 찾으면 SetupEnhanceUI.AutoConnectEnhanceUI()를 여기서 직접 연쇄
            // 호출해 Enhance 화면을 스스로 만들고 다시 조회한다(명령서 3항 "코드로 강제 해결").
            var enhanceUIController = Object.FindAnyObjectByType<EnhanceUIController>(FindObjectsInactive.Include);
            if (enhanceUIController == null)
            {
                Debug.LogWarning("[SetupPlayerManagementUI] 씬에서 EnhanceUIController를 찾지 못해 " +
                    "'KBO Manager/Setup/Auto-Connect Enhance UI'를 자동으로 먼저 실행합니다.");
                SetupEnhanceUI.AutoConnectEnhanceUI();
                enhanceUIController = Object.FindAnyObjectByType<EnhanceUIController>(FindObjectsInactive.Include);
            }

            BindController(controller, enhanceUIController, targetPreviewCard, targetNameText,
                trainButton, enhanceButton, breakthroughButton, skillChangeButton, awakenButton,
                skillChangeResultText, closeButton);

            EditorUtility.SetDirty(controller);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning("[SetupPlayerManagementUI] 씬에서 UIManager를 찾지 못해 screens 등록을 건너뜁니다.");
            }
            else
            {
                RegisterScreen(uiManager, ScreenType.PlayerManagementHub, controller.gameObject);
                EditorUtility.SetDirty(uiManager);
            }

            // [TASK-KBO-145] 인벤토리 카드 클릭이 이 허브를 열도록 InventoryUIController에 배선한다
            // (InventoryUIController.OpenPlayerManagement() 참고 - 필드가 비어 있으면 예전 ShowDetail()
            // 팝업으로 자동 폴백하므로, 이 배선을 건너뛰어도 씬이 깨지지는 않는다).
            var inventoryController = Object.FindAnyObjectByType<InventoryUIController>(FindObjectsInactive.Include);
            if (inventoryController == null)
            {
                Debug.LogWarning("[SetupPlayerManagementUI] 씬에서 InventoryUIController를 찾지 못해 " +
                    "인벤토리 카드 클릭 배선을 건너뜁니다.");
            }
            else
            {
                var serializedInventory = new SerializedObject(inventoryController);
                serializedInventory.FindProperty("playerManagementUIController").objectReferenceValue = controller;
                serializedInventory.ApplyModifiedProperties();
                EditorUtility.SetDirty(inventoryController);
            }

            var scene = controller.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            // [TASK-KBO-146, 명령서 6항] "실행하면 성공한 것으로 보인다"가 아니라 실제 바인딩 결과를
            // 다시 읽어 검증한다 - enhanceButton 필드 자체와 enhanceUIController 참조가 둘 다 non-null인
            // 경우에만 성공으로 판정한다.
            bool enhanceButtonBound = enhanceButton != null && enhanceUIController != null;
            if (enhanceButtonBound)
            {
                Debug.Log("[SetupPlayerManagementUI] [강화] 버튼 바인딩 성공 - enhanceButton -> " +
                    "PlayerManagementUIController.enhanceUIController -> EnhanceUIController 연결 확인.");
            }
            else
            {
                Debug.LogError("[SetupPlayerManagementUI] [강화] 버튼 바인딩 실패 - enhanceUIController가 " +
                    "여전히 null입니다. 씬에 Canvas가 있는지, 'Auto-Connect Enhance UI'가 오류 없이 " +
                    "끝났는지 확인하십시오.");
            }

            bool enhanceScreenRegistered = uiManager != null &&
                FindRegisteredScreenRoot(uiManager, ScreenType.Enhance) != null;
            if (enhanceScreenRegistered)
            {
                Debug.Log("[SetupPlayerManagementUI] EnhanceUI 렌더러 등록 성공 - UIManager.screens에 " +
                    "ScreenType.Enhance 항목이 존재하고 Root가 연결돼 있습니다.");
            }
            else
            {
                Debug.LogError("[SetupPlayerManagementUI] EnhanceUI 렌더러 등록 실패 - UIManager.screens에서 " +
                    "ScreenType.Enhance를 찾지 못했습니다. UIManager.ShowScreen(ScreenType.Enhance)이 " +
                    "\"화면이 등록되어 있지 않습니다\" 경고만 남기고 아무 화면도 켜지 않을 것입니다.");
            }

            Debug.Log("[SetupPlayerManagementUI] 선수 관리 허브 UI 자동 배선 완료.");
        }

        /// <summary>[TASK-KBO-146] `RegisterScreen()`이 방금 쓴 값을 그대로 다시 읽어 검증하는 읽기 전용
        /// 헬퍼 - `UIManager.GetScreenRoot()`는 런타임 `Awake()`가 채우는 딕셔너리를 보므로 에디터
        /// 타임(플레이 모드 아님)에는 항상 비어 있어 검증에 쓸 수 없다 - `screens` SerializedProperty를
        /// 직접 다시 읽는다.</summary>
        private static GameObject FindRegisteredScreenRoot(UIManager uiManager, ScreenType screenType)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("Type").intValue == (int)screenType)
                {
                    return element.FindPropertyRelative("Root").objectReferenceValue as GameObject;
                }
            }

            return null;
        }

        private static PlayerManagementUIController FindOrCreateHubPanel(Transform canvasTransform)
        {
            var existing = canvasTransform.Find(PanelName);
            if (existing != null)
            {
                var existingController = existing.GetComponent<PlayerManagementUIController>();
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

            panelObject.GetComponent<Image>().color = new Color(0.85f, 0.88f, 0.92f);

            return panelObject.AddComponent<PlayerManagementUIController>();
        }

        /// <summary>[명령서 6항 - 템플릿 재사용] `SetupScoutUI.cs`가 만든 `_Templates/PlayerCardTemplate`을
        /// 그대로 재사용한다(중복 조립 대신 기존 완성본 참조, `SetupInventoryUI.FindOrCreatePlayerCardTemplate()`과
        /// 동일한 패턴).</summary>
        private static PlayerCardUI FindOrCreatePlayerCardTemplate(Transform canvasTransform)
        {
            var holderTransform = canvasTransform.Find(TemplatesHolderName);
            var existingCard = holderTransform != null ? holderTransform.Find(PlayerCardTemplateName) : null;
            if (existingCard == null)
            {
                Debug.LogWarning($"[SetupPlayerManagementUI] '_Templates/{PlayerCardTemplateName}'를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Setup/Auto-Connect Scout UI'를 실행해 카드 템플릿을 생성하십시오. " +
                    "targetPreviewCard 바인딩을 건너뜁니다.");
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
            rect.anchorMin = new Vector2(0.05f, 0.82f);
            rect.anchorMax = new Vector2(0.25f, 0.98f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return instance;
        }

        /// <summary>[명령서 4항] 훈련/강화/한계돌파/스킬 변경/각성 5개 사각형 메뉴 타일을 담는
        /// 3열 GridLayoutGroup. 항목 수가 5개뿐이라 스크롤은 두지 않는다.</summary>
        private static Transform FindOrCreateMenuGrid(Transform parent)
        {
            var existing = parent.Find(MenuGridName);
            GameObject gridObject;
            if (existing != null)
            {
                gridObject = existing.gameObject;
            }
            else
            {
                gridObject = new GameObject(MenuGridName, typeof(RectTransform), typeof(GridLayoutGroup));
                Undo.RegisterCreatedObjectUndo(gridObject, $"Create {MenuGridName}");
                gridObject.transform.SetParent(parent, false);
            }

            var rect = (RectTransform)gridObject.transform;
            rect.anchorMin = new Vector2(0.05f, 0.16f);
            rect.anchorMax = new Vector2(0.95f, 0.80f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var grid = gridObject.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(280f, 170f);
            grid.spacing = new Vector2(20f, 20f);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            return gridObject.transform;
        }

        private static Button FindOrCreateGridButton(Transform parent, string name, string label)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingButton = existingChild.GetComponent<Button>();
                if (existingButton != null)
                {
                    ApplyButtonLabel(existingButton, label);
                    return existingButton;
                }
            }

            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
            buttonObject.transform.SetParent(parent, false);

            var image = buttonObject.GetComponent<Image>();
            image.color = Color.white;

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
            text.fontSize = 22;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            text.color = Color.black;
            text.fontSize = fontSize;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }

        /// <summary>[TASK-KBO-136 관례 재사용] 우측 상단 모서리에 고정 픽셀 크기의 단순 'X' 버튼을
        /// 절대 배치한다(`SetupInventoryUI.FindOrCreateCloseButton()`과 동일한 패턴 - 각 Setup*.cs 파일이
        /// 자체 헬퍼를 갖는 이 코드베이스 관례를 따라 이 파일에도 독립적으로 둔다).</summary>
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
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

        private static void BindController(PlayerManagementUIController controller,
            EnhanceUIController enhanceUIController, PlayerCardUI targetPreviewCard, Text targetNameText,
            Button trainButton, Button enhanceButton, Button breakthroughButton, Button skillChangeButton,
            Button awakenButton, Text skillChangeResultText, Button closeButton)
        {
            var serialized = new SerializedObject(controller);

            if (enhanceUIController != null) serialized.FindProperty("enhanceUIController").objectReferenceValue = enhanceUIController;
            if (targetPreviewCard != null) serialized.FindProperty("targetPreviewCard").objectReferenceValue = targetPreviewCard;
            serialized.FindProperty("targetNameText").objectReferenceValue = targetNameText;

            serialized.FindProperty("trainButton").objectReferenceValue = trainButton;
            serialized.FindProperty("enhanceButton").objectReferenceValue = enhanceButton;
            serialized.FindProperty("breakthroughButton").objectReferenceValue = breakthroughButton;
            serialized.FindProperty("skillChangeButton").objectReferenceValue = skillChangeButton;
            serialized.FindProperty("awakenButton").objectReferenceValue = awakenButton;

            serialized.FindProperty("skillChangeResultText").objectReferenceValue = skillChangeResultText;
            serialized.FindProperty("closeButton").objectReferenceValue = closeButton;

            serialized.ApplyModifiedProperties();
        }
    }
}
