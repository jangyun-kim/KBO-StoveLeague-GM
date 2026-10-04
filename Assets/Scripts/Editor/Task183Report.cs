using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-183] 검증 리포트(배치 실행: Unity.exe -batchmode -projectPath . -executeMethod KBOManager.EditorTools.Task183Report.RunBatch -quit).
    ///   1) 10개 구단 신규 부임 직후(2026 LIVE_NORMAL 전원 + '24 골든글러브 선물 4종 각각) 자동 편성 28인 평균 / 구단 OVR / 권장 리그.
    ///   2) 시즌 등급별 기본 OVR → 순수 풀성장 → 풀시너지(144 상한) 실측표, 선물 카드 4종 세부 스탯.
    ///   3) 'OVR 7 격차 법칙' - 구단 OVR 차이별 상위 구단 승률(MatchEngine 시뮬레이션).
    /// 결과는 Logs/Task183Report.txt에도 저장한다.
    /// </summary>
    public static class Task183Report
    {
        public static readonly string[] GiftIds =
        {
            "SAMSUNG_2024_PLY_004038_GG", "KIA_2024_PLY_004368_GG", "NC_2024_PLY_004366_GG", "KT_2024_PLY_004369_GG",
        };

        [MenuItem("KBO Manager/Debug/TASK-183 Balance Report")]
        public static void RunMenu() => Debug.Log(Build(gapGames: 300));

        public static void RunBatch()
        {
            string report = Build(gapGames: 400);
            System.IO.Directory.CreateDirectory("Logs");
            System.IO.File.WriteAllText("Logs/Task183Report.txt", report, Encoding.UTF8);
            Debug.Log(report);
        }

        public static string Build(int gapGames)
        {
            var sb = new StringBuilder();
            var go = new GameObject("Task183ReportDb");
            try
            {
                var db = go.AddComponent<PlayerDatabase>();
                var rosterGo = new GameObject("Task183ReportRoster");
                var rosterManager = rosterGo.AddComponent<RosterManager>();
                var all = db.AllTemplates;

                sb.AppendLine("=== [1] 10개 구단 신규 부임 직후 구단 OVR (2026 LIVE_NORMAL 전원 + '24 골든글러브 선물 1장 + 기본 세트덱) ===");
                foreach (Team team in Enum.GetValues(typeof(Team)))
                {
                    if (team == Team.None) continue;
                    var parts = new List<string>();
                    var tiers = new HashSet<string>();
                    foreach (var giftId in GiftIds)
                    {
                        var m = MeasureOnboarding(all, db, rosterManager, team, giftId);
                        parts.Add($"{giftId.Split('_')[0]}선물 {m.teamOvr}(28인 {m.rosterAvg:F1})");
                        tiers.Add(LeagueTierTable.DisplayName(LeagueTierTable.RecommendedFor(m.teamOvr)));
                    }
                    var none = MeasureOnboarding(all, db, rosterManager, team, null);
                    tiers.Add(LeagueTierTable.DisplayName(LeagueTierTable.RecommendedFor(none.teamOvr)));
                    sb.AppendLine($"{team,-8} LIVE {none.liveCount}장 | 선물 전 28인 평균 {none.rosterAvg:F2} | 세트덱 {none.score}P(+{none.setDeckOvr}) | " +
                                  $"선물 전 {none.teamOvr} | {string.Join(" / ", parts)} | 리그 {string.Join(",", tiers)}");
                }
                {
                    // 상세: 삼성 + 구자욱 '24 선물 - 생성기(GenerateKBODatabase.py simulate_team)와 C# 편성/구단 OVR 대조용.
                    var inv = OnboardingRules.SelectStarterTemplates(all, Team.Samsung).Select(t => new Player(Guid.NewGuid().ToString(), t)).ToList();
                    inv.Add(db.CreatePlayerInstance(GiftIds[0]));
                    var r = rosterManager.AutoSetRoster(inv, "Samsung");
                    sb.AppendLine("  [상세] 삼성+구자욱'24 28인: " + string.Join(", ", r.Select(p =>
                        $"{p.Template.PlayerName}/{(p.Template.IsPitcher ? p.Template.PitcherRole.ToString() : p.Template.BatterPosition.ToString())}/{p.CalculateNeutralOVR()}")));
                    sb.AppendLine($"  [상세] 기본 {TeamOvrCalculator.BaseOvr(r)} / 합계 {TeamOvrCalculator.Calculate(r, "Samsung").Total}");
                }
                UnityEngine.Object.DestroyImmediate(rosterGo);

                sb.AppendLine();
                sb.AppendLine("=== [2] 시즌 등급별 기본 OVR → 순수 풀성장 → 풀시너지(+17, 상한 144) 실측 ===");
                foreach (Grade grade in new[] { Grade.LIVE_NORMAL, Grade.LIVE_EPIC, Grade.ALLSTAR, Grade.FRANCHISE, Grade.TITLE_HOLDER,
                             Grade.GOLDEN_GLOVE, Grade.SIGNATURE, Grade.DYNASTY, Grade.RETIRED_NUMBER })
                {
                    var cards = all.Where(t => t.Grade == grade).ToList();
                    if (cards.Count == 0) continue;
                    int lo = cards.Min(t => t.GetBaseOverall()), hi = cards.Max(t => t.GetBaseOverall());
                    var minP = FullyGrown(cards.First(t => t.GetBaseOverall() == lo));
                    var maxP = FullyGrown(cards.First(t => t.GetBaseOverall() == hi));
                    int g = CardGrowthRules.MaxTotalGrowth(grade);
                    sb.AppendLine($"{grade,-15} {cards.Count,4}장 | 기본 {lo}~{hi} | 풀성장(+{g}) {minP.CalculateNeutralOVR()}~{maxP.CalculateNeutralOVR()} | " +
                                  $"풀시너지 {TeamSynergyRules.ClampFinal(minP.CalculateNeutralOVR() + TeamSynergyRules.MaxSynergyOvr)}~" +
                                  $"{TeamSynergyRules.ClampFinal(maxP.CalculateNeutralOVR() + TeamSynergyRules.MaxSynergyOvr)} | " +
                                  $"강화 {CardGrowthRules.MaxReinforceGrowth} 돌파 {CardGrowthRules.LimitBreakCap(grade)} 특훈 {CardGrowthRules.TrainingCap(grade)} 각성 {CardGrowthRules.AwakenGrowthCap(grade)}");
                }
                sb.AppendLine("--- 온보딩 선물 카드 4종 세부 스탯 ---");
                foreach (var id in GiftIds)
                {
                    var t = db.GetTemplateById(id);
                    if (t == null) { sb.AppendLine($"{id}: 없음"); continue; }
                    sb.AppendLine(t.IsPitcher
                        ? $"{OnboardingRules.CardTitle(t)} ({t.PitcherRole}) OVR {t.GetBaseOverall()} | 구위 {t.PitcherStats.Stuff} 구속 {t.PitcherStats.Velocity} 변화 {t.PitcherStats.Movement} 제구 {t.PitcherStats.Control} 체력 {t.PitcherStats.Stamina}"
                        : $"{OnboardingRules.CardTitle(t)} ({t.BatterPosition}) OVR {t.GetBaseOverall()} | 파워 {t.BatterStats.Power} 정확 {t.BatterStats.Contact} 선구 {t.BatterStats.Discipline} 주력 {t.BatterStats.Speed} 수비 {t.BatterStats.Defense}");
                }
                int flat = all.Count(t => t.IsPitcher
                    ? new[] { t.PitcherStats.Stuff, t.PitcherStats.Velocity, t.PitcherStats.Movement, t.PitcherStats.Control, t.PitcherStats.Stamina }.Distinct().Count() == 1
                    : new[] { t.BatterStats.Power, t.BatterStats.Contact, t.BatterStats.Discipline, t.BatterStats.Speed, t.BatterStats.Defense }.Distinct().Count() == 1);
                sb.AppendLine($"세부 스탯 5개가 모두 같은 카드: {flat}장 / 전체 {all.Count}장");

                sb.AppendLine();
                sb.AppendLine("=== [3] 12단계 리그 AI 9개 구단 목표 OVR ===");
                foreach (var info in LeagueTierTable.All)
                    sb.AppendLine($"{(int)info.Tier,2}. {info.Name,-12} 권장 {info.RangeLabel,-8} AI {string.Join(",", LeagueTierTable.AiTeamOvrTargets(info.Tier))}");

                sb.AppendLine();
                sb.AppendLine($"=== [4] 'OVR 7 격차 법칙' - 구단 OVR 차이별 상위 구단 승률({gapGames}경기, 홈/원정 교대, 무승부 제외) ===");
                foreach (int baseOvr in new[] { 61, 100 })
                {
                    for (int gap = 0; gap <= 12; gap++)
                    {
                        var (win, draw) = SimulateGap(baseOvr, gap, gapGames, seed: 1830 + gap);
                        sb.AppendLine($"기준 {baseOvr} vs {baseOvr + gap} (Δ{gap,2}) 상위 승률 {win:P1}  무 {draw}  {(OvrGapLaw.IsDecisive(gap) ? $"체급 우위 +{OvrGapLaw.ClassAdvantageBonus(baseOvr + gap, baseOvr)}" : "가변 승부")}");
                    }
                }
                var (underdog, _) = SimulateUnderdogWithTools(61, 7, gapGames, 9183);
                sb.AppendLine($"Δ7 약팀(61, 홈 + 치어리더 LEGEND 6인 + 2연패 + 컨디션 최상) vs 68 - 약팀 승률 {underdog:P1}");
                var (underdog8, _) = SimulateUnderdogWithTools(61, 8, gapGames, 9184);
                sb.AppendLine($"Δ8 약팀(61, 같은 조건) vs 69 - 약팀 승률 {underdog8:P1}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
            return sb.ToString();
        }

        public static (int liveCount, double rosterAvg, int score, int setDeckOvr, int teamOvr) MeasureOnboarding(
            IReadOnlyList<PlayerTemplate> all, PlayerDatabase db, RosterManager rosterManager, Team team, string giftId)
        {
            var inventory = OnboardingRules.SelectStarterTemplates(all, team).Select(t => new Player(Guid.NewGuid().ToString(), t)).ToList();
            int liveCount = inventory.Count;
            if (giftId != null) inventory.Add(db.CreatePlayerInstance(giftId));
            var roster = rosterManager.AutoSetRoster(inventory, team.ToString());
            var b = TeamOvrCalculator.Calculate(roster, team.ToString(), null);
            return (liveCount, roster.Average(p => p.CalculateNeutralOVR()), b.SetDeckScore, b.SetDeck, b.Total);
        }

        public static Player FullyGrown(PlayerTemplate template)
        {
            return new Player(Guid.NewGuid().ToString(), template)
            {
                ReinforceLevel = Player.MaxReinforceLevel,
                LimitBreakLevel = CardGrowthRules.MaxLimitBreakGrowth,
                TrainingLevel = CardGrowthRules.MaxTrainingGrowth,
                AwakenLevel = CardGrowthRules.MaxAwakenLevelFor(template.Grade),
            };
        }

        // ---------------------------------------------------------------- 경기 시뮬레이션

        private static readonly List<UnityEngine.Object> Temp = new List<UnityEngine.Object>();

        public static List<Player> ProceduralTeam(Team team, int teamOvr, string tag)
        {
            var roster = new List<Player>();
            int i = 0;
            PlayerTemplate T(bool pitcher, BatterPosition pos, PitcherRole role)
            {
                var t = ScriptableObject.CreateInstance<PlayerTemplate>();
                Temp.Add(t);
                t.TemplateId = $"{tag}_{team}_{i++}"; t.RealPlayerId = t.TemplateId; t.PlayerName = t.TemplateId;
                t.Team = team; t.Grade = Grade.LIVE_NORMAL; t.SeasonYear = 2026; t.IsPitcher = pitcher;
                if (pitcher) { t.PitcherRole = role; t.PitcherStats = StatProfiles.SpreadPitcher(teamOvr, role, t.TemplateId.GetHashCode()); }
                else { t.BatterPosition = pos; t.BatterStats = StatProfiles.SpreadBatter(teamOvr, pos, t.TemplateId.GetHashCode()); }
                return t;
            }
            foreach (BatterPosition pos in Enum.GetValues(typeof(BatterPosition))) roster.Add(new Player(Guid.NewGuid().ToString(), T(false, pos, default)));
            for (int b = 0; b < 6; b++) roster.Add(new Player(Guid.NewGuid().ToString(), T(false, BatterPosition.LeftField, default)));
            foreach (var (role, count) in RosterSlotLayout.PitcherRoleQuota)
                for (int k = 0; k < count; k++) roster.Add(new Player(Guid.NewGuid().ToString(), T(true, default, role)));
            LeagueManager.NormalizeRosterToTeamOvr(roster, teamOvr);
            return roster;
        }

        public static void CleanupTemp()
        {
            foreach (var o in Temp) if (o != null) UnityEngine.Object.DestroyImmediate(o);
            Temp.Clear();
        }

        /// <summary>상위 구단(baseOvr + gap) 승률(무승부 제외)과 무승부 수.</summary>
        public static (double winRate, int draws) SimulateGap(int baseOvr, int gap, int games, int seed)
        {
            var weak = ProceduralTeam(Team.Doosan, baseOvr, "W");
            var strong = ProceduralTeam(Team.LG, baseOvr + gap, "S");
            int weakOvr = TeamOvrCalculator.Calculate(weak).Total, strongOvr = TeamOvrCalculator.Calculate(strong).Total;
            int wins = 0, losses = 0, draws = 0;
            for (int g = 0; g < games; g++)
            {
                bool strongHome = g % 2 == 0;
                var home = strongHome ? strong : weak;
                var away = strongHome ? weak : strong;
                var hm = new TeamPowerModifiers(0, TeamPowerModifiers.HomeAdvantageConditionBuff, 1f, null, null, strongHome ? strongOvr : weakOvr);
                var am = new TeamPowerModifiers(0, 0, 1f, null, null, strongHome ? weakOvr : strongOvr);
                RestoreStamina(home); RestoreStamina(away);
                var result = new MatchEngine(home, away, hm, am, null, null, seed * 1000 + g).PlayFullMatch("H", "A");
                if (result.WinnerTeamName == null) draws++;
                else if ((result.WinnerTeamName == "H") == strongHome) wins++;
                else losses++;
            }
            CleanupTemp();
            return (wins + losses == 0 ? 0 : (double)wins / (wins + losses), draws);
        }

        /// <summary>약팀이 쓸 수 있는 가변 승부 도구(홈 + 치어리더 LEGEND 6인 시너지 + 2연패 대응 + 컨디션 최상)를 모두 쓴 약팀 승률.</summary>
        public static (double underdogWinRate, int draws) SimulateUnderdogWithTools(int weakOvr, int gap, int games, int seed)
        {
            var weak = ProceduralTeam(Team.Samsung, weakOvr, "U");
            var strong = ProceduralTeam(Team.KIA, weakOvr + gap, "F");
            foreach (var p in weak) p.CurrentCondition = PlayerCondition.Excellent;
            int wOvr = TeamOvrCalculator.Calculate(weak).Total, sOvr = TeamOvrCalculator.Calculate(strong).Total;
            var squad = Enumerable.Range(0, CheerSquad.SlotCount).Select(i => new Cheerleader
            {
                InstanceId = $"C{i}", Name = $"응원{i}", Grade = CheerleaderGrade.LEGEND, Team = Team.Samsung, ClutchMultiplier = 1.1f,
            }).ToList();
            var setDeck = SetDeckEvaluator.Evaluate(weak, Team.Samsung.ToString());
            int wins = 0, losses = 0, draws = 0;
            for (int g = 0; g < games; g++)
            {
                var cheer = CheerSquad.BuildEffects(squad, Team.Samsung, true, 2, setDeck.AllPlayersFlatBuff);
                var hm = new TeamPowerModifiers(0, TeamPowerModifiers.HomeAdvantageConditionBuff, 1f, null, cheer, wOvr);
                var am = new TeamPowerModifiers(0, 0, 1f, null, null, sOvr);
                RestoreStamina(weak); RestoreStamina(strong);
                var result = new MatchEngine(weak, strong, hm, am, null, null, seed * 1000 + g).PlayFullMatch("H", "A");
                if (result.WinnerTeamName == null) draws++;
                else if (result.WinnerTeamName == "H") wins++;
                else losses++;
            }
            CleanupTemp();
            return (wins + losses == 0 ? 0 : (double)wins / (wins + losses), draws);
        }

        private static void RestoreStamina(List<Player> roster)
        {
            foreach (var p in roster) if (p.Template.IsPitcher) p.RecoverStamina(1000);
        }
    }
}
