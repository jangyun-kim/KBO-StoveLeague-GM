namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-172 신설] 카드 등급별 "각성 한계 / 초월 가능 여부 / 개인 세트덱 스코어 / 실전 성장 상한"의
    /// 단일 진실 공급원(SSOT). docs/04_card_grade_policy.md 4~5절, docs/기획 고도화 자료.pdf "[각성 시스템
    /// 수정]"/"선수 개인 기본/최대 스코어", 그리고 명령서 STEP 2의 확정 규격을 코드로 옮긴 것이다.
    ///
    /// 각성 체계: 1~9각 + 초월. 기존 "10각"을 "초월"로 명칭만 바꿨으므로 내부 저장값은 그대로
    /// `Player.AwakenLevel == 10`이 초월이다(세이브 파일 하위 호환 - 기존 10각 카드는 초월로 해석되며,
    /// 초월 불가 등급의 10각은 9각으로 클램프되어 계산된다).
    ///
    /// 개인 세트덱 스코어 = 등급 기본 스코어 + 3각(+1) + 6각(+1) + 9각(+1) + 초월(+1, 초월 가능 등급만).
    ///
    /// [TASK-KBO-173 역전 밸런스] "카드 성능(OVR)이 높을수록 개인 스코어는 낮게, LIVE는 가장 높게"로 뒤집었다 -
    /// 27인 최대 목표 200P(1인 평균 7.4)에서 종결 카드(DYN/GG/SIG)만으로 도배하면 핵심 버프 구간에 닿지 못하게
    /// 하는 것이 목적이다(왕조 13 + 골글 13 + 시그 1 풀초월 = 148P).
    ///   LIVE(LIVE_NORMAL/LIVE_EPIC) 4 → 초월 8 | ALLSTAR 4 → 9각 7 | FRANCHISE 3 → 9각 6
    ///   TITLE_HOLDER 3 → 9각 6 | RETIRED_NUMBER 2 → 9각 5(잠정) | GOLDEN_GLOVE 2 → 초월 6
    ///   SIGNATURE 1 → 초월 5 | DYNASTY 1 → 초월 5
    ///   RETIRED_NUMBER는 규격 미정이라 "성능 TH~SIG 사이 → 스코어도 TH(3)~SIG(1) 사이"라는 역전 원칙으로 2를
    ///   잠정 적용한다(DCL-144 결정 필요 항목).
    ///
    /// [TASK-KBO-173 Salary 일원화] 이 기본 스코어가 곧 카드의 "Salary"다 - cards_*.csv의 salary_cost 컬럼은
    /// BaseSetDeckScore()와 같은 값으로 생성되며, 별도 샐러리 캡(구 Player.CalculateSalaryCost / RosterManager
    /// EnforceSalaryCap)은 폐기됐다. 도배 방지는 이 역전 스코어 + 27인 세트덱 편성 규칙이 전담한다.
    /// </summary>
    public static class CardGrowthRules
    {
        /// <summary>초월 불가 등급의 각성 한계(9각).</summary>
        public const int NineStageAwakenCap = 9;

        /// <summary>초월 단계의 내부 AwakenLevel 값(= 구 10각). `Player.MaxAwakenLevel`과 같다.</summary>
        public const int TranscendLevel = 10;

        /// <summary>세트덱 스코어가 +1씩 오르는 각성 단계(3·6·9각). 초월은 별도(+1, 초월 가능 등급만).</summary>
        public static readonly int[] ScoreAwakenSteps = { 3, 6, 9 };

        /// <summary>9각에 도달한 LIVE/SIGNATURE/GOLDEN_GLOVE/DYNASTY만 초월 가능(PDF 원문).</summary>
        public static bool CanTranscend(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => true,
            Grade.LIVE_EPIC => true,
            Grade.GOLDEN_GLOVE => true,
            Grade.SIGNATURE => true,
            Grade.DYNASTY => true,
            _ => false
        };

        /// <summary>등급별 최대 각성 단계 - 초월 가능 등급은 10(초월), 나머지는 9각.
        /// GenerateKBODatabase.py GRADE_META의 max_awaken 컬럼과 반드시 일치해야 한다.</summary>
        public static int MaxAwakenLevelFor(Grade grade) => CanTranscend(grade) ? TranscendLevel : NineStageAwakenCap;

        public static bool IsLive(Grade grade) => grade == Grade.LIVE_NORMAL || grade == Grade.LIVE_EPIC;

        /// <summary>등급별 개인 세트덱 기본 스코어(명함, 0각) = 카드 Salary(cards_*.csv salary_cost).</summary>
        public static int BaseSetDeckScore(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => 4,
            Grade.LIVE_EPIC => 4,
            Grade.ALLSTAR => 4,
            Grade.FRANCHISE => 3,
            Grade.TITLE_HOLDER => 3,
            Grade.RETIRED_NUMBER => 2, // 잠정(규격 미정) - 역전 원칙상 TH(3)와 SIG(1) 사이
            Grade.GOLDEN_GLOVE => 2,
            Grade.SIGNATURE => 1,
            Grade.DYNASTY => 1,
            _ => 0
        };

        /// <summary>등급별 개인 세트덱 최대 스코어(9각 한계 등급은 +3, 초월 가능 등급은 +4).</summary>
        public static int MaxSetDeckScore(Grade grade) =>
            SetDeckScoreFor(grade, MaxAwakenLevelFor(grade));

        /// <summary>등급 + 각성 단계로 개인 세트덱 스코어를 계산한다. 등급 한계를 넘는 AwakenLevel은
        /// 한계로 클램프한다(예: 초월 불가 등급의 레거시 10각 = 9각).</summary>
        public static int SetDeckScoreFor(Grade grade, int awakenLevel)
        {
            int level = ClampAwaken(grade, awakenLevel);
            int score = BaseSetDeckScore(grade);
            foreach (int step in ScoreAwakenSteps)
            {
                if (level >= step) score++;
            }
            if (level >= TranscendLevel && CanTranscend(grade)) score++;
            return score;
        }

        public static int ClampAwaken(Grade grade, int awakenLevel)
        {
            int max = MaxAwakenLevelFor(grade);
            if (awakenLevel < 0) return 0;
            return awakenLevel > max ? max : awakenLevel;
        }

        public static bool IsTranscended(Grade grade, int awakenLevel) =>
            CanTranscend(grade) && awakenLevel >= TranscendLevel;

        /// <summary>
        /// [LIVE 초월 OVR 상한] 강화+각성으로 세부 스탯 각각에 더해지는 성장치의 등급별 상한.
        /// LIVE는 초월(각성 10)까지 성장해 세트덱 스코어는 최대 8점을 주지만, 실전 성장치는 ALLSTAR의
        /// 최대 성장치(10강 + 9각 = +19)를 넘지 못한다 - 즉 같은 선수의 LIVE 초월 카드는 ALLSTAR 9각
        /// 카드와 "동급 이하"의 Final OVR을 갖는다(명령서 STEP 2-3 핵심 밸런스 제약). 초월의 가치는
        /// 오직 세트덱 스코어(+1)에만 있다. 나머지 등급은 상한 없음(int.MaxValue).
        /// </summary>
        public static int MaxStatGrowthFor(Grade grade, int maxReinforceLevel) =>
            IsLive(grade) ? maxReinforceLevel + NineStageAwakenCap : int.MaxValue;

        /// <summary>UI 표기용 각성 단계 문자열("명함", "5각", "초월").</summary>
        public static string AwakenLabel(Grade grade, int awakenLevel)
        {
            int level = ClampAwaken(grade, awakenLevel);
            if (IsTranscended(grade, level)) return "초월";
            return level <= 0 ? "명함" : $"{level}각";
        }
    }
}
