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
        [Tooltip("하이라이트 정지 조건(득점권/후반 접전/끝내기 위기)의 활성화 여부와 임계값. " +
                 "비워두면 기본값(전부 활성화, 7회/3점차/9회 기준)으로 동작한다.")]
        [SerializeField] private HighlightConfig highlightConfig;

        private MatchEngine engine;

        /// <summary>
        /// 하이라이트 모드에서 시뮬레이션을 멈출 트리거 목록(OR 조합 - 하나라도 만족하면 멈춘다).
        /// highlightConfig 에셋의 값으로 BuildHighlightConditions()가 채운다. 코드로만 표현 가능한
        /// 커스텀 트리거는 AddHighlightCondition()으로 추가로 등록할 수 있다(개방-폐쇄 원칙 유지).
        /// </summary>
        private readonly List<IHighlightCondition> highlightConditions = new List<IHighlightCondition>();

        /// <summary>대기 중(WaitUntil)인 코루틴을 풀어주는 스위치. ResumeMatch(false)가 내려서 재개시킨다.</summary>
        private bool isPausedForUser;

        // ResumeMatch(true)로 개입이 요청됐을 때 InterventionController에게 넘겨줄, 멈춘 시점의 상황 스냅샷.
        private AtBatStepResult pausedStep;
        private IHighlightCondition pausedTrigger;

        public bool IsMatchInProgress { get; private set; }
        public bool IsPausedForUser => isPausedForUser;
        public PlayMode CurrentMode { get; private set; }
        public MatchResult LastResult { get; private set; }

        /// <summary>진행 중인 MatchEngine. InterventionController 등이 SubstituteBatter/Pitcher를 직접 호출할 때 쓴다.</summary>
        public MatchEngine Engine => engine;

        public Team HomeTeam { get; private set; }
        public Team AwayTeam { get; private set; }

        /// <summary>타석이 1회 진행될 때마다 호출된다 (연출/로그 갱신용).</summary>
        public event Action<AtBatStepResult> OnAtBatResolved;

        /// <summary>하이라이트 트리거가 발동해 유저 입력 대기 상태로 멈췄을 때 호출된다. UI는 이때 개입/스킵 버튼을 노출한다.</summary>
        public event Action<AtBatStepResult, IHighlightCondition> OnHighlightMoment;

        /// <summary>ResumeMatch(true)(개입)가 호출됐을 때 발생한다. InterventionController가 이 이벤트를 구독해
        /// 교체 UI 흐름을 시작한다. 이 시점에는 아직 시뮬레이션이 재개되지 않는다(교체 확정 후 재개된다).</summary>
        public event Action<AtBatStepResult, IHighlightCondition> OnInterventionRequested;

        /// <summary>경기가 완전히 끝났을 때(결과가 LeagueManager에도 이미 반영된 뒤) 호출된다.</summary>
        public event Action<MatchResult> OnMatchCompleted;

        /// <summary>새 하이라이트 트리거를 등록한다. (개방-폐쇄 원칙: 이 클래스를 고치지 않고 트리거를 추가하는 진입점)</summary>
        public void AddHighlightCondition(IHighlightCondition condition)
        {
            if (condition != null) highlightConditions.Add(condition);
        }

        /// <summary>highlightConfig 에셋(없으면 기본값)을 기준으로 표준 3종 트리거 목록을 다시 구성한다.</summary>
        private void BuildHighlightConditions()
        {
            highlightConditions.Clear();

            bool enableScoringPosition = highlightConfig == null || highlightConfig.enableScoringPosition;
            bool enableLateInning = highlightConfig == null || highlightConfig.enableLateInningCloseGame;
            bool enableWalkOff = highlightConfig == null || highlightConfig.enableWalkOffDanger;

            int lateInningThreshold = highlightConfig != null ? highlightConfig.lateInningThreshold : 7;
            int closeGameMargin = highlightConfig != null ? highlightConfig.closeGameScoreMargin : 3;
            int walkOffInnings = highlightConfig != null ? highlightConfig.walkOffRegulationInnings : 9;

            if (enableScoringPosition) highlightConditions.Add(new ScoringPositionCondition());
            if (enableLateInning) highlightConditions.Add(new LateInningCloseGameCondition(lateInningThreshold, closeGameMargin));
            if (enableWalkOff) highlightConditions.Add(new WalkOffDangerCondition(walkOffInnings));
        }

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

            HomeTeam = fixture.HomeTeam;
            AwayTeam = fixture.AwayTeam;

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
                    BuildHighlightConditions();
                    StartCoroutine(RunHighlight());
                    break;

                case PlayMode.FullPlay:
                    engine.BeginMatch(homeRoster, awayRoster, homeTeamName, awayTeamName, isPostSeason);
                    // 이후 진행은 UI가 PlayNextStep()을 직접 호출한다(타격 연출 등을 보여준 뒤 다음 타석으로).
                    break;
            }
        }

        /// <summary>
        /// 하이라이트: 백그라운드로 타석을 진행하되, highlightConditions 중 하나라도 발동하면 멈추고
        /// OnHighlightMoment로 제어권을 UI에 넘긴 뒤, 유저가 ResumeMatch()를 호출할 때까지 무한정 대기한다.
        /// </summary>
        private IEnumerator RunHighlight()
        {
            while (!engine.IsGameOver)
            {
                var step = engine.PlayNextAtBat();
                OnAtBatResolved?.Invoke(step);

                if (!step.GameEnded)
                {
                    var triggered = FindTriggeredCondition(step);
                    if (triggered != null)
                    {
                        Debug.Log($"[PlayBallController] 하이라이트 정지: {triggered.Name}");
                        pausedStep = step;
                        pausedTrigger = triggered;
                        OnHighlightMoment?.Invoke(step, triggered);

                        isPausedForUser = true;
                        yield return new WaitUntil(() => !isPausedForUser);
                    }
                }
            }

            FinishMatch();
        }

        private IHighlightCondition FindTriggeredCondition(AtBatStepResult step)
        {
            foreach (var condition in highlightConditions)
            {
                if (condition.ShouldPause(step)) return condition;
            }

            return null;
        }

        /// <summary>
        /// 하이라이트 멈춤 상태에서 UI(버튼)가 호출하는 진입점. 대기 중이 아닐 때 호출하면 아무 일도 하지 않는다.
        /// </summary>
        /// <param name="intervene">
        /// false: 개입 없이 다음 타석으로 진행한다(시뮬레이션이 실제로 재개된다).
        /// true: 아직 재개하지 않고 OnInterventionRequested를 발생시켜 InterventionController에게 교체 흐름을
        /// 넘긴다 - 교체(또는 스킵)가 확정되어 InterventionController가 ResumeMatch(false)를 다시 호출할 때
        /// 비로소 실제로 재개된다.
        /// </param>
        public void ResumeMatch(bool intervene)
        {
            if (!isPausedForUser)
            {
                Debug.LogWarning("[PlayBallController] 대기 중이 아닌데 ResumeMatch가 호출되었습니다.");
                return;
            }

            if (intervene)
            {
                Debug.Log("[PlayBallController] 유저가 개입을 선택했습니다 - 교체 흐름으로 위임합니다.");
                OnInterventionRequested?.Invoke(pausedStep, pausedTrigger);
                return; // isPausedForUser는 그대로 true 유지 - 교체가 끝난 뒤 ResumeMatch(false)가 다시 호출되어야 재개된다.
            }

            pausedStep = null;
            pausedTrigger = null;
            isPausedForUser = false;
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
