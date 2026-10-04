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
    /// [TASK-KBO-182] 컴프야V26 1:1 라인업(useCompyaLayout).
    ///   - 상단 3탭 [타자] | [투수] | [보관 선수], 서브 툴바 [기본] [전체 보기] … [자동 교체] [수비 위치 변경] [타순 변경].
    ///   - 타자: 그라운드 다이아몬드 실제 수비 위치(CF / LF·RF / SS·2B / 3B·1B / C·DH)에 주전 9 + 아래 [후보] 띠와 후보 6.
    ///     카드 위 포지션 약어, 카드 좌측 다이아몬드 타순 번호, 우상단 구단 로고, 하단 이름'연도.
    ///   - 투수: 1~5선발 + 불펜 4열(승리조 / 추격조 / 롱릴리프 / 마무리).
    ///   - 하단 기본 바: OVR | 세트덱 스코어 [?] | {구단} {N} POINT + 구간 마커 게이지 + [라인업 정보] [세트덱 버프 선택] [시너지].
    ///   - 슬롯(선수) 클릭: 교체 팝업을 바로 띄우지 않고 슬롯을 강조하고 하단 바를 "선수 액션 트레이"로 바꾼다
    ///     (카드 미리보기 · 스킬/능력치 3종 · [X] · [상세 정보] [선수 관리] [교체]). 교체 팝업은 [교체]를 눌렀을 때만 연다.
    ///     빈 슬롯([+ 선수 배치])은 바로 배치 후보 목록을 연다.
    /// </summary>
    public partial class RosterUIController
    {
        public enum CompyaTab { Batter, Pitcher, Storage }
        private enum LineupMode { None, BattingOrder, Defense }

        [Header("Compya Lineup (TASK-KBO-182)")]
        [SerializeField] private bool useCompyaLayout;
        [SerializeField] private Button storageTabButton;
        [SerializeField] private GameObject storagePage;
        [SerializeField] private Transform storageContainer;
        [SerializeField] private Text storageInfoText;
        [Header("TASK-KBO-184 - 보관 선수 필터/정렬(구 선수 관리 탭 보유 리스트)")]
        [SerializeField] private Button storageScopeButton;
        [SerializeField] private Button storageTeamButton;
        [SerializeField] private Button storagePositionButton;
        [SerializeField] private Button storageGradeButton;
        [SerializeField] private Button storageSortButton;
        [Tooltip("BatterPosition 순서(C, 1B, 2B, 3B, SS, LF, CF, RF, DH)의 다이아몬드 자리.")]
        [SerializeField] private RectTransform[] diamondSlots = new RectTransform[9];
        [SerializeField] private GameObject diamondRoot;
        [SerializeField] private GameObject fullViewRoot;
        [SerializeField] private Transform fullViewContainer;
        [Tooltip("불펜 열: 승리조 / 추격조 / 롱릴리프 / 마무리.")]
        [SerializeField] private RectTransform[] bullpenColumns = new RectTransform[4];
        [SerializeField] private Button basicViewButton;
        [SerializeField] private Button fullViewButton;
        [SerializeField] private Button defenseChangeButton;
        [SerializeField] private Button battingOrderButton;
        [SerializeField] private Text toolbarHintText;

        [Header("Compya Bottom Bar")]
        [SerializeField] private GameObject defaultBarRoot;
        [SerializeField] private Text[] gaugeMarkerTexts = new Text[6];
        [SerializeField] private Text[] gaugeIconTexts = new Text[6];
        [SerializeField] private Image[] gaugeIconImages = new Image[6];
        [SerializeField] private Button helpButton;
        [SerializeField] private Button lineupInfoButton;
        [SerializeField] private Button synergyButton;

        [Header("Compya Action Tray")]
        [SerializeField] private GameObject trayRoot;
        [SerializeField] private RectTransform trayCardHolder;
        [SerializeField] private Text trayTitleText;
        [SerializeField] private Text[] trayChipTitles = new Text[3];
        [SerializeField] private Text[] trayChipSubs = new Text[3];
        [SerializeField] private Text[] trayChipIcons = new Text[3];
        [SerializeField] private Button trayCloseButton;
        [SerializeField] private Button trayDetailButton;
        [SerializeField] private Button trayManageButton;
        [SerializeField] private Button traySwapButton;

        [Header("Compya Info Popup")]
        [SerializeField] private GameObject infoPopupRoot;
        [SerializeField] private Text infoTitleText;
        [SerializeField] private Text infoBodyText;
        [SerializeField] private Button infoCloseButton;
        [SerializeField] private SkillDB skillDB;

        private CompyaTab compyaTab = CompyaTab.Batter;
        private readonly StorageFilter storageFilter = new StorageFilter();
        private const int StorageDisplayLimit = 150;
        private bool fullView;
        private LineupMode mode = LineupMode.None;
        private Player orderFirstPick;
        private Player selectedPlayer;
        private readonly List<PlayerCardUI> spawnedTrayCards = new List<PlayerCardUI>();
        private readonly List<PlayerCardUI> spawnedStorageCards = new List<PlayerCardUI>();
        private readonly Dictionary<Player, GameObject> selectionFrames = new Dictionary<Player, GameObject>();

        private static readonly Color CyanAccent = new Color(0.37f, 0.89f, 1f);
        private static readonly Color SelectGold = new Color(1f, 0.83f, 0.2f);

        private void AwakeCompya()
        {
            if (!useCompyaLayout) return;
            if (storageTabButton != null) storageTabButton.onClick.AddListener(() => ShowCompyaTab(CompyaTab.Storage));
            BindStorageFilter(storageScopeButton, storageFilter.CycleScope);
            BindStorageFilter(storageTeamButton, storageFilter.CycleTeam);
            BindStorageFilter(storagePositionButton, storageFilter.CyclePosition);
            BindStorageFilter(storageGradeButton, storageFilter.CycleGrade);
            BindStorageFilter(storageSortButton, storageFilter.CycleSort);
            if (basicViewButton != null) basicViewButton.onClick.AddListener(() => SetFullView(false));
            if (fullViewButton != null) fullViewButton.onClick.AddListener(() => SetFullView(!fullView));
            if (defenseChangeButton != null) defenseChangeButton.onClick.AddListener(() => ToggleMode(LineupMode.Defense));
            if (battingOrderButton != null) battingOrderButton.onClick.AddListener(() => ToggleMode(LineupMode.BattingOrder));
            if (trayCloseButton != null) trayCloseButton.onClick.AddListener(Deselect);
            if (trayDetailButton != null) trayDetailButton.onClick.AddListener(OpenDetail);
            if (trayManageButton != null) trayManageButton.onClick.AddListener(OpenManagement);
            if (traySwapButton != null) traySwapButton.onClick.AddListener(SwapSelected);
            if (infoCloseButton != null) infoCloseButton.onClick.AddListener(() => infoPopupRoot?.SetActive(false));
            if (helpButton != null) helpButton.onClick.AddListener(() => ShowInfo("세트덱 스코어란?", BuildHelpText()));
            if (lineupInfoButton != null) lineupInfoButton.onClick.AddListener(() => ShowInfo("라인업 정보", BuildLineupInfoText()));
            if (synergyButton != null) synergyButton.onClick.AddListener(() => ShowInfo("시너지", BuildSynergyText()));
        }

        private void OnEnableCompya()
        {
            mode = LineupMode.None;
            orderFirstPick = null;
            if (infoPopupRoot != null) infoPopupRoot.SetActive(false);
            Deselect();
            ShowCompyaTab(compyaTab, refresh: false);
        }

        // ------------------------------------------------------------------ 탭 / 툴바

        public void ShowCompyaTab(CompyaTab tab) => ShowCompyaTab(tab, refresh: true);

        private void ShowCompyaTab(CompyaTab tab, bool refresh)
        {
            compyaTab = tab;
            if (batterPage != null) batterPage.SetActive(tab == CompyaTab.Batter);
            if (pitcherPage != null) pitcherPage.SetActive(tab == CompyaTab.Pitcher);
            if (storagePage != null) storagePage.SetActive(tab == CompyaTab.Storage);
            PaintTab(batterTabButton, tab == CompyaTab.Batter);
            PaintTab(pitcherTabButton, tab == CompyaTab.Pitcher);
            PaintTab(storageTabButton, tab == CompyaTab.Storage);
            mode = LineupMode.None;
            orderFirstPick = null;
            Deselect();
            SetFullView(fullView, refresh: false);
            if (refresh) RefreshRoster();
            else UpdateToolbarHint();
        }

        private void SetFullView(bool on) => SetFullView(on, refresh: true);

        private void SetFullView(bool on, bool refresh)
        {
            fullView = on;
            if (diamondRoot != null) diamondRoot.SetActive(!on);
            if (fullViewRoot != null) fullViewRoot.SetActive(on);
            PaintToolbar(basicViewButton, !on);
            PaintToolbar(fullViewButton, on);
            if (refresh) RefreshRoster();
        }

        private void ToggleMode(LineupMode next)
        {
            if (compyaTab == CompyaTab.Storage) ShowCompyaTab(CompyaTab.Batter);
            if (next == LineupMode.BattingOrder && compyaTab != CompyaTab.Batter) ShowCompyaTab(CompyaTab.Batter);
            mode = mode == next ? LineupMode.None : next;
            orderFirstPick = null;
            Deselect();
            PaintToolbar(defenseChangeButton, mode == LineupMode.Defense);
            PaintToolbar(battingOrderButton, mode == LineupMode.BattingOrder);
            UpdateToolbarHint();
            RefreshRoster();
        }

        private static void PaintToolbar(Button button, bool active)
        {
            if (button == null || button.targetGraphic == null) return;
            button.targetGraphic.color = active ? new Color(0.13f, 0.3f, 0.72f) : new Color(0.95f, 0.96f, 0.98f);
            var label = button.GetComponentInChildren<Text>(true);
            if (label != null) label.color = active ? Color.white : new Color(0.12f, 0.13f, 0.18f);
        }

        private void UpdateToolbarHint(string message = null)
        {
            if (toolbarHintText == null) return;
            toolbarHintText.text = message ?? (mode switch
            {
                LineupMode.BattingOrder => orderFirstPick == null
                    ? "<color=#FFD54A>타순 변경</color>: 순서를 바꿀 첫 번째 주전 타자를 누르십시오."
                    : $"<color=#FFD54A>타순 변경</color>: {orderFirstPick.Template.PlayerName}와 바꿀 타자를 누르십시오.",
                LineupMode.Defense => "<color=#FFD54A>수비 위치 변경</color>: 포지션을 누르면 그 자리에 들어갈 수 있는 선수 목록이 바로 열립니다.",
                _ => compyaTab == CompyaTab.Storage ? "보관 선수를 누르면 [상세 정보] / [선수 관리] / [라인업 투입]을 할 수 있습니다."
                    : "선수를 누르면 하단에 [상세 정보] / [선수 관리] / [교체] 메뉴가 열립니다.",
            });
        }

        // ------------------------------------------------------------------ 그리기

        private void RefreshCompya(IReadOnlyList<Player> roster)
        {
            ClearCards(spawnedStorageCards);
            selectionFrames.Clear();
            var gm = GameManager.Instance;
            var nativeSize = CardHolderFit.NativeSizeOf(cardPrefab);

            var lineup = LineupView.BuildLineup(roster, gm.BattingOrderOverride);
            var bench = LineupView.BuildBench(roster);
            foreach (var entry in fullView ? Enumerable.Empty<LineupView.Entry>() : lineup) // 보이는 쪽만 그린다(선택 테두리가 숨은 카드에 걸리지 않게)
            {
                int index = (int)entry.Position;
                var holder = diamondSlots != null && index < diamondSlots.Length ? diamondSlots[index] : null;
                if (holder != null) SpawnCompyaCell(entry, holder, spawnedBatterCards, nativeSize, entry.Header.Split(' ')[0], true, stretch: true);
            }
            foreach (var entry in bench.Where(e => !e.IsExtra || e.Player != null))
                SpawnCompyaCell(entry, benchContainer, spawnedBenchCards, nativeSize, "", false, stretch: false);
            if (fullViewContainer != null && fullView)
            {
                foreach (var entry in lineup.OrderBy(e => e.BattingOrder == 0 ? 99 : e.BattingOrder))
                    SpawnCompyaCell(entry, fullViewContainer, spawnedBatterCards, nativeSize, $"{entry.Header.Split(' ')[0]}", true, stretch: false);
            }

            var (starters, bullpen) = LineupView.BuildPitchers(roster);
            foreach (var entry in starters) SpawnCompyaCell(entry, startingPitcherContainer, spawnedPitcherCards, nativeSize, entry.Header, false, stretch: false);
            foreach (var entry in bullpen)
            {
                int column = entry.IsExtra ? 2 : LineupView.BullpenGroups.ToList().FindIndex(g => g.Group == entry.Group);
                var container = bullpenColumns != null && column >= 0 && column < bullpenColumns.Length ? bullpenColumns[column] : null;
                SpawnCompyaCell(entry, container != null ? container : bullpenContainer, spawnedPitcherCards, nativeSize,
                    entry.IsExtra ? "추가" : entry.Header + (entry.Header.Any(char.IsDigit) ? "번" : ""), false, stretch: false);
            }

            // [TASK-KBO-184] 보유 선수 리스트(구 [선수 관리] 탭) - 범위/구단/포지션/등급 필터 + 정렬. 라인업 선수는 "라인업" 라벨로 구분한다.
            var storage = compyaTab == CompyaTab.Storage ? storageFilter.Apply(gm.Inventory, roster, gm.FavoriteTeam) : new List<Player>();
            var inLineup = new HashSet<Player>(roster);
            foreach (var player in storage.Take(StorageDisplayLimit))
            {
                bool lineupCard = inLineup.Contains(player);
                var entry = new LineupView.Entry { Player = player, Header = player.Template.IsPitcher ? RoleShort(player.Template.PitcherRole) : RosterSlotLayout.PositionLabel(player.Template.BatterPosition) };
                SpawnCompyaCell(entry, storageContainer, spawnedStorageCards, nativeSize, lineupCard ? "라인업" : "", false, stretch: false, isStorage: !lineupCard);
            }
            if (storageInfoText != null)
                storageInfoText.text = $"{storageFilter.ScopeLabel} {storage.Count}명 · {storageFilter.Summary}" + (storage.Count > StorageDisplayLimit ? $" (상위 {StorageDisplayLimit}명 표시)" : "");
            RefreshStorageFilterLabels();

            if (batterTabButton != null) CompyaUiKit.SetButtonText(batterTabButton, "타자");
            if (pitcherTabButton != null) CompyaUiKit.SetButtonText(pitcherTabButton, "투수");

            // 선택 유지(교체 후에도 같은 카드가 보이면 강조 유지), 사라졌으면 트레이를 닫는다.
            if (selectedPlayer != null)
            {
                bool visible = selectionFrames.ContainsKey(selectedPlayer);
                if (visible) ShowSelection(selectedPlayer); else Deselect();
            }
            UpdateToolbarHint();
        }

        private void BindStorageFilter(Button button, System.Action cycle)
        {
            if (button == null) return;
            button.onClick.AddListener(() =>
            {
                cycle();
                RefreshRoster();
            });
        }

        private void RefreshStorageFilterLabels()
        {
            CompyaUiKit.SetButtonText(storageScopeButton, storageFilter.ScopeLabel);
            CompyaUiKit.SetButtonText(storageTeamButton, storageFilter.Team == Team.None ? "전체 구단" : CompyaUiKit.ShortName(storageFilter.Team));
            CompyaUiKit.SetButtonText(storagePositionButton, storageFilter.PositionLabel);
            CompyaUiKit.SetButtonText(storageGradeButton, storageFilter.GradeLabel);
            CompyaUiKit.SetButtonText(storageSortButton, storageFilter.SortLabel);
        }

        private static string RoleShort(PitcherRole role) => role switch
        {
            PitcherRole.StartingPitcher => "SP",
            PitcherRole.Closer => "CP",
            _ => "RP",
        };

        /// <summary>컴프야 슬롯 칸: 카드 위 라벨(포지션 약어/보직) + 카드(디자인 크기 유지 스케일) + 좌측 다이아몬드 타순 + 우상단 구단 로고 +
        /// 강화 뱃지 + 선택 테두리. 이름은 카드 하단 띠에 "이름'연도"로 바꿔 쓴다.</summary>
        private void SpawnCompyaCell(LineupView.Entry entry, Transform container, List<PlayerCardUI> tracking, Vector2 nativeSize,
            string label, bool showOrder, bool stretch, bool isStorage = false)
        {
            if (container == null) return;
            var cell = new GameObject($"Slot_{entry.Header}", typeof(RectTransform), typeof(Image), typeof(Button));
            var cellRect = (RectTransform)cell.transform;
            cellRect.SetParent(container, false);
            if (stretch) { cellRect.anchorMin = Vector2.zero; cellRect.anchorMax = Vector2.one; cellRect.offsetMin = cellRect.offsetMax = Vector2.zero; }
            var hit = cell.GetComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            var button = cell.GetComponent<Button>();
            button.targetGraphic = hit;
            spawnedCells.Add(cell);

            bool hasLabel = !string.IsNullOrEmpty(label);
            float labelTop = hasLabel ? 0.88f : 1f;
            if (hasLabel)
            {
                var labelRect = CompyaUiKit.Norm(cellRect, "PositionLabel", 0f, 0.885f, 1f, 1f);
                bool pitcherHeader = entry.Kind == RosterSlotLayout.SlotKind.Pitcher;
                if (pitcherHeader) CompyaUiKit.Paint(labelRect, entry.Role == PitcherRole.StartingPitcher ? new Color(0.13f, 0.3f, 0.75f) : new Color(0.36f, 0.38f, 0.44f));
                CellLabel(labelRect, label, pitcherHeader ? TextAnchor.MiddleCenter : TextAnchor.LowerLeft, pitcherHeader ? Color.white : new Color(0.16f, 0.2f, 0.3f), true);
            }

            var holderRect = CompyaUiKit.Norm(cellRect, "CardHolder", 0f, 0f, 1f, labelTop - 0.005f);
            var holder = holderRect.gameObject.AddComponent<CardHolderFit>();
            holder.Configure(nativeSize, 1f);

            if (entry.Player == null)
            {
                var empty = CompyaUiKit.Norm(holderRect, "Empty", 0.12f, 0.04f, 0.88f, 0.96f);
                CompyaUiKit.Paint(empty, new Color(0.25f, 0.28f, 0.36f, 0.75f));
                CellLabel(empty, $"{entry.Header}\n\n<b>[+ 선수 배치]</b>", TextAnchor.MiddleCenter, Color.white, false);
                var slot = entry.ToPlacementSlot();
                button.onClick.AddListener(() => OpenPlacementPopup(slot));
                return;
            }

            var player = entry.Player;
            var card = SpawnCard(player, holderRect, tracking);
            if (card != null)
            {
                holder.Place((RectTransform)card.transform);
                foreach (var graphic in card.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
                if (card.transform.Find("NameText")?.GetComponent<Text>() is Text nameText)
                    nameText.text = player.Template.SeasonYear > 0 ? $"{player.Template.PlayerName}'{player.Template.SeasonYear % 100:00}" : player.Template.PlayerName;
            }

            // 카드 영역(가로 비율 유지) 기준 장식 - 홀더 안에 같은 비율 박스를 만들어 카드와 겹치게 둔다.
            var deco = CompyaUiKit.Norm(holderRect, "Deco", 0f, 0f, 1f, 1f);
            var fitter = deco.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = nativeSize.x / nativeSize.y;

            var logoRect = CompyaUiKit.Norm(deco, "TeamLogo", 0.7f, 0.8f, 0.97f, 0.97f);
            TeamLogoSprites.Apply(logoRect.gameObject.AddComponent<Image>(), player.Template.Team);
            logoRect.GetComponent<Image>().raycastTarget = false;

            if (showOrder && entry.BattingOrder > 0)
            {
                var diamond = CompyaUiKit.Norm(deco, "OrderDiamond", 0.02f, 0.52f, 0.28f, 0.7f);
                var diamondFit = diamond.gameObject.AddComponent<AspectRatioFitter>();
                diamondFit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                diamondFit.aspectRatio = 1f;
                var shape = CompyaUiKit.Norm(diamond, "Shape", 0.15f, 0.15f, 0.85f, 0.85f);
                CompyaUiKit.Paint(shape, mode == LineupMode.BattingOrder && orderFirstPick == player ? SelectGold : new Color(0.75f, 0.12f, 0.2f));
                shape.localRotation = Quaternion.Euler(0f, 0f, 45f);
                CellLabel(diamond, $"<b>{entry.BattingOrder}</b>", TextAnchor.MiddleCenter, Color.white, true);
            }

            if (player.ReinforceLevel > 0)
            {
                var badge = CompyaUiKit.Norm(deco, "Reinforce", 0.02f, 0.36f, 0.3f, 0.5f);
                CompyaUiKit.Paint(badge, new Color(0.55f, 0.18f, 0.62f, 0.95f));
                CellLabel(badge, $"+{player.ReinforceLevel}", TextAnchor.MiddleCenter, Color.white, true);
            }

            var frame = CompyaUiKit.Norm(deco, "SelectFrame", -0.03f, -0.02f, 1.03f, 1.02f);
            var frameImage = CompyaUiKit.Paint(frame, new Color(1f, 0.83f, 0.2f, 0.08f));
            var outline = frame.gameObject.AddComponent<Outline>();
            outline.effectColor = SelectGold;
            outline.effectDistance = new Vector2(5f, -5f);
            frame.gameObject.SetActive(false);
            selectionFrames[player] = frame.gameObject;
            frameImage.raycastTarget = false;

            var captured = entry;
            button.onClick.AddListener(() => OnSlotClicked(captured, isStorage));
        }

        // ------------------------------------------------------------------ 슬롯 클릭 / 선택 트레이

        private void OnSlotClicked(LineupView.Entry entry, bool isStorage)
        {
            var player = entry.Player;
            if (player == null) return;

            if (mode == LineupMode.Defense && !isStorage)
            {
                OpenSwapPopup(player); // 수비 위치 변경 모드: 해당 자리 후보를 바로 연다
                return;
            }

            if (mode == LineupMode.BattingOrder)
            {
                HandleBattingOrderClick(entry);
                return;
            }

            selectedPlayer = player;
            ShowSelection(player);
            ShowTray(player, isStorage);
        }

        private void HandleBattingOrderClick(LineupView.Entry entry)
        {
            if (entry.Kind != RosterSlotLayout.SlotKind.StarterBatter || entry.BattingOrder <= 0)
            {
                UpdateToolbarHint("<color=#FFD54A>타순 변경</color>: 주전 타자(다이아몬드)만 타순을 바꿀 수 있습니다.");
                return;
            }
            var gm = GameManager.Instance;
            if (orderFirstPick == null || orderFirstPick == entry.Player)
            {
                orderFirstPick = orderFirstPick == entry.Player ? null : entry.Player;
                RefreshRoster();
                return;
            }

            var lineup = LineupView.BuildLineup(gm.Roster, gm.BattingOrderOverride)
                .Where(e => e.Player != null).OrderBy(e => e.BattingOrder).Select(e => e.Player).ToList();
            var swapped = LineupOrder.Swap(lineup, orderFirstPick, entry.Player);
            string a = orderFirstPick.Template.PlayerName, b = entry.Player.Template.PlayerName;
            orderFirstPick = null;
            if (swapped != null)
            {
                gm.SetBattingOrderOverride(swapped);
                RefreshRoster();
                UpdateToolbarHint($"<color=#FFD54A>타순 변경</color>: {a} ↔ {b} 완료 (경기 타순에 바로 반영). 계속 바꾸거나 [타순 변경]을 다시 눌러 종료하십시오.");
            }
        }

        private void ShowSelection(Player player)
        {
            foreach (var pair in selectionFrames) if (pair.Value != null) pair.Value.SetActive(pair.Key == player);
        }

        private void ShowTray(Player player, bool isStorage)
        {
            if (trayRoot == null) { OpenSwapPopup(player); return; } // 트레이가 없는 씬: 기존 동작
            if (defaultBarRoot != null) defaultBarRoot.SetActive(false);
            trayRoot.SetActive(true);

            ClearCards(spawnedTrayCards);
            if (trayCardHolder != null && cardPrefab != null)
            {
                if (!trayCardHolder.TryGetComponent<CardHolderFit>(out var fit)) fit = trayCardHolder.gameObject.AddComponent<CardHolderFit>();
                fit.Configure(CardHolderFit.NativeSizeOf(cardPrefab), 1f);
                var card = SpawnCard(player, trayCardHolder, spawnedTrayCards);
                if (card != null)
                {
                    fit.Place((RectTransform)card.transform);
                    foreach (var graphic in card.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
                }
            }

            var t = player.Template;
            if (trayTitleText != null)
                trayTitleText.text = $"<b>{CompyaMatchView.DisplayName(player)}</b>  {CompyaUiKit.ShortName(t.Team)} · {CompyaMatchView.PositionLabel(player)} · " +
                                     $"OVR <color=#FFD54A>{player.CalculateOVR(false)}</color> · SD {player.SetDeckScore}" +
                                     (isStorage ? "  <color=#9AA6BF>(보관)</color>" : "");

            var chips = BuildChips(player);
            for (int i = 0; i < 3; i++)
            {
                var (icon, title, sub) = i < chips.Count ? chips[i] : ("", "", "");
                SetChip(trayChipIcons, i, icon);
                SetChip(trayChipTitles, i, title);
                SetChip(trayChipSubs, i, sub);
            }

            if (traySwapButton != null) CompyaUiKit.SetButtonText(traySwapButton, isStorage ? "라인업 투입" : "교체");
        }

        private static void SetChip(Text[] texts, int index, string value)
        {
            if (texts != null && index < texts.Length && texts[index] != null) texts[index].text = value;
        }

        /// <summary>트레이 중앙 3칸: 보유 스킬(이름 · 티어) 우선, 모자라면 핵심 능력치(타자 파워/정확/선구, 투수 구위/제구/변화).</summary>
        private List<(string Icon, string Title, string Sub)> BuildChips(Player player)
        {
            var chips = new List<(string, string, string)>();
            foreach (var skill in player.AcquiredSkillIds.Where(s => !string.IsNullOrEmpty(s)).Take(3))
            {
                var tier = skillDB != null ? skillDB.FindTier(skill) : null;
                chips.Add((skill.Substring(0, 1), skill, tier.HasValue ? $"스킬 {tier.Value}" : "보유 스킬"));
            }
            var t = player.Template;
            var stats = t.IsPitcher
                ? new[] { ("구위", t.PitcherStats.Stuff), ("제구", t.PitcherStats.Control), ("변화", t.PitcherStats.Movement) }
                : new[] { ("파워", t.BatterStats.Power), ("정확", t.BatterStats.Contact), ("선구", t.BatterStats.Discipline) };
            foreach (var (name, value) in stats)
            {
                if (chips.Count >= 3) break;
                chips.Add((name.Substring(0, 1), name, $"LV {value}"));
            }
            return chips;
        }

        private void Deselect()
        {
            selectedPlayer = null;
            foreach (var frame in selectionFrames.Values) if (frame != null) frame.SetActive(false);
            ClearCards(spawnedTrayCards);
            if (trayRoot != null) trayRoot.SetActive(false);
            if (defaultBarRoot != null) defaultBarRoot.SetActive(true);
        }

        private void OpenDetail()
        {
            if (selectedPlayer == null) return;
            var detail = FindAnyObjectByType<PlayerDetailUIController>(FindObjectsInactive.Include);
            if (detail == null) { UpdateToolbarHint("상세 정보 화면(PlayerDetailUIController)이 씬에 없습니다."); return; }
            detail.Show(selectedPlayer);
        }

        private void OpenManagement()
        {
            if (selectedPlayer == null) return;
            var management = FindAnyObjectByType<PlayerManagementUIController>(FindObjectsInactive.Include);
            if (management == null) { UpdateToolbarHint("선수 관리 화면(PlayerManagementUIController)이 씬에 없습니다."); return; }
            management.Show(selectedPlayer);
        }

        /// <summary>[교체] - 이 버튼을 눌렀을 때만 교체 후보 팝업을 연다. 보관 선수면 "라인업 투입"(같은 인물 카드 → 같은 자리 최저 OVR 순으로 교체).</summary>
        private void SwapSelected()
        {
            var gm = GameManager.Instance;
            if (selectedPlayer == null || gm == null) return;
            if (gm.Roster.Contains(selectedPlayer))
            {
                OpenSwapPopup(selectedPlayer);
                return;
            }

            var incoming = selectedPlayer;
            var roster = gm.Roster;
            var outgoing = roster.Where(p => RosterSwapRules.CanSwap(roster, p, incoming))
                .OrderByDescending(p => RosterSwapRules.PersonKey(p) == RosterSwapRules.PersonKey(incoming))
                .ThenBy(p => p.CalculateOVR(false))
                .FirstOrDefault();
            string name = incoming.Template.PlayerName;
            if (outgoing != null && gm.SwapRosterPlayer(outgoing, incoming))
            {
                UpdateToolbarHint($"라인업 투입: {name} ← {outgoing.Template.PlayerName} (보관함으로)");
            }
            else if (!RosterSwapRules.HasSamePerson(roster, incoming) && gm.AddPlayerToRoster(incoming))
            {
                UpdateToolbarHint($"라인업 투입: {name} (빈 자리)");
            }
            else
            {
                UpdateToolbarHint($"{name}: 교체할 수 있는 같은 포지션/보직 자리가 없거나 같은 인물이 이미 라인업에 있습니다.");
                return;
            }
            Deselect();
            RefreshRoster();
            if (setDeckOptionController != null) setDeckOptionController.Refresh();
        }

        // ------------------------------------------------------------------ 하단 기본 바(세트덱 스코어)

        private void RefreshCompyaBar()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            string favorite = gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null;
            var setDeck = GameManager.EvaluateSetDeck(gm.Roster.ToList(), favorite, gm.SetDeckSelection);
            if (teamOvrText != null) teamOvrText.text = $"<size=70%>OVR</size> <b>{gm.CalculateTeamOVR()}</b>";
            if (setDeckScoreText != null)
                setDeckScoreText.text = $"{CompyaUiKit.ShortName(setDeck.DeckTeam)} <color=#3D7BFF><b>{setDeck.Score}</b></color> <size=70%>POINT</size>";

            var (start, markers) = GaugeWindow(setDeck.Score);
            for (int i = 0; i < 6; i++)
            {
                int marker = markers[i];
                bool reached = setDeck.Score >= marker;
                bool isBracket = SetDeckBuffTable.Brackets.Any(b => b.Threshold == marker);
                if (gaugeMarkerTexts != null && i < gaugeMarkerTexts.Length && gaugeMarkerTexts[i] != null) gaugeMarkerTexts[i].text = marker.ToString();
                if (gaugeIconTexts != null && i < gaugeIconTexts.Length && gaugeIconTexts[i] != null)
                    gaugeIconTexts[i].text = !isBracket ? "-" : reached ? "✔" : "잠김";
                if (gaugeIconImages != null && i < gaugeIconImages.Length && gaugeIconImages[i] != null)
                    gaugeIconImages[i].color = reached ? new Color(0.15f, 0.55f, 0.7f) : new Color(0.22f, 0.24f, 0.3f);
            }
            if (setDeckGaugeFillImage != null)
            {
                setDeckGaugeFillImage.fillAmount = GaugeFill(setDeck.Score, start);
                setDeckGaugeFillImage.color = CyanAccent;
            }
        }

        /// <summary>마커 i는 게이지 폭의 (i + 0.5) / 6 지점(Setup 배치와 동일) - 스코어 위치까지 채운다.</summary>
        public static float GaugeFill(int score, int start) => Mathf.Clamp01(((score - start) / 5f + 0.5f) / 6f);

        /// <summary>게이지 구간 6칸(5P 간격) - 현재 스코어가 두 번째 칸 근처에 오도록 잡되 30~200P 범위를 넘지 않는다.</summary>
        public static (int Start, int[] Markers) GaugeWindow(int score)
        {
            int start = Mathf.Clamp((score / 5) * 5, 30 + 5, SetDeckBuffTable.FinalGoalScore - 25);
            return (start, Enumerable.Range(0, 6).Select(i => start + i * 5).ToArray());
        }

        // ------------------------------------------------------------------ 정보 팝업

        private void ShowInfo(string title, string body)
        {
            if (infoPopupRoot == null) return;
            if (infoTitleText != null) infoTitleText.text = title;
            if (infoBodyText != null) infoBodyText.text = body;
            infoPopupRoot.SetActive(true);
            infoPopupRoot.transform.SetAsLastSibling();
        }

        private static string BuildHelpText() =>
            "라인업 27인(주전 타자 9 · 후보 타자 6 · 선발 5 · 불펜 7)의 <b>선택 구단 카드</b>(골든글러브는 구단 무관) 개인 스코어를 합산합니다.\n\n" +
            "30P부터 구간마다 능력치 버프가 누적되고, 150P(1차 목표) · 185/190P(핵심 버프) · 200P(최종 목표)가 주요 목표입니다.\n" +
            "선택형 구간은 [세트덱 버프 선택]에서 A/B 중 하나를 고를 수 있습니다.\n\n" +
            "[자동 교체]는 선택 구단 선수를 최우선으로 편성하며, 같은 선수의 다른 카드는 한 장만 들어갑니다.";

        private static string BuildLineupInfoText()
        {
            var gm = GameManager.Instance;
            if (gm == null) return "";
            var roster = gm.Roster;
            var lineup = LineupView.BuildLineup(roster, gm.BattingOrderOverride).Where(e => e.Player != null).OrderBy(e => e.BattingOrder).ToList();
            var (starters, bullpen) = LineupView.BuildPitchers(roster);
            int own = roster.Count(p => p.Template.Team == gm.FavoriteTeam);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"팀 OVR {gm.CalculateTeamOVR()} · 1군 {roster.Count}/28 · {CompyaUiKit.ShortName(gm.FavoriteTeam)} 소속 {own}명");
            sb.AppendLine($"타선 평균 OVR {Avg(lineup.Select(e => e.Player))} · 선발 {Avg(starters.Select(e => e.Player))} · 불펜 {Avg(bullpen.Select(e => e.Player))}");
            sb.AppendLine();
            sb.AppendLine("<b>타순</b>" + (gm.BattingOrderOverride.Count > 0 ? " (유저 지정)" : " (기본)"));
            foreach (var e in lineup)
                sb.AppendLine($"{e.BattingOrder}. {e.Player.Template.PlayerName} ({RosterSlotLayout.PositionLabel(e.Position)}) OVR {e.Player.CalculateOVR(false)}");
            return sb.ToString();
        }

        private static string Avg(IEnumerable<Player> players)
        {
            var list = players.Where(p => p != null).ToList();
            return list.Count == 0 ? "-" : list.Average(p => p.CalculateOVR(false)).ToString("F1");
        }

        private static string BuildSynergyText()
        {
            var gm = GameManager.Instance;
            if (gm == null) return "";
            string favorite = gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null;
            var setDeck = GameManager.EvaluateSetDeck(gm.Roster.ToList(), favorite, gm.SetDeckSelection);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{CompyaUiKit.FullName(setDeck.DeckTeam)} {SetDeckUIText.Summary(setDeck)}");
            sb.AppendLine($"합산 카드 {setDeck.CountedPlayers.Count}장 / 세트덱 {setDeck.SlotPlayers.Count}인" +
                          (setDeck.SelectedYear > 0 ? $" · 선택 연도 {setDeck.SelectedYear}" : ""));
            sb.AppendLine();
            sb.AppendLine("<b>적용 중인 구간 버프</b>");
            foreach (var bracket in setDeck.ReachedBrackets.OrderBy(b => b.Threshold))
                sb.AppendLine($"{bracket.Threshold}P · {bracket.Resolve(gm.SetDeckSelection).Label}");
            if (setDeck.ReachedBrackets.Count == 0) sb.AppendLine("아직 도달한 구간이 없습니다.");
            return sb.ToString();
        }
    }
}
