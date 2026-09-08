using UnityEngine;

namespace KBOManager.Data
{
    /// <summary>
    /// 강화 재료 카드 1종의 고유(불변) 원본 데이터. GDD 3절의 "일반 강화카드(1성~5성)"와
    /// "확률업 강화카드(1성+1~5성+1)" 10종을 각각 .asset으로 찍어내어 관리한다.
    /// MaterialType이 UpgradeProbabilityDB의 확률표 조회 키와 정확히 일치해야 강화 계산이 맞물린다.
    /// </summary>
    [CreateAssetMenu(fileName = "ItemTemplate_", menuName = "KBO Manager/Item Template", order = 7)]
    public class ItemTemplate : ScriptableObject
    {
        [Header("Identity")]
        public string TemplateId;      // 카드 고유 식별자 (예: "ENHANCE_STAR3", "ENHANCE_STAR5_PLUS")
        public string DisplayName;     // 카드 표기 이름 (예: "3성 강화카드", "5성 +1 강화카드")
        [TextArea] public string Description;
        public Sprite Icon;

        [Header("Enhance Rule")]
        [Tooltip("UpgradeProbabilityDB.GetSuccessRate() 조회 키. 일반 강화카드(Star1~5)와 확률업 " +
                 "강화카드(Star1Plus~5Plus)를 구분하는 값이 곧 이 필드다.")]
        public MaterialCardType MaterialType;
    }
}
