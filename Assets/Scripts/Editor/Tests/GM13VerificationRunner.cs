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
    /// [TASK-GM-13] 자동 검증(Unity CLI BatchPipelineGM13 1회 실행):
    ///   1) 약속 상태 기계 - PROPOSED → ACTIVE → (출전 미달) BROKEN: 충성도 -25 · 선수단 신뢰도 -10 · 긴장 BGM / 이행 = FULFILLED · 폐기 · 세이브 왕복
    ///   2) 동적 사건 - 중심 타선 연봉 갈등 조건 → 사건 데이터 생성(선택지 3) · 선택지별 예산 · 충성도 · 신뢰도 변동 · BGM / 라커룸 파벌 갈등 → 측근 잔류 약속
    ///   3) 선수단 회의실 UI - 1920×1080 텍스트 겹침 0 · 15pt 이상(지시서 14pt) · Bold 0 · 약속 트래커 · 관계망 · 사건 팝업
    /// </summary>
    public class GM13VerificationRunner
    {
        private static IReadOnlyList<PlayerTemplate> templates;
        private static List<Cheerleader> cheer;
        private static GameObject dbObject;
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void EnsureDatabase()
        {
            var audio = GMAudioManager.Ensure();
            audio.StopAll();
            audio.ClearHistory();
            if (templates != null && templates.Count > 0 && templates.All(t => t != null)) return;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            dbObject = new GameObject("GM13_PlayerDatabase");
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
            var audio = GMAudioManager.Instance;
            if (audio != null) audio.StopAll();
            if (audio != null && (audio.gameObject.hideFlags & HideFlags.DontSave) != 0) Object.DestroyImmediate(audio.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created) if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
            var audio = GMAudioManager.Instance;
            if (audio != null) audio.StopAll();
        }

        private static GMLeagueState NewLeague(string team = "SAM", int seed = 1313)
        {
            var league = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        // ================================================================== 1) 약속 상태 기계

        [Test]
        public void T1_Promise_ActiveToBroken_LoyaltyMinus25_TrustMinus10_Tension()
        {
            var league = NewLeague();
            var team = league.UserTeam;
            var audio = GMAudioManager.Ensure();
            var batters = team.Roster.Where(p => !p.IsPitcher).OrderByDescending(p => p.BaseOverall).ToList();
            var benched = batters[0];
            var kept = batters[1];
            benched.Loyalty = 60;
            kept.Loyalty = 50;
            team.LockerRoomTrust = 60;

            // PROPOSED → ACTIVE
            var promise = GMPromiseSystem.Propose(league, team, benched, GMPromiseKind.StarterGuarantee, "테스트");
            Assert.AreEqual(GMPromiseState.Proposed, promise.State);
            Assert.AreEqual(league.SeasonYear, promise.DueYear, "스토브리그 약속 = 올해 시즌 판정");
            Assert.IsTrue(GMPromiseSystem.Activate(league, promise));
            Assert.AreEqual(GMPromiseState.Active, promise.State);
            Assert.IsFalse(GMPromiseSystem.Activate(league, promise), "ACTIVE 재활성 불가");
            var keptPromise = GMPromiseSystem.Propose(league, team, kept, GMPromiseKind.StarterGuarantee, "테스트");
            GMPromiseSystem.Activate(league, keptPromise);
            var cancelled = GMPromiseSystem.Propose(league, team, batters[2], GMPromiseKind.StarterGuarantee, "테스트");
            Assert.IsTrue(GMPromiseSystem.Cancel(league, cancelled));
            Assert.AreEqual(GMPromiseState.Cancelled, cancelled.State);
            Assert.IsFalse(GMPromiseSystem.Activate(league, cancelled), "폐기된 약속은 활성 불가");
            Assert.AreEqual(2, GMPromiseSystem.Active(league).Count);

            // 출전 기록 주입 - 규정 타석 446의 50%(223) 미달 / 충족
            league.StatsOf(benched, team.TeamCode).PA = 120;
            league.StatsOf(kept, team.TeamCode).PA = 480;
            var verdicts = GMPromiseSystem.Evaluate(league);
            Assert.AreEqual(2, verdicts.Count);
            Assert.AreEqual(GMPromiseState.Broken, promise.State, "출전 미달 = BROKEN");
            Assert.AreEqual(GMPromiseState.Fulfilled, keptPromise.State, "출전 충족 = FULFILLED");
            Assert.AreEqual(60 - GMPromiseSystem.BrokenLoyaltyPenalty, benched.Loyalty, "위반 = 충성도 -25");
            Assert.AreEqual(50 + GMPromiseSystem.FulfilledLoyaltyBonus, kept.Loyalty, "이행 = 충성도 +8");
            Assert.AreEqual(60 - GMPromiseSystem.BrokenTrustPenalty + GMPromiseSystem.FulfilledTrustBonus, team.LockerRoomTrust, "신뢰도 -10 +5");
            Assert.AreEqual(GMAudioEvent.Tension, audio.ActiveEvent, "위반 = 긴장 BGM 하이재킹");
            Assert.AreEqual(TeamAudioProfile.SynthTension, audio.CurrentBgmKey);
            StringAssert.Contains("120타석", promise.ResultNote);
            Assert.IsTrue(league.News.Any(n => n.Title.Contains("약속 불이행")), "위반 소식");
            Assert.AreEqual(0, GMPromiseSystem.Evaluate(league).Count, "판정된 약속은 다시 판정하지 않음");

            // 이행만 있을 때 = 긍정 BGM
            audio.StopAll();
            var only = GMPromiseSystem.Propose(league, team, batters[3], GMPromiseKind.StarterGuarantee, "테스트");
            GMPromiseSystem.Activate(league, only);
            league.StatsOf(batters[3], team.TeamCode).PA = 300;
            GMPromiseSystem.Evaluate(league);
            Assert.AreEqual(GMPromiseState.Fulfilled, only.State);
            Assert.AreEqual(GMAudioEvent.PositiveResult, audio.ActiveEvent, "이행 = 환희");

            // 세이브 왕복(프런트 오피스 상태 JsonUtility)
            var back = JsonUtility.FromJson<GMFrontOfficeState>(JsonUtility.ToJson(GMFrontOffice.Ensure(league)));
            Assert.AreEqual(4, back.Promises.Count);
            Assert.AreEqual(GMPromiseState.Broken, back.Promises.First(p => p.Id == promise.Id).State);
            Assert.AreEqual(23, new GameSaveData().SaveVersion, "세이브 v23");

            // 계약 협상실 [주전 · 보직 보장] 카드 → 타결 = ACTIVE / 결렬 = 폐기
            bool checkedRoom = false;
            foreach (var p in team.Roster.Where(x => x.EgoLevel >= 3).Take(12))
            {
                p.ContractYears = 1;
                var s = GMNegotiationRoom.Open(league, team, p);
                int idx = s.Forecasts.FindIndex(f => f.Card != null && f.Card.Id == GMPromiseSystem.RoleCardId);
                if (idx < 0) continue;
                int before = GMPromiseSystem.All(league).Count();
                var r = GMNegotiationRoom.Resolve(league, team, s, idx);
                var made = GMPromiseSystem.All(league).Skip(before).ToList();
                Assert.AreEqual(1, made.Count, "카드 선택 = 약속 제안");
                Assert.AreEqual(r.Success ? GMPromiseState.Active : GMPromiseState.Cancelled, made[0].State, "타결 = ACTIVE · 결렬 = 폐기");
                TestContext.WriteLine($"[GM13 약속] 협상실 {p.Template.PlayerName} → {(r.Success ? "타결" : "결렬")} · 약속 {GMPromiseSystem.StateLabel(made[0].State)}");
                checkedRoom = true;
                break;
            }
            TestContext.WriteLine($"[GM13 약속] 협상실 연동 확인 {checkedRoom}");
        }

        // ================================================================== 2) 동적 사건

        [Test]
        public void T2_DynamicEvents_SalaryDispute_Choices_Faction_Promise()
        {
            var league = NewLeague();
            var team = league.UserTeam;
            var audio = GMAudioManager.Ensure();
            int source = GMSeasonReview.SourceKind(league);
            var star = team.Roster.Where(p => !p.IsPitcher).OrderByDescending(p => GMSeasonReview.LineOf(league, p, source, team.TeamCode).War).First();

            // 조건 미충족 - 충성도 높음(70 이상)
            star.Salary = 3000;
            star.Loyalty = 85;
            Assert.IsNull(GMDynamicEventEngine.SalaryDisputeCandidate(league, team, out _), "충성도 높음 = 사건 없음");
            // 조건 충족 - WAR 1위 · 연봉 상위 5위 밖 · 충성도 50
            star.Loyalty = 50;
            team.LockerRoomTrust = 60;
            Assert.AreSame(star, GMDynamicEventEngine.SalaryDisputeCandidate(league, team, out double war));
            var events = GMDynamicEventEngine.Generate(league);
            var ev = events.Single(e => e.Kind == GMDynamicEventKind.SalaryDispute);
            Assert.AreEqual(star.InstanceId, ev.PlayerId);
            Assert.AreEqual(3, ev.Choices.Count, "선택지 3개");
            Assert.AreEqual(3, ev.ChoiceHints.Count);
            StringAssert.Contains("연봉 갈등", ev.Title);
            Assert.AreEqual(GMAudioEvent.Tension, audio.ActiveEvent, "갈등 사건 = 긴장 BGM");
            Assert.IsTrue(GMDynamicEventEngine.Pending(league).Contains(ev));
            Assert.AreEqual(0, GMDynamicEventEngine.Generate(league).Count(e => e.Kind == GMDynamicEventKind.SalaryDispute), "같은 해 중복 발생 없음");
            TestContext.WriteLine($"[GM13 사건] {ev.Title} · WAR {war:0.0} · 보상금 {ev.Cost}");

            // ① 즉시 보상금
            long budget = team.Budget;
            var r0 = GMDynamicEventEngine.Resolve(league, ev, 0);
            Assert.IsTrue(r0.Applied && r0.Success);
            Assert.AreEqual(budget - ev.Cost, team.Budget, "예산 차감");
            Assert.AreEqual(-ev.Cost, r0.BudgetDelta);
            Assert.AreEqual(65, star.Loyalty, "충성도 +15");
            Assert.AreEqual(GMAudioEvent.PositiveResult, audio.ActiveEvent, "보상금 = 환희");
            Assert.AreEqual(TeamAudioProfile.SamHwanhui, audio.CurrentBgmKey);
            Assert.IsTrue(ev.Resolved);
            Assert.IsFalse(GMDynamicEventEngine.Resolve(league, ev, 1).Applied, "종결 사건 재선택 불가");

            // ② 언론 반박 - 충성도 -20(40 미만 → 트레이드 요청)
            star.Loyalty = 50;
            var ev1 = GMDynamicEventEngine.CreateSalaryDispute(league, team, star, war);
            int trust = team.LockerRoomTrust;
            var r1 = GMDynamicEventEngine.Resolve(league, ev1, 1);
            Assert.AreEqual(30, star.Loyalty, "충성도 -20");
            Assert.AreEqual(trust - 3, team.LockerRoomTrust);
            Assert.AreEqual(star.Template.RealPlayerId, team.TradeRequestPlayerId, "트레이드 블록 등재 위험");
            Assert.AreEqual(GMAudioEvent.Tension, audio.ActiveEvent, "반박 = 긴장");

            // ③ 주장 면담 - 확률(롤 주입) 성공 · 실패
            star.Loyalty = 50;
            var ev2 = GMDynamicEventEngine.CreateSalaryDispute(league, team, star, war);
            double chance = GMDynamicEventEngine.MediationChance(league, team, ev2);
            Assert.That(chance, Is.InRange(0.10, 0.85));
            Assert.IsTrue(GMDynamicEventEngine.Resolve(league, ev2, 2, rollOverride: 0.0).Success);
            Assert.AreEqual(58, star.Loyalty, "면담 성공 +8");
            var ev3 = GMDynamicEventEngine.CreateSalaryDispute(league, team, star, war);
            Assert.IsFalse(GMDynamicEventEngine.Resolve(league, ev3, 2, rollOverride: 0.999).Success);
            Assert.AreEqual(50, star.Loyalty, "면담 실패 -8");

            // 예산 부족 = 미적용
            var ev4 = GMDynamicEventEngine.CreateSalaryDispute(league, team, star, war);
            team.Budget = 0;
            var r4 = GMDynamicEventEngine.Resolve(league, ev4, 0);
            Assert.IsFalse(r4.Applied, "운영 자금 부족");
            Assert.IsFalse(ev4.Resolved);

            // 라커룸 파벌 - Ego 4+ 3명 · 신뢰도 40 미만
            team.LockerRoomTrust = 30;
            foreach (var p in team.Roster.OrderByDescending(p => p.BaseOverall).Take(3)) p.EgoLevel = 5;
            Assert.IsTrue(GMDynamicEventEngine.FactionCondition(team));
            audio.StopAll();
            var faction = GMDynamicEventEngine.Generate(league).Single(e => e.Kind == GMDynamicEventKind.LockerRoomFaction);
            Assert.AreEqual(GMAudioEvent.Tension, audio.ActiveEvent, "파벌 표면화 = 긴장 BGM");
            Assert.IsNotEmpty(faction.RivalName);
            int activeBefore = GMPromiseSystem.Active(league).Count;
            var rf = GMDynamicEventEngine.Resolve(league, faction, 2);
            Assert.IsTrue(rf.Applied);
            Assert.AreEqual(activeBefore + 1, GMPromiseSystem.Active(league).Count, "측근 잔류 약속 = ACTIVE");
            Assert.AreEqual(GMPromiseKind.TeammateRetention, GMPromiseSystem.Active(league).Last().Kind);
            Assert.IsNull(audio.ActiveEvent, "합의 = 긴장 BGM 종료 후 화면 풀 복귀");
            var leader = team.Roster.First(p => p.InstanceId == faction.PlayerId);
            var f2 = GMDynamicEventEngine.CreateFaction(league, team);
            int leaderLoyalty = leader.Loyalty;
            GMDynamicEventEngine.Resolve(league, f2, 1);
            Assert.AreEqual(System.Math.Max(0, leaderLoyalty - 15), leader.Loyalty, "방출 경고 = 리더 충성도 -15");
            TestContext.WriteLine($"[GM13 사건] 파벌 {faction.PlayerName} ↔ {faction.RivalName} · 중재자 {faction.MediatorName} · 측근 약속 {GMPromiseSystem.Describe(GMPromiseSystem.Active(league).Last())}");
        }

        // ================================================================== 3) 선수단 회의실 UI

        [Test]
        public void T3_LockerRoomView_Layout_NoOverlap_Min15_NoBold()
        {
            var league = NewLeague();
            var team = league.UserTeam;
            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim);
            var batters = team.Roster.Where(p => !p.IsPitcher).ToList();
            GMPromiseSystem.Activate(league, GMPromiseSystem.Propose(league, team, batters[0], GMPromiseKind.StarterGuarantee, "계약 협상실"));
            GMPromiseSystem.Activate(league, GMPromiseSystem.Propose(league, team, batters[1], GMPromiseKind.TeammateRetention, "라커룸 사건", batters[2]));
            var broken = GMPromiseSystem.Propose(league, team, batters[3], GMPromiseKind.StarterGuarantee, "계약 협상실");
            GMPromiseSystem.Activate(league, broken);
            GMPromiseSystem.Break(league, team, broken, batters[3], "120타석 / 기준 223타석(규정 타석 50%)");
            int source = GMSeasonReview.SourceKind(league);
            var star = batters.OrderByDescending(p => GMSeasonReview.LineOf(league, p, source, team.TeamCode).War).First();
            star.Salary = 3000;
            star.Loyalty = 45;

            hub.SelectMainTab(2);
            Assert.AreEqual("선수단 회의실", T(hub.Root, "SubTab3"), "선수단 탭 4번째 서브 탭");
            hub.OpenLockerRoom();
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneLockerRoom, hub.CurrentPane);
            Assert.AreEqual(GMAudioScreen.Squad, hub.CurrentAudioScreen, "선수단 회의실 = 선수단 BGM 그룹");
            var pane = hub.Pane(GMOotpFrontOfficeUIController.PaneLockerRoom);
            StringAssert.Contains("/ 100", T(pane, "LrSummaryPanel/LrTrustValue"));
            StringAssert.Contains("ACTIVE 2건", T(pane, "LrPromisePanel/LrPromiseTitle"));
            StringAssert.Contains("주전 보장", T(pane, "LrPromisePanel/LrPromise0"));
            StringAssert.Contains("잔류 보장", T(pane, "LrPromisePanel/LrPromise1"));
            StringAssert.Contains("시즌 종료 판정", T(pane, "LrPromisePanel/LrPromiseDue0"));
            StringAssert.Contains("[위반]", T(pane, "LrPromisePanel/LrRecent0"));
            StringAssert.Contains(GMLockerRoom.LoyaltyLabel(team.Roster.Min(p => p.Loyalty)), T(pane, "LrPlayersPanel/LrRow0/C2"));
            var allIssues = Enumerable.Range(0, GMOotpFrontOfficeUIController.LrRows).Select(r => pane.Find($"LrPlayersPanel/LrRow{r}")).Where(t => t.gameObject.activeSelf).Select(t => T(t, "C4")).ToList();
            Assert.IsTrue(allIssues.Any(s => s.Contains("●")), "불만 사항 아이콘 + 텍스트");
            CheckPane(hub, "선수단 회의실");
            Click(pane, "LrPlayersPanel/LrNext");
            CheckPane(hub, "선수단 회의실 2쪽");

            // [라커룸 점검] → 연봉 갈등 사건 팝업 → 선택 → 종결
            Assert.GreaterOrEqual(hub.CheckLockerEvents(), 1, "사건 발생");
            Assert.IsTrue(hub.IsLockerEventOpen, "사건 팝업");
            var popup = hub.Root.Find(GMOotpFrontOfficeUIController.LockerEventPopupName);
            StringAssert.Contains("1.", T(popup, "EventChoice0"));
            CheckLayer(popup, "라커룸 사건 팝업");
            var res = hub.ChooseLockerEvent(2);
            Assert.IsNotNull(res);
            Assert.IsTrue(res.Applied);
            Assert.IsTrue(hub.LockerEvent.Resolved);
            Assert.IsNotEmpty(T(popup, "EventResult"));
            CheckLayer(popup, "라커룸 사건 팝업(결과)");
            hub.CloseLockerEvent();
            Assert.IsFalse(hub.IsLockerEventOpen);
            CheckPane(hub, "선수단 회의실(사건 종결)");
            StringAssert.Contains("[종결]", T(pane, "LrSummaryPanel/LrEvent0"));

            int bold = hub.GetComponentsInChildren<Text>(true).Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic || t.text.Contains("<b>"));
            Assert.AreEqual(0, bold, "Bold 0건");
            Assert.AreEqual(0, GMRaycastSanitizer.BlockingTexts(hub.GetComponentsInChildren<Graphic>(true)), "모바일 - 터치 가로채는 텍스트 0");
        }

        // ================================================================== 공통

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim)
        {
            var canvasGo = new GameObject("GM13_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvasGo);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var go = new GameObject("GM13_Hub", typeof(RectTransform));
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
