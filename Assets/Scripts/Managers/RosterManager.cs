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
        /// 인벤토리에서 28인을 자동 편성해 반환한다. favoriteTeam(Team.ToString())은 세트덱 기준 구단 - 생략하면 주전 중 최다 구단.
        ///
        /// [TASK-KBO-182] 선택 구단 최우선 + 동일 인물 중복 금지:
        ///   1) 주전(타자 포지션 9 / 투수 보직 13): 선택 구단 소속 중 같은 포지션·보직 OVR 최고(동률 SD) → 없을 때만 타 구단 같은 포지션·보직.
        ///   2) 후보 타자 6: 선택 구단 타자 우선(세트덱 기여 → OVR) → 선택 구단 타자가 바닥날 때만 타 구단.
        ///   3) 남은 빈칸: 같은 그룹(투수/타자) → 선택 구단 → 보직 일치 → 세트덱 기여 → OVR 순으로 채운다(정원 28 확보).
        ///   모든 단계에서 같은 실존 선수(RealPlayerId = player_id)의 다른 카드는 한 장만 들어간다 - 같은 인물의 연도/등급 카드가
        ///   여러 장이면 위 순서에서 먼저 뽑힌 카드만 쓰고 나머지는 보관한다(그 때문에 빈칸이 남을 수는 있어도 중복 편성은 없다).
        /// 예전에는 OVR만 봐서 온보딩 선물(타 구단 골든글러브)이나 타 구단 영입 카드가 선택 구단 선수를 밀어내 세트덱 스코어를 깎았다.
        /// favoriteTeam이 없으면(선택 구단 없음) 구단 우선순위 없이 기존처럼 OVR 기준이다.
        /// </summary>
        public List<Player> AutoSetRoster(List<Player> inventory, string favoriteTeam = null)
        {
            var slots = BuildEmptySlots();
            var pool = (inventory ?? new List<Player>())
                .Where(p => p != null && p.Template != null)
                .Distinct()
                .ToList();
            Team preferred = !string.IsNullOrEmpty(favoriteTeam) && Enum.TryParse(favoriteTeam, out Team parsed) ? parsed : Team.None;
            var used = new HashSet<string>();

            AssignStarters(slots, pool, preferred, used);

            var starters = slots.Where(s => s.Assigned != null).Select(s => s.Assigned);
            Team deckTeam = SetDeckEvaluator.ResolveDeckTeam(starters, favoriteTeam);

            AssignBench(slots, pool, preferred, deckTeam, used);
            FallbackFillEmptySlots(slots, pool, preferred, deckTeam, used);

            return slots.Select(s => s.Assigned).Where(p => p != null).ToList();
        }

        /// <summary>동일 인물 판정 키(player_id). 비어 있으면 카드 ID.</summary>
        public static string PersonKey(Player player) => RosterSwapRules.PersonKey(player);

        private static bool IsPreferred(Player player, Team preferred) => preferred != Team.None && player.Template.Team == preferred;

        private static void Assign(RosterSlot slot, Player player, List<Player> pool, HashSet<string> used)
        {
            slot.Assigned = player;
            pool.Remove(player);
            used.Add(PersonKey(player));
        }

        private static IEnumerable<Player> Available(List<Player> pool, HashSet<string> used) => pool.Where(p => !used.Contains(PersonKey(p)));

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

        /// <summary>주전(타자 포지션 9 + 투수 보직 13): 선택 구단 같은 포지션·보직 전원을 먼저 돌고, 그래도 빈 칸만 타 구단 같은 포지션·보직으로.</summary>
        private static void AssignStarters(List<RosterSlot> slots, List<Player> pool, Team preferred, HashSet<string> used)
        {
            if (preferred != Team.None)
            {
                FillStarterPass(slots, pool, used, p => IsPreferred(p, preferred), exactRole: true);
                // 투수 보직은 표시용 쿼터라(엔진은 실제 보직 목록으로 기용) 선택 구단의 다른 보직 투수를 타 구단 같은 보직보다 먼저 쓴다.
                FillStarterPass(slots, pool, used, p => IsPreferred(p, preferred), exactRole: false, pitchersOnly: true);
            }
            FillStarterPass(slots, pool, used, _ => true, exactRole: true);
        }

        private static void FillStarterPass(List<RosterSlot> slots, List<Player> pool, HashSet<string> used, Func<Player, bool> filter,
            bool exactRole, bool pitchersOnly = false)
        {
            foreach (var slot in slots)
            {
                if (slot.Kind == SlotKind.BatterBench || slot.Assigned != null) continue;
                if (pitchersOnly && slot.Kind != SlotKind.PitcherRole) continue;

                var candidate = Available(pool, used)
                    .Where(p => filter(p) && (exactRole ? MatchesSlot(p, slot) : p.Template.IsPitcher == (slot.Kind == SlotKind.PitcherRole)))
                    .OrderByDescending(p => p.CalculateOVR(false))
                    .ThenByDescending(p => p.SetDeckScore)
                    .FirstOrDefault();

                if (candidate != null) Assign(slot, candidate, pool, used);
            }
        }

        /// <summary>
        /// [TASK-KBO-173] 후보 타자 6인 = 세트덱 스코어 배터리. [TASK-KBO-182] 선택 구단 타자 우선 → 세트덱 기여 스코어 → OVR.
        /// 그런데 선택 구단 주전 포지션을 비워 둔 채(DH 등) 후보만 채우지 않도록, 빈 주전 칸이 있으면 폴백 단계가 먼저 채운다.
        /// </summary>
        private static void AssignBench(List<RosterSlot> slots, List<Player> pool, Team preferred, Team deckTeam, HashSet<string> used)
        {
            // 비어 있는 주전 타자 칸(주로 DH)을 후보보다 먼저 선택 구단 타자로 채운다 - 엔진 타순 9자리 우선.
            foreach (var slot in slots.Where(s => s.Kind == SlotKind.BatterStarter && s.Assigned == null))
            {
                var filler = Available(pool, used).Where(p => !p.Template.IsPitcher)
                    .OrderByDescending(p => IsPreferred(p, preferred))
                    .ThenByDescending(p => p.CalculateOVR(false))
                    .ThenByDescending(p => p.SetDeckScore)
                    .FirstOrDefault();
                if (filler != null) Assign(slot, filler, pool, used);
            }

            foreach (var slot in slots.Where(s => s.Kind == SlotKind.BatterBench && s.Assigned == null))
            {
                var candidate = Available(pool, used)
                    .Where(p => MatchesSlot(p, slot))
                    .OrderByDescending(p => IsPreferred(p, preferred))
                    .ThenByDescending(p => SetDeckEvaluator.ContributionScore(p, deckTeam))
                    .ThenByDescending(p => p.CalculateOVR(false))
                    .FirstOrDefault();

                if (candidate != null) Assign(slot, candidate, pool, used);
            }
        }

        /// <summary>
        /// [Fallback] 남은 빈칸을 정원(28)을 채우기 위해 무조건 채운다. 우선순위: 같은 그룹(투수 칸 = 투수) → 선택 구단 → 보직 일치 →
        /// 세트덱 기여 스코어 → OVR. [TASK-KBO-181] 그룹 우선(투수 칸에 타자 금지), [TASK-KBO-182] 선택 구단 우선 + 동일 인물 제외.
        /// </summary>
        private static void FallbackFillEmptySlots(List<RosterSlot> slots, List<Player> pool, Team preferred, Team deckTeam, HashSet<string> used)
        {
            foreach (var slot in slots)
            {
                if (slot.Assigned != null) continue;
                bool wantsPitcher = slot.Kind == SlotKind.PitcherRole;
                var best = Available(pool, used)
                    .OrderByDescending(p => p.Template.IsPitcher == wantsPitcher)
                    .ThenByDescending(p => IsPreferred(p, preferred))
                    .ThenByDescending(p => MatchesSlot(p, slot))
                    .ThenByDescending(p => SetDeckEvaluator.ContributionScore(p, deckTeam))
                    .ThenByDescending(p => p.CalculateOVR(false))
                    .FirstOrDefault();
                if (best == null) break;
                Assign(slot, best, pool, used);
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
