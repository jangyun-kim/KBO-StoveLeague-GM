using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-GM-10] 모바일(Android) 터치 대응 - UI 패널 간 RaycastTarget 정리.
    /// 터치를 받을 필요가 없는 그래픽(텍스트 · 로고 RawImage · 장식 배경/선/채움 이미지)이 위에 겹쳐 버튼 터치를 가로채지 않도록 raycastTarget을 끈다.
    /// 버튼 · 토글 · 슬라이더 · 입력창 · 스크롤의 대상 그래픽(targetGraphic)과 모달 차단막(이름에 Popup · Modal · Overlay가 들어간 오브젝트 자신)은 그대로 둔다.
    /// </summary>
    public static class GMRaycastSanitizer
    {
        private static readonly string[] DecorPrefixes = { "Bg", "Fill", "Line", "Connector", "HeadBg", "Border", "Icon", "Logo", "Divider", "Shadow", "Glow", "Frame", "Box" };
        private static readonly string[] BlockerTokens = { "Popup", "Modal", "Overlay", "Blocker", "Dim" };

        /// <summary>root 아래(비활성 포함) 그래픽을 정리하고 끈 개수를 돌려준다.</summary>
        public static int Sanitize(IEnumerable<Graphic> graphics)
        {
            var list = graphics.Where(g => g != null).ToList();
            var keep = new HashSet<Graphic>();
            foreach (var g in list)
            {
                foreach (var s in g.GetComponentsInParent<Selectable>(true)) if (s.targetGraphic != null) keep.Add(s.targetGraphic);
            }
            int changed = 0;
            foreach (var g in list)
            {
                if (!g.raycastTarget || keep.Contains(g)) continue;
                var go = g.gameObject;
                if (go.GetComponent<Selectable>() != null || go.GetComponent<ScrollRect>() != null || go.GetComponent<InputField>() != null) continue;
                bool decor = g is Text || g is RawImage || DecorPrefixes.Any(p => go.name.StartsWith(p));
                bool blocker = BlockerTokens.Any(t => go.name.Contains(t));
                if (!decor || blocker) continue;
                g.raycastTarget = false;
                changed++;
            }
            return changed;
        }

        public static int Sanitize(Transform root) => root == null ? 0 : Sanitize(root.GetComponentsInChildren<Graphic>(true));

        /// <summary>터치를 가로챌 수 있는 텍스트(raycastTarget = true이고 상호작용 대상이 아닌 Text) 수 - 검증용.</summary>
        public static int BlockingTexts(IEnumerable<Graphic> graphics)
        {
            var list = graphics.Where(g => g != null).ToList();
            var keep = new HashSet<Graphic>();
            foreach (var g in list) foreach (var s in g.GetComponentsInParent<Selectable>(true)) if (s.targetGraphic != null) keep.Add(s.targetGraphic);
            return list.Count(g => g is Text && g.raycastTarget && !keep.Contains(g) && g.GetComponent<InputField>() == null);
        }
    }
}
