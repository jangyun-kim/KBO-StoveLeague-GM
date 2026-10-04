using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>[TASK-KBO-189] 완료 보고용 실측 리포트(Logs/Task189Report.txt) - 실제 카드 DB(SampleScene PlayerDatabase)로 각성 · 초월 · 방출 · 재조합 · 상점 · 리그 보상을 돌린다.</summary>
    public static class Task189Report
    {
        private static int seed = 189;

        [MenuItem("KBO Manager/Debug/TASK-189 Report")]
        public static void RunMenu() => Debug.Log(Build());

        public static void RunBatch()
        {
            string report = Build();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/Task189Report.txt", report, Encoding.UTF8);
            Debug.Log(report);
        }

        private static int Pick(int n)
        {
            seed = (seed * 1103515245 + 12345) & 0x7fffffff;
            return n <= 0 ? 0 : seed % n;
        }

        public static string Build()
        {
            var sb = new StringBuilder();
            seed = 189;
            try
            {
                var db = UnityEngine.Object.FindAnyObjectByType<PlayerDatabase>(FindObjectsInactive.Include);
                var templates = db != null ? db.AllTemplates.Where(t => t != null).ToList() : new List<PlayerTemplate>();
                sb.AppendLine($"=== TASK-189 실측 리포트 (카드 템플릿 {templates.Count:N0}장) ===");
                if (templates.Count == 0) { sb.AppendLine("PlayerDatabase 템플릿이 없어 실측을 건너뜁니다."); return sb.ToString(); }
                Player Issue(PlayerTemplate t) => new Player(Guid.NewGuid().ToString(), t);
                PlayerTemplate Find(Grade g, string pos, Func<PlayerTemplate, bool> extra = null) =>
                    templates.FirstOrDefault(t => t.Grade == g && CardGrowthRules.PositionKey(t) == pos && (extra == null || extra(t)));

                // ---- A. +10강 선행 · 같은 포지션 각성
                sb.AppendLine("\n[A] 각성 선행 조건 · 재료 판정");
                var gg = Find(Grade.GOLDEN_GLOVE, "RF") ?? templates.First(t => t.Grade == Grade.GOLDEN_GLOVE);
                string pos = CardGrowthRules.PositionKey(gg);
                var target = Issue(gg);
                target.ReinforceLevel = 9;
                var samePlayer = templates.FirstOrDefault(t => t != gg && t.Grade == gg.Grade && CardGrowthRules.IsSamePlayer(t, gg)) ?? gg;
                var samePos = Find(gg.Grade, pos, t => !CardGrowthRules.IsSamePlayer(t, gg));
                var otherPos = templates.FirstOrDefault(t => t.Grade == gg.Grade && CardGrowthRules.PositionKey(t) != pos && !CardGrowthRules.IsSamePlayer(t, gg));
                CardGrowthRules.CanAwakenNow(target, out var lockMsg);
                sb.AppendLine($"대상 {gg.PlayerName}'{gg.SeasonYear % 100:00} ({pos}) +9강 → \"{lockMsg}\" / ApplyAwaken={UpgradeManager.ApplyAwaken(target, new List<Player> { Issue(samePlayer) })}");
                target.ReinforceLevel = 10;
                var m1 = Issue(samePlayer); var m2 = samePos != null ? Issue(samePos) : null; var m3 = otherPos != null ? Issue(otherPos) : null;
                sb.AppendLine($"+10강: 같은 선수 {samePlayer.PlayerName}({CardGrowthRules.PositionKey(samePlayer)}) {CardGrowthRules.AwakenMaterialBadge(target, m1)} +{CardGrowthRules.AwakenGainFor(target, m1)} | " +
                              $"같은 포지션 {samePos?.PlayerName}({pos}) {CardGrowthRules.AwakenMaterialBadge(target, m2)} +{CardGrowthRules.AwakenGainFor(target, m2)} | " +
                              $"다른 포지션 {otherPos?.PlayerName}({CardGrowthRules.PositionKey(otherPos)}) +{CardGrowthRules.AwakenGainFor(target, m3)}(재료 불가)");
                UpgradeManager.ApplyAwaken(target, new List<Player> { m1, m2, m3 }.Where(x => x != null).ToList());
                sb.AppendLine($"재료 3장 투입 결과: {target.AwakenLabel} (3 + 1 + 0)");
                target.AwakenLevel = 8;
                UpgradeManager.ApplyAwaken(target, new List<Player> { Issue(samePlayer) });
                sb.AppendLine($"8각 + 같은 선수 → {target.AwakenLabel} (재료 각성은 9각에서 정지, 초월 아님: {!target.IsTranscended})");

                // ---- B. 초월
                sb.AppendLine("\n[B] 초월 복합 재료");
                var ledger = new MemoryGrowthLedger { FavoriteTeam = gg.Team, Points = 100000, Trophies = 3 };
                ledger.Cards.Add(target);
                ledger.Lineup.Add(target);
                var core = Issue(samePlayer);
                var supports = templates.Where(t => t.Grade == gg.Grade && CardGrowthRules.PositionKey(t) == pos && !CardGrowthRules.IsSamePlayer(t, gg)).Take(2).Select(Issue).ToList();
                ledger.Cards.Add(core);
                ledger.Cards.AddRange(supports);
                var only1 = TranscendRules.Assign(target, new[] { core }, false);
                sb.AppendLine($"핵심 1장만: {TranscendRules.Validate(target, only1, ledger)}");
                var auto = TranscendRules.AutoAssign(target, ledger.Cards, ledger.Lineup, 0);
                sb.AppendLine($"자동 등록: {TranscendRules.SlotSummary(target, auto)} → 검증 {TranscendRules.Validate(target, auto, ledger) ?? "통과"}");
                bool ok = TranscendRules.TryTranscend(target, auto, ledger, out var msg);
                sb.AppendLine($"실행 {ok}: {msg} | 잔여 {ledger.Points:N0}P · 트로피 {ledger.Trophies} · 카드 {ledger.Cards.Count}장 · {target.AwakenLabel}");
                var live = Issue(templates.First(t => t.Grade == Grade.LIVE_NORMAL));
                live.ReinforceLevel = 10; live.AwakenLevel = 9;
                var liveSupport = Issue(templates.First(t => t.Grade == Grade.LIVE_NORMAL && CardGrowthRules.IsSamePosition(t, live.Template) && !CardGrowthRules.IsSamePlayer(t, live.Template)));
                liveSupport.ReinforceLevel = 5;
                var liveLedger = new MemoryGrowthLedger { Points = 30000, Trophies = 1, TranscendTicket = 1 };
                liveLedger.Cards.AddRange(new[] { live, liveSupport });
                var liveSel = TranscendRules.Assign(live, new[] { liveSupport }, true);
                ok = TranscendRules.TryTranscend(live, liveSel, liveLedger, out msg);
                sb.AppendLine($"LIVE 대체권 + 같은 포지션 +5강 1장: {ok} - {msg}");

                // ---- C. 방출 · 재조합 · 상점 · 리그 보상
                sb.AppendLine("\n[C] 컴프야V26 반영 시스템");
                var shop = new MemoryGrowthLedger { FavoriteTeam = Team.Samsung };
                var junk = new[] { Grade.LIVE_NORMAL, Grade.LIVE_EPIC, Grade.ALLSTAR, Grade.GOLDEN_GLOVE }
                    .Select(g => Issue(templates.First(t => t.Grade == g))).ToList();
                junk[3].ReinforceLevel = 5;
                shop.Cards.AddRange(junk);
                ShopExchangeRules.TryRelease(junk, shop, out int pts, out int coins, out msg);
                sb.AppendLine($"선수 방출 4장(LIVE · 에픽 · 올스타 · GG+5강): {msg}");
                var allstars = templates.Where(t => t.Grade == Grade.ALLSTAR).Take(3).Select(Issue).ToList();
                shop.Cards.AddRange(allstars);
                ok = ShopExchangeRules.TryRecombine(allstars, "SS", shop, templates, Pick, Issue, out var recombined, out msg);
                sb.AppendLine($"3:1 재조합(올스타 {string.Join("/", allstars.Select(a => CardGrowthRules.PositionKey(a)))} → SS): {msg}");
                shop.Points = 15000; shop.GrowthCoin = 3100;
                foreach (var product in ShopExchangeRules.PointProducts.Concat(ShopExchangeRules.CoinProducts))
                {
                    var grade = product == ShopProduct.SpecialPositionPack ? Grade.TITLE_HOLDER : Grade.ALLSTAR;
                    ShopExchangeRules.TryBuy(product, shop, "SS", grade, templates, Pick, Issue, out _, out msg);
                    sb.AppendLine($"  {ShopExchangeRules.PriceLabel(product, grade),-10} {msg}");
                }
                sb.AppendLine($"구매 후 잔액: {shop.Points:N0}P · 코인 {shop.GrowthCoin:N0} · 보조권 {shop.AwakenTicket} · 대체권 {shop.TranscendTicket} · 특훈권 {shop.TrainingTicket} · 카드 {shop.Cards.Count}장");

                sb.AppendLine("\n[D] 리그 단계별 재료 보상(승리 기준 · 확률 보상 제외 / 우승)");
                foreach (var info in LeagueTierTable.All.Where(t => t.Tier != LeagueTier.Rookie))
                {
                    var win = LeagueMaterialRewards.ForMatch(info.Tier, true, null);
                    var champ = LeagueMaterialRewards.ForSeason(info.Tier, 1);
                    sb.AppendLine($"  {info.Name,-10} 승리 {win.Summary()} (특훈 {LeagueMaterialRewards.TrainingChancePercent(info.Tier, true)}% · 트로피 {LeagueMaterialRewards.TrophyChancePercent(info.Tier, true)}%) | 우승 {champ.Summary()}");
                }

                // ---- E. 씬
                var hub = UnityEngine.Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
                var growth = UnityEngine.Object.FindAnyObjectByType<GrowthCenterView>(FindObjectsInactive.Include);
                sb.AppendLine($"\n[E] 씬: 상점 · 교환소 섹션 {(hub != null && hub.ShopSection != null ? hub.ShopSection.name : "없음")} / " +
                              $"성장 센터 초월 탭 {(growth != null && growth.transform.Find(GrowthCenterView.RootName + "/Tab_Transcend") != null ? "있음" : "없음")}");
            }
            catch (Exception e)
            {
                sb.AppendLine("[리포트 오류] " + e);
            }
            return sb.ToString();
        }
    }
}
