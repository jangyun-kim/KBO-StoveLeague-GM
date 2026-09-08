using System;
using System.Collections.Generic;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>구단 1개에 대응하는 버튼 묶음. KBO 10개 구단이 고정 개수이므로(Team.None 제외) 동적
    /// 생성 없이 인스펙터에서 10개를 그대로 연결한다 - LeagueDashboardUIController의 standingsRowTexts와
    /// 같은 스타일.</summary>
    [Serializable]
    public class TeamSelectButtonEntry
    {
        public Team Team;
        public Button Button;
        [Tooltip("구단 로고. 정식 리소스 준비 전까지는 임시 스프라이트/단색 Image로 대체해도 된다.")]
        public Image LogoImage;
        [Tooltip("로고 대신 또는 함께 구단명을 표시할 때 사용(선택).")]
        public Text TeamNameText;
    }

    /// <summary>
    /// 온보딩(선호 구단 선택) 화면. KBO 10개 구단 버튼을 나열하고, 클릭하면
    /// OnboardingManager.CompleteOnboarding(Team)을 호출해 스타터 팩 지급 + 로비 전환을 한 번에
    /// 위임한다. 이 컨트롤러는 어떤 카드를 만들지, 로스터를 어떻게 채울지 전혀 모른다 - 오직
    /// "어떤 구단이 클릭됐는지"만 OnboardingManager에 전달하는 얇은 브릿지다.
    /// </summary>
    public class OnboardingUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private OnboardingManager onboardingManager;

        [Header("Team Buttons (KBO 10개 구단)")]
        [SerializeField] private List<TeamSelectButtonEntry> teamButtons = new List<TeamSelectButtonEntry>();

        private void Awake()
        {
            foreach (var entry in teamButtons)
            {
                if (entry?.Button == null || entry.Team == Team.None) continue;

                if (entry.TeamNameText != null) entry.TeamNameText.text = entry.Team.ToString();

                var team = entry.Team; // 클로저가 마지막 반복값을 공유하지 않도록 지역 변수로 캡처
                entry.Button.onClick.AddListener(() => OnClickTeam(team));
            }
        }

        private void OnClickTeam(Team team)
        {
            if (onboardingManager == null) return;
            onboardingManager.CompleteOnboarding(team);
        }
    }
}
