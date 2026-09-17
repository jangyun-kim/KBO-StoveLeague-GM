using System.Collections.Generic;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 10연차 뽑기 버튼을 눌렀을 때 ScoutManager.Roll10()을 호출하고, 반환된 카드 10장을
    /// PlayerCardUI 프리팹으로 생성해 결과 팝업에 뿌린다. 그 중 SIGNATURE 이상 등급이 하나라도
    /// 있으면 화면 상단에 강조 문구를 띄운다.
    /// </summary>
    public class ScoutUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ScoutManager scoutManager;

        [Header("Roll Buttons")]
        [Tooltip("[TASK-KBO-092] 1회 뽑기 버튼. ScoutManager.Roll1()을 호출한다.")]
        [SerializeField] private Button roll1Button;
        [Tooltip("[TASK-KBO-092] 10회 뽑기 버튼. ScoutManager.Roll10()을 호출한다.")]
        [SerializeField] private Button roll10Button;

        [Header("Result Popup")]
        [SerializeField] private GameObject resultPopupRoot;
        [Tooltip("카드 10장이 배치될 부모. GridLayoutGroup을 붙여 자동 정렬한다.")]
        [SerializeField] private Transform cardContainer;
        [SerializeField] private PlayerCardUI cardPrefab;
        [Tooltip("[TASK-KBO-093] 결과 팝업을 닫는 '확인' 버튼. CloseResultPopup()을 호출한다.")]
        [SerializeField] private Button closeResultPopupButton;

        [Header("Top Pull Announcement")]
        [Tooltip("결과에 SIGNATURE 이상 등급 카드가 있을 때만 활성화되는 강조 텍스트.")]
        [SerializeField] private Text topPullAnnouncementText;
        [SerializeField] private float announcementDisplaySeconds = 2.5f;

        [Header("Navigation")]
        [Tooltip("[TASK-KBO-083] 스카우트 화면을 닫고 로비로 돌아가는 버튼. CheerleaderInventoryUIController/" +
                 "CheerleaderShopUIController의 closeButton과 동일한 관례로 UIManager.ShowScreen()만 호출한다.")]
        [SerializeField] private Button closeButton;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
            if (roll1Button != null) roll1Button.onClick.AddListener(ExecuteRoll1);
            if (roll10Button != null) roll10Button.onClick.AddListener(ExecuteRoll10);
            if (closeResultPopupButton != null) closeResultPopupButton.onClick.AddListener(CloseResultPopup);
        }

        // GDD 2절 등급 서열(숫자가 클수록 상위 등급). "최고급"의 기준(SIGNATURE 이상)을 여기서 정한다.
        private static readonly Dictionary<Grade, int> GradeRank = new Dictionary<Grade, int>
        {
            { Grade.LIVE_NORMAL, 0 },
            { Grade.LIVE_EPIC, 1 },
            { Grade.ALLSTAR, 2 },
            { Grade.TITLE_HOLDER, 3 },
            { Grade.GOLDEN_GLOVE, 4 },
            { Grade.SIGNATURE, 5 },
            { Grade.DYNASTY, 6 },
        };
        private const Grade TopPullThreshold = Grade.SIGNATURE;

        private readonly List<PlayerCardUI> spawnedCards = new List<PlayerCardUI>();

        /// <summary>[TASK-KBO-092] 1회 뽑기 버튼 OnClick. ScoutManager.Roll1()의 단일 결과를 리스트로
        /// 감싸 ShowResults()에 그대로 위임한다 - 카드 렌더링 경로를 10연차와 통일해 중복 로직을 만들지
        /// 않는다.</summary>
        public void ExecuteRoll1()
        {
            if (scoutManager == null)
            {
                Debug.LogWarning("[ScoutUIController] ScoutManager가 연결되지 않았습니다.");
                return;
            }

            var player = scoutManager.Roll1();
            ShowResults(player != null ? new List<Player> { player } : new List<Player>());
        }

        /// <summary>10연차 뽑기 버튼 OnClick.</summary>
        public void ExecuteRoll10()
        {
            if (scoutManager == null)
            {
                Debug.LogWarning("[ScoutUIController] ScoutManager가 연결되지 않았습니다.");
                return;
            }

            var results = scoutManager.Roll10();
            ShowResults(results);
        }

        private void ShowResults(List<Player> players)
        {
            ClearCards();

            if (players == null || players.Count == 0)
            {
                Debug.LogWarning("[ScoutUIController] 뽑기 결과가 비어 있습니다 (재화 부족 등으로 소모되지 않았을 수 있습니다).");
                return;
            }

            foreach (var player in players)
            {
                SpawnCard(player);
            }

            if (resultPopupRoot != null) resultPopupRoot.SetActive(true);

            AnnounceTopPullIfAny(players);
        }

        private void SpawnCard(Player player)
        {
            if (cardPrefab == null || cardContainer == null) return;

            var card = Instantiate(cardPrefab, cardContainer);
            card.gameObject.SetActive(true);
            card.Setup(player);
            spawnedCards.Add(card);
        }

        /// <summary>
        /// SIGNATURE 이상 등급이 하나라도 있으면 상단에 강조 문구를 띄운다.
        /// 지금은 텍스트 노출/자동 숨김만 구현한 연출 뼈대이며, 실제 파티클/사운드 등은 후속 과제다.
        /// </summary>
        private void AnnounceTopPullIfAny(List<Player> players)
        {
            Player best = null;
            int bestRank = -1;

            foreach (var player in players)
            {
                if (player?.Template == null) continue;

                int rank = GradeRank.TryGetValue(player.Template.Grade, out var r) ? r : 0;
                if (rank > bestRank)
                {
                    bestRank = rank;
                    best = player;
                }
            }

            if (best == null || bestRank < GradeRank[TopPullThreshold]) return;

            string message = $"★ 최고급 선수 획득! {best.Template.PlayerName} ({best.Template.Grade}) ★";
            Debug.Log($"[ScoutUIController] {message}");

            if (topPullAnnouncementText == null) return;

            topPullAnnouncementText.text = message;
            topPullAnnouncementText.gameObject.SetActive(true);
            CancelInvoke(nameof(HideAnnouncement));
            Invoke(nameof(HideAnnouncement), announcementDisplaySeconds);
        }

        private void HideAnnouncement()
        {
            if (topPullAnnouncementText != null) topPullAnnouncementText.gameObject.SetActive(false);
        }

        /// <summary>결과 팝업의 닫기 버튼 OnClick.</summary>
        public void CloseResultPopup()
        {
            if (resultPopupRoot != null) resultPopupRoot.SetActive(false);
            ClearCards();
        }

        private void ClearCards()
        {
            foreach (var card in spawnedCards)
            {
                if (card != null) Destroy(card.gameObject);
            }
            spawnedCards.Clear();
        }
    }
}
