using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-076] TASK-KBO-075 조사 중 발견된 중복 UIManager 컴포넌트 핫픽스 -
    /// "GameManagers"(정본)와 "UIControllers"(잘못 붙은 쪽) 양쪽에 UIManager가 하나씩 존재한다.
    /// UIManager는 싱글톤이라(UIManager.cs Awake()에서 Instance != this면 Destroy(gameObject))
    /// 런타임에 어느 쪽이 먼저 초기화되느냐에 따라 나머지 하나의 GameObject 전체가 파괴될 수 있고,
    /// "UIControllers"가 살아남는 쪽으로 경쟁이 갈리면 "GameManagers"에 함께 붙은 GameManager/
    /// LeagueManager 등 다른 핵심 매니저까지 유실되는 치명적 사고로 이어진다.
    /// 이 스크립트는 "UIControllers" 오브젝트 자체가 아니라 그 위에 잘못 붙은 UIManager 컴포넌트
    /// 하나만 정확히 겨냥해 제거한다.
    /// </summary>
    public static class FixDuplicateManagers
    {
        private const string InvalidHostObjectName = "UIControllers";

        [MenuItem("KBO Manager/Setup/Fix Duplicate UIManager")]
        public static void FixDuplicateUIManager()
        {
            // [TASK-KBO-077] FindObjectsSortMode 인자를 받는 오버로드는 Unity 6000.6에서 Obsolete(CS0618)
            // 처리됐다 - 정렬 순서가 이 스캔에 필요하지 않아 공식 경고 문구가 권장하는 FindObjectsInactive
            // 단일 인자 오버로드로 교체했다.
            UIManager[] managers = Object.FindObjectsByType<UIManager>(FindObjectsInactive.Include);

            bool removedAny = false;
            foreach (var manager in managers)
            {
                if (manager.gameObject.name != InvalidHostObjectName) continue;

                // GameObject가 아니라 컴포넌트 자체만 제거 대상으로 넘긴다 - DestroyImmediate가 아니라
                // Undo.DestroyObjectImmediate를 써서 Ctrl+Z로 되돌릴 수 있는 Undo 기록을 함께 남긴다.
                Undo.DestroyObjectImmediate(manager);
                removedAny = true;

                Debug.Log("UIControllers 오브젝트에서 잘못된 UIManager 컴포넌트를 성공적으로 제거했습니다.");
            }

            if (!removedAny)
            {
                Debug.Log("중복 UIManager가 발견되지 않았습니다. 씬이 깨끗합니다.");
                return;
            }

            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            }
        }
    }
}
