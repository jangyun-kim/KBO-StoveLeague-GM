using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>재추첨 시도 1회의 결과. 실패/대기 상태에서도 target/후보 스킬 이름은 UI 표시를 위해 채워진다.</summary>
    public enum RerollOutcome
    {
        Applied,                       // 정상 적용됨 (티켓 1장 소모됨)
        NoTicket,                      // 스킬 변경권 재고 없음 (아무 것도 소모/변경되지 않음)
        InvalidTarget,                 // target이 null이거나 Template이 없음
        InvalidSkillIndex,             // skillIndex가 target.AcquiredSkillIds 범위를 벗어남
        NoSkillAvailable,              // SkillDB에 해당 카테고리 스킬 자체가 없음
        PendingDowngradeConfirmation,  // 새로 뽑힌 스킬이 F등급이라 자동 적용을 보류함 (아무 것도 소모/변경되지 않음)
    }

    public class RerollResult
    {
        public RerollOutcome Outcome;
        public string OldSkillName;
        public string NewSkillName;
        public SkillTier? NewSkillTier;
    }

    /// <summary>
    /// GDD 7절 "스킬 변경권으로 뽑기 진행"을 처리하는 싱글톤. 스킬 변경권(ItemCategory.SkillChangeTicket)
    /// 1장을 소모해 대상 선수가 보유한 스킬 중 하나를 SkillDB의 티어 확률(S+ 2% / S 7% / A 10% ...)에
    /// 따라 새로 뽑은 스킬로 덮어씌운다. 스킬 확률 자체는 SkillDB.GetRandomSkill()을 그대로 재사용해
    /// 최초 스카우트 뽑기(ScoutManager)와 완전히 동일한 확률표를 쓴다 - 재추첨용 확률표를 별도로
    /// 두면 두 곳의 확률이 어긋날 위험이 있어 의도적으로 하나만 둔다.
    ///
    /// 안전장치: 새로 뽑힌 스킬이 최하위 F등급이면 즉시 덮어쓰지 않고 "확정 대기" 상태로 캐시만 해
    /// 둔다(티켓도 소모되지 않고 기존 스킬도 그대로 유지된다). UI가 "F등급인데 그래도 적용할까요?"를
    /// 물어본 뒤에만 ConfirmPendingReroll()을 호출해 그 캐시된 결과를 확정 적용한다 - 방금 뽑은 것과
    /// 다른 스킬이 다시 뽑히는 일이 없도록, 재추첨을 다시 하지 않고 캐시된 후보를 그대로 쓴다.
    /// </summary>
    public class SkillRerollManager : MonoBehaviour
    {
        public static SkillRerollManager Instance { get; private set; }

        [SerializeField] private SkillDB skillDB;

        private class PendingReroll
        {
            public int SkillIndex;
            public SkillEntry Candidate;
        }

        private readonly Dictionary<Player, PendingReroll> pendingByPlayer = new Dictionary<Player, PendingReroll>();

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

        /// <summary>
        /// target의 skillIndex번째 보유 스킬을 스킬 변경권 1장을 소모해 재추첨한다.
        /// 새 스킬이 F등급이면 자동 적용하지 않고 PendingDowngradeConfirmation을 반환한다(티켓 미소모) -
        /// 이 경우 ConfirmPendingReroll() 또는 CancelPendingReroll()로 후속 처리해야 한다.
        ///
        /// 부수 효과(자연 치유): candidate는 skillDB.GetRandomSkill(target.Template)로 뽑으므로 항상
        /// target의 실제 카테고리(타자/선발/불펜) 풀에 속한 유효한 스킬이다. 즉 skillIndex 자리에 예전
        /// 세이브 데이터나 이전 버그(카테고리 무결성 검증 이전 버전)로 인해 소유자 카테고리와 맞지 않는
        /// 이름이 들어 있었더라도, 재추첨 한 번으로 그 자리가 유효한 이름으로 교체돼 자동으로 복구된다 -
        /// 별도의 마이그레이션 스크립트 없이 "재추첨을 한 번 쓰면 고쳐지는" 형태의 자연 치유다.
        /// </summary>
        public RerollResult TryRerollSkill(Player target, int skillIndex)
        {
            var result = new RerollResult();

            if (target?.Template == null || skillDB == null)
            {
                result.Outcome = RerollOutcome.InvalidTarget;
                return result;
            }

            if (skillIndex < 0 || skillIndex >= target.AcquiredSkillIds.Count)
            {
                result.Outcome = RerollOutcome.InvalidSkillIndex;
                return result;
            }

            result.OldSkillName = target.AcquiredSkillIds[skillIndex];

            var ticket = FindSkillChangeTicket();
            if (ticket == null)
            {
                result.Outcome = RerollOutcome.NoTicket;
                return result;
            }

            var candidate = skillDB.GetRandomSkill(target.Template);
            if (candidate == null)
            {
                result.Outcome = RerollOutcome.NoSkillAvailable;
                return result;
            }

            Debug.Assert(skillDB.IsSkillValidForCategory(candidate.SkillName, SkillDB.ResolveCategory(target.Template)),
                $"[SkillRerollManager] GetRandomSkill이 target의 카테고리와 맞지 않는 스킬을 반환했습니다: '{candidate.SkillName}'");

            result.NewSkillName = candidate.SkillName;
            result.NewSkillTier = candidate.Tier;

            if (candidate.Tier == SkillTier.F)
            {
                // 최하위 등급 - 티켓을 쓰기 전에 확정 대기 상태로 캐시만 해 둔다. 여기서 소모/덮어쓰기 없음.
                pendingByPlayer[target] = new PendingReroll { SkillIndex = skillIndex, Candidate = candidate };
                result.Outcome = RerollOutcome.PendingDowngradeConfirmation;
                return result;
            }

            ApplyAndConsumeTicket(target, skillIndex, candidate, ticket);
            result.Outcome = RerollOutcome.Applied;
            return result;
        }

        /// <summary>
        /// PendingDowngradeConfirmation으로 보류됐던 F등급 후보를 그대로(재추첨 없이) 확정 적용한다.
        /// 대기 중이던 사이 티켓이 다른 곳에 쓰였을 수 있으므로 재고를 다시 검증한다.
        /// </summary>
        public RerollResult ConfirmPendingReroll(Player target)
        {
            var result = new RerollResult();

            if (target == null || !pendingByPlayer.TryGetValue(target, out var pending))
            {
                result.Outcome = RerollOutcome.InvalidTarget;
                return result;
            }

            var ticket = FindSkillChangeTicket();
            if (ticket == null)
            {
                result.Outcome = RerollOutcome.NoTicket;
                return result;
            }

            result.OldSkillName = target.AcquiredSkillIds[pending.SkillIndex];
            result.NewSkillName = pending.Candidate.SkillName;
            result.NewSkillTier = pending.Candidate.Tier;

            ApplyAndConsumeTicket(target, pending.SkillIndex, pending.Candidate, ticket);
            pendingByPlayer.Remove(target);

            result.Outcome = RerollOutcome.Applied;
            return result;
        }

        /// <summary>보류 중인 F등급 확정 대기를 취소한다. 기존 스킬/재화 모두 그대로 유지된다.</summary>
        public void CancelPendingReroll(Player target)
        {
            if (target != null) pendingByPlayer.Remove(target);
        }

        private static void ApplyAndConsumeTicket(Player target, int skillIndex, SkillEntry candidate, Item ticket)
        {
            GameManager.Instance.RemoveItemFromInventory(ticket);
            target.AcquiredSkillIds[skillIndex] = candidate.SkillName;
        }

        private static Item FindSkillChangeTicket()
        {
            if (GameManager.Instance == null) return null;

            return GameManager.Instance.ItemInventory
                .FirstOrDefault(i => i.Template != null && i.Template.Category == ItemCategory.SkillChangeTicket);
        }
    }
}
