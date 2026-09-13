using System.IO;
using KBOManager.Managers;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// TASK-KBO-052: TASK-KBO-051에서 구현한 FanSentiment/LosingStreak 세이브 연동을 QA가 플레이 모드에서
    /// 메뉴 클릭 한 번으로 즉시 확인할 수 있게 하는 검증 전용 유틸리티. GameManager/SaveManager는 전혀
    /// 수정하지 않고, 이미 공개돼 있는 API(FanSentiment/LosingStreak 프로퍼티, SaveGame()/LoadGame())만
    /// 호출한다.
    /// </summary>
    public static class SaveDataDebugMenu
    {
        // SaveManager.SaveFileName(private)과 동일한 값. SaveManager를 수정하지 않고 세이브 파일 경로만
        // 열람하기 위해 이 에디터 전용 스크립트에서 별도로 들고 있다 - SaveManager 쪽 파일명이 바뀌면
        // 이 상수도 함께 갱신해야 한다.
        private const string SaveFileName = "kbo_manager_save.json";

        [MenuItem("KBO Manager/Debug/1. 팬심 상태 출력")]
        public static void LogFanSentimentStatus()
        {
            if (!TryGetGameManager(out var gm)) return;

            Debug.Log($"[SaveDataDebugMenu] FanSentiment={gm.FanSentiment}, LosingStreak={gm.LosingStreak}");
        }

        [MenuItem("KBO Manager/Debug/2. 팬심 0으로 강제 세팅")]
        public static void ForceFanSentimentToZero()
        {
            if (!TryGetGameManager(out var gm)) return;

            gm.FanSentiment = 0;
            Debug.Log($"[SaveDataDebugMenu] FanSentiment을 0으로 강제 세팅했습니다. (LosingStreak={gm.LosingStreak})");
        }

        [MenuItem("KBO Manager/Debug/3. 강제 저장")]
        public static void ForceSave()
        {
            if (!TryGetSaveManager(out var sm)) return;

            sm.SaveGame();
            Debug.Log("[SaveDataDebugMenu] SaveManager.SaveGame() 호출 완료.");
        }

        [MenuItem("KBO Manager/Debug/4. 강제 불러오기")]
        public static void ForceLoad()
        {
            if (!TryGetSaveManager(out var sm)) return;

            bool success = sm.LoadGame();
            Debug.Log(success
                ? "[SaveDataDebugMenu] SaveManager.LoadGame() 성공."
                : "[SaveDataDebugMenu] SaveManager.LoadGame() 실패(세이브 파일 없음 또는 파싱 실패).");
        }

        [MenuItem("KBO Manager/Debug/5. 세이브 파일 경로 열기")]
        public static void OpenSaveFileLocation()
        {
            string path = Path.Combine(Application.persistentDataPath, SaveFileName);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[SaveDataDebugMenu] 세이브 파일이 존재하지 않습니다: {path}");
                return;
            }

            Debug.Log($"[SaveDataDebugMenu] 세이브 파일 경로: {path}");
            EditorUtility.RevealInFinder(path);
        }

        private static bool TryGetGameManager(out GameManager gm)
        {
            gm = GameManager.Instance;
            if (gm != null) return true;

            Debug.LogWarning("[SaveDataDebugMenu] 플레이 모드에서만 실행 가능합니다.");
            return false;
        }

        private static bool TryGetSaveManager(out SaveManager sm)
        {
            sm = SaveManager.Instance;
            if (sm != null) return true;

            Debug.LogWarning("[SaveDataDebugMenu] 플레이 모드에서만 실행 가능합니다.");
            return false;
        }
    }
}
