using KBOManager.Controllers;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-190] 씬 적용(idempotent).
    ///   1) SeasonRewardManager가 씬에 없어 시즌 결산(순위 · 재료 · 타이틀 보상)이 한 번도 지급되지 않던 문제 - SeasonRollover와 같은 매니저 오브젝트에
    ///      붙이고 LeagueManager · PostSeasonManager · SeasonStatManager · ScoutManager를 배선, SeasonRollover.seasonRewardManager 연결.
    ///   2) 로비 화면 루트 아래 시즌 완주 오버레이(SeasonCycleView - 정규시즌 종료 → 포스트시즌 · 시상식 → 다음 시즌 시작).
    /// 성장 센터 스킬 섹션 · 상점 7행 · 빠른 진행 6버튼은 각 뷰의 Build()가 만들고 SetupTask184 / 189 / SetupCompyaMatchUI179가 계층을 다시 만든다.
    /// </summary>
    public static class SetupTask190
    {
        public const string CycleObjectName = "SeasonCycleView190";

        [MenuItem("KBO Manager/Setup/Apply TASK-190 (Season Cycle + Skill Slots)")]
        public static void ApplyAll()
        {
            EnsureSeasonRewardManager();
            BuildSeasonCycleView();
            Debug.Log("[SetupTask190] TASK-190 적용 완료(SeasonRewardManager 배선 + 시즌 완주 오버레이) - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static void EnsureSeasonRewardManager()
        {
            var rollover = Object.FindAnyObjectByType<SeasonRollover>(FindObjectsInactive.Include);
            var league = Object.FindAnyObjectByType<LeagueManager>(FindObjectsInactive.Include);
            var host = rollover != null ? rollover.gameObject : league != null ? league.gameObject : null;
            if (host == null)
            {
                Debug.LogWarning("[SetupTask190] SeasonRollover/LeagueManager가 없어 SeasonRewardManager 배선을 건너뜁니다.");
                return;
            }
            var reward = Object.FindAnyObjectByType<SeasonRewardManager>(FindObjectsInactive.Include);
            if (reward == null) reward = Undo.AddComponent<SeasonRewardManager>(host);
            var so = new SerializedObject(reward);
            so.FindProperty("leagueManager").objectReferenceValue = league;
            so.FindProperty("postSeasonManager").objectReferenceValue = Object.FindAnyObjectByType<PostSeasonManager>(FindObjectsInactive.Include);
            so.FindProperty("seasonStatManager").objectReferenceValue = Object.FindAnyObjectByType<SeasonStatManager>(FindObjectsInactive.Include);
            so.FindProperty("scoutManager").objectReferenceValue = Object.FindAnyObjectByType<ScoutManager>(FindObjectsInactive.Include);
            so.ApplyModifiedProperties();
            MarkDirty(reward);

            if (rollover != null)
            {
                var rso = new SerializedObject(rollover);
                rso.FindProperty("seasonRewardManager").objectReferenceValue = reward;
                rso.ApplyModifiedProperties();
                MarkDirty(rollover);
            }
        }

        public static void BuildSeasonCycleView()
        {
            var lobby = LobbyRoot();
            if (lobby == null)
            {
                Debug.LogWarning("[SetupTask190] UIManager 로비 화면 루트를 찾지 못해 시즌 완주 오버레이를 건너뜁니다.");
                return;
            }
            var holder = lobby.transform.Find(CycleObjectName) as RectTransform;
            if (holder == null)
            {
                holder = new GameObject(CycleObjectName, typeof(RectTransform)).GetComponent<RectTransform>();
                Undo.RegisterCreatedObjectUndo(holder.gameObject, "Create " + CycleObjectName);
                holder.SetParent(lobby.transform, false);
            }
            holder.anchorMin = Vector2.zero;
            holder.anchorMax = Vector2.one;
            holder.offsetMin = holder.offsetMax = Vector2.zero;
            holder.SetAsLastSibling();
            if (!holder.TryGetComponent<SeasonCycleView>(out var view)) view = Undo.AddComponent<SeasonCycleView>(holder.gameObject);
            view.Configure(KBOFonts.Bold, KBOFonts.Medium);
            view.Build();
            holder.gameObject.SetActive(true);
            MarkDirty(view);
        }

        public static GameObject LobbyRoot()
        {
            var ui = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (ui == null) return null;
            var screens = new SerializedObject(ui).FindProperty("screens");
            for (int i = 0; i < screens.arraySize; i++)
            {
                var entry = screens.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("Type").enumValueIndex != (int)ScreenType.Lobby) continue;
                return entry.FindPropertyRelative("Root").objectReferenceValue as GameObject;
            }
            return null;
        }

        private static void MarkDirty(Component component)
        {
            if (component == null) return;
            EditorUtility.SetDirty(component);
            if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
