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
    /// [TASK-KBO-083] 방치되어 있던 ScoutUIController(선수 카드 가챠 화면)를 QA가 메뉴 클릭 한 번으로
    /// 씬에 조립·배선할 수 있게 하는 에디터 자동화. 로비 패널에 "스카우트" 진입 버튼, 스카우트 패널에
    /// "닫기" 버튼을 만들어 바인딩하고, UIManager.screens에 Scout 화면을 등록한다. SetupShopUI.cs와
    /// 동일한 관례(이름으로 기존 오브젝트를 찾아 재사용, 없으면 생성)로 여러 번 실행해도 안전하다.
    ///
    /// [범위 제외] ScoutUIController의 resultPopupRoot/cardContainer/cardPrefab/topPullAnnouncementText
    /// (10연뽑 결과 팝업 내부 UI)는 이번 작업의 포함 범위(4개 항목: 패널/진입 버튼/닫기 버튼/screens
    /// 등록)에 없고, 명령서 5항이 "화려한 UI 디자인 배치(그리드 레이아웃 조작 등)는 생략"을 명시해
    /// 바인딩하지 않는다 - PlayerCardUI 프리팹 제작이 필요한 후속 작업이다.
    /// </summary>
    public static class SetupScoutUI
    {
        private const string CanvasName = "Canvas";
        private const string ScoutPanelName = "ScoutPanel";
        private const string ScoutButtonName = "ScoutButton";
        private const string CloseButtonName = "CloseButton";

        [MenuItem("KBO Manager/Setup/Auto-Connect Scout UI")]
        public static void AutoConnectScoutUI()
        {
            var canvas = EnsureCanvas();
            var scoutController = FindOrCreateScoutPanel(canvas.transform);
            BindScoutController(scoutController);

            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupScoutUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "'스카우트' 진입 버튼 생성 및 UIManager 등록을 건너뜁니다.");
            }
            else
            {
                var scoutButton = FindOrCreateButton(dashboard.transform, ScoutButtonName, "스카우트", new Vector2(20f, 80f));
                BindButtonField(dashboard, "scoutButton", scoutButton);
                EditorUtility.SetDirty(dashboard);

                var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
                if (uiManager == null)
                {
                    Debug.LogWarning("[SetupScoutUI] 씬에서 UIManager를 찾지 못해 screens 등록을 건너뜁니다.");
                }
                else
                {
                    RegisterScoutScreen(uiManager, scoutController.gameObject);
                    EditorUtility.SetDirty(uiManager);
                }
            }

            EditorUtility.SetDirty(scoutController);

            var scene = dashboard != null ? dashboard.gameObject.scene : scoutController.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupScoutUI] 스카우트 UI 자동 배선 완료.");
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

        /// <summary>패널을 찾거나 만든다. 화면 전체를 채우는 단순 흰 배경만 붙인다(명령서 5항 - 그리드
        /// 레이아웃 등 화려한 디자인 배치는 생략).</summary>
        private static ScoutUIController FindOrCreateScoutPanel(Transform canvasTransform)
        {
            var existingChild = canvasTransform.Find(ScoutPanelName);
            if (existingChild != null)
            {
                var existingController = existingChild.GetComponent<ScoutUIController>();
                if (existingController != null) return existingController;
            }

            var panelObject = new GameObject(ScoutPanelName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {ScoutPanelName}");
            panelObject.transform.SetParent(canvasTransform, false);

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panelObject.GetComponent<Image>().color = Color.white;

            return panelObject.AddComponent<ScoutUIController>();
        }

        /// <summary>ScoutPanel에 "닫기" 버튼만 만들어 바인딩한다(포함 범위 3번째 항목).</summary>
        private static void BindScoutController(ScoutUIController controller)
        {
            var closeButton = FindOrCreateButton(controller.transform, CloseButtonName, "닫기", new Vector2(20f, 20f));
            BindButtonField(controller, "closeButton", closeButton);
        }

        private static Button FindOrCreateButton(Transform parent, string name, string label, Vector2 anchoredPosition)
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

        private static void BindButtonField(Object controller, string fieldName, Button button)
        {
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty(fieldName).objectReferenceValue = button;
            serializedController.ApplyModifiedProperties();
        }

        /// <summary>
        /// UIManager.screens(List&lt;ScreenEntry&gt;, 필드는 Type/Root - SceneInitializer/SetupShopUI에서
        /// 이미 검증된 실제 필드명)에 Scout 항목을 등록한다. 이미 등록되어 있으면 Root 참조만 최신
        /// 오브젝트로 덮어써 중복 추가를 막는다(명령서 7항).
        /// </summary>
        private static void RegisterScoutScreen(UIManager uiManager, GameObject scoutRoot)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                var typeProperty = element.FindPropertyRelative("Type");
                if (typeProperty.intValue == (int)ScreenType.Scout)
                {
                    element.FindPropertyRelative("Root").objectReferenceValue = scoutRoot;
                    serializedManager.ApplyModifiedProperties();
                    return;
                }
            }

            int newIndex = screensProperty.arraySize;
            screensProperty.arraySize++;

            var newElement = screensProperty.GetArrayElementAtIndex(newIndex);
            newElement.FindPropertyRelative("Type").intValue = (int)ScreenType.Scout;
            newElement.FindPropertyRelative("Root").objectReferenceValue = scoutRoot;

            serializedManager.ApplyModifiedProperties();
        }
    }
}
