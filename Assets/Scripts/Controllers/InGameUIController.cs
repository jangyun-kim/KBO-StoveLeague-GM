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

        private readonly List<Text> spawnedLogEntries = new List<Text>();

        private void OnEnable()
        {
            if (playBallController == null) return;
            playBallController.OnAtBatEnd += HandleAtBatEnd;
            playBallController.OnInningEnd += HandleInningEnd;
            playBallController.OnSubstitutionLog += AddLog;
        }

        private void OnDisable()
        {
            if (playBallController == null) return;
            playBallController.OnAtBatEnd -= HandleAtBatEnd;
            playBallController.OnInningEnd -= HandleInningEnd;
            playBallController.OnSubstitutionLog -= AddLog;
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
    }
}
