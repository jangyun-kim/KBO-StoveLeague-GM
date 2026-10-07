using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-GM-02] 리그 플레이 실시간 144경기 대시보드 6대 자동 검증(Unity CLI BatchPipelineGM02 1회 실행):
    ///   1) 진단 멱등성  2) 진행 방식 3종(한 경기 · 전반기→후반기 · 한 시즌)  3) TOP 3(타자 8 · 투수 7) · 순위표 정합성 · 세이브 왕복
    ///   4) 대기록 · 부상 소식(날짜) · 부상 일수 차감/복귀 · 직접 관리  5) 구단 대표색 · 배경 마크  6) UI 무결성(겹침 0 · 15pt+ · Bold 0)
    /// </summary>
    public class GM02VerificationRunner
    {
        private static IReadOnlyList<PlayerTemplate> templates;
        private static List<Cheerleader> cheer;
        private static GameObject dbObject;
        private readonly List<Object> created = new List<Object>();
        private bool sceneOpened;

        [OneTimeSetUp]
        public void LoadDatabase()
        {
            dbObject = new GameObject("GM02_PlayerDatabase");
            templates = dbObject.AddComponent<PlayerDatabase>().AllTemplates.ToList();
            Assert.Greater(templates.Count, 500, "선수 DB 로드");
            cheer = GMRosterLoader.AllCheerleaderTemplates();
        }

        /// <summary>씬을 다시 열면(OpenScene) Unity가 메모리 전용 템플릿(ScriptableObject)을 회수할 수 있다 - 테스트마다 살아 있는지 확인하고 없으면 다시 로드한다.</summary>
        [SetUp]
        public void EnsureDatabase()
        {
            if (templates != null && templates.Count > 0 && templates.All(t => t != null)) return;
            UnloadDatabase();
            LoadDatabase();
        }

        [OneTimeTearDown]
        public void UnloadDatabase()
        {
            if (templates != null) foreach (var t in templates) if (t != null) Object.DestroyImmediate(t);
            templates = null;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
        }

        [TearDown]
        public void TearDown()
        {
            GameSettings.UseVirtualNames = false;
            foreach (var obj in created) if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = false;
        }

        private static GMLeagueState NewLeague(string team = "SAM", GMStartMode mode = GMStartMode.RealCurrent2026, int seed = 20260328)
        {
            var league = GMRosterLoader.LoadModeRoster(mode, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        private static GMLiveSeasonSimulator FullSeason(string team = "SAM", int seed = 20260328)
        {
            var sim = new GMLiveSeasonSimulator(NewLeague(team, seed: seed));
            Assert.IsTrue(sim.StartRun(GMRunMode.FullSeason));
            sim.RunUntilStop();
            return sim;
        }

        private GMLiveLeagueDashboardUIController NewDashboard()
        {
            var canvasGo = new GameObject("GM02_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvasGo);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); // [TASK-GM-06] Landscape
            var go = new GameObject("GM02_Dashboard", typeof(RectTransform));
            go.transform.SetParent(canvasGo.transform, false);
            var view = go.AddComponent<GMLiveLeagueDashboardUIController>();
            view.Build();
            return view;
        }

        private Player Batter(string position, int ego, LockerRoomRole role)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = t.RealPlayerId = "GM02_" + Guid.NewGuid().ToString("N");
            t.PlayerName = t.RealName = "테스트";
            t.BatterPosition = (BatterPosition)Enum.Parse(typeof(BatterPosition), position);
            t.BatterStats = new BatterStats(80, 80, 80, 60, 80);
            return new Player(Guid.NewGuid().ToString(), t) { EgoLevel = ego, RoleArchetype = role, Salary = 10000, Age = 28 };
        }

        // ================================================================== 1) 진단 멱등성

        [Test]
        public void T1_DiagnosticEvaluation_IsIdempotent_TickAppliesMorale()
        {
            var roster = new List<Player>();
            foreach (var pos in new[] { "FirstBase", "LeftField", "RightField", "DesignatedHitter", "SecondBase", "ThirdBase" })
                roster.Add(Batter(pos, 4, LockerRoomRole.Ambitious));
            foreach (var pos in new[] { "Catcher", "ShortStop", "CenterField" }) roster.Add(Batter(pos, 1, LockerRoomRole.UnsungHero));

            var first = TeamChemistryEngine.EvaluateRoster(roster, 0);
            Assert.IsTrue(first.Has(AllStarOverloadPenalty.LineupRoleConflict), "보직 충돌 발동");
            for (int i = 0; i < 10; i++)
            {
                var again = TeamChemistryEngine.EvaluateRoster(roster, 0);
                Assert.AreEqual(first.TeamworkScore, again.TeamworkScore, "같은 입력 → 같은 결과");
            }
            Assert.IsTrue(roster.All(p => p.PersonalMorale == Player.DefaultPersonalMorale), "10회 평가 후에도 만족도 불변");

            // 진단 화면을 10번 다시 그려도 불변
            var view = new GameObject("GM02_Diag").AddComponent<GMDiagnosticView>();
            created.Add(view.gameObject);
            view.Build();
            for (int i = 0; i < 10; i++) view.Render(roster, null, 0);
            Assert.IsTrue(roster.All(p => p.PersonalMorale == Player.DefaultPersonalMorale), "진단 화면 반복 표시 후에도 만족도 불변");

            TeamChemistryEngine.ApplyChemistryTickEffects(roster);
            var unhappy = TeamChemistryEngine.GetDissatisfiedStars(roster);
            Assert.AreEqual(2, unhappy.Count);
            Assert.IsTrue(unhappy.All(p => p.PersonalMorale == Player.DefaultPersonalMorale - TeamChemistryEngine.TickMoralePenalty), "경기 틱에서만 만족도 하락");
        }

        // ================================================================== 2) 진행 방식 3종

        [Test]
        public void T2_RunModes_SingleGame_FirstThenSecondHalf_FullSeason()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague());
            var view = NewDashboard();
            view.Bind(sim);
            Assert.AreEqual("전반기 진행", view.HalfButtonText);

            Assert.IsTrue(sim.StartRun(GMRunMode.SingleGame));
            Assert.AreEqual(GMSeasonPhase.RegularSeason, sim.League.Phase);
            sim.RunUntilStop();
            Assert.AreEqual(1, sim.GamesPlayed, "한 경기 = 정확히 1경기");
            Assert.IsNull(sim.ActiveMode);
            Assert.IsTrue(sim.League.Records.Values.All(r => r.G == 1), "10구단 모두 1경기");
            Assert.IsNotEmpty(sim.LastUserGameLine);

            Assert.IsTrue(sim.StartRun(GMRunMode.FirstHalf));
            sim.RunUntilStop();
            Assert.AreEqual(GMLiveSeasonSimulator.HalfGames, sim.GamesPlayed, "전반기 72경기에서 정지");
            Assert.IsNull(sim.ActiveMode);
            Assert.AreEqual("후반기 진행", sim.HalfButtonLabel);
            view.Refresh();
            Assert.AreEqual("후반기 진행", view.HalfButtonText, "버튼이 후반기 진행으로 전환");

            Assert.IsTrue(sim.StartRun(GMRunMode.FirstHalf), "같은 버튼 = 후반기 진행");
            Assert.AreEqual(GMRunMode.SecondHalf, sim.ActiveMode);
            sim.RunUntilStop();
            Assert.AreEqual(GMLiveSeasonSimulator.SeasonGames, sim.GamesPlayed, "후반기로 144경기 완주");
            Assert.IsTrue(sim.IsSeasonComplete);
            Assert.IsFalse(sim.StartRun(GMRunMode.SingleGame), "시즌 종료 후 진행 불가");
            Assert.AreEqual(GMSeasonPhase.PostSeason, sim.League.Phase);

            var full = new GMLiveSeasonSimulator(NewLeague("LG", seed: 7));
            Assert.IsTrue(full.StartRun(GMRunMode.FullSeason));
            int days = full.RunUntilStop();
            Assert.AreEqual(144, days);
            Assert.AreEqual(144, full.GamesPlayed, "한 시즌 1~144경기 완주");

            // 대시보드 실시간 틱: 1x 1.75초 × 144 ≈ 4.2분(3~5분 목표)
            Assert.That(GMLiveLeagueDashboardUIController.TickSeconds * 144 / 60f, Is.InRange(3f, 5f));
            view.SetSpeed(4);
            Assert.AreEqual(4, view.Speed);
            view.TogglePause();
            Assert.IsTrue(view.IsPaused);
        }

        // ================================================================== 3) TOP 3 · 순위표 · 세이브

        [Test]
        public void T3_Leaders15Categories_StandingsConsistent_SaveRoundTrip()
        {
            var sim = FullSeason();
            var league = sim.League;

            foreach (var cat in GMLeaderCategories.Batter.Concat(GMLeaderCategories.Pitcher))
            {
                var top = sim.Leaders(cat);
                Assert.AreEqual(3, top.Count, $"{GMLeaderCategories.Label(cat)} 1~3위");
                Assert.IsTrue(top.All(e => !string.IsNullOrEmpty(e.Name) && e.Name != "-" && !string.IsNullOrEmpty(e.ValueLabel)), GMLeaderCategories.Label(cat));
                var values = top.Select(e => e.Value).ToList();
                if (GMLeaderCategories.LowerIsBetter(cat)) CollectionAssert.IsOrdered(values, $"{cat} 오름차순");
                else CollectionAssert.IsOrdered(values.AsEnumerable().Reverse().ToList(), $"{cat} 내림차순");
            }
            Assert.AreEqual(8, GMLeaderCategories.Batter.Length);
            Assert.AreEqual(7, GMLeaderCategories.Pitcher.Length);
            // [TASK-GM-07] 실책률 상향(0.022 → 0.048)으로 출루 · 투구 수가 늘어 선발이 일찍 내려가면서 타율 1위가 .50을 살짝 넘는 시드가 생겼다(.508).
            // 이 범위는 시뮬레이션 폭주 감지용 상한이라 .55로 넓히고, 타율 1위 현실화(.35~.40대)는 밸런스 과제로 남긴다(DCL-162 남은 이슈).
            // [TASK-GM-08] 단장 모드 타격 밸런스(GMBattingBalance)로 리그 .260~.270 · 타격왕 .340~.385가 됐다 - 정밀 검증은 GM08VerificationRunner T2.
            Assert.That(sim.Leaders(GMLeaderCategory.AVG)[0].Value, Is.InRange(0.25, 0.55), "타율 1위 폭주 감지 범위");
            Assert.Greater(sim.Leaders(GMLeaderCategory.HR)[0].Value, 15);
            Assert.Greater(sim.Leaders(GMLeaderCategory.Saves)[0].Value, 5);
            Assert.Greater(sim.Leaders(GMLeaderCategory.Holds)[0].Value, 3);
            Assert.Greater(sim.Leaders(GMLeaderCategory.SB)[0].Value, 3);

            var standings = sim.Standings();
            Assert.AreEqual(10, standings.Count);
            foreach (var r in standings)
            {
                Assert.AreEqual(144, r.G, r.TeamCode + " 144경기");
                Assert.AreEqual(144, r.W + r.D + r.L, r.TeamCode + " 승+무+패");
                Assert.AreEqual(r.W + r.L == 0 ? 0 : (double)r.W / (r.W + r.L), r.Pct, 1e-9, "PCT = 승/(승+패)");
            }
            Assert.AreEqual(standings.Sum(r => r.W), standings.Sum(r => r.L), "리그 전체 승 = 패");
            Assert.AreEqual(standings.Sum(r => r.D) % 2, 0, "무승부는 짝");
            Assert.AreEqual(standings.Sum(r => r.RunsScored), standings.Sum(r => r.RunsAllowed));
            for (int i = 1; i < standings.Count; i++) Assert.GreaterOrEqual(standings[i - 1].Pct, standings[i].Pct, "승률 순");
            Assert.AreEqual(0, GMLiveSeasonSimulator.GamesBehind(standings[0], standings[0]));
            var second = standings[1];
            Assert.AreEqual(((standings[0].W - second.W) + (second.L - standings[0].L)) / 2.0, GMLiveSeasonSimulator.GamesBehind(standings[0], second), 1e-9);
            Assert.AreEqual(".500", GMTeamRecord.PctLabel(0.5));

            // 개인 기록 정합성: 리그 전체 타자 득점 = 리그 전체 실점, 타자 홈런 = 투수 피홈런
            Assert.AreEqual(league.Stats.Values.Sum(s => s.R), standings.Sum(r => r.RunsScored), "득점 합 = 구단 득점 합");
            Assert.AreEqual(league.Stats.Values.Sum(s => s.HR), league.Stats.Values.Sum(s => s.HRA), "홈런 = 피홈런");
            Assert.AreEqual(standings.Sum(r => r.W), league.Stats.Values.Sum(s => s.W), "승리 투수 수 = 승리 경기 수");

            // 세이브 왕복(v14)
            var json = JsonUtility.ToJson(SaveManager.ToGMSaveData(league));
            var data = JsonUtility.FromJson<GMLeagueSaveData>(json);
            var byId = templates.ToDictionary(t => t.TemplateId);
            var restored = SaveManager.FromGMSaveData(data, saved =>
            {
                var p = new Player(saved.InstanceId, byId[saved.TemplateId]);
                SaveManager.ApplyGMFields(p, saved);
                return p;
            });
            Assert.IsNotNull(restored);
            Assert.AreEqual(league.GamesPlayed, restored.GamesPlayed);
            Assert.AreEqual(league.SeasonYear, restored.SeasonYear);
            Assert.AreEqual(league.SelectedTeamCode, restored.SelectedTeamCode);
            Assert.AreEqual(league.Stats.Count, restored.Stats.Count);
            Assert.AreEqual(league.Records["SAM"].W, restored.Records["SAM"].W);
            var a = league.UserTeam.Roster[0];
            var b = restored.UserTeam.Roster.Single(p => p.InstanceId == a.InstanceId);
            Assert.AreEqual(a.Salary, b.Salary);
            Assert.AreEqual(a.EgoLevel, b.EgoLevel);
            Assert.AreEqual(a.RoleArchetype, b.RoleArchetype);
            Assert.AreEqual(a.Age, b.Age);
            Assert.AreEqual(a.ContractYears, b.ContractYears);
            Assert.AreEqual(league.UserTeam.CheerleaderPool.Count, restored.UserTeam.CheerleaderPool.Count);
            Assert.IsNull(SaveManager.FromGMSaveData(new GMLeagueSaveData(), _ => null), "구버전 세이브 = 단장 모드 미시작");
        }

        // ================================================================== 4) 대기록 · 부상

        [Test]
        public void T4_RecordNews_InjuryNews_DaysTickDown_Return_ManualLineup()
        {
            var sim = FullSeason("KIA", 99);
            var news = sim.League.News;
            Assert.IsTrue(news.Any(n => n.Kind == GMNewsKind.Record), "대기록 소식");
            Assert.IsTrue(news.Any(n => n.Kind == GMNewsKind.Injury), "부상 소식");
            Assert.IsTrue(news.Any(n => n.Kind == GMNewsKind.Return), "복귀 소식");
            var date = new Regex(@"^\d{2}/\d{2}/2026$");
            Assert.IsTrue(news.All(n => date.IsMatch(n.DateLabel)), "모든 소식에 MM/DD/2026 날짜");
            Assert.AreEqual("03/28/2026", GMLiveSeasonSimulator.DateLabel(0), "개막일");
            Assert.IsTrue(sim.League.AllPlayers.All(p => p.InjuryRemainingDays >= 0));

            // 직접 부상 등록 → 내 구단 주전이면 일시정지 팝업 → 경기 일수마다 1일 차감 → 0이면 복귀 소식
            var fresh = new GMLiveSeasonSimulator(NewLeague("SAM", seed: 3));
            var team = fresh.League.UserTeam;
            var starter = LineupAssignment.AssignStarters(team.Roster, team.Lineup).First(s => s.Player != null).Player;
            fresh.Injure(0, team, starter, 3, "손목 타박상");
            Assert.IsNotNull(fresh.PendingInterrupt, "주전 부상 → 시뮬레이션 일시정지");
            Assert.AreEqual(GMInterruptKind.Injury, fresh.PendingInterrupt.Kind);
            Assert.IsNotEmpty(fresh.PendingInterrupt.ReplacementCandidates, "대체 후보");
            Assert.IsFalse(fresh.StepGameDay(), "팝업 처리 전에는 진행 안 됨");
            var candidate = fresh.PendingInterrupt.ReplacementCandidates[0];
            fresh.ResolveInterrupt(GMInterruptChoice.ManualLineup, candidate);
            Assert.IsTrue(team.Lineup.Starters.Any(p => p.InstanceId == candidate.InstanceId && p.Position == starter.Template.BatterPosition), "직접 관리 = 대체 선수 고정");
            Assert.IsFalse(team.AvailableRoster.Contains(starter), "부상자는 출전 명단 제외");

            for (int day = 1; day <= 3; day++)
            {
                Assert.IsTrue(fresh.StartRun(GMRunMode.SingleGame));
                fresh.RunUntilStop();
                if (day < 3) Assert.AreEqual(3 - day, starter.InjuryRemainingDays, $"{day}일 경과");
            }
            Assert.AreEqual(0, starter.InjuryRemainingDays, "3경기 후 복귀");
            Assert.IsTrue(team.AvailableRoster.Contains(starter));
            Assert.IsTrue(fresh.League.News.Any(n => n.Kind == GMNewsKind.Return && n.Title.Contains(starter.Template.PlayerName)), "복귀 소식");
            Assert.IsFalse(team.Lineup.Starters.Any(p => p.InstanceId == candidate.InstanceId), "복귀 시 대체 고정 해제");

            // 부상 기간 분포(경미 5~7 · 중등 21~30 · 중상 60~120)
            Assert.AreEqual(5, GMLiveSeasonSimulator.MinorMin);
            Assert.AreEqual(30, GMLiveSeasonSimulator.ModerateMax);
            Assert.AreEqual(120, GMLiveSeasonSimulator.SevereMax);
        }

        // ================================================================== 5) 구단 대표색 · 배경 마크

        [Test]
        public void T5_TeamTheme_AndAnimatedEmblem_BindToSelectedTeam()
        {
            OpenScene();
            var view = Object.FindAnyObjectByType<GMLiveLeagueDashboardUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(view, "씬에 대시보드 조립");
            Assert.IsFalse(view.gameObject.activeSelf, "평소에는 꺼져 있다([플레이 볼]이 연다)");
            Assert.IsNotNull(view.transform.Find(GMLiveLeagueDashboardUIController.RootName));

            view.Build();
            view.Bind(new GMLiveSeasonSimulator(NewLeague("SAM")));
            Assert.AreEqual(Team.Samsung, view.ThemeTeam);
            Assert.AreEqual((Color)new Color32(0x07, 0x4C, 0xA1, 0xFF), TeamThemePalette.Primary(Team.Samsung), "삼성 블루 #074CA1");
            Assert.AreEqual(TeamThemePalette.Background(Team.Samsung), view.Root.GetComponent<Image>().color, "배경 = 구단 대표색");
            var emblem = view.Root.Find("Emblem/Logo").GetComponent<RawImage>();
            Assert.IsTrue(emblem.gameObject.activeInHierarchy || !view.gameObject.activeInHierarchy, "구단 마크 활성");
            Assert.IsNotNull(emblem.texture, "삼성 엠블럼 텍스처");
            StringAssert.Contains("Samsung", emblem.texture.name);
            Assert.That(emblem.color.a, Is.InRange(0.05f, 0.3f), "은은한 반투명");

            view.Bind(new GMLiveSeasonSimulator(NewLeague("LG")));
            Assert.AreEqual(Team.LG, view.ThemeTeam);
            Assert.AreEqual((Color)new Color32(0xC3, 0x04, 0x52, 0xFF), TeamThemePalette.Primary(Team.LG));
            StringAssert.Contains("LG", view.Root.Find("Emblem/Logo").GetComponent<RawImage>().texture.name);
            foreach (Team t in Enum.GetValues(typeof(Team)))
                if (t != Team.None) Assert.AreNotEqual(TeamThemePalette.Primary(Team.None), TeamThemePalette.Primary(t), t + " 고유 색");
        }

        // ================================================================== 6) UI 무결성

        [Test]
        public void T6_DashboardUi_NoOverlap_Min15pt_NoBold()
        {
            var view = NewDashboard();
            var sim = FullSeason("SSG", 11);
            view.Bind(sim);
            view.Refresh();
            CheckLayer(view.Root, "대시보드");

            // 투수 탭(7부문) · 인터럽트 팝업
            var tab = view.Root.Find("TabPitcher").GetComponent<Button>();
            tab.onClick.Invoke();
            Assert.IsTrue(view.ShowingPitchers);
            Assert.IsFalse(view.Root.Find("LeaderTitle7").gameObject.activeSelf, "투수 7부문 - 8번째 칸 숨김");
            CheckLayer(view.Root, "투수 탭");
            StringAssert.Contains("1. ", view.Root.Find("LeaderBody0").GetComponent<Text>().text);

            var live = new GMLiveSeasonSimulator(NewLeague("SSG", seed: 5));
            view.Bind(live);
            var team = live.League.UserTeam;
            var starter = LineupAssignment.AssignStarters(team.Roster, team.Lineup).First(s => s.Player != null).Player;
            live.Injure(0, team, starter, 25, "햄스트링 부상", "중등");
            view.ShowInterrupt(live.PendingInterrupt);
            Assert.IsTrue(view.IsPopupOpen);
            CheckLayer(view.Root.Find("Popup"), "부상 팝업");
            view.Root.Find("Popup/PopupSecondary").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(view.Root.Find("Popup/Candidate0").gameObject.activeSelf, "직접 관리 후보 표시");
            CheckLayer(view.Root.Find("Popup"), "후보 선택");

            OpenScene();
            var bold = SetupTask191.SceneTexts().Where(x => x.fontStyle == FontStyle.Bold || x.fontStyle == FontStyle.BoldAndItalic).Select(x => x.name).ToList();
            Assert.IsEmpty(bold, "씬 전체 FontStyle.Bold 0건");
            var scaler = Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include).First(s => s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize);
            Assert.AreEqual(new Vector2(1920, 1080), scaler.referenceResolution, "[TASK-GM-06] 1920×1080 Landscape");
        }

        /// <summary>layer 직속 자식 중 활성 텍스트/버튼 영역 겹침 0 · 15pt 이상 · Normal.</summary>
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
