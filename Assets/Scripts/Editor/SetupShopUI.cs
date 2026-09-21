using KBOManager.Controllers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-067/129] CheerleaderShopUIController(치어리더 영입 섹션)를 QA가 메뉴 클릭 한 번으로
    /// 씬에 조립·배선할 수 있게 하는 에디터 자동화. SetupLobbyUI/SetupCheerleaderUI/SetupRoutingUI와
    /// 동일한 관례로 여러 번 실행해도 안전하다(이미 있으면 찾아 재사용/덮어쓰기).
    ///
    /// [TASK-KBO-129] 이 메뉴는 이제 "치어리더 영입" 섹션(CheerleaderShopPanel) 내부 조립만 담당한다 -
    /// 로비 진입 버튼 생성/UIManager 화면 등록은 SetupScoutHubUI.AutoConnectScoutHub()로 이관했다
    /// (GDD가 "스카우트" 하나의 화면 안에 선수 영입/치어리더 영입 두 섹션을 두므로, 별도의
    /// ScreenType.CheerleaderShop 화면과 "치어리더 뽑기" 전용 로비 버튼은 더 이상 만들지 않는다).
    ///
    /// [TASK-KBO-139, 사실 정정] 명령서는 "치어리더 영입 탭"의 레이아웃 붕괴 수정 대상 파일로
    /// `SetupCheerleaderUI.cs`를 지목했으나, 전수 확인 결과 그 파일은 완전히 다른 화면("선수 관리 >
    /// 치어리더 인벤토리/보관함", `CheerleaderInventoryUIController`)을 조립한다 - 스카우트 허브의
    /// "치어리더 영입" 섹션(`CheerleaderShopPanel`)을 실제로 조립·바인딩하는 파일은 이 `SetupShopUI.cs`
    /// (`BindShopController()`)다. 레이아웃 수정은 실제 소유 파일인 이곳에 구현했다. **근본 원인**:
    /// `FindOrCreateShopPanel()`의 `VerticalLayoutGroup`이 `childControlHeight = false`로 설정돼 있어
    /// 각 자식(`LayoutElement.preferredHeight`)이 실제 크기 조정에는 전혀 반영되지 않고 "다음 자식이
    /// 배치될 Y 위치" 계산에만 쓰였다 - 정작 자식 자신의 `RectTransform` 크기는 유니티가 새
    /// `RectTransform`에 부여하는 기본값(anchorMin=anchorMax=(0,0), sizeDelta=(100,100))에 그대로
    /// 머물러, 의도한 높이(32~160)보다 훨씬 큰 100px 박스가 다음 자식의 배치 영역을 덮어써 텍스트가
    /// 심하게 겹쳐 보였다(TASK-137이 `SetupScoutUI.cs`에서 고친 것과 증상은 같지만 원인 메커니즘은
    /// 다르다 - 거긴 `GridLayoutGroup`이 `LayoutElement`를 아예 무시, 여긴 `childControlHeight=false`가
    /// 크기 반영만 막음). `childControlHeight = true`로 바꾸고 각 `LayoutElement`에 `minHeight`를
    /// `preferredHeight`와 동일하게 추가해(TASK-135 압사 방지 패턴 재사용) 해소했다.
    /// </summary>
    public static class SetupShopUI
    {
        private const string CanvasName = "Canvas";
        private const string ShopPanelName = "CheerleaderShopPanel";
        private const string CloseButtonName = "CloseButton";

        [MenuItem("KBO Manager/Setup/Auto-Create Gacha Shop UI")]
        public static void AutoCreateShopUI()
        {
            var canvas = EnsureCanvas();
            var shopController = EnsureShopPanelAssembled(canvas.transform);

            var scene = shopController.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupShopUI] 치어리더 영입 섹션(CheerleaderShopPanel) 조립 완료. 로비 진입/화면 " +
                "등록은 'KBO Manager/Setup/Auto-Connect Scout Hub'로 실행하십시오.");
        }

        /// <summary>[TASK-KBO-130] `CheerleaderShopPanel` 하위에 남아있을 수 있는 구형
        /// "PremiumCurrencyText"(TASK-KBO-072 최초 오브젝트, 이후 어떤 정리 로직도 이 이름을 지목한
        /// 적이 없어 계속 잔존해 왔다)를 제거한다. `SetupLobbyCurrencyUI.AutoConnectLobbyCurrencyUI()`가
        /// 재사용한다.</summary>
        public static void DestroyLegacyPremiumCurrencyText()
        {
            var controller = Object.FindAnyObjectByType<CheerleaderShopUIController>(FindObjectsInactive.Include);
            if (controller == null) return;

            var legacy = controller.transform.Find("PremiumCurrencyText");
            if (legacy != null) Object.DestroyImmediate(legacy.gameObject);
        }

        /// <summary>[TASK-KBO-129] CheerleaderShopPanel(치어리더 영입 섹션) 내부를 조립·배선하고
        /// 컨트롤러를 반환한다. SetupScoutHubUI가 ScoutPanel과 합치기 전에 먼저 이 메서드로 내용을
        /// 완성시킨다.</summary>
        internal static CheerleaderShopUIController EnsureShopPanelAssembled(Transform canvasTransform)
        {
            var shopController = FindOrCreateShopPanel(canvasTransform);
            BindShopController(shopController);
            EditorUtility.SetDirty(shopController);
            return shopController;
        }

        private static Canvas EnsureCanvas()
        {
            var existing = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (existing != null) return existing;

            var canvasObject = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Canvas");

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }

        /// <summary>패널을 찾거나 만든다. 화려한 그래픽 없이 넓은 흰색 배경 + VerticalLayoutGroup으로
        /// 자식(텍스트/버튼)을 위에서 아래로 단순 나열한다(명령서 5항 - 아트 에셋 적용 금지).</summary>
        private static CheerleaderShopUIController FindOrCreateShopPanel(Transform canvasTransform)
        {
            var existingChild = canvasTransform.Find(ShopPanelName);
            if (existingChild != null)
            {
                var existingController = existingChild.GetComponent<CheerleaderShopUIController>();
                if (existingController != null) return existingController;
            }

            var panelObject = new GameObject(ShopPanelName, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {ShopPanelName}");
            panelObject.transform.SetParent(canvasTransform, false);

            // 넓게(명령서 6항 2번) - 화면 중앙에 큼직한 고정 크기로 배치한다. 정밀 앵커/디자인은
            // 이번 작업 범위 밖이며, QA가 필요하면 인스펙터에서 직접 조정하면 된다.
            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(700f, 640f);
            rect.anchoredPosition = Vector2.zero;

            panelObject.GetComponent<Image>().color = Color.white;

            var layout = panelObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 24, 24);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            // [TASK-KBO-139] false였을 때 LayoutElement.preferredHeight가 자식 크기 조정에 전혀
            // 반영되지 않아(스택 위치 계산에만 쓰임), 유니티 기본 크기(100x100)로 남은 자식들이 서로
            // 겹쳐 보였다(자세한 원인은 클래스 요약 참고) - true로 바꿔 LayoutElement 값이 실제 크기에
            // 반영되도록 한다.
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            return panelObject.AddComponent<CheerleaderShopUIController>();
        }

        /// <summary>
        /// [TASK-KBO-129] GDD "뽑기(가챠) > 치어리더 영입" 절의 4개 카테고리(일반 > 라이브/한정,
        /// 픽업·프리미엄 > 아이콘/레전드) 버튼 + 4개 재화 표시 텍스트를 조립·바인딩한다. 구
        /// roll1xButton/roll10xButton/cheerStickText(단일 CheerStick 소모, 5단계 혼합 확률)는 더 이상
        /// 존재하지 않는 필드라 - GDD에 없는 레이아웃/버튼 잔재를 씬에서 완전히 삭제한다(명령서 4항
        /// DestroyImmediate 지시).
        /// </summary>
        private static void BindShopController(CheerleaderShopUIController controller)
        {
            var parent = controller.transform;

            DestroyLegacyChild(parent, "CheerStickText");
            DestroyLegacyChild(parent, "Roll1xButton");
            DestroyLegacyChild(parent, "Roll10xButton");

            var liveCheerStickText = FindOrCreateText(parent, "LiveCheerStickText", 32f, isDisplayOnly: true);
            var limitedCheerStickText = FindOrCreateText(parent, "LimitedCheerStickText", 32f, isDisplayOnly: true);
            var starCheerStickText = FindOrCreateText(parent, "StarCheerStickText", 32f, isDisplayOnly: true);
            var legendCheerStickText = FindOrCreateText(parent, "LegendCheerStickText", 32f, isDisplayOnly: true);

            var liveButton = FindOrCreateButton(parent, "LiveButton", "일반 영입 - 라이브 (라이브 응원봉)");
            var limitedButton = FindOrCreateButton(parent, "LimitedButton", "일반 영입 - 한정 (한정 응원봉)");
            var iconButton = FindOrCreateButton(parent, "IconButton", "픽업/프리미엄 영입 - 아이콘 (스타 응원봉)");
            var legendButton = FindOrCreateButton(parent, "LegendButton", "픽업/프리미엄 영입 - 레전드 (레전드 응원봉)");

            var closeButton = FindOrCreateButton(parent, CloseButtonName, "닫기");
            var resultLogText = FindOrCreateText(parent, "ResultLogText", 160f, isDisplayOnly: true);

            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("liveCheerStickText").objectReferenceValue = liveCheerStickText;
            serializedController.FindProperty("limitedCheerStickText").objectReferenceValue = limitedCheerStickText;
            serializedController.FindProperty("starCheerStickText").objectReferenceValue = starCheerStickText;
            serializedController.FindProperty("legendCheerStickText").objectReferenceValue = legendCheerStickText;
            serializedController.FindProperty("liveButton").objectReferenceValue = liveButton;
            serializedController.FindProperty("limitedButton").objectReferenceValue = limitedButton;
            serializedController.FindProperty("iconButton").objectReferenceValue = iconButton;
            serializedController.FindProperty("legendButton").objectReferenceValue = legendButton;
            serializedController.FindProperty("closeButton").objectReferenceValue = closeButton;
            serializedController.FindProperty("resultLogText").objectReferenceValue = resultLogText;
            serializedController.ApplyModifiedProperties();
        }

        /// <summary>이름으로 자식을 찾아 존재하면 DestroyImmediate로 완전히 제거한다. GDD에 없는 구
        /// UI 요소를 정리할 때만 쓴다.</summary>
        private static void DestroyLegacyChild(Transform parent, string name)
        {
            var legacy = parent.Find(name);
            if (legacy != null) Object.DestroyImmediate(legacy.gameObject);
        }

        /// <summary>
        /// 이름으로 기존 Text를 재사용하거나 새로 만든다. isDisplayOnly가 true면(순수 표시용 텍스트 -
        /// 재화 표시/ResultLogText처럼 클릭을 받을 필요가 없는 텍스트) raycastTarget을 꺼 불필요한
        /// 레이캐스트 대상에서 제외한다(명령서 9항 - 퍼포먼스 디테일).
        /// </summary>
        private static Text FindOrCreateText(Transform parent, string name, float preferredHeight, bool isDisplayOnly)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingText = existingChild.GetComponent<Text>();
                if (existingText != null) return existingText;
            }

            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
            textObject.transform.SetParent(parent, false);

            // [TASK-KBO-139] minHeight를 preferredHeight와 동일하게 맞춰(TASK-135 패턴) 부모
            // VerticalLayoutGroup이 공간 부족 시 min<->preferred 사이로 보간(Lerp)하며 짓누르는 것을
            // 방지한다.
            var textLayoutElement = textObject.GetComponent<LayoutElement>();
            textLayoutElement.preferredHeight = preferredHeight;
            textLayoutElement.minHeight = preferredHeight;

            var text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.UpperLeft;
            text.color = Color.black;
            text.fontSize = 20;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = !isDisplayOnly;

            return text;
        }

        private static Button FindOrCreateButton(Transform parent, string name, string label)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingButton = existingChild.GetComponent<Button>();
                if (existingButton != null)
                {
                    ApplyButtonLabel(existingButton, label);
                    return existingButton;
                }
            }

            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
            buttonObject.transform.SetParent(parent, false);

            // [TASK-KBO-139] minHeight를 preferredHeight와 동일하게 맞춘다(TASK-135 패턴, 위 텍스트
            // 헬퍼와 동일한 이유).
            var buttonLayoutElement = buttonObject.GetComponent<LayoutElement>();
            buttonLayoutElement.preferredHeight = 56f;
            buttonLayoutElement.minHeight = 56f;

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
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // 버튼 라벨은 어차피 부모 Button의 Image가 클릭을 받으므로 라벨 자체는 레이캐스트 대상일
            // 필요가 없다(명령서 9항과 동일한 취지의 최적화 - 클릭 처리에는 영향 없음).
            text.raycastTarget = false;
            ApplyButtonLabel(button, label);

            return button;
        }

        /// <summary>[TASK-KBO-117] 이미 존재하는 버튼을 재사용할 때도 라벨 텍스트/색상/가독성 옵션을
        /// 최신 값으로 강제 갱신한다(명령서 6항 - FindOrCreateButton 헬퍼 보완).</summary>
        private static void ApplyButtonLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>(true);
            if (text == null) return;

            text.text = label;
            text.color = Color.black;
            text.resizeTextForBestFit = true;
        }
    }
}
