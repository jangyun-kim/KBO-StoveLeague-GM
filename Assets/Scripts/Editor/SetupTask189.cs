using System.Linq;
using KBOManager.Controllers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-189] 씬 적용(idempotent).
    ///   스카우트 허브 4탭 [선수 스카우트(뽑기)] | [특별 영입(골글·시그니처)] | [응원단 영입] | [상점 · 교환소] + 상점 · 교환소 섹션(ShopExchangeView).
    /// 성장 센터 5탭(초월 추가) · [재료 획득처] 버튼 · 특별 영입 [재료 획득처 · 재조합] 버튼은 GrowthCenterView / SpecialRecruitView Build()가
    /// 만들며, SetupTask184 / SetupTask185가 같은 Build()로 계층을 다시 만든다. 라인업 보관 선수 [선수 방출]은 런타임 트레이가 만든다.
    /// </summary>
    public static class SetupTask189
    {
        public const string ShopPanelName = "ShopExchangePanel";
        public const string ShopTabName = "ShopTabButton";

        [MenuItem("KBO Manager/Setup/Apply TASK-189 (Shop & Exchange)")]
        public static void ApplyAll()
        {
            BuildShopExchange();
            Debug.Log("[SetupTask189] TASK-189 적용 완료(상점 · 교환소 섹션 + 4번째 탭) - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static void BuildShopExchange()
        {
            var hub = Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            if (hub == null)
            {
                Debug.LogWarning("[SetupTask189] ScoutHubUIController가 없어 상점 · 교환소를 건너뜁니다.");
                return;
            }
            var so = new SerializedObject(hub);
            var hubRect = (RectTransform)hub.transform;

            // ---- 섹션
            var panel = hubRect.Find(ShopPanelName) as RectTransform;
            if (panel == null)
            {
                panel = new GameObject(ShopPanelName, typeof(RectTransform)).GetComponent<RectTransform>();
                Undo.RegisterCreatedObjectUndo(panel.gameObject, "Create " + ShopPanelName);
                panel.SetParent(hubRect, false);
            }
            var player = so.FindProperty("playerSection").objectReferenceValue as GameObject;
            var playerRect = player != null ? (RectTransform)player.transform : null;
            panel.anchorMin = playerRect != null ? playerRect.anchorMin : Vector2.zero;
            panel.anchorMax = playerRect != null ? playerRect.anchorMax : new Vector2(1f, 0.92f);
            panel.offsetMin = playerRect != null ? playerRect.offsetMin : Vector2.zero;
            panel.offsetMax = playerRect != null ? playerRect.offsetMax : Vector2.zero;
            var special = so.FindProperty("specialSection").objectReferenceValue as GameObject;
            if (special != null) panel.SetSiblingIndex(special.transform.GetSiblingIndex() + 1);
            if (!panel.TryGetComponent<ShopExchangeView>(out var view)) view = Undo.AddComponent<ShopExchangeView>(panel.gameObject);
            view.Configure(KBOFonts.Bold, KBOFonts.Medium);
            view.Build();
            panel.gameObject.SetActive(false);
            so.FindProperty("shopSection").objectReferenceValue = panel.gameObject;

            // ---- 4번째 탭
            var playerTab = so.FindProperty("playerTabButton").objectReferenceValue as Button;
            var specialTab = so.FindProperty("specialTabButton").objectReferenceValue as Button;
            var cheerTab = so.FindProperty("cheerleaderTabButton").objectReferenceValue as Button;
            if (playerTab != null)
            {
                var tabParent = playerTab.transform.parent;
                var shop = tabParent.Find(ShopTabName)?.GetComponent<Button>();
                if (shop == null)
                {
                    shop = Object.Instantiate(playerTab, tabParent);
                    shop.name = ShopTabName;
                    Undo.RegisterCreatedObjectUndo(shop.gameObject, "Create " + ShopTabName);
                }
                shop.onClick = new Button.ButtonClickedEvent(); // 선수 탭의 영구 리스너가 복제되지 않게
                var last = cheerTab != null ? cheerTab : specialTab != null ? specialTab : playerTab;
                shop.transform.SetSiblingIndex(last.transform.GetSiblingIndex() + 1);
                SetTabText(shop, "상점·교환소");
                so.FindProperty("shopTabButton").objectReferenceValue = shop;
                if (tabParent is RectTransform tabRect && tabRect.GetComponent<HorizontalLayoutGroup>() == null)
                    LayoutTabs(playerTab, specialTab, cheerTab, shop);
            }
            so.ApplyModifiedProperties();
            MarkDirty(view);
            MarkDirty(hub);
        }

        /// <summary>탭 컨테이너에 레이아웃 그룹이 없으면 4등분 앵커로 직접 배치한다.</summary>
        private static void LayoutTabs(params Button[] tabs)
        {
            var list = tabs.Where(t => t != null).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var rect = (RectTransform)list[i].transform;
                Undo.RecordObject(rect, "Layout Scout Tabs");
                rect.anchorMin = new Vector2((float)i / list.Count, 0f);
                rect.anchorMax = new Vector2((float)(i + 1) / list.Count, 1f);
                rect.offsetMin = new Vector2(4f, 0f);
                rect.offsetMax = new Vector2(-4f, 0f);
                EditorUtility.SetDirty(rect);
            }
        }

        private static void SetTabText(Button tab, string text)
        {
            if (tab == null) return;
            foreach (var label in tab.GetComponentsInChildren<Text>(true))
            {
                Undo.RecordObject(label, "Tab Text");
                label.text = text;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = Mathf.Min(label.resizeTextMinSize > 0 ? label.resizeTextMinSize : 14, 14);
                label.resizeTextMaxSize = Mathf.Max(label.resizeTextMaxSize, label.fontSize);
                EditorUtility.SetDirty(label);
            }
        }

        private static void MarkDirty(Component component)
        {
            if (component == null) return;
            EditorUtility.SetDirty(component);
            if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
