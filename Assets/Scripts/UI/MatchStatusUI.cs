using KBOManager.Controllers;
using KBOManager.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// 인게임 화면의 다이아몬드(1루/2루/3루 주자)와 볼카운트(B3/S2/O2)를 실시간으로 보여주는 뼈대 UI.
    /// PlayBallController.OnAtBatEnd를 InGameUIController와 나란히 독립적으로 구독한다 - 이미
    /// MatchRewardManager가 같은 방식으로 OnMatchCompleted를 구독하고 있는 것과 동일한 패턴이라,
    /// InGameUIController를 고치지 않고도 이 컴포넌트를 씬에 추가하기만 하면 동작한다.
    ///
    /// 다만 "새 경기 시작 전 이전 경기의 다이아몬드/카운트 잔상을 지운다"는 InGameUIController.ClearAll()
    /// 시점에 함께 일어나야 자연스러우므로, 그 메서드가 optional 필드로 이 컴포넌트를 참조해
    /// ResetDisplay()만 호출해 준다 - InGameUIController가 다이아몬드의 세부 렌더링 방식은 전혀 몰라도 된다.
    /// </summary>
    public class MatchStatusUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayBallController playBallController;

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

        private void OnEnable()
        {
            if (playBallController != null) playBallController.OnAtBatEnd += HandleAtBatEnd;
        }

        private void OnDisable()
        {
            if (playBallController != null) playBallController.OnAtBatEnd -= HandleAtBatEnd;
        }

        private void HandleAtBatEnd(AtBatStepResult step)
        {
            if (step?.State == null) return;
            Render(step.State);
        }

        /// <summary>MatchState 하나(주자/볼/스트라이크/아웃)를 받아 다이아몬드/카운트 UI 전체를 다시 그린다.</summary>
        private void Render(MatchState state)
        {
            SetImageColor(firstBaseImage, state.RunnerOnFirst ? runnerOnColor : runnerOffColor);
            SetImageColor(secondBaseImage, state.RunnerOnSecond ? runnerOnColor : runnerOffColor);
            SetImageColor(thirdBaseImage, state.RunnerOnThird ? runnerOnColor : runnerOffColor);

            SetPips(ballPips, state.Balls, ballOnColor);
            SetPips(strikePips, state.Strikes, strikeOnColor);
            SetPips(outPips, state.Outs, outOnColor);
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
