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
    /// [TASK-KBO-066] 치어리더 가챠 상점 화면. CheerleaderGachaService.RollGacha()를 호출해 그 결과를
    /// UI 텍스트로 보여주는 순수 UI 백엔드 어댑터 - 확률/재화 차감/카탈로그 조회 로직은 전혀 갖지
    /// 않고 서비스가 반환한 결과만 그린다(PlayerCardUI/CheerleaderSlotUI와 동일한 "표시만 담당" 관례).
    /// </summary>
    public class CheerleaderShopUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Text premiumCurrencyText;
        [SerializeField] private Button roll1xButton;
        [SerializeField] private Button roll10xButton;
        [SerializeField] private Button closeButton;
        [Tooltip("뽑기 결과를 나열할 텍스트. 여러 줄(10연뽑)은 줄바꿈으로 연결된다.")]
        [SerializeField] private Text resultLogText;

        /// <summary>버튼 리스너는 Awake()에서 한 번만 등록한다 - CheerleaderInventoryUIController/
        /// LeagueDashboardUIController(TASK-KBO-060)와 동일한 이유로, OnEnable에 등록하면 화면
        /// 전환마다(UIManager.ShowScreen()의 SetActive(true)) 리스너가 중복 등록된다.</summary>
        private void Awake()
        {
            if (roll1xButton != null) roll1xButton.onClick.AddListener(() => ExecuteRoll(1));
            if (roll10xButton != null) roll10xButton.onClick.AddListener(() => ExecuteRoll(10));
            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
        }

        private void OnEnable()
        {
            RefreshUI();
        }

        private void RefreshUI()
        {
            if (premiumCurrencyText != null && GameManager.Instance != null)
            {
                premiumCurrencyText.text = $"보유 프리미엄 재화: {GameManager.Instance.PremiumCurrency}";
            }
        }

        /// <summary>
        /// [TASK-KBO-066, 명령서 9항] resultLogText는 매 클릭마다 이전 내용을 지우고 새로 쓴다
        /// (Clear & Write 방식, 명령서 권장안 채택) - 클릭할 때마다 로그가 무한히 누적되지 않고,
        /// 화면에는 항상 "가장 최근 뽑기 결과"만 명확하게 남는다. 여러 판의 이력을 남기고 싶다면
        /// 별도의 스크롤 로그 UI가 필요하며, 그건 이번 작업(Phase A, 백엔드 어댑터) 범위 밖이다.
        /// </summary>
        private void ExecuteRoll(int count)
        {
            var results = CheerleaderGachaService.RollGacha(count);

            if (resultLogText != null)
            {
                resultLogText.text = results.Count == 0
                    ? "재화가 부족합니다."
                    : BuildResultLog(results);
            }

            RefreshUI();
        }

        private static string BuildResultLog(List<Cheerleader> results)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < results.Count; i++)
            {
                if (i > 0) builder.Append('\n');
                builder.Append($"획득: [{results[i].Grade}] {results[i].Name}");
            }

            return builder.ToString();
        }
    }
}
