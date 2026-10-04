using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>[TASK-KBO-186] 선수 상세정보 표시 문자열/수치 규칙(순수 로직 - Task186Tests 대상).</summary>
    public static class PlayerDetailRules
    {
        public sealed class StatRow
        {
            public string Label;
            public int Base;
            public int Bonus;
            /// <summary>[TASK-KBO-190] 상시 스킬(3슬롯) 가산 - 조건부 스킬은 경기 중 해당 상황에서만 붙어 여기 넣지 않는다.</summary>
            public int Skill;
            public int Final => Base + Bonus + Skill;
        }

        public enum BarTier { Sky, Green, Blue, Elite }

        /// <summary>게이지 바 100% 기준 수치(최종 OVR 상한 144를 넘는 여유).</summary>
        public const float BarFullValue = 150f;

        public static string Title(Player p) => p?.Template == null ? "" : $"{p.Template.PlayerName} '{p.Template.SeasonYear % 100:00}";

        public static string Subtitle(Player p)
        {
            if (p?.Template == null) return "";
            var t = p.Template;
            string position = t.IsPitcher ? RoleName(LineupAssignment.RoleOf(p)) : LineupView.PositionName(t.BatterPosition);
            return $"{CompyaUiKit.FullName(t.Team)} · {position} · {CardGrowthRules.DisplayName(t.Grade)}";
        }

        public static string RoleName(PitcherRole role) => role switch
        {
            PitcherRole.StartingPitcher => "선발 투수",
            PitcherRole.WinningReliever => "승리조",
            PitcherRole.MopUpReliever => "추격조",
            PitcherRole.LongReliever => "롱릴리프",
            _ => "마무리",
        };

        public static int FinalOvr(Player p, int synergy) => p?.Template == null ? 0 : p.BaseOvr + p.GetStatGrowth() + Mathf.Max(0, synergy);

        public static string OvrBreakdown(Player p, int synergy) =>
            p?.Template == null ? "" : $"기본 {p.BaseOvr} + 성장 +{p.GetStatGrowth()} + 세트덱 +{Mathf.Max(0, synergy)}";

        public static string SetDeckBadge(Player p) =>
            p?.Template == null ? "" : $"세트덱 스코어: SD {p.SetDeckScore}점 (최대 {CardGrowthRules.MaxSetDeckScore(p.Template.Grade)}점)";

        /// <summary>4대 성장 2×2 - (제목, 값). 순서: 강화 · 각성 · 한계돌파 · 특훈.</summary>
        public static List<(string Title, string Value)> GrowthCells(Player p)
        {
            var cells = new List<(string, string)>();
            if (p?.Template == null) return cells;
            var g = p.Template.Grade;
            bool canTranscend = CardGrowthRules.CanTranscend(g);
            string transcend = CardGrowthRules.IsTranscended(g, p.AwakenLevel) ? "초월 달성" : canTranscend ? "초월 가능" : "초월 불가";
            cells.Add(("강화", $"+{p.ReinforceLevel} / {Player.MaxReinforceLevel}강"));
            cells.Add(("각성", $"{Mathf.Min(p.AwakenLevel, CardGrowthRules.NineStageAwakenCap)}각 / 9각 ({transcend})"));
            cells.Add(("한계돌파", $"{p.LimitBreakGrowth} / {CardGrowthRules.LimitBreakCap(g)}단계"));
            cells.Add(("특훈", $"{p.TrainingGrowth} / {CardGrowthRules.TrainingCap(g)}단계"));
            return cells;
        }

        /// <summary>5대 세부 능력치(타자: 파워/정확/선구/주력/수비, 투수: 구위/구속/변화/제구/체력) - 기본 + (성장 + 세트덱).</summary>
        public static List<StatRow> StatRows(Player p, int synergy)
        {
            var rows = new List<StatRow>();
            if (p?.Template == null) return rows;
            int bonus = p.GetStatGrowth() + Mathf.Max(0, synergy);
            var t = p.Template;
            var pairs = t.IsPitcher
                ? new[] { ("구위", t.PitcherStats.Stuff), ("구속", t.PitcherStats.Velocity), ("변화", t.PitcherStats.Movement), ("제구", t.PitcherStats.Control), ("체력", t.PitcherStats.Stamina) }
                : new[] { ("파워", t.BatterStats.Power), ("정확", t.BatterStats.Contact), ("선구", t.BatterStats.Discipline), ("주력", t.BatterStats.Speed), ("수비", t.BatterStats.Defense) };
            var skill = PlayerSkillRules.AlwaysStatBonuses(p); // [TASK-KBO-190]
            for (int i = 0; i < pairs.Length; i++) rows.Add(new StatRow { Label = pairs[i].Item1, Base = pairs[i].Item2, Bonus = bonus, Skill = i < skill.Length ? skill[i] : 0 });
            return rows;
        }

        public static BarTier TierOf(int value) => value >= 100 ? BarTier.Elite : value >= 90 ? BarTier.Blue : value >= 80 ? BarTier.Green : BarTier.Sky;

        public static float FillRatio(int value) => Mathf.Clamp01(value / BarFullValue);

        public static Color BarColor(BarTier tier) => tier switch
        {
            BarTier.Elite => new Color(0.66f, 0.4f, 0.98f),
            BarTier.Blue => new Color(0.24f, 0.52f, 1f),
            BarTier.Green => new Color(0.3f, 0.8f, 0.45f),
            _ => new Color(0.45f, 0.76f, 0.96f),
        };
    }

    /// <summary>
    /// [TASK-KBO-186] 선수 상세정보 4단 카드 레이아웃(1080×1920 기준, 코드 빌드 - Setup과 런타임 Awake가 같은 Build()를 쓴다).
    ///   1단 헤더(110px): 구단 로고 + "{선수명} '{연도}"(36 Bold) + "{구단} · {포지션} · {등급}"(24) | [X](80×80, 36)
    ///   2단(460px): 좌 340px 카드 프리뷰(PlayerCardUI 원래 비율 - CardHolderFit) | 우 다크 요약(OVR 44 Gold · 분해 24 · SD 배지 26 · 4대 성장 2×2 네이비 캡슐)
    ///   3단(480px): 5대 능력치 행 - 이름(28 Bold, 120px) + 가로 게이지(100↑ 퍼플/골드, 90↑ 블루, 80↑ 그린, 그 외 스카이) + 최종 수치(32 Bold) + (기본 A + 성장/시너지 +B)(22)
    ///   4단: 보유 스킬/특성 3칸(#162032 카드 · 아이콘 · 명칭 26 Bold Gold · 설명 22 White) + 하단 [선수 관리 (성장 센터로 이동)](90px, 30 Bold) / [닫기]
    /// 예전 탭형 패널(늘어난 배경 이미지 · 밝은 회색 박스 · 같은 좌표에 겹친 노란 텍스트 5개)은 Build()가 통째로 숨긴다.
    /// </summary>
    public sealed class PlayerDetailLayout186
    {
        public const string RootName = "Detail186";
        public const float RefW = 1080f, RefH = 1920f;
        public const int MinFont = TextTidy.AutoMin; // [TASK-KBO-191] 18 → 11(좁은 칸에서 넘치지 않고 줄어든다)

        private static readonly Color Bg = new Color(0.043f, 0.071f, 0.125f);          // #0B1220
        private static readonly Color HeaderBg = new Color(0.067f, 0.102f, 0.18f);     // #111A2E
        private static readonly Color CardBg = new Color(0.086f, 0.125f, 0.196f);      // #162032
        private static readonly Color Capsule = new Color(0.118f, 0.161f, 0.231f);     // #1E293B
        private static readonly Color Track = new Color(0.2f, 0.25f, 0.34f);
        private static readonly Color White = new Color(0.96f, 0.97f, 0.99f);
        private static readonly Color Muted = new Color(0.66f, 0.72f, 0.82f);
        private static readonly Color Gold = new Color(1f, 0.8f, 0.25f);
        private static readonly Color Blue = new Color(0.16f, 0.42f, 0.95f);
        private static readonly Color Grey = new Color(0.3f, 0.34f, 0.42f);

        private readonly Font bold, regular;
        public RectTransform Root { get; private set; }
        public Button CloseX { get; private set; }
        public Button ManageButton { get; private set; }
        public Button CloseButton { get; private set; }
        public CardHolderFit CardHolder { get; private set; }

        private RawImage logo;
        private Text title, subtitle, ovrText, breakdownText, sdText;
        private readonly Text[] growthTitles = new Text[4];
        private readonly Text[] growthValues = new Text[4];
        private readonly Text[] statNames = new Text[5];
        private readonly Text[] statValues = new Text[5];
        private readonly Text[] statSubs = new Text[5];
        private readonly Image[] statFills = new Image[5];
        private readonly RectTransform[] statFillRects = new RectTransform[5];
        private readonly Text[] skillIcons = new Text[3];
        private readonly Text[] skillNames = new Text[3];
        private readonly Text[] skillDescs = new Text[3];

        public PlayerDetailLayout186(Font boldFont, Font regularFont)
        {
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            bold = boldFont != null ? boldFont : regularFont != null ? regularFont : fallback;
            regular = regularFont != null ? regularFont : bold;
        }

        /// <summary>panel 아래 Detail186을 새로 만들고 나머지(구 탭형 상세) 자식을 숨긴다. preview 카드는 새 카드 칸으로 옮긴다.</summary>
        public void Build(RectTransform panel, PlayerCardUI preview, Transform keepActive = null)
        {
            var old = panel.Find(RootName);
            if (old != null)
            {
                if (preview != null && preview.transform.IsChildOf(old)) preview.transform.SetParent(panel, false);
                old.name = "_" + RootName + "_old";
                old.SetParent(null, false);
                if (Application.isPlaying) Object.Destroy(old.gameObject); else Object.DestroyImmediate(old.gameObject);
            }
            foreach (Transform child in panel)
                if (keepActive == null || !keepActive.IsChildOf(child)) child.gameObject.SetActive(false);

            Root = CompyaUiKit.Fill(panel, RootName);
            Root.SetAsLastSibling();
            Paint(Root, Bg, true); // 아래 화면 클릭 차단

            BuildHeader();
            BuildSummary(preview);
            BuildStats();
            BuildSkills();
        }

        // ------------------------------------------------------------------ 1단 헤더

        private void BuildHeader()
        {
            Box("Header", 0, 0, 1080, 110, HeaderBg);
            logo = CompyaUiKit.Logo(Root, "TeamLogo", 20, 15, 100, 95);
            title = Label("Title", 116, 6, 950, 62, 26, TextAnchor.MiddleLeft, White, true);
            subtitle = Label("Subtitle", 116, 62, 950, 106, 16, TextAnchor.MiddleLeft, Muted, false);
            CloseX = MakeButton("CloseX", "X", 980, 15, 1060, 95, Grey, White, 24);
        }

        // ------------------------------------------------------------------ 2단 카드 + 요약

        private void BuildSummary(PlayerCardUI preview)
        {
            var holderRect = Place("CardHolder", 20, 125, 360, 585);
            Paint(holderRect, CardBg);
            CardHolder = holderRect.gameObject.AddComponent<CardHolderFit>();
            var native = preview != null ? CardHolderFit.NativeSizeOf(preview) : CardHolderFit.DefaultCardSize;
            if (native.x > native.y || native.y > 600f) native = CardHolderFit.DefaultCardSize; // 늘어난 구 프리뷰 크기는 쓰지 않는다
            CardHolder.Configure(native, 0.94f);
            if (preview != null)
            {
                preview.gameObject.SetActive(true);
                CardHolder.Place((RectTransform)preview.transform);
            }

            Box("SummaryBox", 376, 125, 1060, 585, CardBg);
            ovrText = Label("Ovr", 396, 133, 1044, 196, 40, TextAnchor.MiddleLeft, Gold, true);
            breakdownText = Label("OvrBreakdown", 396, 196, 1044, 236, 16, TextAnchor.MiddleLeft, White, false);
            Box("SdBadge", 396, 246, 1044, 300, new Color(0.17f, 0.24f, 0.4f));
            sdText = Label("SdText", 412, 246, 1032, 300, 18, TextAnchor.MiddleLeft, White, true);

            for (int i = 0; i < 4; i++)
            {
                float x0 = i % 2 == 0 ? 396 : 724, x1 = x0 + 320;
                float y0 = i < 2 ? 314 : 448, y1 = y0 + 124;
                Box($"GrowthCell{i}", x0, y0, x1, y1, Capsule);
                growthTitles[i] = Label($"GrowthTitle{i}", x0 + 16, y0 + 8, x1 - 16, y0 + 50, 15, TextAnchor.MiddleLeft, Muted, true);
                growthValues[i] = Label($"GrowthValue{i}", x0 + 16, y0 + 52, x1 - 16, y1 - 10, 17, TextAnchor.MiddleLeft, Gold, true);
            }
        }

        // ------------------------------------------------------------------ 3단 능력치 게이지

        private void BuildStats()
        {
            Label("StatsTitle", 24, 600, 1060, 650, 20, TextAnchor.MiddleLeft, White, true).text = "세부 능력치";
            for (int i = 0; i < 5; i++)
            {
                float y0 = 656 + i * 86, y1 = y0 + 78, cy = (y0 + y1) / 2f;
                Box($"StatRow{i}", 20, y0, 1060, y1, HeaderBg);
                statNames[i] = Label($"StatName{i}", 36, y0, 156, y1, 17, TextAnchor.MiddleLeft, White, true);
                var track = Place($"StatTrack{i}", 166, cy - 14, 640, cy + 14);
                Paint(track, Track);
                statFillRects[i] = CompyaUiKit.Norm(track, "Fill", 0f, 0f, 0.5f, 1f);
                statFills[i] = Paint(statFillRects[i], PlayerDetailRules.BarColor(PlayerDetailRules.BarTier.Sky));
                statValues[i] = Label($"StatValue{i}", 652, y0, 760, y1, 18, TextAnchor.MiddleCenter, White, true);
                statSubs[i] = Label($"StatSub{i}", 764, y0, 1052, y1, 15, TextAnchor.MiddleLeft, Muted, false);
            }
        }

        // ------------------------------------------------------------------ 4단 스킬 + 버튼

        private void BuildSkills()
        {
            Label("SkillsTitle", 24, 1095, 1060, 1145, 20, TextAnchor.MiddleLeft, White, true).text = "보유 스킬 · 특성";
            for (int i = 0; i < 3; i++)
            {
                float x0 = 20 + i * 352, x1 = x0 + 336;
                Box($"SkillCard{i}", x0, 1152, x1, 1760, CardBg);
                var icon = Place($"SkillIconBg{i}", x0 + 128, 1176, x0 + 208, 1256);
                Paint(icon, Capsule);
                CompyaUiKit.Outline(icon.GetComponent<Image>(), Gold, 2f);
                skillIcons[i] = Label($"SkillIcon{i}", x0 + 128, 1176, x0 + 208, 1256, 24, TextAnchor.MiddleCenter, Gold, true);
                skillNames[i] = Label($"SkillName{i}", x0 + 14, 1268, x1 - 14, 1340, 17, TextAnchor.MiddleCenter, Gold, true);
                skillDescs[i] = Label($"SkillDesc{i}", x0 + 18, 1346, x1 - 18, 1748, 15, TextAnchor.UpperLeft, White, false);
            }

            ManageButton = MakeButton("ManageButton", "선수 관리 (성장 센터로 이동)", 20, 1800, 700, 1890, Blue, White, 20);
            CloseButton = MakeButton("CloseButton", "닫기", 720, 1800, 1060, 1890, Grey, White, 20);
        }

        // ------------------------------------------------------------------ 채우기

        public void Fill(Player player, int synergy, SkillDB skillDB)
        {
            if (Root == null || player?.Template == null) return;
            CompyaUiKit.SetLogo(logo, player.Template.Team);
            title.text = PlayerDetailRules.Title(player);
            subtitle.text = PlayerDetailRules.Subtitle(player);
            ovrText.text = $"OVR {PlayerDetailRules.FinalOvr(player, synergy)}";
            breakdownText.text = PlayerDetailRules.OvrBreakdown(player, synergy);
            sdText.text = PlayerDetailRules.SetDeckBadge(player);

            var cells = PlayerDetailRules.GrowthCells(player);
            for (int i = 0; i < 4; i++)
            {
                growthTitles[i].text = i < cells.Count ? cells[i].Title : "";
                growthValues[i].text = i < cells.Count ? cells[i].Value : "";
            }

            var rows = PlayerDetailRules.StatRows(player, synergy);
            for (int i = 0; i < 5; i++)
            {
                var row = i < rows.Count ? rows[i] : null;
                statNames[i].text = row?.Label ?? "";
                statValues[i].text = row != null ? row.Final.ToString() : "";
                statSubs[i].text = row != null ? $"(기본 {row.Base} + 성장/시너지 +{row.Bonus}{(row.Skill > 0 ? $" + 스킬 +{row.Skill}" : "")})" : "";
                var tier = PlayerDetailRules.TierOf(row?.Final ?? 0);
                statFills[i].color = PlayerDetailRules.BarColor(tier);
                statValues[i].color = tier == PlayerDetailRules.BarTier.Elite ? Gold : White;
                statFillRects[i].anchorMax = new Vector2(PlayerDetailRules.FillRatio(row?.Final ?? 0), 1f);
            }

            // [TASK-KBO-190] 3슬롯 스킬(등급 D~S 컬러 · Lv.1~6 · 상세 효과) - 성장 센터 · 라인업 트레이와 같은 PlayerSkillRules 표기
            var slots = PlayerSkillRules.SlotsOf(player);
            for (int i = 0; i < 3; i++)
            {
                if (i >= slots.Count)
                {
                    skillIcons[i].text = "-";
                    skillNames[i].text = "빈 슬롯";
                    skillDescs[i].text = "보유한 스킬/특성이 없습니다.\n선수 관리 > 성장 센터 [훈련·특훈]에서 스킬을 변경할 수 있습니다.";
                    continue;
                }
                var slot = slots[i];
                skillIcons[i].supportRichText = true;
                skillIcons[i].text = $"<color={PlayerSkillRules.GradeColorHex(slot.Grade)}>{PlayerSkillRules.GradeLetter(slot.Grade)}</color>";
                skillNames[i].text = $"{PlayerSkillRules.Name(slot)} Lv.{PlayerSkillRules.ClampLevel(slot.Level)}";
                skillDescs[i].text = PlayerSkillRules.DetailText(slot);
            }
        }

        private static string TierLetter(SkillTier tier)
        {
            return tier == SkillTier.S_PLUS ? "S+" : tier.ToString();
        }

        // ------------------------------------------------------------------ 배치 도우미(1080×1920 기준 px, 좌상단 원점)

        private RectTransform Place(string name, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(Root, false);
            rect.anchorMin = new Vector2(x0 / RefW, 1f - y1 / RefH);
            rect.anchorMax = new Vector2(x1 / RefW, 1f - y0 / RefH);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Image Paint(RectTransform rect, Color color, bool raycast = false) => CompyaUiKit.Paint(rect, color, raycast);

        private Image Box(string name, float x0, float y0, float x1, float y1, Color color) => Paint(Place(name, x0, y0, x1, y1), color);

        private Text Label(string name, float x0, float y0, float x1, float y1, int size, TextAnchor anchor, Color color, bool isBold)
            => Style(Place(name, x0, y0, x1, y1).gameObject.AddComponent<Text>(), size, anchor, color, isBold);

        private Text Style(Text label, int size, TextAnchor anchor, Color color, bool isBold)
        {
            label.font = isBold ? bold : regular;
            label.fontSize = size;
            label.fontStyle = FontStyle.Normal;
            label.alignment = anchor;
            label.color = color;
            label.raycastTarget = false;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate; // 칸 밖으로 넘쳐 다른 글자와 겹치지 않게
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Min(MinFont, size);
            label.resizeTextMaxSize = size;
            // [TASK-KBO-191] 계층 크기(타이틀 26 · OVR 40 · 섹션 20 · 본문 16~18 · 보조 15 · 버튼 20) Normal 고정 + 자간
            return TextTidy.Exact(label, size, MinFont, regular);
        }

        private Button MakeButton(string name, string text, float x0, float y0, float x1, float y1, Color bg, Color fg, int size)
        {
            var rect = Place(name, x0, y0, x1, y1);
            var image = Paint(rect, bg, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var labelRect = CompyaUiKit.Fill(rect, "Text");
            labelRect.offsetMin = new Vector2(12f, 4f);
            labelRect.offsetMax = new Vector2(-12f, -4f);
            Style(labelRect.gameObject.AddComponent<Text>(), size, TextAnchor.MiddleCenter, fg, true).text = text;
            return button;
        }
    }
}
