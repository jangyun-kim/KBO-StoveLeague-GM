using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Data
{
    /// <summary>
    /// 선수 카드의 고유(불변) 원본 데이터. 에디터에서 .asset으로 찍어내어 관리한다.
    /// 유저가 실제로 보유/육성하는 카드는 Models.Player가 이 템플릿을 참조하는 형태로 생성된다.
    /// </summary>
    [CreateAssetMenu(fileName = "PlayerTemplate_", menuName = "KBO Manager/Player Template", order = 1)]
    public class PlayerTemplate : ScriptableObject
    {
        [Header("Identity")]
        public string TemplateId;          // 카드 고유 식별자 (예: "GG_KOOJASOOK_2024")
        public string RealPlayerId;        // 실제 선수 식별자. 카드 종류(등급/연도)가 달라도 동일 선수면 동일 값
                                            // -> 각성 재료 판정("같은 등급의 다른 종류 동일 선수카드")에 사용
        public string PlayerName;          // 카드 표기 이름
        public int SeasonYear;             // 기준 시즌 연도 (타이틀 홀더/골든글러브/시그니처/왕조 등에서 사용)

        [Header("Team & Grade")]
        public Team Team;
        public Grade Grade;

        [Header("Position")]
        public bool IsPitcher;
        public BatterPosition BatterPosition;  // IsPitcher == false 일 때만 유효
        public PitcherRole PitcherRole;        // IsPitcher == true 일 때만 유효

        [Header("Base Stats")]
        [Tooltip("IsPitcher == false 일 때만 유효")]
        public BatterStats BatterStats;
        [Tooltip("IsPitcher == true 일 때만 유효")]
        public PitcherStats PitcherStats;
        public int Cost;           // [레거시] GDD v3.1 시절 고정 코스트 - 어떤 로직도 참조하지 않는다.
                                    // [TASK-KBO-173] 카드 Salary는 개인 세트덱 스코어로 일원화됐다
                                    // (Player.Salary = CardGrowthRules.BaseSetDeckScore, 샐러리 캡 폐기).

        [Header("Skill Preset (선택)")]
        public SkillTier PresetSkillTier;
        public string PresetSkillName;

        /// <summary>
        /// 세부 스탯 평균 기준 1성/명함 기본 OVR. 강화/각성/세트덱 보너스가 없는 순수 원본 수치이며,
        /// 실제 육성 반영 OVR은 Models.Player.CalculateOVR()을 사용한다.
        /// </summary>
        public int GetBaseOverall()
        {
            return IsPitcher
                ? Mathf.RoundToInt((PitcherStats.Stuff + PitcherStats.Velocity + PitcherStats.Movement + PitcherStats.Control) / 4f)
                : Mathf.RoundToInt((BatterStats.Power + BatterStats.Contact + BatterStats.Discipline) / 3f);
        }
    }
}
