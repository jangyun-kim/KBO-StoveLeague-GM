using System;
using UnityEngine;

namespace KBOManager.Models
{
    /// <summary>[TASK-GM-05] 치어리더 4대 응원 스탯(기획서 3절).</summary>
    public enum GMCheerStat
    {
        Leadership = 0, // ① 단장 리더십 - 라커룸 분위기 · 팀워크 가산
        Batting = 1,    // ② 타격 응원력 - 아군 타선 · 후반 클러치
        Mound = 2,      // ③ 마운드 응원력 - 투수 제구 안정 · 야수 실책 감소
        HomeDraw = 3,   // ④ 홈 흥행력 - 홈경기 관중 수익 · 팬 지지율
    }

    /// <summary>
    /// [TASK-GM-05] 치어리더 4대 응원 스탯 · 열정도(CHEER) · 체력 규칙(순수 계산). 기존 성장(강화 +0~+10 · ★1~★5 각성)이 그대로 스탯에 반영된다.
    ///   기본값 = 38 + 유효 티어(등급 + ★3/★5 도약) × 7 + 강화 × 2 + (★ - 1) × 3, 여기에 인물별 고정 편차(-5~+5)와 카드 고유 버프를 더해 1~99로 자른다.
    ///   열정도(CHEER) = 4대 스탯 평균. 체력 = 100 - 피로도(Cheerleader.Fatigue), 체력 30 미만이면 응원 효율 50%.
    /// 모든 값은 경기 조건부 · 경기 결산 효과에만 쓰이며 구단 OVR(라인업 평균)에는 더하지 않는다(TASK-184 원칙).
    /// </summary>
    public static class GMCheerleaderStats
    {
        public const int MinStat = 1, MaxStat = 99;
        public const int TiredStamina = 30;
        public const int DrainPerGame = 9;     // 단상 출전 경기당 체력 소모
        public const int RecoverPerGame = 14;  // 벤치(15인 풀 비엔트리) 경기당 회복
        public static readonly string[] StatLabels = { "단장 리더십", "타격 응원력", "마운드 응원력", "홈 흥행력" };

        public static int BaseValue(Cheerleader c)
        {
            if (c == null) return MinStat;
            return 38 + CheerGrowth.EffectiveTier(c) * 7 + CheerGrowth.Reinforce(c) * 2 + (CheerGrowth.Stars(c) - 1) * 3;
        }

        public static int Stat(Cheerleader c, GMCheerStat stat)
        {
            if (CheerSquad.IsEmpty(c)) return 0;
            int v = BaseValue(c) + Bias(c, (int)stat);
            switch (stat)
            {
                case GMCheerStat.Leadership: v += c.ConditionBuff * 2 + c.SentimentDefense; break;
                case GMCheerStat.Batting: v += Sane(c.ClutchMultiplier, 100f); break;
                case GMCheerStat.Mound: v += c.ConditionBuff; break;
                default: v += Sane(c.EconomicBonusRate, 100f); break;
            }
            return Math.Max(MinStat, Math.Min(MaxStat, v));
        }

        public static int Leadership(Cheerleader c) => Stat(c, GMCheerStat.Leadership);
        public static int Batting(Cheerleader c) => Stat(c, GMCheerStat.Batting);
        public static int Mound(Cheerleader c) => Stat(c, GMCheerStat.Mound);
        public static int HomeDraw(Cheerleader c) => Stat(c, GMCheerStat.HomeDraw);

        /// <summary>열정도(CHEER) = 4대 스탯 평균(반올림).</summary>
        public static int Cheer(Cheerleader c)
        {
            if (CheerSquad.IsEmpty(c)) return 0;
            return (int)Math.Round((Leadership(c) + Batting(c) + Mound(c) + HomeDraw(c)) / 4.0, MidpointRounding.AwayFromZero);
        }

        public static int Stamina(Cheerleader c) => c == null ? 0 : Math.Max(0, Math.Min(100, 100 - c.Fatigue));
        public static bool IsTired(Cheerleader c) => Stamina(c) < TiredStamina;
        /// <summary>응원 효율 - 체력 30 미만이면 절반.</summary>
        public static float Efficiency(Cheerleader c) => IsTired(c) ? 0.5f : 1f;

        /// <summary>경기 1회 결과 - 단상 출전이면 체력 소모, 벤치면 회복.</summary>
        public static void TickStamina(Cheerleader c, bool onStage)
        {
            if (c == null) return;
            c.Fatigue = Math.Max(0, Math.Min(100, c.Fatigue + (onStage ? DrainPerGame : -RecoverPerGame)));
        }

        /// <summary>★ 각성 테두리 - ★1~2 기본(은색), ★3~4 골드, ★5 프리즘.</summary>
        public static Color StarFrameColor(Cheerleader c)
        {
            int stars = CheerGrowth.Stars(c);
            if (stars >= CheerGrowth.MaxStars) return new Color(0.78f, 0.55f, 1f);   // 프리즘
            if (stars >= CheerGrowth.FirstStarThreshold) return new Color(1f, 0.8f, 0.25f); // 골드
            return new Color(0.75f, 0.78f, 0.85f);
        }

        public static string StarFrameLabel(Cheerleader c)
        {
            int stars = CheerGrowth.Stars(c);
            return stars >= CheerGrowth.MaxStars ? "프리즘 테두리" : stars >= CheerGrowth.FirstStarThreshold ? "골드 테두리" : "기본 테두리";
        }

        private static int Sane(float rate, float scale)
        {
            if (float.IsNaN(rate) || float.IsInfinity(rate)) return 0;
            return (int)Math.Round(Math.Max(0f, Math.Min(0.3f, rate - 1f)) * scale);
        }

        /// <summary>인물별 고정 편차(-5~+5) - 같은 사람은 항상 같은 성향(이름 해시).</summary>
        private static int Bias(Cheerleader c, int slot)
        {
            uint h = 2166136261;
            foreach (char ch in CheerSquad.PersonKey(c)) h = (h ^ ch) * 16777619;
            h ^= (uint)(slot + 1) * 2654435761u;
            h *= 16777619;
            return (int)(h % 11) - 5;
        }
    }
}

namespace KBOManager.Models
{
    /// <summary>[TASK-GM-05] 전담 응원 매칭 1건 - 에이스/리더 치어리더가 Ego 4+ 불만 스타의 전담 응원가 · 단상 이벤트를 맡는다.</summary>
    [System.Serializable]
    public class GMCheerDedication
    {
        public string CheerleaderId;
        public string PlayerId;
        public bool GrantedConcession; // 매칭으로 보직 양보 인센티브 동급 효과를 새로 부여했는지(해제 시 되돌림)
    }
}
