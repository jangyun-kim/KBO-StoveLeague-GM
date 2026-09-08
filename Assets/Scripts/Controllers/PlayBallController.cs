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

        private MatchEngine engine;

        /// <summary>
        /// 하이라이트 모드에서 시뮬레이션을 멈출 트리거 목록(OR 조합 - 하나라도 만족하면 멈춘다).
        /// 새 트리거를 추가하고 싶으면 IHighlightCondition을 구현한 클래스를 만들어 이 리스트에
        /// AddHighlightCondition()으로 등록하면 되고, 이 파일(PlayBallController.cs) 자체를 고칠 필요는 없다.
        /// </summary>
        private readonly List<IHighlightCondition> highlightConditions = new List<IHighlightCondition>
        {
            new ScoringPositionCondition(),
            new LateInningCloseGameCondition(),
            new WalkOffDangerCondition(),
        };

        /// <summary>대기 중(WaitUntil)인 코루틴을 풀어주는 스위치. ResumeMatch()가 false로 내려서 재개시킨다.</summary>
        private bool isPausedForUser;

        public bool IsMatchInProgress { get; private set; }
        public bool IsPausedForUser => isPausedForUser;
        public PlayMode CurrentMode { get; private set; }
        public MatchResult LastResult { get; private set; }

        /// <summary>타석이 1회 진행될 때마다 호출된다 (연출/로그 갱신용).</summary>
        public event Action<AtBatStepResult> OnAtBatResolved;

        /// <summary>하이라이트 트리거가 발동해 유저 입력 대기 상태로 멈췄을 때 호출된다. UI는 이때 개입/스킵 버튼을 노출한다.</summary>
        public event Action<AtBatStepResult, IHighlightCondition> OnHighlightMoment;

        /// <summary>경기가 완전히 끝났을 때(결과가 LeagueManager에도 이미 반영된 뒤) 호출된다.</summary>
        public event Action<MatchResult> OnMatchCompleted;

        /// <summary>새 하이라이트 트리거를 등록한다. (개방-폐쇄 원칙: 이 클래스를 고치지 않고 트리거를 추가하는 진입점)</summary>
        public void AddHighlightCondition(IHighlightCondition condition)
        {
            if (condition != null) highlightConditions.Add(condition);
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
        /// 하이라이트 멈춤 상태를 UI(버튼)에서 재개시킨다. 대기 중이 아닐 때 호출하면 아무 일도 하지 않는다.
        /// </summary>
        /// <param name="intervene">
        /// false: 개입 없이 다음 타석으로 스킵한다.
        /// true: 개입을 선택했다는 로그만 남기고 대기를 해제한다 - 실제 투수/타자 교체 UI 연결은 후속 과제다.
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
                // TODO: 투수 교체/대타 등 실제 개입 UI가 만들어지면 여기서 그 흐름을 시작해야 한다.
                Debug.Log("[PlayBallController] 유저가 개입을 선택했습니다. (교체 UI는 아직 연결되지 않음)");
            }

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
