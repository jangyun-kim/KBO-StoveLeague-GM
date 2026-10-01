using System.Collections.Generic;
using KBOManager.Models;

namespace KBOManager.Managers
{
    /// <summary>
    /// [TASK-KBO-138] 강화(ReinforceLevel)를 "확률 판정"에서 "경험치(EXP) 누적 확정" 방식으로 개편하며
    /// 신설. 사용자 GDD 지시에 따라 등급별 요구/제공 경험치를 이 파일 한 곳에 하드코딩해 두어(명령서 6항)
    /// 향후 수치를 쉽게 조절할 수 있도록 한다 - 모든 값은 임시 밸런스 값이다.
    ///
    /// 등급 순서는 `Player.cs`(`GradeBaseCostFor`)와 `Types.cs`의 `Grade` enum 정수값(TASK-KBO-155
    /// 재배치 이후, [TASK-KBO-172] FRANCHISE 삽입으로 LIVE_NORMAL=1 ~ DYNASTY=9)이 이미 합의하고 있는 "파워 서열"을 그대로 따른다
    /// [TASK-KBO-175] 재료 경험치의 등급 차이는 `(int)Grade` 대신 `CardGrowthRules.PowerRank`(위상 순번 -
    /// SIGNATURE = DYNASTY = RETIRED_NUMBER 최상위)로 계산한다. enum 정수값은 CSV/직렬화 호환 때문에 그대로라
    /// RETIRED_NUMBER(6)가 SIGNATURE(7)보다 낮게 계산되는 문제가 있었다.
    /// </summary>
    public static class UpgradeConstants
    {
        /// <summary>
        /// [Required EXP] 타겟 카드가 "0강 -&gt; 1강"으로 올라가는 데 필요한 기본 경험치(등급별 차등).
        /// 사용자 지시 예시(라이브 에픽=1000, 시그니처=2000)를 정확히 반영했고, 나머지 등급은 파워
        /// 서열에 맞춰 단조 증가하도록 임시로 채웠다 - 밸런스 확정 전까지의 하드코딩 값(TODO).
        /// [TASK-KBO-155] SEASON 삭제(사용자 직접 지시)로 항목을 제거했고, 신설 RETIRED_NUMBER는
        /// TITLE_HOLDER(1700)와 SIGNATURE(2000) 사이 값(1850)으로 단조 증가 규칙을 유지했다.
        /// </summary>
        public static readonly Dictionary<Grade, int> RequiredExpBaseByGrade =
            new Dictionary<Grade, int>
            {
                { Grade.LIVE_NORMAL, 850 },
                { Grade.LIVE_EPIC, 1000 },      // 사용자 지시 예시값
                { Grade.ALLSTAR, 1350 },
                { Grade.FRANCHISE, 1500 },      // [TASK-KBO-172 신설] ALLSTAR~TITLE_HOLDER 단조 증가 유지
                { Grade.TITLE_HOLDER, 1700 },
                { Grade.RETIRED_NUMBER, 2800 }, // [TASK-KBO-175] 1850 -> 2800: 최상위 종결 등급(실전 +10, 비가챠 레거시)이라 DYNASTY와 동일
                { Grade.SIGNATURE, 2000 },      // 사용자 지시 예시값
                { Grade.GOLDEN_GLOVE, 2300 },
                { Grade.DYNASTY, 2800 },
            };

        /// <summary>다음 강화 단계로 갈수록 요구 경험치가 늘어나는 배율 - 현재 단계 수 + 1을 그대로 곱하는
        /// 선형 증가(0강->1강은 x1, 1강->2강은 x2, ... 9강->10강은 x10)로 단순하게 잡았다. 밸런스 확정
        /// 전까지의 임시값이며, 필요하면 이 상수 하나만 곡선(제곱 등)으로 바꾸면 된다.</summary>
        public const int RequiredExpLevelMultiplierStep = 1;

        /// <summary>기본값(테이블에 없는 등급 등 예외 상황용) 요구 경험치.</summary>
        public const int RequiredExpFallback = 1000;

        /// <summary>[Material EXP] 재료 카드가 "타겟과 같은 등급"일 때 제공하는 기준 경험치. 사용자 지시
        /// 예시("동일 등급 = 1500")를 그대로 반영했다.</summary>
        public const int MaterialExpBaseSameGrade = 1500;

        /// <summary>재료 등급이 타겟보다 한 단계(파워 서열 기준) 높거나 낮을 때마다 가감되는 경험치 -
        /// 희귀한 카드를 재료로 넣을수록 더 많은 경험치를 주는 취지다. 밸런스 확정 전까지의 임시값.</summary>
        public const int MaterialExpPerGradeStep = 300;

        /// <summary>재료 등급이 타겟보다 훨씬 낮아도 제공 경험치가 0 이하로 떨어지지 않도록 하는 하한선.</summary>
        public const int MaterialExpMinimum = 100;

        /// <summary>타겟의 현재 ReinforceLevel 기준 "다음 단계"로 올라가는 데 필요한 경험치를 계산한다.
        /// target이 이미 만렙(Player.MaxReinforceLevel)이면 int.MaxValue를 반환해(더 이상 오를 수 없으므로)
        /// 호출부가 실수로 계속 레벨업시키는 일을 막는다.</summary>
        public static int GetRequiredExp(Player target)
        {
            if (target?.Template == null) return RequiredExpFallback;
            if (target.ReinforceLevel >= Player.MaxReinforceLevel) return int.MaxValue;

            int baseExp = RequiredExpBaseByGrade.TryGetValue(target.Template.Grade, out var value)
                ? value
                : RequiredExpFallback;

            return baseExp * (target.ReinforceLevel + RequiredExpLevelMultiplierStep);
        }

        /// <summary>재료 카드 1장(materialGrade)을 targetGrade 카드에 강화 재료로 사용했을 때 제공되는
        /// 경험치. 등급이 같으면 `MaterialExpBaseSameGrade`, 재료가 타겟보다 파워 서열상 높을수록/
        /// 낮을수록 `MaterialExpPerGradeStep`만큼씩 가감된다.</summary>
        public static int GetMaterialExp(Grade materialGrade, Grade targetGrade)
        {
            // [TASK-KBO-175] (int)Grade 차이 대신 위상 순번 차이 - RETIRED_NUMBER가 SIGNATURE/DYNASTY와 같은 최상위로 계산된다.
            int rankDelta = CardGrowthRules.PowerRank(materialGrade) - CardGrowthRules.PowerRank(targetGrade);
            int exp = MaterialExpBaseSameGrade + rankDelta * MaterialExpPerGradeStep;
            return exp < MaterialExpMinimum ? MaterialExpMinimum : exp;
        }
    }
}
