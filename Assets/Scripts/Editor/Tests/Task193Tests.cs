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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-KBO-193] 특별 영입 · 스카우트 · 상점 텍스트 폭주 / 영문 포지션 노출 수정, 스카우트 탭 명도 대비, 중계 헤더 중복 성적 제거 · 미니 바 숨김,
    /// 스토브리그(FA · 트레이드 · 신인 드래프트 · 외국인 영입), 리그 기록실(타자 · 투수 TOP 10 · 명예의 전당).
    /// </summary>
    public class Task193Tests
    {
        private readonly List<Object> created = new List<Object>();
        private bool sceneOpened;

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created) if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = false;
        }

        private PlayerTemplate Template(Grade grade, string person, int year = 2024, Team team = Team.Samsung, string position = "RF", int level = 70)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = $"{person}_{grade}_{year}_{Guid.NewGuid():N}";
            t.RealPlayerId = person;
            t.PlayerName = person;
            t.SeasonYear = year;
            t.Team = team;
            t.Grade = grade;
            t.IsPitcher = position == "SP" || position == "RP" || position == "CP";
            if (t.IsPitcher)
            {
                t.PitcherRole = position == "SP" ? PitcherRole.StartingPitcher : position == "CP" ? PitcherRole.Closer : PitcherRole.WinningReliever;
                t.PitcherStats = StatProfiles.SpreadPitcher(level, t.PitcherRole, 7);
            }
            else
            {
                t.BatterPosition = position switch
                {
                    "C" => BatterPosition.Catcher, "1B" => BatterPosition.FirstBase, "2B" => BatterPosition.SecondBase, "3B" => BatterPosition.ThirdBase,
                    "SS" => BatterPosition.ShortStop, "LF" => BatterPosition.LeftField, "CF" => BatterPosition.CenterField, "DH" => BatterPosition.DesignatedHitter,
                    _ => BatterPosition.RightField
                };
                t.BatterStats = StatProfiles.SpreadBatter(level, t.BatterPosition, 7);
            }
            return t;
        }

        private Player Card(PlayerTemplate t, int reinforce = 0) => new Player(Guid.NewGuid().ToString(), t) { ReinforceLevel = reinforce };
        private static Player Issue(PlayerTemplate t) => new Player(Guid.NewGuid().ToString(), t);

        private void OpenScene()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
        }

        private static double Luminance(Color c)
        {
            double L(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            return 0.2126 * L(c.r) + 0.7152 * L(c.g) + 0.0722 * L(c.b);
        }

        private static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        // ================================================================== A. 특별 영입 · 스카우트 · 상점

        [Test]
        public void SpecialRecruit_Preview_UsesKboPositionShort_NoEnumNames()
        {
            var expected = new Dictionary<string, string>
            {
                { "SP", "SP" }, { "RP", "RP" }, { "CP", "CP" }, { "C", "C" }, { "1B", "1B" }, { "2B", "2B" }, { "3B", "3B" },
                { "SS", "SS" }, { "LF", "LF" }, { "CF", "CF" }, { "RF", "RF" }, { "DH", "DH" },
            };
            var enumNames = Enum.GetNames(typeof(BatterPosition)).Concat(Enum.GetNames(typeof(PitcherRole))).ToList();
            foreach (var kv in expected)
            {
                var t = Template(Grade.GOLDEN_GLOVE, "박치국", 2018, position: kv.Key);
                string line1 = SpecialRecruitView.PreviewLine1(t);
                Assert.AreEqual($"박치국'18 ({kv.Value})", line1);
                foreach (var e in enumNames) StringAssert.DoesNotContain(e, line1, $"영문 enum 노출 금지 - {kv.Key}");
                string line2 = SpecialRecruitView.PreviewLine2(t);
                StringAssert.StartsWith(CardGrowthRules.DisplayName(Grade.GOLDEN_GLOVE) + " · OVR ", line2);
                Assert.AreEqual(t.GetBaseOverall().ToString(), line2.Split(' ').Last());
            }
        }

        [Test]
        public void SpecialRecruit_SlotLines_ConditionLeft_StatusRightTwoLines()
        {
            var recipe = SpecialRecruitRules.Recipe(SpecialRecruitKind.GoldenGlove);
            Assert.AreEqual($"[슬롯 1] {recipe.Slots[0].Condition}", SpecialRecruitView.SlotConditionLine(0, recipe.Slots[0]));
            var card = Card(Template(Grade.ALLSTAR, "박치국", 2018));
            string status = SpecialRecruitView.SlotStatusLine(174, new[] { card }, 1);
            StringAssert.StartsWith("보유 174장 · 등록 ", status);
            StringAssert.Contains("1/1", status);
            StringAssert.Contains($"박치국'18 ({CardGrowthRules.DisplayName(Grade.ALLSTAR)} · 0강)", status);
            Assert.AreEqual(1, status.Count(c => c == '\n'), "최대 2줄");
            StringAssert.Contains("미등록", SpecialRecruitView.SlotStatusLine(3, new Player[0], 2));
        }

        [Test]
        public void TextFit_ClampsMaxToTarget_TruncatesVertically()
        {
            var go = new GameObject("Fit", typeof(RectTransform));
            created.Add(go);
            var t = go.AddComponent<Text>();
            t.fontSize = 40;
            t.resizeTextForBestFit = true;
            t.resizeTextMaxSize = 40;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            Assert.AreEqual(1, TextFit193.ClampAll(go.transform, 20));
            Assert.AreEqual(20, t.fontSize);
            Assert.AreEqual(20, t.resizeTextMaxSize, "resizeTextMaxSize = 목표 크기");
            Assert.AreEqual(VerticalWrapMode.Truncate, t.verticalOverflow);
            Assert.AreEqual(FontStyle.Normal, t.fontStyle);
            Assert.AreEqual(0, TextFit193.ClampAll(go.transform, 20), "멱등");
        }

        [Test]
        public void Scene_SpecialRecruit_SlotsDarkCards_TextsCapped_Truncate_Masked()
        {
            OpenScene();
            var hub = Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(hub);
            var root = hub.SpecialSection.transform.Find(SpecialRecruitView.RootName);
            Assert.IsNotNull(root);
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                Assert.LessOrEqual(TextTidy.EffectiveSize(t), 26, $"{t.name} 폭주 금지");
                Assert.AreEqual(VerticalWrapMode.Truncate, t.verticalOverflow, $"{t.transform.parent.name}/{t.name} 세로 Truncate");
                Assert.AreNotEqual(FontStyle.Bold, t.fontStyle);
            }
            for (int i = 0; i < 5; i++)
            {
                var slot = root.Find($"Slot{i}");
                Assert.IsNotNull(slot);
                Assert.IsNotNull(slot.GetComponent<RectMask2D>(), "슬롯 RectMask2D");
                var color = slot.GetComponent<Image>().color;
                Assert.AreEqual(new Color32(0x16, 0x20, 0x32, 0xFF), (Color32)color, "슬롯 다크 카드 #162032");
                Assert.AreEqual(SpecialRecruitView.SlotTitlePt, slot.Find("Title").GetComponent<Text>().resizeTextMaxSize);
                Assert.AreEqual(SpecialRecruitView.SlotStatusPt, slot.Find("Status").GetComponent<Text>().resizeTextMaxSize);
            }
            Assert.AreEqual(17, SpecialRecruitView.SlotTitlePt);
            Assert.AreEqual(16, SpecialRecruitView.SlotStatusPt);
            var grid = root.Find("PreviewScroll/Content").GetComponent<GridLayoutGroup>();
            Assert.GreaterOrEqual(grid.cellSize.y, 70f, "미리보기 2줄 타일");
        }

        [Test]
        public void Scene_ScoutHubTabs_DarkNavyInactive_BlueActive_ReadableContrast()
        {
            OpenScene();
            var hub = Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            hub.ApplyTabStyle(1);
            var so = new UnityEditor.SerializedObject(hub);
            var tabs = new[] { "playerTabButton", "specialTabButton", "cheerleaderTabButton", "shopTabButton" }
                .Select(n => so.FindProperty(n).objectReferenceValue as Button).ToList();
            Assert.IsTrue(tabs.All(t => t != null), "4개 탭");
            for (int i = 0; i < tabs.Count; i++)
            {
                bool active = i == 1;
                var bg = tabs[i].targetGraphic.color;
                var label = tabs[i].GetComponentInChildren<Text>(true);
                Assert.AreEqual(active ? ScoutHubUIController.ActiveTabColor : ScoutHubUIController.InactiveTabColor, bg);
                Assert.AreEqual(active ? Color.white : ScoutHubUIController.InactiveTabTextColor, label.color);
                Assert.AreEqual(active ? 20 : 18, label.resizeTextMaxSize);
                Assert.AreEqual(FontStyle.Normal, label.fontStyle);
                Assert.Greater(Contrast(bg, label.color), 4.5, $"탭 {i} 명도 대비");
            }
            Assert.AreEqual(new Color32(0x1E, 0x29, 0x3B, 0xFF), (Color32)ScoutHubUIController.InactiveTabColor);
            Assert.AreEqual(new Color32(0x25, 0x63, 0xEB, 0xFF), (Color32)ScoutHubUIController.ActiveTabColor);
            Assert.AreEqual(new Color32(0xCB, 0xD5, 0xE1, 0xFF), (Color32)ScoutHubUIController.InactiveTabTextColor);
        }

        [Test]
        public void Scene_ShopExchange_TextsCapped_NoWhiteOnLight()
        {
            OpenScene();
            var hub = Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            var root = hub.ShopSection.transform.Find(ShopExchangeView.RootName);
            Assert.IsNotNull(root);
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                Assert.LessOrEqual(TextTidy.EffectiveSize(t), 26, $"{t.name}");
                Assert.AreEqual(VerticalWrapMode.Truncate, t.verticalOverflow, $"{t.transform.parent.name}/{t.name}");
                // 흰 글씨 아래 밝은 배경 금지: 가장 가까운 배경 Image가 밝으면(휘도 > 0.6) 글씨는 어두워야 한다.
                var bg = t.transform.parent != null ? t.transform.parent.GetComponent<Image>() : null;
                if (bg != null && bg.color.a > 0.5f && Luminance(bg.color) > 0.6) Assert.Less(Luminance(t.color), 0.4, $"{t.transform.parent.name} 흰 바탕 흰 글씨");
            }
            for (int i = 0; i < 7; i++) Assert.LessOrEqual(TextTidy.EffectiveSize(root.Find($"Products/ProductTitle{i}").GetComponent<Text>()), TextFit193.SlotMax);
        }

        // ================================================================== B. 중계 화면

        [Test]
        public void Scene_Broadcast_HeaderLabelsOnly16pt_SeasonRowOneLine_MiniBarHidden()
        {
            OpenScene();
            var view = Object.FindAnyObjectByType<CompyaMatchView>(FindObjectsInactive.Include);
            Assert.IsNotNull(view);
            Assert.AreEqual("AT BAT", CompyaMatchView.AtBatHeader);
            Assert.AreEqual("ON THE MOUND", CompyaMatchView.MoundHeader);
            var texts = view.GetComponentsInChildren<Text>(true);
            foreach (var side in new[] { "LeftInfo", "RightInfo" })
            {
                var header = texts.First(t => t.name == side + "HeaderText");
                Assert.AreEqual(16, header.resizeTextMaxSize, "헤더 16pt");
                StringAssert.DoesNotContain("ERA", header.text);
                StringAssert.DoesNotContain("타율", header.text);
                var season = texts.First(t => t.name == side + "RowValue2");
                Assert.AreEqual(16, season.fontSize);
                Assert.AreEqual(HorizontalWrapMode.Overflow, season.horizontalOverflow, "시즌 행 한 줄");
                var rect = (RectTransform)season.transform;
                Assert.Greater(rect.anchorMax.x - rect.anchorMin.x, (596f - 360f) / CompyaUiKit.RefWidth, "값 칸 폭 확대");
            }
            var group = view.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == CompyaMatchView.RelayProgressGroupName);
            Assert.IsNotNull(group, "반복과제 미니 바 그룹");
            Assert.IsFalse(group.gameObject.activeSelf, "중계 화면 미니 바 숨김");
            Assert.IsNotNull(group.Find("ProgressCount"));
        }

        // ================================================================== C. 스토브리그

        private List<PlayerTemplate> Market()
        {
            var list = new List<PlayerTemplate>();
            string[] pos = { "C", "1B", "2B", "SS", "SP", "RP" };
            var grades = StoveLeagueRules.FaGrades;
            for (int i = 0; i < 8; i++) list.Add(Template(grades[i % grades.Length], $"삼성{i}", 2020 + i % 5, Team.Samsung, pos[i % pos.Length]));
            for (int i = 0; i < 12; i++) list.Add(Template(grades[i % grades.Length], $"김타{i}", 2021, i % 2 == 0 ? Team.LG : Team.KIA, pos[i % pos.Length]));
            list.Add(Template(Grade.LIVE_NORMAL, "일반", 2024, Team.Samsung));
            list.Add(Template(Grade.GOLDEN_GLOVE, "골글", 2024, Team.Samsung));
            return list;
        }

        [Test]
        public void StoveFa_SixOffers_HalfFavorite_GradeRange_Deterministic_SignAddsCard()
        {
            var market = Market();
            var offers = StoveLeagueRules.FaOffers(market, Team.Samsung, 2026);
            Assert.AreEqual(StoveLeagueRules.FaOfferCount, offers.Count);
            Assert.GreaterOrEqual(offers.Count(t => t.Team == Team.Samsung), 3, "선택 구단 50% 이상");
            Assert.IsTrue(offers.All(t => StoveLeagueRules.FaGrades.Contains(t.Grade)), "라이브 에픽 ~ 타이틀 홀더");
            Assert.AreEqual(offers.Count, offers.Select(t => t.RealPlayerId).Distinct().Count(), "중복 선수 없음");
            CollectionAssert.AreEqual(offers, StoveLeagueRules.FaOffers(market, Team.Samsung, 2026), "같은 시즌 = 같은 매물");

            var state = new StoveLeagueState();
            state.EnsureSeason(2026);
            var ledger = new MemoryRecruitLedger { FavoriteTeam = Team.Samsung, Points = 1000000 };
            var target = offers[0];
            Assert.IsTrue(StoveLeagueRules.TrySignFa(target, state, ledger, Issue, out var card, out var msg), msg);
            Assert.AreSame(target, card.Template);
            CollectionAssert.Contains(ledger.Cards, card, "보유 선수 즉시 추가");
            Assert.AreEqual(1000000 - StoveLeagueRules.ContractPrice(target.Grade), ledger.Points);
            Assert.IsFalse(StoveLeagueRules.TrySignFa(target, state, ledger, Issue, out _, out _), "같은 FA 재계약 불가");

            var poor = new MemoryRecruitLedger { FavoriteTeam = Team.Samsung, Points = 10 };
            Assert.IsFalse(StoveLeagueRules.TrySignFa(offers[1], state, poor, Issue, out _, out var fail));
            StringAssert.Contains("포인트 부족", fail);
            Assert.AreEqual(0, poor.Cards.Count);
        }

        [Test]
        public void StoveTrade_ProtectsLineup_ReceivesFavoriteSameOrHigher_AcceptSwapsCards()
        {
            var market = Market();
            var ledger = new MemoryRecruitLedger { FavoriteTeam = Team.Samsung, Points = 10000 };
            var lineup = Card(Template(Grade.ALLSTAR, "주전", 2024, Team.Samsung, "SS"));
            ledger.Cards.Add(lineup);
            ledger.Lineup.Add(lineup);
            var gg = Card(Template(Grade.GOLDEN_GLOVE, "골글보유", 2024, Team.Samsung, "SS"));
            ledger.Cards.Add(gg);
            for (int i = 0; i < 6; i++) ledger.Cards.Add(Card(Template(i % 2 == 0 ? Grade.LIVE_EPIC : Grade.ALLSTAR, $"잉여{i}", 2022, Team.Doosan, i % 2 == 0 ? "SS" : "SP")));

            var surplus = StoveLeagueRules.SurplusCards(ledger.Inventory, ledger.Roster);
            CollectionAssert.DoesNotContain(surplus, lineup, "주전 라인업 보호");
            CollectionAssert.DoesNotContain(surplus, gg, "골든글러브 이상 보호");

            var offers = StoveLeagueRules.TradeOffers(ledger.Inventory, ledger.Roster, market, Team.Samsung, 2026, 0);
            Assert.AreEqual(StoveLeagueRules.TradeOfferCount, offers.Count);
            CollectionAssert.AreEqual(new[] { 1, 2, 2 }, offers.Select(o => o.Give.Count).ToArray());
            Assert.AreEqual(offers.Sum(o => o.Give.Count), offers.SelectMany(o => o.Give).Distinct().Count(), "제안 간 카드 중복 없음");
            foreach (var o in offers)
            {
                Assert.IsTrue(o.Give.All(p => surplus.Contains(p)));
                Assert.GreaterOrEqual(CardGrowthRules.PowerRank(o.Receive.Grade), o.Give.Max(p => CardGrowthRules.PowerRank(p.Template.Grade)), "동급 이상");
                Assert.AreEqual(Team.Samsung, o.Receive.Team, "선택 구단 카드");
            }
            CollectionAssert.AreEqual(offers.Select(o => o.Id).ToArray(),
                StoveLeagueRules.TradeOffers(ledger.Inventory, ledger.Roster, market, Team.Samsung, 2026, 0).Select(o => o.Id).ToArray(), "같은 라운드 = 같은 제안(저장 · 재시작 안전)");

            var state = new StoveLeagueState();
            state.EnsureSeason(2026);
            var offer = offers[1];
            int before = ledger.Cards.Count;
            Assert.IsTrue(StoveLeagueRules.TryAcceptTrade(offer, state, ledger, Issue, out var got, out var msg), msg);
            Assert.AreEqual(before - offer.Give.Count + 1, ledger.Cards.Count);
            Assert.IsTrue(offer.Give.All(p => !ledger.Cards.Contains(p)));
            Assert.AreSame(offer.Receive, got.Template);
            Assert.AreEqual(1, state.TradeRound);

            // 내줄 카드가 라인업에 들어가면 수락 불가
            var stale = offers[0];
            ledger.Lineup.Add(stale.Give[0]);
            Assert.IsFalse(StoveLeagueRules.TryAcceptTrade(stale, state, ledger, Issue, out _, out _));

            Assert.IsTrue(StoveLeagueRules.TryRefreshTrades(state, ledger, out _));
            Assert.AreEqual(10000 - StoveLeagueRules.TradeRefreshCost, ledger.Points);
            Assert.AreEqual(2, state.TradeRound);
        }

        [Test]
        public void StoveDraft_FavoritePositionLatestYearProspect_OncePerSeason_ResetsNextSeason()
        {
            var list = new List<PlayerTemplate>
            {
                Template(Grade.LIVE_EPIC, "유망주A", 2025, Team.Samsung, "SS"),
                Template(Grade.ALLSTAR, "유망주B", 2025, Team.Samsung, "SS"),
                Template(Grade.LIVE_EPIC, "베테랑", 2019, Team.Samsung, "SS"),
                Template(Grade.LIVE_EPIC, "타구단", 2026, Team.LG, "SS"),
                Template(Grade.LIVE_NORMAL, "일반", 2026, Team.Samsung, "SS"),
                Template(Grade.LIVE_EPIC, "포수", 2026, Team.Samsung, "C"),
            };
            var pool = StoveLeagueRules.DraftPool(list, Team.Samsung, "SS");
            CollectionAssert.AreEquivalent(new[] { "유망주A", "유망주B" }, pool.Select(t => t.PlayerName).ToArray());

            var state = new StoveLeagueState();
            state.EnsureSeason(2026);
            var ledger = new MemoryRecruitLedger { FavoriteTeam = Team.Samsung };
            Assert.IsTrue(StoveLeagueRules.TryDraft(list, "SS", state, ledger, n => 0, Issue, out var card, out var msg), msg);
            Assert.AreEqual("SS", CardGrowthRules.PositionKey(card));
            Assert.AreEqual(Team.Samsung, card.Template.Team);
            Assert.GreaterOrEqual(CardGrowthRules.PowerRank(card.Template.Grade), CardGrowthRules.PowerRank(Grade.LIVE_EPIC));
            Assert.IsFalse(StoveLeagueRules.TryDraft(list, "SS", state, ledger, n => 0, Issue, out _, out _), "시즌 1회");
            Assert.IsTrue(state.EnsureSeason(2027), "새 시즌");
            Assert.IsFalse(state.DraftUsed);
            Assert.IsTrue(StoveLeagueRules.TryDraft(list, "C", state, ledger, n => 0, Issue, out var catcher, out _));
            Assert.AreEqual("포수", catcher.Template.PlayerName);
        }

        [Test]
        public void StoveForeign_DetectsForeignNames_ThreeCandidates_SignOnce()
        {
            Assert.IsTrue(StoveLeagueRules.IsForeignName("로하스"));
            Assert.IsTrue(StoveLeagueRules.IsForeignName("페디"));
            Assert.IsTrue(StoveLeagueRules.IsForeignName("메릴 켈리"));
            Assert.IsFalse(StoveLeagueRules.IsForeignName("김도영"));
            Assert.IsFalse(StoveLeagueRules.IsForeignName("구자욱"));
            var list = new List<PlayerTemplate>
            {
                Template(Grade.ALLSTAR, "로하스", 2020, Team.KT, "RF"), Template(Grade.FRANCHISE, "페디", 2023, Team.NC, "SP"),
                Template(Grade.LIVE_EPIC, "에레디아", 2024, Team.SSG, "LF"), Template(Grade.TITLE_HOLDER, "알칸타라", 2020, Team.Doosan, "SP"),
                Template(Grade.ALLSTAR, "이정후", 2022, Team.Kiwoom, "CF"), Template(Grade.LIVE_NORMAL, "레이예스", 2024, Team.Lotte, "RF"),
            };
            var candidates = StoveLeagueRules.ForeignCandidates(list, Team.Samsung, 2026);
            Assert.AreEqual(StoveLeagueRules.ForeignCandidateCount, candidates.Count);
            Assert.IsTrue(candidates.All(t => StoveLeagueRules.IsForeignName(t.PlayerName) && t.Grade != Grade.LIVE_NORMAL));

            var state = new StoveLeagueState();
            state.EnsureSeason(2026);
            var ledger = new MemoryRecruitLedger { FavoriteTeam = Team.Samsung, Points = 500000 };
            Assert.IsTrue(StoveLeagueRules.TrySignForeign(candidates[0], state, ledger, Issue, out var card, out var msg), msg);
            CollectionAssert.Contains(ledger.Cards, card);
            Assert.IsFalse(StoveLeagueRules.TrySignForeign(candidates[1], state, ledger, Issue, out _, out _), "시즌 1회");
        }

        [Test]
        public void StoveState_SavedInSaveData_JsonRoundTrip()
        {
            var data = new GameSaveData();
            data.StoveLeague.EnsureSeason(2026);
            data.StoveLeague.SignedFaIds.Add("FA_1");
            data.StoveLeague.DraftUsed = true;
            data.StoveLeague.TradeRound = 3;
            data.HallOfFame.Add(new HallOfFameEntrySaveData { SeasonNumber = 2, TierName = "퓨처스", KoreanSeriesWon = true, Mvp = "구자욱", Titles = new List<string> { "타격왕 구자욱 .352 ★" } });
            var back = JsonUtility.FromJson<GameSaveData>(JsonUtility.ToJson(data));
            Assert.AreEqual(20, back.SaveVersion); // [TASK-GM-09] v20 - 글로벌 대회 / [TASK-GM-08] v19 - 퓨처스 풀 · FA 보상 / [TASK-GM-07] v18 - 경기 결과 · 감독 설정 · 포스트시즌 진행 / [TASK-GM-06] v17 - 프런트 오피스 · 드래프트 · ABS(v16 = GM-05 치어리더 피로도, v15 = GM-04 시상, v14 = GM-02 단장 모드)
            Assert.AreEqual(2026, back.StoveLeague.SeasonKey);
            CollectionAssert.AreEqual(new[] { "FA_1" }, back.StoveLeague.SignedFaIds);
            Assert.IsTrue(back.StoveLeague.DraftUsed);
            Assert.AreEqual(3, back.StoveLeague.TradeRound);
            Assert.AreEqual(2, back.HallOfFame[0].SeasonNumber);
            Assert.IsTrue(back.HallOfFame[0].KoreanSeriesWon);
            Assert.AreEqual("타격왕 구자욱 .352 ★", back.HallOfFame[0].Titles[0]);
        }

        // ================================================================== D. 리그 기록실

        [Test]
        public void Records_BatterTop10_SortedPerCategory_AverageNeedsQualifying_UserHighlighted()
        {
            var batters = new Dictionary<Player, BatterSeasonStats>();
            var mine = new HashSet<Player>();
            for (int i = 0; i < 14; i++)
            {
                var p = Card(Template(Grade.LIVE_EPIC, $"타자{i:00}", 2024, i % 2 == 0 ? Team.Samsung : Team.LG));
                batters[p] = new BatterSeasonStats { AtBats = 300, Hits = 60 + i * 5, HomeRuns = i, RunsBattedIn = 20 + i * 3, Walks = 20 };
                if (i == 13) mine.Add(p);
            }
            var part = Card(Template(Grade.LIVE_EPIC, "대타", 2024));
            batters[part] = new BatterSeasonStats { AtBats = 10, Hits = 9, HomeRuns = 0, RunsBattedIn = 1 };

            var avg = LeagueRecordsRules.TopBatters(batters, BatterRecord.Average, 100, mine.Contains);
            Assert.AreEqual(10, avg.Count, "TOP 10");
            Assert.IsFalse(avg.Any(r => r.Player == part), "규정타석 미달 제외");
            CollectionAssert.AreEqual(Enumerable.Range(1, 10).ToArray(), avg.Select(r => r.Rank).ToArray());
            Assert.AreEqual("타자13'24", avg[0].Name);
            Assert.IsTrue(avg[0].IsUser, "내 구단 하이라이트");
            Assert.AreEqual(".417", avg[0].Value);

            var hr = LeagueRecordsRules.TopBatters(batters, BatterRecord.HomeRuns, 100, mine.Contains);
            Assert.AreEqual("13홈런", hr[0].Value);
            Assert.IsTrue(hr.Zip(hr.Skip(1), (a, b) => int.Parse(a.Value.Replace("홈런", "")) >= int.Parse(b.Value.Replace("홈런", ""))).All(x => x));
            Assert.AreEqual("59타점", LeagueRecordsRules.TopBatters(batters, BatterRecord.RunsBattedIn, 100, null)[0].Value);
            Assert.AreEqual("125안타", LeagueRecordsRules.TopBatters(batters, BatterRecord.Hits, 100, null)[0].Value);
            CollectionAssert.AreEqual(new[] { "타율", "홈런", "타점", "안타" }, LeagueRecordsRules.BatterLabels);
        }

        [Test]
        public void Records_PitcherTop10_WinsEraStrikeoutsInnings()
        {
            var pitchers = new Dictionary<Player, PitcherSeasonStats>();
            for (int i = 0; i < 12; i++)
            {
                var p = Card(Template(Grade.LIVE_EPIC, $"투수{i:00}", 2024, Team.KT, "SP"));
                pitchers[p] = new PitcherSeasonStats { OutsRecorded = 300 + i * 10, EarnedRuns = 40 - i, Wins = i, Losses = 3, Strikeouts = 50 + i * 7 };
            }
            var closer = Card(Template(Grade.LIVE_EPIC, "마무리", 2024, Team.KT, "CP"));
            pitchers[closer] = new PitcherSeasonStats { OutsRecorded = 30, EarnedRuns = 0, Wins = 1, Strikeouts = 15 };

            var era = LeagueRecordsRules.TopPitchers(pitchers, PitcherRecord.Era, 100, null);
            Assert.AreEqual(10, era.Count);
            Assert.IsFalse(era.Any(r => r.Player == closer), "규정이닝 미달 제외");
            Assert.AreEqual("투수11'24", era[0].Name, "ERA 낮은 순");
            var wins = LeagueRecordsRules.TopPitchers(pitchers, PitcherRecord.Wins, 100, null);
            Assert.AreEqual("11승 3패", wins[0].Value);
            Assert.AreEqual("127K", LeagueRecordsRules.TopPitchers(pitchers, PitcherRecord.Strikeouts, 100, null)[0].Value);
            Assert.AreEqual("136 2/3이닝", LeagueRecordsRules.TopPitchers(pitchers, PitcherRecord.Innings, 100, null)[0].Value);
            CollectionAssert.AreEqual(new[] { "다승", "평균자책점", "탈삼진", "이닝" }, LeagueRecordsRules.PitcherLabels);
        }

        [Test]
        public void HallOfFame_Entry_StoresSeasonTierRecordKsMvpTitles()
        {
            var bat = Card(Template(Grade.LIVE_EPIC, "강타자", 2024, Team.Samsung));
            var pit = Card(Template(Grade.LIVE_EPIC, "에이스", 2024, Team.LG, "SP"));
            string mvp = LeagueRecordsRules.SeasonMvp(
                new Dictionary<Player, BatterSeasonStats> { { bat, new BatterSeasonStats { AtBats = 500, Hits = 170, HomeRuns = 40, RunsBattedIn = 120 } } },
                new Dictionary<Player, PitcherSeasonStats> { { pit, new PitcherSeasonStats { OutsRecorded = 540, EarnedRuns = 50, Wins = 15, Strikeouts = 180 } } });
            StringAssert.StartsWith("강타자 (Samsung)", mvp, "MVP = 점수 최고(타자 410 > 투수 365)");

            var titles = new List<SeasonTitle> { new SeasonTitle { Category = "홈런왕", PlayerName = "강타자", ValueLabel = "40홈런", IsUserPlayer = true } };
            var entry = LeagueRecordsRules.BuildEntry(3, 2027, LeagueTier.Amateur, 1, 2, 85, 4, 55, Team.Samsung, Team.Samsung, mvp, titles);
            Assert.AreEqual(3, entry.SeasonNumber);
            Assert.AreEqual(2027, entry.SeasonYear);
            Assert.AreEqual(LeagueTierTable.DisplayName(LeagueTier.Amateur), entry.TierName);
            Assert.AreEqual((1, 2, 85, 4, 55), (entry.FinalRank, entry.RegularSeasonRank, entry.Wins, entry.Draws, entry.Losses));
            Assert.IsTrue(entry.KoreanSeriesWon);
            CollectionAssert.AreEqual(new[] { "홈런왕 강타자 40홈런 ★" }, entry.Titles);
            string text = LeagueRecordsRules.EntryText(entry);
            StringAssert.Contains("시즌 3 (2027)", text);
            StringAssert.Contains("최종 1위", text);
            StringAssert.Contains("85승 4무 55패", text);
            StringAssert.Contains("한국시리즈 우승", text);
            StringAssert.Contains("MVP 강타자", text);
            Assert.IsFalse(LeagueRecordsRules.BuildEntry(1, 2026, LeagueTier.Amateur, 4, 4, 70, 2, 72, Team.LG, Team.Samsung, null, null).KoreanSeriesWon);
        }

        // ================================================================== 씬 배선

        [Test]
        public void Scene_SeasonCycleStoveButton_StoveOverlay_LeagueRecordsView()
        {
            OpenScene();
            var cycle = Object.FindAnyObjectByType<SeasonCycleView>(FindObjectsInactive.Include);
            Assert.IsNotNull(cycle);
            var stoveButton = cycle.transform.Find(SeasonCycleView.RootName + "/" + SeasonCycleView.StoveButtonName);
            Assert.IsNotNull(stoveButton, "시즌 결산 [스토브리그 전력 보강] 버튼");
            Assert.AreEqual(SeasonCycleView.StoveButtonLabel, stoveButton.GetComponentInChildren<Text>(true).text);
            StringAssert.Contains("스토브리그 전력 보강", SeasonCycleView.StoveButtonLabel);

            var stove = Object.FindAnyObjectByType<StoveLeagueView>(FindObjectsInactive.Include);
            Assert.IsNotNull(stove, "스토브리그 오버레이");
            var stoveRoot = stove.transform.Find(StoveLeagueView.RootName);
            Assert.IsNotNull(stoveRoot);
            foreach (var name in new[] { "Tab_Fa", "Tab_Trade", "Tab_Draft", "Tab_Foreign", "Secondary", "Close" }) Assert.IsNotNull(stoveRoot.Find(name), name);
            Assert.AreEqual(SetupTask190.LobbyRoot().transform, stove.transform.parent, "로비 위 오버레이");

            var records = Object.FindAnyObjectByType<LeagueRecordsView>(FindObjectsInactive.Include);
            Assert.IsNotNull(records, "리그 기록실");
            Assert.IsNotNull(records.GetComponentInParent<LeagueStatsUIController>(true), "LeagueStats 화면 위");
            var root = records.transform.Find(LeagueRecordsView.RootName);
            foreach (var name in new[] { "Tab_Batters", "Tab_Pitchers", "Tab_HallOfFame", "Table/Row9", "HallOfFame/HallScroll", "Close", "StoveButton" })
                Assert.IsNotNull(root.Find(name), name);
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                Assert.AreNotEqual(FontStyle.Bold, t.fontStyle, t.name);
                Assert.That(TextTidy.EffectiveSize(t), Is.InRange(16, 26), $"{t.name} 16~26pt");
            }
        }
    }
}
