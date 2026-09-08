using System.Collections.Generic;
using KBOManager.Controllers;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Tools
{
    /// <summary>
    /// 인게임 개발자/QA 디버그 패널. 인스펙터를 켜지 않고도 즉시 게임 루프를 검증할 수 있는 버튼
    /// 4개를 제공한다. 실제 QA 빌드에서도 접근 가능해야 하므로 컴파일 조건(#if UNITY_EDITOR 등)으로
    /// 걸어 잘라내지 않는다 - 대신 숨겨진 단축키(기본 F1) 또는 화면 구석 투명 버튼 5연속 탭이라는
    /// "제스처 기반 은닉"이 유일한 접근 제한이다(과제 원문이 명시한 방식 그대로).
    /// </summary>
    public class DebugPanelUI : MonoBehaviour
    {
        [Header("Open Triggers")]
        [SerializeField] private KeyCode toggleHotkey = KeyCode.F1;
        [Tooltip("화면 구석에 배치한 투명(또는 최소한의) 버튼. tapWindowSeconds 안에 requiredTapCount번 눌러야 패널이 열린다.")]
        [SerializeField] private Button hiddenCornerTapButton;
        [SerializeField] private int requiredTapCount = 5;
        [SerializeField] private float tapWindowSeconds = 2f;

        [Header("Panel")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;
        [Tooltip("각 버튼 클릭 결과를 보여주는 텍스트. 비워두면 Debug.Log로만 남는다.")]
        [SerializeField] private Text resultText;

        [Header("Debug Actions")]
        [SerializeField] private Button grantPremiumCurrencyButton;
        [SerializeField] private int premiumCurrencyGrantAmount = 10000;
        [SerializeField] private Button forceWinCurrentMatchButton;
        [SerializeField] private PlayBallController playBallController;
        [SerializeField] private Button skipRegularSeasonButton;
        [SerializeField] private Button recoverAllPlayersButton;

        private int tapCount;
        private float lastTapTime;

        private void Awake()
        {
            if (panelRoot != null) panelRoot.SetActive(false);

            if (hiddenCornerTapButton != null) hiddenCornerTapButton.onClick.AddListener(HandleCornerTap);
            if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);

            if (grantPremiumCurrencyButton != null) grantPremiumCurrencyButton.onClick.AddListener(GrantPremiumCurrency);
            if (forceWinCurrentMatchButton != null) forceWinCurrentMatchButton.onClick.AddListener(ForceWinCurrentMatch);
            if (skipRegularSeasonButton != null) skipRegularSeasonButton.onClick.AddListener(SkipToEndOfRegularSeason);
            if (recoverAllPlayersButton != null) recoverAllPlayersButton.onClick.AddListener(RecoverAllPlayers);
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleHotkey)) TogglePanel();
        }

        private void HandleCornerTap()
        {
            float now = Time.unscaledTime;
            if (now - lastTapTime > tapWindowSeconds) tapCount = 0; // 시간 초과 - 연속 탭이 아니었던 것으로 간주

            lastTapTime = now;
            tapCount++;

            if (tapCount >= requiredTapCount)
            {
                tapCount = 0;
                OpenPanel();
            }
        }

        private void TogglePanel()
        {
            if (panelRoot == null) return;
            panelRoot.SetActive(!panelRoot.activeSelf);
        }

        private void OpenPanel()
        {
            if (panelRoot != null) panelRoot.SetActive(true);
        }

        public void ClosePanel()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ----- 디버그 액션 -----

        private void GrantPremiumCurrency()
        {
            if (GameManager.Instance == null) return;

            GameManager.Instance.PremiumCurrency += premiumCurrencyGrantAmount;
            ShowResult($"프리미엄 재화 +{premiumCurrencyGrantAmount} 지급 완료. (현재 {GameManager.Instance.PremiumCurrency})");
        }

        private void ForceWinCurrentMatch()
        {
            if (playBallController == null)
            {
                ShowResult("PlayBallController가 연결되지 않았습니다.");
                return;
            }

            playBallController.DebugForceWinCurrentMatch();
            ShowResult("현재 경기를 유저 팀 승리로 강제 종료했습니다.");
        }

        /// <summary>남은 정규시즌 경기를 전부 즉시 시뮬레이션하고, 유저가 포스트시즌에 진출했다면
        /// 그 브래킷까지 챔피언이 나올 때까지 이어서 자동 진행한다 - 어느 경우든 SeasonRollover.
        /// RolloverToNextSeason()을 호출하기 직전 상태로 맞춘다.</summary>
        private void SkipToEndOfRegularSeason()
        {
            if (LeagueManager.Instance == null) return;

            LeagueManager.Instance.PlayUntil(LeagueManager.TotalUserGames);

            if (PostSeasonManager.Instance != null)
            {
                // 라운드 4개 x 시리즈당 최대 7경기 + 여유분 - 무한 루프 방지용 상한(정상 흐름에서는 항상
                // 그 전에 IsPostSeasonActive가 false가 되어 루프가 끝난다).
                int safety = 4 * 7 + 4;
                while (PostSeasonManager.Instance.IsPostSeasonActive && safety-- > 0)
                {
                    PostSeasonManager.Instance.PlayNextSeriesGame();
                }
            }

            ShowResult("정규 시즌(+포스트시즌)을 전부 스킵했습니다. 이제 SeasonRollover.RolloverToNextSeason() 호출 차례입니다.");
        }

        private void RecoverAllPlayers()
        {
            if (GameManager.Instance != null)
            {
                RecoverAndRefresh(GameManager.Instance.Inventory);
            }

            if (LeagueManager.Instance != null)
            {
                foreach (var info in LeagueManager.Instance.GetStandings())
                {
                    RecoverAndRefresh(info.Roster);
                }
            }

            ShowResult("모든 선수의 체력을 MAX로, 컨디션을 '최상'으로 회복했습니다.");
        }

        private static void RecoverAndRefresh(IEnumerable<Player> players)
        {
            if (players == null) return;

            foreach (var player in players)
            {
                if (player == null) continue;
                if (player.MaxStamina > 0) player.CurrentStamina = player.MaxStamina;
                player.CurrentCondition = PlayerCondition.Excellent;
            }
        }

        private void ShowResult(string message)
        {
            if (resultText != null) resultText.text = message;
            Debug.Log($"[DebugPanelUI] {message}");
        }
    }
}
