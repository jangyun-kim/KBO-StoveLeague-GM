using KBOManager.Data;

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

        // ------------------------------------------------------------------
        // [TASK-KBO-184] 각성 중심 4대 성장(강화 · 한계 돌파 · 훈련/특훈 · 각성) + 12단계 리그(OVR 57~144) 정렬
        //
        // 선수 최종 표시 OVR = 기본 OVR(BaseOvrRange) + 순수 성장(MaxTotalGrowth, 최대 +31) + 정적 시너지(세트덱 OVR 최대 +13), 상한 144.
        // 성장 1포인트 = 세부 스탯 전 항목 +1 = OVR +1. 각성/초월이 가장 큰 축이다(상위 시즌 +15 = 순수 성장의 약 절반).
        //
        // 각성은 비선형 "임계점 돌파" 계단 - 상위 시즌(GOLDEN_GLOVE 이상) 기준 누적 OVR(AwakenCurve):
        //   단계   명함 1각 2각 [3각] 4각 5각 [6각] 7각 8각 [9각] [초월]
        //   OVR     0   1   2   5    6   7   10   11  12   14    15
        //   3각 = 1차 임계점(+3 도약, 실전 투입 최소선) · 6각 = 2차 임계점(+3 도약, 본래 성능) · 9각/초월 = 최종 완성.
        //   세트덱 스코어도 같은 3·6·9각/초월에서 +1씩 오른다(SetDeckScoreFor) - 성장과 세트덱이 같은 계단을 탄다.
        // 하위 등급은 같은 곡선을 등급별 각성 상한(AwakenGrowthCap)으로 축소한다(반올림).
        //   등급            기본 OVR    강화 돌파 특훈 각성 = 합계   풀성장       +세트덱 13(상한 144)
        //   LIVE_NORMAL     55~68       10   1    1    6   = +18   73~86        86~99
        //   LIVE_EPIC       65~75       10   2    2    8   = +22   87~97        100~110
        //   ALLSTAR         75~83       10   2    2   10   = +24   99~107       112~120
        //   FRANCHISE       79~86       10   3    2   11   = +26   105~112      118~125
        //   TITLE_HOLDER    83~90       10   3    3   12   = +28   111~118      124~131
        //   GOLDEN_GLOVE    87~95       10   3    3   15   = +31   118~126      131~139
        //   SIG/DYN/RN      95~102      10   3    3   15   = +31   126~133      139~144
        // ------------------------------------------------------------------

        /// <summary>[TASK-KBO-184] 상위 시즌 각성 누적 OVR 곡선(인덱스 = 각성 단계 0~10, 10 = 초월). 3·6·9각이 임계점.</summary>
        public static readonly int[] AwakenCurve = { 0, 1, 2, 5, 6, 7, 10, 11, 12, 14, 15 };

        /// <summary>[TASK-KBO-184] 각성 임계점(1차 3각 · 2차 6각 · 최종 9각). 초월(10)은 초월 가능 등급의 마지막 단계.</summary>
        public const int FirstAwakenThreshold = 3;
        public const int SecondAwakenThreshold = 6;
        public const int FinalAwakenThreshold = 9;

        /// <summary>[TASK-KBO-183] 시즌 등급별 카드 기본 OVR 대역(base_ovr) - GenerateKBODatabase.py GRADE_BASE_OVR_BAND와 반드시 일치.</summary>
        public static (int Min, int Max) BaseOvrRange(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => (55, 68),
            Grade.LIVE_EPIC => (65, 75),
            Grade.ALLSTAR => (75, 83),
            Grade.FRANCHISE => (79, 86),
            Grade.TITLE_HOLDER => (83, 90),
            Grade.GOLDEN_GLOVE => (87, 95),
            Grade.SIGNATURE => (95, 102),
            Grade.DYNASTY => (95, 102),
            Grade.RETIRED_NUMBER => (95, 102),
            _ => (1, 102)
        };

        public const int MaxReinforceGrowth = 10;
        public const int MaxLimitBreakGrowth = 3;
        public const int MaxTrainingGrowth = 3;
        public const int MaxAwakenGrowth = 15;
        /// <summary>상위 시즌(GOLDEN_GLOVE 이상) 순수 성장 최대치 = 10 + 3 + 3 + 15.</summary>
        public const int MaxPureGrowth = MaxReinforceGrowth + MaxLimitBreakGrowth + MaxTrainingGrowth + MaxAwakenGrowth;

        /// <summary>[TASK-KBO-184] 한계 돌파 단계 상한(1단계 = OVR +1). 10강 달성 후에만 진행할 수 있다.</summary>
        public static int LimitBreakCap(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => 1,
            Grade.LIVE_EPIC => 2,
            Grade.ALLSTAR => 2,
            _ => MaxLimitBreakGrowth
        };

        /// <summary>[TASK-KBO-184] 훈련(특훈) 단계 상한(1단계 = OVR +1).</summary>
        public static int TrainingCap(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => 1,
            Grade.LIVE_EPIC => 2,
            Grade.ALLSTAR => 2,
            Grade.FRANCHISE => 2,
            _ => MaxTrainingGrowth
        };

        /// <summary>[TASK-KBO-184] 각성(최종 단계 - 초월 가능 등급은 초월, 그 외 9각)으로 얻는 OVR 성장 상한.</summary>
        public static int AwakenGrowthCap(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => 6,
            Grade.LIVE_EPIC => 8,
            Grade.ALLSTAR => 10,
            Grade.FRANCHISE => 11,
            Grade.TITLE_HOLDER => 12,
            _ => MaxAwakenGrowth
        };

        /// <summary>[TASK-KBO-184] 각성 단계 → OVR 성장. 상위 시즌 곡선(AwakenCurve)을 등급 상한에 맞춰 축소(반올림)한다 -
        /// 등급 최종 단계(초월 또는 9각)에서 정확히 AwakenGrowthCap에 닿는다.</summary>
        public static int AwakenGrowthFor(Grade grade, int awakenLevel)
        {
            int level = ClampAwaken(grade, awakenLevel);
            int top = AwakenCurve[MaxAwakenLevelFor(grade)];
            int cap = AwakenGrowthCap(grade);
            if (top <= 0) return 0;
            return (AwakenCurve[level] * cap * 2 + top) / (2 * top);
        }

        /// <summary>[TASK-KBO-184] 다음 각성 임계점(3·6·9각 또는 초월) - 없으면 -1.</summary>
        public static int NextAwakenThreshold(Grade grade, int awakenLevel)
        {
            int level = ClampAwaken(grade, awakenLevel);
            foreach (int step in new[] { FirstAwakenThreshold, SecondAwakenThreshold, FinalAwakenThreshold, TranscendLevel })
            {
                if (step > MaxAwakenLevelFor(grade)) break;
                if (level < step) return step;
            }
            return -1;
        }

        /// <summary>[TASK-KBO-184] 각성 단계 설명("1차 임계점 전 - 실전 투입 최소선 3각까지 n단계" 등).</summary>
        public static string AwakenStageNote(Grade grade, int awakenLevel)
        {
            int level = ClampAwaken(grade, awakenLevel);
            if (level < FirstAwakenThreshold) return $"1차 임계점(3각) 전 - 실전 투입 최소선까지 {FirstAwakenThreshold - level}단계";
            if (level < SecondAwakenThreshold) return $"1차 임계점 돌파 · 2차 임계점(6각 = 본래 성능)까지 {SecondAwakenThreshold - level}단계";
            if (level < FinalAwakenThreshold) return $"2차 임계점 돌파 · 최종 완성(9각)까지 {FinalAwakenThreshold - level}단계";
            if (level < MaxAwakenLevelFor(grade)) return "9각 완성 · 초월 1단계 남음";
            return IsTranscended(grade, level) ? "초월 완성(최종)" : "9각 완성(등급 한계)";
        }

        /// <summary>[TASK-KBO-183] 등급별 순수 성장 상한(강화 + 한계 돌파 + 훈련 + 각성).</summary>
        public static int MaxTotalGrowth(Grade grade) =>
            MaxReinforceGrowth + LimitBreakCap(grade) + TrainingCap(grade) + AwakenGrowthCap(grade);

        /// <summary>[TASK-KBO-183] 기존 호출부 호환 - 등급별 순수 성장 상한(MaxTotalGrowth)을 돌려준다.
        /// (TASK-172 시절 "LIVE = 10강 + 9각 = +19, 그 외 무제한"은 4대 성장 상한표로 대체됐다.)</summary>
        public static int MaxStatGrowthFor(Grade grade, int maxReinforceLevel) => MaxTotalGrowth(grade);

        /// <summary>[TASK-KBO-184] 카드 최대 잠재 OVR = 기본 OVR + 순수 성장 상한 + 정적 시너지 최대치(세트덱 +13), 최종 상한 144.</summary>
        public static int MaxPotentialOvr(Grade grade, int baseOvr)
        {
            int potential = baseOvr + MaxTotalGrowth(grade) + TeamSynergyRules.MaxSynergyOvr;
            return potential > TeamSynergyRules.MaxFinalOvr ? TeamSynergyRules.MaxFinalOvr : potential;
        }

        /// <summary>[TASK-KBO-174] 등급별 실전 기본 OVR 보정(같은 선수 기준). GenerateKBODatabase.py GRADE_OVR_BONUS와
        /// 반드시 일치해야 한다 - 카드 CSV의 base_ovr가 없거나 해석 불가일 때 PlayerDatabase가 이 값으로 폴백한다.
        /// [TASK-KBO-183] 기획서 2단계 산정의 "2단계 시즌 등급 보정"이다: base_ovr = 1단계(그 시즌 성적 스탯) + 이 값.
        /// 1단계는 카드(시즌)마다 다르므로 같은 선수라도 카드 간 차이가 이 값의 차이와 같지는 않다.</summary>
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

        /// <summary>[TASK-KBO-188] 같은 시즌 등급 · 같은 선수(RealPlayerId) 재료 1장당 각성 상승폭(연도 무관).</summary>
        public const int SamePlayerAwakenGain = 3;
        /// <summary>[TASK-KBO-188] 같은 시즌 등급 · 다른 선수 재료 1장당 각성 상승폭.</summary>
        public const int OtherPlayerAwakenGain = 1;

        /// <summary>[TASK-KBO-185 호환 별칭] 구 "동일 연도 +3각" 상수 - 이제 "같은 선수 +3각"과 같다.</summary>
        public const int SameYearAwakenGain = SamePlayerAwakenGain;
        /// <summary>[TASK-KBO-185 호환 별칭] 구 "다른 연도 +1각" 상수 - 이제 "다른 선수 +1각"과 같다.</summary>
        public const int OtherYearAwakenGain = OtherPlayerAwakenGain;

        /// <summary>
        /// [TASK-KBO-188 각성 재료 규칙 교정] 각성 재료 1장이 주는 각성 단계(전 등급 공통).
        /// 필수 조건은 대상과 "같은 시즌 등급(Grade)"뿐이다 - 같은 선수(RealPlayerId 일치)면 연도와 무관하게 +3각,
        /// 다른 선수면 +1각이다. (TASK-185의 "동일 선수 한정 · 연도 비교"는 폐기.)
        /// </summary>
        public static int AwakenPointsPerMaterial(Grade targetGrade, bool isSamePlayer) =>
            isSamePlayer ? SamePlayerAwakenGain : OtherPlayerAwakenGain;

        /// <summary>[TASK-KBO-188] 대상/재료가 같은 선수인지(RealPlayerId 우선, 없으면 이름으로 폴백).</summary>
        public static bool IsSamePlayer(PlayerTemplate t, PlayerTemplate m)
        {
            if (t == null || m == null) return false;
            if (!string.IsNullOrEmpty(t.RealPlayerId) || !string.IsNullOrEmpty(m.RealPlayerId)) return t.RealPlayerId == m.RealPlayerId;
            return !string.IsNullOrEmpty(t.PlayerName) && t.PlayerName == m.PlayerName;
        }

        /// <summary>[TASK-KBO-188] 대상/재료 쌍의 각성 상승폭(무효 재료 = 0: 자기 자신 · 다른 시즌 등급). 미리보기 · 배지 · 실제 적용 공용.</summary>
        public static int AwakenGainFor(Player target, Player material)
        {
            if (target?.Template == null || material?.Template == null || ReferenceEquals(target, material)) return 0;
            if (material.Template.Grade != target.Template.Grade) return 0;
            return AwakenPointsPerMaterial(target.Template.Grade, IsSamePlayer(target.Template, material.Template));
        }

        /// <summary>[TASK-KBO-188] 각성 재료 배지 문구("[같은 선수 +3각]" / "[다른 선수 +1각]"), 무효 재료면 빈 문자열.</summary>
        public static string AwakenMaterialBadge(Player target, Player material)
        {
            int gain = AwakenGainFor(target, material);
            if (gain <= 0) return "";
            return gain == SamePlayerAwakenGain ? $"[같은 선수 +{gain}각]" : $"[다른 선수 +{gain}각]";
        }

        /// <summary>[TASK-KBO-188] 카드 위 성장 배지 - 미각성(0각)은 강화 단계("+7"), 1각 이상은 각성 단계("3각"/"초월")로 전환한다.
        /// 강화도 각성도 없으면 빈 문자열.</summary>
        public static string GrowthBadgeLabel(Grade grade, int reinforceLevel, int awakenLevel)
        {
            int level = ClampAwaken(grade, awakenLevel);
            if (level >= 1) return IsTranscended(grade, level) ? "초월" : $"{level}각";
            return reinforceLevel > 0 ? $"+{reinforceLevel}" : "";
        }

        public static bool ShowsAwakenBadge(Grade grade, int awakenLevel) => ClampAwaken(grade, awakenLevel) >= 1;

        public static string GrowthBadgeLabel(Player p) =>
            p?.Template == null ? "" : GrowthBadgeLabel(p.Template.Grade, p.ReinforceLevel, p.AwakenLevel);

        /// <summary>UI 표기용 각성 단계 문자열("명함", "5각", "초월").</summary>
        public static string AwakenLabel(Grade grade, int awakenLevel)
        {
            int level = ClampAwaken(grade, awakenLevel);
            if (IsTranscended(grade, level)) return "초월";
            return level <= 0 ? "명함" : $"{level}각";
        }
    }
}
