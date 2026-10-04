using System.Collections.Generic;
using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-176] 세트덱 "선택형 버프 구간"(기획 고도화 자료.pdf의 "OR" 구간 10개: 80/100/115/120/135/140/150/155/
    /// 185/190P) A/B 선택 패널. 로스터 화면의 [세트덱 버프 선택] 버튼으로 연다.
    /// - 각 구간 행: "{구간}P · 도달/미도달" + [A: 효과] [B: 효과] 버튼. 고른 쪽이 강조색이며, 미도달 구간도 미리 골라 둘 수 있다
    ///   (도달하는 순간 그 선택이 적용된다).
    /// - [연도 선택] 버튼: 자동(세트덱 최다 연도) → 세트덱 카드 연도들 순환. 80/185/190P의 "연도 선택" 효과 대상 연도다.
    /// 선택은 GameManager.SetDeckSelection에 저장되고(GameManager.SetSetDeckOption/SetSetDeckSelectedYear), 세이브
    /// (GameSaveData.SetDeckSelection, v6)와 경기 시뮬레이션(BuildTeamPowerModifiers → SetDeckBuffProfile)에 그대로 쓰인다.
    /// 행은 Setup이 만든 숨김 템플릿(rowTemplate: 자식 "Label" Text, "OptionA"/"OptionB" Button + 하위 Text)을 런타임에 복제한다.
    /// </summary>
    public class SetDeckOptionUIController : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text summaryText;
        [SerializeField] private Transform rowContainer;
        [Tooltip("비활성 행 템플릿 - 자식 'Label'(Text), 'OptionA'/'OptionB'(Button, 하위 Text).")]
        [SerializeField] private GameObject rowTemplate;
        [SerializeField] private Button yearButton;
        [SerializeField] private Text yearText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Color selectedColor = new Color(1f, 0.84f, 0f);
        [Tooltip("[TASK-KBO-182] 선택되지 않은 반대쪽 버튼 - 어두운 비활성 톤(선택 버튼과 대비).")]
        [SerializeField] private Color unselectedColor = new Color(0.2f, 0.22f, 0.29f);
        [SerializeField] private Color selectedTextColor = new Color(0.08f, 0.09f, 0.14f);
        [SerializeField] private Color unselectedTextColor = new Color(0.55f, 0.59f, 0.68f);
        [SerializeField] private Color unreachedLabelColor = new Color(0.45f, 0.45f, 0.45f);
        [Tooltip("[TASK-KBO-178] 도달(적용 중) 구간 라벨 색 - 어두운 테마 창에서는 흰색으로 지정된다.")]
        [SerializeField] private Color reachedLabelColor = Color.black;

        private sealed class Row
        {
            public int Threshold;
            public Text Label;
            public Button OptionA;
            public Button OptionB;
        }

        private readonly List<Row> rows = new List<Row>();
        private string lastFeedback; // [TASK-KBO-182] "{tier}P 구간: A안으로 설정되었습니다"
        public string LastFeedback => lastFeedback;
        public int RowCount => rows.Count;

        /// <summary>[TASK-KBO-185 검증용] threshold 구간의 A(false)/B(true) 버튼(없으면 null).</summary>
        public Button OptionButton(int threshold, bool optionB)
        {
            var row = rows.FirstOrDefault(r => r.Threshold == threshold);
            return row == null ? null : optionB ? row.OptionB : row.OptionA;
        }

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (yearButton != null) yearButton.onClick.AddListener(CycleSelectedYear);
            if (rowTemplate != null) rowTemplate.SetActive(false);
        }

        private void OnEnable()
        {
            if (GameManager.Instance != null) GameManager.Instance.OnSetDeckSelectionChanged += Refresh;
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null) GameManager.Instance.OnSetDeckSelectionChanged -= Refresh;
        }

        public bool IsOpen => panelRoot != null ? panelRoot.activeInHierarchy : gameObject.activeInHierarchy;

        /// <summary>[TASK-KBO-185] 팝업을 켜고 최상단으로 올린다 - 형제 순서상 뒤에 있는 패널(트레이/레거시 버튼)에 가려 A/B 버튼 클릭이
        /// 먹히지 않던 문제를 막는다(컨트롤러는 RosterPanel에 붙어 있고 팝업 루트는 그 자식 SetDeckOptionPanel이다).</summary>
        public void Open()
        {
            lastFeedback = null;
            var popup = panelRoot != null ? panelRoot : gameObject;
            popup.SetActive(true);
            popup.transform.SetAsLastSibling();
            Refresh();
            EnsureRaycastable();
        }

        /// <summary>[TASK-KBO-185] A/B · 연도 · 닫기 버튼이 클릭을 받도록 버튼 그래픽 raycastTarget을 켜고, 버튼을 덮는 라벨 Text는 끈다.</summary>
        public void EnsureRaycastable()
        {
            var root = panelRoot != null ? panelRoot.transform : transform;
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                if (button.targetGraphic != null) button.targetGraphic.raycastTarget = true;
                button.interactable = true;
                foreach (var text in button.GetComponentsInChildren<Text>(true)) text.raycastTarget = false;
            }
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private SetDeckResult EvaluateUserSetDeck()
        {
            var gm = GameManager.Instance;
            string favoriteTeamName = gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null;
            return GameManager.EvaluateSetDeck(gm.Roster.ToList(), favoriteTeamName, gm.SetDeckSelection);
        }

        public void Refresh()
        {
            EnsureRows(); // [TASK-KBO-185] GameManager 없이도 A/B 행은 만든다(팝업이 빈 창으로 뜨지 않게)
            var gm = GameManager.Instance;
            if (gm == null) return;

            var setDeck = EvaluateUserSetDeck();
            var selection = gm.SetDeckSelection;

            foreach (var row in rows)
            {
                var bracket = SetDeckBuffTable.Brackets.First(b => b.Threshold == row.Threshold);
                bool reached = setDeck.Score >= bracket.Threshold;
                bool usesB = selection.UsesOptionB(bracket.Threshold);

                if (row.Label != null)
                {
                    row.Label.text = reached ? $"{bracket.Threshold}P\n도달 · 적용 중" : $"{bracket.Threshold}P\n미도달 · 미리 설정됨";
                    row.Label.color = reached ? reachedLabelColor : unreachedLabelColor;
                    row.Label.supportRichText = true;
                    row.Label.resizeTextForBestFit = true; // [TASK-KBO-182] 2줄(구간 / 도달 상태)
                    row.Label.resizeTextMinSize = 10;
                    row.Label.resizeTextMaxSize = Mathf.Max(row.Label.resizeTextMaxSize, row.Label.fontSize);
                }
                StyleOption(row.OptionA, $"A: {bracket.OptionA.Label}", !usesB);
                StyleOption(row.OptionB, $"B: {bracket.OptionB.Label}", usesB);
            }

            if (yearText != null)
            {
                yearText.text = selection.SelectedYear > 0
                    ? $"연도 선택: {selection.SelectedYear}"
                    : $"연도 선택: 자동({(setDeck.SelectedYear > 0 ? setDeck.SelectedYear.ToString() : "-")})";
            }

            if (summaryText != null)
            {
                int reachedSelectable = SetDeckBuffTable.SelectableBrackets.Count(b => setDeck.Score >= b.Threshold);
                // [TASK-KBO-182] A안도 B안과 같은 "명시적 설정"으로 센다 - 예전 문구는 B안 개수만 보여 A안 선택이 반영되지 않는 것처럼 보였다.
                var (aCount, bCount) = CountChoices(selection, SetDeckBuffTable.SelectableBrackets.Select(b => b.Threshold));
                string head = $"세트덱 {setDeck.Score}P · 도달 구간 {reachedSelectable}/{rows.Count} · 현재 설정: A안 {aCount}개 / B안 {bCount}개";
                summaryText.text = string.IsNullOrEmpty(lastFeedback) ? head : $"{head}\n<color=#7FE3FF>{lastFeedback}</color>";
            }
        }

        /// <summary>[TASK-KBO-182] 선택형 구간별 현재 설정(A/B) 개수 - 모든 구간은 항상 A 또는 B 중 하나로 설정돼 있다(기본 A).</summary>
        public static (int A, int B) CountChoices(SetDeckSelection selection, IEnumerable<int> thresholds)
        {
            var list = thresholds.ToList();
            int b = list.Count(t => selection != null && selection.UsesOptionB(t));
            return (list.Count - b, b);
        }

        /// <summary>A/B 클릭 - 선택을 저장하고 상단 안내줄에 즉시 피드백을 띄운다(같은 쪽을 다시 눌러도 설정 확인 문구가 갱신된다).</summary>
        private void Choose(int threshold, bool optionB)
        {
            lastFeedback = $"{threshold}P 구간: {(optionB ? "B" : "A")}안으로 설정되었습니다";
            var gm = GameManager.Instance;
            if (gm == null) return;
            gm.SetSetDeckOption(threshold, optionB); // OnSetDeckSelectionChanged → Refresh
            Refresh(); // 패널이 이벤트를 구독하지 않은 상태(비활성 부모 등)에서도 즉시 갱신
        }

        private void StyleOption(Button button, string label, bool selected)
        {
            if (button == null) return;
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.supportRichText = true;
                text.text = selected ? $"<b>✔ [선택됨]</b>  {label}" : label;
                text.color = selected ? selectedTextColor : unselectedTextColor;
                text.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            }
            if (button.targetGraphic != null) button.targetGraphic.color = selected ? selectedColor : unselectedColor;
            // 버튼 틴트(ColorBlock)가 선택 대비를 흐리지 않게 normal을 흰색으로 고정한다.
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.95f, 0.95f, 0.95f);
            colors.selectedColor = Color.white;
            button.colors = colors;
        }

        private void EnsureRows()
        {
            if (rows.Count > 0 || rowTemplate == null || rowContainer == null) return;

            foreach (var bracket in SetDeckBuffTable.SelectableBrackets)
            {
                var rowObject = Instantiate(rowTemplate, rowContainer);
                rowObject.name = $"OptionRow_{bracket.Threshold}";
                rowObject.SetActive(true);

                var row = new Row
                {
                    Threshold = bracket.Threshold,
                    Label = rowObject.transform.Find("Label")?.GetComponent<Text>(),
                    OptionA = rowObject.transform.Find("OptionA")?.GetComponent<Button>(),
                    OptionB = rowObject.transform.Find("OptionB")?.GetComponent<Button>(),
                };
                int threshold = bracket.Threshold;
                if (row.OptionA != null) row.OptionA.onClick.AddListener(() => Choose(threshold, false));
                if (row.OptionB != null) row.OptionB.onClick.AddListener(() => Choose(threshold, true));
                rows.Add(row);
            }
        }

        /// <summary>자동(0) → 세트덱 합산 카드의 시즌 연도들(최신순) → 다시 자동으로 순환한다.</summary>
        private void CycleSelectedYear()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            var setDeck = EvaluateUserSetDeck();
            var years = new List<int> { 0 };
            years.AddRange(setDeck.CountedPlayers.Select(p => p.Template.SeasonYear).Where(y => y > 0)
                .Distinct().OrderByDescending(y => y));

            int index = years.IndexOf(gm.SetDeckSelection.SelectedYear);
            gm.SetSetDeckSelectedYear(years[(index + 1) % years.Count]);
        }
    }
}
