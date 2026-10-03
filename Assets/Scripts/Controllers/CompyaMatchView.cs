using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-179] 컴프야V26 1:1 경기 화면(9장 레퍼런스) - 경기 유형 선택 / 메인 세로 중계(전광판·부감도 그라운드·파란 포물선
    /// 타구 궤적·승률 브릿지 바·ON THE MOUND/AT BAT·양 팀 1~9번 타순) / 이닝 종료 중간 화면 / 승부처 직접 플레이 선택 / 직접 플레이
    /// 타석 HUD(대구 삼성 라이온즈 파크) / 경기 종료 결과 1~3.
    ///
    /// 시뮬레이션 경로는 그대로다 - PlayBallController가 MatchEngine.PlayFullMatchAsEventQueue()로 경기 전체를 먼저 계산하고,
    /// BroadcastUIManager가 이벤트를 하나씩 재생한다. 이 뷰는 (1) OnEventPlayed로 매 이벤트 후 화면을 다시 그리고, (2) HoldBeforeEvent
    /// 게이트로 승부처/이닝 종료/일시정지 때 재생을 멈췄다가 이어 갈 뿐 결과를 바꾸지 않는다(GDD v4.0 "단장 시선" - 직접 플레이의 작전
    /// 버튼은 결과 공개 연출이며 판정은 엔진 계산 그대로). 화면 계층은 Build()가 코드로 만든다(Setup 메뉴가 에디터에서 한 번 만들어 씬에
    /// 저장하고, 런타임 Awake에서 같은 코드로 다시 만들어 참조를 확정한다 - 씬 직렬화 누락으로 끊어진 참조가 생길 수 없다).
    /// </summary>
    public partial class CompyaMatchView : MonoBehaviour
    {
        public enum PlayMode { Quick, Highlight, Full }

        [Header("References (비우면 씬에서 자동 탐색)")]
        [SerializeField] private PlayBallController playBallController;
        [SerializeField] private BroadcastUIManager broadcastUIManager;
        [SerializeField] private InGameUIController inGameUIController;
        [SerializeField] private MatchRewardManager matchRewardManager;

        [Header("Fonts (Setup이 KBO Dia Gothic을 주입)")]
        [SerializeField] private Font boldFont;
        [SerializeField] private Font regularFont;

        [Header("Playback")]
        [SerializeField] private float highlightEventDelay = 0.5f;
        [SerializeField] private float fullPlayEventDelay = 0.8f;
        [SerializeField] private int highlightMaxDirectPlays = 4;
        [SerializeField] private int fullPlayMaxDirectPlays = 12;
        [SerializeField] private float inningSplashSeconds = 1.4f;
        [SerializeField] private float directResultSeconds = 1.6f;

        public const string RootName = "Root179";

        private PlayMode mode = PlayMode.Highlight;
        private bool modeChosen;
        private bool paused;
        private bool splashActive;
        private bool choiceActive;
        private bool directResultActive;
        private bool awaitingDirectReveal;
        private int pendingChoiceIndex = -1;
        private int pendingPlateAppearance = -1;
        private int directPlayCount;
        // [TASK-KBO-180] 승부처 판정은 "타석 번호" 기준 - 작전 재계산으로 이벤트 인덱스가 밀려도(도루 이벤트 삽입) 같은 타석을 다시 묻지 않는다.
        private readonly HashSet<int> resolvedChoices = new HashSet<int>();
        private MatchTactic selectedTactic = MatchTactic.None;

        private IReadOnlyList<PlayEvent> events;
        private Team awayTeam, homeTeam;
        private readonly List<Player> awayOrder = new List<Player>();
        private readonly List<Player> homeOrder = new List<Player>();
        private CompyaGameTracker finalTracker;
        private MatchResult lastResult;
        private MatchRewardResult lastReward;
        private Coroutine arcRoutine;

        public PlayMode CurrentMode => mode;

        // ================================================================== 수명주기

        private void Awake()
        {
            ResolveReferences();
            Build();
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (broadcastUIManager != null)
            {
                broadcastUIManager.OnPlaybackStarted += HandlePlaybackStarted;
                broadcastUIManager.OnEventPlayed += HandleEventPlayed;
                broadcastUIManager.OnEventsReplaced += HandleEventsReplaced;
                broadcastUIManager.HoldBeforeEvent = HoldGate;
            }
            if (playBallController != null) playBallController.OnMatchCompleted += HandleMatchCompleted;
            if (matchRewardManager != null) matchRewardManager.OnRewardGranted += HandleRewardGranted;
            if (inGameUIController != null) inGameUIController.SuppressLegacyResultPanel = true;
        }

        private void OnDisable()
        {
            if (broadcastUIManager != null)
            {
                broadcastUIManager.OnPlaybackStarted -= HandlePlaybackStarted;
                broadcastUIManager.OnEventPlayed -= HandleEventPlayed;
                broadcastUIManager.OnEventsReplaced -= HandleEventsReplaced;
                if (broadcastUIManager.HoldBeforeEvent == (System.Func<PlayEvent, int, bool>)HoldGate) broadcastUIManager.HoldBeforeEvent = null;
            }
            if (playBallController != null) playBallController.OnMatchCompleted -= HandleMatchCompleted;
            if (matchRewardManager != null) matchRewardManager.OnRewardGranted -= HandleRewardGranted;
            paused = splashActive = choiceActive = directResultActive = awaitingDirectReveal = false;
        }

        private void ResolveReferences()
        {
            if (playBallController == null) playBallController = FindAnyObjectByType<PlayBallController>(FindObjectsInactive.Include);
            if (broadcastUIManager == null) broadcastUIManager = FindAnyObjectByType<BroadcastUIManager>(FindObjectsInactive.Include);
            if (inGameUIController == null) inGameUIController = FindAnyObjectByType<InGameUIController>(FindObjectsInactive.Include);
            if (matchRewardManager == null) matchRewardManager = MatchRewardManager.Instance != null
                ? MatchRewardManager.Instance
                : FindAnyObjectByType<MatchRewardManager>(FindObjectsInactive.Include);
        }

        /// <summary>Setup 메뉴 전용 - 에디터에서 참조/폰트를 주입한다.</summary>
        public void ConfigureForEditor(PlayBallController playBall, BroadcastUIManager broadcast, InGameUIController inGame,
            MatchRewardManager reward, Font bold, Font regular)
        {
            playBallController = playBall;
            broadcastUIManager = broadcast;
            inGameUIController = inGame;
            matchRewardManager = reward;
            boldFont = bold;
            regularFont = regular;
        }

        // ================================================================== 진입: 경기 유형 선택

        /// <summary>로비 [플레이 볼]이 호출한다 - 경기 유형 선택 화면(SELECT TYPE)을 연다.</summary>
        public void ShowTypeSelect()
        {
            if (root == null) Build();
            HideAll();
            typePanel.SetActive(true);
            RefreshTypeSelect();
        }

        private void SelectMode(PlayMode selected)
        {
            mode = selected;
            RefreshTypeSelect();
        }

        private void OnStartPressed()
        {
            if (playBallController == null)
            {
                typeInfoText.text = "PlayBallController가 없어 경기를 시작할 수 없습니다.";
                return;
            }
            if (LeagueManager.Instance == null || LeagueManager.Instance.PeekNextFixture() == null)
            {
                typeInfoText.text = "진행할 예정 경기가 없습니다.";
                return;
            }

            modeChosen = true;
            paused = splashActive = choiceActive = directResultActive = awaitingDirectReveal = false;
            resolvedChoices.Clear();
            directPlayCount = 0;
            lastReward = null;
            HideAll();
            relayPanel.SetActive(true);

            playBallController.StartMatch(); // -> LoadEvents -> HandlePlaybackStarted (재생 시작)
            if (mode == PlayMode.Quick) broadcastUIManager?.RequestSkip(); // 빠른 진행: 즉시 결과
        }

        private void OnBackPressed()
        {
            HideAll();
            UIManager.Instance?.ShowScreen(ScreenType.Lobby);
        }

        // ================================================================== 재생 연동

        private void HandlePlaybackStarted()
        {
            if (root == null) Build();
            events = broadcastUIManager != null ? broadcastUIManager.EventLog : null;
            awayTeam = playBallController != null ? playBallController.AwayTeam : Team.None;
            homeTeam = playBallController != null ? playBallController.HomeTeam : Team.None;

            if (!modeChosen) mode = PlayMode.Highlight; // 디버그 등 유형 선택 없이 시작된 경기
            modeChosen = false;
            if (broadcastUIManager != null)
                broadcastUIManager.PerEventDelaySeconds = mode == PlayMode.Full ? fullPlayEventDelay : highlightEventDelay;

            BuildBattingOrders();
            HideAll();
            relayPanel.SetActive(true);
            ApplyTeamsToRelay();
            ClearArc();
            toastRoot.gameObject.SetActive(false);
            RefreshRelay(CompyaGameTracker.Build(events, -1), -1);
        }

        private void HandleEventPlayed(PlayEvent evt, int index)
        {
            if (evt == null || events == null) return;
            var tracker = CompyaGameTracker.Build(events, index);
            RefreshRelay(tracker, index);

            if (evt.Type == PlayEventType.AtBatResult)
            {
                DrawTrajectory(evt, index);
                ShowToast(evt, tracker);
                if (awaitingDirectReveal)
                {
                    awaitingDirectReveal = false;
                    StartCoroutine(DirectResultRoutine(evt));
                }
            }
            else if (evt.Type == PlayEventType.HalfInningEnd && !evt.IsTopHalf && mode != PlayMode.Quick)
            {
                StartCoroutine(SplashRoutine(tracker, evt.Inning));
            }
        }

        /// <summary>[TASK-KBO-180] 작전 재계산으로 이벤트 목록이 바뀌면 새 목록을 참조한다.</summary>
        private void HandleEventsReplaced()
        {
            events = broadcastUIManager != null ? broadcastUIManager.EventLog : events;
        }

        /// <summary>BroadcastUIManager 게이트 - true면 재생을 멈춘다.</summary>
        private bool HoldGate(PlayEvent evt, int index)
        {
            if (!isActiveAndEnabled) return false;
            if (paused || splashActive || choiceActive || directResultActive) return true;
            if (evt != null && evt.Type == PlayEventType.AtBatResult && IsClutch(evt, index))
            {
                ShowChoice(evt, index);
                return true;
            }
            return false;
        }

        private void TryRelease()
        {
            if (paused || splashActive || choiceActive || directResultActive) return;
            if (broadcastUIManager != null && broadcastUIManager.IsHeld) broadcastUIManager.ReleaseHold();
        }

        private bool IsUserBatting(bool isTopHalf)
        {
            var user = LeagueManager.Instance != null ? LeagueManager.Instance.UserTeam : Team.None;
            return isTopHalf ? awayTeam == user : homeTeam == user;
        }

        /// <summary>
        /// 승부처 판정(직접 플레이 자동 선택). 하이라이트: 5회 이후 · 3점 차 이내 · (득점권 주자 또는 8회 이후), 최대 4회.
        /// 풀 플레이: 득점권 주자, 최대 12회. 빠른 진행은 직접 플레이가 없다.
        /// [TASK-KBO-180] 우리 팀 공격(공격 작전)뿐 아니라 우리 팀 수비(상대 타석 - 투수 교체/정면 승부/고의사구) 승부처도 고른다.
        /// </summary>
        private bool IsClutch(PlayEvent evt, int index)
        {
            if (mode == PlayMode.Quick || evt.PlateAppearance < 0 || resolvedChoices.Contains(evt.PlateAppearance)) return false;
            var user = LeagueManager.Instance != null ? LeagueManager.Instance.UserTeam : Team.None;
            if (awayTeam != user && homeTeam != user) return false;

            var before = CompyaGameTracker.Build(events, index - 1);
            int diff = Mathf.Abs(before.AwayScore - before.HomeScore);
            if (mode == PlayMode.Full)
                return resolvedChoices.Count < fullPlayMaxDirectPlays && before.HasRunnerInScoringPosition;

            return resolvedChoices.Count < highlightMaxDirectPlays && evt.Inning >= 5 && diff <= 3
                && (before.HasRunnerInScoringPosition || evt.Inning >= 8);
        }

        private void OnPausePressed()
        {
            paused = !paused;
            CompyaUiKit.SetButtonText(pauseButton, paused ? "▶" : "II");
            if (!paused) TryRelease();
        }

        private void OnSkipPressed()
        {
            paused = splashActive = choiceActive = directResultActive = awaitingDirectReveal = false;
            CompyaUiKit.SetButtonText(pauseButton, "II");
            splashPanel.SetActive(false);
            choicePanel.SetActive(false);
            directPanel.SetActive(false);
            broadcastUIManager?.RequestSkip();
        }

        // ---- 이닝 종료 중간 화면

        private IEnumerator SplashRoutine(CompyaGameTracker tracker, int inning)
        {
            splashActive = true;
            FillSplash(tracker, inning);
            splashPanel.SetActive(true);
            yield return new WaitForSeconds(inningSplashSeconds);
            splashPanel.SetActive(false);
            splashActive = false;
            TryRelease();
        }

        // ---- 승부처 직접 플레이

        private void ShowChoice(PlayEvent evt, int index)
        {
            choiceActive = true;
            pendingChoiceIndex = index;
            pendingPlateAppearance = evt.PlateAppearance;
            FillChoice(evt, CompyaGameTracker.Build(events, index - 1));
            choicePanel.SetActive(true);
        }

        private void OnChoiceSkip()
        {
            resolvedChoices.Add(pendingPlateAppearance);
            choicePanel.SetActive(false);
            choiceActive = false;
            TryRelease();
        }

        private void OnChoicePlay()
        {
            choicePanel.SetActive(false);
            var evt = pendingChoiceIndex >= 0 && events != null && pendingChoiceIndex < events.Count ? events[pendingChoiceIndex] : null;
            FillDirect(evt, CompyaGameTracker.Build(events, pendingChoiceIndex - 1));
            directPanel.SetActive(true);
        }

        private void OnDirectPlayBall()
        {
            if (!choiceActive) return;
            StartCoroutine(DirectSwingRoutine(selectedTactic));
        }

        private IEnumerator DirectSwingRoutine(MatchTactic tactic)
        {
            directPlayButton.interactable = false;
            foreach (var button in tacticButtons) button.interactable = false;
            directSubtitleText.text = tactic == MatchTactic.None
                ? "투수, 와인드업... 던졌습니다!"
                : $"작전 [{MatchEngine.TacticLabel(tactic)}]! 투수, 와인드업... 던졌습니다!";

            // 공이 마운드에서 스트라이크 존으로 날아오는 연출(0.45초)
            directBall.gameObject.SetActive(true);
            var from = new Vector2(0.497f, 0.68f);
            var to = new Vector2(Random.Range(0.4f, 0.52f), Random.Range(0.42f, 0.56f));
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
            {
                var p = Vector2.Lerp(from, to, t);
                directBall.anchorMin = directBall.anchorMax = p;
                directBall.localScale = Vector3.one * Mathf.Lerp(0.35f, 1.15f, t);
                yield return null;
            }

            resolvedChoices.Add(pendingPlateAppearance);
            directPlayCount++;
            // [TASK-KBO-180] 작전을 실제 판정에 반영: 같은 시드로 경기를 다시 계산해 이 타석부터 결과를 바꾼다(이전 결과는 동일).
            if (tactic != MatchTactic.None && playBallController != null)
                playBallController.ReplayWithTactic(pendingPlateAppearance, tactic);
            awaitingDirectReveal = true;
            choiceActive = false;
            TryRelease(); // -> 해당 타석 이벤트 재생 -> HandleEventPlayed -> DirectResultRoutine
        }

        private IEnumerator DirectResultRoutine(PlayEvent evt)
        {
            directResultActive = true;
            directResultText.text = DirectResultHeadline(evt);
            directResultText.gameObject.SetActive(true);
            directSubtitleText.text = evt.LogMessage ?? "";
            yield return new WaitForSeconds(directResultSeconds);
            directResultText.gameObject.SetActive(false);
            directBall.gameObject.SetActive(false);
            directPanel.SetActive(false);
            directPlayButton.interactable = true;
            directResultActive = false;
            TryRelease();
        }

        private static string DirectResultHeadline(PlayEvent evt)
        {
            if (evt.Tactic == MatchTactic.Bunt && evt.Result == AtBatResult.Groundout) return "희생번트 성공";
            switch (evt.Result)
            {
                case AtBatResult.HomeRun: return "HOME RUN!";
                case AtBatResult.Triple: return "3루타!";
                case AtBatResult.Double: return "2루타!";
                case AtBatResult.Single: return "안타!";
                case AtBatResult.Walk: return "볼넷";
                case AtBatResult.Strikeout: return "삼진";
                case AtBatResult.Groundout: return "땅볼 아웃";
                default: return "뜬공 아웃";
            }
        }

        // ================================================================== 경기 종료

        private void HandleMatchCompleted(MatchResult result)
        {
            lastResult = result;
            finalTracker = CompyaGameTracker.Build(events, events != null ? events.Count - 1 : -1);
            paused = splashActive = choiceActive = directResultActive = awaitingDirectReveal = false;
            HideAll();
            FillResult1();
            result1Panel.SetActive(true);
        }

        private void HandleRewardGranted(MatchRewardResult reward)
        {
            lastReward = reward;
            if (result3Panel != null && result3Panel.activeSelf) FillResult3();
        }

        private void OnResult1Next()
        {
            HideAll();
            FillResult2();
            result2Panel.SetActive(true);
        }

        private void OnResult2Again()
        {
            ShowTypeSelect();
        }

        private void OnResult2Confirm()
        {
            HideAll();
            FillResult3();
            result3Panel.SetActive(true);
        }

        private void OnResult3Next()
        {
            HideAll();
            if (inGameUIController != null) inGameUIController.ReturnToLobby();
            else UIManager.Instance?.ShowScreen(ScreenType.Lobby);
        }

        // ================================================================== 공통 계산

        private void BuildBattingOrders()
        {
            awayOrder.Clear();
            homeOrder.Clear();
            var engine = playBallController != null ? playBallController.Engine : null;
            if (engine?.AwayBattingOrder != null) awayOrder.AddRange(engine.AwayBattingOrder.Where(p => p != null).Take(9));
            if (engine?.HomeBattingOrder != null) homeOrder.AddRange(engine.HomeBattingOrder.Where(p => p != null).Take(9));
            if (events == null) return;

            // 엔진 최종 타순과 실제 타석 순서가 다르면(대타 등) 타석 순서를 우선한다 - 각 팀 첫 9타석의 타자가 1~9번이다.
            int awayPa = 0, homePa = 0;
            foreach (var e in events)
            {
                if (e.Type != PlayEventType.AtBatResult || e.Batter == null) continue;
                var order = e.IsTopHalf ? awayOrder : homeOrder;
                int slot = (e.IsTopHalf ? awayPa++ : homePa++) % 9;
                if (order.Contains(e.Batter)) continue;
                while (order.Count <= slot) order.Add(null);
                order[slot] = e.Batter;
            }
        }

        private PlayEvent PeekNextAtBat(int afterIndex)
        {
            if (events == null) return null;
            for (int i = afterIndex + 1; i < events.Count; i++)
                if (events[i].Type == PlayEventType.AtBatResult) return events[i];
            return null;
        }

        /// <summary>실시간 승리 확률(원정 기준). 점수차와 경기 진행도로 로지스틱 근사 - 종반일수록 같은 점수차의 무게가 커진다.</summary>
        private static float AwayWinProbability(CompyaGameTracker t)
        {
            if (t.GameOver) return t.AwayScore > t.HomeScore ? 1f : t.AwayScore < t.HomeScore ? 0f : 0.5f;
            float progress = Mathf.Clamp01((t.Inning - 1 + (t.IsTopHalf ? 0f : 0.5f)) / 9f);
            float k = 0.35f + 1.6f * progress * progress;
            int diff = t.AwayScore - t.HomeScore;
            float p = 1f / (1f + Mathf.Exp(-k * diff)) - 0.04f * (1f - progress);
            return Mathf.Clamp(p, 0.01f, 0.99f);
        }

        /// <summary>반복과제 진행 수(레퍼런스 "N개 달성") - 우리 팀 안타 + 우리 팀 투수 탈삼진.</summary>
        private int AchievementCount(CompyaGameTracker t)
        {
            var user = LeagueManager.Instance != null ? LeagueManager.Instance.UserTeam : Team.None;
            bool userAway = awayTeam == user;
            var line = userAway ? t.Away : t.Home;
            var pitchers = userAway ? t.AwayPitchers : t.HomePitchers;
            return line.H + pitchers.Sum(p => t.PitchOf(p).Strikeouts);
        }

        public static string DisplayName(Player player)
        {
            if (player?.Template == null) return "-";
            int year = player.Template.SeasonYear;
            return year > 0 ? $"{player.Template.PlayerName}'{year % 100:00}" : player.Template.PlayerName;
        }

        public static string PositionLabel(Player player)
        {
            if (player?.Template == null) return "";
            if (player.Template.IsPitcher)
            {
                switch (player.Template.PitcherRole)
                {
                    case PitcherRole.StartingPitcher: return "SP";
                    case PitcherRole.Closer: return "CP";
                    default: return "RP";
                }
            }
            switch (player.Template.BatterPosition)
            {
                case BatterPosition.Catcher: return "C";
                case BatterPosition.FirstBase: return "1B";
                case BatterPosition.SecondBase: return "2B";
                case BatterPosition.ThirdBase: return "3B";
                case BatterPosition.ShortStop: return "SS";
                case BatterPosition.LeftField: return "LF";
                case BatterPosition.CenterField: return "CF";
                case BatterPosition.RightField: return "RF";
                default: return "DH";
            }
        }

        private static int Ovr(Player player) => player != null ? player.CalculateOVR(false) : 0;

        private static string InningLabel(int inning, bool top) => $"{inning}회{(top ? "초" : "말")}";
    }
}
