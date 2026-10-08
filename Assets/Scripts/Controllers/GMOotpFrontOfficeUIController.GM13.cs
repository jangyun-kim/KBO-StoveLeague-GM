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
    /// [TASK-GM-13] 선수단 회의실(GMLockerRoomView - 1단 탭 [선수단]의 4번째 서브 탭, 1920×1080 · Normal · 15pt 이상) + 라커룸 사건 팝업.
    ///   좌 - 선수단의 단장 신뢰도 · 평균 구단 충성도 프로그레스 바 · 팀 분위기 · 충성도 분포 · 라커룸 사건(대응 팝업 · 사건 점검)
    ///   중 - 선수별 충성도(등급) · 핵심 유대(배터리 · 키스톤 · 멘토-멘티) · 불만 사항(● 아이콘 + 텍스트)
    ///   우 - 진행 중(ACTIVE) 약속 트래커(남은 유예 기간) · 최근 판정(이행/위반)
    /// 지시서의 GMLockerRoomView는 별도 MonoBehaviour 대신 허브 화면(Pane) 이름으로 두었다(GM-11과 같은 구조).
    /// </summary>
    public partial class GMOotpFrontOfficeUIController
    {
        public const string PaneLockerRoom = "GMLockerRoomView", LockerEventPopupName = "LockerEventPopup";
        public const int LrRows = 14, LrPromiseRows = 8, LrRecentRows = 4, LrEventRows = 3;

        private static readonly float[] LrCols = { 580, 650, 800, 930, 1110, 1336 };
        private static readonly string[] LrHeads = { "포지션", "이름", "충성도", "핵심 유대", "불만 사항" };
        private static readonly Color LrIssue = new Color(1f, 0.62f, 0.2f);

        private RectTransform lrTrustFill, lrLoyaltyFill, lrEventPopup;
        private Text lrTrustValue, lrLoyaltyValue, lrPageLabel, lrPromiseTitle, lrMessage, lrEventTitle, lrEventBody, lrEventResult;
        private readonly Text[] lrInfo = new Text[4], lrEvents = new Text[LrEventRows], lrPromise = new Text[LrPromiseRows], lrPromiseDue = new Text[LrPromiseRows], lrRecent = new Text[LrRecentRows];
        private readonly Text[] lrEventHints = new Text[3];
        private readonly Button[] lrRows = new Button[LrRows], lrEventChoices = new Button[3];
        private readonly Text[,] lrCells = new Text[LrRows, 5];
        private Button lrEventOpen;
        private int lrPage;
        private GMDynamicEvent lrEvent;

        public bool IsLockerEventOpen => lrEventPopup != null && lrEventPopup.gameObject.activeSelf;
        public GMDynamicEvent LockerEvent => lrEvent;

        public void OpenLockerRoom()
        {
            SelectMainTab(2);
            SelectSubTab(subTabs[2].FindIndex(s => s.Pane == PaneLockerRoom));
        }

        // ================================================================== 조립

        private void BuildLockerRoomPane()
        {
            var pane = PaneRoot(PaneLockerRoom);

            var sum = Panel(pane, "LrSummaryPanel", 12, 248, 560, 1036);
            L(sum, "LrTitle", "선수단 회의실 (LOCKER ROOM)", 24, 254, 548, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            L(sum, "LrTrustLabel", "선수단의 단장 신뢰도", 24, 294, 380, 324, BodyPt, TextAnchor.MiddleLeft, Muted);
            lrTrustValue = L(sum, "LrTrustValue", "", 384, 294, 548, 324, BannerPt, TextAnchor.MiddleRight, White);
            lrTrustFill = Bar(sum, "LrTrustBar", 24, 330, 548, 350, BarGreen);
            L(sum, "LrLoyaltyLabel", "평균 구단 충성도", 24, 362, 380, 392, BodyPt, TextAnchor.MiddleLeft, Muted);
            lrLoyaltyValue = L(sum, "LrLoyaltyValue", "", 384, 362, 548, 392, BannerPt, TextAnchor.MiddleRight, White);
            lrLoyaltyFill = Bar(sum, "LrLoyaltyBar", 24, 398, 548, 418, Gold);
            for (int i = 0; i < lrInfo.Length; i++)
            {
                float y0 = 430 + i * 34;
                lrInfo[i] = L(sum, $"LrInfo{i}", "", 24, y0, 548, y0 + 30, CellPt, TextAnchor.MiddleLeft, White);
            }
            L(sum, "LrEventTitle", "라커룸 사건 (1티어 동적 사건)", 24, 572, 548, 602, PanelTitlePt - 1, TextAnchor.MiddleLeft, Gold);
            for (int i = 0; i < LrEventRows; i++)
            {
                float y0 = 608 + i * 52;
                lrEvents[i] = L(sum, $"LrEvent{i}", "", 24, y0, 548, y0 + 48, CellPt, TextAnchor.UpperLeft, White);
            }
            lrEventOpen = Btn(sum, "LrEventOpen", "사건 대응 열기", 24, 770, 548, 814, ButtonOn, ButtonPt);
            lrEventOpen.onClick.AddListener(() => OpenLockerEvent());
            Btn(sum, "LrEventCheck", "라커룸 점검 (사건 조건 확인)", 24, 822, 548, 866, ButtonIdle, ButtonPt).onClick.AddListener(() => CheckLockerEvents());
            lrMessage = L(sum, "LrMessage", "", 24, 874, 548, 1030, CellPt, TextAnchor.UpperLeft, Muted);
            lrMessage.lineSpacing = 1.1f;

            var players = Panel(pane, "LrPlayersPanel", 572, 248, 1340, 1036);
            L(players, "LrPlayersTitle", "선수별 충성도 · 관계망 · 불만 사항", 584, 254, 1328, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int c = 0; c < LrHeads.Length; c++)
                L(players, $"LrHead{c}", LrHeads[c], LrCols[c], 290, LrCols[c + 1] - 4, 318, CellPt, c == 1 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, Muted);
            for (int r = 0; r < LrRows; r++)
            {
                float y0 = 322 + r * 48;
                lrRows[r] = Row(players, $"LrRow{r}", 576, y0, 1336, y0 + 44, LrCols, lrCells, r, CellPt);
            }
            Btn(players, "LrPrev", "◀ 이전", 584, 996, 720, 1030, ButtonIdle, BodyPt).onClick.AddListener(() => { lrPage = Math.Max(0, lrPage - 1); RefreshLockerRoom(); });
            lrPageLabel = L(players, "LrPage", "", 724, 996, 1188, 1030, BodyPt, TextAnchor.MiddleCenter, White);
            Btn(players, "LrNext", "다음 ▶", 1192, 996, 1328, 1030, ButtonIdle, BodyPt).onClick.AddListener(() => { lrPage++; RefreshLockerRoom(); });

            var promises = Panel(pane, "LrPromisePanel", 1352, 248, 1908, 1036);
            lrPromiseTitle = L(promises, "LrPromiseTitle", "", 1364, 254, 1896, 286, PanelTitlePt, TextAnchor.MiddleLeft, Gold);
            for (int i = 0; i < LrPromiseRows; i++)
            {
                float y0 = 294 + i * 64;
                lrPromise[i] = L(promises, $"LrPromise{i}", "", 1364, y0, 1896, y0 + 30, BodyPt, TextAnchor.MiddleLeft, White);
                lrPromiseDue[i] = L(promises, $"LrPromiseDue{i}", "", 1364, y0 + 32, 1896, y0 + 58, CellPt, TextAnchor.MiddleLeft, Muted);
            }
            L(promises, "LrRecentTitle", "최근 판정 (이행 · 위반)", 1364, 812, 1896, 842, PanelTitlePt - 1, TextAnchor.MiddleLeft, Gold);
            for (int i = 0; i < LrRecentRows; i++)
            {
                float y0 = 848 + i * 46;
                lrRecent[i] = L(promises, $"LrRecent{i}", "", 1364, y0, 1896, y0 + 42, CellPt, TextAnchor.UpperLeft, White);
            }
        }

        private void BuildLockerEventPopup()
        {
            lrEventPopup = CompyaUiKit.Fill(root, LockerEventPopupName);
            CompyaUiKit.Paint(lrEventPopup, new Color(0f, 0f, 0f, 0.72f), true);
            CompyaUiKit.Box(lrEventPopup, "EventBox", 460, 220, 1460, 900, new Color(0.13f, 0.13f, 0.16f, 0.98f));
            lrEventTitle = L(lrEventPopup, "EventTitle", "", 500, 240, 1420, 300, PanelTitlePt + 1, TextAnchor.MiddleLeft, Gold);
            lrEventBody = L(lrEventPopup, "EventBody", "", 500, 306, 1420, 446, BodyPt, TextAnchor.UpperLeft, White);
            lrEventBody.lineSpacing = 1.15f;
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                float y0 = 456 + i * 104;
                lrEventChoices[i] = Btn(lrEventPopup, $"EventChoice{i}", "", 500, y0, 1420, y0 + 48, ButtonOn, ButtonPt);
                lrEventChoices[i].onClick.AddListener(() => ChooseLockerEvent(index));
                lrEventHints[i] = L(lrEventPopup, $"EventHint{i}", "", 500, y0 + 52, 1420, y0 + 98, CellPt, TextAnchor.UpperLeft, Muted);
            }
            lrEventResult = L(lrEventPopup, "EventResult", "", 500, 770, 1420, 830, BodyPt, TextAnchor.UpperLeft, White);
            Btn(lrEventPopup, "EventClose", "닫기", 1220, 840, 1420, 884, ButtonIdle, ButtonPt).onClick.AddListener(CloseLockerEvent);
            lrEventPopup.gameObject.SetActive(false);
        }

        // ================================================================== 갱신

        private void RefreshLockerRoom()
        {
            var league = League;
            var team = UserTeam;
            if (league == null || team == null || lrTrustValue == null) return;
            double loyalty = GMLockerRoom.AverageLoyalty(team);
            lrTrustValue.text = $"{team.LockerRoomTrust} / 100";
            lrTrustValue.color = team.LockerRoomTrust < GMDynamicEventEngine.FactionTrustBelow ? ToneRisk : White;
            SetFill(lrTrustFill, team.LockerRoomTrust / 100f);
            lrLoyaltyValue.text = $"{loyalty:0.0} / 100";
            SetFill(lrLoyaltyFill, (float)(loyalty / 100.0));
            int teamwork = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff).TeamworkScore;
            int high = team.Roster.Count(p => p.Loyalty >= GMDynamicEventEngine.HighLoyalty), low = team.Roster.Count(p => p.Loyalty < 40);
            var captain = GMDynamicEventEngine.Captain(team);
            lrInfo[0].text = $"팀 분위기(팀워크) {teamwork} · 구단주 신임도 {GMFrontOffice.Ensure(league).OwnerTrust}";
            lrInfo[1].text = $"충성도 분포 - 높음 {high}명 · 보통 {team.Roster.Count - high - low}명 · 낮음 {low}명";
            lrInfo[2].text = $"자존심 4 이상 {GMDynamicEventEngine.FactionMembers(team).Count}명 (파벌 기준: 3명 · 신뢰도 40 미만)";
            lrInfo[3].text = captain != null ? $"주장 {captain.Template.PlayerName} · 충성도 {captain.Loyalty}" : "주장 미임명 - 면담 · 중재 확률이 낮아집니다";

            var pending = GMDynamicEventEngine.Pending(league).ToList();
            var recentEvents = GMFrontOffice.Ensure(league).DynamicEvents.Where(e => e.TeamCode == team.TeamCode).Reverse().Take(LrEventRows).ToList();
            for (int i = 0; i < LrEventRows; i++)
            {
                var e = i < recentEvents.Count ? recentEvents[i] : null;
                lrEvents[i].text = e == null ? (i == 0 ? "진행 중인 사건 없음" : "") : $"{(e.Resolved ? "[종결]" : "[대응 필요]")} {GMDynamicEventEngine.KindLabel(e.Kind)} - {e.PlayerName}\n{(e.Resolved ? Clip(e.ResultText, 32) : e.Choices.Count + "개 선택지 - [사건 대응 열기]")}";
                lrEvents[i].color = e != null && !e.Resolved ? ToneRisk : White;
            }
            lrEventOpen.interactable = pending.Count > 0;
            CompyaUiKit.SetButtonText(lrEventOpen, pending.Count > 0 ? $"사건 대응 열기 ({pending.Count}건)" : "대응할 사건 없음");

            // 선수 목록 - 불만이 있는 선수 · 충성도 낮은 순
            var regulars = GMLockerRoom.Regulars(team);
            double median = GMLockerRoom.MedianSalary(team);
            int source = GMSeasonReview.SourceKind(league);
            var order = team.Roster.Where(p => p.Template != null).OrderBy(p => p.Loyalty).ToList();
            int pages = Math.Max(1, (order.Count + LrRows - 1) / LrRows);
            lrPage = Math.Min(lrPage, pages - 1);
            lrPageLabel.text = $"{lrPage + 1}/{pages} · {order.Count}명 (충성도 낮은 순)";
            for (int r = 0; r < LrRows; r++)
            {
                int index = lrPage * LrRows + r;
                var p = index < order.Count ? order[index] : null;
                lrRows[r].gameObject.SetActive(p != null);
                if (p == null) continue;
                lrRows[r].targetGraphic.color = r % 2 == 0 ? RowIdle : RowAlt;
                var bonds = GMPlayerBonds.For(team, p);
                var issues = GMLockerRoom.Complaints(league, team, p, regulars, median, source);
                SetCell(lrCells[r, 0], GMSeasonReview.PositionLabelOf(p), White);
                SetCell(lrCells[r, 1], p.Template.PlayerName + (p.IsCaptain ? " (주장)" : ""), White);
                SetCell(lrCells[r, 2], $"{p.Loyalty} {GMLockerRoom.LoyaltyLabel(p.Loyalty)}", p.Loyalty >= GMDynamicEventEngine.HighLoyalty ? ToneNeutral : p.Loyalty < 40 ? ToneRisk : White);
                SetCell(lrCells[r, 3], bonds.Count == 0 ? "-" : $"{GMPlayerBonds.KindLabel(bonds[0].kind)} {bonds[0].partner.Template.PlayerName}" + (bonds.Count > 1 ? $" 외 {bonds.Count - 1}" : ""), bonds.Count > 0 ? ToneWeak : Muted);
                SetCell(lrCells[r, 4], issues.Count == 0 ? "불만 없음" : "● " + string.Join(" · ", issues.Take(2)), issues.Count == 0 ? Muted : LrIssue);
            }

            // 약속 트래커
            var active = GMPromiseSystem.Active(league).Where(x => x.TeamCode == team.TeamCode).OrderBy(x => x.DueYear).ToList();
            lrPromiseTitle.text = $"진행 중인 약속 (ACTIVE {active.Count}건)";
            for (int i = 0; i < LrPromiseRows; i++)
            {
                var pr = i < active.Count ? active[i] : null;
                lrPromise[i].text = pr == null ? (i == 0 ? "진행 중인 약속이 없습니다." : "") : $"{i + 1}. {GMPromiseSystem.Describe(pr)}";
                lrPromiseDue[i].text = pr == null ? (i == 0 ? "협상실 [주전 보장] 카드 · 라커룸 사건에서 생깁니다." : "") : $"{GMPromiseSystem.RemainingText(league, pr)} · 출처 {pr.Source}";
            }
            var recent = GMPromiseSystem.All(league).Where(x => x.TeamCode == team.TeamCode && (x.State == GMPromiseState.Fulfilled || x.State == GMPromiseState.Broken)).Reverse().Take(LrRecentRows).ToList();
            for (int i = 0; i < LrRecentRows; i++)
            {
                var pr = i < recent.Count ? recent[i] : null;
                lrRecent[i].text = pr == null ? (i == 0 ? "판정된 약속 없음" : "") : $"[{GMPromiseSystem.StateLabel(pr.State)}] {GMPromiseSystem.Describe(pr)}\n{pr.ResultNote}";
                lrRecent[i].color = pr == null ? Muted : pr.State == GMPromiseState.Broken ? ToneRisk : ToneNeutral;
            }
        }

        // ================================================================== 사건

        /// <summary>[라커룸 점검] - 사건 조건을 지금 확인해 새 사건을 만든다(같은 종류는 해마다 1회). 새로 생긴 사건 수.</summary>
        public int CheckLockerEvents()
        {
            if (League == null) return 0;
            var created = GMDynamicEventEngine.Generate(League);
            string msg = created.Count > 0 ? $"라커룸 사건 {created.Count}건 발생 - {string.Join(" / ", created.Select(e => GMDynamicEventEngine.KindLabel(e.Kind)))}" : "새로 발생한 사건이 없습니다(조건 미충족 또는 올해 이미 발생).";
            if (lrMessage != null) lrMessage.text = msg;
            SetStatus(msg);
            RefreshLockerRoom();
            if (created.Count > 0) OpenLockerEvent(created[0]);
            return created.Count;
        }

        public bool OpenLockerEvent(GMDynamicEvent ev = null)
        {
            if (lrEventPopup == null || League == null) return false;
            ev = ev ?? GMDynamicEventEngine.Pending(League).FirstOrDefault();
            if (ev == null) return false;
            lrEvent = ev;
            lrEventTitle.text = ev.Title;
            lrEventBody.text = ev.Body;
            for (int i = 0; i < 3; i++)
            {
                bool has = i < ev.Choices.Count;
                lrEventChoices[i].gameObject.SetActive(has);
                lrEventChoices[i].interactable = has && !ev.Resolved;
                if (has) CompyaUiKit.SetButtonText(lrEventChoices[i], $"{i + 1}. {ev.Choices[i]}");
                lrEventHints[i].text = has && i < ev.ChoiceHints.Count ? ev.ChoiceHints[i] : "";
            }
            lrEventResult.text = ev.Resolved ? ev.ResultText : "선택지를 고르십시오 - 결과는 충성도 · 선수단 신뢰도 · 예산에 즉시 반영됩니다.";
            lrEventPopup.gameObject.SetActive(true);
            lrEventPopup.SetAsLastSibling();
            return true;
        }

        public GMDynamicEventResult ChooseLockerEvent(int index)
        {
            if (League == null || lrEvent == null) return null;
            var r = GMDynamicEventEngine.Resolve(League, lrEvent, index);
            lrEventResult.text = r.Message;
            lrEventResult.color = r.Applied ? (r.Audio == GMAudioEvent.Tension ? ToneRisk : ToneNeutral) : ToneRisk;
            foreach (var b in lrEventChoices) b.interactable = !lrEvent.Resolved;
            if (lrMessage != null) lrMessage.text = r.Message;
            SetStatus(r.Message);
            RefreshLockerRoom();
            return r;
        }

        private static string Clip(string s, int max) => string.IsNullOrEmpty(s) || s.Length <= max ? s ?? "" : s.Substring(0, max) + "…";

        public void CloseLockerEvent()
        {
            if (lrEventPopup != null) lrEventPopup.gameObject.SetActive(false);
            lrEvent = null;
            Refresh();
        }
    }
}
