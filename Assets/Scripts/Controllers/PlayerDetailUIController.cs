using System;
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
    /// [TASK-KBO-147] 인벤토리 상세 정보 패널. TASK-138 시절의 텍스트 나열형 구 DetailPanel(강화/각성
    /// 수치와 스킬 목록을 세로로 늘어놓기만 하던 구성 - `InventoryUIController.detailPanelRoot`)을
    /// 완전히 폐기하고, 상단 큰 카드 미리보기 + 하단 4탭([기본 스탯]/[특이폼·페이스]/[핫·콜드존]/[스킬])
    /// 구조로 전면 개편했다. 사용자가 제공한 타 게임 레퍼런스(카드 상세 스와이프 4페이지)를 탭 버튼
    /// 방식으로 재구성한 것이다. `InventoryUIController.ShowDetail()`이 이 컴포넌트의 `Show(Player)`를
    /// 호출하는 것으로 트리거되며, 이 컨트롤러 자체는 인벤토리에 종속되지 않는 독립 컴포넌트다.
    ///
    /// [명령서 5항] 핫/콜드존 실측 타격 데이터와 특이폼 애니메이션은 아직 백엔드가 없다 - 이 두 탭은
    /// 각각 더미(중립 색상 그리드 / "구현 예정" 안내 문구)로만 채운다. 실제 데이터가 있는 두 탭
    /// ([기본 스탯] - PlayerTemplate 세부 스탯, [스킬] - AcquiredSkillIds)만 실데이터를 표시한다.
    /// [기본 스탯] 탭은 우리 데이터 모델이 실제로 갖고 있는 필드 그대로(타자: 파워/정확/선구/주력/수비
    /// 5종, 투수: 구위/구속/변화/제구/체력 5종)를 보여준다 - 레퍼런스 이미지의 "인내"/"주루" 같은
    /// 우리 모델에 없는 이름을 억지로 만들어 채우지 않았다(명령서 5항 "무리하게 백엔드를 창조하지 말 것"과
    /// 동일한 취지).
    ///
    /// [TASK-KBO-150] 기획 흐름 역전 - 이제 인벤토리 카드 클릭 시 허브가 아니라 이 상세 창이 1차로
    /// 열린다(`InventoryUIController.OpenPlayerDetail()` 참고). 대신 이 패널 하단에 [선수 관리] 버튼
    /// (`playerManagementButton`)을 신설해, 클릭하면 `PlayerManagementUIController.Show(currentPlayer)`를
    /// 호출해 허브로 넘어간다 - 명령서는 `UIManager.Instance.ShowScreen(ScreenType.PlayerManagementHub)`를
    /// 예시로 들었으나, 그 원시 호출만으로는 허브의 `currentPlayer`(타겟 카드 미리보기 등)가 갱신되지
    /// 않는다 - `PlayerManagementUIController.Show()`가 내부에서 그 `ShowScreen()` 호출까지 이미 포함하고
    /// 있으므로(TASK-145) 이 메서드를 직접 호출하는 쪽이 명령서 의도(허브로 정상 진입)에 더 정확히
    /// 부합한다. TASK-148이 만든 "허브 상단 초상화 클릭 → 이 상세 창" 역방향 경로(뒤로 가기)는 명령서
    /// 5항 지시대로 전혀 건드리지 않았다.
    ///
    /// [TASK-KBO-186] 탭형 구성(늘어난 배경 이미지 · 밝은 회색 박스 위 흰 글씨 · 같은 좌표에 겹친 노란 능력치 텍스트 5개 · 12~16pt 글씨)을
    /// 화면에서 철거하고 PlayerDetailLayout186(1080×1920 4단 카드 레이아웃)으로 전면 재구축했다. 아래 탭/텍스트 직렬화 필드는 구 씬 호환용으로만
    /// 남아 있고(Build가 구 자식을 모두 숨긴다), 표시는 전부 새 레이아웃이 맡는다. [선수 관리] → 성장 센터 이동 흐름은 그대로다.
    /// </summary>
    public class PlayerDetailUIController : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject panelRoot;

        [Header("Top Summary")]
        [Tooltip("상단 대형 카드 미리보기. PlayerCardUI를 그대로 재사용해 초상화/등급/성급/OVR을 " +
                 "일관되게 표시한다(TASK-KBO-147, 초상화 로딩은 PlayerCardUI.Setup()에 이미 구현됨).")]
        [SerializeField] private PlayerCardUI previewCard;
        [SerializeField] private Text nameText;
        [SerializeField] private Text teamGradeText;

        [Header("Tabs")]
        [SerializeField] private Button statsTabButton;
        [SerializeField] private Button specialFormTabButton;
        [SerializeField] private Button hotColdTabButton;
        [SerializeField] private Button skillTabButton;

        [SerializeField] private GameObject statsContentPanel;
        [SerializeField] private GameObject specialFormContentPanel;
        [SerializeField] private GameObject hotColdContentPanel;
        [SerializeField] private GameObject skillContentPanel;

        [Header("Tab Visual (TASK-KBO-152 - 활성/비활성 시각 구분 강화)")]
        [Tooltip("선택된 탭 버튼의 배경색 - 눈에 띄는 강조색을 쓴다.")]
        [SerializeField] private Color activeTabColor = new Color(0.20f, 0.55f, 0.95f);
        [Tooltip("선택되지 않은 탭 버튼의 배경색 - 어두운 회색으로 확실히 죽인다.")]
        [SerializeField] private Color inactiveTabColor = new Color(0.30f, 0.30f, 0.32f);
        [Tooltip("선택된 탭 라벨 텍스트 색상.")]
        [SerializeField] private Color activeTabTextColor = Color.white;
        [Tooltip("선택되지 않은 탭 라벨 텍스트 색상 - 배경과 마찬가지로 흐리게.")]
        [SerializeField] private Color inactiveTabTextColor = new Color(0.65f, 0.65f, 0.65f);

        [Header("[기본 스탯] 탭 내용")]
        [SerializeField] private Text reinforceText;
        [SerializeField] private Text awakenText;
        [Tooltip("[TASK-KBO-183] [기본 OVR / 현재 성장(+N) / 시너지(+M) / 최대 잠재 OVR] 요약.")]
        [SerializeField] private Text growthSummaryText;
        [Tooltip("정확히 5칸 - 타자/투수 각각의 실제 세부 스탯 5종을 순서대로 채운다(RefreshStatsTab() 참고).")]
        [SerializeField] private Text[] statRowTexts;

        [Header("[스킬] 탭 내용")]
        [SerializeField] private Text skillListText;

        [Header("Action (TASK-KBO-150)")]
        [Tooltip("[선수 관리] 버튼 - 클릭하면 선수 관리 허브로 넘어간다.")]
        [SerializeField] private Button playerManagementButton;
        [Tooltip("[선수 관리] 버튼이 여는 허브 컨트롤러 참조.")]
        [SerializeField] private PlayerManagementUIController playerManagementUIController;

        [Header("닫기")]
        [SerializeField] private Button closeButton;

        [Header("TASK-KBO-186 4단 레이아웃 (Setup이 KBO Dia Gothic · SkillDB 주입)")]
        [SerializeField] private Font boldFont;
        [SerializeField] private Font regularFont;
        [SerializeField] private SkillDB skillDB;

        private PlayerDetailLayout186 layout;
        public PlayerDetailLayout186 Layout => layout;

        /// <summary>내부 닫기 버튼(X)을 눌러 패널이 닫혔을 때 발생 - InventoryUIController가 구독해
        /// 메인 닫기 버튼(로비로 돌아가기)을 다시 보이게 한다.</summary>
        public event Action OnClosed;

        private Player currentPlayer;

        private enum Tab { Stats, SpecialForm, HotCold, Skill }
        private Tab currentTab = Tab.Stats;

        private void Awake()
        {
            if (statsTabButton != null) statsTabButton.onClick.AddListener(() => SelectTab(Tab.Stats));
            if (specialFormTabButton != null) specialFormTabButton.onClick.AddListener(() => SelectTab(Tab.SpecialForm));
            if (hotColdTabButton != null) hotColdTabButton.onClick.AddListener(() => SelectTab(Tab.HotCold));
            if (skillTabButton != null) skillTabButton.onClick.AddListener(() => SelectTab(Tab.Skill));
            if (playerManagementButton != null) playerManagementButton.onClick.AddListener(OnClickPlayerManagement);
            if (closeButton != null) closeButton.onClick.AddListener(HandleCloseClicked);

            BuildLayout();
            Hide();
        }

        /// <summary>Setup 메뉴 전용 - 에디터에서 폰트/스킬 DB를 주입한다.</summary>
        public void Configure(Font bold, Font regular, SkillDB skills)
        {
            boldFont = bold;
            regularFont = regular;
            if (skills != null) skillDB = skills;
        }

        /// <summary>[TASK-KBO-186] panelRoot 아래 4단 레이아웃(Detail186)을 새로 만들고 구 탭형 자식은 숨긴다(Setup과 런타임 Awake 공용).</summary>
        public void BuildLayout()
        {
            if (panelRoot == null || !(panelRoot.transform is RectTransform panel)) return;
            layout = new PlayerDetailLayout186(boldFont, regularFont);
            layout.Build(panel, previewCard, transform);
            layout.CloseX.onClick.AddListener(HandleCloseClicked);
            layout.CloseButton.onClick.AddListener(HandleCloseClicked);
            layout.ManageButton.onClick.AddListener(OnClickPlayerManagement);
        }

        /// <summary>인벤토리(또는 추후 다른 화면)에서 카드를 선택했을 때 호출한다.</summary>
        public void Show(Player player)
        {
            if (player?.Template == null) return;

            currentPlayer = player;

            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
                // [명령서 7항] 탭 전환 시뿐 아니라 패널을 열 때부터 Z-Order 붕괴가 없도록, 열 때마다
                // 부모 계층 최상단(=렌더링 최상위)으로 강제한다(TASK-KBO-139 이후 확립된 관례 재사용).
                panelRoot.transform.SetAsLastSibling();
            }

            if (previewCard != null) previewCard.Setup(player);
            if (layout != null)
            {
                int synergy = GameManager.Instance != null ? GameManager.Instance.CurrentTeamSynergyOvr : 0;
                layout.Fill(player, synergy, skillDB);
                if (layout.CardHolder != null) layout.CardHolder.Refit();
                return;
            }

            // 레이아웃을 만들 수 없는(panelRoot 미배선) 구 씬 폴백
            if (nameText != null) nameText.text = player.Template.PlayerName;
            if (teamGradeText != null) teamGradeText.text = $"{player.Template.Team} · {CardGrowthRules.DisplayName(player.Template.Grade)}";

            RefreshStatsTab();
            RefreshSkillTab();
            SelectTab(Tab.Stats);
        }

        public void Hide()
        {
            currentPlayer = null;
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void HandleCloseClicked()
        {
            Hide();
            OnClosed?.Invoke();
        }

        /// <summary>[TASK-KBO-150] [선수 관리] 버튼 OnClick. 허브의 `Show()`를 직접 호출한다 - 그
        /// 메서드가 `currentPlayer` 갱신과 `UIManager.ShowScreen(ScreenType.PlayerManagementHub)` 호출을
        /// 모두 포함하고 있어(TASK-145), 원시 `ShowScreen()` 호출만으로는 놓치는 허브 쪽 데이터 갱신까지
        /// 함께 보장된다. `PlayerManagementHubPanel`은 `Canvas` 바로 아래(다른 화면들과 동일한 최상위
        /// `ScreenType`)에 있어 이 상세 창(`InventoryPanel`의 자식)이 활성 상태이든 아니든 `ShowScreen()`
        /// 만으로 정상적으로 화면 최상단에 나타난다 - TASK-148에서 확인된 "자식이 비활성 부모 밑에 있어
        /// 안 보이는" 문제가 이 방향(상세창 → 허브)에는 애초에 발생하지 않는다(허브는 상세 창의 자식이
        /// 아니라 완전히 별개의 최상위 화면).</summary>
        private void OnClickPlayerManagement()
        {
            if (currentPlayer == null) return;

            if (playerManagementUIController == null)
            {
                Debug.LogWarning("[PlayerDetailUIController] playerManagementUIController가 바인딩되지 않아 " +
                    "선수 관리 허브를 열 수 없습니다. 'KBO Manager/Setup/Auto-Connect Inventory UI'를 다시 실행하십시오.");
                return;
            }

            var player = currentPlayer;
            Hide(); // [TASK-KBO-184] 캔버스 최상위 상세 창이 성장 센터를 가리지 않게 닫고 연다
            playerManagementUIController.Show(player);
        }

        /// <summary>[명령서 4항] 탭 버튼 클릭 시 해당 탭의 ContentPanel만 SetActive(true)하고 나머지
        /// 3개는 SetActive(false)한다 - 4개 패널이 같은 영역에 겹쳐 있으므로 항상 정확히 하나만 켜져
        /// 있어야 한다(명령서 7항 - Z-Order/겹침 방지).</summary>
        private void SelectTab(Tab tab)
        {
            currentTab = tab;

            if (statsContentPanel != null) statsContentPanel.SetActive(tab == Tab.Stats);
            if (specialFormContentPanel != null) specialFormContentPanel.SetActive(tab == Tab.SpecialForm);
            if (hotColdContentPanel != null) hotColdContentPanel.SetActive(tab == Tab.HotCold);
            if (skillContentPanel != null) skillContentPanel.SetActive(tab == Tab.Skill);

            ApplyTabButtonColor(statsTabButton, tab == Tab.Stats);
            ApplyTabButtonColor(specialFormTabButton, tab == Tab.SpecialForm);
            ApplyTabButtonColor(hotColdTabButton, tab == Tab.HotCold);
            ApplyTabButtonColor(skillTabButton, tab == Tab.Skill);
        }

        /// <summary>[TASK-KBO-152, 명령서 4항] 탭 버튼 하이라이트 - 예전엔 이 메서드가 `static`이라
        /// 인스펙터에서 조정 가능하도록 만들어 둔 `activeTabColor`/`inactiveTabColor` 필드를 전혀 읽지
        /// 않고 하드코딩된 흰색/연회색만 썼다(사실상 죽은 필드였다) - 인스턴스 메서드로 바꿔 그 필드들을
        /// 실제로 사용하고, 배경색뿐 아니라 라벨 텍스트 색상까지 함께 바꿔 "선택됨"이 더 뚜렷하게
        /// 보이도록 강화했다. 화면 전환/데이터 로직은 전혀 건드리지 않은 순수 시각 효과다.</summary>
        private void ApplyTabButtonColor(Button button, bool active)
        {
            if (button == null) return;

            if (button.targetGraphic != null)
            {
                button.targetGraphic.color = active ? activeTabColor : inactiveTabColor;
            }

            var label = button.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.color = active ? activeTabTextColor : inactiveTabTextColor;
                label.fontStyle = FontStyle.Normal;
            }
        }

        /// <summary>[기본 스탯] 탭 - 강화/각성 진행도(기존 구 DetailPanel의 텍스트를 그대로 이관)와
        /// 타자/투수 실제 세부 스탯 5종을 채운다.</summary>
        private void RefreshStatsTab()
        {
            if (currentPlayer?.Template == null) return;

            if (reinforceText != null)
            {
                // [TASK-KBO-183] 4대 성장 진행도(강화 · 한계 돌파 · 특훈) - 각성은 아래 awakenText.
                var g = currentPlayer.Template.Grade;
                reinforceText.text = $"강화 {currentPlayer.ReinforceLevel} / {Player.MaxReinforceLevel} · 한계 돌파 {currentPlayer.LimitBreakGrowth} / {CardGrowthRules.LimitBreakCap(g)}" +
                                     $" · 특훈 {currentPlayer.TrainingGrowth} / {CardGrowthRules.TrainingCap(g)}";
            }

            if (growthSummaryText != null)
            {
                int synergy = GameManager.Instance != null ? GameManager.Instance.CurrentTeamSynergyOvr : 0;
                growthSummaryText.text = $"기본 OVR {currentPlayer.BaseOvr} · 현재 성장 +{currentPlayer.GetStatGrowth()} · 시너지 +{synergy} · " +
                                         $"최대 잠재 OVR {currentPlayer.MaxPotentialOvr}";
            }

            if (awakenText != null)
            {
                // [TASK-KBO-172] 전 등급 각성 가능 - "현재 단계 / 등급 한계(9각 또는 초월)"와 개인 세트덱 스코어를 함께 표기.
                string limitLabel = currentPlayer.MaxAwakenLevelForGrade >= CardGrowthRules.TranscendLevel ? "초월" : "9각";
                awakenText.text = $"각성 {currentPlayer.AwakenLabel} / {limitLabel} (OVR +{currentPlayer.AwakenGrowth}/{CardGrowthRules.AwakenGrowthCap(currentPlayer.Template.Grade)}) · 세트덱 {currentPlayer.SetDeckScore}점";
            }

            if (statRowTexts == null) return;

            var template = currentPlayer.Template;
            (string label, int value)[] rows = template.IsPitcher
                ? new (string, int)[]
                {
                    ("구위", template.PitcherStats.Stuff),
                    ("구속", template.PitcherStats.Velocity),
                    ("변화", template.PitcherStats.Movement),
                    ("제구", template.PitcherStats.Control),
                    ("체력", template.PitcherStats.Stamina),
                }
                : new (string, int)[]
                {
                    ("파워", template.BatterStats.Power),
                    ("정확", template.BatterStats.Contact),
                    ("선구", template.BatterStats.Discipline),
                    ("주력", template.BatterStats.Speed),
                    ("수비", template.BatterStats.Defense),
                };

            for (int i = 0; i < statRowTexts.Length; i++)
            {
                if (statRowTexts[i] == null) continue;
                statRowTexts[i].text = i < rows.Length ? $"{rows[i].label}  {rows[i].value}" : "";
            }
        }

        /// <summary>[스킬] 탭 - 보유 스킬 목록(AcquiredSkillIds)을 실데이터 그대로 표시한다.</summary>
        private void RefreshSkillTab()
        {
            if (skillListText == null || currentPlayer == null) return;

            var skillSlots = PlayerSkillRules.SlotsOf(currentPlayer); // [TASK-KBO-190]
            skillListText.text = skillSlots.Count > 0
                ? string.Join("\n", skillSlots.Select(s => $"{PlayerSkillRules.SlotLabel(s)} - {PlayerSkillRules.EffectText(s)}"))
                : "보유 스킬 없음";
        }
    }
}
