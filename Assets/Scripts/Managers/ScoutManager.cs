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
    /// 스카우트(가챠) 매니저. ScoutReport 재화를 소모해 PlayerTemplate을 추첨하고,
    /// GDD 2절의 등급별 초기 성급/색상 규칙을 강제 적용한 뒤 SkillDB로 초기 스킬을 부여한
    /// Player 인스턴스를 발급한다.
    /// </summary>
    public class ScoutManager : MonoBehaviour
    {
        public static ScoutManager Instance { get; private set; }

        [Header("References")]
        [SerializeField] private PlayerDatabase playerDatabase;
        [SerializeField] private SkillDB skillDB;

        [Header("Cost (ScoutReport 재화 기준)")]
        [SerializeField] private int roll1Cost = 1;
        [SerializeField] private int roll10Cost = 10;

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

        /// <summary>1회 뽑기. 재화가 부족하면 아무 것도 소모하지 않고 null을 반환한다.</summary>
        public Player Roll1()
        {
            if (GameManager.Instance == null || playerDatabase == null) return null;
            if (GameManager.Instance.ScoutReport < roll1Cost) return null;

            GameManager.Instance.ScoutReport -= roll1Cost;

            var player = RollOnce();
            if (player != null) GameManager.Instance.AddPlayerToInventory(player);
            return player;
        }

        /// <summary>10연차 뽑기. 재화가 부족하면 아무 것도 소모하지 않고 빈 리스트를 반환한다.</summary>
        public List<Player> Roll10()
        {
            var results = new List<Player>();
            if (GameManager.Instance == null || playerDatabase == null) return results;
            if (GameManager.Instance.ScoutReport < roll10Cost) return results;

            GameManager.Instance.ScoutReport -= roll10Cost;

            for (int i = 0; i < 10; i++)
            {
                var player = RollOnce();
                if (player == null) continue;

                GameManager.Instance.AddPlayerToInventory(player);
                results.Add(player);
            }

            return results;
        }

        /// <summary>
        /// 프리미엄 팩(10연뽑): 10장 중 정확히 1장(마지막 슬롯)만 RollGuaranteed(minimumGuaranteedGrade)로
        /// "확정" 처리하고, 나머지 9장은 완전히 평범한 RollOnce()로 뽑는다. 재화 소모는 이 메서드의
        /// 책임이 아니다(호출자가 먼저 확인/차감).
        ///
        /// 확률 풀(gradeDropRates)에 전혀 간섭하지 않는 이유: RollGuaranteed() 내부의
        /// RollGradeAtLeast()가 하는 재정규화는 그 메서드 호출 스코프 안의 지역 변수(eligible/total)
        /// 에서만 일어날 뿐, gradeDropRates 필드 자체는 어떤 슬롯을 뽑을 때도 절대 대입/수정되지
        /// 않는다. 즉 이 10연뽑의 나머지 9장은 평소 Roll1()을 9번 부른 것과 확률적으로 완전히
        /// 동일하다 - "확정 슬롯이 있다"는 사실이 다른 9장의 등급 분포에 아무 영향도 주지 않는다.
        /// 나머지 9장 중에서도 자연 확률로 minimumGuaranteedGrade 이상이 추가로 나올 수 있다(중복
        /// 허용) - 이를 배제/필터링하지 않는 것이 의도된 동작이다(확정 슬롯을 별도로 빼내 다시
        /// 채우려 들면 그 자체가 확률 풀에 개입하는 것이 되어 버린다).
        /// </summary>
        public List<Player> RollPremiumTen(Grade minimumGuaranteedGrade)
        {
            var results = new List<Player>(10);

            for (int i = 0; i < 9; i++)
            {
                var player = RollOnce();
                if (player != null) results.Add(player);
            }

            var guaranteed = RollGuaranteed(minimumGuaranteedGrade);
            if (guaranteed != null) results.Add(guaranteed);

            return results;
        }

        /// <summary>
        /// 등급이 minimumGrade 이상으로 "확정"된 카드 1장을 발급한다. 재화 소모는 이 메서드의 책임이
        /// 아니다 - 호출자(ShopUIController 등)가 자신의 재화(프리미엄 재화 등)를 먼저 확인/차감한
        /// 뒤에만 호출해야 한다. gradeDropRates 중 minimumGrade 이상인 항목들만 남겨 그 상대 확률로
        /// 다시 추첨하므로, 같은 "확정" 안에서도 상위 등급(SIGNATURE/DYNASTY 등)일수록 여전히 더 희귀하다.
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

        private Player RollOnce()
        {
            var grade = RollGrade();
            var template = PickTemplate(grade);
            if (template == null) return null;

            var player = new Player(Guid.NewGuid().ToString(), template);
            ApplyInitialGradeRule(player, grade);
            AttachInitialSkill(player, template);

            return player;
        }

        private Grade RollGrade()
        {
            float total = gradeDropRates.Sum(g => g.RatePercent);
            if (total <= 0f) return Grade.LIVE_NORMAL;

            float roll = UnityEngine.Random.Range(0f, total);
            float cumulative = 0f;
            foreach (var entry in gradeDropRates)
            {
                cumulative += entry.RatePercent;
                if (roll <= cumulative) return entry.Grade;
            }

            return gradeDropRates[gradeDropRates.Count - 1].Grade;
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
