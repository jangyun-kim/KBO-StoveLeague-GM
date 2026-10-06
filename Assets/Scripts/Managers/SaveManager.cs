using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// 인벤토리에 저장되는 선수 카드 1장의 경량화 데이터. 원본 PlayerTemplate(이름/구단/스탯 등 불변 데이터)은
    /// 통째로 직렬화하지 않고 TemplateId만 저장한다 - 로드 시 PlayerDatabase에서 TemplateId로 원본을
    /// 다시 찾아 붙이므로, 저장 파일에는 유저의 "육성 진행 상태"만 남는다.
    /// </summary>
    [Serializable]
    public class PlayerSaveData
    {
        public string InstanceId;
        public string TemplateId;
        public int ReinforceLevel;
        // [TASK-KBO-138] EXP 누적 확정 강화 방식으로 개편되며 신설(Player.ReinforceExp). 이 필드가 없는
        // 구버전 세이브는 JsonUtility가 기본값 0으로 채우며, 강화 진행 중 EXP가 없는 상태와 완전히
        // 동일해 별도의 구버전 판별 로직이 필요 없다.
        public int ReinforceExp;
        public int AwakenLevel;
        // [TASK-KBO-183] v10 - 4대 성장 중 한계 돌파 / 훈련(특훈) 단계. 필드 없는 구버전 세이브는 0(미진행)으로 채워진다.
        public int LimitBreakLevel;
        public int TrainingLevel;
        public int StarLevel;
        public StarType CurrentStarType;
        public List<string> AcquiredSkillIds = new List<string>();
        // [TASK-KBO-190] v12 - 3슬롯 스킬(ID · 등급 · 레벨). 필드 없는 구버전 세이브는 빈 목록 → 로드 시 InstanceId 시드로 결정적 부여.
        public List<PlayerSkillSlot> SkillSlots = new List<PlayerSkillSlot>();

        // 체력 필드 도입(v2) 이전 세이브에는 이 두 값이 JSON에 아예 없어 역직렬화 시 기본값(0)이 된다.
        // RestorePlayer()가 "투수인데 MaxStamina가 0"인 경우를 "구버전 세이브"로 간주해 Player 생성자의
        // 롤 기준 기본값(완전 회복 상태)을 그대로 둔다 - 자세한 내용은 RestorePlayer() 참고.
        public int CurrentStamina;
        public int MaxStamina;

        // [TASK-GM-02] v14 - 단장 모드 선수 속성(Player GM 필드). 필드 없는 구버전 세이브는 아래 기본값(만족도 70 · 자존심 1)으로 채워지고,
        // 연봉이 0인 카드는 진단 화면/로더가 시즌 성적으로 1회 산출한다.
        public int Age;
        public int Salary;
        public int ContractYears;
        public int EgoLevel = 1;
        public LockerRoomRole RoleArchetype = LockerRoomRole.UnsungHero;
        public int PersonalMorale = Player.DefaultPersonalMorale;
        public bool IsCaptain;
        public bool HasRoleConcessionBonus;
        public bool IsScouted;
        public int InjuryRemainingDays;
        public List<string> CareerAwardIds = new List<string>();
    }

    /// <summary>
    /// (구버전, v1 이하 세이브 전용) Item을 인스턴스 1개당 1행으로 저장하던 예전 포맷. 강화 재료/스킬
    /// 변경권처럼 완전히 동일한 템플릿을 수십 장씩 보유할 수 있는 소모품에는 비효율적이라 v2부터는
    /// ItemStackSaveData(TemplateId+수량)로 대체됐다. 새 세이브는 이 필드를 항상 빈 리스트로 쓴다 -
    /// 오직 "이 필드에 값이 있다 = v1 이하 세이브"를 판별하기 위한 하위 호환 읽기 전용 필드로만 남겨 둔다.
    /// </summary>
    [Serializable]
    public class ItemSaveData
    {
        public string InstanceId;
        public string TemplateId;
    }

    /// <summary>
    /// v2부터 쓰는 신규 Item 저장 포맷. 같은 TemplateId를 가진 아이템을 개수(Count)로 묶어 한 행으로
    /// 저장한다 - Item은 InstanceId/Template 외에 성장 상태가 전혀 없는 완전한 소모품(fungible)이라
    /// 인스턴스별로 구분해 저장할 이유가 없다. 로드 시에는 TemplateId당 Count개의 새 Item 인스턴스를
    /// (새 GUID로) 다시 만들어 낸다 - InstanceId 자체를 보존할 필요가 없기 때문에 가능한 단순화다.
    /// </summary>
    [Serializable]
    public class ItemStackSaveData
    {
        public string TemplateId;
        public int Count;
    }

    [Serializable]
    public class TeamStandingSaveData
    {
        public Team Team;
        public int Wins;
        public int Draws;
        public int Losses;
    }

    /// <summary>명예의 전당(SeasonRollover.HallOfFame) 항목 1개의 저장 포맷. [TASK-KBO-055] HallOfFameEntry.
    /// ChampionTeam도 이제 이 클래스와 동일하게 Team(non-nullable)이라 그대로 대입하면 되며, 양쪽 모두
    /// Team.None을 "그 시즌 우승팀 기록 없음" 대용으로 쓴다(UserFinalRank의 -1과 같은 관례).</summary>
    [Serializable]
    public class HallOfFameEntrySaveData
    {
        public int SeasonYear;
        public Team ChampionTeam;
        public string BattingAverageLeader;
        public string HomeRunLeader;
        public string WinsLeader;
        public string EraLeader;
        // [TASK-KBO-193] v13 리그 기록실 명예의 전당 - 시즌 번호 · 리그 단계 · 최종 순위/전적 · 한국시리즈 우승 · 시즌 MVP · 타이틀 수상 내역
        public int SeasonNumber;
        public string TierName;
        public int FinalRank;
        public int RegularSeasonRank;
        public int Wins;
        public int Draws;
        public int Losses;
        public bool KoreanSeriesWon;
        public string Mvp;
        public List<string> Titles = new List<string>();
    }

    /// <summary>PostSeasonManager 브래킷 진행 상태의 저장 포맷. Dictionary&lt;Team,int&gt;(FinalRanks)는
    /// JsonUtility가 지원하지 않아 팀/순위 두 병렬 리스트로 대체했다(인덱스로 짝을 맞춘다).</summary>
    [Serializable]
    public class PostSeasonSaveData
    {
        public Team[] Seeds = new Team[5];
        public Team ChampionTeam; // Team.None = 아직 미확정
        public List<Team> FinalRankTeams = new List<Team>();
        public List<int> FinalRankValues = new List<int>();

        public bool HasActiveSeries;
        public PostSeasonRound ActiveRound;
        public Team ActiveHigherSeed;
        public Team ActiveLowerSeed;
        public int WinsRequiredForHigherSeed;
        public int WinsRequiredForLowerSeed;
        public int WinsHigherSeed;
        public int WinsLowerSeed;
    }

    /// <summary>
    /// 저장 파일 전체 스키마. JsonUtility로 직렬화하므로 int?/Dictionary 등 JsonUtility가 지원하지
    /// 않는 타입은 쓰지 않는다(Nullable은 정수 -1(또는 Team.None) 등 "값 없음" 대용으로 사용, Dictionary는
    /// 병렬 리스트로 대체). DateTime도 JsonUtility 미지원이라 ISO 문자열(.ToString("o"))로 저장한다.
    /// </summary>
    [Serializable]
    public class GameSaveData
    {
        // v2: Item 저장 포맷을 인스턴스별(ItemSaveData) -> 수량 그룹(ItemStackSaveData)으로 변경,
        // Player에 스태미나(CurrentStamina/MaxStamina) 필드 추가.
        // v3: 명예의 전당(HallOfFame), 리그 캘린더 날짜(CalendarDateIso), 포스트시즌 브래킷(PostSeason)
        // 추가. 이 값 자체를 읽어 분기하지는 않는다 - 대신 "새 필드가 비어 있으면 구버전"이라는 더 안전한
        // 필드-존재 기반 판별을 쓴다(SaveVersion은 사람이 읽는 기록용).
        // v4: 팬심/연패(FanSentiment/LosingStreak) 필드 추가(TASK-KBO-051). 필드 초기값을
        // GameManager 기본값과 동일하게 맞춰 두어(UserFinalRank=-1 패턴과 동일), 이 필드가 없는
        // 구버전 JSON을 역직렬화해도 자동으로 안전한 기본값이 채워진다.
        // v5: 치어리더 장착 슬롯/보유 목록(EquippedCheerleader/OwnedCheerleaders) 추가(TASK-KBO-057).
        // JsonUtility는 null 참조 필드를 JSON null이 아니라 "기본값으로 채워진 인스턴스"로 직렬화한다
        // (실측 확인 - InstanceId가 빈 문자열인 필드-값 객체로 저장됨). 그래서 EquippedCheerleader가
        // null인지 여부는 SaveManager.ApplySaveData()에서 InstanceId 존재 여부로 판별한다(자세한
        // 내용은 ApplySaveData() 주석 참고) - "필드 존재 여부로 구버전 판별" 관례와는 별개의, JsonUtility
        // 자체의 null 처리 한계에 대한 방어다.
        // v6: 세트덱 선택형 구간 옵션/연도 선택(SetDeckSelection) 추가(TASK-KBO-176). 필드가 없는 구버전 JSON은
        // 기본 인스턴스(전 구간 A안 + 연도 자동)로 역직렬화되므로 별도 분기가 필요 없다.
        // v7: 치어리더 6인 역할 편성(CheerSquad, 6칸 - 빈 칸은 InstanceId가 빈 객체) 추가(TASK-KBO-180). CheerSquad가 비어 있는
        // 구버전(v5~v6) 세이브는 EquippedCheerleader(단일 슬롯)를 1번 응원단장 슬롯으로 이관한다. EquippedCheerleader는
        // 구버전 빌드 호환을 위해 계속 응원단장 슬롯 값을 함께 기록한다.
        // v8: 단장 닉네임(ManagerNickname)·신규 단장 튜토리얼 완료 여부(TutorialCompleted) 추가(TASK-KBO-181). 필드가 없는 구버전
        // 세이브는 닉네임 빈 문자열(로비는 '단장'으로 표기), 튜토리얼 완료(true - 기존 유저에게 가이드를 다시 띄우지 않음)로 채워진다.
        // v9: 라인업 [타순 변경] 유저 지정 타순(BattingOrder, InstanceId 목록) 추가(TASK-KBO-182). 없으면 빈 목록 = 기본 타순.
        // v10: 카드 한계 돌파/훈련 단계(PlayerSaveData.LimitBreakLevel/TrainingLevel)와 12단계 리그 현재 단계(LeagueTier) 추가(TASK-KBO-183).
        //      LeagueTier가 없는 구버전 세이브(-1)는 저장된 구단 OVR의 권장 리그(최소 아마추어)로 복원한다.
        // v11: 라인업 선발(주전) ↔ 후보 맞교환 고정(LineupAssignment - 주전 자리/투수 보직 InstanceId 핀) 추가(TASK-KBO-186). 없으면 기본 OVR 편성.
        // v12: 카드 3슬롯 스킬(PlayerSaveData.SkillSlots)과 스킬 변경권 · 고급 스킬 변경권 추가(TASK-KBO-190). 슬롯 없는 카드는 로드 시 결정적 부여.
        // v13: 스토브리그 시즌 상태(StoveLeague)와 명예의 전당 확장 필드(시즌 번호 · 리그 · 전적 · KS 우승 · MVP · 타이틀) 추가(TASK-KBO-193).
        //      없는 구버전 세이브는 새 시즌 상태 · 빈 확장 필드(기록실은 "-"로 표기)로 채워진다.
        // v14: 단장 모드(TASK-GM-02) - 선수 GM 속성(PlayerSaveData.Age ~ CareerAwardIds)과 리그 상태(GMLeague: 모드 · 연도 · 경기 진행 인덱스 ·
        //      10구단 로스터/치어리더 풀 · 순위 · 개인 누적 기록 · 최신 소식). GMLeague.HasData가 false면 단장 모드 미시작.
        public int SaveVersion = 16; // [TASK-GM-05] v16 - 치어리더 피로도 · 자동 로테이션 · 전담 응원 · 홈 흥행 누적(v15 = GM-04 시상)
        public string SavedAtUtc;

        // GameManager
        public List<PlayerSaveData> Inventory = new List<PlayerSaveData>();
        public List<string> RosterInstanceIds = new List<string>(); // Inventory 중 로스터에 편성된 카드의 InstanceId
        public List<ItemSaveData> ItemInventory = new List<ItemSaveData>(); // v1 이하 세이브 하위 호환 전용 - 새 저장은 항상 빈 리스트
        public List<ItemStackSaveData> ItemStacks = new List<ItemStackSaveData>();
        public Team FavoriteTeam;
        // [TASK-KBO-129] GDD 원문 재화 10종으로 세분화(구 ScoutTicket/CheerStick 2종 폐기).
        public int LiveNormalTicket;
        public int LiveEpicTicket;
        public int PickupTicket;
        public int AdvancedTicket;
        public int Trophy;
        public int SignatureBall;
        public int LiveCheerStick;
        public int StarCheerStick;
        public int LegendCheerStick;
        public int LimitedCheerStick;
        public int GameGold;
        public int Uniform;
        public int Ticket;
        public int GrowthCoin;      // [TASK-KBO-189] 성장 코인(방출 마일리지)
        public int AwakenTicket;    // [TASK-KBO-189] 범용 각성 보조권
        public int TranscendTicket; // [TASK-KBO-189] 초월 핵심 대체권
        public int TrainingTicket;  // [TASK-KBO-189] 특훈권
        public int SkillChangeTicket;        // [TASK-KBO-190] 스킬 변경권
        public int PremiumSkillChangeTicket; // [TASK-KBO-190] 고급 스킬 변경권
        public bool IsFirstLogin = true;
        public int FanSentiment = 100; // 필드 없는 구버전 세이브 로드 시 GameManager 기본값(100)과 동일하게 채워짐
        public int LosingStreak;
        public Cheerleader EquippedCheerleader; // null 여부는 ApplySaveData()에서 InstanceId로 판별(위 v5 주석 참고)
        public List<Cheerleader> OwnedCheerleaders = new List<Cheerleader>();
        public SetDeckSelection SetDeckSelection = new SetDeckSelection(); // [TASK-KBO-176] v6
        public List<Cheerleader> CheerSquad = new List<Cheerleader>(); // [TASK-KBO-180] v7 - 인덱스 = (int)CheerRole
        public string ManagerNickname = ""; // [TASK-KBO-181] v8
        public bool TutorialCompleted = true; // [TASK-KBO-181] v8 - 구버전 세이브는 가이드 생략
        public List<string> BattingOrder = new List<string>(); // [TASK-KBO-182] v9
        public LineupAssignment Lineup = new LineupAssignment(); // [TASK-KBO-186] v11
        public StoveLeagueState StoveLeague = new StoveLeagueState(); // [TASK-KBO-193] v13
        public GMLeagueSaveData GMLeague = new GMLeagueSaveData(); // [TASK-GM-02] v14

        // LeagueManager
        public bool HasLeagueData;
        public Team UserTeam;
        public int PlayedGameCount;
        public LeaguePhase CurrentPhase;
        public int UserFinalRank = -1; // -1 = 아직 시즌을 완주하지 않음(null 대용)
        public int LeagueTier = -1; // [TASK-KBO-183] v10 - (int)Models.LeagueTier, -1 = 구버전 세이브
        public List<TeamStandingSaveData> Standings = new List<TeamStandingSaveData>();
        // [TASK-KBO-190] 이번 시즌 결산(순위 · 타이틀 보상)을 이미 지급했는지 - 결산 후 저장 · 재시작 시 중복 지급 방지.
        public bool SeasonRewardGranted;

        // LeagueCalendar
        public string CalendarDateIso; // null/빈 문자열이면 구버전 세이브(캘린더 도입 이전)

        // SeasonRollover (명예의 전당)
        public List<HallOfFameEntrySaveData> HallOfFame = new List<HallOfFameEntrySaveData>();

        // PostSeasonManager
        public bool HasPostSeasonData;
        public PostSeasonSaveData PostSeason = new PostSeasonSaveData();
    }

    /// <summary>
    /// GameManager(인벤토리/로스터/재화)와 LeagueManager(시즌 진행도/순위)를 JSON으로 직렬화해
    /// Application.persistentDataPath에 저장/로드하는 싱글톤.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        private const string SaveFileName = "kbo_manager_save.json";

        [Header("References")]
        [Tooltip("로드 시 저장된 TemplateId로 원본 PlayerTemplate을 복구하기 위해 필요하다.")]
        [SerializeField] private PlayerDatabase playerDatabase;
        [Tooltip("로드 시 저장된 TemplateId로 원본 ItemTemplate을 복구하기 위해 필요하다.")]
        [SerializeField] private ItemDatabase itemDatabase;

        private string SavePath => Path.Combine(Application.persistentDataPath, SaveFileName);

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public bool HasSaveFile() => File.Exists(SavePath);

        /// <summary>[TASK-KBO-181] 이어하기 가능한 세이브인가 - 파일이 있고 온보딩(구단 선택)을 마친 커리어여야 한다.</summary>
        public bool HasContinuableSave()
        {
            if (!HasSaveFile()) return false;
            try
            {
                var data = JsonUtility.FromJson<GameSaveData>(File.ReadAllText(SavePath));
                return data != null && !data.IsFirstLogin && data.FavoriteTeam != Team.None && data.Inventory != null && data.Inventory.Count > 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] 세이브 파일을 확인하지 못했습니다: {e.Message}");
                return false;
            }
        }

        /// <summary>[TASK-KBO-181] 온보딩을 마친 커리어만 자동 저장한다(타이틀/온보딩 도중의 빈 상태로 기존 세이브를 덮어쓰지 않음).</summary>
        public bool TrySaveCareer()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.IsFirstLogin || gm.FavoriteTeam == Team.None) return false;
            SaveGame();
            return true;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) TrySaveCareer();
        }

        private void OnApplicationQuit() => TrySaveCareer();

        /// <summary>현재 GameManager/LeagueManager 상태를 JSON으로 직렬화해 기기에 저장한다.</summary>
        public void SaveGame()
        {
            var data = BuildSaveData();
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(SavePath, json);
            Debug.Log($"[SaveManager] 저장 완료: {SavePath}");
        }

        /// <summary>저장 파일을 읽어 GameManager/LeagueManager 상태를 복원한다. 저장 파일이 없으면 false.</summary>
        public bool LoadGame()
        {
            if (!HasSaveFile())
            {
                Debug.LogWarning("[SaveManager] 저장 파일이 없습니다.");
                return false;
            }

            string json = File.ReadAllText(SavePath);
            var data = JsonUtility.FromJson<GameSaveData>(json);
            if (data == null)
            {
                Debug.LogError("[SaveManager] 저장 파일을 파싱하지 못했습니다.");
                return false;
            }

            ApplySaveData(data);
            return true;
        }

        // ----- 직렬화 -----

        private GameSaveData BuildSaveData()
        {
            var data = new GameSaveData
            {
                SavedAtUtc = DateTime.UtcNow.ToString("o"),
            };

            if (GameManager.Instance != null)
            {
                var gm = GameManager.Instance;

                data.Inventory = gm.Inventory.Select(ToSaveData).ToList();
                data.RosterInstanceIds = gm.Roster.Select(p => p.InstanceId).ToList();
                data.ItemInventory = new List<ItemSaveData>(); // v2부터는 항상 비워 둔다 - ItemStacks가 유일한 진실
                data.ItemStacks = gm.ItemInventory
                    .Where(i => i?.Template != null)
                    .GroupBy(i => i.Template.TemplateId)
                    .Select(g => new ItemStackSaveData { TemplateId = g.Key, Count = g.Count() })
                    .ToList();
                data.FavoriteTeam = gm.FavoriteTeam;
                data.LiveNormalTicket = gm.LiveNormalTicket;
                data.LiveEpicTicket = gm.LiveEpicTicket;
                data.PickupTicket = gm.PickupTicket;
                data.AdvancedTicket = gm.AdvancedTicket;
                data.Trophy = gm.Trophy;
                data.SignatureBall = gm.SignatureBall;
                data.LiveCheerStick = gm.LiveCheerStick;
                data.StarCheerStick = gm.StarCheerStick;
                data.LegendCheerStick = gm.LegendCheerStick;
                data.LimitedCheerStick = gm.LimitedCheerStick;
                data.GameGold = gm.GameGold;
                data.Uniform = gm.Uniform;
                data.Ticket = gm.Ticket;
                data.GrowthCoin = gm.GrowthCoin;
                data.AwakenTicket = gm.AwakenTicket;
                data.TranscendTicket = gm.TranscendTicket;
                data.TrainingTicket = gm.TrainingTicket;
                data.SkillChangeTicket = gm.SkillChangeTicket;
                data.PremiumSkillChangeTicket = gm.PremiumSkillChangeTicket;
                data.IsFirstLogin = gm.IsFirstLogin;
                data.ManagerNickname = gm.ManagerNickname;
                data.TutorialCompleted = gm.TutorialCompleted;
                data.BattingOrder = gm.BattingOrderOverride.ToList();
                data.Lineup = new LineupAssignment();
                data.Lineup.CopyFrom(gm.LineupAssignment);
                data.StoveLeague = gm.StoveLeague; // [TASK-KBO-193]
                data.GMLeague = ToGMSaveData(gm.GMLeague); // [TASK-GM-02]
                data.FanSentiment = gm.FanSentiment;
                data.LosingStreak = gm.LosingStreak;
                data.SetDeckSelection = new SetDeckSelection(); // [TASK-KBO-176] 사본 저장(런타임 객체 공유 방지)
                data.SetDeckSelection.CopyFrom(gm.SetDeckSelection);
                data.EquippedCheerleader = gm.EquippedCheerleader;
                data.OwnedCheerleaders = new List<Cheerleader>(gm.OwnedCheerleaders);
                // [TASK-KBO-180] 6인 편성 - JsonUtility는 리스트의 null을 못 쓰므로 빈 슬롯은 빈 객체로 저장한다.
                data.CheerSquad = gm.CheerSquadSlots.Select(c => c ?? new Cheerleader()).ToList();
            }

            if (LeagueManager.Instance != null)
            {
                var lm = LeagueManager.Instance;

                data.HasLeagueData = true;
                data.UserTeam = lm.UserTeam;
                data.PlayedGameCount = lm.PlayedGameCount;
                data.CurrentPhase = lm.CurrentPhase;
                data.UserFinalRank = lm.UserFinalRank ?? -1;
                data.LeagueTier = (int)lm.CurrentTier;
                data.SeasonRewardGranted = SeasonRewardManager.Instance != null && SeasonRewardManager.Instance.HasGrantedThisSeason;
                data.Standings = lm.GetStandings().Select(t => new TeamStandingSaveData
                {
                    Team = t.Team,
                    Wins = t.Wins,
                    Draws = t.Draws,
                    Losses = t.Losses,
                }).ToList();
            }

            if (LeagueCalendar.Instance != null)
            {
                data.CalendarDateIso = LeagueCalendar.Instance.CurrentDate.ToString("o");
            }

            if (SeasonRollover.Instance != null)
            {
                data.HallOfFame = SeasonRollover.Instance.HallOfFame.Select(e => new HallOfFameEntrySaveData
                {
                    SeasonYear = e.SeasonYear,
                    ChampionTeam = e.ChampionTeam,
                    BattingAverageLeader = e.BattingAverageLeader,
                    HomeRunLeader = e.HomeRunLeader,
                    WinsLeader = e.WinsLeader,
                    EraLeader = e.EraLeader,
                    SeasonNumber = e.SeasonNumber,
                    TierName = e.TierName,
                    FinalRank = e.FinalRank,
                    RegularSeasonRank = e.RegularSeasonRank,
                    Wins = e.Wins,
                    Draws = e.Draws,
                    Losses = e.Losses,
                    KoreanSeriesWon = e.KoreanSeriesWon,
                    Mvp = e.Mvp,
                    Titles = (e.Titles ?? new List<string>()).ToList(),
                }).ToList();
            }

            if (PostSeasonManager.Instance != null)
            {
                var pm = PostSeasonManager.Instance;
                data.HasPostSeasonData = true;
                data.PostSeason.Seeds = pm.Seeds.ToArray();
                data.PostSeason.ChampionTeam = pm.ChampionTeam ?? Team.None;
                data.PostSeason.FinalRankTeams = pm.FinalRanks.Keys.ToList();
                data.PostSeason.FinalRankValues = pm.FinalRanks.Values.ToList();

                if (pm.CurrentSeries != null)
                {
                    data.PostSeason.HasActiveSeries = true;
                    data.PostSeason.ActiveRound = pm.CurrentRound;
                    data.PostSeason.ActiveHigherSeed = pm.CurrentSeries.HigherSeed;
                    data.PostSeason.ActiveLowerSeed = pm.CurrentSeries.LowerSeed;
                    data.PostSeason.WinsRequiredForHigherSeed = pm.CurrentSeries.WinsRequiredForHigherSeed;
                    data.PostSeason.WinsRequiredForLowerSeed = pm.CurrentSeries.WinsRequiredForLowerSeed;
                    data.PostSeason.WinsHigherSeed = pm.CurrentSeries.WinsHigherSeed;
                    data.PostSeason.WinsLowerSeed = pm.CurrentSeries.WinsLowerSeed;
                }
            }

            return data;
        }

        private static PlayerSaveData ToSaveData(Player player) => new PlayerSaveData
        {
            InstanceId = player.InstanceId,
            TemplateId = player.Template != null ? player.Template.TemplateId : null,
            ReinforceLevel = player.ReinforceLevel,
            ReinforceExp = player.ReinforceExp,
            AwakenLevel = player.AwakenLevel,
            LimitBreakLevel = player.LimitBreakLevel,
            TrainingLevel = player.TrainingLevel,
            StarLevel = player.StarLevel,
            CurrentStarType = player.CurrentStarType,
            AcquiredSkillIds = new List<string>(player.AcquiredSkillIds),
            SkillSlots = (player.SkillSlots ?? new List<PlayerSkillSlot>()).Where(s => s != null).Select(s => s.Clone()).ToList(),
            CurrentStamina = player.CurrentStamina,
            MaxStamina = player.MaxStamina,
            Age = player.Age,
            Salary = player.Salary,
            ContractYears = player.ContractYears,
            EgoLevel = player.EgoLevel,
            RoleArchetype = player.RoleArchetype,
            PersonalMorale = player.PersonalMorale,
            IsCaptain = player.IsCaptain,
            HasRoleConcessionBonus = player.HasRoleConcessionBonus,
            IsScouted = player.IsScouted,
            InjuryRemainingDays = player.InjuryRemainingDays,
            CareerAwardIds = new List<string>(player.CareerAwardIds ?? new List<string>()),
        };

        // ----- [TASK-GM-02] 단장 모드 리그 -----

        public static GMLeagueSaveData ToGMSaveData(GMLeagueState league)
        {
            var data = new GMLeagueSaveData();
            if (league == null) return data;
            data.HasData = true;
            data.Mode = league.Mode;
            data.SeasonYear = league.SeasonYear;
            data.Phase = league.Phase;
            data.SelectedTeamCode = league.SelectedTeamCode;
            data.UseVirtualNames = league.UseVirtualNames;
            data.GamesPlayed = league.GamesPlayed;
            data.Seed = league.Seed;
            foreach (var team in league.Teams.Values)
            {
                var t = new GMTeamSaveData
                {
                    TeamCode = team.TeamCode,
                    IsUserTeam = team.IsUserTeam,
                    Budget = team.Budget,
                    PayrollCap = team.PayrollCap,
                    Roster = team.Roster.Select(ToSaveData).ToList(),
                    CheerleaderPool = new List<Cheerleader>(team.CheerleaderPool),
                    CheerEntrySize = team.CheerEntrySize,
                    ConsecutiveLastPlaceSeasons = team.ConsecutiveLastPlaceSeasons,
                    OwnerPostseasonPressure = team.OwnerPostseasonPressure,
                    TradeRequestPlayerId = team.TradeRequestPlayerId,
                    FanSupport = team.FanSupport,
                    CheerAutoRotate = team.CheerAutoRotate,
                    CheerDedications = team.CheerDedications.Select(d => new GMCheerDedication { CheerleaderId = d.CheerleaderId, PlayerId = d.PlayerId, GrantedConcession = d.GrantedConcession }).ToList(),
                    CheerFanPoints = team.CheerFanPoints,
                };
                t.Lineup.CopyFrom(team.Lineup);
                data.Teams.Add(t);
            }
            data.FreeAgents = league.FreeAgents.Select(ToSaveData).ToList();
            data.Records = league.Records.Values.ToList();
            data.Stats = league.Stats.Values.ToList();
            data.News = new List<GMNewsItem>(league.News);
            data.RecentUserBoxScores = new List<GMMatchBoxScoreData>(league.RecentUserBoxScores); // [TASK-GM-03]
            data.Awards = league.Awards ?? new SeasonAwardCeremonyBundle { SeasonYear = league.SeasonYear }; // [TASK-GM-04]
            data.AwardsHistory = new List<SeasonAwardCeremonyBundle>(league.SeasonAwardsHistory);
            return data;
        }

        /// <summary>저장 데이터 → 리그 상태. 선수 원본은 PlayerDatabase(TemplateId)에서 다시 붙인다. HasData가 false면 null.</summary>
        public GMLeagueState FromGMSaveData(GMLeagueSaveData data) => FromGMSaveData(data, RestorePlayer);

        public static GMLeagueState FromGMSaveData(GMLeagueSaveData data, Func<PlayerSaveData, Player> restore)
        {
            if (data == null || !data.HasData || data.Teams == null || data.Teams.Count == 0) return null;
            var league = new GMLeagueState
            {
                Mode = data.Mode,
                SeasonYear = data.SeasonYear,
                Phase = data.Phase,
                SelectedTeamCode = data.SelectedTeamCode,
                UseVirtualNames = data.UseVirtualNames,
                GamesPlayed = data.GamesPlayed,
                Seed = data.Seed,
            };
            foreach (var t in data.Teams)
            {
                var team = new GMTeamState
                {
                    TeamCode = t.TeamCode,
                    Team = NameAliasTable.ToTeam(t.TeamCode),
                    DisplayName = NameAliasTable.DisplayTeamName(t.TeamCode),
                    IsUserTeam = t.IsUserTeam,
                    Budget = t.Budget,
                    PayrollCap = t.PayrollCap,
                    CheerEntrySize = GMCheerleaderRules.ClampEntrySize(t.CheerEntrySize),
                    ConsecutiveLastPlaceSeasons = t.ConsecutiveLastPlaceSeasons,
                    OwnerPostseasonPressure = t.OwnerPostseasonPressure,
                    TradeRequestPlayerId = t.TradeRequestPlayerId,
                    FanSupport = GMTeamFan.Clamp(t.FanSupport),
                    CheerAutoRotate = t.CheerAutoRotate,
                    CheerFanPoints = t.CheerFanPoints,
                };
                team.CheerDedications.AddRange((t.CheerDedications ?? new List<GMCheerDedication>()).Where(d => d != null && !string.IsNullOrEmpty(d.PlayerId)));
                team.Lineup.CopyFrom(t.Lineup);
                foreach (var saved in t.Roster ?? new List<PlayerSaveData>())
                {
                    var p = restore(saved);
                    if (p != null) team.Roster.Add(p);
                }
                team.CheerleaderPool.AddRange((t.CheerleaderPool ?? new List<Cheerleader>()).Where(c => !CheerSquad.IsEmpty(c)));
                league.Teams[team.TeamCode] = team;
            }
            foreach (var saved in data.FreeAgents ?? new List<PlayerSaveData>())
            {
                var p = restore(saved);
                if (p != null) league.FreeAgents.Add(p);
            }
            foreach (var r in data.Records ?? new List<GMTeamRecord>()) if (!string.IsNullOrEmpty(r.TeamCode)) league.Records[r.TeamCode] = r;
            foreach (var s in data.Stats ?? new List<GMPlayerSeasonStats>()) if (!string.IsNullOrEmpty(s.PlayerId)) league.Stats[s.PlayerId] = s;
            league.News.AddRange(data.News ?? new List<GMNewsItem>());
            // [TASK-GM-04] 시상 묶음(구버전 세이브는 빈 묶음)
            league.Awards = data.Awards ?? new SeasonAwardCeremonyBundle();
            if (league.Awards.SeasonYear == 0) league.Awards.SeasonYear = data.SeasonYear;
            league.SeasonAwardsHistory.AddRange((data.AwardsHistory ?? new List<SeasonAwardCeremonyBundle>()).Where(b => b != null && b.SeasonYear > 0));
            league.RecentUserBoxScores.AddRange((data.RecentUserBoxScores ?? new List<GMMatchBoxScoreData>()).Where(b => b != null && !string.IsNullOrEmpty(b.HomeCode)));
            return league;
        }

        // ----- 역직렬화 -----

        private void ApplySaveData(GameSaveData data)
        {
            if (GameManager.Instance != null)
            {
                var gm = GameManager.Instance;

                var restoredPlayers = data.Inventory
                    .Select(RestorePlayer)
                    .Where(p => p != null)
                    .ToList();
                gm.ReplaceInventory(restoredPlayers);

                var rosterSet = new HashSet<string>(data.RosterInstanceIds ?? new List<string>());
                var restoredRoster = restoredPlayers.Where(p => rosterSet.Contains(p.InstanceId)).ToList();
                gm.OverwriteRoster(restoredRoster);

                gm.ReplaceItemInventory(RestoreItemInventory(data));

                gm.FavoriteTeam = data.FavoriteTeam;
                gm.LiveNormalTicket = data.LiveNormalTicket;
                gm.LiveEpicTicket = data.LiveEpicTicket;
                gm.PickupTicket = data.PickupTicket;
                gm.AdvancedTicket = data.AdvancedTicket;
                gm.Trophy = data.Trophy;
                gm.SignatureBall = data.SignatureBall;
                gm.LiveCheerStick = data.LiveCheerStick;
                gm.StarCheerStick = data.StarCheerStick;
                gm.LegendCheerStick = data.LegendCheerStick;
                gm.LimitedCheerStick = data.LimitedCheerStick;
                gm.GameGold = data.GameGold;
                gm.Uniform = data.Uniform;
                gm.Ticket = data.Ticket;
                gm.GrowthCoin = data.GrowthCoin;
                gm.AwakenTicket = data.AwakenTicket;
                gm.TranscendTicket = data.TranscendTicket;
                gm.TrainingTicket = data.TrainingTicket;
                gm.SkillChangeTicket = data.SkillChangeTicket;
                gm.PremiumSkillChangeTicket = data.PremiumSkillChangeTicket;
                gm.IsFirstLogin = data.IsFirstLogin;
                gm.ManagerNickname = data.ManagerNickname ?? "";
                gm.TutorialCompleted = data.TutorialCompleted;
                gm.SetBattingOrderOverride(data.BattingOrder);
                gm.RestoreLineupAssignment(data.Lineup);
                gm.RestoreStoveLeague(data.StoveLeague); // [TASK-KBO-193]
                gm.RestoreGMLeague(FromGMSaveData(data.GMLeague)); // [TASK-GM-02]
                gm.FanSentiment = data.FanSentiment;
                gm.LosingStreak = data.LosingStreak;
                gm.RestoreSetDeckSelection(data.SetDeckSelection); // [TASK-KBO-176] null/구버전이면 기본값(A안·자동 연도)

                // JsonUtility는 null 참조 필드를 저장할 때 JSON null이 아니라 "필드가 전부 기본값인
                // 인스턴스"로 직렬화한다(실측 확인 - 위 GameSaveData의 v5 주석 참고). 그 결과
                // data.EquippedCheerleader는 원본이 null이었어도 항상 non-null 객체로 역직렬화되므로,
                // InstanceId가 비어 있는지로 "실제로 저장된 치어리더가 있었는지"를 판별한다 - 실제
                // 치어리더는 GameManager.InitializeDevOnlyTestCheerleader() 등 모든 생성 경로에서
                // InstanceId를 항상 채우므로 안전한 판별 기준이다. EquipCheerleader(null)은
                // UnequipCheerleader()로 위임되므로 이 한 줄로 장착/해제 복원이 모두 처리된다.
                bool hasEquippedCheerleader = data.EquippedCheerleader != null &&
                    !string.IsNullOrEmpty(data.EquippedCheerleader.InstanceId);
                // [TASK-KBO-175] 구버전(단일 연도 CatalogId, Team/ActivePeriod 없음) 치어리더를 현행 카탈로그로 매핑한다.
                if (hasEquippedCheerleader) CheerleaderCatalog.Hydrate(data.EquippedCheerleader);
                gm.EquipCheerleader(hasEquippedCheerleader ? data.EquippedCheerleader : null);

                // OwnedCheerleaders가 없는 구버전 세이브(필드 자체가 JSON에 없음)를 불러오면
                // JsonUtility가 필드 초기값(빈 리스트)을 그대로 유지하므로 보통은 null이 되지 않지만,
                // 명령서 지시대로 방어적으로 null 체크를 유지한다.
                gm.OwnedCheerleaders.Clear();
                if (data.OwnedCheerleaders != null)
                {
                    foreach (var owned in data.OwnedCheerleaders) CheerleaderCatalog.Hydrate(owned); // [TASK-KBO-175]
                    gm.OwnedCheerleaders.AddRange(data.OwnedCheerleaders);
                }

                // [TASK-KBO-180] 6인 편성 복원. v7 세이브는 CheerSquad를, 구버전은 위에서 응원단장 슬롯에 넣은 단일 장착을 그대로 쓴다.
                // 같은 카드를 보유 목록과 같은 인스턴스로 맞춰(InstanceId) 이후 장착/해제 비교가 어긋나지 않게 한다.
                if (data.CheerSquad != null && data.CheerSquad.Any(c => c != null && !string.IsNullOrEmpty(c.InstanceId)))
                {
                    var restored = new List<Cheerleader>();
                    for (int i = 0; i < KBOManager.Models.CheerSquad.SlotCount; i++)
                    {
                        var saved = i < data.CheerSquad.Count ? data.CheerSquad[i] : null;
                        if (saved == null || string.IsNullOrEmpty(saved.InstanceId)) { restored.Add(null); continue; }
                        var owned = gm.OwnedCheerleaders.FirstOrDefault(c => c != null && c.InstanceId == saved.InstanceId);
                        if (owned == null) CheerleaderCatalog.Hydrate(saved);
                        restored.Add(owned ?? saved);
                    }
                    gm.RestoreCheerSquad(restored);
                }
                else if (hasEquippedCheerleader)
                {
                    var owned = gm.OwnedCheerleaders.FirstOrDefault(c => c != null && c.InstanceId == data.EquippedCheerleader.InstanceId);
                    if (owned != null) gm.EquippedCheerleader = owned; // 구버전 단일 슬롯 -> 응원단장(마이그레이션)
                }
            }

            if (data.HasLeagueData && LeagueManager.Instance != null)
            {
                var standingsData = (data.Standings ?? new List<TeamStandingSaveData>())
                    .Select(s => (s.Team, s.Wins, s.Draws, s.Losses));

                // [TASK-KBO-183] 리그 단계 - 구버전 세이브(-1)는 복원된 구단 OVR의 권장 리그(최소 아마추어)로.
                LeagueTier savedTier = data.LeagueTier >= 0 && System.Enum.IsDefined(typeof(LeagueTier), data.LeagueTier)
                    ? (LeagueTier)data.LeagueTier
                    : (LeagueTier)System.Math.Max((int)LeagueTier.Amateur, (int)LeagueTierTable.RecommendedFor(GameManager.Instance != null ? GameManager.Instance.CalculateTeamOVR() : 0));
                LeagueManager.Instance.RestoreFromSave(data.UserTeam, data.PlayedGameCount, data.CurrentPhase,
                    data.UserFinalRank, standingsData, savedTier);
                SeasonRewardManager.Instance?.RestoreGrantedFlag(data.SeasonRewardGranted); // [TASK-KBO-190]
            }

            // CalendarDateIso가 비어 있으면 캘린더 도입 이전(v2 이하) 세이브다 - 그 경우 그대로 두면
            // LeagueCalendar가 이미 갖고 있는 기본 날짜(또는 InitializeLeague가 새로 잡아 준 개막일)를
            // 유지하므로 크래시 없이 자연스럽게 호환된다.
            if (!string.IsNullOrEmpty(data.CalendarDateIso) && LeagueCalendar.Instance != null)
            {
                if (DateTime.TryParse(data.CalendarDateIso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
                {
                    LeagueCalendar.Instance.RestoreDate(parsedDate);
                }
            }

            if (SeasonRollover.Instance != null)
            {
                var restoredEntries = (data.HallOfFame ?? new List<HallOfFameEntrySaveData>())
                    .Select(saved => new HallOfFameEntry
                    {
                        SeasonYear = saved.SeasonYear,
                        ChampionTeam = saved.ChampionTeam,
                        BattingAverageLeader = saved.BattingAverageLeader,
                        HomeRunLeader = saved.HomeRunLeader,
                        WinsLeader = saved.WinsLeader,
                        EraLeader = saved.EraLeader,
                        SeasonNumber = saved.SeasonNumber,
                        TierName = saved.TierName,
                        FinalRank = saved.FinalRank,
                        RegularSeasonRank = saved.RegularSeasonRank,
                        Wins = saved.Wins,
                        Draws = saved.Draws,
                        Losses = saved.Losses,
                        KoreanSeriesWon = saved.KoreanSeriesWon,
                        Mvp = saved.Mvp,
                        Titles = (saved.Titles ?? new List<string>()).ToList(),
                    });
                SeasonRollover.Instance.ReplaceHallOfFame(restoredEntries);
            }

            if (data.HasPostSeasonData && PostSeasonManager.Instance != null)
            {
                var ps = data.PostSeason;
                var finalRankTeams = ps.FinalRankTeams ?? new List<Team>();
                var finalRankValues = ps.FinalRankValues ?? new List<int>();
                int pairCount = Mathf.Min(finalRankTeams.Count, finalRankValues.Count);
                var finalRanks = new List<(Team team, int rank)>(pairCount);
                for (int i = 0; i < pairCount; i++)
                {
                    finalRanks.Add((finalRankTeams[i], finalRankValues[i]));
                }

                PostSeasonManager.Instance.RestoreBracket(
                    ps.Seeds,
                    ps.ChampionTeam,
                    ps.HasActiveSeries ? ps.ActiveRound : (PostSeasonRound?)null,
                    ps.ActiveHigherSeed,
                    ps.ActiveLowerSeed,
                    ps.WinsRequiredForHigherSeed,
                    ps.WinsRequiredForLowerSeed,
                    ps.WinsHigherSeed,
                    ps.WinsLowerSeed,
                    finalRanks);
            }
        }

        /// <summary>[TASK-GM-02] 저장된 단장 모드 선수 속성을 복원한다.</summary>
        public static void ApplyGMFields(Player player, PlayerSaveData saved)
        {
            player.Age = saved.Age;
            player.Salary = saved.Salary;
            player.ContractYears = saved.ContractYears;
            player.EgoLevel = Math.Max(1, Math.Min(5, saved.EgoLevel));
            player.RoleArchetype = saved.RoleArchetype;
            player.PersonalMorale = Math.Max(0, Math.Min(100, saved.PersonalMorale));
            player.IsCaptain = saved.IsCaptain;
            player.HasRoleConcessionBonus = saved.HasRoleConcessionBonus;
            player.IsScouted = saved.IsScouted;
            player.InjuryRemainingDays = Math.Max(0, saved.InjuryRemainingDays);
            player.CareerAwardIds = new List<string>(saved.CareerAwardIds ?? new List<string>());
        }

        private Player RestorePlayer(PlayerSaveData saved)
        {
            if (playerDatabase == null || string.IsNullOrEmpty(saved.TemplateId)) return null;

            var template = playerDatabase.GetTemplateById(saved.TemplateId);
            if (template == null)
            {
                Debug.LogWarning($"[SaveManager] TemplateId '{saved.TemplateId}'를 PlayerDatabase에서 찾을 수 없어 " +
                                  "카드 하나를 복구하지 못했습니다. (해당 .asset이 삭제/변경되었을 수 있습니다)");
                return null;
            }

            var player = new Player(saved.InstanceId, template)
            {
                ReinforceLevel = saved.ReinforceLevel,
                ReinforceExp = saved.ReinforceExp,
                AwakenLevel = saved.AwakenLevel,
                LimitBreakLevel = saved.LimitBreakLevel,
                TrainingLevel = saved.TrainingLevel,
                StarLevel = saved.StarLevel,
                CurrentStarType = saved.CurrentStarType,
                AcquiredSkillIds = new List<string>(saved.AcquiredSkillIds ?? new List<string>()),
                SkillSlots = (saved.SkillSlots ?? new List<PlayerSkillSlot>()).Where(s => s != null && !string.IsNullOrEmpty(s.SkillId)).Select(s => s.Clone()).ToList(),
            };
            PlayerSkillRules.EnsureSlots(player); // [TASK-KBO-190]
            ApplyGMFields(player, saved); // [TASK-GM-02]

            // 체력 필드 도입(v2) 이전 세이브는 CurrentStamina/MaxStamina가 JSON에 아예 없어 역직렬화 시
            // 기본값(0)이 된다. 투수인데 MaxStamina가 0이면 "체력 데이터가 없던 구버전 세이브"로 간주해,
            // Player 생성자가 이미 채워 둔 롤 기준 기본값(완전 회복 상태)을 그대로 둔다(덮어쓰지 않음) -
            // 그 결과 구버전 세이브를 불러오면 모든 투수가 "완전히 쉰 상태"로 자연스럽게 시작한다.
            // 그 외(정상 저장된 값, 또는 애초에 체력이 없는 타자 카드)는 저장된 값을 그대로 복원한다.
            bool isLegacySaveMissingStamina = template.IsPitcher && saved.MaxStamina <= 0;
            if (!isLegacySaveMissingStamina)
            {
                player.MaxStamina = saved.MaxStamina;
                player.CurrentStamina = saved.CurrentStamina;
            }

            return player;
        }

        /// <summary>
        /// data.ItemStacks(v2, 수량 그룹)가 있으면 그것을 우선 사용하고, 비어 있으면 data.ItemInventory
        /// (v1 이하, 인스턴스별 1행)로 대체한다 - ItemStacks 필드 자체가 존재하지 않던 구버전 JSON은
        /// 역직렬화 시 자동으로 빈 리스트가 되므로, 이 순서만으로 "신규/구버전 세이브"를 안전하게 구분할
        /// 수 있다(별도의 버전 분기 없이 필드 존재 여부만으로 판별).
        /// </summary>
        private List<Item> RestoreItemInventory(GameSaveData data)
        {
            if (data.ItemStacks != null && data.ItemStacks.Count > 0)
            {
                var restored = new List<Item>();
                foreach (var stack in data.ItemStacks)
                {
                    for (int i = 0; i < stack.Count; i++)
                    {
                        var item = RestoreItemByTemplateId(stack.TemplateId);
                        if (item != null) restored.Add(item);
                    }
                }
                return restored;
            }

            return (data.ItemInventory ?? new List<ItemSaveData>())
                .Select(saved => RestoreItemByTemplateId(saved.TemplateId))
                .Where(i => i != null)
                .ToList();
        }

        private Item RestoreItemByTemplateId(string templateId)
        {
            if (itemDatabase == null || string.IsNullOrEmpty(templateId)) return null;

            var template = itemDatabase.GetTemplateById(templateId);
            if (template == null)
            {
                Debug.LogWarning($"[SaveManager] TemplateId '{templateId}'를 ItemDatabase에서 찾을 수 없어 " +
                                  "재료 카드 하나를 복구하지 못했습니다. (해당 .asset이 삭제/변경되었을 수 있습니다)");
                return null;
            }

            return new Item(Guid.NewGuid().ToString(), template);
        }
    }
}
