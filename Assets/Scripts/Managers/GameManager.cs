using System.Collections.Generic;
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

        [Header("Roster Rule")]
        [SerializeField] private int maxRosterSize = 26; // 1군 로스터 최대 인원

        // ----- 유저 보유 선수 -----
        [SerializeField] private List<Player> inventory = new List<Player>();
        [SerializeField] private List<Player> roster = new List<Player>();

        /// <summary>유저가 보유한 전체 선수 카드.</summary>
        public List<Player> Inventory => inventory;

        /// <summary>1군 로스터에 편성된 선수 카드.</summary>
        public List<Player> Roster => roster;

        // ----- 유저 프로필 -----
        [Header("Profile")]
        [SerializeField] private Team favoriteTeam = Team.None;
        public Team FavoriteTeam
        {
            get => favoriteTeam;
            set => favoriteTeam = value;
        }

        // ----- 재화 -----
        [Header("Currency")]
        [SerializeField] private int scoutReport;   // 스카우트 리포트 (뽑기 재화)
        [SerializeField] private int premiumCurrency; // 프리미엄 재화 (유료성 재화)
        [SerializeField] private int gameGold;      // 게임 머니

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

        // ----- 인벤토리/로스터 헬퍼 -----

        /// <summary>새로 획득한 선수를 인벤토리에 추가한다.</summary>
        public void AddPlayerToInventory(Player player)
        {
            if (player == null) return;
            inventory.Add(player);
        }

        /// <summary>인벤토리의 선수를 1군 로스터에 편성한다.</summary>
        public bool AddPlayerToRoster(Player player)
        {
            if (player == null) return false;
            if (roster.Count >= maxRosterSize) return false;
            if (!inventory.Contains(player)) return false;
            if (roster.Contains(player)) return false;

            roster.Add(player);
            return true;
        }

        /// <summary>1군 로스터에서 선수를 제외한다.</summary>
        public bool RemovePlayerFromRoster(Player player)
        {
            return player != null && roster.Remove(player);
        }
    }
}
