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
    /// </summary>
    public static class SetupUpgradeUI
    {
        private const string PopupName = "MaterialSelectPopup";
        private const string TitleTextName = "TitleText";
        private const string SelectionCountTextName = "SelectionCountText";
        private const string ConfirmButtonName = "ConfirmButton";
        private const string CancelButtonName = "CancelButton";
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

            var titleText = FindOrCreateText(controller.transform, TitleTextName, "",
                new Vector2(0f, 0.85f), new Vector2(1f, 1f));
            var selectionCountText = FindOrCreateText(controller.transform, SelectionCountTextName, "",
                new Vector2(0f, 0.1f), new Vector2(1f, 0.15f));
            var confirmButton = FindOrCreateButton(controller.transform, ConfirmButtonName, "확인",
                new Vector2(0.3f, 0f), new Vector2(0.5f, 0.1f));
            var cancelButton = FindOrCreateButton(controller.transform, CancelButtonName, "취소",
                new Vector2(0.5f, 0f), new Vector2(0.7f, 0.1f));

            var (playerListPanel, playerListContainer) = FindOrCreateListPanel(controller.transform,
                PlayerListPanelName, PlayerListContainerName, new Vector2(140f, 200f), new Vector2(10f, 10f));
            var (itemListPanel, itemListContainer) = FindOrCreateListPanel(controller.transform,
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

            popupObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            return popupObject.AddComponent<MaterialSelectUIController>();
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
                if (existingButton != null) return existingButton;
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
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 18;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return button;
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
            text.color = Color.white; // 팝업 배경(반투명 검정)이 어두우므로 흰색으로 대비시킨다.
            text.fontSize = 16;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.raycastTarget = false;

            return text;
        }
    }
}
