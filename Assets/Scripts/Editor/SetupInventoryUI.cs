using KBOManager.Controllers;
using KBOManager.Managers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-110] TASK-109가 지적한 "진입점 부재"를 해소한다 - `InventoryUIController`(원문 14개
    /// `[SerializeField]` 전수 확인)를 씬에 조립하고, 씬에 이미 존재하는(TASK-109)
    /// `MaterialSelectUIController`를 `materialSelectUIController` 필드에 연결해 [강화하기]/[각성하기]
    /// 버튼이 실제로 그 팝업을 열 수 있게 한다. 카드 프리팹은 `SetupScoutUI.cs`(TASK-092)가 만든
    /// `_Templates/PlayerCardTemplate`을 그대로 재사용한다(TASK-109가 이미 `Button`을 추가해 둬서
    /// `InventoryUIController.SpawnCard()`의 `GetComponent&lt;Button&gt;()` 요구도 그대로 충족된다).
    ///
    /// [결정 필요 아님, 명령서 4항 이행을 위한 최소 코드 변경] "로비 화면에 진입 버튼을 생성하고 리스너
    /// 연결"을 위해서는 `LeagueDashboardUIController`에 그 버튼을 담을 필드가 있어야 하는데,
    /// `shopButton`/`leagueStatsButton`(TASK-103/107/108)과 달리 인벤토리용 필드는 애초에 존재하지
    /// 않았다(원문 55~74행 재확인, `ScreenType.Inventory`는 이미 예약돼 있었으나(62~63행 주석) 이를 쓰는
    /// 버튼 필드가 없었음). `scoutButton`/`rosterButton` 등 기존 6개 버튼과 완전히 동일한 관례로
    /// `inventoryButton` 필드 1개와 `Awake()`의 리스너 1줄만 최소 추가했다(`LeagueDashboardUIController.cs`,
    /// 비즈니스 로직 무관 - 명령서 5항이 금지한 "인벤토리 정렬/필터링/렌더링 로직"이 아니다).
    ///
    /// [결정 필요, 미해소] `InventoryUIController`에는 상세 패널을 닫거나 인벤토리 화면 자체에서 로비로
    /// 돌아가는 버튼 필드가 원문에 전혀 없다(`ScoutUIController.closeButton`과 달리) - `CloseDetail()`은
    /// public이지만 이를 호출하는 UI 트리거가 설계돼 있지 않다. 명령서 5항이 "비즈니스 로직 수정 금지"를
    /// 명시해 새 필드를 추가로 발명하지 않고 이 상태 그대로 두었다 - 한 번 인벤토리 화면에 진입하면
    /// (다른 화면의 진입 버튼을 다시 누르기 전까지는) 상세 패널이나 화면 자체를 닫을 UI 수단이 없다.
    /// (위 문단은 TASK-111에서 해소됨 - closeButton/closeDetailButton 필드 및 리스너 추가.)
    ///
    /// [TASK-KBO-124, 사실 정정] `InventoryUIController.cs` 원문(60~69/281~293행)을 재확인한 결과
    /// `enhanceButton`/`awakenButton`/`skillChangeButton`은 `Awake()`에 이미 리스너가 연결돼 있고
    /// (`OnClickEnhance()`/`OnClickAwaken()`이 캐싱된 `selectedPlayer`로 `materialSelectUIController.
    /// OpenForEnhance()`/`OpenForAwaken()`을 이미 호출), 라벨("강화하기"/"각성하기"/"스킬 변경")과 검은색
    /// 폰트도 이 파일의 `FindOrCreateButton()`이 생성 시점부터 이미 채우고 있었다 - "하얀 백지에 클릭도
    /// 안 됨"이라는 명령서 3항 전제와 달리 `InventoryUIController.cs`는 이 부분을 수정할 필요가 없었다.
    /// 실제 확인된 문제는 `closeButton`/`closeDetailButton`이 같은 화면 좌표(0.85,0.92~1,1)를 공유해
    /// `CloseButton`(더 나중에 생성돼 sibling index가 높음)이 `CloseDetailButton`을 항상 가리고 클릭을
    /// 가로채던 것과, `OnDisable()`에 `CloseDetail()` 호출이 없어 상세 패널 상태가 누수되던 것 2가지였다.
    ///
    /// [TASK-KBO-136] `CloseDetailButton`을 크고 투박한 "상세 닫기" 텍스트 버튼에서 `DetailPanel`
    /// 우측 상단의 컴팩트한 64x64 'X' 버튼(`FindOrCreateCloseButton()`)으로 교체했다. `InventoryUIController.
    /// ShowDetail()`이 상세 패널을 열 때 메인 `closeButton`을 이미 숨기므로(TASK-135), 두 버튼이 같은
    /// 모서리를 공유해도 겹쳐 보이지 않는다.
    ///
    /// [TASK-KBO-138] 카드 목록(`CardContainer`)이 평면 `GridLayoutGroup`만 갖고 있어 카드가 화면을
    /// 넘어가도 스크롤되지 않던 문제를, `SetupUpgradeUI.cs`(TASK-133)와 동일한 표준 스크롤 뷰 계층
    /// (`CardListPanel`(ScrollRect) -&gt; `Viewport`(RectMask2D) -&gt; `Content`=`CardContainer`
    /// (GridLayoutGroup+ContentSizeFitter))으로 재조립해 해소했다(`FindOrCreateCardListPanel()`).
    ///
    /// [TASK-KBO-141, 근본 원인 확정] TASK-139/140이 반복 보고받은 "닫기 X 버튼이 카드 리스트에
    /// 가려짐" 증상의 실제 원인을 특정했다 - TASK-138이 신설한 `FindOrCreateCardListPanel()`은
    /// `AutoConnectInventoryUI()` 맨 처음에 호출되는데, `new GameObject(...).transform.SetParent(parent,
    /// false)`는 유니티 기본 동작상 대상을 부모의 "마지막(=최상단 렌더링) 자식"으로 붙인다. TASK-138
    /// 이전 상태(CardContainer/DetailPanel/CloseButton이 이미 구 순서로 존재하는 씬)에서 이 새 코드를
    /// 처음 실행하면, 새로 만들어진 `CardListPanel`이 기존 `CloseButton`/`DetailPanel`보다 더 나중
    /// sibling이 되어 그 위를 덮어버린다 - `ShowDetail()`의 런타임 `SetAsLastSibling()`(TASK-139)은
    /// DetailPanel이 열릴 때만 작동하는 런타임 대응이라 이 씬 저작 시점의 정적 순서 문제 자체는 고치지
    /// 못했다. `FindOrCreateCardListPanel()`이 자신을 `SetAsFirstSibling()`으로, `CloseButton`/
    /// `DetailPanel`을 `SetAsLastSibling()`으로 양방향에서 강제해 씬의 과거 상태와 무관하게 항상 올바른
    /// 순서가 되도록 고쳤다.
    /// </summary>
    public static class SetupInventoryUI
    {
        private const string PanelName = "InventoryPanel";
        // [TASK-KBO-138] 카드 목록을 감싸는 표준 스크롤 뷰 계층(Panel-Viewport-Content). CardContainerName은
        // 이제 "Content"(GridLayoutGroup+ContentSizeFitter) 오브젝트 이름으로 쓰인다(마이그레이션 대상).
        private const string CardListPanelName = "CardListPanel";
        private const string ViewportName = "Viewport";
        private const string CardContainerName = "CardContainer";
        private const string DetailPanelName = "DetailPanel";
        private const string DetailPreviewCardName = "DetailPreviewCard";
        private const string DetailReinforceTextName = "DetailReinforceText";
        private const string DetailAwakenTextName = "DetailAwakenText";
        private const string DetailSkillsTextName = "DetailSkillsText";
        private const string EnhanceButtonName = "EnhanceButton";
        private const string AwakenButtonName = "AwakenButton";
        private const string SkillChangeButtonName = "SkillChangeButton";
        private const string SkillRerollResultTextName = "SkillRerollResultText";
        private const string DetailCardFlashImageName = "DetailCardFlashImage";
        private const string InventoryButtonName = "InventoryButton";
        private const string CloseButtonName = "CloseButton";
        private const string CloseDetailButtonName = "CloseDetailButton";
        private const string TemplatesHolderName = "_Templates";
        private const string PlayerCardTemplateName = "PlayerCardTemplate";

        [MenuItem("KBO Manager/Setup/Auto-Connect Inventory UI")]
        public static void AutoConnectInventoryUI()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[SetupInventoryUI] 씬에서 Canvas를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Initialize Current Scene'을 실행하십시오.");
                return;
            }

            var controller = FindOrCreateInventoryPanel(canvas.transform);

            var cardContainer = FindOrCreateCardListPanel(controller.transform,
                new Vector2(140f, 200f), new Vector2(10f, 10f));
            var cardPrefab = FindOrCreatePlayerCardTemplate(canvas.transform);

            var detailPanelRoot = FindOrCreateDetailPanel(controller.transform);
            var detailPreviewCard = FindOrCreateDetailPreviewCard(detailPanelRoot.transform, cardPrefab);
            var detailReinforceText = FindOrCreateText(detailPanelRoot.transform, DetailReinforceTextName, "",
                new Vector2(0.35f, 0.6f), new Vector2(1f, 0.68f));
            var detailAwakenText = FindOrCreateText(detailPanelRoot.transform, DetailAwakenTextName, "",
                new Vector2(0.35f, 0.5f), new Vector2(1f, 0.58f));
            var detailSkillsText = FindOrCreateText(detailPanelRoot.transform, DetailSkillsTextName, "",
                new Vector2(0.35f, 0.4f), new Vector2(1f, 0.48f));
            var skillRerollResultText = FindOrCreateText(detailPanelRoot.transform, SkillRerollResultTextName, "",
                new Vector2(0.35f, 0.3f), new Vector2(1f, 0.38f));

            var enhanceButton = FindOrCreateButton(detailPanelRoot.transform, EnhanceButtonName, "강화하기",
                new Vector2(0.35f, 0.15f), new Vector2(0.55f, 0.25f));
            var awakenButton = FindOrCreateButton(detailPanelRoot.transform, AwakenButtonName, "각성하기",
                new Vector2(0.57f, 0.15f), new Vector2(0.77f, 0.25f));
            var skillChangeButton = FindOrCreateButton(detailPanelRoot.transform, SkillChangeButtonName, "스킬 변경",
                new Vector2(0.35f, 0.03f), new Vector2(0.55f, 0.13f));

            var detailCardFlashImage = FindOrCreateImage(detailPanelRoot.transform, DetailCardFlashImageName,
                new Vector2(0.05f, 0.4f), new Vector2(0.3f, 0.9f));

            // [TASK-KBO-111] 인벤토리 화면 자체를 닫는 버튼(InventoryPanel 우측 상단)과 상세 패널만
            // 닫는 버튼(DetailPanel 우측 상단)을 각각 배치한다(명령서 5항 - 대략적인 우측 상단 앵커만).
            //
            // [TASK-KBO-124] 두 버튼이 원래 같은 앵커(0.85,0.92~1,1)를 썼는데, DetailPanel이 CloseButton
            // 보다 먼저 생성돼(70행 FindOrCreateDetailPanel, 93행보다 앞) InventoryPanel 하위 형제 순서상
            // CloseButton이 더 나중(=위쪽 sibling index)이라 항상 CloseDetailButton 위에 그려져 클릭을
            // 가로채고 있었다 - 상세 패널만 닫으려 눌러도 매번 로비로 이동해 버리는 원인이었다.
            // CloseDetailButton을 CloseButton과 겹치지 않는 바로 왼쪽 칸으로 옮겨 해소한다.
            var closeButton = FindOrCreateButton(controller.transform, CloseButtonName, "닫기",
                new Vector2(0.85f, 0.92f), new Vector2(1f, 1f));
            // [TASK-KBO-136] "상세 닫기" 텍스트 버튼(TASK-125)을 폐기하고, DetailPanel 우측 상단
            // 모서리에 컴팩트한 고정 크기 'X' 버튼으로 대체한다. InventoryUIController.ShowDetail()이
            // 상세 패널을 열 때 위 closeButton을 이미 숨기므로(TASK-135), 같은 우측 상단 모서리를
            // 공유해도 두 버튼이 동시에 겹쳐 보일 일이 없다.
            var closeDetailButton = FindOrCreateCloseButton(detailPanelRoot.transform, CloseDetailButtonName, 64f);

            // [TASK-KBO-141, 명령서 4항] `FindOrCreateCardListPanel()`이 자신을 맨 앞으로 강제하는 것과
            // 대칭으로, 이 두 버튼(과 DetailPanel)은 항상 맨 뒤(=렌더링 최상단)로 강제한다 - 씬 상태가
            // 어떻든 두 방향에서 순서를 강제로 고정하므로 어느 한쪽만으로도 이미 충분하지만, "코드로
            // 강제 해결"이라는 명령서 취지에 맞춰 이중으로 보증한다.
            closeButton.transform.SetAsLastSibling();
            detailPanelRoot.transform.SetAsLastSibling();

            var materialSelectUIController = Object.FindAnyObjectByType<MaterialSelectUIController>(FindObjectsInactive.Include);
            if (materialSelectUIController == null)
            {
                Debug.LogWarning("[SetupInventoryUI] 씬에서 MaterialSelectUIController를 찾지 못해 " +
                    "materialSelectUIController 바인딩을 건너뜁니다. 먼저 'KBO Manager/Setup/" +
                    "Auto-Connect Upgrade UI'(TASK-KBO-109)를 실행하십시오.");
            }

            var gameActionController = Object.FindAnyObjectByType<GameActionController>(FindObjectsInactive.Include);
            if (gameActionController == null)
            {
                Debug.LogWarning("[SetupInventoryUI] 씬에서 GameActionController를 찾지 못해 " +
                    "gameActionController 바인딩을 건너뜁니다.");
            }

            BindController(controller, materialSelectUIController, gameActionController, cardContainer, cardPrefab,
                detailPanelRoot, detailPreviewCard, detailReinforceText, detailAwakenText, detailSkillsText,
                enhanceButton, awakenButton, skillChangeButton, skillRerollResultText, detailCardFlashImage,
                closeButton, closeDetailButton);

            EditorUtility.SetDirty(controller);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager == null)
            {
                Debug.LogWarning("[SetupInventoryUI] 씬에서 UIManager를 찾지 못해 screens 등록을 건너뜁니다.");
            }
            else
            {
                RegisterInventoryScreen(uiManager, controller.gameObject);
                EditorUtility.SetDirty(uiManager);
            }

            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupInventoryUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "로비 진입 버튼 생성을 건너뜁니다.");
            }
            else
            {
                // 기존 좌측 하단 버튼 열(manageCheerleaderButton 20/scoutButton 80/rosterButton 140/
                // quickPlayButton 200/shopButton 260/leagueStatsButton 320, SetupLeagueUI.cs 참고)에
                // 이어 380에 배치한다.
                var inventoryButton = FindOrCreateDashboardButton(dashboard.transform, InventoryButtonName,
                    "선수 관리", new Vector2(20f, 380f));
                BindButtonField(dashboard, "inventoryButton", inventoryButton);
                EditorUtility.SetDirty(dashboard);
            }

            var scene = controller.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            // [TASK-KBO-141, 명령서 6항] Z-Order가 실제로 의도대로 고정됐는지 sibling index를 직접
            // 읽어 콘솔에 증명한다 - CardListPanel(카드 스크롤 뷰)의 인덱스가 closeButton/DetailPanel의
            // 인덱스보다 반드시 작아야(=더 먼저 그려져야) 두 버튼이 카드 리스트 위에 보인다.
            int cardListIndex = cardContainer.parent.parent.GetSiblingIndex(); // Content -> Viewport -> CardListPanel
            int closeButtonIndex = closeButton.transform.GetSiblingIndex();
            int detailPanelIndex = detailPanelRoot.transform.GetSiblingIndex();
            bool zOrderOk = cardListIndex < closeButtonIndex && cardListIndex < detailPanelIndex;

            if (zOrderOk)
            {
                Debug.Log($"[SetupInventoryUI] Inventory Z-Order 갱신 완료 - CardListPanel(index {cardListIndex}) < " +
                    $"CloseButton(index {closeButtonIndex}), DetailPanel(index {detailPanelIndex}).");
            }
            else
            {
                Debug.LogError($"[SetupInventoryUI] Inventory Z-Order 검증 실패 - CardListPanel(index {cardListIndex})가 " +
                    $"CloseButton(index {closeButtonIndex}) 또는 DetailPanel(index {detailPanelIndex})보다 뒤에 있습니다. " +
                    "씬을 직접 확인하십시오.");
            }

            Debug.Log("[SetupInventoryUI] 인벤토리 UI 자동 배선 완료.");
        }

        private static InventoryUIController FindOrCreateInventoryPanel(Transform canvasTransform)
        {
            var existing = canvasTransform.Find(PanelName);
            if (existing != null)
            {
                var existingController = existing.GetComponent<InventoryUIController>();
                if (existingController != null) return existingController;
            }

            var panelObject = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {PanelName}");
            panelObject.transform.SetParent(canvasTransform, false);

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panelObject.GetComponent<Image>().color = Color.white;

            return panelObject.AddComponent<InventoryUIController>();
        }

        /// <summary>
        /// [TASK-KBO-138] 인벤토리 카드 목록을 표준 유니티 스크롤 뷰 계층(`Panel`(ScrollRect) -&gt;
        /// `Viewport`(RectMask2D) -&gt; `Content`(GridLayoutGroup+ContentSizeFitter))으로 조립한다.
        /// 과거 버전은 `InventoryPanel` 바로 아래에 `GridLayoutGroup`만 붙은 평면 `CardContainer`를
        /// 뒀는데, 뷰포트/마스크가 없어 카드가 화면을 넘어가도 스크롤이 전혀 동작하지 않았다
        /// (`SetupUpgradeUI.cs`의 `FindOrCreateScrollList()`, TASK-133과 동일한 원인/해법).
        /// 과거 평면 `CardContainer`가 이미 존재하면 삭제하지 않고 새 `Viewport` 하위로 이동시킨다
        /// (재생성 아님, 명령서 7항).
        /// </summary>
        private static Transform FindOrCreateCardListPanel(Transform parent, Vector2 cellSize, Vector2 spacing)
        {
            var panelTransform = parent.Find(CardListPanelName);
            GameObject panelObject;
            if (panelTransform != null)
            {
                panelObject = panelTransform.gameObject;
            }
            else
            {
                panelObject = new GameObject(CardListPanelName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(panelObject, $"Create {CardListPanelName}");
                panelObject.transform.SetParent(parent, false);

                var rect = (RectTransform)panelObject.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
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

                // 과거 InventoryPanel 직속이었던 평면 CardContainer를 새 Viewport 하위로 마이그레이션한다.
                var legacyContainer = parent.Find(CardContainerName);
                if (legacyContainer != null && legacyContainer.parent == parent)
                {
                    legacyContainer.SetParent(viewportObject.transform, false);
                }
            }

            var contentTransform = FindOrCreateGridContent(viewportObject.transform, CardContainerName, cellSize, spacing);

            if (!panelObject.TryGetComponent<ScrollRect>(out var scrollRect))
            {
                scrollRect = panelObject.AddComponent<ScrollRect>();
            }
            scrollRect.viewport = (RectTransform)viewportObject.transform;
            scrollRect.content = (RectTransform)contentTransform;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;

            // [TASK-KBO-141, 근본 원인 확정] 이 메서드가 새 GameObject를 만들 때 `SetParent(parent, false)`는
            // 유니티 기본 동작상 "부모의 마지막(=최상단 렌더링) 자식"으로 붙인다 - 이 함수는 항상
            // `AutoConnectInventoryUI()`의 맨 처음에 호출되므로, 만약 InventoryPanel에 예전 실행에서 만든
            // CloseButton/DetailPanel이 이미 존재하는 씬에서 처음 이 새 버전을 실행하면(TASK-138 이전 ->
            // 이후 마이그레이션 시점) CardListPanel이 그 뒤(=그 위)에 추가되어 CloseButton/DetailPanel을
            // 완전히 덮어버린다 - TASK-139/140이 보고받은 "닫기 X 버튼이 카드 리스트에 가려짐" 증상의
            // 실제 원인이다(사실 확인, 코드 우기기 아님). 재실행마다 무조건 맨 앞(=렌더링 최하단)으로
            // 강제해 이후에 생성/발견되는 CloseButton/DetailPanel이 항상 그 위에 그려지도록 한다.
            panelObject.transform.SetAsFirstSibling();

            return contentTransform;
        }

        /// <summary>`Content`는 위쪽 기준(anchor/pivot 모두 top)으로 고정하고 `ContentSizeFitter`
        /// (Vertical Fit = Preferred Size)를 붙여, `GridLayoutGroup`이 스폰한 카드 수만큼 세로 크기가
        /// 자동으로 늘어나도록 한다(가로는 `Viewport`에 스트레치돼 고정). 재실행 시에도 최신 값이
        /// 반영되도록 cellSize/spacing을 매번 무조건 재적용한다.</summary>
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

            if (!containerObject.TryGetComponent<ContentSizeFitter>(out var fitter))
            {
                fitter = containerObject.AddComponent<ContentSizeFitter>();
            }
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return containerObject.transform;
        }

        /// <summary>
        /// [명령서 6항 - 템플릿 재사용] `SetupScoutUI.cs`(TASK-092)가 만든 `_Templates/PlayerCardTemplate`을
        /// 그대로 재사용한다(중복 조립 대신 기존 완성본 참조). `Button`은 TASK-109(`SetupUpgradeUI.cs`)가
        /// 이미 없을 때만 추가해 둔 상태라 `InventoryUIController.SpawnCard()`의 `GetComponent&lt;Button&gt;()`
        /// 요구도 이미 충족돼 있다 - 여기서는 존재 여부만 확인하고 없으면 경고 후 건너뛴다(명령서 7항).
        /// </summary>
        private static PlayerCardUI FindOrCreatePlayerCardTemplate(Transform canvasTransform)
        {
            var holderTransform = canvasTransform.Find(TemplatesHolderName);
            var existingCard = holderTransform != null ? holderTransform.Find(PlayerCardTemplateName) : null;
            if (existingCard == null)
            {
                Debug.LogWarning($"[SetupInventoryUI] '_Templates/{PlayerCardTemplateName}'를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Setup/Auto-Connect Scout UI'를 실행해 카드 템플릿을 생성하십시오. " +
                    "cardPrefab 바인딩을 건너뜁니다.");
                return null;
            }

            if (!existingCard.TryGetComponent<Button>(out _))
            {
                var button = existingCard.gameObject.AddComponent<Button>();
                if (existingCard.TryGetComponent<Image>(out var image)) button.targetGraphic = image;
            }

            return existingCard.GetComponent<PlayerCardUI>();
        }

        /// <summary>상세 패널. 평소 비활성 상태로 둔다 - `InventoryUIController.Awake()`가 `CloseDetail()`로
        /// 다시 한번 비활성화하지만, 라이브 에디터에서 메뉴 실행 직후 미리보기 화면이 어색하게 뜨지
        /// 않도록 생성 시점에도 명시적으로 꺼 둔다(`ScoutUIController.resultPopupRoot`와 동일한 관례).</summary>
        private static GameObject FindOrCreateDetailPanel(Transform parent)
        {
            var existing = parent.Find(DetailPanelName);
            if (existing != null) return existing.gameObject;

            var panelObject = new GameObject(DetailPanelName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {DetailPanelName}");
            panelObject.transform.SetParent(parent, false);

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panelObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);
            panelObject.SetActive(false);

            return panelObject;
        }

        /// <summary>`cardPrefab`(템플릿)을 복제해 상세 패널 전용 미리보기 카드 인스턴스를 만든다
        /// (풀링 대상인 목록 카드와 달리 항상 하나만 존재하는 고정 인스턴스). 템플릿을 찾지 못했으면
        /// (cardPrefab == null) 미리보기 카드도 만들지 않고 null을 반환한다(명령서 7항 - 안전한 스킵).</summary>
        private static PlayerCardUI FindOrCreateDetailPreviewCard(Transform detailPanelTransform, PlayerCardUI cardPrefab)
        {
            var existing = detailPanelTransform.Find(DetailPreviewCardName);
            if (existing != null)
            {
                var existingCard = existing.GetComponent<PlayerCardUI>();
                if (existingCard != null) return existingCard;
            }

            if (cardPrefab == null) return null;

            var instance = Object.Instantiate(cardPrefab, detailPanelTransform);
            Undo.RegisterCreatedObjectUndo(instance.gameObject, $"Create {DetailPreviewCardName}");
            instance.gameObject.name = DetailPreviewCardName;

            var rect = (RectTransform)instance.transform;
            rect.anchorMin = new Vector2(0.05f, 0.4f);
            rect.anchorMax = new Vector2(0.3f, 0.9f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return instance;
        }

        private static Image FindOrCreateImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null && existingChild.TryGetComponent<Image>(out var existingImage)) return existingImage;

            var imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(imageObject, $"Create {name}");
            imageObject.transform.SetParent(parent, false);

            var rect = (RectTransform)imageObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = imageObject.GetComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0f); // 평소엔 투명 - VFXController가 강화 연출 시에만 색을 바꿔 재생한다.
            image.raycastTarget = false;

            return image;
        }

        private static void RegisterInventoryScreen(UIManager uiManager, GameObject inventoryRoot)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                var typeProperty = element.FindPropertyRelative("Type");
                if (typeProperty.intValue == (int)ScreenType.Inventory)
                {
                    element.FindPropertyRelative("Root").objectReferenceValue = inventoryRoot;
                    serializedManager.ApplyModifiedProperties();
                    return;
                }
            }

            int newIndex = screensProperty.arraySize;
            screensProperty.arraySize++;

            var newElement = screensProperty.GetArrayElementAtIndex(newIndex);
            newElement.FindPropertyRelative("Type").intValue = (int)ScreenType.Inventory;
            newElement.FindPropertyRelative("Root").objectReferenceValue = inventoryRoot;

            serializedManager.ApplyModifiedProperties();
        }

        private static void BindController(InventoryUIController controller,
            MaterialSelectUIController materialSelectUIController, GameActionController gameActionController,
            Transform cardContainer, PlayerCardUI cardPrefab, GameObject detailPanelRoot, PlayerCardUI detailPreviewCard,
            Text detailReinforceText, Text detailAwakenText, Text detailSkillsText,
            Button enhanceButton, Button awakenButton, Button skillChangeButton,
            Text skillRerollResultText, Image detailCardFlashImage,
            Button closeButton, Button closeDetailButton)
        {
            var serialized = new SerializedObject(controller);

            if (materialSelectUIController != null) serialized.FindProperty("materialSelectUIController").objectReferenceValue = materialSelectUIController;
            if (gameActionController != null) serialized.FindProperty("gameActionController").objectReferenceValue = gameActionController;

            serialized.FindProperty("cardContainer").objectReferenceValue = cardContainer;
            if (cardPrefab != null) serialized.FindProperty("cardPrefab").objectReferenceValue = cardPrefab;

            serialized.FindProperty("detailPanelRoot").objectReferenceValue = detailPanelRoot;
            if (detailPreviewCard != null) serialized.FindProperty("detailPreviewCard").objectReferenceValue = detailPreviewCard;
            serialized.FindProperty("detailReinforceText").objectReferenceValue = detailReinforceText;
            serialized.FindProperty("detailAwakenText").objectReferenceValue = detailAwakenText;
            serialized.FindProperty("detailSkillsText").objectReferenceValue = detailSkillsText;
            serialized.FindProperty("enhanceButton").objectReferenceValue = enhanceButton;
            serialized.FindProperty("awakenButton").objectReferenceValue = awakenButton;
            serialized.FindProperty("skillChangeButton").objectReferenceValue = skillChangeButton;
            serialized.FindProperty("skillRerollResultText").objectReferenceValue = skillRerollResultText;
            serialized.FindProperty("detailCardFlashImage").objectReferenceValue = detailCardFlashImage;

            serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
            serialized.FindProperty("closeDetailButton").objectReferenceValue = closeDetailButton;

            serialized.ApplyModifiedProperties();
        }

        private static void BindButtonField(LeagueDashboardUIController dashboard, string fieldName, Button button)
        {
            var serialized = new SerializedObject(dashboard);
            serialized.FindProperty(fieldName).objectReferenceValue = button;
            serialized.ApplyModifiedProperties();
        }

        private static Button FindOrCreateDashboardButton(Transform parent, string name, string label, Vector2 anchoredPosition)
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
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 18;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return button;
        }

        private static Button FindOrCreateButton(Transform parent, string name, string label, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingButton = existingChild.GetComponent<Button>();
                if (existingButton != null)
                {
                    // [TASK-KBO-124] 이미 존재하는 버튼이어도 앵커/라벨을 최신 값으로 강제 갱신한다 -
                    // CloseDetailButton은 앵커가 바뀌었으므로 재실행 시 반드시 새 위치로 옮겨져야 한다.
                    var existingRect = (RectTransform)existingButton.transform;
                    existingRect.anchorMin = anchorMin;
                    existingRect.anchorMax = anchorMax;
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
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;
            ApplyButtonLabel(button, label);

            return button;
        }

        /// <summary>
        /// [TASK-KBO-136] 우측 상단 모서리에 고정 픽셀 크기(`size`x`size`)의 단순 'X' 버튼을 절대
        /// 배치한다(`SetupUpgradeUI.FindOrCreateCloseButton()`과 동일한 패턴 - 각 Setup*.cs 파일이
        /// 자체 헬퍼를 갖는 이 코드베이스 관례를 따라 이 파일에도 독립적으로 둔다). `DetailPanel`에는
        /// 현재 레이아웃 그룹이 없어 `LayoutElement.ignoreLayout`이 당장은 아무 효과가 없지만, 추후
        /// 레이아웃 그룹이 추가되더라도 이 버튼만은 항상 절대 위치를 유지하도록 미리 방어해 둔다.
        /// </summary>
        private static Button FindOrCreateCloseButton(Transform parent, string name, float size)
        {
            var existingChild = parent.Find(name);
            Button button;
            GameObject buttonObject;
            if (existingChild != null && existingChild.TryGetComponent<Button>(out var existingButton))
            {
                button = existingButton;
                buttonObject = existingChild.gameObject;
            }
            else
            {
                buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
                Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
                buttonObject.transform.SetParent(parent, false);

                var image = buttonObject.GetComponent<Image>();
                image.color = new Color(0.9f, 0.9f, 0.9f);

                button = buttonObject.GetComponent<Button>();
                button.targetGraphic = image;
            }

            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(-12f, -12f);

            if (!buttonObject.TryGetComponent<LayoutElement>(out var layoutElement))
            {
                layoutElement = buttonObject.AddComponent<LayoutElement>();
            }
            layoutElement.ignoreLayout = true;

            var labelTransform = buttonObject.transform.Find("Label");
            Text text;
            if (labelTransform != null && labelTransform.TryGetComponent<Text>(out var existingText))
            {
                text = existingText;
            }
            else
            {
                var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
                Undo.RegisterCreatedObjectUndo(labelObject, $"Create {name} Label");
                labelObject.transform.SetParent(buttonObject.transform, false);

                var labelRect = (RectTransform)labelObject.transform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;

                text = labelObject.GetComponent<Text>();
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.raycastTarget = false;
            }

            text.text = "X";
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 28;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 18;
            text.resizeTextMaxSize = 40;

            return button;
        }

        /// <summary>[TASK-KBO-124] 이미 존재하는 버튼을 재사용할 때도 라벨 텍스트/색상/가독성 옵션을
        /// 최신 값으로 강제 갱신한다(TASK-117/118/123이 확립한 관례 재사용).</summary>
        private static void ApplyButtonLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>(true);
            if (text == null) return;

            text.text = label;
            text.color = Color.black;
            text.resizeTextForBestFit = true;
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
            text.color = Color.white; // 상세 패널 배경(반투명 검정)과 대비시킨다.
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }
    }
}
