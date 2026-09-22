using KBOManager.UI;
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
    ///
    /// [TASK-KBO-151, 사실 정정] 명령서는 "`PlayerCardTemplate` 생성 시 Portrait를 만들라"고 지시했으나,
    /// 위 문단이 이미 확인했듯 이 파일은 `PlayerCardTemplate`을 생성하지 않는다 - 실제 생성자는
    /// `SetupScoutUI.FindOrCreateCardTemplate()`이다. 그 파일은 "스카우트"라는, 이전 태스크들이 반복적으로
    /// "절대 건드리지 말 것"으로 지정해 온 안정화 영역이라 명령서 0항 정신에 따라 손대지 않았다 - 대신
    /// 이 파일이 원래부터 하던 일("이미 존재하는 템플릿을 찾아 사후 패치") 그대로, `Portrait` 자식이
    /// 없으면 추가하고 `PlayerCardUI.portraitImage`에 바인딩하는 새 메뉴(`FixPlayerCardTemplatePortrait()`)
    /// 를 신설했다 - 템플릿은 씬에 단 하나만 존재하고 모든 카드가 이를 `Instantiate()`로 복제하므로,
    /// 이 메뉴 한 번 실행으로 스카우트/인벤토리/상점/선수 관리 허브/강화 등 카드가 쓰이는 모든 화면에
    /// 동시에 반영된다.
    /// </summary>
    public static class SetupTemplates
    {
        private const string TemplatesHolderName = "_Templates";
        private const string PlayerCardTemplateName = "PlayerCardTemplate";
        private const string PortraitName = "Portrait";

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

        /// <summary>
        /// [TASK-KBO-151] `PlayerCardTemplate`에 실제 선수 초상화가 그려질 `Portrait`(`Image`) 자식을
        /// 없으면 새로 만들고, `PlayerCardUI.portraitImage`에 바인딩한다 - `PlayerCardUI.Setup()`(TASK-147)의
        /// `Resources.Load&lt;Sprite&gt;("Portraits/{TemplateId}")` 로딩 로직 자체는 이미 존재했지만,
        /// 그 값을 대입할 `Image` 컴포넌트가 템플릿에 없어 그림이 그려질 자리 자체가 없었다(명령서 3항
        /// 진단 그대로).
        ///
        /// [명령서 4항 - Z-Order] `Portrait`를 항상 `SetAsFirstSibling()`으로 강제한다 - 카드 루트
        /// 자신의 `Image`(배경/`frameImage`)는 부모이고 형제가 아니므로 "형제 목록의 첫 번째"가 곧
        /// "배경 바로 다음(=배경 위, 다른 자식들보다는 아래)"과 같은 뜻이 된다. 유니티는 부모를 먼저
        /// 그리고 자식은 sibling index 순서(작은 값부터)로 그리므로, `Portrait`가 index 0이면 배경 위에
        /// 그려지면서도 그 뒤의 `NameText`/`TeamText`/`OvrText`/별 아이콘 등 나머지 모든 자식보다는
        /// 먼저(=아래에) 그려져 글씨를 가리지 않는다. 여러 번 실행해도 안전(idempotent) - 이미 존재하는
        /// `Portrait`를 찾으면 위치만 다시 강제한다.
        ///
        /// [명령서 6항 - 폴백 논리 점검] `PlayerCardUI.SetupPortrait()`(TASK-147)는 `Resources.Load()`가
        /// null이면 `fallbackPortraitSprite`로 대체하고, 그것마저 null이면 `portraitImage.enabled = false`로
        /// 꺼서 빈 흰 사각형이 노출되지 않게 막는다 - 이 메서드가 `fallbackPortraitSprite`에 아무 값도
        /// 대입하지 않으므로(에셋이 아직 없음, TASK-147/150에서 사용자가 "다음 단계"로 유보), 실제
        /// 이미지 리소스가 생기기 전까지는 `Portrait`가 항상 비활성 상태로 조용히 숨어 있다 - 즉 지금
        /// 이 메뉴를 실행해도 카드 비주얼이 바뀌지는 않으며, `Resources/Portraits/{TemplateId}` 이미지나
        /// 폴백 스프라이트를 나중에 추가하는 순간부터 자동으로 나타난다(코드 변경 불필요).
        /// </summary>
        [MenuItem("KBO Manager/Setup/Fix Player Card Template Portrait")]
        public static void FixPlayerCardTemplatePortrait()
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

            var cardUI = templateTransform.GetComponent<PlayerCardUI>();
            if (cardUI == null)
            {
                Debug.LogError($"[SetupTemplates] '{PlayerCardTemplateName}'에 PlayerCardUI 컴포넌트가 없습니다 - " +
                    "씬 상태를 확인하십시오.");
                return;
            }

            var portraitImage = FindOrCreatePortraitImage(templateTransform);

            var serializedCard = new SerializedObject(cardUI);
            serializedCard.FindProperty("portraitImage").objectReferenceValue = portraitImage;
            serializedCard.ApplyModifiedProperties();
            EditorUtility.SetDirty(cardUI);

            var scene = canvas.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            bool portraitBound = new SerializedObject(cardUI).FindProperty("portraitImage").objectReferenceValue == portraitImage;
            if (portraitBound)
            {
                Debug.Log($"[SetupTemplates] '{PlayerCardTemplateName}'에 Portrait 프레임 추가 및 바인딩 성공 - " +
                    "이 템플릿을 복제하는 모든 화면(스카우트/인벤토리/선수 관리 허브/강화 등)에 즉시 반영됩니다. " +
                    "Resources/Portraits/{TemplateId} 이미지나 fallbackPortraitSprite가 아직 없으면 " +
                    "Portrait는 자동으로 비활성 상태로 숨어 있습니다(정상 동작, 명령서 6항 점검 결과).");
            }
            else
            {
                Debug.LogError("[SetupTemplates] Portrait 바인딩 실패 - portraitImage가 여전히 다른 값입니다.");
            }
        }

        /// <summary>`cardTransform`(PlayerCardTemplate) 바로 아래에 `Portrait` `Image`를 찾거나 만든다.
        /// 카드 전체를 꽉 채우도록 앵커를 스트레치하고, 텍스트/별 아이콘을 가리지 않도록 항상 형제
        /// 목록의 맨 앞(sibling index 0)으로 강제한다(명령서 4항).</summary>
        private static Image FindOrCreatePortraitImage(Transform cardTransform)
        {
            var existing = cardTransform.Find(PortraitName);
            GameObject portraitObject;
            if (existing != null)
            {
                portraitObject = existing.gameObject;
            }
            else
            {
                portraitObject = new GameObject(PortraitName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(portraitObject, $"Create {PortraitName}");
                portraitObject.transform.SetParent(cardTransform, false);
            }

            var rect = (RectTransform)portraitObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            if (!portraitObject.TryGetComponent<Image>(out var image))
            {
                image = portraitObject.AddComponent<Image>();
            }
            image.color = Color.white;
            image.raycastTarget = false;
            image.preserveAspect = false;

            // [명령서 4항] 배경(카드 루트 자신의 Image) 바로 다음, 텍스트/등급 아이콘들보다는 앞서
            // 그려지도록 항상 맨 앞 sibling으로 고정한다.
            portraitObject.transform.SetAsFirstSibling();

            return image;
        }
    }
}
