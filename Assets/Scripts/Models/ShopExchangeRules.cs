using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;

namespace KBOManager.Models
{
    /// <summary>[TASK-KBO-189] 상점 · 교환소 상품.</summary>
    public enum ShopProduct
    {
        // 포인트 상시 상품
        LivePositionPack = 0,   // 포지션별 LIVE 영입팩
        TrainingBox = 1,        // 훈련/특훈 재료 상자(특훈권)
        EnhanceSupportPack = 2, // 강화 보조팩(LIVE 3장)
        // 성장 코인 교환소
        SpecialPositionPack = 3,// 선택 구단 · 포지션 지정 스페셜팩(각성 +1각 재료 저격용)
        AwakenTicket = 4,       // 범용 각성 보조권(+1각)
        TranscendTicket = 5,    // 초월 핵심 대체권
        RecruitMaterialBox = 6, // 골글/시그니처 특별 영입 재료 상자
        // [TASK-KBO-190] 3슬롯 스킬 변경권(포인트 또는 성장 코인) · 고급 스킬 변경권(성장 코인 또는 트로피)
        SkillChangeTicket = 7,              // 스킬 변경권(포인트)
        SkillChangeTicketCoin = 8,          // 스킬 변경권(성장 코인)
        PremiumSkillChangeTicket = 9,       // 고급 스킬 변경권(성장 코인)
        PremiumSkillChangeTicketTrophy = 10,// 고급 스킬 변경권(트로피)
    }

    public enum ShopCurrency { Points, GrowthCoin, Trophy }

    /// <summary>
    /// [TASK-KBO-189] 컴프야V26 운영 기조(잉여 카드 재활용 · 포지션/구단 맞춤 수급 · 명확한 재료 획득처)를 옮긴 상점/교환소 순수 규칙.
    ///   1) 선수 방출: 라인업 미편성 카드 → 포인트 + 성장 코인(등급 기본값 × (1 + 0.1 × 강화 단계)).
    ///   2) 3:1 포지션 재조합: 같은 시즌 등급 카드 3장 → 그 등급의 지정 포지션 카드 1장 확정(선택 구단 우선).
    ///   3) 상점(포인트) 3종 + 교환소(성장 코인) 4종 구매.
    /// UI(ShopExchangeView)와 단위 테스트가 같은 규칙을 쓴다. 실패한 거래는 아무것도 바꾸지 않는다.
    /// </summary>
    public static class ShopExchangeRules
    {
        public const int RecombineCount = 3;
        public static readonly Grade[] SpecialPackGrades = { Grade.ALLSTAR, Grade.FRANCHISE, Grade.TITLE_HOLDER };
        public static readonly Grade[] RecombineGrades =
        {
            Grade.LIVE_NORMAL, Grade.LIVE_EPIC, Grade.ALLSTAR, Grade.FRANCHISE, Grade.TITLE_HOLDER,
            Grade.GOLDEN_GLOVE, Grade.SIGNATURE, Grade.DYNASTY, Grade.RETIRED_NUMBER,
        };

        // ================================================================== 1. 선수 방출

        public static int ReleaseBasePoints(Grade g) => g switch
        {
            Grade.LIVE_NORMAL => 300,
            Grade.LIVE_EPIC => 600,
            Grade.ALLSTAR => 1500,
            Grade.FRANCHISE => 2000,
            Grade.TITLE_HOLDER => 2500,
            Grade.GOLDEN_GLOVE => 5000,
            _ => 8000, // SIGNATURE / DYNASTY / RETIRED_NUMBER
        };

        public static int ReleaseBaseCoins(Grade g) => g switch
        {
            Grade.LIVE_NORMAL => 5,
            Grade.LIVE_EPIC => 10,
            Grade.ALLSTAR => 30,
            Grade.FRANCHISE => 40,
            Grade.TITLE_HOLDER => 50,
            Grade.GOLDEN_GLOVE => 100,
            _ => 150,
        };

        /// <summary>방출 보상 - 등급 기본값 × (1 + 0.1 × 강화 단계), 반올림.</summary>
        public static (int Points, int Coins) ReleaseReward(Player p)
        {
            if (p?.Template == null) return (0, 0);
            int reinforce = Math.Max(0, Math.Min(Player.MaxReinforceLevel, p.ReinforceLevel));
            return ((int)Math.Round(ReleaseBasePoints(p.Template.Grade) * (1 + 0.1 * reinforce)),
                    (int)Math.Round(ReleaseBaseCoins(p.Template.Grade) * (1 + 0.1 * reinforce)));
        }

        /// <summary>방출 후보 - 라인업 미편성 보유 카드(아까운 카드가 뒤로: 낮은 위상 · 낮은 OVR 먼저).</summary>
        public static List<Player> ReleaseCandidates(IEnumerable<Player> inventory, IEnumerable<Player> roster)
        {
            return SpecialRecruitRules.AvailableMaterials(inventory, roster)
                .OrderBy(p => CardGrowthRules.PowerRank(p.Template.Grade)).ThenBy(p => p.ReinforceLevel + p.AwakenLevel)
                .ThenBy(p => p.CalculateNeutralOVR()).ThenBy(p => p.Template.PlayerName, StringComparer.Ordinal).ToList();
        }

        public static bool TryRelease(IReadOnlyCollection<Player> cards, IGrowthLedger ledger, out int points, out int coins, out string message)
        {
            points = coins = 0;
            if (ledger == null) { message = "게임 데이터가 없습니다."; return false; }
            var list = (cards ?? Array.Empty<Player>()).Where(c => c != null).Distinct().ToList();
            if (list.Count == 0) { message = "방출할 선수를 선택하십시오."; return false; }
            var owned = new HashSet<Player>(ledger.Inventory ?? Array.Empty<Player>());
            var lineup = new HashSet<Player>(ledger.Roster ?? Array.Empty<Player>());
            foreach (var c in list)
            {
                if (c.Template == null || !owned.Contains(c)) { message = "보유하지 않은 카드가 있습니다."; return false; }
                if (lineup.Contains(c)) { message = $"주전 라인업 카드({c.Template.PlayerName})는 방출할 수 없습니다."; return false; }
            }
            foreach (var c in list)
            {
                var r = ReleaseReward(c);
                points += r.Points;
                coins += r.Coins;
                ledger.RemoveCard(c);
            }
            ledger.Points += points;
            ledger.GrowthCoin += coins;
            message = $"선수 {list.Count}명 방출 - 포인트 +{points:N0} · 성장 코인 +{coins:N0}";
            return true;
        }

        // ================================================================== 2. 3:1 포지션 재조합

        /// <summary>재조합 대상 템플릿 - 등급 · 포지션 일치, 선택 구단 카드가 있으면 그것만(내 선택 구단 우선).</summary>
        public static List<PlayerTemplate> RecombinePool(IEnumerable<PlayerTemplate> templates, Grade grade, string positionKey, Team favorite)
        {
            var all = (templates ?? Enumerable.Empty<PlayerTemplate>())
                .Where(t => t != null && t.Grade == grade && CardGrowthRules.PositionKey(t) == positionKey).ToList();
            var own = all.Where(t => favorite != Team.None && t.Team == favorite).ToList();
            return (own.Count > 0 ? own : all).OrderBy(t => t.PlayerName, StringComparer.Ordinal).ThenBy(t => t.SeasonYear).ToList();
        }

        public static string ValidateRecombine(IReadOnlyCollection<Player> materials, IGrowthLedger ledger)
        {
            if (ledger == null) return "게임 데이터가 없습니다.";
            var list = (materials ?? Array.Empty<Player>()).Where(m => m != null).Distinct().ToList();
            if (list.Count != RecombineCount) return $"같은 시즌 등급 카드 {RecombineCount}장을 선택하십시오 ({list.Count}/{RecombineCount}).";
            var owned = new HashSet<Player>(ledger.Inventory ?? Array.Empty<Player>());
            var lineup = new HashSet<Player>(ledger.Roster ?? Array.Empty<Player>());
            if (list.Any(m => m.Template == null || !owned.Contains(m))) return "보유하지 않은 카드가 있습니다.";
            var locked = list.FirstOrDefault(lineup.Contains);
            if (locked != null) return $"주전 라인업 카드({locked.Template.PlayerName})는 재료로 쓸 수 없습니다.";
            if (list.Select(m => m.Template.Grade).Distinct().Count() != 1) return "재료 3장은 모두 같은 시즌 등급이어야 합니다.";
            return null;
        }

        public static bool TryRecombine(IReadOnlyCollection<Player> materials, string positionKey, IGrowthLedger ledger,
            IEnumerable<PlayerTemplate> templates, Func<int, int> pick, Func<PlayerTemplate, Player> issue, out Player result, out string message)
        {
            result = null;
            message = ValidateRecombine(materials, ledger);
            if (message != null) return false;
            if (!CardGrowthRules.PositionKeys.Contains(positionKey)) { message = "목표 포지션을 선택하십시오."; return false; }
            var list = materials.Where(m => m != null).Distinct().ToList();
            var grade = list[0].Template.Grade;
            var pool = RecombinePool(templates, grade, positionKey, ledger.FavoriteTeam);
            if (pool.Count == 0) { message = $"{CardGrowthRules.DisplayName(grade)} {positionKey} 카드 데이터가 없습니다."; return false; }
            var card = Issue(pool, pick, issue);
            if (card?.Template == null) { message = "카드 발급에 실패했습니다."; return false; }
            foreach (var m in list) ledger.RemoveCard(m);
            ledger.AddCard(card);
            result = card;
            message = $"3:1 재조합 성공! {CardGrowthRules.DisplayName(grade)} {positionKey} {card.Template.PlayerName}'{card.Template.SeasonYear % 100:00} ({card.Template.Team}) 획득";
            return true;
        }

        // ================================================================== 3. 상점 · 교환소

        public static readonly ShopProduct[] PointProducts = { ShopProduct.LivePositionPack, ShopProduct.TrainingBox, ShopProduct.EnhanceSupportPack, ShopProduct.SkillChangeTicket };
        public static readonly ShopProduct[] CoinProducts =
        {
            ShopProduct.SpecialPositionPack, ShopProduct.AwakenTicket, ShopProduct.TranscendTicket, ShopProduct.RecruitMaterialBox,
            ShopProduct.SkillChangeTicketCoin, ShopProduct.PremiumSkillChangeTicket, ShopProduct.PremiumSkillChangeTicketTrophy,
        };

        public const int LivePackEpicPercent = 15;
        public const int FavoriteTeamPercent = 50;
        public const int EnhanceSupportCards = 3;

        public static ShopCurrency CurrencyOf(ShopProduct p)
        {
            switch (p)
            {
                case ShopProduct.LivePositionPack:
                case ShopProduct.TrainingBox:
                case ShopProduct.EnhanceSupportPack:
                case ShopProduct.SkillChangeTicket:
                    return ShopCurrency.Points;
                case ShopProduct.PremiumSkillChangeTicketTrophy:
                    return ShopCurrency.Trophy;
                default:
                    return ShopCurrency.GrowthCoin;
            }
        }

        /// <summary>[TASK-KBO-190] 재화 이름(부족 문구용).</summary>
        public static string CurrencyName(ShopCurrency c) => c == ShopCurrency.Points ? "포인트" : c == ShopCurrency.Trophy ? "트로피" : "성장 코인";

        public static int Owned(IGrowthLedger ledger, ShopCurrency c) =>
            ledger == null ? 0 : c == ShopCurrency.Points ? ledger.Points : c == ShopCurrency.Trophy ? ledger.Trophies : ledger.GrowthCoin;

        public static int Price(ShopProduct p, Grade grade = Grade.ALLSTAR) => p switch
        {
            ShopProduct.LivePositionPack => 3000,
            ShopProduct.TrainingBox => 8000,
            ShopProduct.EnhanceSupportPack => 4000,
            ShopProduct.SpecialPositionPack => grade == Grade.TITLE_HOLDER ? 400 : grade == Grade.FRANCHISE ? 300 : 200,
            ShopProduct.AwakenTicket => 500,
            ShopProduct.TranscendTicket => 1500,
            ShopProduct.RecruitMaterialBox => 800,
            ShopProduct.SkillChangeTicket => 12000,
            ShopProduct.SkillChangeTicketCoin => 150,
            ShopProduct.PremiumSkillChangeTicket => 600,
            ShopProduct.PremiumSkillChangeTicketTrophy => 1,
            _ => 0,
        };

        public static string Name(ShopProduct p) => p switch
        {
            ShopProduct.LivePositionPack => "포지션별 LIVE 영입팩",
            ShopProduct.TrainingBox => "훈련/특훈 재료 상자",
            ShopProduct.EnhanceSupportPack => "강화 보조팩",
            ShopProduct.SpecialPositionPack => "선택 구단 · 포지션 지정 스페셜팩",
            ShopProduct.AwakenTicket => "범용 각성 보조권 (+1각)",
            ShopProduct.TranscendTicket => "초월 핵심 대체권",
            ShopProduct.RecruitMaterialBox => "골글/시그니처 특별 영입 재료 상자",
            ShopProduct.SkillChangeTicket => "스킬 변경권",
            ShopProduct.SkillChangeTicketCoin => "스킬 변경권",
            ShopProduct.PremiumSkillChangeTicket => "고급 스킬 변경권",
            ShopProduct.PremiumSkillChangeTicketTrophy => "고급 스킬 변경권",
            _ => p.ToString(),
        };

        public static string Description(ShopProduct p) => p switch
        {
            ShopProduct.LivePositionPack => $"지정 포지션 LIVE 카드 1장 (에픽 {LivePackEpicPercent}% · 선택 구단 {FavoriteTeamPercent}%)",
            ShopProduct.TrainingBox => "특훈권 1장 - 훈련·특훈 1단계를 볼 대신 진행",
            ShopProduct.EnhanceSupportPack => $"LIVE 카드 {EnhanceSupportCards}장 - 강화 EXP 재료",
            ShopProduct.SpecialPositionPack => "지정 등급(올스타/프랜차이즈/타이틀 홀더) · 지정 포지션 · 선택 구단 1장 - 각성 +1각 재료 저격",
            ShopProduct.AwakenTicket => "같은 시즌 · 포지션 재료가 없을 때 즉시 +1각 (+10강 선행, 9각까지)",
            ShopProduct.TranscendTicket => "초월 슬롯 1(같은 시즌 · 같은 선수 1장)을 대체",
            ShopProduct.RecruitMaterialBox => "스페셜 카드 50% / +3강 LIVE 에픽 30% / +6강 LIVE 에픽 20% - 특별 영입 슬롯 재료",
            ShopProduct.SkillChangeTicket => "선수 3슬롯 스킬의 종류 · 등급(D~S)을 다시 뽑습니다 (성장 센터 > 훈련·특훈)",
            ShopProduct.SkillChangeTicketCoin => "선수 3슬롯 스킬의 종류 · 등급(D~S)을 다시 뽑습니다 (성장 센터 > 훈련·특훈)",
            ShopProduct.PremiumSkillChangeTicket => "스킬 재추첨 + 최소 1슬롯 A~S 등급 확정",
            ShopProduct.PremiumSkillChangeTicketTrophy => "스킬 재추첨 + 최소 1슬롯 A~S 등급 확정 (트로피 교환)",
            _ => "",
        };

        public static string PriceLabel(ShopProduct p, Grade grade = Grade.ALLSTAR)
        {
            var currency = CurrencyOf(p);
            return currency == ShopCurrency.Points ? $"{Price(p, grade):N0}P"
                : currency == ShopCurrency.Trophy ? $"트로피 {Price(p, grade):N0}" : $"코인 {Price(p, grade):N0}";
        }

        /// <summary>
        /// 상품 구매: 재화 확인 → 상품 지급(카드는 issue로 발급) → 재화 차감. 지급할 카드가 없으면 결제하지 않는다.
        /// positionKey/grade는 포지션 지정 상품(LIVE 영입팩 · 스페셜팩)에서만 쓴다. pick(n)은 [0, n) 정수 난수.
        /// </summary>
        public static bool TryBuy(ShopProduct product, IGrowthLedger ledger, string positionKey, Grade grade,
            IEnumerable<PlayerTemplate> templates, Func<int, int> pick, Func<PlayerTemplate, Player> issue,
            out List<Player> cards, out string message)
        {
            cards = new List<Player>();
            if (ledger == null) { message = "게임 데이터가 없습니다."; return false; }
            pick ??= n => 0;
            int price = Price(product, grade);
            var currency = CurrencyOf(product);
            int owned = Owned(ledger, currency);
            if (owned < price) { message = $"{CurrencyName(currency)} 부족 - {PriceLabel(product, grade)} 필요 (보유 {owned:N0})"; return false; }

            var all = (templates ?? Enumerable.Empty<PlayerTemplate>()).Where(t => t != null).ToList();
            string reward;
            switch (product)
            {
                case ShopProduct.LivePositionPack:
                {
                    var live = all.Where(t => CardGrowthRules.IsLive(t.Grade) && CardGrowthRules.PositionKey(t) == positionKey).ToList();
                    if (live.Count == 0) { message = $"{positionKey} LIVE 카드 데이터가 없습니다."; return false; }
                    var epic = live.Where(t => t.Grade == Grade.LIVE_EPIC).ToList();
                    var normal = live.Where(t => t.Grade == Grade.LIVE_NORMAL).ToList();
                    var pool = pick(100) < LivePackEpicPercent && epic.Count > 0 ? epic : normal.Count > 0 ? normal : epic;
                    var card = Issue(PreferFavorite(pool, ledger.FavoriteTeam, pick), pick, issue);
                    if (card == null) { message = "카드 발급에 실패했습니다."; return false; }
                    cards.Add(card);
                    reward = CardLabel(card);
                    break;
                }
                case ShopProduct.EnhanceSupportPack:
                {
                    var pool = all.Where(t => t.Grade == Grade.LIVE_NORMAL).ToList();
                    if (pool.Count == 0) { message = "LIVE 카드 데이터가 없습니다."; return false; }
                    for (int i = 0; i < EnhanceSupportCards; i++)
                    {
                        var card = Issue(pool, pick, issue);
                        if (card != null) cards.Add(card);
                    }
                    if (cards.Count == 0) { message = "카드 발급에 실패했습니다."; return false; }
                    reward = $"LIVE 카드 {cards.Count}장";
                    break;
                }
                case ShopProduct.SpecialPositionPack:
                {
                    if (!SpecialPackGrades.Contains(grade)) { message = "스페셜팩 등급은 올스타 · 프랜차이즈 · 타이틀 홀더 중에서 고르십시오."; return false; }
                    var pool = RecombinePool(all, grade, positionKey, ledger.FavoriteTeam);
                    if (pool.Count == 0) { message = $"{CardGrowthRules.DisplayName(grade)} {positionKey} 카드 데이터가 없습니다."; return false; }
                    var card = Issue(pool, pick, issue);
                    if (card == null) { message = "카드 발급에 실패했습니다."; return false; }
                    cards.Add(card);
                    reward = CardLabel(card);
                    break;
                }
                case ShopProduct.RecruitMaterialBox:
                {
                    int roll = pick(100);
                    List<PlayerTemplate> pool;
                    int reinforce = 0;
                    if (roll < 50) pool = PreferFavorite(all.Where(t => SpecialPackGrades.Contains(t.Grade)).ToList(), ledger.FavoriteTeam, pick);
                    else
                    {
                        reinforce = roll < 80 ? 3 : 6;
                        pool = PreferFavorite(all.Where(t => t.Grade == Grade.LIVE_EPIC).ToList(), ledger.FavoriteTeam, pick);
                    }
                    if (pool.Count == 0) { message = "재료 카드 데이터가 없습니다."; return false; }
                    var card = Issue(pool, pick, issue);
                    if (card == null) { message = "카드 발급에 실패했습니다."; return false; }
                    card.ReinforceLevel = reinforce;
                    cards.Add(card);
                    reward = CardLabel(card) + (reinforce > 0 ? $" +{reinforce}강" : "");
                    break;
                }
                case ShopProduct.TrainingBox:
                    ledger.TrainingTicket += 1;
                    reward = "특훈권 1장";
                    break;
                case ShopProduct.AwakenTicket:
                    ledger.AwakenTicket += 1;
                    reward = "범용 각성 보조권 1장";
                    break;
                case ShopProduct.TranscendTicket:
                    ledger.TranscendTicket += 1;
                    reward = "초월 핵심 대체권 1장";
                    break;
                case ShopProduct.SkillChangeTicket:
                case ShopProduct.SkillChangeTicketCoin:
                    ledger.SkillChangeTicket += 1;
                    reward = "스킬 변경권 1장";
                    break;
                case ShopProduct.PremiumSkillChangeTicket:
                case ShopProduct.PremiumSkillChangeTicketTrophy:
                    ledger.PremiumSkillChangeTicket += 1;
                    reward = "고급 스킬 변경권 1장";
                    break;
                default:
                    message = "알 수 없는 상품입니다.";
                    return false;
            }

            foreach (var c in cards) ledger.AddCard(c);
            if (currency == ShopCurrency.Points) ledger.Points -= price;
            else if (currency == ShopCurrency.Trophy) ledger.Trophies -= price;
            else ledger.GrowthCoin -= price;
            message = $"{Name(product)} 구매 - {reward} 획득 ({PriceLabel(product, grade)})";
            return true;
        }

        /// <summary>[범용 각성 보조권] 사용 - +10강 선행 · 9각까지 +1각.</summary>
        public static bool TryUseAwakenTicket(Player target, IGrowthLedger ledger, out string message)
        {
            if (ledger == null) { message = "게임 데이터가 없습니다."; return false; }
            if (!CardGrowthRules.CanAwakenNow(target, out message)) return false;
            if (ledger.AwakenTicket < 1) { message = "범용 각성 보조권이 없습니다 - 상점 · 교환소(성장 코인 500)에서 교환하십시오."; return false; }
            ledger.AwakenTicket -= 1;
            int before = target.AwakenLevel;
            target.AwakenLevel = Math.Min(before + 1, CardGrowthRules.MaterialAwakenCapFor(target.Template.Grade));
            message = $"각성 보조권 사용 - {CardGrowthRules.AwakenLabel(target.Template.Grade, before)} → {target.AwakenLabel}";
            return true;
        }

        private static List<PlayerTemplate> PreferFavorite(List<PlayerTemplate> pool, Team favorite, Func<int, int> pick)
        {
            if (favorite == Team.None || pool.Count == 0) return pool;
            var own = pool.Where(t => t.Team == favorite).ToList();
            return own.Count > 0 && pick(100) < FavoriteTeamPercent ? own : pool;
        }

        private static Player Issue(IReadOnlyList<PlayerTemplate> pool, Func<int, int> pick, Func<PlayerTemplate, Player> issue)
        {
            if (pool == null || pool.Count == 0) return null;
            int index = pick != null ? pick(pool.Count) : 0;
            var template = pool[Math.Max(0, Math.Min(pool.Count - 1, index))];
            return issue != null ? issue(template) : new Player(Guid.NewGuid().ToString(), template);
        }

        private static string CardLabel(Player c) =>
            $"{CardGrowthRules.DisplayName(c.Template.Grade)} {CardGrowthRules.PositionKey(c)} {c.Template.PlayerName}'{c.Template.SeasonYear % 100:00}";
    }
}
