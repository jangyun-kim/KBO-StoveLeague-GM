using System.Collections.Generic;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-181] 구단 로고(Resources/Broadcast179/logo_{Team}, 텍스처 임포트) → Sprite 변환 캐시. Image.preserveAspect = true로
    /// 붙여 칸 안에서 비율을 유지한다. TASK-180 로비는 RawImage + AspectRatioFitter(FitInParent)를 화면 전체 크기의 부모 아래에 두어
    /// 로고가 화면 가운데를 덮는 거대 오버레이로 늘어나는 버그가 있었다 - 이 헬퍼는 칸(RectTransform) 자체 크기만 쓴다.
    /// </summary>
    public static class TeamLogoSprites
    {
        private static readonly Dictionary<Team, Sprite> Cache = new Dictionary<Team, Sprite>();

        public static Sprite Get(Team team)
        {
            if (team == Team.None) return null;
            if (Cache.TryGetValue(team, out var cached) && cached != null) return cached;

            var texture = Resources.Load<Texture2D>($"Broadcast179/logo_{team}");
            if (texture == null) return null;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = $"logo_{team}";
            Cache[team] = sprite;
            return sprite;
        }

        /// <summary>로고가 있으면 표시, 없으면(Team.None/리소스 없음) 투명 처리.</summary>
        public static void Apply(Image image, Team team, float alpha = 1f)
        {
            if (image == null) return;
            var sprite = Get(team);
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = sprite != null ? new Color(1f, 1f, 1f, alpha) : new Color(1f, 1f, 1f, 0f);
        }
    }
}
