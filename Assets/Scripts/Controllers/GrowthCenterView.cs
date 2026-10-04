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
    /// [TASK-KBO-184] 하단 5탭 [선수 관리] = 성장 전용 허브(성장 센터). 단순 보유 리스트(→ 라인업 [보관 선수] 탭으로 이동)가 아니라
    /// 선수 성장을 직접 수행하는 화면이다.
    ///   상단: 대상 선수 카드 + 이름/등급/포지션 + OVR(현재 → 성장 후 미리보기) + 성장 구성(기본/강화/각성/한계 돌파/특훈/시너지/최대 잠재)
    ///         + 각성 사다리(명함 ~ 9각/초월, 3·6·9각 임계점 강조).
    ///   중단: 4개 성장 탭 [강화] [각성] [한계 돌파] [훈련·특훈] + 탭 설명 + 세부 스탯 변화 미리보기 + 재료 카드 선택 목록([자동 선택]/[선택 해제]).
    ///   하단: [실행] 버튼 + 결과 문구, [대상 선수 변경] 목록(라인업/전체 보유 전환) - 보유한 어떤 선수든 바로 골라 성장시킨다.
    /// CompyaMatchView와 같이 Setup(에디터)과 런타임 Awake가 같은 Build() 코드로 계층을 만든다(PlayerManagementHubPanel 최상단을 덮는다).
    /// 성장 규칙은 GrowthCenterRules / CardGrowthRules / UpgradeManager.ApplyEnhance·ApplyAwaken / CardGrowthActions(단위 테스트 대상)를 그대로 쓴다.
    /// </summary>
    public class GrowthCenterView : MonoBehaviour
    {
        public const string RootName = "GrowthCenter184";

        [SerializeField] private Font boldFont;
        [SerializeField] private Font regularFont;
        [SerializeField] private PlayerCardUI cardPrefab;

        private static readonly Color Bg = new Color(0.06f, 0.08f, 0.15f);
        private static readonly Color Panel = new Color(0.11f, 0.14f, 0.24f);
        private static readonly Color PanelLight = new Color(0.16f, 0.2f, 0.33f);
        private static readonly Color White = new Color(0.95f, 0.96f, 0.98f);
        private static readonly Color Muted = new Color(0.66f, 0.72f, 0.84f);
        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);
        private static readonly Color Green = new Color(0.37f, 0.88f, 0.54f);
        private static readonly Color TabOn = new Color(0.98f, 0.76f, 0.2f);
        private static readonly Color TabOff = new Color(0.2f, 0.25f, 0.4f);
        private static readonly Color RowOn = new Color(0.95f, 0.72f, 0.18f, 0.95f);
        private static readonly Color RowOff = new Color(0.2f, 0.24f, 0.38f, 0.95f);
        private static readonly Color RowLineup = new Color(0.17f, 0.32f, 0.62f, 0.95f);

        private CompyaUiKit kit;
        private RectTransform root;
        private CardHolderFit cardHolder;
        private PlayerCardUI spawnedCard;
        private Text nameText, subText, ovrText, breakdownText, ladderNoteText, descText, statText, materialTitleText, resultText, targetTitleText;
        private readonly Image[] ladderBoxes = new Image[11];
        private readonly Text[] ladderLabels = new Text[11];
        private readonly Button[] tabButtons = new Button[4];
        private Button actionButton, autoButton, clearButton, scopeButton;
        private RectTransform materialContent, targetContent;

        private Player target;
        private GrowthTab tab = GrowthTab.Awaken;
        private readonly List<Player> selected = new List<Player>();
        private bool lineupOnly = true;
        private ScreenType returnScreen = ScreenType.Lobby;

        public Player Target => target;
        public GrowthTab CurrentTab => tab;

        public void Configure(Font bold, Font regular, PlayerCardUI prefab)
        {
            boldFont = bold;
            regularFont = regular;
            cardPrefab = prefab;
        }

        private void Awake()
        {
            if (cardPrefab == null)
            {
                var roster = FindAnyObjectByType<RosterUIController>(FindObjectsInactive.Include);
                if (roster != null) cardPrefab = roster.CardPrefab;
            }
            Build();
        }

        private void OnEnable()
        {
            if (root != null) Refresh();
        }

        // ================================================================== 공개 진입점

        /// <summary>성장 센터를 연다. player가 null이면 대표 선수(라인업 최고 OVR)를 기본 대상으로 한다.</summary>
        public void Open(Player player, ScreenType returnTo)
        {
            if (root == null) Build();
            // 허브 안에서 다시 열린 경우(상세 정보 → [선수 관리], 강화 화면 복귀)는 처음 들어온 화면을 유지한다.
            if (returnTo != ScreenType.PlayerManagementHub && returnTo != ScreenType.Enhance && returnTo != ScreenType.Onboarding) returnScreen = returnTo;
            SetTarget(player);
            if (resultText != null) resultText.text = "";
            transform.SetAsLastSibling();
            root.SetAsLastSibling();
        }

        public void SetTarget(Player player)
        {
            var gm = GameManager.Instance;
            target = player?.Template != null ? player : GrowthCenterRules.DefaultTarget(gm?.Roster, gm?.Inventory);
            selected.Clear();
            Refresh();
        }

        public void SelectTab(GrowthTab next)
        {
            tab = next;
            selected.Clear();
            if (resultText != null) resultText.text = "";
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
            spawnedCard = null;

            kit = new CompyaUiKit(boldFont, regularFont);
            root = CompyaUiKit.Fill(transform, RootName);
            CompyaUiKit.Paint(root, Bg, true); // 아래 구 허브(메뉴 그리드)를 가리고 클릭을 막는다

            // ---- 헤더
            kit.GradientBox(root, "Header", 0, 0, 1248, 140, new Color(0.13f, 0.3f, 0.66f), new Color(0.07f, 0.16f, 0.4f), false);
            kit.Button(root, "BackButton", "◀ 뒤로", 20, 30, 230, 112, new Color(0f, 0f, 0f, 0.25f), White, 34).onClick.AddListener(Close);
            kit.Label(root, "Title", "선수 관리 · 성장 센터", 240, 20, 1000, 120, 46, TextAnchor.MiddleCenter, White, true);
            kit.Button(root, "DetailButton", "상세 정보", 1010, 30, 1228, 112, new Color(1f, 1f, 1f, 0.15f), White, 32).onClick.AddListener(OpenDetail);

            // ---- 대상 선수
            CompyaUiKit.Box(root, "TargetPanel", 20, 155, 1228, 800, Panel);
            var holderRect = CompyaUiKit.Place(root, "CardHolder", 36, 170, 420, 785);
            cardHolder = holderRect.gameObject.AddComponent<CardHolderFit>();
            cardHolder.Configure(cardPrefab != null ? CardHolderFit.NativeSizeOf(cardPrefab) : CardHolderFit.DefaultCardSize, 1f);
            nameText = kit.Label(root, "Name", "", 440, 170, 1210, 240, 46, TextAnchor.MiddleLeft, White, true);
            subText = kit.Label(root, "Sub", "", 440, 242, 1210, 292, 28, TextAnchor.MiddleLeft, Muted);
            ovrText = kit.Label(root, "Ovr", "", 440, 296, 1210, 400, 64, TextAnchor.MiddleLeft, Gold, true);
            breakdownText = kit.Label(root, "Breakdown", "", 440, 404, 1210, 600, 27, TextAnchor.UpperLeft, White);
            kit.Label(root, "LadderTitle", "각성 단계 (3각 · 6각 · 9각 = 임계점)", 440, 604, 1210, 640, 24, TextAnchor.MiddleLeft, Muted, true);
            const float lx0 = 440f, lw = 69f;
            for (int i = 0; i <= 10; i++)
            {
                float x0 = lx0 + i * lw;
                ladderBoxes[i] = CompyaUiKit.Box(root, $"Ladder{i}", x0 + 2, 645, x0 + lw - 2, 715, PanelLight);
                ladderLabels[i] = kit.LabelOn(CompyaUiKit.Fill(ladderBoxes[i].transform, "Text"), i == 0 ? "명함" : i == 10 ? "초월" : $"{i}각", 22, TextAnchor.MiddleCenter, White, true);
            }
            ladderNoteText = kit.Label(root, "LadderNote", "", 440, 720, 1210, 790, 24, TextAnchor.MiddleLeft, Gold);

            // ---- 성장 탭 4개
            for (int i = 0; i < GrowthCenterRules.Tabs.Length; i++)
            {
                var t = GrowthCenterRules.Tabs[i];
                float x0 = 20 + i * 302f;
                tabButtons[i] = kit.Button(root, $"Tab_{t}", GrowthCenterRules.TabName(t), x0, 815, x0 + 298, 905, TabOff, White, 38);
                tabButtons[i].onClick.AddListener(() => SelectTab(t));
            }
            descText = kit.Label(root, "Desc", "", 28, 915, 1220, 1020, 28, TextAnchor.MiddleLeft, White);

            // ---- 스탯 변화 미리보기 / 재료 선택
            CompyaUiKit.Box(root, "StatPanel", 20, 1028, 600, 1420, Panel);
            kit.Label(root, "StatTitle", "세부 스탯 변화 미리보기", 40, 1035, 590, 1085, 28, TextAnchor.MiddleLeft, Gold, true);
            statText = kit.Label(root, "Stats", "", 40, 1090, 590, 1410, 32, TextAnchor.UpperLeft, White);

            CompyaUiKit.Box(root, "MaterialPanel", 612, 1028, 1228, 1420, Panel);
            materialTitleText = kit.Label(root, "MaterialTitle", "재료 카드 선택", 628, 1035, 1220, 1085, 28, TextAnchor.MiddleLeft, Gold, true);
            materialContent = ScrollList(root, "MaterialScroll", 624, 1090, 1216, 1350);
            autoButton = kit.Button(root, "AutoSelect", "자동 선택", 624, 1358, 916, 1412, new Color(0.15f, 0.33f, 0.88f), White, 28);
            autoButton.onClick.AddListener(AutoSelect);
            clearButton = kit.Button(root, "ClearSelect", "선택 해제", 924, 1358, 1216, 1412, PanelLight, White, 28);
            clearButton.onClick.AddListener(() => { selected.Clear(); Refresh(); });

            // ---- 실행
            resultText = kit.Label(root, "Result", "", 28, 1428, 1220, 1490, 28, TextAnchor.MiddleCenter, Green, true);
            actionButton = kit.GradientButton(root, "Action", "실행", 20, 1495, 1228, 1600, new Color(1f, 0.8f, 0.25f), new Color(0.93f, 0.55f, 0.1f), new Color(0.12f, 0.08f, 0.02f), 46);
            actionButton.onClick.AddListener(Execute);

            // ---- 대상 선수 변경
            targetTitleText = kit.Label(root, "TargetTitle", "대상 선수 변경", 28, 1615, 860, 1675, 32, TextAnchor.MiddleLeft, White, true);
            scopeButton = kit.Button(root, "TargetScope", "라인업", 870, 1615, 1228, 1675, PanelLight, White, 28);
            scopeButton.onClick.AddListener(() => { lineupOnly = !lineupOnly; Refresh(); });
            targetContent = ScrollList(root, "TargetScroll", 20, 1682, 1228, 1966);

            if (Application.isPlaying) Refresh();
        }

        /// <summary>세로 스크롤 목록(행 = HorizontalLayoutGroup 3칸) - 픽셀 크기를 몰라도 되게 레이아웃 그룹으로만 배치한다.</summary>
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
            layout.spacing = 6f;
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = content;
            scroll.viewport = viewport;
            return content;
        }

        // ================================================================== 갱신

        public void Refresh()
        {
            if (root == null || kit == null) return;
            var gm = GameManager.Instance;
            if (target == null && gm != null) target = GrowthCenterRules.DefaultTarget(gm.Roster, gm.Inventory);
            if (gm != null && target != null && !gm.Inventory.Contains(target) && !gm.Roster.Contains(target))
                target = GrowthCenterRules.DefaultTarget(gm.Roster, gm.Inventory); // 재료로 소모된 카드면 다시 고른다

            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null) continue;
                bool on = GrowthCenterRules.Tabs[i] == tab;
                if (tabButtons[i].targetGraphic is Image image) image.color = on ? TabOn : TabOff;
                var label = tabButtons[i].transform.Find("Text")?.GetComponent<Text>();
                if (label != null) label.color = on ? new Color(0.12f, 0.08f, 0.02f) : White;
            }

            int gold = gm != null ? gm.GameGold : 0;
            int synergy = gm != null ? gm.CurrentTeamSynergyOvr : 0;
            var candidates = gm != null ? GrowthCenterRules.MaterialCandidates(tab, target, gm.Inventory, gm.Roster) : new List<Player>();
            selected.RemoveAll(p => !candidates.Contains(p));
            var after = GrowthCenterRules.Simulate(tab, target, selected, gold);

            RefreshTargetCard();
            if (target?.Template == null)
            {
                nameText.text = "보유 선수가 없습니다";
                subText.text = ovrText.text = breakdownText.text = ladderNoteText.text = statText.text = "";
                descText.text = GrowthCenterRules.Describe(tab, null, gold);
                RebuildRows(materialContent, new List<(string, Color, UnityEngine.Events.UnityAction)>());
                RebuildTargets(gm);
                actionButton.interactable = false;
                return;
            }

            var tpl = target.Template;
            bool inLineup = gm != null && gm.Roster.Contains(target);
            nameText.text = CompyaMatchView.DisplayName(target);
            subText.text = $"{CompyaUiKit.ShortName(tpl.Team)} · {CompyaMatchView.PositionLabel(target)} · {CardGrowthRules.DisplayName(tpl.Grade)} · " +
                           $"{target.ReinforceLevel}강 {target.AwakenLabel}" + (inLineup ? " · <color=#5ED6F2>라인업</color>" : " · 보관");
            int before = TeamSynergyRules.PlayerFinalOvr(target, synergy);
            int afterOvr = TeamSynergyRules.PlayerFinalOvr(after, synergy);
            ovrText.text = afterOvr != before ? $"OVR {before} → <color=#5EE08A>{afterOvr} (+{afterOvr - before})</color>" : $"OVR {before}";
            breakdownText.text =
                $"기본 {target.BaseOvr}  +  성장 {target.GetStatGrowth()}/{target.MaxGrowth}  +  시너지 {synergy}\n" +
                $"강화 +{target.ReinforceGrowth}/{CardGrowthRules.MaxReinforceGrowth}   각성 +{target.AwakenGrowth}/{CardGrowthRules.AwakenGrowthCap(tpl.Grade)}\n" +
                $"한계 돌파 +{target.LimitBreakGrowth}/{CardGrowthRules.LimitBreakCap(tpl.Grade)}   특훈 +{target.TrainingGrowth}/{CardGrowthRules.TrainingCap(tpl.Grade)}\n" +
                $"최대 잠재 OVR {target.MaxPotentialOvr}  ·  세트덱 SD {target.SetDeckScore}";

            int maxLevel = target.MaxAwakenLevelForGrade;
            for (int i = 0; i <= 10; i++)
            {
                bool available = i <= maxLevel;
                bool reached = i <= target.AwakenLevel;
                bool preview = !reached && i <= after.AwakenLevel;
                bool threshold = i == CardGrowthRules.FirstAwakenThreshold || i == CardGrowthRules.SecondAwakenThreshold
                    || i == CardGrowthRules.FinalAwakenThreshold || i == CardGrowthRules.TranscendLevel;
                ladderBoxes[i].color = !available ? new Color(0.1f, 0.1f, 0.12f, 0.6f)
                    : reached ? (threshold ? Gold : new Color(0.3f, 0.55f, 0.95f))
                    : preview ? Green : threshold ? new Color(0.45f, 0.36f, 0.12f) : PanelLight;
                ladderLabels[i].text = !available ? "-" : i == 0 ? "명함" : i == 10 ? "초월" : $"{i}각";
                ladderLabels[i].color = reached && threshold ? new Color(0.12f, 0.08f, 0.02f) : White;
            }
            ladderNoteText.text = $"+{target.AwakenGrowth} OVR · " + CardGrowthRules.AwakenStageNote(tpl.Grade, target.AwakenLevel);

            descText.text = GrowthCenterRules.Describe(tab, target, gold);
            if (tab == GrowthTab.Awaken && selected.Count > 0)
            {
                string preview = GrowthCenterRules.AwakenPreview(target, selected); // [TASK-KBO-185] "0각 → 3각 · OVR +5 점프"
                if (preview.Length > 0) descText.text = $"<color=#5EE08A>각성 미리보기: {preview}</color>\n" + descText.text.Split('\n')[0];
            }
            statText.text = string.Join("\n", GrowthCenterRules.StatPreviewLines(target, after));

            int maxMaterials = GrowthCenterRules.MaxMaterials(tab);
            materialTitleText.text = tab == GrowthTab.Training
                ? $"특훈 비용 - 볼 {CardGrowthActions.TrainingGoldCost(target.TrainingLevel):N0}"
                : $"재료 카드 선택 {selected.Count}/{maxMaterials} (후보 {candidates.Count}장)";
            var rows = new List<(string, Color, UnityEngine.Events.UnityAction)>();
            if (tab != GrowthTab.Training)
            {
                foreach (var m in candidates.Take(90))
                {
                    var captured = m;
                    bool on = selected.Contains(m);
                    // [TASK-KBO-185] 각성 재료 배지 - [동일 연도 +3각] / [다른 연도 +1각]
                    string badge = tab == GrowthTab.Awaken ? CardGrowthRules.AwakenMaterialBadge(target, m) : "";
                    if (badge.Length > 0)
                        badge = $"<color={(CardGrowthRules.AwakenGainFor(target, m) == CardGrowthRules.SameYearAwakenGain ? "#FFD045" : "#7FD1FF")}>{badge}</color> ";
                    rows.Add(($"{(on ? "✔ " : "")}{CompyaMatchView.DisplayName(m)}\n<size=80%>{badge}{CardGrowthRules.DisplayName(m.Template.Grade)} · OVR {m.CalculateNeutralOVR()}</size>",
                        on ? RowOn : RowOff, () => ToggleMaterial(captured)));
                }
            }
            RebuildRows(materialContent, rows, columns: 2);
            autoButton.interactable = tab != GrowthTab.Training && candidates.Count > 0;
            clearButton.interactable = selected.Count > 0;

            CompyaUiKit.SetButtonText(actionButton, $"{GrowthCenterRules.TabName(tab)} 실행");
            actionButton.interactable = CanExecute(gold, candidates);
            RebuildTargets(gm);
        }

        private bool CanExecute(int gold, IReadOnlyCollection<Player> candidates)
        {
            if (target?.Template == null) return false;
            switch (tab)
            {
                case GrowthTab.Enhance: return target.ReinforceLevel < Player.MaxReinforceLevel && selected.Count > 0;
                case GrowthTab.Awaken: return target.AwakenLevel < target.MaxAwakenLevelForGrade && selected.Count > 0;
                case GrowthTab.LimitBreak: return CardGrowthActions.CanLimitBreak(target, out _) && (selected.Count > 0 || candidates.Count > 0);
                case GrowthTab.Training: return CardGrowthActions.CanTrain(target, gold, out _);
                default: return false;
            }
        }

        private void RebuildTargets(GameManager gm)
        {
            CompyaUiKit.SetButtonText(scopeButton, lineupOnly ? "라인업 ▸ 전체 보유" : "전체 보유 ▸ 라인업");
            var list = gm != null ? GrowthCenterRules.TargetCandidates(gm.Inventory, gm.Roster, lineupOnly) : new List<Player>();
            targetTitleText.text = $"대상 선수 변경 ({(lineupOnly ? "라인업" : "전체 보유")} {list.Count}명)";
            var inLineup = gm != null ? new HashSet<Player>(gm.Roster) : new HashSet<Player>();
            var rows = new List<(string, Color, UnityEngine.Events.UnityAction)>();
            foreach (var p in list.Take(150))
            {
                var captured = p;
                bool current = p == target;
                rows.Add(($"{CompyaMatchView.DisplayName(p)}\n<size=80%>{CompyaMatchView.PositionLabel(p)} · {CardGrowthRules.DisplayName(p.Template.Grade)} · OVR {p.CalculateNeutralOVR()}</size>",
                    current ? RowOn : inLineup.Contains(p) ? RowLineup : RowOff, () => { SetTarget(captured); if (resultText != null) resultText.text = ""; }));
            }
            RebuildRows(targetContent, rows, columns: 3);
        }

        private void RebuildRows(RectTransform content, List<(string text, Color color, UnityEngine.Events.UnityAction onClick)> items, int columns = 2)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i).gameObject;
                child.SetActive(false);
                if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
            }
            for (int start = 0; start < items.Count; start += columns)
            {
                var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.transform.SetParent(content, false);
                var h = row.GetComponent<HorizontalLayoutGroup>();
                h.spacing = 6f;
                h.childControlWidth = h.childControlHeight = true;
                h.childForceExpandWidth = h.childForceExpandHeight = true;
                row.GetComponent<LayoutElement>().preferredHeight = 84f;
                for (int c = 0; c < columns; c++)
                {
                    int index = start + c;
                    var cell = new GameObject(index < items.Count ? "Item" : "Filler", typeof(RectTransform), typeof(LayoutElement));
                    cell.transform.SetParent(row.transform, false);
                    cell.GetComponent<LayoutElement>().flexibleWidth = 1f;
                    if (index >= items.Count) continue;
                    var item = items[index];
                    var image = CompyaUiKit.Paint((RectTransform)cell.transform, item.color, true);
                    var button = cell.AddComponent<Button>();
                    button.targetGraphic = image;
                    button.onClick.AddListener(item.onClick);
                    var label = kit.LabelOn(CompyaUiKit.Norm(cell.transform, "Text", 0.04f, 0.04f, 0.96f, 0.96f), item.text, 26, TextAnchor.MiddleCenter,
                        item.color == RowOn ? new Color(0.12f, 0.08f, 0.02f) : White, true);
                    label.verticalOverflow = VerticalWrapMode.Truncate;
                }
            }
        }

        private void RefreshTargetCard()
        {
            if (spawnedCard != null)
            {
                if (CardPoolManager.Instance != null) CardPoolManager.Instance.Release(spawnedCard);
                else if (Application.isPlaying) Destroy(spawnedCard.gameObject); else DestroyImmediate(spawnedCard.gameObject);
                spawnedCard = null;
            }
            if (target?.Template == null || cardPrefab == null || cardHolder == null || !Application.isPlaying) return;
            spawnedCard = CardPoolManager.Instance != null ? CardPoolManager.Instance.Get(cardPrefab, cardHolder.transform) : Instantiate(cardPrefab, cardHolder.transform);
            spawnedCard.Setup(target);
            cardHolder.Place((RectTransform)spawnedCard.transform);
            foreach (var graphic in spawnedCard.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }

        // ================================================================== 동작

        private void ToggleMaterial(Player material)
        {
            if (selected.Remove(material)) { Refresh(); return; }
            int max = GrowthCenterRules.MaxMaterials(tab);
            if (max == 1) selected.Clear();
            if (selected.Count >= max)
            {
                resultText.text = $"재료는 최대 {max}장까지 선택할 수 있습니다.";
                return;
            }
            selected.Add(material);
            Refresh();
        }

        private void AutoSelect()
        {
            var gm = GameManager.Instance;
            if (gm == null || target == null) return;
            var candidates = GrowthCenterRules.MaterialCandidates(tab, target, gm.Inventory, gm.Roster);
            selected.Clear();
            selected.AddRange(GrowthCenterRules.AutoSelect(tab, target, candidates));
            if (selected.Count == 0) resultText.text = "사용할 수 있는 재료가 없습니다.";
            Refresh();
        }

        /// <summary>선택한 탭의 성장을 실제로 적용한다(재료 소모 → 세이브).</summary>
        public void Execute()
        {
            var gm = GameManager.Instance;
            if (gm == null || target?.Template == null) return;
            int synergy = gm.CurrentTeamSynergyOvr;
            int before = TeamSynergyRules.PlayerFinalOvr(target, synergy);
            string beforeLabel = $"{target.ReinforceLevel}강 {target.AwakenLabel}";
            var used = new List<Player>();
            string message;
            bool ok;
            switch (tab)
            {
                case GrowthTab.Enhance:
                    ok = selected.Count > 0 && UpgradeManager.ApplyEnhance(target, new List<Player>(selected));
                    if (ok) used.AddRange(selected);
                    message = ok ? $"강화 완료 - {target.ReinforceLevel}강 (EXP {target.ReinforceExp})" : "강화할 수 없습니다(재료 선택 또는 10강 확인).";
                    break;
                case GrowthTab.Awaken:
                    ok = selected.Count > 0 && UpgradeManager.ApplyAwaken(target, new List<Player>(selected));
                    if (ok) used.AddRange(selected);
                    message = ok ? $"각성 완료 - {target.AwakenLabel} · {CardGrowthRules.AwakenStageNote(target.Template.Grade, target.AwakenLevel)}" : "각성할 수 없습니다(동일 선수 재료 또는 최대 단계 확인).";
                    break;
                case GrowthTab.LimitBreak:
                {
                    var material = selected.FirstOrDefault() ?? CardGrowthActions.PickLimitBreakMaterial(target, gm.Inventory, gm.Roster);
                    ok = CardGrowthActions.TryLimitBreak(target, material, out message);
                    if (ok) used.Add(material);
                    break;
                }
                case GrowthTab.Training:
                    ok = CardGrowthActions.TryTrain(target, gm.GameGold, out int spent, out message);
                    if (ok) gm.GameGold -= spent;
                    break;
                default:
                    ok = false;
                    message = "";
                    break;
            }

            foreach (var m in used) gm.RemovePlayerFromInventory(m);
            selected.Clear();
            if (ok)
            {
                SaveManager.Instance?.TrySaveCareer();
                int after = TeamSynergyRules.PlayerFinalOvr(target, gm.CurrentTeamSynergyOvr);
                message += after != before ? $"  OVR {before} → {after}" : $"  ({beforeLabel} → {target.ReinforceLevel}강 {target.AwakenLabel})";
            }
            resultText.color = ok ? Green : new Color(1f, 0.45f, 0.45f);
            resultText.text = message;
            Refresh();
        }

        private void OpenDetail()
        {
            if (target == null) return;
            var detail = FindAnyObjectByType<PlayerDetailUIController>(FindObjectsInactive.Include);
            if (detail != null) detail.Show(target);
        }

        private void Close()
        {
            selected.Clear();
            UIManager.Instance?.ShowScreen(returnScreen);
        }
    }
}
