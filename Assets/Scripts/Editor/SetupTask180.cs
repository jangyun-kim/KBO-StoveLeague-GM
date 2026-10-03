using KBOManager.Controllers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-180] 씬 적용(전부 idempotent).
    ///   1) 치어리더 관리(CheerleaderInventoryPanel/Layout178): 장착 영역을 키워 6인 역할 편성 3x2 그리드(CheerSquadPanel)를 넣고,
    ///      필터 바/보유 목록을 아래로 내린다. 요약 줄(EquippedSummaryText)은 그리드 아래 상태 줄로 재배치.
    /// </summary>
    public static class SetupTask180
    {
        [MenuItem("KBO Manager/Setup/Apply TASK-180 (Cheer Squad 6 + Lobby)")]
        public static void ApplyAll()
        {
            ApplyCheerSquadLayout();
            Debug.Log("[SetupTask180] TASK-180 적용 완료 - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static void ApplyCheerSquadLayout()
        {
            var controller = Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include);
            var layout = controller != null ? controller.transform.Find("Layout178") : null;
            if (layout == null)
            {
                Debug.LogWarning("[SetupTask180] 치어리더 관리 화면(Layout178)이 없어 6인 편성 UI를 건너뜁니다 - TASK-178 테마를 먼저 적용하십시오.");
                return;
            }

            var box = layout.Find("EquippedBox") as RectTransform;
            if (box != null)
            {
                SetAnchors(box, 0.03f, 0.545f, 0.97f, 0.928f);
                var title = box.Find("EquippedTitle") as RectTransform;
                if (title != null)
                {
                    SetAnchors(title, 0.03f, 0.915f, 0.97f, 0.995f);
                    var text = title.GetComponent<Text>();
                    if (text != null) { Undo.RecordObject(text, "Cheer Squad Title"); text.text = "치어리더 6인 역할 편성 - 슬롯을 누른 뒤 아래 목록에서 [장착]"; }
                }
                var summary = box.Find("EquippedSummaryText") as RectTransform;
                if (summary != null)
                {
                    SetAnchors(summary, 0.03f, 0.01f, 0.97f, 0.11f);
                    var text = summary.GetComponent<Text>();
                    if (text != null) { Undo.RecordObject(text, "Cheer Squad Summary"); text.fontSize = 22; text.resizeTextForBestFit = true; text.resizeTextMinSize = 14; text.resizeTextMaxSize = 22; }
                }

                var holder = box.Find("CheerSquad180") as RectTransform;
                if (holder == null)
                {
                    var go = new GameObject("CheerSquad180", typeof(RectTransform));
                    Undo.RegisterCreatedObjectUndo(go, "Create Cheer Squad Grid");
                    holder = (RectTransform)go.transform;
                    holder.SetParent(box, false);
                }
                SetAnchors(holder, 0.015f, 0.12f, 0.985f, 0.91f);
                if (!holder.TryGetComponent<CheerSquadPanel>(out var panel)) panel = Undo.AddComponent<CheerSquadPanel>(holder.gameObject);
                panel.ConfigureFonts(KBOFonts.Bold, KBOFonts.Medium);
                panel.Build();
                EditorUtility.SetDirty(panel);

                var so = new SerializedObject(controller);
                so.FindProperty("squadPanel").objectReferenceValue = panel;
                so.ApplyModifiedProperties();
            }

            if (layout.Find("FilterBar") is RectTransform filterBar) SetAnchors(filterBar, 0.03f, 0.488f, 0.97f, 0.535f);
            if (layout.Find("CheerleaderList") is RectTransform list) SetAnchors(list, 0.03f, 0.01f, 0.97f, 0.478f);
            if (layout.Find("EmptyText") is RectTransform empty) SetAnchors(empty, 0.05f, 0.2f, 0.95f, 0.3f);
            MarkDirty(controller);
        }

        private static void SetAnchors(RectTransform rect, float x0, float y0, float x1, float y1)
        {
            Undo.RecordObject(rect, "TASK-180 Layout");
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void MarkDirty(Component component)
        {
            EditorUtility.SetDirty(component);
            var scene = component.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
