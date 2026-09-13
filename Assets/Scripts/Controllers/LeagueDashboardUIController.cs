using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 메인 로비 화면. 현재 시즌 진행도/순위표/다음 매치업을 보여주고, 관전 시작 버튼을
    /// PlayBallController.StartMatch()에 연결한다(GDD v4.0: 수동 개입 방식 폐지로 단일 플레이 방식만 남음).
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
        [Tooltip("세트덱 시너지/치어리더/팬심 요약을 담당하는 어댑터(TASK-KBO-053). 비워두면 다른 대시보드 " +
                 "정보 갱신에는 영향 없이 이 항목만 건너뛴다.")]
        [SerializeField] private TeamSynergyUIController synergyUIController;

        [Header("Season Progress")]
        [SerializeField] private Text seasonProgressText;

        [Header("Standings (1~10위 고정 10줄)")]
        [Tooltip("10개 구단 고정이므로 동적 생성 없이 텍스트 10줄을 그대로 인스펙터에서 연결한다.")]
        [SerializeField] private Text[] standingsRowTexts;
        [Tooltip("GameManager.Instance.FavoriteTeam과 같은 구단 행을 강조할 색상.")]
        [SerializeField] private string favoriteTeamHighlightColor = "#FFEB3B";

        [Header("Next Matchup")]
        [SerializeField] private Text nextMatchupText;

        [Header("Team OVR (GDD v4.0 14절)")]
        [Tooltip("GameManager.Instance.CalculateTeamOVR() 결과를 표시. 씬에 아직 전용 텍스트 오브젝트가 " +
                 "없다면, 유니티 에디터에서 로비 패널 아래 남는(미사용) Text 오브젝트를 하나 이 필드에 " +
                 "드래그해 연결하거나 새 Text를 만들어 연결할 것 - 비워두면(null) 아무 것도 표시되지 않을 " +
                 "뿐 에러는 나지 않는다.")]
        [SerializeField] private Text teamOVRText;

        [Header("Currency")]
        [Tooltip("[TASK-KBO-071] 로비 화면 최상단에 유저의 GameManager.Instance.PremiumCurrency 보유량을 " +
                 "표시한다(가챠를 돌리기 전 얼마나 있는지 바로 확인하기 위함). 비워두면(null) 아무 것도 " +
                 "표시되지 않을 뿐 에러는 나지 않는다.")]
        [SerializeField] private Text premiumCurrencyLobbyText;

        [Header("Play Mode Buttons")]
        [Tooltip("GDD v4.0: 수동 개입 방식(하이라이트 개입/풀 플레이)은 폐지되어 관전 모드 진입 버튼만 남았다.")]
        [SerializeField] private Button quickPlayButton;

        [Header("Navigation Buttons")]
        [Tooltip("상점 화면(UIManager.ScreenType.Shop)으로 전환하는 버튼. UIManager.ShowScreen()만 호출하므로" +
                 " ShopUIController를 직접 참조하지 않는다.")]
        [SerializeField] private Button shopButton;
        [Tooltip("리그 기록실(UIManager.ScreenType.LeagueStats)로 전환하는 버튼.")]
        [SerializeField] private Button leagueStatsButton;
        [Tooltip("치어리더 관리 화면(UIManager.ScreenType.CheerleaderInventory)으로 전환하는 버튼. " +
                 "CheerleaderInventoryUIController를 직접 참조하지 않는다. 기존 ScreenType.Inventory는 " +
                 "선수 카드 인벤토리(InventoryUIController) 전용으로 이미 예약돼 있어 재사용하지 않았다.")]
        [SerializeField] private Button manageCheerleaderButton;
        [Tooltip("치어리더 가챠 상점 화면(UIManager.ScreenType.CheerleaderShop)으로 전환하는 버튼. " +
                 "CheerleaderShopUIController를 직접 참조하지 않는다. 기존 ScreenType.Shop은 선수 카드 " +
                 "상점(ShopUIController) 전용으로 이미 쓰이고 있어 재사용하지 않았다.")]
        [SerializeField] private Button gachaShopButton;

        private void Awake()
        {
            if (quickPlayButton != null) quickPlayButton.onClick.AddListener(StartMatch);
            if (shopButton != null) shopButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Shop));
            if (leagueStatsButton != null) leagueStatsButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.LeagueStats));
            if (manageCheerleaderButton != null) manageCheerleaderButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.CheerleaderInventory));
            if (gachaShopButton != null) gachaShopButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.CheerleaderShop));
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
            RefreshTeamOVR();
            RefreshPremiumCurrency();

            if (synergyUIController != null) synergyUIController.RefreshSynergyUI();
        }

        /// <summary>[TASK-KBO-071] 로비 상단의 보유 프리미엄 재화 표시를 갱신한다. teamOVRText와 동일한
        /// 관례로, 텍스트가 비어 있거나 GameManager.Instance가 아직 없으면 조용히 건너뛴다.</summary>
        private void RefreshPremiumCurrency()
        {
            if (premiumCurrencyLobbyText == null || GameManager.Instance == null) return;

            premiumCurrencyLobbyText.text = $"재화: {GameManager.Instance.PremiumCurrency}";
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

        /// <summary>GameManager.Instance.CalculateTeamOVR()(주전15*0.8 + 후보10*0.2, 반올림 후 시너지 가산)
        /// 결과를 그대로 표시한다. teamOVRText가 인스펙터에 연결되지 않았거나 GameManager가 아직 없으면
        /// (씬 초기화 순서 등) 조용히 건너뛴다 - NullReferenceException을 내지 않는다.</summary>
        private void RefreshTeamOVR()
        {
            if (teamOVRText == null || GameManager.Instance == null) return;

            teamOVRText.text = $"OVR {GameManager.Instance.CalculateTeamOVR()}";
        }

        /// <summary>
        /// 관전 시작 버튼 OnClick. PlayBallController.StartMatch()를 호출한 뒤, UIManager에게
        /// 인게임 화면으로 전환해 달라고만 요청한다 - InGameUIController를 직접 참조하지 않는다.
        /// </summary>
        private void StartMatch()
        {
            if (playBallController == null) return;

            playBallController.StartMatch();
            UIManager.Instance?.ShowScreen(ScreenType.InGame);
        }
    }
}
