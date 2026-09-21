using KBOManager.Controllers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-109] v0.3 Phase 5(선수 성장) UI 킥오프.
    ///
    /// [사실 정정] 명령서가 가정한 `UpgradeUIController.cs`는 프로젝트에 존재하지 않는다(`Assets/Scripts`
    /// 전수 `grep` 결과 0건). 강화/각성 기능을 실제로 담당하는 컨트롤러는 `MaterialSelectUIController.cs`
    /// (인벤토리 상세 패널의 [강화하기]/[각성하기] 버튼이 여는 재료 다중 선택 팝업)이며,
    /// `GameActionController.ExecuteEnhance()`/`ExecuteAwaken()`까지 이미 완전히 연결돼 있으나 씬에는
    /// 전혀 조립돼 있지 않았다(`FindAnyObjectByType` 0건). 명령서 6항이 "또는 그와 유사한 역할을 하는
    /// 스크립트"를 찾으라고 명시했으므로, 이 파일은 `UpgradeUIController`가 아니라
    /// `MaterialSelectUIController`의 실제 필드(`popupRoot`/`titleText`/`selectionCountText`/
    /// `confirmButton`/`cancelButton`/`playerListPanel`/`playerListContainer`/`playerCardPrefab`/
    /// `itemListPanel`/`itemListContainer`/`itemEntryPrefab`/`gameActionController`)를 조립·바인딩한다.
    ///
    /// [범위 제외, 결정 필요] 이 컨트롤러는 강화/각성 모드를 "토글 UI"가 아니라 호출자가
    /// `OpenForEnhance()`/`OpenForAwaken()` 중 무엇을 부르느냐로 결정한다(설계 자체가 다름) - 명령서
    /// 4항이 요구한 "강화/각성 토글 버튼 또는 탭"에 대응하는 실제 UI 요소가 없어 만들지 않았다.
    /// `UIManager.screens`에 `ScreenType.Upgrade`를 등록하는 것(AC-03)도, 이 팝업이 로비처럼 독립된
    /// 풀스크린 화면이 아니라 `ScoutUIController.resultPopupRoot`와 동일한 유형의 모달 팝업이라 만들지
    /// 않았다 - 기존 코드베이스의 다른 모달 팝업(스카우트 결과 팝업 등) 역시 어느 것도 `ScreenType`으로
    /// 등록돼 있지 않다. 이 팝업을 실제로 여는 `InventoryUIController`(`[강화하기]`/`[각성하기]` 버튼의
    /// 호스트) 자체도 씬에 아직 전혀 조립돼 있지 않음을 확인했다(`FindAnyObjectByType` 0건) - 이는 이번
    /// 명령서의 포함 범위(`SetupUpgradeUI.cs` 신설) 밖의 별도 대형 작업이라 이번에는 건드리지 않았다.
    ///
    /// [TASK-KBO-123, 사실 정정] `MaterialSelectUIController.cs` 원문(68~74/303~312행)을 재확인한 결과
    /// `Awake()`가 `cancelButton.onClick.AddListener(ClosePopup)`를 이미 등록하고 있고, `ClosePopup()`도
    /// 선택 상태 초기화 + `popupRoot.SetActive(false)`까지 전부 이미 구현돼 있었다 - "취소 로직 부재"라는
    /// 명령서 전제와 달리 C# 로직 자체는 손댈 필요가 없었다(무수정). 대신 확인/취소 버튼이 팝업 직속
    /// 자식으로 고정 픽셀 좌표에 떨어져 있던 레이아웃 문제(명령서 4항)만 `ActionContainer`
    /// (`HorizontalLayoutGroup`)로 해소했다 - 좌표가 부정확해 클릭 판정 영역이 어긋났을 가능성까지
    /// 함께 정리한다.
    ///
    /// [TASK-KBO-133, 사실 정정] 명령서는 "흰 배경 위 흰 텍스트"를 원인으로 가정했으나, 재확인 결과
    /// `TitleText`/`SelectionCountText`/버튼 라벨/`ItemEntryTemplate`/`PlayerCardTemplate`(재사용,
    /// `SetupScoutUI.cs`)는 신규 생성 시점에 이미 전부 `Color.black`으로 명시돼 있었다(무결함). 대신
    /// 실제 백화 원인은 (1) `PlayerListContainer`/`ItemListContainer`가 `ScrollRect`/`Viewport`/
    /// `Mask` 없이 `GridLayoutGroup`만 붙은 평면 패널이었던 점(표준 스크롤 뷰 계층 미준수, 명령서
    /// 6항), (2) 재료가 0개일 때 `TitleText`/`SelectionCountText` 두 줄만 남고 나머지가 전부 빈
    /// 흰 배경이라 사용자에게는 "완전한 백지"로 체감됐을 가능성(명령서 7항 예외 조건)으로 판단된다.
    /// 이번 수정은 (a) `Panel -> Viewport(RectMask2D) -> Content(GridLayoutGroup+ContentSizeFitter)`
    /// 표준 계층으로 재조립하고(과거 평면 컨테이너는 `Content`로 마이그레이션, 재생성 아님),
    /// (b) 재료 0개 시 노출할 안내 `EmptyText`를 신설하며, (c) 팝업 하위 모든 `Text`의 `color`를
    /// 마지막 단계에서 한 번 더 강제로 검정 처리해(기존 씬에 남아있을 수 있는 레거시 오브젝트 대비)
    /// 안전장치를 이중화한다.
    ///
    /// [TASK-KBO-134] 명령서는 텍스트가 "너무 작다"와 하단 [닫기] 버튼이 "가려져 소프트락"이라는 두
    /// 증상을 전제했다. `MaterialSelectUIController.cs` 원문(72~78/331~341행)을 재확인한 결과
    /// `cancelButton.onClick.AddListener(ClosePopup)` 등록과 `ClosePopup()`의 `popupRoot.SetActive(false)`
    /// 는 TASK-123 이후 그대로 존재해 리스너 자체는 무결함이었다(무수정, 아래 재검증 주석 참고) -
    /// 다만 `ContentPanel`의 5개 자식(`TitleText`/`SelectionCountText`/`ActionContainer`/
    /// `PlayerListPanel`/`ItemListPanel`)이 고정 퍼센트 앵커로만 배치돼 있어 팝업 크기가 작아지거나
    /// 콘텐츠가 늘어나는 경우 겹침 여지를 원천적으로 배제하지 못했다는 점은 사실이라, 명령서 4항이
    /// 요구한 `VerticalLayoutGroup`(+`LayoutElement`)을 `ContentPanel`에 부착해 상단 타이틀 -&gt; 중앙
    /// 스크롤 뷰(가변 높이) -&gt; 하단 `ActionContainer`(고정 높이, 항상 마지막 sibling으로 강제)가
    /// 절대 겹치지 않도록 구조적으로 보증한다. 텍스트 시인성은 `ApplyBestFit()`(신설, `resizeTextForBestFit`
    /// +`resizeTextMinSize`24/`resizeTextMaxSize`72)을 팝업 내 모든 생성 텍스트(타이틀/선택 카운트/
    /// 빈 목록 안내/버튼 라벨/아이템 항목 라벨)에 일괄 적용하고, 마지막 강제 패스에도 합류시켜
    /// 레거시 오브젝트까지 놓치지 않는다. `PlayerCardTemplate`(`SetupScoutUI.cs` 소유) 내부 텍스트는
    /// 명령서 4항 예시(타이틀/안내 문구/버튼 라벨)에 포함되지 않고 관련 데이터 파일 목록 밖이라
    /// 건드리지 않았다.
    ///
    /// [TASK-KBO-135] 명령서 3항 진단(`VerticalLayoutGroup`의 min↔preferred 보간 계산에서 `ActionContainer`
    /// 가 압사)이 실제로 성립하는 계산 특성임을 확인했다 - TASK-134가 `ActionContainer`에 `minHeight`를
    /// 부여하지 않은 채(preferredHeight만 80) `VerticalLayoutGroup`에 맡겼기 때문에, 가용 공간이
    /// 부족해지면 버튼 영역이 0에 가깝게 축소될 수 있었다. 명령서 4항의 두 번째 대안(절대 좌표 고정)을
    /// 채택해 `FindOrCreateActionContainer()`가 `LayoutElement.ignoreLayout = true`로 레이아웃 계산에서
    /// 완전히 제외하고 `ContentPanel` 하단에 고정 픽셀 높이(`ActionContainerHeight`=90)로 절대 배치하도록
    /// 재작성했다 - `ContentPanel`의 `VerticalLayoutGroup` 하단 padding도 그만큼 예약해 스크롤 목록이
    /// 그 영역을 침범하지 않게 했다. `InventoryUIController.cs`는 명령서가 가정한 `OpenDetail()`이
    /// 존재하지 않아(사실 정정, 실제 메서드는 `ShowDetail(Player)`) 그 메서드와 `CloseDetail()`에
    /// 메인 닫기 버튼 숨김/복원을 구현했다.
    /// </summary>
    public static class SetupUpgradeUI
    {
        private const string PopupName = "MaterialSelectPopup";
        private const string ContentPanelName = "ContentPanel";
        private const string TitleTextName = "TitleText";
        private const string SelectionCountTextName = "SelectionCountText";
        private const string ConfirmButtonName = "ConfirmButton";
        private const string CancelButtonName = "CancelButton";
        private const string ActionContainerName = "ActionContainer";
        private const string PlayerListPanelName = "PlayerListPanel";
        private const string PlayerListContainerName = "PlayerListContainer";
        private const string ItemListPanelName = "ItemListPanel";
        private const string ItemListContainerName = "ItemListContainer";
        private const string ViewportName = "Viewport";
        private const string EmptyTextName = "EmptyText";
        private const string TemplatesHolderName = "_Templates";
        private const string PlayerCardTemplateName = "PlayerCardTemplate";
        private const string ItemEntryTemplateName = "ItemEntryTemplate";

        // [TASK-KBO-134] 명령서 4항이 지정한 bestFit 최소/최대 크기.
        private const int BestFitMinSize = 24;
        private const int BestFitMaxSize = 72;

        // [TASK-KBO-135] ActionContainer(확인/취소 버튼)의 고정 픽셀 높이. VerticalLayoutGroup의
        // min<->preferred 보간 계산에서 완전히 제외(ignoreLayout)하고 이 값으로 절대 배치한다.
        private const float ActionContainerHeight = 90f;

        [MenuItem("KBO Manager/Setup/Auto-Connect Upgrade UI")]
        public static void AutoConnectUpgradeUI()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[SetupUpgradeUI] 씬에서 Canvas를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Initialize Current Scene'을 실행하십시오.");
                return;
            }

            var controller = FindOrCreatePopup(canvas.transform);
            // [TASK-KBO-128] 콘텐츠(제목/목록/버튼)를 받쳐주는 불투명 패널이 없어 반투명 딤 배경
            // 위에 그대로 떠 있던 구조를 보완한다 - 기존 5개 콘텐츠 자식은 새 ContentPanel 하위로
            // 이동(재생성 아님, 명령서 6항)한다.
            var contentPanel = FindOrCreateContentPanel(controller.transform);

            // [TASK-KBO-134] 아래 개별 anchorMin/anchorMax 인자는 ContentPanel에 부착된
            // VerticalLayoutGroup(childControlHeight=true)이 매 레이아웃 패스마다 덮어쓰므로 실질적으로는
            // LayoutElement(바로 아래에서 부착)가 우선한다 - 최초 생성 시의 기본 배치값으로만 남겨둔다.
            var titleText = FindOrCreateText(contentPanel, TitleTextName, "",
                new Vector2(0f, 0.85f), new Vector2(1f, 1f));
            EnsureLayoutElement(titleText.gameObject, preferredHeight: 60f, flexibleHeight: 0f, minHeight: 60f);

            var selectionCountText = FindOrCreateText(contentPanel, SelectionCountTextName, "",
                new Vector2(0f, 0.1f), new Vector2(1f, 0.15f));
            EnsureLayoutElement(selectionCountText.gameObject, preferredHeight: 50f, flexibleHeight: 0f, minHeight: 50f);

            // [TASK-KBO-123] 확인/취소 버튼을 전용 컨테이너(HorizontalLayoutGroup)로 정렬한다. 과거
            // 버전에서 팝업 직속 자식으로 고정 픽셀 앵커에 만들어져 있던 두 버튼은 이 컨테이너 하위로
            // 옮겨 재사용한다(명령서 6항 - 중복 생성 방지). [TASK-KBO-135] FindOrCreateActionContainer()
            // 내부에서 VerticalLayoutGroup으로부터 완전히 제외(ignoreLayout)하고 ContentPanel 하단에
            // 고정 픽셀 높이로 절대 배치하므로, 여기서 별도 LayoutElement 설정은 필요 없다.
            var actionContainer = FindOrCreateActionContainer(contentPanel);
            var confirmButton = FindOrCreateButton(actionContainer, ConfirmButtonName, "강화/각성 실행",
                Vector2.zero, Vector2.zero);
            var cancelButton = FindOrCreateButton(actionContainer, CancelButtonName, "닫기",
                Vector2.zero, Vector2.zero);

            var (playerListPanel, playerListContainer, playerListEmptyText) = FindOrCreateScrollList(contentPanel,
                PlayerListPanelName, PlayerListContainerName, new Vector2(140f, 200f), new Vector2(10f, 10f),
                "강화 재료로 사용할 동일 선수가 없습니다.");
            EnsureLayoutElement(playerListPanel, preferredHeight: 120f, flexibleHeight: 1f, minHeight: 120f);

            var (itemListPanel, itemListContainer, itemListEmptyText) = FindOrCreateScrollList(contentPanel,
                ItemListPanelName, ItemListContainerName, new Vector2(160f, 60f), new Vector2(10f, 10f),
                "사용 가능한 강화 재료가 없습니다.");
            EnsureLayoutElement(itemListPanel, preferredHeight: 120f, flexibleHeight: 1f, minHeight: 120f);

            // [TASK-KBO-135] ActionContainer는 이제 레이아웃 그룹 밖에서 절대 배치되지만, sibling
            // 순서(=그리기 순서)상 스크롤 목록보다 나중이어야 혹시 모를 잔여 겹침에서도 버튼이 항상
            // 위에 그려진다 - 항상 마지막 sibling으로 강제한다.
            actionContainer.SetAsLastSibling();

            var playerCardPrefab = FindOrCreatePlayerCardTemplate(canvas.transform);
            var itemEntryPrefab = FindOrCreateItemEntryTemplate(canvas.transform);

            var gameActionController = Object.FindAnyObjectByType<GameActionController>(FindObjectsInactive.Include);
            if (gameActionController == null)
            {
                Debug.LogWarning("[SetupUpgradeUI] 씬에서 GameActionController를 찾지 못해 " +
                    "gameActionController 바인딩을 건너뜁니다.");
            }

            BindController(controller, gameActionController, titleText, selectionCountText, confirmButton, cancelButton,
                playerListPanel, playerListContainer, playerCardPrefab, playerListEmptyText,
                itemListPanel, itemListContainer, itemEntryPrefab, itemListEmptyText);

            // [TASK-KBO-133/134] 팝업 하위(ContentPanel/리스트/버튼)의 모든 Text 색상과 bestFit
            // 크기를 마지막에 한 번 더 강제로 재적용한다. 개별 생성 지점에서 이미 지정하지만, 과거
            // 버전에서 조립된 뒤 씬에 남아있는 레거시 오브젝트까지 놓치지 않기 위한 이중 안전장치다.
            foreach (var text in controller.GetComponentsInChildren<Text>(true))
            {
                text.color = Color.black;
                ApplyBestFit(text);
            }

            EditorUtility.SetDirty(controller);

            var scene = controller.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupUpgradeUI] 강화/각성 재료 선택 팝업(MaterialSelectUIController) 자동 배선 완료.");
        }

        /// <summary>`MaterialSelectPopup`(없으면 신규 생성)에 `MaterialSelectUIController`를 부착한다.
        /// `Awake()`가 `ClosePopup()`으로 스스로를 비활성화하므로(원문 68~74행), 최초 생성 시 활성 상태를
        /// 유지해야 `Awake()`가 정상 실행된다 - `new GameObject`의 기본 활성 상태(true)를 그대로 둔다.</summary>
        private static MaterialSelectUIController FindOrCreatePopup(Transform canvasTransform)
        {
            var existing = canvasTransform.Find(PopupName);
            if (existing != null)
            {
                var existingController = existing.GetComponent<MaterialSelectUIController>();
                if (existingController != null) return existingController;
            }

            var popupObject = new GameObject(PopupName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(popupObject, $"Create {PopupName}");
            popupObject.transform.SetParent(canvasTransform, false);

            var rect = (RectTransform)popupObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            // [TASK-KBO-128] 딤 배경 알파를 0.6->0.8로 진하게 해 뒤의 로비/인벤토리 화면과 팝업의
            // 경계를 더 뚜렷하게 만든다(명령서 4항 DimBackground 스펙).
            popupObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);

            return popupObject.AddComponent<MaterialSelectUIController>();
        }

        /// <summary>
        /// [TASK-KBO-128] `MaterialSelectPopup`(딤 배경 전용) 하위에 콘텐츠(제목/목록/버튼)를 받쳐주는
        /// 불투명 흰색 패널을 신설한다. [사실 정정] 명령서 4항은 "PopupPanel"이 이미 존재하며 그
        /// `Image.color`가 투명하게 설정된 결함이라 전제했으나, `FindOrCreatePopup()`을 재확인한 결과
        /// 그런 별도 패널 오브젝트 자체가 애초에 없었고(콘텐츠가 전부 팝업 딤 배경의 직속 자식이었다),
        /// 딤 배경 자신의 색상도 `(0,0,0,0.6)`으로 이미 정상적으로 반투명 검정이었다(투명/버그 아님,
        /// 씬 재조회로 확인). 다만 콘텐츠를 받쳐주는 불투명 패널이 없어 텍스트/버튼이 딤 배경 위에
        /// 그대로 떠 있던 것은 사실이라, 명령서 취지(불투명 콘텐츠 배경)를 살려 이 패널을 새로 만든다.
        /// 과거 버전에서 팝업 직속 자식이었던 5개 콘텐츠 오브젝트(TitleText/SelectionCountText/
        /// ActionContainer/PlayerListPanel/ItemListPanel)는 이 패널 하위로 이동(재생성 아님)한다.
        /// </summary>
        private static Transform FindOrCreateContentPanel(Transform popupTransform)
        {
            var existing = popupTransform.Find(ContentPanelName);
            Transform contentTransform;
            if (existing != null)
            {
                contentTransform = existing;
            }
            else
            {
                var contentObject = new GameObject(ContentPanelName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(contentObject, $"Create {ContentPanelName}");
                contentObject.transform.SetParent(popupTransform, false);

                var rect = (RectTransform)contentObject.transform;
                // 화면 가장자리에 딤 배경 테두리가 보이도록 살짝 안쪽으로 앵커한다.
                rect.anchorMin = new Vector2(0.1f, 0.1f);
                rect.anchorMax = new Vector2(0.9f, 0.9f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                contentObject.GetComponent<Image>().color = Color.white;

                contentTransform = contentObject.transform;
            }

            foreach (var legacyName in new[]
            {
                TitleTextName, SelectionCountTextName, ActionContainerName, PlayerListPanelName, ItemListPanelName,
            })
            {
                var legacyChild = popupTransform.Find(legacyName);
                if (legacyChild != null && legacyChild.parent == popupTransform)
                {
                    legacyChild.SetParent(contentTransform, false);
                }
            }

            // [TASK-KBO-134] 타이틀 -> 스크롤 목록(가변 높이) -> 하단 버튼이 항상 세로로 순서대로
            // 쌓이고 서로 겹치지 않도록 구조적으로 보증한다. 각 자식의 높이 배분은 LayoutElement
            // (preferredHeight/flexibleHeight, 호출부에서 개별 부착)가 결정한다.
            // [TASK-KBO-135] `ActionContainer`는 이 레이아웃 그룹에서 완전히 제외(ignoreLayout, 아래
            // `FindOrCreateActionContainer()`)되고 ContentPanel 하단에 고정 픽셀 높이로 절대 배치되므로,
            // 나머지 자식(스크롤 목록)이 그 영역까지 늘어나 겹치지 않도록 하단 padding으로 그만큼의
            // 공간을 예약해 둔다.
            if (!contentTransform.TryGetComponent<VerticalLayoutGroup>(out var verticalLayout))
            {
                verticalLayout = contentTransform.gameObject.AddComponent<VerticalLayoutGroup>();
            }
            verticalLayout.padding = new RectOffset(24, 24, 24, Mathf.RoundToInt(ActionContainerHeight) + 24 + 12);
            verticalLayout.spacing = 12f;
            verticalLayout.childAlignment = TextAnchor.UpperCenter;
            verticalLayout.childControlWidth = true;
            verticalLayout.childControlHeight = true;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            verticalLayout.childScaleWidth = false;
            verticalLayout.childScaleHeight = false;

            return contentTransform;
        }

        /// <summary>[TASK-KBO-134] `VerticalLayoutGroup`(childControlHeight=true) 하위에서 자식의 세로
        /// 크기 배분을 결정한다. `flexibleHeight`&gt;0인 자식(스크롤 목록)이 나머지 고정 높이 자식들을
        /// 뺀 잔여 공간을 전부 차지한다.</summary>
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

        /// <summary>
        /// [TASK-KBO-123] 확인/취소 버튼을 담을 하단 컨테이너에 `HorizontalLayoutGroup`(spacing 20,
        /// `MiddleCenter`)을 부착한다. 과거 버전에서 팝업(`MaterialSelectPopup`)의 직속 자식으로 고정
        /// 픽셀 좌표에 만들어져 있던 `ConfirmButton`/`CancelButton`은 이 컨테이너 하위로 이동시켜
        /// 중복 생성을 막는다(명령서 6항).
        ///
        /// [TASK-KBO-135] `ContentPanel`의 `VerticalLayoutGroup`(childControlHeight=true)은 사용
        /// 가능한 공간이 모든 자식의 `preferredHeight` 합보다 부족해지면 각 자식을 `minHeight`~
        /// `preferredHeight` 사이로 보간(Lerp)해 축소한다 - 이때 `ActionContainer`의 `minHeight`가
        /// 0에 가까우면 버튼 영역이 거의 0까지 짓눌릴 수 있다(명령서 3항이 보고한 "압사" 현상과 정확히
        /// 일치하는 계산 특성). 이 클래스의 다른 레이아웃 요소처럼 `minHeight`를 넉넉히 주는 대신,
        /// 명령서 4항의 두 번째 대안(절대 좌표 고정)을 택해 `LayoutElement.ignoreLayout = true`로
        /// `VerticalLayoutGroup` 계산에서 아예 제외하고, `ContentPanel` 하단에 고정 픽셀 높이
        /// (`ActionContainerHeight`)로 절대 배치한다 - 이러면 Lerp 압사 자체가 구조적으로 불가능해진다.
        /// 가로는 `anchorMin.x`=0/`anchorMax.x`=1로 항상 스트레치돼, 해상도/종횡비가 바뀌어도 버튼
        /// 영역이 화면 밖으로 밀려나지 않는다(명령서 6항).
        /// </summary>
        private static Transform FindOrCreateActionContainer(Transform popupTransform)
        {
            var existingContainer = popupTransform.Find(ActionContainerName);
            Transform containerTransform;
            GameObject containerObject;
            if (existingContainer != null)
            {
                containerTransform = existingContainer;
                containerObject = existingContainer.gameObject;
            }
            else
            {
                containerObject = new GameObject(ActionContainerName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(containerObject, $"Create {ActionContainerName}");
                containerObject.transform.SetParent(popupTransform, false);

                containerTransform = containerObject.transform;
            }

            // 하단 고정, 가로 스트레치, 세로는 항상 ActionContainerHeight 고정 - 재실행 시에도
            // 과거 퍼센트 앵커가 남아있을 수 있어 매번 강제로 재적용한다.
            var rect = (RectTransform)containerTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(0f, ActionContainerHeight);
            rect.anchoredPosition = Vector2.zero;

            if (!containerTransform.TryGetComponent<HorizontalLayoutGroup>(out var layout))
            {
                layout = containerObject.AddComponent<HorizontalLayoutGroup>();
            }
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            if (!containerTransform.TryGetComponent<LayoutElement>(out var layoutElement))
            {
                layoutElement = containerObject.AddComponent<LayoutElement>();
            }
            layoutElement.ignoreLayout = true;

            MoveLegacyButtonIfNeeded(popupTransform, containerTransform, ConfirmButtonName);
            MoveLegacyButtonIfNeeded(popupTransform, containerTransform, CancelButtonName);

            return containerTransform;
        }

        private static void MoveLegacyButtonIfNeeded(Transform popupTransform, Transform containerTransform, string buttonName)
        {
            var legacyButton = popupTransform.Find(buttonName);
            if (legacyButton != null && legacyButton.parent == popupTransform)
            {
                legacyButton.SetParent(containerTransform, false);
            }
        }

        /// <summary>
        /// [TASK-KBO-133] 표준 유니티 스크롤 뷰 계층(`Panel`(ScrollRect) -&gt; `Viewport`(RectMask2D) -&gt;
        /// `Content`(GridLayoutGroup+ContentSizeFitter))으로 재료 목록을 조립한다. 과거 버전은
        /// `Panel` 바로 아래에 `GridLayoutGroup`만 붙은 평면 컨테이너를 뒀는데, 뷰포트/마스크가 없어
        /// 스크롤이 전혀 동작하지 않았고 `Content` 높이도 항상 `Panel` 높이로 고정돼 카드 수에 따라
        /// 늘어나지 않았다. 과거 평면 컨테이너(`containerName`)가 이미 존재하면 삭제하지 않고 새
        /// `Viewport` 하위로 이동시킨다(재생성 아님, 명령서 6항).
        /// </summary>
        private static (GameObject panel, Transform content, Text emptyText) FindOrCreateScrollList(Transform parent,
            string panelName, string containerName, Vector2 cellSize, Vector2 spacing, string emptyMessage)
        {
            var panelTransform = parent.Find(panelName);
            GameObject panelObject;
            if (panelTransform != null)
            {
                panelObject = panelTransform.gameObject;
            }
            else
            {
                panelObject = new GameObject(panelName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(panelObject, $"Create {panelName}");
                panelObject.transform.SetParent(parent, false);

                var rect = (RectTransform)panelObject.transform;
                rect.anchorMin = new Vector2(0f, 0.15f);
                rect.anchorMax = new Vector2(1f, 0.85f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var viewportTransform = panelObject.transform.Find(ViewportName);
            GameObject viewportObject;
            if (viewportTransform != null)
            {
                viewportObject = viewportTransform.gameObject;
            }
            else
            {
                viewportObject = new GameObject(ViewportName, typeof(RectTransform), typeof(RectMask2D));
                Undo.RegisterCreatedObjectUndo(viewportObject, $"Create {ViewportName}");
                viewportObject.transform.SetParent(panelObject.transform, false);

                var viewportRect = (RectTransform)viewportObject.transform;
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = Vector2.zero;
                viewportRect.offsetMax = Vector2.zero;

                // 과거 `Panel` 직속이었던 평면 컨테이너를 새 `Viewport` 하위로 마이그레이션한다.
                var legacyContainer = panelObject.transform.Find(containerName);
                if (legacyContainer != null && legacyContainer.parent == panelObject.transform)
                {
                    legacyContainer.SetParent(viewportObject.transform, false);
                }
            }

            var contentTransform = FindOrCreateGridContent(viewportObject.transform, containerName, cellSize, spacing);

            if (!panelObject.TryGetComponent<ScrollRect>(out var scrollRect))
            {
                scrollRect = panelObject.AddComponent<ScrollRect>();
            }
            scrollRect.viewport = (RectTransform)viewportObject.transform;
            scrollRect.content = (RectTransform)contentTransform;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            var emptyText = FindOrCreateEmptyStateText(panelObject.transform, emptyMessage);

            return (panelObject, contentTransform, emptyText);
        }

        /// <summary>
        /// [TASK-KBO-133] `Content`는 위쪽 기준(anchor/pivot 모두 top)으로 고정하고 `ContentSizeFitter`
        /// (Vertical Fit = Preferred Size)를 붙여, `GridLayoutGroup`이 스폰한 카드 수만큼 세로 크기가
        /// 자동으로 늘어나도록 한다(가로는 `Viewport`에 스트레치돼 고정).
        /// </summary>
        private static Transform FindOrCreateGridContent(Transform parent, string name, Vector2 cellSize, Vector2 spacing)
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
            }

            var rect = (RectTransform)containerObject.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            if (!containerObject.TryGetComponent<GridLayoutGroup>(out var grid))
            {
                grid = containerObject.AddComponent<GridLayoutGroup>();
            }
            grid.cellSize = cellSize;
            grid.spacing = spacing;
            grid.childAlignment = TextAnchor.UpperCenter;

            if (!containerObject.TryGetComponent<ContentSizeFitter>(out var fitter))
            {
                fitter = containerObject.AddComponent<ContentSizeFitter>();
            }
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return containerObject.transform;
        }

        /// <summary>[TASK-KBO-133, 명령서 7항] 재료(중복 카드/아이템)가 0개일 때 대신 노출할 안내 문구.
        /// 기본은 비활성 상태로 두고, `MaterialSelectUIController`가 목록을 채운 뒤 개수에 따라 켠다.</summary>
        private static Text FindOrCreateEmptyStateText(Transform panelTransform, string message)
        {
            var existing = panelTransform.Find(EmptyTextName);
            if (existing != null && existing.TryGetComponent<Text>(out var existingText))
            {
                existingText.text = message;
                existingText.color = Color.black;
                ApplyBestFit(existingText);
                return existingText;
            }

            var textObject = new GameObject(EmptyTextName, typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(textObject, $"Create {EmptyTextName}");
            textObject.transform.SetParent(panelTransform, false);

            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var text = textObject.GetComponent<Text>();
            text.text = message;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;
            ApplyBestFit(text);

            textObject.SetActive(false);

            return text;
        }

        /// <summary>
        /// [명령서 7항 - 안전한 스킵] `SetupScoutUI.cs`(TASK-092)가 만든 `_Templates/PlayerCardTemplate`를
        /// 그대로 재사용한다(중복 조립 대신 기존 완성본 참조 - `PlayerCardUI`의 13개 필드가 이미 전부
        /// 채워져 있다). 원본은 `Button`이 없으므로(스카우트 결과는 선택 불가능한 단순 표시 카드라
        /// 필요 없었음) 이번 용도(각성 재료 다중 선택)에 필요한 `Button`만 없을 때 추가한다 - 기존
        /// 스카우트 화면 동작에는 영향 없다(그쪽은 애초에 Button을 참조하지 않는다). 템플릿 자체가
        /// 아직 없으면(스카우트 UI 배선이 실행된 적 없으면) 에러 없이 경고만 남기고 건너뛴다.
        /// </summary>
        private static PlayerCardUI FindOrCreatePlayerCardTemplate(Transform canvasTransform)
        {
            var holderTransform = canvasTransform.Find(TemplatesHolderName);
            var existingCard = holderTransform != null ? holderTransform.Find(PlayerCardTemplateName) : null;
            if (existingCard == null)
            {
                Debug.LogWarning($"[SetupUpgradeUI] '_Templates/{PlayerCardTemplateName}'를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Setup/Auto-Connect Scout UI'를 실행해 카드 템플릿을 생성하십시오. " +
                    "playerCardPrefab 바인딩을 건너뜁니다.");
                return null;
            }

            if (!existingCard.TryGetComponent<Button>(out var button))
            {
                button = existingCard.gameObject.AddComponent<Button>();
                if (existingCard.TryGetComponent<Image>(out var image)) button.targetGraphic = image;
            }

            return existingCard.GetComponent<PlayerCardUI>();
        }

        /// <summary>강화 재료(Item) 단순 버튼 목록용 템플릿. `_Templates` 하위에 신규 생성한다(없으면).</summary>
        private static Button FindOrCreateItemEntryTemplate(Transform canvasTransform)
        {
            var holderTransform = FindOrCreateTemplatesHolder(canvasTransform);

            var existing = holderTransform.Find(ItemEntryTemplateName);
            if (existing != null)
            {
                var existingButton = existing.GetComponent<Button>();
                if (existingButton != null) return existingButton;
            }

            var buttonObject = new GameObject(ItemEntryTemplateName, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {ItemEntryTemplateName}");
            buttonObject.transform.SetParent(holderTransform, false);

            var rect = (RectTransform)buttonObject.transform;
            rect.sizeDelta = new Vector2(160f, 60f);

            var image = buttonObject.GetComponent<Image>();
            image.color = Color.white;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(labelObject, $"Create {ItemEntryTemplateName} Label");
            labelObject.transform.SetParent(buttonObject.transform, false);

            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = labelObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 14;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;
            ApplyBestFit(text);

            return button;
        }

        /// <summary>`SetupScoutUI.cs`/`SetupInGameUI.cs`가 이미 공유 중인 캔버스 직속 `_Templates` 홀더를
        /// 그대로 재사용한다(이름이 같으면 항상 재사용 - 명령서 7항 중복 생성 방지).</summary>
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

        private static void BindController(MaterialSelectUIController controller, GameActionController gameActionController,
            Text titleText, Text selectionCountText, Button confirmButton, Button cancelButton,
            GameObject playerListPanel, Transform playerListContainer, PlayerCardUI playerCardPrefab, Text playerListEmptyText,
            GameObject itemListPanel, Transform itemListContainer, Button itemEntryPrefab, Text itemListEmptyText)
        {
            var serialized = new SerializedObject(controller);

            if (gameActionController != null) serialized.FindProperty("gameActionController").objectReferenceValue = gameActionController;

            serialized.FindProperty("popupRoot").objectReferenceValue = controller.gameObject;
            serialized.FindProperty("titleText").objectReferenceValue = titleText;
            serialized.FindProperty("selectionCountText").objectReferenceValue = selectionCountText;
            serialized.FindProperty("confirmButton").objectReferenceValue = confirmButton;
            serialized.FindProperty("cancelButton").objectReferenceValue = cancelButton;

            serialized.FindProperty("playerListPanel").objectReferenceValue = playerListPanel;
            serialized.FindProperty("playerListContainer").objectReferenceValue = playerListContainer;
            if (playerCardPrefab != null) serialized.FindProperty("playerCardPrefab").objectReferenceValue = playerCardPrefab;
            serialized.FindProperty("playerListEmptyText").objectReferenceValue = playerListEmptyText;

            serialized.FindProperty("itemListPanel").objectReferenceValue = itemListPanel;
            serialized.FindProperty("itemListContainer").objectReferenceValue = itemListContainer;
            serialized.FindProperty("itemEntryPrefab").objectReferenceValue = itemEntryPrefab;
            serialized.FindProperty("itemListEmptyText").objectReferenceValue = itemListEmptyText;

            serialized.ApplyModifiedProperties();
        }

        private static Button FindOrCreateButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax)
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
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 18;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;
            ApplyButtonLabel(button, label);

            return button;
        }

        /// <summary>[TASK-KBO-123] 이미 존재하는 버튼을 재사용할 때도 라벨 텍스트/색상/가독성 옵션을
        /// 최신 값으로 강제 갱신한다(TASK-117이 확립한 관례 재사용).</summary>
        private static void ApplyButtonLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>(true);
            if (text == null) return;

            text.text = label;
            text.color = Color.black;
            ApplyBestFit(text);
        }

        /// <summary>[TASK-KBO-134, 명령서 4항] `resizeTextForBestFit`을 켜고 최소/최대 크기를 큼직하게
        /// 고정해, 텍스트가 담긴 사각형 크기에 맞춰 자동으로 확대/축소되면서도 항상 읽기 쉬운 크기를
        /// 유지하도록 한다.</summary>
        private static void ApplyBestFit(Text text)
        {
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = BestFitMinSize;
            text.resizeTextMaxSize = BestFitMaxSize;
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
            text.alignment = TextAnchor.MiddleCenter;
            // [TASK-KBO-128] ContentPanel 신설로 이 텍스트의 실제 배경이 불투명 흰색이 되어(딤 배경
            // 위가 아님), 검은색으로 대비시킨다 - 흰색으로 두면 흰 배경 위에서 안 보인다.
            text.color = Color.black;
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;
            ApplyBestFit(text);

            return text;
        }
    }
}
