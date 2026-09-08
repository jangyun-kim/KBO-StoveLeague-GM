using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Data
{
    /// <summary>
    /// 스킬을 뽑을 대상 카테고리. GDD 7절의 "타자 / 투수(선발) / 불펜투수" 3분류를 그대로 따른다.
    /// GDD 원문은 GetRandomSkill(Position pos)로 표기되어 있으나, 프로젝트의 포지션 타입은
    /// BatterPosition/PitcherRole로 분리되어 있어 스킬 풀 선택 기준으로는 맞지 않는다.
    /// 대신 이 SkillCategory + PlayerTemplate 기반 오버로드로 동일한 목적을 수행한다.
    /// </summary>
    public enum SkillCategory
    {
        Batter,
        StartingPitcher,
        BullpenPitcher
    }

    /// <summary>
    /// 스킬이 세부 스탯에 실제로 적용되는 방식. GDD 7절은 스킬마다 서로 다른 발동 조건/수치를 갖는
    /// 텍스트 설명만 제공하므로, 우선 GDD가 구체 수치를 명시한 2개 유형만 구현하고 나머지 스킬은
    /// None(효과 없음, 이름/티어만 보유)으로 남겨 둔다. 신규 유형은 이 enum에 추가해 확장한다.
    /// </summary>
    public enum SkillEffectType
    {
        None,
        FlatStatBonus,      // 조건 없이 보유자의 세부 스탯 전체에 +Value (예: 배팅머신)
        UnderdogStatBonus   // 보유자의 OVR이 상대보다 낮을 때만 세부 스탯 전체에 +Value (예: 패기)
    }

    [Serializable]
    public class SkillEffect
    {
        public SkillEffectType EffectType;
        public int Value;
    }

    [Serializable]
    public class SkillEntry
    {
        public string SkillName;
        public SkillTier Tier;
        [TextArea] public string Description;
        public SkillEffect Effect = new SkillEffect();
    }

    [Serializable]
    public class SkillTierProbability
    {
        public SkillTier Tier;
        [Range(0f, 100f)] public float DropRatePercent;
    }

    /// <summary>
    /// 티어별 획득 확률 + 카테고리(타자/선발투수/불펜투수)별 스킬 목록을 관리하는 데이터 테이블.
    /// </summary>
    [CreateAssetMenu(fileName = "SkillDB", menuName = "KBO Manager/Skill DB", order = 3)]
    public class SkillDB : ScriptableObject
    {
        [Header("Tier Drop Rates (총합 100%)")]
        [SerializeField] private List<SkillTierProbability> tierProbabilities = new List<SkillTierProbability>();

        [Header("Skill Pools")]
        [SerializeField] private List<SkillEntry> batterSkills = new List<SkillEntry>();
        [SerializeField] private List<SkillEntry> startingPitcherSkills = new List<SkillEntry>();
        [SerializeField] private List<SkillEntry> bullpenPitcherSkills = new List<SkillEntry>();

        public IReadOnlyList<SkillTierProbability> TierProbabilities => tierProbabilities;

        private List<SkillEntry> GetPool(SkillCategory category) => category switch
        {
            SkillCategory.Batter => batterSkills,
            SkillCategory.StartingPitcher => startingPitcherSkills,
            SkillCategory.BullpenPitcher => bullpenPitcherSkills,
            _ => batterSkills
        };

        /// <summary>
        /// 카테고리에 맞는 스킬을 티어 확률에 따라 무작위로 뽑는다.
        /// 뽑힌 티어에 해당 카테고리 스킬이 없으면(예: 선발투수는 S+ 미보유) 있는 티어가 나올 때까지 재추첨한다.
        /// </summary>
        public SkillEntry GetRandomSkill(SkillCategory category)
        {
            var pool = GetPool(category);
            if (pool == null || pool.Count == 0) return null;

            const int maxAttempts = 20;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var tier = RollTier();
                var candidates = pool.Where(s => s.Tier == tier).ToList();
                if (candidates.Count > 0)
                {
                    return candidates[UnityEngine.Random.Range(0, candidates.Count)];
                }
            }

            // 재추첨으로도 걸리지 않으면(카테고리에 극히 일부 티어만 존재) 풀 전체에서 균등 추첨
            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        /// <summary>PlayerTemplate으로부터 카테고리를 자동 판별해 스킬을 뽑는 편의 오버로드.</summary>
        public SkillEntry GetRandomSkill(PlayerTemplate template)
        {
            return template == null ? null : GetRandomSkill(ResolveCategory(template));
        }

        /// <summary>
        /// 스킬 이름으로 전체 항목(티어+효과)을 역조회한다. (MatchEngine이 보유 스킬 효과를 세부 스탯에
        /// 적용할 때 사용) 3개 풀을 모두 검색하므로, 동일 이름이 여러 풀에 존재하면 검색 순서
        /// (타자→선발→불펜)상 먼저 걸리는 항목을 반환한다.
        /// </summary>
        public SkillEntry FindSkill(string skillName)
        {
            if (string.IsNullOrEmpty(skillName)) return null;

            return batterSkills.Concat(startingPitcherSkills).Concat(bullpenPitcherSkills)
                .FirstOrDefault(s => s.SkillName == skillName);
        }

        /// <summary>스킬 이름으로 티어만 역조회하는 편의 메서드.</summary>
        public SkillTier? FindTier(string skillName)
        {
            var match = FindSkill(skillName);
            return match != null ? match.Tier : (SkillTier?)null;
        }

        public static SkillCategory ResolveCategory(PlayerTemplate template)
        {
            if (!template.IsPitcher) return SkillCategory.Batter;
            return template.PitcherRole == PitcherRole.StartingPitcher
                ? SkillCategory.StartingPitcher
                : SkillCategory.BullpenPitcher;
        }

        private SkillTier RollTier()
        {
            float total = tierProbabilities.Sum(t => t.DropRatePercent);
            if (total <= 0f) return SkillTier.F;

            float roll = UnityEngine.Random.Range(0f, total);
            float cumulative = 0f;
            foreach (var entry in tierProbabilities)
            {
                cumulative += entry.DropRatePercent;
                if (roll <= cumulative) return entry.Tier;
            }
            return tierProbabilities[tierProbabilities.Count - 1].Tier;
        }

        /// <summary>
        /// GDD 7절에 명시된 티어 확률 및 타자/선발투수/불펜투수 스킬 목록으로 초기화한다.
        /// 인스펙터 우클릭(또는 톱니바퀴 메뉴) -> "Populate GDD Default Values"로 실행.
        /// </summary>
        [ContextMenu("Populate GDD Default Values")]
        public void PopulateDefaults()
        {
            tierProbabilities = new List<SkillTierProbability>
            {
                new SkillTierProbability { Tier = SkillTier.S_PLUS, DropRatePercent = 2f },
                new SkillTierProbability { Tier = SkillTier.S, DropRatePercent = 7f },
                new SkillTierProbability { Tier = SkillTier.A, DropRatePercent = 10f },
                new SkillTierProbability { Tier = SkillTier.B, DropRatePercent = 13f },
                new SkillTierProbability { Tier = SkillTier.C, DropRatePercent = 18f },
                new SkillTierProbability { Tier = SkillTier.D, DropRatePercent = 25f },
                new SkillTierProbability { Tier = SkillTier.F, DropRatePercent = 25f },
            };

            batterSkills = BuildSkills(
                (SkillTier.S_PLUS, "정밀타격"), (SkillTier.S_PLUS, "포수리드(포수)"),
                (SkillTier.S, "저니맨"), (SkillTier.S, "배팅머신"), (SkillTier.S, "컨택트히터"),
                (SkillTier.A, "순위경쟁"), (SkillTier.A, "5툴 플레이어"), (SkillTier.A, "가을사나이"), (SkillTier.A, "도전정신"), (SkillTier.A, "공포의 하위타선"),
                (SkillTier.B, "백전노장"), (SkillTier.B, "대도"), (SkillTier.B, "승리의함성"), (SkillTier.B, "난세의 영웅"), (SkillTier.B, "수비 안정성"), (SkillTier.B, "핵타선"), (SkillTier.B, "홈 어드밴티지"), (SkillTier.B, "베스트 포지션"), (SkillTier.B, "얼리스타터"),
                (SkillTier.C, "결정적 한방"), (SkillTier.C, "승부사"), (SkillTier.C, "노림수"), (SkillTier.C, "짜릿한 손길"), (SkillTier.C, "집중력"),
                (SkillTier.D, "클러치히터"), (SkillTier.D, "하이볼히터"), (SkillTier.D, "리드오프"), (SkillTier.D, "어퍼스윙"), (SkillTier.D, "대타 스페셜"), (SkillTier.D, "패기"), (SkillTier.D, "포수리드"),
                (SkillTier.F, "리그의강자"), (SkillTier.F, "빠른발")
            );

            startingPitcherSkills = BuildSkills(
                (SkillTier.S, "철완"), (SkillTier.S, "저니맨"), (SkillTier.S, "필승카드"),
                (SkillTier.A, "전천후"), (SkillTier.A, "패기"), (SkillTier.A, "순위경쟁"), (SkillTier.A, "도전정신"), (SkillTier.A, "에이스"), (SkillTier.A, "홈어드밴티지"), (SkillTier.A, "가을사나이"), (SkillTier.A, "원투펀치"),
                (SkillTier.B, "베스트 포지션"), (SkillTier.B, "난세의 영웅"), (SkillTier.B, "마당쇠"), (SkillTier.B, "첫단추"),
                (SkillTier.C, "집중력"), (SkillTier.C, "백전노장"), (SkillTier.C, "언터쳐블"), (SkillTier.C, "아티스트"), (SkillTier.C, "라이징스타"), (SkillTier.C, "승부사"), (SkillTier.C, "얼리스타터"), (SkillTier.C, "승리의함성"),
                (SkillTier.D, "위닝샷"), (SkillTier.D, "타선지원"), (SkillTier.D, "클러치피쳐"), (SkillTier.D, "흐름끊기"), (SkillTier.D, "원포인트릴리프"),
                (SkillTier.F, "리그의강자"), (SkillTier.F, "수호신")
            );

            bullpenPitcherSkills = BuildSkills(
                (SkillTier.S_PLUS, "마당쇠"),
                (SkillTier.S, "저니맨"), (SkillTier.S, "필승카드"), (SkillTier.S, "철완"), (SkillTier.S, "패기"),
                (SkillTier.A, "전천후"), (SkillTier.A, "순위경쟁"), (SkillTier.A, "도전정신"), (SkillTier.A, "에이스"), (SkillTier.A, "홈어드밴티지"), (SkillTier.A, "가을사나이"),
                (SkillTier.B, "수호신(마무리A)"), (SkillTier.B, "베스트포지션"), (SkillTier.B, "난세의 영웅"), (SkillTier.B, "라이징스타"), (SkillTier.B, "승리의함성(마무리A)"),
                (SkillTier.C, "백전노장"), (SkillTier.C, "집중력"), (SkillTier.C, "언터쳐블"), (SkillTier.C, "승부사"), (SkillTier.C, "아티스트"), (SkillTier.C, "원포인트릴리프(마무리B)"), (SkillTier.C, "원투펀치"), (SkillTier.C, "얼리스타터(마무리B)"),
                (SkillTier.D, "위닝샷"), (SkillTier.D, "타선지원"), (SkillTier.D, "클러치피쳐"), (SkillTier.D, "흐름끊기(마무리C)"), (SkillTier.D, "첫단추"),
                (SkillTier.F, "리그의강자")
            );

            // GDD 7절이 구체 수치를 명시한 2개 스킬만 실제 효과를 연결한다. (나머지는 이름/티어만 보유, 효과 None)
            // 배팅머신: "파워, 정확, 선구 능력치가 9 증가"
            SetEffect(batterSkills, "배팅머신", SkillEffectType.FlatStatBonus, 9);
            // 패기: "상대 타자의 OVR이 더 높은 경우 구위, 구속, 변화, 제구 능력치가 10 증가"
            // (투수 스킬. 배터 D티어의 동명 "패기"는 GDD에 효과가 명시되지 않아 None으로 남겨 둔다.)
            SetEffect(startingPitcherSkills, "패기", SkillEffectType.UnderdogStatBonus, 10);
            SetEffect(bullpenPitcherSkills, "패기", SkillEffectType.UnderdogStatBonus, 10);
        }

        private static void SetEffect(List<SkillEntry> skills, string skillName, SkillEffectType effectType, int value)
        {
            foreach (var skill in skills.Where(s => s.SkillName == skillName))
            {
                skill.Effect = new SkillEffect { EffectType = effectType, Value = value };
            }
        }

        private static List<SkillEntry> BuildSkills(params (SkillTier tier, string name)[] entries)
        {
            var list = new List<SkillEntry>();
            foreach (var (tier, name) in entries)
            {
                list.Add(new SkillEntry { Tier = tier, SkillName = name });
            }
            return list;
        }
    }
}
