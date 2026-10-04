using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.EditorTools;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using NUnit.Framework;
using UnityEngine;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-KBO-184] 선수 관리(성장 센터) / 보관 선수 필터 · 이닝 결과 초기화 · 타 구장 4경기 자동 진행 · 구단 OVR 단일 공식 · 각성 임계점 성장.
    /// </summary>
    public class Task184Tests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
            Task183Report.CleanupTemp();
        }

        private Player Card(Grade grade, int level, bool pitcher = false, string person = null, Team team = Team.Samsung,
            BatterPosition position = BatterPosition.RightField, PitcherRole role = PitcherRole.StartingPitcher)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = Guid.NewGuid().ToString("N"); t.RealPlayerId = person ?? t.TemplateId; t.PlayerName = t.TemplateId.Substring(0, 6);
            t.Team = team; t.Grade = grade; t.SeasonYear = 2026; t.IsPitcher = pitcher;
            if (pitcher) { t.PitcherRole = role; t.PitcherStats = StatProfiles.SpreadPitcher(level, role, 7); }
            else { t.BatterPosition = position; t.BatterStats = StatProfiles.SpreadBatter(level, position, 7); }
            return new Player(Guid.NewGuid().ToString(), t);
        }

        private List<Player> Lineup(Grade grade, int level, Team team = Team.Samsung)
        {
            var roster = new List<Player>();
            foreach (BatterPosition pos in Enum.GetValues(typeof(BatterPosition))) roster.Add(Card(grade, level, false, null, team, pos));
            for (int b = 0; b < 6; b++) roster.Add(Card(grade, level, false, null, team, BatterPosition.LeftField));
            foreach (var (role, count) in RosterSlotLayout.PitcherRoleQuota)
                for (int k = 0; k < count; k++) roster.Add(Card(grade, level, true, null, team, default, role));
            return roster;
        }

        // ------------------------------------------------------------------ 요구사항 B-1: 이닝 결과 초기화

        [Test]
        public void Tracker_InningBadges_ClearOnHalfInningEnd_ButGameTotalsKept()
        {
            var a = Card(Grade.LIVE_NORMAL, 60);
            var b = Card(Grade.LIVE_NORMAL, 60);
            var p = Card(Grade.LIVE_NORMAL, 60, pitcher: true);
            var events = new List<PlayEvent>
            {
                new PlayEvent { Type = PlayEventType.AtBatResult, Inning = 1, IsTopHalf = true, Batter = a, Pitcher = p, Result = AtBatResult.Strikeout },
                new PlayEvent { Type = PlayEventType.AtBatResult, Inning = 1, IsTopHalf = true, Batter = b, Pitcher = p, Result = AtBatResult.Flyout },
                new PlayEvent { Type = PlayEventType.HalfInningEnd, Inning = 1, IsTopHalf = true },
                new PlayEvent { Type = PlayEventType.AtBatResult, Inning = 1, IsTopHalf = false, Batter = p, Pitcher = a, Result = AtBatResult.Single },
            };

            var mid = CompyaGameTracker.Build(events, 1);
            Assert.AreEqual(1, mid.BatOf(a).InningBadges.Count, "이닝 중에는 이번 이닝 결과가 보인다");
            Assert.AreEqual("삼진", mid.BatOf(a).InningBadges[0].Label);

            var afterHalf = CompyaGameTracker.Build(events, 2);
            Assert.AreEqual(0, afterHalf.BatOf(a).InningBadges.Count, "공수 교대 시 이전 이닝 뱃지 초기화");
            Assert.AreEqual(0, afterHalf.BatOf(b).InningBadges.Count);
            Assert.AreEqual(1, afterHalf.BatOf(a).Badges.Count, "경기 누적 기록(기록 팝업용)은 유지");

            var next = CompyaGameTracker.Build(events, 3);
            Assert.AreEqual(1, next.BatOf(p).InningBadges.Count, "새 이닝 타자 결과만 표시");
            Assert.AreEqual(0, next.BatOf(a).InningBadges.Count);
        }

        // ------------------------------------------------------------------ 요구사항 B-2: 타 구장 4경기 자동 진행

        [Test]
        public void PairOtherTeams_FourGames_AllEightTeams_RoundRobinOver7Rounds()
        {
            var teams = new[] { Team.Doosan, Team.LG, Team.KT, Team.SSG, Team.NC, Team.Kiwoom, Team.KIA, Team.Lotte };
            var meetings = new Dictionary<(Team, Team), int>();
            for (int game = 1; game <= 7; game++)
            {
                var pairs = LeagueManager.PairOtherTeams(teams, game);
                Assert.AreEqual(4, pairs.Count);
                var used = pairs.SelectMany(x => new[] { x.Home, x.Away }).ToList();
                CollectionAssert.AreEquivalent(teams, used, $"{game}경기: 8개 구단이 정확히 한 번씩");
                foreach (var (h, aw) in pairs)
                {
                    var key = h < aw ? (h, aw) : (aw, h);
                    meetings[key] = meetings.TryGetValue(key, out int n) ? n + 1 : 1;
                }
            }
            Assert.AreEqual(28, meetings.Count, "7라운드 = 8팀 풀 리그(28개 대진) 한 바퀴");
            Assert.IsTrue(meetings.Values.All(v => v == 1));
        }

        [Test]
        public void League_EachUserGame_SimulatesOtherFourGames_AllTenTeamsSameGameCount()
        {
            var go = new GameObject("Task184League");
            created.Add(go);
            var league = go.AddComponent<LeagueManager>();
            league.InitializeLeague(Team.Samsung, LeagueTier.Amateur);
            league.SetTeamRoster(Team.Samsung, Task183Report.ProceduralTeam(Team.Samsung, 60, "U"));
            int rounds = 0;
            league.OnRoundCompleted += () => rounds++;

            const int games = 6;
            for (int g = 1; g <= games; g++)
            {
                var fixture = league.PlayNextMatch();
                Assert.IsNotNull(fixture);
                var others = league.LastRoundOtherFixtures;
                Assert.AreEqual(4, others.Count, "타 구장 4경기");
                Assert.IsTrue(others.All(f => f.IsPlayed && f.Result != null));
                var teamsToday = others.SelectMany(f => new[] { f.HomeTeam, f.AwayTeam }).Concat(new[] { fixture.HomeTeam, fixture.AwayTeam }).ToList();
                Assert.AreEqual(10, teamsToday.Distinct().Count(), $"{g}경기일: 10개 구단 전원 1경기씩");
                foreach (var f in others)
                    Assert.AreEqual(f.Result.HomeTotalScore > f.Result.AwayTotalScore ? f.HomeTeam.ToString() : f.Result.HomeTotalScore < f.Result.AwayTotalScore ? f.AwayTeam.ToString() : null,
                        f.Result.WinnerTeamName, "실제 스코어와 승패 일치");
                foreach (var info in league.GetStandings())
                    Assert.AreEqual(g, info.Wins + info.Draws + info.Losses, $"{info.Team} {g}경기 소화");
            }
            Assert.AreEqual(games, rounds);
            var table = league.GetStandings();
            Assert.AreEqual(0f, league.GamesBehind(table[0].Team));
            Assert.GreaterOrEqual(league.GamesBehind(table[table.Count - 1].Team), 0f);
        }

        // ------------------------------------------------------------------ 요구사항 C: 구단 OVR 단일 공식

        [Test]
        public void TeamOvr_IsLineupAverageOfFinalOvr_NoCheerDirectBonus_NoCondition()
        {
            var roster = Lineup(Grade.LIVE_NORMAL, 60);
            roster[0].ReinforceLevel = 10; // 70
            roster[1].ReinforceLevel = 4;  // 64
            var b = TeamOvrCalculator.Calculate(roster);
            double expected = roster.Average(p => p.CalculateNeutralOVR() + b.Synergy);
            Assert.AreEqual((int)Math.Round(expected, MidpointRounding.AwayFromZero), b.Total, "구단 OVR = 라인업 최종 OVR 산술 평균");
            Assert.AreEqual(TeamOvrCalculator.AverageFinalOvr(roster, b.Synergy), b.Total);

            // 컨디션은 선수/구단 OVR을 바꾸지 않는다.
            int ovrBefore = roster[0].CalculateOVR(false);
            foreach (var p in roster) p.CurrentCondition = PlayerCondition.Excellent;
            Assert.AreEqual(ovrBefore, roster[0].CalculateOVR(false));
            Assert.AreEqual(b.Total, TeamOvrCalculator.Calculate(roster).Total);
            foreach (var p in roster) p.CurrentCondition = PlayerCondition.Poor;
            Assert.AreEqual(b.Total, TeamOvrCalculator.Calculate(roster).Total);
            Assert.AreEqual(-2, roster[0].ConditionStatBonus, "컨디션은 경기 안 세부 스탯 보정으로만");

            // 치어리더 6인 LEGEND - 구 +4 직접 가산 없음(응원단장 세트덱 보강만, 세트덱 OVR x 10% 반올림).
            var legends = Enumerable.Range(0, CheerSquad.SlotCount)
                .Select(i => new Cheerleader { InstanceId = $"L{i}", Name = $"L{i}", Grade = CheerleaderGrade.LEGEND, Team = Team.Samsung }).ToList();
            var withCheer = TeamOvrCalculator.Calculate(roster, "Samsung", legends);
            var without = TeamOvrCalculator.Calculate(roster, "Samsung");
            Assert.LessOrEqual(withCheer.Total - without.Total, 1, "치어리더 직접 가산(+4) 폐지");
        }

        [Test]
        public void TeamOvr_HallOfFameLineup_FullGrowthFullSetDeck_Reaches137To144()
        {
            foreach (int level in new[] { 95, 98, 102 })
            {
                var roster = Lineup(Grade.SIGNATURE, level);
                foreach (var p in roster) { p.ReinforceLevel = 10; p.LimitBreakLevel = 3; p.TrainingLevel = 3; p.AwakenLevel = 10; }
                int ovr = TeamOvrCalculator.AverageFinalOvr(roster, TeamSynergyRules.StaticSynergyOvr(SetDeckBuffTable.FinalGoalScore));
                TestContext.WriteLine($"SIG 기본 {level} 풀성장 + 200P → 구단 OVR {ovr}");
                Assert.That(ovr, Is.InRange(137, 144), $"기본 {level}");
                Assert.AreEqual(LeagueTier.HallOfFame, LeagueTierTable.RecommendedFor(ovr));
            }
        }

        // ------------------------------------------------------------------ 요구사항 D: 각성 임계점 성장

        [Test]
        public void Awaken_ThresholdStaircase_TopGradePlus15_LargestGrowthShare()
        {
            int[] expectedTop = { 0, 1, 2, 5, 6, 7, 10, 11, 12, 14, 15 };
            for (int lv = 0; lv <= 10; lv++) Assert.AreEqual(expectedTop[lv], CardGrowthRules.AwakenGrowthFor(Grade.GOLDEN_GLOVE, lv), $"{lv}각");
            Assert.GreaterOrEqual(CardGrowthRules.AwakenGrowthFor(Grade.DYNASTY, 3) - CardGrowthRules.AwakenGrowthFor(Grade.DYNASTY, 2), 3, "1차 임계점(3각) 도약");
            Assert.GreaterOrEqual(CardGrowthRules.AwakenGrowthFor(Grade.DYNASTY, 6) - CardGrowthRules.AwakenGrowthFor(Grade.DYNASTY, 5), 3, "2차 임계점(6각) 도약");

            foreach (Grade grade in Enum.GetValues(typeof(Grade)))
            {
                int max = CardGrowthRules.MaxAwakenLevelFor(grade);
                for (int lv = 1; lv <= max; lv++)
                    Assert.GreaterOrEqual(CardGrowthRules.AwakenGrowthFor(grade, lv), CardGrowthRules.AwakenGrowthFor(grade, lv - 1), $"{grade} 단조 증가");
                Assert.AreEqual(CardGrowthRules.AwakenGrowthCap(grade), CardGrowthRules.AwakenGrowthFor(grade, max), $"{grade} 최종 단계 = 상한");
                Assert.AreEqual(CardGrowthRules.AwakenGrowthFor(grade, max), CardGrowthRules.AwakenGrowthFor(grade, 99), "상한 클램프");
            }
            foreach (var grade in new[] { Grade.GOLDEN_GLOVE, Grade.SIGNATURE, Grade.DYNASTY, Grade.RETIRED_NUMBER, Grade.TITLE_HOLDER, Grade.FRANCHISE, Grade.ALLSTAR })
            {
                int awaken = CardGrowthRules.AwakenGrowthCap(grade);
                Assert.Greater(awaken, CardGrowthRules.MaxReinforceGrowth - 1, $"{grade} 각성이 최대 성장 축");
                Assert.Greater(awaken, CardGrowthRules.LimitBreakCap(grade) + CardGrowthRules.TrainingCap(grade));
            }
            Assert.That(CardGrowthRules.AwakenGrowthCap(Grade.SIGNATURE), Is.InRange(12, 15));
            Assert.AreEqual(3, CardGrowthRules.NextAwakenThreshold(Grade.LIVE_NORMAL, 0));
            Assert.AreEqual(6, CardGrowthRules.NextAwakenThreshold(Grade.LIVE_NORMAL, 3));
            Assert.AreEqual(10, CardGrowthRules.NextAwakenThreshold(Grade.GOLDEN_GLOVE, 9), "초월");
            Assert.AreEqual(-1, CardGrowthRules.NextAwakenThreshold(Grade.ALLSTAR, 9), "9각 한계 등급");
            StringAssert.Contains("1차 임계점", CardGrowthRules.AwakenStageNote(Grade.ALLSTAR, 1));
        }

        // ------------------------------------------------------------------ 요구사항 A: 성장 센터 / 보관 선수 필터

        [Test]
        public void GrowthCenter_DefaultTarget_Candidates_AndPreviewDoesNotMutate()
        {
            var star = Card(Grade.GOLDEN_GLOVE, 90, person: "STAR");
            var regular = Card(Grade.LIVE_NORMAL, 60);
            var roster = new List<Player> { regular, star };
            var copy = Card(Grade.GOLDEN_GLOVE, 88, person: "STAR");
            var other = Card(Grade.LIVE_NORMAL, 55);
            var inventory = new List<Player> { star, regular, copy, other };

            Assert.AreSame(star, GrowthCenterRules.DefaultTarget(roster, inventory), "대표 선수 = 라인업 최고 OVR");
            Assert.AreSame(other, GrowthCenterRules.DefaultTarget(null, new[] { other }), "라인업이 비면 보유 최고");
            var targets = GrowthCenterRules.TargetCandidates(inventory, roster);
            Assert.AreEqual(4, targets.Count);
            Assert.AreSame(star, targets[0], "라인업 우선 · OVR 순");
            Assert.AreEqual(2, GrowthCenterRules.TargetCandidates(inventory, roster, lineupOnly: true).Count);

            var awakenMaterials = GrowthCenterRules.MaterialCandidates(GrowthTab.Awaken, star, inventory, roster);
            CollectionAssert.AreEqual(new[] { copy }, awakenMaterials, "각성 = 동일 선수 · 라인업 제외");
            var enhanceMaterials = GrowthCenterRules.MaterialCandidates(GrowthTab.Enhance, star, inventory, roster);
            CollectionAssert.DoesNotContain(enhanceMaterials, regular, "라인업 카드는 재료 불가");
            CollectionAssert.DoesNotContain(enhanceMaterials, star);
            Assert.IsEmpty(GrowthCenterRules.MaterialCandidates(GrowthTab.Training, star, inventory, roster));

            star.ReinforceLevel = 10; // [TASK-KBO-189] 각성은 강화 +10강 선행
            var after = GrowthCenterRules.Simulate(GrowthTab.Awaken, star, awakenMaterials);
            Assert.AreEqual(0, star.AwakenLevel, "미리보기는 원본을 바꾸지 않는다");
            // [TASK-KBO-185] 동일 선수 · 동일 시즌 등급 · 동일 연도(테스트 카드는 모두 2026) = +3각(다른 연도면 +1각 - Task185Tests).
            Assert.AreEqual(3, after.AwakenLevel, "같은 시즌 · 같은 연도 카드(동일 선수) = 각성 +3");
        }

        [Test]
        public void GrowthCenter_AutoSelect_ReachesNextStep_AndTrainingPreview()
        {
            var target = Card(Grade.LIVE_NORMAL, 60, person: "P");
            var mats = Enumerable.Range(0, 8).Select(_ => Card(Grade.LIVE_NORMAL, 55)).ToList();
            var picked = GrowthCenterRules.AutoSelect(GrowthTab.Enhance, target, mats);
            Assert.That(picked.Count, Is.InRange(1, UpgradeManager.MaxEnhanceMaterials));
            Assert.GreaterOrEqual(GrowthCenterRules.Simulate(GrowthTab.Enhance, target, picked).ReinforceLevel, 1);

            var trained = GrowthCenterRules.Simulate(GrowthTab.Training, target, null, gold: 5000);
            Assert.AreEqual(1, trained.TrainingLevel);
            Assert.AreEqual(target.CalculateNeutralOVR() + 1, trained.CalculateNeutralOVR());
            var lines = GrowthCenterRules.StatPreviewLines(target, trained);
            Assert.AreEqual(5, lines.Count);
            StringAssert.Contains("→", lines[0]);
            StringAssert.Contains("각성", GrowthCenterRules.Describe(GrowthTab.Awaken, target, 0));
        }

        [Test]
        public void StorageFilter_ScopeTeamPositionGradeSort()
        {
            var lineupCard = Card(Grade.LIVE_NORMAL, 70, team: Team.Samsung, position: BatterPosition.Catcher);
            var kiaGg = Card(Grade.GOLDEN_GLOVE, 90, team: Team.KIA, position: BatterPosition.ShortStop);
            var samsungP = Card(Grade.LIVE_NORMAL, 62, pitcher: true, team: Team.Samsung, role: PitcherRole.Closer);
            var samsungOf = Card(Grade.LIVE_EPIC, 66, team: Team.Samsung, position: BatterPosition.CenterField);
            var inventory = new List<Player> { lineupCard, kiaGg, samsungP, samsungOf };
            var roster = new List<Player> { lineupCard };

            var f = new StorageFilter();
            CollectionAssert.AreEqual(new[] { kiaGg, samsungOf, samsungP }, f.Apply(inventory, roster, Team.Samsung), "보관 선수 = 라인업 제외, OVR 순");
            f.CycleScope();
            Assert.AreEqual(4, f.Apply(inventory, roster, Team.Samsung).Count, "전체 보유");
            while (f.Team != Team.Samsung) f.CycleTeam();
            CollectionAssert.DoesNotContain(f.Apply(inventory, roster, Team.Samsung), kiaGg);
            while (f.Position != StorageFilter.PositionGroup.Bullpen) f.CyclePosition();
            CollectionAssert.AreEqual(new[] { samsungP }, f.Apply(inventory, roster, Team.Samsung));
            f = new StorageFilter();
            while (f.Grade != Grade.GOLDEN_GLOVE) f.CycleGrade();
            CollectionAssert.AreEqual(new[] { kiaGg }, f.Apply(inventory, roster, Team.Samsung));
            f = new StorageFilter();
            while (f.Sort != StorageFilter.SortKind.GradeDesc) f.CycleSort();
            Assert.AreSame(kiaGg, f.Apply(inventory, roster, Team.Samsung)[0]);
            StringAssert.Contains("보관 선수", f.Summary);
        }
    }
}
