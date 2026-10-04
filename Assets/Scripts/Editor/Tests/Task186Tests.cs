using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.EditorTools;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-KBO-186] 선수 상세정보 4단 재구축 · 경기 결과 좌우(AWAY/HOME) 열 통일 + R/H/E/B 폭 + 투수 명판 · 가독성 패스 ·
    /// 라인업 선발(주전) ↔ 후보 / 선발 ↔ 불펜 맞교환 · 경기 템포 33% 단축.
    /// </summary>
    public class Task186Tests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private bool sceneOpened;
        private LineupAssignment previousActive;

        [SetUp]
        public void SetUp()
        {
            previousActive = LineupAssignment.Active;
            LineupAssignment.Active = null;
        }

        [TearDown]
        public void TearDown()
        {
            LineupAssignment.Active = previousActive;
            foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = false;
        }

        // ------------------------------------------------------------------ 도우미

        private Player Batter(string name, BatterPosition position, int level, Grade grade = Grade.LIVE_EPIC, int year = 2026)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = $"{name}_{Guid.NewGuid():N}";
            t.RealPlayerId = name;
            t.PlayerName = name;
            t.SeasonYear = year;
            t.Team = Team.Samsung;
            t.Grade = grade;
            t.BatterPosition = position;
            t.BatterStats = new BatterStats(level, level, level, level, level);
            return new Player(Guid.NewGuid().ToString(), t);
        }

        private Player Pitcher(string name, PitcherRole role, int level)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = $"{name}_{Guid.NewGuid():N}";
            t.RealPlayerId = name;
            t.PlayerName = name;
            t.SeasonYear = 2026;
            t.Team = Team.Samsung;
            t.Grade = Grade.LIVE_EPIC;
            t.IsPitcher = true;
            t.PitcherRole = role;
            t.PitcherStats = new PitcherStats(level, level, level, level, level);
            return new Player(Guid.NewGuid().ToString(), t);
        }

        /// <summary>포지션별 주전 9인(OVR 80) + 후보 3인(유격수 70 · 포수 65 · 좌익수 60).</summary>
        private List<Player> Roster(out List<Player> starters, out List<Player> bench)
        {
            starters = ((BatterPosition[])Enum.GetValues(typeof(BatterPosition))).Select(pos => Batter("S_" + pos, pos, 80)).ToList();
            bench = new List<Player> { Batter("B_SS", BatterPosition.ShortStop, 70), Batter("B_C", BatterPosition.Catcher, 65), Batter("B_LF", BatterPosition.LeftField, 60) };
            return starters.Concat(bench).ToList();
        }

        private static List<Player> StarterPlayers(IEnumerable<Player> roster, LineupAssignment a) =>
            LineupAssignment.AssignStarters(roster, a).Where(s => s.Player != null).Select(s => s.Player).ToList();

        // ------------------------------------------------------------------ D. 라인업 맞교환

        [Test]
        public void SwapBatters_StarterWithBench_IsOneToOne()
        {
            var roster = Roster(out var starters, out var bench);
            var assignment = new LineupAssignment();
            var catcher = starters[(int)BatterPosition.Catcher];
            var benchSs = bench[0];

            Assert.IsTrue(assignment.SwapBatters(roster, catcher, benchSs));
            var slots = LineupAssignment.AssignStarters(roster, assignment);
            Assert.AreEqual(benchSs, slots.First(s => s.Position == BatterPosition.Catcher).Player, "후보가 주전 포수 자리로");
            var now = StarterPlayers(roster, assignment);
            Assert.AreEqual(9, now.Count);
            Assert.AreEqual(9, now.Distinct().Count(), "중복 선수 없음");
            CollectionAssert.DoesNotContain(now, catcher, "기존 주전은 후보로");
            CollectionAssert.AreEquivalent(starters.Where(p => p != catcher).Append(benchSs), now, "다른 8칸은 그대로");
        }

        [Test]
        public void SwapBatters_BenchWithStarter_AndSecondSwapRestores()
        {
            var roster = Roster(out var starters, out var bench);
            var assignment = new LineupAssignment();
            var cf = starters[(int)BatterPosition.CenterField];
            Assert.IsTrue(assignment.SwapBatters(roster, bench[2], cf));
            Assert.AreEqual(bench[2], LineupAssignment.AssignStarters(roster, assignment).First(s => s.Position == BatterPosition.CenterField).Player);
            Assert.IsTrue(assignment.SwapBatters(roster, cf, bench[2]));
            CollectionAssert.AreEquivalent(starters, StarterPlayers(roster, assignment));
        }

        [Test]
        public void SwapBatters_TwoStarters_ExchangePositions_TwoBench_Rejected()
        {
            var roster = Roster(out var starters, out var bench);
            var assignment = new LineupAssignment();
            var c = starters[(int)BatterPosition.Catcher];
            var dh = starters[(int)BatterPosition.DesignatedHitter];
            Assert.IsTrue(assignment.SwapBatters(roster, c, dh));
            var slots = LineupAssignment.AssignStarters(roster, assignment);
            Assert.AreEqual(dh, slots.First(s => s.Position == BatterPosition.Catcher).Player);
            Assert.AreEqual(c, slots.First(s => s.Position == BatterPosition.DesignatedHitter).Player);
            Assert.IsFalse(assignment.SwapBatters(roster, bench[0], bench[1]), "후보끼리는 OVR 순 - 맞교환 대상 아님");
        }

        [Test]
        public void ActiveAssignment_FlowsIntoLineupViewSetDeckAndEngineOrder()
        {
            var roster = Roster(out var starters, out var bench);
            var assignment = new LineupAssignment();
            LineupAssignment.Active = assignment;
            var catcher = starters[(int)BatterPosition.Catcher];
            Assert.IsTrue(assignment.SwapBatters(roster, catcher, bench[1]));

            var lineup = LineupView.BuildLineup(roster, null);
            Assert.AreEqual(bench[1], lineup.First(e => e.Position == BatterPosition.Catcher).Player);
            CollectionAssert.Contains(LineupView.BuildBench(roster).Select(e => e.Player).ToList(), catcher);
            SetDeckEvaluator.ClassifyBatters(roster, out var setStarters, out var setBench);
            CollectionAssert.Contains(setStarters, bench[1]);
            CollectionAssert.Contains(setBench, catcher);
            var order = LineupAssignment.DefaultBattingOrder(roster);
            Assert.AreEqual(9, order.Count);
            CollectionAssert.Contains(order, bench[1]);
            CollectionAssert.DoesNotContain(order, catcher);
        }

        /// <summary>[TASK-KBO-187] 투수 맞교환은 13칸 자리 고정(PitcherSlots)으로 바뀌었다 - 같은 보직끼리도 순서 교환이 허용된다.</summary>
        [Test]
        public void SwapPitchers_RotationWithBullpen_ExchangesRoles()
        {
            var sp = Pitcher("SP", PitcherRole.StartingPitcher, 80);
            var cl = Pitcher("CL", PitcherRole.Closer, 75);
            var others = Enumerable.Range(0, 4).Select(i => Pitcher("SP" + i, PitcherRole.StartingPitcher, 70)).ToList();
            var roster = others.Append(sp).Append(cl).ToList();
            var assignment = new LineupAssignment();
            LineupAssignment.Active = assignment;

            Assert.IsTrue(assignment.SwapPitchers(roster, sp, cl));
            Assert.AreEqual(PitcherRole.Closer, LineupAssignment.RoleOf(sp));
            Assert.AreEqual(PitcherRole.StartingPitcher, LineupAssignment.RoleOf(cl));
            var (rotation, bullpen) = LineupView.BuildPitchers(roster);
            CollectionAssert.Contains(rotation.Select(e => e.Player).ToList(), cl);
            Assert.AreEqual(sp, bullpen.First(e => e.Group == "마무리").Player);
            Assert.IsTrue(assignment.SwapPitchers(roster, sp, cl));
            Assert.AreEqual(PitcherRole.StartingPitcher, LineupAssignment.RoleOf(sp), "다시 맞바꾸면 원래 자리");
            Assert.AreEqual(0, assignment.Roles.Count, "구 보직 핀은 쓰지 않는다(13칸 고정)");
        }

        [Test]
        public void LineupSwapCandidates_StarterShowsBench_BenchShowsStarters_WithBadges()
        {
            var roster = Roster(out var starters, out var bench);
            var forStarter = RosterSwapRules.GetLineupSwapCandidates(roster, starters[0]);
            CollectionAssert.AreEquivalent(bench, forStarter.Select(c => c.Player).ToList());
            Assert.IsTrue(forStarter.All(c => c.InRoster && c.Badge == RosterSwapRules.BenchSwapBadge && c.ScoreDelta == 0));

            var forBench = RosterSwapRules.GetLineupSwapCandidates(roster, bench[0]);
            CollectionAssert.AreEquivalent(starters, forBench.Select(c => c.Player).ToList());
            Assert.IsTrue(forBench.All(c => c.Badge == RosterSwapRules.StarterSwapBadge));
            StringAssert.Contains("위치 맞교환", RosterSwapRules.StarterSwapBadge);

            var sp = Pitcher("SP", PitcherRole.StartingPitcher, 80);
            var mu = Pitcher("MU", PitcherRole.MopUpReliever, 70);
            var pitchRoster = roster.Append(sp).Append(mu).ToList();
            var forSp = RosterSwapRules.GetLineupSwapCandidates(pitchRoster, sp);
            Assert.AreEqual(mu, forSp.Single().Player);
            Assert.AreEqual("현재 추격조 1 · 위치 맞교환", forSp.Single().Badge, "[TASK-187] 상대의 현재 자리를 배지에 표시");
        }

        [Test]
        public void Assignment_ReplaceIdAndSaveRoundTrip()
        {
            var roster = Roster(out var starters, out var bench);
            var assignment = new LineupAssignment();
            assignment.SwapBatters(roster, starters[0], bench[0]);
            var replacement = Batter("NEW", BatterPosition.ShortStop, 72);
            assignment.ReplaceId(bench[0].InstanceId, replacement.InstanceId);
            Assert.IsTrue(assignment.Starters.Any(p => p.InstanceId == replacement.InstanceId && p.Position == BatterPosition.Catcher));

            var data = new GameSaveData();
            data.Lineup.CopyFrom(assignment);
            var back = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(data));
            Assert.AreEqual(assignment.Starters.Count, back.Lineup.Starters.Count);
            Assert.GreaterOrEqual(back.SaveVersion, 11);
            Assert.IsNotNull(JsonUtility.FromJson<GameSaveData>("{\"SaveVersion\":10}").Lineup, "구버전 세이브 = 빈 지정");
        }

        // ------------------------------------------------------------------ E. 경기 템포

        [Test]
        public void MatchTempo_ShortensPlaybackBy30To35Percent()
        {
            Assert.That(1f - MatchTempo.Scale, Is.InRange(0.30f, 0.35f));
            Assert.AreEqual(0.335f, MatchTempo.Scaled(0.5f), 0.001f);
            Assert.Less(MatchTempo.ArcSeconds, MatchTempo.LegacyArcSeconds * 0.7f);
            Assert.Less(MatchTempo.PitchSeconds, MatchTempo.LegacyPitchSeconds * 0.7f);
        }

        // ------------------------------------------------------------------ B. 결과 화면 좌우 열

        [Test]
        public void ResultColumns_LeftIsAway_RightIsHome()
        {
            var win = Pitcher("W", PitcherRole.StartingPitcher, 80);
            var lose = Pitcher("L", PitcherRole.StartingPitcher, 70);
            // AWAY 4 : HOME 25 → HOME 승(verdict 1): 좌측(AWAY) = 패전 투수 · 회색, 우측(HOME) = 승리 투수 · 파랑
            Assert.IsFalse(ResultColumns.LeftWins(1));
            Assert.IsTrue(ResultColumns.RightWins(1));
            Assert.AreEqual(lose, ResultColumns.PitcherFor(true, 1, win, lose));
            Assert.AreEqual(win, ResultColumns.PitcherFor(false, 1, win, lose));
            Assert.AreEqual(win, ResultColumns.PitcherFor(true, -1, win, lose));
            Assert.AreEqual(lose, ResultColumns.PitcherFor(false, -1, win, lose));
            Assert.AreEqual("시즌 1승 0패", ResultColumns.SeasonRecord(1, 0));
        }

        // ------------------------------------------------------------------ A. 선수 상세정보

        [Test]
        public void DetailRules_FormatHeaderOvrGrowthAndStats()
        {
            var p = Batter("구자욱", BatterPosition.LeftField, 80, Grade.GOLDEN_GLOVE, 2024);
            p.ReinforceLevel = 3;
            Assert.AreEqual("구자욱 '24", PlayerDetailRules.Title(p));
            StringAssert.Contains("좌익수", PlayerDetailRules.Subtitle(p));
            StringAssert.Contains(CardGrowthRules.DisplayName(Grade.GOLDEN_GLOVE), PlayerDetailRules.Subtitle(p));
            Assert.AreEqual(p.BaseOvr + p.GetStatGrowth() + 4, PlayerDetailRules.FinalOvr(p, 4));
            Assert.AreEqual($"기본 {p.BaseOvr} + 성장 +{p.GetStatGrowth()} + 세트덱 +4", PlayerDetailRules.OvrBreakdown(p, 4));
            StringAssert.StartsWith("세트덱 스코어: SD ", PlayerDetailRules.SetDeckBadge(p));

            var cells = PlayerDetailRules.GrowthCells(p);
            CollectionAssert.AreEqual(new[] { "강화", "각성", "한계돌파", "특훈" }, cells.Select(c => c.Title).ToArray());
            Assert.AreEqual("+3 / 10강", cells[0].Value);
            StringAssert.Contains("/ 9각 (초월", cells[1].Value);

            var rows = PlayerDetailRules.StatRows(p, 4);
            CollectionAssert.AreEqual(new[] { "파워", "정확", "선구", "주력", "수비" }, rows.Select(r => r.Label).ToArray());
            Assert.IsTrue(rows.All(r => r.Base == 80 && r.Bonus == p.GetStatGrowth() + 4 && r.Final == r.Base + r.Bonus));
            CollectionAssert.AreEqual(new[] { "구위", "구속", "변화", "제구", "체력" },
                PlayerDetailRules.StatRows(Pitcher("P", PitcherRole.Closer, 70), 0).Select(r => r.Label).ToArray());

            Assert.AreEqual(PlayerDetailRules.BarTier.Elite, PlayerDetailRules.TierOf(100));
            Assert.AreEqual(PlayerDetailRules.BarTier.Blue, PlayerDetailRules.TierOf(90));
            Assert.AreEqual(PlayerDetailRules.BarTier.Green, PlayerDetailRules.TierOf(80));
            Assert.AreEqual(PlayerDetailRules.BarTier.Sky, PlayerDetailRules.TierOf(79));
            Assert.Greater(PlayerDetailRules.FillRatio(120), PlayerDetailRules.FillRatio(80));
        }

        [Test]
        public void DetailLayout_HidesLegacy_UsesTieredFonts_NoOverlappingText()
        {
            var canvasGo = new GameObject("TestCanvas186", typeof(RectTransform), typeof(Canvas));
            created.Add(canvasGo);
            var panel = CompyaUiKit.Fill(canvasGo.transform, "Panel");
            var legacy = CompyaUiKit.Fill(panel, "LegacyStatsContent");

            var layout = new PlayerDetailLayout186(null, null);
            layout.Build(panel, null);
            Assert.IsFalse(legacy.gameObject.activeSelf, "구 탭형 자식 숨김");
            Assert.AreEqual(layout.Root, panel.Find(PlayerDetailLayout186.RootName));

            layout.Fill(Batter("구자욱", BatterPosition.LeftField, 95), 3, null);
            var root = layout.Root;
            // [TASK-KBO-191] 계층 크기(Normal): 타이틀 26 · 메인 OVR 40 · 능력치 18 · 버튼 20 · 닫기 24
            Assert.AreEqual(26, root.Find("Title").GetComponent<Text>().fontSize);
            Assert.AreEqual(40, root.Find("Ovr").GetComponent<Text>().fontSize);
            Assert.AreEqual(18, root.Find("StatValue0").GetComponent<Text>().fontSize);
            Assert.AreEqual(20, layout.ManageButton.GetComponentInChildren<Text>().fontSize);
            Assert.AreEqual(24, layout.CloseX.GetComponentInChildren<Text>().fontSize);
            Assert.IsTrue(root.GetComponentsInChildren<Text>(true).All(t => t.fontStyle == FontStyle.Normal), "Bold 해제");
            var x = (RectTransform)layout.CloseX.transform;
            Assert.AreEqual(80f, (x.anchorMax.x - x.anchorMin.x) * PlayerDetailLayout186.RefW, 0.5f);
            Assert.AreEqual("파워", root.Find("StatName0").GetComponent<Text>().text);
            StringAssert.StartsWith("OVR ", root.Find("Ovr").GetComponent<Text>().text);

            var texts = root.GetComponentsInChildren<Text>(true).Where(t => t.transform.parent == root).ToList();
            Assert.IsTrue(texts.All(t => t.fontSize >= 15 && t.fontSize <= 42), "[TASK-KBO-191] 상세정보 텍스트 15~42pt");
            Assert.IsTrue(texts.All(t => t.resizeTextMinSize >= PlayerDetailLayout186.MinFont));
            var boxes = texts.Select(t => (RectTransform)t.transform).ToList();
            for (int i = 0; i < boxes.Count; i++)
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i]; var b = boxes[j];
                    float w = Mathf.Min(a.anchorMax.x, b.anchorMax.x) - Mathf.Max(a.anchorMin.x, b.anchorMin.x);
                    float h = Mathf.Min(a.anchorMax.y, b.anchorMax.y) - Mathf.Max(a.anchorMin.y, b.anchorMin.y);
                    Assert.IsFalse(w > 0.001f && h > 0.001f, $"텍스트 겹침: {a.name} ↔ {b.name}");
                }
        }

        // ------------------------------------------------------------------ C. 가독성 패스

        /// <summary>[TASK-KBO-191] 가독성 패스 = 확대가 아니라 정리 - Bold 해제 · 과대 크기 계층 축소 · 작은 글씨 유지 · 멱등.</summary>
        [Test]
        public void ReadableFontPass_TidiesBoldAndOversizedText_KeepsSmallText()
        {
            var root = new GameObject("FontRoot186", typeof(RectTransform));
            created.Add(root);
            Text Make(string n, int size, FontStyle style)
            {
                var t = CompyaUiKit.Fill(root.transform, n).gameObject.AddComponent<Text>();
                t.fontSize = size;
                t.fontStyle = style;
                return t;
            }
            var body = Make("Body", 14, FontStyle.Normal);
            var boldStat = Make("Stat", 16, FontStyle.Bold);
            var big = Make("Big", 30, FontStyle.Normal);
            var fitSmall = Make("Fit", 40, FontStyle.Normal);
            fitSmall.resizeTextForBestFit = true;
            fitSmall.resizeTextMaxSize = 12;

            Assert.AreEqual(4, ReadableFontPass.Apply(root.transform));
            Assert.AreEqual(14, body.fontSize, "작은 글씨는 그대로");
            Assert.AreEqual(16, boldStat.fontSize);
            Assert.AreEqual(FontStyle.Normal, boldStat.fontStyle, "Bold 해제");
            Assert.AreEqual(TextTidy.Tier(30, false), big.fontSize);
            Assert.Less(big.fontSize, 30, "과대 글씨 축소");
            Assert.AreEqual(12, fitSmall.resizeTextMaxSize);
            Assert.AreEqual(0, ReadableFontPass.Apply(root.transform), "두 번째 실행은 변화 없음");
        }

        // ------------------------------------------------------------------ 씬 배선

        [Test]
        public void Scene_DetailLayoutResultColumnsAndFontPassApplied()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
            SetupTask186.ApplyAll();

            var detail = UnityEngine.Object.FindAnyObjectByType<PlayerDetailUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(detail);
            var panel = new SerializedObject(detail).FindProperty("panelRoot").objectReferenceValue as GameObject;
            Assert.IsNotNull(panel);
            var layout = panel.transform.Find(PlayerDetailLayout186.RootName);
            Assert.IsNotNull(layout, "Detail186 빌드");
            Assert.IsTrue(panel.transform.Cast<Transform>().Where(t => t != layout && !detail.transform.IsChildOf(t)).All(t => !t.gameObject.activeSelf), "구 탭형 자식 숨김");

            var roots = SetupTask186.FontPassRoots();
            Assert.GreaterOrEqual(roots.Count, 4);
            Assert.IsTrue(roots.All(r => r.GetComponent<ReadableFontPass>() != null));

            var view = UnityEngine.Object.FindAnyObjectByType<CompyaMatchView>(FindObjectsInactive.Include);
            Assert.IsNotNull(view);
            view.Build();
            var result = view.GetComponentsInChildren<Transform>(true).First(t => t.name == "Result1");
            float CenterX(string n) { var r = (RectTransform)result.Find(n); return (r.anchorMin.x + r.anchorMax.x) / 2f; }
            Assert.Less(CenterX("AwayPitcherPanel"), 0.5f, "AWAY 투수 = 좌측 열");
            Assert.Greater(CenterX("HomePitcherPanel"), 0.5f, "HOME 투수 = 우측 열");
            Assert.Less(CenterX("AwayCard"), 0.5f);
            var ar0 = (RectTransform)result.Find("AR0");
            Assert.GreaterOrEqual((ar0.anchorMax.x - ar0.anchorMin.x) * CompyaUiKit.RefWidth, 60f, "R/H/E/B 칸 폭 확장");
            Assert.AreEqual(12, ar0.GetComponent<Text>().resizeTextMinSize, "[TASK-KBO-191] 자동 크기 최소 12");
            var awayRecord = (RectTransform)result.Find("AwayRecord");
            var awayName = (RectTransform)result.Find("AwayPitcherName");
            var homeRecord = (RectTransform)result.Find("HomeRecord");
            // [TASK-KBO-187] 기록은 이름 아래 줄, 각 열 안쪽에만(AWAY 열 < HOME 열) - 가운데에서 만나지 않는다.
            Assert.LessOrEqual(awayRecord.anchorMax.y, awayName.anchorMin.y + 0.0001f, "기록 = 이름 아래 줄");
            Assert.LessOrEqual(awayRecord.anchorMax.x, homeRecord.anchorMin.x + 0.0001f, "W-L 기록 칸 겹침 없음");
        }
    }
}
