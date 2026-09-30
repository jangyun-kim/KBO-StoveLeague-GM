using System.Collections.Generic;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// 강화(0~10강)와 각성(1~10각)을 처리하는 싱글톤 매니저.
    ///
    /// [TASK-KBO-138, 전면 개편] 강화(`TryEnhance`)를 "확률 판정 + Item 재료" 방식에서 "경험치(EXP)
    /// 누적 확정 + Player 카드 재료" 방식으로 교체했다. 등급별 확률표(`UpgradeProbabilityDB`)는 더 이상
    /// 참조하지 않는다(파일 자체는 삭제하지 않고 그대로 남겨 뒀다 - 향후 롤백/참고용). 요구/제공 경험치
    /// 테이블은 `UpgradeConstants.cs`에 분리했다. 각성(`TryAwaken`)은 원래부터 확정(포인트 누적) 방식이라
    /// 이번 개편과 무관하며 무수정이다.
    /// </summary>
    public class UpgradeManager : MonoBehaviour
    {
        public static UpgradeManager Instance { get; private set; }

        public const int MaxEnhanceMaterials = 5; // GDD: 최대 선택 가능 재료 수 5장

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// [TASK-KBO-138] "EXP 누적 확정 강화". 재료(최대 5장, 타겟 자신 제외 아무 보유 카드나 가능)의
        /// 등급별 제공 경험치(`UpgradeConstants.GetMaterialExp()`)를 target.ReinforceExp에 합산하고,
        /// 다음 단계 요구 경험치(`UpgradeConstants.GetRequiredExp()`)를 넘을 때마다 ReinforceLevel을
        /// 1씩 올리며 초과분만 다음 단계로 이월한다(재료를 한꺼번에 많이 넣으면 여러 단계를 한 번에 올릴
        /// 수도 있다). 확률 판정이 사라졌으므로 유효 재료가 하나라도 있으면 항상 true(확정 성공)를
        /// 반환한다 - "실패"는 더 이상 존재하지 않는다. 소모된 재료를 인벤토리에서 제거하는 것은
        /// 호출부(GameActionController) 책임이다.
        /// </summary>
        public bool TryEnhance(Player target, List<Player> materialCards)
        {
            if (target == null || target.Template == null) return false;
            if (target.ReinforceLevel >= Player.MaxReinforceLevel) return false;
            if (materialCards == null || materialCards.Count == 0 || materialCards.Count > MaxEnhanceMaterials) return false;

            int gainedExp = 0;
            foreach (var material in materialCards)
            {
                if (material?.Template == null) continue;
                if (material == target) continue; // 자기 자신은 재료가 될 수 없음

                gainedExp += UpgradeConstants.GetMaterialExp(material.Template.Grade, target.Template.Grade);
            }

            if (gainedExp <= 0) return false;

            target.ReinforceExp += gainedExp;

            while (target.ReinforceLevel < Player.MaxReinforceLevel)
            {
                int required = UpgradeConstants.GetRequiredExp(target);
                if (target.ReinforceExp < required) break;

                target.ReinforceExp -= required;
                target.ReinforceLevel++;
            }

            if (target.ReinforceLevel >= Player.MaxReinforceLevel)
            {
                target.ReinforceExp = 0; // 만렙에서는 더 쌓일 필요가 없다.
            }

            return true;
        }

        /// <summary>
        /// 재료 카드 목록으로 각성을 시도한다.
        /// - [TASK-KBO-172] 전 등급 각성 가능(LIVE 포함). 한계는 등급별 - 9각 한계 등급(ALLSTAR/FRANCHISE/
        ///   TITLE_HOLDER/RETIRED_NUMBER)은 9각, 초월 가능 등급(LIVE/GOLDEN_GLOVE/SIGNATURE/DYNASTY)은 초월(10).
        /// - 재료는 RealPlayerId가 target과 완전히 일치해야 유효한 재료로 인정된다.
        /// - 완전히 동일한 템플릿(카드 종류)이면 +3각, 같은 등급의 다른 템플릿(동일 선수)이면 +1각을 부여한다.
        /// 소모된 재료를 인벤토리에서 제거하는 것은 호출부 책임이다.
        /// </summary>
        public bool TryAwaken(Player target, List<Player> materialCards)
        {
            if (target == null || target.Template == null) return false;
            if (!target.CanAwaken) return false;
            int maxAwaken = target.MaxAwakenLevelForGrade; // [TASK-KBO-172] 등급별 9각 한계/초월
            if (target.AwakenLevel >= maxAwaken) return false;
            if (materialCards == null || materialCards.Count == 0) return false;

            int gainedPoints = 0;
            foreach (var material in materialCards)
            {
                if (material?.Template == null) continue;
                if (material == target) continue; // 자기 자신은 재료가 될 수 없음
                if (material.Template.RealPlayerId != target.Template.RealPlayerId) continue; // 다른 선수 -> 무효 재료

                if (material.Template == target.Template)
                {
                    gainedPoints += 3; // 완벽히 동일한 카드
                }
                else if (material.Template.Grade == target.Template.Grade)
                {
                    gainedPoints += 1; // 같은 등급의 다른 종류(카드) 동일 선수
                }
                // 등급이 다른 동일 선수 카드는 GDD에 명시되지 않아 각성 재료로 인정하지 않음
            }

            if (gainedPoints <= 0) return false;

            target.AwakenLevel = Mathf.Min(target.AwakenLevel + gainedPoints, maxAwaken);
            return true;
        }
    }
}
