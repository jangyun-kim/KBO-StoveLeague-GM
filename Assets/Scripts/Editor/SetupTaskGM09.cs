using KBOManager.Engine;
using KBOManager.Services;
using KBOManager.Simulation;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-GM-09] 점검(idempotent, SetupMasterBinding 체인 마지막) - 씬 변경 없음(로직 태스크).
    /// 투타 밸런스 노브 · 실효 전력 하한 · FA 순환 상수 · 2026~2031 글로벌 대회 일정을 로그로 남긴다.
    /// </summary>
    public static class SetupTaskGM09
    {
        [MenuItem("KBO Manager/Setup/Apply TASK-GM-09 (Balance + FA Cycle + Global Tournaments)")]
        public static void ApplyAll()
        {
            var schedule = new System.Text.StringBuilder();
            for (int y = 2026; y <= 2031; y++)
                foreach (var t in GMGlobalTournamentManager.ScheduleFor(y)) schedule.Append($"{t.Name}({t.Month}월) ");
            Debug.Log($"[SetupTaskGM09] TASK-GM-09 점검 - 장타 배수 {GMBattingBalance.ExtraBaseBoost:0.00} · 투수 우위 하한 {GMBattingBalance.PitcherSkillFloor:0.00} · 타자 상한 {GMBattingBalance.SkillCap:0.00} · " +
                      $"체급 보정 {GMBattingBalance.ClassBonusScale:0.0} · 실효 전력 {TeamChemistryEngine.MinEffectivePower:0.00}~{TeamChemistryEngine.MaxEffectivePower:0.00} · " +
                      $"AI FA 28명 보충(구단당 최대 {GMFreeAgencyCycle.MaxAiSigningsPerTeam}명) · 대회 일정 {schedule}");
        }
    }
}
