using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;
using Random = System.Random; // UnityEngine.Random과의 이름 충돌 방지

namespace KBOManager.Engine
{
    /// <summary>타석 결과.</summary>
    public enum AtBatResult
    {
        Strikeout,
        Groundout,
        Flyout,
        Walk,
        Single,
        Double,
        Triple,
        HomeRun
    }

    /// <summary>한 경기의 스코어보드 결과.</summary>
    public class MatchResult
    {
        public string HomeTeamName;
        public string AwayTeamName;

        public List<int> HomeInningScores = new List<int>(); // 끝내기로 말 공격이 생략되면 마지막 이닝 값이 비어 있을 수 있다
        public List<int> AwayInningScores = new List<int>();

        public int HomeTotalScore;
        public int AwayTotalScore;

        public bool IsExtraInnings;
        public string WinnerTeamName; // null이면 무승부 (연장 상한 도달)
    }

    /// <summary>
    /// 타석 하나가 진행되는 순간의 게임 상황. 스킬 발동 조건(SkillCondition) 판정과 베이스러닝 로직이
    /// 모두 이 객체를 기준으로 동작한다. 하프이닝 동안 하나의 인스턴스가 계속 갱신되며 사용된다.
    /// </summary>
    public class MatchState
    {
        public int Inning = 1;
        public int Outs;
        public int Balls;
        public int Strikes;
        public bool RunnerOnFirst;
        public bool RunnerOnSecond;
        public bool RunnerOnThird;
        public bool IsPostSeason;

        public bool HasAnyRunner => RunnerOnFirst || RunnerOnSecond || RunnerOnThird;
        public bool HasRunnerInScoringPosition => RunnerOnSecond || RunnerOnThird;

        public void ResetCount()
        {
            Balls = 0;
            Strikes = 0;
        }
    }

    /// <summary>PlayNextAtBat() 1회 호출의 결과. UI(PlayBallController 등)가 연출/로그/하이라이트 트리거 판정에 사용한다.</summary>
    public class AtBatStepResult
    {
        public Player Batter;
        public Player Pitcher;
        public AtBatResult Result;
        public int RunsScoredThisPlay;
        public bool HalfInningEnded;
        public bool GameEnded;
        public MatchState State; // 이 타석 종료 직후의 상태 스냅샷 (이닝/아웃/주자 등)

        // 이 타석 종료 직후의 누적 스코어. 하이라이트 트리거(접전/끝내기 위기 등)가 MatchEngine 내부에
        // 접근하지 않고도 판정할 수 있도록 스냅샷으로 노출한다.
        public int HomeScore;
        public int AwayScore;
        public bool IsTopHalf; // true = 원정 공격(초) 중이었던 타석
    }

    /// <summary>
    /// 1이닝 1구(타석) 단위로 진행되는 순수 C# 시뮬레이션 엔진. MonoBehaviour를 상속하지 않아
    /// 씬/프레임 오버헤드 없이 다수의 경기를 즉시(백그라운드) 계산할 수 있다.
    /// </summary>
    public class MatchEngine
    {
        private const int RegulationInnings = 9;
        private const int MaxInnings = 12; // KBO 정규시즌 연장 상한. 도달 시 무승부로 종료

        // 병살타/희생플라이 판정용 임시 확률. GDD 미명시 - 밸런스 확정 전까지의 추정값.
        private const double DoublePlayChance = 0.4; // 1루 주자 있고 2아웃 미만인 땅볼일 때 병살 발생 확률
        private const double TwoStrikeReachProbability = 0.6; // 타석이 2스트라이크까지 도달할 확률(단순화된 카운트 모델)

        /// <summary>타석 결과가 세부 스탯 중 어떤 "매치업"에 의해 좌우되는지.</summary>
        private enum OutcomeDriver
        {
            Strikeout,      // 투수 구위/구속 vs 타자 정확/선구
            Walk,           // 타자 선구 vs 투수 제구
            ContactQuality, // 타자 정확 vs 투수 구위 (범타로 죽느냐, 안타가 되느냐)
            Power           // 타자 파워 vs 투수 변화 (장타가 되느냐)
        }

        // 타석 결과 기본(fallback) 확률표. EngineConfig가 있으면 결과별 BaseWeight는 그쪽 값을 우선 사용하고,
        // driver/direction 관계(어떤 스탯 매치업이 무엇을 좌우하는지)는 물리적 의미가 있는 구조이므로 코드에 고정한다.
        private static readonly (AtBatResult result, float defaultBaseWeight, OutcomeDriver driver, float direction)[] OutcomeTable =
        {
            (AtBatResult.Strikeout, 0.22f, OutcomeDriver.Strikeout,      +1f),
            (AtBatResult.Walk,      0.08f, OutcomeDriver.Walk,           +1f),
            (AtBatResult.Groundout, 0.23f, OutcomeDriver.ContactQuality, -1f),
            (AtBatResult.Flyout,    0.20f, OutcomeDriver.ContactQuality, -1f),
            (AtBatResult.Single,    0.16f, OutcomeDriver.ContactQuality, +1f),
            (AtBatResult.Double,    0.06f, OutcomeDriver.Power,          +1f),
            (AtBatResult.Triple,    0.01f, OutcomeDriver.Power,          +1f),
            (AtBatResult.HomeRun,   0.04f, OutcomeDriver.Power,          +1f),
        };

        // config가 없을 때 쓰는 기본값. EngineConfig.PopulateDefaults()의 값과 동일하게 맞춰 둔다.
        private const float DefaultSkillInfluence = 0.6f;
        private const float DefaultStatDiffNormalizer = 50f;
        private const int DefaultSetDeckActivationThreshold = 5;
        private const float DefaultSetDeckBonusMultiplier = 1.15f;

        private static readonly BatterPosition[] StarterBatterPositions =
            (BatterPosition[])Enum.GetValues(typeof(BatterPosition));

        private readonly SkillDB skillDB;
        private readonly EngineConfig config;
        private readonly Random random;

        private float SkillInfluence => config != null ? config.SkillInfluence : DefaultSkillInfluence;
        private float StatDiffNormalizer => config != null ? config.StatDiffNormalizer : DefaultStatDiffNormalizer;
        private int SetDeckActivationThreshold => config != null ? config.SetDeckActivationThreshold : DefaultSetDeckActivationThreshold;
        private float SetDeckBonusMultiplier => config != null ? config.SetDeckBonusMultiplier : DefaultSetDeckBonusMultiplier;

        // 스킬이 섞이지 않은 순수 OVR(강화/각성/세트덱만 반영). 투수 로테이션 정렬과, "패기"류 스킬의
        // OVR 비교 조건(GDD: 상대 스킬 효과로 인한 OVR 증가 제외)에 사용한다.
        private readonly Dictionary<Player, int> baseOvrCache = new Dictionary<Player, int>();
        private readonly Dictionary<Player, (bool isActive, float multiplier)> setDeckContext = new Dictionary<Player, (bool, float)>();

        /// <summary>
        /// skillDB/config는 선택 사항이다(없으면 각각 스킬 보정 없이, 코드 기본 상수로 진행한다).
        /// randomSeed를 지정하면 결과 재현이 가능하다.
        /// </summary>
        public MatchEngine(SkillDB skillDB = null, EngineConfig config = null, int? randomSeed = null)
        {
            this.skillDB = skillDB;
            this.config = config;
            random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        }

        private class TeamGameState
        {
            public string TeamName;
            public List<Player> BattingOrder = new List<Player>(); // 최대 9명
            public int NextBatterIndex;

            public List<Player> Starters = new List<Player>();     // OVR 내림차순, [0] = 오늘의 선발
            public List<Player> LongRelief = new List<Player>();
            public List<Player> WinningRelief = new List<Player>();
            public List<Player> MopUpRelief = new List<Player>();
            public List<Player> Closers = new List<Player>();
            public HashSet<Player> UsedPitchers = new HashSet<Player>();

            public int RunsScored;
        }

        // ----- 스텝 단위 진행 상태 -----
        // 한 MatchEngine 인스턴스는 한 경기만 진행한다고 가정한다(LeagueManager/PlayBallController 모두
        // 경기마다 새 MatchEngine을 생성해 쓰는 기존 관례를 그대로 따른다). 동시에 여러 경기를 진행하려면
        // 경기마다 별도의 MatchEngine 인스턴스를 만들어야 한다.
        private TeamGameState homeState;
        private TeamGameState awayState;
        private string homeTeamName;
        private string awayTeamName;
        private bool isPostSeasonMatch;
        private int currentInning;
        private bool isTopHalf; // true = 원정 공격(초)
        private MatchState currentAtBatState;
        private Player currentHalfInningPitcher;
        private int runsThisHalfInning;
        private bool matchStarted;

        // 경기 중 교체되어 나간 선수(대타로 빠진 타자, 강판된 투수). 야구 룰상 이 경기에는 다시 출전할 수 없다.
        private readonly HashSet<Player> subbedOutList = new HashSet<Player>();

        public MatchResult Result { get; private set; }
        public bool IsGameOver { get; private set; }
        public MatchState CurrentState => currentAtBatState;
        public int CurrentInning => currentInning;
        public bool IsAwayBatting => isTopHalf;
        public bool IsHomeTeamBatting => !isTopHalf;

        /// <summary>이 경기에서 교체되어 나가, 다시 출전할 수 없는 선수 목록.</summary>
        public IReadOnlyCollection<Player> SubbedOutList => subbedOutList;

        public IReadOnlyList<Player> HomeBattingOrder => homeState?.BattingOrder;
        public IReadOnlyList<Player> AwayBattingOrder => awayState?.BattingOrder;

        /// <summary>현재 이닝을 던지고 있는 투수.</summary>
        public Player CurrentPitcher => currentHalfInningPitcher;

        /// <summary>현재 공격 중인 팀의 다음 타석에 나올 예정인 타자. 대타 교체 대상이 곧 이 선수다.</summary>
        public Player UpcomingBatter
        {
            get
            {
                var battingTeam = isTopHalf ? awayState : homeState;
                if (battingTeam == null || battingTeam.BattingOrder.Count == 0) return null;
                return battingTeam.BattingOrder[battingTeam.NextBatterIndex % battingTeam.BattingOrder.Count];
            }
        }

        /// <summary>
        /// 스텝 단위 진행(PlayNextAtBat)을 위해 경기를 준비한다. PlayFullMatch()도 내부적으로 이를 사용한다.
        /// isPostSeason은 "가을사나이" 등 포스트시즌 조건부 스킬 판정에 쓰인다.
        /// </summary>
        public void BeginMatch(List<Player> homeRoster, List<Player> awayRoster,
            string homeTeamName = "Home", string awayTeamName = "Away", bool isPostSeason = false)
        {
            homeState = BuildTeamState(homeRoster, homeTeamName);
            awayState = BuildTeamState(awayRoster, awayTeamName);
            this.homeTeamName = homeTeamName;
            this.awayTeamName = awayTeamName;
            isPostSeasonMatch = isPostSeason;

            Result = new MatchResult { HomeTeamName = homeTeamName, AwayTeamName = awayTeamName };
            IsGameOver = false;
            currentInning = 1;
            isTopHalf = true;
            matchStarted = true;

            StartHalfInning();
        }

        private void StartHalfInning()
        {
            currentAtBatState = new MatchState { Inning = currentInning, IsPostSeason = isPostSeasonMatch };
            var pitchingTeam = isTopHalf ? homeState : awayState;
            var battingTeam = isTopHalf ? awayState : homeState;
            currentHalfInningPitcher = SelectPitcherForInning(pitchingTeam, battingTeam, currentInning);
            runsThisHalfInning = 0;
        }

        // ----- 선수 교체 API -----

        /// <summary>
        /// 현재 공격 중인 팀의 "다음 타석에 나올 예정인 타자"(UpcomingBatter)를 대타로 교체한다.
        /// 원래 있던 타자는 SubbedOutList에 등록되어 이 경기에서 다시 출전할 수 없다.
        /// MatchState(아웃/주자/볼카운트/이닝)는 건드리지 않으므로, 다음 PlayNextAtBat() 호출부터
        /// 자연스럽게 새 타자로 이어진다.
        /// </summary>
        public bool SubstituteBatter(Player newBatter)
        {
            if (!matchStarted || IsGameOver) return false;
            if (newBatter?.Template == null || newBatter.Template.IsPitcher) return false;
            if (subbedOutList.Contains(newBatter)) return false; // 이미 교체되어 나간 선수는 재출전 불가

            var battingTeam = isTopHalf ? awayState : homeState;
            if (battingTeam.BattingOrder.Count == 0) return false;
            if (battingTeam.BattingOrder.Contains(newBatter)) return false; // 이미 라인업에 있는 선수 중복 방지

            int slotIndex = battingTeam.NextBatterIndex % battingTeam.BattingOrder.Count;
            var outgoingBatter = battingTeam.BattingOrder[slotIndex];
            if (outgoingBatter == newBatter) return false; // 동일 선수로의 "교체"는 무의미

            battingTeam.BattingOrder[slotIndex] = newBatter;
            if (outgoingBatter != null) subbedOutList.Add(outgoingBatter);

            return true;
        }

        /// <summary>
        /// 현재 이닝을 던지고 있는 투수(CurrentPitcher)를 구원 투수로 교체한다. 원래 투수는 SubbedOutList에
        /// 등록되어 이 경기에서 다시 등판할 수 없다. MatchState는 건드리지 않으므로 남은 아웃카운트/주자
        /// 상황 그대로 새 투수가 이어받는다.
        /// </summary>
        public bool SubstitutePitcher(Player newPitcher)
        {
            if (!matchStarted || IsGameOver) return false;
            if (newPitcher?.Template == null || !newPitcher.Template.IsPitcher) return false;
            if (subbedOutList.Contains(newPitcher)) return false; // 이미 교체되어 나간 투수는 재등판 불가
            if (currentHalfInningPitcher == newPitcher) return false; // 동일 투수로의 "교체"는 무의미

            var pitchingTeam = isTopHalf ? homeState : awayState;

            var outgoingPitcher = currentHalfInningPitcher;
            if (outgoingPitcher != null) subbedOutList.Add(outgoingPitcher);

            currentHalfInningPitcher = newPitcher;
            pitchingTeam.UsedPitchers.Add(newPitcher); // 이후 자동 로테이션에서 재선택되지 않도록 등록

            return true;
        }

        /// <summary>
        /// 정확히 타석 1회를 진행한다. BeginMatch()를 먼저 호출해야 하며, IsGameOver가 true가 되면 더 이상
        /// 진행하지 않는다(호출 시 GameEnded=true인 결과를 그대로 반환).
        /// </summary>
        public AtBatStepResult PlayNextAtBat()
        {
            if (!matchStarted) throw new InvalidOperationException("BeginMatch()를 먼저 호출해야 합니다.");
            if (IsGameOver) return new AtBatStepResult { GameEnded = true };

            var battingTeam = isTopHalf ? awayState : homeState;

            if (battingTeam.BattingOrder.Count == 0 || currentHalfInningPitcher == null)
            {
                // 유효한 타자/투수가 전혀 없는 극단적 로스터 상태 - 더 진행할 수 없으므로 경기를 즉시 종료한다.
                FinishGame();
                return new AtBatStepResult { HalfInningEnded = true, GameEnded = true };
            }

            bool wasTopHalf = isTopHalf; // AdvanceAfterHalfInning()이 isTopHalf를 바꾸기 전에 이 타석 시점 값을 보존

            var batter = GetNextBatter(battingTeam);
            currentAtBatState.ResetCount();
            RollPitchCount(currentAtBatState);

            var result = SimulateAtBat(batter, currentHalfInningPitcher, currentAtBatState);
            int runs = ResolveAtBatEffect(result, currentAtBatState);

            runsThisHalfInning += runs;
            if (isTopHalf) { Result.AwayTotalScore += runs; awayState.RunsScored += runs; }
            else { Result.HomeTotalScore += runs; homeState.RunsScored += runs; }

            bool halfInningEnded = false;

            // 진짜 끝내기: 9회 이후 말 공격 중 이 타석으로 홈이 앞서가면 3아웃을 채우지 않고 즉시 종료한다.
            if (!isTopHalf && currentInning >= RegulationInnings && Result.HomeTotalScore > Result.AwayTotalScore)
            {
                halfInningEnded = true;
                FinishHalfInning();
                FinishGame();
            }
            else if (currentAtBatState.Outs >= 3)
            {
                halfInningEnded = true;
                FinishHalfInning();
                AdvanceAfterHalfInning();
            }

            return new AtBatStepResult
            {
                Batter = batter,
                Pitcher = currentHalfInningPitcher,
                Result = result,
                RunsScoredThisPlay = runs,
                HalfInningEnded = halfInningEnded,
                GameEnded = IsGameOver,
                State = currentAtBatState,
                HomeScore = Result.HomeTotalScore,
                AwayScore = Result.AwayTotalScore,
                IsTopHalf = wasTopHalf,
            };
        }

        private void FinishHalfInning()
        {
            if (isTopHalf) Result.AwayInningScores.Add(runsThisHalfInning);
            else Result.HomeInningScores.Add(runsThisHalfInning);
        }

        /// <summary>3아웃으로 하프이닝이 정상 종료된 직후, 다음 하프이닝/이닝으로 넘어가거나 경기를 종료한다.</summary>
        private void AdvanceAfterHalfInning()
        {
            if (isTopHalf)
            {
                // 방금 초(원정 공격)가 끝났다. 9회 이후 홈이 이미 앞서 있으면 말 공격을 생략하고 바로 종료.
                isTopHalf = false;
                bool skipBottom = currentInning >= RegulationInnings && Result.HomeTotalScore > Result.AwayTotalScore;
                if (skipBottom)
                {
                    FinishGame();
                    return;
                }

                StartHalfInning();
            }
            else
            {
                // 방금 말(홈 공격)이 끝났다.
                bool decided = currentInning >= RegulationInnings && Result.HomeTotalScore != Result.AwayTotalScore;
                bool reachedCap = currentInning >= MaxInnings;
                if (decided || reachedCap)
                {
                    FinishGame();
                    return;
                }

                currentInning++;
                isTopHalf = true;
                StartHalfInning();
            }
        }

        private void FinishGame()
        {
            if (IsGameOver) return; // 중복 호출 방지

            IsGameOver = true;
            Result.IsExtraInnings = Result.AwayInningScores.Count > RegulationInnings;
            Result.WinnerTeamName = Result.HomeTotalScore == Result.AwayTotalScore
                ? null
                : (Result.HomeTotalScore > Result.AwayTotalScore ? homeTeamName : awayTeamName);
        }

        /// <summary>
        /// 양 팀의 28인 로스터로 1회부터 9회까지(동점 시 연장 최대 12회) 경기를 즉시 시뮬레이션한다.
        /// 내부적으로 BeginMatch() + PlayNextAtBat() 반복 호출과 완전히 동일한 규칙을 사용한다("빠른 진행" 모드용).
        /// </summary>
        public MatchResult PlayFullMatch(List<Player> homeRoster, List<Player> awayRoster,
            string homeTeamName = "Home", string awayTeamName = "Away", bool isPostSeason = false)
        {
            BeginMatch(homeRoster, awayRoster, homeTeamName, awayTeamName, isPostSeason);

            while (!IsGameOver)
            {
                PlayNextAtBat();
            }

            return Result;
        }

        /// <summary>
        /// 타자/투수의 세부 스탯(강화·각성·세트덱·스킬 효과 모두 반영)을 매치업별로 비교해
        /// 확률적으로 타석 결과를 산출한다. state는 이닝/아웃/볼카운트/주자/포스트시즌 여부를 담아
        /// RunnerOnBase, TwoStrikesOrMore 등 상황부 스킬 조건 판정에 쓰인다.
        /// PlayFullMatch가 미리 채워 둔 세트덱 컨텍스트를 사용하며, 캐시에 없는 대상(단독 호출 등)은
        /// 세트덱 보너스 없이 계산한다.
        /// </summary>
        public AtBatResult SimulateAtBat(Player batter, Player pitcher, MatchState state)
        {
            if (batter?.Template == null || pitcher?.Template == null) return AtBatResult.Groundout;
            if (batter.Template.IsPitcher || !pitcher.Template.IsPitcher) return AtBatResult.Groundout;

            state ??= new MatchState();

            var batterStats = ResolveEffectiveBatterStats(batter, pitcher, state);
            var pitcherStats = ResolveEffectivePitcherStats(pitcher, batter, state);

            // 삼진: 투수 구위/구속 평균 vs 타자 정확/선구 평균 (동일 가중치)
            float strikeoutSkill = NormalizeDiff(
                (pitcherStats.Stuff + pitcherStats.Velocity) / 2f
                - (batterStats.Contact + batterStats.Discipline) / 2f);

            // 볼넷: 타자 선구 vs 투수 제구
            float walkSkill = NormalizeDiff(batterStats.Discipline - pitcherStats.Control);

            // 안타 여부(맞은 공이 범타로 죽느냐/안타가 되느냐): 타자 정확 vs 투수 구위
            float contactSkill = NormalizeDiff(batterStats.Contact - pitcherStats.Stuff);

            // 장타(2루타 이상) 여부: 타자 파워 vs 투수 변화
            float powerSkill = NormalizeDiff(batterStats.Power - pitcherStats.Movement);

            var weights = new float[OutcomeTable.Length];
            float total = 0f;
            for (int i = 0; i < OutcomeTable.Length; i++)
            {
                var (result, defaultBaseWeight, driver, direction) = OutcomeTable[i];
                float baseWeight = config != null ? config.GetBaseWeight(result, defaultBaseWeight) : defaultBaseWeight;
                float skill = driver switch
                {
                    OutcomeDriver.Strikeout => strikeoutSkill,
                    OutcomeDriver.Walk => walkSkill,
                    OutcomeDriver.ContactQuality => contactSkill,
                    OutcomeDriver.Power => powerSkill,
                    _ => 0f
                };

                float multiplier = Mathf.Max(1f + direction * skill * SkillInfluence, 0.05f); // 확률 붕괴 방지 하한
                weights[i] = baseWeight * multiplier;
                total += weights[i];
            }

            double roll = random.NextDouble() * total;
            double cumulative = 0;
            for (int i = 0; i < OutcomeTable.Length; i++)
            {
                cumulative += weights[i];
                if (roll <= cumulative) return OutcomeTable[i].result;
            }

            return OutcomeTable[OutcomeTable.Length - 1].result;
        }

        private float NormalizeDiff(float diff) => Mathf.Clamp(diff / StatDiffNormalizer, -1f, 1f);

        // ----- 팀 상태 구성 -----

        private TeamGameState BuildTeamState(List<Player> roster, string teamName)
        {
            var valid = (roster ?? new List<Player>()).Where(p => p?.Template != null).ToList();
            var (isSetDeckActive, multiplier) = EvaluateSetDeckBonus(valid);

            foreach (var player in valid)
            {
                setDeckContext[player] = (isSetDeckActive, multiplier);
                baseOvrCache[player] = player.CalculateOVR(isSetDeckActive, multiplier); // 스킬 미포함 순수 OVR
            }

            return new TeamGameState
            {
                TeamName = teamName,
                BattingOrder = BuildBattingOrder(valid),
                Starters = valid.Where(p => p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.StartingPitcher)
                    .OrderByDescending(GetBaseOvr).ToList(),
                LongRelief = valid.Where(p => p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.LongReliever).ToList(),
                WinningRelief = valid.Where(p => p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.WinningReliever).ToList(),
                MopUpRelief = valid.Where(p => p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.MopUpReliever).ToList(),
                Closers = valid.Where(p => p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.Closer).ToList(),
            };
        }

        private static List<Player> BuildBattingOrder(List<Player> teamRoster)
        {
            var batters = teamRoster.Where(p => !p.Template.IsPitcher).ToList();
            var order = new List<Player>(9);

            // 포지션당 1명, OVR 최우선으로 우선 배정
            foreach (var position in StarterBatterPositions)
            {
                var pick = batters.Where(p => p.Template.BatterPosition == position && !order.Contains(p))
                    .OrderByDescending(p => p.CalculateOVR(false))
                    .FirstOrDefault();
                if (pick != null) order.Add(pick);
            }

            // [Fallback] 포지션 후보가 없어 9자리가 안 채워지면 남은 타자 중 OVR 상위로 채운다.
            foreach (var extra in batters.Where(p => !order.Contains(p)).OrderByDescending(p => p.CalculateOVR(false)))
            {
                if (order.Count >= 9) break;
                order.Add(extra);
            }

            return order;
        }

        private (bool isActive, float multiplier) EvaluateSetDeckBonus(List<Player> teamRoster)
        {
            int maxCount = teamRoster
                .Where(p => p.Template.Team != Team.None)
                .GroupBy(p => p.Template.Team)
                .Select(g => g.Count())
                .DefaultIfEmpty(0)
                .Max();

            bool isActive = maxCount >= SetDeckActivationThreshold;
            return (isActive, isActive ? SetDeckBonusMultiplier : 1f);
        }

        // ----- 세부 스탯 계산(세트덱 + 스킬 효과 반영) -----

        private (bool isActive, float multiplier) GetSetDeckContext(Player player)
        {
            return setDeckContext.TryGetValue(player, out var context) ? context : (false, 1f);
        }

        private int GetBaseOvr(Player player)
        {
            if (player == null) return 0;
            return baseOvrCache.TryGetValue(player, out var cached) ? cached : player.CalculateOVR(false);
        }

        /// <summary>
        /// 최종 스탯 = (Base + Growth) * SetDeckMultiplier + SkillBonus.
        /// 세트덱 배율은 원본 성장분까지만 곱하고, 스킬 보정은 그 뒤에 가산한다 - 스킬 보너스가
        /// 세트덱 배율의 영향을 받아 함께 부풀려지는 복리 현상을 방지하기 위함이다.
        /// </summary>
        private BatterStats ResolveEffectiveBatterStats(Player batter, Player pitcher, MatchState state)
        {
            var stats = batter.GetEffectiveBatterStats(); // Base + Growth
            var (isActive, multiplier) = GetSetDeckContext(batter);
            if (isActive && multiplier > 1f)
            {
                stats = Scale(stats, multiplier); // (Base + Growth) * SetDeckMultiplier
            }

            // 본인이 보유한 Target=Self 스킬
            stats = ApplyBatterSkills(stats, batter, EffectTarget.Self, self: batter, opponent: pitcher, state);
            // 상대 투수가 보유한 Target=Opponent 스킬(나를 겨냥한 효과) - 조건 판정은 스킬 소유자(투수) 기준
            stats = ApplyBatterSkills(stats, pitcher, EffectTarget.Opponent, self: pitcher, opponent: batter, state);

            return stats;
        }

        /// <summary>ResolveEffectiveBatterStats와 동일한 연산 순서를 투수 스탯에 적용한다.</summary>
        private PitcherStats ResolveEffectivePitcherStats(Player pitcher, Player batter, MatchState state)
        {
            var stats = pitcher.GetEffectivePitcherStats(); // Base + Growth
            var (isActive, multiplier) = GetSetDeckContext(pitcher);
            if (isActive && multiplier > 1f)
            {
                stats = Scale(stats, multiplier); // (Base + Growth) * SetDeckMultiplier
            }

            stats = ApplyPitcherSkills(stats, pitcher, EffectTarget.Self, self: pitcher, opponent: batter, state);
            stats = ApplyPitcherSkills(stats, batter, EffectTarget.Opponent, self: batter, opponent: pitcher, state);

            return stats;
        }

        private static BatterStats Scale(BatterStats stats, float multiplier) => new BatterStats(
            Mathf.RoundToInt(stats.Power * multiplier),
            Mathf.RoundToInt(stats.Contact * multiplier),
            Mathf.RoundToInt(stats.Discipline * multiplier));

        private static PitcherStats Scale(PitcherStats stats, float multiplier) => new PitcherStats(
            Mathf.RoundToInt(stats.Stuff * multiplier),
            Mathf.RoundToInt(stats.Velocity * multiplier),
            Mathf.RoundToInt(stats.Movement * multiplier),
            Mathf.RoundToInt(stats.Control * multiplier));

        /// <summary>
        /// skillOwner가 보유한 스킬 중 지정한 wantedTarget(Self/Opponent)에 해당하고 조건을 만족하는 것만
        /// 골라 stats(타자 스탯)에 적용한다. 조건은 항상 스킬 소유자(self) 기준으로 평가한다.
        /// </summary>
        private BatterStats ApplyBatterSkills(BatterStats stats, Player skillOwner, EffectTarget wantedTarget,
            Player self, Player opponent, MatchState state)
        {
            if (skillDB == null || skillOwner == null) return stats;

            foreach (var skillName in skillOwner.AcquiredSkillIds)
            {
                var effect = skillDB.FindSkill(skillName)?.Effect;
                if (effect == null || effect.Target != wantedTarget) continue;
                if (!IsConditionMet(effect.Condition, self, opponent, state)) continue;

                foreach (var modifier in effect.Modifiers)
                {
                    stats = ApplyModifier(stats, modifier.Stat, modifier.Value);
                }
            }

            return stats;
        }

        private PitcherStats ApplyPitcherSkills(PitcherStats stats, Player skillOwner, EffectTarget wantedTarget,
            Player self, Player opponent, MatchState state)
        {
            if (skillDB == null || skillOwner == null) return stats;

            foreach (var skillName in skillOwner.AcquiredSkillIds)
            {
                var effect = skillDB.FindSkill(skillName)?.Effect;
                if (effect == null || effect.Target != wantedTarget) continue;
                if (!IsConditionMet(effect.Condition, self, opponent, state)) continue;

                foreach (var modifier in effect.Modifiers)
                {
                    stats = ApplyModifier(stats, modifier.Stat, modifier.Value);
                }
            }

            return stats;
        }

        private static BatterStats ApplyModifier(BatterStats stats, StatType stat, int value) => stat switch
        {
            StatType.Power => new BatterStats(stats.Power + value, stats.Contact, stats.Discipline),
            StatType.Contact => new BatterStats(stats.Power, stats.Contact + value, stats.Discipline),
            StatType.Discipline => new BatterStats(stats.Power, stats.Contact, stats.Discipline + value),
            _ => stats // 투수 전용 StatType이 잘못 설정된 경우 무시
        };

        private static PitcherStats ApplyModifier(PitcherStats stats, StatType stat, int value) => stat switch
        {
            StatType.Stuff => new PitcherStats(stats.Stuff + value, stats.Velocity, stats.Movement, stats.Control),
            StatType.Velocity => new PitcherStats(stats.Stuff, stats.Velocity + value, stats.Movement, stats.Control),
            StatType.Movement => new PitcherStats(stats.Stuff, stats.Velocity, stats.Movement + value, stats.Control),
            StatType.Control => new PitcherStats(stats.Stuff, stats.Velocity, stats.Movement, stats.Control + value),
            _ => stats // 타자 전용 StatType이 잘못 설정된 경우 무시
        };

        /// <summary>조건은 항상 스킬 "소유자"(self) 기준으로 평가하며, state는 그 타석 시점의 실제 게임 상황이다.</summary>
        private bool IsConditionMet(SkillCondition condition, Player self, Player opponent, MatchState state) => condition switch
        {
            SkillCondition.Always => true,
            SkillCondition.SelfOvrLowerThanOpponent => GetBaseOvr(self) < GetBaseOvr(opponent),
            SkillCondition.SelfOvrHigherThanOpponent => GetBaseOvr(self) > GetBaseOvr(opponent),
            SkillCondition.RunnerOnBase => state != null && state.HasAnyRunner,
            SkillCondition.RunnerInScoringPosition => state != null && state.HasRunnerInScoringPosition,
            SkillCondition.TwoStrikesOrMore => state != null && state.Strikes >= 2,
            SkillCondition.PostSeasonGame => state != null && state.IsPostSeason,
            SkillCondition.SeventhInningOrLaterNoOuts => state != null && state.Inning >= 7 && state.Outs == 0,
            _ => false
        };

        // ----- 이닝/타석 진행 -----

        /// <summary>
        /// 타석 1회의 결과를 state(아웃/주자)에 반영하고, 이 플레이로 발생한 득점 수를 반환한다.
        /// 병살타/희생플라이 판정이 여기 포함된다.
        /// </summary>
        private int ResolveAtBatEffect(AtBatResult result, MatchState state)
        {
            bool isOut = result == AtBatResult.Strikeout || result == AtBatResult.Groundout || result == AtBatResult.Flyout;

            if (!isOut)
            {
                return AdvanceRunners(result, state);
            }

            if (result == AtBatResult.Flyout && state.RunnerOnThird && state.Outs < 2)
            {
                // 희생플라이: 3루 주자 생환, 배터만 아웃 처리
                state.RunnerOnThird = false;
                state.Outs++;
                return 1;
            }

            if (result == AtBatResult.Groundout && state.RunnerOnFirst && state.Outs < 2
                && random.NextDouble() < DoublePlayChance)
            {
                // 병살타: 배터 + 1루 주자 아웃 (2아웃 동시 소모), 다른 주자는 그대로
                state.RunnerOnFirst = false;
                state.Outs += 2;
                return 0;
            }

            state.Outs++;
            return 0;
        }

        /// <summary>
        /// 실제 투구 시퀀스를 시뮬레이션하지 않는 단순화된 카운트 모델이다. 이번 타석이 2스트라이크까지
        /// 도달하는지만 확률적으로 결정해 state.Strikes에 반영한다(노림수/위닝샷 등의 조건에 사용).
        /// state.Balls는 구조적으로만 채워두며 현재 어떤 확률 계산에도 쓰이지 않는다(TODO: 완전한 구 단위 시뮬레이션).
        /// </summary>
        private void RollPitchCount(MatchState state)
        {
            bool reachesTwoStrikes = random.NextDouble() < TwoStrikeReachProbability;
            state.Strikes = reachesTwoStrikes ? 2 : (random.NextDouble() < 0.5 ? 1 : 0);
            state.Balls = random.Next(0, 4);
        }

        private static Player GetNextBatter(TeamGameState team)
        {
            if (team.BattingOrder.Count == 0) return null;

            var batter = team.BattingOrder[team.NextBatterIndex % team.BattingOrder.Count];
            team.NextBatterIndex++;
            return batter;
        }

        /// <summary>
        /// 간이 불펜 운용 규칙(1~5회 선발, 6회 롱릴리프, 7~8회 승리/추격조, 9회+ 마무리/추격조).
        /// SubbedOutList에 등록된(유저가 명시적으로 강판시킨) 투수는 이 자동 로테이션에서도 절대 재선택되지
        /// 않는다 - UsedPitchers(소프트 선호도)와 별개로 SubbedOutList는 하드 제외 규칙이다.
        /// 실제 투구수/피로도 기반 교체는 후속 과제.
        /// </summary>
        private Player SelectPitcherForInning(TeamGameState pitchingTeam, TeamGameState battingTeam, int inning)
        {
            int scoreDiff = pitchingTeam.RunsScored - battingTeam.RunsScored; // 0 이상이면 투수팀이 동점 이상

            List<Player> preferred;
            if (inning <= 5 && pitchingTeam.Starters.Count > 0)
            {
                preferred = new List<Player> { pitchingTeam.Starters[0] };
            }
            else if (inning == 6)
            {
                preferred = pitchingTeam.LongRelief;
            }
            else if (inning >= RegulationInnings)
            {
                preferred = scoreDiff >= 0 ? pitchingTeam.Closers : pitchingTeam.MopUpRelief;
            }
            else // 7~8회
            {
                preferred = scoreDiff >= 0 ? pitchingTeam.WinningRelief : pitchingTeam.MopUpRelief;
            }

            var eligiblePreferred = preferred.Where(p => !subbedOutList.Contains(p)).ToList();
            var pick = eligiblePreferred.FirstOrDefault(p => !pitchingTeam.UsedPitchers.Contains(p))
                ?? eligiblePreferred.FirstOrDefault();

            if (pick == null)
            {
                // [Fallback] 선호 롤에 가용 투수가 없으면 전체 투수 풀에서 미사용 -> 재사용 순으로 배정한다.
                var allPitchers = pitchingTeam.Starters
                    .Concat(pitchingTeam.LongRelief)
                    .Concat(pitchingTeam.WinningRelief)
                    .Concat(pitchingTeam.MopUpRelief)
                    .Concat(pitchingTeam.Closers)
                    .Where(p => !subbedOutList.Contains(p))
                    .ToList();

                pick = allPitchers.FirstOrDefault(p => !pitchingTeam.UsedPitchers.Contains(p)) ?? allPitchers.FirstOrDefault();

                if (pick == null)
                {
                    // 이 팀의 등판 가능한 투수가 전원 교체되어 나간 극단적 상황. 경기가 멈추지 않도록
                    // 마지막 수단으로 SubbedOutList를 무시하고 가장 최근에 쓴 투수를 재사용한다.
                    Debug.LogWarning($"[MatchEngine] {pitchingTeam.TeamName}: 등판 가능한 투수가 모두 소진되어 " +
                                      "SubbedOutList를 무시하고 재사용합니다.");
                    var anyPitcher = pitchingTeam.Starters
                        .Concat(pitchingTeam.LongRelief)
                        .Concat(pitchingTeam.WinningRelief)
                        .Concat(pitchingTeam.MopUpRelief)
                        .Concat(pitchingTeam.Closers)
                        .ToList();
                    pick = anyPitcher.FirstOrDefault();
                }
            }

            if (pick != null) pitchingTeam.UsedPitchers.Add(pick);
            return pick;
        }

        /// <summary>타석 결과에 따라 state의 주자를 이동시키고 이번 타석에서 발생한 득점 수를 반환한다.</summary>
        private static int AdvanceRunners(AtBatResult result, MatchState state)
        {
            int runs = 0;

            switch (result)
            {
                case AtBatResult.Walk:
                    bool forcedHome = state.RunnerOnFirst && state.RunnerOnSecond && state.RunnerOnThird;
                    bool forceToThird = state.RunnerOnFirst && state.RunnerOnSecond;
                    bool forceToSecond = state.RunnerOnFirst;
                    if (forcedHome) runs++;
                    if (forceToThird) state.RunnerOnThird = true;
                    if (forceToSecond) state.RunnerOnSecond = true;
                    state.RunnerOnFirst = true;
                    break;

                case AtBatResult.Single:
                    if (state.RunnerOnThird) runs++;
                    state.RunnerOnThird = state.RunnerOnSecond;
                    state.RunnerOnSecond = state.RunnerOnFirst;
                    state.RunnerOnFirst = true;
                    break;

                case AtBatResult.Double:
                    if (state.RunnerOnThird) runs++;
                    if (state.RunnerOnSecond) runs++;
                    state.RunnerOnThird = state.RunnerOnFirst; // 단순화: 1루 주자는 3루에서 멈춘다고 가정
                    state.RunnerOnSecond = true;
                    state.RunnerOnFirst = false;
                    break;

                case AtBatResult.Triple:
                    if (state.RunnerOnThird) runs++;
                    if (state.RunnerOnSecond) runs++;
                    if (state.RunnerOnFirst) runs++;
                    state.RunnerOnThird = true;
                    state.RunnerOnSecond = false;
                    state.RunnerOnFirst = false;
                    break;

                case AtBatResult.HomeRun:
                    runs = 1 + (state.RunnerOnFirst ? 1 : 0) + (state.RunnerOnSecond ? 1 : 0) + (state.RunnerOnThird ? 1 : 0);
                    state.RunnerOnFirst = state.RunnerOnSecond = state.RunnerOnThird = false;
                    break;
            }

            return runs;
        }
    }
}
