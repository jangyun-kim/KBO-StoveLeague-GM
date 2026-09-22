using System;
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

        [Header("Tab Visual (선택 표시 - 선택된 탭만 밝게)")]
        [SerializeField] private Color activeTabColor = Color.white;
        [SerializeField] private Color inactiveTabColor = new Color(0.75f, 0.75f, 0.75f);

        [Header("[기본 스탯] 탭 내용")]
        [SerializeField] private Text reinforceText;
        [SerializeField] private Text awakenText;
        [Tooltip("정확히 5칸 - 타자/투수 각각의 실제 세부 스탯 5종을 순서대로 채운다(RefreshStatsTab() 참고).")]
        [SerializeField] private Text[] statRowTexts;

        [Header("[스킬] 탭 내용")]
        [SerializeField] private Text skillListText;

        [Header("닫기")]
        [SerializeField] private Button closeButton;

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
            if (closeButton != null) closeButton.onClick.AddListener(HandleCloseClicked);

            Hide();
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
            if (nameText != null) nameText.text = player.Template.PlayerName;
            if (teamGradeText != null) teamGradeText.text = $"{player.Template.Team} · {player.Template.Grade}";

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

        private static void ApplyTabButtonColor(Button button, bool active)
        {
            if (button == null || button.targetGraphic == null) return;
            button.targetGraphic.color = active ? Color.white : new Color(0.75f, 0.75f, 0.75f);
        }

        /// <summary>[기본 스탯] 탭 - 강화/각성 진행도(기존 구 DetailPanel의 텍스트를 그대로 이관)와
        /// 타자/투수 실제 세부 스탯 5종을 채운다.</summary>
        private void RefreshStatsTab()
        {
            if (currentPlayer?.Template == null) return;

            if (reinforceText != null)
            {
                reinforceText.text = $"강화 {currentPlayer.ReinforceLevel} / {Player.MaxReinforceLevel}";
            }

            if (awakenText != null)
            {
                awakenText.text = currentPlayer.CanAwaken
                    ? $"각성 {currentPlayer.AwakenLevel} / {Player.MaxAwakenLevel}"
                    : "각성 불가 (LIVE 등급)";
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

            skillListText.text = currentPlayer.AcquiredSkillIds.Count > 0
                ? string.Join("\n", currentPlayer.AcquiredSkillIds)
                : "보유 스킬 없음";
        }
    }
}
