using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.EditorTools;
using KBOManager.Models;
using KBOManager.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-KBO-192] 너무 작아진 일반 UI 글씨 복원(본문 19~21 · 보조 16~18 · 탭 20~22 · 타이틀 26~30, Best Fit 최소 15, Normal 유지)과
    /// 진짜 비대한 텍스트 핀셋 축소 - 경기 방식 선택(SELECT TYPE)의 "144경기 / 남은 144" 폭주 · 안내문 줄겹침 · 프리셋 버튼 · 모드 카드 제목,
    /// 선수 카드(PlayerCardUI) 내부 OVR · SD · 배지 · 선수명 약 22% 축소.
    /// </summary>
    public class Task192Tests
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

        private GameObject NewRoot(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            created.Add(go);
            return go;
        }

        private static Text MakeText(Transform parent, string name, int size)
        {
            var t = CompyaUiKit.Fill(parent, name).gameObject.AddComponent<Text>();
            t.fontSize = size;
            return t;
        }

        private void OpenScene()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
        }

        // ================================================================== A. 일반 UI 크기 복원

        [Test]
        public void Tier_RestoresReadableSizes_AutoMin15()
        {
            Assert.AreEqual(15, TextTidy.AutoMin, "Best Fit 최소 15");
            Assert.AreEqual(12, TextTidy.Tier(12, false), "12pt 이하 초소형 칸은 그대로");
            Assert.AreEqual(16, TextTidy.Tier(14, false), "보조 16~18");
            Assert.AreEqual(20, TextTidy.Tier(20, false), "본문 19~21");
            Assert.That(TextTidy.Tier(41, false), Is.InRange(26, 30), "타이틀 26~30");
            Assert.AreEqual(TextTidy.MaxSize, TextTidy.Tier(94, false), "대형 수치 상한 42");
            Assert.That(TextTidy.Tier(36, true), Is.InRange(20, 24), "탭 · 일반 버튼");
            Assert.That(TextTidy.Tier(63, true), Is.InRange(26, 30), "대형 실행 버튼 26~30");
            for (int s = 8; s < 120; s++)
            {
                Assert.LessOrEqual(TextTidy.Tier(s, false), TextTidy.Tier(s + 1, false), $"단조 증가(일반 {s})");
                Assert.LessOrEqual(TextTidy.Tier(s, true), TextTidy.Tier(s + 1, true), $"단조 증가(버튼 {s})");
                Assert.LessOrEqual(TextTidy.Tier(s, false), s + 2, "작은 글씨도 과하게 키우지 않는다");
            }
        }

        [Test]
        public void Migrate191_RaisesShrunkText_4to7pt_KeepsLargeNumbers()
        {
            Assert.AreEqual(20, TextTidy.Migrate191(16, false), "본문 16 → 20");
            Assert.AreEqual(21, TextTidy.Migrate191(17, false), "본문 17 → 21");
            Assert.AreEqual(16, TextTidy.Migrate191(14, false), "보조 14 → 16");
            Assert.AreEqual(17, TextTidy.Migrate191(15, false), "보조 15 → 17");
            Assert.AreEqual(30, TextTidy.Migrate191(26, false), "타이틀 26 → 30");
            Assert.AreEqual(40, TextTidy.Migrate191(40, false), "대형 수치 유지");
            Assert.AreEqual(18, TextTidy.Migrate191(15, true), "작은 버튼 15 → 18");
            Assert.AreEqual(26, TextTidy.Migrate191(21, true), "큰 버튼 21 → 26");
            for (int s = 8; s <= 42; s++)
            {
                Assert.LessOrEqual(TextTidy.Migrate191(s, false), TextTidy.Migrate191(s + 1, false), $"단조 증가 {s}");
                Assert.GreaterOrEqual(TextTidy.Migrate191(s, false), s, "줄이지 않는다");
                Assert.LessOrEqual(TextTidy.Migrate191(s, false), TextTidy.MaxSize);
            }
        }

        [Test]
        public void Normalize_MigratesSavedTask191Text_Once_RaisesAutoMin_KeepsCardSize()
        {
            var root = NewRoot("Migrate192");
            var body = MakeText(root.transform, "Body", 16);
            body.resizeTextForBestFit = true;
            body.resizeTextMaxSize = 16;
            body.resizeTextMinSize = 11;
            var buttonRect = CompyaUiKit.Fill(root.transform, "Btn");
            buttonRect.gameObject.AddComponent<Button>();
            var button = MakeText(buttonRect, "Text", 15);
            var cardRoot = CompyaUiKit.Fill(root.transform, "Card");
            cardRoot.gameObject.AddComponent<PlayerCardUI>();
            var cardText = MakeText(cardRoot, "CardOvr", 26);

            // TASK-191 상태로 저장된 텍스트 재현(version 0 · appliedSize = 현재 크기)
            var versionField = typeof(TextTidy).GetField("version", BindingFlags.Instance | BindingFlags.NonPublic);
            var appliedField = typeof(TextTidy).GetField("appliedSize", BindingFlags.Instance | BindingFlags.NonPublic);
            var lockedField = typeof(TextTidy).GetField("sizeLocked", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (var t in new[] { body, button, cardText })
            {
                var tidy = t.gameObject.AddComponent<TextTidy>();
                appliedField.SetValue(tidy, TextTidy.EffectiveSize(t));
                versionField.SetValue(tidy, 0);
                lockedField.SetValue(tidy, t == cardText);
            }

            Assert.AreEqual(3, ReadableFontPass.Apply(root.transform));
            Assert.AreEqual(20, body.fontSize, "본문 16 → 20");
            Assert.AreEqual(20, body.resizeTextMaxSize);
            Assert.AreEqual(TextTidy.AutoMin, body.resizeTextMinSize, "Best Fit 최소 11 → 15");
            Assert.AreEqual(18, button.fontSize, "버튼 15 → 18");
            Assert.AreEqual(26, cardText.fontSize, "카드 글씨는 PlayerCardUI가 정한다(정리기는 크기 유지)");
            Assert.IsTrue(new[] { body, button, cardText }.All(t => t.GetComponent<TextTidy>().Version == TextTidy.CurrentVersion));
            Assert.AreEqual(0, ReadableFontPass.Apply(root.transform), "한 번만 되올린다(멱등)");
            Assert.AreEqual(20, body.fontSize);

            // Exact: 최소 = min(크기, 15)
            Assert.AreEqual(15, TextTidy.Exact(MakeText(root.transform, "E20", 20), 20).resizeTextMinSize);
            Assert.AreEqual(14, TextTidy.Exact(MakeText(root.transform, "E14", 14), 14).resizeTextMinSize);
        }

        [Test]
        public void Builders_UseRestoredSizes()
        {
            // 성장 센터
            Assert.AreEqual(28, GrowthCenterView.TitlePt);
            Assert.That(GrowthCenterView.SectionPt, Is.InRange(24, 26));
            Assert.That(GrowthCenterView.TabPt, Is.InRange(20, 22), "강화 / 각성 / 초월 / 한계 돌파 / 훈련·특훈 탭");
            Assert.That(GrowthCenterView.ActionPt, Is.InRange(26, 30), "실행 버튼");
            Assert.That(GrowthCenterView.StatPt, Is.InRange(19, 21), "좌측 세부 스탯");
            Assert.That(GrowthCenterView.SkillSlotTitlePt, Is.InRange(19, 21), "보유 스킬 슬롯 제목");
            Assert.That(GrowthCenterView.TargetLine1Pt, Is.InRange(20, 22), "대상 선수 1줄");
            foreach (var aux in new[] { GrowthCenterView.LadderPt, GrowthCenterView.SkillSlotDescPt, GrowthCenterView.SkillButtonPt, GrowthCenterView.TargetLine2Pt })
                Assert.That(aux, Is.InRange(16, 18), "보조 16~18");
            // 로비
            Assert.That(SetupTask181.MenuTileTitlePt, Is.InRange(24, 26), "메뉴 타일 제목");
            Assert.AreEqual(18, SetupTask181.MenuTileSubPt, "메뉴 타일 부제");
            Assert.That(SetupTask181.NavLabelPt, Is.InRange(20, 22), "하단 5탭");
            Assert.That(SetupTask181.StandingRowPt, Is.InRange(19, 21), "KBO 순위표 행");
            Assert.That(SetupTask181.CurrencyValuePt, Is.InRange(19, 21), "재화 숫자");
            Assert.That(SetupTask181.CurrencyLabelPt, Is.InRange(16, 18), "재화 라벨");
            Assert.That(SetupTask181.PlayBallPt, Is.InRange(26, 30), "플레이 볼");
        }

        [Test]
        public void GrowthCenter_Built_TextsReadable_MinAutoSize15()
        {
            var host = NewRoot("GrowthHost192");
            var view = host.AddComponent<GrowthCenterView>();
            view.Configure(null, null, null);
            view.Build();
            var root = host.transform.Find(GrowthCenterView.RootName);
            Text T(string path) => root.Find(path).GetComponentInChildren<Text>(true);
            Assert.AreEqual(GrowthCenterView.TabPt, T("Tab_" + GrowthCenterRules.Tabs[0]).fontSize);
            Assert.AreEqual(GrowthCenterView.StatPt, T("Stats").fontSize);
            Assert.AreEqual(GrowthCenterView.ActionPt, T("Action").fontSize);
            Assert.AreEqual(GrowthCenterView.LadderPt, T("Ladder0/Text").fontSize);
            var texts = root.GetComponentsInChildren<Text>(true);
            Assert.IsTrue(texts.All(t => t.fontStyle == FontStyle.Normal), "Normal 유지");
            var small = texts.Where(t => t.resizeTextForBestFit && t.resizeTextMinSize < Mathf.Min(TextTidy.EffectiveSize(t), TextTidy.AutoMin)).Select(t => t.name).ToList();
            Assert.IsEmpty(small, "Best Fit 최소 15: " + string.Join(", ", small));
        }

        // ================================================================== B. 경기 방식 선택(SELECT TYPE)

        [Test]
        public void SelectType_QuickCountOneLine_DescSeparated_PresetButtons_CardTitles()
        {
            Assert.AreEqual("144경기 (잔여 144)", CompyaMatchView.QuickCountLabel(144, 144));
            StringAssert.DoesNotContain("\n", CompyaMatchView.QuickCountLabel(30, 87));

            OpenScene();
            var match = Object.FindAnyObjectByType<CompyaMatchView>(FindObjectsInactive.Include);
            Assert.IsNotNull(match);
            var p = match.GetComponentsInChildren<Transform>(true).First(t => t.name == "TypeSelect");
            float Height(RectTransform r) => (r.anchorMax.y - r.anchorMin.y) * 1920f; // 화면 = 1080×1920 캔버스 전체
            Text T(string path) => p.Find(path).GetComponent<Text>();

            // 1) "N경기 (잔여 M)" 18pt 한 줄 - 줄바꿈 없음 · 칸 폭 200px 이상
            var count = T("QuickCount/StepRow/Count");
            Assert.AreEqual(18, count.fontSize);
            Assert.AreEqual(HorizontalWrapMode.Overflow, count.horizontalOverflow, "한 줄 고정");
            Assert.IsFalse(count.resizeTextForBestFit);
            var quick = (RectTransform)p.Find("QuickCount");
            var countRect = count.rectTransform;
            float countWidth = (quick.anchorMax.x - quick.anchorMin.x) * (countRect.anchorMax.x - countRect.anchorMin.x) * 1080f;
            Assert.GreaterOrEqual(countWidth, 200f, $"경기 수 칸 폭 {countWidth:F0}px");

            // 2) 하늘색 안내문: 64px 이상 · 18pt · 줄간격 1.2 · 정보/연속 진행/볼 칸과 겹치지 않음
            var desc = T("Desc");
            Assert.AreEqual(18, desc.fontSize);
            Assert.AreEqual(1.2f, desc.lineSpacing, 0.001f);
            Assert.GreaterOrEqual(Height(desc.rectTransform), 64f);
            Assert.AreEqual(FontStyle.Normal, desc.fontStyle);
            var rows = new[] { "Desc", "Info", "QuickCount", "Divider", "BallPill" };
            for (int i = 0; i < rows.Length; i++)
                for (int j = i + 1; j < rows.Length; j++)
                    Assert.IsFalse(Task191Report.Overlap(p.Find(rows[i]), p.Find(rows[j])), $"{rows[i]} ↔ {rows[j]}");

            // 3) 프리셋 6버튼: 높이 48~52px · 17~18pt
            var presetRow = (RectTransform)p.Find("QuickCount/PresetRow");
            var presets = presetRow.GetComponentsInChildren<Button>(true);
            Assert.AreEqual(MatchModeRules.QuickCountPresets.Length, presets.Length);
            foreach (var b in presets)
            {
                var r = (RectTransform)b.transform;
                float h = Height(quick) * (presetRow.anchorMax.y - presetRow.anchorMin.y) * (r.anchorMax.y - r.anchorMin.y);
                Assert.That(h, Is.InRange(48f, 52f), $"{b.name} 높이 {h:F1}px");
                Assert.That(b.GetComponentInChildren<Text>(true).fontSize, Is.InRange(17, 18), b.name);
            }
            for (int i = 1; i < presets.Length; i++)
                Assert.IsFalse(Task191Report.Overlap(presets[i - 1].transform, presets[i].transform), "프리셋 버튼 겹침");

            // 4) 모드 카드 제목 22pt #0F172A · 영문 14pt #475569
            for (int i = 0; i < 3; i++)
            {
                var name = T($"Card{i}/Name");
                var eng = T($"Card{i}/Eng");
                Assert.AreEqual(22, name.fontSize);
                Assert.AreEqual("0F172A", ColorUtility.ToHtmlStringRGB(name.color));
                Assert.AreEqual(14, eng.fontSize);
                Assert.AreEqual("475569", ColorUtility.ToHtmlStringRGB(eng.color));
                Assert.AreEqual(FontStyle.Normal, name.fontStyle);
            }
        }

        // ================================================================== C. 선수 카드

        [Test]
        public void PlayerCard_InnerTextShrunk20to25Percent_Normal_CornersClearOfLogo()
        {
            int[] before = { 34, 16, 18, 20 };
            int[] after = { PlayerCardUI.OvrMaxPt, PlayerCardUI.SetDeckMaxPt, PlayerCardUI.BadgeMaxPt, PlayerCardUI.NameMaxPt };
            for (int i = 0; i < before.Length; i++)
                Assert.That(after[i] / (float)before[i], Is.InRange(0.74f, 0.81f), $"약 20~25% 축소({before[i]} → {after[i]})");

            OpenScene();
            var roster = Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(roster?.CardPrefab);
            var card = Object.Instantiate(roster.CardPrefab);
            created.Add(card.gameObject);

            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = "SAM_GG_192"; t.RealPlayerId = t.TemplateId; t.PlayerName = "구자욱";
            t.Team = Team.Samsung; t.Grade = Grade.GOLDEN_GLOVE; t.SeasonYear = 2024;
            t.BatterPosition = BatterPosition.RightField; t.BatterStats = StatProfiles.SpreadBatter(95, t.BatterPosition, 1);
            var player = new Player("p192", t) { ReinforceLevel = 10, AwakenLevel = 10 };
            card.Setup(player);
            Assert.AreEqual("초월", card.GrowthBadgeText);

            Text Txt(string path) => card.transform.Find(path).GetComponent<Text>();
            Assert.AreEqual(PlayerCardUI.OvrMaxPt, Txt("OvrText").resizeTextMaxSize);
            Assert.AreEqual(PlayerCardUI.SetDeckMaxPt, Txt("SetDeckScoreText").resizeTextMaxSize);
            Assert.AreEqual(PlayerCardUI.BadgeMaxPt, Txt("GrowthBadge188/Text").resizeTextMaxSize);
            Assert.AreEqual(PlayerCardUI.NameMaxPt, Txt("NameText").resizeTextMaxSize);
            foreach (var n in new[] { "OvrText", "SetDeckScoreText", "GrowthBadge188/Text", "NameText" })
                Assert.AreEqual(FontStyle.Normal, Txt(n).fontStyle, n + " Normal");

            RectTransform R(string n) => (RectTransform)card.transform.Find(n);
            Assert.LessOrEqual(R("OvrText").anchorMax.x, R("TeamLogo188").anchorMin.x, "OVR ↔ 로고 사이 여백");
            Assert.GreaterOrEqual(R("SetDeckScoreText").anchorMin.x, R("TeamLogo188").anchorMax.x, "SD ↔ 로고 사이 여백");
            Assert.Less(R("SetDeckScoreText").anchorMax.x, 1f, "SD 오른쪽 여백");
            Assert.LessOrEqual(R("GrowthBadge188").anchorMax.x, R("NameText").anchorMin.x, "배지 ↔ 이름 겹침 없음");
            Assert.Greater(R("GrowthBadge188").anchorMin.y, 0f, "배지 위아래 여백");
            Assert.Less(R("GrowthBadge188").anchorMax.y, PlayerCardUI.NamePlateTop);
            Assert.Greater(R("NameText").anchorMin.y, 0f, "이름 위아래 여백");
            Assert.Less(R("NameText").anchorMax.y, PlayerCardUI.NamePlateTop);
        }

        // ================================================================== D. 씬

        [Test]
        public void Scene_MigratedToTask192_NoBold_NormalTextMin15()
        {
            OpenScene();
            var texts = SetupTask191.SceneTexts().ToList();
            Assert.IsEmpty(texts.Where(x => x.fontStyle == FontStyle.Bold || x.fontStyle == FontStyle.BoldAndItalic).Select(x => x.name), "Bold 0개");
            var old = texts.Where(x => x.TryGetComponent<TextTidy>(out var tidy) && tidy.Version < TextTidy.CurrentVersion).Select(x => x.name).ToList();
            Assert.IsEmpty(old, "TASK-191 크기로 남은 텍스트: " + string.Join(", ", old.Take(10)));
            Assert.AreEqual(0, SetupTask191.TidyScene(out _), "저장된 씬은 이미 정리 완료(멱등)");

            var home = Object.FindAnyObjectByType<LobbyHome181>(FindObjectsInactive.Include);
            Text L(string n) => home.transform.Find(n).GetComponent<Text>();
            Assert.AreEqual(SetupTask181.MenuTileTitlePt, L("MenuTile0_Title").fontSize);
            Assert.AreEqual(SetupTask181.MenuTileSubPt, L("MenuTile0_Sub").fontSize);
            Assert.AreEqual(SetupTask181.StandingRowPt, L("StandingTeam1").fontSize);
            Assert.AreEqual(SetupTask181.CurrencyValuePt, L("Currency0_Value").fontSize);
        }
    }
}
