using System.Collections.Generic;
using KBOManager.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// PlayBallController의 진행 이벤트(OnAtBatEnd/OnInningEnd)를 구독해 이닝별 스코어보드와
    /// 텍스트 중계창을 실시간으로 갱신하는 UI 브릿지. 시뮬레이션 로직은 전혀 갖지 않는다 -
    /// 여기서 하는 일은 오직 "받은 데이터를 화면에 그린다"뿐이다.
    ///
    /// UGUI Text로 작성했다. 프로젝트에 TextMeshPro(TMP Essentials)가 설치돼 있다면 필드 타입을
    /// UnityEngine.UI.Text -> TMPro.TextMeshProUGUI로, using UnityEngine.UI -> using TMPro로만
    /// 바꾸면 그대로 동작한다(로직은 텍스트 컴포넌트의 .text 프로퍼티만 사용하므로 API 차이가 없다).
    /// </summary>
    public class InGameUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayBallController playBallController;

        [Header("Scoreboard - 이닝별 득점 (배열 순서 = 1회, 2회, ... 연장 포함)")]
        [SerializeField] private Text[] awayInningTexts;
        [SerializeField] private Text[] homeInningTexts;
        [SerializeField] private Text awayTotalText;
        [SerializeField] private Text homeTotalText;
        [Tooltip("말 공격이 끝내기로 생략된 이닝 칸에 표시할 문자.")]
        [SerializeField] private string skippedInningLabel = "X";
        [SerializeField] private string emptyInningLabel = "";

        [Header("Text Broadcast Log")]
        [Tooltip("로그 한 줄짜리 프리팹(비활성 상태의 Text 컴포넌트). 스크롤 뷰의 Content 하위에 생성된다.")]
        [SerializeField] private Text logEntryPrefab;
        [Tooltip("로그를 쌓을 스크롤 뷰의 Content Transform.")]
        [SerializeField] private Transform logContainer;
        [SerializeField] private int maxLogEntries = 200;
        [Tooltip("새 로그가 추가될 때 스크롤 뷰를 맨 아래로 내릴지 여부.")]
        [SerializeField] private ScrollRect logScrollRect;

        [Header("Match End / Return to Lobby (뼈대 수준 패널 전환)")]
        [Tooltip("스코어보드/중계창을 포함한 인게임 화면 전체 루트.")]
        [SerializeField] private GameObject inGameScreenRoot;
        [Tooltip("경기 종료 시 노출되는 결과 요약 패널.")]
        [SerializeField] private GameObject matchEndPanelRoot;
        [SerializeField] private Text matchResultSummaryText;
        [SerializeField] private Button returnToLobbyButton;
        [SerializeField] private GameObject lobbyScreenRoot;
        [SerializeField] private LeagueDashboardUIController leagueDashboardUIController;

        private readonly List<Text> spawnedLogEntries = new List<Text>();

        private void Awake()
        {
            if (returnToLobbyButton != null) returnToLobbyButton.onClick.AddListener(ReturnToLobby);
        }

        private void OnEnable()
        {
            if (playBallController == null) return;
            playBallController.OnAtBatEnd += HandleAtBatEnd;
            playBallController.OnInningEnd += HandleInningEnd;
            playBallController.OnSubstitutionLog += AddLog;
            playBallController.OnMatchCompleted += HandleMatchCompleted;
        }

        private void OnDisable()
        {
            if (playBallController == null) return;
            playBallController.OnAtBatEnd -= HandleAtBatEnd;
            playBallController.OnInningEnd -= HandleInningEnd;
            playBallController.OnSubstitutionLog -= AddLog;
            playBallController.OnMatchCompleted -= HandleMatchCompleted;
        }

        private void HandleAtBatEnd(AtBatStepResult step)
        {
            if (step == null || string.IsNullOrEmpty(step.LogMessage)) return;
            AddLog(step.LogMessage);
        }

        private void HandleInningEnd(AtBatStepResult step)
        {
            RefreshScoreboard();
        }

        /// <summary>PlayBallController.Engine.Result의 이닝별 스코어(HomeInningScores/AwayInningScores)를
        /// 그대로 읽어 스코어보드 텍스트를 다시 그린다. 별도 상태를 들고 있지 않고 항상 엔진 결과를 그대로 반영한다.</summary>
        private void RefreshScoreboard()
        {
            var result = playBallController?.Engine?.Result;
            if (result == null) return;

            for (int i = 0; i < awayInningTexts.Length; i++)
            {
                if (awayInningTexts[i] == null) continue;
                awayInningTexts[i].text = i < result.AwayInningScores.Count
                    ? result.AwayInningScores[i].ToString()
                    : emptyInningLabel;
            }

            for (int i = 0; i < homeInningTexts.Length; i++)
            {
                if (homeInningTexts[i] == null) continue;

                if (i < result.HomeInningScores.Count)
                {
                    homeInningTexts[i].text = result.HomeInningScores[i].ToString();
                }
                else
                {
                    // 원정이 이미 그 이닝 초를 쳤는데(=AwayInningScores에는 값이 있는데) 홈은 아직 없다면,
                    // 끝내기로 말 공격이 생략된 것으로 간주해 X로 표기한다.
                    homeInningTexts[i].text = i < result.AwayInningScores.Count ? skippedInningLabel : emptyInningLabel;
                }
            }

            if (awayTotalText != null) awayTotalText.text = result.AwayTotalScore.ToString();
            if (homeTotalText != null) homeTotalText.text = result.HomeTotalScore.ToString();
        }

        /// <summary>텍스트 중계창에 로그 한 줄을 추가한다. maxLogEntries를 넘으면 가장 오래된 줄부터 지운다.</summary>
        public void AddLog(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            if (logEntryPrefab == null || logContainer == null)
            {
                Debug.Log($"[InGameUIController] {message}");
                return;
            }

            var entry = Instantiate(logEntryPrefab, logContainer);
            entry.text = message;
            entry.gameObject.SetActive(true);
            spawnedLogEntries.Add(entry);

            while (spawnedLogEntries.Count > maxLogEntries)
            {
                var oldest = spawnedLogEntries[0];
                spawnedLogEntries.RemoveAt(0);
                if (oldest != null) Destroy(oldest.gameObject);
            }

            if (logScrollRect != null)
            {
                logScrollRect.verticalNormalizedPosition = 0f; // 가장 최근 로그(맨 아래)로 스크롤
            }
        }

        /// <summary>스코어보드/로그를 모두 비운다. 새 경기를 시작하기 전에 호출한다.</summary>
        public void ClearAll()
        {
            foreach (var text in awayInningTexts) if (text != null) text.text = emptyInningLabel;
            foreach (var text in homeInningTexts) if (text != null) text.text = emptyInningLabel;
            if (awayTotalText != null) awayTotalText.text = "0";
            if (homeTotalText != null) homeTotalText.text = "0";

            foreach (var entry in spawnedLogEntries)
            {
                if (entry != null) Destroy(entry.gameObject);
            }
            spawnedLogEntries.Clear();
        }

        /// <summary>
        /// PlayBallController.OnMatchCompleted 핸들러. 경기 결과 요약을 채우고 결과 패널을 연다.
        /// 이 시점에는 이미 LeagueManager.CompleteNextFixture()로 순위표/다음 경기 포인터가 갱신된 뒤다
        /// (PlayBallController.FinishMatch()가 OnMatchCompleted를 발생시키기 전에 먼저 호출한다).
        /// </summary>
        private void HandleMatchCompleted(MatchResult result)
        {
            if (matchEndPanelRoot != null) matchEndPanelRoot.SetActive(true);

            if (matchResultSummaryText != null && result != null)
            {
                string winnerText = result.WinnerTeamName == null ? "무승부" : $"{result.WinnerTeamName} 승";
                matchResultSummaryText.text =
                    $"최종 스코어\n{result.AwayTeamName} {result.AwayTotalScore} : {result.HomeTotalScore} {result.HomeTeamName}\n{winnerText}";
            }
        }

        /// <summary>
        /// 경기 결과 패널의 [로비로 돌아가기] 버튼 OnClick. 인게임 화면을 닫고 리그 대시보드를 다시
        /// 켠 뒤 RefreshDashboard()로 최신 순위/다음 매치업을 반영한다(뼈대 수준 패널 전환).
        /// </summary>
        public void ReturnToLobby()
        {
            if (matchEndPanelRoot != null) matchEndPanelRoot.SetActive(false);
            if (inGameScreenRoot != null) inGameScreenRoot.SetActive(false);

            if (lobbyScreenRoot != null) lobbyScreenRoot.SetActive(true);
            if (leagueDashboardUIController != null)
            {
                leagueDashboardUIController.gameObject.SetActive(true);
                leagueDashboardUIController.RefreshDashboard();
            }

            ClearAll();
        }
    }
}
