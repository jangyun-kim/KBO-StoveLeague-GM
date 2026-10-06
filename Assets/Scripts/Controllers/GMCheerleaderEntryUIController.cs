using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-GM-05] 단장 모드 구단 치어리더 관리 화면(기획서 3절, 모든 글씨 Normal · 15pt 이상). [TASK-GM-06] 1920×1080 Landscape · 전담 대상 직접 지정.
    ///   - 상단: 구단 응원단 · 엔트리 n/6명(4~6명) · 구단 풀 n/15명 · 팀워크/실책 억제/홈 흥행 요약, [X] 닫기(이전 화면 복귀)
    ///   - 엔트리 6칸(1.응원단장 ~ 6.위기 응원, 비어 있는 칸은 "빈 슬롯")
    ///   - 선택 카드 상세: 이름 · 등급 · 강화/★각성(골드 · 프리즘 테두리) · 체력 · 열정도(CHEER) + 4대 응원 스탯 게이지
    ///   - [엔트리 배치/해제] [전담 응원 지정] [최적 컨디션 5인 자동 편성], 15인 풀 목록, 전담 응원 현황, [자동 로테이션] 토글
    /// 대시보드 [치어리더 엔트리 (4~6인)] · 전력 비교 [라인업/치어리더 점검]이 연다. 기존 보유 치어리더 인벤토리 · 강화 · 각성 · 영입 화면은 그대로 둔다.
    /// </summary>
    public class GMCheerleaderEntryUIController : MonoBehaviour
    {
        public const string RootName = "CheerEntryRoot";
        public const int TitlePt = 26, SummaryPt = 18, RolePt = 16, SlotPt = 17, DetailNamePt = 24, DetailPt = 17, CheerPt = 34, GaugePt = 18, ButtonPt = 20, PoolPt = 18;
        public const int SlotCount = 6, PoolRows = GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX;

        private static readonly Color Bg = new Color(0.1f, 0.06f, 0.14f);
        private static readonly Color White = new Color(0.97f, 0.96f, 0.99f);
        private static readonly Color Muted = new Color(0.8f, 0.76f, 0.88f);
        private static readonly Color Pink = new Color(1f, 0.6f, 0.82f);
        private static readonly Color ButtonIdle = new Color(1f, 1f, 1f, 0.1f);
        private static readonly Color EntryOn = new Color(0.72f, 0.24f, 0.52f);
        private static readonly Color Selected = new Color(0.25f, 0.42f, 0.8f);
        private static readonly Color[] GaugeColors =
            { new Color(1f, 0.78f, 0.3f), new Color(1f, 0.45f, 0.45f), new Color(0.4f, 0.75f, 1f), new Color(0.5f, 0.92f, 0.6f) };

        [SerializeField] private Font regularFont;

        private RectTransform root;
        private Text title, summary, message, detailName, detailInfo, detailCheer, dedicationText;
        private readonly Text[] slotRoles = new Text[SlotCount];
        private readonly Button[] slots = new Button[SlotCount];
        private readonly Text[] gaugeValues = new Text[4];
        private readonly RectTransform[] gaugeFills = new RectTransform[4];
        private readonly Button[] poolButtons = new Button[PoolRows];
        private Image detailFrame;
        private Button closeButton, toggleButton, dedicateButton, autoButton, rotateButton, backButton;
        private Button dedicationTargetButton; // [TASK-GM-06] D.3

        private GMTeamState team;
        private Cheerleader selected;
        private Action onClosed;

        public RectTransform Root => root;
        public GMTeamState CurrentTeam => team;
        public Cheerleader SelectedCheerleader => selected;
        public string LastMessage => message != null ? message.text : null;

        public void Configure(Font regular) => regularFont = regular;

        private void Awake()
        {
            if (root == null) Build();
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
            var font = regularFont != null ? regularFont : TextTidy.BodyFont;
            var kit = new CompyaUiKit(font, font);
            using (CompyaUiKit.Wide()) // [TASK-GM-06] 1920×1080 Landscape(좌 엔트리 · 상세 · 조작 / 우 15인 풀)
            {
            root = CompyaUiKit.Fill(transform, RootName);
            CompyaUiKit.Paint(root, Bg, true);

            title = L(kit, "Title", "치어리더 엔트리", 24, 12, 1780, 72, TitlePt, TextAnchor.MiddleLeft, Pink);
            closeButton = Btn(kit, "CloseButton", "X", 1800, 12, 1908, 72, ButtonIdle, ButtonPt + 2);
            closeButton.onClick.AddListener(Close);
            summary = L(kit, "Summary", "", 24, 78, 1908, 118, SummaryPt, TextAnchor.MiddleLeft, White);

            for (int i = 0; i < SlotCount; i++)
            {
                int index = i;
                float x0 = 16 + i * 164, x1 = x0 + 156;
                slotRoles[i] = L(kit, $"SlotRole{i}", $"{i + 1}.{CheerSquad.RoleName((CheerRole)i)}", x0, 124, x1, 156, RolePt, TextAnchor.MiddleCenter, Muted);
                slots[i] = Btn(kit, $"Slot{i}", "", x0, 160, x1, 262, ButtonIdle, SlotPt);
                slots[i].onClick.AddListener(() => SelectSlot(index));
            }

            detailFrame = CompyaUiKit.Box(root, "DetailFrame", 12, 270, 1000, 620, new Color(1f, 1f, 1f, 0.05f));
            detailName = L(kit, "DetailName", "", 24, 276, 700, 326, DetailNamePt, TextAnchor.MiddleLeft, White);
            detailCheer = L(kit, "DetailCheer", "", 710, 272, 990, 330, CheerPt, TextAnchor.MiddleRight, Pink);
            detailInfo = L(kit, "DetailInfo", "", 24, 330, 990, 370, DetailPt, TextAnchor.MiddleLeft, Muted);
            for (int k = 0; k < 4; k++)
            {
                float y0 = 376 + k * 60;
                L(kit, $"GaugeLabel{k}", GMCheerleaderStats.StatLabels[k], 24, y0, 280, y0 + 52, GaugePt, TextAnchor.MiddleLeft, White);
                var bg = CompyaUiKit.Box(root, $"GaugeBg{k}", 290, y0 + 12, 880, y0 + 40, new Color(1f, 1f, 1f, 0.1f));
                gaugeFills[k] = CompyaUiKit.Fill(bg.transform, "Fill");
                CompyaUiKit.Paint(gaugeFills[k], GaugeColors[k]);
                gaugeValues[k] = L(kit, $"GaugeValue{k}", "", 890, y0, 990, y0 + 52, GaugePt + 1, TextAnchor.MiddleRight, White);
            }

            toggleButton = Btn(kit, "ToggleEntryButton", "엔트리 배치", 12, 630, 330, 690, EntryOn, ButtonPt);
            dedicateButton = Btn(kit, "DedicateButton", "전담 응원 지정", 340, 630, 660, 690, new Color(0.45f, 0.3f, 0.65f), ButtonPt);
            autoButton = Btn(kit, "AutoArrangeButton", "최적 컨디션 5인 자동 편성", 670, 630, 1000, 690, new Color(0.15f, 0.45f, 0.85f), ButtonPt - 1);
            toggleButton.onClick.AddListener(() => ToggleSelected());
            dedicateButton.onClick.AddListener(() => DedicateSelected());
            autoButton.onClick.AddListener(AutoArrange);
            // [TASK-GM-06] GM-05 D.3 - 전담 응원 대상 선수 직접 지정(클릭할 때마다 다음 Ego 4+ 스타로 순환)
            dedicationTargetButton = Btn(kit, "DedicationTargetButton", "전담 대상 변경 ▶", 12, 698, 1000, 750, new Color(0.38f, 0.22f, 0.5f), ButtonPt - 1);
            dedicationTargetButton.onClick.AddListener(() => CycleDedicationTarget());
            message = L(kit, "Message", "", 24, 758, 1000, 804, DetailPt, TextAnchor.MiddleLeft, Pink);
            dedicationText = L(kit, "DedicationText", "", 24, 810, 1000, 944, DetailPt, TextAnchor.UpperLeft, Muted);

            for (int i = 0; i < PoolRows; i++)
            {
                int index = i;
                float y0 = 124 + i * 62;
                poolButtons[i] = kit.Button(root, $"Pool{i}", "", 1012, y0, 1908, y0 + 56, ButtonIdle, White, PoolPt / 0.9f, bold: false);
                var label = poolButtons[i].GetComponentInChildren<Text>(true);
                label.alignment = TextAnchor.MiddleLeft;
                TextTidy.Exact(label, PoolPt);
                ((RectTransform)label.transform).offsetMin = new Vector2(14, 0);
                poolButtons[i].onClick.AddListener(() => SelectPool(index));
            }

            rotateButton = Btn(kit, "AutoRotateButton", "자동 로테이션: 켜짐", 12, 956, 500, 1060, ButtonIdle, ButtonPt);
            backButton = Btn(kit, "BackButton", "닫기", 512, 956, 1000, 1060, new Color(0.3f, 0.3f, 0.42f), ButtonPt + 1);
            rotateButton.onClick.AddListener(ToggleAutoRotate);
            backButton.onClick.AddListener(Close);
            root.gameObject.SetActive(true);
            }
        }

        private Text L(CompyaUiKit kit, string name, string text, float x0, float y0, float x1, float y1, int pt, TextAnchor anchor, Color color)
        {
            var t = kit.Label(root, name, text, x0, y0, x1, y1, pt / 0.9f, anchor, color);
            return TextTidy.Exact(t, pt);
        }

        private Button Btn(CompyaUiKit kit, string name, string text, float x0, float y0, float x1, float y1, Color bg, int pt)
        {
            var b = kit.Button(root, name, text, x0, y0, x1, y1, bg, White, pt / 0.9f, bold: false);
            TextTidy.ExactButton(b, pt);
            return b;
        }

        // ================================================================== 진입 · 닫기

        /// <summary>구단 응원단을 띄운다. 닫으면 onClosed(이전 화면 복귀)를 부른다.</summary>
        public void Open(GMTeamState targetTeam, Action closed = null)
        {
            if (root == null) Build();
            team = targetTeam;
            onClosed = closed;
            selected = team?.CheerEntry.FirstOrDefault() ?? team?.CheerleaderPool.FirstOrDefault();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (message != null) message.text = "";
            Refresh();
        }

        /// <summary>[X] · [닫기] - 화면을 끄고 이전 화면으로 돌아간다.</summary>
        public void Close()
        {
            gameObject.SetActive(false);
            var callback = onClosed;
            onClosed = null;
            callback?.Invoke();
        }

        // ================================================================== 조작

        public void SelectSlot(int index)
        {
            var entry = GMCheerleaderRoster.Entry(team);
            if (index < entry.Count) selected = entry[index];
            Refresh();
        }

        public void SelectPool(int index)
        {
            if (team == null || index >= team.CheerleaderPool.Count) return;
            selected = team.CheerleaderPool[index];
            Refresh();
        }

        public void Select(Cheerleader c)
        {
            if (team != null && team.CheerleaderPool.Contains(c)) selected = c;
            Refresh();
        }

        /// <summary>[엔트리 배치/해제] - 4명 미만 · 6명 초과가 되는 변경은 사유와 함께 막는다.</summary>
        public bool ToggleSelected()
        {
            if (team == null || selected == null) return false;
            bool wasIn = GMCheerleaderRoster.IsInEntry(team, selected);
            bool ok = GMCheerleaderRoster.TryToggle(team, selected, out string reason);
            message.text = ok ? $"{selected.DisplayName} {(wasIn ? "엔트리 해제" : "엔트리 배치")} - {GMCheerleaderRules.Summary(team.CheerEntrySize, team.CheerleaderPool.Count)}" : reason;
            Refresh();
            return ok;
        }

        /// <summary>[전담 응원 지정] - 선택한 에이스/리더 치어리더에게 가장 불만이 큰 Ego 4+ 스타를 맡긴다.</summary>
        public bool DedicateSelected()
        {
            if (team == null || selected == null) return false;
            var target = GMCheerleaderRoster.DedicationTarget(team);
            if (target == null) { message.text = "전담 응원이 필요한 Ego 4 이상 스타가 없습니다."; Refresh(); return false; }
            bool ok = GMCheerleaderRoster.TryDedicate(team, selected, target, out string reason);
            message.text = ok ? $"{selected.DisplayName} → {target.Template.PlayerName} 전담 응원가 · 단상 이벤트! 보직 불만 해소 · 만족도 +{GMCheerleaderRoster.DedicationMoraleBonus}" : reason;
            Refresh();
            return ok;
        }

        /// <summary>
        /// [TASK-GM-06] GM-05 D.3 [전담 대상 변경 ▶] - 선택한 에이스/리더 치어리더의 전담 응원 대상을 다음 Ego 4+ 스타로 바꾼다
        /// (자동 배정 대신 단장이 직접 고른다). 새 대상(실패하면 null).
        /// </summary>
        public Player CycleDedicationTarget()
        {
            if (team == null || selected == null) return null;
            var next = GMCheerleaderRoster.CycleDedication(team, selected, out string reason);
            message.text = next != null
                ? $"{selected.DisplayName} 전담 대상 → {next.Template.PlayerName}(Ego {next.EgoLevel}) · 보직 불만 해소 · 만족도 +{GMCheerleaderRoster.DedicationMoraleBonus}"
                : reason;
            Refresh();
            return next;
        }

        /// <summary>[최적 컨디션 5인 자동 편성].</summary>
        public void AutoArrange()
        {
            if (team == null) return;
            var entry = GMCheerleaderRoster.AutoArrange(team);
            selected = entry.FirstOrDefault() ?? selected;
            message.text = $"최적 컨디션 {entry.Count}인 자동 편성 완료 - 체력 30 이상 · 역할 스탯 순 배치";
            Refresh();
        }

        public void ToggleAutoRotate()
        {
            if (team == null) return;
            team.CheerAutoRotate = !team.CheerAutoRotate;
            message.text = team.CheerAutoRotate ? "자동 로테이션 켬 - 체력 30 미만 인원이 생기면 같은 인원수로 재편성합니다." : "자동 로테이션 끔 - 단장이 직접 로테이션합니다.";
            Refresh();
        }

        // ================================================================== 갱신

        public void Refresh()
        {
            if (root == null || team == null) return;
            var entry = GMCheerleaderRoster.Entry(team);
            title.text = $"{NameAliasTable.DisplayTeamName(team.TeamCode)} 응원단 엔트리";
            summary.text = $"{GMCheerleaderRules.Summary(entry.Count, team.CheerleaderPool.Count)} · 팀워크 +{GMCheerleaderRoster.LeadershipTeamworkBonus(entry)} · " +
                           $"실책 -{GMCheerleaderRoster.ErrorReduction(entry) * 100f:0}% · 홈 흥행 +{GMCheerleaderRoster.HomeRevenue(entry):N0}만 원";

            for (int i = 0; i < SlotCount; i++)
            {
                var c = i < entry.Count ? entry[i] : null;
                slotRoles[i].color = c != null ? Pink : Muted;
                CompyaUiKit.SetButtonText(slots[i], c != null ? $"{c.DisplayName}\n체력 {GMCheerleaderStats.Stamina(c)}" : "빈 슬롯");
                slots[i].targetGraphic.color = c == null ? ButtonIdle : c == selected ? Selected : GMCheerleaderStats.IsTired(c) ? new Color(0.45f, 0.2f, 0.2f) : EntryOn;
            }

            var s = selected;
            if (s != null && team.CheerleaderPool.Contains(s))
            {
                int idx = GMCheerleaderRoster.EntryIndexOf(team, s);
                detailName.text = $"{s.DisplayName}  {CheerGrowth.StarBadge(s)}";
                detailInfo.text = $"{s.Grade.Display()} · +{CheerGrowth.Reinforce(s)}강 · {GMCheerleaderStats.StarFrameLabel(s)} · 체력 {GMCheerleaderStats.Stamina(s)}" +
                                  (GMCheerleaderStats.IsTired(s) ? "(효율 50%)" : "") + (idx >= 0 ? $" · {idx + 1}.{CheerSquad.RoleName((CheerRole)idx)}" : " · 벤치");
                detailCheer.text = $"CHEER {GMCheerleaderStats.Cheer(s)}";
                var frame = GMCheerleaderStats.StarFrameColor(s);
                detailFrame.color = new Color(frame.r, frame.g, frame.b, 0.16f);
                for (int k = 0; k < 4; k++)
                {
                    int v = GMCheerleaderStats.Stat(s, (GMCheerStat)k);
                    gaugeValues[k].text = v.ToString();
                    gaugeFills[k].anchorMin = Vector2.zero;
                    gaugeFills[k].anchorMax = new Vector2(Mathf.Clamp01(v / 100f), 1f);
                    gaugeFills[k].offsetMin = gaugeFills[k].offsetMax = Vector2.zero;
                }
                CompyaUiKit.SetButtonText(toggleButton, idx >= 0 ? "엔트리 해제" : "엔트리 배치");
                dedicateButton.interactable = GMCheerleaderRoster.IsAceOrLeader(team, s);
                var dedicated = GMCheerleaderRoster.DedicatedPlayerOf(team, s);
                dedicationTargetButton.interactable = dedicateButton.interactable;
                CompyaUiKit.SetButtonText(dedicationTargetButton, dedicated != null ? $"전담 대상 변경 ▶ (현재 {dedicated.Template.PlayerName})" : "전담 대상 직접 지정 ▶ (Ego 4+ 스타 순환)");
            }
            else
            {
                detailName.text = "치어리더를 선택하십시오";
                detailInfo.text = detailCheer.text = "";
                for (int k = 0; k < 4; k++) { gaugeValues[k].text = "-"; gaugeFills[k].anchorMax = new Vector2(0f, 1f); }
                dedicateButton.interactable = false;
                dedicationTargetButton.interactable = false;
            }

            for (int i = 0; i < PoolRows; i++)
            {
                var c = i < team.CheerleaderPool.Count ? team.CheerleaderPool[i] : null;
                poolButtons[i].gameObject.SetActive(c != null);
                if (c == null) continue;
                int idx = GMCheerleaderRoster.EntryIndexOf(team, c);
                bool dedicated = team.CheerDedications.Any(d => d.CheerleaderId == c.InstanceId);
                CompyaUiKit.SetButtonText(poolButtons[i],
                    $"{CheerGrowth.StarBadge(c)} {c.DisplayName} · {c.Grade.Display()} +{CheerGrowth.Reinforce(c)}강 · CHEER {GMCheerleaderStats.Cheer(c)} · 체력 {GMCheerleaderStats.Stamina(c)}" +
                    (idx >= 0 ? $" · [엔트리 {idx + 1}.{CheerSquad.RoleName((CheerRole)idx)}]" : " · 벤치") + (dedicated ? " · 전담" : ""));
                poolButtons[i].targetGraphic.color = c == selected ? Selected : idx >= 0 ? new Color(EntryOn.r, EntryOn.g, EntryOn.b, 0.55f) : ButtonIdle;
            }

            dedicationText.text = GMCheerleaderRoster.DedicationLabel(team);
            CompyaUiKit.SetButtonText(rotateButton, team.CheerAutoRotate ? "자동 로테이션: 켜짐" : "자동 로테이션: 꺼짐");
        }
    }
}
