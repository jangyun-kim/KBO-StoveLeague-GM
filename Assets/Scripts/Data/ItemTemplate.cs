using UnityEngine;

namespace KBOManager.Data
{
    /// <summary>
    /// 이 아이템이 무슨 용도인지. EnhanceMaterial은 기존 10종(일반/확률업 강화카드), SkillChangeTicket은
    /// GDD 7절 "스킬 변경권" - 둘은 소비 흐름(MaterialSelectUIController 강화 재료 선택 vs
    /// SkillRerollManager 스킬 재추첨)이 완전히 달라, 같은 GameManager.ItemInventory 리스트에 함께
    /// 섞여 있어도 이 필드로 반드시 구분해서 걸러내야 한다.
    /// </summary>
    public enum ItemCategory
    {
        EnhanceMaterial,
        SkillChangeTicket
    }

    /// <summary>
    /// 유저가 보유하는 소모품 카드 1종의 고유(불변) 원본 데이터. GDD 3절의 "일반 강화카드(1성~5성)"와
    /// "확률업 강화카드(1성+1~5성+1)" 10종, 그리고 GDD 7절의 "스킬 변경권"을 이 하나의 템플릿 클래스로
    /// 함께 표현한다. MaterialType은 Category가 EnhanceMaterial일 때만 의미가 있으며, 그 경우
    /// UpgradeProbabilityDB.GetSuccessRate() 조회 키와 정확히 일치해야 강화 계산이 맞물린다.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemTemplate_", menuName = "KBO Manager/Item Template", order = 7)]
    public class ItemTemplate : ScriptableObject
    {
        [Header("Identity")]
        public string TemplateId;      // 카드 고유 식별자 (예: "ENHANCE_STAR3", "SKILL_CHANGE_TICKET")
        public string DisplayName;     // 카드 표기 이름 (예: "3성 강화카드", "스킬 변경권")
        [TextArea] public string Description;
        public Sprite Icon;

        [Header("Category")]
        public ItemCategory Category = ItemCategory.EnhanceMaterial;

        [Header("Enhance Rule (Category == EnhanceMaterial 일 때만 유효)")]
        [Tooltip("UpgradeProbabilityDB.GetSuccessRate() 조회 키. 일반 강화카드(Star1~5)와 확률업 " +
                 "강화카드(Star1Plus~5Plus)를 구분하는 값이 곧 이 필드다. SkillChangeTicket에는 사용되지 않는다.")]
        public MaterialCardType MaterialType;
    }
}
