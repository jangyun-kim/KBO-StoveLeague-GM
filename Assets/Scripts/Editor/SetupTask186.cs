using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-186] 씬 적용(idempotent).
    ///   1) 선수 상세정보(PlayerDetailUIController) - KBO Dia Gothic · SkillDB 주입 후 4단 카드 레이아웃(Detail186)을 빌드하고 구 탭형 자식을 숨긴다.
    ///   2) 전 화면 가독성 - 로비 · 경기 중계 · 라인업 · 스카우트 · 선수 관리 화면 루트에 ReadableFontPass(20pt 미만 → 22/26pt, Best Fit 최소 18)를
    ///      붙이고 씬에 저장된 텍스트에도 한 번 적용한다(코드로 다시 만드는 화면은 런타임 패스가 맡는다).
    /// 경기 결과 화면(좌우 열 통일 · R/H/E/B 폭 · 투수 명판)과 경기 템포는 CompyaMatchView 코드 빌드라 SetupCompyaMatchUI179가 다시 만든다.
    /// </summary>
    public static class SetupTask186
    {
        public static readonly ScreenType[] FontPassScreens =
        {
            ScreenType.Lobby, ScreenType.InGame, ScreenType.Roster, ScreenType.Scout, ScreenType.PlayerManagementHub,
        };

        [MenuItem("KBO Manager/Setup/Apply TASK-186 (Player Detail 4-Tier + Readable Fonts)")]
        public static void ApplyAll()
        {
            bool detail = BuildPlayerDetail();
            int texts = ApplyReadableFonts(out int roots);
            Debug.Log($"[SetupTask186] TASK-186 적용 완료(상세정보 4단 레이아웃 {(detail ? "빌드" : "건너뜀")} · 가독성 패스 {roots}개 화면 / 씬 텍스트 {texts}개 상향) - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static bool BuildPlayerDetail()
        {
            var detail = Object.FindAnyObjectByType<PlayerDetailUIController>(FindObjectsInactive.Include);
            if (detail == null)
            {
                Debug.LogWarning("[SetupTask186] PlayerDetailUIController가 없어 상세정보 재구축을 건너뜁니다.");
                return false;
            }
            Undo.RecordObject(detail, "TASK-186 Player Detail");
            detail.Configure(KBOFonts.Bold, KBOFonts.Medium, FindSkillDB());
            detail.BuildLayout();
            MarkDirty(detail);
            return detail.Layout != null;
        }

        private static SkillDB FindSkillDB()
        {
            var roster = Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            var fromRoster = roster != null ? new SerializedObject(roster).FindProperty("skillDB")?.objectReferenceValue as SkillDB : null;
            if (fromRoster != null) return fromRoster;
            var guid = AssetDatabase.FindAssets("t:SkillDB").FirstOrDefault();
            return guid != null ? AssetDatabase.LoadAssetAtPath<SkillDB>(AssetDatabase.GUIDToAssetPath(guid)) : null;
        }

        public static List<GameObject> FontPassRoots()
        {
            var roots = new List<GameObject>();
            var ui = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (ui == null) return roots;
            var screens = new SerializedObject(ui).FindProperty("screens");
            for (int i = 0; i < screens.arraySize; i++)
            {
                var entry = screens.GetArrayElementAtIndex(i);
                var type = (ScreenType)entry.FindPropertyRelative("Type").enumValueIndex;
                var root = entry.FindPropertyRelative("Root").objectReferenceValue as GameObject;
                if (root != null && FontPassScreens.Contains(type) && !roots.Contains(root)) roots.Add(root);
            }
            return roots;
        }

        public static int ApplyReadableFonts(out int rootCount)
        {
            int changed = 0;
            var roots = FontPassRoots();
            foreach (var root in roots)
            {
                if (root.GetComponent<ReadableFontPass>() == null) Undo.AddComponent<ReadableFontPass>(root);
                foreach (var text in root.GetComponentsInChildren<UnityEngine.UI.Text>(true)) Undo.RecordObject(text, "TASK-186 Readable Fonts");
                changed += ReadableFontPass.Apply(root.transform);
                MarkDirty(root.transform);
            }
            rootCount = roots.Count;
            return changed;
        }

        private static void MarkDirty(Component component)
        {
            if (component == null) return;
            EditorUtility.SetDirty(component);
            if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
