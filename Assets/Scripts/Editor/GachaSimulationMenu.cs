using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-073/129] CheerleaderGachaService.RollLive()(TASK-KBO-129) 전용 등급 확률
    /// (LIVE_NORMAL 62.5% / LIVE_EPIC 37.5%)이 UnityEngine.Random 위에서 장기적으로 목표치에
    /// 수렴하는지 검증하는 순수 시뮬레이션 툴이다. [TASK-KBO-129] 구 4단계(NORMAL/RARE/EPIC/LEGEND)
    /// 확률표는 폐기되었다 - RollLive()만 확률 기반(나머지 3개 카테고리인 RollLimited()/RollIcon()/
    /// RollLegend()는 각각 단일 등급 100% 확정이라 시뮬레이션할 의미가 없다).
    ///
    /// 명령서 5항 지시대로 CheerleaderGachaService/GameManager를 전혀 호출하지 않는 완전히 독립된
    /// Mocking 방식이다 - 확률 판정 로직만 그대로 복사해 돌리므로, 유저의 실제 응원봉이나
    /// OwnedCheerleaders는 이 시뮬레이션으로 단 1도 변하지 않는다.
    ///
    /// [중요, 동기화 주의] 아래 임계값(LiveNormalThreshold)은 CheerleaderGachaService.
    /// LiveNormalRatePercent(private이라 여기서 직접 참조할 수 없음)와 반드시 동일해야 검증
    /// 의미가 있다 - 그쪽 값이 바뀌면 이 파일의 상수도 함께 갱신할 것.
    /// </summary>
    public static class GachaSimulationMenu
    {
        private const int SimulationCount = 10000;

        // CheerleaderGachaService.RollLiveGrade()와 동일한 누적 경계: [0,62.5)=LIVE_NORMAL,
        // [62.5,100]=LIVE_EPIC.
        private const float LiveNormalThreshold = 62.5f;

        [MenuItem("KBO Manager/Debug/Simulate 10,000x Gacha")]
        public static void Simulate10000xGacha()
        {
            int liveNormalCount = 0;
            int liveEpicCount = 0;

            for (int i = 0; i < SimulationCount; i++)
            {
                float roll = UnityEngine.Random.value * 100f; // Random.value는 [0,1] 양끝 포함이라 결과도 [0,100]

                if (roll < LiveNormalThreshold) liveNormalCount++;
                else liveEpicCount++;
            }

            LogResult(liveNormalCount, liveEpicCount);
        }

        private static void LogResult(int liveNormalCount, int liveEpicCount)
        {
            float liveNormalPercent = liveNormalCount * 100f / SimulationCount;
            float liveEpicPercent = liveEpicCount * 100f / SimulationCount;

            Debug.Log($"[GachaSimulationMenu] Total: {SimulationCount}, " +
                $"LIVE_NORMAL: {liveNormalCount} ({liveNormalPercent:F1}%), " +
                $"LIVE_EPIC: {liveEpicCount} ({liveEpicPercent:F1}%)");

            Debug.Log("[GachaSimulationMenu] 목표 확률: LIVE_NORMAL 62.5% / LIVE_EPIC 37.5% " +
                "(RollLive() 전용, TASK-KBO-129). 위 실측값과의 오차가 표본 10,000 기준 대략 " +
                "±1~2%p 이내면 정상으로 간주한다(docs/09_probability_policy.md 5절 참고).");
        }
    }
}
