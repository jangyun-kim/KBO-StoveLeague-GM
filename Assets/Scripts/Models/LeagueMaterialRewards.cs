using System;
using System.Collections.Generic;

namespace KBOManager.Models
{
    /// <summary>[TASK-KBO-189] 리그 경기/시즌 재료 보상 묶음.</summary>
    public struct LeagueMaterialReward
    {
        public int Points;
        public int GrowthCoin;
        public int TrainingTicket;
        public int Trophy;
        /// <summary>[TASK-KBO-190] 스킬 변경권 · 고급 스킬 변경권.</summary>
        public int SkillChangeTicket;
        public int PremiumSkillChangeTicket;

        public bool IsEmpty => Points == 0 && GrowthCoin == 0 && TrainingTicket == 0 && Trophy == 0 && SkillChangeTicket == 0 && PremiumSkillChangeTicket == 0;

        public string Summary()
        {
            var parts = new List<string>();
            if (Points > 0) parts.Add($"포인트 +{Points:N0}");
            if (GrowthCoin > 0) parts.Add($"성장 코인 +{GrowthCoin:N0}");
            if (TrainingTicket > 0) parts.Add($"특훈권 +{TrainingTicket}");
            if (Trophy > 0) parts.Add($"트로피 +{Trophy}");
            if (SkillChangeTicket > 0) parts.Add($"스킬 변경권 +{SkillChangeTicket}");
            if (PremiumSkillChangeTicket > 0) parts.Add($"고급 스킬 변경권 +{PremiumSkillChangeTicket}");
            return parts.Count > 0 ? string.Join(" · ", parts) : "없음";
        }

        public void ApplyTo(IGrowthLedger ledger)
        {
            if (ledger == null) return;
            ledger.Points += Points;
            ledger.GrowthCoin += GrowthCoin;
            ledger.TrainingTicket += TrainingTicket;
            ledger.Trophies += Trophy;
            ledger.SkillChangeTicket += SkillChangeTicket;
            ledger.PremiumSkillChangeTicket += PremiumSkillChangeTicket;
        }
    }

    /// <summary>
    /// [TASK-KBO-189] 12단계 리그(아마추어 ~ 영구결번) 재료 획득처 - 리그 단계(index 0~11)에 비례해 포인트뿐 아니라
    /// 성장 코인 · 특훈권(훈련/특훈 보너스) · 트로피(상위 리그 · 우승)를 준다.
    ///   경기 승리: 포인트 400+200i · 코인 4+2i · 특훈권 확률 10+2i% · 트로피 확률(타이틀홀더 리그 이상) 5×(i-6)%
    ///   경기 무/패: 포인트 150+50i · 코인 1+i/2
    ///   시즌 우승: 포인트 20,000+5,000i · 코인 200+50i · 특훈권 2+i/4 · 트로피 1+i/3
    ///   준우승(2위): 포인트 12,000+3,000i · 코인 150+30i · 특훈권 2 · 트로피 1+i/4
    ///   포스트시즌(3~5위): 포인트 8,000+2,000i · 코인 100+20i · 특훈권 1 · 트로피(3위 1, 4~5위는 프랜차이즈 리그 이상 1)
    ///   그 외: 포인트 3,000+500i · 코인 50+10i
    ///   [TASK-KBO-190] 스킬 변경권: 경기 승리 확률 5+i% · 우승 2+i/4장 + 고급 1장 · 준우승 2장 · 포스트시즌 1장
    /// </summary>
    public static class LeagueMaterialRewards
    {
        public static int Index(LeagueTier tier) => Math.Max(0, Math.Min((int)LeagueTierTable.Highest, (int)tier));

        public static int TrainingChancePercent(LeagueTier tier, bool won) => won ? 10 + 2 * Index(tier) : 0;

        public static int TrophyChancePercent(LeagueTier tier, bool won) => won && Index(tier) > 6 ? 5 * (Index(tier) - 6) : 0;

        /// <summary>[TASK-KBO-190] 경기 승리 시 스킬 변경권 확률(5 + 리그 단계%).</summary>
        public static int SkillTicketChancePercent(LeagueTier tier, bool won) => won ? 5 + Index(tier) : 0;

        /// <summary>경기 1회 보상. roll(n)은 [0, n) 정수 난수(확률 보상 판정) - null이면 확률 보상 없음.</summary>
        public static LeagueMaterialReward ForMatch(LeagueTier tier, bool won, Func<int, int> roll)
        {
            int i = Index(tier);
            var r = new LeagueMaterialReward
            {
                Points = won ? 400 + 200 * i : 150 + 50 * i,
                GrowthCoin = won ? 4 + 2 * i : 1 + i / 2,
            };
            if (roll != null)
            {
                if (roll(100) < TrainingChancePercent(tier, won)) r.TrainingTicket = 1;
                if (roll(100) < TrophyChancePercent(tier, won)) r.Trophy = 1;
                if (roll(100) < SkillTicketChancePercent(tier, won)) r.SkillChangeTicket = 1; // [TASK-KBO-190]
            }
            return r;
        }

        /// <summary>시즌 최종 순위 보상(1위 = 우승).</summary>
        public static LeagueMaterialReward ForSeason(LeagueTier tier, int finalRank)
        {
            int i = Index(tier);
            if (finalRank <= 1)
                return new LeagueMaterialReward { Points = 20000 + 5000 * i, GrowthCoin = 200 + 50 * i, TrainingTicket = 2 + i / 4, Trophy = 1 + i / 3,
                    SkillChangeTicket = 2 + i / 4, PremiumSkillChangeTicket = 1 };
            if (finalRank == 2)
                return new LeagueMaterialReward { Points = 12000 + 3000 * i, GrowthCoin = 150 + 30 * i, TrainingTicket = 2, Trophy = 1 + i / 4, SkillChangeTicket = 2 };
            if (finalRank <= 5)
                return new LeagueMaterialReward { Points = 8000 + 2000 * i, GrowthCoin = 100 + 20 * i, TrainingTicket = 1,
                    Trophy = finalRank == 3 || i >= (int)LeagueTier.Franchise ? 1 : 0, SkillChangeTicket = 1 };
            return new LeagueMaterialReward { Points = 3000 + 500 * i, GrowthCoin = 50 + 10 * i };
        }

        /// <summary>재료 획득처 안내(성장 센터 · 특별 영입 · 상점 안내 탭 공용).</summary>
        public static string SourceGuide(LeagueTier tier)
        {
            var win = ForMatch(tier, true, null);
            var champion = ForSeason(tier, 1);
            return
                $"■ 리그 승리({LeagueTierTable.DisplayName(tier)}): {win.Summary()} · 특훈권 {TrainingChancePercent(tier, true)}% · 트로피 {TrophyChancePercent(tier, true)}%\n" +
                $"■ 시즌 우승: {champion.Summary()} (상위 리그일수록 증가)\n" +
                $"■ 스킬 변경권: 리그 승리 {SkillTicketChancePercent(tier, true)}% · 시즌 결산(우승 · 준우승 · 포스트시즌) · 상점(포인트 12,000 / 코인 150)  ■ 고급 스킬 변경권: 우승 · 교환소(코인 600 / 트로피 1)\n" +
                "■ 개인 타이틀(타격왕 · 홈런왕 · 타점왕 · 다승왕 · 평균자책점 · 탈삼진): 우리 구단 수상 1개당 트로피 +1 · 성장 코인 +100 · 포인트 +30,000\n" +
                "■ 선수 방출: 라인업 외 카드 → 포인트 + 성장 코인(등급 · 강화 비례)\n" +
                "■ 3:1 포지션 재조합: 같은 시즌 등급 3장 → 지정 포지션 1장(선택 구단 우선) - 각성 +1각 · 초월 보조 재료\n" +
                "■ 강화 재료: 강화 보조팩(포인트) · 아무 보관 카드  ■ 특훈: 특훈권(훈련/특훈 재료 상자 · 리그 승리)\n" +
                "■ 각성: 같은 선수(+3각) · 같은 포지션(+1각) - 스페셜팩 · 재조합 · 범용 각성 보조권(코인 500)\n" +
                "■ 초월: 같은 선수 1장(또는 초월 핵심 대체권 코인 1,500) + 같은 포지션 2장(+5강이면 1장) + 포인트 · 트로피\n" +
                "■ 특별 영입 재료: 골글/시그니처 재료 상자(코인 800 - 스페셜 · +3강/+6강 카드)";
        }
    }
}
