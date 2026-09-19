using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Managers;
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
    /// [범위 제외 명시] `InGameUIController`의 나머지 11개 필드(이닝별 스코어 텍스트, 로그 프리팹/
    /// 컨테이너/스크롤뷰, 경기 종료 팝업 등)는 실제 UI 요소를 새로 설계·조립해야 하는 훨씬 큰 작업이라
    /// 이번 "킥오프" 범위에 포함하지 않았다 - `SetupScoutUI.cs`가 TASK-083(패널만) → TASK-092(카드
    /// 템플릿 완성)로 2단계에 나눠 작업했던 전례를 그대로 따라, 인게임 화면의 세부 UI 조립은 후속
    /// 태스크로 남긴다. `PlayBallController.skillDB`/`engineConfig`는 `SetupManagers.cs`가 이미
    /// `Assets/GameData/`에 만들어 둔 에셋이 있으면 찾아서 바인딩하되(선택적, 없으면 비워둠), 새로
    /// 만들지는 않는다 - 에셋 생성 책임은 `SetupManagers.cs`에 있다.
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
            var playBallController = EnsurePlayBallController(inGamePanelObject);
            BindInGameUIController(inGameController, playBallController);

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

        /// <summary>InGameUIController.playBallController/matchRewardManager 2개 필드만 바인딩한다.
        /// 둘 다 씬에 이미 존재하는 컴포넌트를 참조로 연결하는 것뿐이라 새 UI 요소가 필요 없다 - 로그
        /// 시스템/이닝 텍스트/경기 종료 팝업 등 나머지 필드는 범위 제외(클래스 주석 참고).</summary>
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
