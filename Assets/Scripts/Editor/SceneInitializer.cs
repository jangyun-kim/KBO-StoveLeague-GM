using System;
using System.Collections.Generic;
using KBOManager.Controllers;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// "GameManagers" 매니저 오브젝트 + Canvas + 5개 화면 패널 + UIManager.screens 바인딩까지,
    /// 개발자가 매번 손으로 반복하던 씬 세팅을 메뉴 클릭 한 번으로 끝낸다.
    ///
    /// 여러 번 실행해도 안전하다(ItemDataSeeder와 동일한 관례) - 이미 존재하는 오브젝트/컴포넌트는
    /// 중복 생성하지 않고 찾아서 재사용하며, UIManager 바인딩만 항상 최신 상태로 다시 채운다.
    /// 검색 범위는 항상 "현재 활성 씬(Active Scene)"으로 한정한다 - 멀티 씬 편집 중에도 엉뚱한
    /// 씬의 오브젝트를 잘못 찾아오지 않기 위함이다.
    /// </summary>
    public static class SceneInitializer
    {
        private const string GameManagersName = "GameManagers";
        private const string CanvasName = "Canvas";
        private const string EventSystemName = "EventSystem";

        private readonly struct PanelDefinition
        {
            public readonly string Name;
            public readonly Type ControllerType;
            public readonly ScreenType Screen;

            public PanelDefinition(string name, Type controllerType, ScreenType screen)
            {
                Name = name;
                ControllerType = controllerType;
                Screen = screen;
            }
        }

        private static readonly PanelDefinition[] Panels =
        {
            new PanelDefinition("OnboardingPanel", typeof(OnboardingUIController), ScreenType.Onboarding),
            new PanelDefinition("LobbyPanel", typeof(LeagueDashboardUIController), ScreenType.Lobby),
            new PanelDefinition("InGamePanel", typeof(InGameUIController), ScreenType.InGame),
            new PanelDefinition("StatsPanel", typeof(LeagueStatsUIController), ScreenType.LeagueStats),
            new PanelDefinition("ShopPanel", typeof(ShopUIController), ScreenType.Shop),
        };

        [MenuItem("KBO Manager/Initialize Current Scene")]
        public static void InitializeCurrentScene()
        {
            var scene = EditorSceneManager.GetActiveScene();

            var uiManager = SetUpGameManagers(scene);
            var canvas = SetUpCanvas(scene);
            EnsureEventSystem(scene);
            var panelRoots = SetUpPanels(canvas.transform);
            BindUIManagerScreens(uiManager, panelRoots);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SceneInitializer] '{scene.name}' 씬 초기화 완료 - GameManagers 6종 컴포넌트, " +
                      $"Canvas + 패널 {Panels.Length}개, UIManager.screens 바인딩 {Panels.Length}건.");
        }

        /// <summary>GameManagers 오브젝트를 찾거나 만들고, 필요한 매니저 6종을 빠짐없이 부착한다.</summary>
        private static UIManager SetUpGameManagers(Scene scene)
        {
            var root = FindRoot(scene, GameManagersName);
            if (root == null)
            {
                root = new GameObject(GameManagersName);
                Undo.RegisterCreatedObjectUndo(root, "Create GameManagers");
            }

            GetOrAddComponent<GameManager>(root);
            GetOrAddComponent<LeagueManager>(root);
            GetOrAddComponent<LeagueCalendar>(root);
            var uiManager = GetOrAddComponent<UIManager>(root);
            GetOrAddComponent<MatchRewardManager>(root);
            GetOrAddComponent<CardPoolManager>(root);

            return uiManager;
        }

        /// <summary>씬에 Canvas가 이미 있으면 그대로 재사용하고(설정을 건드리지 않는다), 없을 때만 새로 만들어
        /// Scale With Screen Size / 1080x1920 기준 해상도로 세팅한다.</summary>
        private static Canvas SetUpCanvas(Scene scene)
        {
            var existing = FindInScene<Canvas>(scene);
            if (existing != null) return existing;

            var canvasObject = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Canvas");

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);

            return canvas;
        }

        /// <summary>UI 클릭/터치 입력을 받으려면 EventSystem이 반드시 필요하다. 프로젝트가 New Input
        /// System 단독 모드(Project Settings - Active Input Handling)이므로 레거시 StandaloneInputModule
        /// 대신 InputSystemUIInputModule을 부착한다.</summary>
        private static void EnsureEventSystem(Scene scene)
        {
            if (FindInScene<EventSystem>(scene) != null) return;

            var eventSystemObject = new GameObject(EventSystemName, typeof(EventSystem), typeof(InputSystemUIInputModule));
            Undo.RegisterCreatedObjectUndo(eventSystemObject, "Create EventSystem");
        }

        /// <summary>5개 패널을 Canvas 하위에서 찾거나 만들고(화면 전체를 채우는 RectTransform), 각각에
        /// 알맞은 컨트롤러를 부착한다. 편집 중 화면이 서로 겹쳐 보이지 않도록 Lobby만 활성 상태로 두는데,
        /// 실제 시작 화면은 런타임에 UIManager.Start() -&gt; ShowScreen()이 다시 확정하므로 무관하다.</summary>
        private static Dictionary<ScreenType, GameObject> SetUpPanels(Transform canvasTransform)
        {
            var result = new Dictionary<ScreenType, GameObject>(Panels.Length);

            foreach (var panel in Panels)
            {
                var panelObject = FindChild(canvasTransform, panel.Name);
                if (panelObject == null)
                {
                    panelObject = new GameObject(panel.Name, typeof(RectTransform));
                    Undo.RegisterCreatedObjectUndo(panelObject, $"Create {panel.Name}");
                    panelObject.transform.SetParent(canvasTransform, false);

                    var rect = (RectTransform)panelObject.transform;
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = Vector2.zero;
                    rect.offsetMax = Vector2.zero;
                }

                GetOrAddComponent(panelObject, panel.ControllerType);
                result[panel.Screen] = panelObject;
            }

            foreach (var pair in result)
            {
                pair.Value.SetActive(pair.Key == ScreenType.Lobby);
            }

            return result;
        }

        /// <summary>
        /// UIManager.screens는 private [SerializeField] List&lt;ScreenEntry&gt;라 클래스 밖에서 직접 대입할
        /// public 세터가 없다. SerializedObject/SerializedProperty로 그 필드에 접근하면 private 여부와
        /// 무관하게 Unity의 직렬화 시스템을 통해 값을 써 넣을 수 있고, Undo 기록과 씬 Dirty 마킹까지
        /// 자동으로 처리된다 - 이 방식이 리플렉션으로 강제 대입하는 것보다 안전한 이유다.
        /// </summary>
        private static void BindUIManagerScreens(UIManager uiManager, Dictionary<ScreenType, GameObject> panelRoots)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            screensProperty.arraySize = Panels.Length;

            for (int i = 0; i < Panels.Length; i++)
            {
                var screenType = Panels[i].Screen;
                var element = screensProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Type").enumValueIndex = (int)screenType;
                element.FindPropertyRelative("Root").objectReferenceValue = panelRoots[screenType];
            }

            serializedManager.ApplyModifiedProperties();
        }

        private static GameObject FindRoot(Scene scene, string objectName)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == objectName) return root;
            }
            return null;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<T>(true);
                if (found != null) return found;
            }
            return null;
        }

        private static GameObject FindChild(Transform parent, string childName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == childName) return child.gameObject;
            }
            return null;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            var component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static Component GetOrAddComponent(GameObject target, Type componentType)
        {
            var component = target.GetComponent(componentType);
            return component != null ? component : target.AddComponent(componentType);
        }
    }
}
