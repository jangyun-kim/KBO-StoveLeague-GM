using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>스킵 처리의 레이스 컨디션을 막기 위한 최소 상태 기계(TASK-KBO-035 설계안).
    /// 상태 전이는 오직 LoadEvents()/PlaybackRoutine()/RequestSkip() 세 곳에서만 일어난다.</summary>
    public enum BroadcastPlaybackState
    {
        Idle,
        Playing,
        Skipping,
        Finished
    }

    /// <summary>
    /// 2.5D 관전 중계 텍스트 로그 재생기(TASK-KBO-035 설계안의 실제 구현). MatchEngine.
    /// PlayFullMatchAsEventQueue()가 경기 전체를 이미 다 계산해 반환한 Queue&lt;PlayEvent&gt;를 받아
    /// List&lt;PlayEvent&gt;(원본, 재생 중에도 절대 변형/제거하지 않음)로 보존하고, 별도의 재생 커서
    /// (playbackCursor)로 순회하며 타석 하나씩 시간차를 두고 InGameUIController.AddLog()에 밀어 넣는다.
    ///
    /// 시뮬레이션은 전혀 수행하지 않는다 - MatchEngine 내부 상태를 직접 건드리지 않고, 오직 전달받은
    /// PlayEvent 데이터만 읽어서 표현한다. 경기 구성(로스터/팀 버프 등)은 이 클래스의 책임이 아니다 -
    /// StartMatch()는 "이미 완전히 구성된 MatchEngine 인스턴스"를 받아 PlayFullMatchAsEventQueue()만
    /// 호출한다(호출부가 LeagueManager/GameManager.CalculateSynergy 등으로 엔진을 준비해 넘겨준다).
    ///
    /// 2D 미니맵 주자(Dot) 애니메이션은 이번 범위에서 제외되어, RunnerAdvance/HalfInningEnd/GameEnd
    /// 타입 이벤트는 재생 시퀀스에는 포함되지만 텍스트 로그를 남기지 않고 조용히 통과한다(AtBatResult
    /// 타입만 LogMessage를 출력한다).
    /// </summary>
    public class BroadcastUIManager : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("타석 결과 로그(PlayEvent.LogMessage)를 실제로 그릴 UI 컨트롤러. 비워두면 로그는 " +
                 "Debug.Log로만 출력되고(크래시 없음), 결과 화면 전환도 생략된다.")]
        [SerializeField] private InGameUIController inGameUIController;

        [Header("Skip")]
        [Tooltip("스킵 버튼. 비워두면 코드에서 RequestSkip()을 직접 호출해야 한다.")]
        [SerializeField] private Button skipButton;

        [Header("Playback Timing")]
        [Tooltip("타석(AtBatResult) 이벤트 1개당 대기 시간(초). 유저 입력을 기다리는 게 아니라 " +
                 "자동으로 다음 이벤트로 넘어가는 연출용 딜레이다.")]
        [SerializeField] private float perEventDelaySeconds = 0.5f;

        private List<PlayEvent> eventLog = new List<PlayEvent>();
        private int playbackCursor;
        private Coroutine playbackHandle;
        private MatchResult pendingResult;

        public BroadcastPlaybackState State { get; private set; } = BroadcastPlaybackState.Idle;

        /// <summary>재생 원본 전체(읽기 전용). LoadEvents() 이후 이 리스트 자체는 재생 중에도, 스킵
        /// 이후에도 절대 변형되지 않는다 - 다시보기/리플레이 기능이 이 리스트를 그대로 재순회하면 된다.</summary>
        public IReadOnlyList<PlayEvent> EventLog => eventLog;

        /// <summary>[TASK-KBO-041] 재생이 끝났을 때(자연 종료 또는 스킵) 1회 발생한다. 결과 패널 자체는
        /// 이미 ShowResult()가 직접 InGameUIController.ShowMatchResult()로 열어 두므로, 이 이벤트는
        /// PlayBallController처럼 "재생이 끝난 뒤에 보상 지급/시즌 기록/다음 경기 스케줄 진행" 등 UI가
        /// 아닌 게임 상태 처리를 이어서 해야 하는 외부 오케스트레이터를 위한 것이다.</summary>
        public event Action<MatchResult> OnPlaybackFinished;

        private void Awake()
        {
            // [TASK-KBO-041] 인스펙터에서 연결하지 않았다면 같은 오브젝트 -> 자식 순으로 InGameUIController를
            // 찾아 자동 바인딩한다. 그래도 못 찾으면 null로 남아(AddLog/ShowMatchResult 호출부의 기존 null
            // 가드가 그대로 방어한다) 크래시 없이 로그만 Debug.Log로 대체된다.
            if (inGameUIController == null) inGameUIController = GetComponent<InGameUIController>();
            if (inGameUIController == null) inGameUIController = GetComponentInChildren<InGameUIController>(true);

            if (skipButton != null) skipButton.onClick.AddListener(RequestSkip);
        }

        /// <summary>
        /// 경기 시작 진입점. 이미 로스터/팀 버프 주입이 끝난 MatchEngine 인스턴스를 받아
        /// PlayFullMatchAsEventQueue()를 호출하고, 반환된 Queue를 즉시 LoadEvents()로 넘겨 재생을
        /// 시작한다. engine이 null이면(호출부 오류 등) 시뮬레이션 없이 바로 결과 화면으로 분기한다
        /// (7항 경계 조건과 동일하게 처리).
        /// </summary>
        public void StartMatch(MatchEngine engine, string homeTeamName = "Home", string awayTeamName = "Away", bool isPostSeason = false)
        {
            if (engine == null)
            {
                Debug.LogWarning("[BroadcastUIManager] MatchEngine이 null이라 재생할 수 없습니다.");
                pendingResult = null;
                LoadEvents(null);
                return;
            }

            var queue = engine.PlayFullMatchAsEventQueue(homeTeamName, awayTeamName, isPostSeason);
            pendingResult = engine.Result;
            LoadEvents(queue);
        }

        /// <summary>
        /// rawEvents를 List&lt;PlayEvent&gt;로 변환해 원본으로 보존하고 재생 커서를 0으로 초기화한 뒤
        /// 재생 코루틴을 시작한다. rawEvents가 null이거나 비어 있으면(7항 경계 조건: 빈 이벤트 목록)
        /// 재생 없이 즉시 결과 화면으로 분기한다.
        /// [TASK-KBO-041 7항] 이미 재생 중(Playing) 또는 스킵 처리 중(Skipping)이면 새 로드 요청을
        /// 거부한다(경고 로그만 남기고 아무 일도 하지 않음) - 진행 중인 경기 재생 위에 새 코루틴이
        /// 겹쳐 실행되거나, 재생 중이던 경기가 중간에 다른 경기로 조용히 대체되는 것을 막는 안전장치다.
        /// 정상적인 흐름에서는 호출부(PlayBallController.IsMatchInProgress)가 애초에 진입 버튼 연타
        /// 자체를 막아 주므로, 이 가드는 이중 방어선이다.
        /// </summary>
        public void LoadEvents(IEnumerable<PlayEvent> rawEvents)
        {
            if (State == BroadcastPlaybackState.Playing || State == BroadcastPlaybackState.Skipping)
            {
                Debug.LogWarning("[BroadcastUIManager] 이미 재생 중이라 새 LoadEvents 요청을 무시합니다.");
                return;
            }

            eventLog = rawEvents != null ? rawEvents.ToList() : new List<PlayEvent>();
            playbackCursor = 0;

            if (eventLog.Count == 0)
            {
                State = BroadcastPlaybackState.Finished;
                ShowResult();
                return;
            }

            State = BroadcastPlaybackState.Playing;
            playbackHandle = StartCoroutine(PlaybackRoutine());
        }

        /// <summary>
        /// eventLog를 커서 순서대로 하나씩 재생한다. 매 타석 이벤트 사이 perEventDelaySeconds만큼
        /// 대기한다 - 유저 입력을 기다리는 것이 아니라(WaitUntil 없음) 스스로 다음 이벤트로 이어가는
        /// 자동 진행이다. while 조건이 매 반복마다 State를 재확인하므로, RequestSkip()이 State를
        /// Skipping으로 바꾸는 순간 다음 반복에서 스스로 루프를 빠져나간다(레이스 컨디션 방지).
        /// </summary>
        private IEnumerator PlaybackRoutine()
        {
            while (playbackCursor < eventLog.Count && State == BroadcastPlaybackState.Playing)
            {
                PlayOneEvent(eventLog[playbackCursor]);
                playbackCursor++;

                if (playbackCursor < eventLog.Count && State == BroadcastPlaybackState.Playing)
                {
                    yield return new WaitForSeconds(perEventDelaySeconds);
                }
            }

            playbackHandle = null;

            if (State == BroadcastPlaybackState.Playing)
            {
                State = BroadcastPlaybackState.Finished;
                ShowResult();
            }
        }

        /// <summary>
        /// AtBatResult 타입 이벤트만 텍스트 로그를 남긴다. HalfInningEnd/GameEnd 타입은 로그를 남기지
        /// 않는 대신 스코어보드(이닝별 점수)를 갱신한다. RunnerAdvance는 2D 미니맵 애니메이션 전용이라
        /// 이번 범위에서는(로그도, 스코어보드도 건드리지 않고) 조용히 통과한다.
        /// </summary>
        private void PlayOneEvent(PlayEvent evt)
        {
            if (evt == null) return;

            switch (evt.Type)
            {
                case PlayEventType.AtBatResult:
                    if (!string.IsNullOrEmpty(evt.LogMessage))
                    {
                        if (inGameUIController != null) inGameUIController.AddLog(evt.LogMessage);
                        else Debug.Log($"[BroadcastUIManager] {evt.LogMessage}");
                    }
                    break;

                case PlayEventType.HalfInningEnd:
                case PlayEventType.GameEnd:
                    inGameUIController?.RefreshScoreboard();
                    break;
            }
        }

        /// <summary>
        /// 스킵 버튼 OnClick(또는 코드에서 직접 호출). 재생 중(Playing)이 아니면 무시한다(중복 클릭·
        /// 재생 전/종료 후 호출 방어). 진행 중이던 재생 코루틴을 먼저 확실히 멈춘 뒤(StopCoroutine),
        /// 남은 이벤트를 연출(딜레이) 없이 전부 즉시 로그에 반영하고, 그 다음에야 결과 화면으로 전환한다 -
        /// 이 순서를 지켜야 "스킵으로 넘어갔는데 이전 프레임의 재생 결과가 뒤섞여 보이는" 레이스
        /// 컨디션이 생기지 않는다.
        /// </summary>
        public void RequestSkip()
        {
            if (State != BroadcastPlaybackState.Playing) return;

            State = BroadcastPlaybackState.Skipping;

            if (playbackHandle != null)
            {
                StopCoroutine(playbackHandle);
                playbackHandle = null;
            }

            for (; playbackCursor < eventLog.Count; playbackCursor++)
            {
                PlayOneEvent(eventLog[playbackCursor]);
            }

            State = BroadcastPlaybackState.Finished;
            ShowResult();
        }

        /// <summary>InGameUIController.ShowMatchResult()에 위임하고, OnPlaybackFinished를 발생시킨다.
        /// inGameUIController가 미할당이면 결과 패널 표시만 조용히 건너뛴다(AC-03: NullReferenceException
        /// 없이 방어) - OnPlaybackFinished는 그것과 무관하게 항상 발생한다(구독자가 있다면 보상 지급/
        /// 시즌 기록/스케줄 진행 등은 UI 유무와 상관없이 계속 진행되어야 하기 때문이다).</summary>
        private void ShowResult()
        {
            if (inGameUIController != null) inGameUIController.ShowMatchResult(pendingResult);
            OnPlaybackFinished?.Invoke(pendingResult);
        }
    }
}
