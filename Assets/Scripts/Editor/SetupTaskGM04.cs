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
    /// [TASK-GM-04] 씬 적용(idempotent) - 메인 캔버스 바로 아래에 시상식 & 포스트시즌 화면(GMAwardsCeremony + GMAwardsCeremonyUIController)을
    /// 전체 화면으로 조립해 두고 꺼 둔다. 대시보드 [시상 리포트] · [포스트시즌 & 시상식 보기] · 올스타 팝업이 코드로 찾아 연다.
    /// ([시상 리포트] · [20yy 시즌 전환] 버튼은 대시보드 Build()가 만든다.)
    /// </summary>
    public static class SetupTaskGM04
    {
        public const string ObjectName = "GMAwardsCeremony";

        [MenuItem("KBO Manager/Setup/Apply TASK-GM-04 (Awards Ceremony + Postseason)")]
        public static void ApplyAll()
        {
            bool built = BuildAwards();
            Debug.Log($"[SetupTaskGM04] TASK-GM-04 적용 완료 - 시상식 & 포스트시즌 화면 {(built ? "조립" : "건너뜀")} - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static bool BuildAwards()
        {
            var scaler = Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include)
                .FirstOrDefault(s => s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize);
            if (scaler == null)
            {
                Debug.LogWarning("[SetupTaskGM04] ScaleWithScreenSize 캔버스가 없어 건너뜁니다.");
                return false;
            }
            var view = Object.FindAnyObjectByType<GMAwardsCeremonyUIController>(FindObjectsInactive.Include);
            if (view == null)
            {
                var go = new GameObject(ObjectName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Create GM Awards Ceremony");
                go.transform.SetParent(scaler.transform, false);
                view = go.AddComponent<GMAwardsCeremonyUIController>();
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
