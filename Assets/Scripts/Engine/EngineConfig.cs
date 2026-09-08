using System;
using System.Collections.Generic;
using UnityEngine;

namespace KBOManager.Engine
{
    [Serializable]
    public class OutcomeWeight
    {
        public AtBatResult Result;
        [Min(0f)] public float BaseWeight;
    }

    /// <summary>
    /// MatchEngine의 밸런스 상수를 인스펙터에서 즉시 튜닝할 수 있도록 분리한 데이터 테이블.
    /// MatchEngine 생성자에 전달하지 않으면(null) 코드 내 기본값으로 동작하므로, 에셋 없이도
    /// 엔진은 그대로 작동한다 - 이 에셋은 순수히 "코드를 건드리지 않고 밸런스를 조정하는" 용도다.
    /// </summary>
    [CreateAssetMenu(fileName = "EngineConfig", menuName = "KBO Manager/Engine Config", order = 4)]
    public class EngineConfig : ScriptableObject
    {
        [Header("Stat Matchup Tuning")]
        [Tooltip("스탯 격차를 skill=±1(최대 보정)로 정규화하는 기준값")]
        public float StatDiffNormalizer = 50f;

        [Tooltip("매치업 격차(skill)가 타석 결과 확률에 미치는 영향력 계수")]
        public float SkillInfluence = 0.6f;

        [Header("Set Deck Bonus")]
        public int SetDeckActivationThreshold = 5;
        public float SetDeckBonusMultiplier = 1.15f;

        [Header("Outcome Base Weights (총합 1.0 권장)")]
        public List<OutcomeWeight> OutcomeWeights = new List<OutcomeWeight>();

        /// <summary>등록된 결과의 기본 가중치를 반환한다. 등록되어 있지 않으면 fallback(코드 기본값)을 그대로 쓴다.</summary>
        public float GetBaseWeight(AtBatResult result, float fallback)
        {
            var match = OutcomeWeights.Find(w => w.Result == result);
            return match != null ? match.BaseWeight : fallback;
        }

        [ContextMenu("Populate Default Values")]
        public void PopulateDefaults()
        {
            StatDiffNormalizer = 50f;
            SkillInfluence = 0.6f;
            SetDeckActivationThreshold = 5;
            SetDeckBonusMultiplier = 1.15f;

            OutcomeWeights = new List<OutcomeWeight>
            {
                new OutcomeWeight { Result = AtBatResult.Strikeout, BaseWeight = 0.22f },
                new OutcomeWeight { Result = AtBatResult.Groundout, BaseWeight = 0.23f },
                new OutcomeWeight { Result = AtBatResult.Flyout,    BaseWeight = 0.20f },
                new OutcomeWeight { Result = AtBatResult.Walk,      BaseWeight = 0.08f },
                new OutcomeWeight { Result = AtBatResult.Single,    BaseWeight = 0.16f },
                new OutcomeWeight { Result = AtBatResult.Double,    BaseWeight = 0.06f },
                new OutcomeWeight { Result = AtBatResult.Triple,    BaseWeight = 0.01f },
                new OutcomeWeight { Result = AtBatResult.HomeRun,   BaseWeight = 0.04f },
            };
        }
    }
}
