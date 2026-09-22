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
        public const int MaxAwakenLevel = 10;    // 1각 ~ 10각
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
        public int AwakenLevel;        // 0~10각 (ALLSTAR 이상 등급만 유효)
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

        /// <summary>[TASK-KBO-155] SEASON 등급 삭제(사용자 직접 지시) - LIVE_NORMAL / LIVE_EPIC
        /// 등급만 강화 전용(각성 불가)으로 남는다. RETIRED_NUMBER(영구결번)는 TITLE_HOLDER/SIGNATURE와
        /// 같은 상위 등급 취급이라 각성 가능 목록에서 제외하지 않는다.</summary>
        public bool CanAwaken => Template != null
            && Template.Grade != Grade.LIVE_NORMAL
            && Template.Grade != Grade.LIVE_EPIC;

        private static StarType DefaultStarTypeFor(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => StarType.NORMAL,
            Grade.LIVE_EPIC => StarType.NORMAL,
            Grade.ALLSTAR => StarType.PURPLE,
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
            Grade.TITLE_HOLDER => 5,
            Grade.RETIRED_NUMBER => 5, // [TASK-KBO-155 신설] TITLE_HOLDER와 동일한 5성 - 최상위(6성)보다는 한 단계 아래
            Grade.GOLDEN_GLOVE => 5,
            Grade.SIGNATURE => MaxStarLevel,
            Grade.DYNASTY => MaxStarLevel,
            _ => MinStarLevel
        };

        // 강화/각성 1레벨당 세부 스탯 각각에 붙는 성장치. TODO: 밸런스 확정 전까지의 임시값.
        private const int ReinforcePerStatBonus = 1;
        private const int AwakenPerStatBonus = 1;

        /// <summary>
        /// 강화/각성 성장치가 반영된 타자 세부 스탯. Template이 없거나 투수 카드면 default(0,0,0,0,0)를 반환한다.
        /// 스킬/세트덱 보너스는 매치 컨텍스트(상대방 존재)가 필요해 여기 포함하지 않으며, MatchEngine이 계산 시점에 적용한다.
        /// </summary>
        public BatterStats GetEffectiveBatterStats()
        {
            if (Template == null || Template.IsPitcher) return default;

            int growth = ReinforceLevel * ReinforcePerStatBonus + (CanAwaken ? AwakenLevel * AwakenPerStatBonus : 0);
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

            int growth = ReinforceLevel * ReinforcePerStatBonus + (CanAwaken ? AwakenLevel * AwakenPerStatBonus : 0);
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

            average *= ConditionMultiplier(CurrentCondition);

            return Mathf.RoundToInt(average);
        }

        /// <summary>5단계를 -5%~+5% 사이에서 대칭 선형 보간한다: Poor -5%, BelowAverage -2.5%,
        /// Normal 0%, Good +2.5%, Excellent +5%.</summary>
        private static float ConditionMultiplier(PlayerCondition condition)
        {
            int maxIndex = Enum.GetValues(typeof(PlayerCondition)).Length - 1; // Excellent의 인덱스 (4)
            int midIndex = maxIndex / 2; // Normal의 인덱스 (2)
            float step = (int)condition - midIndex; // -2 ~ +2
            return 1f + ConditionMaxBonusPercent * (step / midIndex);
        }

        private static float AverageOf(BatterStats stats) => (stats.Power + stats.Contact + stats.Discipline) / 3f;
        private static float AverageOf(PitcherStats stats) => (stats.Stuff + stats.Velocity + stats.Movement + stats.Control) / 4f;

        // 등급별 기본 코스트. TASK-KBO-031에서 GDD v4.0 확정 수치로 동기화됨(이전 커밋 8e597f9의
        // 잠정값을 대체). GradeBaseCostFor는 Grade를 "이름"으로 매칭하므로 Types.cs의 Grade enum
        // 정수값(ordinal) 배정과는 무관하다.
        private static float GradeBaseCostFor(Grade grade) => grade switch
        {
            Grade.LIVE_NORMAL => 5f,
            Grade.LIVE_EPIC => 8f,
            Grade.ALLSTAR => 12f,
            Grade.TITLE_HOLDER => 15f,
            Grade.RETIRED_NUMBER => 18f, // [TASK-KBO-155 신설] TITLE_HOLDER(15)와 SIGNATURE(20) 중간값
            Grade.SIGNATURE => 20f,
            Grade.GOLDEN_GLOVE => 25f,
            Grade.DYNASTY => 35f,
            _ => 5f
        };

        /// <summary>
        /// GDD v4.0 샐러리 캡 코스트 공식: 등급 기본 코스트 + (최종 OVR - 60) + (각성 단계 * 1.5).
        /// "최종 OVR"은 CalculateOVR()과 동일한 값(강화/세트덱/컨디션 반영)을 그대로 사용한다 - 즉 컨디션이
        /// 하루 단위로 오르내리면 이 코스트도 함께(±5% 이내로) 미세하게 흔들릴 수 있다는 뜻이다.
        /// PlayerTemplate.Cost(고정값)를 대체하는 새 공식으로, RosterManager의 샐러리 캡 검증이 이 값을 사용한다.
        /// </summary>
        public float CalculateSalaryCost(bool isSetDeckBonusActive = false, float setDeckBonusMultiplier = 1.0f)
        {
            if (Template == null) return 0f;

            int finalOvr = CalculateOVR(isSetDeckBonusActive, setDeckBonusMultiplier);
            float baseCost = GradeBaseCostFor(Template.Grade);

            return baseCost + (finalOvr - 60) + (AwakenLevel * 1.5f);
        }
    }
}
