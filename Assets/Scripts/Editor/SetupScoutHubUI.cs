using KBOManager.Controllers;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-129] GDD "UI 흐름 > 스카우트" 절을 그대로 반영해, 그동안 완전히 분리돼 있던 선수
    /// 스카우트(ScreenType.Scout, "스카우트" 로비 버튼)와 치어리더 가챠 상점(ScreenType.CheerleaderShop,
    /// "치어리더 뽑기" 로비 버튼) 두 화면을 "ScoutHubPanel" 하나로 합친다 - 선수 영입/치어리더 영입은
    /// ScoutHubUIController가 토글하는 두 섹션이 되고, 로비 진입점도 "스카우트" 버튼 하나만 남는다.
    ///
    /// 실행 순서: SetupScoutUI.EnsureScoutPanelAssembled()/SetupShopUI.EnsureShopPanelAssembled()로
    /// 각 섹션 내부를 먼저 완성시킨 뒤, 이 스크립트가 두 패널을 ScoutHubPanel 하위로 재부모화하고
    /// 탭 버튼 2개 + ScoutHubUIController를 조립한다. Find-or-Create 관례를 그대로 따르며(명령서 6항),
    /// 더 이상 쓰이지 않는 구 "GachaShopButton"(치어리더 전용 로비 진입 버튼)과 UIManager.screens의
    /// CheerleaderShop 항목은 DestroyImmediate/배열 제거로 완전히 정리한다(명령서 4항).
    ///
    /// [TASK-KBO-137] `ScoutUIController`/`CheerleaderShopUIController`가 각자 하단-좌측에 갖고 있던
    /// "닫기" 버튼(둘 다 `UIManager.Instance?.ShowScreen(ScreenType.Lobby)`로 동일한 로직)을 폐기하고,
    /// 허브(`ScoutHubPanel`) 레벨 우측 상단에 'X' 버튼 하나로 통일한다. 두 컨트롤러의 `closeButton`
    /// 필드를 이 새 버튼으로 재바인딩하는 것만으로 통합되므로(`Awake()`의 리스너 등록 로직은 무수정),
    /// C# 로직을 전혀 건드리지 않는다(명령서 5항).
    ///
    /// [TASK-KBO-141] `ScoutHubUIController.ShowPlayerSection()`/`ShowCheerleaderSection()` 자체는
    /// TASK-138에서 이미 무결함을 재확인했지만(둘 다 항상 SetActive(true)/(false)를 짝지어 호출),
    /// "탭 전환 시 겹침"이 반복 보고돼 씬 데이터 쪽 오바인딩 가능성을 정면으로 다룬다 - `ScoutPanel`/
    /// `CheerleaderShopPanel`/`ScoutHubPanel` 각각이 씬에 중복 존재하면, 이 메뉴가 매번 새로 찾아
    /// 고치는 "정본" 인스턴스와 실제로 `UIManager.screens` 등 다른 곳이 참조 중인 "구본" 인스턴스가
    /// 서로 다를 수 있어 아무리 정본을 고쳐도 화면엔 반영되지 않는 "보이지 않는 오바인딩"이 가능하다.
    /// `DestroyDuplicateInstances()`로 정본 하나만 남기고 나머지를 파괴한 뒤, 재부모화가 끝난
    /// `hubController` 하위에서 `GetComponentInChildren()`으로 다시 찾아 덮어써(명령서 4항, 인스펙터
    /// 값을 신뢰하지 않음) `LogHubBindingVerification()`으로 실제 저장된 값을 콘솔에 증명한다(명령서
    /// 6항 - "ScoutHub 바인딩 갱신 완료").
    /// </summary>
    public static class SetupScoutHubUI
    {
        private const string CanvasName = "Canvas";
        private const string HubPanelName = "ScoutHubPanel";
        private const string TabContainerName = "TabContainer";
        private const string PlayerTabButtonName = "PlayerTabButton";
        private const string CheerleaderTabButtonName = "CheerleaderTabButton";
        private const string ScoutButtonName = "ScoutButton";
        private const string LegacyGachaShopButtonName = "GachaShopButton";
        // [TASK-KBO-137] 각 섹션이 개별로 갖고 있던 구 "닫기" 버튼 오브젝트 이름(둘 다 동일).
        private const string LegacySectionCloseButtonName = "CloseButton";
        private const string HubCloseButtonName = "HubCloseButton";

        [MenuItem("KBO Manager/Setup/Auto-Connect Scout Hub")]
        public static void AutoConnectScoutHub()
        {
            var canvas = EnsureCanvas();

            var scoutController = SetupScoutUI.EnsureScoutPanelAssembled(canvas.transform);
            var shopController = SetupShopUI.EnsureShopPanelAssembled(canvas.transform);

            var hubController = FindOrCreateHubPanel(canvas.transform);

            // [TASK-KBO-141] 씬 어딘가에 같은 컴포넌트의 사본이 남아있으면, 이 메뉴가 방금 확정한
            // 정본(scoutController/shopController/hubController)을 아무리 고쳐도 실제로 화면에 쓰이는
            // 사본은 그대로 방치되는 "보이지 않는 오바인딩"이 가능하다 - 정본만 남기고 전부 파괴한다.
            DestroyDuplicateInstances(scoutController);
            DestroyDuplicateInstances(shopController);
            DestroyDuplicateInstances(hubController);

            ReparentSection(scoutController.transform, hubController.transform);
            ReparentSection(shopController.transform, hubController.transform);

            var (playerTabButton, cheerleaderTabButton) = FindOrCreateTabButtons(hubController.transform);

            // [TASK-KBO-141, 명령서 4항] 인스펙터에 남아있을 수 있는 오바인딩을 신뢰하지 않고, 방금
            // 재부모화까지 끝낸 hubController 하위에서 GetComponentInChildren으로 직접 다시 찾아
            // 덮어쓴다 - scoutController/shopController 변수를 그대로 재사용하는 대신 한 번 더 검증한다.
            var verifiedScoutSection = hubController.GetComponentInChildren<ScoutUIController>(true);
            var verifiedShopSection = hubController.GetComponentInChildren<CheerleaderShopUIController>(true);
            if (verifiedScoutSection == null || verifiedShopSection == null)
            {
                Debug.LogError("[SetupScoutHubUI] ScoutHubPanel 하위에서 ScoutUIController/" +
                    "CheerleaderShopUIController를 찾지 못해 바인딩을 중단합니다 - 재부모화가 실패한 " +
                    "것으로 보입니다.");
                return;
            }

            BindHubController(hubController, playerTabButton, cheerleaderTabButton,
                verifiedScoutSection.gameObject, verifiedShopSection.gameObject);

            // [TASK-KBO-141, 명령서 6항] 실제로 저장된 값을 SerializedObject로 다시 읽어 콘솔에 증명한다.
            LogHubBindingVerification(hubController, verifiedScoutSection.gameObject, verifiedShopSection.gameObject);

            // [TASK-KBO-137] 각 섹션의 구 하단-좌측 "닫기" 버튼을 정리하고, 허브 우측 상단에 'X'
            // 버튼 하나로 통일해 두 컨트롤러의 closeButton 필드 모두에 재바인딩한다.
            DestroyLegacyChild(scoutController.transform, LegacySectionCloseButtonName);
            DestroyLegacyChild(shopController.transform, LegacySectionCloseButtonName);

            var hubCloseButton = FindOrCreateCloseButton(hubController.transform, HubCloseButtonName, 60f);
            BindButtonField(scoutController, "closeButton", hubCloseButton);
            BindButtonField(shopController, "closeButton", hubCloseButton);
            EditorUtility.SetDirty(scoutController);
            EditorUtility.SetDirty(shopController);

            // 탭 컨테이너(FindOrCreateTabButtons 내부에서 이미 SetAsLastSibling)보다도 나중에 그려져야
            // 우측 상단에서 겹치는 탭 버튼 위로 항상 클릭 가능하다.
            hubCloseButton.transform.SetAsLastSibling();

            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupScoutHubUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "'스카우트' 진입 버튼 생성 및 UIManager 등록을 건너뜁니다.");
            }
            else
            {
                // [TASK-KBO-129] 치어리더 전용 로비 버튼은 더 이상 존재하지 않는다 - "스카우트" 버튼
                // 하나가 두 섹션 모두의 진입점이다. 필드도 그대로 남아있으면 널 참조 위험이 있어
                // LeagueDashboardUIController.cs에서 필드 자체를 제거했다(명령서 7항 바인딩 안전).
                var legacyGachaShopButton = dashboard.transform.Find(LegacyGachaShopButtonName);
                if (legacyGachaShopButton != null) Object.DestroyImmediate(legacyGachaShopButton.gameObject);

                // [TASK-KBO-129] 구 로비 상단 재화 텍스트(TASK-KBO-071/126, SetupLobbyPolishingUI.cs -
                // 이제 삭제됨)도 GDD에 없는 UI라 함께 정리한다. LeagueDashboardUIController.cs에서
                // scoutTicketLobbyText/cheerStickLobbyText 필드 자체를 제거했으므로 남겨두면 아무도
                // 갱신하지 않는 고아 텍스트가 된다.
                var legacyScoutTicketLobbyText = dashboard.transform.Find("ScoutTicketLobbyText");
                if (legacyScoutTicketLobbyText != null) Object.DestroyImmediate(legacyScoutTicketLobbyText.gameObject);
                var legacyCheerStickLobbyText = dashboard.transform.Find("CheerStickLobbyText");
                if (legacyCheerStickLobbyText != null) Object.DestroyImmediate(legacyCheerStickLobbyText.gameObject);

                var scoutButton = FindOrCreateScoutButton(dashboard.transform);
                BindButtonField(dashboard, "scoutButton", scoutButton);
                EditorUtility.SetDirty(dashboard);

                var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
                if (uiManager == null)
                {
                    Debug.LogWarning("[SetupScoutHubUI] 씬에서 UIManager를 찾지 못해 screens 등록을 건너뜁니다.");
                }
                else
                {
                    RemoveCheerleaderShopScreenEntry(uiManager);
                    SetupScoutUI.RegisterScoutScreen(uiManager, hubController.gameObject);
                    EditorUtility.SetDirty(uiManager);
                }
            }

            EditorUtility.SetDirty(hubController);

            var scene = hubController.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupScoutHubUI] 스카우트 허브(선수 영입 + 치어리더 영입) 자동 배선 완료.");
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

        /// <summary>화면 전체를 채우는 허브 루트. 자체 그래픽은 없다(자식 두 섹션이 각자 배경을 가짐) -
        /// 탭 전환만 담당하는 순수 컨테이너다.</summary>
        private static ScoutHubUIController FindOrCreateHubPanel(Transform canvasTransform)
        {
            var existingChild = canvasTransform.Find(HubPanelName);
            if (existingChild != null)
            {
                var existingController = existingChild.GetComponent<ScoutHubUIController>();
                if (existingController != null) return existingController;
            }

            var panelObject = new GameObject(HubPanelName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(panelObject, $"Create {HubPanelName}");
            panelObject.transform.SetParent(canvasTransform, false);

            var rect = (RectTransform)panelObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            return panelObject.AddComponent<ScoutHubUIController>();
        }

        /// <summary>섹션(ScoutPanel/CheerleaderShopPanel)을 허브 하위로 옮긴다. 이미 허브 하위라면
        /// 아무 것도 하지 않는다(SetParent(..., false)는 로컬 좌표계만 재계산 - 명령서 6항 안전 이동).</summary>
        private static void ReparentSection(Transform section, Transform hubTransform)
        {
            if (section.parent == hubTransform) return;
            section.SetParent(hubTransform, false);
        }

        /// <summary>허브 상단에 탭 버튼 2개를 담을 컨테이너를 만들고, 그 안에 "선수 영입"/"치어리더
        /// 영입" 버튼을 배치한다. 두 섹션(ScoutPanel/CheerleaderShopPanel) 전체화면 앵커와 겹치지
        /// 않도록 화면 최상단 8% 띠에만 배치한다.</summary>
        private static (Button playerTab, Button cheerleaderTab) FindOrCreateTabButtons(Transform hubTransform)
        {
            var existingContainer = hubTransform.Find(TabContainerName);
            Transform containerTransform;
            if (existingContainer != null)
            {
                containerTransform = existingContainer;
            }
            else
            {
                var containerObject = new GameObject(TabContainerName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(containerObject, $"Create {TabContainerName}");
                containerObject.transform.SetParent(hubTransform, false);

                var rect = (RectTransform)containerObject.transform;
                rect.anchorMin = new Vector2(0f, 0.92f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;

                containerObject.GetComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f);

                containerTransform = containerObject.transform;
            }

            // 탭 컨테이너는 항상 씬의 최상위 형제 목록 순서와 무관하게 두 섹션보다 나중에 그려져야
            // 클릭이 씹히지 않는다(TASK-KBO-125 Z-order 소프트락 선례와 동일한 주의).
            containerTransform.SetAsLastSibling();

            if (!containerTransform.TryGetComponent<HorizontalLayoutGroup>(out var layout))
            {
                layout = containerTransform.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 4f;
                layout.childControlWidth = true;
                layout.childForceExpandWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandHeight = true;
            }

            var playerTab = FindOrCreateTabButton(containerTransform, PlayerTabButtonName, "선수 영입");
            var cheerleaderTab = FindOrCreateTabButton(containerTransform, CheerleaderTabButtonName, "치어리더 영입");

            return (playerTab, cheerleaderTab);
        }

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
            text.fontSize = 20;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;
            ApplyButtonLabel(button, label);

            return button;
        }

        private static void ApplyButtonLabel(Button button, string label)
        {
            var text = button.GetComponentInChildren<Text>(true);
            if (text == null) return;

            text.text = label;
            text.color = Color.black;
            text.resizeTextForBestFit = true;
        }

        private static void BindHubController(ScoutHubUIController hubController, Button playerTabButton,
            Button cheerleaderTabButton, GameObject playerSection, GameObject cheerleaderSection)
        {
            var serialized = new SerializedObject(hubController);
            serialized.FindProperty("playerTabButton").objectReferenceValue = playerTabButton;
            serialized.FindProperty("cheerleaderTabButton").objectReferenceValue = cheerleaderTabButton;
            serialized.FindProperty("playerSection").objectReferenceValue = playerSection;
            serialized.FindProperty("cheerleaderSection").objectReferenceValue = cheerleaderSection;
            serialized.ApplyModifiedProperties();
        }

        /// <summary>[TASK-KBO-141] 씬 전체에서 `T` 타입 컴포넌트를 전수 검색해 `keep` 외의 사본을 전부
        /// `DestroyImmediate`로 제거한다 - "탭 전환 시 겹침"처럼 코드는 멀쩡한데 재현되는 버그의
        /// 흔한 원인(어딘가가 정본이 아닌 사본을 참조 중)을 원천 차단한다. 사본이 없으면 아무 일도
        /// 하지 않는다.</summary>
        private static void DestroyDuplicateInstances<T>(T keep) where T : Component
        {
            var all = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (all.Length <= 1) return;

            Debug.LogWarning($"[SetupScoutHubUI] {typeof(T).Name} 중복 {all.Length}개 발견 - 정본 " +
                $"{GetHierarchyPath(keep.transform)} 하나만 남기고 나머지를 제거합니다.");

            foreach (var instance in all)
            {
                if (instance == keep) continue;

                Debug.LogWarning($"[SetupScoutHubUI]   - 중복 제거: {GetHierarchyPath(instance.transform)}");
                Object.DestroyImmediate(instance.gameObject);
            }
        }

        /// <summary>[TASK-KBO-141, 명령서 6항] `hubController`에 실제로 저장된 `playerSection`/
        /// `cheerleaderSection` 값을 `SerializedObject`로 다시 읽어(방금 쓴 값이 아니라 저장된 값을
        /// 재확인) 기대값과 정확히 일치하는지, 서로 다른 오브젝트인지까지 검증하고 콘솔에 결과를
        /// 남긴다.</summary>
        private static void LogHubBindingVerification(ScoutHubUIController hubController,
            GameObject expectedPlayerSection, GameObject expectedCheerleaderSection)
        {
            var serialized = new SerializedObject(hubController);
            var boundPlayerSection = serialized.FindProperty("playerSection").objectReferenceValue as GameObject;
            var boundCheerleaderSection = serialized.FindProperty("cheerleaderSection").objectReferenceValue as GameObject;

            bool ok = boundPlayerSection == expectedPlayerSection
                && boundCheerleaderSection == expectedCheerleaderSection
                && boundPlayerSection != boundCheerleaderSection;

            if (ok)
            {
                Debug.Log("[SetupScoutHubUI] ScoutHub 바인딩 갱신 완료 - " +
                    $"playerSection={GetHierarchyPath(boundPlayerSection.transform)}, " +
                    $"cheerleaderSection={GetHierarchyPath(boundCheerleaderSection.transform)}");
            }
            else
            {
                Debug.LogError("[SetupScoutHubUI] ScoutHub 바인딩 검증 실패 - playerSection/" +
                    "cheerleaderSection이 기대한 오브젝트와 다르거나 서로 같습니다. 씬을 직접 확인하십시오.");
            }
        }

        private static string GetHierarchyPath(Transform t)
        {
            var path = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }

        private static Button FindOrCreateScoutButton(Transform parent)
        {
            var existingChild = parent.Find(ScoutButtonName);
            if (existingChild != null)
            {
                var existingButton = existingChild.GetComponent<Button>();
                if (existingButton != null)
                {
                    ApplyButtonLabel(existingButton, "스카우트");
                    return existingButton;
                }
            }

            var buttonObject = new GameObject(ScoutButtonName, typeof(RectTransform), typeof(Image), typeof(Button));
            Undo.RegisterCreatedObjectUndo(buttonObject, $"Create {ScoutButtonName}");
            buttonObject.transform.SetParent(parent, false);

            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(160f, 50f);
            rect.anchoredPosition = new Vector2(20f, 80f);

            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.9f, 0.9f, 0.9f);

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            Undo.RegisterCreatedObjectUndo(labelObject, $"Create {ScoutButtonName} Label");
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
            ApplyButtonLabel(button, "스카우트");

            return button;
        }

        private static void BindButtonField(Object controller, string fieldName, Button button)
        {
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty(fieldName).objectReferenceValue = button;
            serializedController.ApplyModifiedProperties();
        }

        /// <summary>이름으로 자식을 찾아 존재하면 DestroyImmediate로 완전히 제거한다. 명령서에서 더
        /// 이상 쓰지 않기로 한 구 UI 요소를 정리할 때만 쓴다(명령서 6항 - 안전한 재조립).</summary>
        private static void DestroyLegacyChild(Transform parent, string name)
        {
            var legacy = parent.Find(name);
            if (legacy != null) Object.DestroyImmediate(legacy.gameObject);
        }

        /// <summary>
        /// [TASK-KBO-137] 허브 우측 상단 모서리에 고정 픽셀 크기(`size`x`size`)의 단순 'X' 버튼을
        /// 절대 배치한다(`SetupUpgradeUI.FindOrCreateCloseButton()`/`SetupInventoryUI.
        /// FindOrCreateCloseButton()`과 동일한 패턴 - 각 Setup*.cs 파일이 자체 헬퍼를 갖는 이 코드베이스
        /// 관례를 따라 이 파일에도 독립적으로 둔다). `ScoutHubPanel`에는 레이아웃 그룹이 없어
        /// `LayoutElement.ignoreLayout`이 당장은 아무 효과가 없지만, 추후 레이아웃 그룹이 추가되더라도
        /// 이 버튼만은 항상 절대 위치를 유지하도록 미리 방어해 둔다.
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

        /// <summary>[TASK-KBO-129] 구 ScreenType.CheerleaderShop 항목이 UIManager.screens에 남아있으면
        /// 배열에서 제거한다 - 이제 이 화면은 ScoutHubPanel 하위의 섹션일 뿐 독립된 ShowScreen() 대상이
        /// 아니다. 항목이 없으면 아무 일도 하지 않는다.</summary>
        private static void RemoveCheerleaderShopScreenEntry(UIManager uiManager)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                var typeProperty = element.FindPropertyRelative("Type");
                if (typeProperty.intValue == (int)ScreenType.CheerleaderShop)
                {
                    screensProperty.DeleteArrayElementAtIndex(i);
                    serializedManager.ApplyModifiedProperties();
                    return;
                }
            }
        }
    }
}
