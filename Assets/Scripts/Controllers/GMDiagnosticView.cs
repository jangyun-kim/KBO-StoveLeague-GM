using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-GM-01] [계약·연봉·팀워크 진단] 화면. GMFeatureFlags로 선수 카드 성장(강화 · 각성 · 초월)과 세트덱이 꺼져 있으면
    /// 선수 관리(성장 센터) · 로비 [세트덱] 타일 대신 이 화면이 열린다(PlayerManagementUIController.Show). 선수 관리 허브 패널 위에
    /// 런타임으로 조립되며(GrowthCenterView와 같은 1248×1972 레퍼런스 좌표), 모든 글씨는 Normal이다.
    ///   - 계약 · 연봉: 선수단 수 · 평균 나이 · 총 연봉 / 샐러리캡 · 계약 만료(FA 대상) · 최고 연봉자
    ///   - 팀워크: TeamChemistryEngine 점수 · 분위기 · 실효 전력 계수 · 6대 부작용 경고/해법
    ///   - 치어리더 엔트리(4~6명) · 구단 풀(최대 15명) + [치어리더 관리] 바로가기
    /// </summary>
    public class GMDiagnosticView : MonoBehaviour
    {
        public const string RootName = "GMDiagnostic";
        public const int TitlePt = 28, SectionPt = 24, BodyPt = 20, BigPt = 40, ButtonPt = 20, NotePt = 17;

        private static readonly Color Bg = new Color(0.06f, 0.08f, 0.15f);
        private static readonly Color Panel = new Color(0.11f, 0.14f, 0.24f);
        private static readonly Color White = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color Muted = new Color(0.72f, 0.78f, 0.88f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);
        private static readonly Color Warn = new Color(1f, 0.62f, 0.45f);

        [SerializeField] private Font regularFont;

        private RectTransform root;
        private Text payrollText, teamworkScoreText, teamworkMetaText, messagesText, cheerText;
        private ScreenType returnScreen = ScreenType.Lobby;

        public RectTransform Root => root;
        public TeamChemistryReport LastReport { get; private set; }

        public void Configure(Font regular) => regularFont = regular;

        private void Awake()
        {
            // 카드 성장이 다시 켜지면(플래그 복구) 성장 센터를 가리지 않도록 진단 화면을 만들지 않는다.
            if (GMFeatureFlags.IsCardGrowthEnabled)
            {
                var stale = transform.Find(RootName);
                if (stale != null) stale.gameObject.SetActive(false);
                return;
            }
            if (root == null) Build();
        }

        /// <summary>진단 화면을 맨 위로 띄우고 최신 로스터로 채운다.</summary>
        public void Open(ScreenType returnTo)
        {
            // [TASK-GM-06] 플레이 중에는 텍스트 몇 줄짜리 진단 모달 대신 OOTP 27 프런트 오피스 [연봉·재계약 협상]을 연다.
            if (GMOotpFrontOfficeUIController.OpenFromDiagnostic())
            {
                if (root != null) root.gameObject.SetActive(false);
                return;
            }
            if (root == null) Build();
            if (returnTo != ScreenType.PlayerManagementHub && returnTo != ScreenType.Enhance) returnScreen = returnTo;
            root.gameObject.SetActive(true);
            transform.SetAsLastSibling();
            root.SetAsLastSibling();
            Refresh();
        }

        public void Build()
        {
            var old = transform.Find(RootName);
            if (old != null)
            {
                old.name = "_" + RootName + "_old";
                old.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            }

            var font = regularFont != null ? regularFont : TextTidy.BodyFont;
            var kit = new CompyaUiKit(font, font);
            root = CompyaUiKit.Fill(transform, RootName);
            CompyaUiKit.Paint(root, Bg, true); // 아래 성장 센터를 가리고 클릭을 막는다

            kit.GradientBox(root, "Header", 0, 0, 1248, 140, new Color(0.13f, 0.3f, 0.66f), new Color(0.07f, 0.16f, 0.4f), false);
            var back = kit.Button(root, "BackButton", "◀ 뒤로", 20, 30, 230, 112, new Color(0f, 0f, 0f, 0.25f), White, 34, bold: false);
            back.onClick.AddListener(Close);
            TextTidy.ExactButton(back, ButtonPt);
            TextTidy.Exact(kit.Label(root, "Title", "계약·연봉·팀워크 진단", 240, 20, 1008, 120, 42, TextAnchor.MiddleCenter, White), TitlePt);
            var x = kit.Button(root, "CloseButton", "X", 1100, 30, 1228, 112, new Color(0f, 0f, 0f, 0.25f), White, 34, bold: false);
            x.onClick.AddListener(Close);
            TextTidy.ExactButton(x, ButtonPt + 4);

            CompyaUiKit.Box(root, "PayrollPanel", 24, 160, 1224, 450, Panel);
            TextTidy.Exact(kit.Label(root, "PayrollTitle", "계약 · 연봉", 48, 172, 1200, 226, 30, TextAnchor.MiddleLeft, Gold), SectionPt);
            payrollText = TextTidy.Exact(kit.Label(root, "PayrollBody", "", 48, 236, 1200, 438, 24, TextAnchor.UpperLeft, White), BodyPt);
            payrollText.lineSpacing = 1.2f;

            CompyaUiKit.Box(root, "TeamworkPanel", 24, 470, 1224, 1300, Panel);
            TextTidy.Exact(kit.Label(root, "TeamworkTitle", "팀워크 · 선수단 케미스트리", 48, 482, 1200, 536, 30, TextAnchor.MiddleLeft, Gold), SectionPt);
            teamworkScoreText = TextTidy.Exact(kit.Label(root, "TeamworkScore", "", 48, 546, 400, 680, 48, TextAnchor.MiddleLeft, White), BigPt);
            teamworkMetaText = TextTidy.Exact(kit.Label(root, "TeamworkMeta", "", 420, 546, 1200, 680, 24, TextAnchor.MiddleLeft, Muted), BodyPt);
            teamworkMetaText.lineSpacing = 1.2f;
            messagesText = TextTidy.Exact(kit.Label(root, "Messages", "", 48, 700, 1200, 1288, 24, TextAnchor.UpperLeft, Warn), BodyPt);
            messagesText.lineSpacing = 1.25f;
            messagesText.verticalOverflow = VerticalWrapMode.Truncate;

            CompyaUiKit.Box(root, "CheerPanel", 24, 1320, 1224, 1500, Panel);
            TextTidy.Exact(kit.Label(root, "CheerTitle", "치어리더 엔트리", 48, 1332, 1200, 1386, 30, TextAnchor.MiddleLeft, Gold), SectionPt);
            cheerText = TextTidy.Exact(kit.Label(root, "CheerBody", "", 48, 1396, 1200, 1488, 24, TextAnchor.UpperLeft, White), BodyPt);

            var manage = kit.Button(root, "CheerManageButton", "치어리더 관리 (구단 15인 / 엔트리 4~6인)", 24, 1524, 820, 1630,
                new Color(0.86f, 0.24f, 0.45f), White, 26, bold: false);
            manage.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.CheerleaderInventory));
            TextTidy.ExactButton(manage, ButtonPt);
            var close = kit.Button(root, "CloseBottomButton", "닫기", 840, 1524, 1224, 1630, new Color(0.2f, 0.25f, 0.4f), White, 26, bold: false);
            close.onClick.AddListener(Close);
            TextTidy.ExactButton(close, ButtonPt);

            TextTidy.Exact(kit.Label(root, "Note", "단장 모드: 선수 강화 · 각성 · 초월 · 세트덱 · 선수 뽑기는 비활성화되었습니다. 선수단은 계약 · 연봉 · 팀워크로 운영합니다.",
                24, 1650, 1224, 1760, 20, TextAnchor.UpperLeft, Muted), NotePt);
        }

        public void Refresh()
        {
            if (root == null) return;
            var gm = GameManager.Instance;
            // [TASK-GM-02] 단장 모드 리그가 있으면 내 구단 28인 · 치어리더 엔트리/풀을 진단한다.
            var userTeam = gm?.GMLeague?.UserTeam;
            if (userTeam != null)
            {
                Render(userTeam.AvailableRoster, userTeam.CheerEntry.ToList(), userTeam.CheerleaderPool.Count);
                return;
            }
            var roster = gm != null ? gm.Roster.Where(p => p?.Template != null).ToList() : new List<Player>();
            var entry = gm != null ? gm.CheerSquadSlots : null;
            int pool = gm?.OwnedCheerleaders?.Count ?? 0;
            Render(roster, entry, pool);
        }

        /// <summary>로스터 · 치어리더 엔트리로 화면을 채운다(테스트에서 직접 호출).</summary>
        public void Render(IReadOnlyList<Player> roster, IReadOnlyList<Cheerleader> entry, int cheerPoolCount)
        {
            if (root == null) Build();
            roster = roster ?? new List<Player>();
            foreach (var p in roster) if (p.Salary <= 0) p.InitializeGMAttributesFromStats(); // 구 세이브 카드 - 성적 기반으로 1회 산출

            int buff = GMCheerleaderRoster.LeadershipTeamworkBonus(entry); // [TASK-GM-05] ① 단장 리더십 기반
            LastReport = TeamChemistryEngine.EvaluateRoster(roster, GMRosterLoader.DefaultPayrollCap, buff);

            payrollText.text = PayrollSummary(roster, GMRosterLoader.DefaultPayrollCap);
            teamworkScoreText.text = $"팀워크 {LastReport.TeamworkScore}";
            int penalties = CountPenalties(LastReport.ActivePenalties);
            teamworkMetaText.text = $"분위기 {TeamChemistryEngine.MoraleLabel(LastReport.MoraleState)} · 실효 전력 x{LastReport.EffectivePowerMultiplier:F2}\n" +
                                    $"발동 부작용 {penalties}개 · 응원단 리더십 +{buff}";
            messagesText.text = LastReport.DiagnosticMessages.Count > 0
                ? string.Join("\n", LastReport.DiagnosticMessages)
                : "슈퍼스타 과밀 부작용이 없습니다. 주장 · 더그아웃 리더 · 살림꾼 균형이 좋습니다.";
            int entryCount = GMCheerleaderRules.EntryCount(entry);
            cheerText.text = GMCheerleaderRules.Summary(entryCount, cheerPoolCount) +
                             (GMCheerleaderRules.IsValidEntryCount(entryCount) ? "" : " - 경기 엔트리를 4~6명으로 맞추십시오.");
        }

        public static string PayrollSummary(IReadOnlyList<Player> roster, int payrollCap)
        {
            if (roster == null || roster.Count == 0) return "등록된 선수가 없습니다.";
            int total = roster.Sum(p => p.Salary);
            int expiring = roster.Count(p => p.ContractYears == 0);
            var top = roster.OrderByDescending(p => p.Salary).First();
            return $"선수단 {roster.Count}명 · 평균 나이 {roster.Average(p => p.Age):F1}세\n" +
                   $"총 연봉 {FormatWon(total)} / 샐러리캡 {FormatWon(payrollCap)}\n" +
                   $"계약 만료(재계약 · FA 대상) {expiring}명 · 최고 연봉 {top.Template.PlayerName} {FormatWon(top.Salary)}";
        }

        /// <summary>만 원 단위 금액 → "15억 2,000만 원" / "3,000만 원".</summary>
        public static string FormatWon(long manwon)
        {
            long eok = manwon / 10000, rest = manwon % 10000;
            if (eok == 0) return $"{rest:N0}만 원";
            return rest == 0 ? $"{eok:N0}억 원" : $"{eok:N0}억 {rest:N0}만 원";
        }

        private static int CountPenalties(AllStarOverloadPenalty flags)
        {
            int n = 0;
            for (int v = (int)flags; v != 0; v &= v - 1) n++;
            return n;
        }

        private void Close()
        {
            if (root != null) root.gameObject.SetActive(false);
            UIManager.Instance?.ShowScreen(returnScreen);
        }
    }
}
