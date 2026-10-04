using System.Linq;
using KBOManager.Controllers;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-181] 9:16 네이티브 스토브리그 로비(Layout181)의 표시 갱신. TASK-180의 캡처 배경(메인 홈.jpg - 휴대폰 시계/선수 캡처)과
    /// 거대 구단 로고 오버레이, 기획에 없는 더미 버튼(PASS/이벤트/출석/커뮤니티/후원사)을 걷어내고 5단으로 다시 짰다.
    ///   1단 헤더: 구단 로고(preserveAspect) · [구단명] [닉네임] 단장 · 팀 OVR · 세트덱 NP · [구단/단장 변경](구단 테마 컬러).
    ///   2단 재화: 포인트(볼) · 싸인볼 · 트로피 · 픽업권/티켓.
    ///   3단 대시보드: NEXT MATCH(내 구단 vs 상대 로고 · 시즌 진행도 · 플레이 볼) / 대표 스타 카드 / KBO 10구단 순위표(순위·구단·승패·승률).
    ///   4단 메뉴 타일, 5단 하단 5탭은 LeagueDashboardUIController 버튼 바인딩(화면 전환)과 LobbyButtonRelay가 담당한다.
    /// 시즌 진행도/다음 경기 문구/플레이 볼은 LeagueDashboardUIController가 그대로 갱신한다(이 컴포넌트는 그 외 표시만).
    /// </summary>
    public class LobbyHome181 : MonoBehaviour
    {
        [Header("1단 헤더")]
        [SerializeField] private Image headerBackground;
        [SerializeField] private Image teamLogo;
        [SerializeField] private Text teamNameText;
        [SerializeField] private Text managerText;
        [SerializeField] private Text headerStatsText;
        [SerializeField] private Button changeManagerButton;

        [Header("2단 재화")]
        [SerializeField] private Text pointText;
        [SerializeField] private Text signBallText;
        [SerializeField] private Text trophyText;
        [SerializeField] private Text ticketText;

        [Header("3단 NEXT MATCH")]
        [SerializeField] private Image myTeamLogo;
        [SerializeField] private Image opponentLogo;
        [SerializeField] private Text myTeamText;
        [SerializeField] private Text opponentText;
        [SerializeField] private Text venueText;

        [Header("3단 대표 스타")]
        [SerializeField] private PlayerCardUI cardPrefab;
        [SerializeField] private RectTransform starCardHolder;
        [SerializeField] private Text starNameText;
        [SerializeField] private Text starDetailText;
        [SerializeField] private Text starStatsText;

        [Header("3단 순위표 (10행 x 순위/구단/승패/승률)")]
        [SerializeField] private Image[] standingRowBackgrounds = new Image[10];
        [SerializeField] private Text[] standingRankTexts = new Text[10];
        [SerializeField] private Text[] standingTeamTexts = new Text[10];
        [SerializeField] private Text[] standingRecordTexts = new Text[10];
        [SerializeField] private Text[] standingRateTexts = new Text[10];

        private PlayerCardUI starCard;

        private void Awake()
        {
            if (changeManagerButton != null) changeManagerButton.onClick.AddListener(OpenTitle);
        }

        private void OnEnable() => Refresh();

        /// <summary>[구단/단장 변경] - 현재 커리어를 저장하고 타이틀([시즌 이어하기] / [새 단장 부임])로 돌아간다.</summary>
        private void OpenTitle()
        {
            SaveManager.Instance?.TrySaveCareer();
            UIManager.Instance?.ShowScreen(ScreenType.Onboarding);
        }

        public void Refresh()
        {
            var gm = GameManager.Instance;
            var league = LeagueManager.Instance;
            var team = gm != null && gm.FavoriteTeam != Team.None ? gm.FavoriteTeam : league != null ? league.UserTeam : Team.None;
            var theme = CompyaUiKit.TeamColor(team);

            // 1단
            if (headerBackground != null) headerBackground.color = CompyaUiKit.Darken(theme, 0.85f);
            TeamLogoSprites.Apply(teamLogo, team);
            if (teamNameText != null) teamNameText.text = team == Team.None ? "구단 미선택" : CompyaUiKit.FullName(team);
            if (managerText != null) managerText.text = OnboardingUIController.ManagerLabel(gm != null ? gm.ManagerNickname : null);
            if (gm != null && headerStatsText != null)
            {
                var setDeck = GameManager.EvaluateSetDeck(gm.Roster.ToList(), team != Team.None ? team.ToString() : null, gm.SetDeckSelection);
                headerStatsText.text = $"팀 OVR <b>{gm.CalculateTeamOVR()}</b>   ·   세트덱 <b><color=#5FE3FF>{setDeck.Score}P</color></b> / {SetDeckBuffTable.FinalGoalScore}P";
            }
            if (changeManagerButton != null && changeManagerButton.targetGraphic != null)
                changeManagerButton.targetGraphic.color = CompyaUiKit.Darken(theme, 0.6f);

            // 2단
            if (gm != null)
            {
                if (pointText != null) pointText.text = $"{gm.GameGold:N0}";
                if (signBallText != null) signBallText.text = $"{gm.SignatureBall:N0}";
                if (trophyText != null) trophyText.text = $"{gm.Trophy:N0}";
                if (ticketText != null) ticketText.text = $"{gm.PickupTicket:N0} / {gm.Ticket:N0}";
            }

            RefreshMatchup(league, team);
            RefreshStar(gm);
            RefreshStandings(league, team);
        }

        private void RefreshMatchup(LeagueManager league, Team team)
        {
            var fixture = league?.PeekNextFixture();
            var opponent = fixture == null ? Team.None : fixture.HomeTeam == league.UserTeam ? fixture.AwayTeam : fixture.HomeTeam;
            TeamLogoSprites.Apply(myTeamLogo, team);
            TeamLogoSprites.Apply(opponentLogo, opponent);
            if (myTeamText != null) myTeamText.text = CompyaUiKit.ShortName(team);
            if (opponentText != null) opponentText.text = opponent == Team.None ? "-" : CompyaUiKit.ShortName(opponent);
            if (venueText != null)
            {
                venueText.text = fixture == null
                    ? "예정된 경기가 없습니다"
                    : $"{fixture.GameNumber}번째 경기 · {(fixture.HomeTeam == league.UserTeam ? "홈" : "원정")} · {CompyaUiKit.Stadium(fixture.HomeTeam)}\n" +
                      KBOManager.Controllers.CompyaMatchView.ProbableStartersLine(league, fixture); // [TASK-KBO-187] 예고 선발
            }
        }

        private void RefreshStar(GameManager gm)
        {
            var hero = gm?.Roster?.Where(p => p?.Template != null).OrderByDescending(p => p.CalculateOVR(false)).FirstOrDefault();

            if (starCardHolder != null && cardPrefab != null)
            {
                if (starCard == null)
                {
                    if (!starCardHolder.TryGetComponent<CardHolderFit>(out var fit)) fit = starCardHolder.gameObject.AddComponent<CardHolderFit>();
                    fit.Configure(CardHolderFit.NativeSizeOf(cardPrefab));
                    starCard = Instantiate(cardPrefab);
                    foreach (var graphic in starCard.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
                    fit.Place((RectTransform)starCard.transform);
                }
                starCard.gameObject.SetActive(hero != null);
                if (hero != null) starCard.Setup(hero);
            }

            if (starNameText != null) starNameText.text = hero != null ? CompyaMatchView.DisplayName(hero) : "대표 선수 없음";
            if (starDetailText != null)
            {
                starDetailText.text = hero != null
                    ? $"{CompyaUiKit.ShortName(hero.Template.Team)} · {CompyaMatchView.PositionLabel(hero)} · {hero.Template.Grade}\nOVR <size=150%><b>{hero.CalculateOVR(false)}</b></size>  SD {hero.SetDeckScore}"
                    : "[라인업]에서 선수단을 편성하십시오";
            }
            if (starStatsText != null)
            {
                if (hero == null) starStatsText.text = "";
                else if (hero.Template.IsPitcher)
                {
                    var s = hero.Template.PitcherStats;
                    starStatsText.text = $"구위 {s.Stuff}   구속 {s.Velocity}\n변화 {s.Movement}   제구 {s.Control}";
                }
                else
                {
                    var s = hero.Template.BatterStats;
                    starStatsText.text = $"파워 {s.Power}   정확 {s.Contact}\n선구 {s.Discipline}   주력 {s.Speed}";
                }
            }
        }

        private void RefreshStandings(LeagueManager league, Team team)
        {
            var standings = league != null ? league.GetStandings() : null;
            for (int i = 0; i < standingTeamTexts.Length; i++)
            {
                var row = standings != null && i < standings.Count ? standings[i] : null;
                bool mine = row != null && row.Team == team && team != Team.None;
                var color = mine ? new Color(1f, 0.84f, 0.29f) : new Color(0.92f, 0.94f, 0.98f);

                SetCell(standingRankTexts, i, row != null ? $"{i + 1}" : "", color);
                SetCell(standingTeamTexts, i, row != null ? CompyaUiKit.ShortName(row.Team) : "", color);
                SetCell(standingRecordTexts, i, row != null ? $"{row.Wins}-{row.Draws}-{row.Losses}" : "", color);
                SetCell(standingRateTexts, i, row != null ? row.WinRate.ToString("0.000") : "", color);
                if (standingRowBackgrounds != null && i < standingRowBackgrounds.Length && standingRowBackgrounds[i] != null)
                {
                    standingRowBackgrounds[i].color = mine
                        ? new Color(CompyaUiKit.TeamColor(team).r, CompyaUiKit.TeamColor(team).g, CompyaUiKit.TeamColor(team).b, 0.55f)
                        : i % 2 == 0 ? new Color(1f, 1f, 1f, 0.05f) : new Color(1f, 1f, 1f, 0.1f);
                }
            }
        }

        private static void SetCell(Text[] column, int index, string value, Color color)
        {
            if (column == null || index >= column.Length || column[index] == null) return;
            column[index].text = value;
            column[index].color = color;
            column[index].fontStyle = color.b < 0.5f ? FontStyle.Bold : FontStyle.Normal;
        }
    }
}
