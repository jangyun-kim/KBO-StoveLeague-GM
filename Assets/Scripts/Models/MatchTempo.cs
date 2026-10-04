namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-186] 경기 중계 템포 - 타석 간 대기 · 포물선 타구 궤적 · 이닝 교대 화면 · 직접 플레이 연출 시간을 기존 대비 33% 단축한다.
    /// 씬에 직렬화된 기존 값(하이라이트 0.5초 / 전체 0.8초 / 이닝 1.4초 / 직접 결과 1.6초)을 그대로 두고 재생 시점에 Scale을 곱한다.
    /// </summary>
    public static class MatchTempo
    {
        public const float Scale = 0.67f;
        public const float LegacyArcSeconds = 0.6f;
        public const float LegacyPitchSeconds = 0.45f;

        public static float Scaled(float legacySeconds) => legacySeconds * Scale;
        public static float ArcSeconds => Scaled(LegacyArcSeconds);
        public static float PitchSeconds => Scaled(LegacyPitchSeconds);
    }
}
