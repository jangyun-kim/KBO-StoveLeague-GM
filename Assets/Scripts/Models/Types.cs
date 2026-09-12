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
    /// 선수 명함(카드) 등급. GDD v4.0/04_card_grade_policy.md 확정 서열(SEASON=0 ~ DYNASTY=7)과
    /// 정수값을 완전히 일치시킨다(TASK-KBO-032-IMPLEMENT). 이렇게 하면 (int)Grade 캐스팅 값이 곧
    /// 등급의 랭크(희귀도)가 되어, ScoutManager.RollGradeAtLeast()의 등급 대소 비교(&gt;=)가 별도의
    /// 랭크 테이블 없이도 안전하게 성립한다.
    ///
    /// [TASK-KBO-032 사전 조사 결과] SaveManager는 Grade 자체를 JSON에 저장하지 않는다(유저 카드는
    /// TemplateId로 PlayerTemplate 원본을 다시 찾아 붙이는 구조) - 따라서 이 정수값 재배치는 기존 JSON
    /// 세이브 파일을 깨뜨리지 않는다(TASK-KBO-031 당시의 "세이브 호환을 위해 SEASON=7 고정" 판단은
    /// 이 조사로 전제가 사라져 철회됨).
    ///
    /// [Inspector 직렬화 주의] 단, Grade가 [SerializeField]로 노출된 곳(PlayerTemplate.Grade,
    /// ScoutManager.gradeDropRates, ShopUIController의 guaranteedMinimumGrade/premiumTenPullMinimumGrade,
    /// SeasonRewardManager의 championGuaranteedGrade/lastPlaceGuaranteedGrade)은 유니티가 정수값으로
    /// 직렬화하므로, 이 재배치 이전에 씬/프리팹/.asset에 이미 값을 지정해 둔 로컬 작업이 있다면 그
    /// Inspector 값이 엉뚱한 등급을 가리키게 될 수 있다 - 재배치 후 반드시 각 Inspector 값을 다시
    /// 확인/재지정할 것.
    /// </summary>
    public enum Grade
    {
        SEASON = 0,         // 시즌 카드 (기본/무등급, 강화만 가능/각성 불가)
        LIVE_NORMAL = 1,    // 라이브 일반 카드 (1~3성, 강화만 가능/각성 불가)
        LIVE_EPIC = 2,      // 라이브 에픽 카드 (4성, 강화만 가능/각성 불가)
        ALLSTAR = 3,        // 올스타 (보라 4성)
        TITLE_HOLDER = 4,   // 타이틀 홀더 (실버 5성)
        SIGNATURE = 5,      // 시그니처 (플래티넘 6성)
        GOLDEN_GLOVE = 6,   // 골든 글러브 (골드 5성)
        DYNASTY = 7         // 왕조 (구단색 6성)
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
