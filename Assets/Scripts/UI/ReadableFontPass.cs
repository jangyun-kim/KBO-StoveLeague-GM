using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-186] 1080×1920 캔버스 가독성 하한. 화면 루트(로비 · 라인업 · 선수 관리 · 스카우트 · 경기 중계)에 붙어, 실제 표시 크기
    /// (자동 크기면 resizeTextMaxSize, 아니면 fontSize)가 20pt 미만인 텍스트를 본문 22pt / 버튼·탭·굵은 글씨(스탯·선수명) 26pt로 올린다.
    /// 넘침 방지: 자동 크기(Best Fit)를 켜고 최소 18pt(원래 크기가 더 작으면 원래 크기까지)로 줄어들 수 있게 하며, 가로는 줄바꿈 모드로 칸 안에 가둔다.
    /// PlayerCardUI 내부 글씨(카드 디자인 px 고정 + 칸 스케일 축소)는 건드리지 않는다. 코드로 늦게 생성되는 셀(라인업 칸 · 필터 칩)까지
    /// 잡도록 활성화 중 0.5초마다 다시 훑는다(이미 20pt 이상이면 즉시 건너뛰어 비용이 거의 없다).
    /// </summary>
    public class ReadableFontPass : MonoBehaviour
    {
        public const int Floor = 20;
        public const int BodySize = 22;
        public const int EmphasisSize = 26;
        public const int MinFitSize = 18;

        private static readonly List<Text> Buffer = new List<Text>();
        private Coroutine routine;

        private void OnEnable()
        {
            Apply(transform);
            routine = StartCoroutine(Rescan());
        }

        private void OnDisable()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
        }

        private IEnumerator Rescan()
        {
            var wait = new WaitForSecondsRealtime(0.5f);
            while (true)
            {
                yield return wait;
                Apply(transform);
            }
        }

        public static int EffectiveSize(Text text) => text.resizeTextForBestFit ? text.resizeTextMaxSize : text.fontSize;

        public static bool IsEmphasis(Text text) =>
            text.fontStyle == FontStyle.Bold || text.fontStyle == FontStyle.BoldAndItalic ||
            text.GetComponentInParent<Button>(true) != null || text.GetComponentInParent<Toggle>(true) != null;

        /// <summary>root 아래 작은 텍스트를 올린다. 바꾼 개수를 돌려준다.</summary>
        public static int Apply(Transform root)
        {
            if (root == null) return 0;
            int changed = 0;
            Buffer.Clear();
            root.GetComponentsInChildren(true, Buffer);
            foreach (var text in Buffer)
            {
                if (text == null || EffectiveSize(text) >= Floor) continue;
                if (text.GetComponentInParent<PlayerCardUI>(true) != null) continue;
                Raise(text);
                changed++;
            }
            Buffer.Clear();
            return changed;
        }

        public static void Raise(Text text)
        {
            int original = Mathf.Max(1, EffectiveSize(text));
            int target = IsEmphasis(text) ? EmphasisSize : BodySize;
            text.fontSize = target;
            text.resizeTextForBestFit = true;
            text.resizeTextMaxSize = target;
            text.resizeTextMinSize = Mathf.Min(MinFitSize, original);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
        }
    }
}
