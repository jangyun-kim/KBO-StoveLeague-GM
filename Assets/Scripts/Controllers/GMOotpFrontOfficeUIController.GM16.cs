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
    /// [TASK-GM-16] UI 시각화 고도화(덕질 포인트) + 조력자 시스템.
    ///   ① 조력자(운영팀장) 대화 모달 - 스토브리그 Turn을 넘길 때 미계약 만료자 · 2차 드래프트/신인 지명 0명 · 주장 미임명 · 1군 부족이 있으면
    ///      단장과 반대 성별의 운영팀장이 스토리형 대사(한 줄씩 [다음 ▶] · [대화 건너뛰기])로 개입하고 3지선다를 낸다:
    ///      [제가 직접 처리하겠습니다(돌아가기)] · [팀장 선에서 적당히 처리해 주세요(AI 위임)] · [괜찮으니 그냥 진행하세요(무시)].
    ///   ② 선수 스카우팅 리포트 팝업(구 백분위 팝업 확장) - 프로필(OVR · 스타터 덱 등급 · 포지션) · 헤드라인 · 성향 · 수상 · 백분위 막대(색 구간) + 20-80 등급 · 기록 · 스카우트 코멘트.
    ///   ③ 응원단 탭 = 단상 라인업 포스터(사진) + 엔트리 시너지 · 4대 스탯 합계. [TASK-GM-17] 클래식(세로) 영입 · 보유 화면 진입점은 제거했다.
    /// </summary>
    public partial class GMOotpFrontOfficeUIController
    {
        public const string AssistantPopupName = "AssistantPopup";

        // ---- ① 조력자
        private RectTransform assistantPopup;
        private Text asInitial, asName, asTitle, asLine, asProgress;
        private Image asPortrait;
        private Button asNext, asSkip, asReturn, asDelegate, asIgnore;
        private List<GMAssistantIssue> asIssues = new List<GMAssistantIssue>();
        private List<string> asLines = new List<string>();
        private int asIndex;

        public bool IsAssistantOpen => assistantPopup != null && assistantPopup.gameObject.activeSelf;
        public IReadOnlyList<GMAssistantIssue> AssistantIssues => asIssues;
        public int AssistantLineIndex => asIndex;
        public int AssistantLineCount => asLines.Count;
        public string AssistantLine => asLine != null ? asLine.text : "";
        public bool AssistantChoicesShown => asReturn != null && asReturn.gameObject.activeSelf;

        private void BuildAssistantPopup()
        {
            assistantPopup = CompyaUiKit.Fill(root, AssistantPopupName);
            CompyaUiKit.Paint(assistantPopup, new Color(0f, 0f, 0f, 0.62f), true);
            CompyaUiKit.Box(assistantPopup, "AsBox", 240, 540, 1680, 1010, new Color(0.11f, 0.12f, 0.16f, 0.98f));
            CompyaUiKit.Box(assistantPopup, "AsAccent", 240, 540, 1680, 546, Gold);
            asPortrait = CompyaUiKit.Box(assistantPopup, "AsPortrait", 270, 570, 470, 790, new Color(0.3f, 0.36f, 0.55f));
            asInitial = L(assistantPopup, "AsInitial", "", 270, 590, 470, 740, BigPt + 6, TextAnchor.MiddleCenter, White);
            asName = L(assistantPopup, "AsName", "", 270, 748, 470, 786, SmallPt, TextAnchor.MiddleCenter, White);
            asTitle = L(assistantPopup, "AsTitle", "", 500, 566, 1650, 604, PanelTitlePt + 1, TextAnchor.MiddleLeft, Gold);
            asLine = L(assistantPopup, "AsLine", "", 500, 612, 1650, 790, BannerPt + 1, TextAnchor.UpperLeft, White);
            asLine.lineSpacing = 1.2f;
            asProgress = L(assistantPopup, "AsProgress", "", 500, 800, 900, 840, SmallPt, TextAnchor.MiddleLeft, Muted);
            asNext = Btn(assistantPopup, "AsNext", "다음 ▶", 1250, 800, 1440, 846, ButtonOn, ButtonPt);
            asSkip = Btn(assistantPopup, "AsSkip", "대화 건너뛰기", 1450, 800, 1650, 846, ButtonIdle, ButtonPt);
            asNext.onClick.AddListener(AssistantNext);
            asSkip.onClick.AddListener(AssistantSkip);
            asReturn = Btn(assistantPopup, "AsChoiceReturn", "제가 직접 처리하겠습니다 (돌아가기)", 270, 870, 735, 960, new Color(0.25f, 0.27f, 0.36f), ButtonPt);
            asDelegate = Btn(assistantPopup, "AsChoiceDelegate", "팀장 선에서 적당히 처리해 주세요 (AI 위임)", 745, 870, 1210, 960, ContinueGreen, ButtonPt);
            asIgnore = Btn(assistantPopup, "AsChoiceIgnore", "괜찮으니 그냥 진행하세요 (무시)", 1220, 870, 1650, 960, new Color(0.45f, 0.25f, 0.22f), ButtonPt);
            asReturn.onClick.AddListener(() => AssistantChoose(0));
            asDelegate.onClick.AddListener(() => AssistantChoose(1));
            asIgnore.onClick.AddListener(() => AssistantChoose(2));
            assistantPopup.gameObject.SetActive(false);
        }

        /// <summary>조력자 모달을 연다(Turn 진행 전 필수 행동 누락).</summary>
        public void ShowAssistant(List<GMAssistantIssue> issues)
        {
            if (assistantPopup == null || issues == null || issues.Count == 0) return;
            asIssues = issues;
            asLines = GMAssistant.Script(League, issues);
            asIndex = 0;
            var a = GMAssistant.ProfileFor(League);
            asInitial.text = a.Initial;
            asName.text = a.Plate;
            asPortrait.color = a.Female ? new Color(0.62f, 0.32f, 0.5f) : new Color(0.24f, 0.38f, 0.62f);
            asTitle.text = $"{GMStoveTurns.Label(GMStoveTurns.Current(League))} · 확인 사항 {issues.Count}건 - {string.Join(" · ", issues.Select(i => i.Title))}";
            assistantPopup.gameObject.SetActive(true);
            assistantPopup.SetAsLastSibling();
            RefreshAssistant();
        }

        private void RefreshAssistant()
        {
            if (asLines.Count == 0) return;
            asIndex = Mathf.Clamp(asIndex, 0, asLines.Count - 1);
            asLine.text = asLines[asIndex];
            asProgress.text = $"대화 {asIndex + 1}/{asLines.Count}";
            bool last = asIndex >= asLines.Count - 1;
            asNext.gameObject.SetActive(!last);
            asSkip.gameObject.SetActive(!last);
            asReturn.gameObject.SetActive(last);
            asDelegate.gameObject.SetActive(last);
            asIgnore.gameObject.SetActive(last);
        }

        public void AssistantNext() { asIndex++; RefreshAssistant(); }

        /// <summary>[대화 건너뛰기] - 마지막 줄(선택지)로.</summary>
        public void AssistantSkip() { asIndex = asLines.Count - 1; RefreshAssistant(); }

        /// <summary>선택 0 = 돌아가기(해당 방) · 1 = AI 위임 후 Turn 진행 · 2 = 무시하고 Turn 진행. Turn이 넘어갔으면 true.</summary>
        public bool AssistantChoose(int choice)
        {
            if (!IsAssistantOpen) return false;
            var issues = asIssues.ToList();
            assistantPopup.gameObject.SetActive(false);
            var a = GMAssistant.ProfileFor(League);
            if (choice == 0)
            {
                var pane = issues.FirstOrDefault()?.Pane;
                OpenPaneByName(pane);
                SetStatus($"{a.Plate}: \"알겠습니다, 단장님. 정리되면 다시 [진행하기]를 눌러 주세요.\"");
                return false;
            }
            string note;
            if (choice == 1)
            {
                var done = GMAssistant.Delegate(League, issues);
                note = $"{a.Plate} 위임 처리 - {string.Join(" / ", done)}";
            }
            else
            {
                GMAssistant.Ignore(League, issues);
                note = $"{a.Plate}: \"단장님 판단을 따르겠습니다.\" (올해 같은 경고는 다시 드리지 않습니다)";
            }
            bool advanced = AdvanceStoveTurnNow();
            SetStatus(note + " · " + StatusText);
            return advanced;
        }

        /// <summary>Pane 이름으로 해당 방을 연다(메인 탭 · 서브 탭 동기화).</summary>
        public void OpenPaneByName(string pane)
        {
            if (string.IsNullOrEmpty(pane)) { GoMainHome(); return; }
            for (int t = 0; t < subTabs.Length; t++)
            {
                int k = subTabs[t].FindIndex(s => s.Pane == pane);
                if (k < 0) continue;
                SelectMainTab(t);
                SelectSubTab(k);
                return;
            }
            GoMainHome();
        }

        /// <summary>[TASK-GM-14] 다음 Turn으로 넘기고 그 방을 연다(조력자 확인 뒤 · 확인할 것이 없을 때).</summary>
        private bool AdvanceStoveTurnNow()
        {
            var league = League;
            if (!GMStoveTurns.Advance(league, out string msg)) { SetStatus(msg); Refresh(); return false; }
            if (GMStoveTurns.IsGating(league)) OpenTurnRoom(GMStoveTurns.Current(league));
            else Refresh();
            SetStatus(msg);
            return true;
        }

        // ================================================================== ② 선수 스카우팅 리포트

        private Image scHeaderBar, scProfileBg;
        private Text scTeamLabel, scOvr, scOvrCaption, scPos, scTier, scTierLabel, scHeadline, scTraits, scAwards, scStats, scReport;
        private readonly Text[] scGrades = new Text[PctRows];
        private Player scPlayer;

        public Player ScoutingPlayer => scPlayer;

        private static readonly Color[] BandColors =
        {
            new Color(0.86f, 0.2f, 0.2f), new Color(1f, 0.56f, 0.38f), new Color(0.62f, 0.63f, 0.66f), new Color(0.48f, 0.64f, 0.95f), new Color(0.22f, 0.38f, 0.9f),
        };

        public static Color BandColor(GMPercentileBand b) => BandColors[(int)b];

        private static string BandLegend() =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(BandColors[0])}>■</color> 최상위  <color=#{ColorUtility.ToHtmlStringRGB(BandColors[1])}>■</color> 상위  " +
            $"<color=#{ColorUtility.ToHtmlStringRGB(BandColors[2])}>■</color> 평균  <color=#{ColorUtility.ToHtmlStringRGB(BandColors[3])}>■</color> 하위  <color=#{ColorUtility.ToHtmlStringRGB(BandColors[4])}>■</color> 최하위";

        /// <summary>구 백분위 팝업(PercentilePopup)을 스카우팅 리포트 지면으로 확장 - PctTitle · PctLabel/PctBar/PctValue · PctClose 이름은 유지한다.</summary>
        private void BuildPercentilePopup()
        {
            pctPopup = CompyaUiKit.Fill(root, "PercentilePopup");
            CompyaUiKit.Paint(pctPopup, new Color(0f, 0f, 0f, 0.78f), true);
            CompyaUiKit.Box(pctPopup, "PctBox", 100, 140, 1820, 1040, new Color(0.96f, 0.96f, 0.95f, 0.99f));
            scHeaderBar = CompyaUiKit.Box(pctPopup, "ScHeaderBar", 100, 140, 1820, 202, new Color(0.07f, 0.3f, 0.63f));
            pctTitle = L(pctPopup, "PctTitle", "", 130, 146, 1380, 198, PanelTitlePt + 5, TextAnchor.MiddleLeft, White);
            scTeamLabel = L(pctPopup, "ScTeamLabel", "", 1390, 146, 1800, 198, BodyPt + 1, TextAnchor.MiddleRight, White);

            var ink = new Color(0.12f, 0.13f, 0.17f);
            var soft = new Color(0.36f, 0.38f, 0.44f);
            scProfileBg = CompyaUiKit.Box(pctPopup, "ScProfile", 130, 218, 400, 488, new Color(0.07f, 0.3f, 0.63f));
            scOvr = L(pctPopup, "ScOvr", "", 130, 226, 400, 318, BigPt + 6, TextAnchor.MiddleCenter, White);
            scOvrCaption = L(pctPopup, "ScOvrCaption", "", 130, 320, 400, 352, SmallPt, TextAnchor.MiddleCenter, White);
            scPos = L(pctPopup, "ScPos", "", 130, 356, 400, 394, BodyPt + 2, TextAnchor.MiddleCenter, White);
            scTier = L(pctPopup, "ScTier", "", 130, 398, 400, 440, BannerPt + 3, TextAnchor.MiddleCenter, Gold);
            scTierLabel = L(pctPopup, "ScTierLabel", "", 130, 442, 400, 482, SmallPt, TextAnchor.MiddleCenter, White);

            pctInfo = L(pctPopup, "PctInfo", "", 420, 218, 1100, 330, BodyPt, TextAnchor.UpperLeft, ink);
            pctInfo.lineSpacing = 1.1f;
            scHeadline = L(pctPopup, "ScHeadline", "", 1110, 218, 1800, 330, BannerPt + 2, TextAnchor.MiddleLeft, new Color(0.07f, 0.3f, 0.63f));
            scTraits = L(pctPopup, "ScTraits", "", 420, 336, 1800, 372, BodyPt, TextAnchor.MiddleLeft, soft);
            scAwards = L(pctPopup, "ScAwards", "", 420, 378, 1800, 484, BodyPt, TextAnchor.UpperLeft, new Color(0.62f, 0.42f, 0.02f));

            L(pctPopup, "ScPctTitle", "백분위 랭킹 (KBO 전체 대비) · 스카우팅 등급 20-80", 130, 500, 1080, 534, PanelTitlePt, TextAnchor.MiddleLeft, ink);
            for (int k = 0; k < PctRows; k++)
            {
                float y0 = 542 + k * 58;
                pctLabels[k] = L(pctPopup, $"PctLabel{k}", "", 130, y0, 330, y0 + 50, BodyPt + 1, TextAnchor.MiddleLeft, ink);
                pctFills[k] = Bar(pctPopup, $"PctBar{k}", 340, y0 + 14, 870, y0 + 36, Gold);
                var bg = pctFills[k].parent.GetComponent<Image>();
                if (bg != null) bg.color = new Color(0f, 0f, 0f, 0.1f);
                pctValues[k] = L(pctPopup, $"PctValue{k}", "", 880, y0, 975, y0 + 50, BodyPt + 1, TextAnchor.MiddleRight, ink);
                scGrades[k] = L(pctPopup, $"ScGrade{k}", "", 985, y0, 1080, y0 + 50, BodyPt + 1, TextAnchor.MiddleCenter, ink);
            }
            L(pctPopup, "ScStatsTitle", "기록", 1110, 500, 1240, 534, PanelTitlePt, TextAnchor.MiddleLeft, ink);
            // [TASK-GM-18] 기록 출처 태그 범례 - 실제 KBO 기록(카드 시즌) / 시뮬레이션 기록(인게임 진행 시즌)
            var tags = L(pctPopup, "ScRecordTags", $"<color={GMScoutingReport.RealTagColor}>■</color> {GMScoutingReport.RealRecordTag}   <color={GMScoutingReport.SimTagColor}>■</color> {GMScoutingReport.SimRecordTag}",
                1250, 500, 1800, 534, SmallPt, TextAnchor.MiddleRight, ink);
            tags.supportRichText = true;
            scStats = L(pctPopup, "ScStats", "", 1110, 540, 1800, 720, BodyPt, TextAnchor.UpperLeft, ink);
            scStats.supportRichText = true; // [TASK-GM-18] 출처 태그 색 견본
            scStats.lineSpacing = 1.1f;
            L(pctPopup, "ScReportTitle", "스카우트 코멘트", 1110, 728, 1800, 762, PanelTitlePt, TextAnchor.MiddleLeft, ink);
            scReport = L(pctPopup, "ScReport", "", 1110, 768, 1800, 944, BodyPt, TextAnchor.UpperLeft, soft);
            scReport.lineSpacing = 1.15f;

            Btn(pctPopup, "ScNegotiate", "계약 협상실에서 협상 ▶", 130, 960, 620, 1024, ButtonOn, ButtonPt).onClick.AddListener(() =>
            {
                var p = scPlayer;
                pctPopup.gameObject.SetActive(false);
                if (p != null && UserTeam != null && UserTeam.ReservePlayers.Contains(p)) OpenNegotiationRoom(p);
            });
            L(pctPopup, "PctLegend", BandLegend(), 630, 960, 1240, 1024, SmallPt, TextAnchor.MiddleCenter, ink);
            Btn(pctPopup, "PctClose", "닫기", 1250, 960, 1800, 1024, new Color(0.3f, 0.32f, 0.38f), ButtonPt).onClick.AddListener(() => pctPopup.gameObject.SetActive(false));
            pctPopup.gameObject.SetActive(false);
        }

        /// <summary>선수 스카우팅 리포트(선수단 · 라인업 행 클릭 · 협상실 [스카우팅 리포트 ▶]).</summary>
        public void OpenPercentiles(Player p)
        {
            if (p?.Template == null || League == null) return;
            scPlayer = p;
            var league = League;
            var rows = GMStoveLeagueMarket.Percentiles(league, p);
            string teamCode = league.TeamCodeOf(p) ?? (UserTeam != null && UserTeam.Futures.Contains(p) ? UserTeam.TeamCode : null);
            var team = teamCode != null ? NameAliasTable.ToTeam(teamCode) : Team.None;
            var primary = team != Team.None ? TeamThemePalette.Primary(team) : new Color(0.25f, 0.27f, 0.33f);
            scHeaderBar.color = primary;
            scProfileBg.color = primary * 0.85f + new Color(0f, 0f, 0f, 0.15f);
            scHeadline.color = new Color(primary.r * 0.8f, primary.g * 0.8f, primary.b * 0.8f);
            var origin = GMRosterTiers.OriginalOf(p.Template);
            var tier = GMStarterDeck.TierOf(p);
            pctTitle.text = $"스카우팅 리포트 · {p.Template.PlayerName}";
            scTeamLabel.text = $"{(teamCode != null ? NameAliasTable.DisplayTeamName(teamCode) : "FA 시장")} · {origin.SeasonYear} 시즌 카드";
            scOvr.text = p.BaseOverall.ToString();
            scOvrCaption.text = $"OVR · 잠재력 {p.Potential}";
            scPos.text = $"{GMFrontOffice.PositionLabel(p.Position)} · {p.Age}세";
            scTier.text = $"{GMStarterDeck.TierName(tier)}급 {new string('★', 5 - (int)tier)}";
            scTierLabel.text = GMStarterDeck.TierLabel(tier).Substring(GMStarterDeck.TierLabel(tier).IndexOf('·') + 1).Trim();
            pctInfo.text = $"연봉 {GMDiagnosticFormat.Short(p.Salary)} · 잔여 계약 {p.ContractYears}년{(p.ContractYears == 0 ? "(만료)" : "")}\n" +
                           $"ABS {p.ABSZoneSkill}({p.AbsRoleLabel}) · {(p.IsPitcher ? "투수" : "타자")} · {(p.InjuryRemainingDays > 0 ? $"부상 {p.InjuryRemainingDays}일" : "건강")}\n" +
                           $"KBO 전체 {(p.IsPitcher ? "투수" : "타자")} 대비 백분위 · 등급 50 = 리그 평균";
            scHeadline.text = GMScoutingReport.Headline(league, p, rows);
            scTraits.text = GMScoutingReport.Traits(p);
            scAwards.text = "수상 · " + GMScoutingReport.AwardsLine(p, 8);
            for (int k = 0; k < PctRows; k++)
            {
                bool used = k < rows.Count;
                pctLabels[k].text = used ? rows[k].Label : "";
                pctValues[k].text = used ? $"{rows[k].Percentile}%" : "";
                scGrades[k].text = used ? GMScoutingReport.ScoutGrade(rows[k].Percentile).ToString() : "";
                SetFill(pctFills[k], used ? rows[k].Percentile / 100f : 0f);
                var color = used ? BandColor(GMScoutingReport.BandOf(rows[k].Percentile)) : Muted;
                var img = pctFills[k].GetComponent<Image>();
                if (img != null) img.color = color;
                pctValues[k].color = used ? new Color(color.r * 0.85f, color.g * 0.85f, color.b * 0.85f) : Muted;
            }
            scStats.text = string.Join("\n", GMScoutingReport.StatLines(league, p));
            scReport.text = GMScoutingReport.Narrative(league, p, rows);
            pctPopup.gameObject.SetActive(true);
            pctPopup.SetAsLastSibling();
        }

        // ================================================================== ③ 응원단 포스터(허브)

        private RawImage cePosterImage;
        private readonly Button[] ceSlots = new Button[CheerSquad.SlotCount];
        private readonly RawImage[] ceSlotPhotos = new RawImage[CheerSquad.SlotCount];
        private readonly Text[] ceEntryLines = new Text[CheerSquad.SlotCount];
        private readonly Text[] ceTotalValues = new Text[4];
        private readonly RectTransform[] ceTotalFills = new RectTransform[4];
        private const float CePosterX = 24, CePosterY = 294, CePosterH = 736;

        public RawImage CheerPosterSlotPhoto(int i) => i >= 0 && i < ceSlotPhotos.Length ? ceSlotPhotos[i] : null;

        private void BuildCheerPane()
        {
            var pane = PaneRoot(PaneCheer);
            var pink = new Color(1f, 0.6f, 0.82f);
            var font = regularFont != null ? regularFont : TextTidy.BodyFont;
            var poster = Panel(pane, "CePosterPanel", 12, 248, 1000, 1036);
            L(poster, "CePosterTitle", "단상 라인업 포스터 - 사진을 누르면 상세 · 엔트리 관리", 24, 252, 990, 288, PanelTitlePt, TextAnchor.MiddleLeft, pink);
            var img = CompyaUiKit.Place(poster, "CePosterImage", CePosterX, CePosterY, CePosterX + GMCheerPoster.WidthFor(CePosterH), CePosterY + CePosterH);
            cePosterImage = img.gameObject.AddComponent<RawImage>();
            cePosterImage.raycastTarget = false;
            for (int i = 0; i < ceSlots.Length; i++)
            {
                int index = i;
                var r = GMCheerPoster.SlotRect(i, CePosterX, CePosterY, CePosterH);
                ceSlots[i] = Btn(poster, $"CeSlot{i}", "", r.x0, r.y0, r.x1, r.y1, new Color(1f, 1f, 1f, 0.001f), BodyPt);
                ceSlotPhotos[i] = GMCheerPoster.Decorate(ceSlots[i], font, BodyPt, CellPt, true, pink, i);
                ceSlots[i].onClick.AddListener(() => OpenCheerEntryAt(index));
            }
            GMCheerPoster.ApplyDrawOrder(ceSlots); // [TASK-GM-17]
            float lx = CePosterX + GMCheerPoster.WidthFor(CePosterH) + 12; // ≈ 625
            for (int i = 0; i < ceEntryLines.Length; i++)
            {
                float y0 = 296 + i * 56;
                ceEntryLines[i] = L(poster, $"CeEntryLine{i}", "", lx, y0, 990, y0 + 52, CellPt, TextAnchor.MiddleLeft, White);
            }
            ceEntry = L(poster, "CeEntry", "", lx, 636, 990, 760, CellPt, TextAnchor.UpperLeft, Muted);
            Btn(poster, "CeOpenEntry", "15인 풀 · 엔트리 · 전담 응원 관리", lx, 770, 990, 836, new Color(0.62f, 0.22f, 0.48f), BodyPt).onClick.AddListener(OpenCheerEntry);
            Btn(poster, "CeAutoArrange", "최적 컨디션 자동 편성", lx, 846, 990, 906, ButtonIdle, BodyPt).onClick.AddListener(() =>
            {
                if (UserTeam == null) return;
                var e = GMCheerleaderRoster.AutoArrange(UserTeam, UserTeam.CheerEntrySize);
                SetStatus($"응원단 최적 컨디션 {e.Count}인 자동 편성 완료");
                Refresh();
            });

            var syn = Panel(pane, "CeSynergyPanel", 1012, 248, 1908, 1036);
            L(syn, "CeSynergyTitle", "엔트리 시너지 · 효과", 1024, 252, 1896, 288, PanelTitlePt, TextAnchor.MiddleLeft, pink);
            ceEffects = L(syn, "CeEffects", "", 1024, 294, 1896, 560, BodyPt, TextAnchor.UpperLeft, White);
            ceEffects.lineSpacing = 1.15f;
            string[] colors = { "#FFC74D", "#FF7373", "#66BFFF", "#80EB99" };
            for (int k = 0; k < 4; k++)
            {
                float y0 = 568 + k * 40;
                L(syn, $"CeTotalLabel{k}", GMCheerleaderStats.StatLabels[k] + " 합계", 1024, y0, 1220, y0 + 36, CellPt, TextAnchor.MiddleLeft, Muted);
                ColorUtility.TryParseHtmlString(colors[k], out var c);
                ceTotalFills[k] = Bar(syn, $"CeTotalBar{k}", 1226, y0 + 10, 1770, y0 + 26, c);
                ceTotalValues[k] = L(syn, $"CeTotalValue{k}", "", 1776, y0, 1896, y0 + 36, CellPt, TextAnchor.MiddleRight, White);
            }
            L(syn, "CePoolTitle", "구단 응원단 풀", 1024, 734, 1896, 768, PanelTitlePt - 1, TextAnchor.MiddleLeft, pink);
            cePool = L(syn, "CePool", "", 1024, 772, 1896, 900, CellPt, TextAnchor.UpperLeft, Muted);
            cePool.lineSpacing = 1.1f;
            // [TASK-GM-17] 클래식 로비 영입 · 보유 화면 진입점 제거 - 구단 15인 풀은 실제 응원단이고, 성장(강화)은 엔트리 화면 [응원단 육성]으로 옮겼다
            L(syn, "CeNote", "응원단 육성(강화)은 15인 풀 · 엔트리 화면에서 - 마케팅 예산(관중 · 굿즈 수익)을 써서 4대 스탯을 올립니다.", 1024, 910, 1896, 970, CellPt, TextAnchor.MiddleLeft, Muted);
        }

        private void RefreshCheer()
        {
            var team = UserTeam;
            if (team == null) return;
            var entry = GMCheerleaderRoster.Entry(team);
            var template = GMCheerPortraits.LineupTemplate(team.Team);
            cePosterImage.texture = template;
            cePosterImage.color = template != null ? Color.white : TeamThemePalette.Primary(team.Team) * 0.6f + new Color(0f, 0f, 0f, 0.4f);
            for (int i = 0; i < ceSlots.Length; i++)
            {
                var c = i < entry.Count ? entry[i] : null;
                GMCheerPoster.Fill(ceSlots[i], ceSlotPhotos[i], c, i, CheerSquad.RoleName((CheerRole)i), c != null ? $"CHEER {GMCheerleaderStats.Cheer(c)}" : "");
                ceSlots[i].targetGraphic.color = c == null && template == null ? new Color(1f, 1f, 1f, 0.12f) : new Color(1f, 1f, 1f, 0.001f);
                ceEntryLines[i].text = c != null
                    ? $"{i + 1}.{CheerSquad.RoleName((CheerRole)i)} · {c.DisplayName}\nCHEER {GMCheerleaderStats.Cheer(c)} · 체력 {GMCheerleaderStats.Stamina(c)}{(GMCheerleaderStats.IsTired(c) ? "(효율 50%)" : "")}"
                    : $"{i + 1}.{CheerSquad.RoleName((CheerRole)i)} · 빈 슬롯";
                ceEntryLines[i].color = c != null ? White : Muted;
            }
            ceEntry.text = $"{GMCheerleaderRules.Summary(entry.Count, team.CheerleaderPool.Count)}\n자동 로테이션 {(team.CheerAutoRotate ? "켬(체력 30 미만 자동 교체)" : "끔(수동)")} · 팬 지지율 {team.FanSupport}";
            ceEffects.text = string.Join("\n", GMCheerSynergy.Lines(team, entry).Where(x => !string.IsNullOrEmpty(x)));
            for (int k = 0; k < 4; k++)
            {
                var (total, _) = GMCheerSynergy.StatTotal(entry, k);
                ceTotalValues[k].text = total.ToString();
                SetFill(ceTotalFills[k], total / (float)(GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MAX * 100));
            }
            var bench = team.CheerleaderPool.Where(c => !GMCheerleaderRoster.IsInEntry(team, c)).Select(c => $"{c.DisplayName}({c.Grade.Display()})").ToList();
            cePool.text = $"엔트리 {entry.Count}명 · 벤치 {bench.Count}명 · 구단 풀 {team.CheerleaderPool.Count}/{GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX}명\n" + string.Join(" · ", bench);
        }

        /// <summary>포스터 사진 → 치어리더 관리 화면(해당 인원 선택 · 사진 · 4대 스탯 상세).</summary>
        public void OpenCheerEntryAt(int slot)
        {
            OpenCheerEntry();
            var view = CheerView != null ? CheerView : FindAnyObjectByType<GMCheerleaderEntryUIController>(FindObjectsInactive.Include);
            var entry = GMCheerleaderRoster.Entry(UserTeam);
            if (view != null && view.gameObject.activeSelf && slot >= 0 && slot < entry.Count) view.Select(entry[slot]);
        }
    }
}
