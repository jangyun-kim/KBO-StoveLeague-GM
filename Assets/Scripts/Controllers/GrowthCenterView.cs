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

        // [TASK-KBO-191] 성장 센터 글씨 계층(Normal) - 화면 타이틀 26 · 섹션 헤더 19 · 본문 16 · 보조 15 · 각성 사다리 14 · 헤더 버튼 17
        // [TASK-KBO-192] 1080×1920 가독성 표준으로 복원(Normal 유지) - 타이틀 28 · 섹션 24 · 탭 21 · 본문/스탯 20 · 보조 17 · 실행 버튼 28
        public const int TitlePt = 28, SectionPt = 24, ButtonPt = 20, BodyPt = 20, SmallPt = 17, LadderPt = 16, TabPt = 21, ActionPt = 28, StatPt = 20;
        public const int SkillHeaderPt = 19, SkillButtonPt = 17, SkillSlotTitlePt = 20, SkillSlotDescPt = 17;
        public const int TargetLine1Pt = 21, TargetLine2Pt = 17, MaterialLine1Pt = 20, MaterialLine2Pt = 17;

        private CompyaUiKit kit;
        private RectTransform root;
        private CardHolderFit cardHolder;
        private PlayerCardUI spawnedCard;
        private Text nameText, subText, ovrText, breakdownText, ladderNoteText, descText, statText, materialTitleText, resultText, targetTitleText;
        private readonly Image[] ladderBoxes = new Image[11];
        private readonly Text[] ladderLabels = new Text[11];
        private readonly Button[] tabButtons = new Button[5];
        private Button actionButton, autoButton, clearButton, scopeButton, contextButton, sourceButton;
        private bool useCoreTicket; // [TASK-KBO-189] 초월 슬롯 1을 초월 핵심 대체권으로
        // [TASK-KBO-190] 훈련·특훈 탭 = 3슬롯 스킬 섹션([스킬 변경] / [고급 스킬 변경] / [스킬 레벨업])
        private Button skillRerollButton, skillPremiumButton, skillLevelButton;
        private int skillSlotIndex;
        public int SkillSlotIndex => skillSlotIndex;
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
            useCoreTicket = false;
            Refresh();
        }

        public void SelectTab(GrowthTab next)
        {
            tab = next;
            selected.Clear();
            useCoreTicket = false;
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
            var back = kit.Button(root, "BackButton", "◀ 뒤로", 20, 30, 230, 112, new Color(0f, 0f, 0f, 0.25f), White, 34);
            back.onClick.AddListener(Close);
            TextTidy.ExactButton(back, ButtonPt);
            TextTidy.Exact(kit.Label(root, "Title", "선수 관리 · 성장 센터", 240, 20, 770, 120, 42, TextAnchor.MiddleCenter, White, true), TitlePt);
            // [TASK-KBO-189] 재료 획득처 / 포지션 재조합 바로가기(스카우트 허브 [상점 · 교환소])
            sourceButton = kit.Button(root, "SourceButton", "재료 획득처 ▸", 780, 30, 1000, 112, new Color(0.98f, 0.76f, 0.2f, 0.9f), new Color(0.12f, 0.08f, 0.02f), 28);
            sourceButton.onClick.AddListener(() => ShopExchangeView.OpenShop(ShopSubTab.Guide));
            TextTidy.ExactButton(sourceButton, ButtonPt);
            var detail = kit.Button(root, "DetailButton", "상세 정보", 1010, 30, 1228, 112, new Color(1f, 1f, 1f, 0.15f), White, 32);
            detail.onClick.AddListener(OpenDetail);
            TextTidy.ExactButton(detail, ButtonPt);

            // ---- 대상 선수
            CompyaUiKit.Box(root, "TargetPanel", 20, 155, 1228, 800, Panel);
            var holderRect = CompyaUiKit.Place(root, "CardHolder", 36, 170, 420, 785);
            cardHolder = holderRect.gameObject.AddComponent<CardHolderFit>();
            cardHolder.Configure(cardPrefab != null ? CardHolderFit.NativeSizeOf(cardPrefab) : CardHolderFit.DefaultCardSize, 1f);
            // [TASK-KBO-192] 계층별 적정 크기(Normal) - 선수명 26 · 메인 OVR 40 · 본문 19~20 · 보조 17
            nameText = TextTidy.Exact(kit.Label(root, "Name", "", 440, 170, 1210, 240, 46, TextAnchor.MiddleLeft, White, true), 26);
            subText = TextTidy.Exact(kit.Label(root, "Sub", "", 440, 242, 1210, 292, 28, TextAnchor.MiddleLeft, Muted), 19);
            ovrText = TextTidy.Exact(kit.Label(root, "Ovr", "", 440, 296, 1210, 400, 64, TextAnchor.MiddleLeft, Gold, true), 40);
            breakdownText = TextTidy.Exact(kit.Label(root, "Breakdown", "", 440, 404, 1210, 600, 27, TextAnchor.UpperLeft, White), BodyPt);
            breakdownText.lineSpacing = 1.1f;
            TextTidy.Exact(kit.Label(root, "LadderTitle", "각성 단계 (3각 · 6각 · 9각 = 임계점)", 440, 604, 1210, 640, 24, TextAnchor.MiddleLeft, Muted, true), SmallPt);
            const float lx0 = 440f, lw = 69f;
            for (int i = 0; i <= 10; i++)
            {
                float x0 = lx0 + i * lw;
                ladderBoxes[i] = CompyaUiKit.Box(root, $"Ladder{i}", x0 + 2, 645, x0 + lw - 2, 715, PanelLight);
                // [TASK-KBO-191] 칸(약 60px)에 비해 컸던 22pt Bold → 14pt Normal(명함 · 초월 뭉개짐 해소)
                ladderLabels[i] = TextTidy.Exact(kit.LabelOn(CompyaUiKit.Fill(ladderBoxes[i].transform, "Text"), i == 0 ? "명함" : i == 10 ? "초월" : $"{i}각", 22, TextAnchor.MiddleCenter, White), LadderPt);
            }
            ladderNoteText = TextTidy.Exact(kit.Label(root, "LadderNote", "", 440, 720, 1210, 790, 24, TextAnchor.MiddleLeft, Gold), SmallPt);

            // ---- 성장 탭 5개([TASK-KBO-189] 초월 추가)
            for (int i = 0; i < GrowthCenterRules.Tabs.Length; i++)
            {
                var t = GrowthCenterRules.Tabs[i];
                float x0 = 20 + i * 242f;
                tabButtons[i] = kit.Button(root, $"Tab_{t}", GrowthCenterRules.TabName(t), x0, 815, x0 + 238, 905, TabOff, White, 36);
                tabButtons[i].onClick.AddListener(() => SelectTab(t));
                TextTidy.ExactButton(tabButtons[i], TabPt);
            }
            descText = TextTidy.Exact(kit.Label(root, "Desc", "", 28, 915, 890, 1020, 26, TextAnchor.MiddleLeft, White), BodyPt);
            descText.verticalOverflow = VerticalWrapMode.Truncate;
            // [TASK-KBO-189] 상황 버튼 - [강화 탭으로 이동] / [각성 보조권 +1각] / [핵심 대체권 사용] / [각성 탭으로 이동]
            contextButton = kit.Button(root, "ContextButton", "", 900, 925, 1228, 1012, new Color(0.15f, 0.33f, 0.88f), White, 28);
            contextButton.onClick.AddListener(OnContextButton);
            TextTidy.ExactButton(contextButton, SmallPt);

            // ---- 스탯 변화 미리보기 / 재료 선택
            CompyaUiKit.Box(root, "StatPanel", 20, 1028, 600, 1420, Panel);
            TextTidy.Exact(kit.Label(root, "StatTitle", "세부 스탯 변화 미리보기", 40, 1035, 590, 1085, 28, TextAnchor.MiddleLeft, Gold, true), SectionPt);
            statText = TextTidy.Exact(kit.Label(root, "Stats", "", 40, 1090, 590, 1410, 32, TextAnchor.UpperLeft, White), StatPt);
            statText.lineSpacing = 1.1f;

            CompyaUiKit.Box(root, "MaterialPanel", 612, 1028, 1228, 1420, Panel);
            // [TASK-KBO-191] 헤더(1035~1085, 한 줄 고정 · 칸 밖으로 넘치지 않음)와 목록(1092~)을 분리하고, 하단 버튼 줄을 76px로 키워 두 줄 버튼이 들어가게 했다.
            materialTitleText = TextTidy.Exact(kit.Label(root, "MaterialTitle", "재료 카드 선택", 628, 1035, 1220, 1085, 28, TextAnchor.MiddleLeft, Gold, true), SectionPt);
            materialTitleText.verticalOverflow = VerticalWrapMode.Truncate;
            materialContent = ScrollList(root, "MaterialScroll", 624, 1092, 1216, 1330);
            autoButton = kit.Button(root, "AutoSelect", "자동 선택", 624, 1336, 916, 1412, new Color(0.15f, 0.33f, 0.88f), White, 28);
            autoButton.onClick.AddListener(AutoSelect);
            TextTidy.ExactButton(autoButton, ButtonPt);
            clearButton = kit.Button(root, "ClearSelect", "선택 해제", 924, 1336, 1216, 1412, PanelLight, White, 28);
            clearButton.onClick.AddListener(() => { selected.Clear(); useCoreTicket = false; Refresh(); });
            TextTidy.ExactButton(clearButton, ButtonPt);
            // [TASK-KBO-190] 훈련·특훈 탭 스킬 버튼(자동 선택/선택 해제 자리) - [TASK-KBO-191→192] 두 줄 문구 17pt Normal · 줄간격 1.1
            skillRerollButton = SkillButton("SkillReroll", "스킬 변경", 624, 818, new Color(0.15f, 0.33f, 0.88f));
            skillRerollButton.onClick.AddListener(() => RerollSkills(false));
            skillPremiumButton = SkillButton("SkillPremium", "고급 변경(A~S)", 822, 1016, new Color(0.55f, 0.2f, 0.62f));
            skillPremiumButton.onClick.AddListener(() => RerollSkills(true));
            skillLevelButton = SkillButton("SkillLevelUp", "스킬 레벨업", 1020, 1216, new Color(0.12f, 0.55f, 0.35f));
            skillLevelButton.onClick.AddListener(() => LevelUpSkill());

            // ---- 실행
            resultText = TextTidy.Exact(kit.Label(root, "Result", "", 28, 1428, 1220, 1490, 28, TextAnchor.MiddleCenter, Green, true), BodyPt);
            actionButton = kit.GradientButton(root, "Action", "실행", 20, 1495, 1228, 1600, new Color(1f, 0.8f, 0.25f), new Color(0.93f, 0.55f, 0.1f), new Color(0.12f, 0.08f, 0.02f), 46);
            actionButton.onClick.AddListener(Execute);
            TextTidy.ExactButton(actionButton, ActionPt);

            // ---- 대상 선수 변경
            targetTitleText = TextTidy.Exact(kit.Label(root, "TargetTitle", "대상 선수 변경", 28, 1615, 860, 1675, 32, TextAnchor.MiddleLeft, White, true), SectionPt);
            scopeButton = kit.Button(root, "TargetScope", "라인업", 870, 1615, 1228, 1675, PanelLight, White, 28);
            scopeButton.onClick.AddListener(() => { lineupOnly = !lineupOnly; Refresh(); });
            TextTidy.ExactButton(scopeButton, SmallPt);
            targetContent = ScrollList(root, "TargetScroll", 20, 1682, 1228, 1966);

            if (Application.isPlaying) Refresh();
        }

        /// <summary>[TASK-KBO-191] 스킬 버튼 - 두 줄 문구(동작 + 비용)가 버튼(76px) 안에 들어가도록 14pt Normal · 줄간격 1.1 · 위아래 여백.</summary>
        private Button SkillButton(string name, string text, float x0, float x1, Color color)
        {
            var button = kit.Button(root, name, text, x0, 1336, x1, 1412, color, White, 22);
            var label = TextTidy.ExactButton(button, SkillButtonPt);
            label.lineSpacing = 1.1f;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            var rect = (RectTransform)label.transform;
            rect.offsetMin = new Vector2(4f, 3f);
            rect.offsetMax = new Vector2(-4f, -3f);
            return button;
        }

        /// <summary>[TASK-KBO-191] 훈련·특훈 탭 헤더 - "보유 스킬 (3슬롯) · 변경권 N / 고급 M / 특훈권 K".</summary>
        public static string SkillHeaderText(int changeTickets, int premiumTickets, int trainingTickets) =>
            $"보유 스킬 (3슬롯) · 변경권 {changeTickets} / 고급 {premiumTickets} / 특훈권 {trainingTickets}";

        /// <summary>[TASK-KBO-191] 스킬 버튼 두 줄 문구. 포인트는 만 단위("1.5만P").</summary>
        public static string SkillRerollLabel() => $"스킬 변경\n(변경권 1 / {Man(PlayerSkillRules.RerollPointCost)}P)";

        public static string SkillPremiumLabel() => "고급 변경(A~S)\n(고급권 1)";

        public static string SkillLevelUpLabel(PlayerSkillSlot slot) =>
            slot == null || slot.Level >= PlayerSkillRules.MaxLevel
                ? "스킬 레벨업\n(최대 Lv.6)"
                : $"스킬 레벨업\n(특훈권 {PlayerSkillRules.LevelUpTicketCost(slot.Level)} / {Man(PlayerSkillRules.LevelUpPointCost(slot.Level))}P)";

        private static string Man(int points) => (points / 10000f).ToString("0.#") + "만";

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
                RebuildRows(materialContent, new List<(string, Color, UnityEngine.Events.UnityAction)>(), 2, MaterialLine1Pt, MaterialLine2Pt);
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
            if (tab == GrowthTab.Training)
            {
                // [TASK-KBO-190] 상시 스킬 보정(경기 판정 + 상세창 "스킬 +N")
                var bonus = PlayerSkillRules.AlwaysStatBonuses(target);
                var labels = tpl.IsPitcher ? new[] { "구위", "구속", "변화", "제구", "체력" } : new[] { "파워", "정확", "선구", "주력", "수비" };
                var parts = labels.Select((l, i) => bonus[i] > 0 ? $"{l} +{bonus[i]}" : null).Where(s => s != null).ToList();
                statText.text += $"\n<color=#5EE08A>스킬 상시 보정: {(parts.Count > 0 ? string.Join(" · ", parts) : "없음(조건부 스킬만)")}</color>";
            }

            int maxMaterials = GrowthCenterRules.MaxMaterials(tab);
            int trainingTickets = gm != null ? gm.TrainingTicket : 0;
            // [TASK-KBO-191] 훈련·특훈 헤더는 짧게 다듬고 15pt로 줄여 한 줄에 고정(첫 스킬 박스와 겹치던 2줄 줄바꿈 제거)
            TextTidy.Exact(materialTitleText, tab == GrowthTab.Training ? SkillHeaderPt : SectionPt);
            materialTitleText.text = tab == GrowthTab.Training
                ? SkillHeaderText(gm != null ? gm.SkillChangeTicket : 0, gm != null ? gm.PremiumSkillChangeTicket : 0, trainingTickets)
                : tab == GrowthTab.Transcend
                    ? $"초월 재료 {TranscendRules.SlotSummary(target, CurrentTranscendSelection())} (후보 {candidates.Count}장)"
                    : $"재료 카드 선택 {selected.Count}/{maxMaterials} (후보 {candidates.Count}장)";
            var rows = new List<(string, Color, UnityEngine.Events.UnityAction)>();
            if (tab != GrowthTab.Training)
            {
                foreach (var m in candidates.Take(90))
                {
                    var captured = m;
                    bool on = selected.Contains(m);
                    // [TASK-KBO-189] 각성 재료 배지 - [같은 선수 +3각] / [같은 포지션 +1각], 초월 - [핵심 · 같은 선수] / [보조 · 같은 포지션]
                    string badge = tab == GrowthTab.Awaken ? CardGrowthRules.AwakenMaterialBadge(target, m)
                        : tab == GrowthTab.Transcend ? TranscendRules.MaterialBadge(target, m) : "";
                    bool core = tab == GrowthTab.Awaken ? CardGrowthRules.AwakenGainFor(target, m) == CardGrowthRules.SamePlayerAwakenGain : TranscendRules.IsCoreMaterial(target, m);
                    if (badge.Length > 0)
                        badge = $"<color={(core ? "#FFD045" : "#7FD1FF")}>{badge}</color> ";
                    rows.Add(($"{(on ? "✔ " : "")}{CardDisplay.TargetLine1(m)}\n{badge}{CardDisplay.TargetLine2(m, m.CalculateNeutralOVR())}",
                        on ? RowOn : RowOff, () => ToggleMaterial(captured)));
                }
            }
            if (tab == GrowthTab.Training)
            {
                // [TASK-KBO-190] 3슬롯 스킬 - "[S] 배팅 머신 Lv.3" + 상세 효과. 행을 누르면 레벨업 대상 슬롯 선택.
                var slots = PlayerSkillRules.SlotsOf(target);
                skillSlotIndex = Mathf.Clamp(skillSlotIndex, 0, Mathf.Max(0, slots.Count - 1));
                for (int i = 0; i < slots.Count; i++)
                {
                    int captured = i;
                    var slot = slots[i];
                    rows.Add(($"{(i == skillSlotIndex ? "▶ " : "")}슬롯 {i + 1}  {PlayerSkillRules.SlotRichLabel(slot)}\n{PlayerSkillRules.DetailText(slot)}",
                        i == skillSlotIndex ? RowLineup : RowOff, () => { skillSlotIndex = captured; Refresh(); }));
                }
            }
            if (tab == GrowthTab.Training) RebuildRows(materialContent, rows, 1, SkillSlotTitlePt, SkillSlotDescPt, rowHeight: 104f, split: 0.6f);
            else RebuildRows(materialContent, rows, 2, MaterialLine1Pt, MaterialLine2Pt);
            RefreshSkillButtons(gm);
            autoButton.interactable = tab != GrowthTab.Training && (candidates.Count > 0 || (tab == GrowthTab.Transcend && gm != null && gm.TranscendTicket > 0));
            CompyaUiKit.SetButtonText(autoButton, tab == GrowthTab.Transcend ? "초월 재료 자동 등록" : "자동 선택");
            clearButton.interactable = selected.Count > 0 || useCoreTicket;

            CompyaUiKit.SetButtonText(actionButton, $"{GrowthCenterRules.TabName(tab)} 실행");
            actionButton.interactable = CanExecute(gold, candidates);
            RefreshContextButton(gm);
            RebuildTargets(gm);
        }

        private bool CanExecute(int gold, IReadOnlyCollection<Player> candidates)
        {
            if (target?.Template == null) return false;
            switch (tab)
            {
                case GrowthTab.Enhance: return target.ReinforceLevel < Player.MaxReinforceLevel && selected.Count > 0;
                case GrowthTab.Awaken: return CardGrowthRules.CanAwakenNow(target, out _) && selected.Count > 0; // [TASK-KBO-189] +10강 선행
                case GrowthTab.Transcend:
                {
                    var gm = GameManager.Instance;
                    return gm != null && TranscendRules.Validate(target, CurrentTranscendSelection(), new GameManagerGrowthLedger(gm)) == null;
                }
                case GrowthTab.LimitBreak: return CardGrowthActions.CanLimitBreak(target, out _) && (selected.Count > 0 || candidates.Count > 0);
                case GrowthTab.Training:
                {
                    var gm = GameManager.Instance;
                    return CardGrowthActions.CanTrain(target, gm != null && gm.TrainingTicket > 0 ? int.MaxValue : gold, out _);
                }
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
                // [TASK-KBO-188] 1줄 "구자욱'24 (RF)" · 2줄 "GG · 3각 · OVR 98" - 줄마다 별도 Text(줄바꿈 잘림으로 이름이 사라지던 문제)
                // [TASK-KBO-191] 24pt Bold / 19pt → 17pt / 14pt Normal
                rows.Add(($"{CardDisplay.TargetLine1(p)}\n{CardDisplay.TargetLine2(p, p.CalculateNeutralOVR())}",
                    current ? RowOn : inLineup.Contains(p) ? RowLineup : RowOff, () => { SetTarget(captured); if (resultText != null) resultText.text = ""; }));
            }
            RebuildRows(targetContent, rows, 3, TargetLine1Pt, TargetLine2Pt);
        }

        private void RebuildRows(RectTransform content, List<(string text, Color color, UnityEngine.Events.UnityAction onClick)> items, int columns,
            int line1Pt, int line2Pt, float rowHeight = 92f, float split = 0.5f)
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
                row.GetComponent<LayoutElement>().preferredHeight = rowHeight;
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
                    var textColor = item.color == RowOn ? new Color(0.12f, 0.08f, 0.02f) : White;
                    int newline = item.text.IndexOf('\n');
                    if (newline < 0)
                    {
                        var label = TextTidy.Exact(kit.LabelOn(CompyaUiKit.Norm(cell.transform, "Text", 0.04f, 0.04f, 0.96f, 0.96f), item.text, 26, TextAnchor.MiddleCenter, textColor), line1Pt);
                        label.verticalOverflow = VerticalWrapMode.Truncate;
                        continue;
                    }
                    // [TASK-KBO-188] 두 줄은 각각 Text로 - 1줄(선수명 · 스킬 제목), 2줄(등급 · 성장 · OVR / 스킬 설명). [TASK-KBO-191] 크기는 호출부가 정한다(Normal).
                    var line1 = TextTidy.Exact(kit.LabelOn(CompyaUiKit.Norm(cell.transform, "Line1", 0.03f, split, 0.97f, 0.97f), item.text.Substring(0, newline), 24, TextAnchor.MiddleCenter, textColor), line1Pt);
                    var line2 = TextTidy.Exact(kit.LabelOn(CompyaUiKit.Norm(cell.transform, "Line2", 0.03f, 0.04f, 0.97f, split), item.text.Substring(newline + 1), 19, TextAnchor.MiddleCenter, textColor), line2Pt);
                    foreach (var line in new[] { line1, line2 })
                    {
                        line.horizontalOverflow = HorizontalWrapMode.Wrap;
                        line.verticalOverflow = VerticalWrapMode.Truncate;
                    }
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
            if (tab == GrowthTab.Transcend)
            {
                // [TASK-KBO-189] 핵심(같은 선수)은 1장 - 새로 고르면 교체, 보조(같은 포지션)는 최대 2장
                if (TranscendRules.IsCoreMaterial(target, material))
                {
                    selected.RemoveAll(p => TranscendRules.IsCoreMaterial(target, p));
                    useCoreTicket = false;
                }
                else if (selected.Count(p => TranscendRules.IsSupportMaterial(target, p)) >= TranscendRules.SupportCount)
                {
                    resultText.text = $"보조 재료는 최대 {TranscendRules.SupportCount}장까지 선택할 수 있습니다.";
                    return;
                }
                selected.Add(material);
                Refresh();
                return;
            }
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
            if (tab == GrowthTab.Transcend)
            {
                // [TASK-KBO-189] 초월 재료 자동 등록 - 핵심(같은 선수, 없으면 대체권) + 보조(같은 포지션 2장 / +5강 1장)
                var auto = TranscendRules.AutoAssign(target, gm.Inventory, gm.Roster, gm.TranscendTicket);
                useCoreTicket = auto.UseCoreTicket;
                selected.AddRange(auto.Cards);
                string reason = TranscendRules.Validate(target, auto, new GameManagerGrowthLedger(gm));
                resultText.color = reason == null ? Green : new Color(1f, 0.45f, 0.45f);
                resultText.text = reason == null ? "초월 재료 자동 등록 완료 - 주전 라인업 카드는 제외했습니다." : reason;
                Refresh();
                return;
            }
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
                {
                    if (!CardGrowthRules.CanAwakenNow(target, out var awakenReason)) { ok = false; message = awakenReason; break; }
                    var valid = selected.Where(m => CardGrowthRules.AwakenGainFor(target, m) > 0).ToList();
                    ok = valid.Count > 0 && UpgradeManager.ApplyAwaken(target, valid);
                    if (ok) used.AddRange(valid);
                    message = ok ? $"각성 완료 - {target.AwakenLabel} · {CardGrowthRules.AwakenStageNote(target.Template.Grade, target.AwakenLevel)}" : "각성할 수 없습니다(같은 선수 / 같은 포지션 재료 확인).";
                    break;
                }
                case GrowthTab.Transcend:
                    // [TASK-KBO-189] 복합 재료 초월 - 재료 · 대체권 · 포인트 · 트로피는 원장에서 직접 소모한다.
                    ok = TranscendRules.TryTranscend(target, CurrentTranscendSelection(), new GameManagerGrowthLedger(gm), out message);
                    if (ok) useCoreTicket = false;
                    break;
                case GrowthTab.LimitBreak:
                {
                    var material = selected.FirstOrDefault() ?? CardGrowthActions.PickLimitBreakMaterial(target, gm.Inventory, gm.Roster);
                    ok = CardGrowthActions.TryLimitBreak(target, material, out message);
                    if (ok) used.Add(material);
                    break;
                }
                case GrowthTab.Training:
                    if (gm.TrainingTicket > 0)
                    {
                        // [TASK-KBO-189] 특훈권이 있으면 볼 대신 1장 소모
                        ok = CardGrowthActions.TryTrain(target, int.MaxValue, out _, out message);
                        if (ok)
                        {
                            gm.TrainingTicket -= 1;
                            message = $"특훈 {target.TrainingLevel}/{CardGrowthRules.TrainingCap(target.Template.Grade)}단계 완료! OVR +1 (특훈권 -1, 남은 {gm.TrainingTicket}장)";
                        }
                        break;
                    }
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

        // ================================================================== [TASK-KBO-190] 3슬롯 스킬

        private void RefreshSkillButtons(GameManager gm)
        {
            bool skillTab = tab == GrowthTab.Training && target?.Template != null;
            autoButton.gameObject.SetActive(!skillTab);
            clearButton.gameObject.SetActive(!skillTab);
            foreach (var b in new[] { skillRerollButton, skillPremiumButton, skillLevelButton }) if (b != null) b.gameObject.SetActive(skillTab);
            if (!skillTab || skillRerollButton == null) return;
            int tickets = gm != null ? gm.SkillChangeTicket : 0, premium = gm != null ? gm.PremiumSkillChangeTicket : 0, points = gm != null ? gm.GameGold : 0;
            // [TASK-KBO-191] 두 줄(동작 / 비용) 문구 - 보유 수량은 헤더(보유 스킬 · 변경권 / 고급 / 특훈권)에 표시
            CompyaUiKit.SetButtonText(skillRerollButton, SkillRerollLabel());
            skillRerollButton.interactable = gm != null && (tickets > 0 || points >= PlayerSkillRules.RerollPointCost);
            CompyaUiKit.SetButtonText(skillPremiumButton, SkillPremiumLabel());
            skillPremiumButton.interactable = gm != null && premium > 0;
            var slots = PlayerSkillRules.SlotsOf(target);
            var slot = skillSlotIndex < slots.Count ? slots[skillSlotIndex] : null;
            CompyaUiKit.SetButtonText(skillLevelButton, SkillLevelUpLabel(slot));
            skillLevelButton.interactable = gm != null && slot != null && slot.Level < PlayerSkillRules.MaxLevel
                && (gm.TrainingTicket >= PlayerSkillRules.LevelUpTicketCost(slot.Level) || points >= PlayerSkillRules.LevelUpPointCost(slot.Level));
        }

        /// <summary>[스킬 변경] / [고급 스킬 변경] - 3슬롯 재추첨(레벨 유지).</summary>
        public bool RerollSkills(bool premium)
        {
            var gm = GameManager.Instance;
            if (gm == null || target?.Template == null) return false;
            bool ok = PlayerSkillRules.TryReroll(target, new GameManagerGrowthLedger(gm), premium, n => Random.Range(0, n), out var message);
            if (ok) SaveManager.Instance?.TrySaveCareer();
            resultText.color = ok ? Green : new Color(1f, 0.45f, 0.45f);
            resultText.text = message;
            Refresh();
            return ok;
        }

        /// <summary>[스킬 레벨업] - 선택 슬롯 Lv.n → n+1(특훈권 n장 또는 포인트 10,000 × n).</summary>
        public bool LevelUpSkill()
        {
            var gm = GameManager.Instance;
            if (gm == null || target?.Template == null) return false;
            bool ok = PlayerSkillRules.TryLevelUp(target, skillSlotIndex, new GameManagerGrowthLedger(gm), out var message);
            if (ok) SaveManager.Instance?.TrySaveCareer();
            resultText.color = ok ? Green : new Color(1f, 0.45f, 0.45f);
            resultText.text = message;
            Refresh();
            return ok;
        }

        public void SelectSkillSlot(int index)
        {
            skillSlotIndex = Mathf.Clamp(index, 0, PlayerSkillRules.SlotCount - 1);
            Refresh();
        }

        /// <summary>[TASK-KBO-189] 현재 선택을 초월 슬롯(핵심 1 · 보조 2)에 배정한 결과.</summary>
        private TranscendRules.Selection CurrentTranscendSelection() => TranscendRules.Assign(target, selected, useCoreTicket);

        /// <summary>[TASK-KBO-189] 탭별 상황 버튼 - 잠금이면 이동 안내, 아니면 보조권/대체권/특훈권.</summary>
        private void RefreshContextButton(GameManager gm)
        {
            if (contextButton == null) return;
            string label = ContextLabel(gm);
            contextButton.gameObject.SetActive(label.Length > 0);
            if (label.Length > 0) CompyaUiKit.SetButtonText(contextButton, label);
        }

        public string ContextLabel(GameManager gm)
        {
            if (target?.Template == null) return "";
            bool locked = target.ReinforceLevel < CardGrowthRules.AwakenRequiredReinforce;
            switch (tab)
            {
                case GrowthTab.Awaken:
                    if (locked) return "강화 탭으로 이동";
                    return $"각성 보조권 +1각 ({(gm != null ? gm.AwakenTicket : 0)})";
                case GrowthTab.Transcend:
                    if (!CardGrowthRules.CanTranscend(target.Template.Grade) || target.IsTranscended) return "";
                    if (locked) return "강화 탭으로 이동";
                    if (target.AwakenLevel < CardGrowthRules.FinalAwakenThreshold) return "각성 탭으로 이동";
                    return useCoreTicket ? $"대체권 사용 중 ({(gm != null ? gm.TranscendTicket : 0)})" : $"핵심 대체권 사용 ({(gm != null ? gm.TranscendTicket : 0)})";
                case GrowthTab.Training:
                    return gm != null && gm.TrainingTicket > 0 ? $"특훈권 {gm.TrainingTicket}장 보유" : "특훈권 구하기 ▸";
                default:
                    return "";
            }
        }

        private void OnContextButton()
        {
            var gm = GameManager.Instance;
            if (target?.Template == null) return;
            bool locked = target.ReinforceLevel < CardGrowthRules.AwakenRequiredReinforce;
            switch (tab)
            {
                case GrowthTab.Awaken:
                {
                    if (locked) { SelectTab(GrowthTab.Enhance); return; }
                    if (gm == null) return;
                    bool ok = ShopExchangeRules.TryUseAwakenTicket(target, new GameManagerGrowthLedger(gm), out var message);
                    if (ok) SaveManager.Instance?.TrySaveCareer();
                    selected.Clear();
                    resultText.color = ok ? Green : new Color(1f, 0.45f, 0.45f);
                    resultText.text = message;
                    Refresh();
                    return;
                }
                case GrowthTab.Transcend:
                    if (locked) { SelectTab(GrowthTab.Enhance); return; }
                    if (target.AwakenLevel < CardGrowthRules.FinalAwakenThreshold) { SelectTab(GrowthTab.Awaken); return; }
                    if (!useCoreTicket && (gm == null || gm.TranscendTicket < 1))
                    {
                        resultText.color = new Color(1f, 0.45f, 0.45f);
                        resultText.text = "초월 핵심 대체권이 없습니다 - 상점 · 교환소(성장 코인 1,500)에서 교환하십시오.";
                        return;
                    }
                    useCoreTicket = !useCoreTicket;
                    if (useCoreTicket) selected.RemoveAll(p => TranscendRules.IsCoreMaterial(target, p));
                    Refresh();
                    return;
                case GrowthTab.Training:
                    if (gm == null || gm.TrainingTicket <= 0) ShopExchangeView.OpenShop(ShopSubTab.PointShop);
                    return;
            }
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
