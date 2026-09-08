using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// 카드 한 장의 시각 요소를 담당하는 순수 표시 컴포넌트. Setup(Player)을 호출하면 이름/구단/포지션/
    /// 최종 OVR/등급 색상/성급 별을 UGUI 요소에 매핑한다. 시뮬레이션·저장 로직은 전혀 갖지 않는다.
    /// </summary>
    public class PlayerCardUI : MonoBehaviour
    {
        [Header("Text")]
        [SerializeField] private Text nameText;
        [SerializeField] private Text teamText;
        [SerializeField] private Text positionText;
        [SerializeField] private Text ovrText;

        [Header("Grade Visual")]
        [Tooltip("카드 배경 또는 테두리 이미지. 등급(StarType)에 따라 색이 바뀐다.")]
        [SerializeField] private Image frameImage;
        [Tooltip("1~6성을 표시할 별 아이콘 6개. 인덱스 0=1성 ... 5=6성.")]
        [SerializeField] private Image[] starIcons;
        [SerializeField] private Color inactiveStarColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        [Header("Selection Feedback (재료 선택 UI 등에서 사용)")]
        [Tooltip("선택 시 카드를 어둡게 덮는 Dim 오버레이. 평소 비활성 상태로 둔다.")]
        [SerializeField] private GameObject selectedOverlay;
        [Tooltip("선택 시 표시할 체크마크 아이콘. 평소 비활성 상태로 둔다.")]
        [SerializeField] private GameObject checkmarkIcon;

        public Player BoundPlayer { get; private set; }
        public bool IsSelected { get; private set; }

        /// <summary>카드에 표시할 선수를 지정한다. player나 Template이 없으면 카드를 비워 표시한다.
        /// 선택 표시는 항상 false로 초기화되므로, 필요하면 Setup() 이후에 SetSelected()를 다시 호출한다.</summary>
        public void Setup(Player player)
        {
            BoundPlayer = player;
            SetSelected(false);

            if (player?.Template == null)
            {
                Clear();
                return;
            }

            if (nameText != null) nameText.text = player.Template.PlayerName;
            if (teamText != null) teamText.text = player.Template.Team.ToString();
            if (positionText != null) positionText.text = DescribePosition(player.Template);
            if (ovrText != null) ovrText.text = player.CalculateOVR(false).ToString();

            var gradeColor = GetStarTypeColor(player.CurrentStarType, player.Template.Team);
            if (frameImage != null) frameImage.color = gradeColor;

            SetupStars(player.StarLevel, gradeColor);
        }

        /// <summary>빈 카드로 되돌린다 (풀링/재사용 시 사용).</summary>
        public void Clear()
        {
            BoundPlayer = null;
            if (nameText != null) nameText.text = "";
            if (teamText != null) teamText.text = "";
            if (positionText != null) positionText.text = "";
            if (ovrText != null) ovrText.text = "";
            if (frameImage != null) frameImage.color = Color.white;
            SetupStars(0, Color.white);
            SetSelected(false);
        }

        /// <summary>재료 선택 UI 등에서 이 카드가 선택됐는지를 시각적으로 표시한다(Dim 오버레이 + 체크마크).</summary>
        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectedOverlay != null) selectedOverlay.SetActive(selected);
            if (checkmarkIcon != null) checkmarkIcon.SetActive(selected);
        }

        private void SetupStars(int starLevel, Color activeColor)
        {
            if (starIcons == null) return;

            for (int i = 0; i < starIcons.Length; i++)
            {
                if (starIcons[i] == null) continue;
                starIcons[i].color = i < starLevel ? activeColor : inactiveStarColor;
            }
        }

        private static string DescribePosition(PlayerTemplate template)
        {
            return template.IsPitcher ? template.PitcherRole.ToString() : template.BatterPosition.ToString();
        }

        /// <summary>
        /// GDD 2절의 등급별 색상 규칙을 실제 UI 색으로 변환한다. Player.CurrentStarType이 이미
        /// (LIVE_NORMAL/EPIC=NORMAL, ALLSTAR=PURPLE, TITLE_HOLDER=SILVER, GOLDEN_GLOVE=GOLD,
        /// SIGNATURE=PLATINUM, DYNASTY=TEAM_COLOR) 등급→색상 매핑을 담고 있으므로 여기서는 그 값을
        /// 그대로 소비하기만 한다. DYNASTY(왕조)만 구단색(GetTeamColor)으로 별도 분기한다.
        /// </summary>
        private static Color GetStarTypeColor(StarType starType, Team team) => starType switch
        {
            StarType.NORMAL => new Color(0.75f, 0.75f, 0.75f),   // 라이브 일반/에픽 - 무채색
            StarType.PURPLE => new Color(0.58f, 0.29f, 0.93f),   // 올스타
            StarType.SILVER => new Color(0.78f, 0.80f, 0.84f),   // 타이틀 홀더
            StarType.GOLD => new Color(1f, 0.84f, 0f),            // 골든 글러브
            StarType.PLATINUM => new Color(0.90f, 0.92f, 0.95f), // 시그니처(플래티넘)
            StarType.TEAM_COLOR => GetTeamColor(team),            // 왕조
            _ => Color.white
        };

        /// <summary>KBO 10개 구단의 대표색(근사치). 실제 브랜드 가이드가 확정되면 이 표만 갱신하면 된다.</summary>
        private static Color GetTeamColor(Team team) => team switch
        {
            Team.Doosan => new Color(0.05f, 0.16f, 0.35f),
            Team.LG => new Color(0.77f, 0.02f, 0.15f),
            Team.KT => new Color(0.05f, 0.05f, 0.05f),
            Team.SSG => new Color(0.85f, 0.10f, 0.15f),
            Team.NC => new Color(0.10f, 0.11f, 0.35f),
            Team.Kiwoom => new Color(0.50f, 0.09f, 0.16f),
            Team.KIA => new Color(0.65f, 0.0f, 0.05f),
            Team.Samsung => new Color(0.0f, 0.32f, 0.70f),
            Team.Lotte => new Color(0.85f, 0.10f, 0.15f),
            Team.Hanwha => new Color(1f, 0.45f, 0.0f),
            _ => Color.gray
        };
    }
}
