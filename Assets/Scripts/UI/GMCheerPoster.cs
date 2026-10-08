using System;
using System.Collections.Generic;
using KBOManager.Core;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-GM-16] 치어리더 사진 · 라인업 포스터 리소스.
    ///   Resources/Image/TEAM/{구단 폴더}/Cheerleaders/{이름}[_팀장]/{이름}'26_profile.png(포스터 · 썸네일) · {이름}'26.png(상세 사진)
    ///   Resources/Image/TEAM/{구단 폴더}/Cheerleaders/cheerleader_lineup.png(560×700 단상 라인업 포스터 템플릿 - 6칸 프레임).
    ///   폴더 이름에 붙은 직함(_팀장)은 무시하고 파일 이름의 ' 앞부분(실명)으로 찾는다. 사진이 없으면 null(호출 측이 이니셜 실루엣으로 대신한다).
    /// </summary>
    public static class GMCheerPortraits
    {
        public const string LineupTemplateName = "cheerleader_lineup";
        private static readonly Dictionary<Team, Dictionary<string, Texture2D>> profiles = new Dictionary<Team, Dictionary<string, Texture2D>>();
        private static readonly Dictionary<Team, Dictionary<string, Texture2D>> photos = new Dictionary<Team, Dictionary<string, Texture2D>>();
        private static readonly Dictionary<Team, Texture2D> templates = new Dictionary<Team, Texture2D>();

        public static string TeamFolder(Team team)
        {
            switch (team)
            {
                case Team.Samsung: return "SAMSUNG";
                case Team.LG: return "LG";
                case Team.KIA: return "KIA";
                case Team.Hanwha: return "HANHWA";
                case Team.Doosan: return "DOOSAN";
                case Team.KT: return "KT";
                case Team.SSG: return "SSG";
                case Team.NC: return "NC";
                case Team.Lotte: return "LOTTE";
                case Team.Kiwoom: return "KIWOOM";
                default: return "";
            }
        }

        private static void Ensure(Team team)
        {
            if (profiles.ContainsKey(team) && AllAlive(profiles[team]) && AllAlive(photos[team])) return;
            var prof = new Dictionary<string, Texture2D>();
            var full = new Dictionary<string, Texture2D>();
            Texture2D template = null;
            string folder = TeamFolder(team);
            if (folder != "")
            {
                foreach (var tex in Resources.LoadAll<Texture2D>($"Image/TEAM/{folder}/Cheerleaders"))
                {
                    if (tex == null) continue;
                    string n = tex.name;
                    if (n == LineupTemplateName) { template = tex; continue; }
                    int q = n.IndexOf('\'');
                    if (q <= 0) continue;
                    string person = n.Substring(0, q).Trim();
                    if (n.EndsWith("_profile", StringComparison.Ordinal)) prof[person] = tex;
                    else full[person] = tex;
                }
            }
            profiles[team] = prof;
            photos[team] = full;
            templates[team] = template;
        }

        private static bool AllAlive(Dictionary<string, Texture2D> d)
        {
            foreach (var v in d.Values) if (v == null) return false;
            return true;
        }

        private static string Key(Cheerleader c) => (c?.Name ?? "").Trim();

        /// <summary>포스터 · 썸네일용 프로필 사진(없으면 상세 사진, 그것도 없으면 null).</summary>
        public static Texture2D Profile(Cheerleader c)
        {
            if (c == null) return null;
            Ensure(c.Team);
            return profiles[c.Team].TryGetValue(Key(c), out var t) ? t : photos[c.Team].TryGetValue(Key(c), out var f) ? f : null;
        }

        /// <summary>상세 사진(없으면 프로필).</summary>
        public static Texture2D Photo(Cheerleader c)
        {
            if (c == null) return null;
            Ensure(c.Team);
            return photos[c.Team].TryGetValue(Key(c), out var f) ? f : profiles[c.Team].TryGetValue(Key(c), out var t) ? t : null;
        }

        public static Texture2D LineupTemplate(Team team)
        {
            Ensure(team);
            return templates.TryGetValue(team, out var t) ? t : null;
        }

        public static void SetTexture(RawImage raw, Texture2D tex)
        {
            if (raw == null) return;
            raw.texture = tex;
            raw.color = tex != null ? Color.white : new Color(0f, 0f, 0f, 0f);
            var fitter = raw.GetComponent<AspectRatioFitter>();
            if (fitter != null && tex != null) fitter.aspectRatio = tex.width / (float)tex.height;
        }
    }

    /// <summary>
    /// [TASK-GM-16] 단상 라인업 포스터 배치(cheerleader_lineup.png 560×700 템플릿의 6칸 프레임 좌표).
    ///   4 · 5번 칸은 위 칸과 모서리(사선)가 겹쳐서 버튼 영역만 위쪽을 잘라 겹침 0을 지킨다(그림은 템플릿이 그린다).
    ///   템플릿이 없는 구단은 같은 좌표에 구단색 프레임을 직접 그린다.
    /// </summary>
    public static class GMCheerPoster
    {
        public const float TemplateWidth = 560f, TemplateHeight = 700f, PlateHeight = 26f;

        /// <summary>프레임(템플릿 px, x0 · y0 · x1 · y1) - 1.응원단장 ~ 6.위기 응원.</summary>
        public static readonly float[][] Frames =
        {
            new[] { 35f, 38f, 170f, 265f },
            new[] { 185f, 93f, 322f, 322f },
            new[] { 338f, 152f, 472f, 378f },
            new[] { 88f, 326f, 225f, 505f },
            new[] { 240f, 382f, 377f, 563f },
            new[] { 392f, 395f, 528f, 620f },
        };

        /// <summary>[TASK-GM-17] 원래 프레임 윗변(4 · 5번 칸은 버튼 영역만 잘랐다) - 사진은 원래 높이로 그려 6칸 사진 크기를 똑같이 맞춘다.</summary>
        public static readonly float[] PhotoTop = { 38f, 93f, 152f, 282f, 335f, 395f };

        /// <summary>그리기 순서 - 아래 칸(4 · 5 · 6)을 먼저, 위 칸(1 · 2 · 3)을 나중에 그려 사선 모서리 겹침은 위 칸 사진이 덮는다.</summary>
        public static readonly int[] DrawOrder = { 3, 4, 5, 0, 1, 2 };

        /// <summary>슬롯 버튼들을 그리기 순서대로 정렬한다.</summary>
        public static void ApplyDrawOrder(IList<Button> slots)
        {
            foreach (int i in DrawOrder) if (i < slots.Count && slots[i] != null) slots[i].transform.SetAsLastSibling();
        }

        /// <summary>포스터를 (x0, y0)에 높이 height로 놓을 때 i번 칸의 절대 좌표(1920×1080 기준).</summary>
        public static (float x0, float y0, float x1, float y1) SlotRect(int i, float x0, float y0, float height)
        {
            float s = height / TemplateHeight;
            var f = Frames[Mathf.Clamp(i, 0, Frames.Length - 1)];
            return (x0 + f[0] * s, y0 + f[1] * s, x0 + f[2] * s, y0 + f[3] * s);
        }

        public static float WidthFor(float height) => height * TemplateWidth / TemplateHeight;

        /// <summary>
        /// 슬롯 버튼 내부 장식 - 사진(포스터 프레임을 채우는 Envelope · 마스크) · 역할 태그 · CHEER 태그. 이름은 버튼 라벨(Text)을 하단 이름판으로 옮겨 쓴다.
        /// 만든 사진 RawImage를 돌려준다.
        /// </summary>
        public static RawImage Decorate(Button slot, Font font, int namePt, int tagPt, bool hasTemplate, Color team, int index = -1)
        {
            var rect = (RectTransform)slot.transform;
            if (!hasTemplate) CompyaUiKit.Paint(CompyaUiKit.Norm(rect, "FrameBg", 0f, 0f, 1f, 1f), new Color(team.r * 0.5f, team.g * 0.5f, team.b * 0.5f, 0.85f));
            // [TASK-GM-17] 잘린 칸(4 · 5)은 사진 영역을 원래 프레임 윗변까지 늘려 6칸 사진 크기 · 얼굴 높이를 맞춘다(cheerleader_lineup_ref 기준)
            float bottom = 0.12f, top = 0.985f;
            if (index >= 0 && index < Frames.Length)
            {
                var f = Frames[index];
                float trimmed = Math.Max(1f, f[3] - f[1]), original = f[3] - PhotoTop[index];
                bottom = 0.12f * original / trimmed;                                  // 이름판 위
                top = 1f + (f[1] - PhotoTop[index]) / trimmed - 0.015f * original / trimmed; // 원래 프레임 윗변 바로 아래
            }
            var holder = CompyaUiKit.Norm(rect, "PhotoMask", 0.03f, bottom, 0.97f, top);
            holder.gameObject.AddComponent<RectMask2D>();
            var photoRect = CompyaUiKit.Fill(holder, "Photo");
            var raw = photoRect.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var fitter = photoRect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            raw.color = new Color(0f, 0f, 0f, 0f);
            if (!hasTemplate) CompyaUiKit.Paint(CompyaUiKit.Norm(rect, "PlateBg", 0.3f, 0f, 1f, bottom), new Color(1f, 1f, 1f, 0.92f));

            var label = slot.transform.Find("Text") as RectTransform;
            if (label != null)
            {
                label.anchorMin = new Vector2(0.3f, 0f);
                label.anchorMax = new Vector2(1f, bottom); // 이름판
                label.offsetMin = label.offsetMax = Vector2.zero;
                label.SetAsLastSibling();
                var t = label.GetComponent<Text>();
                t.color = new Color(0.08f, 0.16f, 0.36f);
                TextTidy.Exact(t, namePt);
            }
            // 이름 라벨이 슬롯의 첫 Text가 되도록 태그는 라벨 뒤에 붙인다(GetComponentInChildren<Text> = 이름 · "빈 슬롯")
            Tag(rect, "RoleTag", font, tagPt, 0.04f, 0.86f, 0.96f, 0.99f, TextAnchor.MiddleLeft);
            Tag(rect, "CheerTag", font, tagPt, 0.04f, bottom + 0.01f, 0.96f, bottom + 0.12f, TextAnchor.MiddleLeft);
            return raw;
        }

        private static Text Tag(RectTransform parent, string name, Font font, int pt, float x0, float y0, float x1, float y1, TextAnchor anchor)
        {
            var r = CompyaUiKit.Norm(parent, name, x0, y0, x1, y1);
            var t = r.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontStyle = FontStyle.Normal;
            t.alignment = anchor;
            t.color = Color.white;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            TextTidy.Exact(t, pt);
            CompyaUiKit.Outline(t, new Color(0.02f, 0.08f, 0.25f, 0.9f), 1.5f);
            return t;
        }

        /// <summary>슬롯 내용 갱신 - 비어 있으면 "빈 슬롯".</summary>
        public static void Fill(Button slot, RawImage photo, Cheerleader c, int index, string roleName, string cheerLine)
        {
            CompyaUiKit.SetButtonText(slot, c != null ? c.DisplayName : "빈 슬롯");
            GMCheerPortraits.SetTexture(photo, c != null ? GMCheerPortraits.Profile(c) : null);
            var role = slot.transform.Find("RoleTag")?.GetComponent<Text>();
            if (role != null) role.text = $"{index + 1} {roleName}";
            var cheer = slot.transform.Find("CheerTag")?.GetComponent<Text>();
            if (cheer != null) cheer.text = c != null ? cheerLine : "";
        }
    }
}

namespace KBOManager.UI
{
    using System.Linq;
    using KBOManager.Services;

    /// <summary>[TASK-GM-16] 엔트리 시너지 · 효과 요약(포스터 우측 패널) - 실제 경기 효과(GMCheerleaderRoster · CheerSquad)를 그대로 풀어 쓴다(새 효과를 만들지 않는다).</summary>
    public static class GMCheerSynergy
    {
        /// <summary>4대 스탯 엔트리 합계(체력 효율 반영 전 원값) · 최대치(인원 × 100).</summary>
        public static (int total, int max) StatTotal(IReadOnlyList<Cheerleader> entry, int stat)
        {
            int total = 0;
            foreach (var c in entry) total += GMCheerleaderStats.Stat(c, (GMCheerStat)stat);
            return (total, entry.Count * 100);
        }

        public static List<string> Lines(GMTeamState team, IReadOnlyList<Cheerleader> entry)
        {
            var lines = new List<string>();
            if (team == null) return lines;
            int same = entry.Count(c => CheerleaderSynergy.IsActive(c, team.Team));
            lines.Add($"구단 소속 시너지 {same}/{entry.Count}명 발동" + (same < entry.Count ? " - 타 구단 출신은 경기 버프 없음" : " - 전원 발동"));
            lines.Add($"단장 리더십 → 팀워크 +{GMCheerleaderRoster.LeadershipTeamworkBonus(entry)}  ·  마운드 응원 → 실책 -{GMCheerleaderRoster.ErrorReduction(entry) * 100f:0}%");
            lines.Add($"타격 응원 → 7회 이후 득점권 클러치 +{GMCheerleaderRoster.ClutchBonus(entry) * 100f:0.0}%  ·  홈 흥행 +{GMCheerleaderRoster.HomeRevenue(entry):N0}만 원/경기");
            var fx = GMCheerleaderRoster.BuildMatchEffects(entry, team.Team, true, 0);
            if (fx != null)
            {
                var role = new List<string>();
                if (fx.BatterContactDiscipline > 0) role.Add($"타자 정확 · 선구 +{fx.BatterContactDiscipline}");
                if (fx.PitcherControlStuff > 0) role.Add($"투수 제구 · 구위 +{fx.PitcherControlStuff}");
                if (fx.HomeAllStatsBonus > 0) role.Add($"홈경기 전 스탯 +{fx.HomeAllStatsBonus}");
                if (fx.OpponentControlPenalty > 0) role.Add($"상대 제구 -{fx.OpponentControlPenalty}");
                if (fx.CloseLateMultiplier > 1f) role.Add($"후반 접전 x{fx.CloseLateMultiplier:0.00}");
                if (role.Count > 0) lines.Add("역할 효과: " + string.Join(" · ", role));
            }
            else lines.Add($"엔트리 {GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN}명 미만 - 경기 효과 없음");
            int tired = entry.Count(GMCheerleaderStats.IsTired);
            if (tired > 0) lines.Add($"체력 30 미만 {tired}명 - 해당 역할 효과 절반");
            lines.Add(GMCheerleaderRoster.DedicationLabel(team));
            return lines;
        }
    }
}
