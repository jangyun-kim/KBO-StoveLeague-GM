using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KBOManager.Controllers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-186] Unity CLI 1회 실행 파이프라인:
    ///   Unity.exe -batchmode -projectPath . -executeMethod KBOManager.EditorTools.BatchPipeline186.Run -logFile Logs/u186.log
    ///   1) 통합 Setup(Apply Latest UI TASK-168~186) + SampleScene 저장  2) 검증 리포트(Logs/Task186Report.txt)
    ///   3) EditMode NUnit 테스트 전체(동기 실행, 결과 Logs/editmode186.xml)  4) SampleScene을 다시 열어 저장 → 종료 코드(실패 0건 = 0).
    /// </summary>
    public static class BatchPipeline186
    {
        public static void Run()
        {
            int exitCode = 1;
            try
            {
                SetupMasterBinding.RunBatchApplyLatestUI();
                Task186Report.RunBatch();

                var callbacks = new Callbacks();
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.RegisterCallbacks(callbacks);
                api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }) { runSynchronously = true });
                api.UnregisterCallbacks(callbacks);

                bool sceneOk = SampleSceneGuard.EnsureOpen(save: true, forceReopen: true);
                Debug.Log($"[BatchPipeline186] EditMode {callbacks.Passed}/{callbacks.Total} 통과, 실패 {callbacks.Failed}, 건너뜀 {callbacks.Skipped} | " +
                          $"Task186Tests {callbacks.Task186Passed}/{callbacks.Task186Total} | SampleScene 열기·저장 {(sceneOk ? "성공" : "실패")}");
                exitCode = callbacks.Finished && callbacks.Failed == 0 && callbacks.Total > 0 && sceneOk ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[BatchPipeline186] 실패: " + e);
            }
            EditorApplication.Exit(exitCode);
        }

        private class Callbacks : ICallbacks
        {
            public int Total, Passed, Failed, Skipped, Task186Total, Task186Passed;
            public bool Finished;

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                Finished = true;
                Directory.CreateDirectory("Logs");
                TestRunnerApi.SaveResultToFile(result, Path.GetFullPath("Logs/editmode186.xml"));
                Passed = result.PassCount;
                Failed = result.FailCount;
                Skipped = result.SkipCount + result.InconclusiveCount;
                Total = Passed + Failed + Skipped;
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren) return;
                if (result.FullName.Contains("Task186Tests"))
                {
                    Task186Total++;
                    if (result.TestStatus == TestStatus.Passed) Task186Passed++;
                }
                if (result.TestStatus != TestStatus.Failed) return;
                Debug.LogError($"[BatchPipeline186] FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
            }
        }
    }

    /// <summary>[TASK-KBO-186] 완료 보고용 검증 리포트(Logs/Task186Report.txt). 씬은 읽기만 한다.</summary>
    public static class Task186Report
    {
        [MenuItem("KBO Manager/Debug/TASK-186 Report")]
        public static void RunMenu() => Debug.Log(Build());

        public static void RunBatch()
        {
            string report = Build();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/Task186Report.txt", report, Encoding.UTF8);
            Debug.Log(report);
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (; t != null; t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        public static string Build()
        {
            var sb = new StringBuilder();
            BuildDetail(sb);
            BuildResult(sb);
            BuildFonts(sb);
            BuildTempo(sb);
            return sb.ToString();
        }

        private static void BuildDetail(StringBuilder sb)
        {
            sb.AppendLine("=== [1] 선수 상세정보 4단 레이아웃 ===");
            var detail = UnityEngine.Object.FindAnyObjectByType<PlayerDetailUIController>(FindObjectsInactive.Include);
            var root = detail != null ? new SerializedObject(detail).FindProperty("panelRoot").objectReferenceValue as GameObject : null;
            var layout = root != null ? root.transform.Find(PlayerDetailLayout186.RootName) : null;
            if (layout == null) { sb.AppendLine("Detail186 없음"); sb.AppendLine(); return; }
            var legacyActive = root.transform.Cast<Transform>().Where(t => t != layout && t.gameObject.activeSelf).Select(t => t.name).ToList();
            sb.AppendLine($"레이아웃: {PathOf(layout)} | 구 탭형 자식 활성 {(legacyActive.Count == 0 ? "없음(전부 숨김)" : string.Join(", ", legacyActive))}");
            var texts = layout.GetComponentsInChildren<Text>(true);
            sb.AppendLine($"텍스트 {texts.Length}개 · 최소 최대크기 {texts.Min(t => t.resizeTextMaxSize)}pt · 자동 크기 최소 {texts.Max(t => t.resizeTextMinSize)}pt 이하 · 세로 잘림(겹침 방지) {texts.Count(t => t.verticalOverflow == VerticalWrapMode.Truncate)}/{texts.Length}");
            foreach (var name in new[] { "Title", "Subtitle", "Ovr", "OvrBreakdown", "SdText", "StatName0", "StatValue0", "StatSub0", "SkillName0", "SkillDesc0" })
            {
                var t = layout.Find(name)?.GetComponent<Text>();
                sb.AppendLine($"  {name,-13} {(t != null ? $"{t.fontSize}pt{(t.fontStyle == FontStyle.Bold ? " Bold" : "")}" : "없음")}");
            }
            // 같은 좌표에 겹친 텍스트(유령 텍스트) 검사 - 텍스트 사각형끼리 50% 이상 겹치면 보고
            var rects = texts.Select(t => (t.name, r: AnchorBox((RectTransform)t.transform))).Where(x => x.r.width > 0).ToList();
            int overlaps = 0;
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                {
                    var a = rects[i].r; var b = rects[j].r;
                    float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin), h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
                    if (w > 0 && h > 0 && w * h > 0.5f * Mathf.Min(a.width * a.height, b.width * b.height)) overlaps++;
                }
            sb.AppendLine($"서로 겹친 텍스트 쌍: {overlaps}");
            sb.AppendLine();
        }

        /// <summary>Detail186 루트 기준 정규화 앵커 박스(직속 자식 기준 - 버튼 라벨 같은 손자는 부모 박스).</summary>
        private static Rect AnchorBox(RectTransform rect)
        {
            if (rect.parent != null && rect.parent.name != PlayerDetailLayout186.RootName) return new Rect(0, 0, 0, 0);
            return Rect.MinMaxRect(rect.anchorMin.x, rect.anchorMin.y, rect.anchorMax.x, rect.anchorMax.y);
        }

        private static void BuildResult(StringBuilder sb)
        {
            sb.AppendLine("=== [2] 경기 결과 화면 좌우 열 · R/H/E/B ===");
            var view = UnityEngine.Object.FindAnyObjectByType<CompyaMatchView>(FindObjectsInactive.Include);
            var result = view != null ? view.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Result1") : null;
            if (result == null) { sb.AppendLine("Result1 없음"); sb.AppendLine(); return; }
            float Center(string n) { var r = result.Find(n) as RectTransform; return r != null ? (r.anchorMin.x + r.anchorMax.x) / 2f : -1f; }
            sb.AppendLine($"좌측 열(AWAY): 점수 카드 {Center("AwayCard"):0.00} · 투수 카드 {Center("AwayPitcherPanel"):0.00} · 명판 {Center("AwayNameBar"):0.00}");
            sb.AppendLine($"우측 열(HOME): 점수 카드 {Center("HomeCard"):0.00} · 투수 카드 {Center("HomePitcherPanel"):0.00} · 명판 {Center("HomeNameBar"):0.00}");
            float Width(string n) { var r = result.Find(n) as RectTransform; return r != null ? (r.anchorMax.x - r.anchorMin.x) * CompyaUiKit.RefWidth : 0f; }
            sb.AppendLine($"R/H/E/B 칸 폭 {Width("AR0"):0}px (기존 55px) · 자동 크기 최소 {result.Find("AR0")?.GetComponent<Text>()?.resizeTextMinSize}pt");
            var awayName = result.Find("AwayPitcherName") as RectTransform; var awayRec = result.Find("AwayRecord") as RectTransform;
            var homeName = result.Find("HomePitcherName") as RectTransform; var homeRec = result.Find("HomeRecord") as RectTransform;
            bool sep = awayName != null && awayRec != null && homeName != null && homeRec != null &&
                       awayName.anchorMax.x <= awayRec.anchorMin.x && awayRec.anchorMax.x <= homeRec.anchorMin.x && homeRec.anchorMax.x <= homeName.anchorMin.x;
            sb.AppendLine($"투수 명판 이름/기록 칸 분리(겹침 없음): {(sep ? "OK" : "실패")} · 표기 예: {ResultColumns.SeasonRecord(1, 0)}");
            sb.AppendLine($"승패 예(AWAY 4 : HOME 25 → verdict 1): 좌 막대 {(ResultColumns.LeftWins(1) ? "파랑" : "회색")} / 우 막대 {(ResultColumns.RightWins(1) ? "파랑" : "회색")}");
            sb.AppendLine();
        }

        private static void BuildFonts(StringBuilder sb)
        {
            sb.AppendLine("=== [3] 가독성 패스(ReadableFontPass) ===");
            foreach (var root in SetupTask186.FontPassRoots())
            {
                var texts = root.GetComponentsInChildren<Text>(true).Where(t => t.GetComponentInParent<PlayerCardUI>(true) == null).ToList();
                int small = texts.Count(t => ReadableFontPass.EffectiveSize(t) < ReadableFontPass.Floor);
                sb.AppendLine($"{root.name,-28} 패스 {(root.GetComponent<ReadableFontPass>() != null ? "부착" : "없음")} · 씬 텍스트 {texts.Count}개 중 20pt 미만 {small}개");
            }
            sb.AppendLine();
        }

        private static void BuildTempo(StringBuilder sb)
        {
            sb.AppendLine("=== [4] 경기 템포 ===");
            sb.AppendLine($"배율 {MatchTempo.Scale:0.00}(-{(1f - MatchTempo.Scale) * 100f:0}%) | 하이라이트 타석 간 0.50→{MatchTempo.Scaled(0.5f):0.00}초 · 전체 중계 0.80→{MatchTempo.Scaled(0.8f):0.00}초 · " +
                          $"타구 궤적 {MatchTempo.LegacyArcSeconds:0.00}→{MatchTempo.ArcSeconds:0.00}초 · 이닝 교대 1.40→{MatchTempo.Scaled(1.4f):0.00}초 · 직접 플레이 결과 1.60→{MatchTempo.Scaled(1.6f):0.00}초");
        }
    }
}
