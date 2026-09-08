using System;
using System.Collections.Generic;
using KBOManager.Data;
using UnityEngine;

namespace KBOManager.Models
{
    /// <summary>
    /// 유저가 실제로 보유한 선수 카드 인스턴스. PlayerTemplate(불변 원본 데이터)을 참조하며,
    /// 강화/각성/스킬 등 유저의 육성 진행 상태만을 들고 있는 순수 데이터 클래스(MonoBehaviour 아님).
    /// </summary>
    [Serializable]
    public class Player
    {
        public const int MaxReinforceLevel = 10; // 명함(0강) ~ 10강
        public const int MaxAwakenLevel = 10;    // 1각 ~ 10각
        public const int MinStarLevel = 1;
        public const int MaxStarLevel = 6;

        public string InstanceId;      // 유저 보유 카드 고유 ID (GUID)
        public PlayerTemplate Template; // 원본 데이터 참조 (이름/구단/기본OVR/코스트/포지션/등급)

        public int ReinforceLevel;     // 0~10강
        public int AwakenLevel;        // 0~10각 (ALLSTAR 이상 등급만 유효)
        public int StarLevel;          // 1~6, 뽑기 시 등급에 따라 결정되는 초기 성급. 강화/각성과는 별개 개념
        public StarType CurrentStarType;

        public List<string> AcquiredSkillIds = new List<string>();

        public Player() { }

        public Player(string instanceId, PlayerTemplate template)
        {
            InstanceId = instanceId;
            Template = template;
            ReinforceLevel = 0;
            AwakenLevel = 0;
            CurrentStarType = template != null ? DefaultStarTypeFor(template.Grade) : StarType.NORMAL;
            StarLevel = template != null ? DefaultStarLevelFor(template.Grade) : MinStarLevel;
        }

        /// <summary>LIVE_NORMAL / LIVE_EPIC 등급은 강화만 가능하고 각성은 불가하다.</summary>
        public bool CanAwaken => Template != null
            && Template.Grade != Grade.LIVE_NORMAL
            && Template.Grade != Grade.LIVE_EPIC;

        private static StarType DefaultStarTypeFor(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => StarType.NORMAL,
            Grade.LIVE_EPIC => StarType.NORMAL,
            Grade.ALLSTAR => StarType.PURPLE,
            Grade.TITLE_HOLDER => StarType.SILVER,
            Grade.GOLDEN_GLOVE => StarType.GOLD,
            Grade.SIGNATURE => StarType.PLATINUM,
            Grade.DYNASTY => StarType.TEAM_COLOR,
            _ => StarType.NORMAL
        };

        /// <summary>
        /// 등급별 결정론적 기본 성급. LIVE_NORMAL의 1~3성 무작위 배정처럼 확률이 개입되는 규칙은
        /// 뽑기를 실제로 수행하는 ScoutManager가 이 기본값을 덮어써서 적용한다.
        /// </summary>
        private static int DefaultStarLevelFor(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => MinStarLevel,
            Grade.LIVE_EPIC => 4,
            Grade.ALLSTAR => 4,
            Grade.TITLE_HOLDER => 5,
            Grade.GOLDEN_GLOVE => 5,
            Grade.SIGNATURE => MaxStarLevel,
            Grade.DYNASTY => MaxStarLevel,
            _ => MinStarLevel
        };

        // 강화/각성 1레벨당 세부 스탯 각각에 붙는 성장치. TODO: 밸런스 확정 전까지의 임시값.
        private const int ReinforcePerStatBonus = 1;
        private const int AwakenPerStatBonus = 1;

        /// <summary>
        /// 강화/각성 성장치가 반영된 타자 세부 스탯. Template이 없거나 투수 카드면 default(0,0,0)를 반환한다.
        /// 스킬/세트덱 보너스는 매치 컨텍스트(상대방 존재)가 필요해 여기 포함하지 않으며, MatchEngine이 계산 시점에 적용한다.
        /// </summary>
        public BatterStats GetEffectiveBatterStats()
        {
            if (Template == null || Template.IsPitcher) return default;

            int growth = ReinforceLevel * ReinforcePerStatBonus + (CanAwaken ? AwakenLevel * AwakenPerStatBonus : 0);
            var baseStats = Template.BatterStats;
            return new BatterStats(baseStats.Power + growth, baseStats.Contact + growth, baseStats.Discipline + growth);
        }

        /// <summary>
        /// 강화/각성 성장치가 반영된 투수 세부 스탯. Template이 없거나 타자 카드면 default(0,0,0,0)를 반환한다.
        /// 스킬/세트덱 보너스는 매치 컨텍스트(상대방 존재)가 필요해 여기 포함하지 않으며, MatchEngine이 계산 시점에 적용한다.
        /// </summary>
        public PitcherStats GetEffectivePitcherStats()
        {
            if (Template == null || !Template.IsPitcher) return default;

            int growth = ReinforceLevel * ReinforcePerStatBonus + (CanAwaken ? AwakenLevel * AwakenPerStatBonus : 0);
            var baseStats = Template.PitcherStats;
            return new PitcherStats(baseStats.Stuff + growth, baseStats.Velocity + growth, baseStats.Movement + growth, baseStats.Control + growth);
        }

        /// <summary>
        /// 강화/각성이 반영된 세부 스탯의 평균에 세트덱(구단 통일) 보너스 배율을 적용해 최종 OVR을 산출한다.
        /// GDD 원문은 "OVR이 크게 뻥튀기"라 표현하므로, 세트덱 보너스는 가산이 아닌 배율(setDeckBonusMultiplier)로 구현했다.
        /// 배율은 GameManager.CheckSetDeckBonus() 결과값을 그대로 전달받아 사용한다.
        /// </summary>
        public int CalculateOVR(bool isSetDeckBonusActive, float setDeckBonusMultiplier = 1.0f)
        {
            if (Template == null) return 0;

            float average = Template.IsPitcher
                ? AverageOf(GetEffectivePitcherStats())
                : AverageOf(GetEffectiveBatterStats());

            if (isSetDeckBonusActive && setDeckBonusMultiplier > 1f)
            {
                average *= setDeckBonusMultiplier;
            }

            return Mathf.RoundToInt(average);
        }

        private static float AverageOf(BatterStats stats) => (stats.Power + stats.Contact + stats.Discipline) / 3f;
        private static float AverageOf(PitcherStats stats) => (stats.Stuff + stats.Velocity + stats.Movement + stats.Control) / 4f;
    }
}
