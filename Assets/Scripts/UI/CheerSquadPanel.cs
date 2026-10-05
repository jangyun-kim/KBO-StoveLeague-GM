using System;
using System.Collections.Generic;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-180] 치어리더 관리 화면 상단 "6인 역할 편성" 3x2 그리드(1.응원단장 / 2.타격 응원 / 3.투수 응원 /
    /// 4.분위기 메이커 / 5.홈 응원 / 6.위기 응원). 각 칸 = 역할명·담당 · 배치된 치어리더 이름·티어·구단 · 구단 시너지 발동 여부 ·
    /// 역할별 수치 요약 · [해제]. 칸을 누르면 그 슬롯이 "배치 대상"으로 선택되고(금색 테두리), 아래 보유 목록의 [장착]이 그 슬롯에 배치한다.
    /// 계층은 Build()가 코드로 만든다(Setup이 에디터에서 한 번 만들어 씬에 저장, 런타임 Awake에서 다시 만들어 참조 확정).
    /// </summary>
    public class CheerSquadPanel : MonoBehaviour
    {
        [SerializeField] private Font boldFont;
        [SerializeField] private Font regularFont;

        public const string GridName = "Grid180";

        public event Action<CheerRole> OnSlotSelected;
        public event Action<CheerRole> OnSlotCleared;

        private class Cell
        {
            public Image Frame, Background;
            public Text Header, Focus, Name, Meta, Synergy, Effect;
            public Button Clear;
        }

        private readonly Cell[] cells = new Cell[CheerSquad.SlotCount];
        private CompyaUiKit kit;

        private static readonly Color Gold = new Color(1f, 0.82f, 0.27f);
        private static readonly Color CellNavy = new Color(0.12f, 0.15f, 0.26f);
        private static readonly Color CellEmpty = new Color(0.09f, 0.1f, 0.16f);
        private static readonly Color White = new Color(0.97f, 0.98f, 1f);
        private static readonly Color Muted = new Color(0.66f, 0.71f, 0.8f);
        private static readonly Color SynergyOn = new Color(0.4f, 0.95f, 0.55f);
        private static readonly Color SynergyOff = new Color(1f, 0.45f, 0.45f);

        private void Awake() => Build();

        public void ConfigureFonts(Font bold, Font regular)
        {
            boldFont = bold;
            regularFont = regular;
        }

        public void Build()
        {
            var old = transform.Find(GridName);
            if (old != null)
            {
                old.name = "_" + GridName + "_old";
                old.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
            }

            kit = new CompyaUiKit(boldFont, regularFont);
            var grid = CompyaUiKit.Fill(transform, GridName);
            for (int i = 0; i < CheerSquad.SlotCount; i++)
            {
                int col = i % 3, row = i / 3;
                float x0 = col / 3f + 0.006f, x1 = (col + 1) / 3f - 0.006f;
                float y1 = 1f - row / 2f - 0.012f, y0 = 1f - (row + 1) / 2f + 0.012f;
                var role = (CheerRole)i;
                var root = CompyaUiKit.Norm(grid, $"Slot{i + 1}", x0, y0, x1, y1);

                var cell = new Cell { Frame = CompyaUiKit.Paint(root, Gold, true) };
                var inner = CompyaUiKit.Norm(root, "Inner", 0.012f, 0.012f, 0.988f, 0.988f);
                cell.Background = CompyaUiKit.Paint(inner, CellNavy);
                var select = root.gameObject.AddComponent<Button>();
                select.targetGraphic = cell.Frame;
                select.onClick.AddListener(() => OnSlotSelected?.Invoke(role));

                cell.Header = kit.LabelOn(CompyaUiKit.Norm(inner, "Header", 0.05f, 0.8f, 0.95f, 0.97f), $"{i + 1}. {CheerSquad.RoleName(role)}", 30, TextAnchor.MiddleLeft, Gold, true);
                cell.Focus = kit.LabelOn(CompyaUiKit.Norm(inner, "Focus", 0.05f, 0.67f, 0.95f, 0.8f), CheerSquad.RoleFocus(role), 21, TextAnchor.MiddleLeft, Muted);
                cell.Name = kit.LabelOn(CompyaUiKit.Norm(inner, "Name", 0.05f, 0.5f, 0.95f, 0.66f), "", 30, TextAnchor.MiddleLeft, White, true);
                cell.Meta = kit.LabelOn(CompyaUiKit.Norm(inner, "Meta", 0.05f, 0.37f, 0.95f, 0.5f), "", 21, TextAnchor.MiddleLeft, Muted);
                cell.Synergy = kit.LabelOn(CompyaUiKit.Norm(inner, "Synergy", 0.05f, 0.25f, 0.95f, 0.37f), "", 21, TextAnchor.MiddleLeft, SynergyOn, true);
                cell.Effect = kit.LabelOn(CompyaUiKit.Norm(inner, "Effect", 0.05f, 0.03f, 0.66f, 0.25f), "", 20, TextAnchor.MiddleLeft, White);
                var clearRect = CompyaUiKit.Norm(inner, "Clear", 0.68f, 0.04f, 0.96f, 0.22f);
                var clearImage = CompyaUiKit.Paint(clearRect, new Color(0.45f, 0.2f, 0.25f), true);
                cell.Clear = clearRect.gameObject.AddComponent<Button>();
                cell.Clear.targetGraphic = clearImage;
                cell.Clear.onClick.AddListener(() => OnSlotCleared?.Invoke(role));
                kit.LabelOn(CompyaUiKit.Fill(clearRect, "Text"), "해제", 22, TextAnchor.MiddleCenter, White, true);
                cells[i] = cell;
            }
        }

        /// <summary>편성 상태를 그린다. deckTeam = 현재 세트덱 기준 구단(시너지 판정), selected = 배치 대상 슬롯.</summary>
        public void Refresh(IReadOnlyList<Cheerleader> slots, Team deckTeam, CheerRole selected)
        {
            if (cells[0] == null) Build();
            for (int i = 0; i < CheerSquad.SlotCount; i++)
            {
                var cell = cells[i];
                var role = (CheerRole)i;
                var c = slots != null && i < slots.Count ? slots[i] : null;
                bool empty = CheerSquad.IsEmpty(c);
                cell.Frame.color = role == selected ? Gold : new Color(0.3f, 0.36f, 0.55f);
                cell.Background.color = empty ? CellEmpty : CellNavy;
                cell.Clear.gameObject.SetActive(!empty);
                if (empty)
                {
                    cell.Name.text = role == selected ? "▶ 배치 대기" : "비어 있음";
                    cell.Meta.text = "아래 목록에서 [장착]";
                    cell.Synergy.text = "";
                    cell.Effect.text = CheerSquad.DescribeRoleEffect(role, null);
                    continue;
                }
                cell.Name.text = $"{c.DisplayName} <color=#FFD54A>{CheerGrowth.StarBadge(c)}</color>{(c.ReinforceLevel > 0 ? $" +{CheerGrowth.Reinforce(c)}" : "")}"; // [TASK-KBO-187]
                string affiliation = string.IsNullOrEmpty(c.AffiliationLabel) ? "구단 무관" : c.AffiliationLabel;
                cell.Meta.text = $"{c.Grade.Display()} · {affiliation}";
                bool synergy = CheerleaderSynergy.IsActive(c, deckTeam);
                cell.Synergy.text = synergy ? "● 시너지 발동" : $"● 미발동 (세트덱 {deckTeam})";
                cell.Synergy.color = synergy ? SynergyOn : SynergyOff;
                cell.Effect.text = CheerSquad.DescribeRoleEffect(role, c);
                cell.Effect.color = synergy ? White : Muted;
            }
        }
    }
}
