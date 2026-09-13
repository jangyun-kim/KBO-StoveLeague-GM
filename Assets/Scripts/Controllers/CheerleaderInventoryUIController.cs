using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-058] 유저가 보유한 치어리더(GameManager.Instance.OwnedCheerleaders) 전체를
    /// CheerleaderSlotUI 프리팹으로 그려주고, 슬롯의 장착/해제 버튼을 GameManager.EquipCheerleader()/
    /// UnequipCheerleader()로 그대로 위임하는 인벤토리 화면 컨트롤러. GameManager.OnCheerleaderChanged를
    /// 구독해 장착 상태가 바뀔 때마다(이 화면이 아닌 다른 경로에서 바뀐 경우 포함) 자동으로 다시 그린다.
    /// </summary>
    public class CheerleaderInventoryUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform contentContainer;
        [SerializeField] private GameObject slotPrefab;

        [Header("Navigation")]
        [Tooltip("로비 화면(UIManager.ScreenType.Lobby)으로 돌아가는 버튼. LeagueDashboardUIController를 " +
                 "직접 참조하지 않고 UIManager.ShowScreen()만 호출한다. 이 화면 자신은 " +
                 "UIManager.ScreenType.CheerleaderInventory에 등록된다(기존 ScreenType.Inventory는 " +
                 "선수 카드 인벤토리 전용으로 이미 예약돼 있어 재사용하지 않음).")]
        [SerializeField] private Button closeButton;

        /// <summary>[TASK-KBO-060] 버튼 리스너는 Awake()에서 한 번만 등록한다 - OnEnable은 화면 전환마다
        /// (UIManager.ShowScreen()이 SetActive(true)할 때마다) 반복 호출되므로, 거기서 AddListener를
        /// 하면 열 때마다 리스너가 중복 등록된다.</summary>
        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
        }

        private void OnEnable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnCheerleaderChanged += RefreshInventory;
            }

            RefreshInventory();
        }

        private void OnDisable()
        {
            if (GameManager.Instance != null)
            {
                GameManager.Instance.OnCheerleaderChanged -= RefreshInventory;
            }
        }

        /// <summary>contentContainer의 기존 자식을 모두 파괴한 뒤, GameManager.Instance.OwnedCheerleaders를
        /// 순회하며 slotPrefab을 새로 Instantiate해 채운다. v0.1 프로토타입 단계라 오브젝트 풀링 없이
        /// 단순 Destroy&amp;Instantiate를 쓴다(명령서 7항 - 오버엔지니어링 회피).</summary>
        public void RefreshInventory()
        {
            if (contentContainer == null || slotPrefab == null) return;

            for (int i = contentContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(contentContainer.GetChild(i).gameObject);
            }

            if (GameManager.Instance == null) return;

            var owned = GameManager.Instance.OwnedCheerleaders;
            if (owned == null || owned.Count == 0) return;

            var equipped = GameManager.Instance.EquippedCheerleader;

            foreach (var cheerleader in owned)
            {
                if (cheerleader == null) continue;

                var slotObject = Instantiate(slotPrefab, contentContainer);
                var slot = slotObject.GetComponent<CheerleaderSlotUI>();
                if (slot == null) continue;

                bool isEquipped = IsSameCheerleader(equipped, cheerleader);
                slot.Initialize(cheerleader, isEquipped,
                    onEquip: target => GameManager.Instance.EquipCheerleader(target),
                    onUnequip: () => GameManager.Instance.UnequipCheerleader());
            }
        }

        /// <summary>
        /// 참조 동일성(==) 대신 InstanceId로 비교한다 - 세이브/로드를 거치면 EquippedCheerleader와
        /// OwnedCheerleaders의 원소가 JsonUtility에 의해 서로 다른 C# 인스턴스로 복원되므로(같은
        /// 치어리더라도 참조가 달라짐), ID 비교만이 "지금 장착된 것과 같은 치어리더인지"를 안정적으로
        /// 판별할 수 있다. 빈 InstanceId끼리는 절대 같다고 보지 않는다(둘 다 "미확정" 상태일 뿐 서로
        /// 다른 데이터일 수 있음).
        /// </summary>
        private static bool IsSameCheerleader(Cheerleader a, Cheerleader b)
        {
            if (a == null || b == null) return false;
            if (string.IsNullOrEmpty(a.InstanceId) || string.IsNullOrEmpty(b.InstanceId)) return false;

            return a.InstanceId == b.InstanceId;
        }
    }
}
