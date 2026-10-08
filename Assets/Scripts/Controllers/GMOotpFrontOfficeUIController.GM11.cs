using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-GM-11] 스토브리그 MVP 1단계 화면 2종(프런트 오피스 서브 탭 7 · 8번째, 1920×1080 · Normal · 15pt 이상).
    ///   ① 시즌 결산실(GMSeasonSummaryView) - 팀 성적 · 재정 수지 · 직원 3인 보고 · 포지션 약점 분석 · 선수별 성과/연봉 효율 리포트(색상 등급).
    ///      정규시즌 144경기가 끝나면 [진행하기]가 포스트시즌보다 먼저 이 화면을 연다.
    ///   ② 계약 협상실(GMNegotiationRoomView) - 대상 선수 → 성과 리포트 · 에이전트 성향 · 유대 → 협상 카드 3장(카드별 진행 가능성 · 결과 분포 · 재무팀장 경고) → 선택 → 결과.
    /// 지시서의 GMSeasonSummaryView / GMNegotiationRoomView는 별도 MonoBehaviour 대신 허브 화면(Pane) 이름으로 두었다(기존 허브 구조 재사용).
    /// 색상 코딩(기획서 4.1)은 이 두 화면에만 적용한다 - 빨강 = 강점 · 초록 = 보통 · 파랑 = 약점 · 주황 = 위험.
    /// </summary>
    public partial class GMOotpFrontOfficeUIController
    {
        public const string PaneSeasonSummary = "GMSeasonSummaryView", PaneNegotiation = "GMNegotiationRoomView";
        public const int SubTabPitch = 237, SummaryRows = 12, NegRows = 14, WeakRows = 4, StaffRows = 3;

        public static readonly Color ToneStrong = new Color(1f, 0.38f, 0.32f);   // 빨강 - 강점
        public static readonly Color ToneNeutral = new Color(0.36f, 0.86f, 0.36f); // 초록 - 보통
        public static readonly Color ToneWeak = new Color(0.4f, 0.7f, 1f);        // 파랑 - 약점
        public static readonly Color ToneRisk = new Color(1f, 0.62f, 0.2f);       // 주황 - 위험
        public static Color ToneColor(GMReportTone t) => t == GMReportTone.Strong ? ToneStrong : t == GMReportTone.Weak ? ToneWeak : t == GMReportTone.Risk ? ToneRisk : ToneNeutral;

        /// <summary>[TASK-GM-16] 색 이름을 글자로 쓰지 않는다 - 색 견본(■)만 해당 색으로 칠한다.</summary>
        private static readonly string ToneLegend =
            $"<color=#{ColorUtility.ToHtmlStringRGB(ToneStrong)}>■</color> 강점   <color=#{ColorUtility.ToHtmlStringRGB(ToneNeutral)}>■</color> 보통   " +
            $"<color=#{ColorUtility.ToHtmlStringRGB(ToneWeak)}>■</color> 약점   <color=#{ColorUtility.ToHtmlStringRGB(ToneRisk)}>■</color> 위험 · 추가 확인";
        private readonly RectTransform[] negMetricFills = new RectTransform[6];

        // ---- 시즌 결산실
        private Text ssSource, ssRecord, ssConfidence, ssPageLabel;
        private readonly Text[] ssFinance = new Text[4], ssWeak = new Text[WeakRows], ssStaff = new Text[StaffRows];
        private readonly Button[] ssRows = new Button[SummaryRows];
        private readonly Text[,] ssCells = new Text[SummaryRows, 11];
        private int ssPage;
        private GMSeasonSummary seasonSummary;

        // ---- 계약 협상실
        private readonly Button[] negRows = new Button[NegRows];
        private readonly Text[,] negCells = new Text[NegRows, 6];
        private Text negPageLabel, negName, negInfo, negMoney, negConfidence, negBonds, negBaseline, negTrust, negMessage, negPool;
        private readonly Text[] negMetricLabels = new Text[6], negMetricValues = new Text[6], negMetricVerdicts = new Text[6];
        private readonly Button[] negYearButtons = new Button[5];
        private Button negBasic;
        private readonly Button[] negCards = new Button[3];
        private readonly Text[] negPitch = new Text[3], negReaction = new Text[3], negProgress = new Text[3], negDist = new Text[3], negFinance = new Text[3];
        private int negPage, negYears;
        private Player negSelected;
        private GMNegotiationSession negSession;
        private List<Player> negShown = new List<Player>();

        public GMSeasonSummary SeasonSummary => seasonSummary;
        public GMNegotiationSession NegotiationSession => negSession;
        public Player NegotiationSelected => negSelected;

        /// <summary>정규시즌을 마쳤는데 이번 시즌 결산실을 아직 열지 않았다.</summary>
        public bool SeasonReviewPending => simulator != null && League != null && simulator.IsSeasonComplete && GMFrontOffice.Ensure(League).SeasonReviewSeenYear < League.SeasonYear;

        private void LayoutSubTab(int k, int count)
        {
            float pitch = count > 8 ? 211 : count > 6 ? SubTabPitch : 312; // [TASK-GM-15] 9칸 = 211px
            float x0 = 12 + k * pitch, x1 = x0 + pitch - (count > 6 ? 8 : 12);
            var rect = (RectTransform)subTabButtons[k].transform;
            rect.anchorMin = new Vector2(x0 / CompyaUiKit.WideWidth, rect.anchorMin.y);
            rect.anchorMax = new Vector2(x1 / CompyaUiKit.WideWidth, rect.anchorMax.y);
        }

        public void OpenSeasonSummary()
        {
            SelectMainTab(0);
            SelectSubTab(subTabs[0].FindIndex(s => s.Pane == PaneSeasonSummary));
        }

        public void OpenNegotiationRoom(Player p = null)
        {
            SelectMainTab(0);
            if (p != null) { negSelected = p; negSession = null; }
            SelectSubTab(subTabs[0].FindIndex(s => s.Pane == PaneNegotiation));
        }

        // ================================================================== ① 시즌 결산실

        private static readonly float[] SsCols = { 24, 100, 270, 320, 450, 610, 740, 880, 1080, 1330, 1550, 1900 };
        private static readonly string[] SsHeads = { "포지션", "이름", "나이", "종합 기여도", "생산성", "수비 기여도", "연봉", "연봉 효율", "대체 불가성", "최근 추세", "분석 신뢰도" };

        private void BuildSeasonSummaryPane()
        {
            var pane = PaneRoot(PaneSeasonSummary);
            var teamPanel = Panel(pane, "SummaryTeamPanel", 12, 248, 640, 560);
            L(teamPanel, "SummaryTitle", "시즌 결산실 (SEASON REVIEW · Turn 1)", 24, 254, 628, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            ssSource = L(teamPanel, "SummarySource", "", 24, 290, 628, 318, CellPt, TextAnchor.MiddleLeft, Muted);
            ssRecord = L(teamPanel, "SummaryRecord", "", 24, 322, 628, 356, BannerPt - 1, TextAnchor.MiddleLeft, White);
            ssConfidence = L(teamPanel, "SummaryConfidence", "", 24, 360, 628, 388, CellPt, TextAnchor.MiddleLeft, Muted);
            for (int i = 0; i < ssFinance.Length; i++)
            {
                float y0 = 394 + i * 40;
                ssFinance[i] = L(teamPanel, $"SummaryFinance{i}", "", 24, y0, 628, y0 + 36, BodyPt, TextAnchor.MiddleLeft, White);
            }

            var staff = Panel(pane, "SummaryStaffPanel", 652, 248, 1908, 560);
            L(staff, "StaffTitle", "포지션 약점 분석 · 직원 보고", 664, 254, 1180, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            L(staff, "StaffLegend", ToneLegend, 1190, 254, 1900, 286, CellPt, TextAnchor.MiddleRight, Muted);
            for (int i = 0; i < WeakRows; i++)
            {
                float y0 = 292 + i * 44;
                ssWeak[i] = L(staff, $"Weakness{i}", "", 664, y0, 1900, y0 + 40, BodyPt, TextAnchor.MiddleLeft, White);
            }
            for (int i = 0; i < StaffRows; i++)
            {
                float y0 = 472 + i * 28;
                ssStaff[i] = L(staff, $"StaffLine{i}", "", 664, y0, 1900, y0 + 27, CellPt, TextAnchor.MiddleLeft, Muted);
            }

            var players = Panel(pane, "SummaryPlayersPanel", 12, 572, 1908, 1036);
            L(players, "PlayersTitle", "선수별 성과 · 연봉 효율 리포트 (행을 누르면 계약 협상실)", 24, 578, 1100, 608, PanelTitlePt - 1, TextAnchor.MiddleLeft, Gold);
            Btn(players, "SummaryPrev", "◀ 이전", 1500, 576, 1600, 608, ButtonIdle, BodyPt).onClick.AddListener(() => { ssPage = Math.Max(0, ssPage - 1); RefreshSeasonSummary(); });
            ssPageLabel = L(players, "SummaryPage", "", 1604, 576, 1700, 608, BodyPt, TextAnchor.MiddleCenter, White);
            Btn(players, "SummaryNext", "다음 ▶", 1704, 576, 1800, 608, ButtonIdle, BodyPt).onClick.AddListener(() => { ssPage++; RefreshSeasonSummary(); });
            for (int c = 0; c < SsHeads.Length; c++)
                L(players, $"SummaryHead{c}", SsHeads[c], SsCols[c], 612, SsCols[c + 1] - 4, 640, CellPt, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < SummaryRows; r++)
            {
                int row = r;
                float y0 = 644 + r * 32;
                ssRows[r] = Row(players, $"SummaryRow{r}", 20, y0, 1904, y0 + 30, SsCols, ssCells, r, CellPt);
                ssRows[r].onClick.AddListener(() => NegotiateFromSummary(row));
            }
        }

        private void RefreshSeasonSummary()
        {
            if (League == null || ssSource == null) return;
            seasonSummary = GMSeasonReview.Build(League);
            var sm = seasonSummary;
            ssSource.text = $"기록 출처: {sm.Source}";
            ssRecord.text = sm.RecordLine;
            ssConfidence.text = $"데이터분석 {sm.ConfidenceLabel} · 협상 근거로 쓰기 전 직접 확인하십시오";
            ssConfidence.color = sm.Confidence == GMReportConfidence.Low ? ToneRisk : Muted;
            for (int i = 0; i < ssFinance.Length; i++)
            {
                ssFinance[i].text = i < sm.FinanceLines.Count ? sm.FinanceLines[i] : "";
                ssFinance[i].color = i == ssFinance.Length - 1 ? (sm.FinanceBalance >= 0 ? ToneNeutral : ToneRisk) : White;
            }
            for (int i = 0; i < WeakRows; i++)
            {
                var w = i < sm.Weaknesses.Count ? sm.Weaknesses[i] : null;
                ssWeak[i].text = w == null ? "" : $"{(i == 0 ? "[데이터분석팀장] " : "")}{w.Comment}";
                ssWeak[i].color = w == null ? White : ToneColor(w.Tone);
            }
            for (int i = 0; i < StaffRows; i++) ssStaff[i].text = i + 1 < sm.StaffLines.Count ? sm.StaffLines[i + 1] : "";

            int pages = Math.Max(1, (sm.Players.Count + SummaryRows - 1) / SummaryRows);
            ssPage = Math.Min(ssPage, pages - 1);
            ssPageLabel.text = $"{ssPage + 1}/{pages}";
            for (int r = 0; r < SummaryRows; r++)
            {
                int index = ssPage * SummaryRows + r;
                var rep = index < sm.Players.Count ? sm.Players[index] : null;
                ssRows[r].gameObject.SetActive(rep != null);
                if (rep == null) continue;
                var p = rep.Player;
                ssRows[r].targetGraphic.color = p == negSelected ? RowSelected : r % 2 == 0 ? RowIdle : RowAlt;
                SetCell(ssCells[r, 0], rep.PositionLabel, White);
                SetCell(ssCells[r, 1], p.Template.PlayerName, White);
                SetCell(ssCells[r, 2], p.Age.ToString(), White);
                SetCell(ssCells[r, 3], $"{rep.War.ValueText} {GMSeasonReview.ToneLabel(rep.War.Tone)}", ToneColor(rep.War.Tone));
                SetCell(ssCells[r, 4], rep.Production.ValueText, ToneColor(rep.Production.Tone));
                SetCell(ssCells[r, 5], rep.Defense.ValueText, p.IsPitcher ? Muted : ToneColor(rep.Defense.Tone));
                SetCell(ssCells[r, 6], GMDiagnosticFormat.Short(p.Salary), White);
                SetCell(ssCells[r, 7], $"{rep.Efficiency.ValueText} {rep.Efficiency.Verdict}", ToneColor(rep.Efficiency.Tone));
                SetCell(ssCells[r, 8], $"{rep.Replaceability.ValueText} {ShortVerdict(rep.Replaceability.Verdict)}", ToneColor(rep.Replaceability.Tone));
                SetCell(ssCells[r, 9], rep.Trend.Verdict, ToneColor(rep.Trend.Tone));
                SetCell(ssCells[r, 10], rep.ConfidenceLabel, rep.SampleWarning ? ToneRisk : Muted);
            }
        }

        private static string ShortVerdict(string v) { int i = v.IndexOf(" (", StringComparison.Ordinal); return i > 0 ? v.Substring(0, i) : v; }

        private static void SetCell(Text t, string text, Color color)
        {
            if (t == null) return;
            t.text = text;
            t.color = color;
        }

        private void NegotiateFromSummary(int row)
        {
            int index = ssPage * SummaryRows + row;
            if (seasonSummary == null || index >= seasonSummary.Players.Count) return;
            OpenNegotiationRoom(seasonSummary.Players[index].Player);
        }

        // ================================================================== ② 계약 협상실

        private static readonly float[] NegCols = { 20, 84, 236, 284, 400, 452, 552 };
        private static readonly string[] NegHeads = { "포지션", "이름", "나이", "연봉", "잔여", "상태" };

        private void BuildNegotiationPane()
        {
            var pane = PaneRoot(PaneNegotiation);
            var list = Panel(pane, "NegListPanel", 12, 248, 560, 1036);
            L(list, "NegListTitle", "계약 협상실 - 잔여 계약 1년 이하", 24, 254, 548, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int c = 0; c < NegHeads.Length; c++)
                L(list, $"NegHead{c}", NegHeads[c], NegCols[c], 290, NegCols[c + 1] - 4, 318, CellPt, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < NegRows; r++)
            {
                int row = r;
                float y0 = 322 + r * 48;
                negRows[r] = Row(list, $"NegRow{r}", 16, y0, 556, y0 + 44, NegCols, negCells, r, CellPt);
                negRows[r].onClick.AddListener(() => SelectNegotiationRow(row));
            }
            Btn(list, "NegPrev", "◀ 이전", 24, 996, 140, 1030, ButtonIdle, BodyPt).onClick.AddListener(() => { negPage = Math.Max(0, negPage - 1); RefreshNegotiation(); });
            negPageLabel = L(list, "NegPage", "", 144, 996, 424, 1030, BodyPt, TextAnchor.MiddleCenter, White);
            Btn(list, "NegNext", "다음 ▶", 428, 996, 548, 1030, ButtonIdle, BodyPt).onClick.AddListener(() => { negPage++; RefreshNegotiation(); });

            var rep = Panel(pane, "NegReportPanel", 572, 248, 1130, 1036);
            negName = L(rep, "NegName", "", 584, 254, 940, 290, BannerPt - 1, TextAnchor.MiddleLeft, White);
            Btn(rep, "NegScoutCard", "스카우팅 리포트 ▶", 946, 254, 1118, 290, ButtonOn, SmallPt).onClick.AddListener(() => { if (negSelected != null) OpenPercentiles(negSelected); }); // [TASK-GM-16]
            negInfo = L(rep, "NegInfo", "", 584, 294, 1118, 350, CellPt, TextAnchor.UpperLeft, Muted);
            negMoney = L(rep, "NegMoney", "", 584, 354, 1118, 384, BodyPt, TextAnchor.MiddleLeft, White);
            negConfidence = L(rep, "NegConfidence", "", 584, 388, 1118, 416, CellPt, TextAnchor.MiddleLeft, Muted);
            for (int i = 0; i < 6; i++)
            {
                float y0 = 422 + i * 36;
                // [TASK-GM-16] 스카우팅 리포트 막대 - 지표 이름 · 막대(길이 = 수준, 색 = 강점/보통/약점/위험) · 수치 · 판정(색 이름 없이)
                negMetricLabels[i] = L(rep, $"NegMetricLabel{i}", "", 584, y0, 712, y0 + 32, CellPt, TextAnchor.MiddleLeft, Muted);
                negMetricFills[i] = Bar(rep, $"NegMetricBar{i}", 716, y0 + 9, 880, y0 + 23, ToneNeutral);
                negMetricValues[i] = L(rep, $"NegMetricValue{i}", "", 884, y0, 966, y0 + 32, BodyPt, TextAnchor.MiddleCenter, White);
                negMetricVerdicts[i] = L(rep, $"NegMetricVerdict{i}", "", 970, y0, 1118, y0 + 32, CellPt, TextAnchor.MiddleLeft, White);
            }
            negBonds = L(rep, "NegBonds", "", 584, 644, 1118, 700, CellPt, TextAnchor.UpperLeft, Muted);
            L(rep, "NegYearsLabel", "계약 기간", 584, 706, 700, 742, BodyPt, TextAnchor.MiddleLeft, Muted);
            for (int k = 0; k < 5; k++)
            {
                int years = k + 1;
                float x0 = 704 + k * 82;
                negYearButtons[k] = Btn(rep, $"NegYears{years}", $"{years}년", x0, 706, x0 + 76, 742, ButtonIdle, BodyPt);
                negYearButtons[k].onClick.AddListener(() => { negYears = years; negSession = null; RefreshNegotiation(); });
            }
            negBaseline = L(rep, "NegBaseline", "", 584, 748, 1118, 804, CellPt, TextAnchor.UpperLeft, White);
            negBasic = Btn(rep, "NegBasic", "카드 없이 기본 제시", 584, 810, 1118, 856, new Color(0.35f, 0.35f, 0.4f), ButtonPt);
            negBasic.onClick.AddListener(() => ChooseNegotiationCard(-1));
            negTrust = L(rep, "NegTrust", "", 584, 862, 1118, 890, CellPt, TextAnchor.MiddleLeft, Muted);
            negMessage = L(rep, "NegMessage", "", 584, 896, 1118, 1030, BodyPt, TextAnchor.UpperLeft, White);
            negMessage.lineSpacing = 1.1f;

            var cards = Panel(pane, "NegCardsPanel", 1142, 248, 1908, 1036);
            L(cards, "NegCardsTitle", "협상 카드 3장 - 근거를 하나 고르십시오", 1154, 254, 1600, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            negPool = L(cards, "NegPool", "", 1604, 254, 1896, 286, CellPt, TextAnchor.MiddleRight, Muted);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                float y0 = 292 + i * 236;
                negCards[i] = Btn(cards, $"NegCard{i}", "", 1154, y0, 1896, y0 + 44, ButtonOn, ButtonPt);
                negCards[i].onClick.AddListener(() => ChooseNegotiationCard(index));
                negPitch[i] = L(cards, $"NegPitch{i}", "", 1154, y0 + 48, 1896, y0 + 96, CellPt, TextAnchor.UpperLeft, White);
                negReaction[i] = L(cards, $"NegReaction{i}", "", 1154, y0 + 100, 1896, y0 + 126, CellPt, TextAnchor.MiddleLeft, Muted);
                negProgress[i] = L(cards, $"NegProgress{i}", "", 1154, y0 + 130, 1896, y0 + 158, BodyPt, TextAnchor.MiddleLeft, Gold);
                negDist[i] = L(cards, $"NegDist{i}", "", 1154, y0 + 162, 1896, y0 + 190, CellPt, TextAnchor.MiddleLeft, White);
                negFinance[i] = L(cards, $"NegFinance{i}", "", 1154, y0 + 194, 1896, y0 + 222, CellPt, TextAnchor.MiddleLeft, White);
            }
            L(cards, "NegLegend", ToneLegend, 1154, 1004, 1896, 1030, CellPt, TextAnchor.MiddleLeft, Muted);
        }

        public void SelectNegotiationRow(int row)
        {
            int index = negPage * NegRows + row;
            if (index < 0 || index >= negShown.Count) return;
            negSelected = negShown[index];
            negSession = null;
            negYears = 0;
            RefreshNegotiation();
        }

        /// <summary>카드 선택(0~2, -1 = 카드 없이 기본 제시) → 협상 결과. 결과 후 같은 선수 세션을 새로 연다(결렬 시 쿨다운 표시).</summary>
        public GMNegotiationRoomResult ChooseNegotiationCard(int index)
        {
            if (League == null || UserTeam == null || negSelected == null) { SetStatus("협상할 선수를 먼저 고르십시오."); return null; }
            if (TurnBlocked(PaneNegotiation, out var lockMsg)) return new GMNegotiationRoomResult { Message = lockMsg }; // [TASK-GM-14]
            var session = negSession ?? GMNegotiationRoom.Open(League, UserTeam, negSelected, negYears);
            if (session.OnCooldown || session.BlockReason != "") { SetStatus(session.BlockReason); return null; }
            var player = session.Player;
            var result = GMNegotiationRoom.Resolve(League, UserTeam, session, index);
            negSession = null;
            RefreshNegotiation();
            negMessage.text = result.Message;
            negMessage.color = result.Success ? ToneNeutral : result.Broken ? ToneRisk : White;
            SetStatus(result.Message);
            // [TASK-GM-14] 연봉 협상은 BGM을 바꾸지 않는다(스토브리그 기본 BGM 유지) - 타결 · 결렬 짧은 효과음 1회
            PlayNegotiationSfx(result.Success, result.Broken);
            return result;
        }

        private void RefreshNegotiation()
        {
            if (League == null || UserTeam == null || negName == null) return;
            var team = UserTeam;
            negShown = GMNegotiationRoom.Targets(team);
            if (negSelected != null && !team.ReservePlayers.Contains(negSelected)) { negSelected = null; negSession = null; }
            if (negSelected == null && negShown.Count > 0) negSelected = negShown[0];
            int pages = Math.Max(1, (negShown.Count + NegRows - 1) / NegRows);
            negPage = Math.Min(negPage, pages - 1);
            negPageLabel.text = $"{negPage + 1}/{pages} · {negShown.Count}명";
            for (int r = 0; r < NegRows; r++)
            {
                int index = negPage * NegRows + r;
                var p = index < negShown.Count ? negShown[index] : null;
                negRows[r].gameObject.SetActive(p != null);
                if (p == null) continue;
                negRows[r].targetGraphic.color = p == negSelected ? RowSelected : r % 2 == 0 ? RowIdle : RowAlt;
                bool cooldown = GMNegotiationRoom.IsOnCooldown(League, p);
                SetCell(negCells[r, 0], GMSeasonReview.PositionLabelOf(p), White);
                SetCell(negCells[r, 1], p.Template.PlayerName + (team.Futures.Contains(p) ? "(퓨처스)" : ""), White);
                SetCell(negCells[r, 2], p.Age.ToString(), White);
                SetCell(negCells[r, 3], GMDiagnosticFormat.Short(p.Salary), White);
                SetCell(negCells[r, 4], $"{p.ContractYears}년", White);
                SetCell(negCells[r, 5], cooldown ? "결렬" : p.ContractYears == 0 ? "만료" : "협상", cooldown ? ToneRisk : p.ContractYears == 0 ? Gold : Muted);
            }

            negTrust.text = $"선수단의 단장 신뢰도 {team.LockerRoomTrust} · 팀 분위기(팀워크) {TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff).TeamworkScore}";
            if (negSelected == null)
            {
                negName.text = "협상 대상 없음";
                negInfo.text = "잔여 계약 1년 이하 선수가 없습니다(시즌 종료 · 연도 전환 후 만료자가 생깁니다).";
                negMoney.text = negConfidence.text = negBonds.text = negBaseline.text = negPool.text = "";
                for (int i = 0; i < 6; i++) { negMetricLabels[i].text = negMetricValues[i].text = negMetricVerdicts[i].text = ""; SetFill(negMetricFills[i], 0f); }
                for (int i = 0; i < 3; i++) { negCards[i].gameObject.SetActive(false); negPitch[i].text = negReaction[i].text = negProgress[i].text = negDist[i].text = negFinance[i].text = ""; }
                negBasic.interactable = false;
                return;
            }

            if (negSession == null || negSession.Player != negSelected) negSession = GMNegotiationRoom.Open(League, team, negSelected, negYears);
            var s = negSession;
            var sel = negSelected;
            var rep = s.Report;
            negName.text = $"{sel.Template.PlayerName} · {rep.PositionLabel} · {sel.Age}세 · OVR {sel.BaseOverall}";
            negInfo.text = $"에이전트 성향 {GMNegotiationRoom.ArchetypeLabel(sel.AgentArchetype)} · 라커룸 {RoleLabel(sel.RoleArchetype)} · Ego {sel.EgoLevel}\n구단 충성도 {sel.Loyalty} · 개인 만족도 {sel.PersonalMorale}{(sel.IsCaptain ? " · 주장" : "")}";
            negMoney.text = $"현재 {GMDiagnosticFormat.Short(s.CurrentSalary)} → 요구 {GMDiagnosticFormat.Short(s.Demand)} · 희망 {s.Years}년";
            negConfidence.text = $"성과 리포트 {rep.ConfidenceLabel} · {rep.SampleNote}";
            negConfidence.color = rep.SampleWarning ? ToneRisk : Muted;
            var metrics = rep.Metrics.ToList();
            for (int i = 0; i < 6; i++)
            {
                negMetricLabels[i].text = metrics[i].Label;
                negMetricValues[i].text = metrics[i].ValueText;
                negMetricValues[i].color = ToneColor(metrics[i].Tone);
                negMetricVerdicts[i].text = metrics[i].Verdict; // [TASK-GM-16] 색 이름(빨강 · 파랑 …)을 글자로 쓰지 않는다
                negMetricVerdicts[i].color = ToneColor(metrics[i].Tone);
                SetFill(negMetricFills[i], GMScoutingReport.MetricFill(i, metrics[i]));
                var fillImg = negMetricFills[i].GetComponent<Image>();
                if (fillImg != null) fillImg.color = ToneColor(metrics[i].Tone);
            }
            negBonds.text = s.Bonds.Count > 0 ? "유대: " + string.Join(" / ", s.Bonds.Take(3)) : "유대: 규칙 기반 연결 고리 없음(배터리 · 키스톤 · 멘토)";
            for (int k = 0; k < 5; k++) negYearButtons[k].targetGraphic.color = k + 1 == s.Years ? ButtonOn : ButtonIdle;
            var b = s.Baseline;
            negBaseline.text = $"기본 제시: {GMNegotiationRoom.ProgressText(b)}\n결과별 연봉: 수용 {GMDiagnosticFormat.Short(b.Salaries[0])} · 소폭 {GMDiagnosticFormat.Short(b.Salaries[1])} · 동결 {GMDiagnosticFormat.Short(b.Salaries[2])} · 삭감 {GMDiagnosticFormat.Short(b.Salaries[3])}";
            negBasic.interactable = !s.OnCooldown;
            negPool.text = $"후보 풀 {s.Pool.Count}장 중 3장";
            for (int i = 0; i < 3; i++)
            {
                bool has = i < s.Forecasts.Count;
                negCards[i].gameObject.SetActive(has);
                if (!has) { negPitch[i].text = negReaction[i].text = negProgress[i].text = negDist[i].text = negFinance[i].text = ""; continue; }
                var f = s.Forecasts[i];
                CompyaUiKit.SetButtonText(negCards[i], $"{(char)('①' + i)} [{GMNegotiationRoom.CategoryLabel(f.Card.Category)}] {f.Card.Title}");
                negCards[i].interactable = !s.OnCooldown;
                negPitch[i].text = f.Card.Pitch;
                negReaction[i].text = $"에이전트 반응: {f.Card.ArchetypeReaction} · 카드 가산 +{f.Bonus * 100:0}%p · 계약 {f.Years}년";
                negProgress[i].text = GMNegotiationRoom.ProgressText(f);
                negDist[i].text = "예상 결과: " + GMNegotiationRoom.DistributionText(f);
                negFinance[i].text = f.FinanceWarning;
                negFinance[i].color = ToneColor(f.FinanceTone);
            }
            if (s.OnCooldown) { negMessage.text = s.BlockReason; negMessage.color = ToneRisk; }
        }

        private static string RoleLabel(LockerRoomRole r) => r == LockerRoomRole.AlphaDog ? "알파독" : r == LockerRoomRole.Ambitious ? "야망가" : r == LockerRoomRole.DugoutLeader ? "더그아웃 리더" : r == LockerRoomRole.Prospect ? "유망주" : "살림꾼";
    }
}
