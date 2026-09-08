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

        [Header("Set Deck Rule")]
        [Tooltip("동일 구단 선수가 이 인원 이상이면 세트덱 보너스 활성화 (TODO: GDD 밸런스 확정 후 구간별 세분화)")]
        [SerializeField] private int setDeckActivationThreshold = 5;
        [SerializeField] private float setDeckBonusMultiplier = 1.15f;

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

        /// <summary>
        /// 1군 로스터(28인) 내 구단별 인원수를 집계하고, 세트덱 보너스 활성화 여부/배율을 판단한다.
        /// TODO: GDD 밸런스 확정 후 인원 구간(예: 5/10/15명)에 따른 보너스 단계 세분화 필요.
        /// </summary>
        /// <param name="dominantTeam">로스터 내 가장 많은 인원을 보유한 구단</param>
        /// <param name="isBonusActive">세트덱 보너스 활성화 여부</param>
        /// <param name="bonusMultiplier">활성화 시 적용할 OVR 배율 (Player.CalculateOVR에 전달)</param>
        /// <returns>구단별 편성 인원수</returns>
        public Dictionary<Team, int> CheckSetDeckBonus(out Team dominantTeam, out bool isBonusActive, out float bonusMultiplier)
        {
            var countByTeam = roster
                .Where(p => p.Template != null && p.Template.Team != Team.None)
                .GroupBy(p => p.Template.Team)
                .ToDictionary(g => g.Key, g => g.Count());

            dominantTeam = Team.None;
            int maxCount = 0;
            foreach (var pair in countByTeam)
            {
                if (pair.Value > maxCount)
                {
                    maxCount = pair.Value;
                    dominantTeam = pair.Key;
                }
            }

            isBonusActive = maxCount >= setDeckActivationThreshold;
            bonusMultiplier = isBonusActive ? setDeckBonusMultiplier : 1.0f;

            return countByTeam;
        }
    }
}
