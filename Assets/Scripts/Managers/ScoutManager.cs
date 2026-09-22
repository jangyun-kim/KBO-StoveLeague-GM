using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    [Serializable]
    public class GradeDropRate
    {
        public Grade Grade;
        [Range(0f, 100f)] public float RatePercent;
    }

    /// <summary>
    /// 스카우트(가챠) 매니저. GDD "뽑기(가챠) > 선수 영입" 절이 정의한 카테고리별 전용 재화를
    /// 소모해 PlayerTemplate을 추첨하고, GDD 2절의 등급별 초기 성급/색상 규칙을 강제 적용한 뒤
    /// SkillDB로 초기 스킬을 부여한 Player 인스턴스를 발급한다.
    ///
    /// [TASK-KBO-129] 구 Roll1()/Roll10()(단일 ScoutTicket 소모, 전체 등급 확률 혼합)을 폐기하고
    /// GDD가 실제로 나열한 6개 카테고리 전용 메서드로 교체했다 - 일반 영입(라이브 일반/라이브 에픽),
    /// 프리미엄 영입(싸인볼/트로피), 픽업 영입(픽업 영입권). 10/40/80회 등 픽업의 누적 확정(천장)
    /// 카운터는 GDD 원문에 구체적 판정 로직이 없고 별도의 영구 상태(세이브 필드) 설계가 필요한
    /// 규모라 이번 작업에서는 구현하지 않았다(각 픽업 호출은 독립적인 확정 1회 뽑기) - [결정 필요]
    /// 후속 과제로 남긴다.
    /// </summary>
    public class ScoutManager : MonoBehaviour
    {
        public static ScoutManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private PlayerDatabase playerDatabase;
        [SerializeField] private SkillDB skillDB;

        [Header("Cost (카테고리별 전용 재화 1개씩 소모)")]
        [SerializeField] private int liveNormalCost = 1;
        [SerializeField] private int liveEpicCost = 1;
        [SerializeField] private int pickupCost = 1;
        [SerializeField] private int premiumCost = 1;

        [Header("Grade Drop Rates (총합 100%)")]
        [Tooltip("v0.1 확정 스펙(04_card_grade_policy.md): 활성 등급 3종(SEASON/LIVE_NORMAL/LIVE_EPIC)만 뽑힌다. " +
                 "ALLSTAR 이상은 v0.5/v2.0에서 활성화 예정이라 이 표에서 제외했다.")]
        [SerializeField]
        private List<GradeDropRate> gradeDropRates = new List<GradeDropRate>
        {
            new GradeDropRate { Grade = Grade.SEASON, RatePercent = 70f },
            new GradeDropRate { Grade = Grade.LIVE_NORMAL, RatePercent = 25f },
            new GradeDropRate { Grade = Grade.LIVE_EPIC, RatePercent = 5f },
        };

        // [TASK-KBO-144] 프리미엄/픽업 영입(시그니처·타이틀 홀더)이 "타겟 등급 100% 확정"으로
        // 동작하던 치명적 기획 오류를 해소하기 위한 가중치 확률표. 위 gradeDropRates(RollGradeAtLeast
        // 필터링용, SEASON~LIVE_EPIC 3종 한정)와 별개로, 이 두 카테고리는 SIGNATURE/TITLE_HOLDER까지
        // 등급 범위가 넓어 전용 드랍 테이블이 필요하다 - RollGradeAtLeast()로 gradeDropRates를
        // 필터링하면 그 표에 SIGNATURE/TITLE_HOLDER 자체가 없어 eligible이 비고, "필터링 결과가
        // 없으면 최소 등급으로 확정"하는 안전장치(215행)가 오히려 매번 타겟 등급만 반환하는 버그로
        // 뒤바뀌어 있었다(명령서 3항이 지적한 실제 원인). 아래 4개 상수는 명령서 4항이 제시한 예시
        // 배분(타겟 최고 등급/바로 아래 등급/중간 등급/기본 등급)을 그대로 채택했다 - 기획 조정 시 이
        // 4개 값만 바꾸면 두 표 모두에 반영된다.
        private const float PremiumTargetRatePercent = 0.5f;   // 타겟 최고 등급
        private const float PremiumOneBelowRatePercent = 1.5f; // 바로 아래 등급
        private const float PremiumMidRatePercent = 3.0f;      // 중간 등급
        private const float PremiumBaseRatePercent = 95.0f;    // 기본(소비) 등급

        /// <summary>[프리미엄/픽업 영입 &gt; 시그니처] RollPremiumSignature()/RollPickupSignature() 공용
        /// 드랍 테이블. SIGNATURE(타겟) 0.5% / TITLE_HOLDER(한 단계 아래) 1.5% / ALLSTAR(중간) 3.0% /
        /// LIVE_EPIC(기본, 나머지 전부) 95.0% - 합계 100%.</summary>
        private static readonly List<GradeDropRate> SignatureDropTable = new List<GradeDropRate>
        {
            new GradeDropRate { Grade = Grade.SIGNATURE, RatePercent = PremiumTargetRatePercent },
            new GradeDropRate { Grade = Grade.TITLE_HOLDER, RatePercent = PremiumOneBelowRatePercent },
            new GradeDropRate { Grade = Grade.ALLSTAR, RatePercent = PremiumMidRatePercent },
            new GradeDropRate { Grade = Grade.LIVE_EPIC, RatePercent = PremiumBaseRatePercent },
        };

        /// <summary>[프리미엄/픽업 영입 &gt; 타이틀 홀더] RollPremiumTitleHolder()/RollPickupTitleHolder()
        /// 공용 드랍 테이블. 타겟이 시그니처보다 한 등급 낮으므로 전체 표도 한 칸씩 아래로 옮긴다 -
        /// TITLE_HOLDER(타겟) 0.5% / ALLSTAR(한 단계 아래) 1.5% / LIVE_EPIC(중간) 3.0% /
        /// LIVE_NORMAL(기본, 나머지 전부) 95.0% - 합계 100%.</summary>
        private static readonly List<GradeDropRate> TitleHolderDropTable = new List<GradeDropRate>
        {
            new GradeDropRate { Grade = Grade.TITLE_HOLDER, RatePercent = PremiumTargetRatePercent },
            new GradeDropRate { Grade = Grade.ALLSTAR, RatePercent = PremiumOneBelowRatePercent },
            new GradeDropRate { Grade = Grade.LIVE_EPIC, RatePercent = PremiumMidRatePercent },
            new GradeDropRate { Grade = Grade.LIVE_NORMAL, RatePercent = PremiumBaseRatePercent },
        };

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>[일반 영입 &gt; 라이브 일반(1~3성)] LiveNormalTicket을 소모해 {SEASON, LIVE_NORMAL}
        /// 풀에서만 추첨한다(LIVE_EPIC은 나오지 않음 - GDD가 "라이브 일반"/"라이브 에픽"을 완전히
        /// 별개 상품으로 분리했다).</summary>
        public Player RollLiveNormal()
        {
            return RollWithCurrency(
                () => GameManager.Instance.LiveNormalTicket,
                amount => GameManager.Instance.LiveNormalTicket = amount,
                liveNormalCost,
                "라이브 일반 영입권",
                () => RollGradeAtMost(Grade.LIVE_NORMAL));
        }

        /// <summary>[일반 영입 &gt; 라이브 에픽(4~5성)] LiveEpicTicket을 소모해 LIVE_EPIC 등급만
        /// 확정으로 추첨한다.</summary>
        public Player RollLiveEpic()
        {
            return RollWithCurrency(
                () => GameManager.Instance.LiveEpicTicket,
                amount => GameManager.Instance.LiveEpicTicket = amount,
                liveEpicCost,
                "라이브 에픽 영입권",
                () => RollGradeAtLeast(Grade.LIVE_EPIC));
        }

        /// <summary>[프리미엄 영입 &gt; 시그니처] SignatureBall(싸인볼)을 소모한다. [TASK-KBO-144] 기존
        /// "SIGNATURE 확정 발급"을 폐기하고 SignatureDropTable 가중치 확률로 교체했다 - 하위 등급도
        /// 확률적으로 등장한다(AC-01).</summary>
        public Player RollPremiumSignature()
        {
            return RollWithCurrency(
                () => GameManager.Instance.SignatureBall,
                amount => GameManager.Instance.SignatureBall = amount,
                premiumCost,
                "싸인볼",
                () => RollWeightedGrade(SignatureDropTable));
        }

        /// <summary>[프리미엄 영입 &gt; 타이틀 홀더] Trophy(트로피)를 소모한다. [TASK-KBO-144] 기존
        /// "TITLE_HOLDER 확정 발급"을 폐기하고 TitleHolderDropTable 가중치 확률로 교체했다.</summary>
        public Player RollPremiumTitleHolder()
        {
            return RollWithCurrency(
                () => GameManager.Instance.Trophy,
                amount => GameManager.Instance.Trophy = amount,
                premiumCost,
                "트로피",
                () => RollWeightedGrade(TitleHolderDropTable));
        }

        /// <summary>[픽업 영입 &gt; 시그니처] PickupTicket(픽업 영입권)을 소모한다. [TASK-KBO-144]
        /// RollPremiumSignature()와 동일한 SignatureDropTable을 공유해 가중치 확률로 교체했다.
        /// [결정 필요, 클래스 요약 참고] 10/40/80회 누적 확정(천장) 카운터는 여전히 미구현 - 매회
        /// 독립적인 가중치 뽑기다.</summary>
        public Player RollPickupSignature()
        {
            return RollWithCurrency(
                () => GameManager.Instance.PickupTicket,
                amount => GameManager.Instance.PickupTicket = amount,
                pickupCost,
                "픽업 영입권",
                () => RollWeightedGrade(SignatureDropTable));
        }

        /// <summary>[픽업 영입 &gt; 타이틀 홀더] PickupTicket(픽업 영입권)을 소모한다. [TASK-KBO-144]
        /// RollPremiumTitleHolder()와 동일한 TitleHolderDropTable을 공유해 가중치 확률로 교체했다.
        /// [결정 필요] RollPickupSignature()와 동일한 천장 미구현 사유.</summary>
        public Player RollPickupTitleHolder()
        {
            return RollWithCurrency(
                () => GameManager.Instance.PickupTicket,
                amount => GameManager.Instance.PickupTicket = amount,
                pickupCost,
                "픽업 영입권",
                () => RollWeightedGrade(TitleHolderDropTable));
        }

        /// <summary>
        /// 카테고리 공통 소모/발급 파이프라인 - 재화 확인, 차감, 등급 판정(gradeSelector), 카드 발급,
        /// 인벤토리 추가, 로그까지 6개 메서드가 반복하던 흐름을 하나로 묶는다. 재화가 부족하면 아무
        /// 것도 차감하지 않고 null을 반환한다.
        /// </summary>
        private Player RollWithCurrency(Func<int> getCurrency, Action<int> setCurrency, int cost,
            string currencyLabel, Func<Grade> gradeSelector)
        {
            if (GameManager.Instance == null || playerDatabase == null) return null;

            int current = getCurrency();
            if (current < cost)
            {
                Debug.LogWarning($"[ScoutManager] {currencyLabel} 부족으로 뽑기를 실행하지 않았습니다. " +
                    $"(필요 {cost} / 보유 {current})");
                return null;
            }

            setCurrency(current - cost);

            var grade = gradeSelector();
            var template = PickTemplate(grade);
            if (template == null) return null;

            var player = new Player(Guid.NewGuid().ToString(), template);
            ApplyInitialGradeRule(player, grade);
            AttachInitialSkill(player, template);

            GameManager.Instance.AddPlayerToInventory(player);
            LogAcquired(player);

            return player;
        }

        /// <summary>[TASK-KBO-121] Roll1()/Roll10()으로 획득한 선수 1명을 콘솔에 기록한다. 치어리더
        /// 가챠(CheerleaderGachaService)와 달리 선수 뽑기는 결과 로그가 전혀 없어 사용자가 결과를
        /// 인지하기 어려웠다는 QA 보고를 반영했다.</summary>
        private static void LogAcquired(Player player)
        {
            if (player?.Template == null) return;

            Debug.Log($"[ScoutManager] 선수 획득: {player.Template.PlayerName} " +
                $"(등급 {player.Template.Grade}, OVR {player.CalculateOVR(false)})");
        }

        /// <summary>
        /// 등급이 minimumGrade 이상으로 "확정"된 카드 1장을 발급한다. 재화 소모는 이 메서드의 책임이
        /// 아니다 - 호출자(SeasonRewardManager의 시즌 종료 확정팩 등)가 먼저 조건을 확인한 뒤에만
        /// 호출해야 한다. gradeDropRates 중 minimumGrade 이상인 항목들만 남겨 그 상대 확률로 다시
        /// 추첨하므로, 같은 "확정" 안에서도 상위 등급(SIGNATURE/DYNASTY 등)일수록 여전히 더 희귀하다.
        /// [TASK-KBO-129] RollWithCurrency()의 gradeSelector로도 동일 로직(RollGradeAtLeast)을 직접
        /// 재사용한다 - 이 공개 메서드는 SeasonRewardManager처럼 재화 소모 없이 등급만 확정해야 하는
        /// 외부 호출부를 위해 그대로 유지한다.
        /// </summary>
        public Player RollGuaranteed(Grade minimumGrade)
        {
            if (playerDatabase == null) return null;

            var grade = RollGradeAtLeast(minimumGrade);
            var template = PickTemplate(grade);
            if (template == null) return null;

            var player = new Player(Guid.NewGuid().ToString(), template);
            ApplyInitialGradeRule(player, grade);
            AttachInitialSkill(player, template);

            return player;
        }

        private Grade RollGradeAtLeast(Grade minimumGrade)
        {
            // Grade enum 정수값이 04_card_grade_policy.md 확정 서열(SEASON=0 ~ DYNASTY=7)과 완전히
            // 일치하도록 재배치되었으므로(TASK-KBO-032-IMPLEMENT), 이 정수 대소 비교(>=)는 곧 "등급
            // 랭크가 minimumGrade 이상인가"와 정확히 같은 의미가 되어 별도 랭크 테이블 없이도 안전하다.
            var eligible = gradeDropRates.Where(g => g.Grade >= minimumGrade).ToList();
            if (eligible.Count == 0) return minimumGrade; // 확률표에 해당 등급 이상이 없으면 최소 등급으로 확정

            float total = eligible.Sum(g => g.RatePercent);
            if (total <= 0f) return eligible[0].Grade;

            float roll = UnityEngine.Random.Range(0f, total);
            float cumulative = 0f;
            foreach (var entry in eligible)
            {
                cumulative += entry.RatePercent;
                if (roll <= cumulative) return entry.Grade;
            }

            return eligible[eligible.Count - 1].Grade;
        }

        /// <summary>[TASK-KBO-129] RollLiveNormal() 전용 - gradeDropRates 중 maximumGrade "이하"인
        /// 항목들만 남겨 그 상대 확률로 재추첨한다(RollGradeAtLeast()의 반대 방향 필터). "라이브 일반
        /// 영입"이 LIVE_EPIC을 절대 뽑지 않도록(GDD가 "라이브 일반"/"라이브 에픽"을 별개 상품으로
        /// 분리) 상한선을 둔다.</summary>
        private Grade RollGradeAtMost(Grade maximumGrade)
        {
            var eligible = gradeDropRates.Where(g => g.Grade <= maximumGrade).ToList();
            if (eligible.Count == 0) return maximumGrade;

            float total = eligible.Sum(g => g.RatePercent);
            if (total <= 0f) return eligible[0].Grade;

            float roll = UnityEngine.Random.Range(0f, total);
            float cumulative = 0f;
            foreach (var entry in eligible)
            {
                cumulative += entry.RatePercent;
                if (roll <= cumulative) return entry.Grade;
            }

            return eligible[eligible.Count - 1].Grade;
        }

        /// <summary>[TASK-KBO-144] 명령서 6항 지시대로 Random.Range(0f, 100f) + 누적 확률(Cumulative
        /// Probability) 방식으로 dropTable(총합 100%)에서 등급 하나를 추첨한다. dropTable은 List라
        /// 순서가 고정되어 있어(Dictionary와 달리 열거 순서가 런타임마다 바뀔 위험이 없음) 매 실행마다
        /// 동일한 누적 구간으로 계산된다. 부동소수점 합산 오차로 극히 드물게 roll이 마지막 누적값을
        /// 넘는 경우에도 예외 없이 마지막 항목(명령서 7항 - 기본 등급, 항상 목록의 가장 낮은 확정
        /// 등급)으로 안전하게 폴백한다.</summary>
        private static Grade RollWeightedGrade(List<GradeDropRate> dropTable)
        {
            float roll = UnityEngine.Random.Range(0f, 100f);
            float cumulative = 0f;
            foreach (var entry in dropTable)
            {
                cumulative += entry.RatePercent;
                if (roll <= cumulative) return entry.Grade;
            }

            return dropTable[dropTable.Count - 1].Grade;
        }

        private PlayerTemplate PickTemplate(Grade grade)
        {
            var candidates = playerDatabase.AllTemplates.Where(t => t.Grade == grade).ToList();
            if (candidates.Count == 0)
            {
                // 해당 등급의 템플릿이 아직 등록되지 않은 경우, 전체 풀에서 대체 추첨한다.
                candidates = playerDatabase.AllTemplates.ToList();
            }
            if (candidates.Count == 0) return null;

            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        /// <summary>
        /// GDD 2절에 명시된 등급별 초기 성급/색상 규칙을 강제한다.
        /// </summary>
        private void ApplyInitialGradeRule(Player player, Grade grade)
        {
            switch (grade)
            {
                // SEASON은 LIVE_NORMAL과 동일하게 취급한다 - 둘 다 각성 불가(Player.CanAwaken 제외 목록)에
                // 속하는 "기본/무과금 베이스" 등급이라는 점이 cards.csv 샘플(max_awaken=0)과도 일치한다.
                case Grade.SEASON:
                case Grade.LIVE_NORMAL:
                    player.CurrentStarType = StarType.NORMAL;
                    player.StarLevel = UnityEngine.Random.Range(1, 4); // 일반 1~3성 무작위
                    break;
                case Grade.LIVE_EPIC:
                    player.CurrentStarType = StarType.NORMAL;
                    player.StarLevel = 4; // 일반 4성
                    break;
                case Grade.ALLSTAR:
                    player.CurrentStarType = StarType.PURPLE;
                    player.StarLevel = 4;
                    break;
                case Grade.TITLE_HOLDER:
                    player.CurrentStarType = StarType.SILVER;
                    player.StarLevel = 5;
                    break;
                case Grade.GOLDEN_GLOVE:
                    player.CurrentStarType = StarType.GOLD;
                    player.StarLevel = 5;
                    break;
                case Grade.SIGNATURE:
                    player.CurrentStarType = StarType.PLATINUM;
                    player.StarLevel = 6;
                    break;
                case Grade.DYNASTY:
                    player.CurrentStarType = StarType.TEAM_COLOR;
                    player.StarLevel = 6;
                    break;
                default:
                    player.CurrentStarType = StarType.NORMAL;
                    player.StarLevel = Player.MinStarLevel;
                    break;
            }
        }

        private void AttachInitialSkill(Player player, PlayerTemplate template)
        {
            if (skillDB == null) return;

            var skill = skillDB.GetRandomSkill(template);
            if (skill != null)
            {
                player.AcquiredSkillIds.Add(skill.SkillName);
            }
        }
    }
}
