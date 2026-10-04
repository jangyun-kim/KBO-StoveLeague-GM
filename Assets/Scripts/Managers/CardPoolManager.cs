using System.Collections.Generic;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.Pool;

namespace KBOManager.Managers
{
    /// <summary>
    /// PlayerCardUI 프리팹을 재사용하는 오브젝트 풀. 인벤토리/로스터/재료 선택 화면이 매번
    /// Instantiate/Destroy 하는 대신 Get()/Release()로 카드를 빌려 쓰고 반납하게 해서,
    /// 카드 수가 많을 때 생기는 GC 압박과 프레임 드랍을 줄인다.
    ///
    /// 프리팹을 키로 풀을 나눠 관리하므로, 여러 화면이 "같은 PlayerCardUI 프리팹 에셋"을
    /// 참조하기만 하면 풀을 자동으로 공유한다(예: 인벤토리 화면과 재료 선택 팝업이 같은 프리팹을
    /// 쓰면 카드 인스턴스가 서로 재사용된다).
    /// </summary>
    public class CardPoolManager : MonoBehaviour
    {
        public static CardPoolManager Instance { get; private set; }

        [Tooltip("풀의 초기 용량. 화면에 동시에 떠 있을 카드 수를 대략 어림잡아 설정하면 재할당이 줄어든다.")]
        [SerializeField] private int defaultCapacity = 50;
        [Tooltip("풀이 보관할 수 있는 최대 개수. 이보다 많이 반납되면 초과분은 그냥 파괴한다(무한 증가 방지).")]
        [SerializeField] private int maxPoolSize = 500;

        private readonly Dictionary<PlayerCardUI, ObjectPool<PlayerCardUI>> poolsByPrefab =
            new Dictionary<PlayerCardUI, ObjectPool<PlayerCardUI>>();

        // Release() 시 "어느 풀로 돌려줘야 하는지" 역추적하기 위한 인스턴스 -> 원본 프리팹 매핑.
        private readonly Dictionary<PlayerCardUI, PlayerCardUI> prefabByInstance =
            new Dictionary<PlayerCardUI, PlayerCardUI>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>prefab에 대응하는 풀에서 카드 인스턴스 하나를 꺼내 parent 아래에 배치하고 활성화해 반환한다.</summary>
        public PlayerCardUI Get(PlayerCardUI prefab, Transform parent)
        {
            var pool = GetOrCreatePool(prefab);
            var instance = pool.Get();

            instance.transform.SetParent(parent, false);
            prefabByInstance[instance] = prefab;

            return instance;
        }

        /// <summary>Get()으로 받은 카드를 파괴하지 않고 풀로 반환한다(비활성화 후 보관, 다음 Get()에서 재사용).</summary>
        public void Release(PlayerCardUI instance)
        {
            if (instance == null) return;

            if (!prefabByInstance.TryGetValue(instance, out var prefab) || !poolsByPrefab.TryGetValue(prefab, out var pool))
            {
                // 이 매니저를 거치지 않고 생성된 카드 등 대응하는 풀을 찾을 수 없으면 안전하게 그냥 파괴한다.
                Destroy(instance.gameObject);
                return;
            }

            prefabByInstance.Remove(instance);
            pool.Release(instance);
        }

        /// <summary>[TASK-KBO-185] 반납 카드의 렌더 상태 초기화 - CardHolderFit 스케일·ignoreLayout, RectMask2D 컬링/클립 잔재.</summary>
        public static void ResetRenderState(PlayerCardUI card)
        {
            if (card == null) return;
            CardHolderFit.ResetCard(card);
            foreach (var graphic in card.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
            {
                var canvasRenderer = graphic.canvasRenderer;
                if (canvasRenderer == null) continue;
                canvasRenderer.cull = false;
                canvasRenderer.DisableRectClipping();
            }
        }

        private ObjectPool<PlayerCardUI> GetOrCreatePool(PlayerCardUI prefab)
        {
            if (poolsByPrefab.TryGetValue(prefab, out var existing)) return existing;

            var pool = new ObjectPool<PlayerCardUI>(
                createFunc: () => Instantiate(prefab),
                actionOnGet: card => card.gameObject.SetActive(true),
                actionOnRelease: card =>
                {
                    ResetRenderState(card); // [TASK-KBO-185] 다른 화면의 스케일/레이아웃 무시/마스크 컬링이 다음 대여로 새지 않게
                    card.Clear();
                    card.gameObject.SetActive(false);
                    card.transform.SetParent(transform, false); // 비활성 카드를 이 매니저 아래에 보관해 씬 하이어라키를 어지럽히지 않는다
                },
                actionOnDestroy: card =>
                {
                    if (card != null) Destroy(card.gameObject);
                },
                collectionCheck: true,
                defaultCapacity: defaultCapacity,
                maxSize: maxPoolSize);

            poolsByPrefab[prefab] = pool;
            return pool;
        }
    }
}
