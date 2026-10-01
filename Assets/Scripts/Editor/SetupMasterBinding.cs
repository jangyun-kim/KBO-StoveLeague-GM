using System;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-105] 씬에 이미 조립돼 있으나 인스펙터 참조가 None으로 비어 있는 핵심 UI 컨트롤러들의
    /// 매니저/텍스트 참조를 한 번에 재탐색·재바인딩하는 통합 핫픽스. 각 Setup*.cs(SetupScoutUI.cs/
    /// SetupLeagueUI.cs/SetupInGameUI.cs)가 최초 조립 시점에 놓쳤거나, 저장 누락/씬 리로드 등으로
    /// 유실된 참조를 대상으로 하며, 새 UI 오브젝트를 생성하지 않고 씬에 이미 존재하는 오브젝트만
    /// 이름으로 다시 찾아 연결한다(명령서 5항 - RectTransform 레이아웃 불변).
    /// </summary>
    public static class SetupMasterBinding
    {
        /// <summary>
        /// [TASK-KBO-176] TASK-168~176에서 추가/변경된 UI를 씬에 한 번에 반영하는 통합 메뉴(전부 idempotent - 몇 번 실행해도 안전).
        ///   1) SetupTemplates.SetupDualPortraitAndGradeFrameLayers - 카드 4단 레이어(TASK-168), 텍스트 Outline+Shadow
        ///      가독성(TASK-170, TASK-176 버그 수정), 카드 "SD n" 세트덱 스코어 표기(TASK-173/176).
        ///   2) SetupRosterUI.AutoConnectRosterUI - 주전/후보 구역 분리 + 카드 클릭 교체 팝업 + 세트덱 선택형 버프 패널(TASK-176).
        ///   3) AutoBindAllMissingReferences - 기존 핫픽스 재바인딩.
        /// 치어리더 시너지 표시(TASK-175)는 기존 텍스트(TeamSynergyUIController.cheerleaderText, CheerleaderSlotUI.gradeText)에
        /// 런타임 문구만 바뀐 것이라 별도 씬 조립이 필요 없다. 실행 후 반드시 씬을 저장(Ctrl+S)해야 디스크에 반영된다.
        /// </summary>
        [MenuItem("KBO Manager/Setup/Apply Latest UI (TASK-168~176)")]
        public static void ApplyLatestUI()
        {
            SetupTemplates.SetupDualPortraitAndGradeFrameLayers();
            SetupRosterUI.AutoConnectRosterUI();
            AutoBindAllMissingReferences();
            Debug.Log("[SetupMasterBinding] TASK-168~176 최신 UI 적용 완료 - 씬을 저장(Ctrl+S)하십시오.");
        }

        /// <summary>[TASK-KBO-176] 배치 실행용(`Unity.exe -batchmode -projectPath . -executeMethod
        /// KBOManager.EditorTools.SetupMasterBinding.RunBatchApplyLatestUI -quit`) - 씬을 열어 ApplyLatestUI() 후 저장한다.</summary>
        public static void RunBatchApplyLatestUI()
        {
            const string scenePath = "Assets/Scenes/SampleScene.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            ApplyLatestUI();
            bool saved = EditorSceneManager.SaveScene(scene);
            Debug.Log(saved
                ? "[SetupMasterBinding] RunBatchApplyLatestUI 완료 - 씬 저장 성공."
                : "[SetupMasterBinding] RunBatchApplyLatestUI 완료했으나 씬 저장 실패.");
        }

        [MenuItem("KBO Manager/Setup/Auto-Bind All Missing References (HOTFIX)")]
        public static void AutoBindAllMissingReferences()
        {
            BindScoutUIController();
            BindLeagueDashboardUIController();
            BindInGameUIController();

            Debug.Log("[SetupMasterBinding] 전역 UI 컨트롤러 마스터 바인딩 완료.");
        }

        /// <summary>
        /// ScoutUIController.scoutManager 바인딩. 원문(ScoutUIController.cs 18행) 확인 결과 이 필드는
        /// SetupScoutUI.cs가 애초에 한 번도 바인딩한 적이 없었다(스카우트 UI 조립 당시 범위에 없었음) -
        /// "저장 누락"이 아니라 최초부터 누락된 배선이었다.
        /// </summary>
        private static void BindScoutUIController()
        {
            try
            {
                var controller = UnityEngine.Object.FindAnyObjectByType<ScoutUIController>(FindObjectsInactive.Include);
                if (controller == null)
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 ScoutUIController를 찾지 못해 건너뜁니다.");
                    return;
                }

                var scoutManager = UnityEngine.Object.FindAnyObjectByType<ScoutManager>(FindObjectsInactive.Include);
                if (scoutManager == null)
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 ScoutManager를 찾지 못해 ScoutUIController.scoutManager 바인딩을 건너뜁니다.");
                    return;
                }

                var serialized = new SerializedObject(controller);
                serialized.FindProperty("scoutManager").objectReferenceValue = scoutManager;
                serialized.ApplyModifiedProperties();

                MarkDirty(controller);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SetupMasterBinding] ScoutUIController 바인딩 중 예외 발생: {e}");
            }
        }

        /// <summary>
        /// LeagueDashboardUIController의 leagueManager + 4개 텍스트(seasonProgressText/nextMatchupText/
        /// teamOVRText/standingsRowTexts) 필드를 씬에 이미 존재하는 오브젝트에서 이름 기준으로 다시
        /// 찾아 강제 재바인딩한다(SetupLeagueUI.cs와 달리 새 오브젝트를 생성하지 않는 순수 재탐색).
        /// </summary>
        private static void BindLeagueDashboardUIController()
        {
            try
            {
                var dashboard = UnityEngine.Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
                if (dashboard == null)
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 LeagueDashboardUIController를 찾지 못해 건너뜁니다.");
                    return;
                }

                var serialized = new SerializedObject(dashboard);

                var leagueManager = UnityEngine.Object.FindAnyObjectByType<LeagueManager>(FindObjectsInactive.Include);
                if (leagueManager != null)
                {
                    serialized.FindProperty("leagueManager").objectReferenceValue = leagueManager;
                }
                else
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 LeagueManager를 찾지 못해 leagueManager 바인딩을 건너뜁니다.");
                }

                var allTexts = dashboard.GetComponentsInChildren<Text>(true);

                BindNamedText(serialized, "seasonProgressText", allTexts, "SeasonProgressText");
                BindNamedText(serialized, "nextMatchupText", allTexts, "NextMatchupText");
                BindNamedText(serialized, "teamOVRText", allTexts, "TeamOVRText");

                var standingsRows = allTexts
                    .Where(t => t.name.StartsWith("StandingsRow", StringComparison.Ordinal))
                    .OrderBy(t => ExtractTrailingNumber(t.name))
                    .ToArray();

                if (standingsRows.Length > 0)
                {
                    var standingsProperty = serialized.FindProperty("standingsRowTexts");
                    standingsProperty.arraySize = standingsRows.Length;
                    for (int i = 0; i < standingsRows.Length; i++)
                    {
                        standingsProperty.GetArrayElementAtIndex(i).objectReferenceValue = standingsRows[i];
                    }
                }
                else
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 'StandingsRow##' 이름의 Text를 하나도 찾지 못해 standingsRowTexts 바인딩을 건너뜁니다.");
                }

                serialized.ApplyModifiedProperties();
                MarkDirty(dashboard);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SetupMasterBinding] LeagueDashboardUIController 바인딩 중 예외 발생: {e}");
            }
        }

        /// <summary>
        /// InGameUIController.cs 원문(28~62행) 확인 결과 이 컨트롤러에는 LeagueManager 참조 필드가
        /// 존재하지 않는다(경기 시뮬레이션 로직을 전혀 갖지 않는 순수 UI 브릿지 - 클래스 주석 11~13행
        /// 참고). 대신 이 컨트롤러가 실제로 갖는 매니저성 참조 2개(playBallController/
        /// matchRewardManager)만, 비어 있을 때만 재탐색해 채운다(이미 채워진 값은 덮어쓰지 않는다 -
        /// 씬에 동일 타입 오브젝트가 여러 개일 경우 의도치 않게 다른 참조로 바뀌는 것을 방지).
        /// </summary>
        private static void BindInGameUIController()
        {
            try
            {
                var controller = UnityEngine.Object.FindAnyObjectByType<InGameUIController>(FindObjectsInactive.Include);
                if (controller == null)
                {
                    Debug.LogWarning("[SetupMasterBinding] 씬에서 InGameUIController를 찾지 못해 건너뜁니다.");
                    return;
                }

                var serialized = new SerializedObject(controller);

                var playBallProperty = serialized.FindProperty("playBallController");
                if (playBallProperty.objectReferenceValue == null)
                {
                    var playBallController = UnityEngine.Object.FindAnyObjectByType<PlayBallController>(FindObjectsInactive.Include);
                    if (playBallController != null)
                    {
                        playBallProperty.objectReferenceValue = playBallController;
                    }
                    else
                    {
                        Debug.LogWarning("[SetupMasterBinding] 씬에서 PlayBallController를 찾지 못해 InGameUIController.playBallController 바인딩을 건너뜁니다.");
                    }
                }

                var matchRewardProperty = serialized.FindProperty("matchRewardManager");
                if (matchRewardProperty.objectReferenceValue == null)
                {
                    var matchRewardManager = UnityEngine.Object.FindAnyObjectByType<MatchRewardManager>(FindObjectsInactive.Include);
                    if (matchRewardManager != null)
                    {
                        matchRewardProperty.objectReferenceValue = matchRewardManager;
                    }
                    else
                    {
                        Debug.LogWarning("[SetupMasterBinding] 씬에서 MatchRewardManager를 찾지 못해 InGameUIController.matchRewardManager 바인딩을 건너뜁니다.");
                    }
                }

                serialized.ApplyModifiedProperties();
                MarkDirty(controller);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SetupMasterBinding] InGameUIController 바인딩 중 예외 발생: {e}");
            }
        }

        private static void BindNamedText(SerializedObject serialized, string propertyName, Text[] pool, string objectName)
        {
            var match = pool.FirstOrDefault(t => t.name == objectName);
            if (match != null)
            {
                serialized.FindProperty(propertyName).objectReferenceValue = match;
            }
            else
            {
                Debug.LogWarning($"[SetupMasterBinding] '{objectName}' 이름의 Text를 찾지 못해 {propertyName} 바인딩을 건너뜁니다.");
            }
        }

        private static int ExtractTrailingNumber(string name)
        {
            var digits = new string(name.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var value) ? value : int.MaxValue;
        }

        private static void MarkDirty(Component component)
        {
            EditorUtility.SetDirty(component);

            var scene = component.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
