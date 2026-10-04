using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.EditorTools;
using KBOManager.Engine;
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
    /// [TASK-KBO-187] 세트덱 SD 표기 단일화 · 게이지 실제 구간 · 타순 마름모 32px · NEXT MATCH 예고 선발 = 실제 선발(1~5선발 로테이션) ·
    /// 투수 13칸 자리 고정 · 치어리더 강화/★각성/도감 · 결과 화면 투수 기록 · 대승 스코어 감쇠.
    /// </summary>
    public class Task187Tests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private LineupAssignment previousActive;
        private bool sceneOpened;

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
            OvrGapLaw.BlowoutDampingEnabled = true;
            foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
            Task183Report.CleanupTemp();
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = false;
        }

        private Player Pitcher(string name, PitcherRole role, int level, Team team = Team.Samsung)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = $"{name}_{Guid.NewGuid():N}";
            t.RealPlayerId = name;
            t.PlayerName = name;
            t.SeasonYear = 2026;
            t.Team = team;
            t.Grade = Grade.LIVE_EPIC;
            t.IsPitcher = true;
            t.PitcherRole = role;
            t.PitcherStats = new PitcherStats(level, level, level, level, level);
            return new Player(Guid.NewGuid().ToString(), t);
        }

        /// <summary>선발 5(OVR 90/85/80/75/70) + 승리조 2 + 추격조 4 + 롱 1 + 마무리 1 = 13인.</summary>
        private List<Player> Staff(out List<Player> rotation)
        {
            rotation = new[] { 90, 85, 80, 75, 70 }.Select((ovr, i) => Pitcher($"SP{i + 1}", PitcherRole.StartingPitcher, ovr)).ToList();
            var staff = new List<Player>(rotation);
            staff.AddRange(Enumerable.Range(0, 2).Select(i => Pitcher($"WR{i}", PitcherRole.WinningReliever, 72)));
            staff.AddRange(Enumerable.Range(0, 4).Select(i => Pitcher($"MU{i}", PitcherRole.MopUpReliever, 66)));
            staff.Add(Pitcher("LR", PitcherRole.LongReliever, 68));
            staff.Add(Pitcher("CL", PitcherRole.Closer, 74));
            return staff;
        }

        // ------------------------------------------------------------------ A. 세트덱 SD 단일화 · 게이지 · 마름모

        [Test]
        public void CardSdBadges_SumToTotalScore_WithOffTeamAndExcludedCards()
        {
            var roster = Task183Report.ProceduralTeam(Team.Samsung, 80, "T187");
            var others = Task183Report.ProceduralTeam(Team.KIA, 80, "T187K");
            roster[2] = others[2];
            roster[25] = others[25];
            var result = SetDeckEvaluator.Evaluate(roster, Team.Samsung.ToString());

            Assert.AreEqual(result.Score, roster.Sum(p => SetDeckEvaluator.CardScoreIn(result, p)), "카드 SD 합계 == 총점");
            Assert.AreEqual(0, SetDeckEvaluator.CardScoreIn(result, roster[2]), "다른 구단 카드 = SD 0");
            Assert.AreEqual(27, result.SlotPlayers.Count);
            Assert.AreEqual(1, roster.Count(p => SetDeckEvaluator.IsExcludedFromSlots(result, p)), "28인 중 1인 제외 표시");
            var counted = roster.First(p => result.CountedPlayers.Contains(p));
            Assert.AreEqual(SetDeckEvaluator.GetEffectiveCardSetDeckScore(counted, Team.Samsung), SetDeckEvaluator.CardScoreIn(result, counted));
            Assert.AreEqual(counted.SetDeckScore, SetDeckEvaluator.GetBaseCardSetDeckScore(counted));
        }

        [Test]
        public void Gauge_UsesRealBuffThresholds_AndMarksReached()
        {
            var (_, markers) = RosterUIController.GaugeWindow(95);
            CollectionAssert.AreEqual(new[] { 80, 100, 115, 120, 135, 140 }, markers);
            StringAssert.StartsWith("✔ 달성", RosterUIController.GaugeIconLabel(80, 95, null, 2024));
            StringAssert.Contains("'24", RosterUIController.GaugeIconLabel(80, 95, null, 2024), "80P = 연도 선택 버프");
            Assert.AreEqual("잠김", RosterUIController.GaugeIconLabel(100, 95, null, 2024));
            Assert.That(RosterUIController.GaugeFill(95, markers), Is.InRange(0.5f / 6f, 1.5f / 6f), "80P와 100P 마커 사이");
            Assert.AreEqual(32f, RosterUIController.OrderDiamondSize);
        }

        // ------------------------------------------------------------------ C. 투수 13칸 고정

        [Test]
        public void PitcherSlots_UserPlacementSurvivesOvrResort()
        {
            var staff = Staff(out var rotation);
            var assignment = new LineupAssignment();
            Assert.IsTrue(assignment.SwapPitchers(staff, rotation[0], rotation[4]), "1선발(OVR 90) ↔ 5선발(OVR 70)");
            var after = StartingRotation.RotationOf(staff, assignment);
            Assert.AreEqual(rotation[4], after[0], "OVR 70 투수가 1선발에 그대로");
            Assert.AreEqual(rotation[0], after[4], "OVR 90 투수가 5선발에 그대로");

            var closer = staff.Single(p => p.Template.PlayerName == "CL");
            Assert.IsTrue(assignment.SwapPitchers(staff, after[2], closer), "3선발 ↔ 마무리");
            var (rot2, bullpen) = LineupView.BuildPitchers(staff, assignment);
            Assert.AreEqual(closer, rot2[2].Player);
            Assert.AreEqual(rotation[2], bullpen.First(e => e.Group == "마무리").Player);
            Assert.AreEqual(13, LineupAssignment.PitcherSlotCount);
            Assert.AreEqual(13, assignment.PitcherSlots.Count, "13칸 전체 고정");
            Assert.AreEqual(PitcherRole.Closer, LineupAssignment.RoleOf(rotation[2], assignment));
        }

        [Test]
        public void PitcherSlots_InventoryReplacementInheritsSlot()
        {
            var staff = Staff(out var rotation);
            var assignment = new LineupAssignment();
            assignment.FreezePitchers(staff);
            var incoming = Pitcher("NEW", PitcherRole.Closer, 99);
            assignment.ReplaceId(rotation[2].InstanceId, incoming.InstanceId);
            var replaced = staff.Select(p => p == rotation[2] ? incoming : p).ToList();
            Assert.AreEqual(incoming, StartingRotation.RotationOf(replaced, assignment)[2], "3선발 자리를 그대로 이어받음(보직 무관)");
            Assert.IsTrue(RosterSwapRules.CanSwap(staff, rotation[2], incoming), "투수 칸은 보직 무관 교체 허용");
        }

        // ------------------------------------------------------------------ B. 로테이션 · 예고 선발 = 실제 선발

        [Test]
        public void Rotation_CyclesOneToFive_AndEngineStartsPredictedPitcher()
        {
            var staff = Staff(out var rotation);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 0 }, Enumerable.Range(0, 6).Select(g => StartingRotation.RotationIndex(g, 5)).ToArray());
            Assert.AreEqual(rotation[0], StartingRotation.PickFor(staff, 0));
            Assert.AreEqual(rotation[1], StartingRotation.PickFor(staff, 1));
            Assert.AreEqual(rotation[4], StartingRotation.PickFor(staff, 4));
            Assert.AreEqual(rotation[0], StartingRotation.PickFor(staff, 5), "6경기째 1선발");

            var home = Task183Report.ProceduralTeam(Team.Samsung, 80, "RH");
            var away = Task183Report.ProceduralTeam(Team.SSG, 80, "RA");
            for (int g = 0; g < 5; g++)
            {
                foreach (var p in home.Concat(away)) if (p.Template.IsPitcher) p.RecoverStamina(1000);
                var homeStarter = StartingRotation.PickFor(home, g);
                var awayStarter = StartingRotation.PickFor(away, g);
                var engine = new MatchEngine(home, away, new TeamPowerModifiers(0), new TeamPowerModifiers(0), null, null, 700 + g)
                {
                    HomeDesignatedStarter = homeStarter,
                    AwayDesignatedStarter = awayStarter,
                };
                engine.BeginMatch("H", "A");
                var first = engine.PlayNextAtBat();
                Assert.AreEqual(homeStarter, first.Pitcher, $"{g + 1}경기 홈 선발 = 예고");
                AtBatStepResult step = first;
                while (step.State != null && step.State.Inning == 1 && !step.HalfInningEnded) step = engine.PlayNextAtBat();
                var bottom = engine.PlayNextAtBat();
                Assert.AreEqual(awayStarter, bottom.Pitcher, $"{g + 1}경기 원정 선발 = 예고");
            }
        }

        // ------------------------------------------------------------------ D. 치어리더 성장

        private static Cheerleader C(string id, string name, Team team, CheerleaderGrade grade = CheerleaderGrade.LIVE_NORMAL) =>
            new Cheerleader { InstanceId = id, Name = name, Team = team, Grade = grade, EconomicBonusRate = 1.05f };

        [Test]
        public void CheerAwaken_SamePersonTwoStars_SameTeamOneStar_MaxFive_TierLeaps()
        {
            var target = C("t", "김응원", Team.Samsung);
            Assert.AreEqual(1, CheerGrowth.Stars(new Cheerleader { InstanceId = "old", StarLevel = 0 }), "구버전 세이브 0 = ★1");
            Assert.AreEqual(2, CheerGrowth.AwakenGain(target, C("a", "김응원", Team.KIA)));
            Assert.AreEqual(1, CheerGrowth.AwakenGain(target, C("b", "박응원", Team.Samsung)));
            Assert.AreEqual(0, CheerGrowth.AwakenGain(target, C("c", "이응원", Team.LG)));
            Assert.AreEqual(0, CheerGrowth.AwakenGain(target, target));

            int tier1 = CheerGrowth.EffectiveTier(target);
            CheerGrowth.ApplyAwaken(target, C("a", "김응원", Team.KIA));
            Assert.AreEqual(3, CheerGrowth.Stars(target));
            Assert.AreEqual(tier1 + 1, CheerGrowth.EffectiveTier(target), "★3 1차 임계점 도약");
            CheerGrowth.ApplyAwaken(target, C("d", "김응원", Team.KIA));
            Assert.AreEqual(5, CheerGrowth.Stars(target));
            Assert.AreEqual(tier1 + 2, CheerGrowth.EffectiveTier(target), "★5 최종 임계점 도약");
            Assert.IsFalse(CheerGrowth.CanAwaken(target, C("e", "김응원", Team.KIA), null, out _), "★5 이후 불가");

            var squadMember = C("s", "박응원", Team.Samsung);
            var squad = new List<Cheerleader> { squadMember };
            var fresh = C("f", "최응원", Team.Samsung);
            Assert.IsFalse(CheerGrowth.CanAwaken(fresh, squadMember, squad, out var reason), "편성 중 카드는 재료 불가");
            StringAssert.Contains("편성", reason);
            var materials = CheerGrowth.AwakenMaterials(fresh, new[] { C("x", "박응원", Team.Samsung), C("y", "최응원", Team.KIA), squadMember }, squad);
            Assert.AreEqual("y", materials[0].InstanceId, "동일 인물(+2★) 재료 우선");
            Assert.AreEqual(2, materials.Count);
        }

        [Test]
        public void CheerReinforce_ScalesRoleEffects_AndRevenue_NotTeamOvr()
        {
            var home = C("h", "홈응원", Team.Samsung, CheerleaderGrade.ICON);
            var squad = Enumerable.Repeat<Cheerleader>(null, CheerSquad.SlotCount).ToList();
            squad[(int)CheerRole.Home] = home;
            squad[(int)CheerRole.Leader] = C("l", "단장", Team.Samsung, CheerleaderGrade.ICON);
            var before = CheerSquad.BuildEffects(squad, Team.Samsung, true, 0, 20);
            home.ReinforceLevel = 10;
            squad[(int)CheerRole.Leader].ReinforceLevel = 10;
            var after = CheerSquad.BuildEffects(squad, Team.Samsung, true, 0, 20);
            Assert.Greater(after.HomeAllStatsBonus, before.HomeAllStatsBonus);
            Assert.AreEqual(before.SetDeckAmplifyPercent + 10, after.SetDeckAmplifyPercent, "응원단장 +1%p/강");
            Assert.AreEqual(20, CheerGrowth.HomeRevenueBonusPercent(home), "홈 응원 +2%p/강");
            Assert.AreEqual(1.05f + 0.2f, CheerGrowth.HomeRevenueMultiplier(home, null), 0.0001f);
            Assert.Less(CheerGrowth.ReinforceCost(0), CheerGrowth.ReinforceCost(9));
            Assert.AreEqual(10, CheerGrowth.MaxReinforce);

            // 구단 OVR 리더 적용률은 카드 등급 티어만 본다(성장 미반영 - TASK-184 원칙)
            Assert.AreEqual(6, TeamSynergyRules.LeaderAmplifyPercent(squad, Team.Samsung));
        }

        [Test]
        public void CheerCollection_CountsDistinctPersonsPerTeam()
        {
            var catalog = new List<Cheerleader>
            {
                C("1", "가", Team.Samsung), C("2", "가", Team.Samsung), C("3", "나", Team.Samsung), C("4", "다", Team.Samsung), C("5", "라", Team.KIA),
            };
            var owned = new List<Cheerleader> { C("o1", "가", Team.Samsung), C("o2", "가", Team.Samsung), C("o3", "나", Team.Samsung), C("o4", "라", Team.KIA) };
            var status = CheerGrowth.Collection(Team.Samsung, owned, catalog);
            Assert.AreEqual(2, status.Collected);
            Assert.AreEqual(3, status.Total);
            Assert.AreEqual(2, status.RevenueBonusPercent);
            Assert.AreEqual("삼성 응원단 수집 2/3명 · 도감 보너스: 홈 관중 수익 +2%", status.Label);
        }

        // ------------------------------------------------------------------ E. 대승 감쇠

        [Test]
        public void BlowoutDamping_Rules()
        {
            Assert.AreEqual(20, OvrGapLaw.BattingClassBonus(20, 4));
            Assert.Less(OvrGapLaw.BattingClassBonus(20, 6), 20);
            Assert.AreEqual(0, OvrGapLaw.BattingClassBonus(20, 8));
            Assert.AreEqual(0f, OvrGapLaw.BlowoutDampingChance(3, 5, 2));
            Assert.Greater(OvrGapLaw.BlowoutDampingChance(0, 4, 4), 0f, "빅이닝 감쇠");
            Assert.Greater(OvrGapLaw.BlowoutDampingChance(10, 12, 0), OvrGapLaw.BlowoutDampingChance(8, 8, 0));
            Assert.LessOrEqual(OvrGapLaw.BlowoutDampingChance(40, 40, 10), 0.9f);
        }

        [Test]
        public void BlowoutDamping_KeepsWinRate_CapsScores()
        {
            var off = Task187Report.SimulateBlowout(60, 30, 80, false);
            var on = Task187Report.SimulateBlowout(60, 30, 80, true);
            Assert.GreaterOrEqual(on.winRate, 0.9, $"ΔOVR 30 상위 구단 승률 유지(감쇠 후 {on.winRate:P1}, 감쇠 전 {off.winRate:P1})");
            Assert.LessOrEqual(on.avgRuns, 16.0, $"평균 득점 {on.avgRuns:F1}점(감쇠 전 {off.avgRuns:F1}점)");
            Assert.Less(on.maxRuns, 30, $"최다 득점 {on.maxRuns}점(감쇠 전 {off.maxRuns}점)");
            Assert.LessOrEqual(on.avgRuns, off.avgRuns);
        }

        // ------------------------------------------------------------------ 씬

        [Test]
        public void Scene_CheerGrowthAttached()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
            var controller = UnityEngine.Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(controller);
            var view = controller.GetComponent<CheerGrowthView>();
            Assert.IsNotNull(view, "CheerGrowthView 부착");
            Assert.IsNotNull(controller.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == CheerGrowthView.StripName), "응원단 성장 띠");
            Assert.IsNotNull(controller.transform.Find(CheerGrowthView.OverlayName), "성장 덮개 패널");
        }
    }
}
