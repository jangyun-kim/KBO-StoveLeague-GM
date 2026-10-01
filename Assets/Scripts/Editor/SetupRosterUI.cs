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
    /// QA가 메뉴 클릭 한 번으로 씬에 조립·배선할 수 있게 하는 에디터 자동화. 로비 패널에 진입 버튼(TASK-099
    /// 이후 라벨 "라인업"), 로스터 패널에 "닫기" 버튼을 만들어 바인딩하고, UIManager.screens에 Roster
    /// 화면을 등록한다.
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
    ///
    /// [TASK-KBO-098] 기획 PM 지시로 로비 진입 버튼 라벨을 "로스터 관리"에서 "구단 관리"로 변경한다
    /// (`RosterButtonName`(내부 오브젝트 이름 "RosterButton")과 `RosterPanelName`은 그대로 둔다 - 식별자
    /// 변경이 아니라 사용자에게 보이는 문자열만 바꾸는 작업이라, 내부 이름을 바꾸면 기존 씬에 이미
    /// 생성된 오브젝트를 재사용하지 못하고 중복 생성될 위험만 생긴다). `RosterPanel` 하위에 Empty
    /// State 안내 텍스트(`EmptyStateText`)를 신설해 `RosterUIController.emptyStateText`에 바인딩한다
    /// (`RosterUIController.cs`의 신규 `RefreshEmptyState()`가 소비). 이 라벨은 원문 GDD의 "구단 관리"
    /// (도감/라커룸/특별 제작소 등)와 명칭이 충돌한다는 사실을 완료 보고서에 [결정 필요]로 남겼다.
    ///
    /// [TASK-KBO-099] TASK-098이 남긴 명칭 충돌([결정 필요])을 PM이 "라인업"으로 확정해, 로비 진입 버튼
    /// 라벨을 "구단 관리"에서 "라인업"으로 다시 변경한다(내부 오브젝트 이름은 이번에도 그대로 유지 -
    /// TASK-098과 동일한 이유).
    ///
    /// [TASK-KBO-176] 후보 6인 수동 교체 + 세트덱 선택형 버프 UI를 같은 메뉴에 통합했다(여러 번 실행해도 안전 -
    /// 이름으로 찾아 재사용하고, 레이아웃 값은 매번 다시 적용해 구버전 씬도 최신 배치로 맞춘다):
    ///   - 타자 구역을 주전(BatterContainer, 위)/후보(BenchContainer, 아래)로 나누고 머리글 3개(주전/후보/투수)를 만든다.
    ///   - SwapPopup(카드 클릭 시 열리는 교체 팝업: 제목/미리보기/스크롤 후보 그리드/교체·취소/빈 목록 안내).
    ///   - SetDeckOptionButton + SetDeckOptionPanel(선택형 10개 구간 A/B 행 템플릿, 연도 선택, 닫기) +
    ///     SetDeckOptionUIController(RosterPanel에 부착).
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
        private const string EmptyStateTextName = "EmptyStateText";

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
            BindEmptyStateText(rosterController);
            BindBenchAndSwapPopup(rosterController);   // [TASK-KBO-176]
            BindSetDeckOptionPanel(rosterController);  // [TASK-KBO-176]
            var gameActionController = BindGameActionController(rosterController, canvas.transform);
            BindRosterManager(gameActionController);

            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupRosterUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "'라인업' 진입 버튼 생성 및 UIManager 등록을 건너뜁니다.");
            }
            else
            {
                var rosterButton = FindOrCreateButton(dashboard.transform, RosterButtonName, "라인업", new Vector2(20f, 140f));
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
            text.font = KBOFonts.Default;
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
        /// [TASK-KBO-098] RosterPanel 중앙에 Empty State 안내 텍스트를 조립하고
        /// RosterUIController.emptyStateText에 바인딩한다. RosterUIController.RefreshEmptyState()가
        /// 로스터가 비어 있을 때만 SetActive(true)로 켜므로, 초기 상태는 비활성으로 둔다.
        /// </summary>
        private static void BindEmptyStateText(RosterUIController controller)
        {
            var emptyStateText = FindOrCreateEmptyStateText(controller.transform);

            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("emptyStateText").objectReferenceValue = emptyStateText;
            serializedController.ApplyModifiedProperties();
        }

        private static Text FindOrCreateEmptyStateText(Transform parent)
        {
            var existingChild = parent.Find(EmptyStateTextName);
            if (existingChild != null && existingChild.TryGetComponent<Text>(out var existingText)) return existingText;

            var textObject = new GameObject(EmptyStateTextName, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {EmptyStateTextName}");
            textObject.transform.SetParent(parent, false);

            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = new Vector2(0f, 0.4f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.4f, 0.4f, 0.4f);
            text.fontSize = 24;
            text.font = KBOFonts.Default;
            text.raycastTarget = false;

            textObject.SetActive(false);
            return text;
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
        /// - `AutoSetRoster(List&lt;Player&gt; inventory, string favoriteTeam)`(TASK-KBO-173: 샐러리 캡 폐기)가 인벤토리/기준 구단을 전부 메서드
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

        // ----- [TASK-KBO-176] 후보 구역 / 교체 팝업 / 세트덱 선택형 버프 패널 -----

        private const string BenchContainerName = "BenchContainer";
        private const string StarterHeaderName = "StarterHeaderText";
        private const string BenchHeaderName = "BenchHeaderText";
        private const string PitcherHeaderName = "PitcherHeaderText";
        private const string SwapPopupName = "SwapPopup";
        private const string SetDeckOptionButtonName = "SetDeckOptionButton";
        private const string SetDeckOptionPanelName = "SetDeckOptionPanel";

        /// <summary>
        /// 타자 구역을 주전 9(위)/후보 6(아래)로 나누고, 카드 클릭 시 열리는 교체 팝업을 조립해 RosterUIController의
        /// benchContainer/benchHeaderText/swap* 필드에 바인딩한다. 기존 BatterContainer/PitcherContainer도 앵커를 다시
        /// 적용한다(구버전 씬은 타자 컨테이너가 패널 왼쪽 전체를 차지했다).
        /// </summary>
        private static void BindBenchAndSwapPopup(RosterUIController controller)
        {
            var panel = controller.transform;
            SetAnchors(FindOrCreateCardGridContainer(panel, BatterContainerName, Vector2.zero, Vector2.one), new Vector2(0f, 0.38f), new Vector2(0.5f, 0.8f));
            SetAnchors(FindOrCreateCardGridContainer(panel, PitcherContainerName, Vector2.zero, Vector2.one), new Vector2(0.5f, 0.08f), new Vector2(1f, 0.8f));
            var benchContainer = FindOrCreateCardGridContainer(panel, BenchContainerName, Vector2.zero, Vector2.one);
            SetAnchors(benchContainer, new Vector2(0f, 0.08f), new Vector2(0.5f, 0.33f));

            FindOrCreateLabel(panel, StarterHeaderName, "주전 타자 9인 (포지션별 최고 OVR · 카드를 눌러 교체)", new Vector2(0f, 0.8f), new Vector2(0.5f, 0.85f), 18);
            var benchHeader = FindOrCreateLabel(panel, BenchHeaderName, "후보 6인", new Vector2(0f, 0.33f), new Vector2(0.5f, 0.38f), 18);
            FindOrCreateLabel(panel, PitcherHeaderName, "투수 13인 (카드를 눌러 같은 보직과 교체)", new Vector2(0.5f, 0.8f), new Vector2(1f, 0.85f), 18);

            var popup = FindOrCreatePanel(panel, SwapPopupName, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.6f));
            var inner = FindOrCreatePanel(popup.transform, "Window", new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f), new Color(0.97f, 0.97f, 0.97f, 1f));
            var title = FindOrCreateLabel(inner.transform, "TitleText", "교체", new Vector2(0f, 0.9f), new Vector2(1f, 1f), 20);
            var preview = FindOrCreateLabel(inner.transform, "PreviewText", "교체할 카드를 선택하십시오.", new Vector2(0f, 0.82f), new Vector2(1f, 0.9f), 18);
            var candidateContent = FindOrCreateScrollGrid(inner.transform, "CandidateScroll", new Vector2(0.02f, 0.12f), new Vector2(0.98f, 0.82f));
            var emptyText = FindOrCreateLabel(inner.transform, "EmptyText", "교체할 수 있는 보유 카드가 없습니다.", new Vector2(0f, 0.4f), new Vector2(1f, 0.5f), 20);
            emptyText.gameObject.SetActive(false);
            var confirm = FindOrCreateStretchButton(inner.transform, "ConfirmButton", "교체", new Vector2(0.55f, 0.02f), new Vector2(0.75f, 0.1f));
            var cancel = FindOrCreateStretchButton(inner.transform, "CancelButton", "취소", new Vector2(0.77f, 0.02f), new Vector2(0.97f, 0.1f));
            popup.transform.SetAsLastSibling();
            popup.SetActive(false);

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("benchContainer").objectReferenceValue = benchContainer;
            serialized.FindProperty("benchHeaderText").objectReferenceValue = benchHeader;
            serialized.FindProperty("swapPopupRoot").objectReferenceValue = popup;
            serialized.FindProperty("swapTitleText").objectReferenceValue = title;
            serialized.FindProperty("swapPreviewText").objectReferenceValue = preview;
            serialized.FindProperty("swapCandidateContainer").objectReferenceValue = candidateContent;
            serialized.FindProperty("swapConfirmButton").objectReferenceValue = confirm;
            serialized.FindProperty("swapCancelButton").objectReferenceValue = cancel;
            serialized.FindProperty("swapEmptyText").objectReferenceValue = emptyText;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// [세트덱 버프 선택] 버튼과 선택형 구간 패널을 조립하고, RosterPanel에 SetDeckOptionUIController를 붙여 바인딩한다.
        /// 행(구간별 A/B)은 런타임에 rowTemplate을 복제해 만든다 - 구간표(SetDeckBuffTable)가 바뀌어도 씬을 다시 조립할 필요가 없다.
        /// </summary>
        private static void BindSetDeckOptionPanel(RosterUIController rosterController)
        {
            var panel = rosterController.transform;
            var openButton = FindOrCreateButton(panel, SetDeckOptionButtonName, "세트덱 버프 선택", new Vector2(200f, 20f));

            var optionPanel = FindOrCreatePanel(panel, SetDeckOptionPanelName, Vector2.zero, Vector2.one, new Color(0f, 0f, 0f, 0.6f));
            var window = FindOrCreatePanel(optionPanel.transform, "Window", new Vector2(0.1f, 0.05f), new Vector2(0.9f, 0.95f), new Color(0.97f, 0.97f, 0.97f, 1f));
            FindOrCreateLabel(window.transform, "TitleText", "세트덱 선택형 버프 구간 (A/B 중 하나 선택 · 미도달 구간도 미리 선택 가능)", new Vector2(0f, 0.93f), new Vector2(1f, 1f), 20);
            var summary = FindOrCreateLabel(window.transform, "SummaryText", "", new Vector2(0f, 0.87f), new Vector2(1f, 0.93f), 16);

            var rows = FindOrCreateRect(window.transform, "Rows", new Vector2(0.02f, 0.12f), new Vector2(0.98f, 0.86f));
            var layout = GetOrAdd<VerticalLayoutGroup>(rows.gameObject);
            layout.spacing = 4f;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var rowTemplate = FindOrCreateOptionRowTemplate(window.transform);

            var yearButton = FindOrCreateStretchButton(window.transform, "YearButton", "연도 선택: 자동", new Vector2(0.02f, 0.02f), new Vector2(0.4f, 0.1f));
            var closeButton = FindOrCreateStretchButton(window.transform, "CloseButton", "닫기", new Vector2(0.78f, 0.02f), new Vector2(0.98f, 0.1f));
            optionPanel.transform.SetAsLastSibling();
            optionPanel.SetActive(false);

            var optionController = GetOrAdd<SetDeckOptionUIController>(rosterController.gameObject);
            var serializedOption = new SerializedObject(optionController);
            serializedOption.FindProperty("panelRoot").objectReferenceValue = optionPanel;
            serializedOption.FindProperty("summaryText").objectReferenceValue = summary;
            serializedOption.FindProperty("rowContainer").objectReferenceValue = rows;
            serializedOption.FindProperty("rowTemplate").objectReferenceValue = rowTemplate;
            serializedOption.FindProperty("yearButton").objectReferenceValue = yearButton;
            serializedOption.FindProperty("yearText").objectReferenceValue = yearButton.GetComponentInChildren<Text>(true);
            serializedOption.FindProperty("closeButton").objectReferenceValue = closeButton;
            serializedOption.ApplyModifiedProperties();
            EditorUtility.SetDirty(optionController);

            var serializedRoster = new SerializedObject(rosterController);
            serializedRoster.FindProperty("setDeckOptionButton").objectReferenceValue = openButton;
            serializedRoster.FindProperty("setDeckOptionController").objectReferenceValue = optionController;
            serializedRoster.ApplyModifiedProperties();
        }

        /// <summary>구간 행 템플릿(비활성): Label(구간/도달 여부) + OptionA + OptionB 버튼. Rows 밖(Window 직속)에 둬서
        /// VerticalLayoutGroup 배치에 끼지 않게 한다.</summary>
        private static GameObject FindOrCreateOptionRowTemplate(Transform window)
        {
            var row = FindOrCreateRect(window, "OptionRowTemplate", new Vector2(0f, 0f), new Vector2(1f, 0f));
            row.sizeDelta = new Vector2(0f, 52f);
            var element = GetOrAdd<LayoutElement>(row.gameObject);
            element.preferredHeight = 52f;
            element.minHeight = 44f;

            FindOrCreateLabel(row, "Label", "80P", new Vector2(0f, 0f), new Vector2(0.16f, 1f), 16);
            FindOrCreateStretchButton(row, "OptionA", "A", new Vector2(0.17f, 0.05f), new Vector2(0.58f, 0.95f));
            FindOrCreateStretchButton(row, "OptionB", "B", new Vector2(0.59f, 0.05f), new Vector2(1f, 0.95f));
            row.gameObject.SetActive(false);
            return row.gameObject;
        }

        /// <summary>에디터의 GetComponent는 컴포넌트가 없을 때 C# null이 아닌 "가짜 null"을 돌려줄 수 있어 `??`가 동작하지
        /// 않는다 - TryGetComponent로 확인하고 없을 때만 Undo.AddComponent한다.</summary>
        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            if (!go.TryGetComponent<T>(out var component)) component = Undo.AddComponent<T>(go);
            return component;
        }

        private static void SetAnchors(Transform target, Vector2 anchorMin, Vector2 anchorMax)
        {
            var rect = (RectTransform)target;
            Undo.RecordObject(rect, "Set Anchors");
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform FindOrCreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existing = parent.Find(name);
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject(name, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
                go.transform.SetParent(parent, false);
            }
            SetAnchors(go.transform, anchorMin, anchorMax);
            return (RectTransform)go.transform;
        }

        private static GameObject FindOrCreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Color color)
        {
            var rect = FindOrCreateRect(parent, name, anchorMin, anchorMax);
            var image = GetOrAdd<Image>(rect.gameObject);
            image.color = color;
            image.raycastTarget = true; // 오버레이 뒤 카드 클릭 차단
            return rect.gameObject;
        }

        private static Text FindOrCreateLabel(Transform parent, string name, string defaultText, Vector2 anchorMin, Vector2 anchorMax, int fontSize)
        {
            var rect = FindOrCreateRect(parent, name, anchorMin, anchorMax);
            bool created = !rect.TryGetComponent<Text>(out _);
            var text = GetOrAdd<Text>(rect.gameObject);
            if (created || string.IsNullOrEmpty(text.text)) text.text = defaultText;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = fontSize;
            text.font = KBOFonts.Default;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>앵커 스트레치형 버튼(라벨 자식 "Label"). 여러 번 실행해도 라벨 텍스트는 처음 생성 때만 쓴다
        /// (런타임 컨트롤러가 덮어쓰는 문구를 매번 초기화하지 않기 위함).</summary>
        private static Button FindOrCreateStretchButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax)
        {
            var rect = FindOrCreateRect(parent, name, anchorMin, anchorMax);
            var image = GetOrAdd<Image>(rect.gameObject);
            image.color = new Color(0.9f, 0.9f, 0.9f);
            var button = GetOrAdd<Button>(rect.gameObject);
            button.targetGraphic = image;
            FindOrCreateLabel(rect, "Label", label, Vector2.zero, Vector2.one, 16);
            return button;
        }

        /// <summary>세로 스크롤 + 카드 그리드(140x200, PlayerCardTemplate과 같은 셀) - 반환값은 카드가 담길 Content.</summary>
        private static Transform FindOrCreateScrollGrid(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var viewport = FindOrCreateRect(parent, name, anchorMin, anchorMax);
            var image = GetOrAdd<Image>(viewport.gameObject);
            image.color = new Color(1f, 1f, 1f, 0.02f);
            if (viewport.GetComponent<RectMask2D>() == null) Undo.AddComponent<RectMask2D>(viewport.gameObject);

            var content = FindOrCreateRect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f));
            content.pivot = new Vector2(0.5f, 1f);
            var grid = GetOrAdd<GridLayoutGroup>(content.gameObject);
            grid.cellSize = new Vector2(140f, 200f);
            grid.spacing = new Vector2(10f, 10f);
            grid.childAlignment = TextAnchor.UpperCenter;
            var fitter = GetOrAdd<ContentSizeFitter>(content.gameObject);
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = GetOrAdd<ScrollRect>(viewport.gameObject);
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return content;
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
            text.font = KBOFonts.Default;
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
