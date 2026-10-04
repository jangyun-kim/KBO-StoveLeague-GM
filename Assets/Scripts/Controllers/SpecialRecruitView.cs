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
    /// <summary>[TASK-KBO-185] GameManager → IRecruitLedger 어댑터(포인트 = 볼(GameGold), 트로피 = Trophy).</summary>
    public sealed class GameManagerRecruitLedger : IRecruitLedger
    {
        private readonly GameManager gm;
        public GameManagerRecruitLedger(GameManager gameManager) { gm = gameManager; }
        public IReadOnlyList<Player> Inventory => gm.Inventory;
        public IReadOnlyList<Player> Roster => gm.Roster;
        public Team FavoriteTeam => gm.FavoriteTeam;
        public int Points { get => gm.GameGold; set => gm.GameGold = value; }
        public int Trophies { get => gm.Trophy; set => gm.Trophy = value; }
        public void RemoveCard(Player player) => gm.RemovePlayerFromInventory(player);
        public void AddCard(Player player) => gm.AddPlayerToInventory(player);
    }

    /// <summary>
    /// [TASK-KBO-185] 스카우트 허브 [특별 영입(골글 · 시그니처)] 섹션 - 컴프야V26 "선수 영입"(재료 슬롯 투입) 벤치마킹.
    ///   상단 [골든글러브 영입] | [시그니처 영입] 전환 → 재료 슬롯(조건 · 보유 현황 · 등록 카드) → 재화 → [조건 맞는 재료 자동 등록]/[등록 해제]
    ///   → 선택 구단 대상 선수 미리보기 → [영입 실행]. 규칙은 SpecialRecruitRules(단위 테스트 대상)를 그대로 쓴다.
    /// 자동 등록은 주전 라인업 카드를 절대 넣지 않는다. CompyaMatchView/GrowthCenterView처럼 Setup과 런타임 Awake가 같은 Build()로 계층을 만든다.
    /// </summary>
    public class SpecialRecruitView : MonoBehaviour
    {
        public const string RootName = "SpecialRecruit185";

        [SerializeField] private Font boldFont;
        [SerializeField] private Font regularFont;

        private static readonly Color Bg = new Color(0.06f, 0.08f, 0.15f);
        private static readonly Color Panel = new Color(0.11f, 0.14f, 0.24f);
        private static readonly Color PanelLight = new Color(0.16f, 0.2f, 0.33f);
        private static readonly Color White = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color Muted = new Color(0.66f, 0.72f, 0.84f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);
        private static readonly Color Green = new Color(0.37f, 0.88f, 0.54f);
        private static readonly Color Red = new Color(1f, 0.45f, 0.45f);
        private static readonly Color TabOn = new Color(0.98f, 0.76f, 0.2f);
        private static readonly Color TabOff = new Color(0.2f, 0.25f, 0.4f);
        private static readonly Color SlotDone = new Color(0.13f, 0.36f, 0.25f);
        private const int MaxSlots = 5;

        private CompyaUiKit kit;
        private RectTransform root;
        private Text teamText, descText, costText, resultText, previewTitleText;
        private readonly Button[] kindButtons = new Button[2];
        private readonly Image[] slotBoxes = new Image[MaxSlots];
        private readonly Text[] slotTitles = new Text[MaxSlots];
        private readonly Text[] slotStatus = new Text[MaxSlots];
        private Button autoButton, clearButton, executeButton;
        private RectTransform previewContent;

        private SpecialRecruitKind kind = SpecialRecruitKind.GoldenGlove;
        private List<List<Player>> assignment = new List<List<Player>>();

        public SpecialRecruitKind Kind => kind;
        public IReadOnlyList<IReadOnlyList<Player>> Assignment => assignment;

        public void Configure(Font bold, Font regular)
        {
            boldFont = bold;
            regularFont = regular;
        }

        private void Awake() => Build();

        private void OnEnable()
        {
            if (root != null) Refresh();
        }

        // ================================================================== 공개 진입점

        public void SelectKind(SpecialRecruitKind next)
        {
            kind = next;
            assignment = new List<List<Player>>();
            if (resultText != null) resultText.text = "";
            Refresh();
        }

        /// <summary>[조건 맞는 재료 자동 등록] - 라인업 카드는 제외한다.</summary>
        public void AutoRegister()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            var recipe = SpecialRecruitRules.Recipe(kind);
            assignment = SpecialRecruitRules.AutoAssign(recipe, gm.Inventory, gm.Roster, gm.FavoriteTeam);
            int missing = recipe.Slots.Select((s, i) => s.Count - assignment[i].Count).Sum();
            SetResult(missing == 0 ? "재료 자동 등록 완료 - 주전 라인업 카드는 제외했습니다." : $"자동 등록: 조건에 맞는 재료가 {missing}장 부족합니다.", missing == 0);
            Refresh();
        }

        public void ClearRegistration()
        {
            assignment = new List<List<Player>>();
            SetResult("", true);
            Refresh();
        }

        /// <summary>[영입 실행] - 재료 소모 · 재화 차감 · 선택 구단 GG/SIG 1장 확정 획득 → 세이브.</summary>
        public bool Execute()
        {
            var gm = GameManager.Instance;
            if (gm == null) return false;
            var recipe = SpecialRecruitRules.Recipe(kind);
            var ledger = new GameManagerRecruitLedger(gm);
            var scout = ScoutManager.Instance != null ? ScoutManager.Instance : FindAnyObjectByType<ScoutManager>();
            var db = scout != null && scout.Database != null ? scout.Database : FindAnyObjectByType<PlayerDatabase>();
            bool ok = SpecialRecruitRules.TryRecruit(recipe, Snapshot(), ledger, db != null ? db.AllTemplates : null,
                n => Random.Range(0, n),
                t => scout != null ? scout.IssueCard(t) : new Player(System.Guid.NewGuid().ToString(), t),
                out var card, out var message);
            if (ok)
            {
                assignment = new List<List<Player>>();
                SaveManager.Instance?.TrySaveCareer();
                Debug.Log($"[SpecialRecruitView] {message}");
            }
            SetResult(message, ok);
            Refresh();
            return ok && card != null;
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
            CompyaUiKit.Paint(root, Bg, true);

            kit.GradientBox(root, "Header", 0, 0, 1248, 150, new Color(0.45f, 0.3f, 0.08f), new Color(0.2f, 0.12f, 0.03f), false);
            kit.Label(root, "Title", "특별 영입 · 골든글러브 / 시그니처", 30, 10, 880, 90, 40, TextAnchor.MiddleLeft, Gold, true);
            // [TASK-KBO-189] 재료 획득처 / 포지션 재조합 바로가기 → [상점 · 교환소]
            kit.Button(root, "SourceButton", "재료 획득처 · 재조합 ▸", 890, 18, 1228, 84, new Color(0.98f, 0.76f, 0.2f, 0.9f), new Color(0.12f, 0.08f, 0.02f), 26)
                .onClick.AddListener(() => ShopExchangeView.OpenShop(ShopSubTab.CoinExchange));
            teamText = kit.Label(root, "Team", "", 30, 88, 1218, 145, 28, TextAnchor.MiddleLeft, White);

            var kinds = new[] { SpecialRecruitKind.GoldenGlove, SpecialRecruitKind.Signature };
            for (int i = 0; i < kinds.Length; i++)
            {
                var k = kinds[i];
                float x0 = 20 + i * 608f;
                kindButtons[i] = kit.Button(root, $"Kind_{k}", SpecialRecruitRules.Recipe(k).Name, x0, 165, x0 + 600, 255, TabOff, White, 38);
                kindButtons[i].onClick.AddListener(() => SelectKind(k));
            }
            descText = kit.Label(root, "Desc", "", 28, 262, 1220, 335, 27, TextAnchor.MiddleLeft, White);

            for (int i = 0; i < MaxSlots; i++)
            {
                float y0 = 345 + i * 150f;
                slotBoxes[i] = CompyaUiKit.Box(root, $"Slot{i}", 20, y0, 1228, y0 + 140, Panel);
                slotTitles[i] = kit.Label(slotBoxes[i].transform, "Title", "", 0, 0, 470, 1972, 34, TextAnchor.MiddleLeft, Gold, true);
                slotStatus[i] = kit.Label(slotBoxes[i].transform, "Status", "", 480, 0, 1230, 1972, 28, TextAnchor.MiddleLeft, White);
            }

            costText = kit.Label(root, "Cost", "", 28, 1100, 1220, 1160, 30, TextAnchor.MiddleLeft, White, true);
            autoButton = kit.Button(root, "AutoRegister", "조건 맞는 재료 자동 등록", 20, 1168, 820, 1258, new Color(0.15f, 0.33f, 0.88f), White, 32);
            autoButton.onClick.AddListener(AutoRegister);
            clearButton = kit.Button(root, "ClearRegister", "등록 해제", 830, 1168, 1228, 1258, PanelLight, White, 32);
            clearButton.onClick.AddListener(ClearRegistration);

            previewTitleText = kit.Label(root, "PreviewTitle", "영입 대상 미리보기", 28, 1268, 1220, 1318, 30, TextAnchor.MiddleLeft, White, true);
            previewContent = ScrollList(root, "PreviewScroll", 20, 1322, 1228, 1640);

            resultText = kit.Label(root, "Result", "", 28, 1648, 1220, 1712, 28, TextAnchor.MiddleCenter, Green, true);
            executeButton = kit.GradientButton(root, "Execute", "영입 실행", 20, 1720, 1228, 1840,
                new Color(1f, 0.8f, 0.25f), new Color(0.93f, 0.55f, 0.1f), new Color(0.12f, 0.08f, 0.02f), 48);
            executeButton.onClick.AddListener(() => Execute());

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
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(340f, 64f);
            grid.spacing = new Vector2(8f, 8f);
            grid.padding = new RectOffset(8, 8, 8, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;
            return content;
        }

        // ================================================================== 갱신

        private List<IReadOnlyList<Player>> Snapshot() => assignment.Select(a => (IReadOnlyList<Player>)a.ToList()).ToList();

        public void Refresh()
        {
            if (root == null || kit == null) return;
            var gm = GameManager.Instance;
            var recipe = SpecialRecruitRules.Recipe(kind);
            var favorite = gm != null ? gm.FavoriteTeam : Team.None;

            // 등록 카드가 소모/라인업 편성으로 무효가 되면 슬롯에서 뺀다.
            var available = gm != null ? new HashSet<Player>(SpecialRecruitRules.AvailableMaterials(gm.Inventory, gm.Roster)) : new HashSet<Player>();
            while (assignment.Count < recipe.Slots.Count) assignment.Add(new List<Player>());
            if (assignment.Count > recipe.Slots.Count) assignment.RemoveRange(recipe.Slots.Count, assignment.Count - recipe.Slots.Count);
            for (int i = 0; i < assignment.Count; i++) assignment[i].RemoveAll(p => !available.Contains(p) || !recipe.Slots[i].Accepts(p, favorite));

            for (int i = 0; i < kindButtons.Length; i++)
            {
                bool on = (int)kind == i;
                if (kindButtons[i].targetGraphic is Image image) image.color = on ? TabOn : TabOff;
                var label = kindButtons[i].transform.Find("Text")?.GetComponent<Text>();
                if (label != null) label.color = on ? new Color(0.12f, 0.08f, 0.02f) : White;
            }

            string teamName = favorite != Team.None ? CompyaUiKit.FullName(favorite) : "미선택";
            teamText.text = $"선택 구단: <color=#FFD54A>{teamName}</color> · 영입 시 선택 구단 {CardGrowthRules.DisplayName(recipe.TargetGrade)} 1장 확정 · 주전 라인업 카드는 재료에서 자동 제외";
            descText.text = kind == SpecialRecruitKind.GoldenGlove
                ? "컴프야V26 골든글러브 영입 완화판 - 3개 재료 슬롯을 채우면 선택 구단 골든글러브 카드를 확정 획득합니다."
                : "컴프야V26 골든글러브 영입 원본 수준 - 5개 재료 슬롯을 채우면 선택 구단 최상위 시그니처 카드를 확정 획득합니다.";

            var availableList = available.ToList();
            for (int i = 0; i < MaxSlots; i++)
            {
                bool used = i < recipe.Slots.Count;
                slotBoxes[i].gameObject.SetActive(used);
                if (!used) continue;
                var slot = recipe.Slots[i];
                var cards = assignment[i];
                bool done = cards.Count >= slot.Count;
                int owned = SpecialRecruitRules.EligibleCount(slot, availableList, favorite);
                slotBoxes[i].color = done ? SlotDone : Panel;
                slotTitles[i].text = $"{"①②③④⑤"[i]} {slot.Title}\n<size=75%><color=#C9D3EA>{slot.Condition}</color></size>";
                string names = cards.Count > 0 ? string.Join(", ", cards.Select(c => $"{CompyaMatchView.DisplayName(c)}({CardGrowthRules.DisplayName(c.Template.Grade)} {c.ReinforceLevel}강 {c.AwakenLabel})")) : "미등록";
                slotStatus[i].text = $"보유 {owned}장 · 등록 <color={(done ? "#5EE08A" : "#FF8A8A")}>{cards.Count}/{slot.Count}</color>\n<size=80%>{names}</size>";
            }

            int points = gm != null ? gm.GameGold : 0;
            int trophies = gm != null ? gm.Trophy : 0;
            var pay = SpecialRecruitRules.Payment(recipe, points, trophies);
            costText.text = $"재화: {SpecialRecruitRules.CostLabel(recipe)}  ·  보유 포인트 {points:N0}" + (recipe.TrophyCost > 0 ? $" / 트로피 {trophies:N0}" : "") +
                            (pay == null ? "  <color=#FF8A8A>(부족)</color>" : "  <color=#5EE08A>(충분)</color>");

            RebuildPreview(gm, recipe, favorite);

            string reason = gm != null ? SpecialRecruitRules.Validate(recipe, Snapshot(), new GameManagerRecruitLedger(gm)) : "게임 데이터가 없습니다.";
            executeButton.interactable = reason == null;
            CompyaUiKit.SetButtonText(executeButton, reason == null ? $"{recipe.Name} 실행" : "영입 실행 (조건 미충족)");
            clearButton.interactable = assignment.Any(a => a.Count > 0);
        }

        private void RebuildPreview(GameManager gm, RecruitRecipe recipe, Team favorite)
        {
            for (int i = previewContent.childCount - 1; i >= 0; i--)
            {
                var child = previewContent.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            var scout = ScoutManager.Instance;
            var db = scout != null && scout.Database != null ? scout.Database : (gm != null ? FindAnyObjectByType<PlayerDatabase>() : null);
            var pool = db != null ? SpecialRecruitRules.TargetPool(db.AllTemplates, recipe, favorite) : new List<PlayerTemplate>();
            previewTitleText.text = $"영입 대상 미리보기 - {(favorite != Team.None ? CompyaUiKit.ShortName(favorite) : "전체")} {CardGrowthRules.DisplayName(recipe.TargetGrade)} {pool.Count}명 (1명 무작위 확정)";
            foreach (var t in pool.Take(60))
            {
                var cell = CompyaUiKit.Place(previewContent, "Target", 0, 0, 10, 10);
                CompyaUiKit.Paint(cell, PanelLight);
                kit.LabelOn(CompyaUiKit.Norm(cell, "Text", 0.04f, 0.04f, 0.96f, 0.96f),
                    $"{t.PlayerName}'{t.SeasonYear % 100:00} <size=80%><color=#C9D3EA>{(t.IsPitcher ? t.PitcherRole.ToString() : t.BatterPosition.ToString())}</color></size>",
                    24, TextAnchor.MiddleCenter, White, true);
            }
        }

        private void SetResult(string message, bool ok)
        {
            if (resultText == null) return;
            resultText.color = ok ? Green : Red;
            resultText.text = message ?? "";
        }
    }
}
