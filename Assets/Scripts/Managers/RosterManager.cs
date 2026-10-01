using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// 인벤토리에서 28인(타자 15 + 투수 13) 로스터를 자동 편성한다(구단 관리 화면의 '오토 라인업' 기능).
    ///
    /// [TASK-KBO-173, Salary ↔ 세트덱 스코어 일원화] 샐러리 캡(구 FullRosterSalaryCap 1350 / EnforceSalaryCap)을
    /// 폐기했다. 이제 편성 규칙은 두 축이다:
    ///   1) 주전(타자 9 포지션 + 투수 13 보직)은 OVR 최우선 - 경기력 슬롯.
    ///   2) 후보 타자 6인은 "세트덱 기여 스코어"(기준 구단 소속 또는 GOLDEN_GLOVE일 때의 개인 스코어) 최우선,
    ///      동점이면 OVR - 스코어 배터리 슬롯. LIVE 초월(8점)이 유일한 최고점 공급원이라 자연스럽게 LIVE가 깔린다.
    /// 종결 카드 도배 방지는 캡이 아니라 역전 스코어(DYN/SIG 최대 5, GG 6 vs LIVE 8)가 담당한다 - 종결 카드로
    /// 주전을 채울수록 27인 세트덱 스코어가 떨어져 버프 구간을 잃는다(docs/04_card_grade_policy.md 10절).
    /// </summary>
    public class RosterManager : MonoBehaviour
    {
        // 타자 선발 9자리: 포지션당 정확히 1명
        private static readonly BatterPosition[] StarterBatterPositions =
            (BatterPosition[])Enum.GetValues(typeof(BatterPosition));

        public const int BenchBatterCount = 6; // 타자 15 = 선발 9 + 후보 6

        // 투수 13명 배분 (기획 확정치): 선발 5 + 승리조 2 + 추격조 4 + 롱릴리프 1 + 마무리 1 = 13명.
        // [TASK-KBO-177] 로스터 화면 고정 슬롯(RosterSlotLayout)과 같은 표를 공유한다.
        private static IReadOnlyList<(PitcherRole Role, int Count)> PitcherRoleQuota => RosterSlotLayout.PitcherRoleQuota;

        static RosterManager()
        {
            int totalPitcherQuota = PitcherRoleQuota.Sum(q => q.Count);
            Debug.Assert(totalPitcherQuota == GameManager.RequiredPitcherCount,
                $"[RosterManager] 투수 쿼터 합계 오류: {totalPitcherQuota}명 (기대값 {GameManager.RequiredPitcherCount}명)");
        }

        private enum SlotKind { BatterStarter, PitcherRole, BatterBench }

        private class RosterSlot
        {
            public SlotKind Kind;
            public BatterPosition RequiredBatterPosition;
            public PitcherRole RequiredPitcherRole;
            public Player Assigned;
        }

        /// <summary>
        /// 인벤토리에서 28인을 자동 편성해 반환한다. 주전은 OVR 최우선, 후보 타자 6인은 세트덱 기여 스코어 최우선
        /// (클래스 요약 참고). favoriteTeam(Team.ToString())은 세트덱 기준 구단 - 생략하면 주전 중 최다 구단.
        /// 포지션 후보가 부족하면 세트덱 기여 스코어가 가장 높은 잉여 선수로 28인 빈칸을 반드시 채운다.
        /// </summary>
        public List<Player> AutoSetRoster(List<Player> inventory, string favoriteTeam = null)
        {
            var slots = BuildEmptySlots();
            var pool = (inventory ?? new List<Player>())
                .Where(p => p != null && p.Template != null)
                .ToList();

            AssignStartersByOvr(slots, pool);

            var starters = slots.Where(s => s.Assigned != null).Select(s => s.Assigned);
            Team deckTeam = SetDeckEvaluator.ResolveDeckTeam(starters, favoriteTeam);

            AssignBenchBySetDeckScore(slots, pool, deckTeam);
            FallbackFillEmptySlots(slots, pool, deckTeam);

            return slots.Select(s => s.Assigned).Where(p => p != null).ToList();
        }

        private static List<RosterSlot> BuildEmptySlots()
        {
            var slots = new List<RosterSlot>(GameManager.RequiredRosterSize);

            foreach (var position in StarterBatterPositions)
            {
                slots.Add(new RosterSlot { Kind = SlotKind.BatterStarter, RequiredBatterPosition = position });
            }

            foreach (var (role, count) in PitcherRoleQuota)
            {
                for (int i = 0; i < count; i++)
                {
                    slots.Add(new RosterSlot { Kind = SlotKind.PitcherRole, RequiredPitcherRole = role });
                }
            }

            for (int i = 0; i < BenchBatterCount; i++)
            {
                slots.Add(new RosterSlot { Kind = SlotKind.BatterBench });
            }

            return slots;
        }

        /// <summary>주전 슬롯(타자 포지션 9 + 투수 보직 13)에 조건을 만족하는 후보 중 OVR이 가장 높은 선수를 배정한다.</summary>
        private static void AssignStartersByOvr(List<RosterSlot> slots, List<Player> pool)
        {
            foreach (var slot in slots)
            {
                if (slot.Kind == SlotKind.BatterBench) continue;

                var candidate = pool
                    .Where(p => MatchesSlot(p, slot))
                    .OrderByDescending(p => p.CalculateOVR(false))
                    .FirstOrDefault();

                if (candidate == null) continue;

                slot.Assigned = candidate;
                pool.Remove(candidate);
            }
        }

        /// <summary>
        /// [TASK-KBO-173] 후보 타자 6인 = 세트덱 스코어 배터리. 기준 구단 세트덱에 실제로 합산될 개인 스코어
        /// (SetDeckEvaluator.ContributionScore) 내림차순, 동점이면 OVR 내림차순으로 배정한다.
        /// </summary>
        private static void AssignBenchBySetDeckScore(List<RosterSlot> slots, List<Player> pool, Team deckTeam)
        {
            foreach (var slot in slots.Where(s => s.Kind == SlotKind.BatterBench && s.Assigned == null))
            {
                var candidate = pool
                    .Where(p => MatchesSlot(p, slot))
                    .OrderByDescending(p => SetDeckEvaluator.ContributionScore(p, deckTeam))
                    .ThenByDescending(p => p.CalculateOVR(false))
                    .FirstOrDefault();

                if (candidate == null) continue;

                slot.Assigned = candidate;
                pool.Remove(candidate);
            }
        }

        /// <summary>
        /// [Fallback] 포지션/롤 후보가 부족해 비어 있는 슬롯을, 남은 인벤토리 중 세트덱 기여 스코어가 가장 높은
        /// 선수로 무조건 채운다(28인 정원을 반드시 채우기 위함). [TASK-KBO-173] 구 기준(샐러리 코스트 최저)을 대체.
        /// </summary>
        private static void FallbackFillEmptySlots(List<RosterSlot> slots, List<Player> pool, Team deckTeam)
        {
            foreach (var slot in slots)
            {
                if (slot.Assigned != null) continue;
                if (pool.Count == 0) break;

                var best = pool
                    .OrderByDescending(p => SetDeckEvaluator.ContributionScore(p, deckTeam))
                    .ThenByDescending(p => p.CalculateOVR(false))
                    .First();
                slot.Assigned = best;
                pool.Remove(best);
            }
        }

        private static bool MatchesSlot(Player player, RosterSlot slot)
        {
            var template = player.Template;
            return slot.Kind switch
            {
                SlotKind.BatterStarter => !template.IsPitcher && template.BatterPosition == slot.RequiredBatterPosition,
                SlotKind.PitcherRole => template.IsPitcher && template.PitcherRole == slot.RequiredPitcherRole,
                SlotKind.BatterBench => !template.IsPitcher,
                _ => false
            };
        }
    }
}
