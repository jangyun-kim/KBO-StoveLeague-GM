using System.Linq;
using KBOManager.Controllers;
using KBOManager.Managers;
using KBOManager.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-GM-08] 씬 · 에셋 점검(idempotent, SetupMasterBinding 체인 마지막).
    ///   1) 삼성 오디오(Assets/Resources/Audio/SAM, ogg 17곡) 임포트 설정 - 리그 홈 BGM은 Streaming, 응원가 · 아웃송은 Compressed In Memory(백그라운드 로드)
    ///   2) 씬에 GMAudioManager 오브젝트(BGM · 응원가 · 효과음 · 응원 믹스 4채널) 배치
    ///   3) 프런트 오피스 새 화면(FA 보상·보호명단 · AI 역제안 팝업 · 포스트시즌 KBO 리더 패널) 존재 확인 - 조립은 SetupTaskGM06 → Build
    /// </summary>
    public static class SetupTaskGM08
    {
        public const string AudioFolder = "Assets/Resources/Audio/SAM";

        [MenuItem("KBO Manager/Setup/Apply TASK-GM-08 (Samsung Audio + Trade AI + FA Compensation)")]
        public static void ApplyAll()
        {
            int configured = ConfigureAudioImporters();
            var audio = Object.FindAnyObjectByType<GMAudioManager>(FindObjectsInactive.Include);
            if (audio == null || (audio.gameObject.hideFlags & HideFlags.DontSave) != 0)
            {
                var go = new GameObject("GMAudioManager");
                audio = go.AddComponent<GMAudioManager>();
            }
            var profile = TeamAudioProfile.For("SAM");
            int loaded = profile.AllClipKeys().Count(k => Resources.Load<AudioClip>(GMAudioManager.ResourceRoot + k) != null);
            int total = profile.AllClipKeys().Count();

            var hub = Object.FindAnyObjectByType<GMOotpFrontOfficeUIController>(FindObjectsInactive.Include);
            var root = hub != null ? hub.transform.Find(GMOotpFrontOfficeUIController.RootName) : null;
            string area = GMOotpFrontOfficeUIController.ContentAreaName;
            bool protect = root != null && root.Find($"{area}/{GMOotpFrontOfficeUIController.PaneProtect}") != null;
            bool counter = root != null && root.Find(GMOotpFrontOfficeUIController.CounterPopupName) != null;
            bool leaders = root != null && root.Find($"{area}/{GMOotpFrontOfficeUIController.PanePostseason}/LeadersPanel") != null;
            bool sound = root != null && root.Find($"{GMOotpFrontOfficeUIController.ManagerSetupName}/BgmVolumeSlider") != null;
            if (hub != null) EditorUtility.SetDirty(hub);
            EditorUtility.SetDirty(audio);
            if (audio.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(audio.gameObject.scene);
            Debug.Log($"[SetupTaskGM08] TASK-GM-08 점검 - 삼성 오디오 {loaded}/{total}곡 로드(임포트 설정 변경 {configured}건) · GMAudioManager 씬 배치 · " +
                      $"FA 보상·보호명단 {Ok(protect)} · AI 역제안 팝업 {Ok(counter)} · 포스트시즌 KBO 리더 {Ok(leaders)} · 사운드 슬라이더 {Ok(sound)} - 씬을 저장(Ctrl+S)하십시오.");
        }

        /// <summary>리그 홈 BGM(sam_bgm_*)은 Streaming, 나머지는 Compressed In Memory + 백그라운드 로드. 바뀐 파일 수.</summary>
        public static int ConfigureAudioImporters()
        {
            int changed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter importer)) continue;
                bool bgm = System.IO.Path.GetFileName(path).StartsWith("sam_bgm_");
                var settings = importer.defaultSampleSettings;
                var load = bgm ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
                if (settings.loadType == load && importer.loadInBackground && settings.compressionFormat == AudioCompressionFormat.Vorbis) continue;
                settings.loadType = load;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = true;
                importer.SaveAndReimport();
                changed++;
            }
            return changed;
        }

        private static string Ok(bool v) => v ? "있음" : "없음";
    }
}
