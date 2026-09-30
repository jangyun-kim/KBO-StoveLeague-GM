using System.Collections.Generic;
using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// GameManager.Instance.Roster(28인)를 타자 15명/투수 13명 두 그룹으로 나눠 각각의
    /// GridLayoutGroup 컨테이너에 PlayerCardUI로 렌더링한다. GameActionController.OnRosterChanged를
    /// 구독해 ExecuteAutoRoster()가 로스터를 덮어쓸 때마다 자동으로 다시 그려진다.
    ///
    /// GDD 1절 "구단 세트덱 중심 플레이"를 유저가 체감하도록, 화면 상단에 현재 로스터의 최다 구단
    /// 인원수를 게이지/텍스트로 보여준다. [TASK-KBO-038] 15_team_power_policy.md 확정 기준(15명 이상
    /// -> +12 OVR)에 맞춰, 이 컨트롤러가 직접 로스터를 그룹핑해 최다 구단/인원수를 구한다 - 더 이상
    /// GameManager.CheckSetDeckBonus()(구식 5명/배율 1.15배 기준, 이번 작업에서 삭제됨)에 의존하지 않는다.
    /// </summary>
    public class RosterUIController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("OnRosterChanged 이벤트를 구독할 GameActionController. 비워두면 자동 갱신 없이 수동 RefreshRoster()만 동작한다.")]
        [SerializeField] private GameActionController gameActionController;

        [Header("Batter Roster (15명)")]
        [SerializeField] private Transform batterContainer;
        [Tooltip("타자/투수 공용 카드 프리팹.")]
        [SerializeField] private PlayerCardUI cardPrefab;

        [Header("Pitcher Roster (13명)")]
        [SerializeField] private Transform pitcherContainer;

        [Header("Set Deck Visualization (GDD 1절 / 15_team_power_policy.md)")]
        [Tooltip("예: 'LG 트윈스 세트덱 활성화: 18/15 (+12 OVR)' 또는 '세트덱 미달성: 7/15'.")]
        [SerializeField] private Text setDeckStatusText;
        [Tooltip("Image.Type=Filled로 설정된 게이지 바. fillAmount = 최다 구단 인원 / 15.")]
        [SerializeField] private Image setDeckGaugeFillImage;
        [Tooltip("세트덱 보너스가 활성화됐을 때만 켜지는 배경 빛망울 등 장식용 오브젝트. 비워두면 생략.")]
        [SerializeField] private GameObject setDeckActiveGlowRoot;
        [SerializeField] private Color setDeckActiveColor = new Color(1f, 0.84f, 0f); // 골드
        [SerializeField] private Color setDeckInactiveColor = Color.white;

        [Header("Navigation")]
        [Tooltip("[TASK-KBO-093] 로스터 화면을 닫고 로비로 돌아가는 버튼. ScoutUIController/" +
                 "CheerleaderInventoryUIController의 closeButton과 동일한 관례로 UIManager.ShowScreen()만 " +
                 "호출한다.")]
        [SerializeField] private Button closeButton;

        [Header("Empty State (TASK-KBO-098)")]
        [Tooltip("타자/투수 카드가 단 한 장도 없을 때만 켜지는 안내 텍스트('배치된 선수가 없습니다' 등). " +
                 "비워두면 Debug.LogWarning만 남기고 화면상 안내는 생략한다.")]
        [SerializeField] private Text emptyStateText;
        [SerializeField] private string emptyStateMessage = "배치된 선수가 없습니다.";

        private readonly List<PlayerCardUI> spawnedBatterCards = new List<PlayerCardUI>();
        private readonly List<PlayerCardUI> spawnedPitcherCards = new List<PlayerCardUI>();

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
        }

        private void OnEnable()
        {
            if (gameActionController != null)
            {
                gameActionController.OnRosterChanged += HandleRosterChanged;
            }

            RefreshRoster();
        }

        private void OnDisable()
        {
            if (gameActionController != null)
            {
                gameActionController.OnRosterChanged -= HandleRosterChanged;
            }
        }

        private void HandleRosterChanged() => RefreshRoster();

        /// <summary>GameManager.Instance.Roster를 다시 읽어 타자/투수 두 컨테이너를 새로 그린다.</summary>
        public void RefreshRoster()
        {
            ClearCards(spawnedBatterCards);
            ClearCards(spawnedPitcherCards);

            if (GameManager.Instance == null) return;

            foreach (var player in GameManager.Instance.Roster)
            {
                if (player?.Template == null) continue;

                if (player.Template.IsPitcher)
                {
                    SpawnCard(player, pitcherContainer, spawnedPitcherCards);
                }
                else
                {
                    SpawnCard(player, batterContainer, spawnedBatterCards);
                }
            }

            RefreshEmptyState();
            RefreshSetDeckStatus();
        }

        /// <summary>
        /// [TASK-KBO-098] 타자/투수 카드가 단 한 장도 생성되지 않았으면(RosterManager 미배선, 인벤토리
        /// 비어있음 등) 화면이 흰 배경만 남은 채로 멈춰 보이는 문제를 방지한다. Debug.LogWarning은 항상
        /// 남기고, emptyStateText가 배선되어 있으면 화면에도 안내 문구를 띄운다.
        /// </summary>
        private void RefreshEmptyState()
        {
            bool isEmpty = spawnedBatterCards.Count == 0 && spawnedPitcherCards.Count == 0;

            if (isEmpty)
            {
                Debug.LogWarning("[RosterUIController] 로스터에 표시할 선수가 없습니다 - " +
                    "GameManager.Instance.Roster가 비어 있거나 아직 편성되지 않았을 수 있습니다.");
            }

            if (emptyStateText != null)
            {
                emptyStateText.text = emptyStateMessage;
                emptyStateText.gameObject.SetActive(isEmpty);
            }
        }

        /// <summary>
        /// [TASK-KBO-172 전면 개편] 27인 세트덱 스코어(GameManager.EvaluateSetDeck)로 상단 게이지·텍스트·글로우를
        /// 갱신한다. 게이지는 최종 목표(200P) 대비 진행률, 텍스트는 기준 구단/스코어/다음 목표 단계, 글로우는
        /// 1차 목표(최소 목표 스코어 150P) 달성 시 켠다 - 이전의 "동일 구단 15명 이상 +12" 게이지는 폐기됐다.
        /// RefreshRoster()가 호출될 때마다 함께 갱신되므로 별도로 구독할 이벤트가 없다.
        /// </summary>
        private void RefreshSetDeckStatus()
        {
            if (GameManager.Instance == null) return;

            var gm = GameManager.Instance;
            string favoriteTeamName = gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null;
            var setDeck = GameManager.EvaluateSetDeck(gm.Roster.ToList(), favoriteTeamName, gm.SetDeckSelection);

            bool isGoalMet = setDeck.IsMinimumGoalMet;
            var themeColor = isGoalMet ? setDeckActiveColor : setDeckInactiveColor;

            if (setDeckStatusText != null)
            {
                setDeckStatusText.text = $"{setDeck.DeckTeam} {SetDeckUIText.Summary(setDeck)}";
                setDeckStatusText.color = themeColor;
            }

            if (setDeckGaugeFillImage != null)
            {
                setDeckGaugeFillImage.fillAmount = Mathf.Clamp01((float)setDeck.Score / SetDeckBuffTable.FinalGoalScore);
                setDeckGaugeFillImage.color = themeColor;
            }

            if (setDeckActiveGlowRoot != null)
            {
                setDeckActiveGlowRoot.SetActive(isGoalMet);
            }
        }

        private void SpawnCard(Player player, Transform container, List<PlayerCardUI> tracking)
        {
            if (cardPrefab == null || container == null) return;

            var card = CardPoolManager.Instance != null
                ? CardPoolManager.Instance.Get(cardPrefab, container)
                : Instantiate(cardPrefab, container);

            card.Setup(player);
            tracking.Add(card);
        }

        private void ClearCards(List<PlayerCardUI> tracking)
        {
            foreach (var card in tracking)
            {
                if (card == null) continue;

                if (CardPoolManager.Instance != null) CardPoolManager.Instance.Release(card);
                else Destroy(card.gameObject);
            }
            tracking.Clear();
        }
    }
}
