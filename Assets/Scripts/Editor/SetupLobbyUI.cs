using KBOManager.Controllers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-055] TASK-KBO-053/054에서 만든 TeamSynergyUIController와 그 3개 Text를 씬에 자동
    /// 생성/배치하고 LeagueDashboardUIController.synergyUIController에 바인딩하는 에디터 자동화.
    ///
    /// ItemDataSeeder/SceneInitializer와 동일한 관례로 여러 번 실행해도 안전하다 - 이미 존재하는
    /// TeamSynergyUIController(또는 그 자식 Text)는 이름/컴포넌트로 찾아 재사용하고, 바인딩만 다시
    /// 수행한다(중복 생성 없음).
    /// </summary>
    public static class SetupLobbyUI
    {
        private const string SynergyPanelName = "TeamSynergyPanel";
        private const string SetDeckTextName = "SetDeckText";
        private const string CheerleaderTextName = "CheerleaderText";
        private const string FanSentimentTextName = "FanSentimentText";
        private const string CanvasName = "Canvas";

        [MenuItem("KBO Manager/Setup/Auto-Create Synergy UI")]
        public static void AutoCreateSynergyUI()
        {
            // [TASK-KBO-056] Unity 6000.6에서 Object.FindFirstObjectByType 자체가 Obsolete 처리되어
            // Object.FindAnyObjectByType으로 교체했다(ItemDataSeeder.cs와 동일). 두 API 모두 비활성
            // 오브젝트는 찾지 못한다는 한계는 동일하므로 - 로비 패널이 다른 화면 전환으로 비활성화돼
            // 있으면 이 메뉴 실행 전에 로비 화면을 먼저 띄워 둬야 한다.
            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>();
            Transform parent = dashboard != null ? dashboard.transform : EnsureFallbackCanvas().transform;

            var synergyController = FindOrCreateSynergyController(parent);
            BindSynergyTexts(synergyController);

            if (dashboard != null)
            {
                BindDashboardReference(dashboard, synergyController);
            }
            else
            {
                Debug.LogWarning("[SetupLobbyUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "synergyUIController 자동 바인딩(5단계)은 건너뛰었습니다. TeamSynergyUIController와 " +
                    "Text 3개는 새 Canvas 아래 생성해 두었으니, 로비 패널을 먼저 구성한 뒤(예: KBO Manager/" +
                    "Initialize Current Scene) 이 메뉴를 다시 실행하거나 인스펙터에서 직접 연결하세요.");
            }

            EditorUtility.SetDirty(synergyController);
            if (dashboard != null) EditorUtility.SetDirty(dashboard);
            EditorSceneManager.MarkSceneDirty(synergyController.gameObject.scene);

            Debug.Log("[SetupLobbyUI] 팀 시너지 UI 자동 생성/바인딩 완료.");
        }

        /// <summary>LeagueDashboardUIController를 찾지 못했을 때만 쓰는 대체 경로. 기존 Canvas가 있으면
        /// 재사용하고, 전혀 없을 때만 새로 만든다(중복 Canvas로 인한 렌더링 충돌 방지).</summary>
        private static Canvas EnsureFallbackCanvas()
        {
            var existing = Object.FindAnyObjectByType<Canvas>();
            if (existing != null) return existing;

            var canvasObject = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Canvas");

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            return canvas;
        }

        private static TeamSynergyUIController FindOrCreateSynergyController(Transform parent)
        {
            var existing = Object.FindAnyObjectByType<TeamSynergyUIController>();
            if (existing != null) return existing;

            var panelObject = new GameObject(SynergyPanelName, typeof(RectTransform), typeof(VerticalLayoutGroup));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {SynergyPanelName}");
            panelObject.transform.SetParent(parent, false);

            // 좌상단 기준 고정 크기 배치(모범 답안 배치일 뿐, 실제 로비 레이아웃에 맞춰 유저가 직접
            // 위치를 조정하는 것을 전제로 한다 - 이번 작업은 "1-클릭 자동 생성"이 목표이지 최종 UX
            // 배치 확정이 목표가 아니다).
            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(420f, 120f);
            rect.anchoredPosition = new Vector2(20f, -20f);

            var layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 4f;

            return panelObject.AddComponent<TeamSynergyUIController>();
        }

        /// <summary>TeamSynergyUIController의 3개 필드(setDeckText/cheerleaderText/fanSentimentText)가
        /// 모두 private [SerializeField]라 SceneInitializer와 동일하게 SerializedObject로 채운다.</summary>
        private static void BindSynergyTexts(TeamSynergyUIController controller)
        {
            var parent = controller.transform;

            var setDeckText = FindOrCreateText(parent, SetDeckTextName);
            var cheerleaderText = FindOrCreateText(parent, CheerleaderTextName);
            var fanSentimentText = FindOrCreateText(parent, FanSentimentTextName);

            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("setDeckText").objectReferenceValue = setDeckText;
            serializedController.FindProperty("cheerleaderText").objectReferenceValue = cheerleaderText;
            serializedController.FindProperty("fanSentimentText").objectReferenceValue = fanSentimentText;
            serializedController.ApplyModifiedProperties();
        }

        /// <summary>이름으로 기존 자식 Text를 재사용하거나, 없으면 새로 만든다. 프로젝트 전체가
        /// TextMeshProUGUI 없이 UGUI Text만 쓰고 있어(SceneInitializer.cs 등 기존 관례) 동일하게
        /// UnityEngine.UI.Text로 생성한다 - TeamSynergyUIController의 필드 타입도 Text다.</summary>
        private static Text FindOrCreateText(Transform parent, string name)
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

            textObject.GetComponent<LayoutElement>().preferredHeight = 32f;

            var text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.fontSize = 16;
            text.font = KBOFonts.Default;

            return text;
        }

        /// <summary>LeagueDashboardUIController.synergyUIController도 private [SerializeField]라
        /// 동일하게 SerializedObject로 접근한다.</summary>
        private static void BindDashboardReference(LeagueDashboardUIController dashboard, TeamSynergyUIController synergyController)
        {
            var serializedDashboard = new SerializedObject(dashboard);
            serializedDashboard.FindProperty("synergyUIController").objectReferenceValue = synergyController;
            serializedDashboard.ApplyModifiedProperties();
        }
    }
}
