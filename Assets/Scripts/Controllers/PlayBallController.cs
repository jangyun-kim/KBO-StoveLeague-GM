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
    /// <summary>
    /// GDD v4.0("단장 시선" - 수동 개입 배제) 기준의 유일한 인게임 플레이 방식: MatchEngine의 스텝
    /// 단위 API(BeginMatch/PlayNextAtBat)를 유저 입력 대기 없이 끝까지 논스톱으로 소비하며, 매
    /// 타석마다 OnAtBatEnd/OnInningEnd를 발생시켜 텍스트 중계/스코어보드가 실시간으로 갱신되게 한다.
    ///
    /// (TASK-KBO-031: 과거 GDD v3.1의 3방식 - 빠른 진행/하이라이트(개입 대기)/풀 플레이(수동 스텝) -
    /// 중 하이라이트/풀 플레이는 대타·투수 교체 개입 UI와 함께 제거되었다. SubstitutionUIController/
    /// InterventionController/HighlightConditions/HighlightConfig도 이때 함께 삭제됨.)
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

        public bool IsMatchInProgress { get; private set; }
        public MatchResult LastResult { get; private set; }

        /// <summary>진행 중인 MatchEngine.</summary>
        public MatchEngine Engine => engine;

        public Team HomeTeam { get; private set; }
        public Team AwayTeam { get; private set; }

        /// <summary>타석이 1회 진행될 때마다 호출된다 (텍스트 중계 로그 등 연출 갱신용).</summary>
        public event Action<AtBatStepResult> OnAtBatEnd;

        /// <summary>하프이닝이 끝난 타석에 한해 추가로 호출된다 (이닝별 스코어보드 갱신용).</summary>
        public event Action<AtBatStepResult> OnInningEnd;

        /// <summary>경기가 완전히 끝났을 때(결과가 LeagueManager에도 이미 반영된 뒤) 호출된다.</summary>
        public event Action<MatchResult> OnMatchCompleted;

        /// <summary>
        /// LeagueManager.PeekNextFixture()가 가리키는 "다음 경기"를 논스톱으로 시작한다.
        /// </summary>
        public void StartMatch()
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

            var homeModifiers = BuildTeamPowerModifiers(fixture.HomeTeam, homeRoster, isHome: true);
            var awayModifiers = BuildTeamPowerModifiers(fixture.AwayTeam, awayRoster, isHome: false);

            BeginMatch(homeRoster, awayRoster, homeModifiers, awayModifiers,
                fixture.HomeTeam.ToString(), fixture.AwayTeam.ToString(), isPostSeason);
        }

        /// <summary>
        /// [TASK-KBO-037] team이 유저 팀(LeagueManager.Instance.UserTeam)이면 GameManager.Instance.
        /// FavoriteTeam을 기준으로, 그 외(AI 팀)는 favoriteTeam 없이(null) GameManager.CalculateSynergy()를
        /// 호출해 로스터 내 최다 구단 기준으로 판정한다.
        /// [TASK-KBO-039] ConditionBuff는 홈팀이면 TeamPowerModifiers.HomeAdvantageConditionBuff(+2),
        /// 원정팀이면 0이다. 치어리더 효과와 ClutchMultiplier는 아직 데이터 시스템이 없어 기본값(1.0f)을 쓴다.
        /// </summary>
        private static TeamPowerModifiers BuildTeamPowerModifiers(Team team, List<Player> roster, bool isHome)
        {
            bool isUserTeamWithFavorite = LeagueManager.Instance != null && team == LeagueManager.Instance.UserTeam
                && GameManager.Instance != null && GameManager.Instance.FavoriteTeam != Team.None;
            string favoriteTeam = isUserTeamWithFavorite ? GameManager.Instance.FavoriteTeam.ToString() : null;

            int synergy = GameManager.CalculateSynergy(roster, favoriteTeam);
            int conditionBuff = isHome ? TeamPowerModifiers.HomeAdvantageConditionBuff : 0;
            return new TeamPowerModifiers(synergy, conditionBuff);
        }

        private void BeginMatch(List<Player> homeRoster, List<Player> awayRoster,
            TeamPowerModifiers homeModifiers, TeamPowerModifiers awayModifiers,
            string homeTeamName, string awayTeamName, bool isPostSeason)
        {
            if (IsMatchInProgress)
            {
                Debug.LogWarning("[PlayBallController] 이미 진행 중인 경기가 있습니다.");
                return;
            }

            engine = new MatchEngine(homeRoster, awayRoster, homeModifiers, awayModifiers, skillDB, engineConfig);
            IsMatchInProgress = true;

            engine.BeginMatch(homeTeamName, awayTeamName, isPostSeason);
            StartCoroutine(PlayNonstop());
        }

        /// <summary>
        /// 유저 입력 대기(WaitUntil 등) 없이 경기가 끝날 때까지(9회말 종료, 필요 시 연장 12회 상한)
        /// 타석을 자동으로 연속 진행한다. 매 타석마다 OnAtBatEnd/OnInningEnd를 발생시켜 텍스트 중계/
        /// 스코어보드가 실시간으로 갱신된다. 타석 사이에 프레임 하나(yield return null)만 넘기는데,
        /// 이는 유저 입력을 "기다리는" 것이 아니라 - 어떤 외부 신호도 없이 다음 프레임에 스스로 이어감 -
        /// 텍스트 중계가 한 프레임에 몰아서 출력되지 않고 실시간으로 흘러가도록, 그리고
        /// DebugForceWinCurrentMatch()가 경기 도중 개입할 수 있도록 하기 위함이다.
        /// </summary>
        private IEnumerator PlayNonstop()
        {
            while (!engine.IsGameOver)
            {
                var step = engine.PlayNextAtBat();
                RaiseStepEvents(step);
                yield return null;
            }

            if (IsMatchInProgress) FinishMatch();
        }

        /// <summary>OnAtBatEnd는 매 타석마다, OnInningEnd는 그 중 하프이닝이 끝난 타석에서만 추가로 발생시킨다.</summary>
        private void RaiseStepEvents(AtBatStepResult step)
        {
            OnAtBatEnd?.Invoke(step);
            if (step.HalfInningEnded)
            {
                OnInningEnd?.Invoke(step);
            }
        }

        private void FinishMatch()
        {
            LastResult = engine.Result;
            IsMatchInProgress = false;

            LeagueManager.Instance?.CompleteNextFixture(LastResult);

            OnMatchCompleted?.Invoke(LastResult);
        }

        /// <summary>
        /// [디버그/QA 전용] 진행 중인 경기를 즉시 유저 팀 승리로 강제 종료한다(DebugPanelUI 전용 진입점).
        ///
        /// PlayNonstop() 코루틴이 아직 살아 있는 상태(타석 사이 yield return null 지점)이므로, 그 코루틴이
        /// 다음 프레임에 깨어나 while(!engine.IsGameOver) 확인 후 스스로 FinishMatch()를 또 호출해
        /// LeagueManager.CompleteNextFixture()가 엉뚱한 다음 경기에 이번 결과를 잘못 기록하는 일이
        /// 없도록, 여기서 먼저 StopAllCoroutines()로 코루틴을 확실히 멈춘 뒤 FinishMatch()를 직접 호출한다.
        /// </summary>
        public void DebugForceWinCurrentMatch()
        {
            if (!IsMatchInProgress || engine == null || engine.IsGameOver) return;

            var userTeam = LeagueManager.Instance != null ? LeagueManager.Instance.UserTeam : Team.None;
            string winningTeamName = (userTeam == HomeTeam ? HomeTeam : AwayTeam).ToString();

            engine.DebugForceEndGame(winningTeamName);

            StopAllCoroutines();
            FinishMatch();
        }
    }
}
