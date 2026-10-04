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
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-KBO-191] 전 화면 한글 텍스트(Bold 해제 · 계층 크기 · 자간 · Best Fit 최소 11), 성장 센터(스킬 헤더 · 슬롯 · 3버튼 · 각성 사다리 · 대상 선수)와
    /// 로비(재화 바 · NEXT MATCH 한글 진행도 · 대표 스타 칸 분리) 겹침 제거, AI 불펜 운용(롱릴리프 독식 · 비정상 탈삼진) 수정.
    /// </summary>
    public class Task191Tests
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

        private GameObject NewRoot(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            created.Add(go);
            return go;
        }

        private Text MakeText(Transform parent, string name, int size, FontStyle style = FontStyle.Normal)
        {
            var t = CompyaUiKit.Fill(parent, name).gameObject.AddComponent<Text>();
            t.fontSize = size;
            t.fontStyle = style;
            return t;
        }

        // ================================================================== A. 텍스트 정리(TextTidy)

        [Test]
        public void Tier_ShrinksOversizedText_KeepsSmall_CapsAt42_ButtonsAt21_Monotonic()
        {
            Assert.AreEqual(14, TextTidy.Tier(14, false), "보조 글씨는 그대로");
            Assert.AreEqual(13, TextTidy.Tier(13, true));
            Assert.AreEqual(16, TextTidy.Tier(20, false));
            Assert.AreEqual(17, TextTidy.Tier(22, false), "본문 16~18");
            Assert.AreEqual(26, TextTidy.Tier(44, false), "타이틀 24~26");
            Assert.AreEqual(TextTidy.MaxSize, TextTidy.Tier(94, false), "대형 점수 상한 42");
            Assert.AreEqual(21, TextTidy.Tier(41, true), "큰 버튼 19~21");
            Assert.AreEqual(15, TextTidy.Tier(20, true), "작은 버튼 13~15");
            for (int s = 8; s < 120; s++)
            {
                Assert.LessOrEqual(TextTidy.Tier(s, false), TextTidy.Tier(s + 1, false), $"단조 증가(일반 {s})");
                Assert.LessOrEqual(TextTidy.Tier(s, true), TextTidy.Tier(s + 1, true), $"단조 증가(버튼 {s})");
                Assert.LessOrEqual(TextTidy.Tier(s, false), Math.Max(s, 16), "확대하지 않는다");
            }
        }

        [Test]
        public void Normalize_RemovesBold_RestoresTask186Raise_SetsAutoMin11_AddsSpacing_Idempotent()
        {
            var root = NewRoot("Tidy191");
            var raisedBody = MakeText(root.transform, "RaisedBody", 22);
            var raisedBold = MakeText(root.transform, "RaisedBold", 26, FontStyle.Bold);
            foreach (var t in new[] { raisedBody, raisedBold })
            {
                t.resizeTextForBestFit = true;
                t.resizeTextMaxSize = t.fontSize;
                t.resizeTextMinSize = 18;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
            }
            var buttonGo = CompyaUiKit.Fill(root.transform, "Btn");
            buttonGo.gameObject.AddComponent<Button>();
            var raisedButton = MakeText(buttonGo, "Text", 26, FontStyle.Bold);
            raisedButton.resizeTextForBestFit = true;
            raisedButton.resizeTextMaxSize = 26;
            raisedButton.resizeTextMinSize = 14;
            raisedButton.horizontalOverflow = HorizontalWrapMode.Wrap;
            var italic = MakeText(root.transform, "Italic", 40, FontStyle.BoldAndItalic);

            Assert.AreEqual(4, ReadableFontPass.Apply(root.transform));
            Assert.AreEqual((16, 17, 15), (raisedBody.fontSize, raisedBold.fontSize, raisedButton.fontSize), "TASK-186 확대분 복원(본문 16 · 강조 17 · 버튼 15)");
            Assert.AreEqual(FontStyle.Normal, raisedBold.fontStyle);
            Assert.AreEqual(FontStyle.Italic, italic.fontStyle, "BoldAndItalic → Italic");
            Assert.AreEqual(TextTidy.Tier(40, false), italic.fontSize);
            Assert.AreEqual(TextTidy.AutoMin, raisedBody.resizeTextMinSize, "Best Fit 최소 11");
            Assert.IsTrue(new[] { raisedBody, raisedBold, raisedButton, italic }.All(t => t.GetComponent<TextTidy>() != null && Mathf.Approximately(t.GetComponent<TextTidy>().CharacterSpacing, 2f)), "자간 2.0");
            Assert.AreEqual(0, ReadableFontPass.Apply(root.transform), "두 번째 실행은 변화 없음");

            // 직접 지정(Exact) 크기는 정리 패스가 다시 줄이지 않는다 / 코드가 크기를 바꾸면 그 값을 기준으로 다시 정리
            TextTidy.Exact(italic, 40);
            Assert.AreEqual(0, ReadableFontPass.Apply(root.transform));
            Assert.AreEqual(40, italic.fontSize);
            raisedBody.fontSize = raisedBody.resizeTextMaxSize = 30;
            Assert.AreEqual(1, ReadableFontPass.Apply(root.transform));
            Assert.AreEqual(TextTidy.Tier(30, false), raisedBody.fontSize);
        }

        [Test]
        public void Normalize_CardTextKeepsSize_ClampsThickOutline()
        {
            var root = NewRoot("Card191");
            root.AddComponent<PlayerCardUI>();
            var cardText = MakeText(root.transform, "CardOvr", 60, FontStyle.Bold);
            var outline = cardText.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(4f, -4f);
            ReadableFontPass.Apply(root.transform);
            Assert.AreEqual(60, cardText.fontSize, "카드 디자인 px 고정 - 크기 유지");
            Assert.AreEqual(FontStyle.Normal, cardText.fontStyle, "카드 안도 Bold 해제");
            Assert.AreEqual(new Vector2(TextTidy.MaxOutline, -TextTidy.MaxOutline), outline.effectDistance, "두꺼운 외곽선 축소");
        }

        [Test]
        public void LetterSpacing_ShiftsGlyphsPerLine_RespectsAlignment()
        {
            var root = NewRoot("Spacing191");
            var text = MakeText(root.transform, "T", 50);
            var tidy = text.gameObject.AddComponent<TextTidy>();
            float[] Run(TextAnchor anchor)
            {
                text.alignment = anchor;
                var vh = new VertexHelper();
                void Quad(float x, float top)
                {
                    var v = UIVertex.simpleVert;
                    var quad = new UIVertex[4];
                    v.position = new Vector3(x, top); quad[0] = v;
                    v.position = new Vector3(x + 8, top); quad[1] = v;
                    v.position = new Vector3(x + 8, top - 10); quad[2] = v;
                    v.position = new Vector3(x, top - 10); quad[3] = v;
                    vh.AddUIVertexQuad(quad);
                }
                Quad(0, 0); Quad(10, 0); Quad(20, 0); // 1줄 3글자
                Quad(0, -20); Quad(10, -20);         // 2줄 2글자
                tidy.ModifyMesh(vh);
                var xs = new float[5];
                var vert = new UIVertex();
                for (int q = 0; q < 5; q++) { vh.PopulateUIVertex(ref vert, q * 4); xs[q] = vert.position.x; }
                vh.Dispose();
                return xs;
            }
            // 자간 2.0(em/100) × 50pt = 1.0 단위
            void Expect(float[] expected, float[] actual, string message)
            {
                for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], actual[i], 0.01f, $"{message} [{i}]");
            }
            Expect(new[] { 0f, 11f, 22f, 0f, 11f }, Run(TextAnchor.UpperLeft), "줄마다 처음부터 1.0씩");
            Expect(new[] { -1f, 10f, 21f, -0.5f, 10.5f }, Run(TextAnchor.MiddleCenter), "가운데 정렬은 좌우 대칭");
        }

        // ================================================================== B. 성장 센터

        private GrowthCenterView BuildGrowthCenter(out Transform root)
        {
            var host = NewRoot("GrowthHost191");
            var view = host.AddComponent<GrowthCenterView>();
            view.Configure(null, null, null);
            view.Build();
            root = host.transform.Find(GrowthCenterView.RootName);
            return view;
        }

        [Test]
        public void GrowthCenter_SkillHeaderSlotsButtons_LadderTargets_SizedNormal_NoOverlap()
        {
            var view = BuildGrowthCenter(out var root);
            Assert.IsNotNull(root);
            var texts = root.GetComponentsInChildren<Text>(true);
            Assert.IsTrue(texts.All(t => t.fontStyle == FontStyle.Normal), "성장 센터 전 텍스트 Normal");
            Assert.IsTrue(texts.All(t => TextTidy.EffectiveSize(t) <= TextTidy.MaxSize));
            Assert.AreEqual(GrowthCenterView.TitlePt, root.Find("Title").GetComponent<Text>().fontSize);
            Assert.AreEqual(40, root.Find("Ovr").GetComponent<Text>().fontSize, "메인 OVR 32~42");
            for (int i = 0; i <= 10; i++)
            {
                var ladder = root.Find($"Ladder{i}/Text").GetComponent<Text>();
                Assert.That(ladder.fontSize, Is.InRange(13, 14), "각성 사다리 13~14pt");
            }
            Assert.AreEqual("명함", root.Find("Ladder0/Text").GetComponent<Text>().text);
            Assert.AreEqual("초월", root.Find("Ladder10/Text").GetComponent<Text>().text);

            // 훈련·특훈 헤더: 짧은 문구 · 15pt · 한 줄 칸이 목록과 겹치지 않음
            Assert.AreEqual("보유 스킬 (3슬롯) · 변경권 4 / 고급 2 / 특훈권 0", GrowthCenterView.SkillHeaderText(4, 2, 0));
            Assert.AreEqual(15, GrowthCenterView.SkillHeaderPt);
            Assert.IsFalse(Task191Report.Overlap(root.Find("MaterialTitle"), root.Find("MaterialScroll")), "헤더 ↔ 스킬 목록");
            Assert.AreEqual(VerticalWrapMode.Truncate, root.Find("MaterialTitle").GetComponent<Text>().verticalOverflow, "칸 밖으로 넘치지 않음");

            // 3버튼: 두 줄 문구 · 14pt · 버튼 높이 확보 · 목록과 겹치지 않음
            Assert.AreEqual("스킬 변경\n(변경권 1 / 1.5만P)", GrowthCenterView.SkillRerollLabel());
            Assert.AreEqual("고급 변경(A~S)\n(고급권 1)", GrowthCenterView.SkillPremiumLabel());
            var slot = new PlayerSkillSlot { SkillId = "table_setter", Grade = SkillGrade.B, Level = 3 };
            Assert.AreEqual($"스킬 레벨업\n(특훈권 {PlayerSkillRules.LevelUpTicketCost(3)} / {PlayerSkillRules.LevelUpPointCost(3) / 10000f:0.#}만P)", GrowthCenterView.SkillLevelUpLabel(slot));
            Assert.AreEqual("스킬 레벨업\n(최대 Lv.6)", GrowthCenterView.SkillLevelUpLabel(new PlayerSkillSlot { SkillId = "table_setter", Level = PlayerSkillRules.MaxLevel }));
            foreach (var n in new[] { "SkillReroll", "SkillPremium", "SkillLevelUp" })
            {
                var b = (RectTransform)root.Find(n);
                var label = b.GetComponentInChildren<Text>(true);
                Assert.AreEqual(GrowthCenterView.SkillButtonPt, label.fontSize, n + " 14pt");
                Assert.AreEqual(VerticalWrapMode.Truncate, label.verticalOverflow, n + " 버튼 밖으로 넘치지 않음");
                Assert.GreaterOrEqual((b.anchorMax.y - b.anchorMin.y) * CompyaUiKit.RefHeight, 70f, n + " 두 줄 높이");
                Assert.IsFalse(Task191Report.Overlap(b, root.Find("MaterialScroll")), n + " ↔ 목록");
            }

            // 스킬 슬롯 행(제목 16 / 설명 13) · 대상 선수 행(17 / 14)
            var rebuild = typeof(GrowthCenterView).GetMethod("RebuildRows", BindingFlags.Instance | BindingFlags.NonPublic);
            var material = (RectTransform)typeof(GrowthCenterView).GetField("materialContent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var target = (RectTransform)typeof(GrowthCenterView).GetField("targetContent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);
            var skillRows = new List<(string, Color, UnityEngine.Events.UnityAction)> { ("▶ 슬롯 1  [B] 테이블세터 Lv.3\n선구(출루) · 주력 상승", Color.gray, () => { }) };
            rebuild.Invoke(view, new object[] { material, skillRows, 1, GrowthCenterView.SkillSlotTitlePt, GrowthCenterView.SkillSlotDescPt, 104f, 0.6f });
            var skillTexts = material.GetComponentsInChildren<Text>(true);
            Assert.AreEqual(16, skillTexts.First(t => t.name == "Line1").fontSize);
            Assert.That(skillTexts.First(t => t.name == "Line2").fontSize, Is.InRange(13, 14));
            var targetRows = new List<(string, Color, UnityEngine.Events.UnityAction)> { ("구자욱'24 (RF)\nGG · 초월 · OVR 121", Color.gray, () => { }) };
            rebuild.Invoke(view, new object[] { target, targetRows, 3, GrowthCenterView.TargetLine1Pt, GrowthCenterView.TargetLine2Pt, 92f, 0.5f });
            var targetTexts = target.GetComponentsInChildren<Text>(true);
            Assert.AreEqual((17, 14), (targetTexts.First(t => t.name == "Line1").fontSize, targetTexts.First(t => t.name == "Line2").fontSize));
            Assert.IsTrue(targetTexts.All(t => t.fontStyle == FontStyle.Normal));
        }

        // ================================================================== C. 로비

        [Test]
        public void Lobby_SeasonProgressKorean_StarDetailKoreanGrade()
        {
            string league = LeagueTierTable.DisplayName(LeagueTier.Amateur);
            Assert.AreEqual($"{league} · 정규시즌 0 / 144 경기", LeagueDashboardUIController.SeasonProgressLabel(LeagueTier.Amateur, LeaguePhase.REGULAR_OPEN, 0));
            Assert.AreEqual($"{league} · 정규시즌 80 / 144 경기", LeagueDashboardUIController.SeasonProgressLabel(LeagueTier.Amateur, LeaguePhase.REGULAR_LOCKED, 80));
            foreach (LeaguePhase phase in Enum.GetValues(typeof(LeaguePhase)))
            {
                string label = LeagueDashboardUIController.SeasonProgressLabel(LeagueTier.Futures, phase, 10);
                StringAssert.DoesNotContain("REGULAR", label, "영문 enum 노출 금지");
                StringAssert.DoesNotContain(phase.ToString(), label);
            }

            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = "SAM_GG_191"; t.RealPlayerId = t.TemplateId; t.PlayerName = "구자욱";
            t.Team = Team.Samsung; t.Grade = Grade.GOLDEN_GLOVE; t.SeasonYear = 2024;
            t.BatterPosition = BatterPosition.RightField; t.BatterStats = StatProfiles.SpreadBatter(90, t.BatterPosition, 1);
            var line = LobbyHome181.StarDetailLine(new Player("p191", t));
            StringAssert.Contains(CardGrowthRules.DisplayName(Grade.GOLDEN_GLOVE), line);
            StringAssert.DoesNotContain("GOLDEN_GLOVE", line);
            StringAssert.DoesNotContain("\n", line, "한 줄 - OVR · SD는 별도 칸");
        }

        [Test]
        public void Scene_AllTextNormal_Tiered_LobbyStarAndCurrencySeparated_GrowthCenterSized()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;

            var texts = SetupTask191.SceneTexts().ToList();
            Assert.Greater(texts.Count, 100);
            var bold = texts.Where(x => x.fontStyle == FontStyle.Bold || x.fontStyle == FontStyle.BoldAndItalic).Select(x => x.name).ToList();
            Assert.IsEmpty(bold, "씬 전체 Bold 0개: " + string.Join(", ", bold.Take(10)));
            var oversized = texts.Where(x => x.GetComponentInParent<PlayerCardUI>(true) == null && TextTidy.EffectiveSize(x) > TextTidy.MaxSize).Select(x => x.name).ToList();
            Assert.IsEmpty(oversized, "42pt 초과 0개: " + string.Join(", ", oversized.Take(10)));
            Assert.AreEqual(0, SetupTask191.TidyScene(out _), "저장된 씬은 이미 정리 완료(멱등)");
            Assert.IsTrue(SetupTask191.RootCanvases().All(c => c.GetComponent<ReadableFontPass>() != null), "루트 캔버스 정리 패스");

            var home = UnityEngine.Object.FindAnyObjectByType<LobbyHome181>(FindObjectsInactive.Include);
            Assert.IsNotNull(home);
            var r = home.transform;
            Text T(string n) => r.Find(n).GetComponent<Text>();
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(SetupTask181.CurrencyLabelPt, T($"Currency{i}_Label").fontSize, "재화 라벨 13pt");
                Assert.AreEqual(SetupTask181.CurrencyValuePt, T($"Currency{i}_Value").fontSize, "재화 숫자 18pt");
                Assert.IsFalse(Task191Report.Overlap(r.Find($"Currency{i}_Label"), r.Find($"Currency{i}_Value")), "재화 라벨 ↔ 숫자 상하 분리");
            }
            Assert.AreEqual("CBD5E1", ColorUtility.ToHtmlStringRGB(T("Currency0_Label").color));
            Assert.That(T("SeasonProgress").fontSize, Is.InRange(14, 15));
            Assert.That(T("Venue").fontSize, Is.InRange(14, 15));
            var star = new[] { "StarName", "StarDetail", "StarOvrLabel", "StarOvrValue", "StarSd" };
            for (int i = 0; i < star.Length; i++)
                for (int j = i + 1; j < star.Length; j++)
                    Assert.IsFalse(Task191Report.Overlap(r.Find(star[i]), r.Find(star[j])), $"대표 스타 칸 겹침: {star[i]} ↔ {star[j]}");
            Assert.AreEqual(40, T("StarOvrValue").fontSize);
            Assert.AreEqual(14, T("StarOvrLabel").fontSize);

            var growth = UnityEngine.Object.FindAnyObjectByType<GrowthCenterView>(FindObjectsInactive.Include);
            Assert.IsNotNull(growth);
            var g = growth.transform.Find(GrowthCenterView.RootName);
            Assert.IsNotNull(g, "씬에 저장된 성장 센터");
            Assert.That(g.Find("Ladder0/Text").GetComponent<Text>().fontSize, Is.InRange(13, 14));
            Assert.AreEqual(GrowthCenterView.SkillButtonPt, g.Find("SkillReroll").GetComponentInChildren<Text>(true).fontSize);
        }

        // ================================================================== D. 투수 운용 · 탈삼진

        [Test]
        public void Bullpen_StartersGo5to7_NoReEntry_LongRelieverNotOverused_KPerGameNormal()
        {
            var s = Task191Report.SimulateGames(40, 70, 9191);
            Assert.AreEqual(0, s.ReEntries, "한 번 내려간 투수는 같은 경기에 다시 오르지 않는다");
            Assert.That(s.StarterInningsAvg, Is.InRange(4.5f, 7.0f), $"선발 평균 {s.StarterInningsAvg:F2}이닝");
            Assert.Less(s.LongReliefShare, 0.2f, $"롱릴리프 이닝 비중 {s.LongReliefShare:P1}");
            Assert.LessOrEqual(s.MaxRelieverInningsInGame, MatchEngineLongLimit + 1, "구원 1경기 이닝 상한");
            Assert.That(s.StrikeoutsPerTeamGame, Is.InRange(5f, 12f), $"팀당 경기 탈삼진 {s.StrikeoutsPerTeamGame:F2}");
        }

        private const int MatchEngineLongLimit = KBOManager.Engine.MatchEngine.LongReliefMaxInnings;

        [Test]
        public void Season144_StrikeoutLeaderIsStarter_InRealisticRange_NoRelieverMonopoly()
        {
            harness = SeasonCycleHarness.Create(61, LeagueTier.Amateur, Team.Samsung, "K191");
            harness.League.PlaySeasonToCompletion();
            Assert.AreEqual(LeagueManager.TotalUserGames, harness.League.PlayedGameCount);
            var m = Task191Report.MeasureSeason(harness);
            var (leader, stats) = m.Leaders[0];
            Assert.AreEqual(PitcherRole.StartingPitcher, leader.Template.PitcherRole, $"탈삼진 1위 = 선발({leader.Template.PlayerName} {leader.Template.PitcherRole} {stats.Strikeouts}K)");
            Assert.That(stats.Strikeouts, Is.InRange(110, 280), $"시즌 개인 최다 탈삼진 {stats.Strikeouts}");
            Assert.LessOrEqual(m.MaxRelieverInnings, 110f, $"구원 최다 이닝 {m.MaxRelieverInnings:F1}");
            Assert.Less(m.MaxRelieverStrikeouts, stats.Strikeouts, "구원 투수 탈삼진 독식 없음");
            Assert.That(m.StrikeoutsPerTeamGame, Is.InRange(5f, 12f), $"팀당 경기 탈삼진 {m.StrikeoutsPerTeamGame:F2}");
            Assert.That(m.StarterInningsPerStart, Is.InRange(4.5f, 7f), $"선발 경기당 {m.StarterInningsPerStart:F2}이닝");
        }
    }
}
