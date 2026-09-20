using KBOManager.Controllers;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-059] TASK-KBO-058에서 만든 CheerleaderInventoryUIController/CheerleaderSlotUI를 QA가
    /// 메뉴 클릭 한 번으로 씬에 조립하고 테스트할 수 있게 하는 에디터 자동화. SceneInitializer/
    /// ItemDataSeeder/SetupLobbyUI와 동일한 관례로 여러 번 실행해도 안전하다(이미 있으면 찾아 재사용).
    /// </summary>
    public static class SetupCheerleaderUI
    {
        private const string CanvasName = "Canvas";
        private const string PanelName = "CheerleaderInventoryPanel";
        private const string ScrollViewName = "ScrollView";
        private const string ViewportName = "Viewport";
        private const string ContentName = "Content";
        private const string TemplatesHolderName = "_Templates (Hidden)";
        private const string SlotTemplateName = "CheerleaderSlotTemplate";

        [MenuItem("KBO Manager/Setup/Auto-Create Cheerleader Inventory UI")]
        public static void AutoCreateInventoryUI()
        {
            var canvas = EnsureCanvas();
            var controller = FindOrCreateInventoryPanel(canvas.transform);
            var content = FindOrCreateScrollView(controller.transform);
            var slotTemplate = FindOrCreateSlotTemplate(canvas.transform);

            BindController(controller, content, slotTemplate);

            EditorUtility.SetDirty(controller);
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);

            Debug.Log("[SetupCheerleaderUI] 치어리더 인벤토리 UI 자동 생성/바인딩 완료.");
        }

        private static Canvas EnsureCanvas()
        {
            var existing = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Exclude);
            if (existing != null) return existing;

            var canvasObject = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Undo.RegisterCreatedObjectUndo(canvasObject, "Create Canvas");

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            return canvas;
        }

        private static CheerleaderInventoryUIController FindOrCreateInventoryPanel(Transform canvasTransform)
        {
            var existingChild = canvasTransform.Find(PanelName);
            if (existingChild != null)
            {
                var existingController = existingChild.GetComponent<CheerleaderInventoryUIController>();
                if (existingController != null) return existingController;
            }

            var panelObject = new GameObject(PanelName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {PanelName}");
            panelObject.transform.SetParent(canvasTransform, false);

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return panelObject.AddComponent<CheerleaderInventoryUIController>();
        }

        /// <summary>패널 아래에 ScrollRect+Viewport(Mask)+Content(VerticalLayoutGroup) 최소 스크롤 뷰
        /// 구조를 만든다. 앵커/수치는 명령서 5항 지시대로 대략적인 기본값만 적용한다(디자인 확정 아님).</summary>
        private static Transform FindOrCreateScrollView(Transform panelTransform)
        {
            var existingScrollView = panelTransform.Find(ScrollViewName);
            if (existingScrollView != null)
            {
                var existingContent = existingScrollView.Find(ViewportName)?.Find(ContentName);
                if (existingContent != null) return existingContent;
            }

            var scrollViewObject = new GameObject(ScrollViewName, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            Undo.RegisterCreatedObjectUndo(scrollViewObject, $"Create {ScrollViewName}");
            scrollViewObject.transform.SetParent(panelTransform, false);

            var scrollViewRect = (RectTransform)scrollViewObject.transform;
            scrollViewRect.anchorMin = Vector2.zero;
            scrollViewRect.anchorMax = Vector2.one;
            scrollViewRect.offsetMin = Vector2.zero;
            scrollViewRect.offsetMax = Vector2.zero;

            scrollViewObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.15f);

            var viewportObject = new GameObject(ViewportName, typeof(RectTransform), typeof(Image), typeof(Mask));
            Undo.RegisterCreatedObjectUndo(viewportObject, $"Create {ViewportName}");
            viewportObject.transform.SetParent(scrollViewObject.transform, false);

            var viewportRect = (RectTransform)viewportObject.transform;
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            viewportObject.GetComponent<Image>().color = Color.white;
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;

            var contentObject = new GameObject(ContentName, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            Undo.RegisterCreatedObjectUndo(contentObject, $"Create {ContentName}");
            contentObject.transform.SetParent(viewportObject.transform, false);

            var contentRect = (RectTransform)contentObject.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = Vector2.zero;

            var layout = contentObject.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;

            contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollRect = scrollViewObject.GetComponent<ScrollRect>();
            scrollRect.viewport = viewportRect;
            scrollRect.content = contentRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            return contentObject.transform;
        }

        // [TASK-KBO-070] 140 -> 190으로 소폭 증가 - ClutchText/SentimentText 2줄이 추가되어도 슬롯
        // 안에서 겹치지 않도록 여유를 둔다(명령서 9항).
        private const float SlotTemplateHeight = 190f;

        /// <summary>
        /// CheerleaderSlotUI가 부착된 "프리팹 대용" 오브젝트를 찾거나 만든다. 부모(_Templates)를
        /// 비활성화해 씬에서는 보이지 않게 숨기지만, 이 템플릿 오브젝트 자신의 activeSelf는 반드시
        /// true로 둔다 - CheerleaderInventoryUIController.RefreshInventory()(TASK-KBO-058, 런타임
        /// 코드라 이번 작업에서 수정 불가)가 Instantiate(slotPrefab, contentContainer)로 복제할 때
        /// 원본의 activeSelf를 그대로 복사하므로, 여기서 false를 두면 실제 인벤토리 슬롯 전부가
        /// 비활성 상태로 태어나 화면에 아무것도 표시되지 않게 된다.
        ///
        /// [TASK-KBO-070] 이전 버전은 기존 템플릿을 찾으면 그 자리에서 즉시 반환해(early return)
        /// ClutchText/SentimentText 등 새로 추가된 필드를 기존(TASK-KBO-059 시점) 템플릿에는 채워
        /// 넣지 못했다 - 명령서 7항 지시대로, 기존 템플릿이 있어도 끝까지 진행해 누락된 텍스트만
        /// 추가/재바인딩하도록 구조를 바꿨다(이미 있는 4개 텍스트는 FindOrCreateText가 그대로
        /// 찾아 재사용하므로 중복 생성되지 않는다).
        /// </summary>
        private static GameObject FindOrCreateSlotTemplate(Transform canvasTransform)
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

            var slotObject = FindOrCreateSlotRoot(holderTransform);

            var rect = (RectTransform)slotObject.transform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, SlotTemplateHeight);

            var nameText = FindOrCreateText(slotObject.transform, "NameText", 24f);
            var gradeText = FindOrCreateText(slotObject.transform, "GradeText", 24f);
            var buffText = FindOrCreateText(slotObject.transform, "BuffText", 24f);
            var economicRateText = FindOrCreateText(slotObject.transform, "EconomicRateText", 24f);
            var clutchText = FindOrCreateText(slotObject.transform, "ClutchText", 24f);
            var sentimentText = FindOrCreateText(slotObject.transform, "SentimentText", 24f);
            var equipButton = FindOrCreateButton(slotObject.transform, "EquipButton", "장착", 36f);
            var equipButtonLabel = equipButton.GetComponentInChildren<Text>();

            var slotUI = slotObject.GetComponent<CheerleaderSlotUI>();
            if (slotUI == null) slotUI = slotObject.AddComponent<CheerleaderSlotUI>();

            var serializedSlot = new SerializedObject(slotUI);
            serializedSlot.FindProperty("nameText").objectReferenceValue = nameText;
            serializedSlot.FindProperty("gradeText").objectReferenceValue = gradeText;
            serializedSlot.FindProperty("buffText").objectReferenceValue = buffText;
            serializedSlot.FindProperty("economicRateText").objectReferenceValue = economicRateText;
            serializedSlot.FindProperty("clutchText").objectReferenceValue = clutchText;
            serializedSlot.FindProperty("sentimentText").objectReferenceValue = sentimentText;
            serializedSlot.FindProperty("equipButton").objectReferenceValue = equipButton;
            serializedSlot.FindProperty("equipButtonLabel").objectReferenceValue = equipButtonLabel;
            serializedSlot.ApplyModifiedProperties();

            return slotObject;
        }

        /// <summary>슬롯 루트 GameObject를 이름으로 찾아 재사용하거나, 없으면 RectTransform+Image+
        /// VerticalLayoutGroup으로 새로 만든다(레이아웃 설정은 최초 생성 시에만 적용 - 기존 템플릿의
        /// 레이아웃 값을 덮어써 QA가 이미 조정해 둔 값을 되돌리지 않기 위함).</summary>
        private static GameObject FindOrCreateSlotRoot(Transform holderTransform)
        {
            var existingSlot = holderTransform.Find(SlotTemplateName);
            if (existingSlot != null) return existingSlot.gameObject;

            var slotObject = new GameObject(SlotTemplateName, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            Undo.RegisterCreatedObjectUndo(slotObject, $"Create {SlotTemplateName}");
            slotObject.transform.SetParent(holderTransform, false);

            slotObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

            var layout = slotObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 2f;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandHeight = false;

            return slotObject;
        }

        private static Text FindOrCreateText(Transform parent, string name, float preferredHeight)
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

            textObject.GetComponent<LayoutElement>().preferredHeight = preferredHeight;

            var text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            return text;
        }

        private static Button FindOrCreateButton(Transform parent, string name, string label, float preferredHeight)
        {
            var existingChild = parent.Find(name);
            if (existingChild != null)
            {
                var existingButton = existingChild.GetComponent<Button>();
                if (existingButton != null) return existingButton;
            }

            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {name}");
            buttonObject.transform.SetParent(parent, false);

            buttonObject.GetComponent<LayoutElement>().preferredHeight = preferredHeight;

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

            return button;
        }

        private static void BindController(CheerleaderInventoryUIController controller, Transform content, GameObject slotTemplate)
        {
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("contentContainer").objectReferenceValue = content;
            serializedController.FindProperty("slotPrefab").objectReferenceValue = slotTemplate;
            serializedController.ApplyModifiedProperties();
        }

        // [TASK-KBO-064] 모든 클릭이 같은 CatalogId를 쓴다 - 최초 1회는 인벤토리에 실제로 추가되고,
        // 그 이후 클릭은 GameManager.AddCheerleader()의 중복 판별 로직(CatalogId 일치)에 걸려
        // CheerStick(응원봉)으로 변환된다. 신규 추가 경로와 중복 변환 경로를 이 메뉴 하나로 반복 테스트할
        // 수 있게 하기 위한 의도적 설계다.
        private const string DummyCatalogId = "DEV_TEST_CHEER_CATALOG_001";

        /// <summary>[TASK-KBO-059] 인벤토리가 비어 있어 렌더링을 눈으로 확인할 수 없는 상황을 대비한
        /// QA 전용 더미 데이터 주입 메뉴. 플레이 모드가 아니면(GameManager.Instance == null) 경고만
        /// 남기고 안전하게 종료한다.</summary>
        [MenuItem("KBO Manager/Debug/Add Dummy Cheerleader to Inventory")]
        public static void AddDummyCheerleaderToInventory()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogWarning("[SetupCheerleaderUI] 플레이 모드에서만 실행 가능합니다.");
                return;
            }

            // 명령서 예시 코드는 InstanceId를 지정하지 않지만, TASK-KBO-057/058에서 확립된 불변식
            // ("치어리더 인스턴스는 항상 비어 있지 않은 InstanceId를 가진다" - SaveManager의 null 판별
            // 로직과 CheerleaderInventoryUIController.IsSameCheerleader()가 모두 이 값에 의존한다)을
            // 어기면, 이 더미를 장착해도 "장착됨"으로 표시되지 않거나 세이브 후 사라질 수 있다. 그래서
            // 명령서 예시에 InstanceId 한 필드만 추가했다 - 나머지 필드는 예시 그대로다.
            // [TASK-KBO-064] CatalogId도 함께 채운다 - 비어 있으면 GameManager.AddCheerleader()의
            // 중복 검사 자체가 통째로 건너뛰어져 이 메뉴로는 중복 변환 경로를 테스트할 수 없다.
            var dummy = new Cheerleader
            {
                InstanceId = System.Guid.NewGuid().ToString(),
                CatalogId = DummyCatalogId,
                Name = "Dummy Cheerleader",
                Grade = CheerleaderGrade.TEST,
                ConditionBuff = 2,
                EconomicBonusRate = 1.1f,
            };

            GameManager.Instance.AddCheerleader(dummy);

            bool wasAddedToInventory = GameManager.Instance.OwnedCheerleaders.Contains(dummy);
            Debug.Log(wasAddedToInventory
                ? $"[SetupCheerleaderUI] 더미 치어리더 추가 완료: {dummy.Name} (InstanceId={dummy.InstanceId}, CatalogId={dummy.CatalogId})"
                : $"[SetupCheerleaderUI] 이미 보유 중인 CatalogId({DummyCatalogId})와 중복되어 인벤토리에 추가되지 않고 " +
                  "재화로 변환되었습니다(자세한 변환량은 GameManager 로그 참고) - 신규 추가를 다시 보려면 " +
                  "먼저 인벤토리에서 이 더미를 제거해야 합니다.");
        }

        /// <summary>
        /// [TASK-KBO-074/129] 가챠 테스트 시 응원봉 부족으로 치어리더 뽑기가 막히는 QA 불편을
        /// 해소하는 재화 충전 메뉴. [TASK-KBO-129] 구 단일 CheerStick 필드가 GDD 재화 4종(라이브/스타/
        /// 레전드/한정 응원봉)으로 나뉘어 4개 전부를 채운다. GachaSimulationMenu.cs가 아니라 이
        /// 파일에 둔 이유는, GachaSimulationMenu는 "GameManager를 전혀 건드리지 않는 완전 독립
        /// Mocking 시뮬레이션 전용" 도구로 스스로 문서화하고 있어(TASK-KBO-073) 실제 GameManager
        /// 상태를 바꾸는 이 메서드를 넣으면 그 문서화된 계약과 모순되기 때문이다 - 반면 이 파일은
        /// 이미 AddDummyCheerleaderToInventory() 등으로 GameManager 상태를 직접 조작하는 QA 메뉴들을
        /// 담아 왔으므로 관례상 더 적합하다. 플레이 모드가 아니면(GameManager.Instance == null)
        /// 경고만 남기고 안전하게 종료한다.
        /// </summary>
        [MenuItem("KBO Manager/Debug/Add 10,000 CheerStick")]
        public static void AddCheerStick()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogWarning("플레이 모드에서만 실행 가능합니다.");
                return;
            }

            GameManager.Instance.LiveCheerStick += 10000;
            GameManager.Instance.StarCheerStick += 10000;
            GameManager.Instance.LegendCheerStick += 10000;
            GameManager.Instance.LimitedCheerStick += 10000;
            Debug.Log("테스트용 응원봉(라이브/스타/레전드/한정) 각 +10000 지급 완료.");
        }

        /// <summary>
        /// [TASK-KBO-115/126/129] 선수 스카우트는 `CheerStick`이 아니라 GDD 선수 영입 재화 6종
        /// (라이브 일반/에픽 영입권, 픽업 영입권, 고급 영입권, 트로피, 싸인볼)을 소모한다 - 위
        /// `AddCheerStick()`만 실행하면 선수 뽑기 버튼을 눌러도 조용히 빈 결과만 돌아온다. GDD 재화
        /// 10종 + GameGold 전부를 한 번에 채워 이 QA 함정을 없앤다.
        /// </summary>
        [MenuItem("KBO Manager/Debug/Add All Currencies (100,000)")]
        public static void AddAllCurrencies()
        {
            if (GameManager.Instance == null)
            {
                Debug.LogWarning("플레이 모드에서만 실행 가능합니다.");
                return;
            }

            GameManager.Instance.LiveNormalTicket += 100000;
            GameManager.Instance.LiveEpicTicket += 100000;
            GameManager.Instance.PickupTicket += 100000;
            GameManager.Instance.AdvancedTicket += 100000;
            GameManager.Instance.Trophy += 100000;
            GameManager.Instance.SignatureBall += 100000;
            GameManager.Instance.LiveCheerStick += 100000;
            GameManager.Instance.StarCheerStick += 100000;
            GameManager.Instance.LegendCheerStick += 100000;
            GameManager.Instance.LimitedCheerStick += 100000;
            GameManager.Instance.GameGold += 100000;

            Debug.Log("테스트용 전체 재화(GDD 10종 + GameGold) 각 +100000 지급 완료.");
        }

        /// <summary>
        /// [TASK-KBO-065, TASK-KBO-132 핫픽스] 구 `CheerleaderGachaService.RollGacha(1)`(TASK-KBO-129에서
        /// 4개 카테고리 전용 메서드로 교체되며 완전히 삭제됨, CS0117 컴파일 에러 유발)을 `RollLive(1)`로
        /// 교체했다. [매핑 근거] 4개 카테고리(`RollLive`/`RollLimited`/`RollIcon`/`RollLegend`) 중
        /// `RollLive()`만 등급이 확률적으로 갈리고(LIVE_NORMAL 62.5%/LIVE_EPIC 37.5%) 나머지 3개는 전부
        /// 단일 등급 100% 확정이라, "가챠 확률 분포가 실제로 작동하는지"를 검증하는 이 QA 메뉴 본연의
        /// 목적(구 RollGacha()의 5단계 혼합 확률 검증과 동일한 취지)에는 `RollLive()`가 유일하게
        /// 대응된다. 플레이 모드가 아니거나 재화가 부족하면 서비스 쪽에서 이미 경고 로그를 남기므로,
        /// 여기서는 실패 시 보충 안내만 한 줄 추가한다.
        /// </summary>
        [MenuItem("KBO Manager/Debug/Roll 1x Gacha")]
        public static void Roll1xGacha()
        {
            var results = CheerleaderGachaService.RollLive(1);
            if (results.Count == 0)
            {
                Debug.LogWarning("[SetupCheerleaderUI] 1연뽑 실행 실패 - 위 CheerleaderGachaService 로그를 확인하세요.");
            }
        }

        /// <summary>[TASK-KBO-065, TASK-KBO-132 핫픽스] 구 `RollGacha(10)`을 `RollLive(10)`으로 교체했다
        /// (매핑 근거는 `Roll1xGacha()` 참고).</summary>
        [MenuItem("KBO Manager/Debug/Roll 10x Gacha")]
        public static void Roll10xGacha()
        {
            var results = CheerleaderGachaService.RollLive(10);
            if (results.Count == 0)
            {
                Debug.LogWarning("[SetupCheerleaderUI] 10연뽑 실행 실패 - 위 CheerleaderGachaService 로그를 확인하세요.");
            }
        }
    }
}
