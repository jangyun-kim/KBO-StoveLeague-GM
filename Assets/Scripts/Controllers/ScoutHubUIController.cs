using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-129] "스카우트" 화면의 탭 스위처. GDD "UI 흐름 > 스카우트" 절이 "선수 영입"/"치어리더
    /// 영입"을 하나의 스카우트 화면 아래 두 섹션으로 명시하고 있어, 기존에 완전히 분리돼 있던
    /// ScreenType.Scout(ScoutUIController)/ScreenType.CheerleaderShop(CheerleaderShopUIController)
    /// 두 화면을 이 컨트롤러가 감싸는 하나의 화면(ScoutHubPanel)으로 합친다.
    ///
    /// ScoutUIController/CheerleaderShopUIController는 각자의 내부 로직(재화 소모/카드 발급/결과
    /// 팝업)을 전혀 수정하지 않고 그대로 재사용한다 - 이 컨트롤러는 두 섹션 GameObject의 SetActive만
    /// 토글하는 얇은 스위처다.
    ///
    /// [TASK-KBO-138, 사실 정정] 명령서 3항이 보고한 "탭 전환 시 이전 탭이 안 숨겨지고 겹침" 증상을
    /// 재확인한 결과, `ShowPlayerSection()`/`ShowCheerleaderSection()` 원문은 이미 두 섹션 모두에
    /// `SetActive(true)`/`SetActive(false)`를 항상 짝지어 호출하고 있었다(무결함, C# 로직 무수정) -
    /// 두 메서드 다 활성화할 패널과 비활성화할 패널을 매번 명시적으로 지정하므로, 이 스크립트만으로는
    /// "겹침"이 재현될 수 없는 구조다. 가장 유력한 원인은 `playerSection`/`cheerleaderSection` 직렬화
    /// 참조가 씬 재조립 과정에서 서로 뒤바뀌거나 동일 오브젝트로 잘못 연결되는 것이라 판단해, `Awake()`
    /// 에 그런 오설정을 즉시 드러내는 방어 로그를 추가했다(아래) - 명령서 6항 "보완" 취지를 실제 코드
    /// 결함이 없는 상태에서도 진단 가능하게 만드는 방향으로 반영했다.
    ///
    /// [TASK-KBO-142, 명령서 4항] "정말 제대로 된 놈을 끄고 켜는지" 실시간으로 증명하기 위해
    /// `ShowPlayerSection()`/`ShowCheerleaderSection()` 각각에 `LogSectionToggle()`을 추가했다 -
    /// 매 탭 전환마다 활성화/비활성화되는 두 GameObject의 이름과 식별용 해시코드를 콘솔에 남기고,
    /// 두 대상이 우연히 같은 값(=같은 오브젝트)이라면 즉시 에러 로그로 강조한다. 이 로직 자체는
    /// TASK-138부터 무결함이었으므로(위 문단) 여기서도 SetActive 호출 순서/조건은 손대지 않았다 -
    /// 순수하게 진단 정보만 추가했다(명령서 5항).
    ///
    /// [TASK-KBO-143, 사실 정정] 위 문단이 도입한 `GetInstanceID()` 호출이 유니티 6 환경에서
    /// `CS0619(obsolete)` 컴파일 에러를 일으켜 이 파일 전체의 컴파일이 막혔다 - `LogSectionToggle()`의
    /// 식별자 출력을 `GetHashCode()`로 교체해 해소했다(명령서 4항). 진단 목적(오바인딩 시 두 값이
    /// 같은지 비교)은 `GetHashCode()`로도 동일하게 달성되므로 기능적 퇴보는 없다.
    /// </summary>
    public class ScoutHubUIController : MonoBehaviour
    {
        [Header("Tabs")]
        [SerializeField] private Button playerTabButton;
        [SerializeField] private Button cheerleaderTabButton;

        [Header("Sections")]
        [Tooltip("ScoutUIController가 붙은 선수 영입 섹션 루트.")]
        [SerializeField] private GameObject playerSection;
        [Tooltip("CheerleaderShopUIController가 붙은 치어리더 영입 섹션 루트.")]
        [SerializeField] private GameObject cheerleaderSection;

        [Header("Tab Style (TASK-KBO-178 - 활성 탭 네이비 + 흰 글씨, 비활성 흰 바탕 + 회색 글씨)")]
        [SerializeField] private Color activeTabColor = new Color(0.06f, 0.17f, 0.45f, 1f);
        [SerializeField] private Color inactiveTabColor = new Color(0.94f, 0.95f, 0.97f, 1f);
        [SerializeField] private Color activeTabTextColor = Color.white;
        [SerializeField] private Color inactiveTabTextColor = new Color(0.5f, 0.53f, 0.6f, 1f);

        private void Awake()
        {
            // [TASK-KBO-138] playerSection/cheerleaderSection이 실수로 같은 오브젝트에 연결되면(에디터
            // 재조립 과정에서 흔히 생길 수 있는 오설정) 두 SetActive 호출이 결국 같은 대상을 놓고
            // 다투게 되어 "탭이 안 바뀌거나 겹쳐 보인다"는 증상으로 나타난다 - 이 경우를 즉시 경고한다.
            if (playerSection != null && playerSection == cheerleaderSection)
            {
                Debug.LogWarning("[ScoutHubUIController] playerSection과 cheerleaderSection이 같은 " +
                    "오브젝트로 연결되어 있습니다. 'KBO Manager/Setup/Auto-Connect Scout Hub'를 다시 " +
                    "실행해 바인딩을 복구하십시오.");
            }

            if (playerTabButton != null) playerTabButton.onClick.AddListener(ShowPlayerSection);
            if (cheerleaderTabButton != null) cheerleaderTabButton.onClick.AddListener(ShowCheerleaderSection);
        }

        /// <summary>화면이 켜질 때마다(UIManager.ShowScreen()의 SetActive(true)) 항상 선수 영입 탭을
        /// 기본으로 보여준다 - 이전에 어느 탭을 보고 있었는지와 무관하게 진입 지점을 고정한다.</summary>
        private void OnEnable()
        {
            ShowPlayerSection();
        }

        public void ShowPlayerSection()
        {
            // [TASK-KBO-142, 명령서 4/6항] "정말 제대로 된 놈을 끄고 켜는지" 매 전환마다 InstanceID와
            // 함께 콘솔에 남긴다 - playerSection/cheerleaderSection이 씬 오바인딩으로 엉뚱한 오브젝트를
            // 가리키고 있다면 이 로그에서 즉시 드러난다(사용자가 직접 대조 가능).
            LogSectionToggle(nameof(ShowPlayerSection), activate: playerSection, deactivate: cheerleaderSection);

            if (playerSection != null) playerSection.SetActive(true);
            if (cheerleaderSection != null) cheerleaderSection.SetActive(false);
            StyleTabs(playerActive: true);
        }

        public void ShowCheerleaderSection()
        {
            LogSectionToggle(nameof(ShowCheerleaderSection), activate: cheerleaderSection, deactivate: playerSection);

            if (playerSection != null) playerSection.SetActive(false);
            if (cheerleaderSection != null) cheerleaderSection.SetActive(true);
            StyleTabs(playerActive: false);
        }

        /// <summary>[TASK-KBO-178] 레퍼런스(선수 스카우트 탭 바)처럼 현재 탭만 네이비로 강조한다.</summary>
        private void StyleTabs(bool playerActive)
        {
            StyleTab(playerTabButton, playerActive);
            StyleTab(cheerleaderTabButton, !playerActive);
        }

        private void StyleTab(Button tab, bool active)
        {
            if (tab == null) return;
            if (tab.targetGraphic != null) tab.targetGraphic.color = active ? activeTabColor : inactiveTabColor;
            var label = tab.GetComponentInChildren<Text>(true);
            if (label != null)
            {
                label.color = active ? activeTabTextColor : inactiveTabTextColor;
                label.fontStyle = active ? FontStyle.Bold : FontStyle.Normal;
            }
        }

        /// <summary>[TASK-KBO-142, 명령서 4/6항] 활성화/비활성화되는 두 GameObject의 이름과
        /// HashCode를 콘솔에 남긴다. `activate`/`deactivate`가 참조 동일(같은 HashCode)하면
        /// "같은 오브젝트를 끄고 켜려 한다"는 오바인딩 신호이므로 강조 경고를 추가로 남긴다.
        /// [TASK-KBO-143] `Object.GetInstanceID()`가 유니티 6 환경에서 CS0619(obsolete) 컴파일
        /// 에러를 일으켜, 동일한 식별 목적의 `GetHashCode()`로 교체했다(명령서 4항).</summary>
        private void LogSectionToggle(string methodName, GameObject activate, GameObject deactivate)
        {
            string activateInfo = activate != null
                ? $"{activate.name}(HashCode {activate.GetHashCode()})"
                : "null";
            string deactivateInfo = deactivate != null
                ? $"{deactivate.name}(HashCode {deactivate.GetHashCode()})"
                : "null";

            Debug.Log($"[ScoutHubUIController] {methodName}: 활성화 -> {activateInfo}, 비활성화 -> {deactivateInfo}");

            if (activate != null && activate == deactivate)
            {
                Debug.LogError($"[ScoutHubUIController] {methodName}: 활성화 대상과 비활성화 대상이 " +
                    "동일한 오브젝트입니다 - playerSection/cheerleaderSection 오바인딩을 의심하십시오.");
            }
        }
    }
}
