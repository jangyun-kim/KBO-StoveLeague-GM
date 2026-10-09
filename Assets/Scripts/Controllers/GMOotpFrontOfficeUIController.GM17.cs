using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
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
    /// [TASK-GM-17] 프런트 직원 리포트 · 시장 연계 · 유대 심화 화면.
    ///   ① 프런트 직원 리포트(GMStaffReportView 모달) - 데이터분석팀장 · 재무팀장 · 선수관리팀장 3탭 + 운영팀장(조력자) 정리 · 탭별 보고 신뢰도(%).
    ///   ② 계약 협상실 제시액 슬라이더(요구액 70~120%) - 갭이 진행 가능성의 뼈대, 카드는 가산 · 삭감 시 조력자 유대 경고 · 협상 기회 3회.
    ///   ③ FA 입찰 경쟁(Bidding War) - 경쟁 구단 역제안 · [추가 베팅] · [포기] · 실효 제시 대비 AI 한도 게이지.
    ///   ④ 트레이드 상대 단장 수락 게이지(100% = 타결점 눈금) · 1:N 역제안이 게이지를 채워 가는 단계 표시.
    /// </summary>
    public partial class GMOotpFrontOfficeUIController
    {
        public const string StaffReportName = "StaffReportPopup";
        public const int StaffTabs = 3, SrRows = 8;
        private static readonly string[] StaffTabLabels = { "데이터분석팀장", "재무팀장", "선수관리팀장" };
        private static readonly Color RiskSafe = new Color(0.36f, 0.86f, 0.36f), RiskCaution = new Color(1f, 0.62f, 0.2f), RiskDanger = new Color(1f, 0.38f, 0.32f);

        public static Color RiskColor(GMRiskLevel r) => r == GMRiskLevel.Danger ? RiskDanger : r == GMRiskLevel.Caution ? RiskCaution : RiskSafe;

        /// <summary>수락 게이지 막대 - 비율 0~1.25를 막대 전체로, 100%(타결점) = 막대 80% 눈금.</summary>
        public static float AcceptanceFill(float ratio) => Mathf.Clamp01(ratio / 1.25f);
        public const float AcceptanceTick = 0.8f;

        // ================================================================== ① 프런트 직원 리포트

        private RectTransform staffPopup;
        private readonly RectTransform[] srGroups = new RectTransform[StaffTabs];
        private readonly Button[] srTabs = new Button[StaffTabs];
        private Text srAssistant, srDataHead, srMarketTitle, srCapLabel, srCapDetail, srTax, srTrust, srRelHead;
        private readonly Text[] srReliability = new Text[StaffTabs];
        private readonly Text[] srPosLabels = new Text[4], srPosValues = new Text[4], srMarket = new Text[3];
        private readonly RectTransform[] srPosFills = new RectTransform[4];
        private readonly Text[] srBest = new Text[5], srWorst = new Text[5], srRisk = new Text[SrRows], srBond = new Text[SrRows];
        private RectTransform srCapFill, srTrustFill;
        private int staffTab;
        private GMStaffReportBundle staffReport;

        public bool IsStaffReportOpen => staffPopup != null && staffPopup.gameObject.activeSelf;
        public int StaffReportTab => staffTab;
        public GMStaffReportBundle StaffReport => staffReport;

        private void BuildStaffReportPopup()
        {
            staffPopup = CompyaUiKit.Fill(root, StaffReportName);
            CompyaUiKit.Paint(staffPopup, new Color(0f, 0f, 0f, 0.74f), true);
            CompyaUiKit.Box(staffPopup, "SrBox", 160, 110, 1760, 1010, new Color(0.12f, 0.13f, 0.16f, 0.99f));
            CompyaUiKit.Box(staffPopup, "SrAccent", 160, 110, 1760, 116, Gold);
            L(staffPopup, "SrTitle", "프런트 직원 리포트 (FRONT OFFICE STAFF REPORT)", 196, 126, 1500, 172, PanelTitlePt + 5, TextAnchor.MiddleLeft, Gold);
            Btn(staffPopup, "SrClose", "닫기", 1560, 126, 1724, 172, ButtonIdle, ButtonPt).onClick.AddListener(CloseStaffReport);
            srAssistant = L(staffPopup, "SrAssistant", "", 196, 178, 1724, 214, BodyPt, TextAnchor.MiddleLeft, White);
            for (int t = 0; t < StaffTabs; t++)
            {
                int tab = t;
                float x0 = 196 + t * 512;
                srTabs[t] = Btn(staffPopup, $"SrTab{t}", StaffTabLabels[t], x0, 222, x0 + 500, 266, ButtonIdle, TabPt + 1);
                srTabs[t].onClick.AddListener(() => SelectStaffTab(tab));
            }
            var ink = White;
            // 데이터분석팀장
            var g0 = srGroups[0] = CompyaUiKit.Fill(staffPopup, "SrDataGroup");
            srDataHead = L(g0, "SrDataHead", "", 196, 282, 1724, 318, BodyPt + 1, TextAnchor.MiddleLeft, Gold);
            L(g0, "SrPosTitle", "취약 포지션 - 10구단 같은 포지션 주전 대비 백분위", 196, 326, 1724, 358, BodyPt, TextAnchor.MiddleLeft, Muted);
            for (int k = 0; k < 4; k++)
            {
                float y0 = 364 + k * 50;
                srPosLabels[k] = L(g0, $"SrPosLabel{k}", "", 196, y0, 520, y0 + 44, BodyPt, TextAnchor.MiddleLeft, ink);
                srPosFills[k] = Bar(g0, $"SrPosBar{k}", 530, y0 + 12, 1180, y0 + 32, BarBlue);
                srPosValues[k] = L(g0, $"SrPosValue{k}", "", 1190, y0, 1724, y0 + 44, BodyPt, TextAnchor.MiddleLeft, ink);
            }
            srMarketTitle = L(g0, "SrMarketTitle", "시장 대체 후보 Top 3 (FA 시장 · 트레이드)", 196, 574, 1724, 608, BodyPt + 1, TextAnchor.MiddleLeft, Gold);
            for (int k = 0; k < 3; k++)
            {
                float y0 = 614 + k * 50;
                srMarket[k] = L(g0, $"SrMarket{k}", "", 196, y0, 1724, y0 + 44, BodyPt, TextAnchor.MiddleLeft, ink);
            }
            srReliability[0] = L(g0, "SrReliability0", "", 196, 950, 1724, 990, BodyPt, TextAnchor.MiddleLeft, Muted);
            // 재무팀장
            var g1 = srGroups[1] = CompyaUiKit.Fill(staffPopup, "SrFinanceGroup");
            srCapLabel = L(g1, "SrCapLabel", "", 196, 282, 1724, 318, BodyPt + 1, TextAnchor.MiddleLeft, Gold);
            srCapFill = Bar(g1, "SrCapBar", 196, 326, 1724, 360, RiskSafe);
            CompyaUiKit.Box(g1, "SrCapTick", 196 + (1724 - 196) * (GMStaffReport.CautionUsage / 1.2f) - 2, 320, 196 + (1724 - 196) * (GMStaffReport.CautionUsage / 1.2f) + 2, 366, White);
            CompyaUiKit.Box(g1, "SrCapFullTick", 196 + (1724 - 196) / 1.2f - 2, 320, 196 + (1724 - 196) / 1.2f + 2, 366, RiskDanger);
            srCapDetail = L(g1, "SrCapDetail", "", 196, 370, 1724, 404, BodyPt, TextAnchor.MiddleLeft, ink);
            srTax = L(g1, "SrTax", "", 196, 410, 1724, 446, BodyPt + 1, TextAnchor.MiddleLeft, RiskSafe);
            L(g1, "SrBestTitle", "연봉 효율 최고 (가성비)", 196, 462, 950, 496, BodyPt + 1, TextAnchor.MiddleLeft, Gold);
            L(g1, "SrWorstTitle", "연봉 효율 최악 (고액 저효율)", 970, 462, 1724, 496, BodyPt + 1, TextAnchor.MiddleLeft, Gold);
            for (int k = 0; k < 5; k++)
            {
                float y0 = 502 + k * 46;
                srBest[k] = L(g1, $"SrBest{k}", "", 196, y0, 950, y0 + 42, BodyPt, TextAnchor.MiddleLeft, ink);
                srWorst[k] = L(g1, $"SrWorst{k}", "", 970, y0, 1724, y0 + 42, BodyPt, TextAnchor.MiddleLeft, ink);
            }
            srReliability[1] = L(g1, "SrReliability1", "", 196, 950, 1724, 990, BodyPt, TextAnchor.MiddleLeft, Muted);
            // 선수관리팀장
            var g2 = srGroups[2] = CompyaUiKit.Fill(staffPopup, "SrRelationsGroup");
            srRelHead = L(g2, "SrRelHead", "", 196, 282, 1724, 318, BodyPt + 1, TextAnchor.MiddleLeft, Gold);
            srTrustFill = Bar(g2, "SrTrustBar", 196, 326, 1724, 352, BarGreen);
            L(g2, "SrRiskTitle", "충성도 40 미만 위험군", 196, 364, 950, 398, BodyPt + 1, TextAnchor.MiddleLeft, Gold);
            L(g2, "SrBondTitle", "핵심 유대 - 파괴 시 예상 선수단 신뢰도 하락", 970, 364, 1724, 398, BodyPt + 1, TextAnchor.MiddleLeft, Gold);
            for (int k = 0; k < SrRows; k++)
            {
                float y0 = 404 + k * 44;
                srRisk[k] = L(g2, $"SrRisk{k}", "", 196, y0, 950, y0 + 40, BodyPt, TextAnchor.MiddleLeft, ink);
                srBond[k] = L(g2, $"SrBond{k}", "", 970, y0, 1724, y0 + 40, BodyPt, TextAnchor.MiddleLeft, ink);
            }
            srReliability[2] = L(g2, "SrReliability2", "", 196, 950, 1724, 990, BodyPt, TextAnchor.MiddleLeft, Muted);
            staffPopup.gameObject.SetActive(false);
        }

        /// <summary>[프런트 직원 리포트] - tab 0 데이터분석 · 1 재무 · 2 선수관리.</summary>
        public void OpenStaffReport(int tab = 0)
        {
            CloseMenus();
            if (staffPopup == null || League == null || UserTeam == null) { SetStatus("단장 모드 리그가 없습니다."); return; }
            staffReport = GMStaffReport.Build(League);
            staffPopup.gameObject.SetActive(true);
            staffPopup.SetAsLastSibling();
            SelectStaffTab(tab);
        }

        public void CloseStaffReport()
        {
            if (staffPopup != null) staffPopup.gameObject.SetActive(false);
        }

        public void SelectStaffTab(int tab)
        {
            staffTab = Mathf.Clamp(tab, 0, StaffTabs - 1);
            for (int t = 0; t < StaffTabs; t++)
            {
                srGroups[t].gameObject.SetActive(t == staffTab);
                srTabs[t].targetGraphic.color = t == staffTab ? new Color(Gold.r, Gold.g, Gold.b, 0.4f) : ButtonIdle;
            }
            RefreshStaffReport();
        }

        private void RefreshStaffReport()
        {
            var b = staffReport;
            if (b == null) return;
            srAssistant.text = b.AssistantLine;
            // 데이터분석
            srDataHead.text = b.DataHeadline;
            for (int k = 0; k < 4; k++)
            {
                var w = k < b.WeakPositions.Count ? b.WeakPositions[k] : null;
                srPosLabels[k].text = w == null ? "" : $"{w.PositionLabel} · {w.PlayerName}";
                srPosValues[k].text = w == null ? "" : $"백분위 {w.Percentile}% · 생산성 {w.Production:0} / 리그 {w.LeagueProduction:0} · WAR {w.War:0.0}";
                SetFill(srPosFills[k], w == null ? 0f : w.Percentile / 100f);
                var img = srPosFills[k].GetComponent<Image>();
                if (img != null && w != null) img.color = BandColor(GMScoutingReport.BandOf(w.Percentile));
            }
            for (int k = 0; k < 3; k++) srMarket[k].text = k < b.Candidates.Count ? $"{k + 1}. [{b.Candidates[k].Source}] {b.Candidates[k].Line}" : k == 0 ? "현재 주전보다 나은 대체 후보가 시장에 없습니다." : "";
            srReliability[0].text = $"보고 신뢰도 {b.DataReliability}% - 타석 · 이닝 표본 기준(직전 시즌 결산 · 진행 중 시즌)";
            // 재무
            srCapLabel.text = $"페이롤 / 샐러리캡 게이지 - {b.CapUsage * 100:0}% (흰 눈금 90% · 경고 눈금 100%)";
            SetFill(srCapFill, Mathf.Clamp01(b.CapUsage / 1.2f));
            var capImg = srCapFill.GetComponent<Image>();
            if (capImg != null) capImg.color = RiskColor(b.TaxRisk);
            srCapDetail.text = b.FinanceHeadline;
            srTax.text = b.TaxLine;
            srTax.color = RiskColor(b.TaxRisk);
            for (int k = 0; k < 5; k++)
            {
                srBest[k].text = k < b.BestValue.Count ? $"{k + 1}. {b.BestValue[k].Line}" : "";
                srWorst[k].text = k < b.WorstValue.Count ? $"{k + 1}. {b.WorstValue[k].Line}" : "";
            }
            srReliability[1].text = $"보고 신뢰도 {b.FinanceReliability}% - 구단 장부(페이롤 · 예산) 기준";
            // 선수관리
            srRelHead.text = b.RelationsHeadline;
            SetFill(srTrustFill, b.Trust / 100f);
            var trustImg = srTrustFill.GetComponent<Image>();
            if (trustImg != null) trustImg.color = b.Trust >= 60 ? RiskSafe : b.Trust >= 40 ? RiskCaution : RiskDanger;
            for (int k = 0; k < SrRows; k++)
            {
                var r = k < b.LoyaltyRisks.Count ? b.LoyaltyRisks[k] : null;
                srRisk[k].text = r == null ? (k == 0 ? "충성도 40 미만 선수가 없습니다." : "") : $"{r.Player.Template.PlayerName} · 충성도 {r.Loyalty} · {r.Reason}";
                srRisk[k].color = r == null ? Muted : r.Loyalty < 25 ? RiskDanger : RiskCaution;
                var bond = k < b.Bonds.Count ? b.Bonds[k] : null;
                srBond[k].text = bond == null ? (k == 0 ? "규칙 기반 유대가 없습니다." : "") : bond.Line;
                srBond[k].color = bond == null ? Muted : bond.TrustDrop >= 5 ? RiskCaution : White;
            }
            srReliability[2].text = $"보고 신뢰도 {b.RelationsReliability}% - 진행 경기 · 결산 기록이 쌓일수록 올라갑니다";
        }

        // ================================================================== ② 계약 협상실 - 제시액 · 조력자 경고

        private Slider negOfferSlider;
        private Text negWarning;

        public int NegotiationOffer => negSession?.Offer ?? 0;
        public string NegotiationWarning => negWarning != null ? negWarning.text : "";

        /// <summary>제시 연봉을 직접 넣는다(테스트 · 단축). 슬라이더 · 카드 예측이 함께 바뀐다.</summary>
        public void SetNegotiationOffer(int salary)
        {
            if (negSession == null || League == null || UserTeam == null) return;
            GMNegotiationRoom.SetOffer(League, UserTeam, negSession, salary);
            if (negOfferSlider != null) negOfferSlider.SetValueWithoutNotify(OfferSliderValue(negSession));
            RefreshNegotiation();
        }

        private static float OfferSliderValue(GMNegotiationSession s) =>
            s == null || s.Demand <= 0 ? 0.5f : Mathf.InverseLerp(GMNegotiationRoom.MinOfferRatio, GMNegotiationRoom.MaxOfferRatio, s.Offer / (float)s.Demand);

        private void OnNegotiationSlider(float t)
        {
            if (negSession == null || League == null || UserTeam == null) return;
            int salary = (int)Math.Round(negSession.Demand * Mathf.Lerp(GMNegotiationRoom.MinOfferRatio, GMNegotiationRoom.MaxOfferRatio, t));
            GMNegotiationRoom.SetOffer(League, UserTeam, negSession, salary);
            RefreshNegotiation();
        }

        /// <summary>조력자 사전 경고 - 삭감이면 유대 동료 반발, 마지막 기회면 FA 이탈 경고.</summary>
        private string NegotiationWarningText(GMNegotiationSession s)
        {
            if (s?.Player == null) return "";
            string cut = GMSalaryChain.PreviewCut(League, UserTeam, s.Player, s.CurrentSalary, s.Offer);
            if (cut != "") return cut;
            if (s.ChancesLeft == 1)
                return $"{GMAssistant.ProfileFor(League).Plate}: \"단장님, 마지막 협상 기회입니다. 이번에도 불발되면 {s.Player.Template.PlayerName} 선수는 FA 시장으로 나갑니다.\"";
            return "";
        }

        // ================================================================== ③ FA 입찰 경쟁

        private GMFaBidSession faBid;
        private Text faBidStatus;
        private RectTransform faBidFill;
        private Button faRaise, faWithdraw;

        public GMFaBidSession FABidSession => faBid;

        private void BuildFABidControls(RectTransform pane)
        {
            faBidStatus = L(pane, "FABidStatus", "", 1314, 806, 1896, 868, CellPt, TextAnchor.UpperLeft, White);
            faBidFill = Bar(pane, "FABidBar", 1314, 872, 1896, 892, BarBlue);
            AddAcceptanceTick(faBidFill); // 경쟁 구단 입찰 = 100% 눈금
            faRaise = Btn(pane, "FARaise", "추가 베팅 (AI 입찰 +5%)", 1314, 898, 1600, 946, new Color(0.62f, 0.45f, 0.12f), BodyPt);
            faRaise.onClick.AddListener(() => RaiseFABid());
            faWithdraw = Btn(pane, "FAWithdraw", "입찰 포기", 1610, 898, 1896, 946, new Color(0.45f, 0.25f, 0.22f), BodyPt);
            faWithdraw.onClick.AddListener(() => WithdrawFABid());
        }

        private void RefreshFABid()
        {
            if (faBidStatus == null) return;
            bool live = faBid != null && faBid.Player == faSelected && faSelected != null;
            faRaise.interactable = faWithdraw.interactable = live && faBid.State == GMFaBidState.CounterBid;
            if (faRaise.interactable && GMLeagueRules.FreeAgencyLocked(League, UserTeam, out _)) faRaise.interactable = false; // [TASK-GM-18] 하드 락 = 추가 베팅 불가
            if (!live)
            {
                faBidStatus.text = faSelected == null ? "" : "입찰 경쟁 - [계약 제시]로 첫 입찰을 넣으면 관심 구단의 역제안이 표시됩니다.";
                SetFill(faBidFill, 0f);
                return;
            }
            string rival = faBid.HasRival ? NameAliasTable.DisplayTeamName(faBid.RivalCode) : "경쟁 없음";
            faBidStatus.text = $"입찰 경쟁 {faBid.Round}R · {rival}{(faBid.HasRival ? $"({faBid.RivalNeed}) 입찰 {GMDiagnosticFormat.Short(faBid.RivalBid)}" : "")} · 우리 실효 제시 {GMDiagnosticFormat.Short(faBid.UserEffective)}\n" +
                               string.Join(" → ", faBid.Log.Skip(Math.Max(0, faBid.Log.Count - 3)));
            float ratio = faBid.HasRival && faBid.RivalBid > 0 ? faBid.UserEffective / (float)faBid.RivalBid : 1f;
            SetFill(faBidFill, AcceptanceFill(ratio));
            var img = faBidFill.GetComponent<Image>();
            if (img != null) img.color = faBid.State == GMFaBidState.Won ? BarGreen : ratio >= 1f ? new Color(0.85f, 0.65f, 0.15f) : BarRed;
        }

        /// <summary>[계약 제시] = 입찰 1라운드. 영입 확정이면 GMNegotiationResult.Success.</summary>
        private GMNegotiationResult BidFA(int salary)
        {
            var player = faSelected;
            if (faBid == null || faBid.Player != player || faBid.State == GMFaBidState.Withdrawn || faBid.State == GMFaBidState.Lost) faBid = GMMarketBidding.Open(League, UserTeam, player);
            GMMarketBidding.Bid(League, UserTeam, faBid, salary, faYearsValue, faRoleOn);
            var r = faBid.State == GMFaBidState.Won && faBid.Signing != null ? faBid.Signing : new GMNegotiationResult
            {
                Message = faBid.State == GMFaBidState.CounterBid
                    ? $"{player.Template.PlayerName} 입찰 경쟁 - 경쟁 구단 {NameAliasTable.DisplayTeamName(faBid.RivalCode)}이(가) {GMDiagnosticFormat.Short(faBid.RivalBid)}로 역제안했습니다. [추가 베팅] 또는 [입찰 포기]를 고르십시오."
                    : faBid.Log.LastOrDefault() ?? "",
                RivalBid = faBid.RivalBid,
            };
            faMessage.text = r.Message;
            SetStatus(r.Message);
            if (r.Success) { faSelected = null; faBid = null; }
            Refresh();
            if (r.Success) PlayAudioEvent(ContractEventFor(player, false)); // [TASK-GM-12] FA 계약 = 환희 · S급 = 엘도라도
            return r;
        }

        /// <summary>[추가 베팅] - AI 현재 입찰을 실효 기준 +5% 넘는 금액으로 다시 입찰.</summary>
        public GMNegotiationResult RaiseFABid()
        {
            if (faBid == null || faSelected == null || faBid.State != GMFaBidState.CounterBid) return null;
            int next = GMMarketBidding.SuggestedRaise(League, UserTeam, faBid);
            faSlider.SetValueWithoutNotify(SliderFor(FADemand, next));
            return BidFA(next);
        }

        /// <summary>[입찰 포기] - 경쟁 구단이 현재 입찰가로 영입한다.</summary>
        public string WithdrawFABid()
        {
            if (faBid == null || faSelected == null) return null;
            string code = GMMarketBidding.Withdraw(League, faBid);
            faMessage.text = faBid.Log.LastOrDefault() ?? "";
            SetStatus(faMessage.text);
            faSelected = null;
            faBid = null;
            Refresh();
            return code;
        }

        // ================================================================== ④ 트레이드 수락 게이지 단계

        /// <summary>역제안 패키지를 한 명씩 얹을 때 수락 게이지가 타결점(100%)을 찾아가는 단계(0% → … → 최종).</summary>
        public List<float> CounterOfferSteps(GMTradeCounterOffer offer)
        {
            var steps = new List<float>();
            if (offer == null || !offer.Valid || League == null) return steps;
            var partner = League.Teams.TryGetValue(offer.PartnerCode, out var t) ? t : null;
            var theirs = new List<Player> { offer.Target };
            var mine = new List<Player>();
            steps.Add(GMStoveLeagueMarket.Evaluate(League, UserTeam, mine, partner, theirs, 0).Ratio);
            foreach (var p in offer.Requested)
            {
                mine.Add(p);
                steps.Add(GMStoveLeagueMarket.Evaluate(League, UserTeam, mine, partner, theirs, 0).Ratio);
            }
            if (offer.CashSubsidy > 0) steps.Add(GMStoveLeagueMarket.Evaluate(League, UserTeam, mine, partner, theirs, offer.CashSubsidy).Ratio);
            return steps;
        }

        private static void AddAcceptanceTick(RectTransform fill)
        {
            if (fill == null || fill.parent == null || fill.parent.Find("AcceptTick") != null) return;
            var tick = CompyaUiKit.Norm(fill.parent, "AcceptTick", AcceptanceTick - 0.003f, -0.25f, AcceptanceTick + 0.003f, 1.25f);
            CompyaUiKit.Paint(tick, White);
        }

        // ================================================================== 정규시즌 종료 → 포스트시즌 트리

        /// <summary>
        /// [TASK-GM-17] 대시보드 [포스트시즌 트리로 ▶] - 허브를 다시 띄우고 [진행하기] 흐름으로 보낸다(시즌 결산실 1회 → 포스트시즌 트리, 1경기씩 · 내 구단 경기 3단계 지휘).
        /// 정규시즌이 끝나지 않았으면 false.
        /// </summary>
        public bool ContinueFromDashboard(GMLiveSeasonSimulator sim)
        {
            if (sim == null || !sim.IsSeasonComplete) return false;
            if (root == null) Build();
            if (simulator != sim) { simulator = sim; ResetSelections(); }
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            ApplyTheme();
            return Continue();
        }

        // ================================================================== 재정 현황(툴바)

        /// <summary>툴바 둘째 줄 - 운영 예산 · 페이롤/캡 · FA 가용 · 경쟁균형세 위험도(색).</summary>
        private void RefreshFinanceStrip()
        {
            var team = UserTeam;
            if (toolbarInfo2 == null || team == null || League == null) return;
            var budget = GMFrontOffice.Budget(League, team);
            float usage = team.PayrollCap > 0 ? team.Payroll / (float)team.PayrollCap : 0f;
            var risk = GMStaffReport.RiskOf(usage);
            toolbarInfo2.text = $"운영 예산 {GMDiagnosticFormat.Short(team.Budget)} · 페이롤 {GMDiagnosticFormat.Short(team.Payroll)}/{GMDiagnosticFormat.Short(team.PayrollCap)}({usage * 100:0}%) · FA 가용 {GMDiagnosticFormat.Short(budget.MoneyForFA)} · 균형세 {GMStaffReport.RiskLabel(risk)}";
            toolbarInfo2.color = risk == GMRiskLevel.Safe ? Gold : RiskColor(risk);
        }
    }
}
