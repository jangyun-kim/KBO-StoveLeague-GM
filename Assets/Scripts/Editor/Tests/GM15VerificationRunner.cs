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
    /// [TASK-GM-15] 자동 검증(Unity CLI BatchPipelineGM15 1회 실행):
    ///   1) 스토브리그 Turn 1~8 중 대시보드 [한 경기] · [전반기/후반기] · [한 시즌] 비활성 + 잠금 막 · 실행 차단(허브 전력 분석 · 플레이 볼 포함)
    ///   2) 육성 회의실 - 훈련 방향 · 1군 콜업/스왑 · 멘토링 1:1 · 3슬롯 상한 · 연간 성장(멘토 +1 · 안정성)
    ///   3) 2차 드래프트 - 홀수 해 25인 보호 명단 밖 = 지명 대상 · 양도금 예산 차감/가산 · AI 지명 마감 · 짝수 해 Turn 3 생략
    ///   4) 언론 브리핑 - 팬 지지율 · 충성도 · 구단주 기대 순위 · 기록된 발언 시즌 종료 신임도 판정
    ///   5) 연도 전환 - 약속 위반으로 깎인 신뢰도 · 충성도 기간제 회복
    ///   6) 1920×1080 - 세 화면 텍스트 겹침 0 · 15pt 이상(지시서 14pt) · Bold 0
    /// </summary>
    public class GM15VerificationRunner
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
            dbObject = new GameObject("GM15_PlayerDatabase");
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

        private static GMLeagueState NewLeague(string team = "SAM", int seed = 1515)
        {
            var league = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        private static void SetTurn(GMLeagueState league, int turn, bool visited = false)
        {
            GMStoveTurns.Begin(league);
            var fo = GMFrontOffice.Ensure(league);
            fo.StoveTurn = turn;
            fo.StoveTurnVisited = visited;
        }

        // ================================================================== 1) 대시보드 우회 차단

        [Test]
        public void T1_Dashboard_RunButtons_LockedUntilStoveLeagueEnds()
        {
            var league = NewLeague();
            var sim = new GMLiveSeasonSimulator(league);
            GMStoveTurns.Begin(league);
            var canvas = NewCanvas();
            var dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM15_Dashboard");
            dash.Build();
            dash.Bind(sim);

            for (int turn = 1; turn <= GMStoveTurns.TurnCount; turn++)
            {
                GMFrontOffice.Ensure(league).StoveTurn = turn;
                dash.Refresh();
                Assert.IsTrue(dash.IsStoveLocked, $"Turn {turn} 잠금");
                foreach (var name in new[] { "ModeSingle", "ModeHalf", "ModeFull" })
                    Assert.IsFalse(dash.Root.Find(name).GetComponent<Button>().interactable, $"Turn {turn} {name} 비활성");
                Assert.IsFalse(dash.AnyRunButtonInteractable);
                var lockBar = dash.Root.Find(GMLiveLeagueDashboardUIController.StoveLockName);
                Assert.IsTrue(lockBar.gameObject.activeSelf, "잠금 막 표시");
                StringAssert.Contains($"Turn {turn}/8", T(lockBar, "LockText"));
                Assert.GreaterOrEqual(TextTidy.EffectiveSize(lockBar.Find("LockText").GetComponent<Text>()), 15);
            }

            // 버튼 우회 호출(onClick 직접 · 메서드 직접)도 개막하지 않는다
            dash.Root.Find("ModeFull").GetComponent<Button>().onClick.Invoke();
            dash.Run(GMRunMode.FullSeason);
            dash.Run(GMRunMode.FirstHalf);
            dash.OpenPreGameOrRun();
            Assert.IsFalse(sim.IsRunning, "한 시즌 · 전반기 진행 차단");
            Assert.AreEqual(0, sim.GamesPlayed, "경기 0");
            StringAssert.Contains("스토브리그", T(dash.Root, "StatusText"));

            var hub = NewHub(sim, canvas, out var prePost);
            hub.OpenPreGame();
            Assert.IsFalse(prePost.gameObject.activeSelf, "허브 [한 경기 3단계 진행] 차단");
            StringAssert.Contains("잠김", hub.StatusText);
            prePost.Attach(sim);
            Assert.IsFalse(prePost.PlayBall(), "플레이 볼 직접 호출 차단");
            Assert.AreEqual(0, sim.GamesPlayed);

            // 8 Turn 완료 = 잠금 해제
            GMFrontOffice.Ensure(league).StoveTurn = GMStoveTurns.Completed;
            dash.Refresh();
            Assert.IsFalse(dash.IsStoveLocked);
            Assert.IsTrue(dash.AnyRunButtonInteractable, "개막 준비 완료 = 진행 버튼 활성");
            Assert.IsFalse(dash.Root.Find(GMLiveLeagueDashboardUIController.StoveLockName).gameObject.activeSelf);
            dash.Run(GMRunMode.SingleGame);
            Assert.IsTrue(sim.IsRunning || sim.GamesPlayed > 0, "완료 후 한 경기 진행 가능");
            sim.Stop();

            // 통제 미시작 리그(구버전 세이브)는 잠그지 않는다
            var free = new GMLiveSeasonSimulator(NewLeague("LG", 1516));
            dash.Bind(free);
            Assert.IsFalse(dash.IsStoveLocked);
            Assert.IsTrue(dash.AnyRunButtonInteractable);
            free.Stop();
        }

        // ================================================================== 2) 육성 회의실

        [Test]
        public void T2_FuturesMeeting_Focus_CallUp_Swap_Mentoring_Growth()
        {
            var league = NewLeague(seed: 1520);
            var team = league.UserTeam;
            var sim = new GMLiveSeasonSimulator(league);
            SetTurn(league, 5);
            var hub = NewHub(sim, NewCanvas(), out _);
            hub.OpenFuturesMeeting();
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneFuturesMeeting, hub.CurrentPane);
            Assert.IsTrue(hub.IsTurnLockShown, "Turn 6 전 = 잠금");
            Assert.IsFalse(hub.SetTrainingFocus(1), "잠금 중 실행 차단");
            GMFrontOffice.Ensure(league).StoveTurn = 6;
            hub.OpenFuturesMeeting();
            Assert.IsFalse(hub.IsTurnLockShown, "Turn 6 = 육성 회의실 해금");
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneFuturesMeeting, GMOotpFrontOfficeUIController.TurnPrimaryPane(6));
            Assert.AreEqual(GMAudioScreen.Squad, hub.CurrentAudioScreen);
            hub.SelectMainTab(2);
            Assert.AreEqual("육성 회의실", T(hub.Root, "SubTab4"), "[선수단] 5번째 서브 탭");
            hub.OpenFuturesMeeting();

            Assert.GreaterOrEqual(team.Futures.Count, GMRosterTiers.FuturesMin);
            var pane = hub.Pane(GMOotpFrontOfficeUIController.PaneFuturesMeeting);
            var first = hub.FuturesSelected;
            Assert.IsNotNull(first, "첫 유망주 자동 선택");
            StringAssert.Contains("~", T(pane, "FmListPanel/FmRow0/C4"), "잠재력 범위");
            Assert.LessOrEqual(GMFuturesMeeting.PotentialMin(first), GMFuturesMeeting.PotentialMax(first));
            Assert.GreaterOrEqual(GMFuturesMeeting.PotentialMin(first), first.BaseOverall);
            CheckPane(hub, "육성 회의실");

            // 훈련 방향
            Assert.IsTrue(hub.SetTrainingFocus(1));
            Assert.AreEqual(GMFuturesMeeting.FocusesFor(first)[1], GMFuturesMeeting.FocusOf(league, first), "훈련 방향 저장");
            StringAssert.Contains(GMFuturesMeeting.FocusLabel(GMFuturesMeeting.FocusesFor(first)[1]), T(pane, "FmTrainPanel/FmDetail2"));

            // 멘토 후보(더그아웃 리더) 4명 확보
            var veterans = team.Roster.Where(p => !p.IsCaptain).OrderByDescending(p => p.Age).ToList();
            int need = 4 - GMFuturesMeeting.MentorCandidates(team).Count;
            foreach (var v in veterans.Where(p => p.RoleArchetype != LockerRoomRole.DugoutLeader).Take(Math.Max(0, need))) v.RoleArchetype = LockerRoomRole.DugoutLeader;
            var mentors = GMFuturesMeeting.MentorCandidates(team);
            Assert.GreaterOrEqual(mentors.Count, 4);
            var nonLeader = team.Roster.First(p => p.RoleArchetype != LockerRoomRole.DugoutLeader);
            Assert.IsFalse(hub.AssignMentorTo(nonLeader), "더그아웃 리더만 멘토");

            var prospects = team.Futures.Where(p => p.Age <= GMFuturesMeeting.MaxGrowthAge).OrderBy(p => p.InstanceId, StringComparer.Ordinal).Take(6).ToList();
            Assert.GreaterOrEqual(prospects.Count, 6);
            for (int i = 0; i < 3; i++)
            {
                hub.SelectFutures(prospects[i]);
                Assert.IsTrue(hub.AssignMentorTo(mentors[i]), $"멘토링 {i + 1}");
                Assert.AreEqual(mentors[i].InstanceId, GMFuturesMeeting.PlanOf(league, prospects[i]).MentorId, "멘토 데이터 반영");
            }
            Assert.AreEqual(3, GMFuturesMeeting.Mentorships(league).Count);
            hub.SelectFutures(prospects[3]);
            Assert.IsFalse(hub.AssignMentorTo(mentors[0]), "1:1 - 이미 담당 중");
            Assert.IsFalse(hub.AssignMentorTo(mentors[3]), "3슬롯 상한");
            StringAssert.Contains("슬롯", hub.StatusText);
            Assert.IsTrue(Enumerable.Range(0, 3).Any(i => T(pane, $"FmMentorPanel/FmSlot{i}").Contains(mentors[0].Template.PlayerName)), "멘토링 슬롯 표시");
            CheckPane(hub, "육성 회의실(멘토링 3쌍)");
            Assert.IsTrue(hub.ClearMentorSlot(2));
            Assert.AreEqual(2, GMFuturesMeeting.Mentorships(league).Count, "해제");
            Assert.IsTrue(hub.AssignMentorTo(mentors[3]), "해제 후 재배정");

            // 멘토 효과 - 성장 +1 · 변동 하한 0(안정성), 상한 = 잠재력
            foreach (var p in prospects)
                for (int y = 2026; y < 2036; y++)
                {
                    int plain = GMFuturesMeeting.GrowthFor(p, GMTrainingFocus.Balanced, false, y);
                    int mentored = GMFuturesMeeting.GrowthFor(p, GMTrainingFocus.Balanced, true, y);
                    Assert.GreaterOrEqual(mentored, plain, "멘토링 성장 ≥ 일반");
                    Assert.GreaterOrEqual(mentored, Math.Min(GMFuturesMeeting.PotentialMax(p) - p.BaseOverall, GMFuturesMeeting.BaseGrowth(p)), "멘토링 = 나쁜 해 없음");
                    Assert.LessOrEqual(mentored, Math.Max(0, GMFuturesMeeting.PotentialMax(p) - p.BaseOverall), "잠재력 상한");
                }

            // 1군 콜업(자리 있음) → 스왑(가득)
            while (team.Roster.Count > GMRosterTiers.FirstTeamMax - 1) Assert.IsTrue(GMRosterTiers.SendDown(team, team.Roster.Last(p => !p.IsCaptain), out _));
            int futures = team.Futures.Count;
            var up = GMFuturesMeeting.Mentorships(league).First().prospect;
            hub.SelectFutures(up);
            Assert.IsTrue(hub.CallUpSelected(), "1군 콜업");
            Assert.IsTrue(team.Roster.Contains(up) && !team.Futures.Contains(up), "콜업 데이터 반영");
            Assert.AreEqual(GMRosterTiers.FirstTeamMax, team.Roster.Count);
            Assert.AreEqual(futures - 1, team.Futures.Count);
            Assert.IsNotNull(GMFuturesMeeting.MentorOf(league, up), "콜업 후에도 멘토링 유지");

            var up2 = prospects[4];
            hub.SelectFutures(up2);
            Assert.IsFalse(hub.CallUpSelected(), "1군 가득 = 스왑 대상 필요");
            var down = team.Roster.Where(p => !p.IsCaptain && p.IsPitcher == up2.IsPitcher && !GMFuturesMeeting.MentorCandidates(team).Contains(p)).OrderBy(GMStoveLeagueMarket.TradeValue).First();
            hub.SelectSwapTarget(down);
            Assert.AreEqual(down, hub.SwapTarget);
            CheckPane(hub, "육성 회의실(스왑 선택)");
            Assert.IsTrue(hub.CallUpSelected(), "스왑 콜업");
            Assert.IsTrue(team.Roster.Contains(up2) && team.Futures.Contains(down), "스왑 데이터 반영");
            Assert.AreEqual(GMRosterTiers.FirstTeamMax, team.Roster.Count, "1군 정원 유지");
            Assert.AreEqual(futures - 1, team.Futures.Count, "퓨처스 인원 유지");

            // 연간 성장(연도 전환 훅) - 멘토링 유망주는 GrowthFor만큼 OVR 상승, 유망주 보정치로 저장
            var grow = GMFuturesMeeting.Mentorships(league).Select(m => m.prospect).First(p => team.Futures.Contains(p) || team.Roster.Contains(p));
            int ovr = grow.BaseOverall, shift = grow.ProspectStatShift;
            var focus = GMFuturesMeeting.FocusOf(league, grow);
            int expected = GMFuturesMeeting.GrowthFor(grow, focus, true, league.SeasonYear);
            var grown = GMFuturesMeeting.ApplySeasonGrowth(league, league.SeasonYear);
            // 전 세부 스탯 +expected → OVR(세부 스탯 평균 반올림)은 expected(투수 4항목 평균은 반올림 경계에서 ±1)
            Assert.AreEqual(shift + expected, grow.ProspectStatShift, "보정치 = 세이브 복원 값");
            Assert.LessOrEqual(Math.Abs(grow.BaseOverall - ovr - expected), grow.IsPitcher ? 1 : 0, "멘토링 유망주 성장 반영");
            Assert.AreEqual(grow.BaseOverall - ovr, GMFuturesMeeting.PlanOf(league, grow).LastGrowth, "실제 성장 기록");
            if (grow.ProspectStatShift != 0)
                Assert.AreSame(GMRosterTiers.ProspectTemplate(GMRosterTiers.OriginalOf(grow.Template), grow.ProspectStatShift), grow.Template, "원본 + 보정치로 같은 템플릿 복원");
            Assert.IsTrue(grown.All(g => g.growth > 0));
            TestContext.WriteLine($"[GM15 육성] 성장 {grown.Count}명 · {grow.Template.PlayerName} {ovr} → {grow.BaseOverall} · 멘토 {GMFuturesMeeting.Mentorships(league).Count}쌍");
        }

        // ================================================================== 3) 2차 드래프트

        [Test]
        public void T3_SecondaryDraft_Protect25_Exposed_TransferBudget_AiConclude()
        {
            var league = NewLeague(seed: 1530);
            league.SeasonYear = 2027;
            var user = league.UserTeam;
            SetTurn(league, 3);
            Assert.IsTrue(GMSecondaryDraft.IsDraftYear(2027));
            Assert.IsTrue(GMSecondaryDraft.IsOpen(league), "홀수 해 Turn 3 = 열림");

            // 자동 보호 · 25인 보호 명단
            foreach (var p in user.ReservePlayers)
            {
                string auto = GMSecondaryDraft.AutoProtectReason(league, p);
                Assert.AreEqual(auto == null, GMSecondaryDraft.Candidates(league, user).Contains(p), "자동 보호 = 보호 명단 후보 제외");
                if (p.Age <= GMSecondaryDraft.RookieMaxAge && user.Futures.Contains(p)) Assert.IsNotNull(auto, "퓨처스 1~3년 차 자동 보호");
                if (GMFaCompensation.IsForeign(p)) Assert.IsNotNull(auto, "외국인 자동 보호");
            }
            var candidates = GMSecondaryDraft.Candidates(league, user);
            var ids = GMSecondaryDraft.UserProtectedIds(league);
            int limit = GMSecondaryDraft.ProtectLimit(league, user);
            Assert.AreEqual(Math.Max(0, Math.Min(GMSecondaryDraft.ProtectSize, candidates.Count - GMSecondaryDraft.MinExposed)), limit, "보호 상한 = min(25, 후보 - 5)");
            Assert.AreEqual(limit, ids.Count, "추천 명단 기본 지정");
            Assert.AreEqual(GMSecondaryDraft.ProtectSize, limit, "현역 로스터(보류 40명) = 25인 보호");
            TestContext.WriteLine($"[GM15 보호] 보류 {user.ReservePlayers.Count()} · 후보 {candidates.Count} · 보호 상한 {limit}");
            var exposed = GMSecondaryDraft.Exposed(league, user);
            Assert.Greater(exposed.Count, 0, "25인 밖 = 지명 대상");
            Assert.IsTrue(exposed.All(p => !ids.Contains(p.InstanceId) && GMSecondaryDraft.AutoProtectReason(league, p) == null));
            // 25인 가득일 때 추가 보호 불가 → 해제 후 가능
            Assert.GreaterOrEqual(exposed.Count, Math.Min(GMSecondaryDraft.MinExposed, candidates.Count), "비보호 최소 5명");
            if (ids.Count == limit && limit > 0)
            {
                var outside = GMSecondaryDraft.Exposed(league, user)[0];
                Assert.IsFalse(GMSecondaryDraft.ToggleProtect(league, outside, out var full), "25인 상한");
                StringAssert.Contains("가득", full);
                var someone = user.ReservePlayers.First(p => ids.Contains(p.InstanceId));
                Assert.IsTrue(GMSecondaryDraft.ToggleProtect(league, someone, out _), "해제");
                Assert.IsTrue(GMSecondaryDraft.IsExposed(league, user, someone), "해제 = 지명 대상");
            }
            var autoOne = user.ReservePlayers.FirstOrDefault(p => GMSecondaryDraft.AutoProtectReason(league, p) != null);
            if (autoOne != null) Assert.IsFalse(GMSecondaryDraft.ToggleProtect(league, autoOne, out _), "자동 보호 선수는 체크 대상 아님");

            // AI 구단 = 10대 가중치 상위 25인 보호
            var kia = league.Teams["KIA"];
            var kiaProtected = GMSecondaryDraft.ProtectedIds(league, kia);
            Assert.AreEqual(GMSecondaryDraft.ProtectLimit(league, kia), kiaProtected.Count);
            Assert.IsTrue(GMSecondaryDraft.Exposed(league, kia).All(p => !kiaProtected.Contains(p.InstanceId)));

            // UI - 2차 드래프트실(Turn 3 대표 방)
            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, NewCanvas(), out _);
            hub.OpenSecondDraft();
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneSecondDraft, hub.CurrentPane);
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneSecondDraft, GMOotpFrontOfficeUIController.TurnPrimaryPane(3));
            Assert.IsFalse(hub.IsTurnLockShown);
            hub.SelectMainTab(5);
            Assert.AreEqual("2차 드래프트", T(hub.Root, "SubTab2"));
            hub.OpenSecondDraft();
            var pane = hub.Pane(GMOotpFrontOfficeUIController.PaneSecondDraft);
            StringAssert.Contains($"/{GMSecondaryDraft.ProtectLimit(league, user)}", T(pane, "SdProtectPanel/SdSummary"));
            CheckPane(hub, "2차 드래프트실");

            // 유저 지명 - 예산 차감 · 원 소속 가산 · 이적
            var pool = GMSecondaryDraft.Pool(league, user.TeamCode);
            Assert.Greater(pool.Count, 0, "타 구단 비보호 선수 풀");
            Assert.IsTrue(pool.All(x => x.from != user && GMSecondaryDraft.IsExposed(league, x.from, x.player)), "풀 = 타 구단 비보호");
            var target = pool[0];
            long userBudget = user.Budget, fromBudget = target.from.Budget;
            int userCount = user.ReservePlayers.Count(), fromCount = target.from.ReservePlayers.Count();
            hub.SelectSecondDraftTarget(target.player);
            StringAssert.Contains("1R 지명", T(pane, "SdPickPanel/SdPickButton"));
            CheckPane(hub, "2차 드래프트실(지명 선택)");
            var pick = hub.PickSecondDraft();
            Assert.IsNotNull(pick, hub.StatusText);
            Assert.AreEqual(1, pick.Round);
            Assert.AreEqual(GMSecondaryDraft.FeeFor(1), pick.Fee);
            Assert.AreEqual(userBudget - GMSecondaryDraft.FeeFor(1), user.Budget, "양도금 지급(예산 차감)");
            Assert.AreEqual(fromBudget + GMSecondaryDraft.FeeFor(1), target.from.Budget, "원 소속 양도금 수령");
            Assert.IsTrue(user.ReservePlayers.Contains(target.player) && !target.from.ReservePlayers.Contains(target.player), "이적 반영");
            Assert.AreEqual(userCount + 1, user.ReservePlayers.Count());
            Assert.AreEqual(fromCount - 1, target.from.ReservePlayers.Count());
            Assert.AreEqual(2, GMSecondaryDraft.NextRound(league, user.TeamCode));

            // 보호 선수 · 예산 부족 지명 불가
            var otherTeam = league.Teams.Values.First(t => t != user && GMSecondaryDraft.ProtectedIds(league, t).Count > 0);
            var otherProtected = GMSecondaryDraft.ProtectedIds(league, otherTeam);
            var protectedOther = otherTeam.ReservePlayers.First(p => otherProtected.Contains(p.InstanceId));
            Assert.IsNull(GMSecondaryDraft.UserPick(league, protectedOther, out var protMsg), "보호 선수 지명 불가");
            StringAssert.Contains("보호", protMsg);
            long saved = user.Budget;
            user.Budget = 100;
            Assert.IsNull(GMSecondaryDraft.UserPick(league, GMSecondaryDraft.Pool(league, user.TeamCode)[0].player, out var budgetMsg));
            StringAssert.Contains("예산 부족", budgetMsg);
            user.Budget = saved;

            // AI 지명 마감 - 예산 총합 보존 · 유출 한도 · 지명 수 한도 · 비보호 선수만
            long total = league.Teams.Values.Sum(t => t.Budget);
            var exposedBefore = league.Teams.Values.ToDictionary(t => t.TeamCode, t => new HashSet<string>(GMSecondaryDraft.Exposed(league, t).Select(p => p.InstanceId)));
            var aiPicks = hub.ConcludeSecondDraft();
            Assert.IsTrue(GMSecondaryDraft.IsHeld(league), "시행 처리");
            Assert.IsFalse(GMSecondaryDraft.IsOpen(league));
            Assert.AreEqual(total, league.Teams.Values.Sum(t => t.Budget), "양도금 = 구단 간 이전(총합 보존)");
            foreach (var p in aiPicks)
            {
                Assert.AreNotEqual(user.TeamCode, p.PickerCode);
                Assert.IsTrue(exposedBefore[p.FromCode].Contains(p.PlayerId), $"{p.PlayerName} = 비보호 선수만 지명");
                Assert.IsTrue(league.Teams[p.PickerCode].ReservePlayers.Any(x => x.InstanceId == p.PlayerId), "AI 이적 반영");
                Assert.AreEqual(GMSecondaryDraft.FeeFor(p.Round), p.Fee);
            }
            foreach (var code in league.Teams.Keys)
            {
                Assert.LessOrEqual(GMSecondaryDraft.PicksBy(league, code), GMSecondaryDraft.Rounds, $"{code} 지명 3명 이하");
                Assert.LessOrEqual(GMSecondaryDraft.LossesOf(league, code), GMSecondaryDraft.MaxLossPerTeam, $"{code} 유출 4명 이하");
            }
            Assert.IsNull(GMSecondaryDraft.UserPick(league, GMSecondaryDraft.Pool(league, user.TeamCode).Select(x => x.player).FirstOrDefault() ?? target.player, out _), "마감 후 지명 불가");
            Assert.IsTrue(league.News.Any(n => n.Title.Contains("2차 드래프트 종료")));
            CheckPane(hub, "2차 드래프트실(마감)");
            TestContext.WriteLine($"[GM15 2차 드래프트] 내 지명 {pick.PlayerName} · AI 지명 {aiPicks.Count}건: " + string.Join(" / ", aiPicks.Take(6).Select(p => $"{p.Round}R {p.PlayerName}({p.FromCode}→{p.PickerCode})")));

            // Turn 3 진행 = 자동 마감 · 짝수 해 = Turn 3 생략
            var odd = NewLeague("KIA", 1531);
            odd.SeasonYear = 2027;
            SetTurn(odd, 3, visited: true);
            Assert.IsTrue(GMStoveTurns.Advance(odd, out var msg3), msg3);
            Assert.IsTrue(GMSecondaryDraft.IsHeld(odd), "Turn 3 진행 = 2차 드래프트 마감");
            StringAssert.Contains("2차 드래프트 마감", msg3);
            Assert.AreEqual(4, GMStoveTurns.Current(odd));
            var even = NewLeague("LG", 1532);
            SetTurn(even, 2, visited: true);
            Assert.IsTrue(GMStoveTurns.Advance(even, out var msg2), msg2);
            Assert.AreEqual(4, GMStoveTurns.Current(even), "짝수 해 = Turn 3 생략");
            Assert.IsFalse(GMSecondaryDraft.IsOpen(even));
            StringAssert.Contains("없는 해", GMSecondaryDraft.StatusText(even));
            Assert.IsNull(GMSecondaryDraft.UserPick(even, even.Teams["SAM"].Roster[0], out _));
        }

        // ================================================================== 4) 언론 브리핑실

        [Test]
        public void T4_PressBriefing_FanSupport_Loyalty_OwnerTrustOnRecord()
        {
            var league = NewLeague(seed: 1540);
            var team = league.UserTeam;
            var fo = GMFrontOffice.Ensure(league);
            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, NewCanvas(), out _);
            hub.SelectMainTab(0);
            Assert.AreEqual("언론 브리핑실", T(hub.Root, "SubTab8"), "[프런트 오피스] 9번째 서브 탭");
            Assert.AreEqual("계약 협상실", T(hub.Root, "SubTab7"), "기존 탭 순서 유지");
            CheckLayer(hub.Root, "허브 헤더 · 9칸 서브 탭");
            hub.OpenPressBriefing();
            Assert.AreEqual(GMOotpFrontOfficeUIController.PanePress, hub.CurrentPane);
            var pane = hub.Pane(GMOotpFrontOfficeUIController.PanePress);
            StringAssert.Contains("스토브리그 출사표", T(pane, "PrBriefPanel/PrTopic"));
            StringAssert.Contains("리빌딩", T(pane, "PrBriefPanel/PrOption0"));
            StringAssert.Contains("FA에 투자", T(pane, "PrBriefPanel/PrOption1"));
            StringAssert.Contains("노코멘트", T(pane, "PrBriefPanel/PrOption2"));
            StringAssert.Contains("구단주 신임도 기대치", T(pane, "PrBriefPanel/PrHint1"));
            CheckPane(hub, "언론 브리핑실");

            // 우승 선언 - 팬 +6 · 베테랑 +3 · 유망주 -2 · 기대 순위 3위 · 기록된 발언
            var vet = team.ReservePlayers.First(p => p.Age >= GMPressBriefing.VeteranAge);
            var kid = team.ReservePlayers.First(p => p.Age <= GMPressBriefing.YoungAge);
            vet.Loyalty = 50; kid.Loyalty = 50;
            team.FanSupport = 50;
            fo.OwnerTrust = 60;
            var opt = GMPressBriefing.Options(GMPressTopic.StoveVision)[1];
            int expectedDelta = GMPressBriefing.ExpectedTrustDelta(league, opt);
            Assert.AreEqual(GMPressBriefing.ProjectedRank(league) <= 3 ? 6 : -15, expectedDelta, "신임도 기대치 = 현재 전망 기준");
            var r = hub.ChoosePress(1);
            Assert.IsTrue(r.Applied, r.Message);
            Assert.AreEqual(56, team.FanSupport, "팬 지지율 +6");
            Assert.AreEqual(53, vet.Loyalty, "베테랑 충성도 +3");
            Assert.AreEqual(48, kid.Loyalty, "유망주 충성도 -2");
            Assert.LessOrEqual(fo.Owner.ExpectedRank, 3, "구단주 기대 순위 갱신");
            Assert.AreEqual(1, fo.PressStatements.Count);
            Assert.AreEqual(3, r.Statement.TargetRank);
            Assert.AreEqual(-15, r.Statement.TrustOnFail);
            StringAssert.Contains("판정 대기", T(pane, "PrRecordPanel/PrNote0"));
            Assert.IsFalse(hub.ChoosePress(0).Applied, "같은 주제 중복 브리핑 불가");
            Assert.AreEqual(56, team.FanSupport);
            CheckPane(hub, "언론 브리핑실(발표 후)");

            // 정규시즌 종료 판정 - 최하위 = 공언 실패 = 신임도 -15
            var ranks = league.Teams.Keys.Where(c => c != team.TeamCode).Concat(new[] { team.TeamCode }).ToList();
            var verdicts = GMPressBriefing.EvaluateSeason(league, ranks);
            Assert.AreEqual(1, verdicts.Count);
            Assert.IsFalse(verdicts[0].Kept);
            Assert.AreEqual(45, fo.OwnerTrust, "무리한 우승 선언 실패 = 신임도 -15");
            Assert.AreEqual(-15, verdicts[0].TrustApplied);
            Assert.IsEmpty(GMPressBriefing.EvaluateSeason(league, ranks), "판정은 1회");
            hub.Refresh();
            StringAssert.Contains("실패", T(pane, "PrRecordPanel/PrNote0"));

            // 리빌딩 선언 - 팬 -4 · 유망주 +5 · 베테랑 -4 · 기대 순위 +2 · 순위 약속 없음(신임도 판정 없음)
            var league2 = NewLeague("HAN", 1541);
            var t2 = league2.UserTeam;
            var fo2 = GMFrontOffice.Ensure(league2);
            t2.FanSupport = 50;
            fo2.OwnerTrust = 60;
            int rank0 = fo2.Owner.ExpectedRank;
            var young = t2.ReservePlayers.First(p => p.Age <= GMPressBriefing.YoungAge);
            young.Loyalty = 40;
            var r2 = GMPressBriefing.Brief(league2, GMPressChoice.Rebuild);
            Assert.IsTrue(r2.Applied);
            Assert.AreEqual(46, t2.FanSupport, "팬 지지율 -4");
            Assert.AreEqual(45, young.Loyalty, "유망주 충성도 +5");
            Assert.AreEqual(Math.Min(10, rank0 + 2), fo2.Owner.ExpectedRank, "구단주 기대치 낮춤");
            // 연도 전환 경로(GMFrontOffice.OnSeasonCompleted)에서도 판정된다
            GMFrontOffice.OnSeasonCompleted(league2, league2.Teams.Keys.ToList(), league2.Teams.Keys.First());
            Assert.IsTrue(fo2.PressStatements[0].Evaluated, "시즌 결산 훅 판정");
            Assert.AreEqual(0, fo2.PressStatements[0].TrustApplied, "리빌딩 = 순위 약속 없음");

            // 시즌 중간 브리핑(40경기 이후) - 가을야구 약속 5위
            league2.GamesPlayed = 50;
            Assert.AreEqual(GMPressTopic.MidSeason, GMPressBriefing.CurrentTopic(league2));
            var r3 = GMPressBriefing.Brief(league2, GMPressChoice.WinNow);
            Assert.IsTrue(r3.Applied);
            Assert.AreEqual(5, r3.Statement.TargetRank);
            league2.GamesPlayed = 20;
            Assert.IsNull(GMPressBriefing.CurrentTopic(league2), "1~39경기 = 브리핑 없음");
        }

        // ================================================================== 5) 약속 위반 페널티 기간제 회복

        [Test]
        public void T5_PromisePenalty_RecoversGradually_OnYearTransition()
        {
            var league = NewLeague(seed: 1550);
            var team = league.UserTeam;
            var p = team.Roster.First(x => !x.IsPitcher);
            team.LockerRoomTrust = 60;
            p.Loyalty = 70;
            var promise = GMPromiseSystem.Propose(league, team, p, GMPromiseKind.StarterGuarantee, "계약 협상실");
            GMPromiseSystem.Activate(league, promise);
            GMPromiseSystem.Break(league, team, promise, p, "테스트 위반");
            Assert.AreEqual(50, team.LockerRoomTrust, "위반 = 신뢰도 -10");
            Assert.AreEqual(45, p.Loyalty, "위반 = 충성도 -25");
            Assert.AreEqual(league.SeasonYear, promise.BrokenYear);

            Assert.IsEmpty(GMPromiseSystem.RecoverPenalties(league, league.SeasonYear), "같은 해 판정분은 다음 해부터");
            Assert.AreEqual(50, team.LockerRoomTrust);
            var trust = new List<int>();
            var loyalty = new List<int>();
            for (int y = 1; y <= 4; y++)
            {
                GMPromiseSystem.RecoverPenalties(league, league.SeasonYear + y);
                trust.Add(team.LockerRoomTrust);
                loyalty.Add(p.Loyalty);
            }
            CollectionAssert.AreEqual(new[] { 55, 60, 60, 60 }, trust, "신뢰도 +5씩 2년 - 원래 페널티(10)까지만");
            CollectionAssert.AreEqual(new[] { 55, 65, 70, 70 }, loyalty, "충성도 +10 · +10 · +5 - 원래 페널티(25)까지만");
            Assert.AreEqual(GMPromiseSystem.BrokenTrustPenalty, promise.TrustRecovered);
            Assert.AreEqual(GMPromiseSystem.BrokenLoyaltyPenalty, promise.LoyaltyRecovered);
            Assert.IsTrue(league.News.Any(n => n.Title.Contains("앙금")));

            // 실제 연도 전환(AdvanceToNextSeasonYear) - 과거 위반 회복 · 퓨처스 성장 적용
            var league2 = NewLeague("NC", 1551);
            var sim = new GMLiveSeasonSimulator(league2);
            var t2 = league2.UserTeam;
            var victim = t2.Roster.First(x => x.IsPitcher);
            var old = GMPromiseSystem.Propose(league2, t2, victim, GMPromiseKind.StarterGuarantee, "계약 협상실");
            GMPromiseSystem.Activate(league2, old);
            GMPromiseSystem.Break(league2, t2, old, victim, "지난 시즌 위반");
            old.BrokenYear = league2.SeasonYear - 1; // 지난 시즌에 깨진 약속
            var prospect = t2.Futures.Where(x => x.Age <= GMFuturesMeeting.MaxGrowthAge && GMFuturesMeeting.BaseGrowth(x) > 0).OrderBy(x => x.InstanceId, StringComparer.Ordinal).First();
            int prospectOvr = prospect.BaseOverall;
            int expectGrowth = GMFuturesMeeting.GrowthFor(prospect, GMTrainingFocus.Balanced, false, league2.SeasonYear);
            Assert.IsTrue(sim.StartRun(GMRunMode.FullSeason));
            sim.RunUntilStop();
            Assert.IsTrue(sim.IsSeasonComplete);
            int trustBefore = t2.LockerRoomTrust, year = league2.SeasonYear;
            Assert.IsTrue(GMAwardEvaluator.AdvanceToNextSeasonYear(sim));
            Assert.AreEqual(year + 1, league2.SeasonYear);
            Assert.AreEqual(Math.Min(100, trustBefore + GMPromiseSystem.TrustRecoveryPerYear), t2.LockerRoomTrust, "연도 전환 = 신뢰도 +5 회복");
            Assert.AreEqual(GMPromiseSystem.TrustRecoveryPerYear, old.TrustRecovered);
            if (t2.ReservePlayers.Contains(victim)) Assert.AreEqual(GMPromiseSystem.LoyaltyRecoveryPerYear, old.LoyaltyRecovered, "충성도 +10 회복");
            if (t2.Futures.Contains(prospect)) Assert.LessOrEqual(Math.Abs(prospect.BaseOverall - prospectOvr - expectGrowth), prospect.IsPitcher ? 1 : 0, "연도 전환 = 퓨처스 성장");
            Assert.AreEqual(1, GMStoveTurns.Current(league2), "새 스토브리그 Turn 1");
            TestContext.WriteLine($"[GM15 회복] 신뢰도 {trustBefore} → {t2.LockerRoomTrust} · {prospect.Template.PlayerName} OVR {prospectOvr} → {prospect.BaseOverall}");
        }

        // ================================================================== 공통

        private GameObject NewCanvas()
        {
            var canvasGo = new GameObject("GM15_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvasGo);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            return canvasGo;
        }

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim, GameObject canvas, out GMMatchPrePostUIController prePost)
        {
            var hub = NewView<GMOotpFrontOfficeUIController>(canvas, "GM15_Hub");
            hub.Build();
            prePost = NewView<GMMatchPrePostUIController>(canvas, "GM15_PrePost");
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
