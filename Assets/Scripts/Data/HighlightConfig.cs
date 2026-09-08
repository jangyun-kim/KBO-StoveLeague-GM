using UnityEngine;

namespace KBOManager.Data
{
    /// <summary>
    /// 하이라이트 모드가 시뮬레이션을 멈추는 조건들을 인스펙터에서 튜닝할 수 있도록 분리한 데이터 테이블.
    /// PlayBallController는 이 에셋의 값을 읽어 표준 3종 트리거(득점권/후반 접전/끝내기 위기)를 구성한다.
    /// 에셋을 연결하지 않으면 아래 기본값(모두 활성화, 기본 임계값)으로 동작한다.
    /// </summary>
    [CreateAssetMenu(fileName = "HighlightConfig", menuName = "KBO Manager/Highlight Config", order = 6)]
    public class HighlightConfig : ScriptableObject
    {
        [Header("득점권 주자 (Scoring Position)")]
        [Tooltip("2루 이상 주자가 있을 때 멈춘다.")]
        public bool enableScoringPosition = true;

        [Header("후반 접전 (Late Inning Close Game)")]
        [Tooltip("이 이닝 이후부터 '후반'으로 판단한다.")]
        [Min(1)] public int lateInningThreshold = 7;
        [Tooltip("점수차가 이 값 이하이면 '접전'으로 판단한다.")]
        [Min(0)] public int closeGameScoreMargin = 3;
        public bool enableLateInningCloseGame = true;

        [Header("끝내기 위기 (Walk-Off Danger)")]
        [Tooltip("이 이닝(보통 9회) 이후 말 공격에서, 홈이 동점이거나 뒤지고 있으면 멈춘다.")]
        [Min(1)] public int walkOffRegulationInnings = 9;
        public bool enableWalkOffDanger = true;
    }
}
