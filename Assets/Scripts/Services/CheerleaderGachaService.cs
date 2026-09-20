using System;
using System.Collections.Generic;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-KBO-065/066/127] 치어리더 가챠 백엔드. docs/16_shop_and_gacha_policy.md 3절(TASK-KBO-066에서
    /// 이 코드와 1:1로 일치하도록 갱신됨)의 사양을 그대로 구현한다: CheerStick(응원봉) 1회당 100 소모
    /// (할인 없음, count * 100), 등급 확률(TASK-KBO-127, PM 확정 5단계) LIVE_NORMAL 50% / LIVE_EPIC 30% /
    /// ICON 12% / LEGEND 5% / SEASON_LIMITED 3%(합계 100%), 보장 슬롯(천장) 없음. 상점 UI/애니메이션은
    /// 이 서비스의 책임이 아니다 - 순수 데이터 처리(재화 차감 -> 등급 판정 -> 카탈로그 조회 -> 인벤토리
    /// 지급)만 담당하고, 발급된 목록을 반환해 CheerleaderShopUIController 등 호출부가 결과를 그릴 수
    /// 있게 한다.
    ///
    /// GameManager.AddCheerleader()(TASK-KBO-057/064)는 전혀 수정하지 않았다 - 이 서비스는 그
    /// 공개 API를 호출만 할 뿐이며, 중복 획득 시 재화로 전환하는 로직은 이미 그쪽에 구현되어 있다.
    /// </summary>
    public static class CheerleaderGachaService
    {
        private const int CostPerRoll = 100;

        // 등급 확률(%), docs/16_shop_and_gacha_policy.md 3절의 단일 뽑기 확률표(TASK-KBO-127 갱신).
        // 누적 판정에 쓰인다.
        private const float LiveNormalRatePercent = 50f;
        private const float LiveEpicRatePercent = 30f;
        private const float IconRatePercent = 12f;
        private const float LegendRatePercent = 5f;
        // SEASON_LIMITED는 나머지 전부(3%) - 누적 판정의 마지막 분기로 처리한다.

        /// <summary>
        /// [TASK-KBO-066] count번 가챠를 실행하고, 실제로 발급된 Cheerleader 목록을 반환한다(UI가
        /// 결과를 바로 그릴 수 있도록 - CheerleaderShopUIController 참고). 재화(CheerStick/응원봉)가
        /// count*100보다 부족하거나 플레이 모드가 아니면(GameManager.Instance == null) 아무것도
        /// 차감하지 않고 빈 리스트를 반환한다. 성공하면 재화를 먼저 전부 차감한 뒤, count번 반복해
        /// 등급을 판정하고 GameManager.Instance.AddCheerleader()로 지급한다(신규 추가/중복 변환
        /// 여부는 그쪽 로직이 알아서 처리) - 개별 회차가 카탈로그 폴백 실패로 발급되지 못하면(예외적
        /// 상황, 명령서 7항) 그 회차만 건너뛰고 나머지는 계속 진행하며, 반환 리스트에는 실제로 발급에
        /// 성공한 것만 담긴다.
        /// </summary>
        public static List<Cheerleader> RollGacha(int count)
        {
            var results = new List<Cheerleader>();

            if (count <= 0) return results;

            if (GameManager.Instance == null)
            {
                Debug.LogWarning("[CheerleaderGachaService] 플레이 모드에서만 실행 가능합니다(GameManager.Instance == null).");
                return results;
            }

            int totalCost = count * CostPerRoll;
            if (GameManager.Instance.CheerStick < totalCost)
            {
                Debug.LogWarning($"[CheerleaderGachaService] 응원봉이 부족합니다. " +
                    $"(필요 {totalCost} / 보유 {GameManager.Instance.CheerStick})");
                return results;
            }

            GameManager.Instance.CheerStick -= totalCost;

            for (int i = 1; i <= count; i++)
            {
                var grade = RollGrade();
                var issued = IssueCheerleader(grade);

                if (issued == null)
                {
                    Debug.LogError($"[CheerleaderGachaService] {i}/{count}번째 뽑기: 카탈로그가 비어 있어 발급에 실패했습니다.");
                    continue;
                }

                GameManager.Instance.AddCheerleader(issued);
                results.Add(issued);
                Debug.Log($"[CheerleaderGachaService] {i}/{count}번째 뽑기 결과: {issued.Grade} 등급 '{issued.Name}' " +
                    $"(CatalogId={issued.CatalogId}, InstanceId={issued.InstanceId})");
            }

            return results;
        }

        /// <summary>0~100 사이 난수로 등급을 판정한다(누적 분포, docs 16 3절 확률표와 동일).</summary>
        private static CheerleaderGrade RollGrade()
        {
            float roll = UnityEngine.Random.Range(0f, 100f);

            float cumulative = LiveNormalRatePercent;
            if (roll < cumulative) return CheerleaderGrade.LIVE_NORMAL;

            cumulative += LiveEpicRatePercent;
            if (roll < cumulative) return CheerleaderGrade.LIVE_EPIC;

            cumulative += IconRatePercent;
            if (roll < cumulative) return CheerleaderGrade.ICON;

            cumulative += LegendRatePercent;
            if (roll < cumulative) return CheerleaderGrade.LEGEND;

            return CheerleaderGrade.SEASON_LIMITED;
        }

        /// <summary>
        /// 판정된 등급의 카탈로그 템플릿 중 하나를 무작위로 골라, 새 InstanceId(Guid)를 부여한 새
        /// Cheerleader 인스턴스로 복사해 반환한다. 해당 등급 카탈로그가 비어 있으면(구현 누락 등)
        /// 하위 등급으로 순차 폴백하고, 모든 등급이 비어 있으면 null을 반환한다(크래시 방지 - 명령서
        /// 7항).
        /// </summary>
        private static Cheerleader IssueCheerleader(CheerleaderGrade grade)
        {
            var candidates = CheerleaderCatalog.GetCheerleadersByGrade(grade);

            if (candidates == null || candidates.Count == 0)
            {
                var fallbackGrade = ResolveFallbackGrade(grade);
                if (fallbackGrade == null)
                {
                    Debug.LogError($"[CheerleaderGachaService] {grade} 등급 및 하위 등급 카탈로그가 전부 비어 있어 발급할 수 없습니다.");
                    return null;
                }

                Debug.LogWarning($"[CheerleaderGachaService] {grade} 등급 카탈로그가 비어 있어 {fallbackGrade} 등급으로 폴백합니다.");
                return IssueCheerleader(fallbackGrade.Value);
            }

            var template = candidates[UnityEngine.Random.Range(0, candidates.Count)];

            return new Cheerleader(
                instanceId: Guid.NewGuid().ToString(),
                name: template.Name,
                grade: template.Grade,
                conditionBuff: template.ConditionBuff,
                clutchMultiplier: template.ClutchMultiplier,
                economicBonusRate: template.EconomicBonusRate,
                sentimentDefense: template.SentimentDefense,
                catalogId: template.CatalogId);
        }

        /// <summary>등급을 한 단계 낮춘다. LIVE_NORMAL보다 더 내려갈 곳이 없으면 null.</summary>
        private static CheerleaderGrade? ResolveFallbackGrade(CheerleaderGrade grade) => grade switch
        {
            CheerleaderGrade.SEASON_LIMITED => CheerleaderGrade.LEGEND,
            CheerleaderGrade.LEGEND => CheerleaderGrade.ICON,
            CheerleaderGrade.ICON => CheerleaderGrade.LIVE_EPIC,
            CheerleaderGrade.LIVE_EPIC => CheerleaderGrade.LIVE_NORMAL,
            _ => null,
        };
    }
}
