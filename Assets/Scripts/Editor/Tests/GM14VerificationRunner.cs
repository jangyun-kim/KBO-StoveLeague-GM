using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-GM-14] 자동 검증(Unity CLI BatchPipelineGM14 1회 실행):
    ///   1) 144경기 초과 방지 - 정규시즌 완주 후 포스트시즌 10경기 강제 진행 · 추가 진행 시도에도 순위표 G = 144 고정
    ///   2) 포스트시즌 무승부 차단 - 포스트시즌 경기 결과는 항상 승/패(무승부 · 재경기 0)
    ///   3) 3연전 일정 - 팀당 144경기 · 상대당 16경기(홈 8 · 원정 8) · 2/3연전 블록 · 월요일 휴식 · 「KBO 시즌 N」
    ///   4) 오디오 방어벽 · 협상 효과음 · 동료 연봉 연쇄 · 8 Turn 통제 · [진행하기] 라우팅
    /// </summary>
    public class GM14VerificationRunner
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
            dbObject = new GameObject("GM14_PlayerDatabase");
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

        private static GMLeagueState NewLeague(string team = "SAM", int seed = 1414)
        {
            var league = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        // ================================================================== 1) · 2) 144경기 고정 · 포스트시즌 무승부 없음

        [Test]
        public void T1_T2_Season144_PostseasonSeparated_NoTies()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague());
            Assert.IsTrue(sim.StartRun(GMRunMode.FullSeason));
            Assert.IsTrue(GMAudioManager.SimulationMode, "한 시즌 고속 진행 = 시뮬레이션 모드");
            sim.RunUntilStop();
            Assert.IsFalse(GMAudioManager.SimulationMode, "진행 종료 = 시뮬레이션 모드 해제");
            Assert.IsTrue(sim.IsSeasonComplete);
            var league = sim.League;
            Dictionary<string, (int g, int w, int d, int l)> Snapshot() => league.Records.Values.ToDictionary(r => r.TeamCode, r => (r.G, r.W, r.D, r.L));
            var before = Snapshot();
            Assert.AreEqual(10, before.Count);
            Assert.IsTrue(before.Values.All(r => r.g == 144 && r.w + r.d + r.l == 144), "정규시즌 팀당 144경기");

            // 정규시즌 추가 진행 시도 - 모두 거부
            Assert.IsFalse(sim.StepGameDay());
            Assert.IsNull(sim.BeginLiveDay());
            Assert.IsFalse(sim.StartRun(GMRunMode.SingleGame));

            // 포스트시즌 10경기 강제 진행(타 구단 자동 + 내 구단 세션)
            var ps = GMAwardEvaluator.BeginPostseason(sim);
            Assert.IsNotNull(ps);
            int played = 0, sessions = 0;
            for (int i = 0; i < 40 && played < 10 && !ps.Completed; i++)
            {
                var next = GMAwardEvaluator.NextPostseasonGame(sim);
                if (next == null) break;
                if (next.HomeCode == league.SelectedTeamCode || next.AwayCode == league.SelectedTeamCode)
                {
                    var s = GMAwardEvaluator.BeginPostseasonSession(sim, next);
                    var box = GMAwardEvaluator.CompletePostseasonSession(sim, next, s);
                    Assert.IsNotNull(box);
                    Assert.AreNotEqual(box.HomeR, box.AwayR, "내 구단 포스트시즌 세션 무승부 없음");
                    Assert.IsFalse(s.SeasonRecorded, "포스트시즌 세션은 정규시즌 미반영");
                    sessions++;
                }
                else GMAwardEvaluator.PlayNextPostseasonGame(sim);
                played++;
            }
            Assert.GreaterOrEqual(played, 10, "포스트시즌 10경기 진행");
            var after = Snapshot();
            foreach (var pair in before) Assert.AreEqual(pair.Value, after[pair.Key], $"{pair.Key} 정규시즌 성적 불변(G 144)");
            Assert.AreEqual(144, sim.GamesPlayed);
            var games = ps.Series.SelectMany(x => x.Games).ToList();
            Assert.IsFalse(games.Any(g => g.Contains("무승부")), "포스트시즌 무승부 · 재경기 0건");
            TestContext.WriteLine($"[GM14 포스트시즌] {played}경기(내 구단 세션 {sessions}) · " + string.Join(" / ", games.Take(10)));

            // 같은 시드라도 포스트시즌 단판은 무조건 승패 - 30경기
            var teams = league.Teams.Values.ToList();
            int extra = 0;
            for (int k = 0; k < 30; k++)
            {
                var home = teams[k % 10];
                var away = teams[(k + 3) % 10];
                var r = GMAwardEvaluator.PlayExhibition(sim, home.AvailableRoster, away.AvailableRoster, home.TeamCode, away.TeamCode, null, null, 9000 + k, true);
                Assert.AreNotEqual(r.HomeRuns, r.AwayRuns, $"포스트시즌 단판 {k} 무승부 없음");
            }
            // 엔진 직접 - 포스트시즌 끝장 승부(12회 상한 없음)
            for (int k = 0; k < 60; k++)
            {
                var home = teams[k % 10];
                var away = teams[(k + 1) % 10];
                var engine = new MatchEngine(home.AvailableRoster, away.AvailableRoster, TeamPowerModifiers.None, TeamPowerModifiers.None, null, null, 4000 + k);
                var res = engine.PlayFullMatch(home.TeamCode, away.TeamCode, true);
                Assert.IsNotNull(res.WinnerTeamName, $"엔진 포스트시즌 {k} 승자 존재");
                if (res.AwayInningScores.Count > 12) extra++;
            }
            Assert.AreEqual(after, Snapshot(), "단판 · 엔진 직접 경기도 순위표 미반영");
            TestContext.WriteLine($"[GM14 끝장 승부] 엔진 60경기 중 13회 이상 {extra}경기");
        }

        // ================================================================== 3) 3연전 일정 · 시즌 표기

        [Test]
        public void T3_Schedule_ThreeGameSeries_16PerOpponent_8Home8Away()
        {
            var codes = NameAliasTable.CanonicalTeamCodes;
            var schedule = GMLiveSeasonSimulator.BuildSchedule(codes);
            Assert.AreEqual(144, schedule.Length);
            var games = codes.ToDictionary(c => c, c => 0);
            var pair = new Dictionary<(string, string), int>(); // (home, away)
            for (int d = 0; d < 144; d++)
            {
                Assert.AreEqual(5, schedule[d].Count, $"{d}일 5경기");
                Assert.AreEqual(10, schedule[d].SelectMany(m => new[] { m.home, m.away }).Distinct().Count(), $"{d}일 10구단 모두 1경기");
                foreach (var (home, away) in schedule[d])
                {
                    games[home]++; games[away]++;
                    pair.TryGetValue((home, away), out int n);
                    pair[(home, away)] = n + 1;
                }
            }
            Assert.IsTrue(games.Values.All(g => g == 144), "팀당 144경기");
            foreach (var a in codes)
                foreach (var b in codes.Where(x => x != a))
                {
                    pair.TryGetValue((a, b), out int home);
                    pair.TryGetValue((b, a), out int away);
                    Assert.AreEqual(16, home + away, $"{a}-{b} 16차전");
                    Assert.AreEqual(8, home, $"{a} 홈 {b}전 8경기");
                }

            // 시리즈 - 같은 블록 동안 같은 대진 · 3연전 36개(화~목 · 금~일) · 2연전 18개 · 월요일 휴식 · 개막 토요일
            int three = 0, two = 0;
            for (int d = 0; d < 144;)
            {
                var (len, game) = GMLiveSeasonSimulator.SeriesOf(d);
                Assert.AreEqual(1, game, $"{d}일 시리즈 시작");
                for (int g = 1; g < len; g++) CollectionAssert.AreEqual(schedule[d], schedule[d + g], $"{d}일 시리즈 대진 유지");
                if (d + len < 144) CollectionAssert.AreNotEqual(schedule[d], schedule[d + len], $"{d}일 다음 시리즈 = 다른 대진");
                var dow = GMLiveSeasonSimulator.DateOf(d, 2026).DayOfWeek;
                if (len == 3) { three++; Assert.IsTrue(dow == DayOfWeek.Tuesday || dow == DayOfWeek.Friday, $"3연전 시작 {dow}"); }
                else two++;
                d += len;
            }
            Assert.AreEqual(36, three, "3연전 36개");
            Assert.AreEqual(18, two, "2연전 18개(개막 주말 1 + 막판 17)");
            Assert.AreEqual(DayOfWeek.Saturday, GMLiveSeasonSimulator.DateOf(0, 2026).DayOfWeek, "개막 토요일");
            Assert.AreEqual("03/28/2026", GMLiveSeasonSimulator.DateLabel(0));
            Assert.IsFalse(Enumerable.Range(0, 144).Any(d => GMLiveSeasonSimulator.DateOf(d, 2026).DayOfWeek == DayOfWeek.Monday), "월요일 휴식");
            Assert.AreEqual(144, Enumerable.Range(0, 144).Select(d => GMLiveSeasonSimulator.DateOf(d, 2026)).Distinct().Count(), "하루 1경기일");
            Assert.AreEqual("3연전 2차전", GMLiveSeasonSimulator.SeriesLabel(3));
            TestContext.WriteLine($"[GM14 일정] 개막 {GMLiveSeasonSimulator.DateLabel(0)} · 최종전 {GMLiveSeasonSimulator.DateLabel(143)} · 3연전 {three} · 2연전 {two}");

            // 시뮬레이터 실제 일정 = 같은 생성기
            var sim = new GMLiveSeasonSimulator(NewLeague());
            for (int d = 0; d < 144; d += 17) CollectionAssert.AreEqual(schedule[d], sim.MatchesOn(d).ToList());

            // 「KBO 시즌 N」
            var league = sim.League;
            Assert.AreEqual(1, league.SeasonNumber);
            Assert.AreEqual("KBO 시즌 1", GMLeagueState.SeasonTitle(league.SeasonNumber));
            league.SeasonYear++;
            Assert.AreEqual("KBO 시즌 2", GMLeagueState.SeasonTitle(league.SeasonNumber));
        }

        // ================================================================== 4) 오디오 · 협상 · 8 Turn · 라우팅

        [Test]
        public void T4_AudioGuard_NegotiationSfx_SalaryChain_TurnGating_ContinueRouting()
        {
            var audio = GMAudioManager.Ensure();
            var league = NewLeague(seed: 1415);
            var sim = new GMLiveSeasonSimulator(league);

            // ① 고속 시뮬레이션 중 이벤트 BGM 하이재킹 무시
            audio.EnterScreen(GMAudioScreen.Hub, "SAM");
            string bgm = audio.CurrentBgmKey;
            Assert.IsTrue(sim.StartRun(GMRunMode.FirstHalf));
            Assert.IsTrue(GMAudioManager.SimulationMode);
            int ignored = GMAudioManager.IgnoredEventCount;
            Assert.IsNull(audio.PlayEventBgm(GMAudioEvent.Tension, "SAM"), "고속 진행 중 하이재킹 무시");
            Assert.IsNull(audio.ActiveEvent);
            Assert.AreEqual(bgm, audio.CurrentBgmKey, "메인 BGM 유지");
            Assert.AreEqual(ignored + 1, GMAudioManager.IgnoredEventCount);
            sim.Stop();
            Assert.IsFalse(GMAudioManager.SimulationMode, "정지 = 해제");
            Assert.AreEqual(TeamAudioProfile.SamEldorado, audio.PlayEventBgm(GMAudioEvent.MajorResult, "SAM"), "해제 후 정상 하이재킹");
            audio.EndEvent();
            Assert.IsTrue(sim.StartRun(GMRunMode.SingleGame));
            Assert.IsFalse(GMAudioManager.SimulationMode, "한 경기는 고속 진행 아님");
            sim.Stop();

            // ② 동료 연봉 연쇄 - 대폭 인상 = 유대 동료 +2 · 동급 비유대 동료 박탈감
            var team = league.UserTeam;
            var batters = team.Roster.Where(p => !p.IsPitcher).OrderBy(p => p.BaseOverall).ToList();
            var target = batters[batters.Count / 2];
            int oldSalary = target.Salary, newSalary = oldSalary + Math.Max(GMSalaryChain.BigRaiseMin, oldSalary / 2);
            var bonded = GMPlayerBonds.For(team, target).Select(b => b.partner).Distinct().Take(GMSalaryChain.MaxBonded).ToList();
            var peers = team.Roster.Where(q => q != target && q.Template != null && !bonded.Contains(q) && q.IsPitcher == target.IsPitcher && q.Salary < newSalary && q.BaseOverall >= target.BaseOverall - GMSalaryChain.PeerOvrGap)
                .OrderByDescending(q => q.BaseOverall).ThenBy(q => q.InstanceId).Take(GMSalaryChain.MaxPeers).ToList();
            foreach (var q in bonded.Concat(peers)) q.Loyalty = 60;
            target.Salary = newSalary;
            var effects = GMSalaryChain.Apply(league, team, target, oldSalary, newSalary, GMNegotiationOutcome.AcceptDemand);
            Assert.IsTrue(GMSalaryChain.IsBigRaise(oldSalary, newSalary));
            Assert.AreEqual(bonded.Count + peers.Count, effects.Count);
            Assert.Greater(peers.Count, 0, "비교 대상 동료 존재");
            foreach (var b in bonded) Assert.AreEqual(62, b.Loyalty, "유대 동료 +2");
            foreach (var q in peers) Assert.AreEqual(60 - (q.EgoLevel >= 4 ? GMSalaryChain.EgoPeerEnvy : GMSalaryChain.PeerEnvy), q.Loyalty, "상대적 박탈감");
            Assert.IsTrue(league.News.Any(n => n.Title.Contains("동료들 반응")));
            Assert.IsEmpty(GMSalaryChain.Apply(league, team, target, newSalary, newSalary, GMNegotiationOutcome.Freeze), "동결 = 연쇄 없음");
            TestContext.WriteLine($"[GM14 연쇄] {target.Template.PlayerName} {oldSalary} → {newSalary}: {string.Join(" · ", effects)}");

            // ③ 8 Turn 통제 + [진행하기] 라우팅 + 협상 효과음
            GMStoveTurns.Begin(league);
            var hub = NewHub(sim, out var prePost);
            hub.GoMainHome();
            Assert.AreEqual(1, hub.StoveTurn);
            StringAssert.Contains("Turn 1/8 시즌 결산", T(hub.Root, "TeamDate"));
            hub.SelectMainTab(4);
            hub.SelectSubTab(0);
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneFA, hub.CurrentPane);
            Assert.IsTrue(hub.IsTurnLockShown, "Turn 5 전 FA 방 = 잠금");
            StringAssert.Contains("Turn 5/8", T(hub.Root, $"{GMOotpFrontOfficeUIController.TurnLockName}/LockTitle"));
            hub.OpenNegotiationRoom();
            Assert.IsTrue(hub.IsTurnLockShown, "Turn 4 전 계약 협상실 = 잠금");
            foreach (var p in team.Roster.Take(3)) p.ContractYears = 1;
            hub.Refresh();
            if (hub.NegotiationSelected != null)
            {
                var blocked = hub.ChooseNegotiationCard(0);
                Assert.IsFalse(blocked.Success);
                StringAssert.Contains("잠김", blocked.Message);
            }

            // [진행하기] - 다른 화면 → 메인 홈 → Turn 1 방 → 메인 홈 → Turn 2(2026 = 2차 드래프트 없음 → Turn 3 건너뜀) …
            Assert.IsTrue(hub.Continue());
            Assert.IsTrue(hub.IsAtMainHome, "[진행하기] = 메인 홈");
            Assert.IsFalse(hub.IsTurnLockShown);
            Assert.IsFalse(GMStoveTurns.Advance(league, out var why), "Turn 1 방 확인 전 진행 불가");
            StringAssert.Contains("시즌 결산", why);
            Assert.IsTrue(hub.Continue());
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneSeasonSummary, hub.CurrentPane, "Turn 1 대표 방 = 시즌 결산실");
            var visited = new List<string> { hub.CurrentPane };
            for (int guard = 0; guard < 30 && GMStoveTurns.IsGating(league); guard++)
            {
                if (!hub.IsAtMainHome)
                {
                    Assert.IsTrue(hub.Continue(), "방 → 메인 홈");
                    Assert.IsTrue(hub.IsAtMainHome);
                }
                int before = hub.StoveTurn;
                Assert.IsTrue(hub.Continue(), $"Turn {before} 진행");
                if (hub.IsAssistantOpen) Assert.IsTrue(hub.AssistantChoose(2), $"Turn {before} 조력자 경고 무시 → 진행"); // [TASK-GM-16] 조력자 개입
                if (GMStoveTurns.IsGating(league))
                {
                    Assert.Greater(hub.StoveTurn, before, "다음 Turn");
                    Assert.IsFalse(hub.IsTurnLockShown, $"Turn {hub.StoveTurn} 대표 방은 열림");
                    visited.Add(hub.CurrentPane);
                    if (hub.StoveTurn == 4)
                    {
                        // 계약 협상실 해금 - 타결 · 결렬 = 효과음만, BGM 유지
                        audio.EnterScreen(GMAudioScreen.Market, "SAM");
                        string marketBgm = audio.CurrentBgmKey;
                        if (hub.NegotiationSelected != null)
                        {
                            var r = hub.ChooseNegotiationCard(0);
                            Assert.IsNotNull(r);
                            StringAssert.DoesNotContain("잠김", r.Message);
                            Assert.IsNull(audio.ActiveEvent, "연봉 협상 = BGM 하이재킹 없음");
                            Assert.AreEqual(marketBgm, audio.CurrentBgmKey, "스토브리그 기본 BGM 유지");
                            if (r.Success) Assert.AreEqual(TeamAudioProfile.SynthDeal, audio.LastSfxKey, "타결 효과음");
                            if (r.Broken) Assert.AreEqual(TeamAudioProfile.SynthFail, audio.LastSfxKey, "결렬 효과음");
                            TestContext.WriteLine($"[GM14 협상] {(r.Success ? "타결" : r.Broken ? "결렬" : "보류")} · 효과음 {audio.LastSfxKey} · {r.Message}");
                        }
                    }
                }
            }
            Assert.IsFalse(GMStoveTurns.IsGating(league), "8 Turn 완료");
            Assert.AreEqual(GMStoveTurns.Completed, GMFrontOffice.Ensure(league).StoveTurn);
            CollectionAssert.DoesNotContain(visited, GMOotpFrontOfficeUIController.PaneProtect, "짝수 해 = 2차 드래프트(Turn 3) 생략");
            CollectionAssert.IsSubsetOf(new[] { GMOotpFrontOfficeUIController.PaneSeasonSummary, GMOotpFrontOfficeUIController.PaneLockerRoom, GMOotpFrontOfficeUIController.PaneNegotiation, GMOotpFrontOfficeUIController.PaneFA, GMOotpFrontOfficeUIController.PaneDraft }, visited, "Turn 대표 방 순서 진행");
            TestContext.WriteLine("[GM14 8 Turn] " + string.Join(" → ", visited));
            hub.SelectMainTab(4);
            hub.SelectSubTab(0);
            Assert.IsFalse(hub.IsTurnLockShown, "완료 후 모든 방 열림");

            // 완료 후 메인 홈 [진행하기] = [TASK-GM-16] 실시간 시즌 대시보드, 다시 [진행하기] = 대시보드 닫고 메인 홈(경기 미진행)
            var dash = NewDash(hub);
            hub.GoMainHome();
            Assert.IsTrue(hub.Continue());
            Assert.IsTrue(dash.gameObject.activeSelf, "개막 = 실시간 시즌 대시보드");
            Assert.IsFalse(prePost.gameObject.activeSelf, "전력 분석으로 튕기지 않음");
            hub.SelectMainTab(3);
            Assert.IsTrue(hub.Continue());
            Assert.IsFalse(dash.gameObject.activeSelf, "[진행하기] = 대시보드 닫기");
            Assert.IsTrue(hub.IsAtMainHome, "메인 홈 리렌더링");
            Assert.AreEqual(0, sim.GamesPlayed, "경기로 튕기지 않음");

            // 통제 시작 전(구버전 · 직접 만든 리그)은 잠금 없음
            var free = new GMLiveSeasonSimulator(NewLeague(team: "LG", seed: 1416));
            var hub2 = NewHub(free, out _);
            hub2.SelectMainTab(4);
            hub2.SelectSubTab(0);
            Assert.IsFalse(hub2.IsTurnLockShown, "통제 미시작 리그 = 자유 진행");
        }

        // ================================================================== 공통

        private GMLiveLeagueDashboardUIController NewDash(GMOotpFrontOfficeUIController hub)
        {
            var go = new GameObject("GM14_Dash", typeof(RectTransform));
            go.transform.SetParent(hub.transform.parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var dash = go.AddComponent<GMLiveLeagueDashboardUIController>();
            dash.Build();
            dash.gameObject.SetActive(false);
            hub.DashView = dash;
            return dash;
        }

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim, out GMMatchPrePostUIController prePost)
        {
            var canvasGo = new GameObject("GM14_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvasGo);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var hub = NewView<GMOotpFrontOfficeUIController>(canvasGo, "GM14_Hub");
            hub.Build();
            prePost = NewView<GMMatchPrePostUIController>(canvasGo, "GM14_PrePost");
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

        private static string T(Transform root, string path)
        {
            var t = root.Find(path);
            Assert.IsNotNull(t, path);
            var text = t.GetComponent<Text>() ?? t.GetComponentInChildren<Text>(true);
            Assert.IsNotNull(text, path);
            return text.text;
        }
    }
}
