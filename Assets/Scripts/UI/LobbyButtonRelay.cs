using KBOManager.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-180] 메인 홈(메인 홈.jpg 1:1) 보조 버튼 - LeagueDashboardUIController에 필드가 없는 퀵메뉴/벤토 타일(라이브 · 세트덱 ·
    /// 응원단 · 기록실 · 순위 · 커뮤니티 · 후원사 · 시그니처)을 기존 화면(UIManager.ScreenType)으로 보내거나 팝업을 켜고 끈다.
    /// </summary>
    public class LobbyButtonRelay : MonoBehaviour
    {
        [SerializeField] private bool useScreen = true;
        [SerializeField] private ScreenType screen = ScreenType.Lobby;
        [Tooltip("지정하면 화면 전환 대신 이 오브젝트를 켜고 끈다(순위표 팝업 등).")]
        [SerializeField] private GameObject toggleTarget;

        public void Configure(ScreenType target) { useScreen = true; screen = target; toggleTarget = null; }
        public void Configure(GameObject toggle) { useScreen = false; toggleTarget = toggle; }

        private void Awake()
        {
            if (TryGetComponent<Button>(out var button)) button.onClick.AddListener(Fire);
        }

        private void Fire()
        {
            if (toggleTarget != null) toggleTarget.SetActive(!toggleTarget.activeSelf);
            else if (useScreen && screen == ScreenType.Inventory) Controllers.PlayerManagementUIController.OpenGrowthHub(); // [TASK-KBO-184] 선수단 강화 = 성장 센터
            else if (useScreen) UIManager.Instance?.ShowScreen(screen);
        }
    }
}
