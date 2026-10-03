using KBOManager.Controllers;
using KBOManager.Managers;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-180] 씬 적용(전부 idempotent).
    ///   1) 치어리더 관리(CheerleaderInventoryPanel/Layout178): 장착 영역을 키워 6인 역할 편성 3x2 그리드(CheerSquadPanel)를 넣고,
    ///      필터 바/보유 목록을 아래로 내린다. 요약 줄(EquippedSummaryText)은 그리드 아래 상태 줄로 재배치.
    ///   2) 메인 홈(로비): 메인 홈.jpg 1:1 레이아웃 + 대구 삼성 라이온즈 파크 배경(BuildLobby180).
    /// 경기 중계 그라운드의 라이온즈 파크 교체는 CompyaMatchView가 런타임에 Broadcast180 텍스처를 읽으므로 씬 작업이 필요 없다.
    /// </summary>
    public static class SetupTask180
    {
        [MenuItem("KBO Manager/Setup/Apply TASK-180 (Cheer Squad 6 + Lobby)")]
        public static void ApplyAll()
        {
            SetupCompyaMatchUI179.ConfigureTextureImporters("Assets/Resources/Broadcast180");
            ApplyCheerSquadLayout();
            BuildLobby180();
            Debug.Log("[SetupTask180] TASK-180 적용 완료 - 씬을 저장(Ctrl+S)하십시오.");
        }

        public static void ApplyCheerSquadLayout()
        {
            var controller = Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include);
            var layout = controller != null ? controller.transform.Find("Layout178") : null;
            if (layout == null)
            {
                Debug.LogWarning("[SetupTask180] 치어리더 관리 화면(Layout178)이 없어 6인 편성 UI를 건너뜁니다 - TASK-178 테마를 먼저 적용하십시오.");
                return;
            }

            var box = layout.Find("EquippedBox") as RectTransform;
            if (box != null)
            {
                SetAnchors(box, 0.03f, 0.545f, 0.97f, 0.928f);
                var title = box.Find("EquippedTitle") as RectTransform;
                if (title != null)
                {
                    SetAnchors(title, 0.03f, 0.915f, 0.97f, 0.995f);
                    var text = title.GetComponent<Text>();
                    if (text != null) { Undo.RecordObject(text, "Cheer Squad Title"); text.text = "치어리더 6인 역할 편성 - 슬롯을 누른 뒤 아래 목록에서 [장착]"; }
                }
                var summary = box.Find("EquippedSummaryText") as RectTransform;
                if (summary != null)
                {
                    SetAnchors(summary, 0.03f, 0.01f, 0.97f, 0.11f);
                    var text = summary.GetComponent<Text>();
                    if (text != null) { Undo.RecordObject(text, "Cheer Squad Summary"); text.fontSize = 22; text.resizeTextForBestFit = true; text.resizeTextMinSize = 14; text.resizeTextMaxSize = 22; }
                }

                var holder = box.Find("CheerSquad180") as RectTransform;
                if (holder == null)
                {
                    var go = new GameObject("CheerSquad180", typeof(RectTransform));
                    Undo.RegisterCreatedObjectUndo(go, "Create Cheer Squad Grid");
                    holder = (RectTransform)go.transform;
                    holder.SetParent(box, false);
                }
                SetAnchors(holder, 0.015f, 0.12f, 0.985f, 0.91f);
                if (!holder.TryGetComponent<CheerSquadPanel>(out var panel)) panel = Undo.AddComponent<CheerSquadPanel>(holder.gameObject);
                panel.ConfigureFonts(KBOFonts.Bold, KBOFonts.Medium);
                panel.Build();
                EditorUtility.SetDirty(panel);

                var so = new SerializedObject(controller);
                so.FindProperty("squadPanel").objectReferenceValue = panel;
                so.ApplyModifiedProperties();
            }

            if (layout.Find("FilterBar") is RectTransform filterBar) SetAnchors(filterBar, 0.03f, 0.488f, 0.97f, 0.535f);
            if (layout.Find("CheerleaderList") is RectTransform list) SetAnchors(list, 0.03f, 0.01f, 0.97f, 0.478f);
            if (layout.Find("EmptyText") is RectTransform empty) SetAnchors(empty, 0.05f, 0.2f, 0.95f, 0.3f);
            MarkDirty(controller);
        }

        // ================================================================== 메인 홈(로비) - 메인 홈.jpg 1:1 + 대구 삼성 라이온즈 파크

        private static readonly Color NavBlue = new Color(0.2f, 0.42f, 0.78f);
        private static readonly Color NavActive = new Color(0.09f, 0.19f, 0.43f);
        private static readonly Color TileDark = new Color(0.11f, 0.12f, 0.16f, 0.95f);
        private static readonly Color AccentRed = new Color(0.86f, 0.16f, 0.22f);
        private static readonly Color White = new Color(0.97f, 0.98f, 1f);
        private static readonly Color Ink = new Color(0.1f, 0.11f, 0.15f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);

        /// <summary>
        /// [TASK-KBO-180] 로비를 레퍼런스 "메인 홈.jpg"(1248x1972) 좌표 그대로 조립한다 - 배경 = 라이온즈 파크(CropLionsParkAssets180.py),
        /// 좌상단 구단 박스 + 명판(구단#·OVR·레벨), 재화 바(P·★·볼 + "+"), 우상단 기록/순위, LIVE 배너 자리 = NEXT MATCH, 우측 퀵메뉴 5개,
        /// 중앙 대표 선수 명판, 시그니처 아이콘, 하단 벤토 타일(스카우트 / 세트덱 스코어 / 커뮤니티 / 후원사 / 플레이 볼), 하단 5탭(홈 전용).
        /// TASK-178 Layout178은 비활성으로 남겨 두고(다음 실행 시 178이 다시 만들고 180이 다시 끈다) LeagueDashboardUIController 바인딩을
        /// 새 오브젝트로 옮긴다. 세트덱/치어리더/팬심 요약(TeamSynergyArea)은 패널 직속 그대로 두고 LIVE 배너 아래로 옮긴다.
        /// </summary>
        public static void BuildLobby180()
        {
            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupTask180] LeagueDashboardUIController가 없어 메인 홈을 건너뜁니다.");
                return;
            }
            var panel = (RectTransform)dashboard.transform;
            foreach (var stale in panel.GetComponentsInChildren<Transform>(true))
            {
                if (stale != null && stale != panel && stale.name == "Layout180") Undo.DestroyObjectImmediate(stale.gameObject);
            }
            var layout178 = panel.Find("Layout178");
            if (layout178 != null) { Undo.RecordObject(layout178.gameObject, "Hide Layout178"); layout178.gameObject.SetActive(false); }

            var kit = new CompyaUiKit(KBOFonts.Bold, KBOFonts.Medium);
            var go = new GameObject("Layout180", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create Lobby180");
            var root = (RectTransform)go.transform;
            root.SetParent(panel, false);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = Vector2.zero; root.offsetMax = Vector2.zero;
            root.SetSiblingIndex(layout178 != null ? layout178.GetSiblingIndex() + 1 : 0);

            CompyaUiKit.Picture(root, "Background", 0, 0, 1248, 1972, "Broadcast180/lobby_lionspark");

            // 좌상단 구단 박스 + 명판
            var teamBox = CompyaUiKit.Box(root, "TeamBox", 10, 115, 190, 220, new Color(0.13f, 0.36f, 0.82f));
            var teamLogo = CompyaUiKit.Logo(root, "TeamLogo", 22, 120, 178, 215);
            CompyaUiKit.Box(root, "NamePlate", 190, 115, 492, 170, new Color(0.96f, 0.96f, 0.98f, 0.95f));
            var profileName = kit.Label(root, "ProfileName", "삼성#단장", 200, 115, 485, 170, 40, TextAnchor.MiddleRight, Ink, true);
            CompyaUiKit.Box(root, "StatPlate", 190, 170, 492, 220, new Color(0.9f, 0.91f, 0.94f, 0.95f));
            var ovr = kit.Label(root, "TeamOVRText", "OVR -", 200, 170, 350, 220, 34, TextAnchor.MiddleLeft, new Color(0.9f, 0.2f, 0.45f), true);
            var level = kit.Label(root, "LevelText", "레벨 1", 350, 170, 485, 220, 30, TextAnchor.MiddleRight, Ink);

            // 재화 바(P / ★ / 볼) + "+" (상점)
            CompyaUiKit.Box(root, "CurrencyStrip", 8, 228, 1240, 290, new Color(0f, 0f, 0f, 0.55f));
            kit.Button(root, "Dropdown", "▼", 15, 232, 68, 286, new Color(0.14f, 0.15f, 0.19f), White, 30);
            CompyaUiKit.Box(root, "IconP", 82, 234, 128, 284, new Color(0.1f, 0.15f, 0.45f));
            kit.Label(root, "IconPText", "P", 82, 234, 128, 284, 34, TextAnchor.MiddleCenter, White, true);
            var ball = kit.Label(root, "BallText", "0", 140, 232, 422, 286, 42, TextAnchor.MiddleRight, White, true);
            kit.Label(root, "IconStar", "★", 497, 230, 550, 288, 50, TextAnchor.MiddleCenter, Gold, true);
            var uniform = kit.Label(root, "UniformText", "0", 555, 232, 778, 286, 42, TextAnchor.MiddleRight, White, true);
            kit.Label(root, "IconBall", "●", 850, 230, 902, 288, 48, TextAnchor.MiddleCenter, White, true);
            var ticket = kit.Label(root, "TicketText", "0", 905, 232, 1170, 286, 42, TextAnchor.MiddleRight, White, true);
            foreach (var (name, x0, x1) in new[] { ("PlusP", 430f, 485f), ("PlusStar", 785f, 840f), ("PlusBall", 1178f, 1235f) })
                Relay(kit.Button(root, name, "+", x0, 232, x1, 286, new Color(0.52f, 0.54f, 0.59f), White, 44), ScreenType.Shop);

            // 우상단(레퍼런스 채팅/우편 자리) - 기록실 / 순위표
            var statsTop = kit.Button(root, "StatsTopButton", "기록", 1042, 120, 1135, 205, new Color(0.18f, 0.19f, 0.24f, 0.9f), White, 30);
            var rankTop = kit.Button(root, "RankTopButton", "순위", 1142, 120, 1235, 205, new Color(0.18f, 0.19f, 0.24f, 0.9f), White, 30);

            // LIVE 업데이트 배너 자리 = NEXT MATCH
            CompyaUiKit.Box(root, "NextMatchBanner", 15, 318, 615, 435, new Color(0.05f, 0.05f, 0.08f, 0.92f));
            CompyaUiKit.Box(root, "NextMatchAccent", 15, 318, 24, 435, AccentRed);
            kit.Label(root, "NextMatchTitle", "<color=#E5303C>NEXT</color> MATCH", 36, 322, 600, 372, 48, TextAnchor.MiddleLeft, White, true, true);
            var nextText = kit.Label(root, "NextMatchupText", "예정된 경기가 없습니다.", 36, 372, 605, 430, 27, TextAnchor.MiddleLeft, White);

            // 우측 퀵메뉴(라이브 / 패스 / 이벤트 / 도전 과제 / 출석 자리)
            var quick = new (string icon, string label, ScreenType? screen)[]
            {
                ("LIVE", "라이브", ScreenType.Scout), ("SET", "세트덱", ScreenType.Roster), ("CHEER", "응원단", ScreenType.CheerleaderShop),
                ("REC", "기록실", ScreenType.LeagueStats), ("RANK", "순위", null),
            };
            var slots = new[] { (1025f, 330f), (1140f, 330f), (1025f, 440f), (1140f, 440f), (1140f, 550f) };
            Button rankQuick = null;
            for (int i = 0; i < quick.Length; i++)
            {
                var (x, y) = slots[i];
                var button = kit.Button(root, $"Quick{i}", quick[i].icon, x, y, x + 83, y + 70, new Color(0.13f, 0.14f, 0.18f, 0.92f),
                    i == 0 ? AccentRed : White, i == 0 ? 34 : 26, true, true);
                CompyaUiKit.Box(root, $"QuickLabelBg{i}", x - 10, y + 70, x + 93, y + 98, new Color(0.05f, 0.05f, 0.08f, 0.75f));
                kit.Label(root, $"QuickLabel{i}", quick[i].label, x - 10, y + 70, x + 93, y + 98, 24, TextAnchor.MiddleCenter, White, true);
                if (quick[i].screen.HasValue) Relay(button, quick[i].screen.Value); else rankQuick = button;
            }

            // 세트덱/치어리더/팬심 요약(기존 TeamSynergyArea) - NEXT MATCH 아래 반투명 카드
            CompyaUiKit.Box(root, "SynergyCard", 15, 445, 615, 585, new Color(0.05f, 0.06f, 0.12f, 0.72f));
            var so = new SerializedObject(dashboard);
            if (so.FindProperty("synergyUIController").objectReferenceValue is Component synergy)
            {
                var top = synergy.transform;
                while (top != null && top.parent != panel) top = top.parent;
                if (top is RectTransform synergyRect)
                {
                    Undo.RecordObject(synergyRect, "Move Synergy");
                    CompyaUiKit.SetBox(synergyRect, 25, 450, 605, 580);
                    synergyRect.SetAsLastSibling();
                }
            }

            // 중앙 대표 선수 명판 + 시그니처 아이콘
            CompyaUiKit.Box(root, "HeroPlate", 700, 1250, 1060, 1350, new Color(0.04f, 0.06f, 0.15f, 0.78f));
            CompyaUiKit.Box(root, "HeroAccent", 700, 1250, 707, 1350, Gold);
            kit.Label(root, "HeroCaption", "대표 선수", 718, 1252, 1050, 1282, 22, TextAnchor.MiddleLeft, Gold, true);
            var heroName = kit.Label(root, "HeroName", "", 718, 1280, 1050, 1320, 36, TextAnchor.MiddleLeft, White, true);
            var heroDetail = kit.Label(root, "HeroDetail", "", 718, 1318, 1050, 1347, 22, TextAnchor.MiddleLeft, new Color(0.75f, 0.8f, 0.9f));
            var signature = kit.Button(root, "SignatureButton", "SIG", 930, 1385, 1050, 1458, new Color(0.62f, 0.2f, 0.45f, 0.9f), White, 36, true, true);
            Relay(signature, ScreenType.Scout);
            CompyaUiKit.Box(root, "SignatureCaptionBg", 915, 1460, 1065, 1520, new Color(0.04f, 0.05f, 0.1f, 0.8f));
            kit.Label(root, "SignatureCaption", "시그니처 획득", 915, 1460, 1065, 1488, 22, TextAnchor.MiddleCenter, new Color(0.45f, 0.9f, 0.95f), true);
            var signatureText = kit.Label(root, "SignatureText", "", 915, 1488, 1065, 1518, 22, TextAnchor.MiddleCenter, Gold, true);

            // 하단 벤토 타일
            var scout = Tile(kit, root, "ScoutTile", 190, 1538, 543, 1667, TileDark, "스카우트", "최고의 선수들을 영입하세요.", White, 52);
            var setDeck = Tile(kit, root, "SetDeckTile", 553, 1538, 1060, 1667, new Color(0.25f, 0.2f, 0.78f, 0.97f), "세트덱 스코어", "라인업 편성 · 세트덱 버프", White, 60, true);
            kit.Label(root, "SetDeckEyebrow", "S E T   D E C K   S C O R E", 585, 1542, 1000, 1566, 20, TextAnchor.MiddleLeft, new Color(0.5f, 0.9f, 1f), true);
            Relay(setDeck, ScreenType.Roster);
            var community = Tile(kit, root, "CommunityTile", 190, 1680, 360, 1810, TileDark, "커뮤니티", "리그 기록실", White, 40);
            var sponsor = Tile(kit, root, "SponsorTile", 370, 1680, 543, 1810, TileDark, "후원사", "응원단 영입", White, 40);
            Relay(sponsor, ScreenType.CheerleaderShop);
            var play = Tile(kit, root, "PlayBallTile", 553, 1680, 1060, 1810, new Color(0.95f, 0.95f, 0.97f, 0.98f), "플레이 볼", "다양한 모드를 즐겨보세요.", Ink, 64);
            var baseball = kit.Label(root, "Baseball", "●", 870, 1675, 1050, 1815, 170, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.95f), true);
            CompyaUiKit.Outline(baseball, new Color(0.85f, 0.2f, 0.25f, 0.9f), 2f);

            // 하단 5탭(홈 전용 - LobbyOnlyNav)
            var nav = CompyaUiKit.Box(root, "BottomNav", 0, 1845, 1248, 1972, NavBlue);
            nav.raycastTarget = true;
            var navItems = new[] { ("선수 관리", "inventoryButton"), ("라인업", "rosterButton"), ("홈", null), ("상점", "shopButton"), ("구단 관리", "manageCheerleaderButton") };
            var navIcons = new[] { "▣", "◆", "⬟", "▲", "●" };
            for (int i = 0; i < navItems.Length; i++)
            {
                bool isHome = navItems[i].Item2 == null;
                var cellRect = CompyaUiKit.Norm(nav.transform, $"Nav{i}", i / 5f, 0f, (i + 1) / 5f, 1f);
                var cellImage = CompyaUiKit.Paint(cellRect, isHome ? NavActive : new Color(0f, 0f, 0f, 0.001f), true);
                var cell = cellRect.gameObject.AddComponent<Button>();
                cell.targetGraphic = cellImage;
                var color = isHome ? White : new Color(0.68f, 0.79f, 0.96f);
                kit.LabelOn(CompyaUiKit.Norm(cellRect, "Icon", 0f, 0.42f, 1f, 0.95f), navIcons[i], 46, TextAnchor.MiddleCenter, color, true);
                kit.LabelOn(CompyaUiKit.Norm(cellRect, "Label", 0f, 0.05f, 1f, 0.42f), navItems[i].Item1, 32, TextAnchor.MiddleCenter, color, isHome);
                if (isHome) CompyaUiKit.Paint(CompyaUiKit.Norm(cellRect, "ActiveBar", 0.25f, 0f, 0.75f, 0.04f), White);
                else so.FindProperty(navItems[i].Item2).objectReferenceValue = cell;
            }
            nav.transform.SetAsLastSibling();
            if (!nav.TryGetComponent<LobbyOnlyNav>(out _)) Undo.AddComponent<LobbyOnlyNav>(nav.gameObject);

            // 순위표 팝업(우상단 "순위" / 퀵메뉴 "순위")
            var popup = CompyaUiKit.Box(root, "StandingsPopup", 0, 0, 1248, 1972, new Color(0f, 0f, 0f, 0.7f));
            popup.raycastTarget = true;
            CompyaUiKit.Box(popup.transform, "Panel", 90, 360, 1158, 1560, new Color(0.08f, 0.1f, 0.18f, 0.98f));
            kit.Label(popup.transform, "Title", "리그 순위표", 90, 370, 1158, 450, 50, TextAnchor.MiddleCenter, White, true);
            var progress = kit.Label(popup.transform, "SeasonProgressText", "", 90, 450, 1158, 500, 28, TextAnchor.MiddleCenter, new Color(0.7f, 0.76f, 0.88f));
            var rows = new Text[10];
            for (int i = 0; i < 10; i++)
            {
                float y0 = 510 + i * 90f;
                CompyaUiKit.Box(popup.transform, $"RowBg{i + 1}", 120, y0, 1128, y0 + 84, i % 2 == 0 ? new Color(0.12f, 0.15f, 0.26f) : new Color(0.15f, 0.19f, 0.31f));
                rows[i] = kit.Label(popup.transform, $"Row{i + 1}", $"{i + 1}위", 140, y0, 1110, y0 + 84, 32, TextAnchor.MiddleLeft, White);
            }
            var close = kit.Button(popup.transform, "Close", "닫기", 474, 1440, 774, 1530, new Color(0.15f, 0.33f, 0.86f), White, 40);
            Relay(close, popup.gameObject);
            Relay(rankTop, popup.gameObject);
            if (rankQuick != null) Relay(rankQuick, popup.gameObject);
            popup.gameObject.SetActive(false);

            var extras = root.gameObject.AddComponent<LobbyHomeExtras>();
            extras.Bind(profileName, level, heroName, heroDetail, signatureText, teamBox, teamLogo);

            so.FindProperty("seasonProgressText").objectReferenceValue = progress;
            so.FindProperty("teamOVRText").objectReferenceValue = ovr;
            so.FindProperty("nextMatchupText").objectReferenceValue = nextText;
            var rowsProperty = so.FindProperty("standingsRowTexts");
            rowsProperty.arraySize = rows.Length;
            for (int i = 0; i < rows.Length; i++) rowsProperty.GetArrayElementAtIndex(i).objectReferenceValue = rows[i];
            so.FindProperty("ballText").objectReferenceValue = ball;
            so.FindProperty("uniformText").objectReferenceValue = uniform;
            so.FindProperty("ticketText").objectReferenceValue = ticket;
            so.FindProperty("scoutButton").objectReferenceValue = scout;
            so.FindProperty("leagueStatsButton").objectReferenceValue = community;
            so.FindProperty("quickPlayButton").objectReferenceValue = play;
            so.ApplyModifiedProperties();
            Relay(statsTop, ScreenType.LeagueStats);
            MarkDirty(dashboard);
        }

        /// <summary>레퍼런스 벤토 타일: 배경 + 좌측 빨간 강조 막대 + 굵은 제목 + 회색 부제.</summary>
        private static Button Tile(CompyaUiKit kit, Transform root, string name, float x0, float y0, float x1, float y1, Color background,
            string title, string subtitle, Color titleColor, float titleSize, bool italic = false)
        {
            var button = kit.Button(root, name, "", x0, y0, x1, y1, background, titleColor, 10);
            float h = y1 - y0;
            CompyaUiKit.Box(root, name + "Accent", x0 + 14, y0 + h * 0.22f, x0 + 20, y0 + h * 0.52f, AccentRed);
            kit.Label(root, name + "Title", title, x0 + 28, y0 + h * 0.12f, x1 - 10, y0 + h * 0.6f, titleSize, TextAnchor.MiddleLeft, titleColor, true, italic);
            var subColor = titleColor == Ink ? new Color(0.45f, 0.47f, 0.52f) : new Color(0.8f, 0.82f, 0.88f);
            kit.Label(root, name + "Subtitle", subtitle, x0 + 30, y0 + h * 0.6f, x1 - 10, y0 + h * 0.9f, 26, TextAnchor.MiddleLeft, subColor);
            return button;
        }

        private static void Relay(Button button, ScreenType screen)
        {
            if (!button.TryGetComponent<LobbyButtonRelay>(out var relay)) relay = Undo.AddComponent<LobbyButtonRelay>(button.gameObject);
            relay.Configure(screen);
            EditorUtility.SetDirty(relay);
        }

        private static void Relay(Button button, GameObject toggle)
        {
            if (!button.TryGetComponent<LobbyButtonRelay>(out var relay)) relay = Undo.AddComponent<LobbyButtonRelay>(button.gameObject);
            relay.Configure(toggle);
            EditorUtility.SetDirty(relay);
        }

        private static void SetAnchors(RectTransform rect, float x0, float y0, float x1, float y1)
        {
            Undo.RecordObject(rect, "TASK-180 Layout");
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void MarkDirty(Component component)
        {
            EditorUtility.SetDirty(component);
            var scene = component.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
