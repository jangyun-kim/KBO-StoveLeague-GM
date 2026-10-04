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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-KBO-190] 시즌 완주 루프(144경기 → 포스트시즌 → 6부문 타이틀 시상식 → 12단계 리그 승격 → 새 시즌 초기화 · AI 전력 재조정 · 유저 데이터 보존),
    /// 선수 3슬롯 스킬(풀 · 등급 확률 · 스킬 변경 / 고급 변경 / 레벨업 · 상시/조건부 스탯 · 경기 판정 · 세이브), 빠른 진행 30경기/시즌 완주,
    /// 상점 · 교환소 스킬 변경권, 리그 보상 스킬 변경권, 씬 배선(SeasonRewardManager · 시즌 완주 오버레이 · 성장 센터 스킬 버튼).
    /// </summary>
    public class Task190Tests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private SeasonCycleHarness harness;
        private bool sceneOpened;

        [TearDown]
        public void TearDown()
        {
            harness?.Dispose();
            harness = null;
            foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
            Task183Report.CleanupTemp();
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = false;
        }

        private Player Card(Grade grade, bool pitcher = false, string id = null)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = id ?? Guid.NewGuid().ToString("N");
            t.RealPlayerId = t.TemplateId;
            t.PlayerName = "T190_" + t.TemplateId.Substring(0, 4);
            t.Team = Team.Samsung;
            t.Grade = grade;
            t.SeasonYear = 2024;
            t.IsPitcher = pitcher;
            if (pitcher) { t.PitcherRole = PitcherRole.StartingPitcher; t.PitcherStats = StatProfiles.SpreadPitcher(80, t.PitcherRole, 3); }
            else { t.BatterPosition = BatterPosition.RightField; t.BatterStats = StatProfiles.SpreadBatter(80, t.BatterPosition, 3); }
            return new Player(id ?? Guid.NewGuid().ToString(), t);
        }

        // ================================================================== A. 시즌 완주 루프

        [Test]
        public void Season_144Games_PostSeason_Awards_Promotion_NextSeasonReset_PreservesUserData()
        {
            harness = SeasonCycleHarness.Create(75, LeagueTier.Amateur);
            var h = harness;
            var star = h.Gm.Roster.First(p => !p.Template.IsPitcher);
            star.ReinforceLevel = 7;
            star.SkillSlots[0].Level = 4;
            var starSlots = star.SkillSlots.Select(PlayerSkillRules.SlotLabel).ToList();
            var cards = h.Gm.Inventory.ToList();
            var roster = h.Gm.Roster.ToList();

            h.League.PlaySeasonToCompletion();
            Assert.AreEqual(LeagueManager.TotalUserGames, h.League.PlayedGameCount);
            Assert.IsTrue(SeasonCycle.IsSeasonOver(h.League));
            Assert.IsTrue(h.League.GetStandings().All(t => t.Wins + t.Draws + t.Losses == LeagueManager.TotalUserGames), "10개 구단 144경기");
            Assert.AreEqual(1, h.League.UserFinalRank, "OVR 75 vs 아마추어(57~64) = 체급 우위 → 정규시즌 1위");

            int trophy = h.Gm.Trophy, coin = h.Gm.GrowthCoin, gold = h.Gm.GameGold, skillTickets = h.Gm.SkillChangeTicket;
            var report = SeasonCycle.CompleteSeason(h.League, h.Post, h.Reward);
            Assert.IsNotNull(report);
            Assert.IsTrue(h.Post.ChampionTeam.HasValue, "유저 진출 포스트시즌을 한국시리즈까지 진행");
            Assert.LessOrEqual(report.FinalRank, 5);

            // 6부문 타이틀 - 실제 누적 기록 1위
            foreach (var c in new[] { "타격왕", "홈런왕", "타점왕", "다승왕", "탈삼진" })
                Assert.IsTrue(report.Titles.Any(t => t.Category == c), c + " 수상자");
            var hr = report.Titles.First(t => t.Category == "홈런왕");
            Assert.AreEqual(h.Stats.AllBatterStats.Max(kv => kv.Value.HomeRuns), h.Stats.GetBatterStats(hr.Player).HomeRuns, "홈런왕 = 실제 최다 홈런");
            var k = report.Titles.First(t => t.Category == "탈삼진");
            Assert.AreEqual(h.Stats.AllPitcherStats.Max(kv => kv.Value.Strikeouts), h.Stats.GetPitcherStats(k.Player).Strikeouts);
            var avg = report.Titles.FirstOrDefault(t => t.Category == "타격왕");
            Assert.GreaterOrEqual(h.Stats.GetBatterStats(avg.Player).PlateAppearances, LeagueManager.TotalUserGames * SeasonStatManager.QualifyingPlateAppearancesPerGame, "규정타석");
            int userTitles = report.Titles.Count(t => t.IsUserPlayer);
            Assert.AreEqual(userTitles, report.Titles.Count(t => h.Gm.Roster.Contains(t.Player)));
            Assert.AreEqual(new LeagueMaterialReward { Trophy = userTitles, GrowthCoin = 100 * userTitles, Points = 30000 * userTitles }.Summary(), report.TitleBonus.Summary());
            Assert.AreEqual(trophy + report.MaterialReward.Trophy + userTitles, h.Gm.Trophy, "트로피 = 순위 보상 + 수상 1건당 +1");
            Assert.AreEqual(coin + report.MaterialReward.GrowthCoin + 100 * userTitles, h.Gm.GrowthCoin);
            Assert.AreEqual(skillTickets + report.MaterialReward.SkillChangeTicket, h.Gm.SkillChangeTicket);
            Assert.Greater(report.MaterialReward.SkillChangeTicket, 0, "포스트시즌 이상 = 스킬 변경권");

            int goldAfter = h.Gm.GameGold;
            Assert.AreSame(report, h.Reward.GrantSeasonEndRewardOnce(), "결산 1회만");
            Assert.AreEqual(goldAfter, h.Gm.GameGold);
            Assert.IsTrue(report.PromotionEarned);
            Assert.AreEqual(LeagueTier.Futures, report.NextTier);

            h.Rollover.RolloverToNextSeason();
            Assert.AreEqual(LeagueTier.Futures, h.League.CurrentTier, "아마추어 → 퓨처스 승격");
            Assert.AreEqual((LeagueTier.Amateur, LeagueTier.Futures), h.League.LastPromotion.Value);
            Assert.AreEqual(0, h.League.PlayedGameCount);
            Assert.AreEqual(1, h.League.PeekNextFixture().GameNumber, "새 시즌 1/144");
            Assert.AreEqual(LeaguePhase.REGULAR_OPEN, h.League.CurrentPhase);
            Assert.IsTrue(h.League.GetStandings().All(t => t.Wins + t.Draws + t.Losses == 0), "승·무·패 초기화");
            Assert.AreEqual(0, h.Stats.AllBatterStats.Count + h.Stats.AllPitcherStats.Count, "시즌 개인 기록 초기화");
            Assert.IsFalse(h.Post.ChampionTeam.HasValue);
            Assert.IsFalse(h.Reward.HasGrantedThisSeason);
            var ai = h.AiTeamOvrs();
            var targets = LeagueTierTable.AiTeamOvrTargets(LeagueTier.Futures).OrderBy(x => x).ToArray();
            for (int i = 0; i < ai.Length; i++) Assert.LessOrEqual(Math.Abs(ai[i] - targets[i]), 1, $"AI {i}: {ai[i]} vs 목표 {targets[i]} (65 +0·1·1 / +2·3·3·4 / +6·7)");
            CollectionAssert.AreEquivalent(cards, h.Gm.Inventory, "보유 카드 100% 보존");
            CollectionAssert.AreEquivalent(roster, h.Gm.Roster, "라인업 보존");
            Assert.AreEqual(7, star.ReinforceLevel);
            CollectionAssert.AreEqual(starSlots, star.SkillSlots.Select(PlayerSkillRules.SlotLabel).ToList(), "스킬 성장 보존");
            Assert.AreEqual(goldAfter, h.Gm.GameGold, "재화 보존");
        }

        [Test]
        public void Promotion_RegularFirstOrKoreanSeriesChampion_TopTierStays()
        {
            Assert.IsTrue(SeasonCycle.ShouldPromote(1, null, Team.Samsung, LeagueTier.Amateur));
            Assert.IsTrue(SeasonCycle.ShouldPromote(4, Team.Samsung, Team.Samsung, LeagueTier.Pennant), "4위로 진출해 한국시리즈 우승");
            Assert.IsFalse(SeasonCycle.ShouldPromote(2, Team.KIA, Team.Samsung, LeagueTier.Pennant));
            Assert.IsFalse(SeasonCycle.ShouldPromote(7, null, Team.Samsung, LeagueTier.Pennant));
            Assert.IsFalse(SeasonCycle.ShouldPromote(1, Team.Samsung, Team.Samsung, LeagueTier.HallOfFame), "영구결번 리그 = 최상위");
            Assert.AreEqual(new[] { 65, 66, 66, 67, 68, 68, 69, 71, 72 }, LeagueTierTable.AiTeamOvrTargets(LeagueTier.Futures));
        }

        [Test]
        public void Awards_SixCategories_QualifiersAndUserBonus()
        {
            Player B(string n) { var p = Card(Grade.ALLSTAR); p.Template.PlayerName = n; return p; }
            Player P(string n) { var p = Card(Grade.ALLSTAR, true); p.Template.PlayerName = n; return p; }
            var a = B("A"); var b = B("B"); var c = B("C");
            var batters = new Dictionary<Player, BatterSeasonStats>
            {
                [a] = new BatterSeasonStats { AtBats = 10, Hits = 6, HomeRuns = 1, RunsBattedIn = 3 },                       // 타율 .600 - 규정타석 미달
                [b] = new BatterSeasonStats { AtBats = 500, Walks = 50, Hits = 170, HomeRuns = 40, RunsBattedIn = 90 },       // .340 / 40홈런
                [c] = new BatterSeasonStats { AtBats = 520, Walks = 40, Hits = 160, HomeRuns = 20, RunsBattedIn = 120 },      // 120타점
            };
            var x = P("X"); var y = P("Y");
            var pitchers = new Dictionary<Player, PitcherSeasonStats>
            {
                [x] = new PitcherSeasonStats { OutsRecorded = 540, EarnedRuns = 50, Wins = 18, Losses = 5, Strikeouts = 150 },
                [y] = new PitcherSeasonStats { OutsRecorded = 90, EarnedRuns = 2, Wins = 4, Losses = 1, Strikeouts = 210 },  // ERA 0.60 - 규정이닝 미달
            };
            var titles = SeasonAwardRules.Compute(batters, pitchers, 144, p => p == b || p == x);
            string Who(string cat) => titles.First(t => t.Category == cat).PlayerName;
            Assert.AreEqual(6, titles.Count);
            Assert.AreEqual("B", Who("타격왕"), "A(.600)는 규정타석 446.4 미달");
            Assert.AreEqual("B", Who("홈런왕"));
            Assert.AreEqual("C", Who("타점왕"));
            Assert.AreEqual("X", Who("다승왕"));
            Assert.AreEqual("X", Who("평균자책점"), "Y(0.60)는 규정이닝 144 미달");
            Assert.AreEqual("Y", Who("탈삼진"));
            Assert.AreEqual(4, titles.Count(t => t.IsUserPlayer));
            var bonus = SeasonAwardRules.UserBonus(titles);
            Assert.AreEqual((4, 400, 120000), (bonus.Trophy, bonus.GrowthCoin, bonus.Points), "수상 1건당 트로피 +1 · 코인 +100 · 30,000P");
            StringAssert.Contains(".340", titles.First(t => t.Category == "타격왕").ValueLabel);
        }

        // ================================================================== B. 3슬롯 스킬

        [Test]
        public void SkillPools_EightEach_PotencyDtoS_Lv1to6()
        {
            Assert.AreEqual(8, PlayerSkillRules.BatterSkills.Count);
            Assert.AreEqual(8, PlayerSkillRules.PitcherSkills.Count);
            CollectionAssert.IsSubsetOf(new[] { "배팅 머신", "거포 본능", "클러치 히터", "테이블세터", "수비 요정", "에이스 킬러", "초구 공략", "배트 컨트롤" },
                PlayerSkillRules.BatterSkills.Select(s => s.Name).ToList());
            CollectionAssert.IsSubsetOf(new[] { "언터처블", "닥터 K", "칼제구", "이닝 이터", "위기 관리", "수호신", "파이어볼러", "땅볼 유도" },
                PlayerSkillRules.PitcherSkills.Select(s => s.Name).ToList());
            Assert.AreEqual(1, PlayerSkillRules.Potency(SkillGrade.D, 1));
            Assert.AreEqual(5, PlayerSkillRules.Potency(SkillGrade.S, 1));
            Assert.AreEqual(10, PlayerSkillRules.Potency(SkillGrade.S, 6));
            Assert.AreEqual(10, PlayerSkillRules.Potency(SkillGrade.S, 99), "Lv.6 상한");
        }

        [Test]
        public void EnsureSlots_ThreeDistinct_RightPool_Deterministic_HigherGradeFavorsAS()
        {
            var batter = Card(Grade.LIVE_NORMAL, id: "same-id-190");
            var again = Card(Grade.LIVE_NORMAL, id: "same-id-190");
            var pitcher = Card(Grade.SIGNATURE, true);
            Assert.IsTrue(PlayerSkillRules.EnsureSlots(batter));
            PlayerSkillRules.EnsureSlots(again);
            PlayerSkillRules.EnsureSlots(pitcher);
            Assert.AreEqual(3, batter.SkillSlots.Select(s => s.SkillId).Distinct().Count());
            Assert.IsTrue(batter.SkillSlots.All(s => !PlayerSkillRules.Find(s.SkillId).ForPitcher && s.Level == 1));
            Assert.IsTrue(pitcher.SkillSlots.All(s => PlayerSkillRules.Find(s.SkillId).ForPitcher));
            CollectionAssert.AreEqual(batter.SkillSlots.Select(PlayerSkillRules.SlotLabel).ToList(), again.SkillSlots.Select(PlayerSkillRules.SlotLabel).ToList(),
                "같은 InstanceId = 같은 슬롯(구버전 세이브 결정적 부여)");
            Assert.IsFalse(PlayerSkillRules.EnsureSlots(batter), "정상 슬롯은 유지");

            var rng = new System.Random(5);
            int Top(Grade g) => Enumerable.Range(0, 4000).Count(_ => PlayerSkillRules.RollGrade(g, n => rng.Next(n)) >= SkillGrade.A);
            Assert.Greater(Top(Grade.SIGNATURE), Top(Grade.LIVE_NORMAL) * 3, "상위 시즌 카드일수록 A/S 우대(40% vs 8%)");

            var gm = new GameObject("T190GM").AddComponent<GameManager>();
            created.Add(gm.gameObject);
            var fresh = Card(Grade.ALLSTAR);
            gm.AddPlayerToInventory(fresh);
            Assert.AreEqual(3, fresh.SkillSlots.Count, "획득 즉시 3슬롯 부여");
        }

        [Test]
        public void Reroll_TicketThenPoints_PremiumGuaranteesAtoS_KeepsLevels()
        {
            var p = Card(Grade.LIVE_NORMAL);
            PlayerSkillRules.EnsureSlots(p);
            p.SkillSlots[1].Level = 5;
            var ledger = new MemoryGrowthLedger { Points = 20000, SkillChangeTicket = 1 };
            var rng = new System.Random(9);
            Assert.IsTrue(PlayerSkillRules.TryReroll(p, ledger, false, n => rng.Next(n), out var msg));
            Assert.AreEqual((0, 20000), (ledger.SkillChangeTicket, ledger.Points), "변경권 우선 소모");
            StringAssert.Contains("스킬 변경권 -1", msg);
            Assert.AreEqual(5, p.SkillSlots[1].Level, "슬롯 레벨 유지");
            Assert.AreEqual(3, p.SkillSlots.Select(s => s.SkillId).Distinct().Count());
            Assert.IsTrue(PlayerSkillRules.TryReroll(p, ledger, false, n => rng.Next(n), out _));
            Assert.AreEqual(5000, ledger.Points, "변경권이 없으면 15,000P");
            var before = p.SkillSlots.Select(PlayerSkillRules.SlotLabel).ToList();
            Assert.IsFalse(PlayerSkillRules.TryReroll(p, ledger, false, n => rng.Next(n), out var poor));
            StringAssert.Contains("15,000P", poor);
            CollectionAssert.AreEqual(before, p.SkillSlots.Select(PlayerSkillRules.SlotLabel).ToList(), "실패 시 변화 없음");

            Assert.IsFalse(PlayerSkillRules.TryReroll(p, ledger, true, n => 0, out var noPremium));
            StringAssert.Contains("고급 스킬 변경권", noPremium);
            ledger.PremiumSkillChangeTicket = 1;
            Assert.IsTrue(PlayerSkillRules.TryReroll(p, ledger, true, n => 0, out _), "난수 0 = 전부 D등급이 나오는 최악의 경우");
            Assert.AreEqual(0, ledger.PremiumSkillChangeTicket);
            Assert.IsTrue(p.SkillSlots.Any(s => s.Grade >= SkillGrade.A), "고급 변경 = 최소 1슬롯 A~S 확정");
        }

        [Test]
        public void LevelUp_TrainingTicketOrPoints_Lv1To6()
        {
            var p = Card(Grade.ALLSTAR);
            p.SkillSlots = new List<PlayerSkillSlot>
            {
                new PlayerSkillSlot { SkillId = "batting_machine", Grade = SkillGrade.B, Level = 1 },
                new PlayerSkillSlot { SkillId = "clutch_hitter", Grade = SkillGrade.A, Level = 1 },
                new PlayerSkillSlot { SkillId = "table_setter", Grade = SkillGrade.D, Level = 1 },
            };
            var ledger = new MemoryGrowthLedger { TrainingTicket = 1, Points = 1000000 };
            Assert.IsTrue(PlayerSkillRules.TryLevelUp(p, 0, ledger, out var msg));
            Assert.AreEqual((2, 0, 1000000), (p.SkillSlots[0].Level, ledger.TrainingTicket, ledger.Points), "Lv.1→2 = 특훈권 1장");
            StringAssert.Contains("Lv.1 → Lv.2", msg);
            Assert.IsTrue(PlayerSkillRules.TryLevelUp(p, 0, ledger, out _));
            Assert.AreEqual(1000000 - 20000, ledger.Points, "특훈권이 없으면 10,000 × 현재 레벨");
            for (int i = 0; i < 10; i++) PlayerSkillRules.TryLevelUp(p, 0, ledger, out _);
            Assert.AreEqual(6, p.SkillSlots[0].Level);
            Assert.IsFalse(PlayerSkillRules.TryLevelUp(p, 0, ledger, out var max));
            StringAssert.Contains("최대 레벨", max);
            Assert.IsFalse(PlayerSkillRules.TryLevelUp(p, 5, ledger, out _), "잘못된 슬롯");
            var poor = new MemoryGrowthLedger();
            Assert.IsFalse(PlayerSkillRules.TryLevelUp(p, 1, poor, out _));
            Assert.AreEqual(1, p.SkillSlots[1].Level);
        }

        [Test]
        public void SkillStats_AlwaysVsConditional_DetailRows_Labels_CloneAndSave()
        {
            var p = Card(Grade.ALLSTAR);
            p.SkillSlots = new List<PlayerSkillSlot>
            {
                new PlayerSkillSlot { SkillId = "batting_machine", Grade = SkillGrade.S, Level = 6 }, // 정확 +10 · 선구 +10
                new PlayerSkillSlot { SkillId = "clutch_hitter", Grade = SkillGrade.B, Level = 2 },   // 득점권 정확/파워 +6
                new PlayerSkillSlot { SkillId = "defense_fairy", Grade = SkillGrade.D, Level = 1 },   // 수비 +2(1.5 반올림) · 정확 +1(0.5 반올림)
            };
            CollectionAssert.AreEqual(new[] { 0, 11, 10, 0, 2 }, PlayerSkillRules.AlwaysStatBonuses(p), "상시만: 파워 · 정확 · 선구 · 주력 · 수비");
            var risp = PlayerSkillRules.BatterBonus(p, new SkillSituation { ScoringPosition = true });
            Assert.AreEqual((6, 17), (risp.Power, risp.Contact), "득점권 = 클러치 히터 추가 발동");
            var none = PlayerSkillRules.BatterBonus(p, new SkillSituation());
            Assert.AreEqual((0, 11), (none.Power, none.Contact));

            var rows = PlayerDetailRules.StatRows(p, 3);
            Assert.AreEqual(11, rows[1].Skill);
            Assert.AreEqual(rows[1].Base + rows[1].Bonus + 11, rows[1].Final, "상세창 최종 = 기본 + 성장/시너지 + 스킬");
            Assert.AreEqual("배팅 머신 [S] Lv.6", PlayerSkillRules.SlotLabel(p.SkillSlots[0]));
            StringAssert.Contains("#FF4D6D", PlayerSkillRules.SlotRichLabel(p.SkillSlots[0]));
            Assert.AreEqual("상시 · 정확 +10 · 선구 +10", PlayerSkillRules.EffectText(p.SkillSlots[0]));
            Assert.AreEqual(p.CalculateNeutralOVR(), GrowthCenterRules.Clone(p).CalculateNeutralOVR(), "스킬은 표시 OVR(정적)에 넣지 않음");

            var clone = GrowthCenterRules.Clone(p);
            clone.SkillSlots[0].Level = 1;
            Assert.AreEqual(6, p.SkillSlots[0].Level, "미리보기 복제는 깊은 복사");
            var json = JsonUtility.ToJson(new PlayerSaveData { SkillSlots = p.SkillSlots });
            var back = JsonUtility.FromJson<PlayerSaveData>(json);
            CollectionAssert.AreEqual(p.SkillSlots.Select(PlayerSkillRules.SlotLabel).ToList(), back.SkillSlots.Select(PlayerSkillRules.SlotLabel).ToList(), "세이브 왕복");
            var legacy = JsonUtility.FromJson<PlayerSaveData>("{\"InstanceId\":\"x\"}");
            Assert.IsNotNull(legacy.SkillSlots, "구버전 세이브 = 빈 목록(로드 시 결정적 부여)");
        }

        [Test]
        public void Engine_SkillSlots_RaiseBatterOutcomes_PitcherSkillsSuppress()
        {
            var home = Task183Report.ProceduralTeam(Team.LG, 80, "E190H");
            var away = Task183Report.ProceduralTeam(Team.KIA, 80, "E190A");
            var batter = home.First(p => !p.Template.IsPitcher);
            var pitcher = away.First(p => p.Template.IsPitcher);
            batter.SkillSlots = new List<PlayerSkillSlot>();
            pitcher.SkillSlots = new List<PlayerSkillSlot>();
            double Rate()
            {
                var engine = new MatchEngine(home, away, TeamPowerModifiers.None, TeamPowerModifiers.None, null, null, 190);
                int positive = 0;
                for (int i = 0; i < 4000; i++)
                {
                    var r = engine.SimulateAtBat(batter, pitcher, new MatchState());
                    if (r == AtBatResult.Single || r == AtBatResult.Double || r == AtBatResult.Triple || r == AtBatResult.HomeRun || r == AtBatResult.Walk) positive++;
                }
                return positive / 4000.0;
            }
            double baseline = Rate();
            batter.SkillSlots = new List<PlayerSkillSlot>
            {
                new PlayerSkillSlot { SkillId = "batting_machine", Grade = SkillGrade.S, Level = 6 },
                new PlayerSkillSlot { SkillId = "power_instinct", Grade = SkillGrade.S, Level = 6 },
                new PlayerSkillSlot { SkillId = "table_setter", Grade = SkillGrade.S, Level = 6 },
            };
            double boosted = Rate();
            Assert.Greater(boosted, baseline + 0.02, $"타자 S Lv.6 3슬롯 출루율 상승 {baseline:P1} → {boosted:P1}");
            pitcher.SkillSlots = new List<PlayerSkillSlot>
            {
                new PlayerSkillSlot { SkillId = "untouchable", Grade = SkillGrade.S, Level = 6 },
                new PlayerSkillSlot { SkillId = "pinpoint", Grade = SkillGrade.S, Level = 6 },
                new PlayerSkillSlot { SkillId = "groundball", Grade = SkillGrade.S, Level = 6 },
            };
            double suppressed = Rate();
            Assert.Less(suppressed, boosted - 0.02, $"투수 스킬이 다시 억제 {boosted:P1} → {suppressed:P1}");
        }

        // ================================================================== C. 빠른 진행 · 상점 · 리그 보상

        [Test]
        public void QuickPresets_30Games_SeasonAll_ClampToRemaining()
        {
            CollectionAssert.Contains(MatchModeRules.QuickCountPresets, 30);
            CollectionAssert.Contains(MatchModeRules.QuickCountPresets, MatchModeRules.SeasonAll);
            Assert.AreEqual(30, MatchModeRules.ClampQuickCount(30, 144));
            Assert.AreEqual(12, MatchModeRules.ClampQuickCount(30, 12), "남은 경기 수로 클램프");
            Assert.AreEqual(87, MatchModeRules.ClampQuickCount(MatchModeRules.SeasonAll, 87), "시즌 완주 = 잔여 경기 전체");
            Assert.AreEqual("시즌 완주", MatchModeRules.PresetLabel(MatchModeRules.SeasonAll));
            Assert.AreEqual("30경기", MatchModeRules.PresetLabel(30));
            Assert.IsTrue(MatchModeRules.IsPresetSelected(MatchModeRules.SeasonAll, 87, 87));
            Assert.IsFalse(MatchModeRules.IsPresetSelected(MatchModeRules.SeasonAll, 30, 87));

            int started = 0;
            QuickSeriesSummary done = null;
            QuickSeriesRunner runner = null;
            var win = new MatchResult { HomeTeamName = "Samsung", AwayTeamName = "KIA", WinnerTeamName = "Samsung" };
            runner = new QuickSeriesRunner(() => { started++; runner.HandleMatchCompleted(win, Team.Samsung); }, () => true, null, null, null);
            runner.Finished += s => done = s;
            Assert.IsTrue(runner.Begin(MatchModeRules.SeasonAll, 87));
            Assert.AreEqual(87, started);
            Assert.AreEqual(87, done.Played);
            StringAssert.StartsWith("87경기 87승", done.Label);
        }

        [Test]
        public void QuickSeason_RealLeague_30GamesThenSeasonAllReaches144()
        {
            harness = SeasonCycleHarness.Create(70, LeagueTier.Amateur, Team.Samsung, "Q190");
            var league = harness.League;
            int Remaining() => MatchModeRules.RemainingRegularGames(league.PlayedGameCount, LeagueManager.TotalUserGames, true, league.PeekNextFixture() != null);
            var runner = new QuickSeriesRunner(() => league.PlayNextMatch(), () => league.PeekNextFixture() != null, null, null, null);
            // 헤드리스: PlayNextMatch가 경기를 끝내므로 완료 통지를 직접 반복한다.
            void Run(int requested)
            {
                int count = MatchModeRules.ClampQuickCount(requested, Remaining());
                for (int i = 0; i < count; i++) league.PlayNextMatch();
            }
            Run(30);
            Assert.AreEqual(30, league.PlayedGameCount);
            Run(MatchModeRules.SeasonAll);
            Assert.AreEqual(LeagueManager.TotalUserGames, league.PlayedGameCount, "시즌 완주 = 144경기까지");
            Assert.IsTrue(SeasonCycle.IsSeasonOver(league));
            Assert.AreEqual(0, MatchModeRules.ClampQuickCount(MatchModeRules.SeasonAll, Remaining()), "더 진행할 경기 없음");
            Assert.IsNotNull(runner);
        }

        [Test]
        public void Shop_SkillChangeTickets_PointsCoinsTrophy()
        {
            var ledger = new MemoryGrowthLedger { Points = 12000, GrowthCoin = 750, Trophies = 1 };
            CollectionAssert.Contains(ShopExchangeRules.PointProducts, ShopProduct.SkillChangeTicket);
            CollectionAssert.IsSubsetOf(new[] { ShopProduct.SkillChangeTicketCoin, ShopProduct.PremiumSkillChangeTicket, ShopProduct.PremiumSkillChangeTicketTrophy },
                ShopExchangeRules.CoinProducts);
            Assert.AreEqual(ShopCurrency.Trophy, ShopExchangeRules.CurrencyOf(ShopProduct.PremiumSkillChangeTicketTrophy));
            Assert.AreEqual(ShopCurrency.GrowthCoin, ShopExchangeRules.CurrencyOf(ShopProduct.RecruitMaterialBox), "기존 상품 재화 유지");
            Assert.IsTrue(ShopExchangeRules.TryBuy(ShopProduct.SkillChangeTicket, ledger, "RF", Grade.ALLSTAR, null, null, null, out _, out var m1));
            StringAssert.Contains("스킬 변경권 1장", m1);
            Assert.IsTrue(ShopExchangeRules.TryBuy(ShopProduct.SkillChangeTicketCoin, ledger, "RF", Grade.ALLSTAR, null, null, null, out _, out _));
            Assert.IsTrue(ShopExchangeRules.TryBuy(ShopProduct.PremiumSkillChangeTicket, ledger, "RF", Grade.ALLSTAR, null, null, null, out _, out _));
            Assert.IsTrue(ShopExchangeRules.TryBuy(ShopProduct.PremiumSkillChangeTicketTrophy, ledger, "RF", Grade.ALLSTAR, null, null, null, out _, out _));
            Assert.AreEqual((0, 0, 0, 2, 2), (ledger.Points, ledger.GrowthCoin, ledger.Trophies, ledger.SkillChangeTicket, ledger.PremiumSkillChangeTicket));
            Assert.IsFalse(ShopExchangeRules.TryBuy(ShopProduct.PremiumSkillChangeTicketTrophy, ledger, "RF", Grade.ALLSTAR, null, null, null, out _, out var poor));
            StringAssert.Contains("트로피 부족", poor);
            Assert.AreEqual("트로피 1", ShopExchangeRules.PriceLabel(ShopProduct.PremiumSkillChangeTicketTrophy));
        }

        [Test]
        public void LeagueRewards_SkillTickets_MatchChance_SeasonRanks()
        {
            var win = LeagueMaterialRewards.ForMatch(LeagueTier.Amateur, true, n => 0);
            Assert.AreEqual(1, win.SkillChangeTicket, "승리 확률 보상(5+i%)");
            Assert.AreEqual(0, LeagueMaterialRewards.ForMatch(LeagueTier.Amateur, false, n => 0).SkillChangeTicket);
            Assert.AreEqual(16, LeagueMaterialRewards.SkillTicketChancePercent(LeagueTier.HallOfFame, true));
            var champ = LeagueMaterialRewards.ForSeason(LeagueTier.Major, 1);
            Assert.AreEqual((3, 1), (champ.SkillChangeTicket, champ.PremiumSkillChangeTicket));
            var second = LeagueMaterialRewards.ForSeason(LeagueTier.Amateur, 2);
            Assert.AreEqual((2, 1), (second.SkillChangeTicket, second.Trophy), "준우승 차등 - 트로피 · 스킬 변경권");
            Assert.AreEqual(1, LeagueMaterialRewards.ForSeason(LeagueTier.Amateur, 3).Trophy, "3위 트로피");
            Assert.AreEqual(0, LeagueMaterialRewards.ForSeason(LeagueTier.Amateur, 5).Trophy);
            Assert.AreEqual(0, LeagueMaterialRewards.ForSeason(LeagueTier.Amateur, 7).SkillChangeTicket);
            var ledger = new MemoryGrowthLedger();
            champ.ApplyTo(ledger);
            Assert.AreEqual((3, 1), (ledger.SkillChangeTicket, ledger.PremiumSkillChangeTicket));
            StringAssert.Contains("고급 스킬 변경권 +1", champ.Summary());
        }

        // ================================================================== D. 씬

        [Test]
        public void Scene_SeasonRewardManagerWired_SeasonCycleOverlay_GrowthSkillButtons_QuickAndShopRows()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
            var reward = UnityEngine.Object.FindAnyObjectByType<SeasonRewardManager>(FindObjectsInactive.Include);
            Assert.IsNotNull(reward, "SeasonRewardManager 씬 배치");
            var so = new UnityEditor.SerializedObject(reward);
            foreach (var f in new[] { "leagueManager", "postSeasonManager", "seasonStatManager" })
                Assert.IsNotNull(so.FindProperty(f).objectReferenceValue, f);
            var rollover = UnityEngine.Object.FindAnyObjectByType<SeasonRollover>(FindObjectsInactive.Include);
            Assert.AreEqual(reward, new UnityEditor.SerializedObject(rollover).FindProperty("seasonRewardManager").objectReferenceValue);

            var cycle = UnityEngine.Object.FindAnyObjectByType<SeasonCycleView>(FindObjectsInactive.Include);
            Assert.IsNotNull(cycle, "시즌 완주 오버레이");
            Assert.AreEqual(SetupTask190.LobbyRoot(), cycle.transform.parent.gameObject, "로비 화면 루트 아래");
            Assert.IsNotNull(cycle.transform.Find(SeasonCycleView.RootName + "/Primary"));

            bool Has(Component c, string name) => c != null && c.GetComponentsInChildren<Transform>(true).Any(t => t.name == name);
            var growth = UnityEngine.Object.FindAnyObjectByType<GrowthCenterView>(FindObjectsInactive.Include);
            foreach (var n in new[] { "SkillReroll", "SkillPremium", "SkillLevelUp" }) Assert.IsTrue(Has(growth, n), "성장 센터 " + n);
            var match = UnityEngine.Object.FindAnyObjectByType<CompyaMatchView>(FindObjectsInactive.Include);
            Assert.IsTrue(Has(match, "Preset30") && Has(match, "PresetSeasonAll"), "빠른 진행 30경기 · 시즌 완주 버튼");
            var shop = UnityEngine.Object.FindAnyObjectByType<ShopExchangeView>(FindObjectsInactive.Include);
            Assert.IsTrue(Has(shop, "Buy6"), "상점 7행");
        }
    }
}
