using System;
using System.Collections.Generic;
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
    }

    /// <summary>
    /// Item도 Player와 동일한 경량화 원칙을 따른다: ItemTemplate 전체를 직렬화하지 않고 TemplateId만
    /// 저장한다 - 로드 시 ItemDatabase에서 TemplateId로 원본을 다시 찾아 붙인다.
    /// </summary>
    [Serializable]
    public class ItemSaveData
    {
        public string InstanceId;
        public string TemplateId;
    }

    [Serializable]
    public class TeamStandingSaveData
    {
        public Team Team;
        public int Wins;
        public int Draws;
        public int Losses;
    }

    /// <summary>
    /// 저장 파일 전체 스키마. JsonUtility로 직렬화하므로 int?/Dictionary 등 JsonUtility가 지원하지
    /// 않는 타입은 쓰지 않는다(Nullable은 정수 -1을 "값 없음" 대용으로 사용, Dictionary는 List로 대체).
    /// </summary>
    [Serializable]
    public class GameSaveData
    {
        public int SaveVersion = 1;
        public string SavedAtUtc;

        // GameManager
        public List<PlayerSaveData> Inventory = new List<PlayerSaveData>();
        public List<string> RosterInstanceIds = new List<string>(); // Inventory 중 로스터에 편성된 카드의 InstanceId
        public List<ItemSaveData> ItemInventory = new List<ItemSaveData>();
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
                data.ItemInventory = gm.ItemInventory.Select(ToSaveData).ToList();
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
        };

        private static ItemSaveData ToSaveData(Item item) => new ItemSaveData
        {
            InstanceId = item.InstanceId,
            TemplateId = item.Template != null ? item.Template.TemplateId : null,
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

                var restoredItems = (data.ItemInventory ?? new List<ItemSaveData>())
                    .Select(RestoreItem)
                    .Where(i => i != null)
                    .ToList();
                gm.ReplaceItemInventory(restoredItems);

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

            return new Player(saved.InstanceId, template)
            {
                ReinforceLevel = saved.ReinforceLevel,
                AwakenLevel = saved.AwakenLevel,
                StarLevel = saved.StarLevel,
                CurrentStarType = saved.CurrentStarType,
                AcquiredSkillIds = new List<string>(saved.AcquiredSkillIds ?? new List<string>()),
            };
        }

        private Item RestoreItem(ItemSaveData saved)
        {
            if (itemDatabase == null || string.IsNullOrEmpty(saved.TemplateId)) return null;

            var template = itemDatabase.GetTemplateById(saved.TemplateId);
            if (template == null)
            {
                Debug.LogWarning($"[SaveManager] TemplateId '{saved.TemplateId}'를 ItemDatabase에서 찾을 수 없어 " +
                                  "재료 카드 하나를 복구하지 못했습니다. (해당 .asset이 삭제/변경되었을 수 있습니다)");
                return null;
            }

            return new Item(saved.InstanceId, template);
        }
    }
}
