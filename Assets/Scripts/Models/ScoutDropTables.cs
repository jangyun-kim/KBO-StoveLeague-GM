using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-176] 선수 스카우트(가챠) 상품별 등급 확률표 - ScoutManager가 이 표로 등급을 추첨한다(순수 데이터라
    /// 산술 검증을 Unity 없이 할 수 있도록 MonoBehaviour에서 분리했다). 각 표는 위상 순번(CardGrowthRules.PowerRank)
    /// 내림차순으로 나열하고 합계는 정확히 100%다 - 마지막 "기본(소비) 등급"은 100에서 나머지를 뺀 값이라 다른 칸을
    /// 바꿔도 합계가 깨지지 않는다.
    ///
    /// FRANCHISE(PowerRank 4: ALLSTAR 3 &lt; FRANCHISE &lt; TITLE_HOLDER 5) 편입(DCL-144/145 미결 "FRANCHISE 획득처" 해소):
    ///   시그니처 상품    SIG 0.5 / TH 1.5 / FRA 2.0 / AS 3.0 / LIVE_EPIC 93.0   (이전: SIG 0.5 / TH 1.5 / AS 3.0 / LE 95.0)
    ///   타이틀홀더 상품  TH 0.5 / FRA 1.0 / AS 1.5 / LE 3.0 / LIVE_NORMAL 94.0  (이전: TH 0.5 / AS 1.5 / LE 3.0 / LN 95.0)
    /// 기존 상위 등급 확률은 한 칸도 바꾸지 않고, FRANCHISE 몫은 기본(소비) 등급에서만 덜어냈다. 등급이 낮을수록 확률이
    /// 높아지는 단조 관계(위상 역순)를 유지하도록 FRANCHISE는 바로 위(TH)와 바로 아래(AS) 사이 값으로 정했다.
    /// 라이브 일반/라이브 에픽 상품은 "등급 확정" 상품(LIVE_NORMAL 100% / LIVE_EPIC 100%)이라 편입 대상이 아니다.
    ///
    /// [TASK-KBO-182] 등급 서열 정합성 교정: 위상 서열은 LIVE_NORMAL(+0) &lt; LIVE_EPIC(+1) &lt; ALLSTAR(+3) &lt; FRANCHISE(+4) &lt;
    /// TITLE_HOLDER(+5) &lt; GOLDEN_GLOVE(+7) &lt; SIGNATURE / DYNASTY / RETIRED_NUMBER(+10)인데, 상시 스카우트 최상위 상품이 GOLDEN_GLOVE를
    /// 건너뛰고 종결 등급 SIGNATURE(0.5%)를 주고 있었다. 종결 등급군(+10)은 상시 스카우트에서 완전히 제외하고(ExcludedFromScout),
    /// 최상위 상품(싸인볼/픽업권)을 "골든글러브 스카우트"로 바꿨다:
    ///   골든글러브 상품  GG 0.5 / TH 1.5 / FRA 2.0 / AS 3.0 / LIVE_EPIC 93.0   (이전 시그니처 상품: SIG 0.5 / TH 1.5 / FRA 2.0 / AS 3.0 / LE 93.0)
    ///   타이틀홀더 상품  변경 없음
    /// </summary>
    public static class ScoutDropTables
    {
        public const float TargetRatePercent = 0.5f;            // 상품 타겟(최고) 등급
        public const float OneBelowRatePercent = 1.5f;          // 타겟 바로 아래 등급
        public const float MidRatePercent = 3.0f;               // 중간 등급
        public const float SignatureFranchiseRatePercent = 2.0f; // 시그니처 상품의 FRANCHISE(TH 1.5 ~ AS 3.0 사이)
        public const float TitleHolderFranchiseRatePercent = 1.0f; // 타이틀홀더 상품의 FRANCHISE(TH 0.5 ~ AS 1.5 사이)

        /// <summary>[TASK-KBO-182] 상시 스카우트에서 절대 나오지 않는 종결 등급군(+10).</summary>
        public static readonly IReadOnlyList<Grade> ExcludedFromScout = new[] { Grade.SIGNATURE, Grade.DYNASTY, Grade.RETIRED_NUMBER };

        public static bool IsExcludedFromScout(Grade grade) => ExcludedFromScout.Contains(grade);

        /// <summary>[TASK-KBO-182] 프리미엄/픽업 골든글러브 스카우트(싸인볼·픽업 영입권) - 구 시그니처 스카우트 대체.</summary>
        public static readonly IReadOnlyList<(Grade Grade, float RatePercent)> GoldenGlove = WithBase(Grade.LIVE_EPIC,
            (Grade.GOLDEN_GLOVE, TargetRatePercent),
            (Grade.TITLE_HOLDER, OneBelowRatePercent),
            (Grade.FRANCHISE, SignatureFranchiseRatePercent),
            (Grade.ALLSTAR, MidRatePercent));

        /// <summary>[호환] 구 이름 - 골든글러브 스카우트와 같은 표다(SIGNATURE는 더 이상 나오지 않는다).</summary>
        [System.Obsolete("TASK-KBO-182: 시그니처 스카우트는 골든글러브 스카우트로 대체됐다 - GoldenGlove를 쓰십시오.")]
        public static IReadOnlyList<(Grade Grade, float RatePercent)> Signature => GoldenGlove;

        /// <summary>프리미엄/픽업 타이틀 홀더(트로피·픽업 영입권).</summary>
        public static readonly IReadOnlyList<(Grade Grade, float RatePercent)> TitleHolder = WithBase(Grade.LIVE_NORMAL,
            (Grade.TITLE_HOLDER, TargetRatePercent),
            (Grade.FRANCHISE, TitleHolderFranchiseRatePercent),
            (Grade.ALLSTAR, OneBelowRatePercent),
            (Grade.LIVE_EPIC, MidRatePercent));

        public static float Total(IEnumerable<(Grade Grade, float RatePercent)> table) => table.Sum(e => e.RatePercent);

        /// <summary>상위 등급 칸 뒤에 "100 - 나머지 합"짜리 기본 등급 칸을 붙인다.</summary>
        private static IReadOnlyList<(Grade Grade, float RatePercent)> WithBase(Grade baseGrade, params (Grade Grade, float RatePercent)[] upper)
        {
            var table = upper.ToList();
            table.Add((baseGrade, 100f - upper.Sum(e => e.RatePercent)));
            return table;
        }
    }
}
