using System;
using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-181] 라인업 화면 [타자 라인업] / [투수 로스터] 탭 전용 슬롯 배치(순수 로직).
    ///
    /// RosterSlotLayout(TASK-177)은 "슬롯 = 카드 템플릿의 포지션/보직과 정확히 일치"만 채우므로, 실제 DB처럼 지명타자(DH) 카드가 없거나
    /// 구단 불펜 보직 분포가 쿼터(승리조 2 / 추격조 4 / 롱 1 / 마무리 1)와 다르면 빈 슬롯과 "추가" 카드가 함께 보였다. 이 뷰는 경기 엔진과
    /// 같은 기준으로 칸을 채운다.
    ///   - 타자: 포지션별 최고 OVR 주전(SetDeckEvaluator.ClassifyBatters) + 비어 있는 포지션(주로 DH)은 남은 타자 중 OVR 최상위로
    ///     채운다(MatchEngine.BuildBattingOrder 폴백과 동일). 타순 = 엔진 타순(포지션 주전이 C→DH 순서, 대체 타자는 그 뒤).
    ///     나머지 타자는 OVR 순으로 BENCH 1~6.
    ///   - 투수: 선발 = 선발 보직 OVR 상위 5(엔진 로테이션 순). 불펜 그룹(승리조/추격조/롱릴리프/마무리)은 같은 보직으로 먼저 채우고,
    ///     쿼터를 넘친 투수는 빈 불펜 칸 → 빈 선발 칸 순서로 "대체" 배치한다. 그래도 남으면 "추가" 칸으로 붙인다(숨기지 않음).
    /// 빈 칸은 Player == null이며 ToPlacementSlot()이 빈 슬롯 배치 팝업(RosterSwapRules.CanPlace) 조건을 돌려준다.
    /// </summary>
    public static class LineupView
    {
        public const int LineupSize = 9;
        public const int BenchSize = 6;
        public const int StartingPitcherSize = 5;

        public sealed class Entry
        {
            public RosterSlotLayout.SlotKind Kind;
            public BatterPosition Position;     // 타자 칸의 수비 포지션
            public PitcherRole Role;            // 투수 칸의 보직
            public string Header;              // 칸 머리글(예: "C 포수", "1선발", "추격조 2", "BENCH 3")
            public string Group;               // 투수 불펜 그룹명(승리조/추격조/롱릴리프/마무리), 그 외 빈 문자열
            public int BattingOrder;           // 타순 1~9(주전 타자 칸, 빈 칸이면 0)
            public Player Player;
            public bool IsFill;                // 보직/포지션이 다른 선수로 대체 배치된 칸
            public bool IsExtra;               // 정원 밖 추가 칸

            /// <summary>빈 칸 배치 조건. DH처럼 대체 배치가 필요한 주전 칸은 "아무 타자"(후보 규칙)로 연다.</summary>
            public RosterSlotLayout.Slot ToPlacementSlot()
            {
                var kind = Kind == RosterSlotLayout.SlotKind.StarterBatter && Position == BatterPosition.DesignatedHitter
                    ? RosterSlotLayout.SlotKind.BenchBatter
                    : Kind;
                return new RosterSlotLayout.Slot { Kind = kind, Position = Position, Role = Role, Label = Header };
            }
        }

        public static readonly IReadOnlyList<(PitcherRole Role, int Count, string Group)> BullpenGroups = new List<(PitcherRole, int, string)>
        {
            (PitcherRole.WinningReliever, 2, "승리조"),
            (PitcherRole.MopUpReliever, 4, "추격조"),
            (PitcherRole.LongReliever, 1, "롱릴리프"),
            (PitcherRole.Closer, 1, "마무리"),
        };

        public static string PositionName(BatterPosition position) => position switch
        {
            BatterPosition.Catcher => "포수",
            BatterPosition.FirstBase => "1루수",
            BatterPosition.SecondBase => "2루수",
            BatterPosition.ThirdBase => "3루수",
            BatterPosition.ShortStop => "유격수",
            BatterPosition.LeftField => "좌익수",
            BatterPosition.CenterField => "중견수",
            BatterPosition.RightField => "우익수",
            _ => "지명타자",
        };

        private static List<Player> Valid(IEnumerable<Player> roster) =>
            (roster ?? Enumerable.Empty<Player>()).Where(p => p?.Template != null).Distinct().ToList();

        /// <summary>주전 타자 9칸(C, 1B, 2B, 3B, SS, LF, CF, RF, DH 순서).</summary>
        public static List<Entry> BuildLineup(IEnumerable<Player> roster) => BuildLineup(roster, null);

        /// <summary>[TASK-KBO-182] overrideIds = 유저 지정 타순(LineupOrder, GameManager.BattingOrderOverride).</summary>
        public static List<Entry> BuildLineup(IEnumerable<Player> roster, IReadOnlyList<string> overrideIds)
        {
            var batters = Valid(roster).Where(p => !p.Template.IsPitcher).ToList();
            SetDeckEvaluator.ClassifyBatters(batters, out var starters, out _);
            var remaining = batters.Except(starters).OrderByDescending(p => p.CalculateOVR(false)).ToList();

            var entries = new List<Entry>();
            foreach (BatterPosition position in Enum.GetValues(typeof(BatterPosition)))
            {
                var player = starters.FirstOrDefault(p => p.Template.BatterPosition == position);
                bool isFill = false;
                if (player == null && remaining.Count > 0)
                {
                    player = remaining[0];
                    remaining.RemoveAt(0);
                    isFill = true;
                }
                entries.Add(new Entry
                {
                    Kind = RosterSlotLayout.SlotKind.StarterBatter, Position = position, Player = player, IsFill = isFill,
                    Header = $"{RosterSlotLayout.PositionLabel(position)} {PositionName(position)}",
                });
            }

            // 엔진 타순: 포지션 주전(C→DH 순서) 다음에 대체 타자(OVR 순 - remaining에서 뽑은 순서와 같다).
            int order = 1;
            foreach (var entry in entries.Where(e => e.Player != null && !e.IsFill)) entry.BattingOrder = order++;
            foreach (var entry in entries.Where(e => e.Player != null && e.IsFill)) entry.BattingOrder = order++;

            if (overrideIds != null && overrideIds.Count > 0)
            {
                var filled = entries.Where(e => e.Player != null).OrderBy(e => e.BattingOrder).ToList();
                var reordered = LineupOrder.Apply(filled.Select(e => e.Player).ToList(), overrideIds);
                foreach (var entry in filled) entry.BattingOrder = reordered.IndexOf(entry.Player) + 1;
            }
            return entries;
        }

        /// <summary>후보 타자 BENCH 1~6(라인업 9칸에 쓰이지 않은 타자, OVR 순). 6명을 넘으면 "추가" 칸이 붙는다.</summary>
        public static List<Entry> BuildBench(IEnumerable<Player> roster)
        {
            var valid = Valid(roster);
            var used = new HashSet<Player>(BuildLineup(valid, null).Where(e => e.Player != null).Select(e => e.Player));
            var bench = valid.Where(p => !p.Template.IsPitcher && !used.Contains(p)).OrderByDescending(p => p.CalculateOVR(false)).ToList();

            var entries = new List<Entry>();
            for (int i = 0; i < Math.Max(BenchSize, bench.Count); i++)
            {
                entries.Add(new Entry
                {
                    Kind = RosterSlotLayout.SlotKind.BenchBatter, Header = i < BenchSize ? $"BENCH {i + 1}" : "BENCH 추가",
                    Player = i < bench.Count ? bench[i] : null, IsExtra = i >= BenchSize,
                });
            }
            return entries;
        }

        /// <summary>선발 1~5 + 불펜(승리조 2 · 추격조 4 · 롱릴리프 1 · 마무리 1) + 추가 칸. 반환: (선발, 불펜).</summary>
        public static (List<Entry> Starters, List<Entry> Bullpen) BuildPitchers(IEnumerable<Player> roster)
        {
            var pitchers = Valid(roster).Where(p => p.Template.IsPitcher).OrderByDescending(p => p.CalculateOVR(false)).ToList();
            var overflow = new List<Player>();

            var starterPool = pitchers.Where(p => p.Template.PitcherRole == PitcherRole.StartingPitcher).ToList();
            var starters = new List<Entry>();
            for (int i = 0; i < StartingPitcherSize; i++)
            {
                starters.Add(new Entry
                {
                    Kind = RosterSlotLayout.SlotKind.Pitcher, Role = PitcherRole.StartingPitcher, Header = $"{i + 1}선발", Group = "선발",
                    Player = i < starterPool.Count ? starterPool[i] : null,
                });
            }
            overflow.AddRange(starterPool.Skip(StartingPitcherSize));

            var bullpen = new List<Entry>();
            foreach (var (role, count, group) in BullpenGroups)
            {
                var ofRole = pitchers.Where(p => p.Template.PitcherRole == role).ToList();
                for (int i = 0; i < count; i++)
                {
                    bullpen.Add(new Entry
                    {
                        Kind = RosterSlotLayout.SlotKind.Pitcher, Role = role, Group = group,
                        Header = count > 1 ? $"{group} {i + 1}" : group,
                        Player = i < ofRole.Count ? ofRole[i] : null,
                    });
                }
                overflow.AddRange(ofRole.Skip(count));
            }

            // 넘친 투수(OVR 순)로 빈 불펜 칸 → 빈 선발 칸을 채운다(엔진은 실제 보직 목록으로 기용하므로 표시만 대체).
            overflow = overflow.OrderByDescending(p => p.CalculateOVR(false)).ToList();
            foreach (var entry in bullpen.Concat(starters).Where(e => e.Player == null))
            {
                if (overflow.Count == 0) break;
                entry.Player = overflow[0];
                entry.IsFill = true;
                overflow.RemoveAt(0);
            }
            foreach (var extra in overflow)
            {
                bullpen.Add(new Entry
                {
                    Kind = RosterSlotLayout.SlotKind.Pitcher, Role = extra.Template.PitcherRole, Group = "추가",
                    Header = "투수 추가", Player = extra, IsExtra = true,
                });
            }
            return (starters, bullpen);
        }
    }
}
