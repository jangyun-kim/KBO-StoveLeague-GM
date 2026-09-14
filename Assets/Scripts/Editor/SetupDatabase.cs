using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-084] TASK-KBO-082가 만든 CSV 기반 PlayerDatabase가 씬 어디에도 컴포넌트로 붙어 있지
    /// 않아 Awake()/Initialize()를 받지 못하던 구조적 결함의 핫픽스. GameManager 컴포넌트가 실제로
    /// 붙어 있는 오브젝트(=정본 "GameManagers")를 찾아 그 오브젝트에만 PlayerDatabase를 부착한다.
    /// 이름으로 찾지 않고 GameManager 컴포넌트 기준으로 찾는 이유는, TASK-076이 겪었던 "이름만 비슷한
    /// 엉뚱한 오브젝트(UIControllers)에 매니저가 잘못 붙는" 사고를 원천적으로 피하기 위함이다.
    /// </summary>
    public static class SetupDatabase
    {
        [MenuItem("KBO Manager/Setup/Auto-Connect Player Database")]
        public static void AutoConnectPlayerDatabase()
        {
            var gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gameManager == null)
            {
                Debug.LogError("[SetupDatabase] 씬에서 GameManager 컴포넌트를 찾지 못했습니다. " +
                    "GameManagers 오브젝트가 씬에 존재하는지 먼저 확인하세요.");
                return;
            }

            var managerObj = gameManager.gameObject;

            if (!managerObj.TryGetComponent<PlayerDatabase>(out _))
            {
                Undo.AddComponent<PlayerDatabase>(managerObj);

                if (!Application.isPlaying)
                {
                    EditorSceneManager.MarkSceneDirty(managerObj.scene);
                }
            }

            Debug.Log("[SetupDatabase] PlayerDatabase 컴포넌트가 GameManagers 오브젝트에 정상적으로 추가/확인되었습니다.");
        }
    }
}
