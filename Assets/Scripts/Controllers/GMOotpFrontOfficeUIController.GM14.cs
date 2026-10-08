using System.Linq;
using KBOManager.Services;
using KBOManager.Simulation;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-GM-14] 스토브리그 8 Turn 진행 통제(Phase Gating) · [진행하기] 라우팅 정상화.
    ///   - 해금 전 방(시즌 결산실 T1 · 선수단 회의실/보호 명단 T2 · 계약 협상실/연봉 T4 · FA/트레이드 T5 · 드래프트 T7)은 잠금 화면을 덮고 실행 메서드도 막는다.
    ///   - [진행하기]: 경기 결과 · 전력 분석 · 팝업 · 대시보드를 닫고 메인 홈(6분할)으로 이동 · 리렌더링한다. 메인 홈에서 다시 누르면
    ///     Turn 통제 중 = 이번 Turn 대표 방 열기 → (확인 후) 다음 Turn, 8 Turn 완료 후 = 개막전 전력 분석(기존 3단계 플로우).
    ///   - 통제가 시작되지 않은 리그(구버전 세이브 · 직접 만든 리그)는 기존처럼 자유 진행한다.
    /// </summary>
    public partial class GMOotpFrontOfficeUIController
    {
        public const string TurnLockName = "TurnLockOverlay";

        private RectTransform turnLock;
        private Text turnLockTitle, turnLockBody;

        public bool IsTurnLockShown => turnLock != null && turnLock.gameObject.activeSelf;
        public int StoveTurn => GMStoveTurns.Current(League);

        /// <summary>패널이 해금되는 Turn(0 = 항상 열림).</summary>
        public static int PaneUnlockTurn(string pane)
        {
            switch (pane)
            {
                case PaneSeasonSummary: return 1;
                case PaneLockerRoom: case PaneProtect: return 2;
                case PaneSecondDraft: return 3;     // [TASK-GM-15]
                case PaneFuturesMeeting: return 6;  // [TASK-GM-15]
                case PaneNegotiation: case PaneSalaries: return 4;
                case PaneFA: case PaneTrade: return 5;
                case PaneDraft: return 7;
                default: return 0;
            }
        }

        /// <summary>Turn 대표 방.</summary>
        public static string TurnPrimaryPane(int turn)
        {
            switch (turn)
            {
                case 1: return PaneSeasonSummary;
                case 2: return PaneLockerRoom;
                case 3: return PaneSecondDraft;     // [TASK-GM-15] 2차 드래프트실
                case 4: return PaneNegotiation;
                case 5: return PaneFA;
                case 6: return PaneFuturesMeeting;  // [TASK-GM-15] 육성 회의실
                case 7: return PaneDraft;
                default: return PaneOwner;
            }
        }

        public bool IsPaneLocked(string pane) => GMStoveTurns.IsGating(League) && PaneUnlockTurn(pane) > GMStoveTurns.Current(League);

        /// <summary>실행 차단 - 잠긴 방의 계약 · 영입 · 트레이드 · 지명 실행을 막는다.</summary>
        private bool TurnBlocked(string pane, out string message)
        {
            message = "";
            if (!IsPaneLocked(pane)) return false;
            message = $"잠김 - {GMStoveTurns.Label(PaneUnlockTurn(pane))}에서 열립니다(현재 {GMStoveTurns.Label(GMStoveTurns.Current(League))}). [진행하기]로 단계를 넘기십시오.";
            SetStatus(message);
            return true;
        }

        private void BuildTurnLockOverlay()
        {
            turnLock = CompyaUiKit.Fill(root, TurnLockName);
            CompyaUiKit.Box(turnLock, "LockBg", 0, 240, 1920, 1040, new Color(0.05f, 0.06f, 0.08f, 0.95f));
            turnLockTitle = L(turnLock, "LockTitle", "", 460, 420, 1460, 472, PanelTitlePt + 4, TextAnchor.MiddleCenter, Gold);
            turnLockBody = L(turnLock, "LockBody", "", 460, 480, 1460, 600, BodyPt, TextAnchor.UpperCenter, White);
            turnLockBody.lineSpacing = 1.15f;
            Btn(turnLock, "LockHome", "메인 홈으로 - [진행하기]로 단계 진행", 660, 620, 1260, 670, ButtonOn, ButtonPt).onClick.AddListener(GoMainHome);
            turnLock.gameObject.SetActive(false);
        }

        private void UpdateTurnLock()
        {
            if (turnLock == null) return;
            bool locked = IsPaneLocked(currentPane);
            turnLock.gameObject.SetActive(locked);
            if (!locked) return;
            int need = PaneUnlockTurn(currentPane), now = GMStoveTurns.Current(League);
            turnLockTitle.text = $"잠김 - {GMStoveTurns.Label(need)}에서 열립니다";
            turnLockBody.text = $"현재 {GMStoveTurns.Label(now)} · 활성 방: {GMStoveTurns.Rooms[now - 1]}\n스토브리그는 8 Turn 순서대로 진행됩니다. 이번 Turn의 방을 확인한 뒤 메인 홈에서 [진행하기]를 누르십시오.";
            turnLock.SetAsLastSibling();
        }

        /// <summary>[진행하기] 전 정리 - 전력 분석 · 경기 결과 화면 · 대시보드 · 팝업을 닫는다. 실시간 이닝 경기 중이면 false(경기를 먼저 끝내야 한다).</summary>
        private bool CloseOverlaysForContinue()
        {
            var view = PrePostView != null ? PrePostView : FindAnyObjectByType<GMMatchPrePostUIController>(FindObjectsInactive.Include);
            if (view != null && view.gameObject.activeSelf)
            {
                if (view.Stage == GMMatchStage.LiveInning) { SetStatus("실시간 이닝 경기 진행 중 - [경기 끝까지] 또는 [결과 보기]로 경기를 마친 뒤 진행하십시오."); return false; }
                view.CloseAll();
            }
            var dash = Dash;
            if (dash != null && dash.gameObject.activeSelf)
            {
                simulator?.Stop();
                dash.gameObject.SetActive(false);
            }
            if (discussPopup != null) discussPopup.gameObject.SetActive(false);
            if (pctPopup != null) pctPopup.gameObject.SetActive(false);
            if (counterPopup != null) counterPopup.gameObject.SetActive(false);
            if (lrEventPopup != null) lrEventPopup.gameObject.SetActive(false);
            if (assistantPopup != null) assistantPopup.gameObject.SetActive(false); // [TASK-GM-16]
            return true;
        }

        /// <summary>Turn 통제 중 메인 홈 [진행하기] - 대표 방을 아직 안 열었으면 연다, 열었으면 다음 Turn으로 넘기고 그 방을 연다.</summary>
        private bool ContinueStoveTurn()
        {
            var league = League;
            int turn = GMStoveTurns.Current(league);
            if (!GMFrontOffice.Ensure(league).StoveTurnVisited)
            {
                OpenTurnRoom(turn);
                SetStatus($"{GMStoveTurns.Label(turn)} - {GMStoveTurns.Rooms[turn - 1]}을(를) 확인한 뒤 메인 홈에서 [진행하기]로 다음 Turn을 여십시오.");
                return true;
            }
            // [TASK-GM-16] 하드 차단이 없고 놓친 필수 행동(미계약 만료자 · 지명 0명 · 주장 · 1군 부족)이 있으면 조력자(운영팀장)가 먼저 개입한다
            if (GMStoveTurns.BlockReason(league) == "")
            {
                var issues = GMAssistant.PendingIssues(league);
                if (issues.Count > 0)
                {
                    ShowAssistant(issues);
                    SetStatus($"{GMAssistant.ProfileFor(league).Plate}이(가) 확인을 요청했습니다 - {string.Join(" · ", issues.Select(i => i.Title))}");
                    return true;
                }
            }
            return AdvanceStoveTurnNow();
        }

        public void OpenTurnRoom(int turn)
        {
            switch (turn)
            {
                case 1: OpenSeasonSummary(); break;
                case 2: OpenLockerRoom(); break;
                case 3: OpenSecondDraft(); break;     // [TASK-GM-15]
                case 4: OpenNegotiationRoom(); break;
                case 5: SelectMainTab(4); SelectSubTab(subTabs[4].FindIndex(s => s.Pane == PaneFA)); break;
                case 6: OpenFuturesMeeting(); break;  // [TASK-GM-15]
                case 7: SelectMainTab(5); SelectSubTab(subTabs[5].FindIndex(s => s.Pane == PaneDraft)); break;
                default: GoMainHome(); break;
            }
        }

        /// <summary>Refresh 시작 - 이번 Turn 대표 방을 보고 있으면 확인 처리.</summary>
        private void MarkTurnVisit()
        {
            var league = League;
            if (league == null || !GMStoveTurns.IsGating(league)) return;
            if (currentPane == TurnPrimaryPane(GMStoveTurns.Current(league)) && !IsPaneLocked(currentPane)) GMStoveTurns.MarkVisited(league);
        }
    }
}
