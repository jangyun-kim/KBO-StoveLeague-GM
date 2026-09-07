using System;

namespace KBOManager.Models
{
    /// <summary>
    /// 선수 명함(카드) 등급. 등급에 따라 초기 성급과 최대 잠재력이 달라진다.
    /// </summary>
    public enum Grade
    {
        LIVE,           // 기본 등급
        ALLSTAR,        // 올스타 등급
        GOLDEN_GLOVE,   // 골든글러브 등급
        SIGNATURE       // 시그니처 등급 (최상위)
    }

    /// <summary>
    /// 일반 성급(1~6성)을 모두 채운 이후 진입하는 특수 별 타입.
    /// </summary>
    public enum StarType
    {
        NORMAL, // 특수 진화 없음 (일반 1~6성 상태)
        GOLD,   // 골드 스타 진화
        RAINBOW // 레인보우 스타 진화 (최종 진화)
    }

    /// <summary>
    /// 선수 포지션.
    /// </summary>
    public enum Position
    {
        Pitcher,
        Catcher,
        FirstBase,
        SecondBase,
        ThirdBase,
        ShortStop,
        LeftField,
        CenterField,
        RightField,
        DesignatedHitter
    }

    /// <summary>
    /// KBO 리그 소속 구단.
    /// </summary>
    public enum Team
    {
        None,     // 무소속 / 미지정
        Doosan,
        LG,
        KT,
        SSG,
        NC,
        Kiwoom,
        KIA,
        Samsung,
        Lotte,
        Hanwha
    }

    /// <summary>
    /// 선수 1명의 데이터 모델. MonoBehaviour를 상속하지 않는 순수 데이터/로직 클래스이며,
    /// PlayerDatabase의 템플릿, 혹은 유저가 실제로 보유한 카드 인스턴스로 함께 사용된다.
    /// </summary>
    [Serializable]
    public class Player
    {
        // ----- 고유 정보 (변하지 않는 원본 데이터) -----
        public string PlayerId;      // 유저가 보유한 개별 카드 인스턴스의 고유 ID (GUID)
        public int TemplateId;       // PlayerDatabase 상의 원본 템플릿 ID
        public string Name;          // 선수 이름
        public Position Position;    // 포지션
        public Team Team;            // 소속 구단
        public Grade Grade;          // 카드 등급
        public int BaseOverall;      // 1성 기준 기본 오버롤
        public int Cost;             // 라인업 편성 코스트

        // ----- 육성 상태 (유저의 진행에 따라 변하는 데이터) -----
        public int StarLevel;        // 현재 일반 성급 (1~6)
        public StarType StarType;    // 특수 별 타입 (6성 달성 후 진화 가능)

        public const int MaxNormalStarLevel = 6;

        public Player() { }

        public Player(string playerId, int templateId, string name, Position position, Team team,
            Grade grade, int baseOverall, int cost, int starLevel, StarType starType)
        {
            PlayerId = playerId;
            TemplateId = templateId;
            Name = name;
            Position = position;
            Team = team;
            Grade = grade;
            BaseOverall = baseOverall;
            Cost = cost;
            StarLevel = starLevel;
            StarType = starType;
        }

        /// <summary>
        /// 성급 및 특수 별 타입이 반영된 현재 오버롤.
        /// 1성 대비 성급 1당 +2, 특수 별 타입 진화 시 추가 보너스가 붙는다.
        /// </summary>
        public int GetCurrentOverall()
        {
            int starBonus = (StarLevel - 1) * 2;
            int starTypeBonus = StarType switch
            {
                StarType.GOLD => 10,
                StarType.RAINBOW => 25,
                _ => 0
            };
            return BaseOverall + starBonus + starTypeBonus;
        }

        /// <summary>
        /// 반환값이 담긴 얕은 복제본을 생성한다. (스카우트/뽑기 등으로 신규 인스턴스 발급 시 사용)
        /// </summary>
        public Player Clone(string newPlayerId)
        {
            return new Player(newPlayerId, TemplateId, Name, Position, Team, Grade, BaseOverall, Cost, StarLevel, StarType);
        }
    }
}
