using System.Linq;
using KBOManager.Controllers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-193] 씬 적용(idempotent).
    ///   1) 스카우트 허브 상단 4탭 명도 대비(비활성 #1E293B + #CBD5E1 18pt / 활성 #2563EB + 흰색 20pt) - ScoutHubUIController.ApplyTabStyle.
    ///   2) [선수 스카우트] · [응원단 영입] 섹션 텍스트 상한(본문 20 · 버튼 22pt) + Best Fit 세로 Truncate - 카드(PlayerCardUI) 안쪽은 제외.
    ///      [특별 영입] · [상점 · 교환소]는 각 View의 Build()가 목표 pt로 직접 고정한다(SetupTask185 / SetupTask189가 다시 만든다).
    ///   3) 로비 위 스토브리그 오버레이(StoveLeagueView) · 리그 기록실 화면(LeagueRecordsView - LeagueStats 화면 루트 위).
    ///   중계 화면(CompyaMatchView) 헤더 라벨 16pt · 시즌 행 한 줄 · 반복과제 미니 바 숨김은 SetupCompyaMatchUI179가 같은 Build()로 만든다.
    /// </summary>
    public static class SetupTask193
    {
        public const string StoveObjectName = "StoveLeagueView193";
        public const string RecordsObjectName = "LeagueRecordsView193";
        public const int SectionBodyMax = 20, SectionButtonMax = 22;

        [MenuItem("KBO Manager/Setup/Apply TASK-193 (Scout Text Fix + Stove League + League Records)")]
        public static void ApplyAll()
        {
            int clamped = FixScoutHub();
            bool stove = BuildStoveLeague();
            bool records = BuildLeagueRecords();
            // 새로 고정한 작은 글씨(≤18pt)의 Bold 서체 파일 → Medium 교체 등 TextTidy 정리를 한 번 더 - 저장된 씬에서 다시 돌려도 0건(멱등)이 되게.
            int tidied = SetupTask191.TidyScene(out _);
            Debug.Log($"[SetupTask193] TASK-193 적용 완료 - 스카우트 탭 명도 대비 · 섹션 텍스트 상한 {clamped}개 · 스토브리그 {(stove ? "생성" : "건너뜀")} · " +
                      $"리그 기록실 {(records ? "생성" : "건너뜀")} · 정리 {tidied}개 - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static int FixScoutHub()
        {
            var hub = Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            if (hub == null) return 0;
            hub.ApplyTabStyle(0);
            var so = new SerializedObject(hub);
            int changed = 0;
            foreach (var field in new[] { "playerSection", "cheerleaderSection" })
            {
                if (!(so.FindProperty(field).objectReferenceValue is GameObject section)) continue;
                changed += TextFit193.ClampAll(section.transform, SectionBodyMax, t => TextFit193.InsideCard(t) || TextTidy.IsInButton(t));
                changed += TextFit193.ClampAll(section.transform, SectionButtonMax, t => TextFit193.InsideCard(t) || !TextTidy.IsInButton(t));
                foreach (var t in section.GetComponentsInChildren<Text>(true)) EditorUtility.SetDirty(t);
            }
            foreach (var b in new[] { "playerTabButton", "specialTabButton", "cheerleaderTabButton", "shopTabButton" })
            {
                if (!(so.FindProperty(b).objectReferenceValue is Button tab)) continue;
                EditorUtility.SetDirty(tab);
                if (tab.targetGraphic != null) EditorUtility.SetDirty(tab.targetGraphic);
                foreach (var t in tab.GetComponentsInChildren<Text>(true)) EditorUtility.SetDirty(t);
            }
            MarkDirty(hub);
            return changed;
        }

        public static bool BuildStoveLeague()
        {
            var lobby = SetupTask190.LobbyRoot();
            if (lobby == null)
            {
                Debug.LogWarning("[SetupTask193] 로비 화면 루트를 찾지 못해 스토브리그 오버레이를 건너뜁니다.");
                return false;
            }
            var holder = Holder(lobby.transform, StoveObjectName);
            if (!holder.TryGetComponent<StoveLeagueView>(out var view)) view = Undo.AddComponent<StoveLeagueView>(holder.gameObject);
            view.Configure(KBOFonts.Bold, KBOFonts.Medium);
            view.Build();
            holder.gameObject.SetActive(true);
            MarkDirty(view);
            return true;
        }

        public static bool BuildLeagueRecords()
        {
            var stats = Object.FindAnyObjectByType<LeagueStatsUIController>(FindObjectsInactive.Include);
            if (stats == null)
            {
                Debug.LogWarning("[SetupTask193] LeagueStatsUIController(리그 기록실 화면)가 없어 기록실을 건너뜁니다.");
                return false;
            }
            var holder = Holder(stats.transform, RecordsObjectName);
            if (!holder.TryGetComponent<LeagueRecordsView>(out var view)) view = Undo.AddComponent<LeagueRecordsView>(holder.gameObject);
            view.Configure(KBOFonts.Bold, KBOFonts.Medium);
            view.Build();
            holder.gameObject.SetActive(true);
            MarkDirty(view);
            return true;
        }

        private static RectTransform Holder(Transform parent, string name)
        {
            var holder = parent.Find(name) as RectTransform;
            if (holder == null)
            {
                holder = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                Undo.RegisterCreatedObjectUndo(holder.gameObject, "Create " + name);
                holder.SetParent(parent, false);
            }
            holder.anchorMin = Vector2.zero;
            holder.anchorMax = Vector2.one;
            holder.offsetMin = holder.offsetMax = Vector2.zero;
            holder.SetAsLastSibling();
            return holder;
        }

        private static void MarkDirty(Component component)
        {
            if (component == null) return;
            EditorUtility.SetDirty(component);
            if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
