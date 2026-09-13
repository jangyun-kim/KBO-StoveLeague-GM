using KBOManager.Controllers;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-067] TASK-KBO-066에서 스크립트로만 만든 CheerleaderShopUIController를 QA가 메뉴
    /// 클릭 한 번으로 씬에 조립·배선할 수 있게 하는 에디터 자동화. 로비 패널에 "치어리더 뽑기" 진입
    /// 버튼, 상점 패널에 5개 UI 컴포넌트를 만들어 바인딩하고, UIManager.screens에 CheerleaderShop
    /// 화면을 등록한다. SetupLobbyUI/SetupCheerleaderUI/SetupRoutingUI와 동일한 관례로 여러 번
    /// 실행해도 안전하다(이미 있으면 찾아 재사용/덮어쓰기).
    /// </summary>
    public static class SetupShopUI
    {
        private const string CanvasName = "Canvas";
        private const string ShopPanelName = "CheerleaderShopPanel";
        private const string PremiumCurrencyTextName = "PremiumCurrencyText";
        private const string Roll1xButtonName = "Roll1xButton";
        private const string Roll10xButtonName = "Roll10xButton";
        private const string CloseButtonName = "CloseButton";
        private const string ResultLogTextName = "ResultLogText";
        private const string GachaShopButtonName = "GachaShopButton";

        [MenuItem("KBO Manager/Setup/Auto-Create Gacha Shop UI")]
        public static void AutoCreateShopUI()
        {
            var canvas = EnsureCanvas();
            var shopController = FindOrCreateShopPanel(canvas.transform);
            BindShopController(shopController);

            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupShopUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "'치어리더 뽑기' 진입 버튼 생성 및 UIManager 등록을 건너뜁니다.");
            }
            else
            {
                var gachaShopButton = FindOrCreateButton(dashboard.transform, GachaShopButtonName, "치어리더 뽑기");
                BindButtonField(dashboard, "gachaShopButton", gachaShopButton);
                EditorUtility.SetDirty(dashboard);

                var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
                if (uiManager == null)
                {
                    Debug.LogWarning("[SetupShopUI] 씬에서 UIManager를 찾지 못해 screens 등록을 건너뜁니다.");
                }
                else
                {
                    RegisterShopScreen(uiManager, shopController.gameObject);
                    EditorUtility.SetDirty(uiManager);
                }
            }

            EditorUtility.SetDirty(shopController);

            var scene = dashboard != null ? dashboard.gameObject.scene : shopController.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupShopUI] 치어리더 가챠 상점 UI 자동 생성/바인딩 완료.");
        }

        private static Canvas EnsureCanvas()
        {
            var existing = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (existing != null) return existing;

            var canvasObject = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Canvas");

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }

        /// <summary>패널을 찾거나 만든다. 화려한 그래픽 없이 넓은 흰색 배경 + VerticalLayoutGroup으로
        /// 5개 자식(텍스트/버튼)을 위에서 아래로 단순 나열한다(명령서 5항 - 아트 에셋 적용 금지).</summary>
        private static CheerleaderShopUIController FindOrCreateShopPanel(Transform canvasTransform)
        {
            var existingChild = canvasTransform.Find(ShopPanelName);
            if (existingChild != null)
            {
                var existingController = existingChild.GetComponent<CheerleaderShopUIController>();
                if (existingController != null) return existingController;
            }

            var panelObject = new GameObject(ShopPanelName, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {ShopPanelName}");
            panelObject.transform.SetParent(canvasTransform, false);

            // 넓게(명령서 6항 2번) - 화면 중앙에 큼직한 고정 크기로 배치한다. 정밀 앵커/디자인은
            // 이번 작업 범위 밖이며, QA가 필요하면 인스펙터에서 직접 조정하면 된다.
            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(700f, 640f);
            rect.anchoredPosition = Vector2.zero;

            panelObject.GetComponent<Image>().color = Color.white;

            var layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            return panelObject.AddComponent<CheerleaderShopUIController>();
        }

        private static void BindShopController(CheerleaderShopUIController controller)
        {
            var parent = controller.transform;

            var premiumCurrencyText = FindOrCreateText(parent, PremiumCurrencyTextName, 40f, isDisplayOnly: true);
            var roll1xButton = FindOrCreateButton(parent, Roll1xButtonName, "1회 뽑기 (100)");
            var roll10xButton = FindOrCreateButton(parent, Roll10xButtonName, "10회 뽑기 (1000)");
            var closeButton = FindOrCreateButton(parent, CloseButtonName, "닫기");
            // 10연뽑 결과 10줄이 스크롤 없이도 충분히 보이도록 높이를 크게 준다(명령서 6항 3번).
            var resultLogText = FindOrCreateText(parent, ResultLogTextName, 320f, isDisplayOnly: true);

            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("premiumCurrencyText").objectReferenceValue = premiumCurrencyText;
            serializedController.FindProperty("roll1xButton").objectReferenceValue = roll1xButton;
            serializedController.FindProperty("roll10xButton").objectReferenceValue = roll10xButton;
            serializedController.FindProperty("closeButton").objectReferenceValue = closeButton;
            serializedController.FindProperty("resultLogText").objectReferenceValue = resultLogText;
            serializedController.ApplyModifiedProperties();
        }

        /// <summary>
        /// 이름으로 기존 Text를 재사용하거나 새로 만든다. isDisplayOnly가 true면(순수 표시용 텍스트 -
        /// PremiumCurrencyText/ResultLogText처럼 클릭을 받을 필요가 없는 텍스트) raycastTarget을 꺼
        /// 불필요한 레이캐스트 대상에서 제외한다(명령서 9항 - 퍼포먼스 디테일).
        /// </summary>
        private static Text FindOrCreateText(Transform parent, string name, float preferredHeight, bool isDisplayOnly)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingText = existingChild.GetComponent<Text>();
                if (existingText != null) return existingText;
            }

            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
            textObject.transform.SetParent(parent, false);

            textObject.GetComponent<LayoutElement>().preferredHeight = preferredHeight;

            var text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.black;
            text.fontSize = 20;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = !isDisplayOnly;

            return text;
        }

        private static Button FindOrCreateButton(Transform parent, string name, string label)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingButton = existingChild.GetComponent<Button>();
                if (existingButton != null) return existingButton;
            }

            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
            buttonObject.transform.SetParent(parent, false);

            buttonObject.GetComponent<LayoutElement>().preferredHeight = 56f;

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
            // 버튼 라벨은 어차피 부모 Button의 Image가 클릭을 받으므로 라벨 자체는 레이캐스트 대상일
            // 필요가 없다(명령서 9항과 동일한 취지의 최적화 - 클릭 처리에는 영향 없음).
            text.raycastTarget = false;

            return button;
        }

        private static void BindButtonField(Object controller, string fieldName, Button button)
        {
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty(fieldName).objectReferenceValue = button;
            serializedController.ApplyModifiedProperties();
        }

        /// <summary>
        /// UIManager.screens(List&lt;ScreenEntry&gt;, 필드는 Type/Root - SceneInitializer/SetupRoutingUI
        /// 에서 이미 검증된 실제 필드명)에 CheerleaderShop 항목을 등록한다. 이미 등록되어 있으면
        /// Root 참조만 최신 오브젝트로 덮어써 중복 추가를 막는다(명령서 7항).
        /// </summary>
        private static void RegisterShopScreen(UIManager uiManager, GameObject shopRoot)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                var typeProperty = element.FindPropertyRelative("Type");
                if (typeProperty.intValue == (int)ScreenType.CheerleaderShop)
                {
                    element.FindPropertyRelative("Root").objectReferenceValue = shopRoot;
                    serializedManager.ApplyModifiedProperties();
                    return;
                }
            }

            int newIndex = screensProperty.arraySize;
            screensProperty.arraySize++;

            var newElement = screensProperty.GetArrayElementAtIndex(newIndex);
            newElement.FindPropertyRelative("Type").intValue = (int)ScreenType.CheerleaderShop;
            newElement.FindPropertyRelative("Root").objectReferenceValue = shopRoot;

            serializedManager.ApplyModifiedProperties();
        }
    }
}
