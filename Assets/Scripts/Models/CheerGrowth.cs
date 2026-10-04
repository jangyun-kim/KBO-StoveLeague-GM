using System;
using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-187] 치어리더 2대 성장(강화 +0~+10강 · ★1~★5 각성)과 구단 응원단 도감 규칙(순수 로직 - Task187Tests 대상).
    ///   - 강화: 포인트(볼)를 써서 +1강씩, 최대 +10강. 단계가 오를수록 배치된 6인 역할 슬롯 효과가 비례 상승
    ///     (응원단장 세트덱 적용률 +1%p/강 · 타격/투수 응원 +5강마다 +1 · 분위기 메이커 열세 대응 +5강마다 +1 / 팬심 방어 +1/2강 ·
    ///     홈 응원 관중 수익 +2%p/강 · 위기 응원 접전 가중치 +0.5%p/강).
    ///   - 각성: 동일 인물 카드 재료 = +2★, 동일 구단 카드 재료 = +1★, 최대 ★5. ★3(1차 임계점) · ★5(최종 임계점)에서 역할 효과 티어가 한 단계씩 도약.
    ///   - 도감: 구단별 보유 인물 수 / 카탈로그 인물 수, 수집 1명당 홈 관중 수익 +1%.
    /// 모든 효과는 경기 조건부(MatchEngine) · 경기 결산(관중 수익 · 팬심) 값이며 구단 OVR(라인업 평균)에는 절대 더하지 않는다(TASK-184 원칙).
    /// </summary>
    public static class CheerGrowth
    {
        public const int MaxReinforce = 10;
        public const int MinStars = 1;
        public const int MaxStars = 5;
        public const int FirstStarThreshold = 3;
        public const int HomeRevenuePercentPerReinforce = 2;
        public const int CollectionRevenuePercentPerCheerleader = 1;
        public const int MaxEffectiveTier = 5;

        public static int Stars(Cheerleader c) => c == null ? MinStars : Math.Max(MinStars, Math.Min(MaxStars, c.StarLevel));
        public static int Reinforce(Cheerleader c) => c == null ? 0 : Math.Max(0, Math.Min(MaxReinforce, c.ReinforceLevel));

        /// <summary>등급 티어(LIVE 1 / ICON 2 / LEGEND 3) + ★3 · ★5 임계점 도약(각 +1). 최대 5.</summary>
        public static int EffectiveTier(Cheerleader c)
        {
            if (c == null) return 1;
            int stars = Stars(c);
            int tier = CheerSquad.Tier(c.Grade) + (stars >= FirstStarThreshold ? 1 : 0) + (stars >= MaxStars ? 1 : 0);
            return Math.Min(MaxEffectiveTier, tier);
        }

        /// <summary>다음 강화(+level → +level+1) 비용(포인트).</summary>
        public static int ReinforceCost(int currentLevel) => 2000 + Math.Max(0, currentLevel) * 1500;

        public static string StarBadge(Cheerleader c) => new string('★', Stars(c)) + new string('☆', MaxStars - Stars(c));

        public static string GrowthLabel(Cheerleader c) => c == null ? "" : $"+{Reinforce(c)}강 · {StarBadge(c)}";

        // ------------------------------------------------------------------ 각성

        /// <summary>재료 투입 시 오르는 ★(동일 인물 2 / 동일 구단 1 / 그 외 0). 자기 자신 · 빈 카드는 0.</summary>
        public static int AwakenGain(Cheerleader target, Cheerleader material)
        {
            if (CheerSquad.IsEmpty(target) || CheerSquad.IsEmpty(material) || target.InstanceId == material.InstanceId) return 0;
            if (CheerSquad.PersonKey(target) == CheerSquad.PersonKey(material)) return 2;
            if (target.Team != Team.None && target.Team == material.Team) return 1;
            return 0;
        }

        public static bool CanAwaken(Cheerleader target, Cheerleader material, IReadOnlyList<Cheerleader> squad, out string reason)
        {
            reason = null;
            if (CheerSquad.IsEmpty(target)) { reason = "각성할 치어리더를 선택하십시오."; return false; }
            if (Stars(target) >= MaxStars) { reason = $"{target.Name}은(는) 이미 ★{MaxStars} 최종 각성입니다."; return false; }
            if (AwakenGain(target, material) <= 0) { reason = "재료는 동일 인물(+2★) 또는 동일 구단(+1★) 치어리더만 쓸 수 있습니다."; return false; }
            if (squad != null && squad.Any(s => !CheerSquad.IsEmpty(s) && s.InstanceId == material.InstanceId))
            {
                reason = $"{material.Name}은(는) 6인 응원단에 편성 중이라 재료로 쓸 수 없습니다(해제 후 사용).";
                return false;
            }
            return true;
        }

        /// <summary>재료 후보(편성 중 · 자기 자신 제외) - 동일 인물(+2★) 먼저, 그다음 동일 구단(+1★), 같은 우선순위면 낮은 성장 카드부터.</summary>
        public static List<Cheerleader> AwakenMaterials(Cheerleader target, IEnumerable<Cheerleader> owned, IReadOnlyList<Cheerleader> squad)
        {
            return (owned ?? Enumerable.Empty<Cheerleader>())
                .Where(m => CanAwaken(target, m, squad, out _))
                .OrderByDescending(m => AwakenGain(target, m))
                .ThenBy(m => Stars(m)).ThenBy(m => Reinforce(m)).ThenBy(m => m.Grade)
                .ToList();
        }

        /// <summary>각성 적용(재료 제거는 호출부). 오른 ★ 수를 돌려준다.</summary>
        public static int ApplyAwaken(Cheerleader target, Cheerleader material)
        {
            int before = Stars(target);
            target.StarLevel = Math.Min(MaxStars, before + AwakenGain(target, material));
            return target.StarLevel - before;
        }

        // ------------------------------------------------------------------ 강화 효과(역할 슬롯 비례 상승)

        public static int LeaderAmplifyBonusPercent(Cheerleader c) => Reinforce(c);
        public static int RoleStatBonus(Cheerleader c) => Reinforce(c) / 5;
        public static int SentimentDefenseBonus(Cheerleader c) => Reinforce(c) / 2;
        public static int HomeRevenueBonusPercent(Cheerleader c) => Reinforce(c) * HomeRevenuePercentPerReinforce;
        public static float CloseLateBonus(Cheerleader c) => Reinforce(c) * 0.005f;

        // ------------------------------------------------------------------ 도감

        public sealed class CollectionStatus
        {
            public Team Team;
            public int Collected;
            public int Total;
            public int RevenueBonusPercent => Collected * CollectionRevenuePercentPerCheerleader;
            public string Label => $"{TeamShortName(Team)} 응원단 수집 {Collected}/{Total}명 · 도감 보너스: 홈 관중 수익 +{RevenueBonusPercent}%";
        }

        public static CollectionStatus Collection(Team team, IEnumerable<Cheerleader> owned, IEnumerable<Cheerleader> catalog)
        {
            var total = new HashSet<string>((catalog ?? Enumerable.Empty<Cheerleader>()).Where(c => c != null && c.Team == team).Select(CheerSquad.PersonKey));
            var collected = new HashSet<string>((owned ?? Enumerable.Empty<Cheerleader>()).Where(c => !CheerSquad.IsEmpty(c) && c.Team == team).Select(CheerSquad.PersonKey));
            if (total.Count > 0) collected.IntersectWith(total);
            return new CollectionStatus { Team = team, Collected = collected.Count, Total = Math.Max(total.Count, collected.Count) };
        }

        /// <summary>카탈로그 전체(등급별 목록 합).</summary>
        public static List<Cheerleader> FullCatalog()
        {
            var all = new List<Cheerleader>();
            foreach (CheerleaderGrade grade in Enum.GetValues(typeof(CheerleaderGrade)))
            {
                if (grade == CheerleaderGrade.NONE || grade == CheerleaderGrade.TEST) continue;
                all.AddRange(CheerleaderCatalog.GetCheerleadersByGrade(grade));
            }
            return all;
        }

        /// <summary>홈 승리 관중 수익 배율 = 홈 응원 카드 기본 배율 + 강화(+2%p/강) + 세트덱 구단 도감 보너스(+1%/명).</summary>
        public static float HomeRevenueMultiplier(Cheerleader homeSlot, CollectionStatus collection)
        {
            float rate = homeSlot != null ? homeSlot.EconomicBonusRate : 1f;
            if (float.IsNaN(rate) || float.IsInfinity(rate) || rate <= 0f) rate = 1f;
            return rate + HomeRevenueBonusPercent(homeSlot) / 100f + (collection?.RevenueBonusPercent ?? 0) / 100f;
        }

        private static string TeamShortName(Team team) => team switch
        {
            Team.Samsung => "삼성",
            Team.KIA => "KIA",
            Team.LG => "LG",
            Team.Doosan => "두산",
            Team.KT => "KT",
            Team.SSG => "SSG",
            Team.Lotte => "롯데",
            Team.Hanwha => "한화",
            Team.NC => "NC",
            Team.Kiwoom => "키움",
            _ => team.ToString(),
        };
    }
}
