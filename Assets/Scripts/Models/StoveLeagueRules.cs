using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;

namespace KBOManager.Models
{
    /// <summary>[TASK-KBO-193] 스토브리그 시즌별 진행 상태(세이브 대상). SeasonKey가 바뀌면(새 시즌) 전부 초기화한다.</summary>
    [Serializable]
    public class StoveLeagueState
    {
        public int SeasonKey = -1;
        public List<string> SignedFaIds = new List<string>(); // 계약 완료된 FA 매물 TemplateId
        public int TradeRound;        // 트레이드 제안 세트 번호(새로고침 · 수락마다 +1)
        public int TradesAccepted;
        public bool DraftUsed;        // 이번 시즌 신인 지명 완료
        public bool ForeignUsed;      // 이번 시즌 외국인 선수 영입 완료

        /// <summary>시즌이 바뀌었으면 초기화하고 true.</summary>
        public bool EnsureSeason(int seasonKey)
        {
            if (SeasonKey == seasonKey) return false;
            SeasonKey = seasonKey;
            SignedFaIds = new List<string>();
            TradeRound = 0;
            TradesAccepted = 0;
            DraftUsed = false;
            ForeignUsed = false;
            return true;
        }
    }

    /// <summary>[TASK-KBO-193] 트레이드 제안 1건 - 내 잉여 카드 1~2장 ↔ 선택 구단 · 주력 포지션 동급 이상 카드 1장.</summary>
    public sealed class TradeOffer
    {
        public List<Player> Give = new List<Player>();
        public PlayerTemplate Receive;
        public string Id => string.Join("+", Give.Select(p => p.InstanceId)) + ">" + (Receive != null ? Receive.TemplateId : "");
    }

    /// <summary>
    /// [TASK-KBO-193] 스토브리그(오프시즌) 전력 보강 규칙 - UI(StoveLeagueView)와 단위 테스트가 같은 경로를 쓴다.
    ///   ① FA 시장: 시즌마다 6명(라이브 에픽 ~ 타이틀 홀더, 선택 구단 선수 50% 이상) · 포인트 계약 → 보유 선수 즉시 추가.
    ///   ② 트레이드: 제안 3건(내 잉여 카드 1~2장 ↔ 선택 구단 · 주력 포지션 동급 이상 1장) - 주전 라인업 카드와 골글 이상 카드는 내주지 않는다.
    ///      수락하면 재료 소모 + 카드 획득 후 새 제안, [새로고침]은 포인트를 내고 새 제안.
    ///   ③ 신인 드래프트: 시즌 1회 무료 - 선택 구단 · 원하는 포지션의 가장 최근 연도 라이브 에픽/올스타 유망주 1장.
    ///   ④ 외국인 선수 스카우트: 시즌 1회 - 외국인 선수(한국 성씨가 아닌 이름) 후보 3명 중 1명 포인트 계약.
    /// 난수는 시즌 키 · 구단 · 라운드로 만든 결정적 시드(System.Random)라 저장 · 재시작해도 같은 매물이 보인다.
    /// </summary>
    public static class StoveLeagueRules
    {
        public const int FaOfferCount = 6;
        public const int TradeOfferCount = 3;
        public const int ForeignCandidateCount = 3;
        public const int TradeRefreshCost = 3000;

        public static readonly Grade[] FaGrades = { Grade.LIVE_EPIC, Grade.ALLSTAR, Grade.FRANCHISE, Grade.TITLE_HOLDER };
        public static readonly Grade[] DraftGrades = { Grade.LIVE_EPIC, Grade.ALLSTAR };

        /// <summary>FA · 외국인 계약금(포인트).</summary>
        public static int ContractPrice(Grade grade) => grade switch
        {
            Grade.LIVE_EPIC => 20000,
            Grade.ALLSTAR => 50000,
            Grade.FRANCHISE => 80000,
            Grade.TITLE_HOLDER => 120000,
            _ => 150000
        };

        public static int Seed(int seasonKey, Team favorite, int salt) => unchecked(seasonKey * 7919 + (int)favorite * 104729 + salt * 31337 + 17);

        private static bool InGrades(PlayerTemplate t, Grade[] grades) => t != null && grades.Contains(t.Grade);

        // ================================================================== ① FA

        /// <summary>FA 매물 6명 - 선택 구단 선수를 먼저 절반(3명) 이상 채우고 나머지는 전체에서(중복 선수 없음).</summary>
        public static List<PlayerTemplate> FaOffers(IEnumerable<PlayerTemplate> templates, Team favorite, int seasonKey)
        {
            var pool = (templates ?? Enumerable.Empty<PlayerTemplate>()).Where(t => InGrades(t, FaGrades))
                .OrderBy(t => t.TemplateId, StringComparer.Ordinal).ToList();
            var rng = new Random(Seed(seasonKey, favorite, 1));
            var picked = new List<PlayerTemplate>();
            var players = new HashSet<string>();
            int ownTarget = (FaOfferCount + 1) / 2;
            Take(pool.Where(t => favorite != Team.None && t.Team == favorite).ToList(), ownTarget, rng, picked, players);
            Take(pool, FaOfferCount - picked.Count, rng, picked, players);
            return picked;
        }

        private static void Take(List<PlayerTemplate> source, int count, Random rng, List<PlayerTemplate> picked, HashSet<string> players)
        {
            var bag = source.ToList();
            while (count > 0 && bag.Count > 0)
            {
                int i = rng.Next(bag.Count);
                var t = bag[i];
                bag.RemoveAt(i);
                string key = string.IsNullOrEmpty(t.RealPlayerId) ? t.PlayerName : t.RealPlayerId;
                if (picked.Contains(t) || !players.Add(key)) continue;
                picked.Add(t);
                count--;
            }
        }

        /// <summary>FA 계약: 포인트 차감 → 카드 발급 → 보유 선수 추가 → 계약 완료 기록. 실패하면 아무것도 바꾸지 않는다.</summary>
        public static bool TrySignFa(PlayerTemplate offer, StoveLeagueState state, IRecruitLedger ledger, Func<PlayerTemplate, Player> issue, out Player card, out string message)
        {
            card = null;
            if (offer == null || state == null || ledger == null) { message = "FA 정보가 없습니다."; return false; }
            if (state.SignedFaIds.Contains(offer.TemplateId)) { message = "이미 계약한 FA입니다."; return false; }
            int price = ContractPrice(offer.Grade);
            if (ledger.Points < price) { message = $"포인트 부족 - {price:N0}P 필요 (보유 {ledger.Points:N0})"; return false; }
            card = issue != null ? issue(offer) : null;
            if (card == null) { message = "카드를 발급하지 못했습니다."; return false; }
            ledger.Points -= price;
            ledger.AddCard(card);
            state.SignedFaIds.Add(offer.TemplateId);
            message = $"FA 계약 완료 - {CardDisplay.NameWithYear(offer)} ({CardGrowthRules.DisplayName(offer.Grade)}) 영입 · -{price:N0}P";
            return true;
        }

        // ================================================================== ② 트레이드

        /// <summary>트레이드로 내줄 수 있는 잉여 카드 - 주전 라인업 제외 · 골든글러브 이상 제외 · 낮은 OVR 순.</summary>
        public static List<Player> SurplusCards(IEnumerable<Player> inventory, IEnumerable<Player> roster)
        {
            var locked = new HashSet<Player>(roster ?? Enumerable.Empty<Player>());
            return (inventory ?? Enumerable.Empty<Player>())
                .Where(p => p?.Template != null && !locked.Contains(p) && CardGrowthRules.PowerRank(p.Template.Grade) <= CardGrowthRules.PowerRank(Grade.TITLE_HOLDER))
                .Distinct()
                .OrderBy(p => p.BaseOvr).ThenBy(p => p.InstanceId, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>트레이드 제안 3건(1장 · 2장 · 2장 교환). 받는 카드: 선택 구단 · 내준 카드의 포지션 · 내준 최고 등급 이상(2장 교환은 한 단계 위까지).</summary>
        public static List<TradeOffer> TradeOffers(IEnumerable<Player> inventory, IEnumerable<Player> roster, IEnumerable<PlayerTemplate> templates,
            Team favorite, int seasonKey, int round)
        {
            var offers = new List<TradeOffer>();
            var surplus = SurplusCards(inventory, roster);
            var all = (templates ?? Enumerable.Empty<PlayerTemplate>()).Where(t => t != null && CardGrowthRules.PowerRank(t.Grade) <= CardGrowthRules.PowerRank(Grade.TITLE_HOLDER)).ToList();
            if (surplus.Count == 0 || all.Count == 0) return offers;
            var rng = new Random(Seed(seasonKey, favorite, 100 + round));
            var used = new HashSet<Player>();
            for (int k = 0; k < TradeOfferCount; k++)
            {
                int giveCount = k == 0 ? 1 : 2;
                var free = surplus.Where(p => !used.Contains(p)).ToList();
                if (free.Count < giveCount) break;
                var give = new List<Player>();
                for (int g = 0; g < giveCount; g++)
                {
                    var p = free[rng.Next(free.Count)];
                    free.Remove(p);
                    give.Add(p);
                }
                int maxRank = give.Max(p => CardGrowthRules.PowerRank(p.Template.Grade));
                int topRank = Math.Min(CardGrowthRules.PowerRank(Grade.TITLE_HOLDER), maxRank + (giveCount >= 2 ? 1 : 0));
                string position = CardGrowthRules.PositionKey(give.OrderByDescending(p => p.BaseOvr).First().Template);
                var receive = PickReceive(all, favorite, position, maxRank, topRank, give, rng);
                if (receive == null) continue;
                foreach (var p in give) used.Add(p);
                offers.Add(new TradeOffer { Give = give, Receive = receive });
            }
            return offers;
        }

        private static PlayerTemplate PickReceive(List<PlayerTemplate> all, Team favorite, string position, int minRank, int maxRank, List<Player> give, Random rng)
        {
            var giveIds = new HashSet<string>(give.Select(p => p.Template.TemplateId));
            bool RankOk(PlayerTemplate t) { int r = CardGrowthRules.PowerRank(t.Grade); return r >= minRank && r <= maxRank; }
            var tiers = new List<Func<PlayerTemplate, bool>>
            {
                t => t.Team == favorite && CardGrowthRules.PositionKey(t) == position && RankOk(t),
                t => t.Team == favorite && RankOk(t),
                t => CardGrowthRules.PositionKey(t) == position && RankOk(t),
                t => CardGrowthRules.PowerRank(t.Grade) >= minRank,
            };
            foreach (var filter in tiers)
            {
                var pool = all.Where(t => !giveIds.Contains(t.TemplateId) && filter(t)).OrderBy(t => t.TemplateId, StringComparer.Ordinal).ToList();
                if (pool.Count > 0) return pool[rng.Next(pool.Count)];
            }
            return null;
        }

        /// <summary>트레이드 수락: 내줄 카드가 여전히 보유 · 비라인업인지 확인 → 소모 → 받는 카드 발급 → 다음 라운드 제안.</summary>
        public static bool TryAcceptTrade(TradeOffer offer, StoveLeagueState state, IRecruitLedger ledger, Func<PlayerTemplate, Player> issue, out Player card, out string message)
        {
            card = null;
            if (offer?.Receive == null || offer.Give.Count == 0 || state == null || ledger == null) { message = "트레이드 제안이 없습니다."; return false; }
            var surplus = new HashSet<Player>(SurplusCards(ledger.Inventory, ledger.Roster));
            if (offer.Give.Any(p => !surplus.Contains(p))) { message = "내줄 카드가 라인업에 편성됐거나 더 이상 보유하지 않습니다 - 새로고침하십시오."; return false; }
            card = issue != null ? issue(offer.Receive) : null;
            if (card == null) { message = "카드를 발급하지 못했습니다."; return false; }
            foreach (var p in offer.Give) ledger.RemoveCard(p);
            ledger.AddCard(card);
            state.TradesAccepted++;
            state.TradeRound++;
            message = $"트레이드 성사 - {string.Join(", ", offer.Give.Select(p => CardDisplay.NameWithYear(p.Template)))} → {CardDisplay.NameWithYear(offer.Receive)} ({CardGrowthRules.DisplayName(offer.Receive.Grade)})";
            return true;
        }

        /// <summary>트레이드 새로고침: 포인트를 내고 다음 라운드 제안.</summary>
        public static bool TryRefreshTrades(StoveLeagueState state, IRecruitLedger ledger, out string message)
        {
            if (state == null || ledger == null) { message = "게임 데이터가 없습니다."; return false; }
            if (ledger.Points < TradeRefreshCost) { message = $"포인트 부족 - 새로고침 {TradeRefreshCost:N0}P 필요"; return false; }
            ledger.Points -= TradeRefreshCost;
            state.TradeRound++;
            message = $"새 트레이드 제안을 받았습니다 (-{TradeRefreshCost:N0}P)";
            return true;
        }

        // ================================================================== ③ 신인 드래프트

        /// <summary>신인 지명 후보 - 선택 구단 · 포지션 · 라이브 에픽/올스타 중 가장 최근 연도(유망주). 없으면 포지션 → 구단 순으로 완화.</summary>
        public static List<PlayerTemplate> DraftPool(IEnumerable<PlayerTemplate> templates, Team favorite, string positionKey)
        {
            var grade = (templates ?? Enumerable.Empty<PlayerTemplate>()).Where(t => InGrades(t, DraftGrades)).ToList();
            var own = favorite != Team.None ? grade.Where(t => t.Team == favorite).ToList() : grade;
            if (own.Count == 0) own = grade;
            var pos = own.Where(t => CardGrowthRules.PositionKey(t) == positionKey).ToList();
            var pool = pos.Count > 0 ? pos : own;
            if (pool.Count == 0) return pool;
            int latest = pool.Max(t => t.SeasonYear);
            return pool.Where(t => t.SeasonYear == latest).OrderBy(t => t.TemplateId, StringComparer.Ordinal).ToList();
        }

        /// <summary>신인 지명(시즌 1회 무료): 후보 중 1명 발급 → 보유 선수 추가.</summary>
        public static bool TryDraft(IEnumerable<PlayerTemplate> templates, string positionKey, StoveLeagueState state, IRecruitLedger ledger,
            Func<int, int> pick, Func<PlayerTemplate, Player> issue, out Player card, out string message)
        {
            card = null;
            if (state == null || ledger == null) { message = "게임 데이터가 없습니다."; return false; }
            if (state.DraftUsed) { message = "이번 시즌 신인 지명을 이미 마쳤습니다."; return false; }
            var pool = DraftPool(templates, ledger.FavoriteTeam, positionKey);
            if (pool.Count == 0) { message = "지명할 유망주 데이터가 없습니다."; return false; }
            int i = pick != null ? pick(pool.Count) : 0;
            var t = pool[Math.Max(0, Math.Min(pool.Count - 1, i))];
            card = issue != null ? issue(t) : null;
            if (card == null) { message = "카드를 발급하지 못했습니다."; return false; }
            ledger.AddCard(card);
            state.DraftUsed = true;
            message = $"신인 지명 - {CardDisplay.NameWithYear(t)} ({CardDisplay.PositionShort(t)} · {CardGrowthRules.DisplayName(t.Grade)}) 입단!";
            return true;
        }

        // ================================================================== ④ 외국인 선수

        // 한국인 성씨 첫 글자 - 이 목록에 없는 글자로 시작하는 이름(로하스 · 페디 · 에레디아 …)을 외국인 선수로 본다.
        private const string KoreanSurnames = "김이박최정강조윤장임한오서신권황안송류전홍고문양손배백허유남심노하곽성차주우구민진나지엄채원천방공현함변염여추도소석선설마길연위표명기반왕금옥육인맹제모탁국어은편용예봉경사부";

        public static bool IsForeignName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.Contains(" ") || KoreanSurnames.IndexOf(name[0]) < 0;
        }

        /// <summary>외국인 선수 후보 3명(라이브 에픽 ~ 타이틀 홀더, 서로 다른 선수).</summary>
        public static List<PlayerTemplate> ForeignCandidates(IEnumerable<PlayerTemplate> templates, Team favorite, int seasonKey)
        {
            var pool = (templates ?? Enumerable.Empty<PlayerTemplate>()).Where(t => InGrades(t, FaGrades) && IsForeignName(t.PlayerName))
                .OrderBy(t => t.TemplateId, StringComparer.Ordinal).ToList();
            var picked = new List<PlayerTemplate>();
            Take(pool, ForeignCandidateCount, new Random(Seed(seasonKey, favorite, 7)), picked, new HashSet<string>());
            return picked;
        }

        /// <summary>외국인 선수 계약(시즌 1회): 포인트 차감 → 발급 → 보유 선수 추가.</summary>
        public static bool TrySignForeign(PlayerTemplate candidate, StoveLeagueState state, IRecruitLedger ledger, Func<PlayerTemplate, Player> issue, out Player card, out string message)
        {
            card = null;
            if (candidate == null || state == null || ledger == null) { message = "외국인 선수 정보가 없습니다."; return false; }
            if (state.ForeignUsed) { message = "이번 시즌 외국인 선수 영입을 이미 마쳤습니다."; return false; }
            int price = ContractPrice(candidate.Grade);
            if (ledger.Points < price) { message = $"포인트 부족 - {price:N0}P 필요 (보유 {ledger.Points:N0})"; return false; }
            card = issue != null ? issue(candidate) : null;
            if (card == null) { message = "카드를 발급하지 못했습니다."; return false; }
            ledger.Points -= price;
            ledger.AddCard(card);
            state.ForeignUsed = true;
            message = $"외국인 선수 계약 - {CardDisplay.NameWithYear(candidate)} ({CardDisplay.PositionShort(candidate)} · {CardGrowthRules.DisplayName(candidate.Grade)}) · -{price:N0}P";
            return true;
        }

        // ================================================================== 문구

        public static string OfferLine(PlayerTemplate t) =>
            t == null ? "" : $"{CardDisplay.NameWithYear(t)} ({CardDisplay.PositionShort(t)}) · {CardGrowthRules.DisplayName(t.Grade)} · OVR {t.GetBaseOverall()}";
    }
}
