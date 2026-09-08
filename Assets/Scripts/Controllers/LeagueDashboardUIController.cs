using System.Linq;
using KBOManager.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 메인 로비 화면. 현재 시즌 진행도/순위표/다음 매치업을 보여주고, GDD 5절의 3가지 플레이 방식
    /// 버튼(빠른 진행/하이라이트/풀 플레이)을 PlayBallController.StartMatch(PlayMode)에 연결한다.
    /// 표시 데이터는 전부 LeagueManager를 그대로 읽어오기만 하며 시뮬레이션/저장 로직은 갖지 않는다.
    /// </summary>
    public class LeagueDashboardUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LeagueManager leagueManager;
        [SerializeField] private PlayBallController playBallController;

        [Header("Season Progress")]
        [SerializeField] private Text seasonProgressText;

        [Header("Standings (1~10위 고정 10줄)")]
        [Tooltip("10개 구단 고정이므로 동적 생성 없이 텍스트 10줄을 그대로 인스펙터에서 연결한다.")]
        [SerializeField] private Text[] standingsRowTexts;

        [Header("Next Matchup")]
        [SerializeField] private Text nextMatchupText;

        [Header("Play Mode Buttons")]
        [SerializeField] private Button quickPlayButton;
        [SerializeField] private Button highlightButton;
        [SerializeField] private Button fullPlayButton;

        [Header("Screen Roots (패널 전환 뼈대)")]
        [Tooltip("경기 시작 시 이 로비 화면을 숨긴다.")]
        [SerializeField] private GameObject lobbyScreenRoot;
        [Tooltip("경기 시작 시 이 인게임 화면을 켠다. InGameUIController.ReturnToLobby()가 다시 꺼 준다.")]
        [SerializeField] private GameObject inGameScreenRoot;

        private void Awake()
        {
            if (quickPlayButton != null) quickPlayButton.onClick.AddListener(() => StartMatch(PlayMode.QuickPlay));
            if (highlightButton != null) highlightButton.onClick.AddListener(() => StartMatch(PlayMode.Highlight));
            if (fullPlayButton != null) fullPlayButton.onClick.AddListener(() => StartMatch(PlayMode.FullPlay));
        }

        private void OnEnable()
        {
            RefreshDashboard();
        }

        /// <summary>LeagueManager의 현재 상태(시즌 진행도/순위표/다음 매치업)를 다시 읽어 전부 갱신한다.</summary>
        public void RefreshDashboard()
        {
            if (leagueManager == null) return;

            RefreshSeasonProgress();
            RefreshStandings();
            RefreshNextMatchup();
        }

        private void RefreshSeasonProgress()
        {
            if (seasonProgressText == null) return;

            seasonProgressText.text = $"{leagueManager.CurrentPhase} - {leagueManager.PlayedGameCount} / {LeagueManager.TotalUserGames} 경기";
        }

        private void RefreshStandings()
        {
            if (standingsRowTexts == null) return;

            var standings = leagueManager.GetStandings();
            for (int i = 0; i < standingsRowTexts.Length; i++)
            {
                if (standingsRowTexts[i] == null) continue;

                if (i < standings.Count)
                {
                    var team = standings[i];
                    standingsRowTexts[i].text = $"{i + 1}위  {team.Team}  {team.Wins}승 {team.Draws}무 {team.Losses}패  {team.WinRate:F3}";
                }
                else
                {
                    standingsRowTexts[i].text = "";
                }
            }
        }

        private void RefreshNextMatchup()
        {
            if (nextMatchupText == null) return;

            var fixture = leagueManager.PeekNextFixture();
            if (fixture == null)
            {
                nextMatchupText.text = "예정된 경기가 없습니다.";
                return;
            }

            bool userIsHome = fixture.HomeTeam == leagueManager.UserTeam;
            var opponentTeam = userIsHome ? fixture.AwayTeam : fixture.HomeTeam;
            var opponentRoster = leagueManager.ResolveRosterForTeam(opponentTeam);

            float opponentOvr = opponentRoster.Count > 0
                ? (float)opponentRoster.Average(p => p.CalculateOVR(false))
                : 0f;

            string venue = userIsHome ? "홈" : "원정";
            nextMatchupText.text = $"다음 경기: vs {opponentTeam} ({venue})  상대 평균 OVR {opponentOvr:F1}";
        }

        /// <summary>
        /// 플레이 모드 버튼 OnClick. PlayBallController.StartMatch(mode)를 호출하고, 로비 화면을 숨긴 뒤
        /// 인게임 화면을 켠다(뼈대 수준 패널 전환 - InGameUIController.ReturnToLobby()가 역방향을 담당).
        /// </summary>
        private void StartMatch(PlayMode mode)
        {
            if (playBallController == null) return;

            playBallController.StartMatch(mode);

            if (lobbyScreenRoot != null) lobbyScreenRoot.SetActive(false);
            if (inGameScreenRoot != null) inGameScreenRoot.SetActive(true);
        }
    }
}
