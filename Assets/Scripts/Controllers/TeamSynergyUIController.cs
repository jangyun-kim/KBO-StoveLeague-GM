using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-053, Phase A] 로비 화면에서 세트덱 시너지(+12 OVR)/치어리더 효과/팬심 상태를 한 줄씩
    /// 요약해 보여주는 어댑터. 세 시스템(GameManager.CalculateSynergy, GameManager.EquippedCheerleader,
    /// GameManager.FanSentiment) 중 무엇도 직접 계산하지 않고, 오직 이미 공개된 값을 읽어 UI 문자열로만
    /// 가공한다. 프리팹 조립/씬 배치는 이번 작업 범위 밖이라 아직 어디에도 연결되어 있지 않다.
    /// </summary>
    public class TeamSynergyUIController : MonoBehaviour
    {
        // MatchRewardManager.FanSentimentPenaltyThreshold(private, 50)와 동일한 값. 그 매니저를
        // 수정하지 않고("기존 매니저 로직/스키마 수정 금지") 동일한 임계값을 읽기 전용으로 표시하기
        // 위해 이 컨트롤러에서 별도로 들고 있다 - 그 값이 바뀌면 이 값도 함께 갱신해야 한다.
        private const int FanSentimentPenaltyThreshold = 50;

        [Header("References")]
        [SerializeField] private Text setDeckText;
        [SerializeField] private Text cheerleaderText;
        [SerializeField] private Text fanSentimentText;

        /// <summary>세트덱/치어리더/팬심 3개 지표를 GameManager에서 읽어와 각 Text에 반영한다.
        /// GameManager.Instance가 없거나(플레이 모드 밖 등) 특정 Text가 인스펙터에 비어 있어도
        /// 안전하게 해당 항목만 건너뛴다.</summary>
        public void RefreshSynergyUI()
        {
            if (GameManager.Instance == null) return;

            var gm = GameManager.Instance;

            RefreshSetDeckText(gm);
            RefreshCheerleaderText(gm);
            RefreshFanSentimentText(gm);
        }

        private void RefreshSetDeckText(GameManager gm)
        {
            if (setDeckText == null) return;

            // GameManager.CalculateTeamOVR()가 favoriteTeam을 CalculateSynergy()에 넘길 때 쓰는 것과
            // 동일한 변환: Team.None(온보딩 이전 등 미지정 상태)이면 null을 넘겨 "로스터 내 최다 구단
            // 기준" 경로를 그대로 태운다.
            string favoriteTeamName = gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null;
            int synergy = GameManager.CalculateSynergy(gm.Roster.ToList(), favoriteTeamName);

            setDeckText.text = synergy > 0 ? "활성화 (+12 OVR)" : "미활성화";
        }

        private void RefreshCheerleaderText(GameManager gm)
        {
            if (cheerleaderText == null) return;

            var cheerleader = gm.EquippedCheerleader;
            if (cheerleader == null)
            {
                cheerleaderText.text = "미장착";
                return;
            }

            int bonusPercent = Mathf.RoundToInt(cheerleader.EconomicBonusRate * 100f);
            cheerleaderText.text = $"장착됨 (관중 수익 {bonusPercent}%)";
        }

        private void RefreshFanSentimentText(GameManager gm)
        {
            if (fanSentimentText == null) return;

            int sentiment = gm.FanSentiment;
            fanSentimentText.text = sentiment < FanSentimentPenaltyThreshold
                ? $"{sentiment} (수익 페널티 적용 중)"
                : sentiment.ToString();
        }
    }
}
