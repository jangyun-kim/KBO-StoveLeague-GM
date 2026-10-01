using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-177] 9:16 세로(CanvasScaler 1080x1920, 가로 기준 매칭) 실사 검수에서 깨져 보인 화면 3종을 다시 조립한다.
    ///   1) CheerleaderInventoryPanel(치어리더 관리) - 헤더(타이틀·보유 수·닫기) / 장착 슬롯 + 세트덱 시너지 요약 / 필터 바
    ///      (전체·구단·LIVE·ICON·LEGEND) / 고정 높이 카드 목록(티어 뱃지·이름·구단·활동기간·버프·장착/해제).
    ///   2) CheerleaderShopPanel(치어리더 영입) - 탭 바 아래 전체 영역, 응원봉 4종 2x2 배지 / 상품 배너 4개(상품명·필요 재화·
    ///      확률 요약·1회/10회) / 최근 영입 결과 스크롤 박스.
    ///   3) ScoutPanel(선수 영입) - 영입 재화 5종 배지 바 / 상품 배너 6개(상품명·1회/10회 소모 재화·보유·1회/10회 버튼).
    /// 방식: 각 패널 아래에 새 루트 "Layout177"을 매번 새로 만들고(이미 있으면 지우고 재생성 - 모든 참조를 다시 바인딩하므로
    /// 안전), 기존 자식 중 새 레이아웃이 대체하는 것은 비활성 "_Legacy177" 보관함으로 옮긴다(파괴하지 않아 혹시 남은 참조가
    /// 있어도 깨지지 않음). 선수 영입 결과 팝업처럼 계속 쓰는 기존 오브젝트는 그대로 두고 맨 앞 레이어로 올린다.
    /// 통합 메뉴 SetupMasterBinding.ApplyLatestUI()가 다른 Setup들 뒤에 호출한다(다른 Setup이 구 레이아웃을 다시 만들어도
    /// 이 단계가 마지막에 덮어쓴다).
    /// </summary>
    public static class SetupMobileUI177
    {
        private const string LayoutRootName = "Layout177";
        private const string LegacyHolderName = "_Legacy177";
        private static readonly Color PanelBackground = new Color(0.95f, 0.96f, 0.98f, 1f);
        private static readonly Color CardColor = Color.white;
        private static readonly Color ButtonColor = new Color(0.88f, 0.9f, 0.94f, 1f);
        private static readonly Color AccentButtonColor = new Color(0.25f, 0.45f, 0.8f, 1f);
        private static readonly Color TextDark = new Color(0.1f, 0.12f, 0.16f, 1f);
        private static readonly Color TextMuted = new Color(0.35f, 0.38f, 0.45f, 1f);

        public static void ApplyAll()
        {
            BuildCheerleaderInventory();
            BuildCheerleaderShop();
            BuildScoutPanel();
            Debug.Log("[SetupMobileUI177] 치어리더 관리 / 치어리더 영입 / 선수 영입 9:16 레이아웃 적용 완료.");
        }

        // ------------------------------------------------------------------ 1) 치어리더 관리

        public static void BuildCheerleaderInventory()
        {
            var controller = Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupMobileUI177] CheerleaderInventoryUIController가 없어 치어리더 관리 화면을 건너뜁니다 - " +
                    "'Auto-Create Cheerleader Inventory UI'가 먼저 실행돼야 합니다.");
                return;
            }

            var panel = (RectTransform)controller.transform;
            Stretch(panel, Vector2.zero, Vector2.one);
            Paint(panel.gameObject, PanelBackground);
            var root = RecreateLayoutRoot(panel);
            MoveOthersToLegacy(panel, root, new HashSet<Transform>());

            // 헤더
            var header = Box(root, "Header", new Vector2(0f, 0.93f), new Vector2(1f, 1f), new Color(0.2f, 0.27f, 0.4f, 1f));
            Label(header, "TitleText", "치어리더 관리", new Vector2(0.04f, 0.42f), new Vector2(0.72f, 1f), 46, TextAnchor.MiddleLeft, Color.white, true);
            var ownedCount = Label(header, "OwnedCountText", "보유 0명", new Vector2(0.04f, 0f), new Vector2(0.72f, 0.45f), 26, TextAnchor.MiddleLeft, new Color(0.85f, 0.9f, 1f));
            var close = Btn(header, "CloseButton", "X 닫기", new Vector2(0.76f, 0.18f), new Vector2(0.97f, 0.82f), 30, new Color(0.95f, 0.95f, 0.97f));

            // 장착 슬롯 + 시너지 요약
            var equipBox = Box(root, "EquippedBox", new Vector2(0.03f, 0.795f), new Vector2(0.97f, 0.92f), new Color(1f, 0.97f, 0.86f, 1f));
            Label(equipBox, "EquippedTitle", "장착 슬롯 (1)", new Vector2(0.03f, 0.74f), new Vector2(0.97f, 0.98f), 26, TextAnchor.MiddleLeft, TextMuted, true);
            var summary = Label(equipBox, "EquippedSummaryText", "장착 슬롯: 비어 있음", new Vector2(0.03f, 0.03f), new Vector2(0.97f, 0.76f), 24, TextAnchor.UpperLeft, TextDark);

            // 필터 바
            var filterBar = Rect(root, "FilterBar", new Vector2(0.03f, 0.735f), new Vector2(0.97f, 0.785f));
            var filters = new[] { ("FilterAll", "전체"), ("FilterTeam", "구단: 전체"), ("FilterLive", "LIVE"), ("FilterIcon", "ICON"), ("FilterLegend", "LEGEND") };
            var filterButtons = new List<Button>();
            for (int i = 0; i < filters.Length; i++)
            {
                float x0 = i / (float)filters.Length, x1 = (i + 1) / (float)filters.Length;
                filterButtons.Add(Btn(filterBar, filters[i].Item1, filters[i].Item2, new Vector2(x0 + 0.005f, 0f), new Vector2(x1 - 0.005f, 1f), 24, ButtonColor));
            }

            // 목록
            var content = VerticalScroll(root, "CheerleaderList", new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.725f), 12f);
            var empty = Label(root, "EmptyText", "보유한 치어리더가 없습니다.", new Vector2(0.05f, 0.35f), new Vector2(0.95f, 0.45f), 28, TextAnchor.MiddleCenter, TextMuted);
            empty.gameObject.SetActive(false);

            var template = BuildCheerleaderCardTemplate(root);

            var so = new SerializedObject(controller);
            so.FindProperty("contentContainer").objectReferenceValue = content;
            so.FindProperty("slotPrefab").objectReferenceValue = template;
            so.FindProperty("closeButton").objectReferenceValue = close;
            so.FindProperty("ownedCountText").objectReferenceValue = ownedCount;
            so.FindProperty("equippedSummaryText").objectReferenceValue = summary;
            so.FindProperty("emptyText").objectReferenceValue = empty;
            so.FindProperty("filterAllButton").objectReferenceValue = filterButtons[0];
            so.FindProperty("filterTeamButton").objectReferenceValue = filterButtons[1];
            so.FindProperty("filterLiveButton").objectReferenceValue = filterButtons[2];
            so.FindProperty("filterIconButton").objectReferenceValue = filterButtons[3];
            so.FindProperty("filterLegendButton").objectReferenceValue = filterButtons[4];
            so.ApplyModifiedProperties();
            MarkDirty(controller);
        }

        /// <summary>치어리더 카드(높이 230): 티어 뱃지 · 이름 · 구단/활동기간 · 버프 4종 · 장착/해제 버튼. 비활성 보관.</summary>
        private static GameObject BuildCheerleaderCardTemplate(Transform root)
        {
            var card = Rect(root, "CheerleaderCardTemplate", new Vector2(0f, 1f), new Vector2(1f, 1f));
            card.sizeDelta = new Vector2(0f, 230f);
            var background = Paint(card.gameObject, CardColor);
            var element = GetOrAdd<LayoutElement>(card.gameObject);
            element.preferredHeight = 230f;
            element.minHeight = 230f;
            var outline = GetOrAdd<Outline>(card.gameObject);
            outline.effectColor = new Color(0.75f, 0.78f, 0.85f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);

            var badge = Box(card, "TierBadge", new Vector2(0.03f, 0.6f), new Vector2(0.2f, 0.92f), new Color(0.45f, 0.55f, 0.65f));
            var badgeText = Label(badge, "Text", "LIVE", Vector2.zero, Vector2.one, 26, TextAnchor.MiddleCenter, Color.white, true);
            var nameText = Label(card, "NameText", "이름", new Vector2(0.22f, 0.66f), new Vector2(0.76f, 0.96f), 36, TextAnchor.MiddleLeft, TextDark, true);
            var teamText = Label(card, "TeamPeriodText", "KIA · 2020~2021", new Vector2(0.22f, 0.46f), new Vector2(0.76f, 0.66f), 24, TextAnchor.MiddleLeft, TextMuted);
            var buff = Label(card, "BuffText", "전력 보정: +3", new Vector2(0.03f, 0.24f), new Vector2(0.38f, 0.44f), 22, TextAnchor.MiddleLeft, TextDark);
            var clutch = Label(card, "ClutchText", "클러치: 1.1x", new Vector2(0.39f, 0.24f), new Vector2(0.76f, 0.44f), 22, TextAnchor.MiddleLeft, TextDark);
            var economic = Label(card, "EconomicText", "관중 수익 x1.15", new Vector2(0.03f, 0.04f), new Vector2(0.38f, 0.24f), 22, TextAnchor.MiddleLeft, TextDark);
            var sentiment = Label(card, "SentimentText", "팬심 방어: +2", new Vector2(0.39f, 0.04f), new Vector2(0.76f, 0.24f), 22, TextAnchor.MiddleLeft, TextDark);
            var equip = Btn(card, "EquipButton", "장착", new Vector2(0.78f, 0.22f), new Vector2(0.97f, 0.78f), 32, AccentButtonColor);
            var equipLabel = equip.GetComponentInChildren<Text>(true);
            equipLabel.color = Color.white;

            var slot = GetOrAdd<CheerleaderSlotUI>(card.gameObject);
            var so = new SerializedObject(slot);
            so.FindProperty("nameText").objectReferenceValue = nameText;
            so.FindProperty("gradeText").objectReferenceValue = null; // 티어/구단은 뱃지·TeamPeriodText가 대신 표시
            so.FindProperty("buffText").objectReferenceValue = buff;
            so.FindProperty("economicRateText").objectReferenceValue = economic;
            so.FindProperty("clutchText").objectReferenceValue = clutch;
            so.FindProperty("sentimentText").objectReferenceValue = sentiment;
            so.FindProperty("equipButton").objectReferenceValue = equip;
            so.FindProperty("equipButtonLabel").objectReferenceValue = equipLabel;
            so.FindProperty("tierBadgeText").objectReferenceValue = badgeText;
            so.FindProperty("tierBadgeImage").objectReferenceValue = badge.GetComponent<Image>();
            so.FindProperty("teamPeriodText").objectReferenceValue = teamText;
            so.FindProperty("cardBackground").objectReferenceValue = background;
            so.ApplyModifiedProperties();

            card.gameObject.SetActive(false);
            return card.gameObject;
        }

        // ------------------------------------------------------------------ 2) 치어리더 영입

        public static void BuildCheerleaderShop()
        {
            var controller = Object.FindAnyObjectByType<CheerleaderShopUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupMobileUI177] CheerleaderShopUIController가 없어 치어리더 영입 화면을 건너뜁니다.");
                return;
            }

            var panel = (RectTransform)controller.transform;
            Stretch(panel, Vector2.zero, new Vector2(1f, 0.92f)); // 허브 탭 바(0.92~1) 아래 전체
            Paint(panel.gameObject, PanelBackground);
            var root = RecreateLayoutRoot(panel);
            MoveOthersToLegacy(panel, root, new HashSet<Transform>());

            // 응원봉 4종 2x2 배지
            var bar = Rect(root, "CurrencyBar", new Vector2(0.03f, 0.835f), new Vector2(0.97f, 0.99f));
            var live = Badge(bar, "LiveStick", new Vector2(0f, 0.52f), new Vector2(0.495f, 1f), new Color(0.82f, 0.9f, 1f));
            var limited = Badge(bar, "LimitedStick", new Vector2(0.505f, 0.52f), new Vector2(1f, 1f), new Color(1f, 0.85f, 0.85f));
            var star = Badge(bar, "StarStick", new Vector2(0f, 0f), new Vector2(0.495f, 0.48f), new Color(0.9f, 0.85f, 1f));
            var legend = Badge(bar, "LegendStick", new Vector2(0.505f, 0f), new Vector2(1f, 0.48f), new Color(1f, 0.93f, 0.75f));

            // 상품 배너 4개
            var banners = new[]
            {
                ("LiveBanner", "일반 영입 · 라이브", "liveButton", "liveButton10", "liveInfoText"),
                ("LimitedBanner", "일반 영입 · 한정", "limitedButton", "limitedButton10", "limitedInfoText"),
                ("IconBanner", "픽업/프리미엄 · 아이콘", "iconButton", "iconButton10", "iconInfoText"),
                ("LegendBanner", "픽업/프리미엄 · 레전드", "legendButton", "legendButton10", "legendInfoText"),
            };
            var so = new SerializedObject(controller);
            const float top = 0.82f, height = 0.12f, gap = 0.008f;
            for (int i = 0; i < banners.Length; i++)
            {
                float y1 = top - i * (height + gap), y0 = y1 - height;
                var (name, title, b1, b10, info) = banners[i];
                var (one, ten, infoText) = Banner(root, name, title, new Vector2(0.03f, y0), new Vector2(0.97f, y1));
                so.FindProperty(b1).objectReferenceValue = one;
                so.FindProperty(b10).objectReferenceValue = ten;
                so.FindProperty(info).objectReferenceValue = infoText;
            }

            // 최근 영입 결과
            Label(root, "ResultHeader", "최근 영입 결과", new Vector2(0.03f, 0.29f), new Vector2(0.97f, 0.33f), 28, TextAnchor.MiddleLeft, TextDark, true);
            var resultContent = VerticalScroll(root, "ResultScroll", new Vector2(0.03f, 0.015f), new Vector2(0.97f, 0.29f), 0f);
            var resultText = Label(resultContent, "ResultLogText", "영입 결과가 여기에 표시됩니다.", Vector2.zero, Vector2.one, 24, TextAnchor.UpperLeft, TextDark);
            resultText.verticalOverflow = VerticalWrapMode.Overflow;
            resultText.resizeTextForBestFit = false; // ContentSizeFitter로 늘어나는 텍스트라 고정 크기 유지
            GetOrAdd<ContentSizeFitter>(resultText.gameObject).verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            so.FindProperty("liveCheerStickText").objectReferenceValue = live;
            so.FindProperty("limitedCheerStickText").objectReferenceValue = limited;
            so.FindProperty("starCheerStickText").objectReferenceValue = star;
            so.FindProperty("legendCheerStickText").objectReferenceValue = legend;
            so.FindProperty("resultLogText").objectReferenceValue = resultText;
            so.ApplyModifiedProperties(); // closeButton은 허브 X 버튼(SetupScoutHubUI)이 그대로 담당
            MarkDirty(controller);
        }

        // ------------------------------------------------------------------ 3) 선수 영입

        public static void BuildScoutPanel()
        {
            var controller = Object.FindAnyObjectByType<ScoutUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupMobileUI177] ScoutUIController가 없어 선수 영입 화면을 건너뜁니다.");
                return;
            }

            var panel = (RectTransform)controller.transform;
            Stretch(panel, Vector2.zero, new Vector2(1f, 0.92f));
            Paint(panel.gameObject, PanelBackground);

            // 결과 팝업/공지 등 계속 쓰는 기존 오브젝트는 legacy로 옮기지 않는다.
            var so = new SerializedObject(controller);
            var keep = new HashSet<Transform>();
            foreach (var field in new[] { "resultPopupRoot", "cardContainer", "closeResultPopupButton", "retryButton", "topPullAnnouncementText" })
            {
                var value = so.FindProperty(field).objectReferenceValue;
                var t = value is GameObject go ? go.transform : value is Component c ? c.transform : null;
                var topChild = TopLevelChild(panel, t);
                if (topChild != null) keep.Add(topChild);
            }

            var root = RecreateLayoutRoot(panel);
            MoveOthersToLegacy(panel, root, keep);

            // 영입 재화 5종 (3 + 2)
            var bar = Rect(root, "CurrencyBar", new Vector2(0.03f, 0.855f), new Vector2(0.97f, 0.99f));
            var liveNormal = Badge(bar, "LiveNormalTicket", new Vector2(0f, 0.52f), new Vector2(0.326f, 1f), new Color(0.85f, 0.92f, 1f));
            var liveEpic = Badge(bar, "LiveEpicTicket", new Vector2(0.337f, 0.52f), new Vector2(0.663f, 1f), new Color(0.85f, 0.95f, 0.9f));
            var ball = Badge(bar, "SignatureBall", new Vector2(0.674f, 0.52f), new Vector2(1f, 1f), new Color(0.93f, 0.88f, 1f));
            var trophy = Badge(bar, "Trophy", new Vector2(0f, 0f), new Vector2(0.495f, 0.48f), new Color(1f, 0.93f, 0.78f));
            var pickup = Badge(bar, "PickupTicket", new Vector2(0.505f, 0f), new Vector2(1f, 0.48f), new Color(1f, 0.86f, 0.86f));

            var products = new[]
            {
                ("LiveNormalBanner", "일반 영입 · 라이브 일반", "liveNormalButton", "liveNormalButton10", "liveNormalCostText"),
                ("LiveEpicBanner", "일반 영입 · 라이브 에픽", "liveEpicButton", "liveEpicButton10", "liveEpicCostText"),
                ("PremiumSignatureBanner", "프리미엄 · 시그니처 (싸인볼)", "premiumSignatureButton", "premiumSignatureButton10", "premiumSignatureCostText"),
                ("PremiumTitleHolderBanner", "프리미엄 · 타이틀 홀더 (트로피)", "premiumTitleHolderButton", "premiumTitleHolderButton10", "premiumTitleHolderCostText"),
                ("PickupSignatureBanner", "픽업 · 시그니처 (픽업권)", "pickupSignatureButton", "pickupSignatureButton10", "pickupSignatureCostText"),
                ("PickupTitleHolderBanner", "픽업 · 타이틀 홀더 (픽업권)", "pickupTitleHolderButton", "pickupTitleHolderButton10", "pickupTitleHolderCostText"),
            };
            const float top = 0.84f, height = 0.13f, gap = 0.008f;
            for (int i = 0; i < products.Length; i++)
            {
                float y1 = top - i * (height + gap), y0 = y1 - height;
                var (name, title, b1, b10, cost) = products[i];
                var (one, ten, costText) = Banner(root, name, title, new Vector2(0.03f, y0), new Vector2(0.97f, y1));
                so.FindProperty(b1).objectReferenceValue = one;
                so.FindProperty(b10).objectReferenceValue = ten;
                so.FindProperty(cost).objectReferenceValue = costText;
            }

            so.FindProperty("liveNormalTicketText").objectReferenceValue = liveNormal;
            so.FindProperty("liveEpicTicketText").objectReferenceValue = liveEpic;
            so.FindProperty("signatureBallText").objectReferenceValue = ball;
            so.FindProperty("trophyText").objectReferenceValue = trophy;
            so.FindProperty("pickupTicketText").objectReferenceValue = pickup;
            so.ApplyModifiedProperties();

            foreach (var t in keep) t.SetAsLastSibling(); // 결과 팝업이 새 레이아웃 위에 그려지도록
            MarkDirty(controller);
        }

        // ------------------------------------------------------------------ 공통 조립 헬퍼

        /// <summary>상품 배너: 제목(굵게) + 정보 2줄 + [1회 영입] [10회 영입]. (1회, 10회, 정보 Text) 반환.</summary>
        private static (Button one, Button ten, Text info) Banner(Transform parent, string name, string title, Vector2 aMin, Vector2 aMax)
        {
            var box = Box(parent, name, aMin, aMax, CardColor);
            var outline = GetOrAdd<Outline>(box.gameObject);
            outline.effectColor = new Color(0.75f, 0.78f, 0.85f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);
            Label(box, "Title", title, new Vector2(0.03f, 0.55f), new Vector2(0.6f, 0.95f), 32, TextAnchor.MiddleLeft, TextDark, true);
            var info = Label(box, "Info", "", new Vector2(0.03f, 0.04f), new Vector2(0.6f, 0.56f), 19, TextAnchor.UpperLeft, TextMuted);
            var one = Btn(box, "Roll1Button", "1회 영입", new Vector2(0.62f, 0.15f), new Vector2(0.795f, 0.85f), 28, ButtonColor);
            var ten = Btn(box, "Roll10Button", "10회 영입", new Vector2(0.805f, 0.15f), new Vector2(0.98f, 0.85f), 28, AccentButtonColor);
            ten.GetComponentInChildren<Text>(true).color = Color.white;
            return (one, ten, info);
        }

        /// <summary>재화 배지(색 배경 + 2줄 텍스트 "이름\n수량"). 텍스트 반환.</summary>
        private static Text Badge(Transform parent, string name, Vector2 aMin, Vector2 aMax, Color color)
        {
            var box = Box(parent, name, aMin, aMax, color);
            return Label(box, "Value", "", new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), 26, TextAnchor.MiddleCenter, TextDark, true);
        }

        private static RectTransform RecreateLayoutRoot(RectTransform panel)
        {
            // 구 조립(SetupShopUI)은 패널 자체에 VerticalLayoutGroup을 달아 자식 위치를 강제했다 - 새 루트가 그 배치에 끌려가지
            // 않도록 패널의 레이아웃 그룹/사이즈 피터를 끈다(컴포넌트는 남겨 둬 구 Setup과 충돌하지 않게).
            foreach (var group in panel.GetComponents<LayoutGroup>())
            {
                Undo.RecordObject(group, "Disable Panel LayoutGroup");
                group.enabled = false;
            }
            foreach (var fitter in panel.GetComponents<ContentSizeFitter>())
            {
                Undo.RecordObject(fitter, "Disable Panel Fitter");
                fitter.enabled = false;
            }

            var existing = panel.Find(LayoutRootName);
            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            var root = Rect(panel, LayoutRootName, Vector2.zero, Vector2.one);
            return root;
        }

        /// <summary>panel 직속 자식 중 root/keep/보관함 외 전부를 비활성 보관함으로 옮긴다(파괴하지 않음).</summary>
        private static void MoveOthersToLegacy(Transform panel, Transform root, HashSet<Transform> keep)
        {
            var holder = panel.Find(LegacyHolderName);
            if (holder == null)
            {
                var go = new GameObject(LegacyHolderName, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, $"Create {LegacyHolderName}");
                go.transform.SetParent(panel, false);
                holder = go.transform;
            }
            holder.gameObject.SetActive(false);

            var children = Enumerable.Range(0, panel.childCount).Select(panel.GetChild).ToList();
            foreach (var child in children)
            {
                if (child == root || child == holder || keep.Contains(child)) continue;
                // 구 Setup이 같은 이름을 다시 만들어 재실행할 때마다 보관함에 사본이 쌓이지 않도록 이전 사본은 지운다
                // (이전 사본은 이미 어떤 필드도 참조하지 않는다 - 이 스크립트가 매번 새 레이아웃으로 재바인딩함).
                var stale = holder.Find(child.name);
                if (stale != null) Undo.DestroyObjectImmediate(stale.gameObject);
                Undo.SetTransformParent(child, holder, "Move To Legacy177");
            }
            holder.SetAsFirstSibling();
        }

        private static Transform TopLevelChild(Transform panel, Transform node)
        {
            while (node != null && node.parent != panel) node = node.parent;
            return node;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            if (!go.TryGetComponent<T>(out var component)) component = Undo.AddComponent<T>(go);
            return component;
        }

        private static void Stretch(RectTransform rect, Vector2 aMin, Vector2 aMax)
        {
            Undo.RecordObject(rect, "Stretch");
            rect.anchorMin = aMin;
            rect.anchorMax = aMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 aMin, Vector2 aMax)
        {
            var existing = parent.Find(name);
            GameObject go;
            if (existing != null) go = existing.gameObject;
            else
            {
                go = new GameObject(name, typeof(RectTransform));
                Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
                go.transform.SetParent(parent, false);
            }
            var rect = (RectTransform)go.transform;
            Stretch(rect, aMin, aMax);
            return rect;
        }

        private static Image Paint(GameObject go, Color color)
        {
            var image = GetOrAdd<Image>(go);
            image.color = color;
            return image;
        }

        private static RectTransform Box(Transform parent, string name, Vector2 aMin, Vector2 aMax, Color color)
        {
            var rect = Rect(parent, name, aMin, aMax);
            Paint(rect.gameObject, color).raycastTarget = false;
            return rect;
        }

        private static Text Label(Transform parent, string name, string text, Vector2 aMin, Vector2 aMax, int fontSize,
            TextAnchor alignment, Color color, bool bold = false)
        {
            var rect = Rect(parent, name, aMin, aMax);
            var label = GetOrAdd<Text>(rect.gameObject);
            label.text = text;
            label.font = KBOFonts.Default;
            label.fontSize = fontSize;
            label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            label.alignment = alignment;
            label.color = color;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true; // 긴 문구는 줄이되 지정 크기 이상으로 키우지 않는다
            label.resizeTextMinSize = Mathf.Max(14, fontSize - 10);
            label.resizeTextMaxSize = fontSize;
            label.raycastTarget = false;
            return label;
        }

        private static Button Btn(Transform parent, string name, string text, Vector2 aMin, Vector2 aMax, int fontSize, Color color)
        {
            var rect = Rect(parent, name, aMin, aMax);
            var image = Paint(rect.gameObject, color);
            image.raycastTarget = true;
            var button = GetOrAdd<Button>(rect.gameObject);
            button.targetGraphic = image;
            Label(rect, "Label", text, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), fontSize, TextAnchor.MiddleCenter, TextDark, true);
            return button;
        }

        /// <summary>세로 스크롤 목록 - 반환값은 항목이 쌓이는 Content(VerticalLayoutGroup + ContentSizeFitter).</summary>
        private static RectTransform VerticalScroll(Transform parent, string name, Vector2 aMin, Vector2 aMax, float spacing)
        {
            var viewport = Rect(parent, name, aMin, aMax);
            Paint(viewport.gameObject, new Color(1f, 1f, 1f, 0.6f));
            GetOrAdd<RectMask2D>(viewport.gameObject);

            var content = Rect(viewport, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f));
            content.pivot = new Vector2(0.5f, 1f);
            var layout = GetOrAdd<VerticalLayoutGroup>(content.gameObject);
            layout.spacing = spacing;
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            GetOrAdd<ContentSizeFitter>(content.gameObject).verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = GetOrAdd<ScrollRect>(viewport.gameObject);
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            return content;
        }

        private static void MarkDirty(Component component)
        {
            EditorUtility.SetDirty(component);
            if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
