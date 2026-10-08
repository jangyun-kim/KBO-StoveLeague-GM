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
    /// [TASK-GM-07] 6대 자동 검증(Unity CLI BatchPipelineGM07 1회 실행):
    ///   1) RealCurrent2026 로스터 = 2026 데이터만 · 단장 모드 전용 연도 시너지(팀워크 → 실효 전력, 세트덱 플래그는 계속 끔)
    ///   2) [진행하기] 라우팅 - 다른 화면 → 메인 홈(프런트 오피스 6분할), 메인 홈 → ① 전력 분석(경기 미진행)
    ///   3) OOTP 레이아웃(툴바 · 사이드바 · 감독 설정 · 시즌 일정 캘린더 · 포스트시즌 트리) 겹침 0 · 15pt 이상 · Bold 0 · 씬
    ///   4) 포스트시즌 1경기씩 순차 진행(시리즈 승수 누적 · 내 구단 경기 3단계 지휘 · 우승 확정)
    ///   5) 한 경기 3단계 PreGameView → LiveMatchInningView(타석 · 이닝 · 전술 개입) → PostGameBoxScoreView 전환 무결성
    ///   6) 실책 상향(0.062) - 144경기 팀당 평균 75~110개
    /// </summary>
    public class GM07VerificationRunner
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
            dbObject = new GameObject("GM07_PlayerDatabase");
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

        private static GMLeagueState NewLeague(GMStartMode mode = GMStartMode.RealCurrent2026, string team = "SAM", int seed = 20260328)
        {
            var league = GMRosterLoader.LoadModeRoster(mode, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        /// <summary>정규시즌 144경기 완주(공유) - 실책 · 포스트시즌 · 시즌 종료 화면에 쓴다.</summary>
        private static GMLiveSeasonSimulator SharedSeason()
        {
            if (season != null) return season;
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "KIA", seed: 7077));
            Assert.IsTrue(sim.StartRun(GMRunMode.FullSeason));
            sim.RunUntilStop();
            Assert.IsTrue(sim.IsSeasonComplete);
            season = sim;
            return season;
        }

        private static void RunDays(GMLiveSeasonSimulator sim, int days)
        {
            sim.StartRun(GMRunMode.FullSeason);
            for (int d = 0, guard = 0; d < days && guard < days * 20; guard++)
            {
                if (sim.PendingInterrupt != null) { sim.ResolveInterrupt(GMInterruptChoice.AutoCallUp); continue; }
                if (sim.StepGameDay()) d++;
            }
            while (sim.PendingInterrupt != null) sim.ResolveInterrupt(GMInterruptChoice.AutoCallUp);
            sim.Stop();
        }

        private GameObject NewCanvas()
        {
            var canvasGo = new GameObject("GM07_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
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

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim, out GameObject canvas, out GMMatchPrePostUIController prePost)
        {
            canvas = NewCanvas();
            var hub = NewView<GMOotpFrontOfficeUIController>(canvas, "GM07_Hub");
            hub.Build();
            prePost = NewView<GMMatchPrePostUIController>(canvas, "GM07_PrePost");
            prePost.Build();
            prePost.gameObject.SetActive(false);
            hub.PrePostView = prePost;
            hub.Bind(sim);
            return hub;
        }

        private static string T(Transform root, string path) => root.Find(path).GetComponent<Text>().text;
        private static void Click(Transform root, string path) => root.Find(path).GetComponent<Button>().onClick.Invoke();

        // ================================================================== 1) 2026 로스터 고정 · 연도 시너지

        [Test]
        public void T1_RealCurrent2026_Only2026Data_YearSynergyToEffectivePower()
        {
            foreach (var mode in new[] { GMStartMode.RealCurrent2026, GMStartMode.StoryCampaign })
            {
                var league = NewLeague(mode, "LOT", 71);
                foreach (var team in league.Teams.Values)
                {
                    Assert.AreEqual(28, team.Roster.Count, $"{mode} {team.TeamCode} 28인");
                    var wrong = team.Roster.Where(p => p.Template.SeasonYear != GMFeatureFlags.DEFAULT_START_YEAR).Select(p => $"{p.Template.PlayerName}({p.Template.SeasonYear})").ToList();
                    Assert.IsEmpty(wrong, $"{mode} {team.TeamCode} 2026 데이터만");
                    Assert.Greater(team.Roster.Count(p => NameAliasTable.ToCode(p.Template.Team) == team.TeamCode), 20, $"{team.TeamCode} 2026 소속 선수 위주");
                }
                Assert.IsTrue(league.FreeAgents.All(p => p.Template.SeasonYear == GMFeatureFlags.DEFAULT_START_YEAR), $"{mode} FA 시장도 2026 데이터");
                Assert.IsTrue(league.DraftPool.All(p => p.Template.SeasonYear == GMFeatureFlags.DEFAULT_START_YEAR), $"{mode} 드래프트 풀 2026 데이터");
                var ids = league.AllPlayers.Select(p => p.Template.RealPlayerId).ToList();
                Assert.AreEqual(ids.Count, ids.Distinct().Count(), "중복 선수 없음");
            }

            // 연도 시너지 규칙 - 세트덱(카드 수집형)은 계속 꺼 두고 단장 모드 전용 팀워크 가산으로만 반영
            Assert.IsFalse(GMFeatureFlags.IsSetDeckEnabled, "ENABLE_SET_DECK_200P 유지(false)");
            Assert.AreEqual(0, GMYearSynergy.GroupBonus(2));
            Assert.AreEqual(1, GMYearSynergy.GroupBonus(3));
            Assert.AreEqual(2, GMYearSynergy.GroupBonus(5));
            Assert.AreEqual(3, GMYearSynergy.GroupBonus(8));
            Assert.AreEqual(4, GMYearSynergy.GroupBonus(12));
            var current = NewLeague(team: "SAM", seed: 72);
            var me = current.UserTeam;
            Assert.AreEqual(0, me.YearSynergyBonus, "2026 현역 데이터 = 기준선(10구단 공통 가산으로 리그 공격력이 부풀지 않게)");
            Assert.AreEqual(me.CheerLeadershipBuff + me.AgendaTeamworkBonus + me.YearSynergyBonus, me.TeamworkBuff, "팀워크 가산 = 응원단 + 안건 + 연도 시너지");

            var dream = NewLeague(GMStartMode.AllTimeDream, "KIA", 73);
            var groups = dream.Teams.Values.SelectMany(t => GMYearSynergy.Groups(t.AvailableRoster)).ToList();
            Assert.IsNotEmpty(groups, "올타임 드림 - 같은 시즌 · 같은 구단 출신 그룹 발생");
            Assert.IsTrue(groups.All(g => g.Year != GMYearSynergy.BaselineYear && g.Count >= GMYearSynergy.MinGroup));
            var synergyTeam = dream.Teams.Values.OrderByDescending(t => t.YearSynergyBonus).First();
            StringAssert.Contains("연도 시너지 +", GMYearSynergy.Summary(synergyTeam.AvailableRoster));
            int strict = 0, raised = 0;
            foreach (var team in dream.Teams.Values)
            {
                int bonus = team.YearSynergyBonus;
                Assert.That(bonus, Is.InRange(0, GMYearSynergy.IsLegendRoster(team.AvailableRoster) ? 99 : GMYearSynergy.MaxTeamBonus)); // [TASK-GM-10] 레전드 로스터는 상한 해제
                var with = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff);
                var without = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff - bonus);
                Assert.GreaterOrEqual(with.TeamworkScore, without.TeamworkScore, team.TeamCode + " 팀워크 가산");
                Assert.GreaterOrEqual(with.EffectivePowerMultiplier, without.EffectivePowerMultiplier, team.TeamCode + " 실효 전력 반영");
                if (with.TeamworkScore > without.TeamworkScore) raised++;
                if (with.TeamworkScore > without.TeamworkScore && without.TeamworkScore >= 50 && without.EffectivePowerMultiplier < 1.15f)
                {
                    Assert.Greater(with.EffectivePowerMultiplier, without.EffectivePowerMultiplier, team.TeamCode + " 시너지가 실효 전력을 올린다");
                    strict++;
                }
            }
            // [TASK-GM-09] 실효 전력 하한 0.90(= 팀워크 50) - 슈퍼스타 과밀 드림 구단은 팀워크 20~40대라 하한에 걸려 실효 전력은 그대로일 수 있다.
            // 시너지는 팀워크 점수를 올리고(raised), 하한 위 구단이면 실효 전력도 올린다(strict - 위 루프에서 검증).
            Assert.Greater(raised + strict, 0, "연도 시너지로 팀워크(하한 위면 실효 전력)가 오른 드림 구단");
            // 선수 OVR(BaseOverall)은 시너지와 무관
            var sample = me.Roster[0];
            Assert.AreEqual(sample.Template.GetBaseOverall(), sample.GetEffectiveOverall(), "OVR = 순수 시즌 성적");
            // 경기 엔진 전달 - ModifiersFor의 케미스트리 전력 가산이 시너지 포함 보고서와 같다
            var sim = new GMLiveSeasonSimulator(dream);
            var mods = sim.ModifiersFor(synergyTeam, true);
            var report = TeamChemistryEngine.EvaluateRoster(synergyTeam.AvailableRoster, synergyTeam.PayrollCap, synergyTeam.TeamworkBuff);
            Assert.AreEqual(GMChemistryModifiers.From(report).PowerBonus, mods.Chemistry.PowerBonus, "경기 실효 전력에 연도 시너지 반영");
        }

        // ================================================================== 2) [진행하기] 라우팅

        [Test]
        public void T2_ContinueRoutesToMainHome_ThenPreGame_NoInstantGame()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "NC", seed: 74));
            var hub = NewHub(sim, out _, out var prePost);
            hub.SelectMainTab(4);
            Assert.AreNotEqual(GMOotpFrontOfficeUIController.PaneOwner, hub.CurrentPane);
            StringAssert.Contains("메인 홈으로", hub.Root.Find("ContinueButton").GetComponentInChildren<Text>().text);
            Click(hub.Root, "ContinueButton");
            Assert.IsTrue(hub.IsAtMainHome, "[진행하기] → 리그 플레이 메인 홈(프런트 오피스 6분할)");
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneOwner, hub.CurrentPane);
            Assert.IsTrue(hub.Pane(GMOotpFrontOfficeUIController.PaneOwner).gameObject.activeSelf);
            Assert.AreEqual(0, sim.GamesPlayed, "경기를 바로 진행하지 않는다");
            Assert.IsFalse(prePost.gameObject.activeSelf);

            // 메인 홈에서 다시 누르면 ① 전력 분석
            Click(hub.Root, "ContinueButton");
            Assert.AreEqual(GMMatchStage.PreGame, prePost.Stage, "메인 홈 → 전력 분석");
            Assert.IsTrue(prePost.PreRoot.gameObject.activeSelf);
            Assert.AreEqual(0, sim.GamesPlayed, "전력 분석 단계에서도 경기 미진행");
            prePost.CloseAll();

            // 툴바 [홈] · 사이드바 [홈] · 메뉴 항목도 메인 홈으로
            hub.SelectMainTab(2);
            Click(hub.Root, "NavHome");
            Assert.IsTrue(hub.IsAtMainHome, "툴바 홈");
            hub.SelectMainTab(6);
            Click(hub.Root, "Quick0");
            Assert.IsTrue(hub.IsAtMainHome, "사이드바 홈");
            hub.SelectMainTab(3);
            hub.ToggleMenu(1);
            Assert.IsTrue(hub.MenuList(1).gameObject.activeSelf, "경기 메뉴 열림");
            Click(hub.MenuList(1), "MenuItem0");
            Assert.IsTrue(hub.IsAtMainHome, "경기 ▼ 진행하기 → 메인 홈");
            Assert.IsFalse(hub.MenuList(1).gameObject.activeSelf, "메뉴 닫힘");

            // 시즌 종료 후 [진행하기] = (TASK-GM-11) 시즌 결산실 1회 → 포스트시즌 트리
            var full = SharedSeason();
            hub.Bind(full);
            hub.SelectMainTab(2);
            if (hub.SeasonReviewPending)
            {
                Assert.IsTrue(hub.Continue());
                Assert.AreEqual(GMOotpFrontOfficeUIController.PaneSeasonSummary, hub.CurrentPane, "정규시즌 종료 → 시즌 결산실(TASK-GM-11)");
            }
            Assert.IsTrue(hub.Continue());
            Assert.AreEqual(GMOotpFrontOfficeUIController.PanePostseason, hub.CurrentPane, "정규시즌 종료 → 포스트시즌 트리");
        }

        // ================================================================== 3) OOTP 레이아웃 무결성

        [Test]
        public void T3_OotpLayouts_ManagerSetup_Calendar_Bracket_NoOverlap_Min15_NoBold()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "LOT", seed: 75));
            var hub = NewHub(sim, out var canvas, out _);
            CheckLayer(hub.Root, "허브 루트(툴바 · 배너 · 사이드바 · 탭)");
            for (int i = 0; i < 6; i++)
            {
                Assert.IsNotNull(hub.Root.Find($"Menu{i}"), $"툴바 메뉴 {i}");
                hub.ToggleMenu(i);
                CheckLayer(hub.MenuList(i), $"툴바 드롭다운 {i}");
            }
            hub.CloseMenus();
            Assert.AreEqual(GMOotpFrontOfficeUIController.SidebarCount, Enumerable.Range(0, GMOotpFrontOfficeUIController.SidebarCount).Count(i => hub.Root.Find($"Quick{i}") != null), "세로 퀵 사이드바");
            Assert.AreEqual(GMOotpFrontOfficeUIController.ContentRight / 1920f, hub.ContentArea.anchorMax.x, 1e-4f, "콘텐츠 영역이 사이드바를 비운다");
            Assert.AreEqual(hub.ContentArea, hub.Pane(GMOotpFrontOfficeUIController.PaneOwner).parent, "화면(Pane)은 콘텐츠 영역 안");
            var owner = hub.Pane(GMOotpFrontOfficeUIController.PaneOwner);
            StringAssert.Contains("TOP 12", T(owner, "CostEfficientPanel/CostTitle"));
            Assert.IsNotNull(owner.Find($"CostEfficientPanel/Cost{GMOotpFrontOfficeUIController.CostRows - 1}_0"), "가성비 12행");
            foreach (var panel in new[] { "OwnerInfoPanel", "OwnerGoalsPanel", "RecordHistoryPanel", "CostEfficientPanel", "BudgetPanel", "AttendancePanel" }) CheckLayer(owner.Find(panel), panel);
            StringAssert.Contains("2026", T(hub.Root, "ToolbarInfo1"));

            // 감독 설정(215504) - 10구단 리스트 · 프로필 · 옵션 5종 · 난이도 드롭다운
            hub.OpenManagerSetup();
            Assert.IsTrue(hub.IsManagerSetupOpen);
            var popup = hub.Root.Find(GMOotpFrontOfficeUIController.ManagerSetupName);
            CheckLayer(popup, "감독 설정");
            for (int i = 0; i < 10; i++) Assert.IsNotNull(popup.Find($"TeamPick{i}"), $"구단 {i}");
            Click(popup, "TeamPick5");
            Assert.AreEqual(NameAliasTable.CanonicalTeamCodes[5], hub.PendingManagerTeam);
            for (int k = 0; k < GMOotpFrontOfficeUIController.ManagerOptionCount; k++)
            {
                StringAssert.StartsWith("[  ]", popup.Find($"Option{k}").GetComponentInChildren<Text>().text);
                Click(popup, $"Option{k}");
                StringAssert.StartsWith("[✔]", popup.Find($"Option{k}").GetComponentInChildren<Text>().text, $"옵션 {k} 체크");
            }
            Click(popup, "DifficultyDrop");
            Assert.IsTrue(popup.Find("DifficultyList").gameObject.activeSelf, "난이도 드롭다운");
            CheckLayer(popup.Find("DifficultyList"), "난이도 목록");
            Click(popup, "DifficultyList/Item3");
            Assert.AreEqual(GMDifficulty.HallOfFame, hub.PendingDifficulty);
            Click(popup, "ModeDrop");
            CheckLayer(popup.Find("ModeList"), "시작 모드 목록");
            Click(popup, "ModeList/Item0");
            popup.Find("NameInput").GetComponent<InputField>().text = "김단장";
            hub.LeagueFactory = (m, code, v) => NewLeague(m, code, 76);
            var started = hub.ConfirmManagerSetup();
            Assert.IsNotNull(started);
            var mgr = started.League.FrontOffice.Manager;
            Assert.AreEqual("김단장", mgr.Name);
            Assert.IsTrue(mgr.Commissioner && mgr.NoFiring && mgr.HardTrade && mgr.PennantMode && mgr.Challenge, "옵션 5종 적용");
            Assert.AreEqual(GMDifficulty.HallOfFame, started.League.FrontOffice.Difficulty);
            Assert.AreEqual(1, started.League.FrontOffice.HouseRuleMaxFA, "챌린지 모드 FA 1회");
            Assert.AreEqual(NameAliasTable.CanonicalTeamCodes[5], started.League.SelectedTeamCode);
            Assert.IsTrue(hub.IsAtMainHome, "게임 시작 → 메인 홈");
            Assert.IsFalse(hub.IsManagerSetupOpen);
            // 옵션 효과 - 커미셔너 모드는 트레이드 가치 판정을 건너뛴다
            var l2 = started.League;
            var partner = l2.Teams.Values.First(t => !t.IsUserTeam);
            var bad = GMStoveLeagueMarket.Evaluate(l2, l2.UserTeam, new[] { l2.UserTeam.Roster.OrderBy(p => p.BaseOverall).First() }, partner, new[] { partner.Roster.OrderByDescending(p => p.BaseOverall).First() });
            Assert.IsTrue(bad.Acceptable, "커미셔너 모드");

            // 시즌 일정(215611) - 대각선 매치업 카드 · 1~12월 달력 · 홈 구단색 / 원정 회색 · 결과 스코어
            var sim2 = new GMLiveSeasonSimulator(NewLeague(team: "LOT", seed: 77));
            hub.Bind(sim2);
            RunDays(sim2, 6);
            hub.OpenSchedule();
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneSchedule, hub.CurrentPane);
            var sched = hub.Pane(GMOotpFrontOfficeUIController.PaneSchedule);
            CheckPane(hub, "시즌 일정");
            Assert.AreEqual(4, hub.ScheduleMonth, "6경기 후 = 4월");
            var cells = Enumerable.Range(0, GMOotpFrontOfficeUIController.ScheduleRows).SelectMany(r => Enumerable.Range(0, 7).Select(c => sched.Find($"Day{r}_{c}"))).ToList();
            var homeCells = cells.Where(c => T(c, "Opp").StartsWith("vs")).ToList();
            var awayCells = cells.Where(c => T(c, "Opp").StartsWith("@")).ToList();
            Assert.IsNotEmpty(homeCells, "4월 홈 경기");
            Assert.IsNotEmpty(awayCells, "4월 원정 경기");
            Assert.IsTrue(awayCells.Select(c => c.Find("Bg").GetComponent<Image>().color).Any(col => Mathf.Abs(col.r - col.b) < 0.06f && col.r < 0.4f), "원정 = 회색");
            Assert.IsTrue(homeCells.Any(c => T(c, "Info").Contains(":")), "예정 경기 = 경기 시간");
            hub.SelectScheduleMonth(3);
            CheckPane(hub, "3월 달력");
            var played = cells.Where(c => T(c, "Info").StartsWith("승") || T(c, "Info").StartsWith("패") || T(c, "Info").StartsWith("무")).ToList();
            Assert.Greater(played.Count, 0, "치른 경기 결과 스코어");
            Assert.AreEqual(sim2.League.UserResults.Count(r => GMLiveSeasonSimulator.DateOf(r.Day, sim2.League.SeasonYear).Month == 3), played.Count, "3월 결과 칸 수 = 3월 경기 수");
            hub.SelectScheduleMonth(1);
            Assert.IsTrue(cells.All(c => T(c, "Opp") == ""), "1월 = 경기 없음");
            StringAssert.Contains("다음 상대", T(sched, "MatchupCardPanel/NextTitle"));
            Assert.AreEqual("18:30", GMOotpFrontOfficeUIController.GameTime(new DateTime(2026, 4, 7)));
            Assert.AreEqual("17:00", GMOotpFrontOfficeUIController.GameTime(new DateTime(2026, 4, 4)));
            Assert.AreEqual("14:00", GMOotpFrontOfficeUIController.GameTime(new DateTime(2026, 4, 5)));

            // 포스트시즌 트리(215614) - 시즌 중 예상 대진 / 시즌 종료 후 실제 대진
            hub.OpenPostseasonTree();
            CheckPane(hub, "포스트시즌 트리(시즌 중)");
            var full = SharedSeason();
            hub.Bind(full);
            hub.OpenPostseasonTree();
            CheckPane(hub, "포스트시즌 트리(시즌 종료)");
            var tree = hub.Pane(GMOotpFrontOfficeUIController.PanePostseason);
            for (int col = 0; col < 4; col++) Assert.AreEqual(GMOotpFrontOfficeUIController.BracketHeads[col], T(tree, $"BracketHead{col}"));
            Assert.AreNotEqual("미정", T(tree, "Bracket0_0/Name"), "와일드카드 4위 시드");

            // 전 탭 · 서브 탭(새 화면 포함) 겹침 0 · 15pt+
            for (int t = 0; t < GMOotpFrontOfficeUIController.MainTabCount; t++)
            {
                hub.SelectMainTab(t);
                for (int s = 0; s < GMOotpFrontOfficeUIController.SubTabMax; s++)
                {
                    var sub = hub.Root.Find($"SubTab{s}");
                    if (!sub.gameObject.activeSelf || sub.GetComponentInChildren<Text>().text.EndsWith("▶")) continue;
                    hub.SelectSubTab(s);
                    CheckPane(hub, $"탭 {t}-{s}");
                }
            }

            // 씬 - 허브(툴바 · 사이드바 · 일정 · 트리 · 감독 설정) · 경기 3단계 화면 · Bold 0
            OpenScene();
            var sceneHub = Object.FindAnyObjectByType<GMOotpFrontOfficeUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(sceneHub);
            var sceneRoot = sceneHub.transform.Find(GMOotpFrontOfficeUIController.RootName);
            foreach (var path in new[] { "ToolbarBg", "Menu0", "Quick0", GMOotpFrontOfficeUIController.ManagerSetupName,
                         $"{GMOotpFrontOfficeUIController.ContentAreaName}/{GMOotpFrontOfficeUIController.PaneSchedule}", $"{GMOotpFrontOfficeUIController.ContentAreaName}/{GMOotpFrontOfficeUIController.PanePostseason}" })
                Assert.IsNotNull(sceneRoot.Find(path), "씬 허브 " + path);
            var scenePrePost = Object.FindAnyObjectByType<GMMatchPrePostUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(scenePrePost.transform.Find(GMMatchPrePostUIController.LiveRootName), "씬 실시간 이닝 경기 화면");
            var bold = SetupTask191.SceneTexts().Where(x => x.fontStyle == FontStyle.Bold || x.fontStyle == FontStyle.BoldAndItalic).Select(x => x.name).ToList();
            Assert.IsEmpty(bold, "씬 전체 FontStyle.Bold 0건");
        }

        // ================================================================== 4) 포스트시즌 1경기씩 순차 진행

        [Test]
        public void T4_Postseason_Bracket_OneGameAtATime_UserGameThreeStage()
        {
            // 공유 시즌을 건드리지 않도록 새 시즌을 돈다
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "KIA", seed: 7171));
            sim.StartRun(GMRunMode.FullSeason);
            sim.RunUntilStop();
            Assert.IsTrue(sim.IsSeasonComplete);
            var league = sim.League;
            var ps = GMAwardEvaluator.BeginPostseason(sim);
            Assert.IsNotNull(ps);
            Assert.AreEqual(5, ps.SeedCodes.Count);
            Assert.AreEqual(1, ps.Series.Count, "와일드카드부터 시작");
            Assert.IsFalse(ps.Completed, "단숨에 스킵하지 않는다");
            // 내 구단을 와일드카드 4위로 맞춘다(내 구단 경기 3단계 지휘 검증)
            string me = ps.SeedCodes[3];
            foreach (var t in league.Teams.Values) t.IsUserTeam = t.TeamCode == me;
            league.SelectedTeamCode = me;

            var hub = NewHub(sim, out _, out var prePost);
            hub.OpenPostseasonTree();
            var tree = hub.Pane(GMOotpFrontOfficeUIController.PanePostseason);
            int userGames = 0, steps = 0, guard = 0;
            while (!ps.Completed && guard++ < 60)
            {
                var next = GMAwardEvaluator.NextPostseasonGame(sim);
                Assert.IsNotNull(next);
                int seriesBefore = ps.Series.Count;
                var series = ps.Series[next.SeriesIndex];
                int winsBefore = series.HigherWins + series.LowerWins, gamesBefore = series.Games.Count, simulatedBefore = ps.GamesSimulated;
                if (next.IsUserGame)
                {
                    Click(tree, "NextGameButton");
                    Assert.AreEqual(GMMatchStage.PreGame, prePost.Stage, "내 구단 경기 = ① 전력 분석");
                    Assert.IsTrue(prePost.CurrentPreview.IsPostseason);
                    Assert.AreEqual(simulatedBefore, ps.GamesSimulated, "전력 분석 단계에서는 진행 안 함");
                    Click(prePost.PreRoot, "StartButton");
                    Assert.AreEqual(GMMatchStage.LiveInning, prePost.Stage, "② 실시간 이닝 경기");
                    prePost.LiveStepInning();
                    prePost.LiveToEnd();
                    Click(prePost.LiveRoot, "ResultButton");
                    Assert.AreEqual(GMMatchStage.PostGame, prePost.Stage, "③ 경기 결과");
                    StringAssert.Contains(next.Round, prePost.CurrentBoxScore.GameTitle);
                    StringAssert.Contains("포스트시즌", T(prePost.PostRoot, "Subtitle"));
                    prePost.CloseAll();
                    userGames++;
                }
                else
                {
                    Click(tree, "NextGameButton");
                    Assert.AreEqual(GMMatchStage.None, prePost.Stage);
                }
                steps++;
                Assert.AreEqual(simulatedBefore + 1, ps.GamesSimulated, "정확히 1경기 진행");
                Assert.AreEqual(gamesBefore + 1, series.Games.Count, "시리즈 경기 기록 +1");
                Assert.That(series.HigherWins + series.LowerWins - winsBefore, Is.InRange(0, 1), "시리즈 승수 누적 0~1(무승부 재경기 0)");
                if (!string.IsNullOrEmpty(series.WinnerCode) && !ps.Completed) Assert.AreEqual(seriesBefore + 1, ps.Series.Count, "시리즈 종료 → 다음 라운드 개설");
                // 브래킷 표시 = 시리즈 승수
                hub.OpenPostseasonTree();
                Assert.AreEqual(series.HigherWins.ToString(), T(tree, $"Bracket{next.SeriesIndex}_0/Wins"));
                Assert.AreEqual(series.LowerWins.ToString(), T(tree, $"Bracket{next.SeriesIndex}_1/Wins"));
            }
            Assert.IsTrue(ps.Completed, "한국시리즈까지 완료");
            Assert.Greater(userGames, 0, "내 구단 경기 3단계 지휘");
            Assert.That(steps, Is.InRange(1 + 3 + 3 + 4, 2 + 5 + 5 + 7 + 10), "경기 수(무승부 재경기 여유)");
            Assert.AreEqual(4, ps.Series.Count);
            Assert.AreEqual(ps.Series[3].WinnerCode, ps.ChampionCode);
            Assert.IsNotEmpty(ps.KoreanSeriesMvp, "한국시리즈 MVP");
            Assert.AreEqual(10, ps.FinalRankCodes.Distinct().Count(), "최종 순위 1~10위");
            Assert.AreEqual(GMSeasonPhase.AwardsCeremony, league.Phase);
            Assert.AreEqual(1, ps.Series[0].HigherNeeds, "와일드카드 4위 1승 어드밴티지");
            Assert.AreEqual(2, ps.Series[0].LowerNeeds, "와일드카드 5위 2승 필요");
            Assert.IsTrue(ps.Series.All(s => s.HigherWins >= s.HigherNeeds || s.LowerWins >= s.LowerNeeds));
            CheckPane(hub, "포스트시즌 종료 트리");
            StringAssert.Contains("시상식", tree.Find("NextGameButton").GetComponentInChildren<Text>().text);
            Assert.IsNull(GMAwardEvaluator.NextPostseasonGame(sim), "남은 경기 없음");
            Assert.IsNotNull(GMAwardEvaluator.RunPostseason(sim), "완료 상태 재호출 안전");

            // 한 번에 진행(RunPostseason)도 같은 1경기 단위 경로 - 새 리그(정규시즌 종료 상태로 표시)로 확인
            // (T3이 씬을 다시 열면 공유 시즌 선수의 원본 템플릿이 파괴되므로 공유 시즌은 쓰지 않는다)
            var quick = new GMLiveSeasonSimulator(NewLeague(team: "LG", seed: 7272));
            quick.League.GamesPlayed = GMLiveSeasonSimulator.SeasonGames;
            var auto = GMAwardEvaluator.RunPostseason(quick);
            Assert.IsNotNull(auto);
            Assert.AreEqual(auto.Series.Sum(s => s.Games.Count), auto.GamesSimulated, "자동 진행 = 1경기씩 누적");
        }

        // ================================================================== 5) 한 경기 3단계 플로우

        [Test]
        public void T5_SingleGame_PreGame_LiveInning_PostGame_StateTransitions()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "SSG", seed: 78));
            var hub = NewHub(sim, out _, out var prePost);
            Assert.AreEqual(GMMatchStage.None, prePost.Stage);
            Assert.IsTrue(prePost.ShowPreGameView(sim));
            Assert.AreEqual(GMMatchStage.PreGame, prePost.Stage, "① 전력 분석");
            StringAssert.Contains("플레이 볼", prePost.PreRoot.Find("StartButton").GetComponentInChildren<Text>().text);
            CheckLayer(prePost.PreRoot, "① 전력 분석");

            Click(prePost.PreRoot, "StartButton");
            Assert.AreEqual(GMMatchStage.LiveInning, prePost.Stage, "② 실시간 이닝 경기");
            Assert.IsTrue(prePost.LiveRoot.gameObject.activeSelf);
            Assert.IsFalse(prePost.PreRoot.gameObject.activeSelf);
            Assert.IsFalse(prePost.PostRoot.gameObject.activeSelf);
            Assert.IsNotNull(sim.LiveSession, "실시간 세션");
            Assert.AreEqual(0, sim.GamesPlayed, "② 단계 - 하루가 아직 넘어가지 않음");
            Assert.AreEqual(GMSeasonPhase.RegularSeason, sim.League.Phase);
            Assert.IsFalse(sim.StepGameDay(), "실시간 경기 중 다른 진행 차단");
            Assert.IsFalse(prePost.LiveRoot.Find("ResultButton").GetComponent<Button>().interactable, "경기 중 결과 보기 잠금");
            CheckLive(prePost, "경기 시작");

            Click(prePost.LiveRoot, "OneAtBatButton");
            Assert.AreEqual(1, prePost.Session.PlateAppearances, "1타석");
            Assert.AreEqual(1, prePost.Session.PlayByPlay.Count, "문자 중계 1줄");
            StringAssert.Contains("1회초", T(prePost.LiveRoot, "PlayByPlayPanel/LiveLog"));
            Assert.Greater(prePost.Session.LastPitchLocations().Count, 0, "ABS 탄착군");
            Assert.IsTrue(prePost.LiveRoot.Find("AbsZonePanel/PitchArea/Pitch0").gameObject.activeSelf);
            int before = prePost.Session.PlateAppearances;
            Click(prePost.LiveRoot, "OneInningButton");
            Assert.Greater(prePost.Session.PlateAppearances, before, "1이닝");
            Assert.IsTrue(prePost.Session.LastStep.HalfInningEnded || prePost.Session.IsOver, "하프이닝 단위 정지");
            CheckLive(prePost, "1이닝 후");

            // 전술 개입 - 공격(강공 · 작전 · 대타) / 수비(투수 교체)
            bool pinched = false, changed = false, tactic = false;
            for (int g = 0; g < 30 && !prePost.Session.IsOver && !(pinched && changed && tactic); g++)
            {
                if (prePost.Session.UserBatting)
                {
                    Assert.IsFalse(prePost.LivePitchingChange(), "공격 중 투수 교체 불가");
                    if (!tactic) { tactic = prePost.LivePowerTactic(); StringAssert.Contains("강공", prePost.LiveMessage); }
                    if (!pinched) { pinched = prePost.LivePinchHit(); if (pinched) StringAssert.Contains("대타", prePost.LiveMessage); }
                }
                else
                {
                    Assert.IsFalse(prePost.LivePinchHit(), "수비 중 대타 불가");
                    if (!changed) { changed = prePost.LivePitchingChange(); if (changed) StringAssert.Contains("투수 교체", prePost.LiveMessage); }
                }
                prePost.LiveStepInning();
            }
            Assert.IsTrue(tactic, "강공 작전");
            Assert.IsTrue(pinched || changed, "선수 교체 개입");
            Assert.IsTrue(prePost.Session.PlayByPlay.Any(l => l.Contains("[작전") || l.Contains("[교체]")), "개입이 중계에 반영");

            Click(prePost.LiveRoot, "ToEndButton");
            Assert.IsTrue(prePost.Session.IsOver, "경기 끝까지");
            Assert.IsTrue(prePost.LiveRoot.Find("ResultButton").GetComponent<Button>().interactable);
            StringAssert.Contains("경기 종료", T(prePost.LiveRoot, "LiveSituation"));
            int pas = prePost.Session.PlateAppearances;
            Click(prePost.LiveRoot, "ResultButton");
            Assert.AreEqual(GMMatchStage.PostGame, prePost.Stage, "③ 경기 결과");
            Assert.IsTrue(prePost.PostRoot.gameObject.activeSelf);
            Assert.IsFalse(prePost.LiveRoot.gameObject.activeSelf);
            Assert.IsNull(sim.LiveSession);
            Assert.AreEqual(1, sim.GamesPlayed, "하루 진행(5경기)");
            var box = prePost.CurrentBoxScore;
            Assert.IsNotNull(box);
            Assert.AreSame(box, sim.League.LastUserMatchBoxScore);
            Assert.AreEqual(pas + 1, box.WpaPoints.Count, "WPA = 타석 수 + 경기 전");
            foreach (var para in new[] { box.Recap.Paragraph1, box.Recap.Paragraph2, box.Recap.Paragraph3, box.Recap.Paragraph4 }) Assert.IsNotEmpty(para, "4문단 기사");
            Assert.AreEqual(box.HomeInningRuns.Where(r => r > 0).Sum(), box.HomeR, "전광판 = 득점");
            Assert.AreEqual(1, sim.League.UserResults.Count, "시즌 일정 결과 기록");
            Assert.AreEqual(10, sim.League.Records.Values.Sum(r => r.G), "다른 4경기도 진행(10구단 1경기씩)");
            CheckLayer(prePost.PostRoot, "③ 경기 결과");

            // 빠른 경로(StartMatch)도 같은 3단계 세션을 거친다(부상 인터럽트가 남아 있으면 경기를 열지 않으므로 먼저 처리)
            while (sim.PendingInterrupt != null) sim.ResolveInterrupt(GMInterruptChoice.AutoCallUp);
            Assert.IsTrue(prePost.ShowPreGameView(sim));
            Assert.IsTrue(prePost.StartMatch());
            Assert.AreEqual(GMMatchStage.PostGame, prePost.Stage);
            Assert.AreEqual(2, sim.GamesPlayed);

            // 고속 진행 토글
            while (sim.PendingInterrupt != null) sim.ResolveInterrupt(GMInterruptChoice.AutoCallUp);
            Assert.IsTrue(prePost.ShowPreGameView(sim));
            Assert.IsTrue(prePost.PlayBall());
            prePost.ToggleAutoPlay();
            Assert.IsTrue(prePost.IsAutoPlaying, "고속 진행");
            StringAssert.Contains("일시정지", prePost.LiveRoot.Find("AutoButton").GetComponentInChildren<Text>().text);
            prePost.ToggleAutoPlay();
            Assert.IsFalse(prePost.IsAutoPlaying);
            Assert.IsTrue(prePost.FinishLiveMatch());
            Assert.AreEqual(3, sim.GamesPlayed);
            hub.Refresh();
        }

        // ================================================================== 6) 실책 상향

        [Test]
        public void T6_ErrorRate_SeasonErrorsPerTeam75to110()
        {
            Assert.AreEqual(0.048f, GMChemistryModifiers.DefaultBaseErrorRate, 1e-6f, "지시서 0.062 → 실측 보정 0.048(DCL-162)");
            var full = SharedSeason();
            var perTeam = full.League.Teams.Keys.Select(c => full.League.Stats.Values.Where(s => s.TeamCode == c).Sum(s => s.Errors)).ToList();
            TestContext.WriteLine($"[GM07 실책] 144경기 팀 실책 평균 {perTeam.Average():0.0} (최소 {perTeam.Min()} · 최대 {perTeam.Max()})");
            Debug.Log($"[GM07 실책] 144경기 팀 실책 평균 {perTeam.Average():0.0} (최소 {perTeam.Min()} · 최대 {perTeam.Max()})");
            Assert.That(perTeam.Average(), Is.InRange(75.0, 110.0), "팀당 시즌 평균 실책 75~110개");
            Assert.That(full.League.Records.Values.Average(r => r.G), Is.EqualTo(144).Within(0.01), "144경기");
            Assert.Greater(full.League.UserResults.Count, 140, "내 구단 결과 기록(무승부 포함 144)");
        }

        // ================================================================== 공용 검사

        private static void CheckLive(GMMatchPrePostUIController prePost, string label)
        {
            CheckLayer(prePost.LiveRoot, label + " 실시간 화면");
            foreach (var panel in new[] { "MatchupPanel", "AbsZonePanel", "PlayByPlayPanel", "WinProbabilityPanel", "TacticsPanel" })
                CheckLayer(prePost.LiveRoot.Find(panel), $"{label} {panel}");
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
                if (child.GetComponent<Text>() != null || child.GetComponent<Button>() != null) continue;
                bool container = child.name.EndsWith("Panel") || child.name.StartsWith("Day") || child.name.StartsWith("Bracket");
                if (container && child.childCount > 0) CheckLayer(child, label + " " + child.name);
            }
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

        private void OpenScene()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
        }
    }
}
