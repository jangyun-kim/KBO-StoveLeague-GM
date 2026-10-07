using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.EditorTools;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using KBOManager.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-GM-10] 자동 검증(Unity CLI BatchPipelineGM10 1회 실행):
    ///   1) Log5 공식(.350 타자 · .200 투수 · 리그 .270) 일치 + 엔진 타석 판정이 Log5 안타 확률을 재현
    ///   2) 144경기 후 리그 투타 밸런스 유지 · 10위 승률 붕괴 없음(Log5 + 단장 역학)
    ///   3) 올타임 드림 - 레전드 우대로 팀워크 50 이상 · 연도 시너지(상한 해제)가 실효 전력에 반영
    ///   4) 보류명단 - AI 구단이 가성비 · OVR 최하위권(고액 저효율 베테랑 · 성장 한계 유망주)을 자유계약 방출 / 원 소속 우선 협상
    ///   5) 1920×1080 레이아웃 겹침 0 · Bold 0 · 모바일 RaycastTarget 정리(터치 가로채는 텍스트 0)
    /// </summary>
    public class GM10VerificationRunner
    {
        private static IReadOnlyList<PlayerTemplate> templates;
        private static List<Cheerleader> cheer;
        private static GameObject dbObject;
        private static GMLiveSeasonSimulator season;
        private readonly List<Object> created = new List<Object>();
        private bool sceneOpened;

        [SetUp]
        public void EnsureDatabase()
        {
            if (templates != null && templates.Count > 0 && templates.All(t => t != null)) return;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            dbObject = new GameObject("GM10_PlayerDatabase");
            templates = dbObject.AddComponent<PlayerDatabase>().AllTemplates.ToList();
            cheer = GMRosterLoader.AllCheerleaderTemplates();
            Assert.Greater(templates.Count, 500);
        }

        [OneTimeTearDown]
        public void Unload()
        {
            if (templates != null) foreach (var t in templates) if (t != null) Object.DestroyImmediate(t);
            templates = null;
            season = null;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
        }

        [TearDown]
        public void TearDown()
        {
            GameSettings.UseVirtualNames = false;
            if (templates != null) NameAliasTable.ApplyDisplayNames(templates.Where(t => t != null), false);
            foreach (var obj in created) if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = false;
        }

        private static GMLeagueState NewLeague(GMStartMode mode = GMStartMode.RealCurrent2026, string team = "SAM", int seed = 606) // [TASK-GM-10] Log5 엔진 기준 재선정(하네스 실측)
        {
            var league = GMRosterLoader.LoadModeRoster(mode, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        private static GMLiveSeasonSimulator SharedSeason()
        {
            if (season != null) return season;
            var sim = new GMLiveSeasonSimulator(NewLeague());
            Assert.IsTrue(sim.StartRun(GMRunMode.FullSeason));
            sim.RunUntilStop();
            Assert.IsTrue(sim.IsSeasonComplete);
            season = sim;
            return season;
        }

        private PlayerTemplate Template(string id, bool pitcher)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = t.RealPlayerId = id;
            t.PlayerName = t.RealName = id;
            t.SeasonYear = 2026;
            t.Team = t.CurrentTeam = Team.Samsung;
            t.Grade = Grade.LIVE_NORMAL;
            t.IsPitcher = pitcher;
            if (pitcher) { t.PitcherRole = PitcherRole.StartingPitcher; t.PitcherStats = new PitcherStats(60, 60, 60, 60, 90); }
            else { t.BatterPosition = BatterPosition.RightField; t.BatterStats = new BatterStats(60, 60, 60, 60, 60); }
            return t;
        }

        private GameObject NewCanvas()
        {
            var canvasGo = new GameObject("GM10_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvasGo);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            return canvasGo;
        }

        private T NewView<T>(GameObject canvas, string name) where T : MonoBehaviour
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return go.AddComponent<T>();
        }

        // ================================================================== 1) Log5

        [Test]
        public void T1_Log5_Formula_And_EngineReproducesHitProbability()
        {
            // 지시서 예시 - 타자 .350 · 투수 피안타율 .200 · 리그 .270
            double b = 0.350, p = 0.200, l = 0.270;
            double expected = (b * p / l) / ((b * p / l) + (1 - b) * (1 - p) / (1 - l));
            Assert.AreEqual(expected, GMLog5.Log5(b, p, l), 1e-12, "Log5 = Bill James 공식");
            Assert.AreEqual(0.2668, GMLog5.Log5(b, p, l), 1e-4);
            Assert.AreEqual(l, GMLog5.Log5(l, l, l), 1e-12, "평균 대 평균 = 리그 평균");
            Assert.AreEqual(b, GMLog5.Log5(b, l, l), 1e-12, "평균 투수 상대 = 타자 본래 타율");
            Assert.AreEqual(p, GMLog5.Log5(l, p, l), 1e-12, "평균 타자 상대 = 투수 본래 피안타율");
            Assert.Greater(GMLog5.Log5(0.30, 0.25, 0.265), GMLog5.Log5(0.25, 0.25, 0.265), "좋은 타자 = 안타 확률 ↑");

            // 원 기록(PerformanceData) - 실제 기록 CSV가 있으면 Real, 없으면 세부 능력치 추정(Fallback)
            var est = GMLog5.Estimate(Template("GM10_EST", false));
            Assert.IsFalse(est.IsReal, "현재 데이터 = 추정");
            Assert.AreEqual(GMLog5.NominalPA, est.PA);
            Assert.AreEqual(est.PA, est.AB + est.BB);
            Assert.That(est.Avg, Is.InRange(0.21, 0.29), "능력치 60 타자 ≈ 리그 평균 이하 타율");
            int loaded = GMLog5.LoadRealStats("real_player_id,season_year,pa,ab,h,hr,bb,so,bf,ip_outs,p_h,p_hr,p_bb,p_so\n" +
                                              "GM10_BAT,2026,600,540,189,20,60,80,0,0,0,0,0,0\n" +
                                              "GM10_PIT,2026,0,0,0,0,0,0,650,450,138,12,50,120\n");
            Assert.AreEqual(2, loaded);
            var batter = new Player("GM10_B", Template("GM10_BAT", false));
            var pitcher = new Player("GM10_P", Template("GM10_PIT", true));
            Assert.IsTrue(batter.Performance.IsReal, "실제 원 기록 연동");
            Assert.AreEqual(0.350, batter.Performance.Avg, 1e-9);
            Assert.AreEqual(0.230, pitcher.Performance.OAvg, 1e-9);

            // 엔진 - 단장 모드 경기(케미스트리 · 리그 밸런스 있음)의 타석 판정이 Log5 안타 확률을 재현(역학 보정 1.0 조건)
            var chemB = new GMChemistryModifiers { Batting = new GMBattingBalance(), EffectivePower = 1f };
            var chemP = new GMChemistryModifiers { Batting = chemB.Batting, EffectivePower = 1f };
            var engine = new MatchEngine(new List<Player> { batter }, new List<Player> { pitcher },
                new TeamPowerModifiers(0, 0, 1f, null, null, 60, chemB), new TeamPowerModifiers(0, 0, 1f, null, null, 60, chemP), null, null, 2026);
            int ab = 0, hits = 0, n = 40000;
            for (int i = 0; i < n; i++)
            {
                var r = engine.SimulateAtBat(batter, pitcher, new MatchState());
                if (r == AtBatResult.Walk) continue;
                ab++;
                if (r == AtBatResult.Single || r == AtBatResult.Double || r == AtBatResult.Triple || r == AtBatResult.HomeRun) hits++;
            }
            double engineHit = (double)hits / ab, log5 = GMLog5.Log5(0.350, 0.230, GMLog5.LgAvg);
            TestContext.WriteLine($"[GM10 Log5] 엔진 안타/타수 {engineHit:.0000} vs Log5 {log5:.0000} ({ab}타수)");
            Assert.AreEqual(log5, engineHit, 0.01, "엔진 = Log5 안타 확률");
        }

        // ================================================================== 2) 투타 밸런스 · 10위

        [Test]
        public void T2_Season144_Balance_Maintained_LastPlaceNoCollapse()
        {
            var sim = SharedSeason();
            var l = sim.League;
            var st = sim.Standings();
            var (avg, obp, slg, k) = sim.LeagueBattingLine();
            double runs = l.Records.Values.Sum(r => r.RunsScored) / (double)l.Records.Values.Sum(r => r.G);
            double lgEra = l.Stats.Values.Sum(s => s.ER) * 27.0 / Math.Max(1, l.Stats.Values.Sum(s => s.OutsPitched));
            var title = sim.Leaders(GMLeaderCategory.AVG, 1)[0];
            var era1 = sim.Leaders(GMLeaderCategory.ERA, 1)[0];
            var hr = sim.Leaders(GMLeaderCategory.HR, 1)[0];
            string line = $"[GM10 Log5 시즌] 타율 {avg:.000} · 출루율 {obp:.000} · 장타율 {slg:.000} · 삼진율 {k:P1} · 득점 {runs:0.00} · ERA {lgEra:0.00} | 타격왕 {title.Name} {title.ValueLabel} · ERA 1위 {era1.Name} {era1.ValueLabel} · 홈런왕 {hr.Name} {hr.ValueLabel} | 1위 {GMTeamRecord.PctLabel(st[0].Pct)} · 10위 {GMTeamRecord.PctLabel(st[9].Pct)}({st[9].W}승)";
            TestContext.WriteLine(line);
            Debug.Log(line);
            Assert.That(avg, Is.InRange(0.255, 0.275), "리그 타율");
            Assert.That(obp, Is.InRange(0.315, 0.350), "출루율");
            Assert.That(slg, Is.InRange(0.380, 0.440), "장타율");
            Assert.That(k, Is.InRange(0.15, 0.22), "삼진율");
            Assert.That(runs, Is.InRange(4.5, 5.0), "팀당 경기 득점");
            Assert.That(lgEra, Is.InRange(4.20, 4.50), "리그 평균자책점");
            Assert.GreaterOrEqual(st[9].Pct, 0.250, "10위 승률 붕괴 없음");
            Assert.LessOrEqual(st[0].Pct, 0.700);
            Assert.That(title.Value, Is.InRange(0.320, 0.400), "타격왕 현실 범위");
            Assert.Greater(hr.Value, 20, "Log5 HR/H - 홈런왕 20개 이상");
            // 단장 역학 - 실효 전력 계수가 최종 오즈 배수로 들어간다(경기 모디파이어에 원값 전달)
            var mods = sim.ModifiersFor(l.UserTeam, true);
            Assert.That(mods.Chemistry.EffectivePower, Is.InRange(TeamChemistryEngine.MinEffectivePower, TeamChemistryEngine.MaxEffectivePower));
            Assert.IsNotNull(mods.Chemistry.Batting, "단장 모드 경기 = Log5 경로");
            Assert.AreEqual(0.5, GMLog5.PowerRatioExponent, 1e-9);
        }

        // ================================================================== 3) 올타임 드림

        [Test]
        public void T3_AllTimeDream_LegendFavor_Teamwork50Plus_YearSynergyActive()
        {
            var dream = NewLeague(GMStartMode.AllTimeDream, "KIA", 73);
            int strict = 0;
            foreach (var team in dream.Teams.Values)
            {
                var roster = team.AvailableRoster;
                Assert.IsTrue(GMYearSynergy.IsLegendRoster(roster), $"{team.TeamCode} 레전드 로스터");
                var with = TeamChemistryEngine.EvaluateRoster(roster, team.PayrollCap, team.TeamworkBuff);
                var without = TeamChemistryEngine.EvaluateRoster(roster, team.PayrollCap, team.TeamworkBuff - team.YearSynergyBonus);
                TestContext.WriteLine($"[GM10 드림] {team.TeamCode} 팀워크 {without.TeamworkScore}→{with.TeamworkScore} · 실효 전력 {without.EffectivePowerMultiplier:0.000}→{with.EffectivePowerMultiplier:0.000} · 시너지 +{team.YearSynergyBonus}");
                Assert.IsTrue(with.LegendMode, "레전드 우대 발동");
                Assert.GreaterOrEqual(with.TeamworkScore, 50, $"{team.TeamCode} 팀워크 50 이상");
                if (team.YearSynergyBonus > 0 && with.EffectivePowerMultiplier > without.EffectivePowerMultiplier) strict++;
                // 레전드 우대가 없을 때(Ego 페널티 100%)보다 높다
                Assert.AreEqual(GMYearSynergy.Groups(roster).Sum(g => g.Bonus), team.YearSynergyBonus, "레전드 = 연도 시너지 상한 해제");
            }
            Assert.Greater(strict, 0, "연도 시너지가 실효 전력을 올린 드림 구단");
            Assert.AreEqual(0.5f, TeamChemistryEngine.LegendEgoPenaltyScale, 1e-6f, "Ego 충돌 페널티 50%");
            // 현역 로스터는 레전드가 아니고 상한 +8 유지
            var current = NewLeague(team: "SAM", seed: 72);
            Assert.IsFalse(GMYearSynergy.IsLegendRoster(current.UserTeam.AvailableRoster));
            Assert.LessOrEqual(GMYearSynergy.TeamworkBonus(current.UserTeam.AvailableRoster), GMYearSynergy.MaxTeamBonus);
            var balance = GMBattingBalance.FromRosters(dream.Teams.Values.Select(t => (IEnumerable<Player>)t.Roster));
            Assert.IsTrue(balance.Legend, "레전드 리그 - 체급 가산 끔");
        }

        // ================================================================== 4) 보류명단 · 우선 협상

        [Test]
        public void T4_ReserveList_AiReleasesLowestValue_PriorityNegotiation()
        {
            var sim = SharedSeason();
            var l = sim.League;
            Assert.IsTrue(GMAwardEvaluator.AdvanceToNextSeasonYear(sim), "2026 → 2027");
            var releases = l.LastReserveReleases.ToList();
            TestContext.WriteLine("[GM10 보류명단] " + string.Join(", ", releases.Select(r => $"{r.TeamCode} {r.Player.Template.PlayerName}(OVR {r.Player.BaseOverall} · 잠재 {r.Player.Potential} · {r.Player.Age}세 · {GMDiagnosticFormat.Short(r.Player.Salary)} · {r.Reason})")));
            foreach (var t in l.Teams.Values.Where(t => !t.IsUserTeam))
            {
                var mine = releases.Where(r => r.TeamCode == t.TeamCode).ToList();
                Assert.That(mine.Count, Is.InRange(GMReserveList.MinReleases, GMReserveList.MaxReleases), $"{t.TeamCode} 보류명단 제외 1~3명");
                Assert.LessOrEqual(GMReserveList.ReserveCount(t), GMReserveList.ReserveLimit, "소속 68명 한도");
                foreach (var r in mine)
                {
                    Assert.Contains(r.Player, l.FreeAgents, "자유계약 FA 풀");
                    Assert.IsNull(GMFaCompensation.OriginOf(l, r.Player), "자유계약 = 보상 없음");
                    Assert.IsFalse(t.ReservePlayers.Contains(r.Player));
                    if (r.Reason.Contains("최하위권"))
                    {
                        var remain = t.ReservePlayers.Where(p => !p.IsCaptain && !l.FASignedThisYear.Contains(p.InstanceId) && !l.RookiesThisYear.Contains(p.InstanceId)).ToList();
                        Assert.LessOrEqual(GMReserveList.ValueScore(r.Player), remain.Min(GMReserveList.ValueScore) + 1e-6, $"{t.TeamCode} {r.Player.Template.PlayerName} = 가성비 · OVR 최하위");
                    }
                    else Assert.IsTrue(GMReserveList.IsOverpaidVeteran(r.Player) || r.Reason.Contains("유망주") || r.Reason.Contains("한도"), r.Reason);
                }
            }
            Assert.IsTrue(releases.All(r => !l.Teams[r.TeamCode].IsUserTeam), "내 구단은 자동 방출 없음");
            Assert.IsTrue(l.News.Any(n => n.Title.Contains("보류명단")), "보류명단 소식");

            // 원 소속 우선 협상 - 내 구단 만료자는 FA 공시 전 1회 협상
            var user = l.UserTeam;
            var pending = GMReserveList.PendingPriority(l);
            Assert.IsTrue(GMReserveList.IsPriorityOpen(l), "우선 협상 열림");
            Assert.IsNotEmpty(pending, "내 구단 계약 만료자");
            Assert.IsTrue(pending.All(p => user.Roster.Contains(p) || user.Futures.Contains(p)), "공시 전에는 내 구단 소속");
            Assert.IsTrue(pending.All(p => !l.FreeAgents.Contains(p)));
            var keep = pending.OrderByDescending(p => p.BaseOverall).First();
            var ext = GMStoveLeagueMarket.Extend(l, user, keep, 3, (int)(GMStoveLeagueMarket.ExtensionDemand(keep, GMDifficulty.Majors) * 1.2), false);
            Assert.IsTrue(ext.Success, ext.Message);
            var gone = GMReserveList.ClosePriorityNegotiation(l);
            Assert.IsFalse(gone.Contains(keep), "재계약 = 잔류");
            Assert.Contains(keep, user.Roster.Concat(user.Futures).ToList());
            Assert.AreEqual(pending.Count - 1, gone.Count, "미계약자 FA 공시");
            Assert.IsTrue(gone.All(p => l.FreeAgents.Contains(p) && GMFaCompensation.OriginOf(l, p)?.TeamCode == user.TeamCode), "원 소속 = 내 구단");
            Assert.IsFalse(GMReserveList.IsPriorityOpen(l));
            Assert.IsTrue(l.News.Any(n => n.Title.Contains("우선 협상 마감")));
            season = null; // 공유 시즌을 2027로 넘겼다
        }

        // ================================================================== 5) 레이아웃 · Bold · 모바일 터치

        [Test]
        public void T5_Layout_NoOverlap_NoBold_MobileRaycastTargets()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "SAM", seed: 88));
            var canvas = NewCanvas();
            var hub = NewView<GMOotpFrontOfficeUIController>(canvas, "GM10_Hub");
            hub.Build();
            var prePost = NewView<GMMatchPrePostUIController>(canvas, "GM10_PrePost");
            prePost.Build();
            prePost.gameObject.SetActive(false);
            hub.PrePostView = prePost;
            hub.Bind(sim);
            hub.SelectMainTab(0);
            hub.SelectSubTab(1);
            var pane = hub.Pane(GMOotpFrontOfficeUIController.PaneSalaries);
            StringAssert.Contains("우선 협상", pane.Find("PriorityLabel").GetComponent<Text>().text);
            Assert.IsFalse(pane.Find("PriorityClose").GetComponent<Button>().interactable, "2026 시작 = 우선 협상 대상 없음");
            CheckLayer(hub.Root, "허브 헤더");
            CheckLayer(pane, "연봉·재계약 + 우선 협상");
            sim.League.PriorityNegotiationIds.Add(sim.League.UserTeam.Roster[0].InstanceId);
            sim.League.UserTeam.Roster[0].ContractYears = 0;
            hub.Refresh();
            Assert.IsTrue(pane.Find("PriorityClose").GetComponent<Button>().interactable, "만료자 = 마감 버튼 활성");
            Assert.AreEqual(1, hub.ClosePriorityNegotiation());
            CheckLayer(pane, "우선 협상 마감 후");

            // 모바일 RaycastTarget - 텍스트 · 장식 그래픽은 터치를 가로채지 않고, 버튼 대상 그래픽은 그대로
            Assert.AreEqual(0, GMRaycastSanitizer.BlockingTexts(hub.GetComponentsInChildren<Graphic>(true)), "허브 - 터치 가로채는 텍스트 0");
            Assert.AreEqual(0, GMRaycastSanitizer.BlockingTexts(prePost.GetComponentsInChildren<Graphic>(true)), "경기 화면 - 터치 가로채는 텍스트 0");
            var buttons = hub.GetComponentsInChildren<Button>(true);
            Assert.IsTrue(buttons.All(b => b.targetGraphic == null || b.targetGraphic.raycastTarget), "버튼 대상 그래픽은 터치 유지");
            Assert.IsTrue(hub.GetComponentsInChildren<RawImage>(true).All(r => !r.raycastTarget || buttons.Any(b => b.targetGraphic == r)), "로고 RawImage 터치 없음");
            int bold = hub.GetComponentsInChildren<Text>(true).Concat(prePost.GetComponentsInChildren<Text>(true)).Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic || t.text.Contains("<b>"));
            Assert.AreEqual(0, bold, "Bold 0건");

            // 씬 - Setup이 레거시 화면까지 RaycastTarget 정리
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
            var sceneGraphics = Object.FindObjectsByType<Graphic>(FindObjectsInactive.Include).Where(g => g.gameObject.scene.IsValid()).ToList();
            Assert.AreEqual(0, GMRaycastSanitizer.BlockingTexts(sceneGraphics), "씬 - 터치 가로채는 텍스트 0");
            var sceneButtons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include).Where(b => b.gameObject.scene.IsValid()).ToList();
            Assert.IsTrue(sceneButtons.All(b => b.targetGraphic == null || b.targetGraphic.raycastTarget), "씬 버튼 터치 유지");
            Assert.AreEqual(0, sceneGraphics.OfType<Text>().Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic), "씬 Bold 0건");
        }

        /// <summary>layer 직속 자식 중 활성 텍스트/버튼 영역 겹침 0 · 15pt 이상 · Normal.</summary>
        private static void CheckLayer(Transform layer, string label)
        {
            Assert.IsNotNull(layer, label);
            var items = new List<Transform>();
            foreach (Transform child in layer)
            {
                if (!child.gameObject.activeSelf) continue;
                if (child.GetComponent<Text>() != null || child.GetComponent<Button>() != null) items.Add(child);
            }
            var overlaps = new List<string>();
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                    if (Task191Report.Overlap(items[i], items[j])) overlaps.Add($"{items[i].name} ↔ {items[j].name}");
            Assert.IsEmpty(overlaps, $"{label} 텍스트 겹침 0건");
            foreach (var t in layer.GetComponentsInChildren<Text>(true))
            {
                Assert.AreEqual(FontStyle.Normal, t.fontStyle, t.name);
                Assert.GreaterOrEqual(TextTidy.EffectiveSize(t), 15, $"{label} {t.name} 15pt 이상");
            }
        }
    }
}
