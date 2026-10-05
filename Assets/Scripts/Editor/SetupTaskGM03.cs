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
    /// [TASK-GM-03] 씬 적용(idempotent) - 메인 캔버스 바로 아래에 한 경기 전력 비교 · 박스스코어 화면(GMMatchPrePost + GMMatchPrePostUIController)을
    /// 전체 화면으로 조립해 두고 꺼 둔다. 대시보드 [한 경기] · [직전 경기 결과]가 코드로 찾아 연다. (새 시즌 설정 모달 · 버튼은 대시보드 Build()가 만든다.)
    /// </summary>
    public static class SetupTaskGM03
    {
        public const string ObjectName = "GMMatchPrePost";

        [MenuItem("KBO Manager/Setup/Apply TASK-GM-03 (Pre-Game Matchup + Box Score)")]
        public static void ApplyAll()
        {
            bool built = BuildPrePost();
            Debug.Log($"[SetupTaskGM03] TASK-GM-03 적용 완료 - 전력 비교 · 박스스코어 화면 {(built ? "조립" : "건너뜀")} - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static bool BuildPrePost()
        {
            var scaler = Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include)
                .FirstOrDefault(s => s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize);
            if (scaler == null)
            {
                Debug.LogWarning("[SetupTaskGM03] ScaleWithScreenSize 캔버스가 없어 건너뜁니다.");
                return false;
            }
            var view = Object.FindAnyObjectByType<GMMatchPrePostUIController>(FindObjectsInactive.Include);
            if (view == null)
            {
                var go = new GameObject(ObjectName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Create GM Match PrePost");
                go.transform.SetParent(scaler.transform, false);
                view = go.AddComponent<GMMatchPrePostUIController>();
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
