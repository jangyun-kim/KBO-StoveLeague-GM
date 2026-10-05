using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-GM-03] 승리 확률(WPA) 폴리라인 - 정규화 좌표(x 0~1 = 경기 진행, y 0~1 = 홈 승리 확률)를 RectTransform 안에 두께 있는 선분으로 그린다.
    /// 텍스처 없이 UI 메시만 쓰고 클릭을 막지 않는다(raycastTarget = false).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class WpaLineGraphic : MaskableGraphic
    {
        [SerializeField] private float thickness = 4f;
        private readonly List<Vector2> points = new List<Vector2>();

        public IReadOnlyList<Vector2> Points => points;

        public void SetPoints(IEnumerable<Vector2> normalized, float lineThickness = 4f)
        {
            points.Clear();
            if (normalized != null) points.AddRange(normalized);
            thickness = lineThickness;
            raycastTarget = false;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points.Count < 2) return;
            var rect = GetPixelAdjustedRect();
            Vector2 ToLocal(Vector2 n) => new Vector2(rect.xMin + n.x * rect.width, rect.yMin + n.y * rect.height);
            float half = thickness * 0.5f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                var a = ToLocal(points[i]);
                var b = ToLocal(points[i + 1]);
                var dir = b - a;
                if (dir.sqrMagnitude < 0.0001f) continue;
                var normal = new Vector2(-dir.y, dir.x).normalized * half;
                int start = vh.currentVertCount;
                vh.AddVert(a - normal, color, Vector2.zero);
                vh.AddVert(a + normal, color, Vector2.zero);
                vh.AddVert(b + normal, color, Vector2.zero);
                vh.AddVert(b - normal, color, Vector2.zero);
                vh.AddTriangle(start, start + 1, start + 2);
                vh.AddTriangle(start, start + 2, start + 3);
            }
        }
    }
}
