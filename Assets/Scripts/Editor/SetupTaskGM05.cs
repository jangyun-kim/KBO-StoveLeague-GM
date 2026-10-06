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
    /// [TASK-GM-05] 씬 적용(idempotent) - 메인 캔버스 바로 아래에 구단 치어리더 엔트리 화면(GMCheerleaderEntry + GMCheerleaderEntryUIController)을
    /// 전체 화면으로 조립해 두고 꺼 둔다. 대시보드 [치어리더 엔트리 (4~6인)] · 전력 비교 [라인업/치어리더 점검]이 코드로 찾아 연다.
    /// (대시보드 바로가기 버튼 · 결과 화면 응원 요약은 각 화면 Build()가 만든다.)
    /// </summary>
    public static class SetupTaskGM05
    {
        public const string ObjectName = "GMCheerleaderEntry";

        [MenuItem("KBO Manager/Setup/Apply TASK-GM-05 (Cheerleader 15 Pool + Entry)")]
        public static void ApplyAll()
        {
            bool built = BuildCheerEntry();
            Debug.Log($"[SetupTaskGM05] TASK-GM-05 적용 완료 - 치어리더 엔트리 화면 {(built ? "조립" : "건너뜀")} - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static bool BuildCheerEntry()
        {
            var scaler = Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include)
                .FirstOrDefault(s => s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize);
            if (scaler == null)
            {
                Debug.LogWarning("[SetupTaskGM05] ScaleWithScreenSize 캔버스가 없어 건너뜁니다.");
                return false;
            }
            var view = Object.FindAnyObjectByType<GMCheerleaderEntryUIController>(FindObjectsInactive.Include);
            if (view == null)
            {
                var go = new GameObject(ObjectName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Create GM Cheerleader Entry");
                go.transform.SetParent(scaler.transform, false);
                view = go.AddComponent<GMCheerleaderEntryUIController>();
            }
            var rect = (RectTransform)view.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Font regular = null;
            var diagnostic = Object.FindAnyObjectByType<GMDiagnosticView>(FindObjectsInactive.Include);
            if (diagnostic != null) regular = new SerializedObject(diagnostic).FindProperty("regularFont").objectReferenceValue as Font;
            if (regular == null) regular = TextTidy.BodyFont;
            view.Configure(regular);
            view.Build();
            foreach (var t in view.GetComponentsInChildren<Text>(true)) EditorUtility.SetDirty(t);
            view.transform.SetAsLastSibling();
            view.gameObject.SetActive(false);
            EditorUtility.SetDirty(view);
            if (view.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            return true;
        }
    }
}
