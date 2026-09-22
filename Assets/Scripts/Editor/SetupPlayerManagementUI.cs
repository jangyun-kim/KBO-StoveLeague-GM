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
    ///
    /// [TASK-KBO-148] 허브 상단 타겟 카드(초상화)에 클릭 감지용 Button을 배선하고, TASK-147이 만든 4탭
    /// 상세 창(`PlayerDetailUIController`, 실체는 `SetupInventoryUI.cs`가 `InventoryPanel` 하위에 조립하는
    /// `DetailPanel`)을 찾아 `PlayerManagementUIController.playerDetailUIController`에 연결한다. 씬에
    /// 아직 없으면(=`Auto-Connect Inventory UI`를 이 메뉴보다 먼저 실행한 적이 없으면) TASK-146이 확립한
    /// "메뉴 실행 순서 의존성 제거" 패턴 그대로 `SetupInventoryUI.AutoConnectInventoryUI()`를 직접 연쇄
    /// 호출해 스스로 만든 뒤 다시 조회한다.
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
            // [TASK-KBO-148] targetPreviewCard는 _Templates/PlayerCardTemplate을 복제한 것이라 이미
            // Button 컴포넌트를 갖고 있다(인벤토리 목록 카드 클릭용으로 SetupInventoryUI.cs가 붙여 둔
            // 것이 그대로 복제됨) - 새로 만들 필요 없이 그 Button을 그대로 찾아 재사용한다(없으면 방어적으로
            // 추가). 클릭 리스너 자체는 PlayerManagementUIController.Awake()가 붙인다(명령서 4항).
            var cardClickButton = EnsureCardClickButton(targetPreviewCard);
            var targetNameText = FindOrCreateText(controller.transform, TargetNameTextName, "",
                new Vector2(0.27f, 0.85f), new Vector2(0.95f, 0.95f), 28);

            var menuGrid = FindOrCreateMenuGrid(controller.transform);
            // [TASK-KBO-152, 명령서 4항] isReady는 PlayerManagementUIController.Awake()의 실제 리스너
            // 배선(강화/스킬 변경만 OnClickXxx, 나머지는 LogNotReady)과 정확히 일치시켰다.
            var trainButton = FindOrCreateGridButton(menuGrid, "TrainButton", "훈련", isReady: false);
            var enhanceButton = FindOrCreateGridButton(menuGrid, "EnhanceButton", "강화", isReady: true);
            var breakthroughButton = FindOrCreateGridButton(menuGrid, "BreakthroughButton", "한계 돌파", isReady: false);
            var skillChangeButton = FindOrCreateGridButton(menuGrid, "SkillChangeButton", "스킬 변경", isReady: true);
            var awakenButton = FindOrCreateGridButton(menuGrid, "AwakenButton", "각성", isReady: false);

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

            // [TASK-KBO-148, CRITICAL] EnhanceUIController와 동일한 이유로 - PlayerDetailUIController를
            // 못 찾으면(=Auto-Connect Inventory UI를 아직 실행한 적이 없으면) 경고만 남기고 영영 null로
            // 두지 않는다. 여기서 직접 SetupInventoryUI.AutoConnectInventoryUI()를 연쇄 호출해 스스로
            // 만든 뒤 다시 조회한다(메뉴 실행 순서 의존성 제거, TASK-146과 동일 패턴).
            var playerDetailUIController = Object.FindAnyObjectByType<PlayerDetailUIController>(FindObjectsInactive.Include);
            if (playerDetailUIController == null)
            {
                Debug.LogWarning("[SetupPlayerManagementUI] 씬에서 PlayerDetailUIController를 찾지 못해 " +
                    "'KBO Manager/Setup/Auto-Connect Inventory UI'를 자동으로 먼저 실행합니다.");
                SetupInventoryUI.AutoConnectInventoryUI();
                playerDetailUIController = Object.FindAnyObjectByType<PlayerDetailUIController>(FindObjectsInactive.Include);
            }

            BindController(controller, enhanceUIController, playerDetailUIController, targetPreviewCard,
                targetNameText, cardClickButton, trainButton, enhanceButton, breakthroughButton,
                skillChangeButton, awakenButton, skillChangeResultText, closeButton);

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

            // [TASK-KBO-148, 명령서 7항] cardClickButton -> playerDetailUIController 배선도 동일하게
            // 검증 로그를 남긴다(TASK-146이 확립한 관례).
            bool cardClickBound = cardClickButton != null && playerDetailUIController != null;
            if (cardClickBound)
            {
                Debug.Log("[SetupPlayerManagementUI] 타겟 카드(초상화) 클릭 바인딩 성공 - cardClickButton -> " +
                    "PlayerManagementUIController.playerDetailUIController -> PlayerDetailUIController 연결 확인.");
            }
            else
            {
                Debug.LogError("[SetupPlayerManagementUI] 타겟 카드(초상화) 클릭 바인딩 실패 - " +
                    "playerDetailUIController가 여전히 null입니다. 'Auto-Connect Inventory UI'가 오류 없이 " +
                    "끝났는지 확인하십시오.");
            }

            Debug.Log("[SetupPlayerManagementUI] 선수 관리 허브 UI 자동 배선 완료.");
        }

        /// <summary>[TASK-KBO-148] `targetPreviewCard`(카드 템플릿 복제본)가 이미 갖고 있는 `Button`
        /// 컴포넌트를 그대로 재사용한다 - 카드 템플릿(`_Templates/PlayerCardTemplate`)은
        /// `SetupInventoryUI.FindOrCreatePlayerCardTemplate()`이 인벤토리 목록 클릭용으로 이미 `Button`을
        /// 붙여 두므로, 이를 복제한 이 미리보기 카드도 태어날 때부터 `Button`을 갖고 있다. 혹시 없는
        /// 경우(카드 템플릿이 아직 준비되지 않은 극단적 상황)에만 방어적으로 새로 추가한다.</summary>
        private static Button EnsureCardClickButton(PlayerCardUI card)
        {
            if (card == null) return null;

            if (card.TryGetComponent<Button>(out var button)) return button;

            button = card.gameObject.AddComponent<Button>();
            if (card.TryGetComponent<Image>(out var image)) button.targetGraphic = image;
            return button;
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

        /// <summary>[TASK-KBO-152, 명령서 4항] 준비 중인 메뉴(`isReady=false`)는 배경/라벨을 흐리게
        /// 딤(Dim) 처리한다. 재사용(reuse) 경로에서도 딤 상태를 항상 재적용해, 메뉴를 다시 실행하면
        /// 매번 최신 준비 상태를 반영하도록 한다(생성/재사용 분기 밖에서 무조건 실행).</summary>
        private static Button FindOrCreateGridButton(Transform parent, string name, string label, bool isReady)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingButton = existingChild.GetComponent<Button>();
                if (existingButton != null)
                {
                    ApplyButtonLabel(existingButton, label);
                    ApplyReadyState(existingButton, isReady);
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
            ApplyReadyState(button, isReady);

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

        /// <summary>[TASK-KBO-152] 준비 중 메뉴는 배경을 반투명 회색, 라벨을 짙은 회색으로 낮춰
        /// "비활성" 느낌을 시각적으로 준다. 클릭 자체는 여전히 동작한다(PlayerManagementUIController가
        /// LogNotReady()로 응답) - 이 작업은 순수 시각 폴리싱이며 클릭/라우팅 로직은 건드리지 않는다.</summary>
        private static void ApplyReadyState(Button button, bool isReady)
        {
            var image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = isReady ? Color.white : new Color(0.6f, 0.6f, 0.6f, 0.55f);
            }

            var text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.color = isReady ? Color.black : new Color(0.35f, 0.35f, 0.35f, 0.9f);
            }
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
            EnhanceUIController enhanceUIController, PlayerDetailUIController playerDetailUIController,
            PlayerCardUI targetPreviewCard, Text targetNameText, Button cardClickButton,
            Button trainButton, Button enhanceButton, Button breakthroughButton, Button skillChangeButton,
            Button awakenButton, Text skillChangeResultText, Button closeButton)
        {
            var serialized = new SerializedObject(controller);

            if (enhanceUIController != null) serialized.FindProperty("enhanceUIController").objectReferenceValue = enhanceUIController;
            if (playerDetailUIController != null) serialized.FindProperty("playerDetailUIController").objectReferenceValue = playerDetailUIController;
            if (targetPreviewCard != null) serialized.FindProperty("targetPreviewCard").objectReferenceValue = targetPreviewCard;
            serialized.FindProperty("targetNameText").objectReferenceValue = targetNameText;
            if (cardClickButton != null) serialized.FindProperty("cardClickButton").objectReferenceValue = cardClickButton;

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
