using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-193] 스토브리그 전력 보강 허브(로비 위 오버레이) - 시즌 결산 화면 [스토브리그 전력 보강] 버튼 · 리그 기록실 [스토브리그 ▸]에서 연다.
    ///   [FA 시장] 6명 포인트 계약 | [트레이드] 제안 3건 수락 · 새로고침 | [신인 드래프트] 원하는 포지션 유망주 1명(시즌 1회 무료) | [외국인 영입] 후보 3명 중 1명(시즌 1회).
    /// 규칙은 StoveLeagueRules(단위 테스트 대상), 진행 상태는 GameManager.StoveLeague(세이브 v13). 획득 카드는 보유 선수 목록에 즉시 추가된다.
    /// 계층은 Build() 코드로 만든다(Setup · Awake 공용) - 글씨는 16~26pt Normal, 행은 다크 카드(#162032) + RectMask2D.
    /// </summary>
    public class StoveLeagueView : MonoBehaviour
    {
        public const string RootName = "StoveLeague193";
        public enum Tab { Fa = 0, Trade = 1, Draft = 2, Foreign = 3 }
        public const int RowCount = 6;

        [SerializeField] private Font boldFont;
        [SerializeField] private Font regularFont;

        private static readonly Color Dim = new Color(0f, 0f, 0f, 0.8f);
        private static readonly Color Panel = new Color(0.06f, 0.09f, 0.17f, 0.99f);
        private static readonly Color RowCard = new Color32(0x16, 0x20, 0x32, 0xFF);
        private static readonly Color White = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color Muted = new Color(0.79f, 0.84f, 0.92f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);
        private static readonly Color Green = new Color(0.37f, 0.88f, 0.54f);
        private static readonly Color Red = new Color(1f, 0.45f, 0.45f);
        private static readonly Color Blue = new Color(0.15f, 0.39f, 0.92f);
        private static readonly Color Slate = new Color(0.2f, 0.25f, 0.36f);

        private static StoveLeagueView instance;
        private CompyaUiKit kit;
        private RectTransform root;
        private Text walletText, descText, resultText;
        private readonly Button[] tabButtons = new Button[4];
        private readonly Image[] rowBoxes = new Image[RowCount];
        private readonly Text[] rowLine1 = new Text[RowCount];
        private readonly Text[] rowLine2 = new Text[RowCount];
        private readonly Button[] rowButtons = new Button[RowCount];
        private Button positionButton, secondaryButton, closeButton;
        private Tab tab = Tab.Fa;
        private int positionIndex;
        private List<PlayerTemplate> faOffers = new List<PlayerTemplate>();
        private List<TradeOffer> tradeOffers = new List<TradeOffer>();
        private List<PlayerTemplate> foreignOffers = new List<PlayerTemplate>();

        public Tab CurrentTab => tab;
        public bool IsOpen => root != null && root.gameObject.activeSelf;
        public string PositionKey => CardGrowthRules.PositionKeys[positionIndex];
        public IReadOnlyList<PlayerTemplate> FaOffers => faOffers;
        public IReadOnlyList<TradeOffer> TradeOffers => tradeOffers;
        public IReadOnlyList<PlayerTemplate> ForeignOffers => foreignOffers;

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

        private void OnEnable() => instance = this;

        private static StoveLeagueView Find() => instance != null ? instance : FindAnyObjectByType<StoveLeagueView>(FindObjectsInactive.Include);

        /// <summary>스토브리그 허브를 연다(로비가 꺼져 있으면 로비로 이동한 뒤). 없으면 false.</summary>
        public static bool OpenStove(Tab start = Tab.Fa)
        {
            var view = Find();
            if (view == null) return false;
            if (!view.gameObject.activeInHierarchy) UIManager.Instance?.ShowScreen(ScreenType.Lobby);
            view.gameObject.SetActive(true);
            view.Open(start);
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
            CompyaUiKit.Paint(root, Dim, true);
            CompyaUiKit.Box(root, "Panel", 30, 80, 1218, 1890, Panel);
            kit.GradientBox(root, "Header", 30, 80, 1218, 200, new Color(0.12f, 0.3f, 0.62f), new Color(0.05f, 0.12f, 0.3f), false);
            TextFit193.Fit(kit.Label(root, "Title", "스토브리그 전력 보강 (FA · 트레이드 · 드래프트)", 56, 86, 1192, 146, 30, TextAnchor.MiddleLeft, Gold), 26, 18);
            walletText = TextFit193.Fit(kit.Label(root, "Wallet", "", 56, 144, 1192, 196, 20, TextAnchor.MiddleLeft, White), 17);

            var names = new[] { "FA 시장", "트레이드", "신인 드래프트", "외국인 영입" };
            for (int i = 0; i < tabButtons.Length; i++)
            {
                var t = (Tab)i;
                float x0 = 46 + i * 290f;
                tabButtons[i] = kit.Button(root, $"Tab_{t}", names[i], x0, 214, x0 + 282, 290, ScoutHubUIController.InactiveTabColor, White, 24);
                tabButtons[i].onClick.AddListener(() => SelectTab(t));
                TextFit193.FitButton(tabButtons[i], i == 0 ? ScoutHubUIController.ActiveTabPt : ScoutHubUIController.InactiveTabPt, 14);
            }
            descText = TextFit193.Fit(kit.Label(root, "Desc", "", 56, 298, 1192, 368, 20, TextAnchor.MiddleLeft, Muted), 17);
            positionButton = kit.Button(root, "PositionSelect", "", 46, 376, 1202, 446, Slate, White, 24);
            positionButton.onClick.AddListener(CyclePosition);
            TextFit193.FitButton(positionButton, 19);

            for (int i = 0; i < RowCount; i++)
            {
                float y0 = 456 + i * 172f;
                rowBoxes[i] = CompyaUiKit.Box(root, $"Row{i}", 46, y0, 1202, y0 + 160, RowCard);
                TextFit193.Mask(rowBoxes[i]);
                // 행 박스 안쪽 좌표(부모 = 행 박스 전체가 1248 × 1972 기준)
                rowLine1[i] = TextFit193.Fit(kit.Label(rowBoxes[i].transform, "Line1", "", 30, 120, 930, 1000, 24, TextAnchor.MiddleLeft, White), 19);
                rowLine2[i] = TextFit193.Fit(kit.Label(rowBoxes[i].transform, "Line2", "", 30, 1000, 930, 1880, 20, TextAnchor.MiddleLeft, Gold), 16);
                int index = i;
                rowButtons[i] = kit.Button(rowBoxes[i].transform, "Action", "", 950, 330, 1220, 1640, Blue, White, 24);
                rowButtons[i].onClick.AddListener(() => OnRowAction(index));
                TextFit193.FitButton(rowButtons[i], 18);
            }

            resultText = TextFit193.Fit(kit.Label(root, "Result", "", 56, 1494, 1192, 1574, 22, TextAnchor.MiddleCenter, Green), 18);
            secondaryButton = kit.GradientButton(root, "Secondary", "", 46, 1586, 1202, 1700, new Color(1f, 0.8f, 0.25f), new Color(0.93f, 0.55f, 0.1f), new Color(0.12f, 0.08f, 0.02f), 30);
            secondaryButton.onClick.AddListener(OnSecondary);
            TextFit193.FitButton(secondaryButton, 22, 16);
            closeButton = kit.Button(root, "Close", "닫기", 46, 1716, 1202, 1866, Slate, White, 26);
            closeButton.onClick.AddListener(Hide);
            TextFit193.FitButton(closeButton, 20);

            root.gameObject.SetActive(false);
        }

        // ================================================================== 진입 · 탭

        public void Open(Tab start = Tab.Fa)
        {
            if (root == null) Build();
            root.gameObject.SetActive(true);
            transform.SetAsLastSibling();
            root.SetAsLastSibling();
            SetResult("", true);
            SelectTab(start);
        }

        public void Hide()
        {
            if (root != null) root.gameObject.SetActive(false);
        }

        public void SelectTab(Tab next)
        {
            tab = next;
            SetResult("", true);
            Refresh();
        }

        public void CyclePosition()
        {
            positionIndex = (positionIndex + 1) % CardGrowthRules.PositionKeys.Length;
            Refresh();
        }

        // ================================================================== 데이터

        /// <summary>시즌 키 = 리그 캘린더 연도(새 시즌 시작 시 +1) - 없으면 명예의 전당 누적 수.</summary>
        public static int CurrentSeasonKey()
        {
            if (LeagueCalendar.Instance != null) return LeagueCalendar.Instance.CurrentDate.Year;
            return SeasonRollover.Instance != null ? SeasonRollover.Instance.HallOfFame.Count : 0;
        }

        private static IReadOnlyList<PlayerTemplate> Templates()
        {
            var scout = ScoutManager.Instance;
            var db = scout != null && scout.Database != null ? scout.Database : FindAnyObjectByType<PlayerDatabase>();
            return db != null ? db.AllTemplates : new List<PlayerTemplate>();
        }

        private static Player Issue(PlayerTemplate t)
        {
            var scout = ScoutManager.Instance != null ? ScoutManager.Instance : FindAnyObjectByType<ScoutManager>();
            return scout != null ? scout.IssueCard(t) : new Player(System.Guid.NewGuid().ToString(), t);
        }

        private StoveLeagueState State(GameManager gm)
        {
            var state = gm.StoveLeague;
            state.EnsureSeason(CurrentSeasonKey());
            return state;
        }

        // ================================================================== 갱신

        public void Refresh()
        {
            if (root == null || kit == null) return;
            var gm = GameManager.Instance;
            for (int i = 0; i < tabButtons.Length; i++)
            {
                bool on = (int)tab == i;
                if (tabButtons[i].targetGraphic != null) tabButtons[i].targetGraphic.color = on ? ScoutHubUIController.ActiveTabColor : ScoutHubUIController.InactiveTabColor;
                var label = tabButtons[i].GetComponentInChildren<Text>(true);
                if (label == null) continue;
                label.color = on ? ScoutHubUIController.ActiveTabTextColor : ScoutHubUIController.InactiveTabTextColor;
                TextFit193.Fit(label, on ? ScoutHubUIController.ActiveTabPt : ScoutHubUIController.InactiveTabPt, 14);
            }

            if (gm == null)
            {
                walletText.text = "게임 데이터가 없습니다.";
                for (int i = 0; i < RowCount; i++) rowBoxes[i].gameObject.SetActive(false);
                positionButton.gameObject.SetActive(false);
                secondaryButton.gameObject.SetActive(false);
                return;
            }
            var state = State(gm);
            var favorite = gm.FavoriteTeam;
            string team = favorite != Team.None ? CompyaUiKit.ShortName(favorite) : "미선택";
            walletText.text = $"선택 구단 <color=#FFD54A>{team}</color> · 포인트 <color=#FFD54A>{gm.GameGold:N0}</color> · 보유 선수 {gm.Inventory.Count}명 · 시즌 {state.SeasonKey}";
            positionButton.gameObject.SetActive(tab == Tab.Draft);
            secondaryButton.gameObject.SetActive(tab == Tab.Trade || tab == Tab.Draft);

            switch (tab)
            {
                case Tab.Fa: RefreshFa(gm, state); break;
                case Tab.Trade: RefreshTrade(gm, state); break;
                case Tab.Draft: RefreshDraft(gm, state); break;
                default: RefreshForeign(gm, state); break;
            }
        }

        private void ShowRow(int i, string line1, string line2, string action, bool interactable, Color line2Color)
        {
            rowBoxes[i].gameObject.SetActive(true);
            rowLine1[i].text = line1;
            rowLine2[i].text = line2;
            rowLine2[i].color = line2Color;
            rowButtons[i].gameObject.SetActive(!string.IsNullOrEmpty(action));
            CompyaUiKit.SetButtonText(rowButtons[i], action);
            rowButtons[i].interactable = interactable;
        }

        private void HideRowsFrom(int from)
        {
            for (int i = from; i < RowCount; i++) rowBoxes[i].gameObject.SetActive(false);
        }

        private void RefreshFa(GameManager gm, StoveLeagueState state)
        {
            descText.text = "① FA 시장 - 이번 시즌 FA 6명(라이브 에픽 ~ 타이틀 홀더, 선택 구단 선수 50% 이상). 포인트로 계약하면 보유 선수에 즉시 추가됩니다.";
            faOffers = StoveLeagueRules.FaOffers(Templates(), gm.FavoriteTeam, state.SeasonKey);
            for (int i = 0; i < RowCount; i++)
            {
                if (i >= faOffers.Count) { HideRowsFrom(i); break; }
                var t = faOffers[i];
                bool signed = state.SignedFaIds.Contains(t.TemplateId);
                int price = StoveLeagueRules.ContractPrice(t.Grade);
                string own = t.Team == gm.FavoriteTeam ? " · <color=#7CD3FF>선택 구단</color>" : $" · {CompyaUiKit.ShortName(t.Team)}";
                ShowRow(i, StoveLeagueRules.OfferLine(t) + own, signed ? "계약 완료 - 보유 선수에 추가됨" : $"계약금 {price:N0}P",
                    signed ? "계약 완료" : "계약", !signed && gm.GameGold >= price, signed ? Green : Gold);
            }
        }

        private void RefreshTrade(GameManager gm, StoveLeagueState state)
        {
            descText.text = "② 트레이드 - 내 잉여 카드 1~2장 ↔ 선택 구단 · 주력 포지션 동급 이상 카드 1장. 주전 라인업 · 골든글러브 이상 카드는 내주지 않습니다.";
            tradeOffers = StoveLeagueRules.TradeOffers(gm.Inventory, gm.Roster, Templates(), gm.FavoriteTeam, state.SeasonKey, state.TradeRound);
            for (int i = 0; i < RowCount; i++)
            {
                if (i >= tradeOffers.Count)
                {
                    if (i == 0) ShowRow(0, "트레이드 가능한 잉여 카드가 없습니다.", "라인업 밖 보유 카드(타이틀 홀더 이하)가 있어야 제안을 받습니다.", "", false, Muted);
                    HideRowsFrom(i == 0 ? 1 : i);
                    break;
                }
                var o = tradeOffers[i];
                string give = string.Join(", ", o.Give.Select(p => $"{CardDisplay.NameWithYear(p.Template)}({CardDisplay.GradeShort(p.Template.Grade)})"));
                ShowRow(i, $"내줌 {give}", $"받음 {StoveLeagueRules.OfferLine(o.Receive)}", "수락", true, Gold);
            }
            CompyaUiKit.SetButtonText(secondaryButton, $"새 제안 받기 (새로고침 -{StoveLeagueRules.TradeRefreshCost:N0}P) · 성사 {state.TradesAccepted}건");
            secondaryButton.interactable = gm.GameGold >= StoveLeagueRules.TradeRefreshCost;
        }

        private void RefreshDraft(GameManager gm, StoveLeagueState state)
        {
            descText.text = "③ 신인 드래프트 - 선택 구단 · 원하는 포지션의 최신 연도 유망주(라이브 에픽 · 올스타) 1명을 무료로 지명합니다. 시즌 1회.";
            CompyaUiKit.SetButtonText(positionButton, $"지명 포지션: {PositionKey} ▸ (눌러서 변경)");
            var pool = StoveLeagueRules.DraftPool(Templates(), gm.FavoriteTeam, PositionKey);
            for (int i = 0; i < RowCount; i++)
            {
                if (i >= pool.Count)
                {
                    if (i == 0) ShowRow(0, "지명 가능한 유망주가 없습니다.", "다른 포지션을 선택하십시오.", "", false, Muted);
                    HideRowsFrom(i == 0 ? 1 : i);
                    break;
                }
                ShowRow(i, StoveLeagueRules.OfferLine(pool[i]), "지명 후보 (무작위 1명)", "", false, Gold);
            }
            CompyaUiKit.SetButtonText(secondaryButton, state.DraftUsed ? "이번 시즌 신인 지명 완료" : $"{PositionKey} 유망주 지명 (무료 · 시즌 1회)");
            secondaryButton.interactable = !state.DraftUsed && pool.Count > 0;
        }

        private void RefreshForeign(GameManager gm, StoveLeagueState state)
        {
            descText.text = "④ 외국인 선수 스카우트 - 이번 시즌 후보 3명 중 1명과 포인트로 계약합니다(라이브 에픽 ~ 타이틀 홀더). 시즌 1회.";
            foreignOffers = StoveLeagueRules.ForeignCandidates(Templates(), gm.FavoriteTeam, state.SeasonKey);
            for (int i = 0; i < RowCount; i++)
            {
                if (i >= foreignOffers.Count)
                {
                    if (i == 0) ShowRow(0, "외국인 선수 데이터가 없습니다.", "", "", false, Muted);
                    HideRowsFrom(i == 0 ? 1 : i);
                    break;
                }
                var t = foreignOffers[i];
                int price = StoveLeagueRules.ContractPrice(t.Grade);
                ShowRow(i, $"{StoveLeagueRules.OfferLine(t)} · {CompyaUiKit.ShortName(t.Team)}", state.ForeignUsed ? "이번 시즌 외국인 영입 완료" : $"계약금 {price:N0}P",
                    state.ForeignUsed ? "완료" : "계약", !state.ForeignUsed && gm.GameGold >= price, state.ForeignUsed ? Muted : Gold);
            }
        }

        // ================================================================== 실행

        private void OnRowAction(int index)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var state = State(gm);
            var ledger = new GameManagerRecruitLedger(gm);
            bool ok = false;
            string message;
            if (tab == Tab.Fa)
            {
                if (index < faOffers.Count) ok = StoveLeagueRules.TrySignFa(faOffers[index], state, ledger, Issue, out _, out message);
                else message = "FA 정보가 없습니다.";
            }
            else if (tab == Tab.Trade)
            {
                if (index < tradeOffers.Count) ok = StoveLeagueRules.TryAcceptTrade(tradeOffers[index], state, ledger, Issue, out _, out message);
                else message = "트레이드 제안이 없습니다.";
            }
            else if (tab == Tab.Foreign)
            {
                if (index < foreignOffers.Count) ok = StoveLeagueRules.TrySignForeign(foreignOffers[index], state, ledger, Issue, out _, out message);
                else message = "외국인 선수 정보가 없습니다.";
            }
            else return;
            Finish(ok, message);
        }

        private void OnSecondary()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var state = State(gm);
            var ledger = new GameManagerRecruitLedger(gm);
            bool ok;
            string message;
            if (tab == Tab.Trade) ok = StoveLeagueRules.TryRefreshTrades(state, ledger, out message);
            else if (tab == Tab.Draft) ok = StoveLeagueRules.TryDraft(Templates(), PositionKey, state, ledger, n => Random.Range(0, n), Issue, out _, out message);
            else return;
            Finish(ok, message);
        }

        private void Finish(bool ok, string message)
        {
            if (ok) SaveManager.Instance?.TrySaveCareer();
            SetResult(message, ok);
            Refresh();
        }

        private void SetResult(string message, bool ok)
        {
            if (resultText == null) return;
            resultText.color = ok ? Green : Red;
            resultText.text = message ?? "";
        }
    }
}
