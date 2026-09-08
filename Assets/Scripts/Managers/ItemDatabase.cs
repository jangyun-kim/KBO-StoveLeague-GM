using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// 전체 강화 아이템 원본 템플릿(ItemTemplate ScriptableObject 에셋)을 보관하는 싱글톤 DB.
    /// PlayerDatabase가 PlayerTemplate에 대해 하는 역할을 ItemTemplate에 대해 동일하게 수행한다 -
    /// 실제 유저 소유 재료(Models.Item 인스턴스)는 CreateItemInstance()로 템플릿을 참조해 발급하고,
    /// SaveManager는 로드 시 TemplateId로 여기서 원본을 다시 찾아 붙인다.
    /// </summary>
    public class ItemDatabase : MonoBehaviour
    {
        public static ItemDatabase Instance { get; private set; }

        [Tooltip("에디터에서 생성한 ItemTemplate .asset들을 여기에 드래그하여 등록한다. " +
                 "GDD 3절 기준 일반 강화카드 5종 + 확률업 강화카드 5종 = 총 10종.")]
        [SerializeField] private List<ItemTemplate> allTemplates = new List<ItemTemplate>();

        /// <summary>DB에 등록된 전체 아이템 템플릿 (읽기 전용 뷰).</summary>
        public IReadOnlyList<ItemTemplate> AllTemplates => allTemplates;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>TemplateId로 원본 템플릿을 조회한다. (SaveManager의 로드 복원용)</summary>
        public ItemTemplate GetTemplateById(string templateId)
        {
            return allTemplates.FirstOrDefault(t => t.TemplateId == templateId);
        }

        /// <summary>MaterialType으로 등록된 템플릿을 조회한다. (뽑기/보상 지급 등에서 편리하게 쓰기 위함)</summary>
        public ItemTemplate GetTemplateByMaterialType(MaterialCardType materialType)
        {
            return allTemplates.FirstOrDefault(t => t.MaterialType == materialType);
        }

        /// <summary>템플릿을 참조하는 새 재료 카드 인스턴스를 발급한다.</summary>
        public Item CreateItemInstance(string templateId)
        {
            var template = GetTemplateById(templateId);
            if (template == null) return null;

            return new Item(Guid.NewGuid().ToString(), template);
        }

        /// <summary>
        /// ItemDataSeeder(에디터 전용 자동 생성 스크립트)가 새로 만든 템플릿들을 이 DB에 자동 등록할 때
        /// 호출한다. TemplateId 기준으로 중복을 걸러내며, 이미 등록된 항목은 같은 참조로 그대로 둔다
        /// (덮어쓰기 없음 - 인스펙터에서 수동으로 순서/내용을 조정해 둔 경우를 보존하기 위함).
        /// allTemplates가 private이므로 외부(에디터 스크립트 포함)에서 이 메서드를 거치지 않고는
        /// 목록을 바꿀 수 없다 - DB 스스로 중복 등록 여부를 책임진다.
        /// </summary>
        /// <returns>새로 추가된 템플릿 개수.</returns>
        public int RegisterTemplates(IEnumerable<ItemTemplate> templates)
        {
            if (templates == null) return 0;

            int addedCount = 0;
            foreach (var template in templates)
            {
                if (template == null) continue;
                if (allTemplates.Contains(template)) continue;
                if (allTemplates.Any(t => t != null && t.TemplateId == template.TemplateId)) continue;

                allTemplates.Add(template);
                addedCount++;
            }

            return addedCount;
        }
    }
}
