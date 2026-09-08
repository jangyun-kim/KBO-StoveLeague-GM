using UnityEngine;
using KBOManager.Engine;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 하이라이트 모드에서 시뮬레이션을 멈추고 유저에게 제어권을 넘길지 판단하는 트리거.
    /// PlayBallController는 이 인터페이스의 구현체 목록만 순회하므로, 새 트리거를 추가할 때
    /// PlayBallController.cs를 건드릴 필요 없이 이 인터페이스를 구현한 클래스를 하나 더 만들고
    /// PlayBallController.HighlightConditions 리스트에 등록하기만 하면 된다(개방-폐쇄 원칙).
    /// </summary>
    public interface IHighlightCondition
    {
        /// <summary>로그/디버깅에 표시할 트리거 이름.</summary>
        string Name { get; }

        /// <summary>이 타석 결과 직후 시뮬레이션을 멈춰야 하면 true.</summary>
        bool ShouldPause(AtBatStepResult step);
    }

    /// <summary>득점권(2루 이상) 주자가 있는 상황.</summary>
    public class ScoringPositionCondition : IHighlightCondition
    {
        public string Name => "득점권 주자";

        public bool ShouldPause(AtBatStepResult step)
        {
            return step?.State != null && step.State.HasRunnerInScoringPosition;
        }
    }

    /// <summary>지정한 이닝 이후(기본 7회) + 점수차가 지정한 폭(기본 3점) 이내인 접전 상황.</summary>
    public class LateInningCloseGameCondition : IHighlightCondition
    {
        private readonly int inningThreshold;
        private readonly int scoreMarginThreshold;

        public LateInningCloseGameCondition(int inningThreshold = 7, int scoreMarginThreshold = 3)
        {
            this.inningThreshold = inningThreshold;
            this.scoreMarginThreshold = scoreMarginThreshold;
        }

        public string Name => "후반 접전";

        public bool ShouldPause(AtBatStepResult step)
        {
            if (step?.State == null) return false;
            if (step.State.Inning < inningThreshold) return false;

            int margin = Mathf.Abs(step.HomeScore - step.AwayScore);
            return margin <= scoreMarginThreshold;
        }
    }

    /// <summary>9회말 이후, 홈이 동점이거나 뒤지고 있어 이 타석이 곧바로 승부를 가를 수 있는 상황.</summary>
    public class WalkOffDangerCondition : IHighlightCondition
    {
        private readonly int regulationInnings;

        public WalkOffDangerCondition(int regulationInnings = 9)
        {
            this.regulationInnings = regulationInnings;
        }

        public string Name => "끝내기 위기";

        public bool ShouldPause(AtBatStepResult step)
        {
            if (step?.State == null) return false;
            if (step.IsTopHalf) return false; // 말(홈 공격) 상황에서만 의미가 있음
            if (step.State.Inning < regulationInnings) return false;

            return step.AwayScore >= step.HomeScore; // 홈이 동점 또는 열세 -> 이 타석이 동점/역전을 만들 수 있음
        }
    }
}
