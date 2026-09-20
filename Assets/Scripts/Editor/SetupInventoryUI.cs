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
    /// [TASK-KBO-110] TASK-109가 지적한 "진입점 부재"를 해소한다 - `InventoryUIController`(원문 14개
    /// `[SerializeField]` 전수 확인)를 씬에 조립하고, 씬에 이미 존재하는(TASK-109)
    /// `MaterialSelectUIController`를 `materialSelectUIController` 필드에 연결해 [강화하기]/[각성하기]
    /// 버튼이 실제로 그 팝업을 열 수 있게 한다. 카드 프리팹은 `SetupScoutUI.cs`(TASK-092)가 만든
    /// `_Templates/PlayerCardTemplate`을 그대로 재사용한다(TASK-109가 이미 `Button`을 추가해 둬서
    /// `InventoryUIController.SpawnCard()`의 `GetComponent&lt;Button&gt;()` 요구도 그대로 충족된다).
    ///
    /// [결정 필요 아님, 명령서 4항 이행을 위한 최소 코드 변경] "로비 화면에 진입 버튼을 생성하고 리스너
    /// 연결"을 위해서는 `LeagueDashboardUIController`에 그 버튼을 담을 필드가 있어야 하는데,
    /// `shopButton`/`leagueStatsButton`(TASK-103/107/108)과 달리 인벤토리용 필드는 애초에 존재하지
    /// 않았다(원문 55~74행 재확인, `ScreenType.Inventory`는 이미 예약돼 있었으나(62~63행 주석) 이를 쓰는
    /// 버튼 필드가 없었음). `scoutButton`/`rosterButton` 등 기존 6개 버튼과 완전히 동일한 관례로
    /// `inventoryButton` 필드 1개와 `Awake()`의 리스너 1줄만 최소 추가했다(`LeagueDashboardUIController.cs`,
    /// 비즈니스 로직 무관 - 명령서 5항이 금지한 "인벤토리 정렬/필터링/렌더링 로직"이 아니다).
    ///
    /// [결정 필요, 미해소] `InventoryUIController`에는 상세 패널을 닫거나 인벤토리 화면 자체에서 로비로
    /// 돌아가는 버튼 필드가 원문에 전혀 없다(`ScoutUIController.closeButton`과 달리) - `CloseDetail()`은
    /// public이지만 이를 호출하는 UI 트리거가 설계돼 있지 않다. 명령서 5항이 "비즈니스 로직 수정 금지"를
    /// 명시해 새 필드를 추가로 발명하지 않고 이 상태 그대로 두었다 - 한 번 인벤토리 화면에 진입하면
    /// (다른 화면의 진입 버튼을 다시 누르기 전까지는) 상세 패널이나 화면 자체를 닫을 UI 수단이 없다.
    /// </summary>
    public static class SetupInventoryUI
    {
        private const string PanelName = "InventoryPanel";
        private const string CardContainerName = "CardContainer";
        private const string DetailPanelName = "DetailPanel";
        private const string DetailPreviewCardName = "DetailPreviewCard";
        private const string DetailReinforceTextName = "DetailReinforceText";
        private const string DetailAwakenTextName = "DetailAwakenText";
        private const string DetailSkillsTextName = "DetailSkillsText";
        private const string EnhanceButtonName = "EnhanceButton";
        private const string AwakenButtonName = "AwakenButton";
        private const string SkillChangeButtonName = "SkillChangeButton";
        private const string SkillRerollResultTextName = "SkillRerollResultText";
        private const string DetailCardFlashImageName = "DetailCardFlashImage";
        private const string InventoryButtonName = "InventoryButton";
        private const string TemplatesHolderName = "_Templates";
        private const string PlayerCardTemplateName = "PlayerCardTemplate";

        [MenuItem("KBO Manager/Setup/Auto-Connect Inventory UI")]
        public static void AutoConnectInventoryUI()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[SetupInventoryUI] 씬에서 Canvas를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Initialize Current Scene'을 실행하십시오.");
                return;
            }

            var controller = FindOrCreateInventoryPanel(canvas.transform);

            var cardContainer = FindOrCreateGridContainer(controller.transform, CardContainerName,
                new Vector2(140f, 200f), new Vector2(10f, 10f));
            var cardPrefab = FindOrCreatePlayerCardTemplate(canvas.transform);

            var detailPanelRoot = FindOrCreateDetailPanel(controller.transform);
            var detailPreviewCard = FindOrCreateDetailPreviewCard(detailPanelRoot.transform, cardPrefab);
            var detailReinforceText = FindOrCreateText(detailPanelRoot.transform, DetailReinforceTextName, "",
                new Vector2(0.35f, 0.6f), new Vector2(1f, 0.68f));
            var detailAwakenText = FindOrCreateText(detailPanelRoot.transform, DetailAwakenTextName, "",
                new Vector2(0.35f, 0.5f), new Vector2(1f, 0.58f));
            var detailSkillsText = FindOrCreateText(detailPanelRoot.transform, DetailSkillsTextName, "",
                new Vector2(0.35f, 0.4f), new Vector2(1f, 0.48f));
            var skillRerollResultText = FindOrCreateText(detailPanelRoot.transform, SkillRerollResultTextName, "",
                new Vector2(0.35f, 0.3f), new Vector2(1f, 0.38f));

            var enhanceButton = FindOrCreateButton(detailPanelRoot.transform, EnhanceButtonName, "강화하기",
                new Vector2(0.35f, 0.15f), new Vector2(0.55f, 0.25f));
            var awakenButton = FindOrCreateButton(detailPanelRoot.transform, AwakenButtonName, "각성하기",
                new Vector2(0.57f, 0.15f), new Vector2(0.77f, 0.25f));
            var skillChangeButton = FindOrCreateButton(detailPanelRoot.transform, SkillChangeButtonName, "스킬 변경",
                new Vector2(0.35f, 0.03f), new Vector2(0.55f, 0.13f));

            var detailCardFlashImage = FindOrCreateImage(detailPanelRoot.transform, DetailCardFlashImageName,
                new Vector2(0.05f, 0.4f), new Vector2(0.3f, 0.9f));

            var materialSelectUIController = Object.FindAnyObjectByType<MaterialSelectUIController>(FindObjectsInactive.Include);
            if (materialSelectUIController == null)
            {
                Debug.LogWarning("[SetupInventoryUI] 씬에서 MaterialSelectUIController를 찾지 못해 " +
                    "materialSelectUIController 바인딩을 건너뜁니다. 먼저 'KBO Manager/Setup/" +
                    "Auto-Connect Upgrade UI'(TASK-KBO-109)를 실행하십시오.");
            }

            var gameActionController = Object.FindAnyObjectByType<GameActionController>(FindObjectsInactive.Include);
            if (gameActionController == null)
            {
                Debug.LogWarning("[SetupInventoryUI] 씬에서 GameActionController를 찾지 못해 " +
                    "gameActionController 바인딩을 건너뜁니다.");
            }

            BindController(controller, materialSelectUIController, gameActionController, cardContainer, cardPrefab,
                detailPanelRoot, detailPreviewCard, detailReinforceText, detailAwakenText, detailSkillsText,
                enhanceButton, awakenButton, skillChangeButton, skillRerollResultText, detailCardFlashImage);

            EditorUtility.SetDirty(controller);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning("[SetupInventoryUI] 씬에서 UIManager를 찾지 못해 screens 등록을 건너뜁니다.");
            }
            else
            {
                RegisterInventoryScreen(uiManager, controller.gameObject);
                EditorUtility.SetDirty(uiManager);
            }

            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupInventoryUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "로비 진입 버튼 생성을 건너뜁니다.");
            }
            else
            {
                // 기존 좌측 하단 버튼 열(manageCheerleaderButton 20/scoutButton 80/rosterButton 140/
                // quickPlayButton 200/shopButton 260/leagueStatsButton 320, SetupLeagueUI.cs 참고)에
                // 이어 380에 배치한다.
                var inventoryButton = FindOrCreateDashboardButton(dashboard.transform, InventoryButtonName,
                    "선수 관리", new Vector2(20f, 380f));
                BindButtonField(dashboard, "inventoryButton", inventoryButton);
                EditorUtility.SetDirty(dashboard);
            }

            var scene = controller.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupInventoryUI] 인벤토리 UI 자동 배선 완료.");
        }

        private static InventoryUIController FindOrCreateInventoryPanel(Transform canvasTransform)
        {
            var existing = canvasTransform.Find(PanelName);
            if (existing != null)
            {
                var existingController = existing.GetComponent<InventoryUIController>();
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

            panelObject.GetComponent<Image>().color = Color.white;

            return panelObject.AddComponent<InventoryUIController>();
        }

        private static Transform FindOrCreateGridContainer(Transform parent, string name, Vector2 cellSize, Vector2 spacing)
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

                var rect = (RectTransform)containerObject.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            if (!containerObject.TryGetComponent<GridLayoutGroup>(out var grid))
            {
                grid = containerObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = cellSize;
                grid.spacing = spacing;
            }

            return containerObject.transform;
        }

        /// <summary>
        /// [명령서 6항 - 템플릿 재사용] `SetupScoutUI.cs`(TASK-092)가 만든 `_Templates/PlayerCardTemplate`을
        /// 그대로 재사용한다(중복 조립 대신 기존 완성본 참조). `Button`은 TASK-109(`SetupUpgradeUI.cs`)가
        /// 이미 없을 때만 추가해 둔 상태라 `InventoryUIController.SpawnCard()`의 `GetComponent&lt;Button&gt;()`
        /// 요구도 이미 충족돼 있다 - 여기서는 존재 여부만 확인하고 없으면 경고 후 건너뛴다(명령서 7항).
        /// </summary>
        private static PlayerCardUI FindOrCreatePlayerCardTemplate(Transform canvasTransform)
        {
            var holderTransform = canvasTransform.Find(TemplatesHolderName);
            var existingCard = holderTransform != null ? holderTransform.Find(PlayerCardTemplateName) : null;
            if (existingCard == null)
            {
                Debug.LogWarning($"[SetupInventoryUI] '_Templates/{PlayerCardTemplateName}'를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Setup/Auto-Connect Scout UI'를 실행해 카드 템플릿을 생성하십시오. " +
                    "cardPrefab 바인딩을 건너뜁니다.");
                return null;
            }

            if (!existingCard.TryGetComponent<Button>(out _))
            {
                var button = existingCard.gameObject.AddComponent<Button>();
                if (existingCard.TryGetComponent<Image>(out var image)) button.targetGraphic = image;
            }

            return existingCard.GetComponent<PlayerCardUI>();
        }

        /// <summary>상세 패널. 평소 비활성 상태로 둔다 - `InventoryUIController.Awake()`가 `CloseDetail()`로
        /// 다시 한번 비활성화하지만, 라이브 에디터에서 메뉴 실행 직후 미리보기 화면이 어색하게 뜨지
        /// 않도록 생성 시점에도 명시적으로 꺼 둔다(`ScoutUIController.resultPopupRoot`와 동일한 관례).</summary>
        private static GameObject FindOrCreateDetailPanel(Transform parent)
        {
            var existing = parent.Find(DetailPanelName);
            if (existing != null) return existing.gameObject;

            var panelObject = new GameObject(DetailPanelName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {DetailPanelName}");
            panelObject.transform.SetParent(parent, false);

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panelObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
            panelObject.SetActive(false);

            return panelObject;
        }

        /// <summary>`cardPrefab`(템플릿)을 복제해 상세 패널 전용 미리보기 카드 인스턴스를 만든다
        /// (풀링 대상인 목록 카드와 달리 항상 하나만 존재하는 고정 인스턴스). 템플릿을 찾지 못했으면
        /// (cardPrefab == null) 미리보기 카드도 만들지 않고 null을 반환한다(명령서 7항 - 안전한 스킵).</summary>
        private static PlayerCardUI FindOrCreateDetailPreviewCard(Transform detailPanelTransform, PlayerCardUI cardPrefab)
        {
            var existing = detailPanelTransform.Find(DetailPreviewCardName);
            if (existing != null)
            {
                var existingCard = existing.GetComponent<PlayerCardUI>();
                if (existingCard != null) return existingCard;
            }

            if (cardPrefab == null) return null;

            var instance = Object.Instantiate(cardPrefab, detailPanelTransform);
            Undo.RegisterCreatedObjectUndo(instance.gameObject, $"Create {DetailPreviewCardName}");
            instance.gameObject.name = DetailPreviewCardName;

            var rect = (RectTransform)instance.transform;
            rect.anchorMin = new Vector2(0.05f, 0.4f);
            rect.anchorMax = new Vector2(0.3f, 0.9f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return instance;
        }

        private static Image FindOrCreateImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null && existingChild.TryGetComponent<Image>(out var existingImage)) return existingImage;

            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(imageObject, $"Create {name}");
            imageObject.transform.SetParent(parent, false);

            var rect = (RectTransform)imageObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = imageObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f); // 평소엔 투명 - VFXController가 강화 연출 시에만 색을 바꿔 재생한다.
            image.raycastTarget = false;

            return image;
        }

        private static void RegisterInventoryScreen(UIManager uiManager, GameObject inventoryRoot)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                var typeProperty = element.FindPropertyRelative("Type");
                if (typeProperty.intValue == (int)ScreenType.Inventory)
                {
                    element.FindPropertyRelative("Root").objectReferenceValue = inventoryRoot;
                    serializedManager.ApplyModifiedProperties();
                    return;
                }
            }

            int newIndex = screensProperty.arraySize;
            screensProperty.arraySize++;

            var newElement = screensProperty.GetArrayElementAtIndex(newIndex);
            newElement.FindPropertyRelative("Type").intValue = (int)ScreenType.Inventory;
            newElement.FindPropertyRelative("Root").objectReferenceValue = inventoryRoot;

            serializedManager.ApplyModifiedProperties();
        }

        private static void BindController(InventoryUIController controller,
            MaterialSelectUIController materialSelectUIController, GameActionController gameActionController,
            Transform cardContainer, PlayerCardUI cardPrefab, GameObject detailPanelRoot, PlayerCardUI detailPreviewCard,
            Text detailReinforceText, Text detailAwakenText, Text detailSkillsText,
            Button enhanceButton, Button awakenButton, Button skillChangeButton,
            Text skillRerollResultText, Image detailCardFlashImage)
        {
            var serialized = new SerializedObject(controller);

            if (materialSelectUIController != null) serialized.FindProperty("materialSelectUIController").objectReferenceValue = materialSelectUIController;
            if (gameActionController != null) serialized.FindProperty("gameActionController").objectReferenceValue = gameActionController;

            serialized.FindProperty("cardContainer").objectReferenceValue = cardContainer;
            if (cardPrefab != null) serialized.FindProperty("cardPrefab").objectReferenceValue = cardPrefab;

            serialized.FindProperty("detailPanelRoot").objectReferenceValue = detailPanelRoot;
            if (detailPreviewCard != null) serialized.FindProperty("detailPreviewCard").objectReferenceValue = detailPreviewCard;
            serialized.FindProperty("detailReinforceText").objectReferenceValue = detailReinforceText;
            serialized.FindProperty("detailAwakenText").objectReferenceValue = detailAwakenText;
            serialized.FindProperty("detailSkillsText").objectReferenceValue = detailSkillsText;
            serialized.FindProperty("enhanceButton").objectReferenceValue = enhanceButton;
            serialized.FindProperty("awakenButton").objectReferenceValue = awakenButton;
            serialized.FindProperty("skillChangeButton").objectReferenceValue = skillChangeButton;
            serialized.FindProperty("skillRerollResultText").objectReferenceValue = skillRerollResultText;
            serialized.FindProperty("detailCardFlashImage").objectReferenceValue = detailCardFlashImage;

            serialized.ApplyModifiedProperties();
        }

        private static void BindButtonField(LeagueDashboardUIController dashboard, string fieldName, Button button)
        {
            var serialized = new SerializedObject(dashboard);
            serialized.FindProperty(fieldName).objectReferenceValue = button;
            serialized.ApplyModifiedProperties();
        }

        private static Button FindOrCreateDashboardButton(Transform parent, string name, string label, Vector2 anchoredPosition)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingButton = existingChild.GetComponent<Button>();
                if (existingButton != null) return existingButton;
            }

            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
            buttonObject.transform.SetParent(parent, false);

            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(160f, 50f);
            rect.anchoredPosition = anchoredPosition;

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
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 18;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return button;
        }

        private static Button FindOrCreateButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingButton = existingChild.GetComponent<Button>();
                if (existingButton != null) return existingButton;
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
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return button;
        }

        private static Text FindOrCreateText(Transform parent, string name, string defaultText, Vector2 anchorMin, Vector2 anchorMax)
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
            text.color = Color.white; // 상세 패널 배경(반투명 검정)과 대비시킨다.
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }
    }
}
