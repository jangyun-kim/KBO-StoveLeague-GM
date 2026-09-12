using System;
using System.Collections.Generic;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Controllers
{
    /// <summary>
    /// GDD v4.0("단장 시선" - 수동 개입 배제) 기준의 유일한 인게임 플레이 방식.
    ///
    /// [TASK-KBO-041] 경로 단일화: 과거(TASK-KBO-031~040)에는 이 컨트롤러가 MatchEngine의 스텝 단위
    /// API(BeginMatch/PlayNextAtBat)를 유저 입력 대기 없이 직접 순회하며 매 타석마다 OnAtBatEnd/
    /// OnInningEnd를 실시간으로 발생시켰다. 이제는 MatchEngine.PlayFullMatchAsEventQueue()를 한 번
    /// 호출해 경기 전체를 즉시 계산한 뒤, 그 결과(Queue&lt;PlayEvent&gt;)를 BroadcastUIManager에
    /// 넘겨 "재생"(딜레이를 둔 순차 표시)하는 단일 경로로 통합했다 - 실시간 텍스트 중계는 이제
    /// BroadcastUIManager의 책임이다(이 컨트롤러는 더 이상 화면을 직접 갱신하지 않는다).
    ///
    /// [TASK-KBO-047] 위 OnAtBatEnd/OnInningEnd 이벤트는 TASK-KBO-041 이후 아무도 발생시키지 않는
    /// 죽은 이벤트로 남아 CS0067 경고를 유발하고 있었다 - 실제 구독부(SeasonStatManager/
    /// InGameUIController)도 함께 더 이상 필요 없어져 선언 자체를 완전히 삭제했다. 시즌 기록은
    /// RecordSeasonStatsFromEvents()가, 텍스트 로그/스코어보드는 BroadcastUIManager.PlayOneEvent()가
    /// 각자 PlayEvent 큐를 직접 읽어 처리하므로 기능 손실은 없다.
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
        [Tooltip("[TASK-KBO-041] 실제 텍스트 중계 재생을 맡는 컴포넌트. 비워두면 재생 없이 즉시 " +
                 "결과 처리(보상/시즌기록/스케줄 진행)만 수행한다(화면에는 아무 것도 표시되지 않음).")]
        [SerializeField] private BroadcastUIManager broadcastUIManager;

        private MatchEngine engine;

        public bool IsMatchInProgress { get; private set; }
        public MatchResult LastResult { get; private set; }

        /// <summary>진행 중인 MatchEngine.</summary>
        public MatchEngine Engine => engine;

        public Team HomeTeam { get; private set; }
        public Team AwayTeam { get; private set; }

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

            // 경기 전체를 즉시 계산한다(랜덤 판정은 이 한 번의 호출 안에서만 일어난다 - 재생은 그 결과를
            // "보여주기"만 할 뿐 시뮬레이션을 다시 돌리지 않는다). engine.Result는 이 시점에 이미 최종값이다.
            var queue = engine.PlayFullMatchAsEventQueue(homeTeamName, awayTeamName, isPostSeason);
            var events = new List<PlayEvent>(queue);

            RecordSeasonStatsFromEvents(events);

            if (broadcastUIManager != null)
            {
                broadcastUIManager.OnPlaybackFinished += HandlePlaybackFinished;
                broadcastUIManager.LoadEvents(events);
            }
            else
            {
                Debug.LogWarning("[PlayBallController] BroadcastUIManager가 연결되지 않아 재생 없이 결과만 즉시 처리합니다.");
                FinishMatch();
            }
        }

        /// <summary>
        /// [TASK-KBO-041] BroadcastUIManager.OnPlaybackFinished 핸들러. 재생이 끝난 뒤(자연 종료 또는
        /// 스킵) 정확히 한 번만 실행되도록 즉시 구독을 해제한 뒤 FinishMatch()로 넘긴다.
        /// </summary>
        private void HandlePlaybackFinished(MatchResult result)
        {
            if (broadcastUIManager != null) broadcastUIManager.OnPlaybackFinished -= HandlePlaybackFinished;
            FinishMatch();
        }

        /// <summary>
        /// [TASK-KBO-041] PlayFullMatchAsEventQueue()는 경기 전체를 한 번에 계산해 Queue&lt;PlayEvent&gt;만
        /// 반환하므로, LeagueManager.SimulateFixture()처럼 매 타석의 AtBatStepResult를 실시간으로 받아
        /// SeasonStatManager.RecordAtBat()에 넘기던 기존 경로가 사라진다 - 이 메서드는 그 회귀를 막기
        /// 위한 어댑터다. PlayEvent(AtBatResult 타입)를 RecordAtBat()이 실제로 읽는 필드만 채운
        /// AtBatStepResult로 되돌려 그대로 넘긴다(MatchEngine.cs/SeasonStatManager.cs 둘 다 수정하지
        /// 않고 기존 공개 API를 그대로 재사용).
        ///
        /// HalfInningEnded는 "이 AtBatResult 이후 다음 AtBatResult가 나오기 전에 HalfInningEnd 타입
        /// 이벤트가 있는가"로 정확히 역산한다 - PlayFullMatchAsEventQueue()가 큐를 채우는 순서
        /// (AtBatResult -&gt; RunnerAdvance* -&gt; [HalfInningEnd] -&gt; 다음 AtBatResult...)와 정확히
        /// 대응하므로 안전하다.
        /// </summary>
        private void RecordSeasonStatsFromEvents(List<PlayEvent> events)
        {
            if (SeasonStatManager.Instance == null) return;

            for (int i = 0; i < events.Count; i++)
            {
                var evt = events[i];
                if (evt.Type != PlayEventType.AtBatResult) continue;

                bool halfInningEnded = false;
                for (int j = i + 1; j < events.Count; j++)
                {
                    if (events[j].Type == PlayEventType.AtBatResult) break;
                    if (events[j].Type == PlayEventType.HalfInningEnd) { halfInningEnded = true; break; }
                }

                var step = new AtBatStepResult
                {
                    Batter = evt.Batter,
                    Pitcher = evt.Pitcher,
                    Result = evt.Result,
                    RunsScoredThisPlay = evt.RunsScoredThisPlay,
                    HalfInningEnded = halfInningEnded,
                    IsTopHalf = evt.IsTopHalf,
                    HomeScore = evt.HomeScore,
                    AwayScore = evt.AwayScore,
                    State = new MatchState { Inning = evt.Inning, Outs = evt.Outs },
                    LogMessage = evt.LogMessage,
                };

                SeasonStatManager.Instance.RecordAtBat(step, HomeTeam, AwayTeam);
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
        /// [디버그/QA 전용] 진행 중인 재생을 즉시 스킵한다(DebugPanelUI 전용 진입점).
        ///
        /// [TASK-KBO-041 의미 변경] 과거에는 engine.DebugForceEndGame()으로 스코어를 조작해 "유저 팀
        /// 승리로 강제 종료"했다. 이제는 BeginMatch() 시점에 PlayFullMatchAsEventQueue()가 경기를
        /// 이미 완전히 계산해 버려서(engine.IsGameOver가 항상 true) 승자를 사후에 바꿀 수 없다 - 그래서
        /// 이 메서드는 "경기 결과를 유저 팀 승리로 조작"하는 대신 "재생을 즉시 스킵해 결과 화면으로
        /// 넘어간다"로 의미가 축소되었다. broadcastUIManager가 없으면 재생 자체가 없었을 것이므로
        /// (BeginMatch()가 이미 FinishMatch()까지 처리했다) 여기서는 아무 것도 하지 않는다.
        /// </summary>
        public void DebugForceWinCurrentMatch()
        {
            if (!IsMatchInProgress) return;

            broadcastUIManager?.RequestSkip();
        }
    }
}
