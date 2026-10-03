using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using NUnit.Framework;
using UnityEngine;

namespace KBOManager.EditorTests
{
    /// <summary>[TASK-KBO-182] 스카우트 등급표(SIG 제외 · GG 편입) · 선택 구단 우선 자동 편성 · 동일 인물 중복 금지 · 타순 변경 · 세트덱 A/B 집계.</summary>
    public class Task182Tests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
        }

        private Player P(string id, Team team, BatterPosition? position, PitcherRole? role, int level, string person = null, Grade grade = Grade.LIVE_NORMAL)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = id; t.RealPlayerId = person ?? id; t.PlayerName = id; t.Team = team; t.Grade = grade; t.SeasonYear = 2026;
            t.IsPitcher = role.HasValue;
            if (role.HasValue) { t.PitcherRole = role.Value; t.PitcherStats = StatProfiles.SpreadPitcher(level, role.Value, id.GetHashCode()); }
            else { t.BatterPosition = position ?? BatterPosition.DesignatedHitter; t.BatterStats = StatProfiles.SpreadBatter(level, t.BatterPosition, id.GetHashCode()); }
            return new Player(Guid.NewGuid().ToString(), t);
        }

        /// <summary>DH를 제외한 포지션 8 + 여분 타자 8 + 보직 쿼터 투수 13.</summary>
        private List<Player> Team28(Team team, int level, bool withCatcher = true)
        {
            var list = new List<Player>();
            foreach (BatterPosition pos in Enum.GetValues(typeof(BatterPosition)))
            {
                if (pos == BatterPosition.DesignatedHitter || (!withCatcher && pos == BatterPosition.Catcher)) continue;
                list.Add(P($"{team}_{pos}", team, pos, null, level));
            }
            for (int i = 0; i < 8; i++) list.Add(P($"{team}_EXTRA{i}", team, BatterPosition.LeftField, null, level - 5));
            foreach (var (role, count) in RosterSlotLayout.PitcherRoleQuota)
                for (int i = 0; i < count; i++) list.Add(P($"{team}_{role}{i}", team, null, role, level));
            return list;
        }

        private List<Player> Auto(List<Player> inventory, string team)
        {
            var go = new GameObject("RosterManager182");
            created.Add(go);
            return go.AddComponent<RosterManager>().AutoSetRoster(inventory, team);
        }

        [Test]
        public void ScoutTables_ExcludeTerminalGrades_AndTopIsGoldenGlove()
        {
            var gg = ScoutDropTables.GoldenGlove;
            CollectionAssert.AreEqual(new[] { Grade.GOLDEN_GLOVE, Grade.TITLE_HOLDER, Grade.FRANCHISE, Grade.ALLSTAR, Grade.LIVE_EPIC }, gg.Select(e => e.Grade).ToArray());
            CollectionAssert.AreEqual(new[] { 0.5f, 1.5f, 2.0f, 3.0f, 93.0f }, gg.Select(e => e.RatePercent).ToArray());
            Assert.AreEqual(100f, ScoutDropTables.Total(gg), 0.0001f);
            Assert.AreEqual(100f, ScoutDropTables.Total(ScoutDropTables.TitleHolder), 0.0001f);
            foreach (var entry in gg.Concat(ScoutDropTables.TitleHolder))
                Assert.IsFalse(ScoutDropTables.IsExcludedFromScout(entry.Grade), $"{entry.Grade}는 상시 스카우트에 나오면 안 됩니다");
            CollectionAssert.AreEquivalent(new[] { Grade.SIGNATURE, Grade.DYNASTY, Grade.RETIRED_NUMBER }, ScoutDropTables.ExcludedFromScout.ToArray());
        }

        [Test]
        public void AutoRoster_PrefersSelectedTeam_OverHigherOvrOtherTeam()
        {
            var inventory = Team28(Team.Samsung, 60);
            var kia3B = P("KIA_3B_STAR", Team.KIA, BatterPosition.ThirdBase, null, 95);
            var kiaSp = P("KIA_SP_STAR", Team.KIA, null, PitcherRole.StartingPitcher, 95);
            inventory.Add(kia3B);
            inventory.Add(kiaSp);

            var roster = Auto(inventory, "Samsung");
            Assert.AreEqual(28, roster.Count);
            Assert.IsTrue(roster.All(p => p.Template.Team == Team.Samsung), "선택 구단 선수만으로 28인을 채울 수 있으면 타 구단은 들어가지 않는다");
            CollectionAssert.DoesNotContain(roster, kia3B);
            CollectionAssert.DoesNotContain(roster, kiaSp);
        }

        [Test]
        public void AutoRoster_UsesOtherTeam_OnlyForMissingPosition()
        {
            var inventory = Team28(Team.Samsung, 60, withCatcher: false);
            var lgCatcher = P("LG_C", Team.LG, BatterPosition.Catcher, null, 70);
            var lgFirst = P("LG_1B", Team.LG, BatterPosition.FirstBase, null, 99);
            inventory.Add(lgCatcher);
            inventory.Add(lgFirst);

            var roster = Auto(inventory, "Samsung");
            CollectionAssert.Contains(roster, lgCatcher, "선택 구단에 포수가 없으면 타 구단 포수를 차선책으로 쓴다");
            CollectionAssert.DoesNotContain(roster, lgFirst, "선택 구단 1루수가 있으면 OVR이 더 높아도 타 구단 1루수는 쓰지 않는다");
            Assert.AreEqual(15, roster.Count(p => !p.Template.IsPitcher));
            Assert.AreEqual(13, roster.Count(p => p.Template.IsPitcher));
        }

        [Test]
        public void AutoRoster_NeverTakesTwoCardsOfSamePerson()
        {
            var inventory = Team28(Team.Samsung, 60);
            var live = P("SAMSUNG_2026_KOO_LN", Team.Samsung, BatterPosition.RightField, null, 80, person: "PLY_004038");
            var gg = P("SAMSUNG_2024_KOO_GG", Team.Samsung, BatterPosition.RightField, null, 99, person: "PLY_004038", grade: Grade.GOLDEN_GLOVE);
            inventory.Add(live);
            inventory.Add(gg);

            var roster = Auto(inventory, "Samsung");
            Assert.AreEqual(1, roster.Count(p => p.Template.RealPlayerId == "PLY_004038"), "같은 player_id 카드는 1장만");
            CollectionAssert.Contains(roster, gg, "OVR이 높은 카드가 남는다");
            Assert.AreEqual(roster.Count, roster.Select(RosterSwapRules.PersonKey).Distinct().Count());
        }

        [Test]
        public void ManualSwap_RejectsSecondCardOfSamePerson()
        {
            var inventory = Team28(Team.Samsung, 60);
            var koo = P("SAMSUNG_KOO_LN", Team.Samsung, BatterPosition.RightField, null, 99, person: "PLY_004038");
            inventory.Add(koo);
            var roster = Auto(inventory, "Samsung");
            Assert.Contains(koo, roster);

            var kooGg = P("SAMSUNG_KOO_GG", Team.Samsung, BatterPosition.LeftField, null, 99, person: "PLY_004038", grade: Grade.GOLDEN_GLOVE);
            var benchBatter = roster.First(p => RosterSwapRules.IsBenchBatter(roster, p));
            Assert.IsFalse(RosterSwapRules.CanSwap(roster, benchBatter, kooGg), "다른 슬롯에 같은 인물 카드를 넣을 수 없다");
            var kooOther = P("SAMSUNG_KOO_RF2", Team.Samsung, BatterPosition.RightField, null, 70, person: "PLY_004038");
            Assert.IsTrue(RosterSwapRules.CanSwap(roster, koo, kooOther), "같은 인물 카드끼리 맞바꾸는 것은 허용");
        }

        [Test]
        public void LineupOrder_SwapAndApply()
        {
            var a = P("A", Team.Samsung, BatterPosition.Catcher, null, 60);
            var b = P("B", Team.Samsung, BatterPosition.FirstBase, null, 60);
            var c = P("C", Team.Samsung, BatterPosition.SecondBase, null, 60);
            var d = P("D", Team.Samsung, BatterPosition.ThirdBase, null, 60);
            var order = new List<Player> { a, b, c };
            var ids = LineupOrder.Swap(order, a, c);
            CollectionAssert.AreEqual(new[] { c, b, a }, LineupOrder.Apply(order, ids));
            CollectionAssert.AreEqual(new[] { c, b, a, d }, LineupOrder.Apply(new List<Player> { a, b, c, d }, ids), "지정 목록 밖 선수는 뒤에 기본 순서로");
            CollectionAssert.AreEqual(order, LineupOrder.Apply(order, null));
        }

        [Test]
        public void SetDeckChoices_CountBothAAndB()
        {
            var selection = new SetDeckSelection();
            var thresholds = SetDeckBuffTable.SelectableBrackets.Select(b => b.Threshold).ToList();
            Assert.AreEqual((thresholds.Count, 0), SetDeckOptionUIController.CountChoices(selection, thresholds));
            selection.SetOption(thresholds[0], true);
            selection.SetOption(thresholds[1], true);
            selection.SetOption(thresholds[1], false);
            Assert.AreEqual((thresholds.Count - 1, 1), SetDeckOptionUIController.CountChoices(selection, thresholds));
        }

        [Test]
        public void GaugeWindow_StaysInsideRange()
        {
            Assert.AreEqual(175, RosterUIController.GaugeWindow(200).Start);
            CollectionAssert.AreEqual(new[] { 175, 180, 185, 190, 195, 200 }, RosterUIController.GaugeWindow(199).Markers);
            Assert.AreEqual(35, RosterUIController.GaugeWindow(10).Start);
            Assert.AreEqual(150, RosterUIController.GaugeWindow(152).Start);
        }
    }
}
