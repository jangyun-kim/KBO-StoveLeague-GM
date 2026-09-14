using System.Collections.Generic;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-075] 씬에 비활성 상태로 이미 만들어져 있는 UI 패널들을 메뉴 클릭 한 번으로 스캔해
    /// UIManager.screens에 다시 매핑한다. SceneInitializer.BindUIManagerScreens와 동일하게
    /// SerializedObject/SerializedProperty로 private [SerializeField] screens 필드에 접근한다.
    /// 여러 번 실행해도 안전하다 - 매번 배열을 완전히 비우고(ClearArray) 현재 씬 상태 기준으로 새로 채운다.
    /// </summary>
    public static class SetupUIManager
    {
        private readonly struct ScreenBinding
        {
            public readonly ScreenType Type;
            public readonly string PanelName;

            public ScreenBinding(ScreenType type, string panelName)
            {
                Type = type;
                PanelName = panelName;
            }
        }

        // [결정 필요 -> 코드 선례로 해석] 명령서(TASK-KBO-075) 6항은 "ScreenType.Stats"라고 적었지만
        // 실제 UIManager.cs의 ScreenType enum에는 Stats가 없고 LeagueStats만 존재한다.
        // SceneInitializer.cs의 기존 StatsPanel 바인딩(Panels 배열)도 동일하게 LeagueStats를 쓰고
        // 있어(문서가 아니라 이미 검증된 코드 선례), 그 선례를 따라 LeagueStats로 해석했다.
        // CheerleaderShopPanel은 이번 조사 시점 기준 씬에 아직 존재하지 않는다(SetupShopUI.cs가
        // 아직 실행되지 않음) - 이 경우 아래 6항 예외 처리 규칙대로 경고만 남기고 스킵한다.
        private static readonly ScreenBinding[] Bindings =
        {
            new ScreenBinding(ScreenType.Lobby, "LobbyPanel"),
            new ScreenBinding(ScreenType.InGame, "InGamePanel"),
            new ScreenBinding(ScreenType.LeagueStats, "StatsPanel"),
            new ScreenBinding(ScreenType.Shop, "ShopPanel"),
            new ScreenBinding(ScreenType.Onboarding, "OnboardingPanel"),
            new ScreenBinding(ScreenType.CheerleaderInventory, "CheerleaderInventoryPanel"),
            new ScreenBinding(ScreenType.CheerleaderShop, "CheerleaderShopPanel"),
        };

        [MenuItem("KBO Manager/Setup/Auto-Bind All UI Screens")]
        public static void AutoBindAllUiScreens()
        {
            var uiManagers = Object.FindObjectsByType<UIManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (uiManagers.Length == 0)
            {
                Debug.LogError("[SetupUIManager] 씬에서 UIManager 컴포넌트를 찾지 못했습니다. " +
                    "GameManagers 오브젝트에 UIManager 컴포넌트가 붙어 있는지 먼저 확인하세요.");
                return;
            }

            if (uiManagers.Length > 1)
            {
                var names = new List<string>(uiManagers.Length);
                foreach (var manager in uiManagers) names.Add(manager.gameObject.name);

                // UIManager.Awake()는 싱글톤 패턴이라 두 번째 인스턴스는 Destroy(gameObject)로
                // 자기 GameObject 전체가 파괴된다 - 그 오브젝트에 다른 매니저(GameManager 등)가
                // 같이 붙어 있다면 그것들도 함께 사라지는 치명적 사고로 이어질 수 있다. 자동으로
                // 지우지 않고 경고만 남긴다(씬 오브젝트 삭제는 이 작업 범위 밖의 파괴적 행동).
                Debug.LogWarning($"[SetupUIManager] 씬에 UIManager 컴포넌트가 {uiManagers.Length}개 있습니다 " +
                    $"({string.Join(", ", names)}). UIManager는 싱글톤이므로 런타임에 하나만 살아남고 " +
                    "나머지는 자신의 GameObject 전체가 파괴됩니다 - 중복 오브젝트를 반드시 수동으로 정리하세요. " +
                    $"이번 바인딩은 첫 번째로 찾은 '{uiManagers[0].gameObject.name}'에만 적용됩니다.");
            }

            var uiManager = uiManagers[0];
            var scene = EditorSceneManager.GetActiveScene();

            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");
            screensProperty.ClearArray();

            int boundCount = 0;
            foreach (var binding in Bindings)
            {
                var panelObject = FindInScene(scene, binding.PanelName);
                if (panelObject == null)
                {
                    Debug.LogWarning($"[SetupUIManager] '{binding.PanelName}'을 찾지 못해 {binding.Type} 매핑을 스킵합니다.");
                    continue;
                }

                int index = screensProperty.arraySize;
                screensProperty.InsertArrayElementAtIndex(index);
                var element = screensProperty.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("Type").intValue = (int)binding.Type;
                element.FindPropertyRelative("Root").objectReferenceValue = panelObject;
                boundCount++;
            }

            serializedManager.ApplyModifiedProperties();
            EditorUtility.SetDirty(uiManager);
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[SetupUIManager] UIManager.screens 재바인딩 완료 - {boundCount}/{Bindings.Length}개 화면 매핑 " +
                $"('{uiManager.gameObject.name}' 오브젝트 기준).");
        }

        /// <summary>비활성 오브젝트까지 포함해 씬 전체를 재귀적으로 탐색한다. Resources.FindObjectsOfTypeAll는
        /// 프로젝트 창의 프리팹 에셋까지 함께 걸려 동일한 이름의 프리팹이 있으면 엉뚱한 것을 찾아올 수 있어
        /// 대신 SceneInitializer.cs의 FindChild/FindInScene과 동일한 관례로 활성 씬의 루트부터 직접 훑는다.</summary>
        private static GameObject FindInScene(Scene scene, string objectName)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = FindRecursive(root.transform, objectName);
                if (found != null) return found;
            }
            return null;
        }

        private static GameObject FindRecursive(Transform parent, string objectName)
        {
            if (parent.name == objectName) return parent.gameObject;

            for (int i = 0; i < parent.childCount; i++)
            {
                var found = FindRecursive(parent.GetChild(i), objectName);
                if (found != null) return found;
            }

            return null;
        }
    }
}
