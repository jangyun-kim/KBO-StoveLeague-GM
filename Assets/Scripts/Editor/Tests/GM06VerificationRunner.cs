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
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-GM-06] 6대 자동 검증(Unity CLI BatchPipelineGM06 1회 실행):
    ///   1) 1920×1080 Landscape 전환 · OOTP 27 프런트 오피스 허브 6대 패널 · [건의(DISCUSS)]
    ///   2) 스토브리그 협상 4대 기능(재계약 · 주장 · 방출 / FA 스카우팅 · 입찰 / Shop a Player · 1:1 · 2:2 트레이드 / 신인 드래프트)
    ///   3) 백분위 랭킹(1~99%) · 4단계 난이도 · 하우스 룰
    ///   4) ABS(투수 · 타자 · 포수 ABSZoneSkill → 삼진 · 볼넷 · 보더라인 콜) · GM-05 잔여 3건(실책률 2.2% · 자동 로테이션 기본 ON · 전담 대상 직접 지정)
    ///   5) 스토리 안건 선택지(예산 · 팬 · 신임도 · 팀워크) · 4종 멀티 엔딩 · 시즌 결산 이력
    ///   6) 1920×1080 전 화면(프런트 오피스 · 협상 탭 · 대시보드 · 전/후 박스스코어 · 시상식 · 치어리더) 겹침 0 · 15pt 이상 · Bold 0 · 씬
    /// </summary>
    public class GM06VerificationRunner
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
            dbObject = new GameObject("GM06_PlayerDatabase");
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

        /// <summary>정규시즌 144경기 완주(공유) - 실책 스케일 · ABS 누적 · UI 무결성(시즌 종료 상태)에 쓴다.</summary>
        private static GMLiveSeasonSimulator SharedSeason()
        {
            if (season != null) return season;
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "DOO", seed: 6066));
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
            var canvasGo = new GameObject("GM06_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
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

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim, out GameObject canvas)
        {
            canvas = NewCanvas();
            var hub = NewView<GMOotpFrontOfficeUIController>(canvas, "GM06_Hub");
            hub.Build();
            hub.Bind(sim);
            return hub;
        }

        private static string T(Transform root, string path) => root.Find(path).GetComponent<Text>().text;
        private static void Click(Transform root, string path) => root.Find(path).GetComponent<Button>().onClick.Invoke();

        // ================================================================== 1) Landscape · 프런트 오피스 6대 패널 · 건의

        [Test]
        public void T1_Landscape1920x1080_FrontOfficeHub_SixPanels_Discuss()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "LOT", seed: 61));
            var hub = NewHub(sim, out _);
            var league = sim.League;
            var team = league.UserTeam;
            var fo = league.FrontOffice;
            Assert.IsTrue(fo.Initialized, "프런트 오피스 초기화");
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneOwner, hub.CurrentPane, "기본 허브 = 구단주·재정 대시보드");
            var owner = hub.Pane(GMOotpFrontOfficeUIController.PaneOwner);
            Assert.IsTrue(owner.gameObject.activeSelf);

            // 헤더 · 티커 · 진행 버튼 · 8탭 · 골드 서브 탭
            StringAssert.Contains("롯데", T(hub.Root, "TeamName"));
            StringAssert.Contains("승률", T(hub.Root, "TeamRecord"));
            StringAssert.Contains("2026년 스토브리그", T(hub.Root, "TeamDate"));
            for (int i = 0; i < 4; i++) Assert.IsNotEmpty(T(hub.Root, $"TickerValue{i}"), $"4일 티커 {i}");
            StringAssert.Contains("진행하기", hub.Root.Find("ContinueButton").GetComponentInChildren<Text>().text);
            for (int i = 0; i < GMOotpFrontOfficeUIController.MainTabCount; i++)
                Assert.AreEqual(GMOotpFrontOfficeUIController.MainTabLabels[i], hub.Root.Find($"MainTab{i}").GetComponentInChildren<Text>().text);
            Assert.AreEqual(GMOotpFrontOfficeUIController.Gold, hub.Root.Find("SubTabBar").GetComponent<Image>().color, "#F5B82E 골드 서브 탭 바");
            Assert.AreEqual("구단주·재정 대시보드", hub.Root.Find("SubTab0").GetComponentInChildren<Text>().text);
            Assert.AreEqual("스토리 안건", hub.Root.Find("SubTab5").GetComponentInChildren<Text>().text);

            // 6대 패널
            foreach (var panel in new[] { "OwnerInfoPanel", "OwnerGoalsPanel", "RecordHistoryPanel", "CostEfficientPanel", "BudgetPanel", "AttendancePanel" })
            {
                var p = owner.Find(panel);
                Assert.IsNotNull(p, panel);
                Assert.IsTrue(p.gameObject.activeSelf, panel);
                CheckLayer(p, panel);
            }
            StringAssert.Contains("롯데 그룹", T(owner, "OwnerInfoPanel/OwnerValue0"), "모기업 · 구단주");
            for (int k = 0; k < 10; k++) Assert.IsNotEmpty(T(owner, $"OwnerInfoPanel/OwnerValue{k}"), $"구단주 정보 {k}");
            StringAssert.Contains(")", T(owner, "OwnerInfoPanel/OwnerValue5"), "구단주 기분 이모티콘");
            Assert.That(fo.Goals.Count, Is.InRange(5, 6), "구단주 목표 5~6개");
            CollectionAssert.AreEquivalent(Enum.GetValues(typeof(GMGoalKind)).Cast<GMGoalKind>(), fo.Goals.Select(g => g.Kind), "6종 목표");
            for (int r = 0; r < fo.Goals.Count; r++)
            {
                Assert.AreEqual(fo.Goals[r].YearIssued.ToString(), T(owner, $"OwnerGoalsPanel/Goal{r}_0"));
                Assert.IsNotEmpty(T(owner, $"OwnerGoalsPanel/Goal{r}_5"), "진행 상황");
                var color = owner.Find($"OwnerGoalsPanel/Goal{r}_5").GetComponent<Text>().color;
                Assert.IsTrue(fo.Goals[r].OnTrack ? color.g > color.r : color.r > color.g, "진행 상황 초록/빨강");
            }
            int bars = Enumerable.Range(0, GMOotpFrontOfficeUIController.HistoryBars).Count(i => owner.Find($"RecordHistoryPanel/RecBar{i}").gameObject.activeSelf);
            Assert.AreEqual(GMFrontOffice.HistorySeasons, bars, "연도별 승률 막대(2017~2025)");
            var hist = GMFrontOffice.RecordBars(league, team.TeamCode);
            for (int i = 0; i < bars; i++)
            {
                var c = owner.Find($"RecordHistoryPanel/RecBar{i}").GetComponent<Image>().color;
                Assert.AreEqual(hist[i].pct >= 0.5, c.g > c.r, $"{hist[i].year} 5할 이상 초록 / 미만 빨강");
            }
            StringAssert.StartsWith("1) ", T(owner, "CostEfficientPanel/Cost0_0"), "가성비 TOP 10");
            var cost = GMFrontOffice.CostEfficient(league, team);
            Assert.AreEqual(10, cost.Count);
            for (int i = 1; i < cost.Count; i++) Assert.LessOrEqual(cost[i - 1].perWar, cost[i].perWar, "1WAR당 연봉 오름차순");
            for (int k = 0; k < GMOotpFrontOfficeUIController.BudgetRows; k++) StringAssert.Contains("원", T(owner, $"BudgetPanel/BudValue{k}"), $"재정표 {k}");
            var budget = GMFrontOffice.Budget(league, team);
            Assert.AreEqual(budget.ProjectedBudget - budget.TotalExpenses, budget.MoneyForFA, "FA 가용 자금 = 총예산 - 총 지출");
            Assert.AreEqual(GMOotpFrontOfficeUIController.HistoryBars, Enumerable.Range(0, GMOotpFrontOfficeUIController.HistoryBars).Count(i => owner.Find($"AttendancePanel/AttBar{i}").gameObject.activeSelf), "관중 막대 10개(올해 예상 포함)");
            StringAssert.Contains("엔트리", T(owner, "AttendancePanel/AttCheer"), "현재 출전 치어리더 엔트리");

            // [건의(DISCUSS)] - 추가 예산(신임도 소모 vs 예산 확보)
            int row = fo.Goals.FindIndex(GMFrontOffice.CanDiscuss);
            Assert.GreaterOrEqual(row, 0, "건의 가능한 목표");
            Assert.IsTrue(owner.Find($"OwnerGoalsPanel/GoalDiscuss{row}").gameObject.activeSelf);
            int trust0 = fo.OwnerTrust;
            long budget0 = team.Budget;
            Click(owner, $"OwnerGoalsPanel/GoalDiscuss{row}");
            Assert.IsTrue(hub.IsDiscussOpen, "건의 팝업");
            CheckLayer(hub.Root.Find("DiscussPopup"), "건의 팝업");
            Click(hub.Root, "DiscussPopup/DiscussBudget");
            Assert.IsFalse(hub.IsDiscussOpen);
            Assert.AreEqual(trust0 - GMFrontOffice.ExtraBudgetTrustCost, fo.OwnerTrust, "신임도 소모");
            Assert.AreEqual(budget0 + team.PayrollCap * (long)GMFrontOffice.ExtraBudgetPercent / 100, team.Budget, "추가 예산 확보");
            StringAssert.Contains("추가 예산", hub.StatusText);
            Assert.IsFalse(owner.Find($"OwnerGoalsPanel/GoalDiscuss{row}").gameObject.activeSelf, "올해 같은 목표 재건의 불가");
            // 목표 완화 · 신임도 부족 거절
            var relax = fo.Goals.FirstOrDefault(g => GMFrontOffice.CanDiscuss(g));
            if (relax != null)
            {
                var prio = relax.Priority;
                Assert.IsTrue(GMFrontOffice.Discuss(league, relax, GMDiscussOption.RelaxGoal, out var relaxMsg), relaxMsg);
                Assert.LessOrEqual((int)relax.Priority, (int)prio, "우선순위 완화");
                Assert.IsTrue(relax.Discussed);
            }
            fo.OwnerTrust = 10;
            var refuse = fo.Goals.FirstOrDefault(g => GMFrontOffice.CanDiscuss(g));
            if (refuse != null)
            {
                Assert.IsFalse(GMFrontOffice.Discuss(league, refuse, GMDiscussOption.ExtraBudget, out var no));
                StringAssert.Contains("거절", no);
            }

            // 탭 이동 · [진행하기]
            hub.SelectMainTab(1);
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneLive, hub.CurrentPane);
            hub.SelectMainTab(0);
            hub.SelectSubTab(2);
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneFA, hub.CurrentPane);
            var dash = NewView<GMLiveLeagueDashboardUIController>(hub.transform.parent.gameObject, "GM06_Dash");
            dash.Build();
            dash.gameObject.SetActive(false);
            hub.DashView = dash;
            // [TASK-GM-07] 라우팅 변경 - [진행하기]는 한 경기를 바로 진행하지 않고 리그 플레이 메인 홈(프런트 오피스 6분할)으로 이동한다.
            Assert.IsTrue(hub.Continue(), "[진행하기] = 메인 홈 이동");
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneOwner, hub.CurrentPane, "메인 홈");
            Assert.AreEqual(GMSeasonPhase.StoveLeague, league.Phase, "경기 전에는 스토브리그 유지");
            Assert.AreEqual(0, sim.GamesPlayed, "바로 경기 진행 안 함");
            Assert.IsFalse(dash.gameObject.activeSelf);
            StringAssert.Contains("2026년 스토브리그", T(hub.Root, "TeamDate"));
        }

        // ================================================================== 2) 스토브리그 협상 4대 기능

        [Test]
        public void T2_StoveNegotiations_Extension_FA_ShopTrade_Draft()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "KIA", seed: 62));
            var hub = NewHub(sim, out _);
            var league = sim.League;
            var team = league.UserTeam;
            var fo = league.FrontOffice;
            hub.SelectMainTab(0);
            hub.SelectSubTab(1);
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneSalaries, hub.CurrentPane);
            var pane = hub.Pane(GMOotpFrontOfficeUIController.PaneSalaries);
            Assert.AreEqual(GMOotpFrontOfficeUIController.TableRows, Enumerable.Range(0, 14).Count(i => pane.Find($"SalRow{i}").gameObject.activeSelf), "28인 중 14인/페이지");
            Click(pane, "SalSort_OVR"); // 기본 OVR 내림차순 → 같은 키 다시 누르면 오름차순
            int ovr0 = int.Parse(pane.Find("SalRow0/C3").GetComponent<Text>().text.Split('/')[0]);
            Assert.AreEqual(team.Roster.Min(p => p.BaseOverall), ovr0, "OVR 정렬(오름차순)");
            Click(pane, "SalSort_AGE");
            Assert.AreEqual(team.Roster.Min(p => p.Age).ToString(), pane.Find("SalRow0/C2").GetComponent<Text>().text, "나이 정렬");

            // ① [TASK-GM-17] 재계약 · 연봉 협상은 계약 협상실에서만 - 연봉 현황 화면은 협상실로 보낸다(요구액 100% 맞추면 되던 우회 계약 제거)
            foreach (var p in team.Roster) { p.HasRoleConcessionBonus = false; p.EgoLevel = Math.Min(p.EgoLevel, p.IsPitcher ? 4 : 3); }
            var star = team.Roster.OrderByDescending(p => p.BaseOverall).First();
            star.ContractYears = 1;
            int salary0 = star.Salary;
            hub.SelectExtension(star);
            Assert.AreSame(star, hub.ExtensionSelected);
            Assert.IsNull(pane.Find("ExtSubmit"), "재계약 실행 버튼 제거");
            Assert.IsNull(pane.Find("ExtSalarySlider"), "연봉 슬라이더 제거");
            var ext = hub.SubmitExtension();
            Assert.IsFalse(ext.Success, "이 화면에서 직접 계약하지 않는다");
            Assert.AreEqual(salary0, star.Salary);
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneNegotiation, hub.CurrentPane, "계약 협상실로 이동");
            Assert.AreSame(star, hub.NegotiationSelected);
            hub.SelectMainTab(0);
            hub.SelectSubTab(1);
            var low = team.Roster.First(p => p != star && p.BaseOverall < 80);
            var lowball = GMStoveLeagueMarket.Extend(league, team, low, 2, Player.MinSalary, false);
            Assert.IsFalse(lowball.Success && GMStoveLeagueMarket.ExtensionDemand(low, fo.Difficulty) > Player.MinSalary * 1.06, "헐값 제시는 거절");

            // 주장 임명
            var captain = team.Roster.First(p => p.RoleArchetype != LockerRoomRole.DugoutLeader && !p.IsCaptain);
            hub.SelectExtension(captain);
            var cap = hub.AppointCaptainSelected();
            Assert.IsTrue(cap.Success);
            Assert.IsTrue(captain.IsCaptain);
            Assert.AreEqual(1, team.Roster.Count(p => p.IsCaptain), "주장 1명");
            Assert.GreaterOrEqual(cap.TeamworkAfter, cap.TeamworkBefore, "주장 = 리더 보정");

            // 방출 → FA 시장
            var cut = team.Roster.OrderBy(p => p.BaseOverall).First(p => !p.IsCaptain && p != star);
            hub.SelectExtension(cut);
            var rel = hub.ReleaseSelected();
            Assert.IsTrue(rel.Success, rel.Message);
            Assert.AreEqual(27, team.Roster.Count);
            Assert.Contains(cut, league.FreeAgents, "방출 = FA 시장");
            Assert.AreEqual(0, cut.ContractYears);

            // ② FA - 스카우팅 리포트 · 입찰
            hub.SelectSubTab(2);
            var faPane = hub.Pane(GMOotpFrontOfficeUIController.PaneFA);
            Assert.GreaterOrEqual(Enumerable.Range(0, GMOotpFrontOfficeUIController.FARows).Count(i => faPane.Find($"FARow{i}").gameObject.activeSelf), 10, "FA 매물 10~15명");
            Click(faPane, "FASort_AGE");
            var market = GMStoveLeagueMarket.FAMarket(league);
            Assert.AreEqual(market.Min(p => p.Age).ToString(), faPane.Find("FARow0/C2").GetComponent<Text>().text, "FA 나이 정렬");
            Click(faPane, "FASort_OVR");
            hub.SelectFARow(0);
            var fa = hub.FASelected;
            Assert.IsNotNull(fa);
            StringAssert.Contains("~", T(faPane, "FARow0/C3"), "스카우팅 전 OVR 범위");
            long b1 = team.Budget;
            Assert.IsTrue(hub.ScoutSelected());
            Assert.IsTrue(fa.IsScouted);
            Assert.AreEqual(b1 - GMStoveLeagueMarket.ScoutCost, team.Budget, "스카우팅 예산 소모");
            StringAssert.Contains("ABS", T(faPane, "FAInfo"), "숨은 성향 · ABS 적응도 공개");
            var cheap = GMStoveLeagueMarket.OfferContract(league, team, fa, 1, Player.MinSalary, false);
            Assert.IsFalse(cheap.Success, "헐값 입찰 = 경쟁 구단에 패배");
            StringAssert.Contains("경쟁 구단", cheap.Message);
            int faDemand = GMStoveLeagueMarket.FADemand(fa, fo.Difficulty);
            hub.SetFATerms(Math.Min(4, GMStoveLeagueMarket.PreferredYears(fa)), faDemand * 3 / 2, true);
            int faSalary = hub.FAOfferSalary;
            int payroll0 = team.Payroll;
            long b2 = team.Budget;
            int faYears = Math.Min(4, GMStoveLeagueMarket.PreferredYears(fa));
            var offer = hub.OfferSelected();
            Assert.IsTrue(offer.Success, offer.Message);
            Assert.Contains(fa, team.Roster, "FA 영입 = 로스터 합류");
            Assert.IsFalse(league.FreeAgents.Contains(fa));
            Assert.AreEqual(payroll0 + faSalary, team.Payroll, "페이롤 증가");
            Assert.AreEqual(b2 - (long)faSalary * faYears * GMStoveLeagueMarket.FABonusPercent / 100, team.Budget, "계약금 예산 차감");
            Assert.AreEqual(1, fo.FASigningsThisYear);

            // ③ Shop a Player - 9개 구단 교환 후보 · 정렬 · 원클릭 체결
            hub.SelectSubTab(3);
            var trPane = hub.Pane(GMOotpFrontOfficeUIController.PaneTrade);
            var target = team.Roster.OrderByDescending(p => p.BaseOverall).Skip(8).First(p => p.InjuryRemainingDays <= 0);
            hub.SetTradeSlots(new[] { target }, "LG", null);
            var offers = hub.ShopSelected();
            Assert.GreaterOrEqual(offers.Count, 5, "타 구단 교환 후보");
            Assert.LessOrEqual(offers.Count, 9);
            Assert.AreEqual(offers.Count, offers.Select(o => o.TeamCode).Distinct().Count(), "구단당 1명");
            Assert.IsTrue(trPane.Find("TrOffer0").gameObject.activeSelf);
            hub.SortShop(GMOfferSort.Ovr); AssertSorted(hub.ShopOffers.Select(o => (double)-o.Player.BaseOverall), "OVR");
            hub.SortShop(GMOfferSort.Potential); AssertSorted(hub.ShopOffers.Select(o => (double)-o.Player.Potential), "잠재력");
            hub.SortShop(GMOfferSort.Salary); AssertSorted(hub.ShopOffers.Select(o => (double)o.Player.Salary), "연봉");
            hub.SortShop(GMOfferSort.Age); AssertSorted(hub.ShopOffers.Select(o => (double)o.Player.Age), "나이");
            hub.SortShop(GMOfferSort.Position); AssertSorted(hub.ShopOffers.Select(o => (double)GMStoveLeagueMarket.PositionOrder(o.Player)), "포지션");
            var pick = hub.ShopOffers[0];
            var partner = league.Teams[pick.TeamCode];
            int mineCount = team.Roster.Count, theirCount = partner.Roster.Count;
            var shop = hub.AcceptShopOffer(0);
            Assert.IsTrue(shop.Success, shop.Message);
            Assert.Contains(pick.Player, team.Roster, "원클릭 체결 - 받은 선수");
            Assert.Contains(target, partner.Roster, "원클릭 체결 - 보낸 선수");
            Assert.AreEqual(mineCount, team.Roster.Count);
            Assert.AreEqual(theirCount, partner.Roster.Count);

            // 1:1 거절 · 2:2 직접 트레이드 + 트레이드 가치 바
            var p2 = league.Teams["SSG"];
            var bad = GMStoveLeagueMarket.Evaluate(league, team, new[] { team.Roster.OrderBy(GMStoveLeagueMarket.TradeValue).First() }, p2, new[] { p2.Roster.OrderByDescending(GMStoveLeagueMarket.TradeValue).First() });
            Assert.IsFalse(bad.Acceptable, "가치 부족 1:1 거절");
            Assert.Less(bad.Ratio, 1f);
            var myTwo = team.Roster.OrderByDescending(GMStoveLeagueMarket.TradeValue).Take(2).ToList();
            var theirTwo = p2.Roster.OrderBy(GMStoveLeagueMarket.TradeValue).Take(2).ToList();
            hub.SetTradeSlots(myTwo, "SSG", theirTwo);
            StringAssert.Contains("%", T(trPane, "TrValueLabel"), "트레이드 가치 바");
            Assert.Greater(trPane.Find("TrValueBar/Fill").GetComponent<RectTransform>().anchorMax.x, 0f);
            var twoTwo = hub.ProposeTrade();
            Assert.IsTrue(twoTwo.Success, twoTwo.Message);
            Assert.IsTrue(theirTwo.All(team.Roster.Contains) && myTwo.All(p2.Roster.Contains), "2:2 로스터 교환");
            Assert.AreEqual(2, fo.TradesThisYear);
            Assert.IsTrue(league.News.Any(n => n.Kind == GMNewsKind.Trade && n.Title.Contains("트레이드")), "트레이드 소식");

            // ④ 신인 드래프트 → 로스터 합류(28인 · 연 2명)
            hub.SelectSubTab(4);
            var drPane = hub.Pane(GMOotpFrontOfficeUIController.PaneDraft);
            Assert.AreEqual(GMStoveLeagueMarket.DraftPoolSize, league.DraftPool.Count, "유망주 풀 10명");
            Assert.AreEqual(10, Enumerable.Range(0, 10).Count(i => drPane.Find($"DrRow{i}").gameObject.activeSelf));
            Assert.AreEqual(28, team.Roster.Count);
            // [TASK-GM-08] 1군 29명(DCL-163) - 퓨처스 1명을 콜업해 가득 채운 뒤 지명 불가를 확인한다.
            Assert.IsTrue(GMRosterTiers.CallUp(league, team, team.Futures[0], out var callUp), callUp);
            Assert.AreEqual(GMStoveLeagueMarket.RosterMax, team.Roster.Count);
            hub.SelectDraftRow(0);
            var full = hub.DraftSelected_();
            Assert.IsFalse(full.Success, "1군 29인 가득 - 지명 불가");
            StringAssert.Contains("방출", full.Message);
            foreach (var p in team.Roster.OrderBy(p => p.BaseOverall).Where(p => !p.IsCaptain).Take(3).ToList()) Assert.IsTrue(GMStoveLeagueMarket.Release(league, team, p).Success);
            hub.SelectDraftRow(0);
            var rookie = hub.DraftSelected;
            var d1 = hub.DraftSelected_();
            Assert.IsTrue(d1.Success, d1.Message);
            Assert.Contains(rookie, team.Roster, "지명 = 로스터 합류");
            Assert.AreEqual(9, league.DraftPool.Count);
            Assert.LessOrEqual(rookie.Age, 22);
            Assert.AreEqual(Player.MinSalary, rookie.Salary);
            Assert.AreEqual(Player.MaxContractYears, rookie.ContractYears);
            Assert.AreEqual(LockerRoomRole.Prospect, rookie.RoleArchetype);
            hub.SelectDraftRow(0);
            Assert.IsTrue(hub.DraftSelected_().Success, "2순위 지명");
            hub.SelectDraftRow(0);
            var third = hub.DraftSelected_();
            Assert.IsFalse(third.Success, "연 2명 지명 한도");
            Assert.AreEqual(2, fo.DraftPicksThisYear);
        }

        private static void AssertSorted(IEnumerable<double> keys, string label)
        {
            var list = keys.ToList();
            for (int i = 1; i < list.Count; i++) Assert.LessOrEqual(list[i - 1], list[i], label + " 정렬");
        }

        // ================================================================== 3) 백분위 · 4단계 난이도 · 하우스 룰

        [Test]
        public void T3_Percentiles_FourDifficulties_HouseRules()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "NC", seed: 63));
            var league = sim.League;
            var team = league.UserTeam;
            var batters = league.AllPlayers.Concat(league.FreeAgents).Concat(league.DraftPool).Where(p => !p.IsPitcher).ToList();
            var best = batters.OrderByDescending(p => p.Template.BatterStats.Contact).First();
            var rows = GMStoveLeagueMarket.Percentiles(league, best);
            Assert.AreEqual(7, rows.Count, "백분위 7항목");
            Assert.IsTrue(rows.All(r => r.Percentile >= 1 && r.Percentile <= 99), "1~99%");
            Assert.AreEqual("타격", rows[0].Label);
            Assert.GreaterOrEqual(rows[0].Percentile, 90, "리그 최고 타격 = 최상위권(동점 포함)");
            Assert.IsTrue(rows.Any(r => r.Label.Contains("ABS")), "선구안(ABS)");
            var worst = batters.OrderBy(p => p.Template.BatterStats.Contact).First();
            Assert.LessOrEqual(GMStoveLeagueMarket.Percentiles(league, worst)[0].Percentile, 10, "리그 최저 타격");
            var pitcher = league.AllPlayers.First(p => p.IsPitcher);
            var prow = GMStoveLeagueMarket.Percentiles(league, pitcher);
            CollectionAssert.Contains(prow.Select(r => r.Label).ToList(), "제구(ABS 보더라인)");

            // 선수 클릭 → 백분위 팝업(슬라이더 바 + 숫자)
            var hub = NewHub(sim, out var canvas);
            hub.SelectMainTab(2);
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneRoster, hub.CurrentPane);
            Click(hub.Pane(GMOotpFrontOfficeUIController.PaneRoster), "RoRow0");
            Assert.IsTrue(hub.IsPercentileOpen, "백분위 팝업");
            var popup = hub.Root.Find("PercentilePopup");
            StringAssert.EndsWith("%", T(popup, "PctValue0"));
            float fill = popup.Find("PctBar0/Fill").GetComponent<RectTransform>().anchorMax.x;
            Assert.AreEqual(int.Parse(T(popup, "PctValue0").TrimEnd('%')) / 100f, fill, 0.011f, "슬라이더 바 = 백분위");
            CheckLayer(popup, "백분위 팝업");
            Click(popup, "PctClose");
            Assert.IsFalse(hub.IsPercentileOpen);

            // 4단계 난이도 - FA 요구액 배수 · 트레이드 요구 · 신임도
            var star = league.AllPlayers.OrderByDescending(p => p.Salary).First();
            var ds = GMFrontOffice.Difficulties;
            Assert.AreEqual(new[] { GMDifficulty.Minors, GMDifficulty.Majors, GMDifficulty.AllStar, GMDifficulty.HallOfFame }, ds);
            for (int i = 1; i < ds.Length; i++)
            {
                Assert.Greater(GMFrontOffice.DemandMultiplier(ds[i]), GMFrontOffice.DemandMultiplier(ds[i - 1]));
                Assert.Greater(GMFrontOffice.TradeMargin(ds[i]), GMFrontOffice.TradeMargin(ds[i - 1]));
                Assert.Less(GMFrontOffice.StartingTrust(ds[i]), GMFrontOffice.StartingTrust(ds[i - 1]));
                Assert.GreaterOrEqual(GMStoveLeagueMarket.FADemand(star, ds[i]), GMStoveLeagueMarket.FADemand(star, ds[i - 1]), "FA 요구액 증가");
            }
            Assert.Greater(GMStoveLeagueMarket.FADemand(team.Roster.OrderBy(p => p.Salary).Skip(14).First(), GMDifficulty.HallOfFame),
                           GMStoveLeagueMarket.FADemand(team.Roster.OrderBy(p => p.Salary).Skip(14).First(), GMDifficulty.Minors), "이지 < 전설");
            Assert.AreEqual("퓨처스(이지)", GMFrontOffice.DifficultyLabel(GMDifficulty.Minors));
            Assert.AreEqual("명예의 전당(전설)", GMFrontOffice.DifficultyLabel(GMDifficulty.HallOfFame));

            // [새 시즌 설정] 모달 - 난이도 · 하우스 룰 → 새 리그 적용
            var dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM06_Dash");
            dash.Build();
            dash.LeagueFactory = (m, t, v) => NewLeague(m, t, 64);
            dash.Bind(sim);
            Click(dash.Root, "NewSeasonButton");
            var modal = dash.Root.Find("SeasonModal");
            Assert.IsTrue(modal.gameObject.activeSelf);
            Click(modal, "Difficulty_HallOfFame");
            Click(modal, "HouseRuleFA"); Click(modal, "HouseRuleFA");   // 제한 없음 → 연 3회 → 연 1회
            Click(modal, "HouseRuleTrade");                               // 제한 없음 → 연 3회
            StringAssert.Contains("연 1회", modal.Find("HouseRuleFA").GetComponentInChildren<Text>().text);
            StringAssert.Contains("명예의 전당", T(modal, "SeasonModalDesc"));
            CheckLayer(modal, "새 시즌 설정(난이도 · 하우스 룰)");
            Click(modal, "SeasonStart");
            var newLeague = dash.Simulator.League;
            Assert.AreNotSame(league, newLeague);
            Assert.AreEqual(GMDifficulty.HallOfFame, newLeague.FrontOffice.Difficulty);
            Assert.AreEqual(1, newLeague.FrontOffice.HouseRuleMaxFA);
            Assert.AreEqual(3, newLeague.FrontOffice.HouseRuleMaxTrades);
            Assert.AreEqual(GMFrontOffice.StartingTrust(GMDifficulty.HallOfFame), newLeague.FrontOffice.OwnerTrust);

            // 하우스 룰 - 연 1회 FA · 트레이드 한도와 [건의] 한도 해제
            var me = newLeague.UserTeam;
            var fo = newLeague.FrontOffice;
            GMStoveLeagueMarket.Release(newLeague, me, me.Roster.OrderBy(p => p.BaseOverall).First());
            GMStoveLeagueMarket.Release(newLeague, me, me.Roster.OrderBy(p => p.BaseOverall).First());
            var fa1 = newLeague.FreeAgents.OrderByDescending(p => p.BaseOverall).First();
            var sign1 = GMStoveLeagueMarket.OfferContract(newLeague, me, fa1, 2, GMStoveLeagueMarket.FADemand(fa1, fo.Difficulty) * 3 / 2, true);
            Assert.IsTrue(sign1.Success, sign1.Message);
            var fa2 = newLeague.FreeAgents.OrderByDescending(p => p.BaseOverall).First();
            var sign2 = GMStoveLeagueMarket.OfferContract(newLeague, me, fa2, 2, GMStoveLeagueMarket.FADemand(fa2, fo.Difficulty) * 3 / 2, true);
            Assert.IsFalse(sign2.Success, "연 1회 FA 한도");
            StringAssert.Contains("하우스 룰", sign2.Message);
            fo.HouseRuleMaxTrades = 1;
            var other = newLeague.Teams["HAN"];
            GMNegotiationResult Trade() => GMStoveLeagueMarket.ExecuteTrade(newLeague, me, new[] { me.Roster.OrderByDescending(GMStoveLeagueMarket.TradeValue).First() }, other, new[] { other.Roster.OrderBy(GMStoveLeagueMarket.TradeValue).First() });
            Assert.IsTrue(Trade().Success, "1번째 트레이드");
            var blocked = Trade();
            Assert.IsFalse(blocked.Success, "연 1회 트레이드 한도");
            StringAssert.Contains("하우스 룰", blocked.Message);
            fo.OwnerTrust = 80;
            var goal = fo.Goals.First(g => GMFrontOffice.CanDiscuss(g));
            Assert.IsTrue(GMFrontOffice.Discuss(newLeague, goal, GMDiscussOption.LiftTradeLimit, out _), "구단주에게 한도 해제 건의");
            Assert.IsTrue(Trade().Success, "한도 해제 후 트레이드");
        }

        // ================================================================== 4) ABS · GM-05 잔여 3건

        [Test]
        public void T4_ABS_ZoneSkill_Engine_BoxScore_And_GM05Residuals()
        {
            var league = NewLeague(team: "SAM", seed: 64);
            foreach (var p in league.AllPlayers.Concat(league.FreeAgents))
                Assert.That(p.ABSZoneSkill, Is.InRange(Player.MinAbsSkill, Player.MaxAbsSkill), "ABSZoneSkill 1~99");
            var catcher = league.UserTeam.Roster.First(p => p.Position == "C");
            Assert.AreEqual("블로킹·도루저지", catcher.AbsRoleLabel);
            Assert.AreEqual("보더라인 공략", league.UserTeam.Pitchers.First().AbsRoleLabel);
            int baseSkill = catcher.ABSZoneSkill;
            catcher.AbsTrainingBonus = 5;
            Assert.AreEqual(Math.Min(99, baseSkill + 5), catcher.ABSZoneSkill, "훈련 보정 반영");
            catcher.AbsTrainingBonus = 0;

            // 엔진 - 같은 타석을 투수 ABS 상/하로 반복: 삼진 ↑ · 볼넷 ↓, 타자 ABS 상/하: 볼넷 ↑
            var sim = new GMLiveSeasonSimulator(league);
            var home = league.Teams["SAM"]; var away = league.Teams["LG"];
            var batter = away.Batters.OrderBy(p => p.BaseOverall).Skip(5).First();
            var pitcher = home.Pitchers.OrderBy(p => p.BaseOverall).Skip(5).First();
            (int k, int bb) Sample(int pitcherBonus, int batterBonus)
            {
                pitcher.AbsTrainingBonus = pitcherBonus; batter.AbsTrainingBonus = batterBonus;
                var engine = new MatchEngine(home.AvailableRoster, away.AvailableRoster, sim.ModifiersFor(home, true), sim.ModifiersFor(away, false), null, null, 777);
                int k = 0, bb = 0;
                for (int i = 0; i < 6000; i++)
                {
                    var r = engine.SimulateAtBat(batter, pitcher, new MatchState());
                    if (r == AtBatResult.Strikeout) k++;
                    else if (r == AtBatResult.Walk) bb++;
                }
                return (k, bb);
            }
            var pitchHigh = Sample(60, 0);
            var pitchLow = Sample(-60, 0);
            var batHigh = Sample(0, 60);
            var batLow = Sample(0, -60);
            pitcher.AbsTrainingBonus = batter.AbsTrainingBonus = 0;
            TestContext.WriteLine($"[GM06 ABS] 투수 ABS 상 K {pitchHigh.k} BB {pitchHigh.bb} / 하 K {pitchLow.k} BB {pitchLow.bb} · 타자 ABS 상 BB {batHigh.bb} / 하 BB {batLow.bb}");
            Assert.Greater(pitchHigh.k, pitchLow.k * 1.1, "투수 ABS 보더라인 공략 → 삼진 증가");
            Assert.Less(pitchHigh.bb, pitchLow.bb, "투수 ABS → 볼넷 감소");
            Assert.Greater(batHigh.bb, batLow.bb * 1.15, "타자 ABS 선구안 → 볼넷 출루 증가");
            Assert.Less(batHigh.k, batLow.k, "타자 ABS → 삼진 감소");

            // 시즌 기록 - 내 구단 투수진 ABS 보정 → 보더라인 콜 · 루킹 삼진 증가, 박스스코어 ABS 표시
            var absLeague = NewLeague(team: "KT", seed: 65);
            foreach (var p in absLeague.UserTeam.Pitchers) p.AbsTrainingBonus = 60;
            var absSim = new GMLiveSeasonSimulator(absLeague) { RecordAllBoxScores = true };
            RunDays(absSim, 24);
            var mine = absLeague.RecordOf("KT");
            var others = absLeague.Teams.Keys.Where(c => c != "KT").Select(absLeague.RecordOf).ToList();
            double mineRate = mine.AbsBorderlineCalls / (double)Math.Max(1, mine.G), otherRate = others.Sum(r => r.AbsBorderlineCalls) / (double)Math.Max(1, others.Sum(r => r.G));
            TestContext.WriteLine($"[GM06 ABS] 보더라인 콜/경기 - KT(투수 ABS +60) {mineRate:0.0} · 나머지 {otherRate:0.0}");
            Assert.Greater(mineRate, otherRate * 1.3, "ABS 보더라인 콜 통계 반영");
            Assert.Greater(others.Sum(r => r.AbsBlockSaves) + mine.AbsBlockSaves, 0, "포수 블로킹 세이브 집계");
            var box = absSim.AllUserBoxScores.Last();
            Assert.That(box.HomeAbsIndex, Is.InRange(1, 99));
            Assert.That(box.AwayAbsIndex, Is.InRange(1, 99));
            Assert.Greater(box.HomeAbsCalls + box.AwayAbsCalls, 0, "박스스코어 보더라인 콜");
            var canvas = NewCanvas();
            var prePost = NewView<GMMatchPrePostUIController>(canvas, "GM06_PrePost");
            prePost.Build();
            prePost.Attach(absSim);
            prePost.ShowPostGameBoxScoreView(box);
            StringAssert.Contains("보더라인 콜", T(prePost.PostRoot, "AbsAway"));
            StringAssert.Contains("블로킹 세이브", T(prePost.PostRoot, "AbsHome"));
            var hub = NewView<GMOotpFrontOfficeUIController>(canvas, "GM06_Hub");
            hub.Build();
            hub.Bind(absSim);
            hub.SelectMainTab(3);
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneAbs, hub.CurrentPane);
            var absPane = hub.Pane(GMOotpFrontOfficeUIController.PaneAbs);
            StringAssert.Contains("보더라인 콜", T(absPane, "AbLast"), "[전략·ABS 분석] 직전 경기 ABS");
            Assert.IsTrue(Enumerable.Range(0, 10).Any(r => T(absPane, $"AbCell{r}_0").Contains("★")), "10구단 비교표 내 구단");

            // D.1 기본 실책률 2.2% - 시즌 실책 스케일
            Assert.AreEqual(0.048f, GMChemistryModifiers.DefaultBaseErrorRate, 1e-6f, "D.1 기본 실책률([TASK-GM-07] 0.022 → 0.048)");
            var full = SharedSeason().League;
            var perTeam = full.Teams.Keys.Select(c => full.Stats.Values.Where(s => s.TeamCode == c).Sum(s => s.Errors)).ToList();
            TestContext.WriteLine($"[GM06 D.1] 144경기 팀 실책 평균 {perTeam.Average():0.0} (최소 {perTeam.Min()} · 최대 {perTeam.Max()})");
            Debug.Log($"[GM06 D.1] 144경기 팀 실책 평균 {perTeam.Average():0.0} (최소 {perTeam.Min()} · 최대 {perTeam.Max()})");
            Assert.That(perTeam.Average(), Is.InRange(40.0, 150.0), "팀당 시즌 실책([TASK-GM-07] 4.8% - 정밀 범위 75~110은 GM07 검증)");

            // D.2 내 구단 자동 로테이션 기본 ON - 한 시즌 고속 진행에도 체력 고갈 엔트리 없음 · 수동 끄기 가능
            Assert.IsTrue(GMCheerleaderRoster.AutoRotateUserCheerleaders);
            var rot = NewLeague(team: "LG", seed: 66);
            Assert.IsTrue(rot.UserTeam.CheerAutoRotate && rot.UserTeam.AutoRotateUserCheerleaders, "D.2 기본 true");
            var rotSim = new GMLiveSeasonSimulator(rot);
            RunDays(rotSim, 12);
            Assert.IsFalse(rot.UserTeam.CheerEntry.Any(GMCheerleaderStats.IsTired), "12경기 연속 진행 - 지친 인원 자동 교체");
            var cheerView = NewView<GMCheerleaderEntryUIController>(canvas, "GM06_Cheer");
            cheerView.Build();
            cheerView.Open(rot.UserTeam);
            StringAssert.Contains("켜짐", cheerView.Root.Find("AutoRotateButton").GetComponentInChildren<Text>().text);
            Click(cheerView.Root, "AutoRotateButton");
            Assert.IsFalse(rot.UserTeam.CheerAutoRotate, "수동 엔트리 관리로 전환 가능");

            // D.3 전담 응원 대상 직접 지정(클릭 순환)
            var team = NewLeague(team: "KIA", seed: 67).UserTeam;
            foreach (var p in team.Roster) { p.HasRoleConcessionBonus = false; p.EgoLevel = Math.Min(p.EgoLevel, p.IsPitcher ? 4 : 3); }
            foreach (var p in team.Roster.Where(x => !x.IsPitcher).Take(6)) p.EgoLevel = 4;
            var candidates = GMCheerleaderRoster.DedicationCandidates(team);
            Assert.GreaterOrEqual(candidates.Count, 2);
            cheerView.Open(team);
            cheerView.Select(team.CheerEntry.First()); // 1번 응원단장(리더)
            Assert.IsTrue(cheerView.Root.Find("DedicationTargetButton").GetComponent<Button>().interactable, "리더 = 지정 가능");
            Click(cheerView.Root, "DedicationTargetButton");
            var first = GMCheerleaderRoster.DedicatedPlayerOf(team, team.CheerEntry.First());
            Assert.IsNotNull(first, "직접 지정");
            Assert.IsTrue(first.HasRoleConcessionBonus);
            StringAssert.Contains(first.Template.PlayerName, cheerView.Root.Find("DedicationTargetButton").GetComponentInChildren<Text>().text);
            Click(cheerView.Root, "DedicationTargetButton");
            var second = GMCheerleaderRoster.DedicatedPlayerOf(team, team.CheerEntry.First());
            Assert.IsNotNull(second);
            Assert.AreNotSame(first, second, "클릭 = 다음 Ego 4+ 스타로 변경");
            Assert.IsFalse(first.HasRoleConcessionBonus, "이전 대상 효과 회수");
            Assert.AreEqual(1, team.CheerDedications.Count, "치어리더 1명 = 매칭 1쌍");
            Assert.IsTrue(GMCheerleaderRoster.TryReassignDedication(team, team.CheerEntry.First(), first, out var why), why);
            Assert.AreSame(first, GMCheerleaderRoster.DedicatedPlayerOf(team, team.CheerEntry.First()), "특정 선수 직접 지정");
            CheckLayer(cheerView.Root, "치어리더 화면(전담 대상 버튼)");
        }

        // ================================================================== 5) 스토리 안건 · 4종 엔딩

        [Test]
        public void T5_StoryAgendas_Choices_FourEndings_SeasonRollover()
        {
            var story = NewLeague(GMStartMode.StoryCampaign, "HAN", 68);
            var fo = story.FrontOffice;
            var team = story.UserTeam;
            Assert.AreEqual(GMFrontOffice.AgendaDefs.Length, GMFrontOffice.OpenAgendaStates(story).Count, "스토리 안건 10종");
            Assert.That(GMFrontOffice.AgendaDefs.Length, Is.InRange(8, 10));
            Assert.AreEqual(5, GMFrontOffice.OpenAgendaStates(NewLeague(team: "SAM", seed: 69)).Count, "일반 모드 공통 안건");
            Assert.AreEqual(0, fo.Owner.Patience, "스토리 - 참을성 없는 구단주");

            long budget0 = team.Budget; int trust0 = fo.OwnerTrust;
            Assert.IsTrue(GMFrontOffice.ResolveAgenda(story, "A_FA_OVERPAY", 0, out var m1), m1);
            Assert.AreEqual(budget0 + 100000, team.Budget, "추경 → 예산");
            Assert.AreEqual(trust0 - 10, fo.OwnerTrust, "추경 → 신임도");
            int tw0 = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff).TeamworkScore;
            Assert.IsTrue(GMFrontOffice.ResolveAgenda(story, "A_FACTION", 0, out _));
            int tw1 = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff).TeamworkScore;
            Assert.AreEqual(Math.Min(100, tw0 + 8), tw1, "파벌 중재 → 팀워크 +8");
            int fan0 = team.FanSupport;
            Assert.IsTrue(GMFrontOffice.ResolveAgenda(story, "A_CHEER_INVEST", 0, out _));
            Assert.AreEqual(Math.Min(100, fan0 + 6), team.FanSupport, "응원단 투자 → 팬 지지율");
            Assert.IsFalse(GMFrontOffice.ResolveAgenda(story, "A_CHEER_INVEST", 0, out _), "이미 결단한 안건");
            var requester = team.Roster.First(p => p.Template.RealPlayerId == team.TradeRequestPlayerId);

            // UI - [스토리 안건] 선택지 버튼
            var sim = new GMLiveSeasonSimulator(story);
            var hub = NewHub(sim, out _);
            hub.SelectMainTab(0);
            hub.SelectSubTab(5);
            var pane = hub.Pane(GMOotpFrontOfficeUIController.PaneStory);
            StringAssert.Contains("트레이드 요구", T(pane, "AgTitle0"), "간판 4번 타자의 트레이드 요구");
            CheckLayer(pane, "스토리 안건");
            Click(pane, "AgOpt0_0"); // 설득 + 주장 완장
            Assert.IsTrue(requester.IsCaptain, "노장 4번 타자 주장 임명");
            Assert.IsNull(team.TradeRequestPlayerId, "트레이드 요구 철회");
            StringAssert.Contains("단장 결단", story.News.First().Title);
            StringAssert.Contains("엔딩 전망", T(pane, "StInd5"));

            // 4종 엔딩 판정(순수)
            Assert.AreEqual(GMEnding.MiracleAutumn, GMFrontOffice.EvaluateEnding(true, 1000, 60, false, 30));
            Assert.AreEqual(GMEnding.DeficitFired, GMFrontOffice.EvaluateEnding(true, -1, 30, false, 30), "적자 · 신임 하락 = 해임 우선");
            Assert.AreEqual(GMEnding.DeficitFired, GMFrontOffice.EvaluateEnding(false, -500, 45, true, 26));
            Assert.AreEqual(GMEnding.SuccessfulRebuild, GMFrontOffice.EvaluateEnding(false, 1000, 50, true, 31));
            Assert.AreEqual(GMEnding.SuccessfulRebuild, GMFrontOffice.EvaluateEnding(false, 1000, 40, false, 26.5));
            Assert.AreEqual(GMEnding.PerformanceFired, GMFrontOffice.EvaluateEnding(false, 1000, 20, false, 30));
            Assert.AreEqual(GMEnding.PerformanceFired, GMFrontOffice.EvaluateEnding(false, 1000, 60, false, 30));

            // 시즌 결산 - 3위(가을야구) → 기적의 가을야구 · 이력 · 신임도 상승
            var codes = NameAliasTable.CanonicalTeamCodes.ToList();
            List<string> RankWith(string code, int rank) { var l = codes.Where(c => c != code).ToList(); l.Insert(rank - 1, code); return l; }
            int trustBefore = fo.OwnerTrust;
            GMFrontOffice.OnSeasonCompleted(story, RankWith("HAN", 3), "LG");
            Assert.AreEqual(GMEnding.MiracleAutumn, fo.Ending, "기적의 가을야구");
            Assert.Greater(fo.OwnerTrust, trustBefore, "가을야구 → 신임도 상승");
            Assert.AreEqual(10, fo.History.Count(h => h.Year == story.SeasonYear), "10구단 시즌 이력");
            StringAssert.Contains("기적의 가을야구", story.News.First().Title);

            var deficit = NewLeague(GMStartMode.StoryCampaign, "HAN", 70);
            deficit.UserTeam.Budget = -10000;
            GMFrontOffice.OnSeasonCompleted(deficit, RankWith("HAN", 7), "LG");
            Assert.AreEqual(GMEnding.DeficitFired, deficit.FrontOffice.Ending, "적자 해임");

            var rebuild = NewLeague(GMStartMode.StoryCampaign, "HAN", 71);
            rebuild.FrontOffice.OwnerTrust = 45;
            GMFrontOffice.OnSeasonCompleted(rebuild, RankWith("HAN", 7), "LG");
            Assert.AreEqual(GMEnding.SuccessfulRebuild, rebuild.FrontOffice.Ending, "최하위 → 7위 상승 = 성공적 리빌딩");

            var fired = NewLeague(GMStartMode.StoryCampaign, "HAN", 72);
            fired.FrontOffice.OwnerTrust = 20;
            GMFrontOffice.OnSeasonCompleted(fired, RankWith("HAN", 10), "LG");
            Assert.AreEqual(GMEnding.PerformanceFired, fired.FrontOffice.Ending, "성적 부진 해임");

            // 실제 시즌 전환 연동(완주 시즌 → 2027) - 이력 · 연간 카운터 초기화 · 목표 재생성
            var full = SharedSeason();
            var league = full.League;
            league.FrontOffice.FASigningsThisYear = 2;
            int year = league.SeasonYear;
            Assert.IsTrue(GMAwardEvaluator.AdvanceToNextSeasonYear(full));
            Assert.AreEqual(year + 1, league.SeasonYear);
            Assert.AreEqual(10, league.FrontOffice.History.Count(h => h.Year == year), "결산 이력 10구단");
            Assert.AreEqual(1, league.FrontOffice.History.Count(h => h.Year == year && h.Champion), "한국시리즈 우승 1팀");
            Assert.AreEqual(0, league.FrontOffice.FASigningsThisYear, "연간 카운터 초기화");
            Assert.IsTrue(league.FrontOffice.Goals.Any(g => g.YearIssued == year + 1), "새 시즌 목표");
            Assert.AreEqual(GMEnding.None, league.FrontOffice.Ending, "일반 모드는 엔딩 없음");
            season = null; // 공유 시즌을 다음 해로 넘겼으므로 T6는 새로 완주한다

            // 세이브 v17 왕복
            var data = SaveManager.ToGMSaveData(story);
            var json = JsonUtility.ToJson(data);
            var back = JsonUtility.FromJson<GMLeagueSaveData>(json);
            Assert.AreEqual(story.FrontOffice.OwnerTrust, back.FrontOffice.OwnerTrust);
            Assert.AreEqual(story.FrontOffice.Ending, back.FrontOffice.Ending);
            Assert.AreEqual(story.FrontOffice.Agendas.Count(a => a.Resolved), back.FrontOffice.Agendas.Count(a => a.Resolved));
            Assert.AreEqual(story.DraftPool.Count, back.DraftPool.Count);
            Assert.AreEqual(story.FrontOffice.History.Count, back.FrontOffice.History.Count);
        }

        // ================================================================== 6) 1920×1080 전 화면 UI 무결성 · 씬

        [Test]
        public void T6_UiIntegrity_Landscape_AllScreens_NoOverlap_Min15pt_NoBold_Scene()
        {
            var full = SharedSeason();
            var fresh = new GMLiveSeasonSimulator(NewLeague(GMStartMode.StoryCampaign, "SSG", 73));
            var hub = NewHub(fresh, out var canvas);
            CheckLayer(hub.Root, "허브 헤더 · 탭");
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
            hub.Bind(full); // 시즌 종료 상태(실시간 · ABS · 역사)
            foreach (int t in new[] { 0, 1, 2, 3, 6, 7 }) { hub.SelectMainTab(t); CheckPane(hub, $"시즌 종료 탭 {t}"); }
            hub.SelectMainTab(0);
            hub.SelectSubTab(1);
            hub.SelectSalaryRow(0);
            CheckPane(hub, "재계약 선택");
            hub.SelectSubTab(2);
            hub.SelectFARow(0);
            hub.ScoutSelected();
            CheckPane(hub, "FA 스카우팅");
            hub.SelectSubTab(3);
            hub.SetTradeSlots(full.League.UserTeam.Roster.Take(2), "KIA", full.League.Teams["KIA"].Roster.Take(2));
            hub.ShopSelected();
            CheckPane(hub, "트레이드 · Shop");
            hub.SelectSubTab(4);
            hub.SelectDraftRow(1);
            CheckPane(hub, "드래프트 백분위");
            hub.OpenDiscuss(0);
            CheckLayer(hub.Root.Find("DiscussPopup"), "건의 팝업");

            var dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM06_Dash");
            dash.Build();
            dash.Bind(fresh);
            CheckLayer(dash.Root, "대시보드(시즌 전)");
            dash.Bind(full);
            CheckLayer(dash.Root, "대시보드(시즌 종료)");
            dash.OpenSeasonModal();
            CheckLayer(dash.Root.Find("SeasonModal"), "새 시즌 모달");
            var prePost = NewView<GMMatchPrePostUIController>(canvas, "GM06_PrePost");
            prePost.Build();
            Assert.IsTrue(prePost.ShowPreGameView(fresh));
            CheckLayer(prePost.PreRoot, "경기 전 전력 비교");
            prePost.Attach(full);
            prePost.ShowPostGameBoxScoreView(full.League.LastUserMatchBoxScore);
            foreach (bool home in new[] { false, true })
                foreach (bool pitching in new[] { false, true })
                {
                    prePost.SelectTab(home, pitching);
                    CheckLayer(prePost.PostRoot, $"경기 후 박스스코어 {home}/{pitching}");
                }
            var awards = NewView<GMAwardsCeremonyUIController>(canvas, "GM06_Awards");
            awards.Build();
            awards.Open(full);
            foreach (GMAwardsTab tab in Enum.GetValues(typeof(GMAwardsTab)))
            {
                awards.SelectTab(tab);
                CheckLayer(awards.Root, $"시상식 {tab}");
            }
            var cheerView = NewView<GMCheerleaderEntryUIController>(canvas, "GM06_Cheer");
            cheerView.Build();
            cheerView.Open(full.League.UserTeam);
            CheckLayer(cheerView.Root, "치어리더 15인 · 엔트리");

            // 씬 - 1920×1080 · 허브 · 단장 모드 화면 전체 화면 · 기존 세로 화면 9:16 프레임 · Bold 0
            OpenScene();
            var scaler = SetupTaskGM06.MainScaler();
            Assert.IsNotNull(scaler);
            Assert.AreEqual(new Vector2(1920, 1080), scaler.referenceResolution, "1920×1080 Landscape");
            Assert.AreEqual(1920, PlayerSettings.defaultScreenWidth);
            Assert.AreEqual(1080, PlayerSettings.defaultScreenHeight);
            Assert.AreEqual(UIOrientation.LandscapeLeft, PlayerSettings.defaultInterfaceOrientation);
            var sceneHub = Object.FindAnyObjectByType<GMOotpFrontOfficeUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(sceneHub, "씬에 프런트 오피스 허브");
            Assert.IsNotNull(sceneHub.transform.Find(GMOotpFrontOfficeUIController.RootName));
            Assert.IsFalse(sceneHub.gameObject.activeSelf, "로비 진입 시 코드로 연다");
            CheckLayer(sceneHub.transform.Find(GMOotpFrontOfficeUIController.RootName), "씬 허브");
            int landscape = 0, framed = 0;
            foreach (Transform child in scaler.transform)
            {
                var rect = (RectTransform)child;
                if (SetupTaskGM06.IsLandscapeView(child))
                {
                    landscape++;
                    Assert.AreEqual(Vector2.zero, rect.anchorMin, child.name);
                    Assert.AreEqual(Vector2.one, rect.anchorMax, child.name);
                    Assert.IsNull(child.GetComponent<LegacyPortraitFrame>(), child.name);
                }
                else if (child.GetComponent<LegacyPortraitFrame>() != null)
                {
                    framed++;
                    Assert.AreEqual(new Vector2(LegacyPortraitFrame.FrameWidth, LegacyPortraitFrame.FrameHeight), rect.sizeDelta, child.name + " 9:16");
                    Assert.AreEqual(LegacyPortraitFrame.DefaultScale, rect.localScale.x, 1e-4f, child.name);
                }
            }
            Assert.AreEqual(5, landscape, "단장 모드 가로 화면 5개(허브 · 대시보드 · 전/후 · 시상식 · 치어리더)");
            Assert.Greater(framed, 0, "기존 세로 화면 9:16 프레임");
            Assert.IsNotNull(Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include), "기존 치어리더 관리 화면 보존");
            Assert.IsNotNull(Object.FindAnyObjectByType<CheerleaderShopUIController>(FindObjectsInactive.Include), "치어리더 영입 보존");
            var bold = SetupTask191.SceneTexts().Where(x => x.fontStyle == FontStyle.Bold || x.fontStyle == FontStyle.BoldAndItalic).Select(x => x.name).ToList();
            Assert.IsEmpty(bold, "씬 전체 FontStyle.Bold 0건");
        }

        /// <summary>허브 루트 + 현재 화면(Pane) + 화면 안 패널 컨테이너 + 표 행 셀 겹침 0 · 15pt+ · Normal.</summary>
        private static void CheckPane(GMOotpFrontOfficeUIController hub, string label)
        {
            CheckLayer(hub.Root, label + " 헤더");
            var pane = hub.Pane(hub.CurrentPane);
            Assert.IsTrue(pane.gameObject.activeSelf, label);
            CheckLayer(pane, label + " " + hub.CurrentPane);
            foreach (Transform child in pane)
            {
                if (!child.gameObject.activeSelf) continue;
                if (child.name.EndsWith("Panel") && child.GetComponent<Text>() == null && child.GetComponent<Button>() == null) CheckLayer(child, label + " " + child.name);
                if (child.GetComponent<Button>() != null && child.Find("C0") != null) CheckLayer(child, label + " " + child.name + " 셀");
            }
        }

        /// <summary>layer 직속 자식 중 활성 텍스트/버튼 영역 겹침 0 · 15pt 이상(지시서 14pt 이상) · Normal.</summary>
        private static void CheckLayer(Transform layer, string label)
        {
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
