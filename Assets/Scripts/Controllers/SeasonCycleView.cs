using System.Linq;
using System.Text;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-190] 시즌 완주 루프 화면(로비 위 오버레이). 정규시즌 144경기가 끝나면 로비에 돌아올 때 자동으로 뜬다.
    ///   1단계 [정규시즌 종료]: 최종 순위표(10개 구단 승 · 무 · 패 · 승률) + 포스트시즌 진출 여부 → [포스트시즌 진행 · 시즌 결산]
    ///   2단계 [시즌 결산 · 시상식]: 최종 순위(한국시리즈 결과) · 순위 보상 · 리그 재료 보상 · 개인 타이틀 6부문(★ 우리 구단 + 수상 보너스)
    ///         · 승격 예정(아마추어 → 퓨처스 …) · 다음 시즌 AI 구단 OVR 분포 → [다음 시즌 시작]
    ///   3단계 [새 시즌 개막]: 승격된 리그 · 1/144 경기일 · AI 9개 구단 OVR 실측 → [확인]
    /// 진행 규칙은 SeasonCycle / SeasonRewardManager / SeasonRollover(단위 테스트 대상)를 그대로 호출한다. 계층은 Build() 코드로 만든다(Setup · Awake 공용).
    /// </summary>
    public class SeasonCycleView : MonoBehaviour
    {
        public const string RootName = "SeasonCycle190";

        [SerializeField] private Font boldFont;
        [SerializeField] private Font regularFont;

        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.78f);
        private static readonly Color Panel = new Color(0.07f, 0.1f, 0.2f, 0.98f);
        private static readonly Color PanelLight = new Color(0.13f, 0.18f, 0.32f);
        private static readonly Color White = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);
        private static readonly Color Cyan = new Color(0.45f, 0.86f, 1f);
        private static readonly Color Dark = new Color(0.12f, 0.08f, 0.02f);

        public enum Stage { Hidden, RegularOver, Report, NewSeason }

        private static SeasonCycleView instance;
        private CompyaUiKit kit;
        private RectTransform root;
        private Text titleText, subText, leftText, rightText, resultText;
        private Button primaryButton, closeButton;
        private Stage stage = Stage.Hidden;
        private SeasonEndReport report;

        public Stage CurrentStage => stage;
        public SeasonEndReport Report => report;
        public bool IsOpen => root != null && root.gameObject.activeSelf;

        public void Configure(Font bold, Font regular)
        {
            boldFont = bold;
            regularFont = regular;
        }

        private void Awake()
        {
            instance = this;
            Build();
        }

        private void OnEnable()
        {
            instance = this;
            if (LeagueManager.Instance != null) LeagueManager.Instance.OnSeasonFinalized += HandleSeasonFinalized;
            if (Application.isPlaying && stage != Stage.NewSeason && SeasonCycle.IsSeasonOver(LeagueManager.Instance)) Open();
        }

        private void OnDisable()
        {
            if (LeagueManager.Instance != null) LeagueManager.Instance.OnSeasonFinalized -= HandleSeasonFinalized;
        }

        private void HandleSeasonFinalized()
        {
            // 경기 화면에서 끝난 경우 로비가 꺼져 있어 OnEnable(로비 복귀)에서 뜬다. 로비가 켜져 있으면 바로 연다.
            if (isActiveAndEnabled) Open();
        }

        private static SeasonCycleView Find() => instance != null ? instance : FindAnyObjectByType<SeasonCycleView>(FindObjectsInactive.Include);

        /// <summary>정규시즌이 끝났으면 시즌 결산 화면을 연다(로비가 꺼져 있으면 다음 로비 진입 때). 시즌이 끝나지 않았으면 false.</summary>
        public static bool OpenIfSeasonOver()
        {
            if (!SeasonCycle.IsSeasonOver(LeagueManager.Instance)) return false;
            var view = Find();
            if (view == null) return false;
            if (view.isActiveAndEnabled) view.Open();
            return true;
        }

        // ================================================================== 조립

        public void Build()
        {
            var old = transform.Find(RootName);
            if (old != null)
            {
                old.name = "_" + RootName + "_old";
                old.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            }

            kit = new CompyaUiKit(boldFont, regularFont);
            root = CompyaUiKit.Fill(transform, RootName);
            CompyaUiKit.Paint(root, Dim, true); // 아래 로비 클릭 차단
            CompyaUiKit.Box(root, "Panel", 36, 110, 1212, 1860, Panel);
            kit.GradientBox(root, "Header", 36, 110, 1212, 240, new Color(0.55f, 0.18f, 0.62f), new Color(0.2f, 0.08f, 0.36f), false);
            titleText = kit.Label(root, "Title", "", 60, 116, 1188, 196, 50, TextAnchor.MiddleCenter, Gold, true);
            subText = kit.Label(root, "Sub", "", 60, 192, 1188, 238, 26, TextAnchor.MiddleCenter, White);

            CompyaUiKit.Box(root, "LeftBox", 56, 256, 606, 1500, PanelLight);
            leftText = kit.Label(root, "Left", "", 74, 268, 592, 1490, 26, TextAnchor.UpperLeft, White);
            CompyaUiKit.Box(root, "RightBox", 622, 256, 1192, 1500, PanelLight);
            rightText = kit.Label(root, "Right", "", 640, 268, 1178, 1490, 25, TextAnchor.UpperLeft, White);
            foreach (var t in new[] { leftText, rightText })
            {
                t.supportRichText = true;
                t.resizeTextForBestFit = true;
                t.resizeTextMinSize = 12;
                t.resizeTextMaxSize = t.fontSize;
            }

            resultText = kit.Label(root, "Result", "", 60, 1510, 1188, 1610, 28, TextAnchor.MiddleCenter, Cyan, true);
            resultText.resizeTextForBestFit = true;
            resultText.resizeTextMinSize = 12;
            resultText.resizeTextMaxSize = resultText.fontSize;
            primaryButton = kit.GradientButton(root, "Primary", "", 120, 1620, 1128, 1735, new Color(1f, 0.8f, 0.25f), new Color(0.93f, 0.55f, 0.1f), Dark, 44);
            primaryButton.onClick.AddListener(OnPrimary);
            closeButton = kit.Button(root, "Close", "닫기 (로비에서 다시 열림)", 120, 1750, 1128, 1838, new Color(0.25f, 0.28f, 0.38f), White, 30);
            closeButton.onClick.AddListener(Hide);

            root.gameObject.SetActive(false);
        }

        // ================================================================== 단계

        /// <summary>현재 리그 상태에 맞는 단계로 연다(결산 전 = 정규시즌 종료, 결산 후 = 결산 · 시상식).</summary>
        public void Open()
        {
            if (root == null) Build();
            var reward = SeasonRewardManager.Instance;
            if (reward != null && reward.HasGrantedThisSeason) ShowReport(reward.GrantSeasonEndRewardOnce());
            else ShowRegularOver();
            root.gameObject.SetActive(true);
            transform.SetAsLastSibling();
            root.SetAsLastSibling();
        }

        public void Hide()
        {
            if (root != null) root.gameObject.SetActive(false);
            if (stage == Stage.NewSeason) stage = Stage.Hidden;
        }

        private void ShowRegularOver()
        {
            stage = Stage.RegularOver;
            var league = LeagueManager.Instance;
            var post = PostSeasonManager.Instance;
            var tier = league != null ? league.CurrentTier : LeagueTier.Amateur;
            int rank = league?.UserFinalRank ?? 0;
            bool eligible = league != null && league.IsUserPlayoffEligible;
            titleText.text = "정규시즌 종료";
            subText.text = $"{LeagueTierTable.DisplayName(tier)} · {LeagueManager.TotalUserGames}경기 완주 · 최종 {rank}위";
            leftText.text = StandingsText(league);
            var sb = new StringBuilder();
            sb.AppendLine($"<color=#FFD045>■ 우리 구단 정규시즌 {rank}위</color>");
            sb.AppendLine(eligible
                ? $"포스트시즌 진출! (5위 이내)\n와일드카드 → 준플레이오프 → 플레이오프 → 한국시리즈"
                : "포스트시즌 진출 실패 (6위 이하)");
            sb.AppendLine();
            bool promote = SeasonCycle.ShouldPromote(league?.UserFinalRank, post != null ? post.ChampionTeam : null, league != null ? league.UserTeam : Team.None, tier);
            sb.AppendLine("<color=#FFD045>■ 리그 승격 조건</color>");
            sb.AppendLine("정규시즌 1위 또는 한국시리즈 우승 시 다음 리그로 승격");
            sb.AppendLine(promote ? $"→ 정규시즌 1위 확정! {LeagueTierTable.DisplayName(LeagueTierTable.Next(tier))} 승격 예정"
                : eligible ? "→ 한국시리즈 우승 시 승격" : "→ 이번 시즌은 리그 유지");
            sb.AppendLine();
            sb.AppendLine("<color=#FFD045>■ 시즌 결산에서 지급</color>");
            sb.AppendLine("순위 보상 · 리그 재료(포인트 · 성장 코인 · 특훈권 · 트로피 · 스킬 변경권)");
            sb.AppendLine("개인 타이틀 6부문 시상식 - 우리 구단 수상 1건당 트로피 +1 · 성장 코인 +100 · 포인트 +30,000");
            rightText.text = sb.ToString();
            resultText.text = "";
            CompyaUiKit.SetButtonText(primaryButton, eligible ? "포스트시즌 진행 · 시즌 결산" : "시즌 결산 · 시상식");
            primaryButton.interactable = true;
            closeButton.gameObject.SetActive(true);
        }

        private void ShowReport(SeasonEndReport r)
        {
            stage = Stage.Report;
            report = r;
            var league = LeagueManager.Instance;
            titleText.text = "시즌 결산 · 타이틀 시상식";
            if (r == null)
            {
                subText.text = "SeasonRewardManager가 없어 결산을 표시할 수 없습니다.";
                leftText.text = StandingsText(league);
                rightText.text = "";
            }
            else
            {
                string champ = r.Champion != Team.None ? CompyaUiKit.ShortName(r.Champion) : "-";
                subText.text = $"{LeagueTierTable.DisplayName(r.Tier)} · 최종 {r.FinalRank}위 (정규시즌 {r.RegularSeasonRank}위) · 한국시리즈 우승 {champ}";
                leftText.text = SummaryText(r);
                rightText.text = TitlesText(r);
            }
            resultText.text = r != null && r.PromotionEarned
                ? $"축하합니다! {LeagueTierTable.DisplayName(r.Tier)} → {LeagueTierTable.DisplayName(r.NextTier)} 승격!"
                : "다음 시즌도 같은 리그에서 우승(정규시즌 1위 · 한국시리즈)에 도전하십시오.";
            CompyaUiKit.SetButtonText(primaryButton, "다음 시즌 시작 ▶");
            primaryButton.interactable = league != null;
            closeButton.gameObject.SetActive(true);
        }

        private void ShowNewSeason(LeagueTier before)
        {
            stage = Stage.NewSeason;
            var league = LeagueManager.Instance;
            var tier = league != null ? league.CurrentTier : before;
            titleText.text = "새 시즌 개막!";
            subText.text = $"{LeagueTierTable.DisplayName(tier)} (권장 구단 OVR {LeagueTierTable.Get(tier).RangeLabel}) · 경기일 {(league != null ? league.PlayedGameCount + 1 : 1)}/{LeagueManager.TotalUserGames}";
            leftText.text = StandingsText(league);
            var sb = new StringBuilder();
            sb.AppendLine(tier != before
                ? $"<color=#5EE08A>■ 리그 승격: {LeagueTierTable.DisplayName(before)} → {LeagueTierTable.DisplayName(tier)}</color>"
                : $"<color=#FFD045>■ 리그 유지: {LeagueTierTable.DisplayName(tier)}</color>");
            sb.AppendLine();
            sb.AppendLine("<color=#FFD045>■ AI 9개 구단 전력(리그 권장 분포)</color>");
            if (league != null)
            {
                foreach (var info in league.GetStandings().Where(t => !t.IsUserTeam).OrderBy(t => league.GetTeamOvr(t.Team)))
                    sb.AppendLine($"{CompyaUiKit.ShortName(info.Team),-4} OVR {league.GetTeamOvr(info.Team)}");
                sb.AppendLine($"목표: {string.Join("·", LeagueTierTable.AiTeamOvrTargets(tier))}");
            }
            sb.AppendLine();
            sb.AppendLine("<color=#FFD045>■ 초기화 / 보존</color>");
            sb.AppendLine("초기화: 10개 구단 승 · 무 · 패, 경기일, 선수 시즌 기록, 포스트시즌");
            sb.AppendLine("보존: 보유 카드 · 강화/각성/스킬 성장 · 라인업 · 재화 · 치어리더");
            rightText.text = sb.ToString();
            resultText.text = "정규시즌 1경기부터 다시 시작합니다. 저장 완료.";
            CompyaUiKit.SetButtonText(primaryButton, "확인");
            primaryButton.interactable = true;
            closeButton.gameObject.SetActive(false);
        }

        private void OnPrimary()
        {
            switch (stage)
            {
                case Stage.RegularOver:
                {
                    var r = SeasonCycle.CompleteSeason(LeagueManager.Instance, PostSeasonManager.Instance, SeasonRewardManager.Instance);
                    SaveManager.Instance?.TrySaveCareer();
                    ShowReport(r);
                    break;
                }
                case Stage.Report:
                    StartNextSeason();
                    break;
                default:
                    Hide();
                    UIManager.Instance?.ShowScreen(ScreenType.Lobby);
                    break;
            }
        }

        /// <summary>[다음 시즌 시작] - SeasonRollover(아카이브 → 기록 · 포스트시즌 · 결산 초기화 → 승격 → AI 재조정 → 스케줄 1/144 → 저장).</summary>
        public void StartNextSeason()
        {
            var league = LeagueManager.Instance;
            if (league == null) return;
            var before = league.CurrentTier;
            var rollover = SeasonRollover.Instance != null ? SeasonRollover.Instance : FindAnyObjectByType<SeasonRollover>();
            if (rollover != null) rollover.RolloverToNextSeason();
            else
            {
                bool promote = SeasonCycle.ShouldPromote(league.UserFinalRank, PostSeasonManager.Instance != null ? PostSeasonManager.Instance.ChampionTeam : null, league.UserTeam, league.CurrentTier);
                SeasonStatManager.Instance?.ResetSeason();
                PostSeasonManager.Instance?.ResetForNextSeason();
                SeasonRewardManager.Instance?.ResetForNextSeason();
                league.AdvanceToNextSeason(promote);
                SaveManager.Instance?.TrySaveCareer();
            }
            report = null;
            ShowNewSeason(before);
        }

        // ================================================================== 문구

        public static string StandingsText(LeagueManager league)
        {
            if (league == null) return "리그 정보가 없습니다.";
            var sb = new StringBuilder();
            sb.AppendLine("<color=#FFD045>순위  구단    승   무   패   승률</color>");
            int i = 0;
            foreach (var t in league.GetStandings())
            {
                i++;
                string line = $"{i,2}위  {CompyaUiKit.ShortName(t.Team),-4}  {t.Wins,3}  {t.Draws,3}  {t.Losses,3}  {t.WinRate:.000}";
                sb.AppendLine(t.IsUserTeam ? $"<color=#5ED6F2>{line}  ◀</color>" : line);
            }
            return sb.ToString();
        }

        public static string SummaryText(SeasonEndReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine(r.FinalRank <= 1 ? "<color=#FFD045>🏆 한국시리즈 우승!</color>" : r.FinalRank == 2 ? "<color=#FFD045>준우승</color>" : $"<color=#FFD045>최종 {r.FinalRank}위</color>");
            sb.AppendLine();
            sb.AppendLine("<color=#FFD045>■ 순위 보상</color>");
            if (r.LiveNormalTicketGained > 0) sb.AppendLine($"라이브 일반 영입권 +{r.LiveNormalTicketGained:N0}");
            if (r.GameGoldGained > 0) sb.AppendLine($"게임 머니 +{r.GameGoldGained:N0}");
            if (!string.IsNullOrEmpty(r.GuaranteedPackGrade)) sb.AppendLine($"확정팩: {r.GuaranteedPackGrade} 이상 1장");
            sb.AppendLine();
            sb.AppendLine("<color=#FFD045>■ 리그 재료 보상</color>");
            sb.AppendLine(r.MaterialReward.Summary());
            sb.AppendLine();
            sb.AppendLine("<color=#FFD045>■ 타이틀 수상 보너스</color>");
            sb.AppendLine(r.TitleBonus.IsEmpty ? "우리 구단 수상 없음" : r.TitleBonus.Summary());
            sb.AppendLine();
            sb.AppendLine("<color=#FFD045>■ 다음 시즌</color>");
            sb.AppendLine(r.PromotionEarned
                ? $"<color=#5EE08A>승격! {LeagueTierTable.DisplayName(r.Tier)} → {LeagueTierTable.DisplayName(r.NextTier)}</color>"
                : $"{LeagueTierTable.DisplayName(r.Tier)} 유지");
            sb.AppendLine($"AI 구단 OVR {string.Join("·", LeagueTierTable.AiTeamOvrTargets(r.NextTier))}");
            return sb.ToString();
        }

        public static string TitlesText(SeasonEndReport r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<color=#FFD045>■ 개인 타이틀 시상식</color>");
            sb.AppendLine();
            foreach (var category in SeasonAwardRules.Categories)
            {
                var title = r.Titles?.FirstOrDefault(t => t.Category == category);
                if (title == null) { sb.AppendLine($"{category}  -  수상자 없음\n"); continue; }
                string team = title.Player?.Template != null ? CompyaUiKit.ShortName(title.Player.Template.Team) : title.TeamLabel;
                sb.AppendLine(title.IsUserPlayer ? $"<color=#5EE08A>★ {category}  {title.PlayerName} ({team})</color>" : $"{category}  {title.PlayerName} ({team})");
                sb.AppendLine($"    {title.ValueLabel}" + (title.IsUserPlayer ? "  <color=#5EE08A>+트로피 1 · 코인 100 · 30,000P</color>" : ""));
            }
            return sb.ToString();
        }
    }
}
