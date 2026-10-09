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
    /// [TASK-GM-18] 자동 검증(Unity CLI BatchPipelineGM18 1회 실행):
    ///   1) 경기 중 BGM 교체 0회 - 관중 앰비언스 고정 · 응원가/이벤트 하이재킹 차단 · 타석 효과음(SFX)만
    ///   2) 협상 - 유망주(WAR 0.5) 인상 상한 · 제시액 ≥ 요구액 = 타결 100% · 충성도 연동 희망 기간 · 저충성 요구액 프리미엄
    ///   3) 7/31 트레이드 · 영입 마감(8/1 이후 차단) · 스토브리그 Turn 잠금 = CanvasGroup 입력 차단
    ///   4) 예산 하드 락(캡 초과 → FA [계약 제시] 버튼 Block) · 개막 신임도 -20 · 응원단 마케팅 예산 분리
    ///   5) 시즌 중 단장 개입 - 주간 종료 후에만 사건(주당 ≤ 3 · 시즌 ≤ 12 · 시즌 4건 이상) · 결정 로그 · 주간 진행 · 팝업 UI
    ///   6) 단장 커리어 - 임기 말 재계약/해임 · 30시즌 은퇴 · 해금 · 엔딩 후 연도 전환 차단 · 뉴게임+
    ///   7) 1920×1080 UI - 기록 태그([실제 KBO 기록] · [시뮬레이션 기록]) · 시즌 팝업 · 시장 잠금 텍스트 겹침 0 · 15pt 이상 · Bold 0
    /// </summary>
    public class GM18VerificationRunner
    {
        private static IReadOnlyList<PlayerTemplate> templates;
        private static List<Cheerleader> cheer;
        private static GameObject dbObject;
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void EnsureDatabase()
        {
            GMAudioManager.SimulationMode = false;
            GMAudioManager.LiveMatchQuiet = false;
            var audio = GMAudioManager.Ensure();
            audio.StopAll();
            audio.ClearHistory();
            if (templates != null && templates.Count > 0 && templates.All(t => t != null)) return;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            dbObject = new GameObject("GM18_PlayerDatabase");
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
            GMAudioManager.LiveMatchQuiet = false;
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
            GMAudioManager.LiveMatchQuiet = false;
        }

        private static GMLeagueState NewLeague(string team = "SAM", int seed = 1818)
        {
            var league = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        // ================================================================== 1) 경기 중 BGM 교체 차단

        [Test]
        public void T1_LiveMatch_NoBgmChange_AmbienceOnly_SfxOnAtBats()
        {
            var audio = GMAudioManager.Ensure();
            var sim = new GMLiveSeasonSimulator(NewLeague("SAM", 1801));
            var hub = NewHub(sim, out var canvas, out var prePost, out _);
            Assert.IsTrue(prePost.ShowPreGameView(sim));
            Assert.IsNull(audio.ActiveEvent, "전력 분석 = 이벤트곡 하이재킹 없음(라인업송 폐지)");
            Assert.IsTrue(prePost.PlayBall());
            Assert.IsTrue(GMAudioManager.LiveMatchQuiet, "플레이 볼 = 경기 중 정숙 모드");
            Assert.AreEqual(TeamAudioProfile.SynthCrowd, audio.CurrentBgmKey, "경기 BGM = 관중 앰비언스");
            int starts = audio.BgmTrackStarts;
            int blocked = GMAudioManager.BlockedMatchRequests;
            Assert.IsNull(audio.PlayEvent(GMAudioEvent.MajorResult, "SAM"), "경기 중 이벤트 BGM 하이재킹 차단");
            Assert.IsNull(audio.PlayCue(GMAudioCue.HighlightSong, "SAM"), "경기 중 응원가(곡 채널) 차단");
            audio.EnterScreen(GMAudioScreen.Hub, "SAM");
            Assert.AreEqual(GMAudioScreen.Match, audio.CurrentScreen, "뒤편 허브 갱신이 경기 BGM을 바꾸지 못한다");
            Assert.Greater(GMAudioManager.BlockedMatchRequests, blocked);
            var sfx = new HashSet<GMAudioCue>();
            int steps = 0;
            for (int i = 0; i < 400 && !prePost.Session.IsOver; i++)
            {
                prePost.LiveStepAtBat();
                steps++;
                if (prePost.LastExtraCue.HasValue) sfx.Add(prePost.LastExtraCue.Value);
            }
            Assert.Greater(steps, 40, "경기 타석 진행");
            Assert.AreEqual(starts, audio.BgmTrackStarts, "경기 중 BGM 곡 교체 0회");
            Assert.AreEqual(TeamAudioProfile.SynthCrowd, audio.CurrentBgmKey, "경기 끝까지 앰비언스 고정");
            Assert.IsFalse(audio.Requests.Any(r => GMAudioManager.IsSongCue(r.cue)), "응원가 · 아웃송 요청 0건");
            Assert.IsTrue(sfx.All(c => c == GMAudioCue.CrowdCheer || c == GMAudioCue.OutCheer), "타석 연출 = 효과음(환호 · 박수)만");
            Assert.IsTrue(sfx.Count > 0, "효과음 발생");
            Assert.IsTrue(audio.History.Any(h => h.cue == GMAudioCue.OutCheer || h.cue == GMAudioCue.CrowdCheer), "OneShot SFX 기록");
            Assert.IsTrue(prePost.FinishLiveMatch());
            Assert.IsFalse(GMAudioManager.LiveMatchQuiet, "경기 종료 = 정숙 모드 해제(결과 BGM 허용)");
            TestContext.WriteLine($"[GM18 오디오] 타석 {steps} · 효과음 {string.Join(", ", sfx)} · 차단 요청 {GMAudioManager.BlockedMatchRequests} · 결과 이벤트 {prePost.LastResultEvent}");
            prePost.CloseAll();
        }

        // ================================================================== 2) 협상 · 연봉

        [Test]
        public void T2_Negotiation_ProspectRaiseCap_FullOfferAlwaysAgrees_LoyaltyYears()
        {
            var league = NewLeague("LG", 1802);
            var team = league.UserTeam;
            var diff = GMFrontOffice.Ensure(league).Difficulty;

            // 유망주 인상 상한 - OVR 65 미만 · 지난 시즌 WAR 0.5 · 연봉 3,000만 → 요구 인상폭 ≤ min(30%, 5,000만)
            var kid = team.ReservePlayers.Where(p => p.BaseOverall < GMStoveLeagueMarket.ProspectCapOvr).OrderBy(p => p.BaseOverall).First();
            kid.Salary = Player.MinSalary;
            kid.Loyalty = 55;
            Assert.IsTrue(GMStoveLeagueMarket.IsRaiseCapTarget(kid, 0.5));
            int capped = GMStoveLeagueMarket.ExtensionDemand(kid, diff, 0.5);
            int cap = GMStoveLeagueMarket.RaiseCapOf(kid);
            Assert.AreEqual(3900, cap, "3,000만 → 상한 3,900만(+30%)");
            Assert.LessOrEqual(capped, cap, "유망주 요구액 ≤ 인상 상한");
            Assert.LessOrEqual(GMNegotiationRoom.DemandOf(league, kid), cap, "협상실 요구액도 상한 적용");
            var star = team.Roster.OrderByDescending(p => p.BaseOverall).First();
            star.Loyalty = 55;
            Assert.IsFalse(GMStoveLeagueMarket.IsRaiseCapTarget(star, 3.0), "주전급(OVR 65 이상 · WAR 1.0 이상)은 상한 없음");
            var mid = Player.MinSalary * 4; // 1억 2,000만 저성과 선수 = 인상폭 3,600만(30%)
            kid.Salary = mid;
            Assert.AreEqual(mid + mid * 3 / 10, GMStoveLeagueMarket.RaiseCapOf(kid));
            kid.Salary = 30000; // 3억 저성과 선수 = 인상폭 5,000만 상한
            Assert.AreEqual(35000, GMStoveLeagueMarket.RaiseCapOf(kid));
            kid.Salary = Player.MinSalary;
            TestContext.WriteLine($"[GM18 협상] 유망주 {kid.Template.PlayerName} OVR {kid.BaseOverall} · 요구 {capped} / 상한 {cap} · 무상한 기준 연봉 {Player.ComputeSalary(kid.BaseOverall, kid.EgoLevel, 0)}");

            // 제시액 ≥ 요구액 = 진행 가능성 100% · 판정 없이 타결
            foreach (var p in team.Roster.OrderByDescending(x => x.BaseOverall).Take(3)) p.ContractYears = 1;
            var target = GMNegotiationRoom.Targets(team).First();
            int demand = GMNegotiationRoom.DemandOf(league, target);
            var s = GMNegotiationRoom.Open(league, team, target, 0, 0, demand);
            Assert.AreEqual(1f, s.Baseline.Progress, 1e-6, "제시액 = 요구액 → 100%");
            Assert.IsTrue(s.Forecasts.All(f => Math.Abs(f.Progress - 1f) < 1e-6), "카드 예측도 100%");
            var over = GMNegotiationRoom.Open(league, team, target, 0, 0, demand * 11 / 10);
            Assert.AreEqual(1f, over.Baseline.Progress, 1e-6, "요구액 초과 제시 → 100%");
            var low = GMNegotiationRoom.Open(league, team, target, 0, 0, demand * 9 / 10);
            Assert.Less(low.Baseline.Progress, 1f, "깎을 때만 확률이 낮아진다");
            var r = GMNegotiationRoom.Resolve(league, team, s, -1, rollOverride: 0.9999);
            Assert.IsTrue(r.Success, "최악의 판정값에서도 요구액 수용 = 타결: " + r.Message);
            Assert.AreEqual(GMNegotiationOutcome.AcceptDemand, r.Outcome);

            // 충성도 연동 희망 기간 · 저충성 프리미엄
            var probe = team.Roster.First(p => p.Age <= 30 && p != target);
            probe.Loyalty = 30;
            Assert.That(GMStoveLeagueMarket.PreferredYears(probe), Is.InRange(1, 2), "충성도 40 미만 = 1~2년 단기");
            int lowDemand = GMStoveLeagueMarket.ExtensionDemand(probe, diff, 3.0);
            probe.Loyalty = 80;
            Assert.That(GMStoveLeagueMarket.PreferredYears(probe), Is.InRange(4, 5), "충성도 70 이상 = 4~5년 장기");
            int highDemand = GMStoveLeagueMarket.ExtensionDemand(probe, diff, 3.0);
            Assert.GreaterOrEqual(lowDemand, highDemand, "충성도가 낮으면 연봉을 더 높게 부른다");
            var vet = team.Roster.First(p => p.Age > 30);
            vet.Loyalty = 20;
            Assert.AreEqual(1, GMStoveLeagueMarket.PreferredYears(vet), "30세 초과 저충성 = 1년");
            vet.Loyalty = 90;
            Assert.AreEqual(4, GMStoveLeagueMarket.PreferredYears(vet), "30세 초과 고충성 = 4년");
            vet.Loyalty = 55;
            Assert.AreEqual(vet.Age <= 33 ? 2 : 1, GMStoveLeagueMarket.PreferredYears(vet), "중간 충성도 = 나이 기준 유지");
        }

        // ================================================================== 3) 7/31 마감 · Turn 잠금

        [Test]
        public void T3_TradeDeadline_Aug1Locks_TurnLock_CanvasGroupBlocks()
        {
            var league = NewLeague("SAM", 1803);
            var team = league.UserTeam;
            int year = league.SeasonYear;
            int aug = Enumerable.Range(0, GMLiveSeasonSimulator.SeasonGames).First(d => GMLiveSeasonSimulator.DateOf(d, year) > new DateTime(year, 7, 31));
            Assert.GreaterOrEqual(GMLiveSeasonSimulator.DateOf(aug, year).Month, 8);
            Assert.IsFalse(GMLeagueRules.IsPastTradeDeadline(league), "스토브리그 = 열림");
            league.Phase = GMSeasonPhase.RegularSeason;
            league.GamesPlayed = aug - 1;
            Assert.IsFalse(GMLeagueRules.IsPastTradeDeadline(league), $"{GMLiveSeasonSimulator.DateOf(aug - 1, year):MM/dd} = 마감 전");
            Assert.IsTrue(GMFrontOffice.CanTrade(league, out _));

            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, out var canvas, out _, out _);
            hub.SelectMainTab(4);
            hub.SelectSubTab(1);
            Assert.IsTrue(hub.IsTradeProposeInteractable, "마감 전 [트레이드 제안] 활성");

            league.GamesPlayed = aug; // 8월 1일 이후
            Assert.IsTrue(GMLeagueRules.IsPastTradeDeadline(league), $"{GMLiveSeasonSimulator.DateOf(aug, year):MM/dd} = 마감 후");
            Assert.IsFalse(GMFrontOffice.CanTrade(league, out var why));
            StringAssert.Contains("7월 31일", why);
            var other = league.Teams.Values.First(t => !t.IsUserTeam);
            var trade = GMStoveLeagueMarket.ExecuteTrade(league, team, new[] { team.Roster.OrderBy(p => p.BaseOverall).First() }, other, new[] { other.Roster.OrderBy(p => p.BaseOverall).First() });
            Assert.IsFalse(trade.Success, "8/1 이후 트레이드 실행 차단");
            Assert.IsFalse(GMFrontOffice.CanSignFreeAgent(league, out _), "8/1 이후 영입 차단");
            hub.Refresh();
            Assert.IsFalse(hub.IsTradeProposeInteractable, "마감 후 [트레이드 제안] 잠금");
            StringAssert.Contains("7/31", T(hub.Pane(GMOotpFrontOfficeUIController.PaneTrade), "TrPropose"));
            CheckPane(hub, "트레이드(7/31 마감)");
            hub.SelectSubTab(0);
            if (league.FreeAgents.Count > 0) hub.SelectFreeAgent(league.FreeAgents[0]);
            Assert.IsFalse(hub.IsFAOfferInteractable, "마감 후 FA [계약 제시] 잠금");
            CheckPane(hub, "FA(7/31 마감)");
            league.Phase = GMSeasonPhase.PostSeason;
            Assert.IsTrue(GMLeagueRules.IsPastTradeDeadline(league), "포스트시즌까지 잠금 유지");
            league.Phase = GMSeasonPhase.StoveLeague;
            league.GamesPlayed = 0;
            Assert.IsFalse(GMLeagueRules.IsPastTradeDeadline(league), "스토브리그 = 다시 열림");

            // 스토브리그 Turn 잠금 - 잠긴 방 = CanvasGroup 입력 원천 차단 · 잠금 막 Raycast 흡수
            GMStoveTurns.Begin(league);
            hub.Refresh();
            hub.SelectMainTab(4);
            hub.SelectSubTab(0);
            Assert.IsTrue(hub.IsPaneLocked(GMOotpFrontOfficeUIController.PaneFA), "Turn 1 = FA 방 잠김");
            Assert.IsTrue(hub.IsTurnLockShown);
            Assert.IsTrue(hub.IsPaneInputBlocked(GMOotpFrontOfficeUIController.PaneFA), "잠긴 방 CanvasGroup blocksRaycasts = false");
            Assert.IsTrue(hub.IsPaneInputBlocked(GMOotpFrontOfficeUIController.PaneTrade), "트레이드 방도 차단");
            var group = hub.Pane(GMOotpFrontOfficeUIController.PaneFA).GetComponent<CanvasGroup>();
            Assert.IsFalse(group.blocksRaycasts);
            Assert.IsFalse(group.interactable);
            var lockBg = hub.Root.Find(GMOotpFrontOfficeUIController.TurnLockName + "/LockBg").GetComponent<Image>();
            Assert.IsTrue(lockBg.raycastTarget, "잠금 막이 클릭을 흡수");
            Assert.IsFalse(hub.IsPaneInputBlocked(GMOotpFrontOfficeUIController.PaneOwner), "열린 방은 입력 가능");
            GMFrontOffice.Ensure(league).StoveTurn = 5; // 시장 협상 Turn
            hub.Refresh();
            Assert.IsFalse(hub.IsPaneInputBlocked(GMOotpFrontOfficeUIController.PaneFA), "Turn 5 해금 = 입력 복구");
        }

        // ================================================================== 4) 예산 하드 락 · 마케팅 예산

        [Test]
        public void T4_BudgetHardLock_FAButtonBlocked_OpeningTrustPenalty_MarketingBudget()
        {
            var league = NewLeague("KIA", 1804);
            var team = league.UserTeam;
            var fo = GMFrontOffice.Ensure(league);
            Assert.IsFalse(GMLeagueRules.FreeAgencyLocked(league, team, out _), "정상 예산 = 열림");
            int cap = team.PayrollCap;
            team.PayrollCap = team.Payroll - 1000; // 샐러리캡 초과 강제 주입
            Assert.IsTrue(GMLeagueRules.IsOverBudget(team));
            Assert.IsTrue(GMLeagueRules.FreeAgencyLocked(league, team, out var reason));
            StringAssert.Contains("샐러리캡", reason);
            var fa = league.FreeAgents.OrderByDescending(p => p.BaseOverall).First();
            var offer = GMStoveLeagueMarket.OfferContract(league, team, fa, 2, GMStoveLeagueMarket.FADemand(fa, fo.Difficulty) * 2, true);
            Assert.IsFalse(offer.Success, "캡 초과 = FA 영입 실패");
            Assert.Contains(fa, league.FreeAgents);
            var bid = GMMarketBidding.Open(league, team, fa);
            GMMarketBidding.Bid(league, team, bid, Player.MaxSalary, 2, true);
            Assert.AreNotEqual(GMFaBidState.Won, bid.State, "캡 초과 = 입찰 불가");

            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, out var canvas, out _, out _);
            hub.SelectMainTab(4);
            hub.SelectSubTab(0);
            hub.SelectFreeAgent(fa);
            Assert.IsFalse(hub.IsFAOfferInteractable, "FA [계약 제시] 버튼 Block");
            StringAssert.Contains("잠김", T(hub.Pane(GMOotpFrontOfficeUIController.PaneFA), "FAOffer"));
            StringAssert.Contains("샐러리캡", hub.FALockReason);
            CheckPane(hub, "FA(예산 하드 락)");
            team.PayrollCap = cap;
            team.Budget = -500;
            Assert.IsTrue(GMLeagueRules.FreeAgencyLocked(league, team, out var deficit), "운영 예산 적자 = 잠금");
            StringAssert.Contains("적자", deficit);
            team.Budget = 100000;
            hub.Refresh();
            Assert.IsTrue(hub.IsFAOfferInteractable, "예산 정상화 = 버튼 복구");

            // 개막 시 초과 상태 = 구단주 신임도 -20(해마다 1회)
            team.PayrollCap = team.Payroll - 1;
            int trust = fo.OwnerTrust = 70;
            Assert.AreEqual(GMSeasonPhase.StoveLeague, league.Phase);
            Assert.IsTrue(sim.StartRun(GMRunMode.SingleGame));
            Assert.AreEqual(trust - GMLeagueRules.OpeningOverBudgetTrustPenalty, fo.OwnerTrust, "개막 예산 초과 = 신임도 -20");
            Assert.IsFalse(GMLeagueRules.ApplyOpeningBudgetPenalty(league), "같은 해 중복 페널티 없음");
            Assert.IsTrue(league.News.Any(n => n.Title.Contains("예산 초과")));
            sim.Stop();
            team.PayrollCap = cap;

            // 응원단 마케팅 예산 분리 - 육성은 마케팅 예산 · 홈 흥행은 마케팅 예산 적립
            var c = team.CheerEntry.First();
            team.MarketingBudget = 100000;
            long op = team.Budget, mk = team.MarketingBudget;
            int lv = CheerGrowth.Reinforce(c);
            Assert.IsTrue(GMCheerleaderRoster.TryReinforce(team, c, out var msg), msg);
            Assert.AreEqual(mk - CheerGrowth.ReinforceCost(lv), team.MarketingBudget, "육성비 = 마케팅 예산");
            Assert.AreEqual(op, team.Budget, "운영 예산 · 페이롤과 섞이지 않음");
            team.MarketingBudget = 0;
            Assert.IsFalse(GMCheerleaderRoster.TryReinforce(team, c, out var poor));
            StringAssert.Contains("마케팅 예산", poor);
            long before = team.MarketingBudget;
            int gate = GMCheerleaderRoster.ApplyHomeGate(team);
            Assert.AreEqual(before + GMLeagueRules.MarketingIncome(team, gate), team.MarketingBudget, "홈경기 = 흥행 수익 50% + 굿즈");
            Assert.Greater(team.MarketingBudget, before);
            var data = SaveManager.ToGMSaveData(league);
            var byId = templates.ToDictionary(t => t.TemplateId);
            var back = SaveManager.FromGMSaveData(JsonUtility.FromJson<GMLeagueSaveData>(JsonUtility.ToJson(data)), saved =>
            {
                var restored = new Player(saved.InstanceId, byId[saved.TemplateId]);
                SaveManager.ApplyGMFields(restored, saved);
                return restored;
            });
            Assert.IsNotNull(back);
            Assert.AreEqual(team.MarketingBudget, back.UserTeam.MarketingBudget, "마케팅 예산 세이브 왕복");
            var legacy = JsonUtility.FromJson<GMTeamSaveData>("{\"TeamCode\":\"KIA\"}");
            Assert.AreEqual(-1, legacy.MarketingBudget, "구버전 세이브 = 미설정(-1) → 로드 시 기본 3억");
        }

        // ================================================================== 5) 시즌 중 단장 개입

        [Test]
        public void T5_InSeasonEvents_WeekEndOnly_Caps_DecisionLog_WeekMode_PopupUi()
        {
            Assert.IsTrue(GMSeasonEvents.IsWeekEnd(2) && GMSeasonEvents.IsWeekEnd(8) && GMSeasonEvents.IsWeekEnd(14));
            Assert.IsFalse(GMSeasonEvents.IsWeekEnd(5) || GMSeasonEvents.IsWeekEnd(144) || GMSeasonEvents.IsWeekEnd(0));
            Assert.AreEqual(8, GMSeasonEvents.NextWeekEnd(2));
            Assert.AreEqual(8, GMSeasonEvents.NextWeekEnd(3));
            Assert.AreEqual(GMLiveSeasonSimulator.SeasonGames, GMSeasonEvents.NextWeekEnd(141));

            // 주간 진행 - 이번 주 마지막 경기에서 멈춘다
            var weekSim = new GMLiveSeasonSimulator(NewLeague("DOO", 1805));
            Assert.IsTrue(weekSim.StartRun(GMRunMode.Week));
            Assert.AreEqual(2, weekSim.TargetGames, "개막 주말 2경기");
            weekSim.RunUntilStop();
            Assert.AreEqual(2, weekSim.GamesPlayed);
            Assert.IsTrue(weekSim.StartRun(GMRunMode.Week));
            weekSim.RunUntilStop();
            Assert.AreEqual(8, weekSim.GamesPlayed, "다음 주(화~일 6경기)");

            // 한 시즌 - 주간 종료마다 사건 판정, 선택지를 돌려 가며 처리
            var league = NewLeague("SAM", 1806);
            var sim = new GMLiveSeasonSimulator(league);
            Assert.IsTrue(sim.StartRun(GMRunMode.FullSeason));
            var events = new List<GMSeasonEvent>();
            var results = new List<GMSeasonEventResult>();
            for (int guard = 0; guard < 4000 && !sim.IsSeasonComplete; guard++)
            {
                var ev = sim.PendingSeasonEvent;
                if (ev != null)
                {
                    Assert.IsTrue(GMSeasonEvents.IsWeekEnd(ev.Day), $"사건은 주간 종료 후에만(G {ev.Day})");
                    Assert.AreEqual(sim.GamesPlayed, ev.Day);
                    Assert.That(ev.Choices.Count, Is.InRange(2, 4), ev.Title);
                    Assert.IsTrue(ev.Choices[ev.DefaultChoice].Available, "기본 선택지는 항상 가능");
                    Assert.IsTrue(ev.Choices.All(c => !string.IsNullOrEmpty(c.Label) && (c.Available ? c.Effect != "" : c.BlockReason != "")), "선택지 효과 미리보기");
                    int idx = events.Count % ev.Choices.Count;
                    if (!ev.Choices[idx].Available) idx = ev.DefaultChoice;
                    events.Add(ev);
                    var r = sim.ResolveSeasonEvent(idx);
                    Assert.IsNotNull(r);
                    Assert.IsTrue(r.Applied, r.Message);
                    Assert.IsTrue(ev.Resolved);
                    Assert.AreEqual(GMNewsKind.Decision, league.News[0].Kind, "결정 = 단장 개입 소식");
                    results.Add(r);
                    continue;
                }
                if (sim.PendingInterrupt != null) { sim.ResolveInterrupt(GMInterruptChoice.AutoCallUp); continue; }
                if (!sim.IsRunning) sim.StartRun(GMRunMode.FullSeason);
                sim.StepGameDay();
            }
            Assert.IsTrue(sim.IsSeasonComplete, "시즌 완주");
            int count = events.Count;
            string kinds = string.Join(", ", events.GroupBy(e => e.Kind).Select(g => $"{GMSeasonEvents.KindLabel(g.Key)} {g.Count()}"));
            TestContext.WriteLine($"[GM18 단장 개입] 시즌 {count}건 · {kinds}");
            Debug.Log($"[GM18 단장 개입] 시즌 {count}건 · {kinds} · 결정 예: {string.Join(" / ", results.Take(4).Select(r => r.Message))}");
            Assert.GreaterOrEqual(count, 4, "시즌당 최소 3~4회 단장 개입");
            Assert.LessOrEqual(count, GMSeasonEvents.MaxPerSeason, "시즌 상한 12건");
            Assert.IsTrue(events.GroupBy(e => e.Week).All(g => g.Count() <= GMSeasonEvents.MaxPerWeek), "주당 최대 3건");
            Assert.IsTrue(events.GroupBy(e => e.Week).All(g => g.Select(e => (int)e.Priority).SequenceEqual(g.Select(e => (int)e.Priority).OrderBy(x => x))), "같은 주 = 우선순위 순");
            Assert.GreaterOrEqual(events.Select(e => e.Kind).Distinct().Count(), 2, "사건 유형 다양성");
            Assert.LessOrEqual(events.Count(e => e.Kind == GMSeasonEventKind.OwnerCheck), 1, "구단주 중간 점검은 시즌 1회 이하");
            var fo = GMFrontOffice.Ensure(league);
            Assert.AreEqual(count, fo.SeasonDecisions.Count(d => d.Year == league.SeasonYear), "결정 로그 = 처리한 사건 수");
            foreach (var t in league.Teams.Values) Assert.That(t.SeasonEventTeamwork, Is.InRange(GMSeasonEvents.TeamworkMin, GMSeasonEvents.TeamworkMax));
            var data = SaveManager.ToGMSaveData(league);
            var json = JsonUtility.FromJson<GMLeagueSaveData>(JsonUtility.ToJson(data));
            Assert.AreEqual(fo.SeasonDecisions.Count, json.FrontOffice.SeasonDecisions.Count, "결정 로그 세이브 왕복");
            Assert.AreEqual(fo.SeasonEventCooldowns.Count, json.FrontOffice.SeasonEventCooldowns.Count);

            // 기본 선택 자동 처리(ResolveInterrupt) - 경기력 무관한 보수적 선택
            var auto = new GMLiveSeasonSimulator(NewLeague("SAM", 1806));
            Assert.IsTrue(auto.StartRun(GMRunMode.FullSeason));
            auto.RunUntilStop();
            Assert.IsTrue(auto.IsSeasonComplete);
            Assert.AreEqual(auto.SeasonEventsRaised, GMFrontOffice.Ensure(auto.League).SeasonDecisions.Count(d => d.Year == auto.League.SeasonYear), "자동 진행 = 기본 선택으로 모두 처리");

            // 팝업 UI - 대시보드가 사건을 띄우고 선택 → 결과 → 계속
            var uiLeague = NewLeague("SAM", 1806);
            var uiSim = new GMLiveSeasonSimulator(uiLeague);
            var hub = NewHub(uiSim, out var canvas, out _, out var dash);
            Assert.IsTrue(uiSim.StartRun(GMRunMode.FullSeason));
            for (int guard = 0; guard < 4000 && uiSim.PendingSeasonEvent == null && !uiSim.IsSeasonComplete; guard++)
            {
                if (uiSim.PendingInterrupt != null) { uiSim.ResolveInterrupt(GMInterruptChoice.AutoCallUp); continue; }
                uiSim.StepGameDay();
            }
            var first = uiSim.PendingSeasonEvent;
            Assert.IsNotNull(first, "시즌 중 사건 발생");
            dash.gameObject.SetActive(true);
            dash.Bind(uiSim);
            Assert.IsTrue(dash.IsPopupOpen, "대기 중인 사건 = 팝업");
            Assert.AreSame(first, dash.ViewingEvent);
            StringAssert.Contains("주차 결산", T(dash.PopupRoot, GMLiveLeagueDashboardUIController.EventTagName));
            Assert.AreEqual(first.Title, dash.PopupTitleText);
            Assert.IsFalse(dash.PopupRoot.Find("PopupPrimary").gameObject.activeSelf, "사건은 선택지로만 진행");
            for (int k = 0; k < first.Choices.Count; k++) Assert.IsTrue(dash.PopupRoot.Find($"Candidate{k}").gameObject.activeSelf, $"선택지 {k + 1}");
            CheckLayer(dash.PopupRoot, "단장 개입 팝업");
            CheckLayer(dash.Root, "대시보드 헤더(주간 진행 버튼)");
            StringAssert.Contains("주간 진행", T(dash.Root, "ModeWeek"));
            Click(dash.PopupRoot, $"Candidate{first.DefaultChoice}");
            Assert.IsTrue(first.Resolved, "선택 = 처리");
            Assert.IsTrue(dash.IsShowingEventResult, "결과 화면");
            StringAssert.Contains("결정 결과", dash.PopupTitleText);
            CheckLayer(dash.PopupRoot, "결정 결과 팝업");
            Click(dash.PopupRoot, "PopupPrimary");
            Assert.IsTrue(uiSim.PendingSeasonEvent == null ? !dash.IsShowingEventResult : dash.ViewingEvent == uiSim.PendingSeasonEvent, "다음 사건 또는 진행");
            int bold = dash.GetComponentsInChildren<Text>(true).Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic || t.text.Contains("<b>"));
            Assert.AreEqual(0, bold, "Bold 0건");
        }

        // ================================================================== 6) 단장 커리어

        [Test]
        public void T6_Career_TermRenewal_Dismissal_Retirement_Unlocks_NewGamePlus()
        {
            var league = NewLeague("HAN", 1807);
            var c = GMCareer.Ensure(league);
            int year = league.SeasonYear;
            Assert.AreEqual(year, c.StartYear, "부임 연도 = 커리어 시작");
            Assert.AreEqual(year + GMCareer.TermYears - 1, c.TermEndYear, "1기 임기 3년");
            StringAssert.Contains("1기", GMCareer.StatusLabel(league));
            var fo = GMFrontOffice.Ensure(league);
            fo.OwnerTrust = 10;
            Assert.IsFalse(GMCareer.OnSeasonCompleted(league, year), "임기 중(1년차)에는 신임도가 낮아도 해임 없음");
            fo.OwnerTrust = 70;
            Assert.IsFalse(GMCareer.OnSeasonCompleted(league, c.TermEndYear), "임기 말 신임도 70 = 재계약");
            Assert.AreEqual(2, c.Term);
            Assert.AreEqual(year + 2 * GMCareer.TermYears - 1, c.TermEndYear, "2기 임기 3년");
            Assert.IsTrue(league.News.Any(n => n.Title.Contains("재계약")));
            fo.OwnerTrust = GMCareer.RenewTrust - 1;
            Assert.IsTrue(GMCareer.OnSeasonCompleted(league, c.TermEndYear), "임기 말 신임도 39 = 해임");
            Assert.IsTrue(c.Ended);
            Assert.AreEqual(GMCareerEnding.Dismissed, c.Ending);
            StringAssert.Contains("해임", league.News.First().Title);
            var sim = new GMLiveSeasonSimulator(league);
            Assert.IsFalse(GMAwardEvaluator.AdvanceToNextSeasonYear(sim), "엔딩 이후 연도 전환 없음");

            // 해임당하지 않음 옵션 = 항상 재계약
            var safe = NewLeague("HAN", 1808);
            var sc = GMCareer.Ensure(safe);
            GMFrontOffice.Ensure(safe).Manager.NoFiring = true;
            GMFrontOffice.Ensure(safe).OwnerTrust = 5;
            Assert.IsFalse(GMCareer.OnSeasonCompleted(safe, sc.TermEndYear));
            Assert.AreEqual(2, sc.Term);

            // 30시즌 은퇴 엔딩 - 2026 + 29 = 2055
            var vet = NewLeague("SAM", 1809);
            var vc = GMCareer.Ensure(vet);
            Assert.AreEqual(vet.SeasonYear + GMCareer.MaxSeasons - 1, GMCareer.FinalYear(vc), "최대 30시즌");
            var vfo = GMFrontOffice.Ensure(vet);
            for (int y = vc.StartYear; y <= vc.StartYear + 5; y++)
                vfo.History.Add(new GMSeasonHistoryEntry { TeamCode = vet.SelectedTeamCode, Year = y, W = 80, L = 60, Rank = y % 2 == 0 ? 1 : 3, Postseason = true, Champion = y % 2 == 0 });
            vfo.OwnerTrust = 90;
            Assert.IsTrue(GMCareer.OnSeasonCompleted(vet, GMCareer.FinalYear(vc)), "30번째 시즌 종료 = 은퇴");
            Assert.AreEqual(GMCareerEnding.Retired, vc.Ending);
            Assert.AreEqual(GMCareer.MaxSeasons, vc.Seasons);
            Assert.GreaterOrEqual(vc.Titles, 3);
            CollectionAssert.Contains(vc.Unlocks, "30년 근속");
            CollectionAssert.Contains(vc.Unlocks, "왕조 설계자");
            Assert.IsTrue(vc.HallOfFame, "레거시 60점 이상 = 명예의 전당");
            StringAssert.Contains("명예의 전당", vc.Note);
            var json = JsonUtility.FromJson<GMLeagueSaveData>(JsonUtility.ToJson(SaveManager.ToGMSaveData(vet)));
            Assert.AreEqual(GMCareerEnding.Retired, json.FrontOffice.Career.Ending, "커리어 세이브 왕복");
            CollectionAssert.AreEqual(vc.Unlocks, json.FrontOffice.Career.Unlocks);

            // 뉴게임+ - 해금 1개당 시작 운영 예산 +2억
            var next = NewLeague("SAM", 1810);
            long budget = next.UserTeam.Budget;
            Assert.AreEqual(vc.Unlocks.Count, GMCareer.ApplyNewGamePlus(next, vc.Unlocks));
            Assert.AreEqual(budget + GMCareer.NewGamePlusBudgetPerUnlock * vc.Unlocks.Count, next.UserTeam.Budget);
            CollectionAssert.IsSubsetOf(vc.Unlocks, GMCareer.Ensure(next).Unlocks);

            // 대시보드 엔딩 팝업
            var canvasSim = new GMLiveSeasonSimulator(vet);
            NewHub(canvasSim, out _, out _, out var dash);
            dash.gameObject.SetActive(true);
            dash.Bind(canvasSim);
            dash.ShowCareerEnding();
            Assert.IsTrue(dash.IsShowingCareerEnding);
            StringAssert.Contains("은퇴", dash.PopupTitleText);
            Assert.IsTrue(dash.PopupRoot.Find("PopupSecondary").gameObject.activeSelf, "[뉴게임+로 새 커리어]");
            CheckLayer(dash.PopupRoot, "커리어 엔딩 팝업");
        }

        // ================================================================== 7) 기록 태그 · UI 무결성

        [Test]
        public void T7_RecordTags_RealVsSim_ScoutingReport_Layout()
        {
            var league = NewLeague("SAM", 1811);
            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, out var canvas, out _, out _);
            var p = league.UserTeam.Roster.OrderByDescending(x => x.BaseOverall).First();
            var lines = GMScoutingReport.StatLines(league, p);
            Assert.IsTrue(lines.Any(l => l.Contains(GMScoutingReport.RealRecordTag)), "카드 시즌 = [실제 KBO 기록]");
            Assert.IsFalse(lines.Any(l => l.Contains(GMScoutingReport.SimRecordTag)), "경기 전 = 시뮬레이션 기록 없음");
            Assert.IsTrue(sim.StartRun(GMRunMode.Week));
            sim.RunUntilStop();
            Assert.IsTrue(sim.StartRun(GMRunMode.Week));
            sim.RunUntilStop();
            var played = league.UserTeam.Roster.First(x => league.Stats.TryGetValue(x.InstanceId, out var s) && (s.PA > 0 || s.OutsPitched > 0));
            var after = GMScoutingReport.StatLines(league, played);
            Assert.IsTrue(after.Any(l => l.Contains(GMScoutingReport.SimRecordTag) && l.Contains($"{league.SeasonYear} 시즌")), "진행 시즌 = [시뮬레이션 기록]");
            Assert.IsTrue(after.Any(l => l.Contains(GMScoutingReport.RealRecordTag)), "실제 기록도 함께(분리 표기)");
            hub.OpenPercentiles(played);
            var popup = hub.Root.Find("PercentilePopup");
            Assert.IsTrue(popup.gameObject.activeSelf);
            StringAssert.Contains(GMScoutingReport.SimRecordTag, T(popup, "ScStats"));
            StringAssert.Contains(GMScoutingReport.RealRecordTag, T(popup, "ScStats"));
            StringAssert.Contains(GMScoutingReport.RealRecordTag, T(popup, "ScRecordTags"));
            Assert.IsFalse(T(popup, "ScRecordTags").Contains("빨강") || T(popup, "ScRecordTags").Contains("파랑"), "색 이름 글자 노출 금지");
            CheckLayer(popup, "스카우팅 리포트(기록 태그)");
            int bold = hub.GetComponentsInChildren<Text>(true).Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic || t.text.Contains("<b>"));
            Assert.AreEqual(0, bold, "Bold 0건");
            Assert.AreEqual(0, GMRaycastSanitizer.BlockingTexts(hub.GetComponentsInChildren<Graphic>(true)), "모바일 - 터치 가로채는 텍스트 0");
        }

        // ================================================================== 공통

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim, out GameObject canvas, out GMMatchPrePostUIController prePost, out GMLiveLeagueDashboardUIController dash)
        {
            canvas = new GameObject("GM18_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvas);
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var hub = NewView<GMOotpFrontOfficeUIController>(canvas, "GM18_Hub");
            hub.Build();
            prePost = NewView<GMMatchPrePostUIController>(canvas, "GM18_PrePost");
            prePost.Build();
            prePost.gameObject.SetActive(false);
            hub.PrePostView = prePost;
            dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM18_Dash");
            dash.Build();
            dash.gameObject.SetActive(false);
            hub.DashView = dash;
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
            int bold = hub.GetComponentsInChildren<Text>(true).Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic || t.text.Contains("<b>"));
            Assert.AreEqual(0, bold, $"{label} Bold 0건");
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
