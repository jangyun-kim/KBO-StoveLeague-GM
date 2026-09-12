using System;

namespace KBOManager.Broadcast.Data
{
    /// <summary>
    /// cards.csv 한 행에 대응하는 원시 데이터 클래스. 한 선수(PlayerModel)는 여러 등급의 카드를
    /// 가질 수 있다(PlayerId로 연결). GradeId를 C# enum으로 고정하지 않고 int로 보관하는 것이
    /// 핵심 설계 결정이다 - 현재 CSV에는 0(SEASON)/2(LIVE_EPIC)/6(GOLDEN_GLOVE)만 있지만,
    /// 기획자가 나중에 1/3/4/5/7번 등급 행을 CSV에 추가해도 DataManager 파싱이나 이 클래스
    /// 정의를 코드 수정 없이 그대로 통과한다 - enum이었다면 새 값마다 코드 배포가 필요했을 것.
    /// </summary>
    [Serializable]
    public class CardModel
    {
        public string CardId;
        public string PlayerId;
        public int GradeId;
        public string GradeName;
        public int BaseOvr;
        public int SalaryCost;
        public int MaxEnhance;
        public int MaxAwaken;
        public bool IsDroppable;
    }
}
