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
    /// [TASK-GM-06] 씬 적용(idempotent, SetupMasterBinding 체인의 마지막 단계):
    ///   1) 메인 캔버스 CanvasScaler 1920×1080(Landscape, match 0.5) · PlayerSettings 1920×1080 가로 고정
    ///   2) OOTP 27 프런트 오피스 허브(GMOotpFrontOffice + GMOotpFrontOfficeUIController) 조립 · 꺼 둠(로비 진입 시 코드로 연다)
    ///   3) 단장 모드 화면(GM02~05 · 허브)은 캔버스 전체 1920×1080, 기존 세로 화면 루트(캔버스 전체를 덮는 자식)는 LegacyPortraitFrame 9:16 프레임
    /// GM02~05 화면은 각자의 Setup(체인 앞 단계)이 Build()로 다시 조립하며, Build()가 1920×1080 좌표를 쓴다.
    /// </summary>
    public static class SetupTaskGM06
    {
        public const string ObjectName = "GMOotpFrontOffice";
        public static readonly Vector2 ReferenceResolution = new Vector2(1920, 1080);

        public sealed class Result
        {
            public bool CanvasLandscape, HubBuilt;
            public int LandscapeViews, FramedLegacy;
        }

        [MenuItem("KBO Manager/Setup/Apply TASK-GM-06 (1920x1080 Landscape + OOTP Front Office)")]
        public static void ApplyAll()
        {
            var r = Apply();
            Debug.Log($"[SetupTaskGM06] TASK-GM-06 적용 완료 - 캔버스 1920×1080 {(r.CanvasLandscape ? "적용" : "실패")} · 프런트 오피스 허브 {(r.HubBuilt ? "조립" : "건너뜀")} · " +
                      $"단장 모드 가로 화면 {r.LandscapeViews}개 · 기존 세로 화면 9:16 프레임 {r.FramedLegacy}개 - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static Result Apply()
        {
            var result = new Result();
            ApplyPlayerSettings();
            var scaler = MainScaler();
            if (scaler == null)
            {
                Debug.LogWarning("[SetupTaskGM06] ScaleWithScreenSize 캔버스가 없어 건너뜁니다.");
                return result;
            }
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;
            EditorUtility.SetDirty(scaler);
            result.CanvasLandscape = true;

            result.HubBuilt = BuildHub(scaler);
            foreach (Transform child in scaler.transform)
            {
                var rect = child as RectTransform;
                if (rect == null) continue;
                if (IsLandscapeView(child))
                {
                    var frame = child.GetComponent<LegacyPortraitFrame>();
                    if (frame != null) Object.DestroyImmediate(frame);
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    rect.localScale = Vector3.one;
                    result.LandscapeViews++;
                    EditorUtility.SetDirty(rect);
                    continue;
                }
                bool stretch = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one;
                var existing = child.GetComponent<LegacyPortraitFrame>();
                if (!stretch && existing == null) continue;
                if (existing == null) existing = child.gameObject.AddComponent<LegacyPortraitFrame>();
                LegacyPortraitFrame.ApplyTo(rect, ReferenceResolution);
                result.FramedLegacy++;
                EditorUtility.SetDirty(rect);
                EditorUtility.SetDirty(existing);
            }
            if (scaler.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(scaler.gameObject.scene);
            return result;
        }

        public static CanvasScaler MainScaler() => Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(s => s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize);

        /// <summary>1920×1080 좌표로 직접 배치되는 단장 모드 화면인지.</summary>
        public static bool IsLandscapeView(Transform t) =>
            t.GetComponent<GMOotpFrontOfficeUIController>() != null || t.GetComponent<GMLiveLeagueDashboardUIController>() != null ||
            t.GetComponent<GMMatchPrePostUIController>() != null || t.GetComponent<GMAwardsCeremonyUIController>() != null ||
            t.GetComponent<GMCheerleaderEntryUIController>() != null;

        private static bool BuildHub(CanvasScaler scaler)
        {
            var view = Object.FindAnyObjectByType<GMOotpFrontOfficeUIController>(FindObjectsInactive.Include);
            if (view == null)
            {
                var go = new GameObject(ObjectName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Create GM OOTP Front Office");
                go.transform.SetParent(scaler.transform, false);
                view = go.AddComponent<GMOotpFrontOfficeUIController>();
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
            foreach (var t in view.Root.GetComponentsInChildren<Text>(true)) EditorUtility.SetDirty(t);
            view.transform.SetAsLastSibling();
            view.gameObject.SetActive(false);
            EditorUtility.SetDirty(view);
            if (view.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
            return true;
        }

        /// <summary>PlayerSettings - 기본 1920×1080 · 가로(LandscapeLeft) 고정 · 세로 자동 회전 끔.</summary>
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
        }
    }
}
