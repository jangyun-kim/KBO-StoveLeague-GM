using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-185] 씬 적용(idempotent).
    ///   1) 라인업(RosterPanel) 직속 레거시 하단 바 고아 정리 - SetupRosterUI(TASK-176/177)가 매 Setup마다 RosterPanel 직속에 다시 만들고
    ///      TASK-182가 그중 하나만 Layout182/DefaultBar로 옮겨, 남은 흰 게이지 막대(SetDeckGaugeFill) · 리스너 없는 [세트덱 버프 선택] ·
    ///      미바인딩 [자동 교체]가 Layout182 위에 그려져 선수 액션 트레이를 가리고 클릭을 가로챘다. 컨트롤러가 참조하지 않는 것만 지운다.
    ///   2) 로비 [세트덱 &amp; 버프 선택] 타일 → 라인업 이동 + 세트덱 선택형 버프(A/B) 팝업 바로 열기(LobbyButtonRelay.ConfigureSetDeckBuffs).
    ///   3) 스카우트 허브 상단 3탭 [선수 스카우트(뽑기)] | [특별 영입(골글·시그니처)] | [응원단 영입] + 특별 영입 섹션(SpecialRecruitView).
    /// </summary>
    public static class SetupTask185
    {
        public const string SpecialPanelName = "SpecialRecruitPanel";
        public const string SpecialTabName = "SpecialTabButton";

        [MenuItem("KBO Manager/Setup/Apply TASK-185 (Lineup Overlap Fix + Special Recruit)")]
        public static void ApplyAll()
        {
            int removed = CleanRosterOrphans();
            WireLobbySetDeckTile();
            BuildSpecialRecruit();
            Debug.Log($"[SetupTask185] TASK-185 적용 완료(라인업 레거시 고아 {removed}개 정리 · 세트덱 버프 타일 · 특별 영입) - 씬을 저장(Ctrl+S)하십시오.");
        }

        /// <summary>RosterUIController가 직렬화 필드로 참조하지 않는 RosterPanel 직속 레거시 바 오브젝트를 지운다. 지운 개수를 돌려준다.</summary>
        public static int CleanRosterOrphans()
        {
            var controller = Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            if (controller == null) return 0;
            var referenced = ReferencedObjects(controller);
            var orphans = controller.transform.Cast<Transform>()
                .Where(t => RosterUIController.LegacyBarObjectNames.Contains(t.name))
                .Where(t => !referenced.Contains(t.gameObject) && !t.GetComponents<Component>().Any(referenced.Contains)
                            && !t.GetComponentsInChildren<Component>(true).Any(referenced.Contains))
                .ToList();
            foreach (var orphan in orphans) Undo.DestroyObjectImmediate(orphan.gameObject);
            if (orphans.Count > 0) MarkDirty(controller);
            return orphans.Count;
        }

        private static HashSet<Object> ReferencedObjects(Component component)
        {
            var set = new HashSet<Object>();
            var iterator = new SerializedObject(component).GetIterator();
            while (iterator.Next(true))
            {
                if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue != null)
                    set.Add(iterator.objectReferenceValue);
            }
            return set;
        }

        public static void WireLobbySetDeckTile()
        {
            foreach (var relay in Object.FindObjectsByType<LobbyButtonRelay>(FindObjectsInactive.Include))
            {
                var title = relay.transform.parent != null ? relay.transform.parent.Find(relay.name + "_Title")?.GetComponent<Text>() : null;
                if (title == null || !title.text.Contains("세트덱")) continue;
                Undo.RecordObject(relay, "Wire SetDeck Tile");
                relay.ConfigureSetDeckBuffs();
                MarkDirty(relay);
            }
        }

        public static void BuildSpecialRecruit()
        {
            var hub = Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            if (hub == null)
            {
                Debug.LogWarning("[SetupTask185] ScoutHubUIController가 없어 특별 영입을 건너뜁니다.");
                return;
            }
            var so = new SerializedObject(hub);
            var hubRect = (RectTransform)hub.transform;

            // ---- 섹션
            var panel = hubRect.Find(SpecialPanelName) as RectTransform;
            if (panel == null)
            {
                panel = new GameObject(SpecialPanelName, typeof(RectTransform)).GetComponent<RectTransform>();
                Undo.RegisterCreatedObjectUndo(panel.gameObject, "Create " + SpecialPanelName);
                panel.SetParent(hubRect, false);
            }
            var player = so.FindProperty("playerSection").objectReferenceValue as GameObject;
            var playerRect = player != null ? (RectTransform)player.transform : null;
            panel.anchorMin = playerRect != null ? playerRect.anchorMin : Vector2.zero;
            panel.anchorMax = playerRect != null ? playerRect.anchorMax : new Vector2(1f, 0.92f);
            panel.offsetMin = playerRect != null ? playerRect.offsetMin : Vector2.zero;
            panel.offsetMax = playerRect != null ? playerRect.offsetMax : Vector2.zero;
            if (playerRect != null) panel.SetSiblingIndex(playerRect.GetSiblingIndex() + 1);
            if (!panel.TryGetComponent<SpecialRecruitView>(out var view)) view = Undo.AddComponent<SpecialRecruitView>(panel.gameObject);
            view.Configure(KBOFonts.Bold, KBOFonts.Medium);
            view.Build();
            panel.gameObject.SetActive(false);
            so.FindProperty("specialSection").objectReferenceValue = panel.gameObject;

            // ---- 탭 3개
            var playerTab = so.FindProperty("playerTabButton").objectReferenceValue as Button;
            var cheerTab = so.FindProperty("cheerleaderTabButton").objectReferenceValue as Button;
            if (playerTab != null)
            {
                var tabParent = playerTab.transform.parent;
                var special = tabParent.Find(SpecialTabName)?.GetComponent<Button>();
                if (special == null)
                {
                    special = Object.Instantiate(playerTab, tabParent);
                    special.name = SpecialTabName;
                    Undo.RegisterCreatedObjectUndo(special.gameObject, "Create " + SpecialTabName);
                }
                special.onClick = new Button.ButtonClickedEvent(); // 선수 탭의 영구 리스너가 복제되지 않게
                special.transform.SetSiblingIndex(playerTab.transform.GetSiblingIndex() + 1);
                if (cheerTab != null) cheerTab.transform.SetSiblingIndex(special.transform.GetSiblingIndex() + 1);
                SetTabText(playerTab, "선수 스카우트(뽑기)");
                SetTabText(special, "특별 영입(골글·시그니처)");
                SetTabText(cheerTab, "응원단 영입");
                so.FindProperty("specialTabButton").objectReferenceValue = special;
                if (tabParent is RectTransform tabRect && tabRect.GetComponent<HorizontalLayoutGroup>() == null) LayoutTabs(tabRect, playerTab, special, cheerTab);
            }
            so.ApplyModifiedProperties();
            MarkDirty(view);
            MarkDirty(hub);
        }

        /// <summary>탭 컨테이너에 레이아웃 그룹이 없으면 3등분 앵커로 직접 배치한다.</summary>
        private static void LayoutTabs(RectTransform container, params Button[] tabs)
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
