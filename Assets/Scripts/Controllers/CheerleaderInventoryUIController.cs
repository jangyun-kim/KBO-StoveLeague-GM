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
    /// [TASK-KBO-060] 치어리더 관리(보유 목록 + 장착/해제) 화면. GameManager.OwnedCheerleaders를 슬롯 템플릿으로 그린다.
    ///
    /// [TASK-KBO-177, 9:16 모바일 레이아웃 전면 개편] 기존 화면은 헤더/닫기 버튼 없이 거대한 흰 막대만 나열됐다. 이제:
    ///   - 상단: 타이틀 + 보유 수량(ownedCountText) + 닫기, 장착 슬롯 요약(equippedSummaryText - 장착 치어리더,
    ///     세트덱 구단과의 시너지 발동 여부, 컨디션/클러치(시너지 시)·수익/팬심(상시) 효과).
    ///   - 필터 바: 전체 / 구단(누를 때마다 보유 구단 순환) / LIVE / ICON / LEGEND.
    ///   - 목록: 고정 높이 카드(티어 뱃지 · 이름 · 구단·활동기간 · 버프 · 장착/해제 - CheerleaderSlotUI).
    /// 장착 슬롯은 게임 규칙상 1개(GameManager.EquippedCheerleader)다. 레이아웃은 SetupThemeUI178(구 SetupMobileUI177)이 조립한다.
    ///
    /// [TASK-KBO-180] 6인 역할 편성으로 전면 개편: 상단 CheerSquadPanel(3x2 - 1.응원단장 ~ 6.위기 응원)에서 슬롯을 고르면
    /// 아래 보유 목록의 [장착]이 그 슬롯에 배치한다(GameManager.TryEquipCheerleader - 동일 인물 중복 편성 금지).
    /// 이미 어느 슬롯에든 편성된 카드는 [해제]로 그 슬롯에서 뺀다. 요약 줄(equippedSummaryText)은 선택 슬롯·시너지 인원·마지막 안내를 보여 준다.
    /// </summary>
    public class CheerleaderInventoryUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform contentContainer;
        [SerializeField] private GameObject slotPrefab;

        [Header("Navigation")]
        [Tooltip("로비 화면(UIManager.ScreenType.Lobby)으로 돌아가는 버튼. 이 화면 자신은 UIManager.ScreenType.CheerleaderInventory에 등록된다.")]
        [SerializeField] private Button closeButton;

        [Header("Header (TASK-KBO-177)")]
        [SerializeField] private Text ownedCountText;
        [Tooltip("장착 슬롯 + 세트덱 구단 시너지 상태 + 효과 합계.")]
        [SerializeField] private Text equippedSummaryText;
        [Tooltip("보유 치어리더가 없거나 필터 결과가 0명일 때 켜지는 안내 문구.")]
        [SerializeField] private Text emptyText;

        [Header("Filter Bar (TASK-KBO-177)")]
        [SerializeField] private Button filterAllButton;
        [Tooltip("누를 때마다 '구단: 전체 → 보유 구단들 → 전체'로 순환한다.")]
        [SerializeField] private Button filterTeamButton;
        [SerializeField] private Button filterLiveButton;
        [SerializeField] private Button filterIconButton;
        [SerializeField] private Button filterLegendButton;
        [SerializeField] private Color filterActiveColor = new Color(1f, 0.84f, 0f);
        [SerializeField] private Color filterIdleColor = new Color(0.9f, 0.9f, 0.9f);

        [Header("Cheer Squad (TASK-KBO-180)")]
        [Tooltip("6인 역할 편성 3x2 그리드. 비우면 자식에서 자동 탐색한다(SetupCheerSquadUI180이 배치).")]
        [SerializeField] private CheerSquadPanel squadPanel;

        private CheerleaderGrade? tierFilter;
        private Team teamFilter = Team.None;
        private CheerRole selectedRole = CheerRole.Leader;
        private string statusMessage;

        private void Awake()
        {
            if (squadPanel == null) squadPanel = GetComponentInChildren<CheerSquadPanel>(true);
            if (squadPanel != null)
            {
                squadPanel.OnSlotSelected += role =>
                {
                    selectedRole = role;
                    statusMessage = $"{(int)role + 1}.{CheerSquad.RoleName(role)} 슬롯 선택 - 아래 목록에서 [장착]을 누르십시오.";
                    RefreshInventory();
                };
                squadPanel.OnSlotCleared += role => GameManager.Instance?.UnequipCheerleader(role);
            }
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
            if (filterAllButton != null) filterAllButton.onClick.AddListener(() => { tierFilter = null; teamFilter = Team.None; RefreshInventory(); });
            if (filterTeamButton != null) filterTeamButton.onClick.AddListener(CycleTeamFilter);
            if (filterLiveButton != null) filterLiveButton.onClick.AddListener(() => ToggleTier(CheerleaderGrade.LIVE_NORMAL));
            if (filterIconButton != null) filterIconButton.onClick.AddListener(() => ToggleTier(CheerleaderGrade.ICON));
            if (filterLegendButton != null) filterLegendButton.onClick.AddListener(() => ToggleTier(CheerleaderGrade.LEGEND));
        }

        private void OnEnable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnCheerleaderChanged += RefreshInventory;
            }

            RefreshInventory();
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnCheerleaderChanged -= RefreshInventory;
            }
        }

        private void ToggleTier(CheerleaderGrade grade)
        {
            tierFilter = tierFilter == grade ? (CheerleaderGrade?)null : grade;
            RefreshInventory();
        }

        private void CycleTeamFilter()
        {
            var owned = GameManager.Instance?.OwnedCheerleaders;
            var teams = new List<Team> { Team.None };
            if (owned != null) teams.AddRange(owned.Where(c => c != null && c.Team != Team.None).Select(c => c.Team).Distinct().OrderBy(t => t));

            int index = teams.IndexOf(teamFilter);
            teamFilter = teams[(index + 1) % teams.Count];
            RefreshInventory();
        }

        /// <summary>필터 조건(티어 + 구단)에 맞는 보유 치어리더. LIVE 필터는 LIVE_NORMAL/LIVE_EPIC을 함께 포함한다.</summary>
        public static IEnumerable<Cheerleader> ApplyFilter(IEnumerable<Cheerleader> owned, CheerleaderGrade? tier, Team team)
        {
            foreach (var c in owned ?? Enumerable.Empty<Cheerleader>())
            {
                if (c == null) continue;
                if (team != Team.None && c.Team != team) continue;
                if (tier.HasValue)
                {
                    bool isLive = c.Grade == CheerleaderGrade.LIVE_NORMAL || c.Grade == CheerleaderGrade.LIVE_EPIC;
                    if (tier.Value == CheerleaderGrade.LIVE_NORMAL ? !isLive : c.Grade != tier.Value) continue;
                }
                yield return c;
            }
        }

        public void RefreshInventory()
        {
            var gm = GameManager.Instance;
            var owned = gm?.OwnedCheerleaders ?? new List<Cheerleader>();
            var filtered = ApplyFilter(owned, tierFilter, teamFilter)
                .OrderByDescending(c => c.Grade).ThenBy(c => c.Team).ThenBy(c => c.Name).ToList();

            RefreshHeader(gm, owned.Count, filtered.Count);
            RefreshFilterBar();

            if (emptyText != null)
            {
                emptyText.text = owned.Count == 0
                    ? "보유한 치어리더가 없습니다.\n스카우트 > 치어리더 영입에서 영입하십시오."
                    : "조건에 맞는 치어리더가 없습니다. [전체]를 눌러 필터를 해제하십시오.";
                emptyText.gameObject.SetActive(filtered.Count == 0);
            }

            if (contentContainer == null || slotPrefab == null) return;

            for (int i = contentContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(contentContainer.GetChild(i).gameObject);
            }

            if (gm == null) return;

            foreach (var cheerleader in filtered)
            {
                var slotObject = Instantiate(slotPrefab, contentContainer);
                slotObject.SetActive(true); // 템플릿이 비활성으로 보관돼 있어도 복제본은 보이게
                var slot = slotObject.GetComponent<CheerleaderSlotUI>();
                if (slot == null) continue;

                // [TASK-KBO-180] 어느 슬롯에든 편성돼 있으면 "해제"(그 슬롯에서 제거), 아니면 선택 슬롯에 "장착".
                var equippedRole = gm.FindSlotOf(cheerleader);
                slot.Initialize(cheerleader, equippedRole.HasValue,
                    onEquip: target =>
                    {
                        statusMessage = GameManager.Instance.TryEquipCheerleader(selectedRole, target, out var reason)
                            ? $"{(int)selectedRole + 1}.{CheerSquad.RoleName(selectedRole)}에 {target.Name} 배치 완료"
                            : reason;
                        RefreshInventory();
                    },
                    onUnequip: () =>
                    {
                        if (equippedRole.HasValue) GameManager.Instance.UnequipCheerleader(equippedRole.Value);
                    });
            }
        }

        private void RefreshHeader(GameManager gm, int ownedCount, int shownCount)
        {
            if (ownedCountText != null)
            {
                ownedCountText.text = shownCount == ownedCount ? $"보유 {ownedCount}명" : $"보유 {ownedCount}명 (표시 {shownCount}명)";
            }

            if (gm != null && squadPanel != null)
            {
                // [TASK-KBO-180] 6인 편성 그리드 + 요약 줄.
                string favorite = gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null;
                var deck = GameManager.EvaluateSetDeck(gm.Roster.ToList(), favorite, gm.SetDeckSelection).DeckTeam;
                squadPanel.Refresh(gm.CheerSquadSlots, deck, selectedRole);
                if (equippedSummaryText != null)
                {
                    int filled = CheerSquad.Filled(gm.CheerSquadSlots).Count();
                    int active = CheerSquad.Filled(gm.CheerSquadSlots).Count(c => CheerleaderSynergy.IsActive(c, deck));
                    equippedSummaryText.text = $"편성 {filled}/6 · 시너지 발동 {active}명 (세트덱 {deck}) · 배치 대상: {(int)selectedRole + 1}.{CheerSquad.RoleName(selectedRole)}" +
                        (string.IsNullOrEmpty(statusMessage) ? "" : $"\n{statusMessage}");
                }
                return;
            }

            if (equippedSummaryText == null) return;
            var equipped = gm?.EquippedCheerleader;
            if (gm == null || equipped == null || string.IsNullOrEmpty(equipped.InstanceId))
            {
                equippedSummaryText.text = "장착 슬롯: 비어 있음 - 아래 목록에서 [장착]을 누르십시오.";
                return;
            }

            string favoriteTeamName = gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null;
            var deckTeam = GameManager.EvaluateSetDeck(gm.Roster.ToList(), favoriteTeamName, gm.SetDeckSelection).DeckTeam;
            bool synergy = CheerleaderSynergy.IsActive(equipped, deckTeam);
            string affiliation = string.IsNullOrEmpty(equipped.AffiliationLabel) ? "구단 무관" : equipped.AffiliationLabel;
            string synergyLine = synergy
                ? $"시너지 발동 (세트덱 {deckTeam}) · 홈 경기 컨디션 +{equipped.ConditionBuff} / 클러치 x{equipped.ClutchMultiplier:F2}"
                : $"시너지 미발동 (세트덱 {deckTeam}) · 경기 버프 없음";

            equippedSummaryText.text =
                $"장착 슬롯: [{CheerleaderSlotUI.TierLabel(equipped.Grade)}] {equipped.Name} · {affiliation}\n" +
                $"{synergyLine}\n상시 효과 · 관중 수익 x{equipped.EconomicBonusRate:F2} / 팬심 방어 +{equipped.SentimentDefense}";
        }

        private void RefreshFilterBar()
        {
            StyleFilter(filterAllButton, tierFilter == null && teamFilter == Team.None, null);
            StyleFilter(filterTeamButton, teamFilter != Team.None, teamFilter == Team.None ? "구단: 전체" : $"구단: {teamFilter}");
            StyleFilter(filterLiveButton, tierFilter == CheerleaderGrade.LIVE_NORMAL, null);
            StyleFilter(filterIconButton, tierFilter == CheerleaderGrade.ICON, null);
            StyleFilter(filterLegendButton, tierFilter == CheerleaderGrade.LEGEND, null);
        }

        private void StyleFilter(Button button, bool active, string label)
        {
            if (button == null) return;
            if (button.targetGraphic != null) button.targetGraphic.color = active ? filterActiveColor : filterIdleColor;
            if (label != null)
            {
                var text = button.GetComponentInChildren<Text>(true);
                if (text != null) text.text = label;
            }
        }

        private static bool IsSameCheerleader(Cheerleader a, Cheerleader b)
        {
            if (a == null || b == null) return false;
            if (string.IsNullOrEmpty(a.InstanceId) || string.IsNullOrEmpty(b.InstanceId)) return false;

            return a.InstanceId == b.InstanceId;
        }
    }
}
