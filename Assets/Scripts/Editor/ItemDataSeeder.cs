using System.Collections.Generic;
using KBOManager.Data;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// GDD 3절에 명시된 강화 재료 카드 10종(일반 1~5성 + 확률업 1~5성+1)의 ItemTemplate .asset을
    /// 에디터 메뉴 한 번으로 자동 생성/갱신하고, 열려 있는 씬의 ItemDatabase 컴포넌트를 찾아 그
    /// AllTemplates에 자동 등록까지 완료한다(드래그 앤 드롭 불필요). 에디터 전용이므로 반드시
    /// "Editor" 폴더 밑에 있어야 빌드에 포함되지 않는다.
    ///
    /// 이미 존재하는 에셋은 덮어쓰지 않고 필드만 최신 값으로 갱신한다 - 여러 번 실행해도 안전하다
    /// (예: MaterialType 이름을 바꿔 다시 실행해도 중복 에셋이 생기지 않는다).
    ///
    /// ItemDatabase 자동 등록은 "현재 열려 있는 씬"에 배치된 인스턴스를 대상으로 한다. 씬이 열려 있지
    /// 않거나 씬에 ItemDatabase가 없으면(예: 프리팹으로만 존재) 자동 등록을 건너뛰고 경고 로그만
    /// 남긴다 - 이 경우 종전처럼 수동으로 드래그해 등록해야 한다.
    /// </summary>
    public static class ItemDataSeeder
    {
        private const string TargetFolder = "Assets/GameData/Items";

        private static readonly (string templateId, string displayName, MaterialCardType materialType)[] DefaultItems =
        {
            ("ENHANCE_STAR1", "1성 강화카드", MaterialCardType.Star1),
            ("ENHANCE_STAR2", "2성 강화카드", MaterialCardType.Star2),
            ("ENHANCE_STAR3", "3성 강화카드", MaterialCardType.Star3),
            ("ENHANCE_STAR4", "4성 강화카드", MaterialCardType.Star4),
            ("ENHANCE_STAR5", "5성 강화카드", MaterialCardType.Star5),
            ("ENHANCE_STAR1_PLUS", "1성 +1 강화카드", MaterialCardType.Star1Plus),
            ("ENHANCE_STAR2_PLUS", "2성 +1 강화카드", MaterialCardType.Star2Plus),
            ("ENHANCE_STAR3_PLUS", "3성 +1 강화카드", MaterialCardType.Star3Plus),
            ("ENHANCE_STAR4_PLUS", "4성 +1 강화카드", MaterialCardType.Star4Plus),
            ("ENHANCE_STAR5_PLUS", "5성 +1 강화카드", MaterialCardType.Star5Plus),
        };

        // GDD 7절 "스킬 변경권으로 뽑기 진행". MaterialType은 EnhanceMaterial 전용 필드라 여기선 의미가
        // 없으므로 임의값(Star1)을 넣어 두되, ItemCategory.SkillChangeTicket이 실제 소비처
        // (SkillRerollManager)를 가르는 유일한 기준이다.
        private const string SkillTicketTemplateId = "SKILL_CHANGE_TICKET";
        private const string SkillTicketDisplayName = "스킬 변경권";

        [MenuItem("KBO Manager/Generate Default Items")]
        public static void GenerateDefaultItems()
        {
            EnsureFolderExists(TargetFolder);

            int created = 0;
            int updated = 0;
            var allSeededTemplates = new List<ItemTemplate>(DefaultItems.Length + 1);

            foreach (var (templateId, displayName, materialType) in DefaultItems)
            {
                var template = CreateOrUpdate(templateId, displayName, materialType, ItemCategory.EnhanceMaterial, out bool wasCreated);
                if (template == null) continue;

                allSeededTemplates.Add(template);
                if (wasCreated) created++; else updated++;
            }

            var ticketTemplate = CreateOrUpdate(SkillTicketTemplateId, SkillTicketDisplayName,
                MaterialCardType.Star1, ItemCategory.SkillChangeTicket, out bool ticketWasCreated);
            if (ticketTemplate != null)
            {
                allSeededTemplates.Add(ticketTemplate);
                if (ticketWasCreated) created++; else updated++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string bindingReport = RegisterIntoSceneDatabase(allSeededTemplates);

            Debug.Log($"[ItemDataSeeder] 강화 아이템 {DefaultItems.Length}종 + 스킬 변경권 1종 처리 완료 " +
                      $"(신규 {created}개, 갱신 {updated}개) - '{TargetFolder}'. {bindingReport}");
        }

        /// <summary>
        /// 현재 열려 있는 씬에서 ItemDatabase 컴포넌트를 찾아 방금 생성/갱신한 템플릿들을 자동으로
        /// AllTemplates에 등록한다(Dirty 마킹 + 씬 저장 포함). Object.FindFirstObjectByType은 비활성
        /// 오브젝트를 찾지 못하므로, 씬에 ItemDatabase가 비활성 상태로 배치돼 있다면 감지되지 않는다는
        /// 한계가 있다 - 그런 경우엔 이 메서드가 "찾지 못함" 경고를 남기고 종전처럼 수동 등록을 안내한다.
        /// </summary>
        private static string RegisterIntoSceneDatabase(List<ItemTemplate> templates)
        {
            var itemDatabase = Object.FindFirstObjectByType<ItemDatabase>();
            if (itemDatabase == null)
            {
                return "ItemDatabase.AllTemplates에는 자동 등록되지 않았습니다(열려 있는 씬에서 ItemDatabase 컴포넌트를 " +
                       "찾지 못함). 씬을 열고 다시 실행하거나, 수동으로 드래그해 등록해 주세요.";
            }

            int addedCount = itemDatabase.RegisterTemplates(templates);

            EditorUtility.SetDirty(itemDatabase);
            EditorSceneManager.MarkSceneDirty(itemDatabase.gameObject.scene);
            EditorSceneManager.SaveScene(itemDatabase.gameObject.scene);

            return $"ItemDatabase.AllTemplates에 신규 {addedCount}개 자동 등록 완료(씬 '{itemDatabase.gameObject.scene.name}' 저장됨). " +
                   "드래그 앤 드롭 불필요.";
        }

        /// <summary>"Assets/GameData/Items"처럼 여러 단계로 없는 폴더를 순서대로 만든다.</summary>
        private static void EnsureFolderExists(string path)
        {
            var parts = path.Split('/');
            string current = parts[0]; // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        /// <summary>wasCreated가 true면 새로 생성한 것, false면 기존 에셋을 갱신한 것. 결과 템플릿을 반환한다.</summary>
        private static ItemTemplate CreateOrUpdate(string templateId, string displayName, MaterialCardType materialType,
            ItemCategory category, out bool wasCreated)
        {
            string assetPath = $"{TargetFolder}/{templateId}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ItemTemplate>(assetPath);

            if (existing != null)
            {
                existing.TemplateId = templateId;
                existing.DisplayName = displayName;
                existing.MaterialType = materialType;
                existing.Category = category;
                EditorUtility.SetDirty(existing);
                wasCreated = false;
                return existing;
            }

            var template = ScriptableObject.CreateInstance<ItemTemplate>();
            template.TemplateId = templateId;
            template.DisplayName = displayName;
            template.MaterialType = materialType;
            template.Category = category;

            AssetDatabase.CreateAsset(template, assetPath);
            wasCreated = true;
            return template;
        }
    }
}
