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
        [SerializeField] private Color unselectedColor = new Color(0.85f, 0.85f, 0.85f);
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

        public void Open()
        {
            if (panelRoot != null) panelRoot.SetActive(true);
            Refresh();
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
            var gm = GameManager.Instance;
            if (gm == null) return;

            EnsureRows();
            var setDeck = EvaluateUserSetDeck();
            var selection = gm.SetDeckSelection;

            foreach (var row in rows)
            {
                var bracket = SetDeckBuffTable.Brackets.First(b => b.Threshold == row.Threshold);
                bool reached = setDeck.Score >= bracket.Threshold;
                bool usesB = selection.UsesOptionB(bracket.Threshold);

                if (row.Label != null)
                {
                    row.Label.text = $"{bracket.Threshold}P · {(reached ? "적용 중" : "미도달")}";
                    row.Label.color = reached ? reachedLabelColor : unreachedLabelColor;
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
                int chosenB = selection.OptionBThresholds?.Count ?? 0;
                summaryText.text = $"세트덱 {setDeck.Score}P · 선택형 구간 {reachedSelectable}/{rows.Count} 도달 · B안 {chosenB}개 선택 " +
                    "(저장 시 세이브에 기록되어 경기에 적용됩니다)";
            }
        }

        private void StyleOption(Button button, string label, bool selected)
        {
            if (button == null) return;
            var text = button.GetComponentInChildren<Text>(true);
            if (text != null) text.text = selected ? $"✔ {label}" : label;
            if (button.targetGraphic != null) button.targetGraphic.color = selected ? selectedColor : unselectedColor;
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
                if (row.OptionA != null) row.OptionA.onClick.AddListener(() => GameManager.Instance?.SetSetDeckOption(threshold, false));
                if (row.OptionB != null) row.OptionB.onClick.AddListener(() => GameManager.Instance?.SetSetDeckOption(threshold, true));
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
