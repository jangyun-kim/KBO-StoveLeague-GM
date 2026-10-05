using System.Linq;
using KBOManager.Controllers;
using KBOManager.Core;
using KBOManager.Managers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-GM-01] 『스토브리그: 단장의 시간』 전환 씬 적용(idempotent, SetupMasterBinding 체인의 마지막 단계).
    ///   1) 로비 메뉴 타일: [세트덱 &amp; 버프 선택] → [계약·연봉·팀워크 진단], [선수단 강화] → [치어리더 관리(구단 15인 / 엔트리 4~6인)],
    ///      [스카우트 센터] 부제 → 응원단 영입 · 상점.
    ///   2) 선수 관리 허브에 [계약·연봉·팀워크 진단](GMDiagnosticView) 부착 · 조립 · 바인딩(성장 센터 위).
    ///   3) 치어리더 관리 동선 점검: 로비 하단 [응원단] 탭 → CheerleaderInventory 화면 등록 → 닫기([X]) 버튼 바인딩(끊겼으면 복구).
    /// 강화 · 각성 · 세트덱 · 선수 뽑기 버튼 숨김은 런타임 컨트롤러가 GMFeatureFlags로 처리한다(씬 오브젝트는 지우지 않는다).
    /// </summary>
    public static class SetupTaskGM01
    {
        public const string DiagnosticTileTitle = "계약·연봉·팀워크 진단";
        public const string DiagnosticTileSub = "페이롤 · 케미스트리 점검";
        public const string CheerTileTitle = "치어리더 관리";
        public const string CheerTileSub = "구단 15인 · 엔트리 4~6인";
        public const string ScoutTileSub = "응원단 영입 · 상점";

        public sealed class Result
        {
            public bool LobbyTiles, Diagnostic, CheerNavBound, CheerScreenRegistered, CheerCloseBound, CheerCloseRepaired;
            public override string ToString() =>
                $"로비 타일 {(LobbyTiles ? "적용" : "없음")} · 진단 화면 {(Diagnostic ? "조립" : "없음")} · 응원단 탭 {(CheerNavBound ? "바인딩" : "미바인딩")} · " +
                $"치어리더 화면 등록 {(CheerScreenRegistered ? "O" : "X")} · 닫기 버튼 {(CheerCloseBound ? (CheerCloseRepaired ? "복구 바인딩" : "바인딩") : "없음")}";
        }

        [MenuItem("KBO Manager/Setup/Apply TASK-GM-01 (Stove League GM Conversion)")]
        public static void ApplyAll()
        {
            var result = Apply();
            Debug.Log($"[SetupTaskGM01] TASK-GM-01 적용 완료 - {result} - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static Result Apply()
        {
            var result = new Result
            {
                LobbyTiles = ApplyLobbyTiles(),
                Diagnostic = BuildDiagnostic(),
            };
            CheckCheerleaderRoute(result);
            return result;
        }

        // ================================================================== 1) 로비 타일

        public static bool ApplyLobbyTiles()
        {
            var home = Object.FindAnyObjectByType<LobbyHome181>(FindObjectsInactive.Include);
            if (home == null)
            {
                Debug.LogWarning("[SetupTaskGM01] LobbyHome181이 없어 로비 타일을 건너뜁니다.");
                return false;
            }
            var root = home.transform;
            bool any = false;
            any |= Retitle(root, "MenuTile0", null, ScoutTileSub);
            if (!GMFeatureFlags.IsSetDeckEnabled) any |= Retile(root, "MenuTile1", DiagnosticTileTitle, DiagnosticTileSub, ScreenType.Inventory);
            if (!GMFeatureFlags.IsCardGrowthEnabled) any |= Retile(root, "MenuTile2", CheerTileTitle, CheerTileSub, ScreenType.CheerleaderInventory);
            return any;
        }

        private static bool Retile(Transform root, string tileName, string title, string sub, ScreenType screen)
        {
            var tile = root.Find(tileName);
            if (tile == null) return false;
            var relay = tile.GetComponent<LobbyButtonRelay>();
            if (relay == null) relay = Undo.AddComponent<LobbyButtonRelay>(tile.gameObject);
            relay.Configure(screen); // [세트덱] 팝업 직행 플래그도 함께 해제된다
            EditorUtility.SetDirty(relay);
            return Retitle(root, tileName, title, sub);
        }

        private static bool Retitle(Transform root, string tileName, string title, string sub)
        {
            bool changed = false;
            if (title != null && root.Find(tileName + "_Title") is Transform t && t.TryGetComponent<Text>(out var titleText))
            {
                titleText.text = title;
                EditorUtility.SetDirty(titleText);
                changed = true;
            }
            if (sub != null && root.Find(tileName + "_Sub") is Transform s && s.TryGetComponent<Text>(out var subText))
            {
                subText.text = sub;
                EditorUtility.SetDirty(subText);
                changed = true;
            }
            if (changed) MarkDirty(root.GetComponent<LobbyHome181>());
            return changed;
        }

        // ================================================================== 2) 진단 화면

        public static bool BuildDiagnostic()
        {
            var hub = Object.FindAnyObjectByType<PlayerManagementUIController>(FindObjectsInactive.Include);
            if (hub == null)
            {
                Debug.LogWarning("[SetupTaskGM01] PlayerManagementUIController가 없어 진단 화면을 건너뜁니다.");
                return false;
            }
            if (!hub.TryGetComponent<GMDiagnosticView>(out var view)) view = Undo.AddComponent<GMDiagnosticView>(hub.gameObject);

            Font regular = null;
            if (hub.TryGetComponent<GrowthCenterView>(out var growth))
                regular = new SerializedObject(growth).FindProperty("regularFont").objectReferenceValue as Font;
            if (regular == null) regular = TextTidy.BodyFont;
            view.Configure(regular);
            view.Build();
            view.Root.SetAsLastSibling();
            foreach (var t in view.Root.GetComponentsInChildren<Text>(true)) EditorUtility.SetDirty(t);

            var so = new SerializedObject(hub);
            so.FindProperty("diagnosticView").objectReferenceValue = view;
            so.ApplyModifiedPropertiesWithoutUndo();
            MarkDirty(view);
            MarkDirty(hub);
            return true;
        }

        // ================================================================== 3) 치어리더 관리 동선

        public static void CheckCheerleaderRoute(Result result)
        {
            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            result.CheerNavBound = dashboard != null && new SerializedObject(dashboard).FindProperty("manageCheerleaderButton").objectReferenceValue != null;

            var inventory = Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include);
            var ui = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (ui != null && inventory != null)
            {
                var screens = new SerializedObject(ui).FindProperty("screens");
                for (int i = 0; i < screens.arraySize; i++)
                {
                    var entry = screens.GetArrayElementAtIndex(i);
                    if (entry.FindPropertyRelative("Type").intValue != (int)ScreenType.CheerleaderInventory) continue;
                    var root = entry.FindPropertyRelative("Root").objectReferenceValue as GameObject;
                    result.CheerScreenRegistered = root != null && (root == inventory.gameObject || inventory.transform.IsChildOf(root.transform));
                }
            }

            if (inventory == null) return;
            var so = new SerializedObject(inventory);
            var close = so.FindProperty("closeButton");
            if (close.objectReferenceValue == null)
            {
                var found = CheerleaderInventoryUIController.FindCloseButton(inventory.transform);
                if (found != null)
                {
                    close.objectReferenceValue = found;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    MarkDirty(inventory);
                    result.CheerCloseRepaired = true;
                }
            }
            result.CheerCloseBound = close.objectReferenceValue != null;
            if (!result.CheerNavBound || !result.CheerScreenRegistered || !result.CheerCloseBound)
                Debug.LogWarning($"[SetupTaskGM01] 치어리더 관리 동선 점검 필요 - {result}");
        }

        private static void MarkDirty(Component component)
        {
            if (component == null) return;
            EditorUtility.SetDirty(component);
            if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
