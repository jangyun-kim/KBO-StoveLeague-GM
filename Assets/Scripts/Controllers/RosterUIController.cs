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
    /// GameManager.Instance.Roster(28인)를 타자 15명/투수 13명 두 그룹으로 나눠 각각의
    /// GridLayoutGroup 컨테이너에 PlayerCardUI로 렌더링한다. GameActionController.OnRosterChanged를
    /// 구독해 ExecuteAutoRoster()가 로스터를 덮어쓸 때마다 자동으로 다시 그려진다.
    ///
    /// GDD 1절 "구단 세트덱 중심 플레이"를 유저가 체감하도록, 화면 상단에 현재 로스터의 최다 구단
    /// 인원수를 게이지/텍스트로 보여준다. [TASK-KBO-038] 15_team_power_policy.md 확정 기준(15명 이상
    /// -> +12 OVR)에 맞춰, 이 컨트롤러가 직접 로스터를 그룹핑해 최다 구단/인원수를 구한다 - 더 이상
    /// GameManager.CheckSetDeckBonus()(구식 5명/배율 1.15배 기준, 이번 작업에서 삭제됨)에 의존하지 않는다.
    /// </summary>
    public partial class RosterUIController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("OnRosterChanged 이벤트를 구독할 GameActionController. 비워두면 자동 갱신 없이 수동 RefreshRoster()만 동작한다.")]
        [SerializeField] private GameActionController gameActionController;

        [Header("Batter Roster (15명)")]
        [SerializeField] private Transform batterContainer;
        [Tooltip("타자/투수 공용 카드 프리팹.")]
        [SerializeField] private PlayerCardUI cardPrefab;
        /// <summary>[TASK-KBO-184] 성장 센터(GrowthCenterView)가 같은 카드 프리팹을 쓰도록 노출한다.</summary>
        public PlayerCardUI CardPrefab => cardPrefab;

        [Header("Pitcher Roster (13명)")]
        [SerializeField] private Transform pitcherContainer;

        [Header("Set Deck Visualization (GDD 1절 / 15_team_power_policy.md)")]
        [Tooltip("예: 'LG 트윈스 세트덱 활성화: 18/15 (+12 OVR)' 또는 '세트덱 미달성: 7/15'.")]
        [SerializeField] private Text setDeckStatusText;
        [Tooltip("Image.Type=Filled로 설정된 게이지 바. fillAmount = 최다 구단 인원 / 15.")]
        [SerializeField] private Image setDeckGaugeFillImage;
        [Tooltip("세트덱 보너스가 활성화됐을 때만 켜지는 배경 빛망울 등 장식용 오브젝트. 비워두면 생략.")]
        [SerializeField] private GameObject setDeckActiveGlowRoot;
        [SerializeField] private Color setDeckActiveColor = new Color(1f, 0.84f, 0f); // 골드
        [SerializeField] private Color setDeckInactiveColor = Color.white;

        [Header("Navigation")]
        [Tooltip("[TASK-KBO-093] 로스터 화면을 닫고 로비로 돌아가는 버튼. ScoutUIController/" +
                 "CheerleaderInventoryUIController의 closeButton과 동일한 관례로 UIManager.ShowScreen()만 " +
                 "호출한다.")]
        [SerializeField] private Button closeButton;

        [Header("Empty State (TASK-KBO-098)")]
        [Tooltip("타자/투수 카드가 단 한 장도 없을 때만 켜지는 안내 텍스트('배치된 선수가 없습니다' 등). " +
                 "비워두면 Debug.LogWarning만 남기고 화면상 안내는 생략한다.")]
        [SerializeField] private Text emptyStateText;
        [SerializeField] private string emptyStateMessage = "배치된 선수가 없습니다.";

        [Header("Bench (TASK-KBO-176 - 후보 6인 구역)")]
        [Tooltip("후보 타자 6인(세트덱 스코어 배터리) 카드를 따로 담는 컨테이너. 비워두면 batterContainer에 함께 그린다.")]
        [SerializeField] private Transform benchContainer;
        [Tooltip("후보 구역 머리글 - '후보 6인 · 세트덱 기여 nP' 형식으로 갱신된다. 비워두면 생략.")]
        [SerializeField] private Text benchHeaderText;

        [Header("Swap Popup (TASK-KBO-176 - 카드 클릭 시 보유 카드와 교체)")]
        [SerializeField] private GameObject swapPopupRoot;
        [SerializeField] private Text swapTitleText;
        [Tooltip("후보 카드를 고르면 '세트덱 182P → 186P (+4)' 미리보기를 표시한다.")]
        [SerializeField] private Text swapPreviewText;
        [SerializeField] private Transform swapCandidateContainer;
        [SerializeField] private Button swapConfirmButton;
        [SerializeField] private Button swapCancelButton;
        [Tooltip("교체 가능한 보유 카드가 0장일 때만 켜지는 안내 문구.")]
        [SerializeField] private Text swapEmptyText;
        [Tooltip("후보 목록 최대 표시 장수(예상 세트덱 스코어 상위부터). 인벤토리가 커도 팝업이 무거워지지 않도록 자른다.")]
        [SerializeField] private int maxSwapCandidates = 60;

        [Header("Set Deck Options (TASK-KBO-176 - 선택형 버프 구간)")]
        [SerializeField] private Button setDeckOptionButton;
        [SerializeField] private SetDeckOptionUIController setDeckOptionController;

        private readonly List<PlayerCardUI> spawnedBatterCards = new List<PlayerCardUI>();
        private readonly List<PlayerCardUI> spawnedPitcherCards = new List<PlayerCardUI>();
        private readonly List<PlayerCardUI> spawnedBenchCards = new List<PlayerCardUI>();
        private readonly List<PlayerCardUI> spawnedSwapCards = new List<PlayerCardUI>();

        [Header("Empty Slots / Auto Lineup (TASK-KBO-177)")]
        [Tooltip("빈 슬롯 플레이스홀더 템플릿(비활성 보관) - Image + Button + 자식 'Label'(Text).")]
        [SerializeField] private GameObject placeholderTemplate;
        [Tooltip("보유 카드로 28인 자동 편성(GameActionController.ExecuteAutoRoster).")]
        [SerializeField] private Button autoLineupButton;

        [Header("Tabs / Slot Frames (TASK-KBO-181 - [타자 라인업] / [투수 로스터] 분리)")]
        [Tooltip("켜면 RosterSlotLayout 28칸을 한 화면에 몰아 그리지 않고, 탭별로 슬롯 칸(머리글 + 카드)을 그린다. " +
                 "타자 탭 = 주전 9(batterContainer, 3x3) + 후보 6(benchContainer), 투수 탭 = 선발 5(startingPitcherContainer) + 불펜(bullpenContainer).")]
        [SerializeField] private bool useSlotFrames;
        [SerializeField] private Button batterTabButton;
        [SerializeField] private Button pitcherTabButton;
        [SerializeField] private GameObject batterPage;
        [SerializeField] private GameObject pitcherPage;
        [SerializeField] private Transform startingPitcherContainer;
        [SerializeField] private Transform bullpenContainer;
        [SerializeField] private Text lineupHeaderText;
        [SerializeField] private Text startingPitcherHeaderText;
        [SerializeField] private Text bullpenHeaderText;
        [SerializeField] private Font slotFont;
        [SerializeField] private Font slotBoldFont;
        [SerializeField] private Color tabActiveColor = new Color(0.09f, 0.19f, 0.47f);
        [SerializeField] private Color tabInactiveColor = new Color(0.93f, 0.94f, 0.97f);

        [Header("Set Deck Bar (TASK-KBO-181 - 하단 고정 바)")]
        [SerializeField] private Image setDeckTeamLogo;
        [SerializeField] private Text setDeckTeamText;
        [SerializeField] private Text setDeckScoreText;
        [SerializeField] private Text teamOvrText;

        private readonly List<GameObject> spawnedCells = new List<GameObject>();
        private bool showingPitchers;

        private readonly List<GameObject> spawnedPlaceholders = new List<GameObject>();
        private bool placeholderWarningLogged;
        private RosterSlotLayout.Slot placementSlot; // null이 아니면 팝업이 "빈 슬롯 배치" 모드

        private Player swapOutgoing;
        private Player swapSelectedIncoming;
        private RosterSwapRules.Candidate swapSelectedCandidate;
        private List<RosterSwapRules.Candidate> swapCandidates = new List<RosterSwapRules.Candidate>();

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
            if (swapConfirmButton != null) swapConfirmButton.onClick.AddListener(ConfirmSwap);
            if (swapCancelButton != null) swapCancelButton.onClick.AddListener(CloseSwapPopup);
            if (autoLineupButton != null) autoLineupButton.onClick.AddListener(ExecuteAutoLineup);
            if (setDeckOptionButton != null) setDeckOptionButton.onClick.AddListener(() => OpenSetDeckBuffSelection()); // [TASK-KBO-185]
            if (batterTabButton != null) batterTabButton.onClick.AddListener(() => ShowTab(false));
            if (pitcherTabButton != null) pitcherTabButton.onClick.AddListener(() => ShowTab(true));
            AwakeCompya();
        }

        /// <summary>[TASK-KBO-181] [타자 라인업 (15인)] / [투수 로스터 (13인)] 탭 전환.</summary>
        public void ShowTab(bool pitchers)
        {
            if (useCompyaLayout) { ShowCompyaTab(pitchers ? CompyaTab.Pitcher : CompyaTab.Batter); return; }
            showingPitchers = pitchers;
            if (batterPage != null) batterPage.SetActive(!pitchers);
            if (pitcherPage != null) pitcherPage.SetActive(pitchers);
            PaintTab(batterTabButton, !pitchers);
            PaintTab(pitcherTabButton, pitchers);
        }

        private void PaintTab(Button tab, bool active)
        {
            if (tab == null) return;
            if (tab.targetGraphic != null) tab.targetGraphic.color = active ? tabActiveColor : tabInactiveColor;
            var label = tab.GetComponentInChildren<Text>(true);
            if (label != null) label.color = active ? Color.white : new Color(0.45f, 0.48f, 0.55f);
        }

        private void OnEnable()
        {
            if (gameActionController != null)
            {
                gameActionController.OnRosterChanged += HandleRosterChanged;
            }
            if (GameManager.Instance != null) GameManager.Instance.OnSetDeckSelectionChanged += HandleRosterChanged;

            CloseSwapPopup();
            if (useCompyaLayout) OnEnableCompya();
            else if (useSlotFrames) ShowTab(showingPitchers);
            RefreshRoster();
        }

        private void OnDisable()
        {
            if (gameActionController != null)
            {
                gameActionController.OnRosterChanged -= HandleRosterChanged;
            }
            if (GameManager.Instance != null) GameManager.Instance.OnSetDeckSelectionChanged -= HandleRosterChanged;
        }

        private void HandleRosterChanged() => RefreshRoster();

        /// <summary>GameManager.Instance.Roster를 다시 읽어 주전 타자/후보 타자/투수 컨테이너를 새로 그린다.
        /// [TASK-KBO-177] 로스터가 비어 있어도 RosterSlotLayout의 고정 28슬롯(주전 C~DH 9 + BENCH 1~6 + SP1~5/RP/CP 13)을 항상
        /// 그린다 - 선수가 있는 슬롯은 카드(클릭 = 교체 팝업), 빈 슬롯은 "포지션 + [+ 선수 배치]" 플레이스홀더(클릭 = 배치 팝업).
        /// TASK-176은 카드가 있어야만 클릭할 수 있어 빈 로스터에서는 선수를 배치할 방법이 없는 데드락이 있었다.</summary>
        public void RefreshRoster()
        {
            ClearCards(spawnedBatterCards);
            ClearCards(spawnedPitcherCards);
            ClearCards(spawnedBenchCards);
            ClearPlaceholders();
            ClearCells();

            if (GameManager.Instance == null) return;

            var roster = GameManager.Instance.Roster;
            if (useCompyaLayout)
            {
                RefreshCompya(roster);
                RefreshEmptyState(roster.Count);
                RefreshSetDeckStatus();
                RefreshCompyaBar();
                return;
            }
            if (useSlotFrames)
            {
                RefreshSlotFrames(roster);
                RefreshEmptyState(roster.Count);
                RefreshSetDeckStatus();
                return;
            }

            var benchTarget = benchContainer != null ? benchContainer : batterContainer;

            foreach (var slot in RosterSlotLayout.Build(roster))
            {
                var container = slot.Kind == RosterSlotLayout.SlotKind.Pitcher ? pitcherContainer
                    : slot.Kind == RosterSlotLayout.SlotKind.BenchBatter ? benchTarget : batterContainer;
                var tracking = slot.Kind == RosterSlotLayout.SlotKind.Pitcher ? spawnedPitcherCards
                    : slot.Kind == RosterSlotLayout.SlotKind.BenchBatter ? spawnedBenchCards : spawnedBatterCards;

                if (slot.Player != null) SpawnRosterCard(slot.Player, container, tracking);
                else SpawnPlaceholder(slot, container);
            }

            RefreshEmptyState(roster.Count);
            RefreshSetDeckStatus();
        }

        // ----- [TASK-KBO-181] 탭별 슬롯 칸(머리글 + 카드) -----

        private void RefreshSlotFrames(IReadOnlyList<Player> roster)
        {
            var lineup = LineupView.BuildLineup(roster);
            var bench = LineupView.BuildBench(roster);
            var (starters, bullpen) = LineupView.BuildPitchers(roster);
            var nativeSize = CardHolderFit.NativeSizeOf(cardPrefab);

            foreach (var entry in lineup) SpawnCell(entry, batterContainer, spawnedBatterCards, nativeSize, new Color(0.13f, 0.3f, 0.72f));
            var benchTarget = benchContainer != null ? benchContainer : batterContainer;
            foreach (var entry in bench) SpawnCell(entry, benchTarget, spawnedBenchCards, nativeSize, new Color(0.24f, 0.26f, 0.32f));
            var spTarget = startingPitcherContainer != null ? startingPitcherContainer : pitcherContainer;
            foreach (var entry in starters) SpawnCell(entry, spTarget, spawnedPitcherCards, nativeSize, new Color(0.13f, 0.3f, 0.72f));
            var bullpenTarget = bullpenContainer != null ? bullpenContainer : pitcherContainer;
            foreach (var entry in bullpen) SpawnCell(entry, bullpenTarget, spawnedPitcherCards, nativeSize, BullpenColor(entry));

            int batterCount = roster.Count(p => p?.Template != null && !p.Template.IsPitcher);
            int pitcherCount = roster.Count(p => p?.Template != null && p.Template.IsPitcher);
            if (lineupHeaderText != null)
                lineupHeaderText.text = $"주전 타자 9인 · 타순 / 수비 포지션 (라인업 OVR {AverageOvr(lineup)})";
            if (startingPitcherHeaderText != null)
                startingPitcherHeaderText.text = $"선발 로테이션 1~5선발 (평균 OVR {AverageOvr(starters)})";
            if (bullpenHeaderText != null)
                bullpenHeaderText.text = $"불펜 · 승리조 2 / 추격조 4 / 롱릴리프 1 / 마무리 1 (투수 {pitcherCount}/{GameManager.RequiredPitcherCount})";
            if (batterTabButton != null) CompyaUiKit.SetButtonText(batterTabButton, $"타자 라인업 ({batterCount}/{GameManager.RequiredBatterCount})");
            if (pitcherTabButton != null) CompyaUiKit.SetButtonText(pitcherTabButton, $"투수 로스터 ({pitcherCount}/{GameManager.RequiredPitcherCount})");
        }

        private static string AverageOvr(IEnumerable<LineupView.Entry> entries)
        {
            var players = entries.Where(e => e.Player != null).Select(e => e.Player).ToList();
            return players.Count == 0 ? "-" : players.Average(p => p.CalculateOVR(false)).ToString("F1");
        }

        private static Color BullpenColor(LineupView.Entry entry) => entry.Role switch
        {
            _ when entry.IsExtra => new Color(0.35f, 0.35f, 0.38f),
            PitcherRole.Closer => new Color(0.66f, 0.1f, 0.14f),
            PitcherRole.WinningReliever => new Color(0.12f, 0.45f, 0.36f),
            PitcherRole.LongReliever => new Color(0.42f, 0.3f, 0.62f),
            _ => new Color(0.2f, 0.24f, 0.33f),
        };

        /// <summary>슬롯 칸 1개: 머리글(타순·포지션 / 선발 순번 / 불펜 보직) + 카드 홀더(카드 디자인 크기 유지 스케일) + 강화 뱃지.
        /// 카드가 있으면 클릭 = 교체 팝업, 비어 있으면 클릭 = 배치 팝업.</summary>
        private void SpawnCell(LineupView.Entry entry, Transform container, List<PlayerCardUI> tracking, Vector2 nativeSize, Color headerColor)
        {
            if (container == null) return;

            var cell = new GameObject($"Slot_{entry.Header}", typeof(RectTransform), typeof(Image), typeof(Button));
            var cellRect = (RectTransform)cell.transform;
            cellRect.SetParent(container, false);
            var background = cell.GetComponent<Image>();
            background.color = entry.Player != null ? new Color(0.07f, 0.09f, 0.16f, 0.92f) : new Color(0.16f, 0.2f, 0.3f, 0.85f);
            var button = cell.GetComponent<Button>();
            button.targetGraphic = background;
            spawnedCells.Add(cell);

            var header = CompyaUiKit.Norm(cellRect, "Header", 0f, 0.84f, 1f, 1f);
            CompyaUiKit.Paint(header, headerColor);
            string headerText = entry.BattingOrder > 0 ? $"<color=#FFD54A>{entry.BattingOrder}번</color> {entry.Header}" : entry.Header;
            if (entry.IsFill) headerText += " <size=80%>(대체)</size>";
            CellLabel(header, headerText, TextAnchor.MiddleCenter, Color.white, true);

            var holderRect = CompyaUiKit.Norm(cellRect, "CardHolder", 0.03f, 0.015f, 0.97f, 0.83f);
            var holder = holderRect.gameObject.AddComponent<CardHolderFit>();
            holder.Configure(nativeSize, 1f);

            if (entry.Player != null)
            {
                var player = entry.Player;
                var card = SpawnCard(player, holderRect, tracking);
                if (card != null)
                {
                    holder.Place((RectTransform)card.transform);
                    BindCardClick(card, () => OpenSwapPopup(player));
                }
                button.onClick.AddListener(() => OpenSwapPopup(player));

                // [TASK-KBO-188] 강화(+N)/각성(N각·초월) 배지는 PlayerCardUI 네임플레이트가 그린다.
            }
            else
            {
                CellLabel(holderRect, $"{entry.Header}\n\n[+ 선수 배치]", TextAnchor.MiddleCenter, new Color(0.78f, 0.85f, 0.98f), false);
                var slot = entry.ToPlacementSlot();
                button.onClick.AddListener(() => OpenPlacementPopup(slot));
            }
        }

        private void CellLabel(RectTransform parent, string text, TextAnchor anchor, Color color, bool bold)
        {
            var rect = CompyaUiKit.Norm(parent, "Text", 0.02f, 0f, 0.98f, 1f);
            var label = rect.gameObject.AddComponent<Text>();
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.font = bold ? (slotBoldFont != null ? slotBoldFont : slotFont != null ? slotFont : fallback) : (slotFont != null ? slotFont : fallback);
            label.text = text;
            label.alignment = anchor;
            label.color = color;
            label.supportRichText = true;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = 30;
            label.fontSize = 30;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            TextTidy.Normalize(label, false, slotFont); // [TASK-KBO-191] Bold 해제 · 계층 크기(30 → 19~) · 자간
        }

        private void ClearCells()
        {
            foreach (var cell in spawnedCells)
            {
                if (cell != null) Destroy(cell);
            }
            spawnedCells.Clear();
        }

        /// <summary>[TASK-KBO-177] 빈 슬롯 플레이스홀더(placeholderTemplate 복제 - 자식 "Label" Text). 템플릿이 없으면(씬 미갱신)
        /// 빈 슬롯을 그리지 못하므로 한 번만 경고한다.</summary>
        private void SpawnPlaceholder(RosterSlotLayout.Slot slot, Transform container)
        {
            if (container == null) return;
            if (placeholderTemplate == null)
            {
                if (!placeholderWarningLogged)
                {
                    Debug.LogWarning("[RosterUIController] 빈 슬롯 템플릿(placeholderTemplate)이 배선되지 않았습니다 - " +
                        "'KBO Manager/Setup/Apply Latest UI (TASK-168~184)'을 실행해 씬을 갱신하십시오.");
                    placeholderWarningLogged = true;
                }
                return;
            }

            var placeholder = Instantiate(placeholderTemplate, container);
            placeholder.SetActive(true);
            placeholder.name = $"EmptySlot_{slot.Label}";
            var label = placeholder.transform.Find("Label")?.GetComponent<Text>();
            if (label != null) label.text = $"{slot.Label}\n\n[+ 선수 배치]";
            var button = placeholder.GetComponent<Button>();
            if (button != null)
            {
                var captured = slot;
                button.onClick.AddListener(() => OpenPlacementPopup(captured));
            }
            spawnedPlaceholders.Add(placeholder);
        }

        private void ClearPlaceholders()
        {
            foreach (var placeholder in spawnedPlaceholders)
            {
                if (placeholder != null) Destroy(placeholder);
            }
            spawnedPlaceholders.Clear();
        }

        /// <summary>
        /// [TASK-KBO-098] 로스터가 비어 있을 때 안내 문구를 띄운다. [TASK-KBO-177] 이제 빈 슬롯이 항상 그려지므로 경고 로그 대신
        /// "빈 슬롯을 누르거나 [자동 편성]" 안내만 표시한다(로스터를 열 때마다 콘솔 경고가 쌓이던 문제 해소).
        /// </summary>
        private void RefreshEmptyState(int rosterCount)
        {
            if (emptyStateText == null) return;

            int inventoryCount = GameManager.Instance != null ? GameManager.Instance.Inventory.Count : 0;
            emptyStateText.text = rosterCount == 0
                ? (inventoryCount > 0
                    ? $"로스터가 비어 있습니다 (보유 카드 {inventoryCount}장). 빈 슬롯을 누르거나 [자동 편성]을 누르십시오."
                    : $"{emptyStateMessage} (보유 카드 0장 - 스카우트에서 선수를 영입하십시오.)")
                : $"1군 {rosterCount}/{RosterSlotLayout.BatterCount + RosterSlotLayout.PitcherCount}명 · 빈 슬롯을 눌러 배치, 카드를 눌러 교체";
            emptyStateText.gameObject.SetActive(true);
        }

        /// <summary>[TASK-KBO-177] [자동 편성] - 보유 카드로 28인 오토 라인업(주전 OVR 우선 + 후보 6인 세트덱 스코어 우선).
        /// GameActionController.ExecuteAutoRoster()가 OnRosterChanged를 내면 이 화면이 다시 그려진다.</summary>
        private void ExecuteAutoLineup()
        {
            if (gameActionController == null)
            {
                Debug.LogWarning("[RosterUIController] GameActionController가 배선되지 않아 자동 편성을 실행할 수 없습니다.");
                return;
            }

            CloseSwapPopup();
            GameManager.Instance?.SetBattingOrderOverride(null); // [TASK-KBO-182] 새 라인업은 기본 타순부터
            GameManager.Instance?.LineupAssignment.Clear(); // [TASK-KBO-186] 맞교환 고정도 초기화(자동 편성 = OVR 기준)
            gameActionController.ExecuteAutoRoster();
            var gm = GameManager.Instance;
            if (gm != null && gm.Roster.Count == 0 && gm.Inventory.Count > 0)
            {
                Debug.LogWarning("[RosterUIController] 자동 편성 후에도 로스터가 비어 있습니다 - GameActionController.rosterManager " +
                    "배선을 확인하십시오('KBO Manager/Setup/Apply Latest UI (TASK-168~184)'이 자동 배선).");
            }
            RefreshRoster(); // OnRosterChanged 구독 여부와 무관하게 즉시 반영
        }

        /// <summary>
        /// [TASK-KBO-172 전면 개편] 27인 세트덱 스코어(GameManager.EvaluateSetDeck)로 상단 게이지·텍스트·글로우를
        /// 갱신한다. 게이지는 최종 목표(200P) 대비 진행률, 텍스트는 기준 구단/스코어/다음 목표 단계, 글로우는
        /// 1차 목표(최소 목표 스코어 150P) 달성 시 켠다 - 이전의 "동일 구단 15명 이상 +12" 게이지는 폐기됐다.
        /// RefreshRoster()가 호출될 때마다 함께 갱신되므로 별도로 구독할 이벤트가 없다.
        /// </summary>
        private void RefreshSetDeckStatus()
        {
            if (GameManager.Instance == null) return;

            var gm = GameManager.Instance;
            string favoriteTeamName = gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null;
            var setDeck = GameManager.EvaluateSetDeck(gm.Roster.ToList(), favoriteTeamName, gm.SetDeckSelection);

            bool isGoalMet = setDeck.IsMinimumGoalMet;
            var themeColor = isGoalMet ? setDeckActiveColor : setDeckInactiveColor;

            if (setDeckStatusText != null)
            {
                setDeckStatusText.text = $"{setDeck.DeckTeam} {SetDeckUIText.Summary(setDeck)}";
                setDeckStatusText.color = themeColor;
            }

            if (setDeckGaugeFillImage != null)
            {
                setDeckGaugeFillImage.fillAmount = Mathf.Clamp01((float)setDeck.Score / SetDeckBuffTable.FinalGoalScore);
                setDeckGaugeFillImage.color = themeColor;
            }

            if (setDeckActiveGlowRoot != null)
            {
                setDeckActiveGlowRoot.SetActive(isGoalMet);
            }

            // [TASK-KBO-181] 하단 고정 바: 활성 구단 세트덱 뱃지(로고 + 구단명) · 스코어 NP / 200P · 팀 OVR.
            if (setDeckTeamLogo != null) TeamLogoSprites.Apply(setDeckTeamLogo, setDeck.DeckTeam);
            if (setDeckTeamText != null) setDeckTeamText.text = $"{CompyaUiKit.FullName(setDeck.DeckTeam)} 세트덱";
            if (setDeckScoreText != null)
                setDeckScoreText.text = $"<color=#5FE3FF>{setDeck.Score}P</color> / {SetDeckBuffTable.FinalGoalScore}P";
            if (teamOvrText != null) teamOvrText.text = $"팀 OVR {gm.CalculateTeamOVR()}";

            if (benchHeaderText != null)
            {
                SetDeckEvaluator.ClassifyBatters(gm.Roster, out _, out var bench);
                int benchScore = bench.Sum(p => SetDeckEvaluator.ContributionScore(p, setDeck.DeckTeam, setDeck.IsDynastyActive));
                benchHeaderText.text = $"후보 {bench.Count}인 · 세트덱 기여 {benchScore}P (카드를 눌러 교체)";
            }
        }

        private void SpawnRosterCard(Player player, Transform container, List<PlayerCardUI> tracking)
        {
            var card = SpawnCard(player, container, tracking);
            if (card != null) BindCardClick(card, () => OpenSwapPopup(player));
        }

        private PlayerCardUI SpawnCard(Player player, Transform container, List<PlayerCardUI> tracking)
        {
            if (cardPrefab == null || container == null) return null;

            var card = CardPoolManager.Instance != null
                ? CardPoolManager.Instance.Get(cardPrefab, container)
                : Instantiate(cardPrefab, container);

            card.Setup(player);
            tracking.Add(card);
            return card;
        }

        /// <summary>[TASK-KBO-176] 카드 클릭 연결. 공용 카드 템플릿에 Button이 없으면 붙인다. 풀링 카드라 이전 대여의
        /// 리스너를 먼저 지운다(ClearCards()도 반납 전에 지워, 이 화면의 리스너가 다른 화면의 카드로 새지 않게 한다).</summary>
        private static void BindCardClick(PlayerCardUI card, UnityEngine.Events.UnityAction onClick)
        {
            var button = card.GetComponent<Button>();
            if (button == null) button = card.gameObject.AddComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(onClick);
        }

        private void ClearCards(List<PlayerCardUI> tracking)
        {
            foreach (var card in tracking)
            {
                if (card == null) continue;
                if (card.TryGetComponent<Button>(out var button)) button.onClick.RemoveAllListeners();
                var badge = card.transform.Find(SwapBadgeName); // [TASK-KBO-186] 맞교환 배지가 풀 카드에 남지 않게
                if (badge != null)
                {
                    badge.SetParent(null, false);
                    if (Application.isPlaying) Destroy(badge.gameObject); else DestroyImmediate(badge.gameObject);
                }
                CardHolderFit.ResetCard(card); // [TASK-KBO-181] 슬롯 칸 스케일이 다른 화면 재사용에 새지 않게

                if (CardPoolManager.Instance != null) CardPoolManager.Instance.Release(card);
                else if (Application.isPlaying) Destroy(card.gameObject);
                else DestroyImmediate(card.gameObject);
            }
            tracking.Clear();
        }

        // ----- [TASK-KBO-176] 후보/주전 수동 교체 팝업 -----

        /// <summary>outgoing 카드와 바꿀 수 있는 보유 카드 목록(RosterSwapRules - 후보 슬롯은 아무 타자, 주전은 같은
        /// 포지션, 투수는 같은 보직)을 교체 후 예상 세트덱 스코어 높은 순으로 띄운다. 카드를 고르면 미리보기가 나오고
        /// [교체] 버튼으로 확정한다(오조작 방지 2단계).</summary>
        public void OpenSwapPopup(Player outgoing)
        {
            var gm = GameManager.Instance;
            if (gm == null || outgoing?.Template == null) return;
            if (swapPopupRoot == null)
            {
                Debug.LogWarning("[RosterUIController] 교체 팝업(swapPopupRoot)이 배선되지 않았습니다 - " +
                    "'KBO Manager/Setup/Auto-Connect Roster UI'를 실행해 씬을 갱신하십시오.");
                return;
            }

            swapOutgoing = outgoing;
            placementSlot = null;
            string favoriteTeamName = gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null;
            // [TASK-KBO-186] 로스터 안 맞교환 대상(주전 ↔ 후보, 선발 ↔ 불펜)을 목록 최상단에, 그 아래 보유 카드 교체 후보.
            var candidates = RosterSwapRules.GetLineupSwapCandidates(gm.Roster, outgoing, favoriteTeamName, gm.SetDeckSelection);
            candidates.AddRange(RosterSwapRules.GetCandidates(gm.Inventory, gm.Roster, outgoing, favoriteTeamName, gm.SetDeckSelection));

            bool isBench = RosterSwapRules.IsBenchBatter(gm.Roster, outgoing);
            string slotLabel = outgoing.Template.IsPitcher ? "투수" : isBench ? "후보 타자" : "주전 타자";
            ShowCandidatePopup(candidates,
                $"{slotLabel} 교체: {outgoing.Template.PlayerName} (OVR {outgoing.CalculateOVR(false)}, SD {outgoing.SetDeckScore})");
        }

        /// <summary>[TASK-KBO-177] 빈 슬롯 배치 팝업 - RosterSwapRules.CanPlace(주전=같은 포지션, 후보=아무 타자, 투수=같은 보직,
        /// 정원 15/13) 조건의 보유 카드를 배치 후 예상 세트덱 스코어 순으로 띄운다.</summary>
        public void OpenPlacementPopup(RosterSlotLayout.Slot slot)
        {
            var gm = GameManager.Instance;
            if (gm == null || slot == null) return;
            if (swapPopupRoot == null)
            {
                Debug.LogWarning("[RosterUIController] 배치 팝업(swapPopupRoot)이 배선되지 않았습니다 - " +
                    "'KBO Manager/Setup/Apply Latest UI (TASK-168~184)'을 실행해 씬을 갱신하십시오.");
                return;
            }

            swapOutgoing = null;
            placementSlot = slot;
            string favoriteTeamName = gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null;
            var candidates = RosterSwapRules.GetPlacementCandidates(gm.Inventory, gm.Roster, slot, favoriteTeamName, gm.SetDeckSelection);
            ShowCandidatePopup(candidates, $"빈 슬롯 배치: {slot.Label}");
        }

        private void ShowCandidatePopup(List<RosterSwapRules.Candidate> candidates, string header)
        {
            swapSelectedIncoming = null;
            swapSelectedCandidate = null;
            int inRoster = candidates.Count(c => c.InRoster);
            swapCandidates = candidates.Take(Mathf.Max(1, maxSwapCandidates) + inRoster).ToList();
            if (swapTitleText != null)
            {
                int owned = candidates.Count - inRoster, shown = swapCandidates.Count - inRoster;
                swapTitleText.text = $"{header}\n{(inRoster > 0 ? $"위치 맞교환 {inRoster}명 · " : "")}보유 카드 {owned}장 (세트덱 스코어 높은 순{(owned > shown ? $", 상위 {shown}장 표시" : "")})";
            }

            ClearCards(spawnedSwapCards);
            foreach (var candidate in swapCandidates)
            {
                var card = SpawnCard(candidate.Player, swapCandidateContainer, spawnedSwapCards);
                if (card == null) continue;
                if (candidate.InRoster) AttachSwapBadge(card, candidate.Badge);
                var captured = candidate;
                BindCardClick(card, () => SelectSwapCandidate(captured));
            }

            if (swapEmptyText != null) swapEmptyText.gameObject.SetActive(swapCandidates.Count == 0);
            UpdateSwapPreview(null);
            swapPopupRoot.SetActive(true);
        }

        private const string SwapBadgeName = "SwapBadge186";

        /// <summary>[TASK-KBO-186] 로스터 안 맞교환 대상 카드 상단 배지([현재 후보 · 위치 맞교환] 등). 풀 반납 전 ClearCards()가 지운다.</summary>
        private static void AttachSwapBadge(PlayerCardUI card, string text)
        {
            var rect = CompyaUiKit.Norm(card.transform, SwapBadgeName, 0f, 0.86f, 1f, 1f);
            CompyaUiKit.Paint(rect, new Color(0.98f, 0.76f, 0.2f, 0.95f));
            var label = CompyaUiKit.Fill(rect, "Text").gameObject.AddComponent<Text>();
            var themed = card.GetComponentInChildren<Text>(true);
            label.font = themed != null && themed.font != null ? themed.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(0.08f, 0.1f, 0.18f);
            label.fontStyle = FontStyle.Normal;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = 22;
        }

        private void SelectSwapCandidate(RosterSwapRules.Candidate candidate)
        {
            swapSelectedCandidate = candidate;
            swapSelectedIncoming = candidate?.Player;
            foreach (var card in spawnedSwapCards)
            {
                if (card != null) card.SetSelected(card.BoundPlayer == swapSelectedIncoming);
            }
            UpdateSwapPreview(candidate);
        }

        private void UpdateSwapPreview(RosterSwapRules.Candidate candidate)
        {
            if (swapConfirmButton != null) swapConfirmButton.interactable = candidate != null;
            if (swapPreviewText == null) return;

            if (candidate == null || (swapOutgoing == null && placementSlot == null))
            {
                swapPreviewText.text = placementSlot != null ? "배치할 카드를 선택하십시오." : "교체할 카드를 선택하십시오.";
                return;
            }

            if (candidate.InRoster && swapOutgoing != null)
            {
                swapPreviewText.text = $"위치 맞교환: {swapOutgoing.Template.PlayerName} ↔ {candidate.Player.Template.PlayerName} " +
                                       $"(OVR {swapOutgoing.CalculateOVR(false)} ↔ {candidate.Player.CalculateOVR(false)}) · 로스터 구성 · 세트덱 {candidate.ProjectedScore}P 유지";
                return;
            }

            int current = candidate.ProjectedScore - candidate.ScoreDelta;
            string delta = candidate.ScoreDelta > 0 ? $"+{candidate.ScoreDelta}" : candidate.ScoreDelta.ToString();
            swapPreviewText.text = swapOutgoing != null
                ? $"세트덱 {current}P → {candidate.ProjectedScore}P ({delta}) · " +
                  $"OVR {swapOutgoing.CalculateOVR(false)} → {candidate.Player.CalculateOVR(false)} · " +
                  $"SD {swapOutgoing.SetDeckScore} → {candidate.Player.SetDeckScore}"
                : $"세트덱 {current}P → {candidate.ProjectedScore}P ({delta}) · " +
                  $"{candidate.Player.Template.PlayerName} OVR {candidate.Player.CalculateOVR(false)} · SD {candidate.Player.SetDeckScore}";
        }

        private void ConfirmSwap()
        {
            var gm = GameManager.Instance;
            if (gm == null || swapSelectedIncoming == null) return;

            string inName = swapSelectedIncoming.Template.PlayerName;
            if (placementSlot != null)
            {
                string slotLabel = placementSlot.Label;
                if (!RosterSwapRules.CanPlace(gm.Roster, placementSlot, swapSelectedIncoming) || !gm.AddPlayerToRoster(swapSelectedIncoming))
                {
                    Debug.LogWarning($"[RosterUIController] 배치 실패: {inName} -> {slotLabel} (정원/슬롯 조건 위반 또는 미보유 카드)");
                    return;
                }

                CloseSwapPopup();
                RefreshRoster();
                if (setDeckOptionController != null) setDeckOptionController.Refresh();
                Debug.Log($"[RosterUIController] 빈 슬롯 배치: {slotLabel} <- {inName}");
                return;
            }

            if (swapOutgoing == null) return;
            string outName = swapOutgoing.Template.PlayerName;
            if (swapSelectedCandidate != null && swapSelectedCandidate.InRoster)
            {
                // [TASK-KBO-186] 로스터 안 1:1 맞교환 - 빈 슬롯/중복 없이 두 선수의 자리(주전 ↔ 후보, 선발 ↔ 불펜)만 바뀐다.
                if (!gm.SwapLineupPositions(swapOutgoing, swapSelectedIncoming))
                {
                    Debug.LogWarning($"[RosterUIController] 위치 맞교환 실패: {outName} ↔ {inName}");
                    return;
                }
                CloseSwapPopup();
                RefreshRoster();
                if (setDeckOptionController != null) setDeckOptionController.Refresh();
                Debug.Log($"[RosterUIController] 위치 맞교환: {outName} ↔ {inName}");
                return;
            }
            if (!gm.SwapRosterPlayer(swapOutgoing, swapSelectedIncoming))
            {
                Debug.LogWarning($"[RosterUIController] 교체 실패: {outName} -> {inName} (교체 규칙 위반 또는 미보유 카드)");
                return;
            }

            CloseSwapPopup();
            RefreshRoster(); // 27인 세트덱 스코어/버프 구간/후보 기여 스코어 즉시 재계산
            if (setDeckOptionController != null) setDeckOptionController.Refresh();
            Debug.Log($"[RosterUIController] 로스터 교체: {outName} -> {inName}");
        }

        public void CloseSwapPopup()
        {
            ClearCards(spawnedSwapCards);
            swapOutgoing = null;
            placementSlot = null;
            swapSelectedIncoming = null;
            swapSelectedCandidate = null;
            if (swapPopupRoot != null) swapPopupRoot.SetActive(false);
        }
    }
}
