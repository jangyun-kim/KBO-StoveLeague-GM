using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-178] 치어리더 영입 상품별 등급 확률표(CheerleaderGachaService가 사용 - 산술/카탈로그 정합성을 Unity 없이 검증할 수
    /// 있도록 순수 데이터로 분리). 치어리더 DB(cheerleaders.csv 185장)에 실제로 있는 티어는 LIVE(enum LIVE_NORMAL) 124 / ICON 46 /
    /// LEGEND 15뿐이다 - LIVE_EPIC·SEASON_LIMITED는 카탈로그가 0장이라 추첨되면 "카탈로그가 비어 있어 폴백" 경고가 났다(TASK-178 이전
    /// 라이브 영입 37.5%, 아이콘 4.5%, 레전드 3.0%가 LIVE_EPIC, 한정 영입 100%가 SEASON_LIMITED였다).
    ///   라이브 영입   LIVE 100%                                  (이전 LIVE 62.5 / LIVE_EPIC 37.5)
    ///   아이콘 영입   ICON 0.5 / LIVE 99.5                       (이전 ICON 0.5 / LIVE_EPIC 4.5 / LIVE 95.0)
    ///   레전드 영입   LEGEND 0.5 / ICON 1.5 / LIVE 98.0          (이전 LEGEND 0.5 / ICON 1.5 / LIVE_EPIC 3.0 / LIVE 95.0)
    ///   한정 영입     상품 폐지(SEASON_LIMITED 카탈로그 없음)
    /// 상위 티어 확률은 그대로 두고 LIVE_EPIC 몫을 LIVE에 합쳤다. 마지막 칸(LIVE)은 100에서 나머지를 뺀 값이라 합계가 항상 100%다.
    /// </summary>
    public static class CheerleaderDropTables
    {
        public const float TargetRatePercent = 0.5f;
        public const float OneBelowRatePercent = 1.5f;

        /// <summary>가챠가 실제로 뽑을 수 있는 티어(카탈로그 존재 티어).</summary>
        public static readonly IReadOnlyList<CheerleaderGrade> CatalogTiers = new[]
        {
            CheerleaderGrade.LIVE_NORMAL, CheerleaderGrade.ICON, CheerleaderGrade.LEGEND,
        };

        public static readonly IReadOnlyList<(CheerleaderGrade Grade, float RatePercent)> Live =
            WithLiveBase();

        public static readonly IReadOnlyList<(CheerleaderGrade Grade, float RatePercent)> Icon =
            WithLiveBase((CheerleaderGrade.ICON, TargetRatePercent));

        public static readonly IReadOnlyList<(CheerleaderGrade Grade, float RatePercent)> Legend =
            WithLiveBase((CheerleaderGrade.LEGEND, TargetRatePercent), (CheerleaderGrade.ICON, OneBelowRatePercent));

        public static float Total(IEnumerable<(CheerleaderGrade Grade, float RatePercent)> table) => table.Sum(e => e.RatePercent);

        /// <summary>배너용 요약("ICON 0.5% / LIVE 99.5%") - 표기는 CheerleaderGradeLabels.Display(LIVE_NORMAL = "LIVE").</summary>
        public static string Describe(IEnumerable<(CheerleaderGrade Grade, float RatePercent)> table) =>
            string.Join(" / ", table.Select(e => $"{e.Grade.Display()} {e.RatePercent:0.#}%"));

        private static IReadOnlyList<(CheerleaderGrade Grade, float RatePercent)> WithLiveBase(params (CheerleaderGrade Grade, float RatePercent)[] upper)
        {
            var table = upper.ToList();
            table.Add((CheerleaderGrade.LIVE_NORMAL, 100f - upper.Sum(e => e.RatePercent)));
            return table;
        }
    }
}
