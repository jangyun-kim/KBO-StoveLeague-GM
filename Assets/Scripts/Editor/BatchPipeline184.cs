using System;
using System.IO;
using System.Linq;
using System.Text;
using KBOManager.Controllers;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-184] Unity CLI 1회 실행 파이프라인:
    ///   Unity.exe -batchmode -projectPath . -executeMethod KBOManager.EditorTools.BatchPipeline184.Run -logFile Logs/u184.log
    ///   1) 통합 Setup(Apply Latest UI TASK-168~184) + SampleScene 저장  2) 검증 리포트(Logs/Task184Report.txt)
    ///   3) EditMode NUnit 테스트 전체(동기 실행, 결과 Logs/editmode184.xml)  4) SampleScene을 다시 열어 저장 → 종료 코드(실패 0건 = 0).
    /// -quit 대신 끝에서 EditorApplication.Exit()로 직접 종료한다(테스트 실행 후 종료 코드를 돌려주기 위해).
    /// </summary>
    public static class BatchPipeline184
    {
        public static void Run()
        {
            int exitCode = 1;
            try
            {
                SetupMasterBinding.RunBatchApplyLatestUI();
                Task184Report.RunBatch();

                var callbacks = new Callbacks();
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.RegisterCallbacks(callbacks);
                api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }) { runSynchronously = true });
                api.UnregisterCallbacks(callbacks);

                bool sceneOk = SampleSceneGuard.EnsureOpen(save: true, forceReopen: true);
                Debug.Log($"[BatchPipeline184] EditMode {callbacks.Passed}/{callbacks.Total} 통과, 실패 {callbacks.Failed}, 건너뜀 {callbacks.Skipped} | " +
                          $"SampleScene 열기·저장 {(sceneOk ? "성공" : "실패")}");
                exitCode = callbacks.Finished && callbacks.Failed == 0 && callbacks.Total > 0 && sceneOk ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[BatchPipeline184] 실패: " + e);
            }
            EditorApplication.Exit(exitCode);
        }

        private class Callbacks : ICallbacks
        {
            public int Total, Passed, Failed, Skipped;
            public bool Finished;

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                Finished = true;
                Directory.CreateDirectory("Logs");
                TestRunnerApi.SaveResultToFile(result, Path.GetFullPath("Logs/editmode184.xml"));
                Passed = result.PassCount;
                Failed = result.FailCount;
                Skipped = result.SkipCount + result.InconclusiveCount;
                Total = Passed + Failed + Skipped;
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren || result.TestStatus != TestStatus.Failed) return;
                Debug.LogError($"[BatchPipeline184] FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
            }
        }
    }

    /// <summary>[TASK-KBO-184] 완료 보고용 검증 리포트(Logs/Task184Report.txt).</summary>
    public static class Task184Report
    {
        [MenuItem("KBO Manager/Debug/TASK-184 Report")]
        public static void RunMenu() => Debug.Log(Build());

        public static void RunBatch()
        {
            string report = Build();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/Task184Report.txt", report, Encoding.UTF8);
            Debug.Log(report);
        }

        public static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== [1] 씬 배선 ===");
            var hub = UnityEngine.Object.FindAnyObjectByType<PlayerManagementUIController>(FindObjectsInactive.Include);
            var view = hub != null ? hub.GetComponent<GrowthCenterView>() : null;
            sb.AppendLine($"선수 관리 허브: {(hub != null ? hub.name : "없음")} / GrowthCenterView: {(view != null ? "부착" : "없음")} / " +
                          $"루트 {(view != null && view.transform.Find(GrowthCenterView.RootName) != null ? GrowthCenterView.RootName : "없음")} / " +
                          $"growthCenter 바인딩 {(hub != null && new SerializedObject(hub).FindProperty("growthCenter").objectReferenceValue != null)}");
            var roster = UnityEngine.Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            if (roster != null)
            {
                var so = new SerializedObject(roster);
                var names = new[] { "storageScopeButton", "storageTeamButton", "storagePositionButton", "storageGradeButton", "storageSortButton" };
                sb.AppendLine("보관 선수 필터 바인딩: " + string.Join(", ", names.Select(n => $"{n}={(so.FindProperty(n).objectReferenceValue != null ? "OK" : "없음")}")));
            }

            sb.AppendLine();
            sb.AppendLine("=== [2] 1경기 진행 시 10개 구단 동시 진행(타 구장 4경기) ===");
            var go = new GameObject("Task184ReportLeague");
            try
            {
                var league = go.AddComponent<LeagueManager>();
                league.InitializeLeague(Team.Samsung, LeagueTier.Amateur);
                league.SetTeamRoster(Team.Samsung, Task183Report.ProceduralTeam(Team.Samsung, 61, "R"));
                for (int g = 1; g <= 3; g++)
                {
                    var f = league.PlayNextMatch();
                    sb.AppendLine($"[{g}경기] 우리 경기 {f.AwayTeam} {f.Result.AwayTotalScore} : {f.Result.HomeTotalScore} {f.HomeTeam} | 타 구장 " +
                                  string.Join(", ", league.LastRoundOtherFixtures.Select(o => $"{o.AwayTeam} {o.Result.AwayTotalScore} : {o.Result.HomeTotalScore} {o.HomeTeam}")));
                }
                sb.AppendLine("순위표(3경기 후):");
                int rank = 1;
                foreach (var t in league.GetStandings())
                    sb.AppendLine($"  {rank++,2}위 {t.Team,-8} {t.Wins}승 {t.Draws}무 {t.Losses}패 ({t.Wins + t.Draws + t.Losses}경기) 승률 {t.WinRate:F3} 게임차 {league.GamesBehind(t.Team):0.#}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                Task183Report.CleanupTemp();
            }

            sb.AppendLine();
            sb.AppendLine("=== [3] 구단 OVR = 라인업 28인 최종 OVR 평균 / 각성 임계점 성장표 ===");
            var dbGo = new GameObject("Task184ReportDb");
            var rosterGo = new GameObject("Task184ReportRoster");
            try
            {
                var db = dbGo.AddComponent<PlayerDatabase>();
                var rosterManager = rosterGo.AddComponent<RosterManager>();
                foreach (Team team in Enum.GetValues(typeof(Team)))
                {
                    if (team == Team.None) continue;
                    var none = Task183Report.MeasureOnboarding(db.AllTemplates, db, rosterManager, team, null);
                    var gifts = Task183Report.GiftIds.Select(id => Task183Report.MeasureOnboarding(db.AllTemplates, db, rosterManager, team, id).teamOvr).ToList();
                    sb.AppendLine($"온보딩 {team,-8} 28인 평균 {none.rosterAvg:F2} + 세트덱 {none.score}P(+{none.setDeckOvr}) → 구단 OVR {none.teamOvr} | " +
                                  $"선물 4종 {string.Join("/", gifts)} → {LeagueTierTable.DisplayName(LeagueTierTable.RecommendedFor(none.teamOvr))}");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(rosterGo);
                UnityEngine.Object.DestroyImmediate(dbGo);
            }
            sb.AppendLine("등급            기본 OVR  강화 돌파 특훈 각성 합계 | 각성 단계별 OVR(명함~초월)");
            foreach (var grade in new[] { Grade.LIVE_NORMAL, Grade.LIVE_EPIC, Grade.ALLSTAR, Grade.FRANCHISE, Grade.TITLE_HOLDER, Grade.GOLDEN_GLOVE, Grade.SIGNATURE })
            {
                var band = CardGrowthRules.BaseOvrRange(grade);
                int max = CardGrowthRules.MaxAwakenLevelFor(grade);
                var steps = Enumerable.Range(0, max + 1).Select(l => CardGrowthRules.AwakenGrowthFor(grade, l));
                sb.AppendLine($"{grade,-15} {band.Min}~{band.Max}   {CardGrowthRules.MaxReinforceGrowth,3} {CardGrowthRules.LimitBreakCap(grade),4} {CardGrowthRules.TrainingCap(grade),4} " +
                              $"{CardGrowthRules.AwakenGrowthCap(grade),4} {CardGrowthRules.MaxTotalGrowth(grade),4} | {string.Join(" ", steps)} | " +
                              $"최종 {CardGrowthRules.MaxPotentialOvr(grade, band.Min)}~{CardGrowthRules.MaxPotentialOvr(grade, band.Max)}");
            }
            foreach (int level in new[] { 95, 102 })
            {
                var team = Task183Report.ProceduralTeam(Team.Samsung, level, "H");
                foreach (var p in team) { p.Template.Grade = Grade.SIGNATURE; p.ReinforceLevel = 10; p.LimitBreakLevel = 3; p.TrainingLevel = 3; p.AwakenLevel = 10; }
                int ovr = TeamOvrCalculator.AverageFinalOvr(team, TeamSynergyRules.StaticSynergyOvr(SetDeckBuffTable.FinalGoalScore));
                sb.AppendLine($"종결 라인업(SIG 기본 ≈{level}, 풀성장, 세트덱 200P) 구단 OVR {ovr} → {LeagueTierTable.DisplayName(LeagueTierTable.RecommendedFor(ovr))}");
                Task183Report.CleanupTemp();
            }
            return sb.ToString();
        }
    }
}
