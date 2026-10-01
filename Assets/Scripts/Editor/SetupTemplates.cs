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
        private const string PortraitBGName = "PortraitBG";
        private const string FrameOverlayName = "FrameOverlay";

        /// <summary>
        /// [TASK-KBO-170, 배치 실행용] 메뉴는 에디터 GUI에서 사람이 클릭해야 실행되므로, 에디터를 열지
        /// 않고 `Unity.exe -batchmode -executeMethod`로 씬에 4단 레이어/가독성 보강을 일괄 적용하기 위한
        /// 진입점이다. 대상 씬을 직접 열고 -&gt; `SetupDualPortraitAndGradeFrameLayers()`를 실행하고 -&gt;
        /// 씬을 저장하고 종료한다(대화형 메뉴들과 달리 `MarkSceneDirty`만으로는 배치 프로세스 종료 시
        /// 디스크에 반영되지 않으므로 `EditorSceneManager.SaveScene`을 명시적으로 호출).
        /// </summary>
        public static void RunBatchRenderPolish()
        {
            const string scenePath = "Assets/Scenes/SampleScene.unity";
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            SetupDualPortraitAndGradeFrameLayers();

            bool saved = EditorSceneManager.SaveScene(scene);
            Debug.Log(saved
                ? "[SetupTemplates] RunBatchRenderPolish 완료 - 씬 저장 성공."
                : "[SetupTemplates] RunBatchRenderPolish 완료했으나 씬 저장 실패.");
        }

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

        /// <summary>
        /// [TASK-KBO-168] `PlayerCardTemplate`에 4단 레이어(배경/배경인물(듀얼샷)/메인인물/등급테두리)를
        /// 완성하기 위해 `PortraitBG`(배경 인물)와 `FrameOverlay`(등급별 테두리) `Image` 자식을 없으면
        /// 새로 만들고, `PlayerCardUI.portraitBGImage`/`frameOverlayImage`에 바인딩한다.
        ///
        /// [Z-Order] 유니티는 부모(카드 루트 자신의 `frameImage`)를 먼저 그리고, 그 다음 자식들을
        /// sibling index 순서(작은 값부터)로 그린다. 명령서가 요구한 순서를 그대로 sibling index로
        /// 강제한다: `PortraitBG`(0, 맨 뒤) → `Portrait`(1, TASK-KBO-151이 이미 만들어 둔 것을 재사용) →
        /// `FrameOverlay`(2, 맨 앞) → 그 뒤의 기존 자식들(NameText/TeamText/별 아이콘 등, 전혀 손대지
        /// 않음 - `SetSiblingIndex`는 지정한 인덱스보다 뒤에 있던 요소들을 밀어낼 뿐 순서를 바꾸지 않는다).
        /// 여러 번 실행해도 안전(idempotent) - 이미 존재하는 두 자식을 찾으면 위치만 다시 강제한다.
        /// </summary>
        [MenuItem("KBO Manager/Setup/Setup Dual Portrait And Grade Frame Layers")]
        public static void SetupDualPortraitAndGradeFrameLayers()
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

            // [명령서 3-1항] Portrait가 아직 없는 프로젝트 상태(TASK-151 메뉴 미실행)도 안전하게
            // 지원하기 위해 여기서도 찾거나 만든다 - PortraitBG/FrameOverlay와 함께 한 번에 정리된다.
            var portraitImage = FindOrCreatePortraitImage(templateTransform);
            var portraitBGImage = FindOrCreatePortraitBGImage(templateTransform);
            var frameOverlayImage = FindOrCreateFrameOverlayImage(templateTransform);

            portraitBGImage.transform.SetSiblingIndex(0);
            portraitImage.transform.SetSiblingIndex(1);
            frameOverlayImage.transform.SetSiblingIndex(2);

            var serializedCard = new SerializedObject(cardUI);
            serializedCard.FindProperty("portraitBGImage").objectReferenceValue = portraitBGImage;
            serializedCard.FindProperty("frameOverlayImage").objectReferenceValue = frameOverlayImage;
            // [TASK-KBO-176] TASK-173이 PlayerCardUI.setDeckScoreText("SD n")를 추가했지만 이를 만드는 Setup이 없어 카드에
            // 세트덱 스코어가 한 번도 표시되지 않았다 - OVR 바로 아래에 만들어 바인딩한다(아래 가독성 보강 대상에도 포함됨).
            serializedCard.FindProperty("setDeckScoreText").objectReferenceValue = FindOrCreateSetDeckScoreText(templateTransform);
            serializedCard.ApplyModifiedProperties();
            EditorUtility.SetDirty(cardUI);

            // [TASK-KBO-170] 화려한 프레임/인물 사진 위에 텍스트가 올라가는 레이아웃이 이제 실제로
            // 완성됐으니, 같은 타이밍에 텍스트 가독성도 함께 보장한다(아래 EnsureCardTextReadability 참고).
            int readabilityCount = EnsureCardTextReadability(templateTransform);

            var scene = canvas.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"[SetupTemplates] '{PlayerCardTemplateName}'에 PortraitBG/FrameOverlay 4단 레이어 " +
                "추가 및 바인딩 성공, Text {readabilityCount}개에 Outline/Shadow 가독성 보강 완료 - " +
                "이 템플릿을 복제하는 모든 화면에 즉시 반영됩니다. " +
                "Resources/Portraits/{TemplateId}_BG, Resources/CardDesigns/BG_{등급코드}·Frame_{등급코드} " +
                "리소스가 아직 없으면 해당 레이어는 자동으로 비활성 상태로 숨어 있습니다(정상 동작).");
        }

        /// <summary>
        /// [TASK-KBO-170, 텍스트 가독성 강화] `cardTransform` 하위 모든 `Text`(이름/구단/포지션/OVR)에
        /// 흰색 `Outline` + 검은색 `Shadow`를 추가/보정한다. `FixPlayerCardTemplateColor()`가 텍스트
        /// 색상을 검정으로 고정해 두었는데(TASK-119), TASK-168 이후 카드 배경이 단색 틴트가 아니라 실제
        /// 선수 사진/디자인 프레임(`CardDesigns/BG_*`, `Portrait`)으로 바뀌면서 어두운 사진/프레임
        /// 위에서는 검은 글씨가 그대로 묻혀 버릴 수 있다 - 흰색 외곽선을 둘러 밝은 배경/어두운 배경
        /// 양쪽 모두에서 대비가 확보되도록 한다(그림자는 반대로 밝은 배경 위에서의 입체감/가독성을 보강).
        /// 여러 번 실행해도 안전(idempotent) - 이미 있는 컴포넌트는 값만 재보정한다.
        /// </summary>
        private static int EnsureCardTextReadability(Transform cardTransform)
        {
            var texts = cardTransform.GetComponentsInChildren<Text>(true);
            foreach (var text in texts)
            {
                if (!text.TryGetComponent<Outline>(out var outline))
                {
                    outline = text.gameObject.AddComponent<Outline>();
                }
                outline.effectColor = new Color(1f, 1f, 1f, 0.85f);
                outline.effectDistance = new Vector2(1.2f, -1.2f);
                outline.useGraphicAlpha = true;

                // [TASK-KBO-176, 버그 수정] Outline은 Shadow를 상속하므로 TryGetComponent<Shadow>는 방금 만든 Outline을
                // 돌려준다 - 그래서 지금까지는 Shadow가 따로 생기지 않고 Outline 값(흰색)이 그림자 값(검정 0.6)으로 덮였다
                // (미커밋 SampleScene.unity의 Outline 5개가 실제로 검정 0.6/(1,-1) 상태). 정확히 Shadow 타입만 찾는다.
                Shadow shadow = null;
                foreach (var candidate in text.GetComponents<Shadow>())
                {
                    if (candidate.GetType() == typeof(Shadow)) { shadow = candidate; break; }
                }
                if (shadow == null)
                {
                    shadow = text.gameObject.AddComponent<Shadow>();
                }
                shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
                shadow.effectDistance = new Vector2(1f, -1f);
                shadow.useGraphicAlpha = true;

                EditorUtility.SetDirty(text);
            }

            return texts.Length;
        }

        /// <summary>[TASK-KBO-176] OVR 텍스트(상단 기준 -90) 바로 아래 "SetDeckScoreText"(예: "SD 8"). 여러 번 실행해도 안전 -
        /// 있으면 위치/스타일만 다시 맞춘다. 레이어 순서상 FrameOverlay보다 뒤 sibling이라 프레임 위에 그려진다.</summary>
        private static Text FindOrCreateSetDeckScoreText(Transform cardTransform)
        {
            const string name = "SetDeckScoreText";
            var existing = cardTransform.Find(name);
            GameObject textObject;
            if (existing != null)
            {
                textObject = existing.gameObject;
            }
            else
            {
                textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
                Undo.RegisterCreatedObjectUndo(textObject, $"Create {name}");
                textObject.transform.SetParent(cardTransform, false);
            }

            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(132f, 18f);
            rect.anchoredPosition = new Vector2(0f, -110f);
            textObject.transform.SetAsLastSibling();

            if (!textObject.TryGetComponent<Text>(out var text)) text = textObject.AddComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.fontSize = 13;
            text.fontStyle = FontStyle.Bold;
            text.font = KBOFonts.Default;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>`cardTransform`(PlayerCardTemplate) 바로 아래에 `PortraitBG`(배경 인물, 듀얼 샷)
        /// `Image`를 찾거나 만든다. `Portrait`와 동일하게 카드 전체를 꽉 채운다 - 실제 표시 여부/앞뒤
        /// 순서는 `SetupDualPortraitAndGradeFrameLayers()`가 sibling index로 강제한다.</summary>
        private static Image FindOrCreatePortraitBGImage(Transform cardTransform)
        {
            var existing = cardTransform.Find(PortraitBGName);
            GameObject portraitBGObject;
            if (existing != null)
            {
                portraitBGObject = existing.gameObject;
            }
            else
            {
                portraitBGObject = new GameObject(PortraitBGName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(portraitBGObject, $"Create {PortraitBGName}");
                portraitBGObject.transform.SetParent(cardTransform, false);
            }

            var rect = (RectTransform)portraitBGObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            if (!portraitBGObject.TryGetComponent<Image>(out var image))
            {
                image = portraitBGObject.AddComponent<Image>();
            }
            image.color = Color.white;
            image.raycastTarget = false;
            image.preserveAspect = false;

            return image;
        }

        /// <summary>`cardTransform`(PlayerCardTemplate) 바로 아래에 `FrameOverlay`(등급별 테두리)
        /// `Image`를 찾거나 만든다. 카드 전체를 꽉 채우는 오버레이이며, `PlayerCardUI.SetupGradeDesign()`이
        /// 매칭되는 `CardDesigns/Frame_{등급코드}` 리소스가 있을 때만 활성화한다.</summary>
        private static Image FindOrCreateFrameOverlayImage(Transform cardTransform)
        {
            var existing = cardTransform.Find(FrameOverlayName);
            GameObject frameOverlayObject;
            if (existing != null)
            {
                frameOverlayObject = existing.gameObject;
            }
            else
            {
                frameOverlayObject = new GameObject(FrameOverlayName, typeof(RectTransform), typeof(Image));
                Undo.RegisterCreatedObjectUndo(frameOverlayObject, $"Create {FrameOverlayName}");
                frameOverlayObject.transform.SetParent(cardTransform, false);
            }

            var rect = (RectTransform)frameOverlayObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            if (!frameOverlayObject.TryGetComponent<Image>(out var image))
            {
                image = frameOverlayObject.AddComponent<Image>();
            }
            image.color = Color.white;
            image.raycastTarget = false;
            image.preserveAspect = false;

            return image;
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
