using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KBOManager.Models;
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
        public int AwakenLevel;
        public int StarLevel;
        public StarType CurrentStarType;
        public List<string> AcquiredSkillIds = new List<string>();

        // 체력 필드 도입(v2) 이전 세이브에는 이 두 값이 JSON에 아예 없어 역직렬화 시 기본값(0)이 된다.
        // RestorePlayer()가 "투수인데 MaxStamina가 0"인 경우를 "구버전 세이브"로 간주해 Player 생성자의
        // 롤 기준 기본값(완전 회복 상태)을 그대로 둔다 - 자세한 내용은 RestorePlayer() 참고.
        public int CurrentStamina;
        public int MaxStamina;
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

    /// <summary>명예의 전당(SeasonRollover.HallOfFame) 항목 1개의 저장 포맷. HallOfFameEntry의
    /// Team? ChampionTeam은 JsonUtility가 Nullable&lt;T&gt;를 지원하지 않아 그대로 저장할 수 없으므로,
    /// 여기서는 Team.None을 "그 시즌 우승팀 기록 없음" 대용으로 쓴다(UserFinalRank의 -1과 같은 관례).</summary>
    [Serializable]
    public class HallOfFameEntrySaveData
    {
        public int SeasonYear;
        public Team ChampionTeam;
        public string BattingAverageLeader;
        public string HomeRunLeader;
        public string WinsLeader;
        public string EraLeader;
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
        public int SaveVersion = 3;
        public string SavedAtUtc;

        // GameManager
        public List<PlayerSaveData> Inventory = new List<PlayerSaveData>();
        public List<string> RosterInstanceIds = new List<string>(); // Inventory 중 로스터에 편성된 카드의 InstanceId
        public List<ItemSaveData> ItemInventory = new List<ItemSaveData>(); // v1 이하 세이브 하위 호환 전용 - 새 저장은 항상 빈 리스트
        public List<ItemStackSaveData> ItemStacks = new List<ItemStackSaveData>();
        public Team FavoriteTeam;
        public int ScoutReport;
        public int PremiumCurrency;
        public int GameGold;
        public bool IsFirstLogin = true;

        // LeagueManager
        public bool HasLeagueData;
        public Team UserTeam;
        public int PlayedGameCount;
        public LeaguePhase CurrentPhase;
        public int UserFinalRank = -1; // -1 = 아직 시즌을 완주하지 않음(null 대용)
        public List<TeamStandingSaveData> Standings = new List<TeamStandingSaveData>();

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
                data.ScoutReport = gm.ScoutReport;
                data.PremiumCurrency = gm.PremiumCurrency;
                data.GameGold = gm.GameGold;
                data.IsFirstLogin = gm.IsFirstLogin;
            }

            if (LeagueManager.Instance != null)
            {
                var lm = LeagueManager.Instance;

                data.HasLeagueData = true;
                data.UserTeam = lm.UserTeam;
                data.PlayedGameCount = lm.PlayedGameCount;
                data.CurrentPhase = lm.CurrentPhase;
                data.UserFinalRank = lm.UserFinalRank ?? -1;
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
                    ChampionTeam = e.ChampionTeam ?? Team.None,
                    BattingAverageLeader = e.BattingAverageLeader,
                    HomeRunLeader = e.HomeRunLeader,
                    WinsLeader = e.WinsLeader,
                    EraLeader = e.EraLeader,
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
            AwakenLevel = player.AwakenLevel,
            StarLevel = player.StarLevel,
            CurrentStarType = player.CurrentStarType,
            AcquiredSkillIds = new List<string>(player.AcquiredSkillIds),
            CurrentStamina = player.CurrentStamina,
            MaxStamina = player.MaxStamina,
        };

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
                gm.ScoutReport = data.ScoutReport;
                gm.PremiumCurrency = data.PremiumCurrency;
                gm.GameGold = data.GameGold;
                gm.IsFirstLogin = data.IsFirstLogin;
            }

            if (data.HasLeagueData && LeagueManager.Instance != null)
            {
                var standingsData = (data.Standings ?? new List<TeamStandingSaveData>())
                    .Select(s => (s.Team, s.Wins, s.Draws, s.Losses));

                LeagueManager.Instance.RestoreFromSave(data.UserTeam, data.PlayedGameCount, data.CurrentPhase,
                    data.UserFinalRank, standingsData);
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
                        ChampionTeam = saved.ChampionTeam == Team.None ? (Team?)null : saved.ChampionTeam,
                        BattingAverageLeader = saved.BattingAverageLeader,
                        HomeRunLeader = saved.HomeRunLeader,
                        WinsLeader = saved.WinsLeader,
                        EraLeader = saved.EraLeader,
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
                AwakenLevel = saved.AwakenLevel,
                StarLevel = saved.StarLevel,
                CurrentStarType = saved.CurrentStarType,
                AcquiredSkillIds = new List<string>(saved.AcquiredSkillIds ?? new List<string>()),
            };

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
