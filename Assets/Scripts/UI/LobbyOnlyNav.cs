using KBOManager.Managers;
using UnityEngine;

namespace KBOManager.UI
{
    /// <summary>
    /// [TASK-KBO-179 요구사항 C] 하단 5탭 내비게이션(선수 관리 / 라인업 / 홈 / 상점 / 치어리더)은 "홈(로비) 화면 전용"이다.
    /// 내비게이션은 로비 패널 하위에만 조립되지만, 스카우트 허브·치어리더 관리처럼 UIManager에 등록되지 않고 로비 위에 덮어 여는
    /// 오버레이 화면이 열리면 로비가 꺼지지 않아 탭 바가 그 화면 아래로 비쳐 보일 수 있다. 이 컴포넌트는 매 프레임
    /// (1) UIManager 현재 화면이 Lobby이고 (2) 캔버스 최상위에서 로비보다 뒤(위)에 그려지는 활성 전체 화면 패널이 없을 때만
    /// 탭 바 그래픽을 보이게 한다. 탭 바 GameObject 자체를 끄지 않고 CanvasGroup으로 숨겨 버튼 바인딩/레이아웃은 그대로 둔다.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class LobbyOnlyNav : MonoBehaviour
    {
        [Tooltip("로비 화면 루트(비우면 부모 중 Canvas 바로 아래 조상을 자동 사용).")]
        [SerializeField] private RectTransform lobbyRoot;

        private CanvasGroup group;

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            if (lobbyRoot == null) lobbyRoot = FindCanvasChildAncestor();
        }

        private void LateUpdate()
        {
            bool visible = IsLobbyFrontmost();
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        private bool IsLobbyFrontmost()
        {
            if (UIManager.Instance != null && UIManager.Instance.CurrentScreen != ScreenType.Lobby) return false;
            if (lobbyRoot == null || lobbyRoot.parent == null) return true;

            var parent = lobbyRoot.parent;
            for (int i = lobbyRoot.GetSiblingIndex() + 1; i < parent.childCount; i++)
            {
                var sibling = parent.GetChild(i) as RectTransform;
                if (sibling == null || !sibling.gameObject.activeInHierarchy) continue;
                if (sibling.name.StartsWith("_")) continue; // _Templates 등 비표시 보관함
                if (CoversScreen(sibling)) return false;
            }
            return true;
        }

        private static bool CoversScreen(RectTransform rect)
        {
            // 전체 화면 패널(앵커가 부모 전체를 덮음)만 "다른 화면"으로 본다 - 토스트/작은 팝업은 탭 바를 숨기지 않는다.
            // [TASK-GM-06] 1920×1080 전환 후 기존 세로 화면은 9:16 프레임(LegacyPortraitFrame, 중앙 앵커)이라 프레임 자체를 전체 화면으로 본다.
            if (rect.GetComponent<LegacyPortraitFrame>() != null) return true;
            return rect.anchorMin.x <= 0.01f && rect.anchorMin.y <= 0.01f && rect.anchorMax.x >= 0.99f && rect.anchorMax.y >= 0.99f;
        }

        private RectTransform FindCanvasChildAncestor()
        {
            Transform node = transform;
            while (node.parent != null && node.parent.GetComponent<Canvas>() == null) node = node.parent;
            return node as RectTransform;
        }
    }
}
