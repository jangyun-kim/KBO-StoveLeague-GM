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
    /// [TASK-GM-16] 텍스트 위주 레이아웃 폐기 → 「단상 라인업 포스터」(덕질 포인트):
    ///   - 좌: cheerleader_lineup.png 포스터 템플릿 6칸에 엔트리 4~6인 프로필 사진(Slot0~5, 비면 "빈 슬롯") - 역할 태그 · CHEER · 이름판
    ///   - 중: 선택 치어리더 상세 - 사진('26.png) · 이름 · 등급 · 강화/★각성 · 체력 · CHEER + 4대 응원 스탯 게이지 · [엔트리 배치/해제] [전담 응원] [자동 편성]
    ///   - 우: 엔트리 시너지 · 효과(구단 소속 시너지 · 팀워크 · 실책 억제 · 클러치 · 홈 흥행 · 역할 효과) + 4대 스탯 합계 막대 + 15인 풀 사진 갤러리(Pool0~14)
    /// 대시보드 [치어리더 엔트리 (4~6인)] · 전력 비교 [라인업/치어리더 점검] · 허브 응원단 포스터가 연다. 기존 보유 치어리더 인벤토리 · 강화 · 각성 · 영입 화면은 그대로 둔다.
    /// </summary>
    public class GMCheerleaderEntryUIController : MonoBehaviour
    {
        public const string RootName = "CheerEntryRoot";
        public const int TitlePt = 26, SummaryPt = 18, RolePt = 16, SlotPt = 17, DetailNamePt = 24, DetailPt = 17, CheerPt = 34, GaugePt = 18, ButtonPt = 20, PoolPt = 16;
        public const int SlotCount = 6, PoolRows = GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX;
        public const float PosterX = 20, PosterY = 124, PosterH = 940;
        private const string NL = "\n";

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
        private Text title, summary, message, detailName, detailInfo, detailCheer, dedicationText, synergyText;
        private readonly Button[] slots = new Button[SlotCount];
        private readonly RawImage[] slotPhotos = new RawImage[SlotCount];
        private readonly Text[] gaugeValues = new Text[4], totalValues = new Text[4];
        private readonly RectTransform[] gaugeFills = new RectTransform[4], totalFills = new RectTransform[4];
        private readonly Button[] poolButtons = new Button[PoolRows];
        private readonly RawImage[] poolPhotos = new RawImage[PoolRows];
        private RawImage posterBg, detailPhoto;
        private Image detailFrame;
        private Button closeButton, toggleButton, dedicateButton, autoButton, rotateButton, backButton;
        private Button dedicationTargetButton; // [TASK-GM-06] D.3
        private Button reinforceButton;        // [TASK-GM-17] 응원단 육성(강화)

        private GMTeamState team;
        private Cheerleader selected;
        private Action onClosed;
        private Font font;

        public RectTransform Root => root;
        public GMTeamState CurrentTeam => team;
        public Cheerleader SelectedCheerleader => selected;
        public string LastMessage => message != null ? message.text : null;
        /// <summary>[TASK-GM-16] 포스터 슬롯 i의 사진(검증용).</summary>
        public RawImage SlotPhoto(int i) => i >= 0 && i < SlotCount ? slotPhotos[i] : null;
        public RawImage DetailPhoto => detailPhoto;
        public string SynergyText => synergyText != null ? synergyText.text : "";

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
            font = regularFont != null ? regularFont : TextTidy.BodyFont;
            var kit = new CompyaUiKit(font, font);
            using (CompyaUiKit.Wide()) // [TASK-GM-06] 1920×1080 Landscape · [TASK-GM-16] 좌 포스터 · 중 상세 · 우 시너지/풀
            {
            root = CompyaUiKit.Fill(transform, RootName);
            CompyaUiKit.Paint(root, Bg, true);

            title = L(kit, "Title", "치어리더 엔트리", 24, 12, 1780, 72, TitlePt, TextAnchor.MiddleLeft, Pink);
            closeButton = Btn(kit, "CloseButton", "X", 1800, 12, 1908, 72, ButtonIdle, ButtonPt + 2);
            closeButton.onClick.AddListener(Close);
            summary = L(kit, "Summary", "", 24, 78, 1908, 118, SummaryPt, TextAnchor.MiddleLeft, White);

            // ---- 좌: 단상 라인업 포스터
            float pw = GMCheerPoster.WidthFor(PosterH);
            var posterRect = CompyaUiKit.Place(root, "PosterBg", PosterX, PosterY, PosterX + pw, PosterY + PosterH);
            posterBg = posterRect.gameObject.AddComponent<RawImage>();
            posterBg.raycastTarget = false;
            posterBg.color = new Color(0.16f, 0.2f, 0.34f);
            for (int i = 0; i < SlotCount; i++)
            {
                int index = i;
                var r = GMCheerPoster.SlotRect(i, PosterX, PosterY, PosterH);
                slots[i] = Btn(kit, $"Slot{i}", "", r.x0, r.y0, r.x1, r.y1, new Color(1f, 1f, 1f, 0.001f), SlotPt);
                slotPhotos[i] = GMCheerPoster.Decorate(slots[i], font, SlotPt, RolePt, true, Pink, i);
                slots[i].onClick.AddListener(() => SelectSlot(index));
            }
            GMCheerPoster.ApplyDrawOrder(slots); // [TASK-GM-17] 위 칸이 사선 모서리를 덮도록

            // ---- 중: 선택 치어리더 상세(사진 · 4대 스탯)
            float dx = PosterX + pw + 14; // ≈ 786
            detailFrame = CompyaUiKit.Box(root, "DetailFrame", dx - 6, 124, 1340, 812, new Color(1f, 1f, 1f, 0.05f));
            var photoHolder = CompyaUiKit.Place(root, "DetailPhotoFrame", dx, 130, dx + 230, 352);
            CompyaUiKit.Paint(photoHolder, new Color(1f, 1f, 1f, 0.08f));
            photoHolder.gameObject.AddComponent<RectMask2D>();
            var photoRect = CompyaUiKit.Fill(photoHolder, "Photo");
            detailPhoto = photoRect.gameObject.AddComponent<RawImage>();
            detailPhoto.raycastTarget = false;
            photoRect.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            detailName = L(kit, "DetailName", "", dx + 240, 130, 1334, 176, DetailNamePt, TextAnchor.MiddleLeft, White);
            detailCheer = L(kit, "DetailCheer", "", dx + 240, 180, 1334, 232, CheerPt, TextAnchor.MiddleLeft, Pink);
            detailInfo = L(kit, "DetailInfo", "", dx + 240, 236, 1334, 352, DetailPt - 1, TextAnchor.UpperLeft, Muted);
            for (int k = 0; k < 4; k++)
            {
                float y0 = 362 + k * 52;
                L(kit, $"GaugeLabel{k}", GMCheerleaderStats.StatLabels[k], dx, y0, dx + 150, y0 + 46, GaugePt, TextAnchor.MiddleLeft, White);
                var bg = CompyaUiKit.Box(root, $"GaugeBg{k}", dx + 156, y0 + 12, 1262, y0 + 34, new Color(1f, 1f, 1f, 0.1f));
                gaugeFills[k] = CompyaUiKit.Fill(bg.transform, "Fill");
                CompyaUiKit.Paint(gaugeFills[k], GaugeColors[k]);
                gaugeValues[k] = L(kit, $"GaugeValue{k}", "", 1268, y0, 1334, y0 + 46, GaugePt + 1, TextAnchor.MiddleRight, White);
            }
            toggleButton = Btn(kit, "ToggleEntryButton", "엔트리 배치", dx, 576, dx + 270, 630, EntryOn, ButtonPt);
            dedicateButton = Btn(kit, "DedicateButton", "전담 응원 지정", dx + 278, 576, 1334, 630, new Color(0.45f, 0.3f, 0.65f), ButtonPt);
            autoButton = Btn(kit, "AutoArrangeButton", "최적 컨디션 5인 자동 편성", dx, 638, dx + 270, 690, new Color(0.15f, 0.45f, 0.85f), ButtonPt - 2);
            reinforceButton = Btn(kit, "ReinforceButton", "응원단 육성 +1강", dx + 278, 638, 1334, 690, new Color(0.62f, 0.45f, 0.12f), ButtonPt - 2); // [TASK-GM-17]
            reinforceButton.onClick.AddListener(() => ReinforceSelected());
            toggleButton.onClick.AddListener(() => ToggleSelected());
            dedicateButton.onClick.AddListener(() => DedicateSelected());
            autoButton.onClick.AddListener(AutoArrange);
            // [TASK-GM-06] GM-05 D.3 - 전담 응원 대상 선수 직접 지정(클릭할 때마다 다음 Ego 4+ 스타로 순환)
            dedicationTargetButton = Btn(kit, "DedicationTargetButton", "전담 대상 변경 ▶", dx, 698, 1334, 748, new Color(0.38f, 0.22f, 0.5f), ButtonPt - 2);
            dedicationTargetButton.onClick.AddListener(() => CycleDedicationTarget());
            message = L(kit, "Message", "", dx, 756, 1334, 806, DetailPt - 1, TextAnchor.MiddleLeft, Pink);
            dedicationText = L(kit, "DedicationText", "", dx, 818, 1334, 940, DetailPt - 1, TextAnchor.UpperLeft, Muted);
            rotateButton = Btn(kit, "AutoRotateButton", "자동 로테이션: 켜짐", dx, 950, dx + 270, 1060, ButtonIdle, ButtonPt - 1);
            backButton = Btn(kit, "BackButton", "닫기", dx + 278, 950, 1334, 1060, new Color(0.3f, 0.3f, 0.42f), ButtonPt + 1);
            rotateButton.onClick.AddListener(ToggleAutoRotate);
            backButton.onClick.AddListener(Close);

            // ---- 우: 엔트리 시너지 · 4대 스탯 합계 · 15인 풀 갤러리
            L(kit, "SynergyTitle", "엔트리 시너지 · 효과", 1352, 124, 1908, 160, SummaryPt + 2, TextAnchor.MiddleLeft, Pink);
            synergyText = L(kit, "SynergyText", "", 1352, 164, 1908, 410, PoolPt, TextAnchor.UpperLeft, White);
            synergyText.lineSpacing = 1.1f;
            for (int k = 0; k < 4; k++)
            {
                float y0 = 416 + k * 38;
                L(kit, $"TotalLabel{k}", GMCheerleaderStats.StatLabels[k] + " 합계", 1352, y0, 1520, y0 + 34, PoolPt, TextAnchor.MiddleLeft, Muted);
                var bg = CompyaUiKit.Box(root, $"TotalBg{k}", 1526, y0 + 9, 1800, y0 + 25, new Color(1f, 1f, 1f, 0.1f));
                totalFills[k] = CompyaUiKit.Fill(bg.transform, "Fill");
                CompyaUiKit.Paint(totalFills[k], GaugeColors[k]);
                totalValues[k] = L(kit, $"TotalValue{k}", "", 1806, y0, 1908, y0 + 34, PoolPt, TextAnchor.MiddleRight, White);
            }
            L(kit, "PoolTitle", "구단 응원단 15인 - 눌러서 상세 · 배치", 1352, 572, 1908, 604, PoolPt + 1, TextAnchor.MiddleLeft, Pink);
            for (int i = 0; i < PoolRows; i++)
            {
                int index = i;
                int col = i % 3, row = i / 3;
                float x0 = 1352 + col * 186, y0 = 610 + row * 90;
                poolButtons[i] = kit.Button(root, $"Pool{i}", "", x0, y0, x0 + 180, y0 + 84, ButtonIdle, White, PoolPt / 0.9f, bold: false);
                var label = poolButtons[i].GetComponentInChildren<Text>(true);
                label.alignment = TextAnchor.MiddleLeft;
                TextTidy.Exact(label, PoolPt);
                var lr = (RectTransform)label.transform;
                lr.anchorMin = new Vector2(0.42f, 0f);
                lr.offsetMin = new Vector2(4, 0);
                var ph = CompyaUiKit.Norm(poolButtons[i].transform, "PhotoMask", 0.02f, 0.04f, 0.4f, 0.96f);
                ph.gameObject.AddComponent<RectMask2D>();
                var pr = CompyaUiKit.Fill(ph, "Photo");
                poolPhotos[i] = pr.gameObject.AddComponent<RawImage>();
                poolPhotos[i].raycastTarget = false;
                pr.gameObject.AddComponent<AspectRatioFitter>().aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                poolButtons[i].onClick.AddListener(() => SelectPool(index));
            }
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

        /// <summary>[TASK-GM-17] [응원단 육성 +1강] - [TASK-GM-18] 마케팅 예산으로 강화(클래식 로비 성장 화면 대체).</summary>
        public bool ReinforceSelected()
        {
            if (team == null || selected == null) return false;
            bool ok = GMCheerleaderRoster.TryReinforce(team, selected, out string reason);
            message.text = reason;
            Refresh();
            return ok;
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
            title.text = $"{NameAliasTable.DisplayTeamName(team.TeamCode)} 응원단 엔트리 · 마케팅 예산 {GMDiagnosticFormat.Short(team.MarketingBudget)}"; // [TASK-GM-18] 응원단 전용 재화
            summary.text = $"{GMCheerleaderRules.Summary(entry.Count, team.CheerleaderPool.Count)} · 팀워크 +{GMCheerleaderRoster.LeadershipTeamworkBonus(entry)} · " +
                           $"실책 -{GMCheerleaderRoster.ErrorReduction(entry) * 100f:0}% · 홈 흥행 +{GMCheerleaderRoster.HomeRevenue(entry):N0}만 원";

            // [TASK-GM-16] 단상 라인업 포스터 - 템플릿(없으면 구단색) · 슬롯 사진 · 역할 · CHEER · 이름판
            var template = GMCheerPortraits.LineupTemplate(team.Team);
            posterBg.texture = template;
            posterBg.color = template != null ? Color.white : TeamThemePalette.Primary(team.Team) * 0.6f + new Color(0f, 0f, 0f, 0.4f);
            for (int i = 0; i < SlotCount; i++)
            {
                var c = i < entry.Count ? entry[i] : null;
                GMCheerPoster.Fill(slots[i], slotPhotos[i], c, i, CheerSquad.RoleName((CheerRole)i),
                    c != null ? $"CHEER {GMCheerleaderStats.Cheer(c)} · 체력 {GMCheerleaderStats.Stamina(c)}" : "");
                slots[i].targetGraphic.color = c == null ? new Color(1f, 1f, 1f, template != null ? 0.001f : 0.12f)
                    : c == selected ? new Color(Selected.r, Selected.g, Selected.b, 0.45f)
                    : GMCheerleaderStats.IsTired(c) ? new Color(0.45f, 0.2f, 0.2f, 0.45f) : new Color(1f, 1f, 1f, 0.001f);
            }

            var synergy = GMCheerSynergy.Lines(team, entry);
            synergyText.text = string.Join(NL, synergy.Where(x => !string.IsNullOrEmpty(x)));
            for (int k = 0; k < 4; k++)
            {
                var (total, max) = GMCheerSynergy.StatTotal(entry, k);
                totalValues[k].text = total.ToString();
                totalFills[k].anchorMin = Vector2.zero;
                totalFills[k].anchorMax = new Vector2(max > 0 ? Mathf.Clamp01(total / (float)(GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MAX * 100)) : 0f, 1f);
                totalFills[k].offsetMin = totalFills[k].offsetMax = Vector2.zero;
            }

            var s = selected;
            if (s != null && team.CheerleaderPool.Contains(s))
            {
                int idx = GMCheerleaderRoster.EntryIndexOf(team, s);
                detailName.text = $"{s.DisplayName}  {CheerGrowth.StarBadge(s)}";
                detailInfo.text = $"{s.Grade.Display()} · +{CheerGrowth.Reinforce(s)}강 · {GMCheerleaderStats.StarFrameLabel(s)}{NL}체력 {GMCheerleaderStats.Stamina(s)}" +
                                  (GMCheerleaderStats.IsTired(s) ? "(효율 50%)" : "") + (idx >= 0 ? $" · {idx + 1}.{CheerSquad.RoleName((CheerRole)idx)}" : " · 벤치") +
                                  (string.IsNullOrEmpty(s.AffiliationLabel) ? "" : $"{NL}{CompyaUiKit.ShortName(s.Team)} {s.ActivePeriod}");
                detailCheer.text = $"CHEER {GMCheerleaderStats.Cheer(s)}";
                GMCheerPortraits.SetTexture(detailPhoto, GMCheerPortraits.Photo(s)); // [TASK-GM-16] 상세 사진
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
                bool maxed = CheerGrowth.Reinforce(s) >= CheerGrowth.MaxReinforce;
                CompyaUiKit.SetButtonText(reinforceButton, maxed ? "육성 완료(+10강)" : $"응원단 육성 +1강 (-{GMDiagnosticFormat.Short(GMCheerleaderRoster.ReinforceCost(s))})");
                reinforceButton.interactable = !maxed;
                dedicateButton.interactable = GMCheerleaderRoster.IsAceOrLeader(team, s);
                var dedicated = GMCheerleaderRoster.DedicatedPlayerOf(team, s);
                dedicationTargetButton.interactable = dedicateButton.interactable;
                CompyaUiKit.SetButtonText(dedicationTargetButton, dedicated != null ? $"전담 대상 변경 ▶ (현재 {dedicated.Template.PlayerName})" : "전담 대상 직접 지정 ▶ (Ego 4+ 스타 순환)");
            }
            else
            {
                detailName.text = "치어리더를 선택하십시오";
                detailInfo.text = detailCheer.text = "";
                GMCheerPortraits.SetTexture(detailPhoto, null);
                for (int k = 0; k < 4; k++) { gaugeValues[k].text = "-"; gaugeFills[k].anchorMax = new Vector2(0f, 1f); }
                dedicateButton.interactable = false;
                dedicationTargetButton.interactable = false;
                reinforceButton.interactable = false;
            }

            for (int i = 0; i < PoolRows; i++)
            {
                var c = i < team.CheerleaderPool.Count ? team.CheerleaderPool[i] : null;
                poolButtons[i].gameObject.SetActive(c != null);
                if (c == null) continue;
                int idx = GMCheerleaderRoster.EntryIndexOf(team, c);
                bool dedicated = team.CheerDedications.Any(d => d.CheerleaderId == c.InstanceId);
                CompyaUiKit.SetButtonText(poolButtons[i], $"{c.DisplayName}{NL}{(idx >= 0 ? $"{idx + 1}.{CheerSquad.RoleName((CheerRole)idx)}" : "벤치")}{(dedicated ? " · 전담" : "")}");
                GMCheerPortraits.SetTexture(poolPhotos[i], GMCheerPortraits.Profile(c));
                poolButtons[i].targetGraphic.color = c == selected ? Selected : idx >= 0 ? new Color(EntryOn.r, EntryOn.g, EntryOn.b, 0.55f) : ButtonIdle;
            }

            dedicationText.text = GMCheerleaderRoster.DedicationLabel(team);
            CompyaUiKit.SetButtonText(rotateButton, team.CheerAutoRotate ? "자동 로테이션: 켜짐" : "자동 로테이션: 꺼짐");
        }
    }
}
