using System;
using System.Collections.Generic;
using KBOManager.Data;
using UnityEngine;

namespace KBOManager.Models
{
    /// <summary>
    /// 유저가 실제로 보유한 선수 카드 인스턴스. PlayerTemplate(불변 원본 데이터)을 참조하며,
    /// 강화/각성/스킬 등 유저의 육성 진행 상태만을 들고 있는 순수 데이터 클래스(MonoBehaviour 아님).
    /// </summary>
    [Serializable]
    public class Player
    {
        public const int MaxReinforceLevel = 10; // 명함(0강) ~ 10강
        // [TASK-KBO-172] 1~9각 + 초월(내부 값 10 = 구 10각). 이 값은 "전 등급 공통 절대 상한"이며, 실제 등급별
        // 한계(9각 한계 등급 = 9, 초월 가능 등급 = 10)는 MaxAwakenLevelForGrade / CardGrowthRules가 결정한다.
        public const int MaxAwakenLevel = CardGrowthRules.TranscendLevel;
        public const int MinStarLevel = 1;
        public const int MaxStarLevel = 6;

        // ----- 투수 체력(Stamina) -----
        public const int MaxStaminaStartingPitcher = 100; // 선발
        public const int MaxStaminaBullpen = 40;          // 승리조/추격조/롱릴리프/마무리 전부 불펜 취급
        public const float LowStaminaThresholdPercent = 0.3f;  // 30% 미만이면 페널티 발동
        public const float LowStaminaOvrPenaltyPercent = 0.15f; // 페널티 크기: 최종 스탯 -15%

        // ----- 일일 컨디션(Condition) -----
        // 5단계 중 가장 좋은/나쁜 쪽 보너스 크기. 가운데(Normal)는 0%, 그 사이 두 단계(BelowAverage/Good)는
        // 이 값의 절반씩 선형 보간한다 - 그래서 5단계가 -5% / -2.5% / 0% / +2.5% / +5%로 대칭을 이룬다.
        public const float ConditionMaxBonusPercent = 0.05f;

        public string InstanceId;      // 유저 보유 카드 고유 ID (GUID)
        public PlayerTemplate Template; // 원본 데이터 참조 (이름/구단/기본OVR/코스트/포지션/등급)

        public int ReinforceLevel;     // 0~10강
        // [TASK-KBO-138] "EXP 누적 확정 강화" 방식으로 개편되며 신설. 다음 강화 단계(ReinforceLevel+1)로
        // 올라가는 데 필요한 경험치 중 현재까지 쌓인 양 - UpgradeManager.TryEnhance()가 재료 카드의
        // 등급별 제공 경험치(UpgradeConstants.GetMaterialExp())를 여기 누적하고, 요구치
        // (UpgradeConstants.GetRequiredExp())를 넘을 때마다 ReinforceLevel을 올리며 초과분만 이월한다.
        public int ReinforceExp;
        public int AwakenLevel;        // 0~9각, 10 = 초월([TASK-KBO-172] 전 등급 각성 가능, 한계는 등급별)
        // [TASK-KBO-183] 4대 성장 시스템 - 한계 돌파(10강 달성 후, 단계당 OVR +1)와 훈련/특훈(단계당 OVR +1). 등급별 상한은 CardGrowthRules.
        public int LimitBreakLevel;
        public int TrainingLevel;
        public int StarLevel;          // 1~6, 뽑기 시 등급에 따라 결정되는 초기 성급. 강화/각성과는 별개 개념
        public StarType CurrentStarType;

        public List<string> AcquiredSkillIds = new List<string>();

        // 투수 카드에만 의미가 있다(타자 카드는 항상 0). MatchEngine이 타석마다 ConsumeStamina()로
        // 깎고, LeagueManager가 경기 종료마다 RecoverStamina()로 회복시킨다.
        public int MaxStamina;
        public int CurrentStamina;

        // 타자/투수 카드 모두에 적용된다(체력과 달리 컨디션은 투수 전용 개념이 아니다).
        // LeagueCalendar의 날짜가 바뀔 때마다 LeagueManager가 ShiftCondition()으로 갱신한다.
        public PlayerCondition CurrentCondition = PlayerCondition.Normal;

        public Player() { }

        public Player(string instanceId, PlayerTemplate template)
        {
            InstanceId = instanceId;
            Template = template;
            ReinforceLevel = 0;
            AwakenLevel = 0;
            CurrentStarType = template != null ? DefaultStarTypeFor(template.Grade) : StarType.NORMAL;
            StarLevel = template != null ? DefaultStarLevelFor(template.Grade) : MinStarLevel;
            MaxStamina = template != null && template.IsPitcher ? DefaultMaxStaminaFor(template.PitcherRole) : 0;
            CurrentStamina = MaxStamina; // 새로 발급된 카드는 항상 완전 회복 상태로 시작한다.
            CurrentCondition = PlayerCondition.Normal; // 새로 발급된 카드는 항상 '보통'으로 시작한다.
        }

        /// <summary>
        /// CurrentCondition을 인접한 한 단계만 위/아래로 옮긴다(예: Good에서 바로 Poor로 떨어지는 등의
        /// 급변이 없도록 보장). 이미 최상/최악 단계에서 같은 방향으로 더 이동하려 하면 그 자리에 머문다.
        /// </summary>
        public void ShiftCondition(bool up)
        {
            int maxIndex = Enum.GetValues(typeof(PlayerCondition)).Length - 1;
            int shifted = Mathf.Clamp((int)CurrentCondition + (up ? 1 : -1), 0, maxIndex);
            CurrentCondition = (PlayerCondition)shifted;
        }

        /// <summary>투수 롤에 따른 기본 최대 체력. 선발은 오래 던지므로 넉넉하게, 불펜은 짧고 굵게 쓰므로 낮게 잡았다.</summary>
        public static int DefaultMaxStaminaFor(PitcherRole role) =>
            role == PitcherRole.StartingPitcher ? MaxStaminaStartingPitcher : MaxStaminaBullpen;

        /// <summary>체력이 30% 미만으로 떨어진 투수. MatchEngine이 이 값을 보고 최종 투구 스탯에 -15%
        /// 페널티를 적용한다. 타자 카드(MaxStamina == 0)는 항상 false.</summary>
        public bool IsLowStamina => Template != null && Template.IsPitcher && MaxStamina > 0
            && (float)CurrentStamina / MaxStamina < LowStaminaThresholdPercent;

        /// <summary>체력을 amount만큼 소모한다. 0 밑으로 내려가지 않는다(MatchEngine이 타석마다 호출).</summary>
        public void ConsumeStamina(int amount)
        {
            if (amount <= 0) return;
            CurrentStamina = Mathf.Max(0, CurrentStamina - amount);
        }

        /// <summary>체력을 amount만큼 회복한다. MaxStamina를 넘지 않는다(LeagueManager가 경기 종료마다 호출).</summary>
        public void RecoverStamina(int amount)
        {
            if (amount <= 0) return;
            CurrentStamina = Mathf.Min(MaxStamina, CurrentStamina + amount);
        }

        /// <summary>[TASK-KBO-172] 전 등급 각성 가능 - LIVE_NORMAL/LIVE_EPIC도 이제 3·6·9각을 거쳐 초월까지
        /// 성장해 세트덱 스코어(최대 8점)를 올리는 "스코어 배터리" 역할을 한다(기획 고도화 자료.pdf). 대신
        /// 실전 성장치는 [TASK-KBO-183] 등급별 4대 성장 상한(LIVE +14)으로 자른다(GetStatGrowth 참고). 이전 규칙(TASK-155:
        /// LIVE 각성 불가)은 폐기됐다.</summary>
        public bool CanAwaken => Template != null;

        /// <summary>[TASK-KBO-172] 이 카드 등급의 각성 한계(9각 한계 = 9, 초월 가능 = 10).</summary>
        public int MaxAwakenLevelForGrade => Template != null
            ? CardGrowthRules.MaxAwakenLevelFor(Template.Grade)
            : CardGrowthRules.NineStageAwakenCap;

        /// <summary>[TASK-KBO-172] 등급 한계로 클램프한 유효 각성 단계(레거시 세이브의 초월 불가 등급 10각 = 9각).</summary>
        public int EffectiveAwakenLevel => Template != null
            ? CardGrowthRules.ClampAwaken(Template.Grade, AwakenLevel)
            : 0;

        public bool IsTranscended => Template != null && CardGrowthRules.IsTranscended(Template.Grade, AwakenLevel);

        /// <summary>[TASK-KBO-172] 이 카드가 세트덱에 기여하는 개인 스코어(등급 기본 + 3·6·9각 + 초월).</summary>
        public int SetDeckScore => Template != null ? CardGrowthRules.SetDeckScoreFor(Template.Grade, AwakenLevel) : 0;

        /// <summary>UI 표기용 각성 라벨("명함"/"n각"/"초월").</summary>
        public string AwakenLabel => Template != null ? CardGrowthRules.AwakenLabel(Template.Grade, AwakenLevel) : "명함";

        private static StarType DefaultStarTypeFor(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => StarType.NORMAL,
            Grade.LIVE_EPIC => StarType.NORMAL,
            Grade.ALLSTAR => StarType.PURPLE,
            Grade.FRANCHISE => StarType.BRONZE, // [TASK-KBO-172 신설]
            Grade.TITLE_HOLDER => StarType.SILVER,
            Grade.RETIRED_NUMBER => StarType.BLACK, // [TASK-KBO-155 신설]
            Grade.GOLDEN_GLOVE => StarType.GOLD,
            Grade.SIGNATURE => StarType.PLATINUM,
            Grade.DYNASTY => StarType.TEAM_COLOR,
            _ => StarType.NORMAL
        };

        /// <summary>
        /// 등급별 결정론적 기본 성급. LIVE_NORMAL의 1~3성 무작위 배정처럼 확률이 개입되는 규칙은
        /// 뽑기를 실제로 수행하는 ScoutManager가 이 기본값을 덮어써서 적용한다.
        /// </summary>
        private static int DefaultStarLevelFor(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => MinStarLevel,
            Grade.LIVE_EPIC => 4,
            Grade.ALLSTAR => 4,
            Grade.FRANCHISE => 5, // [TASK-KBO-172 신설] TITLE_HOLDER와 같은 5성(브론즈 컬러로 구분)
            Grade.TITLE_HOLDER => 5,
            Grade.RETIRED_NUMBER => 5, // [TASK-KBO-155 신설] TITLE_HOLDER와 동일한 5성 - 최상위(6성)보다는 한 단계 아래
            Grade.GOLDEN_GLOVE => 5,
            Grade.SIGNATURE => MaxStarLevel,
            Grade.DYNASTY => MaxStarLevel,
            _ => MinStarLevel
        };

        // 강화 1레벨당 세부 스탯 각각에 붙는 성장치.
        private const int ReinforcePerStatBonus = 1;

        /// <summary>[TASK-KBO-183] 강화 성장(0~+10).</summary>
        public int ReinforceGrowth => Math.Min(Math.Max(0, ReinforceLevel), CardGrowthRules.MaxReinforceGrowth) * ReinforcePerStatBonus;

        /// <summary>[TASK-KBO-183] 한계 돌파 성장(등급 상한까지).</summary>
        public int LimitBreakGrowth => Template != null ? Math.Min(Math.Max(0, LimitBreakLevel), CardGrowthRules.LimitBreakCap(Template.Grade)) : 0;

        /// <summary>[TASK-KBO-183] 훈련(특훈) 성장(등급 상한까지).</summary>
        public int TrainingGrowth => Template != null ? Math.Min(Math.Max(0, TrainingLevel), CardGrowthRules.TrainingCap(Template.Grade)) : 0;

        /// <summary>[TASK-KBO-184] 각성 성장(임계점 계단 곡선 CardGrowthRules.AwakenCurve - 상위 시즌 3각 +5 / 6각 +10 / 9각 +14 / 초월 +15).</summary>
        public int AwakenGrowth => Template != null ? CardGrowthRules.AwakenGrowthFor(Template.Grade, AwakenLevel) : 0;

        /// <summary>
        /// [TASK-KBO-183] 4대 성장(강화 + 한계 돌파 + 훈련/특훈 + 각성) 합계 - 세부 스탯 각각에 균등 가산된다(= OVR +N).
        /// 등급별 순수 성장 상한(CardGrowthRules.MaxTotalGrowth: LIVE +14 ~ 상위 시즌 +25)으로 자른다. TASK-172의 "강화 + 각성(1단계당 +1),
        /// LIVE만 +19 상한" 규칙은 이 4대 성장 상한표로 대체됐다.
        /// </summary>
        public int GetStatGrowth()
        {
            if (Template == null) return 0;
            int growth = ReinforceGrowth + LimitBreakGrowth + TrainingGrowth + AwakenGrowth;
            int cap = CardGrowthRules.MaxTotalGrowth(Template.Grade);
            return growth > cap ? cap : growth;
        }

        /// <summary>[TASK-KBO-183] 카드 기본 OVR(성장/시너지 제외 = cards_*.csv base_ovr).</summary>
        public int BaseOvr => Template != null ? Template.GetBaseOverall() : 0;

        /// <summary>[TASK-KBO-183] 이 카드 등급의 순수 성장 상한.</summary>
        public int MaxGrowth => Template != null ? CardGrowthRules.MaxTotalGrowth(Template.Grade) : 0;

        /// <summary>[TASK-KBO-184] 최대 잠재 OVR = 기본 + 순수 성장 상한 + 정적 시너지 최대(세트덱 +13), 상한 144.</summary>
        public int MaxPotentialOvr => Template != null ? CardGrowthRules.MaxPotentialOvr(Template.Grade, BaseOvr) : 0;

        /// <summary>
        /// 강화/각성 성장치가 반영된 타자 세부 스탯. Template이 없거나 투수 카드면 default(0,0,0,0,0)를 반환한다.
        /// 스킬/세트덱 보너스는 매치 컨텍스트(상대방 존재)가 필요해 여기 포함하지 않으며, MatchEngine이 계산 시점에 적용한다.
        /// </summary>
        public BatterStats GetEffectiveBatterStats()
        {
            if (Template == null || Template.IsPitcher) return default;

            int growth = GetStatGrowth();
            // [TASK-KBO-089] operator+(Types.cs, TASK-088)로 5개 필드 전부에 growth를 균등 가산한다 - 이전에는
            // 3-인자 생성자로 재조립하면서 Speed/Defense가 0으로 유실되었다.
            return Template.BatterStats + new BatterStats(growth, growth, growth, growth, growth);
        }

        /// <summary>
        /// 강화/각성 성장치가 반영된 투수 세부 스탯. Template이 없거나 타자 카드면 default(0,0,0,0,0)를 반환한다.
        /// 스킬/세트덱 보너스는 매치 컨텍스트(상대방 존재)가 필요해 여기 포함하지 않으며, MatchEngine이 계산 시점에 적용한다.
        /// </summary>
        public PitcherStats GetEffectivePitcherStats()
        {
            if (Template == null || !Template.IsPitcher) return default;

            int growth = GetStatGrowth();
            // [TASK-KBO-089] operator+(Types.cs, TASK-088)로 5개 필드 전부에 growth를 균등 가산한다 - 이전에는
            // 4-인자 생성자로 재조립하면서 Stamina가 0으로 유실되었다.
            return Template.PitcherStats + new PitcherStats(growth, growth, growth, growth, growth);
        }

        /// <summary>
        /// 강화/각성이 반영된 세부 스탯의 평균에 세트덱(구단 통일) 보너스 배율과 일일 컨디션 배율을
        /// 적용해 최종 OVR을 산출한다. GDD 원문은 "OVR이 크게 뻥튀기"라 표현하므로, 세트덱 보너스는
        /// 가산이 아닌 배율(setDeckBonusMultiplier)로 구현했다. 배율은 GameManager.CheckSetDeckBonus()
        /// 결과값을 그대로 전달받아 사용한다.
        ///
        /// 컨디션 배율은 이 메서드(카드 목록 정렬, 라인업/로테이션 결정, "패기" 등 OVR 비교 스킬 조건
        /// 판정 등 OVR을 참조하는 모든 곳)에는 적용되지만, MatchEngine이 실제 타석 결과를 굴릴 때 쓰는
        /// 세부 스탯(BatterStats/PitcherStats) 자체에는 반영되지 않는다 - 즉 "컨디션이 좋아 보이는 카드가
        /// 라인업에 더 잘 뽑힌다"까지만 보장하며, 타석 하나하나의 확률 계산에 직접 끼어들지는 않는다.
        /// </summary>
        public int CalculateOVR(bool isSetDeckBonusActive, float setDeckBonusMultiplier = 1.0f)
        {
            if (Template == null) return 0;

            float average = Template.IsPitcher
                ? AverageOf(GetEffectivePitcherStats())
                : AverageOf(GetEffectiveBatterStats());

            if (isSetDeckBonusActive && setDeckBonusMultiplier > 1f)
            {
                average *= setDeckBonusMultiplier;
            }

            // [TASK-KBO-184] 컨디션 배율 제거 - 선수 OVR은 컨디션·경기 상황에 흔들리지 않는 정적 수치다(컨디션은 MatchEngine이
            // 타석마다 세부 스탯에 ConditionStatBonus로만 반영하는 경기 안 가변 요소).
            return Mathf.RoundToInt(average);
        }

        /// <summary>[TASK-KBO-183] 컨디션 배율을 뺀 OVR(= 기본 OVR + 4대 성장). 구단 OVR(TeamOvrCalculator)과 'OVR 7 격차 법칙'은
        /// 기획서대로 "기본 OVR" 기준이라 일일 컨디션(±5%)에 흔들리지 않게 이 값을 쓴다 - 컨디션은 경기 안 가변 요소로만 작동한다.</summary>
        public int CalculateNeutralOVR()
        {
            if (Template == null) return 0;
            float average = Template.IsPitcher ? AverageOf(GetEffectivePitcherStats()) : AverageOf(GetEffectiveBatterStats());
            return Mathf.RoundToInt(average);
        }

        /// <summary>[TASK-KBO-184] 컨디션 → 경기 중 세부 스탯 가산(Poor -2 / BelowAverage -1 / Normal 0 / Good +1 / Excellent +2).
        /// MatchEngine만 쓴다 - 표시 OVR/구단 OVR에는 들어가지 않는다.</summary>
        public int ConditionStatBonus => (int)CurrentCondition - (Enum.GetValues(typeof(PlayerCondition)).Length - 1) / 2;

        private static float AverageOf(BatterStats stats) => (stats.Power + stats.Contact + stats.Discipline) / 3f;
        private static float AverageOf(PitcherStats stats) => (stats.Stuff + stats.Velocity + stats.Movement + stats.Control) / 4f;

        // [TASK-KBO-173, Salary ↔ 세트덱 스코어 일원화] 구 GDD v4.0 샐러리 코스트(GradeBaseCostFor + (최종 OVR - 60)
        // + 각성 * 1.5)와 CalculateSalaryCost()를 삭제했다. 카드의 "Salary"는 이제 곧 개인 세트덱 스코어다:
        // Salary(명함 기준, cards_*.csv salary_cost와 동일) = CardGrowthRules.BaseSetDeckScore(),
        // 현재 기여값 = SetDeckScore(각성/초월 반영). 최상위 카드 도배 방지는 샐러리 캡 대신 역전 스코어
        // (종결 카드일수록 낮음) + 27인 세트덱 편성 규칙(RosterManager, SetDeckEvaluator)이 맡는다.
        public int Salary => Template != null ? CardGrowthRules.BaseSetDeckScore(Template.Grade) : 0;
    }
}
