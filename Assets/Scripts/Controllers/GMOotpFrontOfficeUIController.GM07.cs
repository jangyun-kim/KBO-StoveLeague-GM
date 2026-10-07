using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// [TASK-GM-07] OOTP 27 레퍼런스 6종 반영(1920×1080):
    ///   ① 최상단 드롭다운 툴바(파일 · 경기 · 단장 · KBO · 구단 · 플레이) + 리그 날짜 · 성적 한 줄
    ///   ② 팀 배너(4일 일정 티커) + 우측 세로 퀵 아이콘 사이드바(홈 · 일정 · 순위 · 선수 · 재정 · 시장 · 응원 · PS · 설정)
    ///   ③ 감독 설정(좌 KBO 10구단 로고 리스트 · 우 프로필 · 플레이 모드 옵션 5종 · 4단계 난이도 드롭다운)
    ///   ④ 시즌 일정(좌 원정/홈 대각선 분할 매치업 카드 · 우 1~12월 달력 그리드 - 홈 구단색 · 원정 회색, 경기 시간 · 결과 스코어)
    ///   ⑤ 포스트시즌 트리(와일드카드 → 준PO → PO → 한국시리즈 4열 브래킷 · 시리즈 승수 · [다음 경기 진행] 1경기씩)
    ///   ⑥ [진행하기] 라우팅: 다른 화면에서 누르면 리그 플레이 메인 홈(프런트 오피스 6분할)으로, 메인 홈에서 누르면 ① 전력 분석부터 3단계 플로우.
    /// </summary>
    public partial class GMOotpFrontOfficeUIController
    {
        public const string PaneSchedule = "Pane_Schedule", PanePostseason = "Pane_Postseason";
        public const string ManagerSetupName = "ManagerSetupPopup", ContentAreaName = "ContentArea";
        public const float ContentRight = 1866f;  // 퀵 사이드바 자리를 비운 콘텐츠 폭(1920 좌표)
        public const int SidebarCount = 9, ScheduleRows = 6, ManagerOptionCount = 5;

        private static readonly string[] MenuTitles = { "파일", "경기", "단장", "KBO", "구단", "플레이" };
        private static readonly string[] SidebarLabels = { "홈", "일정", "순위", "선수", "재정", "시장", "응원", "PS", "설정" };
        public static readonly string[] ManagerOptionLabels = { "커미셔너 모드로 플레이", "해임당하지 않습니다", "거래 하드 모드 및 평판 시스템 사용", "페넌트 레이스 모드 (중요한 결정만)", "챌린지 모드" };
        private static readonly string[] ManagerOptionDescs =
        {
            "다른 구단과 선수 계약까지 직접 조정합니다 - 트레이드 가치 · FA 경쟁 입찰 판정을 건너뜁니다.",
            "구단주 신임도가 25 아래로 내려가지 않습니다 - 성적 부진 · 적자에도 자리를 지킵니다.",
            "AI 단장이 트레이드에서 가치를 10% 더 요구합니다.",
            "중요한 결정에만 집중합니다 - 대기록 팝업 없이 시즌이 진행되고 소식으로만 남습니다.",
            "연간 FA 영입 · 트레이드가 각 1회로 고정됩니다.",
        };
        private static readonly string[] DifficultyDescs =
        {
            "FA 요구액 0.85배 · AI 트레이드 요구 0.9배 · 구단주 시작 신임도 75 - 처음 단장을 맡는 분께 권합니다.",
            "추가 보너스나 불이익 없이 플레이하는 기본 난이도입니다(전공 · 기본값).",
            "FA 요구액 1.15배 · AI 트레이드 요구 1.12배 · 시작 신임도 50.",
            "FA 요구액 1.3배 · AI 트레이드 요구 1.25배 · 시작 신임도 40 - 전설의 단장에 도전합니다.",
        };

        private RectTransform contentArea;
        private readonly Button[] menuButtons = new Button[6];
        private readonly RectTransform[] menuLists = new RectTransform[6];
        private Text toolbarInfo1, toolbarInfo2;
        private readonly Button[] sidebarButtons = new Button[SidebarCount];

        // ---- 시즌 일정
        private int scheduleMonth = 4;
        private Text scNextTitle, scDate, scMyLabel, scMyStarter, scVersus, scOppLabel, scOppStarter, scHeadToHead, scMonthTitle;
        private RawImage scMyLogo, scOppLogo;
        private PolygonGraphic scTopPoly, scBottomPoly;
        private readonly Button[] scMonthTabs = new Button[12];
        private readonly Image[,] scCells = new Image[ScheduleRows, 7];
        private readonly Text[,] scDay = new Text[ScheduleRows, 7], scOpp = new Text[ScheduleRows, 7], scInfo = new Text[ScheduleRows, 7];
        private readonly RawImage[,] scLogos = new RawImage[ScheduleRows, 7];

        // ---- 포스트시즌 트리
        private readonly Text[,] psNames = new Text[4, 2], psWins = new Text[4, 2];
        private readonly RawImage[,] psLogos = new RawImage[4, 2];
        private readonly Image[,] psRows = new Image[4, 2];
        private readonly Text[] psGames = new Text[4];
        private Text psChampion, psNext;
        private Button psNextButton, psAutoButton;

        // ---- 감독 설정
        private RectTransform managerPopup;
        private InputField mgNameInput;
        private readonly Button[] mgTeamButtons = new Button[10];
        private readonly Button[] mgOptions = new Button[ManagerOptionCount];
        private Button mgRole, mgMode, mgVirtual, mgDifficulty, mgStart, mgApply;
        private RectTransform mgDifficultyList, mgModeList, mgRoleList;
        private Text mgDifficultyDesc, mgTeamLabel;
        private string mgTeam = NameAliasTable.SAM;
        private GMStartMode mgStartMode = GMStartMode.RealCurrent2026;
        private GMDifficulty mgDiff = GMDifficulty.Majors;
        private bool mgVirtualNames;
        private readonly GMManagerProfile mgProfile = new GMManagerProfile();

        /// <summary>테스트 · 툴 - 감독 설정 [게임 시작]이 새 리그를 만드는 함수(비우면 GameManager → GMRosterLoader).</summary>
        public Func<GMStartMode, string, bool, GMLeagueState> LeagueFactory { get; set; }

        public bool IsManagerSetupOpen => managerPopup != null && managerPopup.gameObject.activeSelf;
        public bool IsAtMainHome => mainTab == 0 && currentPane == PaneOwner;
        public int ScheduleMonth => scheduleMonth;
        public RectTransform ContentArea => contentArea;
        public GMManagerProfile PendingManagerProfile => mgProfile;
        public string PendingManagerTeam => mgTeam;
        public GMDifficulty PendingDifficulty => mgDiff;

        // ================================================================== 조립 - 툴바 · 사이드바

        private void BuildContentArea()
        {
            contentArea = CompyaUiKit.Norm(root, ContentAreaName, 0f, 0f, ContentRight / CompyaUiKit.WideWidth, 1f);
        }

        private void BuildToolbar()
        {
            CompyaUiKit.Box(root, "ToolbarBg", 0, 0, 1920, 44, new Color(0.05f, 0.05f, 0.06f));
            Btn(root, "NavBack", "◀", 6, 4, 46, 40, ButtonIdle, SmallPt).onClick.AddListener(() => { if (mainTab > 0) SelectMainTab(mainTab - 1); });
            Btn(root, "NavForward", "▶", 50, 4, 90, 40, ButtonIdle, SmallPt).onClick.AddListener(() => { if (mainTab < MainTabCount - 1) SelectMainTab(mainTab + 1); });
            Btn(root, "NavHome", "홈", 94, 4, 150, 40, ButtonIdle, SmallPt).onClick.AddListener(GoMainHome);
            var actions = new (string label, Action action)[][]
            {
                new (string, Action)[] { ("저장하기", SaveNow), ("감독 설정 · 새 게임", OpenManagerSetup), ("새 시즌/난이도 모달", OpenSeasonSettings), ("클래식 로비", GoClassicLobby) },
                new (string, Action)[] { ("진행하기 (메인 홈 → 전력 분석)", () => Continue()), ("한 경기 전력 분석", OpenPreGame), ("실시간 시즌 대시보드", OpenDashboard), ("직전 경기 결과", OpenLastBox) },
                new (string, Action)[] { ("감독 설정 · 플레이 모드", OpenManagerSetup), ("구단주 목표 · 재정", GoMainHome), ("스토리 안건", () => { SelectMainTab(0); SelectSubTab(5); }) },
                new (string, Action)[] { ("순위 · 리더 · 소식", () => SelectMainTab(1)), ("시즌 일정 (캘린더)", OpenSchedule), ("포스트시즌 트리", OpenPostseasonTree), ("시상식", OpenAwards) },
                new (string, Action)[] { ("선수단 · 라인업", () => SelectMainTab(2)), ("트레이드 · FA 시장", () => SelectMainTab(4)), ("응원단", () => SelectMainTab(6)), ("구단 역사", () => SelectMainTab(7)) },
                new (string, Action)[] { ("한 경기 (3단계 플로우)", OpenPreGame), ("전반기 · 한 시즌 (대시보드)", OpenDashboard), ("포스트시즌 다음 경기", () => PlayNextPostseason()) },
            };
            for (int i = 0; i < 6; i++)
            {
                int index = i;
                float x0 = 160 + i * 168;
                menuButtons[i] = Btn(root, $"Menu{i}", MenuTitles[i] + " ▼", x0, 4, x0 + 160, 40, new Color(1f, 1f, 1f, 0.06f), SmallPt + 1);
                var list = CompyaUiKit.Fill(root, $"MenuList{i}");
                var items = actions[i];
                CompyaUiKit.Box(list, "Bg", x0, 44, x0 + 330, 48 + items.Length * 44, new Color(0.11f, 0.11f, 0.13f, 0.98f));
                for (int k = 0; k < items.Length; k++)
                {
                    var act = items[k].action;
                    Btn(list, $"MenuItem{k}", items[k].label, x0 + 4, 48 + k * 44, x0 + 326, 88 + k * 44, ButtonIdle, SmallPt).onClick.AddListener(() => { CloseMenus(); act(); });
                }
                list.gameObject.SetActive(false);
                menuLists[i] = list;
                menuButtons[i].onClick.AddListener(() => ToggleMenu(index));
            }
            toolbarInfo1 = L(root, "ToolbarInfo1", "", 1180, 2, 1910, 22, CellPt, TextAnchor.MiddleRight, White);
            toolbarInfo2 = L(root, "ToolbarInfo2", "", 1180, 23, 1910, 43, CellPt, TextAnchor.MiddleRight, Muted);
        }

        public void ToggleMenu(int index)
        {
            for (int i = 0; i < menuLists.Length; i++)
            {
                if (menuLists[i] == null) continue;
                bool open = i == index && !menuLists[i].gameObject.activeSelf;
                menuLists[i].gameObject.SetActive(open);
                if (open) menuLists[i].SetAsLastSibling();
            }
        }

        public void CloseMenus()
        {
            foreach (var m in menuLists) if (m != null) m.gameObject.SetActive(false);
        }

        public RectTransform MenuList(int index) => index >= 0 && index < menuLists.Length ? menuLists[index] : null;

        private void BuildSidebar()
        {
            CompyaUiKit.Box(root, "SidebarBg", 1868, 246, 1920, 1040, new Color(0.06f, 0.06f, 0.07f));
            var actions = new Action[] { GoMainHome, OpenSchedule, () => SelectMainTab(1), () => SelectMainTab(2), () => { SelectMainTab(0); SelectSubTab(1); },
                () => SelectMainTab(4), () => SelectMainTab(6), OpenPostseasonTree, OpenManagerSetup };
            for (int i = 0; i < SidebarCount; i++)
            {
                var act = actions[i];
                float y0 = 252 + i * 54;
                sidebarButtons[i] = Btn(root, $"Quick{i}", SidebarLabels[i], 1870, y0, 1918, y0 + 48, new Color(1f, 1f, 1f, 0.08f), CellPt);
                sidebarButtons[i].onClick.AddListener(() => act());
            }
        }

        // ================================================================== 시즌 일정(215611)

        private static readonly string[] WeekdayLabels = { "일요일", "월요일", "화요일", "수요일", "목요일", "금요일", "토요일" };
        private const float GridX0 = 580, GridX1 = 1860, GridY0 = 326, GridY1 = 1036;

        private void BuildSchedulePane()
        {
            var pane = PaneRoot(PaneSchedule);
            var card = CompyaUiKit.Fill(pane, "MatchupCardPanel");
            CompyaUiKit.Box(card, "Bg", 12, 248, 560, 1040, new Color(0.12f, 0.14f, 0.2f));
            scTopPoly = CompyaUiKit.Polygon(card, "TopColor", 12, 248, 560, 1040, new Color(0.2f, 0.35f, 0.55f), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0.56f), new Vector2(0, 0.44f));
            scBottomPoly = CompyaUiKit.Polygon(card, "BottomColor", 12, 248, 560, 1040, new Color(0.7f, 0.1f, 0.12f), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 0.56f), new Vector2(0, 0.44f));
            scNextTitle = L(card, "NextTitle", "다음 상대", 28, 256, 548, 284, BodyPt, TextAnchor.MiddleLeft, White);
            scDate = L(card, "NextDate", "", 28, 286, 548, 314, BodyPt, TextAnchor.MiddleLeft, White);
            scMyLogo = CompyaUiKit.Logo(card, "MyLogo", 196, 326, 376, 466);
            scMyLabel = L(card, "MyLabel", "", 24, 474, 548, 512, PanelTitlePt + 3, TextAnchor.MiddleCenter, White);
            scMyStarter = L(card, "MyStarter", "", 24, 514, 548, 542, BodyPt, TextAnchor.MiddleCenter, White);
            scVersus = L(card, "Versus", "vs", 24, 600, 548, 640, PanelTitlePt + 3, TextAnchor.MiddleCenter, White);
            scOppLogo = CompyaUiKit.Logo(card, "OppLogo", 196, 680, 376, 820);
            scOppLabel = L(card, "OppLabel", "", 24, 828, 548, 866, PanelTitlePt + 3, TextAnchor.MiddleCenter, White);
            scOppStarter = L(card, "OppStarter", "", 24, 868, 548, 896, BodyPt, TextAnchor.MiddleCenter, White);
            scHeadToHead = L(card, "HeadToHead", "", 24, 910, 548, 1034, BodyPt, TextAnchor.UpperCenter, White);

            scMonthTitle = L(pane, "MonthTitle", "", GridX0, 252, 1040, 288, PanelTitlePt + 1, TextAnchor.MiddleLeft, Gold);
            for (int m = 0; m < 12; m++)
            {
                int month = m + 1;
                float x0 = 1048 + m * 67;
                scMonthTabs[m] = Btn(pane, $"MonthTab{month}", $"{month}월", x0, 254, x0 + 63, 288, ButtonIdle, CellPt);
                scMonthTabs[m].onClick.AddListener(() => SelectScheduleMonth(month));
            }
            float cw = (GridX1 - GridX0) / 7f, ch = (GridY1 - GridY0) / ScheduleRows;
            for (int c = 0; c < 7; c++)
                L(pane, $"Weekday{c}", WeekdayLabels[c], GridX0 + c * cw, 292, GridX0 + (c + 1) * cw - 4, 322, CellPt, TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < ScheduleRows; r++)
                for (int c = 0; c < 7; c++)
                {
                    float x0 = GridX0 + c * cw, y0 = GridY0 + r * ch, x1 = x0 + cw - 6, y1 = y0 + ch - 6;
                    var cell = CompyaUiKit.Fill(pane, $"Day{r}_{c}");
                    scCells[r, c] = CompyaUiKit.Box(cell, "Bg", x0, y0, x1, y1, new Color(0.14f, 0.14f, 0.16f));
                    scDay[r, c] = L(cell, "Num", "", x0 + 6, y0 + 4, x0 + 56, y0 + 30, BodyPt - 1, TextAnchor.MiddleLeft, White);
                    scLogos[r, c] = CompyaUiKit.Logo(cell, "Logo", x0 + 62, y0 + 6, x1 - 50, y0 + 58);
                    scOpp[r, c] = L(cell, "Opp", "", x0 + 4, y0 + 60, x1 - 4, y0 + 86, BodyPt - 1, TextAnchor.MiddleCenter, White);
                    scInfo[r, c] = L(cell, "Info", "", x0 + 4, y0 + 86, x1 - 4, y1 - 2, CellPt, TextAnchor.MiddleCenter, White);
                }
        }

        public void OpenSchedule()
        {
            if (subTabs == null) Build();
            mainTab = 1;
            subTab = subTabs[1].FindIndex(s => s.Pane == PaneSchedule);
            if (simulator != null && League != null)
                scheduleMonth = simulator.IsSeasonComplete ? 10 : GMLiveSeasonSimulator.DateOf(Math.Min(simulator.GamesPlayed, GMLiveSeasonSimulator.SeasonGames - 1), League.SeasonYear).Month;
            SwitchPane(PaneSchedule);
        }

        public void SelectScheduleMonth(int month)
        {
            scheduleMonth = Mathf.Clamp(month, 1, 12);
            Refresh();
        }

        /// <summary>시즌 날짜 → 경기 일차(0~143). 경기 없는 날은 없음.</summary>
        private Dictionary<DateTime, int> DayMap()
        {
            var map = new Dictionary<DateTime, int>();
            int year = League?.SeasonYear ?? GMFeatureFlags.DEFAULT_START_YEAR;
            for (int d = 0; d < GMLiveSeasonSimulator.SeasonGames; d++) map[GMLiveSeasonSimulator.DateOf(d, year).Date] = d;
            return map;
        }

        /// <summary>KBO 경기 개시 시각 - 평일 18:30 · 토요일 17:00 · 일요일 14:00.</summary>
        public static string GameTime(DateTime date) => date.DayOfWeek == DayOfWeek.Sunday ? "14:00" : date.DayOfWeek == DayOfWeek.Saturday ? "17:00" : "18:30";

        private void RefreshSchedule()
        {
            var league = League;
            var team = UserTeam;
            string me = team.TeamCode;
            int year = league.SeasonYear;
            var map = DayMap();
            var myColor = CompyaUiKit.TeamColor(team.Team);

            // 좌측 매치업 카드
            int next = simulator.GamesPlayed;
            var m = next < GMLiveSeasonSimulator.SeasonGames ? simulator.MatchesOn(next).FirstOrDefault(x => x.home == me || x.away == me) : default;
            var myRec = league.RecordOf(me);
            CompyaUiKit.SetLogo(scMyLogo, team.Team, 1f);
            scMyLabel.text = $"{CompyaUiKit.ShortName(team.Team)} ({myRec.W}-{myRec.L})";
            scTopPoly.color = CompyaUiKit.Darken(myColor, 0.85f);
            if (m.home != null)
            {
                string opp = m.home == me ? m.away : m.home;
                var oppTeam = league.Teams[opp];
                var oppRec = league.RecordOf(opp);
                var date = GMLiveSeasonSimulator.DateOf(next, year);
                scNextTitle.text = "다음 상대";
                scDate.text = $"{date:M월 d일 yyyy} ({WeekdayLabels[(int)date.DayOfWeek].Substring(0, 1)}) {GameTime(date)} · {CompyaUiKit.Stadium(NameAliasTable.ToTeam(m.home))}";
                var myStarter = StartingRotation.PickFor(team.AvailableRoster, next, team.Lineup);
                var oppStarter = StartingRotation.PickFor(oppTeam.AvailableRoster, next, oppTeam.Lineup);
                scMyStarter.text = myStarter != null ? $"선발 {myStarter.Template.PlayerName}" : "선발 미정";
                scVersus.text = m.home == me ? "vs (홈)" : "@ (원정)";
                CompyaUiKit.SetLogo(scOppLogo, oppTeam.Team, 1f);
                scOppLabel.text = $"{CompyaUiKit.ShortName(oppTeam.Team)} ({oppRec.W}-{oppRec.L})";
                scOppStarter.text = oppStarter != null ? $"선발 {oppStarter.Template.PlayerName}" : "선발 미정";
                scBottomPoly.color = CompyaUiKit.Darken(CompyaUiKit.TeamColor(oppTeam.Team), 0.85f);
                var h2h = league.UserResults.Where(r => r.OpponentCode == opp).ToList();
                scHeadToHead.text = $"시즌 상대 전적 {h2h.Count(r => r.My > r.Their)}승 {h2h.Count(r => r.My == r.Their)}무 {h2h.Count(r => r.My < r.Their)}패\n" +
                                    $"남은 맞대결 {Enumerable.Range(next, GMLiveSeasonSimulator.SeasonGames - next).Count(d => simulator.MatchesOn(d).Any(x => (x.home == me && x.away == opp) || (x.away == me && x.home == opp)))}경기";
            }
            else
            {
                scNextTitle.text = "정규시즌 종료";
                scDate.text = $"{year} 정규시즌 144경기를 모두 마쳤습니다.";
                scMyStarter.text = "";
                scVersus.text = "포스트시즌";
                CompyaUiKit.SetLogo(scOppLogo, Team.None, 0f);
                scOppLabel.text = "플레이오프 트리";
                scOppStarter.text = "[PS] 사이드바 · KBO 메뉴에서 확인";
                scBottomPoly.color = new Color(0.3f, 0.3f, 0.34f);
                scHeadToHead.text = $"최종 {myRec.W}승 {myRec.D}무 {myRec.L}패 · {GMFrontOffice.RankOf(league, me)}위";
            }

            // 우측 달력
            scMonthTitle.text = $"{year}년 {scheduleMonth}월 일정";
            for (int i = 0; i < 12; i++) scMonthTabs[i].targetGraphic.color = i + 1 == scheduleMonth ? new Color(Gold.r, Gold.g, Gold.b, 0.45f) : ButtonIdle;
            var first = new DateTime(year, scheduleMonth, 1);
            int offset = (int)first.DayOfWeek;
            int days = DateTime.DaysInMonth(year, scheduleMonth);
            for (int r = 0; r < ScheduleRows; r++)
                for (int c = 0; c < 7; c++)
                {
                    int dayOfMonth = r * 7 + c - offset + 1;
                    bool inMonth = dayOfMonth >= 1 && dayOfMonth <= days;
                    scDay[r, c].text = inMonth ? dayOfMonth.ToString() : "";
                    scOpp[r, c].text = scInfo[r, c].text = "";
                    CompyaUiKit.SetLogo(scLogos[r, c], Team.None, 0f);
                    scCells[r, c].color = inMonth ? new Color(0.14f, 0.14f, 0.16f) : new Color(0.1f, 0.1f, 0.11f, 0.6f);
                    if (!inMonth) continue;
                    var date = new DateTime(year, scheduleMonth, dayOfMonth);
                    if (!map.TryGetValue(date, out int d)) continue;
                    var game = simulator.MatchesOn(d).FirstOrDefault(x => x.home == me || x.away == me);
                    if (game.home == null) continue;
                    bool home = game.home == me;
                    string opp = home ? game.away : game.home;
                    var oppTeam = NameAliasTable.ToTeam(opp);
                    CompyaUiKit.SetLogo(scLogos[r, c], oppTeam, 1f);
                    scOpp[r, c].text = $"{(home ? "vs" : "@")} {CompyaUiKit.ShortName(oppTeam)}";
                    var result = league.ResultOn(d);
                    scInfo[r, c].text = result != null ? result.Label : d < simulator.GamesPlayed ? "경기 종료" : GameTime(date);
                    scInfo[r, c].color = result == null ? White : result.My > result.Their ? GreenOk : result.My < result.Their ? RedBad : Muted;
                    scCells[r, c].color = home ? CompyaUiKit.Darken(myColor, 0.75f) : new Color(0.32f, 0.33f, 0.36f);
                    if (d == simulator.GamesPlayed) scCells[r, c].color = Color.Lerp(scCells[r, c].color, Gold, 0.35f);
                }
        }

        // ================================================================== 포스트시즌 트리(215614)

        public static readonly string[] BracketHeads = { "와일드카드 결정전", "준플레이오프", "플레이오프", "한국시리즈" };

        private static float BracketX0(int col) => 12 + col * 456;
        private static float BracketRowY0(int col, int row) => 330 + col * 150 + row * 80;

        private void BuildPostseasonPane()
        {
            var pane = PaneRoot(PanePostseason);
            for (int col = 0; col < 4; col++)
            {
                float x0 = BracketX0(col), x1 = x0 + 440;
                CompyaUiKit.Box(pane, $"HeadBg{col}", x0, 252, x1, 292, PanelColor);
                L(pane, $"BracketHead{col}", BracketHeads[col], x0, 252, x1, 292, BodyPt, TextAnchor.MiddleCenter, Muted);
                for (int row = 0; row < 2; row++)
                {
                    float y0 = BracketRowY0(col, row), y1 = y0 + 70;
                    var slot = CompyaUiKit.Fill(pane, $"Bracket{col}_{row}");
                    psRows[col, row] = CompyaUiKit.Box(slot, "Bg", x0, y0, x1, y1, new Color(0.17f, 0.17f, 0.19f));
                    psLogos[col, row] = CompyaUiKit.Logo(slot, "Logo", x0 + 8, y0 + 8, x0 + 62, y1 - 8);
                    psNames[col, row] = L(slot, "Name", "", x0 + 72, y0, x0 + 360, y1, PanelTitlePt + 1, TextAnchor.MiddleLeft, White);
                    psWins[col, row] = L(slot, "Wins", "", x0 + 364, y0, x1 - 12, y1, PanelTitlePt + 5, TextAnchor.MiddleRight, White);
                }
                if (col > 0) CompyaUiKit.Box(pane, $"Connector{col}", BracketX0(col - 1) + 440, BracketRowY0(col - 1, 0) + 75, BracketX0(col), BracketRowY0(col - 1, 0) + 79, new Color(1f, 1f, 1f, 0.3f));
                float gy0 = BracketRowY0(col, 1) + 80;
                psGames[col] = L(pane, $"SeriesGames{col}", "", x0, gy0, x1, col == 3 ? 1036 : gy0 + 120, CellPt, TextAnchor.UpperLeft, Muted);
                psGames[col].lineSpacing = 1.05f;
                psGames[col].verticalOverflow = VerticalWrapMode.Truncate;
            }
            psChampion = L(pane, "Champion", "", BracketX0(3), 330, BracketX0(3) + 440, 470, PanelTitlePt + 3, TextAnchor.MiddleCenter, Gold);
            psNext = L(pane, "NextGame", "", 12, 880, 904, 944, BodyPt, TextAnchor.MiddleLeft, White);
            psNextButton = Btn(pane, "NextGameButton", "다음 경기 진행 ▶", 12, 952, 440, 1036, ContinueGreen, ButtonPt + 2);
            psAutoButton = Btn(pane, "AutoPostseasonButton", "남은 포스트시즌 자동 진행", 468, 952, 904, 1036, ButtonIdle, ButtonPt);
            psNextButton.onClick.AddListener(() => PlayNextPostseason());
            psAutoButton.onClick.AddListener(() => AutoPostseason());
        }

        public void OpenPostseasonTree()
        {
            if (subTabs == null) Build();
            mainTab = 7;
            subTab = subTabs[7].FindIndex(s => s.Pane == PanePostseason);
            SwitchPane(PanePostseason);
        }

        /// <summary>
        /// [다음 경기 진행] - 내 구단 경기면 ① 전력 분석 → ② 실시간 이닝 경기 → ③ 경기 결과(3단계)로, 다른 구단 경기면 1경기를 바로 진행해 브래킷에 반영한다.
        /// 포스트시즌이 끝났으면 시상식을 연다. 진행(또는 화면 전환)했으면 true.
        /// </summary>
        public bool PlayNextPostseason()
        {
            if (simulator == null || !simulator.IsSeasonComplete) { SetStatus("정규시즌 144경기를 마친 뒤 포스트시즌이 열립니다."); return false; }
            var game = GMAwardEvaluator.NextPostseasonGame(simulator);
            if (game == null) { OpenAwards(); return true; }
            if (game.IsUserGame)
            {
                var view = PrePostView != null ? PrePostView : FindAnyObjectByType<GMMatchPrePostUIController>(FindObjectsInactive.Include);
                if (view != null && view.ShowPostseasonPreGame(simulator, game))
                {
                    view.OnClosed -= Refresh; view.OnClosed += Refresh;
                    SetStatus($"{game.Title} - 내 구단 경기입니다. 전력 분석 후 [플레이 볼]로 지휘하십시오.");
                    return true;
                }
            }
            var played = GMAwardEvaluator.PlayNextPostseasonGame(simulator);
            var series = GMAwardEvaluator.BeginPostseason(simulator)?.Series.ElementAtOrDefault(played?.SeriesIndex ?? -1);
            SetStatus(played != null && series != null ? $"{played.Title} 결과: {series.Games.LastOrDefault()}" : "포스트시즌이 끝났습니다.");
            Refresh();
            return played != null;
        }

        /// <summary>[남은 포스트시즌 자동 진행] - 남은 경기를 모두 시뮬레이션하고(내 구단 경기 포함) 시상식 준비 상태로 둔다.</summary>
        public bool AutoPostseason()
        {
            if (simulator == null || !simulator.IsSeasonComplete) return false;
            var ps = GMAwardEvaluator.RunPostseason(simulator);
            SetStatus(ps != null ? $"{ps.Series.Last().Round} 종료 - {NameAliasTable.DisplayTeamName(ps.ChampionCode)} 우승 · MVP {ps.KoreanSeriesMvp}" : "포스트시즌을 진행할 수 없습니다.");
            Refresh();
            return ps != null;
        }

        private void RefreshPostseason()
        {
            var league = League;
            string me = league.SelectedTeamCode;
            var ps = simulator.IsSeasonComplete ? GMAwardEvaluator.BeginPostseason(simulator) : null;
            var standings = simulator.Standings();
            var seeds = ps != null ? ps.SeedCodes : standings.Take(5).Select(r => r.TeamCode).ToList();
            for (int col = 0; col < 4; col++)
            {
                var series = ps != null && col < ps.Series.Count ? ps.Series[col] : null;
                string higher = series?.HigherCode ?? (seeds.Count > GMAwardEvaluator.PostseasonRounds[col].higherSeed ? seeds[GMAwardEvaluator.PostseasonRounds[col].higherSeed] : null);
                string lower = series?.LowerCode ?? (col == 0 && seeds.Count > 4 ? seeds[4] : null);
                for (int row = 0; row < 2; row++)
                {
                    string code = row == 0 ? higher : lower;
                    int wins = series == null ? 0 : row == 0 ? series.HigherWins : series.LowerWins;
                    bool winner = series != null && !string.IsNullOrEmpty(series.WinnerCode) && series.WinnerCode == code;
                    bool loser = series != null && !string.IsNullOrEmpty(series.WinnerCode) && series.WinnerCode != code;
                    int seed = code != null ? seeds.IndexOf(code) + 1 : 0;
                    psNames[col, row].text = code == null ? "미정" : $"{seed}. {CompyaUiKit.ShortName(NameAliasTable.ToTeam(code))}{(code == me ? " (내 구단)" : "")}";
                    psNames[col, row].color = winner ? Gold : loser ? Muted : White;
                    psWins[col, row].text = code == null ? "" : wins.ToString();
                    psWins[col, row].color = winner ? Gold : White;
                    CompyaUiKit.SetLogo(psLogos[col, row], code != null ? NameAliasTable.ToTeam(code) : Team.None, code != null ? (loser ? 0.45f : 1f) : 0f);
                    psRows[col, row].color = code == me ? CompyaUiKit.Darken(CompyaUiKit.TeamColor(NameAliasTable.ToTeam(code)), 0.6f) : new Color(0.17f, 0.17f, 0.19f);
                }
                string need = col == 0 ? "4위 1승 어드밴티지 · 2선승" : col == 3 ? "4선승" : "3선승";
                psGames[col].text = series == null ? need : $"{need}\n{string.Join("\n", series.Games.Skip(Math.Max(0, series.Games.Count - (col == 3 ? 3 : 4))))}";
            }
            if (ps != null && ps.Completed)
            {
                psChampion.text = $"{league.SeasonYear} 한국시리즈 우승\n{NameAliasTable.DisplayTeamName(ps.ChampionCode)}\nMVP {ps.KoreanSeriesMvp}";
                psNext.text = "포스트시즌 종료 - [시상식 열기]로 KBO 시상식 · 골든글러브를 진행하십시오.";
                CompyaUiKit.SetButtonText(psNextButton, "시상식 열기 ▶");
                psAutoButton.interactable = false;
            }
            else
            {
                psChampion.text = "";
                var next = ps != null ? GMAwardEvaluator.NextPostseasonGame(simulator) : null;
                psNext.text = !simulator.IsSeasonComplete ? $"정규시즌 진행 중 - 현재 순위 기준 예상 대진(G {simulator.GamesPlayed}/{GMLiveSeasonSimulator.SeasonGames})"
                    : next == null ? "포스트시즌 준비 중"
                    : $"다음: {next.Title} {CompyaUiKit.ShortName(NameAliasTable.ToTeam(next.AwayCode))} @ {CompyaUiKit.ShortName(NameAliasTable.ToTeam(next.HomeCode))}{(next.IsUserGame ? " - 내 구단 경기(3단계 지휘)" : "")}";
                CompyaUiKit.SetButtonText(psNextButton, next != null && next.IsUserGame ? "내 구단 경기 지휘 ▶" : "다음 경기 진행 ▶");
                psAutoButton.interactable = simulator.IsSeasonComplete;
            }
            psNextButton.interactable = simulator.IsSeasonComplete;
        }

        // ================================================================== 감독 설정(215504)

        private void BuildManagerSetup()
        {
            managerPopup = CompyaUiKit.Fill(root, ManagerSetupName);
            CompyaUiKit.Paint(managerPopup, new Color(0f, 0f, 0f, 0.75f), true);
            CompyaUiKit.Box(managerPopup, "SetupBox", 200, 80, 1720, 1010, new Color(0.17f, 0.17f, 0.18f, 0.99f));
            CompyaUiKit.Box(managerPopup, "SetupTitleBar", 200, 80, 1720, 130, new Color(0.24f, 0.24f, 0.26f));
            L(managerPopup, "SetupTitle", "감독 설정", 200, 82, 1720, 128, PanelTitlePt + 5, TextAnchor.MiddleCenter, White);

            // 좌측 - 팀 선택
            L(managerPopup, "TeamSelectTitle", "팀 선택", 230, 140, 760, 172, BodyPt, TextAnchor.MiddleLeft, Muted);
            L(managerPopup, "LeagueLabel", "선택한 리그: KBO 리그", 230, 176, 760, 208, BodyPt, TextAnchor.MiddleLeft, White);
            Btn(managerPopup, "RandomTeam", "무작위로 시작", 230, 214, 480, 250, ButtonIdle, SmallPt).onClick.AddListener(PickRandomTeam);
            mgTeamLabel = L(managerPopup, "TeamPicked", "", 490, 214, 770, 250, SmallPt, TextAnchor.MiddleLeft, Gold);
            var codes = NameAliasTable.CanonicalTeamCodes;
            for (int i = 0; i < 10 && i < codes.Length; i++)
            {
                string code = codes[i];
                float y0 = 262 + i * 56;
                var logo = CompyaUiKit.Logo(managerPopup, $"TeamLogo{i}", 230, y0 + 4, 276, y0 + 46);
                CompyaUiKit.SetLogo(logo, NameAliasTable.ToTeam(code), 1f);
                mgTeamButtons[i] = Btn(managerPopup, $"TeamPick{i}", CompyaUiKit.FullName(NameAliasTable.ToTeam(code)), 284, y0, 770, y0 + 50, new Color(1f, 1f, 1f, 0.05f), BodyPt);
                var label = mgTeamButtons[i].GetComponentInChildren<Text>(true);
                if (label != null) label.alignment = TextAnchor.MiddleLeft;
                mgTeamButtons[i].onClick.AddListener(() => SelectManagerTeam(code));
            }

            // 우측 - 프로필 · 플레이 모드
            L(managerPopup, "ProfileTitle", "프로필", 810, 140, 1700, 172, BodyPt, TextAnchor.MiddleLeft, Muted);
            L(managerPopup, "NameLabel", "이름", 810, 178, 990, 212, BodyPt, TextAnchor.MiddleLeft, White);
            mgNameInput = BuildInput(managerPopup, "NameInput", 1000, 178, 1700, 212);
            L(managerPopup, "RoleLabel", "당신의 역할", 810, 218, 990, 252, BodyPt, TextAnchor.MiddleLeft, White);
            mgRole = Btn(managerPopup, "RoleDrop", "", 1000, 218, 1400, 252, new Color(0.25f, 0.25f, 0.28f), SmallPt);
            L(managerPopup, "ModeLabel", "시작 모드", 810, 258, 990, 292, BodyPt, TextAnchor.MiddleLeft, White);
            mgMode = Btn(managerPopup, "ModeDrop", "", 1000, 258, 1400, 292, new Color(0.25f, 0.25f, 0.28f), SmallPt);
            mgVirtual = Btn(managerPopup, "VirtualToggle", "", 1410, 258, 1700, 292, new Color(0.25f, 0.25f, 0.28f), SmallPt);
            mgVirtual.onClick.AddListener(() => { mgVirtualNames = !mgVirtualNames; RefreshManagerSetup(); });
            L(managerPopup, "PlayModeTitle", "플레이 모드", 810, 300, 1700, 330, BodyPt, TextAnchor.MiddleLeft, Muted);
            for (int k = 0; k < ManagerOptionCount; k++)
            {
                int index = k;
                float y0 = 334 + k * 92;
                mgOptions[k] = Btn(managerPopup, $"Option{k}", "", 810, y0, 1700, y0 + 38, new Color(1f, 1f, 1f, 0.04f), BodyPt);
                var label = mgOptions[k].GetComponentInChildren<Text>(true);
                if (label != null) label.alignment = TextAnchor.MiddleLeft;
                mgOptions[k].onClick.AddListener(() => ToggleManagerOption(index));
                L(managerPopup, $"OptionDesc{k}", ManagerOptionDescs[k], 850, y0 + 40, 1700, y0 + 86, CellPt, TextAnchor.UpperLeft, Muted);
            }
            L(managerPopup, "DifficultyLabel", "게임 난이도", 810, 798, 990, 834, BodyPt, TextAnchor.MiddleLeft, White);
            mgDifficulty = Btn(managerPopup, "DifficultyDrop", "", 1000, 798, 1400, 834, new Color(0.25f, 0.25f, 0.28f), SmallPt);
            mgDifficultyDesc = L(managerPopup, "DifficultyDesc", "", 810, 840, 1700, 900, CellPt, TextAnchor.UpperLeft, Muted);

            mgStart = Btn(managerPopup, "StartGameButton", "✔ 게임 시작", 1200, 930, 1440, 990, ContinueGreen, ButtonPt + 1);
            mgApply = Btn(managerPopup, "ApplyButton", "현재 리그에 적용", 950, 930, 1190, 990, new Color(0.25f, 0.4f, 0.7f), ButtonPt);
            Btn(managerPopup, "CancelButton", "취소", 1450, 930, 1700, 990, ButtonIdle, ButtonPt).onClick.AddListener(CloseManagerSetup);
            mgStart.onClick.AddListener(() => ConfirmManagerSetup());
            mgApply.onClick.AddListener(() => ApplyManagerSetupToCurrent());

            // 드롭다운 목록(맨 위에 그린다)
            mgRoleList = DropList(managerPopup, "RoleList", 1000, 252, 1400, new[] { "단장", "단장 및 감독" }, i => { mgProfile.Role = i; RefreshManagerSetup(); });
            mgModeList = DropList(managerPopup, "ModeList", 1000, 292, 1400, new[] { "2026 현역 모드", "올타임 드림 모드", "스토리 「꼴찌 구단의 겨울」" }, i => { mgStartMode = (GMStartMode)i; RefreshManagerSetup(); });
            mgDifficultyList = DropList(managerPopup, "DifficultyList", 1000, 834, 1400, GMFrontOffice.Difficulties.Select(GMFrontOffice.DifficultyLabel).ToArray(), i => { mgDiff = GMFrontOffice.Difficulties[i]; RefreshManagerSetup(); }, upward: true);
            mgRole.onClick.AddListener(() => ToggleDrop(mgRoleList));
            mgMode.onClick.AddListener(() => ToggleDrop(mgModeList));
            mgDifficulty.onClick.AddListener(() => ToggleDrop(mgDifficultyList));
            managerPopup.gameObject.SetActive(false);
        }

        private RectTransform DropList(Transform parent, string name, float x0, float y0, float x1, string[] options, Action<int> pick, bool upward = false)
        {
            var list = CompyaUiKit.Fill(parent, name);
            float h = 40f, top = upward ? y0 - 36 - options.Length * h - 4 : y0 + 2;
            CompyaUiKit.Box(list, "Bg", x0, top, x1, top + options.Length * h + 4, new Color(0.1f, 0.1f, 0.12f, 0.99f));
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                Btn(list, $"Item{i}", options[i], x0 + 2, top + 2 + i * h, x1 - 2, top + (i + 1) * h, ButtonIdle, SmallPt).onClick.AddListener(() => { list.gameObject.SetActive(false); pick(index); });
            }
            list.gameObject.SetActive(false);
            return list;
        }

        private static void ToggleDrop(RectTransform list)
        {
            if (list == null) return;
            bool open = !list.gameObject.activeSelf;
            list.gameObject.SetActive(open);
            if (open) list.SetAsLastSibling();
        }

        private InputField BuildInput(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var rect = CompyaUiKit.Place(parent, name, x0, y0, x1, y1);
            var bg = CompyaUiKit.Paint(rect, new Color(0.06f, 0.06f, 0.07f), true);
            var textRect = CompyaUiKit.Norm(rect, "Text", 0.02f, 0f, 0.98f, 1f);
            var text = kit.LabelOn(textRect, "", BodyPt / 0.9f, TextAnchor.MiddleLeft, White);
            TextTidy.Exact(text, BodyPt);
            text.supportRichText = false;
            var input = rect.gameObject.AddComponent<InputField>();
            input.targetGraphic = bg;
            input.textComponent = text;
            input.characterLimit = 16;
            return input;
        }

        public void OpenManagerSetup()
        {
            if (managerPopup == null) Build();
            CloseMenus();
            var fo = League != null ? GMFrontOffice.Ensure(League) : null;
            var current = fo?.Manager ?? new GMManagerProfile();
            mgProfile.Name = current.Name; mgProfile.Role = current.Role;
            mgProfile.Commissioner = current.Commissioner; mgProfile.NoFiring = current.NoFiring; mgProfile.HardTrade = current.HardTrade;
            mgProfile.PennantMode = current.PennantMode; mgProfile.Challenge = current.Challenge;
            mgTeam = League?.SelectedTeamCode ?? NameAliasTable.SAM;
            mgStartMode = League?.Mode ?? GMStartMode.RealCurrent2026;
            mgDiff = fo?.Difficulty ?? GMDifficulty.Majors;
            mgVirtualNames = League?.UseVirtualNames ?? GameSettings.UseVirtualNames;
            if (mgNameInput != null) mgNameInput.text = mgProfile.Name;
            managerPopup.gameObject.SetActive(true);
            managerPopup.SetAsLastSibling();
            RefreshManagerSetup();
        }

        public void CloseManagerSetup()
        {
            if (managerPopup != null) managerPopup.gameObject.SetActive(false);
        }

        public void SelectManagerTeam(string code)
        {
            mgTeam = NameAliasTable.ResolveCanonicalTeamCode(code) ?? mgTeam;
            RefreshManagerSetup();
        }

        public void PickRandomTeam()
        {
            var codes = NameAliasTable.CanonicalTeamCodes;
            mgTeam = codes[(Environment.TickCount & 0x7FFFFFFF) % codes.Length];
            RefreshManagerSetup();
        }

        public void ToggleManagerOption(int index)
        {
            switch (index)
            {
                case 0: mgProfile.Commissioner = !mgProfile.Commissioner; break;
                case 1: mgProfile.NoFiring = !mgProfile.NoFiring; break;
                case 2: mgProfile.HardTrade = !mgProfile.HardTrade; break;
                case 3: mgProfile.PennantMode = !mgProfile.PennantMode; break;
                case 4: mgProfile.Challenge = !mgProfile.Challenge; break;
            }
            RefreshManagerSetup();
        }

        public void SelectManagerDifficulty(GMDifficulty d) { mgDiff = d; RefreshManagerSetup(); }
        public void SelectManagerMode(GMStartMode m) { mgStartMode = m; RefreshManagerSetup(); }

        private static bool OptionOn(GMManagerProfile p, int k) => k == 0 ? p.Commissioner : k == 1 ? p.NoFiring : k == 2 ? p.HardTrade : k == 3 ? p.PennantMode : p.Challenge;

        private void RefreshManagerSetup()
        {
            if (managerPopup == null) return;
            var codes = NameAliasTable.CanonicalTeamCodes;
            for (int i = 0; i < mgTeamButtons.Length && i < codes.Length; i++)
                mgTeamButtons[i].targetGraphic.color = codes[i] == mgTeam ? new Color(0.25f, 0.42f, 0.8f, 0.85f) : new Color(1f, 1f, 1f, 0.05f);
            mgTeamLabel.text = $"선택: {NameAliasTable.DisplayTeamName(mgTeam)}";
            CompyaUiKit.SetButtonText(mgRole, $"{mgProfile.RoleLabel} ▼");
            CompyaUiKit.SetButtonText(mgMode, $"{GMModeLabel(mgStartMode)} ▼");
            CompyaUiKit.SetButtonText(mgVirtual, mgVirtualNames ? "선수 이름: 가상명" : "선수 이름: 실명");
            for (int k = 0; k < ManagerOptionCount; k++)
                CompyaUiKit.SetButtonText(mgOptions[k], $"{(OptionOn(mgProfile, k) ? "[✔]" : "[  ]")} {ManagerOptionLabels[k]}");
            CompyaUiKit.SetButtonText(mgDifficulty, $"{GMFrontOffice.DifficultyLabel(mgDiff)} ▼");
            mgDifficultyDesc.text = DifficultyDescs[Mathf.Clamp((int)mgDiff, 0, DifficultyDescs.Length - 1)];
            mgApply.interactable = League != null;
        }

        private void CaptureName()
        {
            var name = mgNameInput != null ? (mgNameInput.text ?? "").Trim() : "";
            mgProfile.Name = string.IsNullOrEmpty(name) ? "단장" : name;
        }

        private GMManagerProfile ProfileCopy() => new GMManagerProfile
        {
            Name = mgProfile.Name, Role = mgProfile.Role, Commissioner = mgProfile.Commissioner, NoFiring = mgProfile.NoFiring,
            HardTrade = mgProfile.HardTrade, PennantMode = mgProfile.PennantMode, Challenge = mgProfile.Challenge,
        };

        /// <summary>[✔ 게임 시작] - 선택 구단 · 시작 모드 · 실명/가상명으로 새 리그를 만들고 감독 설정(프로필 · 옵션 5종 · 난이도)을 적용한 뒤 메인 홈으로 간다.</summary>
        public GMLiveSeasonSimulator ConfirmManagerSetup()
        {
            CaptureName();
            GMLeagueState league;
            GMLiveSeasonSimulator sim = null;
            if (LeagueFactory != null) league = LeagueFactory(mgStartMode, mgTeam, mgVirtualNames);
            else if (GameManager.Instance != null)
            {
                league = GameManager.Instance.StartGMLeague(mgStartMode, mgTeam, mgVirtualNames);
                sim = GameManager.Instance.GMSimulator;
            }
            else league = GMRosterLoader.LoadModeRoster(mgStartMode, mgTeam, mgVirtualNames);
            if (league == null) { SetStatus("새 리그를 만들 수 없습니다(선수 데이터 없음)."); return null; }
            GMFrontOffice.ApplyManagerSetup(league, ProfileCopy(), mgDiff);
            sim = sim ?? new GMLiveSeasonSimulator(league);
            CloseManagerSetup();
            simulator = sim;
            ResetSelections();
            ApplyTheme();
            GoMainHome();
            SetStatus($"{mgProfile.Name} {mgProfile.RoleLabel} 부임 - {NameAliasTable.DisplayTeamName(league.SelectedTeamCode)} · {GMModeLabel(league.Mode)} · {GMFrontOffice.DifficultyLabel(mgDiff)}");
            return sim;
        }

        /// <summary>[현재 리그에 적용] - 구단 · 모드는 그대로 두고 프로필 · 플레이 모드 옵션 · 난이도만 바꾼다.</summary>
        public bool ApplyManagerSetupToCurrent()
        {
            if (League == null) return false;
            CaptureName();
            GMFrontOffice.ApplyManagerSetup(League, ProfileCopy(), mgDiff);
            CloseManagerSetup();
            SetStatus($"감독 설정 적용 - {mgProfile.Name} {mgProfile.RoleLabel} · {GMFrontOffice.DifficultyLabel(mgDiff)}");
            Refresh();
            return true;
        }

        // ================================================================== 라우팅 · 툴바 갱신

        /// <summary>[홈] - 리그 플레이 메인 홈(프런트 오피스 6분할).</summary>
        public void GoMainHome()
        {
            CloseMenus();
            if (subTabs == null) Build();
            mainTab = 0;
            subTab = 0;
            SwitchPane(PaneOwner);
        }

        private void SaveNow()
        {
            var save = SaveManager.Instance;
            if (save == null) { SetStatus("저장 관리자가 없습니다."); return; }
            save.SaveGame();
            SetStatus("저장했습니다.");
        }

        private void RefreshToolbar()
        {
            if (toolbarInfo1 == null) return;
            var league = League;
            var team = UserTeam;
            var manager = league != null ? GMFrontOffice.Manager(league) : null;
            CompyaUiKit.SetButtonText(menuButtons[2], $"{(manager != null ? manager.Name : "단장")} ▼");
            CompyaUiKit.SetButtonText(menuButtons[4], $"{(team != null ? CompyaUiKit.ShortName(team.Team) : "구단")} ▼");
            if (league == null || team == null || simulator == null) { toolbarInfo1.text = "KBO"; toolbarInfo2.text = ""; return; }
            var rec = league.RecordOf(team.TeamCode);
            string phase = simulator.IsSeasonComplete ? "포스트시즌" : simulator.GamesPlayed == 0 ? "스토브리그" : "정규시즌";
            var date = simulator.IsSeasonComplete ? new DateTime(league.SeasonYear, 10, 6) : GMLiveSeasonSimulator.DateOf(simulator.GamesPlayed, league.SeasonYear);
            toolbarInfo1.text = $"KBO {phase} | {date:yyyy년 M월 d일} ({WeekdayLabels[(int)date.DayOfWeek].Substring(0, 1)})";
            toolbarInfo2.text = $"{NameAliasTable.DisplayTeamName(team.TeamCode)} {rec.W}-{rec.L} · 승률 {GMTeamRecord.PctLabel(rec.Pct)} · {GMFrontOffice.RankOf(league, team.TeamCode)}위";
        }
    }
}
