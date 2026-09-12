using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// 게임 전역 상태(유저 인벤토리, 1군 로스터, 재화 등)를 관리하는 싱글톤.
    /// 씬 전환 시에도 파괴되지 않는다.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        // GDD: 1군 엔트리는 정확히 타자 15명 + 투수 13명 = 28명이어야 한다.
        public const int RequiredBatterCount = 15;
        public const int RequiredPitcherCount = 13;
        public const int RequiredRosterSize = RequiredBatterCount + RequiredPitcherCount;

        [Header("Inventory / Roster")]
        [SerializeField] private List<Player> inventory = new List<Player>();
        [SerializeField] private List<Player> roster = new List<Player>();
        [SerializeField] private List<Item> itemInventory = new List<Item>();

        /// <summary>유저가 보유한 전체 선수 카드.</summary>
        public IReadOnlyList<Player> Inventory => inventory;

        /// <summary>1군 로스터에 편성된 선수 카드 (최대 타자 15 + 투수 13 = 28).</summary>
        public IReadOnlyList<Player> Roster => roster;

        /// <summary>유저가 보유한 강화 재료(Item) 카드.</summary>
        public IReadOnlyList<Item> ItemInventory => itemInventory;

        // ----- 유저 프로필 -----
        [Header("Profile")]
        [SerializeField] private Team favoriteTeam = Team.None;
        public Team FavoriteTeam
        {
            get => favoriteTeam;
            set => favoriteTeam = value;
        }

        [Tooltip("true인 동안은 UIManager가 로비 대신 온보딩(선호 구단 선택) 화면을 강제 출력한다. " +
                 "OnboardingManager.CompleteOnboarding()이 스타터 팩 지급을 마친 뒤 false로 내린다.")]
        [SerializeField] private bool isFirstLogin = true;
        public bool IsFirstLogin
        {
            get => isFirstLogin;
            set => isFirstLogin = value;
        }

        // ----- 치어리더 장착 슬롯 (TASK-KBO-048) -----
        [Header("Cheerleader")]
        [Tooltip("유저가 장착한 치어리더 1명(v0.1은 단일 슬롯). null이면 '미장착' 상태 - 경기 조건부 " +
                 "버프(ConditionBuff/ClutchMultiplier)가 전혀 적용되지 않는다. 가챠/획득/세이브 시스템은 " +
                 "이번 작업 범위 밖이라, 실제 장착 UI가 생기기 전까지는 아래 에디터 전용 더미 데이터나 " +
                 "인스펙터 직접 할당으로만 값이 채워진다.")]
        [SerializeField] private Cheerleader equippedCheerleader;
        public Cheerleader EquippedCheerleader
        {
            get => equippedCheerleader;
            set => equippedCheerleader = value;
        }

#if UNITY_EDITOR
        [Tooltip("[에디터 전용] true면 Awake() 시 EquippedCheerleader가 비어 있을 때만 테스트용 치어리더를 " +
                 "자동 장착한다. '미장착(null)' 상태를 그대로 테스트하고 싶다면 이 토글을 꺼 두면 된다 - " +
                 "이 필드와 관련 로직 전체가 #if UNITY_EDITOR로 감싸여 있어 릴리스 빌드에는 포함되지 않는다.")]
        [SerializeField] private bool devAutoEquipTestCheerleader = true;
#endif

        // ----- 재화 -----
        [Header("Currency")]
        [SerializeField] private int scoutReport;     // 스카우트 리포트 (뽑기 재화)
        [SerializeField] private int premiumCurrency;  // 프리미엄 재화
        [SerializeField] private int gameGold;         // 게임 머니

        public int ScoutReport
        {
            get => scoutReport;
            set => scoutReport = Mathf.Max(0, value);
        }

        public int PremiumCurrency
        {
            get => premiumCurrency;
            set => premiumCurrency = Mathf.Max(0, value);
        }

        public int GameGold
        {
            get => gameGold;
            set => gameGold = Mathf.Max(0, value);
        }

        // ----- 팬심 / 연패 (TASK-KBO-049, 치어리더 B안 결산 연동용) -----
        [Header("Fan Sentiment / Losing Streak")]
        [Tooltip("[TASK-KBO-050] 0~100 범위로 정규화된 팬심 수치. 기본값 100(최상)에서 시작해 유저 팀 " +
                 "연패 시(MatchRewardManager) 하락한다. 50 미만이면 MatchRewardManager.GrantRewardForMatch() " +
                 "가 홈 경기 기본 보상에 0.8배 페널티를 적용한다(15_team_power_policy.md 참고). " +
                 "상승 요인/다른 소모처는 v0.1 범위 밖이라 아직 없다.")]
        [SerializeField] private int fanSentiment = 100;
        public int FanSentiment
        {
            get => fanSentiment;
            set => fanSentiment = Mathf.Clamp(value, 0, 100);
        }

        [Tooltip("유저 팀의 현재 연속 패배 횟수. MatchRewardManager.GrantRewardForMatch()가 매 경기 " +
                 "종료 시 갱신한다(승리 또는 무승부 시 0으로 리셋). 0 미만으로는 내려가지 않는다.")]
        [SerializeField] private int losingStreak;
        public int LosingStreak
        {
            get => losingStreak;
            set => losingStreak = Mathf.Max(0, value);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

#if UNITY_EDITOR
            InitializeDevOnlyTestCheerleader();
            LogCheerleaderBuffSelfCheck();
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// [TASK-KBO-048][에디터 전용] devAutoEquipTestCheerleader가 켜져 있고 EquippedCheerleader가
        /// 비어 있을 때만 개발/QA 검증용 치어리더를 1명 장착시킨다 - 인스펙터나 세이브 로드로 이미
        /// 값이 채워져 있으면 절대 덮어쓰지 않는다(요구사항 3항 가드레일). 릴리스 빌드에서는 이
        /// 메서드 자체가 컴파일되지 않는다(#if UNITY_EDITOR).
        /// </summary>
        private void InitializeDevOnlyTestCheerleader()
        {
            if (!devAutoEquipTestCheerleader) return;
            if (equippedCheerleader != null) return;

            equippedCheerleader = new Cheerleader(
                instanceId: "DEV_TEST_CHEER_001",
                name: "[개발용] 테스트 치어리더",
                grade: CheerleaderGrade.TEST,
                conditionBuff: 1,
                clutchMultiplier: 1.05f,
                economicBonusRate: 1.2f,
                sentimentDefense: 3);
        }

        /// <summary>
        /// [TASK-KBO-048][에디터 전용][검증용] 프로젝트에 Unity Test Framework 어셈블리(EditMode Test용
        /// asmdef)가 아직 없어(요구사항 9항 "분리 불가능 시" 경로를 택함), ResolveCheerleaderConditionBuff()/
        /// ResolveCheerleaderClutchMultiplier() 순수 정적 함수에 AC-01~AC-04, AC-06 시나리오를 직접
        /// 대입해 콘솔에 기대값과 함께 출력한다. 실제 EquippedCheerleader 등 게임 상태는 전혀 건드리지
        /// 않는 격리된 셀프체크이며, 매치 생성 파이프라인(BuildTeamPowerModifiers)과는 별도로 이 두
        /// 정적 함수 자체의 입출력만 검증한다.
        /// </summary>
        private static void LogCheerleaderBuffSelfCheck()
        {
            var equipped = new Cheerleader("SELFCHECK", "SelfCheck", CheerleaderGrade.TEST, conditionBuff: 3, clutchMultiplier: 1.2f);

            Debug.Log($"[TASK-KBO-048 SelfCheck] AC-01(유저 홈+장착): ConditionBuff={ResolveCheerleaderConditionBuff(true, equipped)}(기대 3), " +
                $"ClutchMultiplier={ResolveCheerleaderClutchMultiplier(true, equipped)}(기대 1.2)");

            Debug.Log($"[TASK-KBO-048 SelfCheck] AC-02(유저 홈+미장착): ConditionBuff={ResolveCheerleaderConditionBuff(true, null)}(기대 0), " +
                $"ClutchMultiplier={ResolveCheerleaderClutchMultiplier(true, null)}(기대 1)");

            Debug.Log($"[TASK-KBO-048 SelfCheck] AC-03(유저 원정+장착): ConditionBuff={ResolveCheerleaderConditionBuff(false, equipped)}(기대 0), " +
                $"ClutchMultiplier={ResolveCheerleaderClutchMultiplier(false, equipped)}(기대 1)");

            // AC-04(AI 홈): 팀 식별 자체는 각 매니저의 BuildTeamPowerModifiers가 책임지고, 이 정적
            // 함수는 그 판별 결과(isUserTeamHome)만 입력으로 받는다 - "AI 팀"이라는 조건은 여기서
            // isUserTeamHome=false로 표현되며 입력 형태상 AC-03과 동일하다(둘 다 0/1.0 기대).
            Debug.Log($"[TASK-KBO-048 SelfCheck] AC-04(AI 홈, isUserTeamHome=false로 표현): ConditionBuff={ResolveCheerleaderConditionBuff(false, equipped)}(기대 0)");

            var nanCheer = new Cheerleader("SELFCHECK_NAN", "NaN", CheerleaderGrade.TEST, 0, float.NaN);
            var infCheer = new Cheerleader("SELFCHECK_INF", "Inf", CheerleaderGrade.TEST, 0, float.PositiveInfinity);
            var zeroCheer = new Cheerleader("SELFCHECK_ZERO", "Zero", CheerleaderGrade.TEST, 0, 0f);
            var negCheer = new Cheerleader("SELFCHECK_NEG", "Neg", CheerleaderGrade.TEST, 0, -5f);
            Debug.Log("[TASK-KBO-048 SelfCheck] AC-06(방어적 설계, 모두 기대값 1): " +
                $"NaN->{ResolveCheerleaderClutchMultiplier(true, nanCheer)}, " +
                $"Infinity->{ResolveCheerleaderClutchMultiplier(true, infCheer)}, " +
                $"0->{ResolveCheerleaderClutchMultiplier(true, zeroCheer)}, " +
                $"음수->{ResolveCheerleaderClutchMultiplier(true, negCheer)}");
        }
#endif

        // ----- 인벤토리/로스터 헬퍼 -----

        /// <summary>새로 획득한 선수를 인벤토리에 추가한다.</summary>
        public void AddPlayerToInventory(Player player)
        {
            if (player == null) return;
            inventory.Add(player);
        }

        /// <summary>인벤토리의 선수를 1군 로스터에 편성한다. 타자 15 / 투수 13 정원을 초과할 수 없다.</summary>
        public bool AddPlayerToRoster(Player player)
        {
            if (player == null || player.Template == null) return false;
            if (!inventory.Contains(player)) return false;
            if (roster.Contains(player)) return false;

            int batterCount = roster.Count(p => !p.Template.IsPitcher);
            int pitcherCount = roster.Count(p => p.Template.IsPitcher);

            if (player.Template.IsPitcher && pitcherCount >= RequiredPitcherCount) return false;
            if (!player.Template.IsPitcher && batterCount >= RequiredBatterCount) return false;

            roster.Add(player);
            return true;
        }

        /// <summary>1군 로스터에서 선수를 제외한다.</summary>
        public bool RemovePlayerFromRoster(Player player)
        {
            return player != null && roster.Remove(player);
        }

        /// <summary>인벤토리에서 선수를 제거한다. (각성 재료 소모 등으로 카드가 완전히 사라질 때 사용)</summary>
        public bool RemovePlayerFromInventory(Player player)
        {
            if (player == null) return false;
            roster.Remove(player); // 로스터에 편성돼 있었다면 유령 참조가 남지 않도록 함께 제거
            return inventory.Remove(player);
        }

        /// <summary>오토 라인업 등으로 새로 계산된 28인 리스트를 로스터에 그대로 덮어쓴다.</summary>
        public void OverwriteRoster(List<Player> newRoster)
        {
            roster.Clear();
            if (newRoster != null) roster.AddRange(newRoster);
        }

        /// <summary>인벤토리 전체를 교체한다. (SaveManager의 로드 복원 전용)</summary>
        public void ReplaceInventory(List<Player> players)
        {
            inventory.Clear();
            if (players != null) inventory.AddRange(players);
        }

        /// <summary>강화 재료(Item) 인벤토리 전체를 교체한다. (SaveManager의 로드 복원 전용)</summary>
        public void ReplaceItemInventory(List<Item> items)
        {
            itemInventory.Clear();
            if (items != null) itemInventory.AddRange(items);
        }

        /// <summary>새로 획득한 강화 재료(Item)를 인벤토리에 추가한다.</summary>
        public void AddItemToInventory(Item item)
        {
            if (item != null) itemInventory.Add(item);
        }

        /// <summary>강화 소모 등으로 재료(Item)를 인벤토리에서 제거한다.</summary>
        public bool RemoveItemFromInventory(Item item)
        {
            return item != null && itemInventory.Remove(item);
        }

        /// <summary>로스터가 타자 15 + 투수 13 = 28명 정원을 정확히 채웠는지 확인한다.</summary>
        public bool IsRosterComplete()
        {
            int batterCount = roster.Count(p => !p.Template.IsPitcher);
            int pitcherCount = roster.Count(p => p.Template.IsPitcher);
            return batterCount == RequiredBatterCount && pitcherCount == RequiredPitcherCount;
        }

        // ----- 팀 OVR (GDD v4.0) -----

        private static readonly BatterPosition[] AllBatterPositions =
            (BatterPosition[])System.Enum.GetValues(typeof(BatterPosition));

        private const int BenchBatterQuota = 4;  // 후보 타자 인원
        private const int BenchReliefQuota = 6;  // 후보 구원(롱/중/셋) 인원

        /// <summary>
        /// 구단 OVR = (주전 15인 평균 OVR * 0.8) + (후보 10인 평균 OVR * 0.2) + 시너지 합산.
        /// 주전 15 = 포지션별 최고 OVR 타자 9 + 선발투수 5 + 마무리 1.
        /// 후보 10 = 나머지 타자 중 OVR 상위 4 + 나머지 구원(승리조/추격조/롱릴리프) 중 OVR 상위 6.
        /// [TASK-KBO-031 공식화] 28인 로스터 중 (28 - 15 - 10 =) 3명(타자 2명, 투수 1명)은 각 그룹
        /// (벤치 타자/구원) 내 OVR 최하위 순으로 자동 제외된다 - 실제 KBO의 28인 등록/25인 경기 엔트리
        /// 구조를 본뜬 공식 스펙으로 확정됨(이 계산 방식 자체는 변경 없음, 기존 구현을 그대로 유지).
        /// 로스터가 비어 있거나 해당 그룹에 아무도 없으면 그 그룹의 평균은 0으로 취급한다(0으로 나누기 방지).
        /// 세트덱 보너스(Player.CalculateOVR의 setDeckBonus)는 로스터 구성 자체가 세트덱 활성화 여부를
        /// 좌우하는 순환 참조를 피하기 위해(RosterManager의 다른 OVR 계산들과 동일하게) 개별 선수 OVR에는
        /// 반영하지 않는다 - 대신 세트덱 시너지는 CalculateSynergy()의 가산 항목으로 별도 처리된다.
        /// [TASK-KBO-033] 25인 가중평균은 시너지를 더하기 "전"에 정수로 반올림한다(반올림 시점 고정 -
        /// 시너지 가산 이후로 옮기지 말 것).
        /// </summary>
        public int CalculateTeamOVR()
        {
            var batters = roster.Where(p => p?.Template != null && !p.Template.IsPitcher).ToList();
            var pitchers = roster.Where(p => p?.Template != null && p.Template.IsPitcher).ToList();

            var starterBatters = new List<Player>();
            foreach (var position in AllBatterPositions)
            {
                var pick = batters
                    .Where(p => p.Template.BatterPosition == position && !starterBatters.Contains(p))
                    .OrderByDescending(p => p.CalculateOVR(false))
                    .FirstOrDefault();
                if (pick != null) starterBatters.Add(pick);
            }

            var benchBatters = batters
                .Except(starterBatters)
                .OrderByDescending(p => p.CalculateOVR(false))
                .Take(BenchBatterQuota)
                .ToList();

            var startingPitchers = pitchers.Where(p => p.Template.PitcherRole == PitcherRole.StartingPitcher).ToList();
            var closers = pitchers.Where(p => p.Template.PitcherRole == PitcherRole.Closer).ToList();

            var benchRelief = pitchers
                .Except(startingPitchers).Except(closers)
                .OrderByDescending(p => p.CalculateOVR(false))
                .Take(BenchReliefQuota)
                .ToList();

            var starters15 = starterBatters.Concat(startingPitchers).Concat(closers).ToList();
            var bench10 = benchBatters.Concat(benchRelief).ToList();

            float starterAvg = starters15.Count > 0 ? (float)starters15.Average(p => p.CalculateOVR(false)) : 0f;
            float benchAvg = bench10.Count > 0 ? (float)bench10.Average(p => p.CalculateOVR(false)) : 0f;

            int baseOvr = Mathf.RoundToInt((starterAvg * 0.8f) + (benchAvg * 0.2f));

            // favoriteTeam이 Team.None(온보딩 이전 등 미지정 상태)이면 null을 넘겨, CalculateSynergy()의
            // "지정 없음 -> 최다 구단 기준" 경로를 그대로 태운다 - AI와 동일한 규칙을 적용하는 셈이라,
            // 특별 취급(항상 0)을 위한 별도 분기가 필요 없다.
            string favoriteTeamName = favoriteTeam != Team.None ? favoriteTeam.ToString() : null;
            int synergy = CalculateSynergy(roster, favoriteTeamName);

            return baseOvr + synergy;
        }

        // TASK-KBO-037 확정 수치: 28인 로스터 내 특정(선호 또는 최다) 구단 소속 선수가 이 인원 이상이면
        // 세트덱 시너지가 발동한다. 유저/AI 공통 기준.
        private const int TeamSynergyThreshold = 15;
        private const int TeamSynergyBonus = 12;

        /// <summary>
        /// 로스터의 세트덱 시너지(+12 또는 0)를 계산하는 정적 유틸리티 - 유저/AI 양쪽에서 공용으로 쓴다.
        ///
        /// [TASK-KBO-037 통폐합] 그동안 세트덱 판정이 세 갈래로 파편화되어 있었다: (1) MatchEngine.
        /// EvaluateSetDeckBonus() - 로스터 내 최다 구단 5명 이상이면 세부 스탯에 배율 1.15배 적용(실제
        /// 경기 판정에 반영됨, 이번에 삭제), (2) GameManager.CheckSetDeckBonus() - 동일 기준(5명, 배율
        /// 1.15배)이지만 로스터 화면(RosterUIController)의 게이지 표시 전용(경기 판정과 무관, 호출부가
        /// 있어 이번 작업에서는 보존 - 완료 보고서 F 섹션 참고), (3) 구 GameManager.CalculateTeamSynergy()
        /// - 15명 기준 +12(유저 전용, AI 미지원). 이 메서드가 (3)을 대체하며 AI까지 포함해 "15명 이상 +12"
        /// 단일 기준으로 통합한다 - 실제 경기 판정(MatchEngine)에 쓰이는 시너지는 이제 이 메서드의
        /// 결과값이 유일한 근거다.
        ///
        /// favoriteTeam을 지정하면(Team.ToString() 형태의 문자열) 그 구단과 일치하는 인원을 센다(유저
        /// 경로). 지정하지 않으면(null/빈 문자열) 로스터 내 가장 많은 비중을 차지하는 구단의 인원을
        /// 센다(AI 경로 - AI는 FavoriteTeam 개념이 없으므로 최다 구단을 기준으로 삼는다). 일치/최다
        /// 인원이 TeamSynergyThreshold(15) 이상이면 TeamSynergyBonus(+12), 아니면 0을 반환한다.
        /// 구단 미지정 선수(Template.Team == Team.None)는 두 경로 모두에서 집계 대상에서 제외한다.
        /// 로스터가 비어 있거나 null이면 0(7항 경계 조건).
        /// </summary>
        public static int CalculateSynergy(List<Player> roster, string favoriteTeam = null)
        {
            if (roster == null || roster.Count == 0) return 0;

            var validPlayers = roster.Where(p => p?.Template != null && p.Template.Team != Team.None).ToList();
            if (validPlayers.Count == 0) return 0;

            int matchingCount = !string.IsNullOrEmpty(favoriteTeam)
                ? validPlayers.Count(p => p.Template.Team.ToString() == favoriteTeam)
                : validPlayers.GroupBy(p => p.Template.Team).Select(g => g.Count()).DefaultIfEmpty(0).Max();

            return matchingCount >= TeamSynergyThreshold ? TeamSynergyBonus : 0;
        }

        // ----- 치어리더 경기 조건부 버프 해석 (TASK-KBO-048) -----

        /// <summary>기본/중립 배율. 치어리더 미장착이거나 조건(유저 팀 + 홈경기) 미충족일 때 이 값을 반환한다.</summary>
        public const float NeutralClutchMultiplier = 1.0f;

        /// <summary>
        /// [TASK-KBO-048] "유저 팀의 홈 경기"(isUserTeamHome)일 때만 장착된 치어리더의 ConditionBuff를
        /// 반환한다(조건 미충족이거나 equippedCheerleader가 null이면 0). 호출부(각 매니저의
        /// BuildTeamPowerModifiers 계열 헬퍼)가 이 값을 기본 홈 어드밴티지(Engine.TeamPowerModifiers.
        /// HomeAdvantageConditionBuff, +2)에 그대로 더하면 된다 - 이 메서드 자체는 시너지/홈버프
        /// 상수를 알지 못하며(GameManager.cs는 Engine 네임스페이스를 참조하지 않는다), 오직 치어리더
        /// 쪽 가산분만 계산해서 넘긴다. 치어리더 효과는 Player.CalculateOVR()/CalculateTeamOVR()
        /// (영구·표시용 구단 OVR)에는 절대 반영되지 않는다(Cheerleader.cs 클래스 주석 참고).
        /// </summary>
        public static int ResolveCheerleaderConditionBuff(bool isUserTeamHome, Cheerleader equippedCheerleader)
        {
            if (!isUserTeamHome || equippedCheerleader == null) return 0;
            return equippedCheerleader.ConditionBuff;
        }

        /// <summary>
        /// [TASK-KBO-048] "유저 팀의 홈 경기"일 때만 장착된 치어리더의 ClutchMultiplier를 반환하고
        /// (조건 미충족/미장착이면 중립값 NeutralClutchMultiplier), MatchEngine에 전달되기 전 비정상
        /// 입력을 반드시 정규화(Sanitize)한다: NaN/Infinity는 곱셈 시 확률 가중치를 각각 깨뜨리거나
        /// (NaN) 무한대로 발산시키므로(Infinity) 단순 Mathf.Max로는 걸러지지 않아 별도로 먼저
        /// 걸러내고, 그 외 0 이하 값은 Mathf.Max(NeutralClutchMultiplier, value)로 중립값 이상으로
        /// 끌어올린다.
        /// [TBD] 코치 등 다른 시너지와 이 값이 중첩될 때의 합산 방식/상한선은 아직 기획 확정 전이다 -
        /// 지금은 치어리더 단독 값만 정규화해서 반환한다(7항 경계 조건).
        /// </summary>
        public static float ResolveCheerleaderClutchMultiplier(bool isUserTeamHome, Cheerleader equippedCheerleader)
        {
            if (!isUserTeamHome || equippedCheerleader == null) return NeutralClutchMultiplier;

            float raw = equippedCheerleader.ClutchMultiplier;
            if (float.IsNaN(raw) || float.IsInfinity(raw)) return NeutralClutchMultiplier;

            return Mathf.Max(NeutralClutchMultiplier, raw);
        }
    }
}
