using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Services;

namespace KBOManager.Simulation
{
    /// <summary>[TASK-GM-03] 경기 전 전력 비교(기획서 2.2.1) - 한 팀 분량.</summary>
    public class GMTeamPreview
    {
        public string Code;
        public Team Team;
        public string Name;
        public bool IsHome;
        public string RecordLabel;   // "12승 1무 8패"
        public int Rank;
        public string Recent5;       // "승 승 패 무 승"
        public Player Starter;
        public string StarterSeasonLabel;  // "3승 2패 · ERA 3.12 · WHIP 1.20 · 탈삼진 45"
        public string StarterRatingLabel;  // "구위 72 / 제구 65 / 체력 70"
        public string StarterConditionLabel;
        // 5대 전력(0~100)
        public int Batting, Power, Rotation, Bullpen, DefenseTeamwork;
        public int Teamwork;
        public float PowerMultiplier;
        public readonly List<string> Badges = new List<string>();
        public readonly List<Cheerleader> CheerEntry = new List<Cheerleader>();
        public int Leadership, BattingBuff, MoundBuff;

        public int Metric(int index) => index switch { 0 => Batting, 1 => Power, 2 => Rotation, 3 => Bullpen, _ => DefenseTeamwork };
    }

    /// <summary>[TASK-GM-03] 다음 내 구단 경기의 전력 비교 데이터. Build()는 상태를 바꾸지 않는다.</summary>
    public class GMMatchPreview
    {
        public static readonly string[] MetricLabels = { "타격", "장타", "선발", "불펜", "수비·팀워크" };

        public int GameIndex;
        public string GameLabel;
        public string DateLabel;
        public string Stadium;
        public GMTeamPreview Away, Home;

        public static GMMatchPreview Build(GMLiveSeasonSimulator sim)
        {
            if (sim == null || sim.IsSeasonComplete) return null;
            var league = sim.League;
            int day = sim.GamesPlayed;
            var match = sim.MatchesOn(day).FirstOrDefault(m => m.home == league.SelectedTeamCode || m.away == league.SelectedTeamCode);
            if (match.home == null) return null;
            var standings = sim.Standings();
            var preview = new GMMatchPreview
            {
                GameIndex = day,
                GameLabel = $"Game #{day + 1:000} / {GMLiveSeasonSimulator.SeasonGames}",
                DateLabel = GMLiveSeasonSimulator.DateLabel(day),
                Stadium = KBOManager.UI.CompyaUiKit.Stadium(NameAliasTable.ToTeam(match.home)),
            };
            preview.Home = TeamPreview(league, league.Teams[match.home], true, day, standings);
            preview.Away = TeamPreview(league, league.Teams[match.away], false, day, standings);
            return preview;
        }

        private static GMTeamPreview TeamPreview(GMLeagueState league, GMTeamState team, bool isHome, int day, List<GMTeamRecord> standings)
        {
            var record = league.RecordOf(team.TeamCode);
            var available = team.AvailableRoster;
            var p = new GMTeamPreview
            {
                Code = team.TeamCode,
                Team = team.Team,
                Name = NameAliasTable.DisplayTeamName(team.TeamCode),
                IsHome = isHome,
                RecordLabel = $"{record.W}승 {record.D}무 {record.L}패",
                Rank = standings.FindIndex(r => r.TeamCode == team.TeamCode) + 1,
                Recent5 = record.RecentLabel(5),
            };

            p.Starter = StartingRotation.PickFor(available, day, team.Lineup);
            if (p.Starter != null)
            {
                league.Stats.TryGetValue(p.Starter.InstanceId, out var st);
                p.StarterSeasonLabel = st == null || st.PG == 0
                    ? "시즌 첫 등판"
                    : $"{st.W}승 {st.L}패 · ERA {st.ERA:0.00} · WHIP {st.WHIP:0.00} · 탈삼진 {st.PSO}";
                var ps = p.Starter.Template.PitcherStats;
                p.StarterRatingLabel = $"구위 {ps.Stuff} / 제구 {ps.Control} / 체력 {ps.Stamina}";
                p.StarterConditionLabel = ConditionLabel(p.Starter.CurrentCondition);
            }
            else
            {
                p.StarterSeasonLabel = "-";
                p.StarterRatingLabel = "-";
                p.StarterConditionLabel = "-";
            }

            var starters = LineupAssignment.AssignStarters(available, team.Lineup).Where(s => s.Player != null).Select(s => s.Player).ToList();
            var rotation = StartingRotation.RotationOf(available, team.Lineup);
            var bullpen = available.Where(x => x.IsPitcher && !rotation.Contains(x)).ToList();
            var report = TeamChemistryEngine.EvaluateRoster(available, team.PayrollCap, team.CheerLeadershipBuff);
            p.Batting = Avg(starters, x => (x.Template.BatterStats.Contact + x.Template.BatterStats.Discipline) / 2.0);
            p.Power = Avg(starters, x => x.Template.BatterStats.Power);
            p.Rotation = Avg(rotation, x => x.GetEffectiveOverall());
            p.Bullpen = Avg(bullpen, x => x.GetEffectiveOverall());
            p.Teamwork = report.TeamworkScore;
            p.PowerMultiplier = report.EffectivePowerMultiplier;
            p.DefenseTeamwork = (int)Math.Round((Avg(starters, x => x.Template.BatterStats.Defense) + report.TeamworkScore) / 2.0);
            p.Badges.AddRange(Badges(report));

            p.CheerEntry.AddRange(team.CheerEntry);
            p.Leadership = team.CheerLeadershipBuff;
            if (p.CheerEntry.Count >= GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN)
            {
                var fx = CheerSquad.BuildEffects(p.CheerEntry, team.Team, isHome, Math.Max(0, -record.Streak), 0);
                p.BattingBuff = fx.BatterContactDiscipline + fx.HomeAllStatsBonus;
                p.MoundBuff = fx.PitcherControlStuff + fx.HomeAllStatsBonus;
            }
            return p;
        }

        public static IEnumerable<string> Badges(TeamChemistryReport report)
        {
            var list = new List<string>();
            if (report.Has(AllStarOverloadPenalty.AlphaDogFactionSplit)) list.Add("파벌 분열");
            if (report.Has(AllStarOverloadPenalty.LineupRoleConflict)) list.Add("보직 충돌");
            if (report.Has(AllStarOverloadPenalty.HeroBallDoublePlay)) list.Add("Hero Ball");
            if (report.Has(AllStarOverloadPenalty.DefenseImbalance)) list.Add("수비 붕괴");
            if (report.Has(AllStarOverloadPenalty.PayrollDepthCollapse)) list.Add("뎁스 붕괴");
            if (report.Has(AllStarOverloadPenalty.UnderdogUpsetVulnerability)) list.Add("방심 경계");
            if (list.Count == 0) list.Add(report.MoraleState == TeamMoraleState.Boosted ? "시너지: 분위기 고무" : "부작용 없음");
            return list;
        }

        public static string ConditionLabel(PlayerCondition c) => c switch
        {
            PlayerCondition.Poor => "열악",
            PlayerCondition.BelowAverage => "저조",
            PlayerCondition.Good => "호조",
            PlayerCondition.Excellent => "최상",
            _ => "보통",
        };

        private static int Avg(IEnumerable<Player> players, Func<Player, double> f)
        {
            var list = players.ToList();
            return list.Count == 0 ? 0 : (int)Math.Round(Math.Max(0, Math.Min(100, list.Average(f))));
        }
    }
}
