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
        private const string TemplatesHolderName = "_Templates";
        private const string PlayerCardTemplateName = "PlayerCardTemplate";
        private const string ItemEntryTemplateName = "ItemEntryTemplate";

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

            var titleText = FindOrCreateText(contentPanel, TitleTextName, "",
                new Vector2(0f, 0.85f), new Vector2(1f, 1f));
            var selectionCountText = FindOrCreateText(contentPanel, SelectionCountTextName, "",
                new Vector2(0f, 0.1f), new Vector2(1f, 0.15f));
            // [TASK-KBO-123] 확인/취소 버튼을 전용 컨테이너(HorizontalLayoutGroup)로 정렬한다. 과거
            // 버전에서 팝업 직속 자식으로 고정 픽셀 앵커에 만들어져 있던 두 버튼은 이 컨테이너 하위로
            // 옮겨 재사용한다(명령서 6항 - 중복 생성 방지).
            var actionContainer = FindOrCreateActionContainer(contentPanel);
            var confirmButton = FindOrCreateButton(actionContainer, ConfirmButtonName, "강화/각성 실행",
                Vector2.zero, Vector2.zero);
            var cancelButton = FindOrCreateButton(actionContainer, CancelButtonName, "닫기",
                Vector2.zero, Vector2.zero);

            var (playerListPanel, playerListContainer) = FindOrCreateListPanel(contentPanel,
                PlayerListPanelName, PlayerListContainerName, new Vector2(140f, 200f), new Vector2(10f, 10f));
            var (itemListPanel, itemListContainer) = FindOrCreateListPanel(contentPanel,
                ItemListPanelName, ItemListContainerName, new Vector2(160f, 60f), new Vector2(10f, 10f));

            var playerCardPrefab = FindOrCreatePlayerCardTemplate(canvas.transform);
            var itemEntryPrefab = FindOrCreateItemEntryTemplate(canvas.transform);

            var gameActionController = Object.FindAnyObjectByType<GameActionController>(FindObjectsInactive.Include);
            if (gameActionController == null)
            {
                Debug.LogWarning("[SetupUpgradeUI] 씬에서 GameActionController를 찾지 못해 " +
                    "gameActionController 바인딩을 건너뜁니다.");
            }

            BindController(controller, gameActionController, titleText, selectionCountText, confirmButton, cancelButton,
                playerListPanel, playerListContainer, playerCardPrefab, itemListPanel, itemListContainer, itemEntryPrefab);

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

            return contentTransform;
        }

        /// <summary>
        /// [TASK-KBO-123] 확인/취소 버튼을 담을 하단 컨테이너. 명령서 4항이 지정한 좌표(anchorMin 0,0 ~
        /// anchorMax 1,0.15)에 `HorizontalLayoutGroup`(spacing 20, `MiddleCenter`)을 부착한다. 과거
        /// 버전에서 팝업(`MaterialSelectPopup`)의 직속 자식으로 고정 픽셀 좌표에 만들어져 있던
        /// `ConfirmButton`/`CancelButton`은 이 컨테이너 하위로 이동시켜 중복 생성을 막는다(명령서 6항).
        /// </summary>
        private static Transform FindOrCreateActionContainer(Transform popupTransform)
        {
            var existingContainer = popupTransform.Find(ActionContainerName);
            Transform containerTransform;
            if (existingContainer != null)
            {
                containerTransform = existingContainer;
            }
            else
            {
                var containerObject = new GameObject(ActionContainerName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(containerObject, $"Create {ActionContainerName}");
                containerObject.transform.SetParent(popupTransform, false);

                var rect = (RectTransform)containerObject.transform;
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(1f, 0.15f);
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

        private static (GameObject panel, Transform container) FindOrCreateListPanel(Transform parent,
            string panelName, string containerName, Vector2 cellSize, Vector2 spacing)
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

            var containerTransform = FindOrCreateGridContainer(panelObject.transform, containerName, cellSize, spacing);

            return (panelObject, containerTransform);
        }

        private static Transform FindOrCreateGridContainer(Transform parent, string name, Vector2 cellSize, Vector2 spacing)
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
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            if (!containerObject.TryGetComponent<GridLayoutGroup>(out var grid))
            {
                grid = containerObject.AddComponent<GridLayoutGroup>();
                grid.cellSize = cellSize;
                grid.spacing = spacing;
            }

            return containerObject.transform;
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
            GameObject playerListPanel, Transform playerListContainer, PlayerCardUI playerCardPrefab,
            GameObject itemListPanel, Transform itemListContainer, Button itemEntryPrefab)
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

            serialized.FindProperty("itemListPanel").objectReferenceValue = itemListPanel;
            serialized.FindProperty("itemListContainer").objectReferenceValue = itemListContainer;
            serialized.FindProperty("itemEntryPrefab").objectReferenceValue = itemEntryPrefab;

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
            text.alignment = TextAnchor.MiddleCenter;
            // [TASK-KBO-128] ContentPanel 신설로 이 텍스트의 실제 배경이 불투명 흰색이 되어(딤 배경
            // 위가 아님), 검은색으로 대비시킨다 - 흰색으로 두면 흰 배경 위에서 안 보인다.
            text.color = Color.black;
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }
    }
}
