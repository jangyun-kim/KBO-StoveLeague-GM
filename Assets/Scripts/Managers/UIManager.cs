using System;
using System.Collections.Generic;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>화면 단위. 새 화면이 생기면 이 enum에 값 하나만 추가하면 된다.</summary>
    public enum ScreenType
    {
        Lobby,
        InGame,
        Inventory,
        Roster,
        Scout,
        Shop,
        Onboarding,
        LeagueStats,
        // [TASK-KBO-060] 기존 Inventory는 InventoryUIController(GameManager.Instance.Inventory, 선수
        // 카드 인벤토리)를 위한 값으로 이미 예약되어 있다(아직 UIManager.screens에 실제 연결되진
        // 않았지만, 이름/주석/컨트롤러 존재로 미루어 다른 용도였음이 명확하다). 치어리더 인벤토리는
        // 완전히 다른 화면이므로 기존 값을 재사용하지 않고 새 값을 끝에 추가했다(중간 삽입 시 이미
        // 씬에 저장된 enum 정수값이 밀려 다른 화면을 가리키게 되는 사고를 피하기 위해 항상 끝에 추가).
        CheerleaderInventory
    }

    /// <summary>
    /// 화면 전환을 중앙에서 관리하는 싱글톤. 각 화면 컨트롤러(LeagueDashboardUIController,
    /// InGameUIController 등)는 서로를 직접 참조하지 않고 UIManager.Instance.ShowScreen(type)만
    /// 호출한다 - 화면 A가 화면 B의 존재를 몰라도 전환이 가능하므로, 화면이 늘어나도
    /// 기존 컨트롤러를 고칠 필요가 없다(개방-폐쇄 원칙).
    ///
    /// ShowScreen()은 대상 화면의 루트만 SetActive(true)하고 나머지는 SetActive(false)한다 - 화면이
    /// 켜지는 순간 그 화면 컨트롤러의 OnEnable()이 자동으로 호출되므로, 여기서 별도로 Refresh()를
    /// 호출해 줄 필요가 없다(각 컨트롤러가 이미 OnEnable에서 스스로 새로고침하는 관례를 따른다).
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Serializable]
        public class ScreenEntry
        {
            public ScreenType Type;
            public GameObject Root;
        }

        [Tooltip("각 ScreenType에 대응하는 화면 루트 GameObject를 등록한다.")]
        [SerializeField] private List<ScreenEntry> screens = new List<ScreenEntry>();

        [Tooltip("시작 시 자동으로 켤 화면.")]
        [SerializeField] private ScreenType initialScreen = ScreenType.Lobby;

        private readonly Dictionary<ScreenType, GameObject> screenRoots = new Dictionary<ScreenType, GameObject>();

        public ScreenType CurrentScreen { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            screenRoots.Clear();
            foreach (var entry in screens)
            {
                if (entry?.Root == null) continue;
                screenRoots[entry.Type] = entry.Root;
            }
        }

        /// <summary>
        /// GameManager.IsFirstLogin이 true면(=아직 선호 구단을 고른 적이 없으면) initialScreen 대신
        /// 온보딩 화면을 강제로 먼저 띄운다. GameManager는 DontDestroyOnLoad 싱글톤이라 Awake()가
        /// 이미 씬의 모든 오브젝트보다 먼저 끝나 있으므로(Unity의 Awake -&gt; Start 실행 순서 보장),
        /// 여기 Start() 시점에는 GameManager.Instance가 항상 준비돼 있다.
        /// </summary>
        private void Start()
        {
            bool needsOnboarding = GameManager.Instance != null && GameManager.Instance.IsFirstLogin;
            ShowScreen(needsOnboarding ? ScreenType.Onboarding : initialScreen);
        }

        /// <summary>지정한 화면만 켜고 나머지 등록된 화면은 전부 끈다.</summary>
        public void ShowScreen(ScreenType type)
        {
            if (!screenRoots.ContainsKey(type))
            {
                Debug.LogWarning($"[UIManager] '{type}' 화면이 등록되어 있지 않습니다.");
                return;
            }

            foreach (var pair in screenRoots)
            {
                pair.Value.SetActive(pair.Key == type);
            }

            CurrentScreen = type;
        }

        /// <summary>type에 대응하는 화면 루트를 반환한다. 등록되어 있지 않으면 null.</summary>
        public GameObject GetScreenRoot(ScreenType type)
        {
            return screenRoots.TryGetValue(type, out var root) ? root : null;
        }
    }
}
