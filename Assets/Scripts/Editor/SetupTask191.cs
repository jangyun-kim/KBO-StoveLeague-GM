using System.Collections.Generic;
using System.Linq;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-191] 씬 적용(idempotent) - 전 화면 한글 텍스트 정리.
    ///   1) 루트 캔버스마다 ReadableFontPass(정리 패스, 본문 서체 = KBO Dia Gothic Medium) - 팝업 · 오버레이 · 런타임 생성 행까지 덮는다.
    ///      화면 루트(SetupTask186)에 붙은 패스는 서체만 채운다(재스캔은 상위 캔버스 패스가 맡는다).
    ///   2) 씬에 저장된 모든 UI Text를 TextTidy.Normalize - FontStyle.Bold 해제 · TASK-186 일괄 확대(22/26pt) 복원 · 과대 크기 계층 축소 ·
    ///      Best Fit 최소 11pt · 18pt 이하 Bold 서체 → Medium · 과도한 Outline 축소 · 자간 2.0. PlayerCardUI 내부는 크기를 두고 굵기 · 자간만.
    /// 성장 센터 · 로비 · 선수 상세 · 경기 중계의 정확한 크기는 각 빌더(GrowthCenterView / SetupTask181 / PlayerDetailLayout186 / CompyaUiKit)가 정한다.
    /// </summary>
    public static class SetupTask191
    {
        [MenuItem("KBO Manager/Setup/Apply TASK-191 (Readable Korean Text)")]
        public static void ApplyAll()
        {
            int purged = PurgeLegacyDuplicates();
            int passes = EnsureCanvasPasses();
            int changed = TidyScene(out int total);
            Debug.Log($"[SetupTask191] TASK-191 적용 완료 - 보관함 중복 레이아웃 {purged}개 정리 · 루트 캔버스 정리 패스 {passes}개 · 씬 텍스트 {total}개 중 {changed}개 정리(Bold 해제 · 계층 크기 · 자간) - 씬을 저장(Ctrl+S)하십시오.");
        }

        /// <summary>
        /// SetupThemeUI178은 실행마다 패널의 기존 자식을 비활성 보관함(_Legacy178)으로 옮기는데, 그 뒤 TASK-181/190이 같은 이름의 루트(Layout181 ·
        /// Tutorial181 · SeasonCycleView190 등)를 새로 만들어 보관함에 옛 복사본이 남는다. FindAnyObjectByType이 숨은 복사본(구 LobbyHome181 ·
        /// SeasonCycleView)을 집어 오지 않도록, 보관함 자식 중 패널에 같은 이름의 현역 자식이 있는 것(= 중복)만 지운다.
        /// </summary>
        public static int PurgeLegacyDuplicates()
        {
            int purged = 0;
            var holders = Resources.FindObjectsOfTypeAll<Transform>()
                .Where(t => t != null && t.name == "_Legacy178" && t.parent != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t))
                .ToList();
            foreach (var holder in holders)
            {
                var panel = holder.parent;
                var live = new HashSet<string>(panel.Cast<Transform>().Where(c => c != holder).Select(c => c.name));
                foreach (var child in holder.Cast<Transform>().Where(c => live.Contains(c.name)).ToList())
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                    purged++;
                }
            }
            var scene = EditorSceneManager.GetActiveScene();
            if (purged > 0 && scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            return purged;
        }

        public static List<Canvas> RootCanvases() =>
            Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(c => c != null && c.gameObject.scene.IsValid() && (c.transform.parent == null || c.transform.parent.GetComponentInParent<Canvas>(true) == null))
                .ToList();

        public static int EnsureCanvasPasses()
        {
            var font = KBOFonts.Medium;
            int count = 0;
            foreach (var canvas in RootCanvases())
            {
                if (!canvas.TryGetComponent<ReadableFontPass>(out var pass)) pass = Undo.AddComponent<ReadableFontPass>(canvas.gameObject);
                pass.Configure(font);
                MarkDirty(pass);
                count++;
            }
            foreach (var pass in Object.FindObjectsByType<ReadableFontPass>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                pass.Configure(font);
                MarkDirty(pass);
            }
            return count;
        }

        /// <summary>열린 씬의 모든 UI Text 정리. 바뀐 개수를 돌려준다(두 번째 실행은 0).</summary>
        public static int TidyScene(out int total)
        {
            var font = KBOFonts.Medium;
            int changed = 0;
            total = 0;
            foreach (var text in SceneTexts())
            {
                total++;
                bool card = text.GetComponentInParent<PlayerCardUI>(true) != null;
                if (!TextTidy.Normalize(text, card, font)) continue;
                EditorUtility.SetDirty(text);
                changed++;
            }
            var scene = EditorSceneManager.GetActiveScene();
            if (changed > 0 && scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            return changed;
        }

        public static IEnumerable<Text> SceneTexts() =>
            Resources.FindObjectsOfTypeAll<Text>().Where(t => t != null && t.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(t));

        private static void MarkDirty(Component component)
        {
            if (component == null) return;
            EditorUtility.SetDirty(component);
            if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
