using KBOManager.Data;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// GDD 3절에 명시된 강화 재료 카드 10종(일반 1~5성 + 확률업 1~5성+1)의 ItemTemplate .asset을
    /// 에디터 메뉴 한 번으로 자동 생성/갱신한다. 에디터 전용이므로 반드시 "Editor" 폴더 밑에 있어야
    /// 빌드에 포함되지 않는다.
    ///
    /// 이미 존재하는 에셋은 덮어쓰지 않고 필드만 최신 값으로 갱신한다 - 여러 번 실행해도 안전하다
    /// (예: MaterialType 이름을 바꿔 다시 실행해도 중복 에셋이 생기지 않는다).
    ///
    /// 실행 후에는 ItemDatabase.AllTemplates에 새로 만들어진 10개 에셋을 수동으로 드래그해 등록해야
    /// 한다 - ItemDatabase는 씬에 배치되는 MonoBehaviour라 에디터 스크립트가 임의로 찾아 수정하지 않는다.
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

        [MenuItem("KBO Manager/Generate Default Items")]
        public static void GenerateDefaultItems()
        {
            EnsureFolderExists(TargetFolder);

            int created = 0;
            int updated = 0;

            foreach (var (templateId, displayName, materialType) in DefaultItems)
            {
                if (CreateOrUpdate(templateId, displayName, materialType))
                {
                    created++;
                }
                else
                {
                    updated++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ItemDataSeeder] 강화 아이템 {DefaultItems.Length}종 처리 완료 " +
                      $"(신규 {created}개, 갱신 {updated}개) - '{TargetFolder}'. " +
                      "ItemDatabase.AllTemplates에는 아직 자동 등록되지 않으니, 씬의 ItemDatabase 컴포넌트에 " +
                      "직접 드래그해 등록해 주세요.");
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

        /// <summary>true를 반환하면 새로 생성한 것, false면 기존 에셋을 갱신한 것.</summary>
        private static bool CreateOrUpdate(string templateId, string displayName, MaterialCardType materialType)
        {
            string assetPath = $"{TargetFolder}/{templateId}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<ItemTemplate>(assetPath);

            if (existing != null)
            {
                existing.TemplateId = templateId;
                existing.DisplayName = displayName;
                existing.MaterialType = materialType;
                EditorUtility.SetDirty(existing);
                return false;
            }

            var template = ScriptableObject.CreateInstance<ItemTemplate>();
            template.TemplateId = templateId;
            template.DisplayName = displayName;
            template.MaterialType = materialType;

            AssetDatabase.CreateAsset(template, assetPath);
            return true;
        }
    }
}
