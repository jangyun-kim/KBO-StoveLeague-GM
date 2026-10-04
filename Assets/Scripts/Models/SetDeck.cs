using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;

namespace KBOManager.Models
{
    /// <summary>[TASK-KBO-172] 세트덱 버프가 적용되는 대상 그룹(기획 고도화 자료.pdf "버프 구간 상세").</summary>
    public enum SetDeckTarget
    {
        AllPlayers,            // 모든 능력치(타자/투수 전원)
        AllBatters,            // 타자 전원
        AllPitchers,           // 투수 전원
        SelectedYearBatters,   // 연도 선택 타자(카드 SeasonYear == 선택 연도)
        SelectedYearPitchers,  // 연도 선택 투수
        InfieldCatcherBatters, // 내야/포수(1B/2B/3B/SS/C)
        OutfieldDhBatters,     // 외야/지명(LF/CF/RF/DH)
        TopOrderBatters,       // 상위 타선 1·2번
        CleanupBatters,        // 중심 타선 3·4·5번
        LowerOrderBatters,     // 하위 타선 6·7·8·9번
        StartingPitchers,      // 선발 투수
        BullpenPitchers        // 불펜(중계/마무리)
    }

    /// <summary>[TASK-KBO-172] 버프 대상 세부 능력치. PDF 용어 매핑: 정확=Contact, 선구/인내=Discipline(게임에
    /// 인내 스탯이 따로 없어 선구로 통합), 주루/주력=Speed, 수비=Defense, 구위=Stuff, 구속=Velocity,
    /// 변화=Movement, 제구=Control, 지구력=Stamina. 투수의 "수비"는 대응 스탯이 없어 무시한다.</summary>
    [Flags]
    public enum SetDeckStat
    {
        None = 0,
        Power = 1 << 0,
        Contact = 1 << 1,
        Discipline = 1 << 2,
        Speed = 1 << 3,
        Defense = 1 << 4,
        Stuff = 1 << 5,
        Velocity = 1 << 6,
        Movement = 1 << 7,
        Control = 1 << 8,
        Stamina = 1 << 9,
        AllBatter = Power | Contact | Discipline | Speed | Defense,
        AllPitcher = Stuff | Velocity | Movement | Control | Stamina,
        All = AllBatter | AllPitcher
    }

    /// <summary>[TASK-KBO-172] 유저 목표 단계(핵심 버프 구간). docs/04_card_grade_policy.md 7절 참고.</summary>
    public enum SetDeckMilestone
    {
        None,
        MinimumGoal, // 1차 목표 150P - PDF "최소 목표 스코어"
        Core,        // 2차 목표 185P/190P - PDF "핵심 버프 효과"
        Final        // 최종 목표 200P - PDF 11.4 "최종 엔드 콘텐츠"
    }

    public sealed class SetDeckEffect
    {
        public readonly SetDeckTarget Target;
        public readonly SetDeckStat Stats;
        public readonly int Amount;
        public readonly string Label;

        public SetDeckEffect(SetDeckTarget target, SetDeckStat stats, int amount, string label)
        {
            Target = target;
            Stats = stats;
            Amount = amount;
            Label = label;
        }

        public bool IsAllPlayersFlat => Target == SetDeckTarget.AllPlayers && Stats == SetDeckStat.All;
    }

    public sealed class SetDeckBracket
    {
        public readonly int Threshold;
        public readonly SetDeckEffect OptionA;
        /// <summary>선택형 구간의 두 번째 선택지(PDF의 "OR"). 고정 구간이면 null.</summary>
        public readonly SetDeckEffect OptionB;
        public readonly SetDeckMilestone Milestone;

        public SetDeckBracket(int threshold, SetDeckEffect optionA, SetDeckEffect optionB = null,
            SetDeckMilestone milestone = SetDeckMilestone.None)
        {
            Threshold = threshold;
            OptionA = optionA;
            OptionB = optionB;
            Milestone = milestone;
        }

        public bool IsSelectable => OptionB != null;

        public SetDeckEffect Resolve(SetDeckSelection selection) =>
            IsSelectable && selection != null && selection.UsesOptionB(Threshold) ? OptionB : OptionA;
    }

    /// <summary>
    /// [TASK-KBO-172] 27인 세트덱 스코어 버프 구간표 - docs/기획 고도화 자료.pdf "주요 목표 및 버프 구간"을
    /// 한 줄도 빠짐없이 옮겼다(30P~200P, 27개 구간, 누적 적용). 구간 도달 시 자동 적용되며, "OR" 구간은
    /// 유저가 A/B 중 하나를 선택한다(SetDeckSelection, 기본값 A).
    /// </summary>
    public static class SetDeckBuffTable
    {
        public const int MinimumGoalScore = 150;
        public const int FinalGoalScore = 200;
        public static readonly int[] CoreMilestoneScores = { 185, 190 };

        private static SetDeckEffect E(SetDeckTarget t, SetDeckStat s, int n, string label) => new SetDeckEffect(t, s, n, label);

        private const SetDeckStat ContactSpeedDefense = SetDeckStat.Contact | SetDeckStat.Speed | SetDeckStat.Defense;
        private const SetDeckStat PowerDiscipline = SetDeckStat.Power | SetDeckStat.Discipline;
        private const SetDeckStat ControlStuffStamina = SetDeckStat.Control | SetDeckStat.Stuff | SetDeckStat.Stamina;

        public static readonly IReadOnlyList<SetDeckBracket> Brackets = new List<SetDeckBracket>
        {
            new SetDeckBracket(30, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 1, "모든 능력치 +1")),
            new SetDeckBracket(40, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 1, "모든 능력치 +1")),
            new SetDeckBracket(50, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 1, "모든 능력치 +1")),
            new SetDeckBracket(60, E(SetDeckTarget.AllBatters, SetDeckStat.AllBatter, 1, "타자 모든 능력치 +1")),
            new SetDeckBracket(70, E(SetDeckTarget.AllPitchers, SetDeckStat.AllPitcher, 1, "투수 모든 능력치 +1")),
            new SetDeckBracket(80,
                E(SetDeckTarget.SelectedYearBatters, SetDeckStat.Power | SetDeckStat.Contact, 3, "연도 선택 타자 파워/정확 +3"),
                E(SetDeckTarget.SelectedYearPitchers, SetDeckStat.Stuff | SetDeckStat.Control, 3, "연도 선택 투수 구위/제구 +3")),
            new SetDeckBracket(90, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 2, "모든 능력치 +2")),
            new SetDeckBracket(100,
                E(SetDeckTarget.InfieldCatcherBatters, SetDeckStat.Discipline | SetDeckStat.Defense, 2, "내야/포수 인내·수비 +2"),
                E(SetDeckTarget.OutfieldDhBatters, SetDeckStat.Discipline | SetDeckStat.Speed, 2, "외야/지명 선구·주루 +2")),
            new SetDeckBracket(105, E(SetDeckTarget.AllPitchers, SetDeckStat.AllPitcher, 1, "투수 모든 능력치 +1")),
            new SetDeckBracket(110, E(SetDeckTarget.AllBatters, SetDeckStat.AllBatter, 1, "타자 모든 능력치 +1")),
            new SetDeckBracket(115,
                E(SetDeckTarget.CleanupBatters, ContactSpeedDefense, 2, "중심 타선(3~5번) 정확/주루/수비 +2"),
                E(SetDeckTarget.StartingPitchers, ControlStuffStamina, 1, "선발 투수 제구/구위/지구력 +1")),
            new SetDeckBracket(120,
                E(SetDeckTarget.CleanupBatters, SetDeckStat.AllBatter, 2, "중심 타선(3~5번) 모든 능력치 +2"),
                E(SetDeckTarget.StartingPitchers, SetDeckStat.AllPitcher, 1, "선발 투수 모든 능력치 +1")),
            new SetDeckBracket(125, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 1, "모든 능력치 +1")),
            new SetDeckBracket(130, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 1, "모든 능력치 +1")),
            new SetDeckBracket(135,
                E(SetDeckTarget.LowerOrderBatters, ContactSpeedDefense, 2, "하위 타선(6~9번) 정확/주루/수비 +2"),
                E(SetDeckTarget.BullpenPitchers, ControlStuffStamina, 2, "불펜 제구/구위/지구력 +2")),
            new SetDeckBracket(140,
                E(SetDeckTarget.LowerOrderBatters, SetDeckStat.AllBatter, 1, "하위 타선(6~9번) 모든 능력치 +1"),
                E(SetDeckTarget.BullpenPitchers, SetDeckStat.AllPitcher, 1, "불펜 모든 능력치 +1")),
            new SetDeckBracket(145, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 1, "모든 능력치 +1")),
            new SetDeckBracket(150,
                E(SetDeckTarget.TopOrderBatters, ContactSpeedDefense, 2, "상위 타선(1·2번) 정확/주루/수비 +2"),
                E(SetDeckTarget.StartingPitchers, ControlStuffStamina, 1, "선발 투수 제구/구위/지구력 +1"),
                SetDeckMilestone.MinimumGoal),
            new SetDeckBracket(155,
                E(SetDeckTarget.TopOrderBatters, PowerDiscipline, 2, "상위 타선(1·2번) 파워/선구/인내 +2"),
                E(SetDeckTarget.StartingPitchers, SetDeckStat.Velocity | SetDeckStat.Movement, 1, "선발 투수 구속/변화/수비 +1")),
            new SetDeckBracket(160, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 1, "모든 능력치 +1")),
            new SetDeckBracket(165, E(SetDeckTarget.AllBatters, ContactSpeedDefense, 1, "타자 정확/주루/수비 +1")),
            new SetDeckBracket(170, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 1, "모든 능력치 +1")),
            new SetDeckBracket(175, E(SetDeckTarget.AllBatters, PowerDiscipline, 1, "타자 파워/선구/인내 +1")),
            new SetDeckBracket(180, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 2, "모든 능력치 +2")),
            new SetDeckBracket(185,
                E(SetDeckTarget.SelectedYearBatters, SetDeckStat.AllBatter, 1, "선택 연도 타자 모든 능력치 +1"),
                E(SetDeckTarget.TopOrderBatters, SetDeckStat.Power | SetDeckStat.Speed, 2, "상위 타선(1·2번) 파워/주력 +2"),
                SetDeckMilestone.Core),
            new SetDeckBracket(190,
                E(SetDeckTarget.StartingPitchers, SetDeckStat.Stuff | SetDeckStat.Stamina, 1, "선발 투수 구위/지구력 +1"),
                E(SetDeckTarget.SelectedYearPitchers, SetDeckStat.AllPitcher, 1, "선택 연도 투수 모든 능력치 +1"),
                SetDeckMilestone.Core),
            new SetDeckBracket(195, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 1, "모든 능력치 +1")),
            new SetDeckBracket(200, E(SetDeckTarget.AllPlayers, SetDeckStat.All, 3, "모든 능력치 +3"),
                milestone: SetDeckMilestone.Final),
        };

        /// <summary>[TASK-KBO-176] 유저가 A/B를 고르는 "OR" 구간 10개(80/100/115/120/135/140/150/155/185/190P).</summary>
        public static IEnumerable<SetDeckBracket> SelectableBrackets => Brackets.Where(b => b.IsSelectable);

        public static bool IsSelectableThreshold(int threshold) => Brackets.Any(b => b.Threshold == threshold && b.IsSelectable);

        public static IEnumerable<SetDeckBracket> ReachedBrackets(int score) => Brackets.Where(b => score >= b.Threshold);

        public static SetDeckBracket NextBracket(int score) => Brackets.FirstOrDefault(b => score < b.Threshold);

        /// <summary>다음 "목표 단계"(150 -> 185 -> 190 -> 200). 모두 달성했으면 null.</summary>
        public static SetDeckBracket NextMilestone(int score) =>
            Brackets.FirstOrDefault(b => b.Milestone != SetDeckMilestone.None && score < b.Threshold);
    }

    /// <summary>[TASK-KBO-172] 유저가 고른 선택형 구간(OR) 옵션과 "연도 선택" 대상 연도. 기본값은 전 구간 A안,
    /// 선택 연도 미지정(null)이면 세트덱 안에서 카드가 가장 많은 연도를 자동 선택한다.</summary>
    [Serializable]
    public sealed class SetDeckSelection
    {
        public List<int> OptionBThresholds = new List<int>();
        public int SelectedYear; // 0 = 자동

        public bool UsesOptionB(int threshold) => OptionBThresholds != null && OptionBThresholds.Contains(threshold);

        /// <summary>[TASK-KBO-176] 선택형 구간(threshold)의 A/B를 지정한다. 선택형이 아닌 구간은 무시하고 false.</summary>
        public bool SetOption(int threshold, bool useOptionB)
        {
            if (!SetDeckBuffTable.IsSelectableThreshold(threshold)) return false;
            if (OptionBThresholds == null) OptionBThresholds = new List<int>();

            OptionBThresholds.Remove(threshold);
            if (useOptionB) OptionBThresholds.Add(threshold);
            OptionBThresholds.Sort();
            return true;
        }

        /// <summary>[TASK-KBO-176] 세이브 복원용 - 다른 선택을 덮어쓴다. 선택형이 아니거나 중복된 구간은 버리고
        /// 음수 연도는 자동(0)으로 정규화한다(손상/구버전 세이브 방어).</summary>
        public void CopyFrom(SetDeckSelection other)
        {
            OptionBThresholds = (other?.OptionBThresholds ?? new List<int>())
                .Where(SetDeckBuffTable.IsSelectableThreshold).Distinct().OrderBy(t => t).ToList();
            SelectedYear = other != null && other.SelectedYear > 0 ? other.SelectedYear : 0;
        }
    }

    /// <summary>
    /// [TASK-KBO-177] 로스터 화면의 고정 슬롯 배치(순수 로직). 로스터가 비어 있거나 일부만 차 있어도 항상
    /// 주전 타자 9(C~DH) + 후보 타자 6(BENCH 1~6) + 투수 13(SP 1~5, 승리조 RP 2, 추격조 RP 4, 롱 RP 1, 마무리 CP 1) = 28슬롯을
    /// 돌려준다 - 빈 슬롯(Player == null)을 눌러 보유 카드를 배치할 수 있게 하기 위함(TASK-176은 카드가 있어야만 클릭할 수
    /// 있어 빈 로스터에서 배치 수단이 없는 데드락이 있었다). 주전/후보 구분은 SetDeckEvaluator.ClassifyBatters()와 같고,
    /// 정원·보직 쿼터를 넘는 카드(OverwriteRoster 등으로 들어온 예외)는 슬롯 뒤에 "추가" 슬롯으로 붙여 숨기지 않는다.
    /// </summary>
    public static class RosterSlotLayout
    {
        public enum SlotKind { StarterBatter, BenchBatter, Pitcher }

        public sealed class Slot
        {
            public SlotKind Kind;
            public BatterPosition Position;
            public PitcherRole Role;
            public string Label;
            public Player Player;
            public bool IsExtra;
        }

        public const int BatterCount = 15;
        public const int PitcherCount = 13;

        /// <summary>1군 투수 13인 보직 쿼터(RosterManager 오토 라인업과 공유).</summary>
        public static readonly IReadOnlyList<(PitcherRole Role, int Count)> PitcherRoleQuota = new List<(PitcherRole, int)>
        {
            (PitcherRole.StartingPitcher, 5),
            (PitcherRole.WinningReliever, 2),
            (PitcherRole.MopUpReliever, 4),
            (PitcherRole.LongReliever, 1),
            (PitcherRole.Closer, 1),
        };

        public static string PositionLabel(BatterPosition position) => position switch
        {
            BatterPosition.Catcher => "C",
            BatterPosition.FirstBase => "1B",
            BatterPosition.SecondBase => "2B",
            BatterPosition.ThirdBase => "3B",
            BatterPosition.ShortStop => "SS",
            BatterPosition.LeftField => "LF",
            BatterPosition.CenterField => "CF",
            BatterPosition.RightField => "RF",
            _ => "DH"
        };

        public static string RoleLabel(PitcherRole role, int index) => role switch
        {
            PitcherRole.StartingPitcher => $"SP {index}",
            PitcherRole.WinningReliever => $"RP 승리조 {index}",
            PitcherRole.MopUpReliever => $"RP 추격조 {index}",
            PitcherRole.LongReliever => "RP 롱릴리프",
            _ => "CP 마무리"
        };

        public static List<Slot> Build(IReadOnlyList<Player> roster)
        {
            var valid = (roster ?? new List<Player>()).Where(p => p?.Template != null).Distinct().ToList();
            var slots = new List<Slot>();

            SetDeckEvaluator.ClassifyBatters(valid, out var starters, out var bench);
            foreach (BatterPosition position in Enum.GetValues(typeof(BatterPosition)))
            {
                slots.Add(new Slot
                {
                    Kind = SlotKind.StarterBatter, Position = position, Label = PositionLabel(position),
                    Player = starters.FirstOrDefault(p => p.Template.BatterPosition == position),
                });
            }

            var benchQueue = new Queue<Player>(bench.Concat(valid.Where(p => !p.Template.IsPitcher && !starters.Contains(p) && !bench.Contains(p))));
            for (int i = 1; i <= SetDeckEvaluator.BenchBatterSlots || benchQueue.Count > 0; i++)
            {
                slots.Add(new Slot
                {
                    Kind = SlotKind.BenchBatter, Label = $"BENCH {i}",
                    Player = benchQueue.Count > 0 ? benchQueue.Dequeue() : null,
                    IsExtra = i > SetDeckEvaluator.BenchBatterSlots,
                });
            }

            var pitchers = valid.Where(p => p.Template.IsPitcher).ToList();
            var overflow = new List<Player>();
            foreach (var (role, count) in PitcherRoleQuota)
            {
                var ofRole = pitchers.Where(p => p.Template.PitcherRole == role).ToList();
                for (int i = 0; i < count; i++)
                {
                    slots.Add(new Slot
                    {
                        Kind = SlotKind.Pitcher, Role = role, Label = RoleLabel(role, i + 1),
                        Player = i < ofRole.Count ? ofRole[i] : null,
                    });
                }
                overflow.AddRange(ofRole.Skip(count));
            }
            foreach (var extra in overflow)
            {
                slots.Add(new Slot
                {
                    Kind = SlotKind.Pitcher, Role = extra.Template.PitcherRole,
                    Label = $"{RoleLabel(extra.Template.PitcherRole, 0).Split(' ')[0]} 추가", Player = extra, IsExtra = true,
                });
            }

            return slots;
        }
    }

    /// <summary>
    /// [TASK-KBO-176] 로스터 수동 교체 규칙(순수 로직 - 로스터 화면의 카드 교체 팝업과 GameManager.SwapRosterPlayer 공용).
    /// - 후보 타자 슬롯: 로스터 밖의 아무 타자나 넣을 수 있다(세트덱 스코어 배터리 - LIVE 초월 8P 배치가 핵심 전략).
    /// - 주전 타자 슬롯: 같은 수비 포지션 타자만(포지션 공백 방지).
    /// - 투수: 같은 보직(선발/승리조/추격조/롱릴리프/마무리) 투수만(13인 보직 쿼터 유지).
    /// 후보 목록은 교체 후 예상 세트덱 스코어 내림차순, 같으면 OVR 내림차순으로 정렬한다. 주전/후보 구분은
    /// SetDeckEvaluator.ClassifyBatters()가 OVR로 파생하므로, 후보에 넣은 카드의 OVR이 같은 포지션 주전보다
    /// 높으면 다음 평가에서 주전으로 올라가고 기존 주전이 후보로 내려온다 - 어느 쪽이든 로스터 타자 15명 전원이
    /// 27인 세트덱(주전 9 + 후보 6)에 합산되므로 스코어는 같다.
    /// </summary>
    public static class RosterSwapRules
    {
        public sealed class Candidate
        {
            public Player Player;
            public int ProjectedScore;
            public int ScoreDelta;
            /// <summary>[TASK-KBO-186] 이미 로스터에 있는 선수(주전 ↔ 후보 / 선발 ↔ 불펜 위치 맞교환 대상).</summary>
            public bool InRoster;
            public string Badge = "";
        }

        public const string BenchSwapBadge = "현재 후보 · 위치 맞교환";
        public const string StarterSwapBadge = "현재 주전 · 위치 맞교환";
        public const string BullpenSwapBadge = "현재 불펜 · 위치 맞교환";
        public const string RotationSwapBadge = "현재 선발 · 위치 맞교환";

        /// <summary>[TASK-KBO-186] 로스터 안에서 outgoing과 자리를 맞바꿀 수 있는 선수 - 주전 타자면 현재 후보 전원, 후보면 현재 주전 9인,
        /// 선발 투수면 불펜 전원, 불펜이면 선발 전원. 맞교환은 로스터 구성이 같아 세트덱 스코어 변화가 없다(ScoreDelta 0). 교체 팝업 최상단에 놓인다.</summary>
        public static List<Candidate> GetLineupSwapCandidates(IReadOnlyList<Player> roster, Player outgoing,
            string favoriteTeam = null, SetDeckSelection selection = null)
        {
            var result = new List<Candidate>();
            if (roster == null || outgoing?.Template == null || !roster.Contains(outgoing)) return result;
            int score = SetDeckEvaluator.Evaluate(roster, favoriteTeam, selection).Score;

            IEnumerable<Player> partners;
            string badge;
            if (outgoing.Template.IsPitcher)
            {
                // [TASK-KBO-187] 투수 13칸은 어느 자리끼리든 맞교환(1선발 ↔ 3선발 · 선발 ↔ 마무리 등). 배지에 상대의 현재 자리를 적는다.
                var (rotationEntries, bullpenEntries) = LineupView.BuildPitchers(roster);
                foreach (var entry in rotationEntries.Concat(bullpenEntries).Where(e => e.Player != null && e.Player != outgoing))
                {
                    result.Add(new Candidate
                    {
                        Player = entry.Player, ProjectedScore = score, ScoreDelta = 0, InRoster = true,
                        Badge = $"현재 {entry.Header} · 위치 맞교환",
                    });
                }
                return result;
            }
            else
            {
                var starters = LineupAssignment.AssignStarters(roster).Where(s => s.Player != null).Select(s => s.Player).ToList();
                bool starter = starters.Contains(outgoing);
                partners = starter
                    ? roster.Where(p => p?.Template != null && !p.Template.IsPitcher && !starters.Contains(p))
                    : starters.Where(p => p != outgoing);
                badge = starter ? BenchSwapBadge : StarterSwapBadge;
            }

            foreach (var partner in partners.Distinct().OrderByDescending(p => p.CalculateOVR(false)))
                result.Add(new Candidate { Player = partner, ProjectedScore = score, ScoreDelta = 0, InRoster = true, Badge = badge });
            return result;
        }

        public static bool IsBenchBatter(IEnumerable<Player> roster, Player player)
        {
            if (player?.Template == null || player.Template.IsPitcher) return false;
            SetDeckEvaluator.ClassifyBatters(roster, out _, out var bench);
            return bench.Contains(player);
        }

        /// <summary>[TASK-KBO-182] 동일 인물 판정 키(player_id = RealPlayerId, 비어 있으면 카드 ID).</summary>
        public static string PersonKey(Player player) =>
            !string.IsNullOrEmpty(player?.Template?.RealPlayerId) ? player.Template.RealPlayerId : player?.Template?.TemplateId ?? "";

        /// <summary>[TASK-KBO-182] 같은 실존 선수의 다른 카드가 이미 로스터에 있는가(except는 빠질 카드라 제외).</summary>
        public static bool HasSamePerson(IEnumerable<Player> roster, Player incoming, Player except = null)
        {
            string key = PersonKey(incoming);
            return (roster ?? Enumerable.Empty<Player>()).Any(p => p != null && !ReferenceEquals(p, except) && !ReferenceEquals(p, incoming) && PersonKey(p) == key);
        }

        /// <summary>outgoing 자리에 incoming을 넣을 수 있는가(그룹/포지션/보직 + 로스터 밖 보유 카드 + [TASK-KBO-182] 동일 인물 중복 금지).</summary>
        public static bool CanSwap(IReadOnlyList<Player> roster, Player outgoing, Player incoming)
        {
            if (roster == null || outgoing?.Template == null || incoming?.Template == null) return false;
            if (!roster.Contains(outgoing) || roster.Contains(incoming) || ReferenceEquals(outgoing, incoming)) return false;
            if (HasSamePerson(roster, incoming, outgoing)) return false;

            var outT = outgoing.Template;
            var inT = incoming.Template;
            if (outT.IsPitcher != inT.IsPitcher) return false;
            if (outT.IsPitcher) return true; // [TASK-KBO-187] 투수 칸은 자리(1~5선발 · 불펜) 기준 - 들어온 카드가 그 자리를 그대로 이어받는다
            return IsBenchBatter(roster, outgoing) || outT.BatterPosition == inT.BatterPosition;
        }

        /// <summary>교체한 로스터 사본(같은 자리에 넣어 순서 유지). 교체 불가면 null.</summary>
        public static List<Player> BuildSwappedRoster(IReadOnlyList<Player> roster, Player outgoing, Player incoming)
        {
            if (!CanSwap(roster, outgoing, incoming)) return null;
            var swapped = roster.ToList();
            swapped[swapped.IndexOf(outgoing)] = incoming;
            return swapped;
        }

        /// <summary>[TASK-KBO-177] 빈 슬롯(slot.Player == null)에 넣을 수 있는가 - 로스터 밖 카드 + 슬롯 조건(주전=같은 포지션,
        /// 후보=아무 타자, 투수=같은 보직) + 1군 정원(타자 15 / 투수 13).</summary>
        public static bool CanPlace(IReadOnlyList<Player> roster, RosterSlotLayout.Slot slot, Player incoming)
        {
            if (roster == null || slot == null || slot.Player != null || incoming?.Template == null) return false;
            if (roster.Contains(incoming)) return false;
            if (HasSamePerson(roster, incoming)) return false; // [TASK-KBO-182] 동일 인물 중복 금지

            var t = incoming.Template;
            switch (slot.Kind)
            {
                case RosterSlotLayout.SlotKind.StarterBatter:
                    if (t.IsPitcher || t.BatterPosition != slot.Position) return false;
                    break;
                case RosterSlotLayout.SlotKind.BenchBatter:
                    if (t.IsPitcher) return false;
                    break;
                case RosterSlotLayout.SlotKind.Pitcher:
                    if (!t.IsPitcher || t.PitcherRole != slot.Role) return false;
                    break;
            }

            int sameGroup = roster.Count(p => p?.Template != null && p.Template.IsPitcher == t.IsPitcher);
            return sameGroup < (t.IsPitcher ? RosterSlotLayout.PitcherCount : RosterSlotLayout.BatterCount);
        }

        /// <summary>[TASK-KBO-177] 빈 슬롯 배치 후보 - 배치 후 예상 세트덱 스코어 높은 순(같으면 OVR 순).</summary>
        public static List<Candidate> GetPlacementCandidates(IEnumerable<Player> inventory, IReadOnlyList<Player> roster,
            RosterSlotLayout.Slot slot, string favoriteTeam = null, SetDeckSelection selection = null)
        {
            int currentScore = SetDeckEvaluator.Evaluate(roster, favoriteTeam, selection).Score;
            var result = new List<Candidate>();
            foreach (var candidate in (inventory ?? Enumerable.Empty<Player>()).Distinct())
            {
                if (!CanPlace(roster, slot, candidate)) continue;
                int projected = SetDeckEvaluator.Evaluate(roster.Append(candidate), favoriteTeam, selection).Score;
                result.Add(new Candidate { Player = candidate, ProjectedScore = projected, ScoreDelta = projected - currentScore });
            }

            return result.OrderByDescending(c => c.ProjectedScore)
                .ThenByDescending(c => c.Player.CalculateOVR(false))
                .ToList();
        }

        public static List<Candidate> GetCandidates(IEnumerable<Player> inventory, IReadOnlyList<Player> roster, Player outgoing,
            string favoriteTeam = null, SetDeckSelection selection = null)
        {
            int currentScore = SetDeckEvaluator.Evaluate(roster, favoriteTeam, selection).Score;
            var result = new List<Candidate>();
            foreach (var candidate in (inventory ?? Enumerable.Empty<Player>()).Distinct())
            {
                var swapped = BuildSwappedRoster(roster, outgoing, candidate);
                if (swapped == null) continue;

                int projected = SetDeckEvaluator.Evaluate(swapped, favoriteTeam, selection).Score;
                result.Add(new Candidate { Player = candidate, ProjectedScore = projected, ScoreDelta = projected - currentScore });
            }

            return result.OrderByDescending(c => c.ProjectedScore)
                .ThenByDescending(c => c.Player.CalculateOVR(false))
                .ToList();
        }
    }

    /// <summary>[TASK-KBO-172] 경기에 주입되는 세트덱 버프 프로필. "모든 능력치"(AllPlayers) 균등 가산은
    /// TeamPowerModifiers.SynergyBuff로 따로 전달되므로(이중 가산 방지) 여기엔 그 외 대상/부분 스탯 효과만 담는다.</summary>
    public sealed class SetDeckBuffProfile
    {
        public static readonly SetDeckBuffProfile Empty = new SetDeckBuffProfile(new List<SetDeckEffect>(), 0);

        private readonly List<SetDeckEffect> effects;
        public int SelectedYear { get; }
        public IReadOnlyList<SetDeckEffect> Effects => effects;

        public SetDeckBuffProfile(List<SetDeckEffect> effects, int selectedYear)
        {
            this.effects = effects ?? new List<SetDeckEffect>();
            SelectedYear = selectedYear;
        }

        /// <summary>battingOrderSlot: 1~9(타순), 0이면 타순 미상(타순 조건부 효과 미적용).</summary>
        public BatterStats GetBatterBonus(Player batter, int battingOrderSlot)
        {
            var bonus = new BatterStats(0, 0, 0, 0, 0);
            if (batter?.Template == null || batter.Template.IsPitcher) return bonus;

            foreach (var effect in effects)
            {
                if (!AppliesToBatter(effect.Target, batter.Template, battingOrderSlot)) continue;
                bonus = bonus + new BatterStats(
                    Pick(effect, SetDeckStat.Power), Pick(effect, SetDeckStat.Contact), Pick(effect, SetDeckStat.Discipline),
                    Pick(effect, SetDeckStat.Speed), Pick(effect, SetDeckStat.Defense));
            }
            return bonus;
        }

        public PitcherStats GetPitcherBonus(Player pitcher)
        {
            var bonus = new PitcherStats(0, 0, 0, 0, 0);
            if (pitcher?.Template == null || !pitcher.Template.IsPitcher) return bonus;

            foreach (var effect in effects)
            {
                if (!AppliesToPitcher(effect.Target, pitcher.Template)) continue;
                bonus = bonus + new PitcherStats(
                    Pick(effect, SetDeckStat.Stuff), Pick(effect, SetDeckStat.Velocity), Pick(effect, SetDeckStat.Movement),
                    Pick(effect, SetDeckStat.Control), Pick(effect, SetDeckStat.Stamina));
            }
            return bonus;
        }

        private static int Pick(SetDeckEffect effect, SetDeckStat stat) => (effect.Stats & stat) != 0 ? effect.Amount : 0;

        private bool AppliesToBatter(SetDeckTarget target, PlayerTemplate t, int slot)
        {
            switch (target)
            {
                case SetDeckTarget.AllPlayers:
                case SetDeckTarget.AllBatters: return true;
                case SetDeckTarget.SelectedYearBatters: return SelectedYear != 0 && t.SeasonYear == SelectedYear;
                case SetDeckTarget.InfieldCatcherBatters: return IsInfieldOrCatcher(t.BatterPosition);
                case SetDeckTarget.OutfieldDhBatters: return !IsInfieldOrCatcher(t.BatterPosition);
                case SetDeckTarget.TopOrderBatters: return slot >= 1 && slot <= 2;
                case SetDeckTarget.CleanupBatters: return slot >= 3 && slot <= 5;
                case SetDeckTarget.LowerOrderBatters: return slot >= 6 && slot <= 9;
                default: return false;
            }
        }

        private bool AppliesToPitcher(SetDeckTarget target, PlayerTemplate t)
        {
            switch (target)
            {
                case SetDeckTarget.AllPlayers:
                case SetDeckTarget.AllPitchers: return true;
                case SetDeckTarget.SelectedYearPitchers: return SelectedYear != 0 && t.SeasonYear == SelectedYear;
                case SetDeckTarget.StartingPitchers: return t.PitcherRole == PitcherRole.StartingPitcher;
                case SetDeckTarget.BullpenPitchers: return t.PitcherRole != PitcherRole.StartingPitcher;
                default: return false;
            }
        }

        private static bool IsInfieldOrCatcher(BatterPosition position) =>
            position == BatterPosition.Catcher || position == BatterPosition.FirstBase || position == BatterPosition.SecondBase ||
            position == BatterPosition.ThirdBase || position == BatterPosition.ShortStop;
    }

    public sealed class SetDeckResult
    {
        public static readonly SetDeckResult Empty = new SetDeckResult();

        public int Score;
        public Team DeckTeam = Team.None;
        public List<Player> SlotPlayers = new List<Player>();     // 세트덱 27인(주전 타자 9 + 후보 타자 6 + 선발 5 + 불펜 7)
        public List<Player> CountedPlayers = new List<Player>();  // 실제 스코어에 합산된 카드
        public bool IsDynastyActive;                              // 단일 구단 조건 충족 여부(DYNASTY 스코어 적용)
        public int SelectedYear;
        public List<SetDeckBracket> ReachedBrackets = new List<SetDeckBracket>();
        public SetDeckBracket NextBracket;
        public SetDeckBracket NextMilestone;
        /// <summary>"모든 능력치" 누적 합(타자/투수 공통 균등 가산) - TeamPowerModifiers.SynergyBuff와 팀 OVR 표시에 쓰인다.</summary>
        public int AllPlayersFlatBuff;
        public SetDeckBuffProfile Profile = SetDeckBuffProfile.Empty;

        public bool IsMinimumGoalMet => Score >= SetDeckBuffTable.MinimumGoalScore;
        public bool IsFinalGoalMet => Score >= SetDeckBuffTable.FinalGoalScore;
    }

    /// <summary>
    /// [TASK-KBO-172] 27인 세트덱 스코어 계산기. 규칙(docs/04_card_grade_policy.md 6절):
    /// 1) 세트덱 27인 = 주전 타자 9(포지션별 최고 OVR) + 후보 타자 6 + 선발 투수 5 + 중계/마무리 7(각 OVR 순).
    /// 2) 기준 구단(favoriteTeam, 미지정이면 로스터 최다 구단) 소속 카드만 스코어에 합산한다(자팀 선수 필수).
    ///    예외: GOLDEN_GLOVE는 소속 구단 무관 합산.
    /// 3) DYNASTY는 "단일 구단" 세트덱일 때만 합산한다 - 27인 중 GOLDEN_GLOVE를 제외한 다른 구단 카드가
    ///    한 장이라도 있으면 DYNASTY 스코어는 0(PDF: "다른 구단 선수가 있을 경우 미적용").
    /// 4) 카드 스코어 = CardGrowthRules.SetDeckScoreFor(등급, 각성).
    /// </summary>
    public static class SetDeckEvaluator
    {
        public const int StarterBatterSlots = 9;
        public const int BenchBatterSlots = 6;
        public const int StartingPitcherSlots = 5;
        public const int BullpenSlots = 7;
        public const int TotalSlots = StarterBatterSlots + BenchBatterSlots + StartingPitcherSlots + BullpenSlots; // 27

        public static List<Player> SelectSlots(IEnumerable<Player> roster)
        {
            var valid = (roster ?? Enumerable.Empty<Player>()).Where(p => p?.Template != null).Distinct().ToList();
            var pitchers = valid.Where(p => p.Template.IsPitcher).OrderByDescending(p => p.CalculateOVR(false)).ToList();

            ClassifyBatters(valid, out var starters, out var bench);
            // [TASK-KBO-187] 투수 = 라인업 투수 탭 13칸과 같은 배치(LineupView.BuildPitchers - 유저 고정 자리 반영): 1~5선발 칸 전원 +
            // 나머지 투수 중 OVR 상위 7인(불펜). 28인 중 이 규칙으로 빠지는 1인은 카드에 "제외"(SD 0)로 표시된다.
            var (rotationEntries, _) = LineupView.BuildPitchers(valid);
            var sp = rotationEntries.Where(e => e.Player != null).Select(e => e.Player).Take(StartingPitcherSlots).ToList();
            var bullpen = pitchers.Where(p => !sp.Contains(p)).Take(BullpenSlots);

            return starters.Concat(bench).Concat(sp).Concat(bullpen).ToList();
        }

        /// <summary>[TASK-KBO-187] 카드 기본 세트덱 스코어(등급 + 각성 - 구단/편성과 무관한 카드 고유값).</summary>
        public static int GetBaseCardSetDeckScore(Player card) => card?.SetDeckScore ?? 0;

        /// <summary>[TASK-KBO-187] 단일 진실 공급원 - 이 카드가 activeTeam 세트덱 총점에 실제로 더하는 값(구단 불일치 · 다이너스티 미발동이면 0).</summary>
        public static int GetEffectiveCardSetDeckScore(Player card, Team activeTeam, bool dynastyActive = true) =>
            ContributionScore(card, activeTeam, dynastyActive);

        /// <summary>[TASK-KBO-187] 평가 결과 기준 카드별 합산값 - 27인 슬롯 밖(제외)이거나 미합산이면 0. 라인업 카드 SD 배지가 쓰는 값이며
        /// 로스터 전원의 이 값 합계 == result.Score.</summary>
        public static int CardScoreIn(SetDeckResult result, Player card) =>
            result != null && card != null && result.CountedPlayers.Contains(card)
                ? GetEffectiveCardSetDeckScore(card, result.DeckTeam, result.IsDynastyActive) : 0;

        public static bool IsExcludedFromSlots(SetDeckResult result, Player card) =>
            result != null && card != null && !result.SlotPlayers.Contains(card);

        /// <summary>[TASK-KBO-176] 로스터 타자를 주전 9(포지션별 최고 OVR)와 후보 6(나머지 OVR 순)으로 나눈다 -
        /// SelectSlots()와 로스터 화면의 주전/후보 구역 표시가 같은 규칙을 쓰도록 분리했다.</summary>
        public static void ClassifyBatters(IEnumerable<Player> roster, out List<Player> starters, out List<Player> bench)
        {
            var batters = (roster ?? Enumerable.Empty<Player>())
                .Where(p => p?.Template != null && !p.Template.IsPitcher).Distinct()
                .OrderByDescending(p => p.CalculateOVR(false)).ToList();

            // [TASK-KBO-186] 유저 맞교환 고정(LineupAssignment) → 포지션별 최고 OVR. 빈 포지션 대체 타자(IsFill)는 기존처럼 후보로 센다.
            var picked = LineupAssignment.AssignStarters(batters).Where(s => s.Player != null && !s.IsFill).Select(s => s.Player).ToList();
            starters = picked;
            bench = batters.Except(picked).Take(BenchBatterSlots).ToList();
        }

        public static Team ResolveDeckTeam(IEnumerable<Player> slots, string favoriteTeam)
        {
            if (!string.IsNullOrEmpty(favoriteTeam) && Enum.TryParse(favoriteTeam, out Team parsed) && parsed != Team.None)
            {
                return parsed;
            }

            return slots.Where(p => p.Template.Team != Team.None)
                .GroupBy(p => p.Template.Team)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Select(g => g.Key)
                .DefaultIfEmpty(Team.None)
                .First();
        }

        /// <summary>[TASK-KBO-173] 이 카드가 deckTeam 세트덱에 실제로 합산될 개인 스코어 - 자팀 소속 또는
        /// GOLDEN_GLOVE면 SetDeckScore, 아니면 0. dynastyActive=false면 DYNASTY는 0(단일 구단 조건 미충족).
        /// RosterManager 오토 라인업의 후보 6인(스코어 배터리) 선발 기준으로도 쓰인다.</summary>
        public static int ContributionScore(Player player, Team deckTeam, bool dynastyActive = true)
        {
            if (player?.Template == null || deckTeam == Team.None) return 0;
            var grade = player.Template.Grade;
            bool counts = grade == Grade.GOLDEN_GLOVE || player.Template.Team == deckTeam;
            if (grade == Grade.DYNASTY && !dynastyActive) counts = false;
            return counts ? player.SetDeckScore : 0;
        }

        public static SetDeckResult Evaluate(IEnumerable<Player> roster, string favoriteTeam = null, SetDeckSelection selection = null)
        {
            var slots = SelectSlots(roster);
            if (slots.Count == 0) return SetDeckResult.Empty;

            var result = new SetDeckResult { SlotPlayers = slots };
            result.DeckTeam = ResolveDeckTeam(slots, favoriteTeam);
            if (result.DeckTeam == Team.None) return result;

            result.IsDynastyActive = slots.All(p =>
                p.Template.Team == result.DeckTeam || p.Template.Grade == Grade.GOLDEN_GLOVE);

            foreach (var player in slots)
            {
                bool counts = player.Template.Grade == Grade.GOLDEN_GLOVE || player.Template.Team == result.DeckTeam;
                if (player.Template.Grade == Grade.DYNASTY && !result.IsDynastyActive) counts = false;
                if (!counts) continue;

                result.Score += GetEffectiveCardSetDeckScore(player, result.DeckTeam, result.IsDynastyActive);
                result.CountedPlayers.Add(player);
            }

            result.SelectedYear = selection != null && selection.SelectedYear != 0
                ? selection.SelectedYear
                : result.CountedPlayers.GroupBy(p => p.Template.SeasonYear)
                    .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key)
                    .Select(g => g.Key).DefaultIfEmpty(0).First();

            var profileEffects = new List<SetDeckEffect>();
            foreach (var bracket in SetDeckBuffTable.ReachedBrackets(result.Score))
            {
                result.ReachedBrackets.Add(bracket);
                var effect = bracket.Resolve(selection);
                if (effect.IsAllPlayersFlat) result.AllPlayersFlatBuff += effect.Amount;
                else profileEffects.Add(effect);
            }

            result.NextBracket = SetDeckBuffTable.NextBracket(result.Score);
            result.NextMilestone = SetDeckBuffTable.NextMilestone(result.Score);
            result.Profile = new SetDeckBuffProfile(profileEffects, result.SelectedYear);
            return result;
        }
    }
}

namespace KBOManager.Models
{
    /// <summary>[TASK-KBO-172] 세트덱 상태 한 줄 요약(로스터 게이지/시너지 패널 공용) - "핵심 버프 구간"을
    /// 다음 목표로 노출해 유저가 150P -> 185P/190P -> 200P 순서로 목표를 잡도록 유도한다.</summary>
    public static class SetDeckUIText
    {
        public static string MilestoneLabel(SetDeckMilestone milestone) => milestone switch
        {
            SetDeckMilestone.MinimumGoal => "1차 목표",
            SetDeckMilestone.Core => "핵심 버프",
            SetDeckMilestone.Final => "최종 목표",
            _ => ""
        };

        public static string Summary(SetDeckResult setDeck)
        {
            if (setDeck == null || setDeck.SlotPlayers.Count == 0) return "세트덱 미구성";

            string head = $"세트덱 {setDeck.Score}P (모든 능력치 +{setDeck.AllPlayersFlatBuff}, {setDeck.ReachedBrackets.Count}개 구간)";
            var next = setDeck.NextMilestone;
            return next == null
                ? $"{head} · 최종 목표 달성"
                : $"{head} · 다음 {MilestoneLabel(next.Milestone)} {next.Threshold}P({next.Threshold - setDeck.Score}P 남음)";
        }
    }
}
