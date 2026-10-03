using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using NUnit.Framework;
using UnityEngine;

namespace KBOManager.EditorTests
{
    /// <summary>[TASK-KBO-181] 온보딩 규칙 · 라인업 탭 슬롯 배치 · 오토 라인업 그룹 폴백 · 선물 카드 DB 존재 검증(Window > General > Test Runner > EditMode).</summary>
    public class Task181Tests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
        }

        private PlayerTemplate Template(string id, Team team, Grade grade, int year, BatterPosition? position, PitcherRole? role, int level)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = id; t.RealPlayerId = id; t.PlayerName = id; t.Team = team; t.Grade = grade; t.SeasonYear = year;
            t.IsPitcher = role.HasValue;
            if (role.HasValue) { t.PitcherRole = role.Value; t.PitcherStats = StatProfiles.SpreadPitcher(level, role.Value, id.GetHashCode()); }
            else { t.BatterPosition = position ?? BatterPosition.DesignatedHitter; t.BatterStats = StatProfiles.SpreadBatter(level, t.BatterPosition, id.GetHashCode()); }
            return t;
        }

        private Player P(string id, BatterPosition? position, PitcherRole? role, int level, Team team = Team.Samsung, Grade grade = Grade.LIVE_NORMAL, int year = 2026) =>
            new Player(Guid.NewGuid().ToString(), Template(id, team, grade, year, position, role, level));

        /// <summary>2026 삼성 LIVE 실제 분포: 포지션 타자 8(DH 없음) + 여분 타자 12, 투수 SP6 · RP4 · CP1 · LR2(추격조 MR 0명).</summary>
        private List<Player> SamsungLikeInventory()
        {
            var list = new List<Player>();
            foreach (BatterPosition pos in Enum.GetValues(typeof(BatterPosition)))
            {
                if (pos == BatterPosition.DesignatedHitter) continue;
                list.Add(P($"B_{pos}_A", pos, null, 80));
                list.Add(P($"B_{pos}_B", pos, null, 60));
            }
            for (int i = 0; i < 4; i++) list.Add(P($"B_CF_X{i}", BatterPosition.CenterField, null, 55 + i));
            for (int i = 0; i < 6; i++) list.Add(P($"SP{i}", null, PitcherRole.StartingPitcher, 70 + i));
            for (int i = 0; i < 4; i++) list.Add(P($"RP{i}", null, PitcherRole.WinningReliever, 65 + i));
            list.Add(P("CP0", null, PitcherRole.Closer, 75));
            for (int i = 0; i < 2; i++) list.Add(P($"LR{i}", null, PitcherRole.LongReliever, 60 + i));
            return list;
        }

        [Test]
        public void GiftTemplateIds_AreTheFour2024GoldenGloveCards()
        {
            CollectionAssert.AreEqual(new[] { "SAMSUNG_2024_PLY_004038_GG", "KIA_2024_PLY_004368_GG", "NC_2024_PLY_004366_GG", "KT_2024_PLY_004369_GG" },
                OnboardingRules.GiftTemplateIds.ToArray());
        }

        [Test]
        public void GiftCards_ExistInCardsCsv_AsGoldenGlove2024()
        {
            var rows = Directory.GetFiles(Path.Combine(Application.dataPath, "Resources", "Data"), "cards_*.csv")
                .SelectMany(File.ReadAllLines).Select(l => l.Split(',')).Where(c => c.Length >= 10).ToList();
            foreach (var id in OnboardingRules.GiftTemplateIds)
            {
                var row = rows.FirstOrDefault(c => c[0].Trim().TrimStart('﻿') == id);
                Assert.IsNotNull(row, $"{id} 행이 없습니다");
                Assert.AreEqual("GOLDEN_GLOVE", row[3].Trim(), id);
                Assert.AreEqual("2024", row[9].Trim(), id);
            }
        }

        [Test]
        public void SelectStarterTemplates_KeepsOnlyTeam2026LiveNormal()
        {
            var all = new[]
            {
                Template("S_LN_26", Team.Samsung, Grade.LIVE_NORMAL, 2026, BatterPosition.Catcher, null, 60),
                Template("S_LN_25", Team.Samsung, Grade.LIVE_NORMAL, 2025, BatterPosition.Catcher, null, 60),
                Template("S_GG_26", Team.Samsung, Grade.GOLDEN_GLOVE, 2026, BatterPosition.Catcher, null, 90),
                Template("K_LN_26", Team.KIA, Grade.LIVE_NORMAL, 2026, BatterPosition.Catcher, null, 60),
            };
            var picked = OnboardingRules.SelectStarterTemplates(all, Team.Samsung);
            Assert.AreEqual(1, picked.Count);
            Assert.AreEqual("S_LN_26", picked[0].TemplateId);
            Assert.IsEmpty(OnboardingRules.SelectStarterTemplates(all, Team.None));
        }

        [Test]
        public void NormalizeNickname_TrimsAndLimits()
        {
            Assert.IsNull(OnboardingRules.NormalizeNickname("   "));
            Assert.AreEqual("라팍", OnboardingRules.NormalizeNickname("  라팍 "));
            Assert.AreEqual(OnboardingRules.NicknameMaxLength, OnboardingRules.NormalizeNickname(new string('가', 30)).Length);
        }

        [Test]
        public void AutoSetRoster_FillsPitcherSlotsWithPitchers_WhenRoleQuotaMissing()
        {
            var go = new GameObject("RosterManagerTest");
            created.Add(go);
            var manager = go.AddComponent<RosterManager>();
            var roster = manager.AutoSetRoster(SamsungLikeInventory(), Team.Samsung.ToString());

            Assert.AreEqual(GameManager.RequiredRosterSize, roster.Count);
            Assert.AreEqual(GameManager.RequiredBatterCount, roster.Count(p => !p.Template.IsPitcher), "타자 15");
            Assert.AreEqual(GameManager.RequiredPitcherCount, roster.Count(p => p.Template.IsPitcher), "투수 13 (추격조 MR 0명이어도 타자로 채우지 않음)");
        }

        [Test]
        public void LineupView_FillsDhAndBattingOrder_AndBenchSix()
        {
            var go = new GameObject("RosterManagerTest");
            created.Add(go);
            var roster = go.AddComponent<RosterManager>().AutoSetRoster(SamsungLikeInventory(), "Samsung");

            var lineup = LineupView.BuildLineup(roster);
            Assert.AreEqual(9, lineup.Count);
            Assert.IsTrue(lineup.All(e => e.Player != null), "주전 9칸 전부 채움");
            var dh = lineup.Last();
            Assert.AreEqual(BatterPosition.DesignatedHitter, dh.Position);
            Assert.IsTrue(dh.IsFill);
            Assert.AreEqual(9, dh.BattingOrder);
            CollectionAssert.AreEquivalent(Enumerable.Range(1, 9), lineup.Select(e => e.BattingOrder));
            Assert.AreEqual(RosterSlotLayout.SlotKind.BenchBatter, dh.ToPlacementSlot().Kind, "빈 DH 칸은 아무 타자나 배치");

            var bench = LineupView.BuildBench(roster);
            Assert.AreEqual(6, bench.Count);
            Assert.IsTrue(bench.All(e => e.Player != null && !e.IsExtra));
            Assert.AreEqual(15, lineup.Count + bench.Count);
        }

        [Test]
        public void LineupView_Pitchers_FiveStartersAndEightBullpen_NoEmptySlots()
        {
            var go = new GameObject("RosterManagerTest");
            created.Add(go);
            var roster = go.AddComponent<RosterManager>().AutoSetRoster(SamsungLikeInventory(), "Samsung");

            var (starters, bullpen) = LineupView.BuildPitchers(roster);
            Assert.AreEqual(5, starters.Count);
            Assert.IsTrue(starters.All(e => e.Player != null && e.Player.Template.PitcherRole == PitcherRole.StartingPitcher));
            Assert.AreEqual("1선발", starters[0].Header);
            Assert.GreaterOrEqual(starters[0].Player.CalculateOVR(false), starters[4].Player.CalculateOVR(false), "로테이션 = OVR 순");
            Assert.AreEqual(8, bullpen.Count, "승리조 2 + 추격조 4 + 롱 1 + 마무리 1, 추가 칸 없음");
            Assert.IsTrue(bullpen.All(e => e.Player != null));
            Assert.AreEqual("마무리", bullpen.Last().Header);
            Assert.AreEqual(PitcherRole.Closer, bullpen.Last().Player.Template.PitcherRole);
        }

        [Test]
        public void DescribeGiftPlacement_StartsHigherOvrGoldenGlove()
        {
            var inventory = SamsungLikeInventory();
            var gift = P("KIA_2024_PLY_004368_GG", BatterPosition.ThirdBase, null, 95, Team.KIA, Grade.GOLDEN_GLOVE, 2024);
            gift.Template.PlayerName = "김도영";
            inventory.Add(gift);
            var go = new GameObject("RosterManagerTest");
            created.Add(go);
            var roster = go.AddComponent<RosterManager>().AutoSetRoster(inventory, "Samsung");

            Assert.Contains(gift, roster);
            var note = OnboardingRules.DescribeGiftPlacement(roster, gift);
            StringAssert.Contains("김도영 '24", note);
            StringAssert.Contains("3B 주전 즉시 편성", note);
            StringAssert.Contains("보관함", OnboardingRules.DescribeGiftPlacement(new List<Player>(), gift));
        }
    }
}
