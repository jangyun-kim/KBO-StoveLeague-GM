using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// 강화(0~10강)와 각성(1~10각)을 처리하는 싱글톤 매니저.
    /// 확률 판정은 UpgradeProbabilityDB(데이터 테이블)에 위임하고, 이 클래스는 판정/적용 로직만 담당한다.
    /// </summary>
    public class UpgradeManager : MonoBehaviour
    {
        public static UpgradeManager Instance { get; private set; }

        public const int MaxEnhanceMaterials = 5; // GDD: 최대 선택 가능 재료 수 5장

        [SerializeField] private UpgradeProbabilityDB probabilityDB;

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
        /// 재료(최대 5장)의 성공 확률을 합산해 강화 성공 여부를 판정한다.
        /// 성공 시 target의 ReinforceLevel을 1 올린다. 소모된 재료를 인벤토리에서 제거하는 것은 호출부 책임이다.
        /// </summary>
        public bool TryEnhance(Player target, List<Item> enhanceCards)
        {
            if (probabilityDB == null || target == null || target.Template == null) return false;
            if (target.ReinforceLevel >= Player.MaxReinforceLevel) return false;
            if (enhanceCards == null || enhanceCards.Count == 0 || enhanceCards.Count > MaxEnhanceMaterials) return false;

            int fromLevel = target.ReinforceLevel;
            float totalRate = enhanceCards
                .Where(item => item?.Template != null)
                .Sum(item => probabilityDB.GetSuccessRate(fromLevel, item.Template.MaterialType));
            totalRate = Mathf.Clamp(totalRate, 0f, 100f);

            bool success = Random.Range(0f, 100f) < totalRate;
            if (success)
            {
                target.ReinforceLevel = Mathf.Min(target.ReinforceLevel + 1, Player.MaxReinforceLevel);
            }

            return success;
        }

        /// <summary>
        /// 재료 카드 목록으로 각성을 시도한다.
        /// - target이 ALLSTAR 이상 등급(Player.CanAwaken)이어야 한다.
        /// - 재료는 RealPlayerId가 target과 완전히 일치해야 유효한 재료로 인정된다.
        /// - 완전히 동일한 템플릿(카드 종류)이면 +3각, 같은 등급의 다른 템플릿(동일 선수)이면 +1각을 부여한다.
        /// 소모된 재료를 인벤토리에서 제거하는 것은 호출부 책임이다.
        /// </summary>
        public bool TryAwaken(Player target, List<Player> materialCards)
        {
            if (target == null || target.Template == null) return false;
            if (!target.CanAwaken) return false; // ALLSTAR 이상 등급 검증
            if (target.AwakenLevel >= Player.MaxAwakenLevel) return false;
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

            target.AwakenLevel = Mathf.Min(target.AwakenLevel + gainedPoints, Player.MaxAwakenLevel);
            return true;
        }
    }
}
