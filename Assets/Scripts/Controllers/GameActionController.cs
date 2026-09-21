using System;
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
        // [TASK-KBO-138] 강화 재료가 Item(소모 아이템)에서 Player(보유 선수 카드)로 바뀌었다 - EXP
        // 누적 확정 강화 개편, 아래 ExecuteEnhance() 참고.
        private readonly List<Player> enhanceMaterials = new List<Player>();

        private Player awakenTarget;
        private readonly List<Player> awakenMaterials = new List<Player>();

        public Player EnhanceTarget => enhanceTarget;
        public IReadOnlyList<Player> EnhanceMaterials => enhanceMaterials;

        public Player AwakenTarget => awakenTarget;
        public IReadOnlyList<Player> AwakenMaterials => awakenMaterials;

        /// <summary>ExecuteAutoRoster()가 GameManager.Roster를 실제로 덮어쓴 직후 발생한다.
        /// RosterUIController 등 로스터를 별도로 그리는 화면이 이 이벤트만 구독하면 재조립 없이 자동 갱신된다.</summary>
        public event Action OnRosterChanged;

        /// <summary>ExecuteEnhance()의 성공/실패가 결정된 직후 발생한다(대상 선수, 성공 여부).
        /// InventoryUIController가 이를 구독해 VFXController로 성공/실패 연출을 트리거한다 - 성공/실패
        /// 여부는 원래 Debug.Log로만 남고 UI로 전파되지 않았는데, 이 이벤트가 그 경로를 열어 준다.</summary>
        public event Action<Player, bool> OnEnhanceCompleted;

        // ----- Enhance 대상/재료 주입 (인벤토리 UI가 카드를 클릭할 때 호출) -----

        public void SetEnhanceTarget(Player target)
        {
            enhanceTarget = target;
        }

        public void AddEnhanceMaterial(Player material)
        {
            if (material == null || material == enhanceTarget || enhanceMaterials.Contains(material)) return;
            if (enhanceMaterials.Count >= UpgradeManager.MaxEnhanceMaterials) return;

            enhanceMaterials.Add(material);
        }

        public void RemoveEnhanceMaterial(Player material)
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

        /// <summary>강화 버튼 OnClick. [TASK-KBO-138] 재료가 Item에서 Player 카드로 바뀌어, 성공 시
        /// 소모된 재료(Player)를 인벤토리에서 제거한다(RemovePlayerFromInventory - Awaken과 동일한
        /// 경로 재사용).</summary>
        public void ExecuteEnhance()
        {
            if (UpgradeManager.Instance == null || GameManager.Instance == null || enhanceTarget == null) return;

            var usedMaterials = new List<Player>(enhanceMaterials);
            bool success = UpgradeManager.Instance.TryEnhance(enhanceTarget, usedMaterials);

            Debug.Log($"[GameActionController] ExecuteEnhance: {(success ? "성공" : "실패")} " +
                      $"(대상: {enhanceTarget.Template?.PlayerName}, 현재 {enhanceTarget.ReinforceLevel}강, " +
                      $"EXP {enhanceTarget.ReinforceExp}/{UpgradeConstants.GetRequiredExp(enhanceTarget)})");

            var completedTarget = enhanceTarget;

            if (success)
            {
                foreach (var material in usedMaterials)
                {
                    GameManager.Instance.RemovePlayerFromInventory(material);
                }
                ClearEnhanceSelection();
            }

            OnEnhanceCompleted?.Invoke(completedTarget, success);
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
            OnRosterChanged?.Invoke();
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
