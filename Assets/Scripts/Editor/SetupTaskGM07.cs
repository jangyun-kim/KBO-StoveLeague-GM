using KBOManager.Controllers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-GM-07] 씬 점검(idempotent, SetupMasterBinding 체인 마지막). 화면 조립 자체는 앞 단계가 한다 -
    /// 프런트 오피스 허브(SetupTaskGM06 → Build: 툴바 · 사이드바 · 감독 설정 · 일정 · 포스트시즌 트리)와
    /// 경기 화면(SetupTaskGM03 → Build: 전력 분석 · 실시간 이닝 경기 · 경기 결과). 여기서는 새 요소가 씬에 들어갔는지 확인하고 로그를 남긴다.
    /// </summary>
    public static class SetupTaskGM07
    {
        [MenuItem("KBO Manager/Setup/Apply TASK-GM-07 (OOTP Layouts + 3-Stage Match)")]
        public static void ApplyAll()
        {
            var hub = Object.FindAnyObjectByType<GMOotpFrontOfficeUIController>(FindObjectsInactive.Include);
            var prePost = Object.FindAnyObjectByType<GMMatchPrePostUIController>(FindObjectsInactive.Include);
            var root = hub != null ? hub.transform.Find(GMOotpFrontOfficeUIController.RootName) : null;
            bool toolbar = root != null && root.Find("ToolbarBg") != null && root.Find("Menu0") != null;
            bool sidebar = root != null && root.Find("Quick0") != null;
            bool setup = root != null && root.Find(GMOotpFrontOfficeUIController.ManagerSetupName) != null;
            string area = GMOotpFrontOfficeUIController.ContentAreaName;
            bool schedule = root != null && root.Find($"{area}/{GMOotpFrontOfficeUIController.PaneSchedule}") != null;
            bool tree = root != null && root.Find($"{area}/{GMOotpFrontOfficeUIController.PanePostseason}") != null;
            bool live = prePost != null && prePost.transform.Find(GMMatchPrePostUIController.LiveRootName) != null;
            if (hub != null) EditorUtility.SetDirty(hub);
            if (prePost != null) EditorUtility.SetDirty(prePost);
            if (hub != null && hub.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
            Debug.Log($"[SetupTaskGM07] TASK-GM-07 점검 - 툴바 {Ok(toolbar)} · 사이드바 {Ok(sidebar)} · 감독 설정 {Ok(setup)} · 시즌 일정 {Ok(schedule)} · " +
                      $"포스트시즌 트리 {Ok(tree)} · 실시간 이닝 경기 {Ok(live)} - 씬을 저장(Ctrl+S)하십시오.");
        }

        private static string Ok(bool v) => v ? "있음" : "없음";
    }
}
