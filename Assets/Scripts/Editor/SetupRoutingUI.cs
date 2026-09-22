using KBOManager.Controllers;
using KBOManager.Managers;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-061] TASK-KBO-060에서 코드로만 연결해 둔 "로비 <-> 치어리더 인벤토리" 화면 전환을
    /// QA가 메뉴 클릭 한 번으로 실제 씬에 배선할 수 있게 하는 에디터 자동화. 로비 패널에 "치어리더 관리"
    /// 버튼, 인벤토리 패널에 "닫기" 버튼을 만들어 각 컨트롤러 필드에 바인딩하고, UIManager.screens에
    /// CheerleaderInventory 화면을 등록한다. SceneInitializer/ItemDataSeeder/SetupLobbyUI/
    /// SetupCheerleaderUI와 동일한 관례로 여러 번 실행해도 안전하다(이미 있으면 찾아 재사용/덮어쓰기).
    /// </summary>
    public static class SetupRoutingUI
    {
        private const string ManageCheerleaderButtonName = "ManageCheerleaderButton";
        private const string CloseButtonName = "CloseButton";

        [MenuItem("KBO Manager/Setup/Auto-Connect Routing UI")]
        public static void AutoConnectRoutingUI()
        {
            // 두 패널 모두 화면 전환에 따라 비활성 상태일 수 있어(예: 인벤토리 패널이 아직 한 번도
            // 안 열렸거나, 로비가 아닌 다른 화면이 떠 있는 상태) FindObjectsInactive.Include로 찾는다.
            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            var inventory = Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include);
            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);

            if (dashboard == null)
            {
                Debug.LogWarning("[SetupRoutingUI] 씬에서 LeagueDashboardUIController를 찾지 못해 " +
                    "'치어리더 관리' 버튼 생성을 건너뜁니다.");
            }
            else
            {
                var manageButton = FindOrCreateButton(dashboard.transform, ManageCheerleaderButtonName,
                    "치어리더 관리", new Vector2(20f, 20f));
                BindButtonField(dashboard, "manageCheerleaderButton", manageButton);
                EditorUtility.SetDirty(dashboard);
            }

            if (inventory == null)
            {
                Debug.LogWarning("[SetupRoutingUI] 씬에서 CheerleaderInventoryUIController를 찾지 못해 " +
                    "'닫기' 버튼 생성 및 UIManager 등록을 건너뜁니다.");
            }
            else
            {
                var closeButton = FindOrCreateButton(inventory.transform, CloseButtonName,
                    "닫기", new Vector2(20f, 20f));
                BindButtonField(inventory, "closeButton", closeButton);
                EditorUtility.SetDirty(inventory);
            }

            if (uiManager == null)
            {
                Debug.LogWarning("[SetupRoutingUI] 씬에서 UIManager를 찾지 못해 screens 등록을 건너뜁니다.");
            }
            else if (inventory != null)
            {
                RegisterInventoryScreen(uiManager, inventory.gameObject);
                EditorUtility.SetDirty(uiManager);
            }

            var scene = ResolveTargetScene(dashboard, inventory, uiManager);
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log("[SetupRoutingUI] 라우팅 UI 자동 연결 완료.");
        }

        private static Scene ResolveTargetScene(LeagueDashboardUIController dashboard,
            CheerleaderInventoryUIController inventory, UIManager uiManager)
        {
            if (dashboard != null) return dashboard.gameObject.scene;
            if (inventory != null) return inventory.gameObject.scene;
            if (uiManager != null) return uiManager.gameObject.scene;
            return default;
        }

        /// <summary>이름으로 기존 버튼을 재사용하거나, 없으면 Image+Button 루트와 전체를 채우는 Text
        /// 라벨로 새로 조립한다(SceneInitializer.FindOrCreateDebugButton()과 동일한 관례). 앵커/미세
        /// 디자인은 명령서 5항 지시대로 대략적인 좌하단 배치만 적용한다.</summary>
        private static Button FindOrCreateButton(Transform parent, string name, string label, Vector2 anchoredPosition)
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
            text.font = KBOFonts.Default;

            return button;
        }

        private static void BindButtonField(Object controller, string fieldName, Button button)
        {
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty(fieldName).objectReferenceValue = button;
            serializedController.ApplyModifiedProperties();
        }

        /// <summary>
        /// UIManager.screens(List&lt;ScreenEntry&gt;, 필드는 Type/Root 두 개 - SceneInitializer.
        /// BindUIManagerScreens()에서 이미 검증된 실제 필드명)에 CheerleaderInventory 항목을 등록한다.
        /// 이미 등록되어 있으면(같은 Type을 가진 원소가 있으면) 새로 추가하지 않고 Root 참조만
        /// 최신 오브젝트로 덮어써 중복 생성을 막는다(명령서 7항).
        /// </summary>
        private static void RegisterInventoryScreen(UIManager uiManager, GameObject inventoryRoot)
        {
            var serializedManager = new SerializedObject(uiManager);
            var screensProperty = serializedManager.FindProperty("screens");

            for (int i = 0; i < screensProperty.arraySize; i++)
            {
                var element = screensProperty.GetArrayElementAtIndex(i);
                var typeProperty = element.FindPropertyRelative("Type");
                if (typeProperty.intValue == (int)ScreenType.CheerleaderInventory)
                {
                    element.FindPropertyRelative("Root").objectReferenceValue = inventoryRoot;
                    serializedManager.ApplyModifiedProperties();
                    return;
                }
            }

            int newIndex = screensProperty.arraySize;
            screensProperty.arraySize++;

            var newElement = screensProperty.GetArrayElementAtIndex(newIndex);
            newElement.FindPropertyRelative("Type").intValue = (int)ScreenType.CheerleaderInventory;
            newElement.FindPropertyRelative("Root").objectReferenceValue = inventoryRoot;

            serializedManager.ApplyModifiedProperties();
        }
    }
}
