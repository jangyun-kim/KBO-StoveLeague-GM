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

    /// <summary>
    /// [TASK-KBO-180] 승부처 직접 플레이 작전. MatchEngine.Tactics[타석 번호]에 넣으면 그 타석의 판정 확률/주자 처리에 실제로 반영된다.
    /// 공격: 강공(파워↑ 정확·선구↓) / 컨택(정확·선구↑ 파워↓) / 번트(희생번트 위주 분포 + 주자 1루씩 진루) / 도루(타석 전 1루 주자 2루 도루 판정).
    /// 수비: 정면 승부(투수 구위·구속↑ 제구↓) / 투수 교체(가용 불펜 중 최고 OVR로 즉시 교체) / 고의사구(볼넷 확정).
    /// 정수값은 저장/로그 호환을 위해 끝에만 추가한다.
    /// </summary>
    public enum MatchTactic
    {
        None = 0,
        PowerSwing = 1,
        ContactSwing = 2,
        Bunt = 3,
        Steal = 4,
        FullForce = 5,
        PitchingChange = 6,
        IntentionalWalk = 7,
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

        /// <summary>독립된 스냅샷을 만든다. PlayNextAtBat()이 이후 하프이닝 전환으로 원본을 교체하기 전에
        /// AtBatStepResult에 담아 반환할 "그 타석 시점"의 상태를 보존하는 데 쓰인다.</summary>
        public MatchState Clone() => new MatchState
        {
            Inning = Inning,
            Outs = Outs,
            Balls = Balls,
            Strikes = Strikes,
            RunnerOnFirst = RunnerOnFirst,
            RunnerOnSecond = RunnerOnSecond,
            RunnerOnThird = RunnerOnThird,
            IsPostSeason = IsPostSeason,
        };
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
        public MatchState State; // 이 타석 종료 직후의 상태 스냅샷 (이닝/아웃/주자 등) - 독립 복사본이라 이후 변형되지 않는다

        // 이 타석 종료 직후의 누적 스코어. 하이라이트 트리거(접전/끝내기 위기 등)가 MatchEngine 내부에
        // 접근하지 않고도 판정할 수 있도록 스냅샷으로 노출한다.
        public int HomeScore;
        public int AwayScore;
        public bool IsTopHalf; // true = 원정 공격(초) 중이었던 타석

        /// <summary>"9회초 홍길동, 좌월 홈런!" 형태의 중계 텍스트. MatchLogger가 생성한다.</summary>
        public string LogMessage;

        /// <summary>이 타석에서 실제로 발생한 주자 이동(득점 포함) 목록. 아웃으로 주자 변화가 없으면 빈 리스트.
        /// PlayFullMatchAsEventQueue()가 이걸 그대로 RunnerAdvance PlayEvent로 변환한다.</summary>
        public List<RunnerMovement> RunnerMovements = new List<RunnerMovement>();

        /// <summary>[TASK-KBO-180] 경기 전체 기준 타석 번호(0부터) - 작전 지정 키(MatchEngine.Tactics).</summary>
        public int PlateAppearance = -1;
        /// <summary>[TASK-KBO-180] 이 타석에 적용된 작전(없으면 None).</summary>
        public MatchTactic Tactic;
        /// <summary>[TASK-KBO-180] 타석 "전"에 일어난 주자 이동(도루 성공 1→2 / 도루 실패 1→-1).</summary>
        public List<RunnerMovement> PreAtBatMovements = new List<RunnerMovement>();
    }

    /// <summary>
    /// 한 주자(타자 본인 포함)의 단일 베이스 이동. 0 = 타자석(홈에서 출발), 1~3 = 1~3루, 4 = 득점(홈 생환).
    /// [TASK-KBO-047] -1 = 아웃(그 베이스에서 주자가 사라짐 - 현재는 병살타로 1루 주자가 아웃되는
    /// 경우에만 씀. FromBase에 아웃된 주자의 원래 베이스 번호, ToBase에 -1을 담아 표현한다).
    /// 2.5D 중계 미니맵이 주자 Dot을 FromBase에서 ToBase로 애니메이션시키는 데 쓴다.
    /// </summary>
    public readonly struct RunnerMovement
    {
        public readonly int FromBase;
        public readonly int ToBase;

        public RunnerMovement(int fromBase, int toBase)
        {
            FromBase = fromBase;
            ToBase = toBase;
        }
    }

    /// <summary>Queue&lt;PlayEvent&gt;에 담기는 개별 이벤트의 종류.</summary>
    public enum PlayEventType
    {
        AtBatResult,   // 타석 결과 확정 (삼진/범타/안타/홈런/사사구 등) - 중계 로그 1줄에 대응
        RunnerAdvance, // 주자(타자 포함) 1명의 베이스 이동 1회
        HalfInningEnd, // 하프이닝 종료 (공수 교대)
        GameEnd        // 경기 종료
    }

    /// <summary>
    /// 2.5D 중계 뷰가 순서대로 재생(Playback)할 단위 이벤트. MatchEngine은 결과를 즉시 UI에 쏘지 않고
    /// 경기 전체를 시뮬레이션한 뒤 이 이벤트들을 Queue&lt;PlayEvent&gt;에 담아 한 번에 반환한다
    /// (PlayFullMatchAsEventQueue() 참고) - UI는 큐를 하나씩 꺼내 타이핑 로그/주자 애니메이션으로 재생하면 된다.
    /// </summary>
    public class PlayEvent
    {
        public PlayEventType Type;

        // ----- 공통 컨텍스트 -----
        public int Inning;
        public bool IsTopHalf;
        public int HomeScore;
        public int AwayScore;

        // ----- Type == AtBatResult 일 때만 유효 -----
        public Player Batter;
        public Player Pitcher;
        public AtBatResult Result;
        public int Outs;
        public int RunsScoredThisPlay;
        /// <summary>"9회초 홍길동, 좌월 홈런!" 형태의 중계 텍스트. 하단 텍스트 창 타이핑 연출에 그대로 사용.</summary>
        public string LogMessage;
        /// <summary>[TASK-KBO-047] 볼/스트라이크 카운트 다이아몬드 UI를 밋밋하지 않게 보여주기 위한 연출용
        /// 값(Flavor)이다 - MatchEngine이 투구 단위로 시뮬레이션하지 않고 타석 결과를 한 번에 확정하므로
        /// (RollPitchCount() 참고), 이 값은 실제 판정 확률 계산에는 전혀 관여하지 않는다.
        /// PlayFullMatchAsEventQueue()가 Result가 확정된 뒤 결과와 모순되지 않는 범위에서 무작위로
        /// 채운다(예: 삼진=3S/0~2B, 볼넷=4B/0~2S, 그 외=0~2S/0~3B).</summary>
        public int Balls;
        public int Strikes;

        // ----- Type == RunnerAdvance 일 때만 유효 -----
        public int FromBase;
        public int ToBase;

        /// <summary>[TASK-KBO-180] 경기 전체 기준 타석 번호(AtBatResult와 그 타석의 RunnerAdvance가 공유). 작전 재계산 키.</summary>
        public int PlateAppearance = -1;
        /// <summary>[TASK-KBO-180] AtBatResult: 적용된 작전. 번트 + 땅볼은 희생번트(타수 미포함)다.</summary>
        public MatchTactic Tactic;
        /// <summary>[TASK-KBO-180] RunnerAdvance: 타석 전 도루 이동(성공 1→2, 실패 1→-1 = 도루자, 병살 아님).</summary>
        public bool IsSteal;
    }

    /// <summary>
    /// 한 팀의 "경기 적용 전력" 보정치(15_team_power_policy.md 3층위 중 마지막 층위, TASK-KBO-039에서
    /// B+C 하이브리드로 확장). B안(가산) - SynergyBuff(상시 시너지 - 세트덱 등)와 ConditionBuff(경기별
    /// 조건부 가산 - 홈 어드밴티지/치어리더)의 합계(TotalBuff)를 세부 스탯에 균등 가산한다.
    /// C안(배율) - ClutchMultiplier(득점권 상황에서 타자 긍정적 결과 가중치에 곱할 배율, 기본 1.0f =
    /// 효과 없음)는 TotalBuff와 별개로 SimulateAtBat()이 직접 사용한다.
    /// 호출부(LeagueManager 등)가 경기 시작 전에 구성해 MatchEngine 생성자로 주입한다 - 엔진 스스로는
    /// 절대 이 값을 계산하지 않는다(엔진 내부 계산 로직은 로지스틱/랜덤 판정을 그대로 유지).
    /// </summary>
    public readonly struct TeamPowerModifiers
    {
        /// <summary>[TASK-KBO-039 확정 수치] 매치 생성 시 홈팀 ConditionBuff에 부여할 기본 홈 어드밴티지.
        /// 호출부(LeagueManager 등)의 BuildTeamPowerModifiers가 이 상수를 참조해 매직넘버를 피한다.</summary>
        public const int HomeAdvantageConditionBuff = 2;

        public readonly int SynergyBuff;
        public readonly int ConditionBuff;
        public readonly float ClutchMultiplier;
        /// <summary>[TASK-KBO-172] 세트덱 버프 구간 중 "모든 능력치" 균등 가산(SynergyBuff에 이미 포함) 외의
        /// 대상 한정 효과(타자/투수 전용, 타순, 선택 연도, 선발/불펜, 부분 스탯). null이면 효과 없음.</summary>
        public readonly SetDeckBuffProfile SetDeckProfile;
        /// <summary>[TASK-KBO-180] 치어리더 6인 편성 효과(유저 구단 전용, 없으면 null). 상황(홈/연패/열세/후반 접전)별 적용은
        /// MatchEngine이 타석마다 판정한다.</summary>
        public readonly CheerSquadEffects Cheer;
        /// <summary>[TASK-KBO-183] 이 팀의 구단 OVR(TeamOvrCalculator - 기본 + 팀 시너지). 'OVR 7 격차 법칙' 판정에 쓴다.
        /// 0이면 MatchEngine이 로스터로 직접 계산한다(시너지는 최다 구단 기준 세트덱만).</summary>
        public readonly int TeamOvr;
        public int TotalBuff => SynergyBuff + ConditionBuff;

        public TeamPowerModifiers(int synergyBuff, int conditionBuff = 0, float clutchMultiplier = 1.0f,
            SetDeckBuffProfile setDeckProfile = null, CheerSquadEffects cheer = null, int teamOvr = 0)
        {
            SynergyBuff = synergyBuff;
            ConditionBuff = conditionBuff;
            ClutchMultiplier = clutchMultiplier;
            SetDeckProfile = setDeckProfile;
            Cheer = cheer;
            TeamOvr = teamOvr;
        }

        /// <summary>[TASK-KBO-172] 세트덱 평가 결과로 보정치를 만든다 - SynergyBuff = "모든 능력치" 누적합,
        /// SetDeckProfile = 그 외 대상 한정 효과. 세 경기 진입 호출부(LeagueManager/PlayBallController/
        /// PostSeasonManager)가 공통으로 쓴다.</summary>
        public static TeamPowerModifiers FromSetDeck(SetDeckResult setDeck, int conditionBuff, float clutchMultiplier,
            CheerSquadEffects cheer = null, int teamOvr = 0) =>
            new TeamPowerModifiers(setDeck?.AllPlayersFlatBuff ?? 0, conditionBuff, clutchMultiplier, setDeck?.Profile, cheer, teamOvr);

        /// <summary>버프 없음(0, 0, 1.0f = 클러치 효과 없음). 치어리더/홈 어드밴티지가 없는 호출부
        /// (BatchSimulator 등)가 쓴다.</summary>
        public static readonly TeamPowerModifiers None = new TeamPowerModifiers(0, 0, 1.0f);
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

        // 투수 체력(Stamina): 타석 1회를 상대할 때마다 소모되는 양. 선발/불펜을 다르게 잡았다 - 이전
        // 배치 시뮬레이션(수학적 추적)에서 선발/불펜 모두 동일하게 4를 쓰면 불펜이 단발 등판 + 평일
        // 회복(+15)만으로 거의 매번 100%에 가깝게 리셋돼 30% 페널티 구간이 사실상 발동하지 않는다는
        // 결론이 나왔다 - 불펜만 6으로 올려 "짧고 굵게 쓰면 확실히 지친다"는 체감을 만든다.
        private const int StaminaCostPerBatterFacedStarter = 4;
        private const int StaminaCostPerBatterFacedBullpen = 6;

        // 퀵후크(조기 강판) 기준: 이 둘 중 하나라도 해당하면, 이닝 수와 무관하게 선발을 다음 하프이닝부터
        // 롱릴리프/불펜으로 교체한다. 체력 기준(35%)은 Player.LowStaminaThresholdPercent(30%)의 -15%
        // OVR 페널티 구간보다 살짝 높게 잡아, "페널티를 실제로 맞기 직전에 미리 내린다"는 감독의 판단을
        // 흉내낸다 - 그래서 AI 자동 로테이션에서는 -15% 페널티 구간에 거의 진입하지 않고, 대신 유저가
        // 직접 개입해 무리하게 더 끌고 가는 경우에만 그 페널티를 실제로 감수하게 된다.
        private const float QuickHookStaminaPercent = 0.35f;
        private const int QuickHookRunsAllowedThreshold = 4;

        // SelectPitcherForInning()이 투수를 고를 때 "체력이 이 비율 이상인 투수"를 최우선으로 취급한다.
        private const float MinStaminaPercentToPitch = 0.6f;

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
        // isBatterPositive(TASK-KBO-039): 타자에게 유리한 결과인가 - Strikeout은 direction이 +1이지만
        // "투수 유리 방향"이라 타자 긍정 이벤트가 아니다(direction은 스탯 매치업의 부호일 뿐, 타자 유불리와는
        // 별개 축이다). 득점권 클러치 배율(ClutchMultiplier)은 이 플래그가 true인 결과에만 곱해진다.
        private static readonly (AtBatResult result, float defaultBaseWeight, OutcomeDriver driver, float direction, bool isBatterPositive)[] OutcomeTable =
        {
            (AtBatResult.Strikeout, 0.22f, OutcomeDriver.Strikeout,      +1f, false),
            (AtBatResult.Walk,      0.08f, OutcomeDriver.Walk,           +1f, true),
            (AtBatResult.Groundout, 0.23f, OutcomeDriver.ContactQuality, -1f, false),
            (AtBatResult.Flyout,    0.20f, OutcomeDriver.ContactQuality, -1f, false),
            (AtBatResult.Single,    0.16f, OutcomeDriver.ContactQuality, +1f, true),
            (AtBatResult.Double,    0.06f, OutcomeDriver.Power,          +1f, true),
            (AtBatResult.Triple,    0.01f, OutcomeDriver.Power,          +1f, true),
            (AtBatResult.HomeRun,   0.04f, OutcomeDriver.Power,          +1f, true),
        };

        // config가 없을 때 쓰는 기본값. EngineConfig.PopulateDefaults()의 값과 동일하게 맞춰 둔다.
        private const float DefaultSkillInfluence = 0.6f;
        private const float DefaultStatDiffNormalizer = 50f;

        // TASK-KBO-037: 팀 버프 가산 후 세부 스탯이 0 이하로 떨어지지 않도록 하는 하한선(7항 방어 코드).
        private const int MinEffectiveStatValue = 1;


        private readonly SkillDB skillDB;
        private readonly EngineConfig config;
        private readonly Random random;

        // TASK-KBO-037: 경기 시작 전 호출부가 구성해 주입하는 "경기 적용 전력" 보정치. 엔진은 이 값을
        // 그대로 세부 스탯에 가산할 뿐, 절대 스스로 계산하지 않는다(계산 책임은 GameManager.CalculateSynergy
        // 등 호출부에 있다). 레거시 EvaluateSetDeckBonus()(로스터 내 최다 구단 5명 이상 -> 배율 1.15배)는
        // 이 필드로 완전히 대체되어 삭제됨 - GameManager/LeagueManager 쪽의 파편화된 세트덱 판정과의
        // 계산 충돌을 막기 위함.
        private readonly TeamPowerModifiers homeModifiers;
        private readonly TeamPowerModifiers awayModifiers;

        private float SkillInfluence => config != null ? config.SkillInfluence : DefaultSkillInfluence;
        private float StatDiffNormalizer => config != null ? config.StatDiffNormalizer : DefaultStatDiffNormalizer;

        // 스킬이 섞이지 않은 순수 OVR(강화/각성만 반영 - 세트덱 배율은 TASK-KBO-037에서 제거됨). 투수
        // 로테이션 정렬과, "패기"류 스킬의 OVR 비교 조건(GDD: 상대 스킬 효과로 인한 OVR 증가 제외)에 사용한다.
        private readonly Dictionary<Player, int> baseOvrCache = new Dictionary<Player, int>();

        // 이번 경기에서 투수별로 누적 허용한 실점. 퀵후크(조기 강판) 판정에 쓴다 - 경기 전체 누적이며
        // 이닝별로 나뉘어 있지 않다(간단한 "대량 실점" 기준이라 이 정도 단순화로 충분하다고 봤다).
        private readonly Dictionary<Player, int> runsAllowedByPitcher = new Dictionary<Player, int>();

        // 양 팀의 28인 로스터 원본. 생성자에서 주입받아 경기 내내 이 엔진 인스턴스가 직접 소유한다.
        // (원본 리스트 자체는 복사하지만 Player 참조는 공유 - "얕은 복사"로 호출부의 리스트 변형으로부터
        // 격리하면서도 GameManager.Roster 등이 들고 있는 실제 카드 인스턴스와 동일 객체를 가리키게 유지한다.)
        // SubstituteBatter/SubstitutePitcher가 "이 팀 로스터에 실제로 있는 선수인가"를 검증하는 데 쓰인다.
        private readonly List<Player> homeRoster;
        private readonly List<Player> awayRoster;

        /// <summary>
        /// homeRoster/awayRoster(각 28인)는 필수다 - 이 엔진 인스턴스가 진행할 경기의 양 팀 전체 로스터이며,
        /// 선수 교체(SubstituteBatter/SubstitutePitcher) 시 "이 팀 소속이 맞는가"를 검증하는 유일한 근거가 된다.
        /// homeModifiers/awayModifiers(TASK-KBO-037)는 "경기 적용 전력" 보정치 - 호출부가 경기 시작 전
        /// GameManager.CalculateSynergy() 등으로 미리 계산해 주입해야 한다(엔진은 계산하지 않는다).
        /// 버프가 필요 없으면 TeamPowerModifiers.None을 넘기면 된다.
        /// skillDB/config는 선택 사항이다(없으면 각각 스킬 보정 없이, 코드 기본 상수로 진행한다).
        /// randomSeed를 지정하면 결과 재현이 가능하다.
        /// </summary>
        public MatchEngine(List<Player> homeRoster, List<Player> awayRoster,
            TeamPowerModifiers homeModifiers, TeamPowerModifiers awayModifiers,
            SkillDB skillDB = null, EngineConfig config = null, int? randomSeed = null)
        {
            this.homeRoster = new List<Player>(homeRoster ?? new List<Player>());
            this.awayRoster = new List<Player>(awayRoster ?? new List<Player>());
            this.homeModifiers = homeModifiers;
            this.awayModifiers = awayModifiers;
            this.skillDB = skillDB;
            this.config = config;
            random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        }

        private class TeamGameState
        {
            public string TeamName;
            public List<Player> BattingOrder = new List<Player>(); // 최대 9명
            public int NextBatterIndex;

            public List<Player> Starters = new List<Player>();     // [TASK-KBO-187] 1~5선발 칸 순서(라인업 투수 탭과 동일)
            public Player DesignatedStarter;                       // [TASK-KBO-187] 오늘의 선발(StartingRotation - NEXT MATCH 예고와 동일)
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

        /// <summary>[TASK-KBO-183] 경기 시작 시 판정한 양 팀 구단 OVR과 체급 우위 가산(OvrGapLaw).</summary>
        public int HomeTeamOvr { get; private set; }
        public int AwayTeamOvr { get; private set; }
        private int homeClassBonus;
        private int awayClassBonus;
        public int HomeClassBonus => homeClassBonus;
        public int AwayClassBonus => awayClassBonus;
        private int ClassBonusFor(Player player) => player != null && homeRoster.Contains(player) ? homeClassBonus : awayClassBonus;

        /// <summary>[TASK-KBO-187] player 소속 팀의 현재 리드(음수 = 열세).</summary>
        private int LeadOf(Player player)
        {
            if (homeState == null || awayState == null) return 0;
            bool home = player != null && homeRoster.Contains(player);
            return home ? homeState.RunsScored - awayState.RunsScored : awayState.RunsScored - homeState.RunsScored;
        }

        /// <summary>[TASK-KBO-180] 타석 번호 -> 작전. 같은 시드(randomSeed)로 다시 계산하면 지정 타석 직전까지는 결과가 같고, 그 타석부터
        /// 작전이 실제 확률에 반영된다(PlayBallController.ReplayWithTactic).</summary>
        public Dictionary<int, MatchTactic> Tactics { get; } = new Dictionary<int, MatchTactic>();
        private int plateAppearanceCounter;
        private MatchTactic currentTactic = MatchTactic.None;
        private const double StealSuccessChance = 0.72; // KBO 리그 도루 성공률 근사(약 70%대)
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
        /// 로스터는 생성자에서 이미 주입받았으므로 여기서는 팀 이름/포스트시즌 여부만 받는다.
        /// isPostSeason은 "가을사나이" 등 포스트시즌 조건부 스킬 판정에 쓰인다.
        /// </summary>
        public void BeginMatch(string homeTeamName = "Home", string awayTeamName = "Away", bool isPostSeason = false)
        {
            homeState = BuildTeamState(homeRoster, homeTeamName);
            awayState = BuildTeamState(awayRoster, awayTeamName);
            homeState.DesignatedStarter = HomeDesignatedStarter;
            awayState.DesignatedStarter = AwayDesignatedStarter;

            // [TASK-KBO-183] 'OVR 7 격차 법칙' - ΔOVR ≤ 7이면 가변 승부(보정 없음), ≥ 8이면 상위 구단에 체급 우위 세부 스탯 가산.
            HomeTeamOvr = homeModifiers.TeamOvr > 0 ? homeModifiers.TeamOvr : TeamOvrCalculator.Calculate(homeRoster).Total;
            AwayTeamOvr = awayModifiers.TeamOvr > 0 ? awayModifiers.TeamOvr : TeamOvrCalculator.Calculate(awayRoster).Total;
            homeClassBonus = OvrGapLaw.ClassAdvantageBonus(HomeTeamOvr, AwayTeamOvr);
            awayClassBonus = OvrGapLaw.ClassAdvantageBonus(AwayTeamOvr, HomeTeamOvr);
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

            var battingRoster = isTopHalf ? awayRoster : homeRoster;
            if (!battingRoster.Contains(newBatter)) return false; // 이 팀 28인 로스터 소속이 아니면 거부(엔진 레벨 소속 검증)

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

            var pitchingRoster = isTopHalf ? homeRoster : awayRoster;
            if (!pitchingRoster.Contains(newPitcher)) return false; // 이 팀 28인 로스터 소속이 아니면 거부(엔진 레벨 소속 검증)

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

            // [TASK-KBO-180] 이 타석의 작전(승부처 직접 플레이). 투수 교체는 타석 전에 실행한다.
            int plateAppearance = plateAppearanceCounter++;
            currentTactic = Tactics.TryGetValue(plateAppearance, out var tactic) ? tactic : MatchTactic.None;
            string tacticNote = null;
            if (currentTactic == MatchTactic.PitchingChange) tacticNote = TryTacticalPitchingChange();

            bool wasTopHalf = isTopHalf; // AdvanceAfterHalfInning()이 isTopHalf를 바꾸기 전에 이 타석 시점 값을 보존
            var pitcherForThisAtBat = currentHalfInningPitcher; // AdvanceAfterHalfInning()이 다음 하프이닝 투수로 바꾸기 전에 보존

            var batter = GetNextBatter(battingTeam);
            var preAtBatMovements = new List<RunnerMovement>();
            if (currentTactic == MatchTactic.Steal) tacticNote = ResolveSteal(currentAtBatState, preAtBatMovements);
            currentAtBatState.ResetCount();
            RollPitchCount(currentAtBatState);

            var result = SimulateAtBat(batter, pitcherForThisAtBat, currentAtBatState);

            // [TASK-KBO-187] 대승 스코어 감쇠 - 큰 리드 · 빅이닝 · 두 자릿수 득점 중인 공격 팀의 안타/볼넷을 일정 확률로 범타 처리(53:0 방지).
            if (currentTactic == MatchTactic.None && IsBatterPositive(result)) // 작전(고의사구 등) 결과는 건드리지 않는다
            {
                var attack = isTopHalf ? awayState : homeState;
                var defense = isTopHalf ? homeState : awayState;
                float damping = OvrGapLaw.BlowoutDampingChance(attack.RunsScored - defense.RunsScored, attack.RunsScored, runsThisHalfInning);
                if (damping > 0f && random.NextDouble() < damping) result = result == AtBatResult.Walk ? AtBatResult.Strikeout : AtBatResult.Flyout;
            }

            // 이 타석을 던진 대가로 체력을 소모한다. 선발/불펜 롤에 따라 소모량이 다르다(불펜이 더 큼 -
            // 짧고 굵게 쓰는 만큼 더 빨리 지친다). SimulateAtBat()이 이미 "이번 타석 시작 시점"의 체력을
            // 기준으로 페널티(IsLowStamina) 여부를 판정한 뒤이므로, 소모는 그 판정 이후에 반영해야
            // "이번 타석 도중 지쳐서 이번 타석 결과에도 소급 적용되는" 부자연스러움이 없다.
            bool pitcherIsStarter = LineupAssignment.RoleOf(pitcherForThisAtBat) == PitcherRole.StartingPitcher; // [TASK-KBO-186] 맞교환 보직
            pitcherForThisAtBat.ConsumeStamina(pitcherIsStarter ? StaminaCostPerBatterFacedStarter : StaminaCostPerBatterFacedBullpen);

            // 로그의 "[3회초 2사 1,3루]" 부분은 배터가 타석에 "들어선 시점"의 상황이어야 하므로,
            // ResolveAtBatEffect()가 아웃/주자를 바꾸기 직전에 별도로 스냅샷을 떠 둔다.
            var situationBeforePlay = currentAtBatState.Clone();
            int outsBeforePlay = currentAtBatState.Outs;

            var (runs, runnerMovements) = ResolveAtBatEffect(result, currentAtBatState);
            bool isDoublePlay = result == AtBatResult.Groundout && currentAtBatState.Outs - outsBeforePlay == 2;

            // 퀵후크(조기 강판) 판정용 누적 실점. 이닝 로테이션 로직(SelectPitcherForInning)이 다음
            // 하프이닝을 고를 때 이 값을 읽는다.
            if (runs > 0)
            {
                runsAllowedByPitcher.TryGetValue(pitcherForThisAtBat, out int runsSoFar);
                runsAllowedByPitcher[pitcherForThisAtBat] = runsSoFar + runs;
            }

            // AdvanceAfterHalfInning()이 아래에서 currentAtBatState를 다음 하프이닝용 새 객체로 교체할 수 있으므로,
            // "이 타석 종료 직후" 상태도 독립 스냅샷으로 떠 둔다 - AtBatStepResult.State/하이라이트 조건이 이걸 쓴다.
            var stateSnapshot = currentAtBatState.Clone();
            string logMessage = MatchLogger.BuildLog(situationBeforePlay, wasTopHalf, batter, result, runs, isDoublePlay);
            if (currentTactic != MatchTactic.None)
            {
                // [TASK-KBO-180] 작전 표기 - 번트 + 땅볼은 희생번트(주자 진루는 ResolveAtBatEffect가 처리).
                string note = currentTactic == MatchTactic.Bunt && result == AtBatResult.Groundout && situationBeforePlay.HasAnyRunner
                    ? "희생번트 성공"
                    : tacticNote;
                logMessage = $"[작전: {TacticLabel(currentTactic)}{(string.IsNullOrEmpty(note) ? "" : " - " + note)}] {logMessage}";
            }

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
                Pitcher = pitcherForThisAtBat,
                Result = result,
                RunsScoredThisPlay = runs,
                HalfInningEnded = halfInningEnded,
                GameEnded = IsGameOver,
                State = stateSnapshot,
                HomeScore = Result.HomeTotalScore,
                AwayScore = Result.AwayTotalScore,
                IsTopHalf = wasTopHalf,
                LogMessage = logMessage,
                RunnerMovements = runnerMovements,
                PlateAppearance = plateAppearance,
                Tactic = ConsumeTactic(),
                PreAtBatMovements = preAtBatMovements,
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
        /// [디버그/QA 전용] 진행 중인 경기를 즉시 강제 종료하고 winningTeamName을 승자로 확정한다.
        /// 실제 시뮬레이션 규칙을 전혀 따르지 않는다(스코어를 임의로 조작할 뿐) - DebugPanelUI의
        /// "현재 경기 승리로 강제 종료" 버튼 외에는 호출하지 말 것. FinishGame()이 이미 갖고 있는
        /// "동점이면 무승부" 판정 로직을 그대로 재사용하기 위해, 직접 WinnerTeamName을 대입하는 대신
        /// 승자 쪽 점수를 패자보다 1점 많게만 맞춰 두고 FinishGame()에게 판정을 맡긴다.
        /// </summary>
        public void DebugForceEndGame(string winningTeamName)
        {
            if (IsGameOver) return;

            if (winningTeamName == homeTeamName && Result.HomeTotalScore <= Result.AwayTotalScore)
            {
                Result.HomeTotalScore = Result.AwayTotalScore + 1;
            }
            else if (winningTeamName == awayTeamName && Result.AwayTotalScore <= Result.HomeTotalScore)
            {
                Result.AwayTotalScore = Result.HomeTotalScore + 1;
            }

            FinishGame();
        }

        /// <summary>
        /// 생성자에서 주입받은 양 팀의 28인 로스터로 1회부터 9회까지(동점 시 연장 최대 12회) 경기를
        /// 즉시 시뮬레이션한다. 내부적으로 BeginMatch() + PlayNextAtBat() 반복 호출과 완전히 동일한
        /// 규칙을 사용한다("빠른 진행" 모드용).
        /// </summary>
        public MatchResult PlayFullMatch(string homeTeamName = "Home", string awayTeamName = "Away", bool isPostSeason = false)
        {
            BeginMatch(homeTeamName, awayTeamName, isPostSeason);

            while (!IsGameOver)
            {
                PlayNextAtBat();
            }

            return Result;
        }

        /// <summary>
        /// GDD v4.0 2.5D 중계 뷰 전용 진입점. PlayFullMatch()와 동일한 규칙으로 경기 전체를 내부적으로
        /// 즉시 시뮬레이션하되, 결과를 UI로 그때그때 흘려보내지 않고 발생한 모든 이벤트(타석 결과 +
        /// 주자 이동 + 하프이닝/경기 종료)를 순서대로 Queue&lt;PlayEvent&gt;에 담아 마지막에 한 번에 반환한다.
        /// BroadcastUIManager 등 UI 계층은 이 큐를 하나씩 Dequeue하며 텍스트 로그 타이핑 + 미니맵 주자
        /// 애니메이션을 재생하면 된다. 기존 PlayNextAtBat()/PlayFullMatch() 스텝 API는 대체 없이 그대로
        /// 유지된다(대타/투수 교체 등 수동 개입이 필요한 화면이 아직 이를 참조하기 때문).
        /// </summary>
        public Queue<PlayEvent> PlayFullMatchAsEventQueue(string homeTeamName = "Home", string awayTeamName = "Away", bool isPostSeason = false)
        {
            var queue = new Queue<PlayEvent>();
            BeginMatch(homeTeamName, awayTeamName, isPostSeason);

            while (!IsGameOver)
            {
                var step = PlayNextAtBat();
                if (step.Batter == null && step.Pitcher == null && step.GameEnded)
                {
                    break; // 로스터 이상 등으로 더 진행할 타자/투수가 전혀 없는 극단적 상황의 안전장치
                }

                var (flavorBalls, flavorStrikes) = RollFlavorCount(step.Result, random);

                foreach (var movement in step.PreAtBatMovements)
                {
                    queue.Enqueue(new PlayEvent
                    {
                        Type = PlayEventType.RunnerAdvance,
                        Inning = step.State?.Inning ?? currentInning,
                        IsTopHalf = step.IsTopHalf,
                        HomeScore = step.HomeScore - (step.IsTopHalf ? 0 : step.RunsScoredThisPlay),
                        AwayScore = step.AwayScore - (step.IsTopHalf ? step.RunsScoredThisPlay : 0),
                        FromBase = movement.FromBase,
                        ToBase = movement.ToBase,
                        PlateAppearance = step.PlateAppearance,
                        IsSteal = true,
                    });
                }

                queue.Enqueue(new PlayEvent
                {
                    Type = PlayEventType.AtBatResult,
                    Inning = step.State?.Inning ?? currentInning,
                    IsTopHalf = step.IsTopHalf,
                    HomeScore = step.HomeScore,
                    AwayScore = step.AwayScore,
                    Batter = step.Batter,
                    Pitcher = step.Pitcher,
                    Result = step.Result,
                    Outs = step.State?.Outs ?? 0,
                    RunsScoredThisPlay = step.RunsScoredThisPlay,
                    LogMessage = step.LogMessage,
                    Balls = flavorBalls,
                    Strikes = flavorStrikes,
                    PlateAppearance = step.PlateAppearance,
                    Tactic = step.Tactic,
                });

                foreach (var movement in step.RunnerMovements)
                {
                    queue.Enqueue(new PlayEvent
                    {
                        Type = PlayEventType.RunnerAdvance,
                        Inning = step.State?.Inning ?? currentInning,
                        IsTopHalf = step.IsTopHalf,
                        HomeScore = step.HomeScore,
                        AwayScore = step.AwayScore,
                        FromBase = movement.FromBase,
                        ToBase = movement.ToBase,
                        PlateAppearance = step.PlateAppearance,
                    });
                }

                if (step.HalfInningEnded && !step.GameEnded)
                {
                    queue.Enqueue(new PlayEvent
                    {
                        Type = PlayEventType.HalfInningEnd,
                        Inning = step.State?.Inning ?? currentInning,
                        IsTopHalf = step.IsTopHalf,
                        HomeScore = step.HomeScore,
                        AwayScore = step.AwayScore,
                    });
                }
            }

            queue.Enqueue(new PlayEvent
            {
                Type = PlayEventType.GameEnd,
                Inning = currentInning,
                HomeScore = Result.HomeTotalScore,
                AwayScore = Result.AwayTotalScore,
            });

            return queue;
        }

        /// <summary>
        /// [TASK-KBO-047] PlayEvent.Balls/Strikes에 담을 연출용(Flavor) 카운트를 결과와 모순되지 않는
        /// 범위에서 무작위로 만든다. 이미 확정된 result를 "포장"만 할 뿐 - 이 값 자체가 result에
        /// 영향을 주거나 어떤 확률 계산에도 관여하지 않는다(SimulateAtBat()의 판정 로직과는 완전히
        /// 무관). 삼진은 반드시 3스트라이크, 볼넷은 반드시 4볼이어야 앞뒤가 맞으므로 그 결과만 고정값을
        /// 쓰고, 나머지(안타/범타/뜬공 등)는 0~2스트라이크/0~3볼 범위에서 자유롭게 채운다.
        /// </summary>
        private static (int balls, int strikes) RollFlavorCount(AtBatResult result, Random rng)
        {
            switch (result)
            {
                case AtBatResult.Strikeout:
                    return (rng.Next(0, 3), 3); // 0~2B, 확정 3S
                case AtBatResult.Walk:
                    return (4, rng.Next(0, 3)); // 확정 4B, 0~2S
                default:
                    return (rng.Next(0, 4), rng.Next(0, 3)); // 0~3B, 0~2S
            }
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

            // [TASK-KBO-180] 작전 - 고의사구는 볼넷 확정, 번트는 희생번트 위주의 별도 분포(타자 정확도가 번트 안타/실패에 영향).
            if (currentTactic == MatchTactic.IntentionalWalk) return AtBatResult.Walk;
            if (currentTactic == MatchTactic.Bunt) return RollBunt(batter);

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

            // TASK-KBO-039: 득점권(2루 또는 3루 주자)이면 공격 팀(타자)의 ClutchMultiplier를 타자
            // 긍정 이벤트(OutcomeTable.isBatterPositive)의 가중치에만 곱한다. 수비 팀(투수)의 위기 탈출
            // 배율은 이번 스코프에 포함되지 않는다(7항) - 오직 타자 쪽에만 적용한다.
            bool isScoringPosition = state.HasRunnerInScoringPosition;
            float batterClutchMultiplier = GetModifiersFor(batter).ClutchMultiplier;

            // [TASK-KBO-180] 6. 위기 응원 - 후반(7회~) 접전(2점 차 이내)이면 타자 긍정 결과 배율, 후반 득점권이면 카드 고유 클러치 배율.
            var batterCheer = GetModifiersFor(batter).Cheer;
            float closeLateMultiplier = 1f;
            if (batterCheer != null && state.Inning >= batterCheer.CloseLateFromInning)
            {
                if (Mathf.Abs(TeamScoreDiff(batter)) <= batterCheer.CloseLateMaxDiff) closeLateMultiplier *= batterCheer.CloseLateMultiplier;
                if (isScoringPosition) batterClutchMultiplier = Mathf.Max(batterClutchMultiplier, batterCheer.LateRispMultiplier);
            }

            var weights = new float[OutcomeTable.Length];
            float total = 0f;
            for (int i = 0; i < OutcomeTable.Length; i++)
            {
                var (result, defaultBaseWeight, driver, direction, isBatterPositive) = OutcomeTable[i];
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
                float weight = baseWeight * multiplier;

                if (isScoringPosition && isBatterPositive && batterClutchMultiplier > 1f)
                {
                    weight *= batterClutchMultiplier; // 기존 로지스틱/가중치 계산 결과에 단순 곱셈으로만 개입
                }
                if (isBatterPositive && closeLateMultiplier > 1f) weight *= closeLateMultiplier; // [TASK-KBO-180] 위기 응원

                weights[i] = weight;
                total += weight;
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

        // ----- [TASK-KBO-180] 작전/치어리더 상황 판정 헬퍼 -----

        /// <summary>player 소속 팀 기준 현재 점수차(+ 리드 / - 열세).</summary>
        private int TeamScoreDiff(Player player)
        {
            if (Result == null) return 0;
            bool isHome = player != null && homeRoster.Contains(player);
            return isHome ? Result.HomeTotalScore - Result.AwayTotalScore : Result.AwayTotalScore - Result.HomeTotalScore;
        }

        private MatchTactic ConsumeTactic()
        {
            var used = currentTactic;
            currentTactic = MatchTactic.None;
            return used;
        }

        public static string TacticLabel(MatchTactic tactic)
        {
            switch (tactic)
            {
                case MatchTactic.PowerSwing: return "강공";
                case MatchTactic.ContactSwing: return "컨택";
                case MatchTactic.Bunt: return "번트";
                case MatchTactic.Steal: return "도루";
                case MatchTactic.FullForce: return "정면 승부";
                case MatchTactic.PitchingChange: return "투수 교체";
                case MatchTactic.IntentionalWalk: return "고의사구";
                default: return "일반";
            }
        }

        /// <summary>번트 분포: 희생번트(땅볼 처리) / 번트 안타 / 번트 실패 삼진 / 뜬공. 타자 정확이 높을수록 성공·안타가 늘어난다.</summary>
        private AtBatResult RollBunt(Player batter)
        {
            float contact = batter?.GetEffectiveBatterStats().Contact ?? 50;
            double skill = Mathf.Clamp((contact - 50f) / 100f, -0.2f, 0.3f);
            double single = 0.1 + skill * 0.15, strikeout = 0.1 - skill * 0.1, flyout = 0.1 - skill * 0.05;
            double roll = random.NextDouble();
            if (roll < single) return AtBatResult.Single;
            if (roll < single + strikeout) return AtBatResult.Strikeout;
            if (roll < single + strikeout + flyout) return AtBatResult.Flyout;
            return AtBatResult.Groundout;
        }

        /// <summary>도루: 1루 주자만 있고 2아웃 미만일 때 2루 도루 판정(성공률 StealSuccessChance). 그 외 상황은 시도하지 않는다.</summary>
        private string ResolveSteal(MatchState state, List<RunnerMovement> movements)
        {
            if (!state.RunnerOnFirst || state.RunnerOnSecond || state.Outs >= 2) return "도루 시도 불가 상황";
            state.RunnerOnFirst = false;
            if (random.NextDouble() < StealSuccessChance)
            {
                state.RunnerOnSecond = true;
                movements.Add(new RunnerMovement(1, 2));
                return "도루 성공";
            }
            state.Outs++;
            movements.Add(new RunnerMovement(1, -1));
            return "도루 실패";
        }

        /// <summary>투수 교체: 수비 팀 가용 투수(교체되지 않았고 체력이 남은) 중 마무리(8회 이후) → 승리조 → 롱릴리프 → 추격조 순, 같은 그룹은 OVR 순.</summary>
        private string TryTacticalPitchingChange()
        {
            var team = isTopHalf ? homeState : awayState;
            if (team == null) return null;
            IEnumerable<Player> ordered = currentInning >= 8
                ? team.Closers.Concat(team.WinningRelief).Concat(team.LongRelief).Concat(team.MopUpRelief)
                : team.WinningRelief.Concat(team.LongRelief).Concat(team.Closers).Concat(team.MopUpRelief);
            var pick = ordered.Where(p => p != null && p != currentHalfInningPitcher && !subbedOutList.Contains(p) && p.CurrentStamina > 0)
                .OrderByDescending(p => (float)p.CurrentStamina / Mathf.Max(1, p.MaxStamina) >= MinStaminaPercentToPitch)
                .FirstOrDefault();
            if (pick == null) return "가용 불펜 없음";
            return SubstitutePitcher(pick) ? $"{pick.Template.PlayerName} 등판" : "교체 실패";
        }

        // ----- 팀 상태 구성 -----

        /// <summary>[TASK-KBO-187] 오늘의 선발 투수(LeagueManager.GetNextStartingPitcher - NEXT MATCH 예고와 같은 투수). BeginMatch 전에 지정한다.
        /// null이면 기존처럼 1~5선발 중 체력이 가장 넉넉한 투수가 나온다.</summary>
        public Player HomeDesignatedStarter { get; set; }
        public Player AwayDesignatedStarter { get; set; }

        private TeamGameState BuildTeamState(List<Player> roster, string teamName)
        {
            var valid = (roster ?? new List<Player>()).Where(p => p?.Template != null).ToList();

            foreach (var player in valid)
            {
                // 세트덱 배율은 TASK-KBO-037에서 제거됨(팀 버프는 이제 homeModifiers/awayModifiers를 통한
                // 균등 가산으로만 반영된다) - 순수 강화/각성만 반영한 OVR.
                baseOvrCache[player] = player.CalculateOVR(false);
            }

            return new TeamGameState
            {
                TeamName = teamName,
                BattingOrder = BuildBattingOrder(valid),
                // [TASK-KBO-187] 투수 운용 = 라인업 투수 탭 13칸 그대로(유저 고정 자리 반영, AI는 보직별 OVR 순): 선발 = 1~5선발 칸 순서,
                // 불펜 그룹 = 그 칸의 보직(대체 배치 포함). 정원 밖 "추가" 투수는 자기 보직 그룹 뒤에 붙는다.
                Starters = PitchingStaff(valid).Rotation,
                LongRelief = PitchingStaff(valid).Group(PitcherRole.LongReliever),
                WinningRelief = PitchingStaff(valid).Group(PitcherRole.WinningReliever),
                MopUpRelief = PitchingStaff(valid).Group(PitcherRole.MopUpReliever),
                Closers = PitchingStaff(valid).Group(PitcherRole.Closer),
            };
        }

        private sealed class Staff
        {
            public List<Player> Rotation;
            public List<LineupView.Entry> Bullpen;
            public List<Player> Group(PitcherRole role) => Bullpen.Where(e => e.Player != null && e.Role == role).Select(e => e.Player).ToList();
        }

        private readonly Dictionary<List<Player>, Staff> staffCache = new Dictionary<List<Player>, Staff>();

        private Staff PitchingStaff(List<Player> valid)
        {
            if (staffCache.TryGetValue(valid, out var staff)) return staff;
            var (rotation, bullpen) = LineupView.BuildPitchers(valid);
            staff = new Staff { Rotation = rotation.Where(e => e.Player != null).Select(e => e.Player).ToList(), Bullpen = bullpen };
            staffCache[valid] = staff;
            return staff;
        }

        private static List<Player> BuildBattingOrder(List<Player> teamRoster)
        {
            // [TASK-KBO-186] 포지션당 1명(유저 맞교환 고정 → OVR 최우선) + 빈 포지션은 남은 타자 OVR 상위로 대체(LineupAssignment - 라인업 화면과 같은 기준).
            var order = LineupAssignment.DefaultBattingOrder(teamRoster);

            // [TASK-KBO-182] 라인업 [타순 변경] 유저 지정 타순 - 유저 구단 InstanceId에만 매칭되므로 AI 로스터는 그대로다.
            return LineupOrder.Apply(order, KBOManager.Managers.GameManager.Instance != null ? KBOManager.Managers.GameManager.Instance.BattingOrderOverride : null);
        }

        // ----- 세부 스탯 계산(팀 버프 + 스킬 효과 반영) -----

        private static bool IsBatterPositive(AtBatResult result) =>
            result == AtBatResult.Walk || result == AtBatResult.Single || result == AtBatResult.Double || result == AtBatResult.Triple || result == AtBatResult.HomeRun;

        private int GetBaseOvr(Player player)
        {
            if (player == null) return 0;
            return baseOvrCache.TryGetValue(player, out var cached) ? cached : player.CalculateOVR(false);
        }

        /// <summary>player가 homeRoster 소속이면 homeModifiers를, 그 외(awayRoster 소속 등)에는
        /// awayModifiers를 반환한다. 두 로스터는 생성자에서 서로 다른 리스트로 주입받으므로 교집합이
        /// 없다는 전제 하에 동작한다(경기당 한 MatchEngine 인스턴스가 한 경기만 진행한다는 기존 전제와 동일).</summary>
        private TeamPowerModifiers GetModifiersFor(Player player)
        {
            return player != null && homeRoster.Contains(player) ? homeModifiers : awayModifiers;
        }

        /// <summary>
        /// 최종 스탯 = Base + Growth + SkillBonus + 팀 버프(TASK-KBO-037: TotalBuff를 세부 스탯 전항목에
        /// 균등 가산). 레거시 세트덱 배율(곱셈)은 삭제되었다 - 이제 팀 단위 보정은 전부 이 가산 항목
        /// 하나로 통일된다. 버프가 음수(향후 페널티)여도 최종 스탯이 MinEffectiveStatValue(1) 밑으로
        /// 떨어지지 않도록 클램핑한다.
        /// </summary>
        private BatterStats ResolveEffectiveBatterStats(Player batter, Player pitcher, MatchState state)
        {
            var stats = batter.GetEffectiveBatterStats(); // Base + Growth
            if (batter.ConditionStatBonus != 0) stats = AddTeamBuff(stats, batter.ConditionStatBonus); // [TASK-KBO-184] 컨디션 = 경기 안 가변 요소

            // 본인이 보유한 Target=Self 스킬
            stats = ApplyBatterSkills(stats, batter, EffectTarget.Self, self: batter, opponent: pitcher, state);
            // 상대 투수가 보유한 Target=Opponent 스킬(나를 겨냥한 효과) - 조건 판정은 스킬 소유자(투수) 기준
            stats = ApplyBatterSkills(stats, pitcher, EffectTarget.Opponent, self: pitcher, opponent: batter, state);

            var batterModifiers = GetModifiersFor(batter);
            // [TASK-KBO-183] 체급 우위(Δ ≥ 8) - [TASK-KBO-187] 타격 쪽은 리드가 클수록 감쇠(OvrGapLaw.BattingClassBonus)
            stats = AddTeamBuff(stats, batterModifiers.TotalBuff + OvrGapLaw.BattingClassBonus(ClassBonusFor(batter), LeadOf(batter)));
            if (batterModifiers.SetDeckProfile != null)
            {
                // [TASK-KBO-172] 세트덱 대상 한정 효과(타순 1~9 기준 상위/중심/하위 타선 등)를 추가 가산한다.
                stats = AddTeamBuff(stats, batterModifiers.SetDeckProfile.GetBatterBonus(batter, BattingOrderSlotOf(batter)));
            }

            var cheer = batterModifiers.Cheer;
            if (cheer != null)
            {
                // [TASK-KBO-180] 치어리더 6인 편성: 응원단장(세트덱 적용률 보강분) + 직접 보정(스탯별 합계 DirectStatCap 상한).
                // 전 스탯(홈 응원·연패 대응·응원단장 보강) 합계는 GeneralStatCap, 담당 스탯(정확·선구)은 DirectStatCap까지.
                int all = cheer.LosingStreakBonus + cheer.HomeAllStatsBonus + cheer.SetDeckAmplifyBonus;
                int contactEye = all + cheer.BatterContactDiscipline
                    + (TeamScoreDiff(batter) <= -cheer.TrailingThreshold ? cheer.TrailingBatterBonus : 0);
                int a = CheerSquadEffects.CapGeneral(all), ce = CheerSquadEffects.Cap(contactEye);
                stats = AddTeamBuff(stats, new BatterStats(a, ce, ce, a, a));
            }

            // [TASK-KBO-180] 공격 작전(강공/컨택) - 이번 타석만.
            if (currentTactic == MatchTactic.PowerSwing) stats = AddTeamBuff(stats, new BatterStats(8, -3, -4, 0, 0));
            else if (currentTactic == MatchTactic.ContactSwing) stats = AddTeamBuff(stats, new BatterStats(-6, 6, 2, 0, 0));

            return stats;
        }

        /// <summary>
        /// ResolveEffectiveBatterStats와 동일한 연산 순서를 투수 스탯에 적용하되, 체력 페널티(곱셈)는
        /// 스킬 보정 이전 단계에 적용해 스킬 효과 자체의 절댓값이 깎이지 않게 한다. 팀 버프(가산)는
        /// ResolveEffectiveBatterStats와 동일하게 맨 마지막에 더한다.
        /// </summary>
        private PitcherStats ResolveEffectivePitcherStats(Player pitcher, Player batter, MatchState state)
        {
            var stats = pitcher.GetEffectivePitcherStats(); // Base + Growth
            if (pitcher.ConditionStatBonus != 0) stats = AddTeamBuff(stats, pitcher.ConditionStatBonus); // [TASK-KBO-184] 컨디션 = 경기 안 가변 요소

            if (pitcher.IsLowStamina)
            {
                stats = Scale(stats, 1f - Player.LowStaminaOvrPenaltyPercent); // 체력 30% 미만: -15%
            }

            stats = ApplyPitcherSkills(stats, pitcher, EffectTarget.Self, self: pitcher, opponent: batter, state);
            stats = ApplyPitcherSkills(stats, batter, EffectTarget.Opponent, self: batter, opponent: pitcher, state);

            var pitcherModifiers = GetModifiersFor(pitcher);
            stats = AddTeamBuff(stats, pitcherModifiers.TotalBuff + ClassBonusFor(pitcher)); // [TASK-KBO-183] 체급 우위(Δ ≥ 8)
            if (pitcherModifiers.SetDeckProfile != null)
            {
                stats = AddTeamBuff(stats, pitcherModifiers.SetDeckProfile.GetPitcherBonus(pitcher)); // [TASK-KBO-172]
            }

            var cheer = pitcherModifiers.Cheer;
            if (cheer != null)
            {
                // [TASK-KBO-180] 치어리더 6인 편성(투수 쪽): 응원단장 + 직접 보정(스탯별 DirectStatCap 상한).
                int all = cheer.LosingStreakBonus + cheer.HomeAllStatsBonus + cheer.SetDeckAmplifyBonus;
                int a = CheerSquadEffects.CapGeneral(all), cs = CheerSquadEffects.Cap(all + cheer.PitcherControlStuff);
                stats = AddTeamBuff(stats, new PitcherStats(cs, a, a, cs, a));
            }
            // [TASK-KBO-180] 5. 홈 응원(상대 팀) - 팬 압박으로 이 투수의 제구가 떨어진다.
            var opponentCheer = GetModifiersFor(batter).Cheer;
            if (opponentCheer != null && opponentCheer.OpponentControlPenalty > 0)
            {
                stats = AddTeamBuff(stats, new PitcherStats(0, 0, 0, -opponentCheer.OpponentControlPenalty, 0));
            }

            // [TASK-KBO-180] 수비 작전(정면 승부) - 이번 타석만.
            if (currentTactic == MatchTactic.FullForce) stats = AddTeamBuff(stats, new PitcherStats(5, 4, 0, -3, 0));

            return stats;
        }

        // TASK-KBO-037: 팀 버프(N)를 세부 스탯 전항목에 균등 가산한다. 7항 방어 코드 - 음수 버프(향후
        // 페널티 도입 시)로 스탯이 0 이하로 떨어지지 않도록 MinEffectiveStatValue(1)로 클램핑한다.
        // [TASK-KBO-089] operator+(Types.cs, TASK-088)로 5개 필드를 한 번에 가산한 뒤 클램핑만 필드별로
        // 적용한다 - Speed/Defense가 이전에는 여기서 0으로 유실되던 결함을 해소했다.
        private static BatterStats AddTeamBuff(BatterStats stats, int buff)
        {
            var buffed = stats + new BatterStats(buff, buff, buff, buff, buff);
            return new BatterStats(
                Mathf.Max(MinEffectiveStatValue, buffed.Power),
                Mathf.Max(MinEffectiveStatValue, buffed.Contact),
                Mathf.Max(MinEffectiveStatValue, buffed.Discipline),
                Mathf.Max(MinEffectiveStatValue, buffed.Speed),
                Mathf.Max(MinEffectiveStatValue, buffed.Defense));
        }

        /// <summary>[TASK-KBO-172] 세트덱 대상 한정 효과처럼 스탯마다 값이 다른 가산 - 균등 가산 버전과 같은
        /// MinEffectiveStatValue 클램핑을 적용한다.</summary>
        private static BatterStats AddTeamBuff(BatterStats stats, BatterStats bonus)
        {
            var buffed = stats + bonus;
            return new BatterStats(
                Mathf.Max(MinEffectiveStatValue, buffed.Power),
                Mathf.Max(MinEffectiveStatValue, buffed.Contact),
                Mathf.Max(MinEffectiveStatValue, buffed.Discipline),
                Mathf.Max(MinEffectiveStatValue, buffed.Speed),
                Mathf.Max(MinEffectiveStatValue, buffed.Defense));
        }

        private static PitcherStats AddTeamBuff(PitcherStats stats, PitcherStats bonus)
        {
            var buffed = stats + bonus;
            return new PitcherStats(
                Mathf.Max(MinEffectiveStatValue, buffed.Stuff),
                Mathf.Max(MinEffectiveStatValue, buffed.Velocity),
                Mathf.Max(MinEffectiveStatValue, buffed.Movement),
                Mathf.Max(MinEffectiveStatValue, buffed.Control),
                Mathf.Max(MinEffectiveStatValue, buffed.Stamina));
        }

        /// <summary>[TASK-KBO-172] 타자의 현재 타순(1~9). 대타 교체 시 해당 슬롯을 그대로 이어받는다. 라인업에
        /// 없으면 0(타순 조건부 세트덱 효과 미적용).</summary>
        private int BattingOrderSlotOf(Player batter)
        {
            int index = homeState?.BattingOrder.IndexOf(batter) ?? -1;
            if (index < 0) index = awayState?.BattingOrder.IndexOf(batter) ?? -1;
            return index >= 0 ? index + 1 : 0;
        }

        private static PitcherStats AddTeamBuff(PitcherStats stats, int buff)
        {
            var buffed = stats + new PitcherStats(buff, buff, buff, buff, buff);
            return new PitcherStats(
                Mathf.Max(MinEffectiveStatValue, buffed.Stuff),
                Mathf.Max(MinEffectiveStatValue, buffed.Velocity),
                Mathf.Max(MinEffectiveStatValue, buffed.Movement),
                Mathf.Max(MinEffectiveStatValue, buffed.Control),
                Mathf.Max(MinEffectiveStatValue, buffed.Stamina));
        }

        // [TASK-KBO-089] 배율(곱셈)은 operator+로 표현할 수 없어 기존과 동일하게 필드별로 직접 계산하되,
        // Speed/Defense/Stamina를 누락 없이 포함하도록 갱신했다.
        private static BatterStats Scale(BatterStats stats, float multiplier) => new BatterStats(
            Mathf.RoundToInt(stats.Power * multiplier),
            Mathf.RoundToInt(stats.Contact * multiplier),
            Mathf.RoundToInt(stats.Discipline * multiplier),
            Mathf.RoundToInt(stats.Speed * multiplier),
            Mathf.RoundToInt(stats.Defense * multiplier));

        private static PitcherStats Scale(PitcherStats stats, float multiplier) => new PitcherStats(
            Mathf.RoundToInt(stats.Stuff * multiplier),
            Mathf.RoundToInt(stats.Velocity * multiplier),
            Mathf.RoundToInt(stats.Movement * multiplier),
            Mathf.RoundToInt(stats.Control * multiplier),
            Mathf.RoundToInt(stats.Stamina * multiplier));

        /// <summary>
        /// skillOwner가 보유한 스킬 중 지정한 wantedTarget(Self/Opponent)에 해당하고 조건을 만족하는 것만
        /// 골라 stats(타자 스탯)에 적용한다. 조건은 항상 스킬 소유자(self) 기준으로 평가한다.
        /// </summary>
        private BatterStats ApplyBatterSkills(BatterStats stats, Player skillOwner, EffectTarget wantedTarget,
            Player self, Player opponent, MatchState state)
        {
            if (skillDB == null || skillOwner?.Template == null) return stats;

            // 소유자의 실제 카테고리(타자/선발/불펜) 풀에서만 조회한다 - "패기"/"마당쇠"처럼 동일 이름의
            // 스킬이 다른 카테고리 풀에 다른 효과로도 존재하는 경우, 이름만으로 전체 풀을 검색하면
            // 소유자와 무관한(예: 투수용) 효과가 잘못 붙을 수 있다. AI 세대교체/스킬 재추첨 등으로 저장된
            // 스킬 이름이 더 이상 소유자 카테고리와 맞지 않게 되어도(구버전 세이브 등) FindSkill이 null을
            // 반환해 아래에서 조용히 스킵된다 - 크래시나 잘못된 스탯 적용 없이 무시.
            var ownerCategory = SkillDB.ResolveCategory(skillOwner.Template);

            foreach (var skillName in skillOwner.AcquiredSkillIds)
            {
                var effect = skillDB.FindSkill(skillName, ownerCategory)?.Effect;
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
            if (skillDB == null || skillOwner?.Template == null) return stats;

            // ApplyBatterSkills와 동일한 이유로 소유자의 실제 카테고리(선발/불펜) 풀에서만 조회한다.
            var ownerCategory = SkillDB.ResolveCategory(skillOwner.Template);

            foreach (var skillName in skillOwner.AcquiredSkillIds)
            {
                var effect = skillDB.FindSkill(skillName, ownerCategory)?.Effect;
                if (effect == null || effect.Target != wantedTarget) continue;
                if (!IsConditionMet(effect.Condition, self, opponent, state)) continue;

                foreach (var modifier in effect.Modifiers)
                {
                    stats = ApplyModifier(stats, modifier.Stat, modifier.Value);
                }
            }

            return stats;
        }

        // [TASK-KBO-089] 5-인자 생성자로 나머지 필드를 그대로 보존(stats.Speed/stats.Defense 등)하며 지정된
        // 한 필드만 값을 더한다 - StatType에 없는 케이스는 아래 `_ => stats`가 그대로 흡수해 예외를 던지지
        // 않는다(AC-03, 명령서 7항).
        private static BatterStats ApplyModifier(BatterStats stats, StatType stat, int value) => stat switch
        {
            StatType.Power => new BatterStats(stats.Power + value, stats.Contact, stats.Discipline, stats.Speed, stats.Defense),
            StatType.Contact => new BatterStats(stats.Power, stats.Contact + value, stats.Discipline, stats.Speed, stats.Defense),
            StatType.Discipline => new BatterStats(stats.Power, stats.Contact, stats.Discipline + value, stats.Speed, stats.Defense),
            StatType.Speed => new BatterStats(stats.Power, stats.Contact, stats.Discipline, stats.Speed + value, stats.Defense),
            StatType.Defense => new BatterStats(stats.Power, stats.Contact, stats.Discipline, stats.Speed, stats.Defense + value),
            _ => stats // 투수 전용 StatType이 잘못 설정된 경우 무시
        };

        private static PitcherStats ApplyModifier(PitcherStats stats, StatType stat, int value) => stat switch
        {
            StatType.Stuff => new PitcherStats(stats.Stuff + value, stats.Velocity, stats.Movement, stats.Control, stats.Stamina),
            StatType.Velocity => new PitcherStats(stats.Stuff, stats.Velocity + value, stats.Movement, stats.Control, stats.Stamina),
            StatType.Movement => new PitcherStats(stats.Stuff, stats.Velocity, stats.Movement + value, stats.Control, stats.Stamina),
            StatType.Control => new PitcherStats(stats.Stuff, stats.Velocity, stats.Movement, stats.Control + value, stats.Stamina),
            StatType.Stamina => new PitcherStats(stats.Stuff, stats.Velocity, stats.Movement, stats.Control, stats.Stamina + value),
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
        /// 타석 1회의 결과를 state(아웃/주자)에 반영하고, 이 플레이로 발생한 득점 수와 실제 주자 이동
        /// 목록(PlayEvent 변환용)을 함께 반환한다. 병살타/희생플라이 판정이 여기 포함된다.
        /// </summary>
        private (int runs, List<RunnerMovement> movements) ResolveAtBatEffect(AtBatResult result, MatchState state)
        {
            bool isOut = result == AtBatResult.Strikeout || result == AtBatResult.Groundout || result == AtBatResult.Flyout;

            if (currentTactic == MatchTactic.Bunt && result == AtBatResult.Groundout && state.HasAnyRunner)
            {
                // [TASK-KBO-180] 희생번트: 타자 아웃, 모든 주자 한 루씩 진루(병살 없음).
                var moves = new List<RunnerMovement>();
                int scored = 0;
                if (state.RunnerOnThird) { moves.Add(new RunnerMovement(3, 4)); scored++; }
                if (state.RunnerOnSecond) moves.Add(new RunnerMovement(2, 3));
                if (state.RunnerOnFirst) moves.Add(new RunnerMovement(1, 2));
                state.RunnerOnThird = state.RunnerOnSecond;
                state.RunnerOnSecond = state.RunnerOnFirst;
                state.RunnerOnFirst = false;
                state.Outs++;
                if (state.Outs >= 3) { scored = 0; moves.RemoveAll(m => m.ToBase == 4); } // 3아웃이면 득점 불인정
                return (scored, moves);
            }

            if (!isOut)
            {
                return AdvanceRunners(result, state);
            }

            if (result == AtBatResult.Flyout && state.RunnerOnThird && state.Outs < 2)
            {
                // 희생플라이: 3루 주자 생환, 배터만 아웃 처리
                state.RunnerOnThird = false;
                state.Outs++;
                return (1, new List<RunnerMovement> { new RunnerMovement(3, 4) });
            }

            if (result == AtBatResult.Groundout && state.RunnerOnFirst && state.Outs < 2
                && random.NextDouble() < DoublePlayChance)
            {
                // 병살타: 배터 + 1루 주자 아웃 (2아웃 동시 소모), 다른 주자는 그대로
                state.RunnerOnFirst = false;
                state.Outs += 2;
                // [TASK-KBO-047] 1루 주자가 사라졌다는 사실을 PlayEvent로 흘려보내 UI(MatchStatusUI)가
                // 1루 다이아몬드를 비울 수 있도록, "베이스 1에서 아웃(-1)"으로 기록한다(RunnerMovement.cs
                // 주석 참고). 판정 확률(DoublePlayChance)이나 아웃 수 계산 등 시뮬레이션 로직 자체는
                // 위 두 줄에서 이미 끝났고, 이 한 줄은 그 결과를 UI로 전달하기 위한 포장일 뿐이다.
                return (0, new List<RunnerMovement> { new RunnerMovement(1, -1) });
            }

            state.Outs++;
            return (0, new List<RunnerMovement>());
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

        /// <summary>체력이 MinStaminaPercentToPitch(60%) 이상인 투수. 타자/체력 데이터가 없는 카드는 항상 true를 반환해
        /// (필터로 인해 아예 후보가 사라지는 사고를 막는다) 안전하게 우회한다.</summary>
        private static bool HasSufficientStamina(Player pitcher)
        {
            if (pitcher?.Template == null || !pitcher.Template.IsPitcher || pitcher.MaxStamina <= 0) return true;
            return (float)pitcher.CurrentStamina / pitcher.MaxStamina >= MinStaminaPercentToPitch;
        }

        /// <summary>
        /// candidates 중에서 "아직 이 경기에 등판하지 않았고 체력도 충분한" 투수를 최우선으로,
        /// 그다음 "아직 등판하지 않은" 투수를, 그래도 없으면 아무나(순서 유지)를 반환한다.
        /// 이 3단계 우선순위 하나로 SelectPitcherForInning() 전체(선호 롤 후보/전체 폴백 후보)를 통일해서 처리한다.
        /// </summary>
        private static Player PickByStaminaThenUsage(List<Player> candidates, HashSet<Player> usedPitchers)
        {
            return candidates.FirstOrDefault(p => !usedPitchers.Contains(p) && HasSufficientStamina(p))
                ?? candidates.FirstOrDefault(p => !usedPitchers.Contains(p))
                ?? candidates.FirstOrDefault();
        }

        /// <summary>
        /// 선발이 QuickHookStaminaPercent(35%) 미만이거나 QuickHookRunsAllowedThreshold(4점) 이상을
        /// 이미 내줬다면 더 이상 믿고 맡기지 않는다(조기 강판/퀵후크) - 이닝 수와 무관하다.
        /// </summary>
        private bool ShouldPullStarter(Player starter)
        {
            if (starter == null || starter.MaxStamina <= 0) return true;
            if ((float)starter.CurrentStamina / starter.MaxStamina < QuickHookStaminaPercent) return true;

            runsAllowedByPitcher.TryGetValue(starter, out int runsAllowed);
            return runsAllowed >= QuickHookRunsAllowedThreshold;
        }

        /// <summary>
        /// 유연화된 불펜 운용 규칙: 선발은 더 이상 "이닝 1~5"라는 고정 상한 없이, 퀵후크 조건
        /// (ShouldPullStarter - 체력 35% 미만 또는 4실점 이상)에 걸리기 전까지는 6~8회까지도 계속
        /// 던질 수 있다. 일단 강판되면(또는 애초에 대량 실점으로 조기 강판되면) 그 시점 이닝을 기준으로
        /// 롱릴리프(~6회)/승리·추격조(7~8회)/마무리·추격조(9회+)로 넘어간다.
        /// 체력 기반 우선순위(PickByStaminaThenUsage - 체력 60% 이상 우선, 그다음 미사용, 그다음 아무나)는
        /// 불펜 후보를 고를 때 그대로 유지한다. SubbedOutList에 등록된(유저가 명시적으로 강판시킨) 투수는
        /// 이 자동 로테이션에서도 절대 재선택되지 않는다 - UsedPitchers(소프트 선호도)/체력(소프트
        /// 우선순위)과 별개로 SubbedOutList는 하드 제외 규칙이다.
        /// </summary>
        private Player SelectPitcherForInning(TeamGameState pitchingTeam, TeamGameState battingTeam, int inning)
        {
            int scoreDiff = pitchingTeam.RunsScored - battingTeam.RunsScored; // 0 이상이면 투수팀이 동점 이상

            List<Player> preferred;
            if (inning >= RegulationInnings)
            {
                preferred = scoreDiff >= 0 ? pitchingTeam.Closers : pitchingTeam.MopUpRelief;
            }
            else
            {
                var startedToday = pitchingTeam.Starters.FirstOrDefault(p => pitchingTeam.UsedPitchers.Contains(p))
                                   ?? (pitchingTeam.DesignatedStarter != null && pitchingTeam.UsedPitchers.Contains(pitchingTeam.DesignatedStarter) ? pitchingTeam.DesignatedStarter : null);

                if (startedToday != null && !subbedOutList.Contains(startedToday) && !ShouldPullStarter(startedToday))
                {
                    // 아직 믿고 맡길 만하다 - 이닝 상한 없이 계속 그 선발로 이어간다.
                    preferred = new List<Player> { startedToday };
                }
                else if (startedToday == null && inning <= 5 && pitchingTeam.DesignatedStarter != null && !subbedOutList.Contains(pitchingTeam.DesignatedStarter))
                {
                    // [TASK-KBO-187] 1~5선발 순차 로테이션 - NEXT MATCH에 예고된 바로 그 투수가 선발 등판한다.
                    preferred = new List<Player> { pitchingTeam.DesignatedStarter };
                }
                else if (startedToday == null && inning <= 5 && pitchingTeam.Starters.Count > 0)
                {
                    // 이번 경기에서 아직 아무도 선발로 등판하지 않은, 경기 최초의 투수 결정. 체력이 가장
                    // 넉넉한 선발이 우선 선택되도록 Starters 전체를 후보로 넘긴다 - 이게 곧 "선발 5명을
                    // 강제로 돌려쓰게 만드는" 로테이션이다(기존에는 항상 Starters[0](최고 OVR)만 고정으로
                    // 선발 등판했다 - 체력과 무관하게 매 경기 동일 인물이 선발이었던 것을 여기서 고쳤다).
                    preferred = pitchingTeam.Starters;
                }
                else if (inning <= 6)
                {
                    // 선발이 이미 강판됐거나(퀵후크), 6회 진입 - 롱릴리프가 이어받는다.
                    preferred = pitchingTeam.LongRelief;
                }
                else // 7~8회, 선발은 이미 내려간 상태
                {
                    preferred = scoreDiff >= 0 ? pitchingTeam.WinningRelief : pitchingTeam.MopUpRelief;
                }
            }

            var eligiblePreferred = preferred.Where(p => !subbedOutList.Contains(p)).ToList();
            var pick = PickByStaminaThenUsage(eligiblePreferred, pitchingTeam.UsedPitchers);

            if (pick == null)
            {
                // [Fallback] 선호 롤에 가용 투수가 없으면 전체 투수 풀에서 체력 우선 -> 미사용 -> 재사용 순으로 배정한다.
                var allPitchers = pitchingTeam.Starters
                    .Concat(pitchingTeam.LongRelief)
                    .Concat(pitchingTeam.WinningRelief)
                    .Concat(pitchingTeam.MopUpRelief)
                    .Concat(pitchingTeam.Closers)
                    .Where(p => !subbedOutList.Contains(p))
                    .ToList();

                pick = PickByStaminaThenUsage(allPitchers, pitchingTeam.UsedPitchers);

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

        /// <summary>타석 결과에 따라 state의 주자를 이동시키고, 이번 타석에서 발생한 득점 수와
        /// 실제 발생한 주자 이동(타자 본인 포함) 목록을 함께 반환한다.</summary>
        private static (int runs, List<RunnerMovement> movements) AdvanceRunners(AtBatResult result, MatchState state)
        {
            int runs = 0;
            var movements = new List<RunnerMovement>();

            switch (result)
            {
                case AtBatResult.Walk:
                    bool forcedHome = state.RunnerOnFirst && state.RunnerOnSecond && state.RunnerOnThird;
                    bool forceToThird = state.RunnerOnFirst && state.RunnerOnSecond;
                    bool forceToSecond = state.RunnerOnFirst;
                    if (forcedHome) { runs++; movements.Add(new RunnerMovement(3, 4)); }
                    if (forceToThird) { state.RunnerOnThird = true; movements.Add(new RunnerMovement(2, 3)); }
                    if (forceToSecond) { state.RunnerOnSecond = true; movements.Add(new RunnerMovement(1, 2)); }
                    state.RunnerOnFirst = true;
                    movements.Add(new RunnerMovement(0, 1)); // 타자 1루 진루(볼넷)
                    break;

                case AtBatResult.Single:
                {
                    bool thirdScores = state.RunnerOnThird;
                    bool secondToThird = state.RunnerOnSecond;
                    bool firstToSecond = state.RunnerOnFirst;
                    if (thirdScores) { runs++; movements.Add(new RunnerMovement(3, 4)); }
                    state.RunnerOnThird = state.RunnerOnSecond;
                    if (secondToThird) movements.Add(new RunnerMovement(2, 3));
                    state.RunnerOnSecond = state.RunnerOnFirst;
                    if (firstToSecond) movements.Add(new RunnerMovement(1, 2));
                    state.RunnerOnFirst = true;
                    movements.Add(new RunnerMovement(0, 1));
                    break;
                }

                case AtBatResult.Double:
                {
                    bool thirdScores = state.RunnerOnThird;
                    bool secondScores = state.RunnerOnSecond;
                    bool firstToThird = state.RunnerOnFirst; // 단순화: 1루 주자는 3루에서 멈춘다고 가정
                    if (thirdScores) { runs++; movements.Add(new RunnerMovement(3, 4)); }
                    if (secondScores) { runs++; movements.Add(new RunnerMovement(2, 4)); }
                    state.RunnerOnThird = state.RunnerOnFirst;
                    if (firstToThird) movements.Add(new RunnerMovement(1, 3));
                    state.RunnerOnSecond = true;
                    state.RunnerOnFirst = false;
                    movements.Add(new RunnerMovement(0, 2));
                    break;
                }

                case AtBatResult.Triple:
                {
                    bool thirdScores = state.RunnerOnThird;
                    bool secondScores = state.RunnerOnSecond;
                    bool firstScores = state.RunnerOnFirst;
                    if (thirdScores) { runs++; movements.Add(new RunnerMovement(3, 4)); }
                    if (secondScores) { runs++; movements.Add(new RunnerMovement(2, 4)); }
                    if (firstScores) { runs++; movements.Add(new RunnerMovement(1, 4)); }
                    state.RunnerOnThird = true;
                    state.RunnerOnSecond = false;
                    state.RunnerOnFirst = false;
                    movements.Add(new RunnerMovement(0, 3));
                    break;
                }

                case AtBatResult.HomeRun:
                    if (state.RunnerOnThird) movements.Add(new RunnerMovement(3, 4));
                    if (state.RunnerOnSecond) movements.Add(new RunnerMovement(2, 4));
                    if (state.RunnerOnFirst) movements.Add(new RunnerMovement(1, 4));
                    runs = 1 + (state.RunnerOnFirst ? 1 : 0) + (state.RunnerOnSecond ? 1 : 0) + (state.RunnerOnThird ? 1 : 0);
                    state.RunnerOnFirst = state.RunnerOnSecond = state.RunnerOnThird = false;
                    movements.Add(new RunnerMovement(0, 4)); // 타자 본인 득점(홈런)
                    break;
            }

            return (runs, movements);
        }
    }
}
