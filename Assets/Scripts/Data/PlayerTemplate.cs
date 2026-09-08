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
        public int BaseOverall;    // 1성/명함 기준 기본 오버롤
        public int Cost;           // 샐러리 캡 코스트. 강화/각성해도 변하지 않는 고정값

        [Header("Skill Preset (선택)")]
        public SkillTier PresetSkillTier;
        public string PresetSkillName;
    }
}
