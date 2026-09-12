using System;

namespace KBOManager.Models
{
    /// <summary>
    /// 치어리더 등급. 선수 카드 등급(Types.cs의 Grade enum - GDD 문서/코드 주석에서는 종종
    /// "PlayerGrade"로도 불린다)과는 완전히 분리된 별도 체계이며, 서로 혼용하지 않는다.
    ///
    /// [TBD] v0.1 시점에는 치어리더 획득 방식/등급 서열/개수가 기획 확정되지 않아 개발·테스트용
    /// 값만 우선 둔다. 실제 등급 체계(단계 수, 명칭, 등급별 버프 수치 밸런스 등)는 후속 작업에서
    /// 확정되는 대로 이 enum에 값을 추가/재배치한다(Grade enum이 JsonUtility 정수 직렬화 이슈로
    /// 정수값을 명시적으로 고정했던 선례 - docs/13_decision_change_log.md DCL-006 - 를 참고할 것.
    /// 단, 이번 작업은 세이브/로드 대상이 아니므로 TASK-KBO-048 시점에는 직렬화 안정성 이슈가
    /// 아직 발생하지 않는다).
    /// </summary>
    public enum CheerleaderGrade
    {
        NONE = 0,
        TEST = 1,
    }

    /// <summary>
    /// 치어리더 1명의 데이터. [TASK-KBO-048] TASK-KBO-039에서 마련된 B+C 하이브리드 버프 인프라
    /// (Engine.TeamPowerModifiers의 ConditionBuff/ClutchMultiplier)에 실제 값을 채워 넣는 소스다.
    ///
    /// 이 클래스가 들고 있는 두 버프 필드는 "경기 조건부 적용 전력(MatchConditionModifier)"이다 -
    /// 즉 Player.CalculateOVR()/GameManager.CalculateTeamOVR()(영구·표시용 구단 OVR)에는 절대
    /// 반영되지 않으며, 오직 경기가 시작되는 순간에만 각 매니저의 BuildTeamPowerModifiers()류
    /// 헬퍼가 이 값을 읽어 Engine.TeamPowerModifiers에 실어 MatchEngine에 일회성으로 전달한다
    /// (15_team_power_policy.md 2절 "③ 경기 적용 전력" 참고). 선수 스탯 원본이나 저장 데이터를
    /// 오염시키는 경로가 아니다.
    /// </summary>
    [Serializable]
    public class Cheerleader
    {
        public string InstanceId;
        public string Name;
        public CheerleaderGrade Grade = CheerleaderGrade.NONE;

        /// <summary>
        /// [TASK-KBO-048] 유저 팀의 홈 경기에서만(BuildTeamPowerModifiers 계열 호출부가 판별) 기본
        /// 홈 어드밴티지(Engine.TeamPowerModifiers.HomeAdvantageConditionBuff, +2)에 가산되는
        /// 조건부 보정치다. Player.FinalOVR이나 GameManager.CalculateTeamOVR()(영구·표시용 구단
        /// OVR)에는 절대 반영되지 않는다 - 경기 시작 시점에만 MatchEngine으로 일회성 전달되는
        /// "경기 조건부 적용 전력(MatchConditionModifier)"이다.
        /// </summary>
        public int ConditionBuff;

        /// <summary>
        /// [TASK-KBO-048] 득점권 상황에서 타자 긍정 결과(볼넷/안타/2루타/3루타/홈런) 가중치에 곱해지는
        /// 배율(Engine.TeamPowerModifiers.ClutchMultiplier에 전달 - 적용 지점은
        /// 15_team_power_policy.md 6절 참고). 기본/중립값은 1.0f(효과 없음)다.
        ///
        /// 이 값 역시 선수 스탯이나 영구 OVR에는 전혀 반영되지 않는 일회성 경기 조건부 값이며,
        /// 호출부(GameManager.ResolveCheerleaderClutchMultiplier())가 MatchEngine에 전달하기
        /// 직전 반드시 0 이하/NaN/Infinity 등 비정상 값을 1.0f로 정규화(Sanitize)한다.
        ///
        /// [TBD] 코치 등 다른 시너지와 중첩될 때의 합산 방식이나 상한선은 아직 기획 확정 전이다.
        /// </summary>
        public float ClutchMultiplier = 1.0f;

        /// <summary>
        /// [TASK-KBO-049] B안(상시 경제 효과) - 유저 팀이 "홈 경기에서 승리"했을 때 경기 보상(재화)에
        /// 곱해지는 배율. 기본/중립값은 1.0f(효과 없음). 위 ConditionBuff/ClutchMultiplier(A/C안)와
        /// 달리 MatchEngine에는 전혀 전달되지 않는다 - 시뮬레이션이 아니라 "스토브리그 로비 연산"
        /// (경기 결산 단계)에서만 쓰이는 값이며, 적용 지점은 MatchRewardManager.GrantRewardForMatch()
        /// 참고(15_team_power_policy.md 7절).
        /// </summary>
        public float EconomicBonusRate = 1.0f;

        /// <summary>
        /// [TASK-KBO-049] B안(상시 멘탈 효과) - 유저 팀이 연패했을 때 팬심(GameManager.FanSentiment)
        /// 하락폭을 방어하는 수치. 기본/중립값은 0(방어 없음). MatchRewardManager가
        /// `Mathf.Max(0, 기본 하락치 - SentimentDefense)`로 적용해, 방어가 하락폭을 초과해도 팬심이
        /// 오히려 상승하는 일이 없도록 클램핑한다. 위 두 필드와 마찬가지로 MatchEngine에는 전달되지
        /// 않으며, 오직 경기 결산 단계에서만 쓰인다.
        /// </summary>
        public int SentimentDefense = 0;

        public Cheerleader() { }

        public Cheerleader(string instanceId, string name, CheerleaderGrade grade, int conditionBuff, float clutchMultiplier,
            float economicBonusRate = 1.0f, int sentimentDefense = 0)
        {
            InstanceId = instanceId;
            Name = name;
            Grade = grade;
            ConditionBuff = conditionBuff;
            ClutchMultiplier = clutchMultiplier;
            EconomicBonusRate = economicBonusRate;
            SentimentDefense = sentimentDefense;
        }
    }
}
