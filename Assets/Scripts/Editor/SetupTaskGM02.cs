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
    /// [TASK-GM-02] 씬 적용(idempotent, SetupMasterBinding 체인의 마지막 단계) - 메인 캔버스 바로 아래에 리그 플레이 실시간 대시보드
    /// (GMLiveDashboard + GMLiveLeagueDashboardUIController)를 전체 화면으로 조립해 두고 꺼 둔다. 로비 [플레이 볼]이
    /// LeagueDashboardUIController.StartMatch → GMLiveLeagueDashboardUIController.OpenFromLobby()로 연다(코드 연결).
    /// </summary>
    public static class SetupTaskGM02
    {
        public const string ObjectName = "GMLiveDashboard";

        [MenuItem("KBO Manager/Setup/Apply TASK-GM-02 (Live 144-Game Dashboard)")]
        public static void ApplyAll()
        {
            bool built = BuildDashboard();
            // 체인 마지막 정리 - SetupThemeUI178이 보관함(_Legacy178)으로 옮긴 옛 복사본(예: StoveLeagueView193) 중 현역과 이름이 같은 중복을 지운다.
            // FindAnyObjectByType이 숨은 복사본을 집으면 런타임 열기 · 씬 검증이 비결정적이 된다(SetupTask191.PurgeLegacyDuplicates와 같은 규칙).
            int purged = SetupTask191.PurgeLegacyDuplicates();
            Debug.Log($"[SetupTaskGM02] TASK-GM-02 적용 완료 - 실시간 대시보드 {(built ? "조립" : "건너뜀")} · 보관함 중복 정리 {purged}개 - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static bool BuildDashboard()
        {
            var scaler = Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include)
                .FirstOrDefault(s => s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize);
            if (scaler == null)
            {
                Debug.LogWarning("[SetupTaskGM02] ScaleWithScreenSize 캔버스가 없어 대시보드를 건너뜁니다.");
                return false;
            }

            var view = Object.FindAnyObjectByType<GMLiveLeagueDashboardUIController>(FindObjectsInactive.Include);
            if (view == null)
            {
                var go = new GameObject(ObjectName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Create GM Live Dashboard");
                go.transform.SetParent(scaler.transform, false);
                view = go.AddComponent<GMLiveLeagueDashboardUIController>();
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
            view.Refresh();
            foreach (var t in view.Root.GetComponentsInChildren<Text>(true)) EditorUtility.SetDirty(t);
            view.transform.SetAsLastSibling();
            view.gameObject.SetActive(false);

            EditorUtility.SetDirty(view);
            if (view.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            return true;
        }
    }
}
