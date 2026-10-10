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
    /// [TASK-GM-19] 프랜차이즈 애착 시스템 화면.
    ///   ① 선수 상세(스카우팅 리포트 팝업) [커리어 타임라인] 탭 - 연도 점 타임라인(연도를 누르면 그해 이벤트만) + 이벤트 카드 2열(색 점 = 입단/이적 · 데뷔 · 영광 · 계약 · 갈등 · 은퇴),
    ///      기록 출처 태그(실제 KBO 기록 · 시뮬레이션 기록 · 추정) · 근속 / 통산 WAR / 영구결번 자격 요약.
    ///   ② 은퇴식 팝업 - 영구결번 자격(근속 10년 · 통산 WAR 40) 은퇴 선수가 대기 큐에 있으면 허브가 띄운다: [영구결번 지정] · [일반 은퇴식] · [조용히 방출].
    ///   ③ 영구결번 선수 이름 = 황금색 고정(선수단 표 · 스카우팅 리포트 · 타임라인 · 실시간 경기).
    /// </summary>
    public partial class GMOotpFrontOfficeUIController
    {
        public const string TimelinePanelName = "CareerTimelinePanel", RetirementPopupName = "RetirementPopup";
        public const int TimelineYearSlots = 12, TimelineRows = 9, TimelineCols = 2;
        public static readonly Color RetiredGold = new Color(1f, 0.78f, 0.24f);

        private static readonly Color[] TimelineTones =
        {
            new Color(0.25f, 0.55f, 0.95f), // 입단 · 이적
            new Color(0.2f, 0.72f, 0.45f),  // 데뷔 · 성장
            new Color(0.95f, 0.72f, 0.12f), // 영광
            new Color(0.55f, 0.45f, 0.85f), // 계약
            new Color(0.92f, 0.38f, 0.25f), // 갈등
            new Color(0.45f, 0.48f, 0.55f), // 은퇴
        };

        // ---- ① 타임라인
        private RectTransform tlPanel;
        private Button scTabReport, scTabTimeline, tlAll, tlPrev, tlNext;
        private Text tlSummary, tlListTitle, tlPageLabel, tlEmpty;
        private readonly Button[] tlYears = new Button[TimelineYearSlots];
        private readonly Image[] tlYearDots = new Image[TimelineYearSlots];
        private readonly Image[] tlRowDots = new Image[TimelineRows * TimelineCols];
        private readonly Text[] tlRowTexts = new Text[TimelineRows * TimelineCols];
        private List<GMCareerEvent> tlEvents = new List<GMCareerEvent>();
        private List<int> tlYearList = new List<int>();
        private int tlYear, tlPage;

        public bool IsTimelineOpen => tlPanel != null && tlPanel.gameObject.activeSelf && pctPopup != null && pctPopup.gameObject.activeSelf;
        public RectTransform TimelinePanel => tlPanel;
        public IReadOnlyList<GMCareerEvent> TimelineEvents => tlEvents;

        // ---- ② 은퇴식
        private RectTransform rtPopup;
        private Text rtTitle, rtName, rtFacts, rtHighlights, rtResult;
        private readonly Button[] rtChoices = new Button[3];
        private readonly Text[] rtEffects = new Text[3];
        private Button rtClose;
        private GMRetirementCeremony rtCurrent;
        private bool rtSnoozed;

        public bool IsRetirementPopupOpen => rtPopup != null && rtPopup.gameObject.activeSelf;
        public RectTransform RetirementPopup => rtPopup;
        public GMRetirementCeremony CurrentCeremony => rtCurrent;

        // ================================================================== ① 커리어 타임라인

        /// <summary>스카우팅 리포트 팝업에 [스카우팅 리포트] · [커리어 타임라인] 탭과 타임라인 지면을 붙인다(BuildPercentilePopup 직후).</summary>
        private void BuildCareerTimeline()
        {
            if (pctPopup == null) return;
            var title = pctPopup.Find("PctTitle") as RectTransform;
            if (title != null) CompyaUiKit.SetBox(title, 130, 146, 900, 198);
            scTabReport = Btn(pctPopup, "ScTabReport", "스카우팅 리포트", 910, 150, 1140, 194, new Color(1f, 1f, 1f, 0.28f), ButtonPt);
            scTabTimeline = Btn(pctPopup, "ScTabTimeline", "커리어 타임라인", 1150, 150, 1380, 194, new Color(0f, 0f, 0f, 0.25f), ButtonPt);
            scTabReport.onClick.AddListener(() => ShowTimelineTab(false));
            scTabTimeline.onClick.AddListener(() => ShowTimelineTab(true));

            var ink = new Color(0.12f, 0.13f, 0.17f);
            var soft = new Color(0.36f, 0.38f, 0.44f);
            tlPanel = CompyaUiKit.Fill(pctPopup, TimelinePanelName);
            CompyaUiKit.Box(tlPanel, "Bg", 100, 206, 1820, 952, new Color(0.96f, 0.96f, 0.95f, 1f));
            tlSummary = L(tlPanel, "TlSummary", "", 130, 214, 1800, 262, BodyPt + 1, TextAnchor.MiddleLeft, ink);
            tlSummary.supportRichText = true;
            CompyaUiKit.Box(tlPanel, "TlAxis", 150, 318, 1770, 322, new Color(0.12f, 0.13f, 0.17f, 0.35f));
            float w = (1770f - 150f) / TimelineYearSlots;
            for (int k = 0; k < TimelineYearSlots; k++)
            {
                int slot = k;
                float x0 = 150 + k * w + 4, x1 = 150 + (k + 1) * w - 4;
                tlYears[k] = Btn(tlPanel, $"TlYear{k}", "", x0, 280, x1, 392, new Color(1f, 1f, 1f, 0.001f), SmallPt, ink);
                var label = tlYears[k].GetComponentInChildren<Text>(true);
                if (label != null) label.alignment = TextAnchor.LowerCenter;
                var dot = CompyaUiKit.Norm(tlYears[k].transform, "Dot", 0.5f, 0.66f, 0.5f, 0.66f);
                dot.sizeDelta = new Vector2(26f, 26f);
                tlYearDots[k] = CompyaUiKit.Paint(dot, TimelineTones[0]);
                tlYears[k].onClick.AddListener(() => SelectTimelineYear(slot));
            }
            tlListTitle = L(tlPanel, "TlListTitle", "", 130, 402, 1290, 440, PanelTitlePt, TextAnchor.MiddleLeft, ink);
            tlPrev = Btn(tlPanel, "TlPrev", "◀ 이전", 1300, 402, 1430, 440, new Color(0.3f, 0.32f, 0.38f), ButtonPt);
            tlPageLabel = L(tlPanel, "TlPage", "", 1434, 402, 1496, 440, SmallPt, TextAnchor.MiddleCenter, soft);
            tlNext = Btn(tlPanel, "TlNext", "다음 ▶", 1500, 402, 1630, 440, new Color(0.3f, 0.32f, 0.38f), ButtonPt);
            tlAll = Btn(tlPanel, "TlAll", "전체 연도", 1640, 402, 1800, 440, new Color(0.07f, 0.3f, 0.63f), ButtonPt);
            tlPrev.onClick.AddListener(() => { tlPage = Math.Max(0, tlPage - 1); BindTimeline(); });
            tlNext.onClick.AddListener(() => { tlPage++; BindTimeline(); });
            tlAll.onClick.AddListener(() => { tlYear = 0; tlPage = 0; BindTimeline(); });
            for (int i = 0; i < TimelineRows * TimelineCols; i++)
            {
                int col = i / TimelineRows, row = i % TimelineRows;
                float x0 = col == 0 ? 130 : 975, x1 = col == 0 ? 955 : 1800, y0 = 450 + row * 54;
                tlRowDots[i] = CompyaUiKit.Box(tlPanel, $"TlDot{i}", x0, y0 + 14, x0 + 18, y0 + 32, TimelineTones[0]);
                tlRowTexts[i] = L(tlPanel, $"TlRow{i}", "", x0 + 28, y0, x1, y0 + 52, SmallPt, TextAnchor.MiddleLeft, ink);
                tlRowTexts[i].supportRichText = true;
                tlRowTexts[i].lineSpacing = 1.05f;
                tlRowTexts[i].verticalOverflow = VerticalWrapMode.Truncate; // 2줄 카드 - 다음 행을 침범하지 않는다
            }
            tlEmpty = L(tlPanel, "TlEmpty", "", 130, 600, 1800, 700, BodyPt + 1, TextAnchor.MiddleCenter, soft);
            tlPanel.gameObject.SetActive(false);
        }

        /// <summary>[커리어 타임라인] 탭으로 선수 상세를 연다.</summary>
        public void OpenCareerTimeline(Player p)
        {
            OpenPercentiles(p);
            if (pctPopup != null && pctPopup.gameObject.activeSelf) ShowTimelineTab(true);
        }

        public void ShowTimelineTab(bool timeline)
        {
            if (tlPanel == null || scPlayer == null) return;
            tlPanel.gameObject.SetActive(timeline);
            scTabReport.targetGraphic.color = timeline ? new Color(0f, 0f, 0f, 0.25f) : new Color(1f, 1f, 1f, 0.28f);
            scTabTimeline.targetGraphic.color = timeline ? new Color(1f, 1f, 1f, 0.28f) : new Color(0f, 0f, 0f, 0.25f);
            if (!timeline) return;
            tlYear = 0;
            tlPage = 0;
            BindTimeline();
        }

        private void SelectTimelineYear(int slot)
        {
            if (slot < 0 || slot >= tlYearList.Count) return;
            tlYear = tlYear == tlYearList[slot] ? 0 : tlYearList[slot];
            tlPage = 0;
            BindTimeline();
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        private void BindTimeline()
        {
            var p = scPlayer;
            if (p == null || League == null || tlPanel == null) return;
            var all = GMCareerTimeline.Timeline(League, p);
            tlEvents = all;
            string name = GMRetirement.IsRetiredNumber(League, p) ? GMRetirement.Gold(p.Template.PlayerName) : p.Template.PlayerName;
            tlSummary.text = $"{name} · {GMCareerTimeline.Summary(League, p)}";

            // 연도 점 - 최근 12개 연도(이벤트가 있는 해)
            tlYearList = all.Select(e => e.Year).Distinct().OrderBy(y => y).ToList();
            if (tlYearList.Count > TimelineYearSlots) tlYearList = tlYearList.Skip(tlYearList.Count - TimelineYearSlots).ToList();
            for (int k = 0; k < TimelineYearSlots; k++)
            {
                bool used = k < tlYearList.Count;
                tlYears[k].gameObject.SetActive(used);
                if (!used) continue;
                int year = tlYearList[k];
                var events = all.Where(e => e.Year == year).ToList();
                int tone = events.Select(e => GMCareerTimeline.ToneOf(e.EventKind)).OrderBy(t => t == 2 ? 0 : t == 5 ? 1 : t == 4 ? 2 : 3 + t).First();
                tlYearDots[k].color = TimelineTones[tone];
                float size = Mathf.Min(34f, 18f + events.Count * 4f);
                tlYearDots[k].rectTransform.sizeDelta = new Vector2(size, size);
                CompyaUiKit.SetButtonText(tlYears[k], $"{year} · {events.Count}건");
                var label = tlYears[k].GetComponentInChildren<Text>(true);
                if (label != null) label.color = tlYear == year ? new Color(0.07f, 0.3f, 0.63f) : new Color(0.12f, 0.13f, 0.17f);
            }

            var shown = tlYear == 0 ? all : all.Where(e => e.Year == tlYear).ToList();
            int perPage = TimelineRows * TimelineCols;
            int pages = Math.Max(1, (shown.Count + perPage - 1) / perPage);
            tlPage = Mathf.Clamp(tlPage, 0, pages - 1);
            tlListTitle.text = tlYear == 0 ? $"커리어 이벤트 · 전체 {shown.Count}건 (연도 점을 누르면 그해만)" : $"{tlYear}년 이벤트 {shown.Count}건";
            tlPageLabel.text = $"{tlPage + 1}/{pages}";
            tlPrev.interactable = tlPage > 0;
            tlNext.interactable = tlPage < pages - 1;
            var page = shown.Skip(tlPage * perPage).Take(perPage).ToList();
            for (int i = 0; i < perPage; i++)
            {
                bool used = i < page.Count;
                tlRowDots[i].gameObject.SetActive(used);
                tlRowTexts[i].gameObject.SetActive(used);
                if (!used) continue;
                var e = page[i];
                var tone = TimelineTones[GMCareerTimeline.ToneOf(e.EventKind)];
                tlRowDots[i].color = tone;
                string src = e.Kind == (int)GMCareerEventKind.Joined ? "추정"
                    : e.Sim ? $"<color={GMScoutingReport.SimTagColor}>■</color> {GMScoutingReport.SimRecordTag}" : $"<color={GMScoutingReport.RealTagColor}>■</color> {GMScoutingReport.RealRecordTag}";
                tlRowTexts[i].text = $"<color={Hex(tone)}>{e.Year}</color>  {GMCareerTimeline.KindLabel(e.EventKind)} - {e.Title}\n" +
                                     $"<color=#5C616E>{(string.IsNullOrEmpty(e.Detail) ? "" : e.Detail + " · ")}{src}</color>";
            }
            tlEmpty.gameObject.SetActive(shown.Count == 0);
            tlEmpty.text = shown.Count == 0 ? "아직 기록된 커리어 이벤트가 없습니다 - 계약 · 수상 · 우승이 이곳에 쌓입니다." : "";
        }

        // ================================================================== ② 은퇴식 팝업

        private void BuildRetirementPopup()
        {
            rtPopup = CompyaUiKit.Fill(root, RetirementPopupName);
            CompyaUiKit.Paint(rtPopup, new Color(0f, 0f, 0f, 0.72f), true);
            CompyaUiKit.Box(rtPopup, "RtBox", 360, 200, 1560, 880, new Color(0.1f, 0.1f, 0.13f, 0.99f));
            CompyaUiKit.Box(rtPopup, "RtAccent", 360, 200, 1560, 208, RetiredGold);
            rtTitle = L(rtPopup, "RtTitle", "", 400, 224, 1520, 270, PanelTitlePt + 5, TextAnchor.MiddleLeft, RetiredGold);
            rtName = L(rtPopup, "RtName", "", 400, 280, 1520, 350, BigPt, TextAnchor.MiddleLeft, RetiredGold);
            rtFacts = L(rtPopup, "RtFacts", "", 400, 360, 1520, 450, BodyPt + 2, TextAnchor.UpperLeft, White);
            rtFacts.lineSpacing = 1.15f;
            rtHighlights = L(rtPopup, "RtHighlights", "", 400, 458, 1520, 600, BodyPt + 1, TextAnchor.UpperLeft, Muted);
            rtHighlights.lineSpacing = 1.15f;
            var colors = new[] { new Color(0.72f, 0.55f, 0.1f), new Color(0.2f, 0.42f, 0.7f), new Color(0.35f, 0.36f, 0.42f) };
            for (int i = 0; i < 3; i++)
            {
                int choice = i;
                float x0 = 400 + i * 380, x1 = x0 + 360;
                rtChoices[i] = Btn(rtPopup, $"RtChoice{i}", GMRetirement.ChoiceLabel((GMCeremonyChoice)i), x0, 612, x1, 676, colors[i], ButtonPt + 2);
                rtEffects[i] = L(rtPopup, $"RtEffect{i}", GMRetirement.ChoiceEffect((GMCeremonyChoice)i), x0, 684, x1, 790, SmallPt, TextAnchor.UpperLeft, Muted);
                rtChoices[i].onClick.AddListener(() => ResolveRetirement((GMCeremonyChoice)choice));
            }
            rtResult = L(rtPopup, "RtResult", "", 400, 800, 1280, 864, SmallPt + 1, TextAnchor.MiddleLeft, RetiredGold);
            rtClose = Btn(rtPopup, "RtClose", "나중에", 1300, 806, 1520, 860, ButtonIdle, ButtonPt);
            rtClose.onClick.AddListener(CloseRetirementPopup);
            rtPopup.gameObject.SetActive(false);
        }

        /// <summary>허브 갱신 끝 - 대기 중인 은퇴식이 있으면 팝업(나중에를 누르면 다음 Bind까지 묻지 않는다).</summary>
        private void PromptRetirementCeremony()
        {
            if (rtPopup == null || League == null || rtSnoozed || IsRetirementPopupOpen) return;
            var c = GMRetirement.NextCeremony(League);
            if (c != null) ShowRetirementCeremony(c);
        }

        public bool ShowRetirementCeremony(GMRetirementCeremony c)
        {
            if (rtPopup == null || c == null || League == null) return false;
            rtCurrent = c;
            var fo = GMFrontOffice.Ensure(League);
            rtTitle.text = $"{c.Year} 은퇴식 결정 · {NameAliasTable.DisplayTeamName(c.TeamCode)}의 전설";
            rtName.text = $"{c.PlayerName} · {c.Position}";
            rtFacts.text = $"{c.Age}세 은퇴 · {NameAliasTable.DisplayTeamName(c.TeamCode)} 근속 {c.TenureYears}년 · 통산 WAR {c.CareerWar:0.0} · 은퇴 직전 OVR {c.Ovr}\n" +
                           $"영구결번 자격(근속 {GMRetirement.RetiredNumberTenure}년 · WAR {GMRetirement.RetiredNumberWar:0} 이상) 충족 · 마케팅 예산 {GMDiagnosticFormat.Short(UserTeam?.MarketingBudget ?? 0)} · 팬 지지율 {UserTeam?.FanSupport ?? 0}";
            var retiredPlayer = FindTimelinePlayer(c);
            var glory = retiredPlayer == null ? new List<GMCareerEvent>() : GMCareerTimeline.Timeline(League, retiredPlayer).Where(e => GMCareerTimeline.ToneOf(e.EventKind) == 2).Reverse().Take(4).ToList();
            rtHighlights.text = glory.Count > 0 ? "커리어 하이라이트 · " + string.Join(" / ", glory.Select(e => $"{e.Year} {e.Title}"))
                : $"구단 역대 영구결번 {fo.RetiredNumbers.Count(n => n.TeamCode == c.TeamCode)}명 · 팬들은 그의 마지막 인사를 기다리고 있습니다.";
            for (int i = 0; i < 3; i++)
            {
                var choice = (GMCeremonyChoice)i;
                bool afford = GMRetirement.CanAfford(League, choice);
                rtChoices[i].interactable = !c.Resolved && afford;
                rtEffects[i].text = GMRetirement.ChoiceEffect(choice) + (afford ? "" : " (마케팅 예산 부족)");
            }
            rtResult.text = c.Resolved ? c.Result : "단장의 결정이 남은 커리어의 이름 색과 팬심을 바꿉니다.";
            CompyaUiKit.SetButtonText(rtClose, c.Resolved ? "확인" : "나중에");
            rtPopup.gameObject.SetActive(true);
            rtPopup.SetAsLastSibling();
            return true;
        }

        private static Player FindTimelinePlayer(GMRetirementCeremony c) => GMRetirement.RetiredPlayer(c?.PlayerId);

        public GMCeremonyResult ResolveRetirement(GMCeremonyChoice choice)
        {
            var c = rtCurrent;
            var r = GMRetirement.Resolve(League, c, choice);
            if (r.Applied)
            {
                SetStatus(r.Message);
                if (choice == GMCeremonyChoice.RetireNumber) KBOManager.Managers.GMAudioManager.Ensure().PlayEventBgm(GMAudioEvent.MajorResult, League.SelectedTeamCode);
            }
            ShowRetirementCeremony(c);
            if (!r.Applied) rtResult.text = r.Message;
            return r;
        }

        public void CloseRetirementPopup()
        {
            if (rtPopup == null) return;
            bool resolved = rtCurrent != null && rtCurrent.Resolved;
            rtPopup.gameObject.SetActive(false);
            rtCurrent = null;
            if (!resolved) { rtSnoozed = true; return; }
            var next = GMRetirement.NextCeremony(League);
            if (next != null) ShowRetirementCeremony(next);
            else Refresh();
        }
    }
}
