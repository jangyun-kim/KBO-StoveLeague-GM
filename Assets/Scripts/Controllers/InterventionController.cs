using System.Collections.Generic;
using System.Linq;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 유저가 하이라이트 멈춤에서 '개입'(ResumeMatch(true))을 선택했을 때의 후속 흐름을 담당한다.
    /// PlayBallController.OnInterventionRequested를 구독해 대기 중인 상황을 받아 두고, 벤치(후보 타자)/
    /// 불펜(구원 투수) 후보 목록을 UI에 제공한 뒤, 유저가 확정한 교체를 MatchEngine의
    /// SubstituteBatter/SubstitutePitcher API로 반영하고 경기를 재개시킨다.
    ///
    /// 교체 후보는 항상 GameManager.Instance.Roster(유저 팀)에서만 가져온다 - AI 팀 로스터는
    /// 유저가 직접 교체할 대상이 아니다.
    /// </summary>
    public class InterventionController : MonoBehaviour
    {
        [SerializeField] private PlayBallController playBallController;

        public AtBatStepResult PendingStep { get; private set; }
        public IHighlightCondition PendingTrigger { get; private set; }
        public bool HasPendingIntervention => PendingStep != null;

        private void OnEnable()
        {
            if (playBallController != null)
            {
                playBallController.OnInterventionRequested += HandleInterventionRequested;
            }
        }

        private void OnDisable()
        {
            if (playBallController != null)
            {
                playBallController.OnInterventionRequested -= HandleInterventionRequested;
            }
        }

        private void HandleInterventionRequested(AtBatStepResult step, IHighlightCondition triggeredBy)
        {
            PendingStep = step;
            PendingTrigger = triggeredBy;
            Debug.Log($"[InterventionController] 개입 대기 시작 (사유: {triggeredBy?.Name})");
        }

        private bool IsUserTeamBatting()
        {
            if (playBallController?.Engine == null || LeagueManager.Instance == null) return false;

            var battingTeam = playBallController.Engine.IsHomeTeamBatting ? playBallController.HomeTeam : playBallController.AwayTeam;
            return battingTeam == LeagueManager.Instance.UserTeam;
        }

        /// <summary>
        /// 지금 대타로 낼 수 있는 유저 로스터의 벤치 타자 목록. 지금 타석이 유저 팀 공격이 아니면 비어 있다
        /// (상대 AI 팀 타순은 유저가 교체할 수 없다).
        /// </summary>
        public List<Player> GetAvailablePinchHitters()
        {
            if (!IsUserTeamBatting() || GameManager.Instance == null) return new List<Player>();

            var engine = playBallController.Engine;
            var currentLineup = engine.IsHomeTeamBatting ? engine.HomeBattingOrder : engine.AwayBattingOrder;

            return GameManager.Instance.Roster
                .Where(p => p?.Template != null && !p.Template.IsPitcher)
                .Where(p => !currentLineup.Contains(p))
                .Where(p => !engine.SubbedOutList.Contains(p))
                .ToList();
        }

        /// <summary>
        /// 지금 투입할 수 있는 유저 로스터의 구원 투수 목록. 지금 타석에서 유저 팀이 수비 중이 아니면(=공격 중이면) 비어 있다.
        /// </summary>
        public List<Player> GetAvailableRelievers()
        {
            if (IsUserTeamBatting() || GameManager.Instance == null) return new List<Player>();

            var engine = playBallController.Engine;
            var currentPitcher = engine.CurrentPitcher;

            return GameManager.Instance.Roster
                .Where(p => p?.Template != null && p.Template.IsPitcher)
                .Where(p => p != currentPitcher)
                .Where(p => !engine.SubbedOutList.Contains(p))
                .ToList();
        }

        /// <summary>대타 교체를 확정하고 경기를 재개한다.</summary>
        public void ConfirmBatterSubstitution(Player newBatter)
        {
            if (playBallController?.Engine == null) return;

            bool success = playBallController.Engine.SubstituteBatter(newBatter);
            Debug.Log($"[InterventionController] 대타 교체 {(success ? "성공" : "실패")}: {newBatter?.Template?.PlayerName}");

            ClearPendingAndResume();
        }

        /// <summary>투수 교체를 확정하고 경기를 재개한다.</summary>
        public void ConfirmPitcherSubstitution(Player newPitcher)
        {
            if (playBallController?.Engine == null) return;

            bool success = playBallController.Engine.SubstitutePitcher(newPitcher);
            Debug.Log($"[InterventionController] 투수 교체 {(success ? "성공" : "실패")}: {newPitcher?.Template?.PlayerName}");

            ClearPendingAndResume();
        }

        /// <summary>교체 없이 개입 흐름을 종료하고 경기를 재개한다.</summary>
        public void SkipIntervention()
        {
            ClearPendingAndResume();
        }

        private void ClearPendingAndResume()
        {
            PendingStep = null;
            PendingTrigger = null;
            playBallController.ResumeMatch(false);
        }
    }
}
