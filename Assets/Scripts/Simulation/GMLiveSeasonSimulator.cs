using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Models;
using KBOManager.Services;

namespace KBOManager.Simulation
{
    /// <summary>
    /// [TASK-GM-02] 단장 모드 정규시즌 144경기 진행기(기획서 2.1). 순수 C#(MonoBehaviour 아님) - 대시보드가 타이머 틱으로 StepGameDay()를 부르고,
    /// 테스트는 RunUntilStop()으로 즉시 돌린다.
    ///   - 일정: 10구단 원형 대진(하루 5경기 × 144일, 상대별 16경기 · 홈/원정 교대)
    ///   - 경기: MatchEngine + 구단별 TeamChemistryReport(6대 역학) + 치어리더 엔트리(4~6인) 효과 + 단장 라인업 지정
    ///   - 집계: 순위(승/무/패/승률/게임차) · 선수 누적 기록(타자 · 투수 · WAR · 연속 안타) · 최신 소식
    ///   - 인터럽트: 대기록(내 구단 또는 리그 최상급 기록) · 내 구단 주전 부상 시 일시정지 → ResolveInterrupt()
    ///   - 부상: 경미(담/타박상 5~7일) · 중등(햄스트링/내복사근 21~30일) · 중상(인대/골절 60~120일), 경기 일수마다 1일 차감 → 0이면 복귀 소식
    /// </summary>
    public class GMLiveSeasonSimulator
    {
        public const int SeasonGames = 144;
        public const int HalfGames = 72;
        public const int TeamCount = 10;
        public static readonly DateTime OpeningDay = new DateTime(2026, 3, 28);

        // 부상(팀당 경기당 확률) - 시즌 팀당 약 14~15건
        public const double InjuryChancePerTeamGame = 0.10;
        public const int MinorMin = 5, MinorMax = 7, ModerateMin = 21, ModerateMax = 30, SevereMin = 60, SevereMax = 120;
        private static readonly string[] MinorParts = { "허리 담 증세", "손목 타박상", "옆구리 담 증세", "발목 타박상", "어깨 뭉침" };
        private static readonly string[] ModerateParts = { "햄스트링 부상", "내복사근 부상", "허벅지 근육 손상", "팔꿈치 염좌" };
        private static readonly string[] SevereParts = { "무릎 인대 손상", "손등 골절", "팔꿈치 인대 손상", "발목 골절" };

        private readonly GMLeagueState league;
        private readonly SkillDB skillDB;
        private readonly EngineConfig config;
        private readonly Random random;
        private readonly List<(string home, string away)>[] schedule;
        private readonly Queue<GMSimInterrupt> interrupts = new Queue<GMSimInterrupt>();
        private readonly Dictionary<string, int> weeklyHits = new Dictionary<string, int>();

        public GMLeagueState League => league;
        public GMRunMode? ActiveMode { get; private set; }
        public int TargetGames { get; private set; }
        public int GamesPlayed => league.GamesPlayed;
        public bool IsSeasonComplete => league.GamesPlayed >= SeasonGames;
        public bool IsFirstHalfDone => league.GamesPlayed >= HalfGames;
        public GMSimInterrupt PendingInterrupt => interrupts.Count > 0 ? interrupts.Peek() : null;
        public bool IsRunning => ActiveMode.HasValue && league.GamesPlayed < TargetGames && PendingInterrupt == null;
        /// <summary>전반기 버튼 라벨 - 72경기를 마치면 자동으로 [후반기 진행]으로 바뀐다.</summary>
        public string HalfButtonLabel => IsFirstHalfDone ? "후반기 진행" : "전반기 진행";
        /// <summary>마지막으로 진행한 날의 내 구단 경기 결과 한 줄.</summary>
        public string LastUserGameLine { get; private set; }

        public event Action OnDayCompleted;

        public GMLiveSeasonSimulator(GMLeagueState league, SkillDB skillDB = null, EngineConfig config = null)
        {
            this.league = league ?? throw new ArgumentNullException(nameof(league));
            this.skillDB = skillDB;
            this.config = config;
            random = new Random(league.Seed + league.SeasonYear * 7 + league.GamesPlayed * 131);
            schedule = BuildSchedule(NameAliasTable.CanonicalTeamCodes);
            foreach (var code in NameAliasTable.CanonicalTeamCodes) league.RecordOf(code);
        }

        // ================================================================== 일정

        /// <summary>원형(circle) 대진 9라운드 × 16회 = 144일, 하루 5경기. 홀수 사이클은 홈/원정을 뒤집는다.</summary>
        public static List<(string home, string away)>[] BuildSchedule(IReadOnlyList<string> codes)
        {
            int n = codes.Count;
            var rounds = new List<List<(string, string)>>();
            var rotating = codes.Skip(1).ToList();
            for (int r = 0; r < n - 1; r++)
            {
                var day = new List<(string, string)>();
                var circle = new List<string> { codes[0] };
                circle.AddRange(rotating);
                for (int i = 0; i < n / 2; i++)
                {
                    string a = circle[i], b = circle[n - 1 - i];
                    day.Add(r % 2 == 0 ? (a, b) : (b, a));
                }
                rounds.Add(day);
                rotating.Insert(0, rotating[rotating.Count - 1]);
                rotating.RemoveAt(rotating.Count - 1);
            }
            var result = new List<(string, string)>[SeasonGames];
            for (int d = 0; d < SeasonGames; d++)
            {
                int cycle = d / rounds.Count;
                result[d] = rounds[d % rounds.Count].Select(m => cycle % 2 == 0 ? m : (m.Item2, m.Item1)).ToList();
            }
            return result;
        }

        public IReadOnlyList<(string home, string away)> MatchesOn(int dayIndex) => schedule[Math.Max(0, Math.Min(SeasonGames - 1, dayIndex))];

        /// <summary>경기 일자(월요일 휴식 반영: 6경기마다 하루 휴식).</summary>
        public static DateTime DateOf(int dayIndex) => OpeningDay.AddDays(dayIndex + dayIndex / 6);
        public static string DateLabel(int dayIndex) => DateOf(dayIndex).ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);

        // ================================================================== 진행 제어

        /// <summary>
        /// 진행 방식 시작. SingleGame = 다음 1경기, FirstHalf/SecondHalf = 전반기(72) 또는 후반기(144)까지 - 전반기를 마친 뒤 FirstHalf를 누르면
        /// 자동으로 후반기로 처리된다, FullSeason = 144까지. 이미 시즌이 끝났으면 false.
        /// </summary>
        public bool StartRun(GMRunMode mode)
        {
            if (IsSeasonComplete) return false;
            if (mode == GMRunMode.FirstHalf && IsFirstHalfDone) mode = GMRunMode.SecondHalf;
            if (mode == GMRunMode.SecondHalf && !IsFirstHalfDone) mode = GMRunMode.FirstHalf;
            TargetGames = mode switch
            {
                GMRunMode.SingleGame => Math.Min(SeasonGames, league.GamesPlayed + 1),
                GMRunMode.FirstHalf => HalfGames,
                _ => SeasonGames,
            };
            ActiveMode = mode;
            if (league.Phase == GMSeasonPhase.StoveLeague) league.Phase = GMSeasonPhase.RegularSeason;
            if (league.GamesPlayed == 0 && league.News.All(n => n.Kind != GMNewsKind.Season || n.GameIndex != 0))
                AddNews(0, GMNewsKind.Season, $"{league.SeasonYear} KBO 리그 개막", $"{league.SeasonYear} 시즌 정규리그 144경기 대장정이 시작됩니다.", false, false);
            return true;
        }

        public void Stop() => ActiveMode = null;

        /// <summary>인터럽트 처리 - 대기록은 Continue, 부상은 AutoCallUp(벤치 자동 대체) 또는 ManualLineup(replacement를 그 자리에 고정).</summary>
        public void ResolveInterrupt(GMInterruptChoice choice, Player replacement = null)
        {
            if (interrupts.Count == 0) return;
            var current = interrupts.Dequeue();
            if (current.Kind == GMInterruptKind.Injury && choice == GMInterruptChoice.ManualLineup && replacement != null && !current.Player.IsPitcher)
            {
                var team = league.Teams[current.TeamCode];
                team.Lineup.Starters.RemoveAll(pin => pin.Position == current.Position);
                team.Lineup.Starters.Add(new LineupAssignment.StarterPin { InstanceId = replacement.InstanceId, Position = current.Position });
            }
        }

        /// <summary>테스트/빠른 진행 - 목표까지 즉시 진행. autoResolve면 인터럽트를 자동 처리(부상 = 자동 콜업)한다. 진행한 일수를 반환.</summary>
        public int RunUntilStop(bool autoResolve = true, int maxDays = SeasonGames + 5)
        {
            int days = 0;
            while (days < maxDays && ActiveMode.HasValue && league.GamesPlayed < TargetGames)
            {
                if (PendingInterrupt != null)
                {
                    if (!autoResolve) break;
                    ResolveInterrupt(PendingInterrupt.Kind == GMInterruptKind.Injury ? GMInterruptChoice.AutoCallUp : GMInterruptChoice.Continue);
                    continue;
                }
                StepGameDay();
                days++;
            }
            while (autoResolve && PendingInterrupt != null) ResolveInterrupt(GMInterruptChoice.Continue);
            return days;
        }

        // ================================================================== 하루 진행

        /// <summary>하루(5경기)를 진행한다. 진행했으면 true. 목표 경기 수에 도달하면 ActiveMode가 꺼진다.</summary>
        public bool StepGameDay()
        {
            if (IsSeasonComplete || PendingInterrupt != null) return false;
            int day = league.GamesPlayed;

            TickInjuries(day);
            foreach (var team in league.Teams.Values) RecoverStamina(team.Roster);

            foreach (var (home, away) in schedule[day]) PlayGame(day, league.Teams[home], league.Teams[away]);

            foreach (var team in league.Teams.Values) TeamChemistryEngine.ApplyMatchChemistryTick(team.AvailableRoster);
            league.GamesPlayed++;
            PeriodicNews(day);

            if (IsSeasonComplete)
            {
                var top = Standings().First();
                AddNews(day, GMNewsKind.Season, "정규시즌 종료", $"{NameAliasTable.DisplayTeamName(top.TeamCode)} 정규시즌 1위 확정({top.W}승 {top.D}무 {top.L}패).", top.TeamCode == league.SelectedTeamCode, true);
                league.Phase = GMSeasonPhase.PostSeason;
            }
            else if (league.GamesPlayed == HalfGames)
            {
                AddNews(day, GMNewsKind.Season, "전반기 종료 · 올스타 브레이크", "72경기를 마쳤습니다. 후반기 진행으로 이어갑니다.", false, false);
            }
            if (ActiveMode.HasValue && league.GamesPlayed >= TargetGames) ActiveMode = null;
            UpdateWar();
            OnDayCompleted?.Invoke();
            return true;
        }

        private static void RecoverStamina(IEnumerable<Player> roster)
        {
            foreach (var p in roster)
            {
                if (!p.IsPitcher) continue;
                p.RecoverStamina(p.Template.PitcherRole == PitcherRole.StartingPitcher ? 25 : 15);
            }
        }

        // ================================================================== 경기

        private sealed class BatLine { public int PA, AB, H, D2, D3, HR, RBI, R, BB, SO, SB; }
        private sealed class PitchLine { public int Outs, ER, H, BB, SO, HR, BF; }
        private sealed class Stint { public Player Pitcher; public int EntryLead; public int ExitLead; }

        private void PlayGame(int day, GMTeamState home, GMTeamState away)
        {
            var homeRoster = home.AvailableRoster;
            var awayRoster = away.AvailableRoster;
            var engine = new MatchEngine(homeRoster, awayRoster, ModifiersFor(home, true), ModifiersFor(away, false), skillDB, config, random.Next())
            {
                HomeDesignatedStarter = StartingRotation.PickFor(homeRoster, day, home.Lineup),
                AwayDesignatedStarter = StartingRotation.PickFor(awayRoster, day, away.Lineup),
                HomeAssignment = home.Lineup,
                AwayAssignment = away.Lineup,
            };
            engine.BeginMatch(home.TeamCode, away.TeamCode);

            var bat = new Dictionary<Player, BatLine>();
            var pit = new Dictionary<Player, PitchLine>();
            var stints = new Dictionary<string, List<Stint>> { { home.TeamCode, new List<Stint>() }, { away.TeamCode, new List<Stint>() } };
            var bases = new Player[4];
            (int inning, bool top) half = (0, false);
            int outsInHalf = 0;
            Player winCandHome = null, winCandAway = null, loseCandHome = null, loseCandAway = null;
            bool pendingWinHome = false, pendingWinAway = false;

            BatLine B(Player p) { if (!bat.TryGetValue(p, out var l)) bat[p] = l = new BatLine(); return l; }
            PitchLine P(Player p) { if (!pit.TryGetValue(p, out var l)) pit[p] = l = new PitchLine(); return l; }

            int guard = 0;
            while (!engine.IsGameOver && guard++ < 400)
            {
                var step = engine.PlayNextAtBat();
                if (step.Batter == null || step.Pitcher == null || step.State == null) continue;

                bool top = step.IsTopHalf;
                var battingTeam = top ? away : home;
                var fieldingTeam = top ? home : away;
                if (half.inning != step.State.Inning || half.top != top)
                {
                    half = (step.State.Inning, top);
                    Array.Clear(bases, 0, bases.Length);
                    outsInHalf = 0;
                }

                int battingAfter = top ? step.AwayScore : step.HomeScore;
                int fieldingScore = top ? step.HomeScore : step.AwayScore;
                int battingBefore = battingAfter - step.RunsScoredThisPlay;

                // 투수 교체(등판 기록)
                var teamStints = stints[fieldingTeam.TeamCode];
                if (teamStints.Count == 0 || teamStints[teamStints.Count - 1].Pitcher != step.Pitcher)
                {
                    if (teamStints.Count > 0) teamStints[teamStints.Count - 1].ExitLead = fieldingScore - battingBefore;
                    teamStints.Add(new Stint { Pitcher = step.Pitcher, EntryLead = fieldingScore - battingBefore });
                    if (top && pendingWinHome) { winCandHome = step.Pitcher; pendingWinHome = false; }
                    if (!top && pendingWinAway) { winCandAway = step.Pitcher; pendingWinAway = false; }
                }

                // 타자 기록
                var bl = B(step.Batter);
                var pl = P(step.Pitcher);
                bl.PA++; pl.BF++;
                switch (step.Result)
                {
                    case AtBatResult.Walk: bl.BB++; pl.BB++; break;
                    case AtBatResult.Strikeout: bl.AB++; bl.SO++; pl.SO++; break;
                    case AtBatResult.Single: bl.AB++; bl.H++; pl.H++; break;
                    case AtBatResult.Double: bl.AB++; bl.H++; bl.D2++; pl.H++; break;
                    case AtBatResult.Triple: bl.AB++; bl.H++; bl.D3++; pl.H++; break;
                    case AtBatResult.HomeRun: bl.AB++; bl.H++; bl.HR++; pl.H++; pl.HR++; break;
                    default: bl.AB++; break;
                }
                bl.RBI += step.RunsScoredThisPlay;
                pl.ER += step.RunsScoredThisPlay;

                // 주자 이동 → 득점(R)
                var before = (Player[])bases.Clone();
                foreach (var m in step.RunnerMovements) if (m.FromBase >= 1 && m.FromBase <= 3) bases[m.FromBase] = null;
                foreach (var m in step.RunnerMovements)
                {
                    var runner = m.FromBase == 0 ? step.Batter : (m.FromBase >= 1 && m.FromBase <= 3 ? before[m.FromBase] : null);
                    if (runner == null) continue;
                    if (m.ToBase == 4) B(runner).R++;
                    else if (m.ToBase >= 1 && m.ToBase <= 3) bases[m.ToBase] = runner;
                }

                // 아웃 → 이닝
                int outsNow = Math.Min(3, step.State.Outs);
                if (outsNow > outsInHalf) { pl.Outs += outsNow - outsInHalf; outsInHalf = outsNow; }

                // 도루(기록 전용 확률) - 1루 출루 후 2루가 비어 있으면 주력에 비례해 시도 · 성공
                if ((step.Result == AtBatResult.Single || step.Result == AtBatResult.Walk) && bases[2] == null && outsInHalf < 3)
                {
                    int speed = step.Batter.Template.BatterStats.Speed;
                    double chance = Math.Max(0, Math.Min(0.14, (speed - 55) / 250.0));
                    if (random.NextDouble() < chance) bl.SB++;
                }

                // 리드 변화 → 승/패 투수 후보
                int leadBefore = battingBefore - fieldingScore, leadAfter = battingAfter - fieldingScore;
                if (leadBefore <= 0 && leadAfter > 0)
                {
                    if (top)
                    {
                        loseCandHome = step.Pitcher;
                        var own = stints[away.TeamCode];
                        if (own.Count > 0) winCandAway = own[own.Count - 1].Pitcher; else { winCandAway = null; pendingWinAway = true; }
                    }
                    else
                    {
                        loseCandAway = step.Pitcher;
                        var own = stints[home.TeamCode];
                        if (own.Count > 0) winCandHome = own[own.Count - 1].Pitcher; else { winCandHome = null; pendingWinHome = true; }
                    }
                }
                else if (leadBefore < 0 && leadAfter == 0)
                {
                    winCandHome = winCandAway = loseCandHome = loseCandAway = null;
                    pendingWinHome = pendingWinAway = false;
                }
            }

            var result = engine.Result;
            int homeRuns = result.HomeTotalScore, awayRuns = result.AwayTotalScore;
            foreach (var list in stints) if (list.Value.Count > 0)
                list.Value[list.Value.Count - 1].ExitLead = list.Key == home.TeamCode ? homeRuns - awayRuns : awayRuns - homeRuns;

            // 구단 성적
            var hr = league.RecordOf(home.TeamCode);
            var ar = league.RecordOf(away.TeamCode);
            hr.G++; ar.G++;
            hr.RunsScored += homeRuns; hr.RunsAllowed += awayRuns;
            ar.RunsScored += awayRuns; ar.RunsAllowed += homeRuns;
            string winnerCode = homeRuns > awayRuns ? home.TeamCode : awayRuns > homeRuns ? away.TeamCode : null;
            if (winnerCode == null) { hr.D++; ar.D++; hr.Streak = 0; ar.Streak = 0; }
            else
            {
                var w = winnerCode == home.TeamCode ? hr : ar;
                var l = winnerCode == home.TeamCode ? ar : hr;
                w.W++; l.L++;
                w.Streak = w.Streak > 0 ? w.Streak + 1 : 1;
                l.Streak = l.Streak < 0 ? l.Streak - 1 : -1;
            }

            // 투수 결정(승 · 패 · 세이브 · 홀드)
            Player winP = null, loseP = null, saveP = null;
            if (winnerCode != null)
            {
                bool homeWon = winnerCode == home.TeamCode;
                var winStints = stints[winnerCode];
                winP = homeWon ? winCandHome : winCandAway;
                loseP = homeWon ? loseCandAway : loseCandHome;
                if (winP == null && winStints.Count > 0) winP = winStints[0].Pitcher;
                if (winStints.Count > 1 && winP == winStints[0].Pitcher && pit.TryGetValue(winP, out var starterLine) && starterLine.Outs < 15)
                    winP = winStints[1].Pitcher; // 선발 5이닝 미만 - 첫 구원 투수에게 승리
                if (loseP == null)
                {
                    var lost = stints[homeWon ? away.TeamCode : home.TeamCode];
                    if (lost.Count > 0) loseP = lost[0].Pitcher;
                }
                var last = winStints.Count > 0 ? winStints[winStints.Count - 1] : null;
                if (last != null && winStints.Count > 1 && last.Pitcher != winP && last.EntryLead > 0 && last.EntryLead <= 3) saveP = last.Pitcher;
            }

            var holds = new List<Player>();
            foreach (var pair in stints)
            {
                var list = pair.Value;
                for (int i = 1; i < list.Count - 1; i++) // 선발 · 마지막 투수 제외 중간 계투
                {
                    var s = list[i];
                    if (s.Pitcher == winP || s.Pitcher == loseP || s.Pitcher == saveP) continue;
                    if (s.EntryLead > 0 && s.EntryLead <= 3 && s.ExitLead > 0) holds.Add(s.Pitcher);
                }
            }

            // 선수 누적 · 연속 기록 · 경기 이벤트
            int homeTeamHrBefore = hr.TeamHomeRuns, awayTeamHrBefore = ar.TeamHomeRuns;
            foreach (var pair in bat)
            {
                var player = pair.Key; var line = pair.Value;
                string code = home.Roster.Contains(player) ? home.TeamCode : away.TeamCode;
                var st = league.StatsOf(player, code);
                st.G++; st.PA += line.PA; st.AB += line.AB; st.H += line.H; st.Doubles += line.D2; st.Triples += line.D3; st.HR += line.HR;
                st.RBI += line.RBI; st.R += line.R; st.BB += line.BB; st.SO += line.SO; st.SB += line.SB;
                st.CurrentHitStreak = line.H > 0 ? st.CurrentHitStreak + 1 : 0;
                st.MaxHitStreak = Math.Max(st.MaxHitStreak, st.CurrentHitStreak);
                st.CurrentHrStreak = line.HR > 0 ? st.CurrentHrStreak + 1 : 0;
                league.RecordOf(code).TeamHomeRuns += line.HR;
                weeklyHits.TryGetValue(player.InstanceId, out int wk);
                weeklyHits[player.InstanceId] = wk + line.H;
                CheckBatterEvents(day, player, code, line, st);
            }
            foreach (var pair in pit)
            {
                var player = pair.Key; var line = pair.Value;
                string code = home.Roster.Contains(player) ? home.TeamCode : away.TeamCode;
                var st = league.StatsOf(player, code);
                st.PG++; st.OutsPitched += line.Outs; st.ER += line.ER; st.PSO += line.SO; st.PBB += line.BB; st.HA += line.H; st.HRA += line.HR;
                var own = stints[code];
                if (own.Count > 0 && own[0].Pitcher == player) st.GS++;
                if (player == winP) st.W++;
                if (player == loseP) st.L++;
                if (player == saveP) st.SV++;
                if (holds.Contains(player)) st.HLD++;
                bool complete = own.Count == 1 && own[0].Pitcher == player;
                int allowed = code == home.TeamCode ? awayRuns : homeRuns;
                CheckPitcherEvents(day, player, code, line, complete, allowed);
            }
            CheckTeamHomeRunMilestone(day, home.TeamCode, homeTeamHrBefore, hr.TeamHomeRuns);
            CheckTeamHomeRunMilestone(day, away.TeamCode, awayTeamHrBefore, ar.TeamHomeRuns);

            if (home.IsUserTeam || away.IsUserTeam)
            {
                var me = home.IsUserTeam ? home : away;
                var opp = home.IsUserTeam ? away : home;
                int my = me == home ? homeRuns : awayRuns, their = me == home ? awayRuns : homeRuns;
                string outcome = my > their ? "승" : my < their ? "패" : "무";
                LastUserGameLine = $"G{day + 1} {CompyaShort(me.Team)} {my} : {their} {CompyaShort(opp.Team)} ({outcome})";
            }

            RollInjury(day, home, bat, pit);
            RollInjury(day, away, bat, pit);
        }

        private TeamPowerModifiers ModifiersFor(GMTeamState team, bool isHome)
        {
            var available = team.AvailableRoster;
            var report = TeamChemistryEngine.EvaluateRoster(available, team.PayrollCap, team.CheerLeadershipBuff);
            var record = league.RecordOf(team.TeamCode);
            var entry = team.CheerEntry.ToList();
            var cheer = entry.Count >= GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN
                ? CheerSquad.BuildEffects(entry, team.Team, isHome, Math.Max(0, -record.Streak), 0)
                : null;
            int teamOvr = available.Count > 0 ? (int)Math.Round(available.Average(p => p.GetEffectiveOverall())) : 0;
            return new TeamPowerModifiers(0, isHome ? TeamPowerModifiers.HomeAdvantageConditionBuff : 0, 1.0f, null, cheer, teamOvr,
                GMChemistryModifiers.From(report));
        }

        // ================================================================== 부상

        private void TickInjuries(int day)
        {
            foreach (var team in league.Teams.Values)
            {
                foreach (var p in team.Roster)
                {
                    if (p.InjuryRemainingDays <= 0) continue;
                    p.InjuryRemainingDays--;
                    if (p.InjuryRemainingDays > 0) continue;
                    if (!p.IsPitcher) team.Lineup.Starters.RemoveAll(pin => pin.Position == p.Template.BatterPosition && pin.InstanceId != p.InstanceId);
                    AddNews(day, GMNewsKind.Return, $"{p.Template.PlayerName} 1군 복귀",
                        $"{NameAliasTable.DisplayTeamName(team.TeamCode)} {p.Template.PlayerName}({p.Position})이(가) 부상을 털고 엔트리에 복귀했습니다.", team.IsUserTeam, false);
                }
            }
        }

        private void RollInjury(int day, GMTeamState team, Dictionary<Player, BatLine> bat, Dictionary<Player, PitchLine> pit)
        {
            if (random.NextDouble() >= InjuryChancePerTeamGame) return;
            var played = bat.Keys.Concat(pit.Keys).Where(p => team.Roster.Contains(p) && p.InjuryRemainingDays <= 0).Distinct().ToList();
            if (played.Count == 0) return;
            var player = played[random.Next(played.Count)];
            double roll = random.NextDouble();
            int days; string part; string grade;
            if (roll < 0.6) { days = random.Next(MinorMin, MinorMax + 1); part = MinorParts[random.Next(MinorParts.Length)]; grade = "경미"; }
            else if (roll < 0.9) { days = random.Next(ModerateMin, ModerateMax + 1); part = ModerateParts[random.Next(ModerateParts.Length)]; grade = "중등"; }
            else { days = random.Next(SevereMin, SevereMax + 1); part = SevereParts[random.Next(SevereParts.Length)]; grade = "중상"; }
            Injure(day, team, player, days, part, grade);
        }

        /// <summary>부상 등록 + 소식(내 구단 주전이면 인터럽트). 테스트에서도 직접 호출한다.</summary>
        public void Injure(int day, GMTeamState team, Player player, int days, string part, string grade = "경미")
        {
            player.InjuryRemainingDays = days;
            bool starter = IsStarter(team, player);
            var news = AddNews(day, GMNewsKind.Injury, $"{player.Template.PlayerName} {part}",
                $"{NameAliasTable.DisplayTeamName(team.TeamCode)} {player.Template.PlayerName}({player.Position}) {part}({grade}) - 약 {days}일 결장 예정.",
                team.IsUserTeam, team.IsUserTeam && starter);
            if (!team.IsUserTeam || !starter) return;

            var interrupt = new GMSimInterrupt { Kind = GMInterruptKind.Injury, News = news, Player = player, TeamCode = team.TeamCode };
            if (!player.IsPitcher)
            {
                interrupt.Position = player.Template.BatterPosition;
                var starters = new HashSet<Player>(LineupAssignment.AssignStarters(team.AvailableRoster, team.Lineup).Select(s => s.Player).Where(p => p != null));
                interrupt.ReplacementCandidates.AddRange(team.AvailableRoster
                    .Where(p => !p.IsPitcher && p != player && !starters.Contains(p))
                    .OrderByDescending(p => p.Template.BatterPosition == player.Template.BatterPosition)
                    .ThenByDescending(p => p.GetEffectiveOverall()).Take(3));
            }
            interrupts.Enqueue(interrupt);
        }

        private static bool IsStarter(GMTeamState team, Player player)
        {
            if (player.IsPitcher) return StartingRotation.RotationOf(team.Roster, team.Lineup).Contains(player) || player.Template.PitcherRole == PitcherRole.Closer;
            return LineupAssignment.AssignStarters(team.Roster, team.Lineup).Any(s => s.Player == player);
        }

        // ================================================================== 대기록 · 소식

        private void CheckBatterEvents(int day, Player p, string code, BatLine line, GMPlayerSeasonStats st)
        {
            bool user = code == league.SelectedTeamCode;
            string who = $"{NameAliasTable.DisplayTeamName(code)} {p.Template.PlayerName}";
            if (line.H >= 1 && line.D2 >= 1 && line.D3 >= 1 && line.HR >= 1 && line.H - line.D2 - line.D3 - line.HR >= 1)
                Record(day, $"{p.Template.PlayerName} 사이클링 히트!", $"{who}이(가) 단타 · 2루타 · 3루타 · 홈런을 모두 기록했습니다.", user, true);
            if (line.HR >= 3) Record(day, $"{p.Template.PlayerName} 한 경기 3홈런", $"{who}이(가) 한 경기 {line.HR}홈런을 터뜨렸습니다.", user, true);
            else if (line.HR == 2) Record(day, $"{p.Template.PlayerName} 멀티홈런", $"{who} 멀티홈런(시즌 {st.HR}호).", user, false);
            if (line.HR > 0 && st.CurrentHrStreak >= 3)
                Record(day, $"{p.Template.PlayerName} {st.CurrentHrStreak}경기 연속 홈런", $"{who} {st.CurrentHrStreak}경기 연속 홈런 행진(시즌 {st.HR}호).", user, false);
            if (st.CurrentHitStreak >= 15 && line.H > 0 && (st.CurrentHitStreak == 15 || st.CurrentHitStreak % 5 == 0))
                Record(day, $"{p.Template.PlayerName} {st.CurrentHitStreak}경기 연속 안타", $"{who} {st.CurrentHitStreak}경기 연속 안타 행진 중.", user, st.CurrentHitStreak >= 20);
        }

        private void CheckPitcherEvents(int day, Player p, string code, PitchLine line, bool complete, int allowed)
        {
            bool user = code == league.SelectedTeamCode;
            string who = $"{NameAliasTable.DisplayTeamName(code)} {p.Template.PlayerName}";
            if (complete && allowed == 0 && line.Outs >= 27)
                Record(day, $"{p.Template.PlayerName} 완봉승", $"{who} 9이닝 무실점 완봉 역투({line.SO}탈삼진).", user, true);
            else if (line.SO >= 10)
                Record(day, $"{p.Template.PlayerName} 두 자릿수 탈삼진", $"{who} 한 경기 {line.SO}탈삼진.", user, false);
        }

        private void CheckTeamHomeRunMilestone(int day, string code, int before, int after)
        {
            if (after / 50 <= before / 50 || after < 50) return;
            AddNews(day, GMNewsKind.Milestone, $"{NameAliasTable.DisplayTeamName(code)} 시즌 팀 {after / 50 * 50}홈런", $"{NameAliasTable.DisplayTeamName(code)}이(가) 시즌 팀 홈런 {after / 50 * 50}개를 돌파했습니다.",
                code == league.SelectedTeamCode, false);
        }

        /// <summary>대기록 소식. 내 구단 기록이거나 리그 최상급(사이클 · 3홈런 · 완봉 · 20경기 연속 안타)이면 시뮬레이션을 멈추고 팝업.</summary>
        private void Record(int day, string title, string body, bool userTeam, bool leagueMajor)
        {
            bool pause = userTeam || leagueMajor;
            var news = AddNews(day, GMNewsKind.Record, title, body, userTeam, pause);
            if (pause) interrupts.Enqueue(new GMSimInterrupt { Kind = GMInterruptKind.Record, News = news });
        }

        private void PeriodicNews(int day)
        {
            int played = day + 1;
            if (played % 6 == 0 && weeklyHits.Count > 0)
            {
                var best = weeklyHits.OrderByDescending(p => p.Value).First();
                var player = league.FindPlayer(best.Key);
                if (player != null)
                {
                    string code = league.TeamCodeOf(player);
                    AddNews(day, GMNewsKind.Weekly, $"주간 스타: {player.Template.PlayerName}", $"{NameAliasTable.DisplayTeamName(code)} {player.Template.PlayerName} 이번 주 {best.Value}안타 맹타.", code == league.SelectedTeamCode, false);
                }
                weeklyHits.Clear();
            }
            if (played % 24 == 0)
            {
                var mvp = league.Stats.Values.OrderByDescending(s => s.IsPitcher ? s.PitcherWAR : s.BatterWAR).FirstOrDefault();
                var player = mvp != null ? league.FindPlayer(mvp.PlayerId) : null;
                if (player != null)
                    AddNews(day, GMNewsKind.Monthly, $"월간 MVP 후보: {player.Template.PlayerName}", $"{NameAliasTable.DisplayTeamName(mvp.TeamCode)} {player.Template.PlayerName} - 시즌 WAR {(mvp.IsPitcher ? mvp.PitcherWAR : mvp.BatterWAR):0.00}.", mvp.TeamCode == league.SelectedTeamCode, false);
            }
            if (played % 20 == 10 && league.FreeAgents.Count > 0)
            {
                var fa = league.FreeAgents[(played / 20) % league.FreeAgents.Count];
                AddNews(day, GMNewsKind.Scouting, "스카우팅 리포트 도착", $"FA 시장 관찰 대상: {fa.Template.PlayerName}({fa.Position}, OVR {fa.GetEffectiveOverall()}, 희망 연봉 {fa.Salary:N0}만 원).", true, false);
            }
            if (played % 30 == 15)
            {
                var codes = NameAliasTable.CanonicalTeamCodes.Where(c => c != league.SelectedTeamCode).ToList();
                string a = codes[random.Next(codes.Count)], b = codes[random.Next(codes.Count)];
                if (a != b) AddNews(day, GMNewsKind.Trade, "트레이드 소문", $"{NameAliasTable.DisplayTeamName(a)}와(과) {NameAliasTable.DisplayTeamName(b)}가 불펜 보강 트레이드를 논의 중이라는 소식입니다.", false, false);
            }
        }

        private GMNewsItem AddNews(int day, GMNewsKind kind, string title, string body, bool userTeam, bool major)
        {
            var item = new GMNewsItem { GameIndex = day, DateLabel = DateLabel(day), Kind = kind, Title = title, Body = body, IsUserTeam = userTeam, IsMajor = major };
            league.AddNews(item);
            return item;
        }

        // ================================================================== 순위 · 리더보드 · WAR

        /// <summary>승률 → 승 → 득실차 순 정렬.</summary>
        public List<GMTeamRecord> Standings() =>
            NameAliasTable.CanonicalTeamCodes.Select(league.RecordOf)
                .OrderByDescending(r => r.Pct).ThenByDescending(r => r.W).ThenByDescending(r => r.RunsScored - r.RunsAllowed).ToList();

        /// <summary>1위와의 게임차 = ((1위 승 - 승) + (패 - 1위 패)) / 2.</summary>
        public static double GamesBehind(GMTeamRecord leader, GMTeamRecord team) => ((leader.W - team.W) + (team.L - leader.L)) / 2.0;

        public static string GamesBehindLabel(double gb) => gb <= 0 ? "-" : gb.ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>부문별 1~3위. 비율 부문은 규정 타석(경기 수 × 3.1) · 규정 이닝(경기 수 × 1)을 채운 선수만(아무도 없으면 상위 표본으로 대체).</summary>
        public List<GMLeaderEntry> Leaders(GMLeaderCategory category, int top = 3)
        {
            bool pitching = GMLeaderCategories.IsPitching(category);
            var pool = league.Stats.Values.Where(s => pitching ? s.PG > 0 : s.PA > 0).ToList();
            if (GMLeaderCategories.NeedsQualification(category))
            {
                int g = Math.Max(1, league.GamesPlayed);
                var qualified = pool.Where(s => pitching ? s.OutsPitched >= g * 3 : s.PA >= g * 3.1).ToList();
                pool = qualified.Count >= top ? qualified : pool.OrderByDescending(s => pitching ? s.OutsPitched : s.PA).Take(Math.Max(top, 10)).ToList();
            }
            var ordered = GMLeaderCategories.LowerIsBetter(category)
                ? pool.OrderBy(s => GMLeaderCategories.Value(category, s))
                : pool.OrderByDescending(s => GMLeaderCategories.Value(category, s));
            return ordered.Take(top).Select(s =>
            {
                var p = league.FindPlayer(s.PlayerId);
                double v = GMLeaderCategories.Value(category, s);
                return new GMLeaderEntry { PlayerId = s.PlayerId, Name = p?.Template?.PlayerName ?? "-", TeamCode = s.TeamCode, Value = v, ValueLabel = GMLeaderCategories.Format(category, v) };
            }).ToList();
        }

        /// <summary>
        /// 간이 WAR - 타자: (wRAA + 대체 수준 20점/600타석 + 도루 0.2점) / 10, wOBA 가중치(BB .69 · 1B .89 · 2B 1.27 · 3B 1.62 · HR 2.10).
        /// 투수: ((리그 FIP + 1.0 - FIP) × 이닝 / 9) / 10, FIP = (13HR + 3BB - 2SO) / IP + 상수(리그 FIP = 리그 ERA).
        /// </summary>
        public void UpdateWar()
        {
            var batters = league.Stats.Values.Where(s => s.PA > 0).ToList();
            int lgPa = batters.Sum(s => s.PA);
            double lgWoba = lgPa == 0 ? 0.32 : batters.Sum(Woba) / lgPa;
            foreach (var s in batters)
            {
                double woba = s.PA == 0 ? 0 : Woba(s) / s.PA;
                double wraa = (woba - lgWoba) / 1.15 * s.PA;
                s.BatterWAR = (float)((wraa + s.PA * 20.0 / 600.0 + s.SB * 0.2) / 10.0);
            }

            var pitchers = league.Stats.Values.Where(s => s.OutsPitched > 0).ToList();
            double lgIp = pitchers.Sum(s => s.IP);
            if (lgIp <= 0) return;
            double lgEra = pitchers.Sum(s => s.ER) * 9.0 / lgIp;
            double lgRaw = pitchers.Sum(s => 13.0 * s.HRA + 3.0 * s.PBB - 2.0 * s.PSO) / lgIp;
            double constant = lgEra - lgRaw;
            foreach (var s in pitchers)
            {
                double fip = (13.0 * s.HRA + 3.0 * s.PBB - 2.0 * s.PSO) / s.IP + constant;
                s.PitcherWAR = (float)(((lgEra + 1.0 - fip) * s.IP / 9.0) / 10.0);
            }
        }

        private static double Woba(GMPlayerSeasonStats s) => 0.69 * s.BB + 0.89 * s.Singles + 1.27 * s.Doubles + 1.62 * s.Triples + 2.10 * s.HR;

        private static string CompyaShort(Team team) => KBOManager.UI.CompyaUiKit.ShortName(team);
    }
}
