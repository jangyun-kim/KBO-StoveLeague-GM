using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-184] Unity 에디터를 열었을 때 빈 Untitled 씬이 아니라 Assets/Scenes/SampleScene.unity가 바로 보이도록 보장한다.
    ///   - EnsureOpen(save): 배치 Setup 마지막 단계와 EditMode 테스트 종료 시점(SampleSceneTestFixture)에서 호출 - 씬을 열고(필요하면 저장) 마지막 씬으로 남긴다.
    ///   - 에디터 시작 시(배치 모드 제외, 세션당 1회) 활성 씬이 저장되지 않은 Untitled면 SampleScene을 연다.
    /// </summary>
    [InitializeOnLoad]
    public static class SampleSceneGuard
    {
        public const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string SessionKey = "KBO_SampleSceneGuard_Checked";

        static SampleSceneGuard()
        {
            if (UnityEngine.Application.isBatchMode || SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);
            EditorApplication.delayCall += OpenIfUntitled;
        }

        private static void OpenIfUntitled()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var active = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(active.path) || active.isDirty) return;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>SampleScene을 단독으로 열고 save면 저장한다. forceReopen이면 현재 씬(테스트가 만든 임시 오브젝트 등)을 버리고
        /// 디스크의 SampleScene을 다시 연다 - EditMode 테스트 종료 시점 전용. 성공하면 true.</summary>
        public static bool EnsureOpen(bool save, bool forceReopen = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return false;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) return false;
            var active = SceneManager.GetActiveScene();
            bool reuse = !forceReopen && active.path == ScenePath && SceneManager.sceneCount == 1;
            var scene = reuse ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            return !save || EditorSceneManager.SaveScene(scene);
        }
    }
}
