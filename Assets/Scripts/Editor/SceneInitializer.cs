using System;
using System.Collections.Generic;
using KBOManager.Controllers;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Tools;
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

        private const string TeamButtonGridName = "TeamButtonGrid";

        // OnboardingUIController.Awake()가 Team.None을 건너뛰므로, Team.None을 제외한 KBO 10개 구단을
        // 요청받은 노출 순서 그대로 나열한다(Team enum 선언 순서와는 다르다).
        private static readonly Team[] OnboardingTeamOrder =
        {
            Team.LG, Team.KT, Team.SSG, Team.NC, Team.Doosan,
            Team.KIA, Team.Lotte, Team.Samsung, Team.Hanwha, Team.Kiwoom,
        };

        [MenuItem("KBO Manager/Initialize Current Scene")]
        public static void InitializeCurrentScene()
        {
            var scene = EditorSceneManager.GetActiveScene();

            var (uiManager, onboardingManager, leagueManager) = SetUpGameManagers(scene);
            var canvas = SetUpCanvas(scene);
            EnsureEventSystem(scene);
            var panelRoots = SetUpPanels(canvas.transform);
            BindUIManagerScreens(uiManager, panelRoots);
            SetUpOnboardingTeamButtons(panelRoots[ScreenType.Onboarding], onboardingManager);
            SetUpLobbyStandings(panelRoots[ScreenType.Lobby], leagueManager);
            SetUpDebugPanel(canvas.transform, panelRoots);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();

            Debug.Log($"[SceneInitializer] '{scene.name}' 씬 초기화 완료 - GameManagers 8종 컴포넌트, " +
                      $"Canvas + 패널 {Panels.Length}개, UIManager.screens 바인딩 {Panels.Length}건, " +
                      $"온보딩 구단 버튼 {OnboardingTeamOrder.Length}개 바인딩, 디버그 패널 연동 완료.");
        }

        /// <summary>GameManagers 오브젝트를 찾거나 만들고, 필요한 매니저 8종을 빠짐없이 부착한다.
        /// OnboardingManager가 없으면 OnboardingUIController가 구단 버튼 클릭을 받아도 조용히
        /// 무시하므로(onboardingManager == null 가드) 반드시 함께 만들어 둔다.</summary>
        private static (UIManager UI, OnboardingManager Onboarding, LeagueManager League) SetUpGameManagers(Scene scene)
        {
            var root = FindRoot(scene, GameManagersName);
            if (root == null)
            {
                root = new GameObject(GameManagersName);
                Undo.RegisterCreatedObjectUndo(root, "Create GameManagers");
            }

            GetOrAddComponent<GameManager>(root);
            var leagueManager = GetOrAddComponent<LeagueManager>(root);
            GetOrAddComponent<LeagueCalendar>(root);
            var uiManager = GetOrAddComponent<UIManager>(root);
            GetOrAddComponent<MatchRewardManager>(root);
            GetOrAddComponent<CardPoolManager>(root);
            var onboardingManager = GetOrAddComponent<OnboardingManager>(root);
            // LeagueManager.SimulateFixture()가 헤드리스 스킵 중에도 SeasonStatManager.Instance를 직접
            // 찾아 호출한다(PlayBallController 이벤트 구독 없이) - 이 컴포넌트가 씬에 없으면 스킵해도
            // 시즌 기록이 조용히 누락되므로 반드시 함께 만들어 둔다.
            GetOrAddComponent<SeasonStatManager>(root);

            return (uiManager, onboardingManager, leagueManager);
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
                element.FindPropertyRelative("Type").intValue = (int)screenType;
                element.FindPropertyRelative("Root").objectReferenceValue = panelRoots[screenType];
            }

            serializedManager.ApplyModifiedProperties();
        }

        /// <summary>OnboardingPanel 하위에 Grid Layout Group 컨테이너와 KBO 10개 구단 버튼을 만들고,
        /// OnboardingUIController.teamButtons에 순서대로 바인딩한다. 버튼에 onClick 리스너를 여기서
        /// 직접 붙이지는 않는다 - OnboardingUIController.Awake()가 teamButtons를 순회하며 이미
        /// AddListener(() =&gt; OnClickTeam(team))로 매 실행마다 코드로 붙이므로, 에디터에서 영구
        /// 리스너(PersistentCall)를 추가하면 중복 호출이 생긴다.</summary>
        private static void SetUpOnboardingTeamButtons(GameObject onboardingPanel, OnboardingManager onboardingManager)
        {
            var controller = onboardingPanel.GetComponent<OnboardingUIController>();

            var grid = FindChild(onboardingPanel.transform, TeamButtonGridName);
            if (grid == null)
            {
                grid = new GameObject(TeamButtonGridName, typeof(RectTransform), typeof(GridLayoutGroup));
                Undo.RegisterCreatedObjectUndo(grid, "Create TeamButtonGrid");
                grid.transform.SetParent(onboardingPanel.transform, false);

                var rect = (RectTransform)grid.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var layout = grid.GetComponent<GridLayoutGroup>();
                layout.cellSize = new Vector2(420f, 160f);
                layout.spacing = new Vector2(24f, 24f);
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = 2;
            }

            var entries = new List<TeamSelectButtonEntry>(OnboardingTeamOrder.Length);
            foreach (var team in OnboardingTeamOrder)
            {
                var (button, label) = FindOrCreateTeamButton(grid.transform, team);
                entries.Add(new TeamSelectButtonEntry { Team = team, Button = button, TeamNameText = label });
            }

            ConfigureOnboardingController(controller, onboardingManager, entries);
        }

        /// <summary>이름으로 기존 버튼을 재사용하거나(재실행 시 중복 생성 방지), 없으면 Image+Button 루트와
        /// 그 위에 전체를 채우는 Text 라벨 하나로 원시 GameObject를 조립해 새로 만든다.</summary>
        private static (Button Button, Text Label) FindOrCreateTeamButton(Transform parent, Team team)
        {
            string buttonName = $"{team}Button";
            var existing = FindChild(parent, buttonName);
            if (existing != null)
            {
                return (existing.GetComponent<Button>(), existing.GetComponentInChildren<Text>(true));
            }

            var buttonObject = new GameObject(buttonName, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {buttonName}");
            buttonObject.transform.SetParent(parent, false);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.85f, 0.85f, 0.85f);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(labelObject, $"Create {buttonName} Label");
            labelObject.transform.SetParent(buttonObject.transform, false);

            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var label = labelObject.GetComponent<Text>();
            label.text = team.ToString();
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.black;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return (button, label);
        }

        /// <summary>
        /// OnboardingUIController.onboardingManager와 teamButtons 모두 private [SerializeField]라
        /// UIManager.screens 바인딩과 동일하게 SerializedObject/SerializedProperty로 접근한다.
        /// teamButtons는 TeamSelectButtonEntry([Serializable] 순수 C# 클래스)의 List이므로, ScreenEntry와
        /// 마찬가지로 SerializedProperty 레벨에서는 고정 배열처럼 arraySize/GetArrayElementAtIndex +
        /// FindPropertyRelative(필드명)으로 각 원소의 Team/Button/LogoImage/TeamNameText를 채운다.
        /// </summary>
        private static void ConfigureOnboardingController(OnboardingUIController controller,
            OnboardingManager onboardingManager, List<TeamSelectButtonEntry> entries)
        {
            var serializedController = new SerializedObject(controller);

            serializedController.FindProperty("onboardingManager").objectReferenceValue = onboardingManager;

            var teamButtonsProperty = serializedController.FindProperty("teamButtons");
            teamButtonsProperty.arraySize = entries.Count;

            for (int i = 0; i < entries.Count; i++)
            {
                var element = teamButtonsProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("Team").intValue = (int)entries[i].Team;
                element.FindPropertyRelative("Button").objectReferenceValue = entries[i].Button;
                element.FindPropertyRelative("LogoImage").objectReferenceValue = entries[i].LogoImage;
                element.FindPropertyRelative("TeamNameText").objectReferenceValue = entries[i].TeamNameText;
            }

            serializedController.ApplyModifiedProperties();
        }

        private const string StandingsListName = "StandingsList";
        private const int StandingsRowCount = 10; // KBO 10개 구단 고정

        /// <summary>
        /// LeagueDashboardUIController.RefreshDashboard()는 leagueManager와 standingsRowTexts가 둘 다
        /// 채워져 있어야 실제로 뭔가를 그린다(둘 중 하나라도 null이면 조용히 아무 일도 안 하고
        /// 리턴한다) - 스킵 직후 "순위표 갱신"이 화면에 보이려면 이 바인딩이 먼저 되어 있어야 한다.
        /// </summary>
        private static void SetUpLobbyStandings(GameObject lobbyPanel, LeagueManager leagueManager)
        {
            var controller = lobbyPanel.GetComponent<LeagueDashboardUIController>();

            var list = FindChild(lobbyPanel.transform, StandingsListName);
            if (list == null)
            {
                list = new GameObject(StandingsListName, typeof(RectTransform), typeof(VerticalLayoutGroup));
                Undo.RegisterCreatedObjectUndo(list, "Create StandingsList");
                list.transform.SetParent(lobbyPanel.transform, false);

                var rect = (RectTransform)list.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var layout = list.GetComponent<VerticalLayoutGroup>();
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
                layout.childControlHeight = false;
                layout.childForceExpandHeight = false;
                layout.spacing = 4f;
                layout.padding = new RectOffset(16, 16, 16, 16);
            }

            var rowTexts = new Text[StandingsRowCount];
            for (int i = 0; i < StandingsRowCount; i++)
            {
                rowTexts[i] = FindOrCreateDebugText(list.transform, $"StandingsRow{i + 1}", 36f);
            }

            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("leagueManager").objectReferenceValue = leagueManager;

            var rowsProperty = serializedController.FindProperty("standingsRowTexts");
            rowsProperty.arraySize = rowTexts.Length;
            for (int i = 0; i < rowTexts.Length; i++)
            {
                rowsProperty.GetArrayElementAtIndex(i).objectReferenceValue = rowTexts[i];
            }

            serializedController.ApplyModifiedProperties();
        }

        private const string DebugPanelName = "DebugPanel";
        private const string CornerTapButtonName = "CornerTapButton";
        private const string PanelContentName = "PanelContent";

        /// <summary>
        /// Canvas 우측 상단 구석에 작은 QA 디버그 패널을 만든다. F12 토글(DebugPanelUI.Update)과
        /// 구석 탭 5연속(HandleCornerTap) 두 경로 모두로 열 수 있고, 닫기 버튼으로만 닫힌다.
        /// panelRoots[ScreenType.Lobby]에서 LeagueDashboardUIController를 가져와 스킵 완료 후
        /// 순위표를 새로고침할 대상으로 바로 바인딩한다.
        /// </summary>
        private static void SetUpDebugPanel(Transform canvasTransform, Dictionary<ScreenType, GameObject> panelRoots)
        {
            var lobbyController = panelRoots[ScreenType.Lobby].GetComponent<LeagueDashboardUIController>();

            var debugPanelRoot = FindChild(canvasTransform, DebugPanelName);
            if (debugPanelRoot == null)
            {
                debugPanelRoot = new GameObject(DebugPanelName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(debugPanelRoot, "Create DebugPanel");
                debugPanelRoot.transform.SetParent(canvasTransform, false);

                // 화면 우측 상단에 작게: pivot/anchor를 우상단(1,1)에 고정하고 그 지점 기준으로만 크기를 잡는다.
                var rect = (RectTransform)debugPanelRoot.transform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(320f, 460f);
                rect.anchoredPosition = new Vector2(-20f, -20f);
            }

            var debugPanelUI = GetOrAddComponent<DebugPanelUI>(debugPanelRoot);

            var cornerTapButton = FindOrCreateCornerTapButton(debugPanelRoot.transform);
            var panelContent = FindOrCreatePanelContentRoot(debugPanelRoot.transform);
            var closeButton = FindOrCreateDebugButton(panelContent.transform, "CloseButton", "닫기 (X)", 40f);
            var resultText = FindOrCreateDebugText(panelContent.transform, "ResultText", 100f);
            var skipButton = FindOrCreateDebugButton(panelContent.transform, "SkipRegularSeasonButton", "정규 시즌 즉시 스킵", 60f);
            var grantCurrencyButton = FindOrCreateDebugButton(panelContent.transform, "GrantPremiumCurrencyButton", "프리미엄 재화 +10,000 획득", 60f);

            ConfigureDebugPanel(debugPanelUI, cornerTapButton, panelContent, closeButton, resultText,
                skipButton, grantCurrencyButton, lobbyController);
        }

        /// <summary>DebugPanel 전체 영역 중 우측 상단 64x64만 차지하는, 거의 투명한 "숨겨진" 탭 영역.
        /// HandleCornerTap()이 tapWindowSeconds 안에 requiredTapCount번 눌리는지 센다.</summary>
        private static Button FindOrCreateCornerTapButton(Transform parent)
        {
            var existing = FindChild(parent, CornerTapButtonName);
            if (existing != null) return existing.GetComponent<Button>();

            var buttonObject = new GameObject(CornerTapButtonName, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, "Create CornerTapButton");
            buttonObject.transform.SetParent(parent, false);

            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(64f, 64f);
            rect.anchoredPosition = Vector2.zero;

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.02f); // 거의 안 보이는 은닉 탭 영역

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            return button;
        }

        /// <summary>실제 버튼들이 모이는 패널 본체. DebugPanelUI.panelRoot로 바인딩되어 평소엔 꺼져 있다가
        /// F12/구석 탭으로 열린다.</summary>
        private static GameObject FindOrCreatePanelContentRoot(Transform parent)
        {
            var existing = FindChild(parent, PanelContentName);
            if (existing != null) return existing;

            var panelContent = new GameObject(PanelContentName, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            Undo.RegisterCreatedObjectUndo(panelContent, "Create PanelContent");
            panelContent.transform.SetParent(parent, false);

            var rect = (RectTransform)panelContent.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var background = panelContent.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.85f);

            var layout = panelContent.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 16, 16);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            panelContent.SetActive(false); // DebugPanelUI.Awake()도 다시 꺼 주지만, 에디터 미리보기도 깔끔하게 유지한다.

            return panelContent;
        }

        /// <summary>이름으로 기존 요소를 재사용하거나, 없으면 Image+Button 루트 + 전체를 채우는 Text
        /// 라벨로 새로 조립한다. VerticalLayoutGroup 하위에서 높이를 고정하기 위해 LayoutElement를 함께 붙인다.</summary>
        private static Button FindOrCreateDebugButton(Transform parent, string name, string label, float preferredHeight)
        {
            var existing = FindChild(parent, name);
            if (existing != null) return existing.GetComponent<Button>();

            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
            buttonObject.transform.SetParent(parent, false);

            buttonObject.GetComponent<LayoutElement>().preferredHeight = preferredHeight;

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

            return button;
        }

        /// <summary>이름으로 기존 Text를 재사용하거나, 없으면 LayoutElement가 붙은 새 Text를 만든다.</summary>
        private static Text FindOrCreateDebugText(Transform parent, string name, float preferredHeight)
        {
            var existing = FindChild(parent, name);
            if (existing != null) return existing.GetComponent<Text>();

            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
            textObject.transform.SetParent(parent, false);

            textObject.GetComponent<LayoutElement>().preferredHeight = preferredHeight;

            var text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return text;
        }

        /// <summary>
        /// DebugPanelUI의 모든 필드가 private [SerializeField]라 지금까지와 동일하게
        /// SerializedObject/SerializedProperty로 채운다. toggleHotkey(KeyCode)는 ScreenType/Team과 달리
        /// 선언 순서와 실제 값이 다른 enum이라(KeyCode.F12는 293) enumValueIndex(선언 순서 인덱스)를
        /// 쓰면 엉뚱한 키가 바인딩된다 - intValue로 실제 정수값(293)을 직접 써야 한다.
        /// </summary>
        private static void ConfigureDebugPanel(DebugPanelUI debugPanelUI, Button cornerTapButton, GameObject panelContent,
            Button closeButton, Text resultText, Button skipButton, Button grantCurrencyButton, LeagueDashboardUIController leagueDashboard)
        {
            var serializedPanel = new SerializedObject(debugPanelUI);

            serializedPanel.FindProperty("toggleHotkey").intValue = (int)KeyCode.F12;
            serializedPanel.FindProperty("hiddenCornerTapButton").objectReferenceValue = cornerTapButton;
            serializedPanel.FindProperty("panelRoot").objectReferenceValue = panelContent;
            serializedPanel.FindProperty("closeButton").objectReferenceValue = closeButton;
            serializedPanel.FindProperty("resultText").objectReferenceValue = resultText;
            serializedPanel.FindProperty("grantPremiumCurrencyButton").objectReferenceValue = grantCurrencyButton;
            serializedPanel.FindProperty("skipRegularSeasonButton").objectReferenceValue = skipButton;
            serializedPanel.FindProperty("leagueDashboard").objectReferenceValue = leagueDashboard;

            serializedPanel.ApplyModifiedProperties();
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
