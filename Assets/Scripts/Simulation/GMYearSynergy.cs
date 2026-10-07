using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;

namespace KBOManager.Simulation
{
    /// <summary>
    /// [TASK-GM-07] 단장 모드 전용 연도 시너지(DCL-162). 카드 수집형 세트덱(ENABLE_SET_DECK_200P · 등급 스코어 · OVR 배율)은 계속 꺼 두고,
    /// "같은 시즌 · 같은 구단 출신 동료"가 함께 뛰면 팀워크가 오르는 단장 시뮬레이션 규칙으로 새로 만들었다. 선수 OVR(BaseOverall)은 건드리지 않는다.
    ///   - 그룹: 출전 가능 로스터에서 (카드 기준 시즌 연도, 발급 구단)이 같은 선수 묶음.
    ///     현역 시즌(2026) 데이터는 기준선이라 제외한다 - 10구단 모두 2026 프랜차이즈 선수단이라 전부에 같은 가산이 붙으면 리그 공격력만 부풀었다(GM-07 1차 실측).
    ///     그래서 올타임 드림 모드 · 은퇴 선수 영입처럼 "다른 시대의 같은 팀 동료"를 모을 때만 시너지가 생긴다.
    ///   - 그룹별 가산: 3~4명 +1 · 5~7명 +2 · 8~11명 +3 · 12명 이상 +4 / 구단 합계 상한 +8
    ///   - 반영: GMTeamState.TeamworkBuff → TeamChemistryEngine 팀워크 → 실효 전력 계수(1.0 + (팀워크-70)×0.005) → 경기 엔진 전 스탯 가산
    /// 올타임 드림 모드에서 같은 왕조 시즌 선수를 모으거나, 현역 모드에서 같은 시대 레전드(과거 시즌 카드)를 함께 쓸수록 보너스가 커진다.
    /// </summary>
    public static class GMYearSynergy
    {
        public const int MinGroup = 3;
        public const int MaxTeamBonus = 8;

        public sealed class Group
        {
            public int Year;
            public Team Team;
            public int Count;
            public int Bonus;
            public string Label => $"{Year} {KBOManager.UI.CompyaUiKit.ShortName(Team)} {Count}명 +{Bonus}";
        }

        public static int GroupBonus(int count) => count >= 12 ? 4 : count >= 8 ? 3 : count >= 5 ? 2 : count >= MinGroup ? 1 : 0;

        /// <summary>시너지에서 제외하는 기준선 시즌(현역 데이터 연도).</summary>
        public const int BaselineYear = KBOManager.Core.GMFeatureFlags.DEFAULT_START_YEAR;

        public static List<Group> Groups(IEnumerable<Player> roster) =>
            (roster ?? Enumerable.Empty<Player>()).Where(p => p?.Template != null && p.Template.SeasonYear > 0 && p.Template.SeasonYear != BaselineYear && p.Template.Team != Team.None)
                .GroupBy(p => (p.Template.SeasonYear, p.Template.Team))
                .Select(g => new Group { Year = g.Key.SeasonYear, Team = g.Key.Team, Count = g.Count(), Bonus = GroupBonus(g.Count()) })
                .Where(g => g.Bonus > 0)
                .OrderByDescending(g => g.Bonus).ThenByDescending(g => g.Count).ThenBy(g => g.Year)
                .ToList();

        /// <summary>구단 연도 시너지 팀워크 가산(0 ~ +8).</summary>
        public static int TeamworkBonus(IEnumerable<Player> roster) => Math.Min(MaxTeamBonus, Groups(roster).Sum(g => g.Bonus));

        /// <summary>화면 요약 - "연도 시너지 +5 (2002 삼성 9명 +3 · 2009 KIA 5명 +2)".</summary>
        public static string Summary(IEnumerable<Player> roster)
        {
            var groups = Groups(roster);
            if (groups.Count == 0) return "연도 시너지 없음 (과거 같은 시즌 · 같은 구단 출신 3명부터)";
            return $"연도 시너지 +{Math.Min(MaxTeamBonus, groups.Sum(g => g.Bonus))} ({string.Join(" · ", groups.Take(3).Select(g => g.Label))})";
        }
    }
}
