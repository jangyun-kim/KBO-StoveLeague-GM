using System.Collections.Generic;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-179] 컴프야V26 1:1 경기 화면(CompyaMatchView) 전용 런타임 UI 빌더.
    ///
    /// 좌표계: 레퍼런스 세로 캡처(1248x1972 px, 좌상단 원점)의 픽셀 좌표를 그대로 받아 부모 RectTransform의 정규화 앵커로
    /// 바꾼다 - 레퍼런스에서 잰 박스 좌표를 그대로 옮겨 적으면 9:16 화면에서도 같은 비율 위치에 놓인다(레이아웃 1:1 복제).
    /// 글자 크기도 레퍼런스 px 기준으로 받아 1080x1920 캔버스 비율(약 0.9배)로 환산한다.
    /// </summary>
    public class CompyaUiKit
    {
        public const float RefWidth = 1248f;
        public const float RefHeight = 1972f;
        private const float FontScale = 0.9f;

        // [TASK-GM-06] 1920×1080 Landscape 좌표계 - 단장 모드 화면은 Wide() 스코프 안에서 1920×1080 px(좌상단 원점) 좌표를 그대로 넘긴다.
        public const float WideWidth = 1920f;
        public const float WideHeight = 1080f;
        private static float activeWidth = RefWidth, activeHeight = RefHeight;

        /// <summary>현재 배치 기준 폭/높이(기본 1248×1972 세로 레퍼런스, Wide() 스코프 안에서는 1920×1080).</summary>
        public static float ActiveWidth => activeWidth;
        public static float ActiveHeight => activeHeight;

        /// <summary>[TASK-GM-06] using 블록 동안 Place/SetBox가 1920×1080 가로 좌표를 쓴다(중첩 가능 - 끝나면 이전 기준으로 복원).</summary>
        public static System.IDisposable Wide() => new RefScope(WideWidth, WideHeight);

        private sealed class RefScope : System.IDisposable
        {
            private readonly float prevW, prevH;
            private bool disposed;

            public RefScope(float w, float h)
            {
                prevW = activeWidth; prevH = activeHeight;
                activeWidth = w; activeHeight = h;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                activeWidth = prevW; activeHeight = prevH;
            }
        }

        public Font Bold;
        public Font Regular;

        private readonly Dictionary<long, Texture2D> gradientCache = new Dictionary<long, Texture2D>();

        public CompyaUiKit(Font bold, Font regular)
        {
            var fallback = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Bold = bold != null ? bold : (regular != null ? regular : fallback);
            Regular = regular != null ? regular : Bold;
        }

        // ------------------------------------------------------------------ 배치

        /// <summary>레퍼런스 px 박스(x0,y0)-(x1,y1)(좌상단 원점)를 parent 안에 앵커로 배치한다.</summary>
        public static RectTransform Place(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            SetBox(rect, x0, y0, x1, y1);
            return rect;
        }

        public static void SetBox(RectTransform rect, float x0, float y0, float x1, float y1)
        {
            rect.anchorMin = new Vector2(x0 / activeWidth, 1f - y1 / activeHeight);
            rect.anchorMax = new Vector2(x1 / activeWidth, 1f - y0 / activeHeight);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>부모 전체를 덮는 자식.</summary>
        public static RectTransform Fill(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        /// <summary>부모 기준 정규화 좌표(좌하단 원점) 배치 - 작은 위젯 내부 배치용.</summary>
        public static RectTransform Norm(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(x0, y0);
            rect.anchorMax = new Vector2(x1, y1);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        // ------------------------------------------------------------------ 그래픽

        public static Image Paint(RectTransform rect, Color color, bool raycast = false)
        {
            if (!rect.TryGetComponent<Image>(out var image)) image = rect.gameObject.AddComponent<Image>(); // ?? 금지(Unity 가짜 null)
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static Image Box(Transform parent, string name, float x0, float y0, float x1, float y1, Color color)
            => Paint(Place(parent, name, x0, y0, x1, y1), color);

        public RawImage Gradient(RectTransform rect, Color a, Color b, bool horizontal)
        {
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = GradientTexture(a, b, horizontal);
            raw.raycastTarget = false;
            return raw;
        }

        public RawImage GradientBox(Transform parent, string name, float x0, float y0, float x1, float y1, Color a, Color b, bool horizontal)
            => Gradient(Place(parent, name, x0, y0, x1, y1), a, b, horizontal);

        /// <summary>a -> b 2색 그라데이션 텍스처(세로면 a=위, 가로면 a=왼쪽). 같은 조합은 캐시한다.</summary>
        public Texture2D GradientTexture(Color a, Color b, bool horizontal)
        {
            long key = ((long)ColorUtility.ToHtmlStringRGBA(a).GetHashCode() << 32) ^ (uint)ColorUtility.ToHtmlStringRGBA(b).GetHashCode() ^ (horizontal ? 1L : 0L);
            if (gradientCache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int steps = 64;
            var tex = new Texture2D(horizontal ? steps : 1, horizontal ? 1 : steps, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            for (int i = 0; i < steps; i++)
            {
                float t = i / (steps - 1f);
                var c = horizontal ? Color.Lerp(a, b, t) : Color.Lerp(b, a, t); // 텍스처 y=0이 아래
                if (horizontal) tex.SetPixel(i, 0, c); else tex.SetPixel(0, i, c);
            }
            tex.Apply();
            gradientCache[key] = tex;
            return tex;
        }

        public static RawImage Picture(Transform parent, string name, float x0, float y0, float x1, float y1, string resource, Color? tint = null)
        {
            var rect = Place(parent, name, x0, y0, x1, y1);
            return PictureOn(rect, resource, tint);
        }

        public static RawImage PictureOn(RectTransform rect, string resource, Color? tint = null)
        {
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.texture = Resources.Load<Texture2D>(resource);
            raw.color = tint ?? Color.white;
            raw.raycastTarget = false;
            if (raw.texture == null) raw.color = new Color(0f, 0f, 0f, 0f);
            return raw;
        }

        /// <summary>구단 로고(Resources/Broadcast179/logo_{Team}) - 비율을 유지해 박스 안에 맞춘다.
        /// [TASK-KBO-182] AspectRatioFitter(FitInParent)는 "자기 부모" 크기에 맞춘다 - 예전에는 로고 자신을 화면 전체 크기의 페이지에
        /// 바로 붙여 박스 좌표가 무시되고 로고가 화면 전체로 늘어났다(경기 결과 화면/로비의 거대 로고). 이제 박스 크기의 홀더를 두고
        /// 그 안의 자식에 피터를 붙여 박스 안에서만 비율을 맞춘다.</summary>
        public static RawImage Logo(Transform parent, string name, float x0, float y0, float x1, float y1)
        {
            var holder = Place(parent, name, x0, y0, x1, y1);
            var rect = Fill(holder, "Logo");
            var raw = rect.gameObject.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var fitter = rect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            return raw;
        }

        public static void SetLogo(RawImage raw, Team team, float alpha = 1f)
        {
            if (raw == null) return;
            var tex = Resources.Load<Texture2D>($"Broadcast179/logo_{team}");
            raw.texture = tex;
            raw.color = tex != null ? new Color(1f, 1f, 1f, alpha) : new Color(0f, 0f, 0f, 0f);
            var fitter = raw.GetComponent<AspectRatioFitter>();
            if (fitter != null && tex != null) fitter.aspectRatio = tex.width / (float)tex.height;
        }

        public static PolygonGraphic Polygon(Transform parent, string name, float x0, float y0, float x1, float y1, Color color, params Vector2[] points)
        {
            var rect = Place(parent, name, x0, y0, x1, y1);
            var poly = rect.gameObject.AddComponent<PolygonGraphic>();
            poly.color = color;
            poly.raycastTarget = false;
            poly.SetPoints(points);
            return poly;
        }

        // ------------------------------------------------------------------ 텍스트

        public Text Label(Transform parent, string name, string text, float x0, float y0, float x1, float y1,
            float refFontSize, TextAnchor anchor, Color color, bool bold = false, bool italic = false)
        {
            var rect = Place(parent, name, x0, y0, x1, y1);
            return LabelOn(rect, text, refFontSize, anchor, color, bold, italic);
        }

        public Text LabelOn(RectTransform rect, string text, float refFontSize, TextAnchor anchor, Color color, bool bold = false, bool italic = false)
        {
            var label = rect.gameObject.AddComponent<Text>();
            label.font = bold ? Bold : Regular;
            label.text = text;
            int size = Mathf.Max(8, Mathf.RoundToInt(refFontSize * FontScale));
            label.fontSize = size;
            label.fontStyle = italic ? FontStyle.Italic : FontStyle.Normal;
            label.alignment = anchor;
            label.color = color;
            label.raycastTarget = false;
            label.supportRichText = true;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(8, size / 2);
            label.resizeTextMaxSize = size;
            // [TASK-KBO-191] Bold 해제(서체 굵기만 사용) · 역할별 적정 크기(Tier) · Best Fit 최소 11 · 자간 2.0. 정확한 크기가 필요한 곳은 TextTidy.Exact로 덮는다.
            TextTidy.Normalize(label, false, Regular);
            return label;
        }

        public static void Outline(Graphic graphic, Color color, float distance = 1.5f)
        {
            var outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
        }

        public static void Shadow(Graphic graphic, Color color, float distance = 3f)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = new Vector2(distance, -distance);
        }

        /// <summary>단색 버튼(배경 Image + 라벨). 배경이 클릭 영역이다.</summary>
        public Button Button(Transform parent, string name, string text, float x0, float y0, float x1, float y1,
            Color background, Color textColor, float refFontSize, bool bold = true, bool italic = false)
        {
            var rect = Place(parent, name, x0, y0, x1, y1);
            var image = Paint(rect, background, true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.85f);
            button.colors = colors;
            var label = LabelOn(Fill(rect, "Text"), text, refFontSize, TextAnchor.MiddleCenter, textColor, bold, italic);
            label.raycastTarget = false;
            return button;
        }

        /// <summary>위->아래 그라데이션 버튼(START/PLAY BALL 보라, 확인 파랑 등 레퍼런스 버튼의 위쪽 하이라이트 재현).</summary>
        public Button GradientButton(Transform parent, string name, string text, float x0, float y0, float x1, float y1,
            Color top, Color bottom, Color textColor, float refFontSize, bool italic = false)
        {
            var rect = Place(parent, name, x0, y0, x1, y1);
            var hit = Paint(rect, new Color(1f, 1f, 1f, 0.001f), true);
            Gradient(Fill(rect, "Fill"), top, bottom, false);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            var label = LabelOn(Fill(rect, "Text"), text, refFontSize, TextAnchor.MiddleCenter, textColor, true, italic);
            Shadow(label, new Color(0f, 0f, 0f, 0.35f), 2f);
            return button;
        }

        public static void SetButtonText(Button button, string text)
        {
            if (button == null) return;
            var label = button.transform.Find("Text")?.GetComponent<Text>();
            if (label != null) label.text = text;
        }

        // ------------------------------------------------------------------ 구단 데이터(레퍼런스 표기 그대로)

        public static string ShortName(Team team)
        {
            switch (team)
            {
                case Team.Samsung: return "삼성";
                case Team.KIA: return "KIA";
                case Team.LG: return "LG";
                case Team.Doosan: return "두산";
                case Team.KT: return "KT";
                case Team.SSG: return "SSG";
                case Team.Lotte: return "롯데";
                case Team.Hanwha: return "한화";
                case Team.NC: return "NC";
                case Team.Kiwoom: return "키움";
                default: return "-";
            }
        }

        public static string FullName(Team team)
        {
            switch (team)
            {
                case Team.Samsung: return "삼성 라이온즈";
                case Team.KIA: return "KIA 타이거즈";
                case Team.LG: return "LG 트윈스";
                case Team.Doosan: return "두산 베어스";
                case Team.KT: return "kt wiz";
                case Team.SSG: return "SSG 랜더스";
                case Team.Lotte: return "롯데 자이언츠";
                case Team.Hanwha: return "한화 이글스";
                case Team.NC: return "NC 다이노스";
                case Team.Kiwoom: return "키움 히어로즈";
                default: return "-";
            }
        }

        /// <summary>홈 구장명(결과 화면 2 "고척스카이돔" 표기 위치).</summary>
        public static string Stadium(Team home)
        {
            switch (home)
            {
                case Team.Samsung: return "대구 삼성 라이온즈 파크";
                case Team.KIA: return "광주-기아 챔피언스 필드";
                case Team.LG:
                case Team.Doosan: return "잠실야구장";
                case Team.KT: return "수원 kt 위즈 파크";
                case Team.SSG: return "인천 SSG 랜더스필드";
                case Team.Lotte: return "사직 야구장";
                case Team.Hanwha: return "대전 한화생명 볼파크";
                case Team.NC: return "창원 NC 파크";
                case Team.Kiwoom: return "고척스카이돔";
                default: return "";
            }
        }

        /// <summary>구단 대표 컬러(스코어버그 팀 박스·사선 띠·승률 게이지). 삼성 블루/키움 버건디는 레퍼런스에서 추출.</summary>
        public static Color TeamColor(Team team)
        {
            switch (team)
            {
                case Team.Samsung: return new Color(0.13f, 0.36f, 0.82f);
                case Team.Kiwoom: return new Color(0.6f, 0.13f, 0.2f);
                case Team.KIA: return new Color(0.76f, 0.07f, 0.15f);
                case Team.LG: return new Color(0.73f, 0.06f, 0.25f);
                case Team.Doosan: return new Color(0.1f, 0.14f, 0.36f);
                case Team.KT: return new Color(0.16f, 0.16f, 0.17f);
                case Team.SSG: return new Color(0.82f, 0.11f, 0.15f);
                case Team.Lotte: return new Color(0.07f, 0.2f, 0.42f);
                case Team.Hanwha: return new Color(0.95f, 0.42f, 0.1f);
                case Team.NC: return new Color(0.12f, 0.25f, 0.48f);
                default: return new Color(0.3f, 0.32f, 0.38f);
            }
        }

        public static Color Darken(Color c, float factor) => new Color(c.r * factor, c.g * factor, c.b * factor, c.a);
    }
}
