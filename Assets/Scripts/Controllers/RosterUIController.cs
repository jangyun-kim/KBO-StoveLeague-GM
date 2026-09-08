using System.Collections.Generic;
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
    /// 인원수를 게이지/텍스트로 보여주고, GameManager.CheckSetDeckBonus()가 활성화로 판정하면
    /// 게이지/텍스트 색상과 별도 글로우 오브젝트로 시각적 피드백을 준다.
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

        [Header("Set Deck Visualization (GDD 1절)")]
        [Tooltip("예: 'LG 트윈스 세트덱 활성화: 18/28'. 최다 구단이 없으면(로스터가 비어 있으면) 표시를 생략한다.")]
        [SerializeField] private Text setDeckStatusText;
        [Tooltip("Image.Type=Filled로 설정된 게이지 바. fillAmount = 최다 구단 인원 / 28.")]
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

        /// <summary>
        /// GameManager.CheckSetDeckBonus()로 현재 로스터의 최다 구단/인원수/활성화 여부를 다시 읽어
        /// 상단 게이지·텍스트·글로우 오브젝트를 갱신한다. RefreshRoster()가 호출될 때마다 함께 갱신되므로
        /// 별도로 구독할 이벤트가 없다 - 로스터가 바뀌는 모든 경로(오토 라인업, 강화/각성 등)가 이미
        /// RefreshRoster()를 거치기 때문이다.
        /// </summary>
        private void RefreshSetDeckStatus()
        {
            if (GameManager.Instance == null) return;

            var countByTeam = GameManager.Instance.CheckSetDeckBonus(out Team dominantTeam, out bool isBonusActive, out float bonusMultiplier);
            int dominantCount = dominantTeam != Team.None && countByTeam.TryGetValue(dominantTeam, out var count) ? count : 0;

            var themeColor = isBonusActive ? setDeckActiveColor : setDeckInactiveColor;

            if (setDeckStatusText != null)
            {
                string teamLabel = dominantTeam != Team.None ? dominantTeam.ToString() : "없음";
                setDeckStatusText.text = isBonusActive
                    ? $"{teamLabel} 세트덱 활성화! {dominantCount}/{GameManager.RequiredRosterSize} (x{bonusMultiplier:F2})"
                    : $"{teamLabel} {dominantCount}/{GameManager.RequiredRosterSize} (세트덱 미활성)";
                setDeckStatusText.color = themeColor;
            }

            if (setDeckGaugeFillImage != null)
            {
                setDeckGaugeFillImage.fillAmount = Mathf.Clamp01((float)dominantCount / GameManager.RequiredRosterSize);
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
