using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-19] 실시간 경기 전술 대시보드 데이터(LiveMatchInningView 개편).
    ///   - 코스별 피안타율(Hot/Cold Zone 3×3): 시즌 피안타율(표본 10이닝 이상, 없으면 구위 · 제구 · 변화 추정) × 코스 가중치(한가운데 · 몸쪽 높은 공 = 뜨거움,
    ///     낮은 바깥쪽 = 차가움) + 제구가 좋을수록 모서리가 더 차갑고, 선수별 고정 편차(±0.015)를 더한다. 표시 전용 - 타석 판정에는 쓰지 않는다.
    ///   - 투구수 · 체력 게이지: 오늘 투구수와 현재 체력 비율(35% 미만 = 교체 권고, 60% 미만 = 피로 구간).
    ///   - 단장 개입 지시([투수 교체 지시] · [대타 기용 지시]): 현장(감독)이 받아들일 확률 = 50% + (감독 신뢰도 - 50) × 0.8%p + 상황 보정
    ///     (투수 체력 35% 미만 +25%p · 60% 미만 +10%p · 체력 충분 -5%p, 대타 +5%p), 15~95%. 하프이닝마다 종류별 1회, 판정은 경기 결과 난수열과 분리된 결정적 난수.
    /// </summary>
    public static class GMLiveTactics
    {
        public const int ZoneCells = 9;
        public const int SampleOuts = 30;
        public const float MinBaa = 0.12f, MaxBaa = 0.45f;
        public const float TiredRatio = 0.6f, HookRatio = 0.35f;
        public const float OrderBase = 0.5f, OrderTrustSlope = 0.008f, OrderMin = 0.15f, OrderMax = 0.95f;

        // 행 0 = 높은 공, 열 0 = 3루 쪽(우타자 몸쪽) - 코스 가중치
        private static readonly float[] ZoneShape =
        {
            -0.030f, 0.012f, -0.028f,
             0.018f, 0.062f,  0.010f,
            -0.036f, 0.002f, -0.040f,
        };
        private static readonly bool[] Corner = { true, false, true, false, false, false, true, false, true };

        /// <summary>시즌 피안타율(10이닝 이상) - 없으면 null.</summary>
        public static float? SeasonBaa(GMLeagueState league, Player p)
        {
            if (league == null || p == null || !league.Stats.TryGetValue(p.InstanceId, out var s) || s.OutsPitched < SampleOuts) return null;
            return s.HA / (float)Math.Max(1, s.OutsPitched + s.HA);
        }

        /// <summary>능력치 추정 피안타율(.200~.320).</summary>
        public static float EstimatedBaa(Player p)
        {
            if (p?.Template == null || !p.IsPitcher) return 0.265f;
            var st = p.Template.PitcherStats;
            float avg = (st.Stuff + st.Control + st.Movement) / 3f;
            return Math.Max(0.2f, Math.Min(0.32f, 0.3f - (avg - 50f) * 0.002f));
        }

        /// <summary>코스별 피안타율 9칸(행 우선, 위 → 아래). estimated = 시즌 표본이 부족해 능력치 추정으로 만들었는지.</summary>
        public static float[] ZoneMap(GMLeagueState league, Player p, out bool estimated)
        {
            var map = new float[ZoneCells];
            var season = SeasonBaa(league, p);
            estimated = !season.HasValue;
            float baseBaa = season ?? EstimatedBaa(p);
            float control = p?.Template != null && p.IsPitcher ? (p.Template.PitcherStats.Control - 50f) / 50f : 0f;
            string id = p?.Template?.RealPlayerId ?? p?.InstanceId ?? "";
            for (int i = 0; i < ZoneCells; i++)
            {
                float personal = (GMFrontOffice.Hash($"{id}_zone_{i}") % 31 - 15) / 1000f;
                float edge = Corner[i] ? -0.02f * control : i == 4 ? 0.012f * -control : 0f;
                map[i] = Math.Max(MinBaa, Math.Min(MaxBaa, baseBaa + ZoneShape[i] + edge + personal));
            }
            return map;
        }

        // 타자 코스 가중치(한가운데 · 가운데 높은 공 = 강함, 낮은 바깥쪽 = 약함)
        private static readonly float[] BatterShape =
        {
            -0.035f, 0.02f, -0.03f,
             0.012f, 0.07f,  0.015f,
            -0.03f, 0.005f, -0.045f,
        };
        public const int SamplePa = 30;

        /// <summary>[TASK-GM-19] 타자 코스별 타율 9칸(참고 시안 - 투수 피안타율과 나란히). 시즌 30타석 이상 = 시즌 타율 기준, 아니면 정확 · 파워 추정.</summary>
        public static float[] BatterZoneMap(GMLeagueState league, Player p, out bool estimated)
        {
            var map = new float[ZoneCells];
            GMPlayerSeasonStats s = null;
            bool season = league != null && p != null && league.Stats.TryGetValue(p.InstanceId, out s) && s.PA >= SamplePa && s.AB > 0;
            estimated = !season;
            float baseAvg;
            if (season) baseAvg = (float)s.AVG;
            else if (p?.Template != null && !p.IsPitcher) baseAvg = Math.Max(0.2f, Math.Min(0.34f, 0.255f + (p.Template.BatterStats.Contact - 50f) * 0.0022f + (p.Template.BatterStats.Power - 50f) * 0.0006f));
            else baseAvg = 0.2f;
            string id = p?.Template?.RealPlayerId ?? p?.InstanceId ?? "";
            for (int i = 0; i < ZoneCells; i++)
            {
                float personal = (GMFrontOffice.Hash($"{id}_bat_zone_{i}") % 41 - 20) / 1000f;
                map[i] = Math.Max(MinBaa, Math.Min(MaxBaa, baseAvg + BatterShape[i] + personal));
            }
            return map;
        }

        /// <summary>[TASK-GM-19] 치어리더 응원 말풍선(참고 시안 "얼쑤~!") - 내 구단 공격 · 득점권 · 점수 상황별 한마디.</summary>
        public static string CheerShout(bool userBatting, bool scoringPosition, int userScore, int oppScore, string batterName)
        {
            if (userBatting && scoringPosition) return string.IsNullOrEmpty(batterName) ? "적시타 가자!" : $"{batterName} 적시타!";
            if (userBatting) return userScore < oppScore ? "할 수 있다!" : "얼쑤~!";
            if (userScore > oppScore) return "끝까지 지켜라!";
            return userScore < oppScore ? "포기는 없다!" : "막아라~!";
        }

        /// <summary>현재 체력 비율(0~1, 체력 개념이 없으면 1).</summary>
        public static float StaminaRatio(Player p) => p == null || p.MaxStamina <= 0 ? 1f : Math.Max(0f, Math.Min(1f, p.CurrentStamina / (float)p.MaxStamina));

        public static string StaminaNote(float ratio) =>
            ratio < HookRatio ? "교체 권고 구간" : ratio < TiredRatio ? "피로 누적" : "구위 유지";

        /// <summary>단장 지시를 현장이 받아들일 확률.</summary>
        public static float OrderChance(GMTeamState team, bool pitchingChange, float staminaRatio)
        {
            int trust = team?.ManagerTrust ?? GMTeamState.DefaultManagerTrust;
            float situation = pitchingChange ? (staminaRatio < HookRatio ? 0.25f : staminaRatio < TiredRatio ? 0.1f : -0.05f) : 0.05f;
            return Math.Max(OrderMin, Math.Min(OrderMax, OrderBase + (trust - 50) * OrderTrustSlope + situation));
        }

        /// <summary>지시 판정 난수(경기 · 타석 · 지시 종류로 결정 - 경기 결과 난수열과 분리).</summary>
        public static double OrderRoll(int seed, int gameIndex, int plateAppearances, bool pitchingChange) =>
            new Random(seed ^ (gameIndex * 7919) ^ (plateAppearances * 131) ^ (pitchingChange ? 0x51 : 0xA3)).NextDouble();
    }
}
