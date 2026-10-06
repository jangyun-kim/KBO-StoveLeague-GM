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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-GM-03] 한 경기 시뮬레이션 6대 자동 검증(Unity CLI BatchPipelineGM03 1회 실행):
    ///   1) 경기 전 전력 비교(2.2.1) 바인딩  2) 박스스코어 수학적 정합성(2.2.2)  3) 승리 확률 그래프 · 결정적 플레이 TOP 3
    ///   4) 기사 헤드라인 + 4문단  5) 새 시즌 설정 모달(3대 모드 · 10구단 · 실명/가상명)  6) UI 무결성(겹침 0 · 15pt+ · Bold 0)
    /// </summary>
    public class GM03VerificationRunner
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
            dbObject = new GameObject("GM03_PlayerDatabase");
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

        private static GMLeagueState NewLeague(GMStartMode mode = GMStartMode.RealCurrent2026, string team = "SAM", int seed = 20260328, bool virtualNames = false)
        {
            var league = GMRosterLoader.LoadModeRoster(mode, team, virtualNames, templates, cheer);
            league.Seed = seed;
            return league;
        }

        /// <summary>
        /// 내 구단(삼성)을 일부러 '파벌 분열'(Ego 5 알파독 다수 · 주장/리더 없음)로 만들어 실책 · 비자책이 실제로 나오게 한 한 시즌(전 경기 박스스코어 보관).
        /// </summary>
        private static GMLiveSeasonSimulator SharedSeason()
        {
            if (season != null) return season;
            var league = NewLeague(seed: 3031);
            var user = league.UserTeam;
            foreach (var p in user.Roster)
            {
                p.IsCaptain = false;
                if (p.RoleArchetype == LockerRoomRole.DugoutLeader) p.RoleArchetype = LockerRoomRole.UnsungHero;
            }
            foreach (var p in user.Roster.Where(x => !x.IsPitcher).OrderByDescending(x => x.BaseOverall).Take(4)) { p.EgoLevel = 5; p.RoleArchetype = LockerRoomRole.AlphaDog; }
            Assert.IsTrue(TeamChemistryEngine.EvaluateRoster(user.Roster, user.PayrollCap).Has(AllStarOverloadPenalty.AlphaDogFactionSplit), "실책 배수 2.0 조건");
            season = new GMLiveSeasonSimulator(league) { RecordAllBoxScores = true };
            Assert.IsTrue(season.StartRun(GMRunMode.FullSeason));
            season.RunUntilStop();
            Assert.AreEqual(144, season.AllUserBoxScores.Count, "내 구단 144경기 박스스코어");
            return season;
        }

        private GameObject NewCanvas()
        {
            var canvasGo = new GameObject("GM03_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvasGo);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); // [TASK-GM-06] Landscape
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

        private static string T(Transform root, string path) => root.Find(path).GetComponent<Text>().text;
        private static string Short(string code) => CompyaUiKit.ShortName(NameAliasTable.ToTeam(code));

        // ================================================================== 1) 경기 전 전력 비교

        [Test]
        public void T1_PreGameMatchup_BindsLogosStartersPowerTeamworkCheer()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague());
            var preview = GMMatchPreview.Build(sim);
            Assert.IsNotNull(preview);
            Assert.AreEqual("Game #001 / 144", preview.GameLabel);
            Assert.AreEqual("03/28/2026", preview.DateLabel);
            Assert.IsNotEmpty(preview.Stadium);
            Assert.IsTrue(preview.Home.Code == "SAM" || preview.Away.Code == "SAM", "내 구단 경기");
            Assert.IsTrue(preview.Home.IsHome && !preview.Away.IsHome);
            foreach (var t in new[] { preview.Away, preview.Home })
            {
                Assert.IsNotNull(t.Starter, t.Code + " 선발");
                Assert.IsTrue(t.Starter.IsPitcher);
                StringAssert.Contains("구위", t.StarterRatingLabel);
                Assert.IsNotEmpty(t.StarterConditionLabel);
                for (int k = 0; k < 5; k++) Assert.That(t.Metric(k), Is.InRange(1, 100), $"{t.Code} {GMMatchPreview.MetricLabels[k]}");
                Assert.That(t.Teamwork, Is.InRange(20, 100));
                Assert.That(t.PowerMultiplier, Is.InRange(0.8f, 1.15f));
                Assert.IsNotEmpty(t.Badges);
                Assert.That(t.CheerEntry.Count, Is.InRange(4, 6), t.Code + " 치어리더 엔트리 4~6인");
                Assert.That(t.Leadership, Is.InRange(1, GMCheerleaderRules.MaxLeadershipBuff));
            }

            var canvas = NewCanvas();
            var view = NewView<GMMatchPrePostUIController>(canvas, "GM03_PrePost");
            view.Build();
            Assert.IsTrue(view.ShowPreGameView(sim));
            var root = view.PreRoot;
            Assert.IsTrue(root.gameObject.activeSelf);
            Assert.AreEqual(preview.GameLabel, T(root, "GameLabel"));
            StringAssert.Contains(preview.Stadium, T(root, "DateStadium"));
            Assert.AreEqual(preview.Away.Name, T(root, "AwayName"));
            Assert.AreEqual(preview.Home.Name, T(root, "HomeName"));
            StringAssert.Contains(preview.Away.Starter.Template.PlayerName, T(root, "AwayStarter"));
            StringAssert.Contains(preview.Home.Starter.Template.PlayerName, T(root, "HomeStarter"));
            StringAssert.Contains("최근", T(root, "AwayRecord"));
            for (int k = 0; k < 5; k++)
            {
                Assert.AreEqual(preview.Away.Metric(k).ToString(), T(root, $"MetricAway{k}"));
                Assert.AreEqual(preview.Home.Metric(k).ToString(), T(root, $"MetricHome{k}"));
                Assert.AreEqual(GMMatchPreview.MetricLabels[k], T(root, $"MetricLabel{k}"));
            }
            StringAssert.Contains("팀워크", T(root, "AwayTeamwork"));
            StringAssert.Contains("실효 전력 x", T(root, "HomeTeamwork"));
            Assert.IsNotEmpty(T(root, "AwayBadges"));
            StringAssert.Contains("리더십·팀워크", T(root, "AwayCheer"));
            StringAssert.Contains("마운드 버프", T(root, "HomeCheer"));
            StringAssert.Contains(preview.Home.CheerEntry[0].DisplayName, T(root, "HomeCheer"));
            Assert.IsNotNull(root.Find("AwayLogo/Logo").GetComponent<RawImage>().texture, "원정 로고");
            Assert.IsNotNull(root.Find("HomeLogo/Logo").GetComponent<RawImage>().texture, "홈 로고");
            foreach (var b in new[] { "StartButton", "CheckButton", "BackButton" }) Assert.IsNotNull(root.Find(b).GetComponent<Button>(), b);
            Assert.IsTrue(root.Find("StartButton").GetComponent<Button>().interactable);

            // [경기 시작] → 1경기 진행 + 박스스코어 화면
            Assert.IsTrue(view.StartMatch());
            Assert.AreEqual(1, sim.GamesPlayed);
            Assert.IsTrue(view.PostRoot.gameObject.activeSelf);
            Assert.IsFalse(view.PreRoot.gameObject.activeSelf);
            Assert.AreSame(sim.League.LastUserMatchBoxScore, view.CurrentBoxScore);
            var after = GMMatchPreview.Build(sim);
            Assert.AreEqual("Game #002 / 144", after.GameLabel);
            Assert.AreNotEqual("-", after.Home.Recent5, "최근 경기 흐름");
        }

        // ================================================================== 2) 박스스코어 정합성

        [Test]
        public void T2_BoxScore_RunsHitsWalksStrikeouts_MatchAcrossLinescoreBattersPitchers()
        {
            var sim = SharedSeason();
            bool sawError = false, sawUnearned = false;
            foreach (var b in sim.AllUserBoxScores)
            {
                string g = $"G{b.GameIndex + 1}";
                Assert.AreEqual(b.AwayR, b.AwayInningRuns.Where(r => r >= 0).Sum(), g + " 원정 이닝 합 = R");
                Assert.AreEqual(b.HomeR, b.HomeInningRuns.Where(r => r >= 0).Sum(), g + " 홈 이닝 합 = R");
                Assert.AreEqual(b.AwayR, b.AwayBatters.Sum(x => x.R), g + " 원정 R = 타자 득점 합");
                Assert.AreEqual(b.HomeR, b.HomeBatters.Sum(x => x.R), g + " 홈 R = 타자 득점 합");
                Assert.AreEqual(b.AwayR, b.HomePitchers.Sum(x => x.R), g + " 원정 R = 홈 투수 실점 합");
                Assert.AreEqual(b.HomeR, b.AwayPitchers.Sum(x => x.R), g + " 홈 R = 원정 투수 실점 합");
                Assert.AreEqual(b.AwayH, b.AwayBatters.Sum(x => x.H), g + " 원정 H");
                Assert.AreEqual(b.AwayH, b.HomePitchers.Sum(x => x.H), g + " 원정 H = 홈 투수 피안타");
                Assert.AreEqual(b.HomeH, b.AwayPitchers.Sum(x => x.H), g + " 홈 H = 원정 투수 피안타");
                Assert.AreEqual(b.AwayBatters.Sum(x => x.BB), b.HomePitchers.Sum(x => x.BB), g + " BB");
                Assert.AreEqual(b.HomeBatters.Sum(x => x.SO), b.AwayPitchers.Sum(x => x.SO), g + " SO");
                Assert.AreEqual(b.AwayBatters.Sum(x => x.HR), b.HomePitchers.Sum(x => x.HR), g + " HR");
                foreach (var p in b.HomePitchers.Concat(b.AwayPitchers))
                {
                    Assert.LessOrEqual(p.ER, p.R, g + " 자책 ≤ 실점");
                    Assert.Greater(p.NP, 0, g + " 투구수");
                    Assert.GreaterOrEqual(p.Outs, 0);
                }
                int topHalves = b.AwayInningRuns.Count;
                Assert.AreEqual(topHalves * 3, b.HomePitchers.Sum(p => p.Outs), g + " 홈 투수 이닝 합 = 원정 공격 이닝 × 3");
                int bottomHalves = b.HomeInningRuns.Count(r => r >= 0);
                Assert.That(b.AwayPitchers.Sum(p => p.Outs), Is.InRange(bottomHalves * 3 - 3, bottomHalves * 3), g + " 원정 투수 이닝 합(끝내기 반영)");
                Assert.That(b.Innings, Is.InRange(9, 12));
                if (!b.IsTie)
                {
                    var winSide = b.WinnerCode == b.HomeCode ? b.HomePitchers : b.AwayPitchers;
                    var loseSide = b.WinnerCode == b.HomeCode ? b.AwayPitchers : b.HomePitchers;
                    Assert.AreEqual(1, winSide.Count(p => p.Decision == "W"), g + " 승리 투수 1명");
                    Assert.AreEqual(1, loseSide.Count(p => p.Decision == "L"), g + " 패전 투수 1명");
                    Assert.LessOrEqual(winSide.Count(p => p.Decision == "S"), 1);
                    Assert.AreEqual(b.WinnerCode == b.HomeCode, b.HomeR > b.AwayR);
                }
                Assert.AreEqual(9, b.HomeBatters.Count(x => x.Order <= 9), g + " 홈 타순 9명");
                if (b.HomeE + b.AwayE > 0) sawError = true;
                if (b.HomePitchers.Concat(b.AwayPitchers).Any(p => p.R > p.ER)) sawUnearned = true;
            }
            Assert.IsTrue(sawError, "실책(E) 발생 경기");
            Assert.IsTrue(sawUnearned, "실책 이닝 비자책(R > ER) 발생");
            Assert.AreEqual(10, sim.League.RecentUserBoxScores.Count, "최근 10경기 보관");
            Assert.AreSame(sim.AllUserBoxScores.Last(), sim.League.LastUserMatchBoxScore, "직전 경기");

            // 시즌 누적과도 일치(내 구단 타자 안타 합)
            string me = sim.League.SelectedTeamCode;
            int boxHits = sim.AllUserBoxScores.Sum(b => (b.HomeCode == me ? b.HomeBatters : b.AwayBatters).Sum(x => x.H));
            int seasonHits = sim.League.Stats.Values.Where(s => s.TeamCode == me && !s.IsPitcher).Sum(s => s.H);
            Assert.AreEqual(seasonHits, boxHits, "박스스코어 안타 합 = 시즌 누적 안타");
        }

        // ================================================================== 3) WPA

        [Test]
        public void T3_WinProbabilityGraph_StartsAt50_EndsAtResult_Top3KeyPlays()
        {
            Assert.AreEqual(0.5f, GMLiveSeasonSimulator.HomeWinProbability(1, true, 0, 0, 0), 1e-5f, "경기 전 50%");
            Assert.Greater(GMLiveSeasonSimulator.HomeWinProbability(9, false, 2, 3, 1), 0.9f, "9회말 2점 리드 홈");
            Assert.Less(GMLiveSeasonSimulator.HomeWinProbability(8, true, 0, 0, 4), 0.1f, "8회초 4점 열세 홈");
            Assert.Greater(GMLiveSeasonSimulator.HomeWinProbability(5, true, 0, 1, 0), 0.5f);

            foreach (var b in SharedSeason().AllUserBoxScores)
            {
                var pts = b.WpaPoints;
                int pa = b.HomeBatters.Concat(b.AwayBatters).Sum(x => x.AB + x.BB);
                Assert.AreEqual(pa + 1, pts.Count, $"G{b.GameIndex + 1} 경기 전 + 타석마다 1점");
                Assert.AreEqual(0.5f, pts[0].HomeWinProbability, 1e-5f, "시작 50%");
                float end = b.IsTie ? 0.5f : b.WinnerCode == b.HomeCode ? 1f : 0f;
                Assert.AreEqual(end, pts[pts.Count - 1].HomeWinProbability, 1e-5f, "종료 = 승리 팀 100%");
                Assert.IsTrue(pts.All(p => p.HomeWinProbability >= 0f && p.HomeWinProbability <= 1f));

                float maxSwing = Enumerable.Range(1, pts.Count - 1).Max(i => Math.Abs(pts[i].HomeWinProbability - pts[i - 1].HomeWinProbability));
                Assert.AreEqual(Math.Min(3, pa), b.KeyPlays.Count, "결정적 플레이 3개");
                Assert.AreEqual(maxSwing, Math.Abs(b.KeyPlays[0].DeltaWPA), 1e-4f, "1위 = 최대 변동 타석");
                for (int i = 1; i < b.KeyPlays.Count; i++) Assert.GreaterOrEqual(Math.Abs(b.KeyPlays[i - 1].DeltaWPA), Math.Abs(b.KeyPlays[i].DeltaWPA) - 1e-6f, "|ΔWPA| 내림차순");
                StringAssert.Contains("WPA", b.KeyPlays[0].Label);
                StringAssert.Contains("회", b.KeyPlays[0].Label);
            }
        }

        // ================================================================== 4) 기사

        [Test]
        public void T4_RecapArticle_HeadlineScoreMatches_FourParagraphs()
        {
            var boxes = SharedSeason().AllUserBoxScores;
            foreach (var b in boxes)
            {
                var a = b.Recap;
                string g = $"G{b.GameIndex + 1}";
                Assert.IsNotNull(a);
                foreach (var part in new[] { a.Headline, a.Paragraph1, a.Paragraph2, a.Paragraph3, a.Paragraph4 }) Assert.IsFalse(string.IsNullOrWhiteSpace(part), g + " 빈 문단 없음");
                if (b.IsTie) StringAssert.Contains("무승부", a.Headline);
                else
                {
                    bool homeWon = b.WinnerCode == b.HomeCode;
                    int wr = homeWon ? b.HomeR : b.AwayR, lr = homeWon ? b.AwayR : b.HomeR;
                    StringAssert.StartsWith(Short(b.WinnerCode), a.Headline, g + " 헤드라인 승리 팀");
                    StringAssert.Contains($"{wr}-{lr}", a.Headline, g + " 헤드라인 스코어");
                    var w = (homeWon ? b.HomePitchers : b.AwayPitchers).First(p => p.Decision == "W");
                    StringAssert.Contains(w.Name, a.Paragraph1, g + " 승리 투수");
                }
                StringAssert.Contains(b.KeyPlays[0].BatterName, a.Paragraph2, g + " 결정적 타석 타자");
                StringAssert.Contains("\"", a.Paragraph3, g + " 소감 인용");
                if (b.GameIndex < 143) StringAssert.Contains(GMLiveSeasonSimulator.DateLabel(b.GameIndex + 1), a.Paragraph4, g + " 차전 날짜");
                else StringAssert.Contains("모두 끝났다", a.Paragraph4);
            }
            StringAssert.Contains("개막전", boxes[0].Recap.Headline + (boxes[0].IsTie ? "개막전" : ""), "개막전 맥락");
        }

        // ================================================================== 5) 새 시즌 설정 모달

        [Test]
        public void T5_NewSeasonModal_ResetsLeagueToChosenModeTeamAndNames()
        {
            var canvas = NewCanvas();
            var dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM03_Dashboard");
            dash.Build();
            dash.LeagueFactory = (mode, code, v) => GMRosterLoader.LoadModeRoster(mode, code, v, templates, cheer);
            var first = new GMLiveSeasonSimulator(NewLeague());
            first.StartRun(GMRunMode.SingleGame);
            first.RunUntilStop();
            dash.Bind(first);
            Assert.IsNotNull(dash.Root.Find("NewSeasonButton").GetComponent<Button>());

            void Pick(GMStartMode mode, string code, bool virtualNames)
            {
                dash.Root.Find("NewSeasonButton").GetComponent<Button>().onClick.Invoke();
                Assert.IsTrue(dash.IsSeasonModalOpen, "모달 열림");
                var modal = dash.Root.Find("SeasonModal");
                modal.Find($"Mode_{mode}").GetComponent<Button>().onClick.Invoke();
                modal.Find($"Team_{code}").GetComponent<Button>().onClick.Invoke();
                if (GameSettings.UseVirtualNames != virtualNames && dash.Simulator.League.UseVirtualNames != virtualNames)
                    modal.Find("VirtualToggle").GetComponent<Button>().onClick.Invoke();
                dash.SetSeasonVirtualNames(virtualNames);
                modal.Find("SeasonStart").GetComponent<Button>().onClick.Invoke();
                Assert.IsFalse(dash.IsSeasonModalOpen, "시작 후 모달 닫힘");
                var league = dash.Simulator.League;
                Assert.AreEqual(mode, league.Mode, "모드 즉시 전환");
                Assert.AreEqual(code, league.SelectedTeamCode, "구단 즉시 전환");
                Assert.AreEqual(virtualNames, league.UseVirtualNames);
                Assert.AreEqual(virtualNames, GameSettings.UseVirtualNames);
                Assert.AreEqual(0, league.GamesPlayed, "새 시즌 0경기");
                Assert.AreEqual(2026, league.SeasonYear);
                Assert.AreEqual(10, league.Teams.Count);
                Assert.AreEqual(NameAliasTable.ToTeam(code), dash.ThemeTeam, "구단 테마");
                Assert.AreEqual("G 000 / 144", T(dash.Root, "GameCounter"));
                var sample = league.UserTeam.Roster.First(p => !string.IsNullOrEmpty(p.Template.RealName));
                if (virtualNames) Assert.AreNotEqual(sample.Template.RealName, sample.Template.PlayerName, "가상명 표시");
                else Assert.AreEqual(sample.Template.RealName, sample.Template.PlayerName, "실명 표시");
            }

            Pick(GMStartMode.AllTimeDream, "KIA", true);
            Assert.IsNotEmpty(dash.Simulator.League.FreeAgents, "드림 FA 시장");
            Pick(GMStartMode.StoryCampaign, "LG", false);
            Assert.AreEqual(4, dash.Simulator.League.UserTeam.ConsecutiveLastPlaceSeasons, "스토리 캠페인");
            Pick(GMStartMode.RealCurrent2026, "SAM", false);

            dash.OpenSeasonModal();
            dash.Root.Find("SeasonModal/SeasonCancel").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(dash.IsSeasonModalOpen, "취소");
            Assert.AreEqual("SAM", dash.Simulator.League.SelectedTeamCode, "취소 시 리그 유지");
        }

        // ================================================================== 6) UI 무결성

        [Test]
        public void T6_PreGame_BoxScore_SeasonModal_NoOverlap_Min15pt_NoBold()
        {
            var sim = SharedSeason();
            var canvas = NewCanvas();
            var view = NewView<GMMatchPrePostUIController>(canvas, "GM03_PrePost");
            view.Build();
            Assert.IsTrue(view.ShowPreGameView(new GMLiveSeasonSimulator(NewLeague(team: "NC"))));
            CheckLayer(view.PreRoot, "전력 비교");

            view.Attach(sim);
            var boxes = new List<GMMatchBoxScoreData> { sim.League.LastUserMatchBoxScore };
            var extra = sim.AllUserBoxScores.FirstOrDefault(b => b.Innings > 9);
            if (extra != null) boxes.Add(extra);
            foreach (var b in boxes)
            {
                view.ShowPostGameBoxScoreView(b);
                var root = view.PostRoot;
                Assert.AreEqual("KOREAN BASEBALL ORGANIZATION", T(root, "KboTitle"));
                Assert.AreEqual(b.Recap.Headline, T(root, "Headline"));
                Assert.AreEqual(b.HomeR.ToString(), T(root, $"LS2_{GMMatchPrePostUIController.MaxInnings + 1}"), "전광판 홈 R");
                Assert.AreEqual(b.AwayH.ToString(), T(root, $"LS1_{GMMatchPrePostUIController.MaxInnings + 2}"), "전광판 원정 H");
                Assert.AreEqual(b.Innings, Enumerable.Range(1, GMMatchPrePostUIController.MaxInnings).Count(i => root.Find($"LS0_{i}").gameObject.activeSelf), "이닝 칸 수");
                Assert.Greater(root.Find("WpaArea/WpaLine").GetComponent<WpaLineGraphic>().Points.Count, 10, "WPA 폴리라인");
                StringAssert.Contains("WPA", T(root, "KeyPlay0"));
                foreach (var (home, pitching) in new[] { (false, false), (true, false), (false, true), (true, true) })
                {
                    view.SelectTab(home, pitching);
                    Assert.AreEqual(pitching ? "투수" : "선수", T(root, "Grid0_0"));
                    StringAssert.StartsWith("Totals", string.Join("|", Enumerable.Range(1, GMMatchPrePostUIController.GridRows - 1).Select(r => T(root, $"Grid{r}_0")).Where(x => x.StartsWith("Totals"))));
                    Assert.IsNotEmpty(T(root, "Footnote"));
                    CheckLayer(root, $"박스스코어 {(home ? "홈" : "원정")} {(pitching ? "투수" : "타자")}");
                }
                view.SelectTab(false, false);
                Assert.AreEqual(b.AwayBatters.Sum(x => x.AB).ToString(), T(root, $"Grid{Math.Min(b.AwayBatters.Count, GMMatchPrePostUIController.GridRows - 2) + 1}_1"), "Totals 타수");
            }

            var dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM03_Dashboard");
            dash.Build();
            dash.Bind(sim);
            CheckLayer(dash.Root, "대시보드(새 버튼 포함)");
            dash.OpenSeasonModal();
            CheckLayer(dash.Root.Find("SeasonModal"), "새 시즌 모달");

            OpenScene();
            var scenePrePost = Object.FindAnyObjectByType<GMMatchPrePostUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(scenePrePost, "씬에 전력 비교 · 박스스코어 화면");
            Assert.IsFalse(scenePrePost.gameObject.activeSelf);
            Assert.IsNotNull(scenePrePost.transform.Find(GMMatchPrePostUIController.PreRootName));
            Assert.IsNotNull(scenePrePost.transform.Find(GMMatchPrePostUIController.PostRootName));
            var sceneDash = Object.FindAnyObjectByType<GMLiveLeagueDashboardUIController>(FindObjectsInactive.Include);
            var dashRoot = sceneDash.transform.Find(GMLiveLeagueDashboardUIController.RootName);
            foreach (var n in new[] { "NewSeasonButton", "LastBoxButton", "SeasonModal" }) Assert.IsNotNull(dashRoot.Find(n), n);
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
