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
    ///
    /// [TASK-KBO-140, 사실 정정 + 재정의] TASK-129의 "카테고리당 1회 고정"(위 문단) 판단과 이번 명령서
    /// ("치어리더 영입도 선수 영입 탭과 동일하게 [1회]-[10회] 버튼 구조")는 정면으로 충돌한다 - 다만
    /// `CheerleaderGachaService.RollX(int count)`는 애초부터 count 매개변수를 받아 한 번에 N장을 뽑도록
    /// 설계돼 있었다(`SetupCheerleaderUI.Roll10xGacha()`의 `RollLive(10)` 호출이 이미 증명, 전수 확인)
    /// - `ScoutUIController.ExecuteMultiRoll()`처럼 단일 뽑기를 UI 레이어에서 반복 호출하는 우회가
    /// 필요 없이, `ExecuteRoll()`에 count 인자만 추가하면 된다. 확률/재화 차감 로직은 여전히 서비스가
    /// 전담하므로 이번 변경으로 새로 추가되는 계산은 없다.
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
        [SerializeField] private Button liveButton10;
        [SerializeField] private Button limitedButton;
        [SerializeField] private Button limitedButton10;

        [Header("픽업/프리미엄 영입 (스타 응원봉 / 레전드 응원봉)")]
        [SerializeField] private Button iconButton;
        [SerializeField] private Button iconButton10;
        [SerializeField] private Button legendButton;
        [SerializeField] private Button legendButton10;

        [SerializeField] private Button closeButton;
        [Tooltip("뽑기 결과를 나열할 텍스트. 여러 줄(다중 발급)은 줄바꿈으로 연결된다.")]
        [SerializeField] private Text resultLogText;

        /// <summary>버튼 리스너는 Awake()에서 한 번만 등록한다 - CheerleaderInventoryUIController/
        /// LeagueDashboardUIController(TASK-KBO-060)와 동일한 이유로, OnEnable에 등록하면 화면
        /// 전환마다(UIManager.ShowScreen()의 SetActive(true)) 리스너가 중복 등록된다.</summary>
        private void Awake()
        {
            if (liveButton != null) liveButton.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollLive, 1));
            if (limitedButton != null) limitedButton.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollLimited, 1));
            if (iconButton != null) iconButton.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollIcon, 1));
            if (legendButton != null) legendButton.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollLegend, 1));

            // [TASK-KBO-140] CheerleaderGachaService.RollX(int count)가 이미 count를 받으므로 그대로
            // 10을 넘기기만 하면 된다(ScoutUIController.ExecuteMultiRoll()류의 반복 호출 불필요).
            if (liveButton10 != null) liveButton10.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollLive, 10));
            if (limitedButton10 != null) limitedButton10.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollLimited, 10));
            if (iconButton10 != null) iconButton10.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollIcon, 10));
            if (legendButton10 != null) legendButton10.onClick.AddListener(() => ExecuteRoll(CheerleaderGachaService.RollLegend, 10));

            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
        }

        [Header("Banner Info (TASK-KBO-177 - 상품별 필요 재화 + 등장 확률 요약)")]
        [SerializeField] private Text liveInfoText;
        [SerializeField] private Text limitedInfoText;
        [SerializeField] private Text iconInfoText;
        [SerializeField] private Text legendInfoText;

        private void OnEnable()
        {
            RefreshUI();
        }

        private void RefreshUI()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            // [TASK-KBO-177] 2x2 재화 배지용 2줄 표기(이름 / 수량).
            if (liveCheerStickText != null) liveCheerStickText.text = $"라이브 응원봉\n{gm.LiveCheerStick:N0}";
            if (limitedCheerStickText != null) limitedCheerStickText.text = $"한정 응원봉\n{gm.LimitedCheerStick:N0}";
            if (starCheerStickText != null) starCheerStickText.text = $"스타 응원봉\n{gm.StarCheerStick:N0}";
            if (legendCheerStickText != null) legendCheerStickText.text = $"레전드 응원봉\n{gm.LegendCheerStick:N0}";

            SetInfo(liveInfoText, "라이브 응원봉", gm.LiveCheerStick, CheerleaderGachaService.LiveRateSummary);
            SetInfo(limitedInfoText, "한정 응원봉", gm.LimitedCheerStick, CheerleaderGachaService.LimitedRateSummary);
            SetInfo(iconInfoText, "스타 응원봉", gm.StarCheerStick, CheerleaderGachaService.IconRateSummary);
            SetInfo(legendInfoText, "레전드 응원봉", gm.LegendCheerStick, CheerleaderGachaService.LegendRateSummary);
        }

        private static void SetInfo(Text target, string currency, int owned, string rates)
        {
            if (target == null) return;
            int cost = CheerleaderGachaService.CostPerRoll;
            target.text = $"1회 {currency} {cost} · 10회 {cost * 10} (보유 {owned:N0}, {owned / cost:N0}회 가능)\n확률: {rates}";
        }

        /// <summary>
        /// [TASK-KBO-066/129, 명령서 9항] resultLogText는 매 클릭마다 이전 내용을 지우고 새로 쓴다
        /// (Clear & Write 방식) - 클릭할 때마다 로그가 무한히 누적되지 않고, 화면에는 항상 "가장 최근
        /// 뽑기 결과"만 명확하게 남는다. 8개 버튼(카테고리 4 x 1회/10회, TASK-KBO-140) 전부가 이 헬퍼
        /// 하나를 공유하며 count만 다르게 넘긴다.
        /// </summary>
        private void ExecuteRoll(Func<int, List<Cheerleader>> rollMethod, int count)
        {
            if (rollMethod == null) return;

            var results = rollMethod(count);

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
                string affiliation = string.IsNullOrEmpty(c.AffiliationLabel) ? "" : $" · {c.AffiliationLabel}"; // [TASK-KBO-177]
                builder.Append($"[{c.Grade}] {c.Name}{affiliation}  (컨디션 +{c.ConditionBuff} / 클러치 x{c.ClutchMultiplier:F2} / 수익 x{c.EconomicBonusRate:F2} / 팬심 +{c.SentimentDefense})");
            }

            return builder.ToString();
        }
    }
}
