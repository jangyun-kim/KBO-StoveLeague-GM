using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-186 → 191] 화면/캔버스 루트 텍스트 정리 패스.
    /// TASK-186에서는 20pt 미만을 본문 22 / 강조 26pt로 일괄 확대했으나, 한글 서체(KBO Dia Gothic)에 Bold까지 겹쳐 획이 뭉개지고 칸을 넘쳤다.
    /// TASK-191부터는 TextTidy.Normalize로 굵기 해제(Normal) · 역할별 적정 크기(Tier, 예전 확대분은 16/17 · 버튼 15로 복원) · Best Fit 최소 11pt ·
    /// 자간 2.0(em/100)을 적용한다. [TASK-KBO-192] 계층을 다시 올리고(본문 19~21 · 보조 16~18 · 탭 20~22 · 타이틀 26~30) Best Fit 최소 15,
    /// TASK-191 크기로 저장된 텍스트는 한 번 되올린다(TextTidy.Migrate191). PlayerCardUI 내부 글씨는 카드 디자인 px 고정이라 크기는 두고 굵기 · 자간만 정리한다.
    /// 코드로 늦게 생성되는 셀(라인업 칸 · 필터 칩 · 목록 행)까지 잡도록 활성화 중 0.5초마다 활성 텍스트를 다시 훑는다
    /// (TextTidy 표식의 appliedSize가 현재 크기와 같으면 즉시 건너뛰어 비용이 거의 없다). 상위에 같은 패스가 있으면 재스캔은 상위에 맡긴다.
    /// </summary>
    public class ReadableFontPass : MonoBehaviour
    {
        /// <summary>Best Fit 최소 크기(TASK-191: 18 → 11, TASK-192: 11 → 15).</summary>
        public const int Floor = TextTidy.AutoMin;

        [SerializeField] private Font bodyFont;

        private static readonly List<Text> Buffer = new List<Text>();
        private Coroutine routine;

        public void Configure(Font body) => bodyFont = body;

        private void OnEnable()
        {
            if (bodyFont != null && TextTidy.BodyFont == null) TextTidy.BodyFont = bodyFont;
            Apply(transform, true, bodyFont);
            bool covered = transform.parent != null && transform.parent.GetComponentInParent<ReadableFontPass>() != null;
            if (!covered) routine = StartCoroutine(Rescan());
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
                Apply(transform, false, bodyFont);
            }
        }

        public static int EffectiveSize(Text text) => TextTidy.EffectiveSize(text);

        /// <summary>root 아래 텍스트를 정리한다. 바뀐 개수를 돌려준다(두 번째 실행은 0 - 멱등).</summary>
        public static int Apply(Transform root, bool includeInactive = true, Font body = null)
        {
            if (root == null) return 0;
            int changed = 0;
            Buffer.Clear();
            root.GetComponentsInChildren(includeInactive, Buffer);
            foreach (var text in Buffer)
            {
                if (text == null) continue;
                if (text.TryGetComponent<TextTidy>(out var tidy) && tidy.AppliedSize == TextTidy.EffectiveSize(text) && tidy.Version >= TextTidy.CurrentVersion
                    && text.fontStyle != FontStyle.Bold && text.fontStyle != FontStyle.BoldAndItalic) continue;
                bool card = tidy == null && text.GetComponentInParent<PlayerCardUI>(true) != null;
                if (TextTidy.Normalize(text, card, body)) changed++;
            }
            Buffer.Clear();
            return changed;
        }
    }
}
