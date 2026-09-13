using System;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-KBO-065] 치어리더 가챠 백엔드. docs/16_shop_and_gacha_policy.md가 제안한 정책 중
    /// 이번 프로토타입 단계에서 실제 구현하는 단순화 버전을 따른다: PremiumCurrency 1회당 100
    /// 소모(할인 없음, count * 100), 등급 확률 NORMAL 70% / RARE 22% / EPIC 7% / LEGEND 1%
    /// (합계 100%), 보장 슬롯(천장) 없음. 상점 UI/애니메이션은 이 서비스의 책임이 아니다 - 순수
    /// 데이터 처리(재화 차감 -> 등급 판정 -> 카탈로그 조회 -> 인벤토리 지급)만 담당한다.
    ///
    /// GameManager.AddCheerleader()(TASK-KBO-057/064)는 전혀 수정하지 않았다 - 이 서비스는 그
    /// 공개 API를 호출만 할 뿐이며, 중복 획득 시 재화로 전환하는 로직은 이미 그쪽에 구현되어 있다.
    /// </summary>
    public static class CheerleaderGachaService
    {
        private const int CostPerRoll = 100;

        // 등급 확률(%), docs/16_shop_and_gacha_policy.md 3-1절의 단일 뽑기 확률표. 누적 판정에 쓰인다.
        private const float NormalRatePercent = 70f;
        private const float RareRatePercent = 22f;
        private const float EpicRatePercent = 7f;
        // LEGEND는 나머지 전부(1%) - 누적 판정의 마지막 분기로 처리한다.

        /// <summary>
        /// count번 가챠를 실행한다. 재화(PremiumCurrency)가 count*100보다 부족하면 아무것도 차감하지
        /// 않고 false를 반환한다. 성공하면 재화를 먼저 전부 차감한 뒤, count번 반복해 등급을 판정하고
        /// GameManager.Instance.AddCheerleader()로 지급한다(신규 추가/중복 변환 여부는 그쪽 로직이
        /// 알아서 처리). 플레이 모드가 아니면(GameManager.Instance == null) false를 반환한다.
        /// </summary>
        public static bool RollGacha(int count)
        {
            if (count <= 0) return false;

            if (GameManager.Instance == null)
            {
                Debug.LogWarning("[CheerleaderGachaService] 플레이 모드에서만 실행 가능합니다(GameManager.Instance == null).");
                return false;
            }

            int totalCost = count * CostPerRoll;
            if (GameManager.Instance.PremiumCurrency < totalCost)
            {
                Debug.LogWarning($"[CheerleaderGachaService] 프리미엄 재화가 부족합니다. " +
                    $"(필요 {totalCost} / 보유 {GameManager.Instance.PremiumCurrency})");
                return false;
            }

            GameManager.Instance.PremiumCurrency -= totalCost;

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
                Debug.Log($"[CheerleaderGachaService] {i}/{count}번째 뽑기 결과: {issued.Grade} 등급 '{issued.Name}' " +
                    $"(CatalogId={issued.CatalogId}, InstanceId={issued.InstanceId})");
            }

            return true;
        }

        /// <summary>0~100 사이 난수로 등급을 판정한다(누적 분포, docs 16 3-1절 확률표와 동일).</summary>
        private static CheerleaderGrade RollGrade()
        {
            float roll = UnityEngine.Random.Range(0f, 100f);

            float cumulative = NormalRatePercent;
            if (roll < cumulative) return CheerleaderGrade.NORMAL;

            cumulative += RareRatePercent;
            if (roll < cumulative) return CheerleaderGrade.RARE;

            cumulative += EpicRatePercent;
            if (roll < cumulative) return CheerleaderGrade.EPIC;

            return CheerleaderGrade.LEGEND;
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

        /// <summary>등급을 한 단계 낮춘다. NORMAL보다 더 내려갈 곳이 없으면 null.</summary>
        private static CheerleaderGrade? ResolveFallbackGrade(CheerleaderGrade grade) => grade switch
        {
            CheerleaderGrade.LEGEND => CheerleaderGrade.EPIC,
            CheerleaderGrade.EPIC => CheerleaderGrade.RARE,
            CheerleaderGrade.RARE => CheerleaderGrade.NORMAL,
            _ => null,
        };
    }
}
