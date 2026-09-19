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
    /// [TASK-KBO-093] v0.3 Phase 2 - 씬에 배선되지 않고 방치되어 있던 RosterUIController(로스터 관리 화면)를
    /// QA가 메뉴 클릭 한 번으로 씬에 조립·배선할 수 있게 하는 에디터 자동화. 로비 패널에 "로스터 관리" 진입
    /// 버튼, 로스터 패널에 "닫기" 버튼을 만들어 바인딩하고, UIManager.screens에 Roster 화면을 등록한다.
    /// SetupScoutUI.cs/SetupCheerleaderUI.cs와 동일한 관례(이름으로 기존 오브젝트를 찾아 재사용, 없으면
    /// 생성)로 여러 번 실행해도 안전하다.
    ///
    /// [TASK-KBO-094] TASK-093 당시 범위 제외됐던 batterContainer/pitcherContainer/cardPrefab/
    /// setDeckStatusText/setDeckGaugeFillImage/setDeckActiveGlowRoot/gameActionController 바인딩을
    /// 이번에 완성한다. SetupScoutUI.cs(TASK-092)가 `_Templates/PlayerCardTemplate`을 만든 전례를 그대로
    /// 재활용 - 새 프리팹을 만들지 않고 canvas 하위 `_Templates/PlayerCardTemplate`을 찾아 공유 배선한다.
    /// gameActionController는 RosterUIController 자체 주석("비워두면 자동 갱신 없이 수동 RefreshRoster()만
    /// 동작")에 따라 선택적 필드이므로, dashboard/uiManager와 동일한 관례(찾으면 바인딩, 없으면 경고 후
    /// 스킵)를 따른다 - 더미 GameActionController를 생성하지 않는다(RosterManager 배선이 없는 빈 컴포넌트는
    /// 오히려 오해를 유발할 수 있음, 명령서 5항 가드레일 - RosterManager 백엔드는 건드리지 않음).
    ///
    /// [TASK-KBO-096] TASK-094가 보류했던 gameActionController 자동 생성을 PM 지시로 완성한다 -
    /// `FindAnyObjectByType`로 여전히 씬을 먼저 탐색하되(중복 생성 방지), 못 찾으면 canvas 하위
    /// `UIControllers`(없으면 새로 생성)에 `Undo.AddComponent&lt;GameActionController&gt;()`로 부착한다.
    /// (당시 `GameActionController.rosterManager`는 `RosterManager`가 씬에 없어 비워뒀었다 - 아래
    /// TASK-097에서 해소됨.)
    ///
    /// [TASK-KBO-097] TASK-096이 비워뒀던 `GameActionController.rosterManager`를 PM 지시로 마저 채운다 -
    /// `GameManagers`(씬에 이미 존재하는 매니저 홀더)를 찾아 `RosterManager`가 없으면
    /// `Undo.AddComponent&lt;RosterManager&gt;()`로 부착한 뒤 바인딩한다. `RosterManager.cs` 원문을
    /// 재확인한 결과 `[SerializeField]` 필드가 전혀 없는 무상태 매니저라, 추가로 바인딩할 타 매니저
    /// 종속성 자체가 없다. 이로써 `GameActionController.ExecuteAutoRoster()`의 `rosterManager == null`
    /// 가드가 더 이상 걸리지 않아 '자동 라인업' 기능이 실제로 동작한다.
    /// </summary>
    public static class SetupRosterUI
    {
        private const string CanvasName = "Canvas";
        private const string RosterPanelName = "RosterPanel";
        private const string RosterButtonName = "RosterButton";
        private const string CloseButtonName = "CloseButton";
        private const string BatterContainerName = "BatterContainer";
        private const string PitcherContainerName = "PitcherContainer";
        private const string SetDeckStatusTextName = "SetDeckStatusText";
        private const string SetDeckGaugeFillImageName = "SetDeckGaugeFill";
        private const string SetDeckActiveGlowRootName = "SetDeckActiveGlow";
        private const string TemplatesHolderName = "_Templates";
        private const string CardTemplateName = "PlayerCardTemplate";
        private const string UIControllersHolderName = "UIControllers";
        private const string GameManagersHolderName = "GameManagers";

        [MenuItem("KBO Manager/Setup/Auto-Connect Roster UI")]
        public static void AutoConnectRosterUI()
        {
            var canvas = EnsureCanvas();
            var rosterController = FindOrCreateRosterPanel(canvas.transform);
            BindRosterController(rosterController);

            var cardTemplate = FindCardTemplate(canvas.transform);
            if (cardTemplate == null)
            {
                Debug.LogError("[SetupRosterUI] canvas 하위 '_Templates/PlayerCardTemplate'을 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Setup/Auto-Connect Scout UI'를 실행해 카드 템플릿을 생성한 뒤 다시 " +
                    "시도하십시오.");
                return;
            }

            BindRosterContainers(rosterController, cardTemplate);
            BindSetDeckVisualization(rosterController);
            var gameActionController = BindGameActionController(rosterController, canvas.transform);
            BindRosterManager(gameActionController);

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

        /// <summary>
        /// [TASK-KBO-094] SetupScoutUI.FindOrCreateCardTemplate()이 만들어 둔 canvas 하위
        /// `_Templates/PlayerCardTemplate`을 그대로 찾아 재사용한다(명령서 6항 - 새 프리팹을 만들지 않고
        /// 기존 템플릿을 공유 배선). 존재하지 않으면 null을 반환하고, 호출부(AutoConnectRosterUI)가
        /// LogError 후 조기 반환한다(명령서 7항 - 널 레퍼런스로 인한 에디터 멈춤 방지).
        /// </summary>
        private static PlayerCardUI FindCardTemplate(Transform canvasTransform)
        {
            var holderTransform = canvasTransform.Find(TemplatesHolderName);
            if (holderTransform == null) return null;

            var cardTransform = holderTransform.Find(CardTemplateName);
            if (cardTransform == null) return null;

            return cardTransform.GetComponent<PlayerCardUI>();
        }

        /// <summary>
        /// RosterPanel 하위에 타자(BatterContainer)/투수(PitcherContainer) 카드 목록용 GridLayoutGroup
        /// 컨테이너를 조립하고, RosterUIController.batterContainer/pitcherContainer/cardPrefab 3개 필드를
        /// 바인딩한다(포함 범위 1·2번째 항목). SetupScoutUI.FindOrCreateCardContainer()와 동일한 셀 크기
        /// (140x200)를 사용해 같은 cardPrefab을 그대로 담을 수 있게 한다.
        /// </summary>
        private static void BindRosterContainers(RosterUIController controller, PlayerCardUI cardTemplate)
        {
            var batterContainer = FindOrCreateCardGridContainer(controller.transform, BatterContainerName,
                new Vector2(0f, 0f), new Vector2(0.5f, 0.85f));
            var pitcherContainer = FindOrCreateCardGridContainer(controller.transform, PitcherContainerName,
                new Vector2(0.5f, 0f), new Vector2(1f, 0.85f));

            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("batterContainer").objectReferenceValue = batterContainer;
            serializedController.FindProperty("pitcherContainer").objectReferenceValue = pitcherContainer;
            serializedController.FindProperty("cardPrefab").objectReferenceValue = cardTemplate;
            serializedController.ApplyModifiedProperties();
        }

        private static Transform FindOrCreateCardGridContainer(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existingContainer = parent.Find(name);
            if (existingContainer != null) return existingContainer;

            var containerObject = new GameObject(name, typeof(RectTransform), typeof(GridLayoutGroup));
            Undo.RegisterCreatedObjectUndo(containerObject, $"Create {name}");
            containerObject.transform.SetParent(parent, false);

            var rect = (RectTransform)containerObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var grid = containerObject.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(140f, 200f);
            grid.spacing = new Vector2(10f, 10f);
            grid.childAlignment = TextAnchor.UpperCenter;

            return containerObject.transform;
        }

        /// <summary>
        /// RosterPanel 상단에 세트덱 게이지/텍스트/글로우 연출용 오브젝트를 조립하고
        /// RosterUIController.setDeckStatusText/setDeckGaugeFillImage/setDeckActiveGlowRoot 3개 필드를
        /// 바인딩한다. setDeckGaugeFillImage는 RefreshSetDeckStatus()가 fillAmount를 직접 갱신하므로
        /// Image.Type.Filled(Horizontal)로 미리 설정해 둔다.
        /// </summary>
        private static void BindSetDeckVisualization(RosterUIController controller)
        {
            var statusText = FindOrCreateSetDeckStatusText(controller.transform);
            var gaugeFillImage = FindOrCreateSetDeckGauge(controller.transform);
            var glowRoot = FindOrCreateSetDeckGlow(controller.transform);

            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("setDeckStatusText").objectReferenceValue = statusText;
            serializedController.FindProperty("setDeckGaugeFillImage").objectReferenceValue = gaugeFillImage;
            serializedController.FindProperty("setDeckActiveGlowRoot").objectReferenceValue = glowRoot;
            serializedController.ApplyModifiedProperties();
        }

        private static Text FindOrCreateSetDeckStatusText(Transform parent)
        {
            var existingChild = parent.Find(SetDeckStatusTextName);
            if (existingChild != null && existingChild.TryGetComponent<Text>(out var existingText)) return existingText;

            var textObject = new GameObject(SetDeckStatusTextName, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {SetDeckStatusTextName}");
            textObject.transform.SetParent(parent, false);

            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = new Vector2(0f, 0.9f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 20;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }

        private static Image FindOrCreateSetDeckGauge(Transform parent)
        {
            var existingChild = parent.Find(SetDeckGaugeFillImageName);
            if (existingChild != null && existingChild.TryGetComponent<Image>(out var existingImage)) return existingImage;

            var gaugeObject = new GameObject(SetDeckGaugeFillImageName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(gaugeObject, $"Create {SetDeckGaugeFillImageName}");
            gaugeObject.transform.SetParent(parent, false);

            var rect = (RectTransform)gaugeObject.transform;
            rect.anchorMin = new Vector2(0f, 0.85f);
            rect.anchorMax = new Vector2(1f, 0.9f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = gaugeObject.GetComponent<Image>();
            image.color = Color.white;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.raycastTarget = false;

            return image;
        }

        /// <summary>세트덱 보너스 활성화 시에만 켜지는 장식용 글로우 오브젝트. RosterUIController가
        /// RefreshSetDeckStatus()에서 SetActive()로 토글하므로 초기 상태는 비활성으로 둔다.</summary>
        private static GameObject FindOrCreateSetDeckGlow(Transform parent)
        {
            var existingChild = parent.Find(SetDeckActiveGlowRootName);
            if (existingChild != null) return existingChild.gameObject;

            var glowObject = new GameObject(SetDeckActiveGlowRootName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(glowObject, $"Create {SetDeckActiveGlowRootName}");
            glowObject.transform.SetParent(parent, false);

            var rect = (RectTransform)glowObject.transform;
            rect.anchorMin = new Vector2(0f, 0.85f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = glowObject.GetComponent<Image>();
            image.color = new Color(1f, 0.84f, 0f, 0.25f);
            image.raycastTarget = false;

            glowObject.SetActive(false);
            return glowObject;
        }

        /// <summary>
        /// [TASK-KBO-096] 씬에서 GameActionController를 먼저 탐색해 중복 생성을 막고(명령서 6항), 없으면
        /// canvas 하위 `UIControllers`(없으면 새로 생성)에 `Undo.AddComponent()`로 부착한 뒤 바인딩한다.
        /// 반환값은 [TASK-KBO-097]의 `BindRosterManager()`가 이어서 `rosterManager` 필드를 채우는 데 쓰인다.
        /// </summary>
        private static GameActionController BindGameActionController(RosterUIController controller, Transform canvasTransform)
        {
            var gameActionController = Object.FindAnyObjectByType<GameActionController>(FindObjectsInactive.Include);
            if (gameActionController == null)
            {
                var holderObject = FindOrCreateUIControllersHolder(canvasTransform);
                gameActionController = Undo.AddComponent<GameActionController>(holderObject);
                EditorUtility.SetDirty(gameActionController);

                Debug.Log($"[SetupRosterUI] 씬에서 GameActionController를 찾지 못해 '{holderObject.name}'에 " +
                    "새로 생성했습니다.");
            }

            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("gameActionController").objectReferenceValue = gameActionController;
            serializedController.ApplyModifiedProperties();

            return gameActionController;
        }

        /// <summary>
        /// [TASK-KBO-097] `GameManagers`(씬에 이미 존재하는 매니저 홀더 - `GameManager`/`UIManager`/
        /// `LeagueManager`/`LeagueCalendar`/`MatchRewardManager`/`CardPoolManager`/`PlayerDatabase`가 이미
        /// 부착되어 있음)를 탐색해 `RosterManager`가 없으면 `Undo.AddComponent`로 부착하고,
        /// `GameActionController.rosterManager` 필드에 바인딩한다. `RosterManager.cs` 원문을 전수
        /// 검토한 결과 `[SerializeField]` 필드가 단 하나도 없는 완전 무상태(stateless) 매니저임을 확인했다
        /// - `AutoSetRoster(List&lt;Player&gt; inventory, float salaryCap)`가 인벤토리/캡을 전부 메서드
        /// 인자로만 받으므로(정적 상수 `GameManager.RequiredRosterSize`/`RequiredPitcherCount` 참조만 있고
        /// 인스펙터 종속성 없음) AC-03(타 매니저 필드 바인딩)에 해당하는 작업 자체가 존재하지 않는다.
        /// `gameActionController`가 null(직전 단계에서 바인딩 실패)이면 조기 반환한다.
        /// </summary>
        private static void BindRosterManager(GameActionController gameActionController)
        {
            if (gameActionController == null) return;

            var gameManagersHolder = GameObject.Find(GameManagersHolderName);
            if (gameManagersHolder == null)
            {
                Debug.LogError($"[SetupRosterUI] 씬에서 '{GameManagersHolderName}' 오브젝트를 찾지 못해 " +
                    "RosterManager 배선을 건너뜁니다. 씬에 매니저 홀더가 없다면 먼저 확인하십시오.");
                return;
            }

            var rosterManager = gameManagersHolder.GetComponent<RosterManager>();
            if (rosterManager == null)
            {
                rosterManager = Undo.AddComponent<RosterManager>(gameManagersHolder);
                Debug.Log($"[SetupRosterUI] 씬에서 RosterManager를 찾지 못해 '{gameManagersHolder.name}'에 " +
                    "새로 생성했습니다.");
            }
            EditorUtility.SetDirty(rosterManager);

            var serializedController = new SerializedObject(gameActionController);
            serializedController.FindProperty("rosterManager").objectReferenceValue = rosterManager;
            serializedController.ApplyModifiedProperties();
            EditorUtility.SetDirty(gameActionController);
        }

        /// <summary>canvas 하위 `UIControllers` 오브젝트를 찾거나 만든다(기존 씬에는 이미 빈 RectTransform
        /// 홀더로 존재 - GameManagers가 `KBOManager.Managers` 계열을 모아두는 것과 동일하게, UI 액션
        /// 컨트롤러(`KBOManager.Controllers`)를 모아두는 용도로 씬에 이미 준비되어 있던 홀더를 재사용한다).</summary>
        private static GameObject FindOrCreateUIControllersHolder(Transform canvasTransform)
        {
            var existing = canvasTransform.Find(UIControllersHolderName);
            if (existing != null) return existing.gameObject;

            var holderObject = new GameObject(UIControllersHolderName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(holderObject, $"Create {UIControllersHolderName}");
            holderObject.transform.SetParent(canvasTransform, false);

            Debug.LogWarning($"[SetupRosterUI] 씬에서 '{UIControllersHolderName}' 오브젝트를 찾지 못해 " +
                "canvas 하위에 새로 생성했습니다.");

            return holderObject;
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
