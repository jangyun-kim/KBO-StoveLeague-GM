using KBOManager.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// 인게임 화면의 다이아몬드(1루/2루/3루 주자)와 볼카운트(B3/S2/O2)를 실시간으로 보여주는 뼈대 UI.
    ///
    /// [TASK-KBO-046] 과거에는 PlayBallController.OnAtBatEnd를 독립적으로 구독했으나, TASK-KBO-041에서
    /// 재생 경로가 MatchEngine.PlayFullMatchAsEventQueue() 기반으로 전환되며 그 이벤트의 발생부가
    /// 완전히 사라져(TASK-KBO-045 조사) 이 컴포넌트가 좀비 상태가 되었었다. 이제는 구독 방식 대신
    /// BroadcastUIManager.PlayOneEvent()가 재생 중인 PlayEvent를 매번 UpdateStatus()로 직접 밀어
    /// 넣어주는 푸시(push) 방식으로 갱신된다(BroadcastUIManager가 옵션 필드로 이 컴포넌트를 찾아 호출).
    ///
    /// 다만 "새 경기 시작 전 이전 경기의 다이아몬드/카운트 잔상을 지운다"는 InGameUIController.ClearAll()
    /// 시점에 함께 일어나야 자연스러우므로, 그 메서드가 optional 필드로 이 컴포넌트를 참조해
    /// ResetDisplay()만 호출해 준다 - InGameUIController가 다이아몬드의 세부 렌더링 방식은 전혀 몰라도 된다.
    /// </summary>
    public class MatchStatusUI : MonoBehaviour
    {
        [Header("Diamond (1루/2루/3루)")]
        [SerializeField] private Image firstBaseImage;
        [SerializeField] private Image secondBaseImage;
        [SerializeField] private Image thirdBaseImage;
        [SerializeField] private Color runnerOnColor = new Color(1f, 0.84f, 0f);      // 주자 있음 (골드)
        [SerializeField] private Color runnerOffColor = new Color(1f, 1f, 1f, 0.25f); // 주자 없음 (반투명)

        [Header("Count - Balls(3칸)/Strikes(2칸)/Outs(2칸)")]
        [Tooltip("왼쪽부터 1번째~3번째 볼 순서로 정확히 3개를 연결한다.")]
        [SerializeField] private Image[] ballPips = new Image[3];
        [Tooltip("왼쪽부터 1번째~2번째 스트라이크 순서로 정확히 2개를 연결한다.")]
        [SerializeField] private Image[] strikePips = new Image[2];
        [Tooltip("왼쪽부터 1번째~2번째 아웃 순서로 정확히 2개를 연결한다.")]
        [SerializeField] private Image[] outPips = new Image[2];
        [SerializeField] private Color ballOnColor = new Color(0.3f, 0.85f, 0.3f);  // 초록
        [SerializeField] private Color strikeOnColor = new Color(1f, 0.82f, 0.2f);  // 노랑
        [SerializeField] private Color outOnColor = new Color(0.9f, 0.25f, 0.25f);  // 빨강
        [SerializeField] private Color pipOffColor = new Color(1f, 1f, 1f, 0.25f);

        /// <summary>
        /// [TASK-KBO-046] BroadcastUIManager.PlayOneEvent()가 재생 중인 PlayEvent 하나를 그대로
        /// 전달하면 이벤트 타입에 맞춰 필요한 만큼만 갱신한다. PlayEvent는 MatchState 전체 스냅샷이
        /// 아니라 이벤트 타입별로 필요한 값만 담고 있다(Outs는 AtBatResult 타입에만, FromBase/ToBase는
        /// RunnerAdvance 타입에만 유효 - MatchEngine.cs의 PlayEvent 클래스 정의 참고).
        ///
        /// [알려진 제약] PlayEvent는 볼/스트라이크 카운트를 담지 않는다 - MatchEngine이 투구 단위로
        /// 시뮬레이션하지 않고 타석 결과를 한 번에 확정하기 때문이다(MatchEngine.RollPitchCount() 주석:
        /// "state.Balls는 구조적으로만 채워두며 현재 어떤 확률 계산에도 쓰이지 않는다"). 그래서 매
        /// AtBatResult 이벤트마다 볼/스트라이크 핍은 "이번 타석이 새로 시작됐다"는 뜻으로 0개로
        /// 리셋할 뿐, 실제 구 단위 카운트는 표시하지 않는다(이번 작업은 MatchEngine 내부 상태 산출
        /// 로직을 건드리지 않는 어댑터 역할만 수행하므로, PlayEvent에 볼/스트라이크 필드를 새로
        /// 추가하지 않았다 - 완료 보고서 F 섹션에 [결정 필요]로 별도 기록).
        /// </summary>
        public void UpdateStatus(PlayEvent evt)
        {
            if (evt == null) return;

            switch (evt.Type)
            {
                case PlayEventType.AtBatResult:
                    SetPips(outPips, evt.Outs, outOnColor);
                    SetPips(ballPips, 0, ballOnColor);
                    SetPips(strikePips, 0, strikeOnColor);
                    break;

                case PlayEventType.RunnerAdvance:
                    SetBaseOccupied(evt.FromBase, false);
                    SetBaseOccupied(evt.ToBase, true);
                    break;

                case PlayEventType.HalfInningEnd:
                    ResetDisplay();
                    break;
            }
        }

        /// <summary>
        /// 1~3만 실제 베이스(다이아몬드)를 갱신한다. 0(타자석 출발)과 4(득점/생환)는 다이아몬드에
        /// 표시할 베이스가 아니므로 조용히 무시한다.
        /// </summary>
        private void SetBaseOccupied(int baseNumber, bool occupied)
        {
            switch (baseNumber)
            {
                case 1: SetImageColor(firstBaseImage, occupied ? runnerOnColor : runnerOffColor); break;
                case 2: SetImageColor(secondBaseImage, occupied ? runnerOnColor : runnerOffColor); break;
                case 3: SetImageColor(thirdBaseImage, occupied ? runnerOnColor : runnerOffColor); break;
            }
        }

        /// <summary>litCount번째 칸까지는 onColor로 점등하고, 나머지는 pipOffColor로 소등한다.</summary>
        private void SetPips(Image[] pips, int litCount, Color onColor)
        {
            if (pips == null) return;

            for (int i = 0; i < pips.Length; i++)
            {
                SetImageColor(pips[i], i < litCount ? onColor : pipOffColor);
            }
        }

        private static void SetImageColor(Image image, Color color)
        {
            if (image != null) image.color = color;
        }

        /// <summary>새 경기 시작 전 InGameUIController.ClearAll() 등이 호출해 이전 경기의 다이아몬드/카운트 잔상을 지운다.</summary>
        public void ResetDisplay()
        {
            SetImageColor(firstBaseImage, runnerOffColor);
            SetImageColor(secondBaseImage, runnerOffColor);
            SetImageColor(thirdBaseImage, runnerOffColor);

            SetPips(ballPips, 0, ballOnColor);
            SetPips(strikePips, 0, strikeOnColor);
            SetPips(outPips, 0, outOnColor);
        }
    }
}
