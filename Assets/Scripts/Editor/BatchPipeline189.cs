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
    /// [TASK-KBO-189] Unity CLI 1회 실행 파이프라인:
    ///   Unity.exe -batchmode -projectPath . -executeMethod KBOManager.EditorTools.BatchPipeline189.Run -logFile Logs/u189.log
    ///   1) 통합 Setup(Apply Latest UI TASK-168~189) + SampleScene 저장  2) 검증 리포트(Logs/Task189Report.txt)
    ///   3) EditMode NUnit 테스트 전체(동기 실행, 결과 Logs/editmode189.xml)  4) SampleScene을 다시 열어 저장 → 종료 코드(실패 0건 = 0).
    /// </summary>
    public static class BatchPipeline189
    {
        public static void Run()
        {
            int exitCode = 1;
            try
            {
                SetupMasterBinding.RunBatchApplyLatestUI();
                Task189Report.RunBatch();

                var callbacks = new Callbacks();
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.RegisterCallbacks(callbacks);
                api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }) { runSynchronously = true });
                api.UnregisterCallbacks(callbacks);

                bool sceneOk = SampleSceneGuard.EnsureOpen(save: true, forceReopen: true);
                Debug.Log($"[BatchPipeline189] EditMode {callbacks.Passed}/{callbacks.Total} 통과, 실패 {callbacks.Failed}, 건너뜀 {callbacks.Skipped} | " +
                          $"Task189Tests {callbacks.Task189Passed}/{callbacks.Task189Total} | SampleScene 열기·저장 {(sceneOk ? "성공" : "실패")}");
                exitCode = callbacks.Finished && callbacks.Failed == 0 && callbacks.Total > 0 && sceneOk ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[BatchPipeline189] 실패: " + e);
            }
            EditorApplication.Exit(exitCode);
        }

        private class Callbacks : ICallbacks
        {
            public int Total, Passed, Failed, Skipped, Task189Total, Task189Passed;
            public bool Finished;

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                Finished = true;
                Directory.CreateDirectory("Logs");
                TestRunnerApi.SaveResultToFile(result, Path.GetFullPath("Logs/editmode189.xml"));
                Passed = result.PassCount;
                Failed = result.FailCount;
                Skipped = result.SkipCount + result.InconclusiveCount;
                Total = Passed + Failed + Skipped;
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren) return;
                if (result.FullName.Contains("Task189Tests"))
                {
                    Task189Total++;
                    if (result.TestStatus == TestStatus.Passed) Task189Passed++;
                }
                if (result.TestStatus != TestStatus.Failed) return;
                Debug.LogError($"[BatchPipeline189] FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
            }
        }
    }
}
