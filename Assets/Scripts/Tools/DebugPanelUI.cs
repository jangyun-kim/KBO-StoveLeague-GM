using System.Collections;
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
        [SerializeField] private Button grantCheerStickButton;
        [SerializeField] private int cheerStickGrantAmount = 10000;
        [SerializeField] private Button forceWinCurrentMatchButton;
        [SerializeField] private PlayBallController playBallController;
        [SerializeField] private Button skipRegularSeasonButton;
        [SerializeField] private Button recoverAllPlayersButton;
        [Tooltip("스킵 완료 직후 순위표를 갱신할 로비 화면. 비워두면 갱신 호출을 건너뛴다.")]
        [SerializeField] private LeagueDashboardUIController leagueDashboard;

        // 한 프레임에 이 개수만큼만 시뮬레이션하고 yield return null로 한 프레임 양보한다 - 144경기를
        // 전부 한 프레임에 몰아서 처리하면 그 프레임만 스파이크가 나므로, 몇 프레임에 걸쳐 얇게 펴서
        // 눈에 띄는 끊김 없이 "몇 초" 안에 끝나도록 한다.
        private const int GamesPerFrameDuringSkip = 20;

        private int tapCount;
        private float lastTapTime;
        private bool isSkipInProgress;

        private void Awake()
        {
            if (panelRoot != null) panelRoot.SetActive(false);

            if (hiddenCornerTapButton != null) hiddenCornerTapButton.onClick.AddListener(HandleCornerTap);
            if (closeButton != null) closeButton.onClick.AddListener(ClosePanel);

            if (grantCheerStickButton != null) grantCheerStickButton.onClick.AddListener(GrantCheerStick);
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

        private void GrantCheerStick()
        {
            if (GameManager.Instance == null) return;

            GameManager.Instance.CheerStick += cheerStickGrantAmount;
            ShowResult($"응원봉 +{cheerStickGrantAmount} 지급 완료. (현재 {GameManager.Instance.CheerStick})");
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
        /// RolloverToNextSeason()을 호출하기 직전 상태로 맞춘다. 버튼 중복 클릭으로 코루틴이 여러 개
        /// 겹쳐 도는 것을 막기 위해 isSkipInProgress로 재진입을 막는다.</summary>
        private void SkipToEndOfRegularSeason()
        {
            if (isSkipInProgress || LeagueManager.Instance == null) return;

            StartCoroutine(SkipToEndOfRegularSeasonRoutine());
        }

        /// <summary>
        /// LeagueManager.PlayNextMatch()/PostSeasonManager.PlayNextSeriesGame() 자체는 이미 완전히
        /// 헤드리스다(PlayBallController를 거치지 않으므로 UI 이벤트/애니메이션이 원천적으로 발생하지
        /// 않는다) - 이 코루틴이 GamesPerFrameDuringSkip경기마다 한 번 yield return null로 양보하는
        /// 이유는 그 UI 이벤트를 억제하기 위해서가 아니라, 144경기 전부를 한 프레임에 몰아 계산하면
        /// 그 한 프레임만 스파이크가 나는 것을 막기 위함이다(여러 프레임에 얇게 펴서 그래도 몇 초 안에
        /// 끝나도록). 완료 직후 로비 순위표를 명시적으로 새로고침한다 - LeagueDashboardUIController는
        /// OnEnable()에서만 스스로 갱신하므로, 로비 화면이 이미 켜져 있는 상태로 스킵했다면 아무도
        /// 대신 다시 그려주지 않기 때문이다.
        /// </summary>
        private IEnumerator SkipToEndOfRegularSeasonRoutine()
        {
            isSkipInProgress = true;
            if (skipRegularSeasonButton != null) skipRegularSeasonButton.interactable = false;
            ShowResult("정규 시즌 스킵 진행 중...");

            int processed = 0;
            while (LeagueManager.Instance.PeekNextFixture() != null &&
                   LeagueManager.Instance.PeekNextFixture().GameNumber <= LeagueManager.TotalUserGames)
            {
                LeagueManager.Instance.PlayNextMatch();

                if (++processed % GamesPerFrameDuringSkip == 0) yield return null;
            }

            if (PostSeasonManager.Instance != null)
            {
                // 라운드 4개 x 시리즈당 최대 7경기 + 여유분 - 무한 루프 방지용 상한(정상 흐름에서는 항상
                // 그 전에 IsPostSeasonActive가 false가 되어 루프가 끝난다).
                int safety = 4 * 7 + 4;
                while (PostSeasonManager.Instance.IsPostSeasonActive && safety-- > 0)
                {
                    PostSeasonManager.Instance.PlayNextSeriesGame();

                    if (++processed % GamesPerFrameDuringSkip == 0) yield return null;
                }
            }

            if (leagueDashboard != null) leagueDashboard.RefreshDashboard();

            if (skipRegularSeasonButton != null) skipRegularSeasonButton.interactable = true;
            isSkipInProgress = false;

            ShowResult($"정규 시즌(+포스트시즌) {processed}경기를 전부 스킵하고 로비 순위표를 갱신했습니다. " +
                       "이제 SeasonRollover.RolloverToNextSeason() 호출 차례입니다.");
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
