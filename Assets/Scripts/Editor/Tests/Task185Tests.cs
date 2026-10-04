using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// [TASK-KBO-185] 각성 재료 규칙(동일 연도 +3각 / 다른 연도 +1각) · 라인업 3대 UI 버그 · 스카우트 골글 이상 제외 + 선택 구단 50% 픽업 ·
    /// 특별 영입(골든글러브 3슬롯 / 시그니처 5슬롯).
    /// </summary>
    public class Task185Tests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private bool sceneOpened;

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single); // 테스트 중 바꾼 씬 상태를 버린다
            sceneOpened = false;
        }

        private PlayerTemplate Template(Grade grade, string person, int year = 2024, Team team = Team.Samsung, bool pitcher = false, int level = 70)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = $"{person}_{grade}_{year}_{Guid.NewGuid():N}";
            t.RealPlayerId = person;
            t.PlayerName = person;
            t.SeasonYear = year;
            t.Team = team;
            t.Grade = grade;
            t.IsPitcher = pitcher;
            if (pitcher) { t.PitcherRole = PitcherRole.StartingPitcher; t.PitcherStats = StatProfiles.SpreadPitcher(level, PitcherRole.StartingPitcher, 7); }
            else { t.BatterPosition = BatterPosition.RightField; t.BatterStats = StatProfiles.SpreadBatter(level, BatterPosition.RightField, 7); }
            return t;
        }

        private Player Card(Grade grade, string person, int year = 2024, Team team = Team.Samsung, int reinforce = 0, int awaken = 0) =>
            new Player(Guid.NewGuid().ToString(), Template(grade, person, year, team)) { ReinforceLevel = reinforce, AwakenLevel = awaken };

        private void OpenScene()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
        }

        // ================================================================== A. 각성 재료 규칙

        [Test]
        public void Awaken_SameSeasonSameYear_GivesPlus3PerMaterial_ClampedAt9()
        {
            var target = Card(Grade.TITLE_HOLDER, "KOO", 2024, reinforce: 10); // [TASK-KBO-189] +10강 선행
            var copy = Card(Grade.TITLE_HOLDER, "KOO", 2024); // 같은 시즌 · 같은 연도지만 다른 템플릿 인스턴스(구 버그: +1각 처리)
            Assert.AreEqual(3, CardGrowthRules.AwakenGainFor(target, copy));
            Assert.IsTrue(UpgradeManager.ApplyAwaken(target, new List<Player> { copy }));
            Assert.AreEqual(3, target.AwakenLevel, "0각 → 3각");
            UpgradeManager.ApplyAwaken(target, new List<Player> { Card(Grade.TITLE_HOLDER, "KOO", 2024) });
            Assert.AreEqual(6, target.AwakenLevel, "3각 → 6각");
            UpgradeManager.ApplyAwaken(target, new List<Player> { Card(Grade.TITLE_HOLDER, "KOO", 2024), Card(Grade.TITLE_HOLDER, "KOO", 2024) });
            Assert.AreEqual(9, target.AwakenLevel, "6각 → 9각(상한 클램프)");
        }

        [Test]
        public void Awaken_SameSeasonOtherYear_GivesPlus3_SamePlayer()
        {
            // [TASK-KBO-188] 같은 시즌 등급 · 같은 선수면 연도 무관 +3각
            var target = Card(Grade.ALLSTAR, "KOO", 2024, reinforce: 10);
            var otherYear = Card(Grade.ALLSTAR, "KOO", 2023);
            Assert.AreEqual(3, CardGrowthRules.AwakenGainFor(target, otherYear));
            UpgradeManager.ApplyAwaken(target, new List<Player> { otherYear });
            Assert.AreEqual(3, target.AwakenLevel);
            Assert.AreEqual("[같은 선수 +3각]", CardGrowthRules.AwakenMaterialBadge(target, otherYear));
        }

        [Test]
        public void Awaken_OtherGradeOrOtherPlayer_IsNotMaterial_ExceptSameGradeOtherPlayer()
        {
            var target = Card(Grade.ALLSTAR, "KOO", 2024, reinforce: 10);
            Assert.AreEqual(0, CardGrowthRules.AwakenGainFor(target, Card(Grade.FRANCHISE, "KOO", 2024)), "다른 시즌 등급");
            Assert.AreEqual(1, CardGrowthRules.AwakenGainFor(target, Card(Grade.ALLSTAR, "LEE", 2024)), "[TASK-KBO-189] 같은 시즌 · 같은 포지션(RF) 다른 선수 = +1각");
            Assert.AreEqual(0, CardGrowthRules.AwakenGainFor(target, target), "자기 자신");
            Assert.IsFalse(UpgradeManager.ApplyAwaken(target, new List<Player> { Card(Grade.LIVE_EPIC, "KOO", 2024) }));
            Assert.AreEqual(0, target.AwakenLevel);
        }

        [Test]
        public void GrowthCenter_AwakenCandidates_SameYearFirst_LineupExcluded_PreviewShowsJump()
        {
            // [TASK-KBO-188] 같은 선수(연도 무관) +3각이 먼저, 같은 시즌 다른 선수 +1각이 뒤
            var target = Card(Grade.GOLDEN_GLOVE, "KOO", 2024, reinforce: 10);
            var sameYear = Card(Grade.GOLDEN_GLOVE, "KOO", 2024);
            var otherYear = Card(Grade.GOLDEN_GLOVE, "LEE", 2022);
            var lineupCopy = Card(Grade.GOLDEN_GLOVE, "KOO", 2024);
            var inventory = new List<Player> { target, otherYear, sameYear, lineupCopy, Card(Grade.ALLSTAR, "KOO", 2024) };
            var candidates = GrowthCenterRules.MaterialCandidates(GrowthTab.Awaken, target, inventory, new[] { lineupCopy });
            CollectionAssert.AreEqual(new[] { sameYear, otherYear }, candidates, "같은 선수(+3각) 먼저, 라인업·다른 등급 제외");

            int jump = CardGrowthRules.AwakenGrowthFor(Grade.GOLDEN_GLOVE, 3) - CardGrowthRules.AwakenGrowthFor(Grade.GOLDEN_GLOVE, 0);
            Assert.AreEqual(5, jump, "상위 시즌 3각 임계점 = OVR +5");
            Assert.AreEqual($"0각 → 3각 · OVR +{jump} 점프", GrowthCenterRules.AwakenPreview(target, new[] { sameYear }));
            Assert.AreEqual("0각 → 1각 · OVR +1 점프", GrowthCenterRules.AwakenPreview(target, new[] { otherYear }));
            Assert.AreEqual(0, target.AwakenLevel, "미리보기는 원본을 바꾸지 않는다");
        }

        // ================================================================== B. 라인업 UI 버그

        [Test]
        public void Lineup_NoLegacyOrphansAboveTray_SetDeckButtonLivesInDefaultBar()
        {
            OpenScene();
            var roster = UnityEngine.Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(roster);
            var activeOrphans = roster.transform.Cast<Transform>()
                .Where(t => RosterUIController.LegacyBarObjectNames.Contains(t.name) && t.gameObject.activeSelf).Select(t => t.name).ToList();
            CollectionAssert.IsEmpty(activeOrphans, "RosterPanel 직속에 흰 게이지 막대/[세트덱 버프 선택]/[자동 교체] 잔재가 없어야 한다");

            var so = new SerializedObject(roster);
            var defaultBar = (GameObject)so.FindProperty("defaultBarRoot").objectReferenceValue;
            var tray = (GameObject)so.FindProperty("trayRoot").objectReferenceValue;
            var option = (Button)so.FindProperty("setDeckOptionButton").objectReferenceValue;
            Assert.IsNotNull(defaultBar); Assert.IsNotNull(tray); Assert.IsNotNull(option);
            Assert.IsTrue(option.transform.IsChildOf(defaultBar.transform), "[세트덱 버프 선택]은 하단 기본 바 안에만 있다(트레이가 열리면 함께 숨는다)");
            Assert.IsFalse(option.transform.IsChildOf(tray.transform));
            Assert.AreEqual(1, roster.transform.Cast<Transform>().Concat(roster.GetComponentsInChildren<Transform>(true))
                .Distinct().Count(t => t.name == "SetDeckOptionButton"), "[세트덱 버프 선택] 버튼은 하나뿐");
        }

        [Test]
        public void Lineup_TrayAndDefaultBar_AreMutuallyExclusive()
        {
            OpenScene();
            var roster = UnityEngine.Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            var so = new SerializedObject(roster);
            var defaultBar = (GameObject)so.FindProperty("defaultBarRoot").objectReferenceValue;
            var tray = (GameObject)so.FindProperty("trayRoot").objectReferenceValue;
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var player = Card(Grade.GOLDEN_GLOVE, "KOO", 2024);

            typeof(RosterUIController).GetMethod("ShowTray", flags).Invoke(roster, new object[] { player, false });
            Assert.IsTrue(tray.activeSelf, "선수 선택 → 액션 트레이만");
            Assert.IsFalse(defaultBar.activeSelf, "선수 선택 → 세트덱 스코어 바(+[세트덱 버프 선택]) 숨김");

            typeof(RosterUIController).GetMethod("Deselect", flags).Invoke(roster, null);
            Assert.IsFalse(tray.activeSelf);
            Assert.IsTrue(defaultBar.activeSelf, "선수 미선택 → 세트덱 스코어 바");
        }

        [Test]
        public void SetDeckBuffPopup_OpensOnTop_AndABButtonsRespond()
        {
            OpenScene();
            var roster = UnityEngine.Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            var options = roster.GetComponentInChildren<SetDeckOptionUIController>(true);
            Assert.IsNotNull(options);
            var panel = (GameObject)new SerializedObject(options).FindProperty("panelRoot").objectReferenceValue;
            Assert.IsNotNull(panel);

            Assert.IsTrue(roster.OpenSetDeckBuffSelection(), "[세트덱 버프 선택] → 팝업 열림");
            Assert.IsTrue(panel.activeSelf);
            Assert.AreEqual(panel.transform.parent.childCount - 1, panel.transform.GetSiblingIndex(), "팝업이 형제 중 최상단(SetAsLastSibling)");
            Assert.Greater(options.RowCount, 0, "선택형 구간 A/B 행이 생성된다");

            int threshold = SetDeckBuffTable.SelectableBrackets.First().Threshold;
            foreach (bool b in new[] { false, true })
            {
                var button = options.OptionButton(threshold, b);
                Assert.IsNotNull(button);
                Assert.IsTrue(button.interactable);
                Assert.IsTrue(button.targetGraphic != null && button.targetGraphic.raycastTarget, "A/B 버튼 그래픽이 클릭을 받는다");
                Assert.IsTrue(button.GetComponentsInChildren<Text>(true).All(t => !t.raycastTarget), "라벨이 클릭을 가로채지 않는다");
                button.onClick.Invoke();
                StringAssert.Contains(b ? "B안" : "A안", options.LastFeedback, "onClick 리스너 동작");
            }
        }

        [Test]
        public void LobbySetDeckTile_OpensBuffPopup()
        {
            OpenScene();
            var relays = UnityEngine.Object.FindObjectsByType<LobbyButtonRelay>(FindObjectsInactive.Include).Where(r =>
            {
                var title = r.transform.parent != null ? r.transform.parent.Find(r.name + "_Title") : null;
                return title != null && title.TryGetComponent<Text>(out var text) && text.text.Contains("세트덱");
            }).ToList();
            Assert.IsNotEmpty(relays, "로비 [세트덱 & 버프 선택] 타일");
            Assert.IsTrue(relays.All(r => r.OpensSetDeckBuffs), "타일이 버프 선택 팝업까지 연다");
        }

        [Test]
        public void StorageCards_AreFreshInstances_FullyRendered_AndPoolReleaseResetsState()
        {
            OpenScene();
            var roster = UnityEngine.Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            var holder = new GameObject("StorageHolderTest", typeof(RectTransform));
            created.Add(holder);
            var tracking = new List<PlayerCardUI>();
            var spawn = typeof(RosterUIController).GetMethod("SpawnFreshCard", BindingFlags.Instance | BindingFlags.NonPublic);
            var players = new[] { Card(Grade.GOLDEN_GLOVE, "KOO", 2024), Card(Grade.LIVE_NORMAL, "LEE", 2026), Card(Grade.ALLSTAR, "PARK", 2025) };
            foreach (var p in players) spawn.Invoke(roster, new object[] { p, holder.transform, tracking });

            Assert.AreEqual(players.Length, tracking.Count);
            Assert.AreEqual(players.Length, tracking.Distinct().Count(), "카드마다 새 인스턴스");
            for (int i = 0; i < tracking.Count; i++)
            {
                var card = tracking[i];
                Assert.AreSame(holder.transform, card.transform.parent);
                Assert.IsTrue(card.gameObject.activeSelf);
                Assert.AreEqual(Vector3.one, card.transform.localScale);
                Assert.AreSame(players[i], card.BoundPlayer, $"{i + 1}번째 카드도 선수 데이터가 바인딩된다");
                var root = card.GetComponent<Image>();
                Assert.IsTrue(root != null && root.enabled && root.color.a > 0.5f, $"{i + 1}번째 카드 배경/프레임");
                Assert.IsTrue(card.GetComponentsInChildren<Text>(true).Any(t => t.name == "OvrText" && !string.IsNullOrEmpty(t.text)), $"{i + 1}번째 카드 OVR");
                Assert.IsTrue(card.GetComponentsInChildren<Graphic>(true).All(g => !g.canvasRenderer.cull), "컬링 잔재 없음");
            }

            var reused = tracking[1];
            reused.transform.localScale = new Vector3(2.5f, 2.5f, 1f);
            if (!reused.TryGetComponent<LayoutElement>(out var layout)) layout = reused.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;
            foreach (var g in reused.GetComponentsInChildren<Graphic>(true)) g.canvasRenderer.cull = true;
            CardPoolManager.ResetRenderState(reused);
            Assert.AreEqual(Vector3.one, reused.transform.localScale);
            Assert.IsFalse(layout.ignoreLayout);
            Assert.IsTrue(reused.GetComponentsInChildren<Graphic>(true).All(g => !g.canvasRenderer.cull), "풀 반납 시 렌더 상태 초기화");
        }

        // ================================================================== C. 스카우트

        [Test]
        public void ScoutTables_ExactRates_TopIsTitleHolder_GoldenGloveAndAboveExcluded()
        {
            CollectionAssert.AreEqual(new[] { Grade.TITLE_HOLDER, Grade.FRANCHISE, Grade.ALLSTAR, Grade.LIVE_EPIC }, ScoutDropTables.Premium.Select(e => e.Grade).ToArray());
            CollectionAssert.AreEqual(new[] { 1.0f, 2.5f, 4.5f, 92.0f }, ScoutDropTables.Premium.Select(e => e.RatePercent).ToArray());
            CollectionAssert.AreEqual(new[] { Grade.TITLE_HOLDER, Grade.FRANCHISE, Grade.ALLSTAR, Grade.LIVE_EPIC, Grade.LIVE_NORMAL }, ScoutDropTables.Normal.Select(e => e.Grade).ToArray());
            var normal = ScoutDropTables.Normal.Select(e => e.RatePercent).ToArray();
            for (int i = 0; i < 5; i++) Assert.AreEqual(new[] { 0.3f, 1.0f, 2.2f, 6.5f, 90.0f }[i], normal[i], 0.0001f);
            Assert.AreEqual(100f, ScoutDropTables.Total(ScoutDropTables.Premium), 0.0001f);
            Assert.AreEqual(100f, ScoutDropTables.Total(ScoutDropTables.Normal), 0.0001f);
            foreach (var g in new[] { Grade.GOLDEN_GLOVE, Grade.SIGNATURE, Grade.DYNASTY, Grade.RETIRED_NUMBER })
                Assert.IsTrue(ScoutDropTables.IsExcludedFromScout(g), $"{g} 뽑기 제외");
            Assert.AreEqual(Grade.TITLE_HOLDER, ScoutDropTables.HighestScoutGrade);

            var rng = new System.Random(185);
            const int draws = 400000;
            foreach (var table in new[] { ScoutDropTables.Premium, ScoutDropTables.Normal })
            {
                var counts = new Dictionary<Grade, int>();
                for (int i = 0; i < draws; i++)
                {
                    var g = ScoutDropTables.RollGrade(table, (float)(rng.NextDouble() * 100.0));
                    counts[g] = counts.TryGetValue(g, out int n) ? n + 1 : 1;
                }
                Assert.IsFalse(counts.Keys.Any(ScoutDropTables.IsExcludedFromScout), "골글 이상이 뽑히지 않는다");
                foreach (var (grade, rate) in table)
                    Assert.AreEqual(rate, 100.0 * (counts.TryGetValue(grade, out int c) ? c : 0) / draws, Math.Max(0.15, rate * 0.05), $"{grade} 실측 확률");
            }
        }

        [Test]
        public void PickupScout_FavoriteTeamShare_IsFiftyPercent_AndNeverTerminalGrades()
        {
            var teams = Enum.GetValues(typeof(Team)).Cast<Team>().Where(t => t != Team.None).ToList();
            Assert.AreEqual(10, teams.Count);
            var templates = new List<PlayerTemplate>();
            foreach (var team in teams)
            {
                for (int i = 0; i < 6; i++) templates.Add(Template(Grade.TITLE_HOLDER, $"{team}_TH{i}", 2024, team));
                templates.Add(Template(Grade.GOLDEN_GLOVE, $"{team}_GG", 2024, team));
                templates.Add(Template(Grade.SIGNATURE, $"{team}_SIG", 2024, team));
            }
            var rng = new System.Random(7);
            Func<float> r = () => (float)rng.NextDouble();
            const int draws = 100000;
            int pickupOwn = 0, normalOwn = 0;
            for (int i = 0; i < draws; i++)
            {
                var a = ScoutDropTables.PickTemplate(templates, Grade.TITLE_HOLDER, Team.Samsung, true, r);
                var b = ScoutDropTables.PickTemplate(templates, Grade.TITLE_HOLDER, Team.Samsung, false, r);
                Assert.AreEqual(Grade.TITLE_HOLDER, a.Grade);
                if (a.Team == Team.Samsung) pickupOwn++;
                if (b.Team == Team.Samsung) normalOwn++;
            }
            Assert.AreEqual(0.5, (double)pickupOwn / draws, 0.01, "픽업: 선택 구단 50%");
            Assert.AreEqual(0.1, (double)normalOwn / draws, 0.01, "일반: 균등 1/10");
            for (int i = 0; i < 2000; i++)
                Assert.IsFalse(ScoutDropTables.IsExcludedFromScout(ScoutDropTables.PickTemplate(templates, Grade.GOLDEN_GLOVE, Team.Samsung, true, r).Grade),
                    "GG를 요청해도 뽑기에서는 GG 이상이 나오지 않는다");
            StringAssert.Contains("[선택 구단(삼성) 픽업 확률 UP! (50%)]", ScoutUIController.PickupBannerText(Team.Samsung));
        }

        // ================================================================== D. 특별 영입

        private MemoryRecruitLedger Ledger(int points = 0, int trophies = 0) =>
            new MemoryRecruitLedger { FavoriteTeam = Team.Samsung, Points = points, Trophies = trophies };

        private List<PlayerTemplate> TargetTemplates() => new List<PlayerTemplate>
        {
            Template(Grade.GOLDEN_GLOVE, "SAM_GG1", 2024, Team.Samsung), Template(Grade.GOLDEN_GLOVE, "SAM_GG2", 2023, Team.Samsung),
            Template(Grade.GOLDEN_GLOVE, "KIA_GG", 2024, Team.KIA),
            Template(Grade.SIGNATURE, "SAM_SIG", 2024, Team.Samsung), Template(Grade.SIGNATURE, "LG_SIG", 2024, Team.LG),
        };

        private static Player Issue(PlayerTemplate t) => new Player(Guid.NewGuid().ToString(), t);

        [Test]
        public void GoldenGloveRecruit_ThreeSlots_AutoRegisterSkipsLineup_ConsumesAndGrantsFavoriteGG()
        {
            var recipe = SpecialRecruitRules.GoldenGloveRecipe;
            Assert.AreEqual(3, recipe.Slots.Count);
            CollectionAssert.AreEqual(new[] { 1, 1, 2 }, recipe.Slots.Select(s => s.Count).ToArray());

            var ledger = Ledger(points: 50000);
            var special = Card(Grade.FRANCHISE, "A", 2024, Team.KIA);
            var reinforced = Card(Grade.LIVE_NORMAL, "B", 2026, Team.LG, reinforce: 3);
            var own1 = Card(Grade.LIVE_EPIC, "C", 2026, Team.Samsung);
            var own2 = Card(Grade.ALLSTAR, "D", 2025, Team.Samsung);
            var lineup = Card(Grade.LIVE_EPIC, "E", 2026, Team.Samsung);
            var weak = Card(Grade.LIVE_NORMAL, "F", 2026, Team.Samsung); // 라이브 일반 = 구단 카드 조건 미달
            ledger.Cards.AddRange(new[] { special, reinforced, own1, own2, lineup, weak });
            ledger.Lineup.Add(lineup);

            var assign = SpecialRecruitRules.AutoAssign(recipe, ledger.Inventory, ledger.Roster, ledger.FavoriteTeam);
            CollectionAssert.AreEqual(new[] { special }, assign[0]);
            CollectionAssert.AreEqual(new[] { reinforced }, assign[1]);
            CollectionAssert.AreEquivalent(new[] { own1, own2 }, assign[2]);
            Assert.IsFalse(assign.SelectMany(a => a).Contains(lineup), "주전 라인업 카드는 자동 등록에서 제외");

            var snapshot = assign.Select(a => (IReadOnlyList<Player>)a).ToList();
            Assert.IsTrue(SpecialRecruitRules.TryRecruit(recipe, snapshot, ledger, TargetTemplates(), n => 0, Issue, out var gg, out var message), message);
            Assert.AreEqual(Grade.GOLDEN_GLOVE, gg.Template.Grade);
            Assert.AreEqual(Team.Samsung, gg.Template.Team, "선택 구단 골든글러브 확정");
            Assert.AreEqual(0, ledger.Points, "50,000 포인트 소모");
            CollectionAssert.AreEquivalent(new[] { lineup, weak, gg }, ledger.Cards, "재료 4장 소모 · GG 1장 획득");
        }

        [Test]
        public void GoldenGloveRecruit_Fails_WithoutPoints_OrWithLineupMaterial_OrMissingSlot()
        {
            var recipe = SpecialRecruitRules.GoldenGloveRecipe;
            var ledger = Ledger(points: 49999);
            var special = Card(Grade.TITLE_HOLDER, "A", 2024, Team.KIA);
            var reinforced = Card(Grade.ALLSTAR, "B", 2025, Team.LG, reinforce: 5);
            var own1 = Card(Grade.LIVE_EPIC, "C", 2026, Team.Samsung);
            var own2 = Card(Grade.LIVE_EPIC, "D", 2026, Team.Samsung);
            ledger.Cards.AddRange(new[] { special, reinforced, own1, own2 });
            var full = new List<IReadOnlyList<Player>> { new[] { special }, new[] { reinforced }, new[] { own1, own2 } };
            StringAssert.Contains("재화 부족", SpecialRecruitRules.Validate(recipe, full, ledger));

            ledger.Points = 50000;
            Assert.IsNull(SpecialRecruitRules.Validate(recipe, full, ledger));
            ledger.Lineup.Add(own2);
            StringAssert.Contains("주전 라인업", SpecialRecruitRules.Validate(recipe, full, ledger));
            ledger.Lineup.Clear();
            var missing = new List<IReadOnlyList<Player>> { new[] { special }, new[] { reinforced }, new[] { own1 } };
            StringAssert.Contains("재료 부족", SpecialRecruitRules.Validate(recipe, missing, ledger));
            var dup = new List<IReadOnlyList<Player>> { new[] { special }, new[] { special }, new[] { own1, own2 } };
            Assert.IsNotNull(SpecialRecruitRules.Validate(recipe, dup, ledger), "+3강 미만/중복 등록 거부");
            Assert.IsFalse(SpecialRecruitRules.TryRecruit(recipe, missing, ledger, TargetTemplates(), n => 0, Issue, out _, out _));
            Assert.AreEqual(4, ledger.Cards.Count, "실패 시 아무것도 소모하지 않는다");
            Assert.AreEqual(50000, ledger.Points);
        }

        [Test]
        public void SignatureRecruit_FiveSlots_PaysTrophiesOrPoints_GrantsFavoriteSignature()
        {
            var recipe = SpecialRecruitRules.SignatureRecipe;
            Assert.AreEqual(5, recipe.Slots.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 1, 1, 3 }, recipe.Slots.Select(s => s.Count).ToArray());

            Player[] Materials(MemoryRecruitLedger l)
            {
                var cards = new[]
                {
                    Card(Grade.GOLDEN_GLOVE, "G", 2024, Team.KIA),
                    Card(Grade.TITLE_HOLDER, "T", 2024, Team.LG), Card(Grade.FRANCHISE, "F", 2025, Team.NC),
                    Card(Grade.LIVE_EPIC, "R", 2026, Team.KT, reinforce: 6),
                    Card(Grade.ALLSTAR, "W", 2025, Team.SSG, awaken: 3),
                    Card(Grade.LIVE_NORMAL, "S1", 2026, Team.Samsung), Card(Grade.LIVE_NORMAL, "S2", 2026, Team.Samsung), Card(Grade.LIVE_EPIC, "S3", 2026, Team.Samsung),
                };
                l.Cards.AddRange(cards);
                return cards;
            }

            var byTrophy = Ledger(points: 999999, trophies: 5);
            var cardsA = Materials(byTrophy);
            var assign = SpecialRecruitRules.AutoAssign(recipe, byTrophy.Inventory, byTrophy.Roster, Team.Samsung);
            Assert.IsTrue(assign.Select((a, i) => a.Count == recipe.Slots[i].Count).All(x => x), "5슬롯 자동 등록 완료");
            Assert.IsTrue(SpecialRecruitRules.TryRecruit(recipe, assign.Select(a => (IReadOnlyList<Player>)a).ToList(), byTrophy, TargetTemplates(), n => 0, Issue, out var sig, out var msg), msg);
            Assert.AreEqual(Grade.SIGNATURE, sig.Template.Grade);
            Assert.AreEqual(Team.Samsung, sig.Template.Team);
            Assert.AreEqual(0, byTrophy.Trophies, "트로피 5개 우선 결제");
            Assert.AreEqual(999999, byTrophy.Points);
            Assert.IsTrue(cardsA.All(c => !byTrophy.Cards.Contains(c)), "재료 8장 소모");

            var byPoints = Ledger(points: 200000, trophies: 4);
            Materials(byPoints);
            var assignB = SpecialRecruitRules.AutoAssign(recipe, byPoints.Inventory, byPoints.Roster, Team.Samsung);
            Assert.IsTrue(SpecialRecruitRules.TryRecruit(recipe, assignB.Select(a => (IReadOnlyList<Player>)a).ToList(), byPoints, TargetTemplates(), n => 0, Issue, out _, out msg), msg);
            Assert.AreEqual(4, byPoints.Trophies);
            Assert.AreEqual(0, byPoints.Points, "트로피 부족 → 200,000 포인트");

            var broke = Ledger(points: 199999, trophies: 4);
            Materials(broke);
            var assignC = SpecialRecruitRules.AutoAssign(recipe, broke.Inventory, broke.Roster, Team.Samsung);
            StringAssert.Contains("재화 부족", SpecialRecruitRules.Validate(recipe, assignC.Select(a => (IReadOnlyList<Player>)a).ToList(), broke));
        }

        [Test]
        public void SpecialRecruit_TargetPreview_IsFavoriteTeamOnly()
        {
            var templates = TargetTemplates();
            var gg = SpecialRecruitRules.TargetPool(templates, SpecialRecruitRules.GoldenGloveRecipe, Team.Samsung);
            Assert.AreEqual(2, gg.Count);
            Assert.IsTrue(gg.All(t => t.Team == Team.Samsung && t.Grade == Grade.GOLDEN_GLOVE));
            var sig = SpecialRecruitRules.TargetPool(templates, SpecialRecruitRules.SignatureRecipe, Team.Samsung);
            Assert.AreEqual(1, sig.Count);
            Assert.AreEqual("SAM_SIG", sig[0].PlayerName);
        }

        [Test]
        public void ScoutHub_HasThreeTabs_AndSpecialRecruitSection()
        {
            OpenScene();
            var hub = UnityEngine.Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(hub);
            var so = new SerializedObject(hub);
            var tabs = new[] { "playerTabButton", "specialTabButton", "cheerleaderTabButton" }.Select(n => (Button)so.FindProperty(n).objectReferenceValue).ToList();
            Assert.IsTrue(tabs.All(t => t != null));
            CollectionAssert.AreEqual(new[] { "선수 스카우트(뽑기)", "특별 영입(골글·시그니처)", "응원단 영입" }, tabs.Select(t => t.GetComponentInChildren<Text>(true).text).ToArray());
            Assert.Less(tabs[0].transform.GetSiblingIndex(), tabs[1].transform.GetSiblingIndex());
            Assert.Less(tabs[1].transform.GetSiblingIndex(), tabs[2].transform.GetSiblingIndex());
            Assert.IsNotNull(hub.SpecialSection);
            var view = hub.SpecialSection.GetComponent<SpecialRecruitView>();
            Assert.IsNotNull(view);
            var root = hub.SpecialSection.transform.Find(SpecialRecruitView.RootName);
            Assert.IsNotNull(root);
            foreach (var name in new[] { "Kind_GoldenGlove", "Kind_Signature", "AutoRegister", "ClearRegister", "Execute", "PreviewScroll" })
                Assert.IsNotNull(root.Find(name), name);

            hub.ShowSpecialSection();
            Assert.IsTrue(hub.SpecialSection.activeSelf);
            hub.ShowPlayerSection();
            Assert.IsFalse(hub.SpecialSection.activeSelf);
        }
    }
}
