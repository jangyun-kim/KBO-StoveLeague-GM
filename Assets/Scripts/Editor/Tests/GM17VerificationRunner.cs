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
    /// [TASK-GM-17] 자동 검증(Unity CLI BatchPipelineGM17 1회 실행):
    ///   1) 프런트 직원 리포트 - 데이터분석(취약 포지션 · 시장 대체 Top 3) · 재무(캡 게이지 · 경쟁균형세 위험도 · 가성비 최고/최악) · 선수관리(충성도 위험군 · 유대) · 보고 신뢰도
    ///   2) FA 시장 연계 - 협상 최종 결렬 / 방출 → FA 풀 · 유저 입찰에 AI 구단 역제안(금액 상승) · 추가 베팅 · 포기 · 트레이드 수락 게이지 단계
    ///   3) 유대 연쇄 - 대폭 삭감 타결 시 유대 동료 충성도 -5~-10 · 협상실 조력자 사전 경고 · 방출 시 유대 동료 반발
    ///   4) UI 무결성 - 직원 리포트 · 협상실 제시액 · FA 입찰 · 트레이드 게이지 · 연봉 현황 · 응원단 육성 · 클래식 로비 잔재 제거 · 포스터 사진 크기 통일
    /// </summary>
    public class GM17VerificationRunner
    {
        private static IReadOnlyList<PlayerTemplate> templates;
        private static List<Cheerleader> cheer;
        private static GameObject dbObject;
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void EnsureDatabase()
        {
            GMAudioManager.SimulationMode = false;
            var audio = GMAudioManager.Ensure();
            audio.StopAll();
            audio.ClearHistory();
            if (templates != null && templates.Count > 0 && templates.All(t => t != null)) return;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            dbObject = new GameObject("GM17_PlayerDatabase");
            templates = dbObject.AddComponent<PlayerDatabase>().AllTemplates.ToList();
            cheer = GMRosterLoader.AllCheerleaderTemplates();
            Assert.Greater(templates.Count, 500);
        }

        [OneTimeTearDown]
        public void Unload()
        {
            if (templates != null) foreach (var t in templates) if (t != null) Object.DestroyImmediate(t);
            templates = null;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            GMAudioManager.SimulationMode = false;
            var audio = GMAudioManager.Instance;
            if (audio != null) audio.StopAll();
            if (audio != null && (audio.gameObject.hideFlags & HideFlags.DontSave) != 0) Object.DestroyImmediate(audio.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created) if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
            GMAudioManager.SimulationMode = false;
        }

        private static GMLeagueState NewLeague(string team = "SAM", int seed = 1717)
        {
            var league = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        // ================================================================== 1) 프런트 직원 리포트

        [Test]
        public void T1_StaffReport_DataFinanceRelations_WeakPosition_Risks_Reliability()
        {
            var league = NewLeague();
            var team = league.UserTeam;
            var risky = team.Roster.OrderBy(p => p.InstanceId, StringComparer.Ordinal).First();
            risky.Loyalty = 22;
            var b = GMStaffReport.Build(league);
            Assert.IsNotNull(b);
            // 데이터분석팀장
            Assert.GreaterOrEqual(b.WeakPositions.Count, 1, "취약 포지션 1개 이상");
            Assert.IsTrue(b.WeakPositions.All(w => w.Percentile >= 1 && w.Percentile <= 99 && !string.IsNullOrEmpty(w.PositionLabel)));
            Assert.LessOrEqual(b.WeakPositions[0].Production - b.WeakPositions[0].LeagueProduction, b.WeakPositions.Last().Production - b.WeakPositions.Last().LeagueProduction, "가장 취약한 포지션이 맨 앞");
            Assert.IsNotEmpty(b.DataHeadline);
            Assert.GreaterOrEqual(b.Candidates.Count, 1, "시장 대체 후보");
            Assert.LessOrEqual(b.Candidates.Count, GMStaffReport.TopCandidates);
            Assert.IsTrue(b.Candidates.All(c => c.OvrGain > 0 && (c.IsFreeAgent ? league.FreeAgents.Contains(c.Player) : league.Teams.Values.Any(t => !t.IsUserTeam && t.Roster.Contains(c.Player)))), "현재 주전보다 나은 FA · 트레이드 후보");
            // 재무팀장
            Assert.AreEqual(team.Payroll, b.Payroll);
            Assert.AreEqual(team.PayrollCap, b.Cap);
            Assert.AreEqual(b.Payroll / (float)b.Cap, b.CapUsage, 1e-4);
            Assert.GreaterOrEqual(b.BestValue.Count, 1);
            Assert.GreaterOrEqual(b.WorstValue.Count, 1);
            Assert.GreaterOrEqual(b.BestValue[0].Ratio, b.WorstValue[0].Ratio, "가성비 최고 ≥ 최악");
            Assert.AreEqual(GMRiskLevel.Safe, GMStaffReport.RiskOf(0.5f));
            Assert.AreEqual(GMRiskLevel.Caution, GMStaffReport.RiskOf(0.95f));
            Assert.AreEqual(GMRiskLevel.Danger, GMStaffReport.RiskOf(1.05f));
            int cap = team.PayrollCap;
            team.PayrollCap = team.Payroll - 1000;
            var over = GMStaffReport.Build(league);
            Assert.AreEqual(GMRiskLevel.Danger, over.TaxRisk, "캡 초과 = 경쟁균형세 위험");
            StringAssert.Contains("경쟁균형세", over.TaxLine);
            StringAssert.Contains("샐러리캡 초과", over.AssistantLine, "운영팀장이 가장 급한 것으로 정리");
            team.PayrollCap = cap;
            // 선수관리팀장
            Assert.GreaterOrEqual(b.LoyaltyRisks.Count, 1, "충성도 위험군 1명 이상");
            Assert.IsTrue(b.LoyaltyRisks.Any(r => r.Player == risky));
            Assert.IsTrue(b.LoyaltyRisks.All(r => r.Loyalty < GMStaffReport.LoyaltyRiskLine));
            Assert.GreaterOrEqual(b.Bonds.Count, 1, "핵심 유대");
            Assert.IsTrue(b.Bonds.All(x => x.TrustDrop >= 2));
            // 보고 신뢰도 · 조력자
            foreach (int r in new[] { b.DataReliability, b.FinanceReliability, b.RelationsReliability }) Assert.That(r, Is.InRange(1, 100));
            StringAssert.Contains(GMAssistant.ProfileFor(league).Plate, b.AssistantLine);

            // 모달 UI - 3탭 · 진입점(결산실 · 툴바 · 헤더)
            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, out _);
            hub.OpenSeasonSummary();
            Click(hub.Pane(GMOotpFrontOfficeUIController.PaneSeasonSummary), "SummaryStaffPanel/SummaryStaffReport");
            Assert.IsTrue(hub.IsStaffReportOpen, "결산실 [프런트 직원 리포트]");
            var popup = hub.Root.Find(GMOotpFrontOfficeUIController.StaffReportName);
            for (int t = 0; t < GMOotpFrontOfficeUIController.StaffTabs; t++)
            {
                Click(popup, $"SrTab{t}");
                Assert.AreEqual(t, hub.StaffReportTab);
                var group = popup.Find(new[] { "SrDataGroup", "SrFinanceGroup", "SrRelationsGroup" }[t]);
                Assert.IsTrue(group.gameObject.activeSelf);
                StringAssert.Contains("보고 신뢰도", T(group, $"SrReliability{t}"));
                CheckLayer(popup, $"직원 리포트 탭 {t}");
                CheckLayer(group, $"직원 리포트 탭 {t} 내용");
            }
            StringAssert.Contains("백분위", T(popup, "SrDataGroup/SrPosValue0"));
            StringAssert.Contains("경쟁균형세", T(popup, "SrFinanceGroup/SrTax"));
            StringAssert.Contains("충성도", T(popup, "SrRelationsGroup/SrRisk0"));
            Click(popup, "SrClose");
            Assert.IsFalse(hub.IsStaffReportOpen);
            Click(hub.Root, "FinanceButton");
            Assert.IsTrue(hub.IsStaffReportOpen, "헤더 [재정 · 직원 리포트]");
            Assert.AreEqual(1, hub.StaffReportTab, "재무팀장 탭");
            hub.CloseStaffReport();
        }

        // ================================================================== 2) FA 시장 연계 · 입찰 경쟁 · 트레이드 게이지

        [Test]
        public void T2_FinalBreakAndRelease_ToFAPool_BiddingWar_CounterBid_TradeGauge()
        {
            var league = NewLeague("LG", 1720);
            var team = league.UserTeam;
            // 협상 최종 결렬 → FA 풀
            Player broken = null;
            foreach (var cand in team.Roster.OrderByDescending(p => p.BaseOverall).Take(6).ToList())
            {
                cand.ContractYears = 1;
                for (int i = 0; i < GMNegotiationRoom.MaxStrikes; i++)
                {
                    var s = GMNegotiationRoom.Open(league, team, cand, 0, 0, 1);
                    var r = GMNegotiationRoom.Resolve(league, team, s, -1, rollOverride: 0.999);
                    Assert.IsFalse(r.Success);
                    if (i < GMNegotiationRoom.MaxStrikes - 1) Assert.IsTrue(r.Stalled, "1 · 2회 실패 = 결렬 위기(양보 후 재협상)");
                    else { Assert.IsTrue(r.Broken, "3회 실패 = 최종 결렬"); Assert.IsTrue(r.MovedToFA); }
                }
                broken = cand;
                break;
            }
            Assert.IsNotNull(broken);
            Assert.Contains(broken, league.FreeAgents, "최종 결렬 = FA 풀 이동");
            Assert.IsFalse(team.ReservePlayers.Contains(broken));
            Assert.IsTrue(league.News.Any(n => n.Title.Contains(broken.Template.PlayerName) && n.Title.Contains("결렬")));
            // 방출 → FA 풀
            var cut = team.Roster.Where(p => !p.IsCaptain).OrderBy(p => p.BaseOverall).First();
            Assert.IsTrue(GMStoveLeagueMarket.Release(league, team, cut).Success);
            Assert.Contains(cut, league.FreeAgents, "방출 = FA 풀 이동");

            // 입찰 경쟁 - 포지션이 취약한 AI 구단의 역제안(금액 상승)
            foreach (var t in league.Teams.Values.Where(t => !t.IsUserTeam)) t.Budget += 3000000;
            GMFaBidSession bid = null;
            foreach (var fa in league.FreeAgents.OrderByDescending(p => p.BaseOverall).ToList())
            {
                var s = GMMarketBidding.Open(league, team, fa);
                if (s.HasRival && s.RivalMax > s.RivalBid + 600) { bid = s; break; }
            }
            Assert.IsNotNull(bid, "경쟁 구단이 붙는 FA");
            var target = bid.Player;
            int rival0 = bid.RivalBid;
            double factor = GMMarketBidding.Effective(league, team, target, 100000, bid.Years, false) / 100000.0;
            int salary = (int)Math.Ceiling((rival0 + 200) / factor / 100.0) * 100;
            GMMarketBidding.Bid(league, team, bid, salary, bid.Years, false);
            Assert.AreEqual(GMFaBidState.CounterBid, bid.State, string.Join(" / ", bid.Log));
            Assert.Greater(bid.RivalBid, rival0, "AI 역제안 = 입찰 금액 상승");
            Assert.LessOrEqual(bid.RivalBid, bid.RivalMax, "AI 한도 내");
            Assert.IsTrue(bid.Log.Any(l => l.Contains("역제안")));
            Assert.Contains(target, league.FreeAgents, "역제안 단계 = 아직 계약 전");
            // 포기 = 경쟁 구단 영입
            string winner = GMMarketBidding.Withdraw(league, bid);
            if (winner != null) Assert.IsTrue(league.Teams[winner].Roster.Contains(target), "포기 = 경쟁 구단이 영입");
            Assert.AreEqual(GMFaBidState.Withdrawn, bid.State);

            // 허브 FA 화면 - [계약 제시] = 입찰 1R · [추가 베팅] → 한도 돌파 시 영입
            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, out _);
            hub.SelectMainTab(4);
            Player uiTarget = null;
            GMFaBidSession probe = null;
            foreach (var fa in league.FreeAgents.OrderByDescending(p => p.BaseOverall).ToList())
            {
                probe = GMMarketBidding.Open(league, team, fa);
                if (probe.HasRival && probe.RivalMax > probe.RivalBid + 600) { uiTarget = fa; break; }
            }
            Assert.IsNotNull(uiTarget);
            hub.SelectFreeAgent(uiTarget);
            double f2 = GMMarketBidding.Effective(league, team, uiTarget, 100000, Math.Min(GMStoveLeagueMarket.MaxFAYears, GMStoveLeagueMarket.PreferredYears(uiTarget)), false) / 100000.0;
            hub.SetFATerms(Math.Min(GMStoveLeagueMarket.MaxFAYears, GMStoveLeagueMarket.PreferredYears(uiTarget)), (int)Math.Ceiling((probe.RivalBid + 200) / f2 / 100.0) * 100, false);
            var first = hub.OfferSelected();
            Assert.IsNotNull(first);
            var faPane = hub.Pane(GMOotpFrontOfficeUIController.PaneFA);
            if (!first.Success)
            {
                Assert.AreEqual(GMFaBidState.CounterBid, hub.FABidSession.State);
                StringAssert.Contains("역제안", T(faPane, "FAMessage"));
                StringAssert.Contains("입찰 경쟁", T(faPane, "FABidStatus"));
                Assert.IsTrue(faPane.Find("FARaise").GetComponent<Button>().interactable, "[추가 베팅] 활성");
                CheckPane(hub, "FA 입찰 경쟁(역제안)");
                GMNegotiationResult raised = null;
                for (int i = 0; i < 12 && hub.FABidSession != null && hub.FABidSession.State == GMFaBidState.CounterBid; i++) raised = hub.RaiseFABid();
                Assert.IsNotNull(raised);
                Assert.IsTrue(raised.Success, "추가 베팅으로 AI 한도 돌파 = 영입: " + raised.Message);
            }
            Assert.Contains(uiTarget, team.Roster, "입찰 승리 = 로스터 합류");

            // 트레이드 - 상대 단장 수락 게이지(타결점 100%) · 1:N 역제안이 게이지를 채워 가는 단계
            hub.SelectSubTab(1);
            var trPane = hub.Pane(GMOotpFrontOfficeUIController.PaneTrade);
            var partner = league.Teams["KIA"];
            var star = partner.Roster.OrderByDescending(p => p.BaseOverall).First();
            hub.SetTradeSlots(null, "KIA", new[] { star });
            StringAssert.Contains("수락 게이지", T(trPane, "TrValueLabel"));
            Assert.IsNotNull(trPane.Find("TrValueBar/AcceptTick"), "타결점 눈금");
            var offer = hub.RequestCounterOffer();
            Assert.IsNotNull(offer);
            if (offer.Valid)
            {
                var steps = hub.CounterOfferSteps(offer);
                Assert.AreEqual(offer.Requested.Count + 1 + (offer.CashSubsidy > 0 ? 1 : 0), steps.Count, "패키지 한 명씩 단계");
                Assert.Greater(steps.Last(), steps.First(), "게이지가 타결점 쪽으로 올라감");
                var counter = hub.Root.Find(GMOotpFrontOfficeUIController.CounterPopupName);
                StringAssert.Contains("→", T(counter, "CounterValue"));
                StringAssert.Contains("타결점 100%", T(counter, "CounterValue"));
                CheckLayer(counter, "AI 역제안 게이지");
                hub.CloseCounterPopup();
            }
            CheckPane(hub, "트레이드 수락 게이지");
        }

        // ================================================================== 3) 유대 연쇄

        [Test]
        public void T3_BondChain_CutSettlementLowersBondedLoyalty_AssistantWarning_Release()
        {
            var league = NewLeague("SAM", 1730);
            var team = league.UserTeam;
            var p = team.Roster.Where(x => GMSalaryChain.BondedOf(team, x).Count > 0).OrderByDescending(x => x.Salary).First();
            var bonded = GMSalaryChain.BondedOf(team, p).Select(b => b.partner).ToList();
            foreach (var b in bonded) b.Loyalty = 70;
            Assert.AreEqual(5, GMSalaryChain.CutLoyaltyDrop(10000, 9500), "5% 삭감 = -5");
            Assert.AreEqual(10, GMSalaryChain.CutLoyaltyDrop(10000, 6000), "40% 삭감 = -10(상한)");
            Assert.That(GMSalaryChain.CutLoyaltyDrop(10000, 8000), Is.InRange(5, 10));
            Assert.IsEmpty(GMSalaryChain.PreviewCut(league, team, p, p.Salary, p.Salary), "동결 = 경고 없음");

            // 협상실 - 삭감 제시 시 조력자 사전 경고 → 타결 강제 = 유대 동료 충성도 동반 하락
            p.ContractYears = 1;
            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, out _);
            hub.OpenNegotiationRoom(p);
            Assert.AreSame(p, hub.NegotiationSelected);
            int old = p.Salary;
            hub.SetNegotiationOffer(1); // 최저(요구액의 70%) = 현재 연봉보다 낮은 삭감 제시
            int offer = hub.NegotiationOffer;
            Assert.Less(offer, old, "삭감 제시");
            StringAssert.Contains(bonded[0].Template.PlayerName, hub.NegotiationWarning, "유대 동료 이름");
            StringAssert.Contains("유대", hub.NegotiationWarning);
            StringAssert.Contains(GMAssistant.ProfileFor(league).Plate, hub.NegotiationWarning, "운영팀장 경고");
            var neg = hub.Pane(GMOotpFrontOfficeUIController.PaneNegotiation);
            Assert.AreEqual(hub.NegotiationWarning, T(neg, "NegReportPanel/NegWarning"));
            StringAssert.Contains("제시 연봉", T(neg, "NegReportPanel/NegBaseline"));
            CheckPane(hub, "계약 협상실(삭감 경고)");
            var s = GMNegotiationRoom.Open(league, team, p, 0, 0, offer);
            var r = GMNegotiationRoom.Resolve(league, team, s, -1, rollOverride: 0.0);
            Assert.IsTrue(r.Success, r.Message);
            Assert.AreEqual(offer, p.Salary, "삭감 연봉 타결");
            Assert.AreEqual(GMNegotiationOutcome.Cut, r.Outcome);
            int drop = GMSalaryChain.CutLoyaltyDrop(old, offer);
            Assert.That(drop, Is.InRange(5, 10));
            foreach (var b in bonded) Assert.AreEqual(70 - drop, b.Loyalty, $"{b.Template.PlayerName} 충성도 동반 하락 -{drop}");
            Assert.IsTrue(r.ChainEffects.Any(e => e.Contains("유대 동료")));

            // 방출 - 사전 경고 · 유대 동료 반발(-8) · 선수단 신뢰도 하락
            var leaver = team.Roster.Where(x => x != p && !x.IsCaptain && GMSalaryChain.BondedOf(team, x).Count > 0).OrderBy(x => x.BaseOverall).First();
            var mates = GMSalaryChain.BondedOf(team, leaver).Select(b => b.partner).ToList();
            var before = mates.ToDictionary(m => m, m => m.Loyalty);
            int trust = team.LockerRoomTrust;
            hub.SelectMainTab(0);
            hub.SelectSubTab(1);
            hub.SelectExtension(leaver);
            StringAssert.Contains("유대", T(hub.Pane(GMOotpFrontOfficeUIController.PaneSalaries), "ExtWarning"), "방출 전 조력자 경고");
            var rel = hub.ReleaseSelected();
            Assert.IsTrue(rel.Success, rel.Message);
            foreach (var m in mates) Assert.AreEqual(before[m] - GMSalaryChain.DepartureLoyalty, m.Loyalty, "방출 = 유대 동료 충성도 -8");
            Assert.AreEqual(Math.Max(0, trust - GMSalaryChain.DepartureTrustPerBond * mates.Count), team.LockerRoomTrust);
            Assert.Contains(leaver, league.FreeAgents);
        }

        // ================================================================== 4) UI 무결성 · 잔재 제거 · 포스터

        [Test]
        public void T4_UI_StaffReport_Negotiation_FA_Salaries_Cheer_NoLegacy_NoOverlap_Min15()
        {
            var league = NewLeague("SAM", 1740);
            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, out var canvas);
            var team = league.UserTeam;

            // 클래식 로비 잔재 제거 · 재정 현황
            Assert.IsNull(hub.Root.Find("LobbyButton"), "헤더 [클래식 로비] 제거");
            Assert.IsNotNull(hub.Root.Find("FinanceButton"));
            StringAssert.Contains("운영 예산", T(hub.Root, "ToolbarInfo2"), "재정 현황 상시 표시");
            StringAssert.Contains("FA 가용", T(hub.Root, "ToolbarInfo2"));
            Assert.AreEqual(0, hub.GetComponentsInChildren<Text>(true).Count(t => t.text.Contains("클래식 로비")), "화면 문자열 0");
            hub.SelectMainTab(6);
            var cheerLabels = Enumerable.Range(0, GMOotpFrontOfficeUIController.SubTabMax).Select(k => hub.Root.Find($"SubTab{k}")).Where(t => t.gameObject.activeSelf).Select(t => t.GetComponentInChildren<Text>().text).ToList();
            Assert.IsFalse(cheerLabels.Any(l => l.Contains("영입") || l.Contains("보유")), "응원단 탭 클래식 영입 · 보유 제거: " + string.Join(" / ", cheerLabels));
            var cheerPane = hub.Pane(GMOotpFrontOfficeUIController.PaneCheer);
            Assert.IsNull(cheerPane.Find("CeSynergyPanel/CeOpenShop"));
            Assert.IsNull(cheerPane.Find("CeSynergyPanel/CeOpenInventory"));
            CheckPane(hub, "응원단 포스터");

            // 포스터 사진 크기 통일(6칸 사진 높이 차 4% 이내)
            var heights = Enumerable.Range(0, 6).Select(i => PhotoHeight(cheerPane.Find($"CePosterPanel/CeSlot{i}/PhotoMask"))).ToList();
            Assert.Less(heights.Max() / heights.Min(), 1.04f, "포스터 6칸 사진 크기 통일: " + string.Join(", ", heights.Select(h => h.ToString("0.0"))));

            // 응원단 육성(강화) - 운영 예산 사용
            var view = NewView<GMCheerleaderEntryUIController>(canvas, "GM17_Cheer");
            view.Build();
            view.Open(team);
            var c = team.CheerEntry.First();
            view.Select(c);
            int lv = CheerGrowth.Reinforce(c), cheer0 = GMCheerleaderStats.Cheer(c);
            long budget = team.Budget;
            Click(view.Root, "ReinforceButton");
            Assert.AreEqual(lv + 1, CheerGrowth.Reinforce(c), "육성 +1강");
            Assert.AreEqual(budget - CheerGrowth.ReinforceCost(lv), team.Budget, "육성비 = 운영 예산 차감");
            Assert.Greater(GMCheerleaderStats.Cheer(c), cheer0, "CHEER 상승");
            StringAssert.Contains("육성", view.LastMessage);
            CheckLayer(view.Root, "응원단 엔트리(육성 버튼)");
            var eh = Enumerable.Range(0, 6).Select(i => PhotoHeight(view.Root.Find($"Slot{i}/PhotoMask"))).ToList();
            Assert.Less(eh.Max() / eh.Min(), 1.04f, "엔트리 포스터 사진 크기 통일");
            view.Close();

            // 연봉 현황 · 협상실 · FA · 직원 리포트
            hub.SelectMainTab(0);
            hub.SelectSubTab(1);
            Assert.IsNull(hub.Pane(GMOotpFrontOfficeUIController.PaneSalaries).Find("ExtSubmit"));
            Assert.IsNotNull(hub.Pane(GMOotpFrontOfficeUIController.PaneSalaries).Find("ExtToNegotiation"));
            hub.SelectExtension(team.Roster[0]);
            CheckPane(hub, "연봉 현황 · 주장 · 방출");
            foreach (var p in team.Roster.Take(3)) p.ContractYears = 1;
            hub.OpenNegotiationRoom(team.Roster[0]);
            hub.SetNegotiationOffer(team.Roster[0].Salary * 2);
            CheckPane(hub, "계약 협상실(제시액 슬라이더)");
            hub.SelectMainTab(4);
            hub.SelectFreeAgent(league.FreeAgents.OrderByDescending(p => p.BaseOverall).First());
            hub.OfferSelected();
            CheckPane(hub, "FA 입찰");
            for (int t = 0; t < 3; t++)
            {
                hub.OpenStaffReport(t);
                CheckLayer(hub.Root.Find(GMOotpFrontOfficeUIController.StaffReportName), $"직원 리포트 {t}");
            }
            hub.CloseStaffReport();
            int bold = hub.GetComponentsInChildren<Text>(true).Concat(view.GetComponentsInChildren<Text>(true)).Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic || t.text.Contains("<b>"));
            Assert.AreEqual(0, bold, "Bold 0건");
            Assert.AreEqual(0, GMRaycastSanitizer.BlockingTexts(hub.GetComponentsInChildren<Graphic>(true)), "모바일 - 터치 가로채는 텍스트 0");
        }

        // ================================================================== 공통

        private static float PhotoHeight(Transform t)
        {
            Assert.IsNotNull(t, "PhotoMask");
            var corners = new Vector3[4];
            ((RectTransform)t).GetWorldCorners(corners);
            return Mathf.Abs(corners[1].y - corners[0].y);
        }

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim, out GameObject canvas)
        {
            canvas = new GameObject("GM17_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvas);
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var hub = NewView<GMOotpFrontOfficeUIController>(canvas, "GM17_Hub");
            hub.Build();
            var prePost = NewView<GMMatchPrePostUIController>(canvas, "GM17_PrePost");
            prePost.Build();
            prePost.gameObject.SetActive(false);
            hub.PrePostView = prePost;
            hub.Bind(sim);
            return hub;
        }

        private static T NewView<T>(GameObject canvas, string name) where T : MonoBehaviour
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return go.AddComponent<T>();
        }

        private static void Click(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.IsNotNull(t, path);
            t.GetComponent<Button>().onClick.Invoke();
        }

        private static string T(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.IsNotNull(t, path);
            var text = t.GetComponent<Text>() ?? t.GetComponentInChildren<Text>(true);
            Assert.IsNotNull(text, path);
            return text.text;
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
            int bold = hub.GetComponentsInChildren<Text>(true).Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic || t.text.Contains("<b>"));
            Assert.AreEqual(0, bold, $"{label} Bold 0건");
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
