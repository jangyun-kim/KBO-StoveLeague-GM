using KBOManager.Controllers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-183] 씬 적용(idempotent).
    ///   1) 선수 관리 허브(PlayerManagementHubPanel): 메뉴 그리드를 살짝 올리고 그 아래에 성장 요약 텍스트(GrowthInfoText -
    ///      [기본 OVR / 현재 성장(+N) / 시너지(+M) / 최대 잠재 OVR] + 강화·한계 돌파·특훈·각성 진행도)를 만든다. 구 "준비 중" 3타일
    ///      ([훈련] [한계 돌파] [각성])을 [강화]와 같은 활성 색으로 바꾸고, [각성]이 여는 MaterialSelectUIController를 바인딩한다.
    ///   2) 상세 정보(DetailPanel/StatsContent): 맨 위에 성장 요약 줄(DetailGrowthSummaryText)을 추가해 PlayerDetailUIController에 바인딩한다.
    /// </summary>
    public static class SetupTask183
    {
        private const string GrowthInfoTextName = "GrowthInfoText";
        private const string DetailGrowthSummaryTextName = "DetailGrowthSummaryText";
        private static readonly Color TextLight = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);

        [MenuItem("KBO Manager/Setup/Apply TASK-183 (Growth Hub + League Tier)")]
        public static void ApplyAll()
        {
            BuildGrowthHub();
            BuildDetailGrowthSummary();
            Debug.Log("[SetupTask183] TASK-183 적용 완료(4대 성장 허브 · 성장 요약) - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static void BuildGrowthHub()
        {
            var controller = Object.FindAnyObjectByType<PlayerManagementUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupTask183] PlayerManagementUIController가 없어 성장 허브를 건너뜁니다.");
                return;
            }

            var grid = controller.transform.Find("MenuGrid") as RectTransform;
            if (grid != null)
            {
                Undo.RecordObject(grid, "Raise Menu Grid");
                grid.anchorMin = new Vector2(grid.anchorMin.x, 0.25f);
            }

            var info = FindOrCreateText(controller.transform, GrowthInfoTextName, new Vector2(0.05f, 0.15f), new Vector2(0.95f, 0.245f), 22);
            info.alignment = TextAnchor.MiddleCenter;
            info.color = Gold;
            info.resizeTextForBestFit = true;
            info.resizeTextMinSize = 14;
            info.resizeTextMaxSize = 24;
            info.text = "기본 OVR · 성장 · 시너지 · 최대 잠재 OVR";

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("growthInfoText").objectReferenceValue = info;
            var materialSelect = Object.FindAnyObjectByType<MaterialSelectUIController>(FindObjectsInactive.Include);
            if (materialSelect != null) serialized.FindProperty("materialSelectUIController").objectReferenceValue = materialSelect;
            else Debug.LogWarning("[SetupTask183] MaterialSelectUIController를 찾지 못해 [각성]은 런타임 탐색으로 동작합니다.");

            var enhance = serialized.FindProperty("enhanceButton").objectReferenceValue as Button;
            foreach (var name in new[] { "trainButton", "breakthroughButton", "awakenButton" })
            {
                if (serialized.FindProperty(name).objectReferenceValue is Button button) MatchReadyStyle(button, enhance);
            }
            serialized.ApplyModifiedProperties();
            Mark(controller);
        }

        public static void BuildDetailGrowthSummary()
        {
            var detail = Object.FindAnyObjectByType<PlayerDetailUIController>(FindObjectsInactive.Include);
            if (detail == null)
            {
                Debug.LogWarning("[SetupTask183] PlayerDetailUIController가 없어 상세 성장 요약을 건너뜁니다.");
                return;
            }

            var serialized = new SerializedObject(detail);
            var reinforce = serialized.FindProperty("reinforceText").objectReferenceValue as Text;
            if (reinforce == null)
            {
                Debug.LogWarning("[SetupTask183] 상세 정보 reinforceText가 없어 성장 요약 줄을 만들 위치를 찾지 못했습니다.");
                return;
            }

            var parent = reinforce.transform.parent;
            var existing = parent.Find(DetailGrowthSummaryTextName);
            Text summary;
            if (existing != null && existing.TryGetComponent(out Text found)) summary = found;
            else
            {
                var go = new GameObject(DetailGrowthSummaryTextName, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
                Undo.RegisterCreatedObjectUndo(go, "Create " + DetailGrowthSummaryTextName);
                go.transform.SetParent(parent, false);
                summary = go.GetComponent<Text>();
                var layout = go.GetComponent<LayoutElement>();
                var reinforceLayout = reinforce.GetComponent<LayoutElement>();
                layout.preferredHeight = reinforceLayout != null && reinforceLayout.preferredHeight > 0 ? reinforceLayout.preferredHeight : 44f;
                layout.flexibleWidth = 1f;
            }
            summary.transform.SetSiblingIndex(reinforce.transform.GetSiblingIndex());
            summary.font = reinforce.font;
            summary.fontSize = reinforce.fontSize;
            summary.alignment = reinforce.alignment;
            summary.color = Gold;
            summary.fontStyle = FontStyle.Bold;
            summary.resizeTextForBestFit = true;
            summary.resizeTextMinSize = 12;
            summary.resizeTextMaxSize = Mathf.Max(14, reinforce.fontSize);
            summary.raycastTarget = false;

            serialized.FindProperty("growthSummaryText").objectReferenceValue = summary;
            serialized.ApplyModifiedProperties();
            Mark(detail);
        }

        private static void MatchReadyStyle(Button target, Button reference)
        {
            if (target.targetGraphic is Image image)
            {
                Undo.RecordObject(image, "Ready Tile");
                image.color = reference != null && reference.targetGraphic is Image refImage ? refImage.color : Color.white;
            }
            var label = target.GetComponentInChildren<Text>(true);
            var refLabel = reference != null ? reference.GetComponentInChildren<Text>(true) : null;
            if (label != null)
            {
                Undo.RecordObject(label, "Ready Tile Label");
                label.color = refLabel != null ? refLabel.color : Color.black;
            }
        }

        private static Text FindOrCreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, int fontSize)
        {
            var existing = parent.Find(name);
            Text text;
            if (existing != null && existing.TryGetComponent(out Text found)) text = found;
            else
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Text));
                Undo.RegisterCreatedObjectUndo(go, "Create " + name);
                go.transform.SetParent(parent, false);
                text = go.GetComponent<Text>();
                text.font = KBOFonts.Default;
                text.raycastTarget = false;
            }
            var rect = (RectTransform)text.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            text.fontSize = fontSize;
            if (text.color.a < 0.1f) text.color = TextLight;
            return text;
        }

        private static void Mark(Object obj)
        {
            EditorUtility.SetDirty(obj);
            if (obj is Component c && c.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
        }
    }
}
