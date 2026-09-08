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
    ///
    /// 강화/각성 대상·재료는 인스펙터에 고정하지 않는다. 인벤토리 UI가 카드를 클릭할 때마다
    /// SetEnhanceTarget/AddEnhanceMaterial 등을 런타임에 호출해 상태를 채워 넣는 방식으로 동작한다.
    /// </summary>
    public class GameActionController : MonoBehaviour
    {
        [Header("Auto Roster")]
        [SerializeField] private RosterManager rosterManager;
        [SerializeField] private int salaryCap = 300;

        private Player enhanceTarget;
        private readonly List<Item> enhanceMaterials = new List<Item>();

        private Player awakenTarget;
        private readonly List<Player> awakenMaterials = new List<Player>();

        public Player EnhanceTarget => enhanceTarget;
        public IReadOnlyList<Item> EnhanceMaterials => enhanceMaterials;

        public Player AwakenTarget => awakenTarget;
        public IReadOnlyList<Player> AwakenMaterials => awakenMaterials;

        // ----- Enhance 대상/재료 주입 (인벤토리 UI가 카드를 클릭할 때 호출) -----

        public void SetEnhanceTarget(Player target)
        {
            enhanceTarget = target;
        }

        public void AddEnhanceMaterial(Item material)
        {
            if (material == null || enhanceMaterials.Contains(material)) return;
            if (enhanceMaterials.Count >= UpgradeManager.MaxEnhanceMaterials) return;

            enhanceMaterials.Add(material);
        }

        public void RemoveEnhanceMaterial(Item material)
        {
            enhanceMaterials.Remove(material);
        }

        public void ClearEnhanceSelection()
        {
            enhanceTarget = null;
            enhanceMaterials.Clear();
        }

        // ----- Awaken 대상/재료 주입 (인벤토리 UI가 카드를 클릭할 때 호출) -----

        public void SetAwakenTarget(Player target)
        {
            awakenTarget = target;
        }

        public void AddAwakenMaterial(Player material)
        {
            if (material == null || material == awakenTarget || awakenMaterials.Contains(material)) return;

            awakenMaterials.Add(material);
        }

        public void RemoveAwakenMaterial(Player material)
        {
            awakenMaterials.Remove(material);
        }

        public void ClearAwakenSelection()
        {
            awakenTarget = null;
            awakenMaterials.Clear();
        }

        // ----- Auto Roster 설정 -----

        public void SetSalaryCap(int cap)
        {
            salaryCap = cap;
        }

        // ----- 버튼 OnClick 엔드포인트 -----

        /// <summary>강화 버튼 OnClick. 성공 시 소모된 재료(Item)를 인벤토리에서 제거한다.</summary>
        public void ExecuteEnhance()
        {
            if (UpgradeManager.Instance == null || GameManager.Instance == null || enhanceTarget == null) return;

            var usedMaterials = new List<Item>(enhanceMaterials);
            bool success = UpgradeManager.Instance.TryEnhance(enhanceTarget, usedMaterials);

            Debug.Log($"[GameActionController] ExecuteEnhance: {(success ? "성공" : "실패")} " +
                      $"(대상: {enhanceTarget.Template?.PlayerName}, 현재 {enhanceTarget.ReinforceLevel}강)");

            if (success)
            {
                foreach (var item in usedMaterials)
                {
                    GameManager.Instance.RemoveItemFromInventory(item);
                }
                ClearEnhanceSelection();
            }
        }

        /// <summary>각성 버튼 OnClick. 성공 시 소모된 재료(Player) 카드를 인벤토리에서 제거한다.</summary>
        public void ExecuteAwaken()
        {
            if (UpgradeManager.Instance == null || GameManager.Instance == null || awakenTarget == null) return;

            var usedMaterials = new List<Player>(awakenMaterials);
            bool success = UpgradeManager.Instance.TryAwaken(awakenTarget, usedMaterials);

            Debug.Log($"[GameActionController] ExecuteAwaken: {(success ? "성공" : "실패")} " +
                      $"(대상: {awakenTarget.Template?.PlayerName}, 현재 {awakenTarget.AwakenLevel}각)");

            if (success)
            {
                foreach (var material in usedMaterials)
                {
                    GameManager.Instance.RemovePlayerFromInventory(material);
                }
                ClearAwakenSelection();
            }
        }

        /// <summary>오토 라인업 버튼 OnClick. 인벤토리 기준 28인을 자동 편성해 GameManager.Roster를 덮어쓴다.</summary>
        public void ExecuteAutoRoster()
        {
            if (rosterManager == null || GameManager.Instance == null) return;

            var newRoster = rosterManager.AutoSetRoster(GameManager.Instance.Inventory.ToList(), salaryCap);
            GameManager.Instance.OverwriteRoster(newRoster);

            Debug.Log($"[GameActionController] ExecuteAutoRoster: {newRoster.Count}명 편성 완료 (샐러리 캡 {salaryCap})");
        }

        /// <summary>저장 버튼 OnClick. SaveManager.SaveGame()으로 바로 위임한다.</summary>
        public void ExecuteSaveGame()
        {
            if (SaveManager.Instance == null)
            {
                Debug.LogWarning("[GameActionController] SaveManager가 없어 저장할 수 없습니다.");
                return;
            }

            SaveManager.Instance.SaveGame();
        }

        /// <summary>불러오기 버튼 OnClick. SaveManager.LoadGame()으로 바로 위임한다.</summary>
        public void ExecuteLoadGame()
        {
            if (SaveManager.Instance == null)
            {
                Debug.LogWarning("[GameActionController] SaveManager가 없어 불러올 수 없습니다.");
                return;
            }

            bool success = SaveManager.Instance.LoadGame();
            Debug.Log($"[GameActionController] ExecuteLoadGame: {(success ? "성공" : "실패")}");
        }
    }
}
