using System;

namespace KBOManager.Broadcast.Data
{
    /// <summary>
    /// match_event_rates.csv 한 행에 대응하는 원시 데이터 클래스. 1타석 결과 후보(삼진/뜬공/안타/
    /// 홈런 등) 각각의 기본 가중치와, 타자-투수 OVR 차이가 그 가중치를 얼마나 밀어붙이는지를
    /// 정의한다. BroadcastMatchEngine이 이 테이블을 그대로 순회하며 확률을 계산하므로,
    /// 기획자가 새 이벤트 행을 추가/삭제해도 엔진 코드 수정 없이 결과 후보군이 바뀐다.
    /// </summary>
    [Serializable]
    public class MatchEventModel
    {
        public string EventId;
        public string EventName;
        public string Category; // OUT / HIT / BASE 등 - 문자열로 보관해 새 카테고리 추가에도 안전
        public float BaseWeight;
        public float OvrDiffModifier;
        public string Description;
    }
}
