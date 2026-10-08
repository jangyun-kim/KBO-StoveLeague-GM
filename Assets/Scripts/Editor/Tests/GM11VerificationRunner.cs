using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.EditorTools;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using KBOManager.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-GM-11] 자동 검증(Unity CLI BatchPipelineGM11 1회 실행):
    ///   1) 정규시즌 종료 → 시즌 결산 리포트 산출(성적 · 재정 · 직원 3인 보고) · 약점 포지션 1개 이상 · [진행하기] 첫 화면 = 시즌 결산실
    ///   2) 데이터 부족 선수(2026 시작 추정치 · 소표본)는 신뢰도 낮음 · 표본 부족, 규정 타석 선수는 신뢰도 높음 · 연도 전환 후 스냅숏 결산
    ///   3) 계약 협상실 - 후보 풀 5~7 · 유효 카드 3장 · 카드별 진행 가능성(+10%p × 성향 0.5~1.5) · 결과 분포(합 1) · 재무팀장 경고 · 결렬 쿨다운 · 세이브 필드
    ///   4) 1920×1080 레이아웃 - 시즌 결산실 · 계약 협상실 텍스트 겹침 0 · 15pt 이상(지시서 14pt) · Bold 0
    /// </summary>
    public class GM11VerificationRunner
    {
        private static IReadOnlyList<PlayerTemplate> templates;
        private static List<Cheerleader> cheer;
        private static GameObject dbObject;
        private static GMLiveSeasonSimulator season;
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void EnsureDatabase()
        {
            if (templates != null && templates.Count > 0 && templates.All(t => t != null)) return;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            dbObject = new GameObject("GM11_PlayerDatabase");
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
        }

        private static GMLeagueState NewLeague(string team = "SAM", int seed = 606)
        {
            var league = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, team, false, templates, cheer);
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

        // ================================================================== 1) 시즌 종료 → 결산 리포트 · 약점 포지션

        [Test]
        public void T1_SeasonEnd_SummaryReport_WeaknessFound_FirstScreen()
        {
            var sim = SharedSeason();
            var league = sim.League;
            var sm = GMSeasonReview.Build(league);
            StringAssert.Contains("정규시즌 기록", sm.Source);
            Assert.IsFalse(sm.Estimated);
            Assert.AreEqual(GMReportConfidence.High, sm.Confidence, "144경기 = 신뢰도 높음");
            Assert.AreEqual(144, sm.W + sm.D + sm.L, "팀 최종 성적");
            Assert.That(sm.Rank, Is.InRange(1, 10));
            StringAssert.Contains("승률", sm.RecordLine);
            Assert.AreEqual(4, sm.FinanceLines.Count, "재정 수지 요약");
            StringAssert.Contains("재정 수지", sm.FinanceLines[3]);
            Assert.AreEqual(11, sm.AllPositions.Count, "9 포지션 + 선발진 + 불펜");
            Assert.GreaterOrEqual(sm.Weaknesses.Count, 1, "약점 포지션 1개 이상");
            Assert.IsTrue(sm.Weaknesses.All(w => !string.IsNullOrEmpty(w.Comment) && w.Comment.Contains(w.PositionLabel)));
            Assert.IsTrue(sm.AllPositions.All(w => w.LeagueProduction > 0));
            Assert.AreEqual(3, sm.StaffLines.Count, "직원 3인 보고");
            StringAssert.Contains("데이터분석팀장", sm.StaffLines[0]);
            StringAssert.Contains("재무팀장", sm.StaffLines[1]);
            StringAssert.Contains("선수관리팀장", sm.StaffLines[2]);
            Assert.AreEqual(league.UserTeam.Roster.Count, sm.Players.Count, "1군 전원 성과 리포트");
            Assert.IsTrue(sm.Players.All(r => r.Metrics.Count() == 6 && r.Metrics.All(m => !string.IsNullOrEmpty(m.ValueText))));
            Debug.Log($"[GM11] 결산 {sm.RecordLine} · 약점 {string.Join(" / ", sm.Weaknesses.Select(w => $"{w.PositionLabel} {w.Production:0}({w.LeagueProduction:0}) WAR {w.War:0.0}"))}");

            // 정규시즌 종료 직후 [진행하기] 첫 화면 = 시즌 결산실 → 한 번 더 = 포스트시즌 트리
            var hub = NewHub(sim);
            Assert.IsTrue(hub.SeasonReviewPending);
            Assert.IsTrue(hub.Continue());
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneSeasonSummary, hub.CurrentPane);
            Assert.IsFalse(hub.SeasonReviewPending, "결산실은 시즌당 1회 자동");
            Assert.IsNotNull(hub.SeasonSummary);
            var pane = hub.Pane(GMOotpFrontOfficeUIController.PaneSeasonSummary);
            StringAssert.Contains("승", T(pane, "SummaryTeamPanel/SummaryRecord"));
            Assert.IsNotEmpty(T(pane, "SummaryStaffPanel/Weakness0"));
            CheckPane(hub, "시즌 결산실(시즌 종료)");
            Assert.IsTrue(hub.Continue());
            Assert.AreEqual(GMOotpFrontOfficeUIController.PanePostseason, hub.CurrentPane, "결산 다음 = 포스트시즌 트리");
            GMFrontOffice.Ensure(league).SeasonReviewSeenYear = 0; // 공유 시즌 원상 복구
        }

        // ================================================================== 2) 신뢰도 낮음 · 표본 부족

        [Test]
        public void T2_LowSample_LowConfidence_RegularsHigh_SnapshotAfterRollover()
        {
            // 2026 시작 - 시뮬레이션 기록 없음 = 전원 추정치(신뢰도 낮음 · 표본 부족)
            var fresh = NewLeague();
            var est = GMSeasonReview.Build(fresh);
            Assert.IsTrue(est.Estimated);
            Assert.AreEqual(GMReportConfidence.Low, est.Confidence);
            StringAssert.Contains("신뢰도 낮음", est.ConfidenceLabel);
            StringAssert.Contains("표본 부족", est.ConfidenceLabel);
            Assert.GreaterOrEqual(est.Weaknesses.Count, 1);
            Assert.IsTrue(est.Weaknesses.All(w => w.Comment.Contains("[표본 부족 / 신뢰도 낮음]")));
            Assert.IsTrue(est.Players.All(r => r.Confidence == GMReportConfidence.Low && r.SampleWarning && r.ConfidenceLabel.Contains("신뢰도 낮음")), "추정 리포트 전원 신뢰도 낮음");
            Assert.IsTrue(est.Players.All(r => r.Trend.Verdict.Contains("판단 보류") || r.Trend.Tone == GMReportTone.Risk), "추세 판단 보류(노쇠 · 부상 위험 제외)");

            // 시즌 종료 - 규정 타석 선수는 신뢰도 높음, 소표본 선수는 낮음
            var sim = SharedSeason();
            var league = sim.League;
            var user = league.UserTeam;
            var regular = user.Roster.First(p => !p.IsPitcher && league.Stats.TryGetValue(p.InstanceId, out var s) && s.PA >= GMSeasonReview.HighConfidencePA);
            var rr = GMSeasonReview.Report(league, regular);
            Assert.AreEqual(GMReportConfidence.High, rr.Confidence, regular.Template.PlayerName);
            Assert.IsFalse(rr.SampleWarning);
            Assert.IsFalse(rr.Line.Estimated);
            var bench = user.Roster.Where(p => !p.IsPitcher && p != regular).OrderBy(p => league.Stats.TryGetValue(p.InstanceId, out var s) ? s.PA : 0).First();
            var bs = league.StatsOf(bench, user.TeamCode);
            bs.PA = 18; bs.AB = 16; bs.H = 4; bs.Doubles = 1; bs.Triples = 0; bs.HR = 0; bs.BB = 2; bs.SO = 5; // 소표본
            var br = GMSeasonReview.Report(league, bench);
            Assert.AreEqual(GMReportConfidence.Low, br.Confidence, "18타석 = 신뢰도 낮음");
            Assert.IsTrue(br.SampleWarning);
            StringAssert.Contains("신뢰도 낮음", br.ConfidenceLabel);
            StringAssert.Contains("표본 부족", br.SampleNote);

            // 연도 전환 - 기록이 비워져도 직전 시즌 스냅숏으로 Turn 1 결산
            GMSeasonReview.TakeSnapshot(league);
            Assert.AreEqual(league.SeasonYear, GMFrontOffice.Ensure(league).LastSeasonReviewYear);
            int year = league.SeasonYear;
            for (int guard = 0; guard < 6 && !league.AdvancePhase(); guard++) { }
            Assert.AreEqual(year + 1, league.SeasonYear);
            Assert.AreEqual(0, league.Stats.Count, "연도 전환 = 기록 초기화");
            var snap = GMSeasonReview.Build(league);
            StringAssert.Contains("스냅숏", snap.Source);
            Assert.AreEqual(GMReportConfidence.High, snap.Confidence);
            Assert.GreaterOrEqual(snap.Weaknesses.Count, 1);
            Assert.AreEqual(GMReportConfidence.High, GMSeasonReview.Report(league, regular).Confidence, "스냅숏 규정 타석 = 신뢰도 높음");
            Assert.AreEqual(GMReportConfidence.Low, GMSeasonReview.Report(league, bench).Confidence, "스냅숏 소표본 = 신뢰도 낮음");
            season = null; // 공유 시즌을 다음 해로 넘겼다
        }

        // ================================================================== 3) 3지선다 협상

        [Test]
        public void T3_NegotiationRoom_ThreeValidCards_OfferGapProgress_RetryConcession()
        {
            var league = NewLeague(seed: 4040);
            var team = league.UserTeam;
            Assert.IsTrue(league.Teams.Values.All(t => t.LockerRoomTrust == GMTeamState.DefaultLockerRoomTrust), "선수단 단장 신뢰도 기본 60");
            Assert.IsTrue(team.Roster.All(p => p.Loyalty >= 10 && p.Loyalty <= 95), "충성도 기본값 10~95");
            Assert.Greater(team.Roster.Select(p => p.AgentArchetype).Distinct().Count(), 1, "에이전트 성향 분산");
            var targets = GMNegotiationRoom.Targets(team);
            if (targets.Count < 3) { foreach (var p in team.Roster.Take(3)) p.ContractYears = 1; targets = GMNegotiationRoom.Targets(team); }
            Assert.GreaterOrEqual(targets.Count, 3);

            foreach (var p in targets.Take(6))
            {
                var s = GMNegotiationRoom.Open(league, team, p);
                string who = p.Template.PlayerName;
                Assert.AreEqual("", s.BlockReason, who);
                Assert.That(s.Pool.Count, Is.InRange(GMNegotiationRoom.PoolMin, GMNegotiationRoom.PoolMax), who + " 후보 풀 5~7");
                Assert.AreEqual(3, s.Offered.Count, who + " 제시 카드 3장");
                Assert.AreEqual(3, s.Offered.Select(c => c.Id).Distinct().Count(), "중복 없음");
                Assert.IsTrue(s.Offered.All(c => s.Pool.Contains(c) && GMNegotiationRoom.AllCardIds.Contains(c.Id) && !string.IsNullOrEmpty(c.Pitch)), "유효 카드");
                Assert.GreaterOrEqual(s.Offered.Select(c => c.Category).Distinct().Count(), 2, "근거 분류 분산");
                Assert.GreaterOrEqual(s.Demand, s.CurrentSalary, "요구액 ≥ 현재 연봉");
                Assert.AreEqual(3, s.Forecasts.Count);
                CheckForecast(s.Baseline, s, who + " 기본");
                Assert.AreEqual(0f, s.Baseline.Bonus);
                foreach (var f in s.Forecasts)
                {
                    CheckForecast(f, s, $"{who} {f.Card.Title}");
                    Assert.That(f.Card.ArchetypeMultiplier, Is.InRange(GMNegotiationRoom.MinArchetypeMultiplier, GMNegotiationRoom.MaxArchetypeMultiplier));
                    Assert.AreEqual(GMNegotiationRoom.CardBaseBonus * f.Card.ArchetypeMultiplier, f.Bonus, 1e-5, "카드 가산 = +10%p × 성향 배수");
                    Assert.AreEqual(Math.Min(GMNegotiationRoom.MaxProgress, s.Baseline.Progress + f.Bonus), f.Progress, 1e-4, "진행 가능성 = 제시액 갭 기본 + 카드 가산");
                    Assert.AreEqual(s.Offer, f.Salary, "타결 연봉 = 제시액");
                    StringAssert.Contains("협상 진행 가능성", GMNegotiationRoom.ProgressText(f));
                    StringAssert.Contains("결렬 위험", GMNegotiationRoom.ProgressText(f));
                    StringAssert.Contains("타결 시", GMNegotiationRoom.DistributionText(f));
                }
                Debug.Log($"[GM11] {who}({GMNegotiationRoom.ArchetypeLabel(p.AgentArchetype)}) 기본 {GMNegotiationRoom.ProgressText(s.Baseline)} | " +
                          string.Join(" | ", s.Forecasts.Select(f => $"{f.Card.Title} {f.Progress * 100:0}% [{GMNegotiationRoom.DistributionText(f)}]")));
                // [TASK-GM-17] 제시액 갭 = 진행 가능성의 뼈대 - 요구액 100% 제시가 95% 제시보다 높고, 80% 제시보다 훨씬 높다
                var full = GMNegotiationRoom.Open(league, team, p, 0, 0, s.Demand);
                var low = GMNegotiationRoom.Open(league, team, p, 0, 0, s.Demand * 8 / 10);
                Assert.Greater(full.Baseline.Progress, s.Baseline.Progress, "제시액 ↑ = 진행 가능성 ↑");
                Assert.Greater(s.Baseline.Progress, low.Baseline.Progress + 0.1f, "요구액 80% 제시는 크게 낮다");
                Assert.Greater(full.Baseline.Progress, 0.5f, "요구액 100% 제시 = 과반 이상(진행 가능성 과소 문제 해소)");
                Assert.AreEqual(GMNegotiationOutcome.AcceptDemand, full.Baseline.Outcome);
            }
            foreach (var id in GMNegotiationRoom.AllCardIds)
                foreach (GMAgentArchetype a in Enum.GetValues(typeof(GMAgentArchetype)))
                    Assert.That(GMNegotiationRoom.ArchetypeMultiplier(id, a), Is.InRange(0.5f, 1.5f), $"{id} × {a}");

            // 재무팀장 경고 - 샐러리캡 초과
            var target = targets[0];
            int cap = team.PayrollCap;
            team.PayrollCap = team.Payroll;
            var tight = GMNegotiationRoom.Open(league, team, target, 0, 0, GMNegotiationRoom.DemandOf(league, target) * 11 / 10); // [TASK-GM-17] 제시액(요구액 110%) 기준 캡 초과
            Assert.IsTrue(tight.Forecasts.All(f => f.FinanceTone == GMReportTone.Risk && f.FinanceWarning.Contains("샐러리캡")), "캡 초과 경고");
            team.PayrollCap = cap;

            // [TASK-GM-17] 결과 반영 - 타결 = 제시액으로 계약, 실패 = 요구액 양보 + 다음 기회, 3회 실패 = 최종 결렬 → FA 시장
            var session = GMNegotiationRoom.Open(league, team, target);
            int news = league.News.Count;
            var r = GMNegotiationRoom.Resolve(league, team, session, 0);
            Assert.IsTrue(r.Success ^ r.Stalled, r.Message);
            if (r.Success)
            {
                Assert.AreEqual(r.Salary, target.Salary);
                Assert.AreEqual(r.Years, target.ContractYears);
                Assert.AreEqual(session.Offer, r.Salary, "타결 연봉 = 제시액");
                Assert.Greater(league.News.Count, news, "협상 타결 소식");
            }

            // 실패 = 결렬 위기 → 요구액 양보 · 남은 기회, 3회째 = 최종 결렬(FA 시장)
            var other = targets[1];
            other.ContractYears = 1;
            var s1 = GMNegotiationRoom.Open(league, team, other, 0, 0, 1); // 최저 제시(요구액의 70%)
            int demand0 = s1.Demand, salary = other.Salary;
            GMNegotiationRoomResult last = null;
            for (int attempt = 0; attempt < 20 && team.ReservePlayers.Contains(other); attempt++)
            {
                var sx = GMNegotiationRoom.Open(league, team, other, 0, 0, 1);
                Assert.AreEqual("", sx.BlockReason);
                last = GMNegotiationRoom.Resolve(league, team, sx, -1);
                if (last.Success) break;
                if (last.Stalled)
                {
                    Assert.Less(last.NewDemand, sx.Demand, "실패 = 요구액 양보(맞춰 가기)");
                    Assert.AreEqual(GMNegotiationRoom.MaxStrikes - GMNegotiationRoom.TalkOf(league, other).Strikes, last.ChancesLeft);
                    Assert.AreEqual(last.NewDemand, GMNegotiationRoom.Open(league, team, other).Demand, "다음 테이블 = 양보한 요구액");
                }
            }
            Assert.IsNotNull(last);
            if (!last.Success)
            {
                Assert.IsTrue(last.Broken, "3회 실패 = 최종 결렬");
                Assert.IsTrue(last.MovedToFA, "최종 결렬 = FA 시장 이동");
                Assert.Contains(other, league.FreeAgents);
                Assert.IsFalse(team.ReservePlayers.Contains(other));
                Assert.AreEqual(GMOriginOf(league, other), team.TeamCode, "원 소속 = 내 구단");
            }
            TestContext.WriteLine($"[GM11 협상] 최초 요구 {demand0} · 결과 {(last.Success ? "타결" : "최종 결렬 → FA")} · {last.Message}");
            GMFrontOffice.OnNewSeason(league);
            Assert.IsNull(GMNegotiationRoom.TalkOf(league, targets[0]), "새 스토브리그 = 협상 기록 초기화");

            // 유대 - 주전 포수 ↔ 선발(배터리)
            var catcher = LineupAssignment.AssignStarters(team.Roster, team.Lineup).First(x => x.Position == BatterPosition.Catcher).Player;
            Assert.IsTrue(GMPlayerBonds.For(team, catcher).Any(b => b.kind == GMBondKind.Battery), "배터리 유대");

            // 세이브 v22 필드
            var probe = new Player("GM11_probe", target.Template);
            SaveManager.ApplyGMFields(probe, new PlayerSaveData { Age = 30, Loyalty = 77, AgentArchetype = (int)GMAgentArchetype.RoleSeeker });
            Assert.AreEqual(77, probe.Loyalty);
            Assert.AreEqual(GMAgentArchetype.RoleSeeker, probe.AgentArchetype);
            SaveManager.ApplyGMFields(probe, new PlayerSaveData { Age = 30 });
            Assert.That(probe.Loyalty, Is.InRange(10, 95), "구버전 세이브 = 성향 기본값");
            Assert.AreEqual(GMTeamState.DefaultLockerRoomTrust, new GMTeamSaveData().LockerRoomTrust);
        }

        private static string GMOriginOf(GMLeagueState league, Player p) => league.FAOrigins.TryGetValue(p.InstanceId, out var o) ? o.TeamCode : "";

        private static void CheckForecast(GMNegotiationForecast f, GMNegotiationSession s, string label)
        {
            Assert.That(f.Progress, Is.InRange(GMNegotiationRoom.MinProgress, GMNegotiationRoom.MaxProgress), label);
            Assert.AreEqual(1f, f.Distribution.Sum(), 1e-4, label + " 결과 분류 1개");
            Assert.AreEqual(1f - f.Progress, f.BreakRisk, 1e-6);
            Assert.GreaterOrEqual(f.Salaries[0], f.Salaries[1], label + " 수용 ≥ 소폭");
            if (s.CurrentSalary < Player.MaxSalary) Assert.Greater(f.Salaries[1], f.Salaries[2], label + " 소폭 > 동결");
            else Assert.GreaterOrEqual(f.Salaries[1], f.Salaries[2], label + " 최고 연봉 - 소폭 = 동결");
            Assert.GreaterOrEqual(f.Salaries[2], f.Salaries[3], label + " 동결 ≥ 삭감");
            Assert.AreEqual(s.CurrentSalary, f.Salaries[2]);
            Assert.That(f.Years, Is.InRange(1, Player.MaxContractYears));
            StringAssert.Contains("재무팀장", f.FinanceWarning);
        }

        // ================================================================== 4) 레이아웃

        [Test]
        public void T4_Layout_SummaryAndNegotiation_NoOverlap_Min15_NoBold()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague(seed: 88));
            var hub = NewHub(sim);
            CheckLayer(hub.Root, "허브 헤더 · 8칸 서브 탭");
            hub.SelectMainTab(0);
            Assert.AreEqual("시즌 결산실", T(hub.Root, "SubTab6"));
            Assert.AreEqual("계약 협상실", T(hub.Root, "SubTab7"));
            Assert.AreEqual("스토리 안건", T(hub.Root, "SubTab5"), "기존 탭 순서 유지");
            hub.OpenSeasonSummary();
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneSeasonSummary, hub.CurrentPane);
            var summary = hub.Pane(GMOotpFrontOfficeUIController.PaneSeasonSummary);
            StringAssert.Contains("신뢰도 낮음", T(summary, "SummaryTeamPanel/SummaryConfidence"));
            StringAssert.Contains("신뢰도 낮음", T(summary, "SummaryPlayersPanel/SummaryRow0/C10"));
            CheckPane(hub, "시즌 결산실(2026 추정)");
            Click(summary, "SummaryPlayersPanel/SummaryNext");
            CheckPane(hub, "시즌 결산실 2쪽");

            hub.OpenNegotiationRoom();
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneNegotiation, hub.CurrentPane);
            var neg = hub.Pane(GMOotpFrontOfficeUIController.PaneNegotiation);
            if (hub.NegotiationSelected == null)
            {
                foreach (var p in sim.League.UserTeam.Roster.Take(3)) p.ContractYears = 1;
                hub.Refresh();
            }
            Assert.IsNotNull(hub.NegotiationSelected);
            hub.SelectNegotiationRow(0);
            Assert.IsNotNull(hub.NegotiationSession);
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(neg.Find($"NegCardsPanel/NegCard{i}").gameObject.activeSelf, $"카드 {i}");
                StringAssert.Contains("협상 진행 가능성", T(neg, $"NegCardsPanel/NegProgress{i}"));
                StringAssert.Contains("타결 시", T(neg, $"NegCardsPanel/NegDist{i}")); // [TASK-GM-17] 타결 조건(제시액 · 기간)
                StringAssert.Contains("재무팀장", T(neg, $"NegCardsPanel/NegFinance{i}"));
            }
            StringAssert.Contains("에이전트 성향", T(neg, "NegReportPanel/NegInfo"));
            StringAssert.Contains("충성도", T(neg, "NegReportPanel/NegInfo"));
            CheckPane(hub, "계약 협상실(카드 3장)");
            var result = hub.ChooseNegotiationCard(1);
            Assert.IsNotNull(result);
            Assert.IsNotEmpty(T(neg, "NegReportPanel/NegMessage"));
            CheckPane(hub, "계약 협상실(결과)");

            // 요약 행 → 협상실 이동
            hub.OpenSeasonSummary();
            Click(summary, "SummaryPlayersPanel/SummaryRow0");
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneNegotiation, hub.CurrentPane, "성과 리포트 행 → 계약 협상실");

            int bold = hub.GetComponentsInChildren<Text>(true).Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic || t.text.Contains("<b>"));
            Assert.AreEqual(0, bold, "Bold 0건");
            Assert.AreEqual(0, GMRaycastSanitizer.BlockingTexts(hub.GetComponentsInChildren<Graphic>(true)), "모바일 - 터치 가로채는 텍스트 0");
        }

        // ================================================================== 공통

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim)
        {
            var canvasGo = new GameObject("GM11_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvasGo);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var go = new GameObject("GM11_Hub", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var hub = go.AddComponent<GMOotpFrontOfficeUIController>();
            hub.Build();
            hub.Bind(sim);
            return hub;
        }

        private static string T(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.IsNotNull(t, path);
            var text = t.GetComponent<Text>() ?? t.GetComponentInChildren<Text>(true);
            Assert.IsNotNull(text, path);
            return text.text;
        }

        private static void Click(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.IsNotNull(t, path);
            t.GetComponent<Button>().onClick.Invoke();
        }

        private static void CheckPane(GMOotpFrontOfficeUIController hub, string label)
        {
            CheckLayer(hub.Root, label + " 헤더");
            var pane = hub.Pane(hub.CurrentPane);
            Assert.IsTrue(pane.gameObject.activeSelf, label);
            CheckLayer(pane, label + " " + hub.CurrentPane);
            foreach (Transform child in pane)
            {
                if (!child.gameObject.activeSelf) continue;
                if (!child.name.EndsWith("Panel") || child.GetComponent<Text>() != null || child.GetComponent<Button>() != null) continue;
                CheckLayer(child, label + " " + child.name);
                foreach (Transform row in child)
                    if (row.gameObject.activeSelf && row.GetComponent<Button>() != null && row.Find("C0") != null) CheckLayer(row, label + " " + row.name + " 셀");
            }
        }

        /// <summary>layer 직속 자식 중 활성 텍스트/버튼 영역 겹침 0 · 15pt 이상(지시서 14pt 이상) · Normal.</summary>
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
