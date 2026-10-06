using System;
using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-GM-05] Unity CLI 1회 실행 파이프라인:
    ///   Unity.exe -batchmode -projectPath . -executeMethod KBOManager.EditorTools.BatchPipelineGM05.Run -logFile Logs/uGM05.log
    ///   1) 통합 Setup(Apply Latest UI TASK-168~193 + GM-01~05) + SampleScene 저장  2) (SetupTaskGM05 로그가 치어리더 엔트리 화면 조립 결과를 남긴다)
    ///   3) EditMode NUnit 테스트 전체(동기 실행, 결과 Logs/editmodeGM05.xml)  4) SampleScene을 다시 열어 저장 → 종료 코드(실패 0건 = 0).
    /// </summary>
    public static class BatchPipelineGM05
    {
        public static void Run()
        {
            int exitCode = 1;
            try
            {
                SetupMasterBinding.RunBatchApplyLatestUI();

                var callbacks = new Callbacks();
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.RegisterCallbacks(callbacks);
                api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }) { runSynchronously = true });
                api.UnregisterCallbacks(callbacks);

                bool sceneOk = SampleSceneGuard.EnsureOpen(save: true, forceReopen: true);
                Debug.Log($"[BatchPipelineGM05] EditMode {callbacks.Passed}/{callbacks.Total} 통과, 실패 {callbacks.Failed}, 건너뜀 {callbacks.Skipped} | " +
                          $"GM05VerificationRunner {callbacks.GM05Passed}/{callbacks.GM05Total} | SampleScene 열기·저장 {(sceneOk ? "성공" : "실패")}");
                exitCode = callbacks.Finished && callbacks.Failed == 0 && callbacks.Total > 0 && sceneOk ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[BatchPipelineGM05] 실패: " + e);
            }
            EditorApplication.Exit(exitCode);
        }

        private class Callbacks : ICallbacks
        {
            public int Total, Passed, Failed, Skipped, GM05Total, GM05Passed;
            public bool Finished;

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                Finished = true;
                Directory.CreateDirectory("Logs");
                TestRunnerApi.SaveResultToFile(result, Path.GetFullPath("Logs/editmodeGM05.xml"));
                Passed = result.PassCount;
                Failed = result.FailCount;
                Skipped = result.SkipCount + result.InconclusiveCount;
                Total = Passed + Failed + Skipped;
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren) return;
                if (result.FullName.Contains("GM05VerificationRunner"))
                {
                    GM05Total++;
                    if (result.TestStatus == TestStatus.Passed) GM05Passed++;
                    Debug.Log($"[GM05VerificationRunner] {(result.TestStatus == TestStatus.Passed ? "PASS" : result.TestStatus.ToString().ToUpperInvariant())} {result.Name}");
                }
                if (result.TestStatus != TestStatus.Failed) return;
                Debug.LogError($"[BatchPipelineGM05] FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
            }
        }
    }
}
