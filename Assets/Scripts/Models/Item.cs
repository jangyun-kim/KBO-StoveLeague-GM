using System;
using KBOManager.Data;

namespace KBOManager.Models
{
    /// <summary>
    /// 강화(재료) 카드 1종을 나타내는 소모성 아이템. 선수 카드(Player)와 달리 고유 성장 상태를 갖지 않는다.
    /// </summary>
    [Serializable]
    public class Item
    {
        public string ItemId;
        public MaterialCardType MaterialType;
        public string DisplayName;

        public Item() { }

        public Item(string itemId, MaterialCardType materialType, string displayName)
        {
            ItemId = itemId;
            MaterialType = materialType;
            DisplayName = displayName;
        }
    }
}
