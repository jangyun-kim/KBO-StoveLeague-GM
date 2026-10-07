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
    /// [TASK-GM-06] OOTP 27 스타일 프런트 오피스 통합 허브(1920×1080 Landscape, 1920×1080 px 좌표 · 모든 글씨 Normal · 15pt 이상).
    ///   - 최상단 팀 배너: 구단 로고 · 구단명 · 시즌 성적(승-무-패 · 승률 · GB · 순위) · 날짜 | 4일 일정 티커 | [✔ 진행하기 (CONTINUE / PLAY) >] · [새 시즌/난이도 설정]
    ///   - 1단 메인 탭 8개 · 2단 골드(#F5B82E) 서브 탭
    ///   - 프런트 오피스 6분할(구단주 정보 · 구단주 목표 + [건의] · 연도별 승률 · 가성비 TOP 10 · 재정/연장계약 · 관중 · 응원)
    ///   - 스토브리그 협상: 연봉·재계약 / FA / 트레이드 · Shop a Player / 신인 드래프트 · 백분위 / 스토리 안건 · 엔딩
    ///   - 선수단 · 라인업, 전략 · ABS 분석, 응원단, 시상 · 구단 역사 요약 + 각 전용 화면 바로가기
    /// 게임을 켜서 로비로 들어오면(UIManager.ShowScreen(Lobby)) 이 허브가 기본으로 열린다(OnLobbyScreenShown).
    /// </summary>
    public partial class GMOotpFrontOfficeUIController : MonoBehaviour
    {
        public const string RootName = "FrontOfficeRoot";
        public const int BannerTitlePt = 30, BannerPt = 19, SmallPt = 16, TabPt = 18, SubTabPt = 18, PanelTitlePt = 19, BodyPt = 16, TablePt = 16, CellPt = 15, ButtonPt = 18, BigPt = 36;
        public const int MainTabCount = 8, SubTabMax = 6, GoalRows = 6, CostRows = 12, BudgetRows = 8, HistoryBars = 10, TableRows = 14, FARows = 15, ShopRows = 7, DraftRows = 10, PctRows = 7, AgendaBlocks = 5;

        public static readonly Color Gold = new Color(0.961f, 0.722f, 0.18f); // #F5B82E
        private static readonly Color Bg = new Color(0.09f, 0.09f, 0.1f);
        private static readonly Color PanelColor = new Color(0.15f, 0.15f, 0.17f);
        private static readonly Color TabBarColor = new Color(0.12f, 0.12f, 0.14f);
        private static readonly Color White = new Color(0.95f, 0.95f, 0.96f);
        private static readonly Color Muted = new Color(0.68f, 0.7f, 0.74f);
        private static readonly Color Dark = new Color(0.1f, 0.1f, 0.1f);
        private static readonly Color GreenOk = new Color(0.36f, 0.86f, 0.36f);
        private static readonly Color RedBad = new Color(1f, 0.38f, 0.32f);
        private static readonly Color BlueLink = new Color(0.4f, 0.7f, 1f);
        private static readonly Color ContinueGreen = new Color(0.17f, 0.75f, 0.23f);
        private static readonly Color ButtonIdle = new Color(1f, 1f, 1f, 0.1f);
        private static readonly Color ButtonOn = new Color(0.15f, 0.45f, 0.85f);
        private static readonly Color RowIdle = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color RowAlt = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color RowSelected = new Color(0.25f, 0.42f, 0.8f, 0.85f);
        private static readonly Color BarGreen = new Color(0.16f, 0.62f, 0.2f);
        private static readonly Color BarRed = new Color(0.62f, 0.12f, 0.1f);
        private static readonly Color BarBlue = new Color(0.33f, 0.4f, 0.62f);

        public static readonly string[] MainTabLabels =
            { "프런트 오피스(스토브리그)", "실시간 시즌 대시보드", "선수단·라인업", "전략·ABS 분석", "트레이드·FA 시장", "스카우팅·드래프트", "응원단 (15인/엔트리 4~6인)", "시상·구단 역사" };

        /// <summary>서브 탭 한 칸 - Pane이 있으면 그 화면으로 전환, Action이 있으면 전용 화면을 연다.</summary>
        private sealed class SubTab
        {
            public string Label;
            public string Pane;
            public Action Action;
        }

        public const string PaneOwner = "Pane_Owner", PaneSalaries = "Pane_Salaries", PaneFA = "Pane_FA", PaneTrade = "Pane_Trade", PaneDraft = "Pane_Draft",
            PaneStory = "Pane_Story", PaneLive = "Pane_Live", PaneRoster = "Pane_Roster", PaneChem = "Pane_Chem", PaneAbs = "Pane_Abs", PaneCheer = "Pane_Cheer", PaneHistory = "Pane_History";

        [SerializeField] private Font regularFont;

        private CompyaUiKit kit;
        private RectTransform root;
        private readonly Dictionary<string, RectTransform> panes = new Dictionary<string, RectTransform>();
        private List<SubTab>[] subTabs;
        private int mainTab, subTab;
        private string currentPane = PaneOwner;

        // ---- 헤더
        private Image banner;
        private RawImage teamLogo;
        private Text teamName, teamRecord, teamDate, statusLine;
        private readonly Text[] tickerLabels = new Text[4], tickerValues = new Text[4];
        private Button continueButton, settingsButton, lobbyButton;
        private readonly Button[] mainTabs = new Button[MainTabCount];
        private readonly Button[] subTabButtons = new Button[SubTabMax];

        // ---- 프런트 오피스 6분할
        private readonly Text[] ownerValues = new Text[10];
        private Text goalsTrust;
        private readonly Text[,] goalCells = new Text[GoalRows, 6];
        private readonly Button[] goalDiscuss = new Button[GoalRows];
        private readonly Image[] recBars = new Image[HistoryBars], attBars = new Image[HistoryBars];
        private readonly Text[] recVals = new Text[HistoryBars], recYears = new Text[HistoryBars], attYears = new Text[HistoryBars];
        private Text attNow, attCheer;
        private readonly Text[,] costCells = new Text[CostRows, 4];
        private readonly Text[] budgetLabels = new Text[BudgetRows], budgetValues = new Text[BudgetRows];

        // ---- 건의 팝업
        private RectTransform discussPopup;
        private Text discussTitle, discussBody;
        private GMOwnerGoal discussGoal;

        // ---- 연봉 · 재계약
        private readonly Button[] salRows = new Button[TableRows];
        private readonly Text[,] salCells = new Text[TableRows, 9];
        private Text salPage, extPlayer, extInfo, extSalaryLabel, extTeamwork, extMessage;
        private readonly Button[] extYears = new Button[5];
        private Slider extSlider;
        private Button extConcession, extSubmit, extCaptain, extRelease;
        private string salSort = "OVR";
        private bool salDesc = true;
        private int salPageIndex;
        private Player extSelected;
        private int extYearsValue = 3;
        private bool extConcessionOn;
        private List<Player> salShown = new List<Player>();

        // ---- FA
        private readonly Button[] faRows = new Button[FARows];
        private readonly Text[,] faCells = new Text[FARows, 8];
        private Text faPlayer, faInfo, faSalaryLabel, faBudget, faMessage, faCompensation;
        private readonly Button[] faYears = new Button[GMStoveLeagueMarket.MaxFAYears];
        private Slider faSlider;
        private Button faScout, faRole, faOffer;
        private string faSort = "OVR";
        private Player faSelected;
        private int faYearsValue = 3;
        private bool faRoleOn;
        private List<Player> faShown = new List<Player>();

        // ---- 트레이드
        private readonly Button[] trMyRows = new Button[TableRows], trTheirRows = new Button[TableRows];
        private Text trPartnerName, trMySlots, trTheirSlots, trValueLabel, trNeeds, trMyPage;
        private RectTransform trValueFill;
        private readonly Button[] trShopSorts = new Button[5];
        private readonly Button[] trOffers = new Button[ShopRows];
        private int partnerIndex, trMyPageIndex;
        private readonly List<Player> trMine = new List<Player>(), trTheirs = new List<Player>();
        private List<Player> trMyShown = new List<Player>(), trTheirShown = new List<Player>();
        private List<GMTradeOffer> shopOffers = new List<GMTradeOffer>();
        private Player shopTarget;
        private GMOfferSort shopSort = GMOfferSort.Ovr;

        // ---- 드래프트 · 백분위
        private readonly Button[] drRows = new Button[DraftRows];
        private Text drTitle, drDetailTitle, drNote;
        private readonly Text[] drPctLabels = new Text[PctRows], drPctValues = new Text[PctRows];
        private readonly RectTransform[] drPctFills = new RectTransform[PctRows];
        private Button drPick;
        private Player drSelected;
        private RectTransform pctPopup;
        private Text pctTitle, pctInfo;
        private readonly Text[] pctLabels = new Text[PctRows], pctValues = new Text[PctRows];
        private readonly RectTransform[] pctFills = new RectTransform[PctRows];

        // ---- 스토리 안건
        private readonly Text[] agTitles = new Text[AgendaBlocks], agBodies = new Text[AgendaBlocks];
        private readonly Button[,] agOptions = new Button[AgendaBlocks, 3];
        private readonly Text[] stInd = new Text[6];
        private Text stEnding;
        private List<GMAgendaState> agShown = new List<GMAgendaState>();

        // ---- 실시간 · 선수단 · ABS · 응원단 · 역사
        private readonly Text[,] lvCells = new Text[10, 6];
        private readonly Text[] lvLeads = new Text[8], lvNews = new Text[6];
        private Text lvInfo;
        private readonly Button[] roRows = new Button[TableRows];
        private readonly Text[,] roCells = new Text[TableRows, 10];
        private Text roPage;
        private bool roPitchers;
        private int roPageIndex;
        private string roSort = "POS";
        private List<Player> roShown = new List<Player>();
        private Text chScore, chMeta, chMessages;
        private readonly Text[,] abCells = new Text[10, 6];
        private readonly Text[] abTop = new Text[9];
        private Text abLast, abNote;
        private Text ceEntry, ceEffects, cePool;
        private readonly Text[,] hiCells = new Text[12, 6];
        private Text hiAwards, hiEnding;

        private GMLiveSeasonSimulator simulator;
        private bool followGameManager = true;

        // ---- 테스트 주입(비우면 씬에서 찾는다)
        public GMLiveLeagueDashboardUIController DashView { get; set; }
        public GMMatchPrePostUIController PrePostView { get; set; }
        public GMCheerleaderEntryUIController CheerView { get; set; }
        public GMAwardsCeremonyUIController AwardsView { get; set; }

        public RectTransform Root => root;
        public GMLiveSeasonSimulator Simulator => simulator;
        public GMLeagueState League => simulator?.League;
        public GMTeamState UserTeam => simulator?.League?.UserTeam;
        public int CurrentMainTab => mainTab;
        public int CurrentSubTab => subTab;
        public string CurrentPane => currentPane;
        public string StatusText => statusLine != null ? statusLine.text : "";
        public RectTransform Pane(string name) => panes.TryGetValue(name, out var p) ? p : null;
        public Player ExtensionSelected => extSelected;
        public Player FASelected => faSelected;
        public Player DraftSelected => drSelected;
        public IReadOnlyList<GMTradeOffer> ShopOffers => shopOffers;
        public IReadOnlyList<Player> TradeMine => trMine;
        public IReadOnlyList<Player> TradeTheirs => trTheirs;
        public GMTeamState TradePartner => PartnerTeams().ElementAtOrDefault(partnerIndex);
        public bool IsDiscussOpen => discussPopup != null && discussPopup.gameObject.activeSelf;
        public bool IsPercentileOpen => pctPopup != null && pctPopup.gameObject.activeSelf;

        /// <summary>다음 로비 진입 1회 자동 열기를 건너뛴다([클래식 로비]).</summary>
        public static bool SuppressAutoOpenOnce { get; set; }

        public void Configure(Font regular) => regularFont = regular;

        private void Awake()
        {
            if (root == null) Build();
        }

        // ================================================================== 진입점

        /// <summary>UIManager.ShowScreen(Lobby) 직후 호출(플레이 모드) - 단장 모드 허브를 로비 위에 연다.</summary>
        public static void OnLobbyScreenShown()
        {
            if (!Application.isPlaying) return;
            if (SuppressAutoOpenOnce) { SuppressAutoOpenOnce = false; return; }
            var view = FindAnyObjectByType<GMOotpFrontOfficeUIController>(FindObjectsInactive.Include);
            if (view != null) view.OpenHub();
        }

        /// <summary>[TASK-GM-06] 기존 [계약·연봉·팀워크 진단] 진입 대신 프런트 오피스 [연봉·재계약 협상]을 연다(플레이 모드). 열었으면 true.</summary>
        public static bool OpenFromDiagnostic()
        {
            if (!Application.isPlaying) return false;
            var view = FindAnyObjectByType<GMOotpFrontOfficeUIController>(FindObjectsInactive.Include);
            if (view == null) return false;
            view.OpenHub();
            view.SelectMainTab(0);
            view.SelectSubTab(1);
            return true;
        }

        /// <summary>GameManager 단장 모드 리그(없으면 기본 시작)를 붙여 허브를 맨 위에 연다.</summary>
        public void OpenHub()
        {
            if (root == null) Build();
            followGameManager = true;
            var gm = GameManager.Instance;
            if (gm != null) gm.EnsureGMLeague();
            simulator = gm != null ? gm.GMSimulator : simulator;
            Show();
        }

        /// <summary>진행기를 직접 붙인다(테스트 · 툴). GameManager를 따라가지 않는다.</summary>
        public void Bind(GMLiveSeasonSimulator sim)
        {
            if (root == null) Build();
            followGameManager = false;
            simulator = sim;
            ResetSelections();
            Show();
        }

        private void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (League != null) GMFrontOffice.Ensure(League);
            ApplyTheme();
            Refresh();
        }

        public void Hide() => gameObject.SetActive(false);

        private void ResetSelections()
        {
            extSelected = faSelected = drSelected = shopTarget = null;
            trMine.Clear(); trTheirs.Clear(); shopOffers.Clear();
            partnerIndex = salPageIndex = trMyPageIndex = roPageIndex = 0;
            trCash = 0; counterOffer = null; prSelected = null; protectPage = 0; // [TASK-GM-08]
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
            panes.Clear();
            var font = regularFont != null ? regularFont : TextTidy.BodyFont;
            kit = new CompyaUiKit(font, font);
            using (CompyaUiKit.Wide())
            {
                root = CompyaUiKit.Fill(transform, RootName);
                CompyaUiKit.Paint(root, Bg, true);
                BuildContentArea();  // [TASK-GM-07] 퀵 사이드바 자리를 비운 화면 영역
                BuildHeader();
                BuildToolbar();      // [TASK-GM-07] 최상단 드롭다운 툴바
                BuildSidebar();      // [TASK-GM-07] 우측 세로 퀵 아이콘 사이드바
                BuildOwnerPane();
                BuildSalariesPane();
                BuildFAPane();
                BuildTradePane();
                BuildDraftPane();
                BuildStoryPane();
                BuildLivePane();
                BuildRosterPane();
                BuildChemPane();
                BuildAbsPane();
                BuildCheerPane();
                BuildHistoryPane();
                BuildSchedulePane();     // [TASK-GM-07] 시즌 일정(달력)
                BuildPostseasonPane();   // [TASK-GM-07] 포스트시즌 트리 · [TASK-GM-08] KBO 리더 · 4열 브래킷 · 일일 리포트
                BuildProtectPane();      // [TASK-GM-08] FA 보상 · 보호 명단
                BuildDiscussPopup();
                BuildPercentilePopup();
                BuildManagerSetup();     // [TASK-GM-07] 감독 설정
                BuildCounterPopup();     // [TASK-GM-08] AI 단장 1:N 역제안
            }
            subTabs = BuildSubTabs();
            mainTab = 0; subTab = 0; currentPane = PaneOwner;
            ApplyPaneVisibility();
            RefreshTabs();
        }

        private Text L(Transform parent, string name, string text, float x0, float y0, float x1, float y1, int pt, TextAnchor anchor, Color color)
        {
            var t = kit.Label(parent, name, text, x0, y0, x1, y1, pt / 0.9f, anchor, color);
            return TextTidy.Exact(t, pt);
        }

        private Button Btn(Transform parent, string name, string text, float x0, float y0, float x1, float y1, Color bg, int pt, Color? textColor = null)
        {
            var b = kit.Button(parent, name, text, x0, y0, x1, y1, bg, textColor ?? White, pt / 0.9f, bold: false);
            TextTidy.ExactButton(b, pt);
            return b;
        }

        /// <summary>표 한 행 = 버튼(배경 · 클릭) + 열 텍스트(행 내부 정규화 좌표). cols = 열 경계 px(절대 좌표).</summary>
        private Button Row(Transform parent, string name, float x0, float y0, float x1, float y1, float[] cols, Text[,] cells, int r, int pt)
        {
            var rect = CompyaUiKit.Place(parent, name, x0, y0, x1, y1);
            var img = CompyaUiKit.Paint(rect, RowIdle, true);
            var b = rect.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            float w = x1 - x0;
            for (int c = 0; c + 1 < cols.Length; c++)
            {
                var cellRect = CompyaUiKit.Norm(rect, $"C{c}", (cols[c] - x0) / w, 0f, (cols[c + 1] - 4 - x0) / w, 1f);
                var t = kit.LabelOn(cellRect, "", pt / 0.9f, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, White);
                TextTidy.Exact(t, pt);
                cells[r, c] = t;
            }
            return b;
        }

        private RectTransform PaneRoot(string name)
        {
            var p = CompyaUiKit.Fill(contentArea != null ? contentArea : root, name);
            panes[name] = p;
            return p;
        }

        private RectTransform Panel(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var p = CompyaUiKit.Fill(parent, name);
            CompyaUiKit.Box(p, "Bg", x0, y0, x1, y1, PanelColor);
            return p;
        }

        private RectTransform Bar(Transform parent, string name, float x0, float y0, float x1, float y1, Color fill)
        {
            var bg = CompyaUiKit.Box(parent, name, x0, y0, x1, y1, new Color(1f, 1f, 1f, 0.1f));
            var f = CompyaUiKit.Fill(bg.transform, "Fill");
            CompyaUiKit.Paint(f, fill);
            f.anchorMax = new Vector2(0f, 1f);
            return f;
        }

        private static void SetFill(RectTransform fill, float v)
        {
            if (fill == null) return;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(Mathf.Clamp01(v), 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
        }

        /// <summary>코드 조립 슬라이더(배경 · 채움 · 손잡이) - 연봉 제시용.</summary>
        private Slider BuildSlider(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var rect = CompyaUiKit.Place(parent, name, x0, y0, x1, y1);
            var bg = CompyaUiKit.Fill(rect, "Background");
            CompyaUiKit.Paint(bg, new Color(1f, 1f, 1f, 0.12f), true);
            var fillArea = CompyaUiKit.Norm(rect, "Fill Area", 0.01f, 0.25f, 0.99f, 0.75f);
            var fill = CompyaUiKit.Fill(fillArea, "Fill");
            CompyaUiKit.Paint(fill, Gold);
            var handleArea = CompyaUiKit.Norm(rect, "Handle Slide Area", 0.01f, 0f, 0.99f, 1f);
            var handle = CompyaUiKit.Fill(handleArea, "Handle");
            handle.sizeDelta = new Vector2(18f, 0f);
            handle.anchorMin = new Vector2(0f, 0f);
            handle.anchorMax = new Vector2(0f, 1f);
            var hImg = CompyaUiKit.Paint(handle, White, true);
            var slider = rect.gameObject.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = hImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f; slider.maxValue = 1f; slider.value = 0.5f;
            return slider;
        }

        // ------------------------------------------------------------------ 헤더 · 탭

        private void BuildHeader()
        {
            // [TASK-GM-07] 최상단 툴바(0~44) 아래로 배너를 46~134에 압축 - 4일 일정 티커 유지
            banner = CompyaUiKit.Box(root, "Banner", 0, 46, 1920, 134, new Color(0.15f, 0.2f, 0.35f));
            teamLogo = CompyaUiKit.Logo(root, "TeamLogo", 20, 50, 104, 130);
            teamName = L(root, "TeamName", "", 120, 47, 780, 87, BannerTitlePt, TextAnchor.MiddleLeft, White);
            teamRecord = L(root, "TeamRecord", "", 120, 88, 780, 111, BannerPt, TextAnchor.MiddleLeft, White);
            teamDate = L(root, "TeamDate", "", 120, 112, 780, 133, SmallPt, TextAnchor.MiddleLeft, Muted);
            string[] ticker = { "어제", "오늘", "내일", "다음 시리즈" };
            for (int i = 0; i < 4; i++)
            {
                float y0 = 48 + i * 21.5f;
                tickerLabels[i] = L(root, $"TickerLabel{i}", ticker[i], 800, y0, 930, y0 + 21, SmallPt, TextAnchor.MiddleLeft, Muted);
                tickerValues[i] = L(root, $"TickerValue{i}", "", 936, y0, 1390, y0 + 21, SmallPt, TextAnchor.MiddleLeft, White);
            }
            L(root, "ContinueCheck", "✔", 1404, 52, 1470, 130, BigPt - 6, TextAnchor.MiddleCenter, ContinueGreen);
            continueButton = Btn(root, "ContinueButton", "✔ 진행하기 (CONTINUE / PLAY) >", 1476, 52, 1760, 130, ContinueGreen, 20);
            continueButton.onClick.AddListener(() => Continue());
            settingsButton = Btn(root, "SettingsButton", "감독 설정", 1772, 50, 1908, 88, ButtonIdle, 15);
            settingsButton.onClick.AddListener(OpenManagerSetup);
            lobbyButton = Btn(root, "LobbyButton", "클래식 로비", 1772, 94, 1908, 130, ButtonIdle, SmallPt);
            lobbyButton.onClick.AddListener(GoClassicLobby);

            CompyaUiKit.Box(root, "MainTabBar", 0, 136, 1920, 192, TabBarColor);
            for (int i = 0; i < MainTabCount; i++)
            {
                int index = i;
                float x0 = 8 + i * 238;
                mainTabs[i] = Btn(root, $"MainTab{i}", MainTabLabels[i], x0, 140, x0 + 232, 188, ButtonIdle, TabPt);
                mainTabs[i].onClick.AddListener(() => SelectMainTab(index));
            }
            CompyaUiKit.Box(root, "SubTabBar", 0, 196, 1920, 242, Gold);
            for (int k = 0; k < SubTabMax; k++)
            {
                int index = k;
                float x0 = 12 + k * 312;
                subTabButtons[k] = Btn(root, $"SubTab{k}", "", x0, 200, x0 + 300, 238, new Color(0f, 0f, 0f, 0f), SubTabPt, Dark);
                subTabButtons[k].onClick.AddListener(() => SelectSubTab(index));
            }
            statusLine = L(root, "StatusLine", "", 12, 1046, 1908, 1076, SmallPt, TextAnchor.MiddleLeft, Gold);
        }

        private List<SubTab>[] BuildSubTabs()
        {
            SubTab P(string label, string pane) => new SubTab { Label = label, Pane = pane };
            SubTab A(string label, Action action) => new SubTab { Label = label, Action = action };
            return new[]
            {
                new List<SubTab> { P("구단주·재정 대시보드", PaneOwner), P("연봉·재계약 협상", PaneSalaries), P("FA 영입 협상", PaneFA), P("트레이드(Shop Player)", PaneTrade), P("신인 드래프트", PaneDraft), P("스토리 안건", PaneStory) },
                new List<SubTab> { P("순위·리더·소식", PaneLive), P("시즌 일정(캘린더)", PaneSchedule), A("대시보드 열기", OpenDashboard), A("한 경기 3단계 진행", OpenPreGame), A("직전 경기 박스스코어", OpenLastBox) },
                new List<SubTab> { P("타자", PaneRoster), P("투수", PaneRoster), P("팀워크 진단", PaneChem) },
                new List<SubTab> { P("ABS 분석·팀 비교", PaneAbs), P("팀워크·전술 기조", PaneChem) },
                new List<SubTab> { P("FA 영입 협상", PaneFA), P("트레이드·Shop a Player", PaneTrade), P("연봉·재계약", PaneSalaries), P("FA 보상·보호명단", PaneProtect) }, // [TASK-GM-08]
                new List<SubTab> { P("신인 드래프트·백분위", PaneDraft), P("FA 스카우팅", PaneFA) },
                new List<SubTab> { P("엔트리·전담 응원", PaneCheer), A("15인 풀·엔트리 관리 열기", OpenCheerEntry), A("치어리더 영입", () => GoLegacy(ScreenType.CheerleaderShop)), A("보유 치어리더·성장", () => GoLegacy(ScreenType.CheerleaderInventory)) },
                new List<SubTab> { P("구단 역사", PaneHistory), P("포스트시즌 트리", PanePostseason), A("시상식 열기", OpenAwards), P("스토리 엔딩", PaneStory) },
            };
        }

        public void SelectMainTab(int index)
        {
            if (subTabs == null) Build();
            mainTab = Mathf.Clamp(index, 0, MainTabCount - 1);
            subTab = 0;
            var first = subTabs[mainTab].First(s => s.Pane != null);
            subTab = subTabs[mainTab].IndexOf(first);
            SwitchPane(first.Pane);
        }

        /// <summary>서브 탭 - 화면 전환(Pane) 또는 전용 화면 열기(Action). 열기 탭은 선택 표시만 바꾸지 않는다.</summary>
        public void SelectSubTab(int index)
        {
            if (subTabs == null) Build();
            var list = subTabs[mainTab];
            if (index < 0 || index >= list.Count) return;
            var s = list[index];
            if (s.Action != null) { s.Action(); RefreshTabs(); return; }
            subTab = index;
            if (mainTab == 2) { roPitchers = index == 1; roPageIndex = 0; }
            SwitchPane(s.Pane);
        }

        private void SwitchPane(string pane)
        {
            currentPane = pane;
            ApplyPaneVisibility();
            Refresh();
        }

        private void ApplyPaneVisibility()
        {
            foreach (var pair in panes) pair.Value.gameObject.SetActive(pair.Key == currentPane);
            if (discussPopup != null) discussPopup.SetAsLastSibling();
            if (pctPopup != null) pctPopup.SetAsLastSibling();
            if (counterPopup != null) counterPopup.SetAsLastSibling(); // [TASK-GM-08]
            foreach (var m in menuLists) if (m != null) m.SetAsLastSibling(); // [TASK-GM-07] 툴바 메뉴 · 감독 설정은 맨 위
            if (managerPopup != null) managerPopup.SetAsLastSibling();
        }

        private void RefreshTabs()
        {
            for (int i = 0; i < MainTabCount; i++)
                if (mainTabs[i] != null) mainTabs[i].targetGraphic.color = i == mainTab ? new Color(Gold.r, Gold.g, Gold.b, 0.35f) : ButtonIdle;
            var list = subTabs[mainTab];
            for (int k = 0; k < SubTabMax; k++)
            {
                bool used = k < list.Count;
                subTabButtons[k].gameObject.SetActive(used);
                if (!used) continue;
                CompyaUiKit.SetButtonText(subTabButtons[k], list[k].Action != null ? list[k].Label + " ▶" : list[k].Label);
                bool on = k == subTab && list[k].Pane != null;
                subTabButtons[k].targetGraphic.color = on ? Dark : new Color(0f, 0f, 0f, 0f);
                var label = subTabButtons[k].GetComponentInChildren<Text>(true);
                if (label != null) label.color = on ? Gold : Dark;
            }
        }

        // ------------------------------------------------------------------ 프런트 오피스 6분할

        private static readonly string[] OwnerLabels = { "모기업 · 구단주", "인내심", "재정 성향", "구단 개입도", "최우선 가치", "현재 기분", "시즌 기대치", "구단주 신임도", "팀워크 점수", "라커룸 부작용" };
        private static readonly string[] GoalHeads = { "부여연도", "목표연도", "우선순위", "목표 분류", "상세 목표 내용", "현재 진행 상황", "건의" };
        private static readonly float[] GoalCols = { 624, 728, 832, 954, 1094, 1454, 1772 };
        private static readonly string[] BudgetLabels = { "예상 시즌 총예산", "보장 연봉 총액", "재계약 예상 총액", "응원단·홈 마케팅 수익", "기타 운영비", "총 지출 예상", "FA 영입 가용 자금", "다년 연장계약 가용 자금" };

        private void BuildOwnerPane()
        {
            var pane = PaneRoot(PaneOwner);
            var info = Panel(pane, "OwnerInfoPanel", 12, 248, 600, 640);
            L(info, "OwnerInfoTitle", "구단주 정보 (OWNER INFORMATION)", 24, 254, 590, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int k = 0; k < 10; k++)
            {
                float y0 = 292 + k * 34;
                L(info, $"OwnerLabel{k}", OwnerLabels[k], 24, y0, 210, y0 + 32, BodyPt, TextAnchor.MiddleLeft, Muted);
                ownerValues[k] = L(info, $"OwnerValue{k}", "", 214, y0, 590, y0 + 32, BodyPt, TextAnchor.MiddleLeft, White);
            }

            var goals = Panel(pane, "OwnerGoalsPanel", 612, 248, 1908, 640);
            L(goals, "OwnerGoalsTitle", "구단주 목표 (OWNER GOALS)", 624, 254, 1240, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            goalsTrust = L(goals, "GoalsTrust", "", 1250, 254, 1900, 286, SmallPt, TextAnchor.MiddleRight, Muted);
            for (int c = 0; c < 7; c++)
                L(goals, $"GoalHead{c}", GoalHeads[c], GoalCols[c], 292, c < 6 ? GoalCols[c + 1] - 4 : 1900, 322, CellPt, c == 4 || c == 5 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < GoalRows; r++)
            {
                int row = r;
                float y0 = 328 + r * 52, y1 = y0 + 48;
                CompyaUiKit.Box(goals, $"GoalRowBg{r}", 620, y0, 1902, y1, r % 2 == 0 ? RowIdle : RowAlt);
                for (int c = 0; c < 6; c++)
                    goalCells[r, c] = L(goals, $"Goal{r}_{c}", "", GoalCols[c], y0, GoalCols[c + 1] - 4, y1, c >= 4 ? CellPt : BodyPt, c >= 4 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, White);
                goalDiscuss[r] = Btn(goals, $"GoalDiscuss{r}", "건의", 1776, y0 + 6, 1900, y1 - 6, new Color(0.35f, 0.35f, 0.4f), BodyPt);
                goalDiscuss[r].onClick.AddListener(() => OpenDiscuss(row));
            }

            var rec = Panel(pane, "RecordHistoryPanel", 12, 652, 477, 1040);
            L(rec, "RecordTitle", "연도별 승률 (RECORD HISTORY)", 24, 658, 465, 688, PanelTitlePt - 1, TextAnchor.MiddleLeft, Gold);
            CompyaUiKit.Box(rec, "RecBaseline", 30, 960 - 0.5f / 0.8f * 230f - 1, 465, 960 - 0.5f / 0.8f * 230f + 1, new Color(1f, 1f, 1f, 0.35f));
            for (int i = 0; i < HistoryBars; i++)
            {
                float sx = 34 + i * 43;
                recBars[i] = CompyaUiKit.Box(rec, $"RecBar{i}", sx + 7, 900, sx + 36, 960, BarGreen);
                recVals[i] = L(rec, $"RecVal{i}", "", sx, 876, sx + 43, 898, CellPt, TextAnchor.MiddleCenter, White);
                recYears[i] = L(rec, $"RecYear{i}", "", sx, 964, sx + 43, 990, CellPt, TextAnchor.MiddleCenter, Muted);
            }
            L(rec, "RecLegend", "초록 = 5할 이상 · 빨강 = 5할 미만 · 흰 선 = .500", 24, 996, 465, 1034, CellPt, TextAnchor.MiddleLeft, Muted);

            var cost = Panel(pane, "CostEfficientPanel", 489, 652, 954, 1040);
            L(cost, "CostTitle", "가성비 선수 TOP 12 (COST EFFICIENT)", 501, 658, 942, 688, PanelTitlePt - 1, TextAnchor.MiddleLeft, Gold);
            float[] cc = { 501, 704, 760, 854, 946 };
            string[] ch = { "순위 · 선수", "WAR", "연봉", "1WAR당" };
            for (int c = 0; c < 4; c++) L(cost, $"CostHead{c}", ch[c], cc[c], 692, cc[c + 1] - 4, 716, CellPt, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, Muted);
            for (int r = 0; r < CostRows; r++) // [TASK-GM-07] TOP 12(OOTP COST EFFICIENT PLAYERS 12행)
            {
                float y0 = 718 + r * 26.5f;
                for (int c = 0; c < 4; c++)
                    costCells[r, c] = L(cost, $"Cost{r}_{c}", "", cc[c], y0, cc[c + 1] - 4, y0 + 26, CellPt, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight, White);
            }

            var bud = Panel(pane, "BudgetPanel", 966, 652, 1431, 1040);
            L(bud, "BudgetTitle", "재정 · 연장계약 (EXTENSION & BUDGET)", 978, 658, 1419, 688, PanelTitlePt - 1, TextAnchor.MiddleLeft, Gold);
            for (int k = 0; k < BudgetRows; k++)
            {
                float y0 = 696 + k * 42;
                budgetLabels[k] = L(bud, $"BudLabel{k}", BudgetLabels[k], 978, y0, 1200, y0 + 38, BodyPt, TextAnchor.MiddleLeft, Muted);
                budgetValues[k] = L(bud, $"BudValue{k}", "", 1204, y0, 1419, y0 + 38, BodyPt, TextAnchor.MiddleRight, White);
            }

            var att = Panel(pane, "AttendancePanel", 1443, 652, 1908, 1040);
            L(att, "AttendanceTitle", "관중 · 응원 (ATTENDANCE & CHEER)", 1455, 658, 1896, 688, PanelTitlePt - 1, TextAnchor.MiddleLeft, Gold);
            attNow = L(att, "AttNow", "", 1455, 694, 1896, 720, CellPt, TextAnchor.MiddleLeft, White);
            for (int i = 0; i < HistoryBars; i++)
            {
                float sx = 1460 + i * 43;
                attBars[i] = CompyaUiKit.Box(att, $"AttBar{i}", sx + 7, 820, sx + 36, 880, BarBlue);
                attYears[i] = L(att, $"AttYear{i}", "", sx, 884, sx + 43, 908, CellPt, TextAnchor.MiddleCenter, Muted);
            }
            attCheer = L(att, "AttCheer", "", 1455, 914, 1896, 1034, CellPt, TextAnchor.UpperLeft, White);
            attCheer.lineSpacing = 1.05f;
        }

        // ------------------------------------------------------------------ 연봉 · 재계약

        private static readonly float[] SalCols = { 16, 104, 304, 364, 474, 644, 784, 844, 1044, 1286 };
        private static readonly string[] SalHeads = { "포지션", "이름", "나이", "OVR/잠재", "성향(Ego)", "현재 연봉", "잔여", "요구 조건", "상태" };

        private void BuildSalariesPane()
        {
            var pane = PaneRoot(PaneSalaries);
            CompyaUiKit.Box(pane, "TableBg", 12, 248, 1292, 1036, PanelColor);
            L(pane, "SalSortLabel", "정렬", 20, 252, 90, 290, BodyPt, TextAnchor.MiddleLeft, Muted);
            string[] keys = { "OVR", "SAL", "YRS", "AGE", "POS" };
            string[] labels = { "OVR", "연봉", "잔여 계약", "나이", "포지션" };
            for (int k = 0; k < keys.Length; k++)
            {
                string key = keys[k];
                float x0 = 96 + k * 136;
                Btn(pane, $"SalSort_{key}", labels[k], x0, 252, x0 + 130, 290, ButtonIdle, BodyPt).onClick.AddListener(() => SortSalaries(key));
            }
            Btn(pane, "SalPrev", "◀ 이전", 1000, 252, 1095, 290, ButtonIdle, BodyPt).onClick.AddListener(() => { salPageIndex = Math.Max(0, salPageIndex - 1); RefreshSalaries(); });
            salPage = L(pane, "SalPage", "", 1099, 252, 1191, 290, BodyPt, TextAnchor.MiddleCenter, White);
            Btn(pane, "SalNext", "다음 ▶", 1195, 252, 1288, 290, ButtonIdle, BodyPt).onClick.AddListener(() => { salPageIndex++; RefreshSalaries(); });
            for (int c = 0; c < 9; c++) L(pane, $"SalHead{c}", SalHeads[c], SalCols[c], 296, SalCols[c + 1] - 4, 328, CellPt, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < TableRows; r++)
            {
                int row = r;
                float y0 = 332 + r * 50;
                salRows[r] = Row(pane, $"SalRow{r}", 12, y0, 1288, y0 + 46, SalCols, salCells, r, TablePt);
                salRows[r].onClick.AddListener(() => SelectSalaryRow(row));
            }

            CompyaUiKit.Box(pane, "ExtBg", 1302, 248, 1908, 1036, PanelColor);
            L(pane, "ExtTitle", "재계약 · 연장 협상 (SALARIES & EXTENSIONS)", 1314, 254, 1896, 290, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            extPlayer = L(pane, "ExtPlayer", "", 1314, 294, 1896, 328, BannerPt - 1, TextAnchor.MiddleLeft, White);
            extInfo = L(pane, "ExtInfo", "", 1314, 330, 1896, 396, BodyPt, TextAnchor.UpperLeft, Muted);
            L(pane, "ExtYearsLabel", "계약 기간", 1314, 402, 1440, 440, BodyPt, TextAnchor.MiddleLeft, Muted);
            for (int k = 0; k < 5; k++)
            {
                int years = k + 1;
                float x0 = 1446 + k * 90;
                extYears[k] = Btn(pane, $"ExtYears{years}", $"{years}년", x0, 402, x0 + 84, 440, ButtonIdle, BodyPt);
                extYears[k].onClick.AddListener(() => { extYearsValue = years; RefreshExtensionPanel(); });
            }
            extSalaryLabel = L(pane, "ExtSalaryLabel", "", 1314, 448, 1896, 482, BodyPt, TextAnchor.MiddleLeft, White);
            extSlider = BuildSlider(pane, "ExtSalarySlider", 1314, 488, 1896, 520);
            extSlider.onValueChanged.AddListener(_ => RefreshExtensionSalaryLabel());
            extConcession = Btn(pane, "ExtConcession", "", 1314, 530, 1896, 572, ButtonIdle, BodyPt);
            extConcession.onClick.AddListener(() => { extConcessionOn = !extConcessionOn; RefreshExtensionPanel(); });
            extSubmit = Btn(pane, "ExtSubmit", "재계약/연장 협상 실행", 1314, 582, 1896, 636, ButtonOn, ButtonPt + 1);
            extSubmit.onClick.AddListener(() => SubmitExtension());
            extCaptain = Btn(pane, "ExtCaptain", "주장(Captain) 임명", 1314, 646, 1600, 698, new Color(0.45f, 0.36f, 0.12f), ButtonPt);
            extCaptain.onClick.AddListener(() => AppointCaptainSelected());
            extRelease = Btn(pane, "ExtRelease", "방출(Release)", 1610, 646, 1896, 698, new Color(0.55f, 0.18f, 0.16f), ButtonPt);
            extRelease.onClick.AddListener(() => ReleaseSelected());
            extTeamwork = L(pane, "ExtTeamwork", "", 1314, 708, 1896, 742, BodyPt + 1, TextAnchor.MiddleLeft, Gold);
            extMessage = L(pane, "ExtMessage", "", 1314, 748, 1896, 1030, BodyPt, TextAnchor.UpperLeft, White);
            extMessage.lineSpacing = 1.15f;
        }

        // ------------------------------------------------------------------ FA

        private static readonly float[] FACols = { 16, 104, 304, 364, 500, 664, 744, 964, 1286 };
        private static readonly string[] FAHeads = { "포지션", "이름", "나이", "OVR/잠재", "성향", "ABS", "요구액(연봉 × 년)", "스카우팅" };

        private void BuildFAPane()
        {
            var pane = PaneRoot(PaneFA);
            CompyaUiKit.Box(pane, "TableBg", 12, 248, 1292, 1036, PanelColor);
            L(pane, "FASortLabel", "정렬", 20, 252, 90, 290, BodyPt, TextAnchor.MiddleLeft, Muted);
            string[] keys = { "OVR", "POT", "AGE", "DEM", "POS" };
            string[] labels = { "OVR", "잠재력", "나이", "요구액", "포지션" };
            for (int k = 0; k < keys.Length; k++)
            {
                string key = keys[k];
                float x0 = 96 + k * 136;
                Btn(pane, $"FASort_{key}", labels[k], x0, 252, x0 + 130, 290, ButtonIdle, BodyPt).onClick.AddListener(() => { faSort = key; RefreshFA(); });
            }
            L(pane, "FAMarketNote", "스토브리그 FA 시장 매물 (OVR 상위 15명)", 800, 252, 1288, 290, CellPt, TextAnchor.MiddleRight, Muted);
            for (int c = 0; c < 8; c++) L(pane, $"FAHead{c}", FAHeads[c], FACols[c], 296, FACols[c + 1] - 4, 328, CellPt, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < FARows; r++)
            {
                int row = r;
                float y0 = 332 + r * 46;
                faRows[r] = Row(pane, $"FARow{r}", 12, y0, 1288, y0 + 43, FACols, faCells, r, TablePt);
                faRows[r].onClick.AddListener(() => SelectFARow(row));
            }

            CompyaUiKit.Box(pane, "FABg", 1302, 248, 1908, 1036, PanelColor);
            L(pane, "FATitle", "FA 영입 협상 (FREE AGENCY)", 1314, 254, 1896, 290, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            faPlayer = L(pane, "FAPlayer", "", 1314, 294, 1896, 328, BannerPt - 1, TextAnchor.MiddleLeft, White);
            faInfo = L(pane, "FAInfo", "", 1314, 330, 1896, 396, CellPt, TextAnchor.UpperLeft, Muted);
            faScout = Btn(pane, "FAScout", $"스카우팅 리포트 열람 (-{GMStoveLeagueMarket.ScoutCost:N0}만 원)", 1314, 402, 1896, 444, new Color(0.3f, 0.32f, 0.5f), BodyPt);
            faScout.onClick.AddListener(() => ScoutSelected());
            L(pane, "FAYearsLabel", "계약 기간", 1314, 452, 1440, 490, BodyPt, TextAnchor.MiddleLeft, Muted);
            for (int k = 0; k < faYears.Length; k++)
            {
                int years = k + 1;
                float x0 = 1446 + k * 112;
                faYears[k] = Btn(pane, $"FAYears{years}", $"{years}년", x0, 452, x0 + 106, 490, ButtonIdle, BodyPt);
                faYears[k].onClick.AddListener(() => { faYearsValue = years; RefreshFAPanel(); });
            }
            faSalaryLabel = L(pane, "FASalaryLabel", "", 1314, 498, 1896, 530, BodyPt, TextAnchor.MiddleLeft, White);
            faSlider = BuildSlider(pane, "FASalarySlider", 1314, 536, 1896, 568);
            faSlider.onValueChanged.AddListener(_ => RefreshFASalaryLabel());
            faRole = Btn(pane, "FARoleGuarantee", "", 1314, 578, 1896, 620, ButtonIdle, BodyPt);
            faRole.onClick.AddListener(() => { faRoleOn = !faRoleOn; RefreshFAPanel(); });
            faOffer = Btn(pane, "FAOffer", "계약 제시 (Offer Contract)", 1314, 630, 1896, 684, ButtonOn, ButtonPt + 1);
            faOffer.onClick.AddListener(() => OfferSelected());
            faBudget = L(pane, "FABudget", "", 1314, 692, 1896, 740, CellPt, TextAnchor.UpperLeft, Gold);
            faCompensation = L(pane, "FACompensation", "", 1314, 744, 1896, 800, CellPt, TextAnchor.UpperLeft, BlueLink); // [TASK-GM-08] FA 등급 · 보상
            faMessage = L(pane, "FAMessage", "", 1314, 806, 1896, 1030, BodyPt, TextAnchor.UpperLeft, White);
            faMessage.lineSpacing = 1.15f;
        }

        // ------------------------------------------------------------------ 트레이드 · Shop a Player

        private void BuildTradePane()
        {
            var pane = PaneRoot(PaneTrade);
            CompyaUiKit.Box(pane, "MyBg", 12, 248, 624, 1036, PanelColor);
            L(pane, "TrMyTitle", "내 선수단 (클릭 = 내줄 선수 · 최대 3)", 20, 252, 380, 290, CellPt, TextAnchor.MiddleLeft, Gold);
            Btn(pane, "TrMyPrev", "◀", 384, 252, 444, 290, ButtonIdle, BodyPt).onClick.AddListener(() => { trMyPageIndex = Math.Max(0, trMyPageIndex - 1); RefreshTrade(); });
            trMyPage = L(pane, "TrMyPage", "", 448, 252, 556, 290, CellPt, TextAnchor.MiddleCenter, White);
            Btn(pane, "TrMyNext", "▶", 560, 252, 620, 290, ButtonIdle, BodyPt).onClick.AddListener(() => { trMyPageIndex++; RefreshTrade(); });
            CompyaUiKit.Box(pane, "TheirBg", 632, 248, 1244, 1036, PanelColor);
            Btn(pane, "TrPartnerPrev", "◀", 640, 252, 720, 290, ButtonIdle, BodyPt).onClick.AddListener(() => ShiftPartner(-1));
            trPartnerName = L(pane, "TrPartnerName", "", 724, 252, 1152, 290, BodyPt, TextAnchor.MiddleCenter, Gold);
            Btn(pane, "TrPartnerNext", "▶", 1156, 252, 1236, 290, ButtonIdle, BodyPt).onClick.AddListener(() => ShiftPartner(1));
            for (int r = 0; r < TableRows; r++)
            {
                int row = r;
                float y0 = 296 + r * 52;
                trMyRows[r] = ListRow(pane, $"TrMyRow{r}", 16, y0, 620, y0 + 48);
                trMyRows[r].onClick.AddListener(() => ToggleMine(row));
                trTheirRows[r] = ListRow(pane, $"TrTheirRow{r}", 636, y0, 1240, y0 + 48);
                trTheirRows[r].onClick.AddListener(() => ToggleTheirs(row));
            }

            CompyaUiKit.Box(pane, "BlockBg", 1252, 248, 1908, 1036, PanelColor);
            L(pane, "TrBlockTitle", "직접 트레이드 협상 (1:N · 최대 3:3 · 연봉 보조)", 1264, 252, 1896, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            trMySlots = L(pane, "TrMySlots", "", 1264, 290, 1896, 346, CellPt, TextAnchor.UpperLeft, White);
            trTheirSlots = L(pane, "TrTheirSlots", "", 1264, 350, 1896, 406, CellPt, TextAnchor.UpperLeft, White);
            trValueLabel = L(pane, "TrValueLabel", "", 1264, 410, 1896, 440, CellPt, TextAnchor.MiddleLeft, Gold);
            trValueFill = Bar(pane, "TrValueBar", 1264, 444, 1896, 466, BarGreen);
            trNeeds = L(pane, "TrNeeds", "", 1264, 470, 1896, 502, CellPt, TextAnchor.MiddleLeft, Muted);
            BuildTradeCashRow(pane); // [TASK-GM-08] 연봉 보조
            Btn(pane, "TrPropose", "트레이드 제안", 1264, 546, 1576, 590, ButtonOn, ButtonPt).onClick.AddListener(() => ProposeTrade());
            Btn(pane, "TrClear", "선택 초기화", 1586, 546, 1896, 590, ButtonIdle, ButtonPt).onClick.AddListener(() => { trMine.Clear(); trTheirs.Clear(); trCash = 0; RefreshTrade(); });
            Btn(pane, "TrCounter", "AI 단장 역제안 받기 (받을 선수 1명 기준 · 1:N 패키지)", 1264, 596, 1896, 636, new Color(0.3f, 0.32f, 0.5f), BodyPt).onClick.AddListener(() => RequestCounterOffer());
            L(pane, "TrShopTitle", "Shop a Player (트레이드 매물 내놓기)", 1264, 642, 1896, 676, PanelTitlePt - 1, TextAnchor.MiddleLeft, Gold);
            Btn(pane, "TrShopButton", "선택한 내 선수를 매물로 등록 → 9개 구단 제안 받기", 1264, 680, 1896, 718, new Color(0.45f, 0.36f, 0.12f), BodyPt).onClick.AddListener(() => ShopSelected());
            for (int k = 0; k < 5; k++)
            {
                var sort = (GMOfferSort)k;
                float x0 = 1264 + k * 127;
                trShopSorts[k] = Btn(pane, $"TrShopSort_{k}", GMStoveLeagueMarket.SortLabel(sort), x0, 724, x0 + 121, 756, ButtonIdle, CellPt);
                trShopSorts[k].onClick.AddListener(() => SortShop(sort));
            }
            for (int i = 0; i < ShopRows; i++)
            {
                int index = i;
                float y0 = 762 + i * 38;
                trOffers[i] = ListRow(pane, $"TrOffer{i}", 1264, y0, 1896, y0 + 35, CellPt);
                trOffers[i].onClick.AddListener(() => AcceptShopOffer(index));
            }
        }

        private Button ListRow(Transform parent, string name, float x0, float y0, float x1, float y1, int pt = TablePt)
        {
            var b = kit.Button(parent, name, "", x0, y0, x1, y1, RowIdle, White, pt / 0.9f, bold: false);
            var label = b.GetComponentInChildren<Text>(true);
            label.alignment = TextAnchor.MiddleLeft;
            TextTidy.Exact(label, pt);
            ((RectTransform)label.transform).offsetMin = new Vector2(10, 0);
            return b;
        }

        // ------------------------------------------------------------------ 드래프트 · 백분위

        private void BuildDraftPane()
        {
            var pane = PaneRoot(PaneDraft);
            CompyaUiKit.Box(pane, "ListBg", 12, 248, 1100, 1036, PanelColor);
            drTitle = L(pane, "DrTitle", "", 20, 252, 1092, 292, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int r = 0; r < DraftRows; r++)
            {
                int row = r;
                float y0 = 300 + r * 72;
                drRows[r] = ListRow(pane, $"DrRow{r}", 16, y0, 1096, y0 + 66);
                drRows[r].onClick.AddListener(() => SelectDraftRow(row));
            }
            CompyaUiKit.Box(pane, "DetailBg", 1112, 248, 1908, 1036, PanelColor);
            drDetailTitle = L(pane, "DrDetailTitle", "", 1124, 252, 1896, 292, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int k = 0; k < PctRows; k++)
            {
                float y0 = 300 + k * 58;
                drPctLabels[k] = L(pane, $"DrPctLabel{k}", "", 1124, y0, 1330, y0 + 50, BodyPt, TextAnchor.MiddleLeft, White);
                drPctFills[k] = Bar(pane, $"DrPctBar{k}", 1336, y0 + 12, 1760, y0 + 38, Gold);
                drPctValues[k] = L(pane, $"DrPctValue{k}", "", 1770, y0, 1896, y0 + 50, BodyPt, TextAnchor.MiddleRight, White);
            }
            drPick = Btn(pane, "DrPick", "지명 (Draft)", 1124, 712, 1896, 768, ButtonOn, ButtonPt + 1);
            drPick.onClick.AddListener(() => DraftSelected_());
            drNote = L(pane, "DrNote", "", 1124, 778, 1896, 1030, BodyPt, TextAnchor.UpperLeft, Muted);
            drNote.lineSpacing = 1.12f;
        }

        private void BuildPercentilePopup()
        {
            pctPopup = CompyaUiKit.Fill(root, "PercentilePopup");
            CompyaUiKit.Paint(pctPopup, new Color(0f, 0f, 0f, 0.72f), true);
            CompyaUiKit.Box(pctPopup, "PctBox", 520, 180, 1400, 920, new Color(0.13f, 0.13f, 0.16f, 0.98f));
            pctTitle = L(pctPopup, "PctTitle", "", 560, 200, 1360, 240, PanelTitlePt + 3, TextAnchor.MiddleLeft, Gold);
            pctInfo = L(pctPopup, "PctInfo", "", 560, 244, 1360, 300, BodyPt, TextAnchor.UpperLeft, Muted);
            for (int k = 0; k < PctRows; k++)
            {
                float y0 = 310 + k * 62;
                pctLabels[k] = L(pctPopup, $"PctLabel{k}", "", 560, y0, 780, y0 + 52, BodyPt + 1, TextAnchor.MiddleLeft, White);
                pctFills[k] = Bar(pctPopup, $"PctBar{k}", 790, y0 + 14, 1240, y0 + 38, Gold);
                pctValues[k] = L(pctPopup, $"PctValue{k}", "", 1250, y0, 1360, y0 + 52, BodyPt + 1, TextAnchor.MiddleRight, White);
            }
            Btn(pctPopup, "PctClose", "닫기", 560, 820, 1360, 890, ButtonIdle, ButtonPt).onClick.AddListener(() => pctPopup.gameObject.SetActive(false));
            pctPopup.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ 스토리 안건

        private void BuildStoryPane()
        {
            var pane = PaneRoot(PaneStory);
            for (int i = 0; i < AgendaBlocks; i++)
            {
                int block = i;
                float y0 = 252 + i * 156;
                CompyaUiKit.Box(pane, $"AgBg{i}", 12, y0 - 2, 1292, y0 + 150, PanelColor);
                agTitles[i] = L(pane, $"AgTitle{i}", "", 24, y0, 1284, y0 + 32, BannerPt - 1, TextAnchor.MiddleLeft, Gold);
                agBodies[i] = L(pane, $"AgBody{i}", "", 24, y0 + 34, 1284, y0 + 92, BodyPt, TextAnchor.UpperLeft, White);
                for (int k = 0; k < 3; k++)
                {
                    int option = k;
                    float x0 = 24 + k * 422;
                    agOptions[i, k] = Btn(pane, $"AgOpt{i}_{k}", "", x0, y0 + 98, x0 + 412, y0 + 144, new Color(0.25f, 0.27f, 0.36f), BodyPt);
                    agOptions[i, k].onClick.AddListener(() => ResolveAgendaBlock(block, option));
                }
            }
            CompyaUiKit.Box(pane, "IndBg", 1302, 248, 1908, 1036, PanelColor);
            L(pane, "StIndTitle", "구단 지표 · 엔딩 전망", 1314, 254, 1896, 290, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int k = 0; k < stInd.Length; k++)
            {
                float y0 = 298 + k * 46;
                stInd[k] = L(pane, $"StInd{k}", "", 1314, y0, 1896, y0 + 42, BodyPt, TextAnchor.MiddleLeft, White);
            }
            stEnding = L(pane, "StEnding", "", 1314, 584, 1896, 1030, BodyPt, TextAnchor.UpperLeft, White);
            stEnding.lineSpacing = 1.15f;
        }

        // ------------------------------------------------------------------ 실시간 · 선수단 · 팀워크 · ABS · 응원단 · 역사

        private static readonly float[] LvCols = { 16, 84, 364, 604, 724, 824, 936 };

        private void BuildLivePane()
        {
            var pane = PaneRoot(PaneLive);
            CompyaUiKit.Box(pane, "StandBg", 12, 248, 940, 780, PanelColor);
            L(pane, "LvStandTitle", "KBO 시즌 순위표", 20, 252, 932, 290, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            string[] heads = { "순위", "구단", "승-무-패", "승률", "게임차", "최근 5" };
            for (int c = 0; c < 6; c++) L(pane, $"LvHead{c}", heads[c], LvCols[c], 296, LvCols[c + 1] - 4, 330, CellPt, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < 10; r++)
            {
                float y0 = 334 + r * 44;
                for (int c = 0; c < 6; c++)
                    lvCells[r, c] = L(pane, $"LvCell{r}_{c}", "", LvCols[c], y0, LvCols[c + 1] - 4, y0 + 40, BodyPt, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, White);
            }
            CompyaUiKit.Box(pane, "LeadBg", 952, 248, 1908, 860, PanelColor);
            L(pane, "LvLeadTitle", "KBO 개인 성적 1위", 960, 252, 1900, 290, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int i = 0; i < 8; i++)
            {
                float x0 = 960 + (i % 2) * 470, y0 = 296 + (i / 2) * 66;
                lvLeads[i] = L(pane, $"LvLead{i}", "", x0, y0, x0 + 460, y0 + 60, CellPt, TextAnchor.UpperLeft, White);
            }
            L(pane, "LvNewsTitle", "최신 소식", 960, 564, 1900, 598, PanelTitlePt - 1, TextAnchor.MiddleLeft, Gold);
            for (int i = 0; i < 6; i++) lvNews[i] = L(pane, $"LvNews{i}", "", 960, 602 + i * 42, 1900, 640 + i * 42, CellPt, TextAnchor.MiddleLeft, White);
            Btn(pane, "LvOpenDashboard", "실시간 시즌 대시보드 열기", 12, 878, 632, 940, ButtonOn, ButtonPt).onClick.AddListener(OpenDashboard);
            Btn(pane, "LvPreGame", "한 경기 전력 비교", 644, 878, 1264, 940, ButtonIdle, ButtonPt).onClick.AddListener(OpenPreGame);
            Btn(pane, "LvLastBox", "직전 경기 박스스코어", 1276, 878, 1896, 940, ButtonIdle, ButtonPt).onClick.AddListener(OpenLastBox);
            lvInfo = L(pane, "LvInfo", "", 12, 950, 1908, 1036, BodyPt, TextAnchor.UpperLeft, Muted);
        }

        private static readonly float[] RoCols = { 16, 110, 344, 410, 544, 640, 844, 944, 1124, 1204, 1904 };
        private static readonly string[] RoHeads = { "포지션", "이름", "나이", "OVR/잠재", "ABS", "성향(Ego)", "만족도", "연봉", "잔여", "상태 · 클릭 = 백분위" };

        private void BuildRosterPane()
        {
            var pane = PaneRoot(PaneRoster);
            CompyaUiKit.Box(pane, "TableBg", 12, 248, 1908, 1036, PanelColor);
            L(pane, "RoSortLabel", "정렬", 20, 252, 90, 290, BodyPt, TextAnchor.MiddleLeft, Muted);
            string[] keys = { "POS", "OVR", "POT", "ABS", "AGE", "SAL" };
            string[] labels = { "포지션", "OVR", "잠재력", "ABS", "나이", "연봉" };
            for (int k = 0; k < keys.Length; k++)
            {
                string key = keys[k];
                float x0 = 96 + k * 136;
                Btn(pane, $"RoSort_{key}", labels[k], x0, 252, x0 + 130, 290, ButtonIdle, BodyPt).onClick.AddListener(() => { roSort = key; roPageIndex = 0; RefreshRoster(); });
            }
            Btn(pane, "RoPrev", "◀ 이전", 1620, 252, 1715, 290, ButtonIdle, BodyPt).onClick.AddListener(() => { roPageIndex = Math.Max(0, roPageIndex - 1); RefreshRoster(); });
            roPage = L(pane, "RoPage", "", 1719, 252, 1805, 290, BodyPt, TextAnchor.MiddleCenter, White);
            Btn(pane, "RoNext", "다음 ▶", 1809, 252, 1904, 290, ButtonIdle, BodyPt).onClick.AddListener(() => { roPageIndex++; RefreshRoster(); });
            for (int c = 0; c < 10; c++) L(pane, $"RoHead{c}", RoHeads[c], RoCols[c], 296, RoCols[c + 1] - 4, 328, CellPt, c == 1 || c == 9 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < TableRows; r++)
            {
                int row = r;
                float y0 = 332 + r * 50;
                roRows[r] = Row(pane, $"RoRow{r}", 12, y0, 1904, y0 + 46, RoCols, roCells, r, TablePt);
                roCells[r, 9].alignment = TextAnchor.MiddleLeft;
                roRows[r].onClick.AddListener(() => { if (row < roShown.Count) OpenPercentiles(roShown[row]); });
            }
        }

        private void BuildChemPane()
        {
            var pane = PaneRoot(PaneChem);
            CompyaUiKit.Box(pane, "ChemBg", 12, 248, 1908, 1036, PanelColor);
            chScore = L(pane, "ChScore", "", 24, 256, 600, 330, BigPt, TextAnchor.MiddleLeft, White);
            chMeta = L(pane, "ChMeta", "", 612, 256, 1896, 330, BannerPt - 1, TextAnchor.MiddleLeft, Muted);
            chMessages = L(pane, "ChMessages", "", 24, 340, 1896, 900, BodyPt + 1, TextAnchor.UpperLeft, new Color(1f, 0.66f, 0.5f));
            chMessages.lineSpacing = 1.2f;
            Btn(pane, "ChCheerButton", "치어리더 엔트리 · 전담 응원 관리", 24, 920, 940, 990, new Color(0.62f, 0.22f, 0.48f), ButtonPt).onClick.AddListener(OpenCheerEntry);
            Btn(pane, "ChSalaryButton", "주장 임명 · 보직 양보 인센티브 (연봉·재계약)", 952, 920, 1896, 990, ButtonIdle, ButtonPt).onClick.AddListener(() => { SelectMainTab(0); SelectSubTab(1); });
        }

        private static readonly float[] AbCols = { 16, 300, 520, 740, 920, 1140, 1300 };

        private void BuildAbsPane()
        {
            var pane = PaneRoot(PaneAbs);
            CompyaUiKit.Box(pane, "AbsBg", 12, 248, 1304, 1036, PanelColor);
            L(pane, "AbTitle", "ABS(자동 투구 판정 시스템) 시즌 분석 · 10구단 비교", 20, 252, 1296, 290, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            string[] heads = { "구단", "ABS 존 적응 지수", "보더라인 콜 획득", "루킹 삼진", "포수 블로킹 세이브", "볼넷 출루" };
            for (int c = 0; c < 6; c++) L(pane, $"AbHead{c}", heads[c], AbCols[c], 296, AbCols[c + 1] - 4, 330, CellPt, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < 10; r++)
            {
                float y0 = 334 + r * 40;
                for (int c = 0; c < 6; c++)
                    abCells[r, c] = L(pane, $"AbCell{r}_{c}", "", AbCols[c], y0, AbCols[c + 1] - 4, y0 + 36, BodyPt, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, White);
            }
            abNote = L(pane, "AbNote", "", 20, 744, 1296, 1030, BodyPt, TextAnchor.UpperLeft, Muted);
            abNote.lineSpacing = 1.15f;
            CompyaUiKit.Box(pane, "AbsTopBg", 1316, 248, 1908, 1036, PanelColor);
            L(pane, "AbMyTitle", "우리 구단 ABS 스페셜리스트", 1324, 252, 1900, 290, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int i = 0; i < abTop.Length; i++) abTop[i] = L(pane, $"AbTop{i}", "", 1324, 296 + i * 44, 1900, 336 + i * 44, BodyPt, TextAnchor.MiddleLeft, White);
            abLast = L(pane, "AbLast", "", 1324, 700, 1900, 1030, BodyPt, TextAnchor.UpperLeft, Gold);
            abLast.lineSpacing = 1.15f;
        }

        private void BuildCheerPane()
        {
            var pane = PaneRoot(PaneCheer);
            CompyaUiKit.Box(pane, "CheerBg", 12, 248, 1908, 1036, PanelColor);
            L(pane, "CeTitle", "응원단 (구단 15인 / 경기 엔트리 4~6인)", 24, 252, 1896, 290, PanelTitlePt, TextAnchor.MiddleLeft, new Color(1f, 0.6f, 0.82f));
            ceEntry = L(pane, "CeEntry", "", 24, 296, 940, 610, BodyPt, TextAnchor.UpperLeft, White);
            ceEntry.lineSpacing = 1.15f;
            ceEffects = L(pane, "CeEffects", "", 952, 296, 1896, 610, BodyPt, TextAnchor.UpperLeft, Gold);
            ceEffects.lineSpacing = 1.15f;
            Btn(pane, "CeOpenEntry", "15인 풀 · 엔트리 · 전담 응원 관리 열기", 24, 620, 632, 690, new Color(0.62f, 0.22f, 0.48f), ButtonPt).onClick.AddListener(OpenCheerEntry);
            Btn(pane, "CeOpenShop", "치어리더 영입 (상점)", 644, 620, 1264, 690, ButtonIdle, ButtonPt).onClick.AddListener(() => GoLegacy(ScreenType.CheerleaderShop));
            Btn(pane, "CeOpenInventory", "보유 치어리더 · 성장", 1276, 620, 1896, 690, ButtonIdle, ButtonPt).onClick.AddListener(() => GoLegacy(ScreenType.CheerleaderInventory));
            cePool = L(pane, "CePool", "", 24, 700, 1896, 1030, CellPt, TextAnchor.UpperLeft, Muted);
            cePool.lineSpacing = 1.1f;
        }

        private static readonly float[] HiCols = { 16, 140, 420, 540, 640, 820, 1100 };

        private void BuildHistoryPane()
        {
            var pane = PaneRoot(PaneHistory);
            CompyaUiKit.Box(pane, "HistBg", 12, 248, 1104, 1036, PanelColor);
            L(pane, "HiTitle", "구단 역사 (시즌별 성적 · 관중)", 20, 252, 1096, 290, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            string[] heads = { "연도", "성적", "승률", "순위", "관중/경기", "비고" };
            for (int c = 0; c < 6; c++) L(pane, $"HiHead{c}", heads[c], HiCols[c], 296, HiCols[c + 1] - 4, 330, CellPt, TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < 12; r++)
            {
                float y0 = 334 + r * 44;
                for (int c = 0; c < 6; c++) hiCells[r, c] = L(pane, $"HiCell{r}_{c}", "", HiCols[c], y0, HiCols[c + 1] - 4, y0 + 40, BodyPt, TextAnchor.MiddleCenter, White);
            }
            hiEnding = L(pane, "HiEnding", "", 20, 870, 1096, 1030, BodyPt, TextAnchor.UpperLeft, Gold);
            hiEnding.lineSpacing = 1.12f;
            CompyaUiKit.Box(pane, "AwardBg", 1116, 248, 1908, 1036, PanelColor);
            L(pane, "HiAwardsTitle", "역대 시즌 수상 (우리 구단)", 1124, 252, 1900, 290, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            hiAwards = L(pane, "HiAwards", "", 1124, 296, 1900, 860, BodyPt, TextAnchor.UpperLeft, White);
            hiAwards.lineSpacing = 1.12f;
            Btn(pane, "HiOpenAwards", "시상식 & 포스트시즌 열기", 1124, 880, 1900, 950, ButtonOn, ButtonPt).onClick.AddListener(OpenAwards);
        }

        // ------------------------------------------------------------------ 건의 팝업

        private void BuildDiscussPopup()
        {
            discussPopup = CompyaUiKit.Fill(root, "DiscussPopup");
            CompyaUiKit.Paint(discussPopup, new Color(0f, 0f, 0f, 0.72f), true);
            CompyaUiKit.Box(discussPopup, "DiscussBox", 560, 300, 1360, 780, new Color(0.13f, 0.13f, 0.16f, 0.98f));
            discussTitle = L(discussPopup, "DiscussTitle", "", 600, 320, 1320, 362, PanelTitlePt + 2, TextAnchor.MiddleLeft, Gold);
            discussBody = L(discussPopup, "DiscussBody", "", 600, 368, 1320, 500, BodyPt, TextAnchor.UpperLeft, White);
            discussBody.lineSpacing = 1.15f;
            Btn(discussPopup, "DiscussRelax", $"목표 완화 요청 (신임도 -{GMFrontOffice.DiscussTrustCost})", 600, 510, 1320, 560, new Color(0.25f, 0.27f, 0.36f), ButtonPt).onClick.AddListener(() => Discuss(GMDiscussOption.RelaxGoal));
            Btn(discussPopup, "DiscussBudget", $"추가 예산 요청 · 샐러리캡 {GMFrontOffice.ExtraBudgetPercent}% (신임도 -{GMFrontOffice.ExtraBudgetTrustCost})", 600, 570, 1320, 620, new Color(0.25f, 0.27f, 0.36f), ButtonPt).onClick.AddListener(() => Discuss(GMDiscussOption.ExtraBudget));
            Btn(discussPopup, "DiscussTrade", $"트레이드 한도 +2 해제 (신임도 -{GMFrontOffice.LiftTradeTrustCost})", 600, 630, 1320, 680, new Color(0.25f, 0.27f, 0.36f), ButtonPt).onClick.AddListener(() => Discuss(GMDiscussOption.LiftTradeLimit));
            Btn(discussPopup, "DiscussCancel", "취소", 600, 700, 1320, 760, ButtonIdle, ButtonPt).onClick.AddListener(() => discussPopup.gameObject.SetActive(false));
            discussPopup.gameObject.SetActive(false);
        }

        public void OpenDiscuss(int row)
        {
            var goals = League != null ? GMFrontOffice.Ensure(League).Goals : null;
            if (goals == null || row >= goals.Count) return;
            discussGoal = goals[row];
            var fo = GMFrontOffice.Ensure(League);
            discussTitle.text = $"구단주 건의 - {discussGoal.Category}";
            discussBody.text = $"{discussGoal.Description}\n진행: {discussGoal.Progress}\n{fo.Owner.OwnerName} · 기분 {GMFrontOffice.MoodLabel(fo.OwnerTrust)} · 신임도 {fo.OwnerTrust}\n신임도를 쓰면 목표 완화 · 예산 확보가 가능하지만, 신임도가 바닥나면 해임 위험이 커집니다.";
            discussPopup.gameObject.SetActive(true);
            discussPopup.SetAsLastSibling();
        }

        public bool Discuss(GMDiscussOption option)
        {
            if (League == null || discussGoal == null) return false;
            bool ok = GMFrontOffice.Discuss(League, discussGoal, option, out string message);
            discussPopup.gameObject.SetActive(false);
            SetStatus(message);
            Refresh();
            return ok;
        }

        // ================================================================== 갱신

        private void SetStatus(string text)
        {
            if (statusLine != null) statusLine.text = text ?? "";
        }

        private void ApplyTheme()
        {
            var team = UserTeam != null ? UserTeam.Team : Team.Samsung;
            if (banner != null) banner.color = CompyaUiKit.Darken(TeamThemePalette.Primary(team), 0.7f);
            CompyaUiKit.SetLogo(teamLogo, team, 1f);
        }

        public void Refresh()
        {
            if (root == null) return;
            if (followGameManager && GameManager.Instance != null && GameManager.Instance.GMLeague != null && GameManager.Instance.GMSimulator != simulator)
            {
                simulator = GameManager.Instance.GMSimulator;
                ResetSelections();
                ApplyTheme();
            }
            RefreshTabs();
            RefreshHeader();
            RefreshToolbar(); // [TASK-GM-07]
            if (League == null || UserTeam == null) { SetStatus("단장 모드 리그가 없습니다 - [새 시즌/난이도 설정]으로 시작하십시오."); return; }
            GMFrontOffice.RefreshGoals(League);
            PlayFrontOfficeBgm(); // [TASK-GM-08] 구단 테마 BGM
            using (CompyaUiKit.Wide())
            {
                switch (currentPane)
                {
                    case PaneOwner: RefreshOwner(); break;
                    case PaneSalaries: RefreshSalaries(); break;
                    case PaneFA: RefreshFA(); break;
                    case PaneTrade: RefreshTrade(); break;
                    case PaneDraft: RefreshDraft(); break;
                    case PaneStory: RefreshStory(); break;
                    case PaneLive: RefreshLive(); break;
                    case PaneRoster: RefreshRoster(); break;
                    case PaneChem: RefreshChem(); break;
                    case PaneAbs: RefreshAbs(); break;
                    case PaneCheer: RefreshCheer(); break;
                    case PaneHistory: RefreshHistory(); break;
                    case PaneSchedule: RefreshSchedule(); break;      // [TASK-GM-07]
                    case PanePostseason: RefreshPostseason(); break;  // [TASK-GM-07]
                    case PaneProtect: RefreshProtect(); break;        // [TASK-GM-08]
                }
            }
        }

        private void RefreshHeader()
        {
            var league = League;
            var team = UserTeam;
            if (league == null || team == null)
            {
                teamName.text = "스토브리그: 단장의 시간";
                teamRecord.text = teamDate.text = "";
                foreach (var t in tickerValues) t.text = "-";
                return;
            }
            var rec = league.RecordOf(team.TeamCode);
            int rank = GMFrontOffice.RankOf(league, team.TeamCode);
            var leader = simulator.Standings().FirstOrDefault();
            string gb = leader != null && rec.G > 0 ? GMLiveSeasonSimulator.GamesBehindLabel(GMLiveSeasonSimulator.GamesBehind(leader, rec)) : "-";
            teamName.text = NameAliasTable.DisplayTeamName(team.TeamCode);
            teamRecord.text = rec.G > 0 ? $"{rec.W}승 {rec.D}무 {rec.L}패 · 승률 {GMTeamRecord.PctLabel(rec.Pct)} · 게임차 {gb} · {rank}위"
                                        : $"0승 0무 0패 · 승률 .000 · 게임차 - · 지난 시즌 {GMFrontOffice.LastRank(league, team.TeamCode)}위";
            bool stove = simulator.GamesPlayed == 0;
            teamDate.text = stove ? $"{league.SeasonYear}년 스토브리그 · {GMModeLabel(league.Mode)} · 난이도 {GMFrontOffice.DifficultyLabel(GMFrontOffice.Ensure(league).Difficulty)}"
                                  : simulator.IsSeasonComplete ? $"{league.SeasonYear} 정규시즌 종료 · 포스트시즌 & 시상식" : $"{simulator.DayLabel(simulator.GamesPlayed)} · {league.SeasonYear} 정규시즌 G {simulator.GamesPlayed}/{GMLiveSeasonSimulator.SeasonGames}";
            tickerValues[0].text = string.IsNullOrEmpty(simulator.LastUserGameLine) ? (stove ? "스토브리그 - 경기 없음" : "-") : simulator.LastUserGameLine;
            tickerValues[1].text = MatchupLabel(simulator.GamesPlayed, true);
            tickerValues[2].text = MatchupLabel(simulator.GamesPlayed + 1, false);
            tickerValues[3].text = NextSeriesLabel();
            // [TASK-GM-07] 메인 홈이 아니면 [진행하기] = 메인 홈 이동, 메인 홈이면 ① 전력 분석부터 3단계 플로우
            string next = simulator.IsSeasonComplete ? "포스트시즌 트리" : !IsAtMainHome ? "메인 홈으로" : stove ? "개막전 전력 분석" : "오늘 경기 전력 분석";
            CompyaUiKit.SetButtonText(continueButton, $"✔ 진행하기 (CONTINUE) >\n{next}");
        }

        private static string GMModeLabel(GMStartMode m) => m == GMStartMode.AllTimeDream ? "올타임 드림" : m == GMStartMode.StoryCampaign ? "스토리 「꼴찌 구단의 겨울」" : "2026 현역";

        private string MatchupLabel(int day, bool today)
        {
            if (simulator == null || day >= GMLiveSeasonSimulator.SeasonGames) return today && simulator != null && simulator.IsSeasonComplete ? "포스트시즌" : "-";
            string me = League.SelectedTeamCode;
            var m = simulator.MatchesOn(day).FirstOrDefault(x => x.home == me || x.away == me);
            if (m.home == null) return "휴식일";
            string opp = m.home == me ? m.away : m.home;
            var oppTeam = League.Teams[opp];
            var starter = StartingRotation.PickFor(oppTeam.AvailableRoster, day, oppTeam.Lineup);
            return $"{simulator.DayLabel(day)} {(m.home == me ? "vs" : "@")} {CompyaUiKit.ShortName(NameAliasTable.ToTeam(opp))}" + (starter != null ? $" - 선발 {starter.Template.PlayerName}" : "");
        }

        private string NextSeriesLabel()
        {
            if (simulator == null) return "-";
            string me = League.SelectedTeamCode;
            string current = null;
            for (int d = simulator.GamesPlayed; d < GMLiveSeasonSimulator.SeasonGames; d++)
            {
                var m = simulator.MatchesOn(d).FirstOrDefault(x => x.home == me || x.away == me);
                if (m.home == null) continue;
                string opp = m.home == me ? m.away : m.home;
                if (current == null) { current = opp; continue; }
                if (opp != current) return $"{simulator.DayLabel(d)}~ {(m.home == me ? "홈" : "원정")} {CompyaUiKit.ShortName(NameAliasTable.ToTeam(opp))} 시리즈";
            }
            return "-";
        }

        // ---- 프런트 오피스

        private void RefreshOwner()
        {
            var league = League;
            var team = UserTeam;
            var fo = GMFrontOffice.Ensure(league);
            var o = fo.Owner;
            var report = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff);
            string[] values =
            {
                $"{o.Company} · {o.OwnerName}", GMFrontOffice.PatienceLabel(o.Patience), GMFrontOffice.FiscalLabel(o.Fiscal), GMFrontOffice.InvolvementLabel(o.Involvement),
                GMFrontOffice.PriorityLabel(o.Priority), GMFrontOffice.MoodLabel(fo.OwnerTrust), GMFrontOffice.ExpectationLabel(o, league.Mode), $"{fo.OwnerTrust} / 100",
                $"{report.TeamworkScore} ({TeamChemistryEngine.MoraleLabel(report.MoraleState)} · 실효 전력 x{report.EffectivePowerMultiplier:0.00})",
                PenaltySummary(report),
            };
            for (int k = 0; k < 10; k++)
            {
                ownerValues[k].text = values[k];
                ownerValues[k].color = k == 1 ? (o.Patience == 0 ? RedBad : GreenOk) : k == 5 ? (GMFrontOffice.MoodIsGood(fo.OwnerTrust) ? GreenOk : RedBad)
                                     : k == 9 ? (report.ActivePenalties == AllStarOverloadPenalty.None ? GreenOk : RedBad) : k == 6 ? GreenOk : White;
            }
            goalsTrust.text = $"구단주 신임도 {fo.OwnerTrust} · [건의] 시 신임도 소모";
            for (int r = 0; r < GoalRows; r++)
            {
                var g = r < fo.Goals.Count ? fo.Goals[r] : null;
                for (int c = 0; c < 6; c++) goalCells[r, c].text = "";
                goalDiscuss[r].gameObject.SetActive(g != null && GMFrontOffice.CanDiscuss(g));
                if (g == null) continue;
                goalCells[r, 0].text = g.YearIssued.ToString();
                goalCells[r, 1].text = g.TargetYear.ToString();
                goalCells[r, 2].text = GMFrontOffice.PriorityText(g.Priority);
                goalCells[r, 2].color = g.Priority >= GMGoalPriority.VeryHigh ? BlueLink : g.Priority == GMGoalPriority.High ? new Color(0.3f, 0.85f, 0.85f) : g.Priority == GMGoalPriority.Average ? GreenOk : new Color(1f, 0.6f, 0.2f);
                goalCells[r, 3].text = g.Category;
                goalCells[r, 4].text = g.Description;
                goalCells[r, 5].text = g.Progress;
                goalCells[r, 5].color = g.OnTrack ? GreenOk : RedBad;
            }

            var bars = GMFrontOffice.RecordBars(league, team.TeamCode, HistoryBars);
            for (int i = 0; i < HistoryBars; i++)
            {
                bool used = i < bars.Count;
                recBars[i].gameObject.SetActive(used);
                recVals[i].gameObject.SetActive(used);
                recYears[i].gameObject.SetActive(used);
                if (!used) continue;
                var (year, pct, current) = bars[i];
                float sx = 34 + i * 43;
                float h = Mathf.Clamp01((float)pct / 0.8f) * 230f;
                CompyaUiKit.SetBox((RectTransform)recBars[i].transform, sx + 7, 960 - Mathf.Max(2f, h), sx + 36, 960);
                CompyaUiKit.SetBox((RectTransform)recVals[i].transform, sx, 960 - h - 24, sx + 43, 960 - h - 2);
                recBars[i].color = pct >= 0.5 ? BarGreen : BarRed;
                recVals[i].text = GMTeamRecord.PctLabel(pct);
                recYears[i].text = current ? $"{year}*" : year.ToString();
            }

            var cost = GMFrontOffice.CostEfficient(league, team, CostRows);
            for (int r = 0; r < CostRows; r++)
            {
                bool used = r < cost.Count;
                for (int c = 0; c < 4; c++) costCells[r, c].text = "";
                if (!used) continue;
                var (p, war, perWar) = cost[r];
                costCells[r, 0].text = $"{r + 1}) {GMFrontOffice.PositionLabel(p.Position)} {p.Template.PlayerName}";
                costCells[r, 1].text = war.ToString("0.0");
                costCells[r, 2].text = GMDiagnosticFormat.Short(p.Salary);
                costCells[r, 3].text = GMDiagnosticFormat.Short(perWar);
            }

            var b = GMFrontOffice.Budget(league, team);
            long[] money = { b.ProjectedBudget, b.GuaranteedPayroll, b.ReSignEstimate, b.CheerRevenue, b.OtherExpenses, b.TotalExpenses, b.MoneyForFA, b.MoneyForExtensions };
            for (int k = 0; k < BudgetRows; k++)
            {
                budgetValues[k].text = GMDiagnosticFormat.Won(money[k]);
                budgetValues[k].color = k == 0 || k == 3 ? GreenOk : k == 5 ? (b.OverBudget ? RedBad : White) : k >= 6 ? (money[k] < 0 ? RedBad : GreenOk) : White;
            }

            var att = GMFrontOffice.AttendanceBars(league, team.TeamCode, HistoryBars);
            int max = Math.Max(1, att.Max(a => a.attendance));
            for (int i = 0; i < HistoryBars; i++)
            {
                bool used = i < att.Count;
                attBars[i].gameObject.SetActive(used);
                attYears[i].gameObject.SetActive(used);
                if (!used) continue;
                float sx = 1460 + i * 43;
                float h = Mathf.Max(4f, att[i].attendance / (float)max * 150f);
                CompyaUiKit.SetBox((RectTransform)attBars[i].transform, sx + 7, 880 - h, sx + 36, 880);
                attBars[i].color = att[i].current ? new Color(0.45f, 0.55f, 0.85f) : BarBlue;
                attYears[i].text = att[i].current ? $"{att[i].year}*" : att[i].year.ToString();
            }
            attNow.text = $"올해 예상 {att.Last().attendance:N0}명/경기 · 최고 {max:N0}명 · 팬 지지율 {team.FanSupport}";
            var entry = GMCheerleaderRoster.Entry(team);
            attCheer.text = $"{GMCheerleaderRules.Summary(entry.Count, team.CheerleaderPool.Count)}\n" +
                            $"출전: {string.Join(" · ", entry.Select(c => c.DisplayName))}\n" +
                            $"홈 흥행 +{GMCheerleaderRoster.HomeRevenue(entry):N0}만 원/경기 · 자동 로테이션 {(team.CheerAutoRotate ? "켬" : "끔")}\n" +
                            GMCheerleaderRoster.DedicationLabel(team);
        }

        private static string PenaltySummary(TeamChemistryReport report)
        {
            if (report.ActivePenalties == AllStarOverloadPenalty.None) return "부작용 없음";
            var names = new List<string>();
            if (report.Has(AllStarOverloadPenalty.AlphaDogFactionSplit)) names.Add("파벌 분열");
            if (report.Has(AllStarOverloadPenalty.LineupRoleConflict)) names.Add("보직 충돌");
            if (report.Has(AllStarOverloadPenalty.HeroBallDoublePlay)) names.Add("Hero Ball");
            if (report.Has(AllStarOverloadPenalty.DefenseImbalance)) names.Add("센터라인 붕괴");
            if (report.Has(AllStarOverloadPenalty.PayrollDepthCollapse)) names.Add("뎁스 붕괴");
            if (report.Has(AllStarOverloadPenalty.UnderdogUpsetVulnerability)) names.Add("방심");
            return string.Join(" · ", names);
        }

        // ---- 연봉 · 재계약

        public void SortSalaries(string key)
        {
            salDesc = salSort == key ? !salDesc : key != "POS" && key != "AGE";
            salSort = key;
            salPageIndex = 0;
            Refresh();
        }

        private void RefreshSalaries()
        {
            var team = UserTeam;
            var diff = GMFrontOffice.Ensure(League).Difficulty;
            var sorted = GMStoveLeagueMarket.SortRoster(team.Roster, salSort, salDesc);
            int pages = Math.Max(1, (sorted.Count + TableRows - 1) / TableRows);
            salPageIndex = Mathf.Clamp(salPageIndex, 0, pages - 1);
            salShown = sorted.Skip(salPageIndex * TableRows).Take(TableRows).ToList();
            salPage.text = $"{salPageIndex + 1}/{pages}";
            for (int r = 0; r < TableRows; r++)
            {
                var p = r < salShown.Count ? salShown[r] : null;
                salRows[r].gameObject.SetActive(p != null);
                if (p == null) continue;
                string[] v =
                {
                    GMFrontOffice.PositionLabel(p.Position), p.Template.PlayerName, p.Age.ToString(), $"{p.BaseOverall}/{p.Potential}",
                    $"{GMStoveLeagueMarket.RoleLabel(p.RoleArchetype)}({p.EgoLevel})", GMDiagnosticFormat.Short(p.Salary), $"{p.ContractYears}년",
                    GMStoveLeagueMarket.DemandLabel(p, diff, false), StatusTags(p),
                };
                for (int c = 0; c < 9; c++) salCells[r, c].text = v[c];
                salCells[r, 6].color = p.ContractYears <= 1 ? RedBad : White;
                salRows[r].targetGraphic.color = p == extSelected ? RowSelected : r % 2 == 0 ? RowIdle : RowAlt;
            }
            RefreshExtensionPanel();
        }

        private static string StatusTags(Player p)
        {
            var tags = new List<string>();
            if (p.IsCaptain) tags.Add("주장");
            if (GMStoveLeagueMarket.IsExtensionTarget(p)) tags.Add("재계약 대상");
            if (p.HasRoleConcessionBonus) tags.Add("보직 양보");
            if (p.InjuryRemainingDays > 0) tags.Add($"부상 {p.InjuryRemainingDays}일");
            if (p.PersonalMorale < 50) tags.Add("불만");
            return tags.Count > 0 ? string.Join(" · ", tags) : "-";
        }

        public void SelectSalaryRow(int row)
        {
            if (row >= salShown.Count) return;
            SelectExtension(salShown[row]);
        }

        /// <summary>재계약 대상 선택 - 기간은 선호 기간, 연봉 슬라이더는 요구액(중앙)으로 맞춘다.</summary>
        public void SelectExtension(Player p)
        {
            extSelected = p;
            if (p != null)
            {
                extYearsValue = GMStoveLeagueMarket.PreferredYears(p);
                extConcessionOn = false;
                if (extSlider != null) extSlider.SetValueWithoutNotify(0.5f);
            }
            Refresh();
        }

        /// <summary>슬라이더 0~1 → 요구액 70% ~ 150%.</summary>
        public static int SliderSalary(int demand, float t) => Math.Max(Player.MinSalary, Math.Min(Player.MaxSalary, (int)Math.Round(demand * Mathf.Lerp(0.7f, 1.5f, Mathf.Clamp01(t)) / 100.0) * 100));
        public static float SliderFor(int demand, int salary) => demand <= 0 ? 0.5f : Mathf.InverseLerp(0.7f, 1.5f, salary / (float)demand);

        private int ExtensionDemand => extSelected != null ? GMStoveLeagueMarket.ExtensionDemand(extSelected, GMFrontOffice.Ensure(League).Difficulty) : 0;
        public int ExtensionOfferSalary => extSelected != null ? SliderSalary(ExtensionDemand, extSlider.value) : 0;

        /// <summary>테스트 · 단축 - 제시 조건을 직접 넣는다.</summary>
        public void SetExtensionTerms(int years, int salary, bool concession)
        {
            extYearsValue = Mathf.Clamp(years, 1, Player.MaxContractYears);
            extConcessionOn = concession;
            if (extSelected != null) extSlider.SetValueWithoutNotify(SliderFor(ExtensionDemand, salary));
            RefreshExtensionPanel();
        }

        private void RefreshExtensionPanel()
        {
            var p = extSelected;
            var team = UserTeam;
            var report = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff);
            extTeamwork.text = $"팀워크 {report.TeamworkScore} · 실효 전력 x{report.EffectivePowerMultiplier:0.00} · {PenaltySummary(report)}";
            for (int k = 0; k < 5; k++) extYears[k].targetGraphic.color = k + 1 == extYearsValue ? ButtonOn : ButtonIdle;
            CompyaUiKit.SetButtonText(extConcession, $"[{(extConcessionOn ? "V" : " ")}] 보직 양보 인센티브 (스타 불만 즉시 해소)");
            extConcession.targetGraphic.color = extConcessionOn ? new Color(0.2f, 0.5f, 0.3f) : ButtonIdle;
            bool has = p != null && team.Roster.Contains(p);
            extSubmit.interactable = extCaptain.interactable = extRelease.interactable = has;
            if (!has)
            {
                extPlayer.text = "선수를 선택하십시오 (계약 만료 = 잔여 1년 이하 빨강)";
                extInfo.text = "";
                extSalaryLabel.text = "";
                return;
            }
            extPlayer.text = $"{GMFrontOffice.PositionLabel(p.Position)} {p.Template.PlayerName} · {p.Age}세 · OVR {p.BaseOverall}/{p.Potential}{(p.IsCaptain ? " · 주장" : "")}";
            extInfo.text = $"현재 연봉 {GMDiagnosticFormat.Won(p.Salary)} · 잔여 {p.ContractYears}년 · 만족도 {p.PersonalMorale} · {GMStoveLeagueMarket.RoleLabel(p.RoleArchetype)}(Ego {p.EgoLevel})\n" +
                           $"요구 조건: {GMDiagnosticFormat.Won(ExtensionDemand)} × {GMStoveLeagueMarket.PreferredYears(p)}년{(GMStoveLeagueMarket.IsExtensionTarget(p) ? " · 재계약 대상" : " · 연장 협상")}";
            RefreshExtensionSalaryLabel();
        }

        private void RefreshExtensionSalaryLabel()
        {
            if (extSelected == null || extSalaryLabel == null) return;
            int s = ExtensionOfferSalary, d = Math.Max(1, ExtensionDemand);
            extSalaryLabel.text = $"제시 연봉 {GMDiagnosticFormat.Won(s)} (요구 대비 {s * 100 / d}%) · {extYearsValue}년 총액 {GMDiagnosticFormat.Won((long)s * extYearsValue)}";
        }

        public GMNegotiationResult SubmitExtension()
        {
            if (extSelected == null) return null;
            var r = GMStoveLeagueMarket.Extend(League, UserTeam, extSelected, extYearsValue, ExtensionOfferSalary, extConcessionOn);
            extMessage.text = r.Message;
            SetStatus(r.Message);
            Refresh();
            return r;
        }

        public GMNegotiationResult AppointCaptainSelected()
        {
            if (extSelected == null) return null;
            var r = GMStoveLeagueMarket.AppointCaptain(League, UserTeam, extSelected);
            extMessage.text = r.Message;
            SetStatus(r.Message);
            Refresh();
            return r;
        }

        public GMNegotiationResult ReleaseSelected()
        {
            if (extSelected == null) return null;
            var r = GMStoveLeagueMarket.Release(League, UserTeam, extSelected);
            extMessage.text = r.Message;
            SetStatus(r.Message);
            if (r.Success) extSelected = null;
            Refresh();
            return r;
        }

        // ---- FA

        private void RefreshFA()
        {
            var league = League;
            var diff = GMFrontOffice.Ensure(league).Difficulty;
            var market = GMStoveLeagueMarket.FAMarket(league);
            switch (faSort)
            {
                case "POT": market = market.OrderByDescending(p => p.IsScouted ? p.Potential : p.BaseOverall).ToList(); break;
                case "AGE": market = market.OrderBy(p => p.Age).ToList(); break;
                case "DEM": market = market.OrderBy(p => GMStoveLeagueMarket.FADemand(p, diff)).ToList(); break;
                case "POS": market = market.OrderBy(GMStoveLeagueMarket.PositionOrder).ToList(); break;
                default: market = market.OrderByDescending(p => p.BaseOverall).ToList(); break;
            }
            faShown = market;
            for (int r = 0; r < FARows; r++)
            {
                var p = r < faShown.Count ? faShown[r] : null;
                faRows[r].gameObject.SetActive(p != null);
                if (p == null) continue;
                string[] v =
                {
                    GMFrontOffice.PositionLabel(p.Position), p.Template.PlayerName, p.Age.ToString(), GMStoveLeagueMarket.OvrLabel(p),
                    p.IsScouted ? $"{GMStoveLeagueMarket.RoleLabel(p.RoleArchetype)}({p.EgoLevel})" : "?", p.IsScouted ? p.ABSZoneSkill.ToString() : "?",
                    GMStoveLeagueMarket.DemandLabel(p, diff, true), p.IsScouted ? "리포트 열람 완료" : "미열람",
                };
                for (int c = 0; c < 8; c++) faCells[r, c].text = v[c];
                faCells[r, 7].color = p.IsScouted ? GreenOk : Muted;
                faRows[r].targetGraphic.color = p == faSelected ? RowSelected : r % 2 == 0 ? RowIdle : RowAlt;
            }
            RefreshFAPanel();
        }

        public void SelectFARow(int row)
        {
            if (row >= faShown.Count) return;
            SelectFreeAgent(faShown[row]);
        }

        public void SelectFreeAgent(Player p)
        {
            faSelected = p;
            if (p != null)
            {
                faYearsValue = Math.Min(GMStoveLeagueMarket.MaxFAYears, GMStoveLeagueMarket.PreferredYears(p));
                faRoleOn = false;
                if (faSlider != null) faSlider.SetValueWithoutNotify(0.5f);
            }
            Refresh();
        }

        private int FADemand => faSelected != null ? GMStoveLeagueMarket.FADemand(faSelected, GMFrontOffice.Ensure(League).Difficulty) : 0;
        public int FAOfferSalary => faSelected != null ? SliderSalary(FADemand, faSlider.value) : 0;

        public void SetFATerms(int years, int salary, bool roleGuarantee)
        {
            faYearsValue = Mathf.Clamp(years, 1, GMStoveLeagueMarket.MaxFAYears);
            faRoleOn = roleGuarantee;
            if (faSelected != null) faSlider.SetValueWithoutNotify(SliderFor(FADemand, salary));
            RefreshFAPanel();
        }

        private void RefreshFAPanel()
        {
            var league = League;
            var team = UserTeam;
            var fo = GMFrontOffice.Ensure(league);
            var b = GMFrontOffice.Budget(league, team);
            faBudget.text = $"운영 자금 {GMDiagnosticFormat.Won(team.Budget)} · FA 가용 자금 {GMDiagnosticFormat.Won(b.MoneyForFA)}\n" +
                            $"로스터 {team.Roster.Count}/{GMStoveLeagueMarket.RosterMax}인 · 올해 FA {fo.FASigningsThisYear}/{GMFrontOffice.HouseRuleLabel(fo.HouseRuleMaxFA)}";
            for (int k = 0; k < faYears.Length; k++) faYears[k].targetGraphic.color = k + 1 == faYearsValue ? ButtonOn : ButtonIdle;
            CompyaUiKit.SetButtonText(faRole, $"[{(faRoleOn ? "V" : " ")}] 보직 보장 옵션 (주전 · 선발 로테이션 보장)");
            faRole.targetGraphic.color = faRoleOn ? new Color(0.2f, 0.5f, 0.3f) : ButtonIdle;
            var p = faSelected;
            bool has = p != null && league.FreeAgents.Contains(p);
            faOffer.interactable = faScout.interactable = has;
            if (!has)
            {
                faPlayer.text = "FA 선수를 선택하십시오";
                faInfo.text = faSalaryLabel.text = "";
                faCompensation.text = league.PendingCompensations.Count > 0 ? $"FA 보상 정산 대기 {league.PendingCompensations.Count}건 - [FA 보상·보호명단]에서 확인" : "";
                return;
            }
            faPlayer.text = $"{GMFrontOffice.PositionLabel(p.Position)} {p.Template.PlayerName} · {p.Age}세 · OVR {GMStoveLeagueMarket.OvrLabel(p)}";
            faInfo.text = p.IsScouted ? GMStoveLeagueMarket.ScoutReport(p) : "스카우팅 전 - OVR은 추정 범위, 잠재력 · 숨은 성향 · ABS 적응도는 비공개입니다.";
            faInfo.text += $"\n요구: {GMStoveLeagueMarket.DemandLabel(p, fo.Difficulty, true)}";
            faCompensation.text = GMFaCompensation.CompensationLabel(league, p, team.TeamCode); // [TASK-GM-08]
            RefreshFASalaryLabel();
        }

        private void RefreshFASalaryLabel()
        {
            if (faSelected == null || faSalaryLabel == null) return;
            int s = FAOfferSalary, d = Math.Max(1, FADemand);
            faSalaryLabel.text = $"제시 연봉 {GMDiagnosticFormat.Won(s)} (요구 대비 {s * 100 / d}%) · {faYearsValue}년 · 계약금 {GMDiagnosticFormat.Won((long)s * faYearsValue * GMStoveLeagueMarket.FABonusPercent / 100)}";
        }

        public bool ScoutSelected()
        {
            if (faSelected == null) return false;
            bool ok = GMStoveLeagueMarket.Scout(League, UserTeam, faSelected, out string message);
            faMessage.text = message;
            SetStatus(message);
            Refresh();
            return ok;
        }

        public GMNegotiationResult OfferSelected()
        {
            if (faSelected == null) return null;
            var r = GMStoveLeagueMarket.OfferContract(League, UserTeam, faSelected, faYearsValue, FAOfferSalary, faRoleOn);
            faMessage.text = r.Message;
            SetStatus(r.Message);
            if (r.Success) faSelected = null;
            Refresh();
            return r;
        }

        // ---- 트레이드

        private List<GMTeamState> PartnerTeams() => League == null ? new List<GMTeamState>() : NameAliasTable.CanonicalTeamCodes.Where(c => c != League.SelectedTeamCode && League.Teams.ContainsKey(c)).Select(c => League.Teams[c]).ToList();

        public void ShiftPartner(int delta)
        {
            int n = Math.Max(1, PartnerTeams().Count);
            partnerIndex = ((partnerIndex + delta) % n + n) % n;
            trTheirs.Clear();
            Refresh();
        }

        public void SetPartner(string teamCode)
        {
            int i = PartnerTeams().FindIndex(t => t.TeamCode == teamCode);
            if (i >= 0) { partnerIndex = i; trTheirs.Clear(); }
            Refresh();
        }

        private void RefreshTrade()
        {
            var team = UserTeam;
            var partner = TradePartner;
            var mine = GMStoveLeagueMarket.SortRoster(team.Roster, "OVR", true);
            int pages = Math.Max(1, (mine.Count + TableRows - 1) / TableRows);
            trMyPageIndex = Mathf.Clamp(trMyPageIndex, 0, pages - 1);
            trMyShown = mine.Skip(trMyPageIndex * TableRows).Take(TableRows).ToList();
            trMyPage.text = $"{trMyPageIndex + 1}/{pages}";
            trTheirShown = partner != null ? GMStoveLeagueMarket.SortRoster(partner.Roster, "OVR", true).Take(TableRows).ToList() : new List<Player>();
            trPartnerName.text = partner != null ? $"{partner.DisplayName} (상위 14인 · 클릭 = 받을 선수 · 역제안)" : "-";
            for (int r = 0; r < TableRows; r++)
            {
                var a = r < trMyShown.Count ? trMyShown[r] : null;
                trMyRows[r].gameObject.SetActive(a != null);
                if (a != null)
                {
                    CompyaUiKit.SetButtonText(trMyRows[r], PlayerLine(a));
                    trMyRows[r].targetGraphic.color = trMine.Contains(a) ? RowSelected : a == shopTarget ? new Color(0.5f, 0.4f, 0.12f, 0.8f) : r % 2 == 0 ? RowIdle : RowAlt;
                }
                var b = r < trTheirShown.Count ? trTheirShown[r] : null;
                trTheirRows[r].gameObject.SetActive(b != null);
                if (b != null)
                {
                    CompyaUiKit.SetButtonText(trTheirRows[r], PlayerLine(b));
                    trTheirRows[r].targetGraphic.color = trTheirs.Contains(b) ? RowSelected : r % 2 == 0 ? RowIdle : RowAlt;
                }
            }
            trMySlots.text = $"내줄 선수({trMine.Count}/{GMStoveLeagueMarket.MaxTradeSide}): {(trMine.Count > 0 ? string.Join(" · ", trMine.Select(p => $"{p.Template.PlayerName}(가치 {GMStoveLeagueMarket.TradeValue(p):0})")) : "-")}";
            trTheirSlots.text = $"받을 선수({trTheirs.Count}/{GMStoveLeagueMarket.MaxTradeSide}): {(trTheirs.Count > 0 ? string.Join(" · ", trTheirs.Select(p => $"{p.Template.PlayerName}(가치 {GMStoveLeagueMarket.TradeValue(p):0})")) : "-")}";
            var e = GMStoveLeagueMarket.Evaluate(League, team, trMine, partner, trTheirs, trCash); // [TASK-GM-08] 1:N · 연봉 보조
            RefreshTradeCash();
            trValueLabel.text = e.Required > 0 ? $"트레이드 가치 바: {e.Ratio * 100:0}% (100% 이상 = 상대 단장 수락)" : $"트레이드 가치 바: {e.Reason}";
            SetFill(trValueFill, e.Required > 0 ? Mathf.Clamp01(e.Ratio / 1.5f) : 0f);
            if (trValueFill != null) trValueFill.GetComponent<Image>().color = e.Acceptable ? BarGreen : BarRed;
            var fo = GMFrontOffice.Ensure(League);
            trNeeds.text = (e.Required > 0 ? e.NeedsNote : "구단 니즈 평가 대기") + $" · 올해 트레이드 {fo.TradesThisYear}/{GMFrontOffice.HouseRuleLabel(fo.HouseRuleMaxTrades > 0 ? fo.HouseRuleMaxTrades + fo.ExtraTradeAllowance : 0)}";
            for (int k = 0; k < 5; k++) trShopSorts[k].targetGraphic.color = (int)shopSort == k ? ButtonOn : ButtonIdle;
            for (int i = 0; i < ShopRows; i++)
            {
                var o = i < shopOffers.Count ? shopOffers[i] : null;
                trOffers[i].gameObject.SetActive(o != null);
                if (o == null) continue;
                CompyaUiKit.SetButtonText(trOffers[i], $"{CompyaUiKit.ShortName(NameAliasTable.ToTeam(o.TeamCode))} · {PlayerLine(o.Player)} · 체결 ▶");
                trOffers[i].targetGraphic.color = i % 2 == 0 ? RowIdle : RowAlt;
            }
        }

        private static string PlayerLine(Player p) =>
            $"{GMFrontOffice.PositionLabel(p.Position)} {p.Template.PlayerName} · OVR {p.BaseOverall}/{p.Potential} · {p.Age}세 · {GMDiagnosticFormat.Short(p.Salary)}";

        public void ToggleMine(int row)
        {
            if (row >= trMyShown.Count) return;
            var p = trMyShown[row];
            if (trMine.Contains(p)) trMine.Remove(p);
            else if (trMine.Count < GMStoveLeagueMarket.MaxTradeSide) trMine.Add(p);
            shopTarget = trMine.FirstOrDefault();
            Refresh();
        }

        public void ToggleTheirs(int row)
        {
            if (row >= trTheirShown.Count) return;
            var p = trTheirShown[row];
            if (trTheirs.Contains(p)) trTheirs.Remove(p);
            else if (trTheirs.Count < GMStoveLeagueMarket.MaxTradeSide) trTheirs.Add(p);
            // [TASK-GM-08] 내줄 선수 없이 상대 핵심 선수 1명을 고르면 AI 단장이 니즈 기반 1:N 역제안을 바로 띄운다
            if (trMine.Count == 0 && trTheirs.Count == 1 && trTheirs[0] == p) { RequestCounterOffer(); return; }
            Refresh();
        }

        /// <summary>테스트 · 단축 - 트레이드 슬롯을 직접 채운다.</summary>
        public void SetTradeSlots(IEnumerable<Player> mine, string partnerCode, IEnumerable<Player> theirs)
        {
            trMine.Clear(); trMine.AddRange((mine ?? Enumerable.Empty<Player>()).Take(GMStoveLeagueMarket.MaxTradeSide));
            shopTarget = trMine.FirstOrDefault();
            int i = PartnerTeams().FindIndex(t => t.TeamCode == partnerCode);
            if (i >= 0) partnerIndex = i;
            trTheirs.Clear(); trTheirs.AddRange((theirs ?? Enumerable.Empty<Player>()).Take(GMStoveLeagueMarket.MaxTradeSide));
            Refresh();
        }

        public GMNegotiationResult ProposeTrade()
        {
            var r = GMStoveLeagueMarket.ExecuteTrade(League, UserTeam, trMine.ToList(), TradePartner, trTheirs.ToList(), trCash);
            SetStatus(r.Message);
            if (r.Success) { trMine.Clear(); trTheirs.Clear(); shopOffers.Clear(); shopTarget = null; trCash = 0; }
            Refresh();
            return r;
        }

        public List<GMTradeOffer> ShopSelected()
        {
            shopTarget = trMine.FirstOrDefault() ?? shopTarget;
            if (shopTarget == null) { SetStatus("매물로 내놓을 내 선수를 먼저 고르십시오."); return shopOffers; }
            shopOffers = GMStoveLeagueMarket.ShopPlayer(League, UserTeam, shopTarget, shopSort);
            SetStatus($"{shopTarget.Template.PlayerName} 매물 등록 - {shopOffers.Count}개 구단이 교환 후보를 제시했습니다 ({GMStoveLeagueMarket.SortLabel(shopSort)} 순).");
            Refresh();
            return shopOffers;
        }

        public void SortShop(GMOfferSort sort)
        {
            shopSort = sort;
            shopOffers = GMStoveLeagueMarket.SortOffers(shopOffers, sort);
            Refresh();
        }

        public GMNegotiationResult AcceptShopOffer(int index)
        {
            if (index >= shopOffers.Count || shopTarget == null) return null;
            var r = GMStoveLeagueMarket.AcceptOffer(League, UserTeam, shopTarget, shopOffers[index]);
            SetStatus(r.Message);
            if (r.Success) { shopOffers.Clear(); trMine.Clear(); trTheirs.Clear(); shopTarget = null; }
            Refresh();
            return r;
        }

        // ---- 드래프트 · 백분위

        private void RefreshDraft()
        {
            var league = League;
            var fo = GMFrontOffice.Ensure(league);
            drTitle.text = $"{league.SeasonYear} 신인 드래프트 유망주 풀 ({league.DraftPool.Count}명) · 올해 지명 {fo.DraftPicksThisYear}/{GMStoveLeagueMarket.MaxDraftPicksPerYear}";
            for (int r = 0; r < DraftRows; r++)
            {
                var p = r < league.DraftPool.Count ? league.DraftPool[r] : null;
                drRows[r].gameObject.SetActive(p != null);
                if (p == null) continue;
                var top = GMStoveLeagueMarket.Percentiles(league, p).OrderByDescending(x => x.Percentile).First();
                CompyaUiKit.SetButtonText(drRows[r], $"{r + 1}. {p.Template.PlayerName} ({GMFrontOffice.PositionLabel(p.Position)} · {p.Age}세) OVR {p.BaseOverall} / 잠재력 {p.Potential}\n" +
                                                     $"스카우팅: {top.Label} 상위 {100 - top.Percentile}% · ABS {p.ABSZoneSkill} · 계약금 {GMDiagnosticFormat.Short(GMStoveLeagueMarket.RookieBonus(p))}");
                drRows[r].targetGraphic.color = p == drSelected ? RowSelected : r % 2 == 0 ? RowIdle : RowAlt;
            }
            if (drSelected != null && !league.DraftPool.Contains(drSelected)) drSelected = null;
            var sel = drSelected ?? league.DraftPool.FirstOrDefault();
            drPick.interactable = drSelected != null;
            if (sel == null)
            {
                drDetailTitle.text = "드래프트 풀이 비었습니다";
                for (int k = 0; k < PctRows; k++) { drPctLabels[k].text = drPctValues[k].text = ""; SetFill(drPctFills[k], 0f); }
                drNote.text = "";
                return;
            }
            drDetailTitle.text = $"백분위 랭킹 (Percentile) - {sel.Template.PlayerName}";
            var rows = GMStoveLeagueMarket.Percentiles(league, sel);
            for (int k = 0; k < PctRows; k++)
            {
                bool used = k < rows.Count;
                drPctLabels[k].text = used ? rows[k].Label : "";
                drPctValues[k].text = used ? $"{rows[k].Percentile}%" : "";
                SetFill(drPctFills[k], used ? rows[k].Percentile / 100f : 0f);
            }
            drNote.text = $"{GMStoveLeagueMarket.ScoutReport(sel)}\n신인 계약: 최저연봉 {GMDiagnosticFormat.Won(Player.MinSalary)} × {Player.MaxContractYears}년 · 계약금 {GMDiagnosticFormat.Won(GMStoveLeagueMarket.RookieBonus(sel))}\n" +
                          $"로스터 {UserTeam.Roster.Count}/{GMStoveLeagueMarket.RosterMax}인 - 가득 차면 [연봉·재계약]에서 방출 후 지명하십시오." + (drSelected == null ? "\n유망주를 클릭해 선택하면 [지명]이 활성화됩니다." : "");
        }

        public void SelectDraftRow(int row)
        {
            if (League == null || row >= League.DraftPool.Count) return;
            drSelected = League.DraftPool[row];
            Refresh();
        }

        public GMNegotiationResult DraftSelected_()
        {
            if (drSelected == null) return null;
            var r = GMStoveLeagueMarket.Draft(League, UserTeam, drSelected);
            SetStatus(r.Message);
            if (r.Success) drSelected = null;
            Refresh();
            return r;
        }

        /// <summary>선수 백분위 팝업(선수단 · 라인업 행 클릭).</summary>
        public void OpenPercentiles(Player p)
        {
            if (p == null || League == null) return;
            var rows = GMStoveLeagueMarket.Percentiles(League, p);
            pctTitle.text = $"백분위 랭킹 (Percentile Rankings) - {p.Template.PlayerName}";
            pctInfo.text = $"{GMFrontOffice.PositionLabel(p.Position)} · {p.Age}세 · OVR {p.BaseOverall}/{p.Potential} · ABS {p.ABSZoneSkill}({p.AbsRoleLabel}) · KBO 전체 {(p.IsPitcher ? "투수" : "타자")} 대비";
            for (int k = 0; k < PctRows; k++)
            {
                bool used = k < rows.Count;
                pctLabels[k].text = used ? rows[k].Label : "";
                pctValues[k].text = used ? $"{rows[k].Percentile}%" : "";
                SetFill(pctFills[k], used ? rows[k].Percentile / 100f : 0f);
            }
            pctPopup.gameObject.SetActive(true);
            pctPopup.SetAsLastSibling();
        }

        // ---- 스토리 안건

        private void RefreshStory()
        {
            var league = League;
            var team = UserTeam;
            var fo = GMFrontOffice.Ensure(league);
            agShown = GMFrontOffice.OpenAgendaStates(league).Take(AgendaBlocks).ToList();
            for (int i = 0; i < AgendaBlocks; i++)
            {
                var st = i < agShown.Count ? agShown[i] : null;
                var def = st != null ? GMFrontOffice.DefOf(st.Id) : null;
                agTitles[i].text = def != null ? $"안건 {i + 1}. {def.Title}" : i == 0 ? "남은 안건이 없습니다 - 결단 기록은 최신 소식에서 확인하십시오." : "";
                agBodies[i].text = def?.Body ?? "";
                for (int k = 0; k < 3; k++)
                {
                    bool used = def != null && k < def.Options.Length;
                    agOptions[i, k].gameObject.SetActive(used);
                    if (used) CompyaUiKit.SetButtonText(agOptions[i, k], OptionLabel(def.Options[k]));
                }
            }
            var report = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff);
            stInd[0].text = $"운영 자금: {GMDiagnosticFormat.Won(team.Budget)}";
            stInd[0].color = team.Budget < 0 ? RedBad : White;
            stInd[1].text = $"팬 지지율: {team.FanSupport} / 100";
            stInd[2].text = $"구단주 신임도: {fo.OwnerTrust} · {GMFrontOffice.MoodLabel(fo.OwnerTrust)}";
            stInd[2].color = GMFrontOffice.MoodIsGood(fo.OwnerTrust) ? GreenOk : RedBad;
            stInd[3].text = $"팀워크: {report.TeamworkScore} (안건 보정 {team.AgendaTeamworkBonus:+0;-0;0} · 응원단 +{team.CheerLeadershipBuff})";
            stInd[4].text = $"난이도 {GMFrontOffice.DifficultyLabel(fo.Difficulty)} · FA {GMFrontOffice.HouseRuleLabel(fo.HouseRuleMaxFA)} · 트레이드 {GMFrontOffice.HouseRuleLabel(fo.HouseRuleMaxTrades)}";
            var forecast = GMFrontOffice.ForecastEnding(league);
            stInd[5].text = fo.Ending != GMEnding.None ? $"{fo.EndingYear} 결과: {GMFrontOffice.EndingLabel(fo.Ending)}" : $"엔딩 전망: {GMFrontOffice.EndingLabel(forecast)}";
            stInd[5].color = Gold;
            stEnding.text = (fo.Ending != GMEnding.None ? fo.EndingNote + "\n\n" : "") +
                            "「꼴찌 구단의 겨울」 4종 엔딩\n① 기적의 가을야구 - 5위 이내 포스트시즌 진출\n② 성공적 리빌딩 - 가을야구 실패 · 신임도 35+ · 순위 상승 또는 평균 27세 이하\n" +
                            "③ 적자 해임 - 운영 자금 적자 · 신임도 50 미만\n④ 성적 부진 해임 - 그 외\n안건 선택지는 예산 · 팬 지지율 · 구단주 신임도 · 팀워크를 즉시 바꿉니다.";
        }

        private static string OptionLabel(GMFrontOffice.AgendaOption o)
        {
            var parts = new List<string>();
            if (o.Budget != 0) parts.Add($"예산 {(o.Budget > 0 ? "+" : "-")}{GMDiagnosticFormat.Short(Math.Abs(o.Budget))}");
            if (o.Trust != 0) parts.Add($"신임 {o.Trust:+0;-0}");
            if (o.Teamwork != 0) parts.Add($"팀워크 {o.Teamwork:+0;-0}");
            if (o.Fan != 0) parts.Add($"팬 {o.Fan:+0;-0}");
            return parts.Count > 0 ? $"{o.Label} ({string.Join(" · ", parts)})" : o.Label;
        }

        public bool ResolveAgendaBlock(int block, int option)
        {
            if (block >= agShown.Count) return false;
            bool ok = GMFrontOffice.ResolveAgenda(League, agShown[block].Id, option, out string message);
            SetStatus(message);
            Refresh();
            return ok;
        }

        // ---- 실시간 · 선수단 · 팀워크 · ABS · 응원단 · 역사

        private void RefreshLive()
        {
            var rows = simulator.Standings();
            var leader = rows.FirstOrDefault();
            for (int r = 0; r < 10; r++)
            {
                var rec = r < rows.Count ? rows[r] : null;
                bool mine = rec != null && rec.TeamCode == League.SelectedTeamCode;
                string[] v = rec == null ? new string[6] : new[]
                {
                    (r + 1).ToString(), NameAliasTable.DisplayTeamName(rec.TeamCode) + (mine ? " ★" : ""), $"{rec.W}-{rec.D}-{rec.L}", GMTeamRecord.PctLabel(rec.Pct),
                    leader != null ? GMLiveSeasonSimulator.GamesBehindLabel(GMLiveSeasonSimulator.GamesBehind(leader, rec)) : "-", rec.RecentLabel(5),
                };
                for (int c = 0; c < 6; c++) { lvCells[r, c].text = v[c] ?? ""; lvCells[r, c].color = mine ? Gold : White; }
            }
            var cats = new[] { GMLeaderCategory.AVG, GMLeaderCategory.HR, GMLeaderCategory.RBI, GMLeaderCategory.BatterWAR, GMLeaderCategory.ERA, GMLeaderCategory.Wins, GMLeaderCategory.Strikeouts, GMLeaderCategory.Saves };
            for (int i = 0; i < 8; i++)
            {
                var top = simulator.Leaders(cats[i], 1).FirstOrDefault();
                lvLeads[i].text = $"{GMLeaderCategories.Label(cats[i])}\n" + (top != null ? $"{top.Name}({CompyaUiKit.ShortName(NameAliasTable.ToTeam(top.TeamCode))}) {top.ValueLabel}" : "기록 집계 전");
            }
            var news = League.News.Take(6).ToList();
            for (int i = 0; i < 6; i++) lvNews[i].text = i < news.Count ? $"{news[i].DateLabel}  {(news[i].IsUserTeam ? "★ " : "")}{news[i].Title}" : "";
            lvInfo.text = simulator.IsSeasonComplete ? "정규시즌 144경기 종료 - [진행하기]로 포스트시즌 & 시상식을 확인하십시오."
                : $"G {simulator.GamesPlayed}/{GMLiveSeasonSimulator.SeasonGames} · 한 경기 | 전반기(→ 후반기) | 한 시즌(약 5분 실시간) 진행은 대시보드에서 합니다.";
        }

        private void RefreshRoster()
        {
            var team = UserTeam;
            var list = GMStoveLeagueMarket.SortRoster(team.Roster.Where(p => p.IsPitcher == roPitchers), roSort, roSort != "POS" && roSort != "AGE");
            int pages = Math.Max(1, (list.Count + TableRows - 1) / TableRows);
            roPageIndex = Mathf.Clamp(roPageIndex, 0, pages - 1);
            roShown = list.Skip(roPageIndex * TableRows).Take(TableRows).ToList();
            roPage.text = $"{roPageIndex + 1}/{pages}";
            for (int r = 0; r < TableRows; r++)
            {
                var p = r < roShown.Count ? roShown[r] : null;
                roRows[r].gameObject.SetActive(p != null);
                if (p == null) continue;
                string[] v =
                {
                    GMFrontOffice.PositionLabel(p.Position), p.Template.PlayerName, p.Age.ToString(), $"{p.BaseOverall}/{p.Potential}", p.ABSZoneSkill.ToString(),
                    $"{GMStoveLeagueMarket.RoleLabel(p.RoleArchetype)}({p.EgoLevel})", p.PersonalMorale.ToString(), GMDiagnosticFormat.Short(p.Salary), $"{p.ContractYears}년", StatusTags(p),
                };
                for (int c = 0; c < 10; c++) roCells[r, c].text = v[c];
                roCells[r, 6].color = p.PersonalMorale < 50 ? RedBad : White;
                roRows[r].targetGraphic.color = r % 2 == 0 ? RowIdle : RowAlt;
            }
        }

        private void RefreshChem()
        {
            var team = UserTeam;
            var report = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff);
            chScore.text = $"팀워크 {report.TeamworkScore}";
            chMeta.text = $"분위기 {TeamChemistryEngine.MoraleLabel(report.MoraleState)} · 실효 전력 x{report.EffectivePowerMultiplier:0.00} · 응원단 리더십 +{team.CheerLeadershipBuff} · 안건 보정 {team.AgendaTeamworkBonus:+0;-0;0}\n" +
                          $"주장: {team.Roster.FirstOrDefault(p => p.IsCaptain)?.Template.PlayerName ?? "미임명"} · {GMDiagnosticFormat.Won(team.Payroll)} / 샐러리캡 {GMDiagnosticFormat.Won(team.PayrollCap)}";
            chMessages.text = report.DiagnosticMessages.Count > 0 ? string.Join("\n", report.DiagnosticMessages)
                : "슈퍼스타 과밀 부작용이 없습니다. 주장 · 더그아웃 리더 · 살림꾼 균형이 좋습니다.";
        }

        private void RefreshAbs()
        {
            var league = League;
            var order = simulator.Standings().Select(r => r.TeamCode).ToList();
            if (order.Count == 0) order = league.Teams.Keys.ToList();
            for (int r = 0; r < 10; r++)
            {
                string code = r < order.Count ? order[r] : null;
                if (code == null || !league.Teams.TryGetValue(code, out var t)) { for (int c = 0; c < 6; c++) abCells[r, c].text = ""; continue; }
                var rec = league.RecordOf(code);
                bool mine = code == league.SelectedTeamCode;
                string[] v =
                {
                    NameAliasTable.DisplayTeamName(code) + (mine ? " ★" : ""), TeamAbsIndex(t).ToString(), rec.AbsBorderlineCalls.ToString("N0"), rec.AbsLookingStrikeouts.ToString("N0"),
                    rec.AbsBlockSaves.ToString("N0"), rec.AbsWalksDrawn.ToString("N0"),
                };
                for (int c = 0; c < 6; c++) { abCells[r, c].text = v[c]; abCells[r, c].color = mine ? Gold : White; }
            }
            var team = UserTeam;
            var lines = new List<string>();
            lines.AddRange(team.Pitchers.OrderByDescending(p => p.ABSZoneSkill).Take(3).Select(p => $"투수 {p.Template.PlayerName} - 보더라인 공략 {p.ABSZoneSkill}"));
            lines.AddRange(team.Batters.Where(p => p.Position != "C").OrderByDescending(p => p.ABSZoneSkill).Take(3).Select(p => $"타자 {p.Template.PlayerName} - 고정 존 선구안 {p.ABSZoneSkill}"));
            lines.AddRange(team.Batters.Where(p => p.Position == "C").OrderByDescending(p => p.ABSZoneSkill).Take(3).Select(p => $"포수 {p.Template.PlayerName} - 블로킹·도루저지 {p.ABSZoneSkill}"));
            for (int i = 0; i < abTop.Length; i++) abTop[i].text = i < lines.Count ? lines[i] : "";
            var last = league.LastUserMatchBoxScore;
            abLast.text = last == null ? "직전 경기 ABS 기록 없음 - 경기를 진행하면 보더라인 콜 · 블로킹 세이브가 집계됩니다."
                : $"직전 경기 ABS ({last.DateLabel})\n원정 {CompyaUiKit.ShortName(NameAliasTable.ToTeam(last.AwayCode))}: 보더라인 콜 {last.AwayAbsCalls} · 적응 지수 {last.AwayAbsIndex} · 블로킹 세이브 {last.AwayBlockSaves} · 루킹 삼진 {last.AwayLookingK}\n" +
                  $"홈 {CompyaUiKit.ShortName(NameAliasTable.ToTeam(last.HomeCode))}: 보더라인 콜 {last.HomeAbsCalls} · 적응 지수 {last.HomeAbsIndex} · 블로킹 세이브 {last.HomeBlockSaves} · 루킹 삼진 {last.HomeLookingK}";
            abNote.text = "ABS 엔진 반영(단장 모드 경기)\n· 투수 ABS 보더라인 공략력 ↑ → 루킹 삼진 · 삼진 확률 ↑, 볼넷 ↓\n· 타자 ABS 고정 존 선구안 ↑ → 볼넷 출루 ↑, 삼진 ↓\n" +
                          "· 포수는 프레이밍 대신 블로킹 · 도루저지 가치가 볼넷 · 안타를 억제(투수 실점 억제)\n· 존 적응 지수 = 선수단 ABSZoneSkill 평균(1~99)";
        }

        public static int TeamAbsIndex(GMTeamState t) => t.Roster.Count == 0 ? 50 : (int)Math.Round(t.Roster.Average(p => p.ABSZoneSkill));

        private void RefreshCheer()
        {
            var team = UserTeam;
            var entry = GMCheerleaderRoster.Entry(team);
            ceEntry.text = $"{GMCheerleaderRules.Summary(entry.Count, team.CheerleaderPool.Count)}\n" +
                           string.Join("\n", entry.Select((c, i) => $"{i + 1}.{CheerSquad.RoleName((CheerRole)i)} {c.DisplayName} · CHEER {GMCheerleaderStats.Cheer(c)} · 체력 {GMCheerleaderStats.Stamina(c)}{(GMCheerleaderStats.IsTired(c) ? "(효율 50%)" : "")}"));
            ceEffects.text = $"단장 리더십 → 팀워크 +{GMCheerleaderRoster.LeadershipTeamworkBonus(entry)}\n마운드 응원 → 실책 -{GMCheerleaderRoster.ErrorReduction(entry) * 100f:0}%\n" +
                             $"타격 응원 → 후반 클러치 +{GMCheerleaderRoster.ClutchBonus(entry) * 100f:0.0}%\n홈 흥행 → +{GMCheerleaderRoster.HomeRevenue(entry):N0}만 원/홈경기 · 팬 지지율 {team.FanSupport}\n" +
                             $"자동 로테이션: {(team.CheerAutoRotate ? "켬(체력 30 미만 자동 교체)" : "끔(수동)")}\n{GMCheerleaderRoster.DedicationLabel(team)}";
            var pool = team.CheerleaderPool.Select((c, i) => $"{c.DisplayName}({c.Grade.Display()} · 체력 {GMCheerleaderStats.Stamina(c)})").ToList();
            var lines = new List<string> { $"구단 응원단 풀 {pool.Count}/{GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX}명" };
            for (int i = 0; i < pool.Count; i += 3) lines.Add(string.Join("   ", pool.Skip(i).Take(3)));
            cePool.text = string.Join("\n", lines);
        }

        private void RefreshHistory()
        {
            var league = League;
            var team = UserTeam;
            var hist = GMFrontOffice.HistoryOf(league, team.TeamCode);
            var rows = hist.Skip(Math.Max(0, hist.Count - 12)).ToList();
            for (int r = 0; r < 12; r++)
            {
                var h = r < rows.Count ? rows[rows.Count - 1 - r] : null;
                string[] v = h == null ? new string[6] : new[]
                {
                    h.Year.ToString(), $"{h.W}승 {h.D}무 {h.L}패", GMTeamRecord.PctLabel(h.Pct), $"{h.Rank}위", $"{h.AttendancePerGame:N0}",
                    h.Champion ? "한국시리즈 우승" : h.Postseason ? "포스트시즌" : h.Rank == 10 ? "최하위" : "-",
                };
                for (int c = 0; c < 6; c++)
                {
                    hiCells[r, c].text = v[c] ?? "";
                    hiCells[r, c].color = h != null && h.Champion ? Gold : h != null && h.Pct < 0.5 ? new Color(1f, 0.7f, 0.65f) : White;
                }
            }
            var mine = league.SeasonAwardsHistory.Concat(new[] { league.Awards }).Where(b => b != null)
                .SelectMany(b => b.All().Where(a => a != null && a.HasWinner && a.TeamCode == team.TeamCode).Select(a => $"{b.SeasonYear} {a.AwardName} - {a.PlayerName}"))
                .Reverse().Take(18).ToList();
            hiAwards.text = mine.Count > 0 ? string.Join("\n", mine) : "아직 단장 재임 중 수상 기록이 없습니다. 시즌이 끝나면 시상식 결과가 여기에 쌓입니다.";
            var fo = GMFrontOffice.Ensure(league);
            hiEnding.text = fo.Ending != GMEnding.None ? $"{fo.EndingYear} {GMFrontOffice.EndingLabel(fo.Ending)}\n{fo.EndingNote}"
                : $"구단주 신임도 {fo.OwnerTrust} · 엔딩 전망 {GMFrontOffice.EndingLabel(GMFrontOffice.ForecastEnding(league))}";
        }

        // ================================================================== 진행 · 다른 화면

        /// <summary>
        /// [✔ 진행하기] - [TASK-GM-07] 라우팅 변경: 한 경기를 바로 진행하지 않는다.
        ///   - 메인 홈(프런트 오피스 6분할)이 아닌 화면에서 누르면 메인 홈으로 이동해 전체 상황을 먼저 보여 준다.
        ///   - 메인 홈에서 누르면 오늘 경기 ① 전력 분석(PreGameView) → [플레이 볼] ② 실시간 이닝 경기 → ③ 경기 결과로 이어진다.
        ///   - 정규시즌이 끝났으면 포스트시즌 트리(1경기씩 진행), 포스트시즌까지 끝났으면 시상식을 연다. 인터럽트가 남아 있으면 대시보드.
        /// 화면을 옮겼으면 true.
        /// </summary>
        public bool Continue()
        {
            CloseMenus();
            if (simulator == null) { SetStatus("단장 모드 리그가 없습니다."); return false; }
            if (simulator.IsSeasonComplete)
            {
                var ps = GMAwardEvaluator.BeginPostseason(simulator);
                if (ps != null && ps.Completed) { OpenAwards(); return true; }
                OpenPostseasonTree();
                SetStatus("정규시즌 종료 - 포스트시즌 트리에서 [다음 경기 진행]으로 1경기씩 치르십시오.");
                return true;
            }
            if (!IsAtMainHome)
            {
                GoMainHome();
                SetStatus("리그 플레이 메인 홈 - 구단 상황 · 4일 일정을 확인하고 [진행하기]를 한 번 더 누르면 오늘 경기 전력 분석으로 이어집니다.");
                return true;
            }
            if (simulator.PendingInterrupt != null) { OpenDashboard(); return false; }
            string settled = SettleCompensationsBeforeSeason(); // [TASK-GM-08] 개막 전 FA 보상 정산
            var view = PrePostView != null ? PrePostView : FindAnyObjectByType<GMMatchPrePostUIController>(FindObjectsInactive.Include);
            if (view == null || !view.ShowPreGameView(simulator)) { SetStatus("전력 분석 화면을 열 수 없습니다."); return false; }
            view.OnClosed -= Refresh; view.OnClosed += Refresh;
            SetStatus((settled != null ? settled + " / " : "") + "① 전력 분석 - 선발 · 전력 · 치어리더를 확인하고 [플레이 볼]을 누르십시오.");
            return true;
        }

        private GMLiveLeagueDashboardUIController Dash => DashView != null ? DashView : FindAnyObjectByType<GMLiveLeagueDashboardUIController>(FindObjectsInactive.Include);

        public void OpenDashboard()
        {
            var dash = Dash;
            if (dash == null || simulator == null) { SetStatus("대시보드 화면이 없습니다."); return; }
            dash.Open(simulator);
        }

        /// <summary>[새 시즌/난이도 설정] - 대시보드의 새 시즌 모달(3대 모드 · 10구단 · 실명/가상명 · 4단계 난이도 · 하우스 룰)을 연다.</summary>
        public void OpenSeasonSettings()
        {
            var dash = Dash;
            if (dash == null) { SetStatus("새 시즌 설정 화면이 없습니다."); return; }
            dash.Open(simulator);
            dash.OpenSeasonModal();
        }

        public void OpenPreGame()
        {
            var view = PrePostView != null ? PrePostView : FindAnyObjectByType<GMMatchPrePostUIController>(FindObjectsInactive.Include);
            if (view == null || simulator == null || !view.ShowPreGameView(simulator)) SetStatus("전력 비교를 열 수 없습니다(시즌 종료 또는 화면 없음).");
            else { view.OnClosed -= Refresh; view.OnClosed += Refresh; }
        }

        public void OpenLastBox()
        {
            var view = PrePostView != null ? PrePostView : FindAnyObjectByType<GMMatchPrePostUIController>(FindObjectsInactive.Include);
            var last = League?.LastUserMatchBoxScore;
            if (view == null || last == null) { SetStatus("아직 경기 기록이 없습니다."); return; }
            view.Attach(simulator);
            view.ShowPostGameBoxScoreView(last);
            view.OnClosed -= Refresh; view.OnClosed += Refresh;
        }

        public void OpenCheerEntry()
        {
            var view = CheerView != null ? CheerView : FindAnyObjectByType<GMCheerleaderEntryUIController>(FindObjectsInactive.Include);
            if (view == null || UserTeam == null) { SetStatus("응원단 화면이 없습니다."); return; }
            view.Open(UserTeam, Refresh);
        }

        public void OpenAwards()
        {
            var view = AwardsView != null ? AwardsView : FindAnyObjectByType<GMAwardsCeremonyUIController>(FindObjectsInactive.Include);
            if (view == null || simulator == null) { SetStatus("시상식 화면이 없습니다."); return; }
            view.OnClosed -= Refresh; view.OnClosed += Refresh;
            view.OnSeasonAdvanced -= OnSeasonAdvanced; view.OnSeasonAdvanced += OnSeasonAdvanced;
            view.Open(simulator, null);
        }

        private void OnSeasonAdvanced(GMLiveSeasonSimulator next)
        {
            if (next == null) return;
            simulator = next;
            ResetSelections();
            Refresh();
        }

        /// <summary>기존(세로) 화면으로 이동 - 허브를 잠시 내리고 UIManager로 전환한다. 그 화면을 닫고 로비로 오면 허브가 다시 열린다.</summary>
        public void GoLegacy(ScreenType type)
        {
            if (UIManager.Instance == null) { SetStatus("화면 관리자가 없습니다."); return; }
            Hide();
            UIManager.Instance.ShowScreen(type);
        }

        public void GoClassicLobby()
        {
            SuppressAutoOpenOnce = true;
            Hide();
            UIManager.Instance?.ShowScreen(ScreenType.Lobby);
        }
    }
}
