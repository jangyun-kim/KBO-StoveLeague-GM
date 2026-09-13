using System.Collections.Generic;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-065] 치어리더 가챠 대상이 될 원본(카탈로그) 목록을 임시로 하드코딩해 제공하는
    /// 정적 클래스. docs/16_shop_and_gacha_policy.md 2절이 제안한 등급별 버프 수치 가이드라인을
    /// 그대로 반영했다. CSV 카탈로그(cheerleaders.csv)가 아직 없어(docs/11_data_dictionary.md 7/9절)
    /// v0.2 프로토타입 단계까지만 쓰는 임시 데이터 소스다 - 실제 카탈로그가 생기면 이 클래스를 CSV
    /// 로더로 교체한다.
    ///
    /// 여기서 반환하는 Cheerleader 객체는 "템플릿"이다 - InstanceId를 일부러 비워 둔다(실제 발급은
    /// CheerleaderGachaService.IssueCheerleader()가 새 Guid로 채운다). CatalogId만 채워져 있으며,
    /// 이 값이 GameManager.AddCheerleader()의 중복 판별 기준(TASK-KBO-064)이 된다. 템플릿 자체는
    /// OwnedCheerleaders에 직접 들어가지 않으므로 "InstanceId는 항상 비어있지 않아야 한다"는
    /// 세이브 불변식(11_data_dictionary.md 9절)을 위반하지 않는다.
    /// </summary>
    public static class CheerleaderCatalog
    {
        // 등급별 버프 가이드라인은 docs/16_shop_and_gacha_policy.md 2절 [Draft] 표를 그대로 반영했다.
        private static readonly List<Cheerleader> AllTemplates = new List<Cheerleader>
        {
            new Cheerleader { CatalogId = "CHR_001", Name = "치어리더 A (NORMAL)", Grade = CheerleaderGrade.NORMAL, ConditionBuff = 1, ClutchMultiplier = 1.00f, EconomicBonusRate = 1.05f, SentimentDefense = 0 },
            new Cheerleader { CatalogId = "CHR_002", Name = "치어리더 B (NORMAL)", Grade = CheerleaderGrade.NORMAL, ConditionBuff = 1, ClutchMultiplier = 1.00f, EconomicBonusRate = 1.05f, SentimentDefense = 0 },
            new Cheerleader { CatalogId = "CHR_003", Name = "치어리더 A (RARE)", Grade = CheerleaderGrade.RARE, ConditionBuff = 2, ClutchMultiplier = 1.05f, EconomicBonusRate = 1.10f, SentimentDefense = 1 },
            new Cheerleader { CatalogId = "CHR_004", Name = "치어리더 B (RARE)", Grade = CheerleaderGrade.RARE, ConditionBuff = 2, ClutchMultiplier = 1.05f, EconomicBonusRate = 1.10f, SentimentDefense = 1 },
            new Cheerleader { CatalogId = "CHR_005", Name = "치어리더 A (EPIC)", Grade = CheerleaderGrade.EPIC, ConditionBuff = 3, ClutchMultiplier = 1.10f, EconomicBonusRate = 1.15f, SentimentDefense = 2 },
            new Cheerleader { CatalogId = "CHR_006", Name = "치어리더 B (EPIC)", Grade = CheerleaderGrade.EPIC, ConditionBuff = 3, ClutchMultiplier = 1.10f, EconomicBonusRate = 1.15f, SentimentDefense = 2 },
            new Cheerleader { CatalogId = "CHR_007", Name = "치어리더 A (LEGEND)", Grade = CheerleaderGrade.LEGEND, ConditionBuff = 4, ClutchMultiplier = 1.15f, EconomicBonusRate = 1.20f, SentimentDefense = 3 },
            new Cheerleader { CatalogId = "CHR_008", Name = "치어리더 B (LEGEND)", Grade = CheerleaderGrade.LEGEND, ConditionBuff = 4, ClutchMultiplier = 1.15f, EconomicBonusRate = 1.20f, SentimentDefense = 3 },
        };

        /// <summary>지정한 등급의 카탈로그 템플릿 목록을 반환한다(항상 새 리스트 - 내부 원본 보호).
        /// 해당 등급이 하나도 없으면 빈 리스트를 반환한다(null 아님).</summary>
        public static List<Cheerleader> GetCheerleadersByGrade(CheerleaderGrade grade)
        {
            return AllTemplates.FindAll(c => c.Grade == grade);
        }
    }
}
