using KBOManager.Controllers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-113] v0.3 잔여 화면(상점/리그 기록실) 뼈대 조립 및 닫기 버튼 배선.
    ///
    /// [사실 확인] `ShopUIController`/`LeagueStatsUIController` 둘 다 씬에 이미 부착돼 있었다
    /// (`ShopPanel`/`StatsPanel`, `FindAnyObjectByType` 확인) - 다만 두 컨트롤러 전체 필드가 전부
    /// `{fileID: 0}`(None)이었다(명령서 3항 서술과 일치). 두 컨트롤러 원문 모두에 `closeButton` 필드가
    /// 없어(명령서 4항이 예외적으로 허가한 대로) `ShopUIController.cs`/`LeagueStatsUIController.cs`에
    /// `closeButton` 필드 1개 + `Awake()` 리스너 1줄만 각각 최소 추가했다(TASK-111과 동일 관례).
    /// </summary>
    public static class SetupMiscUI
    {
        private const string ShopPanelName = "ShopPanel";
        private const string StatsPanelName = "StatsPanel";
        private const string CloseButtonName = "CloseButton";
        private const string TemplatesHolderName = "_Templates";
        private const string PlayerCardTemplateName = "PlayerCardTemplate";
        private const string HallOfFameEntryTemplateName = "HallOfFameEntryTemplate";

        [MenuItem("KBO Manager/Setup/Auto-Connect Misc UI (Shop & Stats)")]
        public static void AutoConnectMiscUI()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[SetupMiscUI] 씬에서 Canvas를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Initialize Current Scene'을 실행하십시오.");
                return;
            }

            BindShopPanel(canvas.transform);
            BindStatsPanel(canvas.transform);

            Debug.Log("[SetupMiscUI] 상점/리그 기록실 UI 자동 배선 완료.");
        }

        // ================= 상점 =================

        private static void BindShopPanel(Transform canvasTransform)
        {
            var controller = FindOrCreatePanelController<ShopUIController>(canvasTransform, ShopPanelName);

            // [TASK-KBO-129] ShopUIController.cs에서 "특수 확정 패키지"/"프리미엄 팩(10연뽑)"을 완전히
            // 삭제했다(GDD에 없는 양산형 가챠 관습) - 씬에 남아있던 대응 오브젝트도 여기서 함께
            // 제거한다(명령서 4항 "엉뚱한 캔버스 패널이나 버튼들을... 완전히 삭제"). ScoutTicketText도
            // 구 ScoutTicket 필드 폐기(TASK-129)로 더 이상 쓰이지 않아 함께 제거한다.
            DestroyLegacyChild(controller.transform, "BuyGuaranteedPackageButton");
            DestroyLegacyChild(controller.transform, "BuyPremiumTenPullButton");
            DestroyLegacyChild(controller.transform, "ScoutTicketText");

            var closeButton = FindOrCreateButton(controller.transform, CloseButtonName, "닫기",
                new Vector2(0.85f, 0.92f), new Vector2(1f, 1f));

            var buySkillTicketButton = FindOrCreateButton(controller.transform, "BuySkillTicketButton",
                "스킬 변경권 구매", new Vector2(0.05f, 0.2f), new Vector2(0.45f, 0.35f));

            var gameGoldText = FindOrCreateText(controller.transform, "GameGoldText", "",
                new Vector2(0.5f, 0.68f), new Vector2(1f, 0.78f));
            var purchaseResultText = FindOrCreateText(controller.transform, "PurchaseResultText", "",
                new Vector2(0.05f, 0.03f), new Vector2(0.95f, 0.15f));

            var itemDatabase = Object.FindAnyObjectByType<ItemDatabase>(FindObjectsInactive.Include);
            if (itemDatabase == null)
            {
                Debug.LogWarning("[SetupMiscUI] 씬에서 ItemDatabase를 찾지 못해 itemDatabase 바인딩을 건너뜁니다.");
            }

            var serialized = new SerializedObject(controller);
            if (itemDatabase != null) serialized.FindProperty("itemDatabase").objectReferenceValue = itemDatabase;
            serialized.FindProperty("buySkillTicketButton").objectReferenceValue = buySkillTicketButton;
            serialized.FindProperty("gameGoldText").objectReferenceValue = gameGoldText;
            serialized.FindProperty("purchaseResultText").objectReferenceValue = purchaseResultText;
            serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
            serialized.ApplyModifiedProperties();

            EditorUtility.SetDirty(controller);
            MarkSceneDirty(controller);
        }

        /// <summary>[TASK-KBO-129] 이름으로 자식을 찾아 존재하면 DestroyImmediate로 완전히 제거한다.
        /// GDD에 없는 구 UI 요소를 정리할 때만 쓴다 - 일반적인 Find-or-Create 재사용 경로에는 쓰지
        /// 않는다.</summary>
        private static void DestroyLegacyChild(Transform parent, string name)
        {
            var legacy = parent.Find(name);
            if (legacy != null) Object.DestroyImmediate(legacy.gameObject);
        }

        // ================= 리그 기록실 =================

        private static void BindStatsPanel(Transform canvasTransform)
        {
            var controller = FindOrCreatePanelController<LeagueStatsUIController>(canvasTransform, StatsPanelName);
            var cardTemplate = FindPlayerCardTemplate(canvasTransform);

            var closeButton = FindOrCreateButton(controller.transform, CloseButtonName, "닫기",
                new Vector2(0.85f, 0.92f), new Vector2(1f, 1f));

            var batterTabButton = FindOrCreateButton(controller.transform, "BatterTabButton", "타자 순위",
                new Vector2(0f, 0.9f), new Vector2(0.33f, 1f));
            var pitcherTabButton = FindOrCreateButton(controller.transform, "PitcherTabButton", "투수 순위",
                new Vector2(0.33f, 0.9f), new Vector2(0.66f, 1f));
            var hallOfFameTabButton = FindOrCreateButton(controller.transform, "HallOfFameTabButton", "명예의 전당",
                new Vector2(0.66f, 0.9f), new Vector2(0.85f, 1f));

            var batterPanelRoot = FindOrCreateChild(controller.transform, "BatterPanelRoot", Vector2.zero, new Vector2(1f, 0.9f));
            var pitcherPanelRoot = FindOrCreateChild(controller.transform, "PitcherPanelRoot", Vector2.zero, new Vector2(1f, 0.9f));
            var hallOfFamePanelRoot = FindOrCreateChild(controller.transform, "HallOfFamePanelRoot", Vector2.zero, new Vector2(1f, 0.9f));

            var battingAveragePanel = CreateStatPanelEntry(batterPanelRoot, "BattingAverage", cardTemplate);
            var homeRunPanel = CreateStatPanelEntry(batterPanelRoot, "HomeRun", cardTemplate);
            var battingPlaceholderText1 = FindOrCreateStackedText(batterPanelRoot, "BattingPlaceholderText1");
            var battingPlaceholderText2 = FindOrCreateStackedText(batterPanelRoot, "BattingPlaceholderText2");

            var winsPanel = CreateStatPanelEntry(pitcherPanelRoot, "Wins", cardTemplate);
            var eraPanel = CreateStatPanelEntry(pitcherPanelRoot, "Era", cardTemplate);
            var pitchingPlaceholderText1 = FindOrCreateStackedText(pitcherPanelRoot, "PitchingPlaceholderText1");
            var pitchingPlaceholderText2 = FindOrCreateStackedText(pitcherPanelRoot, "PitchingPlaceholderText2");

            var hallOfFameListContainer = FindOrCreateVerticalContainer(hallOfFamePanelRoot, "HallOfFameListContainer");
            var hallOfFameEntryPrefab = FindOrCreateHallOfFameEntryTemplate(canvasTransform);

            var seasonStatManager = Object.FindAnyObjectByType<SeasonStatManager>(FindObjectsInactive.Include);
            if (seasonStatManager == null)
            {
                Debug.LogWarning("[SetupMiscUI] 씬에서 SeasonStatManager를 찾지 못해 seasonStatManager 바인딩을 건너뜁니다.");
            }

            var leagueManager = Object.FindAnyObjectByType<LeagueManager>(FindObjectsInactive.Include);
            if (leagueManager == null)
            {
                Debug.LogWarning("[SetupMiscUI] 씬에서 LeagueManager를 찾지 못해 leagueManager 바인딩을 건너뜁니다.");
            }

            var seasonRollover = Object.FindAnyObjectByType<SeasonRollover>(FindObjectsInactive.Include);
            if (seasonRollover == null)
            {
                Debug.LogWarning("[SetupMiscUI] 씬에서 SeasonRollover를 찾지 못해 seasonRollover 바인딩을 건너뜁니다.");
            }

            var serialized = new SerializedObject(controller);
            if (seasonStatManager != null) serialized.FindProperty("seasonStatManager").objectReferenceValue = seasonStatManager;
            if (leagueManager != null) serialized.FindProperty("leagueManager").objectReferenceValue = leagueManager;

            serialized.FindProperty("batterTabButton").objectReferenceValue = batterTabButton;
            serialized.FindProperty("pitcherTabButton").objectReferenceValue = pitcherTabButton;
            serialized.FindProperty("hallOfFameTabButton").objectReferenceValue = hallOfFameTabButton;
            serialized.FindProperty("batterPanelRoot").objectReferenceValue = batterPanelRoot.gameObject;
            serialized.FindProperty("pitcherPanelRoot").objectReferenceValue = pitcherPanelRoot.gameObject;
            serialized.FindProperty("hallOfFamePanelRoot").objectReferenceValue = hallOfFamePanelRoot.gameObject;

            if (seasonRollover != null) serialized.FindProperty("seasonRollover").objectReferenceValue = seasonRollover;
            serialized.FindProperty("hallOfFameListContainer").objectReferenceValue = hallOfFameListContainer;
            serialized.FindProperty("hallOfFameEntryPrefab").objectReferenceValue = hallOfFameEntryPrefab;

            BindStatPanelEntry(serialized.FindProperty("battingAveragePanel"), battingAveragePanel);
            BindStatPanelEntry(serialized.FindProperty("homeRunPanel"), homeRunPanel);
            serialized.FindProperty("battingPlaceholderText1").objectReferenceValue = battingPlaceholderText1;
            serialized.FindProperty("battingPlaceholderText2").objectReferenceValue = battingPlaceholderText2;

            BindStatPanelEntry(serialized.FindProperty("winsPanel"), winsPanel);
            BindStatPanelEntry(serialized.FindProperty("eraPanel"), eraPanel);
            serialized.FindProperty("pitchingPlaceholderText1").objectReferenceValue = pitchingPlaceholderText1;
            serialized.FindProperty("pitchingPlaceholderText2").objectReferenceValue = pitchingPlaceholderText2;

            serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
            serialized.ApplyModifiedProperties();

            EditorUtility.SetDirty(controller);
            MarkSceneDirty(controller);
        }

        /// <summary>StatPanelEntry(TitleText/FirstPlaceCardRoot/FirstPlaceCard/SecondPlaceText/ThirdPlaceText)
        /// 1개 분량의 UI를 조립한다. cardTemplate이 null이면(_Templates/PlayerCardTemplate 미존재) FirstPlaceCard는
        /// 만들지 않고 null로 남긴다(명령서 7항 - 안전한 스킵).</summary>
        private static (Text title, GameObject cardRoot, PlayerCardUI card, Text second, Text third) CreateStatPanelEntry(
            Transform parent, string prefix, PlayerCardUI cardTemplate)
        {
            var container = FindOrCreateChild(parent, $"{prefix}Panel", Vector2.zero, Vector2.one);
            if (!container.TryGetComponent<VerticalLayoutGroup>(out _))
            {
                var layout = container.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childForceExpandHeight = false;
                layout.childControlHeight = false;
            }

            var titleText = FindOrCreateStackedText(container, $"{prefix}TitleText");

            var cardRootName = $"{prefix}FirstPlaceCardRoot";
            var cardRoot = FindOrCreateChild(container, cardRootName, Vector2.zero, Vector2.one).gameObject;

            PlayerCardUI card = null;
            var cardName = $"{prefix}FirstPlaceCard";
            var existingCard = cardRoot.transform.Find(cardName);
            if (existingCard != null)
            {
                card = existingCard.GetComponent<PlayerCardUI>();
            }
            else if (cardTemplate != null)
            {
                card = Object.Instantiate(cardTemplate, cardRoot.transform);
                Undo.RegisterCreatedObjectUndo(card.gameObject, $"Create {cardName}");
                card.gameObject.name = cardName;
            }

            var secondText = FindOrCreateStackedText(container, $"{prefix}SecondPlaceText");
            var thirdText = FindOrCreateStackedText(container, $"{prefix}ThirdPlaceText");

            return (titleText, cardRoot, card, secondText, thirdText);
        }

        private static void BindStatPanelEntry(SerializedProperty panelProperty,
            (Text title, GameObject cardRoot, PlayerCardUI card, Text second, Text third) entry)
        {
            panelProperty.FindPropertyRelative("TitleText").objectReferenceValue = entry.title;
            panelProperty.FindPropertyRelative("FirstPlaceCardRoot").objectReferenceValue = entry.cardRoot;
            if (entry.card != null) panelProperty.FindPropertyRelative("FirstPlaceCard").objectReferenceValue = entry.card;
            panelProperty.FindPropertyRelative("SecondPlaceText").objectReferenceValue = entry.second;
            panelProperty.FindPropertyRelative("ThirdPlaceText").objectReferenceValue = entry.third;
        }

        /// <summary>[명령서 6항 - 템플릿 재사용] `_Templates/PlayerCardTemplate`을 찾기만 한다(생성하지 않음
        /// - 없으면 `null`을 반환해 `FirstPlaceCard` 바인딩만 안전하게 건너뛴다).</summary>
        private static PlayerCardUI FindPlayerCardTemplate(Transform canvasTransform)
        {
            var holderTransform = canvasTransform.Find(TemplatesHolderName);
            var existingCard = holderTransform != null ? holderTransform.Find(PlayerCardTemplateName) : null;
            if (existingCard == null)
            {
                Debug.LogWarning($"[SetupMiscUI] '_Templates/{PlayerCardTemplateName}'를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Setup/Auto-Connect Scout UI'를 실행해 카드 템플릿을 생성하십시오. " +
                    "FirstPlaceCard 바인딩을 건너뜁니다.");
                return null;
            }

            return existingCard.GetComponent<PlayerCardUI>();
        }

        private static Text FindOrCreateHallOfFameEntryTemplate(Transform canvasTransform)
        {
            var holderTransform = FindOrCreateTemplatesHolder(canvasTransform);

            var existing = holderTransform.Find(HallOfFameEntryTemplateName);
            if (existing != null && existing.TryGetComponent<Text>(out var existingText)) return existingText;

            var textObject = new GameObject(HallOfFameEntryTemplateName, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {HallOfFameEntryTemplateName}");
            textObject.transform.SetParent(holderTransform, false);

            var rect = (RectTransform)textObject.transform;
            rect.sizeDelta = new Vector2(600f, 24f);

            var text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.fontSize = 14;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }

        private static Transform FindOrCreateTemplatesHolder(Transform canvasTransform)
        {
            var holderTransform = canvasTransform.Find(TemplatesHolderName);
            if (holderTransform != null) return holderTransform;

            var holderObject = new GameObject(TemplatesHolderName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(holderObject, $"Create {TemplatesHolderName}");
            holderObject.transform.SetParent(canvasTransform, false);
            holderObject.SetActive(false);
            return holderObject.transform;
        }

        // ================= 공용 헬퍼 =================

        /// <summary>대상 타입 T의 컴포넌트를 씬 어디서든 우선 찾아 재사용한다(이름 무관, 명령서 6항 -
        /// 컨트롤러 정밀 조사). 못 찾으면 panelName으로 Find-or-Create한 패널에 새로 부착한다.</summary>
        private static T FindOrCreatePanelController<T>(Transform canvasTransform, string panelName) where T : Component
        {
            var existingController = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (existingController != null) return existingController;

            var panelTransform = canvasTransform.Find(panelName);
            GameObject panelObject;
            if (panelTransform != null)
            {
                panelObject = panelTransform.gameObject;
            }
            else
            {
                panelObject = new GameObject(panelName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(panelObject, $"Create {panelName}");
                panelObject.transform.SetParent(canvasTransform, false);

                var rect = (RectTransform)panelObject.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                panelObject.GetComponent<Image>().color = Color.white;
            }

            return Undo.AddComponent<T>(panelObject);
        }

        private static Transform FindOrCreateChild(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existing = parent.Find(name);
            if (existing != null) return existing;

            var childObject = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(childObject, $"Create {name}");
            childObject.transform.SetParent(parent, false);

            var rect = (RectTransform)childObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return childObject.transform;
        }

        private static Transform FindOrCreateVerticalContainer(Transform parent, string name)
        {
            var containerTransform = FindOrCreateChild(parent, name, Vector2.zero, Vector2.one);

            if (!containerTransform.TryGetComponent<VerticalLayoutGroup>(out _))
            {
                var layout = containerTransform.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childForceExpandHeight = false;
                layout.childControlHeight = false;
            }

            return containerTransform;
        }

        /// <summary>부모의 `VerticalLayoutGroup`이 위치를 제어하므로 앵커 없이 크기만 지정하는 Text를 만든다.</summary>
        private static Text FindOrCreateStackedText(Transform parent, string name)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null && existingChild.TryGetComponent<Text>(out var existingText)) return existingText;

            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
            textObject.transform.SetParent(parent, false);

            var rect = (RectTransform)textObject.transform;
            rect.sizeDelta = new Vector2(400f, 24f);

            var text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.fontSize = 14;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }

        private static Button FindOrCreateButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingButton = existingChild.GetComponent<Button>();
                if (existingButton != null) return existingButton;
            }

            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
            buttonObject.transform.SetParent(parent, false);

            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.9f, 0.9f, 0.9f);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(labelObject, $"Create {name} Label");
            labelObject.transform.SetParent(buttonObject.transform, false);

            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = labelObject.GetComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return button;
        }

        private static Text FindOrCreateText(Transform parent, string name, string defaultText, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null && existingChild.TryGetComponent<Text>(out var existingText)) return existingText;

            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
            textObject.transform.SetParent(parent, false);

            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<Text>();
            text.text = defaultText;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.black;
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }

        private static void MarkSceneDirty(Component component)
        {
            var scene = component.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
