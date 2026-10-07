using System.Linq;
using KBOManager.Engine;
using KBOManager.Simulation;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-GM-10] 씬 정리(idempotent, SetupMasterBinding 체인 마지막) - 모바일(Android) 터치 대응 RaycastTarget 정리(레거시 화면 포함)
    /// + Log5 엔진 · 레전드 우대 상수 로그.
    /// </summary>
    public static class SetupTaskGM10
    {
        [MenuItem("KBO Manager/Setup/Apply TASK-GM-10 (Log5 Engine + Mobile Raycast)")]
        public static void ApplyAll()
        {
            var graphics = Object.FindObjectsByType<Graphic>(FindObjectsInactive.Include).Where(g => g.gameObject.scene.IsValid()).ToList();
            int before = GMRaycastSanitizer.BlockingTexts(graphics);
            int changed = GMRaycastSanitizer.Sanitize(graphics);
            int after = GMRaycastSanitizer.BlockingTexts(graphics);
            foreach (var g in graphics) EditorUtility.SetDirty(g);
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[SetupTaskGM10] TASK-GM-10 점검 - RaycastTarget 정리 {changed}건(터치 가로채는 텍스트 {before} → {after}) · " +
                      $"Log5 리그 기준 타율 {GMLog5.LgAvg:.000} · BB {GMLog5.LgBB:.000} · K {GMLog5.LgK:.000} · HR/H {GMLog5.LgHRPerHit:.000} · 실효 전력 비 지수 {GMLog5.PowerRatioExponent} · " +
                      $"레전드 Ego 페널티 {TeamChemistryEngine.LegendEgoPenaltyScale:P0} - 씬을 저장(Ctrl+S)하십시오.");
        }
    }
}
