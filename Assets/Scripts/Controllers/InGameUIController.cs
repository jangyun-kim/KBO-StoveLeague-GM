using System.Collections.Generic;
using System.Linq;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.UI;
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
    ///
    /// 로비로 돌아갈 때 UIManager.Instance.ShowScreen()만 호출한다 - LeagueDashboardUIController를
    /// 직접 참조하지 않으므로(양방향 결합 제거), 로비 쪽 구현이 바뀌어도 이 클래스는 영향받지 않는다.
    /// </summary>
    public class InGameUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayBallController playBallController;
        [Tooltip("MatchRewardManager.OnRewardGranted를 구독해 보상 내역을 결과창에 표시한다. 비워두면 보상 표시를 생략한다.")]
        [SerializeField] private MatchRewardManager matchRewardManager;
        [Tooltip("다이아몬드/볼카운트 UI. PlayBallController.OnAtBatEnd는 스스로 구독하므로, 여기서는 " +
                 "새 경기 시작 전 잔상을 지우는 ResetDisplay() 호출만 위임한다. 비워두면 초기화를 생략한다.")]
        [SerializeField] private MatchStatusUI matchStatusUI;

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

        [Header("Match End / Return to Lobby")]
        [Tooltip("경기 종료 시 노출되는 결과 요약 패널.")]
        [SerializeField] private GameObject matchEndPanelRoot;
        [SerializeField] private Text matchResultSummaryText;
        [SerializeField] private Text rewardSummaryText;
        [SerializeField] private Button returnToLobbyButton;

        private readonly List<Text> spawnedLogEntries = new List<Text>();

        private void Awake()
        {
            if (returnToLobbyButton != null) returnToLobbyButton.onClick.AddListener(ReturnToLobby);
        }

        private void OnEnable()
        {
            if (playBallController != null)
            {
                playBallController.OnAtBatEnd += HandleAtBatEnd;
                playBallController.OnInningEnd += HandleInningEnd;
                playBallController.OnMatchCompleted += HandleMatchCompleted;
            }

            if (matchRewardManager != null)
            {
                matchRewardManager.OnRewardGranted += HandleRewardGranted;
            }
        }

        private void OnDisable()
        {
            if (playBallController != null)
            {
                playBallController.OnAtBatEnd -= HandleAtBatEnd;
                playBallController.OnInningEnd -= HandleInningEnd;
                playBallController.OnMatchCompleted -= HandleMatchCompleted;
            }

            if (matchRewardManager != null)
            {
                matchRewardManager.OnRewardGranted -= HandleRewardGranted;
            }
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

            matchStatusUI?.ResetDisplay();
        }

        /// <summary>
        /// PlayBallController.OnMatchCompleted 핸들러. ShowMatchResult()에 위임한다.
        /// 이 시점에는 이미 LeagueManager.CompleteNextFixture()로 순위표/다음 경기 포인터가 갱신된 뒤다
        /// (PlayBallController.FinishMatch()가 OnMatchCompleted를 발생시키기 전에 먼저 호출한다).
        /// 보상 지급은 별도로 MatchRewardManager가 같은 이벤트를 구독해 처리하고, 그 결과가
        /// OnRewardGranted로 도착하면 HandleRewardGranted가 이어서 화면에 반영한다.
        /// </summary>
        private void HandleMatchCompleted(MatchResult result) => ShowMatchResult(result);

        /// <summary>
        /// [TASK-KBO-040] 경기 결과 요약을 채우고 결과 패널을 연다. PlayBallController.OnMatchCompleted를
        /// 통해서만이 아니라, BroadcastUIManager처럼 자체적으로 경기를 재생하는 외부 컨트롤러도 재생 종료
        /// 시(또는 스킵 시) 직접 호출할 수 있도록 public으로 노출한다 - 이 메서드는 순수 UI 갱신만 하며
        /// 어떤 매니저/엔진 상태도 직접 건드리지 않는다. result가 null이어도(호출부 오류 등) 결과 패널은
        /// 그대로 열되 텍스트만 갱신을 건너뛴다.
        /// </summary>
        public void ShowMatchResult(MatchResult result)
        {
            if (matchEndPanelRoot != null) matchEndPanelRoot.SetActive(true);

            if (matchResultSummaryText != null && result != null)
            {
                string winnerText = result.WinnerTeamName == null ? "무승부" : $"{result.WinnerTeamName} 승";
                matchResultSummaryText.text =
                    $"최종 스코어\n{result.AwayTeamName} {result.AwayTotalScore} : {result.HomeTotalScore} {result.HomeTeamName}\n{winnerText}";
            }
        }

        /// <summary>MatchRewardManager.OnRewardGranted 핸들러. 지급된 재화/아이템 내역을 결과창에 표시한다.</summary>
        private void HandleRewardGranted(MatchRewardResult reward)
        {
            if (rewardSummaryText == null || reward == null) return;

            string itemsLine = reward.ItemsGained.Count > 0
                ? string.Join(", ", reward.ItemsGained.Select(i => i.Template != null ? i.Template.DisplayName : "알 수 없는 재료"))
                : "없음";

            rewardSummaryText.text = $"보상: 스카우트 리포트 +{reward.ScoutReportGained}\n획득 아이템: {itemsLine}";
        }

        /// <summary>
        /// 경기 결과 패널의 [로비로 돌아가기] 버튼 OnClick. UIManager에게 로비 화면으로 전환해 달라고만
        /// 요청한다 - 로비 화면이 켜지는 순간 LeagueDashboardUIController.OnEnable()이 자동으로
        /// RefreshDashboard()를 호출하므로, 여기서 그 컨트롤러를 직접 참조해 호출할 필요가 없다.
        /// </summary>
        public void ReturnToLobby()
        {
            if (matchEndPanelRoot != null) matchEndPanelRoot.SetActive(false);

            UIManager.Instance?.ShowScreen(ScreenType.Lobby);

            ClearAll();
        }
    }
}
