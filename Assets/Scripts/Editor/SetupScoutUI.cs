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
    /// </summary>
    public static class SetupScoutUI
    {
        private const string CanvasName = "Canvas";
        private const string ScoutPanelName = "ScoutPanel";
        private const string ScoutButtonName = "ScoutButton";
        private const string CloseButtonName = "CloseButton";
        private const string Roll1ButtonName = "Roll1Button";
        private const string Roll10ButtonName = "Roll10Button";
        private const string ResultPopupName = "ResultPopup";
        private const string ClosePopupButtonName = "ClosePopupBtn";
        private const string CardContainerName = "CardContainer";
        private const string TemplatesHolderName = "_Templates";
        private const string CardTemplateName = "PlayerCardTemplate";

        [MenuItem("KBO Manager/Setup/Auto-Connect Scout UI")]
        public static void AutoConnectScoutUI()
        {
            var canvas = EnsureCanvas();
            var scoutController = FindOrCreateScoutPanel(canvas.transform);
            BindScoutController(scoutController);
            BindRollButtons(scoutController);

            var (resultPopupRoot, cardContainer, closeResultPopupButton) = FindOrCreateResultPopup(scoutController.transform);
            var cardTemplate = FindOrCreateCardTemplate(canvas.transform);
            BindScoutResultFields(scoutController, resultPopupRoot, cardContainer, cardTemplate, closeResultPopupButton);

            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupScoutUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "'스카우트' 진입 버튼 생성 및 UIManager 등록을 건너뜁니다.");
            }
            else
            {
                var scoutButton = FindOrCreateButton(dashboard.transform, ScoutButtonName, "스카우트", new Vector2(20f, 80f));
                BindButtonField(dashboard, "scoutButton", scoutButton);
                EditorUtility.SetDirty(dashboard);

                var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
                if (uiManager == null)
                {
                    Debug.LogWarning("[SetupScoutUI] 씬에서 UIManager를 찾지 못해 screens 등록을 건너뜁니다.");
                }
                else
                {
                    RegisterScoutScreen(uiManager, scoutController.gameObject);
                    EditorUtility.SetDirty(uiManager);
                }
            }

            EditorUtility.SetDirty(scoutController);

            var scene = dashboard != null ? dashboard.gameObject.scene : scoutController.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupScoutUI] 스카우트 UI 자동 배선 완료.");
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

        /// <summary>[TASK-KBO-092] 1회/10회 뽑기 버튼을 만들어 ScoutUIController.roll1Button/roll10Button에
        /// 바인딩한다. 실제 onClick 연결(ExecuteRoll1/ExecuteRoll10)은 ScoutUIController.Awake()가 담당한다
        /// (closeButton과 동일한 관례 - 에디터 스크립트는 필드 참조만 채운다).</summary>
        private static void BindRollButtons(ScoutUIController controller)
        {
            var roll1Button = FindOrCreateButton(controller.transform, Roll1ButtonName, "선수 1회 뽑기", new Vector2(200f, 20f));
            BindButtonField(controller, "roll1Button", roll1Button);

            // [TASK-KBO-117] 치어리더 뽑기(SetupShopUI.cs)의 "10회 뽑기"와 라벨이 거의 동일해 혼동을
            // 유발했다 - "선수"를 명시해 구분한다.
            var roll10Button = FindOrCreateButton(controller.transform, Roll10ButtonName, "선수 10연차 뽑기", new Vector2(380f, 20f));
            BindButtonField(controller, "roll10Button", roll10Button);
        }

        /// <summary>[TASK-KBO-092/093] ScoutPanel 하위에 결과 팝업(ResultPopup)과 그 안의 카드 컨테이너
        /// (GridLayoutGroup), 그리고 팝업을 닫는 "확인" 버튼(ClosePopupBtn)을 조립한다. 팝업은 평소 숨겨져
        /// 있다가 ScoutUIController.ShowResults()가 뽑기 시점에 활성화한다(기존 런타임 로직, 여기서는 최초
        /// 생성 시 초기 상태만 비활성으로 맞춘다). [TASK-KBO-093] TASK-092가 남긴 "팝업을 닫을 버튼이
        /// 없다"는 UX 블로커를 여기서 해소한다.</summary>
        private static (GameObject popupRoot, Transform container, Button closeButton) FindOrCreateResultPopup(Transform scoutPanelTransform)
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
            var closeButton = FindOrCreateButton(popupObject.transform, ClosePopupButtonName, "확인", new Vector2(300f, 20f));

            return (popupObject, container, closeButton);
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

        /// <summary>ScoutUIController의 resultPopupRoot/cardContainer/cardPrefab/closeResultPopupButton
        /// 4개 필드를 바인딩한다.</summary>
        private static void BindScoutResultFields(ScoutUIController controller, GameObject resultPopupRoot,
            Transform cardContainer, PlayerCardUI cardTemplate, Button closeResultPopupButton)
        {
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("resultPopupRoot").objectReferenceValue = resultPopupRoot;
            serializedController.FindProperty("cardContainer").objectReferenceValue = cardContainer;
            serializedController.FindProperty("cardPrefab").objectReferenceValue = cardTemplate;
            serializedController.FindProperty("closeResultPopupButton").objectReferenceValue = closeResultPopupButton;
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
        /// 오브젝트로 덮어써 중복 추가를 막는다(명령서 7항).
        /// </summary>
        private static void RegisterScoutScreen(UIManager uiManager, GameObject scoutRoot)
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
