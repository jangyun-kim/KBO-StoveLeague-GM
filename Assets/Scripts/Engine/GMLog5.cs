using System;
using System.Collections.Generic;
using System.Globalization;
using KBOManager.Data;
using KBOManager.Models;

namespace KBOManager.Engine
{
    /// <summary>
    /// [TASK-GM-10] 선수 기준 시즌 원 기록(Raw Stats). 타자: PA · AB · H · HR · BB · SO / 투수: BF · IP(아웃 수) · H · HR · BB · SO.
    /// Source = Real(실제 기록 CSV) 또는 Estimated(데이터가 없어 세부 능력치로 추정 - Fallback).
    /// 현재 프로젝트 데이터(players.csv)에는 원 기록 컬럼이 없어 전 선수가 Estimated다 - Resources/Data/player_raw_stats.csv를 넣으면 그 선수는 Real이 된다.
    /// </summary>
    [Serializable]
    public class PerformanceData
    {
        public bool IsReal;
        public int PA, AB, H, HR, BB, SO;            // 타자
        public int BF, IPOuts, PH, PHR, PBB, PSO;    // 투수

        public double Avg => AB <= 0 ? GMLog5.LgAvg : (double)H / AB;
        public double BBRate => PA <= 0 ? GMLog5.LgBB : (double)BB / PA;
        public double KRate => PA <= 0 ? GMLog5.LgK : (double)SO / PA;
        public double HRPerHit => H <= 0 ? GMLog5.LgHRPerHit : (double)HR / H;
        public double OAvg => BF - PBB <= 0 ? GMLog5.LgAvg : (double)PH / (BF - PBB);
        public double PBBRate => BF <= 0 ? GMLog5.LgBB : (double)PBB / BF;
        public double PKRate => BF <= 0 ? GMLog5.LgK : (double)PSO / BF;
        public double PHRPerHit => PH <= 0 ? GMLog5.LgHRPerHit : (double)PHR / PH;
    }

    /// <summary>
    /// [TASK-GM-10] Sabermetrics Log5(Bill James) 타석 판정 엔진 - 단장 모드 경기 전용(레거시 카드 모드는 기존 능력치 확률표 그대로).
    ///   Log5(b, p, l) = (b·p/l) / (b·p/l + (1-b)(1-p)/(1-l))  - b = 타자 비율, p = 투수 비율, l = 리그 평균
    ///   타석 판정 순서: ① 볼넷(BB%/PA) → ② 타수 중 안타(AVG · OAVG · LgAVG) → ③ 안타 중 홈런(HR/H) · 나머지 2루타/3루타/단타 분배(파워 · 주력)
    ///                  → ④ 범타 중 삼진(K/아웃 - K%를 아웃 기준으로 환산) · 나머지 땅볼/뜬공(파워)
    ///   선수 비율 = 원 기록(PerformanceData) - 경기 중 버프(컨디션 · 스킬 · 치어리더 · 체급 · 작전 · 피로)로 세부 스탯이 바뀌면 그 차이만큼 로짓 이동.
    ///   단장 역학(최종 곱연산, 오즈 배수): 실효 전력 계수 비(공격 팀 ÷ 수비 팀, 하한 0.90) · 클러치 · 위기 응원 · ABS · 리그 환경 정규화.
    /// </summary>
    public static class GMLog5
    {
        // 리그 기준값(KBO 실측 근사 · GM-08/09 기준대) - 장타는 GM-09 +12% 반영
        public const double LgAvg = 0.265, LgBB = 0.092, LgK = 0.180;
        public static double LgHRPerHit = 0.092;
        public const double LgDoubleShare = 0.21, LgTripleShare = 0.019, LgFlyShare = 0.48;
        public const int NominalPA = 550, NominalBF = 650;

        // 추정식 기준(2026 현역 주전 평균 능력치) · 기울기(능력치 1점당 로짓) - 하네스 실측으로 정함
        public const double MeanContact = 68.2, MeanEye = 63.8, MeanPower = 66.8, MeanSpeed = 60;
        public const double MeanStuff = 62.3, MeanVelocity = 61.5, MeanMovement = 58.1, MeanControl = 58.1;
        public static double SlopeAvg = 0.013, SlopeBB = 0.030, SlopeK = 0.030, SlopeHR = 0.040;
        public static double PitchSlopeAvg = 0.008, PitchSlopeBB = 0.020, PitchSlopeK = 0.022, PitchSlopeHR = 0.018; // 투수 편차 압축(ERA 1위 운 편차 완화 - 하네스 실측)
        // 비율 상하한(현실 범위) - S급 투수 피안타율 하한 .210
        /// <summary>실효 전력 계수 비(공격 ÷ 수비)를 안타 오즈에 반영하는 지수(1 = 그대로).</summary>
        public static double PowerRatioExponent = 0.5; // 팀 단위 편차만 줄인다(개인 타격 편차는 유지) - 10위 붕괴 방지(하네스 실측)
        public static double MinAvg = 0.180, MaxAvg = 0.355, MinOAvg = 0.228, MaxOAvg = 0.320;

        /// <summary>
        /// 판정 확률 양자화(1e-7) - Mono(Unity)와 .NET의 Math.Exp/Log 마지막 자리 차이가 난수 비교를 뒤집지 않게 해 런타임이 달라도 같은 시드 = 같은 시즌.
        /// </summary>
        public static double Q(double p) => Math.Round(p, 7);

        public static double Logit(double p) { p = Clamp01(p); return Math.Log(p / (1 - p)); }
        public static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-x));
        private static double Clamp01(double p) => Math.Max(1e-4, Math.Min(1 - 1e-4, p));

        /// <summary>Bill James Log5.</summary>
        public static double Log5(double batter, double pitcher, double league)
        {
            batter = Clamp01(batter); pitcher = Clamp01(pitcher); league = Clamp01(league);
            double num = batter * pitcher / league;
            double den = num + (1 - batter) * (1 - pitcher) / (1 - league);
            return num / den;
        }

        /// <summary>확률에 오즈 배수를 곱한다(0~1 유지).</summary>
        public static double ApplyOdds(double p, double mult)
        {
            if (mult <= 0) return 0;
            p = Clamp01(p);
            double odds = p / (1 - p) * mult;
            return odds / (1 + odds);
        }

        // ================================================================== 원 기록 · 추정

        private static readonly Dictionary<string, PerformanceData> real = new Dictionary<string, PerformanceData>();
        private static bool realLoaded;

        /// <summary>실제 원 기록 CSV(선택) - 헤더: real_player_id,season_year,pa,ab,h,hr,bb,so,bf,ip_outs,p_h,p_hr,p_bb,p_so. 등록한 행 수.</summary>
        public static int LoadRealStats(string csv)
        {
            int n = 0;
            if (string.IsNullOrEmpty(csv)) return n;
            var lines = csv.Replace("\r\n", "\n").Split('\n');
            for (int i = 1; i < lines.Length; i++)
            {
                var c = lines[i].Trim().Split(',');
                if (c.Length < 14) continue;
                int I(int k) => int.TryParse(c[k].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
                real[$"{c[0].Trim()}|{I(1)}"] = new PerformanceData
                {
                    IsReal = true, PA = I(2), AB = I(3), H = I(4), HR = I(5), BB = I(6), SO = I(7),
                    BF = I(8), IPOuts = I(9), PH = I(10), PHR = I(11), PBB = I(12), PSO = I(13),
                };
                n++;
            }
            return n;
        }

        private static void EnsureRealLoaded()
        {
            if (realLoaded) return;
            realLoaded = true;
            try
            {
                var asset = UnityEngine.Resources.Load<UnityEngine.TextAsset>("Data/player_raw_stats");
                if (asset != null) LoadRealStats(asset.text);
            }
            catch (Exception) { /* Unity 밖(툴 · 하네스) - 실제 기록 없이 추정만 */ }
        }

        /// <summary>선수 기준 시즌 원 기록 - 실제 기록이 있으면 그것, 없으면(또는 퓨처스 유망주 보정 선수) 세부 능력치 추정.</summary>
        public static PerformanceData For(Player p)
        {
            if (p?.Template == null) return new PerformanceData();
            EnsureRealLoaded();
            if (p.ProspectStatShift == 0 && real.TryGetValue($"{p.Template.RealPlayerId}|{p.Template.SeasonYear}", out var r)) return r;
            return Estimate(p.Template);
        }

        /// <summary>Fallback - 세부 능력치 → 원 기록(규정 타석 550 · 상대 타자 650 기준 환산).</summary>
        public static PerformanceData Estimate(PlayerTemplate t)
        {
            var d = new PerformanceData();
            if (t.IsPitcher)
            {
                var s = t.PitcherStats;
                double oavg = PitcherAvg(s), bb = PitcherBB(s), k = PitcherK(s), hrh = PitcherHRPerHit(s);
                d.BF = NominalBF;
                d.PBB = (int)Math.Round(d.BF * bb);
                d.PH = (int)Math.Round((d.BF - d.PBB) * oavg);
                d.PHR = (int)Math.Round(d.PH * hrh);
                d.PSO = (int)Math.Round(d.BF * k);
                d.IPOuts = Math.Max(1, d.BF - d.PBB - d.PH);
            }
            else
            {
                var s = t.BatterStats;
                double avg = BatterAvg(s), bb = BatterBB(s), k = BatterK(s), hrh = BatterHRPerHit(s);
                d.PA = NominalPA;
                d.BB = (int)Math.Round(d.PA * bb);
                d.AB = d.PA - d.BB;
                d.H = (int)Math.Round(d.AB * avg);
                d.HR = (int)Math.Round(d.H * hrh);
                d.SO = (int)Math.Round(d.PA * k);
            }
            return d;
        }

        public static double BatterAvg(BatterStats s) => Math.Max(MinAvg, Math.Min(MaxAvg, Sigmoid(Logit(LgAvg) + SlopeAvg * (s.Contact - MeanContact))));
        public static double BatterBB(BatterStats s) => Sigmoid(Logit(LgBB) + SlopeBB * (s.Discipline - MeanEye));
        public static double BatterK(BatterStats s) => Sigmoid(Logit(LgK) - SlopeK * ((s.Contact + s.Discipline) / 2.0 - (MeanContact + MeanEye) / 2.0));
        public static double BatterHRPerHit(BatterStats s) => Sigmoid(Logit(LgHRPerHit) + SlopeHR * (s.Power - MeanPower));
        public static double PitcherAvg(PitcherStats s) => Math.Max(MinOAvg, Math.Min(MaxOAvg, Sigmoid(Logit(LgAvg) - PitchSlopeAvg * ((s.Stuff + s.Movement) / 2.0 - (MeanStuff + MeanMovement) / 2.0))));
        public static double PitcherBB(PitcherStats s) => Sigmoid(Logit(LgBB) - PitchSlopeBB * (s.Control - MeanControl));
        public static double PitcherK(PitcherStats s) => Sigmoid(Logit(LgK) + PitchSlopeK * ((s.Stuff + s.Velocity) / 2.0 - (MeanStuff + MeanVelocity) / 2.0));
        public static double PitcherHRPerHit(PitcherStats s) => Sigmoid(Logit(LgHRPerHit) - PitchSlopeHR * (s.Movement - MeanMovement));

        /// <summary>원 기록 비율을 경기 중 세부 스탯 변화(유효 스탯 - 기본 스탯)만큼 로짓 이동한다(버프 · 피로 · 작전 반영).</summary>
        public static double Shift(double rate, double slope, double delta, double min = 1e-4, double max = 1 - 1e-4) =>
            Math.Max(min, Math.Min(max, Sigmoid(Logit(rate) + slope * delta)));

        /// <summary>
        /// [TASK-GM-10] 리그 상대 비율 - 원 기록 비율의 리그 평균 대비 로짓 편차에 경기 중 변화(shift)를 더하고 리그 분산(spread ≥ 1)으로 나눈다.
        /// 2026 현역 리그는 spread ≈ 1(그대로), 올타임 드림처럼 격차가 큰 리그는 편차가 줄어 구단 간 승률 붕괴를 막는다.
        /// </summary>
        public static double Relative(double rate, double league, double shift, double spread, double min = 1e-4, double max = 1 - 1e-4)
        {
            double dev = (Logit(rate) - Logit(league) + shift) / Math.Max(1.0, spread);
            return Math.Max(min, Math.Min(max, Sigmoid(Logit(league) + dev)));
        }

        /// <summary>K%(타석당) → 범타(아웃) 중 삼진 비율.</summary>
        public static double KPerOut(double kPerPA, double bbRate, double avg) => Clamp01(kPerPA / Math.Max(0.05, (1 - bbRate) * (1 - avg)));
    }
}
