using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Simulation;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>[TASK-GM-04] 시상식 화면 탭 4종.</summary>
    public enum GMAwardsTab
    {
        KboCeremony = 0,  // 11월 KBO 시상식(MVP · 신인 · 타이틀 14 · 수비 10 · 특별 2)
        GoldenGlove = 1,  // 12월 골든글러브(10부문)
        AllStarMonthly = 2, // 7월 올스타전 & 월간 시상
        Postseason = 3,   // 포스트시즌 결과
    }

    /// <summary>
    /// [TASK-GM-04] 시상식 & 포스트시즌 리포트(기획서 1.1~1.7, 1080×1920 Portrait, 1248×1972 레퍼런스 좌표, 모든 글씨 Normal · 15pt 이상).
    ///   - 탭: [11월 KBO 시상식] [12월 골든글러브] [7월 올스타 · 월간] [포스트시즌 결과]
    ///   - 본문: 2열 × 14행 수상 카드(부문 · 수상자 · 구단 · 기록), 내 구단 수상은 ★ · 구단색 강조
    ///   - 정규시즌 종료 후 열면 포스트시즌 → 11월 KBO 시상식을 순서대로 개최, 골든글러브는 12월 탭의 [골든글러브 시상식 개최]로 단독 개최
    ///   - 하단: [20yy 시즌 전환](남은 단계를 마저 치르고 연도 전환) · [대시보드로 돌아가기]
    /// 대시보드 [시상 리포트] · [포스트시즌 & 시상식 보기] · 올스타 팝업이 연다.
    /// </summary>
    public class GMAwardsCeremonyUIController : MonoBehaviour
    {
        public const string RootName = "AwardsRoot";
        public const int TitlePt = 26, TabPt = 20, SummaryPt = 19, CellTitlePt = 17, CellBodyPt = 17, StatusPt = 17, ButtonPt = 21;
        public const int Rows = 14, Cols = 2, CellCount = Rows * Cols;

        private static readonly Color Bg = new Color(0.06f, 0.08f, 0.15f);
        private static readonly Color White = new Color(0.96f, 0.97f, 0.99f);
        private static readonly Color Muted = new Color(0.74f, 0.8f, 0.9f);
        private static readonly Color Gold = new Color(1f, 0.84f, 0.3f);
        private static readonly Color CellIdle = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color ButtonIdle = new Color(1f, 1f, 1f, 0.12f);
        private static readonly Color ButtonOn = new Color(0.15f, 0.45f, 0.85f);
        private static readonly string[] TabLabels = { "11월 KBO 시상식", "12월 골든글러브", "7월 올스타 · 월간", "포스트시즌 결과" };

        [SerializeField] private Font regularFont;

        private RectTransform root;
        private Text title, summary, status;
        private readonly Button[] tabs = new Button[4];
        private readonly Image[] cellBgs = new Image[CellCount];
        private readonly Text[] cellTitles = new Text[CellCount];
        private readonly Text[] cellBodies = new Text[CellCount];
        private Button holdGoldenGlove, nextSeason, back, close;

        private GMLiveSeasonSimulator simulator;
        private GMAwardsTab tab = GMAwardsTab.AllStarMonthly;

        public event Action OnClosed;
        /// <summary>연도 전환 후 새 진행기(대시보드가 받아서 다시 연결한다).</summary>
        public event Action<GMLiveSeasonSimulator> OnSeasonAdvanced;

        public RectTransform Root => root;
        public GMAwardsTab CurrentTab => tab;
        public GMLiveSeasonSimulator Simulator => simulator;

        public void Configure(Font regular) => regularFont = regular;

        private void Awake()
        {
            if (root == null) Build();
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
            var font = regularFont != null ? regularFont : TextTidy.BodyFont;
            var kit = new CompyaUiKit(font, font);
            root = CompyaUiKit.Fill(transform, RootName);
            CompyaUiKit.Paint(root, Bg, true);

            title = L(kit, "Title", "KBO 시상식 & 포스트시즌", 24, 16, 1090, 84, TitlePt, TextAnchor.MiddleLeft, Gold);
            close = Btn(kit, "CloseButton", "X", 1110, 16, 1228, 84, ButtonIdle, ButtonPt + 2);
            close.onClick.AddListener(Close);

            for (int i = 0; i < tabs.Length; i++)
            {
                var t = (GMAwardsTab)i;
                float x0 = 20 + i * 305;
                tabs[i] = Btn(kit, $"Tab_{t}", TabLabels[i], x0, 96, x0 + 293, 166, ButtonIdle, TabPt);
                tabs[i].onClick.AddListener(() => SelectTab(t));
            }
            summary = L(kit, "Summary", "", 24, 176, 1224, 238, SummaryPt, TextAnchor.MiddleLeft, White);

            for (int i = 0; i < CellCount; i++)
            {
                int r = i / Cols, c = i % Cols;
                float x0 = c == 0 ? 20 : 630, x1 = c == 0 ? 618 : 1228;
                float y0 = 246 + r * 108, y1 = y0 + 100;
                cellBgs[i] = CompyaUiKit.Box(root, $"CellBg{i}", x0, y0, x1, y1, CellIdle);
                cellTitles[i] = L(kit, $"CellTitle{i}", "", x0 + 14, y0 + 4, x1 - 10, y0 + 42, CellTitlePt, TextAnchor.MiddleLeft, Gold);
                cellBodies[i] = L(kit, $"CellBody{i}", "", x0 + 14, y0 + 44, x1 - 10, y1 - 4, CellBodyPt, TextAnchor.UpperLeft, White);
                cellBodies[i].lineSpacing = 1.05f;
            }

            holdGoldenGlove = Btn(kit, "HoldGoldenGlove", "12월 골든글러브 시상식 개최", 274, 820, 974, 930, ButtonOn, ButtonPt + 2);
            holdGoldenGlove.onClick.AddListener(HoldGoldenGlove);
            status = L(kit, "Status", "", 24, 1760, 1224, 1808, StatusPt, TextAnchor.MiddleLeft, Muted);
            nextSeason = Btn(kit, "NextSeasonButton", "다음 시즌 전환", 20, 1824, 610, 1940, ButtonOn, ButtonPt + 1);
            back = Btn(kit, "BackButton", "대시보드로 돌아가기", 638, 1824, 1228, 1940, new Color(0.3f, 0.34f, 0.46f), ButtonPt + 1);
            nextSeason.onClick.AddListener(() => AdvanceSeason());
            back.onClick.AddListener(Close);
        }

        private Text L(CompyaUiKit kit, string name, string text, float x0, float y0, float x1, float y1, int pt, TextAnchor anchor, Color color)
        {
            var t = kit.Label(root, name, text, x0, y0, x1, y1, pt / 0.9f, anchor, color);
            return TextTidy.Exact(t, pt);
        }

        private Button Btn(CompyaUiKit kit, string name, string text, float x0, float y0, float x1, float y1, Color bg, int pt)
        {
            var b = kit.Button(root, name, text, x0, y0, x1, y1, bg, White, pt / 0.9f, bold: false);
            TextTidy.ExactButton(b, pt);
            return b;
        }

        // ================================================================== 진입 · 조작

        /// <summary>
        /// 화면을 연다. 정규시즌이 끝났으면 포스트시즌 → 11월 KBO 시상식을 (아직이면) 개최하고 KBO 시상식 탭을, 시즌 중이면 올스타 · 월간 탭을 기본으로 보여 준다.
        /// </summary>
        public void Open(GMLiveSeasonSimulator sim, GMAwardsTab? initialTab = null)
        {
            if (root == null) Build();
            simulator = sim;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            bool complete = sim != null && sim.IsSeasonComplete;
            if (complete)
            {
                GMAwardEvaluator.RunPostseason(sim);
                GMAwardEvaluator.HoldKboAwardsCeremony(sim);
            }
            SelectTab(initialTab ?? (complete ? GMAwardsTab.KboCeremony : GMAwardsTab.AllStarMonthly));
        }

        public void Close()
        {
            gameObject.SetActive(false);
            OnClosed?.Invoke();
        }

        public void SelectTab(GMAwardsTab t)
        {
            tab = t;
            Refresh();
        }

        /// <summary>12월 골든글러브 단독 개최(11월 시상식 · 포스트시즌이 안 끝났으면 먼저 치른다). 정규시즌 중이면 아무것도 하지 않는다.</summary>
        public void HoldGoldenGlove()
        {
            if (simulator == null || !simulator.IsSeasonComplete) return;
            GMAwardEvaluator.HoldGoldenGloveCeremony(simulator);
            SelectTab(GMAwardsTab.GoldenGlove);
        }

        /// <summary>[20yy 시즌 전환] - 남은 단계를 마저 치르고 다음 해로 넘긴다. 새 진행기를 돌려준다(실패하면 null).</summary>
        public GMLiveSeasonSimulator AdvanceSeason()
        {
            if (simulator == null || !simulator.IsSeasonComplete) return null;
            var league = simulator.League;
            if (!GMAwardEvaluator.AdvanceToNextSeasonYear(simulator)) return null;
            GMLiveSeasonSimulator next;
            var gm = GameManager.Instance;
            if (gm != null && gm.GMLeague == league)
            {
                gm.RestoreGMLeague(league);
                next = gm.GMSimulator;
            }
            else next = new GMLiveSeasonSimulator(league);
            simulator = next;
            OnSeasonAdvanced?.Invoke(next);
            Close();
            return next;
        }

        // ================================================================== 갱신

        public void Refresh()
        {
            if (root == null) return;
            var league = simulator?.League;
            var bundle = league?.Awards ?? new SeasonAwardCeremonyBundle();
            int year = league?.SeasonYear ?? GMFeatureFlags.DEFAULT_START_YEAR;
            bool complete = simulator != null && simulator.IsSeasonComplete;
            title.text = $"{year} KBO 시상식 & 포스트시즌";
            for (int i = 0; i < tabs.Length; i++) tabs[i].targetGraphic.color = i == (int)tab ? ButtonOn : ButtonIdle;
            CompyaUiKit.SetButtonText(nextSeason, $"{year + 1} 시즌 전환");
            nextSeason.interactable = complete;
            holdGoldenGlove.gameObject.SetActive(false);

            var items = new List<(string title, string body, string team)>();
            switch (tab)
            {
                case GMAwardsTab.KboCeremony:
                    if (bundle.KboCeremonyHeld)
                    {
                        var mvp = bundle.Find("MVP");
                        summary.text = $"{year} KBO 시상식(11월) - MVP {mvp?.PlayerName ?? "-"} · 신인상 {bundle.Find("ROOKIE")?.PlayerName ?? "-"} · 타이틀 14 · 수비상 10 · 특별상 2";
                        items.AddRange(bundle.KboCeremony.Select(Item));
                    }
                    else summary.text = complete ? "포스트시즌을 마치면 11월 KBO 시상식이 열립니다." : $"정규시즌 종료 후 개최됩니다. (현재 {simulator?.GamesPlayed ?? 0} / 144경기)";
                    break;
                case GMAwardsTab.GoldenGlove:
                    if (bundle.GoldenGloveHeld)
                    {
                        summary.text = $"{year} KBO 골든글러브(12월 단독 개최) - 공 · 수 · 주 종합 10인";
                        items.AddRange(bundle.GoldenGlove.Select(Item));
                    }
                    else
                    {
                        summary.text = complete ? "11월 KBO 시상식이 끝났습니다. 12월 골든글러브 시상식을 개최하십시오." : "정규시즌 · 포스트시즌 · 11월 시상식 이후 12월에 단독 개최됩니다.";
                        holdGoldenGlove.gameObject.SetActive(complete);
                    }
                    break;
                case GMAwardsTab.AllStarMonthly:
                    var star = bundle.AllStar;
                    summary.text = star != null && star.Played ? star.Summary : "올스타전은 전반기(72경기) 종료 직후 7월에 열립니다. 월간 시상은 24경기마다 발표됩니다.";
                    if (star != null && star.Played) items.AddRange(star.Awards.Select(Item));
                    foreach (var m in bundle.Monthly.OrderBy(m => m.MonthIndex))
                    {
                        items.Add(($"1.7.1 {m.MonthLabel} 월간 MVP", Body(m.Mvp), m.Mvp.TeamCode));
                        items.Add(($"1.7.2 {m.MonthLabel} 캡스플레이상", Body(m.CapsPlay), m.CapsPlay.TeamCode));
                    }
                    break;
                default:
                    var ps = bundle.Postseason;
                    if (ps != null && ps.Completed)
                    {
                        summary.text = $"{year} 포스트시즌 - 우승 {NameAliasTable.DisplayTeamName(ps.ChampionCode)} · 준우승 {NameAliasTable.DisplayTeamName(ps.RunnerUpCode)}";
                        foreach (var s in ps.Series)
                            items.Add(($"{s.Round}", $"{Short(s.HigherCode)} {s.HigherWins}승 - {s.LowerWins}승 {Short(s.LowerCode)} → {Short(s.WinnerCode)} 진출\n{string.Join(" · ", s.Games.Where(g => !g.Contains("재경기")).Select(g => g.Substring(g.IndexOf(' ') + 1)).Take(3))}", s.WinnerCode));
                        items.Add(("한국시리즈 우승", NameAliasTable.DisplayTeamName(ps.ChampionCode), ps.ChampionCode));
                        items.Add(("한국시리즈 MVP", string.IsNullOrEmpty(ps.KoreanSeriesMvp) ? "-" : ps.KoreanSeriesMvp, ps.ChampionCode));
                        for (int i = 0; i < ps.FinalRankCodes.Count; i++)
                            items.Add(($"최종 {i + 1}위", NameAliasTable.DisplayTeamName(ps.FinalRankCodes[i]) + (i < 5 ? $" (정규시즌 {ps.SeedCodes.IndexOf(ps.FinalRankCodes[i]) + 1}위)" : ""), ps.FinalRankCodes[i]));
                    }
                    else summary.text = complete ? "포스트시즌을 진행합니다." : "정규시즌 144경기 종료 후 상위 5개 구단이 포스트시즌을 치릅니다.";
                    break;
            }

            string me = league?.SelectedTeamCode;
            var theme = league?.UserTeam?.Team ?? Team.Samsung;
            for (int i = 0; i < CellCount; i++)
            {
                bool used = i < items.Count;
                cellBgs[i].gameObject.SetActive(used);
                cellTitles[i].gameObject.SetActive(used);
                cellBodies[i].gameObject.SetActive(used);
                if (!used) continue;
                bool mine = me != null && items[i].team == me;
                cellTitles[i].text = items[i].title;
                cellBodies[i].text = (mine ? "★ " : "") + items[i].body;
                var primary = TeamThemePalette.Primary(theme);
                cellBgs[i].color = mine ? new Color(primary.r, primary.g, primary.b, 0.6f) : CellIdle;
            }

            int mineCount = bundle.All().Count(a => a != null && a.HasWinner && a.TeamCode == me && !string.IsNullOrEmpty(a.PlayerId));
            status.text = bundle.DynamicsApplied
                ? $"우리 구단 수상 {mineCount}건 · 단장 과제: 수상자 {bundle.Dynamics.Count}명 Ego +1 · 연봉 {GMAwardEvaluator.MinRaisePercent}~{GMAwardEvaluator.MaxRaisePercent}% 인상 반영"
                : $"우리 구단 수상 {mineCount}건 · 골든글러브까지 끝나면 MVP · 골든글러브 · 타이틀 수상자의 Ego와 연봉이 오릅니다.";
        }

        private static (string, string, string) Item(GMAwardWinner a) => ($"{a.Section} {a.AwardName}", Body(a), a.TeamCode);

        private static string Body(GMAwardWinner a)
        {
            if (a == null || !a.HasWinner) return "-";
            string team = string.IsNullOrEmpty(a.TeamCode) ? "" : Short(a.TeamCode);
            string who = string.IsNullOrEmpty(a.PlayerId) ? a.PlayerName : $"{a.PlayerName} ({team} · {a.Position})";
            return string.IsNullOrEmpty(a.ValueLabel) ? who : $"{who}\n{a.ValueLabel}";
        }

        private static string Short(string code) => string.IsNullOrEmpty(code) ? "-" : CompyaUiKit.ShortName(NameAliasTable.ToTeam(code));
    }
}
