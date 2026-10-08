using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Simulation;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-GM-03] 한 경기 시뮬레이션 화면(모든 글씨 Normal · 15pt 이상). [TASK-GM-06] 1920×1080 Landscape 재배치 + ABS 판정 리포트.
    ///   - PreGame(기획서 2.2.1): 매치업 헤더(경기 번호 · 날짜 · 구장 · 양 팀 로고 · 시즌 성적 · 순위 · 최근 5경기) · 선발투수 맞대결 ·
    ///     5대 전력 비교 바 + 팀워크/실효 전력/부작용 배지 · 오늘의 치어리더 엔트리(4~6인)와 응원 버프 · [경기 시작] [라인업/치어리더 점검] [대시보드로 돌아가기]
    ///   - PostGame(기획서 2.2.2): KBO 전광판(1~9회, 연장 최대 12회 · R/H/E) · 자동 기사(헤드라인 + 4문단) · 승리 확률 그래프 + 결정적 플레이 TOP 3 ·
    ///     원정/홈 × 타자/투수 박스스코어 탭 + 주석(2루타 · 3루타 · 홈런 · 도루 · 병살타 · 실책 · 비자책)
    /// </summary>
    public partial class GMMatchPrePostUIController : MonoBehaviour
    {
        public const string PreRootName = "PreGameRoot", PostRootName = "PostGameRoot";
        public const int TitlePt = 24, SubPt = 17, NamePt = 22, BodyPt = 18, SmallPt = 16, ButtonPt = 20, MetricPt = 17, GridPt = 16, HeadlinePt = 21;
        public const int MaxInnings = 12, GridRows = 12, GridCols = 10;

        private static readonly Color Bg = new Color(0.07f, 0.09f, 0.16f);
        private static readonly Color Panel = new Color(1f, 1f, 1f, 0.06f);
        private static readonly Color White = new Color(0.96f, 0.97f, 0.99f);
        private static readonly Color Muted = new Color(0.74f, 0.8f, 0.9f);
        private static readonly Color Gold = new Color(1f, 0.84f, 0.3f);
        private static readonly Color Green = new Color(0.45f, 0.92f, 0.55f);
        private static readonly Color ButtonIdle = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color ButtonOn = new Color(0.15f, 0.45f, 0.85f);

        [SerializeField] private Font regularFont;

        private CompyaUiKit kit;
        private RectTransform preRoot, postRoot;

        // ---- PreGame
        private Text preGameLabel, preDate, preAwayName, preHomeName, preAwayRecord, preHomeRecord, preAwayStarter, preHomeStarter;
        private Text preAwayTeamwork, preHomeTeamwork, preAwayBadges, preHomeBadges, preAwayCheer, preHomeCheer;
        private RawImage preAwayLogo, preHomeLogo;
        private readonly Text[] metricAway = new Text[5], metricHome = new Text[5];
        private readonly RectTransform[] barAway = new RectTransform[5], barHome = new RectTransform[5];
        private Button startButton, checkButton, backButton;

        // ---- PostGame
        private Text postSubtitle, headline, recapBody, wpaTitle, footnote, cheerSummary;
        private Text absAway, absHome; // [TASK-GM-06] ABS 판정 리포트
        private readonly Text[,] lineScore = new Text[3, MaxInnings + 4]; // [행(헤더/원정/홈), 열(팀 · 1~12회 · R · H · E)]
        private readonly RawImage[] lineLogos = new RawImage[2];
        private readonly Text[] keyPlays = new Text[3];
        private readonly Text[,] grid = new Text[GridRows, GridCols];
        private WpaLineGraphic wpaLine;
        private Button tabAway, tabHome, tabBatting, tabPitching, postClose;

        private GMLiveSeasonSimulator simulator;
        private GMMatchPreview preview;
        private GMMatchBoxScoreData box;
        private bool showHome, showPitching;

        public event Action OnClosed;

        public RectTransform PreRoot => preRoot;
        public RectTransform PostRoot => postRoot;
        public GMMatchPreview CurrentPreview => preview;
        public GMMatchBoxScoreData CurrentBoxScore => box;
        public bool ShowingHome => showHome;
        public bool ShowingPitching => showPitching;

        public void Configure(Font regular) => regularFont = regular;

        private void Awake()
        {
            if (preRoot == null || postRoot == null) Build();
        }

        // ================================================================== 공개 진입점

        /// <summary>진행기를 연결한다(직전 경기 결과만 열 때 - 내 구단 탭 기본 선택용).</summary>
        public void Attach(GMLiveSeasonSimulator sim) => simulator = sim;

        /// <summary>대시보드 [한 경기] - 다음 내 구단 경기의 전력 비교를 띄운다. 시즌이 끝났으면 false.</summary>
        public bool ShowPreGameView(GMLiveSeasonSimulator sim)
        {
            simulator = sim;
            postseasonGame = null; // [TASK-GM-07] 정규시즌 경기
            var p = GMMatchPreview.Build(sim);
            if (p == null) return false;
            ShowPreGameView(p);
            return true;
        }

        public void ShowPreGameView(GMMatchPreview scheduledMatch)
        {
            if (preRoot == null) Build();
            preview = scheduledMatch;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            postRoot.gameObject.SetActive(false);
            if (liveRoot != null) liveRoot.gameObject.SetActive(false);
            preRoot.gameObject.SetActive(true);
            stage = GMMatchStage.PreGame;
            BindPreview();
            PlayPreGameAudio(); // [TASK-GM-12] 라인업송
        }

        public void ShowPostGameBoxScoreView(GMMatchBoxScoreData boxScore)
        {
            if (postRoot == null) Build();
            if (boxScore == null) return;
            box = boxScore;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            preRoot.gameObject.SetActive(false);
            if (liveRoot != null) liveRoot.gameObject.SetActive(false);
            postRoot.gameObject.SetActive(true);
            stage = GMMatchStage.PostGame;
            string user = simulator?.League?.SelectedTeamCode;
            showHome = user != null ? user == box.HomeCode : false;
            showPitching = false;
            BindBoxScore();
        }

        /// <summary>
        /// [TASK-GM-07] 결과까지 한 번에 - ① → ② 실시간 이닝 경기(세션) → 남은 타석 자동 진행 → ③ 박스스코어. 화면 버튼은 [플레이 볼](PlayBall)로
        /// ② 단계에서 멈추고, 이 함수는 고속 확인 · 테스트용 경로다(같은 세션 · 같은 기록 반영).
        /// </summary>
        public bool StartMatch()
        {
            if (simulator == null) return false;
            if (postseasonGame == null && (simulator.IsSeasonComplete || simulator.PendingInterrupt != null)) return false;
            if (!PlayBall()) return false;
            return FinishLiveMatch();
        }

        public void CloseAll()
        {
            autoPlay = false;
            DetachLiveAudio(); // [TASK-GM-08] 응원 믹스 · 타석 이벤트 구독 해제
            if (preRoot != null) preRoot.gameObject.SetActive(false);
            if (postRoot != null) postRoot.gameObject.SetActive(false);
            if (liveRoot != null) liveRoot.gameObject.SetActive(false);
            stage = GMMatchStage.None;
            gameObject.SetActive(false);
            OnClosed?.Invoke();
        }

        public void SelectTab(bool home, bool pitching)
        {
            showHome = home;
            showPitching = pitching;
            BindGrid();
        }

        // ================================================================== 조립

        public void Build()
        {
            foreach (var n in new[] { PreRootName, PostRootName, LiveRootName })
            {
                var old = transform.Find(n);
                if (old == null) continue;
                old.name = "_" + n + "_old";
                old.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            }
            var font = regularFont != null ? regularFont : TextTidy.BodyFont;
            kit = new CompyaUiKit(font, font);
            using (CompyaUiKit.Wide()) // [TASK-GM-06] 1920×1080 Landscape 좌표
            {
                BuildPre();
                BuildPost();
                BuildLive(); // [TASK-GM-07] ② 실시간 이닝 경기
            }
            preRoot.gameObject.SetActive(false);
            postRoot.gameObject.SetActive(false);
            liveRoot.gameObject.SetActive(false);
            GMRaycastSanitizer.Sanitize(transform); // [TASK-GM-10] 모바일 터치 - 장식 그래픽 RaycastTarget 정리
        }

        private Text L(Transform parent, string name, string text, float x0, float y0, float x1, float y1, int pt, TextAnchor anchor, Color color)
        {
            var t = kit.Label(parent, name, text, x0, y0, x1, y1, pt / 0.9f, anchor, color);
            return TextTidy.Exact(t, pt);
        }

        private Button Btn(Transform parent, string name, string text, float x0, float y0, float x1, float y1, Color bg, int pt)
        {
            var b = kit.Button(parent, name, text, x0, y0, x1, y1, bg, White, pt / 0.9f, bold: false);
            TextTidy.ExactButton(b, pt);
            return b;
        }

        private void BuildPre()
        {
            preRoot = CompyaUiKit.Fill(transform, PreRootName);
            CompyaUiKit.Paint(preRoot, Bg, true);
            preGameLabel = L(preRoot, "GameLabel", "", 20, 10, 1300, 58, TitlePt, TextAnchor.MiddleLeft, Gold);
            preDate = L(preRoot, "DateStadium", "", 20, 60, 1300, 98, SubPt, TextAnchor.MiddleLeft, Muted);

            // 매치업 헤더(좌 원정 · 중앙 VS · 우 홈)
            preAwayLogo = CompyaUiKit.Logo(preRoot, "AwayLogo", 60, 108, 290, 326);
            preHomeLogo = CompyaUiKit.Logo(preRoot, "HomeLogo", 1630, 108, 1860, 326);
            L(preRoot, "AwayTag", "원정", 310, 120, 460, 160, SmallPt, TextAnchor.MiddleCenter, Muted);
            L(preRoot, "HomeTag", "홈", 1460, 120, 1610, 160, SmallPt, TextAnchor.MiddleCenter, Muted);
            preAwayName = L(preRoot, "AwayName", "", 310, 168, 890, 220, NamePt, TextAnchor.MiddleCenter, White);
            preHomeName = L(preRoot, "HomeName", "", 1030, 168, 1610, 220, NamePt, TextAnchor.MiddleCenter, White);
            L(preRoot, "Versus", "VS", 900, 166, 1020, 236, TitlePt + 6, TextAnchor.MiddleCenter, White);
            preAwayRecord = L(preRoot, "AwayRecord", "", 310, 226, 890, 268, SmallPt, TextAnchor.MiddleCenter, Muted);
            preHomeRecord = L(preRoot, "HomeRecord", "", 1030, 226, 1610, 268, SmallPt, TextAnchor.MiddleCenter, Muted);

            CompyaUiKit.Box(preRoot, "StarterPanel", 12, 340, 640, 944, Panel);
            L(preRoot, "StarterTitle", "선발투수 맞대결", 24, 344, 630, 384, BodyPt + 2, TextAnchor.MiddleLeft, Gold);
            preAwayStarter = L(preRoot, "AwayStarter", "", 24, 390, 320, 938, BodyPt, TextAnchor.UpperLeft, White);
            preHomeStarter = L(preRoot, "HomeStarter", "", 332, 390, 630, 938, BodyPt, TextAnchor.UpperLeft, White);
            preAwayStarter.lineSpacing = preHomeStarter.lineSpacing = 1.15f;

            CompyaUiKit.Box(preRoot, "PowerPanel", 652, 340, 1268, 944, Panel);
            L(preRoot, "PowerTitle", "구단 5대 전력 & 팀워크", 664, 344, 1256, 384, BodyPt + 2, TextAnchor.MiddleLeft, Gold);
            for (int k = 0; k < 5; k++)
            {
                float y0 = 392 + k * 60, y1 = y0 + 52;
                metricAway[k] = L(preRoot, $"MetricAway{k}", "", 660, y0, 730, y1, MetricPt + 2, TextAnchor.MiddleCenter, White);
                barAway[k] = Bar(preRoot, $"BarAway{k}", 736, y0 + 14, 880, y1 - 14, true);
                L(preRoot, $"MetricLabel{k}", GMMatchPreview.MetricLabels[k], 886, y0, 1034, y1, MetricPt, TextAnchor.MiddleCenter, Muted);
                barHome[k] = Bar(preRoot, $"BarHome{k}", 1040, y0 + 14, 1184, y1 - 14, false);
                metricHome[k] = L(preRoot, $"MetricHome{k}", "", 1190, y0, 1260, y1, MetricPt + 2, TextAnchor.MiddleCenter, White);
            }
            preAwayTeamwork = L(preRoot, "AwayTeamwork", "", 664, 700, 1256, 742, SmallPt + 1, TextAnchor.MiddleLeft, White);
            preHomeTeamwork = L(preRoot, "HomeTeamwork", "", 664, 746, 1256, 788, SmallPt + 1, TextAnchor.MiddleRight, White);
            preAwayBadges = L(preRoot, "AwayBadges", "", 664, 794, 1256, 864, SmallPt, TextAnchor.MiddleLeft, Gold);
            preHomeBadges = L(preRoot, "HomeBadges", "", 664, 868, 1256, 938, SmallPt, TextAnchor.MiddleRight, Gold);

            CompyaUiKit.Box(preRoot, "CheerPanel", 1280, 340, 1908, 944, Panel);
            L(preRoot, "CheerTitle", "오늘의 치어리더 엔트리 (4~6인)", 1292, 344, 1896, 384, BodyPt + 2, TextAnchor.MiddleLeft, Gold);
            preAwayCheer = L(preRoot, "AwayCheer", "", 1292, 390, 1896, 660, SmallPt, TextAnchor.UpperLeft, White);
            preHomeCheer = L(preRoot, "HomeCheer", "", 1292, 666, 1896, 938, SmallPt, TextAnchor.UpperLeft, White);

            startButton = Btn(preRoot, "StartButton", "플레이 볼 (실시간 이닝 경기)", 20, 956, 640, 1066, ButtonOn, ButtonPt + 2);
            checkButton = Btn(preRoot, "CheckButton", "라인업/치어리더 점검", 652, 956, 1268, 1066, new Color(0.55f, 0.22f, 0.45f), ButtonPt + 2);
            backButton = Btn(preRoot, "BackButton", "대시보드로 돌아가기", 1280, 956, 1900, 1066, ButtonIdle, ButtonPt);
            startButton.onClick.AddListener(() => PlayBall());
            checkButton.onClick.AddListener(OpenLineupCheck);
            backButton.onClick.AddListener(CloseAll);
        }

        private RectTransform Bar(Transform parent, string name, float x0, float y0, float x1, float y1, bool fromRight)
        {
            var bg = CompyaUiKit.Box(parent, name, x0, y0, x1, y1, new Color(1f, 1f, 1f, 0.1f));
            var fill = CompyaUiKit.Fill(bg.transform, "Fill");
            CompyaUiKit.Paint(fill, fromRight ? new Color(0.95f, 0.55f, 0.25f) : new Color(0.3f, 0.6f, 1f));
            fill.pivot = new Vector2(fromRight ? 1f : 0f, 0.5f);
            return fill;
        }

        // [TASK-GM-06] 전광판 행 y(헤더 · 원정 · 홈) - 1920×1080 좌표
        private static readonly float[] LineY0 = { 102, 146, 204 }, LineY1 = { 142, 200, 258 };

        private void BuildPost()
        {
            postRoot = CompyaUiKit.Fill(transform, PostRootName);
            CompyaUiKit.Paint(postRoot, Bg, true);
            L(postRoot, "KboTitle", "KOREAN BASEBALL ORGANIZATION", 20, 8, 1200, 50, TitlePt - 2, TextAnchor.MiddleLeft, Gold);
            postSubtitle = L(postRoot, "Subtitle", "", 20, 52, 1680, 92, SubPt, TextAnchor.MiddleLeft, Muted);
            postClose = Btn(postRoot, "CloseButton", "대시보드로", 1700, 12, 1908, 92, ButtonIdle, ButtonPt);
            postClose.onClick.AddListener(CloseAll);

            // 전광판
            CompyaUiKit.Box(postRoot, "LineScorePanel", 12, 98, 1236, 264, Panel);
            for (int r = 0; r < 3; r++)
            {
                float y0 = LineY0[r], y1 = LineY1[r];
                if (r > 0) lineLogos[r - 1] = CompyaUiKit.Logo(postRoot, $"LineLogo{r}", 20, y0 + 4, 70, y1 - 4);
                for (int c = 0; c < MaxInnings + 4; c++)
                    lineScore[r, c] = L(postRoot, $"LS{r}_{c}", "", 0, y0, 10, y1, r == 0 ? SmallPt : BodyPt, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, r == 0 ? Muted : White);
            }

            // [TASK-GM-06] ABS 판정 리포트(보더라인 콜 · 존 적응 지수 · 포수 블로킹 세이브 · 루킹 삼진)
            CompyaUiKit.Box(postRoot, "AbsPanel", 1248, 98, 1908, 264, Panel);
            L(postRoot, "AbsTitle", "ABS(자동 투구 판정) 리포트", 1260, 102, 1900, 140, BodyPt, TextAnchor.MiddleLeft, Gold);
            absAway = L(postRoot, "AbsAway", "", 1260, 146, 1900, 200, SmallPt, TextAnchor.MiddleLeft, White);
            absHome = L(postRoot, "AbsHome", "", 1260, 204, 1900, 258, SmallPt, TextAnchor.MiddleLeft, White);

            // 기사
            CompyaUiKit.Box(postRoot, "RecapPanel", 12, 272, 760, 640, Panel);
            headline = L(postRoot, "Headline", "", 24, 276, 750, 330, HeadlinePt, TextAnchor.MiddleLeft, Gold);
            recapBody = L(postRoot, "RecapBody", "", 24, 334, 750, 636, SmallPt + 1, TextAnchor.UpperLeft, White);
            recapBody.lineSpacing = 1.12f;
            recapBody.verticalOverflow = VerticalWrapMode.Truncate;

            // 승리 확률 그래프
            CompyaUiKit.Box(postRoot, "WpaPanel", 12, 648, 760, 1072, Panel);
            wpaTitle = L(postRoot, "WpaTitle", "승리 확률 변동(WPA)", 24, 652, 750, 686, BodyPt, TextAnchor.MiddleLeft, Gold);
            var area = CompyaUiKit.Box(postRoot, "WpaArea", 30, 690, 740, 880, new Color(0f, 0f, 0f, 0.3f));
            var mid = CompyaUiKit.Norm(area.transform, "Baseline", 0f, 0.497f, 1f, 0.503f);
            CompyaUiKit.Paint(mid, new Color(1f, 1f, 1f, 0.45f));
            var lineRect = CompyaUiKit.Fill(area.transform, "WpaLine");
            wpaLine = lineRect.gameObject.AddComponent<WpaLineGraphic>();
            wpaLine.color = new Color(0.4f, 0.85f, 1f);
            L(postRoot, "AxisHome", "▲ 홈 승리", 24, 884, 380, 914, SmallPt - 1, TextAnchor.MiddleLeft, Muted);
            L(postRoot, "AxisAway", "▼ 원정 승리", 390, 884, 750, 914, SmallPt - 1, TextAnchor.MiddleRight, Muted);
            L(postRoot, "KeyPlaysTitle", "결정적 플레이 TOP 3", 24, 918, 750, 950, BodyPt, TextAnchor.MiddleLeft, Gold);
            for (int k = 0; k < 3; k++)
                keyPlays[k] = L(postRoot, $"KeyPlay{k}", "", 24, 954 + k * 38, 750, 988 + k * 38, SmallPt, TextAnchor.MiddleLeft, Green);

            // 박스스코어 탭 · 표 · 주석(우측)
            tabAway = Btn(postRoot, "TabAway", "원정", 780, 276, 1040, 318, ButtonIdle, ButtonPt - 2);
            tabHome = Btn(postRoot, "TabHome", "홈", 1050, 276, 1310, 318, ButtonIdle, ButtonPt - 2);
            tabBatting = Btn(postRoot, "TabBatting", "타자 기록", 1330, 276, 1610, 318, ButtonIdle, ButtonPt - 2);
            tabPitching = Btn(postRoot, "TabPitching", "투수 기록", 1620, 276, 1900, 318, ButtonIdle, ButtonPt - 2);
            tabAway.onClick.AddListener(() => SelectTab(false, showPitching));
            tabHome.onClick.AddListener(() => SelectTab(true, showPitching));
            tabBatting.onClick.AddListener(() => SelectTab(showHome, false));
            tabPitching.onClick.AddListener(() => SelectTab(showHome, true));
            CompyaUiKit.Box(postRoot, "GridPanel", 772, 322, 1908, 876, Panel);
            for (int r = 0; r < GridRows; r++)
            {
                float y0 = r == 0 ? 324 : 368 + (r - 1) * 46, y1 = r == 0 ? 364 : y0 + 42;
                for (int c = 0; c < GridCols; c++)
                {
                    float x0 = c == 0 ? 780 : 1086 + (c - 1) * 91, x1 = c == 0 ? 1080 : x0 + 88;
                    grid[r, c] = L(postRoot, $"Grid{r}_{c}", "", x0, y0, x1, y1, GridPt, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, r == 0 ? Muted : White);
                }
            }
            footnote = L(postRoot, "Footnote", "", 780, 880, 1900, 1010, SmallPt, TextAnchor.UpperLeft, Muted);
            footnote.lineSpacing = 1.1f;
            // [TASK-GM-05] 오늘 단상에 오른 4~6인 응원단 활약 요약
            cheerSummary = L(postRoot, "CheerSummary", "", 780, 1014, 1900, 1070, SmallPt + 1, TextAnchor.MiddleLeft, new Color(1f, 0.62f, 0.82f));
        }

        // ================================================================== PreGame 바인딩

        private void BindPreview()
        {
            var p = preview;
            if (p == null) return;
            preGameLabel.text = p.GameLabel;
            preDate.text = $"{p.DateLabel} · {p.Stadium}";
            CompyaUiKit.SetLogo(preAwayLogo, p.Away.Team, 1f);
            CompyaUiKit.SetLogo(preHomeLogo, p.Home.Team, 1f);
            preAwayName.text = p.Away.Name;
            preHomeName.text = p.Home.Name;
            preAwayRecord.text = TeamLine(p.Away);
            preHomeRecord.text = TeamLine(p.Home);
            preAwayStarter.text = StarterCard(p.Away);
            preHomeStarter.text = StarterCard(p.Home);
            for (int k = 0; k < 5; k++)
            {
                int a = p.Away.Metric(k), h = p.Home.Metric(k);
                metricAway[k].text = a.ToString();
                metricHome[k].text = h.ToString();
                SetFill(barAway[k], a, true);
                SetFill(barHome[k], h, false);
            }
            preAwayTeamwork.text = $"팀워크 {p.Away.Teamwork} · 실효 전력 x{p.Away.PowerMultiplier:0.00}";
            preHomeTeamwork.text = $"팀워크 {p.Home.Teamwork} · 실효 전력 x{p.Home.PowerMultiplier:0.00}";
            preAwayBadges.text = string.Join(" · ", p.Away.Badges);
            preHomeBadges.text = string.Join(" · ", p.Home.Badges);
            preAwayCheer.text = CheerCard(p.Away);
            preHomeCheer.text = CheerCard(p.Home);
            startButton.interactable = simulator != null && (postseasonGame != null || (!simulator.IsSeasonComplete && simulator.PendingInterrupt == null));
            checkButton.interactable = postseasonGame == null;
        }

        public static string TeamLine(GMTeamPreview t) => $"{t.RecordLabel} · {t.Rank}위 · 최근 {t.Recent5}";

        public static string StarterCard(GMTeamPreview t) =>
            t.Starter == null ? "선발 미정"
                : $"{t.Starter.Template.PlayerName} ({(t.IsHome ? "홈" : "원정")} 선발)\n{t.StarterSeasonLabel}\n{t.StarterRatingLabel}\n컨디션 {t.StarterConditionLabel}";

        public static string CheerCard(GMTeamPreview t)
        {
            if (t.CheerEntry.Count == 0) return "엔트리 없음";
            var names = string.Join(" · ", t.CheerEntry.Select(c => c.DisplayName));
            return $"{t.CheerEntry.Count}인: {names}\n리더십·팀워크 +{t.Leadership} / 타격 버프 +{t.BattingBuff} / 마운드 버프 +{t.MoundBuff}\n" +
                   $"실책 -{t.ErrorReductionPercent}%{(t.IsHome ? $" · 홈 흥행 +{t.HomeGate:N0}만 원" : "")}{(t.TiredCount > 0 ? $" · 체력 저하 {t.TiredCount}명" : "")}";
        }

        private static void SetFill(RectTransform fill, int value, bool fromRight)
        {
            float v = Mathf.Clamp01(value / 100f);
            fill.anchorMin = new Vector2(fromRight ? 1f - v : 0f, 0f);
            fill.anchorMax = new Vector2(fromRight ? 1f : v, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
        }

        /// <summary>[TASK-GM-05] 치어리더 엔트리 화면(비우면 씬에서 찾는다 - 테스트가 주입한다).</summary>
        public GMCheerleaderEntryUIController CheerView { get; set; }

        /// <summary>
        /// [라인업/치어리더 점검] - [TASK-GM-05] 치어리더 엔트리 화면(15인 풀 · 4~6인 엔트리)을 전력 비교 위에 띄우고, 닫으면 전력 비교로 돌아와
        /// 바뀐 엔트리 · 버프를 다시 보여 준다. 화면이 없으면 기존 [계약·연봉·팀워크 진단]으로 간다.
        /// </summary>
        public void OpenLineupCheck()
        {
            var cheer = CheerView != null ? CheerView : FindAnyObjectByType<GMCheerleaderEntryUIController>(FindObjectsInactive.Include);
            var team = simulator?.League?.UserTeam;
            if (cheer != null && team != null)
            {
                var sim = simulator;
                cheer.Open(team, () => { if (sim != null && !sim.IsSeasonComplete) ShowPreGameView(sim); });
                return;
            }
            CloseAll();
            var dash = FindAnyObjectByType<GMLiveLeagueDashboardUIController>(FindObjectsInactive.Include);
            if (dash != null) dash.gameObject.SetActive(false);
            PlayerManagementUIController.OpenGrowthHub(); // 단장 모드 = [계약·연봉·팀워크 진단](치어리더 엔트리 · 관리 바로가기)
        }

        // ================================================================== PostGame 바인딩

        private void BindBoxScore()
        {
            var b = box;
            string away = NameAliasTable.DisplayTeamName(b.AwayCode), home = NameAliasTable.DisplayTeamName(b.HomeCode);
            postSubtitle.text = string.IsNullOrEmpty(b.GameTitle)
                ? $"{b.Stadium} · {b.DateLabel} · {away} vs {home} · Game #{b.GameIndex + 1:000}"
                : $"KBO 포스트시즌 {b.GameTitle} · {b.Stadium} · {b.DateLabel} · {away} vs {home}"; // [TASK-GM-07]
            cheerSummary.text = string.IsNullOrEmpty(b.CheerSummary) ? "" :
                $"오늘의 응원단({string.Join(" · ", b.CheerEntryNames ?? new List<string>())}) - {b.CheerSummary}";
            using (CompyaUiKit.Wide()) BindLineScore();
            absAway.text = AbsLine(b.AwayCode, b.AwayAbsCalls, b.AwayAbsIndex, b.AwayBlockSaves, b.AwayLookingK);
            absHome.text = AbsLine(b.HomeCode, b.HomeAbsCalls, b.HomeAbsIndex, b.HomeBlockSaves, b.HomeLookingK);
            headline.text = b.Recap?.Headline ?? "";
            recapBody.text = b.Recap?.Body ?? "";
            var pts = b.WpaPoints ?? new List<WPAPoint>();
            int n = Math.Max(1, pts.Count - 1);
            wpaLine.SetPoints(pts.Select((p, i) => new Vector2(i / (float)n, p.HomeWinProbability)), 4f);
            for (int k = 0; k < 3; k++)
                keyPlays[k].text = b.KeyPlays != null && k < b.KeyPlays.Count ? $"{k + 1}. {b.KeyPlays[k].Label}" : "";
            CompyaUiKit.SetButtonText(tabAway, $"원정: {CompyaUiKit.ShortName(NameAliasTable.ToTeam(b.AwayCode))}");
            CompyaUiKit.SetButtonText(tabHome, $"홈: {CompyaUiKit.ShortName(NameAliasTable.ToTeam(b.HomeCode))}");
            BindGrid();
        }

        /// <summary>[TASK-GM-06] 박스스코어 ABS 한 줄 - "원정 KIA: 보더라인 콜 12 · 존 적응 64 · 블로킹 세이브 1 · 루킹 삼진 3".</summary>
        public static string AbsLine(string code, int calls, int index, int blocks, int lookingK) =>
            $"{CompyaUiKit.ShortName(NameAliasTable.ToTeam(code))}: 보더라인 콜 {calls} · 존 적응 {index} · 블로킹 세이브 {blocks} · 루킹 삼진 {lookingK}";

        private void BindLineScore()
        {
            int innings = Math.Min(MaxInnings, Math.Max(9, box.Innings));
            float teamX1 = 240, innX0 = 250, innX1 = 1000, w = (innX1 - innX0) / innings;
            float[] rx = { 1006, 1080, 1154, 1228 };
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < MaxInnings + 4; c++)
                {
                    var t = lineScore[r, c];
                    var rect = (RectTransform)t.transform;
                    float y0 = LineY0[r], y1 = LineY1[r];
                    bool used = c == 0 || (c <= MaxInnings && c <= innings) || c > MaxInnings;
                    t.gameObject.SetActive(used);
                    if (!used) continue;
                    if (c == 0) CompyaUiKit.SetBox(rect, r == 0 ? 20 : 76, y0, teamX1, y1);
                    else if (c <= MaxInnings) CompyaUiKit.SetBox(rect, innX0 + (c - 1) * w, y0, innX0 + c * w - 2, y1);
                    else CompyaUiKit.SetBox(rect, rx[c - MaxInnings - 1], y0, rx[c - MaxInnings] - 2, y1);
                }
            }
            string[] rhe = { "R", "H", "E" };
            lineScore[0, 0].text = "구단";
            for (int i = 1; i <= innings; i++) lineScore[0, i].text = i.ToString();
            for (int k = 0; k < 3; k++) lineScore[0, MaxInnings + 1 + k].text = rhe[k];
            for (int side = 0; side < 2; side++)
            {
                bool isHome = side == 1;
                int r = side + 1;
                string code = isHome ? box.HomeCode : box.AwayCode;
                var runs = isHome ? box.HomeInningRuns : box.AwayInningRuns;
                CompyaUiKit.SetLogo(lineLogos[side], NameAliasTable.ToTeam(code), 1f);
                lineScore[r, 0].text = CompyaUiKit.ShortName(NameAliasTable.ToTeam(code));
                for (int i = 1; i <= innings; i++)
                    lineScore[r, i].text = i - 1 < runs.Count ? (runs[i - 1] < 0 ? "X" : runs[i - 1].ToString()) : "";
                lineScore[r, MaxInnings + 1].text = (isHome ? box.HomeR : box.AwayR).ToString();
                lineScore[r, MaxInnings + 2].text = (isHome ? box.HomeH : box.AwayH).ToString();
                lineScore[r, MaxInnings + 3].text = (isHome ? box.HomeE : box.AwayE).ToString();
            }
        }

        private void BindGrid()
        {
            if (box == null) return;
            Highlight(tabAway, !showHome); Highlight(tabHome, showHome);
            Highlight(tabBatting, !showPitching); Highlight(tabPitching, showPitching);
            for (int r = 0; r < GridRows; r++) for (int c = 0; c < GridCols; c++) grid[r, c].text = "";

            if (!showPitching)
            {
                string[] head = { "선수", "타수", "득점", "안타", "타점", "볼넷", "삼진", "타율", "홈런", "시즌타점" };
                for (int c = 0; c < GridCols; c++) grid[0, c].text = head[c];
                var rows = box.BattersOf(showHome);
                int shown = Math.Min(rows.Count, GridRows - 2);
                for (int i = 0; i < shown; i++)
                {
                    var b = rows[i];
                    string[] v = { $"{b.Order}. {b.Name} {b.Position}", b.AB.ToString(), b.R.ToString(), b.H.ToString(), b.RBI.ToString(), b.BB.ToString(), b.SO.ToString(), Avg(b.SeasonAVG), b.SeasonHR.ToString(), b.SeasonRBI.ToString() };
                    for (int c = 0; c < GridCols; c++) grid[i + 1, c].text = v[c];
                }
                string[] tot = { "Totals", rows.Sum(x => x.AB).ToString(), rows.Sum(x => x.R).ToString(), rows.Sum(x => x.H).ToString(), rows.Sum(x => x.RBI).ToString(), rows.Sum(x => x.BB).ToString(), rows.Sum(x => x.SO).ToString(), "", "", "" };
                for (int c = 0; c < GridCols; c++) grid[shown + 1, c].text = tot[c];
                footnote.text = BattingFootnote(rows, showHome ? box.AwayE : box.HomeE);
            }
            else
            {
                string[] head = { "투수", "이닝", "피안타", "실점", "자책", "볼넷", "삼진", "피홈런", "투구수", "시즌ERA" };
                for (int c = 0; c < GridCols; c++) grid[0, c].text = head[c];
                var rows = box.PitchersOf(showHome);
                int shown = Math.Min(rows.Count, GridRows - 2);
                for (int i = 0; i < shown; i++)
                {
                    var p = rows[i];
                    string[] v = { $"{p.Name}{(string.IsNullOrEmpty(p.Decision) ? "" : $" ({p.Decision})")}", p.IPLabel, p.H.ToString(), p.R.ToString(), p.ER.ToString(), p.BB.ToString(), p.SO.ToString(), p.HR.ToString(), p.NP.ToString(), p.SeasonERA.ToString("0.00") };
                    for (int c = 0; c < GridCols; c++) grid[i + 1, c].text = v[c];
                }
                int outs = rows.Sum(x => x.Outs);
                string ip = outs % 3 == 0 ? $"{outs / 3}" : $"{outs / 3} {outs % 3}/3";
                string[] tot = { "Totals", ip, rows.Sum(x => x.H).ToString(), rows.Sum(x => x.R).ToString(), rows.Sum(x => x.ER).ToString(), rows.Sum(x => x.BB).ToString(), rows.Sum(x => x.SO).ToString(), rows.Sum(x => x.HR).ToString(), rows.Sum(x => x.NP).ToString(), "" };
                for (int c = 0; c < GridCols; c++) grid[shown + 1, c].text = tot[c];
                footnote.text = PitchingFootnote(rows, showHome ? box.HomeE : box.AwayE);
            }
        }

        private static string Avg(double v) => v >= 1 ? v.ToString("0.000") : v.ToString(".000");

        /// <summary>타격 · 수비 주석 - 해당 기록이 있는 선수(시즌 누적). defenseErrors = 상대 수비 실책 수(이 팀이 얻은 실책 출루).</summary>
        public static string BattingFootnote(List<BatterBoxScoreLine> rows, int opponentErrors)
        {
            var lines = new List<string>();
            void Add(string label, Func<BatterBoxScoreLine, int> game, Func<BatterBoxScoreLine, int> season)
            {
                var who = rows.Where(r => game(r) > 0).Select(r => $"{r.Name}{(game(r) > 1 ? $" {game(r)}개" : "")}(시즌 {season(r)})").ToList();
                if (who.Count > 0) lines.Add($"{label}: {string.Join(", ", who)}");
            }
            Add("2루타(2B)", r => r.Doubles, r => r.SeasonDoubles);
            Add("3루타(3B)", r => r.Triples, r => r.SeasonTriples);
            Add("홈런(HR)", r => r.HR, r => r.SeasonHR);
            Add("도루(SB)", r => r.SB, r => r.SeasonSB);
            var gidp = rows.Where(r => r.GIDP > 0).Select(r => r.Name).ToList();
            if (gidp.Count > 0) lines.Add($"병살타(GIDP): {string.Join(", ", gidp)}");
            if (opponentErrors > 0) lines.Add($"실책(E): 상대 수비 실책 {opponentErrors}개(실책 출루는 안타 · 타점 제외)");
            return lines.Count == 0 ? "특이 기록 없음" : string.Join("\n", lines);
        }

        public static string PitchingFootnote(List<PitcherBoxScoreLine> rows, int ownErrors)
        {
            var lines = new List<string>();
            var unearned = rows.Where(p => p.R > p.ER).Select(p => $"{p.Name} {p.R - p.ER}점").ToList();
            if (unearned.Count > 0) lines.Add($"비자책 실점: {string.Join(", ", unearned)}(실책 이후 실점)");
            if (ownErrors > 0) lines.Add($"수비 실책(E): {ownErrors}개");
            var decisions = rows.Where(p => !string.IsNullOrEmpty(p.Decision))
                .Select(p => $"{p.Name}({p.Decision} · 시즌 {p.SeasonW}승 {p.SeasonL}패 {p.SeasonSV}세 {p.SeasonHLD}홀)").ToList();
            if (decisions.Count > 0) lines.Add($"결정: {string.Join(", ", decisions)}");
            lines.Add("투구수(NP)는 타석 결과 기반 추정치입니다.");
            return string.Join("\n", lines);
        }

        private static void Highlight(Button b, bool on)
        {
            if (b?.targetGraphic != null) b.targetGraphic.color = on ? ButtonOn : ButtonIdle;
        }
    }
}
