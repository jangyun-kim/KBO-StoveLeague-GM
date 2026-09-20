using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>부문 1개(예: 타율)의 TOP 3 출력 단위. 1위는 축소 PlayerCardUI로, 2~3위는 텍스트 한 줄씩으로 표기한다.</summary>
    [Serializable]
    public class StatPanelEntry
    {
        public Text TitleText;
        [Tooltip("1위 선수를 보여줄 축소 카드의 루트. 랭킹에 아무도 없으면(예: 아직 규정 타석을 채운 선수가 없음) 비활성화된다.")]
        public GameObject FirstPlaceCardRoot;
        public PlayerCardUI FirstPlaceCard;
        public Text SecondPlaceText;
        public Text ThirdPlaceText;
    }

    /// <summary>
    /// 리그 기록실(타이틀 홀더) 화면. SeasonStatManager의 리그 전체(유저 팀 + AI 9개 구단 모두 포함) 누적
    /// 기록에서 부문별 TOP 3를 뽑아 보여준다 - 이 화면이 다루는 "선수"는 유저 인벤토리에 국한되지 않고,
    /// 이번 시즌 실제로 타석/이닝을 소화한 리그의 모든 선수(AI 포함)다.
    ///
    /// 상단 탭([타자 순위]/[투수 순위])이 2x2 바둑판 패널 묶음을 통째로 바꿔 보여준다. SeasonStatManager가
    /// 실제로 추적하는 부문은 타율/홈런/다승/방어율 4가지뿐이라, 그 외 GDD가 언급한 다른 세부 부문(타점/
    /// 출루율/탈삼진/세이브 등)은 "준비 중" 텍스트로만 자리를 채워 뒀다 - 존재하지 않는 데이터를 지어내지
    /// 않기 위함이다.
    /// </summary>
    public class LeagueStatsUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SeasonStatManager seasonStatManager;
        [SerializeField] private LeagueManager leagueManager;

        [Header("Tabs")]
        [SerializeField] private Button batterTabButton;
        [SerializeField] private Button pitcherTabButton;
        [SerializeField] private Button hallOfFameTabButton;
        [SerializeField] private GameObject batterPanelRoot;
        [SerializeField] private GameObject pitcherPanelRoot;
        [SerializeField] private GameObject hallOfFamePanelRoot;

        [Header("Hall of Fame Tab (역대 시즌 기록 - SaveManager가 복원한 SeasonRollover.HallOfFame)")]
        [SerializeField] private SeasonRollover seasonRollover;
        [SerializeField] private Transform hallOfFameListContainer;
        [SerializeField] private Text hallOfFameEntryPrefab;

        [Header("Batter Panels (2x2) - 타율 / 홈런")]
        [SerializeField] private StatPanelEntry battingAveragePanel;
        [SerializeField] private StatPanelEntry homeRunPanel;
        [Tooltip("아직 추적하지 않는 부문(예: 타점) 자리 표시용.")]
        [SerializeField] private Text battingPlaceholderText1;
        [SerializeField] private Text battingPlaceholderText2;

        [Header("Pitcher Panels (2x2) - 다승 / 방어율")]
        [SerializeField] private StatPanelEntry winsPanel;
        [SerializeField] private StatPanelEntry eraPanel;
        [Tooltip("아직 추적하지 않는 부문(예: 탈삼진) 자리 표시용.")]
        [SerializeField] private Text pitchingPlaceholderText1;
        [SerializeField] private Text pitchingPlaceholderText2;

        [Header("User Team Highlight (2~3위 텍스트 전용)")]
        [SerializeField] private Color userTeamHighlightColor = new Color(1f, 0.84f, 0f); // 골드
        [SerializeField] private Color normalTextColor = Color.white;

        [Header("Close Button")]
        [Tooltip("[TASK-KBO-113] 리그 기록실 화면을 닫고 로비로 돌아가는 버튼. UIManager.ShowScreen()만 호출한다.")]
        [SerializeField] private Button closeButton;

        private readonly List<Text> spawnedHallOfFameEntries = new List<Text>();

        private void Awake()
        {
            if (batterTabButton != null) batterTabButton.onClick.AddListener(ShowBatterTab);
            if (pitcherTabButton != null) pitcherTabButton.onClick.AddListener(ShowPitcherTab);
            if (hallOfFameTabButton != null) hallOfFameTabButton.onClick.AddListener(ShowHallOfFameTab);
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
        }

        private void OnEnable()
        {
            ShowBatterTab(); // 화면에 들어올 때마다 기본 탭(타자 순위)으로 초기화 + 최신 기록으로 갱신
        }

        public void ShowBatterTab()
        {
            if (batterPanelRoot != null) batterPanelRoot.SetActive(true);
            if (pitcherPanelRoot != null) pitcherPanelRoot.SetActive(false);
            if (hallOfFamePanelRoot != null) hallOfFamePanelRoot.SetActive(false);
            RefreshBatterPanels();
        }

        public void ShowPitcherTab()
        {
            if (batterPanelRoot != null) batterPanelRoot.SetActive(false);
            if (pitcherPanelRoot != null) pitcherPanelRoot.SetActive(true);
            if (hallOfFamePanelRoot != null) hallOfFamePanelRoot.SetActive(false);
            RefreshPitcherPanels();
        }

        /// <summary>명예의 전당(역대 시즌 우승팀/타이틀 홀더) 탭. SeasonRollover.HallOfFame은
        /// SaveManager.LoadGame()이 복원해 두므로, 여기선 그 리스트를 읽어 화면에 그리기만 하면 된다.</summary>
        public void ShowHallOfFameTab()
        {
            if (batterPanelRoot != null) batterPanelRoot.SetActive(false);
            if (pitcherPanelRoot != null) pitcherPanelRoot.SetActive(false);
            if (hallOfFamePanelRoot != null) hallOfFamePanelRoot.SetActive(true);
            RefreshHallOfFame();
        }

        private void RefreshHallOfFame()
        {
            foreach (var entry in spawnedHallOfFameEntries)
            {
                if (entry != null) Destroy(entry.gameObject);
            }
            spawnedHallOfFameEntries.Clear();

            if (seasonRollover == null || hallOfFameEntryPrefab == null || hallOfFameListContainer == null) return;

            foreach (var record in seasonRollover.HallOfFame)
            {
                string championLabel = record.ChampionTeam != Team.None ? record.ChampionTeam.ToString() : "기록 없음";

                var text = Instantiate(hallOfFameEntryPrefab, hallOfFameListContainer);
                text.gameObject.SetActive(true);
                text.text = $"{record.SeasonYear}시즌 - 우승: {championLabel} | 타율 1위: {record.BattingAverageLeader} | " +
                            $"홈런 1위: {record.HomeRunLeader} | 다승 1위: {record.WinsLeader} | 방어율 1위: {record.EraLeader}";
                spawnedHallOfFameEntries.Add(text);
            }
        }

        private void RefreshBatterPanels()
        {
            if (battingPlaceholderText1 != null) battingPlaceholderText1.text = "타점 (준비 중)";
            if (battingPlaceholderText2 != null) battingPlaceholderText2.text = "출루율 (준비 중)";

            if (seasonStatManager == null || leagueManager == null) return;

            int teamGamesPlayed = leagueManager.PlayedGameCount;

            var avgLeaders = seasonStatManager.GetBattingAverageLeaders(teamGamesPlayed)
                .Select(l => (l.Player, FormatAverage(l.Stats.BattingAverage)))
                .ToList();
            PopulatePanel(battingAveragePanel, "타율", avgLeaders);

            var hrLeaders = seasonStatManager.GetHomeRunLeaders()
                .Select(l => (l.Player, $"{l.Stats.HomeRuns}개"))
                .ToList();
            PopulatePanel(homeRunPanel, "홈런", hrLeaders);
        }

        private void RefreshPitcherPanels()
        {
            if (pitchingPlaceholderText1 != null) pitchingPlaceholderText1.text = "탈삼진 (준비 중)";
            if (pitchingPlaceholderText2 != null) pitchingPlaceholderText2.text = "세이브 (준비 중)";

            if (seasonStatManager == null || leagueManager == null) return;

            int teamGamesPlayed = leagueManager.PlayedGameCount;

            var winLeaders = seasonStatManager.GetWinLeaders()
                .Select(l => (l.Player, $"{l.Stats.Wins}승"))
                .ToList();
            PopulatePanel(winsPanel, "다승", winLeaders);

            var eraLeaders = seasonStatManager.GetEraLeaders(teamGamesPlayed)
                .Select(l => (l.Player, FormatEra(l.Stats.EarnedRunAverage)))
                .ToList();
            PopulatePanel(eraPanel, "방어율", eraLeaders);
        }

        private void PopulatePanel(StatPanelEntry panel, string title, List<(Player Player, string ValueLabel)> ranked)
        {
            if (panel == null) return;
            if (panel.TitleText != null) panel.TitleText.text = title;

            bool hasFirstPlace = ranked.Count > 0;
            if (panel.FirstPlaceCardRoot != null) panel.FirstPlaceCardRoot.SetActive(hasFirstPlace);
            if (hasFirstPlace && panel.FirstPlaceCard != null)
            {
                panel.FirstPlaceCard.Setup(ranked[0].Player);
            }

            SetRankText(panel.SecondPlaceText, ranked, 1);
            SetRankText(panel.ThirdPlaceText, ranked, 2);
        }

        private void SetRankText(Text text, List<(Player Player, string ValueLabel)> ranked, int rankIndex)
        {
            if (text == null) return;

            if (rankIndex >= ranked.Count)
            {
                text.text = "";
                return;
            }

            var (player, valueLabel) = ranked[rankIndex];
            string playerName = player?.Template != null ? player.Template.PlayerName : "알 수 없음";

            text.text = $"{rankIndex + 1}위 {playerName} {valueLabel}";
            text.color = IsUserTeamPlayer(player) ? userTeamHighlightColor : normalTextColor;
        }

        private bool IsUserTeamPlayer(Player player) =>
            player?.Template != null && leagueManager != null && player.Template.Team == leagueManager.UserTeam;

        private static string FormatAverage(float average) => average.ToString("0.000").TrimStart('0');
        private static string FormatEra(float era) => era.ToString("0.00");
    }
}
