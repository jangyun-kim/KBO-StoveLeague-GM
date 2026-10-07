using System.Linq;
using KBOManager.Services;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-GM-10] 프런트 오피스 - 원 소속 우선 협상(연봉·재계약 화면 하단) · 모바일 RaycastTarget 정리.
    ///   내 구단 계약 만료자는 시즌 종료 후 우선 협상 명단에 남는다 → 표에서 골라 [재계약/연장 협상 실행]으로 잔류시키고, [우선 협상 마감]을 누르면 남은 선수가 FA 공시된다
    ///   (마감하지 않아도 정규시즌 개막 준비에서 자동 마감).
    /// </summary>
    public partial class GMOotpFrontOfficeUIController
    {
        private Text priorityLabel;
        private Button priorityClose;

        private void BuildPriorityRow(Transform pane)
        {
            priorityLabel = L(pane, "PriorityLabel", "", 1314, 956, 1700, 1030, CellPt, TextAnchor.MiddleLeft, Gold);
            priorityClose = Btn(pane, "PriorityClose", "우선 협상 마감", 1710, 962, 1896, 1026, new Color(0.55f, 0.18f, 0.16f), BodyPt);
            priorityClose.onClick.AddListener(() => ClosePriorityNegotiation());
        }

        private void RefreshPriorityRow()
        {
            if (priorityLabel == null || League == null) return;
            bool open = GMReserveList.IsPriorityOpen(League);
            var pending = GMReserveList.PendingPriority(League);
            priorityLabel.text = open
                ? $"원 소속 우선 협상 {League.PriorityNegotiationIds.Count - pending.Count}/{League.PriorityNegotiationIds.Count}명 잔류 · 미계약 {pending.Count}명 - {string.Join(" · ", pending.Take(3).Select(p => p.Template.PlayerName))}{(pending.Count > 3 ? " 외" : "")}"
                : "원 소속 우선 협상 대상 없음(시즌 종료 후 계약 만료자가 생기면 열립니다)";
            priorityClose.interactable = open;
        }

        /// <summary>[우선 협상 마감] - 재계약하지 않은 만료자를 FA로 내보낸다.</summary>
        public int ClosePriorityNegotiation()
        {
            if (League == null) return 0;
            var gone = GMReserveList.ClosePriorityNegotiation(League);
            SetStatus(gone.Count > 0 ? $"우선 협상 마감 - FA 공시 {gone.Count}명: {string.Join(" · ", gone.Select(p => p.Template.PlayerName))}" : "우선 협상 마감 - 계약 만료자 없음");
            Refresh();
            return gone.Count;
        }

        /// <summary>[TASK-GM-10] 모바일 터치 - 허브 전체 RaycastTarget 정리(끈 개수).</summary>
        public int SanitizeRaycasts() => GMRaycastSanitizer.Sanitize(transform);
    }
}
