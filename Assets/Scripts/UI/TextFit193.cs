using System;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-193] 슬롯 · 카드 · 미리보기 텍스트가 박스 밖으로 팽창하지 않게 묶는 공용 헬퍼.
    ///   원인: Best Fit을 켠 채 resizeTextMaxSize가 기본값(40pt 안팎)이거나 verticalOverflow = Overflow라 글씨가 줄어들지 않고 이웃 칸을 덮었다.
    ///   처리: 크기를 목표 pt로 고정(TextTidy.Exact - 최대 = 목표) + Best Fit(작은 칸에서만 최소 pt까지 축소) + 세로 Truncate + 가로 Wrap.
    /// </summary>
    public static class TextFit193
    {
        /// <summary>스카우트 · 특별 영입 · 상점 · 응원단 슬롯/카드/미리보기 글씨 상한(pt).</summary>
        public const int SlotMax = 20;
        /// <summary>좁은 칸에서 Best Fit이 줄일 수 있는 최소(pt).</summary>
        public const int SlotMin = 13;

        public static Text Fit(Text t, int size, int min = SlotMin)
        {
            if (t == null) return null;
            TextTidy.Exact(t, size, Mathf.Min(size, min));
            t.fontStyle = FontStyle.Normal;
            t.resizeTextForBestFit = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        public static Text FitButton(Button button, int size, int min = SlotMin) =>
            button == null ? null : Fit(button.GetComponentInChildren<Text>(true), size, min);

        /// <summary>한 줄 고정(가로 넘침 허용 · 자동 크기 끔) - 폭이 충분한 짧은 수치 줄(중계 "시즌" 행 등).</summary>
        public static Text SingleLine(Text t, int size)
        {
            if (t == null) return null;
            TextTidy.Exact(t, size, size);
            t.fontStyle = FontStyle.Normal;
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        /// <summary>root 아래 모든 텍스트 중 max를 넘는 것을 max로 묶고, Best Fit 텍스트는 세로 Truncate로 바꾼다. skip이 true면 건너뛴다. 바꾼 수.</summary>
        public static int ClampAll(Transform root, int max, Func<Text, bool> skip = null)
        {
            if (root == null) return 0;
            int changed = 0;
            foreach (var t in root.GetComponentsInChildren<Text>(true))
            {
                if (skip != null && skip(t)) continue;
                bool over = TextTidy.EffectiveSize(t) > max || t.fontSize > max;
                bool overflow = t.resizeTextForBestFit && t.verticalOverflow == VerticalWrapMode.Overflow;
                if (!over && !overflow) continue;
                if (over) Fit(t, max);
                else t.verticalOverflow = VerticalWrapMode.Truncate;
                changed++;
            }
            return changed;
        }

        /// <summary>컨테이너에 RectMask2D를 붙여 자식 글씨가 박스 밖으로 그려지지 않게 한다.</summary>
        public static RectMask2D Mask(Component container)
        {
            if (container == null) return null;
            if (!container.TryGetComponent<RectMask2D>(out var mask)) mask = container.gameObject.AddComponent<RectMask2D>();
            return mask;
        }

        /// <summary>PlayerCardUI(카드 디자인 px 고정) 안쪽 글씨인지 - 일괄 상한에서 제외한다.</summary>
        public static bool InsideCard(Text t) => t != null && t.GetComponentInParent<PlayerCardUI>(true) != null;
    }
}
