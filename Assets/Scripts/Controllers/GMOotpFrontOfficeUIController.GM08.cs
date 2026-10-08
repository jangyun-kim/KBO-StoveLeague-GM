using System;
using System.Collections.Generic;
using System.Linq;
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
    /// [TASK-GM-08] 프런트 오피스 추가 화면(1920×1080 · Normal · 15pt 이상):
    ///   ① 포스트시즌 트리 최종 정렬(image_85c548 레퍼런스) - 좌측 KBO 리더(타율 · 홈런 · 타점 · 도루 · OPS · 타자 WAR · 연속 안타 · ERA · 승리 1~3위),
    ///      중앙 4열 브래킷(WILD CARD GAME → SEMI PLAYOFF → PLAYOFF → KOREAN SERIES · 엠블럼 · 구단명 · 붉은 시리즈 승수 · 연결선), 하단 MM/DD/YYYY 일일 리포트
    ///   ② 트레이드 1:N - 연봉 보조 조절 · [AI 단장 역제안] 팝업(니즈 · 패키지 · 가치 바 · 수락/슬롯 조정)
    ///   ③ FA 보상 · 보호 명단 - 내 구단 20/25인 추천 명단(10대 가중치 · 수동 토글) · AI 구단 보호 명단 미리보기 · 보상 정산
    ///   ④ 감독 설정 사운드 - BGM · 효과음 볼륨 슬라이더(GMAudioManager)
    /// </summary>
    public partial class GMOotpFrontOfficeUIController
    {
        public const string PaneProtect = "Pane_Protect", CounterPopupName = "TradeCounterPopup";
        public const int LeaderCategoriesShown = 9, ProtectRows = 14, OpenRows = 6;

        // ================================================================== ① 포스트시즌 트리

        public static readonly string[] BracketHeads = { "WILD CARD GAME", "SEMI PLAYOFF", "PLAYOFF", "KOREAN SERIES" };
        public static readonly string[] BracketSubs = { "3판 과반승 / 4위 1승 어드밴티지", "5판 과반승", "5판 과반승", "7판 과반승" };
        public static readonly GMLeaderCategory[] BracketLeaderCategories =
        {
            GMLeaderCategory.AVG, GMLeaderCategory.HR, GMLeaderCategory.RBI, GMLeaderCategory.SB, GMLeaderCategory.OPS,
            GMLeaderCategory.BatterWAR, GMLeaderCategory.HitStreak, GMLeaderCategory.ERA, GMLeaderCategory.Wins,
        };
        private static readonly Color SeriesRed = new Color(0.93f, 0.2f, 0.18f);

        private const float LeaderX0 = 12, LeaderX1 = 452;
        private const float BracketLeft = 470, BracketColW = 320, BracketGap = 27;
        private static float BracketX0(int col) => BracketLeft + col * (BracketColW + BracketGap);
        private static float BracketY0(int col) => 340 + col * 120;
        private const float BracketRowH = 56;

        private readonly Text[] psLeaderTitles = new Text[LeaderCategoriesShown];
        private readonly Text[,] psLeaderNames = new Text[LeaderCategoriesShown, 3], psLeaderValues = new Text[LeaderCategoriesShown, 3];
        private Text psReportDate, psReport;

        private void BuildPostseasonPane()
        {
            var pane = PaneRoot(PanePostseason);
            // 좌측 KBO 리더(2열 × 5행)
            var leaders = CompyaUiKit.Fill(pane, "LeadersPanel");
            CompyaUiKit.Box(leaders, "Bg", LeaderX0, 248, LeaderX1, 1036, PanelColor);
            L(leaders, "LeadersTitle", "KBO 리더", LeaderX0 + 10, 252, LeaderX1 - 10, 288, PanelTitlePt + 1, TextAnchor.MiddleLeft, Gold);
            for (int i = 0; i < LeaderCategoriesShown; i++)
            {
                float x0 = LeaderX0 + 8 + (i % 2) * 218, x1 = x0 + 210, y0 = 294 + (i / 2) * 148;
                psLeaderTitles[i] = L(leaders, $"LeaderCat{i}", GMLeaderCategories.Label(BracketLeaderCategories[i]), x0, y0, x1, y0 + 28, BodyPt, TextAnchor.MiddleLeft, Gold);
                for (int k = 0; k < 3; k++)
                {
                    float ry = y0 + 32 + k * 36;
                    psLeaderNames[i, k] = L(leaders, $"Leader{i}_{k}", "", x0, ry, x0 + 118, ry + 32, CellPt, TextAnchor.MiddleLeft, White);
                    psLeaderValues[i, k] = L(leaders, $"LeaderVal{i}_{k}", "", x0 + 120, ry, x1, ry + 32, CellPt, TextAnchor.MiddleRight, White);
                }
            }

            // 중앙 4열 브래킷
            for (int col = 0; col < 4; col++)
            {
                float x0 = BracketX0(col), x1 = x0 + BracketColW;
                CompyaUiKit.Box(pane, $"HeadBg{col}", x0, 252, x1, 320, PanelColor);
                L(pane, $"BracketHead{col}", BracketHeads[col], x0, 254, x1, 286, PanelTitlePt, TextAnchor.MiddleCenter, White);
                L(pane, $"BracketSub{col}", BracketSubs[col], x0, 288, x1, 318, CellPt, TextAnchor.MiddleCenter, Muted);
                for (int row = 0; row < 2; row++)
                {
                    float y0 = BracketY0(col) + row * (BracketRowH + 4), y1 = y0 + BracketRowH;
                    var slot = CompyaUiKit.Fill(pane, $"Bracket{col}_{row}");
                    psRows[col, row] = CompyaUiKit.Box(slot, "Bg", x0, y0, x1, y1, new Color(0.17f, 0.17f, 0.19f));
                    psLogos[col, row] = CompyaUiKit.Logo(slot, "Logo", x0 + 6, y0 + 6, x0 + 50, y1 - 6);
                    psNames[col, row] = L(slot, "Name", "", x0 + 58, y0, x1 - 62, y1, BodyPt + 1, TextAnchor.MiddleLeft, White);
                    psWins[col, row] = L(slot, "Wins", "", x1 - 58, y0, x1 - 10, y1, PanelTitlePt + 7, TextAnchor.MiddleRight, SeriesRed);
                }
                if (col > 0)
                {
                    // 연결선: 앞 열 브래킷 오른쪽 중앙 → 이번 열 하단(진출 팀) 행 중앙
                    float fromY = BracketY0(col - 1) + BracketRowH + 2, toY = BracketY0(col) + BracketRowH + 4 + BracketRowH / 2f;
                    float midX = BracketX0(col) - BracketGap / 2f;
                    var line = new Color(1f, 1f, 1f, 0.35f);
                    CompyaUiKit.Box(pane, $"Connector{col}_A", BracketX0(col - 1) + BracketColW, fromY - 1.5f, midX + 1.5f, fromY + 1.5f, line);
                    CompyaUiKit.Box(pane, $"Connector{col}_B", midX - 1.5f, fromY - 1.5f, midX + 1.5f, toY + 1.5f, line);
                    CompyaUiKit.Box(pane, $"Connector{col}_C", midX - 1.5f, toY - 1.5f, BracketX0(col), toY + 1.5f, line);
                }
                float gy0 = BracketY0(col) + 2 * BracketRowH + 12;
                psGames[col] = L(pane, $"SeriesGames{col}", "", x0, gy0, x1, 900, CellPt, TextAnchor.UpperLeft, Muted);
                psGames[col].lineSpacing = 1.05f;
                psGames[col].verticalOverflow = VerticalWrapMode.Truncate;
            }
            psChampion = L(pane, "Champion", "", BracketX0(3), 480, BracketX0(3) + BracketColW, 680, PanelTitlePt + 3, TextAnchor.MiddleCenter, Gold);

            // 하단 - 날짜 · 시리즈 진출 소식 · 일일 리포트 / 진행 버튼
            CompyaUiKit.Box(pane, "ReportBg", BracketLeft, 908, 1860, 1036, new Color(0.12f, 0.12f, 0.14f));
            psReportDate = L(pane, "ReportDate", "", BracketLeft + 10, 912, 1060, 942, BodyPt, TextAnchor.MiddleLeft, Gold);
            psReport = L(pane, "PostseasonReport", "", BracketLeft + 10, 944, 1270, 1034, CellPt, TextAnchor.UpperLeft, White);
            psReport.verticalOverflow = VerticalWrapMode.Truncate;
            psNext = L(pane, "NextGame", "", 1070, 912, 1856, 942, CellPt, TextAnchor.MiddleRight, White);
            psNextButton = Btn(pane, "NextGameButton", "다음 경기 진행 ▶", 1280, 950, 1568, 1030, ContinueGreen, ButtonPt + 1);
            psAutoButton = Btn(pane, "AutoPostseasonButton", "남은 경기 자동 진행", 1578, 950, 1856, 1030, ButtonIdle, ButtonPt);
            psNextButton.onClick.AddListener(() => PlayNextPostseason());
            psAutoButton.onClick.AddListener(() => AutoPostseason());
        }

        private void RefreshPostseason()
        {
            var league = League;
            string me = league.SelectedTeamCode;
            var ps = simulator.IsSeasonComplete ? GMAwardEvaluator.BeginPostseason(simulator) : null;
            var standings = simulator.Standings();
            var seeds = ps != null ? ps.SeedCodes : standings.Take(5).Select(r => r.TeamCode).ToList();

            // KBO 리더
            for (int i = 0; i < LeaderCategoriesShown; i++)
            {
                var cat = BracketLeaderCategories[i];
                var top = simulator.Leaders(cat, 3);
                for (int k = 0; k < 3; k++)
                {
                    var e = k < top.Count ? top[k] : null;
                    psLeaderNames[i, k].text = e != null ? $"{k + 1}. {e.Name}" : $"{k + 1}. -";
                    psLeaderNames[i, k].color = e != null && e.TeamCode == me ? Gold : White;
                    psLeaderValues[i, k].text = e != null ? $"{CompyaUiKit.ShortName(NameAliasTable.ToTeam(e.TeamCode))} {e.ValueLabel}" : "";
                }
            }

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
                    psWins[col, row].color = loser ? new Color(SeriesRed.r, SeriesRed.g, SeriesRed.b, 0.45f) : SeriesRed;
                    CompyaUiKit.SetLogo(psLogos[col, row], code != null ? NameAliasTable.ToTeam(code) : Team.None, code != null ? (loser ? 0.45f : 1f) : 0f);
                    psRows[col, row].color = code == me ? CompyaUiKit.Darken(CompyaUiKit.TeamColor(NameAliasTable.ToTeam(code)), 0.6f) : winner ? new Color(0.24f, 0.22f, 0.14f) : new Color(0.17f, 0.17f, 0.19f);
                }
                string need = col == 0 ? "4위 1승 어드밴티지 · 2선승" : col == 3 ? "4선승" : "3선승";
                psGames[col].text = series == null ? need : $"{need}\n{string.Join("\n", series.Games.Skip(Math.Max(0, series.Games.Count - (col == 0 ? 6 : col == 3 ? 3 : 4))))}";
            }

            var next = ps != null && !ps.Completed ? GMAwardEvaluator.NextPostseasonGame(simulator) : null;
            string date = next?.DateLabel ?? (ps != null && ps.Completed ? $"10/31/{league.SeasonYear}" : GMLiveSeasonSimulator.DateLabel(Math.Min(simulator.GamesPlayed, GMLiveSeasonSimulator.SeasonGames - 1), league.SeasonYear));
            psReportDate.text = $"{date} 포스트시즌 일일 리포트";
            var news = league.News.Where(n => n.Kind == GMNewsKind.Postseason).Take(3).Select(n => $"{n.DateLabel} {n.Title}").ToList();
            if (ps != null)
                foreach (var s in ps.Series.Where(s => !string.IsNullOrEmpty(s.WinnerCode)).Reverse().Take(Math.Max(0, 3 - news.Count)))
                    news.Add($"{CompyaUiKit.ShortName(NameAliasTable.ToTeam(s.WinnerCode))} {s.Round} 통과 ({Math.Max(s.HigherWins, s.LowerWins)}승 {Math.Min(s.HigherWins, s.LowerWins)}패)");
            psReport.text = news.Count > 0 ? string.Join("\n", news.Take(3)) : (simulator.IsSeasonComplete ? "와일드카드 결정전부터 1경기씩 진행합니다." : "정규시즌이 끝나면 상위 5개 구단이 가을야구에 나섭니다.");

            if (ps != null && ps.Completed)
            {
                psChampion.text = $"{league.SeasonYear} 한국시리즈 우승\n{NameAliasTable.DisplayTeamName(ps.ChampionCode)}\nMVP {ps.KoreanSeriesMvp}";
                psNext.text = "포스트시즌 종료 - 시상식으로 이어집니다.";
                CompyaUiKit.SetButtonText(psNextButton, "시상식 열기 ▶");
                psAutoButton.interactable = false;
            }
            else
            {
                psChampion.text = "";
                psNext.text = !simulator.IsSeasonComplete ? $"정규시즌 진행 중 - 현재 순위 기준 예상 대진 (G {simulator.GamesPlayed}/{GMLiveSeasonSimulator.SeasonGames})"
                    : next == null ? "포스트시즌 준비 중"
                    : $"다음: {next.Title} {CompyaUiKit.ShortName(NameAliasTable.ToTeam(next.AwayCode))} @ {CompyaUiKit.ShortName(NameAliasTable.ToTeam(next.HomeCode))}{(next.IsUserGame ? " - 내 구단(3단계 지휘)" : "")}";
                CompyaUiKit.SetButtonText(psNextButton, next != null && next.IsUserGame ? "내 구단 경기 지휘 ▶" : "다음 경기 진행 ▶");
                psAutoButton.interactable = simulator.IsSeasonComplete;
            }
            psNextButton.interactable = simulator.IsSeasonComplete;
        }

        // ================================================================== ② 트레이드 1:N · AI 역제안

        private int trCash;
        private Text trCashLabel;
        private RectTransform counterPopup;
        private Text cpTitle, cpPitch, cpNeeds, cpPackage, cpValue;
        private RectTransform cpValueFill;
        private GMTradeCounterOffer counterOffer;

        public int TradeCashSubsidy => trCash;
        public GMTradeCounterOffer CounterOffer => counterOffer;
        public bool IsCounterPopupOpen => counterPopup != null && counterPopup.gameObject.activeSelf;

        private void BuildTradeCashRow(Transform pane)
        {
            trCashLabel = L(pane, "TrCashLabel", "", 1264, 508, 1600, 540, CellPt, TextAnchor.MiddleLeft, White);
            Btn(pane, "TrCashMinus", $"-{GMTradeAI.CashStep:N0}만", 1606, 508, 1746, 540, ButtonIdle, CellPt).onClick.AddListener(() => SetTradeCash(trCash - GMTradeAI.CashStep * 5));
            Btn(pane, "TrCashPlus", $"+{GMTradeAI.CashStep * 5:N0}만", 1752, 508, 1896, 540, ButtonIdle, CellPt).onClick.AddListener(() => SetTradeCash(trCash + GMTradeAI.CashStep * 5));
        }

        /// <summary>연봉 보조(만 원, 0 ~ 5억) - 트레이드 가치 바가 즉시 바뀐다.</summary>
        public void SetTradeCash(int cash)
        {
            trCash = Mathf.Clamp(cash, 0, GMTradeAI.MaxCashSubsidy);
            Refresh();
        }

        private void RefreshTradeCash()
        {
            if (trCashLabel == null) return;
            trCashLabel.text = trCash > 0 ? $"연봉 보조 {GMDiagnosticFormat.Won(trCash)} (가치 +{GMTradeAI.CashValue(trCash):0.0})" : "연봉 보조 없음 (5,000만 원 단위 · 최대 5억)";
        }

        private void BuildCounterPopup()
        {
            counterPopup = CompyaUiKit.Fill(root, CounterPopupName);
            CompyaUiKit.Paint(counterPopup, new Color(0f, 0f, 0f, 0.72f), true);
            CompyaUiKit.Box(counterPopup, "CounterBox", 420, 230, 1500, 870, new Color(0.13f, 0.13f, 0.16f, 0.98f));
            cpTitle = L(counterPopup, "CounterTitle", "", 460, 248, 1460, 292, PanelTitlePt + 2, TextAnchor.MiddleLeft, Gold);
            cpNeeds = L(counterPopup, "CounterNeeds", "", 460, 296, 1460, 330, BodyPt, TextAnchor.MiddleLeft, Muted);
            cpPitch = L(counterPopup, "CounterPitch", "", 460, 336, 1460, 440, BodyPt, TextAnchor.UpperLeft, White);
            cpPitch.lineSpacing = 1.15f;
            cpPackage = L(counterPopup, "CounterPackage", "", 460, 446, 1460, 610, BodyPt, TextAnchor.UpperLeft, White);
            cpPackage.lineSpacing = 1.15f;
            cpValue = L(counterPopup, "CounterValue", "", 460, 616, 1460, 648, CellPt, TextAnchor.MiddleLeft, Gold);
            cpValueFill = Bar(counterPopup, "CounterValueBar", 460, 652, 1460, 676, BarGreen);
            Btn(counterPopup, "CounterAccept", "역제안 수락 · 체결", 460, 700, 800, 760, ContinueGreen, ButtonPt).onClick.AddListener(() => AcceptCounterOffer());
            Btn(counterPopup, "CounterLoad", "슬롯에 올려 직접 조정", 820, 700, 1140, 760, new Color(0.25f, 0.4f, 0.7f), ButtonPt).onClick.AddListener(LoadCounterIntoSlots);
            Btn(counterPopup, "CounterClose", "닫기", 1160, 700, 1460, 760, ButtonIdle, ButtonPt).onClick.AddListener(CloseCounterPopup);
            L(counterPopup, "CounterHint", "가치 바 100% 이상이면 상대 단장이 수락합니다. 슬롯에서 선수를 얹거나 빼면 바가 실시간으로 바뀝니다.", 460, 776, 1460, 840, CellPt, TextAnchor.UpperLeft, Muted);
            counterPopup.gameObject.SetActive(false);
        }

        /// <summary>[AI 단장 역제안 받기] - 받을 선수(상대 핵심 선수) 1명을 기준으로 AI가 1:N 패키지를 제시한다.</summary>
        public GMTradeCounterOffer RequestCounterOffer()
        {
            var partner = TradePartner;
            var target = trTheirs.FirstOrDefault();
            if (partner == null || target == null) { SetStatus("받을 상대 선수 1명을 먼저 고르십시오."); return null; }
            counterOffer = GMTradeAI.BuildCounterOffer(League, UserTeam, partner, target);
            ShowCounterPopup();
            SetStatus(counterOffer.Valid ? $"{partner.DisplayName} 단장 역제안 - {string.Join(" + ", counterOffer.Requested.Select(p => p.Template.PlayerName))}{(counterOffer.CashSubsidy > 0 ? $" + 연봉 보조 {GMDiagnosticFormat.Won(counterOffer.CashSubsidy)}" : "")}" : counterOffer.Pitch);
            return counterOffer;
        }

        private void ShowCounterPopup()
        {
            if (counterPopup == null || counterOffer == null) return;
            var partner = League.Teams.TryGetValue(counterOffer.PartnerCode, out var t) ? t : null;
            var target = counterOffer.Target;
            cpTitle.text = $"AI 단장 역제안 - {partner?.DisplayName} {target?.Template.PlayerName} ({GMFrontOffice.PositionLabel(target?.Position ?? "")} · OVR {target?.BaseOverall})";
            cpNeeds.text = $"구단 니즈: {GMTradeAI.NeedsLabel(counterOffer.Needs)}";
            cpPitch.text = counterOffer.Pitch;
            cpPackage.text = counterOffer.Valid
                ? "요구 패키지(1:" + counterOffer.Requested.Count + ")\n" + string.Join("\n", counterOffer.Requested.Select(p => $"· {PlayerLine(p)}{(UserTeam.Futures.Contains(p) ? " [퓨처스]" : "")}")) +
                  (counterOffer.CashSubsidy > 0 ? $"\n· 연봉 보조 {GMDiagnosticFormat.Won(counterOffer.CashSubsidy)}" : "")
                : "제시할 수 있는 패키지가 없습니다.";
            var e = counterOffer.Evaluation;
            float ratio = e != null && e.Required > 0 ? e.Ratio : 0f;
            cpValue.text = $"트레이드 가치 바: {ratio * 100:0}% ({(e != null && e.Acceptable ? "수락 가능" : "부족")})";
            SetFill(cpValueFill, Mathf.Clamp01(ratio / 1.5f));
            if (cpValueFill != null) cpValueFill.GetComponent<Image>().color = e != null && e.Acceptable ? BarGreen : BarRed;
            counterPopup.Find("CounterAccept").GetComponent<Button>().interactable = counterOffer.Valid && e != null && e.Acceptable;
            counterPopup.Find("CounterLoad").GetComponent<Button>().interactable = counterOffer.Valid;
            counterPopup.gameObject.SetActive(true);
            counterPopup.SetAsLastSibling();
        }

        public void CloseCounterPopup()
        {
            if (counterPopup != null) counterPopup.gameObject.SetActive(false);
        }

        public GMNegotiationResult AcceptCounterOffer()
        {
            if (counterOffer == null || !counterOffer.Valid) return new GMNegotiationResult { Message = "역제안이 없습니다." };
            if (TurnBlocked(PaneTrade, out var lockMsg)) return new GMNegotiationResult { Message = lockMsg }; // [TASK-GM-14]
            var incoming = counterOffer.Target;
            var r = GMTradeAI.AcceptCounterOffer(League, UserTeam, counterOffer);
            SetStatus(r.Message);
            if (r.Success) { trMine.Clear(); trTheirs.Clear(); trCash = 0; shopOffers.Clear(); shopTarget = null; counterOffer = null; CloseCounterPopup(); }
            Refresh();
            if (r.Success) PlayAudioEvent(ContractEventFor(incoming, false)); // [TASK-GM-12]
            return r;
        }

        /// <summary>역제안 패키지를 트레이드 슬롯에 올린다(이후 선수를 얹거나 빼며 가치 바로 협상).</summary>
        public void LoadCounterIntoSlots()
        {
            if (counterOffer == null || !counterOffer.Valid) return;
            trMine.Clear();
            trMine.AddRange(counterOffer.Requested.Take(GMStoveLeagueMarket.MaxTradeSide));
            trTheirs.Clear();
            trTheirs.Add(counterOffer.Target);
            trCash = counterOffer.CashSubsidy;
            shopTarget = trMine.FirstOrDefault();
            CloseCounterPopup();
            Refresh();
        }

        // ================================================================== ③ FA 보상 · 보호 명단

        private int protectSize = GMFaCompensation.ProtectA, protectPage, protectPartnerIndex;
        private readonly Button[] prRows = new Button[ProtectRows];
        private readonly Button[] prOpenRows = new Button[OpenRows];
        private Text prSummary, prPage, prPartnerName, prPartnerInfo, prDetail, prPending;
        private Button prSize20, prSize25;
        private GMProtectionList prMine, prPartner;
        private List<GMProtectionEntry> prShown = new List<GMProtectionEntry>();
        private List<GMProtectionEntry> prOpenShown = new List<GMProtectionEntry>();
        private GMProtectionEntry prSelected;

        public GMProtectionList MyProtection => prMine;
        public GMProtectionList PartnerProtection => prPartner;
        public int ProtectSize => protectSize;

        private void BuildProtectPane()
        {
            var pane = PaneRoot(PaneProtect);
            CompyaUiKit.Box(pane, "MineBg", 12, 248, 940, 1036, PanelColor);
            L(pane, "PrTitle", "내 구단 보호 명단 (10대 가중치 추천 · 클릭 = 수동 보호/해제)", 20, 252, 640, 288, CellPt, TextAnchor.MiddleLeft, Gold);
            prSize20 = Btn(pane, "PrSize20", "20인(A등급)", 648, 252, 786, 288, ButtonIdle, CellPt);
            prSize25 = Btn(pane, "PrSize25", "25인(B등급)", 792, 252, 932, 288, ButtonIdle, CellPt);
            prSize20.onClick.AddListener(() => SetProtectSize(GMFaCompensation.ProtectA));
            prSize25.onClick.AddListener(() => SetProtectSize(GMFaCompensation.ProtectB));
            for (int r = 0; r < ProtectRows; r++)
            {
                int row = r;
                float y0 = 294 + r * 48;
                prRows[r] = ListRow(pane, $"PrRow{r}", 16, y0, 936, y0 + 44, CellPt);
                prRows[r].onClick.AddListener(() => ToggleProtectRow(row));
            }
            Btn(pane, "PrPrev", "◀", 16, 972, 76, 1030, ButtonIdle, BodyPt).onClick.AddListener(() => { protectPage = Math.Max(0, protectPage - 1); Refresh(); });
            prPage = L(pane, "PrPage", "", 80, 972, 160, 1030, CellPt, TextAnchor.MiddleCenter, White);
            Btn(pane, "PrNext", "▶", 164, 972, 224, 1030, ButtonIdle, BodyPt).onClick.AddListener(() => { protectPage++; Refresh(); });
            prSummary = L(pane, "PrSummary", "", 232, 966, 700, 1034, CellPt, TextAnchor.MiddleLeft, White);
            Btn(pane, "PrReset", "추천 명단으로 초기화", 708, 972, 936, 1030, ButtonIdle, CellPt).onClick.AddListener(ResetUserProtection);

            CompyaUiKit.Box(pane, "PartnerBg", 952, 248, 1908, 640, PanelColor);
            Btn(pane, "PrPartnerPrev", "◀", 960, 252, 1030, 288, ButtonIdle, BodyPt).onClick.AddListener(() => ShiftProtectPartner(-1));
            prPartnerName = L(pane, "PrPartnerName", "", 1036, 252, 1824, 288, BodyPt, TextAnchor.MiddleCenter, Gold);
            Btn(pane, "PrPartnerNext", "▶", 1830, 252, 1900, 288, ButtonIdle, BodyPt).onClick.AddListener(() => ShiftProtectPartner(1));
            prPartnerInfo = L(pane, "PrPartnerInfo", "", 964, 292, 1900, 352, CellPt, TextAnchor.UpperLeft, Muted);
            L(pane, "PrOpenTitle", "AI 보호 명단 밖 상위 선수(보상선수 후보 · 클릭 = 상세 · 보상 지명)", 964, 356, 1900, 388, CellPt, TextAnchor.MiddleLeft, White);
            for (int i = 0; i < OpenRows; i++)
            {
                int index = i;
                float y0 = 392 + i * 41;
                prOpenRows[i] = ListRow(pane, $"PrOpen{i}", 960, y0, 1900, y0 + 38, CellPt);
                prOpenRows[i].onClick.AddListener(() => SelectProtectOpen(index));
            }

            CompyaUiKit.Box(pane, "DetailBg", 952, 648, 1908, 1036, PanelColor);
            prDetail = L(pane, "PrDetail", "", 964, 654, 1900, 870, CellPt, TextAnchor.UpperLeft, White);
            prDetail.lineSpacing = 1.05f;
            prPending = L(pane, "PrPending", "", 964, 874, 1560, 1032, CellPt, TextAnchor.UpperLeft, Gold);
            Btn(pane, "PrSettle", "보상 정산 진행", 1570, 900, 1900, 960, ContinueGreen, ButtonPt).onClick.AddListener(() => SettlePendingCompensations());
            Btn(pane, "PrClaim", "선택 선수 보상 지명", 1570, 970, 1900, 1030, new Color(0.45f, 0.36f, 0.12f), ButtonPt).onClick.AddListener(() => ClaimSelectedCompensation());
        }

        public void OpenProtection()
        {
            if (subTabs == null) Build();
            mainTab = 4;
            subTab = subTabs[4].FindIndex(s => s.Pane == PaneProtect);
            SwitchPane(PaneProtect);
        }

        public void SetProtectSize(int size)
        {
            protectSize = size >= GMFaCompensation.ProtectB ? GMFaCompensation.ProtectB : GMFaCompensation.ProtectA;
            protectPage = 0;
            Refresh();
        }

        private GMTeamState ProtectPartner => PartnerTeams().ElementAtOrDefault(((protectPartnerIndex % 9) + 9) % 9);

        public void ShiftProtectPartner(int delta)
        {
            int n = Math.Max(1, PartnerTeams().Count);
            protectPartnerIndex = ((protectPartnerIndex + delta) % n + n) % n;
            prSelected = null;
            Refresh();
        }

        /// <summary>내 구단 수동 보호 토글 - 처음 손대면 현재 추천 명단을 수동 명단으로 복사한 뒤 바꾼다(자동 보호 선수는 바꿀 수 없음).</summary>
        public void ToggleProtectRow(int row)
        {
            if (row >= prShown.Count || League == null) return;
            var e = prShown[row];
            prSelected = e;
            if (e.AutoProtected) { SetStatus($"{e.Player.Template.PlayerName} - {e.Tags} (자동 보호)"); Refresh(); return; }
            var ids = League.UserProtectedIds;
            if (ids.Count == 0 && prMine != null) ids.AddRange(prMine.Protected.Select(x => x.Player.InstanceId));
            if (ids.Contains(e.Player.InstanceId)) ids.Remove(e.Player.InstanceId);
            else if (ids.Count < protectSize) ids.Add(e.Player.InstanceId);
            else { SetStatus($"보호 인원 {protectSize}명이 가득 찼습니다 - 먼저 다른 선수를 해제하십시오."); Refresh(); return; }
            Refresh();
        }

        public void ResetUserProtection()
        {
            League?.UserProtectedIds.Clear();
            SetStatus("10대 가중치 추천 보호 명단으로 되돌렸습니다.");
            Refresh();
        }

        public void SelectProtectOpen(int index)
        {
            if (index >= prOpenShown.Count) return;
            prSelected = prOpenShown[index];
            Refresh();
        }

        /// <summary>[보상 정산 진행] - 정산 대기 중인 FA 보상을 처리한다(내 구단이 영입 측이면 원 소속 AI가 선택, 수령 측이면 AI 추천으로 대신 선택).</summary>
        public List<GMCompensationResult> SettlePendingCompensations()
        {
            var results = new List<GMCompensationResult>();
            if (League == null) return results;
            foreach (var pending in League.PendingCompensations.Where(c => c.ToTeam == League.SelectedTeamCode || c.FromTeam == League.SelectedTeamCode).ToList())
                results.Add(GMFaCompensation.Settle(League, pending));
            SetStatus(results.Count > 0 ? string.Join(" / ", results.Select(r => r.Message)) : "정산할 FA 보상이 없습니다.");
            Refresh();
            return results;
        }

        /// <summary>[선택 선수 보상 지명] - 내 구단이 FA를 빼앗긴 경우, 영입한 AI 구단의 보호 명단 밖 선수를 직접 지명한다.</summary>
        public GMCompensationResult ClaimSelectedCompensation()
        {
            if (League == null) return null;
            var partner = ProtectPartner;
            var pending = League.PendingCompensations.FirstOrDefault(c => c.FromTeam == League.SelectedTeamCode && c.ToTeam == partner?.TeamCode);
            if (pending == null) { SetStatus($"{partner?.DisplayName}에 대한 보상 수령 대기 건이 없습니다(우리 FA를 영입한 구단만 지명할 수 있습니다)."); return null; }
            var r = GMFaCompensation.Settle(League, pending, prSelected?.Player, humanChooses: true);
            SetStatus(r.Message);
            prSelected = null;
            Refresh();
            return r;
        }

        private void RefreshProtect()
        {
            var league = League;
            var team = UserTeam;
            prSize20.targetGraphic.color = protectSize == GMFaCompensation.ProtectA ? ButtonOn : ButtonIdle;
            prSize25.targetGraphic.color = protectSize == GMFaCompensation.ProtectB ? ButtonOn : ButtonIdle;
            prMine = GMFaCompensation.BuildProtectionList(league, team, protectSize, league.UserProtectedIds.Count > 0 ? league.UserProtectedIds : null);
            int pages = Math.Max(1, (prMine.Entries.Count + ProtectRows - 1) / ProtectRows);
            protectPage = Mathf.Clamp(protectPage, 0, pages - 1);
            prShown = prMine.Entries.Skip(protectPage * ProtectRows).Take(ProtectRows).ToList();
            prPage.text = $"{protectPage + 1}/{pages}";
            for (int r = 0; r < ProtectRows; r++)
            {
                var e = r < prShown.Count ? prShown[r] : null;
                prRows[r].gameObject.SetActive(e != null);
                if (e == null) continue;
                string mark = e.AutoProtected ? "[자동]" : e.Protected ? "[보호]" : "[    ]";
                bool futures = team.Futures.Contains(e.Player);
                CompyaUiKit.SetButtonText(prRows[r], $"{mark} {protectPage * ProtectRows + r + 1}. {GMFrontOffice.PositionLabel(e.Player.Position)} {e.Player.Template.PlayerName}{(futures ? "(퓨처스)" : "")} · {e.Player.Age}세 · OVR {e.Player.BaseOverall}/{e.Player.Potential} · 점수 {e.Score:0.0}{(string.IsNullOrEmpty(e.Tags) ? "" : " · " + e.Tags)}");
                prRows[r].targetGraphic.color = e == prSelected ? RowSelected : e.AutoProtected ? new Color(0.3f, 0.3f, 0.45f, 0.6f) : e.Protected ? new Color(0.16f, 0.4f, 0.22f, 0.7f) : r % 2 == 0 ? RowIdle : RowAlt;
            }
            prSummary.text = $"보호 {prMine.Protected.Count()}/{protectSize}명 · 자동 보호 {prMine.AutoProtected.Count()}명 · {(league.UserProtectedIds.Count > 0 ? "수동 명단" : "추천 명단")}\n{GMRosterTiers.Summary(team)}";

            var partner = ProtectPartner;
            prPartner = partner != null ? GMFaCompensation.BuildProtectionList(league, partner, protectSize) : null;
            prPartnerName.text = partner != null ? $"AI 구단 보호 명단 미리보기 - {partner.DisplayName} ({protectSize}인)" : "-";
            if (prPartner != null)
            {
                var w = prPartner.Weights;
                prPartnerInfo.text = $"노선 {(prPartner.WinNow ? "우승 도전(윈나우)" : "리빌딩")} · 단장 성향 {prPartner.PersonalityLabel}(±{prPartner.PersonalitySwing}) · 자동 보호 {prPartner.AutoProtected.Count()}명\n" +
                                     $"가중치 ①{w[0]:0}% ②{w[1]:0}% ③{w[2]:0}% ④{w[3]:0}% ⑤{w[4]:0}%{(prPartner.BalanceNotes.Count > 0 ? $" · ⑨ 검수 교체 {prPartner.BalanceNotes.Count}건" : "")}";
                prOpenShown = prPartner.Unprotected.Take(OpenRows).ToList();
            }
            else { prPartnerInfo.text = ""; prOpenShown.Clear(); }
            for (int i = 0; i < OpenRows; i++)
            {
                var e = i < prOpenShown.Count ? prOpenShown[i] : null;
                prOpenRows[i].gameObject.SetActive(e != null);
                if (e == null) continue;
                CompyaUiKit.SetButtonText(prOpenRows[i], $"{GMFrontOffice.PositionLabel(e.Player.Position)} {e.Player.Template.PlayerName} · {e.Player.Age}세 · OVR {e.Player.BaseOverall}/{e.Player.Potential} · 점수 {e.Score:0.0}");
                prOpenRows[i].targetGraphic.color = e == prSelected ? RowSelected : i % 2 == 0 ? RowIdle : RowAlt;
            }

            var s = prSelected;
            prDetail.text = s == null
                ? "선수를 클릭하면 10대 가중치 세부 점수를 보여 줍니다.\nFA 등급 - A: 보호 20인 외 1명 + 연봉 200%(또는 300%) · B: 보호 25인 외 1명 + 100%(또는 200%) · C: 연봉 150%"
                : $"{s.Player.Template.PlayerName} 최종 {s.Score:0.0}점 = 가중합 {s.Weighted:0.0} + ⑦ 희소성 {s.Scarcity:0} + ⑩ 성향 {s.Personality:+0;-0;0}\n" +
                  $"① 대체 불가능성 {s.Irreplaceability:0} · ② 잠재력 {s.Potential:0} · ③ 현재 OVR {s.Overall:0}\n④ 역할·워크에식 {s.RoleEthic:0} · ⑤ 계약 효율성 {s.ContractEfficiency:0}" +
                  $"{(string.IsNullOrEmpty(s.Tags) ? "" : $"\n태그: {s.Tags}")}{(s.AutoProtected ? "\n⑧ 자동 보호 - 명단 인원에서 제외" : "")}";
            var pendings = league.PendingCompensations.Where(c => c.ToTeam == league.SelectedTeamCode || c.FromTeam == league.SelectedTeamCode).ToList();
            prPending.text = pendings.Count == 0 ? "정산 대기 FA 보상 없음"
                : string.Join("\n", pendings.Take(4).Select(c => c.ToTeam == league.SelectedTeamCode
                    ? $"지급 대기: FA {c.PlayerName}({GMFaCompensation.GradeLabel(c.Grade)}) → 원 소속 {CompyaUiKit.ShortName(NameAliasTable.ToTeam(c.FromTeam))}"
                    : $"수령 대기: FA {c.PlayerName}({GMFaCompensation.GradeLabel(c.Grade)}) ← 영입 {CompyaUiKit.ShortName(NameAliasTable.ToTeam(c.ToTeam))} - 명단 밖 선수 지명 가능"));
        }

        // ================================================================== ④ 감독 설정 사운드

        private Slider mgBgmSlider, mgSfxSlider;
        private Text mgBgmLabel, mgSfxLabel;

        public Slider BgmVolumeSlider => mgBgmSlider;
        public Slider SfxVolumeSlider => mgSfxSlider;

        private void BuildSoundSettings(Transform popup)
        {
            L(popup, "SoundTitle", "사운드 (구단 BGM · 응원가/효과음)", 230, 830, 770, 858, BodyPt, TextAnchor.MiddleLeft, Muted);
            mgBgmLabel = L(popup, "BgmLabel", "", 230, 862, 420, 892, CellPt, TextAnchor.MiddleLeft, White);
            mgBgmSlider = BuildSlider(popup, "BgmVolumeSlider", 430, 864, 770, 890);
            mgSfxLabel = L(popup, "SfxLabel", "", 230, 898, 420, 928, CellPt, TextAnchor.MiddleLeft, White);
            mgSfxSlider = BuildSlider(popup, "SfxVolumeSlider", 430, 900, 770, 926);
            mgBgmSlider.onValueChanged.AddListener(v => { GMAudioManager.Ensure().BgmVolume = v; RefreshSoundLabels(); });
            mgSfxSlider.onValueChanged.AddListener(v => { GMAudioManager.Ensure().SfxVolume = v; RefreshSoundLabels(); });
        }

        private void RefreshSoundSettings()
        {
            if (mgBgmSlider == null) return;
            var audio = GMAudioManager.Ensure();
            mgBgmSlider.SetValueWithoutNotify(audio.BgmVolume);
            mgSfxSlider.SetValueWithoutNotify(audio.SfxVolume);
            RefreshSoundLabels();
        }

        private void RefreshSoundLabels()
        {
            if (mgBgmLabel == null) return;
            mgBgmLabel.text = $"BGM 볼륨 {Mathf.RoundToInt(mgBgmSlider.value * 100)}%";
            mgSfxLabel.text = $"효과음 볼륨 {Mathf.RoundToInt(mgSfxSlider.value * 100)}%";
        }

        /// <summary>
        /// 프런트 오피스 진입 · 갱신 - 현재 화면의 BGM 그룹 풀(삼성 = 구단 BGM 로테이션, 그 밖 = 기본 앰비언스).
        /// [TASK-GM-12] 같은 그룹 화면이면 곡을 끊지 않는다(매니저가 논리 상태로 판정 - Refresh가 여러 번 불려도 재시작 없음).
        /// </summary>
        private void PlayFrontOfficeBgm()
        {
            var team = UserTeam;
            if (team == null || !isActiveAndEnabled) return;
            GMAudioManager.Ensure().EnterScreen(CurrentAudioScreen, team.TeamCode);
        }

        /// <summary>[TASK-GM-12] 현재 패널 → BGM 화면 그룹(구단주 건의 팝업이 열려 있으면 구단주 보고실).</summary>
        public GMAudioScreen CurrentAudioScreen => discussPopup != null && discussPopup.gameObject.activeSelf ? GMAudioScreen.OwnerReport : AudioScreenFor(currentPane);

        public static GMAudioScreen AudioScreenFor(string pane)
        {
            switch (pane)
            {
                case PaneFA: case PaneTrade: case PaneSalaries: case PaneProtect: case PaneNegotiation: return GMAudioScreen.Market;
                case PaneRoster: case PaneChem: case PaneDraft: case PaneLockerRoom: return GMAudioScreen.Squad;
                default: return GMAudioScreen.Hub;
            }
        }

        /// <summary>[TASK-GM-12] 이벤트 전용곡 트리거(내 구단 기준).</summary>
        private void PlayAudioEvent(GMAudioEvent ev)
        {
            var team = UserTeam;
            if (team == null) return;
            GMAudioManager.Ensure().PlayEvent(ev, team.TeamCode);
        }

        /// <summary>[TASK-GM-14] 연봉 협상 결과 효과음 - 타결 = 차임 · 결렬/거절 = 저음(BGM은 바꾸지 않는다). 둘 다 아니면(자금 부족 등) 소리 없음.</summary>
        private void PlayNegotiationSfx(bool success, bool broken)
        {
            if (!success && !broken) return;
            GMAudioManager.Ensure().PlaySfx(success ? TeamAudioProfile.SynthDeal : TeamAudioProfile.SynthFail);
        }

        /// <summary>[TASK-GM-12] 계약 성사 등급 - S급(OVR 80 이상 · 연봉 10억 이상) 또는 프랜차이즈(주장 · 충성도 80 이상) = 엘도라도, 그 밖 = 환희.</summary>
        public static GMAudioEvent ContractEventFor(Player p, bool franchise)
        {
            if (p == null) return GMAudioEvent.PositiveResult;
            bool star = p.BaseOverall >= MajorSigningOvr || p.Salary >= MajorSigningSalary;
            bool franchiseStar = franchise && (p.IsCaptain || p.Loyalty >= 80 || p.BaseOverall >= MajorSigningOvr - 5);
            return star || franchiseStar ? GMAudioEvent.MajorResult : GMAudioEvent.PositiveResult;
        }

        public const int MajorSigningOvr = 80, MajorSigningSalary = 100000;

        /// <summary>정규시즌 첫 경기 전 [진행하기] - 정산 대기 FA 보상을 처리한다. 처리 메시지(없으면 null).</summary>
        private string SettleCompensationsBeforeSeason()
        {
            var league = League;
            if (league == null || simulator == null || simulator.GamesPlayed > 0 || league.PendingCompensations.Count == 0) return null;
            var results = league.PendingCompensations.Where(c => c.ToTeam == league.SelectedTeamCode || c.FromTeam == league.SelectedTeamCode).ToList()
                .Select(c => GMFaCompensation.Settle(league, c)).Where(r => r.Success).Select(r => r.Message).ToList();
            return results.Count > 0 ? string.Join(" / ", results) : null;
        }
    }
}
