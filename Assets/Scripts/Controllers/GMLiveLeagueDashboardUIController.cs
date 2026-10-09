using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-GM-02] 리그 플레이 실시간 144경기 대시보드(기획서 2절 · 2.1). [TASK-GM-06] 1920×1080 Landscape 재배치(좌 순위표 · 우 TOP 3 · 하단 소식),
    /// 새 시즌 설정에 4단계 난이도 · 하우스 룰. 로비 [플레이 볼] · 프런트 오피스 허브가 연다.
    ///   - 배경: 선택 구단 대표색(TeamThemePalette) + 중앙 구단 마크(Pulse Scale · Shimmer Alpha · 미세 플로팅)
    ///   - 헤더: KBO 시즌 N([TASK-GM-14]) · G 000 / 144 · [한 경기] [전반기 진행 → 후반기 진행] [한 시즌] · 1x/2x/4x · 일시정지
    ///   - 패널 1 순위표(순위 · 구단 · G · W · D · L · PCT · GB, 내 구단 강조, 5위 아래 포스트시즌 커트라인)
    ///   - 패널 2 개인 성적 TOP 3(타자 8 · 투수 7 탭, 2열 카드)
    ///   - 패널 3 최신 소식(날짜 · 제목, 누르면 본문 팝업)
    ///   - 인터럽트 팝업: 대기록 [확인 후 계속 진행] / 부상 [대체 선수 자동 콜업 후 계속] · [라인업/엔트리 직접 관리]
    /// 1x 기준 경기일 1.75초 틱(144경기 ≈ 4.2분), 2x/4x 배속. 모든 글씨 Normal · 15pt 이상.
    /// [TASK-GM-04] 헤더 [시상 리포트](월간 · 올스타 · 시상식), 144경기 종료 시 진행 버튼 자리에 [포스트시즌 & 시상식 보기] · [20yy 시즌 전환],
    /// 올스타전 팝업의 [올스타전 결과 & 시상 리포트].
    /// </summary>
    public class GMLiveLeagueDashboardUIController : MonoBehaviour
    {
        public const string RootName = "GMLiveRoot";
        public const float TickSeconds = 1.75f;
        public const int TitlePt = 26, CounterPt = 26, ButtonPt = 21, SectionPt = 22, RowPt = 19, HeadPt = 17, LeaderTitlePt = 19, LeaderBodyPt = 17, NewsPt = 17, PopupTitlePt = 24, PopupBodyPt = 20;
        public const int NewsRows = 6, LeaderCells = 8, StandingRows = 10;

        private static readonly Color White = new Color(0.96f, 0.97f, 0.99f);
        private static readonly Color Muted = new Color(0.78f, 0.83f, 0.92f);
        private static readonly Color Gold = new Color(1f, 0.84f, 0.3f);
        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.38f);
        private static readonly Color ButtonIdle = new Color(1f, 1f, 1f, 0.12f);

        [SerializeField] private Font regularFont;

        private RectTransform root;
        private Image background, headerBar;
        // [TASK-GM-15] 스토브리그 8 Turn 진행 중 고속 진행 버튼 잠금
        public const string StoveLockName = "StoveLockBar";
        private RectTransform stoveLock;
        private Text stoveLockText;
        private RawImage emblem;
        private RectTransform emblemHolder;
        private Text seasonTitle, gameCounter, statusText, lastGameText;
        private Button modeSingle, modeWeek, modeHalf, modeFull, speed1, speed2, speed4, pauseButton, closeButton, tabBatter, tabPitcher;
        // [TASK-GM-03] 새 시즌 설정 · 직전 경기 결과
        private Button newSeasonButton, lastBoxButton;
        // [TASK-GM-04] 시상 리포트 · 포스트시즌 & 시상식 · 연도 전환
        private Button awardsReportButton, awardsButton, nextSeasonButton;
        private bool popupOpensAwards, pausedForAwards;
        // [TASK-GM-05] 치어리더 엔트리 바로가기
        private Button cheerEntryButton;
        private bool pausedForCheer;

        /// <summary>[TASK-GM-05] 치어리더 엔트리 화면(비우면 씬에서 찾는다 - 테스트가 주입한다).</summary>
        public GMCheerleaderEntryUIController CheerView { get; set; }

        /// <summary>[TASK-GM-04] 시상식 화면(비우면 씬에서 찾는다 - 테스트가 주입한다).</summary>
        public GMAwardsCeremonyUIController AwardsView { get; set; }
        private RectTransform seasonModal;
        private Text seasonModalDesc;
        private readonly Button[] modeButtons = new Button[3];
        private readonly Button[] teamButtons = new Button[10];
        private Button virtualToggle, seasonStart, seasonCancel;
        private GMStartMode pendingMode = GMStartMode.RealCurrent2026;
        private string pendingTeam = NameAliasTable.SAM;
        private bool pendingVirtual;

        /// <summary>[TASK-GM-03] 새 시즌 리그 생성기(모드 · 구단 코드 · 가상명). 비우면 GameManager.StartGMLeague(없으면 GMRosterLoader)를 쓴다 - 테스트가 주입한다.</summary>
        public Func<GMStartMode, string, bool, GMLeagueState> LeagueFactory { get; set; }
        public bool IsSeasonModalOpen => seasonModal != null && seasonModal.gameObject.activeSelf;
        private readonly Image[] rowBgs = new Image[StandingRows];
        private readonly Text[,] cells = new Text[StandingRows, 8];
        private readonly RawImage[] rowLogos = new RawImage[StandingRows];
        private readonly Image[] leaderBgs = new Image[LeaderCells];
        private readonly Text[] leaderTitles = new Text[LeaderCells];
        private readonly Text[] leaderBodies = new Text[LeaderCells];
        private readonly Button[] newsButtons = new Button[NewsRows];
        private readonly Text[] newsTexts = new Text[NewsRows];
        private RectTransform popup;
        private Text popupTitle, popupBody;
        private Button popupPrimary, popupSecondary;
        private readonly Button[] candidateButtons = new Button[4]; // [TASK-GM-18] 단장 개입 선택지 최대 4개(부상 직접 관리는 3개)
        // [TASK-GM-18] 단장 개입 사건 팝업 상태 · 결과 확인 · 커리어 엔딩
        private GMSeasonEvent viewingEvent;
        private bool showingEventResult, showingCareerEnding;
        private Text popupTag;
        private List<string> pendingNewGamePlus;
        public const string EventTagName = "PopupEventTag";
        public GMSeasonEvent ViewingEvent => viewingEvent;
        public bool IsShowingEventResult => showingEventResult;
        public bool IsShowingCareerEnding => showingCareerEnding;
        public string PopupTitleText => popupTitle != null ? popupTitle.text : "";
        public string PopupBodyText => popupBody != null ? popupBody.text : "";
        public RectTransform PopupRoot => popup;

        private GMLiveSeasonSimulator simulator;
        private bool showPitchers;
        private bool paused;
        private int speed = 1;
        private float tickTimer;
        private GMNewsItem viewingNews;
        private bool manualSelecting;
        private Team themeTeam = Team.None;
        private List<GMNewsItem> shownNews = new List<GMNewsItem>();

        public RectTransform Root => root;
        public GMLiveSeasonSimulator Simulator => simulator;
        public int Speed => speed;
        public bool IsPaused => paused;
        public bool ShowingPitchers => showPitchers;
        public Team ThemeTeam => themeTeam;
        public bool IsPopupOpen => popup != null && popup.gameObject.activeSelf;
        public string HalfButtonText => modeHalf != null ? modeHalf.GetComponentInChildren<Text>(true).text : null;

        public void Configure(Font regular) => regularFont = regular;

        private void Awake()
        {
            if (root == null) Build();
        }

        /// <summary>로비 [플레이 볼] - 단장 모드 리그(없으면 기본 시작)를 붙이고 대시보드를 띄운다.</summary>
        public static GMLiveLeagueDashboardUIController OpenFromLobby()
        {
            var view = FindAnyObjectByType<GMLiveLeagueDashboardUIController>(FindObjectsInactive.Include);
            if (view == null) return null;
            var gm = GameManager.Instance;
            if (gm != null) gm.EnsureGMLeague();
            view.Open(gm != null ? gm.GMSimulator : null);
            return view;
        }

        public void Open(GMLiveSeasonSimulator sim)
        {
            if (root == null) Build();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Bind(sim);
        }

        /// <summary>진행기를 연결하고 구단 테마를 적용한다(테스트에서도 직접 호출).</summary>
        public void Bind(GMLiveSeasonSimulator sim)
        {
            if (root == null) Build();
            simulator = sim;
            var user = sim?.League?.UserTeam;
            ApplyTheme(user != null ? user.Team : Team.Samsung);
            paused = false;
            HidePopup();
            Refresh();
            if (sim?.PendingInterrupt != null) ShowInterrupt(sim.PendingInterrupt); // [TASK-GM-18] 허브에서 넘어와도 대기 중인 단장 개입 사건을 바로 띄운다
        }

        public void Close()
        {
            if (simulator != null) simulator.Stop();
            HidePopup();
            gameObject.SetActive(false);
            UIManager.Instance?.ShowScreen(ScreenType.Lobby);
        }

        // ================================================================== 조립

        public void Build()
        {
            var old = transform.Find(RootName);
            if (old != null)
            {
                old.name = "_" + RootName + "_old";
                old.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            }
            var font = regularFont != null ? regularFont : TextTidy.BodyFont;
            var kit = new CompyaUiKit(font, font);
            using (CompyaUiKit.Wide()) // [TASK-GM-06] 1920×1080 Landscape 좌표
            {
            root = CompyaUiKit.Fill(transform, RootName);
            background = CompyaUiKit.Paint(root, TeamThemePalette.Background(Team.Samsung), true);

            // 중앙 구단 마크(은은한 반짝임)
            emblem = CompyaUiKit.Logo(root, "Emblem", 660, 240, 1260, 840);
            emblemHolder = (RectTransform)emblem.transform.parent;

            // ---- 헤더
            headerBar = CompyaUiKit.Box(root, "HeaderBar", 0, 0, 1920, 150, TeamThemePalette.Primary(Team.Samsung));
            seasonTitle = L(kit, "SeasonTitle", GMLeagueState.SeasonTitle(1), 20, 10, 330, 70, TitlePt, TextAnchor.MiddleLeft, White);
            awardsReportButton = Btn(kit, "AwardsReportButton", "시상 리포트", 340, 10, 560, 70, ButtonIdle, ButtonPt - 2);
            awardsReportButton.onClick.AddListener(() => { if (simulator != null && simulator.IsSeasonComplete) GoPostseasonOrAwards(); else OpenAwards(null); }); // [TASK-GM-17] 시즌 종료 후엔 포스트시즌부터
            gameCounter = L(kit, "GameCounter", "G 000 / 144", 570, 10, 860, 70, CounterPt, TextAnchor.MiddleRight, Gold);
            newSeasonButton = Btn(kit, "NewSeasonButton", "새 시즌 설정", 870, 10, 1110, 70, ButtonIdle, ButtonPt - 2);
            newSeasonButton.onClick.AddListener(OpenSeasonModal);
            statusText = L(kit, "StatusText", "대기 중", 1120, 10, 1790, 70, HeadPt, TextAnchor.MiddleRight, Muted);
            closeButton = Btn(kit, "CloseButton", "X", 1800, 10, 1908, 70, ButtonIdle, ButtonPt + 2);
            closeButton.onClick.AddListener(Close);

            // [TASK-GM-18] 진행 단위 4종 - 한 경기 · 주간(6경기, 주간 종료 후 단장 개입) · 전반기/후반기 · 한 시즌(주간마다 사건이 있으면 멈춤)
            modeSingle = Btn(kit, "ModeSingle", "한 경기", 20, 80, 300, 142, ButtonIdle, ButtonPt);
            modeWeek = Btn(kit, "ModeWeek", "주간 진행", 310, 80, 590, 142, new Color(0.2f, 0.42f, 0.32f), ButtonPt);
            modeHalf = Btn(kit, "ModeHalf", "전반기 진행", 600, 80, 890, 142, ButtonIdle, ButtonPt);
            modeFull = Btn(kit, "ModeFull", "한 시즌", 900, 80, 1184, 142, ButtonIdle, ButtonPt);
            modeWeek.onClick.AddListener(() => Run(GMRunMode.Week));
            modeSingle.onClick.AddListener(OpenPreGameOrRun);
            modeHalf.onClick.AddListener(() => Run(GMRunMode.FirstHalf));
            modeFull.onClick.AddListener(() => Run(GMRunMode.FullSeason));
            // [TASK-GM-04] 정규시즌 종료 후 진행 버튼 자리에 표시
            awardsButton = Btn(kit, "AwardsButton", "포스트시즌 & 시상식 보기", 20, 80, 700, 142, new Color(0.15f, 0.45f, 0.85f), ButtonPt);
            nextSeasonButton = Btn(kit, "NextSeasonButton", "다음 시즌 전환", 712, 80, 1184, 142, new Color(0.62f, 0.42f, 0.1f), ButtonPt);
            awardsButton.onClick.AddListener(() => GoPostseasonOrAwards()); // [TASK-GM-17] 정규시즌 종료 → 시즌 결산실 → 포스트시즌 트리(1경기씩)
            nextSeasonButton.onClick.AddListener(() => AdvanceSeason());
            awardsButton.gameObject.SetActive(false);
            nextSeasonButton.gameObject.SetActive(false);
            // [TASK-GM-15] 스토브리그 8 Turn이 끝나기 전에는 [한 경기] · [전반기/후반기] · [한 시즌]을 잠금 막으로 덮는다(우회 개막 차단)
            stoveLock = CompyaUiKit.Fill(root, StoveLockName);
            CompyaUiKit.Box(stoveLock, "LockShade", 16, 76, 1188, 146, new Color(0.05f, 0.06f, 0.08f, 0.9f)).raycastTarget = true;
            stoveLockText = L(kit, "LockText", "", 30, 80, 1176, 142, ButtonPt, TextAnchor.MiddleCenter, Gold, stoveLock);
            stoveLock.gameObject.SetActive(false);

            speed1 = Btn(kit, "Speed1x", "1x", 1196, 80, 1296, 142, ButtonIdle, ButtonPt);
            speed2 = Btn(kit, "Speed2x", "2x", 1306, 80, 1406, 142, ButtonIdle, ButtonPt);
            speed4 = Btn(kit, "Speed4x", "4x", 1416, 80, 1516, 142, ButtonIdle, ButtonPt);
            pauseButton = Btn(kit, "PauseButton", "일시정지", 1526, 80, 1680, 142, ButtonIdle, ButtonPt);
            // [TASK-GM-05] 치어리더 관리 바로가기
            cheerEntryButton = Btn(kit, "CheerEntryButton", "치어리더 엔트리 (4~6인)", 1690, 80, 1908, 142, new Color(0.62f, 0.22f, 0.48f), ButtonPt - 3);
            cheerEntryButton.onClick.AddListener(OpenCheerEntry);
            speed1.onClick.AddListener(() => SetSpeed(1));
            speed2.onClick.AddListener(() => SetSpeed(2));
            speed4.onClick.AddListener(() => SetSpeed(4));
            pauseButton.onClick.AddListener(TogglePause);

            // ---- 패널 1: 순위표(좌측)
            CompyaUiKit.Box(root, "StandingsPanel", 12, 156, 700, 720, PanelColor);
            L(kit, "StandingsTitle", "KBO 2026 시즌 실시간 순위표", 24, 160, 690, 198, SectionPt, TextAnchor.MiddleLeft, Gold);
            float[] colX = { 20, 80, 124, 330, 390, 450, 505, 560, 640, 698 };
            string[] heads = { "순위", "", "구단", "G", "W", "D", "L", "PCT", "GB" };
            int[] colOf = { 0, 2, 3, 4, 5, 6, 7, 8 }; // 셀 열 → colX 시작 인덱스(1 = 로고 칸)
            for (int c = 0; c < 8; c++)
            {
                int ci = colOf[c];
                float x0 = c == 1 ? colX[2] : colX[ci], x1 = colX[ci + 1];
                L(kit, $"StandHead{c}", heads[ci], x0, 200, x1, 234, HeadPt, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            }
            for (int r = 0; r < StandingRows; r++)
            {
                float y0 = 238 + r * 48, y1 = y0 + 44;
                rowBgs[r] = CompyaUiKit.Box(root, $"RowBg{r}", 16, y0, 696, y1, new Color(1f, 1f, 1f, 0.05f));
                rowLogos[r] = CompyaUiKit.Logo(root, $"RowLogo{r}", colX[1], y0 + 4, colX[2] - 6, y1 - 4);
                for (int c = 0; c < 8; c++)
                {
                    int ci = colOf[c];
                    float x0 = c == 1 ? colX[2] : colX[ci], x1 = colX[ci + 1];
                    cells[r, c] = L(kit, $"Cell{r}_{c}", "", x0, y0, x1, y1, RowPt, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, White);
                }
            }
            CompyaUiKit.Box(root, "PostseasonCutline", 16, 238 + 5 * 48 - 3, 696, 238 + 5 * 48 - 1, Gold);

            // ---- 패널 2: 개인 성적 TOP 3(우측, 4열 × 2행)
            CompyaUiKit.Box(root, "LeadersPanel", 712, 156, 1908, 720, PanelColor);
            L(kit, "LeadersTitle", "KBO 개인 성적 TOP 3", 724, 160, 1300, 198, SectionPt, TextAnchor.MiddleLeft, Gold);
            tabBatter = Btn(kit, "TabBatter", "타자 8개 부문", 1320, 160, 1606, 198, ButtonIdle, ButtonPt - 2);
            tabPitcher = Btn(kit, "TabPitcher", "투수 7개 부문", 1616, 160, 1900, 198, ButtonIdle, ButtonPt - 2);
            tabBatter.onClick.AddListener(() => { showPitchers = false; Refresh(); });
            tabPitcher.onClick.AddListener(() => { showPitchers = true; Refresh(); });
            for (int i = 0; i < LeaderCells; i++)
            {
                float x0 = 720 + (i % 4) * 297, x1 = x0 + 289;
                float y0 = 206 + (i / 4) * 256, y1 = y0 + 248;
                leaderBgs[i] = CompyaUiKit.Box(root, $"LeaderBg{i}", x0, y0, x1, y1, new Color(1f, 1f, 1f, 0.07f));
                leaderTitles[i] = L(kit, $"LeaderTitle{i}", "", x0 + 12, y0 + 6, x1 - 8, y0 + 44, LeaderTitlePt, TextAnchor.MiddleLeft, Gold);
                leaderBodies[i] = L(kit, $"LeaderBody{i}", "", x0 + 12, y0 + 48, x1 - 8, y1 - 8, LeaderBodyPt, TextAnchor.UpperLeft, White);
                leaderBodies[i].lineSpacing = 1.25f;
            }

            // ---- 패널 3: 최신 소식(하단 전폭)
            CompyaUiKit.Box(root, "NewsPanel", 12, 728, 1908, 1072, PanelColor);
            L(kit, "NewsTitle", "최신 소식", 24, 732, 300, 770, SectionPt, TextAnchor.MiddleLeft, Gold);
            lastBoxButton = Btn(kit, "LastBoxButton", "직전 경기 결과", 310, 732, 640, 772, ButtonIdle, ButtonPt - 3);
            lastBoxButton.onClick.AddListener(OpenLastBoxScore);
            lastGameText = L(kit, "LastGame", "", 650, 732, 1900, 770, HeadPt, TextAnchor.MiddleRight, White);
            for (int i = 0; i < NewsRows; i++)
            {
                float y0 = 776 + i * 48, y1 = y0 + 44;
                int index = i;
                newsButtons[i] = kit.Button(root, $"News{i}", "", 20, y0, 1900, y1, new Color(1f, 1f, 1f, 0.06f), White, 24, bold: false);
                newsTexts[i] = newsButtons[i].GetComponentInChildren<Text>(true);
                newsTexts[i].alignment = TextAnchor.MiddleLeft;
                TextTidy.Exact(newsTexts[i], NewsPt);
                ((RectTransform)newsTexts[i].transform).offsetMin = new Vector2(14, 0);
                newsButtons[i].onClick.AddListener(() => OpenNews(index));
            }

            // ---- 인터럽트 · 소식 팝업
            popup = CompyaUiKit.Fill(root, "Popup");
            CompyaUiKit.Paint(popup, new Color(0f, 0f, 0f, 0.7f), true);
            CompyaUiKit.Box(popup, "PopupBox", 400, 120, 1520, 960, new Color(0.1f, 0.12f, 0.2f, 0.98f));
            popupTag = L(kit, EventTagName, "", 440, 136, 1480, 174, HeadPt, TextAnchor.MiddleLeft, Muted, popup); // [TASK-GM-18] 우선순위 · 주차 결산 태그
            popupTitle = L(kit, "PopupTitle", "", 440, 180, 1480, 236, PopupTitlePt, TextAnchor.MiddleLeft, Gold, popup);
            popupBody = L(kit, "PopupBody", "", 440, 244, 1480, 548, PopupBodyPt, TextAnchor.UpperLeft, White, popup);
            popupBody.lineSpacing = 1.2f;
            for (int k = 0; k < candidateButtons.Length; k++)
            {
                int index = k;
                candidateButtons[k] = Btn(kit, $"Candidate{k}", "", 440, 560 + k * 76, 1480, 626 + k * 76, new Color(0.2f, 0.32f, 0.6f), ButtonPt - 2, popup);
                candidateButtons[k].onClick.AddListener(() => ChooseCandidate(index));
            }
            popupPrimary = Btn(kit, "PopupPrimary", "확인 후 계속 진행", 440, 870, 950, 940, new Color(0.15f, 0.45f, 0.85f), ButtonPt, popup);
            popupSecondary = Btn(kit, "PopupSecondary", "라인업/엔트리 직접 관리", 970, 870, 1480, 940, new Color(0.3f, 0.34f, 0.46f), ButtonPt, popup);
            popupPrimary.onClick.AddListener(OnPopupPrimary);
            popupSecondary.onClick.AddListener(OnPopupSecondary);
            popup.gameObject.SetActive(false);

            BuildSeasonModal(kit);
            }
        }

        // ================================================================== [TASK-GM-03] 새 시즌 설정 모달

        private static readonly (GMStartMode mode, string label, string desc)[] ModeDefs =
        {
            (GMStartMode.RealCurrent2026, "2026 현역", "2026 현역 모드 - 현역 선수로 10구단 28인을 구성해 2026 스토브리그부터 시작합니다."),
            (GMStartMode.AllTimeDream, "올타임 드림", "올타임 드림 모드 - 1986~2026 구단 계보별 최고 시즌 선수로 드림 로스터와 FA 시장을 꾸립니다. 슈퍼스타 과밀 부작용에 주의하십시오."),
            (GMStartMode.StoryCampaign, "스토리 캠페인", "스토리 캠페인 「꼴찌 구단의 겨울」 - 4년 연속 최하위 · 예산 20% 삭감 · 노장 4번 타자의 트레이드 요구 속에서 포스트시즌을 노립니다."),
        };

        private void BuildSeasonModal(CompyaUiKit kit)
        {
            seasonModal = CompyaUiKit.Fill(root, "SeasonModal");
            CompyaUiKit.Paint(seasonModal, new Color(0f, 0f, 0f, 0.72f), true);
            CompyaUiKit.Box(seasonModal, "SeasonModalBox", 260, 56, 1660, 1044, new Color(0.1f, 0.12f, 0.2f, 0.98f));
            L(kit, "SeasonModalTitle", "새 시즌 설정", 300, 72, 1620, 122, PopupTitlePt, TextAnchor.MiddleLeft, Gold, seasonModal);
            L(kit, "ModeLabel", "시작 모드", 300, 128, 1620, 160, HeadPt, TextAnchor.MiddleLeft, Muted, seasonModal);
            for (int i = 0; i < 3; i++)
            {
                var mode = ModeDefs[i].mode;
                float x0 = 300 + i * 444;
                modeButtons[i] = Btn(kit, $"Mode_{mode}", ModeDefs[i].label, x0, 164, x0 + 432, 224, ButtonIdle, ButtonPt, seasonModal);
                modeButtons[i].onClick.AddListener(() => SelectSeasonMode(mode));
            }
            L(kit, "TeamLabel", "구단 선택", 300, 232, 1620, 264, HeadPt, TextAnchor.MiddleLeft, Muted, seasonModal);
            for (int i = 0; i < NameAliasTable.CanonicalTeamCodes.Length; i++)
            {
                string code = NameAliasTable.CanonicalTeamCodes[i];
                float x0 = 300 + (i % 5) * 266, y0 = 268 + (i / 5) * 66;
                teamButtons[i] = Btn(kit, $"Team_{code}", CompyaUiKit.ShortName(NameAliasTable.ToTeam(code)), x0, y0, x0 + 254, y0 + 58, ButtonIdle, ButtonPt, seasonModal);
                teamButtons[i].onClick.AddListener(() => SelectSeasonTeam(code));
            }
            virtualToggle = Btn(kit, "VirtualToggle", "", 300, 406, 1620, 460, ButtonIdle, ButtonPt, seasonModal);
            virtualToggle.onClick.AddListener(() => SetSeasonVirtualNames(!pendingVirtual));
            // [TASK-GM-06] OOTP 27 4단계 난이도 · 하우스 룰(연간 FA/트레이드 한도)
            L(kit, "DifficultyLabel", "난이도 (FA 요구액 · AI 트레이드 요구 가치 · 구단주 신임도)", 300, 468, 1620, 500, HeadPt, TextAnchor.MiddleLeft, Muted, seasonModal);
            for (int i = 0; i < difficultyButtons.Length; i++)
            {
                var d = GMFrontOffice.Difficulties[i];
                float x0 = 300 + i * 332;
                difficultyButtons[i] = Btn(kit, $"Difficulty_{d}", GMFrontOffice.DifficultyLabel(d), x0, 504, x0 + 320, 560, ButtonIdle, ButtonPt, seasonModal);
                difficultyButtons[i].onClick.AddListener(() => SelectSeasonDifficulty(d));
            }
            houseRuleFA = Btn(kit, "HouseRuleFA", "", 300, 572, 950, 628, ButtonIdle, ButtonPt - 1, seasonModal);
            houseRuleTrade = Btn(kit, "HouseRuleTrade", "", 970, 572, 1620, 628, ButtonIdle, ButtonPt - 1, seasonModal);
            houseRuleFA.onClick.AddListener(() => CycleHouseRule(true));
            houseRuleTrade.onClick.AddListener(() => CycleHouseRule(false));
            seasonModalDesc = L(kit, "SeasonModalDesc", "", 300, 640, 1620, 900, PopupBodyPt - 1, TextAnchor.UpperLeft, White, seasonModal);
            seasonModalDesc.lineSpacing = 1.2f;
            seasonStart = Btn(kit, "SeasonStart", "새 시즌 시작", 300, 920, 950, 1010, new Color(0.15f, 0.45f, 0.85f), ButtonPt + 1, seasonModal);
            seasonCancel = Btn(kit, "SeasonCancel", "취소", 970, 920, 1620, 1010, new Color(0.3f, 0.34f, 0.46f), ButtonPt + 1, seasonModal);
            seasonStart.onClick.AddListener(() => ConfirmNewSeason());
            seasonCancel.onClick.AddListener(CloseSeasonModal);
            seasonModal.gameObject.SetActive(false);
        }

        // [TASK-GM-06] 새 시즌 설정 - 난이도 · 하우스 룰
        private readonly Button[] difficultyButtons = new Button[4];
        private Button houseRuleFA, houseRuleTrade;
        private GMDifficulty pendingDifficulty = GMDifficulty.Majors;
        private int pendingMaxFA, pendingMaxTrades;
        public GMDifficulty PendingDifficulty => pendingDifficulty;

        public void SelectSeasonDifficulty(GMDifficulty d) { pendingDifficulty = d; RefreshSeasonModal(); }

        /// <summary>하우스 룰 순환(제한 없음 → 연 3회 → 연 1회).</summary>
        public void CycleHouseRule(bool freeAgency)
        {
            var steps = GMFrontOffice.HouseRuleSteps;
            int cur = freeAgency ? pendingMaxFA : pendingMaxTrades;
            int next = steps[(Array.IndexOf(steps, cur) + 1 + steps.Length) % steps.Length];
            if (freeAgency) pendingMaxFA = next; else pendingMaxTrades = next;
            RefreshSeasonModal();
        }

        public void SetSeasonHouseRules(int maxFA, int maxTrades) { pendingMaxFA = Math.Max(0, maxFA); pendingMaxTrades = Math.Max(0, maxTrades); RefreshSeasonModal(); }

        public void OpenSeasonModal()
        {
            if (root == null) Build();
            var league = simulator?.League;
            pendingMode = league?.Mode ?? GMStartMode.RealCurrent2026;
            pendingTeam = league?.SelectedTeamCode ?? NameAliasTable.SAM;
            pendingVirtual = league?.UseVirtualNames ?? GameSettings.UseVirtualNames;
            var fo = league?.FrontOffice;
            pendingDifficulty = fo?.Difficulty ?? GMDifficulty.Majors;
            pendingMaxFA = fo?.HouseRuleMaxFA ?? 0;
            pendingMaxTrades = fo?.HouseRuleMaxTrades ?? 0;
            paused = true;
            seasonModal.gameObject.SetActive(true);
            seasonModal.SetAsLastSibling();
            RefreshSeasonModal();
        }

        public void CloseSeasonModal()
        {
            if (seasonModal != null) seasonModal.gameObject.SetActive(false);
            Refresh();
        }

        public void SelectSeasonMode(GMStartMode mode) { pendingMode = mode; RefreshSeasonModal(); }
        public void SelectSeasonTeam(string code) { pendingTeam = NameAliasTable.ResolveCanonicalTeamCode(code) ?? pendingTeam; RefreshSeasonModal(); }
        public void SetSeasonVirtualNames(bool on) { pendingVirtual = on; RefreshSeasonModal(); }

        private void RefreshSeasonModal()
        {
            for (int i = 0; i < 3; i++) Highlight(modeButtons[i], ModeDefs[i].mode == pendingMode);
            for (int i = 0; i < teamButtons.Length; i++) Highlight(teamButtons[i], NameAliasTable.CanonicalTeamCodes[i] == pendingTeam);
            CompyaUiKit.SetButtonText(virtualToggle, pendingVirtual ? "선수 이름: 가상명 (눌러서 실명)" : "선수 이름: 실명 (눌러서 가상명)");
            Highlight(virtualToggle, pendingVirtual);
            for (int i = 0; i < difficultyButtons.Length; i++) Highlight(difficultyButtons[i], GMFrontOffice.Difficulties[i] == pendingDifficulty);
            CompyaUiKit.SetButtonText(houseRuleFA, $"하우스 룰 · 연간 FA 영입: {GMFrontOffice.HouseRuleLabel(pendingMaxFA)}");
            CompyaUiKit.SetButtonText(houseRuleTrade, $"하우스 룰 · 연간 트레이드: {GMFrontOffice.HouseRuleLabel(pendingMaxTrades)}");
            var def = ModeDefs.First(d => d.mode == pendingMode);
            seasonModalDesc.text = $"{def.desc}\n\n선택 구단: {NameAliasTable.DisplayTeamName(pendingTeam)} · 난이도 {GMFrontOffice.DifficultyLabel(pendingDifficulty)}" +
                                   $" (FA 요구액 x{GMFrontOffice.DemandMultiplier(pendingDifficulty):0.00} · 트레이드 요구 x{GMFrontOffice.TradeMargin(pendingDifficulty):0.00} · 구단주 신임도 {GMFrontOffice.StartingTrust(pendingDifficulty)})\n진행 중인 시즌 기록은 새 시즌으로 초기화됩니다.";
        }

        /// <summary>선택한 모드 · 구단 · 이름 설정으로 리그를 즉시 다시 만들고 대시보드를 새 시즌에 연결한다. 실패하면 null.</summary>
        public GMLiveSeasonSimulator ConfirmNewSeason()
        {
            if (simulator != null) simulator.Stop();
            GMLeagueState league;
            GMLiveSeasonSimulator sim = null;
            if (LeagueFactory != null) league = LeagueFactory(pendingMode, pendingTeam, pendingVirtual);
            else if (GameManager.Instance != null)
            {
                league = GameManager.Instance.StartGMLeague(pendingMode, pendingTeam, pendingVirtual);
                sim = GameManager.Instance.GMSimulator;
            }
            else league = GMRosterLoader.LoadModeRoster(pendingMode, pendingTeam, pendingVirtual);
            if (league == null) return null;
            GMFrontOffice.ApplySettings(league, pendingDifficulty, pendingMaxFA, pendingMaxTrades); // [TASK-GM-06]
            if (pendingNewGamePlus != null) { GMCareer.ApplyNewGamePlus(league, pendingNewGamePlus); pendingNewGamePlus = null; } // [TASK-GM-18] 뉴게임+ 해금 계승
            sim = sim ?? new GMLiveSeasonSimulator(league);
            GMStoveTurns.Begin(league); // [TASK-GM-14] 새 시즌 = 스토브리그 Turn 1부터
            seasonModal.gameObject.SetActive(false);
            Bind(sim);
            return sim;
        }

        // ================================================================== [TASK-GM-03] 한 경기 전력 비교 · 직전 경기 결과

        private GMMatchPrePostUIController PrePost
        {
            get
            {
                var view = FindAnyObjectByType<GMMatchPrePostUIController>(FindObjectsInactive.Include);
                if (view != null)
                {
                    view.OnClosed -= OnPrePostClosed;
                    view.OnClosed += OnPrePostClosed;
                }
                return view;
            }
        }

        /// <summary>[한 경기] - 전력 비교 화면이 있으면 먼저 띄운다(경기 시작은 그 화면에서). 없으면 바로 1경기 진행.</summary>
        public void OpenPreGameOrRun()
        {
            if (StoveBlocked()) return; // [TASK-GM-15]
            var view = PrePost;
            if (view != null && simulator != null && simulator.PendingInterrupt == null && view.ShowPreGameView(simulator)) return;
            Run(GMRunMode.SingleGame);
        }

        public void OpenLastBoxScore()
        {
            var last = simulator?.League?.LastUserMatchBoxScore;
            var view = PrePost;
            if (last == null || view == null) { if (statusText != null) statusText.text = "아직 경기 기록 없음"; return; }
            view.Attach(simulator);
            view.ShowPostGameBoxScoreView(last);
        }

        private void OnPrePostClosed()
        {
            Refresh();
            if (simulator?.PendingInterrupt != null) ShowInterrupt(simulator.PendingInterrupt);
        }

        private Text L(CompyaUiKit kit, string name, string text, float x0, float y0, float x1, float y1, int pt, TextAnchor anchor, Color color, Transform parent = null)
        {
            var t = kit.Label(parent ?? root, name, text, x0, y0, x1, y1, pt / 0.9f, anchor, color);
            return TextTidy.Exact(t, pt);
        }

        private Button Btn(CompyaUiKit kit, string name, string text, float x0, float y0, float x1, float y1, Color bg, int pt, Transform parent = null)
        {
            var b = kit.Button(parent ?? root, name, text, x0, y0, x1, y1, bg, White, pt / 0.9f, bold: false);
            TextTidy.ExactButton(b, pt);
            return b;
        }

        // ================================================================== 테마 · 애니메이션

        public void ApplyTheme(Team team)
        {
            themeTeam = team;
            if (background != null) background.color = TeamThemePalette.Background(team);
            if (headerBar != null) headerBar.color = CompyaUiKit.Darken(TeamThemePalette.Primary(team), 0.8f);
            CompyaUiKit.SetLogo(emblem, team, 0.14f);
            if (emblem != null) emblem.gameObject.SetActive(true);
        }

        private void Update()
        {
            if (emblemHolder != null)
            {
                float t = Time.unscaledTime;
                float scale = 1f + 0.035f * Mathf.Sin(t * 1.8f);                       // Pulse Scale
                emblemHolder.localScale = new Vector3(scale, scale, 1f);
                emblemHolder.anchoredPosition = new Vector2(0f, 8f * Mathf.Sin(t * 0.7f)); // 미세 플로팅
                if (emblem != null && emblem.texture != null)
                    emblem.color = new Color(1f, 1f, 1f, 0.1f + 0.07f * (0.5f + 0.5f * Mathf.Sin(t * 2.6f))); // Shimmer Alpha
            }

            if (simulator == null || paused || !simulator.IsRunning) return;
            tickTimer += Time.unscaledDeltaTime * speed;
            if (tickTimer < TickSeconds) return;
            tickTimer = 0f;
            simulator.StepGameDay();
            Refresh();
            if (simulator.PendingInterrupt != null) ShowInterrupt(simulator.PendingInterrupt);
        }

        // ================================================================== 조작

        public void Run(GMRunMode mode)
        {
            if (simulator == null) return;
            if (StoveBlocked()) return; // [TASK-GM-15] 스토브리그 8 Turn 진행 중 = 개막 차단
            if (!simulator.StartRun(mode)) { if (statusText != null) statusText.text = "시즌 종료"; return; }
            paused = false;
            tickTimer = TickSeconds; // 첫 틱은 바로
            Refresh();
        }

        /// <summary>[TASK-GM-15] 스토브리그 8 Turn 진행 중(개막 전) - 고속 진행 · 한 경기 버튼 잠금.</summary>
        public bool IsStoveLocked => GMStoveTurns.IsGating(simulator?.League);
        public bool AnyRunButtonInteractable => modeSingle != null && (modeSingle.interactable || modeWeek.interactable || modeHalf.interactable || modeFull.interactable);

        private bool StoveBlocked()
        {
            if (!IsStoveLocked) return false;
            if (statusText != null) statusText.text = "스토브리그 진행 중 · 개막 전";
            Refresh();
            return true;
        }

        public void SetSpeed(int value)
        {
            speed = value == 4 ? 4 : value == 2 ? 2 : 1;
            Refresh();
        }

        public void TogglePause()
        {
            paused = !paused;
            Refresh();
        }

        // ================================================================== 갱신

        public void Refresh()
        {
            if (root == null) return;
            var league = simulator?.League;
            int played = simulator?.GamesPlayed ?? 0;
            seasonTitle.text = GMLeagueState.SeasonTitle(league?.SeasonNumber ?? 1); // [TASK-GM-14] 「2026 KBO 시즌」 → 「KBO 시즌 1」
            gameCounter.text = $"G {played:000} / {GMLiveSeasonSimulator.SeasonGames}";
            CompyaUiKit.SetButtonText(modeHalf, simulator != null ? simulator.HalfButtonLabel : "전반기 진행");
            bool done = simulator == null || simulator.IsSeasonComplete;
            bool stoveLocked = IsStoveLocked; // [TASK-GM-15]
            modeSingle.interactable = modeWeek.interactable = modeHalf.interactable = modeFull.interactable = !done && !stoveLocked;
            CompyaUiKit.SetButtonText(modeWeek, simulator != null && !done ? $"주간 진행 ({simulator.GamesLeftThisWeek}경기)" : "주간 진행"); // [TASK-GM-18]
            stoveLock.gameObject.SetActive(stoveLocked);
            if (stoveLocked) stoveLockText.text = $"잠김 - 스토브리그 {GMStoveTurns.Label(GMStoveTurns.Current(league))} 진행 중 · 8 Turn을 마쳐야 개막합니다";
            bool seasonOver = simulator != null && simulator.IsSeasonComplete; // [TASK-GM-04]
            modeSingle.gameObject.SetActive(!seasonOver);
            modeWeek.gameObject.SetActive(!seasonOver);
            modeHalf.gameObject.SetActive(!seasonOver);
            modeFull.gameObject.SetActive(!seasonOver);
            bool psDone = league?.Awards?.Postseason != null && league.Awards.Postseason.Completed; // [TASK-GM-17] 포스트시즌을 직접 보기 전에는 시즌 전환 숨김
            awardsButton.gameObject.SetActive(seasonOver);
            nextSeasonButton.gameObject.SetActive(seasonOver && psDone);
            CompyaUiKit.SetButtonText(awardsButton, psDone ? "시상식 보기" : "포스트시즌 트리로 ▶");
            CompyaUiKit.SetButtonText(nextSeasonButton, $"{(league?.SeasonYear ?? GMFeatureFlags.DEFAULT_START_YEAR) + 1} 시즌 전환");
            var awards = league?.Awards;
            awardsReportButton.interactable = (awards != null && awards.HasAny) || seasonOver;
            CompyaUiKit.SetButtonText(pauseButton, paused ? "계속" : "일시정지");
            Highlight(speed1, speed == 1); Highlight(speed2, speed == 2); Highlight(speed4, speed == 4);
            Highlight(tabBatter, !showPitchers); Highlight(tabPitcher, showPitchers);
            statusText.text = stoveLocked ? "스토브리그 진행 중 · 개막 전" : done ? (league?.Awards != null && league.Awards.IsComplete ? "시상식 종료" : "정규시즌 종료") :
                simulator.PendingSeasonEvent != null ? "일시정지 · 단장 개입 필요" : simulator.PendingInterrupt != null ? "일시정지 · 소식 확인" :
                paused ? "일시정지" : simulator.IsRunning ? $"진행 중 · {speed}x" : "대기 중";
            lastGameText.text = simulator?.LastUserGameLine ?? "";
            lastBoxButton.interactable = simulator?.League?.LastUserMatchBoxScore != null;

            RefreshStandings();
            RefreshLeaders();
            RefreshNews();
        }

        private void Highlight(Button b, bool on)
        {
            if (b?.targetGraphic == null) return;
            b.targetGraphic.color = on ? CompyaUiKit.Darken(TeamThemePalette.Primary(themeTeam), 1f) : ButtonIdle;
        }

        private void RefreshStandings()
        {
            var rows = simulator != null ? simulator.Standings() : new List<GMTeamRecord>();
            var leader = rows.FirstOrDefault();
            string userCode = simulator?.League?.SelectedTeamCode;
            for (int r = 0; r < StandingRows; r++)
            {
                var rec = r < rows.Count ? rows[r] : null;
                bool mine = rec != null && rec.TeamCode == userCode;
                rowBgs[r].color = mine ? new Color(TeamThemePalette.Primary(themeTeam).r, TeamThemePalette.Primary(themeTeam).g, TeamThemePalette.Primary(themeTeam).b, 0.75f) : new Color(1f, 1f, 1f, r % 2 == 0 ? 0.05f : 0.02f);
                var team = rec != null ? NameAliasTable.ToTeam(rec.TeamCode) : Team.None;
                CompyaUiKit.SetLogo(rowLogos[r], team, 1f);
                cells[r, 0].text = rec != null ? (r + 1).ToString() : "";
                cells[r, 1].text = rec != null ? NameAliasTable.DisplayTeamName(rec.TeamCode) : "";
                cells[r, 2].text = rec?.G.ToString() ?? "";
                cells[r, 3].text = rec?.W.ToString() ?? "";
                cells[r, 4].text = rec?.D.ToString() ?? "";
                cells[r, 5].text = rec?.L.ToString() ?? "";
                cells[r, 6].text = rec != null ? GMTeamRecord.PctLabel(rec.Pct) : "";
                cells[r, 7].text = rec != null && leader != null ? GMLiveSeasonSimulator.GamesBehindLabel(GMLiveSeasonSimulator.GamesBehind(leader, rec)) : "";
            }
        }

        private void RefreshLeaders()
        {
            var cats = showPitchers ? GMLeaderCategories.Pitcher : GMLeaderCategories.Batter;
            for (int i = 0; i < LeaderCells; i++)
            {
                bool used = i < cats.Length;
                leaderBgs[i].gameObject.SetActive(used);
                leaderTitles[i].gameObject.SetActive(used);
                leaderBodies[i].gameObject.SetActive(used);
                if (!used) continue;
                var cat = cats[i];
                leaderTitles[i].text = GMLeaderCategories.Label(cat);
                var top = simulator != null ? simulator.Leaders(cat) : new List<GMLeaderEntry>();
                leaderBodies[i].text = top.Count == 0
                    ? "기록 집계 전"
                    : string.Join("\n", top.Select((e, k) => $"{k + 1}. {e.Name}({CompyaUiKit.ShortName(NameAliasTable.ToTeam(e.TeamCode))})  {e.ValueLabel}"));
            }
        }

        private void RefreshNews()
        {
            shownNews = simulator?.League?.News.Take(NewsRows).ToList() ?? new List<GMNewsItem>();
            for (int i = 0; i < NewsRows; i++)
            {
                var n = i < shownNews.Count ? shownNews[i] : null;
                newsButtons[i].gameObject.SetActive(n != null);
                if (n != null) newsTexts[i].text = $"{n.DateLabel}   {(n.IsUserTeam ? "★ " : "")}{n.Title}";
            }
        }

        // ================================================================== 팝업

        private void OpenNews(int index)
        {
            if (index >= shownNews.Count) return;
            viewingNews = shownNews[index];
            ShowPopup(viewingNews.Title, $"{viewingNews.DateLabel}\n{viewingNews.Body}", "확인", null);
        }

        public void ShowInterrupt(GMSimInterrupt interrupt)
        {
            viewingNews = null;
            manualSelecting = false;
            if (interrupt.Kind == GMInterruptKind.SeasonEvent && interrupt.Event != null) { ShowSeasonEvent(interrupt.Event); return; } // [TASK-GM-18]
            if (interrupt.Kind == GMInterruptKind.Injury)
                ShowPopup($"부상 발생 · {interrupt.News.Title}", $"{interrupt.News.DateLabel}\n{interrupt.News.Body}\n대체 선수를 어떻게 처리할까요?",
                    "대체 선수 자동 콜업 후 계속", interrupt.Player != null && !interrupt.Player.IsPitcher && interrupt.ReplacementCandidates.Count > 0 ? "라인업/엔트리 직접 관리" : null);
            else
            {
                bool awardNews = interrupt.News.Kind == GMNewsKind.Award;
                ShowPopup(interrupt.News.Title, $"{interrupt.News.DateLabel}\n{interrupt.News.Body}", "확인 후 계속 진행", awardNews ? "올스타전 결과 & 시상 리포트" : null);
                popupOpensAwards = awardNews;
            }
        }

        private void ShowPopup(string title, string body, string primary, string secondary)
        {
            popup.gameObject.SetActive(true);
            popup.SetAsLastSibling();
            viewingEvent = null;
            showingEventResult = showingCareerEnding = false;
            popupPrimary.gameObject.SetActive(true);
            if (popupTag != null) popupTag.text = "";
            popupTitle.text = title;
            popupBody.text = body;
            CompyaUiKit.SetButtonText(popupPrimary, primary);
            popupSecondary.gameObject.SetActive(secondary != null);
            if (secondary != null) CompyaUiKit.SetButtonText(popupSecondary, secondary);
            foreach (var b in candidateButtons) b.gameObject.SetActive(false);
        }

        private void HidePopup()
        {
            if (popup != null) popup.gameObject.SetActive(false);
            manualSelecting = false;
            popupOpensAwards = false;
            viewingEvent = null;
            showingEventResult = showingCareerEnding = false;
        }

        // ================================================================== [TASK-GM-18] 단장 개입 사건 · 커리어 엔딩

        /// <summary>주간 종료 사건 팝업 - [우선순위 · 주차 결산] 태그 · 제목 · 상황/핵심 정보 · 선택지(효과 미리보기) 최대 4개. 선택해야 진행이 이어진다.</summary>
        public void ShowSeasonEvent(GMSeasonEvent ev)
        {
            if (ev == null) return;
            ShowPopup(ev.Title, "", "확인", null);
            viewingEvent = ev;
            popupTag.text = $"[단장 개입 · {GMSeasonEvents.PriorityLabel(ev.Priority)} · {GMSeasonEvents.KindLabel(ev.Kind)}]  {ev.WeekSummary}";
            popupBody.text = ev.Situation + (ev.Facts.Count > 0 ? "\n" + string.Join("\n", ev.Facts.Select(f => "· " + f)) : "");
            popupPrimary.gameObject.SetActive(false);
            popupSecondary.gameObject.SetActive(false);
            for (int k = 0; k < candidateButtons.Length; k++)
            {
                bool has = k < ev.Choices.Count;
                candidateButtons[k].gameObject.SetActive(has);
                if (!has) continue;
                var c = ev.Choices[k];
                CompyaUiKit.SetButtonText(candidateButtons[k], $"{k + 1}. {c.Label}  -  {(c.Available ? c.Effect : c.BlockReason)}");
                candidateButtons[k].interactable = c.Available;
            }
        }

        /// <summary>단장 개입 선택(선택지 index) - 결과를 보여 주고 [확인 후 계속 진행]으로 다음 사건 · 진행을 잇는다.</summary>
        public GMSeasonEventResult ChooseSeasonEvent(int index)
        {
            if (viewingEvent == null || simulator == null || simulator.PendingSeasonEvent != viewingEvent) return null;
            var ev = viewingEvent;
            var r = simulator.ResolveSeasonEvent(index);
            string label = ev.Choices[Math.Max(0, Math.Min(ev.Choices.Count - 1, index))].Label;
            ShowPopup($"결정 결과 · {ev.Title}", (r?.Message ?? "") + $"\n\n선택: {label}", "확인 후 계속 진행", null);
            showingEventResult = true;
            popupTag.text = $"[단장 결정 · {GMSeasonEvents.KindLabel(ev.Kind)}]  올해 단장 개입 {GMSeasonEvents.DecisionsThisYear(simulator.League)}건";
            Refresh();
            return r;
        }

        /// <summary>커리어 엔딩 팝업 - 은퇴 · 해임 요약, 명예의 전당 · 해금, [뉴게임+로 새 커리어].</summary>
        public void ShowCareerEnding()
        {
            var league = simulator?.League;
            if (league == null) return;
            var c = GMCareer.Ensure(league);
            ShowPopup(c.Ending == GMCareerEnding.Retired ? $"단장 은퇴 엔딩 · {c.Seasons}시즌의 여정" : $"단장 해임 엔딩 · {c.Term}기 임기 만료",
                c.Note + "\n\n" + (c.HallOfFame ? "명예의 전당에 헌액되었습니다. " : "") + "새 커리어는 [뉴게임+]로 시작합니다 - 해금 1개당 시작 운영 예산 +2억.",
                "확인", "뉴게임+로 새 커리어");
            showingCareerEnding = true;
            popupTag.text = $"[커리어 엔딩 · {GMCareer.EndingLabel(c.Ending)}]  레거시 {c.LegacyScore}점";
        }

        /// <summary>[뉴게임+] - 새 시즌 설정 모달을 연다. 시작하면 이전 커리어 해금을 새 리그에 물려준다.</summary>
        public void StartNewGamePlus()
        {
            var league = simulator?.League;
            pendingNewGamePlus = league != null ? new List<string>(GMCareer.Ensure(league).Unlocks) : GMCareer.StoredUnlocks();
            HidePopup();
            OpenSeasonModal();
        }

        // ================================================================== [TASK-GM-04] 시상식 · 연도 전환

        private GMAwardsCeremonyUIController Awards
        {
            get
            {
                var view = AwardsView != null ? AwardsView : FindAnyObjectByType<GMAwardsCeremonyUIController>(FindObjectsInactive.Include);
                if (view != null)
                {
                    view.OnClosed -= OnAwardsClosed;
                    view.OnClosed += OnAwardsClosed;
                    view.OnSeasonAdvanced -= OnSeasonAdvanced;
                    view.OnSeasonAdvanced += OnSeasonAdvanced;
                }
                return view;
            }
        }

        /// <summary>
        /// [TASK-GM-17] 정규시즌 종료 후 [포스트시즌 트리로 ▶] - 대시보드를 닫고 프런트 오피스 허브의 [진행하기] 흐름(시즌 결산실 1회 → 포스트시즌 트리 1경기씩)으로 보낸다.
        /// 포스트시즌이 끝났으면 시상식을 연다. 허브가 없으면 기존처럼 시상식(포스트시즌 일괄 진행).
        /// </summary>
        public Func<bool> PostseasonRoute { get; set; }

        public void GoPostseasonOrAwards()
        {
            var league = simulator?.League;
            bool psDone = league?.Awards?.Postseason != null && league.Awards.Postseason.Completed;
            if (!psDone && simulator != null && simulator.IsSeasonComplete)
            {
                if (simulator != null) simulator.Stop();
                HidePopup();
                var route = PostseasonRoute;
                if (route != null && route()) { gameObject.SetActive(false); return; }
                var hub = FindAnyObjectByType<GMOotpFrontOfficeUIController>(FindObjectsInactive.Include);
                if (hub != null && hub.ContinueFromDashboard(simulator)) { gameObject.SetActive(false); return; }
            }
            OpenAwards(null);
        }

        /// <summary>[시상 리포트] · [포스트시즌 & 시상식 보기] - 시상식 화면을 연다(정규시즌이 끝났으면 포스트시즌 → 11월 시상식 개최).</summary>
        public void OpenAwards(GMAwardsTab? initialTab)
        {
            var view = Awards;
            if (view == null || simulator == null) { if (statusText != null) statusText.text = "시상식 화면 없음"; return; }
            view.Open(simulator, initialTab);
        }

        /// <summary>[20yy 시즌 전환] - 남은 포스트시즌 · 시상식을 마저 치르고 다음 해 0/144로 넘어간다. 새 진행기(실패하면 null).</summary>
        public GMLiveSeasonSimulator AdvanceSeason()
        {
            if (simulator == null || !simulator.IsSeasonComplete) return null;
            var league = simulator.League;
            if (!GMAwardEvaluator.AdvanceToNextSeasonYear(simulator))
            {
                if (GMCareer.Ensure(league).Ended) { ShowCareerEnding(); Refresh(); } // [TASK-GM-18] 30시즌 은퇴 · 임기 말 해임
                return null;
            }
            var gm = GameManager.Instance;
            GMLiveSeasonSimulator next;
            if (gm != null && gm.GMLeague == league)
            {
                gm.RestoreGMLeague(league);
                next = gm.GMSimulator;
            }
            else next = new GMLiveSeasonSimulator(league);
            Bind(next);
            return next;
        }

        /// <summary>[치어리더 엔트리 (4~6인)] - 진행을 잠시 멈추고 구단 응원단 관리 화면을 연다. 닫으면 대시보드로 돌아와 이어서 진행한다.</summary>
        public void OpenCheerEntry()
        {
            var view = CheerView != null ? CheerView : FindAnyObjectByType<GMCheerleaderEntryUIController>(FindObjectsInactive.Include);
            var team = simulator?.League?.UserTeam;
            if (view == null || team == null) { if (statusText != null) statusText.text = "응원단 화면 없음"; return; }
            if (!paused) { paused = true; pausedForCheer = true; }
            view.Open(team, OnCheerClosed);
        }

        private void OnCheerClosed()
        {
            if (pausedForCheer) paused = pausedForCheer = false;
            Refresh();
        }

        private void OnAwardsClosed()
        {
            if (pausedForAwards) paused = pausedForAwards = false; // 올스타 리포트를 보고 돌아오면 이어서 진행
            Refresh();
        }

        private void OnSeasonAdvanced(GMLiveSeasonSimulator next)
        {
            if (next != null) Bind(next);
        }

        private void OnPopupPrimary()
        {
            if (showingEventResult || showingCareerEnding) { viewingNews = null; AfterPopup(); return; } // [TASK-GM-18] 결과 확인 = 이미 처리됨
            if (viewingEvent != null) return; // 사건은 선택지로만 처리
            var pending = simulator?.PendingInterrupt;
            if (viewingNews == null && pending != null)
                simulator.ResolveInterrupt(pending.Kind == GMInterruptKind.Injury ? GMInterruptChoice.AutoCallUp : GMInterruptChoice.Continue);
            viewingNews = null;
            AfterPopup();
        }

        private void OnPopupSecondary()
        {
            if (showingCareerEnding) { StartNewGamePlus(); return; } // [TASK-GM-18]
            if (popupOpensAwards)
            {
                if (simulator?.PendingInterrupt != null && simulator.PendingInterrupt.Kind == GMInterruptKind.Record) simulator.ResolveInterrupt(GMInterruptChoice.Continue);
                HidePopup();
                paused = pausedForAwards = true;
                OpenAwards(GMAwardsTab.AllStarMonthly);
                return;
            }
            var pending = simulator?.PendingInterrupt;
            if (pending == null || pending.Kind != GMInterruptKind.Injury) return;
            manualSelecting = true;
            popupBody.text = $"{pending.News.Body}\n{LineupLabel(pending.Position)} 자리에 넣을 선수를 고르십시오.";
            for (int k = 0; k < candidateButtons.Length; k++)
            {
                bool has = k < pending.ReplacementCandidates.Count;
                candidateButtons[k].gameObject.SetActive(has);
                if (!has) continue;
                var c = pending.ReplacementCandidates[k];
                CompyaUiKit.SetButtonText(candidateButtons[k], $"{c.Template.PlayerName} ({LineupLabel(c.Template.BatterPosition)} · OVR {c.GetEffectiveOverall()})");
            }
            popupSecondary.gameObject.SetActive(false);
        }

        private void ChooseCandidate(int index)
        {
            if (viewingEvent != null) { ChooseSeasonEvent(index); return; } // [TASK-GM-18]
            var pending = simulator?.PendingInterrupt;
            if (!manualSelecting || pending == null || index >= pending.ReplacementCandidates.Count) return;
            simulator.ResolveInterrupt(GMInterruptChoice.ManualLineup, pending.ReplacementCandidates[index]);
            AfterPopup();
        }

        private void AfterPopup()
        {
            HidePopup();
            if (simulator?.PendingInterrupt != null) ShowInterrupt(simulator.PendingInterrupt);
            Refresh();
        }

        private static string LineupLabel(BatterPosition pos)
        {
            switch (pos)
            {
                case BatterPosition.Catcher: return "포수";
                case BatterPosition.FirstBase: return "1루수";
                case BatterPosition.SecondBase: return "2루수";
                case BatterPosition.ThirdBase: return "3루수";
                case BatterPosition.ShortStop: return "유격수";
                case BatterPosition.LeftField: return "좌익수";
                case BatterPosition.CenterField: return "중견수";
                case BatterPosition.RightField: return "우익수";
                default: return "지명타자";
            }
        }
    }
}
