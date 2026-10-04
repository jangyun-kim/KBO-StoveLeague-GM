using System.Linq;
using KBOManager.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-192] 씬 적용(idempotent) - 너무 작아진 일반 UI 글씨 복원 + 비대한 텍스트 핀셋 축소.
    ///   1) 크기 기준은 TextTidy(계층 상향 · Best Fit 최소 15 · TASK-191 크기 1회 이전)와 각 빌더(GrowthCenterView · SetupTask181 ·
    ///      PlayerDetailLayout186 · CompyaMatchView SELECT TYPE · PlayerCardUI)가 정한다 - 빌더는 앞선 Setup 단계가 같은 Build()로 다시 만든다.
    ///   2) 여기서는 남은 씬 텍스트를 한 번 더 정리(SetupTask191.TidyScene → TextTidy.Migrate191)하고 결과를 집계해 로그로 남긴다.
    /// </summary>
    public static class SetupTask192
    {
        [MenuItem("KBO Manager/Setup/Apply TASK-192 (Readable Size Restore)")]
        public static void ApplyAll()
        {
            int changed = SetupTask191.TidyScene(out int total);
            var texts = SetupTask191.SceneTexts().Where(t => t.GetComponentInParent<PlayerCardUI>(true) == null).ToList();
            int stale = texts.Count(t => t.TryGetComponent<TextTidy>(out var tidy) && tidy.Version < TextTidy.CurrentVersion);
            int bold = texts.Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic);
            int lowMin = texts.Count(t => t.resizeTextForBestFit && t.resizeTextMinSize < Mathf.Min(TextTidy.EffectiveSize(t), TextTidy.AutoMin));
            string Bucket(int lo, int hi) => texts.Count(t => TextTidy.EffectiveSize(t) >= lo && TextTidy.EffectiveSize(t) <= hi).ToString();
            Debug.Log($"[SetupTask192] TASK-192 적용 완료 - 씬 텍스트 {total}개 중 {changed}개 정리 · TASK-191 크기 잔여 {stale}개 · Bold {bold}개 · " +
                      $"Best Fit 최소 15 미만(표 칸 등) {lowMin}개 | 크기 분포 ≤15: {Bucket(0, 15)} · 16~18: {Bucket(16, 18)} · 19~22: {Bucket(19, 22)} · " +
                      $"23~30: {Bucket(23, 30)} · 31~42: {Bucket(31, 42)} - 씬을 저장(Ctrl+S)하십시오.");
        }
    }
}
