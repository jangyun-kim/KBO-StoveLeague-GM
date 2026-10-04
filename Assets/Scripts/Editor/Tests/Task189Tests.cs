using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.EditorTools;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-KBO-189] 강화 +10강 후 각성 해금 · 같은 시즌 · 같은 포지션 각성 재료 · 초월 복합 재료(+10강 · 9각 · 동일 선수 1장 + 동포지션 2장/+5강 1장 + 포인트/트로피) ·
    /// 선수 방출(성장 코인) · 3:1 포지션 재조합 · 상점/교환소 7종 · 리그 단계별 재료 보상 · 씬 UI(상점 · 교환소 탭, 성장 센터 초월 탭/바로가기).
    /// </summary>
    public class Task189Tests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private bool sceneOpened;

        [TearDown]
        public void TearDown()
        {
            foreach (var type in new[] { typeof(GameManager), typeof(LeagueManager) }) SetInstance(type, null);
            foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = false;
        }

        private static void SetInstance(Type type, object value)
        {
            var setter = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetSetMethod(true);
            setter?.Invoke(null, new[] { value });
        }

        private PlayerTemplate Template(Grade grade, string person, string pos = "RF", int year = 2024, Team team = Team.Samsung)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = $"{person}_{grade}_{year}_{pos}_{Guid.NewGuid():N}";
            t.RealPlayerId = person;
            t.PlayerName = person;
            t.SeasonYear = year;
            t.Team = team;
            t.Grade = grade;
            switch (pos)
            {
                case "SP": t.IsPitcher = true; t.PitcherRole = PitcherRole.StartingPitcher; break;
                case "RP": t.IsPitcher = true; t.PitcherRole = PitcherRole.WinningReliever; break;
                case "CP": t.IsPitcher = true; t.PitcherRole = PitcherRole.Closer; break;
                case "SS": t.BatterPosition = BatterPosition.ShortStop; break;
                case "C": t.BatterPosition = BatterPosition.Catcher; break;
                default: t.BatterPosition = BatterPosition.RightField; break;
            }
            if (t.IsPitcher) t.PitcherStats = StatProfiles.SpreadPitcher(80, t.PitcherRole, 7);
            else t.BatterStats = StatProfiles.SpreadBatter(80, t.BatterPosition, 7);
            return t;
        }

        private Player Card(Grade grade, string person, string pos = "RF", int reinforce = 0, int awaken = 0, int year = 2024, Team team = Team.Samsung) =>
            new Player(Guid.NewGuid().ToString(), Template(grade, person, pos, year, team)) { ReinforceLevel = reinforce, AwakenLevel = awaken };

        private MemoryGrowthLedger Ledger(params Player[] cards)
        {
            var ledger = new MemoryGrowthLedger { FavoriteTeam = Team.Samsung };
            ledger.Cards.AddRange(cards);
            return ledger;
        }

        // ================================================================== A. +10강 선행 · 같은 시즌 · 같은 포지션 각성

        [Test]
        public void Awaken_LockedUntilPlus10_ExactMessage()
        {
            var target = Card(Grade.GOLDEN_GLOVE, "KOO", "RF", reinforce: 9);
            var same = Card(Grade.GOLDEN_GLOVE, "KOO", "RF", year: 2021);
            Assert.IsFalse(CardGrowthRules.CanAwakenNow(target, out var reason));
            Assert.AreEqual("강화 +10강 달성 후 각성을 진행할 수 있습니다 (현재 +9/10강)", reason);
            Assert.IsFalse(UpgradeManager.ApplyAwaken(target, new List<Player> { same }), "+9강은 유효 재료가 있어도 각성 불가");
            Assert.AreEqual(0, target.AwakenLevel);
            StringAssert.Contains("(현재 +9/10강)", GrowthCenterRules.Describe(GrowthTab.Awaken, target, 0));
            StringAssert.Contains("강화 탭으로 이동", GrowthCenterRules.Describe(GrowthTab.Awaken, target, 0));
            Assert.AreEqual(0, GrowthCenterRules.Simulate(GrowthTab.Awaken, target, new[] { same }).AwakenLevel, "잠금 상태 미리보기도 변화 없음");

            target.ReinforceLevel = 10;
            Assert.IsTrue(CardGrowthRules.CanAwakenNow(target, out _));
            Assert.IsTrue(UpgradeManager.ApplyAwaken(target, new List<Player> { same }));
            Assert.AreEqual(3, target.AwakenLevel, "+10강 완료 후 같은 선수 +3각");
        }

        [Test]
        public void Awaken_SamePlayerPlus3_SamePositionPlus1_OtherPositionRejected()
        {
            var target = Card(Grade.ALLSTAR, "KOO", "RF", reinforce: 10);
            var samePlayerOtherPos = Card(Grade.ALLSTAR, "KOO", "SS", year: 2019);
            var samePos = Card(Grade.ALLSTAR, "LEE", "RF");
            var otherPos = Card(Grade.ALLSTAR, "PARK", "SS");
            var otherGrade = Card(Grade.FRANCHISE, "CHOI", "RF");
            Assert.AreEqual(3, CardGrowthRules.AwakenGainFor(target, samePlayerOtherPos), "같은 선수는 연도 · 포지션 무관 +3각");
            Assert.AreEqual(1, CardGrowthRules.AwakenGainFor(target, samePos), "같은 포지션(RF) 다른 선수 +1각");
            Assert.AreEqual(0, CardGrowthRules.AwakenGainFor(target, otherPos), "다른 포지션 다른 선수는 재료 불가");
            Assert.AreEqual(0, CardGrowthRules.AwakenGainFor(target, otherGrade), "다른 시즌 등급 불가");
            Assert.AreEqual("[같은 선수 +3각]", CardGrowthRules.AwakenMaterialBadge(target, samePlayerOtherPos));
            Assert.AreEqual("[같은 포지션 +1각]", CardGrowthRules.AwakenMaterialBadge(target, samePos));
            Assert.AreEqual("", CardGrowthRules.AwakenMaterialBadge(target, otherPos));

            var lineup = Card(Grade.ALLSTAR, "LEE", "RF");
            var inventory = new List<Player> { target, samePlayerOtherPos, samePos, otherPos, otherGrade, lineup };
            var candidates = GrowthCenterRules.MaterialCandidates(GrowthTab.Awaken, target, inventory, new[] { lineup, target });
            CollectionAssert.AreEqual(new[] { samePlayerOtherPos, samePos }, candidates, "후보 = 같은 선수(+3) → 같은 포지션(+1), 다른 포지션 · 라인업 제외");

            Assert.IsFalse(UpgradeManager.ApplyAwaken(target, new List<Player> { otherPos }), "다른 포지션만 넣으면 실패");
            Assert.IsTrue(UpgradeManager.ApplyAwaken(target, new List<Player> { samePos, otherPos }));
            Assert.AreEqual(1, target.AwakenLevel, "다른 포지션 카드는 0각으로 계산");
        }

        [Test]
        public void Awaken_PitcherRoles_SpRpCpAreSeparatePositions()
        {
            var sp = Card(Grade.GOLDEN_GLOVE, "WON", "SP", reinforce: 10);
            Assert.AreEqual("SP", CardGrowthRules.PositionKey(sp));
            Assert.AreEqual("RP", CardGrowthRules.PositionKey(Card(Grade.GOLDEN_GLOVE, "OH", "RP")));
            Assert.AreEqual("CP", CardGrowthRules.PositionKey(Card(Grade.GOLDEN_GLOVE, "OH", "CP")));
            Assert.AreEqual(1, CardGrowthRules.AwakenGainFor(sp, Card(Grade.GOLDEN_GLOVE, "BAEK", "SP")));
            Assert.AreEqual(0, CardGrowthRules.AwakenGainFor(sp, Card(Grade.GOLDEN_GLOVE, "BAEK", "RP")));
            Assert.AreEqual(0, CardGrowthRules.AwakenGainFor(sp, Card(Grade.GOLDEN_GLOVE, "BAEK", "CP")));
        }

        [Test]
        public void Awaken_MaterialsStopAt9_TranscendOnlyViaCompositeRecipe()
        {
            var target = Card(Grade.GOLDEN_GLOVE, "KOO", "RF", reinforce: 10, awaken: 8);
            Assert.IsTrue(UpgradeManager.ApplyAwaken(target, new List<Player> { Card(Grade.GOLDEN_GLOVE, "KOO", "RF", year: 2020) }));
            Assert.AreEqual(9, target.AwakenLevel, "8각 + 3 = 9각에서 멈춘다(초월 아님)");
            Assert.IsFalse(target.IsTranscended);
            Assert.IsFalse(UpgradeManager.ApplyAwaken(target, new List<Player> { Card(Grade.GOLDEN_GLOVE, "KOO", "RF", year: 2021) }), "9각은 재료 각성 불가");
            Assert.IsFalse(CardGrowthRules.CanAwakenNow(target, out var reason));
            StringAssert.Contains("[초월] 탭", reason);
        }

        // ================================================================== B. 초월 복합 재료

        [Test]
        public void Transcend_Prerequisites_Plus10And9Stage()
        {
            Assert.IsFalse(TranscendRules.CanAttempt(Card(Grade.GOLDEN_GLOVE, "KOO", reinforce: 9, awaken: 9), out var r1));
            StringAssert.Contains("+10강", r1);
            Assert.IsFalse(TranscendRules.CanAttempt(Card(Grade.GOLDEN_GLOVE, "KOO", reinforce: 10, awaken: 8), out var r2));
            StringAssert.Contains("9각", r2);
            Assert.IsFalse(TranscendRules.CanAttempt(Card(Grade.ALLSTAR, "KOO", reinforce: 10, awaken: 9), out _), "올스타는 9각 한계(초월 불가)");
            Assert.IsTrue(TranscendRules.CanAttempt(Card(Grade.GOLDEN_GLOVE, "KOO", reinforce: 10, awaken: 9), out _));
            Assert.IsTrue(TranscendRules.CanAttempt(Card(Grade.LIVE_NORMAL, "KOO", reinforce: 10, awaken: 9), out _));
            Assert.AreEqual((30000, 1), (TranscendRules.PointCost(Grade.LIVE_EPIC), TranscendRules.TrophyCost(Grade.LIVE_EPIC)));
            Assert.AreEqual((60000, 2), (TranscendRules.PointCost(Grade.TITLE_HOLDER), TranscendRules.TrophyCost(Grade.TITLE_HOLDER)));
            foreach (var g in new[] { Grade.GOLDEN_GLOVE, Grade.SIGNATURE, Grade.DYNASTY, Grade.RETIRED_NUMBER })
                Assert.AreEqual((100000, 3), (TranscendRules.PointCost(g), TranscendRules.TrophyCost(g)), g.ToString());
        }

        [Test]
        public void Transcend_CoreAndTwoSupport_ConsumesAll()
        {
            var target = Card(Grade.GOLDEN_GLOVE, "KOO", "RF", reinforce: 10, awaken: 9);
            var core = Card(Grade.GOLDEN_GLOVE, "KOO", "RF", year: 2021);
            var s1 = Card(Grade.GOLDEN_GLOVE, "LEE", "RF");
            var s2 = Card(Grade.GOLDEN_GLOVE, "PARK", "RF");
            var wrongPos = Card(Grade.GOLDEN_GLOVE, "CHOI", "SS");
            var ledger = Ledger(target, core, s1, s2, wrongPos);
            ledger.Lineup.Add(target);
            ledger.Points = 150000;
            ledger.Trophies = 5;

            // 단일 재료 1장(구 규칙)으로는 불가
            var single = TranscendRules.Assign(target, new[] { core }, false);
            StringAssert.Contains("[슬롯 2 보조]", TranscendRules.Validate(target, single, ledger));
            // 다른 포지션은 보조 재료 불가
            Assert.IsFalse(TranscendRules.IsSupportMaterial(target, wrongPos));
            Assert.AreEqual(1, TranscendRules.Assign(target, new[] { core, s1, wrongPos }, false).Support.Count);

            var selection = TranscendRules.Assign(target, new[] { core, s1, s2 }, false);
            Assert.IsNull(TranscendRules.Validate(target, selection, ledger));
            Assert.IsTrue(TranscendRules.TryTranscend(target, selection, ledger, out var message), message);
            Assert.IsTrue(target.IsTranscended);
            Assert.AreEqual("초월", target.AwakenLabel);
            Assert.AreEqual(50000, ledger.Points, "GG 100,000P 소모");
            Assert.AreEqual(2, ledger.Trophies, "GG 트로피 3개 소모");
            CollectionAssert.AreEquivalent(new[] { target, wrongPos }, ledger.Cards, "핵심 1 + 보조 2장 소모");
        }

        [Test]
        public void Transcend_Plus5SupportCountsAlone_TicketReplacesCore_FailureChangesNothing()
        {
            var target = Card(Grade.LIVE_NORMAL, "KIM", "SS", reinforce: 10, awaken: 9);
            var enhanced = Card(Grade.LIVE_NORMAL, "NA", "SS", reinforce: 5);
            var plain = Card(Grade.LIVE_NORMAL, "HA", "SS", reinforce: 4);
            var ledger = Ledger(target, enhanced, plain);
            ledger.Points = 30000;
            ledger.Trophies = 1;

            Assert.IsFalse(TranscendRules.SupportSatisfied(new[] { plain }), "+4강 1장은 부족");
            Assert.IsTrue(TranscendRules.SupportSatisfied(new[] { enhanced }), "+5강 1장으로 슬롯 2 충족");
            var noCore = TranscendRules.Assign(target, new[] { enhanced }, false);
            StringAssert.Contains("[슬롯 1 핵심]", TranscendRules.Validate(target, noCore, ledger));
            var ticketNoStock = TranscendRules.Assign(target, new[] { enhanced }, true);
            StringAssert.Contains("대체권", TranscendRules.Validate(target, ticketNoStock, ledger));

            ledger.TranscendTicket = 1;
            ledger.Points = 29999;
            Assert.IsFalse(TranscendRules.TryTranscend(target, ticketNoStock, ledger, out var lack));
            StringAssert.Contains("재화 부족", lack);
            Assert.AreEqual(9, target.AwakenLevel);
            Assert.AreEqual(3, ledger.Cards.Count, "실패하면 재료 그대로");
            Assert.AreEqual(1, ledger.TranscendTicket);

            ledger.Points = 30000;
            Assert.IsTrue(TranscendRules.TryTranscend(target, ticketNoStock, ledger, out var ok), ok);
            Assert.IsTrue(target.IsTranscended);
            Assert.AreEqual(0, ledger.TranscendTicket, "핵심 대체권 1개 소모");
            Assert.AreEqual((0, 0), (ledger.Points, ledger.Trophies), "LIVE 30,000P + 트로피 1개");
            CollectionAssert.AreEquivalent(new[] { target, plain }, ledger.Cards, "+5강 보조 1장만 소모");
        }

        [Test]
        public void Transcend_AutoAssign_SkipsLineup_PrefersPlainPair_ThenTicket()
        {
            var target = Card(Grade.SIGNATURE, "YANG", "C", reinforce: 10, awaken: 9);
            var lineupCore = Card(Grade.SIGNATURE, "YANG", "C", year: 2015);
            var p1 = Card(Grade.SIGNATURE, "KANG", "C");
            var p2 = Card(Grade.SIGNATURE, "PARK", "C");
            var plus6 = Card(Grade.SIGNATURE, "LEE", "C", reinforce: 6);
            var inventory = new List<Player> { target, lineupCore, p1, p2, plus6 };
            var auto = TranscendRules.AutoAssign(target, inventory, new[] { lineupCore }, coreTickets: 1);
            Assert.IsTrue(auto.UseCoreTicket, "라인업 같은 선수 카드는 제외 → 대체권 사용");
            CollectionAssert.AreEquivalent(new[] { p1, p2 }, auto.Support, "일반 2장 우선(+6강 카드 보존)");

            var auto2 = TranscendRules.AutoAssign(target, new List<Player> { target, plus6 }, null, 0);
            CollectionAssert.AreEqual(new[] { plus6 }, auto2.Support, "일반 카드가 모자라면 +5강 이상 1장");
            Assert.IsNull(auto2.Core);
            Assert.IsFalse(auto2.UseCoreTicket);
            Assert.AreEqual(GrowthTab.Transcend, GrowthCenterRules.Tabs[2], "성장 센터 5탭 중 3번째 = 초월");
            Assert.AreEqual(CardGrowthRules.TranscendLevel, GrowthCenterRules.Simulate(GrowthTab.Transcend, target, null).AwakenLevel);
        }

        // ================================================================== C. 방출 · 재조합 · 상점 · 리그 보상

        [Test]
        public void Release_GivesPointsAndCoins_ScaledByGradeAndReinforce_LineupBlocked()
        {
            var live = Card(Grade.LIVE_NORMAL, "A");
            var gg5 = Card(Grade.GOLDEN_GLOVE, "B", reinforce: 5);
            var lineup = Card(Grade.SIGNATURE, "C");
            Assert.AreEqual((300, 5), ShopExchangeRules.ReleaseReward(live));
            Assert.AreEqual((7500, 150), ShopExchangeRules.ReleaseReward(gg5), "GG 5,000P · 100코인 × 1.5");
            var ledger = Ledger(live, gg5, lineup);
            ledger.Lineup.Add(lineup);
            CollectionAssert.DoesNotContain(ShopExchangeRules.ReleaseCandidates(ledger.Cards, ledger.Lineup), lineup);
            Assert.IsFalse(ShopExchangeRules.TryRelease(new[] { live, lineup }, ledger, out _, out _, out var blocked));
            StringAssert.Contains("라인업", blocked);
            Assert.AreEqual(3, ledger.Cards.Count);

            Assert.IsTrue(ShopExchangeRules.TryRelease(new[] { live, gg5 }, ledger, out int pts, out int coins, out _));
            Assert.AreEqual((7800, 155), (pts, coins));
            Assert.AreEqual((7800, 155), (ledger.Points, ledger.GrowthCoin));
            CollectionAssert.AreEqual(new[] { lineup }, ledger.Cards);
        }

        [Test]
        public void Recombine_ThreeSameGrade_ToChosenPosition_FavoriteTeamFirst()
        {
            var m = new[] { Card(Grade.ALLSTAR, "A", "SP"), Card(Grade.ALLSTAR, "B", "C"), Card(Grade.ALLSTAR, "C", "CP") };
            var ledger = Ledger(m);
            var templates = new List<PlayerTemplate>
            {
                Template(Grade.ALLSTAR, "KIA_RF", "RF", team: Team.KIA),
                Template(Grade.ALLSTAR, "SAM_RF", "RF", team: Team.Samsung),
                Template(Grade.ALLSTAR, "SAM_SS", "SS", team: Team.Samsung),
                Template(Grade.TITLE_HOLDER, "SAM_TH_RF", "RF", team: Team.Samsung),
            };
            Assert.IsTrue(ShopExchangeRules.TryRecombine(m, "RF", ledger, templates, n => 0, null, out var card, out var message), message);
            Assert.AreEqual("SAM_RF", card.Template.PlayerName, "선택 구단(삼성) · 같은 등급 · 지정 포지션");
            Assert.AreEqual(Grade.ALLSTAR, card.Template.Grade);
            CollectionAssert.AreEqual(new[] { card }, ledger.Cards, "재료 3장 → 1장");

            var mixed = Ledger(Card(Grade.ALLSTAR, "A"), Card(Grade.ALLSTAR, "B"), Card(Grade.FRANCHISE, "C"));
            StringAssert.Contains("같은 시즌 등급", ShopExchangeRules.ValidateRecombine(mixed.Cards, mixed));
            var two = Ledger(Card(Grade.ALLSTAR, "A"), Card(Grade.ALLSTAR, "B"));
            StringAssert.Contains("(2/3)", ShopExchangeRules.ValidateRecombine(two.Cards, two));
            var kiaOnly = ShopExchangeRules.RecombinePool(templates, Grade.ALLSTAR, "RF", Team.Hanwha);
            Assert.AreEqual(2, kiaOnly.Count, "선택 구단 카드가 없으면 같은 등급 · 포지션 전체");
        }

        [Test]
        public void Shop_AllSevenProducts_PurchasableWithCorrectCurrency()
        {
            var templates = new List<PlayerTemplate>
            {
                Template(Grade.LIVE_NORMAL, "L_RF", "RF"), Template(Grade.LIVE_EPIC, "E_RF", "RF"),
                Template(Grade.LIVE_NORMAL, "L_SS", "SS"), Template(Grade.FRANCHISE, "F_RF", "RF"),
                Template(Grade.ALLSTAR, "A_SS", "SS"), Template(Grade.LIVE_EPIC, "E_SS", "SS"),
            };
            var ledger = Ledger();
            ledger.Points = 15000;
            ledger.GrowthCoin = 3100;

            Assert.IsTrue(ShopExchangeRules.TryBuy(ShopProduct.LivePositionPack, ledger, "RF", Grade.ALLSTAR, templates, n => n == 100 ? 99 : 0, null, out var live, out _));
            Assert.AreEqual("RF", CardGrowthRules.PositionKey(live[0]));
            Assert.IsTrue(CardGrowthRules.IsLive(live[0].Template.Grade));
            Assert.IsTrue(ShopExchangeRules.TryBuy(ShopProduct.TrainingBox, ledger, "RF", Grade.ALLSTAR, templates, null, null, out _, out _));
            Assert.IsTrue(ShopExchangeRules.TryBuy(ShopProduct.EnhanceSupportPack, ledger, "RF", Grade.ALLSTAR, templates, null, null, out var fodder, out _));
            Assert.AreEqual(3, fodder.Count);
            Assert.AreEqual(0, ledger.Points, "3,000 + 8,000 + 4,000 = 15,000P");
            Assert.AreEqual(1, ledger.TrainingTicket);
            Assert.IsFalse(ShopExchangeRules.TryBuy(ShopProduct.LivePositionPack, ledger, "RF", Grade.ALLSTAR, templates, null, null, out _, out var poor));
            StringAssert.Contains("포인트 부족", poor);

            Assert.IsTrue(ShopExchangeRules.TryBuy(ShopProduct.SpecialPositionPack, ledger, "RF", Grade.FRANCHISE, templates, null, null, out var special, out _));
            Assert.AreEqual(("F_RF", Grade.FRANCHISE), (special[0].Template.PlayerName, special[0].Template.Grade), "지정 등급 · 지정 포지션 저격");
            Assert.IsTrue(ShopExchangeRules.TryBuy(ShopProduct.AwakenTicket, ledger, "RF", Grade.ALLSTAR, templates, null, null, out _, out _));
            Assert.IsTrue(ShopExchangeRules.TryBuy(ShopProduct.TranscendTicket, ledger, "RF", Grade.ALLSTAR, templates, null, null, out _, out _));
            Assert.IsTrue(ShopExchangeRules.TryBuy(ShopProduct.RecruitMaterialBox, ledger, "RF", Grade.ALLSTAR, templates, n => n == 100 ? 85 : 0, null, out var box, out _));
            Assert.AreEqual((Grade.LIVE_EPIC, 6), (box[0].Template.Grade, box[0].ReinforceLevel), "재료 상자 85 → +6강 LIVE 에픽");
            Assert.AreEqual(3100 - 300 - 500 - 1500 - 800, ledger.GrowthCoin);
            Assert.AreEqual((1, 1), (ledger.AwakenTicket, ledger.TranscendTicket));
            Assert.AreEqual(6, ledger.Cards.Count, "카드 상품 1 + 3 + 1 + 1장");
            Assert.IsFalse(ShopExchangeRules.TryBuy(ShopProduct.TranscendTicket, ledger, "RF", Grade.ALLSTAR, templates, null, null, out _, out var coinPoor));
            StringAssert.Contains("성장 코인 부족", coinPoor);
            Assert.AreEqual(ShopCurrency.Points, ShopExchangeRules.CurrencyOf(ShopProduct.EnhanceSupportPack));
            Assert.AreEqual(ShopCurrency.GrowthCoin, ShopExchangeRules.CurrencyOf(ShopProduct.RecruitMaterialBox));
        }

        [Test]
        public void AwakenTicket_PlusOne_RequiresPlus10_StopsAt9()
        {
            var ledger = Ledger();
            ledger.AwakenTicket = 2;
            var locked = Card(Grade.TITLE_HOLDER, "A", reinforce: 3);
            Assert.IsFalse(ShopExchangeRules.TryUseAwakenTicket(locked, ledger, out var msg));
            StringAssert.Contains("+10강", msg);
            Assert.AreEqual(2, ledger.AwakenTicket);
            var target = Card(Grade.TITLE_HOLDER, "A", reinforce: 10, awaken: 8);
            Assert.IsTrue(ShopExchangeRules.TryUseAwakenTicket(target, ledger, out _));
            Assert.AreEqual((9, 1), (target.AwakenLevel, ledger.AwakenTicket));
            Assert.IsFalse(ShopExchangeRules.TryUseAwakenTicket(target, ledger, out _), "9각 이후는 보조권 불가");
        }

        [Test]
        public void LeagueRewards_ScaleWithTier_TrophyOnlyUpperLeaguesAndChampion()
        {
            var low = LeagueMaterialRewards.ForMatch(LeagueTier.Amateur, true, n => 0);
            var high = LeagueMaterialRewards.ForMatch(LeagueTier.HallOfFame, true, n => 0);
            Assert.AreEqual((600, 6), (low.Points, low.GrowthCoin));
            Assert.AreEqual((2600, 26), (high.Points, high.GrowthCoin));
            Assert.AreEqual((1, 0), (low.TrainingTicket, low.Trophy), "아마추어 승리: 특훈 확률 12% · 트로피 없음");
            Assert.AreEqual((1, 1), (high.TrainingTicket, high.Trophy), "영구결번 승리: 트로피 확률 25%");
            Assert.AreEqual(0, LeagueMaterialRewards.TrophyChancePercent(LeagueTier.Franchise, true));
            Assert.AreEqual(5, LeagueMaterialRewards.TrophyChancePercent(LeagueTier.TitleHolder, true));
            var lose = LeagueMaterialRewards.ForMatch(LeagueTier.Major, false, n => 0);
            Assert.AreEqual((350, 3, 0, 0), (lose.Points, lose.GrowthCoin, lose.TrainingTicket, lose.Trophy));

            var champLow = LeagueMaterialRewards.ForSeason(LeagueTier.Amateur, 1);
            var champHigh = LeagueMaterialRewards.ForSeason(LeagueTier.HallOfFame, 1);
            Assert.AreEqual((1, 2), (champLow.Trophy, champLow.TrainingTicket));
            Assert.AreEqual((4, 4, 750), (champHigh.Trophy, champHigh.TrainingTicket, champHigh.GrowthCoin));
            Assert.AreEqual(0, LeagueMaterialRewards.ForSeason(LeagueTier.Amateur, 7).Trophy);
            StringAssert.Contains("3:1 포지션 재조합", LeagueMaterialRewards.SourceGuide(LeagueTier.Major));
        }

        [Test]
        public void MatchRewardManager_GrantsTierMaterialRewardsToGameManager()
        {
            var go = new GameObject("Task189Managers");
            created.Add(go);
            var gm = go.AddComponent<GameManager>();
            SetInstance(typeof(GameManager), gm);
            var league = go.AddComponent<LeagueManager>();
            SetInstance(typeof(LeagueManager), league);
            league.InitializeLeague(Team.Samsung, LeagueTier.GoldenGlove);
            var reward = go.AddComponent<MatchRewardManager>();
            int gold = gm.GameGold, coin = gm.GrowthCoin;
            var result = reward.GrantRewardForMatch(new MatchResult { HomeTeamName = "KIA", AwayTeamName = "Samsung", WinnerTeamName = "Samsung" });
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Won);
            Assert.AreEqual(400 + 200 * 8, result.MaterialReward.Points, "골든글러브 리그(8단계) 승리 포인트");
            Assert.AreEqual(4 + 2 * 8, result.MaterialReward.GrowthCoin);
            Assert.AreEqual(gold + result.MaterialReward.Points, gm.GameGold);
            Assert.AreEqual(coin + result.MaterialReward.GrowthCoin, gm.GrowthCoin);
        }

        // ================================================================== D. 씬 UI

        [Test]
        public void Scene_ScoutHubShopTab_GrowthCenterTranscendTab_Shortcuts()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
            var hub = UnityEngine.Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(hub);
            var so = new SerializedObject(hub);
            var shopTab = so.FindProperty("shopTabButton").objectReferenceValue as Button;
            Assert.IsNotNull(shopTab, "4번째 탭 [상점·교환소]");
            Assert.AreEqual("상점·교환소", shopTab.GetComponentInChildren<Text>(true).text);
            Assert.IsNotNull(hub.ShopSection);
            var view = hub.ShopSection.GetComponent<ShopExchangeView>();
            Assert.IsNotNull(view);
            var root = hub.ShopSection.transform.Find(ShopExchangeView.RootName);
            Assert.IsNotNull(root);
            foreach (var name in new[] { "Sub_PointShop", "Sub_CoinExchange", "Sub_Release", "Sub_Recombine", "Sub_Guide", "PositionSelect", "GradeSelect", "Execute" })
                Assert.IsNotNull(root.Find(name), name);

            hub.ShowShopSection(ShopSubTab.Recombine);
            Assert.IsTrue(hub.ShopSection.activeSelf);
            Assert.AreEqual(ShopSubTab.Recombine, view.CurrentSubTab);
            hub.ShowPlayerSection();
            Assert.IsFalse(hub.ShopSection.activeSelf);

            var growth = UnityEngine.Object.FindAnyObjectByType<GrowthCenterView>(FindObjectsInactive.Include);
            Assert.IsNotNull(growth);
            var gRoot = growth.transform.Find(GrowthCenterView.RootName);
            Assert.IsNotNull(gRoot);
            foreach (var name in new[] { "Tab_Enhance", "Tab_Awaken", "Tab_Transcend", "Tab_LimitBreak", "Tab_Training", "SourceButton", "ContextButton" })
                Assert.IsNotNull(gRoot.Find(name), name);

            var special = hub.SpecialSection.transform.Find(SpecialRecruitView.RootName);
            Assert.IsNotNull(special.Find("SourceButton"), "특별 영입 [재료 획득처 · 재조합] 바로가기");
        }
    }
}
