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
    /// 클릭하면 `PlayerManagementUIController`(선수 관리 허브, TASK-145)를 띄운다. 카드 목록은
    /// CardPoolManager로 재사용한다.
    ///
    /// [TASK-KBO-145] 카드 클릭 시 뜨던 "기존의 단순 팝업"(강화하기/각성하기/스킬 변경 버튼 3개만
    /// 나열하던 구 상세 패널)을 폐기하고 `PlayerManagementUIController`(격자형 선수 관리 허브)를 대신
    /// 띄운다 - `SpawnCard()`의 클릭 리스너가 `OpenPlayerManagement()`를 호출한다.
    ///
    /// [TASK-KBO-147, 전면 개편] 구 상세 패널(`detailPanelRoot` 및 그 하위 텍스트 나열형 구성 -
    /// `detailReinforceText`/`detailAwakenText`/`detailSkillsText`/`enhanceButton`/`awakenButton`/
    /// `skillChangeButton` 등)을 명령서 지시대로 완전히 폐기했다 - 씬에서 해당 GameObject 전부를
    /// DestroyImmediate로 제거하고, 탭 UI(기본 스탯/특이폼·페이스/핫·콜드존/스킬)를 갖춘 신규
    /// `PlayerDetailUIController`로 교체했다(`SetupInventoryUI.cs` 참고). 구 상세 패널이 담당하던
    /// [강화하기]/[각성하기]/[스킬 변경] 기능은 이미 TASK-145/146에서 `PlayerManagementUIController`/
    /// `EnhanceUIController`로 완전히 이관되어 중복이었으므로(사용자가 카드를 클릭하면 애초에 구
    /// 상세 패널이 아니라 선수 관리 허브가 뜬다 - 위 TASK-145 항목 참고), 새 상세 패널은 순수 "정보
    /// 열람"(스탯/스킬 등) 전용으로 좁혀 재설계했고 그 두 버튼/로직은 이관과 함께 자연히 제거됐다.
    /// `ShowDetail()`/`CloseDetail()`은 이제 `playerDetailUIController`에 얇게 위임만 한다.
    /// </summary>
    public class InventoryUIController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("[TASK-KBO-145] 카드 클릭 시 뜨는 신규 '선수 관리 허브'. 비어 있으면(구형 씬 - Setup " +
                 "메뉴 재실행 전) 기존 ShowDetail() 팝업으로 자동 폴백한다.")]
        [SerializeField] private PlayerManagementUIController playerManagementUIController;

        [Header("Card List")]
        [Tooltip("카드가 나열될 부모(Scroll View의 Content). GridLayoutGroup을 붙여 정렬한다.")]
        [SerializeField] private Transform cardContainer;
        [Tooltip("Button 컴포넌트가 반드시 포함된 PlayerCardUI 프리팹. 재료 선택 팝업과 같은 프리팹을 " +
                 "연결하면 CardPoolManager 풀을 공유해 카드 인스턴스를 재사용한다.")]
        [SerializeField] private PlayerCardUI cardPrefab;

        [Header("Detail Panel (TASK-KBO-147 - 탭 UI 전면 개편)")]
        [Tooltip("[TASK-KBO-147] 구 텍스트 나열형 DetailPanel을 대체하는 신규 탭 UI 컨트롤러. " +
                 "playerManagementUIController가 비어 있을 때의 폴백 경로(OpenPlayerManagement() 참고)에서만 열린다.")]
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
                button.onClick.AddListener(() => OpenPlayerManagement(player));
            }

            spawnedCards.Add(card);
        }

        /// <summary>[TASK-KBO-145] 카드 클릭 진입점. 신규 선수 관리 허브가 배선되어 있으면 그쪽을 띄우고,
        /// 아직 배선 전(구형 씬)이면 신규 상세 패널(TASK-KBO-147)로 폴백해 클릭이 조용히 무시되는 것을
        /// 막는다.</summary>
        private void OpenPlayerManagement(Player player)
        {
            if (playerManagementUIController != null)
            {
                playerManagementUIController.Show(player);
            }
            else
            {
                ShowDetail(player);
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
