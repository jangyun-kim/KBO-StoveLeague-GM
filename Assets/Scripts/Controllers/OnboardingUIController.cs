using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>구단 1개에 대응하는 버튼 묶음(구 SceneInitializer 배선 호환). TASK-181 구단 타일도 같은 형식을 쓴다.</summary>
    [Serializable]
    public class TeamSelectButtonEntry
    {
        public Team Team;
        public Button Button;
        [Tooltip("구단 로고 Image(preserveAspect) - TASK-181에서 런타임에 Broadcast179/logo_{Team}을 채운다.")]
        public Image LogoImage;
        [Tooltip("구단명 텍스트(선택).")]
        public Text TeamNameText;
        [Tooltip("[TASK-KBO-181] 선택 강조용 배경(선택 시 구단 컬러, 아니면 어두운 남색).")]
        public Image Background;
    }

    /// <summary>[TASK-KBO-181] 정착 지원 선물 카드 1장 칸.</summary>
    [Serializable]
    public class GiftSlotEntry
    {
        public Button Button;
        public RectTransform CardHolder;
        public Text CaptionText;
        public Image SelectionFrame;
    }

    /// <summary>
    /// [TASK-KBO-181 전면 개편] 정식 온보딩 화면 3페이지.
    ///   Step 1 타이틀: 『KBO 스토브리그 : 단장의 시간』 - 이어할 커리어가 있으면 [시즌 이어하기] / [새 단장 부임], 없으면 [단장 부임하기].
    ///   Step 2 구단 선택 + 단장 닉네임: KBO 10개 구단 타일(로고 + 구단명) 중 하나와 닉네임(최대 12자)을 정해야 [다음].
    ///   Step 3 신규 단장 정착 지원 선물: 2024 골든글러브 4종 실제 카드를 나란히 보여 주고 1장을 고른 뒤 [선물 수령 및 단장 취임]
    ///          → OnboardingManager.CompleteOnboarding(구단, 닉네임, 선물) - 선택 구단 2026 LIVE_NORMAL 전원 지급 + 오토 라인업 + 로비.
    /// Step 4 튜토리얼은 로비(LobbyTutorial181)가 GameManager.TutorialCompleted == false일 때 띄운다.
    /// 이 컨트롤러는 지급/편성 규칙을 모른다 - 고른 값만 OnboardingManager에 넘긴다.
    /// </summary>
    public class OnboardingUIController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private OnboardingManager onboardingManager;
        [Tooltip("선물 카드 미리보기에 쓸 공용 카드 템플릿(_Templates/PlayerCardTemplate).")]
        [SerializeField] private PlayerCardUI cardPrefab;

        [Header("Step 1 - Title")]
        [SerializeField] private GameObject titlePage;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button newManagerButton;
        [SerializeField] private Button startButton;
        [SerializeField] private Text titleStatusText;

        [Header("Step 2 - Team + Nickname")]
        [SerializeField] private GameObject teamPage;
        [Tooltip("KBO 10개 구단(Team.None 제외). 구 SceneInitializer가 만든 버튼 목록도 그대로 동작한다.")]
        [SerializeField] private List<TeamSelectButtonEntry> teamButtons = new List<TeamSelectButtonEntry>();
        [SerializeField] private InputField nicknameInput;
        [SerializeField] private Button teamNextButton;
        [SerializeField] private Button teamBackButton;
        [SerializeField] private Text teamHintText;

        [Header("Step 3 - Gift (2024 Golden Glove)")]
        [SerializeField] private GameObject giftPage;
        [SerializeField] private List<GiftSlotEntry> giftSlots = new List<GiftSlotEntry>();
        [SerializeField] private Button giftConfirmButton;
        [SerializeField] private Button giftBackButton;
        [SerializeField] private Text giftHintText;

        [Header("Colors")]
        [SerializeField] private Color tileIdleColor = new Color(0.12f, 0.15f, 0.25f, 0.95f);
        [SerializeField] private Color giftSelectedColor = new Color(1f, 0.82f, 0.27f);

        private Team selectedTeam = Team.None;
        private int selectedGift = -1;
        private readonly List<Player> giftPreviews = new List<Player>();
        private readonly List<PlayerCardUI> giftCards = new List<PlayerCardUI>();

        private bool HasPagedLayout => titlePage != null && teamPage != null && giftPage != null;

        private void Awake()
        {
            foreach (var entry in teamButtons)
            {
                if (entry?.Button == null || entry.Team == Team.None) continue;
                if (entry.TeamNameText != null) entry.TeamNameText.text = CompyaUiKit.FullName(entry.Team);
                if (entry.LogoImage != null) TeamLogoSprites.Apply(entry.LogoImage, entry.Team);
                var team = entry.Team; // 클로저 캡처
                entry.Button.onClick.AddListener(() => OnClickTeam(team));
            }

            for (int i = 0; i < giftSlots.Count; i++)
            {
                int index = i;
                if (giftSlots[i]?.Button != null) giftSlots[i].Button.onClick.AddListener(() => SelectGift(index));
            }

            if (continueButton != null) continueButton.onClick.AddListener(ContinueSeason);
            if (newManagerButton != null) newManagerButton.onClick.AddListener(ShowTeamPage);
            if (startButton != null) startButton.onClick.AddListener(ShowTeamPage);
            if (teamNextButton != null) teamNextButton.onClick.AddListener(ShowGiftPage);
            if (teamBackButton != null) teamBackButton.onClick.AddListener(ShowTitle);
            if (giftBackButton != null) giftBackButton.onClick.AddListener(ShowTeamPage);
            if (giftConfirmButton != null) giftConfirmButton.onClick.AddListener(ConfirmGift);
            if (nicknameInput != null)
            {
                nicknameInput.characterLimit = OnboardingRules.NicknameMaxLength;
                nicknameInput.onValueChanged.AddListener(_ => RefreshTeamPage());
            }
        }

        private void OnEnable() => ShowTitle();

        private OnboardingManager Manager => onboardingManager != null ? onboardingManager : OnboardingManager.Instance;

        // ------------------------------------------------------------------ Step 1

        private bool HasCareerInMemory()
        {
            var gm = GameManager.Instance;
            return gm != null && !gm.IsFirstLogin && gm.FavoriteTeam != Team.None && gm.Inventory.Count > 0;
        }

        public void ShowTitle()
        {
            if (!HasPagedLayout) return; // 구 씬(버튼만 있는 온보딩): 구단 버튼 클릭 즉시 온보딩 완료
            ShowPage(titlePage);

            bool canContinue = HasCareerInMemory() || (SaveManager.Instance != null && SaveManager.Instance.HasContinuableSave());
            if (continueButton != null) continueButton.gameObject.SetActive(canContinue);
            if (newManagerButton != null) newManagerButton.gameObject.SetActive(canContinue);
            if (startButton != null) startButton.gameObject.SetActive(!canContinue);
            if (titleStatusText != null)
            {
                var gm = GameManager.Instance;
                titleStatusText.text = canContinue && gm != null && HasCareerInMemory()
                    ? $"진행 중: {CompyaUiKit.FullName(gm.FavoriteTeam)} {ManagerLabel(gm.ManagerNickname)}"
                    : canContinue ? "저장된 시즌이 있습니다." : "KBO 10개 구단 중 하나를 맡아 첫 시즌을 시작하십시오.";
            }
        }

        public static string ManagerLabel(string nickname) => string.IsNullOrEmpty(nickname) ? "단장" : $"{nickname} 단장";

        private void ContinueSeason()
        {
            if (!HasCareerInMemory())
            {
                bool loaded = SaveManager.Instance != null && SaveManager.Instance.LoadGame();
                if (!loaded || !HasCareerInMemory())
                {
                    if (titleStatusText != null) titleStatusText.text = "이어할 시즌을 불러오지 못했습니다 - [새 단장 부임]으로 시작하십시오.";
                    return;
                }
            }
            UIManager.Instance?.ShowScreen(ScreenType.Lobby);
        }

        // ------------------------------------------------------------------ Step 2

        public void ShowTeamPage()
        {
            if (!HasPagedLayout) return;
            ShowPage(teamPage);
            RefreshTeamPage();
        }

        private void OnClickTeam(Team team)
        {
            if (!HasPagedLayout)
            {
                Manager?.CompleteOnboarding(team); // 구 씬 호환
                return;
            }
            selectedTeam = team;
            RefreshTeamPage();
        }

        private string Nickname => OnboardingRules.NormalizeNickname(nicknameInput != null ? nicknameInput.text : null);

        private void RefreshTeamPage()
        {
            foreach (var entry in teamButtons)
            {
                if (entry?.Background == null) continue;
                bool selected = entry.Team == selectedTeam;
                entry.Background.color = selected ? CompyaUiKit.TeamColor(entry.Team) : tileIdleColor;
                if (entry.TeamNameText != null) entry.TeamNameText.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            }

            bool ready = selectedTeam != Team.None && Nickname != null;
            if (teamNextButton != null) teamNextButton.interactable = ready;
            if (teamHintText != null)
            {
                teamHintText.text = selectedTeam == Team.None ? "맡을 구단을 선택하십시오."
                    : Nickname == null ? $"{CompyaUiKit.FullName(selectedTeam)} - 단장 닉네임을 입력하십시오(최대 {OnboardingRules.NicknameMaxLength}자)."
                    : $"{CompyaUiKit.FullName(selectedTeam)} {Nickname} 단장 - 2026 LIVE 선수단 전원이 무료 지급됩니다.";
            }
        }

        // ------------------------------------------------------------------ Step 3

        public void ShowGiftPage()
        {
            if (!HasPagedLayout || selectedTeam == Team.None || Nickname == null) return;
            ShowPage(giftPage);
            BuildGiftCards();
            SelectGift(-1);
        }

        private void BuildGiftCards()
        {
            if (giftCards.Count > 0) return; // 미리보기는 한 번만 만든다
            giftPreviews.Clear();
            var previews = Manager != null ? Manager.CreateGiftPreviews() : new List<Player>();
            var nativeSize = CardHolderFit.NativeSizeOf(cardPrefab);

            for (int i = 0; i < giftSlots.Count; i++)
            {
                var slot = giftSlots[i];
                var player = i < previews.Count ? previews[i] : null;
                giftPreviews.Add(player);
                if (slot == null) continue;

                if (slot.CaptionText != null)
                {
                    slot.CaptionText.text = player?.Template == null
                        ? "카드 데이터 없음"
                        : $"<b>{OnboardingRules.CardTitle(player.Template)}</b>\n{CompyaUiKit.ShortName(player.Template.Team)} · " +
                          $"{CompyaMatchView.PositionLabel(player)} · OVR {player.CalculateOVR(false)}";
                }
                if (slot.Button != null) slot.Button.interactable = player != null;
                if (player == null || cardPrefab == null || slot.CardHolder == null) continue;

                if (!slot.CardHolder.TryGetComponent<CardHolderFit>(out var fit)) fit = slot.CardHolder.gameObject.AddComponent<CardHolderFit>();
                fit.Configure(nativeSize);
                var card = Instantiate(cardPrefab);
                card.gameObject.SetActive(true);
                card.Setup(player);
                foreach (var graphic in card.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false; // 클릭은 칸 버튼이 받는다
                fit.Place((RectTransform)card.transform);
                giftCards.Add(card);
            }
        }

        private void SelectGift(int index)
        {
            selectedGift = index >= 0 && index < giftPreviews.Count && giftPreviews[index] != null ? index : -1;
            for (int i = 0; i < giftSlots.Count; i++)
            {
                var frame = giftSlots[i]?.SelectionFrame;
                if (frame != null) frame.color = i == selectedGift ? giftSelectedColor : new Color(1f, 1f, 1f, 0.08f);
                if (i < giftCards.Count && giftCards[i] != null) giftCards[i].SetSelected(i == selectedGift);
            }

            if (giftConfirmButton != null) giftConfirmButton.interactable = selectedGift >= 0;
            if (giftHintText != null)
            {
                giftHintText.text = selectedGift < 0
                    ? "2024 골든글러브 수상자 4명 중 1명을 정착 지원 선물로 영입할 수 있습니다."
                    : OnboardingRules.DescribeGiftChoice(giftPreviews[selectedGift], selectedTeam);
            }
        }

        private void ConfirmGift()
        {
            if (selectedGift < 0 || selectedTeam == Team.None) return;
            var manager = Manager;
            if (manager == null)
            {
                Debug.LogWarning("[OnboardingUIController] OnboardingManager가 없어 단장 취임을 진행할 수 없습니다 - " +
                    "'KBO Manager/Setup/Apply Latest UI (TASK-168~182)'을 실행하십시오.");
                return;
            }
            manager.CompleteOnboarding(selectedTeam, Nickname, OnboardingRules.GiftTemplateIds[selectedGift]);
            selectedGift = -1;
        }

        private void ShowPage(GameObject page)
        {
            if (titlePage != null) titlePage.SetActive(page == titlePage);
            if (teamPage != null) teamPage.SetActive(page == teamPage);
            if (giftPage != null) giftPage.SetActive(page == giftPage);
        }
    }
}
