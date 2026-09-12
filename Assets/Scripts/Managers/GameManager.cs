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

        [Tooltip("true인 동안은 UIManager가 로비 대신 온보딩(선호 구단 선택) 화면을 강제 출력한다. " +
                 "OnboardingManager.CompleteOnboarding()이 스타터 팩 지급을 마친 뒤 false로 내린다.")]
        [SerializeField] private bool isFirstLogin = true;
        public bool IsFirstLogin
        {
            get => isFirstLogin;
            set => isFirstLogin = value;
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

        // ----- 팀 OVR (GDD v4.0) -----

        private static readonly BatterPosition[] AllBatterPositions =
            (BatterPosition[])System.Enum.GetValues(typeof(BatterPosition));

        private const int BenchBatterQuota = 4;  // 후보 타자 인원
        private const int BenchReliefQuota = 6;  // 후보 구원(롱/중/셋) 인원

        /// <summary>
        /// 구단 OVR = (주전 15인 평균 OVR * 0.8) + (후보 10인 평균 OVR * 0.2).
        /// 주전 15 = 포지션별 최고 OVR 타자 9 + 선발투수 5 + 마무리 1.
        /// 후보 10 = 나머지 타자 중 OVR 상위 4 + 나머지 구원(승리조/추격조/롱릴리프) 중 OVR 상위 6.
        /// [TASK-KBO-031 공식화] 28인 로스터 중 (28 - 15 - 10 =) 3명(타자 2명, 투수 1명)은 각 그룹
        /// (벤치 타자/구원) 내 OVR 최하위 순으로 자동 제외된다 - 실제 KBO의 28인 등록/25인 경기 엔트리
        /// 구조를 본뜬 공식 스펙으로 확정됨(이 계산 방식 자체는 변경 없음, 기존 구현을 그대로 유지).
        /// 로스터가 비어 있거나 해당 그룹에 아무도 없으면 그 그룹의 평균은 0으로 취급한다(0으로 나누기 방지).
        /// 세트덱 보너스는 로스터 구성 자체가 세트덱 활성화 여부를 좌우하는 순환 참조를 피하기 위해
        /// (RosterManager의 다른 OVR 계산들과 동일하게) 반영하지 않는다.
        /// </summary>
        public float CalculateTeamOVR()
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

            return (starterAvg * 0.8f) + (benchAvg * 0.2f);
        }
    }
}
