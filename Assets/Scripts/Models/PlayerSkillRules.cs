using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;

namespace KBOManager.Models
{
    /// <summary>[TASK-KBO-190] 선수 스킬 슬롯 등급(D &lt; C &lt; B &lt; A &lt; S). 정수값은 세이브(PlayerSkillSlot.Grade)와 1:1이라 순서를 바꾸지 않는다.</summary>
    public enum SkillGrade
    {
        D = 0,
        C = 1,
        B = 2,
        A = 3,
        S = 4,
    }

    /// <summary>[TASK-KBO-190] 스킬 발동 조건(경기 엔진이 타석마다 판정).</summary>
    public enum SkillTrigger
    {
        Always,             // 상시
        ScoringPosition,    // 득점권(2·3루) 주자
        RunnerOnBase,       // 주자 출루
        TwoStrikes,         // 2스트라이크 이후
        EarlyCount,         // 초구 승부(0스트라이크 카운트)
        VsStrongerOpponent, // 상대 OVR이 더 높을 때
        LateInning,         // 7회 이후(후반 · 마무리 등판)
    }

    /// <summary>[TASK-KBO-190] 카드 1장이 가진 스킬 슬롯 1칸(스킬 ID · 등급 D~S · 레벨 Lv.1~6). 세이브(PlayerSaveData.SkillSlots)에 그대로 직렬화된다.</summary>
    [Serializable]
    public class PlayerSkillSlot
    {
        public string SkillId;
        public SkillGrade Grade;
        public int Level = 1;

        public PlayerSkillSlot Clone() => new PlayerSkillSlot { SkillId = SkillId, Grade = Grade, Level = Level };
    }

    /// <summary>[TASK-KBO-190] 스킬 정의 1종 - 대상(타자/투수) · 발동 조건 · 스탯별 가중치(%, 100 = 스킬 위력 그대로).</summary>
    public sealed class PlayerSkillDef
    {
        public readonly string Id;
        public readonly string Name;
        public readonly bool ForPitcher;
        public readonly SkillTrigger Trigger;
        public readonly string Summary;
        public readonly (StatType Stat, int Weight)[] Stats;

        public PlayerSkillDef(string id, string name, bool forPitcher, SkillTrigger trigger, string summary, params (StatType, int)[] stats)
        {
            Id = id; Name = name; ForPitcher = forPitcher; Trigger = trigger; Summary = summary; Stats = stats;
        }
    }

    /// <summary>[TASK-KBO-190] 경기 중 스킬 발동 판정용 타석 상황.</summary>
    public struct SkillSituation
    {
        public bool ScoringPosition;
        public bool RunnerOnBase;
        public int Strikes;
        public int Inning;
        public bool OpponentStronger;

        public bool Satisfies(SkillTrigger trigger) => trigger switch
        {
            SkillTrigger.Always => true,
            SkillTrigger.ScoringPosition => ScoringPosition,
            SkillTrigger.RunnerOnBase => RunnerOnBase,
            SkillTrigger.TwoStrikes => Strikes >= 2,
            SkillTrigger.EarlyCount => Strikes == 0,
            SkillTrigger.VsStrongerOpponent => OpponentStronger,
            SkillTrigger.LateInning => Inning >= PlayerSkillRules.LateInningFrom,
            _ => false,
        };
    }

    /// <summary>
    /// [TASK-KBO-190] 선수 3슬롯 스킬 시스템 SSOT(순수 C# - 성장 센터 · 상세창 · 라인업 트레이 · 경기 엔진 · 단위 테스트 공용).
    ///   - 스킬 풀: 타자 8종 / 투수 8종(PlayerSkillDef). 카드 1장 = 서로 다른 스킬 3슬롯, 슬롯마다 등급 D~S · 레벨 Lv.1~6.
    ///   - 위력(스탯 포인트) = 등급 기본(D1 · C2 · B3 · A4 · S5) + (레벨 - 1) → D Lv.1 = 1 ~ S Lv.6 = 10. 스탯별 보정 = 위력 × 가중치 / 100(반올림).
    ///   - 상시 스킬은 세부 스탯 표기(상세창 "스킬 +N")와 경기 판정 모두에, 조건부 스킬은 조건이 맞는 타석에만 엔진이 가산한다.
    ///     표시 OVR · 구단 OVR(리그 권장 구간 · OVR 7 격차 법칙)에는 넣지 않는다(정적 OVR 원칙 - 스킬은 경기 안 판정 보정).
    ///   - 초기 등급 확률: 상위 시즌 카드일수록 A/S 비중이 높다(GradeWeights). 기존 세이브의 슬롯 없는 카드는 InstanceId 시드로 결정적으로 부여된다.
    ///   - [스킬 변경]: 스킬 변경권 1장(없으면 15,000P) - 3슬롯의 스킬 종류 · 등급을 다시 뽑는다(슬롯 레벨은 유지).
    ///     [고급 스킬 변경]: 고급 스킬 변경권 1장 - 같은 재추첨 + 최소 1슬롯 A~S 등급 확정.
    ///   - [스킬 레벨업]: Lv.n → n+1 = 특훈권 n장(없으면 포인트 10,000 × n), 최대 Lv.6.
    /// </summary>
    public static class PlayerSkillRules
    {
        public const int SlotCount = 3;
        public const int MinLevel = 1;
        public const int MaxLevel = 6;
        public const int LateInningFrom = 7;
        public const int RerollPointCost = 15000;
        public const int LevelUpPointCostPerLevel = 10000;

        private const StatType Pow = StatType.Power, Con = StatType.Contact, Eye = StatType.Discipline, Spd = StatType.Speed, Def = StatType.Defense;
        private const StatType Stf = StatType.Stuff, Vel = StatType.Velocity, Mov = StatType.Movement, Ctl = StatType.Control, Sta = StatType.Stamina;

        public static readonly IReadOnlyList<PlayerSkillDef> BatterSkills = new[]
        {
            new PlayerSkillDef("batting_machine", "배팅 머신", false, SkillTrigger.Always, "정확 · 선구 상승", (Con, 100), (Eye, 100)),
            new PlayerSkillDef("power_instinct", "거포 본능", false, SkillTrigger.Always, "파워 · 장타 상승", (Pow, 150), (Con, 50)),
            new PlayerSkillDef("clutch_hitter", "클러치 히터", false, SkillTrigger.ScoringPosition, "득점권 정확 · 파워 상승(타점 증가)", (Con, 150), (Pow, 150)),
            new PlayerSkillDef("table_setter", "테이블세터", false, SkillTrigger.Always, "선구(출루) · 주력 상승", (Eye, 100), (Spd, 100)),
            new PlayerSkillDef("defense_fairy", "수비 요정", false, SkillTrigger.Always, "수비 · 송구 안정성 상승", (Def, 150), (Con, 50)),
            new PlayerSkillDef("ace_killer", "에이스 킬러", false, SkillTrigger.VsStrongerOpponent, "상위 OVR 투수 상대 파워 · 정확 · 선구 상승", (Pow, 100), (Con, 100), (Eye, 100)),
            new PlayerSkillDef("first_pitch", "초구 공략", false, SkillTrigger.EarlyCount, "초구 · 0스트라이크 카운트 강타", (Pow, 150), (Con, 150)),
            new PlayerSkillDef("bat_control", "배트 컨트롤", false, SkillTrigger.TwoStrikes, "2스트라이크 이후 삼진 감소", (Con, 200), (Eye, 100)),
        };

        public static readonly IReadOnlyList<PlayerSkillDef> PitcherSkills = new[]
        {
            new PlayerSkillDef("untouchable", "언터처블", true, SkillTrigger.Always, "구위 상승 · 피안타 억제", (Stf, 150), (Mov, 50)),
            new PlayerSkillDef("doctor_k", "닥터 K", true, SkillTrigger.Always, "변화 · 구속 상승(탈삼진 증가)", (Mov, 100), (Vel, 100)),
            new PlayerSkillDef("pinpoint", "칼제구", true, SkillTrigger.Always, "제구 상승 · 볼넷 억제", (Ctl, 150), (Stf, 50)),
            new PlayerSkillDef("inning_eater", "이닝 이터", true, SkillTrigger.Always, "체력 · 이닝 소화력 상승", (Sta, 150), (Ctl, 50)),
            new PlayerSkillDef("crisis_manager", "위기 관리", true, SkillTrigger.RunnerOnBase, "주자 출루 시 구위 · 변화 · 제구 상승(실점 억제)", (Stf, 100), (Mov, 100), (Ctl, 100)),
            new PlayerSkillDef("guardian", "수호신", true, SkillTrigger.LateInning, "7회 이후 · 마무리 등판 구위 · 구속 · 제구 상승", (Stf, 100), (Vel, 100), (Ctl, 100)),
            new PlayerSkillDef("fireballer", "파이어볼러", true, SkillTrigger.Always, "구속 상승", (Vel, 150), (Stf, 50)),
            new PlayerSkillDef("groundball", "땅볼 유도", true, SkillTrigger.Always, "변화 상승 · 장타 억제", (Mov, 150), (Ctl, 50)),
        };

        private static readonly Dictionary<string, PlayerSkillDef> ById =
            BatterSkills.Concat(PitcherSkills).ToDictionary(s => s.Id, s => s);

        public static PlayerSkillDef Find(string id) => id != null && ById.TryGetValue(id, out var def) ? def : null;

        public static IReadOnlyList<PlayerSkillDef> PoolFor(bool isPitcher) => isPitcher ? PitcherSkills : BatterSkills;

        public static IReadOnlyList<PlayerSkillDef> PoolFor(Player player) => PoolFor(player?.Template != null && player.Template.IsPitcher);

        // ================================================================== 위력 · 표기

        public static int GradeBase(SkillGrade grade) => (int)grade + 1;

        public static int ClampLevel(int level) => level < MinLevel ? MinLevel : level > MaxLevel ? MaxLevel : level;

        /// <summary>위력(스탯 포인트) = 등급 기본 + (레벨 - 1). D Lv.1 = 1 ~ S Lv.6 = 10.</summary>
        public static int Potency(SkillGrade grade, int level) => GradeBase(grade) + ClampLevel(level) - 1;

        public static int StatValue(SkillGrade grade, int level, int weight) => (int)Math.Round(Potency(grade, level) * weight / 100.0, MidpointRounding.AwayFromZero);

        public static string GradeLetter(SkillGrade grade) => grade.ToString();

        /// <summary>등급 뱃지 색(리치 텍스트 hex) - S 레드 · A 골드 · B 블루 · C 그린 · D 그레이.</summary>
        public static string GradeColorHex(SkillGrade grade) => grade switch
        {
            SkillGrade.S => "#FF4D6D",
            SkillGrade.A => "#FFB020",
            SkillGrade.B => "#4FC3F7",
            SkillGrade.C => "#7CD67C",
            _ => "#B0B8C8",
        };

        public static string TriggerLabel(SkillTrigger trigger) => trigger switch
        {
            SkillTrigger.Always => "상시",
            SkillTrigger.ScoringPosition => "득점권",
            SkillTrigger.RunnerOnBase => "주자 출루 시",
            SkillTrigger.TwoStrikes => "2스트라이크 이후",
            SkillTrigger.EarlyCount => "0스트라이크 카운트",
            SkillTrigger.VsStrongerOpponent => "상위 OVR 상대",
            SkillTrigger.LateInning => $"{LateInningFrom}회 이후",
            _ => "",
        };

        public static string StatLabel(StatType stat) => stat switch
        {
            StatType.Power => "파워",
            StatType.Contact => "정확",
            StatType.Discipline => "선구",
            StatType.Speed => "주력",
            StatType.Defense => "수비",
            StatType.Stuff => "구위",
            StatType.Velocity => "구속",
            StatType.Movement => "변화",
            StatType.Control => "제구",
            StatType.Stamina => "체력",
            _ => stat.ToString(),
        };

        public static string Name(PlayerSkillSlot slot) => Find(slot?.SkillId)?.Name ?? "알 수 없는 스킬";

        /// <summary>"배팅 머신 [S] Lv.3".</summary>
        public static string SlotLabel(PlayerSkillSlot slot) =>
            slot == null ? "빈 슬롯" : $"{Name(slot)} [{GradeLetter(slot.Grade)}] Lv.{ClampLevel(slot.Level)}";

        /// <summary>등급 컬러 리치 텍스트 "<color=#FF4D6D>[S]</color> 배팅 머신 Lv.3".</summary>
        public static string SlotRichLabel(PlayerSkillSlot slot) =>
            slot == null ? "빈 슬롯" : $"<color={GradeColorHex(slot.Grade)}>[{GradeLetter(slot.Grade)}]</color> {Name(slot)} Lv.{ClampLevel(slot.Level)}";

        /// <summary>"상시 · 정확 +8 · 선구 +8".</summary>
        public static string EffectText(PlayerSkillSlot slot)
        {
            var def = Find(slot?.SkillId);
            if (def == null) return "";
            var parts = def.Stats.Select(s => $"{StatLabel(s.Stat)} +{StatValue(slot.Grade, slot.Level, s.Weight)}");
            return $"{TriggerLabel(def.Trigger)} · {string.Join(" · ", parts)}";
        }

        /// <summary>상세 효과 "정확 · 선구 상승 (상시 · 정확 +8 · 선구 +8)".</summary>
        public static string DetailText(PlayerSkillSlot slot)
        {
            var def = Find(slot?.SkillId);
            return def == null ? "" : $"{def.Summary} ({EffectText(slot)})";
        }

        // ================================================================== 생성 · 보정

        /// <summary>카드 시즌 등급별 초기/재추첨 스킬 등급 가중치 D · C · B · A · S(합 100). 상위 시즌일수록 A/S 우대.</summary>
        public static int[] GradeWeights(Grade cardGrade)
        {
            switch (CardGrowthRules.PowerRank(cardGrade))
            {
                case 1: return new[] { 45, 30, 17, 6, 2 };   // LIVE 일반
                case 2: return new[] { 35, 32, 20, 10, 3 };  // LIVE 에픽
                case 3:
                case 4: return new[] { 25, 30, 25, 15, 5 };  // 올스타 · 프랜차이즈
                case 5:
                case 6: return new[] { 15, 25, 30, 22, 8 };  // 타이틀 홀더 · 골든글러브 · 영구결번
                default: return new[] { 8, 20, 32, 28, 12 }; // 시그니처 · 왕조
            }
        }

        /// <summary>A~S 확정 슬롯 가중치(A 75 · S 25).</summary>
        public static readonly int[] PremiumGuaranteeWeights = { 75, 25 };

        public static SkillGrade RollGrade(Grade cardGrade, Func<int, int> pick)
        {
            var weights = GradeWeights(cardGrade);
            int roll = SafePick(pick, weights.Sum());
            for (int i = 0; i < weights.Length; i++)
            {
                if (roll < weights[i]) return (SkillGrade)i;
                roll -= weights[i];
            }
            return SkillGrade.D;
        }

        /// <summary>서로 다른 스킬 3종 + 등급 추첨(레벨은 keepLevels가 있으면 슬롯별 유지, 없으면 Lv.1).</summary>
        public static List<PlayerSkillSlot> RollSlots(Player player, Func<int, int> pick, bool premium = false, IReadOnlyList<PlayerSkillSlot> keepLevels = null)
        {
            var result = new List<PlayerSkillSlot>();
            if (player?.Template == null) return result;
            var pool = PoolFor(player).ToList();
            var cardGrade = player.Template.Grade;
            for (int i = 0; i < SlotCount && pool.Count > 0; i++)
            {
                var def = pool[SafePick(pick, pool.Count)];
                pool.Remove(def);
                int level = keepLevels != null && i < keepLevels.Count && keepLevels[i] != null ? ClampLevel(keepLevels[i].Level) : MinLevel;
                result.Add(new PlayerSkillSlot { SkillId = def.Id, Grade = RollGrade(cardGrade, pick), Level = level });
            }
            if (premium && result.Count > 0 && result.All(s => s.Grade < SkillGrade.A))
            {
                var slot = result[SafePick(pick, result.Count)];
                slot.Grade = SafePick(pick, PremiumGuaranteeWeights.Sum()) < PremiumGuaranteeWeights[0] ? SkillGrade.A : SkillGrade.S;
            }
            return result;
        }

        /// <summary>슬롯이 정상(3칸 · 풀에 맞는 서로 다른 스킬)인지.</summary>
        public static bool HasValidSlots(Player player)
        {
            if (player?.Template == null || player.SkillSlots == null || player.SkillSlots.Count != SlotCount) return false;
            var pool = PoolFor(player);
            var ids = new HashSet<string>();
            foreach (var slot in player.SkillSlots)
            {
                if (slot == null || !pool.Any(d => d.Id == slot.SkillId) || !ids.Add(slot.SkillId)) return false;
            }
            return true;
        }

        /// <summary>
        /// 슬롯이 없거나(신규 · 구버전 세이브) 깨졌으면 InstanceId 시드로 결정적으로 3슬롯을 부여한다(같은 카드는 항상 같은 결과 - 세이브 호환).
        /// 이미 정상 슬롯이 있으면 레벨만 1~6으로 정리하고 그대로 둔다. 부여했으면 true.
        /// </summary>
        public static bool EnsureSlots(Player player)
        {
            if (player?.Template == null) return false;
            if (HasValidSlots(player))
            {
                foreach (var slot in player.SkillSlots) slot.Level = ClampLevel(slot.Level);
                return false;
            }
            var rng = new Random(StableSeed(player.InstanceId ?? player.Template.TemplateId ?? "card"));
            player.SkillSlots = RollSlots(player, n => rng.Next(n));
            return true;
        }

        /// <summary>표시용 슬롯(없으면 부여 후 반환).</summary>
        public static IReadOnlyList<PlayerSkillSlot> SlotsOf(Player player)
        {
            if (player?.Template == null) return Array.Empty<PlayerSkillSlot>();
            EnsureSlots(player);
            return player.SkillSlots;
        }

        public static int StableSeed(string text)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in text ?? "") { hash ^= c; hash *= 16777619; }
                return (int)(hash & 0x7fffffff);
            }
        }

        private static int SafePick(Func<int, int> pick, int n)
        {
            if (n <= 1) return 0;
            int value = pick != null ? pick(n) : 0;
            return value < 0 ? 0 : value >= n ? n - 1 : value;
        }

        // ================================================================== 스탯 반영

        /// <summary>조건에 맞는 슬롯의 스탯 가산 합계(타자). situation이 null이면 상시 스킬만.</summary>
        public static BatterStats BatterBonus(Player player, SkillSituation? situation = null)
        {
            int pow = 0, con = 0, eye = 0, spd = 0, def = 0;
            foreach (var (stat, value) in Contributions(player, false, situation))
            {
                switch (stat)
                {
                    case StatType.Power: pow += value; break;
                    case StatType.Contact: con += value; break;
                    case StatType.Discipline: eye += value; break;
                    case StatType.Speed: spd += value; break;
                    case StatType.Defense: def += value; break;
                }
            }
            return new BatterStats(pow, con, eye, spd, def);
        }

        /// <summary>조건에 맞는 슬롯의 스탯 가산 합계(투수). situation이 null이면 상시 스킬만.</summary>
        public static PitcherStats PitcherBonus(Player player, SkillSituation? situation = null)
        {
            int stf = 0, vel = 0, mov = 0, ctl = 0, sta = 0;
            foreach (var (stat, value) in Contributions(player, true, situation))
            {
                switch (stat)
                {
                    case StatType.Stuff: stf += value; break;
                    case StatType.Velocity: vel += value; break;
                    case StatType.Movement: mov += value; break;
                    case StatType.Control: ctl += value; break;
                    case StatType.Stamina: sta += value; break;
                }
            }
            return new PitcherStats(stf, vel, mov, ctl, sta);
        }

        /// <summary>상세창 5대 능력치 순서(타자 파워/정확/선구/주력/수비, 투수 구위/구속/변화/제구/체력)의 상시 스킬 가산.</summary>
        public static int[] AlwaysStatBonuses(Player player)
        {
            if (player?.Template == null || player.SkillSlots == null || player.SkillSlots.Count == 0) return new int[5];
            if (player.Template.IsPitcher)
            {
                var p = PitcherBonus(player);
                return new[] { p.Stuff, p.Velocity, p.Movement, p.Control, p.Stamina };
            }
            var b = BatterBonus(player);
            return new[] { b.Power, b.Contact, b.Discipline, b.Speed, b.Defense };
        }

        private static IEnumerable<(StatType Stat, int Value)> Contributions(Player player, bool pitcherSide, SkillSituation? situation)
        {
            if (player?.Template == null || player.Template.IsPitcher != pitcherSide || player.SkillSlots == null) yield break;
            foreach (var slot in player.SkillSlots)
            {
                var def = Find(slot?.SkillId);
                if (def == null || def.ForPitcher != pitcherSide) continue;
                bool active = situation.HasValue ? situation.Value.Satisfies(def.Trigger) : def.Trigger == SkillTrigger.Always;
                if (!active) continue;
                foreach (var (stat, weight) in def.Stats) yield return (stat, StatValue(slot.Grade, slot.Level, weight));
            }
        }

        // ================================================================== 스킬 변경 · 레벨업

        public static int LevelUpTicketCost(int currentLevel) => ClampLevel(currentLevel);

        public static int LevelUpPointCost(int currentLevel) => LevelUpPointCostPerLevel * ClampLevel(currentLevel);

        /// <summary>
        /// [스킬 변경] / [고급 스킬 변경] - 3슬롯을 다시 뽑는다(레벨 유지). 일반: 스킬 변경권 1장 우선, 없으면 15,000P.
        /// 고급: 고급 스킬 변경권 1장 필수 + 최소 1슬롯 A~S 확정. 재화가 모자라면 아무것도 바꾸지 않는다.
        /// </summary>
        public static bool TryReroll(Player player, IGrowthLedger ledger, bool premium, Func<int, int> pick, out string message)
        {
            if (player?.Template == null) { message = "스킬을 변경할 선수를 선택하십시오."; return false; }
            if (ledger == null) { message = "게임 데이터가 없습니다."; return false; }
            string cost;
            if (premium)
            {
                if (ledger.PremiumSkillChangeTicket < 1) { message = "고급 스킬 변경권이 없습니다 - 상점 · 교환소(성장 코인 600 / 트로피 1)에서 교환하십시오."; return false; }
                ledger.PremiumSkillChangeTicket -= 1;
                cost = "고급 스킬 변경권 -1";
            }
            else if (ledger.SkillChangeTicket > 0)
            {
                ledger.SkillChangeTicket -= 1;
                cost = "스킬 변경권 -1";
            }
            else if (ledger.Points >= RerollPointCost)
            {
                ledger.Points -= RerollPointCost;
                cost = $"{RerollPointCost:N0}P";
            }
            else
            {
                message = $"스킬 변경권 또는 {RerollPointCost:N0}P가 필요합니다 (보유 {ledger.Points:N0}P).";
                return false;
            }
            EnsureSlots(player);
            var before = string.Join(" / ", player.SkillSlots.Select(SlotLabel));
            player.SkillSlots = RollSlots(player, pick, premium, player.SkillSlots);
            message = $"{(premium ? "고급 스킬 변경" : "스킬 변경")} 완료 ({cost}) - {string.Join(" / ", player.SkillSlots.Select(SlotLabel))}";
            UnityEngine.Debug.Log($"[PlayerSkillRules] {player.Template.PlayerName}: {before} → {string.Join(" / ", player.SkillSlots.Select(SlotLabel))}");
            return true;
        }

        /// <summary>[스킬 레벨업] Lv.n → n+1: 특훈권 n장 우선(부족하면 포인트 10,000 × n). Lv.6이 최대.</summary>
        public static bool TryLevelUp(Player player, int slotIndex, IGrowthLedger ledger, out string message)
        {
            if (player?.Template == null) { message = "스킬을 강화할 선수를 선택하십시오."; return false; }
            if (ledger == null) { message = "게임 데이터가 없습니다."; return false; }
            EnsureSlots(player);
            if (slotIndex < 0 || slotIndex >= player.SkillSlots.Count) { message = "레벨업할 스킬 슬롯을 선택하십시오."; return false; }
            var slot = player.SkillSlots[slotIndex];
            if (slot.Level >= MaxLevel) { message = $"{Name(slot)}은(는) 이미 최대 레벨(Lv.{MaxLevel})입니다."; return false; }
            int tickets = LevelUpTicketCost(slot.Level), points = LevelUpPointCost(slot.Level);
            string cost;
            if (ledger.TrainingTicket >= tickets)
            {
                ledger.TrainingTicket -= tickets;
                cost = $"특훈권 -{tickets}";
            }
            else if (ledger.Points >= points)
            {
                ledger.Points -= points;
                cost = $"{points:N0}P";
            }
            else
            {
                message = $"특훈권 {tickets}장 또는 {points:N0}P가 필요합니다 (보유 특훈권 {ledger.TrainingTicket} · {ledger.Points:N0}P).";
                return false;
            }
            slot.Level = ClampLevel(slot.Level + 1);
            message = $"{Name(slot)} Lv.{slot.Level - 1} → Lv.{slot.Level} ({cost}) - {EffectText(slot)}";
            return true;
        }

        public static string LevelUpCostLabel(PlayerSkillSlot slot) =>
            slot == null || slot.Level >= MaxLevel ? "최대 레벨" : $"특훈권 {LevelUpTicketCost(slot.Level)} / {LevelUpPointCost(slot.Level):N0}P";
    }
}
