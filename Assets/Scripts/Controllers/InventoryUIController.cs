using System.Collections.Generic;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// GameManager.Instance.Inventory 전체를 PlayerCardUI 목록으로 그리는 인벤토리 메인 허브. 카드를
    /// 클릭하면 `PlayerDetailUIController`(4탭 상세 창, TASK-147)를 띄운다. 카드 목록은
    /// CardPoolManager로 재사용한다.
    ///
    /// [TASK-KBO-145] 카드 클릭 시 뜨던 "기존의 단순 팝업"(강화하기/각성하기/스킬 변경 버튼 3개만
    /// 나열하던 구 상세 패널)을 폐기하고 `PlayerManagementUIController`(격자형 선수 관리 허브)를 대신
    /// 띄웠다 - 이 흐름은 TASK-150이 다시 뒤집었다(아래 참고).
    ///
    /// [TASK-KBO-147, 전면 개편] 구 상세 패널(`detailPanelRoot` 및 그 하위 텍스트 나열형 구성 -
    /// `detailReinforceText`/`detailAwakenText`/`detailSkillsText`/`enhanceButton`/`awakenButton`/
    /// `skillChangeButton` 등)을 명령서 지시대로 완전히 폐기했다 - 씬에서 해당 GameObject 전부를
    /// DestroyImmediate로 제거하고, 탭 UI(기본 스탯/특이폼·페이스/핫·콜드존/스킬)를 갖춘 신규
    /// `PlayerDetailUIController`로 교체했다(`SetupInventoryUI.cs` 참고). `ShowDetail()`/`CloseDetail()`은
    /// `playerDetailUIController`에 얇게 위임만 한다.
    ///
    /// [TASK-KBO-150, 진입 흐름 역전] 기획자 의도에 따라 "인벤토리 → 허브 → 상세창"이던 진입 순서를
    /// "인벤토리 → 상세창 → 허브"로 뒤집었다 - `SpawnCard()`의 클릭 리스너가 이제 `OpenPlayerDetail()`을
    /// 호출해 1차로 `PlayerDetailUIController.Show()`(=`ShowDetail()`)를 연다. 허브로 가는 경로는
    /// 상세 창 하단의 [선수 관리] 버튼(`PlayerDetailUIController.playerManagementButton`, TASK-150)으로
    /// 옮겨졌다 - TASK-148이 만든 "허브 상단 초상화 클릭 → 상세창"(뒤로 가기 성격) 로직은 명령서 5항
    /// 지시대로 그대로 남겨 뒀다. `playerManagementUIController` 필드는 `playerDetailUIController`가
    /// 비어 있는 구형 씬을 위한 폴백으로만 남는다(우선순위 역전, 필드 자체는 유지 - `SetupPlayerManagementUI.cs`가
    /// 여전히 이 필드에 바인딩하므로 삭제하면 그 에디터 스크립트가 깨진다).
    /// </summary>
    public class InventoryUIController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("[TASK-KBO-150] playerDetailUIController가 비어 있을 때(구형 씬)만 쓰는 폴백 - " +
                 "정상 흐름에서는 카드 클릭이 상세 창을 직접 연다.")]
        [SerializeField] private PlayerManagementUIController playerManagementUIController;

        [Header("Card List")]
        [Tooltip("카드가 나열될 부모(Scroll View의 Content). GridLayoutGroup을 붙여 정렬한다.")]
        [SerializeField] private Transform cardContainer;
        [Tooltip("Button 컴포넌트가 반드시 포함된 PlayerCardUI 프리팹. 재료 선택 팝업과 같은 프리팹을 " +
                 "연결하면 CardPoolManager 풀을 공유해 카드 인스턴스를 재사용한다.")]
        [SerializeField] private PlayerCardUI cardPrefab;

        [Header("Detail Panel (TASK-KBO-147 - 탭 UI 전면 개편)")]
        [Tooltip("[TASK-KBO-150] 카드 클릭 시 1차로 여는 4탭 상세 창(구 텍스트 나열형 DetailPanel 대체).")]
        [SerializeField] private PlayerDetailUIController playerDetailUIController;

        [Header("Close Buttons")]
        [Tooltip("[TASK-KBO-111] 인벤토리 화면을 닫고 로비로 돌아가는 버튼. UIManager.ShowScreen()만 호출한다.")]
        [SerializeField] private Button closeButton;

        private readonly List<PlayerCardUI> spawnedCards = new List<PlayerCardUI>();

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));

            CloseDetail();
        }

        private void OnEnable()
        {
            // [TASK-KBO-121] UIManager.ShowScreen()이 이 패널을 SetActive(true)할 때마다 최신
            // GameManager.Instance.Inventory로 카드 목록을 다시 그린다 - 기존에는 이 호출이 없어
            // 화면에 처음 진입하면(또는 재진입해도) 카드가 갱신되지 않고 비어 보였다.
            RefreshInventory();

            // [TASK-KBO-147] 상세 패널의 내부 닫기(X) 버튼이 눌리면 메인 닫기 버튼을 되돌린다.
            if (playerDetailUIController != null) playerDetailUIController.OnClosed += CloseDetail;
        }

        private void OnDisable()
        {
            // [TASK-KBO-124] 상세 패널을 연 채로 화면을 나가면(다른 화면 버튼 클릭 등으로 이 패널이
            // SetActive(false)됨) 다음 재진입 시 이전 상태가 그대로 다시 보이는 상태 누수가 있었다 -
            // CloseDetail()로 항상 초기화한다.
            CloseDetail();

            if (playerDetailUIController != null) playerDetailUIController.OnClosed -= CloseDetail;
        }

        /// <summary>GameManager.Instance.Inventory 전체를 다시 읽어 카드 리스트를 새로 그린다.</summary>
        public void RefreshInventory()
        {
            ClearCards();

            if (GameManager.Instance == null || cardPrefab == null || cardContainer == null) return;

            foreach (var player in GameManager.Instance.Inventory)
            {
                SpawnCard(player);
            }
        }

        private void SpawnCard(Player player)
        {
            var card = CardPoolManager.Instance != null
                ? CardPoolManager.Instance.Get(cardPrefab, cardContainer)
                : Instantiate(cardPrefab, cardContainer);

            // [TASK-KBO-121] 방어 코드 - 풀링/템플릿 유래로 스케일이 흐트러진 카드가 눈에 안 보이는
            // 크기로 렌더링되는 사고를 막는다(cardContainer의 GridLayoutGroup이 위치/크기는 통제하지만
            // localScale까지는 건드리지 않는다).
            card.transform.localScale = Vector3.one;

            card.Setup(player);

            var button = card.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogWarning("[InventoryUIController] 카드 프리팹에 Button 컴포넌트가 없어 클릭 이벤트를 연결하지 못했습니다.");
            }
            else
            {
                // 풀링된 카드는 같은 Button 인스턴스가 재사용되므로, 이전 대여에서 붙은 리스너(다른 player를
                // 가리키는 클로저)가 쌓이지 않도록 먼저 전부 제거한 뒤 새로 연결한다.
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => OpenPlayerDetail(player));
            }

            spawnedCards.Add(card);
        }

        /// <summary>[TASK-KBO-150, 진입 흐름 역전] 카드 클릭 진입점. 이제 4탭 상세 창이 1차 목적지다 -
        /// `playerDetailUIController`가 배선되어 있으면 그쪽을 열고, 아직 배선 전(구형 씬)이면 선수 관리
        /// 허브로 폴백해 클릭이 조용히 무시되는 것을 막는다(TASK-145가 확립한 폴백 관례를 방향만
        /// 뒤집어 재사용).</summary>
        private void OpenPlayerDetail(Player player)
        {
            if (playerDetailUIController != null)
            {
                ShowDetail(player);
            }
            else if (playerManagementUIController != null)
            {
                playerManagementUIController.Show(player);
            }
            else
            {
                Debug.LogWarning("[InventoryUIController] playerDetailUIController/playerManagementUIController " +
                    "모두 바인딩되지 않아 카드 클릭에 반응할 수 없습니다.");
            }
        }

        private void ClearCards()
        {
            foreach (var card in spawnedCards)
            {
                if (card == null) continue;

                if (CardPoolManager.Instance != null) CardPoolManager.Instance.Release(card);
                else Destroy(card.gameObject);
            }
            spawnedCards.Clear();
        }

        // ----- 상세 정보 패널 (TASK-KBO-147 - PlayerDetailUIController에 위임) -----

        private void ShowDetail(Player player)
        {
            if (player?.Template == null) return;

            if (playerDetailUIController != null) playerDetailUIController.Show(player);

            // [TASK-KBO-135] 상세 패널이 열려 있는 동안은 메인 인벤토리 닫기 버튼(로비로 돌아가기)을
            // 숨겨, 우측 상단에 두 닫기 버튼이 겹쳐 보이는 UX 결함을 막는다 - CloseDetail()에서 되돌린다.
            if (closeButton != null) closeButton.gameObject.SetActive(false);
        }

        public void CloseDetail()
        {
            if (playerDetailUIController != null) playerDetailUIController.Hide();

            // [TASK-KBO-135] ShowDetail()에서 숨긴 메인 닫기 버튼을 되돌린다.
            if (closeButton != null) closeButton.gameObject.SetActive(true);
        }
    }
}
