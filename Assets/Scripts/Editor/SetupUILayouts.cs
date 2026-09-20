using System;
using KBOManager.Controllers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-106] UI 레이아웃(좌표/정렬) 자동 정리 핫픽스.
    ///
    /// 작업 전 씬을 직접 재조회한 결과, 명령서 3항이 "전부 (0,0)에 겹쳐서 생성"됐다고 서술한 4개
    /// 대상 중 3개(`StandingsContainer`의 `VerticalLayoutGroup`, `MatchStatusArea`의
    /// `GridLayoutGroup`, `BatterContainer`/`PitcherContainer`의 `GridLayoutGroup`+좌우 앵커 분리)는
    /// 이전 태스크(`SetupLeagueUI.cs`/`SetupInGameUI.cs`/`SetupRosterUI.cs`)에서 이미 부착·분리돼
    /// 있었다 - 내부 요소끼리는 겹치지 않는다. 대신 재조사 중 실제로 겹치는 지점을 하나 발견했다:
    /// 인게임 화면에서 `LogScrollView`(y:0~0.85, 좌측 절반)의 상단이 `MatchStatusArea`(y:0.7~0.85,
    /// 전체 폭)와 y:[0.7, 0.85] 구간에서 겹쳐, 중계 로그 상단 줄이 베이스/카운트 전광판에 가려진다.
    /// 이 스크립트는 (1) 이미 붙어 있는 레이아웃 그룹은 `TryGetComponent`로 건드리지 않고, (2) 없는
    /// 경우에만 새로 부착하며, (3) 실제로 확인된 `LogScrollView`/`MatchStatusArea` 겹침만 앵커 조정으로
    /// 바로잡는다.
    /// </summary>
    public static class SetupUILayouts
    {
        private const string StandingsContainerName = "StandingsContainer";
        private const string MatchStatusAreaName = "MatchStatusArea";
        private const string LogScrollViewName = "LogScrollView";
        private const string BatterContainerName = "BatterContainer";
        private const string PitcherContainerName = "PitcherContainer";

        // MatchStatusArea의 y 하단 경계(0.7)와 동일하게 맞춰, LogScrollView가 그 아래(0~0.7)만
        // 차지하도록 한다. 원래 값(0.85)은 SetupInGameUI.cs(TASK-101)가 부여한 값이다.
        private const float LogScrollViewMaxY = 0.7f;

        [MenuItem("KBO Manager/Setup/Auto-Arrange UI Layouts")]
        public static void AutoArrangeUILayouts()
        {
            ArrangeLobbyDashboard();
            ArrangeInGameLayout();
            ArrangeRosterLayout();

            Debug.Log("[SetupUILayouts] UI 레이아웃 자동 정리 완료.");
        }

        private static void ArrangeLobbyDashboard()
        {
            try
            {
                var dashboard = UnityEngine.Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
                if (dashboard == null)
                {
                    Debug.LogWarning("[SetupUILayouts] 씬에서 LeagueDashboardUIController를 찾지 못해 로비 레이아웃 정리를 건너뜁니다.");
                    return;
                }

                EnsureVerticalLayoutGroup(dashboard.transform, StandingsContainerName);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SetupUILayouts] 로비 대시보드 레이아웃 정리 중 예외 발생: {e}");
            }
        }

        private static void ArrangeInGameLayout()
        {
            try
            {
                var inGame = UnityEngine.Object.FindAnyObjectByType<InGameUIController>(FindObjectsInactive.Include);
                if (inGame == null)
                {
                    Debug.LogWarning("[SetupUILayouts] 씬에서 InGameUIController를 찾지 못해 인게임 레이아웃 정리를 건너뜁니다.");
                    return;
                }

                EnsureGridLayoutGroup(inGame.transform, MatchStatusAreaName, new Vector2(30f, 30f), new Vector2(4f, 0f));
                FixLogScrollViewOverlap(inGame.transform);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SetupUILayouts] 인게임 레이아웃 정리 중 예외 발생: {e}");
            }
        }

        private static void ArrangeRosterLayout()
        {
            try
            {
                var roster = UnityEngine.Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
                if (roster == null)
                {
                    Debug.LogWarning("[SetupUILayouts] 씬에서 RosterUIController를 찾지 못해 로스터 레이아웃 정리를 건너뜁니다.");
                    return;
                }

                EnsureGridLayoutGroup(roster.transform, BatterContainerName, new Vector2(140f, 200f), new Vector2(10f, 10f));
                EnsureGridLayoutGroup(roster.transform, PitcherContainerName, new Vector2(140f, 200f), new Vector2(10f, 10f));
            }
            catch (Exception e)
            {
                Debug.LogError($"[SetupUILayouts] 로스터 레이아웃 정리 중 예외 발생: {e}");
            }
        }

        /// <summary>대상을 찾아 VerticalLayoutGroup이 없을 때만 부착한다. 이미 있으면 기존 설정(간격/정렬 등)을 건드리지 않는다.</summary>
        private static void EnsureVerticalLayoutGroup(Transform root, string name)
        {
            var target = FindChildByName(root, name);
            if (target == null)
            {
                Debug.LogWarning($"[SetupUILayouts] '{name}'를 찾지 못해 건너뜁니다.");
                return;
            }

            if (!target.TryGetComponent<VerticalLayoutGroup>(out _))
            {
                var layout = Undo.AddComponent<VerticalLayoutGroup>(target.gameObject);
                layout.spacing = 2f;
                layout.childForceExpandHeight = false;
                layout.childControlHeight = false;
                layout.childAlignment = TextAnchor.UpperCenter;
                Debug.Log($"[SetupUILayouts] '{name}'에 VerticalLayoutGroup을 신규 부착했습니다.");
            }
            else
            {
                Debug.Log($"[SetupUILayouts] '{name}'에는 이미 VerticalLayoutGroup이 부착돼 있어 그대로 둡니다.");
            }

            EditorUtility.SetDirty(target);
            MarkSceneDirty(target);
        }

        /// <summary>대상을 찾아 GridLayoutGroup이 없을 때만 부착한다. 이미 있으면 기존 설정을 건드리지 않는다.</summary>
        private static void EnsureGridLayoutGroup(Transform root, string name, Vector2 cellSize, Vector2 spacing)
        {
            var target = FindChildByName(root, name);
            if (target == null)
            {
                Debug.LogWarning($"[SetupUILayouts] '{name}'를 찾지 못해 건너뜁니다.");
                return;
            }

            if (!target.TryGetComponent<GridLayoutGroup>(out _))
            {
                var grid = Undo.AddComponent<GridLayoutGroup>(target.gameObject);
                grid.cellSize = cellSize;
                grid.spacing = spacing;
                Debug.Log($"[SetupUILayouts] '{name}'에 GridLayoutGroup을 신규 부착했습니다.");
            }
            else
            {
                Debug.Log($"[SetupUILayouts] '{name}'에는 이미 GridLayoutGroup이 부착돼 있어 그대로 둡니다.");
            }

            EditorUtility.SetDirty(target);
            MarkSceneDirty(target);
        }

        /// <summary>
        /// 재조사로 확인한 실제 겹침(클래스 주석 참고)만 바로잡는다. LogScrollView의 anchorMax.y가
        /// MatchStatusArea의 하단 경계(0.7)보다 위에 있을 때만(즉 아직 겹칠 때만) 낮춘다 - 이미
        /// 해소돼 있으면(예: 다른 값으로 수동 조정된 경우) 건드리지 않는다.
        /// </summary>
        private static void FixLogScrollViewOverlap(Transform root)
        {
            var logScrollView = FindChildByName(root, LogScrollViewName);
            if (logScrollView == null)
            {
                Debug.LogWarning($"[SetupUILayouts] '{LogScrollViewName}'을 찾지 못해 건너뜁니다.");
                return;
            }

            var anchorMax = logScrollView.anchorMax;
            if (anchorMax.y > LogScrollViewMaxY)
            {
                anchorMax.y = LogScrollViewMaxY;
                logScrollView.anchorMax = anchorMax;
                Debug.Log($"[SetupUILayouts] '{LogScrollViewName}'의 anchorMax.y를 {LogScrollViewMaxY:F2}로 낮춰 '{MatchStatusAreaName}'와의 겹침을 해소했습니다.");
            }
            else
            {
                Debug.Log($"[SetupUILayouts] '{LogScrollViewName}'은 이미 '{MatchStatusAreaName}'과 겹치지 않는 범위입니다.");
            }

            EditorUtility.SetDirty(logScrollView);
            MarkSceneDirty(logScrollView);
        }

        private static RectTransform FindChildByName(Transform root, string name)
        {
            foreach (var candidate in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (candidate.name == name) return candidate;
            }
            return null;
        }

        private static void MarkSceneDirty(Component component)
        {
            var scene = component.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
