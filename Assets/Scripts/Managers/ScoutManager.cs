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
        [Tooltip("[TASK-KBO-155, 사용자 직접 지시] SEASON 등급이 전면 삭제되어 활성 등급 2종" +
                 "(LIVE_NORMAL/LIVE_EPIC)만 뽑힌다. 기존 SEASON 70%는 LIVE_NORMAL로 흡수해 " +
                 "비율(LIVE_NORMAL:LIVE_EPIC=25:5=5:1)을 유지한 채 재분배했다. " +
                 "ALLSTAR 이상은 v0.5/v2.0에서 활성화 예정이라 이 표에서 제외했다. " +
                 "[Inspector 재확인 필요] 이 필드가 씬에 값이 저장돼 있다면 라이브 에디터에서 직접 재설정할 것.")]
        [SerializeField]
        private List<GradeDropRate> gradeDropRates = new List<GradeDropRate>
        {
            new GradeDropRate { Grade = Grade.LIVE_NORMAL, RatePercent = 83.33f },
            new GradeDropRate { Grade = Grade.LIVE_EPIC, RatePercent = 16.67f },
        };

        // [TASK-KBO-144] 프리미엄/픽업 영입(시그니처·타이틀 홀더)이 "타겟 등급 100% 확정"으로 동작하던 기획 오류를
        // 해소하기 위한 가중치 확률표. 위 gradeDropRates(RollGradeAtLeast 필터링용, LIVE 2종 한정)와 별개다.
        // [TASK-KBO-176] 표 정의를 순수 데이터 클래스 ScoutDropTables로 옮기고 FRANCHISE를 편입했다(시그니처 상품
        // FRA 2.0%, 타이틀홀더 상품 FRA 1.0% - 기본 등급에서만 덜어내 합계 100% 유지). 확률 조정은 그 파일에서 한다.

        /// <summary>[프리미엄/픽업 영입 &gt; 시그니처] SIG 0.5 / TH 1.5 / FRA 2.0 / AS 3.0 / LIVE_EPIC 93.0 = 100%.</summary>
        private static readonly List<GradeDropRate> SignatureDropTable = ToDropRates(ScoutDropTables.Signature);

        /// <summary>[프리미엄/픽업 영입 &gt; 타이틀 홀더] TH 0.5 / FRA 1.0 / AS 1.5 / LIVE_EPIC 3.0 / LIVE_NORMAL 94.0 = 100%.</summary>
        private static readonly List<GradeDropRate> TitleHolderDropTable = ToDropRates(ScoutDropTables.TitleHolder);

        private static List<GradeDropRate> ToDropRates(IEnumerable<(Grade Grade, float RatePercent)> table) =>
            table.Select(e => new GradeDropRate { Grade = e.Grade, RatePercent = e.RatePercent }).ToList();

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
            // [TASK-KBO-175] Grade enum 정수 비교 대신 위상 순번(CardGrowthRules.PowerRank)으로 비교한다 -
            // enum 정수로는 RETIRED_NUMBER(6)가 SIGNATURE/GOLDEN_GLOVE보다 낮게 판정돼 "SIGNATURE 이상 확정"
            // 같은 필터에서 최상위 RN이 빠지는 문제가 있었다.
            int minimumRank = CardGrowthRules.PowerRank(minimumGrade);
            var eligible = gradeDropRates.Where(g => CardGrowthRules.PowerRank(g.Grade) >= minimumRank).ToList();
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
            int maximumRank = CardGrowthRules.PowerRank(maximumGrade); // [TASK-KBO-175] 위상 순번 비교
            var eligible = gradeDropRates.Where(g => CardGrowthRules.PowerRank(g.Grade) <= maximumRank).ToList();
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
                // [TASK-KBO-155, 사용자 직접 지시] SEASON 삭제 - LIVE_NORMAL이 유일한 "기본/무과금
                // 베이스" 등급이 되었다([TASK-KBO-172] 이제 LIVE도 각성/초월 가능 - CardGrowthRules 참고).
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
                case Grade.FRANCHISE: // [TASK-KBO-172 신설] 5성 브론즈
                    player.CurrentStarType = StarType.BRONZE;
                    player.StarLevel = 5;
                    break;
                case Grade.TITLE_HOLDER:
                    player.CurrentStarType = StarType.SILVER;
                    player.StarLevel = 5;
                    break;
                case Grade.RETIRED_NUMBER: // [TASK-KBO-155 신설] TITLE_HOLDER/GOLDEN_GLOVE와 동일한 5성, 검정 컬러로 구분
                    player.CurrentStarType = StarType.BLACK;
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
