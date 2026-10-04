using System.Collections.Generic;
using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-193] 로비 4번째 메뉴 타일 [리그 기록실](시즌 기록 · 명예의 전당) 화면 - LeagueStats 화면 루트 위를 덮는 코드 빌드 화면.
    ///   [타자 부문 순위 (TOP 10)] 타율 · 홈런 · 타점 · 안타 / [투수 부문 순위 (TOP 10)] 다승 · 평균자책점 · 탈삼진 · 이닝(내 구단 선수 하이라이트)
    ///   [명예의 전당] 시즌 완주마다 누적된 시즌 번호 · 리그 · 최종 순위/전적 · 한국시리즈 우승 · 시즌 MVP · 타이틀(SeasonRollover.HallOfFame, 세이브 v13).
    /// 규칙은 LeagueRecordsRules(단위 테스트 대상). 글씨는 16~26pt Normal(Bold 없음).
    /// </summary>
    public class LeagueRecordsView : MonoBehaviour
    {
        public const string RootName = "LeagueRecords193";
        public enum Tab { Batters = 0, Pitchers = 1, HallOfFame = 2 }
        public const int RowCount = LeagueRecordsRules.TopCount;

        [SerializeField] private Font boldFont;
        [SerializeField] private Font regularFont;

        private static readonly Color Bg = new Color(0.05f, 0.07f, 0.13f);
        private static readonly Color RowCard = new Color32(0x16, 0x20, 0x32, 0xFF);
        private static readonly Color UserRow = new Color32(0x1D, 0x3F, 0x8A, 0xFF);
        private static readonly Color White = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color Muted = new Color(0.79f, 0.84f, 0.92f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);
        private static readonly Color Slate = new Color(0.2f, 0.25f, 0.36f);
        private static readonly Color CategoryOn = new Color(0.98f, 0.76f, 0.2f);
        private static readonly Color Dark = new Color(0.12f, 0.08f, 0.02f);

        private CompyaUiKit kit;
        private RectTransform root, tableGroup, hallGroup, hallContent;
        private Text subText, headerValueText, emptyText;
        private readonly Button[] tabButtons = new Button[3];
        private readonly Button[] categoryButtons = new Button[4];
        private readonly Image[] rowBoxes = new Image[RowCount];
        private readonly Text[] rowRank = new Text[RowCount], rowName = new Text[RowCount], rowTeam = new Text[RowCount], rowValue = new Text[RowCount];
        private Tab tab = Tab.Batters;
        private int category;

        public Tab CurrentTab => tab;
        public int Category => category;

        public void Configure(Font bold, Font regular)
        {
            boldFont = bold;
            regularFont = regular;
        }

        private void Awake() => Build();

        private void OnEnable()
        {
            if (root == null) return;
            root.SetAsLastSibling();
            Refresh();
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
            CompyaUiKit.Paint(root, Bg, true); // 예전 기록실(TOP 3 카드) UI를 덮는다

            kit.GradientBox(root, "Header", 0, 0, 1248, 150, new Color(0.4f, 0.24f, 0.16f), new Color(0.16f, 0.09f, 0.05f), false);
            TextFit193.Fit(kit.Label(root, "Title", "리그 기록실", 34, 12, 700, 84, 30, TextAnchor.MiddleLeft, Gold), 26, 18);
            subText = TextFit193.Fit(kit.Label(root, "Sub", "", 34, 84, 900, 144, 20, TextAnchor.MiddleLeft, White), 17);
            var stove = kit.Button(root, "StoveButton", "스토브리그 ▸", 720, 20, 960, 84, new Color(0.15f, 0.39f, 0.92f), White, 22);
            stove.onClick.AddListener(() => StoveLeagueView.OpenStove());
            TextFit193.FitButton(stove, 17);
            var close = kit.Button(root, "Close", "닫기", 972, 20, 1228, 84, Slate, White, 22);
            close.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
            TextFit193.FitButton(close, 18);

            var names = new[] { "타자 부문 순위 (TOP 10)", "투수 부문 순위 (TOP 10)", "명예의 전당 (역대 시즌)" };
            for (int i = 0; i < tabButtons.Length; i++)
            {
                var t = (Tab)i;
                float x0 = 20 + i * 404f;
                tabButtons[i] = kit.Button(root, $"Tab_{t}", names[i], x0, 162, x0 + 398, 240, ScoutHubUIController.InactiveTabColor, White, 24);
                tabButtons[i].onClick.AddListener(() => SelectTab(t));
                TextFit193.FitButton(tabButtons[i], i == 0 ? ScoutHubUIController.ActiveTabPt : ScoutHubUIController.InactiveTabPt, 14);
            }

            // ---- 순위표(타자 · 투수 공용)
            tableGroup = CompyaUiKit.Fill(root, "Table");
            for (int i = 0; i < categoryButtons.Length; i++)
            {
                int c = i;
                float x0 = 20 + i * 304f;
                categoryButtons[i] = kit.Button(tableGroup, $"Category{i}", "", x0, 252, x0 + 298, 318, Slate, White, 22);
                categoryButtons[i].onClick.AddListener(() => SelectCategory(c));
                TextFit193.FitButton(categoryButtons[i], 18);
            }
            CompyaUiKit.Box(tableGroup, "TableHeader", 20, 330, 1228, 384, new Color(0.12f, 0.15f, 0.24f));
            TextFit193.Fit(kit.Label(tableGroup, "HeadRank", "순위", 30, 330, 150, 384, 20, TextAnchor.MiddleCenter, Muted), 17);
            TextFit193.Fit(kit.Label(tableGroup, "HeadName", "선수", 170, 330, 640, 384, 20, TextAnchor.MiddleLeft, Muted), 17);
            TextFit193.Fit(kit.Label(tableGroup, "HeadTeam", "구단", 650, 330, 880, 384, 20, TextAnchor.MiddleCenter, Muted), 17);
            headerValueText = TextFit193.Fit(kit.Label(tableGroup, "HeadValue", "기록", 890, 330, 1210, 384, 20, TextAnchor.MiddleRight, Muted), 17);
            for (int i = 0; i < RowCount; i++)
            {
                float y0 = 394 + i * 128f;
                rowBoxes[i] = CompyaUiKit.Box(tableGroup, $"Row{i}", 20, y0, 1228, y0 + 118, RowCard);
                TextFit193.Mask(rowBoxes[i]);
                // 행 박스 안쪽 좌표(부모 = 행 박스 전체가 1248 × 1972 기준)
                rowRank[i] = TextFit193.Fit(kit.Label(rowBoxes[i].transform, "Rank", "", 10, 0, 130, 1972, 24, TextAnchor.MiddleCenter, Gold), 22);
                rowName[i] = TextFit193.Fit(kit.Label(rowBoxes[i].transform, "Name", "", 150, 0, 630, 1972, 24, TextAnchor.MiddleLeft, White), 20);
                rowTeam[i] = TextFit193.Fit(kit.Label(rowBoxes[i].transform, "Team", "", 640, 0, 870, 1972, 20, TextAnchor.MiddleCenter, Muted), 17);
                rowValue[i] = TextFit193.Fit(kit.Label(rowBoxes[i].transform, "Value", "", 880, 0, 1220, 1972, 24, TextAnchor.MiddleRight, White), 21);
            }
            emptyText = TextFit193.Fit(kit.Label(tableGroup, "Empty", "", 40, 400, 1208, 600, 22, TextAnchor.MiddleCenter, Muted), 19);

            // ---- 명예의 전당
            hallGroup = CompyaUiKit.Fill(root, "HallOfFame");
            hallContent = ScrollList(hallGroup, "HallScroll", 20, 252, 1228, 1900);

            if (Application.isPlaying) Refresh();
        }

        private static RectTransform ScrollList(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var viewport = CompyaUiKit.Place(parent, name, x0, y0, x1, y1);
            CompyaUiKit.Paint(viewport, new Color(0f, 0f, 0f, 0.2f), true);
            viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = content.offsetMax = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;
            return content;
        }

        // ================================================================== 탭

        public void SelectTab(Tab next)
        {
            tab = next;
            category = 0;
            Refresh();
        }

        public void SelectCategory(int next)
        {
            category = Mathf.Clamp(next, 0, categoryButtons.Length - 1);
            Refresh();
        }

        // ================================================================== 갱신

        private static bool IsUserCard(Player p)
        {
            var gm = GameManager.Instance;
            return gm != null && p != null && (gm.Roster.Contains(p) || gm.Inventory.Contains(p));
        }

        public void Refresh()
        {
            if (root == null || kit == null) return;
            var league = LeagueManager.Instance;
            subText.text = league != null
                ? $"시즌 기록 · 명예의 전당 · {LeagueTierTable.DisplayName(league.CurrentTier)} · 경기일 {league.PlayedGameCount}/{LeagueManager.TotalUserGames}"
                : "시즌 기록 · 명예의 전당";

            for (int i = 0; i < tabButtons.Length; i++)
            {
                bool on = (int)tab == i;
                if (tabButtons[i].targetGraphic != null) tabButtons[i].targetGraphic.color = on ? ScoutHubUIController.ActiveTabColor : ScoutHubUIController.InactiveTabColor;
                var label = tabButtons[i].GetComponentInChildren<Text>(true);
                if (label == null) continue;
                label.color = on ? ScoutHubUIController.ActiveTabTextColor : ScoutHubUIController.InactiveTabTextColor;
                TextFit193.Fit(label, on ? ScoutHubUIController.ActiveTabPt : ScoutHubUIController.InactiveTabPt, 14);
            }

            bool hall = tab == Tab.HallOfFame;
            tableGroup.gameObject.SetActive(!hall);
            hallGroup.gameObject.SetActive(hall);
            if (hall) RefreshHall();
            else RefreshTable(league);
        }

        private void RefreshTable(LeagueManager league)
        {
            var labels = tab == Tab.Batters ? LeagueRecordsRules.BatterLabels : LeagueRecordsRules.PitcherLabels;
            for (int i = 0; i < categoryButtons.Length; i++)
            {
                bool on = i == category;
                if (categoryButtons[i].targetGraphic != null) categoryButtons[i].targetGraphic.color = on ? CategoryOn : Slate;
                CompyaUiKit.SetButtonText(categoryButtons[i], labels[i]);
                var label = categoryButtons[i].GetComponentInChildren<Text>(true);
                if (label != null) label.color = on ? Dark : White;
            }
            headerValueText.text = labels[category];

            var stats = SeasonStatManager.Instance;
            int games = league != null ? league.PlayedGameCount : 0;
            var rows = stats == null ? new List<RecordRow>()
                : tab == Tab.Batters
                    ? LeagueRecordsRules.TopBatters(stats.AllBatterStats, (BatterRecord)category, games, IsUserCard)
                    : LeagueRecordsRules.TopPitchers(stats.AllPitcherStats, (PitcherRecord)category, games, IsUserCard);
            emptyText.gameObject.SetActive(rows.Count == 0);
            emptyText.text = rows.Count == 0 ? "아직 이 부문 기록이 없습니다 - 경기를 진행하면 순위가 채워집니다(타율 · 평균자책점은 규정 타석/이닝 필요)." : "";
            for (int i = 0; i < RowCount; i++)
            {
                bool used = i < rows.Count;
                rowBoxes[i].gameObject.SetActive(used);
                if (!used) continue;
                var r = rows[i];
                rowBoxes[i].color = r.IsUser ? UserRow : RowCard;
                rowRank[i].text = $"{r.Rank}";
                rowName[i].text = r.IsUser ? $"★ {r.Name}" : r.Name;
                rowName[i].color = r.IsUser ? Gold : White;
                rowTeam[i].text = CompyaUiKit.ShortName(r.Team);
                rowValue[i].text = r.Value;
            }
        }

        private void RefreshHall()
        {
            for (int i = hallContent.childCount - 1; i >= 0; i--)
            {
                var child = hallContent.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            var entries = SeasonRollover.Instance != null ? SeasonRollover.Instance.HallOfFame.Reverse().ToList() : new List<HallOfFameEntry>();
            if (entries.Count == 0)
            {
                AddHallCard("아직 기록된 시즌이 없습니다.\n정규시즌 144경기 → 포스트시즌 → 시즌 결산 후 [다음 시즌 시작]을 누르면 시즌 기록이 명예의 전당에 쌓입니다.", RowCard);
                return;
            }
            foreach (var e in entries) AddHallCard(LeagueRecordsRules.EntryText(e), e.KoreanSeriesWon ? new Color(0.3f, 0.22f, 0.06f) : RowCard);
        }

        private void AddHallCard(string text, Color color)
        {
            var card = new GameObject("Season", typeof(RectTransform)).GetComponent<RectTransform>();
            card.SetParent(hallContent, false);
            CompyaUiKit.Paint(card, color);
            card.gameObject.AddComponent<LayoutElement>().preferredHeight = 150f;
            TextFit193.Mask(card);
            var label = kit.LabelOn(CompyaUiKit.Norm(card, "Text", 0.02f, 0.05f, 0.98f, 0.95f), text, 20, TextAnchor.MiddleLeft, White);
            TextFit193.Fit(label, 17, 13);
            label.lineSpacing = 1.1f;
        }
    }
}
