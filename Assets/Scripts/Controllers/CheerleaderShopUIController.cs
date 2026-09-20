using System;
using System.Collections.Generic;
using System.Text;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-066/129] 치어리더 영입 화면. GDD "뽑기(가챠) > 치어리더 영입" 절의 4개 카테고리
    /// (일반 > 라이브/한정, 픽업·프리미엄 > 아이콘/레전드) 버튼을 각각 CheerleaderGachaService의
    /// 대응 메서드에 연결해 그 결과를 UI 텍스트로 보여주는 순수 UI 백엔드 어댑터 - 확률/재화 차감/
    /// 카탈로그 조회 로직은 전혀 갖지 않고 서비스가 반환한 결과만 그린다(PlayerCardUI/
    /// CheerleaderSlotUI와 동일한 "표시만 담당" 관례).
    /// [TASK-KBO-129] 구 roll1xButton/roll10xButton(단일 CheerStick 소모, 5단계 혼합 확률)을 폐기했다.
    /// </summary>
    public class CheerleaderShopUIController : MonoBehaviour
    {
        [Header("Currency Display")]
        [SerializeField] private Text liveCheerStickText;
        [SerializeField] private Text limitedCheerStickText;
        [SerializeField] private Text starCheerStickText;
        [SerializeField] private Text legendCheerStickText;

        [Header("일반 영입 (라이브 응원봉 / 한정 응원봉)")]
        [SerializeField] private Button liveButton;
        [SerializeField] private Button limitedButton;

        [Header("픽업/프리미엄 영입 (스타 응원봉 / 레전드 응원봉)")]
        [SerializeField] private Button iconButton;
        [SerializeField] private Button legendButton;

        [SerializeField] private Button closeButton;
        [Tooltip("뽑기 결과를 나열할 텍스트. 여러 줄(다중 발급)은 줄바꿈으로 연결된다.")]
        [SerializeField] private Text resultLogText;

        /// <summary>버튼 리스너는 Awake()에서 한 번만 등록한다 - CheerleaderInventoryUIController/
        /// LeagueDashboardUIController(TASK-KBO-060)와 동일한 이유로, OnEnable에 등록하면 화면
        /// 전환마다(UIManager.ShowScreen()의 SetActive(true)) 리스너가 중복 등록된다.</summary>
        private void Awake()
        {
            if (liveButton != null) liveButton.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollLive));
            if (limitedButton != null) limitedButton.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollLimited));
            if (iconButton != null) iconButton.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollIcon));
            if (legendButton != null) legendButton.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollLegend));
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
        }

        private void OnEnable()
        {
            RefreshUI();
        }

        private void RefreshUI()
        {
            if (GameManager.Instance == null) return;

            if (liveCheerStickText != null) liveCheerStickText.text = $"라이브 응원봉: {GameManager.Instance.LiveCheerStick}";
            if (limitedCheerStickText != null) limitedCheerStickText.text = $"한정 응원봉: {GameManager.Instance.LimitedCheerStick}";
            if (starCheerStickText != null) starCheerStickText.text = $"스타 응원봉: {GameManager.Instance.StarCheerStick}";
            if (legendCheerStickText != null) legendCheerStickText.text = $"레전드 응원봉: {GameManager.Instance.LegendCheerStick}";
        }

        /// <summary>
        /// [TASK-KBO-066/129, 명령서 9항] resultLogText는 매 클릭마다 이전 내용을 지우고 새로 쓴다
        /// (Clear & Write 방식) - 클릭할 때마다 로그가 무한히 누적되지 않고, 화면에는 항상 "가장 최근
        /// 뽑기 결과"만 명확하게 남는다. 4개 카테고리 버튼이 전부 이 헬퍼 하나를 공유한다(count=1
        /// 고정 - TASK-KBO-129가 10연뽑 개념을 폐지했다, ScoutUIController와 동일한 사유).
        /// </summary>
        private void ExecuteRoll(Func<int, List<Cheerleader>> rollMethod)
        {
            if (rollMethod == null) return;

            var results = rollMethod(1);

            if (resultLogText != null)
            {
                resultLogText.text = results.Count == 0
                    ? "재화가 부족합니다."
                    : BuildResultLog(results);
            }

            RefreshUI();
        }

        /// <summary>[TASK-KBO-070] 경제/클러치/팬심방어 3개 스탯을 함께 표시해, 유저가 뽑기 결과의
        /// 가치를 한 줄만 보고도 파악할 수 있게 한다(명령서 6항).</summary>
        private static string BuildResultLog(List<Cheerleader> results)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0) builder.Append('\n');

                var c = results[i];
                builder.Append($"획득: [{c.Grade}] {c.Name} (경제: {c.EconomicBonusRate:F2}x / 클러치: {c.ClutchMultiplier:F2}x / 팬심방어: {c.SentimentDefense})");
            }

            return builder.ToString();
        }
    }
}
