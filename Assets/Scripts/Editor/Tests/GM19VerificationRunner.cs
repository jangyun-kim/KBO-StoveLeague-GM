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
    /// [TASK-GM-19] 자동 검증(Unity CLI BatchPipelineGM19 1회 실행):
    ///   1) 커리어 타임라인 - 가상 선수 MVP 수상 · 연봉 계약 → History 적재 · 세이브 왕복 · 데뷔 첫 기록 · [커리어 타임라인] 탭 UI
    ///   2) 영구결번 - 근속 10년 · 통산 WAR 40 베테랑이 연도 전환(AdvanceToNextSeasonYear)에서 은퇴 → 은퇴식 팝업 큐 · 허브 팝업 · 영구결번 지정(황금색 이름)
    ///   3) 2단계 사건 - 5연패 강제 주입 → [감독과의 갈등] 트리거 · 3지선다 결과(강등 · 감독 신뢰도 폭락 · 중재) · [라이벌 S급 영입] · 맞불 영입 공언 판정
    ///   4) UI 무결성 - 개편 LiveMatch 대시보드(치어리더 오버레이 · 코스별 피안타율 · 투구수/체력 · 단장 지시) · 타임라인 탭 텍스트 겹침 0 · 15pt(지시서 14pt) 이상 · Bold 0
    /// </summary>
    public class GM19VerificationRunner
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
            if (templates != null && templates.Count > 0 && templates.All(t => t != null)) return;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            dbObject = new GameObject("GM19_PlayerDatabase");
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

        private static GMLeagueState NewLeague(string team, int seed)
        {
            var league = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        // ================================================================== 1) 커리어 타임라인

        [Test]
        public void T1_CareerTimeline_VirtualPlayer_MvpAndContract_HistoryStored_TabUi()
        {
            var league = NewLeague("SAM", 1901);
            var team = league.UserTeam;
            // 가상 선수 - 템플릿 1장으로 새 인스턴스를 만들어 내 구단에 넣는다
            var template = templates.First(t => t != null && !t.IsPitcher && t.GetBaseOverall() >= 70 && team.ReservePlayers.All(p => p.Template.RealPlayerId != t.RealPlayerId) &&
                                                league.Teams.Values.All(x => x.ReservePlayers.All(p => p.Template.RealPlayerId != t.RealPlayerId)));
            var vp = new Player(GMRosterLoader.StableId("GM19_virtual_player"), template);
            vp.InitializeGMAttributesFromStats(league.SeasonYear);
            vp.Age = 29;
            vp.ContractYears = 0;
            vp.Salary = 20000;
            if (team.Roster.Count >= GMRosterTiers.FirstTeamMax) team.Futures.Add(vp); else team.Roster.Add(vp);
            Assert.IsEmpty(vp.CareerHistory, "새 선수 = 빈 타임라인");

            // MVP 수상 트리거(시상 훅과 같은 경로)
            GMCareerTimeline.RecordAward(league, vp, "MVP", "KBO MVP", team.TeamCode, league.SeasonYear);
            GMCareerTimeline.RecordAward(league, vp, "MONTHLY_MVP", "KBO 월간 MVP", team.TeamCode, league.SeasonYear);
            Assert.IsTrue(GMCareerTimeline.Has(vp, GMCareerEventKind.Mvp), "MVP → History");
            Assert.AreEqual(1, vp.CareerHistory.Count, "월간 MVP는 타임라인 제외(수상 태그로만)");
            GMCareerTimeline.RecordAward(league, vp, "MVP", "KBO MVP", team.TeamCode, league.SeasonYear);
            Assert.AreEqual(1, vp.CareerHistory.Count(e => e.Kind == (int)GMCareerEventKind.Mvp), "같은 해 같은 수상 중복 적재 없음");

            // 연봉 계약(계약 협상실) - 제시액 = 요구액 → 100% 타결
            var s = GMNegotiationRoom.Open(league, team, vp);
            Assert.AreEqual("", s.BlockReason);
            s = GMNegotiationRoom.Open(league, team, vp, offer: s.Demand);
            var r = GMNegotiationRoom.Resolve(league, team, s, -1, 0.0);
            Assert.IsTrue(r.Success, r.Message);
            var contract = vp.CareerHistory.FirstOrDefault(e => e.Kind == (int)GMCareerEventKind.Contract || e.Kind == (int)GMCareerEventKind.SalaryConflict);
            Assert.IsNotNull(contract, "연봉 계약 → History");
            StringAssert.Contains(GMDiagnosticFormat.Short(r.Salary), contract.Detail);
            Assert.IsTrue(contract.Sim, "인게임 계약 = 시뮬레이션 기록");

            // 데뷔 첫 기록 - 게임 안 데뷔 선수(첫 콜업)만
            var rookie = team.Futures.First(p => !p.IsPitcher && p != vp);
            GMCareerTimeline.OnCalledUp(league, rookie, team.TeamCode);
            Assert.IsTrue(GMCareerTimeline.TracksDebut(rookie));
            GMCareerTimeline.CheckDebut(league, rookie, team.TeamCode, "04/01", "LG", 1, 1, false);
            Assert.IsTrue(GMCareerTimeline.Has(rookie, GMCareerEventKind.FirstHit) && GMCareerTimeline.Has(rookie, GMCareerEventKind.FirstHomeRun), "데뷔 첫 안타 · 홈런");
            Assert.IsFalse(GMCareerTimeline.TracksDebut(rookie), "데뷔 기록 완료");
            var veteran = team.Roster.First(p => !p.IsPitcher && p != vp && p.CareerHistory.Count == 0);
            Assert.IsFalse(GMCareerTimeline.TracksDebut(veteran), "기존 선수는 '데뷔 첫 안타'를 새로 만들지 않는다");

            // 근속 · 통산 WAR 추정 · 트레이드 = 근속 초기화
            GMCareerTimeline.EnsureCareer(veteran, team.TeamCode);
            Assert.GreaterOrEqual(veteran.CareerWarEstimate, 0f);
            Assert.AreEqual(team.TeamCode, veteran.TenureTeam);
            GMCareerTimeline.OnTraded(league, veteran, team.TeamCode, "LG");
            Assert.AreEqual("LG", veteran.TenureTeam);
            Assert.AreEqual(0, veteran.TenureYears, "이적 = 근속 0부터");

            // 타임라인 = 저장 이벤트 + 카드 수상 태그(실제 기록) - 연도순
            vp.CareerAwardIds.Add("GOLDEN_GLOVE_2019_SS");
            var tl = GMCareerTimeline.Timeline(league, vp);
            Assert.IsTrue(tl.Any(e => e.Kind == (int)GMCareerEventKind.GoldenGlove && !e.Sim && e.Year == 2019), "카드 수상 = 실제 KBO 기록");
            Assert.IsTrue(tl.Select(e => e.Year).SequenceEqual(tl.Select(e => e.Year).OrderBy(y => y)), "연도순");

            // 세이브 v29 왕복
            var json = JsonUtility.FromJson<GMLeagueSaveData>(JsonUtility.ToJson(SaveManager.ToGMSaveData(league)));
            var saved = json.Teams.First(t => t.TeamCode == team.TeamCode).Roster.Concat(json.Teams.First(t => t.TeamCode == team.TeamCode).Futures).First(x => x.InstanceId == vp.InstanceId);
            Assert.AreEqual(vp.CareerHistory.Count, saved.CareerHistory.Count, "커리어 타임라인 세이브 왕복");
            var restored = new Player(vp.InstanceId, template);
            SaveManager.ApplyGMFields(restored, saved);
            Assert.IsTrue(GMCareerTimeline.Has(restored, GMCareerEventKind.Mvp));
            Assert.AreEqual(vp.TenureTeam, restored.TenureTeam);
            Assert.AreEqual(vp.CareerWarEstimate, restored.CareerWarEstimate, 1e-4f);
            Assert.AreEqual(team.ManagerTrust, json.Teams.First(t => t.TeamCode == team.TeamCode).ManagerTrust, "감독 신뢰도 세이브");

            // [커리어 타임라인] 탭 UI
            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, out _, out _, out _);
            hub.OpenCareerTimeline(vp);
            Assert.IsTrue(hub.IsTimelineOpen, "선수 상세 → 커리어 타임라인 탭");
            Assert.IsTrue(hub.TimelineEvents.Any(e => e.Kind == (int)GMCareerEventKind.Mvp));
            var panel = hub.TimelinePanel;
            StringAssert.Contains("근속", T(panel, "TlSummary"));
            var rows = Enumerable.Range(0, GMOotpFrontOfficeUIController.TimelineRows * GMOotpFrontOfficeUIController.TimelineCols)
                .Select(i => panel.Find($"TlRow{i}")).Where(x => x != null && x.gameObject.activeSelf).Select(x => x.GetComponent<Text>().text).ToList();
            Assert.IsTrue(rows.Any(x => x.Contains("MVP")), "MVP 이벤트 카드");
            Assert.IsTrue(rows.Any(x => x.Contains("재계약") || x.Contains("연봉")), "계약 이벤트 카드");
            Assert.IsTrue(panel.Find("TlYear0").gameObject.activeSelf, "연도 점");
            Click(panel, "TlYear0");
            CheckLayer(panel, "커리어 타임라인 탭");
            CheckLayer(hub.Root.Find("PercentilePopup"), "선수 상세(탭 버튼)");
            Click(hub.Root.Find("PercentilePopup"), "ScTabReport");
            Assert.IsFalse(panel.gameObject.activeSelf, "[스카우팅 리포트] 탭 복귀");
            TestContext.WriteLine($"[GM19 타임라인] {vp.Template.PlayerName}: " + string.Join(" / ", tl.Select(e => $"{e.Year} {GMCareerTimeline.KindLabel(e.EventKind)} {e.Title}")));
        }

        // ================================================================== 2) 은퇴 · 영구결번

        [Test]
        public void T2_Retirement_FranchiseLegend_YearAdvance_CeremonyQueued_RetiredNumberGold()
        {
            var league = NewLeague("LG", 1902);
            var team = league.UserTeam;
            var legend = team.Roster.Where(p => !p.IsPitcher && !p.IsCaptain).OrderByDescending(p => p.BaseOverall).First();
            legend.Age = 40;          // 연도 전환 후 41세 = 은퇴 확률 100%
            legend.ContractYears = 1; // 연도 전환 후 0 = 계약 종료
            legend.TenureTeam = team.TeamCode;
            legend.TenureYears = 12;
            legend.CareerWarEstimate = 45f;
            legend.CareerWarSim = 0f;
            Assert.IsTrue(GMRetirement.QualifiesForRetiredNumber(legend));

            var sim = new GMLiveSeasonSimulator(league);
            Assert.IsTrue(sim.StartRun(GMRunMode.FullSeason));
            sim.RunUntilStop();
            Assert.IsTrue(sim.IsSeasonComplete, "시즌 완주");
            int year = league.SeasonYear;
            Assert.IsTrue(GMAwardEvaluator.AdvanceToNextSeasonYear(sim), "연도 전환");
            Assert.AreEqual(year + 1, league.SeasonYear);

            var fo = GMFrontOffice.Ensure(league);
            Assert.IsFalse(team.ReservePlayers.Contains(legend), "은퇴 = 로스터 제외");
            Assert.IsFalse(league.FreeAgents.Contains(legend), "은퇴 = FA 시장 제외");
            Assert.IsTrue(GMCareerTimeline.Has(legend, GMCareerEventKind.Retirement), "타임라인 [은퇴]");
            Assert.GreaterOrEqual(legend.TenureYears, 13, "연도 전환 = 근속 +1");
            var ceremony = GMRetirement.NextCeremony(league);
            Assert.IsNotNull(ceremony, "영구결번 자격 은퇴 → 팝업 큐");
            Assert.AreEqual(legend.InstanceId, ceremony.PlayerId);
            Assert.GreaterOrEqual(ceremony.CareerWar, GMRetirement.RetiredNumberWar);
            Assert.IsTrue(league.News.Any(n => n.Title.Contains("영구결번 논의")), "은퇴 소식");
            Assert.IsTrue(fo.Retirements.Any(x => x.PlayerName == legend.Template.PlayerName));
            TestContext.WriteLine($"[GM19 은퇴] {year + 1}년 은퇴 {fo.Retirements.Count(x => x.Year == year + 1)}명: " +
                                  string.Join(", ", fo.Retirements.Where(x => x.Year == year + 1).Take(12).Select(x => $"{x.PlayerName}({x.Age}세 · {x.Reason})")));

            // 허브 팝업 → 영구결번 지정
            var hubSim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(hubSim, out _, out _, out _);
            Assert.IsTrue(hub.IsRetirementPopupOpen, "허브 진입 = 은퇴식 팝업");
            StringAssert.Contains(legend.Template.PlayerName, T(hub.RetirementPopup, "RtName"));
            CheckLayer(hub.RetirementPopup, "은퇴식 팝업");
            team.MarketingBudget = Math.Max(team.MarketingBudget, GMRetirement.RetireNumberCost + 1000);
            hub.ShowRetirementCeremony(ceremony);
            int fan = team.FanSupport;
            long marketing = team.MarketingBudget;
            Click(hub.RetirementPopup, "RtChoice0");
            Assert.IsTrue(ceremony.Resolved, "영구결번 지정 처리");
            Assert.AreEqual(marketing - GMRetirement.RetireNumberCost, team.MarketingBudget, "막대한 마케팅 예산 소모");
            Assert.GreaterOrEqual(team.FanSupport, Math.Min(100, fan + GMRetirement.RetireNumberFan), "팬 지지율 대폭 상승");
            Assert.IsTrue(GMRetirement.IsRetiredNumber(league, legend), "영구결번 등록(RealPlayerId)");
            StringAssert.Contains(GMRetirement.GoldHex, GMRetirement.NameRich(league, legend), "이름 황금색 고정");
            Assert.IsTrue(GMCareerTimeline.Has(legend, GMCareerEventKind.RetiredNumber), "타임라인 [영구결번]");
            Assert.GreaterOrEqual(team.FanSupport, GMRetirement.FanFloor(league, team), "영구 하한");
            StringAssert.Contains("확인", hub.RetirementPopup.Find("RtClose").GetComponentInChildren<Text>().text);
            Click(hub.RetirementPopup, "RtClose");
            Assert.IsNull(GMRetirement.NextCeremony(league), "큐 비움");
            var json = JsonUtility.FromJson<GMLeagueSaveData>(JsonUtility.ToJson(SaveManager.ToGMSaveData(league)));
            Assert.IsTrue(json.FrontOffice.RetiredNumbers.Any(n => n.RealPlayerId == legend.Template.RealPlayerId), "영구결번 세이브 왕복");

            // 자격 미달 · AI 구단 은퇴 = 팝업 없음, 36세 미만 = 은퇴 없음
            var other = NewLeague("KT", 1912);
            var ai = other.Teams.Values.First(t => !t.IsUserTeam);
            var aiVet = ai.Futures.First();
            aiVet.Age = 37;
            var youngFa = other.FreeAgents.FirstOrDefault();
            if (youngFa != null) youngFa.Age = 30;
            var userVet = other.UserTeam.Futures.First();
            userVet.Age = 36;
            userVet.TenureTeam = other.UserTeam.TeamCode;
            userVet.TenureYears = 3;
            var retired = GMRetirement.Process(other, other.SeasonYear, 0.0);
            CollectionAssert.Contains(retired, aiVet, "37세 퓨처스(1군 밖) = 은퇴 후보");
            CollectionAssert.Contains(retired, userVet);
            if (youngFa != null) CollectionAssert.DoesNotContain(retired, youngFa, "36세 미만 은퇴 없음");
            Assert.IsFalse(GMFrontOffice.Ensure(other).PendingCeremonies.Any(c => c.PlayerId == aiVet.InstanceId || c.PlayerId == userVet.InstanceId), "자격 미달 · AI 구단 = 은퇴식 팝업 없음");
            Assert.IsTrue(GMCareerTimeline.Has(userVet, GMCareerEventKind.Retirement));
            var young = new Player { Template = legend.Template, Age = 35 };
            Assert.AreEqual(0.0, GMRetirement.RetireChance(young, false), 1e-9, "35세 = 은퇴 확률 0%");
            Assert.AreEqual(1.0, GMRetirement.AgeChance(40), 1e-9, "40세 이상 = 100%");
        }

        // ================================================================== 3) 2단계 사건

        [Test]
        public void T3_DynamicEventsPhase2_ManagerDemotion_On5LosingStreak_RivalSigning()
        {
            var league = NewLeague("KIA", 1903);
            var team = league.UserTeam;
            var sim = new GMLiveSeasonSimulator(league);
            Assert.IsTrue(sim.StartRun(GMRunMode.Week));
            sim.RunUntilStop();
            Assert.IsTrue(sim.StartRun(GMRunMode.Week));
            sim.RunUntilStop();
            Assert.IsTrue(GMSeasonEvents.IsWeekEnd(league.GamesPlayed), $"주간 종료(G {league.GamesPlayed})");
            int week = GMSeasonEvents.WeekOf(league.GamesPlayed);

            var rec = league.RecordOf(team.TeamCode);
            rec.Streak = -4;
            Assert.IsNull(GMSeasonEvents.BuildManagerDemotion(league, team, week), "4연패 = 미발동");
            rec.Streak = -5; // 5연패 강제 주입
            var generated = GMSeasonEvents.Generate(sim);
            var ev = generated.FirstOrDefault(e => e.Kind == GMSeasonEventKind.ManagerDemotion);
            Assert.IsNotNull(ev, "5연패 → [감독과의 갈등] 트리거(P0 = 항상 채택)");
            Assert.AreEqual(GMEventPriority.P0, ev.Priority);
            Assert.AreEqual("감독과의 갈등", GMSeasonEvents.KindLabel(ev.Kind));
            var top5 = team.Roster.Where(p => p.InjuryRemainingDays <= 0 && !p.IsCaptain).OrderByDescending(p => p.Salary).ThenBy(p => p.InstanceId, StringComparer.Ordinal)
                .Take(GMSeasonEvents.DemotionSalaryRank).ToList();
            CollectionAssert.Contains(top5, ev.Player, "대상 = 고액 연봉자(팀 내 연봉 상위 5위)");
            CollectionAssert.AreEqual(new[] { "SUPPORT", "OVERRULE", "MEDIATE" }, ev.Choices.Select(c => c.Id).ToArray());
            Assert.AreEqual("MEDIATE", ev.Choices[ev.DefaultChoice].Id, "자동 진행 = 면담 중재");

            // 감독 지지 - 2군 강등 · 충성도 폭락 · 감독 신뢰도 상승
            var target = ev.Player;
            int loyalty = target.Loyalty, mTrust = team.ManagerTrust;
            var r = GMSeasonEvents.Resolve(league, ev, 0);
            Assert.IsTrue(r.Applied, r.Message);
            Assert.IsTrue(team.Futures.Contains(target), "감독 지지 = 퓨처스 강등");
            Assert.AreEqual(Math.Max(0, loyalty + GMSeasonEvents.SupportLoyalty), target.Loyalty, "선수 충성도 폭락");
            Assert.AreEqual(Math.Min(100, mTrust + GMSeasonEvents.SupportManagerTrust), team.ManagerTrust);
            Assert.IsTrue(GMCareerTimeline.Has(target, GMCareerEventKind.BondConflict), "유대 갈등 → 커리어 타임라인");

            // 단장 권한 1군 유지 - 감독 신뢰도 폭락
            var ev2 = GMSeasonEvents.BuildManagerDemotion(league, team, week);
            Assert.IsNotNull(ev2);
            mTrust = team.ManagerTrust;
            GMSeasonEvents.Resolve(league, ev2, 1);
            Assert.AreEqual(Math.Max(0, mTrust + GMSeasonEvents.OverruleManagerTrust), team.ManagerTrust, "감독 신뢰도 폭락");
            Assert.IsTrue(team.Roster.Contains(ev2.Player), "1군 유지");

            // 면담 중재 - 성공/실패 확률 판정
            var ev3 = GMSeasonEvents.BuildManagerDemotion(league, team, week);
            mTrust = team.ManagerTrust;
            var r3 = GMSeasonEvents.Resolve(league, ev3, 2, 0.0);
            Assert.IsTrue(r3.Success, "중재 성공(난수 0)");
            Assert.AreEqual(Math.Min(100, mTrust + GMSeasonEvents.MediateManagerTrust), team.ManagerTrust);
            var ev4 = GMSeasonEvents.BuildManagerDemotion(league, team, week);
            var r4 = GMSeasonEvents.Resolve(league, ev4, 2, 0.99);
            Assert.IsFalse(r4.Success, "중재 실패(난수 0.99)");
            Assert.IsTrue(GMFrontOffice.Ensure(league).SeasonDecisions.Count(d => d.Kind == GMSeasonEventKind.ManagerDemotion.ToString()) >= 4, "결정 로그");

            // [라이벌 S급 영입] - 전통 라이벌(KIA ↔ SAM)이 FA로 S급 영입
            string rivalCode = GMSeasonEvents.TraditionalRival(team.TeamCode);
            Assert.AreEqual("SAM", rivalCode);
            var rival = league.Teams[rivalCode];
            var star = league.FreeAgents.Where(GMSeasonEvents.IsSClass).OrderByDescending(p => p.BaseOverall).FirstOrDefault();
            if (star == null)
            {
                var used = new HashSet<string>(league.Teams.Values.SelectMany(t => t.ReservePlayers).Concat(league.FreeAgents).Select(p => p.Template.RealPlayerId));
                var gg = templates.First(t => t != null && GMStarterDeck.TierOf(t) == GMStarterTier.S && !used.Contains(t.RealPlayerId));
                star = new Player(GMRosterLoader.StableId("GM19_rival_star"), gg);
                star.InitializeGMAttributesFromStats(league.SeasonYear);
                league.FreeAgents.Add(star);
            }
            if (rival.Roster.Count >= GMRosterTiers.FirstTeamMax) GMRosterTiers.SendDown(rival, rival.Roster.OrderBy(p => p.BaseOverall).First(p => !p.IsCaptain), out _);
            GMFaCompensation.AiSignFreeAgent(league, rival, star);
            Assert.IsTrue(rival.Roster.Contains(star));
            Assert.IsTrue(star.CareerHistory.Any(e => e.Kind == (int)GMCareerEventKind.FaSigning && e.TeamCode == rivalCode), "FA 계약 = 커리어 타임라인 기록");
            var rivalEv = GMSeasonEvents.BuildRivalSigning(sim, week);
            Assert.IsNotNull(rivalEv, "라이벌 S급 영입 → 사건");
            Assert.IsTrue(GMSeasonEvents.IsSClass(rivalEv.Player) && rival.Roster.Contains(rivalEv.Player), "라이벌 구단의 S급 선수");
            CollectionAssert.AreEqual(new[] { "COUNTER", "YOUTH", "CHEER" }, rivalEv.Choices.Select(c => c.Id).ToArray());
            var seen = new List<Player> { rivalEv.Player };
            for (int k = 0; k < 10; k++)
            {
                var more = GMSeasonEvents.BuildRivalSigning(sim, week);
                if (more == null) break;
                CollectionAssert.DoesNotContain(seen, more.Player, "같은 영입으로 두 번 발생하지 않음");
                seen.Add(more.Player);
            }
            CollectionAssert.Contains(seen, star, "주입한 S급 FA 영입 = 사건 대상");
            Assert.IsNull(GMSeasonEvents.BuildRivalSigning(sim, week), "모두 다룬 뒤 = 사건 없음");
            int fan = team.FanSupport, owner = GMFrontOffice.Ensure(league).OwnerTrust;
            var rr = GMSeasonEvents.Resolve(league, rivalEv, 0);
            Assert.IsTrue(rr.Applied, rr.Message);
            Assert.AreEqual(Math.Min(100, fan + GMSeasonEvents.CounterPledgeFan), team.FanSupport, "맞불 예고 = 팬심 방어");
            Assert.AreEqual(league.SeasonYear, GMFrontOffice.Ensure(league).CounterPledgeYear);
            Assert.IsFalse(GMSeasonEvents.CounterPledgeMet(league), "아직 영입 없음");
            string verdict = GMSeasonEvents.EvaluateCounterPledge(league);
            StringAssert.Contains("미달", verdict);
            Assert.AreEqual(Math.Max(0, owner + GMSeasonEvents.CounterFailTrust), GMFrontOffice.Ensure(league).OwnerTrust, "기한 내 미달성 = 신임도 하락");
            TestContext.WriteLine($"[GM19 2단계 사건] {ev.Title} · {r.Message} / {rivalEv.Title} · {verdict}");
        }

        // ================================================================== 4) LiveMatch 대시보드 · UI 무결성

        [Test]
        public void T4_LiveMatchDashboard_Cheer_ZoneMap_Stamina_Orders_Layout()
        {
            var league = NewLeague("SAM", 1904);
            var team = league.UserTeam;
            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, out _, out var prePost, out _);
            Assert.IsTrue(prePost.ShowPreGameView(sim));
            Assert.IsTrue(prePost.PlayBall());
            var live = prePost.LiveRoot;
            var cheerPanel = live.Find(GMMatchPrePostUIController.CheerOverlayName);
            Assert.IsNotNull(cheerPanel, "치어리더 오버레이");
            Assert.IsNotNull(prePost.LiveCheerleader, "오늘의 전담 응원/응원 리더");
            StringAssert.Contains(prePost.LiveCheerleader.DisplayName, T(cheerPanel, "CheerName"));
            Assert.AreEqual("투수 교체 지시", live.Find("TacticsPanel/PitchingButton").GetComponentInChildren<Text>().text);
            Assert.AreEqual("대타 기용 지시", live.Find("TacticsPanel/PinchButton").GetComponentInChildren<Text>().text);

            for (int i = 0; i < 6; i++) prePost.LiveStepAtBat();
            var zone = live.Find("AbsZonePanel");
            for (int i = 0; i < GMLiveTactics.ZoneCells; i++)
            {
                StringAssert.StartsWith(".", T(zone, $"HeatValue{i}"), "코스별 피안타율 9칸(투수)");
                StringAssert.StartsWith(".", T(zone, $"BatHeatValue{i}"), "코스별 타율 9칸(타자 - 참고 시안)");
            }
            Assert.IsTrue(prePost.LiveBatterZoneMap.All(v => v >= GMLiveTactics.MinBaa && v <= GMLiveTactics.MaxBaa));
            Assert.IsNotEmpty(T(cheerPanel, "CheerFrame/ShoutBubble/CheerShout"), "치어리더 응원 말풍선");
            Assert.IsTrue(prePost.LiveZoneMap.All(v => v >= GMLiveTactics.MinBaa && v <= GMLiveTactics.MaxBaa));
            Assert.Greater(prePost.LiveZoneMap[4], prePost.LiveZoneMap[8], "한가운데 > 바깥쪽 낮은 공");
            Assert.IsTrue(int.TryParse(T(zone, "PitchCountValue"), out int np) && np >= 0, "오늘 투구수");
            StringAssert.Contains("체력", T(zone, "StaminaLabel"));
            string abs = T(zone, "AbsSummary") + T(zone, "HeatLegend");
            Assert.IsFalse(abs.Contains("빨강") || abs.Contains("파랑"), "색 이름 글자 노출 금지(색 견본)");

            // 감독 신뢰도 → 지시 수용 확률
            Assert.Greater(GMLiveTactics.OrderChance(new GMTeamState { ManagerTrust = 90 }, true, 0.8f), GMLiveTactics.OrderChance(new GMTeamState { ManagerTrust = 20 }, true, 0.8f));
            Assert.Greater(GMLiveTactics.OrderChance(team, true, 0.2f), GMLiveTactics.OrderChance(team, true, 0.9f), "지친 투수 = 교체 지시 수용 쉬움");

            // [투수 교체 지시] - 내 구단 수비 때 · 하프이닝당 1회
            bool pitchingTested = false, pinchTested = false;
            for (int i = 0; i < 300 && !prePost.Session.IsOver && !(pitchingTested && pinchTested); i++)
            {
                var s = prePost.Session;
                if (!pitchingTested && !s.UserBatting)
                {
                    Assert.IsTrue(prePost.LiveOrderPitchingChange(0.0), prePost.LiveMessage);
                    Assert.AreEqual(true, prePost.LastOrderAccepted);
                    StringAssert.Contains("현장 수용", prePost.LiveMessage);
                    StringAssert.Contains("투수 교체", prePost.LiveMessage);
                    Assert.IsFalse(prePost.LiveOrderPitchingChange(0.0), "같은 하프이닝 재지시 불가");
                    StringAssert.Contains("이미", prePost.LiveMessage);
                    Assert.IsFalse(live.Find("TacticsPanel/PitchingButton").GetComponent<Button>().interactable);
                    pitchingTested = true;
                }
                else if (!pinchTested && s.UserBatting)
                {
                    Assert.IsFalse(prePost.LiveOrderPinchHit(0.99), "수용 확률 밖 = 현장 보류");
                    Assert.AreEqual(false, prePost.LastOrderAccepted);
                    StringAssert.Contains("현장 보류", prePost.LiveMessage);
                    pinchTested = true;
                }
                CheckLive(prePost, $"타석 {i}");
                prePost.LiveStepAtBat();
            }
            Assert.IsTrue(pitchingTested && pinchTested, "투수 교체 · 대타 지시 모두 검증");
            StringAssert.Contains("감독 신뢰도", T(live, "TacticsPanel/OrderInfo"));
            CheckLive(prePost, "실시간 대시보드");
            int bold = prePost.GetComponentsInChildren<Text>(true).Concat(hub.GetComponentsInChildren<Text>(true))
                .Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic || t.text.Contains("<b>"));
            Assert.AreEqual(0, bold, "Bold 0건");
            Assert.AreEqual(0, GMRaycastSanitizer.BlockingTexts(prePost.GetComponentsInChildren<Graphic>(true)), "모바일 - 터치 가로채는 텍스트 0");
            Assert.IsTrue(prePost.FinishLiveMatch());
            prePost.CloseAll();
        }

        // ================================================================== 공통

        private static void CheckLive(GMMatchPrePostUIController prePost, string label)
        {
            CheckLayer(prePost.LiveRoot, label + " 실시간 화면");
            foreach (var panel in new[] { GMMatchPrePostUIController.CheerOverlayName, "MatchupPanel", "AbsZonePanel", "PlayByPlayPanel", "WinProbabilityPanel", "TacticsPanel" })
                CheckLayer(prePost.LiveRoot.Find(panel), $"{label} {panel}");
        }

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim, out GameObject canvas, out GMMatchPrePostUIController prePost, out GMLiveLeagueDashboardUIController dash)
        {
            canvas = new GameObject("GM19_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvas);
            var scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var hub = NewView<GMOotpFrontOfficeUIController>(canvas, "GM19_Hub");
            hub.Build();
            prePost = NewView<GMMatchPrePostUIController>(canvas, "GM19_PrePost");
            prePost.Build();
            prePost.gameObject.SetActive(false);
            hub.PrePostView = prePost;
            dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM19_Dash");
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

        /// <summary>layer 직속 자식 중 활성 텍스트/버튼 영역 겹침 0 · 15pt 이상(지시서 최소 14pt) · Normal.</summary>
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
