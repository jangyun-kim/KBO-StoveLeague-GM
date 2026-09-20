using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-119] 카드 템플릿 텍스트 가독성 핫픽스 + 사실 재검증.
    ///
    /// [사실 정정] 명령서 4항은 `_Templates/PlayerCardTemplate.prefab`(프로젝트 에셋)을 로드해
    /// `PrefabUtility.SavePrefabAsset()`으로 저장하라고 지시했으나, `Assets` 전체를 `PlayerCardTemplate`
    /// 로 전수 검색한 결과 그런 `.prefab` 에셋 파일은 존재하지 않는다 - `PlayerCardTemplate`은
    /// `SetupScoutUI.cs`(TASK-092)가 씬의 `Canvas/_Templates` 하위에 `new GameObject(...)`로 직접 만든
    /// **씬 오브젝트**이며(`ScoutUIController.SpawnCard()`/`InventoryUIController.SpawnCard()`가 이를
    /// `Object.Instantiate()`로 복제해 카드를 찍어낸다), 프리팹 에셋으로 저장된 적이 한 번도 없다.
    /// 이 파일은 실제 존재하는 씬 오브젝트를 대상으로 동일한 목적(가독성 보장)을 달성한다.
    ///
    /// [사실 정정] 명령서 3항/4항이 전제한 "데이터 바인딩 누락"/"흰색 폰트"도 재검증 결과 이미 해소돼
    /// 있었다 - `PlayerCardUI.Setup(Player)`(원문 56~78행)가 `nameText`/`teamText`/`positionText`/
    /// `ovrText`를 전부 실제 `Player` 데이터로 채우고 있고, `ScoutUIController.SpawnCard()`/
    /// `InventoryUIController.SpawnCard()` 둘 다 `Instantiate` 직후 `card.Setup(player)`를 이미
    /// 호출하고 있었다(코드 변경 불필요). `_Templates/PlayerCardTemplate`의 `NameText` 원본 색상도
    /// 씬 재조회 결과 이미 `(0,0,0,1)`(검은색)이었다 - `Setup()`은 `.text`만 채울 뿐 `.color`는 건드리지
    /// 않으므로, 런타임에 복제되는 카드들도 전부 이 원본의 검은색을 그대로 물려받는다.
    /// </summary>
    public static class SetupTemplates
    {
        private const string TemplatesHolderName = "_Templates";
        private const string PlayerCardTemplateName = "PlayerCardTemplate";

        [MenuItem("KBO Manager/Setup/Fix Player Card Template Color")]
        public static void FixPlayerCardTemplateColor()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[SetupTemplates] 씬에서 Canvas를 찾지 못했습니다.");
                return;
            }

            var holderTransform = canvas.transform.Find(TemplatesHolderName);
            var templateTransform = holderTransform != null ? holderTransform.Find(PlayerCardTemplateName) : null;
            if (templateTransform == null)
            {
                Debug.LogWarning($"[SetupTemplates] '{TemplatesHolderName}/{PlayerCardTemplateName}'를 찾지 못했습니다. " +
                    "먼저 'KBO Manager/Setup/Auto-Connect Scout UI'를 실행해 카드 템플릿을 생성하십시오.");
                return;
            }

            var texts = templateTransform.GetComponentsInChildren<Text>(true);
            foreach (var text in texts)
            {
                text.color = Color.black;
                text.resizeTextForBestFit = true;
                EditorUtility.SetDirty(text);
            }

            var scene = canvas.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[SetupTemplates] '{PlayerCardTemplateName}' 하위 Text {texts.Length}개를 검은색으로 " +
                "고정했습니다(이 템플릿을 복제하는 모든 카드 - 스카우트 결과/인벤토리/기록실 1위 카드 등 - 에 " +
                "동일하게 적용됩니다).");
        }
    }
}
