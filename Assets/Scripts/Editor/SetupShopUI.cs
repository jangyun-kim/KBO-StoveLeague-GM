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
    ///
    /// [TASK-KBO-140, 사실 정정 + 강화] 명령서는 "이름 검색(Find)에 의존한 클린업 실패로 구형 UI와
    /// 신규 UI가 중복 렌더링된다"고 전제했다. 재확인 결과 TASK-139 시점 코드는 딱 3개의 구형 이름
    /// ("CheerStickText"/"Roll1xButton"/"Roll10xButton", TASK-129 세대)만 `DestroyLegacyChild()`로
    /// 지목하고 있어, 그보다 더 오래됐거나 그 사이 세대에 존재했을 수 있는 다른 이름의 잔재는 애초에
    /// 정리 대상에 없었다 - "이름 기반 선택적 삭제"라는 접근 자체가 구조적으로 매 세대마다 놓치는
    /// 이름이 생길 수 있는 취약점이었다는 명령서의 지적은 타당하다. 이번에 `BindShopController()`
    /// 최상단에서 `ShopPanel`의 모든 자식을 이름과 무관하게 역순 `for` 루프로 전부 `DestroyImmediate`
    /// 한 뒤(패널 오브젝트 자신과 그 `Image`/`VerticalLayoutGroup`은 유지) 바닥부터 재조립하도록
    /// 바꿔, "다음 세대에 새 이름이 추가돼도 놓치는" 구조적 취약점 자체를 제거했다(명령서 4항). 카테고리
    /// 4개는 `SetupScoutUI.cs`(TASK-137/139)와 동일하게 [라벨]-[1회 영입]-[10회 영입] 3칸
    /// `HorizontalLayoutGroup` 행으로 재구성했다 - `CheerleaderShopUIController.cs`에 10회 전용 필드
    /// 4개를 신설했다(자세한 내용은 그 파일 주석 참고). 조립 직후 계층 구조를 텍스트로 덤프하는
    /// `LogHierarchyDump()`를 추가해(명령서 6항, AC-02) 매 실행마다 중복 여부를 콘솔에서 스스로
    /// 증명한다.
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
        /// [TASK-KBO-129/140] GDD "뽑기(가챠) > 치어리더 영입" 절의 4개 카테고리(일반 > 라이브/한정,
        /// 픽업·프리미엄 > 아이콘/레전드)를 [라벨]-[1회 영입]-[10회 영입] 행으로, 4개 재화 표시 텍스트를
        /// 조립·바인딩한다. [TASK-KBO-140] 이름 기반 선택적 삭제가 세대를 거듭할수록 놓치는 이름이
        /// 생기는 구조적 취약점이었다는 사실이 확인돼(클래스 요약 참고), 특정 이름을 지목해 지우는 대신
        /// `parent`의 모든 자식을 예외 없이 역순으로 파괴한 뒤 바닥부터 다시 조립한다(명령서 4항).
        /// </summary>
        private static void BindShopController(CheerleaderShopUIController controller)
        {
            var parent = controller.transform;

            // [TASK-KBO-140] 이름을 몰라도 안전하게 전부 지운다 - 역순으로 순회해 DestroyImmediate가
            // childCount를 바꿔도 인덱스가 밀리지 않는다. 패널 자신(parent)과 그 Image/
            // VerticalLayoutGroup 컴포넌트는 대상이 아니다(자식만 제거).
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
            }

            var liveCheerStickText = FindOrCreateText(parent, "LiveCheerStickText", 32f, isDisplayOnly: true);
            var limitedCheerStickText = FindOrCreateText(parent, "LimitedCheerStickText", 32f, isDisplayOnly: true);
            var starCheerStickText = FindOrCreateText(parent, "StarCheerStickText", 32f, isDisplayOnly: true);
            var legendCheerStickText = FindOrCreateText(parent, "LegendCheerStickText", 32f, isDisplayOnly: true);

            var (liveButton, liveButton10) = CreateCategoryRow(parent, "LiveRow", "일반 영입 - 라이브 (라이브 응원봉)");
            var (limitedButton, limitedButton10) = CreateCategoryRow(parent, "LimitedRow", "일반 영입 - 한정 (한정 응원봉)");
            var (iconButton, iconButton10) = CreateCategoryRow(parent, "IconRow", "픽업/프리미엄 영입 - 아이콘 (스타 응원봉)");
            var (legendButton, legendButton10) = CreateCategoryRow(parent, "LegendRow", "픽업/프리미엄 영입 - 레전드 (레전드 응원봉)");

            var closeButton = FindOrCreateButton(parent, CloseButtonName, "닫기");
            var resultLogText = FindOrCreateText(parent, "ResultLogText", 160f, isDisplayOnly: true);

            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("liveCheerStickText").objectReferenceValue = liveCheerStickText;
            serializedController.FindProperty("limitedCheerStickText").objectReferenceValue = limitedCheerStickText;
            serializedController.FindProperty("starCheerStickText").objectReferenceValue = starCheerStickText;
            serializedController.FindProperty("legendCheerStickText").objectReferenceValue = legendCheerStickText;
            serializedController.FindProperty("liveButton").objectReferenceValue = liveButton;
            serializedController.FindProperty("liveButton10").objectReferenceValue = liveButton10;
            serializedController.FindProperty("limitedButton").objectReferenceValue = limitedButton;
            serializedController.FindProperty("limitedButton10").objectReferenceValue = limitedButton10;
            serializedController.FindProperty("iconButton").objectReferenceValue = iconButton;
            serializedController.FindProperty("iconButton10").objectReferenceValue = iconButton10;
            serializedController.FindProperty("legendButton").objectReferenceValue = legendButton;
            serializedController.FindProperty("legendButton10").objectReferenceValue = legendButton10;
            serializedController.FindProperty("closeButton").objectReferenceValue = closeButton;
            serializedController.FindProperty("resultLogText").objectReferenceValue = resultLogText;
            serializedController.ApplyModifiedProperties();

            // [TASK-KBO-140, 명령서 6항/AC-02] 씬을 직접 볼 수 없으므로, 조립 직후 계층을 텍스트로
            // 덤프해 중복 생성 여부를 콘솔 로그로 스스로 증명한다.
            LogHierarchyDump(parent);
        }

        /// <summary>[TASK-KBO-140] 카테고리 한 칸을 [라벨]-[1회 영입]-[10회 영입] 3칸짜리
        /// `HorizontalLayoutGroup` 행으로 새로 만든다(`SetupScoutUI.BindCategoryRow()`와 동일 패턴).
        /// `BindShopController()`가 매 실행마다 부모의 자식을 전부 지운 뒤 호출하므로 재사용 분기 없이
        /// 항상 새로 만든다.</summary>
        private static (Button roll1, Button roll10) CreateCategoryRow(Transform parent, string rowName, string label)
        {
            var rowObject = new GameObject(rowName, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(rowObject, $"Create {rowName}");
            rowObject.transform.SetParent(parent, false);

            var layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            // [TASK-KBO-140] minHeight=preferredHeight(TASK-135 패턴)로 부모 VerticalLayoutGroup의
            // min<->preferred 보간 압사를 방지한다 - 80/90은 SetupScoutUI.BindCategoryRow()와 동일한 값.
            var rowLayoutElement = rowObject.GetComponent<LayoutElement>();
            rowLayoutElement.preferredHeight = 90f;
            rowLayoutElement.minHeight = 80f;

            CreateRowLabel(rowObject.transform, label);
            var roll1Button = CreateRowButton(rowObject.transform, "Roll1Button", "1회 영입");
            var roll10Button = CreateRowButton(rowObject.transform, "Roll10Button", "10회 영입");

            return (roll1Button, roll10Button);
        }

        private static void CreateRowLabel(Transform parent, string label)
        {
            var textObject = new GameObject("CategoryLabel", typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, "Create CategoryLabel");
            textObject.transform.SetParent(parent, false);

            var text = textObject.GetComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 14;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = 18;
            text.raycastTarget = false;
        }

        private static Button CreateRowButton(Transform parent, string name, string label)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
            buttonObject.transform.SetParent(parent, false);

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
            text.fontSize = 14;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;
            ApplyButtonLabel(button, label);

            return button;
        }

        /// <summary>[TASK-KBO-140, 명령서 6항] 유니티 에디터 화면을 직접 볼 수 없는 한계를 코드로
        /// 보완하기 위해, 조립 직후 `root`(ShopPanel) 하위 전체 계층을 이름+자식 수와 함께 콘솔에
        /// 텍스트로 덤프한다. 정상이라면 직속 자식은 정확히 10개(재화 텍스트 4 + 카테고리 행 4 + 닫기
        /// 1 + 결과 로그 1)이고, 각 카테고리 행은 정확히 3개(라벨+1회+10회)여야 한다 - 같은 이름이
        /// 두 번 나타나거나 이 개수를 넘으면 중복 생성이 재발했다는 뜻이다.</summary>
        private static void LogHierarchyDump(Transform root)
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"[SetupShopUI] {root.name} 계층 구조 덤프 (직속 자식 {root.childCount}개, 예상 10개):");
            AppendHierarchy(root, builder, 1);
            Debug.Log(builder.ToString());
        }

        private static void AppendHierarchy(Transform node, System.Text.StringBuilder builder, int depth)
        {
            for (int i = 0; i < node.childCount; i++)
            {
                var child = node.GetChild(i);
                builder.AppendLine($"{new string(' ', depth * 2)}- {child.name} (자식 {child.childCount}개)");
                AppendHierarchy(child, builder, depth + 1);
            }
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
