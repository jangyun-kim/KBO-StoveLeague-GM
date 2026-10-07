using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;

namespace KBOManager.Engine
{
    /// <summary>
    /// [TASK-GM-08] 단장 모드 타격 밸런스(타율 .508 인플레이션 긴급 패치). 단장 모드 경기(케미스트리 있음)에서만 MatchEngine이 이 값을 쓴다 - 레거시 카드 모드는 그대로.
    ///   원인(Python 재현 · 2026 로스터 기준): ① 기본 확률표의 안타 비중 27%(단타 .16) ② 타자 능력치 평균이 투수보다 정확 +5.9 · 선구 +5.7 · 파워 +8.6 높아
    ///   평균 매치업부터 타자 우위 ③ 스킬 영향력 0.6 · 정규화 상한 ±1 → 정확 104 타자는 단타 ×1.6 · 범타 ×0.4 → 리그 .327 · 장타율 .574 · 1위 .565.
    ///   해법: ① KBO 실측형 기본 확률표(K 18.5% · BB 9.2% · 단타 17.5% · 2루타 4.5% · 3루타 0.4% · 홈런 2.4%)
    ///         ② 매치업 격차를 리그 평균 격차 기준으로 센터링(평균 타자 vs 평균 투수 = 기본 확률표)
    ///         ③ 스킬 영향력 0.6 유지 + 격차 상한 ±0.3(극단 매치업 압축 - 타격왕 .340~.385)
    ///         ④ OOTP식 리그 환경 정규화 - 경기일마다 리그 누적 타율을 목표 .265로 끌어당기는 안타 가중치 배수(0.8~1.2, 게인 0.15)
    ///         ⑤ 투수 피로 누적 - 체력 60% 미만부터 구위 · 구속 · 변화 · 제구가 선형으로 최대 -15%(30% 미만 계단식 -15%를 대체)
    /// </summary>
    public sealed class GMBattingBalance
    {
        public const float TargetLeagueAvg = 0.265f;
        public const float SkillInfluence = 0.6f;
        public const float StatDiffNormalizer = 50f;
        public static float SkillCap = 0.42f; // [TASK-GM-09] 타자 우위 쪽 상한(투수 우위 쪽은 PitcherSkillFloor)
        public const float EnvironmentGain = 0.15f;
        public const float EnvironmentMin = 0.8f, EnvironmentMax = 1.2f;
        public const int EnvironmentMinAtBats = 1500;
        public const float FatigueStartRatio = 0.6f, FatigueMaxPenalty = 0.15f;
        /// <summary>실책 판정 범타 비중 보정 - 새 확률표는 범타(땅볼 · 뜬공) 비중이 43% → 49%로 늘어, 기본 실책률(0.048)의 타석당 빈도를 유지하도록 0.875배로 판정한다(팀당 약 90~97개).</summary>
        public const double ErrorScale = 0.875;

        /// <summary>단장 모드 기본 확률표(합계 1.035 - 가중치라 정규화된다).</summary>
        public static readonly IReadOnlyDictionary<AtBatResult, float> BaseWeights = new Dictionary<AtBatResult, float>
        {
            { AtBatResult.Strikeout, 0.185f }, { AtBatResult.Walk, 0.092f }, { AtBatResult.Groundout, 0.265f }, { AtBatResult.Flyout, 0.245f },
            { AtBatResult.Single, 0.175f }, { AtBatResult.Double, 0.045f }, { AtBatResult.Triple, 0.004f }, { AtBatResult.HomeRun, 0.024f },
        };

        // 리그 평균 매치업 격차(센터링 기준) - 삼진: (구위+구속)/2 - (정확+선구)/2, 볼넷: 선구 - 제구, 안타: 정확 - 구위, 장타: 파워 - 변화
        public float StrikeoutOffset, WalkOffset, ContactOffset, PowerOffset;
        /// <summary>리그 환경 정규화 배수 - 안타(단타 · 2루타 · 3루타 · 홈런) 가중치에 곱한다.</summary>
        public float EnvironmentHitFactor = 1f;

        // [TASK-GM-09] 투고타저 완화 · 약팀 붕괴 방지 노브(단장 모드 전용 - 하네스 실측으로 정한 값)
        /// <summary>장타(2루타 · 3루타 · 홈런) 가중치 배수 - 기본 확률표 대비 +12%.</summary>
        public static float ExtraBaseBoost = 1.12f;
        /// <summary>투수 우위 방향 격차 하한(안타 · 장타 판정) - S급 투수도 피안타율이 .210 아래로 내려가지 않게 -0.3 → -0.18.</summary>
        public static float PitcherSkillFloor = -0.12f;
        /// <summary>체급 우위(ΔOVR ≥ 8) 세부 스탯 가산 배수 - 단장 모드는 40%만 적용(+10~+24 → +4~+10).</summary>
        public static float ClassBonusScale = 0.2f;
        /// <summary>대승 감쇠 - 단장 모드는 빅이닝(4득점) 감쇠를 끄고 8점 차 이상 · 두 자릿수 득점에서만 식힌다.</summary>
        public static bool DisableBigInningDamping = true;
        /// <summary>[TASK-GM-09] 주자 추가 진루(KBO 실측 근사) - 단타 2루 주자 득점 · 단타 1루→3루 · 2루타 1루 주자 득점 · 땅볼 진루 · 땅볼 3루 주자 득점 · 뜬공 2루 주자 태그업.</summary>
        public static float SingleScoresFromSecond = 0.58f, SingleFirstToThird = 0.28f, DoubleScoresFromFirst = 0.42f;
        public static float GroundoutAdvance = 0.45f, GroundoutScoresFromThird = 0.35f, FlyoutTagFromSecond = 0.25f;

        public static float BaseWeight(AtBatResult result, float fallback)
        {
            float w = BaseWeights.TryGetValue(result, out var v) ? v : fallback;
            return result == AtBatResult.Double || result == AtBatResult.Triple || result == AtBatResult.HomeRun ? w * ExtraBaseBoost : w;
        }

        /// <summary>격차 → 스킬(-0.3 ~ +0.3). offset은 리그 평균 격차.</summary>
        public static float Skill(float diff, float offset) => Math.Max(-SkillCap, Math.Min(SkillCap, (diff - offset) / StatDiffNormalizer));

        /// <summary>[TASK-GM-09] 안타 · 장타 판정 스킬 - 투수 우위 쪽은 PitcherSkillFloor까지만(에이스 피안타율 하한 보장).</summary>
        public static float HitSkill(float diff, float offset) => Math.Max(PitcherSkillFloor, Skill(diff, offset));

        public static int ScaleClassBonus(int bonus) => bonus <= 0 ? bonus : (int)Math.Round(bonus * ClassBonusScale);

        /// <summary>리그 로스터(주전 타자 · 투수)로 센터링 기준을 만든다. 타자는 구단별 OVR 상위 9명, 투수는 전원.</summary>
        public static GMBattingBalance FromRosters(IEnumerable<IEnumerable<Player>> teamRosters)
        {
            var batters = new List<Player>();
            var pitchers = new List<Player>();
            foreach (var roster in teamRosters ?? Enumerable.Empty<IEnumerable<Player>>())
            {
                var list = (roster ?? Enumerable.Empty<Player>()).Where(p => p?.Template != null).ToList();
                batters.AddRange(list.Where(p => !p.IsPitcher).OrderByDescending(p => p.BaseOverall).Take(9));
                pitchers.AddRange(list.Where(p => p.IsPitcher));
            }
            var b = new GMBattingBalance();
            if (batters.Count == 0 || pitchers.Count == 0) return b;
            double con = batters.Average(p => p.Template.BatterStats.Contact), eye = batters.Average(p => p.Template.BatterStats.Discipline), pow = batters.Average(p => p.Template.BatterStats.Power);
            double stf = pitchers.Average(p => p.Template.PitcherStats.Stuff), vel = pitchers.Average(p => p.Template.PitcherStats.Velocity);
            double mov = pitchers.Average(p => p.Template.PitcherStats.Movement), ctl = pitchers.Average(p => p.Template.PitcherStats.Control);
            b.StrikeoutOffset = (float)((stf + vel) / 2 - (con + eye) / 2);
            b.WalkOffset = (float)(eye - ctl);
            b.ContactOffset = (float)(con - stf);
            b.PowerOffset = (float)(pow - mov);
            return b;
        }

        /// <summary>리그 환경 정규화 1회(경기일 종료) - 누적 타율이 목표보다 높으면 안타 배수를 낮추고, 낮으면 올린다.</summary>
        public void UpdateEnvironment(int leagueHits, int leagueAtBats)
        {
            if (leagueAtBats < EnvironmentMinAtBats || leagueHits <= 0) return;
            double avg = (double)leagueHits / leagueAtBats;
            EnvironmentHitFactor = (float)Math.Max(EnvironmentMin, Math.Min(EnvironmentMax, EnvironmentHitFactor * Math.Pow(TargetLeagueAvg / avg, EnvironmentGain)));
        }

        /// <summary>투수 피로 배수(1.0 ~ 0.85) - 체력 비율 60% 미만부터 선형 감소.</summary>
        public static float FatigueMultiplier(float staminaRatio)
        {
            if (staminaRatio >= FatigueStartRatio) return 1f;
            float t = Math.Max(0f, Math.Min(1f, (FatigueStartRatio - staminaRatio) / FatigueStartRatio));
            return 1f - FatigueMaxPenalty * t;
        }
    }
}
