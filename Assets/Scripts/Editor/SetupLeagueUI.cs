using KBOManager.Controllers;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-103] v0.3 Phase 4 킥오프 - 로비 화면(`LeagueDashboardUIController`)의 리그 정보 표시
    /// UI(시즌 진행도/순위표 10줄/다음 매치업/팀 OVR)를 자동 조립하고 배선한다.
    ///
    /// `LeagueDashboardUIController.RefreshDashboard()`(기존 코드, 미수정)는 `leagueManager == null`이면
    /// 즉시 반환하도록 가드되어 있어(원문 95행), `leagueManager` 필드 자체가 비어 있으면 이번에 새로
    /// 만드는 텍스트들이 전부 채워지지 않는다 - 그래서 명령서 4항이 명시하지 않았지만 씬에 이미 존재하는
    /// `LeagueManager`(`GameManagers`에 부착, `TASK-KBO-096`~`097` 조사에서 이미 확인된 컴포넌트)를 찾아
    /// 함께 바인딩한다(기존 컴포넌트 참조 연결일 뿐 새 UI가 필요 없다).
    ///
    /// [TASK-KBO-107] `synergyUIController`(`TeamSynergyUIController`)도 이번에 함께 조립한다 -
    /// `TeamSynergyArea` 하위에 `SetDeckText`/`CheerleaderText`/`FanSentimentText` 3개 텍스트를 만들어
    /// `TeamSynergyUIController`에 바인딩하고, 그 컨트롤러 자체를 `LeagueDashboardUIController.
    /// synergyUIController`에 연결한다. `shopButton`/`leagueStatsButton`은 명령서 4항이 열거한 UI
    /// 영역(시즌 진행도/순위표/매치업/시너지)에 포함되지 않고 "리그" 데이터가 아닌 별개 화면
    /// 내비게이션이라 여전히 건드리지 않는다. `premiumCurrencyLobbyText`는 이미 씬에 바인딩되어 있어
    /// (TASK-KBO-071 등) 손대지 않았다.
    /// </summary>
    public static class SetupLeagueUI
    {
        private const string SeasonProgressTextName = "SeasonProgressText";
        private const string StandingsContainerName = "StandingsContainer";
        private const string StandingsRowNamePrefix = "StandingsRow";
        private const string NextMatchupTextName = "NextMatchupText";
        private const string TeamOVRTextName = "TeamOVRText";
        private const string TeamSynergyAreaName = "TeamSynergyArea";
        private const string SetDeckTextName = "SetDeckText";
        private const string CheerleaderTextName = "CheerleaderText";
        private const string FanSentimentTextName = "FanSentimentText";

        // LeagueDashboardUIController.standingsRowTexts 배열은 10개 구단 고정(원문 29~31행 - "10개 구단
        // 고정이므로 동적 생성 없이 텍스트 10줄을 그대로 인스펙터에서 연결한다")이므로 그대로 10을 쓴다.
        private const int StandingsRowCount = 10;

        [MenuItem("KBO Manager/Setup/Auto-Connect League UI")]
        public static void AutoConnectLeagueUI()
        {
            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogError("[SetupLeagueUI] 씬에서 LeagueDashboardUIController를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Initialize Current Scene'을 실행해 LobbyPanel을 생성하십시오.");
                return;
            }

            var seasonProgressText = FindOrCreateText(dashboard.transform, SeasonProgressTextName, "",
                new Vector2(0f, 0.95f), new Vector2(1f, 1f));
            var standingsRows = FindOrCreateStandingsRows(dashboard.transform);
            var nextMatchupText = FindOrCreateText(dashboard.transform, NextMatchupTextName, "",
                new Vector2(0f, 0.4f), new Vector2(1f, 0.45f));
            var teamOVRText = FindOrCreateText(dashboard.transform, TeamOVRTextName, "",
                new Vector2(0f, 0.9f), new Vector2(0.3f, 0.95f));

            var leagueManager = Object.FindAnyObjectByType<LeagueManager>(FindObjectsInactive.Include);
            if (leagueManager == null)
            {
                Debug.LogWarning("[SetupLeagueUI] 씬에서 LeagueManager를 찾지 못해 " +
                    "leagueManager 바인딩을 건너뜁니다. RefreshDashboard()가 이 필드 없이는 아무것도 " +
                    "갱신하지 않으니(원문 95행 가드) 참고하십시오.");
            }

            var synergyController = FindOrCreateSynergyUI(dashboard.transform);

            BindDashboard(dashboard, leagueManager, seasonProgressText, standingsRows, nextMatchupText,
                teamOVRText, synergyController);

            EditorUtility.SetDirty(dashboard);

            var scene = dashboard.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupLeagueUI] 리그 대시보드 UI 자동 배선 완료.");
        }

        /// <summary>StandingsContainer(없으면 새로 생성) 하위에 StandingsRow01~10(Text) 10개를
        /// Find-or-Create로 조립한다. 배열 길이는 LeagueDashboardUIController.standingsRowTexts가
        /// 10개 구단 고정을 전제로 하므로(원문 주석) StandingsRowCount=10을 그대로 쓴다 - 인덱스
        /// 초과 참조가 생기지 않는다(명령서 9항).</summary>
        private static Text[] FindOrCreateStandingsRows(Transform parent)
        {
            var containerTransform = FindOrCreateChild(parent, StandingsContainerName,
                new Vector2(0f, 0.45f), new Vector2(1f, 0.9f));

            if (!containerTransform.TryGetComponent<VerticalLayoutGroup>(out _))
            {
                var layout = containerTransform.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childForceExpandHeight = false;
                layout.childControlHeight = false;
            }

            var rows = new Text[StandingsRowCount];
            for (int i = 0; i < StandingsRowCount; i++)
            {
                rows[i] = FindOrCreateStandingsRowText(containerTransform, $"{StandingsRowNamePrefix}{i + 1:00}");
            }

            return rows;
        }

        private static Text FindOrCreateStandingsRowText(Transform parent, string name)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null && existingChild.TryGetComponent<Text>(out var existingText)) return existingText;

            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
            textObject.transform.SetParent(parent, false);

            var rect = (RectTransform)textObject.transform;
            rect.sizeDelta = new Vector2(400f, 24f);

            var text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.black;
            text.fontSize = 14;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }

        private static void BindDashboard(LeagueDashboardUIController dashboard, LeagueManager leagueManager,
            Text seasonProgressText, Text[] standingsRows, Text nextMatchupText, Text teamOVRText,
            TeamSynergyUIController synergyController)
        {
            var serialized = new SerializedObject(dashboard);

            if (leagueManager != null) serialized.FindProperty("leagueManager").objectReferenceValue = leagueManager;

            serialized.FindProperty("seasonProgressText").objectReferenceValue = seasonProgressText;
            serialized.FindProperty("nextMatchupText").objectReferenceValue = nextMatchupText;
            serialized.FindProperty("teamOVRText").objectReferenceValue = teamOVRText;
            serialized.FindProperty("synergyUIController").objectReferenceValue = synergyController;

            var standingsProperty = serialized.FindProperty("standingsRowTexts");
            standingsProperty.arraySize = standingsRows.Length;
            for (int i = 0; i < standingsRows.Length; i++)
            {
                standingsProperty.GetArrayElementAtIndex(i).objectReferenceValue = standingsRows[i];
            }

            serialized.ApplyModifiedProperties();
        }

        /// <summary>
        /// [TASK-KBO-107] `TeamSynergyArea`(없으면 신규 생성, 있으면 재사용 - 중복 생성 방지) 하위에
        /// `TeamSynergyUIController.cs` 원문(23~25행)이 요구하는 3개 Text(`setDeckText`/
        /// `cheerleaderText`/`fanSentimentText`)를 조립해 바인딩한다. 컨트롤러 컴포넌트 자체도
        /// `TryGetComponent`로 이미 붙어 있으면 재사용하고, 없을 때만 새로 붙인다(명령서 7항).
        /// </summary>
        private static TeamSynergyUIController FindOrCreateSynergyUI(Transform dashboardTransform)
        {
            var areaTransform = FindOrCreateChild(dashboardTransform, TeamSynergyAreaName,
                new Vector2(0.5f, 0.05f), new Vector2(1f, 0.4f));

            if (!areaTransform.TryGetComponent<VerticalLayoutGroup>(out _))
            {
                var layout = areaTransform.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childForceExpandHeight = false;
                layout.childControlHeight = false;
            }

            if (!areaTransform.TryGetComponent<TeamSynergyUIController>(out var synergyController))
            {
                synergyController = Undo.AddComponent<TeamSynergyUIController>(areaTransform.gameObject);
            }

            var setDeckText = FindOrCreateStandingsRowText(areaTransform, SetDeckTextName);
            var cheerleaderText = FindOrCreateStandingsRowText(areaTransform, CheerleaderTextName);
            var fanSentimentText = FindOrCreateStandingsRowText(areaTransform, FanSentimentTextName);

            var serializedController = new SerializedObject(synergyController);
            serializedController.FindProperty("setDeckText").objectReferenceValue = setDeckText;
            serializedController.FindProperty("cheerleaderText").objectReferenceValue = cheerleaderText;
            serializedController.FindProperty("fanSentimentText").objectReferenceValue = fanSentimentText;
            serializedController.ApplyModifiedProperties();

            EditorUtility.SetDirty(synergyController);

            return synergyController;
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

        private static Text FindOrCreateText(Transform parent, string name, string defaultText, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null && existingChild.TryGetComponent<Text>(out var existingText)) return existingText;

            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
            textObject.transform.SetParent(parent, false);

            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<Text>();
            text.text = defaultText;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }
    }
}
