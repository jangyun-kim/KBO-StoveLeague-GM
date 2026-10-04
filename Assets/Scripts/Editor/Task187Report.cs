using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KBOManager.Controllers;
using KBOManager.Engine;
using KBOManager.Models;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>[TASK-KBO-187] 완료 보고용 검증 리포트(Logs/Task187Report.txt).</summary>
    public static class Task187Report
    {
        [MenuItem("KBO Manager/Debug/TASK-187 Report")]
        public static void RunMenu() => Debug.Log(Build());

        public static void RunBatch()
        {
            string report = Build();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/Task187Report.txt", report, Encoding.UTF8);
            Debug.Log(report);
        }

        public static string Build()
        {
            var sb = new StringBuilder();
            var previous = LineupAssignment.Active;
            try
            {
                LineupAssignment.Active = null;
                BuildSetDeck(sb);
                BuildRotation(sb);
                BuildCheer(sb);
                BuildBlowout(sb);
            }
            finally
            {
                LineupAssignment.Active = previous;
                OvrGapLaw.BlowoutDampingEnabled = true;
                Task183Report.CleanupTemp();
            }
            return sb.ToString();
        }

        private static void BuildSetDeck(StringBuilder sb)
        {
            sb.AppendLine("=== [1] 라인업 카드 SD 합계 == 하단 NP POINT ===");
            var roster = Task183Report.ProceduralTeam(Team.Samsung, 80, "SD");
            var others = Task183Report.ProceduralTeam(Team.KIA, 80, "SDK");
            roster[3] = others[3];
            roster[20] = others[20]; // 다른 구단 카드 2장(SD 0)
            var result = SetDeckEvaluator.Evaluate(roster, Team.Samsung.ToString());
            int sum = roster.Sum(p => SetDeckEvaluator.CardScoreIn(result, p));
            int excluded = roster.Count(p => SetDeckEvaluator.IsExcludedFromSlots(result, p));
            int zero = roster.Count(p => SetDeckEvaluator.CardScoreIn(result, p) == 0);
            sb.AppendLine($"28인 카드 SD 합계 {sum} / 총점 {result.Score} → {(sum == result.Score ? "일치" : "불일치")} · SD 0 카드 {zero}장(다른 구단 2 + 27인 제외 {excluded})");
            foreach (int score in new[] { 60, 95, 152, 199 })
            {
                var (_, markers) = RosterUIController.GaugeWindow(score);
                sb.AppendLine($"게이지 {score}P: " + string.Join(" | ", markers.Select(m => $"{m}P {RosterUIController.GaugeIconLabel(m, score, null, 2024)}")));
            }
            sb.AppendLine($"타순 마름모: 고정 {RosterUIController.OrderDiamondSize}×{RosterUIController.OrderDiamondSize}px, 카드 좌측 중단(AspectRatioFitter 제거)");
            sb.AppendLine();
        }

        private static string N(Player p) => p == null ? "-" : $"{p.Template.PlayerName}(OVR {p.CalculateOVR(false)})";

        private static void BuildRotation(StringBuilder sb)
        {
            sb.AppendLine("=== [2][3] 투수 13칸 고정 · 1~5선발 로테이션 · 예고 선발 = 실제 선발 ===");
            var roster = Task183Report.ProceduralTeam(Team.Samsung, 80, "ROT");
            var assignment = new LineupAssignment();
            var rotation = StartingRotation.RotationOf(roster, assignment);
            var closer = LineupView.BuildPitchers(roster, assignment).Bullpen.First(e => e.Group == "마무리").Player;
            assignment.SwapPitchers(roster, rotation[0], rotation[3]);
            assignment.SwapPitchers(roster, rotation[1], closer);
            var after = StartingRotation.RotationOf(roster, assignment);
            sb.AppendLine("맞교환 전 1~5선발: " + string.Join(", ", rotation.Select(N)));
            sb.AppendLine("1선발↔4선발, 2선발↔마무리 후: " + string.Join(", ", after.Select(N)) +
                          $" | 마무리 {N(LineupView.BuildPitchers(roster, assignment).Bullpen.First(e => e.Group == "마무리").Player)}");

            LineupAssignment.Active = assignment;
            var other = Task183Report.ProceduralTeam(Team.SSG, 80, "OPP");
            var lines = new List<string>();
            int match = 0;
            for (int g = 0; g < 7; g++)
            {
                var predicted = StartingRotation.PickFor(roster, g);
                foreach (var p in roster.Concat(other)) if (p.Template.IsPitcher) p.RecoverStamina(1000);
                var engine = new MatchEngine(roster, other, new TeamPowerModifiers(0), new TeamPowerModifiers(0), null, null, 187 + g)
                {
                    HomeDesignatedStarter = predicted,
                    AwayDesignatedStarter = StartingRotation.PickFor(other, g),
                };
                engine.BeginMatch("H", "A");
                var first = engine.PlayNextAtBat(); // 1회초 - 홈 선발이 던진다
                if (first.Pitcher == predicted) match++;
                lines.Add($"{g + 1}경기 예고 {predicted?.Template.PlayerName} / 등판 {first.Pitcher?.Template.PlayerName}");
            }
            LineupAssignment.Active = null;
            sb.AppendLine(string.Join(" · ", lines));
            sb.AppendLine($"예고 선발 = 실제 선발 {match}/7 (6경기째 1선발 복귀)");
            sb.AppendLine();
        }

        private static Cheerleader C(string id, string name, Team team) =>
            new Cheerleader { InstanceId = id, Name = name, Team = team, Grade = CheerleaderGrade.LIVE_NORMAL, EconomicBonusRate = 1.05f };

        private static void BuildCheer(StringBuilder sb)
        {
            sb.AppendLine("=== [4] 치어리더 강화 · ★각성 · 도감 ===");
            var target = C("t", "김응원", Team.Samsung);
            var squad = Enumerable.Repeat<Cheerleader>(null, CheerSquad.SlotCount).ToList();
            squad[(int)CheerRole.Home] = target;
            string before = CheerSquad.DescribeRoleEffect(CheerRole.Home, target);
            target.ReinforceLevel = CheerGrowth.MaxReinforce;
            int g1 = CheerGrowth.ApplyAwaken(target, C("m1", "김응원", Team.Samsung));
            int g2 = CheerGrowth.ApplyAwaken(target, C("m2", "박응원", Team.Samsung));
            int g3 = CheerGrowth.ApplyAwaken(target, C("m3", "김응원", Team.Samsung));
            sb.AppendLine($"홈 응원 +0강 ★1: {before}");
            sb.AppendLine($"홈 응원 +10강 · 동일 인물 +{g1}★ · 동일 구단 +{g2}★ · 동일 인물 +{g3}★ → {CheerGrowth.StarBadge(target)}(티어 {CheerGrowth.EffectiveTier(target)}): " +
                          CheerSquad.DescribeRoleEffect(CheerRole.Home, target));
            sb.AppendLine($"강화 비용 +0→+1 {CheerGrowth.ReinforceCost(0):N0} / +9→+10 {CheerGrowth.ReinforceCost(9):N0} 포인트");
            var catalog = CheerGrowth.FullCatalog();
            var owned = catalog.Where(c => c.Team == Team.Samsung).Take(5).ToList();
            var collection = CheerGrowth.Collection(Team.Samsung, owned, catalog);
            sb.AppendLine($"{collection.Label} | 홈 승리 수익 배율 x{CheerGrowth.HomeRevenueMultiplier(target, collection):F2}");
            var fx = CheerSquad.BuildEffects(squad, Team.Samsung, true, 0, 0);
            sb.AppendLine($"경기 효과(홈 경기): 홈 전 스탯 +{fx.HomeAllStatsBonus}, 상대 제구 -{fx.OpponentControlPenalty} | 구단 OVR 가산 없음(TeamOvrCalculator는 카드 등급 티어만 사용)");
            sb.AppendLine();
        }

        private static void BuildBlowout(StringBuilder sb)
        {
            sb.AppendLine("=== [5] 대승 스코어 감쇠(ΔOVR ≥ 8) ===");
            foreach (int gap in new[] { 10, 25, 40 })
            {
                var off = SimulateBlowout(60, gap, 150, false);
                var on = SimulateBlowout(60, gap, 150, true);
                sb.AppendLine($"Δ{gap}: 감쇠 전 승률 {off.winRate:P1} · 평균 {off.avgRuns:F1}점 · 최다 {off.maxRuns}점 → 감쇠 후 승률 {on.winRate:P1} · 평균 {on.avgRuns:F1}점 · 최다 {on.maxRuns}점");
            }
        }

        /// <summary>상위 구단(baseOvr + gap) 승률(무승부 제외)과 상위 구단 경기당 평균/최다 득점.</summary>
        public static (double winRate, double avgRuns, int maxRuns) SimulateBlowout(int baseOvr, int gap, int games, bool damping)
        {
            OvrGapLaw.BlowoutDampingEnabled = damping;
            try
            {
                var weak = Task183Report.ProceduralTeam(Team.Doosan, baseOvr, "BW");
                var strong = Task183Report.ProceduralTeam(Team.LG, baseOvr + gap, "BS");
                int weakOvr = TeamOvrCalculator.Calculate(weak).Total, strongOvr = TeamOvrCalculator.Calculate(strong).Total;
                int wins = 0, losses = 0, runs = 0, max = 0;
                for (int g = 0; g < games; g++)
                {
                    bool strongHome = g % 2 == 0;
                    var home = strongHome ? strong : weak;
                    var away = strongHome ? weak : strong;
                    foreach (var p in home.Concat(away)) if (p.Template.IsPitcher) p.RecoverStamina(1000);
                    var hm = new TeamPowerModifiers(0, TeamPowerModifiers.HomeAdvantageConditionBuff, 1f, null, null, strongHome ? strongOvr : weakOvr);
                    var am = new TeamPowerModifiers(0, 0, 1f, null, null, strongHome ? weakOvr : strongOvr);
                    var result = new MatchEngine(home, away, hm, am, null, null, 187000 + g).PlayFullMatch("H", "A");
                    int strongRuns = strongHome ? result.HomeTotalScore : result.AwayTotalScore;
                    runs += strongRuns;
                    max = Math.Max(max, strongRuns);
                    if (result.WinnerTeamName == null) continue;
                    if ((result.WinnerTeamName == "H") == strongHome) wins++; else losses++;
                }
                return (wins + losses == 0 ? 0 : (double)wins / (wins + losses), (double)runs / games, max);
            }
            finally
            {
                OvrGapLaw.BlowoutDampingEnabled = true;
            }
        }
    }
}
