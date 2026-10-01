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
    ///   TITLE_HOLDER 3 → 9각 6 | GOLDEN_GLOVE 2 → 초월 6 | SIGNATURE 1 → 초월 5 | DYNASTY 1 → 초월 5
    ///   RETIRED_NUMBER 2 → 초월 6 [TASK-KBO-174 확정] - 실전 성능은 SIG/DYN과 같은 최상위 종결급이면서
    ///   세트덱 스코어는 SIG/DYN보다 +1 높은 "구단 성골 우대"(Core Captain). 구단별 1~4명뿐이라 도배 불가.
    ///
    /// [TASK-KBO-173 Salary 일원화] 이 기본 스코어가 곧 카드의 "Salary"다 - cards_*.csv의 salary_cost 컬럼은
    /// BaseSetDeckScore()와 같은 값으로 생성되며, 별도 샐러리 캡(구 Player.CalculateSalaryCost / RosterManager
    /// EnforceSalaryCap)은 폐기됐다. 도배 방지는 이 역전 스코어 + 27인 세트덱 편성 규칙이 전담한다.
    ///
    /// [TASK-KBO-174 실전 능력치 연동] GradeOvrBonus() - 같은 선수의 카드는 등급에 따라 기본 OVR이
    /// LIVE_NORMAL(+0) < LIVE_EPIC(+1) < ALLSTAR(+3) < FRANCHISE(+4) ≤ TITLE_HOLDER(+5) < GOLDEN_GLOVE(+7)
    /// < SIGNATURE = DYNASTY = RETIRED_NUMBER(+10)만큼 높다. cards_*.csv의 base_ovr = 선수 기본 OVR + 이 보정이며,
    /// PlayerDatabase가 카드 세부 스탯을 base_ovr에 맞춰 이동시킨다.
    /// </summary>
    public static class CardGrowthRules
    {
        /// <summary>초월 불가 등급의 각성 한계(9각).</summary>
        public const int NineStageAwakenCap = 9;

        /// <summary>초월 단계의 내부 AwakenLevel 값(= 구 10각). `Player.MaxAwakenLevel`과 같다.</summary>
        public const int TranscendLevel = 10;

        /// <summary>세트덱 스코어가 +1씩 오르는 각성 단계(3·6·9각). 초월은 별도(+1, 초월 가능 등급만).</summary>
        public static readonly int[] ScoreAwakenSteps = { 3, 6, 9 };

        /// <summary>9각에 도달한 LIVE/SIGNATURE/GOLDEN_GLOVE/DYNASTY(PDF 원문) + RETIRED_NUMBER([TASK-KBO-174]
        /// 최상위 종결 등급 격상)만 초월 가능. ALLSTAR/FRANCHISE/TITLE_HOLDER는 9각 한계.</summary>
        public static bool CanTranscend(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => true,
            Grade.LIVE_EPIC => true,
            Grade.GOLDEN_GLOVE => true,
            Grade.SIGNATURE => true,
            Grade.DYNASTY => true,
            Grade.RETIRED_NUMBER => true,
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
            Grade.RETIRED_NUMBER => 2, // [TASK-KBO-174 확정] 2 → 초월 6(SIG/DYN보다 +1, 구단 성골 우대)
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

        /// <summary>[TASK-KBO-174] 등급별 실전 기본 OVR 보정(같은 선수 기준). GenerateKBODatabase.py GRADE_OVR_BONUS와
        /// 반드시 일치해야 한다 - 카드 CSV의 base_ovr가 없거나 해석 불가일 때 PlayerDatabase가 이 값으로 폴백한다.</summary>
        public static int GradeOvrBonus(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => 0,
            Grade.LIVE_EPIC => 1,
            Grade.ALLSTAR => 3,
            Grade.FRANCHISE => 4,
            Grade.TITLE_HOLDER => 5,
            Grade.GOLDEN_GLOVE => 7,
            Grade.SIGNATURE => 10,
            Grade.DYNASTY => 10,
            Grade.RETIRED_NUMBER => 10,
            _ => 0
        };

        /// <summary>[TASK-KBO-178] 유저에게 보이는 선수 등급 표기 - 화면/로그에 내부 enum 이름(LIVE_NORMAL 등)을 그대로 쓰지 않는다.
        /// 선수 카드는 LIVE_NORMAL(1~3성)과 LIVE_EPIC(4성)이 실제로 다른 등급(별도 영입 상품·5,000장 이상씩 존재)이라 둘을 구분해 표기한다.</summary>
        public static string DisplayName(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => "라이브",
            Grade.LIVE_EPIC => "라이브 에픽",
            Grade.ALLSTAR => "올스타",
            Grade.FRANCHISE => "프랜차이즈",
            Grade.TITLE_HOLDER => "타이틀 홀더",
            Grade.RETIRED_NUMBER => "영구결번",
            Grade.SIGNATURE => "시그니처",
            Grade.GOLDEN_GLOVE => "골든글러브",
            Grade.DYNASTY => "왕조",
            _ => grade.ToString()
        };

        /// <summary>최상위 종결 등급(SIGNATURE/DYNASTY/RETIRED_NUMBER)의 PowerRank.</summary>
        public const int TopTierPowerRank = 7;

        /// <summary>
        /// [TASK-KBO-175] 등급 "위상" 순번 - 영입 확률 등급 필터(ScoutManager)·강화 재료 경험치(UpgradeConstants)·
        /// 최고급 뽑기 연출(ScoutUIController)이 등급 대소를 비교할 때 쓴다. 실전 성능 서열(GradeOvrBonus)과 같은
        /// LIVE_NORMAL &lt; LIVE_EPIC &lt; ALLSTAR &lt; FRANCHISE &lt; TITLE_HOLDER &lt; GOLDEN_GLOVE &lt; SIGNATURE = DYNASTY =
        /// RETIRED_NUMBER 순서다. Grade enum 정수값은 cards_*.csv grade_id/직렬화 호환 때문에 그대로 두므로
        /// (RETIRED_NUMBER = 6이 SIGNATURE(7)/GOLDEN_GLOVE(8)보다 작다) 등급 비교에 (int)Grade를 직접 쓰지 말 것.
        /// </summary>
        public static int PowerRank(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => 1,
            Grade.LIVE_EPIC => 2,
            Grade.ALLSTAR => 3,
            Grade.FRANCHISE => 4,
            Grade.TITLE_HOLDER => 5,
            Grade.GOLDEN_GLOVE => 6,
            Grade.SIGNATURE => TopTierPowerRank,
            Grade.DYNASTY => TopTierPowerRank,
            Grade.RETIRED_NUMBER => TopTierPowerRank,
            _ => 0
        };

        /// <summary>
        /// [TASK-KBO-174 LIVE 각성/초월 비용 완화] 각성 재료 1장이 주는 각성 포인트(초월 = 10포인트).
        /// 완전히 같은 카드 사본: LIVE 5 / 그 외 3. 같은 선수의 같은 등급 다른 카드: LIVE 2 / 그 외 1.
        /// → LIVE 초월 = 같은 카드 사본 2장, 상위 등급 초월/9각 = 사본 4장/3장. 무과금이 연간 LIVE 약 10장을
        /// 초월(사본 약 20장)해 185~190P 구간에 닿도록 하는 설계값이다(docs/04 11절).
        /// </summary>
        public static int AwakenPointsPerMaterial(Grade targetGrade, bool isIdenticalCard) =>
            IsLive(targetGrade) ? (isIdenticalCard ? 5 : 2) : (isIdenticalCard ? 3 : 1);

        /// <summary>UI 표기용 각성 단계 문자열("명함", "5각", "초월").</summary>
        public static string AwakenLabel(Grade grade, int awakenLevel)
        {
            int level = ClampAwaken(grade, awakenLevel);
            if (IsTranscended(grade, level)) return "초월";
            return level <= 0 ? "명함" : $"{level}각";
        }
    }
}
