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
    /// [TASK-GM-04] KBO 7대 시상식 · 포스트시즌 · 연도 전환 6대 자동 검증(Unity CLI BatchPipelineGM04 1회 실행):
    ///   1) 월간 시상 6회(1.7) + 개인 수비 기록  2) 7월 올스타전 시상 10종(1.6)  3) 타이틀 홀더 14개 부문 = 리그 1위 기록(1.2)
    ///   4) KBO 시상식(1.1 · 1.3 · 1.5) · 골든글러브(1.4) · 포스트시즌 완결성  5) 2026 → 2027 전환 · 단장 데이터 · 세이브/로드  6) UI 무결성(겹침 0 · 15pt+ · Bold 0)
    /// </summary>
    public class GM04VerificationRunner
    {
        private static IReadOnlyList<PlayerTemplate> templates;
        private static List<Cheerleader> cheer;
        private static GameObject dbObject;
        private static GMLiveSeasonSimulator season;
        private static readonly List<GMNewsItem> seasonNews = new List<GMNewsItem>();
        private static bool allStarBefore72, allStarAt72;
        private readonly List<Object> created = new List<Object>();
        private bool sceneOpened;

        [SetUp]
        public void EnsureDatabase()
        {
            if (templates != null && templates.Count > 0 && templates.All(t => t != null)) return;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            dbObject = new GameObject("GM04_PlayerDatabase");
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
            seasonNews.Clear();
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

        private static GMLeagueState NewLeague(string team = "SAM", int seed = 20260328)
        {
            var league = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        /// <summary>한 시즌(144경기) 완주 - 매일 새로 들어온 소식을 모으고, 72경기 전후 올스타전 개최 여부를 기록한다.</summary>
        private static GMLiveSeasonSimulator SharedSeason()
        {
            if (season != null) return season;
            // 현 엔진은 실책이 케미스트리 부작용(실책 배수)으로만 발생하므로, GM-03 검증과 같이 내 구단을 '파벌 분열'로 만들어 실책이 실제로 나오게 한다.
            var league = NewLeague(seed: 4041);
            var user = league.UserTeam;
            foreach (var p in user.Roster)
            {
                p.IsCaptain = false;
                if (p.RoleArchetype == LockerRoomRole.DugoutLeader) p.RoleArchetype = LockerRoomRole.UnsungHero;
            }
            foreach (var p in user.Roster.Where(x => !x.IsPitcher).OrderByDescending(x => x.BaseOverall).Take(4)) { p.EgoLevel = 5; p.RoleArchetype = LockerRoomRole.AlphaDog; }
            Assert.IsTrue(TeamChemistryEngine.EvaluateRoster(user.Roster, user.PayrollCap).Has(AllStarOverloadPenalty.AlphaDogFactionSplit), "실책 배수 2.0 조건");
            var sim = new GMLiveSeasonSimulator(league) { RecordAllBoxScores = true };
            var seen = new HashSet<GMNewsItem>();
            sim.OnDayCompleted += () =>
            {
                foreach (var n in sim.League.News.Take(40)) if (seen.Add(n)) seasonNews.Add(n);
                if (sim.GamesPlayed == 71) allStarBefore72 = sim.League.Awards.AllStar.Played;
                if (sim.GamesPlayed == 72) allStarAt72 = sim.League.Awards.AllStar.Played;
            };
            Assert.IsTrue(sim.StartRun(GMRunMode.FullSeason));
            sim.RunUntilStop();
            Assert.IsTrue(sim.IsSeasonComplete);
            season = sim;
            return season;
        }

        private GameObject NewCanvas()
        {
            var canvasGo = new GameObject("GM04_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
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

        private static void AssertRegistered(GMLeagueState league, GMAwardWinner w, string label)
        {
            Assert.IsNotNull(w, label + " 수상자 존재");
            Assert.IsTrue(w.HasWinner, label + " 이름");
            Assert.IsNotEmpty(w.PlayerId, label + " 선수 ID");
            Assert.IsNotEmpty(w.TeamCode, label + " 구단");
            var p = league.FindPlayer(w.PlayerId);
            Assert.IsNotNull(p, label + " 리그 소속 선수");
            Assert.IsNotEmpty(w.CareerTag, label + " 태그");
            CollectionAssert.Contains(p.CareerAwardIds, w.CareerTag, label + " CareerAwardIds 등록");
        }

        // ================================================================== 1) 월간 시상 6회 + 개인 수비

        [Test]
        public void T1_MonthlyAwards_SixTimes_MvpAndCapsPlay_InNews_DefenseTracked()
        {
            var sim = SharedSeason();
            var league = sim.League;
            var monthly = league.Awards.Monthly;
            Assert.AreEqual(6, monthly.Count, "월간 시상 6회(24 · 48 · 72 · 96 · 120 · 144경기)");
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, monthly.Select(m => m.MonthIndex).ToArray());
            CollectionAssert.AreEqual(GMAwardEvaluator.MonthLabels, monthly.Select(m => m.MonthLabel).ToArray());
            int userAwards = 0;
            foreach (var m in monthly)
            {
                Assert.AreEqual((m.MonthIndex - 1) * 24 + 1, m.StartGame);
                Assert.AreEqual(m.MonthIndex * 24, m.EndGame);
                AssertRegistered(league, m.Mvp, $"{m.MonthLabel} 월간 MVP");
                AssertRegistered(league, m.CapsPlay, $"{m.MonthLabel} 캡스플레이상");
                Assert.AreEqual("1.7.1", m.Mvp.Section);
                Assert.AreEqual("1.7.2", m.CapsPlay.Section);
                StringAssert.Contains("월간 WAR", m.Mvp.ValueLabel);
                StringAssert.Contains("수비 기여", m.CapsPlay.ValueLabel);
                Assert.IsFalse(league.FindPlayer(m.CapsPlay.PlayerId).IsPitcher, "캡스플레이상 = 야수");
                Assert.IsTrue(seasonNews.Any(n => n.Kind == GMNewsKind.Monthly && n.Title == $"{m.MonthLabel} KBO 월간 MVP: {m.Mvp.PlayerName}"), $"{m.MonthLabel} 월간 MVP 소식");
                Assert.IsTrue(seasonNews.Any(n => n.Kind == GMNewsKind.Monthly && n.Title == $"{m.MonthLabel} 월간 캡스플레이상: {m.CapsPlay.PlayerName}"), $"{m.MonthLabel} 캡스플레이상 소식");
                userAwards += (m.Mvp.TeamCode == league.SelectedTeamCode ? 1 : 0) + (m.CapsPlay.TeamCode == league.SelectedTeamCode ? 1 : 0);
            }
            // [TASK-GM-05] 홈 흥행(치어리더 홈 흥행력)도 팬 지지율을 올리므로 월간 수상 보너스 이상인지 확인한다.
            Assert.GreaterOrEqual(league.UserTeam.FanSupport, Math.Min(100, GMTeamFan.DefaultSupport + userAwards * GMTeamFan.MonthlyAwardBonus), "내 구단 수상 = 팬 지지율 보너스");

            // 개인 수비: 내 구단 선수 개인 실책 합 = 내 구단 경기 박스스코어 실책(E) 합, 수비 기여 점수 산출
            string me = league.SelectedTeamCode;
            int boxErrors = sim.AllUserBoxScores.Sum(b => b.HomeCode == me ? b.HomeE : b.AwayE);
            int playerErrors = league.Stats.Values.Where(s => s.TeamCode == me).Sum(s => s.Errors);
            Assert.Greater(boxErrors, 0, "실책 발생");
            Assert.AreEqual(boxErrors, playerErrors, "개인 실책 합 = 팀 실책 합");
            var fielders = league.Stats.Values.Where(s => !s.IsPitcher && s.DefG > 0).ToList();
            Assert.GreaterOrEqual(fielders.Count, 80, "야수 수비 출전(10구단 × 8포지션 이상)");
            Assert.IsTrue(fielders.All(s => s.Chances >= 0 && s.DefG <= 144 && s.Positions.Take(8).Sum() == s.DefG), "수비 출전 = 포지션별 출전 합");
            Assert.IsTrue(fielders.Any(s => s.FinePlays > 0), "호수비 기록");
            foreach (var s in fielders.Take(30))
                Assert.AreEqual(GMAwardEvaluator.DefensiveScore(s.DefG, s.Chances, s.Errors, s.FinePlays, s.DefRating), s.DefensiveScore, 1e-3f, "수비 기여 점수 공식");
            var clean = GMAwardEvaluator.DefensiveScore(100, 200, 0, 5, 70);
            Assert.Greater(clean, GMAwardEvaluator.DefensiveScore(100, 200, 10, 5, 70), "실책이 많으면 점수 하락");
        }

        // ================================================================== 2) 7월 올스타전

        [Test]
        public void T2_AllStarGame_After72Games_AllTenAwards()
        {
            var sim = SharedSeason();
            var league = sim.League;
            var star = league.Awards.AllStar;
            Assert.IsFalse(allStarBefore72, "71경기까지 올스타전 없음");
            Assert.IsTrue(allStarAt72, "전반기 72경기 직후 개최");
            Assert.IsTrue(star.Played);
            CollectionAssert.AreEquivalent(new[] { "SAM", "DOO", "KT", "SSG", "LOT" }, star.DreamTeams, "드림 올스타");
            CollectionAssert.AreEquivalent(new[] { "LG", "NC", "KIA", "HAN", "KIW" }, star.NanumTeams, "나눔 올스타");
            Assert.AreEqual(star.DreamWon, star.DreamScore > star.NanumScore || (star.DreamScore == star.NanumScore && star.DreamWon), "승패 = 점수");
            if (star.DreamScore == star.NanumScore) StringAssert.Contains("스윙오프", star.Decider);
            StringAssert.Contains("드림 올스타", star.Summary);

            var ids = new[]
            {
                "ALLSTAR_MVP", "ALLSTAR_BEST_PITCHER", "ALLSTAR_BEST_BATTER", "ALLSTAR_WIN_MANAGER", "ALLSTAR_LOSING_PITCHER", "ALLSTAR_FIGHTING_SPIRIT",
                "ALLSTAR_HR_DERBY", "ALLSTAR_HR_DERBY_RUNNER_UP", "ALLSTAR_HR_RACE", "ALLSTAR_PERFECT_PITCHER",
            };
            Assert.AreEqual(ids.Length, star.Awards.Count, "올스타 시상 10종");
            var winTeams = star.DreamWon ? star.DreamTeams : star.NanumTeams;
            var loseTeams = star.DreamWon ? star.NanumTeams : star.DreamTeams;
            foreach (var id in ids)
            {
                var a = star.Find(id);
                Assert.IsNotNull(a, id);
                Assert.IsTrue(a.HasWinner, id + " 수상자");
                if (id == "ALLSTAR_WIN_MANAGER")
                {
                    CollectionAssert.Contains(winTeams, a.TeamCode, "승리감독 = 승리 팀 구단");
                    StringAssert.Contains("감독", a.PlayerName);
                    continue;
                }
                AssertRegistered(league, a, id);
            }
            CollectionAssert.Contains(winTeams, star.Find("ALLSTAR_MVP").TeamCode, "미스터 올스타 = 승리 팀");
            CollectionAssert.Contains(loseTeams, star.Find("ALLSTAR_LOSING_PITCHER").TeamCode, "패전투수 = 패전 팀");
            CollectionAssert.Contains(loseTeams, star.Find("ALLSTAR_FIGHTING_SPIRIT").TeamCode, "감투상 = 패전 팀");
            Assert.IsTrue(league.FindPlayer(star.Find("ALLSTAR_BEST_PITCHER").PlayerId).IsPitcher, "우수투수 = 투수");
            Assert.IsFalse(league.FindPlayer(star.Find("ALLSTAR_BEST_BATTER").PlayerId).IsPitcher, "우수타자 = 타자");
            Assert.IsTrue(league.FindPlayer(star.Find("ALLSTAR_PERFECT_PITCHER").PlayerId).IsPitcher, "퍼펙트피처 = 투수");
            Assert.AreNotEqual(star.Find("ALLSTAR_HR_DERBY").PlayerId, star.Find("ALLSTAR_HR_DERBY_RUNNER_UP").PlayerId, "홈런더비 우승 ≠ 준우승");
            Assert.GreaterOrEqual(star.Find("ALLSTAR_HR_DERBY").Value, star.Find("ALLSTAR_HR_DERBY_RUNNER_UP").Value, "결승 홈런 수");
            var news = seasonNews.FirstOrDefault(n => n.Kind == GMNewsKind.Award && n.Title.Contains("올스타전"));
            Assert.IsNotNull(news, "올스타전 소식");
            Assert.IsTrue(news.IsMajor);
            StringAssert.Contains(star.Find("ALLSTAR_MVP").PlayerName, news.Title);
            Assert.IsTrue(seasonNews.Any(n => n.Title.Contains("전반기 종료")), "전반기 종료 소식");
        }

        // ================================================================== 3) 타이틀 홀더 14개 부문

        private static double Stat(string id, GMPlayerSeasonStats s)
        {
            switch (id)
            {
                case "W": return s.W;
                case "ERA": return s.OutsPitched == 0 ? 99.99 : s.ER * 27.0 / s.OutsPitched;
                case "SO": return s.PSO;
                case "WPCT": return s.W + s.L == 0 ? 0 : (double)s.W / (s.W + s.L);
                case "SV": return s.SV;
                case "HLD": return s.HLD;
                case "AVG": return s.AB == 0 ? 0 : (double)s.H / s.AB;
                case "HR": return s.HR;
                case "RBI": return s.RBI;
                case "H": return s.H;
                case "R": return s.R;
                case "SB": return s.SB;
                case "OBP": return s.PA == 0 ? 0 : (double)(s.H + s.BB) / s.PA;
                default: return s.AB == 0 ? 0 : (double)(s.H + s.Doubles + 2 * s.Triples + 3 * s.HR) / s.AB; // SLG
            }
        }

        [Test]
        public void T3_TitleHolders_14Categories_MatchLeagueLeaders()
        {
            var sim = SharedSeason();
            GMAwardEvaluator.HoldKboAwardsCeremony(sim);
            var league = sim.League;
            var titles = league.Awards.KboCeremony.Where(a => a.Category == GMAwardCategory.TitleHolder).ToList();
            Assert.AreEqual(14, titles.Count, "타이틀 14개 부문");
            Assert.AreEqual(6, titles.Count(a => a.Section == "1.2.1"), "투수 6");
            Assert.AreEqual(8, titles.Count(a => a.Section == "1.2.2"), "타자 8");
            var expectedIds = new[] { "W", "ERA", "SO", "WPCT", "SV", "HLD", "AVG", "HR", "RBI", "H", "R", "SB", "OBP", "SLG" };
            CollectionAssert.AreEquivalent(expectedIds.Select(i => "TITLE_" + i), titles.Select(a => a.AwardId));

            int g = league.GamesPlayed;
            foreach (var id in expectedIds)
            {
                bool pitching = id == "W" || id == "ERA" || id == "SO" || id == "WPCT" || id == "SV" || id == "HLD";
                var pool = league.Stats.Values.Where(s => pitching ? s.IsPitcher && s.PG > 0 : !s.IsPitcher && s.PA > 0).ToList();
                if (id == "AVG" || id == "OBP" || id == "SLG") pool = pool.Where(s => s.PA >= g * 3.1).ToList();
                if (id == "ERA") pool = pool.Where(s => s.OutsPitched >= g * 3).ToList();
                if (id == "WPCT")
                {
                    var tenWins = pool.Where(s => s.W >= 10).ToList();
                    if (tenWins.Count > 0) pool = tenWins;
                    else { int maxW = pool.Max(s => s.W); pool = pool.Where(s => s.W >= maxW * 0.5).ToList(); }
                }
                Assert.IsNotEmpty(pool, id + " 규정 충족 후보");
                double best = id == "ERA" ? pool.Min(s => Stat(id, s)) : pool.Max(s => Stat(id, s));
                var award = titles.Single(a => a.AwardId == "TITLE_" + id);
                AssertRegistered(league, award, id);
                var winnerStats = league.Stats[award.PlayerId];
                Assert.AreEqual(best, award.Value, 1e-9, $"{id} 수상 기록 = 리그 1위 기록");
                Assert.AreEqual(best, Stat(id, winnerStats), 1e-9, $"{id} 수상자 실제 기록");
                Assert.IsTrue(pool.Contains(winnerStats), $"{id} 수상자 규정 충족");
                StringAssert.StartsWith($"TITLE_HOLDER_{league.SeasonYear}_", award.CareerTag);
            }
            // 리더보드(대시보드 TOP 3)와도 일치 - 홈런 · 타율
            Assert.AreEqual(sim.Leaders(GMLeaderCategory.HR, 1)[0].Value, titles.Single(a => a.AwardId == "TITLE_HR").Value, 1e-9);
            Assert.AreEqual(sim.Leaders(GMLeaderCategory.AVG, 1)[0].Value, titles.Single(a => a.AwardId == "TITLE_AVG").Value, 1e-9);
        }

        // ================================================================== 4) 시상식 · 골든글러브 · 포스트시즌 완결성

        [Test]
        public void T4_KboCeremony_Defense_Special_GoldenGlove_Postseason_Complete()
        {
            var sim = SharedSeason();
            var league = sim.League;
            Assert.IsTrue(sim.IsSeasonComplete, "정규시즌 종료");
            Assert.GreaterOrEqual((int)league.Phase, (int)GMSeasonPhase.PostSeason);

            // 포스트시즌
            var ps = GMAwardEvaluator.RunPostseason(sim);
            Assert.IsTrue(ps.Completed);
            CollectionAssert.AreEqual(sim.Standings().Take(5).Select(r => r.TeamCode).ToList(), ps.SeedCodes, "상위 5개 팀");
            CollectionAssert.AreEqual(new[] { "와일드카드 결정전", "준플레이오프", "플레이오프", "한국시리즈" }, ps.Series.Select(s => s.Round).ToArray());
            Assert.AreEqual(1, ps.Series[0].HigherNeeds, "와일드카드 4위 1승 어드밴티지");
            Assert.AreEqual(2, ps.Series[0].LowerNeeds);
            Assert.AreEqual(4, ps.Series[3].HigherNeeds, "한국시리즈 4선승");
            foreach (var s in ps.Series)
            {
                Assert.IsTrue(s.WinnerCode == s.HigherCode ? s.HigherWins == s.HigherNeeds && s.LowerWins < s.LowerNeeds
                                                          : s.LowerWins == s.LowerNeeds && s.HigherWins < s.HigherNeeds, s.Round + " 시리즈 승수");
                Assert.GreaterOrEqual(s.Games.Count, s.HigherWins + s.LowerWins);
            }
            Assert.AreEqual(ps.Series[0].WinnerCode, ps.Series[1].LowerCode, "와일드카드 승자 → 준PO");
            Assert.AreEqual(ps.Series[1].WinnerCode, ps.Series[2].LowerCode, "준PO 승자 → PO");
            Assert.AreEqual(ps.Series[2].WinnerCode, ps.Series[3].LowerCode, "PO 승자 → KS");
            Assert.AreEqual(ps.Series[3].WinnerCode, ps.ChampionCode);
            Assert.AreEqual(10, ps.FinalRankCodes.Distinct().Count(), "최종 순위 10구단");
            Assert.AreEqual(ps.ChampionCode, ps.FinalRankCodes[0]);
            Assert.IsNotEmpty(ps.KoreanSeriesMvp);
            Assert.AreEqual(GMSeasonPhase.AwardsCeremony, league.Phase, "포스트시즌 후 시상식 단계");

            // 11월 KBO 시상식
            var list = GMAwardEvaluator.HoldKboAwardsCeremony(sim);
            Assert.AreEqual(28, list.Count, "MVP · 신인 · 타이틀 14 · 수비 10 · 특별 2");
            Assert.AreEqual(list.Count, list.Select(a => a.AwardId).Distinct().Count());
            foreach (var a in list) AssertRegistered(league, a, a.AwardId);
            var mvp = list.Single(a => a.AwardId == "MVP");
            Assert.AreEqual("1.1.1", mvp.Section);
            StringAssert.Contains("득표율", mvp.ValueLabel);
            Assert.That(mvp.Value, Is.InRange(0.0, 100.0));
            CollectionAssert.Contains(league.FindPlayer(mvp.PlayerId).CareerAwardIds, $"MVP_{league.SeasonYear}");
            var roy = list.Single(a => a.AwardId == "ROOKIE");
            if (league.Stats.Values.Any(s => league.FindPlayer(s.PlayerId).Age <= GMAwardEvaluator.RookieMaxAge && (s.PA >= 100 || s.OutsPitched >= 90)))
                Assert.LessOrEqual(league.FindPlayer(roy.PlayerId).Age, GMAwardEvaluator.RookieMaxAge, "신인상 23세 이하");

            var slots = new Dictionary<string, int> { { "P", 9 }, { "C", 0 }, { "1B", 1 }, { "2B", 2 }, { "3B", 3 }, { "SS", 4 }, { "LF", 5 }, { "CF", 6 }, { "RF", 7 } };
            var defense = list.Where(a => a.Category == GMAwardCategory.Defense).ToList();
            Assert.AreEqual(10, defense.Count, "수비상 9포지션 + 유틸리티");
            foreach (var pair in slots)
            {
                var a = defense.Single(x => x.AwardId == "DEF_" + pair.Key);
                Assert.AreEqual("1.3.1", a.Section);
                Assert.Greater(league.Stats[a.PlayerId].Positions[pair.Value], 0, $"{pair.Key} 수비상 = 해당 포지션 출전");
                Assert.AreEqual(pair.Value == 9, league.FindPlayer(a.PlayerId).IsPitcher, $"{pair.Key} 투수/야수 구분");
            }
            var util = defense.Single(a => a.AwardId == "DEF_UTIL");
            Assert.AreEqual("1.3.2", util.Section);
            CollectionAssert.DoesNotContain(defense.Where(a => a != util).Select(a => a.PlayerId).ToList(), util.PlayerId, "유틸리티 ≠ 포지션 수상자");
            var fair = list.Single(a => a.AwardId == "FAIR_PLAY");
            var special = list.Single(a => a.AwardId == "KBO_SPECIAL");
            Assert.AreEqual("1.5.1", fair.Section);
            Assert.AreEqual("1.5.2", special.Section);
            StringAssert.IsMatch("시즌 대기록|리그 발전 기여 베테랑", special.ValueLabel);
            var fairRole = league.FindPlayer(fair.PlayerId).RoleArchetype;
            Assert.IsTrue(fairRole == LockerRoomRole.DugoutLeader || fairRole == LockerRoomRole.UnsungHero, "페어플레이 = 더그아웃 리더 · 살림꾼");

            // 12월 골든글러브 - UI [골든글러브 시상식 개최]로 단독 개최
            var canvas = NewCanvas();
            var view = NewView<GMAwardsCeremonyUIController>(canvas, "GM04_Awards");
            view.Build();
            view.Open(sim, GMAwardsTab.GoldenGlove);
            if (!league.Awards.GoldenGloveHeld)
            {
                var hold = view.Root.Find("HoldGoldenGlove");
                Assert.IsTrue(hold.gameObject.activeSelf, "골든글러브 개최 버튼");
                hold.GetComponent<Button>().onClick.Invoke();
            }
            Assert.IsTrue(league.Awards.GoldenGloveHeld, "12월 골든글러브 개최");
            var gg = league.Awards.GoldenGlove;
            Assert.AreEqual(10, gg.Count, "골든글러브 10명");
            CollectionAssert.AreEquivalent(new[] { "GG_P", "GG_C", "GG_1B", "GG_2B", "GG_3B", "GG_SS", "GG_OF1", "GG_OF2", "GG_OF3", "GG_DH" }, gg.Select(a => a.AwardId));
            Assert.AreEqual(10, gg.Select(a => a.PlayerId).Distinct().Count(), "골든글러브 중복 수상 없음");
            foreach (var a in gg)
            {
                AssertRegistered(league, a, a.AwardId);
                Assert.AreEqual("1.4.1", a.Section);
                StringAssert.Contains("득표율", a.ValueLabel);
            }
            Assert.IsTrue(league.FindPlayer(gg.Single(a => a.AwardId == "GG_P").PlayerId).IsPitcher);
            foreach (var of in gg.Where(a => a.AwardId.StartsWith("GG_OF")))
            {
                var st = league.Stats[of.PlayerId];
                Assert.Greater(st.Positions[5] + st.Positions[6] + st.Positions[7], 0, "외야수 = 외야 출전");
            }
            Assert.IsTrue(league.Awards.IsComplete, "포스트시즌 · 11월 · 12월 시상 완결");
            Assert.IsTrue(league.Awards.DynamicsApplied, "골든글러브 후 단장 역학 반영");
            Assert.AreEqual(GMAwardsTab.GoldenGlove, view.CurrentTab);
            Assert.AreEqual(10, Enumerable.Range(0, GMAwardsCeremonyUIController.CellCount).Count(i => view.Root.Find($"CellTitle{i}").gameObject.activeSelf), "골든글러브 카드 10장");
        }

        // ================================================================== 5) 2026 → 2027

        [Test]
        public void T5_AdvanceTo2027_AgeContractEgoSalary_Reset_SaveLoad()
        {
            var league = NewLeague(team: "LG", seed: 5051);
            var sim = new GMLiveSeasonSimulator(league);
            sim.StartRun(GMRunMode.FullSeason);
            sim.RunUntilStop();
            Assert.IsTrue(sim.IsSeasonComplete);
            var everyone = league.AllPlayers.Concat(league.FreeAgents).ToList();
            var before = everyone.ToDictionary(p => p, p => (p.Age, p.ContractYears, p.EgoLevel, p.Salary));

            // 대시보드: 144경기 종료 → 진행 버튼 대신 [포스트시즌 & 시상식 보기] · [2027 시즌 전환]
            var canvas = NewCanvas();
            var dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM04_Dashboard");
            dash.Build();
            var awardsView = NewView<GMAwardsCeremonyUIController>(canvas, "GM04_Awards");
            awardsView.Build();
            dash.AwardsView = awardsView;
            dash.Bind(sim);
            var root = dash.Root;
            Assert.IsTrue(root.Find("AwardsButton").gameObject.activeSelf, "[포스트시즌 & 시상식 보기]");
            Assert.IsTrue(root.Find("NextSeasonButton").gameObject.activeSelf, "[2027 시즌 전환]");
            Assert.AreEqual("2027 시즌 전환", root.Find("NextSeasonButton").GetComponentInChildren<Text>().text);
            Assert.IsFalse(root.Find("ModeFull").gameObject.activeSelf, "진행 버튼 숨김");
            root.Find("AwardsButton").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(awardsView.gameObject.activeSelf, "시상식 화면 열림");
            Assert.AreEqual(GMAwardsTab.KboCeremony, awardsView.CurrentTab);
            Assert.IsTrue(league.Awards.Postseason.Completed && league.Awards.KboCeremonyHeld, "열면 포스트시즌 → 11월 시상식");
            Assert.IsFalse(league.Awards.GoldenGloveHeld, "골든글러브는 12월 단독");
            awardsView.Root.Find("BackButton").GetComponent<Button>().onClick.Invoke();
            Assert.IsFalse(awardsView.gameObject.activeSelf);

            string mvpId = league.Awards.Find("MVP").PlayerId;
            root.Find("NextSeasonButton").GetComponent<Button>().onClick.Invoke();
            var next = dash.Simulator;
            Assert.AreNotSame(sim, next, "새 진행기");
            Assert.AreSame(league, next.League);
            Assert.AreEqual(2027, league.SeasonYear, "2026 → 2027");
            Assert.AreEqual(0, league.GamesPlayed, "0 / 144");
            Assert.AreEqual("G 000 / 144", T(root, "GameCounter"));
            Assert.AreEqual("2027 KBO 시즌", T(root, "SeasonTitle"));
            Assert.IsTrue(root.Find("ModeFull").gameObject.activeSelf, "새 시즌 진행 버튼");
            Assert.AreEqual(GMSeasonPhase.StoveLeague, league.Phase);
            Assert.IsEmpty(league.Stats, "시즌 기록 리셋");
            Assert.IsTrue(league.Records.Values.All(r => r.G == 0), "순위 리셋");
            Assert.IsTrue(everyone.All(p => p.InjuryRemainingDays == 0), "부상 초기화");

            // 수상 기록 영구 보존 · 새 시즌 묶음
            Assert.AreEqual(1, league.SeasonAwardsHistory.Count);
            var last = league.SeasonAwardsHistory[0];
            Assert.AreEqual(2026, last.SeasonYear);
            Assert.IsTrue(last.IsComplete);
            Assert.AreEqual(2027, league.Awards.SeasonYear);
            Assert.IsFalse(league.Awards.HasAny);
            var mvpPlayer = league.FindPlayer(mvpId) ?? league.FreeAgents.FirstOrDefault(p => p.InstanceId == mvpId); // [TASK-GM-09/10] 계약 만료 · 보류 제외로 FA 시장에 있을 수 있다
            CollectionAssert.Contains(mvpPlayer.CareerAwardIds, "MVP_2026", "수상 이력 보존");

            // 나이 +1 · 계약 -1 · 수상자 Ego +1 · 연봉 15~30% 인상, 그 외 Ego · 연봉 유지
            var raised = last.Dynamics.ToDictionary(d => d.PlayerId);
            Assert.IsTrue(raised.ContainsKey(mvpId), "MVP 단장 역학 반영");
            Assert.GreaterOrEqual(raised.Count, 10, "MVP · 골든글러브 · 타이틀 수상자");
            foreach (var pair in before)
            {
                var p = pair.Key;
                var (age, years, ego, salary) = pair.Value;
                Assert.AreEqual(Math.Min(Player.MaxAge, age + 1), p.Age, "나이 +1");
                bool released = league.LastReserveReleases.Any(r => r.Player == p); // [TASK-GM-10] 보류명단 제외 = 자유계약(계약 0)
                Assert.AreEqual(released ? 0 : Math.Max(0, years - 1), p.ContractYears, "계약 -1");
                if (raised.TryGetValue(p.InstanceId, out var d))
                {
                    Assert.AreEqual(Math.Min(5, ego + 1), p.EgoLevel, d.PlayerName + " Ego +1");
                    Assert.That(d.RaisePercent, Is.InRange(GMAwardEvaluator.MinRaisePercent, GMAwardEvaluator.MaxRaisePercent));
                    Assert.AreEqual((int)Math.Min(Player.MaxSalary, Math.Round(salary * (100 + d.RaisePercent) / 100.0)), p.Salary, d.PlayerName + " 연봉 인상");
                    if (salary < Player.MaxSalary) Assert.Greater(p.Salary, salary);
                }
                else
                {
                    Assert.AreEqual(ego, p.EgoLevel, "비수상자 Ego 유지");
                    Assert.AreEqual(salary, p.Salary, "비수상자 연봉 유지");
                }
            }

            // 2027 시즌 진행 - 날짜 연도 반영
            Assert.IsTrue(next.StartRun(GMRunMode.SingleGame));
            next.RunUntilStop();
            Assert.AreEqual(1, next.GamesPlayed);
            Assert.AreEqual("03/28/2027", league.LastUserMatchBoxScore.DateLabel);
            Assert.AreEqual(2027, league.LastUserMatchBoxScore.SeasonYear);

            // 세이브 / 로드(v15) 무결성
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
            Assert.AreEqual(2027, restored.SeasonYear);
            Assert.AreEqual(1, restored.GamesPlayed);
            Assert.AreEqual(1, restored.SeasonAwardsHistory.Count);
            Assert.AreEqual(last.Find("MVP").PlayerName, restored.SeasonAwardsHistory[0].Find("MVP").PlayerName);
            Assert.AreEqual(last.GoldenGlove.Count, restored.SeasonAwardsHistory[0].GoldenGlove.Count);
            Assert.AreEqual(last.Postseason.ChampionCode, restored.SeasonAwardsHistory[0].Postseason.ChampionCode);
            Assert.AreEqual(last.Monthly.Count, restored.SeasonAwardsHistory[0].Monthly.Count);
            Assert.AreEqual(last.AllStar.Find("ALLSTAR_MVP").PlayerName, restored.SeasonAwardsHistory[0].AllStar.Find("ALLSTAR_MVP").PlayerName);
            Assert.AreEqual(2027, restored.Awards.SeasonYear);
            var mvpRestored = restored.FindPlayer(mvpId);
            var mvpNow = league.FindPlayer(mvpId);
            Assert.AreEqual(mvpNow.EgoLevel, mvpRestored.EgoLevel);
            Assert.AreEqual(mvpNow.Salary, mvpRestored.Salary);
            Assert.AreEqual(mvpNow.Age, mvpRestored.Age);
            CollectionAssert.AreEqual(mvpNow.CareerAwardIds, mvpRestored.CareerAwardIds);
            Assert.AreEqual(league.UserTeam.FanSupport, restored.UserTeam.FanSupport);
            var anyStats = league.Stats.Values.First(s => s.DefG > 0);
            Assert.AreEqual(anyStats.DefG, restored.Stats[anyStats.PlayerId].DefG, "수비 기록 저장");
            CollectionAssert.AreEqual(anyStats.Positions, restored.Stats[anyStats.PlayerId].Positions);
            Assert.IsNotNull(new GMLiveSeasonSimulator(restored), "복원 리그로 진행기 생성");
        }

        // ================================================================== 6) UI 무결성

        [Test]
        public void T6_AwardsCeremonyUI_AllTabs_NoOverlap_Min15pt_NoBold()
        {
            var sim = SharedSeason();
            var canvas = NewCanvas();

            // 시즌 중(올스타 · 월간 기본 탭)
            var midSeason = new GMLiveSeasonSimulator(NewLeague(team: "KIA", seed: 6061));
            midSeason.StartRun(GMRunMode.FirstHalf);
            midSeason.RunUntilStop();
            var view = NewView<GMAwardsCeremonyUIController>(canvas, "GM04_Awards");
            view.Build();
            view.Open(midSeason);
            Assert.AreEqual(GMAwardsTab.AllStarMonthly, view.CurrentTab, "시즌 중 = 올스타 · 월간");
            Assert.AreEqual(10 + 3 * 2, ActiveCells(view), "올스타 10 + 월간 3회 × 2");
            CheckLayer(view.Root, "시즌 중 올스타 · 월간");
            foreach (GMAwardsTab t in Enum.GetValues(typeof(GMAwardsTab)))
            {
                view.SelectTab(t);
                CheckLayer(view.Root, $"시즌 중 {t}");
            }
            Assert.IsFalse(view.Root.Find("NextSeasonButton").GetComponent<Button>().interactable, "시즌 중 전환 불가");

            // 시즌 종료(4개 탭)
            view.Open(sim);
            Assert.AreEqual(GMAwardsTab.KboCeremony, view.CurrentTab);
            Assert.AreEqual(28, ActiveCells(view), "KBO 시상식 28부문");
            StringAssert.Contains("MVP", T(view.Root, "Summary"));
            CheckLayer(view.Root, "11월 KBO 시상식");
            view.SelectTab(GMAwardsTab.GoldenGlove);
            CheckLayer(view.Root, "12월 골든글러브(개최 전/후)");
            view.HoldGoldenGlove();
            Assert.AreEqual(10, ActiveCells(view));
            CheckLayer(view.Root, "12월 골든글러브");
            view.SelectTab(GMAwardsTab.AllStarMonthly);
            Assert.AreEqual(10 + 12, ActiveCells(view), "올스타 10 + 월간 6회 × 2");
            CheckLayer(view.Root, "7월 올스타 · 월간");
            view.SelectTab(GMAwardsTab.Postseason);
            Assert.AreEqual(4 + 2 + 10, ActiveCells(view), "시리즈 4 + 우승 · KS MVP + 최종 순위 10");
            CheckLayer(view.Root, "포스트시즌 결과");
            Assert.IsTrue(view.Root.Find("NextSeasonButton").GetComponent<Button>().interactable);
            Assert.AreEqual("2027 시즌 전환", view.Root.Find("NextSeasonButton").GetComponentInChildren<Text>().text);

            // 대시보드(시즌 중 · 종료 후 버튼 교체)
            var dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM04_Dashboard");
            dash.Build();
            dash.Bind(midSeason);
            Assert.IsTrue(dash.Root.Find("AwardsReportButton").GetComponent<Button>().interactable, "시상 리포트(월간 · 올스타 이후)");
            CheckLayer(dash.Root, "대시보드 시즌 중");
            dash.Bind(sim);
            CheckLayer(dash.Root, "대시보드 시즌 종료");

            OpenScene();
            var sceneAwards = Object.FindAnyObjectByType<GMAwardsCeremonyUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(sceneAwards, "씬에 시상식 화면");
            Assert.IsFalse(sceneAwards.gameObject.activeSelf);
            Assert.IsNotNull(sceneAwards.transform.Find(GMAwardsCeremonyUIController.RootName));
            var sceneDash = Object.FindAnyObjectByType<GMLiveLeagueDashboardUIController>(FindObjectsInactive.Include);
            var dashRoot = sceneDash.transform.Find(GMLiveLeagueDashboardUIController.RootName);
            foreach (var n in new[] { "AwardsReportButton", "AwardsButton", "NextSeasonButton" }) Assert.IsNotNull(dashRoot.Find(n), n);
            var bold = SetupTask191.SceneTexts().Where(x => x.fontStyle == FontStyle.Bold || x.fontStyle == FontStyle.BoldAndItalic).Select(x => x.name).ToList();
            Assert.IsEmpty(bold, "씬 전체 FontStyle.Bold 0건");
            var scaler = Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include).First(s => s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize);
            Assert.AreEqual(new Vector2(1920, 1080), scaler.referenceResolution, "[TASK-GM-06] 1920×1080 Landscape");
        }

        private static int ActiveCells(GMAwardsCeremonyUIController view) =>
            Enumerable.Range(0, GMAwardsCeremonyUIController.CellCount).Count(i => view.Root.Find($"CellTitle{i}").gameObject.activeSelf);

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
