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
    ///
    /// [TASK-KBO-142] TASK-141이 커밋됐다는 사실이 "라이브 씬에 반영됐다"는 뜻은 아니다 - 이 파일은
    /// 에디터 메뉴(Auto-Connect Inventory UI)이고, 사용자의 Unity 에디터 프로세스가 이 작업 세션
    /// 내내(수 시간) 계속 열려 있어(작업 로그로 확인) 그 사이 커밋된 어떤 Setup*.cs 변경도 사용자가
    /// 직접 메뉴를 재실행하기 전까지는 씬에 전혀 반영되지 않는다 - "커밋했다"와 "씬이 고쳐졌다"는
    /// 서로 다른 문장이다. 이번 작업은 TASK-141의 SetAsFirstSibling/SetAsLastSibling 순서 강제(여전히
    /// 유효, 그대로 둠)에 더해, InventoryPanel의 모든 직속 자식을 매 실행마다 무조건 DestroyImmediate로
    /// 전부 지운 뒤 올바른 순서(스크롤 뷰 -> 상세 패널/버튼)로 처음부터 다시 만들도록 강화했다(명령서
    /// 0/4항) - 씬에 어떤 과거 세대의 잔재가 있었든 이 재조립 시점부터는 완전히 무관해진다.
    /// LogInventorySiblingDump()로 InventoryPanel 직속 자식 전체의 이름+순서를 콘솔에 덤프해 사용자가
    /// 메뉴 실행 즉시 눈으로 확인할 수 있게 했다(명령서 6항).
    ///
    /// [TASK-KBO-147, 전면 개편] 명령서 지시대로 텍스트 나열형 구 `DetailPanel`(강화/각성 수치 4줄 +
    /// 강화하기/각성하기/스킬 변경 버튼 3개)을 완전히 폐기했다 - `AutoConnectInventoryUI()`가 이미 매
    /// 실행마다 `InventoryPanel`의 모든 직속 자식을 `DestroyImmediate`하므로(TASK-142), `DetailPanel`도
    /// 그 시점에 예외 없이 파괴된 뒤 아래 `FindOrCreateDetailPanel()`이 처음부터 다시 조립한다(명령서
    /// "기존 낡은 상세 창 오브젝트는 DestroyImmediate로 완벽히 폭파" 그대로 이행 - 별도 타겟 삭제 코드가
    /// 필요 없다). 새 구조는 상단 대형 카드 미리보기+이름+팀·등급, 하단 4탭(기본 스탯/특이폼·페이스/
    /// 핫·콜드존/스킬) - 탭 전환 로직은 신규 `PlayerDetailUIController`가 전담한다(`InventoryUIController`는
    /// `Show()`/`Hide()` 호출만 위임). 구 상세 패널의 [강화하기]/[각성하기]/[스킬 변경] 버튼은 이미
    /// TASK-145/146에서 `PlayerManagementUIController`/`EnhanceUIController`로 완전히 이관되어 중복이었
    /// 으므로(카드 클릭 시 애초에 이 패널이 아니라 선수 관리 허브가 뜬다) 새 패널에는 다시 만들지
    /// 않았다 - 새 패널은 순수 "정보 열람" 전용이다(명령서 4항 범위와 일치).
    ///
    /// [TASK-KBO-150, 진입 흐름 역전] 기획자 의도에 따라 카드 클릭 진입 순서를 "인벤토리 → 허브 →
    /// 상세창"에서 "인벤토리 → 상세창 → 허브"로 뒤집었다 - 이 파일이 조립하는 `DetailPanel` 하단에
    /// [선수 관리] 버튼을 신설해 `PlayerDetailUIController.playerManagementButton`/
    /// `playerManagementUIController`에 바인딩한다. 씬에 `PlayerManagementUIController`가 아직 없으면
    /// (`Auto-Connect Player Management UI`를 이 메뉴보다 먼저 실행한 적이 없으면) TASK-146/148이 확립한
    /// "메뉴 실행 순서 의존성 제거" 패턴 그대로 `SetupPlayerManagementUI.AutoConnectPlayerManagementUI()`를
    /// 직접 연쇄 호출한다. **상호 순환 호출 안전성**: `SetupPlayerManagementUI.cs`도 TASK-148부터 이미
    /// `PlayerDetailUIController`가 없으면 이 파일의 `AutoConnectInventoryUI()`를 연쇄 호출한다 - 언뜻
    /// 무한 재귀처럼 보이지만, 이 메서드가 그 연쇄 호출 지점(`FindOrCreateDetailPanel()` 이후)에 도달할
    /// 때는 `PlayerDetailUIController` 컴포넌트가 이미 이 호출 스택 안에서 생성 완료된 뒤이므로,
    /// `SetupPlayerManagementUI`가 (설령 이 호출이 그쪽에서 왔더라도) 자신의 `PlayerDetailUIController`
    /// 탐색에서 그 값을 바로 찾아내 다시 이 파일로 되돌아오지 않는다 - 양쪽 모두 "내 쪽 핵심 컴포넌트를
    /// 먼저 만든 뒤에만 상대를 조회/연쇄 호출"하는 순서를 지키는 한 재귀는 정확히 1회 왕복에서 끝난다.
    /// </summary>
    public static class SetupInventoryUI
    {
        private const string PanelName = "InventoryPanel";
        // [TASK-KBO-138] 카드 목록을 감싸는 표준 스크롤 뷰 계층(Panel-Viewport-Content). CardContainerName은
        // 이제 "Content"(GridLayoutGroup+ContentSizeFitter) 오브젝트 이름으로 쓰인다(마이그레이션 대상).
        private const string CardListPanelName = "CardListPanel";
        private const string ViewportName = "Viewport";
        private const string CardContainerName = "CardContainer";
        private const string InventoryButtonName = "InventoryButton";
        private const string CloseButtonName = "CloseButton";
        private const string TemplatesHolderName = "_Templates";
        private const string PlayerCardTemplateName = "PlayerCardTemplate";

        // ----- [TASK-KBO-147] 상세 정보 패널(탭 UI) 관련 이름 상수 -----
        private const string DetailPanelName = "DetailPanel";
        private const string DetailPreviewCardName = "DetailPreviewCard";
        private const string DetailNameTextName = "DetailNameText";
        private const string DetailTeamGradeTextName = "DetailTeamGradeText";
        private const string DetailTabBarName = "DetailTabBar";
        private const string StatsTabButtonName = "StatsTabButton";
        private const string SpecialFormTabButtonName = "SpecialFormTabButton";
        private const string HotColdTabButtonName = "HotColdTabButton";
        private const string SkillTabButtonName = "SkillTabButton";
        private const string StatsContentPanelName = "StatsContentPanel";
        private const string SpecialFormContentPanelName = "SpecialFormContentPanel";
        private const string HotColdContentPanelName = "HotColdContentPanel";
        private const string SkillContentPanelName = "SkillContentPanel";
        private const string DetailReinforceTextName = "DetailReinforceText";
        private const string DetailAwakenTextName = "DetailAwakenText";
        private const string StatRowTextName = "StatRowText";
        private const string SkillListTextName = "SkillListText";
        private const string HotColdGridName = "HotColdGrid";
        private const string DetailCloseButtonName = "DetailCloseButton";
        private const string PlayerManagementButtonName = "PlayerManagementButton";

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

            // [TASK-KBO-142, 명령서 0/4항] "SetAsFirstSibling/LastSibling로 순서만 고친다"는 TASK-141
            // 접근을 신뢰하지 않고, InventoryPanel의 모든 직속 자식을 예외 없이 먼저 파괴한 뒤 올바른
            // 순서로 처음부터 다시 만든다(TASK-140의 SetupShopUI.cs와 동일한 "완전 초기화 후 재조립"
            // 패턴) - 씬에 어떤 과거 세대의 잔재가 남아있었든 이 시점부터는 전혀 무관해진다. `_Templates`
            // (PlayerCardTemplate 등 다른 화면과 공유하는 자산)는 InventoryPanel 하위가 아니므로
            // 영향받지 않는다.
            for (int i = controller.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(controller.transform.GetChild(i).gameObject);
            }

            // 카드 스크롤 뷰를 가장 먼저 만든다 - 항상 맨 아래(=렌더링 최하단)에 있어야 한다(명령서 4항).
            var cardContainer = FindOrCreateCardListPanel(controller.transform,
                new Vector2(140f, 200f), new Vector2(10f, 10f));
            var cardPrefab = FindOrCreatePlayerCardTemplate(canvas.transform);

            // [TASK-KBO-147] 상세 정보 패널을 탭 UI로 전면 개편한다 - `FindOrCreateDetailPanel()`이
            // 상단 카드 미리보기+이름+팀/등급, 하단 4탭(기본 스탯/특이폼·페이스/핫·콜드존/스킬)까지
            // 전부 조립하고 `PlayerDetailUIController`에 배선까지 마친 뒤 반환한다.
            var playerDetailController = FindOrCreateDetailPanel(controller.transform, cardPrefab);

            // [TASK-KBO-111] 인벤토리 화면 자체를 닫는 버튼(InventoryPanel 우측 상단). 상세 패널 자체
            // 닫기 버튼은 FindOrCreateDetailPanel() 내부에서 DetailPanel 우측 상단에 별도로 만든다.
            var closeButton = FindOrCreateButton(controller.transform, CloseButtonName, "닫기",
                new Vector2(0.85f, 0.92f), new Vector2(1f, 1f));

            // [TASK-KBO-141, 명령서 4항] `FindOrCreateCardListPanel()`이 자신을 맨 앞으로 강제하는 것과
            // 대칭으로, 이 두 버튼(과 DetailPanel)은 항상 맨 뒤(=렌더링 최상단)로 강제한다 - 씬 상태가
            // 어떻든 두 방향에서 순서를 강제로 고정하므로 어느 한쪽만으로도 이미 충분하지만, "코드로
            // 강제 해결"이라는 명령서 취지에 맞춰 이중으로 보증한다.
            closeButton.transform.SetAsLastSibling();
            playerDetailController.transform.SetAsLastSibling();

            BindController(controller, cardContainer, cardPrefab, playerDetailController, closeButton);

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

            // [TASK-KBO-142, 명령서 6항] InventoryPanel 직속 자식 전체의 이름 + sibling index를 콘솔에
            // 덤프해 순서를 증명한다(TASK-141의 3개 항목 요약 검증을 전수 덤프로 강화).
            LogInventorySiblingDump(controller.transform);

            int cardListIndex = cardContainer.parent.parent.GetSiblingIndex(); // Content -> Viewport -> CardListPanel
            int closeButtonIndex = closeButton.transform.GetSiblingIndex();
            int detailPanelIndex = playerDetailController.transform.GetSiblingIndex();
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

        /// <summary>[TASK-KBO-142, 명령서 6항] `root`(InventoryPanel) 직속 자식 전체를 sibling index
        /// 순서 그대로 콘솔에 덤프한다. 정상이라면 index 0이 `CardListPanel`(카드 스크롤 뷰)이고,
        /// `CloseButton`/`DetailPanel`은 그보다 큰 index(=더 나중에 그려짐)여야 한다.
        /// [TASK-KBO-143, 사실 정정] 원래 `GetInstanceID()`로 식별자를 출력했으나 유니티 6 환경에서
        /// `CS0619(obsolete)` 컴파일 에러가 발생해 `GetHashCode()`로 교체했다(명령서 4항).</summary>
        private static void LogInventorySiblingDump(Transform root)
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine($"[SetupInventoryUI] {root.name} 직속 자식 Sibling 순서 덤프 (총 {root.childCount}개):");
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                builder.AppendLine($"  [{i}] {child.name} (HashCode {child.gameObject.GetHashCode()})");
            }
            Debug.Log(builder.ToString());
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

        /// <summary>
        /// [TASK-KBO-147, 전면 개편] 상세 정보 패널 전체(상단 카드 미리보기+이름+팀/등급, 하단 4탭 -
        /// 기본 스탯/특이폼·페이스/핫·콜드존/스킬)를 조립하고 `PlayerDetailUIController`에 전부 배선한
        /// 뒤 반환한다. `AutoConnectInventoryUI()`가 매 실행마다 `InventoryPanel`의 모든 직속 자식을
        /// 먼저 `DestroyImmediate`하므로(TASK-142), 이 메서드는 사실상 항상 "새로 만들기" 분기를 타 -
        /// 명령서가 요구한 "기존 낡은 상세 창 오브젝트 완벽 폭파 후 재생성"이 그대로 보장된다. 평소
        /// 비활성 상태로 둔다 - `InventoryUIController.Awake()`가 `CloseDetail()`로 다시 한번
        /// 비활성화하지만, 라이브 에디터에서 메뉴 실행 직후 어색하게 뜨지 않도록 생성 시점에도 명시적으로
        /// 꺼 둔다(`ScoutUIController.resultPopupRoot`와 동일한 관례).
        /// </summary>
        private static PlayerDetailUIController FindOrCreateDetailPanel(Transform parent, PlayerCardUI cardTemplate)
        {
            var existing = parent.Find(DetailPanelName);
            if (existing != null)
            {
                var existingController = existing.GetComponent<PlayerDetailUIController>();
                if (existingController != null) return existingController;
            }

            var panelObject = new GameObject(DetailPanelName, typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {DetailPanelName}");
            panelObject.transform.SetParent(parent, false);

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            panelObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
            panelObject.SetActive(false);

            // ----- 상단 요약: 대형 카드 미리보기 + 이름 + 팀/등급 -----
            var previewCard = FindOrCreateDetailPreviewCard(panelObject.transform, cardTemplate);
            var nameText = FindOrCreateText(panelObject.transform, DetailNameTextName, "",
                new Vector2(0.42f, 0.85f), new Vector2(0.95f, 0.95f));
            nameText.fontSize = 26;
            var teamGradeText = FindOrCreateText(panelObject.transform, DetailTeamGradeTextName, "",
                new Vector2(0.42f, 0.78f), new Vector2(0.95f, 0.85f));

            // ----- 탭 바: [기본 스탯]/[특이폼·페이스]/[핫·콜드존]/[스킬] -----
            var tabBar = FindOrCreateTabBar(panelObject.transform);
            var statsTabButton = FindOrCreateTabButton(tabBar, StatsTabButtonName, "기본 스탯");
            var specialFormTabButton = FindOrCreateTabButton(tabBar, SpecialFormTabButtonName, "특이폼/페이스");
            var hotColdTabButton = FindOrCreateTabButton(tabBar, HotColdTabButtonName, "핫/콜드존");
            var skillTabButton = FindOrCreateTabButton(tabBar, SkillTabButtonName, "스킬");

            // ----- 탭 콘텐츠 4개(같은 영역에 겹쳐두고 SelectTab()이 하나만 SetActive(true)) -----
            var contentAnchorMin = new Vector2(0.05f, 0.08f);
            var contentAnchorMax = new Vector2(0.95f, 0.66f);

            var statsPanel = FindOrCreateContentPanel(panelObject.transform, StatsContentPanelName, contentAnchorMin, contentAnchorMax);
            var reinforceText = FindOrCreateText(statsPanel, DetailReinforceTextName, "",
                new Vector2(0.05f, 0.85f), new Vector2(0.95f, 0.95f));
            var awakenText = FindOrCreateText(statsPanel, DetailAwakenTextName, "",
                new Vector2(0.05f, 0.72f), new Vector2(0.95f, 0.82f));
            var statRowTexts = new Text[5];
            float[] rowTopY = { 0.63f, 0.50f, 0.37f, 0.24f, 0.11f };
            for (int i = 0; i < statRowTexts.Length; i++)
            {
                statRowTexts[i] = FindOrCreateText(statsPanel, $"{StatRowTextName}{i}", "",
                    new Vector2(0.05f, rowTopY[i] - 0.10f), new Vector2(0.95f, rowTopY[i]));
                statRowTexts[i].fontSize = 20;
            }

            var specialFormPanel = FindOrCreateContentPanel(panelObject.transform, SpecialFormContentPanelName, contentAnchorMin, contentAnchorMax);
            var specialFormPlaceholderText = FindOrCreateText(specialFormPanel, "SpecialFormPlaceholderText",
                "특이폼/페이스 정보는 추후 업데이트 예정입니다.", new Vector2(0.05f, 0.4f), new Vector2(0.95f, 0.6f));
            specialFormPlaceholderText.alignment = TextAnchor.MiddleCenter;

            var hotColdPanel = FindOrCreateContentPanel(panelObject.transform, HotColdContentPanelName, contentAnchorMin, contentAnchorMax);
            FindOrCreateHotColdGrid(hotColdPanel);
            var hotColdCaptionText = FindOrCreateText(hotColdPanel, "HotColdCaptionText",
                "핫/콜드존 데이터는 추후 업데이트 예정입니다.", new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.14f));
            hotColdCaptionText.alignment = TextAnchor.MiddleCenter;

            var skillPanel = FindOrCreateContentPanel(panelObject.transform, SkillContentPanelName, contentAnchorMin, contentAnchorMax);
            var skillListText = FindOrCreateText(skillPanel, SkillListTextName, "",
                new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.95f));
            skillListText.alignment = TextAnchor.UpperLeft;

            var detailCloseButton = FindOrCreateCloseButton(panelObject.transform, DetailCloseButtonName, 64f);

            // [TASK-KBO-150, 명령서 4항] 4탭 콘텐츠 하단 여백(y 0~0.08, 위 contentAnchorMin.y=0.08과
            // 겹치지 않는 자리)에 큼직한 [선수 관리] 액션 버튼을 신설한다.
            var playerManagementButton = FindOrCreateButton(panelObject.transform, PlayerManagementButtonName,
                "선수 관리", new Vector2(0.20f, 0.01f), new Vector2(0.80f, 0.075f));

            var controller = panelObject.GetComponent<PlayerDetailUIController>();
            if (controller == null) controller = panelObject.AddComponent<PlayerDetailUIController>();

            // [TASK-KBO-150, CRITICAL] [선수 관리] 버튼이 열어야 할 `PlayerManagementUIController`를
            // 찾는다 - 못 찾으면(=Auto-Connect Player Management UI를 아직 실행한 적이 없으면) TASK-146/148이
            // 확립한 패턴 그대로 직접 연쇄 호출해 스스로 만든다(메뉴 실행 순서 의존성 제거). 순환 호출
            // 안전성은 클래스 요약 참고 - controller(PlayerDetailUIController)가 이미 위에서 생성 완료된
            // 뒤라 안전하다.
            var playerManagementUIController = Object.FindAnyObjectByType<PlayerManagementUIController>(FindObjectsInactive.Include);
            if (playerManagementUIController == null)
            {
                Debug.LogWarning("[SetupInventoryUI] 씬에서 PlayerManagementUIController를 찾지 못해 " +
                    "'KBO Manager/Setup/Auto-Connect Player Management UI'를 자동으로 먼저 실행합니다.");
                SetupPlayerManagementUI.AutoConnectPlayerManagementUI();
                playerManagementUIController = Object.FindAnyObjectByType<PlayerManagementUIController>(FindObjectsInactive.Include);
            }

            // [TASK-KBO-151, CRITICAL - 근본 원인 수정] 위 코드는 지금까지 "이 DetailPanel이 참조할
            // Hub"만 찾아 한 방향으로만 바인딩했다 - 그런데 이 메서드는 매 실행마다 `PlayerDetailUIController`
            // 자체를 완전히 새로 만든다(이 파일 맨 위 `AutoConnectInventoryUI()`가 `InventoryPanel`의
            // 모든 자식을 `DestroyImmediate`하므로, `DetailPanel`도 그 시점에 파괴되고 여기서 새
            // 인스턴스로 재생성된다). 만약 씬에 `PlayerManagementUIController`가 이미 있었다면(=이전에
            // "Auto-Connect Player Management UI"를 먼저 실행해 둔 상태였다면), 그 Hub가 들고 있던
            // `playerDetailUIController` 필드는 여전히 "방금 파괴된 이전 DetailPanel 인스턴스"를 가리키고
            // 있다 - Hub 쪽에서 아무도 그 필드를 다시 갱신해 주지 않으므로, 허브의 초상화를 클릭하면
            // 유니티가 "파괴된 오브젝트"를 null처럼 취급해 `PlayerManagementUIController.OnClickCardPortrait()`가
            // "playerDetailUIController가 바인딩되지 않았다"는 경고를 내며 조용히 실패한다 - 사용자가
            // 보고한 증상과 정확히 일치하는 근본 원인이다("Auto-Connect Inventory UI"를 나중에 재실행하면
            // 매번 재발한다). 이 자리에서 Hub 쪽 필드도 직접 다시 써서 항상 최신 DetailPanel 인스턴스를
            // 가리키도록 강제한다 - 어느 메뉴를 어떤 순서로 실행하든 매번 확실하게 동기화된다(명령서
            // 4항 "확실하게 할당").
            if (playerManagementUIController != null)
            {
                var serializedHub = new SerializedObject(playerManagementUIController);
                serializedHub.FindProperty("playerDetailUIController").objectReferenceValue = controller;
                serializedHub.ApplyModifiedProperties();
                EditorUtility.SetDirty(playerManagementUIController);
            }

            BindDetailController(controller, previewCard, nameText, teamGradeText,
                statsTabButton, specialFormTabButton, hotColdTabButton, skillTabButton,
                statsPanel.gameObject, specialFormPanel.gameObject, hotColdPanel.gameObject, skillPanel.gameObject,
                reinforceText, awakenText, statRowTexts, skillListText,
                playerManagementButton, playerManagementUIController, detailCloseButton);
            EditorUtility.SetDirty(controller);

            bool playerManagementBound = playerManagementButton != null && playerManagementUIController != null;
            if (playerManagementBound)
            {
                Debug.Log("[SetupInventoryUI] [선수 관리] 버튼 바인딩 성공 - playerManagementButton -> " +
                    "PlayerDetailUIController.playerManagementUIController -> PlayerManagementUIController 연결 확인.");
            }
            else
            {
                Debug.LogError("[SetupInventoryUI] [선수 관리] 버튼 바인딩 실패 - playerManagementUIController가 " +
                    "여전히 null입니다. 'Auto-Connect Player Management UI'가 오류 없이 끝났는지 확인하십시오.");
            }

            // [TASK-KBO-151, 명령서 4항] 위 "역방향 동기화"가 실제로 반영됐는지 다시 읽어 검증한다.
            bool hubPointsBackToThisDetail = playerManagementUIController != null &&
                new SerializedObject(playerManagementUIController).FindProperty("playerDetailUIController").objectReferenceValue == controller;
            if (hubPointsBackToThisDetail)
            {
                Debug.Log("[SetupInventoryUI] 허브 ↔ 상세창 역방향 바인딩 성공 - " +
                    "PlayerManagementUIController.playerDetailUIController가 이 DetailPanel을 정확히 가리킵니다.");
            }
            else
            {
                Debug.LogError("[SetupInventoryUI] 허브 ↔ 상세창 역방향 바인딩 실패 - 허브의 초상화 클릭이 " +
                    "여전히 무반응일 수 있습니다. playerManagementUIController가 null인지 확인하십시오.");
            }

            return controller;
        }

        /// <summary>`cardPrefab`(템플릿)을 복제해 상세 패널 전용 대형 미리보기 카드 인스턴스를 만든다
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
            rect.anchorMin = new Vector2(0.05f, 0.55f);
            rect.anchorMax = new Vector2(0.38f, 0.95f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return instance;
        }

        /// <summary>[TASK-KBO-147] 4개 탭 버튼을 담을 가로 배치 컨테이너.</summary>
        private static Transform FindOrCreateTabBar(Transform parent)
        {
            var existing = parent.Find(DetailTabBarName);
            GameObject barObject;
            if (existing != null)
            {
                barObject = existing.gameObject;
            }
            else
            {
                barObject = new GameObject(DetailTabBarName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
                Undo.RegisterCreatedObjectUndo(barObject, $"Create {DetailTabBarName}");
                barObject.transform.SetParent(parent, false);
            }

            var rect = (RectTransform)barObject.transform;
            rect.anchorMin = new Vector2(0.05f, 0.68f);
            rect.anchorMax = new Vector2(0.95f, 0.76f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var layout = barObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = true;

            return barObject.transform;
        }

        /// <summary>[TASK-KBO-147, 명령서 4항] 탭 버튼 1개. 클릭 리스너는 `BindDetailController()`가
        /// 붙이지 않는다 - `PlayerDetailUIController.Awake()`가 스스로 자신의 4개 버튼에
        /// `onClick.AddListener()`를 연결하므로(SetupPlayerManagementUI.cs 등 다른 컨트롤러와 동일
        /// 관례 - 클릭 로직은 항상 컨트롤러 쪽에 둔다), 여기서는 순수 시각 요소만 만든다.</summary>
        private static Button FindOrCreateTabButton(Transform parent, string name, string label)
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
            image.color = Color.white;

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
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 10;
            text.resizeTextMaxSize = 18;
            ApplyButtonLabel(button, label);

            return button;
        }

        /// <summary>[TASK-KBO-147] 탭 콘텐츠 1개(같은 영역에 4개가 겹쳐 배치되며, PlayerDetailUIController가
        /// 한 번에 하나만 SetActive(true)한다). 배경은 없음(투명) - 각 콘텐츠가 자기 텍스트/그리드만
        /// 그린다.</summary>
        private static Transform FindOrCreateContentPanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var existing = parent.Find(name);
            GameObject panelObject;
            if (existing != null)
            {
                panelObject = existing.gameObject;
            }
            else
            {
                panelObject = new GameObject(name, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(panelObject, $"Create {name}");
                panelObject.transform.SetParent(parent, false);
            }

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return panelObject.transform;
        }

        /// <summary>[명령서 5항 - 더미 데이터] 실제 타격 존 데이터가 없으므로, 3x3 중립 회색 타일
        /// 그리드만 자리에 채워 둔다(추후 실데이터 연동 시 이 타일들의 색상만 갱신하면 된다).</summary>
        private static void FindOrCreateHotColdGrid(Transform parent)
        {
            var existing = parent.Find(HotColdGridName);
            GameObject gridObject;
            if (existing != null)
            {
                gridObject = existing.gameObject;
            }
            else
            {
                gridObject = new GameObject(HotColdGridName, typeof(RectTransform), typeof(GridLayoutGroup));
                Undo.RegisterCreatedObjectUndo(gridObject, $"Create {HotColdGridName}");
                gridObject.transform.SetParent(parent, false);
            }

            var rect = (RectTransform)gridObject.transform;
            rect.anchorMin = new Vector2(0.3f, 0.2f);
            rect.anchorMax = new Vector2(0.7f, 0.95f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var grid = gridObject.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(56f, 56f);
            grid.spacing = new Vector2(6f, 6f);
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;

            for (int i = 0; i < 9; i++)
            {
                string tileName = $"Tile{i}";
                var tileTransform = gridObject.transform.Find(tileName);
                GameObject tileObject = tileTransform != null ? tileTransform.gameObject : null;
                if (tileObject == null)
                {
                    tileObject = new GameObject(tileName, typeof(RectTransform), typeof(Image));
                    Undo.RegisterCreatedObjectUndo(tileObject, $"Create {tileName}");
                    tileObject.transform.SetParent(gridObject.transform, false);
                }

                tileObject.GetComponent<Image>().color = new Color(0.55f, 0.55f, 0.55f); // 더미 - 전부 동일한 중립 회색
            }
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

        /// <summary>[TASK-KBO-147] `InventoryUIController`의 대폭 축소된 필드 세트를 배선한다 - 구
        /// 상세 패널 관련 필드(`materialSelectUIController`/`gameActionController`/`detailPreviewCard`
        /// 등)는 컨트롤러 자체에서 전부 제거됐으므로(신규 `playerDetailUIController` 하나로 대체) 더
        /// 이상 여기서 바인딩하지 않는다.</summary>
        private static void BindController(InventoryUIController controller,
            Transform cardContainer, PlayerCardUI cardPrefab,
            PlayerDetailUIController playerDetailUIController, Button closeButton)
        {
            var serialized = new SerializedObject(controller);

            serialized.FindProperty("cardContainer").objectReferenceValue = cardContainer;
            if (cardPrefab != null) serialized.FindProperty("cardPrefab").objectReferenceValue = cardPrefab;

            serialized.FindProperty("playerDetailUIController").objectReferenceValue = playerDetailUIController;
            serialized.FindProperty("closeButton").objectReferenceValue = closeButton;

            serialized.ApplyModifiedProperties();
        }

        /// <summary>[TASK-KBO-147, TASK-KBO-150 확장] 신규 `PlayerDetailUIController`(탭 UI)의 전체
        /// 필드를 배선한다 - TASK-150에서 [선수 관리] 버튼(`playerManagementButton`)과 그 대상
        /// 컨트롤러(`playerManagementUIController`) 두 필드가 추가됐다.</summary>
        private static void BindDetailController(PlayerDetailUIController controller,
            PlayerCardUI previewCard, Text nameText, Text teamGradeText,
            Button statsTabButton, Button specialFormTabButton, Button hotColdTabButton, Button skillTabButton,
            GameObject statsContentPanel, GameObject specialFormContentPanel, GameObject hotColdContentPanel, GameObject skillContentPanel,
            Text reinforceText, Text awakenText, Text[] statRowTexts, Text skillListText,
            Button playerManagementButton, PlayerManagementUIController playerManagementUIController, Button closeButton)
        {
            var serialized = new SerializedObject(controller);

            serialized.FindProperty("panelRoot").objectReferenceValue = controller.gameObject;

            if (previewCard != null) serialized.FindProperty("previewCard").objectReferenceValue = previewCard;
            serialized.FindProperty("nameText").objectReferenceValue = nameText;
            serialized.FindProperty("teamGradeText").objectReferenceValue = teamGradeText;

            serialized.FindProperty("statsTabButton").objectReferenceValue = statsTabButton;
            serialized.FindProperty("specialFormTabButton").objectReferenceValue = specialFormTabButton;
            serialized.FindProperty("hotColdTabButton").objectReferenceValue = hotColdTabButton;
            serialized.FindProperty("skillTabButton").objectReferenceValue = skillTabButton;

            serialized.FindProperty("statsContentPanel").objectReferenceValue = statsContentPanel;
            serialized.FindProperty("specialFormContentPanel").objectReferenceValue = specialFormContentPanel;
            serialized.FindProperty("hotColdContentPanel").objectReferenceValue = hotColdContentPanel;
            serialized.FindProperty("skillContentPanel").objectReferenceValue = skillContentPanel;

            serialized.FindProperty("reinforceText").objectReferenceValue = reinforceText;
            serialized.FindProperty("awakenText").objectReferenceValue = awakenText;

            var statRowsProperty = serialized.FindProperty("statRowTexts");
            statRowsProperty.arraySize = statRowTexts.Length;
            for (int i = 0; i < statRowTexts.Length; i++)
            {
                statRowsProperty.GetArrayElementAtIndex(i).objectReferenceValue = statRowTexts[i];
            }

            serialized.FindProperty("skillListText").objectReferenceValue = skillListText;

            if (playerManagementButton != null) serialized.FindProperty("playerManagementButton").objectReferenceValue = playerManagementButton;
            if (playerManagementUIController != null) serialized.FindProperty("playerManagementUIController").objectReferenceValue = playerManagementUIController;

            serialized.FindProperty("closeButton").objectReferenceValue = closeButton;

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
