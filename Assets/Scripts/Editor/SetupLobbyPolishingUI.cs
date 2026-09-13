using KBOManager.Controllers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-072] TASK-KBO-071에서 코드로만 신설된 LeagueDashboardUIController.
    /// premiumCurrencyLobbyText 필드를 QA가 메뉴 클릭 한 번으로 씬에 조립·배선할 수 있게 하는 에디터
    /// 자동화. SetupRoutingUI/SetupShopUI와 동일한 관례로 여러 번 실행해도 안전하다(이미 있으면 찾아
    /// 재사용/재바인딩).
    /// </summary>
    public static class SetupLobbyPolishingUI
    {
        private const string PremiumCurrencyTextName = "PremiumCurrencyText";

        [MenuItem("KBO Manager/Setup/Auto-Connect Lobby Currency UI")]
        public static void AutoConnectLobbyCurrencyUI()
        {
            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupLobbyPolishingUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "재화 텍스트 생성 및 바인딩을 건너뜁니다.");
                return;
            }

            var currencyText = FindOrCreateCurrencyText(dashboard.transform);
            BindCurrencyTextField(dashboard, currencyText);

            EditorUtility.SetDirty(dashboard);
            EditorUtility.SetDirty(currencyText);
            EditorSceneManager.MarkSceneDirty(dashboard.gameObject.scene);

            Debug.Log("[SetupLobbyPolishingUI] 로비 재화 텍스트 자동 생성/바인딩 완료.");
        }

        /// <summary>이름으로 기존 텍스트를 재사용하거나, 없으면 로비 패널 우측 상단에 새로 만든다.
        /// 앵커/크기는 명령서 6항 지시대로 대략적인 눈에 띄는 배치만 적용한다(정밀 디자인 아님).</summary>
        private static Text FindOrCreateCurrencyText(Transform parent)
        {
            var existingChild = parent.Find(PremiumCurrencyTextName);
            if (existingChild != null)
            {
                var existingText = existingChild.GetComponent<Text>();
                if (existingText != null) return existingText;
            }

            var textObject = new GameObject(PremiumCurrencyTextName, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {PremiumCurrencyTextName}");
            textObject.transform.SetParent(parent, false);

            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(360f, 70f);
            rect.anchoredPosition = new Vector2(-20f, -20f);

            var text = textObject.GetComponent<Text>();
            text.text = "재화: 0";
            text.alignment = TextAnchor.MiddleRight;
            text.color = Color.white;
            text.fontSize = 40;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return text;
        }

        private static void BindCurrencyTextField(LeagueDashboardUIController dashboard, Text currencyText)
        {
            var serializedDashboard = new SerializedObject(dashboard);
            serializedDashboard.FindProperty("premiumCurrencyLobbyText").objectReferenceValue = currencyText;
            serializedDashboard.ApplyModifiedProperties();
        }
    }
}
