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

        private readonly List<PlayerCardUI> spawnedBatterCards = new List<PlayerCardUI>();
        private readonly List<PlayerCardUI> spawnedPitcherCards = new List<PlayerCardUI>();

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

            RefreshSetDeckStatus();
        }

        // [TASK-KBO-038] 15_team_power_policy.md 확정 기준. GameManager.CalculateSynergy()가 쓰는
        // 값과 동일하나, 이 UI는 "어느 구단이 최다인지"(dominantTeam)까지 표시해야 해서 GroupBy 결과를
        // 직접 들고 있어야 한다 - 그래서 그 메서드를 호출하는 대신 여기서 독립적으로 계산한다.
        private const int SetDeckSynergyThreshold = 15;
        private const int SetDeckSynergyBonus = 12;

        /// <summary>
        /// 현재 로스터를 직접 그룹핑해 최다 구단/인원수를 구하고, 상단 게이지·텍스트·글로우 오브젝트를
        /// 갱신한다. RefreshRoster()가 호출될 때마다 함께 갱신되므로 별도로 구독할 이벤트가 없다 -
        /// 로스터가 바뀌는 모든 경로(오토 라인업, 강화/각성 등)가 이미 RefreshRoster()를 거치기 때문이다.
        /// Team.None(무소속) 선수는 집계 대상에서 제외하며, 로스터가 비어 있으면 0/15·미활성으로 표시한다.
        /// </summary>
        private void RefreshSetDeckStatus()
        {
            if (GameManager.Instance == null) return;

            var validPlayers = GameManager.Instance.Roster
                .Where(p => p?.Template != null && p.Template.Team != Team.None)
                .ToList();

            Team dominantTeam = Team.None;
            int maxTeamCount = 0;
            if (validPlayers.Count > 0)
            {
                var dominantGroup = validPlayers
                    .GroupBy(p => p.Template.Team)
                    .OrderByDescending(g => g.Count())
                    .First();
                dominantTeam = dominantGroup.Key;
                maxTeamCount = dominantGroup.Count();
            }

            bool isBonusActive = maxTeamCount >= SetDeckSynergyThreshold;
            var themeColor = isBonusActive ? setDeckActiveColor : setDeckInactiveColor;

            if (setDeckStatusText != null)
            {
                setDeckStatusText.text = isBonusActive
                    ? $"{dominantTeam} 세트덱 활성화: {maxTeamCount}/{SetDeckSynergyThreshold} (+{SetDeckSynergyBonus} OVR)"
                    : $"세트덱 미달성: {maxTeamCount}/{SetDeckSynergyThreshold}";
                setDeckStatusText.color = themeColor;
            }

            if (setDeckGaugeFillImage != null)
            {
                setDeckGaugeFillImage.fillAmount = Mathf.Clamp01((float)maxTeamCount / SetDeckSynergyThreshold);
                setDeckGaugeFillImage.color = themeColor;
            }

            if (setDeckActiveGlowRoot != null)
            {
                setDeckActiveGlowRoot.SetActive(isBonusActive);
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
