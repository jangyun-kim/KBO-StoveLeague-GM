using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Simulation
{
    public class TeamChemistryReport
    {
        public int TeamworkScore;                       // 0 ~ 100 (기본 75)
        public TeamMoraleState MoraleState;             // 침체 / 보통 / 고무
        public AllStarOverloadPenalty ActivePenalties;  // 발동 중인 슈퍼스타 과밀 부작용 비트플래그
        public float EffectivePowerMultiplier;          // 실효 전력 계수 (0.80f ~ 1.15f)
        public float ClutchHitModifier;                 // 7회 이후 접전 클러치 타율 보정 (-0.15f ~ +0.10f)
        public float ErrorRateMultiplier;               // 수비 실책 확률 배수 (0.8f ~ 2.0f)
        public float DoublePlayRiskMultiplier;          // 득점권 병살타/삼진 배수 (0.85f ~ 1.50f)
        public float PitcherEraPenalty;                 // 수비 불균형으로 인한 투수 ERA 가산치 (0.00f ~ +1.25f)
        public float UpsetVulnerabilityChance;          // 약팀 상대 방심 업셋 확률 (0.00f ~ 0.25f)
        public List<string> DiagnosticMessages = new List<string>(); // 단장 보고서용 경고/해법 메시지

        public bool Has(AllStarOverloadPenalty penalty) => (ActivePenalties & penalty) == penalty && penalty != AllStarOverloadPenalty.None;
    }

    /// <summary>
    /// [TASK-GM-01] 마스터 인계 문서 제4절 "성적이 좋은 선수들만 모였을 때의 6대 부작용과 해결책"을 계산하는 엔진(지시서 2.4절 사양 그대로).
    /// 해법: 주장 임명 · 더그아웃 리더 · 헌신형 살림꾼 배치, 보직 양보 인센티브, 전술 기조 '규율 우선', 치어리더 리더십 버프.
    /// [TASK-GM-02] EvaluateRoster는 상태를 바꾸지 않는 순수 계산 함수다(진단 화면을 몇 번 열어도 만족도 불변).
    /// 실제 만족도 · 컨디션 변동은 경기(일자) 진행 시 ApplyMatchChemistryTick()이 한 번씩 적용한다.
    /// </summary>
    public static class TeamChemistryEngine
    {
        public static TeamChemistryReport EvaluateRoster(
            IReadOnlyList<Player> activeRoster,
            int teamPayrollCap,
            int cheerleaderLeadershipBuff = 0,
            bool isTacticalDisciplineMode = false)
        {
            var report = new TeamChemistryReport
            {
                TeamworkScore = 75,
                MoraleState = TeamMoraleState.Normal,
                ActivePenalties = AllStarOverloadPenalty.None,
                EffectivePowerMultiplier = 1.0f,
                ClutchHitModifier = 0.0f,
                ErrorRateMultiplier = 1.0f,
                DoublePlayRiskMultiplier = 1.0f,
                PitcherEraPenalty = 0.0f,
                UpsetVulnerabilityChance = 0.0f
            };

            if (activeRoster == null || activeRoster.Count == 0)
                return report;

            int alphaDogCount = activeRoster.Count(p => p.EgoLevel >= 5 || p.RoleArchetype == LockerRoomRole.AlphaDog);
            int ambitiousCount = activeRoster.Count(p => p.RoleArchetype == LockerRoomRole.Ambitious);
            int leaderCount = activeRoster.Count(p => p.RoleArchetype == LockerRoomRole.DugoutLeader || p.IsCaptain);
            int unsungHeroCount = activeRoster.Count(p => p.RoleArchetype == LockerRoomRole.UnsungHero);

            // [경우의 수 ①] 왕좌의 게임 (알파독 파벌 분열)
            // 조건: Ego 5 알파독 3명 이상 & 이를 중재할 더그아웃 리더(또는 주장) 부재
            if (alphaDogCount >= 3 && leaderCount == 0)
            {
                report.ActivePenalties |= AllStarOverloadPenalty.AlphaDogFactionSplit;
                report.TeamworkScore -= (alphaDogCount - 2) * 12;
                report.MoraleState = TeamMoraleState.Slump;
                report.ClutchHitModifier -= 0.15f;
                report.ErrorRateMultiplier *= 2.0f;
                report.DiagnosticMessages.Add(
                    "[파벌 분열 경고] 자존심(Ego 5) 슈퍼스타가 3명 이상이나 라커룸을 중재할 주장(리더)이 없습니다. 클러치 타율 -15%, 실책 확률 2배 페널티가 발생합니다.");
            }
            else if (leaderCount > 0)
            {
                report.TeamworkScore += Math.Min(leaderCount * 8, 16);
            }

            // [경우의 수 ②] 타순·보직 자존심 충돌 (중심타선 3·4·5번 / 1선발 독점욕)
            // [TASK-GM-02] 순수 계산 - 밀려난 스타를 세기만 하고 만족도는 바꾸지 않는다(실제 변동은 ApplyMatchChemistryTick).
            int dissatisfiedStars = GetDissatisfiedStars(activeRoster).Count;

            if (dissatisfiedStars > 0)
            {
                report.ActivePenalties |= AllStarOverloadPenalty.LineupRoleConflict;
                report.TeamworkScore -= dissatisfiedStars * 7;
                report.DiagnosticMessages.Add(
                    $"[보직 자존심 충돌] 하위 타선·후순위 선발로 밀려난 스타 {dissatisfiedStars}명이 불만 태업(개인 만족도 급락·컨디션 저하) 상태입니다. 보직 양보 인센티브나 응원단 전담 버프가 필요합니다.");
            }

            // [경우의 수 ③] 개인 기록 탐욕 (Hero Ball — 팀배팅 거부)
            if (ambitiousCount >= 4 && unsungHeroCount < 2 && !isTacticalDisciplineMode)
            {
                report.ActivePenalties |= AllStarOverloadPenalty.HeroBallDoublePlay;
                report.DoublePlayRiskMultiplier = 1.35f;
                report.TeamworkScore -= 10;
                report.DiagnosticMessages.Add(
                    "[팀배팅 거부(Hero Ball)] 개인 타이틀에 집착하는 야망가 성향 타자가 과밀되어 득점권 병살타·삼진 확률이 +35% 증가했습니다. 헌신형 살림꾼을 배치하거나 전술 기조를 '규율 우선'으로 변경하세요.");
            }

            // [경우의 수 ④] 수비 기피 및 센터라인(포수·유격수·중견수) 수비 붕괴
            float centerLineDefenseAvg = GetCenterLineDefenseAverage(activeRoster);
            if (centerLineDefenseAvg < 68.0f)
            {
                report.ActivePenalties |= AllStarOverloadPenalty.DefenseImbalance;
                report.PitcherEraPenalty = 0.85f;
                report.ErrorRateMultiplier *= 1.35f;
                report.TeamworkScore -= 8;
                report.DiagnosticMessages.Add(
                    "[공수 불균형 경고] 공격형 거포 위주 편성으로 센터라인(포수·유격수·중견수) 수비력이 기준 미달입니다. 투수진 평균자책점(ERA)이 +0.85 상승하고 투타 불화 위험이 커집니다.");
            }

            // [경우의 수 ⑤] 샐러리캡 폭발 및 벤치·불펜 뎁스 붕괴
            int totalPayroll = activeRoster.Sum(p => p.Salary);
            var top12Salary = activeRoster.OrderByDescending(p => p.Salary).Take(12).Sum(p => p.Salary);
            float top12Share = totalPayroll > 0 ? (float)top12Salary / totalPayroll : 0f;
            if ((teamPayrollCap > 0 && totalPayroll > teamPayrollCap) || top12Share >= 0.85f)
            {
                report.ActivePenalties |= AllStarOverloadPenalty.PayrollDepthCollapse;
                report.TeamworkScore -= 8;
                report.DiagnosticMessages.Add(
                    "[페이롤 편중·뎁스 붕괴] 상위 12명에게 연봉의 85% 이상이 집중되어 벤치·추격조 불펜 뎁스가 취약합니다. 주전 부상 시 연패 위험이 급증합니다.");
            }

            // 살림꾼 및 치어리더 단장 리더십 보정 반영
            report.TeamworkScore += Math.Min(unsungHeroCount * 4, 12);
            report.TeamworkScore += cheerleaderLeadershipBuff;
            report.TeamworkScore = Mathf.Clamp(report.TeamworkScore, 20, 100);

            // [경우의 수 ⑥] 역(逆) 시너지: 스타 군단의 방심 vs 언더독의 반란
            double avgOvr = activeRoster.Average(p => p.BaseOverall);
            if (avgOvr >= 85.0 && report.TeamworkScore < 50)
            {
                report.ActivePenalties |= AllStarOverloadPenalty.UnderdogUpsetVulnerability;
                report.UpsetVulnerabilityChance = 0.20f;
                report.MoraleState = TeamMoraleState.Slump;
                report.DiagnosticMessages.Add(
                    "[스타 군단의 방심] 선수단 평균 기량은 최상위권이나 팀워크가 50 미만입니다. 약팀 상대 업셋 패배 확률이 +20% 증가합니다.");
            }
            else if (report.TeamworkScore >= 90)
            {
                report.MoraleState = TeamMoraleState.Boosted;
                report.ClutchHitModifier += 0.08f;
            }

            // 최종 실효 전력 계수 (0.80 ~ 1.15) 산출
            // 공식: 기본 1.0 + (팀워크 - 70) * 0.005
            float rawMultiplier = 1.0f + ((report.TeamworkScore - 70) * 0.005f);
            report.EffectivePowerMultiplier = Mathf.Clamp(rawMultiplier, 0.80f, 1.15f);

            return report;
        }

        /// <summary>② 중심타선(최대 4자리) · 1~2선발에서 밀려난 자존심 높은 스타(보직 양보 인센티브 수령자 제외). 상태 변경 없음.</summary>
        public static List<Player> GetDissatisfiedStars(IReadOnlyList<Player> activeRoster)
        {
            var result = new List<Player>();
            if (activeRoster == null) return result;
            var highEgoBatters = activeRoster.Where(p => !p.IsPitcher && p.EgoLevel >= 4).ToList();
            var highEgoPitchers = activeRoster.Where(p => p.IsPitcher && p.EgoLevel >= 5).ToList();
            if (highEgoBatters.Count > 4) result.AddRange(highEgoBatters.Skip(4).Where(p => !p.HasRoleConcessionBonus));
            if (highEgoPitchers.Count >= 3) result.AddRange(highEgoPitchers.Skip(2).Where(p => !p.HasRoleConcessionBonus));
            return result;
        }

        public const int TickMoralePenalty = 3;   // 불만 스타 경기당 만족도 하락
        public const int TickMoraleRecovery = 1;  // 그 외 선수 경기당 회복(기본 70까지)
        public const int MoraleFloorBatter = 20, MoraleFloorPitcher = 25;

        /// <summary>
        /// [TASK-GM-02] 경기(일자) 진행 시점에만 호출하는 실제 케미스트리 변동: 밀려난 스타는 만족도 -3(타자 하한 20 · 투수 하한 25)과
        /// 컨디션 한 단계 하락(만족도 40 미만), 나머지는 기본 만족도(70)까지 +1(분위기 '고무'면 +2) 회복한다.
        /// </summary>
        public static void ApplyMatchChemistryTick(IReadOnlyList<Player> activeRoster)
        {
            if (activeRoster == null || activeRoster.Count == 0) return;
            var report = EvaluateRoster(activeRoster, 0);
            var unhappy = new HashSet<Player>(GetDissatisfiedStars(activeRoster));
            int recovery = report.MoraleState == TeamMoraleState.Boosted ? TickMoraleRecovery + 1 : TickMoraleRecovery;
            foreach (var p in activeRoster)
            {
                if (unhappy.Contains(p))
                {
                    p.PersonalMorale = Math.Max(p.IsPitcher ? MoraleFloorPitcher : MoraleFloorBatter, p.PersonalMorale - TickMoralePenalty);
                    if (p.PersonalMorale < 40) p.ShiftCondition(false);
                }
                else if (p.PersonalMorale < Player.DefaultPersonalMorale)
                {
                    p.PersonalMorale = Math.Min(Player.DefaultPersonalMorale, p.PersonalMorale + recovery);
                }
            }
        }

        /// <summary>지시서 1항 명칭 - ApplyMatchChemistryTick과 같다.</summary>
        public static void ApplyChemistryTickEffects(IReadOnlyList<Player> activeRoster) => ApplyMatchChemistryTick(activeRoster);

        private static float GetCenterLineDefenseAverage(IReadOnlyList<Player> roster)
        {
            var catchers = roster.Where(p => p.Position == "C").ToList();
            var shortstops = roster.Where(p => p.Position == "SS").ToList();
            var centerFielders = roster.Where(p => p.Position == "CF").ToList();

            float cDef = catchers.Count > 0 ? catchers.Max(p => p.DefenseStat) : 55f;
            float ssDef = shortstops.Count > 0 ? shortstops.Max(p => p.DefenseStat) : 55f;
            float cfDef = centerFielders.Count > 0 ? centerFielders.Max(p => p.DefenseStat) : 55f;

            return (cDef + ssDef + cfDef) / 3.0f;
        }

        /// <summary>UI 표기용 분위기 이름.</summary>
        public static string MoraleLabel(TeamMoraleState state) =>
            state == TeamMoraleState.Slump ? "침체" : state == TeamMoraleState.Boosted ? "고무" : "보통";
    }
}
