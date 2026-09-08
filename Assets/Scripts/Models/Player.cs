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

        /// <summary>
        /// 강화/각성 보너스가 반영된 현재 OVR을 계산한다.
        /// 세트덱(구단 통일) 보너스가 활성화된 경우 GameManager.CheckSetDeckBonus() 결과로 얻은
        /// 배율(setDeckBonusMultiplier)을 곱해 OVR이 크게 상승한다.
        /// 계수(TODO)는 밸런스 확정 전까지의 임시값이다.
        /// </summary>
        public int CalculateOVR(bool isSetDeckBonusActive, float setDeckBonusMultiplier = 1.0f)
        {
            if (Template == null) return 0;

            int reinforceBonus = ReinforceLevel * 2;              // TODO: 강화 1단당 OVR 증가폭 밸런스 확정 필요
            int awakenBonus = CanAwaken ? AwakenLevel * 3 : 0;    // TODO: 각성 1단당 OVR 증가폭 밸런스 확정 필요

            int totalOverall = Template.BaseOverall + reinforceBonus + awakenBonus;

            if (isSetDeckBonusActive && setDeckBonusMultiplier > 1f)
            {
                totalOverall = Mathf.RoundToInt(totalOverall * setDeckBonusMultiplier);
            }

            return totalOverall;
        }
    }
}
