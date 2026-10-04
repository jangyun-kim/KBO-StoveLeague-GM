using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-180] 치어리더 6인 역할 편성 슬롯 - docs/기획 고도화 자료.pdf 12절 "치어리더 6명 편성 구조" 표 그대로.
    /// 정수값은 세이브(GameSaveData.CheerSquad 리스트 인덱스)와 1:1이라 순서를 바꾸지 않는다.
    /// </summary>
    public enum CheerRole
    {
        Leader = 0,     // 1. 응원단장 - 전체 세트덱 보강
        Batting = 1,    // 2. 타격 응원 - 타자 컨디션·멘탈
        Pitching = 2,   // 3. 투수 응원 - 투수 컨디션·멘탈
        MoodMaker = 3,  // 4. 분위기 메이커 - 연패·실점 대응
        Home = 4,       // 5. 홈 응원 - 홈경기·팬 압박
        Clutch = 5,     // 6. 위기 응원 - 후반·접전 상황
    }

    /// <summary>
    /// [TASK-KBO-180] 한 경기에 적용될 치어리더 6인 편성 효과(유저 구단 전용). CheerSquad.BuildEffects()가 만들고
    /// Engine.TeamPowerModifiers.Cheer로 MatchEngine에 전달된다. 엔진은 아래 값을 상황(홈/연패/열세/후반 접전)에 맞춰 적용하고
    /// 직접 능력치 보정은 스탯별 합계를 DirectStatCap(+2)으로 자른다(PDF 11.4/14.1절 "직접 능력치 보정 최대 +2~3").
    /// 구단 OVR(Player.CalculateOVR/CalculateTeamOVR)에는 절대 들어가지 않는 경기 조건부 값이다(PDF 2절 - 치어리더 OVR 비합산).
    /// </summary>
    public class CheerSquadEffects
    {
        public const int DirectStatCap = 2;  // 역할 담당 스탯(타자 정확·선구 / 투수 제구·구위) 합계 상한 - PDF 11.4/14.1절 "직접 보정 최대 +2~3"
        public const int GeneralStatCap = 1; // 그 외 전 스탯 합계 상한(응원단장 적용률 보강분 포함) - 6인 최상위 편성 승률 기여를 PDF 목표 2~5%로 제한

        // 1. 응원단장 - 세트덱 효과 적용률(%)과, 그 적용률로 늘어난 "모든 능력치" 가산(정수 반올림, 세트덱 보강분이라 캡 별도)
        public int SetDeckAmplifyPercent;
        public int SetDeckAmplifyBonus;
        // 2. 타격 응원 - 타자 정확·선구(컨디션이 먼저 영향을 주는 스탯, PDF 6.2절)
        public int BatterContactDiscipline;
        // 3. 투수 응원 - 투수 제구·구위
        public int PitcherControlStuff;
        // 4. 분위기 메이커 - 경기 전 연패(2연패 이상) 시 주요 스탯, 경기 중 TrailingThreshold점 이상 열세 시 타자 정확·선구
        public int LosingStreakBonus;
        public int TrailingBatterBonus;
        public int TrailingThreshold = 3;
        // 5. 홈 응원 - 홈경기 전 스탯(홈 응원 컨디션) + 상대 투수 제구 압박(팬 압박)
        public int HomeAllStatsBonus;
        public int OpponentControlPenalty;
        // 6. 위기 응원 - CloseLateFromInning회 이후 CloseLateMaxDiff점 차 이내 접전에서 타자 긍정 결과 가중치 배율 + 후반 득점권 클러치(카드 고유 배율)
        public float CloseLateMultiplier = 1f;
        public float LateRispMultiplier = 1f;
        public int CloseLateFromInning = 7;
        public int CloseLateMaxDiff = 2;

        /// <summary>UI/로그용 발동 요약(역할별 한 줄).</summary>
        public readonly List<string> Lines = new List<string>();

        public bool IsEmpty => SetDeckAmplifyBonus == 0 && BatterContactDiscipline == 0 && PitcherControlStuff == 0
            && LosingStreakBonus == 0 && TrailingBatterBonus == 0 && HomeAllStatsBonus == 0 && OpponentControlPenalty == 0
            && CloseLateMultiplier <= 1f && LateRispMultiplier <= 1f;

        public static int Cap(int value) => value > DirectStatCap ? DirectStatCap : value;
        public static int CapGeneral(int value) => value > GeneralStatCap ? GeneralStatCap : value;
    }

    /// <summary>[TASK-KBO-180] 치어리더 6인 편성 규칙(역할 명칭·효과 수치·중복 금지·구단 시너지).</summary>
    public static class CheerSquad
    {
        public const int SlotCount = 6;

        public static readonly CheerRole[] Roles =
            { CheerRole.Leader, CheerRole.Batting, CheerRole.Pitching, CheerRole.MoodMaker, CheerRole.Home, CheerRole.Clutch };

        public static string RoleName(CheerRole role)
        {
            switch (role)
            {
                case CheerRole.Leader: return "응원단장";
                case CheerRole.Batting: return "타격 응원";
                case CheerRole.Pitching: return "투수 응원";
                case CheerRole.MoodMaker: return "분위기 메이커";
                case CheerRole.Home: return "홈 응원";
                default: return "위기 응원";
            }
        }

        public static string RoleFocus(CheerRole role)
        {
            switch (role)
            {
                case CheerRole.Leader: return "전체 세트덱 보강";
                case CheerRole.Batting: return "타자 컨디션·멘탈";
                case CheerRole.Pitching: return "투수 컨디션·멘탈";
                case CheerRole.MoodMaker: return "연패·실점 대응";
                case CheerRole.Home: return "홈경기·팬 압박";
                default: return "후반·접전 상황";
            }
        }

        /// <summary>등급 단계(효과 강도): LIVE 1 / ICON 2 / LEGEND·시즌 한정 3. PDF 5.2절 "일반 / 고급 / 최상위" 3단 상한 구조.</summary>
        public static int Tier(CheerleaderGrade grade)
        {
            switch (grade)
            {
                case CheerleaderGrade.ICON: return 2;
                case CheerleaderGrade.LEGEND:
                case CheerleaderGrade.SEASON_LIMITED: return 3;
                default: return 1;
            }
        }

        // 단계별 수치표(인덱스 = Tier 1~3)
        // [TASK-KBO-187] 인덱스 4 · 5 = ★3 · ★5 각성 임계점 도약 티어(CheerGrowth.EffectiveTier).
        private static readonly int[] LeaderAmplifyPercent = { 0, 3, 6, 10, 13, 16 };   // PDF 5.2절: 일반 +3% / 고급 +6% / 최상위 +10%
        private static readonly int[] RoleStatBonus = { 0, 1, 1, 2, 2, 3 };            // 타격·투수 응원, 홈 응원 컨디션
        private static readonly int[] StreakBonus = { 0, 1, 1, 1, 2, 2 };
        private static readonly int[] TrailingBonus = { 0, 1, 1, 2, 2, 3 };
        private static readonly int[] HomePressure = { 0, 1, 1, 1, 2, 2 };
        private static readonly float[] CloseLate = { 1f, 1.03f, 1.05f, 1.08f, 1.1f, 1.12f };   // PDF 11.4절 상황 보정(최대 15% 이내)

        /// <summary>같은 사람 판별 키 - 연도/구단/티어가 달라도 이름이 같으면 같은 인물(중복 편성 금지 기준).</summary>
        public static string PersonKey(Cheerleader cheerleader) => cheerleader?.Name?.Trim() ?? string.Empty;

        public static bool IsEmpty(Cheerleader cheerleader) => cheerleader == null || string.IsNullOrEmpty(cheerleader.InstanceId);

        /// <summary>role 슬롯에 candidate를 둘 수 있는지. 다른 슬롯에 같은 인물이 있으면 false + 사유.</summary>
        public static bool CanAssign(IReadOnlyList<Cheerleader> slots, CheerRole role, Cheerleader candidate, out string reason)
        {
            reason = null;
            if (IsEmpty(candidate)) { reason = "배치할 치어리더가 없습니다."; return false; }
            string key = PersonKey(candidate);
            for (int i = 0; i < SlotCount && slots != null && i < slots.Count; i++)
            {
                if (i == (int)role || IsEmpty(slots[i])) continue;
                if (PersonKey(slots[i]) == key)
                {
                    reason = $"{candidate.Name}은(는) 이미 {i + 1}.{RoleName((CheerRole)i)} 슬롯에 편성돼 있습니다(동일 인물 중복 편성 금지).";
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 편성 6인으로 이번 경기 효과를 만든다(유저 구단 전용 - AI 구단은 null을 넘긴다). 각 슬롯은 치어리더 소속 구단이 세트덱 기준
        /// 구단과 같을 때만 100% 발동한다(CheerleaderSynergy, TASK-175 규칙 유지 - 구단이 다르면 그 슬롯은 미발동).
        /// </summary>
        public static CheerSquadEffects BuildEffects(IReadOnlyList<Cheerleader> slots, Team deckTeam, bool isHome, int losingStreak, int setDeckFlatBuff)
        {
            var fx = new CheerSquadEffects();
            if (slots == null) return fx;

            for (int i = 0; i < SlotCount && i < slots.Count; i++)
            {
                var c = slots[i];
                if (IsEmpty(c)) continue;
                var role = (CheerRole)i;
                if (!CheerleaderSynergy.IsActive(c, deckTeam))
                {
                    fx.Lines.Add($"{i + 1}.{RoleName(role)} {c.Name}: 구단 시너지 미발동({c.Team} ≠ 세트덱 {deckTeam})");
                    continue;
                }
                int t = CheerGrowth.EffectiveTier(c); // [TASK-KBO-187] 등급 + ★3/★5 각성 도약
                switch (role)
                {
                    case CheerRole.Leader:
                        fx.SetDeckAmplifyPercent = LeaderAmplifyPercent[t] + CheerGrowth.LeaderAmplifyBonusPercent(c);
                        fx.SetDeckAmplifyBonus = (int)System.Math.Round(setDeckFlatBuff * fx.SetDeckAmplifyPercent / 100.0, System.MidpointRounding.AwayFromZero);
                        fx.Lines.Add($"1.응원단장 {c.Name}: 세트덱 적용률 +{fx.SetDeckAmplifyPercent}% (모든 능력치 +{fx.SetDeckAmplifyBonus})");
                        break;
                    case CheerRole.Batting:
                        fx.BatterContactDiscipline = RoleStatBonus[t] + CheerGrowth.RoleStatBonus(c);
                        fx.Lines.Add($"2.타격 응원 {c.Name}: 타자 정확·선구 +{fx.BatterContactDiscipline}");
                        break;
                    case CheerRole.Pitching:
                        fx.PitcherControlStuff = RoleStatBonus[t] + CheerGrowth.RoleStatBonus(c);
                        fx.Lines.Add($"3.투수 응원 {c.Name}: 투수 제구·구위 +{fx.PitcherControlStuff}");
                        break;
                    case CheerRole.MoodMaker:
                        fx.LosingStreakBonus = losingStreak >= 2 ? StreakBonus[t] : 0;
                        fx.TrailingBatterBonus = TrailingBonus[t] + CheerGrowth.RoleStatBonus(c);
                        fx.Lines.Add($"4.분위기 메이커 {c.Name}: {(losingStreak >= 2 ? $"{losingStreak}연패 대응 전 스탯 +{fx.LosingStreakBonus}, " : "")}" +
                                     $"{fx.TrailingThreshold}점 이상 열세 시 타자 정확·선구 +{fx.TrailingBatterBonus}");
                        break;
                    case CheerRole.Home:
                        if (isHome)
                        {
                            fx.HomeAllStatsBonus = RoleStatBonus[t] + CheerGrowth.RoleStatBonus(c);
                            fx.OpponentControlPenalty = HomePressure[t];
                            fx.Lines.Add($"5.홈 응원 {c.Name}: 홈 컨디션 전 스탯 +{fx.HomeAllStatsBonus}, 상대 투수 제구 -{fx.OpponentControlPenalty}");
                        }
                        else fx.Lines.Add($"5.홈 응원 {c.Name}: 원정 경기 - 미발동");
                        break;
                    default:
                        fx.CloseLateMultiplier = System.Math.Min(1.15f, CloseLate[t] + CheerGrowth.CloseLateBonus(c));
                        float card = c.ClutchMultiplier;
                        fx.LateRispMultiplier = float.IsNaN(card) || float.IsInfinity(card) ? 1f : System.Math.Max(1f, System.Math.Min(card, 1.1f));
                        fx.Lines.Add($"6.위기 응원 {c.Name}: {fx.CloseLateFromInning}회 이후 {fx.CloseLateMaxDiff}점 차 접전 타격 x{fx.CloseLateMultiplier:F2}" +
                                     (fx.LateRispMultiplier > 1f ? $", 후반 득점권 x{fx.LateRispMultiplier:F2}" : ""));
                        break;
                }
            }
            return fx;
        }

        /// <summary>슬롯 카드에 표시할 역할별 수치 요약(시너지/홈 조건 없이 "발동 시" 수치).</summary>
        public static string DescribeRoleEffect(CheerRole role, Cheerleader c)
        {
            if (IsEmpty(c)) return RoleFocus(role);
            int t = CheerGrowth.EffectiveTier(c);
            int stat = RoleStatBonus[t] + CheerGrowth.RoleStatBonus(c);
            string growth = c.ReinforceLevel > 0 || CheerGrowth.Stars(c) > 1 ? $" ({CheerGrowth.GrowthLabel(c)})" : "";
            switch (role)
            {
                case CheerRole.Leader: return $"세트덱 적용률 +{LeaderAmplifyPercent[t] + CheerGrowth.LeaderAmplifyBonusPercent(c)}%{growth}";
                case CheerRole.Batting: return $"타자 정확·선구 +{stat}{growth}";
                case CheerRole.Pitching: return $"투수 제구·구위 +{stat}{growth}";
                case CheerRole.MoodMaker: return $"연패 전 스탯 +{StreakBonus[t]} / 열세 타격 +{TrailingBonus[t] + CheerGrowth.RoleStatBonus(c)} / 팬심 방어 +{CheerGrowth.SentimentDefenseBonus(c)}{growth}";
                case CheerRole.Home: return $"홈 전 스탯 +{stat} / 상대 제구 -{HomePressure[t]} / 관중 수익 +{CheerGrowth.HomeRevenueBonusPercent(c)}%p{growth}";
                default: return $"7회~ 접전 타격 x{System.Math.Min(1.15f, CloseLate[t] + CheerGrowth.CloseLateBonus(c)):F2}{growth}";
            }
        }

        public static IEnumerable<Cheerleader> Filled(IReadOnlyList<Cheerleader> slots) => (slots ?? new List<Cheerleader>()).Where(c => !IsEmpty(c));
    }
}
