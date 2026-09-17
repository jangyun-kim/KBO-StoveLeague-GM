using KBOManager.Controllers;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-093] v0.3 Phase 2 - 씬에 배선되지 않고 방치되어 있던 RosterUIController(로스터 관리 화면)를
    /// QA가 메뉴 클릭 한 번으로 씬에 조립·배선할 수 있게 하는 에디터 자동화. 로비 패널에 "로스터 관리" 진입
    /// 버튼, 로스터 패널에 "닫기" 버튼을 만들어 바인딩하고, UIManager.screens에 Roster 화면을 등록한다.
    /// SetupScoutUI.cs/SetupCheerleaderUI.cs와 동일한 관례(이름으로 기존 오브젝트를 찾아 재사용, 없으면
    /// 생성)로 여러 번 실행해도 안전하다.
    ///
    /// [범위 제외] RosterUIController의 batterContainer/pitcherContainer/cardPrefab/gameActionController/
    /// 세트덱 게이지 필드(로스터 카드 렌더링 내부 UI)는 이번 작업의 포함 범위(패널/진입 버튼/닫기 버튼/
    /// screens 등록 4개 항목)에 없다 - SetupScoutUI.cs가 TASK-083(패널·버튼만)과 TASK-092(카드 템플릿
    /// 완성)로 두 단계에 나눠 작업했던 것과 동일한 전례를 따라, 카드 렌더링 배선은 후속 작업으로 남긴다.
    /// RosterManager의 백엔드 로직(자동 라인업 세팅 등)은 명령서 5항 가드레일에 따라 전혀 건드리지 않는다.
    /// </summary>
    public static class SetupRosterUI
    {
        private const string CanvasName = "Canvas";
        private const string RosterPanelName = "RosterPanel";
        private const string RosterButtonName = "RosterButton";
        private const string CloseButtonName = "CloseButton";

        [MenuItem("KBO Manager/Setup/Auto-Connect Roster UI")]
        public static void AutoConnectRosterUI()
        {
            var canvas = EnsureCanvas();
            var rosterController = FindOrCreateRosterPanel(canvas.transform);
            BindRosterController(rosterController);

            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupRosterUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "'로스터 관리' 진입 버튼 생성 및 UIManager 등록을 건너뜁니다.");
            }
            else
            {
                var rosterButton = FindOrCreateButton(dashboard.transform, RosterButtonName, "로스터 관리", new Vector2(20f, 140f));
                BindButtonField(dashboard, "rosterButton", rosterButton);
                EditorUtility.SetDirty(dashboard);

                var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
                if (uiManager == null)
                {
                    Debug.LogWarning("[SetupRosterUI] 씬에서 UIManager를 찾지 못해 screens 등록을 건너뜁니다.");
                }
                else
                {
                    RegisterRosterScreen(uiManager, rosterController.gameObject);
                    EditorUtility.SetDirty(uiManager);
                }
            }

            EditorUtility.SetDirty(rosterController);

            var scene = dashboard != null ? dashboard.gameObject.scene : rosterController.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupRosterUI] 로스터 UI 자동 배선 완료.");
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

        /// <summary>RosterPanel을 찾거나 만든다. 화면 전체를 채우는 단순 흰 배경만 붙인다(명령서 5항 -
        /// 화려한 UI 레이아웃 조작은 생략).</summary>
        private static RosterUIController FindOrCreateRosterPanel(Transform canvasTransform)
        {
            var existingChild = canvasTransform.Find(RosterPanelName);
            if (existingChild != null)
            {
                var existingController = existingChild.GetComponent<RosterUIController>();
                if (existingController != null) return existingController;
            }

            var panelObject = new GameObject(RosterPanelName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {RosterPanelName}");
            panelObject.transform.SetParent(canvasTransform, false);

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panelObject.GetComponent<Image>().color = Color.white;

            return panelObject.AddComponent<RosterUIController>();
        }

        /// <summary>RosterPanel에 "닫기" 버튼만 만들어 바인딩한다(포함 범위 항목).</summary>
        private static void BindRosterController(RosterUIController controller)
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
        /// UIManager.screens(List&lt;ScreenEntry&gt;, 필드는 Type/Root - SetupScoutUI.cs에서 이미 검증된
        /// 실제 필드명)에 Roster 항목을 등록한다. 이미 등록되어 있으면 Root 참조만 최신 오브젝트로
        /// 덮어써 중복 추가를 막는다(명령서 7항/AC-03 - 안전한 덮어쓰기/추가).
        /// </summary>
        private static void RegisterRosterScreen(UIManager uiManager, GameObject rosterRoot)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                var typeProperty = element.FindPropertyRelative("Type");
                if (typeProperty.intValue == (int)ScreenType.Roster)
                {
                    element.FindPropertyRelative("Root").objectReferenceValue = rosterRoot;
                    serializedManager.ApplyModifiedProperties();
                    return;
                }
            }

            int newIndex = screensProperty.arraySize;
            screensProperty.arraySize++;

            var newElement = screensProperty.GetArrayElementAtIndex(newIndex);
            newElement.FindPropertyRelative("Type").intValue = (int)ScreenType.Roster;
            newElement.FindPropertyRelative("Root").objectReferenceValue = rosterRoot;

            serializedManager.ApplyModifiedProperties();
        }
    }
}
