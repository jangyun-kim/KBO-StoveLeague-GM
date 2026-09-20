using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-073] CheerleaderGachaService.RollGrade()(TASK-KBO-065)의 등급 확률(NORMAL 70% /
    /// RARE 22% / EPIC 7% / LEGEND 1%)이 UnityEngine.Random 위에서 장기적으로 목표치에 수렴하는지
    /// 검증하는 순수 시뮬레이션 툴이다.
    ///
    /// 명령서 5항 지시대로 CheerleaderGachaService/GameManager를 전혀 호출하지 않는 완전히 독립된
    /// Mocking 방식이다 - 확률 판정 로직만 그대로 복사해 돌리므로, 유저의 실제 CheerStick(응원봉)이나
    /// OwnedCheerleaders는 이 시뮬레이션으로 단 1도 변하지 않는다.
    ///
    /// [중요, 동기화 주의] 아래 임계값(NormalThreshold/RareThreshold/EpicThreshold)은
    /// CheerleaderGachaService.RollGrade()의 실제 누적 확률(NormalRatePercent=70f/
    /// RareRatePercent=22f/EpicRatePercent=7f, 둘 다 private이라 여기서 직접 참조할 수 없음)과
    /// 반드시 동일해야 검증 의미가 있다 - 그쪽 값이 바뀌면 이 파일의 상수도 함께 갱신할 것.
    /// </summary>
    public static class GachaSimulationMenu
    {
        private const int SimulationCount = 10000;

        // CheerleaderGachaService.RollGrade()와 동일한 누적 경계: [0,70)=NORMAL, [70,92)=RARE,
        // [92,99)=EPIC, [99,100]=LEGEND.
        private const float NormalThreshold = 70f;
        private const float RareThreshold = 92f;
        private const float EpicThreshold = 99f;

        [MenuItem("KBO Manager/Debug/Simulate 10,000x Gacha")]
        public static void Simulate10000xGacha()
        {
            int normalCount = 0;
            int rareCount = 0;
            int epicCount = 0;
            int legendCount = 0;

            for (int i = 0; i < SimulationCount; i++)
            {
                float roll = UnityEngine.Random.value * 100f; // Random.value는 [0,1] 양끝 포함이라 결과도 [0,100]

                // 경계값(0.0f/100.0f)이 나와도 아래 else-if 체인은 서로 겹치지 않고 마지막 else가
                // 항상 받아주므로(명령서 9항) 누락/중복 카운트나 인덱스 오류가 생기지 않는다.
                if (roll < NormalThreshold) normalCount++;
                else if (roll < RareThreshold) rareCount++;
                else if (roll < EpicThreshold) epicCount++;
                else legendCount++;
            }

            LogResult(normalCount, rareCount, epicCount, legendCount);
        }

        private static void LogResult(int normalCount, int rareCount, int epicCount, int legendCount)
        {
            float normalPercent = normalCount * 100f / SimulationCount;
            float rarePercent = rareCount * 100f / SimulationCount;
            float epicPercent = epicCount * 100f / SimulationCount;
            float legendPercent = legendCount * 100f / SimulationCount;

            Debug.Log($"[GachaSimulationMenu] Total: {SimulationCount}, " +
                $"NORMAL: {normalCount} ({normalPercent:F1}%), " +
                $"RARE: {rareCount} ({rarePercent:F1}%), " +
                $"EPIC: {epicCount} ({epicPercent:F1}%), " +
                $"LEGEND: {legendCount} ({legendPercent:F1}%)");

            Debug.Log("[GachaSimulationMenu] 목표 확률: NORMAL 70% / RARE 22% / EPIC 7% / LEGEND 1% " +
                "(docs/16_shop_and_gacha_policy.md 3절). 위 실측값과의 오차가 표본 10,000 기준 대략 " +
                "±1~2%p 이내면 정상으로 간주한다(docs/09_probability_policy.md 5절 참고).");
        }
    }
}
