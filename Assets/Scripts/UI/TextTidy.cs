using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-191] 전 화면 한글 텍스트 정리(Legacy UI Text 공용).
    ///   1) 굵기: KBO Dia Gothic은 서체 자체가 충분히 굵어 FontStyle.Bold(가짜 볼드)를 켜면 획이 뭉개진다 → 항상 Normal(BoldAndItalic → Italic).
    ///      18pt 이하 작은 글씨가 Bold 서체 파일이면 Medium으로 바꾸고, 2px을 넘는 Outline은 1.5px로 줄인다.
    ///   2) 크기: TASK-186의 일괄 확대(20pt 미만 → 22/26pt)를 되돌리고 역할별 적정 크기로 내린다(Tier) -
    ///      대형 점수 · 메인 OVR 32~42 / 화면 타이틀 24~26 / 큰 버튼 · 섹션 헤더 19~21 / 본문 · 능력치 16~18 / 보조 · 작은 버튼 13~15.
    ///      자동 크기(Best Fit) 최소는 11pt - 좁은 칸에서도 넘치지 않고 자연스럽게 줄어든다.
    ///   3) 자간: TMP characterSpacing과 같은 단위(em/100)의 글자 간격(기본 2.0 = 글자 크기의 2%)을 메시 효과로 준다(Text엔 자간 속성이 없다).
    /// 이 컴포넌트는 "정리 완료" 표식을 겸한다 - appliedSize와 현재 크기가 같으면 다시 줄이지 않아(멱등) 런타임 재스캔에도 안전하고,
    /// 코드가 나중에 크기를 바꾸면(appliedSize와 달라지면) 그 값을 기준으로 한 번 더 정리한다. 직접 지정한 크기는 Exact()로 고정한다.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Text))]
    public sealed class TextTidy : BaseMeshEffect
    {
        public const float DefaultSpacing = 2f;
        public const int AutoMin = 11;
        public const int MaxSize = 42;
        public const int SmallBodyMaxForBoldFont = 18;
        public const float MaxOutline = 1.5f;

        [Tooltip("글자 간격(em/100, TMP characterSpacing과 같은 단위)")]
        [SerializeField] private float characterSpacing = DefaultSpacing;
        [Tooltip("정리기가 마지막으로 적용한 크기(현재 크기와 같으면 다시 줄이지 않는다)")]
        [SerializeField] private int appliedSize;
        [Tooltip("크기는 건드리지 않고 굵기 · 자간만 정리(카드 디자인 px 고정 텍스트)")]
        [SerializeField] private bool sizeLocked;

        public float CharacterSpacing { get => characterSpacing; set { characterSpacing = value; if (graphic != null) graphic.SetVerticesDirty(); } }
        public int AppliedSize => appliedSize;
        public bool SizeLocked => sizeLocked;

        /// <summary>Body/Bold 서체 쌍(런타임 정리용 - ReadableFontPass / CompyaUiKit가 채운다).</summary>
        public static Font BodyFont;

        // ================================================================== 크기 계층

        private static readonly (int from, int to)[] LabelTiers =
        {
            (16, 16), (18, 16), (20, 16), (22, 17), (24, 17), (26, 18), (28, 19), (30, 19), (32, 20),
            (36, 22), (40, 24), (44, 26), (48, 28), (54, 32), (60, 36), (66, 40), (72, 42),
        };

        private static readonly (int from, int to)[] ButtonTiers =
        {
            (15, 15), (20, 15), (24, 16), (28, 17), (32, 19), (36, 20), (40, 21),
        };

        /// <summary>예전 크기 → 역할별 적정 크기. 버튼(Button/Toggle 안) 글씨는 19~21 이하, 그 외는 최대 42pt. 작은 글씨는 그대로 둔다.</summary>
        public static int Tier(int size, bool inButton)
        {
            var table = inButton ? ButtonTiers : LabelTiers;
            if (size <= table[0].from) return size;
            for (int i = 1; i < table.Length; i++)
            {
                if (size > table[i].from) continue;
                var (x0, y0) = table[i - 1];
                var (x1, y1) = table[i];
                return Mathf.RoundToInt(Mathf.Lerp(y0, y1, (size - x0) / (float)(x1 - x0)));
            }
            return table[table.Length - 1].to;
        }

        /// <summary>TASK-186 ReadableFontPass가 올려 둔 텍스트(Best Fit · 최대 = 크기 = 22 또는 26 · 최소 18 이하 · 줄바꿈).</summary>
        public static bool LooksRaised186(Text t) =>
            t.resizeTextForBestFit && t.fontSize == t.resizeTextMaxSize && (t.fontSize == 22 || t.fontSize == 26)
            && t.resizeTextMinSize <= 18 && t.horizontalOverflow == HorizontalWrapMode.Wrap;

        public static int EffectiveSize(Text t) => t.resizeTextForBestFit ? t.resizeTextMaxSize : t.fontSize;

        public static bool IsInButton(Component c) =>
            c.GetComponentInParent<Button>(true) != null || c.GetComponentInParent<Toggle>(true) != null;

        private static void SetSize(Text t, int size, int? min = null)
        {
            t.fontSize = size;
            t.resizeTextMaxSize = size;
            if (min.HasValue) t.resizeTextMinSize = Mathf.Min(size, min.Value);
            else if (t.resizeTextForBestFit) t.resizeTextMinSize = Mathf.Min(size, Mathf.Clamp(t.resizeTextMinSize, 1, AutoMin));
        }

        // ================================================================== 정리

        /// <summary>굵기 해제 + (크기 고정이 아니면) 계층 크기 + 자간 표식. 바뀐 게 있으면 true.</summary>
        public static bool Normalize(Text t, bool sizeLocked = false, Font bodyFont = null)
        {
            if (t == null) return false;
            bool changed = false;
            if (t.fontStyle == FontStyle.Bold) { t.fontStyle = FontStyle.Normal; changed = true; }
            else if (t.fontStyle == FontStyle.BoldAndItalic) { t.fontStyle = FontStyle.Italic; changed = true; }

            if (!t.TryGetComponent<TextTidy>(out var tidy))
            {
                tidy = t.gameObject.AddComponent<TextTidy>();
                tidy.sizeLocked = sizeLocked;
                tidy.appliedSize = -1;
                changed = true;
            }

            int current = EffectiveSize(t);
            if (tidy.appliedSize != current)
            {
                if (!tidy.sizeLocked)
                {
                    bool inButton = IsInButton(t);
                    int target = LooksRaised186(t)
                        ? (inButton ? 15 : t.fontSize == 26 ? 17 : 16)
                        : Tier(current, inButton);
                    if (target != current || t.fontSize != target) SetSize(t, target);
                    else if (t.resizeTextForBestFit && t.resizeTextMinSize > AutoMin) t.resizeTextMinSize = Mathf.Min(target, AutoMin);
                }
                tidy.appliedSize = EffectiveSize(t);
                changed = true;
            }

            changed |= LightenSmallBold(t, bodyFont);
            changed |= ClampOutline(t);
            return changed;
        }

        /// <summary>직접 지정한 크기로 고정(정리기가 다시 줄이지 않는다). min = Best Fit 최소(기본 11).</summary>
        public static Text Exact(Text t, int size, int min = AutoMin, Font bodyFont = null)
        {
            if (t == null) return null;
            if (t.fontStyle == FontStyle.Bold) t.fontStyle = FontStyle.Normal;
            else if (t.fontStyle == FontStyle.BoldAndItalic) t.fontStyle = FontStyle.Italic;
            SetSize(t, size, min);
            if (!t.TryGetComponent<TextTidy>(out var tidy)) tidy = t.gameObject.AddComponent<TextTidy>();
            tidy.appliedSize = EffectiveSize(t);
            LightenSmallBold(t, bodyFont);
            return t;
        }

        /// <summary>버튼 라벨(자식 "Text")을 Exact로 고정.</summary>
        public static Text ExactButton(Button button, int size, int min = AutoMin, Font bodyFont = null)
        {
            if (button == null) return null;
            var label = button.GetComponentInChildren<Text>(true);
            return Exact(label, size, min, bodyFont);
        }

        private static bool LightenSmallBold(Text t, Font bodyFont)
        {
            var body = bodyFont != null ? bodyFont : BodyFont;
            if (body == null || t.font == null || t.font == body) return false;
            if (EffectiveSize(t) > SmallBodyMaxForBoldFont || !t.font.name.Contains("Bold")) return false;
            t.font = body;
            return true;
        }

        private static readonly List<Outline> OutlineBuffer = new List<Outline>();

        private static bool ClampOutline(Text t)
        {
            bool changed = false;
            OutlineBuffer.Clear();
            t.GetComponents(OutlineBuffer);
            foreach (var o in OutlineBuffer)
            {
                var d = o.effectDistance;
                if (Mathf.Abs(d.x) <= MaxOutline && Mathf.Abs(d.y) <= MaxOutline) continue;
                o.effectDistance = new Vector2(Mathf.Clamp(d.x, -MaxOutline, MaxOutline), Mathf.Clamp(d.y, -MaxOutline, MaxOutline));
                changed = true;
            }
            OutlineBuffer.Clear();
            return changed;
        }

        // ================================================================== 자간(메시 효과)

        private static readonly List<int> LineOf = new List<int>();
        private static readonly List<int> IndexInLine = new List<int>();
        private static readonly List<int> LineCount = new List<int>();

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Approximately(characterSpacing, 0f)) return;
            var text = graphic as Text;
            if (text == null) return;
            int quads = vh.currentVertCount / 4;
            if (quads < 2) return;

            float size = text.fontSize;
            if (text.resizeTextForBestFit && text.cachedTextGenerator != null && text.cachedTextGenerator.fontSizeUsedForBestFit > 0)
                size = text.cachedTextGenerator.fontSizeUsedForBestFit / Mathf.Max(0.0001f, text.pixelsPerUnit);
            float step = characterSpacing / 100f * size;

            // 줄 나누기: 글자 왼쪽 x가 앞 글자보다 왼쪽으로 돌아가거나(줄바꿈 · Outline 복제 패스) 앞 글자 아래로 내려가면 새 줄.
            LineOf.Clear(); IndexInLine.Clear(); LineCount.Clear();
            var v = new UIVertex();
            float prevX = float.MinValue, prevBottom = float.MaxValue;
            int line = -1, index = 0;
            for (int q = 0; q < quads; q++)
            {
                vh.PopulateUIVertex(ref v, q * 4);
                float left = v.position.x, top = v.position.y;
                vh.PopulateUIVertex(ref v, q * 4 + 2);
                float bottom = v.position.y;
                float middle = (top + bottom) / 2f;
                if (line < 0 || left < prevX - 0.01f || middle < prevBottom)
                {
                    line++;
                    index = 0;
                    LineCount.Add(0);
                }
                LineOf.Add(line);
                IndexInLine.Add(index++);
                LineCount[line]++;
                prevX = left;
                prevBottom = bottom;
            }

            float align = 0f;
            switch (text.alignment)
            {
                case TextAnchor.UpperCenter: case TextAnchor.MiddleCenter: case TextAnchor.LowerCenter: align = 0.5f; break;
                case TextAnchor.UpperRight: case TextAnchor.MiddleRight: case TextAnchor.LowerRight: align = 1f; break;
            }

            for (int q = 0; q < quads; q++)
            {
                float offset = IndexInLine[q] * step - (LineCount[LineOf[q]] - 1) * step * align;
                if (Mathf.Approximately(offset, 0f)) continue;
                for (int k = 0; k < 4; k++)
                {
                    vh.PopulateUIVertex(ref v, q * 4 + k);
                    v.position.x += offset;
                    vh.SetUIVertex(v, q * 4 + k);
                }
            }
        }
    }
}
