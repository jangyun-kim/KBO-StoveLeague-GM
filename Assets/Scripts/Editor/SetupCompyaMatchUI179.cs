using System.IO;
using KBOManager.Controllers;
using KBOManager.Managers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-179] 경기 화면 컴프야V26 1:1 중계형 UI + 하단 5탭 홈 전용 고정을 씬에 적용한다(전부 idempotent).
    ///   1) Resources/Broadcast179 텍스처(CropBroadcastAssets179.py가 레퍼런스에서 크롭) 임포트 설정 - 비2제곱 크기 유지(NPOT None),
    ///      밉맵 끔, 무압축 - 로고 비율(AspectRatioFitter)과 그라운드/타석 뷰 좌표 정합을 보장한다.
    ///   2) InGamePanel(InGameUIController) 하위 "CompyaMatchView179"에 CompyaMatchView를 붙이고 참조/폰트를 주입한 뒤 Build()로
    ///      9개 화면 계층을 만들어 씬에 저장되게 한다(런타임 Awake에서도 같은 코드로 재조립한다).
    ///   3) 로비 하단 5탭(Layout178/BottomNav)에 LobbyOnlyNav를 붙여 홈 화면 최전면일 때만 보이게 한다.
    /// 로비 [플레이 볼]은 LeagueDashboardUIController.StartMatch()가 이 뷰를 찾아 "경기 유형 선택"부터 띄운다(코드 연결 - 씬 바인딩 불필요).
    /// </summary>
    public static class SetupCompyaMatchUI179
    {
        public const string ViewObjectName = "CompyaMatchView179";
        private const string TextureFolder = "Assets/Resources/Broadcast179";

        [MenuItem("KBO Manager/Setup/Apply Compya Match UI (TASK-179)")]
        public static void ApplyAll()
        {
            ConfigureTextureImporters();
            AttachMatchView();
            AttachLobbyOnlyNav();
            Debug.Log("[SetupCompyaMatchUI179] 컴프야V26 경기 화면(9종) + 하단 5탭 홈 전용 적용 완료 - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static void ConfigureTextureImporters()
        {
            if (!AssetDatabase.IsValidFolder(TextureFolder))
            {
                Debug.LogWarning($"[SetupCompyaMatchUI179] {TextureFolder} 폴더가 없습니다 - 프로젝트 루트에서 CropBroadcastAssets179.py를 실행하십시오.");
                return;
            }

            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TextureFolder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer)) continue;
                bool dirty = importer.textureType != TextureImporterType.Default
                    || importer.npotScale != TextureImporterNPOTScale.None
                    || importer.mipmapEnabled
                    || importer.textureCompression != TextureImporterCompression.Uncompressed
                    || importer.wrapMode != TextureWrapMode.Clamp
                    || importer.maxTextureSize < 2048
                    || importer.alphaIsTransparency != Path.GetFileName(path).StartsWith("logo_");
                if (!dirty) continue;

                importer.textureType = TextureImporterType.Default;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 2048;
                importer.alphaIsTransparency = Path.GetFileName(path).StartsWith("logo_");
                importer.SaveAndReimport();
                changed++;
            }
            Debug.Log($"[SetupCompyaMatchUI179] Broadcast179 텍스처 임포트 설정 {changed}개 갱신.");
        }

        public static void AttachMatchView()
        {
            var inGame = Object.FindAnyObjectByType<InGameUIController>(FindObjectsInactive.Include);
            if (inGame == null)
            {
                Debug.LogWarning("[SetupCompyaMatchUI179] InGameUIController(InGamePanel)가 없어 경기 화면을 건너뜁니다 - 'Auto-Connect InGame UI'를 먼저 실행하십시오.");
                return;
            }

            var panel = inGame.transform;
            var holder = panel.Find(ViewObjectName) as RectTransform;
            if (holder == null)
            {
                var go = new GameObject(ViewObjectName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, "Create Compya Match View");
                holder = (RectTransform)go.transform;
                holder.SetParent(panel, false);
            }
            holder.anchorMin = Vector2.zero;
            holder.anchorMax = Vector2.one;
            holder.offsetMin = Vector2.zero;
            holder.offsetMax = Vector2.zero;
            holder.SetAsLastSibling();

            if (!holder.TryGetComponent<CompyaMatchView>(out var view)) view = Undo.AddComponent<CompyaMatchView>(holder.gameObject);
            view.ConfigureForEditor(
                Object.FindAnyObjectByType<PlayBallController>(FindObjectsInactive.Include),
                Object.FindAnyObjectByType<BroadcastUIManager>(FindObjectsInactive.Include),
                inGame,
                Object.FindAnyObjectByType<MatchRewardManager>(FindObjectsInactive.Include),
                KBOFonts.Bold,
                KBOFonts.Medium);
            view.Build();
            MarkDirty(view);
        }

        public static void AttachLobbyOnlyNav()
        {
            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            var nav = dashboard != null ? dashboard.transform.Find("Layout178/BottomNav") : null;
            if (nav == null)
            {
                Debug.LogWarning("[SetupCompyaMatchUI179] 로비 하단 5탭(Layout178/BottomNav)이 없어 홈 전용 고정을 건너뜁니다 - TASK-178 테마가 먼저 적용돼야 합니다.");
                return;
            }
            if (!nav.TryGetComponent<LobbyOnlyNav>(out var guard)) guard = Undo.AddComponent<LobbyOnlyNav>(nav.gameObject);
            MarkDirty(guard);
        }

        private static void MarkDirty(Component component)
        {
            EditorUtility.SetDirty(component);
            var scene = component.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
