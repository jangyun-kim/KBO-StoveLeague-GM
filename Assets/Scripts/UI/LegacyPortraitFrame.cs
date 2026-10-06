using UnityEngine;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-GM-06] 1920×1080 Landscape 전환 후에도 기존 세로(1080×1920) 화면(로비 · 온보딩 · 치어리더 영입/보유 · 경기 중계 등)을
    /// 깨뜨리지 않도록, 그 화면 루트를 가로 캔버스 가운데의 9:16 프레임(1080×1920 크기 · 균등 축소)으로 놓는다.
    /// 화면 내부 배치(정규화 앵커)는 그대로라 겹침 · 비율이 유지된다. 단장 모드 화면(GM*)은 이 컴포넌트를 쓰지 않고 1920×1080 좌표로 직접 배치된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class LegacyPortraitFrame : MonoBehaviour
    {
        public const float FrameWidth = 1080f, FrameHeight = 1920f;
        /// <summary>1920×1080 레퍼런스 캔버스에서의 기본 축소 배율(1080 / 1920).</summary>
        public const float DefaultScale = 1080f / 1920f;

        private Vector2 lastParentSize = new Vector2(-1f, -1f);

        private void OnEnable() => Apply();

        private void LateUpdate()
        {
            var rect = transform as RectTransform;
            var parent = transform.parent as RectTransform;
            if (rect == null || parent == null) return;
            var size = parent.rect.size;
            if (size != lastParentSize || rect.sizeDelta != new Vector2(FrameWidth, FrameHeight) || rect.anchorMin != new Vector2(0.5f, 0.5f)) Apply();
        }

        /// <summary>현재 부모 크기에 맞춰 9:16 프레임을 다시 잡는다.</summary>
        public void Apply()
        {
            var rect = transform as RectTransform;
            if (rect == null) return;
            var parent = transform.parent as RectTransform;
            Vector2 size = parent != null ? parent.rect.size : Vector2.zero;
            lastParentSize = size;
            ApplyTo(rect, size);
        }

        /// <summary>rect를 부모(parentSize, 0이면 1920×1080 기준) 가운데 1080×1920 프레임으로 맞춘다.</summary>
        public static void ApplyTo(RectTransform rect, Vector2 parentSize)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(FrameWidth, FrameHeight);
            rect.anchoredPosition = Vector2.zero;
            float scale = parentSize.x > 1f && parentSize.y > 1f ? Mathf.Min(parentSize.x / FrameWidth, parentSize.y / FrameHeight) : DefaultScale;
            rect.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
