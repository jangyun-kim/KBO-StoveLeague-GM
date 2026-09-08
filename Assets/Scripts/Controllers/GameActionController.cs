using System.Collections.Generic;
using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Controllers
{
    /// <summary>
    /// UGUI 버튼의 OnClick()이 직접 참조하는 액션 엔드포인트.
    /// 매니저(UpgradeManager/RosterManager)를 호출하고, 성공 시 GameManager의
    /// 인벤토리/로스터 상태를 실제로 변경(Mutation)하는 것까지 이 클래스가 책임진다.
    /// </summary>
    public class GameActionController : MonoBehaviour
    {
        [Header("Enhance Target (UI에서 선택된 대상)")]
        [SerializeField] private Player enhanceTarget;
        [SerializeField] private List<Item> enhanceMaterials = new List<Item>();

        [Header("Awaken Target (UI에서 선택된 대상)")]
        [SerializeField] private Player awakenTarget;
        [SerializeField] private List<Player> awakenMaterials = new List<Player>();

        [Header("Auto Roster")]
        [SerializeField] private RosterManager rosterManager;
        [SerializeField] private int salaryCap = 300;

        /// <summary>강화 버튼 OnClick. 성공 시 소모된 재료(Item)를 인벤토리에서 제거한다.</summary>
        public void ExecuteEnhance()
        {
            if (UpgradeManager.Instance == null || GameManager.Instance == null || enhanceTarget == null) return;

            var usedMaterials = new List<Item>(enhanceMaterials);
            bool success = UpgradeManager.Instance.TryEnhance(enhanceTarget, usedMaterials);

            if (success)
            {
                foreach (var item in usedMaterials)
                {
                    GameManager.Instance.RemoveItemFromInventory(item);
                }
                enhanceMaterials.Clear();
            }

            Debug.Log($"[GameActionController] ExecuteEnhance: {(success ? "성공" : "실패")} " +
                      $"(대상: {enhanceTarget.Template?.PlayerName}, 현재 {enhanceTarget.ReinforceLevel}강)");
        }

        /// <summary>각성 버튼 OnClick. 성공 시 소모된 재료(Player) 카드를 인벤토리에서 제거한다.</summary>
        public void ExecuteAwaken()
        {
            if (UpgradeManager.Instance == null || GameManager.Instance == null || awakenTarget == null) return;

            var usedMaterials = new List<Player>(awakenMaterials);
            bool success = UpgradeManager.Instance.TryAwaken(awakenTarget, usedMaterials);

            if (success)
            {
                foreach (var material in usedMaterials)
                {
                    GameManager.Instance.RemovePlayerFromInventory(material);
                }
                awakenMaterials.Clear();
            }

            Debug.Log($"[GameActionController] ExecuteAwaken: {(success ? "성공" : "실패")} " +
                      $"(대상: {awakenTarget.Template?.PlayerName}, 현재 {awakenTarget.AwakenLevel}각)");
        }

        /// <summary>오토 라인업 버튼 OnClick. 인벤토리 기준 28인을 자동 편성해 GameManager.Roster를 덮어쓴다.</summary>
        public void ExecuteAutoRoster()
        {
            if (rosterManager == null || GameManager.Instance == null) return;

            var newRoster = rosterManager.AutoSetRoster(GameManager.Instance.Inventory.ToList(), salaryCap);
            GameManager.Instance.OverwriteRoster(newRoster);

            Debug.Log($"[GameActionController] ExecuteAutoRoster: {newRoster.Count}명 편성 완료 (샐러리 캡 {salaryCap})");
        }
    }
}
