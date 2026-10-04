using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-183] 선수 관리 허브의 [한계 돌파] · [훈련(특훈)] 순수 규칙(MonoBehaviour 없음 - UI/단위 테스트 공용).
    /// [강화](UpgradeManager.TryEnhance, EXP 누적)와 [각성](UpgradeManager.TryAwaken, 동일 선수 재료)은 기존 경로를 그대로 쓴다.
    ///   - 한계 돌파: 10강 달성 카드만. 재료 1장(동일 선수 카드이거나, 같은 위상 이상 등급 카드)을 소모해 1단계(OVR +1) 상승.
    ///     등급별 상한 CardGrowthRules.LimitBreakCap(LIVE 2 ~ 상위 시즌 5). 1군 로스터 카드는 재료로 쓰지 않는다.
    ///   - 훈련(특훈): 게임 머니(볼)를 소모해 1단계(OVR +1) 상승. 비용 = TrainingGoldPerLevel x 다음 단계. 등급별 상한 CardGrowthRules.TrainingCap.
    /// 결과는 Player.LimitBreakLevel / TrainingLevel에 저장되고 SaveManager(PlayerSaveData v10)가 세이브에 기록한다.
    /// </summary>
    public static class CardGrowthActions
    {
        public const int TrainingGoldPerLevel = 1000;

        public static int TrainingGoldCost(int currentTrainingLevel) => TrainingGoldPerLevel * (currentTrainingLevel + 1);

        public static bool CanLimitBreak(Player target, out string reason)
        {
            reason = null;
            if (target?.Template == null) { reason = "선수 카드가 없습니다."; return false; }
            int cap = CardGrowthRules.LimitBreakCap(target.Template.Grade);
            if (target.LimitBreakLevel >= cap) { reason = $"한계 돌파 최대 단계({cap}단계)입니다."; return false; }
            if (target.ReinforceLevel < Player.MaxReinforceLevel) { reason = $"10강 달성 후 한계 돌파할 수 있습니다(현재 {target.ReinforceLevel}강)."; return false; }
            return true;
        }

        public static bool IsValidLimitBreakMaterial(Player target, Player material)
        {
            if (target?.Template == null || material?.Template == null || material == target) return false;
            if (material.Template.RealPlayerId == target.Template.RealPlayerId) return true;
            return CardGrowthRules.PowerRank(material.Template.Grade) >= CardGrowthRules.PowerRank(target.Template.Grade);
        }

        /// <summary>보관 카드 중 재료 자동 선택 - 동일 선수 카드 우선, 그다음 낮은 등급 → 낮은 OVR → 낮은 성장 순(아까운 카드를 늦게 쓴다).</summary>
        public static Player PickLimitBreakMaterial(Player target, IEnumerable<Player> inventory, IEnumerable<Player> roster)
        {
            var locked = new HashSet<Player>(roster ?? Enumerable.Empty<Player>());
            return (inventory ?? Enumerable.Empty<Player>())
                .Where(p => IsValidLimitBreakMaterial(target, p) && !locked.Contains(p))
                .OrderByDescending(p => p.Template.RealPlayerId == target.Template.RealPlayerId)
                .ThenBy(p => CardGrowthRules.PowerRank(p.Template.Grade))
                .ThenBy(p => p.CalculateOVR(false))
                .ThenBy(p => p.GetStatGrowth())
                .FirstOrDefault();
        }

        /// <summary>한계 돌파 1단계. 재료를 인벤토리에서 제거하는 것은 호출부 책임이다.</summary>
        public static bool TryLimitBreak(Player target, Player material, out string message)
        {
            if (!CanLimitBreak(target, out message)) return false;
            if (!IsValidLimitBreakMaterial(target, material))
            {
                message = "한계 돌파 재료가 없습니다 - 동일 선수 카드 또는 같은 등급 이상 보관 카드 1장이 필요합니다.";
                return false;
            }
            target.LimitBreakLevel++;
            message = $"한계 돌파 {target.LimitBreakLevel}/{CardGrowthRules.LimitBreakCap(target.Template.Grade)}단계 성공! OVR +1 (재료: {material.Template.PlayerName})";
            return true;
        }

        public static bool CanTrain(Player target, int gold, out string reason)
        {
            reason = null;
            if (target?.Template == null) { reason = "선수 카드가 없습니다."; return false; }
            int cap = CardGrowthRules.TrainingCap(target.Template.Grade);
            if (target.TrainingLevel >= cap) { reason = $"특훈 최대 단계({cap}단계)입니다."; return false; }
            int cost = TrainingGoldCost(target.TrainingLevel);
            if (gold < cost) { reason = $"볼이 부족합니다(필요 {cost:N0} / 보유 {gold:N0})."; return false; }
            return true;
        }

        /// <summary>훈련(특훈) 1단계. 성공하면 소모한 볼 수를 spentGold로 돌려준다(차감은 호출부가 GameManager.GameGold에 반영).</summary>
        public static bool TryTrain(Player target, int gold, out int spentGold, out string message)
        {
            spentGold = 0;
            if (!CanTrain(target, gold, out message)) return false;
            spentGold = TrainingGoldCost(target.TrainingLevel);
            target.TrainingLevel++;
            message = $"특훈 {target.TrainingLevel}/{CardGrowthRules.TrainingCap(target.Template.Grade)}단계 완료! OVR +1 (볼 -{spentGold:N0})";
            return true;
        }

        /// <summary>선수 관리/상세 화면 표기 - [기본 OVR / 현재 성장(+N) / 시너지(+M) / 최대 잠재 OVR] + 항목별 진행도.</summary>
        public static string GrowthSummary(Player player, int teamSynergyOvr)
        {
            if (player?.Template == null) return "";
            var g = player.Template.Grade;
            int current = TeamSynergyRules.ClampFinal(player.CalculateNeutralOVR() + teamSynergyOvr);
            return $"기본 OVR {player.BaseOvr}  ·  성장 +{player.GetStatGrowth()}/{player.MaxGrowth}  ·  시너지 +{teamSynergyOvr}  ·  " +
                   $"현재 {current}  ·  최대 잠재 {player.MaxPotentialOvr}\n" +
                   $"강화 {player.ReinforceGrowth}/{CardGrowthRules.MaxReinforceGrowth}  한계 돌파 {player.LimitBreakGrowth}/{CardGrowthRules.LimitBreakCap(g)}  " +
                   $"특훈 {player.TrainingGrowth}/{CardGrowthRules.TrainingCap(g)}  각성 {player.AwakenGrowth}/{CardGrowthRules.AwakenGrowthCap(g)} ({player.AwakenLabel})";
        }
    }
}
