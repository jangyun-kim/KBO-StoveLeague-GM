using System.Collections.Generic;
using KBOManager.Models;

namespace KBOManager.Managers
{
    /// <summary>
    /// [TASK-KBO-138] 강화(ReinforceLevel)를 "확률 판정"에서 "경험치(EXP) 누적 확정" 방식으로 개편하며
    /// 신설. 사용자 GDD 지시에 따라 등급별 요구/제공 경험치를 이 파일 한 곳에 하드코딩해 두어(명령서 6항)
    /// 향후 수치를 쉽게 조절할 수 있도록 한다 - 모든 값은 임시 밸런스 값이다.
    ///
    /// 등급 순서는 `Player.cs`(`GradeBaseCostFor`)와 `Types.cs`의 `Grade` enum 정수값(SEASON=0 ~
    /// DYNASTY=7)이 이미 합의하고 있는 "파워 서열"을 그대로 따른다(`(int)Grade`를 순위로 직접 사용) -
    /// `ScoutUIController.GradeRank`(단순 "최고급 뽑기 연출" 임계값 판정용, GOLDEN_GLOVE/SIGNATURE 순서가
    /// 이와 다름)는 이 파일과 무관한 별개 용도라 참고하지 않았다.
    /// </summary>
    public static class UpgradeConstants
    {
        /// <summary>
        /// [Required EXP] 타겟 카드가 "0강 -&gt; 1강"으로 올라가는 데 필요한 기본 경험치(등급별 차등).
        /// 사용자 지시 예시(라이브 에픽=1000, 시그니처=2000)를 정확히 반영했고, 나머지 등급은 파워
        /// 서열에 맞춰 단조 증가하도록 임시로 채웠다 - 밸런스 확정 전까지의 하드코딩 값(TODO).
        /// </summary>
        public static readonly Dictionary<Grade, int> RequiredExpBaseByGrade =
            new Dictionary<Grade, int>
            {
                { Grade.SEASON, 700 },
                { Grade.LIVE_NORMAL, 850 },
                { Grade.LIVE_EPIC, 1000 },      // 사용자 지시 예시값
                { Grade.ALLSTAR, 1350 },
                { Grade.TITLE_HOLDER, 1700 },
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
            int rankDelta = (int)materialGrade - (int)targetGrade;
            int exp = MaterialExpBaseSameGrade + rankDelta * MaterialExpPerGradeStep;
            return exp < MaterialExpMinimum ? MaterialExpMinimum : exp;
        }
    }
}
