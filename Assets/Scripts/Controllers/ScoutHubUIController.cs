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

        private void Awake()
        {
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
            if (playerSection != null) playerSection.SetActive(true);
            if (cheerleaderSection != null) cheerleaderSection.SetActive(false);
        }

        public void ShowCheerleaderSection()
        {
            if (playerSection != null) playerSection.SetActive(false);
            if (cheerleaderSection != null) cheerleaderSection.SetActive(true);
        }
    }
}
