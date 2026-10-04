using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-181] 씬 적용(전부 idempotent - 다시 실행하면 Layout181을 지우고 새로 만든다).
    ///   0) 매니저: OnboardingManager / SaveManager가 씬에 없으면 GameManagers에 붙이고 PlayerDatabase·RosterManager·SkillDB·ItemDatabase를 배선,
    ///      UIManager에 Onboarding 화면(OnboardingPanel) 등록.
    ///   1) 온보딩(OnboardingPanel/Layout181): Step1 타이틀 / Step2 10구단 타일 + 닉네임 / Step3 2024 골든글러브 4종 선물.
    ///   2) 로비(LobbyPanel/Layout181): 캡처 배경·거대 로고·더미 버튼을 쓰던 Layout178/Layout180 삭제 → 1080x1920 5단 네이티브 레이아웃
    ///      + 튜토리얼 오버레이(LobbyTutorial181).
    ///   3) 라인업(RosterPanel/Layout181): [타자 라인업] / [투수 로스터] 탭 + 3x3 주전 · 6열 후보 · 5열 선발 · 4x2 불펜 슬롯 칸 + 하단 세트덱 바.
    /// 좌표는 전부 1080x1920 캔버스(CanvasScaler 기준 해상도) 좌상단 원점 px다.
    /// </summary>
    public static class SetupTask181
    {
        internal const float W = 1080f;
        internal const float H = 1920f;

        internal static readonly Color Navy = new Color(0.05f, 0.07f, 0.13f);
        internal static readonly Color Card = new Color(0.1f, 0.13f, 0.24f, 0.96f);
        internal static readonly Color SectionBar = new Color(0.16f, 0.19f, 0.27f);
        internal static readonly Color White = new Color(0.97f, 0.98f, 1f);
        internal static readonly Color Muted = new Color(0.68f, 0.73f, 0.82f);
        internal static readonly Color Gold = new Color(1f, 0.82f, 0.27f);
        internal static readonly Color Accent = new Color(0.86f, 0.16f, 0.24f);
        internal static readonly Color Blue = new Color(0.15f, 0.36f, 0.86f);
        internal static readonly Color Gray = new Color(0.32f, 0.35f, 0.42f);
        internal static readonly Color PlayOrange = new Color(0.96f, 0.4f, 0.16f);

        internal static readonly Team[] Teams =
            { Team.Samsung, Team.KIA, Team.LG, Team.Doosan, Team.KT, Team.SSG, Team.Lotte, Team.Hanwha, Team.NC, Team.Kiwoom };

        [MenuItem("KBO Manager/Setup/Apply TASK-181 (Onboarding + Native Lobby + Lineup Tabs)")]
        public static void ApplyAll()
        {
            EnsureManagers();
            BuildOnboarding();
            BuildLobby();
            BuildRoster();
            Debug.Log("[SetupTask181] TASK-181 적용 완료(온보딩 3페이지 · 로비 5단 · 라인업 탭) - 씬을 저장(Ctrl+S)하십시오.");
        }

        // ================================================================== 0) 매니저 배선

        public static void EnsureManagers()
        {
            var gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gameManager == null)
            {
                Debug.LogWarning("[SetupTask181] GameManager가 없어 매니저 배선을 건너뜁니다 - 'Auto-Connect All Managers'를 먼저 실행하십시오.");
                return;
            }
            var host = gameManager.gameObject;
            var playerDatabase = Object.FindAnyObjectByType<PlayerDatabase>(FindObjectsInactive.Include);
            var itemDatabase = Object.FindAnyObjectByType<ItemDatabase>(FindObjectsInactive.Include);
            var rosterManager = Object.FindAnyObjectByType<RosterManager>(FindObjectsInactive.Include);
            var skillGuid = AssetDatabase.FindAssets("t:SkillDB").FirstOrDefault();
            var skillDB = skillGuid != null ? AssetDatabase.LoadAssetAtPath<SkillDB>(AssetDatabase.GUIDToAssetPath(skillGuid)) : null;

            var onboarding = Object.FindAnyObjectByType<OnboardingManager>(FindObjectsInactive.Include);
            if (onboarding == null) onboarding = Undo.AddComponent<OnboardingManager>(host);
            var so = new SerializedObject(onboarding);
            so.FindProperty("playerDatabase").objectReferenceValue = playerDatabase;
            so.FindProperty("rosterManager").objectReferenceValue = rosterManager;
            if (skillDB != null) so.FindProperty("skillDB").objectReferenceValue = skillDB;
            so.ApplyModifiedProperties();
            MarkDirty(onboarding);

            var save = Object.FindAnyObjectByType<SaveManager>(FindObjectsInactive.Include);
            if (save == null) save = Undo.AddComponent<SaveManager>(host);
            var sso = new SerializedObject(save);
            sso.FindProperty("playerDatabase").objectReferenceValue = playerDatabase;
            sso.FindProperty("itemDatabase").objectReferenceValue = itemDatabase;
            sso.ApplyModifiedProperties();
            MarkDirty(save);

            var ui = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            var onboardingPanel = Object.FindAnyObjectByType<OnboardingUIController>(FindObjectsInactive.Include);
            if (ui != null && onboardingPanel != null)
            {
                var uso = new SerializedObject(ui);
                var screens = uso.FindProperty("screens");
                bool found = false;
                for (int i = 0; i < screens.arraySize; i++)
                {
                    var entry = screens.GetArrayElementAtIndex(i);
                    if (entry.FindPropertyRelative("Type").enumValueIndex != (int)ScreenType.Onboarding) continue;
                    entry.FindPropertyRelative("Root").objectReferenceValue = onboardingPanel.gameObject;
                    found = true;
                }
                if (!found)
                {
                    screens.arraySize++;
                    var entry = screens.GetArrayElementAtIndex(screens.arraySize - 1);
                    entry.FindPropertyRelative("Type").enumValueIndex = (int)ScreenType.Onboarding;
                    entry.FindPropertyRelative("Root").objectReferenceValue = onboardingPanel.gameObject;
                }
                uso.FindProperty("startWithTitle").boolValue = true;
                uso.ApplyModifiedProperties();
                MarkDirty(ui);
            }
        }

        // ================================================================== 1) 온보딩

        public static void BuildOnboarding()
        {
            var controller = Object.FindAnyObjectByType<OnboardingUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupTask181] OnboardingUIController(OnboardingPanel)가 없어 온보딩 화면을 건너뜁니다.");
                return;
            }
            var panel = (RectTransform)controller.transform;
            StretchFull(panel);
            var root = NewRoot(panel, "Layout181");
            Box(root, "Background", 0, 0, W, H, Navy);
            Box(root, "TopStripe", 0, 0, W, 14, Accent);
            Box(root, "BottomStripe", 0, H - 14, W, H, Blue);

            // ---- Step 1 타이틀
            var title = Page(root, "TitlePage");
            Box(title, "Emblem", 390, 300, 690, 312, Gold);
            Label(title, "Title", "KBO 스토브리그", 40, 360, 1040, 520, 104, TextAnchor.MiddleCenter, Gold, true);
            Label(title, "Subtitle", ": 단장의 시간", 40, 520, 1040, 640, 66, TextAnchor.MiddleCenter, White, true);
            Box(title, "Emblem2", 390, 680, 690, 692, Gold);
            Label(title, "Tagline", "2026 시즌 · KBO 10개 구단 단장 시뮬레이션", 40, 720, 1040, 780, 32, TextAnchor.MiddleCenter, Muted);
            var status = Label(title, "StatusText", "", 60, 1060, 1020, 1140, 30, TextAnchor.MiddleCenter, Muted);
            var continueButton = Btn(title, "ContinueButton", "시즌 이어하기", 190, 1180, 890, 1310, Blue, White, 46);
            var newManagerButton = Btn(title, "NewManagerButton", "새 단장 부임", 190, 1340, 890, 1470, Gray, White, 42);
            var startButton = Btn(title, "StartButton", "단장 부임하기", 190, 1180, 890, 1330, Gold, Navy, 50);

            // ---- Step 2 구단 + 닉네임
            var teamPage = Page(root, "TeamPage");
            Label(teamPage, "Step", "STEP 2 / 3", 40, 50, 1040, 100, 28, TextAnchor.MiddleCenter, Gold, true);
            Label(teamPage, "Header", "맡을 구단을 선택하고 단장 닉네임을 정하십시오", 40, 100, 1040, 180, 40, TextAnchor.MiddleCenter, White, true);
            var tiles = new List<(Team team, Button button, Image logo, Text name, Image bg)>();
            for (int i = 0; i < Teams.Length; i++)
            {
                float x0 = i % 2 == 0 ? 40 : 550, x1 = x0 + 490;
                float y0 = 210 + (i / 2) * 166, y1 = y0 + 150;
                var tile = Btn(teamPage, $"Team_{Teams[i]}", "", x0, y0, x1, y1, new Color(0.12f, 0.15f, 0.25f, 0.95f), White, 10);
                var logo = Img(teamPage, $"Team_{Teams[i]}_Logo", x0 + 16, y0 + 15, x0 + 136, y1 - 15);
                var name = Label(teamPage, $"Team_{Teams[i]}_Name", CompyaUiKit.FullName(Teams[i]), x0 + 150, y0, x1 - 10, y1, 36, TextAnchor.MiddleLeft, White);
                tiles.Add((Teams[i], tile, logo, name, (Image)tile.targetGraphic));
            }
            Label(teamPage, "NicknameLabel", "단장 닉네임 (최대 12자)", 40, 1060, 1040, 1110, 30, TextAnchor.MiddleLeft, Muted, true);
            var input = Input(teamPage, "NicknameInput", 40, 1120, 1040, 1240, "닉네임을 입력하십시오");
            var teamHint = Label(teamPage, "Hint", "", 40, 1260, 1040, 1380, 30, TextAnchor.MiddleCenter, Gold);
            Label(teamPage, "Benefit", "가입 즉시: 선택 구단 2026 LIVE 선수단 전원 무료 지급 + 28인 라인업 자동 편성", 40, 1400, 1040, 1480, 28, TextAnchor.MiddleCenter, Muted);
            var teamBack = Btn(teamPage, "BackButton", "이전", 40, 1720, 360, 1850, Gray, White, 40);
            var teamNext = Btn(teamPage, "NextButton", "다음 - 정착 지원 선물", 380, 1720, 1040, 1850, Blue, White, 42);

            // ---- Step 3 정착 지원 선물(2024 골든글러브 4종 택1)
            var giftPage = Page(root, "GiftPage");
            Label(giftPage, "Step", "STEP 3 / 3", 40, 50, 1040, 100, 28, TextAnchor.MiddleCenter, Gold, true);
            Label(giftPage, "Header", "신규 단장 정착 지원 선물", 40, 100, 1040, 190, 54, TextAnchor.MiddleCenter, Gold, true);
            Label(giftPage, "Sub", "2024 골든글러브 특별 영입 - 4명 중 1명을 선택하십시오", 40, 190, 1040, 250, 32, TextAnchor.MiddleCenter, White);
            var gifts = new List<(Button button, RectTransform holder, Text caption, Image frame)>();
            for (int i = 0; i < OnboardingRules.GiftTemplateIds.Count; i++)
            {
                float x0 = 24 + i * 262, x1 = x0 + 246;
                var frame = Box(giftPage, $"Gift{i}_Frame", x0 - 6, 294, x1 + 6, 1006, new Color(1f, 1f, 1f, 0.08f));
                var slot = Btn(giftPage, $"Gift{i}", "", x0, 300, x1, 1000, Card, White, 10);
                var holder = Place(giftPage, $"Gift{i}_CardHolder", x0 + 8, 320, x1 - 8, 680);
                var caption = Label(giftPage, $"Gift{i}_Caption", "", x0 + 8, 700, x1 - 8, 980, 30, TextAnchor.UpperCenter, White);
                gifts.Add((slot, holder, caption, frame));
            }
            var giftHint = Label(giftPage, "Hint", "", 40, 1040, 1040, 1160, 30, TextAnchor.MiddleCenter, Gold);
            Label(giftPage, "Benefit", "선택한 카드는 인벤토리에 지급됩니다. 자동 편성은 선택 구단 선수를 우선하므로 같은 구단 카드는 즉시 주전, 타 구단 카드는 [보관 선수]에서 직접 투입할 수 있습니다.\n" +
                "골든글러브 카드는 구단과 무관하게 세트덱 스코어에 합산됩니다.", 40, 1180, 1040, 1340, 28, TextAnchor.MiddleCenter, Muted);
            var giftBack = Btn(giftPage, "BackButton", "이전", 40, 1720, 360, 1850, Gray, White, 40);
            var giftConfirm = Btn(giftPage, "ConfirmButton", "선물 수령 및 단장 취임", 380, 1720, 1040, 1850, Gold, Navy, 44);

            teamPage.gameObject.SetActive(false);
            giftPage.gameObject.SetActive(false);

            var so = new SerializedObject(controller);
            so.FindProperty("onboardingManager").objectReferenceValue = Object.FindAnyObjectByType<OnboardingManager>(FindObjectsInactive.Include);
            so.FindProperty("cardPrefab").objectReferenceValue = FindCardTemplate();
            so.FindProperty("titlePage").objectReferenceValue = title.gameObject;
            so.FindProperty("continueButton").objectReferenceValue = continueButton;
            so.FindProperty("newManagerButton").objectReferenceValue = newManagerButton;
            so.FindProperty("startButton").objectReferenceValue = startButton;
            so.FindProperty("titleStatusText").objectReferenceValue = status;
            so.FindProperty("teamPage").objectReferenceValue = teamPage.gameObject;
            so.FindProperty("nicknameInput").objectReferenceValue = input;
            so.FindProperty("teamNextButton").objectReferenceValue = teamNext;
            so.FindProperty("teamBackButton").objectReferenceValue = teamBack;
            so.FindProperty("teamHintText").objectReferenceValue = teamHint;
            so.FindProperty("giftPage").objectReferenceValue = giftPage.gameObject;
            so.FindProperty("giftConfirmButton").objectReferenceValue = giftConfirm;
            so.FindProperty("giftBackButton").objectReferenceValue = giftBack;
            so.FindProperty("giftHintText").objectReferenceValue = giftHint;

            var teamList = so.FindProperty("teamButtons");
            teamList.arraySize = tiles.Count;
            for (int i = 0; i < tiles.Count; i++)
            {
                var e = teamList.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Team").enumValueIndex = (int)tiles[i].team;
                e.FindPropertyRelative("Button").objectReferenceValue = tiles[i].button;
                e.FindPropertyRelative("LogoImage").objectReferenceValue = tiles[i].logo;
                e.FindPropertyRelative("TeamNameText").objectReferenceValue = tiles[i].name;
                e.FindPropertyRelative("Background").objectReferenceValue = tiles[i].bg;
            }
            var giftList = so.FindProperty("giftSlots");
            giftList.arraySize = gifts.Count;
            for (int i = 0; i < gifts.Count; i++)
            {
                var e = giftList.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("Button").objectReferenceValue = gifts[i].button;
                e.FindPropertyRelative("CardHolder").objectReferenceValue = gifts[i].holder;
                e.FindPropertyRelative("CaptionText").objectReferenceValue = gifts[i].caption;
                e.FindPropertyRelative("SelectionFrame").objectReferenceValue = gifts[i].frame;
            }
            so.ApplyModifiedProperties();
            MarkDirty(controller);
        }

        // ================================================================== 2) 로비 - 5단 네이티브

        public static void BuildLobby()
        {
            var dashboard = Object.FindAnyObjectByType<LeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dashboard == null)
            {
                Debug.LogWarning("[SetupTask181] LeagueDashboardUIController가 없어 로비를 건너뜁니다.");
                return;
            }
            var panel = (RectTransform)dashboard.transform;
            var so = new SerializedObject(dashboard);

            // 캡처 배경(메인 홈.jpg 1:1 - 휴대폰 시계/선수 캡처) · 거대 로고 · 더미 버튼을 담고 있던 TASK-178/180 레이아웃을 삭제한다.
            foreach (var stale in new[] { "Layout180", "Layout178" })
            {
                var t = panel.Find(stale);
                if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
            }
            var legacy = panel.Find("_Legacy178");
            if (legacy != null) legacy.gameObject.SetActive(false);
            if (panel.TryGetComponent<Image>(out var panelImage))
            {
                Undo.RecordObject(panelImage, "Lobby Background");
                panelImage.sprite = null;
                panelImage.color = Navy;
            }

            var root = NewRoot(panel, "Layout181");
            root.SetAsFirstSibling();
            Box(root, "Background", 0, 0, W, H, Navy);

            // ---- 1단 헤더
            var headerBg = Box(root, "HeaderBg", 0, 0, W, 176, Blue);
            Box(root, "LogoPlate", 22, 18, 162, 158, new Color(1f, 1f, 1f, 0.14f));
            var teamLogo = Img(root, "TeamLogo", 30, 26, 154, 150);
            // [TASK-KBO-191] 로비 글씨 계층(Normal) - 구단명 26 · 본문 16 · 보조 13~15 · 대형 숫자 32~40
            var teamName = Pt(Label(root, "TeamName", "", 182, 16, 780, 78, 46, TextAnchor.MiddleLeft, White, true), 26);
            var manager = Pt(Label(root, "ManagerName", "", 182, 78, 780, 120, 30, TextAnchor.MiddleLeft, new Color(0.9f, 0.93f, 1f)), 16);
            var headerStats = Pt(Label(root, "HeaderStats", "", 182, 120, 790, 164, 28, TextAnchor.MiddleLeft, White), 16);
            var change = Btn(root, "ChangeManagerButton", "구단/단장 변경", 800, 48, 1058, 128, new Color(0f, 0f, 0f, 0.35f), White, 28);
            TextTidy.ExactButton(change, 15, TextTidy.AutoMin, KBOFonts.Medium);

            // ---- 2단 재화
            var currencyNames = new[] { "포인트", "싸인볼", "트로피", "픽업권 / 티켓" };
            var currencyTexts = new Text[4];
            for (int i = 0; i < 4; i++)
            {
                float x0 = 22 + i * 261, x1 = x0 + 249;
                Box(root, $"Currency{i}", x0, 190, x1, 266, new Color(1f, 1f, 1f, 0.07f));
                // [TASK-KBO-191] 좌상단 라벨 13pt(#CBD5E1) / 우하단 숫자 18pt(#FFFFFF) - 상하 칸을 분리해 겹치지 않는다.
                Pt(Label(root, $"Currency{i}_Label", currencyNames[i], x0 + 14, 194, x1 - 10, 222, 22, TextAnchor.UpperLeft, CurrencyLabel), CurrencyLabelPt);
                currencyTexts[i] = Pt(Label(root, $"Currency{i}_Value", "0", x0 + 14, 230, x1 - 14, 262, 32, TextAnchor.LowerRight, Color.white, true), CurrencyValuePt);
            }

            // ---- 3단 대시보드: NEXT MATCH
            Box(root, "MatchCard", 22, 282, 1058, 664, Card);
            Box(root, "MatchAccent", 22, 282, 32, 664, Accent);
            Pt(Label(root, "MatchTitle", "<color=#E5303C>NEXT</color> MATCH", 50, 292, 620, 350, 44, TextAnchor.MiddleLeft, White, true), 26);
            var progress = Pt(Label(root, "SeasonProgress", "", 50, 352, 620, 392, 24, TextAnchor.MiddleLeft, Muted), 15);
            var myLogo = Img(root, "MyLogo", 70, 405, 220, 555);
            Pt(Label(root, "Versus", "VS", 220, 440, 330, 520, 50, TextAnchor.MiddleCenter, Gold, true), 32);
            var oppLogo = Img(root, "OpponentLogo", 330, 405, 480, 555);
            var myName = Pt(Label(root, "MyName", "", 40, 560, 250, 604, 28, TextAnchor.MiddleCenter, White, true), 17);
            var oppName = Pt(Label(root, "OpponentName", "", 300, 560, 510, 604, 28, TextAnchor.MiddleCenter, White, true), 17);
            var venue = Pt(Label(root, "Venue", "", 50, 606, 640, 660, 22, TextAnchor.MiddleLeft, Muted), VenuePt); // 경기장 / 예고 선발 2줄
            venue.lineSpacing = 1.05f;
            var play = Btn(root, "PlayBallButton", "", 650, 318, 1036, 640, PlayOrange, White, 10);
            Pt(Label(root, "PlayBallTitle", "플레이 볼", 670, 360, 1016, 470, 66, TextAnchor.MiddleCenter, White, true), 34);
            Pt(Label(root, "PlayBallSub", "PLAY BALL · 경기 시작", 670, 470, 1016, 520, 26, TextAnchor.MiddleCenter, new Color(1f, 0.92f, 0.85f)), 15);
            var ball = Img(root, "PlayBallIcon", 800, 530, 886, 616);
            ball.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            ball.color = White;
            ball.preserveAspect = true;

            // ---- 3단 대시보드: 대표 스타 카드
            Box(root, "StarCard", 22, 680, 532, 1300, Card);
            Pt(Label(root, "StarTitle", "내 구단 대표 스타", 42, 690, 512, 740, 30, TextAnchor.MiddleLeft, Gold, true), 19);
            var starHolder = Place(root, "StarCardHolder", 36, 750, 266, 1080);
            // [TASK-KBO-191] 우측 정보 = 칸 분리(이름 / 구단 · 포지션 · 등급 / OVR 라벨 / 큰 숫자 / SD) - 한 Text에 <size=150%>를 섞어 겹치던 문제
            var starName = Pt(Label(root, "StarName", "", 278, 756, 522, 806, 34, TextAnchor.MiddleLeft, White, true), 18);
            var starDetail = Pt(Label(root, "StarDetail", "", 278, 812, 522, 846, 26, TextAnchor.MiddleLeft, new Color(0.85f, 0.89f, 0.97f)), 15);
            Pt(Label(root, "StarOvrLabel", "OVR", 278, 856, 522, 880, 22, TextAnchor.MiddleLeft, Muted), 14);
            var starOvr = Pt(Label(root, "StarOvrValue", "", 278, 882, 522, 940, 60, TextAnchor.MiddleLeft, Gold, true), 40);
            var starSd = Pt(Label(root, "StarSd", "", 278, 948, 522, 980, 26, TextAnchor.MiddleLeft, new Color(0.37f, 0.89f, 1f)), 15);
            Box(root, "StarStatsBg", 36, 1096, 518, 1286, new Color(1f, 1f, 1f, 0.06f));
            var starStats = Pt(Label(root, "StarStats", "", 50, 1100, 506, 1282, 30, TextAnchor.MiddleCenter, White), 17);
            starStats.lineSpacing = 1.2f;

            // ---- 3단 대시보드: KBO 10구단 순위표
            Box(root, "StandingsCard", 548, 680, 1058, 1300, Card);
            Pt(Label(root, "StandingsTitle", "KBO 순위", 566, 690, 1040, 740, 30, TextAnchor.MiddleLeft, Gold, true), 19);
            float[] cols = { 556, 616, 746, 930, 1050 };
            string[] heads = { "순위", "구단", "승-무-패", "승률" };
            for (int c = 0; c < 4; c++) Pt(Label(root, $"StandingsHead{c}", heads[c], cols[c], 742, cols[c + 1], 784, 22, TextAnchor.MiddleCenter, Muted, true), 14);
            var rowBgs = new Image[10];
            var rank = new Text[10]; var teamCol = new Text[10]; var record = new Text[10]; var rate = new Text[10];
            for (int r = 0; r < 10; r++)
            {
                float y0 = 788 + r * 50.5f, y1 = y0 + 48;
                rowBgs[r] = Box(root, $"StandingRow{r + 1}", 556, y0, 1050, y1, new Color(1f, 1f, 1f, 0.05f));
                rank[r] = Pt(Label(root, $"StandingRank{r + 1}", "", cols[0], y0, cols[1], y1, 26, TextAnchor.MiddleCenter, White, true), 16);
                teamCol[r] = Pt(Label(root, $"StandingTeam{r + 1}", "", cols[1], y0, cols[2], y1, 26, TextAnchor.MiddleCenter, White), 16);
                record[r] = Pt(Label(root, $"StandingRecord{r + 1}", "", cols[2], y0, cols[3], y1, 24, TextAnchor.MiddleCenter, White), 15);
                rate[r] = Pt(Label(root, $"StandingRate{r + 1}", "", cols[3], y0, cols[4], y1, 24, TextAnchor.MiddleCenter, White), 15);
            }

            // ---- 4단 메뉴 타일
            var tileDefs = new (string title, string sub, ScreenType screen, Color color)[]
            {
                ("스카우트 센터", "선수 · 치어리더 영입", ScreenType.Scout, new Color(0.15f, 0.22f, 0.45f)),
                ("세트덱 & 버프 선택", "라인업 편성 · 구간 버프", ScreenType.Roster, new Color(0.3f, 0.2f, 0.7f)),
                ("선수단 강화", "강화 · 각성 · 스킬", ScreenType.Inventory, new Color(0.12f, 0.4f, 0.38f)),
                ("리그 기록실", "시즌 기록 · 명예의 전당", ScreenType.LeagueStats, new Color(0.4f, 0.24f, 0.16f)),
            };
            for (int i = 0; i < tileDefs.Length; i++)
            {
                float x0 = i % 2 == 0 ? 22 : 546, x1 = x0 + 512;
                float y0 = 1318 + (i / 2) * 136, y1 = y0 + 124;
                var tile = Btn(root, $"MenuTile{i}", "", x0, y0, x1, y1, tileDefs[i].color, White, 10);
                Box(root, $"MenuTile{i}_Accent", x0 + 16, y0 + 30, x0 + 24, y1 - 30, Gold);
                Pt(Label(root, $"MenuTile{i}_Title", tileDefs[i].title, x0 + 40, y0 + 10, x1 - 16, y0 + 74, 38, TextAnchor.MiddleLeft, White, true), 21);
                Pt(Label(root, $"MenuTile{i}_Sub", tileDefs[i].sub, x0 + 42, y0 + 74, x1 - 16, y1 - 10, 24, TextAnchor.MiddleLeft, new Color(0.85f, 0.88f, 0.95f)), 14);
                Relay(tile, tileDefs[i].screen);
            }

            // ---- 세트덱/치어리더/팬심 요약(기존 TeamSynergyArea) - 타일과 하단 탭 사이
            Box(root, "SynergyBg", 22, 1600, 1058, 1774, new Color(1f, 1f, 1f, 0.05f));
            if (so.FindProperty("synergyUIController").objectReferenceValue is Component synergy)
            {
                var top = synergy.transform;
                while (top != null && top.parent != panel) top = top.parent;
                if (top is RectTransform synergyRect)
                {
                    Undo.RecordObject(synergyRect, "Move Synergy");
                    SetBox(synergyRect, 36, 1606, 1044, 1768);
                    synergyRect.SetAsLastSibling();
                    // [TASK-KBO-191] 하단 요약(세트덱 / 치어리더 / 팬심) 안내 글씨 14pt Normal
                    foreach (var t in synergyRect.GetComponentsInChildren<Text>(true)) Pt(t, LogPt);
                }
            }

            // ---- 5단 하단 5탭(홈 전용)
            var nav = Box(root, "BottomNav", 0, 1790, W, H, new Color(0.13f, 0.3f, 0.66f));
            nav.raycastTarget = true;
            var navDefs = new (string label, string icon, string field)[]
            {
                ("선수 관리", "▣", "inventoryButton"), ("라인업", "◆", "rosterButton"), ("홈", "⬟", null),
                ("스카우트", "▲", "scoutButton"), ("응원단", "●", "manageCheerleaderButton"),
            };
            RectTransform lineupTab = null;
            for (int i = 0; i < navDefs.Length; i++)
            {
                bool home = navDefs[i].field == null;
                var cell = Btn(nav.transform, $"Nav{i}", "", i * 216f, 1790, (i + 1) * 216f, H, home ? new Color(0.07f, 0.16f, 0.4f) : new Color(0f, 0f, 0f, 0.001f), White, 10, true);
                var color = home ? White : new Color(0.75f, 0.84f, 0.98f);
                Pt(Label(cell.transform, "Icon", navDefs[i].icon, i * 216f, 1798, (i + 1) * 216f, 1860, 42, TextAnchor.MiddleCenter, color, true, true), 26);
                Pt(Label(cell.transform, "Label", navDefs[i].label, i * 216f, 1858, (i + 1) * 216f, 1908, 30, TextAnchor.MiddleCenter, color, home, true), 15);
                if (home)
                {
                    var bar = Box(cell.transform, "ActiveBar", i * 216f + 54, 1910, (i + 1) * 216f - 54, H, White);
                    Reanchor(bar.rectTransform, (RectTransform)cell.transform, i * 216f + 54, 1910, (i + 1) * 216f - 54, H);
                }
                else so.FindProperty(navDefs[i].field).objectReferenceValue = cell;
                if (navDefs[i].field == "rosterButton") lineupTab = (RectTransform)cell.transform;
            }
            if (!nav.TryGetComponent<LobbyOnlyNav>(out _)) Undo.AddComponent<LobbyOnlyNav>(nav.gameObject);

            // ---- 튜토리얼 오버레이(맨 위)
            var tutorial = BuildTutorial(panel, root, lineupTab, (RectTransform)headerStats.transform, (RectTransform)play.transform, play);

            // ---- 바인딩
            var cardTemplate = FindCardTemplate();
            var home181 = Undo.AddComponent<LobbyHome181>(root.gameObject);
            var hso = new SerializedObject(home181);
            hso.FindProperty("headerBackground").objectReferenceValue = headerBg;
            hso.FindProperty("teamLogo").objectReferenceValue = teamLogo;
            hso.FindProperty("teamNameText").objectReferenceValue = teamName;
            hso.FindProperty("managerText").objectReferenceValue = manager;
            hso.FindProperty("headerStatsText").objectReferenceValue = headerStats;
            hso.FindProperty("changeManagerButton").objectReferenceValue = change;
            hso.FindProperty("pointText").objectReferenceValue = currencyTexts[0];
            hso.FindProperty("signBallText").objectReferenceValue = currencyTexts[1];
            hso.FindProperty("trophyText").objectReferenceValue = currencyTexts[2];
            hso.FindProperty("ticketText").objectReferenceValue = currencyTexts[3];
            hso.FindProperty("myTeamLogo").objectReferenceValue = myLogo;
            hso.FindProperty("opponentLogo").objectReferenceValue = oppLogo;
            hso.FindProperty("myTeamText").objectReferenceValue = myName;
            hso.FindProperty("opponentText").objectReferenceValue = oppName;
            hso.FindProperty("venueText").objectReferenceValue = venue;
            hso.FindProperty("cardPrefab").objectReferenceValue = cardTemplate;
            hso.FindProperty("starCardHolder").objectReferenceValue = starHolder;
            hso.FindProperty("starNameText").objectReferenceValue = starName;
            hso.FindProperty("starDetailText").objectReferenceValue = starDetail;
            hso.FindProperty("starOvrText").objectReferenceValue = starOvr;
            hso.FindProperty("starSdText").objectReferenceValue = starSd;
            hso.FindProperty("starStatsText").objectReferenceValue = starStats;
            SetArray(hso.FindProperty("standingRowBackgrounds"), rowBgs);
            SetArray(hso.FindProperty("standingRankTexts"), rank);
            SetArray(hso.FindProperty("standingTeamTexts"), teamCol);
            SetArray(hso.FindProperty("standingRecordTexts"), record);
            SetArray(hso.FindProperty("standingRateTexts"), rate);
            hso.ApplyModifiedProperties();

            so.FindProperty("seasonProgressText").objectReferenceValue = progress;
            so.FindProperty("teamOVRText").objectReferenceValue = null;      // 헤더(LobbyHome181)가 팀 OVR · 세트덱을 함께 표시
            so.FindProperty("nextMatchupText").objectReferenceValue = null;  // 매치업 카드(로고 · 구장)가 대체
            so.FindProperty("standingsRowTexts").arraySize = 0;             // 순위표는 열 분리 표(LobbyHome181)
            so.FindProperty("ballText").objectReferenceValue = null;
            so.FindProperty("uniformText").objectReferenceValue = null;
            so.FindProperty("ticketText").objectReferenceValue = null;
            so.FindProperty("quickPlayButton").objectReferenceValue = play;
            so.FindProperty("shopButton").objectReferenceValue = null;
            so.FindProperty("leagueStatsButton").objectReferenceValue = null; // 4단 타일(LobbyButtonRelay)
            so.ApplyModifiedProperties();
            MarkDirty(dashboard);
            MarkDirty(tutorial);
        }

        internal static LobbyTutorial181 BuildTutorial(RectTransform panel, RectTransform root, RectTransform lineupTab, RectTransform setDeckTarget, RectTransform playTarget, Button play)
        {
            // 오버레이는 로비 패널 맨 위(세트덱 요약 TeamSynergyArea보다 위)에 둔다.
            foreach (var stale in panel.Cast<Transform>().Where(t => t.name == "Tutorial181").ToList()) Undo.DestroyObjectImmediate(stale.gameObject);
            var overlay = Box(panel, "Tutorial181", 0, 0, W, H, new Color(0f, 0f, 0f, 0.62f));
            Undo.RegisterCreatedObjectUndo(overlay.gameObject, "Create Tutorial181");
            overlay.raycastTarget = true;
            var highlight = Box(overlay.transform, "Highlight", 0, 0, 100, 100, new Color(1f, 0.84f, 0.27f, 0.12f));
            var outline = highlight.gameObject.AddComponent<Outline>();
            outline.effectColor = Gold;
            outline.effectDistance = new Vector2(5f, -5f);

            var message = Box(overlay.transform, "Message", 40, 300, 1040, 840, new Color(0.08f, 0.1f, 0.2f, 0.98f));
            message.raycastTarget = true;
            var m = (RectTransform)message.transform;
            Box(m, "Accent", 0, 0, W, 14, Gold).rectTransform.anchorMin = new Vector2(0f, 0.97f);
            var step = NormLabel(m, "Step", 0.05f, 0.84f, 0.95f, 0.96f, 26, TextAnchor.MiddleLeft, Gold, true);
            var title = NormLabel(m, "Title", 0.05f, 0.68f, 0.95f, 0.84f, 42, TextAnchor.MiddleLeft, White, true);
            var body = NormLabel(m, "Body", 0.05f, 0.24f, 0.95f, 0.68f, 30, TextAnchor.UpperLeft, new Color(0.88f, 0.91f, 0.98f), false);
            var skip = NormButton(m, "SkipButton", "건너뛰기", 0.05f, 0.04f, 0.36f, 0.2f, Gray, White, 32);
            var next = NormButton(m, "NextButton", "다음", 0.4f, 0.04f, 0.95f, 0.2f, Blue, White, 36);

            var tutorial = Undo.AddComponent<LobbyTutorial181>(root.gameObject);
            var so = new SerializedObject(tutorial);
            so.FindProperty("overlayRoot").objectReferenceValue = overlay.gameObject;
            so.FindProperty("highlightFrame").objectReferenceValue = highlight.rectTransform;
            so.FindProperty("messagePanel").objectReferenceValue = m;
            so.FindProperty("stepText").objectReferenceValue = step;
            so.FindProperty("titleText").objectReferenceValue = title;
            so.FindProperty("bodyText").objectReferenceValue = body;
            so.FindProperty("nextButton").objectReferenceValue = next;
            so.FindProperty("skipButton").objectReferenceValue = skip;
            so.FindProperty("playBallButton").objectReferenceValue = play;
            SetArray(so.FindProperty("targets"), new Object[] { lineupTab, setDeckTarget, playTarget });
            so.ApplyModifiedProperties();
            overlay.transform.SetAsLastSibling();
            overlay.gameObject.SetActive(false);
            return tutorial;
        }

        // ================================================================== 3) 라인업 - [타자 라인업] / [투수 로스터] 탭

        public static void BuildRoster()
        {
            var controller = Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            if (controller == null)
            {
                Debug.LogWarning("[SetupTask181] RosterUIController가 없어 라인업 화면을 건너뜁니다.");
                return;
            }
            var panel = (RectTransform)controller.transform;

            // 28칸을 한 화면에 몰아넣던 TASK-177/178 구역은 끈다(삭제하지 않음 - 구 메뉴 재실행 호환).
            foreach (var name in new[] { "BatterContainer", "BenchContainer", "PitcherContainer", "StarterHeaderText", "StarterHeaderTextBG",
                         "BenchHeaderText", "BenchHeaderTextBG", "PitcherHeaderText", "PitcherHeaderTextBG", "SetDeckBar178", "EmptyStateText" })
            {
                var t = panel.Find(name);
                if (t != null) { Undo.RecordObject(t.gameObject, "Hide Roster177"); t.gameObject.SetActive(false); }
            }

            // 재실행 시 이전 Layout181로 옮겨 둔 기존 오브젝트(게이지/상태 문구/버튼)를 패널로 되돌린 뒤 지운다.
            if (panel.Find("Layout181") is Transform previous)
            {
                foreach (var keep in new[] { "SetDeckGaugeFill", "SetDeckStatusText", "SetDeckActiveGlow", "SetDeckOptionButton", "AutoLineupButton" })
                {
                    if (previous.Find(keep) is Transform moved) Undo.SetTransformParent(moved, panel, "Restore " + keep);
                }
            }
            var root = NewRoot(panel, "Layout181");
            var header = panel.Find("Header178");
            root.SetSiblingIndex(header != null ? header.GetSiblingIndex() + 1 : 0);
            Box(root, "Background", 0, 115, W, H, new Color(0.88f, 0.9f, 0.94f));

            // 탭
            var batterTab = Btn(root, "BatterTab", "타자 라인업 (15인)", 0, 118, 540, 214, new Color(0.09f, 0.19f, 0.47f), White, 38);
            var pitcherTab = Btn(root, "PitcherTab", "투수 로스터 (13인)", 540, 118, W, 214, new Color(0.93f, 0.94f, 0.97f), Muted, 38);
            Box(root, "TabUnderline", 0, 214, W, 220, new Color(0.25f, 0.6f, 1f));

            // 타자 페이지
            var batterPage = Page(root, "BatterPage");
            var lineupHeader = Section(batterPage, "LineupHeader", 228, 276);
            var lineupGrid = Grid(batterPage, "LineupGrid", 280, 1182, 3, 3);
            var benchHeader = Section(batterPage, "BenchHeader", 1190, 1238);
            var benchGrid = Grid(batterPage, "BenchGrid", 1242, 1612, 6, 1);

            // 투수 페이지
            var pitcherPage = Page(root, "PitcherPage");
            var spHeader = Section(pitcherPage, "StartingPitcherHeader", 228, 276);
            var spGrid = Grid(pitcherPage, "StartingPitcherGrid", 280, 740, 5, 1);
            var bullpenHeader = Section(pitcherPage, "BullpenHeader", 750, 798);
            var bullpenGrid = Grid(pitcherPage, "BullpenGrid", 802, 1612, 4, 2);
            pitcherPage.gameObject.SetActive(false);

            var empty = Label(root, "EmptyStateText181", "", 24, 1618, 1056, 1662, 22, TextAnchor.MiddleCenter, new Color(0.3f, 0.33f, 0.4f));

            // 하단 고정 세트덱 바
            Box(root, "SetDeckBar", 0, 1666, W, H, new Color(0.06f, 0.07f, 0.11f, 0.97f));
            var logo = Img(root, "SetDeckTeamLogo", 22, 1676, 92, 1746);
            var teamText = Label(root, "SetDeckTeamText", "", 104, 1676, 470, 1746, 28, TextAnchor.MiddleLeft, White, true);
            var scoreText = Label(root, "SetDeckScoreText", "", 470, 1672, 790, 1750, 44, TextAnchor.MiddleCenter, White, true);
            var ovrText = Label(root, "TeamOvrText", "", 790, 1676, 1058, 1746, 28, TextAnchor.MiddleRight, Gold, true);
            Box(root, "GaugeTrack", 22, 1752, 1058, 1772, new Color(1f, 1f, 1f, 0.12f));
            if (panel.Find("SetDeckGaugeFill") is RectTransform gauge) { MoveTo(gauge, root, 22, 1752, 1058, 1772); }
            if (panel.Find("SetDeckStatusText") is RectTransform status)
            {
                MoveTo(status, root, 22, 1776, 1058, 1822);
                if (status.TryGetComponent<Text>(out var statusText))
                {
                    Undo.RecordObject(statusText, "Status Text");
                    statusText.fontSize = 22; statusText.resizeTextForBestFit = true; statusText.resizeTextMinSize = 14; statusText.resizeTextMaxSize = 22;
                    statusText.alignment = TextAnchor.MiddleCenter;
                    foreach (var effect in statusText.GetComponents<Outline>()) Undo.DestroyObjectImmediate(effect);
                }
            }
            if (panel.Find("SetDeckActiveGlow") is RectTransform glow) MoveTo(glow, root, 0, 1666, W, 1776);
            if (panel.Find("SetDeckOptionButton") is RectTransform option) { MoveTo(option, root, 22, 1830, 532, 1906); Restyle(option, "세트덱 버프 선택", new Color(0.3f, 0.2f, 0.7f)); }
            if (panel.Find("AutoLineupButton") is RectTransform auto) { MoveTo(auto, root, 548, 1830, 1058, 1906); Restyle(auto, "자동 편성", Blue); }

            // 팝업/닫기는 맨 위
            foreach (var top in new[] { "CloseButton", "SwapPopup", "SetDeckOptionPanel" }) panel.Find(top)?.SetAsLastSibling();

            var so = new SerializedObject(controller);
            so.FindProperty("useSlotFrames").boolValue = true;
            so.FindProperty("batterTabButton").objectReferenceValue = batterTab;
            so.FindProperty("pitcherTabButton").objectReferenceValue = pitcherTab;
            so.FindProperty("batterPage").objectReferenceValue = batterPage.gameObject;
            so.FindProperty("pitcherPage").objectReferenceValue = pitcherPage.gameObject;
            so.FindProperty("batterContainer").objectReferenceValue = lineupGrid;
            so.FindProperty("benchContainer").objectReferenceValue = benchGrid;
            so.FindProperty("pitcherContainer").objectReferenceValue = spGrid;
            so.FindProperty("startingPitcherContainer").objectReferenceValue = spGrid;
            so.FindProperty("bullpenContainer").objectReferenceValue = bullpenGrid;
            so.FindProperty("lineupHeaderText").objectReferenceValue = lineupHeader;
            so.FindProperty("benchHeaderText").objectReferenceValue = benchHeader;
            so.FindProperty("startingPitcherHeaderText").objectReferenceValue = spHeader;
            so.FindProperty("bullpenHeaderText").objectReferenceValue = bullpenHeader;
            so.FindProperty("slotFont").objectReferenceValue = KBOFonts.Medium;
            so.FindProperty("slotBoldFont").objectReferenceValue = KBOFonts.Bold;
            so.FindProperty("setDeckTeamLogo").objectReferenceValue = logo;
            so.FindProperty("setDeckTeamText").objectReferenceValue = teamText;
            so.FindProperty("setDeckScoreText").objectReferenceValue = scoreText;
            so.FindProperty("teamOvrText").objectReferenceValue = ovrText;
            so.FindProperty("emptyStateText").objectReferenceValue = empty;
            so.ApplyModifiedProperties();
            MarkDirty(controller);
        }

        internal static Text Section(RectTransform parent, string name, float y0, float y1)
        {
            Box(parent, name + "Bg", 12, y0, W - 12, y1, SectionBar);
            return Label(parent, name, "", 30, y0, W - 30, y1, 26, TextAnchor.MiddleLeft, White, true);
        }

        internal static RectTransform Grid(RectTransform parent, string name, float y0, float y1, int columns, int rows)
        {
            var rect = Place(parent, name, 12, y0, W - 12, y1);
            var grid = rect.gameObject.AddComponent<GridLayoutGroup>();
            grid.spacing = new Vector2(8f, 8f);
            grid.padding = new RectOffset(0, 0, 4, 0);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            rect.gameObject.AddComponent<AdaptiveGridCells>().Configure(columns, rows);
            return rect;
        }

        internal static void Restyle(RectTransform button, string label, Color color)
        {
            if (button.TryGetComponent<Image>(out var image)) { Undo.RecordObject(image, "Restyle"); image.color = color; }
            foreach (var text in button.GetComponentsInChildren<Text>(true))
            {
                Undo.RecordObject(text, "Restyle");
                text.text = label;
                text.color = White;
                text.font = KBOFonts.Bold;
                text.fontSize = 32; text.resizeTextForBestFit = true; text.resizeTextMinSize = 18; text.resizeTextMaxSize = 32;
                foreach (var effect in text.GetComponents<Outline>()) Undo.DestroyObjectImmediate(effect);
            }
        }

        internal static void MoveTo(RectTransform rect, RectTransform parent, float x0, float y0, float x1, float y1)
        {
            Undo.SetTransformParent(rect, parent, "TASK-181 Move");
            Undo.RecordObject(rect, "TASK-181 Move");
            SetBox(rect, x0, y0, x1, y1);
            rect.SetAsLastSibling();
            rect.gameObject.SetActive(rect.name != "SetDeckActiveGlow");
        }

        // ================================================================== 공용 빌더(1080x1920 px, 좌상단 원점)

        internal static PlayerCardUI FindCardTemplate()
        {
            var roster = Object.FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
            if (roster != null && new SerializedObject(roster).FindProperty("cardPrefab").objectReferenceValue is PlayerCardUI fromRoster) return fromRoster;
            var canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
            var template = canvas != null ? canvas.transform.Find("_Templates/PlayerCardTemplate") : null;
            return template != null ? template.GetComponent<PlayerCardUI>() : null;
        }

        internal static RectTransform NewRoot(RectTransform panel, string name)
        {
            foreach (var stale in panel.Cast<Transform>().Where(t => t.name == name).ToList()) Undo.DestroyObjectImmediate(stale.gameObject);
            var go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            var root = (RectTransform)go.transform;
            root.SetParent(panel, false);
            StretchFull(root);
            return root;
        }

        internal static RectTransform Page(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            StretchFull(rect);
            return rect;
        }

        internal static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }

        /// <summary>px 박스 → 부모 기준 앵커(부모가 전체 화면 크기라고 가정 - 이 스크립트의 모든 부모는 전체 화면 페이지다).</summary>
        internal static void SetBox(RectTransform rect, float x0, float y0, float x1, float y1)
        {
            rect.anchorMin = new Vector2(x0 / W, 1f - y1 / H);
            rect.anchorMax = new Vector2(x1 / W, 1f - y0 / H);
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        internal static RectTransform Place(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            SetBox(rect, x0, y0, x1, y1);
            return rect;
        }

        internal static Image Box(Transform parent, string name, float x0, float y0, float x1, float y1, Color color, bool raycast = false)
        {
            var image = Place(parent, name, x0, y0, x1, y1).gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        internal static Image Img(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var image = Box(parent, name, x0, y0, x1, y1, new Color(1f, 1f, 1f, 0f));
            image.preserveAspect = true;
            return image;
        }

        // [TASK-KBO-191] 로비 재화 바 · NEXT MATCH · 하단 요약 글씨 크기
        public const int CurrencyLabelPt = 13, CurrencyValuePt = 18, VenuePt = 14, LogPt = 14;
        internal static readonly Color CurrencyLabel = new Color(0xCB / 255f, 0xD5 / 255f, 0xE1 / 255f); // #CBD5E1

        /// <summary>[TASK-KBO-191] 크기 고정(Normal · 18pt 이하는 Medium 서체 · 자간) - 정리 패스가 다시 줄이지 않는다.</summary>
        internal static Text Pt(Text label, int size) => TextTidy.Exact(label, size, TextTidy.AutoMin, KBOFonts.Medium);

        internal static Text Label(Transform parent, string name, string text, float x0, float y0, float x1, float y1,
            int size, TextAnchor anchor, Color color, bool bold = false, bool absoluteInNav = false)
        {
            var rect = Place(parent, name, x0, y0, x1, y1);
            if (absoluteInNav) Reanchor(rect, (RectTransform)parent, x0, y0, x1, y1);
            return Style(rect.gameObject.AddComponent<Text>(), text, size, anchor, color, bold);
        }

        /// <summary>하단 탭 셀(부모가 전체 화면이 아님) 안 자식 - 셀 좌표 기준 정규화로 다시 건다.</summary>
        internal static void Reanchor(RectTransform rect, RectTransform parent, float x0, float y0, float x1, float y1)
        {
            var p = PixelBox(parent);
            rect.anchorMin = new Vector2((x0 - p.xMin) / p.width, 1f - (y1 - p.yMin) / p.height);
            rect.anchorMax = new Vector2((x1 - p.xMin) / p.width, 1f - (y0 - p.yMin) / p.height);
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
        }

        /// <summary>RectTransform의 px 박스(1080x1920 좌상단 원점) - 부모 체인의 앵커를 곱해 역산한다.</summary>
        internal static Rect PixelBox(RectTransform rect)
        {
            float xMin = 0f, xMax = 1f, yMin = 0f, yMax = 1f; // 정규화(좌하단 원점)
            for (var t = rect; t != null && t.GetComponent<Canvas>() == null; t = t.parent as RectTransform)
            {
                xMin = t.anchorMin.x + (t.anchorMax.x - t.anchorMin.x) * xMin;
                xMax = t.anchorMin.x + (t.anchorMax.x - t.anchorMin.x) * xMax;
                yMin = t.anchorMin.y + (t.anchorMax.y - t.anchorMin.y) * yMin;
                yMax = t.anchorMin.y + (t.anchorMax.y - t.anchorMin.y) * yMax;
            }
            return Rect.MinMaxRect(xMin * W, (1f - yMax) * H, xMax * W, (1f - yMin) * H);
        }

        internal static Text Style(Text label, string text, int size, TextAnchor anchor, Color color, bool bold)
        {
            label.font = bold ? KBOFonts.Bold : KBOFonts.Medium;
            label.text = text;
            label.fontSize = size;
            label.alignment = anchor;
            label.color = color;
            label.raycastTarget = false;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.resizeTextForBestFit = true;
            label.resizeTextMaxSize = size;
            label.resizeTextMinSize = Mathf.Max(12, Mathf.RoundToInt(size * 0.6f));
            return label;
        }

        internal static Button Btn(Transform parent, string name, string text, float x0, float y0, float x1, float y1,
            Color background, Color textColor, int size, bool absoluteInNav = false)
        {
            var rect = Place(parent, name, x0, y0, x1, y1);
            if (absoluteInNav) Reanchor(rect, (RectTransform)parent, x0, y0, x1, y1);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = background;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.85f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.6f, 0.6f);
            button.colors = colors;
            var labelRect = new GameObject("Text", typeof(RectTransform));
            labelRect.transform.SetParent(rect, false);
            StretchFull((RectTransform)labelRect.transform);
            Style(labelRect.AddComponent<Text>(), text, size, TextAnchor.MiddleCenter, textColor, true);
            return button;
        }

        internal static Text NormLabel(RectTransform parent, string name, float x0, float y0, float x1, float y1, int size, TextAnchor anchor, Color color, bool bold)
        {
            var rect = CompyaUiKit.Norm(parent, name, x0, y0, x1, y1);
            return Style(rect.gameObject.AddComponent<Text>(), "", size, anchor, color, bold);
        }

        internal static Button NormButton(RectTransform parent, string name, string text, float x0, float y0, float x1, float y1, Color bg, Color fg, int size)
        {
            var rect = CompyaUiKit.Norm(parent, name, x0, y0, x1, y1);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = bg;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var label = new GameObject("Text", typeof(RectTransform));
            label.transform.SetParent(rect, false);
            StretchFull((RectTransform)label.transform);
            Style(label.AddComponent<Text>(), text, size, TextAnchor.MiddleCenter, fg, true);
            return button;
        }

        internal static InputField Input(Transform parent, string name, float x0, float y0, float x1, float y1, string placeholder)
        {
            var rect = Place(parent, name, x0, y0, x1, y1);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.97f, 0.98f, 1f);
            var field = rect.gameObject.AddComponent<InputField>();
            field.targetGraphic = image;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(rect, false);
            var textRect = (RectTransform)textGo.transform;
            StretchFull(textRect);
            textRect.offsetMin = new Vector2(24f, 8f); textRect.offsetMax = new Vector2(-24f, -8f);
            var text = Style(textGo.AddComponent<Text>(), "", 40, TextAnchor.MiddleLeft, Navy, true);
            text.supportRichText = false;
            text.resizeTextForBestFit = false;

            var phGo = new GameObject("Placeholder", typeof(RectTransform));
            phGo.transform.SetParent(rect, false);
            var phRect = (RectTransform)phGo.transform;
            StretchFull(phRect);
            phRect.offsetMin = new Vector2(24f, 8f); phRect.offsetMax = new Vector2(-24f, -8f);
            var ph = Style(phGo.AddComponent<Text>(), placeholder, 36, TextAnchor.MiddleLeft, new Color(0.5f, 0.53f, 0.6f), false);
            ph.fontStyle = FontStyle.Italic;

            field.textComponent = text;
            field.placeholder = ph;
            field.characterLimit = OnboardingRules.NicknameMaxLength;
            field.lineType = InputField.LineType.SingleLine;
            return field;
        }

        internal static void SetArray(SerializedProperty property, IReadOnlyList<Object> values)
        {
            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        internal static void Relay(Button button, ScreenType screen)
        {
            var relay = button.GetComponent<LobbyButtonRelay>();
            if (relay == null) relay = Undo.AddComponent<LobbyButtonRelay>(button.gameObject);
            relay.Configure(screen);
            EditorUtility.SetDirty(relay);
        }

        internal static void MarkDirty(Component component)
        {
            if (component == null) return;
            EditorUtility.SetDirty(component);
            var scene = component.gameObject.scene;
            if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);
        }
    }
}
