using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Models;
using KBOManager.Services;

namespace KBOManager.Simulation
{
    public partial class GMLiveSeasonSimulator
    {
        /// <summary>
        /// [TASK-GM-07] 한 경기를 타석 단위로 진행하는 세션. 기존 PlayGame 본문(기록 · 실책 · ABS · WPA · 승패 투수 · 박스스코어)을 그대로 옮겨
        ///   - 일반 경기: new → PlayToEnd → Finish(시즌 누적 · 순위 · 소식 · 부상 반영)
        ///   - 실시간 이닝 경기(LiveMatchInningView): Step / StepHalfInning + 전술 개입(강공 · 작전 · 투수교체 · 대타) → Finish
        ///   - 포스트시즌(recordSeason = false): 시즌 누적 · 순위 · 부상 · 흥행에 반영하지 않고, 체력 · 컨디션을 경기 전 상태로 되돌린다.
        /// </summary>
        /// <summary>[TASK-GM-08] 타석 직전 상황(사운드 연출 판정용) - 득점권 주자 · 점수 · 이닝, 타석 후 이 하프이닝 누적 득점.</summary>
        public sealed class StepContext
        {
            public bool RispBefore;
            public int HomeBefore, AwayBefore;
            public int Inning;
            public int HalfRuns;
        }

        public sealed class GameSession
        {
            private readonly GMLiveSeasonSimulator sim;
            private readonly GMLeagueState league;
            private readonly int day;
            private readonly bool recordSeason;
            private readonly List<Player> homeRoster, awayRoster;
            private readonly List<Player> homeOrder, awayOrder;
            private readonly Dictionary<int, Player> homeField, awayField;
            private readonly Dictionary<Player, FieldLine> fielding = new Dictionary<Player, FieldLine>();
            private readonly Dictionary<Player, BatLine> bat = new Dictionary<Player, BatLine>();
            private readonly Dictionary<Player, PitchLine> pit = new Dictionary<Player, PitchLine>();
            private readonly Dictionary<string, List<Player>> pitchOrder;
            private readonly Dictionary<string, List<Stint>> stints;
            private readonly Dictionary<(int, bool), int> inningRuns = new Dictionary<(int, bool), int>();
            private readonly List<PaRecord> plays = new List<PaRecord>();
            private readonly Player[] bases = new Player[4];
            private readonly List<(Player p, int stamina, PlayerCondition condition)> savedState;
            private (int inning, bool top) half = (0, false);
            private int outsInHalf;
            private bool errorThisHalf;
            private int homeE, awayE;
            private int homeAbsCalls, awayAbsCalls, homeBlocks, awayBlocks, homeLookingK, awayLookingK, homeAbsWalks, awayAbsWalks;
            private readonly int homeCatcherAbs, awayCatcherAbs;
            private Player winCandHome, winCandAway, loseCandHome, loseCandAway;
            private bool pendingWinHome, pendingWinAway;
            private readonly List<WPAPoint> wpa = new List<WPAPoint> { new WPAPoint { PlateAppearance = -1, Inning = 1, IsTop = true, HomeWinProbability = 0.5f } };
            private int guard;
            private bool finished;
            private GMMatchBoxScoreData box;

            public readonly MatchEngine Engine;
            public readonly GMTeamState Home, Away;
            public readonly bool IsPostSeason;
            public readonly int GameIndex;
            public string DateLabel;
            public string GameTitle = "";
            /// <summary>실시간 문자 중계(오래된 순).</summary>
            public readonly List<string> PlayByPlay = new List<string>();
            public AtBatStepResult LastStep { get; private set; }
            /// <summary>[TASK-GM-08] 타석 1회가 기록될 때마다(실시간 이닝 경기 사운드 연출).</summary>
            public event Action<AtBatStepResult, StepContext> OnStepped;
            public int PlateAppearances => plays.Count;
            public bool IsOver => Engine.IsGameOver || guard >= 400;
            public bool IsFinished => finished;
            public int HomeScore => Engine.Result?.HomeTotalScore ?? 0;
            public int AwayScore => Engine.Result?.AwayTotalScore ?? 0;
            public int HomeHits => bat.Where(x => homeRoster.Contains(x.Key)).Sum(x => x.Value.H);
            public int AwayHits => bat.Where(x => awayRoster.Contains(x.Key)).Sum(x => x.Value.H);
            public int HomeErrors => homeE;
            public int AwayErrors => awayE;
            public int Outs => outsInHalf;
            public bool OnFirst => bases[1] != null;
            public bool OnSecond => bases[2] != null;
            public bool OnThird => bases[3] != null;
            public IReadOnlyList<WPAPoint> Wpa => wpa;
            public float HomeWinProbabilityNow => wpa.Count > 0 ? wpa[wpa.Count - 1].HomeWinProbability : 0.5f;
            public GMMatchBoxScoreData BoxScore => box;
            /// <summary>다음 타석이 원정 공격(초)인지.</summary>
            public bool NextIsTop => Engine.IsAwayBatting;
            public int CurrentInning => Engine.CurrentInning;
            public Player UpcomingBatter => Engine.UpcomingBatter;
            public Player CurrentPitcher => Engine.CurrentPitcher;
            public GMTeamState UserTeam => Home.IsUserTeam ? Home : Away.IsUserTeam ? Away : null;
            public bool UserBatting => UserTeam != null && (UserTeam == Away) == Engine.IsAwayBatting;
            /// <summary>이닝별 득점(-1 = 아직/생략). 1회부터.</summary>
            public int InningRuns(int inning, bool top) => inningRuns.TryGetValue((inning, top), out int r) ? r : -1;
            public int MaxInningPlayed => inningRuns.Keys.Select(k => k.Item1).DefaultIfEmpty(1).Max();

            internal GameSession(GMLiveSeasonSimulator sim, int day, GMTeamState home, GMTeamState away, bool recordSeason, bool isPostSeason,
                int? seed = null, Player homeStarter = null, Player awayStarter = null, int gameIndex = -1, string dateLabel = null)
            {
                this.sim = sim;
                league = sim.league;
                this.day = day;
                this.recordSeason = recordSeason;
                Home = home;
                Away = away;
                IsPostSeason = isPostSeason;
                GameIndex = gameIndex >= 0 ? gameIndex : day;
                DateLabel = dateLabel ?? sim.DayLabel(day);
                homeRoster = home.AvailableRoster;
                awayRoster = away.AvailableRoster;
                if (!recordSeason)
                    savedState = homeRoster.Concat(awayRoster).Distinct().Select(p => (p, p.CurrentStamina, p.CurrentCondition)).ToList();
                Engine = new MatchEngine(homeRoster, awayRoster, sim.ModifiersFor(home, true), sim.ModifiersFor(away, false), sim.skillDB, sim.config, seed ?? sim.random.Next())
                {
                    HomeDesignatedStarter = homeStarter ?? StartingRotation.PickFor(homeRoster, day, home.Lineup),
                    AwayDesignatedStarter = awayStarter ?? StartingRotation.PickFor(awayRoster, day, away.Lineup),
                    HomeAssignment = home.Lineup,
                    AwayAssignment = away.Lineup,
                };
                Engine.BeginMatch(home.TeamCode, away.TeamCode, isPostSeason);
                homeOrder = Engine.HomeBattingOrder != null ? Engine.HomeBattingOrder.ToList() : new List<Player>();
                awayOrder = Engine.AwayBattingOrder != null ? Engine.AwayBattingOrder.ToList() : new List<Player>();
                // [TASK-GM-04] 수비 위치(0~7 = 포수~우익수, 8 = 지명타자) - 실책 · 처리 기회 · 호수비 귀속
                homeField = FieldersOf(homeRoster, home.Lineup);
                awayField = FieldersOf(awayRoster, away.Lineup);
                pitchOrder = new Dictionary<string, List<Player>> { { home.TeamCode, new List<Player>() }, { away.TeamCode, new List<Player>() } };
                stints = new Dictionary<string, List<Stint>> { { home.TeamCode, new List<Stint>() }, { away.TeamCode, new List<Stint>() } };
                homeCatcherAbs = CatcherAbsSkill(homeRoster, home.Lineup);
                awayCatcherAbs = CatcherAbsSkill(awayRoster, away.Lineup);
            }

            private BatLine B(Player p) { if (!bat.TryGetValue(p, out var l)) bat[p] = l = new BatLine(); return l; }
            private PitchLine P(Player p) { if (!pit.TryGetValue(p, out var l)) pit[p] = l = new PitchLine(); return l; }

            // ================================================================== 진행

            /// <summary>타석 1회. 경기가 끝났으면 null.</summary>
            public AtBatStepResult Step()
            {
                while (!IsOver)
                {
                    guard++;
                    var ctx = OnStepped != null ? new StepContext { RispBefore = OnSecond || OnThird, HomeBefore = HomeScore, AwayBefore = AwayScore, Inning = Engine.CurrentInning } : null;
                    var step = Engine.PlayNextAtBat();
                    if (step.Batter == null || step.Pitcher == null || step.State == null) continue;
                    Record(step);
                    LastStep = step;
                    if (ctx != null)
                    {
                        ctx.HalfRuns = Math.Max(0, InningRuns(ctx.Inning, step.IsTopHalf));
                        OnStepped?.Invoke(step, ctx);
                    }
                    return step;
                }
                return null;
            }

            /// <summary>현재 하프이닝(초/말)을 끝까지 진행한다. 진행한 타석 수.</summary>
            public int StepHalfInning()
            {
                int n = 0;
                while (!IsOver)
                {
                    var s = Step();
                    if (s == null) break;
                    n++;
                    if (s.HalfInningEnded || s.GameEnded) break;
                }
                return n;
            }

            public void PlayToEnd()
            {
                while (!IsOver && Step() != null) { }
            }

            private void Record(AtBatStepResult step)
            {
                bool top = step.IsTopHalf;
                var battingTeam = top ? Away : Home;
                var fieldingTeam = top ? Home : Away;
                if (half.inning != step.State.Inning || half.top != top)
                {
                    half = (step.State.Inning, top);
                    Array.Clear(bases, 0, bases.Length);
                    outsInHalf = 0;
                    errorThisHalf = false;
                    if (!inningRuns.ContainsKey(half)) inningRuns[half] = 0;
                }

                int battingAfter = top ? step.AwayScore : step.HomeScore;
                int fieldingScore = top ? step.HomeScore : step.AwayScore;
                int battingBefore = battingAfter - step.RunsScoredThisPlay;
                int homeBefore = top ? fieldingScore : battingBefore, awayBefore = top ? battingBefore : fieldingScore;
                float pBefore = HomeWinProbability(half.inning, top, outsInHalf, homeBefore, awayBefore, bases[1] != null, bases[2] != null, bases[3] != null);
                var record = new PaRecord
                {
                    Pa = step.PlateAppearance, Inning = half.inning, Top = top, Outs = outsInHalf, Batter = step.Batter, TeamCode = battingTeam.TeamCode,
                    Result = step.Result, Error = step.IsError, Runs = step.RunsScoredThisPlay, R1 = bases[1] != null, R2 = bases[2] != null, R3 = bases[3] != null,
                };

                // 투수 교체(등판 기록)
                var teamStints = stints[fieldingTeam.TeamCode];
                if (teamStints.Count == 0 || teamStints[teamStints.Count - 1].Pitcher != step.Pitcher)
                {
                    if (teamStints.Count > 0) teamStints[teamStints.Count - 1].ExitLead = fieldingScore - battingBefore;
                    teamStints.Add(new Stint { Pitcher = step.Pitcher, EntryLead = fieldingScore - battingBefore });
                    if (!pitchOrder[fieldingTeam.TeamCode].Contains(step.Pitcher)) pitchOrder[fieldingTeam.TeamCode].Add(step.Pitcher);
                    if (top && pendingWinHome) { winCandHome = step.Pitcher; pendingWinHome = false; }
                    if (!top && pendingWinAway) { winCandAway = step.Pitcher; pendingWinAway = false; }
                }

                // 타자 · 투수 기록(실책 출루는 타수만 - 안타 아님, 이후 이 이닝 실점은 비자책)
                var bl = B(step.Batter);
                var pl = P(step.Pitcher);
                bl.PA++; pl.BF++;
                if (step.IsError)
                {
                    bl.AB++;
                    errorThisHalf = true;
                    if (fieldingTeam == Home) homeE++; else awayE++;
                }
                else
                {
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
                }
                bl.RBI += step.IsError ? 0 : step.RunsScoredThisPlay;
                pl.R += step.RunsScoredThisPlay;
                if (!errorThisHalf) pl.ER += step.RunsScoredThisPlay;
                int np = step.Result == AtBatResult.Strikeout ? 3 + sim.random.Next(0, 4)
                       : step.Result == AtBatResult.Walk ? 4 + sim.random.Next(0, 4)
                       : 1 + sim.random.Next(0, 5);
                pl.NP += np;
                inningRuns[half] = inningRuns[half] + step.RunsScoredThisPlay;
                sim.RecordFielding(top ? homeField : awayField, step, fielding);
                // [TASK-GM-06] ABS 기록(수비 난수열) - 보더라인 콜 · 루킹 삼진은 수비 투수진, 블로킹 세이브는 수비 포수, 볼넷은 공격 팀
                var abs = sim.RollAbsEvents(step, bases[1] != null || bases[2] != null || bases[3] != null, top ? homeCatcherAbs : awayCatcherAbs);
                if (top) { homeAbsCalls += abs.calls; homeLookingK += abs.lookingK; homeBlocks += abs.block; if (step.Result == AtBatResult.Walk && !step.IsError) awayAbsWalks++; }
                else { awayAbsCalls += abs.calls; awayLookingK += abs.lookingK; awayBlocks += abs.block; if (step.Result == AtBatResult.Walk && !step.IsError) homeAbsWalks++; }
                if (step.Result == AtBatResult.Groundout && step.RunnerMovements.Any(m => m.FromBase == 1 && m.ToBase == -1))
                {
                    bl.GIDP++;
                    record.Gidp = true;
                }

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
                if ((step.Result == AtBatResult.Single || step.Result == AtBatResult.Walk) && !step.IsError && bases[2] == null && outsInHalf < 3)
                {
                    int speed = step.Batter.Template.BatterStats.Speed;
                    double chance = Math.Max(0, Math.Min(0.14, (speed - 55) / 250.0));
                    if (sim.random.NextDouble() < chance) bl.SB++;
                }

                // 승리 확률(타석 후)
                float pAfter;
                if (step.GameEnded) pAfter = step.HomeScore > step.AwayScore ? 1f : step.HomeScore < step.AwayScore ? 0f : 0.5f;
                else if (step.HalfInningEnded) pAfter = top ? HomeWinProbability(half.inning, false, 0, step.HomeScore, step.AwayScore)
                                                            : HomeWinProbability(half.inning + 1, true, 0, step.HomeScore, step.AwayScore);
                else pAfter = HomeWinProbability(half.inning, top, outsInHalf, step.HomeScore, step.AwayScore, bases[1] != null, bases[2] != null, bases[3] != null);
                record.Delta = (pAfter - pBefore) * (top ? -1f : 1f);
                plays.Add(record);
                wpa.Add(new WPAPoint { PlateAppearance = step.PlateAppearance, Inning = half.inning, IsTop = top, HomeWinProbability = pAfter });

                // 리드 변화 → 승/패 투수 후보
                int leadBefore = battingBefore - fieldingScore, leadAfter = battingAfter - fieldingScore;
                if (leadBefore <= 0 && leadAfter > 0)
                {
                    if (top)
                    {
                        loseCandHome = step.Pitcher;
                        var own = stints[Away.TeamCode];
                        if (own.Count > 0) winCandAway = own[own.Count - 1].Pitcher; else { winCandAway = null; pendingWinAway = true; }
                    }
                    else
                    {
                        loseCandAway = step.Pitcher;
                        var own = stints[Home.TeamCode];
                        if (own.Count > 0) winCandHome = own[own.Count - 1].Pitcher; else { winCandHome = null; pendingWinHome = true; }
                    }
                }
                else if (leadBefore < 0 && leadAfter == 0)
                {
                    winCandHome = winCandAway = loseCandHome = loseCandAway = null;
                    pendingWinHome = pendingWinAway = false;
                }

                PlayByPlay.Add($"{half.inning}회{(top ? "초" : "말")} {CompyaShort(battingTeam.Team)} {step.Batter.Template.PlayerName} - {ResultLabel(record)}" +
                               $"{(step.RunsScoredThisPlay > 0 ? $" ({step.RunsScoredThisPlay}점, {step.AwayScore}:{step.HomeScore})" : "")}" +
                               $"{(step.Tactic != MatchTactic.None ? $" [작전 {MatchTacticLabel(step.Tactic)}]" : "")}");
            }

            // ================================================================== 전술 개입

            /// <summary>다음 타석 작전. 공격(강공 · 컨택 · 번트 · 도루)은 내 구단 공격 때, 수비(정면 승부 · 고의사구)는 수비 때만 받는다.</summary>
            public bool SetTactic(MatchTactic tactic, out string message)
            {
                message = "";
                if (IsOver || UserTeam == null) { message = "경기가 끝났거나 내 구단 경기가 아닙니다."; return false; }
                bool offensive = tactic == MatchTactic.PowerSwing || tactic == MatchTactic.ContactSwing || tactic == MatchTactic.Bunt || tactic == MatchTactic.Steal;
                if (offensive != UserBatting) { message = offensive ? "공격 중일 때만 쓸 수 있는 작전입니다." : "수비 중일 때만 쓸 수 있는 작전입니다."; return false; }
                if (tactic == MatchTactic.Steal && !OnFirst) { message = "1루 주자가 없어 도루를 걸 수 없습니다."; return false; }
                Engine.Tactics[Engine.NextPlateAppearance] = tactic;
                message = $"다음 타석 작전: {MatchTacticLabel(tactic)}";
                return true;
            }

            /// <summary>[대타] - 벤치 타자 중 OVR 최고(타순 · 교체 아웃 제외)를 다음 타자 자리에 넣는다. 내 구단 공격 때만.</summary>
            public bool PinchHit(out string message)
            {
                message = "";
                if (IsOver || UserTeam == null || !UserBatting) { message = "내 구단 공격 때만 대타를 낼 수 있습니다."; return false; }
                var roster = UserTeam == Home ? homeRoster : awayRoster;
                var order = UserTeam == Home ? Engine.HomeBattingOrder : Engine.AwayBattingOrder;
                var outgoing = Engine.UpcomingBatter;
                var pinch = roster.Where(p => !p.IsPitcher && (order == null || !order.Contains(p)) && !Engine.SubbedOutList.Contains(p))
                    .OrderByDescending(p => p.GetEffectiveOverall()).FirstOrDefault();
                if (pinch == null || outgoing == null || !Engine.SubstituteBatter(pinch)) { message = "투입할 벤치 타자가 없습니다."; return false; }
                message = $"대타 {pinch.Template.PlayerName}(OVR {pinch.GetEffectiveOverall()}) ← {outgoing.Template.PlayerName}";
                PlayByPlay.Add($"[교체] {message}");
                return true;
            }

            /// <summary>[투수 교체] - 아직 등판하지 않은 불펜 중 OVR 최고 투수로 바꾼다. 내 구단 수비 때만.</summary>
            public bool ChangePitcher(out string message)
            {
                message = "";
                if (IsOver || UserTeam == null || UserBatting) { message = "내 구단 수비 때만 투수를 바꿀 수 있습니다."; return false; }
                var roster = UserTeam == Home ? homeRoster : awayRoster;
                var used = pitchOrder[UserTeam.TeamCode];
                var current = Engine.CurrentPitcher;
                var reliever = roster.Where(p => p.IsPitcher && p != current && !used.Contains(p) && !Engine.SubbedOutList.Contains(p)
                                                 && LineupAssignment.RoleOf(p) != PitcherRole.StartingPitcher)
                    .OrderByDescending(p => p.GetEffectiveOverall()).FirstOrDefault();
                if (reliever == null || !Engine.SubstitutePitcher(reliever)) { message = "올릴 수 있는 불펜 투수가 없습니다."; return false; }
                message = $"투수 교체 {reliever.Template.PlayerName}(OVR {reliever.GetEffectiveOverall()}) ← {current?.Template.PlayerName}";
                PlayByPlay.Add($"[교체] {message}");
                return true;
            }

            /// <summary>[TASK-GM-19] 오늘 투구수(실시간 경기 대시보드 투구수 · 체력 게이지).</summary>
            public int TodayPitchCount(Player p) => p != null && pit.TryGetValue(p, out var l) ? l.NP : 0;
            /// <summary>[TASK-GM-19] 오늘 상대 타자 수 · 피안타(대시보드 오늘 피안타율).</summary>
            public (int bf, int hits, int walks) TodayFaced(Player p) => p != null && pit.TryGetValue(p, out var l) ? (l.BF, l.H, l.BB) : (0, 0, 0);

            /// <summary>오늘 경기 타자 라인(타수-안타) 한 줄.</summary>
            public string TodayBatting(Player p) => p != null && bat.TryGetValue(p, out var l) ? $"오늘 {l.AB}타수 {l.H}안타{(l.HR > 0 ? $" {l.HR}홈런" : "")}{(l.RBI > 0 ? $" {l.RBI}타점" : "")}" : "오늘 첫 타석";
            public string TodayPitching(Player p) => p != null && pit.TryGetValue(p, out var l) ? $"오늘 {l.Outs / 3}{(l.Outs % 3 > 0 ? $" {l.Outs % 3}/3" : "")}이닝 투구수 {l.NP} · {l.SO}K {l.R}실점" : "오늘 첫 등판";

            /// <summary>ABS 탄착군 - 마지막 타석의 투구 위치(존 기준 -1~1 정규화)와 판정(true = 스트라이크). 결과 · 투수 ABS 기량으로 결정적으로 만든다.</summary>
            public List<(float x, float y, bool strike)> LastPitchLocations()
            {
                var list = new List<(float, float, bool)>();
                var s = LastStep;
                if (s == null || s.Pitcher == null) return list;
                var rnd = new Random(s.PlateAppearance * 7919 + (s.Pitcher.InstanceId ?? "").Length * 131 + GameIndex);
                int pitches = s.Result == AtBatResult.Strikeout ? 3 + rnd.Next(0, 4) : s.Result == AtBatResult.Walk ? 4 + rnd.Next(0, 3) : 1 + rnd.Next(0, 4);
                int walkBalls = s.Result == AtBatResult.Walk ? 4 : 0, balls = 0;
                float control = Math.Max(0.15f, (100 - s.Pitcher.ABSZoneSkill) / 100f);
                for (int i = 0; i < pitches; i++)
                {
                    bool last = i == pitches - 1;
                    bool strike = last ? s.Result != AtBatResult.Walk : (walkBalls > 0 && balls < 3 && rnd.NextDouble() < 0.6) ? false : rnd.NextDouble() < 0.55;
                    if (!strike) balls++;
                    float spread = strike ? 0.8f : 1.0f + control;
                    float x = (float)(rnd.NextDouble() * 2 - 1) * spread, y = (float)(rnd.NextDouble() * 2 - 1) * spread;
                    if (!strike && Math.Abs(x) <= 1f && Math.Abs(y) <= 1f) { if (Math.Abs(x) > Math.Abs(y)) x = Math.Sign(x == 0 ? 1 : x) * (1.08f + (float)rnd.NextDouble() * 0.3f); else y = Math.Sign(y == 0 ? 1 : y) * (1.08f + (float)rnd.NextDouble() * 0.3f); }
                    list.Add((Math.Max(-1.5f, Math.Min(1.5f, x)), Math.Max(-1.5f, Math.Min(1.5f, y)), strike));
                }
                return list;
            }

            // ================================================================== 마무리

            /// <summary>경기를 끝까지 진행한 뒤 기록을 반영하고(시즌 경기), 내 구단 경기면 박스스코어를 돌려준다.</summary>
            public GMMatchBoxScoreData Finish()
            {
                if (finished) return box;
                PlayToEnd();
                if (IsPostSeason && !Engine.IsGameOver) Engine.DebugForceEndGame(Home.TeamCode); // [TASK-GM-14] 타석 상한에 걸려도 포스트시즌은 무승부 없음(홈 끝내기)
                finished = true;
                var result = Engine.Result;
                int homeRuns = result.HomeTotalScore, awayRuns = result.AwayTotalScore;
                foreach (var list in stints) if (list.Value.Count > 0)
                    list.Value[list.Value.Count - 1].ExitLead = list.Key == Home.TeamCode ? homeRuns - awayRuns : awayRuns - homeRuns;
                if (wpa.Count > 1) wpa[wpa.Count - 1].HomeWinProbability = homeRuns > awayRuns ? 1f : homeRuns < awayRuns ? 0f : 0.5f;
                string winnerCode = homeRuns > awayRuns ? Home.TeamCode : awayRuns > homeRuns ? Away.TeamCode : null;

                // 투수 결정(승 · 패 · 세이브 · 홀드)
                Player winP = null, loseP = null, saveP = null;
                if (winnerCode != null)
                {
                    bool homeWon = winnerCode == Home.TeamCode;
                    var winStints = stints[winnerCode];
                    winP = homeWon ? winCandHome : winCandAway;
                    loseP = homeWon ? loseCandAway : loseCandHome;
                    if (winP == null && winStints.Count > 0) winP = winStints[0].Pitcher;
                    if (winStints.Count > 1 && winP == winStints[0].Pitcher && pit.TryGetValue(winP, out var starterLine) && starterLine.Outs < 15)
                        winP = winStints[1].Pitcher; // 선발 5이닝 미만 - 첫 구원 투수에게 승리
                    if (loseP == null)
                    {
                        var lost = stints[homeWon ? Away.TeamCode : Home.TeamCode];
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
                        if (s.EntryLead > 0 && s.EntryLead <= 3 && s.ExitLead > 0 && !holds.Contains(s.Pitcher)) holds.Add(s.Pitcher);
                    }
                }
                LosingPitcher = loseP;

                int homeGate = 0;
                // [TASK-GM-14] 정규시즌 반영 가드 - 진행 중인 경기일 · 팀당 하루 1경기 · 144경기 상한(포스트시즌 · 대회 · 중복 세션은 순위표에 들어가지 않는다)
                bool record = recordSeason && sim.CanRecordSeasonGame(day, Home, Away);
                SeasonRecorded = record;
                if (record) homeGate = ApplySeason(homeRuns, awayRuns, winnerCode, winP, loseP, saveP, holds);

                if (Home.IsUserTeam || Away.IsUserTeam)
                {
                    var me = Home.IsUserTeam ? Home : Away;
                    var opp = Home.IsUserTeam ? Away : Home;
                    int my = me == Home ? homeRuns : awayRuns, their = me == Home ? awayRuns : homeRuns;
                    string outcome = my > their ? "승" : my < their ? "패" : "무";
                    sim.LastUserGameLine = $"{(IsPostSeason ? GameTitle : $"G{day + 1}")} {CompyaShort(me.Team)} {my} : {their} {CompyaShort(opp.Team)} ({outcome})";
                    if (record)
                    {
                        league.UserResults.RemoveAll(x => x.Day == day);
                        league.UserResults.Add(new GMGameResultEntry { Day = day, OpponentCode = opp.TeamCode, Home = me == Home, My = my, Their = their });
                    }
                    box = BuildBox(homeRuns, awayRuns, winnerCode, winP, loseP, saveP, holds, me, homeGate);
                    league.AddBoxScore(box);
                    if (sim.RecordAllBoxScores && record) sim.AllUserBoxScores.Add(box);
                }

                if (record)
                {
                    sim.RollInjury(day, Home, bat, pit);
                    sim.RollInjury(day, Away, bat, pit);
                }
                else if (savedState != null)
                {
                    foreach (var (p, stamina, condition) in savedState) { p.CurrentStamina = stamina; p.CurrentCondition = condition; }
                }
                return box;
            }

            /// <summary>[TASK-GM-14] 이 경기가 정규시즌 순위 · 기록에 반영됐는지(가드 통과 여부).</summary>
            public bool SeasonRecorded { get; private set; }

            /// <summary>마지막 경기의 패전 투수(포스트시즌 단판 결과용).</summary>
            public Player LosingPitcher { get; private set; }

            /// <summary>포스트시즌 MVP 집계용 - 기존 단판 경기 결과 형식으로 옮긴다.</summary>
            public GMAwardEvaluator.ExhibitionResult ToExhibitionResult()
            {
                var r = new GMAwardEvaluator.ExhibitionResult { HomeRuns = HomeScore, AwayRuns = AwayScore, LosingPitcher = LosingPitcher };
                foreach (var pair in bat)
                    r.Batting[pair.Key] = new GMAwardEvaluator.ExhibitionBatLine { PA = pair.Value.PA, AB = pair.Value.AB, H = pair.Value.H, HR = pair.Value.HR, RBI = pair.Value.RBI, R = pair.Value.R, BB = pair.Value.BB, SO = pair.Value.SO };
                foreach (var pair in pit)
                    r.Pitching[pair.Key] = new GMAwardEvaluator.ExhibitionPitchLine { Outs = pair.Value.Outs, R = pair.Value.R, H = pair.Value.H, BB = pair.Value.BB, SO = pair.Value.SO };
                return r;
            }

            /// <summary>시즌 경기 반영 - 흥행 · 순위 · 선수 누적 · 연속 기록 · 경기 이벤트 · 개인 수비. 홈 흥행 수익을 돌려준다.</summary>
            private int ApplySeason(int homeRuns, int awayRuns, string winnerCode, Player winP, Player loseP, Player saveP, List<Player> holds)
            {
                // [TASK-GM-05] 홈 흥행력 → 관중 수익(예산) · 팬 지지율
                int homeGate = GMCheerleaderRoster.ApplyHomeGate(Home);

                var hr = league.RecordOf(Home.TeamCode);
                var ar = league.RecordOf(Away.TeamCode);
                hr.G++; ar.G++;
                hr.AbsBorderlineCalls += homeAbsCalls; ar.AbsBorderlineCalls += awayAbsCalls; // [TASK-GM-06]
                hr.AbsLookingStrikeouts += homeLookingK; ar.AbsLookingStrikeouts += awayLookingK;
                hr.AbsBlockSaves += homeBlocks; ar.AbsBlockSaves += awayBlocks;
                hr.AbsWalksDrawn += homeAbsWalks; ar.AbsWalksDrawn += awayAbsWalks;
                hr.RunsScored += homeRuns; hr.RunsAllowed += awayRuns;
                ar.RunsScored += awayRuns; ar.RunsAllowed += homeRuns;
                if (winnerCode == null) { hr.D++; ar.D++; hr.Streak = 0; ar.Streak = 0; hr.PushRecent('D'); ar.PushRecent('D'); }
                else
                {
                    var w = winnerCode == Home.TeamCode ? hr : ar;
                    var l = winnerCode == Home.TeamCode ? ar : hr;
                    w.W++; l.L++;
                    w.Streak = w.Streak > 0 ? w.Streak + 1 : 1;
                    l.Streak = l.Streak < 0 ? l.Streak - 1 : -1;
                    w.PushRecent('W'); l.PushRecent('L');
                }

                // 선수 누적 · 연속 기록 · 경기 이벤트
                int homeTeamHrBefore = hr.TeamHomeRuns, awayTeamHrBefore = ar.TeamHomeRuns;
                foreach (var pair in bat)
                {
                    var player = pair.Key; var line = pair.Value;
                    string code = Home.Roster.Contains(player) ? Home.TeamCode : Away.TeamCode;
                    var st = league.StatsOf(player, code);
                    st.G++; st.PA += line.PA; st.AB += line.AB; st.H += line.H; st.Doubles += line.D2; st.Triples += line.D3; st.HR += line.HR;
                    st.RBI += line.RBI; st.R += line.R; st.BB += line.BB; st.SO += line.SO; st.SB += line.SB;
                    st.CurrentHitStreak = line.H > 0 ? st.CurrentHitStreak + 1 : 0;
                    st.MaxHitStreak = Math.Max(st.MaxHitStreak, st.CurrentHitStreak);
                    st.CurrentHrStreak = line.HR > 0 ? st.CurrentHrStreak + 1 : 0;
                    league.RecordOf(code).TeamHomeRuns += line.HR;
                    sim.weeklyHits.TryGetValue(player.InstanceId, out int wk);
                    sim.weeklyHits[player.InstanceId] = wk + line.H;
                    sim.CheckBatterEvents(day, player, code, line, st);
                    if ((line.H > 0 || line.HR > 0) && GMCareerTimeline.TracksDebut(player)) // [TASK-GM-19] 게임 안 데뷔 선수의 첫 안타 · 홈런
                        GMCareerTimeline.CheckDebut(league, player, code, DateLabel, code == Home.TeamCode ? Away.TeamCode : Home.TeamCode, line.H, line.HR, false);
                }
                foreach (var pair in pit)
                {
                    var player = pair.Key; var line = pair.Value;
                    string code = Home.Roster.Contains(player) ? Home.TeamCode : Away.TeamCode;
                    var st = league.StatsOf(player, code);
                    st.PG++; st.OutsPitched += line.Outs; st.ER += line.ER; st.PSO += line.SO; st.PBB += line.BB; st.HA += line.H; st.HRA += line.HR;
                    var own = stints[code];
                    if (own.Count > 0 && own[0].Pitcher == player) st.GS++;
                    if (player == winP) st.W++;
                    if (player == loseP) st.L++;
                    if (player == saveP) st.SV++;
                    if (holds.Contains(player)) st.HLD++;
                    bool complete = own.Count == 1 && own[0].Pitcher == player;
                    int allowed = code == Home.TeamCode ? awayRuns : homeRuns;
                    sim.CheckPitcherEvents(day, player, code, line, complete, allowed);
                    if (player == winP && GMCareerTimeline.TracksDebut(player)) // [TASK-GM-19] 데뷔 첫 승리
                        GMCareerTimeline.CheckDebut(league, player, code, DateLabel, code == Home.TeamCode ? Away.TeamCode : Home.TeamCode, 0, 0, true);
                }
                // [TASK-GM-04] 개인 수비 누적(수비 출전 · 포지션별 출전 · 실책 · 처리 기회 · 호수비)
                foreach (var (team, field) in new[] { (Home, homeField), (Away, awayField) })
                {
                    foreach (var pair in field)
                    {
                        var st = league.StatsOf(pair.Value, team.TeamCode);
                        st.Positions[pair.Key]++;
                        if (pair.Key != GMPlayerSeasonStats.DesignatedHitterSlot) st.DefG++;
                    }
                }
                foreach (var player in pit.Keys)
                {
                    var st = league.StatsOf(player, Home.Roster.Contains(player) ? Home.TeamCode : Away.TeamCode);
                    st.Positions[GMPlayerSeasonStats.PitcherSlot]++;
                    st.DefG++;
                }
                foreach (var pair in fielding)
                {
                    var st = league.StatsOf(pair.Key, Home.Roster.Contains(pair.Key) ? Home.TeamCode : Away.TeamCode);
                    st.Chances += pair.Value.Chances;
                    st.Errors += pair.Value.Errors;
                    st.FinePlays += pair.Value.Fine;
                }

                sim.CheckTeamHomeRunMilestone(day, Home.TeamCode, homeTeamHrBefore, hr.TeamHomeRuns);
                sim.CheckTeamHomeRunMilestone(day, Away.TeamCode, awayTeamHrBefore, ar.TeamHomeRuns);
                return homeGate;
            }

            /// <summary>시즌 누적 칸 - 시즌 경기는 반영된 값, 포스트시즌은 정규시즌 값을 그대로 보여 준다(새 기록 항목을 만들지 않는다).</summary>
            private GMPlayerSeasonStats SeasonOf(Player p, string code)
            {
                if (recordSeason) return league.StatsOf(p, code);
                return league.Stats.TryGetValue(p.InstanceId, out var st) ? st : new GMPlayerSeasonStats { PlayerId = p.InstanceId, TeamCode = code, IsPitcher = p.IsPitcher };
            }

            private GMMatchBoxScoreData BuildBox(int homeRuns, int awayRuns, string winnerCode, Player winP, Player loseP, Player saveP, List<Player> holds, GMTeamState me, int homeGate)
            {
                var b = new GMMatchBoxScoreData
                {
                    GameIndex = GameIndex,
                    GameTitle = GameTitle ?? "",
                    SeasonYear = league.SeasonYear,
                    DateLabel = DateLabel,
                    Stadium = KBOManager.UI.CompyaUiKit.Stadium(Home.Team),
                    HomeCode = Home.TeamCode,
                    AwayCode = Away.TeamCode,
                    WinnerCode = winnerCode ?? "",
                    HomeR = homeRuns, AwayR = awayRuns,
                    HomeE = homeE, AwayE = awayE,
                    WpaPoints = wpa,
                    HomeAbsCalls = homeAbsCalls, AwayAbsCalls = awayAbsCalls, HomeBlockSaves = homeBlocks, AwayBlockSaves = awayBlocks,
                    HomeLookingK = homeLookingK, AwayLookingK = awayLookingK,
                    HomeAbsIndex = AbsIndexOf(homeOrder, pitchOrder[Home.TeamCode]), AwayAbsIndex = AbsIndexOf(awayOrder, pitchOrder[Away.TeamCode]),
                };
                int innings = Math.Max(9, inningRuns.Keys.Select(k => k.Item1).DefaultIfEmpty(9).Max());
                for (int i = 1; i <= innings; i++)
                {
                    b.AwayInningRuns.Add(inningRuns.TryGetValue((i, true), out int a) ? a : 0);
                    b.HomeInningRuns.Add(inningRuns.TryGetValue((i, false), out int h) ? h : -1); // 말 공격 없음 = 'X'
                }
                foreach (var (team, order, isHome) in new[] { (Home, homeOrder, true), (Away, awayOrder, false) })
                {
                    var positions = LineupAssignment.AssignStarters(team == Home ? homeRoster : awayRoster, team.Lineup)
                        .Where(s => s.Player != null).ToDictionary(s => s.Player, s => s.Position);
                    var batters = b.BattersOf(isHome);
                    var lineup = order.Concat(bat.Keys.Where(p => team.Roster.Contains(p) && !order.Contains(p))).ToList();
                    for (int i = 0; i < lineup.Count; i++)
                    {
                        var p = lineup[i];
                        if (!bat.TryGetValue(p, out var l)) continue;
                        var st = SeasonOf(p, team.TeamCode);
                        batters.Add(new BatterBoxScoreLine
                        {
                            PlayerId = p.InstanceId, Name = p.Template.PlayerName, Order = i + 1,
                            Position = PositionShort(positions.TryGetValue(p, out var pos) ? pos : p.Template.BatterPosition),
                            AB = l.AB, R = l.R, H = l.H, RBI = l.RBI, BB = l.BB, SO = l.SO, Doubles = l.D2, Triples = l.D3, HR = l.HR, SB = l.SB, GIDP = l.GIDP,
                            SeasonAVG = st.AVG, SeasonHR = st.HR, SeasonRBI = st.RBI, SeasonDoubles = st.Doubles, SeasonTriples = st.Triples, SeasonSB = st.SB,
                        });
                    }
                    var pitchers = b.PitchersOf(isHome);
                    foreach (var p in pitchOrder[team.TeamCode])
                    {
                        var l = pit[p];
                        var st = SeasonOf(p, team.TeamCode);
                        pitchers.Add(new PitcherBoxScoreLine
                        {
                            PlayerId = p.InstanceId, Name = p.Template.PlayerName,
                            Decision = p == winP ? "W" : p == loseP ? "L" : p == saveP ? "S" : holds.Contains(p) ? "H" : "",
                            Outs = l.Outs, H = l.H, R = l.R, ER = l.ER, BB = l.BB, SO = l.SO, HR = l.HR, NP = Math.Max(1, l.NP),
                            SeasonERA = st.ERA, SeasonW = st.W, SeasonL = st.L, SeasonSV = st.SV, SeasonHLD = st.HLD,
                        });
                    }
                }
                b.HomeH = b.HomeBatters.Sum(x => x.H);
                b.AwayH = b.AwayBatters.Sum(x => x.H);
                b.KeyPlays = plays.OrderByDescending(p => Math.Abs(p.Delta)).Take(3).Select(p => new WPAKeyPlay
                {
                    PlateAppearance = p.Pa, Inning = p.Inning, IsTop = p.Top, TeamCode = p.TeamCode, BatterName = p.Batter.Template.PlayerName,
                    Situation = SituationLabel(p), ResultLabel = ResultLabel(p), RBI = p.Error ? 0 : p.Runs, DeltaWPA = p.Delta,
                }).ToList();
                b.Recap = sim.WriteRecap(day, b, plays, IsPostSeason ? GameTitle : null);
                // [TASK-GM-05] 오늘의 응원단 단상 활약
                b.CheerEntryNames = me.CheerEntry.Select(c => c.DisplayName).ToList();
                b.CheerSummary = GMCheerleaderRoster.MatchSummary(me, me == Home, me == Home ? homeGate : 0);
                if (recordSeason && me == Home && homeGate > 0)
                    sim.AddNews(day, GMNewsKind.Cheer, $"응원단 {b.CheerEntryNames.Count}인 단상 응원", b.CheerSummary, true, false);
                return b;
            }
        }

        public static string MatchTacticLabel(MatchTactic t)
        {
            switch (t)
            {
                case MatchTactic.PowerSwing: return "강공";
                case MatchTactic.ContactSwing: return "컨택";
                case MatchTactic.Bunt: return "번트";
                case MatchTactic.Steal: return "도루";
                case MatchTactic.FullForce: return "정면 승부";
                case MatchTactic.PitchingChange: return "투수 교체";
                case MatchTactic.IntentionalWalk: return "고의사구";
                default: return "없음";
            }
        }
    }
}
