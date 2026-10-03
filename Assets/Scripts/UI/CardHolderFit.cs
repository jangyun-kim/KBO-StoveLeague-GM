using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-181] 카드 칸(홀더) 안의 PlayerCardUI를 원래 디자인 크기(템플릿 140x200 등) 그대로 두고 localScale로 칸에 맞춘다 -
    /// 카드 내부 텍스트/별/이름 띠가 고정 px 오프셋으로 배치돼 있어 sizeDelta를 늘리면 배치가 흐트러지기 때문. 홀더 크기가 레이아웃
    /// 확정 뒤에 바뀌면(OnRectTransformDimensionsChange) 다시 맞춘다. 카드를 풀에 돌려보내기 전에 반드시 ResetCard()로 스케일을 1로
    /// 되돌려야 다른 화면(GridLayoutGroup)에서 재사용될 때 크기가 새지 않는다.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class CardHolderFit : MonoBehaviour
    {
        public static readonly Vector2 DefaultCardSize = new Vector2(140f, 200f);

        [SerializeField] private Vector2 nativeSize = new Vector2(140f, 200f);
        [SerializeField, Range(0.5f, 1f)] private float fill = 0.98f;

        public void Configure(Vector2 cardNativeSize, float fillRatio = 0.98f)
        {
            nativeSize = cardNativeSize.x > 1f && cardNativeSize.y > 1f ? cardNativeSize : DefaultCardSize;
            fill = Mathf.Clamp(fillRatio, 0.5f, 1f);
            Refit();
        }

        /// <summary>카드 템플릿의 디자인 크기(RectTransform rect, 없으면 140x200).</summary>
        public static Vector2 NativeSizeOf(Component cardPrefab)
        {
            var rect = cardPrefab != null ? cardPrefab.transform as RectTransform : null;
            if (rect == null) return DefaultCardSize;
            var size = rect.rect.size;
            if (size.x < 1f || size.y < 1f) size = rect.sizeDelta;
            return size.x > 1f && size.y > 1f ? size : DefaultCardSize;
        }

        /// <summary>card를 이 홀더 가운데에 디자인 크기로 놓고 비율 유지 스케일로 맞춘다.</summary>
        public void Place(RectTransform card)
        {
            if (card == null) return;
            card.SetParent(transform, false);
            if (card.TryGetComponent<LayoutElement>(out var layout)) layout.ignoreLayout = true;
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = nativeSize;
            card.anchoredPosition = Vector2.zero;
            Refit();
        }

        public void Refit()
        {
            var size = ((RectTransform)transform).rect.size;
            if (size.x <= 1f || size.y <= 1f) return;
            float scale = Mathf.Min(size.x / nativeSize.x, size.y / nativeSize.y) * fill;
            foreach (Transform child in transform)
            {
                if (child.GetComponent<PlayerCardUI>() == null) continue;
                child.localScale = new Vector3(scale, scale, 1f);
            }
        }

        public static void ResetCard(Component card)
        {
            if (card == null) return;
            card.transform.localScale = Vector3.one;
            if (card.TryGetComponent<LayoutElement>(out var layout)) layout.ignoreLayout = false;
        }

        private void OnRectTransformDimensionsChange() => Refit();
    }
}
