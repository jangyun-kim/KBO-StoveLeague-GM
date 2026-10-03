using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-179] 볼록 다각형 단색 채우기 UGUI 그래픽 - 컴프야V26 중계 화면의 사다리꼴 카운트 박스, 좌우 구단 컬러 사선 띠,
    /// 현재 타석 행의 왼쪽 화살표 하이라이트처럼 사각형이 아닌 도형에 쓴다. 꼭짓점은 RectTransform 기준 정규화 좌표(0~1, 좌하단 원점)다.
    /// 세로 그라데이션(topColor -> color)을 선택적으로 지원한다.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class PolygonGraphic : MaskableGraphic
    {
        [SerializeField] private List<Vector2> normalizedPoints = new List<Vector2>();
        [SerializeField] private bool useVerticalGradient;
        [SerializeField] private Color topColor = Color.white;

        public void SetPoints(params Vector2[] points)
        {
            normalizedPoints.Clear();
            normalizedPoints.AddRange(points);
            SetVerticesDirty();
        }

        public void SetVerticalGradient(Color top, Color bottom)
        {
            useVerticalGradient = true;
            topColor = top;
            color = bottom;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (normalizedPoints.Count < 3) return;

            var rect = rectTransform.rect;
            var vertex = UIVertex.simpleVert;
            foreach (var p in normalizedPoints)
            {
                vertex.position = new Vector2(rect.xMin + p.x * rect.width, rect.yMin + p.y * rect.height);
                vertex.color = useVerticalGradient ? Color.Lerp(color, topColor, p.y) : color;
                vh.AddVert(vertex);
            }
            for (int i = 1; i < normalizedPoints.Count - 1; i++) vh.AddTriangle(0, i, i + 1);
        }

        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            SetVerticesDirty();
        }
    }
}
