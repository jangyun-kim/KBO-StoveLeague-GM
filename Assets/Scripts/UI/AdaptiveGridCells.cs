using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-181] GridLayoutGroup 칸 크기를 컨테이너 크기 / (열 x 행)으로 맞춘다 - 9:16 어느 해상도에서도 라인업 3x3, 후보 6열,
    /// 선발 5열, 불펜 4x2가 겹치지 않고 영역을 꽉 채우게 한다(고정 cellSize 140x200은 1080 폭에서 카드가 작고 행이 넘쳤다).
    /// </summary>
    [RequireComponent(typeof(GridLayoutGroup))]
    public class AdaptiveGridCells : MonoBehaviour
    {
        [SerializeField, Min(1)] private int columns = 3;
        [SerializeField, Min(1)] private int rows = 3;

        public void Configure(int columnCount, int rowCount)
        {
            columns = Mathf.Max(1, columnCount);
            rows = Mathf.Max(1, rowCount);
            Apply();
        }

        private void OnEnable() => Apply();

        private void OnRectTransformDimensionsChange() => Apply();

        public void Apply()
        {
            var grid = GetComponent<GridLayoutGroup>();
            var size = ((RectTransform)transform).rect.size;
            if (grid == null || size.x <= 1f || size.y <= 1f) return;

            float width = (size.x - grid.padding.horizontal - grid.spacing.x * (columns - 1)) / columns;
            float height = (size.y - grid.padding.vertical - grid.spacing.y * (rows - 1)) / rows;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = columns;
            grid.cellSize = new Vector2(Mathf.Max(10f, width), Mathf.Max(10f, height));
        }
    }
}
