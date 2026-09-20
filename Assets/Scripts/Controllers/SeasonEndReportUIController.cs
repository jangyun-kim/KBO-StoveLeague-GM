using System.Collections.Generic;
using KBOManager.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 시즌 결산 리포트 팝업. LeagueDashboardUIController가 떠 있는 로비 화면 위에 겹쳐 뜨는 오버레이라
    /// 씬 계층상 그 화면의 자식으로 두면 되지만, 코드 레벨로는 LeagueDashboardUIController를 전혀
    /// 참조하지 않는다 - SeasonRewardManager.OnSeasonEndReportReady를 직접 구독해 스스로 열고 닫는다
    /// (MatchRewardManager/InGameUIController가 이미 쓰고 있는, 같은 이벤트를 여러 컴포넌트가 독립적으로
    /// 구독하는 패턴과 동일하다).
    /// </summary>
    public class SeasonEndReportUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SeasonRewardManager seasonRewardManager;

        [Header("Popup")]
        [SerializeField] private GameObject popupRoot;
        [SerializeField] private Text finalRankText;
        [SerializeField] private Text rewardSummaryText;
        [SerializeField] private Button closeButton;

        [Header("Title Holders (타이틀 홀더 명단, 한 줄짜리 Text 프리팹 동적 생성)")]
        [SerializeField] private Transform titleHolderListContainer;
        [SerializeField] private Text titleHolderEntryPrefab;

        private readonly List<Text> spawnedEntries = new List<Text>();

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(ClosePopup);
            if (popupRoot != null) popupRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (seasonRewardManager != null) seasonRewardManager.OnSeasonEndReportReady += ShowReport;
        }

        private void OnDisable()
        {
            if (seasonRewardManager != null) seasonRewardManager.OnSeasonEndReportReady -= ShowReport;
        }

        private void ShowReport(SeasonEndReport report)
        {
            if (report == null) return;

            if (finalRankText != null)
            {
                finalRankText.text = report.FinalRank <= 1 ? "🏆 한국시리즈 우승!" : $"최종 순위: {report.FinalRank}위";
            }

            if (rewardSummaryText != null)
            {
                string packLine = string.IsNullOrEmpty(report.GuaranteedPackGrade)
                    ? ""
                    : $"\n확정팩: {report.GuaranteedPackGrade} 이상 1장";
                rewardSummaryText.text = $"영입권 +{report.ScoutTicketGained}\n게임 머니 +{report.GameGoldGained}{packLine}";
            }

            SpawnTitleHolders(report.TitleHolders);

            if (popupRoot != null) popupRoot.SetActive(true);
        }

        private void SpawnTitleHolders(List<(string Category, string PlayerName, string ValueLabel)> titleHolders)
        {
            ClearEntries();
            if (titleHolderEntryPrefab == null || titleHolderListContainer == null) return;

            foreach (var (category, playerName, valueLabel) in titleHolders)
            {
                var entry = Instantiate(titleHolderEntryPrefab, titleHolderListContainer);
                entry.gameObject.SetActive(true);
                entry.text = $"{category} 1위: {playerName} ({valueLabel})";
                spawnedEntries.Add(entry);
            }
        }

        private void ClearEntries()
        {
            foreach (var entry in spawnedEntries)
            {
                if (entry != null) Destroy(entry.gameObject);
            }
            spawnedEntries.Clear();
        }

        /// <summary>닫기 버튼 OnClick.</summary>
        public void ClosePopup()
        {
            if (popupRoot != null) popupRoot.SetActive(false);
        }
    }
}
