using System;
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

        /// <summary>[TASK-KBO-056] 치어리더 장착 상태(EquippedCheerleader)가 바뀔 때마다 발생한다.
        /// TeamSynergyUIController 등 UI가 Update() 폴링 없이 이 이벤트만 구독해 갱신할 수 있다.</summary>
        public event Action OnCheerleaderChanged;

        /// <summary>인벤토리에서 고른 치어리더를 정식으로 장착한다. target이 null이면 방어적으로
        /// UnequipCheerleader()를 대신 호출한다.</summary>
        public void EquipCheerleader(Cheerleader target)
        {
            if (target == null)
            {
                UnequipCheerleader();
                return;
            }

            equippedCheerleader = target;
            OnCheerleaderChanged?.Invoke();
        }

        /// <summary>현재 장착된 치어리더를 해제한다(미장착 상태로 되돌림).</summary>
        public void UnequipCheerleader()
        {
            equippedCheerleader = null;
            OnCheerleaderChanged?.Invoke();
        }

        /// <summary>[TASK-KBO-057] 유저가 영구적으로 보유한 치어리더 목록(장착 여부와 무관). 가챠/보상
        /// 등으로 새 치어리더를 얻으면 AddCheerleader()로 여기 추가된다.</summary>
        public List<Cheerleader> OwnedCheerleaders { get; private set; } = new List<Cheerleader>();

        /// <summary>
        /// [TASK-KBO-064/129] 신규 획득한 치어리더를 보유 목록에 추가한다. newCheerleader가 null이면
        /// 아무 일도 하지 않는다. CatalogId(원본 식별자)가 채워져 있고 이미 같은 CatalogId를 가진
        /// 치어리더를 보유 중이면(docs/16_shop_and_gacha_policy.md 4절 A안) 인벤토리에 중복 추가하지
        /// 않고 등급에 대응하는 응원봉 재화로 변환 지급한다. CatalogId가 비어 있으면(카탈로그가 아직
        /// 없던 구버전 더미 데이터 등) 중복 검사 없이 그냥 추가한다.
        /// [결정 필요, TASK-KBO-129] GDD 원문 어디에도 "치어리더 중복 획득 시 재화로 자동 변환"되는
        /// 규칙이 없다 - 이 메커니즘은 docs/16_shop_and_gacha_policy.md 4절(TASK-KBO-064, [Draft])이
        /// 제안한, 기획서에 근거가 없는 AI 고안 장치다. 다만 이 로직을 완전히 제거하면 중복 치어리더가
        /// 무제한으로 인벤토리에 쌓이는 대체 문제가 새로 생기고, GDD도 이 경우의 대안(예: 치어리더
        /// 승급/한계돌파)을 명시하지 않아 무엇으로 대체해야 할지 알 수 없다 - 그래서 삭제하지 않고
        /// 유지하되, 소모처가 이제 등급별 응원봉 4종으로 나뉘었으므로 환급 대상도 그 등급이 실제로
        /// 소모하는 응원봉으로 맞췄다(단일 CheerStick 폐기에 따른 최소 연쇄 수정). 이 메커니즘 자체의
        /// 존치/폐기는 사용자 확인이 필요하다.
        /// </summary>
        public void AddCheerleader(Cheerleader newCheerleader)
        {
            if (newCheerleader == null) return;
            if (OwnedCheerleaders == null) OwnedCheerleaders = new List<Cheerleader>();

            bool hasCatalogId = !string.IsNullOrEmpty(newCheerleader.CatalogId);
            bool isDuplicate = hasCatalogId &&
                OwnedCheerleaders.Any(c => c.CatalogId == newCheerleader.CatalogId);

            if (isDuplicate)
            {
                int convertedAmount = ResolveCheerleaderDuplicateConversionValue(newCheerleader.Grade);
                string currencyName = ApplyCheerleaderDuplicateConversion(newCheerleader.Grade, convertedAmount);
                Debug.Log($"[GameManager] 중복 획득으로 재화 변환됨: {newCheerleader.Name} " +
                    $"(CatalogId={newCheerleader.CatalogId}, Grade={newCheerleader.Grade}) -> {currencyName} +{convertedAmount}");
                return;
            }

            OwnedCheerleaders.Add(newCheerleader);
        }

        /// <summary>등급에 대응하는 응원봉 재화에 변환량을 더하고, 지급된 재화명을 로그용으로 반환한다.</summary>
        private string ApplyCheerleaderDuplicateConversion(CheerleaderGrade grade, int amount)
        {
            switch (grade)
            {
                case CheerleaderGrade.ICON:
                    StarCheerStick += amount;
                    return nameof(StarCheerStick);
                case CheerleaderGrade.LEGEND:
                    LegendCheerStick += amount;
                    return nameof(LegendCheerStick);
                case CheerleaderGrade.SEASON_LIMITED:
                    LimitedCheerStick += amount;
                    return nameof(LimitedCheerStick);
                case CheerleaderGrade.LIVE_NORMAL:
                case CheerleaderGrade.LIVE_EPIC:
                default:
                    LiveCheerStick += amount;
                    return nameof(LiveCheerStick);
            }
        }

        /// <summary>[TASK-KBO-064/127/128/129] docs/16_shop_and_gacha_policy.md 4절이 제안한 등급별
        /// 마일리지 환급량([Draft], v0.2 밸런싱에서 조정 가능) - PM 확정 5단계(TASK-KBO-127)에 맞춰
        /// 갱신했다. [TASK-KBO-128 하향 조정] TASK-KBO-127이 매겼던 값(10/50/200/1000/3000)은 최상위
        /// 2개 등급(LEGEND=1000, SEASON_LIMITED=3000)이 10연뽑 비용(구 CheerleaderGachaService.
        /// CostPerRoll=100 x 10=1000)과 같거나 초과해, 중복 1장만 나와도 10연뽑 비용 전액(또는 그 이상)이
        /// 환급되는 재화 무한 증식 밸런스 붕괴가 있었다 - 5단계 전부 10연뽑 비용(1000)보다 확실히 낮은
        /// 값으로 하향했다. 정식 등급(LIVE_NORMAL~SEASON_LIMITED) 외의 값(NONE/TEST 등 구버전 더미
        /// 등급)은 최저 등급(LIVE_NORMAL)과 동일하게 취급한다.</summary>
        private static int ResolveCheerleaderDuplicateConversionValue(CheerleaderGrade grade) => grade switch
        {
            CheerleaderGrade.LIVE_NORMAL => 10,
            CheerleaderGrade.LIVE_EPIC => 30,
            CheerleaderGrade.ICON => 50,
            CheerleaderGrade.LEGEND => 100,
            CheerleaderGrade.SEASON_LIMITED => 300,
            _ => 10,
        };

#if UNITY_EDITOR
        [Tooltip("[에디터 전용] true면 Awake() 시 EquippedCheerleader가 비어 있을 때만 테스트용 치어리더를 " +
                 "자동 장착한다. [TASK-KBO-056] EquipCheerleader()/UnequipCheerleader() 정식 API가 생겨 " +
                 "더 이상 기본으로 켜 둘 필요가 없어 기본값을 false로 변경했다 - QA 목적으로 필요하면 " +
                 "인스펙터에서 직접 켤 수 있다. 이 필드와 관련 로직 전체가 #if UNITY_EDITOR로 감싸여 있어 " +
                 "릴리스 빌드에는 포함되지 않는다.")]
        [SerializeField] private bool devAutoEquipTestCheerleader = false;
#endif

        // ----- 재화 -----
        // [TASK-KBO-129] GDD "스토브리그: 단장의 시간" 원문 "재화 > 뽑기(가챠) 재화" 절의 명칭을 그대로
        // 따른다. TASK-126이 도입한 단순화된 2종(영입권/응원봉)을 폐기하고, 원문이 실제로 나열한
        // 선수 영입 재화 6종 + 치어리더 영입 재화 4종으로 세분화한다 - 각 재화는 원문의 "뽑기(가챠)"절이
        // 명시한 서로 다른 영입 카테고리(픽업/프리미엄/일반) 전용이며 서로 대체 불가능하다.
        // [사실 확인] 원문 "재화" 절은 "라이브 고급 영입권"이라 적었으나, "뽑기(가챠)" 절의 실제 상품
        // 표는 같은 대상을 "라이브 에픽 영입권"으로 적어(라이브 에픽 영입(4~5성) 항목) 원문 자체에
        // 내부 표기 불일치가 있다 - 이미 확정된 Grade.LIVE_EPIC 명칭과 일치시키기 위해 "라이브 에픽
        // 영입권"을 채택했다. "고급 영입권"(내 구단 시즌/라이브 카드 선수 영입 전용, 원문 재화 절에
        // 별도 항목으로 존재)은 원문 "뽑기(가챠)" 절 어디에도 이를 소모하는 명시적 상품/화면이
        // 없어 [결정 필요] - 필드는 만들되 아직 어떤 버튼도 소모하지 않는다(기획서에 없는 화면을
        // 임의로 만들지 말라는 명령서 5항 준수).
        [Header("Currency - 선수 영입")]
        [SerializeField] private int liveNormalTicket;  // 라이브 일반 영입권 (일반 영입 > 라이브 일반)
        [SerializeField] private int liveEpicTicket;    // 라이브 에픽 영입권 (일반 영입 > 라이브 에픽, 원문 "라이브 고급 영입권"과 동일 대상)
        [SerializeField] private int pickupTicket;      // 픽업 영입권 (픽업 영입 > 시그니처/타이틀 홀더)
        [SerializeField] private int advancedTicket;    // 고급 영입권 - [결정 필요] 소모처 미확정, 필드만 유지
        [SerializeField] private int trophy;            // 트로피 (프리미엄 영입 > 타이틀 홀더)
        [SerializeField] private int signatureBall;     // 싸인볼 (프리미엄 영입 > 시그니처)

        [Header("Currency - 치어리더 영입")]
        [SerializeField] private int liveCheerStick;    // 라이브 응원봉 (일반 영입 > 라이브)
        [SerializeField] private int starCheerStick;    // 스타 응원봉 (프리미엄/픽업 영입 > 아이콘)
        [SerializeField] private int legendCheerStick;  // 레전드 응원봉 (프리미엄/픽업 영입 > 레전드)
        [SerializeField] private int limitedCheerStick; // 한정 응원봉 (일반 영입 > 한정, 시즌 한정 기간)

        // [TASK-KBO-130] GDD 원문 "재화 > 기타 소모 재화" 절이 명시한 3종을 로비 상단에 상시 표시한다
        // (사용자 제공 레퍼런스: 컴투스프로야구V26류 상단 재화 바). 볼(GameGold, 이미 존재)/유니폼/
        // 플레이 티켓 - 전부 특정 가챠 카테고리 전용이 아니라 게임 전반에서 쓰이는 "기본 소모 재화"라는
        // 공통점이 있어 이 셋만 상단 상시 노출 대상으로 선정했다(GDD 원문: "유니폼 - 수급처 랭킹 챌린지
        // 일일/주간 보상, 홈런 레이스 주간 보상 / 사용처 선수·치어리더 영입, 랭킹 챌린지 새로고침 구매",
        // "티켓 - 리그/홈런 레이스/랭킹 챌린지 등 모든 플레이에 필요한 재화").
        [Header("Currency - 기타")]
        [SerializeField] private int gameGold;         // 게임 머니 (볼)
        [SerializeField] private int uniform;          // 유니폼
        [SerializeField] private int ticket;           // 플레이 티켓

        public int LiveNormalTicket { get => liveNormalTicket; set => liveNormalTicket = Mathf.Max(0, value); }
        public int LiveEpicTicket { get => liveEpicTicket; set => liveEpicTicket = Mathf.Max(0, value); }
        public int PickupTicket { get => pickupTicket; set => pickupTicket = Mathf.Max(0, value); }
        public int AdvancedTicket { get => advancedTicket; set => advancedTicket = Mathf.Max(0, value); }
        public int Trophy { get => trophy; set => trophy = Mathf.Max(0, value); }
        public int SignatureBall { get => signatureBall; set => signatureBall = Mathf.Max(0, value); }

        public int LiveCheerStick { get => liveCheerStick; set => liveCheerStick = Mathf.Max(0, value); }
        public int StarCheerStick { get => starCheerStick; set => starCheerStick = Mathf.Max(0, value); }
        public int LegendCheerStick { get => legendCheerStick; set => legendCheerStick = Mathf.Max(0, value); }
        public int LimitedCheerStick { get => limitedCheerStick; set => limitedCheerStick = Mathf.Max(0, value); }

        public int GameGold
        {
            get => gameGold;
            set => gameGold = Mathf.Max(0, value);
        }

        public int Uniform { get => uniform; set => uniform = Mathf.Max(0, value); }
        public int Ticket { get => ticket; set => ticket = Mathf.Max(0, value); }

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

            // [TASK-KBO-068] 치어리더 가챠 카탈로그(cheerleaders.csv)를 게임 시작 시 1회 로드한다.
            // CheerleaderCatalog.Initialize() 자체가 이미 초기화됐으면 재실행을 건너뛰므로, 씬 재로드
            // 등으로 이 Awake()가 다시 호출돼도 안전하다.
            CheerleaderCatalog.Initialize();

            // [TASK-KBO-085] 선수 데이터베이스(players.csv) 초기화 호출을 여기서 제거했다 - 같은
            // GameObject에 붙은 PlayerDatabase와 GameManager 중 어느 Awake()가 먼저 실행되는지 Unity가
            // 보장하지 않아(TASK-082/083/084에서 이미 제기된 리스크), GameManager가 대신 초기화를
            // 책임지는 구조 자체가 취약했다. 이제 PlayerDatabase가 자신의 Awake()에서 스스로
            // Initialize()를 호출하고, AllTemplates/GetTemplateById() 접근 시점에도 지연 초기화하므로
            // GameManager는 PlayerDatabase의 존재/초기화 여부를 더 이상 알 필요가 없다.

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

            Debug.Log($"[TASK-KBO-048 SelfCheck] AC-01(유저 홈+장착): ConditionBuff={ResolveCheerleaderConditionBuff(true, equipped, Team.None)}(기대 3), " +
                $"ClutchMultiplier={ResolveCheerleaderClutchMultiplier(true, equipped, Team.None)}(기대 1.2)");

            Debug.Log($"[TASK-KBO-048 SelfCheck] AC-02(유저 홈+미장착): ConditionBuff={ResolveCheerleaderConditionBuff(true, null, Team.None)}(기대 0), " +
                $"ClutchMultiplier={ResolveCheerleaderClutchMultiplier(true, null, Team.None)}(기대 1)");

            Debug.Log($"[TASK-KBO-048 SelfCheck] AC-03(유저 원정+장착): ConditionBuff={ResolveCheerleaderConditionBuff(false, equipped, Team.None)}(기대 0), " +
                $"ClutchMultiplier={ResolveCheerleaderClutchMultiplier(false, equipped, Team.None)}(기대 1)");

            // AC-04(AI 홈): 팀 식별 자체는 각 매니저의 BuildTeamPowerModifiers가 책임지고, 이 정적
            // 함수는 그 판별 결과(isUserTeamHome)만 입력으로 받는다 - "AI 팀"이라는 조건은 여기서
            // isUserTeamHome=false로 표현되며 입력 형태상 AC-03과 동일하다(둘 다 0/1.0 기대).
            Debug.Log($"[TASK-KBO-048 SelfCheck] AC-04(AI 홈, isUserTeamHome=false로 표현): ConditionBuff={ResolveCheerleaderConditionBuff(false, equipped, Team.None)}(기대 0)");

            var nanCheer = new Cheerleader("SELFCHECK_NAN", "NaN", CheerleaderGrade.TEST, 0, float.NaN);
            var infCheer = new Cheerleader("SELFCHECK_INF", "Inf", CheerleaderGrade.TEST, 0, float.PositiveInfinity);
            var zeroCheer = new Cheerleader("SELFCHECK_ZERO", "Zero", CheerleaderGrade.TEST, 0, 0f);
            var negCheer = new Cheerleader("SELFCHECK_NEG", "Neg", CheerleaderGrade.TEST, 0, -5f);
            Debug.Log("[TASK-KBO-048 SelfCheck] AC-06(방어적 설계, 모두 기대값 1): " +
                $"NaN->{ResolveCheerleaderClutchMultiplier(true, nanCheer, Team.None)}, " +
                $"Infinity->{ResolveCheerleaderClutchMultiplier(true, infCheer, Team.None)}, " +
                $"0->{ResolveCheerleaderClutchMultiplier(true, zeroCheer, Team.None)}, " +
                $"음수->{ResolveCheerleaderClutchMultiplier(true, negCheer, Team.None)}");

            // [TASK-KBO-175] 구단 시너지: 이아영 KIA(2020~2021) 카드는 KIA 세트덱에서만 발동한다.
            var kiaCheer = new Cheerleader("SELFCHECK_KIA", "이아영", CheerleaderGrade.LEGEND, conditionBuff: 4, clutchMultiplier: 1.15f,
                team: Team.KIA, activePeriod: "2020~2021");
            Debug.Log("[TASK-KBO-175 SelfCheck] AC-07(구단 시너지): " +
                $"KIA 세트덱 ConditionBuff={ResolveCheerleaderConditionBuff(true, kiaCheer, Team.KIA)}(기대 4), " +
                $"NC 세트덱 ConditionBuff={ResolveCheerleaderConditionBuff(true, kiaCheer, Team.NC)}(기대 0), " +
                $"NC 세트덱 Clutch={ResolveCheerleaderClutchMultiplier(true, kiaCheer, Team.NC)}(기대 1), " +
                $"활동기간 2021 포함={CheerleaderActivePeriod.Contains(kiaCheer.ActivePeriod, 2021)}(기대 True)/2022={CheerleaderActivePeriod.Contains(kiaCheer.ActivePeriod, 2022)}(기대 False)");
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

        /// <summary>
        /// [TASK-KBO-172] 유저 구단의 세트덱 선택형 구간(OR) 옵션과 "연도 선택" 값. 기본값은 전 구간 A안 +
        /// 연도 자동 선택. UI가 이 객체를 수정하면 다음 경기부터 반영된다(세이브 저장은 후속 작업 - DCL 참고).
        /// </summary>
        public SetDeckSelection SetDeckSelection { get; } = new SetDeckSelection();

        /// <summary>
        /// [TASK-KBO-172] 27인 세트덱 스코어/버프 구간 판정(docs/04_card_grade_policy.md 6~7절). 유저/AI 공용.
        /// favoriteTeam(Team.ToString())을 주면 그 구단이 세트덱 기준 구단이 되고, null이면 로스터 최다 구단.
        /// selection은 선택형 구간 옵션(유저 구단만 GameManager.SetDeckSelection을 넘기고 AI는 null = 기본 A안).
        /// </summary>
        public static SetDeckResult EvaluateSetDeck(List<Player> roster, string favoriteTeam = null,
            SetDeckSelection selection = null)
        {
            if (roster == null || roster.Count == 0) return SetDeckResult.Empty;
            return SetDeckEvaluator.Evaluate(roster, favoriteTeam, selection);
        }

        /// <summary>
        /// 로스터의 세트덱 시너지(팀 OVR 가산 + 경기 세부 스탯 균등 가산값)를 계산하는 정적 유틸리티 - 유저/AI 공용.
        ///
        /// [TASK-KBO-172 전면 개편] 이전 규칙(TASK-KBO-037 "28인 중 동일 구단 15명 이상 +12" + TASK-KBO-166
        /// "왕조 카드 5명 이상 +15")을 폐기하고, 기획 고도화 자료.pdf의 "27인 세트덱 스코어 -> 30P~200P 버프
        /// 구간" 체계로 교체했다. 반환값은 도달한 구간 중 "모든 능력치 +N"(대상 전원·전 스탯) 효과의 누적합이며
        /// (200P 풀 도달 시 +16), 타자/투수 한정·타순·선택 연도·부분 스탯 효과는 `EvaluateSetDeck().Profile`로
        /// 경기 엔진(TeamPowerModifiers.SetDeckProfile)에 따로 전달된다 - 이 반환값과 중복 가산되지 않는다.
        /// 기존 호출부(팀 OVR 표시 등)는 시그니처 그대로 동작한다.
        /// </summary>
        public static int CalculateSynergy(List<Player> roster, string favoriteTeam = null)
        {
            return EvaluateSetDeck(roster, favoriteTeam).AllPlayersFlatBuff;
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
        /// <remarks>[TASK-KBO-175] deckTeam = 그 경기 세트덱의 기준 구단(SetDeckResult.DeckTeam). 치어리더 카드의 소속
        /// 구단(활동 기간 동안 소속했던 구단)이 deckTeam과 다르면 구단 시너지 미발동으로 0이다(CheerleaderSynergy).</remarks>
        public static int ResolveCheerleaderConditionBuff(bool isUserTeamHome, Cheerleader equippedCheerleader, Team deckTeam)
        {
            if (!isUserTeamHome || !CheerleaderSynergy.IsActive(equippedCheerleader, deckTeam)) return 0;
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
        /// <remarks>[TASK-KBO-175] 구단 시너지 미발동(치어리더 소속 구단 != deckTeam)이면 중립값.</remarks>
        public static float ResolveCheerleaderClutchMultiplier(bool isUserTeamHome, Cheerleader equippedCheerleader, Team deckTeam)
        {
            if (!isUserTeamHome || !CheerleaderSynergy.IsActive(equippedCheerleader, deckTeam)) return NeutralClutchMultiplier;

            float raw = equippedCheerleader.ClutchMultiplier;
            if (float.IsNaN(raw) || float.IsInfinity(raw)) return NeutralClutchMultiplier;

            return Mathf.Max(NeutralClutchMultiplier, raw);
        }
    }
}
