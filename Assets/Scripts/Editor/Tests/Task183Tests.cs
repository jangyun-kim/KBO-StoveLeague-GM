using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KBOManager.Data;
using KBOManager.EditorTools;
using KBOManager.Managers;
using KBOManager.Models;
using NUnit.Framework;
using UnityEngine;

namespace KBOManager.EditorTests
{
    /// <summary>[TASK-KBO-183] 12단계 리그(57~144) · 'OVR 7 격차 법칙' · 시즌 등급별 기본 OVR 리밸런싱 · 4대 성장(강화/한계 돌파/특훈/각성) · 최종 144.</summary>
    public class Task183Tests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private static PlayerDatabase sharedDb;

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
            Task183Report.CleanupTemp();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (sharedDb != null) UnityEngine.Object.DestroyImmediate(sharedDb.gameObject);
            sharedDb = null;
        }

        private static PlayerDatabase Db
        {
            get
            {
                if (sharedDb == null) sharedDb = new GameObject("Task183Db").AddComponent<PlayerDatabase>();
                return sharedDb;
            }
        }

        private Player Card(Grade grade, int level, bool pitcher = false, string person = null)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = Guid.NewGuid().ToString("N"); t.RealPlayerId = person ?? t.TemplateId; t.PlayerName = t.TemplateId;
            t.Team = Team.Samsung; t.Grade = grade; t.SeasonYear = 2026; t.IsPitcher = pitcher;
            if (pitcher) { t.PitcherRole = PitcherRole.StartingPitcher; t.PitcherStats = StatProfiles.SpreadPitcher(level, PitcherRole.StartingPitcher, 7); }
            else { t.BatterPosition = BatterPosition.RightField; t.BatterStats = StatProfiles.SpreadBatter(level, BatterPosition.RightField, 7); }
            return new Player(Guid.NewGuid().ToString(), t);
        }

        // ------------------------------------------------------------------ 요구사항 A: 12단계 리그

        [Test]
        public void LeagueTable_TwelveTiers_Amateur57_To_HallOfFame144_Step8_Width7()
        {
            var all = LeagueTierTable.All;
            Assert.AreEqual(12, all.Count);
            Assert.AreEqual((57, 64), (LeagueTierTable.Get(LeagueTier.Amateur).MinOvr, LeagueTierTable.Get(LeagueTier.Amateur).MaxOvr));
            Assert.AreEqual((65, 72), (LeagueTierTable.Get(LeagueTier.Futures).MinOvr, LeagueTierTable.Get(LeagueTier.Futures).MaxOvr));
            Assert.AreEqual((97, 104), (LeagueTierTable.Get(LeagueTier.Franchise).MinOvr, LeagueTierTable.Get(LeagueTier.Franchise).MaxOvr));
            Assert.AreEqual((129, 136), (LeagueTierTable.Get(LeagueTier.SignatureDynasty).MinOvr, LeagueTierTable.Get(LeagueTier.SignatureDynasty).MaxOvr));
            Assert.AreEqual((137, 144), (LeagueTierTable.Get(LeagueTier.HallOfFame).MinOvr, LeagueTierTable.Get(LeagueTier.HallOfFame).MaxOvr));
            Assert.AreEqual(56, LeagueTierTable.Get(LeagueTier.Rookie).MaxOvr, "루키 ~56");
            for (int i = 1; i < all.Count; i++)
            {
                Assert.AreEqual(7, all[i].MaxOvr - all[i].MinOvr, all[i].Name);
                if (i > 1) Assert.AreEqual(8, all[i].MinOvr - all[i - 1].MinOvr, all[i].Name);
            }
            Assert.AreEqual(LeagueTier.Amateur, LeagueTierTable.RecommendedFor(61));
            Assert.AreEqual(LeagueTier.Rookie, LeagueTierTable.RecommendedFor(56));
            Assert.AreEqual(LeagueTier.HallOfFame, LeagueTierTable.RecommendedFor(144));
            Assert.AreEqual("프랜차이즈 리그", LeagueTierTable.DisplayName(LeagueTier.Franchise));
        }

        [Test]
        public void AiTargets_NineTeams_SpreadLowMidBoss_WithinTierBand()
        {
            foreach (var info in LeagueTierTable.All)
            {
                var targets = LeagueTierTable.AiTeamOvrTargets(info.Tier);
                Assert.AreEqual(9, targets.Length);
                Assert.IsTrue(targets.All(info.Contains), info.Name);
                Assert.AreEqual(3, targets.Count(t => t <= info.MinOvr + 1), $"{info.Name} 하위권");
                Assert.AreEqual(4, targets.Count(t => t >= info.MinOvr + 2 && t <= info.MinOvr + 4), $"{info.Name} 중위권");
                Assert.AreEqual(2, targets.Count(t => t >= info.MinOvr + 6), $"{info.Name} 보스");
            }
        }

        [Test]
        public void AiRoster_NormalizesExactlyToTargetTeamOvr()
        {
            foreach (int target in new[] { 57, 64, 100, 144 })
            {
                var roster = Task183Report.ProceduralTeam(Team.LG, target - 3, "N");
                LeagueManager.NormalizeRosterToTeamOvr(roster, target);
                Assert.AreEqual(target, TeamOvrCalculator.Calculate(roster).Total, $"목표 {target}");
            }
        }

        [Test]
        public void GapLaw_BonusOnlyFromGap8_AndGrowsWithGap()
        {
            Assert.AreEqual(0, OvrGapLaw.ClassAdvantageBonus(68, 61), "Δ7 = 가변 승부");
            Assert.AreEqual(0, OvrGapLaw.ClassAdvantageBonus(61, 69), "약팀은 보정 없음");
            Assert.AreEqual(OvrGapLaw.EntryStatBonus, OvrGapLaw.ClassAdvantageBonus(69, 61), "Δ8 진입");
            Assert.Greater(OvrGapLaw.ClassAdvantageBonus(72, 61), OvrGapLaw.ClassAdvantageBonus(69, 61));
            Assert.IsFalse(OvrGapLaw.IsDecisive(7));
            Assert.IsTrue(OvrGapLaw.IsDecisive(-8));
        }

        [Test]
        public void GapLaw_Simulation_Gap7IsVariable_Gap8IsDecisive()
        {
            const int games = 300;
            var (fav7, _) = Task183Report.SimulateGap(61, 7, games, 7001);
            var (fav8, _) = Task183Report.SimulateGap(61, 8, games, 8001);
            var (under7, _) = Task183Report.SimulateUnderdogWithTools(61, 7, games, 7002);
            var (under8, _) = Task183Report.SimulateUnderdogWithTools(61, 8, games, 8002);
            TestContext.WriteLine($"Δ7 상위 {fav7:P1} / 약팀+도구 {under7:P1} | Δ8 상위 {fav8:P1} / 약팀+도구 {under8:P1}");

            Assert.Less(fav7, 0.82, "Δ7 이내는 상위 구단이 확정적이지 않다");
            Assert.GreaterOrEqual(under7, 0.30, "Δ7 약팀은 컨디션·치어리더·홈으로 30% 이상 이긴다");
            Assert.GreaterOrEqual(fav8, 0.85, "Δ8 이상은 상위 구단이 확실한 체급 우위");
            Assert.LessOrEqual(under8, 0.25, "Δ8 약팀은 도구를 다 써도 25% 이하");
        }

        // ------------------------------------------------------------------ 요구사항 B: 등급별 기본 OVR (실제 DB)

        [Test]
        public void Database_EveryCard_BaseOvrWithinGradeBand_AndNoFlatStats()
        {
            var all = Db.AllTemplates;
            Assert.Greater(all.Count, 1600);
            foreach (var t in all)
            {
                var band = CardGrowthRules.BaseOvrRange(t.Grade);
                int ovr = t.GetBaseOverall();
                Assert.That(ovr, Is.InRange(band.Min, band.Max), $"{t.TemplateId} {t.Grade} {ovr}");
                int distinct = t.IsPitcher
                    ? new[] { t.PitcherStats.Stuff, t.PitcherStats.Velocity, t.PitcherStats.Movement, t.PitcherStats.Control, t.PitcherStats.Stamina }.Distinct().Count()
                    : new[] { t.BatterStats.Power, t.BatterStats.Contact, t.BatterStats.Discipline, t.BatterStats.Speed, t.BatterStats.Defense }.Distinct().Count();
                Assert.Greater(distinct, 1, $"{t.TemplateId} 세부 스탯 5개 동일");
            }
            foreach (var grade in new[] { Grade.LIVE_NORMAL, Grade.LIVE_EPIC, Grade.ALLSTAR, Grade.FRANCHISE, Grade.TITLE_HOLDER, Grade.GOLDEN_GLOVE })
            {
                var band = CardGrowthRules.BaseOvrRange(grade);
                var ovrs = all.Where(t => t.Grade == grade).Select(t => t.GetBaseOverall()).ToList();
                Assert.AreEqual(band.Min, ovrs.Min(), $"{grade} 하한 사용");
                Assert.AreEqual(band.Max, ovrs.Max(), $"{grade} 상한 사용");
            }
        }

        [Test]
        public void Database_GiftCards89To91_AndLegendTop102()
        {
            foreach (var id in OnboardingRules.GiftTemplateIds)
            {
                var t = Db.GetTemplateById(id);
                Assert.IsNotNull(t, id);
                Assert.That(t.GetBaseOverall(), Is.InRange(89, 91), id);
            }
            Assert.AreEqual(102, Db.GetTemplateById("SAMSUNG_1999_PLY_000421_RN").GetBaseOverall(), "이승엽'99 RN");
            Assert.AreEqual(102, Db.AllTemplates.Max(t => t.GetBaseOverall()));
        }

        [Test]
        public void Database_SamePlayer_GradeOrder_LiveBelowEpicBelowGoldenGlove()
        {
            var byPerson = Db.AllTemplates.GroupBy(t => t.RealPlayerId);
            int checkedPeople = 0;
            foreach (var g in byPerson)
            {
                var live = g.Where(t => t.Grade == Grade.LIVE_NORMAL).Select(t => t.GetBaseOverall()).ToList();
                var epic = g.Where(t => t.Grade == Grade.LIVE_EPIC).Select(t => t.GetBaseOverall()).ToList();
                var top = g.Where(t => CardGrowthRules.PowerRank(t.Grade) == CardGrowthRules.TopTierPowerRank).Select(t => t.GetBaseOverall()).ToList();
                if (live.Count > 0 && epic.Count > 0) { Assert.Less(live.Max(), epic.Min(), g.Key); checkedPeople++; }
                if (epic.Count > 0 && top.Count > 0) Assert.Less(epic.Max(), top.Min(), g.Key);
            }
            Assert.Greater(checkedPeople, 50);
        }

        [Test]
        public void CardsCsv_CardIdsUnique_SamsungNamesakesSplit()
        {
            var ids = Directory.GetFiles(Path.Combine(Application.dataPath, "Resources", "Data"), "cards_*.csv")
                .SelectMany(f => File.ReadAllLines(f).Skip(1)).Where(l => l.Trim().Length > 0).Select(l => l.Split(',')[0].Trim()).ToList();
            var dupes = ids.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            CollectionAssert.IsEmpty(dupes, string.Join(",", dupes));
            CollectionAssert.Contains(ids, "SAMSUNG_2026_PLY_004087_LN");
            CollectionAssert.Contains(ids, "SAMSUNG_2026_PLY_004094_LN");
            CollectionAssert.Contains(ids, "SAMSUNG_2026_PLY_900002_LN");
            CollectionAssert.Contains(ids, "SAMSUNG_2026_PLY_900003_LN");
            CollectionAssert.Contains(ids, "SAMSUNG_2024_PLY_004038_GG", "구자욱 ID 보존");
        }

        [Test]
        public void Onboarding_AllTenTeams_TeamOvr60To62_AmateurLeague()
        {
            var rosterGo = new GameObject("Task183Roster");
            created.Add(rosterGo);
            var rosterManager = rosterGo.AddComponent<RosterManager>();
            var all = Db.AllTemplates;
            foreach (Team team in Enum.GetValues(typeof(Team)))
            {
                if (team == Team.None) continue;
                var none = Task183Report.MeasureOnboarding(all, Db, rosterManager, team, null);
                TestContext.WriteLine($"{team}: LIVE {none.liveCount}장, 28인 평균 {none.rosterAvg:F2}, 세트덱 {none.score}P(+{none.setDeckOvr}), 구단 OVR {none.teamOvr}");
                Assert.That(none.rosterAvg, Is.InRange(59.0, 60.0), $"{team} 28인 평균");
                Assert.That(none.teamOvr, Is.InRange(60, 62), $"{team} 선물 전");
                foreach (var gift in Task183Report.GiftIds)
                {
                    var m = Task183Report.MeasureOnboarding(all, Db, rosterManager, team, gift);
                    Assert.That(m.teamOvr, Is.InRange(60, 62), $"{team} + {gift}");
                    Assert.AreEqual(LeagueTier.Amateur, LeagueTierTable.RecommendedFor(m.teamOvr));
                }
            }
        }

        // ------------------------------------------------------------------ 요구사항 C: 4대 성장 + 최종 144

        [Test]
        public void GrowthCaps_PerGrade_MatchSpecTable_AndFinal144()
        {
            var expected = new Dictionary<Grade, (int growth, int finalMin, int finalMax)>
            {
                { Grade.LIVE_NORMAL, (14, 86, 99) }, { Grade.LIVE_EPIC, (18, 100, 110) }, { Grade.ALLSTAR, (21, 113, 121) },
                { Grade.FRANCHISE, (22, 118, 125) }, { Grade.TITLE_HOLDER, (23, 123, 130) }, { Grade.GOLDEN_GLOVE, (25, 129, 137) },
                { Grade.SIGNATURE, (25, 137, 144) }, { Grade.DYNASTY, (25, 137, 144) }, { Grade.RETIRED_NUMBER, (25, 137, 144) },
            };
            Assert.AreEqual(25, CardGrowthRules.MaxPureGrowth);
            Assert.AreEqual(17, TeamSynergyRules.MaxSynergyOvr);
            foreach (var pair in expected)
            {
                var band = CardGrowthRules.BaseOvrRange(pair.Key);
                Assert.AreEqual(pair.Value.growth, CardGrowthRules.MaxTotalGrowth(pair.Key), pair.Key.ToString());
                Assert.AreEqual(pair.Value.growth, CardGrowthRules.MaxReinforceGrowth + CardGrowthRules.LimitBreakCap(pair.Key)
                    + CardGrowthRules.TrainingCap(pair.Key) + CardGrowthRules.AwakenGrowthCap(pair.Key));
                Assert.AreEqual(pair.Value.finalMin, CardGrowthRules.MaxPotentialOvr(pair.Key, band.Min), $"{pair.Key} 최저 최종");
                Assert.AreEqual(pair.Value.finalMax, CardGrowthRules.MaxPotentialOvr(pair.Key, band.Max), $"{pair.Key} 최고 최종");
            }
            Assert.AreEqual(144, CardGrowthRules.MaxPotentialOvr(Grade.RETIRED_NUMBER, 110), "상한 144");
        }

        [Test]
        public void PlayerGrowth_FourSystemsStack_AndRespectGradeCap()
        {
            var live = Card(Grade.LIVE_NORMAL, 60);
            Assert.AreEqual(60, live.CalculateNeutralOVR());
            live.ReinforceLevel = 10; live.LimitBreakLevel = 5; live.TrainingLevel = 5; live.AwakenLevel = 10;
            Assert.AreEqual(10, live.ReinforceGrowth);
            Assert.AreEqual(2, live.LimitBreakGrowth);
            Assert.AreEqual(1, live.TrainingGrowth);
            Assert.AreEqual(1, live.AwakenGrowth);
            Assert.AreEqual(14, live.GetStatGrowth());
            Assert.AreEqual(74, live.CalculateNeutralOVR());

            var gg = Card(Grade.GOLDEN_GLOVE, 90, pitcher: true);
            gg.ReinforceLevel = 10; gg.LimitBreakLevel = 5; gg.TrainingLevel = 5; gg.AwakenLevel = 10;
            Assert.AreEqual(25, gg.GetStatGrowth());
            Assert.AreEqual(115, gg.CalculateNeutralOVR());
            Assert.AreEqual(Math.Min(144, 90 + 25 + 17), gg.MaxPotentialOvr);

            var allstar = Card(Grade.ALLSTAR, 80);
            allstar.AwakenLevel = 9;
            Assert.AreEqual(3, allstar.AwakenGrowth, "9각 = 4 → 올스타 상한 3");
            allstar.AwakenLevel = 4;
            Assert.AreEqual(2, allstar.AwakenGrowth);
        }

        [Test]
        public void LimitBreak_NeedsTenEnhance_ConsumesMaterial_AndCaps()
        {
            var target = Card(Grade.LIVE_EPIC, 70, person: "P1");
            var sameGrade = Card(Grade.LIVE_EPIC, 66);
            var lowerOther = Card(Grade.LIVE_NORMAL, 60);
            var samePerson = Card(Grade.LIVE_NORMAL, 58, person: "P1");

            Assert.IsFalse(CardGrowthActions.TryLimitBreak(target, sameGrade, out var msg), msg);
            target.ReinforceLevel = Player.MaxReinforceLevel;
            Assert.IsFalse(CardGrowthActions.IsValidLimitBreakMaterial(target, lowerOther), "낮은 등급 타 선수는 재료 불가");
            Assert.IsTrue(CardGrowthActions.IsValidLimitBreakMaterial(target, samePerson), "동일 선수는 등급 무관");
            var picked = CardGrowthActions.PickLimitBreakMaterial(target, new[] { target, sameGrade, lowerOther, samePerson }, new[] { target });
            Assert.AreSame(samePerson, picked, "동일 선수 카드 우선");
            Assert.IsNull(CardGrowthActions.PickLimitBreakMaterial(target, new[] { target, sameGrade }, new[] { sameGrade }), "1군 카드는 재료로 쓰지 않음");

            int cap = CardGrowthRules.LimitBreakCap(Grade.LIVE_EPIC);
            for (int i = 0; i < cap; i++) Assert.IsTrue(CardGrowthActions.TryLimitBreak(target, sameGrade, out msg), msg);
            Assert.AreEqual(cap, target.LimitBreakLevel);
            Assert.IsFalse(CardGrowthActions.TryLimitBreak(target, sameGrade, out msg), "상한 도달");
            Assert.AreEqual(70 + 10 + cap, target.CalculateNeutralOVR());
        }

        [Test]
        public void Training_CostsGold_AndCaps()
        {
            var target = Card(Grade.TITLE_HOLDER, 85);
            Assert.IsFalse(CardGrowthActions.TryTrain(target, 500, out _, out var msg), msg);
            int gold = 100000, spentTotal = 0;
            for (int i = 0; i < CardGrowthRules.TrainingCap(Grade.TITLE_HOLDER); i++)
            {
                Assert.IsTrue(CardGrowthActions.TryTrain(target, gold, out int spent, out msg), msg);
                Assert.AreEqual(CardGrowthActions.TrainingGoldPerLevel * (i + 1), spent);
                gold -= spent; spentTotal += spent;
            }
            Assert.IsFalse(CardGrowthActions.TryTrain(target, gold, out _, out _), "특훈 상한");
            Assert.AreEqual(4, target.TrainingGrowth);
            Assert.AreEqual(1000 + 2000 + 3000 + 4000, spentTotal);
            StringAssert.Contains("최대 잠재", CardGrowthActions.GrowthSummary(target, 1));
        }

        [Test]
        public void SaveData_PersistsLimitBreakTraining_AndLeagueTier()
        {
            var saved = new PlayerSaveData { TemplateId = "X", LimitBreakLevel = 3, TrainingLevel = 2 };
            var json = JsonUtility.ToJson(saved);
            var back = JsonUtility.FromJson<PlayerSaveData>(json);
            Assert.AreEqual(3, back.LimitBreakLevel);
            Assert.AreEqual(2, back.TrainingLevel);

            var data = JsonUtility.FromJson<GameSaveData>("{\"SaveVersion\":9}");
            Assert.AreEqual(-1, data.LeagueTier, "구버전 세이브 = -1(권장 리그로 복원)");
            Assert.GreaterOrEqual(new GameSaveData().SaveVersion, 10);
        }

        // ------------------------------------------------------------------ 팀 시너지

        [Test]
        public void TeamSynergy_SetDeckPlus13_CheerPlus4_Max17()
        {
            Assert.AreEqual(1, TeamSynergyRules.SetDeckOvrBonus(104), "기본 세트덱 +1");
            Assert.AreEqual(0, TeamSynergyRules.SetDeckOvrBonus(99));
            Assert.AreEqual(13, TeamSynergyRules.SetDeckOvrBonus(200));
            var legends = Enumerable.Range(0, CheerSquad.SlotCount)
                .Select(i => new Cheerleader { InstanceId = $"L{i}", Name = $"L{i}", Grade = CheerleaderGrade.LEGEND, Team = Team.Samsung }).ToList();
            Assert.AreEqual(4, TeamSynergyRules.CheerSquadOvrBonus(legends, Team.Samsung));
            Assert.AreEqual(0, TeamSynergyRules.CheerSquadOvrBonus(legends, Team.KIA), "구단 시너지 미발동");
            Assert.AreEqual(144, TeamSynergyRules.ClampFinal(150));
        }
    }
}
