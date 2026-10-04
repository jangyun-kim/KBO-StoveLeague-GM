using System;
using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-186] 라인업 선발(주전) ↔ 후보 맞교환. 주전 타자 9인은 원래 "포지션별 최고 OVR"(SetDeckEvaluator.ClassifyBatters)로만
    /// 파생돼, 이미 로스터에 있는 후보 선수를 주전 자리에 넣을 방법이 없었다. 유저가 맞교환하면 그 순간의 주전 9칸 배치를
    /// InstanceId로 고정(Starters 핀)하고, 투수는 선발 ↔ 불펜 보직을 서로 바꿔(Roles 핀) 기록한다.
    ///   - AssignStarters(): 핀 → 포지션별 최고 OVR → 빈 칸(주로 DH)은 남은 타자 OVR 순 대체. 라인업 화면 · 세트덱 분류 · 경기 엔진 타순이 모두 이 결과를 쓴다.
    ///   - RoleOf(): 핀 보직 → 카드 보직. 라인업 투수 탭과 경기 엔진 투수 운용이 모두 이 값을 쓴다.
    /// Active는 GameManager가 연결한 유저 구단 지정값이다(세이브 v11). AI 구단 카드는 InstanceId가 달라 영향을 받지 않는다.
    /// </summary>
    [Serializable]
    public sealed class LineupAssignment
    {
        [Serializable]
        public struct StarterPin
        {
            public string InstanceId;
            public BatterPosition Position;
        }

        [Serializable]
        public struct RolePin
        {
            public string InstanceId;
            public PitcherRole Role;
        }

        /// <summary>[TASK-KBO-187] 투수 13칸 고정(0~4 = 1~5선발, 5~12 = 불펜 칸 - LineupView.BullpenGroups 순서).</summary>
        [Serializable]
        public struct PitcherSlotPin
        {
            public string InstanceId;
            public int Slot;
        }

        public List<StarterPin> Starters = new List<StarterPin>();
        /// <summary>[TASK-KBO-186] 보직 핀(구버전 호환). [TASK-KBO-187]부터 투수는 PitcherSlots(13칸 자리 고정)가 우선한다.</summary>
        public List<RolePin> Roles = new List<RolePin>();
        public List<PitcherSlotPin> PitcherSlots = new List<PitcherSlotPin>();

        /// <summary>유저 구단 지정(GameManager.Awake가 연결). null이면 지정 없음(기본 OVR 편성).</summary>
        public static LineupAssignment Active;

        public sealed class Slot
        {
            public BatterPosition Position;
            public Player Player;
            public bool IsFill;     // 빈 포지션을 남은 타자로 채운 칸(엔진 타순에서 뒤로)
            public bool IsPinned;   // 유저 맞교환으로 고정된 칸
        }

        public bool IsEmpty => Starters.Count == 0 && Roles.Count == 0 && PitcherSlots.Count == 0;

        public void Clear()
        {
            Starters.Clear();
            Roles.Clear();
            PitcherSlots.Clear();
        }

        public void CopyFrom(LineupAssignment other)
        {
            Clear();
            if (other == null) return;
            if (other.Starters != null) Starters.AddRange(other.Starters.Where(p => !string.IsNullOrEmpty(p.InstanceId)));
            if (other.Roles != null) Roles.AddRange(other.Roles.Where(p => !string.IsNullOrEmpty(p.InstanceId)));
            if (other.PitcherSlots != null) PitcherSlots.AddRange(other.PitcherSlots.Where(p => !string.IsNullOrEmpty(p.InstanceId)));
        }

        /// <summary>카드가 같은 자리의 다른 카드로 바뀌었을 때(보유 카드 교체) 고정 자리/보직을 그대로 물려준다.</summary>
        public void ReplaceId(string oldId, string newId)
        {
            if (string.IsNullOrEmpty(oldId) || string.IsNullOrEmpty(newId)) return;
            for (int i = 0; i < Starters.Count; i++)
                if (Starters[i].InstanceId == oldId) Starters[i] = new StarterPin { InstanceId = newId, Position = Starters[i].Position };
            for (int i = 0; i < Roles.Count; i++)
                if (Roles[i].InstanceId == oldId) Roles[i] = new RolePin { InstanceId = newId, Role = Roles[i].Role };
            for (int i = 0; i < PitcherSlots.Count; i++)
                if (PitcherSlots[i].InstanceId == oldId) PitcherSlots[i] = new PitcherSlotPin { InstanceId = newId, Slot = PitcherSlots[i].Slot };
        }

        // ------------------------------------------------------------------ 타자

        /// <summary>주전 9칸(C → DH 순서). assignment가 null이면 Active를 쓴다.</summary>
        public static List<Slot> AssignStarters(IEnumerable<Player> roster, LineupAssignment assignment = null)
        {
            assignment = assignment ?? Active;
            var batters = (roster ?? Enumerable.Empty<Player>())
                .Where(p => p?.Template != null && !p.Template.IsPitcher).Distinct()
                .OrderByDescending(p => p.CalculateOVR(false)).ToList();
            var positions = (BatterPosition[])Enum.GetValues(typeof(BatterPosition));
            var slots = positions.Select(pos => new Slot { Position = pos }).ToList();
            var used = new HashSet<Player>();

            if (assignment != null)
            {
                foreach (var pin in assignment.Starters)
                {
                    var slot = slots.FirstOrDefault(s => s.Position == pin.Position);
                    var player = batters.FirstOrDefault(p => p.InstanceId == pin.InstanceId);
                    if (slot == null || slot.Player != null || player == null || used.Contains(player)) continue;
                    slot.Player = player;
                    slot.IsPinned = true;
                    used.Add(player);
                }
            }

            foreach (var slot in slots.Where(s => s.Player == null))
            {
                var pick = batters.FirstOrDefault(p => p.Template.BatterPosition == slot.Position && !used.Contains(p));
                if (pick == null) continue;
                slot.Player = pick;
                used.Add(pick);
            }

            foreach (var slot in slots.Where(s => s.Player == null))
            {
                var pick = batters.FirstOrDefault(p => !used.Contains(p));
                if (pick == null) break;
                slot.Player = pick;
                slot.IsFill = true;
                used.Add(pick);
            }
            return slots;
        }

        /// <summary>경기 엔진 기본 타순 - 주전(C → DH 순서) 다음에 대체 타자(OVR 순).</summary>
        public static List<Player> DefaultBattingOrder(IEnumerable<Player> roster, LineupAssignment assignment = null)
        {
            var slots = AssignStarters(roster, assignment).Where(s => s.Player != null).ToList();
            return slots.Where(s => !s.IsFill).Concat(slots.Where(s => s.IsFill)).Select(s => s.Player).ToList();
        }

        public static bool IsStarter(IEnumerable<Player> roster, Player player, LineupAssignment assignment = null) =>
            player != null && AssignStarters(roster, assignment).Any(s => s.Player == player);

        /// <summary>두 타자의 라인업 자리를 1:1로 맞바꾼다(주전 ↔ 후보, 주전 ↔ 주전). 현재 주전 9칸 전체를 고정해
        /// 다른 칸이 OVR 재계산으로 움직이지 않게 한다. 둘 다 후보면(후보끼리는 OVR 순 정렬) false.</summary>
        public bool SwapBatters(IEnumerable<Player> roster, Player a, Player b)
        {
            var list = (roster ?? Enumerable.Empty<Player>()).ToList();
            if (a?.Template == null || b?.Template == null || a == b || a.Template.IsPitcher || b.Template.IsPitcher) return false;
            if (!list.Contains(a) || !list.Contains(b)) return false;

            var slots = AssignStarters(list, this);
            var slotA = slots.FirstOrDefault(s => s.Player == a);
            var slotB = slots.FirstOrDefault(s => s.Player == b);
            if (slotA == null && slotB == null) return false;

            if (slotA != null) slotA.Player = b;
            if (slotB != null) slotB.Player = a;
            Starters = slots.Where(s => s.Player != null)
                .Select(s => new StarterPin { InstanceId = s.Player.InstanceId, Position = s.Position }).ToList();
            return true;
        }

        // ------------------------------------------------------------------ 투수

        /// <summary>투수 고정 칸 수(1~5선발 + 불펜 8칸).</summary>
        public static int PitcherSlotCount => LineupView.StartingPitcherSize + LineupView.BullpenGroups.Sum(g => g.Count);

        /// <summary>칸 인덱스의 보직(0~4 선발, 그 뒤 승리조 · 추격조 · 롱릴리프 · 마무리 순).</summary>
        public static PitcherRole SlotRole(int slot)
        {
            if (slot < LineupView.StartingPitcherSize) return PitcherRole.StartingPitcher;
            int i = slot - LineupView.StartingPitcherSize;
            foreach (var (role, count, _) in LineupView.BullpenGroups)
            {
                if (i < count) return role;
                i -= count;
            }
            return PitcherRole.Closer;
        }

        public static bool TryPitcherSlot(Player pitcher, out int slot, LineupAssignment assignment = null)
        {
            slot = -1;
            assignment = assignment ?? Active;
            if (pitcher == null || assignment == null) return false;
            foreach (var pin in assignment.PitcherSlots)
            {
                if (pin.InstanceId != pitcher.InstanceId) continue;
                slot = pin.Slot;
                return true;
            }
            return false;
        }

        /// <summary>투수 보직: 고정 칸 → [TASK-186] 보직 핀 → 카드 보직.</summary>
        public static PitcherRole RoleOf(Player pitcher, LineupAssignment assignment = null)
        {
            if (pitcher?.Template == null) return PitcherRole.StartingPitcher;
            assignment = assignment ?? Active;
            if (assignment != null)
            {
                if (TryPitcherSlot(pitcher, out int slot, assignment)) return SlotRole(slot);
                foreach (var pin in assignment.Roles)
                    if (pin.InstanceId == pitcher.InstanceId) return pin.Role;
            }
            return pitcher.Template.PitcherRole;
        }

        public static bool IsStartingPitcher(Player pitcher, LineupAssignment assignment = null) =>
            pitcher?.Template != null && pitcher.Template.IsPitcher && RoleOf(pitcher, assignment) == PitcherRole.StartingPitcher;

        /// <summary>현재 투수 13칸 배치(LineupView.BuildPitchers)를 그대로 고정한다(다른 칸이 OVR 재정렬로 움직이지 않게).</summary>
        public void FreezePitchers(IEnumerable<Player> roster)
        {
            var slots = PitcherSlotEntries(roster, this);
            PitcherSlots = slots.Select((e, i) => (e, i)).Where(x => x.e.Player != null)
                .Select(x => new PitcherSlotPin { InstanceId = x.e.Player.InstanceId, Slot = x.i }).ToList();
            Roles.Clear();
        }

        /// <summary>13칸(선발 5 + 불펜 8) 엔트리 - 정원 밖 "추가" 칸은 제외.</summary>
        public static List<LineupView.Entry> PitcherSlotEntries(IEnumerable<Player> roster, LineupAssignment assignment)
        {
            var (rotation, bullpen) = LineupView.BuildPitchers(roster, assignment);
            return rotation.Concat(bullpen.Where(e => !e.IsExtra)).ToList();
        }

        /// <summary>[TASK-KBO-187] 두 투수의 자리를 1:1로 맞바꾼다(1~5선발 · 불펜 13칸 어느 자리끼리든). 13칸 전체를 고정해
        /// OVR 재정렬이 일어나지 않는다. 같은 보직끼리(1선발 ↔ 3선발)도 로테이션 순서가 바뀌므로 허용한다.</summary>
        public bool SwapPitchers(IEnumerable<Player> roster, Player a, Player b)
        {
            var list = (roster ?? Enumerable.Empty<Player>()).ToList();
            if (a?.Template == null || b?.Template == null || a == b || !a.Template.IsPitcher || !b.Template.IsPitcher) return false;
            if (!list.Contains(a) || !list.Contains(b)) return false;

            var slots = PitcherSlotEntries(list, this);
            int ia = slots.FindIndex(e => e.Player == a), ib = slots.FindIndex(e => e.Player == b);
            if (ia < 0 && ib < 0) return false;
            if (ia >= 0) slots[ia].Player = b;
            if (ib >= 0) slots[ib].Player = a;
            PitcherSlots = slots.Select((e, i) => (e, i)).Where(x => x.e.Player != null)
                .Select(x => new PitcherSlotPin { InstanceId = x.e.Player.InstanceId, Slot = x.i }).ToList();
            Roles.Clear();
            return true;
        }

        /// <summary>같은 그룹(타자/투수) 두 선수를 맞교환한다.</summary>
        public bool Swap(IEnumerable<Player> roster, Player a, Player b)
        {
            if (a?.Template == null || b?.Template == null || a.Template.IsPitcher != b.Template.IsPitcher) return false;
            return a.Template.IsPitcher ? SwapPitchers(roster, a, b) : SwapBatters(roster, a, b);
        }
    }
}
