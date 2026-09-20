using System;
using System.Collections.Generic;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 선수 영입 화면. GDD "뽑기(가챠) > 선수 영입" 절의 3개 카테고리(일반/프리미엄/픽업) 6개 버튼을
    /// 각각 ScoutManager의 대응 메서드에 연결한다. 반환된 카드(1장)를 PlayerCardUI 프리팹으로 생성해
    /// 결과 팝업에 뿌린다. SIGNATURE 이상 등급이면 화면 상단에 강조 문구를 띄운다.
    /// [TASK-KBO-129] 구 roll1Button/roll10Button(단일 재화 혼합 확률)을 폐기했다 - GDD가 10연뽑
    /// 개념을 명시한 카테고리가 없어(픽업의 10/40/80회는 "누적 횟수" 개념이지 "1회 클릭 10연출"이
    /// 아니다) 전부 1회 클릭 = 1장 확정 구조로 통일했다.
    /// </summary>
    public class ScoutUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ScoutManager scoutManager;

        [Header("일반 영입 (라이브 일반 영입권 / 라이브 에픽 영입권)")]
        [SerializeField] private Button liveNormalButton;
        [SerializeField] private Button liveEpicButton;

        [Header("프리미엄 영입 (싸인볼 / 트로피)")]
        [SerializeField] private Button premiumSignatureButton;
        [SerializeField] private Button premiumTitleHolderButton;

        [Header("픽업 영입 (픽업 영입권)")]
        [SerializeField] private Button pickupSignatureButton;
        [SerializeField] private Button pickupTitleHolderButton;

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
            if (liveNormalButton != null) liveNormalButton.onClick.AddListener(() => ExecuteRoll(scoutManager?.RollLiveNormal));
            if (liveEpicButton != null) liveEpicButton.onClick.AddListener(() => ExecuteRoll(scoutManager?.RollLiveEpic));
            if (premiumSignatureButton != null) premiumSignatureButton.onClick.AddListener(() => ExecuteRoll(scoutManager?.RollPremiumSignature));
            if (premiumTitleHolderButton != null) premiumTitleHolderButton.onClick.AddListener(() => ExecuteRoll(scoutManager?.RollPremiumTitleHolder));
            if (pickupSignatureButton != null) pickupSignatureButton.onClick.AddListener(() => ExecuteRoll(scoutManager?.RollPickupSignature));
            if (pickupTitleHolderButton != null) pickupTitleHolderButton.onClick.AddListener(() => ExecuteRoll(scoutManager?.RollPickupTitleHolder));
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

        /// <summary>[TASK-KBO-129] 6개 카테고리 버튼이 공통으로 쓰는 실행 헬퍼. ScoutManager의 각
        /// Roll* 메서드(단일 Player 반환)를 리스트로 감싸 ShowResults()에 위임한다 - 카드 렌더링
        /// 경로를 6개 카테고리 전부 동일하게 유지한다.</summary>
        private void ExecuteRoll(Func<Player> rollMethod)
        {
            if (scoutManager == null || rollMethod == null)
            {
                Debug.LogWarning("[ScoutUIController] ScoutManager가 연결되지 않았습니다.");
                return;
            }

            var player = rollMethod();
            ShowResults(player != null ? new List<Player> { player } : new List<Player>());
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
