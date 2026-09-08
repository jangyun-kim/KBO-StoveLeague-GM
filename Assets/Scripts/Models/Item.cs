using System;
using KBOManager.Data;

namespace KBOManager.Models
{
    /// <summary>
    /// 유저가 실제로 보유한 강화 재료 카드 1장. PlayerTemplate/Player와 동일한 패턴으로, 원본(불변)
    /// 데이터는 ItemTemplate ScriptableObject가 갖고 있고 이 클래스는 그 참조 + 인스턴스 ID만 들고 있는
    /// 순수 데이터 클래스다. 강화 재료 카드 자체는 성장 상태가 없는 소모품이라 InstanceId/Template 외의
    /// 추가 필드가 필요 없다.
    /// </summary>
    [Serializable]
    public class Item
    {
        public string InstanceId;
        public ItemTemplate Template;

        public Item() { }

        public Item(string instanceId, ItemTemplate template)
        {
            InstanceId = instanceId;
            Template = template;
        }
    }
}
