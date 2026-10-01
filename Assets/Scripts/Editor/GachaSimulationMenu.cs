using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-073/129] 치어리더 영입 확률이 UnityEngine.Random 위에서 목표치에 수렴하는지 검증하는 순수 시뮬레이션 툴.
    /// CheerleaderGachaService/GameManager를 호출하지 않는 독립 Mocking 방식이라 실제 응원봉/보유 치어리더는 변하지 않는다.
    ///
    /// [TASK-KBO-178] 상수를 복사해 두던 방식(구 LIVE_NORMAL 62.5 / LIVE_EPIC 37.5)을 폐기하고, 서비스가 쓰는 확률표
    /// CheerleaderDropTables(라이브/아이콘/레전드)를 그대로 읽어 같은 누적 판정으로 돌린다 - 표가 바뀌어도 동기화가 필요 없다.
    /// LIVE_EPIC은 더 이상 어떤 표에도 없다(카탈로그 0장).
    /// </summary>
    public static class GachaSimulationMenu
    {
        private const int SimulationCount = 10000;

        [MenuItem("KBO Manager/Debug/Simulate 10,000x Gacha")]
        public static void Simulate10000xGacha()
        {
            Simulate("라이브 영입", CheerleaderDropTables.Live);
            Simulate("아이콘 영입", CheerleaderDropTables.Icon);
            Simulate("레전드 영입", CheerleaderDropTables.Legend);
        }

        private static void Simulate(string label, IReadOnlyList<(CheerleaderGrade Grade, float RatePercent)> table)
        {
            var counts = table.ToDictionary(e => e.Grade, _ => 0);
            for (int i = 0; i < SimulationCount; i++)
            {
                float roll = Random.value * 100f;
                float cumulative = 0f;
                var pick = table[table.Count - 1].Grade;
                foreach (var entry in table)
                {
                    cumulative += entry.RatePercent;
                    if (roll <= cumulative) { pick = entry.Grade; break; }
                }
                counts[pick]++;
            }

            string measured = string.Join(" / ", table.Select(e => $"{e.Grade.Display()} {counts[e.Grade] * 100f / SimulationCount:F2}%"));
            Debug.Log($"[GachaSimulationMenu] {label} {SimulationCount}회: {measured} (목표 {CheerleaderDropTables.Describe(table)})");
        }
    }
}
