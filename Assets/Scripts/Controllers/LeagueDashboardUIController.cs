using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 메인 로비 화면. 현재 시즌 진행도/순위표/다음 매치업을 보여주고, GDD 5절의 3가지 플레이 방식
    /// 버튼(빠른 진행/하이라이트/풀 플레이)을 PlayBallController.StartMatch(PlayMode)에 연결한다.
    /// 표시 데이터는 전부 LeagueManager를 그대로 읽어오기만 하며 시뮬레이션/저장 로직은 갖지 않는다.
    ///
    /// 화면 전환은 UIManager.Instance.ShowScreen()에만 위임한다 - InGameUIController를 직접 참조하지
    /// 않으므로(양방향 결합 제거), 다른 화면 컨트롤러가 몇 개가 늘어나도 이 클래스를 고칠 필요가 없다.
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
        [Tooltip("GameManager.Instance.FavoriteTeam과 같은 구단 행을 강조할 색상.")]
        [SerializeField] private string favoriteTeamHighlightColor = "#FFEB3B";

        [Header("Next Matchup")]
        [SerializeField] private Text nextMatchupText;

        [Header("Play Mode Buttons")]
        [SerializeField] private Button quickPlayButton;
        [SerializeField] private Button highlightButton;
        [SerializeField] private Button fullPlayButton;

        [Header("Navigation Buttons")]
        [Tooltip("상점 화면(UIManager.ScreenType.Shop)으로 전환하는 버튼. UIManager.ShowScreen()만 호출하므로" +
                 " ShopUIController를 직접 참조하지 않는다.")]
        [SerializeField] private Button shopButton;

        private void Awake()
        {
            if (quickPlayButton != null) quickPlayButton.onClick.AddListener(() => StartMatch(PlayMode.QuickPlay));
            if (highlightButton != null) highlightButton.onClick.AddListener(() => StartMatch(PlayMode.Highlight));
            if (fullPlayButton != null) fullPlayButton.onClick.AddListener(() => StartMatch(PlayMode.FullPlay));
            if (shopButton != null) shopButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Shop));
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
            var favoriteTeam = GameManager.Instance != null ? GameManager.Instance.FavoriteTeam : Team.None;

            for (int i = 0; i < standingsRowTexts.Length; i++)
            {
                if (standingsRowTexts[i] == null) continue;

                if (i < standings.Count)
                {
                    var team = standings[i];
                    string line = $"{i + 1}위  {team.Team}  {team.Wins}승 {team.Draws}무 {team.Losses}패  {team.WinRate:F3}";

                    bool isFavoriteTeam = favoriteTeam != Team.None && team.Team == favoriteTeam;
                    standingsRowTexts[i].text = isFavoriteTeam
                        ? $"<b><color={favoriteTeamHighlightColor}>{line}</color></b>"
                        : line;
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
        /// 플레이 모드 버튼 OnClick. PlayBallController.StartMatch(mode)를 호출한 뒤, UIManager에게
        /// 인게임 화면으로 전환해 달라고만 요청한다 - InGameUIController를 직접 참조하지 않는다.
        /// </summary>
        private void StartMatch(PlayMode mode)
        {
            if (playBallController == null) return;

            playBallController.StartMatch(mode);
            UIManager.Instance?.ShowScreen(ScreenType.InGame);
        }
    }
}
