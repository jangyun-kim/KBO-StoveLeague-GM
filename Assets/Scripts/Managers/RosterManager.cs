using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// 인벤토리에서 샐러리 캡 이내 최고 OVR 조합의 28인(타자 15 + 투수 13) 로스터를 자동 편성한다.
    /// (구단 관리 화면의 '오토 라인업' 기능)
    /// </summary>
    public class RosterManager : MonoBehaviour
    {
        // 타자 선발 9자리: 포지션당 정확히 1명
        private static readonly BatterPosition[] StarterBatterPositions =
            (BatterPosition[])Enum.GetValues(typeof(BatterPosition));

        public const int BenchBatterCount = 6; // 타자 15 = 선발 9 + 후보 6

        // 투수 13명 배분. GDD 원문 "승리조(RP), 추격조(RP) 4인"은 두 롤 합산 4명으로 해석하고,
        // 총 인원(5+4+3+1=13)에 맞춰 롱릴리프 3명을 역산했다. (TODO: 세부 인원 배분은 기획 확정 시 조정)
        private static readonly (PitcherRole role, int count)[] PitcherRoleQuota =
        {
            (PitcherRole.StartingPitcher, 5),
            (PitcherRole.WinningReliever, 2),
            (PitcherRole.MopUpReliever, 2),
            (PitcherRole.LongReliever, 3),
            (PitcherRole.Closer, 1),
        };

        private enum SlotKind { BatterStarter, PitcherRole, BatterBench }

        private class RosterSlot
        {
            public SlotKind Kind;
            public BatterPosition RequiredBatterPosition;
            public PitcherRole RequiredPitcherRole;
            public Player Assigned;
        }

        /// <summary>
        /// 인벤토리에서 샐러리 캡 이내 최적(OVR 최우선) 28인을 자동 편성해 반환한다.
        /// 포지션 후보가 부족하거나 캡을 초과해도, 코스트가 가장 낮은 잉여 선수로 억지로 채워
        /// 28인 빈칸을 반드시 채우는 Fallback 로직을 포함한다.
        /// </summary>
        public List<Player> AutoSetRoster(List<Player> inventory, int salaryCap)
        {
            var slots = BuildEmptySlots();
            var pool = (inventory ?? new List<Player>())
                .Where(p => p != null && p.Template != null)
                .ToList();

            AssignByCategory(slots, pool);
            FallbackFillEmptySlots(slots, pool);
            EnforceSalaryCap(slots, pool, salaryCap);

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

        /// <summary>슬롯 조건에 맞는 인벤토리 후보 중 OVR이 가장 높은 선수를 우선 배정한다.</summary>
        private static void AssignByCategory(List<RosterSlot> slots, List<Player> pool)
        {
            foreach (var slot in slots)
            {
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
        /// [Fallback] 포지션/롤 후보가 부족해 비어 있는 슬롯을, 남은 인벤토리 중
        /// 코스트가 가장 낮은 선수로 무조건 채운다. (28인 정원을 반드시 채우기 위함)
        /// </summary>
        private static void FallbackFillEmptySlots(List<RosterSlot> slots, List<Player> pool)
        {
            foreach (var slot in slots)
            {
                if (slot.Assigned != null) continue;
                if (pool.Count == 0) break;

                var cheapest = pool.OrderBy(p => p.Template.Cost).First();
                slot.Assigned = cheapest;
                pool.Remove(cheapest);
            }
        }

        /// <summary>
        /// 총 코스트가 샐러리 캡을 초과하면, 코스트가 비싼 슬롯부터 조건(포지션/롤)을 만족하는
        /// 더 저렴한 잉여 인벤토리 선수로 교체해 캡 이내로 맞춘다.
        /// 대체 가능한 후보가 없으면 28인 채움을 우선시하여 캡 초과 상태를 그대로 유지한다.
        /// </summary>
        private static void EnforceSalaryCap(List<RosterSlot> slots, List<Player> pool, int salaryCap)
        {
            int TotalCost() => slots.Where(s => s.Assigned != null).Sum(s => s.Assigned.Template.Cost);

            int safety = slots.Count * Mathf.Max(1, pool.Count) + 1; // 무한루프 방지
            while (TotalCost() > salaryCap && safety-- > 0)
            {
                var expensiveSlot = slots
                    .Where(s => s.Assigned != null)
                    .OrderByDescending(s => s.Assigned.Template.Cost)
                    .FirstOrDefault();

                if (expensiveSlot == null) break;

                var cheaperAlternative = pool
                    .Where(p => MatchesSlot(p, expensiveSlot) && p.Template.Cost < expensiveSlot.Assigned.Template.Cost)
                    .OrderBy(p => p.Template.Cost)
                    .FirstOrDefault();

                if (cheaperAlternative == null) break; // 더 교체할 대체자가 없음 -> 28인 채움 유지, 캡 초과 허용

                pool.Add(expensiveSlot.Assigned);
                pool.Remove(cheaperAlternative);
                expensiveSlot.Assigned = cheaperAlternative;
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
