using System;
using System.Collections.Generic;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-KBO-065/066/127/129] 치어리더 가챠 백엔드. GDD "뽑기(가챠) > 치어리더 영입" 절이 실제로
    /// 나열한 4개 카테고리 전용 메서드로 구성된다 - 일반 영입(라이브/한정), 프리미엄·픽업 영입(아이콘/
    /// 레전드). GDD "재화" 절의 매핑(라이브 응원봉=라이브 치어리더, 스타 응원봉=아이콘 치어리더,
    /// 레전드 응원봉=레전드 치어리더, 한정 응원봉=시즌 한정 치어리더)을 그대로 따른다.
    /// [결정 필요, TASK-KBO-129] GDD UI 흐름 절은 치어리더에도 "픽업 영입"/"프리미엄 영입"을 별도
    /// 하위 항목으로 나열하지만(둘 다 자식이 "아이콘 영입"/"레전드 영입"로 동일), 재화 절에는 이 둘을
    /// 구분할 전용 재화(선수의 "픽업 영입권" 같은)가 없다 - 그래서 두 탭을 억지로 만들지 않고
    /// RollIcon()/RollLegend() 하나씩으로 통합했다(기획서에 없는 화면을 임의로 만들지 말라는 명령서
    /// 5항 준수). 픽업/프리미엄을 실제로 구분해야 한다면 그 차이(예: 확정 카운터 유무)를 먼저
    /// 확정해야 한다.
    ///
    /// GameManager.AddCheerleader()(TASK-KBO-057/064)는 전혀 수정하지 않았다 - 이 서비스는 그
    /// 공개 API를 호출만 할 뿐이며, 중복 획득 시 재화로 전환하는 로직은 이미 그쪽에 구현되어 있다.
    /// </summary>
    public static class CheerleaderGachaService
    {
        private const int CostPerRoll = 100;

        // [TASK-KBO-129] "라이브 영입" 전용 2단계 확률(%) - TASK-KBO-127의 5단계 표(50/30/12/5/3)에서
        // LIVE_NORMAL/LIVE_EPIC 두 항목만 남겨 그 비율(50:30)대로 재정규화했다(62.5%/37.5%).
        private const float LiveNormalRatePercent = 62.5f;
        // LIVE_EPIC은 나머지 전부(37.5%) - 누적 판정의 마지막 분기로 처리한다.

        // [TASK-KBO-144] 픽업/프리미엄 영입(아이콘·레전드)이 "타겟 등급 100% 확정"으로 동작하던
        // 치명적 기획 오류를 해소하기 위한 가중치 확률표 - ScoutManager.cs의 SignatureDropTable/
        // TitleHolderDropTable과 동일하게 명령서 4항 예시 배분(타겟 최고 등급/바로 아래 등급/중간
        // 등급/기본 등급)을 채택했다. 등급 서열은 Cheerleader.cs의 CheerleaderGrade 정수값
        // (LIVE_NORMAL=2 &lt; LIVE_EPIC=3 &lt; ICON=4 &lt; LEGEND=5)을 그대로 따른다.
        private const float PremiumTargetRatePercent = 0.5f;   // 타겟 최고 등급
        private const float PremiumOneBelowRatePercent = 1.5f; // 바로 아래 등급
        private const float PremiumMidRatePercent = 3.0f;      // 중간 등급
        private const float PremiumBaseRatePercent = 95.0f;    // 기본(소비) 등급

        /// <summary>[픽업/프리미엄 영입 &gt; 아이콘] RollIcon() 전용 드랍 테이블. ICON 아래 등급이
        /// LIVE_EPIC/LIVE_NORMAL 2개뿐이라("바로 아래"+"중간"을 나눌 3번째 등급이 없음) 두 비율을
        /// LIVE_EPIC 한 칸에 합쳐(1.5%+3.0%=4.5%) 3단계로 구성한다 - ICON(타겟) 0.5% /
        /// LIVE_EPIC(한 단계 아래+중간 통합) 4.5% / LIVE_NORMAL(기본, 나머지 전부) 95.0% - 합계 100%.</summary>
        private static readonly List<(CheerleaderGrade Grade, float RatePercent)> IconDropTable =
            new List<(CheerleaderGrade, float)>
            {
                (CheerleaderGrade.ICON, PremiumTargetRatePercent),
                (CheerleaderGrade.LIVE_EPIC, PremiumOneBelowRatePercent + PremiumMidRatePercent),
                (CheerleaderGrade.LIVE_NORMAL, PremiumBaseRatePercent),
            };

        /// <summary>[픽업/프리미엄 영입 &gt; 레전드] RollLegend() 전용 드랍 테이블. LEGEND 아래 등급이
        /// ICON/LIVE_EPIC/LIVE_NORMAL 3개라 명령서 4항 예시를 그대로 4단계로 적용한다 - LEGEND(타겟)
        /// 0.5% / ICON(한 단계 아래) 1.5% / LIVE_EPIC(중간) 3.0% / LIVE_NORMAL(기본, 나머지 전부)
        /// 95.0% - 합계 100%.</summary>
        private static readonly List<(CheerleaderGrade Grade, float RatePercent)> LegendDropTable =
            new List<(CheerleaderGrade, float)>
            {
                (CheerleaderGrade.LEGEND, PremiumTargetRatePercent),
                (CheerleaderGrade.ICON, PremiumOneBelowRatePercent),
                (CheerleaderGrade.LIVE_EPIC, PremiumMidRatePercent),
                (CheerleaderGrade.LIVE_NORMAL, PremiumBaseRatePercent),
            };

        /// <summary>[일반 영입 &gt; 라이브] LiveCheerStick(라이브 응원봉)을 소모해 {LIVE_NORMAL, LIVE_EPIC}
        /// 풀에서만 추첨한다.</summary>
        public static List<Cheerleader> RollLive(int count) => RollWithCurrency(count,
            () => GameManager.Instance.LiveCheerStick,
            amount => GameManager.Instance.LiveCheerStick = amount,
            "라이브 응원봉",
            RollLiveGrade);

        /// <summary>[일반 영입 &gt; 한정(시즌 한정 기간)] LimitedCheerStick(한정 응원봉)을 소모해
        /// SEASON_LIMITED 등급을 확정 발급한다.</summary>
        public static List<Cheerleader> RollLimited(int count) => RollWithCurrency(count,
            () => GameManager.Instance.LimitedCheerStick,
            amount => GameManager.Instance.LimitedCheerStick = amount,
            "한정 응원봉",
            () => CheerleaderGrade.SEASON_LIMITED);

        /// <summary>[픽업/프리미엄 영입 &gt; 아이콘] StarCheerStick(스타 응원봉)을 소모한다. [TASK-KBO-144]
        /// 기존 "ICON 확정 발급"을 폐기하고 IconDropTable 가중치 확률로 교체했다 - 하위 등급도
        /// 확률적으로 등장한다(AC-01).</summary>
        public static List<Cheerleader> RollIcon(int count) => RollWithCurrency(count,
            () => GameManager.Instance.StarCheerStick,
            amount => GameManager.Instance.StarCheerStick = amount,
            "스타 응원봉",
            () => RollWeightedGrade(IconDropTable));

        /// <summary>[픽업/프리미엄 영입 &gt; 레전드] LegendCheerStick(레전드 응원봉)을 소모한다.
        /// [TASK-KBO-144] 기존 "LEGEND 확정 발급"을 폐기하고 LegendDropTable 가중치 확률로
        /// 교체했다.</summary>
        public static List<Cheerleader> RollLegend(int count) => RollWithCurrency(count,
            () => GameManager.Instance.LegendCheerStick,
            amount => GameManager.Instance.LegendCheerStick = amount,
            "레전드 응원봉",
            () => RollWeightedGrade(LegendDropTable));

        /// <summary>
        /// 카테고리 공통 소모/발급 파이프라인. count번 가챠를 실행하고, 실제로 발급된 Cheerleader
        /// 목록을 반환한다(UI가 결과를 바로 그릴 수 있도록 - CheerleaderShopUIController 참고). 재화가
        /// count*100보다 부족하거나 플레이 모드가 아니면(GameManager.Instance == null) 아무것도
        /// 차감하지 않고 빈 리스트를 반환한다. 성공하면 재화를 먼저 전부 차감한 뒤, count번 반복해
        /// gradeSelector로 등급을 정하고 GameManager.Instance.AddCheerleader()로 지급한다(신규 추가/
        /// 중복 변환 여부는 그쪽 로직이 알아서 처리) - 개별 회차가 카탈로그 폴백 실패로 발급되지 못하면
        /// 그 회차만 건너뛰고 나머지는 계속 진행하며, 반환 리스트에는 실제로 발급에 성공한 것만 담긴다.
        /// </summary>
        private static List<Cheerleader> RollWithCurrency(int count, Func<int> getCurrency,
            Action<int> setCurrency, string currencyLabel, Func<CheerleaderGrade> gradeSelector)
        {
            var results = new List<Cheerleader>();

            if (count <= 0) return results;

            if (GameManager.Instance == null)
            {
                Debug.LogWarning("[CheerleaderGachaService] 플레이 모드에서만 실행 가능합니다(GameManager.Instance == null).");
                return results;
            }

            int totalCost = count * CostPerRoll;
            int current = getCurrency();
            if (current < totalCost)
            {
                Debug.LogWarning($"[CheerleaderGachaService] {currencyLabel}이(가) 부족합니다. " +
                    $"(필요 {totalCost} / 보유 {current})");
                return results;
            }

            setCurrency(current - totalCost);

            for (int i = 1; i <= count; i++)
            {
                var grade = gradeSelector();
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

        /// <summary>0~100 사이 난수로 {LIVE_NORMAL, LIVE_EPIC} 중 하나를 판정한다(RollLive() 전용).</summary>
        private static CheerleaderGrade RollLiveGrade()
        {
            float roll = UnityEngine.Random.Range(0f, 100f);
            return roll < LiveNormalRatePercent ? CheerleaderGrade.LIVE_NORMAL : CheerleaderGrade.LIVE_EPIC;
        }

        /// <summary>[TASK-KBO-144] 명령서 6항 지시대로 Random.Range(0f, 100f) + 누적 확률(Cumulative
        /// Probability) 방식으로 dropTable(총합 100%)에서 등급 하나를 추첨한다. List(튜플)라 열거
        /// 순서가 고정되어 있어 매 실행마다 동일한 누적 구간으로 계산된다. 부동소수점 합산 오차로
        /// 극히 드물게 roll이 마지막 누적값을 넘는 경우에도 예외 없이 마지막 항목(명령서 7항 - 기본
        /// 등급, 항상 목록의 가장 낮은 확정 등급)으로 안전하게 폴백한다.</summary>
        private static CheerleaderGrade RollWeightedGrade(List<(CheerleaderGrade Grade, float RatePercent)> dropTable)
        {
            float roll = UnityEngine.Random.Range(0f, 100f);
            float cumulative = 0f;
            foreach (var entry in dropTable)
            {
                cumulative += entry.RatePercent;
                if (roll <= cumulative) return entry.Grade;
            }

            return dropTable[dropTable.Count - 1].Grade;
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
                catalogId: template.CatalogId,
                team: template.Team,                 // [TASK-KBO-175] 구단 시너지 판정용
                activePeriod: template.ActivePeriod);
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
