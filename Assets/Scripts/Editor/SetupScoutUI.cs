using KBOManager.Controllers;
using KBOManager.Managers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-083] 방치되어 있던 ScoutUIController(선수 카드 가챠 화면)를 QA가 메뉴 클릭 한 번으로
    /// 씬에 조립·배선할 수 있게 하는 에디터 자동화. 로비 패널에 "스카우트" 진입 버튼, 스카우트 패널에
    /// "닫기" 버튼을 만들어 바인딩하고, UIManager.screens에 Scout 화면을 등록한다. SetupShopUI.cs와
    /// 동일한 관례(이름으로 기존 오브젝트를 찾아 재사용, 없으면 생성)로 여러 번 실행해도 안전하다.
    ///
    /// [TASK-KBO-092] TASK-083 당시 범위 제외됐던 resultPopupRoot/cardContainer/cardPrefab 바인딩을
    /// 이번에 완성한다 - ResultPopup(+GridLayoutGroup CardContainer)을 ScoutPanel 하위에 조립하고,
    /// 숨겨진 `_Templates` 노드 하위에 PlayerCardUI가 부착된 PlayerCardTemplate을 만들어 연결한다.
    /// 1회/10회 뽑기 버튼도 신설해 ScoutUIController.ExecuteRoll1()/ExecuteRoll10()에 연결한다.
    /// SetupCheerleaderUI.cs의 슬롯 템플릿 조립 패턴(SerializedObject.FindProperty로 필드 바인딩,
    /// `_Templates` 홀더를 SetActive(false)로 숨기되 템플릿 오브젝트 자신은 activeSelf=true 유지)을
    /// 그대로 따른다.
    ///
    /// [TASK-KBO-136, 사실 정정] 명령서는 "10회 영입" 버튼 작업의 관련 파일로 `SetupScoutHubUI.cs`/
    /// `ScoutHubUIController.cs`를 지목했으나, 그 두 파일은 선수 영입/치어리더 영입 "탭 전환"만
    /// 담당하는 얇은 스위처다(`ScoutHubUIController`에는 카테고리별 뽑기 버튼 필드 자체가 없음,
    /// 전수 확인). 6개 카테고리 버튼을 실제로 조립·바인딩하는 코드는 이 파일의 `BindCategoryButtons()`
    /// 이고, `onClick` 연결은 `ScoutUIController.cs`의 `Awake()`이므로, "1회/10회 영입" 버튼 쌍 신설은
    /// 실제 소유 파일인 이 두 곳에 구현했다. `SetupScoutHubUI.cs`/`ScoutHubUIController.cs`는 무수정이다.
    ///
    /// [TASK-KBO-137] TASK-136의 "2열 그리드 x 셀" 구조가 라이브 씬에서 텍스트 겹침 압사로 확인돼,
    /// `BindCategoryButtons()`/`FindOrCreateCategoryListContainer()`/`BindCategoryRow()`를 "6개 행이
    /// 세로로 나열되는 `VerticalLayoutGroup`" 구조로 재작성했다(자세한 근거는 `BindCategoryButtons()`
    /// 문서 주석 참고).
    /// </summary>
    public static class SetupScoutUI
    {
        private const string CanvasName = "Canvas";
        private const string ScoutPanelName = "ScoutPanel";
        private const string CloseButtonName = "CloseButton";
        private const string Roll1ButtonName = "Roll1Button";
        private const string Roll10ButtonName = "Roll10Button";
        private const string PlayerRollButtonsContainerName = "PlayerRollButtonsContainer";
        private const string ResultPopupName = "ResultPopup";
        private const string ClosePopupButtonName = "ClosePopupBtn";
        private const string RetryButtonName = "RetryButton";
        private const string ResultButtonContainerName = "ResultButtonContainer";
        private const string CardContainerName = "CardContainer";
        private const string TemplatesHolderName = "_Templates";
        private const string CardTemplateName = "PlayerCardTemplate";

        /// <summary>
        /// [TASK-KBO-129] 이 메뉴는 이제 "선수 영입" 섹션(ScoutPanel) 내부 조립만 담당한다 - 로비 진입
        /// 버튼 생성/UIManager 화면 등록은 SetupScoutHubUI.AutoConnectScoutHub()로 이관했다(치어리더
        /// 영입 섹션과 하나의 "스카우트" 화면으로 합쳐야 해서, 두 섹션을 모두 아는 상위 스크립트가
        /// 그 책임을 가져야 한다). 단독 실행해도 ScoutPanel 내부는 정상 조립되지만, 로비에서 진입하려면
        /// Auto-Connect Scout Hub까지 함께 실행해야 한다.
        /// </summary>
        [MenuItem("KBO Manager/Setup/Auto-Connect Scout UI")]
        public static void AutoConnectScoutUI()
        {
            var canvas = EnsureCanvas();
            var scoutController = EnsureScoutPanelAssembled(canvas.transform);

            var scene = scoutController.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupScoutUI] 선수 영입 섹션(ScoutPanel) 조립 완료. 로비 진입/화면 등록은 " +
                "'KBO Manager/Setup/Auto-Connect Scout Hub'로 실행하십시오.");
        }

        /// <summary>[TASK-KBO-129] ScoutPanel(선수 영입 섹션) 내부를 조립·배선하고 컨트롤러를 반환한다.
        /// SetupScoutHubUI가 CheerleaderShopPanel과 합치기 전에 먼저 이 메서드로 내용을 완성시킨다.</summary>
        internal static ScoutUIController EnsureScoutPanelAssembled(Transform canvasTransform)
        {
            var scoutController = FindOrCreateScoutPanel(canvasTransform);
            BindScoutController(scoutController);
            BindCategoryButtons(scoutController);

            var (resultPopupRoot, cardContainer, closeResultPopupButton, retryButton) = FindOrCreateResultPopup(scoutController.transform);
            var cardTemplate = FindOrCreateCardTemplate(canvasTransform);
            BindScoutResultFields(scoutController, resultPopupRoot, cardContainer, cardTemplate, closeResultPopupButton, retryButton);

            EditorUtility.SetDirty(scoutController);
            return scoutController;
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

        /// <summary>패널을 찾거나 만든다. 화면 전체를 채우는 단순 흰 배경만 붙인다(명령서 5항 - 그리드
        /// 레이아웃 등 화려한 디자인 배치는 생략).</summary>
        private static ScoutUIController FindOrCreateScoutPanel(Transform canvasTransform)
        {
            var existingChild = canvasTransform.Find(ScoutPanelName);
            if (existingChild != null)
            {
                var existingController = existingChild.GetComponent<ScoutUIController>();
                if (existingController != null) return existingController;
            }

            var panelObject = new GameObject(ScoutPanelName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {ScoutPanelName}");
            panelObject.transform.SetParent(canvasTransform, false);

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panelObject.GetComponent<Image>().color = Color.white;

            return panelObject.AddComponent<ScoutUIController>();
        }

        /// <summary>ScoutPanel에 "닫기" 버튼만 만들어 바인딩한다(포함 범위 3번째 항목).</summary>
        private static void BindScoutController(ScoutUIController controller)
        {
            var closeButton = FindOrCreateButton(controller.transform, CloseButtonName, "닫기", new Vector2(20f, 20f));
            BindButtonField(controller, "closeButton", closeButton);
        }

        /// <summary>
        /// [TASK-KBO-129] GDD "뽑기(가챠) > 선수 영입" 절의 3개 카테고리(일반/프리미엄/픽업)를 만들어
        /// ScoutUIController의 대응 필드에 바인딩한다. 구 roll1Button/roll10Button(단일 재화 혼합
        /// 확률)은 더 이상 존재하지 않는 필드라 - GDD에 없는 레이아웃/버튼 잔재를 씬에서 완전히
        /// 삭제한다(명령서 4항 DestroyImmediate 지시). 실제 onClick 연결은 ScoutUIController.Awake()가
        /// 담당한다(closeButton과 동일한 관례 - 에디터 스크립트는 필드 참조만 채운다).
        ///
        /// [TASK-KBO-137, 사실 정정 + 재조립] TASK-136이 도입한 "2열 그리드(`GridLayoutGroup`) x
        /// 셀(라벨+버튼 줄을 세로로 쌓은 `VerticalLayoutGroup`)" 구조가 실제 라이브 씬에서 텍스트가
        /// 심하게 겹치는 압사로 나타났다 - `GridLayoutGroup`은 자식의 `LayoutElement`(preferred/min)를
        /// 전혀 참조하지 않고 `cellSize`만으로 고정 크기를 강제하는 "둔감한" 레이아웃이라, 셀 내부의
        /// 중첩 레이아웃 그룹과 상호작용이 매끄럽지 않았다. 명령서 4항 지시대로 `GridLayoutGroup`을
        /// 완전히 걷어내고, 부모 컨테이너(`PlayerRollButtonsContainer`)를 `VerticalLayoutGroup`으로
        /// 바꿔 6개 카테고리를 세로로 나열되는 "행(Row)"으로 재구성했다 - 각 행 자체가
        /// `HorizontalLayoutGroup`이라 [카테고리 라벨 | 1회 영입 | 10회 영입] 3칸이 가로로 나란히
        /// 배치되고(명령서 6항 "Child Force Expand"로 균등 분할), 행에는 `LayoutElement`
        /// (minHeight=80, preferredHeight=90)를 부여해 TASK-135의 압사 방지 패턴을 그대로 재사용한다.
        /// TASK-136이 만든 구 "셀"(`LiveNormalCell` 등)과 그보다 더 오래된 구 단일 버튼(`LiveNormalButton`
        /// 등)을 모두 `DestroyImmediate`로 정리한 뒤 새 구조를 조립한다(명령서 6항 - 안전한 재조립).
        /// </summary>
        private static void BindCategoryButtons(ScoutUIController controller)
        {
            DestroyLegacyChild(controller.transform, Roll1ButtonName);
            DestroyLegacyChild(controller.transform, Roll10ButtonName);

            var list = FindOrCreateCategoryListContainer(controller.transform, PlayerRollButtonsContainerName,
                new Vector2(0f, 0.15f), new Vector2(0.6f, 0.85f));

            // [TASK-KBO-137] TASK-136의 "셀" 구조와 그보다 오래된 "셀=버튼 그 자체" 구조의 잔재를
            // 전부 정리한다 - 새 "행(Row)"은 이름이 달라 자동으로는 안 지워지므로 명시적으로 파괴한다.
            foreach (var legacyName in new[]
            {
                "LiveNormalCell", "LiveEpicCell", "PremiumSignatureCell",
                "PremiumTitleHolderCell", "PickupSignatureCell", "PickupTitleHolderCell",
                "LiveNormalButton", "LiveEpicButton", "PremiumSignatureButton",
                "PremiumTitleHolderButton", "PickupSignatureButton", "PickupTitleHolderButton",
            })
            {
                DestroyLegacyChild(list, legacyName);
            }

            BindCategoryRow(controller, list, "LiveNormalRow", "일반 영입\n라이브 일반",
                "liveNormalButton", "liveNormalButton10");
            BindCategoryRow(controller, list, "LiveEpicRow", "일반 영입\n라이브 에픽",
                "liveEpicButton", "liveEpicButton10");
            BindCategoryRow(controller, list, "PremiumSignatureRow", "프리미엄 영입\n시그니처 (싸인볼)",
                "premiumSignatureButton", "premiumSignatureButton10");
            BindCategoryRow(controller, list, "PremiumTitleHolderRow", "프리미엄 영입\n타이틀 홀더 (트로피)",
                "premiumTitleHolderButton", "premiumTitleHolderButton10");
            BindCategoryRow(controller, list, "PickupSignatureRow", "픽업 영입\n시그니처 (픽업권)",
                "pickupSignatureButton", "pickupSignatureButton10");
            BindCategoryRow(controller, list, "PickupTitleHolderRow", "픽업 영입\n타이틀 홀더 (픽업권)",
                "pickupTitleHolderButton", "pickupTitleHolderButton10");
        }

        /// <summary>이름으로 자식을 찾아 존재하면 DestroyImmediate로 완전히 제거한다. GDD에 없는 구
        /// UI 요소를 정리할 때만 쓴다.</summary>
        private static void DestroyLegacyChild(Transform parent, string name)
        {
            var legacy = parent.Find(name);
            if (legacy != null) Object.DestroyImmediate(legacy.gameObject);
        }

        /// <summary>[TASK-KBO-137] 6개 카테고리 행(Row)을 세로로 나열할 컨테이너. 과거(TASK-136)
        /// `GridLayoutGroup`을 썼던 자리를 `VerticalLayoutGroup`으로 교체한다 - 남아있을 수 있는 구
        /// `GridLayoutGroup` 컴포넌트는 명시적으로 제거한다(명령서 6항 안전한 재조립). 재실행 시에도
        /// 최신 값이 반영되도록 매번 무조건 재적용한다.</summary>
        private static Transform FindOrCreateCategoryListContainer(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existing = parent.Find(name);
            GameObject containerObject;
            if (existing != null)
            {
                containerObject = existing.gameObject;
            }
            else
            {
                containerObject = new GameObject(name, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(containerObject, $"Create {name}");
                containerObject.transform.SetParent(parent, false);

                var rect = (RectTransform)containerObject.transform;
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            if (containerObject.TryGetComponent<GridLayoutGroup>(out var legacyGrid))
            {
                Object.DestroyImmediate(legacyGrid);
            }

            if (!containerObject.TryGetComponent<VerticalLayoutGroup>(out var vertical))
            {
                vertical = containerObject.AddComponent<VerticalLayoutGroup>();
            }
            vertical.padding = new RectOffset(8, 8, 8, 8);
            vertical.spacing = 12f;
            vertical.childAlignment = TextAnchor.UpperCenter;
            vertical.childControlWidth = true;
            vertical.childForceExpandWidth = true;
            vertical.childControlHeight = true;
            vertical.childForceExpandHeight = false;

            return containerObject.transform;
        }

        /// <summary>[TASK-KBO-137] 카테고리 하나(예: "일반 영입 - 라이브 일반")를 가로 한 줄(Row)로
        /// 조립한다. `HorizontalLayoutGroup`(childForceExpandWidth=true)이 [카테고리 라벨 | 1회 영입 |
        /// 10회 영입] 3칸을 균등 분할하고(명령서 6항), 행 자체에는 `LayoutElement`(minHeight=80,
        /// preferredHeight=90)를 부여해 부모 `VerticalLayoutGroup`의 min↔preferred 보간으로 압사되지
        /// 않도록 방어한다(TASK-135와 동일 패턴). 완성된 두 버튼을 컨트롤러의 대응 필드에 바인딩한다.</summary>
        private static void BindCategoryRow(ScoutUIController controller, Transform list, string rowName,
            string categoryLabel, string roll1FieldName, string roll10FieldName)
        {
            var existing = list.Find(rowName);
            GameObject rowObject;
            if (existing != null)
            {
                rowObject = existing.gameObject;
            }
            else
            {
                rowObject = new GameObject(rowName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(rowObject, $"Create {rowName}");
                rowObject.transform.SetParent(list, false);
            }

            if (!rowObject.TryGetComponent<HorizontalLayoutGroup>(out var layout))
            {
                layout = rowObject.AddComponent<HorizontalLayoutGroup>();
            }
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 4, 4);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            EnsureLayoutElement(rowObject, preferredHeight: 90f, flexibleHeight: 0f, minHeight: 80f);

            FindOrCreateRowLabel(rowObject.transform, categoryLabel);
            var roll1Button = FindOrCreateRowButton(rowObject.transform, "Roll1Button", "1회 영입");
            var roll10Button = FindOrCreateRowButton(rowObject.transform, "Roll10Button", "10회 영입");

            BindButtonField(controller, roll1FieldName, roll1Button);
            BindButtonField(controller, roll10FieldName, roll10Button);
        }

        private static Text FindOrCreateRowLabel(Transform parent, string label)
        {
            const string name = "CategoryLabel";
            var existing = parent.Find(name);
            Text text;
            if (existing != null && existing.TryGetComponent<Text>(out var existingText))
            {
                text = existingText;
            }
            else
            {
                var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
                Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
                textObject.transform.SetParent(parent, false);

                text = textObject.GetComponent<Text>();
                text.font = KBOFonts.Default;
                text.raycastTarget = false;
            }

            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 14;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = 18;

            return text;
        }

        private static Button FindOrCreateRowButton(Transform parent, string name, string label)
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
            text.font = KBOFonts.Default;
            text.raycastTarget = false;
            ApplyButtonLabel(button, label);

            return button;
        }

        /// <summary>[TASK-KBO-136] `VerticalLayoutGroup`(childControlHeight=true) 하위에서 자식의 세로
        /// 크기를 고정한다(TASK-135가 `SetupUpgradeUI.cs`에 도입한 동일 패턴 - `minHeight`를
        /// `preferredHeight`와 맞춰 min↔preferred 역방향 보간에 의한 압사를 방지한다).</summary>
        private static void EnsureLayoutElement(GameObject go, float preferredHeight, float flexibleHeight, float minHeight = 0f)
        {
            if (!go.TryGetComponent<LayoutElement>(out var layoutElement))
            {
                layoutElement = go.AddComponent<LayoutElement>();
            }
            layoutElement.minHeight = minHeight;
            layoutElement.preferredHeight = preferredHeight;
            layoutElement.flexibleHeight = flexibleHeight;
        }

        /// <summary>[TASK-KBO-092/093] ScoutPanel 하위에 결과 팝업(ResultPopup)과 그 안의 카드 컨테이너
        /// (GridLayoutGroup), 그리고 팝업을 닫는 "확인" 버튼(ClosePopupBtn)을 조립한다. 팝업은 평소 숨겨져
        /// 있다가 ScoutUIController.ShowResults()가 뽑기 시점에 활성화한다(기존 런타임 로직, 여기서는 최초
        /// 생성 시 초기 상태만 비활성으로 맞춘다). [TASK-KBO-093] TASK-092가 남긴 "팝업을 닫을 버튼이
        /// 없다"는 UX 블로커를 여기서 해소한다.</summary>
        private static (GameObject popupRoot, Transform container, Button closeButton, Button retryButton) FindOrCreateResultPopup(Transform scoutPanelTransform)
        {
            var existingPopup = scoutPanelTransform.Find(ResultPopupName);
            GameObject popupObject;
            if (existingPopup != null)
            {
                popupObject = existingPopup.gameObject;
            }
            else
            {
                popupObject = new GameObject(ResultPopupName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(popupObject, $"Create {ResultPopupName}");
                popupObject.transform.SetParent(scoutPanelTransform, false);

                var rect = (RectTransform)popupObject.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                popupObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);
                popupObject.SetActive(false);
            }

            var container = FindOrCreateCardContainer(popupObject.transform);
            var buttonContainer = FindOrCreateResultButtonContainer(popupObject.transform);
            // [TASK-KBO-140] "다시 뽑기"를 "확인" 옆에 신설한다 - 둘 다 같은 ResultButtonContainer
            // (HorizontalLayoutGroup)에 속하므로 자동으로 나란히 배치된다.
            var retryButton = FindOrCreateButton(buttonContainer, RetryButtonName, "다시 뽑기", Vector2.zero);
            var closeButton = FindOrCreateButton(buttonContainer, ClosePopupButtonName, "확인", Vector2.zero);

            return (popupObject, container, closeButton, retryButton);
        }

        /// <summary>
        /// [TASK-KBO-118] 결과 팝업 하단에 액션 버튼을 담을 전용 컨테이너. `ResultPopup`은 `ScoutPanel`과
        /// 동일한 전체화면 앵커(0,0)~(1,1)를 쓰므로, 팝업 안의 "확인" 버튼(구 픽셀 좌표 300,20)이
        /// `ScoutPanel` 직속의 `roll1Button`(200~360)/`roll10Button`(380~540)과 같은 좌표계를 공유해
        /// 실제로 겹쳐 있었다 - 스크린샷으로 보고된 클릭 먹통 현상의 원인이다. `HorizontalLayoutGroup`으로
        /// 가운데 정렬해 좌측의 두 뽑기 버튼과 겹치지 않게 한다. 과거 버전에서 팝업의 직속 자식으로
        /// 만들어져 있던 "확인" 버튼은 이 컨테이너 하위로 이동시켜 중복 생성을 막는다(명령서 6항).
        ///
        /// [결정 필요 아님, 명령서 4항 전제 오류] 명령서는 `roll10Button`("다시 뽑기/10연차 연동 버튼")도
        /// 이 컨테이너로 옮기라고 지시했으나, `ScoutUIController.cs` 원문을 재확인한 결과 `roll10Button`은
        /// "결과 팝업 안의 재뽑기 버튼"이 아니라 애초에 가챠를 실행하는 유일한 트리거(`ExecuteRoll10()`)다.
        /// `ResultButtonContainer`는 `ResultPopup`의 자식이고 `ResultPopup`은 평소 `SetActive(false)`로
        /// 숨겨져 있다가 뽑기 "이후"에만 열리므로, `roll10Button`을 이 안으로 옮기면 애초에 뽑기를 시작할
        /// 방법이 사라지는 순환 잠금(뽑아야 버튼이 보이는데 버튼이 없어 뽑을 수 없음)이 생긴다 - 그래서
        /// `roll10Button`은 옮기지 않고 `ScoutPanel` 직속에 그대로 뒀다.
        /// </summary>
        private static Transform FindOrCreateResultButtonContainer(Transform popupTransform)
        {
            var existingContainer = popupTransform.Find(ResultButtonContainerName);
            Transform containerTransform;
            if (existingContainer != null)
            {
                containerTransform = existingContainer;
            }
            else
            {
                var containerObject = new GameObject(ResultButtonContainerName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(containerObject, $"Create {ResultButtonContainerName}");
                containerObject.transform.SetParent(popupTransform, false);

                var rect = (RectTransform)containerObject.transform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0.2f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                containerTransform = containerObject.transform;
            }

            if (!containerTransform.TryGetComponent<HorizontalLayoutGroup>(out var layout))
            {
                layout = containerTransform.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 20f;
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
            }

            // 과거 버전에서 팝업의 직속 자식으로 만들어져 있던 "확인" 버튼을 이 컨테이너 하위로 이동한다
            // (명령서 6항 - 중복 생성 방지, SetParent(..., false)로 로컬 좌표계만 재계산).
            var legacyButton = popupTransform.Find(ClosePopupButtonName);
            if (legacyButton != null && legacyButton.parent == popupTransform)
            {
                legacyButton.SetParent(containerTransform, false);
            }

            return containerTransform;
        }

        private static Transform FindOrCreateCardContainer(Transform popupTransform)
        {
            var existingContainer = popupTransform.Find(CardContainerName);
            if (existingContainer != null) return existingContainer;

            var containerObject = new GameObject(CardContainerName, typeof(RectTransform), typeof(GridLayoutGroup));
            Undo.RegisterCreatedObjectUndo(containerObject, $"Create {CardContainerName}");
            containerObject.transform.SetParent(popupTransform, false);

            var rect = (RectTransform)containerObject.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(760f, 420f);
            rect.anchoredPosition = Vector2.zero;

            var grid = containerObject.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(140f, 200f);
            grid.spacing = new Vector2(10f, 10f);
            grid.childAlignment = TextAnchor.UpperCenter;

            return containerObject.transform;
        }

        /// <summary>ScoutUIController의 resultPopupRoot/cardContainer/cardPrefab/closeResultPopupButton/
        /// retryButton(TASK-KBO-140 신설) 5개 필드를 바인딩한다.</summary>
        private static void BindScoutResultFields(ScoutUIController controller, GameObject resultPopupRoot,
            Transform cardContainer, PlayerCardUI cardTemplate, Button closeResultPopupButton, Button retryButton)
        {
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("resultPopupRoot").objectReferenceValue = resultPopupRoot;
            serializedController.FindProperty("cardContainer").objectReferenceValue = cardContainer;
            serializedController.FindProperty("cardPrefab").objectReferenceValue = cardTemplate;
            serializedController.FindProperty("closeResultPopupButton").objectReferenceValue = closeResultPopupButton;
            serializedController.FindProperty("retryButton").objectReferenceValue = retryButton;
            serializedController.ApplyModifiedProperties();
        }

        /// <summary>
        /// [TASK-KBO-092] "프리팹 대용" PlayerCardUI 템플릿을 `_Templates` 노드 하위에 조립한다.
        /// SetupCheerleaderUI.FindOrCreateSlotTemplate()과 동일한 관례 - 부모(`_Templates`)는
        /// SetActive(false)로 숨기지만, 템플릿 오브젝트 자신의 activeSelf는 반드시 true로 유지한다
        /// (ScoutUIController.SpawnCard()가 Instantiate(cardPrefab, cardContainer)로 복제할 때 원본의
        /// activeSelf를 그대로 복사하므로, 여기서 false를 두면 실제로 뽑은 카드 전부가 비활성 상태로
        /// 태어나 화면에 표시되지 않는다). PlayerCardUI.cs의 모든 [SerializeField] 필드(Text 4종,
        /// frameImage, starIcons 6개, selectedOverlay, checkmarkIcon, staminaBarRoot/staminaFillImage,
        /// conditionIconImage/conditionArrowText)를 누락 없이 생성·바인딩한다.
        /// </summary>
        private static PlayerCardUI FindOrCreateCardTemplate(Transform canvasTransform)
        {
            var holderTransform = canvasTransform.Find(TemplatesHolderName);
            if (holderTransform == null)
            {
                var holderObject = new GameObject(TemplatesHolderName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(holderObject, $"Create {TemplatesHolderName}");
                holderObject.transform.SetParent(canvasTransform, false);
                holderObject.SetActive(false);
                holderTransform = holderObject.transform;
            }

            var cardObject = FindOrCreateCardRoot(holderTransform);
            var cardTransform = cardObject.transform;

            var nameText = FindOrCreateText(cardTransform, "NameText", new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(132f, 20f));
            var teamText = FindOrCreateText(cardTransform, "TeamText", new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(132f, 18f));
            var positionText = FindOrCreateText(cardTransform, "PositionText", new Vector2(0.5f, 1f), new Vector2(0f, -44f), new Vector2(132f, 18f));
            var ovrText = FindOrCreateText(cardTransform, "OvrText", new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(132f, 20f));

            var frameImage = cardObject.GetComponent<Image>();
            var starIcons = FindOrCreateStarIcons(cardTransform);
            var selectedOverlay = FindOrCreateSelectedOverlay(cardTransform);
            var checkmarkIcon = FindOrCreateCheckmark(cardTransform);
            var (staminaBarRoot, staminaFillImage) = FindOrCreateStaminaBar(cardTransform);
            var (conditionIconImage, conditionArrowText) = FindOrCreateConditionIndicator(cardTransform);

            var cardUI = cardObject.GetComponent<PlayerCardUI>();
            if (cardUI == null) cardUI = cardObject.AddComponent<PlayerCardUI>();

            var serializedCard = new SerializedObject(cardUI);
            serializedCard.FindProperty("nameText").objectReferenceValue = nameText;
            serializedCard.FindProperty("teamText").objectReferenceValue = teamText;
            serializedCard.FindProperty("positionText").objectReferenceValue = positionText;
            serializedCard.FindProperty("ovrText").objectReferenceValue = ovrText;
            serializedCard.FindProperty("frameImage").objectReferenceValue = frameImage;

            var starIconsProperty = serializedCard.FindProperty("starIcons");
            starIconsProperty.arraySize = starIcons.Length;
            for (int i = 0; i < starIcons.Length; i++)
            {
                starIconsProperty.GetArrayElementAtIndex(i).objectReferenceValue = starIcons[i];
            }

            serializedCard.FindProperty("selectedOverlay").objectReferenceValue = selectedOverlay;
            serializedCard.FindProperty("checkmarkIcon").objectReferenceValue = checkmarkIcon;
            serializedCard.FindProperty("staminaBarRoot").objectReferenceValue = staminaBarRoot;
            serializedCard.FindProperty("staminaFillImage").objectReferenceValue = staminaFillImage;
            serializedCard.FindProperty("conditionIconImage").objectReferenceValue = conditionIconImage;
            serializedCard.FindProperty("conditionArrowText").objectReferenceValue = conditionArrowText;
            serializedCard.ApplyModifiedProperties();

            return cardUI;
        }

        private static GameObject FindOrCreateCardRoot(Transform holderTransform)
        {
            var existingCard = holderTransform.Find(CardTemplateName);
            if (existingCard != null) return existingCard.gameObject;

            var cardObject = new GameObject(CardTemplateName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(cardObject, $"Create {CardTemplateName}");
            cardObject.transform.SetParent(holderTransform, false);

            var rect = (RectTransform)cardObject.transform;
            rect.sizeDelta = new Vector2(140f, 200f);

            cardObject.GetComponent<Image>().color = Color.white;

            return cardObject;
        }

        private static Text FindOrCreateText(Transform parent, string name, Vector2 anchor, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null && existingChild.TryGetComponent<Text>(out var existingText)) return existingText;

            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
            textObject.transform.SetParent(parent, false);

            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;

            var text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 14;
            text.font = KBOFonts.Default;
            text.raycastTarget = false;

            return text;
        }

        private static Image[] FindOrCreateStarIcons(Transform cardTransform)
        {
            const int starCount = 6;
            const float spacing = 20f;
            float startX = -(spacing * (starCount - 1)) / 2f;

            var icons = new Image[starCount];
            for (int i = 0; i < starCount; i++)
            {
                string name = $"Star{i + 1}";
                var existingChild = cardTransform.Find(name);
                if (existingChild != null && existingChild.TryGetComponent<Image>(out var existingImage))
                {
                    icons[i] = existingImage;
                    continue;
                }

                var starObject = new GameObject(name, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(starObject, $"Create {name}");
                starObject.transform.SetParent(cardTransform, false);

                var rect = (RectTransform)starObject.transform;
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(16f, 16f);
                rect.anchoredPosition = new Vector2(startX + spacing * i, -66f);

                var image = starObject.GetComponent<Image>();
                image.color = new Color(0.35f, 0.35f, 0.35f, 1f);
                image.raycastTarget = false;

                icons[i] = image;
            }

            return icons;
        }

        /// <summary>카드 전체를 덮는 선택 표시 오버레이. 평소 비활성 상태로 둔다(PlayerCardUI.SetSelected()가 토글).</summary>
        private static GameObject FindOrCreateSelectedOverlay(Transform cardTransform)
        {
            const string name = "SelectedOverlay";
            var existingChild = cardTransform.Find(name);
            if (existingChild != null) return existingChild.gameObject;

            var overlayObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(overlayObject, $"Create {name}");
            overlayObject.transform.SetParent(cardTransform, false);

            var rect = (RectTransform)overlayObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = overlayObject.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.5f);
            image.raycastTarget = false;

            overlayObject.SetActive(false);
            return overlayObject;
        }

        /// <summary>좌상단 체크마크 아이콘. 평소 비활성 상태로 둔다.</summary>
        private static GameObject FindOrCreateCheckmark(Transform cardTransform)
        {
            const string name = "CheckmarkIcon";
            var existingChild = cardTransform.Find(name);
            if (existingChild != null) return existingChild.gameObject;

            var checkObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(checkObject, $"Create {name}");
            checkObject.transform.SetParent(cardTransform, false);

            var rect = (RectTransform)checkObject.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(20f, 20f);
            rect.anchoredPosition = new Vector2(4f, -4f);

            var image = checkObject.GetComponent<Image>();
            image.color = new Color(0.2f, 0.8f, 0.2f);
            image.raycastTarget = false;

            checkObject.SetActive(false);
            return checkObject;
        }

        /// <summary>하단 체력 게이지(Image.Type=Filled, Horizontal). 투수 카드에서만 PlayerCardUI.SetupStamina()가
        /// 활성화·fillAmount를 갱신한다.</summary>
        private static (GameObject root, Image fill) FindOrCreateStaminaBar(Transform cardTransform)
        {
            const string rootName = "StaminaBarRoot";
            var rootTransform = cardTransform.Find(rootName);
            GameObject rootObject;
            if (rootTransform != null)
            {
                rootObject = rootTransform.gameObject;
            }
            else
            {
                rootObject = new GameObject(rootName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(rootObject, $"Create {rootName}");
                rootObject.transform.SetParent(cardTransform, false);

                var rect = (RectTransform)rootObject.transform;
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.sizeDelta = new Vector2(120f, 8f);
                rect.anchoredPosition = new Vector2(0f, 6f);

                rootObject.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f);
            }

            var fillTransform = rootObject.transform.Find("Fill");
            if (fillTransform != null && fillTransform.TryGetComponent<Image>(out var existingFill))
            {
                return (rootObject, existingFill);
            }

            var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(fillObject, "Create Fill");
            fillObject.transform.SetParent(rootObject.transform, false);

            var fillRect = (RectTransform)fillObject.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            var fillImage = fillObject.GetComponent<Image>();
            fillImage.color = Color.green;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.raycastTarget = false;

            return (rootObject, fillImage);
        }

        /// <summary>우상단 컨디션 아이콘 + 화살표 텍스트.</summary>
        private static (Image icon, Text arrow) FindOrCreateConditionIndicator(Transform cardTransform)
        {
            const string iconName = "ConditionIcon";
            Image iconImage;
            var iconTransform = cardTransform.Find(iconName);
            if (iconTransform != null && iconTransform.TryGetComponent<Image>(out iconImage))
            {
                // 기존 오브젝트 재사용
            }
            else
            {
                var iconObject = new GameObject(iconName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(iconObject, $"Create {iconName}");
                iconObject.transform.SetParent(cardTransform, false);

                var rect = (RectTransform)iconObject.transform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.sizeDelta = new Vector2(16f, 16f);
                rect.anchoredPosition = new Vector2(-4f, -4f);

                iconImage = iconObject.GetComponent<Image>();
                iconImage.color = new Color(0.75f, 0.75f, 0.75f);
                iconImage.raycastTarget = false;
            }

            var arrowText = FindOrCreateText(cardTransform, "ConditionArrowText",
                new Vector2(1f, 1f), new Vector2(-4f, -22f), new Vector2(20f, 18f));

            return (iconImage, arrowText);
        }

        private static Button FindOrCreateButton(Transform parent, string name, string label, Vector2 anchoredPosition)
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

            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
            buttonObject.transform.SetParent(parent, false);

            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(160f, 50f);
            rect.anchoredPosition = anchoredPosition;

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
            text.fontSize = 18;
            text.font = KBOFonts.Default;
            text.raycastTarget = false;
            ApplyButtonLabel(button, label);

            return button;
        }

        /// <summary>[TASK-KBO-117] 이미 존재하는 버튼을 재사용할 때도 라벨 텍스트/색상/가독성 옵션을
        /// 최신 값으로 강제 갱신한다 - 기존에는 재사용 시 라벨을 건드리지 않아 예전 텍스트가 그대로
        /// 남아 있었다(명령서 6항 - FindOrCreateButton 헬퍼 보완).</summary>
        private static void ApplyButtonLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>(true);
            if (text == null) return;

            text.text = label;
            text.color = Color.black;
            text.resizeTextForBestFit = true;
        }

        private static void BindButtonField(Object controller, string fieldName, Button button)
        {
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty(fieldName).objectReferenceValue = button;
            serializedController.ApplyModifiedProperties();
        }

        /// <summary>
        /// UIManager.screens(List&lt;ScreenEntry&gt;, 필드는 Type/Root - SceneInitializer/SetupShopUI에서
        /// 이미 검증된 실제 필드명)에 Scout 항목을 등록한다. 이미 등록되어 있으면 Root 참조만 최신
        /// 오브젝트로 덮어써 중복 추가를 막는다(명령서 7항). [TASK-KBO-129] internal로 열어
        /// SetupScoutHubUI.cs(선수/치어리더 영입을 하나의 Scout 화면으로 등록하는 상위 스크립트)가
        /// 재사용한다.
        /// </summary>
        internal static void RegisterScoutScreen(UIManager uiManager, GameObject scoutRoot)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                var typeProperty = element.FindPropertyRelative("Type");
                if (typeProperty.intValue == (int)ScreenType.Scout)
                {
                    element.FindPropertyRelative("Root").objectReferenceValue = scoutRoot;
                    serializedManager.ApplyModifiedProperties();
                    return;
                }
            }

            int newIndex = screensProperty.arraySize;
            screensProperty.arraySize++;

            var newElement = screensProperty.GetArrayElementAtIndex(newIndex);
            newElement.FindPropertyRelative("Type").intValue = (int)ScreenType.Scout;
            newElement.FindPropertyRelative("Root").objectReferenceValue = scoutRoot;

            serializedManager.ApplyModifiedProperties();
        }
    }
}
