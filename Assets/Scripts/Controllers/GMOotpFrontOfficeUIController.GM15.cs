using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-GM-15] 스토브리그 3단계 화면(1920×1080 · Normal · 15pt 이상) - 지시서의 View 이름을 허브 화면(Pane) 이름으로 둔다(GM-11 · GM-13과 같은 구조).
    ///   ① 육성 회의실(GMFuturesMeetingView · [선수단] 5번째 서브 탭 · Turn 6): 좌 퓨처스 명단 · OVR · 잠재력 범위 · 성장 유형 / 중 훈련 방향 · 콜업 스왑 / 우 베테랑 멘토링 3슬롯
    ///   ② 2차 드래프트실(GMSecondaryDraftView · [스카우팅·드래프트] 3번째 서브 탭 · Turn 3): 좌 25인 보호 체크 / 우 타 구단 비보호 선수 지명 · AI 지명 마감
    ///   ③ 언론 브리핑실(GMPressBriefingView · [프런트 오피스] 9번째 서브 탭): 3지선다 브리핑 · 효과 미리보기 · 기록된 발언
    /// </summary>
    public partial class GMOotpFrontOfficeUIController
    {
        public const string PaneFuturesMeeting = "GMFuturesMeetingView", PaneSecondDraft = "GMSecondaryDraftView", PanePress = "GMPressBriefingView";
        public const int FmRows = 13, FmSwapRows = 6, FmMentorRows = 6, SdRows = 13, SdPoolRows = 10, PrStatementRows = 8;

        private static readonly float[] FmCols = { 24, 84, 230, 280, 340, 450, 570, 696 };
        private static readonly string[] FmHeads = { "포지션", "이름", "나이", "OVR", "잠재력", "성장 유형", "훈련 방향" };
        private static readonly float[] SdCols = { 24, 84, 150, 330, 390, 460, 590, 936 };
        private static readonly string[] SdHeads = { "보호", "포지션", "이름", "나이", "OVR", "연봉", "상태" };
        private static readonly float[] SdPoolCols = { 964, 1064, 1134, 1324, 1394, 1464, 1594, 1896 };
        private static readonly string[] SdPoolHeads = { "구단", "포지션", "이름", "나이", "OVR", "연봉", "소속" };

        // ---- 육성 회의실
        private readonly Button[] fmRows = new Button[FmRows], fmFocus = new Button[4], fmSwapRows = new Button[FmSwapRows], fmMentorRows = new Button[FmMentorRows], fmSlotClear = new Button[GMFuturesMeeting.MaxMentorSlots];
        private readonly Text[,] fmCells = new Text[FmRows, 7];
        private readonly Text[] fmDetail = new Text[5], fmSlots = new Text[GMFuturesMeeting.MaxMentorSlots];
        private Text fmSummary, fmPageLabel, fmMessage, fmMentorNote;
        private Button fmCallUp, fmSendDown;
        private Player fmSelected, fmSwap;
        private int fmPage;
        private List<Player> fmShown = new List<Player>(), fmSwapShown = new List<Player>(), fmMentorShown = new List<Player>();

        // ---- 2차 드래프트
        private readonly Button[] sdRows = new Button[SdRows], sdPoolRows = new Button[SdPoolRows];
        private readonly Text[,] sdCells = new Text[SdRows, 7], sdPoolCells = new Text[SdPoolRows, 7];
        private Text sdSummary, sdPageLabel, sdStatus, sdPoolPage, sdResults;
        private Button sdPick, sdConclude, sdRecommend;
        private int sdPage, sdPoolPageIndex;
        private Player sdSelected;
        private List<Player> sdShown = new List<Player>();
        private List<(Player player, GMTeamState from)> sdPoolShown = new List<(Player, GMTeamState)>();

        // ---- 언론 브리핑실
        private readonly Button[] prOptions = new Button[3];
        private readonly Text[] prHints = new Text[3], prQuotes = new Text[PrStatementRows], prNotes = new Text[PrStatementRows];
        private Text prTopic, prContext, prResult;

        public Player FuturesSelected => fmSelected;
        public Player SwapTarget => fmSwap;
        public Player SecondDraftSelected => sdSelected;

        // ================================================================== 열기

        public void OpenFuturesMeeting()
        {
            SelectMainTab(2);
            SelectSubTab(subTabs[2].FindIndex(s => s.Pane == PaneFuturesMeeting));
        }

        public void OpenSecondDraft()
        {
            SelectMainTab(5);
            SelectSubTab(subTabs[5].FindIndex(s => s.Pane == PaneSecondDraft));
        }

        public void OpenPressBriefing()
        {
            SelectMainTab(0);
            SelectSubTab(subTabs[0].FindIndex(s => s.Pane == PanePress));
        }

        // ================================================================== 조립 - 육성 회의실

        private void BuildFuturesMeetingPane()
        {
            var pane = PaneRoot(PaneFuturesMeeting);

            var list = Panel(pane, "FmListPanel", 12, 248, 700, 1036);
            L(list, "FmTitle", "퓨처스 핵심 유망주 (FUTURES ROSTER)", 24, 254, 688, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            fmSummary = L(list, "FmSummary", "", 24, 290, 688, 318, CellPt, TextAnchor.MiddleLeft, Muted);
            for (int c = 0; c < FmHeads.Length; c++)
                L(list, $"FmHead{c}", FmHeads[c], FmCols[c], 322, FmCols[c + 1] - 4, 350, CellPt, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < FmRows; r++)
            {
                int row = r;
                float y0 = 354 + r * 48;
                fmRows[r] = Row(list, $"FmRow{r}", 16, y0, 696, y0 + 44, FmCols, fmCells, r, CellPt);
                fmRows[r].onClick.AddListener(() => { if (row < fmShown.Count) SelectFutures(fmShown[row]); });
            }
            Btn(list, "FmPrev", "◀ 이전", 24, 984, 160, 1026, ButtonIdle, BodyPt).onClick.AddListener(() => { fmPage = Math.Max(0, fmPage - 1); RefreshFuturesMeeting(); });
            fmPageLabel = L(list, "FmPage", "", 164, 984, 548, 1026, BodyPt, TextAnchor.MiddleCenter, White);
            Btn(list, "FmNext", "다음 ▶", 552, 984, 688, 1026, ButtonIdle, BodyPt).onClick.AddListener(() => { fmPage++; RefreshFuturesMeeting(); });

            var mid = Panel(pane, "FmTrainPanel", 712, 248, 1300, 1036);
            L(mid, "FmTrainTitle", "훈련 방향 · 콜업 (TRAINING / CALL-UP)", 724, 254, 1288, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int i = 0; i < fmDetail.Length; i++)
            {
                float y0 = 292 + i * 32;
                fmDetail[i] = L(mid, $"FmDetail{i}", "", 724, y0, 1288, y0 + 30, BodyPt, TextAnchor.MiddleLeft, White);
            }
            L(mid, "FmFocusTitle", "훈련 방향 지정", 724, 460, 1288, 488, PanelTitlePt - 1, TextAnchor.MiddleLeft, Gold);
            for (int k = 0; k < 4; k++)
            {
                int index = k;
                float x0 = 724 + k * 142;
                fmFocus[k] = Btn(mid, $"FmFocus{k}", "", x0, 494, x0 + 134, 538, ButtonIdle, ButtonPt);
                fmFocus[k].onClick.AddListener(() => SetTrainingFocus(index));
            }
            L(mid, "FmSwapTitle", "1군 맞바꿀 선수 (같은 유형 · 가치 낮은 순)", 724, 550, 1288, 578, BodyPt, TextAnchor.MiddleLeft, Muted);
            for (int i = 0; i < FmSwapRows; i++)
            {
                int index = i;
                float y0 = 584 + i * 46;
                fmSwapRows[i] = Btn(mid, $"FmSwap{i}", "", 724, y0, 1288, y0 + 42, RowIdle, CellPt);
                fmSwapRows[i].onClick.AddListener(() => { if (index < fmSwapShown.Count) SelectSwapTarget(fmSwapShown[index]); });
            }
            fmCallUp = Btn(mid, "FmCallUp", "1군 콜업", 724, 866, 1000, 912, ButtonOn, ButtonPt);
            fmCallUp.onClick.AddListener(() => CallUpSelected());
            fmSendDown = Btn(mid, "FmSendDown", "선택 1군 선수 퓨처스 이관", 1008, 866, 1288, 912, ButtonIdle, ButtonPt - 1);
            fmSendDown.onClick.AddListener(() => SendDownSwapTarget());
            fmMessage = L(mid, "FmMessage", "", 724, 920, 1288, 1030, CellPt, TextAnchor.UpperLeft, Muted);
            fmMessage.lineSpacing = 1.1f;

            var right = Panel(pane, "FmMentorPanel", 1312, 248, 1908, 1036);
            L(right, "FmMentorTitle", $"베테랑 멘토링 (최대 {GMFuturesMeeting.MaxMentorSlots}쌍 · 1:1)", 1324, 254, 1896, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int i = 0; i < fmSlots.Length; i++)
            {
                int slot = i;
                float y0 = 292 + i * 64;
                fmSlots[i] = L(right, $"FmSlot{i}", "", 1324, y0, 1760, y0 + 58, CellPt, TextAnchor.MiddleLeft, White);
                fmSlotClear[i] = Btn(right, $"FmSlotClear{i}", "해제", 1768, y0 + 7, 1896, y0 + 51, ButtonIdle, BodyPt);
                fmSlotClear[i].onClick.AddListener(() => ClearMentorSlot(slot));
            }
            L(right, "FmMentorListTitle", "멘토 후보 - 1군 더그아웃 리더 (선택 유망주에 배정)", 1324, 490, 1896, 518, BodyPt, TextAnchor.MiddleLeft, Muted);
            for (int i = 0; i < FmMentorRows; i++)
            {
                int index = i;
                float y0 = 524 + i * 48;
                fmMentorRows[i] = Btn(right, $"FmMentor{i}", "", 1324, y0, 1896, y0 + 44, RowIdle, CellPt);
                fmMentorRows[i].onClick.AddListener(() => { if (index < fmMentorShown.Count) AssignMentorTo(fmMentorShown[index]); });
            }
            fmMentorNote = L(right, "FmMentorNote", "", 1324, 820, 1896, 1030, CellPt, TextAnchor.UpperLeft, Muted);
            fmMentorNote.lineSpacing = 1.1f;
        }

        // ================================================================== 조립 - 2차 드래프트

        private void BuildSecondDraftPane()
        {
            var pane = PaneRoot(PaneSecondDraft);

            var prot = Panel(pane, "SdProtectPanel", 12, 248, 940, 1036);
            L(prot, "SdProtectTitle", $"보호 명단 지정 ({GMSecondaryDraft.ProtectSize}인 · 행 클릭 = 보호/해제)", 24, 254, 928, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            sdSummary = L(prot, "SdSummary", "", 24, 290, 928, 318, CellPt, TextAnchor.MiddleLeft, Muted);
            for (int c = 0; c < SdHeads.Length; c++)
                L(prot, $"SdHead{c}", SdHeads[c], SdCols[c], 322, SdCols[c + 1] - 4, 350, CellPt, c == 2 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < SdRows; r++)
            {
                int row = r;
                float y0 = 354 + r * 48;
                sdRows[r] = Row(prot, $"SdRow{r}", 16, y0, 936, y0 + 44, SdCols, sdCells, r, CellPt);
                sdCells[r, 1].alignment = TextAnchor.MiddleCenter;
                sdCells[r, 2].alignment = TextAnchor.MiddleLeft;
                sdRows[r].onClick.AddListener(() => { if (row < sdShown.Count) ToggleSecondDraftProtect(sdShown[row]); });
            }
            Btn(prot, "SdPrev", "◀ 이전", 24, 984, 160, 1026, ButtonIdle, BodyPt).onClick.AddListener(() => { sdPage = Math.Max(0, sdPage - 1); RefreshSecondDraft(); });
            sdPageLabel = L(prot, "SdPage", "", 164, 984, 560, 1026, BodyPt, TextAnchor.MiddleCenter, White);
            Btn(prot, "SdNext", "다음 ▶", 564, 984, 700, 1026, ButtonIdle, BodyPt).onClick.AddListener(() => { sdPage++; RefreshSecondDraft(); });
            sdRecommend = Btn(prot, "SdRecommend", "추천 명단 적용", 712, 984, 928, 1026, ButtonIdle, BodyPt);
            sdRecommend.onClick.AddListener(ApplyRecommendedProtection);

            var pick = Panel(pane, "SdPickPanel", 952, 248, 1908, 1036);
            L(pick, "SdPickTitle", "지명 (타 구단 비보호 선수 · 흙 속의 진주)", 964, 254, 1896, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            sdStatus = L(pick, "SdStatus", "", 964, 290, 1896, 346, CellPt, TextAnchor.UpperLeft, White);
            for (int c = 0; c < SdPoolHeads.Length; c++)
                L(pick, $"SdPoolHead{c}", SdPoolHeads[c], SdPoolCols[c], 350, SdPoolCols[c + 1] - 4, 378, CellPt, c == 2 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < SdPoolRows; r++)
            {
                int row = r;
                float y0 = 382 + r * 48;
                sdPoolRows[r] = Row(pick, $"SdPoolRow{r}", 956, y0, 1900, y0 + 44, SdPoolCols, sdPoolCells, r, CellPt);
                sdPoolCells[r, 1].alignment = TextAnchor.MiddleCenter;
                sdPoolCells[r, 2].alignment = TextAnchor.MiddleLeft;
                sdPoolRows[r].onClick.AddListener(() => { if (row < sdPoolShown.Count) { sdSelected = sdPoolShown[row].player; RefreshSecondDraft(); } });
            }
            Btn(pick, "SdPoolPrev", "◀ 이전", 964, 866, 1090, 904, ButtonIdle, BodyPt).onClick.AddListener(() => { sdPoolPageIndex = Math.Max(0, sdPoolPageIndex - 1); RefreshSecondDraft(); });
            sdPoolPage = L(pick, "SdPoolPage", "", 1094, 866, 1500, 904, BodyPt, TextAnchor.MiddleCenter, White);
            Btn(pick, "SdPoolNext", "다음 ▶", 1504, 866, 1630, 904, ButtonIdle, BodyPt).onClick.AddListener(() => { sdPoolPageIndex++; RefreshSecondDraft(); });
            sdPick = Btn(pick, "SdPickButton", "지명", 964, 912, 1426, 956, ButtonOn, ButtonPt);
            sdPick.onClick.AddListener(() => PickSecondDraft());
            sdConclude = Btn(pick, "SdConclude", "AI 지명 진행 · 2차 드래프트 마감", 1434, 912, 1896, 956, ButtonIdle, ButtonPt - 1);
            sdConclude.onClick.AddListener(() => ConcludeSecondDraft());
            sdResults = L(pick, "SdResults", "", 964, 962, 1896, 1030, CellPt, TextAnchor.UpperLeft, Muted);
        }

        // ================================================================== 조립 - 언론 브리핑실

        private void BuildPressPane()
        {
            var pane = PaneRoot(PanePress);

            var brief = Panel(pane, "PrBriefPanel", 12, 248, 940, 1036);
            L(brief, "PrTitle", "언론 브리핑실 (PRESS BRIEFING)", 24, 254, 928, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            prTopic = L(brief, "PrTopic", "", 24, 292, 928, 324, BannerPt, TextAnchor.MiddleLeft, White);
            prContext = L(brief, "PrContext", "", 24, 330, 928, 420, CellPt, TextAnchor.UpperLeft, Muted);
            prContext.lineSpacing = 1.1f;
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                float y0 = 430 + i * 120;
                prOptions[i] = Btn(brief, $"PrOption{i}", "", 24, y0, 928, y0 + 48, ButtonOn, ButtonPt);
                prOptions[i].onClick.AddListener(() => ChoosePress(index));
                prHints[i] = L(brief, $"PrHint{i}", "", 24, y0 + 52, 928, y0 + 114, CellPt, TextAnchor.UpperLeft, Muted);
            }
            prResult = L(brief, "PrResult", "", 24, 800, 928, 1030, BodyPt, TextAnchor.UpperLeft, White);
            prResult.lineSpacing = 1.1f;

            var rec = Panel(pane, "PrRecordPanel", 952, 248, 1908, 1036);
            L(rec, "PrRecordTitle", "기록된 발언 (ON THE RECORD · 시즌 종료 판정)", 964, 254, 1896, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int i = 0; i < PrStatementRows; i++)
            {
                float y0 = 292 + i * 90;
                prQuotes[i] = L(rec, $"PrQuote{i}", "", 964, y0, 1896, y0 + 30, BodyPt, TextAnchor.MiddleLeft, White);
                prNotes[i] = L(rec, $"PrNote{i}", "", 964, y0 + 32, 1896, y0 + 84, CellPt, TextAnchor.UpperLeft, Muted);
            }
        }

        // ================================================================== 갱신 - 육성 회의실

        private void RefreshFuturesMeeting()
        {
            var league = League;
            var team = UserTeam;
            if (league == null || team == null || fmSummary == null) return;
            GMFuturesMeeting.Prune(league);
            if (fmSelected != null && !team.Futures.Contains(fmSelected)) fmSelected = null;
            if (fmSwap != null && !team.Roster.Contains(fmSwap)) fmSwap = null;
            fmSummary.text = GMRosterTiers.Summary(team);

            var order = team.Futures.Where(p => p.Template != null).OrderByDescending(p => GMFuturesMeeting.PotentialMax(p)).ThenByDescending(p => p.BaseOverall).ThenBy(p => p.InstanceId, StringComparer.Ordinal).ToList();
            if (fmSelected == null && order.Count > 0) fmSelected = order[0];
            int pages = Math.Max(1, (order.Count + FmRows - 1) / FmRows);
            fmPage = Math.Min(fmPage, pages - 1);
            fmPageLabel.text = $"{fmPage + 1}/{pages} · 퓨처스 {order.Count}명 (잠재력 높은 순)";
            fmShown = order.Skip(fmPage * FmRows).Take(FmRows).ToList();
            for (int r = 0; r < FmRows; r++)
            {
                var p = r < fmShown.Count ? fmShown[r] : null;
                fmRows[r].gameObject.SetActive(p != null);
                if (p == null) continue;
                fmRows[r].targetGraphic.color = p == fmSelected ? RowSelected : r % 2 == 0 ? RowIdle : RowAlt;
                var mentor = GMFuturesMeeting.MentorOf(league, p);
                SetCell(fmCells[r, 0], GMSeasonReview.PositionLabelOf(p), White);
                SetCell(fmCells[r, 1], p.Template.PlayerName + (mentor != null ? " ★" : ""), mentor != null ? Gold : White);
                SetCell(fmCells[r, 2], p.Age.ToString(), White);
                SetCell(fmCells[r, 3], p.BaseOverall.ToString(), White);
                SetCell(fmCells[r, 4], $"{GMFuturesMeeting.PotentialMin(p)}~{GMFuturesMeeting.PotentialMax(p)}", GMFuturesMeeting.PotentialMax(p) >= 80 ? ToneStrong : White);
                SetCell(fmCells[r, 5], GMFuturesMeeting.GrowthTypeLabel(GMFuturesMeeting.TypeOf(p)), Muted);
                SetCell(fmCells[r, 6], GMFuturesMeeting.FocusLabel(GMFuturesMeeting.FocusOf(league, p)), White);
            }

            // 중앙 - 선택 유망주 상세 · 훈련 방향 · 스왑 후보
            var s = fmSelected;
            if (s == null)
            {
                fmDetail[0].text = "퓨처스 유망주가 없습니다.";
                for (int i = 1; i < fmDetail.Length; i++) fmDetail[i].text = "";
            }
            else
            {
                var focus = GMFuturesMeeting.FocusOf(league, s);
                var plan = GMFuturesMeeting.PlanOf(league, s);
                var mentor = GMFuturesMeeting.MentorOf(league, s);
                fmDetail[0].text = $"{s.Template.PlayerName} · {GMSeasonReview.PositionLabelOf(s)} · {s.Age}세 · 계약 {s.ContractYears}년 · 연봉 {GMDiagnosticFormat.Short(s.Salary)}";
                fmDetail[1].text = $"OVR {s.BaseOverall} · 잠재력 {GMFuturesMeeting.PotentialMin(s)}~{GMFuturesMeeting.PotentialMax(s)} · {GMFuturesMeeting.GrowthTypeLabel(GMFuturesMeeting.TypeOf(s))}";
                fmDetail[2].text = $"훈련 방향 {GMFuturesMeeting.FocusLabel(focus)} · 적합도 {GMFuturesMeeting.FitLabel(s, focus)}";
                fmDetail[3].text = $"예상 연간 성장 +{GMFuturesMeeting.ExpectedGrowth(league, s)} (연도 전환 시 반영 · 변동 ±1)";
                fmDetail[4].text = mentor != null ? $"멘토 {mentor.Template.PlayerName} - 성장 +1 · 변동 하한 0(안정성)" : plan != null && plan.LastGrowthYear > 0 ? $"멘토 없음 · 직전 성장 +{plan.LastGrowth}({plan.LastGrowthYear})" : "멘토 없음 - 우측 더그아웃 리더를 배정하십시오";
            }
            var focuses = GMFuturesMeeting.FocusesFor(s);
            for (int k = 0; k < 4; k++)
            {
                CompyaUiKit.SetButtonText(fmFocus[k], GMFuturesMeeting.FocusLabel(focuses[k]));
                fmFocus[k].interactable = s != null;
                fmFocus[k].targetGraphic.color = s != null && GMFuturesMeeting.FocusOf(league, s) == focuses[k] ? ButtonOn : ButtonIdle;
            }
            fmSwapShown = s == null ? new List<Player>() : team.Roster.Where(p => p.Template != null && p.IsPitcher == s.IsPitcher && !p.IsCaptain)
                .OrderBy(GMStoveLeagueMarket.TradeValue).ThenBy(p => p.BaseOverall).Take(FmSwapRows).ToList();
            if (fmSwap != null && !fmSwapShown.Contains(fmSwap)) { if (fmSwapShown.Count >= FmSwapRows) fmSwapShown.RemoveAt(fmSwapShown.Count - 1); fmSwapShown.Add(fmSwap); }
            for (int i = 0; i < FmSwapRows; i++)
            {
                var p = i < fmSwapShown.Count ? fmSwapShown[i] : null;
                fmSwapRows[i].gameObject.SetActive(p != null);
                if (p == null) continue;
                fmSwapRows[i].targetGraphic.color = p == fmSwap ? RowSelected : RowIdle;
                CompyaUiKit.SetButtonText(fmSwapRows[i], $"{GMSeasonReview.PositionLabelOf(p)} {p.Template.PlayerName} · OVR {p.BaseOverall} · {p.Age}세 · {GMDiagnosticFormat.Short(p.Salary)}");
            }
            bool full = team.Roster.Count >= GMRosterTiers.FirstTeamMax;
            fmCallUp.interactable = s != null && (!full || fmSwap != null);
            CompyaUiKit.SetButtonText(fmCallUp, full ? (fmSwap != null ? "콜업 ↔ 스왑" : "스왑 대상 선택") : "1군 콜업");
            fmSendDown.interactable = fmSwap != null && team.Futures.Count < GMRosterTiers.FuturesMax;

            // 우측 - 멘토링 슬롯 · 후보
            var pairs = GMFuturesMeeting.Mentorships(league);
            for (int i = 0; i < fmSlots.Length; i++)
            {
                var pair = i < pairs.Count ? pairs[i] : default;
                fmSlots[i].text = pair.prospect == null ? $"슬롯 {i + 1}: 비어 있음" : $"슬롯 {i + 1}: {pair.mentor.Template.PlayerName}({pair.mentor.Age}세) → {pair.prospect.Template.PlayerName}({pair.prospect.Age}세)\n성장 +1 · 변동 하한 0 · 예상 +{GMFuturesMeeting.ExpectedGrowth(league, pair.prospect)}";
                fmSlots[i].color = pair.prospect == null ? Muted : White;
                fmSlotClear[i].gameObject.SetActive(pair.prospect != null);
            }
            fmMentorShown = GMFuturesMeeting.MentorCandidates(team).Take(FmMentorRows).ToList();
            for (int i = 0; i < FmMentorRows; i++)
            {
                var m = i < fmMentorShown.Count ? fmMentorShown[i] : null;
                fmMentorRows[i].gameObject.SetActive(m != null);
                if (m == null) continue;
                var mentee = pairs.FirstOrDefault(x => x.mentor == m).prospect;
                fmMentorRows[i].targetGraphic.color = mentee != null ? new Color(Gold.r, Gold.g, Gold.b, 0.18f) : RowIdle;
                CompyaUiKit.SetButtonText(fmMentorRows[i], $"{GMSeasonReview.PositionLabelOf(m)} {m.Template.PlayerName} · {m.Age}세 · OVR {m.BaseOverall} · {(mentee != null ? "담당 " + mentee.Template.PlayerName : "배정 가능")}");
            }
            fmMentorNote.text = fmMentorShown.Count == 0
                ? "1군에 더그아웃 리더 성향 베테랑이 없습니다 - FA · 트레이드로 리더를 영입하면 멘토링을 열 수 있습니다."
                : $"멘토링 {pairs.Count}/{GMFuturesMeeting.MaxMentorSlots}쌍 - 유망주 연간 성장 +1, 나쁜 해(변동 -1)를 막아 안정성을 높입니다.\n멘토가 퓨처스로 내려가거나 팀을 떠나면 해제됩니다.";
        }

        public void SelectFutures(Player p)
        {
            fmSelected = p;
            fmSwap = null;
            RefreshFuturesMeeting();
        }

        public void SelectSwapTarget(Player p)
        {
            fmSwap = fmSwap == p ? null : p;
            RefreshFuturesMeeting();
        }

        private void FmSay(string msg, bool ok)
        {
            if (fmMessage != null) { fmMessage.text = msg; fmMessage.color = ok ? ToneNeutral : ToneRisk; }
            SetStatus(msg);
        }

        public bool SetTrainingFocus(int index)
        {
            if (TurnBlocked(PaneFuturesMeeting, out _) || fmSelected == null) return false;
            var focuses = GMFuturesMeeting.FocusesFor(fmSelected);
            if (index < 0 || index >= focuses.Length) return false;
            bool ok = GMFuturesMeeting.SetFocus(League, fmSelected, focuses[index], out var msg);
            FmSay(msg, ok);
            RefreshFuturesMeeting();
            return ok;
        }

        /// <summary>[1군 콜업] - 1군에 자리가 있으면 바로 콜업, 가득이면 선택한 1군 선수와 스왑.</summary>
        public bool CallUpSelected()
        {
            if (TurnBlocked(PaneFuturesMeeting, out _)) return false;
            var team = UserTeam;
            if (team == null || fmSelected == null) { FmSay("콜업할 퓨처스 유망주를 고르십시오.", false); return false; }
            var swap = team.Roster.Count >= GMRosterTiers.FirstTeamMax ? fmSwap : null;
            bool ok = GMFuturesMeeting.CallUp(League, fmSelected, swap, out var msg);
            FmSay(msg, ok);
            if (ok) { fmSelected = null; fmSwap = null; }
            RefreshFuturesMeeting();
            return ok;
        }

        public bool SendDownSwapTarget()
        {
            if (TurnBlocked(PaneFuturesMeeting, out _)) return false;
            if (fmSwap == null) { FmSay("퓨처스로 내릴 1군 선수를 고르십시오.", false); return false; }
            bool ok = GMFuturesMeeting.SendDown(League, fmSwap, out var msg);
            FmSay(msg, ok);
            if (ok) fmSwap = null;
            RefreshFuturesMeeting();
            return ok;
        }

        public bool AssignMentorTo(Player mentor)
        {
            if (TurnBlocked(PaneFuturesMeeting, out _)) return false;
            bool ok = GMFuturesMeeting.AssignMentor(League, fmSelected, mentor, out var msg);
            FmSay(msg, ok);
            RefreshFuturesMeeting();
            return ok;
        }

        public bool ClearMentorSlot(int slot)
        {
            if (TurnBlocked(PaneFuturesMeeting, out _)) return false;
            var pairs = GMFuturesMeeting.Mentorships(League);
            if (slot < 0 || slot >= pairs.Count) return false;
            GMFuturesMeeting.ClearMentor(League, pairs[slot].prospect);
            FmSay($"멘토링 해제 - {pairs[slot].mentor.Template.PlayerName} → {pairs[slot].prospect.Template.PlayerName}", true);
            RefreshFuturesMeeting();
            return true;
        }

        // ================================================================== 갱신 - 2차 드래프트

        private void RefreshSecondDraft()
        {
            var league = League;
            var team = UserTeam;
            if (league == null || team == null || sdSummary == null) return;
            bool draftYear = GMSecondaryDraft.IsDraftYear(league.SeasonYear);
            bool open = GMSecondaryDraft.IsOpen(league);
            bool held = GMSecondaryDraft.IsHeld(league);
            var protectedIds = new HashSet<string>(GMSecondaryDraft.UserProtectedIds(league));
            var candidates = GMSecondaryDraft.Candidates(league, team);
            var auto = team.ReservePlayers.Where(p => p.Template != null && !candidates.Contains(p)).ToList();
            int exposedCount = candidates.Count(p => !protectedIds.Contains(p.InstanceId));
            sdSummary.text = $"보호 {protectedIds.Count}/{GMSecondaryDraft.ProtectLimit(league, team)} · 자동 보호 {auto.Count}명(1~3년 차 · FA 계약자 · 외국인) · 지명 대상(비보호) {exposedCount}명";

            var order = candidates.OrderByDescending(p => protectedIds.Contains(p.InstanceId)).ThenByDescending(p => p.BaseOverall).ThenBy(p => p.InstanceId, StringComparer.Ordinal)
                .Concat(auto.OrderByDescending(p => p.BaseOverall).ThenBy(p => p.InstanceId, StringComparer.Ordinal)).ToList();
            int pages = Math.Max(1, (order.Count + SdRows - 1) / SdRows);
            sdPage = Math.Min(sdPage, pages - 1);
            sdPageLabel.text = $"{sdPage + 1}/{pages} · 보류선수 {order.Count}명";
            sdShown = order.Skip(sdPage * SdRows).Take(SdRows).ToList();
            for (int r = 0; r < SdRows; r++)
            {
                var p = r < sdShown.Count ? sdShown[r] : null;
                sdRows[r].gameObject.SetActive(p != null);
                if (p == null) continue;
                string autoReason = GMSecondaryDraft.AutoProtectReason(league, p);
                bool prot = autoReason != null || protectedIds.Contains(p.InstanceId);
                sdRows[r].targetGraphic.color = r % 2 == 0 ? RowIdle : RowAlt;
                SetCell(sdCells[r, 0], autoReason != null ? "자동" : prot ? "■" : "□", prot ? ToneNeutral : ToneRisk);
                SetCell(sdCells[r, 1], GMSeasonReview.PositionLabelOf(p), White);
                SetCell(sdCells[r, 2], p.Template.PlayerName + (team.Futures.Contains(p) ? " (퓨처스)" : ""), White);
                SetCell(sdCells[r, 3], p.Age.ToString(), White);
                SetCell(sdCells[r, 4], p.BaseOverall.ToString(), White);
                SetCell(sdCells[r, 5], GMDiagnosticFormat.Short(p.Salary), White);
                SetCell(sdCells[r, 6], autoReason != null ? $"자동 보호({autoReason})" : prot ? "보호" : "지명 대상", autoReason != null ? Muted : prot ? ToneNeutral : ToneRisk);
            }
            sdRecommend.interactable = draftYear && !held;

            // 지명 패널
            int round = GMSecondaryDraft.NextRound(league, team.TeamCode);
            string roundText = round > GMSecondaryDraft.Rounds ? "지명 완료(최대 3명)" : $"다음 {round}R 양도금 {GMDiagnosticFormat.Won(GMSecondaryDraft.FeeFor(round))}";
            sdStatus.text = GMSecondaryDraft.StatusText(league) + $"\n가용 예산 {GMDiagnosticFormat.Won(team.Budget)} · {roundText} · 유출 {GMSecondaryDraft.LossesOf(league, team.TeamCode)}/{GMSecondaryDraft.MaxLossPerTeam}명";
            List<(Player player, GMTeamState from)> pool = draftYear && !held ? GMSecondaryDraft.Pool(league, team.TeamCode) : new List<(Player player, GMTeamState from)>();
            int poolPages = Math.Max(1, (pool.Count + SdPoolRows - 1) / SdPoolRows);
            sdPoolPageIndex = Math.Min(sdPoolPageIndex, poolPages - 1);
            sdPoolPage.text = pool.Count == 0 ? (held ? "지명 종료" : "지명 대상 없음") : $"{sdPoolPageIndex + 1}/{poolPages} · 비보호 {pool.Count}명 (OVR순)";
            sdPoolShown = pool.Skip(sdPoolPageIndex * SdPoolRows).Take(SdPoolRows).ToList();
            if (sdSelected != null && !pool.Any(x => x.Item1 == sdSelected)) sdSelected = null;
            for (int r = 0; r < SdPoolRows; r++)
            {
                var row = r < sdPoolShown.Count ? sdPoolShown[r] : default;
                var p = row.player;
                sdPoolRows[r].gameObject.SetActive(p != null);
                if (p == null) continue;
                sdPoolRows[r].targetGraphic.color = p == sdSelected ? RowSelected : r % 2 == 0 ? RowIdle : RowAlt;
                SetCell(sdPoolCells[r, 0], CompyaUiKit.ShortName(NameAliasTable.ToTeam(row.from.TeamCode)), White);
                SetCell(sdPoolCells[r, 1], GMSeasonReview.PositionLabelOf(p), White);
                SetCell(sdPoolCells[r, 2], p.Template.PlayerName, White);
                SetCell(sdPoolCells[r, 3], p.Age.ToString(), White);
                SetCell(sdPoolCells[r, 4], p.BaseOverall.ToString(), p.BaseOverall >= 70 ? ToneStrong : White);
                SetCell(sdPoolCells[r, 5], GMDiagnosticFormat.Short(p.Salary), White);
                SetCell(sdPoolCells[r, 6], row.from.Futures.Contains(p) ? "퓨처스" : "1군", Muted);
            }
            sdPick.interactable = open && sdSelected != null && round <= GMSecondaryDraft.Rounds;
            CompyaUiKit.SetButtonText(sdPick, sdSelected != null && round <= GMSecondaryDraft.Rounds
                ? $"{round}R 지명 - {sdSelected.Template.PlayerName} · {GMDiagnosticFormat.Short(GMSecondaryDraft.FeeFor(round))}"
                : "지명할 선수를 고르십시오");
            sdConclude.interactable = open;
            var picks = GMSecondaryDraft.PicksThisYear(league);
            sdResults.text = picks.Count == 0 ? (draftYear ? "지명 기록 없음" : "올해는 2차 드래프트가 없습니다 - Turn 3은 자동으로 건너뜁니다.")
                : "지명: " + string.Join(" · ", picks.Where(x => x.PickerCode == team.TeamCode || x.FromCode == team.TeamCode).Concat(picks).Distinct().Take(6)
                    .Select(x => $"{x.Round}R {x.PlayerName}({CompyaUiKit.ShortName(NameAliasTable.ToTeam(x.FromCode))}→{CompyaUiKit.ShortName(NameAliasTable.ToTeam(x.PickerCode))})"));
        }

        private void SdSay(string msg, bool ok)
        {
            SetStatus(msg);
            if (sdResults != null && !ok) sdResults.text = msg;
        }

        public bool ToggleSecondDraftProtect(Player p)
        {
            if (TurnBlocked(PaneSecondDraft, out _)) return false;
            bool ok = GMSecondaryDraft.ToggleProtect(League, p, out var msg);
            RefreshSecondDraft();
            SdSay(msg, ok);
            return ok;
        }

        public void ApplyRecommendedProtection()
        {
            if (TurnBlocked(PaneSecondDraft, out _) || League == null || GMSecondaryDraft.IsHeld(League)) return;
            var ids = GMSecondaryDraft.UserProtectedIds(League);
            ids.Clear();
            ids.AddRange(GMSecondaryDraft.Recommended(League, UserTeam).Select(p => p.InstanceId));
            RefreshSecondDraft();
            SetStatus($"추천 보호 명단 {ids.Count}인 적용(10대 가중치)");
        }

        public void SelectSecondDraftTarget(Player p)
        {
            sdSelected = p;
            RefreshSecondDraft();
        }

        public GMSecondDraftPick PickSecondDraft()
        {
            if (TurnBlocked(PaneSecondDraft, out _)) return null;
            var pick = GMSecondaryDraft.UserPick(League, sdSelected, out var msg);
            if (pick != null) { sdSelected = null; PlayNegotiationSfx(true, false); }
            RefreshSecondDraft();
            SdSay(msg, pick != null);
            return pick;
        }

        public List<GMSecondDraftPick> ConcludeSecondDraft()
        {
            if (TurnBlocked(PaneSecondDraft, out _) || League == null || !GMSecondaryDraft.IsOpen(League)) { SetStatus(GMSecondaryDraft.StatusText(League)); return new List<GMSecondDraftPick>(); }
            var picks = GMSecondaryDraft.Conclude(League);
            RefreshSecondDraft();
            SetStatus($"2차 드래프트 마감 - AI 지명 {picks.Count}건 · {GMSecondaryDraft.StatusText(League)}");
            return picks;
        }

        // ================================================================== 갱신 - 언론 브리핑실

        private void RefreshPress()
        {
            var league = League;
            var team = UserTeam;
            if (league == null || team == null || prTopic == null) return;
            var fo = GMFrontOffice.Ensure(league);
            var topic = GMPressBriefing.CurrentTopic(league);
            bool can = GMPressBriefing.CanBrief(league, out var reason);
            prTopic.text = topic != null ? $"주제: {GMPressBriefing.TopicLabel(topic.Value)} · {league.SeasonYear} 시즌" : "지금은 브리핑 주제가 없습니다";
            int projected = GMPressBriefing.ProjectedRank(league);
            prContext.text = $"팬 지지율 {team.FanSupport}/100 · 구단주 신임도 {fo.OwnerTrust}/100 · 구단주 기대 순위 {fo.Owner.ExpectedRank}위 · 현재 전망 {projected}위\n" +
                             (can ? "발언은 기록됩니다 - 순위를 공언하면 정규시즌 종료 때 구단주 신임도로 판정합니다." : reason);
            var options = topic != null ? GMPressBriefing.Options(topic.Value) : new List<GMPressOption>();
            for (int i = 0; i < 3; i++)
            {
                var o = i < options.Count ? options[i] : null;
                prOptions[i].gameObject.SetActive(o != null);
                prHints[i].text = o == null ? "" : $"{o.Effect}" + (o.TargetRank > 0 ? $"\n구단주 신임도 기대치 {(GMPressBriefing.ExpectedTrustDelta(league, o) > 0 ? "+" : "")}{GMPressBriefing.ExpectedTrustDelta(league, o)} (현재 전망 {projected}위 기준)" : "");
                if (o == null) continue;
                CompyaUiKit.SetButtonText(prOptions[i], o.Label);
                prOptions[i].interactable = can;
            }
            var statements = fo.PressStatements.AsEnumerable().Reverse().Take(PrStatementRows).ToList();
            for (int i = 0; i < PrStatementRows; i++)
            {
                var st = i < statements.Count ? statements[i] : null;
                prQuotes[i].text = st == null ? (i == 0 ? "기록된 발언이 없습니다." : "") : $"{st.Year} {GMPressBriefing.TopicLabel(st.Topic)} - {GMPressBriefing.ChoiceLabel(st.Choice)}";
                prNotes[i].text = st == null ? "" : $"\"{st.Quote}\"\n" + (st.Evaluated ? st.ResultNote : st.TargetRank > 0 ? $"판정 대기 - {st.TargetRank}위 이내 · 달성 {st.TrustOnSuccess:+0;-0} / 실패 {st.TrustOnFail:+0;-0}" : "순위 약속 없음");
                prNotes[i].color = st != null && st.Evaluated && !st.Kept ? ToneRisk : Muted;
            }
        }

        public GMPressResult ChoosePress(int index)
        {
            var league = League;
            if (league == null) return null;
            var choice = index == 0 ? GMPressChoice.Rebuild : index == 1 ? GMPressChoice.WinNow : GMPressChoice.NoComment;
            var r = GMPressBriefing.Brief(league, choice);
            RefreshPress();
            if (prResult != null) { prResult.text = r.Message; prResult.color = r.Applied ? White : ToneRisk; }
            SetStatus(r.Message);
            return r;
        }
    }
}
