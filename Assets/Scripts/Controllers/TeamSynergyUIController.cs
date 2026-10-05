using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-053, Phase A] 로비 화면에서 세트덱 시너지(일반 +12 및/또는 왕조 +15, TASK-KBO-166)/
    /// 치어리더 효과/팬심 상태를 한 줄씩 요약해 보여주는 어댑터. 세 시스템(GameManager.CalculateSynergy,
    /// GameManager.EquippedCheerleader, GameManager.FanSentiment) 중 무엇도 직접 계산하지 않고, 오직
    /// 이미 공개된 값을 읽어 UI 문자열로만 가공한다. 프리팹 조립/씬 배치는 이번 작업 범위 밖이라
    /// 아직 어디에도 연결되어 있지 않다.
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

            // GameManager.CalculateTeamOVR()가 favoriteTeam을 CalculateSynergy()에 넘길 때 쓰는 것과
            // 동일한 변환: Team.None(온보딩 이전 등 미지정 상태)이면 null을 넘겨 "로스터 내 최다 구단
            // 기준" 경로를 그대로 태운다.
            string favoriteTeamName = gm.FavoriteTeam != Team.None ? gm.FavoriteTeam.ToString() : null;
            var setDeck = GameManager.EvaluateSetDeck(gm.Roster.ToList(), favoriteTeamName, gm.SetDeckSelection);

            RefreshSetDeckText(setDeck);
            RefreshCheerleaderText(gm, setDeck);
            RefreshFanSentimentText(gm);
        }

        private void RefreshSetDeckText(SetDeckResult setDeck)
        {
            if (setDeckText == null) return;

            // [TASK-KBO-172] 27인 세트덱 스코어 체계 - "현재 스코어 / 모든 능력치 누적 / 다음 목표 단계"를 한 줄로.
            setDeckText.text = SetDeckUIText.Summary(setDeck);
        }

        private void RefreshCheerleaderText(GameManager gm, SetDeckResult setDeck)
        {
            if (cheerleaderText == null) return;

            // [TASK-KBO-180] 6인 역할 편성 요약 - 편성 인원 / 구단 시너지 발동 인원.
            int filled = CheerSquad.Filled(gm.CheerSquadSlots).Count();
            int active = CheerSquad.Filled(gm.CheerSquadSlots).Count(c => CheerleaderSynergy.IsActive(c, setDeck.DeckTeam));
            var cheerleader = gm.EquippedCheerleader;
            if (filled == 0)
            {
                cheerleaderText.text = "편성 0/6";
                return;
            }
            if (cheerleader == null)
            {
                cheerleaderText.text = $"편성 {filled}/6 · 시너지 {active}명 (응원단장 공석)";
                return;
            }

            int bonusPercent = Mathf.RoundToInt(cheerleader.EconomicBonusRate * 100f);
            string text = $"편성 {filled}/6 · 시너지 {active}명 · 응원단장 {cheerleader.DisplayName} (관중 수익 {bonusPercent}%)";

            // [TASK-KBO-175] 구단 시너지 - 치어리더 소속 구단(활동 기간 기준)이 세트덱 기준 구단과 같을 때만 경기 버프 발동.
            if (cheerleader.Team != Team.None)
            {
                text += CheerleaderSynergy.IsActive(cheerleader, setDeck.DeckTeam)
                    ? $" · {cheerleader.AffiliationLabel} 시너지 발동"
                    : $" · 시너지 미발동({cheerleader.AffiliationLabel} ≠ 세트덱 {setDeck.DeckTeam})";
            }
            cheerleaderText.text = text;
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
