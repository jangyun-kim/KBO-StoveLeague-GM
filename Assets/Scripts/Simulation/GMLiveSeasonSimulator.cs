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
    ///   - [TASK-GM-04] 개인 수비(실책 · 처리 기회 · 호수비 · 포지션별 출전) 집계, 24경기마다 월간 시상(1.7), 72경기 직후 올스타전(1.6) - GMAwardEvaluator
    /// </summary>
    public partial class GMLiveSeasonSimulator
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
        private readonly Random defenseRandom; // [TASK-GM-04] 수비 기록 전용(경기 결과 난수열과 분리)
        private readonly List<(string home, string away)>[] schedule;
        private readonly Queue<GMSimInterrupt> interrupts = new Queue<GMSimInterrupt>();
        private readonly Dictionary<string, int> weeklyHits = new Dictionary<string, int>();

        public GMLeagueState League => league;
        private GMRunMode? activeMode;
        /// <summary>진행 방식. [TASK-GM-14] 전반기 · 후반기 · 한 시즌 고속 진행 중에는 GMAudioManager.SimulationMode를 켜 이벤트 BGM 하이재킹을 막는다.</summary>
        public GMRunMode? ActiveMode
        {
            get => activeMode;
            private set { activeMode = value; KBOManager.Managers.GMAudioManager.SimulationMode = IsFastRun(value); }
        }

        /// <summary>[TASK-GM-14] 고속 시뮬레이션(전반기 · 후반기 · 한 시즌) 여부 - 한 경기는 아니다.</summary>
        public static bool IsFastRun(GMRunMode? mode) => mode.HasValue && mode.Value != GMRunMode.SingleGame;
        public int TargetGames { get; private set; }
        public int GamesPlayed => league.GamesPlayed;
        public bool IsSeasonComplete => league.GamesPlayed >= SeasonGames;
        public bool IsFirstHalfDone => league.GamesPlayed >= HalfGames;
        public GMSimInterrupt PendingInterrupt => interrupts.Count > 0 ? interrupts.Peek() : null;
        public bool IsRunning => ActiveMode.HasValue && league.GamesPlayed < TargetGames && PendingInterrupt == null;
        /// <summary>[TASK-GM-18] 이번 주 남은 경기 수(주간 진행 버튼 라벨).</summary>
        public int GamesLeftThisWeek => Math.Max(0, GMSeasonEvents.NextWeekEnd(league.GamesPlayed) - league.GamesPlayed);
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
            defenseRandom = new Random(league.Seed * 31 + league.SeasonYear + league.GamesPlayed * 17);
            if (league.Awards == null || league.Awards.SeasonYear != league.SeasonYear && !league.Awards.HasAny)
                league.Awards = new SeasonAwardCeremonyBundle { SeasonYear = league.SeasonYear };
            schedule = BuildSchedule(NameAliasTable.CanonicalTeamCodes);
            foreach (var code in NameAliasTable.CanonicalTeamCodes) league.RecordOf(code);
        }

        // ================================================================== 일정

        /// <summary>
        /// [TASK-GM-14] KBO 3연전 일정 - 원형 대진 9라운드를 시리즈 단위로 묶는다(하루 5경기 · 144일 · 상대별 16경기 = 홈 8 · 원정 8).
        ///   사이클 0~3 = 3연전(라운드마다 3일, 홀수 사이클은 홈/원정 반전) · 사이클 4~5 = 2연전(같은 규칙) → 상대별 3+3+3+3+2+2 = 16.
        ///   배치: 개막 주말 2연전(토~일) → 3연전 36개(화~목 · 금~일, 108일) → 2연전 17개(화~수 · 목~금 · 토~일, 시즌 막판). 월요일 휴식.
        ///   같은 상대와 시리즈가 연달아 붙지 않도록 3연전은 라운드 1부터 돈다.
        /// </summary>
        public static List<(string home, string away)>[] BuildSchedule(IReadOnlyList<string> codes)
        {
            var rounds = CircleRounds(codes);
            int n = rounds.Count; // 9
            var blocks = new List<(int len, int cycle, int round)> { (2, 4, 0) };
            for (int c = 0; c < 4; c++) for (int k = 0; k < n; k++) blocks.Add((3, c, (k + 1) % n));
            for (int k = 1; k < n; k++) blocks.Add((2, 4, k));
            for (int k = 0; k < n; k++) blocks.Add((2, 5, k));
            var result = new List<(string, string)>[SeasonGames];
            int d = 0;
            foreach (var (len, cycle, round) in blocks)
            {
                var day = rounds[round].Select(m => cycle % 2 == 0 ? m : (m.Item2, m.Item1)).ToList();
                for (int g = 0; g < len && d < SeasonGames; g++) result[d++] = day;
            }
            return result;
        }

        private static readonly (int length, int game)[] seriesTable = BuildSeriesTable();

        private static (int, int)[] BuildSeriesTable()
        {
            var t = new (int, int)[SeasonGames];
            int d = 0;
            void Add(int len) { for (int g = 1; g <= len && d < SeasonGames; g++) t[d++] = (len, g); }
            Add(2);
            for (int i = 0; i < 36; i++) Add(3);
            while (d < SeasonGames) Add(2);
            return t;
        }

        /// <summary>[TASK-GM-14] 그날 경기의 시리즈 정보 - (2연전/3연전, 몇 차전).</summary>
        public static (int length, int game) SeriesOf(int dayIndex) => seriesTable[Math.Max(0, Math.Min(SeasonGames - 1, dayIndex))];

        public static string SeriesLabel(int dayIndex) { var s = SeriesOf(dayIndex); return $"{s.length}연전 {s.game}차전"; }

        /// <summary>원형(circle) 대진 9라운드(하루 5경기) - 짝수 라운드는 정방향, 홀수 라운드는 홈/원정 반전.</summary>
        private static List<List<(string, string)>> CircleRounds(IReadOnlyList<string> codes)
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
            return rounds;
        }

        public SkillDB SkillDB => skillDB;
        public EngineConfig Config => config;
        /// <summary>[TASK-GM-04] 시뮬레이터 외부(올스타 · 포스트시즌)에서 쓰는 재현 가능한 난수 시드.</summary>
        public int NextSeed() => random.Next();

        public IReadOnlyList<(string home, string away)> MatchesOn(int dayIndex) => schedule[Math.Max(0, Math.Min(SeasonGames - 1, dayIndex))];

        /// <summary>경기 일자 - [TASK-GM-14] 토요일 개막(3/28) 2연전 후 화~일 6경기 · 월요일 휴식. 연도를 생략하면 2026시즌.</summary>
        public static DateTime DateOf(int dayIndex) => OpeningDay.AddDays(DayOffset(dayIndex));
        public static string DateLabel(int dayIndex) => DateOf(dayIndex).ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        /// <summary>[TASK-GM-04] 시즌 연도별 개막일(3월 28일) 기준 경기 일자 - 2027시즌부터 날짜 연도가 따라 바뀐다.</summary>
        public static DateTime DateOf(int dayIndex, int year) => new DateTime(year, OpeningDay.Month, OpeningDay.Day).AddDays(DayOffset(dayIndex));
        /// <summary>[TASK-GM-14] 개막일부터 지난 날 수 - 개막 주말(토 · 일) 다음부터 매주 월요일을 건너뛴다.</summary>
        public static int DayOffset(int dayIndex) => dayIndex + (dayIndex + 4) / 6;
        public static string DateLabel(int dayIndex, int year) => DateOf(dayIndex, year).ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
        public string DayLabel(int dayIndex) => DateLabel(dayIndex, league.SeasonYear);

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
                GMRunMode.Week => GMSeasonEvents.NextWeekEnd(league.GamesPlayed), // [TASK-GM-18]
                _ => SeasonGames,
            };
            ActiveMode = mode;
            if (league.Phase == GMSeasonPhase.StoveLeague && league.GamesPlayed == 0)
            {
                // [TASK-GM-09] 개막 준비 - AI 구단 FA 입찰(28명 보충 · 예산 한도) · 보상 정산 → 3월 WBC(해당 연도)
                league.LastAiSignings.Clear();
                league.LastAiSignings.AddRange(GMFreeAgencyCycle.PrepareOpeningDay(league));
                GMGlobalTournamentManager.Trigger(league, GMTournamentWindow.PreSeason, league.Seed);
            }
            if (league.Phase == GMSeasonPhase.StoveLeague)
            {
                league.Phase = GMSeasonPhase.RegularSeason;
                GMLeagueRules.ApplyOpeningBudgetPenalty(league); // [TASK-GM-18] 예산 초과 상태로 개막 = 구단주 신임도 -20
            }
            if (league.GamesPlayed == 0 && league.News.All(n => n.Kind != GMNewsKind.Season || n.GameIndex != 0))
                AddNews(0, GMNewsKind.Season, $"{league.SeasonYear} KBO 리그 개막", $"{league.SeasonYear} 시즌 정규리그 144경기 대장정이 시작됩니다.", false, false);
            return true;
        }

        public void Stop() => ActiveMode = null;

        /// <summary>
        /// 인터럽트 처리 - 대기록은 Continue, 부상은 AutoCallUp(벤치 자동 대체) 또는 ManualLineup(replacement를 그 자리에 고정).
        /// [TASK-GM-18] 단장 개입 사건을 이 경로로 넘기면 기본 선택지(DefaultChoice - 경기력에 영향 없는 보수적 선택)로 처리한다.
        /// </summary>
        public void ResolveInterrupt(GMInterruptChoice choice, Player replacement = null)
        {
            if (interrupts.Count == 0) return;
            var current = interrupts.Dequeue();
            if (current.Kind == GMInterruptKind.SeasonEvent)
            {
                if (current.Event != null && !current.Event.Resolved) LastSeasonEventResult = GMSeasonEvents.Resolve(league, current.Event, current.Event.DefaultChoice);
                return;
            }
            if (current.Kind == GMInterruptKind.Injury && choice == GMInterruptChoice.ManualLineup && replacement != null && !current.Player.IsPitcher)
            {
                var team = league.Teams[current.TeamCode];
                team.Lineup.Starters.RemoveAll(pin => pin.Position == current.Position);
                team.Lineup.Starters.Add(new LineupAssignment.StarterPin { InstanceId = replacement.InstanceId, Position = current.Position });
            }
        }

        /// <summary>[TASK-GM-18] 대기 중인 단장 개입 사건(맨 앞 인터럽트가 사건이 아니면 null).</summary>
        public GMSeasonEvent PendingSeasonEvent => PendingInterrupt != null && PendingInterrupt.Kind == GMInterruptKind.SeasonEvent ? PendingInterrupt.Event : null;
        public GMSeasonEventResult LastSeasonEventResult { get; private set; }
        /// <summary>[TASK-GM-18] 이번 시즌 발생한 단장 개입 사건 수(테스트 · 결산).</summary>
        public int SeasonEventsRaised { get; private set; }

        /// <summary>[TASK-GM-18] 단장 개입 사건 선택(선택지 index). 맨 앞 인터럽트가 사건이 아니면 null.</summary>
        public GMSeasonEventResult ResolveSeasonEvent(int choiceIndex)
        {
            var ev = PendingSeasonEvent;
            if (ev == null) return null;
            interrupts.Dequeue();
            LastSeasonEventResult = GMSeasonEvents.Resolve(league, ev, choiceIndex);
            var r = LastSeasonEventResult;
            if (r != null && r.Applied)
            {
                var audio = KBOManager.Managers.GMAudioManager.Instance;
                if (audio != null)
                {
                    if (!string.IsNullOrEmpty(r.Sfx)) audio.PlaySfx(r.Sfx);
                    if (r.Audio.HasValue) audio.PlayEvent(r.Audio.Value, league.SelectedTeamCode); // 고속 진행 중이면 SimulationMode가 무시(효과음만)
                }
            }
            return r;
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
            if (IsSeasonComplete || PendingInterrupt != null || LiveSession != null) return false;
            int day = league.GamesPlayed;
            BeginDay(day);
            foreach (var (home, away) in schedule[day]) PlayGame(day, league.Teams[home], league.Teams[away]);
            CompleteDay(day);
            return true;
        }

        // ================================================================== [TASK-GM-07] 실시간 이닝 경기(LiveMatchInningView)

        /// <summary>진행 중인 내 구단 실시간 경기(없으면 null). 이 경기가 끝나 FinishLiveDay()를 부르기 전까지 하루가 넘어가지 않는다.</summary>
        public GameSession LiveSession { get; private set; }

        /// <summary>
        /// 오늘 경기를 실시간으로 시작한다 - 부상 · 체력을 하루 진행 전 상태로 맞추고, 내 구단 경기를 타석 단위 세션으로 연다.
        /// 다른 4경기는 FinishLiveDay()에서 진행한다. 시즌이 끝났거나 인터럽트가 남아 있으면 null.
        /// </summary>
        public GameSession BeginLiveDay()
        {
            if (LiveSession != null) return LiveSession;
            if (IsSeasonComplete || PendingInterrupt != null) return null;
            if (!ActiveMode.HasValue && !StartRun(GMRunMode.SingleGame)) return null;
            int day = league.GamesPlayed;
            BeginDay(day);
            var match = schedule[day].FirstOrDefault(m => m.home == league.SelectedTeamCode || m.away == league.SelectedTeamCode);
            if (match.home == null) return null;
            LiveSession = new GameSession(this, day, league.Teams[match.home], league.Teams[match.away], true, false);
            return LiveSession;
        }

        /// <summary>실시간 경기를 (남은 타석까지) 끝내고 기록을 반영한 뒤 나머지 경기와 하루 마무리를 진행한다. 내 구단 박스스코어를 돌려준다.</summary>
        public GMMatchBoxScoreData FinishLiveDay()
        {
            var session = LiveSession;
            if (session == null) return null;
            int day = league.GamesPlayed;
            var box = session.Finish();
            LiveSession = null;
            foreach (var (home, away) in schedule[day])
            {
                if (home == session.Home.TeamCode && away == session.Away.TeamCode) continue;
                PlayGame(day, league.Teams[home], league.Teams[away]);
            }
            CompleteDay(day);
            return box;
        }

        /// <summary>
        /// [TASK-GM-14] 정규시즌 반영 가드 - 오늘(GamesPlayed) 경기이고, 두 팀 모두 오늘 아직 기록이 없으며(G ≤ 경기일), 144경기를 넘지 않을 때만 순위 · 기록에 넣는다.
        /// 포스트시즌 · 글로벌 대회 · 지난 날짜로 남은 세션 · 같은 날 중복 진행은 모두 걸러진다.
        /// </summary>
        internal bool CanRecordSeasonGame(int day, GMTeamState home, GMTeamState away)
        {
            if (home == null || away == null || day < 0 || day >= SeasonGames || day != league.GamesPlayed || league.Phase == GMSeasonPhase.PostSeason) return false;
            var hr = league.RecordOf(home.TeamCode);
            var ar = league.RecordOf(away.TeamCode);
            return hr.G <= day && ar.G <= day && hr.G < SeasonGames && ar.G < SeasonGames;
        }

        /// <summary>[TASK-GM-07] 포스트시즌 단판 세션(시즌 누적 · 순위 미반영, 체력 · 컨디션 복원).</summary>
        public GameSession CreateExhibitionSession(GMTeamState home, GMTeamState away, int seed, Player homeStarter, Player awayStarter, int gameIndex, string dateLabel, string title)
        {
            return new GameSession(this, SeasonGames - 1, home, away, false, true, seed, homeStarter, awayStarter, gameIndex, dateLabel) { GameTitle = title ?? "" };
        }

        private void BeginDay(int day)
        {
            TickInjuries(day);
            foreach (var team in league.Teams.Values) RecoverStamina(team.Roster);
        }

        private void CompleteDay(int day)
        {
            foreach (var team in league.Teams.Values) TeamChemistryEngine.ApplyMatchChemistryTick(team.AvailableRoster);
            foreach (var team in league.Teams.Values) GMCheerleaderRoster.TickDay(team); // [TASK-GM-05] 단상 체력 소모 · 벤치 회복 · 자동 로테이션
            league.GamesPlayed++;
            // [TASK-GM-09] 9월 진입 - 아시안게임(해당 연도) 리그 중단 · 국가대표 차출
            if (GMGlobalTournamentManager.EntersSeptember(league.SeasonYear, league.GamesPlayed))
            {
                var ag = GMGlobalTournamentManager.Trigger(league, GMTournamentWindow.MidSeason, league.Seed);
                if (ag != null) AddNews(day, GMNewsKind.Season, $"{ag.Name} 정규시즌 중단", $"국가대표 {ag.RosterIds.Count}명 차출로 리그가 잠시 멈춥니다. {ag.Summary}", ag.RosterIds.Any(id => league.UserTeam?.Roster.Any(p => p.InstanceId == id) == true), true);
            }
            UpdateLeagueEnvironment();
            UpdateWar();
            PeriodicNews(day);
            // [TASK-GM-04] 월말(24경기 단위) 월간 시상 → 전반기 종료 직후 7월 올스타전
            if (league.GamesPlayed % GMAwardEvaluator.MonthGames == 0) GMAwardEvaluator.EvaluateMonthlyAwards(this, day);
            if (league.GamesPlayed == HalfGames) GMAwardEvaluator.HoldAllStarGame(this, day);

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
            // [TASK-GM-18] 주간 종료 → 단장 개입 사건 0~3건(경기 중간이 아닌 주간 마감 후에만 시뮬레이션을 멈춘다)
            if (!IsSeasonComplete && GMSeasonEvents.IsWeekEnd(league.GamesPlayed))
            {
                GMSeasonEvents.OnWeekEnd(league);
                foreach (var ev in GMSeasonEvents.Generate(this))
                {
                    SeasonEventsRaised++;
                    interrupts.Enqueue(new GMSimInterrupt { Kind = GMInterruptKind.SeasonEvent, Event = ev, News = league.News.FirstOrDefault(n => n.Kind == GMNewsKind.Decision) });
                }
            }
            if (ActiveMode.HasValue && league.GamesPlayed >= TargetGames) ActiveMode = null;
            OnDayCompleted?.Invoke();
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

        private sealed class BatLine { public int PA, AB, H, D2, D3, HR, RBI, R, BB, SO, SB, GIDP; }
        private sealed class PitchLine { public int Outs, R, ER, H, BB, SO, HR, BF, NP; }
        private sealed class Stint { public Player Pitcher; public int EntryLead; public int ExitLead; }
        private sealed class FieldLine { public int Chances, Errors, Fine; }
        private sealed class PaRecord { public int Pa, Inning, Outs; public bool Top; public Player Batter; public string TeamCode; public AtBatResult Result; public bool Error, Gidp; public int Runs; public bool R1, R2, R3; public float Delta; }

        /// <summary>
        /// [TASK-GM-03] 홈 팀 승리 확률(0~1) - 점수 차 + 주자 기대 득점을 남은 아웃 수 기반 표준편차로 나눈 로지스틱 근사.
        /// 경기 전(1회초 무사 0:0) = 0.5, 남은 아웃이 없으면 리드 팀 1 / 0, 동점 0.5.
        /// </summary>
        public static float HomeWinProbability(int inning, bool top, int outs, int homeScore, int awayScore, bool r1 = false, bool r2 = false, bool r3 = false)
        {
            outs = Math.Max(0, Math.Min(3, outs));
            int remaining = inning <= 9
                ? Math.Max(0, 54 - ((inning - 1) * 6 + (top ? 0 : 3) + outs))
                : (top ? 6 - outs : 3 - outs);
            double lead = homeScore - awayScore;
            double runners = ((r1 ? 0.4 : 0) + (r2 ? 0.6 : 0) + (r3 ? 0.85 : 0)) * (3 - outs) / 3.0;
            lead += top ? -runners : runners;
            if (remaining <= 0) return lead > 0 ? 1f : lead < 0 ? 0f : 0.5f;
            double sd = Math.Sqrt(remaining / 3.0 * 0.95 + 0.15);
            return (float)(1.0 / (1.0 + Math.Exp(-1.7 * lead / sd)));
        }

        /// <summary>[TASK-GM-03] 테스트/검증용 - true면 내 구단 전 경기 박스스코어를 AllUserBoxScores에 모은다(세이브에는 최근 10경기만).</summary>
        public bool RecordAllBoxScores { get; set; }
        public readonly List<GMMatchBoxScoreData> AllUserBoxScores = new List<GMMatchBoxScoreData>();

        /// <summary>[TASK-GM-07] 한 경기 = 타석 세션을 끝까지 진행 + 기록 반영(GMGameSession.cs).</summary>
        private void PlayGame(int day, GMTeamState home, GMTeamState away)
        {
            var session = new GameSession(this, day, home, away, true, false);
            session.PlayToEnd();
            session.Finish();
        }
        // ================================================================== [TASK-GM-06] ABS(자동 투구 판정) 기록

        /// <summary>타석 1건의 ABS 기록 - 보더라인 스트라이크 콜(0~2) · 루킹 삼진(삼진 중 존 판정) · 포수 블로킹 세이브(주자 있을 때).
        /// 확률은 투수 · 타자 · 포수 ABSZoneSkill 차이로 정해지고, 경기 결과 난수열과 분리된 수비 난수열을 쓴다.</summary>
        private (int calls, int lookingK, int block) RollAbsEvents(AtBatStepResult step, bool runnersOn, int catcherAbs)
        {
            float pAbs = (step.Pitcher.ABSZoneSkill - 50) / 50f, bAbs = (step.Batter.ABSZoneSkill - 50) / 50f, cAbs = (catcherAbs - 50) / 50f;
            double borderline = Math.Max(0.05, Math.Min(0.85, AbsBorderlineBase + 0.32 * pAbs - 0.14 * bAbs));
            int calls = 0;
            if (defenseRandom.NextDouble() < borderline) calls++;
            if (step.Result == AtBatResult.Strikeout && defenseRandom.NextDouble() < borderline) calls++;
            int looking = 0;
            if (step.Result == AtBatResult.Strikeout && !step.IsError && defenseRandom.NextDouble() < Math.Max(0.05, Math.Min(0.6, AbsLookingBase + 0.18 * pAbs - 0.08 * bAbs))) looking = 1;
            int block = runnersOn && defenseRandom.NextDouble() < Math.Max(0.0, Math.Min(0.2, AbsBlockBase + 0.05 * cAbs)) ? 1 : 0;
            return (calls, looking, block);
        }

        public const double AbsBorderlineBase = 0.34, AbsLookingBase = 0.24, AbsBlockBase = 0.045;

        /// <summary>주전 포수(라인업 포수 자리, 없으면 포수 포지션 최고 OVR)의 ABS 블로킹 가치. 없으면 50.</summary>
        public static int CatcherAbsSkill(List<Player> roster, LineupAssignment lineup)
        {
            // [TASK-GM-07] 포수 자리가 비면(부상 등) FirstOrDefault가 null - 예전에는 .Player에서 NullReferenceException
            var catcher = LineupAssignment.AssignStarters(roster, lineup ?? new LineupAssignment()).FirstOrDefault(s => s != null && s.Player != null && s.Position == BatterPosition.Catcher)?.Player
                          ?? roster.Where(p => !p.IsPitcher && p.Template.BatterPosition == BatterPosition.Catcher).OrderByDescending(p => p.BaseOverall).FirstOrDefault();
            return catcher != null ? catcher.ABSZoneSkill : 50;
        }

        /// <summary>ABS 존 적응 지수 - 그 경기 출전 타자 · 등판 투수 ABSZoneSkill 평균(1~99).</summary>
        public static int AbsIndexOf(IEnumerable<Player> batters, IEnumerable<Player> pitchers)
        {
            var all = (batters ?? Enumerable.Empty<Player>()).Concat(pitchers ?? Enumerable.Empty<Player>()).Where(p => p?.Template != null).ToList();
            return all.Count == 0 ? 50 : (int)Math.Round(all.Average(p => p.ABSZoneSkill));
        }

        // ================================================================== [TASK-GM-04] 개인 수비 기록

        // 처리 위치 가중치(인덱스 0~7 = 포수~우익수, 8 = 투수). 땅볼은 내야, 뜬공은 외야 중심, 실책은 유격 · 3루 · 2루 순.
        private static readonly int[] GroundWeights = { 2, 14, 22, 18, 24, 0, 0, 0, 8 };
        private static readonly int[] FlyWeights = { 3, 3, 4, 4, 5, 25, 30, 26, 0 };
        private static readonly int[] ErrorWeights = { 5, 9, 16, 20, 24, 6, 6, 8, 6 };

        /// <summary>구단 주전 수비 위치(지명타자 포함 9칸, 투수 제외) - 경기 엔진과 같은 LineupAssignment 규칙.</summary>
        private static Dictionary<int, Player> FieldersOf(List<Player> roster, LineupAssignment lineup)
        {
            var map = new Dictionary<int, Player>();
            foreach (var slot in LineupAssignment.AssignStarters(roster, lineup ?? new LineupAssignment()))
                if (slot.Player != null) map[(int)slot.Position] = slot.Player;
            return map;
        }

        /// <summary>
        /// 타석 하나의 수비 처리를 수비수에게 귀속한다: 실책 출루 = 개인 실책 1, 땅볼 · 뜬공 아웃 = 처리 기회 1(+ 수비력 비례 호수비 확률),
        /// 삼진 = 포수 처리 기회 1. 투수 처리분은 그 타석의 투수에게 간다.
        /// </summary>
        private void RecordFielding(Dictionary<int, Player> field, AtBatStepResult step, Dictionary<Player, FieldLine> fielding)
        {
            FieldLine F(Player p) { if (!fielding.TryGetValue(p, out var l)) fielding[p] = l = new FieldLine(); return l; }
            Player Pick(int[] weights)
            {
                int total = 0;
                for (int i = 0; i < weights.Length; i++) if (i == 8 || field.ContainsKey(i)) total += weights[i];
                if (total <= 0) return step.Pitcher;
                int roll = defenseRandom.Next(total);
                for (int i = 0; i < weights.Length; i++)
                {
                    if (i != 8 && !field.ContainsKey(i)) continue;
                    roll -= weights[i];
                    if (roll < 0) return i == 8 ? step.Pitcher : field[i];
                }
                return step.Pitcher;
            }

            if (step.IsError) { var f = Pick(ErrorWeights); if (f != null) F(f).Errors++; return; }
            if (step.Result == AtBatResult.Strikeout)
            {
                if (field.TryGetValue((int)BatterPosition.Catcher, out var c)) F(c).Chances++;
                return;
            }
            if (step.Result != AtBatResult.Groundout && step.Result != AtBatResult.Flyout) return;
            var fielder = Pick(step.Result == AtBatResult.Flyout ? FlyWeights : GroundWeights);
            if (fielder == null) return;
            var line = F(fielder);
            line.Chances++;
            int rating = fielder.IsPitcher ? 50 : fielder.DefenseStat;
            if (defenseRandom.NextDouble() < 0.02 + Math.Max(0, rating - 50) / 1000.0) line.Fine++;
        }

        // ================================================================== [TASK-GM-03] 박스스코어 표기 · 기사

        public static string PositionShort(BatterPosition pos)
        {
            switch (pos)
            {
                case BatterPosition.Catcher: return "포";
                case BatterPosition.FirstBase: return "1";
                case BatterPosition.SecondBase: return "2";
                case BatterPosition.ThirdBase: return "3";
                case BatterPosition.ShortStop: return "유";
                case BatterPosition.LeftField: return "좌";
                case BatterPosition.CenterField: return "중";
                case BatterPosition.RightField: return "우";
                default: return "지";
            }
        }

        private static string SituationLabel(PaRecord p)
        {
            string runners = p.R1 || p.R2 || p.R3
                ? string.Join(",", new[] { p.R1 ? "1" : null, p.R2 ? "2" : null, p.R3 ? "3" : null }.Where(x => x != null)) + "루"
                : "주자 없음";
            if (p.R1 && p.R2 && p.R3) runners = "만루";
            return $"{(p.Outs == 0 ? "무사" : $"{p.Outs}사")} {runners}";
        }

        private static string ResultLabel(PaRecord p)
        {
            if (p.Error) return "실책 출루";
            if (p.Gidp) return "병살타";
            switch (p.Result)
            {
                case AtBatResult.Single: return p.Runs > 0 ? "적시타" : "안타";
                case AtBatResult.Double: return "2루타";
                case AtBatResult.Triple: return "3루타";
                case AtBatResult.HomeRun: return p.Runs >= 4 ? "만루 홈런" : "홈런";
                case AtBatResult.Walk: return "볼넷";
                case AtBatResult.Strikeout: return "삼진";
                case AtBatResult.Flyout: return p.Runs > 0 ? "희생플라이" : "뜬공";
                default: return "땅볼";
            }
        }

        private static readonly string[] WinQuotes =
        {
            "팀이 이겨서 무엇보다 기쁩니다. 동료들이 만들어 준 기회를 놓치지 않으려 했습니다.",
            "중요한 순간에 집중한 것이 좋은 결과로 이어졌습니다. 팬분들 응원 덕분입니다.",
            "준비한 대로 자신 있게 했습니다. 다음 경기도 오늘처럼 하겠습니다.",
            "더그아웃 분위기가 정말 좋습니다. 이 흐름을 계속 이어 가고 싶습니다.",
        };

        private GameRecapArticle WriteRecap(int day, GMMatchBoxScoreData box, List<PaRecord> plays, string postseasonTitle = null)
        {
            string Name(string code) => NameAliasTable.DisplayTeamName(code);
            string Short(string code) => CompyaShort(NameAliasTable.ToTeam(code));
            var article = new GameRecapArticle();
            bool tie = box.IsTie;
            string winner = tie ? box.HomeCode : box.WinnerCode;
            string loser = winner == box.HomeCode ? box.AwayCode : box.HomeCode;
            int wr = winner == box.HomeCode ? box.HomeR : box.AwayR, lr = winner == box.HomeCode ? box.AwayR : box.HomeR;
            var rec = league.RecordOf(winner);
            string context = !string.IsNullOrEmpty(postseasonTitle) ? $"{postseasonTitle}에서" : box.GameIndex == 0 ? "개막전에서" : winner == box.HomeCode ? "홈에서 열린 경기에서" : "원정 경기에서";
            string streak = !tie && rec.Streak >= 3 ? $" · {rec.Streak}연승" : "";
            article.Headline = tie
                ? $"{Short(box.AwayCode)}-{Short(box.HomeCode)}, 연장 혈투 끝에 {box.AwayR}-{box.HomeR} 무승부"
                : $"{Short(winner)}, {context} {Short(loser)}에 {wr}-{lr} 승리{streak}";

            var winPitchers = box.PitchersOf(winner == box.HomeCode);
            var losePitchers = box.PitchersOf(winner != box.HomeCode);
            var w = winPitchers.FirstOrDefault(p => p.Decision == "W");
            var l = losePitchers.FirstOrDefault(p => p.Decision == "L");
            var s = winPitchers.FirstOrDefault(p => p.Decision == "S");
            var winStarter = winPitchers.FirstOrDefault();
            var loseStarter = losePitchers.FirstOrDefault();
            string Line(PitcherBoxScoreLine p) => $"{p.Name}({p.IPLabel}이닝 {p.H}피안타 {p.SO}탈삼진 {p.ER}자책)";
            if (tie)
                article.Paragraph1 = $"{Name(box.AwayCode)}와(과) {Name(box.HomeCode)}가 {box.DateLabel} {box.Stadium}에서 {box.Innings}회까지 승부를 가리지 못했다. " +
                                     $"선발 {Line(box.AwayPitchers.First())}, {Line(box.HomePitchers.First())}이(가) 마운드를 지켰다.";
            else
                article.Paragraph1 = $"{Name(winner)}가 {box.DateLabel} {box.Stadium}에서 {Name(loser)}를 {wr}-{lr}로 꺾었다. " +
                                     $"승리 투수는 {(w != null ? Line(w) : "-")}, 패전 투수는 {(l != null ? Line(l) : "-")}" +
                                     (s != null ? $", 세이브는 {s.Name}가 기록했다." : "이다.") +
                                     $" 선발 맞대결은 {(winStarter != null ? Line(winStarter) : "-")} 대 {(loseStarter != null ? Line(loseStarter) : "-")}였다.";

            var key = box.KeyPlays.FirstOrDefault();
            article.Paragraph2 = key != null
                ? $"승부처는 {key.Inning}회{(key.IsTop ? "초" : "말")}였다. {key.Situation} 상황에서 {Short(key.TeamCode)} {key.BatterName}이(가) {key.ResultLabel}{(key.RBI > 0 ? $"로 {key.RBI}타점을 올렸다" : "을(를) 기록했다")}. " +
                  $"이 한 타석으로 팀 승리 확률이 {(key.DeltaWPA >= 0 ? "+" : "")}{key.DeltaWPA * 100f:0.0}% 움직였다."
                : "양 팀 모두 결정적인 장면 없이 팽팽한 흐름이 이어졌다.";

            var heroPa = plays.Where(p => tie || p.TeamCode == winner).GroupBy(p => p.Batter)
                .Select(g => (player: g.Key, wpa: g.Sum(x => x.Delta))).OrderByDescending(x => x.wpa).FirstOrDefault();
            if (heroPa.player != null)
            {
                var heroLine = box.BattersOf(league.TeamCodeOf(heroPa.player) == box.HomeCode).FirstOrDefault(b => b.PlayerId == heroPa.player.InstanceId);
                string quote = WinQuotes[(((heroPa.player.InstanceId ?? "").GetHashCode() + day) & 0x7FFFFFFF) % WinQuotes.Length];
                article.Paragraph3 = $"수훈 선수 {heroPa.player.Template.PlayerName}은(는) {(heroLine != null ? $"{heroLine.AB}타수 {heroLine.H}안타 {heroLine.RBI}타점" : "맹활약")}으로 경기를 이끌었다. " +
                                     $"경기 후 그는 \"{quote}\"라고 소감을 밝혔다.";
            }
            else
            {
                article.Paragraph3 = $"{Name(winner)} 감독은 \"선수들이 끝까지 집중해 줬다. 팬들께 감사드린다\"고 말했다.";
            }

            string me = league.SelectedTeamCode;
            if (!string.IsNullOrEmpty(postseasonTitle))
                article.Paragraph4 = $"{Name(me)}의 가을야구는 계속된다. 시리즈 다음 경기는 플레이오프 트리에서 이어진다.";
            else if (day + 1 < SeasonGames)
            {
                var next = MatchesOn(day + 1).FirstOrDefault(m => m.home == me || m.away == me);
                if (next.home != null)
                {
                    string opp = next.home == me ? next.away : next.home;
                    article.Paragraph4 = $"{Name(me)}는 {DayLabel(day + 1)} {KBOManager.UI.CompyaUiKit.Stadium(NameAliasTable.ToTeam(next.home))}에서 {Name(opp)}와(과) " +
                                         $"{(next.home == me ? "홈" : "원정")} 경기를 치른다.";
                }
            }
            if (string.IsNullOrEmpty(article.Paragraph4)) article.Paragraph4 = $"{Name(me)}의 {league.SeasonYear} 정규시즌 일정이 모두 끝났다. 이제 가을야구를 준비한다.";
            return article;
        }

        /// <summary>구단 경기 보정(케미스트리 6대 역학 · 치어리더 엔트리 · 홈 어드밴티지). [TASK-GM-04] 포스트시즌도 같은 보정을 쓴다.</summary>
        public TeamPowerModifiers ModifiersFor(GMTeamState team, bool isHome)
        {
            var available = team.AvailableRoster;
            var report = TeamChemistryEngine.EvaluateRoster(available, team.PayrollCap, team.TeamworkBuff);
            var record = league.RecordOf(team.TeamCode);
            var entry = team.CheerEntry.ToList();
            var cheer = GMCheerleaderRoster.BuildMatchEffects(entry, team.Team, isHome, Math.Max(0, -record.Streak)); // [TASK-GM-05] 체력 30 미만 효율 50%
            int teamOvr = available.Count > 0 ? (int)Math.Round(available.Average(p => p.GetEffectiveOverall())) : 0;
            var chem = GMChemistryModifiers.From(report);
            if (chem != null)
            {
                // [TASK-GM-05] 기본 실책률(수비 주전 평균 수비력) · 마운드 응원 실책 억제 · 타격 응원 후반 클러치
                var fielders = LineupAssignment.AssignStarters(available, team.Lineup)
                    .Where(s => s.Player != null && s.Position != BatterPosition.DesignatedHitter).Select(s => s.Player).ToList();
                chem.BaseErrorRate = GMChemistryModifiers.BaseErrorRateFor(fielders.Count > 0 ? fielders.Average(p => p.DefenseStat) : 65.0);
                chem.CheerErrorReduction = GMCheerleaderRoster.ErrorReduction(entry);
                chem.ClutchHitModifier += GMCheerleaderRoster.ClutchBonus(entry);
                chem.AbsCatcherSkill = CatcherAbsSkill(available, team.Lineup); // [TASK-GM-06] ABS 포수 블로킹 · 도루저지
                chem.Batting = league.EnsureBattingBalance();                     // [TASK-GM-08] 타격 밸런스 · 리그 환경 정규화
            }
            return new TeamPowerModifiers(0, isHome ? TeamPowerModifiers.HomeAdvantageConditionBuff : 0, 1.0f, null, cheer, teamOvr, chem);
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
            // [TASK-GM-07] 페넌트 레이스 모드(중요 결정만 직접) - 대기록은 소식으로만 남기고 시뮬레이션을 멈추지 않는다.
            bool pause = (userTeam || leagueMajor) && !(league.FrontOffice?.Manager?.PennantMode ?? false);
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

        /// <summary>[TASK-GM-04] 시상 · 포스트시즌 소식(날짜 표기를 직접 지정 - "11/24/2026" 등). pause면 대기록처럼 시뮬레이션을 멈추고 팝업.</summary>
        public GMNewsItem PostNews(int gameIndex, string dateLabel, GMNewsKind kind, string title, string body, bool userTeam, bool major, bool pause = false)
        {
            var item = new GMNewsItem { GameIndex = gameIndex, DateLabel = dateLabel ?? DayLabel(gameIndex), Kind = kind, Title = title, Body = body, IsUserTeam = userTeam, IsMajor = major };
            league.AddNews(item);
            if (pause) interrupts.Enqueue(new GMSimInterrupt { Kind = GMInterruptKind.Record, News = item });
            return item;
        }

        private GMNewsItem AddNews(int day, GMNewsKind kind, string title, string body, bool userTeam, bool major)
        {
            var item = new GMNewsItem { GameIndex = day, DateLabel = DayLabel(day), Kind = kind, Title = title, Body = body, IsUserTeam = userTeam, IsMajor = major };
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

            foreach (var s in league.Stats.Values) s.DefensiveScore = GMAwardEvaluator.DefensiveScore(s.DefG, s.Chances, s.Errors, s.FinePlays, s.DefRating); // [TASK-GM-04]

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

        /// <summary>[TASK-GM-08] OOTP식 리그 환경 정규화 - 리그 누적 타율을 목표(.265)로 당기는 안타 가중치 배수를 갱신한다.</summary>
        private void UpdateLeagueEnvironment()
        {
            int h = 0, ab = 0;
            foreach (var s in league.Stats.Values) { h += s.H; ab += s.AB; }
            league.EnsureBattingBalance().UpdateEnvironment(h, ab);
        }

        /// <summary>[TASK-GM-08] 리그 평균 타율 · 출루율 · 장타율 · 삼진율(타석 기준).</summary>
        public (double avg, double obp, double slg, double kRate) LeagueBattingLine()
        {
            int pa = 0, ab = 0, h = 0, bb = 0, tb = 0, so = 0;
            foreach (var s in league.Stats.Values) { pa += s.PA; ab += s.AB; h += s.H; bb += s.BB; tb += s.TotalBases; so += s.SO; }
            return (ab == 0 ? 0 : (double)h / ab, pa == 0 ? 0 : (double)(h + bb) / pa, ab == 0 ? 0 : (double)tb / ab, pa == 0 ? 0 : (double)so / pa);
        }

        private static double Woba(GMPlayerSeasonStats s) => 0.69 * s.BB + 0.89 * s.Singles + 1.27 * s.Doubles + 1.62 * s.Triples + 2.10 * s.HR;

        private static string CompyaShort(Team team) => KBOManager.UI.CompyaUiKit.ShortName(team);
    }
}
