using System;
using System.Collections.Generic;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// 선수 영입 화면. GDD "뽑기(가챠) > 선수 영입" 절의 3개 카테고리(일반/프리미엄/픽업) 6개 버튼을
    /// 각각 ScoutManager의 대응 메서드에 연결한다. 반환된 카드(1장)를 PlayerCardUI 프리팹으로 생성해
    /// 결과 팝업에 뿌린다. SIGNATURE 이상 등급이면 화면 상단에 강조 문구를 띄운다.
    /// [TASK-KBO-129] 구 roll1Button/roll10Button(단일 재화 혼합 확률)을 폐기했다 - GDD가 10연뽑
    /// 개념을 명시한 카테고리가 없어(픽업의 10/40/80회는 "누적 횟수" 개념이지 "1회 클릭 10연출"이
    /// 아니다) 전부 1회 클릭 = 1장 확정 구조로 통일했다.
    ///
    /// [TASK-KBO-136, 사실 정정 + 재정의] 위 TASK-129의 판단(10연뽑 개념 자체를 없앰)과 이번 명령서
    /// AC-03("모든 선수 스카우트 섹션에 10회 영입 버튼이 존재")은 정면으로 충돌한다. `ScoutManager`에는
    /// `RollTarget(int)` 같은 배치(batch) 메서드가 없고(전수 확인), GDD에도 "1회 클릭 10연출" 명세가
    /// 없다는 TASK-129의 사실관계 자체는 지금도 유효하다 - 다만 이번 명령서는 사용자가 명시적으로
    /// "10회 영입 필수"를 요청한 신규 지시이므로, `ScoutManager`의 확률/재화 로직은 단 한 줄도 건드리지
    /// 않고 이 컨트롤러(UI 레이어)에서 기존 단일 뽑기 메서드(`RollLiveNormal()` 등)를 10번 반복
    /// 호출해 결과를 모아 기존 `ShowResults(List&lt;Player&gt;)`(이미 다중 카드 렌더링을 지원)에 그대로
    /// 넘기는 방식으로 구현했다(`ExecuteMultiRoll()`) - 카테고리별 확률표/천장 로직은 무엇도 새로
    /// 추가하지 않았다.
    ///
    /// [TASK-KBO-137] `Awake()`에 `scoutManager == null`이면 `FindAnyObjectByType&lt;ScoutManager&gt;()`로
    /// 자동 복구하는 안전장치를 추가했다 - 인스펙터 바인딩이 씬 재조립 과정에서 풀려 콘솔에 경고가
    /// 반복 출력되던 증상(명령서 3항)에 대응한다.
    ///
    /// [TASK-KBO-140, 사실 정정] 명령서는 결과 팝업 담당 파일을 `ScoutResultUIController.cs`로
    /// 가정했으나, 그런 이름의 클래스/파일은 프로젝트에 존재하지 않는다(전수 확인) - 결과 팝업
    /// (`resultPopupRoot`/`ShowResults()`/`CloseResultPopup()`)은 이 클래스가 그대로 소유한다.
    /// "다시 뽑기" 버튼은 `lastRetryAction`(직전에 실행한 `ExecuteRoll()`/`ExecuteMultiRoll()` 호출을
    /// 그대로 다시 실행하는 델리게이트)을 저장해 뒀다가 `OnClickRetry()`에서 재호출하는 방식으로
    /// 구현했다 - 확률/재화 차감 로직(`ScoutManager.RollX()`)은 그대로 재사용할 뿐 한 줄도 새로
    /// 추가하지 않았다(명령서 5항). 재화 부족 시 `ShowResults()`가 기존 카드를 지우지 않고 경고만
    /// 남기도록 순서를 조정했다(명령서 7항 - 재시도 실패가 직전 성공 결과를 지워버리는 사고 방지).
    /// </summary>
    public class ScoutUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ScoutManager scoutManager;

        [Header("일반 영입 (라이브 일반 영입권 / 라이브 에픽 영입권)")]
        [SerializeField] private Button liveNormalButton;
        [SerializeField] private Button liveNormalButton10;
        [SerializeField] private Button liveEpicButton;
        [SerializeField] private Button liveEpicButton10;

        [Header("프리미엄 영입 (싸인볼 / 트로피)")]
        [SerializeField] private Button premiumSignatureButton;
        [SerializeField] private Button premiumSignatureButton10;
        [SerializeField] private Button premiumTitleHolderButton;
        [SerializeField] private Button premiumTitleHolderButton10;

        [Header("픽업 영입 (픽업 영입권)")]
        [SerializeField] private Button pickupSignatureButton;
        [SerializeField] private Button pickupSignatureButton10;
        [SerializeField] private Button pickupTitleHolderButton;
        [SerializeField] private Button pickupTitleHolderButton10;

        [Header("Result Popup")]
        [SerializeField] private GameObject resultPopupRoot;
        [Tooltip("카드 10장이 배치될 부모. GridLayoutGroup을 붙여 자동 정렬한다.")]
        [SerializeField] private Transform cardContainer;
        [SerializeField] private PlayerCardUI cardPrefab;
        [Tooltip("[TASK-KBO-093] 결과 팝업을 닫는 '확인' 버튼. CloseResultPopup()을 호출한다.")]
        [SerializeField] private Button closeResultPopupButton;
        [Tooltip("[TASK-KBO-140] 직전과 동일한 카테고리/횟수로 즉시 다시 뽑는 버튼. lastRetryAction을 재호출한다.")]
        [SerializeField] private Button retryButton;

        [Header("Top Pull Announcement")]
        [Tooltip("결과에 SIGNATURE 이상 등급 카드가 있을 때만 활성화되는 강조 텍스트.")]
        [SerializeField] private Text topPullAnnouncementText;
        [SerializeField] private float announcementDisplaySeconds = 2.5f;

        [Header("Navigation")]
        [Tooltip("[TASK-KBO-083] 스카우트 화면을 닫고 로비로 돌아가는 버튼. CheerleaderInventoryUIController/" +
                 "CheerleaderShopUIController의 closeButton과 동일한 관례로 UIManager.ShowScreen()만 호출한다.")]
        [SerializeField] private Button closeButton;

        private void Awake()
        {
            // [TASK-KBO-137] 인스펙터 바인딩이 씬 재조립/재배선 과정 등으로 풀려도(콘솔에 "ScoutManager가
            // 연결되지 않았습니다" 경고가 반복 출력되는 증상) 런타임에 자동으로 씬의 ScoutManager를 찾아
            // 복구하는 안전장치. ScoutManager.Awake()가 DontDestroyOnLoad로 항상 유일한 인스턴스를
            // 유지하므로 FindAnyObjectByType 1회 호출로 충분하다.
            if (scoutManager == null) scoutManager = FindAnyObjectByType<ScoutManager>();

            if (closeButton != null) closeButton.onClick.AddListener(() => UIManager.Instance?.ShowScreen(ScreenType.Lobby));
            // [TASK-KBO-131] CS8978 핫픽스: `scoutManager?.RollX`(메서드 그룹에 직접 null 조건부 연산자를
            // 적용해 Func&lt;Player&gt;로 변환하려는 시도)는 C# 컴파일러가 이 조합의 결과 타입을 확정할 수
            // 없어 "'method group' cannot be made nullable"로 거부한다(?.는 멤버 접근/호출 결과에는
            // 쓸 수 있어도, 호출하지 않은 메서드 그룹 자체에는 델리게이트 변환 문맥에서 쓸 수 없다).
            // `() => scoutManager.RollX()`처럼 새 람다로 감싸면 이 조합 자체가 사라진다 - scoutManager가
            // 실제로 null인 경우의 안전장치는 이 람다가 아니라 ExecuteRoll() 본문의 `scoutManager == null`
            // 체크가 이미 담당하고 있어(그 체크가 rollMethod()를 호출하기 전에 항상 먼저 실행됨) 동작은
            // 기존과 완전히 동일하다.
            if (liveNormalButton != null) liveNormalButton.onClick.AddListener(() => ExecuteRoll(() => scoutManager.RollLiveNormal()));
            if (liveEpicButton != null) liveEpicButton.onClick.AddListener(() => ExecuteRoll(() => scoutManager.RollLiveEpic()));
            if (premiumSignatureButton != null) premiumSignatureButton.onClick.AddListener(() => ExecuteRoll(() => scoutManager.RollPremiumSignature()));
            if (premiumTitleHolderButton != null) premiumTitleHolderButton.onClick.AddListener(() => ExecuteRoll(() => scoutManager.RollPremiumTitleHolder()));
            if (pickupSignatureButton != null) pickupSignatureButton.onClick.AddListener(() => ExecuteRoll(() => scoutManager.RollPickupSignature()));
            if (pickupTitleHolderButton != null) pickupTitleHolderButton.onClick.AddListener(() => ExecuteRoll(() => scoutManager.RollPickupTitleHolder()));

            // [TASK-KBO-136] 명령서 4항의 명시적 경고(TASK-131 CS8978 재발 방지)대로, `scoutManager?.RollX`
            // 형태(메서드 그룹에 직접 null 조건부 연산자)를 쓰지 않고 항상 `() => scoutManager.RollX()`
            // 람다로 감싼다 - ExecuteMultiRoll()이 호출 전에 scoutManager null 여부를 먼저 확인한다.
            if (liveNormalButton10 != null) liveNormalButton10.onClick.AddListener(() => ExecuteMultiRoll(() => scoutManager.RollLiveNormal(), 10));
            if (liveEpicButton10 != null) liveEpicButton10.onClick.AddListener(() => ExecuteMultiRoll(() => scoutManager.RollLiveEpic(), 10));
            if (premiumSignatureButton10 != null) premiumSignatureButton10.onClick.AddListener(() => ExecuteMultiRoll(() => scoutManager.RollPremiumSignature(), 10));
            if (premiumTitleHolderButton10 != null) premiumTitleHolderButton10.onClick.AddListener(() => ExecuteMultiRoll(() => scoutManager.RollPremiumTitleHolder(), 10));
            if (pickupSignatureButton10 != null) pickupSignatureButton10.onClick.AddListener(() => ExecuteMultiRoll(() => scoutManager.RollPickupSignature(), 10));
            if (pickupTitleHolderButton10 != null) pickupTitleHolderButton10.onClick.AddListener(() => ExecuteMultiRoll(() => scoutManager.RollPickupTitleHolder(), 10));

            if (closeResultPopupButton != null) closeResultPopupButton.onClick.AddListener(CloseResultPopup);
            if (retryButton != null) retryButton.onClick.AddListener(OnClickRetry);
        }

        // GDD 2절 등급 서열(숫자가 클수록 상위 등급). "최고급"의 기준(SIGNATURE 이상)을 여기서 정한다.
        private static readonly Dictionary<Grade, int> GradeRank = new Dictionary<Grade, int>
        {
            { Grade.LIVE_NORMAL, 0 },
            { Grade.LIVE_EPIC, 1 },
            { Grade.ALLSTAR, 2 },
            { Grade.TITLE_HOLDER, 3 },
            { Grade.GOLDEN_GLOVE, 4 },
            { Grade.SIGNATURE, 5 },
            { Grade.DYNASTY, 6 },
        };
        private const Grade TopPullThreshold = Grade.SIGNATURE;

        private readonly List<PlayerCardUI> spawnedCards = new List<PlayerCardUI>();

        /// <summary>[TASK-KBO-140] 직전에 실행한 ExecuteRoll()/ExecuteMultiRoll() 호출을 인자 그대로
        /// 다시 실행하는 델리게이트. "다시 뽑기" 버튼(OnClickRetry())이 이 델리게이트를 재호출한다 -
        /// 카테고리/횟수를 별도로 기억할 필요 없이 클로저가 그 정보를 전부 갖고 있다.</summary>
        private Action lastRetryAction;

        /// <summary>[TASK-KBO-129] 6개 카테고리 버튼이 공통으로 쓰는 실행 헬퍼. ScoutManager의 각
        /// Roll* 메서드(단일 Player 반환)를 리스트로 감싸 ShowResults()에 위임한다 - 카드 렌더링
        /// 경로를 6개 카테고리 전부 동일하게 유지한다.</summary>
        private void ExecuteRoll(Func<Player> rollMethod)
        {
            if (scoutManager == null || rollMethod == null)
            {
                Debug.LogWarning("[ScoutUIController] ScoutManager가 연결되지 않았습니다.");
                return;
            }

            lastRetryAction = () => ExecuteRoll(rollMethod);

            var player = rollMethod();
            ShowResults(player != null ? new List<Player> { player } : new List<Player>());
        }

        /// <summary>[TASK-KBO-136] "N회 영입" 버튼 공통 실행 헬퍼. `ScoutManager`에는 배치(batch) 뽑기
        /// 메서드가 없으므로, 기존 단일 뽑기 메서드(`rollMethod`)를 `count`번 반복 호출해 결과를 모아
        /// `ShowResults()`에 한 번에 넘긴다 - 확률/재화 소모 로직은 각 `rollMethod()` 호출이 그대로
        /// 담당하므로 여기서 새로 추가하는 계산은 없다. 중간에 재화가 바닥나 `rollMethod()`가 null을
        /// 반환하면 그 회차만 결과에서 제외한다(예: 재화가 3개뿐이면 10회를 눌러도 카드 3장만 나온다).</summary>
        private void ExecuteMultiRoll(Func<Player> rollMethod, int count)
        {
            if (scoutManager == null || rollMethod == null)
            {
                Debug.LogWarning("[ScoutUIController] ScoutManager가 연결되지 않았습니다.");
                return;
            }

            lastRetryAction = () => ExecuteMultiRoll(rollMethod, count);

            var results = new List<Player>();
            for (int i = 0; i < count; i++)
            {
                var player = rollMethod();
                if (player != null) results.Add(player);
            }

            ShowResults(results);
        }

        private void ShowResults(List<Player> players)
        {
            // [TASK-KBO-140] 재화 부족 등으로 이번 뽑기가 비었을 때 ClearCards()를 먼저 호출해 버리면
            // "다시 뽑기"가 실패한 것뿐인데 직전에 성공적으로 보여주고 있던 카드까지 함께 지워진다 -
            // 빈 결과 체크를 ClearCards()보다 앞으로 옮겨, 실패한 시도는 기존 화면을 그대로 둔 채
            // 경고만 남기도록 한다(명령서 7항 - "재화가 부족합니다" 경고 + 뽑기 미실행 방어).
            if (players == null || players.Count == 0)
            {
                Debug.LogWarning("[ScoutUIController] 재화가 부족합니다. 뽑기가 실행되지 않았습니다.");
                return;
            }

            ClearCards();

            foreach (var player in players)
            {
                SpawnCard(player);
            }

            if (resultPopupRoot != null)
            {
                resultPopupRoot.SetActive(true);
                // [TASK-KBO-139] DetailPanel(InventoryUIController)과 동일한 선제적 방어 - 결과 팝업도
                // ScoutPanel의 다른 형제(카테고리 목록 등)에 가려질 여지를 원천 차단한다(명령서 6항).
                resultPopupRoot.transform.SetAsLastSibling();
            }

            AnnounceTopPullIfAny(players);
        }

        private void SpawnCard(Player player)
        {
            if (cardPrefab == null || cardContainer == null) return;

            var card = Instantiate(cardPrefab, cardContainer);
            card.gameObject.SetActive(true);
            card.Setup(player);
            spawnedCards.Add(card);
        }

        /// <summary>
        /// SIGNATURE 이상 등급이 하나라도 있으면 상단에 강조 문구를 띄운다.
        /// 지금은 텍스트 노출/자동 숨김만 구현한 연출 뼈대이며, 실제 파티클/사운드 등은 후속 과제다.
        /// </summary>
        private void AnnounceTopPullIfAny(List<Player> players)
        {
            Player best = null;
            int bestRank = -1;

            foreach (var player in players)
            {
                if (player?.Template == null) continue;

                int rank = GradeRank.TryGetValue(player.Template.Grade, out var r) ? r : 0;
                if (rank > bestRank)
                {
                    bestRank = rank;
                    best = player;
                }
            }

            if (best == null || bestRank < GradeRank[TopPullThreshold]) return;

            string message = $"★ 최고급 선수 획득! {best.Template.PlayerName} ({best.Template.Grade}) ★";
            Debug.Log($"[ScoutUIController] {message}");

            if (topPullAnnouncementText == null) return;

            topPullAnnouncementText.text = message;
            topPullAnnouncementText.gameObject.SetActive(true);
            CancelInvoke(nameof(HideAnnouncement));
            Invoke(nameof(HideAnnouncement), announcementDisplaySeconds);
        }

        private void HideAnnouncement()
        {
            if (topPullAnnouncementText != null) topPullAnnouncementText.gameObject.SetActive(false);
        }

        /// <summary>결과 팝업의 닫기 버튼 OnClick.</summary>
        public void CloseResultPopup()
        {
            if (resultPopupRoot != null) resultPopupRoot.SetActive(false);
            ClearCards();
        }

        /// <summary>[TASK-KBO-140] 결과 팝업의 "다시 뽑기" 버튼 OnClick. 직전에 실행했던 뽑기(카테고리 +
        /// 1회/10회 여부)를 그대로 재호출한다 - 팝업을 닫지 않고 결과만 갱신된다. 재화가 부족하면
        /// ShowResults()가 기존 카드를 지우지 않고 경고 로그만 남긴다(위 참고).</summary>
        private void OnClickRetry()
        {
            if (lastRetryAction == null)
            {
                Debug.LogWarning("[ScoutUIController] 다시 뽑을 직전 뽑기 정보가 없습니다.");
                return;
            }

            lastRetryAction();
        }

        private void ClearCards()
        {
            foreach (var card in spawnedCards)
            {
                if (card != null) Destroy(card.gameObject);
            }
            spawnedCards.Clear();
        }
    }
}
