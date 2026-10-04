using KBOManager.Controllers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using static KBOManager.EditorTools.SetupTask181;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-184] 씬 적용(idempotent).
    ///   1) 하단 [선수 관리] = 성장 전용 허브: PlayerManagementHubPanel(PlayerManagementUIController)에 GrowthCenterView를 붙이고 Build()로
    ///      성장 센터(대상 카드 · 스탯 미리보기 · [강화][각성][한계 돌파][훈련·특훈] 4탭 · 재료 선택 · 대상 선수 변경)를 조립해 연결한다.
    ///   2) 라인업 → [보관 선수] 탭: 보유 선수 리스트 필터/정렬 바(범위 · 구단 · 포지션 · 등급 · 정렬)를 만들고 스크롤을 그 아래로 내린다.
    /// 로비 하단 [선수 관리] 탭 / 메인 홈 [선수단 강화] 타일 / 라인업 트레이 [선수 관리]의 성장 센터 연결은 런타임 코드(OpenGrowthHub)가 맡는다.
    /// </summary>
    public static class SetupTask184
    {
        public const string FilterBarName = "StorageFilterBar184";
        private static readonly Color FilterBg = new Color(0.09f, 0.19f, 0.47f);

        [MenuItem("KBO Manager/Setup/Apply TASK-184 (Growth Center + Storage Filters)")]
        public static void ApplyAll()
        {
            BuildGrowthCenter();
            BuildStorageFilterBar();
            Debug.Log("[SetupTask184] TASK-184 적용 완료(선수 관리 성장 센터 · 보관 선수 필터) - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static void BuildGrowthCenter()
        {
            var controller = Object.FindAnyObjectByType<PlayerManagementUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupTask184] PlayerManagementUIController가 없어 성장 센터를 건너뜁니다.");
                return;
            }
            if (!controller.TryGetComponent<GrowthCenterView>(out var view)) view = Undo.AddComponent<GrowthCenterView>(controller.gameObject);
            var roster = Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            view.Configure(KBOFonts.Bold, KBOFonts.Medium, roster != null ? roster.CardPrefab : null);
            view.Build();

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("growthCenter").objectReferenceValue = view;
            serialized.ApplyModifiedProperties();
            MarkDirty(view);
            MarkDirty(controller);
        }

        public static void BuildStorageFilterBar()
        {
            var controller = Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupTask184] RosterUIController가 없어 보관 선수 필터를 건너뜁니다.");
                return;
            }
            var serialized = new SerializedObject(controller);
            var page = serialized.FindProperty("storagePage").objectReferenceValue as GameObject;
            if (page == null)
            {
                Debug.LogWarning("[SetupTask184] storagePage가 없어(TASK-182 라인업 미적용) 보관 선수 필터를 건너뜁니다.");
                return;
            }
            var pageRect = (RectTransform)page.transform;
            if (pageRect.Find(FilterBarName) is Transform previous) Undo.DestroyObjectImmediate(previous.gameObject);

            var bar = Page(pageRect, FilterBarName);
            Undo.RegisterCreatedObjectUndo(bar.gameObject, "Create " + FilterBarName);
            var fields = new[] { "storageScopeButton", "storageTeamButton", "storagePositionButton", "storageGradeButton", "storageSortButton" };
            var labels = new[] { "보관 선수", "전체 구단", "전체 포지션", "전체 등급", "OVR 순" };
            const float x0 = 12f, gap = 8f, y0 = 358f, y1 = 418f;
            float width = (W - 2 * x0 - gap * (fields.Length - 1)) / fields.Length;
            for (int i = 0; i < fields.Length; i++)
            {
                float left = x0 + i * (width + gap);
                var button = Btn(bar, fields[i].Replace("storage", "").Replace("Button", ""), labels[i], left, y0, left + width, y1, FilterBg, Color.white, 26);
                serialized.FindProperty(fields[i]).objectReferenceValue = button;
            }
            serialized.ApplyModifiedProperties();

            // 스크롤을 필터 바 아래로(TASK-182: 356~1500 → 424~1500).
            if (pageRect.Find("StorageScroll") is RectTransform scroll)
            {
                Undo.RecordObject(scroll, "Move Storage Scroll");
                SetBox(scroll, 12, 424, W - 12, 1500);
            }
            MarkDirty(controller);
        }

        private static void MarkDirty(Component component)
        {
            if (component == null) return;
            EditorUtility.SetDirty(component);
            if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
