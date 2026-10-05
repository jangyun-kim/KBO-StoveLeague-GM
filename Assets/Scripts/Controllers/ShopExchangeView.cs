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
    /// <summary>[TASK-KBO-189] 상점 · 교환소 서브 탭.</summary>
    public enum ShopSubTab
    {
        PointShop = 0,  // 포인트 상점(상시)
        CoinExchange = 1, // 성장 코인 교환소
        Release = 2,    // 선수 방출
        Recombine = 3,  // 3:1 포지션 재조합
        Guide = 4,      // 재료 획득처 안내
    }

    /// <summary>
    /// [TASK-KBO-189] 스카우트 허브 [상점 · 교환소] 섹션 - 컴프야V26 운영 기조(잉여 카드 재활용 · 포지션/구단 맞춤 수급 · 명확한 재료 획득처).
    ///   [포인트 상점] 포지션별 LIVE 영입팩 · 훈련/특훈 재료 상자 · 강화 보조팩
    ///   [코인 교환소] 선택 구단 · 포지션 지정 스페셜팩 · 범용 각성 보조권 · 초월 핵심 대체권 · 골글/시그니처 특별 영입 재료 상자
    ///   [선수 방출] 라인업 외 카드 다중 선택 → 포인트 + 성장 코인
    ///   [3:1 재조합] 같은 시즌 등급 3장 → 지정 포지션 1장(선택 구단 우선)
    ///   [획득처 안내] 리그 단계별 재료 보상표 + 재료별 획득처
    /// 규칙은 ShopExchangeRules / LeagueMaterialRewards(단위 테스트 대상)를 그대로 쓴다. SpecialRecruitView처럼 Setup과 런타임 Awake가 같은 Build()로 계층을 만든다.
    /// </summary>
    public class ShopExchangeView : MonoBehaviour
    {
        public const string RootName = "ShopExchange189";
        private const int ProductRows = 7; // [TASK-KBO-190] 코인 교환소 7종(스킬 변경권 · 고급 스킬 변경권 추가)

        [SerializeField] private Font boldFont;
        [SerializeField] private Font regularFont;

        private static readonly Color Bg = new Color(0.06f, 0.08f, 0.15f);
        private static readonly Color Panel = new Color(0.11f, 0.14f, 0.24f);
        private static readonly Color PanelLight = new Color(0.16f, 0.2f, 0.33f);
        private static readonly Color White = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);
        private static readonly Color Green = new Color(0.37f, 0.88f, 0.54f);
        private static readonly Color Red = new Color(1f, 0.45f, 0.45f);
        private static readonly Color TabOn = new Color(0.98f, 0.76f, 0.2f);
        private static readonly Color TabOff = new Color(0.2f, 0.25f, 0.4f);
        private static readonly Color RowOn = new Color(0.95f, 0.72f, 0.18f, 0.95f);
        private static readonly Color RowOff = new Color(0.2f, 0.24f, 0.38f, 0.95f);
        private static readonly Color Blue = new Color(0.15f, 0.33f, 0.88f);
        private static readonly Color Dark = new Color(0.12f, 0.08f, 0.02f);

        private CompyaUiKit kit;
        private RectTransform root;
        private Text walletText, descText, resultText, guideText, listTitleText;
        private readonly Button[] subTabButtons = new Button[5];
        private readonly Image[] productBoxes = new Image[ProductRows];
        private readonly Text[] productTitles = new Text[ProductRows];
        private readonly Text[] productDescs = new Text[ProductRows];
        private readonly Button[] buyButtons = new Button[ProductRows];
        private Button positionButton, gradeButton, executeButton, clearButton;
        private RectTransform productGroup, listGroup, listContent;

        private ShopSubTab subTab = ShopSubTab.PointShop;
        private int positionIndex = 7; // RF
        private int specialGradeIndex;
        private Grade recombineGrade = Grade.LIVE_NORMAL;
        private readonly List<Player> selected = new List<Player>();

        public ShopSubTab CurrentSubTab => subTab;
        public string PositionKey => CardGrowthRules.PositionKeys[positionIndex];
        public Grade SpecialGrade => ShopExchangeRules.SpecialPackGrades[specialGradeIndex];
        public Grade RecombineGrade => recombineGrade;
        public IReadOnlyList<Player> Selected => selected;

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

        /// <summary>[재료 획득처 / 포지션 재조합 바로가기] - 스카우트 허브를 열고 [상점 · 교환소]의 지정 서브 탭으로 이동한다.</summary>
        public static bool OpenShop(ShopSubTab tab)
        {
            var hub = FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            if (hub == null) return false;
            UIManager.Instance?.ShowScreen(ScreenType.Scout);
            if (!hub.gameObject.activeInHierarchy) hub.gameObject.SetActive(true);
            hub.ShowShopSection(tab);
            return true;
        }

        public void SelectSubTab(ShopSubTab next)
        {
            subTab = next;
            selected.Clear();
            SetResult("", true);
            if (next == ShopSubTab.Recombine) recombineGrade = BestRecombineGrade();
            Refresh();
        }

        public void CyclePosition()
        {
            positionIndex = (positionIndex + 1) % CardGrowthRules.PositionKeys.Length;
            Refresh();
        }

        public void CycleGrade()
        {
            if (subTab == ShopSubTab.Recombine)
            {
                var grades = ShopExchangeRules.RecombineGrades;
                recombineGrade = grades[(System.Array.IndexOf(grades, recombineGrade) + 1) % grades.Length];
                selected.Clear();
            }
            else specialGradeIndex = (specialGradeIndex + 1) % ShopExchangeRules.SpecialPackGrades.Length;
            Refresh();
        }

        public bool Buy(ShopProduct product)
        {
            var gm = GameManager.Instance;
            if (gm == null) return false;
            var (templates, issue) = CardSource();
            var grade = product == ShopProduct.SpecialPositionPack ? SpecialGrade : Grade.ALLSTAR;
            bool ok = ShopExchangeRules.TryBuy(product, new GameManagerGrowthLedger(gm), PositionKey, grade, templates, n => Random.Range(0, n), issue, out _, out var message);
            if (ok) SaveManager.Instance?.TrySaveCareer();
            SetResult(message, ok);
            Refresh();
            return ok;
        }

        /// <summary>하단 실행 버튼 - [선수 방출] / [3:1 재조합].</summary>
        public bool Execute()
        {
            var gm = GameManager.Instance;
            if (gm == null) return false;
            var ledger = new GameManagerGrowthLedger(gm);
            bool ok;
            string message;
            if (subTab == ShopSubTab.Release)
            {
                ok = ShopExchangeRules.TryRelease(selected.ToList(), ledger, out _, out _, out message);
            }
            else if (subTab == ShopSubTab.Recombine)
            {
                var (templates, issue) = CardSource();
                ok = ShopExchangeRules.TryRecombine(selected.ToList(), PositionKey, ledger, templates, n => Random.Range(0, n), issue, out _, out message);
            }
            else return false;
            if (ok)
            {
                selected.Clear();
                SaveManager.Instance?.TrySaveCareer();
            }
            SetResult(message, ok);
            Refresh();
            return ok;
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

            kit.GradientBox(root, "Header", 0, 0, 1248, 150, new Color(0.1f, 0.36f, 0.3f), new Color(0.04f, 0.16f, 0.14f), false);
            kit.Label(root, "Title", "상점 · 교환소", 30, 10, 1218, 80, 44, TextAnchor.MiddleLeft, Gold, true);
            walletText = kit.Label(root, "Wallet", "", 30, 78, 1218, 148, 24, TextAnchor.MiddleLeft, White);

            var tabs = new[] { ShopSubTab.PointShop, ShopSubTab.CoinExchange, ShopSubTab.Release, ShopSubTab.Recombine, ShopSubTab.Guide };
            var names = new[] { "포인트 상점", "코인 교환소", "선수 방출", "3:1 재조합", "획득처 안내" };
            for (int i = 0; i < tabs.Length; i++)
            {
                var t = tabs[i];
                float x0 = 20 + i * 242f;
                subTabButtons[i] = kit.Button(root, $"Sub_{t}", names[i], x0, 162, x0 + 238, 240, TabOff, White, 30);
                subTabButtons[i].onClick.AddListener(() => SelectSubTab(t));
            }
            // [TASK-GM-02] 단장 모드 - 선수 카드 3:1 재조합(합성)은 카드 수집형 기능이라 탭을 숨긴다(GMFeatureFlags, 코드 · 데이터는 유지).
            if (!KBOManager.Core.GMFeatureFlags.IsPlayerGachaEnabled) subTabButtons[(int)ShopSubTab.Recombine].gameObject.SetActive(false);
            descText = kit.Label(root, "Desc", "", 28, 246, 1220, 320, 25, TextAnchor.MiddleLeft, White);

            // ---- 선택기(포지션 · 등급)
            positionButton = kit.Button(root, "PositionSelect", "", 20, 326, 620, 396, PanelLight, White, 30);
            positionButton.onClick.AddListener(CyclePosition);
            gradeButton = kit.Button(root, "GradeSelect", "", 628, 326, 1228, 396, PanelLight, White, 30);
            gradeButton.onClick.AddListener(CycleGrade);

            // ---- 상품 목록(최대 7행 - [TASK-KBO-190] 행 높이 215 → 165)
            productGroup = CompyaUiKit.Fill(root, "Products");
            for (int i = 0; i < ProductRows; i++)
            {
                float y0 = 410 + i * 175f;
                productBoxes[i] = CompyaUiKit.Box(productGroup, $"Product{i}", 20, y0, 1228, y0 + 165, Panel);
                productTitles[i] = kit.Label(productGroup, $"ProductTitle{i}", "", 44, y0 + 8, 900, y0 + 66, 32, TextAnchor.MiddleLeft, Gold, true);
                productDescs[i] = kit.Label(productGroup, $"ProductDesc{i}", "", 44, y0 + 68, 900, y0 + 158, 23, TextAnchor.UpperLeft, White);
                productDescs[i].resizeTextForBestFit = true;
                productDescs[i].resizeTextMinSize = Mathf.Min(productDescs[i].fontSize, TextTidy.AutoMin); // [TASK-KBO-192] 12 → 15
                productDescs[i].resizeTextMaxSize = productDescs[i].fontSize;
                int index = i;
                buyButtons[i] = kit.GradientButton(productGroup, $"Buy{i}", "구매", 920, y0 + 28, 1208, y0 + 138, new Color(1f, 0.8f, 0.25f), new Color(0.93f, 0.55f, 0.1f), Dark, 32);
                buyButtons[i].onClick.AddListener(() => OnBuy(index));
            }

            // ---- 카드 목록(방출 · 재조합)
            listGroup = CompyaUiKit.Fill(root, "ListGroup");
            listTitleText = kit.Label(listGroup, "ListTitle", "", 28, 404, 1220, 454, 28, TextAnchor.MiddleLeft, Gold, true);
            listContent = ScrollList(listGroup, "CardScroll", 20, 460, 1228, 1560);
            clearButton = kit.Button(listGroup, "ClearSelect", "선택 해제", 20, 1568, 1228, 1636, PanelLight, White, 28);
            clearButton.onClick.AddListener(() => { selected.Clear(); Refresh(); });

            // ---- 획득처 안내
            guideText = kit.Label(root, "Guide", "", 36, 410, 1212, 1640, 27, TextAnchor.UpperLeft, White);
            guideText.resizeTextForBestFit = true;
            guideText.resizeTextMinSize = Mathf.Min(guideText.fontSize, TextTidy.AutoMin); // [TASK-KBO-192] 12 → 15
            guideText.resizeTextMaxSize = guideText.fontSize;

            resultText = kit.Label(root, "Result", "", 28, 1648, 1220, 1712, 26, TextAnchor.MiddleCenter, Green, true);
            executeButton = kit.GradientButton(root, "Execute", "실행", 20, 1720, 1228, 1840,
                new Color(1f, 0.8f, 0.25f), new Color(0.93f, 0.55f, 0.1f), Dark, 44);
            executeButton.onClick.AddListener(() => Execute());
            ApplyReadableSizes();

            if (Application.isPlaying) Refresh();
        }

        /// <summary>[TASK-KBO-193] 글씨 폭주 · 흰 바탕 흰 글씨 점검 - 모든 텍스트를 목표 pt로 고정(최대 = 목표) + 세로 Truncate.
        /// 타이틀 26 · 실행 26 · 탭/선택기/버튼 18~20 · 상품명 19 · 설명/보유 16~17. 상품 행은 RectMask2D로 행 밖 그리기를 막는다.</summary>
        private void ApplyReadableSizes()
        {
            TextFit193.Fit(root.Find("Title")?.GetComponent<Text>(), 26, 18);
            TextFit193.Fit(walletText, 16);
            foreach (var b in subTabButtons) TextFit193.FitButton(b, 18);
            TextFit193.Fit(descText, 17);
            TextFit193.FitButton(positionButton, 19);
            TextFit193.FitButton(gradeButton, 19);
            for (int i = 0; i < ProductRows; i++)
            {
                TextFit193.Mask(productBoxes[i]);
                TextFit193.Fit(productTitles[i], 19);
                TextFit193.Fit(productDescs[i], 16);
                TextFit193.FitButton(buyButtons[i], 18);
            }
            TextFit193.Fit(listTitleText, 18);
            TextFit193.FitButton(clearButton, 18);
            TextFit193.Fit(guideText, 17);
            TextFit193.Fit(resultText, 18);
            TextFit193.FitButton(executeButton, 26, 18);
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
            grid.cellSize = new Vector2(340f, 86f);
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

        private ShopProduct[] CurrentProducts() =>
            subTab == ShopSubTab.PointShop ? ShopExchangeRules.PointProducts : subTab == ShopSubTab.CoinExchange ? ShopExchangeRules.CoinProducts : new ShopProduct[0];

        public void Refresh()
        {
            if (root == null || kit == null) return;
            var gm = GameManager.Instance;
            for (int i = 0; i < subTabButtons.Length; i++)
            {
                bool on = (int)subTab == i;
                if (subTabButtons[i].targetGraphic is Image image) image.color = on ? TabOn : TabOff;
                var label = subTabButtons[i].transform.Find("Text")?.GetComponent<Text>();
                if (label != null) label.color = on ? Dark : White;
            }

            walletText.text = gm == null ? "" :
                $"포인트 <color=#FFD54A>{gm.GameGold:N0}</color> · 성장 코인 <color=#5EE08A>{gm.GrowthCoin:N0}</color> · 트로피 {gm.Trophy} · " +
                $"각성 보조권 {gm.AwakenTicket} · 초월 대체권 {gm.TranscendTicket} · 특훈권 {gm.TrainingTicket} · 스킬 변경권 {gm.SkillChangeTicket} · 고급 {gm.PremiumSkillChangeTicket}";

            bool products = subTab == ShopSubTab.PointShop || subTab == ShopSubTab.CoinExchange;
            bool list = subTab == ShopSubTab.Release || subTab == ShopSubTab.Recombine;
            productGroup.gameObject.SetActive(products);
            listGroup.gameObject.SetActive(list);
            guideText.gameObject.SetActive(subTab == ShopSubTab.Guide);
            executeButton.gameObject.SetActive(list);
            positionButton.gameObject.SetActive(subTab != ShopSubTab.Release && subTab != ShopSubTab.Guide);
            gradeButton.gameObject.SetActive(subTab == ShopSubTab.CoinExchange || subTab == ShopSubTab.Recombine);
            CompyaUiKit.SetButtonText(positionButton, $"{(subTab == ShopSubTab.Recombine ? "목표 포지션" : "지정 포지션")}: {PositionKey} ▸");
            CompyaUiKit.SetButtonText(gradeButton, subTab == ShopSubTab.Recombine
                ? $"재료 시즌 등급: {CardGrowthRules.DisplayName(recombineGrade)} ▸"
                : $"스페셜팩 등급: {CardGrowthRules.DisplayName(SpecialGrade)} ▸");

            switch (subTab)
            {
                case ShopSubTab.PointShop:
                    descText.text = "포인트 상시 상품 - 포지션 지정 LIVE 영입 · 특훈권 · 강화 EXP 재료 · 스킬 변경권";
                    break;
                case ShopSubTab.CoinExchange:
                    descText.text = "성장 코인(방출 마일리지) 교환소 - 각성 · 초월 · 특별 영입 재료 · 스킬 변경권 · 고급 스킬 변경권(트로피 교환 포함)";
                    break;
                case ShopSubTab.Release:
                    descText.text = "라인업에 없는 카드를 방출해 포인트 + 성장 코인을 얻습니다 (등급 · 강화 단계 비례).";
                    break;
                case ShopSubTab.Recombine:
                    descText.text = $"같은 시즌 등급 카드 {ShopExchangeRules.RecombineCount}장 → 그 등급의 목표 포지션 카드 1장 확정(선택 구단 우선) - 각성 +1각 · 초월 보조 재료용";
                    break;
                default:
                    descText.text = "재료 획득처 - 부족한 재료를 어디서 얻는지 확인하십시오.";
                    break;
            }

            if (products) RefreshProducts(gm);
            if (list) RefreshList(gm);
            if (subTab == ShopSubTab.Guide)
            {
                var tier = LeagueManager.Instance != null ? LeagueManager.Instance.CurrentTier : LeagueTier.Amateur;
                guideText.text = LeagueMaterialRewards.SourceGuide(tier);
            }
        }

        private void RefreshProducts(GameManager gm)
        {
            var items = CurrentProducts();
            for (int i = 0; i < ProductRows; i++)
            {
                bool used = i < items.Length;
                productBoxes[i].gameObject.SetActive(used);
                productTitles[i].gameObject.SetActive(used);
                productDescs[i].gameObject.SetActive(used);
                buyButtons[i].gameObject.SetActive(used);
                if (!used) continue;
                var p = items[i];
                var grade = p == ShopProduct.SpecialPositionPack ? SpecialGrade : Grade.ALLSTAR;
                string target = p == ShopProduct.LivePositionPack ? $" [{PositionKey}]"
                    : p == ShopProduct.SpecialPositionPack ? $" [{CardGrowthRules.DisplayName(grade)} · {PositionKey}]" : "";
                productTitles[i].text = ShopExchangeRules.Name(p) + target;
                productDescs[i].text = ShopExchangeRules.Description(p);
                int owned = gm == null ? 0 : ShopExchangeRules.Owned(new GameManagerGrowthLedger(gm), ShopExchangeRules.CurrencyOf(p));
                bool affordable = owned >= ShopExchangeRules.Price(p, grade);
                CompyaUiKit.SetButtonText(buyButtons[i], ShopExchangeRules.PriceLabel(p, grade));
                buyButtons[i].interactable = gm != null && affordable;
            }
        }

        private void OnBuy(int index)
        {
            var items = CurrentProducts();
            if (index < items.Length) Buy(items[index]);
        }

        private List<Player> ListCandidates(GameManager gm)
        {
            if (gm == null) return new List<Player>();
            var pool = ShopExchangeRules.ReleaseCandidates(gm.Inventory, gm.Roster);
            return subTab == ShopSubTab.Recombine ? pool.Where(p => p.Template.Grade == recombineGrade).ToList() : pool;
        }

        private void RefreshList(GameManager gm)
        {
            var candidates = ListCandidates(gm);
            selected.RemoveAll(p => !candidates.Contains(p));
            if (subTab == ShopSubTab.Release)
            {
                int points = selected.Sum(p => ShopExchangeRules.ReleaseReward(p).Points);
                int coins = selected.Sum(p => ShopExchangeRules.ReleaseReward(p).Coins);
                listTitleText.text = $"방출할 선수 선택 {selected.Count}명 → 포인트 +{points:N0} · 성장 코인 +{coins:N0} (라인업 제외 {candidates.Count}명)";
                CompyaUiKit.SetButtonText(executeButton, selected.Count > 0 ? $"선택 선수 {selected.Count}명 방출" : "선수 방출");
                executeButton.interactable = selected.Count > 0;
            }
            else
            {
                listTitleText.text = $"{CardGrowthRules.DisplayName(recombineGrade)} 재료 {selected.Count}/{ShopExchangeRules.RecombineCount} 선택 → {PositionKey} 1장 (보유 {candidates.Count}장)";
                CompyaUiKit.SetButtonText(executeButton, $"3:1 재조합 실행 ({PositionKey})");
                executeButton.interactable = gm != null && ShopExchangeRules.ValidateRecombine(selected, new GameManagerGrowthLedger(gm)) == null;
            }
            clearButton.interactable = selected.Count > 0;

            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                var child = listContent.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            foreach (var p in candidates.Take(150))
            {
                var captured = p;
                bool on = selected.Contains(p);
                var cell = CompyaUiKit.Place(listContent, "Card", 0, 0, 10, 10);
                var image = CompyaUiKit.Paint(cell, on ? RowOn : RowOff, true);
                var button = cell.gameObject.AddComponent<Button>();
                button.targetGraphic = image;
                button.onClick.AddListener(() => Toggle(captured));
                var color = on ? Dark : White;
                string extra = subTab == ShopSubTab.Release
                    ? $"+{ShopExchangeRules.ReleaseReward(p).Points:N0}P · 코인 +{ShopExchangeRules.ReleaseReward(p).Coins}"
                    : $"{CompyaUiKit.ShortName(p.Template.Team)} · OVR {p.CalculateNeutralOVR()}";
                var l1 = kit.LabelOn(CompyaUiKit.Norm(cell, "Line1", 0.03f, 0.5f, 0.97f, 0.97f), $"{(on ? "✔ " : "")}{CardDisplay.TargetLine1(p)}", 22, TextAnchor.MiddleCenter, color, true);
                var l2 = kit.LabelOn(CompyaUiKit.Norm(cell, "Line2", 0.03f, 0.04f, 0.97f, 0.5f), $"{CardGrowthRules.DisplayName(p.Template.Grade)} {p.ReinforceLevel}강 · {extra}", 17, TextAnchor.MiddleCenter, color, false);
                TextFit193.Mask(cell);
                TextFit193.Fit(l1, 16, 12); // [TASK-KBO-193] 22 → 16pt
                TextFit193.Fit(l2, 14, 12); // [TASK-KBO-193] 17 → 14pt
            }
        }

        private void Toggle(Player p)
        {
            if (!selected.Remove(p))
            {
                if (subTab == ShopSubTab.Recombine && selected.Count >= ShopExchangeRules.RecombineCount)
                {
                    SetResult($"재조합 재료는 {ShopExchangeRules.RecombineCount}장까지 선택합니다.", false);
                    return;
                }
                selected.Add(p);
            }
            Refresh();
        }

        /// <summary>재조합 기본 등급 - 라인업 외 카드가 가장 많은 등급.</summary>
        private Grade BestRecombineGrade()
        {
            var gm = GameManager.Instance;
            if (gm == null) return recombineGrade;
            var best = ShopExchangeRules.ReleaseCandidates(gm.Inventory, gm.Roster).GroupBy(p => p.Template.Grade)
                .OrderByDescending(g => g.Count()).FirstOrDefault();
            return best != null ? best.Key : recombineGrade;
        }

        private static (IEnumerable<PlayerTemplate> Templates, System.Func<PlayerTemplate, Player> Issue) CardSource()
        {
            var scout = ScoutManager.Instance != null ? ScoutManager.Instance : FindAnyObjectByType<ScoutManager>();
            var db = scout != null && scout.Database != null ? scout.Database : FindAnyObjectByType<PlayerDatabase>();
            return (db != null ? db.AllTemplates : null,
                t => scout != null ? scout.IssueCard(t) : new Player(System.Guid.NewGuid().ToString(), t));
        }

        private void SetResult(string message, bool ok)
        {
            if (resultText == null) return;
            resultText.color = ok ? Green : Red;
            resultText.text = message ?? "";
        }
    }
}
