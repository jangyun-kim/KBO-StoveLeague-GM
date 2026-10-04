using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;

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
    ///
    /// [TASK-KBO-185] 골든글러브 이상(GG / SIG / DYN / RN)을 스카우트(뽑기)에서 전면 제외 - 뽑기 최고 등급은 TITLE_HOLDER다.
    /// 골든글러브 · 시그니처는 스카우트 허브 [특별 영입](SpecialRecruitRules, 컴프야V26 재료 투입식)으로만 얻는다.
    ///   프리미엄 · 픽업 스카우트(싸인볼 · 트로피 · 픽업권)  TH 1.0 / FRA 2.5 / AS 4.5 / LIVE_EPIC 92.0
    ///   일반 스카우트(포인트 · 일반권)                    TH 0.3 / FRA 1.0 / AS 2.2 / LIVE_EPIC 6.5 / LIVE_NORMAL 90.0
    /// 픽업 영입은 등급이 정해진 뒤 선택 구단(SelectedTeam) 카드가 50%(나머지 9개 구단 합산 50%)로 나오도록 구단을 먼저 가른다
    /// (PickTemplate - 기존은 등급 풀 전체 균등이라 선택 구단이 약 10%였다).
    /// </summary>
    public static class ScoutDropTables
    {
        /// <summary>[TASK-KBO-185] 스카우트(뽑기)에서 절대 나오지 않는 등급 - 골든글러브 이상(GG / SIG / DYN / RN).</summary>
        public static readonly IReadOnlyList<Grade> ExcludedFromScout = new[] { Grade.GOLDEN_GLOVE, Grade.SIGNATURE, Grade.DYNASTY, Grade.RETIRED_NUMBER };

        /// <summary>[TASK-KBO-185] 뽑기로 얻을 수 있는 최고 등급.</summary>
        public const Grade HighestScoutGrade = Grade.TITLE_HOLDER;

        /// <summary>[TASK-KBO-185] 픽업 영입에서 선택 구단 카드가 나올 확률(나머지 9개 구단 합산 = 1 - 이 값).</summary>
        public const float PickupFavoriteTeamShare = 0.5f;

        public static bool IsExcludedFromScout(Grade grade) => ExcludedFromScout.Contains(grade);

        /// <summary>[TASK-KBO-185] 프리미엄 · 픽업 스카우트(싸인볼 / 트로피 / 픽업 영입권) - TH 1.0 / FRA 2.5 / AS 4.5 / LIVE_EPIC 92.0.</summary>
        public static readonly IReadOnlyList<(Grade Grade, float RatePercent)> Premium = WithBase(Grade.LIVE_EPIC,
            (Grade.TITLE_HOLDER, 1.0f),
            (Grade.FRANCHISE, 2.5f),
            (Grade.ALLSTAR, 4.5f));

        /// <summary>[TASK-KBO-185] 일반 스카우트(포인트 · 라이브 일반 영입권) - TH 0.3 / FRA 1.0 / AS 2.2 / LIVE_EPIC 6.5 / LIVE_NORMAL 90.0.</summary>
        public static readonly IReadOnlyList<(Grade Grade, float RatePercent)> Normal = WithBase(Grade.LIVE_NORMAL,
            (Grade.TITLE_HOLDER, 0.3f),
            (Grade.FRANCHISE, 1.0f),
            (Grade.ALLSTAR, 2.2f),
            (Grade.LIVE_EPIC, 6.5f));

        /// <summary>[호환] TASK-182 골든글러브 스카우트 이름 - TASK-185부터 프리미엄 표(GG 제외)와 같다.</summary>
        [System.Obsolete("TASK-KBO-185: 골든글러브는 스카우트에서 제외됐다 - Premium을 쓰십시오.")]
        public static IReadOnlyList<(Grade Grade, float RatePercent)> GoldenGlove => Premium;

        /// <summary>[호환] TASK-176 타이틀 홀더 상품 이름 - TASK-185부터 프리미엄 표와 같다.</summary>
        [System.Obsolete("TASK-KBO-185: 프리미엄 상품 확률은 Premium 하나로 통일됐다.")]
        public static IReadOnlyList<(Grade Grade, float RatePercent)> TitleHolder => Premium;

        public static float Total(IEnumerable<(Grade Grade, float RatePercent)> table) => table.Sum(e => e.RatePercent);

        /// <summary>[TASK-KBO-185] roll(0~100) 누적 확률로 등급을 고른다(넘치면 마지막 기본 등급).</summary>
        public static Grade RollGrade(IReadOnlyList<(Grade Grade, float RatePercent)> table, float roll)
        {
            float cumulative = 0f;
            foreach (var entry in table)
            {
                cumulative += entry.RatePercent;
                if (roll < cumulative) return entry.Grade;
            }
            return table[table.Count - 1].Grade;
        }

        /// <summary>
        /// [TASK-KBO-185] 등급이 정해진 뒤 실제 카드(템플릿)를 고른다. 스카우트 제외 등급은 어떤 경우에도 나오지 않는다.
        /// pickup이면 선택 구단 카드를 PickupFavoriteTeamShare(50%), 나머지 구단 카드를 합산 50%로 먼저 가른 뒤 그 안에서 균등 추첨한다
        /// (한쪽이 비면 다른 쪽에서). 등급 풀이 비면 스카우트 가능한 전체 풀에서 대체 추첨한다. random01은 [0, 1) 난수 공급자.
        /// </summary>
        public static PlayerTemplate PickTemplate(IEnumerable<PlayerTemplate> all, Grade grade, Team favoriteTeam, bool pickup, System.Func<float> random01)
        {
            var scoutable = (all ?? Enumerable.Empty<PlayerTemplate>()).Where(t => t != null && !IsExcludedFromScout(t.Grade)).ToList();
            var pool = IsExcludedFromScout(grade) ? new List<PlayerTemplate>() : scoutable.Where(t => t.Grade == grade).ToList();
            if (pool.Count == 0) pool = scoutable;
            if (pool.Count == 0) return null;

            if (pickup && favoriteTeam != Team.None)
            {
                var own = pool.Where(t => t.Team == favoriteTeam).ToList();
                var others = pool.Where(t => t.Team != favoriteTeam).ToList();
                if (own.Count > 0 && others.Count > 0) pool = random01() < PickupFavoriteTeamShare ? own : others;
                else if (own.Count > 0) pool = own;
            }
            int index = (int)(random01() * pool.Count);
            return pool[index < 0 ? 0 : index >= pool.Count ? pool.Count - 1 : index];
        }

        /// <summary>"타이틀 홀더 1% · 프랜차이즈 2.5% · …"(기본 등급 제외) - 스카우트 배너 표기용.</summary>
        public static string Describe(IReadOnlyList<(Grade Grade, float RatePercent)> table) =>
            string.Join(" · ", table.Take(table.Count - 1).Select(e => $"{CardGrowthRules.DisplayName(e.Grade)} {e.RatePercent:0.#}%"));

        /// <summary>[TASK-KBO-185] "[선택 구단(삼성) 픽업 확률 UP! (50%)]".</summary>
        public static string PickupBanner(string teamName) =>
            $"[선택 구단({(string.IsNullOrEmpty(teamName) ? "미선택" : teamName)}) 픽업 확률 UP! ({PickupFavoriteTeamShare * 100f:0}%)]";

        /// <summary>상위 등급 칸 뒤에 "100 - 나머지 합"짜리 기본 등급 칸을 붙인다.</summary>
        private static IReadOnlyList<(Grade Grade, float RatePercent)> WithBase(Grade baseGrade, params (Grade Grade, float RatePercent)[] upper)
        {
            var table = upper.ToList();
            table.Add((baseGrade, 100f - upper.Sum(e => e.RatePercent)));
            return table;
        }
    }
}
