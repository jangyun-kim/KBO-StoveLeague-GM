using KBOManager.Controllers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-130] 로비 상단에 GDD "재화 &gt; 기타 소모 재화" 절이 명시한 볼(GameGold)/유니폼/
    /// 플레이 티켓 3종을 횡렬로 표시하는 텍스트 UI 그룹을 조립·배선한다(사용자 제공 상단 UI
    /// 레퍼런스 - 아이콘 영역 포함 가로 배치).
    ///
    /// [사실 정정] 명령서는 이 작업을 `SetupLobbyPolishingUI.cs`에 하라고 지시했으나, 그 파일은
    /// TASK-KBO-129에서 완전히 삭제되었다(구 scoutTicketLobbyText/cheerStickLobbyText 전용 스크립트였고
    /// LeagueDashboardUIController.cs에서 그 필드 자체를 제거해 더 이상 유효한 대상이 없었음, DCL-101).
    /// 대신 이 신규 파일에 동일한 관례(Find-or-Create, 여러 번 실행해도 안전)로 구현한다.
    ///
    /// [실제 버그 확인, 명령서 4항] 씬을 직접 조회한 결과 "PremiumCurrencyText"라는 이름의 오브젝트가
    /// `LobbyPanel`/`ShopPanel`/`CheerleaderShopPanel` 3곳에 각각 남아 있었다 - TASK-KBO-072 당시
    /// 최초로 만들어진 이름인데, 이후 TASK-KBO-126(→ScoutTicketText/CheerStickText)/
    /// TASK-KBO-128/129의 모든 DestroyImmediate 정리 로직이 매번 "직전 세대"의 이름만 지목해(예:
    /// TASK-129는 "CheerStickText"만 찾았지 그 이전 세대인 "PremiumCurrencyText"는 찾지 않음) 이
    /// 가장 오래된 조상 오브젝트만 유일하게 모든 정리망을 피해 3곳 모두에 그대로 남아 있었다 - 로비
    /// 화면에서 보고된 "재화: 0" 텍스트 겹침의 실제 원인이다. 3곳 전부에서 DestroyImmediate로
    /// 제거한다.
    /// </summary>
    public static class SetupLobbyCurrencyUI
    {
        private const string LegacyPremiumCurrencyTextName = "PremiumCurrencyText";
        private const string GroupName = "TopCurrencyGroup";
        private const string BallTextName = "BallText";
        private const string UniformTextName = "UniformText";
        private const string TicketTextName = "TicketText";

        [MenuItem("KBO Manager/Setup/Auto-Connect Lobby Currency UI")]
        public static void AutoConnectLobbyCurrencyUI()
        {
            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupLobbyCurrencyUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "재화 UI 생성 및 정리를 건너뜁니다.");
                return;
            }

            // [명령서 4항] 로비의 구형 "PremiumCurrencyText" 잔재를 제거한다. ShopPanel/
            // CheerleaderShopPanel의 동명 잔재는 각 화면을 소유한 SetupMiscUI.cs/SetupShopUI.cs가
            // 이미 함께 정리한다(아래 두 호출).
            DestroyLegacyChild(dashboard.transform, LegacyPremiumCurrencyTextName);

            var group = FindOrCreateGroup(dashboard.transform);
            var ballText = FindOrCreateCurrencyText(group, BallTextName, "볼: 0");
            var uniformText = FindOrCreateCurrencyText(group, UniformTextName, "유니폼: 0");
            var ticketText = FindOrCreateCurrencyText(group, TicketTextName, "티켓: 0");

            BindCurrencyTextFields(dashboard, ballText, uniformText, ticketText);

            // [명령서 4항 확장] 다른 두 화면의 동명 잔재도 이 메뉴 한 번으로 함께 정리한다 - QA가
            // 세 화면을 일일이 찾아다니지 않아도 되게 한다(명령서 6항 사용자 편의).
            SetupMiscUI.DestroyLegacyPremiumCurrencyText();
            SetupShopUI.DestroyLegacyPremiumCurrencyText();

            EditorUtility.SetDirty(dashboard);
            EditorUtility.SetDirty(ballText);
            EditorUtility.SetDirty(uniformText);
            EditorUtility.SetDirty(ticketText);
            EditorSceneManager.MarkSceneDirty(dashboard.gameObject.scene);

            Debug.Log("[SetupLobbyCurrencyUI] 로비 상단 볼/유니폼/티켓 재화 UI 자동 생성/바인딩 및 구형 " +
                "PremiumCurrencyText 정리 완료(로비/상점/치어리더 영입 3곳).");
        }

        private static void DestroyLegacyChild(Transform parent, string name)
        {
            var legacy = parent.Find(name);
            if (legacy != null) Object.DestroyImmediate(legacy.gameObject);
        }

        /// <summary>3개 텍스트를 가로로 나란히 배치할 컨테이너. 로비 우측 상단에 고정한다.</summary>
        private static Transform FindOrCreateGroup(Transform parent)
        {
            var existing = parent.Find(GroupName);
            GameObject groupObject;
            if (existing != null)
            {
                groupObject = existing.gameObject;
            }
            else
            {
                groupObject = new GameObject(GroupName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(groupObject, $"Create {GroupName}");
                groupObject.transform.SetParent(parent, false);

                var rect = (RectTransform)groupObject.transform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(660f, 60f);
                rect.anchoredPosition = new Vector2(-20f, -20f);
            }

            if (!groupObject.TryGetComponent<HorizontalLayoutGroup>(out var layout))
            {
                layout = groupObject.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 12f;
                layout.childAlignment = TextAnchor.MiddleRight;
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = true;
            }

            return groupObject.transform;
        }

        /// <summary>
        /// [사용자 레퍼런스 - 아이콘 영역 포함] 텍스트 왼쪽에 작은 아이콘 자리를 함께 만든다. 실제
        /// 스프라이트 에셋은 이 작업 범위 밖(아트 에셋 미보유)이라 단색 사각형 placeholder로
        /// 자리만 확보한다 - 추후 아이콘 에셋이 준비되면 이 Image의 sprite만 교체하면 된다.
        /// </summary>
        private static Text FindOrCreateCurrencyText(Transform parent, string name, string defaultText)
        {
            var existing = parent.Find(name);
            if (existing != null)
            {
                var existingText = existing.GetComponentInChildren<Text>();
                if (existingText != null) return existingText;
            }

            var slotObject = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            Undo.RegisterCreatedObjectUndo(slotObject, $"Create {name}");
            slotObject.transform.SetParent(parent, false);

            var slotLayout = slotObject.GetComponent<HorizontalLayoutGroup>();
            slotLayout.spacing = 6f;
            slotLayout.childAlignment = TextAnchor.MiddleLeft;
            slotLayout.childControlWidth = false;
            slotLayout.childForceExpandWidth = false;
            slotLayout.childControlHeight = true;
            slotLayout.childForceExpandHeight = true;

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(iconObject, $"Create {name} Icon");
            iconObject.transform.SetParent(slotObject.transform, false);
            iconObject.GetComponent<Image>().color = new Color(0.85f, 0.7f, 0.2f);
            var iconLayout = iconObject.GetComponent<LayoutElement>();
            iconLayout.preferredWidth = 36f;
            iconLayout.preferredHeight = 36f;

            var textObject = new GameObject("Value", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name} Value");
            textObject.transform.SetParent(slotObject.transform, false);

            var textLayout = textObject.GetComponent<LayoutElement>();
            textLayout.preferredWidth = 140f;

            var text = textObject.GetComponent<Text>();
            text.text = defaultText;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.fontSize = 28;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }

        private static void BindCurrencyTextFields(LeagueDashboardUIController dashboard, Text ballText,
            Text uniformText, Text ticketText)
        {
            var serialized = new SerializedObject(dashboard);
            serialized.FindProperty("ballText").objectReferenceValue = ballText;
            serialized.FindProperty("uniformText").objectReferenceValue = uniformText;
            serialized.FindProperty("ticketText").objectReferenceValue = ticketText;
            serialized.ApplyModifiedProperties();
        }
    }
}
