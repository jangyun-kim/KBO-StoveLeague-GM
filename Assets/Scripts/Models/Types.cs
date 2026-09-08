namespace KBOManager.Models
{
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
