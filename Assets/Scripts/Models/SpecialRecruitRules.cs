using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;

namespace KBOManager.Models
{
    /// <summary>[TASK-KBO-185] 스카우트 허브 [특별 영입] 종류.</summary>
    public enum SpecialRecruitKind
    {
        GoldenGlove = 0, // 골든글러브 영입(컴프야V26 골글 영입 완화판 - 3슬롯)
        Signature = 1,   // 시그니처 영입(컴프야V26 골글 영입 원본 수준 - 5슬롯)
    }

    /// <summary>[TASK-KBO-185] 특별 영입 재료 슬롯 1칸(조건 + 필요 장수).</summary>
    public sealed class RecruitSlot
    {
        public string Title;
        public string Condition;
        public int Count;
        public Func<Player, Team, bool> Accepts;
    }

    /// <summary>[TASK-KBO-185] 특별 영입 레시피(대상 등급 + 재료 슬롯 + 재화).</summary>
    public sealed class RecruitRecipe
    {
        public SpecialRecruitKind Kind;
        public string Name;
        public Grade TargetGrade;
        public IReadOnlyList<RecruitSlot> Slots;
        public int PointCost;            // 포인트(볼) 비용 - 트로피 대체가 있으면 트로피가 모자랄 때만 쓴다
        public int TrophyCost;           // 0이면 트로피 결제 없음
        public int TotalMaterials => Slots.Sum(s => s.Count);
    }

    /// <summary>[TASK-KBO-185] 특별 영입이 읽고 쓰는 보유 상태(GameManager 어댑터 / 테스트 더블 공용).</summary>
    public interface IRecruitLedger
    {
        IReadOnlyList<Player> Inventory { get; }
        IReadOnlyList<Player> Roster { get; }
        Team FavoriteTeam { get; }
        int Points { get; set; }
        int Trophies { get; set; }
        void RemoveCard(Player player);
        void AddCard(Player player);
    }

    /// <summary>[TASK-KBO-185] 메모리 원장(단위 테스트 · 검증 리포트용).</summary>
    public sealed class MemoryRecruitLedger : IRecruitLedger
    {
        public readonly List<Player> Cards = new List<Player>();
        public readonly List<Player> Lineup = new List<Player>();
        public IReadOnlyList<Player> Inventory => Cards;
        public IReadOnlyList<Player> Roster => Lineup;
        public Team FavoriteTeam { get; set; }
        public int Points { get; set; }
        public int Trophies { get; set; }
        public void RemoveCard(Player player) { Lineup.Remove(player); Cards.Remove(player); }
        public void AddCard(Player player) { if (player != null) Cards.Add(player); }
    }

    /// <summary>
    /// [TASK-KBO-185] 골든글러브 · 시그니처 특별 영입 - 스카우트(뽑기)에서 제외된 GG/SIG를 컴프야V26 "선수 영입"(다중 슬롯 재료 투입)처럼 얻는다.
    ///   [골든글러브 영입] 3슬롯(완화판) → 선택 구단 GOLDEN_GLOVE 1장 확정
    ///     ① 스페셜 카드: ALLSTAR / FRANCHISE / TITLE_HOLDER 1장  ② 강화 카드: +3강 이상 1장(등급 무관)
    ///     ③ 구단 카드: 선택 구단 LIVE_EPIC 이상 2장 + 50,000 포인트
    ///   [시그니처 영입] 5슬롯(정통판) → 선택 구단 SIGNATURE 1장 확정
    ///     ① 골글 재료: GOLDEN_GLOVE 1장  ② 상위 스페셜: TITLE_HOLDER 또는 FRANCHISE 2장  ③ 고강화: +6강 이상 1장
    ///     ④ 각성: 3각 이상 1장  ⑤ 구단 재료: 선택 구단 카드 3장 + 트로피 5개(부족하면 200,000 포인트)
    /// 재료는 주전 라인업(Roster) 카드를 절대 쓰지 않는다(자동 등록 · 검증 모두 제외). 한 카드는 한 슬롯에만 들어간다.
    /// 자동 등록은 슬롯 순서대로 "다른 슬롯에도 쓸 수 있는 카드는 뒤로, 낮은 위상 · 낮은 OVR 먼저" 골라 아까운 카드가 덜 갈리게 한다.
    /// </summary>
    public static class SpecialRecruitRules
    {
        public const int GoldenGlovePointCost = 50000;
        public const int SignatureTrophyCost = 5;
        public const int SignaturePointCost = 200000;

        public static readonly RecruitRecipe GoldenGloveRecipe = new RecruitRecipe
        {
            Kind = SpecialRecruitKind.GoldenGlove,
            Name = "골든글러브 영입",
            TargetGrade = Grade.GOLDEN_GLOVE,
            PointCost = GoldenGlovePointCost,
            TrophyCost = 0,
            Slots = new[]
            {
                new RecruitSlot { Title = "스페셜 카드", Condition = "올스타 · 프랜차이즈 · 타이틀 홀더 1장", Count = 1,
                    Accepts = (p, _) => IsGrade(p, Grade.ALLSTAR, Grade.FRANCHISE, Grade.TITLE_HOLDER) },
                new RecruitSlot { Title = "강화 카드", Condition = "+3강 이상 1장 (등급 무관)", Count = 1,
                    Accepts = (p, _) => p?.Template != null && p.ReinforceLevel >= 3 },
                new RecruitSlot { Title = "구단 카드", Condition = "선택 구단 라이브 에픽 이상 2장", Count = 2,
                    Accepts = (p, team) => IsTeam(p, team) && CardGrowthRules.PowerRank(p.Template.Grade) >= CardGrowthRules.PowerRank(Grade.LIVE_EPIC) },
            },
        };

        public static readonly RecruitRecipe SignatureRecipe = new RecruitRecipe
        {
            Kind = SpecialRecruitKind.Signature,
            Name = "시그니처 영입",
            TargetGrade = Grade.SIGNATURE,
            PointCost = SignaturePointCost,
            TrophyCost = SignatureTrophyCost,
            Slots = new[]
            {
                new RecruitSlot { Title = "핵심 골글 재료", Condition = "골든글러브 1장", Count = 1,
                    Accepts = (p, _) => IsGrade(p, Grade.GOLDEN_GLOVE) },
                new RecruitSlot { Title = "상위 스페셜", Condition = "타이틀 홀더 · 프랜차이즈 2장", Count = 2,
                    Accepts = (p, _) => IsGrade(p, Grade.TITLE_HOLDER, Grade.FRANCHISE) },
                new RecruitSlot { Title = "고강화 재료", Condition = "+6강 이상 1장", Count = 1,
                    Accepts = (p, _) => p?.Template != null && p.ReinforceLevel >= 6 },
                new RecruitSlot { Title = "각성 재료", Condition = "3각 이상 1장", Count = 1,
                    Accepts = (p, _) => p?.Template != null && p.AwakenLevel >= 3 },
                new RecruitSlot { Title = "구단 재료", Condition = "선택 구단 카드 3장", Count = 3,
                    Accepts = (p, team) => IsTeam(p, team) },
            },
        };

        public static RecruitRecipe Recipe(SpecialRecruitKind kind) => kind == SpecialRecruitKind.Signature ? SignatureRecipe : GoldenGloveRecipe;

        private static bool IsGrade(Player p, params Grade[] grades) => p?.Template != null && grades.Contains(p.Template.Grade);
        private static bool IsTeam(Player p, Team team) => p?.Template != null && team != Team.None && p.Template.Team == team;

        /// <summary>재화 조건 문구 - "50,000 포인트" / "트로피 5개 (또는 200,000 포인트)".</summary>
        public static string CostLabel(RecruitRecipe recipe) => recipe.TrophyCost > 0
            ? $"트로피 {recipe.TrophyCost}개 (또는 {recipe.PointCost:N0} 포인트)"
            : $"{recipe.PointCost:N0} 포인트";

        /// <summary>재료로 쓸 수 있는 보유 카드(라인업 제외).</summary>
        public static List<Player> AvailableMaterials(IEnumerable<Player> inventory, IEnumerable<Player> roster)
        {
            var locked = new HashSet<Player>(roster ?? Enumerable.Empty<Player>());
            return (inventory ?? Enumerable.Empty<Player>()).Where(p => p?.Template != null && !locked.Contains(p)).Distinct().ToList();
        }

        /// <summary>슬롯별 조건을 만족하는 보유 카드 수(라인업 제외, 다른 슬롯과 중복 허용 - "보유 현황" 표시용).</summary>
        public static int EligibleCount(RecruitSlot slot, IEnumerable<Player> available, Team favorite) =>
            (available ?? Enumerable.Empty<Player>()).Count(p => slot.Accepts(p, favorite));

        /// <summary>[조건 맞는 재료 자동 등록] - 슬롯 순서대로 채운다. 부족한 슬롯은 채울 수 있는 만큼만 담는다.</summary>
        public static List<List<Player>> AutoAssign(RecruitRecipe recipe, IEnumerable<Player> inventory, IEnumerable<Player> roster, Team favorite)
        {
            var available = AvailableMaterials(inventory, roster);
            var used = new HashSet<Player>();
            var result = new List<List<Player>>();
            for (int i = 0; i < recipe.Slots.Count; i++)
            {
                var slot = recipe.Slots[i];
                var others = recipe.Slots.Where((_, j) => j != i).ToList();
                var picks = available.Where(p => !used.Contains(p) && slot.Accepts(p, favorite))
                    .OrderBy(p => others.Count(o => o.Accepts(p, favorite)))           // 다른 슬롯에도 필요한 카드는 아껴 둔다
                    .ThenBy(p => CardGrowthRules.PowerRank(p.Template.Grade))
                    .ThenBy(p => p.CalculateNeutralOVR())
                    .ThenBy(p => p.ReinforceLevel + p.AwakenLevel)
                    .ThenBy(p => p.InstanceId, StringComparer.Ordinal)
                    .Take(slot.Count).ToList();
                foreach (var p in picks) used.Add(p);
                result.Add(picks);
            }
            return result;
        }

        /// <summary>재화 결제 수단 - 트로피가 충분하면 트로피, 아니면 포인트. 둘 다 부족하면 null.</summary>
        public static (int Trophies, int Points)? Payment(RecruitRecipe recipe, int ownedPoints, int ownedTrophies)
        {
            if (recipe.TrophyCost > 0 && ownedTrophies >= recipe.TrophyCost) return (recipe.TrophyCost, 0);
            if (ownedPoints >= recipe.PointCost) return (0, recipe.PointCost);
            return null;
        }

        /// <summary>영입 실행 가능 여부 - 불가하면 사유 문자열, 가능하면 null.</summary>
        public static string Validate(RecruitRecipe recipe, IReadOnlyList<IReadOnlyList<Player>> assignment, IRecruitLedger ledger)
        {
            if (ledger == null) return "게임 데이터가 없습니다.";
            if (ledger.FavoriteTeam == Team.None) return "선택 구단이 없습니다 - 온보딩에서 구단을 먼저 고르십시오.";
            if (assignment == null || assignment.Count != recipe.Slots.Count) return "재료 슬롯이 비어 있습니다.";
            var roster = new HashSet<Player>(ledger.Roster ?? Array.Empty<Player>());
            var owned = new HashSet<Player>(ledger.Inventory ?? Array.Empty<Player>());
            var seen = new HashSet<Player>();
            for (int i = 0; i < recipe.Slots.Count; i++)
            {
                var slot = recipe.Slots[i];
                var cards = assignment[i] ?? Array.Empty<Player>();
                if (cards.Count < slot.Count) return $"[{slot.Title}] 재료 부족 ({cards.Count}/{slot.Count}) - {slot.Condition}";
                foreach (var card in cards)
                {
                    if (card?.Template == null || !owned.Contains(card)) return $"[{slot.Title}] 보유하지 않은 카드가 있습니다.";
                    if (roster.Contains(card)) return $"[{slot.Title}] 주전 라인업 카드({card.Template.PlayerName})는 재료로 쓸 수 없습니다.";
                    if (!slot.Accepts(card, ledger.FavoriteTeam)) return $"[{slot.Title}] {card.Template.PlayerName}: 조건 미충족 - {slot.Condition}";
                    if (!seen.Add(card)) return $"{card.Template.PlayerName} 카드가 두 슬롯에 중복 등록됐습니다.";
                }
            }
            if (Payment(recipe, ledger.Points, ledger.Trophies) == null) return $"재화 부족 - {CostLabel(recipe)} 필요";
            return null;
        }

        /// <summary>영입 대상 미리보기 - 선택 구단의 해당 등급 카드(없으면 같은 등급 전체), 이름순.</summary>
        public static List<PlayerTemplate> TargetPool(IEnumerable<PlayerTemplate> templates, RecruitRecipe recipe, Team favorite)
        {
            var grade = (templates ?? Enumerable.Empty<PlayerTemplate>()).Where(t => t != null && t.Grade == recipe.TargetGrade).ToList();
            var own = grade.Where(t => t.Team == favorite).ToList();
            return (own.Count > 0 ? own : grade).OrderBy(t => t.PlayerName, StringComparer.Ordinal).ThenBy(t => t.SeasonYear).ToList();
        }

        /// <summary>
        /// 영입 실행: 검증 → 재료 소모 → 재화 차감 → 선택 구단 대상 카드 1장 발급(인벤토리 추가). 실패하면 아무것도 바꾸지 않는다.
        /// pick(n)은 [0, n) 정수 난수, issue는 템플릿 → 카드 발급(ScoutManager.IssueCard 등).
        /// </summary>
        public static bool TryRecruit(RecruitRecipe recipe, IReadOnlyList<IReadOnlyList<Player>> assignment, IRecruitLedger ledger,
            IEnumerable<PlayerTemplate> templates, Func<int, int> pick, Func<PlayerTemplate, Player> issue, out Player recruited, out string message)
        {
            recruited = null;
            message = Validate(recipe, assignment, ledger);
            if (message != null) return false;
            var pool = TargetPool(templates, recipe, ledger.FavoriteTeam);
            if (pool.Count == 0) { message = $"{CardGrowthRules.DisplayName(recipe.TargetGrade)} 카드 데이터가 없습니다."; return false; }
            int index = pick != null ? pick(pool.Count) : 0;
            var template = pool[Math.Max(0, Math.Min(pool.Count - 1, index))];
            var card = issue?.Invoke(template);
            if (card?.Template == null) { message = "카드 발급에 실패했습니다."; return false; }

            var pay = Payment(recipe, ledger.Points, ledger.Trophies).Value;
            foreach (var material in assignment.SelectMany(a => a)) ledger.RemoveCard(material);
            ledger.Trophies -= pay.Trophies;
            ledger.Points -= pay.Points;
            ledger.AddCard(card);
            recruited = card;
            string paid = pay.Trophies > 0 ? $"트로피 {pay.Trophies}개" : $"{pay.Points:N0} 포인트";
            message = $"{recipe.Name} 성공! {CardGrowthRules.DisplayName(template.Grade)} {template.PlayerName}'{template.SeasonYear % 100:00} 획득 " +
                      $"(재료 {recipe.TotalMaterials}장 · {paid} 소모)";
            return true;
        }
    }
}
