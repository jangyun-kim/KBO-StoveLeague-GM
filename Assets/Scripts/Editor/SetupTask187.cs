using KBOManager.Controllers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-187] 씬 적용(idempotent) - 치어리더 관리 화면에 [응원단 성장] 띠(도감 요약 + 버튼)와 성장 덮개 패널(CheerGrowthView)을 붙인다.
    /// 보유 목록(CheerleaderList)을 띠 높이만큼 줄인다. 라인업 SD 배지 · 게이지 · 타순 마름모 · 결과 화면 투수 명판 · 예고 선발은 런타임 코드(씬 무관).
    /// </summary>
    public static class SetupTask187
    {
        [MenuItem("KBO Manager/Setup/Apply TASK-187 (Cheerleader Growth)")]
        public static void ApplyAll()
        {
            bool cheer = BuildCheerGrowth();
            Debug.Log($"[SetupTask187] TASK-187 적용 완료(응원단 성장 {(cheer ? "부착" : "건너뜀")}) - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static bool BuildCheerGrowth()
        {
            var controller = Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupTask187] CheerleaderInventoryUIController가 없어 응원단 성장을 건너뜁니다.");
                return false;
            }
            var layout = controller.transform.Find("Layout178") as RectTransform;
            if (layout != null && layout.Find("CheerleaderList") is RectTransform list)
            {
                Undo.RecordObject(list, "TASK-187 Cheer List");
                list.anchorMin = new Vector2(0.03f, 0.01f);
                list.anchorMax = new Vector2(0.97f, 0.425f);
                list.offsetMin = list.offsetMax = Vector2.zero;
            }
            if (!controller.TryGetComponent<CheerGrowthView>(out var view)) view = Undo.AddComponent<CheerGrowthView>(controller.gameObject);
            view.Configure(KBOFonts.Bold, KBOFonts.Medium, layout);
            view.Build();
            EditorUtility.SetDirty(view);
            if (controller.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            return true;
        }
    }
}
