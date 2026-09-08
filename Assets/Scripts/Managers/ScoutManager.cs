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
        [Tooltip("GDD에 가챠 등급별 확률이 명시되지 않아 임시값. 기획 확정 후 인스펙터에서 조정할 것.")]
        [SerializeField]
        private List<GradeDropRate> gradeDropRates = new List<GradeDropRate>
        {
            new GradeDropRate { Grade = Grade.LIVE_NORMAL, RatePercent = 60f },
            new GradeDropRate { Grade = Grade.LIVE_EPIC, RatePercent = 20f },
            new GradeDropRate { Grade = Grade.ALLSTAR, RatePercent = 10f },
            new GradeDropRate { Grade = Grade.TITLE_HOLDER, RatePercent = 5f },
            new GradeDropRate { Grade = Grade.GOLDEN_GLOVE, RatePercent = 3f },
            new GradeDropRate { Grade = Grade.SIGNATURE, RatePercent = 1.5f },
            new GradeDropRate { Grade = Grade.DYNASTY, RatePercent = 0.5f },
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
