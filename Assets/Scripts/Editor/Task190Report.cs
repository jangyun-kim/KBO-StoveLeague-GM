using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KBOManager.Controllers;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-190] 완료 보고용 실측 리포트(Logs/Task190Report.txt).
    ///   [A] 144경기 완주 → 포스트시즌 → 타이틀 시상식 → 리그 승격 → 새 시즌 초기화 · AI 구단 전력 재조정(실제 매니저 코드, 헤드리스)
    ///   [B] 3슬롯 스킬 - 풀 · 등급 확률 · 스킬 변경 / 고급 변경 / 레벨업 · 상시/조건부 스탯 · 경기 엔진 판정 차이
    ///   [C] 빠른 진행 30경기 / 시즌 완주 프리셋 · 상점 스킬 변경권 구매 · 리그 보상
    /// </summary>
    public static class Task190Report
    {
        [MenuItem("KBO Manager/Debug/TASK-190 Report")]
        public static void RunMenu() => Debug.Log(Build());

        public static void RunBatch()
        {
            string report = Build();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/Task190Report.txt", report, Encoding.UTF8);
            Debug.Log(report);
        }

        public static string Build()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== TASK-190 실측 리포트 ===");
            try { SeasonSection(sb); } catch (Exception e) { sb.AppendLine("[A 오류] " + e); }
            try { SkillSection(sb); } catch (Exception e) { sb.AppendLine("[B 오류] " + e); }
            try { ShopSection(sb); } catch (Exception e) { sb.AppendLine("[C 오류] " + e); }
            return sb.ToString();
        }

        private static void SeasonSection(StringBuilder sb)
        {
            sb.AppendLine("\n[A] 시즌 완주 루프 (유저 구단 OVR 75, 아마추어 리그 57~64)");
            using (var h = SeasonCycleHarness.Create(75, LeagueTier.Amateur, Team.Samsung, "R190"))
            {
                var aiBefore = h.AiTeamOvrs();
                int cards = h.Gm.Inventory.Count;
                var star = h.Gm.Roster.First();
                star.ReinforceLevel = 7;
                var starSlots = string.Join(" / ", star.SkillSlots.Select(PlayerSkillRules.SlotLabel));
                var t0 = DateTime.Now;
                h.League.PlaySeasonToCompletion();
                sb.AppendLine($"정규시즌 {h.League.PlayedGameCount}/{LeagueManager.TotalUserGames}경기 완주 ({(DateTime.Now - t0).TotalSeconds:F1}초) · 시즌 종료 판정 {SeasonCycle.IsSeasonOver(h.League)} · 단계 {h.League.CurrentPhase}");
                foreach (var line in SeasonCycleView.StandingsText(h.League).Split('\n').Where(l => l.Length > 0))
                    sb.AppendLine("  " + System.Text.RegularExpressions.Regex.Replace(line, "<.*?>", ""));
                int trophy = h.Gm.Trophy, coin = h.Gm.GrowthCoin, gold = h.Gm.GameGold;
                var report = SeasonCycle.CompleteSeason(h.League, h.Post, h.Reward);
                sb.AppendLine($"포스트시즌 우승: {(h.Post.ChampionTeam.HasValue ? h.Post.ChampionTeam.Value.ToString() : "-")} · 최종 {report.FinalRank}위 (정규시즌 {report.RegularSeasonRank}위)");
                sb.AppendLine($"리그 재료 보상: {report.MaterialReward.Summary()}");
                sb.AppendLine("타이틀 시상식:");
                foreach (var t in report.Titles) sb.AppendLine("  " + t.Line);
                sb.AppendLine($"우리 구단 수상 보너스: {(report.TitleBonus.IsEmpty ? "없음" : report.TitleBonus.Summary())} → 트로피 {trophy}→{h.Gm.Trophy} · 코인 {coin}→{h.Gm.GrowthCoin} · 포인트 {gold:N0}→{h.Gm.GameGold:N0}");
                int goldAfter = h.Gm.GameGold;
                h.Reward.GrantSeasonEndRewardOnce();
                sb.AppendLine($"결산 재호출(중복 지급 방지): 포인트 {goldAfter:N0} → {h.Gm.GameGold:N0}");
                sb.AppendLine($"승격 예정: {report.PromotionEarned} ({LeagueTierTable.DisplayName(report.Tier)} → {LeagueTierTable.DisplayName(report.NextTier)})");

                h.Rollover.RolloverToNextSeason();
                var aiAfter = h.AiTeamOvrs();
                sb.AppendLine($"[다음 시즌 시작] → {LeagueTierTable.DisplayName(h.League.CurrentTier)} (권장 {LeagueTierTable.Get(h.League.CurrentTier).RangeLabel}) · LastPromotion {h.League.LastPromotion}");
                sb.AppendLine($"  AI 9개 구단 OVR: 이전 {string.Join("·", aiBefore)} → 이후 {string.Join("·", aiAfter)} (목표 {string.Join("·", LeagueTierTable.AiTeamOvrTargets(h.League.CurrentTier))})");
                sb.AppendLine($"  초기화: 경기 {h.League.PlayedGameCount}/144 · 다음 경기 #{h.League.PeekNextFixture()?.GameNumber} · 10개 구단 승무패 합 {h.League.GetStandings().Sum(t => t.Wins + t.Draws + t.Losses)} · 시즌 기록 {h.Stats.AllBatterStats.Count + h.Stats.AllPitcherStats.Count}명 · 단계 {h.League.CurrentPhase}");
                sb.AppendLine($"  보존: 카드 {cards}→{h.Gm.Inventory.Count}장 · 라인업 {h.Gm.Roster.Count}명 · 대표 선수 {star.ReinforceLevel}강 · 스킬 {starSlots} = {string.Join(" / ", star.SkillSlots.Select(PlayerSkillRules.SlotLabel))} · 트로피 {h.Gm.Trophy} · 포인트 {h.Gm.GameGold:N0}");
            }
        }

        private static void SkillSection(StringBuilder sb)
        {
            sb.AppendLine("\n[B] 선수 3슬롯 스킬");
            sb.AppendLine("타자 풀: " + string.Join(", ", PlayerSkillRules.BatterSkills.Select(s => $"{s.Name}({PlayerSkillRules.TriggerLabel(s.Trigger)})")));
            sb.AppendLine("투수 풀: " + string.Join(", ", PlayerSkillRules.PitcherSkills.Select(s => $"{s.Name}({PlayerSkillRules.TriggerLabel(s.Trigger)})")));
            var rng = new System.Random(190);
            foreach (var g in new[] { Grade.LIVE_NORMAL, Grade.ALLSTAR, Grade.GOLDEN_GLOVE, Grade.SIGNATURE })
            {
                var counts = new int[5];
                for (int i = 0; i < 5000; i++) counts[(int)PlayerSkillRules.RollGrade(g, n => rng.Next(n))]++;
                sb.AppendLine($"  {CardGrowthRules.DisplayName(g),-8} 등급 분포(5,000회) D {counts[0] / 50.0:F1}% · C {counts[1] / 50.0:F1}% · B {counts[2] / 50.0:F1}% · A {counts[3] / 50.0:F1}% · S {counts[4] / 50.0:F1}%");
            }
            var team = Task183Report.ProceduralTeam(Team.LG, 80, "SK190");
            var batter = team.First(p => !p.Template.IsPitcher);
            PlayerSkillRules.EnsureSlots(batter);
            var ledger = new MemoryGrowthLedger { Points = 40000, SkillChangeTicket = 1, PremiumSkillChangeTicket = 1, TrainingTicket = 3 };
            sb.AppendLine($"대상 {batter.Template.PlayerName}: {string.Join(" / ", batter.SkillSlots.Select(PlayerSkillRules.SlotLabel))}");
            PlayerSkillRules.TryReroll(batter, ledger, false, n => rng.Next(n), out var msg); sb.AppendLine("  [스킬 변경] " + msg);
            PlayerSkillRules.TryReroll(batter, ledger, false, n => rng.Next(n), out msg); sb.AppendLine("  [스킬 변경 - 변경권 소진 후 15,000P] " + msg);
            PlayerSkillRules.TryReroll(batter, ledger, true, n => 0, out msg); sb.AppendLine("  [고급 스킬 변경 - 최악 난수에서도 A~S 1슬롯] " + msg);
            for (int i = 0; i < 6; i++) { PlayerSkillRules.TryLevelUp(batter, 0, ledger, out msg); sb.AppendLine("  [레벨업] " + msg); }
            sb.AppendLine($"  잔여: 포인트 {ledger.Points:N0} · 변경권 {ledger.SkillChangeTicket} · 고급 {ledger.PremiumSkillChangeTicket} · 특훈권 {ledger.TrainingTicket}");
            var rows = PlayerDetailRules.StatRows(batter, 0);
            sb.AppendLine("  상세창 능력치: " + string.Join(" · ", rows.Select(r => $"{r.Label} {r.Final}(스킬 +{r.Skill})")));

            // 경기 엔진 판정 차이(같은 시드): 스킬 없음 vs S Lv.6 3슬롯
            var opp = Task183Report.ProceduralTeam(Team.KIA, 80, "SK190O");
            var pitcher = opp.First(p => p.Template.IsPitcher);
            double Rate(List<PlayerSkillSlot> slots)
            {
                batter.SkillSlots = slots;
                var engine = new MatchEngine(team, opp, TeamPowerModifiers.None, TeamPowerModifiers.None, null, null, 190);
                int positive = 0;
                for (int i = 0; i < 4000; i++)
                {
                    var r = engine.SimulateAtBat(batter, pitcher, new MatchState());
                    if (r == AtBatResult.Single || r == AtBatResult.Double || r == AtBatResult.Triple || r == AtBatResult.HomeRun || r == AtBatResult.Walk) positive++;
                }
                return positive / 40.0;
            }
            var none = new List<PlayerSkillSlot>();
            var best = new List<PlayerSkillSlot>
            {
                new PlayerSkillSlot { SkillId = "batting_machine", Grade = SkillGrade.S, Level = 6 },
                new PlayerSkillSlot { SkillId = "power_instinct", Grade = SkillGrade.S, Level = 6 },
                new PlayerSkillSlot { SkillId = "table_setter", Grade = SkillGrade.S, Level = 6 },
            };
            double a = Rate(none), b = Rate(best);
            sb.AppendLine($"  경기 판정(4,000타석, 동일 시드): 스킬 없음 출루 {a:F1}% → S Lv.6 3슬롯 {b:F1}% (+{b - a:F1}%p)");
            Task183Report.CleanupTemp();
        }

        private static void ShopSection(StringBuilder sb)
        {
            sb.AppendLine("\n[C] 빠른 진행 · 상점 · 리그 보상");
            sb.AppendLine($"빠른 진행 프리셋: {string.Join(" / ", MatchModeRules.QuickCountPresets.Select(MatchModeRules.PresetLabel))} · 남은 87경기에서 30경기 → {MatchModeRules.ClampQuickCount(30, 87)}, 시즌 완주 → {MatchModeRules.ClampQuickCount(MatchModeRules.SeasonAll, 87)}");
            int started = 0;
            QuickSeriesSummary done = null;
            QuickSeriesRunner runner = null;
            runner = new QuickSeriesRunner(() => { started++; runner.HandleMatchCompleted(new MatchResult { HomeTeamName = "Samsung", AwayTeamName = "KIA", WinnerTeamName = "Samsung" }, Team.Samsung); },
                () => true, null, null, null);
            runner.Finished += s => done = s;
            runner.Begin(MatchModeRules.SeasonAll, 87);
            sb.AppendLine($"시즌 완주 연속 진행(가상 87경기): {started}경기 시작 · {done?.Label}");
            var ledger = new MemoryGrowthLedger { Points = 20000, GrowthCoin = 1000, Trophies = 1 };
            foreach (var p in new[] { ShopProduct.SkillChangeTicket, ShopProduct.SkillChangeTicketCoin, ShopProduct.PremiumSkillChangeTicket, ShopProduct.PremiumSkillChangeTicketTrophy, ShopProduct.PremiumSkillChangeTicketTrophy })
            {
                ShopExchangeRules.TryBuy(p, ledger, "RF", Grade.ALLSTAR, null, null, null, out _, out var msg);
                sb.AppendLine($"  {ShopExchangeRules.PriceLabel(p),-10} {msg}");
            }
            sb.AppendLine($"  잔액 {ledger.Points:N0}P · 코인 {ledger.GrowthCoin} · 트로피 {ledger.Trophies} · 스킬 변경권 {ledger.SkillChangeTicket} · 고급 {ledger.PremiumSkillChangeTicket}");
            foreach (var tier in new[] { LeagueTier.Amateur, LeagueTier.GoldenGlove, LeagueTier.HallOfFame })
                sb.AppendLine($"  {LeagueTierTable.DisplayName(tier),-10} 승리 스킬 변경권 {LeagueMaterialRewards.SkillTicketChancePercent(tier, true)}% | 우승 {LeagueMaterialRewards.ForSeason(tier, 1).Summary()} | 준우승 {LeagueMaterialRewards.ForSeason(tier, 2).Summary()}");
        }
    }
}
