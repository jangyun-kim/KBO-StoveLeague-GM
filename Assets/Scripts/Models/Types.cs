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
        public int Speed;      // [TASK-KBO-088] 주력: players.csv의 z_speed(타자) 대응 필드. 현재 MatchEngine은
                                // 아직 이 필드를 참조하지 않는다(스코프 밖).
        public int Defense;    // [TASK-KBO-088] 수비: players.csv의 z_def 대응 필드. 현재 MatchEngine은 아직
                                // 이 필드를 참조하지 않는다(스코프 밖).

        // [TASK-KBO-088] 기존 3-인자 호출부(MatchEngine/Player/LeagueManager/OnboardingManager 등)를 깨뜨리지
        // 않기 위해 유지 - Speed/Defense는 0으로 초기화된다. 두 필드를 실제로 채우려면 아래 5-인자 생성자를
        // 명시적으로 호출해야 한다.
        public BatterStats(int power, int contact, int discipline) : this(power, contact, discipline, 0, 0)
        {
        }

        public BatterStats(int power, int contact, int discipline, int speed, int defense)
        {
            Power = power;
            Contact = contact;
            Discipline = discipline;
            Speed = speed;
            Defense = defense;
        }

        public static BatterStats operator +(BatterStats a, BatterStats b) =>
            new BatterStats(a.Power + b.Power, a.Contact + b.Contact, a.Discipline + b.Discipline,
                a.Speed + b.Speed, a.Defense + b.Defense);
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
        public int Stamina;    // [TASK-KBO-088] 체력: players.csv의 z_stamina 대응 필드. Player.MaxStamina(롤
                                // 기준 상수)와는 별개이며, 현재 MatchEngine은 아직 이 필드를 참조하지 않는다(스코프 밖).

        // [TASK-KBO-088] 기존 4-인자 호출부(MatchEngine/Player/LeagueManager/OnboardingManager 등)를 깨뜨리지
        // 않기 위해 유지 - Stamina는 0으로 초기화된다. 이 필드를 실제로 채우려면 아래 5-인자 생성자를 명시적으로
        // 호출해야 한다.
        public PitcherStats(int stuff, int velocity, int movement, int control) : this(stuff, velocity, movement, control, 0)
        {
        }

        public PitcherStats(int stuff, int velocity, int movement, int control, int stamina)
        {
            Stuff = stuff;
            Velocity = velocity;
            Movement = movement;
            Control = control;
            Stamina = stamina;
        }

        public static PitcherStats operator +(PitcherStats a, PitcherStats b) =>
            new PitcherStats(a.Stuff + b.Stuff, a.Velocity + b.Velocity, a.Movement + b.Movement, a.Control + b.Control,
                a.Stamina + b.Stamina);
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
    /// 선수 명함(카드) 등급. GDD v4.0/04_card_grade_policy.md 확정 서열과 정수값을 완전히
    /// 일치시킨다(TASK-KBO-032-IMPLEMENT). 이렇게 하면 (int)Grade 캐스팅 값이 곧 등급의 랭크
    /// (희귀도)가 되어, ScoutManager.RollGradeAtLeast()의 등급 대소 비교(&gt;=)와
    /// UpgradeConstants.GetMaterialExp()의 rankDelta 계산이 별도의 랭크 테이블 없이도 안전하게
    /// 성립한다.
    ///
    /// [TASK-KBO-032 사전 조사 결과] SaveManager는 Grade 자체를 JSON에 저장하지 않는다(유저 카드는
    /// TemplateId로 PlayerTemplate 원본을 다시 찾아 붙이는 구조) - 따라서 이 정수값 재배치는 기존 JSON
    /// 세이브 파일을 깨뜨리지 않는다.
    ///
    /// [TASK-KBO-155, 사용자 직접 지시 - 등급 체계 재설계] `SEASON`을 전면 삭제하고(0번 값은 영구
    /// 결번 처리 - 재사용하지 않는다), "역대 우승 주역" `DYNASTY`와 성격이 비슷한 신규 등급
    /// `RETIRED_NUMBER`(영구결번)를 `TITLE_HOLDER`와 `SIGNATURE` 사이(랭크상 그 중간 성능)에
    /// 끼워 넣었다 - "(int)Grade = 랭크"라는 위 핵심 불변식을 지키려면 `SIGNATURE`/`GOLDEN_GLOVE`/
    /// `DYNASTY`의 정수값을 부득이 한 칸씩 밀어야 했다(단순히 맨 끝에 추가하면 `RETIRED_NUMBER`가
    /// `DYNASTY`보다도 랭크가 높아지는 모순이 생긴다).
    ///
    /// [Inspector 직렬화 주의, 재확인 필요] Grade가 [SerializeField]로 노출된 곳
    /// (`ScoutManager.gradeDropRates`, `SeasonRewardManager`의 `championGuaranteedGrade`/
    /// `lastPlaceGuaranteedGrade`)은 유니티가 정수값으로 직렬화하므로, 이 재배치 이전에 씬/프리팹에
    /// 이미 값을 지정해 둔 로컬 작업이 있다면 그 Inspector 값이 엉뚱한 등급을 가리키게 될 수 있다 -
    /// 재배치 후 반드시 각 Inspector 값을 다시 확인/재지정할 것(`ShopUIController`는 더 이상 Grade
    /// 필드를 갖고 있지 않아 목록에서 제외했다 - 과거 리팩토링으로 없어진 필드를 이 주석만 남아 있던
    /// 것을 이번에 발견해 정정).
    /// </summary>
    public enum Grade
    {
        LIVE_NORMAL = 1,    // 라이브 일반 카드 (1~3성, 강화만 가능/각성 불가)
        LIVE_EPIC = 2,      // 라이브 에픽 카드 (4성, 강화만 가능/각성 불가)
        ALLSTAR = 3,        // 올스타 (보라 4성)
        TITLE_HOLDER = 4,   // 타이틀 홀더 (실버 5성)
        RETIRED_NUMBER = 5, // [TASK-KBO-155 신설] 영구결번 (검정 5성 - TITLE_HOLDER~SIGNATURE 중간 성능)
        SIGNATURE = 6,      // 시그니처 (플래티넘 6성)
        GOLDEN_GLOVE = 7,   // 골든 글러브 (골드 5성)
        DYNASTY = 8         // 왕조 (구단색 6성)
    }

    /// <summary>
    /// 등급에 대응하는 카드의 특수 별(성급) 시각화 타입.
    /// [TASK-KBO-155] `BLACK`을 끝에 추가했다(영구결번 전용 - 실제 KBO 구단들이 영구결번 현수막을
    /// 검은 바탕에 금색 글씨로 게시하는 관례를 참고). Grade와 달리 이 enum은 정수 랭크로 쓰이지
    /// 않고 색상 매핑 전용이라, 기존 값 순서를 건드리지 않고 끝에만 추가해도 안전하다.
    /// </summary>
    public enum StarType
    {
        NORMAL,     // LIVE_NORMAL / LIVE_EPIC
        PURPLE,     // ALLSTAR
        SILVER,     // TITLE_HOLDER
        GOLD,       // GOLDEN_GLOVE
        PLATINUM,   // SIGNATURE
        TEAM_COLOR, // DYNASTY
        BLACK       // RETIRED_NUMBER (영구결번)
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
