using System;

namespace KBOManager.Models
{
    /// <summary>
    /// 타자 세부 스탯. 파워(장타력)/정확(컨택)/선구(눈).
    /// MatchEngine이 타석 결과 확률을 계산할 때 이 3개 스탯을 직접 참조한다.
    /// </summary>
    [Serializable]
    public struct BatterStats
    {
        public int Power;      // 파워: 장타(2루타/3루타/홈런) 확률에 기여
        public int Contact;    // 정확: 삼진 감소, 안타 확률에 기여
        public int Discipline; // 선구: 볼넷 확률 증가, 삼진 감소에 기여

        public BatterStats(int power, int contact, int discipline)
        {
            Power = power;
            Contact = contact;
            Discipline = discipline;
        }

        public static BatterStats operator +(BatterStats a, BatterStats b) =>
            new BatterStats(a.Power + b.Power, a.Contact + b.Contact, a.Discipline + b.Discipline);
    }

    /// <summary>
    /// 투수 세부 스탯. 구위(탈삼진력)/구속(구위와 함께 헛스윙 유도)/변화(범타 유도)/제구(볼넷 억제).
    /// MatchEngine이 타석 결과 확률을 계산할 때 이 4개 스탯을 직접 참조한다.
    /// </summary>
    [Serializable]
    public struct PitcherStats
    {
        public int Stuff;      // 구위: 삼진 확률 증가에 기여
        public int Velocity;   // 구속: 삼진 확률 증가, 장타 억제에 기여
        public int Movement;   // 변화: 범타(땅볼/뜬공) 유도, 장타 억제에 기여
        public int Control;    // 제구: 볼넷 확률 감소에 기여

        public PitcherStats(int stuff, int velocity, int movement, int control)
        {
            Stuff = stuff;
            Velocity = velocity;
            Movement = movement;
            Control = control;
        }

        public static PitcherStats operator +(PitcherStats a, PitcherStats b) =>
            new PitcherStats(a.Stuff + b.Stuff, a.Velocity + b.Velocity, a.Movement + b.Movement, a.Control + b.Control);
    }

    /// <summary>
    /// GDD 5절 리그 진행 단계.
    /// </summary>
    public enum LeaguePhase
    {
        STOVE_LEAGUE,   // 비시즌 (시즌 시작 전)
        REGULAR_OPEN,   // 1~72경기: 로스터 자유 편성
        REGULAR_LOCKED, // 73~144경기: 트레이드 마감, 로스터 스냅샷 고정
        POST_PREP,      // 정규 종료, 포스트시즌 진출 시 로스터 락 해제
        POST_SEASON     // 가을야구 (수동 개입)
    }

    /// <summary>
    /// 선수 명함(카드) 등급. GDD v4.0 기준 8단계. 가장 낮은 SEASON이 신설되었고(강화만 가능/각성 불가 -
    /// cards.csv의 CRD_0001 SEASON 샘플이 max_awaken=0인 것과 일치), 나머지 7종의 상대적 희귀도 순서는
    /// v3.1과 동일하게 유지한다(GOLDEN_GLOVE(골드 5성) &lt; SIGNATURE(플래티넘 6성) - ScoutManager의
    /// GradeDropRate 확률표(GOLDEN_GLOVE 3% &gt; SIGNATURE 1.5%)와 어긋나지 않도록).
    ///
    /// [중요] SaveManager는 JsonUtility로 직렬화하며, JsonUtility는 enum을 이름이 아닌 "정수 ordinal"로
    /// 저장한다. 따라서 기존 7종(LIVE_NORMAL=0 ~ DYNASTY=6) 각각에 v3.1 시절과 동일한 정수값을 명시적으로
    /// 고정해 두어야 기존 세이브 파일의 등급이 깨지지 않는다(예: 값 고정 없이 SEASON을 맨 앞에 추가했다면
    /// 기존 세이브의 LIVE_NORMAL(0)이 로드 시 SEASON(0)으로 잘못 해석되었을 것). SEASON은 기존에 없던
    /// 값이므로 안전하게 새 번호(7)를 받는다.
    /// </summary>
    public enum Grade
    {
        LIVE_NORMAL = 0,    // 라이브 일반 카드 (1~3성, 강화만 가능/각성 불가)
        LIVE_EPIC = 1,      // 라이브 에픽 카드 (4성, 강화만 가능/각성 불가)
        ALLSTAR = 2,        // 올스타 (보라 4성)
        TITLE_HOLDER = 3,   // 타이틀 홀더 (실버 5성)
        GOLDEN_GLOVE = 4,   // 골든 글러브 (골드 5성)
        SIGNATURE = 5,      // 시그니처 (플래티넘 6성)
        DYNASTY = 6,        // 왕조 (구단색 6성)
        SEASON = 7          // 시즌 카드 (기본/무등급, 강화만 가능/각성 불가) - v4.0 신설, 기존 세이브 호환을 위해 마지막 번호로 배정
    }

    /// <summary>
    /// 등급에 대응하는 카드의 특수 별(성급) 시각화 타입.
    /// </summary>
    public enum StarType
    {
        NORMAL,     // LIVE_NORMAL / LIVE_EPIC
        PURPLE,     // ALLSTAR
        SILVER,     // TITLE_HOLDER
        GOLD,       // GOLDEN_GLOVE
        PLATINUM,   // SIGNATURE
        TEAM_COLOR  // DYNASTY
    }

    /// <summary>
    /// 보유 스킬 등급.
    /// </summary>
    public enum SkillTier
    {
        S_PLUS,
        S,
        A,
        B,
        C,
        D,
        F
    }

    /// <summary>
    /// 하루 단위로 오르내리는 선수 컨디션(5단계). enum 선언 순서가 곧 등급 순서라 Player.ShiftCondition()이
    /// (int)current ± 1로 인접 단계만 이동시킬 수 있다 - 순서를 바꾸면 그 로직도 함께 깨진다.
    /// </summary>
    public enum PlayerCondition
    {
        Poor,        // 열악
        BelowAverage,// 저조
        Normal,      // 보통
        Good,        // 호조
        Excellent    // 최상
    }

    /// <summary>
    /// 타자 선발 포지션 (9자리).
    /// </summary>
    public enum BatterPosition
    {
        Catcher,            // 포수 C
        FirstBase,          // 1루수
        SecondBase,         // 2루수
        ThirdBase,          // 3루수
        ShortStop,          // 유격수
        LeftField,          // 좌익수
        CenterField,        // 중견수
        RightField,         // 우익수
        DesignatedHitter    // 지명타자
    }

    /// <summary>
    /// 투수 롤 (5자리). 선발 1~5선발은 동일 SP 롤을 순번으로 편성한다.
    /// </summary>
    public enum PitcherRole
    {
        StartingPitcher,    // 선발 (1~5선발)
        WinningReliever,    // 승리조
        MopUpReliever,      // 추격조
        LongReliever,       // 롱릴리프
        Closer              // 마무리
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
}
