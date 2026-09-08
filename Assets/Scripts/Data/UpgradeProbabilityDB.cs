using System;
using System.Collections.Generic;
using UnityEngine;

namespace KBOManager.Data
{
    /// <summary>
    /// 강화(0강/명함 ~ 10강)에 사용되는 재료 카드 종류.
    /// PlusN 계열은 이미 +1~5강 상태인 해당 성급 강화카드를 의미한다.
    /// </summary>
    public enum MaterialCardType
    {
        Star1,
        Star2,
        Star3,
        Star4,
        Star5,
        Star1Plus,
        Star2Plus,
        Star3Plus,
        Star4Plus,
        Star5Plus
    }

    [Serializable]
    public class MaterialProbability
    {
        public MaterialCardType MaterialType;
        [Range(0f, 100f)] public float SuccessRatePercent;

        public MaterialProbability(MaterialCardType type, float percent)
        {
            MaterialType = type;
            SuccessRatePercent = percent;
        }
    }

    [Serializable]
    public class UpgradeStage
    {
        public int FromLevel;
        public int ToLevel;
        public List<MaterialProbability> MaterialProbabilities = new List<MaterialProbability>();
    }

    /// <summary>
    /// 명함~10강까지, 강화 단계별 재료 카드에 따른 성공 확률 테이블.
    /// 인스펙터에서 자유롭게 수정 가능한 데이터 테이블로, 하드코딩된 강화 로직을 대체한다.
    /// </summary>
    [CreateAssetMenu(fileName = "UpgradeProbabilityDB", menuName = "KBO Manager/Upgrade Probability DB", order = 2)]
    public class UpgradeProbabilityDB : ScriptableObject
    {
        [SerializeField] private List<UpgradeStage> stages = new List<UpgradeStage>();

        public IReadOnlyList<UpgradeStage> Stages => stages;

        /// <summary>fromLevel 단계에서 materialType 재료를 사용했을 때의 성공 확률(%)을 조회한다.</summary>
        public float GetSuccessRate(int fromLevel, MaterialCardType materialType)
        {
            var stage = stages.Find(s => s.FromLevel == fromLevel);
            if (stage == null) return 0f;

            var material = stage.MaterialProbabilities.Find(m => m.MaterialType == materialType);
            return material?.SuccessRatePercent ?? 0f;
        }

        /// <summary>
        /// GDD v3.1에 명시된 기본 확률표로 초기화한다.
        /// 인스펙터에서 컴포넌트 우클릭(또는 톱니바퀴 메뉴) -> "Populate GDD Default Values"로 실행.
        /// </summary>
        [ContextMenu("Populate GDD Default Values")]
        public void PopulateDefaults()
        {
            stages = new List<UpgradeStage>
            {
                BuildStage(0, 1,
                    (MaterialCardType.Star1, 75f),
                    (MaterialCardType.Star2, 100f),
                    (MaterialCardType.Star3, 100f),
                    (MaterialCardType.Star4, 100f),
                    (MaterialCardType.Star5, 100f),
                    (MaterialCardType.Star5Plus, 100f)),

                BuildStage(1, 2,
                    (MaterialCardType.Star1, 50f),
                    (MaterialCardType.Star2, 100f),
                    (MaterialCardType.Star3, 100f),
                    (MaterialCardType.Star4, 100f),
                    (MaterialCardType.Star5, 100f),
                    (MaterialCardType.Star5Plus, 100f)),

                BuildStage(2, 3,
                    (MaterialCardType.Star1, 25f),
                    (MaterialCardType.Star2, 50f),
                    (MaterialCardType.Star3, 100f),
                    (MaterialCardType.Star4, 100f),
                    (MaterialCardType.Star5, 100f),
                    (MaterialCardType.Star5Plus, 100f)),

                BuildStage(3, 4,
                    (MaterialCardType.Star1, 10f),
                    (MaterialCardType.Star2, 25f),
                    (MaterialCardType.Star3, 50f),
                    (MaterialCardType.Star4, 75f),
                    (MaterialCardType.Star5, 100f),
                    (MaterialCardType.Star5Plus, 100f)),

                BuildStage(4, 5,
                    (MaterialCardType.Star1, 5f),
                    (MaterialCardType.Star2, 10f),
                    (MaterialCardType.Star3, 15f),
                    (MaterialCardType.Star4, 25f),
                    (MaterialCardType.Star5, 50f),
                    (MaterialCardType.Star5Plus, 100f)),   // GDD: 명함~5강은 5성+n 카드 사용 시 확률 +100%(확정)
            };

            // 5강 -> 10강: GDD에 "모두 동일 확률표" 로 명시되어 있으므로 5단계 반복 생성.
            for (int from = 5; from <= 9; from++)
            {
                stages.Add(BuildStage(from, from + 1,
                    (MaterialCardType.Star1, 0.05f),
                    (MaterialCardType.Star2, 0.1f),
                    (MaterialCardType.Star3, 1f),
                    (MaterialCardType.Star4, 5f),
                    (MaterialCardType.Star5, 10f),
                    (MaterialCardType.Star1Plus, 10f),
                    (MaterialCardType.Star2Plus, 15f),
                    (MaterialCardType.Star3Plus, 25f),
                    (MaterialCardType.Star4Plus, 50f),
                    (MaterialCardType.Star5Plus, 50f)));
            }
        }

        private static UpgradeStage BuildStage(int from, int to, params (MaterialCardType type, float percent)[] entries)
        {
            var stage = new UpgradeStage { FromLevel = from, ToLevel = to };
            foreach (var (type, percent) in entries)
            {
                stage.MaterialProbabilities.Add(new MaterialProbability(type, percent));
            }
            return stage;
        }
    }
}
