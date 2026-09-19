using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-098] "[ScoutUIController] ScoutManager가 연결되지 않았습니다." 경고와 함께 스카우트/로스터
    /// 화면이 하얗게 멈추는 버그의 핫픽스. `ScoutManager`/`RosterManager`/`UpgradeManager`가 씬의
    /// `GameManagers` 오브젝트에 전혀 부착되어 있지 않았던 것이 근본 원인이었다(스크립트 GUID로 씬을
    /// grep해 확인 - 전부 0건).
    ///
    /// `GameManagers`는 `SetupDatabase.cs`와 동일한 관례로 이름이 아니라 `GameManager` 컴포넌트를
    /// 기준으로 찾는다 - `SetupDatabase.cs`가 남긴 TASK-076 사고 기록("UIControllers"처럼 이름만 비슷한
    /// 엉뚱한 오브젝트에 매니저가 잘못 붙었던 사고)을 그대로 따라 이름 기반 검색의 위험을 피한다.
    ///
    /// `ScoutManager`/`LeagueManager`가 요구하는 `SkillDB`, `UpgradeManager`가 요구하는
    /// `UpgradeProbabilityDB`, `LeagueManager`가 요구하는 `EngineConfig`는 전부 `ScriptableObject`라 씬이
    /// 아니라 프로젝트 에셋으로 존재해야 하는데, 조사 결과(`AssetDatabase` 검색) 세 타입 모두 .asset
    /// 인스턴스가 프로젝트에 단 하나도 없었다. `ItemDataSeeder.cs`가 `ItemTemplate` .asset을 만드는 것과
    /// 동일한 관례(`ScriptableObject.CreateInstance` + `AssetDatabase.CreateAsset`)로 `Assets/GameData`
    /// 하위에 새로 만들고, 각 타입이 이미 갖고 있던 `PopulateDefaults()`(GDD 기본값 채움 - 스킬 확률표/
    /// 강화 확률표/엔진 밸런스 상수, 기존에 이미 검증되어 있던 메서드)를 "새로 생성한 경우에만" 호출한다
    /// - 이미 존재하는 에셋은 절대 덮어쓰지 않는다(디자이너가 인스펙터에서 손으로 튜닝했을 값 보존).
    /// </summary>
    public static class SetupManagers
    {
        private const string GameDataFolder = "Assets/GameData";
        private const string SkillDBAssetPath = GameDataFolder + "/SkillDB.asset";
        private const string UpgradeProbabilityDBAssetPath = GameDataFolder + "/UpgradeProbabilityDB.asset";
        private const string EngineConfigAssetPath = GameDataFolder + "/EngineConfig.asset";

        [MenuItem("KBO Manager/Setup/Auto-Connect All Managers")]
        public static void AutoConnectAllManagers()
        {
            var gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gameManager == null)
            {
                Debug.LogError("[SetupManagers] 씬에서 GameManager 컴포넌트를 찾지 못했습니다. " +
                    "'KBO Manager/Initialize Current Scene'을 먼저 실행해 GameManagers 오브젝트를 만드십시오.");
                return;
            }

            var managerObject = gameManager.gameObject;

            var playerDatabase = EnsureComponent<PlayerDatabase>(managerObject);
            var scoutManager = EnsureComponent<ScoutManager>(managerObject);
            EnsureComponent<RosterManager>(managerObject); // [SerializeField] 종속성 없음(DCL-067) - 부착만으로 충분
            var upgradeManager = EnsureComponent<UpgradeManager>(managerObject);
            var leagueManager = EnsureComponent<LeagueManager>(managerObject);

            var skillDB = FindOrCreateAsset<SkillDB>(SkillDBAssetPath, db => db.PopulateDefaults());
            var upgradeProbabilityDB = FindOrCreateAsset<UpgradeProbabilityDB>(UpgradeProbabilityDBAssetPath, db => db.PopulateDefaults());
            var engineConfig = FindOrCreateAsset<EngineConfig>(EngineConfigAssetPath, cfg => cfg.PopulateDefaults());

            BindScoutManager(scoutManager, playerDatabase, skillDB);
            BindUpgradeManager(upgradeManager, upgradeProbabilityDB);
            BindLeagueManager(leagueManager, skillDB, engineConfig, playerDatabase);

            EditorUtility.SetDirty(managerObject);
            EditorSceneManager.MarkSceneDirty(managerObject.scene);
            AssetDatabase.SaveAssets();

            Debug.Log("[SetupManagers] 필수 매니저(PlayerDatabase/ScoutManager/RosterManager/UpgradeManager/" +
                "LeagueManager) 부착 및 SkillDB/UpgradeProbabilityDB/EngineConfig 종속성 바인딩 완료.");
        }

        /// <summary>TryGetComponent로 이미 붙어 있는지 먼저 확인해(명령서 7항 - 여러 번 실행해도 컴포넌트가
        /// 중복 부착되지 않도록) 없을 때만 Undo.AddComponent로 부착한다.</summary>
        private static T EnsureComponent<T>(GameObject target) where T : Component
        {
            if (target.TryGetComponent<T>(out var existing)) return existing;

            var added = Undo.AddComponent<T>(target);
            Debug.Log($"[SetupManagers] {typeof(T).Name}이 씬에 없어 '{target.name}'에 새로 생성했습니다.");
            return added;
        }

        /// <summary>지정 경로에 .asset이 이미 있으면 그대로 로드해 반환하고(기존 값 보존, 절대 덮어쓰지
        /// 않음), 없으면 새로 만들어 populateDefaults 콜백으로 GDD 기본값을 채운 뒤 저장한다.</summary>
        private static T FindOrCreateAsset<T>(string assetPath, System.Action<T> populateDefaults) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (existing != null) return existing;

            EnsureFolder(GameDataFolder);

            var created = ScriptableObject.CreateInstance<T>();
            populateDefaults(created);
            AssetDatabase.CreateAsset(created, assetPath);

            Debug.Log($"[SetupManagers] {typeof(T).Name} 에셋이 없어 '{assetPath}'에 GDD 기본값으로 새로 생성했습니다.");
            return created;
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            var parent = System.IO.Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            var folderName = System.IO.Path.GetFileName(folderPath);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(string.IsNullOrEmpty(parent) ? "Assets" : parent, folderName);
        }

        private static void BindScoutManager(ScoutManager scoutManager, PlayerDatabase playerDatabase, SkillDB skillDB)
        {
            var serialized = new SerializedObject(scoutManager);
            serialized.FindProperty("playerDatabase").objectReferenceValue = playerDatabase;
            serialized.FindProperty("skillDB").objectReferenceValue = skillDB;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(scoutManager);
        }

        private static void BindUpgradeManager(UpgradeManager upgradeManager, UpgradeProbabilityDB probabilityDB)
        {
            var serialized = new SerializedObject(upgradeManager);
            serialized.FindProperty("probabilityDB").objectReferenceValue = probabilityDB;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(upgradeManager);
        }

        private static void BindLeagueManager(LeagueManager leagueManager, SkillDB skillDB, EngineConfig engineConfig, PlayerDatabase playerDatabase)
        {
            var serialized = new SerializedObject(leagueManager);
            serialized.FindProperty("skillDB").objectReferenceValue = skillDB;
            serialized.FindProperty("engineConfig").objectReferenceValue = engineConfig;
            serialized.FindProperty("playerDatabase").objectReferenceValue = playerDatabase;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(leagueManager);
        }
    }
}
