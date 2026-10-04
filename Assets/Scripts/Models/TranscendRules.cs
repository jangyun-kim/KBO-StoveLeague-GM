using System;
using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-189] 초월(9각 → 초월) 복합 재료 규칙 - 단일 재료 1장 초월을 폐지하고 아래를 동시에 요구한다.
    ///   선행 조건: 초월 가능 등급(LIVE · GG · SIG · DYN · RN) + 강화 +10강 + 9각.
    ///   슬롯 1(핵심): 같은 시즌 등급 · 같은 선수 1장 (또는 초월 핵심 대체권 1개).
    ///   슬롯 2(보조): 같은 시즌 등급 · 같은 포지션 · 다른 선수 2장 - 그중 +5강 이상 카드가 있으면 1장으로 충족.
    ///   비용: 포인트(LIVE 30,000 / AS·FRA·TH 60,000 / GG·SIG·DYN·RN 100,000) + 트로피(LIVE 1 / 스페셜 2 / GG 이상 3).
    /// 재료는 주전 라인업 카드를 쓰지 않으며 한 카드는 한 슬롯에만 들어간다. 실패하면 아무것도 바꾸지 않는다.
    /// </summary>
    public static class TranscendRules
    {
        public const int SupportCount = 2;
        public const int SupportEnhancedLevel = 5;

        public sealed class Selection
        {
            public Player Core;
            public bool UseCoreTicket;
            public readonly List<Player> Support = new List<Player>();
            public IEnumerable<Player> Cards => (Core != null && !UseCoreTicket ? new[] { Core } : Array.Empty<Player>()).Concat(Support);
        }

        public static bool IsSpecialGrade(Grade g) => g == Grade.ALLSTAR || g == Grade.FRANCHISE || g == Grade.TITLE_HOLDER;

        public static int PointCost(Grade g) => CardGrowthRules.IsLive(g) ? 30000 : IsSpecialGrade(g) ? 60000 : 100000;

        public static int TrophyCost(Grade g) => CardGrowthRules.IsLive(g) ? 1 : IsSpecialGrade(g) ? 2 : 3;

        public static string CostLabel(Grade g) => $"{PointCost(g):N0}P + 트로피 {TrophyCost(g)}개";

        /// <summary>초월 시도 가능 여부(재료 무관) - 불가하면 사유.</summary>
        public static bool CanAttempt(Player target, out string reason)
        {
            reason = null;
            if (target?.Template == null) { reason = "선수 카드가 없습니다."; return false; }
            var g = target.Template.Grade;
            if (!CardGrowthRules.CanTranscend(g)) { reason = $"{CardGrowthRules.DisplayName(g)} 등급은 9각이 한계입니다(초월 불가 등급)."; return false; }
            if (CardGrowthRules.IsTranscended(g, target.AwakenLevel)) { reason = "이미 초월한 카드입니다."; return false; }
            if (target.ReinforceLevel < CardGrowthRules.AwakenRequiredReinforce)
            {
                reason = $"초월은 강화 +10강 · 9각 달성 후 가능합니다 (현재 +{target.ReinforceLevel}/10강)";
                return false;
            }
            if (target.AwakenLevel < CardGrowthRules.FinalAwakenThreshold)
            {
                reason = $"초월은 9각 달성 후 가능합니다 (현재 {CardGrowthRules.AwakenLabel(g, target.AwakenLevel)})";
                return false;
            }
            return true;
        }

        public static bool IsCoreMaterial(Player target, Player m) =>
            target?.Template != null && m?.Template != null && !ReferenceEquals(target, m)
            && m.Template.Grade == target.Template.Grade && CardGrowthRules.IsSamePlayer(target.Template, m.Template);

        public static bool IsSupportMaterial(Player target, Player m) =>
            target?.Template != null && m?.Template != null && !ReferenceEquals(target, m)
            && m.Template.Grade == target.Template.Grade && !CardGrowthRules.IsSamePlayer(target.Template, m.Template)
            && CardGrowthRules.IsSamePosition(target.Template, m.Template);

        /// <summary>보조 슬롯 충족 - 2장, 또는 +5강 이상 1장.</summary>
        public static bool SupportSatisfied(IReadOnlyCollection<Player> support) =>
            support != null && (support.Count >= SupportCount || support.Any(p => p != null && p.ReinforceLevel >= SupportEnhancedLevel));

        /// <summary>재료 배지("[핵심 · 같은 선수]" / "[보조 · 같은 포지션 +5강 1장 충족]" / "[보조 · 같은 포지션]").</summary>
        public static string MaterialBadge(Player target, Player m)
        {
            if (IsCoreMaterial(target, m)) return "[핵심 · 같은 선수]";
            if (IsSupportMaterial(target, m)) return m.ReinforceLevel >= SupportEnhancedLevel ? "[보조 · +5강 1장 충족]" : "[보조 · 같은 포지션]";
            return "";
        }

        /// <summary>선택한 카드 목록을 슬롯에 배정한다(핵심 1 · 보조 최대 2, +5강 카드가 들어오면 그 1장만으로 충족).</summary>
        public static Selection Assign(Player target, IEnumerable<Player> picked, bool useCoreTicket)
        {
            var s = new Selection { UseCoreTicket = useCoreTicket };
            foreach (var m in picked ?? Enumerable.Empty<Player>())
            {
                if (!useCoreTicket && s.Core == null && IsCoreMaterial(target, m)) { s.Core = m; continue; }
                if (IsSupportMaterial(target, m) && s.Support.Count < SupportCount && !s.Support.Contains(m)) s.Support.Add(m);
            }
            return s;
        }

        /// <summary>[초월 재료 자동 등록] - 라인업 · 대상 제외. 핵심 = 같은 선수 중 가장 아까운 덜한 카드(없으면 대체권),
        /// 보조 = 일반 카드 2장 우선, 2장이 안 되면 +5강 카드 1장.</summary>
        public static Selection AutoAssign(Player target, IEnumerable<Player> inventory, IEnumerable<Player> roster, int coreTickets, bool preferTicket = false)
        {
            var s = new Selection();
            if (target?.Template == null) return s;
            var locked = new HashSet<Player>(roster ?? Enumerable.Empty<Player>());
            var pool = (inventory ?? Enumerable.Empty<Player>()).Where(p => p?.Template != null && p != target && !locked.Contains(p)).Distinct().ToList();
            Func<Player, int> value = p => p.ReinforceLevel * 4 + p.AwakenLevel * 6 + p.LimitBreakLevel + p.TrainingLevel;

            var core = pool.Where(p => IsCoreMaterial(target, p)).OrderBy(value).ThenBy(p => p.CalculateNeutralOVR()).FirstOrDefault();
            if ((preferTicket || core == null) && coreTickets > 0) s.UseCoreTicket = true;
            else s.Core = core;

            var support = pool.Where(p => IsSupportMaterial(target, p)).ToList();
            var plain = support.Where(p => p.ReinforceLevel < SupportEnhancedLevel).OrderBy(value).ThenBy(p => p.CalculateNeutralOVR()).ToList();
            var enhanced = support.Where(p => p.ReinforceLevel >= SupportEnhancedLevel).OrderBy(value).ThenBy(p => p.CalculateNeutralOVR()).ToList();
            if (plain.Count >= SupportCount) s.Support.AddRange(plain.Take(SupportCount));
            else if (enhanced.Count > 0) s.Support.Add(enhanced[0]);
            else s.Support.AddRange(plain);
            return s;
        }

        /// <summary>초월 실행 가능 여부 - 불가하면 사유, 가능하면 null.</summary>
        public static string Validate(Player target, Selection selection, IGrowthLedger ledger)
        {
            if (ledger == null) return "게임 데이터가 없습니다.";
            if (!CanAttempt(target, out var reason)) return reason;
            selection ??= new Selection();
            var owned = new HashSet<Player>(ledger.Inventory ?? Array.Empty<Player>());
            var lineup = new HashSet<Player>(ledger.Roster ?? Array.Empty<Player>());
            if (selection.UseCoreTicket)
            {
                if (ledger.TranscendTicket < 1) return "[슬롯 1 핵심] 초월 핵심 대체권이 없습니다.";
            }
            else
            {
                var core = selection.Core;
                if (core == null) return "[슬롯 1 핵심] 같은 시즌 · 같은 선수 카드 1장(또는 초월 핵심 대체권)이 필요합니다.";
                if (!IsCoreMaterial(target, core)) return $"[슬롯 1 핵심] {core.Template?.PlayerName}: 같은 시즌 등급 · 같은 선수가 아닙니다.";
                if (!owned.Contains(core)) return "[슬롯 1 핵심] 보유하지 않은 카드입니다.";
                if (lineup.Contains(core)) return $"[슬롯 1 핵심] 주전 라인업 카드({core.Template.PlayerName})는 재료로 쓸 수 없습니다.";
            }
            var seen = new HashSet<Player>();
            foreach (var m in selection.Support)
            {
                if (!IsSupportMaterial(target, m)) return $"[슬롯 2 보조] {m?.Template?.PlayerName}: 같은 시즌 등급 · 같은 포지션({CardGrowthRules.PositionKey(target)}) 다른 선수가 아닙니다.";
                if (!owned.Contains(m)) return "[슬롯 2 보조] 보유하지 않은 카드가 있습니다.";
                if (lineup.Contains(m)) return $"[슬롯 2 보조] 주전 라인업 카드({m.Template.PlayerName})는 재료로 쓸 수 없습니다.";
                if (!seen.Add(m) || ReferenceEquals(m, selection.Core)) return $"{m.Template.PlayerName} 카드가 중복 등록됐습니다.";
            }
            if (!SupportSatisfied(selection.Support))
                return $"[슬롯 2 보조] 같은 포지션({CardGrowthRules.PositionKey(target)}) 다른 선수 {selection.Support.Count}/{SupportCount}장 - +5강 이상이면 1장으로 충족";
            var g = target.Template.Grade;
            if (ledger.Points < PointCost(g) || ledger.Trophies < TrophyCost(g))
                return $"재화 부족 - {CostLabel(g)} 필요 (보유 {ledger.Points:N0}P · 트로피 {ledger.Trophies})";
            return null;
        }

        /// <summary>초월 실행: 검증 → 재료 · 대체권 · 포인트 · 트로피 소모 → 초월(AwakenLevel = 10).</summary>
        public static bool TryTranscend(Player target, Selection selection, IGrowthLedger ledger, out string message)
        {
            message = Validate(target, selection, ledger);
            if (message != null) return false;
            var g = target.Template.Grade;
            int cards = 0;
            foreach (var m in selection.Cards.ToList()) { ledger.RemoveCard(m); cards++; }
            if (selection.UseCoreTicket) ledger.TranscendTicket -= 1;
            ledger.Points -= PointCost(g);
            ledger.Trophies -= TrophyCost(g);
            target.AwakenLevel = CardGrowthRules.TranscendLevel;
            message = $"초월 성공! {target.Template.PlayerName} 9각 → 초월 (재료 {cards}장{(selection.UseCoreTicket ? " + 핵심 대체권 1개" : "")} · {CostLabel(g)} 소모)";
            return true;
        }

        /// <summary>성장 센터 슬롯 현황 문구.</summary>
        public static string SlotSummary(Player target, Selection selection)
        {
            if (target?.Template == null) return "";
            selection ??= new Selection();
            string core = selection.UseCoreTicket ? "대체권" : selection.Core != null ? "1/1" : "0/1";
            bool supportOk = SupportSatisfied(selection.Support);
            string support = $"{selection.Support.Count}/{SupportCount}" + (supportOk && selection.Support.Count < SupportCount ? "(+5강 충족)" : "");
            return $"핵심 {core} · 보조 {support} · {CostLabel(target.Template.Grade)}";
        }
    }
}
