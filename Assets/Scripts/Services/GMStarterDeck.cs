using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;

namespace KBOManager.Services
{
    /// <summary>[TASK-GM-16] 스타터 덱 등급(카드 등급 → 5단계).</summary>
    public enum GMStarterTier { S = 0, A = 1, B = 2, C = 3, D = 4 }

    /// <summary>
    /// [TASK-GM-16] 초기 구단 로스터 '스타터 덱'(리세마라) - 새 게임마다 10구단 28인 자리를 구단 계보의 역대 전 연도(1986~2026) 카드 풀에서 확률 추첨한다.
    ///   등급 확률(슬롯마다 독립 추첨): S 4%(골든글러브 · 왕조 · 시그니처 · 영구결번) · A 10%(타이틀 홀더 · 프랜차이즈) · B 25%(올스타) ·
    ///   C 45%(라이브 에픽 + 라이브 일반 OVR 57 이상 = 1군 레귤러) · D 16%(라이브 일반 OVR 56 이하 = 백업/유망주).
    ///   슬롯 = 수비 위치 주전 9 → 벤치 타자 6 → 선발 5 → 마무리 1 → 불펜 7(로더 PickRoster와 같은 골격).
    ///   후보가 없으면 ① 같은 등급 · 다른 계보 → ② 가까운 등급(한 단계씩, 아래 등급 먼저) 순으로 대체한다(같은 인물은 리그 전체에서 1번만).
    ///   나이는 그 카드 시즌 기준(21 + 카드 연도 - 데뷔 연도) - 과거 시즌 카드를 그 시절 그대로 데려온다. 능력치는 BaseOverall(카드) 그대로.
    /// </summary>
    public static class GMStarterDeck
    {
        public static readonly int[] TierPercent = { 4, 10, 25, 45, 16 };
        public static readonly string[] TierNames = { "S", "A", "B", "C", "D" };
        public static readonly string[] TierLabels = { "S급 · 골든글러브급", "A급 · 프랜차이즈/타이틀 홀더급", "B급 · 올스타급", "C급 · 1군 레귤러급", "D급 · 백업/유망주급" };

        public static string TierName(GMStarterTier t) => TierNames[(int)t];
        public static string TierLabel(GMStarterTier t) => TierLabels[(int)t];

        public static GMStarterTier TierOf(Grade g)
        {
            switch (g)
            {
                case Grade.GOLDEN_GLOVE: case Grade.DYNASTY: case Grade.SIGNATURE: case Grade.RETIRED_NUMBER: return GMStarterTier.S;
                case Grade.TITLE_HOLDER: case Grade.FRANCHISE: return GMStarterTier.A;
                case Grade.ALLSTAR: return GMStarterTier.B;
                case Grade.LIVE_EPIC: return GMStarterTier.C;
                default: return GMStarterTier.D;
            }
        }

        /// <summary>
        /// 라이브(2026 현역) 카드 1군 레귤러 기준 OVR - 라이브 에픽 카드는 75장뿐이라(C급 45%를 못 채운다) 라이브 일반 카드도 OVR 57 이상이면 C급(1군 레귤러),
        /// 그 미만(라이브 카드 하위 약 26%)이 D급(백업/유망주)이다.
        /// </summary>
        public const int LiveRegularMinOvr = 57;

        public static GMStarterTier TierOf(PlayerTemplate t)
        {
            if (t == null) return GMStarterTier.D;
            var o = GMRosterTiers.OriginalOf(t);
            if (o.Grade == Grade.LIVE_NORMAL) return o.GetBaseOverall() >= LiveRegularMinOvr ? GMStarterTier.C : GMStarterTier.D;
            return TierOf(o.Grade);
        }
        public static GMStarterTier TierOf(Player p) => TierOf(p?.Template);

        /// <summary>0~99 주사위 → 등급(4/10/25/45/16 누적).</summary>
        public static GMStarterTier Roll(Random rng)
        {
            int roll = rng.Next(100), acc = 0;
            for (int i = 0; i < TierPercent.Length; i++)
            {
                acc += TierPercent[i];
                if (roll < acc) return (GMStarterTier)i;
            }
            return GMStarterTier.D;
        }

        /// <summary>새 게임 시드(리세마라 - 매번 다른 덱). 0은 쓰지 않는다.</summary>
        public static int NewSeed() => (Environment.TickCount ^ Guid.NewGuid().GetHashCode()) & 0x7FFFFFFF | 1;

        /// <summary>추첨 기록 한 줄(검증 · 화면 표시).</summary>
        public sealed class Draw
        {
            public string TeamCode;
            public string Slot;
            public GMStarterTier Rolled, Actual;
            public PlayerTemplate Card;
            public bool OwnLineage;
        }

        /// <summary>직전 LoadModeRoster의 스타터 덱 추첨 기록(10구단 · 280장).</summary>
        public static readonly List<Draw> LastDraws = new List<Draw>();

        private sealed class Slot
        {
            public string Name;
            public Func<PlayerTemplate, bool> Filter;
        }

        private static readonly BatterPosition[] Positions =
        {
            BatterPosition.Catcher, BatterPosition.FirstBase, BatterPosition.SecondBase, BatterPosition.ThirdBase, BatterPosition.ShortStop,
            BatterPosition.LeftField, BatterPosition.CenterField, BatterPosition.RightField, BatterPosition.DesignatedHitter,
        };

        private static List<Slot> Slots()
        {
            var slots = new List<Slot>();
            foreach (var pos in Positions) { var p = pos; slots.Add(new Slot { Name = p.ToString(), Filter = t => !t.IsPitcher && (t.BatterPosition == p || p == BatterPosition.DesignatedHitter) }); }
            for (int i = 0; i < GMRosterLoader.BatterCount - Positions.Length; i++) slots.Add(new Slot { Name = "Bench", Filter = t => !t.IsPitcher });
            for (int i = 0; i < GMRosterLoader.StartingPitcherCount; i++) slots.Add(new Slot { Name = "SP", Filter = t => t.IsPitcher && t.PitcherRole == PitcherRole.StartingPitcher });
            slots.Add(new Slot { Name = "CL", Filter = t => t.IsPitcher && t.PitcherRole == PitcherRole.Closer });
            for (int i = 0; i < GMRosterLoader.PitcherCount - GMRosterLoader.StartingPitcherCount - 1; i++) slots.Add(new Slot { Name = "RP", Filter = t => t.IsPitcher && t.PitcherRole != PitcherRole.StartingPitcher });
            return slots;
        }

        public static string LineageOf(PlayerTemplate t) => t == null ? null : NameAliasTable.ToCode(t.Team != Team.None ? t.Team : t.CurrentTeam);

        /// <summary>
        /// 10구단 스타터 덱 추첨. 반환 = 구단 코드 → (카드, 기준 시즌) 28장. used = 이미 배정된 인물(RealPlayerId) - 추첨한 인물을 더한다.
        /// </summary>
        public static Dictionary<string, List<GMRosterLoader.Candidate>> DrawAll(IReadOnlyList<PlayerTemplate> cards, int seed, HashSet<string> used)
        {
            LastDraws.Clear();
            var result = new Dictionary<string, List<GMRosterLoader.Candidate>>();
            var valid = cards.Where(t => t != null && !string.IsNullOrEmpty(t.RealPlayerId) && LineageOf(t) != null).ToList();
            // 계보 → 등급 → 카드
            var byLineage = valid.GroupBy(LineageOf).ToDictionary(g => g.Key, g => g.GroupBy(TierOf).ToDictionary(x => x.Key, x => x.OrderBy(t => t.TemplateId, StringComparer.Ordinal).ToList()));
            var byTier = valid.GroupBy(TierOf).ToDictionary(g => g.Key, g => g.OrderBy(t => t.TemplateId, StringComparer.Ordinal).ToList());
            var slots = Slots();
            foreach (var code in NameAliasTable.CanonicalTeamCodes)
            {
                var rng = new Random(seed ^ (GMFrontOffice.Hash("starter_" + code) * 397));
                var picks = new List<GMRosterLoader.Candidate>();
                byLineage.TryGetValue(code, out var own);
                foreach (var slot in slots)
                {
                    var rolled = Roll(rng);
                    PlayerTemplate card = null;
                    bool ownLineage = false;
                    foreach (var tier in TierOrder(rolled))
                    {
                        card = PickFrom(own != null && own.TryGetValue(tier, out var l1) ? l1 : null, slot.Filter, used, rng);
                        if (card != null) { ownLineage = true; break; }
                        card = PickFrom(byTier.TryGetValue(tier, out var l2) ? l2 : null, slot.Filter, used, rng);
                        if (card != null) break;
                    }
                    if (card == null) // 슬롯 조건을 만족하는 카드가 전혀 없으면 같은 유형(타자/투수) 아무나
                    {
                        bool pitcher = slot.Name == "SP" || slot.Name == "CL" || slot.Name == "RP";
                        card = PickFrom(valid, t => t.IsPitcher == pitcher, used, rng);
                    }
                    if (card == null) continue;
                    used.Add(card.RealPlayerId);
                    picks.Add(new GMRosterLoader.Candidate { Template = card, Score = card.GetBaseOverall(), SeasonYear = card.SeasonYear });
                    LastDraws.Add(new Draw { TeamCode = code, Slot = slot.Name, Rolled = rolled, Actual = TierOf(card), Card = card, OwnLineage = ownLineage });
                }
                result[code] = picks;
            }
            return result;
        }

        /// <summary>대체 순서 - 굴린 등급 → 한 단계 아래 → 한 단계 위 → 두 단계 아래 …</summary>
        public static IEnumerable<GMStarterTier> TierOrder(GMStarterTier rolled)
        {
            int r = (int)rolled;
            yield return rolled;
            for (int d = 1; d <= 4; d++)
            {
                if (r + d <= 4) yield return (GMStarterTier)(r + d);
                if (r - d >= 0) yield return (GMStarterTier)(r - d);
            }
        }

        private static PlayerTemplate PickFrom(List<PlayerTemplate> list, Func<PlayerTemplate, bool> filter, HashSet<string> used, Random rng)
        {
            if (list == null || list.Count == 0) return null;
            // 무작위 시작점에서 한 바퀴 - 전체 필터링보다 가볍다(같은 시드 = 같은 결과)
            int start = rng.Next(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[(start + i) % list.Count];
                if (!used.Contains(t.RealPlayerId) && filter(t)) return t;
            }
            return null;
        }

        /// <summary>추첨 결과 요약(등급별 장수 · 비율).</summary>
        public static string Summary(IEnumerable<Draw> draws)
        {
            var list = draws.ToList();
            if (list.Count == 0) return "스타터 덱 없음";
            return string.Join(" · ", Enumerable.Range(0, 5).Select(i => $"{TierNames[i]} {list.Count(d => (int)d.Actual == i)}장({100.0 * list.Count(d => (int)d.Actual == i) / list.Count:0}%)"));
        }
    }
}
