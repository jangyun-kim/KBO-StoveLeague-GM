using System;
using System.Collections.Generic;
using KBOManager.Controllers;
using KBOManager.Engine;
using KBOManager.Models;
using UnityEngine;
using Random = UnityEngine.Random;

namespace KBOManager.Managers
{
    /// <summary>경기 1회에 지급된 보상 내역. InGameUIController가 결과창에 표시하는 데 쓴다.</summary>
    public class MatchRewardResult
    {
        public bool Won;
        public bool Draw;
        public int ScoutReportGained;
        public List<Item> ItemsGained = new List<Item>();
    }

    /// <summary>
    /// 경기 종료(PlayBallController.OnMatchCompleted) 시 승/무/패에 따라 차등 보상을 즉시
    /// GameManager(ScoutReport, ItemInventory)에 지급하는 싱글톤. [경기 -> 재화/아이템 획득] 구간을
    /// 담당하는 코어 루프의 한 축이다.
    /// </summary>
    public class MatchRewardManager : MonoBehaviour
    {
        public static MatchRewardManager Instance { get; private set; }

        [Header("References")]
        [Tooltip("보상 지급의 트리거가 되는 PlayBallController.OnMatchCompleted를 구독한다.")]
        [SerializeField] private PlayBallController playBallController;
        [Tooltip("무작위 아이템 드롭 시 후보 템플릿을 가져올 DB.")]
        [SerializeField] private ItemDatabase itemDatabase;

        [Header("Reward Amounts")]
        [SerializeField] private int winScoutReportReward = 100;
        [SerializeField] private int loseOrDrawScoutReportReward = 30;
        [SerializeField] private int winMinItemDrop = 1;
        [SerializeField] private int winMaxItemDrop = 3;
        [SerializeField] private int loseOrDrawItemDrop = 1;

        /// <summary>보상 지급이 끝날 때마다 발생. InGameUIController 등이 구독해 결과창에 표시한다.</summary>
        public event Action<MatchRewardResult> OnRewardGranted;

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

        private void OnEnable()
        {
            if (playBallController != null)
            {
                playBallController.OnMatchCompleted += HandleMatchCompleted;
            }
        }

        private void OnDisable()
        {
            if (playBallController != null)
            {
                playBallController.OnMatchCompleted -= HandleMatchCompleted;
            }
        }

        private void HandleMatchCompleted(MatchResult result)
        {
            GrantRewardForMatch(result);
        }

        /// <summary>
        /// LeagueManager.Instance.UserTeam 기준으로 승/무/패를 판별해 즉시 보상을 지급한다.
        /// 승리: 스카우트 리포트 100 + 강화 재료 1~3장. 무승부/패배: 30 + 1장.
        /// </summary>
        public MatchRewardResult GrantRewardForMatch(MatchResult result)
        {
            if (result == null || GameManager.Instance == null) return null;

            bool isDraw = result.WinnerTeamName == null;
            bool won = !isDraw && LeagueManager.Instance != null
                && result.WinnerTeamName == LeagueManager.Instance.UserTeam.ToString();

            int scoutReward = won ? winScoutReportReward : loseOrDrawScoutReportReward;
            GameManager.Instance.ScoutReport += scoutReward;

            int itemCount = won ? Random.Range(winMinItemDrop, winMaxItemDrop + 1) : loseOrDrawItemDrop;
            var grantedItems = new List<Item>();

            for (int i = 0; i < itemCount; i++)
            {
                var item = RollRandomItem();
                if (item == null) continue;

                GameManager.Instance.AddItemToInventory(item);
                grantedItems.Add(item);
            }

            var rewardResult = new MatchRewardResult
            {
                Won = won,
                Draw = isDraw,
                ScoutReportGained = scoutReward,
                ItemsGained = grantedItems,
            };

            OnRewardGranted?.Invoke(rewardResult);
            return rewardResult;
        }

        private Item RollRandomItem()
        {
            if (itemDatabase == null || itemDatabase.AllTemplates.Count == 0) return null;

            var template = itemDatabase.AllTemplates[Random.Range(0, itemDatabase.AllTemplates.Count)];
            return new Item(Guid.NewGuid().ToString(), template);
        }
    }
}
