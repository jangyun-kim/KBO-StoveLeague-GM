using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-179] 컴프야V26 중계 화면의 "파란 입체 포물선 타구 궤적"을 그리는 UGUI 폴리라인. 점 목록(이 RectTransform의
    /// 로컬 좌표)을 받아 세그먼트마다 두께 있는 사각형을 만들고, Progress(0~1)만큼만 앞에서부터 그린다(궤적이 홈에서 낙구 지점까지
    /// 실시간으로 뻗어 나가는 연출). 점 사이 이음새는 반원 대신 짧은 겹침으로 처리해 꺾임이 벌어지지 않게 했다.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class ArcLineGraphic : MaskableGraphic
    {
        [SerializeField] private float thickness = 6f;
        [Range(0f, 1f)] [SerializeField] private float progress = 1f;

        private readonly List<Vector2> points = new List<Vector2>();

        public float Thickness
        {
            get => thickness;
            set { thickness = Mathf.Max(0.5f, value); SetVerticesDirty(); }
        }

        /// <summary>0이면 아무것도 그리지 않고, 1이면 전체 궤적을 그린다.</summary>
        public float Progress
        {
            get => progress;
            set { progress = Mathf.Clamp01(value); SetVerticesDirty(); }
        }

        public void SetPoints(IList<Vector2> newPoints)
        {
            points.Clear();
            if (newPoints != null) points.AddRange(newPoints);
            SetVerticesDirty();
        }

        public void Clear()
        {
            points.Clear();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (points.Count < 2 || progress <= 0f) return;

            // 누적 길이 기준으로 progress 지점까지만 그린다.
            float total = 0f;
            for (int i = 1; i < points.Count; i++) total += Vector2.Distance(points[i - 1], points[i]);
            float limit = total * progress;

            float walked = 0f;
            float half = thickness * 0.5f;
            for (int i = 1; i < points.Count && walked < limit; i++)
            {
                Vector2 a = points[i - 1];
                Vector2 b = points[i];
                float segment = Vector2.Distance(a, b);
                if (segment <= 0.0001f) continue;
                if (walked + segment > limit) b = Vector2.Lerp(a, b, (limit - walked) / segment);
                walked += segment;

                Vector2 dir = (b - a).normalized;
                Vector2 normal = new Vector2(-dir.y, dir.x) * half;
                Vector2 overlap = dir * half * 0.6f; // 꺾임 이음새 메우기
                AddQuad(vh, a - overlap - normal, a - overlap + normal, b + overlap + normal, b + overlap - normal);
            }
        }

        private void AddQuad(VertexHelper vh, Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3)
        {
            int start = vh.currentVertCount;
            var vertex = UIVertex.simpleVert;
            vertex.color = color;
            vertex.position = v0; vh.AddVert(vertex);
            vertex.position = v1; vh.AddVert(vertex);
            vertex.position = v2; vh.AddVert(vertex);
            vertex.position = v3; vh.AddVert(vertex);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
