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
    /// 선수 명함(카드) 등급. GDD v3.1 기준 7단계.
    /// </summary>
    public enum Grade
    {
        LIVE_NORMAL,    // 라이브 일반 카드 (1~3성, 강화만 가능/각성 불가)
        LIVE_EPIC,      // 라이브 에픽 카드 (4성, 강화만 가능/각성 불가)
        ALLSTAR,        // 올스타 (보라 4성)
        TITLE_HOLDER,   // 타이틀 홀더 (실버 5성)
        GOLDEN_GLOVE,   // 골든 글러브 (골드 5성)
        SIGNATURE,      // 시그니처 (플래티넘 6성)
        DYNASTY         // 왕조 (구단색 6성)
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
