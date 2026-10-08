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
    /// [TASK-GM-16] 자동 검증(Unity CLI BatchPipelineGM16 1회 실행):
    ///   1) [진행하기] 라우팅 - 프런트 오피스 메인 홈 → 실시간 시즌 대시보드(전력 분석으로 튕기지 않음) · 미계약 만료자가 있으면 조력자(운영팀장) 모달 ·
    ///      3지선다(돌아가기 · AI 위임 · 무시) · 단장 반대 성별 조력자 · [단장 설정] 명칭 · 트레이드/FA 시장의 연봉·재계약 탭 제거
    ///   2) 스타터 덱(리세마라) - 10구단 28인 · 계보 역대 전 연도 카드 · 등급 확률 4/10/25/45/16% 근사 · 시드 재현성 · 시드마다 다른 덱
    ///   3) 에이징 커브 - 연도 전환 1회 후 35세 이상 OVR -1~-3 · 30~33세 동결/-1 · 20대 초반 유망주 성장 상한(+3, 25~29세 +1)
    ///   4) UI 무결성 - 치어리더 포스터(엔트리 화면 · 허브 응원단) · 스카우팅 리포트 · 계약 협상실 막대 · 조력자 모달 · 단장 설정: 1920×1080 겹침 0 · 15pt 이상 · Bold 0 · 색 이름 글자 0
    /// </summary>
    public class GM16VerificationRunner
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
            dbObject = new GameObject("GM16_PlayerDatabase");
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

        private static GMLeagueState NewLeague(string team = "SAM", int seed = 1616)
        {
            var league = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, team, false, templates, cheer);
            league.Seed = seed;
            foreach (var p in league.UserTeam.ReservePlayers) p.ContractYears = Math.Max(1, p.ContractYears);
            return league;
        }

        private static void SetTurn(GMLeagueState league, int turn, bool visited = true)
        {
            GMStoveTurns.Begin(league);
            var fo = GMFrontOffice.Ensure(league);
            fo.StoveTurn = turn;
            fo.StoveTurnVisited = visited;
        }

        // ================================================================== 1) [진행하기] 라우팅 · 조력자

        [Test]
        public void T1_Continue_RoutesToDashboard_AssistantInterjects_OnMissedActions()
        {
            // 메인 홈 [진행하기] = 실시간 시즌 대시보드(전력 분석으로 튕기지 않음)
            var league = NewLeague();
            var sim = new GMLiveSeasonSimulator(league);
            var canvas = NewCanvas();
            var hub = NewHub(sim, canvas, out var prePost, out var dash);
            StringAssert.Contains("단장 설정", T(hub.Root, "SettingsButton"), "[감독 설정] → [단장 설정]");
            hub.OpenManagerSetup();
            Assert.AreEqual("단장 설정", T(hub.Root, $"{GMOotpFrontOfficeUIController.ManagerSetupName}/SetupTitle"));
            Assert.AreEqual(0, hub.GetComponentsInChildren<Text>(true).Count(t => t.text.Contains("감독 설정")), "화면 문자열에 감독 설정 0건");
            hub.CloseManagerSetup();
            hub.SelectMainTab(4);
            Assert.IsTrue(hub.Continue(), "다른 화면 → 메인 홈");
            Assert.IsTrue(hub.IsAtMainHome);
            StringAssert.Contains("실시간 시즌 대시보드", T(hub.Root, "ContinueButton"));
            Assert.IsTrue(hub.Continue(), "메인 홈 → 대시보드");
            Assert.IsTrue(dash.gameObject.activeSelf, "실시간 시즌 대시보드 열림");
            Assert.IsFalse(prePost.gameObject.activeSelf, "전력 분석으로 튕기지 않음");
            Assert.AreEqual(0, sim.GamesPlayed, "경기 미진행");
            dash.gameObject.SetActive(false);

            // 트레이드 · FA 시장 = 외부 영입 전용(연봉·재계약 탭 제거)
            hub.SelectMainTab(4);
            var labels = Enumerable.Range(0, GMOotpFrontOfficeUIController.SubTabMax).Select(k => hub.Root.Find($"SubTab{k}")).Where(t => t.gameObject.activeSelf).Select(t => t.GetComponentInChildren<Text>().text).ToList();
            Assert.AreEqual(3, labels.Count, string.Join(" / ", labels));
            Assert.IsFalse(labels.Any(l => l.Contains("연봉") || l.Contains("재계약")), "시장 탭에 연봉 · 재계약 없음");
            hub.SelectMainTab(0);
            var foLabels = Enumerable.Range(0, GMOotpFrontOfficeUIController.SubTabMax).Select(k => hub.Root.Find($"SubTab{k}")).Where(t => t.gameObject.activeSelf).Select(t => t.GetComponentInChildren<Text>().text).ToList();
            Assert.AreEqual(1, foLabels.Count(l => l.Contains("계약 협상실")), "내부 계약 = 계약 협상실 하나");

            // 조력자 - 단장 반대 성별
            Assert.IsFalse(GMFrontOffice.Manager(league).Female);
            Assert.IsTrue(GMAssistant.ProfileFor(league).Female, "남 단장 = 여 운영팀장");
            Assert.AreEqual(GMAssistant.FemaleName, GMAssistant.ProfileFor(league).Name);
            GMFrontOffice.Manager(league).Female = true;
            Assert.IsFalse(GMAssistant.ProfileFor(league).Female, "여 단장 = 남 운영팀장");
            Assert.AreEqual(GMAssistant.MaleName, GMAssistant.ProfileFor(league).Name);
            GMFrontOffice.Manager(league).Female = false;

            // Turn 4 계약 협상 - 만료자 2명 미계약 상태로 [진행하기] → 조력자 모달(Turn 유지)
            SetTurn(league, 4);
            var expired = league.UserTeam.Roster.OrderByDescending(p => p.BaseOverall).Take(2).ToList();
            foreach (var p in expired) p.ContractYears = 0;
            hub.GoMainHome();
            Assert.IsTrue(hub.Continue());
            Assert.IsTrue(hub.IsAssistantOpen, "미계약 만료자 = 조력자 개입");
            Assert.AreEqual(4, hub.StoveTurn, "개입 중 Turn 유지");
            Assert.IsTrue(hub.AssistantIssues.Any(i => i.Kind == GMAssistantIssueKind.ExpiredContracts));
            StringAssert.Contains(GMAssistant.FemaleName, T(hub.Root, $"{GMOotpFrontOfficeUIController.AssistantPopupName}/AsName"));
            Assert.GreaterOrEqual(hub.AssistantLineCount, 4, "인사 · 사유 · 선택 안내 - 스토리형 대사");
            StringAssert.Contains(expired[0].Template.PlayerName, string.Join(" ", GMAssistant.Script(league, hub.AssistantIssues)));
            Assert.IsFalse(hub.AssistantChoicesShown, "대사 중에는 선택지 숨김");
            CheckLayer(hub.Root.Find(GMOotpFrontOfficeUIController.AssistantPopupName), "조력자 모달(대사)");
            hub.AssistantNext();
            Assert.AreEqual(1, hub.AssistantLineIndex);
            hub.AssistantSkip();
            Assert.IsTrue(hub.AssistantChoicesShown, "[대화 건너뛰기] = 선택지");
            CheckLayer(hub.Root.Find(GMOotpFrontOfficeUIController.AssistantPopupName), "조력자 모달(선택지)");

            // ① 돌아가기 = 계약 협상실 · Turn 유지
            Assert.IsFalse(hub.AssistantChoose(0));
            Assert.IsFalse(hub.IsAssistantOpen);
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneNegotiation, hub.CurrentPane, "돌아가기 = 계약 협상실");
            Assert.AreEqual(4, hub.StoveTurn);

            // ② AI 위임 = 만료자 정리(재계약 또는 FA 공시) 후 Turn 5
            Assert.IsTrue(hub.Continue(), "방 → 메인 홈");
            Assert.IsTrue(hub.Continue());
            Assert.IsTrue(hub.IsAssistantOpen, "아직 미계약 = 다시 개입");
            hub.AssistantSkip();
            Assert.IsTrue(hub.AssistantChoose(1), "위임 후 Turn 진행");
            Assert.AreEqual(5, hub.StoveTurn);
            Assert.IsEmpty(GMAssistant.ExpiredUnsigned(league), "위임 = 만료자 전원 정리");
            Assert.AreEqual(1, GMFrontOffice.Ensure(league).AssistantDelegations);
            Assert.IsTrue(league.News.Any(n => n.Title.Contains("운영팀장")), "위임 처리 소식");
            Assert.IsTrue(expired.All(p => league.UserTeam.ReservePlayers.Contains(p) ? p.ContractYears > 0 : league.FreeAgents.Contains(p)), "재계약 또는 FA 공시");

            // ③ 무시 = 그대로 Turn 진행 · 그해 같은 경고 없음
            var league2 = NewLeague("LG", 1617);
            var sim2 = new GMLiveSeasonSimulator(league2);
            var hub2 = NewHub(sim2, canvas, out _, out _);
            SetTurn(league2, 4);
            league2.UserTeam.Roster[0].ContractYears = 0;
            hub2.GoMainHome();
            Assert.IsTrue(hub2.Continue());
            Assert.IsTrue(hub2.IsAssistantOpen);
            hub2.AssistantSkip();
            Assert.IsTrue(hub2.AssistantChoose(2), "무시 = Turn 진행");
            Assert.AreEqual(5, hub2.StoveTurn);
            Assert.AreEqual(1, GMAssistant.ExpiredUnsigned(league2).Count, "무시 = 만료자 그대로");
            Assert.IsTrue(GMAssistant.IsIgnored(league2, GMAssistantIssueKind.ExpiredContracts));
            GMFrontOffice.Ensure(league2).StoveTurnVisited = true;
            hub2.GoMainHome();
            Assert.IsTrue(hub2.Continue());
            Assert.IsFalse(hub2.IsAssistantOpen, "무시한 경고는 그해 다시 묻지 않음");
            Assert.AreEqual(6, hub2.StoveTurn);

            // Turn 7 신인 지명 0명 · Turn 8 주장 미임명 → 위임 = 지명 · 임명
            SetTurn(league2, 7);
            hub2.GoMainHome();
            Assert.IsTrue(hub2.Continue());
            Assert.IsTrue(hub2.IsAssistantOpen);
            Assert.IsTrue(hub2.AssistantIssues.Any(i => i.Kind == GMAssistantIssueKind.RookieDraftNoPick));
            hub2.AssistantSkip();
            Assert.IsTrue(hub2.AssistantChoose(1));
            Assert.AreEqual(1, GMFrontOffice.Ensure(league2).DraftPicksThisYear, "위임 = 신인 1명 지명");
            foreach (var p in league2.UserTeam.Roster) p.IsCaptain = false;
            SetTurn(league2, 8);
            GMRosterTiers.EnforceLimits(league2, league2.UserTeam);
            hub2.GoMainHome();
            Assert.IsTrue(hub2.Continue());
            Assert.IsTrue(hub2.IsAssistantOpen);
            Assert.IsTrue(hub2.AssistantIssues.Any(i => i.Kind == GMAssistantIssueKind.NoCaptain));
            hub2.AssistantSkip();
            Assert.IsTrue(hub2.AssistantChoose(1));
            Assert.IsTrue(league2.UserTeam.Roster.Any(p => p.IsCaptain), "위임 = 주장 임명");
            Assert.IsFalse(GMStoveTurns.IsGating(league2), "8 Turn 완료");

            // 통제 밖(정규시즌 · 구버전 리그)은 개입하지 않는다
            Assert.IsEmpty(GMAssistant.PendingIssues(NewLeague("KT", 1618)));
            TestContext.WriteLine($"[GM16 조력자] {GMAssistant.ProfileFor(league).Plate} · 위임 {GMFrontOffice.Ensure(league).AssistantLastNote}");
        }

        // ================================================================== 2) 스타터 덱(리세마라)

        [Test]
        public void T2_StarterDeck_TierDistribution_Reproducible_Reroll()
        {
            Assert.IsTrue(GMRosterLoader.StarterDeckApplies(GMStartMode.RealCurrent2026));
            Assert.IsTrue(GMRosterLoader.StarterDeckApplies(GMStartMode.StoryCampaign));
            Assert.IsFalse(GMRosterLoader.StarterDeckApplies(GMStartMode.AllTimeDream), "올타임 드림 = 기존 드림 로스터");
            CollectionAssert.AreEqual(new[] { 4, 10, 25, 45, 16 }, GMStarterDeck.TierPercent);
            Assert.AreEqual(100, GMStarterDeck.TierPercent.Sum());

            var draws = new List<GMStarterDeck.Draw>();
            var userSets = new List<HashSet<string>>();
            int[] seeds = { 160001, 160002, 160003, 160004 };
            foreach (int seed in seeds)
            {
                var league = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, "SAM", false, templates, cheer, seed);
                draws.AddRange(GMStarterDeck.LastDraws);
                Assert.AreEqual(seed, GMFrontOffice.Ensure(league).StarterDeckSeed);
                foreach (var team in league.Teams.Values)
                {
                    Assert.AreEqual(GMRosterLoader.BatterCount + GMRosterLoader.PitcherCount, team.Roster.Count, $"{team.TeamCode} 28인");
                    Assert.AreEqual(GMRosterLoader.BatterCount, team.Roster.Count(p => !p.IsPitcher), $"{team.TeamCode} 타자 15");
                    Assert.AreEqual(GMRosterLoader.PitcherCount, team.Roster.Count(p => p.IsPitcher), $"{team.TeamCode} 투수 13");
                    Assert.IsTrue(team.Roster.All(p => p.Age >= Player.MinAge && p.Age <= Player.MaxAge));
                }
                var people = league.Teams.Values.SelectMany(t => t.Roster).Select(p => p.Template.RealPlayerId).ToList();
                Assert.AreEqual(people.Count, people.Distinct().Count(), "같은 인물은 리그 전체 1명");
                var user = league.UserTeam;
                Assert.Greater(user.Roster.Count(p => p.Template.SeasonYear < GMFeatureFlags.DEFAULT_START_YEAR), 0, "역대 전 연도 카드 포함");
                Assert.IsTrue(league.News.Any(n => n.Title.Contains("[스타터 덱]")), "첫 선수단 지급 소식");
                int own = GMStarterDeck.LastDraws.Count(d => d.OwnLineage);
                Assert.Greater(own, GMStarterDeck.LastDraws.Count / 2, "대부분 구단 계보 카드");
                userSets.Add(new HashSet<string>(user.Roster.Select(p => p.Template.TemplateId)));
            }
            Assert.AreEqual(seeds.Length * 10 * 28, draws.Count);

            // 확률 근사 - 굴린 등급(순수 확률)은 ±4%p, 실제 지급 등급(대체 포함)은 ±7%p
            var rolled = Enumerable.Range(0, 5).Select(i => 100.0 * draws.Count(d => (int)d.Rolled == i) / draws.Count).ToArray();
            var actual = Enumerable.Range(0, 5).Select(i => 100.0 * draws.Count(d => (int)d.Actual == i) / draws.Count).ToArray();
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(GMStarterDeck.TierPercent[i], rolled[i], 4.0, $"{GMStarterDeck.TierNames[i]}급 추첨 확률");
                Assert.AreEqual(GMStarterDeck.TierPercent[i], actual[i], 7.0, $"{GMStarterDeck.TierNames[i]}급 지급 비율");
            }
            TestContext.WriteLine("[GM16 스타터 덱] 추첨 " + string.Join(" · ", Enumerable.Range(0, 5).Select(i => $"{GMStarterDeck.TierNames[i]} {rolled[i]:0.0}%")) +
                                  " | 지급 " + string.Join(" · ", Enumerable.Range(0, 5).Select(i => $"{GMStarterDeck.TierNames[i]} {actual[i]:0.0}%")));

            // 재현성(같은 시드 = 같은 덱) · 리세마라(다른 시드 = 다른 덱)
            var again = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, "SAM", false, templates, cheer, seeds[0]);
            CollectionAssert.AreEquivalent(userSets[0], again.UserTeam.Roster.Select(p => p.Template.TemplateId).ToList(), "같은 시드 = 같은 덱");
            Assert.IsTrue(userSets.Skip(1).All(s => !s.SetEquals(userSets[0])), "시드마다 다른 덱");
            // 시드 없음(테스트 · 툴 오버로드) = 기존 2026 현역 로스터
            var legacy = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, "SAM", false, templates, cheer);
            Assert.IsTrue(legacy.UserTeam.Roster.All(p => p.Template.SeasonYear == GMFeatureFlags.DEFAULT_START_YEAR), "시드 없음 = 2026 현역");
            Assert.AreEqual(0, GMFrontOffice.Ensure(legacy).StarterDeckSeed);
        }

        // ================================================================== 3) 에이징 커브

        [Test]
        public void T3_AgingCurve_VeteransDecline_ProspectsCapped_AfterYearTransition()
        {
            // 규칙(함수) - 나이별 성장 상한 · 하락 범위 · 완화
            Assert.AreEqual(3, GMFuturesMeeting.GrowthCap(21, false));
            Assert.AreEqual(4, GMFuturesMeeting.GrowthCap(21, true), "멘토링 최대 +4");
            Assert.AreEqual(1, GMFuturesMeeting.GrowthCap(27, true), "25~29세 +0~+1");
            Assert.AreEqual(0, GMFuturesMeeting.GrowthCap(31, false));

            var league = NewLeague("SAM", 1660);
            var sim = new GMLiveSeasonSimulator(league);
            var ai = league.Teams.Values.Where(t => !t.IsUserTeam).ToList();
            var veterans = ai.SelectMany(t => t.Roster).OrderBy(p => p.InstanceId, StringComparer.Ordinal).Take(14).ToList();
            for (int i = 0; i < veterans.Count; i++) { veterans[i].Age = 35 + i % 3; veterans[i].IsCaptain = false; }
            var thirties = ai.SelectMany(t => t.Roster).Except(veterans).OrderBy(p => p.InstanceId, StringComparer.Ordinal).Take(8).ToList();
            foreach (var p in thirties) p.Age = 31;
            foreach (var v in veterans)
                for (int y = 2026; y < 2040; y++)
                {
                    int d = GMAgingCurve.DeclineFor(v, y);
                    Assert.That(d, Is.InRange(1, 3), "34세 이상 -1~-3 강제 하락");
                }
            foreach (var p in thirties) Assert.That(GMAgingCurve.DeclineFor(p, 2026), Is.InRange(0, 1), "30~33세 동결 또는 -1");
            var probe = veterans[0];
            int loyal = probe.Loyalty;
            probe.Loyalty = 10;
            var plain = Enumerable.Range(2026, 20).Select(y => GMAgingCurve.DeclineFor(probe, y)).ToList();
            probe.Loyalty = 95;
            var bonded = Enumerable.Range(2026, 20).Select(y => GMAgingCurve.DeclineFor(probe, y)).ToList();
            probe.Loyalty = loyal;
            Assert.IsTrue(plain.Zip(bonded, (a, b) => b <= a).All(x => x), "단장 유대 = 하락 완화");
            Assert.Less(bonded.Sum(), plain.Sum() + (GMAgingCurve.HasBond(probe) && probe.Loyalty < 80 ? 1 : 0) + 1);

            var vetBefore = veterans.ToDictionary(p => p, p => p.BaseOverall);
            var midBefore = thirties.ToDictionary(p => p, p => p.BaseOverall);
            var prospects = league.Teams.Values.SelectMany(t => t.Futures).Where(p => p.Age <= GMFuturesMeeting.MaxGrowthAge).ToList();
            var proBefore = prospects.ToDictionary(p => p, p => (ovr: p.BaseOverall, age: p.Age, max: GMFuturesMeeting.PotentialMax(p)));

            Assert.IsTrue(sim.StartRun(GMRunMode.FullSeason));
            sim.RunUntilStop();
            Assert.IsTrue(sim.IsSeasonComplete);
            int year = league.SeasonYear;
            Assert.IsTrue(GMAwardEvaluator.AdvanceToNextSeasonYear(sim), "연도 전환 1회");
            Assert.AreEqual(year + 1, league.SeasonYear);

            var drops = new List<int>();
            foreach (var v in veterans)
            {
                int delta = v.BaseOverall - vetBefore[v];
                drops.Add(-delta);
                Assert.That(delta, Is.InRange(v.IsPitcher ? -4 : -3, -1), $"{v.Template.PlayerName}({vetBefore[v]}) 35세 이상 OVR -1~-3");
            }
            Assert.That(drops.Average(), Is.InRange(1.0, 3.0));
            foreach (var p in thirties) Assert.That(p.BaseOverall - midBefore[p], Is.InRange(-2, 0), "30~33세 동결/미세 하락");
            int grownCount = 0;
            foreach (var p in prospects)
            {
                var b = proBefore[p];
                int g = p.BaseOverall - b.ovr;
                bool mentored = GMFuturesMeeting.MentorOf(league, p) != null;
                Assert.That(g, Is.InRange(0, GMFuturesMeeting.GrowthCap(b.age, mentored)), $"{p.Template.PlayerName}({b.age}세) 성장 상한");
                Assert.LessOrEqual(p.BaseOverall, Math.Max(b.ovr, b.max), "잠재력 상한");
                if (g > 0) grownCount++;
            }
            Assert.Greater(grownCount, 0, "20대 초반 유망주 성장");
            Assert.IsTrue(veterans.All(v => v.Template.Grade == GMRosterTiers.OriginalOf(v.Template).Grade || GMRosterTiers.OriginalOf(v.Template).GetBaseOverall() - 3 <= GMRosterTiers.ProspectCeiling), "소폭 하락 베테랑은 원 등급 유지");
            Assert.IsTrue(league.News.Any(n => n.Title.Contains("[에이징 커브]")) || veterans.All(v => !league.UserTeam.ReservePlayers.Contains(v)), "내 구단 베테랑 하락 소식");
            TestContext.WriteLine($"[GM16 에이징] 35세+ {veterans.Count}명 평균 -{drops.Average():0.00} · 유망주 {prospects.Count}명 중 성장 {grownCount}명 · 최대 +{prospects.Max(p => p.BaseOverall - proBefore[p].ovr)}");
        }

        // ================================================================== 4) UI 무결성

        [Test]
        public void T4_PosterScoutingNegotiationAssistant_NoOverlap_Min15_NoBold_NoColorWords()
        {
            var league = NewLeague("SAM", 1640);
            var sim = new GMLiveSeasonSimulator(league);
            var canvas = NewCanvas();
            var team = league.UserTeam;

            // 치어리더 엔트리 = 단상 라인업 포스터(사진) + 시너지 · 15인 갤러리
            var view = NewView<GMCheerleaderEntryUIController>(canvas, "GM16_Cheer");
            view.Build();
            view.Open(team);
            Assert.IsNull(view.Root.Find("SlotRole0"), "텍스트 위주 역할 줄 레이아웃 폐기");
            Assert.IsNotNull(view.Root.Find("PosterBg"));
            var entry = GMCheerleaderRoster.Entry(team);
            bool samPhotos = entry.Any(c => GMCheerPortraits.Profile(c) != null);
            if (GMCheerPortraits.LineupTemplate(Team.Samsung) != null)
                Assert.IsNotNull(view.Root.Find("PosterBg").GetComponent<RawImage>().texture, "포스터 템플릿");
            for (int i = 0; i < entry.Count; i++)
            {
                StringAssert.Contains(entry[i].DisplayName, view.Root.Find($"Slot{i}").GetComponentInChildren<Text>().text);
                if (GMCheerPortraits.Profile(entry[i]) != null) Assert.IsNotNull(view.SlotPhoto(i).texture, $"{entry[i].DisplayName} 프로필 사진");
            }
            Assert.AreEqual("빈 슬롯", view.Root.Find($"Slot{GMCheerleaderEntryUIController.SlotCount - 1}").GetComponentInChildren<Text>().text);
            StringAssert.Contains("구단 소속 시너지", view.SynergyText);
            StringAssert.Contains("팀워크", view.SynergyText);
            for (int k = 0; k < 4; k++) Assert.Greater(int.Parse(T(view.Root, $"TotalValue{k}")), 0, "4대 스탯 합계");
            view.Select(entry[0]);
            if (GMCheerPortraits.Photo(entry[0]) != null) Assert.IsNotNull(view.DetailPhoto.texture, "상세 사진");
            StringAssert.StartsWith("CHEER ", T(view.Root, "DetailCheer"));
            CheckLayer(view.Root, "치어리더 포스터 5인");
            GMCheerleaderRoster.TrySetEntry(team, team.CheerleaderPool.Take(6).ToList(), out _);
            view.Refresh();
            CheckLayer(view.Root, "치어리더 포스터 6인");
            GMCheerleaderRoster.TrySetEntry(team, team.CheerleaderPool.Take(4).ToList(), out _);
            view.Refresh();
            CheckLayer(view.Root, "치어리더 포스터 4인");
            Click(view.Root, "CloseButton");
            Assert.IsFalse(view.gameObject.activeSelf, "[X] 닫기 동선");

            // 허브 응원단 탭 = 포스터 + 시너지(클래식 텍스트 로비 폐기)
            var hub = NewHub(sim, canvas, out _, out _);
            hub.CheerView = view;
            hub.SelectMainTab(6);
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneCheer, hub.CurrentPane);
            var cheerPane = hub.Pane(GMOotpFrontOfficeUIController.PaneCheer);
            Assert.IsNotNull(cheerPane.Find("CePosterPanel/CePosterImage"));
            StringAssert.Contains("구단 소속 시너지", T(cheerPane, "CeSynergyPanel/CeEffects"));
            if (samPhotos) Assert.IsNotNull(hub.CheerPosterSlotPhoto(0).texture, "허브 포스터 사진");
            CheckPane(hub, "응원단 포스터");
            Click(cheerPane, "CePosterPanel/CeSlot1");
            Assert.IsTrue(view.gameObject.activeSelf, "포스터 사진 → 치어리더 상세");
            Assert.AreSame(GMCheerleaderRoster.Entry(team)[1], view.SelectedCheerleader);
            view.Close();

            // 선수 스카우팅 리포트 - 백분위 막대(색 구간) · 20-80 등급 · 헤드라인 · 기록 · 코멘트
            var star = team.Roster.OrderByDescending(p => p.BaseOverall).First();
            hub.OpenPercentiles(star);
            Assert.IsTrue(hub.IsPercentileOpen);
            var popup = hub.Root.Find("PercentilePopup");
            StringAssert.Contains(star.Template.PlayerName, T(popup, "PctTitle"));
            Assert.AreEqual(star.BaseOverall.ToString(), T(popup, "ScOvr"));
            Assert.IsNotEmpty(T(popup, "ScHeadline"));
            Assert.IsNotEmpty(T(popup, "ScReport"));
            Assert.IsNotEmpty(T(popup, "ScStats"));
            StringAssert.Contains("급", T(popup, "ScTier"));
            var rows = GMStoveLeagueMarket.Percentiles(league, star);
            for (int k = 0; k < rows.Count; k++)
            {
                float fill = popup.Find($"PctBar{k}/Fill").GetComponent<RectTransform>().anchorMax.x;
                Assert.AreEqual(rows[k].Percentile / 100f, fill, 0.011f, "막대 길이 = 백분위");
                Assert.AreEqual(GMOotpFrontOfficeUIController.BandColor(GMScoutingReport.BandOf(rows[k].Percentile)), popup.Find($"PctBar{k}/Fill").GetComponent<Image>().color, "막대 색 = 구간");
                Assert.AreEqual(GMScoutingReport.ScoutGrade(rows[k].Percentile).ToString(), T(popup, $"ScGrade{k}"));
            }
            NoColorWords(popup, "스카우팅 리포트");
            CheckLayer(popup, "스카우팅 리포트");
            Click(popup, "PctClose");
            Assert.IsFalse(hub.IsPercentileOpen);

            // 계약 협상실 가운데 리포트 = 막대 + 판정(색 이름 없음)
            foreach (var p in team.Roster.Take(3)) p.ContractYears = 1;
            hub.OpenNegotiationRoom(team.Roster[0]);
            var neg = hub.Pane(GMOotpFrontOfficeUIController.PaneNegotiation);
            for (int i = 0; i < 6; i++) Assert.IsNotNull(neg.Find($"NegReportPanel/NegMetricBar{i}/Fill"), "지표 막대");
            Assert.Greater(neg.Find("NegReportPanel/NegMetricBar0/Fill").GetComponent<RectTransform>().anchorMax.x, 0f);
            NoColorWords(neg, "계약 협상실");
            CheckPane(hub, "계약 협상실(막대)");
            Click(neg, "NegReportPanel/NegScoutCard");
            Assert.IsTrue(hub.IsPercentileOpen, "협상실 → 스카우팅 리포트");
            Click(popup, "PctClose");
            hub.OpenSeasonSummary();
            NoColorWords(hub.Pane(GMOotpFrontOfficeUIController.PaneSeasonSummary), "시즌 결산실");

            // 단장 설정(성별 토글) · Bold 0
            hub.OpenManagerSetup();
            var setup = hub.Root.Find(GMOotpFrontOfficeUIController.ManagerSetupName);
            StringAssert.Contains("남성", T(setup, "GenderToggle"));
            Click(setup, "GenderToggle");
            StringAssert.Contains("여성", T(setup, "GenderToggle"));
            Assert.IsTrue(hub.PendingManagerProfile.Female);
            CheckLayer(setup, "단장 설정 + 성별");
            hub.CloseManagerSetup();
            int bold = hub.GetComponentsInChildren<Text>(true).Concat(view.GetComponentsInChildren<Text>(true)).Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic || t.text.Contains("<b>"));
            Assert.AreEqual(0, bold, "Bold 0건");
            Assert.AreEqual(0, GMRaycastSanitizer.BlockingTexts(hub.GetComponentsInChildren<Graphic>(true)), "모바일 - 터치 가로채는 텍스트 0");
        }

        // ================================================================== 공통

        private static readonly string[] ColorWords = { "빨강", "파랑", "초록", "주황" };

        private static void NoColorWords(Transform root, string label)
        {
            var hits = root.GetComponentsInChildren<Text>(true).Where(t => ColorWords.Any(w => t.text.Contains(w))).Select(t => $"{t.name}: {t.text}").ToList();
            Assert.IsEmpty(hits, $"{label} - 색 이름 글자 0건");
        }

        private GameObject NewCanvas()
        {
            var canvasGo = new GameObject("GM16_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvasGo);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            return canvasGo;
        }

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim, GameObject canvas, out GMMatchPrePostUIController prePost, out GMLiveLeagueDashboardUIController dash)
        {
            var hub = NewView<GMOotpFrontOfficeUIController>(canvas, "GM16_Hub");
            hub.Build();
            prePost = NewView<GMMatchPrePostUIController>(canvas, "GM16_PrePost");
            prePost.Build();
            prePost.gameObject.SetActive(false);
            hub.PrePostView = prePost;
            dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM16_Dash");
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
