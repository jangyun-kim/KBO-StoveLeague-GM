using System.Collections.Generic;
using System.Linq;
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
    /// [TASK-KBO-178] Assets/Resources/SetImage 레퍼런스 34장(메인 홈·구단 관리·리그 홈·라인업 4종·선수 스카우트 3종·영입 결과 3종·
    /// 선수 관리 5종·선수 상세 6종·경기 흐름 9종)의 디자인 언어를 9:16(1080x1920) 화면에 적용한다(TASK-177 SetupMobileUI177 대체).
    ///
    /// 레퍼런스에서 추출해 적용한 규칙:
    ///   - 상단: 파란 헤더 바 + 가운데 네이비 필(pill) 타이틀(라인업/치어리더 관리), 그 아래 검정 재화 스트립(이름·수량·회색 + 칸).
    ///   - 하단: 파란 5탭 내비게이션(선수 관리 / 라인업 / 홈 / 상점 / 치어리더) - 활성 탭만 진한 네이비 + 흰 굵은 글씨.
    ///   - 콘텐츠: "어두운 헤더 띠 + 밝은(또는 네이비) 본문" 카드. 구매/영입 버튼은 보라색 + 아래쪽 진보라 비용 띠, 확인은 파란색,
    ///     보조 동작은 회색. 탭 바는 흰 바탕 + 활성 탭 네이비.
    ///   - 배경: 로비/스카우트/치어리더 = 짙은 네이비, 라인업 = 밝은 회색(구장 톤) + 하단 검정 세트덱 스코어 바(시안 게이지),
    ///     영입 결과 = 검정 + "SCOUT RESULT" 타이틀 + 5열 카드 그리드 + 보라(다시 영입)/파랑(확인) 버튼.
    ///   - 카드: OVR 큰 숫자 좌상단 · 포지션 · 별 상단 중앙 · 하단 검정 이름 띠(흰 굵은 글씨) · SD 금색 배지.
    ///   - 타이포: 본문 22~26, 버튼 26~32, 타이틀 34~46(모바일 가독성).
    /// 레퍼런스 이미지는 디자인 참고용일 뿐 어떤 코드도 로드하지 않는다(Resources.Load 호출 없음).
    ///
    /// 조립 방식(TASK-177과 동일): 패널마다 "Layout178"을 새로 만들고 대체된 구 자식은 비활성 "_Legacy178"로 옮긴다(파괴하지 않음).
    /// 다른 Setup이 만든 오브젝트(로스터·스카우트 결과 팝업·카드 템플릿)는 이름으로 찾아 재배치/재색칠만 한다.
    /// 인벤토리/강화/선수 관리/선수 상세/재료 선택 화면은 구조를 유지한 채 "다크 테마 패스"(배경·버튼 색 + 배경 밝기에 따른 글자색 +
    /// 최소 글꼴 크기)를 적용한다.
    /// </summary>
    public static class SetupThemeUI178
    {
        private const string LayoutRootName = "Layout178";
        private const string LegacyHolderName = "_Legacy178";

        // ---- 팔레트(레퍼런스 스크린샷에서 추출) ----
        private static readonly Color HeaderBlue = new Color(0.17f, 0.36f, 0.66f);
        private static readonly Color PillNavy = new Color(0.1f, 0.2f, 0.45f);
        private static readonly Color StripBlack = new Color(0.08f, 0.09f, 0.11f);
        private static readonly Color NavBlue = new Color(0.2f, 0.42f, 0.78f);
        private static readonly Color NavActive = new Color(0.09f, 0.19f, 0.43f);
        private static readonly Color NavText = new Color(0.66f, 0.78f, 0.96f);
        private static readonly Color BgNavy = new Color(0.07f, 0.09f, 0.16f);
        private static readonly Color BgDark = new Color(0.13f, 0.14f, 0.18f);
        private static readonly Color BgStadium = new Color(0.84f, 0.86f, 0.9f);
        private static readonly Color CardDark = new Color(0.12f, 0.15f, 0.26f);
        private static readonly Color CardDarkAlt = new Color(0.15f, 0.19f, 0.31f);
        private static readonly Color CardHeader = new Color(0.13f, 0.15f, 0.21f);
        private static readonly Color CardLight = new Color(0.92f, 0.93f, 0.96f);
        private static readonly Color SectionBar = new Color(0.21f, 0.23f, 0.28f);
        private static readonly Color Purple = new Color(0.46f, 0.18f, 0.9f);
        private static readonly Color PurpleDeep = new Color(0.29f, 0.1f, 0.6f);
        private static readonly Color ConfirmBlue = new Color(0.15f, 0.33f, 0.86f);
        private static readonly Color GrayButton = new Color(0.4f, 0.43f, 0.48f);
        private static readonly Color LightButton = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color TextWhite = new Color(0.97f, 0.98f, 1f);
        private static readonly Color TextDark = new Color(0.07f, 0.09f, 0.15f);
        private static readonly Color TextMutedOnDark = new Color(0.68f, 0.73f, 0.82f);
        private static readonly Color TextMutedOnLight = new Color(0.36f, 0.39f, 0.46f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);
        private static readonly Color AccentRed = new Color(0.86f, 0.16f, 0.24f);
        private static readonly Color OvrPink = new Color(0.9f, 0.16f, 0.4f);

        public static void ApplyAll()
        {
            ThemeCardTemplate();
            BuildLobby();
            ThemeRoster();
            ThemeScoutHub();
            BuildScoutPanel();
            ThemeScoutResultPopup();
            BuildCheerleaderShop();
            BuildCheerleaderInventory();
            ApplyDarkThemePass<InventoryUIController>();
            ApplyDarkThemePass<EnhanceUIController>();
            ApplyDarkThemePass<PlayerManagementUIController>();
            ApplyDarkThemePass<PlayerDetailUIController>();
            ApplyDarkThemePass<MaterialSelectUIController>();
            Debug.Log("[SetupThemeUI178] SetImage 레퍼런스 기반 테마 적용 완료(로비/로스터/스카우트 허브/영입 결과/치어리더 관리·영입/카드/인벤토리·강화).");
        }

        // ================================================================== 공용 카드(PlayerCardTemplate)

        /// <summary>레퍼런스 카드 배치: 별(상단 중앙) · OVR 큰 숫자 + 포지션(좌상단) · 구단 + SD(우상단) · 하단 검정 이름 띠.</summary>
        public static void ThemeCardTemplate()
        {
            var canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            var template = canvas != null ? canvas.transform.Find("_Templates/PlayerCardTemplate") : null;
            if (template == null)
            {
                Debug.LogWarning("[SetupThemeUI178] _Templates/PlayerCardTemplate이 없어 카드 테마를 건너뜁니다.");
                return;
            }

            for (int i = 1; i <= 6; i++)
            {
                if (template.Find($"Star{i}") is RectTransform star)
                {
                    Undo.RecordObject(star, "Theme Card Star");
                    star.anchoredPosition = new Vector2(star.anchoredPosition.x, -6f);
                    star.sizeDelta = new Vector2(17f, 17f);
                }
            }

            PlaceCardText(template, "OvrText", new Vector2(0f, 1f), new Vector2(6f, -22f), new Vector2(72f, 42f), 36, TextAnchor.UpperLeft, TextWhite, true);
            PlaceCardText(template, "PositionText", new Vector2(0f, 1f), new Vector2(7f, -62f), new Vector2(70f, 22f), 18, TextAnchor.UpperLeft, new Color(0.5f, 0.95f, 0.88f), true);
            PlaceCardText(template, "TeamText", new Vector2(1f, 1f), new Vector2(-6f, -24f), new Vector2(66f, 20f), 15, TextAnchor.UpperRight, TextWhite, true);
            PlaceCardText(template, "SetDeckScoreText", new Vector2(1f, 1f), new Vector2(-6f, -46f), new Vector2(66f, 20f), 15, TextAnchor.UpperRight, Gold, true);

            // 하단 이름 띠(검정 반투명) + 이름(흰 굵은 글씨) - 띠는 FrameOverlay 바로 뒤에 둬 프레임 아트 위, 텍스트 아래에 그린다.
            var strip = Rect(template, "NameStrip", new Vector2(0f, 0f), new Vector2(1f, 0f));
            strip.pivot = new Vector2(0.5f, 0f);
            strip.sizeDelta = new Vector2(0f, 36f);
            Paint(strip.gameObject, new Color(0f, 0f, 0f, 0.62f)).raycastTarget = false;
            var overlay = template.Find("FrameOverlay");
            strip.SetSiblingIndex(overlay != null ? overlay.GetSiblingIndex() + 1 : 0);
            PlaceCardText(template, "NameText", new Vector2(0.5f, 0f), new Vector2(0f, 7f), new Vector2(134f, 26f), 20, TextAnchor.MiddleCenter, TextWhite, true);
            template.Find("NameText")?.SetAsLastSibling();

            if (template.Find("StaminaBarRoot") is RectTransform stamina)
            {
                Undo.RecordObject(stamina, "Theme Card Stamina");
                stamina.anchoredPosition = new Vector2(stamina.anchoredPosition.x, 40f); // 이름 띠 위로
            }

            // 흰 글씨용 가독성: 검정 외곽선 + 그림자(TASK-170의 흰 외곽선은 검정 글씨 기준이었다).
            foreach (var text in template.GetComponentsInChildren<Text>(true))
            {
                var outline = GetOrAdd<Outline>(text.gameObject);
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1.4f, -1.4f);
            }
            MarkDirty(template.GetComponent<PlayerCardUI>());
        }

        private static void PlaceCardText(Transform card, string name, Vector2 anchor, Vector2 position, Vector2 size, int fontSize,
            TextAnchor alignment, Color color, bool bold)
        {
            var t = card.Find(name);
            if (t == null || !t.TryGetComponent<Text>(out var text)) return;
            var rect = (RectTransform)t;
            Undo.RecordObject(rect, "Theme Card Text");
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Undo.RecordObject(text, "Theme Card Text");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(10, fontSize - 8);
            text.resizeTextMaxSize = fontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // ================================================================== 로비(메인 홈)

        public static void BuildLobby()
        {
            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupThemeUI178] LeagueDashboardUIController가 없어 로비를 건너뜁니다.");
                return;
            }

            var panel = (RectTransform)dashboard.transform;
            Stretch(panel, Vector2.zero, Vector2.one);
            Paint(panel.gameObject, BgNavy);

            var so = new SerializedObject(dashboard);
            var keep = new HashSet<Transform>();
            if (so.FindProperty("synergyUIController").objectReferenceValue is Component synergy)
            {
                var top = TopLevelChild(panel, synergy.transform);
                if (top != null) keep.Add(top);
            }

            var root = RecreateLayoutRoot(panel);
            MoveOthersToLegacy(panel, root, keep);

            // 헤더 + 프로필 박스(구단 · 시즌 진행 · 팀 OVR)
            var header = Box(root, "Header", new Vector2(0f, 0.905f), new Vector2(1f, 1f), HeaderBlue);
            var profile = Box(header, "Profile", new Vector2(0.03f, 0.14f), new Vector2(0.7f, 0.86f), new Color(0.97f, 0.97f, 0.99f));
            var badge = Box(profile, "Badge", new Vector2(0f, 0f), new Vector2(0.2f, 1f), PillNavy);
            Label(badge, "Text", "KBO", Vector2.zero, Vector2.one, 30, TextAnchor.MiddleCenter, TextWhite, true);
            var season = Label(profile, "SeasonProgressText", "시즌 진행", new Vector2(0.23f, 0.52f), new Vector2(0.98f, 0.98f), 22, TextAnchor.MiddleLeft, TextMutedOnLight, true);
            var ovr = Label(profile, "TeamOVRText", "OVR -", new Vector2(0.23f, 0.04f), new Vector2(0.98f, 0.52f), 32, TextAnchor.MiddleLeft, OvrPink, true);
            Label(header, "Tagline", "KBO MANAGER", new Vector2(0.72f, 0.2f), new Vector2(0.97f, 0.8f), 26, TextAnchor.MiddleRight, TextWhite, true);

            // 재화 스트립(볼 / 유니폼 / 티켓)
            var currency = CurrencyStrip(root, "CurrencyStrip", new Vector2(0f, 0.866f), new Vector2(1f, 0.905f),
                new[] { ("볼", new Color(0.93f, 0.93f, 0.95f)), ("유니폼", new Color(0.35f, 0.55f, 0.95f)), ("티켓", Gold) });

            // NEXT MATCH 카드
            var next = Box(root, "NextMatchCard", new Vector2(0.03f, 0.69f), new Vector2(0.97f, 0.852f), CardDark);
            AccentBar(next, AccentRed);
            Label(next, "Title", "NEXT MATCH", new Vector2(0.05f, 0.56f), new Vector2(0.95f, 0.95f), 44, TextAnchor.MiddleCenter, TextWhite, true).fontStyle = FontStyle.BoldAndItalic;
            var nextText = Label(next, "NextMatchupText", "예정된 경기가 없습니다.", new Vector2(0.05f, 0.08f), new Vector2(0.95f, 0.54f), 26, TextAnchor.MiddleCenter, TextMutedOnDark, true);

            // 순위표 카드(헤더 띠 + 10행)
            var standings = Box(root, "StandingsCard", new Vector2(0.03f, 0.4f), new Vector2(0.97f, 0.68f), CardDark);
            var standingsHeader = Box(standings, "Header", new Vector2(0f, 0.9f), new Vector2(1f, 1f), CardHeader);
            Label(standingsHeader, "Text", "리그 순위표", new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), 26, TextAnchor.MiddleLeft, TextWhite, true);
            var rows = new List<Text>();
            for (int i = 0; i < 10; i++)
            {
                float y1 = 0.9f - i * 0.09f, y0 = y1 - 0.09f;
                var row = Box(standings, $"Row{i + 1}", new Vector2(0f, y0), new Vector2(1f, y1), i % 2 == 0 ? CardDark : CardDarkAlt);
                var text = Label(row, "Text", $"{i + 1}위", new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), 24, TextAnchor.MiddleLeft, TextWhite);
                text.supportRichText = true; // 응원 구단 강조(<color>) 유지
                rows.Add(text);
            }

            // 시너지 카드(기존 TeamSynergyArea를 유지하고 카드 위치로 옮긴다)
            Box(root, "SynergyCard", new Vector2(0.03f, 0.31f), new Vector2(0.97f, 0.39f), CardDarkAlt);
            foreach (var kept in keep)
            {
                Stretch((RectTransform)kept, new Vector2(0.05f, 0.312f), new Vector2(0.95f, 0.388f));
                kept.SetAsLastSibling();
                foreach (var text in kept.GetComponentsInChildren<Text>(true)) StyleExistingText(text, 22, TextWhite);
            }

            // 메뉴 타일(스카우트 / 리그 기록실 / 플레이 볼)
            var scout = Tile(root, "ScoutTile", "스카우트", "최고의 선수들을 영입하세요.", new Vector2(0.03f, 0.205f), new Vector2(0.49f, 0.3f), new Color(0.14f, 0.16f, 0.22f), TextWhite, AccentRed);
            var stats = Tile(root, "LeagueStatsTile", "리그 기록실", "순위와 개인 기록을 확인하세요.", new Vector2(0.51f, 0.205f), new Vector2(0.97f, 0.3f), new Color(0.22f, 0.27f, 0.72f), TextWhite, Gold);
            var play = Tile(root, "PlayBallTile", "플레이 볼", "다음 경기를 시작합니다.", new Vector2(0.03f, 0.105f), new Vector2(0.97f, 0.195f), new Color(0.96f, 0.96f, 0.98f), TextDark, AccentRed);

            // 하단 5탭 내비게이션
            var nav = Box(root, "BottomNav", new Vector2(0f, 0f), new Vector2(1f, 0.09f), NavBlue);
            var navItems = new[] { ("선수 관리", "inventoryButton"), ("라인업", "rosterButton"), ("홈", null), ("상점", "shopButton"), ("치어리더", "manageCheerleaderButton") };
            for (int i = 0; i < navItems.Length; i++)
            {
                bool isHome = navItems[i].Item2 == null;
                var cell = NavCell(nav, $"Nav{i}", navItems[i].Item1, new Vector2(i / 5f, 0f), new Vector2((i + 1) / 5f, 1f), isHome);
                if (!isHome) so.FindProperty(navItems[i].Item2).objectReferenceValue = cell;
            }

            so.FindProperty("seasonProgressText").objectReferenceValue = season;
            so.FindProperty("teamOVRText").objectReferenceValue = ovr;
            so.FindProperty("nextMatchupText").objectReferenceValue = nextText;
            var rowsProperty = so.FindProperty("standingsRowTexts");
            rowsProperty.arraySize = rows.Count;
            for (int i = 0; i < rows.Count; i++) rowsProperty.GetArrayElementAtIndex(i).objectReferenceValue = rows[i];
            so.FindProperty("favoriteTeamHighlightColor").stringValue = "#FFD54A";
            so.FindProperty("ballText").objectReferenceValue = currency[0];
            so.FindProperty("uniformText").objectReferenceValue = currency[1];
            so.FindProperty("ticketText").objectReferenceValue = currency[2];
            so.FindProperty("scoutButton").objectReferenceValue = scout;
            so.FindProperty("leagueStatsButton").objectReferenceValue = stats;
            so.FindProperty("quickPlayButton").objectReferenceValue = play;
            so.ApplyModifiedProperties();
            MarkDirty(dashboard);
        }

        // ================================================================== 로스터(라인업)

        public static void ThemeRoster()
        {
            var controller = Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupThemeUI178] RosterUIController가 없어 로스터 테마를 건너뜁니다.");
                return;
            }

            var panel = (RectTransform)controller.transform;
            Paint(panel.gameObject, BgStadium);

            // 헤더(파란 바 + "라인업" 필) - 닫기는 헤더 우측 X
            var header = Box(panel, "Header178", new Vector2(0f, 0.94f), new Vector2(1f, 1f), HeaderBlue);
            header.SetAsFirstSibling();
            var pill = Box(header, "Pill", new Vector2(0.32f, 0.16f), new Vector2(0.68f, 0.84f), PillNavy);
            Label(pill, "Text", "라인업", Vector2.zero, Vector2.one, 34, TextAnchor.MiddleCenter, TextWhite, true);
            RestyleButton(panel.Find("CloseButton"), "X", new Vector2(0.86f, 0.948f), new Vector2(0.98f, 0.992f), new Color(1f, 1f, 1f, 0.15f), TextWhite, 36);
            panel.Find("CloseButton")?.SetAsLastSibling();

            // 액션 바(레퍼런스 "자동 교체 / 수비 위치 변경" - 흰 버튼 + 진한 글씨)
            RestyleButton(panel.Find("AutoLineupButton"), "자동 편성", new Vector2(0.02f, 0.896f), new Vector2(0.32f, 0.935f), LightButton, TextDark, 28);
            RestyleButton(panel.Find("SetDeckOptionButton"), "세트덱 버프 선택", new Vector2(0.34f, 0.896f), new Vector2(0.72f, 0.935f), LightButton, TextDark, 28);
            PlaceText(panel.Find("EmptyStateText"), new Vector2(0.02f, 0.87f), new Vector2(0.98f, 0.894f), 22, TextMutedOnLight);

            // 섹션 머리글(진회색 띠 + 흰 글씨) + 슬롯 그리드
            SectionHeader(panel, "StarterHeaderText", new Vector2(0.02f, 0.845f), new Vector2(0.98f, 0.868f));
            SectionHeader(panel, "BenchHeaderText", new Vector2(0.02f, 0.612f), new Vector2(0.98f, 0.635f));
            SectionHeader(panel, "PitcherHeaderText", new Vector2(0.02f, 0.477f), new Vector2(0.98f, 0.5f));
            PlaceGrid(panel.Find("BatterContainer"), new Vector2(0f, 0.637f), new Vector2(1f, 0.843f));
            PlaceGrid(panel.Find("BenchContainer"), new Vector2(0f, 0.502f), new Vector2(1f, 0.61f));
            PlaceGrid(panel.Find("PitcherContainer"), new Vector2(0f, 0.08f), new Vector2(1f, 0.475f));

            // 하단 세트덱 스코어 바(레퍼런스 "OVR 115.9 | 세트덱 스코어 174 POINT" + 시안 게이지)
            var bar = Box(panel, "SetDeckBar178", new Vector2(0f, 0f), new Vector2(1f, 0.076f), StripBlack);
            bar.SetSiblingIndex(1);
            PlaceText(panel.Find("SetDeckStatusText"), new Vector2(0.03f, 0.026f), new Vector2(0.97f, 0.074f), 26, TextWhite);
            Place(panel.Find("SetDeckGaugeFill"), new Vector2(0.03f, 0.008f), new Vector2(0.97f, 0.022f));
            Place(panel.Find("SetDeckActiveGlow"), new Vector2(0f, 0f), new Vector2(1f, 0.076f));

            // 빈 슬롯 플레이스홀더(밝은 배경 위 짙은 슬롯)
            if (panel.Find("SlotPlaceholderTemplate") is Transform placeholder)
            {
                Paint(placeholder.gameObject, new Color(0.2f, 0.24f, 0.35f));
                if (placeholder.Find("Label") is Transform label) StyleExistingText(label.GetComponent<Text>(), 22, new Color(0.78f, 0.85f, 0.98f));
            }

            // 교체 팝업 / 버프 선택 패널 - 네이비 창 + 흰 글씨 + 파랑(확정)/회색(취소)
            if (panel.Find("SwapPopup/Window") is Transform swap)
            {
                Paint(swap.gameObject, CardDark);
                StyleChildText(swap, "TitleText", 30, TextWhite);
                StyleChildText(swap, "PreviewText", 26, Gold);
                StyleChildText(swap, "EmptyText", 28, TextMutedOnDark);
                RestyleButton(swap.Find("ConfirmButton"), "확정", null, null, ConfirmBlue, TextWhite, 32);
                RestyleButton(swap.Find("CancelButton"), "취소", null, null, GrayButton, TextWhite, 32);
                if (swap.Find("CandidateScroll") is Transform scroll) Paint(scroll.gameObject, new Color(1f, 1f, 1f, 0.04f));
            }
            if (panel.Find("SetDeckOptionPanel/Window") is Transform option)
            {
                Paint(option.gameObject, CardDark);
                StyleChildText(option, "TitleText", 28, TextWhite);
                StyleChildText(option, "SummaryText", 22, Gold);
                RestyleButton(option.Find("YearButton"), null, null, null, ConfirmBlue, TextWhite, 28);
                RestyleButton(option.Find("CloseButton"), "닫기", null, null, GrayButton, TextWhite, 30);
            }
            if (controller.TryGetComponent<SetDeckOptionUIController>(out var optionController))
            {
                var oso = new SerializedObject(optionController);
                oso.FindProperty("reachedLabelColor").colorValue = TextWhite;
                oso.FindProperty("unreachedLabelColor").colorValue = new Color(0.55f, 0.6f, 0.7f);
                oso.ApplyModifiedProperties();
                MarkDirty(optionController);
            }
            MarkDirty(controller);
        }

        private static void SectionHeader(Transform panel, string textName, Vector2 aMin, Vector2 aMax)
        {
            var text = panel.Find(textName);
            if (text == null) return;
            var bg = Box(panel, textName + "BG", aMin, aMax, SectionBar);
            bg.SetSiblingIndex(text.GetSiblingIndex()); // 글자 바로 뒤
            PlaceText(text, aMin, aMax, 24, TextWhite);
        }

        private static void PlaceGrid(Transform grid, Vector2 aMin, Vector2 aMax)
        {
            if (grid == null) return;
            Place(grid, aMin, aMax);
            if (grid.TryGetComponent<GridLayoutGroup>(out var layout))
            {
                Undo.RecordObject(layout, "Theme Grid");
                layout.cellSize = new Vector2(140f, 200f);
                layout.spacing = new Vector2(8f, 4f);
                layout.childAlignment = TextAnchor.UpperCenter;
            }
        }

        // ================================================================== 스카우트 허브 / 선수 영입 / 영입 결과

        public static void ThemeScoutHub()
        {
            var hub = Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            if (hub == null) return;
            Paint(hub.gameObject, BgDark);
            var so = new SerializedObject(hub);
            foreach (var field in new[] { "playerTabButton", "cheerleaderTabButton" })
            {
                if (so.FindProperty(field).objectReferenceValue is Button tab)
                {
                    var label = tab.GetComponentInChildren<Text>(true);
                    if (label != null) StyleExistingText(label, 32, TextDark);
                }
            }
            RestyleButton(hub.transform.Find("HubCloseButton"), "X", null, null, new Color(0.2f, 0.22f, 0.27f), TextWhite, 36);
            MarkDirty(hub);
        }

        public static void BuildScoutPanel()
        {
            var controller = Object.FindAnyObjectByType<ScoutUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupThemeUI178] ScoutUIController가 없어 선수 영입 화면을 건너뜁니다.");
                return;
            }

            var panel = (RectTransform)controller.transform;
            Stretch(panel, Vector2.zero, new Vector2(1f, 0.92f));
            Paint(panel.gameObject, BgDark);

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

            // 영입 재화 5종 스트립(2줄 표기 - 컨트롤러가 "이름\n수량"으로 갱신)
            var strip = Box(root, "CurrencyStrip", new Vector2(0f, 0.925f), new Vector2(1f, 1f), StripBlack);
            var currencyTexts = new List<Text>();
            for (int i = 0; i < 5; i++)
            {
                var cell = Rect(strip, $"Cell{i}", new Vector2(i / 5f, 0f), new Vector2((i + 1) / 5f, 1f));
                currencyTexts.Add(Label(cell, "Value", "", new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.95f), 20, TextAnchor.MiddleCenter, TextWhite, true));
            }

            var scoutManager = Object.FindAnyObjectByType<ScoutManager>(FindObjectsInactive.Include);
            int liveNormalCost = scoutManager != null ? scoutManager.LiveNormalCost : 1;
            int liveEpicCost = scoutManager != null ? scoutManager.LiveEpicCost : 1;
            int premiumCost = scoutManager != null ? scoutManager.PremiumCost : 1;
            int pickupCost = scoutManager != null ? scoutManager.PickupCost : 1;
            // [TASK-KBO-185] 골든글러브 이상은 뽑기 제외(특별 영입 전용) - 프리미엄/픽업 = Premium 표, 일반 = Normal 표, 최고 등급 TITLE_HOLDER.
            string premiumRates = DescribeScoutTable(ScoutDropTables.Premium);
            string normalRates = DescribeScoutTable(ScoutDropTables.Normal);

            var products = new[]
            {
                ("LiveNormalBanner", "일반 스카우트", normalRates, "영입권", liveNormalCost, "liveNormalButton", "liveNormalButton10", "liveNormalCostText"),
                ("LiveEpicBanner", "일반 · 라이브 에픽 스카우트", "라이브 에픽 카드 100%", "영입권", liveEpicCost, "liveEpicButton", "liveEpicButton10", "liveEpicCostText"),
                ("PremiumSignatureBanner", "프리미엄 스카우트 (싸인볼)", premiumRates, "싸인볼", premiumCost, "premiumSignatureButton", "premiumSignatureButton10", "premiumSignatureCostText"),
                ("PremiumTitleHolderBanner", "프리미엄 스카우트 (트로피)", premiumRates, "트로피", premiumCost, "premiumTitleHolderButton", "premiumTitleHolderButton10", "premiumTitleHolderCostText"),
                ("PickupSignatureBanner", "픽업 · 선택 구단 스카우트", premiumRates + " · 선택 구단 50%", "픽업권", pickupCost, "pickupSignatureButton", "pickupSignatureButton10", "pickupSignatureCostText"),
                ("PickupTitleHolderBanner", "픽업 · 선택 구단 스카우트 II", premiumRates + " · 선택 구단 50%", "픽업권", pickupCost, "pickupTitleHolderButton", "pickupTitleHolderButton10", "pickupTitleHolderCostText"),
            };
            const float top = 0.915f, height = 0.145f, gap = 0.006f;
            for (int i = 0; i < products.Length; i++)
            {
                float y1 = top - i * (height + gap), y0 = y1 - height;
                var (name, title, tag, currency, cost, b1, b10, costField) = products[i];
                var (one, ten, info) = Banner(root, name, title, tag, currency, cost, new Vector2(0.03f, y0), new Vector2(0.97f, y1));
                so.FindProperty(b1).objectReferenceValue = one;
                so.FindProperty(b10).objectReferenceValue = ten;
                so.FindProperty(costField).objectReferenceValue = info;
            }

            so.FindProperty("liveNormalTicketText").objectReferenceValue = currencyTexts[0];
            so.FindProperty("liveEpicTicketText").objectReferenceValue = currencyTexts[1];
            so.FindProperty("signatureBallText").objectReferenceValue = currencyTexts[2];
            so.FindProperty("trophyText").objectReferenceValue = currencyTexts[3];
            so.FindProperty("pickupTicketText").objectReferenceValue = currencyTexts[4];
            so.ApplyModifiedProperties();

            foreach (var t in keep) t.SetAsLastSibling();
            MarkDirty(controller);
        }

        private static string DescribeScoutTable(IEnumerable<(Grade Grade, float RatePercent)> table) =>
            string.Join(" · ", table.Take(table.Count() - 1).Select(e => $"{CardGrowthRules.DisplayName(e.Grade)} {e.RatePercent:0.#}%"));

        /// <summary>레퍼런스 "SCOUT RESULT": 검정 배경 + 타이틀 + 5열 카드 그리드 + 보라(다시 영입) / 파랑(확인).</summary>
        public static void ThemeScoutResultPopup()
        {
            var controller = Object.FindAnyObjectByType<ScoutUIController>(FindObjectsInactive.Include);
            if (controller == null) return;
            var so = new SerializedObject(controller);
            if (!(so.FindProperty("resultPopupRoot").objectReferenceValue is GameObject popup)) return;

            Paint(popup, new Color(0.04f, 0.05f, 0.07f, 0.97f));
            var title = Label(popup.transform, "ScoutResultTitle178", "SCOUT RESULT", new Vector2(0.05f, 0.84f), new Vector2(0.95f, 0.93f), 64, TextAnchor.MiddleCenter, TextWhite, true);
            title.fontStyle = FontStyle.BoldAndItalic;

            if (so.FindProperty("cardContainer").objectReferenceValue is Transform cards)
            {
                Place(cards, new Vector2(0.04f, 0.2f), new Vector2(0.96f, 0.8f));
                if (cards.TryGetComponent<GridLayoutGroup>(out var grid))
                {
                    Undo.RecordObject(grid, "Theme Result Grid");
                    grid.cellSize = new Vector2(140f, 200f);
                    grid.spacing = new Vector2(16f, 16f);
                    grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                    grid.constraintCount = 5;
                    grid.childAlignment = TextAnchor.MiddleCenter;
                }
            }

            if (popup.transform.Find("ResultButtonContainer") is Transform buttons)
            {
                Place(buttons, new Vector2(0.08f, 0.05f), new Vector2(0.92f, 0.13f));
                if (buttons.TryGetComponent<HorizontalLayoutGroup>(out var layout))
                {
                    Undo.RecordObject(layout, "Theme Result Buttons");
                    layout.spacing = 32f;
                    layout.childControlWidth = true;
                    layout.childControlHeight = true;
                    layout.childForceExpandWidth = true;
                    layout.childForceExpandHeight = true;
                }
            }
            if (so.FindProperty("retryButton").objectReferenceValue is Button retry) RestyleButton(retry.transform, "다시 영입", null, null, Purple, TextWhite, 34);
            if (so.FindProperty("closeResultPopupButton").objectReferenceValue is Button close) RestyleButton(close.transform, "확인", null, null, ConfirmBlue, TextWhite, 34);
            MarkDirty(controller);
        }

        // ================================================================== 치어리더 영입 / 관리

        public static void BuildCheerleaderShop()
        {
            var controller = Object.FindAnyObjectByType<CheerleaderShopUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupThemeUI178] CheerleaderShopUIController가 없어 치어리더 영입 화면을 건너뜁니다.");
                return;
            }

            var panel = (RectTransform)controller.transform;
            Stretch(panel, Vector2.zero, new Vector2(1f, 0.92f));
            Paint(panel.gameObject, BgDark);
            var root = RecreateLayoutRoot(panel);
            MoveOthersToLegacy(panel, root, new HashSet<Transform>());

            // 응원봉 3종(라이브/스타/레전드) - 한정 응원봉은 상품 폐지로 표시하지 않는다(TASK-178).
            var strip = Box(root, "CurrencyStrip", new Vector2(0f, 0.925f), new Vector2(1f, 1f), StripBlack);
            var sticks = new List<Text>();
            for (int i = 0; i < 3; i++)
            {
                var cell = Rect(strip, $"Cell{i}", new Vector2(i / 3f, 0f), new Vector2((i + 1) / 3f, 1f));
                sticks.Add(Label(cell, "Value", "", new Vector2(0.03f, 0.05f), new Vector2(0.97f, 0.95f), 24, TextAnchor.MiddleCenter, TextWhite, true));
            }

            int cost = CheerleaderGachaService.CostPerRoll;
            var banners = new[]
            {
                ("LiveBanner", "일반 · 라이브 영입", CheerleaderDropTables.Describe(CheerleaderDropTables.Live), "liveButton", "liveButton10", "liveInfoText"),
                ("IconBanner", "픽업/프리미엄 · 아이콘 영입", CheerleaderDropTables.Describe(CheerleaderDropTables.Icon), "iconButton", "iconButton10", "iconInfoText"),
                ("LegendBanner", "픽업/프리미엄 · 레전드 영입", CheerleaderDropTables.Describe(CheerleaderDropTables.Legend), "legendButton", "legendButton10", "legendInfoText"),
            };
            var currencies = new[] { "라이브 응원봉", "스타 응원봉", "레전드 응원봉" };
            var so = new SerializedObject(controller);
            const float top = 0.915f, height = 0.175f, gap = 0.008f;
            for (int i = 0; i < banners.Length; i++)
            {
                float y1 = top - i * (height + gap), y0 = y1 - height;
                var (name, title, rates, b1, b10, infoField) = banners[i];
                var (one, ten, info) = Banner(root, name, title, rates, currencies[i], cost, new Vector2(0.03f, y0), new Vector2(0.97f, y1));
                so.FindProperty(b1).objectReferenceValue = one;
                so.FindProperty(b10).objectReferenceValue = ten;
                so.FindProperty(infoField).objectReferenceValue = info;
            }

            var resultCard = Box(root, "ResultCard", new Vector2(0.03f, 0.015f), new Vector2(0.97f, 0.36f), CardDark);
            var resultHeader = Box(resultCard, "Header", new Vector2(0f, 0.86f), new Vector2(1f, 1f), CardHeader);
            Label(resultHeader, "Text", "최근 영입 결과", new Vector2(0.04f, 0f), new Vector2(0.96f, 1f), 26, TextAnchor.MiddleLeft, TextWhite, true);
            var content = VerticalScroll(resultCard, "ResultScroll", new Vector2(0.02f, 0.02f), new Vector2(0.98f, 0.85f), 0f, new Color(1f, 1f, 1f, 0.03f));
            var resultText = Label(content, "ResultLogText", "영입 결과가 여기에 표시됩니다.", Vector2.zero, Vector2.one, 24, TextAnchor.UpperLeft, TextWhite);
            resultText.verticalOverflow = VerticalWrapMode.Overflow;
            resultText.resizeTextForBestFit = false;
            GetOrAdd<ContentSizeFitter>(resultText.gameObject).verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            so.FindProperty("liveCheerStickText").objectReferenceValue = sticks[0];
            so.FindProperty("starCheerStickText").objectReferenceValue = sticks[1];
            so.FindProperty("legendCheerStickText").objectReferenceValue = sticks[2];
            so.FindProperty("resultLogText").objectReferenceValue = resultText;
            so.ApplyModifiedProperties(); // closeButton은 허브 X 버튼이 담당
            MarkDirty(controller);
        }

        public static void BuildCheerleaderInventory()
        {
            var controller = Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupThemeUI178] CheerleaderInventoryUIController가 없어 치어리더 관리 화면을 건너뜁니다.");
                return;
            }

            var panel = (RectTransform)controller.transform;
            Stretch(panel, Vector2.zero, Vector2.one);
            Paint(panel.gameObject, BgNavy);
            var root = RecreateLayoutRoot(panel);
            MoveOthersToLegacy(panel, root, new HashSet<Transform>());

            var header = Box(root, "Header", new Vector2(0f, 0.94f), new Vector2(1f, 1f), HeaderBlue);
            var pill = Box(header, "Pill", new Vector2(0.3f, 0.16f), new Vector2(0.7f, 0.84f), PillNavy);
            Label(pill, "Text", "치어리더 관리", Vector2.zero, Vector2.one, 34, TextAnchor.MiddleCenter, TextWhite, true);
            var ownedCount = Label(header, "OwnedCountText", "보유 0명", new Vector2(0.03f, 0.1f), new Vector2(0.29f, 0.9f), 24, TextAnchor.MiddleLeft, TextWhite, true);
            var close = Btn(header, "CloseButton", "X", new Vector2(0.86f, 0.14f), new Vector2(0.98f, 0.86f), 36, new Color(1f, 1f, 1f, 0.15f), TextWhite);

            var equip = Box(root, "EquippedBox", new Vector2(0.03f, 0.8f), new Vector2(0.97f, 0.928f), CardDark);
            AccentBar(equip, Gold);
            Label(equip, "EquippedTitle", "장착 슬롯 (1)", new Vector2(0.05f, 0.74f), new Vector2(0.97f, 0.97f), 26, TextAnchor.MiddleLeft, Gold, true);
            var summary = Label(equip, "EquippedSummaryText", "장착 슬롯: 비어 있음", new Vector2(0.05f, 0.04f), new Vector2(0.97f, 0.76f), 23, TextAnchor.UpperLeft, TextWhite);

            var filterBar = Rect(root, "FilterBar", new Vector2(0.03f, 0.738f), new Vector2(0.97f, 0.788f));
            var filters = new[] { ("FilterAll", "전체"), ("FilterTeam", "구단: 전체"), ("FilterLive", "LIVE"), ("FilterIcon", "ICON"), ("FilterLegend", "LEGEND") };
            var filterButtons = new List<Button>();
            for (int i = 0; i < filters.Length; i++)
            {
                float x0 = i / (float)filters.Length, x1 = (i + 1) / (float)filters.Length;
                filterButtons.Add(Btn(filterBar, filters[i].Item1, filters[i].Item2, new Vector2(x0 + 0.005f, 0f), new Vector2(x1 - 0.005f, 1f), 24, LightButton, TextDark));
            }

            var content = VerticalScroll(root, "CheerleaderList", new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.728f), 12f, new Color(1f, 1f, 1f, 0.03f));
            var empty = Label(root, "EmptyText", "보유한 치어리더가 없습니다.", new Vector2(0.05f, 0.35f), new Vector2(0.95f, 0.45f), 28, TextAnchor.MiddleCenter, TextMutedOnDark);
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
            so.FindProperty("filterActiveColor").colorValue = Gold;
            so.FindProperty("filterIdleColor").colorValue = LightButton;
            so.ApplyModifiedProperties();
            MarkDirty(controller);
        }

        /// <summary>치어리더 카드(높이 230, 네이비): 티어 뱃지 · 이름 · 구단/활동기간 · 버프 4종 · 보라 장착 버튼.</summary>
        private static GameObject BuildCheerleaderCardTemplate(Transform root)
        {
            var card = Rect(root, "CheerleaderCardTemplate", new Vector2(0f, 1f), new Vector2(1f, 1f));
            card.sizeDelta = new Vector2(0f, 230f);
            var background = Paint(card.gameObject, CardDark);
            var element = GetOrAdd<LayoutElement>(card.gameObject);
            element.preferredHeight = 230f;
            element.minHeight = 230f;
            var outline = GetOrAdd<Outline>(card.gameObject);
            outline.effectColor = new Color(0.3f, 0.38f, 0.6f, 1f);
            outline.effectDistance = new Vector2(2f, -2f);

            var badge = Box(card, "TierBadge", new Vector2(0.03f, 0.6f), new Vector2(0.2f, 0.92f), new Color(0.45f, 0.55f, 0.65f));
            var badgeText = Label(badge, "Text", "LIVE", Vector2.zero, Vector2.one, 26, TextAnchor.MiddleCenter, TextWhite, true);
            var nameText = Label(card, "NameText", "이름", new Vector2(0.22f, 0.66f), new Vector2(0.76f, 0.96f), 36, TextAnchor.MiddleLeft, TextWhite, true);
            var teamText = Label(card, "TeamPeriodText", "KIA · 2020~2021", new Vector2(0.22f, 0.46f), new Vector2(0.76f, 0.66f), 24, TextAnchor.MiddleLeft, TextMutedOnDark);
            var buff = Label(card, "BuffText", "", new Vector2(0.03f, 0.24f), new Vector2(0.38f, 0.44f), 22, TextAnchor.MiddleLeft, TextWhite);
            var clutch = Label(card, "ClutchText", "", new Vector2(0.39f, 0.24f), new Vector2(0.76f, 0.44f), 22, TextAnchor.MiddleLeft, TextWhite);
            var economic = Label(card, "EconomicText", "", new Vector2(0.03f, 0.04f), new Vector2(0.38f, 0.24f), 22, TextAnchor.MiddleLeft, TextWhite);
            var sentiment = Label(card, "SentimentText", "", new Vector2(0.39f, 0.04f), new Vector2(0.76f, 0.24f), 22, TextAnchor.MiddleLeft, TextWhite);
            var equip = Btn(card, "EquipButton", "장착", new Vector2(0.78f, 0.22f), new Vector2(0.97f, 0.78f), 32, Purple, TextWhite);
            var equipLabel = equip.GetComponentInChildren<Text>(true);

            var slot = GetOrAdd<CheerleaderSlotUI>(card.gameObject);
            var so = new SerializedObject(slot);
            so.FindProperty("nameText").objectReferenceValue = nameText;
            so.FindProperty("gradeText").objectReferenceValue = null;
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
            so.FindProperty("normalBackgroundColor").colorValue = CardDark;
            so.FindProperty("equippedBackgroundColor").colorValue = new Color(0.3f, 0.22f, 0.45f);
            so.ApplyModifiedProperties();

            card.gameObject.SetActive(false);
            return card.gameObject;
        }

        // ================================================================== 다크 테마 패스(인벤토리/강화/선수 관리 등)

        /// <summary>
        /// 구조는 그대로 두고 (1) 루트 배경 네이비 (2) 회색/흰색 계열 버튼 → 파랑 (3) 무채색 글자는 "가장 가까운 불투명 배경"의 밝기로
        /// 흰색/짙은색 결정(배경과 대비 확보) (4) 22pt 미만 글자는 best-fit 최대 22pt로 키운다(원래 크기 미만으로는 줄지 않음).
        /// 선수 카드(PlayerCardUI)·치어리더 카드·템플릿·보관함 하위와 유채색(강조) 글자는 건드리지 않는다.
        /// </summary>
        public static void ApplyDarkThemePass<T>() where T : Component
        {
            var controller = Object.FindAnyObjectByType<T>(FindObjectsInactive.Include);
            if (controller == null) return;
            var root = controller.transform;
            if (root.TryGetComponent<Image>(out var rootImage))
            {
                Undo.RecordObject(rootImage, "Dark Theme");
                rootImage.color = BgNavy;
            }

            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                if (IsExcluded(button.transform, root)) continue;
                if (button.targetGraphic is Image image && Saturation(image.color) < 0.25f && image.color.a > 0.3f)
                {
                    Undo.RecordObject(image, "Dark Theme Button");
                    image.color = Luminance(image.color) < 0.25f ? GrayButton : ConfirmBlue;
                }
            }

            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (IsExcluded(text.transform, root)) continue;
                Undo.RecordObject(text, "Dark Theme Text");
                if (Saturation(text.color) < 0.25f)
                {
                    text.color = Luminance(BackgroundColorBehind(text.transform, root)) < 0.5f ? TextWhite : TextDark;
                }
                if (text.fontSize < 22)
                {
                    text.resizeTextMinSize = text.fontSize;
                    text.resizeTextMaxSize = 22;
                    text.resizeTextForBestFit = true;
                }
            }
            MarkDirty(controller);
        }

        private static bool IsExcluded(Transform node, Transform root)
        {
            for (var t = node; t != null && t != root.parent; t = t.parent)
            {
                if (t.GetComponent<PlayerCardUI>() != null || t.GetComponent<CheerleaderSlotUI>() != null) return true;
                if (t.name.StartsWith("_Templates") || t.name.StartsWith("_Legacy")) return true;
            }
            return false;
        }

        private static Color BackgroundColorBehind(Transform node, Transform root)
        {
            for (var t = node.parent; t != null; t = t.parent)
            {
                if (t.TryGetComponent<Image>(out var image) && image.enabled && image.color.a > 0.3f) return image.color;
                if (t == root) break;
            }
            return BgNavy;
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static float Saturation(Color c)
        {
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            return max <= 0.0001f ? 0f : (max - min) / max;
        }

        // ================================================================== 조립 헬퍼

        /// <summary>영입 배너: 어두운 헤더 띠(보유/비용 정보 - 런타임 갱신) + 밝은 본문(상품명 · 확률 태그) + 보라 버튼 2개(비용 띠 포함).</summary>
        private static (Button one, Button ten, Text info) Banner(Transform parent, string name, string title, string tag, string currency, int costPerRoll,
            Vector2 aMin, Vector2 aMax)
        {
            var box = Box(parent, name, aMin, aMax, CardLight);
            var outline = GetOrAdd<Outline>(box.gameObject);
            outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
            outline.effectDistance = new Vector2(2f, -2f);
            var head = Box(box, "Header", new Vector2(0f, 0.74f), new Vector2(1f, 1f), CardHeader);
            var info = Label(head, "Info", "", new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), 19, TextAnchor.MiddleLeft, TextWhite, true);
            Label(box, "Title", title, new Vector2(0.03f, 0.4f), new Vector2(0.56f, 0.72f), 32, TextAnchor.MiddleLeft, PillNavy, true);
            var tagBox = Box(box, "Tag", new Vector2(0.03f, 0.08f), new Vector2(0.56f, 0.38f), new Color(0.2f, 0.22f, 0.3f));
            Label(tagBox, "Text", tag, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), 19, TextAnchor.MiddleLeft, Gold, true);
            var one = PurpleButton(box, "Roll1Button", "1회 영입", $"{currency} ×{costPerRoll:N0}", new Vector2(0.58f, 0.08f), new Vector2(0.775f, 0.68f));
            var ten = PurpleButton(box, "Roll10Button", "10회 영입", $"{currency} ×{costPerRoll * 10:N0}", new Vector2(0.785f, 0.08f), new Vector2(0.98f, 0.68f));
            return (one, ten, info);
        }

        /// <summary>보라 구매 버튼(위: 동작명, 아래 진보라 띠: 비용) - 레퍼런스 "1회 구매하기 / 영입권 1".</summary>
        private static Button PurpleButton(Transform parent, string name, string label, string cost, Vector2 aMin, Vector2 aMax)
        {
            var rect = Rect(parent, name, aMin, aMax);
            var image = Paint(rect.gameObject, Purple);
            image.raycastTarget = true;
            var button = GetOrAdd<Button>(rect.gameObject);
            button.targetGraphic = image;
            Label(rect, "Label", label, new Vector2(0.04f, 0.42f), new Vector2(0.96f, 0.98f), 26, TextAnchor.MiddleCenter, TextWhite, true);
            var costBar = Box(rect, "CostBar", new Vector2(0.06f, 0.07f), new Vector2(0.94f, 0.42f), PurpleDeep);
            Label(costBar, "Text", cost, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), 18, TextAnchor.MiddleCenter, Gold, true);
            return button;
        }

        private static Button Tile(Transform parent, string name, string title, string subtitle, Vector2 aMin, Vector2 aMax, Color color, Color textColor, Color accent)
        {
            var rect = Rect(parent, name, aMin, aMax);
            var image = Paint(rect.gameObject, color);
            image.raycastTarget = true;
            var button = GetOrAdd<Button>(rect.gameObject);
            button.targetGraphic = image;
            AccentBar(rect, accent);
            Label(rect, "Title", title, new Vector2(0.07f, 0.42f), new Vector2(0.97f, 0.95f), 38, TextAnchor.MiddleLeft, textColor, true);
            Label(rect, "Subtitle", subtitle, new Vector2(0.07f, 0.06f), new Vector2(0.97f, 0.42f), 20, TextAnchor.MiddleLeft,
                Luminance(color) < 0.5f ? TextMutedOnDark : TextMutedOnLight);
            return button;
        }

        private static Button NavCell(Transform nav, string name, string label, Vector2 aMin, Vector2 aMax, bool active)
        {
            var rect = Rect(nav, name, aMin, aMax);
            var image = Paint(rect.gameObject, active ? NavActive : NavBlue);
            image.raycastTarget = true;
            var button = GetOrAdd<Button>(rect.gameObject);
            button.targetGraphic = image;
            button.interactable = !active; // 현재 화면(홈)
            Label(rect, "Label", label, new Vector2(0.03f, 0.1f), new Vector2(0.97f, 0.9f), 26, TextAnchor.MiddleCenter, active ? TextWhite : NavText, active);
            if (active) Box(rect, "Marker", new Vector2(0.3f, 0.05f), new Vector2(0.7f, 0.1f), TextWhite);
            return button;
        }

        /// <summary>검정 재화 스트립 - 칸마다 [색 점 · 이름 · 수량(우측, 런타임 갱신) · 회색 +]. 수량 Text 배열 반환.</summary>
        private static Text[] CurrencyStrip(Transform parent, string name, Vector2 aMin, Vector2 aMax, (string Label, Color Dot)[] items)
        {
            var strip = Box(parent, name, aMin, aMax, StripBlack);
            var values = new Text[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                var cell = Rect(strip, $"Cell{i}", new Vector2(i / (float)items.Length, 0f), new Vector2((i + 1) / (float)items.Length, 1f));
                Box(cell, "Dot", new Vector2(0.04f, 0.25f), new Vector2(0.11f, 0.75f), items[i].Dot);
                Label(cell, "Name", items[i].Label, new Vector2(0.13f, 0f), new Vector2(0.4f, 1f), 18, TextAnchor.MiddleLeft, TextMutedOnDark);
                values[i] = Label(cell, "Value", "0", new Vector2(0.4f, 0f), new Vector2(0.82f, 1f), 28, TextAnchor.MiddleRight, TextWhite, true);
                var plus = Box(cell, "Plus", new Vector2(0.85f, 0.15f), new Vector2(0.97f, 0.85f), new Color(0.45f, 0.47f, 0.52f));
                Label(plus, "Text", "+", Vector2.zero, Vector2.one, 28, TextAnchor.MiddleCenter, TextWhite, true);
            }
            return values;
        }

        private static void AccentBar(Transform parent, Color color) =>
            Box(parent, "Accent", new Vector2(0f, 0f), new Vector2(0.015f, 1f), color);

        private static void RestyleButton(Transform button, string label, Vector2? aMin, Vector2? aMax, Color color, Color textColor, int fontSize)
        {
            if (button == null) return;
            if (aMin.HasValue && aMax.HasValue) Place(button, aMin.Value, aMax.Value);
            if (button.TryGetComponent<Image>(out var image))
            {
                Undo.RecordObject(image, "Theme Button");
                image.color = color;
            }
            var text = button.GetComponentInChildren<Text>(true);
            if (text == null) return;
            if (label != null)
            {
                Undo.RecordObject(text, "Theme Button Label");
                text.text = label;
            }
            StyleExistingText(text, fontSize, textColor);
            text.fontStyle = FontStyle.Bold;
        }

        private static void StyleChildText(Transform parent, string name, int fontSize, Color color)
        {
            if (parent.Find(name) is Transform t && t.TryGetComponent<Text>(out var text)) StyleExistingText(text, fontSize, color);
        }

        private static void StyleExistingText(Text text, int fontSize, Color color)
        {
            if (text == null) return;
            Undo.RecordObject(text, "Theme Text");
            text.color = color;
            text.fontSize = fontSize;
            text.font = KBOFonts.Default;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(12, fontSize - 10);
            text.resizeTextMaxSize = fontSize;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private static void PlaceText(Transform target, Vector2 aMin, Vector2 aMax, int fontSize, Color color)
        {
            if (target == null) return;
            Place(target, aMin, aMax);
            if (target.TryGetComponent<Text>(out var text)) StyleExistingText(text, fontSize, color);
        }

        private static void Place(Transform target, Vector2 aMin, Vector2 aMax)
        {
            if (target is RectTransform rect) Stretch(rect, aMin, aMax);
        }

        private static RectTransform RecreateLayoutRoot(RectTransform panel)
        {
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
            return Rect(panel, LayoutRootName, Vector2.zero, Vector2.one);
        }

        /// <summary>panel 직속 자식 중 root/keep/보관함 외 전부(TASK-177 Layout177·_Legacy177 포함)를 비활성 보관함으로 옮긴다.</summary>
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

            foreach (var child in Enumerable.Range(0, panel.childCount).Select(panel.GetChild).ToList())
            {
                if (child == root || child == holder || keep.Contains(child)) continue;
                var stale = holder.Find(child.name);
                if (stale != null) Undo.DestroyObjectImmediate(stale.gameObject);
                Undo.SetTransformParent(child, holder, "Move To Legacy178");
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
            Undo.RecordObject(image, "Paint");
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
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(12, fontSize - 10);
            label.resizeTextMaxSize = fontSize;
            label.raycastTarget = false;
            return label;
        }

        private static Button Btn(Transform parent, string name, string text, Vector2 aMin, Vector2 aMax, int fontSize, Color color, Color textColor)
        {
            var rect = Rect(parent, name, aMin, aMax);
            var image = Paint(rect.gameObject, color);
            image.raycastTarget = true;
            var button = GetOrAdd<Button>(rect.gameObject);
            button.targetGraphic = image;
            Label(rect, "Label", text, new Vector2(0.03f, 0f), new Vector2(0.97f, 1f), fontSize, TextAnchor.MiddleCenter, textColor, true);
            return button;
        }

        private static RectTransform VerticalScroll(Transform parent, string name, Vector2 aMin, Vector2 aMax, float spacing, Color background)
        {
            var viewport = Rect(parent, name, aMin, aMax);
            Paint(viewport.gameObject, background);
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
            if (component == null) return;
            EditorUtility.SetDirty(component);
            if (component.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
        }
    }
}
