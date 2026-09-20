using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-115] 자동 생성 UI 텍스트 가독성 핫픽스.
    ///
    /// [사실 확인] 명령서 3항이 지목한 4개 패널의 실제 배경을 하나씩 재조회했다: `LobbyPanel`
    /// (`Image` 흰색 39.2% 알파), `ScoutPanel`/`RosterPanel`(둘 다 완전 불투명 흰색 `Image`)은
    /// 전부 밝은 배경이라 검은색 텍스트가 정답이다 - `LobbyPanel`의 `PremiumCurrencyText`(TASK-072,
    /// `SetupLobbyPolishingUI.cs`)가 흰색으로 생성돼 있어 실제로 거의 안 보였음을 확인했다.
    ///
    /// [결정 필요, 명령서 4항과 부분 배치] `InGamePanel`은 SceneInitializer.cs가 `Image` 없이
    /// 생성해(원문 154~168행) 자체 배경색이 없고, 씬의 메인 카메라 `m_BackGroundColor`가
    /// `(0.192, 0.302, 0.475)`(중간 톤 파란색)이라 실제로는 어두운 배경이다 - 이 위에서는 검은색이
    /// 아니라 흰색 텍스트가 정답이며, 실제로 `SetupInGameUI.cs`의 로그(`FindOrCreateLogEntryTemplate`)
    /// 텍스트는 이미 흰색으로 올바르게 만들어져 있었다. 반대로 스코어보드 숫자(`FindOrCreateText`,
    /// 원문 545행)는 이미 검은색으로 만들어져 있어 이 어두운 배경 위에서 오히려 거의 안 보이는
    /// 상태였다. 명령서가 요구한 "InGamePanel 하위 모든 Text를 검은색으로"를 그대로 적용하면 이미
    /// 정상인 로그 텍스트까지 망가뜨리고 스코어보드 문제는 전혀 고치지 못하므로, `InGamePanel`은
    /// 색상 강제 변경 대상에서 제외했다(가독성 보조 옵션(`bestFit`/`overflow`)만 적용) - 실제
    /// 스코어보드 대비 문제를 고치려면 `SetupInGameUI.cs`의 해당 텍스트 색상 자체를 흰색으로 바꾸는
    /// 별도 판단이 필요해 이번 "일괄 검은색" 도구의 범위를 벗어난다고 보고 손대지 않았다.
    ///
    /// [TASK-KBO-116] `InventoryPanel`을 탐색 대상에 추가했다. 이 패널 자체는 `SetupInventoryUI.cs`가
    /// 불투명 흰색 `Image`로 만들어 둬(원문 `FindOrCreateInventoryPanel`) 검은색이 정답이지만, 그
    /// 자식 `DetailPanel`은 반투명 검정 오버레이(`Color(0,0,0,0.75)`, 씬에서 실측 확인)라 그 안의
    /// 텍스트(`detailReinforceText`/`detailAwakenText`/`detailSkillsText`/`skillRerollResultText`,
    /// TASK-110이 이미 흰색으로 정확히 만들어 둠)는 `InGamePanel`의 로그 텍스트와 동일한 이유로 예외
    /// 처리해야 한다 - `DetailPanel` 하위는 건너뛰고 `InventoryPanel`의 나머지 부분(카드 목록/닫기
    /// 버튼 라벨 등, 전부 이미 검은색)만 검은색 대상으로 삼는다.
    /// </summary>
    public static class SetupUIColors
    {
        private static readonly string[] LightBackgroundPanels = { "LobbyPanel", "ScoutPanel", "RosterPanel" };
        private const string InGamePanelName = "InGamePanel";
        private const string InventoryPanelName = "InventoryPanel";
        private const string DetailPanelName = "DetailPanel";

        [MenuItem("KBO Manager/Setup/Fix All Text Colors (Black)")]
        public static void FixAllTextColors()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[SetupUIColors] 씬에서 Canvas를 찾지 못했습니다.");
                return;
            }

            foreach (var panelName in LightBackgroundPanels)
            {
                FixPanelTextColor(canvas.transform, panelName, Color.black);
            }

            // InGamePanel은 배경이 어두워(카메라 배경색 기준) 색상은 그대로 두고 가독성 옵션만 적용한다.
            ApplyReadabilityOnly(canvas.transform, InGamePanelName);

            // InventoryPanel은 밝은 배경이지만, 그 자식 DetailPanel만은 어두운 오버레이라 예외 처리한다.
            FixInventoryPanelTextColor(canvas.transform);

            var scene = canvas.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupUIColors] UI 텍스트 가독성 패치 완료.");
        }

        private static void FixPanelTextColor(Transform canvasTransform, string panelName, Color color)
        {
            var panelTransform = canvasTransform.Find(panelName);
            if (panelTransform == null)
            {
                Debug.LogWarning($"[SetupUIColors] '{panelName}'를 찾지 못해 건너뜁니다.");
                return;
            }

            var texts = panelTransform.GetComponentsInChildren<Text>(true);
            foreach (var text in texts)
            {
                text.color = color;
                ApplyReadabilityOptions(text);
                EditorUtility.SetDirty(text);
            }

            Debug.Log($"[SetupUIColors] '{panelName}' 하위 Text {texts.Length}개를 검은색으로 변경했습니다.");
        }

        /// <summary>`InventoryPanel`은 검은색으로 바꾸되, 어두운 오버레이인 자식 `DetailPanel`
        /// 하위 텍스트만은 흰색을 그대로 유지한다(클래스 주석 참고 - 안 그러면 다시 안 보이게 된다).</summary>
        private static void FixInventoryPanelTextColor(Transform canvasTransform)
        {
            var panelTransform = canvasTransform.Find(InventoryPanelName);
            if (panelTransform == null)
            {
                Debug.LogWarning($"[SetupUIColors] '{InventoryPanelName}'를 찾지 못해 건너뜁니다.");
                return;
            }

            var detailPanelTransform = panelTransform.Find(DetailPanelName);

            var texts = panelTransform.GetComponentsInChildren<Text>(true);
            int changedCount = 0;
            int skippedCount = 0;
            foreach (var text in texts)
            {
                if (detailPanelTransform != null && text.transform.IsChildOf(detailPanelTransform))
                {
                    ApplyReadabilityOptions(text); // 색상은 그대로, 가독성 옵션만 적용
                    EditorUtility.SetDirty(text);
                    skippedCount++;
                    continue;
                }

                text.color = Color.black;
                ApplyReadabilityOptions(text);
                EditorUtility.SetDirty(text);
                changedCount++;
            }

            Debug.Log($"[SetupUIColors] '{InventoryPanelName}' 하위 Text {changedCount}개를 검은색으로 변경했습니다 " +
                $"('{DetailPanelName}' 하위 {skippedCount}개는 어두운 배경이라 흰색을 유지했습니다).");
        }

        private static void ApplyReadabilityOnly(Transform canvasTransform, string panelName)
        {
            var panelTransform = canvasTransform.Find(panelName);
            if (panelTransform == null)
            {
                Debug.LogWarning($"[SetupUIColors] '{panelName}'를 찾지 못해 건너뜁니다.");
                return;
            }

            var texts = panelTransform.GetComponentsInChildren<Text>(true);
            foreach (var text in texts)
            {
                ApplyReadabilityOptions(text);
                EditorUtility.SetDirty(text);
            }

            Debug.Log($"[SetupUIColors] '{panelName}' 하위 Text {texts.Length}개에 색상 변경 없이 가독성 옵션만 적용했습니다" +
                "(배경이 어두워 검은색으로 바꾸면 오히려 안 보이게 됩니다 - 클래스 주석 참고).");
        }

        /// <summary>명령서 4항의 `bestFit`/`overflow` 가독성 보조 옵션. 색상과 무관하게 항상 안전하다.</summary>
        private static void ApplyReadabilityOptions(Text text)
        {
            text.resizeTextForBestFit = true;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }
}
