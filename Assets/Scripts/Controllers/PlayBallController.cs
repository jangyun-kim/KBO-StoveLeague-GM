using System;
using System.Collections;
using System.Collections.Generic;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Controllers
{
    /// <summary>GDD 5절에 명시된 3가지 인게임 플레이 방식.</summary>
    public enum PlayMode
    {
        QuickPlay,  // 빠른 진행: 결과만 즉시 산출
        Highlight,  // 하이라이트: 득점권 상황 등에서 멈추고 유저에게 제어권을 넘김
        FullPlay    // 풀 플레이: 타석 1개씩 유저가 직접 진행
    }

    /// <summary>
    /// GDD 5절의 3가지 플레이 방식(빠른 진행/하이라이트/풀 플레이)에 맞춰 MatchEngine의 스텝 단위 API
    /// (BeginMatch/PlayNextAtBat)를 어떻게 소비할지 제어하는 UI 브릿지.
    ///
    /// LeagueManager.PeekNextFixture()로 "이번에 치를 경기"를 가져와 직접 시뮬레이션을 진행하고,
    /// 끝나면 LeagueManager.CompleteNextFixture(result)로 결과만 돌려준다 - LeagueManager가 같은
    /// 경기를 다시 시뮬레이션하지 않도록(중복 실행 방지) 결과를 "생산"하는 쪽은 항상 이 컨트롤러다.
    /// </summary>
    public class PlayBallController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SkillDB skillDB;
        [SerializeField] private EngineConfig engineConfig;

        [Header("Highlight Mode")]
        [Tooltip("득점권 상황에서 멈춘 뒤 이 프레임 수만큼 대기하고 자동 재개한다. " +
                 "실제 UI가 붙으면 유저의 '계속' 입력으로 대체되어야 하는 자리표시자 값이다.")]
        [SerializeField] private int highlightAutoResumeFrames = 30;

        private MatchEngine engine;

        public bool IsMatchInProgress { get; private set; }
        public PlayMode CurrentMode { get; private set; }
        public MatchResult LastResult { get; private set; }

        /// <summary>타석이 1회 진행될 때마다 호출된다 (연출/로그 갱신용).</summary>
        public event Action<AtBatStepResult> OnAtBatResolved;

        /// <summary>하이라이트 모드에서 득점권 상황이 발생해 진행이 멈췄을 때 호출된다.</summary>
        public event Action<MatchState> OnHighlightMoment;

        /// <summary>경기가 완전히 끝났을 때(결과가 LeagueManager에도 이미 반영된 뒤) 호출된다.</summary>
        public event Action<MatchResult> OnMatchCompleted;

        /// <summary>
        /// LeagueManager.PeekNextFixture()가 가리키는 "다음 경기"를 지정한 모드로 시작한다.
        /// </summary>
        public void StartMatch(PlayMode mode)
        {
            if (LeagueManager.Instance == null)
            {
                Debug.LogWarning("[PlayBallController] LeagueManager가 없어 경기를 시작할 수 없습니다.");
                return;
            }

            var fixture = LeagueManager.Instance.PeekNextFixture();
            if (fixture == null)
            {
                Debug.LogWarning("[PlayBallController] 더 진행할 예정 경기가 없습니다.");
                return;
            }

            var homeRoster = LeagueManager.Instance.ResolveRosterForTeam(fixture.HomeTeam);
            var awayRoster = LeagueManager.Instance.ResolveRosterForTeam(fixture.AwayTeam);
            bool isPostSeason = fixture.Phase == LeaguePhase.POST_SEASON;

            BeginMatch(homeRoster, awayRoster, fixture.HomeTeam.ToString(), fixture.AwayTeam.ToString(), isPostSeason, mode);
        }

        private void BeginMatch(List<Player> homeRoster, List<Player> awayRoster,
            string homeTeamName, string awayTeamName, bool isPostSeason, PlayMode mode)
        {
            if (IsMatchInProgress)
            {
                Debug.LogWarning("[PlayBallController] 이미 진행 중인 경기가 있습니다.");
                return;
            }

            engine = new MatchEngine(skillDB, engineConfig);
            IsMatchInProgress = true;
            CurrentMode = mode;

            switch (mode)
            {
                case PlayMode.QuickPlay:
                    // 빠른 진행: 기존처럼 PlayFullMatch()를 즉시 실행하고 결과만 UI로 반환한다.
                    engine.PlayFullMatch(homeRoster, awayRoster, homeTeamName, awayTeamName, isPostSeason);
                    FinishMatch();
                    break;

                case PlayMode.Highlight:
                    engine.BeginMatch(homeRoster, awayRoster, homeTeamName, awayTeamName, isPostSeason);
                    StartCoroutine(RunHighlight());
                    break;

                case PlayMode.FullPlay:
                    engine.BeginMatch(homeRoster, awayRoster, homeTeamName, awayTeamName, isPostSeason);
                    // 이후 진행은 UI가 PlayNextStep()을 직접 호출한다(타격 연출 등을 보여준 뒤 다음 타석으로).
                    break;
            }
        }

        /// <summary>
        /// 하이라이트: 백그라운드로 타석을 진행하되, 득점권(2루 이상 주자) 상황이 발생하면 멈추고
        /// OnHighlightMoment로 제어권을 UI에 넘긴다. 지금은 유저 입력 대신 일정 프레임 대기 후
        /// 자동으로 재개하는 것으로 뼈대만 구현했다 - 실제 UI 연동 시 대기 지점을 유저 입력 이벤트로 교체한다.
        /// </summary>
        private IEnumerator RunHighlight()
        {
            while (!engine.IsGameOver)
            {
                var step = engine.PlayNextAtBat();
                OnAtBatResolved?.Invoke(step);

                if (!step.GameEnded && step.State != null && step.State.HasRunnerInScoringPosition)
                {
                    OnHighlightMoment?.Invoke(step.State);
                    yield return WaitForHighlightResume();
                }
            }

            FinishMatch();
        }

        /// <summary>
        /// 하이라이트 정지 후 재개를 기다리는 지점. 자리표시자로 지정된 프레임 수만큼 대기하며,
        /// 실제 UI가 붙으면 유저 입력(예: "계속" 버튼) 이벤트를 기다리는 코드로 교체되어야 한다.
        /// </summary>
        private IEnumerator WaitForHighlightResume()
        {
            for (int i = 0; i < highlightAutoResumeFrames; i++)
            {
                yield return null;
            }
        }

        /// <summary>
        /// 풀 플레이: 타석 1회를 진행하고 결과를 반환한다. UI가 연출(타격 모션/결과 로그)을 다 보여준 뒤
        /// 다시 호출하는 식으로 사용한다. QuickPlay/Highlight 모드에서 호출하면 아무 일도 하지 않는다.
        /// </summary>
        public AtBatStepResult PlayNextStep()
        {
            if (!IsMatchInProgress || engine == null || engine.IsGameOver) return null;

            var step = engine.PlayNextAtBat();
            OnAtBatResolved?.Invoke(step);

            if (engine.IsGameOver)
            {
                FinishMatch();
            }

            return step;
        }

        private void FinishMatch()
        {
            LastResult = engine.Result;
            IsMatchInProgress = false;

            LeagueManager.Instance?.CompleteNextFixture(LastResult);

            OnMatchCompleted?.Invoke(LastResult);
        }
    }
}
