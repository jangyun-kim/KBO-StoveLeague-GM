using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KBOManager.Controllers;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-191] 완료 보고용 실측 리포트(Logs/Task191Report.txt).
    ///   [A] 전 화면 텍스트 - 씬 Text 굵기/크기 분포(Bold 0 · 42pt 초과 0), 계층 매핑표
    ///   [B] 성장 센터(스킬 헤더 · 슬롯 · 3버튼 · 각성 사다리 · 대상 선수 목록) · 로비(재화 바 · NEXT MATCH · 대표 스타) 크기와 칸 겹침 검사
    ///   [C] 투수 운용 - 단일 경기 반복(선발 이닝 · 재등판 금지 · 롱릴리프 비중 · 경기당 탈삼진) + 144경기 완주 개인 탈삼진 순위
    /// </summary>
    public static class Task191Report
    {
        [MenuItem("KBO Manager/Debug/TASK-191 Report")]
        public static void RunMenu() => Debug.Log(Build());

        public static void RunBatch()
        {
            string report = Build();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/Task191Report.txt", report, Encoding.UTF8);
            Debug.Log(report);
        }

        public static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== TASK-191 실측 리포트 ===");
            try { FontSection(sb); } catch (Exception e) { sb.AppendLine("[A 오류] " + e); }
            try { LayoutSection(sb); } catch (Exception e) { sb.AppendLine("[B 오류] " + e); }
            try { PitchingSection(sb); } catch (Exception e) { sb.AppendLine("[C 오류] " + e); }
            return sb.ToString();
        }

        // ================================================================== [A] 폰트

        private static void FontSection(StringBuilder sb)
        {
            sb.AppendLine("\n[A] 전 화면 텍스트 굵기 · 크기");
            var texts = SetupTask191.SceneTexts().ToList();
            var ui = texts.Where(t => t.GetComponentInParent<PlayerCardUI>(true) == null).ToList();
            int bold = texts.Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic);
            int over = ui.Count(t => TextTidy.EffectiveSize(t) > TextTidy.MaxSize);
            int tidy = texts.Count(t => t.GetComponent<TextTidy>() != null);
            int minFit = ui.Count(t => t.resizeTextForBestFit && t.resizeTextMinSize > 12);
            sb.AppendLine($"씬 Text {texts.Count}개(카드 내부 {texts.Count - ui.Count}) · Bold/BoldItalic {bold}개 · 42pt 초과 {over}개 · 자간/정리 표식 {tidy}개 · 자동 크기 최소 12pt 초과 {minFit}개");
            var bands = new[] { (0, 12, "~12"), (13, 15, "13~15 보조"), (16, 18, "16~18 본문"), (19, 21, "19~21 버튼·헤더"), (22, 26, "22~26 타이틀"), (27, 42, "27~42 대형 숫자"), (43, 999, "43~") };
            sb.AppendLine("크기 분포(카드 제외): " + string.Join(" · ", bands.Select(b => $"{b.Item3} {ui.Count(t => TextTidy.EffectiveSize(t) >= b.Item1 && TextTidy.EffectiveSize(t) <= b.Item2)}")));
            sb.AppendLine("계층 매핑(예전 → 새 크기, 일반 / 버튼): " + string.Join(" · ", new[] { 14, 18, 20, 22, 24, 26, 28, 32, 36, 42, 46, 58, 64, 94 }
                .Select(s => $"{s}→{TextTidy.Tier(s, false)}/{TextTidy.Tier(s, true)}")));
            sb.AppendLine("TASK-186 확대분 복원: 22pt 본문 → 16 · 26pt 강조 → 17 · 버튼 → 15 (Best Fit 최소 18 → 11)");
        }

        // ================================================================== [B] 레이아웃

        private static void LayoutSection(StringBuilder sb)
        {
            sb.AppendLine("\n[B] 성장 센터 · 로비 텍스트 겹침");
            var growth = UnityEngine.Object.FindAnyObjectByType<GrowthCenterView>(FindObjectsInactive.Include);
            if (growth != null)
            {
                var root = growth.transform.Find(GrowthCenterView.RootName);
                Text T(string path) => root != null ? root.Find(path)?.GetComponent<Text>() : null;
                string S(Text t) => t == null ? "없음" : $"{t.fontSize}pt {t.fontStyle}";
                sb.AppendLine($"성장 센터: 타이틀 {S(T("Title"))} · 선수명 {S(T("Name"))} · OVR {S(T("Ovr"))} · 각성 사다리 {S(T("Ladder0/Text"))}/{S(T("Ladder10/Text"))} · 헤더 버튼 {S(T("SourceButton/Text"))}");
                sb.AppendLine($"  훈련·특훈 헤더 \"{GrowthCenterView.SkillHeaderText(4, 2, 0)}\" {GrowthCenterView.SkillHeaderPt}pt · 슬롯 제목 {GrowthCenterView.SkillSlotTitlePt}pt / 설명 {GrowthCenterView.SkillSlotDescPt}pt");
                foreach (var n in new[] { "SkillReroll", "SkillPremium", "SkillLevelUp" })
                {
                    var b = root?.Find(n) as RectTransform;
                    var label = b != null ? b.GetComponentInChildren<Text>(true) : null;
                    sb.AppendLine($"  [{n}] {S(label)} · 버튼 높이 {(b != null ? (b.anchorMax.y - b.anchorMin.y) * CompyaUiKit.RefHeight : 0f):F0}px(레퍼런스)");
                }
                sb.AppendLine($"  버튼 문구: \"{GrowthCenterView.SkillRerollLabel().Replace("\n", " / ")}\" · \"{GrowthCenterView.SkillPremiumLabel().Replace("\n", " / ")}\" · \"{GrowthCenterView.SkillLevelUpLabel(new PlayerSkillSlot { SkillId = "table_setter", Grade = SkillGrade.B, Level = 3 }).Replace("\n", " / ")}\"");
                sb.AppendLine($"  대상 선수 행: 1줄 {GrowthCenterView.TargetLine1Pt}pt / 2줄 {GrowthCenterView.TargetLine2Pt}pt Normal");
                sb.AppendLine($"  헤더 ↔ 목록 겹침: {Overlap(root?.Find("MaterialTitle"), root?.Find("MaterialScroll"))} · 목록 ↔ 스킬 버튼 겹침: {Overlap(root?.Find("MaterialScroll"), root?.Find("SkillReroll"))}");
            }
            else sb.AppendLine("성장 센터 없음");

            var home = UnityEngine.Object.FindAnyObjectByType<LobbyHome181>(FindObjectsInactive.Include);
            if (home != null)
            {
                var r = home.transform;
                Text T(string n) => r.Find(n)?.GetComponent<Text>();
                string S(Text t) => t == null ? "없음" : $"{t.fontSize}pt {t.fontStyle}";
                sb.AppendLine($"로비 재화: 라벨 {S(T("Currency0_Label"))}(#{ColorUtility.ToHtmlStringRGB(T("Currency0_Label")?.color ?? Color.clear)}) / 숫자 {S(T("Currency0_Value"))} · 겹침 {Overlap(r.Find("Currency0_Label"), r.Find("Currency0_Value"))}");
                sb.AppendLine($"NEXT MATCH: 진행도 {S(T("SeasonProgress"))} \"{LeagueDashboardUIController.SeasonProgressLabel(LeagueTier.Amateur, LeaguePhase.REGULAR_OPEN, 0)}\" · 경기장/예고 선발 {S(T("Venue"))}");
                var star = new[] { "StarName", "StarDetail", "StarOvrLabel", "StarOvrValue", "StarSd" };
                int overlaps = 0;
                for (int i = 0; i < star.Length; i++)
                    for (int j = i + 1; j < star.Length; j++)
                        if (Overlap(r.Find(star[i]), r.Find(star[j]))) overlaps++;
                sb.AppendLine($"대표 스타: {string.Join(" · ", star.Select(n => $"{n} {S(T(n))}"))} · 칸 겹침 {overlaps}쌍");
                var synergy = UnityEngine.Object.FindObjectsByType<TeamSynergyUIController>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .SelectMany(c => c.GetComponentsInChildren<Text>(true)).Select(S).Distinct().ToList();
                sb.AppendLine($"하단 요약/로그(세트덱 · 치어리더 · 팬심): {(synergy.Count > 0 ? string.Join(" · ", synergy) : "없음")}");
                sb.AppendLine($"_Legacy178 보관함 안 LobbyHome181 복사본: {UnityEngine.Object.FindObjectsByType<LobbyHome181>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length - 1}개");
            }
            else sb.AppendLine("로비(LobbyHome181) 없음");
        }

        /// <summary>두 RectTransform(같은 부모 기준 앵커 박스)이 겹치는가.</summary>
        public static bool Overlap(Transform a, Transform b)
        {
            if (!(a is RectTransform ra) || !(b is RectTransform rb)) return false;
            float w = Mathf.Min(ra.anchorMax.x, rb.anchorMax.x) - Mathf.Max(ra.anchorMin.x, rb.anchorMin.x);
            float h = Mathf.Min(ra.anchorMax.y, rb.anchorMax.y) - Mathf.Max(ra.anchorMin.y, rb.anchorMin.y);
            return w > 0.0005f && h > 0.0005f;
        }

        // ================================================================== [C] 투수 운용

        public sealed class BullpenSample
        {
            public int Games;
            public float StarterInningsAvg;
            public float LongReliefShare;
            public float StrikeoutsPerTeamGame;
            public int ReEntries;
            public int MaxRelieverInningsInGame;
        }

        /// <summary>
        /// 같은 두 구단(OVR ovr)으로 games경기를 실제 엔진으로 연속 진행한다(1~5선발 로테이션 · 하루 체력 회복 = LeagueManager와 같은 선발 +25 / 불펜 +15).
        /// 선발 이닝(선발이 시작한 하프이닝 수) · 롱릴리프 이닝 비중 · 팀당 경기 탈삼진 · 같은 경기 재등판 횟수를 잰다.
        /// </summary>
        public static BullpenSample SimulateGames(int games, int ovr, int seed)
        {
            var home = Task183Report.ProceduralTeam(Team.LG, ovr, "BP" + seed + "H");
            var away = Task183Report.ProceduralTeam(Team.KT, ovr, "BP" + seed + "A");
            var sample = new BullpenSample { Games = games };
            int starterInnings = 0, starts = 0, longInnings = 0, totalInnings = 0, strikeouts = 0;
            for (int g = 0; g < games; g++)
            {
                var homeStarter = StartingRotation.PickFor(home, g);
                var awayStarter = StartingRotation.PickFor(away, g);
                var engine = new MatchEngine(home, away, new TeamPowerModifiers(0), new TeamPowerModifiers(0), null, null, seed * 1000 + g)
                {
                    HomeDesignatedStarter = homeStarter,
                    AwayDesignatedStarter = awayStarter,
                };
                engine.BeginMatch("H", "A");
                var sequence = new Dictionary<bool, List<Player>> { [true] = new List<Player>(), [false] = new List<Player>() };
                var halfInnings = new Dictionary<Player, HashSet<(int, bool)>>();
                AtBatStepResult step;
                do
                {
                    step = engine.PlayNextAtBat();
                    if (step.Pitcher == null || step.State == null) continue;
                    var seq = sequence[step.IsTopHalf];
                    if (seq.Count == 0 || seq[seq.Count - 1] != step.Pitcher) seq.Add(step.Pitcher);
                    if (!halfInnings.TryGetValue(step.Pitcher, out var set)) halfInnings[step.Pitcher] = set = new HashSet<(int, bool)>();
                    set.Add((step.State.Inning, step.IsTopHalf));
                    if (step.Result == AtBatResult.Strikeout) strikeouts++;
                } while (!step.GameEnded && !engine.IsGameOver);

                foreach (var seq in sequence.Values)
                {
                    sample.ReEntries += seq.Count - seq.Distinct().Count();
                    if (seq.Count == 0) continue;
                    starts++;
                    starterInnings += halfInnings[seq[0]].Count;
                }
                foreach (var kv in halfInnings)
                {
                    totalInnings += kv.Value.Count;
                    var role = kv.Key.Template.PitcherRole;
                    if (role == PitcherRole.LongReliever) longInnings += kv.Value.Count;
                    if (role != PitcherRole.StartingPitcher) sample.MaxRelieverInningsInGame = Mathf.Max(sample.MaxRelieverInningsInGame, kv.Value.Count);
                }

                // 다음 날 체력 회복(LeagueManager.RecoverRosterStamina와 같은 값)
                foreach (var p in home.Concat(away).Where(p => p.Template.IsPitcher))
                    p.RecoverStamina(p.Template.PitcherRole == PitcherRole.StartingPitcher ? 25 : 15);
            }
            sample.StarterInningsAvg = starts > 0 ? starterInnings / (float)starts : 0f;
            sample.LongReliefShare = totalInnings > 0 ? longInnings / (float)totalInnings : 0f;
            sample.StrikeoutsPerTeamGame = games > 0 ? strikeouts / (games * 2f) : 0f;
            return sample;
        }

        public sealed class SeasonPitching
        {
            public List<(Player player, PitcherSeasonStats stats)> Leaders = new List<(Player, PitcherSeasonStats)>();
            public float StrikeoutsPerTeamGame;
            public float MaxRelieverInnings;
            public int MaxRelieverStrikeouts;
            public float StarterInningsPerStart;
            public int TeamGames;
        }

        public static SeasonPitching MeasureSeason(SeasonCycleHarness h)
        {
            var all = h.Stats.AllPitcherStats.Select(kv => (player: kv.Key, stats: kv.Value)).ToList();
            var result = new SeasonPitching { TeamGames = h.League.GetStandings().Sum(t => t.Wins + t.Draws + t.Losses) };
            result.Leaders = all.OrderByDescending(x => x.stats.Strikeouts).Take(5).ToList();
            result.StrikeoutsPerTeamGame = result.TeamGames > 0 ? all.Sum(x => x.stats.Strikeouts) / (float)result.TeamGames : 0f;
            var relievers = all.Where(x => x.player.Template.PitcherRole != PitcherRole.StartingPitcher).ToList();
            result.MaxRelieverInnings = relievers.Count > 0 ? relievers.Max(x => x.stats.InningsPitched) : 0f;
            result.MaxRelieverStrikeouts = relievers.Count > 0 ? relievers.Max(x => x.stats.Strikeouts) : 0;
            float starterInnings = all.Where(x => x.player.Template.PitcherRole == PitcherRole.StartingPitcher).Sum(x => x.stats.InningsPitched);
            result.StarterInningsPerStart = result.TeamGames > 0 ? starterInnings / result.TeamGames : 0f;
            return result;
        }

        private static string RoleLabel(PitcherRole role) => role switch
        {
            PitcherRole.StartingPitcher => "선발",
            PitcherRole.LongReliever => "롱릴리프",
            PitcherRole.WinningReliever => "승리조",
            PitcherRole.MopUpReliever => "추격조",
            PitcherRole.Closer => "마무리",
            _ => role.ToString(),
        };

        private static void PitchingSection(StringBuilder sb)
        {
            sb.AppendLine("\n[C] 투수 운용 · 탈삼진");
            foreach (int ovr in new[] { 60, 80 })
            {
                var s = SimulateGames(60, ovr, 191 + ovr);
                sb.AppendLine($"단일 경기 60연전(OVR {ovr} 대등): 선발 평균 {s.StarterInningsAvg:F2}이닝 · 롱릴리프 이닝 비중 {s.LongReliefShare:P1} · 구원 1경기 최다 {s.MaxRelieverInningsInGame}이닝 · " +
                              $"팀당 경기 탈삼진 {s.StrikeoutsPerTeamGame:F2}개 · 같은 경기 재등판 {s.ReEntries}회");
                Task183Report.CleanupTemp();
            }

            using (var h = SeasonCycleHarness.Create(61, LeagueTier.Amateur, Team.Samsung, "R191"))
            {
                h.League.PlaySeasonToCompletion();
                var m = MeasureSeason(h);
                sb.AppendLine($"144경기 완주(유저 OVR 61 · 아마추어 57~64): 10개 구단 {m.TeamGames}팀경기 · 팀당 경기 탈삼진 {m.StrikeoutsPerTeamGame:F2}개 · 선발 경기당 {m.StarterInningsPerStart:F2}이닝 · " +
                              $"구원 최다 {m.MaxRelieverInnings:F1}이닝 / 최다 탈삼진 {m.MaxRelieverStrikeouts}개");
                sb.AppendLine("개인 탈삼진 순위:");
                int rank = 1;
                foreach (var (p, st) in m.Leaders)
                    sb.AppendLine($"  {rank++}위 {p.Template.PlayerName} ({p.Template.Team} · {RoleLabel(p.Template.PitcherRole)})  {st.Strikeouts}탈삼진 · {st.InningsPitched:F1}이닝 · ERA {st.EarnedRunAverage:F2} · {st.Wins}승 {st.Losses}패");
                var titles = SeasonAwardRules.Compute(h.Stats.AllBatterStats, h.Stats.AllPitcherStats, h.League.PlayedGameCount, p => p.Template.Team == h.League.UserTeam);
                var k = titles.FirstOrDefault(t => t.Category == "탈삼진");
                if (k != null) sb.AppendLine("탈삼진 타이틀: " + k.Line);
            }
            Task183Report.CleanupTemp();
        }
    }
}
