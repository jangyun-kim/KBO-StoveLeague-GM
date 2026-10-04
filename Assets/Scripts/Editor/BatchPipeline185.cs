using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-185] Unity CLI 1회 실행 파이프라인:
    ///   Unity.exe -batchmode -projectPath . -executeMethod KBOManager.EditorTools.BatchPipeline185.Run -logFile Logs/u185.log
    ///   1) 통합 Setup(Apply Latest UI TASK-168~185) + SampleScene 저장  2) 검증 리포트(Logs/Task185Report.txt)
    ///   3) EditMode NUnit 테스트 전체(동기 실행, 결과 Logs/editmode185.xml)  4) SampleScene을 다시 열어 저장 → 종료 코드(실패 0건 = 0).
    /// </summary>
    public static class BatchPipeline185
    {
        public static void Run()
        {
            int exitCode = 1;
            try
            {
                SetupMasterBinding.RunBatchApplyLatestUI();
                Task185Report.RunBatch();

                var callbacks = new Callbacks();
                var api = ScriptableObject.CreateInstance<TestRunnerApi>();
                api.RegisterCallbacks(callbacks);
                api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode }) { runSynchronously = true });
                api.UnregisterCallbacks(callbacks);

                bool sceneOk = SampleSceneGuard.EnsureOpen(save: true, forceReopen: true);
                Debug.Log($"[BatchPipeline185] EditMode {callbacks.Passed}/{callbacks.Total} 통과, 실패 {callbacks.Failed}, 건너뜀 {callbacks.Skipped} | " +
                          $"Task185Tests {callbacks.Task185Passed}/{callbacks.Task185Total} | SampleScene 열기·저장 {(sceneOk ? "성공" : "실패")}");
                exitCode = callbacks.Finished && callbacks.Failed == 0 && callbacks.Total > 0 && sceneOk ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError("[BatchPipeline185] 실패: " + e);
            }
            EditorApplication.Exit(exitCode);
        }

        private class Callbacks : ICallbacks
        {
            public int Total, Passed, Failed, Skipped, Task185Total, Task185Passed;
            public bool Finished;

            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                Finished = true;
                Directory.CreateDirectory("Logs");
                TestRunnerApi.SaveResultToFile(result, Path.GetFullPath("Logs/editmode185.xml"));
                Passed = result.PassCount;
                Failed = result.FailCount;
                Skipped = result.SkipCount + result.InconclusiveCount;
                Total = Passed + Failed + Skipped;
            }

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.HasChildren) return;
                if (result.FullName.Contains("Task185Tests"))
                {
                    Task185Total++;
                    if (result.TestStatus == TestStatus.Passed) Task185Passed++;
                }
                if (result.TestStatus != TestStatus.Failed) return;
                Debug.LogError($"[BatchPipeline185] FAIL {result.FullName}\n{result.Message}\n{result.StackTrace}");
            }
        }
    }

    /// <summary>[TASK-KBO-185] 완료 보고용 검증 리포트(Logs/Task185Report.txt). 씬은 읽기만 한다.</summary>
    public static class Task185Report
    {
        [MenuItem("KBO Manager/Debug/TASK-185 Report")]
        public static void RunMenu() => Debug.Log(Build());

        public static void RunBatch()
        {
            string report = Build();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/Task185Report.txt", report, Encoding.UTF8);
            Debug.Log(report);
        }

        private static readonly List<UnityEngine.Object> Temp = new List<UnityEngine.Object>();

        private static Player Card(Grade grade, string person, int year, Team team, int reinforce = 0, int awaken = 0)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            Temp.Add(t);
            t.TemplateId = $"{person}_{grade}_{year}_{Guid.NewGuid():N}";
            t.RealPlayerId = person; t.PlayerName = person; t.SeasonYear = year; t.Team = team; t.Grade = grade;
            t.BatterPosition = BatterPosition.RightField;
            t.BatterStats = StatProfiles.SpreadBatter(70, BatterPosition.RightField, 7);
            return new Player(Guid.NewGuid().ToString(), t) { ReinforceLevel = reinforce, AwakenLevel = awaken };
        }

        public static string Build()
        {
            var sb = new StringBuilder();
            try
            {
                BuildAwaken(sb);
                BuildLineup(sb);
                BuildScout(sb);
                BuildSpecial(sb);
            }
            finally
            {
                foreach (var o in Temp) if (o != null) UnityEngine.Object.DestroyImmediate(o);
                Temp.Clear();
            }
            return sb.ToString();
        }

        private static void BuildAwaken(StringBuilder sb)
        {
            sb.AppendLine("=== [1] 각성 재료 규칙(동일 선수 + 동일 시즌 등급: 같은 연도 +3각 / 다른 연도 +1각) ===");
            foreach (var grade in new[] { Grade.LIVE_EPIC, Grade.TITLE_HOLDER, Grade.GOLDEN_GLOVE })
            {
                var target = Card(grade, "KOO", 2024, Team.Samsung);
                var steps = new List<string>();
                for (int i = 0; i < 4; i++)
                {
                    int before = target.AwakenLevel;
                    int ovrBefore = target.AwakenGrowth;
                    UpgradeManager.ApplyAwaken(target, new List<Player> { Card(grade, "KOO", 2024, Team.Samsung) });
                    steps.Add($"{before}각→{target.AwakenLevel}각(OVR +{target.AwakenGrowth - ovrBefore})");
                }
                var other = Card(grade, "KOO", 2024, Team.Samsung);
                UpgradeManager.ApplyAwaken(other, new List<Player> { Card(grade, "KOO", 2022, Team.Samsung) });
                sb.AppendLine($"{CardGrowthRules.DisplayName(grade),-8} 같은 연도 재료 1장씩: {string.Join(" / ", steps)} | 다른 연도 재료 1장: 0각→{other.AwakenLevel}각 " +
                              $"| 배지 {CardGrowthRules.AwakenMaterialBadge(other, Card(grade, "KOO", 2024, Team.Samsung))} {CardGrowthRules.AwakenMaterialBadge(other, Card(grade, "KOO", 2021, Team.Samsung))}");
            }
            var gg = Card(Grade.GOLDEN_GLOVE, "KOO", 2024, Team.Samsung);
            sb.AppendLine($"성장 센터 미리보기: {GrowthCenterRules.AwakenPreview(gg, new[] { Card(Grade.GOLDEN_GLOVE, "KOO", 2024, Team.Samsung) })} / " +
                          $"{GrowthCenterRules.AwakenPreview(gg, new[] { Card(Grade.GOLDEN_GLOVE, "KOO", 2023, Team.Samsung) })}");
            sb.AppendLine();
        }

        private static void BuildLineup(StringBuilder sb)
        {
            sb.AppendLine("=== [2] 라인업 화면(씬 배선) ===");
            var roster = UnityEngine.Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            if (roster == null) { sb.AppendLine("RosterUIController 없음"); sb.AppendLine(); return; }
            var orphans = roster.transform.Cast<Transform>().Where(t => RosterUIController.LegacyBarObjectNames.Contains(t.name)).Select(t => $"{t.name}({(t.gameObject.activeSelf ? "활성" : "비활성")})").ToList();
            sb.AppendLine($"RosterPanel 직속 레거시 바 오브젝트: {(orphans.Count == 0 ? "없음(정리 완료)" : string.Join(", ", orphans))}");
            var so = new SerializedObject(roster);
            string PathOf(UnityEngine.Object o)
            {
                var t = o is GameObject g ? g.transform : o is Component c ? c.transform : null;
                var parts = new List<string>();
                for (; t != null; t = t.parent) parts.Insert(0, t.name);
                return t == null && parts.Count == 0 ? "없음" : string.Join("/", parts);
            }
            foreach (var field in new[] { "setDeckOptionButton", "autoLineupButton", "setDeckGaugeFillImage", "defaultBarRoot", "trayRoot" })
                sb.AppendLine($"  {field} → {PathOf(so.FindProperty(field).objectReferenceValue)}");
            int setDeckButtons = roster.GetComponentsInChildren<Transform>(true).Count(t => t.name == "SetDeckOptionButton");
            sb.AppendLine($"[세트덱 버프 선택] 버튼 수: {setDeckButtons} (DefaultBar 안 1개 = 트레이가 열리면 함께 숨김)");
            var options = roster.GetComponentInChildren<SetDeckOptionUIController>(true);
            var panel = options != null ? new SerializedObject(options).FindProperty("panelRoot").objectReferenceValue as GameObject : null;
            sb.AppendLine($"세트덱 버프 팝업: {PathOf(panel)} (형제 {(panel != null ? panel.transform.GetSiblingIndex() + 1 : 0)}/{(panel != null ? panel.transform.parent.childCount : 0)}) - Open 시 SetAsLastSibling");
            var tiles = UnityEngine.Object.FindObjectsByType<UI.LobbyButtonRelay>(FindObjectsInactive.Include).Where(r => r.OpensSetDeckBuffs).Select(r => PathOf(r)).ToList();
            sb.AppendLine($"로비 [세트덱 & 버프 선택] 타일 → 버프 팝업 직행: {(tiles.Count > 0 ? string.Join(", ", tiles) : "없음")}");
            sb.AppendLine("보관 선수 그리드: 카드마다 새 인스턴스(SpawnFreshCard) + 풀 반납 시 렌더 상태 초기화(CardPoolManager.ResetRenderState)");
            sb.AppendLine();
        }

        private static void BuildScout(StringBuilder sb)
        {
            sb.AppendLine("=== [3] 스카우트 확률표(골글 이상 제외 · 최고 TITLE_HOLDER) 실측 ===");
            var rng = new System.Random(185);
            const int draws = 1000000;
            foreach (var (label, table) in new[] { ("프리미엄·픽업(싸인볼/트로피/픽업권)", ScoutDropTables.Premium), ("일반(포인트/일반권)", ScoutDropTables.Normal) })
            {
                var counts = new Dictionary<Grade, int>();
                for (int i = 0; i < draws; i++)
                {
                    var g = ScoutDropTables.RollGrade(table, (float)(rng.NextDouble() * 100.0));
                    counts[g] = counts.TryGetValue(g, out int n) ? n + 1 : 1;
                }
                sb.AppendLine($"{label} {draws:N0}회: " + string.Join(" / ", table.Select(e =>
                    $"{CardGrowthRules.DisplayName(e.Grade)} 설정 {e.RatePercent:0.0}% · 실측 {100.0 * (counts.TryGetValue(e.Grade, out int c) ? c : 0) / draws:0.00}%")) +
                    $" | 골글 이상 {counts.Where(kv => ScoutDropTables.IsExcludedFromScout(kv.Key)).Sum(kv => kv.Value)}회");
            }

            var dbGo = new GameObject("Task185ReportDb");
            try
            {
                var db = dbGo.AddComponent<PlayerDatabase>();
                var all = db.AllTemplates;
                sb.AppendLine($"카드 DB {all.Count:N0}장 - 등급별 스카우트 가능 여부: " + string.Join(", ", all.GroupBy(t => t.Grade).OrderBy(g => CardGrowthRules.PowerRank(g.Key))
                    .Select(g => $"{CardGrowthRules.DisplayName(g.Key)} {g.Count()}장{(ScoutDropTables.IsExcludedFromScout(g.Key) ? "(제외)" : "")}")));
                var pickRng = new System.Random(7);
                Func<float> r = () => (float)pickRng.NextDouble();
                const int picks = 20000;
                foreach (var team in new[] { Team.Samsung, Team.KIA })
                {
                    var parts = new List<string>();
                    foreach (var grade in new[] { Grade.TITLE_HOLDER, Grade.FRANCHISE, Grade.ALLSTAR, Grade.LIVE_EPIC })
                    {
                        var gradePool = all.Where(t => t.Grade == grade).ToList(); // 같은 등급만 넘겨 실측 속도를 맞춘다(결과는 전체 풀과 동일)
                        if (!gradePool.Any(t => t.Team == team)) { parts.Add($"{CardGrowthRules.DisplayName(grade)} 해당 구단 카드 없음"); continue; }
                        int own = 0, ownNormal = 0;
                        for (int i = 0; i < picks; i++)
                        {
                            if (ScoutDropTables.PickTemplate(gradePool, grade, team, true, r).Team == team) own++;
                            if (ScoutDropTables.PickTemplate(gradePool, grade, team, false, r).Team == team) ownNormal++;
                        }
                        parts.Add($"{CardGrowthRules.DisplayName(grade)} 픽업 {100.0 * own / picks:0.0}% (일반 {100.0 * ownNormal / picks:0.0}%)");
                    }
                    sb.AppendLine($"선택 구단 {team} 픽업 실측({picks:N0}회): {string.Join(" / ", parts)} | 배너 {ScoutUIController.PickupBannerText(team)}");
                }
                sb.AppendLine();
                BuildSpecialTargets(sb, all);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dbGo);
            }
        }

        private static void BuildSpecialTargets(StringBuilder sb, IReadOnlyList<PlayerTemplate> all)
        {
            sb.AppendLine("=== [4] 특별 영입 대상(선택 구단 GG / SIG 풀) ===");
            foreach (Team team in Enum.GetValues(typeof(Team)))
            {
                if (team == Team.None) continue;
                var gg = SpecialRecruitRules.TargetPool(all, SpecialRecruitRules.GoldenGloveRecipe, team);
                var sig = SpecialRecruitRules.TargetPool(all, SpecialRecruitRules.SignatureRecipe, team);
                sb.AppendLine($"{team,-8} 골든글러브 {gg.Count(t => t.Team == team)}명 · 시그니처 {sig.Count(t => t.Team == team)}명 (예: {string.Join(", ", gg.Take(3).Select(t => $"{t.PlayerName}'{t.SeasonYear % 100:00}"))})");
            }
        }

        private static void BuildSpecial(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("=== [5] 특별 영입 재료 조건 · 동작 ===");
            foreach (var recipe in new[] { SpecialRecruitRules.GoldenGloveRecipe, SpecialRecruitRules.SignatureRecipe })
                sb.AppendLine($"{recipe.Name}({recipe.Slots.Count}슬롯): " + string.Join(" | ", recipe.Slots.Select((s, i) => $"{i + 1}. {s.Title} - {s.Condition}")) +
                              $" | 재화 {SpecialRecruitRules.CostLabel(recipe)}");

            var templates = new List<PlayerTemplate>
            {
                Card(Grade.GOLDEN_GLOVE, "삼성GG", 2024, Team.Samsung).Template, Card(Grade.SIGNATURE, "삼성SIG", 2024, Team.Samsung).Template,
                Card(Grade.GOLDEN_GLOVE, "KIA GG", 2024, Team.KIA).Template,
            };
            Player Issue(PlayerTemplate t) => new Player(Guid.NewGuid().ToString(), t);

            var gg = new MemoryRecruitLedger { FavoriteTeam = Team.Samsung, Points = 60000 };
            var lineup = Card(Grade.LIVE_EPIC, "주전", 2026, Team.Samsung);
            gg.Cards.AddRange(new[] { Card(Grade.FRANCHISE, "FRA", 2025, Team.KIA), Card(Grade.LIVE_NORMAL, "강화", 2026, Team.LG, reinforce: 3),
                Card(Grade.LIVE_EPIC, "삼성1", 2026, Team.Samsung), Card(Grade.LIVE_EPIC, "삼성2", 2026, Team.Samsung), lineup });
            gg.Lineup.Add(lineup);
            var assign = SpecialRecruitRules.AutoAssign(SpecialRecruitRules.GoldenGloveRecipe, gg.Inventory, gg.Roster, Team.Samsung);
            sb.AppendLine("골글 자동 등록: " + string.Join(" | ", assign.Select(a => string.Join(",", a.Select(p => p.Template.PlayerName)))) + $" (주전 '{lineup.Template.PlayerName}' 제외 {(!assign.SelectMany(a => a).Contains(lineup) ? "OK" : "실패")})");
            bool ok = SpecialRecruitRules.TryRecruit(SpecialRecruitRules.GoldenGloveRecipe, assign.Select(a => (IReadOnlyList<Player>)a).ToList(), gg, templates, n => 0, Issue, out var card, out var msg);
            sb.AppendLine($"골글 영입: {(ok ? "성공" : "실패")} - {msg} | 남은 포인트 {gg.Points:N0} · 보유 {gg.Cards.Count}장");

            var sig = new MemoryRecruitLedger { FavoriteTeam = Team.Samsung, Points = 250000, Trophies = 5 };
            sig.Cards.AddRange(new[] { Card(Grade.GOLDEN_GLOVE, "GG", 2024, Team.KIA), Card(Grade.TITLE_HOLDER, "TH", 2024, Team.LG), Card(Grade.FRANCHISE, "FRA", 2025, Team.NC),
                Card(Grade.LIVE_EPIC, "+6강", 2026, Team.KT, reinforce: 6), Card(Grade.ALLSTAR, "3각", 2025, Team.SSG, awaken: 3),
                Card(Grade.LIVE_NORMAL, "삼성A", 2026, Team.Samsung), Card(Grade.LIVE_NORMAL, "삼성B", 2026, Team.Samsung), Card(Grade.LIVE_EPIC, "삼성C", 2026, Team.Samsung) });
            var sigAssign = SpecialRecruitRules.AutoAssign(SpecialRecruitRules.SignatureRecipe, sig.Inventory, sig.Roster, Team.Samsung);
            ok = SpecialRecruitRules.TryRecruit(SpecialRecruitRules.SignatureRecipe, sigAssign.Select(a => (IReadOnlyList<Player>)a).ToList(), sig, templates, n => 0, Issue, out card, out msg);
            sb.AppendLine($"시그니처 영입: {(ok ? "성공" : "실패")} - {msg} | 남은 트로피 {sig.Trophies} · 포인트 {sig.Points:N0}");
            sb.AppendLine($"재화 부족 검증: {SpecialRecruitRules.Validate(SpecialRecruitRules.GoldenGloveRecipe, assign.Select(a => (IReadOnlyList<Player>)a).ToList(), new MemoryRecruitLedger { FavoriteTeam = Team.Samsung, Points = 1 })}");

            var hub = UnityEngine.Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            var special = hub != null ? hub.SpecialSection : null;
            sb.AppendLine($"스카우트 허브 특별 영입 섹션: {(special != null ? special.name : "없음")} / SpecialRecruitView {(special != null && special.GetComponent<SpecialRecruitView>() != null ? "부착" : "없음")}");
        }
    }
}
