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
using KBOManager.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-KBO-188] 각성 재료 규칙(같은 시즌 · 같은 선수 +3각 / 같은 시즌 · 다른 선수 +1각) + 각성 표기 전환 · 중계 시즌 누적 성적 ·
    /// 선수 카드 3단 레이아웃/초상화 폴백 · 성장 센터 대상 2줄 · 타순표 열 분리 · 경기 진행 방식(빠른 진행 N경기 / 하이라이트 / 풀 플레이).
    /// </summary>
    public class Task188Tests
    {
        private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
        private bool sceneOpened;
        private LineupAssignment previousAssignment;

        [SetUp]
        public void SetUp()
        {
            previousAssignment = LineupAssignment.Active;
            LineupAssignment.Active = null;
            PortraitResolver.ResetCache();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var type in new[] { typeof(GameManager), typeof(LeagueManager), typeof(SeasonStatManager) }) SetInstance(type, null);
            foreach (var obj in created) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            created.Clear();
            Task183Report.CleanupTemp();
            LineupAssignment.Active = previousAssignment;
            PortraitResolver.ResetCache();
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = false;
        }

        private static void SetInstance(Type type, object value)
        {
            var setter = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetSetMethod(true);
            setter?.Invoke(null, new[] { value });
        }

        private PlayerTemplate Template(Grade grade, string person, int year = 2024, Team team = Team.Samsung, string templateId = null)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = templateId ?? $"{person}_{grade}_{year}_{Guid.NewGuid():N}";
            t.RealPlayerId = person;
            t.PlayerName = person;
            t.SeasonYear = year;
            t.Team = team;
            t.Grade = grade;
            t.BatterPosition = BatterPosition.RightField;
            t.BatterStats = StatProfiles.SpreadBatter(80, BatterPosition.RightField, 7);
            return t;
        }

        private Player Card(Grade grade, string person, int year = 2024, int reinforce = 0, int awaken = 0, string templateId = null) =>
            new Player(Guid.NewGuid().ToString(), Template(grade, person, year, Team.Samsung, templateId)) { ReinforceLevel = reinforce, AwakenLevel = awaken };

        private void OpenScene()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
        }

        // ================================================================== A. 각성 재료 규칙 · 각성 표기

        [Test]
        public void Awaken_SameGradeSamePlayer_Plus3_RegardlessOfYear()
        {
            var target = Card(Grade.GOLDEN_GLOVE, "KOO", 2024, reinforce: 10); // [TASK-KBO-189] +10강 선행
            foreach (int year in new[] { 2024, 2021, 2025 })
                Assert.AreEqual(3, CardGrowthRules.AwakenGainFor(target, Card(Grade.GOLDEN_GLOVE, "KOO", year)), $"같은 시즌 등급 · 같은 선수 {year}년 = +3각");
            UpgradeManager.ApplyAwaken(target, new List<Player> { Card(Grade.GOLDEN_GLOVE, "KOO", 2021), Card(Grade.GOLDEN_GLOVE, "KOO", 2023) });
            Assert.AreEqual(6, target.AwakenLevel, "다른 연도 같은 선수 2장 = 6각");
        }

        [Test]
        public void Awaken_SameGradeOtherPlayer_Plus1_OtherGradeAndSelfInvalid()
        {
            var target = Card(Grade.ALLSTAR, "KOO", 2024, reinforce: 10);
            var other = Card(Grade.ALLSTAR, "LEE", 2022);
            Assert.AreEqual(1, CardGrowthRules.AwakenGainFor(target, other), "[TASK-KBO-189] 같은 시즌 등급 · 같은 포지션(RF) 다른 선수 = +1각");
            Assert.AreEqual(0, CardGrowthRules.AwakenGainFor(target, Card(Grade.TITLE_HOLDER, "KOO", 2024)), "다른 시즌 등급은 재료 아님");
            Assert.AreEqual(0, CardGrowthRules.AwakenGainFor(target, target), "자기 자신");
            Assert.AreEqual("[같은 선수 +3각]", CardGrowthRules.AwakenMaterialBadge(target, Card(Grade.ALLSTAR, "KOO", 2020)));
            Assert.AreEqual("[같은 포지션 +1각]", CardGrowthRules.AwakenMaterialBadge(target, other)); // [TASK-KBO-189] 배지 문구 변경

            UpgradeManager.ApplyAwaken(target, new List<Player> { other, Card(Grade.ALLSTAR, "PARK", 2023), Card(Grade.ALLSTAR, "KOO", 2025) });
            Assert.AreEqual(5, target.AwakenLevel, "다른 선수 1 + 1 + 같은 선수 3 = 5각");
        }

        [Test]
        public void GrowthCenter_AwakenCandidates_AllSameGradeCards_LineupExcluded_SamePlayerFirst()
        {
            var target = Card(Grade.GOLDEN_GLOVE, "KOO", 2024);
            var same = Card(Grade.GOLDEN_GLOVE, "KOO", 2021);
            var otherA = Card(Grade.GOLDEN_GLOVE, "LEE", 2024);
            var otherB = Card(Grade.GOLDEN_GLOVE, "PARK", 2023);
            var lineupCard = Card(Grade.GOLDEN_GLOVE, "CHOI", 2024);
            var otherGrade = Card(Grade.SIGNATURE, "KOO", 2024);
            var inventory = new List<Player> { target, otherA, same, otherB, lineupCard, otherGrade };
            var candidates = GrowthCenterRules.MaterialCandidates(GrowthTab.Awaken, target, inventory, new[] { lineupCard, target });
            Assert.AreEqual(3, candidates.Count, "같은 시즌 등급 가용 카드 전부(같은 선수 + 다른 선수), 라인업 · 다른 등급 제외");
            Assert.AreSame(same, candidates[0], "같은 선수(+3각) 우선");
            CollectionAssert.AreEquivalent(new[] { otherA, otherB }, candidates.Skip(1).ToList());
            CollectionAssert.DoesNotContain(candidates, lineupCard);
        }

        [Test]
        public void GrowthBadge_ReinforceUntilAwakened_ThenAwakenStageOrTranscend()
        {
            Assert.AreEqual("+10", CardGrowthRules.GrowthBadgeLabel(Grade.GOLDEN_GLOVE, 10, 0), "미각성 = 강화 단계");
            Assert.AreEqual("+3", CardGrowthRules.GrowthBadgeLabel(Grade.LIVE_NORMAL, 3, 0));
            Assert.AreEqual("", CardGrowthRules.GrowthBadgeLabel(Grade.LIVE_NORMAL, 0, 0), "성장 없음 = 배지 없음");
            Assert.AreEqual("1각", CardGrowthRules.GrowthBadgeLabel(Grade.GOLDEN_GLOVE, 10, 1), "1각부터 +10 대신 각성 표기");
            Assert.AreEqual("9각", CardGrowthRules.GrowthBadgeLabel(Grade.ALLSTAR, 10, 10), "초월 불가 등급의 10 = 9각");
            Assert.AreEqual("초월", CardGrowthRules.GrowthBadgeLabel(Grade.GOLDEN_GLOVE, 10, 10));
        }

        [Test]
        public void PlayerCard_Shows10ReinforceUntilAwakened_ThenAwakenBadge_ThreeTierLayout()
        {
            OpenScene();
            var roster = UnityEngine.Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(roster?.CardPrefab, "씬 카드 템플릿");
            var card = UnityEngine.Object.Instantiate(roster.CardPrefab);
            created.Add(card.gameObject);

            var player = Card(Grade.GOLDEN_GLOVE, "KOO", 2024, reinforce: 10);
            card.Setup(player);
            Assert.AreEqual("+10", card.GrowthBadgeText, "미각성 10강 = +10");
            Assert.AreEqual("RF KOO'24", card.NameLabel, "네임플레이트 = 포지션 이름'연도");

            player.AwakenLevel = 3;
            card.Setup(player);
            Assert.AreEqual("3각", card.GrowthBadgeText, "1각 이상이면 +10 숨기고 각성 표기");
            player.AwakenLevel = 10;
            card.Setup(player);
            Assert.AreEqual("초월", card.GrowthBadgeText);
            var badge = card.transform.Find("GrowthBadge188").GetComponent<Image>();
            Assert.AreEqual((Color32)PlayerCardUI.TranscendBadgeColor, (Color32)badge.color, "초월 전용 퍼플 배지");

            // 3단 분리: 헤더(OVR · SD) ≥ 0.86 · 네임플레이트(이름 · 배지) ≤ 0.17 · 네임플레이트 색 #0F172A
            RectTransform R(string n) => (RectTransform)card.transform.Find(n);
            Assert.GreaterOrEqual(R("OvrText").anchorMin.y, PlayerCardUI.HeaderBottom);
            Assert.GreaterOrEqual(R("SetDeckScoreText").anchorMin.y, PlayerCardUI.HeaderBottom);
            Assert.LessOrEqual(R("NameText").anchorMax.y, PlayerCardUI.NamePlateTop);
            Assert.LessOrEqual(R("GrowthBadge188").anchorMax.y, PlayerCardUI.NamePlateTop);
            Assert.LessOrEqual(R("GrowthBadge188").anchorMax.x, R("NameText").anchorMin.x, "배지와 이름이 겹치지 않는다");
            Assert.AreEqual((Color32)PlayerCardUI.NamePlateColor, (Color32)R("NameStrip").GetComponent<Image>().color);
            Assert.IsFalse(R("PositionText").gameObject.activeSelf, "포지션은 네임플레이트로 이동(헤더 겹침 제거)");
            Assert.IsTrue(R("Vignette188").GetComponent<Image>().sprite != null, "일러스트 하단 다크 비네팅");

            player.AwakenLevel = 0;
            player.ReinforceLevel = 0;
            card.Setup(player);
            Assert.AreEqual("", card.GrowthBadgeText, "성장 없으면 배지 숨김");
            Assert.AreEqual(0.04f, R("NameText").anchorMin.x, 0.001f, "배지가 없으면 이름이 네임플레이트 전체 폭");
        }

        [Test]
        public void Portrait_FallsBackToSamePlayerPortrait_ForEveryGrade()
        {
            PortraitResolver.SetIndex(new[]
            {
                "Portraits/SAMSUNG/2024/SAMSUNG_2024_PLY_004038_GG",
                "Portraits/SAMSUNG/2024/SAMSUNG_2024_PLY_004038_SIG",
                "Portraits/SAMSUNG/2024/SAMSUNG_2024_PLY_004038_SIG_BG",
                "Portraits/SAMSUNG/2025/SAMSUNG_2025_PLY_004038_NOR",
            });
            PlayerTemplate T(string id, Grade g, int year) => Template(g, "PLY_004038", year, Team.Samsung, id);
            Assert.AreEqual("Portraits/SAMSUNG/2024/SAMSUNG_2024_PLY_004038_GG", PortraitResolver.Resolve(T("SAMSUNG_2024_PLY_004038_GG", Grade.GOLDEN_GLOVE, 2024)), "정확한 카드");
            Assert.AreEqual("Portraits/SAMSUNG/2024/SAMSUNG_2024_PLY_004038_GG", PortraitResolver.Resolve(T("SAMSUNG_2023_PLY_004038_GG", Grade.GOLDEN_GLOVE, 2023)), "같은 등급 가까운 연도");
            Assert.AreEqual("Portraits/SAMSUNG/2025/SAMSUNG_2025_PLY_004038_NOR", PortraitResolver.Resolve(T("SAMSUNG_2026_PLY_004038_LN", Grade.LIVE_NORMAL, 2026)), "LN = NOR 별칭");
            Assert.AreEqual("Portraits/SAMSUNG/2024/SAMSUNG_2024_PLY_004038_SIG", PortraitResolver.Resolve(T("SAMSUNG_2026_PLY_004038_SIG", Grade.SIGNATURE, 2026)));
            Assert.IsNotNull(PortraitResolver.Resolve(T("SAMSUNG_2021_PLY_004038_TH", Grade.TITLE_HOLDER, 2021)), "초상화 없는 등급도 같은 선수 사진");
            Assert.IsNull(PortraitResolver.Resolve(Template(Grade.GOLDEN_GLOVE, "PLY_009999", 2024, Team.Samsung, "SAMSUNG_2024_PLY_009999_GG")), "초상화가 전혀 없는 선수는 null(기본 실루엣)");
        }

        [Test]
        public void PlayerCard_KooJaWook_PortraitRendered_InGoldenGloveSignatureAndLive()
        {
            OpenScene();
            var roster = UnityEngine.Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            var card = UnityEngine.Object.Instantiate(roster.CardPrefab);
            created.Add(card.gameObject);
            Assert.IsNotNull(Resources.Load<TextAsset>(PortraitResolver.IndexResource), "초상화 색인(Setup 생성)");
            foreach (var (id, grade, year) in new[]
            {
                ("SAMSUNG_2024_PLY_004038_GG", Grade.GOLDEN_GLOVE, 2024),
                ("SAMSUNG_2023_PLY_004038_GG", Grade.GOLDEN_GLOVE, 2023),
                ("SAMSUNG_2025_PLY_004038_TH", Grade.TITLE_HOLDER, 2025),
                ("SAMSUNG_2026_PLY_004038_LN", Grade.LIVE_NORMAL, 2026),
                ("SAMSUNG_2024_PLY_004038_SIG", Grade.SIGNATURE, 2024),
            })
            {
                var p = new Player(Guid.NewGuid().ToString(), Template(grade, "PLY_004038", year, Team.Samsung, id));
                card.Setup(p);
                Assert.IsTrue(card.PortraitVisible, $"{id} 초상화 표시");
                StringAssert.Contains("PLY_004038", card.PortraitSprite.name, $"{id} = 구자욱 사진");
            }
        }

        // ================================================================== B. 중계 시즌 누적 성적

        [Test]
        public void Broadcast_SeasonPlusTodayLive_AverageHomeRunsRbi_EraWinLoss()
        {
            var season = new BatterSeasonStats { AtBats = 100, Hits = 31, HomeRuns = 14, Walks = 9, RunsBattedIn = 52 };
            var today = new CompyaGameTracker.BatLine { AtBats = 3, Hits = 2, HomeRuns = 1, RunsBattedIn = 3 };
            var live = CompyaGameTracker.CombineBatting(season, today);
            Assert.AreEqual(103, live.AtBats);
            Assert.AreEqual(".320", CompyaGameTracker.FormatAverage(live.Average), "시즌 누적 + 오늘 실시간 합산 타율(.000 아님)");
            Assert.AreEqual("타율 .320 | 15홈런 55타점", live.Summary);
            Assert.AreEqual(".000", CompyaGameTracker.FormatAverage(CompyaGameTracker.CombineBatting(null, new CompyaGameTracker.BatLine()).Average), "기록이 전혀 없을 때만 .000");

            var pitch = CompyaGameTracker.CombinePitching(new PitcherSeasonStats { OutsRecorded = 270, EarnedRuns = 30, Wins = 7, Losses = 3 },
                new CompyaGameTracker.PitchLine { Outs = 15, Runs = 2 });
            Assert.AreEqual(285, pitch.Outs);
            Assert.AreEqual("7승 3패 ERA 3.03", pitch.Summary);
        }

        [Test]
        public void Tracker_CountsRbi_AndSeasonStatsRecordRbi()
        {
            var bat = Card(Grade.LIVE_NORMAL, "BAT");
            var pit = Card(Grade.LIVE_NORMAL, "PIT");
            var events = new List<PlayEvent>
            {
                new PlayEvent { Type = PlayEventType.AtBatResult, Inning = 1, IsTopHalf = true, Batter = bat, Pitcher = pit, Result = AtBatResult.HomeRun, RunsScoredThisPlay = 2 },
                new PlayEvent { Type = PlayEventType.AtBatResult, Inning = 3, IsTopHalf = true, Batter = bat, Pitcher = pit, Result = AtBatResult.Single, RunsScoredThisPlay = 1 },
            };
            var tracker = CompyaGameTracker.Build(events, 1);
            Assert.AreEqual(3, tracker.BatOf(bat).RunsBattedIn, "오늘 타점 = 그 타석 득점 합");

            var go = new GameObject("Task188Stats");
            created.Add(go);
            var stats = go.AddComponent<SeasonStatManager>();
            stats.RecordAtBat(new AtBatStepResult { Batter = bat, Pitcher = pit, Result = AtBatResult.HomeRun, RunsScoredThisPlay = 2, IsTopHalf = true, State = new MatchState { Inning = 1, Outs = 0 } }, Team.KIA, Team.Samsung);
            Assert.AreEqual(2, stats.GetBatterStats(bat).RunsBattedIn, "시즌 기록에도 타점 누적");
            Assert.AreEqual(1, stats.GetBatterStats(bat).HomeRuns);
        }

        // ================================================================== C. 성장 센터 대상 2줄 · 타순표 열 분리

        [Test]
        public void TargetRows_ShowNameYearPosition_AndGradeGrowthOvr()
        {
            var p = Card(Grade.GOLDEN_GLOVE, "구자욱", 2024, reinforce: 10, awaken: 3);
            Assert.AreEqual("구자욱'24 (RF)", CardDisplay.TargetLine1(p));
            Assert.AreEqual("GG · 3각 · OVR 98", CardDisplay.TargetLine2(p, 98));
            Assert.AreEqual("GG · +7강 · OVR 90", CardDisplay.TargetLine2(Card(Grade.GOLDEN_GLOVE, "A", 2024, reinforce: 7), 90));
            Assert.AreEqual("LIVE · 명함 · OVR 60", CardDisplay.TargetLine2(Card(Grade.LIVE_NORMAL, "B", 2026), 60));
        }

        [Test]
        public void GrowthCenter_TargetRow_RendersNameLineSeparately()
        {
            OpenScene();
            var view = UnityEngine.Object.FindAnyObjectByType<GrowthCenterView>(FindObjectsInactive.Include);
            Assert.IsNotNull(view);
            view.Build();
            var content = (RectTransform)typeof(GrowthCenterView).GetField("targetContent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var rebuild = typeof(GrowthCenterView).GetMethod("RebuildRows", BindingFlags.Instance | BindingFlags.NonPublic);
            var p = Card(Grade.GOLDEN_GLOVE, "구자욱", 2024, awaken: 3);
            var items = new List<(string, Color, UnityEngine.Events.UnityAction)> { ($"{CardDisplay.TargetLine1(p)}\n{CardDisplay.TargetLine2(p, 98)}", Color.gray, () => { }) };
            rebuild.Invoke(view, new object[] { content, items, 3 });
            var texts = content.GetComponentsInChildren<Text>(true);
            var line1 = texts.First(t => t.name == "Line1");
            var line2 = texts.First(t => t.name == "Line2");
            Assert.AreEqual("구자욱'24 (RF)", line1.text, "1번째 줄 = 선수명'연도 (포지션)");
            Assert.AreEqual(FontStyle.Bold, line1.fontStyle);
            Assert.AreEqual("GG · 3각 · OVR 98", line2.text);
            Assert.IsFalse(line1.text.Contains("\n"), "한 줄씩 별도 Text - 줄바꿈 잘림으로 이름이 사라지지 않는다");
            Assert.GreaterOrEqual(line1.rectTransform.anchorMin.y, line2.rectTransform.anchorMax.y, "1줄이 2줄 위");
        }

        [Test]
        public void RelayLineup_ColumnsSeparated_BadgePosNameAvgOvr()
        {
            float[][] cols =
            {
                new[] { CompyaMatchView.LineupBadgeX0, CompyaMatchView.LineupBadgeX1 },
                new[] { CompyaMatchView.LineupPosX0, CompyaMatchView.LineupPosX1 },
                new[] { CompyaMatchView.LineupNameX0, CompyaMatchView.LineupNameX1 },
                new[] { CompyaMatchView.LineupAvgX0, CompyaMatchView.LineupAvgX1 },
                new[] { CompyaMatchView.LineupOvrX0, CompyaMatchView.LineupOvrX1 },
            };
            for (int i = 1; i < cols.Length; i++)
                Assert.GreaterOrEqual(cols[i][0] - cols[i - 1][1], 6f, $"{i}번째 열 간격 6px 이상(텍스트 붙음 제거)");
            Assert.GreaterOrEqual(CompyaMatchView.LineupNameX1 - CompyaMatchView.LineupNameX0, 200f, "이름'연도 칸 충분한 폭");

            OpenScene();
            var view = UnityEngine.Object.FindAnyObjectByType<CompyaMatchView>(FindObjectsInactive.Include);
            Assert.IsNotNull(view);
            view.Build();
            var rects = view.GetComponentsInChildren<RectTransform>(true);
            RectTransform R(string n) => rects.First(r => r.name == n);
            var order = new[] { "LeftRow1BadgeText", "LeftRow1Pos", "LeftRow1Name", "LeftRow1Avg", "LeftRow1Ovr" }.Select(R).ToList();
            for (int i = 1; i < order.Count; i++)
                Assert.Greater(order[i].anchorMin.x, order[i - 1].anchorMax.x, $"{order[i - 1].name} | {order[i].name} 겹침 없음");
            var name = R("LeftRow1Name").GetComponent<Text>();
            Assert.AreEqual(VerticalWrapMode.Truncate, name.verticalOverflow, "넘치면 잘라 옆 칸 침범 없음");
            Assert.IsTrue(name.resizeTextForBestFit);
        }

        // ================================================================== D. 경기 진행 방식

        [Test]
        public void QuickCount_ClampedToRemainingRegularSeasonGames()
        {
            Assert.AreEqual(5, MatchModeRules.ClampQuickCount(5, 144));
            Assert.AreEqual(3, MatchModeRules.ClampQuickCount(10, 3), "남은 경기 수 초과 금지");
            Assert.AreEqual(1, MatchModeRules.ClampQuickCount(0, 10));
            Assert.AreEqual(0, MatchModeRules.ClampQuickCount(5, 0));
            Assert.AreEqual(4, MatchModeRules.RemainingRegularGames(140, 144, true, true));
            Assert.AreEqual(0, MatchModeRules.RemainingRegularGames(144, 144, true, false));
            Assert.AreEqual(1, MatchModeRules.RemainingRegularGames(144, 144, false, true), "포스트시즌은 1경기씩");
            CollectionAssert.AreEqual(new[] { 1, 3, 5, 10, 30, MatchModeRules.SeasonAll }, MatchModeRules.QuickCountPresets); // [TASK-KBO-190] 30경기 · 시즌 완주 추가
        }

        [Test]
        public void Highlight_IsOldFullPlayFlow_FullPlay_CommandsEveryUserPlateAppearance()
        {
            var H = MatchModeRules.Mode.Highlight;
            var F = MatchModeRules.Mode.Full;
            // 하이라이트 = 구 풀 플레이: 득점권 승부처에서만, 최대 12회(공격·수비 모두)
            Assert.IsTrue(MatchModeRules.ShouldIntervene(H, true, true, true, true, 0));
            Assert.IsTrue(MatchModeRules.ShouldIntervene(H, true, false, true, true, 3), "수비 위기도 개입");
            Assert.IsFalse(MatchModeRules.ShouldIntervene(H, true, true, false, true, 0), "주자 1루만 = 승부처 아님");
            Assert.IsFalse(MatchModeRules.ShouldIntervene(H, true, true, true, true, MatchModeRules.HighlightMaxInterventions));
            // 풀 플레이 = 우리 팀 매 타석 + 주자 있는 수비 타석, 횟수 제한 없음
            Assert.IsTrue(MatchModeRules.ShouldIntervene(F, true, true, false, false, 0), "주자 없어도 우리 타석은 매번");
            Assert.IsTrue(MatchModeRules.ShouldIntervene(F, true, true, false, false, 99));
            Assert.IsTrue(MatchModeRules.ShouldIntervene(F, true, false, false, true, 40), "주자 출루 수비 상황");
            Assert.IsFalse(MatchModeRules.ShouldIntervene(F, true, false, false, false, 0), "주자 없는 수비 타석은 자동");
            Assert.IsFalse(MatchModeRules.ShouldIntervene(MatchModeRules.Mode.Quick, true, true, true, true, 0), "빠른 진행은 개입 없음");
            Assert.IsFalse(MatchModeRules.ShouldIntervene(F, false, true, true, true, 0), "우리 팀 경기가 아니면 없음");
            Assert.AreEqual((int)CompyaMatchView.PlayMode.Highlight, (int)H);
            Assert.AreEqual((int)CompyaMatchView.PlayMode.Full, (int)F);
        }

        [Test]
        public void QuickSeriesRunner_StopsEarly_WhenSeasonEnds_SummaryLabel()
        {
            int started = 0, gold = 0;
            bool open = true;
            QuickSeriesSummary finished = null;
            QuickSeriesRunner runner = null;
            runner = new QuickSeriesRunner(() => started++, () => open, null, () => started * 100, () => gold);
            runner.Finished += s => finished = s;
            Assert.IsTrue(runner.Begin(5, 144));
            var win = new MatchResult { WinnerTeamName = Team.Samsung.ToString() };
            var loss = new MatchResult { WinnerTeamName = Team.KIA.ToString() };
            runner.HandleMatchCompleted(win, Team.Samsung);
            runner.HandleMatchCompleted(win, Team.Samsung);
            open = false; // 정규시즌 종료
            runner.HandleMatchCompleted(loss, Team.Samsung);
            Assert.IsNotNull(finished, "시즌이 끝나면 조기 종료");
            Assert.AreEqual(3, started);
            Assert.AreEqual(3, finished.Played);
            Assert.AreEqual(3, finished.Requested);
            Assert.AreEqual("3경기 2승 1패 · 획득 자금 +300 영입권", finished.Label);
            Assert.IsFalse(runner.HandleMatchCompleted(win, Team.Samsung), "연속 진행이 끝나면 일반 경기 처리");
            Assert.IsFalse(new QuickSeriesRunner(null, null, null, null, null).Begin(5, 0), "남은 경기 없음");
        }

        [Test]
        public void QuickSeries_FiveGames_RealPlayBallPath_RotationOtherParksSeasonStatsRewards()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            sceneOpened = true;
            var go = new GameObject("Task188Managers");
            created.Add(go);
            var gm = go.AddComponent<GameManager>();
            SetInstance(typeof(GameManager), gm);
            gm.FavoriteTeam = Team.Samsung;
            foreach (var p in Task183Report.ProceduralTeam(Team.Samsung, 70, "Q188"))
            {
                gm.AddPlayerToInventory(p);
                Assert.IsTrue(gm.AddPlayerToRoster(p));
            }
            var league = go.AddComponent<LeagueManager>();
            SetInstance(typeof(LeagueManager), league);
            league.InitializeLeague(Team.Samsung, LeagueTier.Amateur);
            var stats = go.AddComponent<SeasonStatManager>();
            SetInstance(typeof(SeasonStatManager), stats);
            var reward = go.AddComponent<MatchRewardManager>();
            var playBall = go.AddComponent<PlayBallController>();

            var rewards = new List<MatchRewardResult>();
            var userStarters = new List<Player>();
            var otherParks = new List<int>();
            playBall.OnMatchCompleted += stats.RecordMatchCompleted;
            playBall.OnMatchCompleted += r => rewards.Add(reward.GrantRewardForMatch(r));
            var runner = new QuickSeriesRunner(
                () => { userStarters.Add(league.GetNextStartingPitcher(Team.Samsung)); playBall.StartMatch(); },
                () => league.PeekNextFixture() != null,
                null, // EditMode: 즉시 다음 경기(실전 뷰는 한 프레임 뒤)
                () => gm.LiveNormalTicket,
                () => gm.GameGold);
            playBall.OnMatchCompleted += r => { otherParks.Add(league.LastRoundOtherFixtures.Count); runner.HandleMatchCompleted(r, Team.Samsung); };
            int ticketsBefore = gm.LiveNormalTicket;

            Assert.IsTrue(runner.Begin(5, MatchModeRules.RemainingRegularGames(league.PlayedGameCount, LeagueManager.TotalUserGames, true, true)));
            var summary = runner.Summary;

            Assert.AreEqual(5, summary.Played, "5경기 연속 자동 진행");
            Assert.AreEqual(5, league.PlayedGameCount);
            Assert.AreEqual(5, userStarters.Distinct().Count(), "① 1~5선발 순차 로테이션(경기마다 다른 선발)");
            Assert.IsTrue(userStarters.All(p => p != null && p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.StartingPitcher));
            CollectionAssert.AreEqual(new[] { 4, 4, 4, 4, 4 }, otherParks, "② 매 경기 타 구장 4경기 동시 진행");
            Assert.IsTrue(league.GetStandings().All(t => t.Wins + t.Draws + t.Losses == 5), "10개 구단 모두 5경기 소화");

            var user = league.GetStandings().First(t => t.Team == Team.Samsung);
            Assert.AreEqual((user.Wins, user.Draws, user.Losses), (summary.Wins, summary.Draws, summary.Losses), "전적 요약 = 순위표");
            int decisions = userStarters.Sum(p => (stats.GetPitcherStats(p)?.Wins ?? 0) + (stats.GetPitcherStats(p)?.Losses ?? 0));
            Assert.AreEqual(summary.Wins + summary.Losses, decisions, "③ 경기마다 선발 승·패가 시즌 기록에 합산");
            int userPa = gm.Roster.Where(p => !p.Template.IsPitcher).Sum(p => stats.GetBatterStats(p)?.PlateAppearances ?? 0);
            Assert.GreaterOrEqual(userPa, 5 * 18, "③ 5경기 타석이 시즌 타격 기록에 합산");
            Assert.IsTrue(userStarters.All(p => (stats.GetPitcherStats(p)?.OutsRecorded ?? 0) > 0), "③ 선발 이닝 기록");

            Assert.AreEqual(5, rewards.Count(r => r != null), "④ 경기당 보상 5회");
            Assert.AreEqual(rewards.Sum(r => r.LiveNormalTicketGained), gm.LiveNormalTicket - ticketsBefore);
            Assert.AreEqual(gm.LiveNormalTicket - ticketsBefore, summary.TicketsGained, "④ 획득 자금 누적 = 요약");
            StringAssert.StartsWith("5경기 ", summary.Label);
            Debug.Log($"[Task188Tests] 빠른 진행 5경기 실측: {summary.Label} | 선발 {string.Join(", ", userStarters.Select(p => p.Template.PlayerName))}");
        }
    }
}
