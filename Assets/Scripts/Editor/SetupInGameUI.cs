using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-100] v0.3 Phase 3 킥오프 - `SceneInitializer.cs`가 이미 만들어 둔 `InGamePanel`/
    /// `InGameUIController`(패널 존재 + 컨트롤러 부착까지만 완료, `UIManager.screens` 등록도 이미
    /// 되어 있음)에서, 로비 화면에서 실제로 "진입"할 수 있는 경로를 완성한다.
    ///
    /// `LeagueDashboardUIController.StartMatch()`(기존 코드, 미수정)는 이미
    /// `playBallController.StartMatch()` 호출 직후 `UIManager.Instance.ShowScreen(ScreenType.InGame)`을
    /// 호출하도록 구현되어 있었다 - 즉 로비→인게임 화면 전환 로직 자체는 이미 완성되어 있었고, 실제로
    /// 비어 있던 것은 (1) `quickPlayButton`을 누를 UI 버튼 자체, (2) `LeagueDashboardUIController.
    /// playBallController` 필드, (3) 씬에 아예 존재하지 않던 `PlayBallController` 컴포넌트 3가지뿐이었다
    /// (`LeagueDashboardUIController.cs`/`PlayBallController.cs`는 이번에도 전혀 수정하지 않았다 -
    /// 명령서 5항 가드레일).
    ///
    /// [TASK-KBO-100] `PlayBallController.skillDB`/`engineConfig`는 `SetupManagers.cs`가 이미
    /// `Assets/GameData/`에 만들어 둔 에셋이 있으면 찾아서 바인딩하되(선택적, 없으면 비워둠), 새로
    /// 만들지는 않는다 - 에셋 생성 책임은 `SetupManagers.cs`에 있다.
    ///
    /// [TASK-KBO-101] TASK-100이 범위 제외했던 `InGameUIController`의 세부 UI 필드들을 마저 조립한다:
    /// 스코어보드(이닝별 득점 12칸 + 합계 2조), 중계 로그(ScrollRect+Content+Viewport, 로그 한 줄
    /// 템플릿은 `_Templates` 하위에 공유 배치), 경기 결과 팝업(요약/보상 텍스트 + 로비 복귀 버튼).
    /// `MatchEngine.cs`의 `RegulationInnings=9`/`MaxInnings=12`(private const, 205~206행)를 인용해
    /// 이닝 칸 수를 12로 고정했다(연장 포함 최대치 - 이 값들은 private라 직접 참조할 수 없어 상수를
    /// 주석으로 인용만 하고 리터럴 12를 사용했다).
    ///
    /// [TASK-KBO-101] `matchStatusUI`(다이아몬드 주자 표시 3개 + 볼/스트라이크/아웃 카운트 핍 7개,
    /// 총 10개의 `Image` 참조를 요구하는 `MatchStatusUI` 컴포넌트 자체를 새로 조립해야 함)는 그
    /// 태스크의 대상에서 제외됐었다 - 명령서 4항이 명시한 3개 영역(스코어보드/로그/팝업)에 포함되지
    /// 않았고, `InGameUIController` 자신의 주석("비워두면 초기화를 생략한다")이 이 필드를 선택적으로
    /// 취급하고 있어 AC-02("모든 미바인딩 필드")와 명령서 4항의 범위 나열이 서로 충돌한다고 판단했다.
    ///
    /// [TASK-KBO-102] TASK-101이 보류했던 `matchStatusUI`를 PM 지시로 마저 완성한다 - `InGamePanel`
    /// 하위에 `MatchStatusArea`(`MatchStatusUI` 부착)를 만들고, 그 하위에 베이스 3개(`FirstBaseImage`/
    /// `SecondBaseImage`/`ThirdBaseImage`)와 카운트 핍 7개(`BallPip1~3`/`StrikePip1~2`/`OutPip1~2`)
    /// 총 10개의 `Image`를 조립해 `MatchStatusUI.firstBaseImage`/`secondBaseImage`/`thirdBaseImage`/
    /// `ballPips`/`strikePips`/`outPips` 6개 필드(배열 3개 포함)에 바인딩한 뒤,
    /// `InGameUIController.matchStatusUI`에도 연결한다. `MatchStatusUI.cs`(베이스 점등/핍 갱신 로직)는
    /// 전혀 수정하지 않았다 - 명령서 5항 가드레일.
    /// </summary>
    public static class SetupInGameUI
    {
        private const string QuickPlayButtonName = "QuickPlayButton";
        private const string SkillDBAssetPath = "Assets/GameData/SkillDB.asset";
        private const string EngineConfigAssetPath = "Assets/GameData/EngineConfig.asset";

        [MenuItem("KBO Manager/Setup/Auto-Connect InGame UI")]
        public static void AutoConnectInGameUI()
        {
            var inGameController = Object.FindAnyObjectByType<InGameUIController>(FindObjectsInactive.Include);
            if (inGameController == null)
            {
                Debug.LogError("[SetupInGameUI] 씬에서 InGameUIController를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Initialize Current Scene'을 실행해 InGamePanel을 생성하십시오.");
                return;
            }

            var inGamePanelObject = inGameController.gameObject;
            var canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            var canvasTransform = canvas != null ? canvas.transform : inGamePanelObject.transform.root;

            var playBallController = EnsurePlayBallController(inGamePanelObject);
            BindInGameUIController(inGameController, playBallController);
            BindScoreboard(inGameController, inGamePanelObject.transform);
            BindLog(inGameController, inGamePanelObject.transform, canvasTransform);
            BindMatchEndPanel(inGameController, inGamePanelObject.transform);
            BindMatchStatusUI(inGameController, inGamePanelObject.transform);

            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogError("[SetupInGameUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "'매치 시작' 진입 버튼 생성을 건너뜁니다.");
            }
            else
            {
                var quickPlayButton = FindOrCreateButton(dashboard.transform, QuickPlayButtonName, "매치 시작", new Vector2(20f, 200f));
                BindDashboard(dashboard, quickPlayButton, playBallController);
                EditorUtility.SetDirty(dashboard);
            }

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning("[SetupInGameUI] 씬에서 UIManager를 찾지 못해 screens 등록을 건너뜁니다.");
            }
            else
            {
                RegisterInGameScreen(uiManager, inGamePanelObject);
                EditorUtility.SetDirty(uiManager);
            }

            EditorUtility.SetDirty(inGamePanelObject);

            var scene = inGamePanelObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupInGameUI] 인게임 매치 UI 진입 배선 완료.");
        }

        /// <summary>InGamePanel에 PlayBallController가 없으면 부착한다(TryGetComponent 가드로 중복 방지).
        /// skillDB/engineConfig는 SetupManagers.cs가 이미 만들어 둔 에셋이 있으면 찾아서 바인딩하되,
        /// 없으면 비워둔다(MatchEngine 생성자가 null을 이미 허용하므로 안전 - PlayBallController.cs
        /// 원문 확인).</summary>
        private static PlayBallController EnsurePlayBallController(GameObject inGamePanelObject)
        {
            if (!inGamePanelObject.TryGetComponent<PlayBallController>(out var playBallController))
            {
                playBallController = Undo.AddComponent<PlayBallController>(inGamePanelObject);
                Debug.Log($"[SetupInGameUI] 씬에서 PlayBallController를 찾지 못해 " +
                    $"'{inGamePanelObject.name}'에 새로 생성했습니다.");
            }

            var skillDB = AssetDatabase.LoadAssetAtPath<SkillDB>(SkillDBAssetPath);
            var engineConfig = AssetDatabase.LoadAssetAtPath<EngineConfig>(EngineConfigAssetPath);

            var serialized = new SerializedObject(playBallController);
            if (skillDB != null) serialized.FindProperty("skillDB").objectReferenceValue = skillDB;
            if (engineConfig != null) serialized.FindProperty("engineConfig").objectReferenceValue = engineConfig;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(playBallController);

            return playBallController;
        }

        /// <summary>InGameUIController.playBallController/matchRewardManager 2개 필드를 바인딩한다.
        /// 둘 다 씬에 이미 존재하는 컴포넌트를 참조로 연결하는 것뿐이라 새 UI 요소가 필요 없다. 나머지
        /// UI 필드(스코어보드/로그/팝업)는 BindScoreboard()/BindLog()/BindMatchEndPanel()이 담당한다.</summary>
        private static void BindInGameUIController(InGameUIController inGameController, PlayBallController playBallController)
        {
            var matchRewardManager = Object.FindAnyObjectByType<MatchRewardManager>(FindObjectsInactive.Include);

            var serialized = new SerializedObject(inGameController);
            serialized.FindProperty("playBallController").objectReferenceValue = playBallController;
            if (matchRewardManager != null)
            {
                serialized.FindProperty("matchRewardManager").objectReferenceValue = matchRewardManager;
            }
            else
            {
                Debug.LogWarning("[SetupInGameUI] 씬에서 MatchRewardManager를 찾지 못해 " +
                    "InGameUIController.matchRewardManager 바인딩을 건너뜁니다.");
            }
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(inGameController);
        }

        // [TASK-KBO-101] MatchEngine.cs 205~206행의 RegulationInnings=9/MaxInnings=12(둘 다 private
        // const라 직접 참조 불가)를 인용해, 연장까지 표시 가능하도록 이닝 칸 수를 12로 고정한다.
        private const int InningSlotCount = 12;
        private const string ScoreboardAreaName = "ScoreboardArea";
        private const string AwayScoreRowName = "AwayScoreRow";
        private const string HomeScoreRowName = "HomeScoreRow";
        private const string AwayTotalTextName = "AwayTotalText";
        private const string HomeTotalTextName = "HomeTotalText";
        private const string LogScrollViewName = "LogScrollView";
        private const string LogViewportName = "Viewport";
        private const string LogContentName = "Content";
        private const string TemplatesHolderName = "_Templates";
        private const string LogEntryTemplateName = "LogEntryTemplate";
        private const string MatchEndPanelName = "MatchEndPanel";
        private const string MatchResultSummaryTextName = "MatchResultSummaryText";
        private const string RewardSummaryTextName = "RewardSummaryText";
        private const string ReturnToLobbyButtonName = "ReturnToLobbyButton";

        /// <summary>
        /// InGamePanel 상단에 원정/홈 이닝별 득점 2줄(각 12칸 + 합계 1칸 = 13칸)을 GridLayoutGroup으로
        /// 조립하고, awayInningTexts/homeInningTexts(배열)/awayTotalText/homeTotalText 4개 필드를
        /// 바인딩한다. 배열 인덱스 0=1회이며, RefreshScoreboard()가 이 순서 그대로
        /// result.AwayInningScores[i]/HomeInningScores[i]를 읽으므로 생성 순서를 반드시 지킨다.
        /// </summary>
        private static void BindScoreboard(InGameUIController controller, Transform inGamePanelTransform)
        {
            var scoreboardArea = FindOrCreateChild(inGamePanelTransform, ScoreboardAreaName,
                new Vector2(0f, 0.85f), new Vector2(1f, 1f));

            var (awayTexts, awayTotal) = FindOrCreateScoreRow(scoreboardArea, AwayScoreRowName, "Away",
                AwayTotalTextName, new Vector2(0f, 0.5f), new Vector2(1f, 1f));
            var (homeTexts, homeTotal) = FindOrCreateScoreRow(scoreboardArea, HomeScoreRowName, "Home",
                HomeTotalTextName, new Vector2(0f, 0f), new Vector2(1f, 0.5f));

            var serialized = new SerializedObject(controller);
            var awayProperty = serialized.FindProperty("awayInningTexts");
            awayProperty.arraySize = awayTexts.Length;
            for (int i = 0; i < awayTexts.Length; i++) awayProperty.GetArrayElementAtIndex(i).objectReferenceValue = awayTexts[i];

            var homeProperty = serialized.FindProperty("homeInningTexts");
            homeProperty.arraySize = homeTexts.Length;
            for (int i = 0; i < homeTexts.Length; i++) homeProperty.GetArrayElementAtIndex(i).objectReferenceValue = homeTexts[i];

            serialized.FindProperty("awayTotalText").objectReferenceValue = awayTotal;
            serialized.FindProperty("homeTotalText").objectReferenceValue = homeTotal;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
        }

        /// <summary>이닝 칸(InningSlotCount개) + 합계 칸 1개, 총 (InningSlotCount+1)칸을 GridLayoutGroup
        /// 한 줄에 나열한다. teamPrefix는 자식 오브젝트 이름 접두사("Away"/"Home")로만 쓰인다.</summary>
        private static (Text[] inningTexts, Text totalText) FindOrCreateScoreRow(Transform parent, string rowName,
            string teamPrefix, string totalTextName, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existingRow = parent.Find(rowName);
            GameObject rowObject;
            if (existingRow != null)
            {
                rowObject = existingRow.gameObject;
            }
            else
            {
                rowObject = new GameObject(rowName, typeof(RectTransform), typeof(GridLayoutGroup));
                Undo.RegisterCreatedObjectUndo(rowObject, $"Create {rowName}");
                rowObject.transform.SetParent(parent, false);

                var rect = (RectTransform)rowObject.transform;
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                var grid = rowObject.GetComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(40f, 30f);
                grid.spacing = new Vector2(2f, 0f);
                grid.childAlignment = TextAnchor.MiddleLeft;
            }

            var inningTexts = new Text[InningSlotCount];
            for (int i = 0; i < InningSlotCount; i++)
            {
                inningTexts[i] = FindOrCreateText(rowObject.transform, $"{teamPrefix}Inning{i + 1:00}", (i + 1).ToString());
            }

            var totalText = FindOrCreateText(rowObject.transform, totalTextName, "0");

            return (inningTexts, totalText);
        }

        /// <summary>
        /// InGamePanel 하단에 표준 ScrollRect 계층(LogScrollView -&gt; Viewport(RectMask2D) -&gt;
        /// Content(VerticalLayoutGroup+ContentSizeFitter))을 조립하고 logContainer/logScrollRect를
        /// 바인딩한다. logEntryPrefab은 canvas 하위 `_Templates`(SetupScoutUI.cs의 PlayerCardTemplate과
        /// 같은 홀더 재사용)에 비활성 Text 템플릿으로 만든다 - AddLog()가 Instantiate() 직후
        /// entry.gameObject.SetActive(true)를 직접 호출하므로(원문 153행) 템플릿 자신은 비활성이어도
        /// 안전하다(PlayerCardTemplate 때와 달리 이 클래스는 활성화를 스스로 보정한다).
        /// </summary>
        private static void BindLog(InGameUIController controller, Transform inGamePanelTransform, Transform canvasTransform)
        {
            var scrollViewObject = FindOrCreateLogScrollView(inGamePanelTransform, out var content, out var scrollRect);
            var logEntryTemplate = FindOrCreateLogEntryTemplate(canvasTransform);

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("logContainer").objectReferenceValue = content;
            serialized.FindProperty("logScrollRect").objectReferenceValue = scrollRect;
            serialized.FindProperty("logEntryPrefab").objectReferenceValue = logEntryTemplate;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);

            EditorUtility.SetDirty(scrollViewObject);
        }

        private static GameObject FindOrCreateLogScrollView(Transform parent, out Transform content, out ScrollRect scrollRect)
        {
            var existing = parent.Find(LogScrollViewName);
            if (existing != null)
            {
                scrollRect = existing.GetComponent<ScrollRect>();
                var existingViewport = existing.Find(LogViewportName);
                content = existingViewport != null ? existingViewport.Find(LogContentName) : null;
                return existing.gameObject;
            }

            var scrollViewObject = new GameObject(LogScrollViewName, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            Undo.RegisterCreatedObjectUndo(scrollViewObject, $"Create {LogScrollViewName}");
            scrollViewObject.transform.SetParent(parent, false);

            var scrollViewRect = (RectTransform)scrollViewObject.transform;
            scrollViewRect.anchorMin = new Vector2(0f, 0f);
            scrollViewRect.anchorMax = new Vector2(0.5f, 0.85f);
            scrollViewRect.offsetMin = Vector2.zero;
            scrollViewRect.offsetMax = Vector2.zero;

            scrollViewObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.15f);

            var viewportObject = new GameObject(LogViewportName, typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            Undo.RegisterCreatedObjectUndo(viewportObject, $"Create {LogViewportName}");
            viewportObject.transform.SetParent(scrollViewObject.transform, false);

            var viewportRect = (RectTransform)viewportObject.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            viewportObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);

            var contentObject = new GameObject(LogContentName, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            Undo.RegisterCreatedObjectUndo(contentObject, $"Create {LogContentName}");
            contentObject.transform.SetParent(viewportObject.transform, false);

            var contentRect = (RectTransform)contentObject.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 0f);

            var verticalLayout = contentObject.GetComponent<VerticalLayoutGroup>();
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childControlHeight = true;

            var sizeFitter = contentObject.GetComponent<ContentSizeFitter>();
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRectComponent = scrollViewObject.GetComponent<ScrollRect>();
            scrollRectComponent.viewport = viewportRect;
            scrollRectComponent.content = contentRect;
            scrollRectComponent.horizontal = false;
            scrollRectComponent.vertical = true;

            content = contentObject.transform;
            scrollRect = scrollRectComponent;
            return scrollViewObject;
        }

        /// <summary>canvas 하위 `_Templates`(없으면 새로 생성 - SetupScoutUI.cs가 만든 홀더를 찾으면
        /// 그대로 재사용)에 로그 한 줄짜리 비활성 Text 템플릿을 만든다.</summary>
        private static Text FindOrCreateLogEntryTemplate(Transform canvasTransform)
        {
            var holderTransform = canvasTransform.Find(TemplatesHolderName);
            GameObject holderObject;
            if (holderTransform != null)
            {
                holderObject = holderTransform.gameObject;
            }
            else
            {
                holderObject = new GameObject(TemplatesHolderName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(holderObject, $"Create {TemplatesHolderName}");
                holderObject.transform.SetParent(canvasTransform, false);
                holderObject.SetActive(false);
            }

            var existingTemplate = holderObject.transform.Find(LogEntryTemplateName);
            if (existingTemplate != null && existingTemplate.TryGetComponent<Text>(out var existingText)) return existingText;

            var templateObject = new GameObject(LogEntryTemplateName, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(templateObject, $"Create {LogEntryTemplateName}");
            templateObject.transform.SetParent(holderObject.transform, false);

            var rect = (RectTransform)templateObject.transform;
            rect.sizeDelta = new Vector2(400f, 24f);

            var text = templateObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.fontSize = 14;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            templateObject.SetActive(false);
            return text;
        }

        /// <summary>InGamePanel 전체를 덮는 경기 결과 팝업(초기 비활성)을 조립하고
        /// matchEndPanelRoot/matchResultSummaryText/rewardSummaryText/returnToLobbyButton 4개 필드를
        /// 바인딩한다. returnToLobbyButton의 onClick(ReturnToLobby) 연결은 InGameUIController.Awake()
        /// (기존 코드, 미수정)가 담당한다.</summary>
        private static void BindMatchEndPanel(InGameUIController controller, Transform inGamePanelTransform)
        {
            var panelObject = FindOrCreateMatchEndPanel(inGamePanelTransform);
            var summaryText = FindOrCreateText(panelObject.transform, MatchResultSummaryTextName, "");
            var rewardText = FindOrCreateText(panelObject.transform, RewardSummaryTextName, "");
            var returnButton = FindOrCreateButton(panelObject.transform, ReturnToLobbyButtonName, "로비로 돌아가기", new Vector2(20f, 20f));

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("matchEndPanelRoot").objectReferenceValue = panelObject;
            serialized.FindProperty("matchResultSummaryText").objectReferenceValue = summaryText;
            serialized.FindProperty("rewardSummaryText").objectReferenceValue = rewardText;
            serialized.FindProperty("returnToLobbyButton").objectReferenceValue = returnButton;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);
        }

        private static GameObject FindOrCreateMatchEndPanel(Transform parent)
        {
            var existing = parent.Find(MatchEndPanelName);
            if (existing != null) return existing.gameObject;

            var panelObject = new GameObject(MatchEndPanelName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {MatchEndPanelName}");
            panelObject.transform.SetParent(parent, false);

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panelObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

            panelObject.SetActive(false);
            return panelObject;
        }

        private const string MatchStatusAreaName = "MatchStatusArea";
        private const string FirstBaseImageName = "FirstBaseImage";
        private const string SecondBaseImageName = "SecondBaseImage";
        private const string ThirdBaseImageName = "ThirdBaseImage";
        private const string BallPipNamePrefix = "BallPip";
        private const string StrikePipNamePrefix = "StrikePip";
        private const string OutPipNamePrefix = "OutPip";

        /// <summary>
        /// [TASK-KBO-102] `InGamePanel` 하위에 `MatchStatusArea`(`MatchStatusUI` 부착)를 만들고, 그
        /// 안에 베이스 3개 + 카운트 핍 7개(볼 3/스트라이크 2/아웃 2) 총 10개의 `Image`를 조립해
        /// `MatchStatusUI`의 6개 필드(`firstBaseImage`/`secondBaseImage`/`thirdBaseImage`/`ballPips`/
        /// `strikePips`/`outPips`)에 바인딩한 뒤, 완성된 컴포넌트를
        /// `InGameUIController.matchStatusUI`에도 연결한다. 명령서 5항에 따라 정밀한 다이아몬드 좌표
        /// 배치 없이 GridLayoutGroup 한 줄에 10칸을 순서대로 나열한다(기능 테스트 목적, 명령서 5항).
        /// </summary>
        private static void BindMatchStatusUI(InGameUIController inGameController, Transform inGamePanelTransform)
        {
            var matchStatusUI = FindOrCreateMatchStatus(inGamePanelTransform);

            var serialized = new SerializedObject(inGameController);
            serialized.FindProperty("matchStatusUI").objectReferenceValue = matchStatusUI;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(inGameController);
        }

        /// <summary>`MatchStatusArea`를 찾거나 만들고(`MatchStatusUI` 컴포넌트 부착, `TryGetComponent`
        /// 가드로 중복 방지), 10개의 `Image`를 조립해 `MatchStatusUI`의 6개 필드에 바인딩한다.</summary>
        private static MatchStatusUI FindOrCreateMatchStatus(Transform inGamePanelTransform)
        {
            var areaTransform = FindOrCreateChild(inGamePanelTransform, MatchStatusAreaName,
                new Vector2(0f, 0.7f), new Vector2(1f, 0.85f));

            var areaObject = areaTransform.gameObject;
            if (!areaObject.TryGetComponent<GridLayoutGroup>(out _))
            {
                var grid = areaObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = new Vector2(30f, 30f);
                grid.spacing = new Vector2(4f, 0f);
                grid.childAlignment = TextAnchor.MiddleLeft;
            }

            if (!areaObject.TryGetComponent<MatchStatusUI>(out var matchStatusUI))
            {
                matchStatusUI = Undo.AddComponent<MatchStatusUI>(areaObject);
                Debug.Log($"[SetupInGameUI] 씬에서 MatchStatusUI를 찾지 못해 '{areaObject.name}'에 새로 생성했습니다.");
            }

            var firstBase = FindOrCreateImage(areaTransform, FirstBaseImageName);
            var secondBase = FindOrCreateImage(areaTransform, SecondBaseImageName);
            var thirdBase = FindOrCreateImage(areaTransform, ThirdBaseImageName);

            var ballPips = new Image[3];
            for (int i = 0; i < ballPips.Length; i++) ballPips[i] = FindOrCreateImage(areaTransform, $"{BallPipNamePrefix}{i + 1}");

            var strikePips = new Image[2];
            for (int i = 0; i < strikePips.Length; i++) strikePips[i] = FindOrCreateImage(areaTransform, $"{StrikePipNamePrefix}{i + 1}");

            var outPips = new Image[2];
            for (int i = 0; i < outPips.Length; i++) outPips[i] = FindOrCreateImage(areaTransform, $"{OutPipNamePrefix}{i + 1}");

            var serialized = new SerializedObject(matchStatusUI);
            serialized.FindProperty("firstBaseImage").objectReferenceValue = firstBase;
            serialized.FindProperty("secondBaseImage").objectReferenceValue = secondBase;
            serialized.FindProperty("thirdBaseImage").objectReferenceValue = thirdBase;

            var ballProperty = serialized.FindProperty("ballPips");
            ballProperty.arraySize = ballPips.Length;
            for (int i = 0; i < ballPips.Length; i++) ballProperty.GetArrayElementAtIndex(i).objectReferenceValue = ballPips[i];

            var strikeProperty = serialized.FindProperty("strikePips");
            strikeProperty.arraySize = strikePips.Length;
            for (int i = 0; i < strikePips.Length; i++) strikeProperty.GetArrayElementAtIndex(i).objectReferenceValue = strikePips[i];

            var outProperty = serialized.FindProperty("outPips");
            outProperty.arraySize = outPips.Length;
            for (int i = 0; i < outPips.Length; i++) outProperty.GetArrayElementAtIndex(i).objectReferenceValue = outPips[i];

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(matchStatusUI);

            return matchStatusUI;
        }

        private static Image FindOrCreateImage(Transform parent, string name)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null && existingChild.TryGetComponent<Image>(out var existingImage)) return existingImage;

            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(imageObject, $"Create {name}");
            imageObject.transform.SetParent(parent, false);

            var image = imageObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.25f);
            image.raycastTarget = false;

            return image;
        }

        private static Transform FindOrCreateChild(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing;

            var childObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(childObject, $"Create {name}");
            childObject.transform.SetParent(parent, false);

            var rect = (RectTransform)childObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return childObject.transform;
        }

        private static Text FindOrCreateText(Transform parent, string name, string defaultText)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null && existingChild.TryGetComponent<Text>(out var existingText)) return existingText;

            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
            textObject.transform.SetParent(parent, false);

            var rect = (RectTransform)textObject.transform;
            rect.sizeDelta = new Vector2(160f, 30f);

            var text = textObject.GetComponent<Text>();
            text.text = defaultText;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }

        private static void BindDashboard(LeagueDashboardUIController dashboard, Button quickPlayButton, PlayBallController playBallController)
        {
            var serialized = new SerializedObject(dashboard);
            serialized.FindProperty("quickPlayButton").objectReferenceValue = quickPlayButton;
            serialized.FindProperty("playBallController").objectReferenceValue = playBallController;
            serialized.ApplyModifiedProperties();
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

        /// <summary>
        /// UIManager.screens(List&lt;ScreenEntry&gt;, 필드는 Type/Root)에 InGame 항목을 등록한다.
        /// SceneInitializer.cs가 이미 등록해 두었을 가능성이 높지만(InGamePanel 자체를 그 스크립트가
        /// 만들었으므로), 방어적으로 다시 확인한다 - 이미 등록되어 있으면 Root 참조만 최신으로 덮어써
        /// 중복 추가를 막는다(SetupRosterUI.RegisterRosterScreen()과 동일한 관례).
        /// </summary>
        private static void RegisterInGameScreen(UIManager uiManager, GameObject inGameRoot)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                var typeProperty = element.FindPropertyRelative("Type");
                if (typeProperty.intValue == (int)ScreenType.InGame)
                {
                    element.FindPropertyRelative("Root").objectReferenceValue = inGameRoot;
                    serializedManager.ApplyModifiedProperties();
                    return;
                }
            }

            int newIndex = screensProperty.arraySize;
            screensProperty.arraySize++;

            var newElement = screensProperty.GetArrayElementAtIndex(newIndex);
            newElement.FindPropertyRelative("Type").intValue = (int)ScreenType.InGame;
            newElement.FindPropertyRelative("Root").objectReferenceValue = inGameRoot;

            serializedManager.ApplyModifiedProperties();
        }
    }
}
