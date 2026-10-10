using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using KBOManager.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KBOManager.Controllers
{
    /// <summary>[TASK-GM-07] 한 경기 3단계 플로우 단계.</summary>
    public enum GMMatchStage
    {
        None = 0,
        PreGame = 1,      // ① 전력 분석(PreGameView)
        LiveInning = 2,   // ② 실시간 이닝 경기(LiveMatchInningView)
        PostGame = 3,     // ③ 경기 결과(PostGameBoxScoreView)
    }

    /// <summary>
    /// [TASK-GM-07] ② 실시간 이닝 경기(LiveMatchInningView, 1920×1080):
    ///   이닝 전광판(1~9회 · 연장 · R/H/E) · 현재 투타 매치업(주자 · 아웃 · 오늘 기록) · ABS 탄착군(마지막 타석 투구 위치 · 스트라이크/볼) ·
    ///   실시간 문자 중계 · 실시간 승리 확률(WPA) · 속도 제어(1타석 / 1이닝 / 고속 / 경기 끝까지) · 전술 개입(강공 / 작전 / 투수 교체 / 대타).
    ///   경기가 끝나면 [경기 결과 보기]로 ③ 박스스코어 · 4문단 기사 · WPA 그래프로 넘어간다.
    /// [TASK-GM-19] 전술 대시보드 개편 - 좌측 전담 응원 치어리더 포스터 컷 오버레이 · 투수 분석(코스별 피안타율 Hot/Cold 히트맵 + 마지막 타석 투구 위치 ·
    ///   오늘 투구수 · 체력 게이지) · 단장 지시 버튼([투수 교체 지시] · [대타 기용 지시] = 감독 신뢰도 기반 확률적 수용, 하프이닝당 종류별 1회).
    /// </summary>
    public partial class GMMatchPrePostUIController
    {
        public const string LiveRootName = "LiveInningRoot";
        public const int LiveDots = 10, LiveLogLines = 13;
        public const float AutoStepSeconds = 0.12f;

        private RectTransform liveRoot;
        private Text liveTitle, liveSituation, liveOuts, liveBatter, livePitcher, liveAbsSummary, liveLog, liveWpaLabel, liveMessage;
        private readonly Text[,] liveScore = new Text[3, MaxInnings + 4];
        private readonly RawImage[] liveLogos = new RawImage[2];
        private readonly Image[] liveBases = new Image[3];
        private readonly Image[] liveDots = new Image[LiveDots];
        private WpaLineGraphic liveWpa;
        private Button livePower, liveTactic, livePitching, livePinch, liveOneAtBat, liveOneInning, liveAuto, liveToEnd, liveResult;
        private GMLiveSeasonSimulator.GameSession session;
        private GMAwardEvaluator.GMPostseasonGame postseasonGame;
        private bool autoPlay;
        private float autoTimer;
        private int tacticCycle;
        private GMMatchStage stage;
        private bool liveAudioMuted;
        private GMLiveSeasonSimulator.GameSession audioSession;

        /// <summary>[TASK-GM-08] 마지막 타석의 사운드 연출 큐(없으면 null).</summary>
        public GMAudioCue? LastLiveCue { get; private set; }
        public GMAudioCue? LastExtraCue { get; private set; }       // [TASK-GM-12]
        public GMAudioEvent? LastResultEvent { get; private set; }  // [TASK-GM-12]
        private bool lateCloseUsed;

        // [TASK-GM-19] 전술 대시보드
        public const string CheerOverlayName = "CheerOverlayPanel";
        private static readonly Color Pink = new Color(1f, 0.6f, 0.82f);
        private const string StrikeHex = "#FF594D", BallHex = "#59A6FF";
        private Text liveCheerTitle, liveCheerName, liveCheerLine, liveCheerStats, liveCheerInitial;
        private RawImage liveCheerPhoto;
        private Image liveCheerFrame;
        private readonly Image[] liveHeat = new Image[GMLiveTactics.ZoneCells];
        private readonly Text[] liveHeatText = new Text[GMLiveTactics.ZoneCells];
        private readonly Image[] liveBatHeat = new Image[GMLiveTactics.ZoneCells];
        private readonly Text[] liveBatHeatText = new Text[GMLiveTactics.ZoneCells];
        private Text liveCheerShout, livePitcherZoneLabel, liveBatterZoneLabel;
        private Text liveZoneTitle, livePitchCount, liveStaminaLabel, liveStaminaNote, liveHeatLegend, liveOrderInfo;
        private RectTransform liveStaminaFill;
        private Image liveStaminaImage;
        private readonly HashSet<string> liveOrdersUsed = new HashSet<string>();

        /// <summary>[TASK-GM-19] 오늘 오버레이에 띄운 치어리더(전담 응원 → 없으면 엔트리 1번).</summary>
        public Cheerleader LiveCheerleader { get; private set; }
        /// <summary>[TASK-GM-19] 마지막 단장 지시 결과(true = 현장 수용 · 실행, false = 보류/실패, null = 지시 없음).</summary>
        public bool? LastOrderAccepted { get; private set; }
        public string LastOrderResult { get; private set; } = "";
        /// <summary>[TASK-GM-19] 현재 투수 코스별 피안타율(표시 중인 값)과 추정 여부.</summary>
        public float[] LiveZoneMap { get; private set; } = new float[GMLiveTactics.ZoneCells];
        public bool LiveZoneEstimated { get; private set; }
        /// <summary>[TASK-GM-19] 현재 타자 코스별 타율(참고 시안 - 투수 피안타율과 나란히).</summary>
        public float[] LiveBatterZoneMap { get; private set; } = new float[GMLiveTactics.ZoneCells];

        public RectTransform LiveRoot => liveRoot;
        public GMMatchStage Stage => stage;
        public GMLiveSeasonSimulator.GameSession Session => session;
        public bool IsAutoPlaying => autoPlay;
        public GMAwardEvaluator.GMPostseasonGame PostseasonGame => postseasonGame;
        public string LiveMessage => liveMessage != null ? liveMessage.text : "";

        private static readonly MatchTactic[] OffenseCycle = { MatchTactic.Bunt, MatchTactic.Steal, MatchTactic.ContactSwing };

        // ================================================================== 진입

        /// <summary>[TASK-GM-07] 포스트시즌 내 구단 경기 - 전력 분석부터 3단계로 진행한다.</summary>
        public bool ShowPostseasonPreGame(GMLiveSeasonSimulator sim, GMAwardEvaluator.GMPostseasonGame game)
        {
            if (sim == null || game == null || !game.IsUserGame) return false;
            simulator = sim;
            var p = GMMatchPreview.BuildPostseason(sim, game);
            if (p == null) return false;
            ShowPreGameView(p);
            postseasonGame = game;
            BindPreview();
            return true;
        }

        /// <summary>
        /// [플레이 볼] ① → ② - 정규시즌은 오늘 경기 실시간 세션(GMLiveSeasonSimulator.BeginLiveDay), 포스트시즌은 시리즈 경기 세션을 연다.
        /// 열 수 없으면(시즌 종료 · 인터럽트 대기) false.
        /// </summary>
        public bool PlayBall()
        {
            if (simulator == null) return false;
            if (postseasonGame == null && KBOManager.Services.GMStoveTurns.IsGating(simulator.League)) return false; // [TASK-GM-15] 스토브리그 8 Turn 진행 중 = 정규시즌 개막 차단
            if (session == null || session.IsFinished)
            {
                session = postseasonGame != null
                    ? GMAwardEvaluator.BeginPostseasonSession(simulator, postseasonGame)
                    : simulator.BeginLiveDay();
            }
            if (session == null) return false;
            if (liveRoot == null) Build();
            autoPlay = false;
            tacticCycle = 0;
            liveOrdersUsed.Clear(); // [TASK-GM-19]
            LastOrderAccepted = null;
            LastOrderResult = "";
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            preRoot.gameObject.SetActive(false);
            postRoot.gameObject.SetActive(false);
            liveRoot.gameObject.SetActive(true);
            stage = GMMatchStage.LiveInning;
            StartLiveAudio();
            SetLiveMessage(session.UserTeam != null ? $"플레이 볼! {(session.UserBatting ? "공격" : "수비")}부터 시작합니다. 속도를 고르거나 전술로 개입하십시오." : "플레이 볼!");
            BindLive();
            return true;
        }

        // ================================================================== 진행 · 개입

        public int LiveStepAtBat()
        {
            if (session == null || session.IsOver) return 0;
            int n = session.Step() != null ? 1 : 0;
            BindLive();
            return n;
        }

        public int LiveStepInning()
        {
            if (session == null || session.IsOver) return 0;
            int n = session.StepHalfInning();
            BindLive();
            return n;
        }

        public void LiveToEnd()
        {
            if (session == null) return;
            autoPlay = false;
            liveAudioMuted = true; // [TASK-GM-08] 경기 끝까지 즉시 진행 - 타석마다 응원가를 끊어 틀지 않는다
            session.PlayToEnd();
            liveAudioMuted = false;
            BindLive();
        }

        public void ToggleAutoPlay()
        {
            if (session == null || session.IsOver) { autoPlay = false; BindLive(); return; }
            autoPlay = !autoPlay;
            autoTimer = 0f;
            BindLive();
        }

        private void Update()
        {
            if (!autoPlay || session == null || stage != GMMatchStage.LiveInning) return;
            autoTimer += Time.unscaledDeltaTime;
            if (autoTimer < AutoStepSeconds) return;
            autoTimer = 0f;
            if (LiveStepAtBat() == 0 || session.IsOver) { autoPlay = false; BindLive(); }
        }

        /// <summary>[강공] - 공격이면 강공(파워 스윙), 수비면 정면 승부.</summary>
        public bool LivePowerTactic()
        {
            if (session == null) return false;
            bool ok = session.SetTactic(session.UserBatting ? MatchTactic.PowerSwing : MatchTactic.FullForce, out var msg);
            SetLiveMessage(msg);
            BindLive();
            return ok;
        }

        /// <summary>[작전] - 공격이면 번트 → 도루 → 컨택 순서로, 수비면 고의사구.</summary>
        public bool LiveSpecialTactic()
        {
            if (session == null) return false;
            MatchTactic tactic;
            if (session.UserBatting)
            {
                tactic = OffenseCycle[tacticCycle % OffenseCycle.Length];
                if (tactic == MatchTactic.Steal && !session.OnFirst) tactic = OffenseCycle[++tacticCycle % OffenseCycle.Length];
                tacticCycle++;
            }
            else tactic = MatchTactic.IntentionalWalk;
            bool ok = session.SetTactic(tactic, out var msg);
            SetLiveMessage(msg);
            BindLive();
            return ok;
        }

        public bool LivePitchingChange()
        {
            if (session == null) return false;
            bool ok = session.ChangePitcher(out var msg);
            SetLiveMessage(msg);
            BindLive();
            return ok;
        }

        public bool LivePinchHit()
        {
            if (session == null) return false;
            bool ok = session.PinchHit(out var msg);
            SetLiveMessage(msg);
            BindLive();
            return ok;
        }

        // ================================================================== [TASK-GM-19] 단장 지시(확률적 현장 수용)

        private string OrderKey(bool pitching) => $"{session.CurrentInning}|{session.NextIsTop}|{(pitching ? "P" : "H")}";

        /// <summary>이번 하프이닝에 같은 종류 지시를 이미 보냈는지.</summary>
        public bool OrderUsedThisHalf(bool pitching) => session != null && liveOrdersUsed.Contains(OrderKey(pitching));

        /// <summary>지금 지시를 보내면 현장이 받아들일 확률.</summary>
        public float LiveOrderChance(bool pitching) =>
            GMLiveTactics.OrderChance(session?.UserTeam, pitching, GMLiveTactics.StaminaRatio(session?.CurrentPitcher));

        /// <summary>[투수 교체 지시] - 내 구단 수비 때. 수용되면 불펜 최고 OVR 투수로 교체, 보류되면 현장 판단 유지.</summary>
        public bool LiveOrderPitchingChange(double? rollOverride = null) => IssueOrder(true, rollOverride);

        /// <summary>[대타 기용 지시] - 내 구단 공격 때. 수용되면 벤치 최고 OVR 타자를 다음 타석에.</summary>
        public bool LiveOrderPinchHit(double? rollOverride = null) => IssueOrder(false, rollOverride);

        private bool IssueOrder(bool pitching, double? rollOverride)
        {
            LastOrderAccepted = null;
            if (session == null || session.IsOver || session.UserTeam == null) { SetLiveMessage("경기가 끝났거나 내 구단 경기가 아닙니다."); BindLive(); return false; }
            if (pitching == session.UserBatting)
            {
                SetLiveMessage(pitching ? "내 구단 수비 때만 투수 교체를 지시할 수 있습니다." : "내 구단 공격 때만 대타 기용을 지시할 수 있습니다.");
                BindLive();
                return false;
            }
            string key = OrderKey(pitching);
            if (liveOrdersUsed.Contains(key)) { SetLiveMessage("이번 하프이닝에는 이미 같은 지시를 보냈습니다 - 현장 판단을 기다립니다."); BindLive(); return false; }
            liveOrdersUsed.Add(key);
            float chance = LiveOrderChance(pitching);
            double roll = rollOverride ?? GMLiveTactics.OrderRoll(simulator?.League?.Seed ?? 0, session.GameIndex, session.PlateAppearances, pitching);
            string label = pitching ? "투수 교체" : "대타 기용";
            if (roll >= chance)
            {
                LastOrderAccepted = false;
                LastOrderResult = $"[현장 보류] 감독이 {label} 지시를 보류했습니다(수용 확률 {chance * 100f:0}%) - 현장 판단을 유지합니다.";
                session.PlayByPlay.Add($"[단장 지시] {label} - 현장 보류");
                SetLiveMessage(LastOrderResult);
                BindLive();
                return false;
            }
            string msg;
            bool ok = pitching ? session.ChangePitcher(out msg) : session.PinchHit(out msg);
            LastOrderAccepted = ok;
            LastOrderResult = ok ? $"[현장 수용] 감독이 {label} 지시를 받아들였습니다(수용 확률 {chance * 100f:0}%) - {msg}"
                                 : $"[현장 수용] 지시는 받아들였지만 실행할 수 없습니다 - {msg}";
            SetLiveMessage(LastOrderResult);
            BindLive();
            return ok;
        }

        /// <summary>② → ③ - 남은 타석을 끝내고 기록을 반영한 뒤 경기 결과(박스스코어 · 기사 · WPA)를 띄운다.</summary>
        public bool FinishLiveMatch()
        {
            if (session == null || simulator == null) return false;
            autoPlay = false;
            liveAudioMuted = true;
            session.PlayToEnd();
            liveAudioMuted = false;
            DetachLiveAudio(); // [TASK-GM-08] 응원 믹스 정리(승리 응원가는 결과 화면에서도 이어진다)
            GMMatchBoxScoreData result;
            if (postseasonGame != null)
            {
                result = GMAwardEvaluator.CompletePostseasonSession(simulator, postseasonGame, session);
                postseasonGame = null;
            }
            else
            {
                result = simulator.FinishLiveDay();
                simulator.Stop();
            }
            session = null;
            if (result == null) { CloseAll(); return false; }
            ShowPostGameBoxScoreView(result);
            PlayResultAudio(result); // [TASK-GM-12]
            return true;
        }

        // ================================================================== [TASK-GM-08] 구단 사운드 연출

        /// <summary>
        /// 경기 시작 - 관중 앰비언스 · 치어리더 단상 믹스(엔트리 4~6인 + 리더십 버프) · 타석 이벤트 구독.
        /// [TASK-GM-18] 경기 중 BGM 교체 완전 차단 - GMAudioManager.LiveMatchQuiet을 켜 이벤트 하이재킹 · 응원가 · 화면 BGM 전환을 모두 막고,
        /// 타석마다 짧은 효과음(득점 · 안타 = 환호, 수비 아웃 = 박수)만 낸다.
        /// </summary>
        private void StartLiveAudio()
        {
            if (session == null) return;
            DetachLiveAudio();
            var audio = GMAudioManager.Ensure();
            var user = session.UserTeam;
            if (audio.ActiveEvent.HasValue) audio.EndEvent(); // 경기 전 이벤트곡(결과 BGM 등)이 남아 있으면 정리 - 그 뒤로는 앰비언스 고정
            audio.PlayMatchAmbience(user?.TeamCode);
            GMAudioManager.LiveMatchQuiet = true;
            if (user != null) audio.SetCheerleaderMix(GMCheerleaderRules.EntryCount(user.CheerEntry), user.CheerLeadershipBuff > 0);
            audioSession = session;
            audioSession.OnStepped += OnLiveStep;
            LastLiveCue = null;
            LastExtraCue = null;
            lateCloseUsed = false;
        }

        /// <summary>[TASK-GM-12 → GM-18] ① 전력 분석 진입 - 경기 화면 그룹(관중 앰비언스)으로만 옮긴다(라인업송 하이재킹 폐지 - 플레이 볼에서 곡이 바뀌지 않는다).</summary>
        private void PlayPreGameAudio()
        {
            string user = simulator?.League?.SelectedTeamCode;
            if (string.IsNullOrEmpty(user)) return;
            var audio = GMAudioManager.Ensure();
            if (audio.ActiveEvent.HasValue) audio.EndEvent();
            audio.EnterScreen(GMAudioScreen.Match, user);
        }

        /// <summary>[TASK-GM-12] ③ 경기 결과(경기 종료 후 1회) - 승리 = 승리의 라이온즈(대승 → 엘도라도), 패배 = 공통 패배 BGM.</summary>
        private void PlayResultAudio(GMMatchBoxScoreData result)
        {
            GMAudioManager.LiveMatchQuiet = false;
            string user = simulator?.League?.SelectedTeamCode;
            if (result == null || string.IsNullOrEmpty(user) || (result.HomeCode != user && result.AwayCode != user)) return;
            bool home = result.HomeCode == user;
            var ev = GMLiveAudioDirector.ResultEventFor(home ? result.HomeR : result.AwayR, home ? result.AwayR : result.HomeR);
            LastResultEvent = ev;
            if (!ev.HasValue) return;
            var audio = GMAudioManager.Ensure();
            audio.StopMatchAudio();
            audio.PlayEvent(ev.Value, user);
        }

        private void DetachLiveAudio()
        {
            if (audioSession != null) audioSession.OnStepped -= OnLiveStep;
            audioSession = null;
            GMAudioManager.LiveMatchQuiet = false; // [TASK-GM-18]
            var audio = GMAudioManager.Instance;
            if (audio != null) audio.SetCheerleaderMix(0, false);
        }

        /// <summary>
        /// 타석 1회 → 내 구단 기준 상황 판정(GMLiveAudioDirector.CueFor - 기록 · 해설용) → [TASK-GM-18] 효과음만 재생(SfxFor).
        /// 응원가 · 아웃송 · 빅이닝/접전 곡은 더 이상 경기 중에 틀지 않는다(BGM 혼선 원인).
        /// </summary>
        private void OnLiveStep(AtBatStepResult step, GMLiveSeasonSimulator.StepContext ctx)
        {
            if (liveAudioMuted || audioSession == null || step == null || ctx == null) return;
            var user = audioSession.UserTeam;
            if (user == null) return;
            bool userAway = user == audioSession.Away;
            bool userBatting = step.IsTopHalf == userAway;
            int userBefore = userAway ? ctx.AwayBefore : ctx.HomeBefore, oppBefore = userAway ? ctx.HomeBefore : ctx.AwayBefore;
            int userAfter = userAway ? step.AwayScore : step.HomeScore;
            LastLiveCue = GMLiveAudioDirector.CueFor(step, userBatting, ctx.RispBefore, ctx.HalfRuns, userBefore, oppBefore, step.GameEnded && userAfter > (userAway ? step.HomeScore : step.AwayScore));
            LastExtraCue = GMLiveAudioDirector.SfxFor(step, userBatting);
            if (LastExtraCue.HasValue) GMAudioManager.Ensure().PlayCue(LastExtraCue.Value, user.TeamCode, ctx.Inning);
        }

        private void SetLiveMessage(string text)
        {
            if (liveMessage != null) liveMessage.text = text ?? "";
        }

        // ================================================================== 조립

        private void BuildLive()
        {
            liveRoot = CompyaUiKit.Fill(transform, LiveRootName);
            CompyaUiKit.Paint(liveRoot, Bg, true);
            liveTitle = L(liveRoot, "LiveTitle", "", 20, 8, 1500, 54, TitlePt - 2, TextAnchor.MiddleLeft, Gold);
            liveSituation = L(liveRoot, "LiveSituation", "", 1510, 8, 1900, 54, BodyPt + 2, TextAnchor.MiddleRight, White);

            // 이닝 전광판
            CompyaUiKit.Box(liveRoot, "LiveScorePanel", 12, 60, 1908, 222, Panel);
            for (int r = 0; r < 3; r++)
            {
                float y0 = LiveLineY0[r], y1 = LiveLineY1[r];
                if (r > 0) liveLogos[r - 1] = CompyaUiKit.Logo(liveRoot, $"LiveLogo{r}", 20, y0 + 4, 70, y1 - 4);
                for (int c = 0; c < MaxInnings + 4; c++)
                    liveScore[r, c] = L(liveRoot, $"LS{r}_{c}", "", 0, y0, 10, y1, r == 0 ? SmallPt : BodyPt, c == 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter, r == 0 ? Muted : White);
            }

            // [TASK-GM-19] 전담 응원 치어리더 포스터 컷(좌측 세로 스트립)
            var cheer = CompyaUiKit.Fill(liveRoot, CheerOverlayName);
            CompyaUiKit.Box(cheer, "Bg", 12, 230, 300, 948, new Color(1f, 0.55f, 0.8f, 0.08f));
            CompyaUiKit.Box(cheer, "Accent", 12, 230, 300, 234, Pink);
            liveCheerTitle = L(cheer, "CheerTitle", "오늘의 전담 응원", 24, 238, 290, 272, BodyPt, TextAnchor.MiddleLeft, Pink);
            liveCheerFrame = CompyaUiKit.Box(cheer, "CheerFrame", 24, 278, 288, 752, new Color(0f, 0f, 0f, 0.35f));
            var mask = CompyaUiKit.Norm(liveCheerFrame.transform, "PhotoMask", 0f, 0f, 1f, 1f);
            mask.gameObject.AddComponent<RectMask2D>();
            var photo = CompyaUiKit.Fill(mask, "CheerPhoto");
            liveCheerPhoto = photo.gameObject.AddComponent<RawImage>();
            liveCheerPhoto.raycastTarget = false;
            liveCheerPhoto.color = new Color(0f, 0f, 0f, 0f);
            var fitter = photo.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = 0.7f;
            CompyaUiKit.Outline(liveCheerFrame, Pink, 2f);
            liveCheerInitial = TextTidy.Exact(kit.LabelOn(CompyaUiKit.Fill(liveCheerFrame.transform, "CheerInitial"), "", (TitlePt + 18) / 0.9f, TextAnchor.MiddleCenter, White), TitlePt + 18); // 대형 수치 상한 42pt
            var bubble = CompyaUiKit.Norm(liveCheerFrame.transform, "ShoutBubble", 0.06f, 0.86f, 0.94f, 0.98f);
            CompyaUiKit.Paint(bubble, new Color(1f, 1f, 1f, 0.92f));
            CompyaUiKit.Outline(bubble.GetComponent<Image>(), Pink, 2f);
            liveCheerShout = TextTidy.Exact(kit.LabelOn(CompyaUiKit.Fill(bubble, "CheerShout"), "", (BodyPt + 2) / 0.9f, TextAnchor.MiddleCenter, new Color(0.75f, 0.12f, 0.42f)), BodyPt + 2);
            liveCheerName = L(cheer, "CheerName", "", 24, 760, 288, 800, NamePt, TextAnchor.MiddleCenter, White);
            liveCheerLine = L(cheer, "CheerLine", "", 24, 804, 288, 872, SmallPt, TextAnchor.UpperCenter, Muted);
            liveCheerStats = L(cheer, "CheerStats", "", 24, 876, 288, 944, SmallPt, TextAnchor.UpperCenter, Gold);

            // 투타 매치업
            var match = CompyaUiKit.Fill(liveRoot, "MatchupPanel");
            CompyaUiKit.Box(match, "Bg", 312, 230, 760, 700, Panel);
            L(match, "MatchupTitle", "현재 투타 매치업", 324, 234, 750, 272, BodyPt + 2, TextAnchor.MiddleLeft, Gold);
            var diamond = CompyaUiKit.Place(match, "Diamond", 600, 284, 750, 420);
            liveBases[1] = BaseSquare(diamond, "Base2", 0.4f, 0.62f);
            liveBases[0] = BaseSquare(diamond, "Base1", 0.72f, 0.32f);
            liveBases[2] = BaseSquare(diamond, "Base3", 0.08f, 0.32f);
            BaseSquare(diamond, "Home", 0.4f, 0.02f).color = new Color(1f, 1f, 1f, 0.35f);
            liveOuts = L(match, "LiveOuts", "", 324, 280, 592, 420, BodyPt, TextAnchor.UpperLeft, White);
            liveOuts.lineSpacing = 1.1f;
            liveBatter = L(match, "LiveBatter", "", 324, 430, 750, 560, BodyPt, TextAnchor.UpperLeft, White);
            livePitcher = L(match, "LivePitcher", "", 324, 566, 750, 696, BodyPt, TextAnchor.UpperLeft, White);
            liveBatter.lineSpacing = livePitcher.lineSpacing = 1.1f;
            liveBatter.supportRichText = livePitcher.supportRichText = true; // [TASK-GM-19] 영구결번 이름 황금색

            // [TASK-GM-19] 투수 · 타자 분석(참고 시안) - 코스별 피안타율(투수 · 마지막 타석 투구 위치 겹침) + 코스별 타율(타자) · 투구수 · 체력 게이지
            var abs = CompyaUiKit.Fill(liveRoot, "AbsZonePanel");
            CompyaUiKit.Box(abs, "Bg", 772, 230, 1300, 700, Panel);
            liveZoneTitle = L(abs, "AbsTitle", "투수 · 타자 분석 (코스별)", 784, 234, 1290, 272, BodyPt + 2, TextAnchor.MiddleLeft, Gold);
            livePitcherZoneLabel = L(abs, "PitcherZoneLabel", "코스별 피안타율", 784, 276, 1004, 302, SmallPt, TextAnchor.MiddleCenter, Muted);
            liveBatterZoneLabel = L(abs, "BatterZoneLabel", "코스별 타율", 1056, 276, 1276, 302, SmallPt, TextAnchor.MiddleCenter, Muted);
            var area = BuildHeatmap(abs, "PitchArea", 784, 306, 1004, 526, liveHeat, liveHeatText, "HeatValue");
            for (int i = 0; i < LiveDots; i++)
            {
                var dot = CompyaUiKit.Norm(area, $"Pitch{i}", 0.5f, 0.5f, 0.5f, 0.5f);
                dot.sizeDelta = new Vector2(16f, 16f);
                liveDots[i] = CompyaUiKit.Paint(dot, Color.white);
                CompyaUiKit.Outline(liveDots[i], new Color(0f, 0f, 0f, 0.6f), 1.5f);
                dot.gameObject.SetActive(false);
            }
            BuildHeatmap(abs, "BatterArea", 1056, 306, 1276, 526, liveBatHeat, liveBatHeatText, "BatHeatValue");
            L(abs, "PitchCountLabel", "오늘 투구수", 784, 532, 900, 556, SmallPt, TextAnchor.MiddleLeft, Muted);
            livePitchCount = L(abs, "PitchCountValue", "", 784, 558, 900, 606, TitlePt + 8, TextAnchor.MiddleLeft, White);
            liveStaminaLabel = L(abs, "StaminaLabel", "", 912, 532, 1290, 556, SmallPt, TextAnchor.MiddleLeft, Muted);
            var barBg = CompyaUiKit.Box(abs, "StaminaBar", 912, 562, 1290, 578, new Color(1f, 1f, 1f, 0.12f));
            liveStaminaFill = CompyaUiKit.Norm(barBg.transform, "Fill", 0f, 0f, 1f, 1f);
            liveStaminaImage = CompyaUiKit.Paint(liveStaminaFill, Green);
            liveStaminaNote = L(abs, "StaminaNote", "", 912, 582, 1290, 606, SmallPt, TextAnchor.MiddleLeft, White);
            liveAbsSummary = L(abs, "AbsSummary", "", 784, 610, 1290, 650, SmallPt, TextAnchor.MiddleLeft, White);
            liveAbsSummary.supportRichText = true;
            liveHeatLegend = L(abs, "HeatLegend", HeatLegendText(), 784, 654, 1290, 696, SmallPt, TextAnchor.MiddleLeft, Muted);
            liveHeatLegend.supportRichText = true;

            // 문자 중계
            var log = CompyaUiKit.Fill(liveRoot, "PlayByPlayPanel");
            CompyaUiKit.Box(log, "Bg", 1312, 230, 1908, 700, Panel);
            L(log, "LogTitle", "실시간 문자 중계", 1324, 234, 1896, 272, BodyPt + 2, TextAnchor.MiddleLeft, Gold);
            liveLog = L(log, "LiveLog", "", 1324, 278, 1896, 696, SmallPt, TextAnchor.UpperLeft, White);
            liveLog.lineSpacing = 1.05f;
            liveLog.verticalOverflow = VerticalWrapMode.Truncate;

            // 승리 확률
            var wp = CompyaUiKit.Fill(liveRoot, "WinProbabilityPanel");
            CompyaUiKit.Box(wp, "Bg", 312, 708, 1100, 948, Panel);
            liveWpaLabel = L(wp, "WpaLabel", "", 324, 712, 1090, 748, BodyPt, TextAnchor.MiddleLeft, Gold);
            var wArea = CompyaUiKit.Box(wp, "WpaArea", 330, 752, 1080, 940, new Color(0f, 0f, 0f, 0.3f));
            var mid = CompyaUiKit.Norm(wArea.transform, "Baseline", 0f, 0.497f, 1f, 0.503f);
            CompyaUiKit.Paint(mid, new Color(1f, 1f, 1f, 0.45f));
            var lineRect = CompyaUiKit.Fill(wArea.transform, "WpaLine");
            liveWpa = lineRect.gameObject.AddComponent<WpaLineGraphic>();
            liveWpa.color = new Color(0.4f, 0.85f, 1f);

            // 전술 개입 · 단장 지시
            var tac = CompyaUiKit.Fill(liveRoot, "TacticsPanel");
            CompyaUiKit.Box(tac, "Bg", 1112, 708, 1908, 948, Panel);
            L(tac, "TacticsTitle", "전술 개입 (다음 타석) · 단장 지시", 1124, 712, 1896, 748, BodyPt + 2, TextAnchor.MiddleLeft, Gold);
            livePower = Btn(tac, "PowerButton", "강공", 1124, 756, 1300, 816, new Color(0.7f, 0.25f, 0.2f), ButtonPt);
            liveTactic = Btn(tac, "TacticButton", "작전 ▶", 1310, 756, 1500, 816, new Color(0.25f, 0.45f, 0.75f), ButtonPt);
            livePitching = Btn(tac, "PitchingButton", "투수 교체 지시", 1510, 756, 1700, 816, new Color(0.2f, 0.55f, 0.4f), ButtonPt);
            livePinch = Btn(tac, "PinchButton", "대타 기용 지시", 1710, 756, 1896, 816, new Color(0.55f, 0.4f, 0.15f), ButtonPt);
            livePower.onClick.AddListener(() => LivePowerTactic());
            liveTactic.onClick.AddListener(() => LiveSpecialTactic());
            livePitching.onClick.AddListener(() => LiveOrderPitchingChange()); // [TASK-GM-19] 현장에 지시(확률적 수용)
            livePinch.onClick.AddListener(() => LiveOrderPinchHit());
            liveMessage = L(tac, "LiveMessage", "", 1124, 822, 1896, 900, SmallPt + 1, TextAnchor.UpperLeft, Green);
            liveOrderInfo = L(tac, "OrderInfo", "", 1124, 904, 1896, 944, SmallPt, TextAnchor.MiddleLeft, Muted);

            // 속도 제어
            liveOneAtBat = Btn(liveRoot, "OneAtBatButton", "1타석 진행", 12, 958, 380, 1066, ButtonOn, ButtonPt + 1);
            liveOneInning = Btn(liveRoot, "OneInningButton", "1이닝 진행", 392, 958, 760, 1066, ButtonOn, ButtonPt + 1);
            liveAuto = Btn(liveRoot, "AutoButton", "고속 진행 ▶", 772, 958, 1140, 1066, new Color(0.45f, 0.3f, 0.7f), ButtonPt + 1);
            liveToEnd = Btn(liveRoot, "ToEndButton", "경기 끝까지", 1152, 958, 1520, 1066, ButtonIdle, ButtonPt + 1);
            liveResult = Btn(liveRoot, "ResultButton", "경기 결과 보기", 1532, 958, 1908, 1066, new Color(0.17f, 0.62f, 0.25f), ButtonPt + 1);
            liveOneAtBat.onClick.AddListener(() => LiveStepAtBat());
            liveOneInning.onClick.AddListener(() => LiveStepInning());
            liveAuto.onClick.AddListener(ToggleAutoPlay);
            liveToEnd.onClick.AddListener(LiveToEnd);
            liveResult.onClick.AddListener(() => FinishLiveMatch());
        }

        // ---- [TASK-GM-19] 히트맵 색 · 범례(색 이름 글자 대신 색 견본)

        private static readonly Color HeatCold = new Color(0.24f, 0.5f, 0.91f), HeatMid = new Color(0.95f, 0.7f, 0.24f), HeatHot = new Color(0.91f, 0.27f, 0.24f);
        public const float HeatColdBaa = 0.2f, HeatHotBaa = 0.32f;

        public static Color HeatColor(float baa)
        {
            float t = Mathf.Clamp01((baa - HeatColdBaa) / (HeatHotBaa - HeatColdBaa));
            var c = t < 0.5f ? Color.Lerp(HeatCold, HeatMid, t * 2f) : Color.Lerp(HeatMid, HeatHot, (t - 0.5f) * 2f);
            c.a = 0.82f;
            return c;
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>[TASK-GM-19] 3×3 코스 히트맵(바깥 = 볼 존 · 안 = 스트라이크 존 9칸) - 칸 값 텍스트는 패널 직속(겹침 검사 대상)으로 둔다.</summary>
        private RectTransform BuildHeatmap(RectTransform panel, string name, float x0, float y0, float x1, float y1, Image[] cells, Text[] values, string valueName)
        {
            var area = CompyaUiKit.Place(panel, name, x0, y0, x1, y1);
            CompyaUiKit.Paint(area, new Color(0f, 0f, 0f, 0.35f));
            var zone = CompyaUiKit.Norm(area, "StrikeZone", 1f / 6f, 1f / 6f, 5f / 6f, 5f / 6f);
            CompyaUiKit.Paint(zone, new Color(1f, 1f, 1f, 0.04f));
            for (int i = 0; i < GMLiveTactics.ZoneCells; i++)
            {
                int row = i / 3, col = i % 3;
                var cell = CompyaUiKit.Norm(zone, $"Heat{i}", col / 3f + 0.006f, 1f - (row + 1) / 3f + 0.006f, (col + 1) / 3f - 0.006f, 1f - row / 3f - 0.006f);
                cells[i] = CompyaUiKit.Paint(cell, new Color(1f, 1f, 1f, 0.1f));
            }
            CompyaUiKit.Outline(zone.GetComponent<Image>(), new Color(1f, 1f, 1f, 0.75f), 2f);
            float zx0 = x0 + (x1 - x0) / 6f, zy0 = y0 + (y1 - y0) / 6f, cw = (x1 - x0) * (4f / 6f) / 3f, ch = (y1 - y0) * (4f / 6f) / 3f;
            for (int i = 0; i < GMLiveTactics.ZoneCells; i++)
            {
                int row = i / 3, col = i % 3;
                values[i] = L(panel, $"{valueName}{i}", "", zx0 + col * cw, zy0 + row * ch, zx0 + (col + 1) * cw, zy0 + (row + 1) * ch, SmallPt, TextAnchor.MiddleCenter, White);
            }
            return area;
        }

        private static string Baa(float v) => v >= 1f ? "1.000" : v.ToString(".000", System.Globalization.CultureInfo.InvariantCulture);

        private static string HeatLegendText() =>
            $"<color={Hex(HeatHot)}>■</color> 뜨거운 코스(.320+)  <color={Hex(HeatMid)}>■</color> 보통  <color={Hex(HeatCold)}>■</color> 차가운 코스(.200-)";

        private static readonly float[] LiveLineY0 = { 64, 110, 166 }, LiveLineY1 = { 106, 162, 218 };

        private static Image BaseSquare(RectTransform parent, string name, float x, float y)
        {
            var r = CompyaUiKit.Norm(parent, name, x, y, x + 0.2f, y + 0.3f);
            r.localRotation = Quaternion.Euler(0f, 0f, 45f);
            return CompyaUiKit.Paint(r, new Color(1f, 1f, 1f, 0.2f));
        }

        // ================================================================== 바인딩

        private void BindLive()
        {
            if (liveRoot == null || session == null) return;
            var s = session;
            string away = NameAliasTable.DisplayTeamName(s.Away.TeamCode), home = NameAliasTable.DisplayTeamName(s.Home.TeamCode);
            liveTitle.text = $"KBO LIVE · {(s.IsPostSeason ? s.GameTitle + " · " : "")}{away} vs {home} · {s.DateLabel} {CompyaUiKit.Stadium(s.Home.Team)}";
            if (s.IsOver) liveSituation.text = $"경기 종료 {s.AwayScore} : {s.HomeScore}";
            else
            {
                bool top = s.NextIsTop;
                var last = s.LastStep;
                bool fresh = last == null || last.HalfInningEnded;
                liveSituation.text = $"{s.CurrentInning}회{(top ? "초" : "말")} {(fresh ? 0 : s.Outs)}사 · {s.AwayScore} : {s.HomeScore}";
            }
            using (CompyaUiKit.Wide()) BindLiveScore();

            bool newHalf = s.LastStep == null || s.LastStep.HalfInningEnded;
            int outs = newHalf || s.IsOver ? 0 : s.Outs;
            bool r1 = !newHalf && s.OnFirst, r2 = !newHalf && s.OnSecond, r3 = !newHalf && s.OnThird;
            liveBases[0].color = r1 ? Gold : new Color(1f, 1f, 1f, 0.2f);
            liveBases[1].color = r2 ? Gold : new Color(1f, 1f, 1f, 0.2f);
            liveBases[2].color = r3 ? Gold : new Color(1f, 1f, 1f, 0.2f);
            string runners = r1 || r2 || r3 ? string.Join(" · ", new[] { r1 ? "1루" : null, r2 ? "2루" : null, r3 ? "3루" : null }.Where(x => x != null)) : "주자 없음";
            liveOuts.text = $"아웃 {new string('●', outs)}{new string('○', 3 - outs)}\n{runners}\n공격 {(s.NextIsTop ? CompyaUiKit.ShortName(s.Away.Team) : CompyaUiKit.ShortName(s.Home.Team))}" +
                            $"{(s.UserTeam != null ? (s.UserBatting ? " (내 구단 공격)" : " (내 구단 수비)") : "")}";

            var batter = s.IsOver ? s.LastStep?.Batter : s.UpcomingBatter;
            var pitcher = s.IsOver ? s.LastStep?.Pitcher : s.CurrentPitcher;
            var league = simulator.League;
            if (batter != null)
            {
                league.Stats.TryGetValue(batter.InstanceId, out var st);
                liveBatter.text = $"타자 {GMRetirement.NameRich(league, batter)} ({GMLiveSeasonSimulator.PositionShort(batter.Template.BatterPosition)} · OVR {batter.GetEffectiveOverall()})\n" +
                                  $"시즌 {(st != null ? $"타율 {(st.AVG >= 1 ? st.AVG.ToString("0.000") : st.AVG.ToString(".000"))} · {st.HR}홈런 · {st.RBI}타점" : "기록 없음")}\n{s.TodayBatting(batter)}";
            }
            else liveBatter.text = "타자 -";
            if (pitcher != null)
            {
                league.Stats.TryGetValue(pitcher.InstanceId, out var pt);
                livePitcher.text = $"투수 {GMRetirement.NameRich(league, pitcher)} (OVR {pitcher.GetEffectiveOverall()} · ABS {pitcher.ABSZoneSkill})\n" +
                                   $"시즌 {(pt != null && pt.PG > 0 ? $"{pt.W}승 {pt.L}패 · ERA {pt.ERA:0.00}" : "첫 등판")}\n{s.TodayPitching(pitcher)}";
            }
            else livePitcher.text = "투수 -";

            var pitches = s.LastPitchLocations();
            for (int i = 0; i < LiveDots; i++)
            {
                bool on = i < pitches.Count;
                liveDots[i].gameObject.SetActive(on);
                if (!on) continue;
                var (x, y, strike) = pitches[i];
                var rect = liveDots[i].rectTransform;
                float nx = 0.5f + x / 3f, ny = 0.5f + y / 3f;
                rect.anchorMin = rect.anchorMax = new Vector2(nx, ny);
                rect.anchoredPosition = Vector2.zero;
                liveDots[i].color = strike ? new Color(1f, 0.35f, 0.3f) : new Color(0.35f, 0.65f, 1f);
            }
            int strikes = pitches.Count(p => p.strike);
            liveAbsSummary.text = pitches.Count == 0 ? $"첫 투구 전 · <color={StrikeHex}>●</color> 스트라이크 · <color={BallHex}>●</color> 볼"
                : $"마지막 타석 투구 {pitches.Count}개 · <color={StrikeHex}>●</color> 스트라이크 {strikes} · <color={BallHex}>●</color> 볼 {pitches.Count - strikes}";
            BindLiveZone(s, pitcher); // [TASK-GM-19]
            BindLiveCheer(s);

            var lines = s.PlayByPlay.Skip(Math.Max(0, s.PlayByPlay.Count - LiveLogLines)).Reverse();
            liveLog.text = s.PlayByPlay.Count == 0 ? "경기 시작 전입니다." : string.Join("\n", lines);

            var pts = s.Wpa;
            int n = Math.Max(1, pts.Count - 1);
            liveWpa.SetPoints(pts.Select((p, i) => new Vector2(i / (float)n, p.HomeWinProbability)), 4f);
            float homeWp = s.HomeWinProbabilityNow;
            liveWpaLabel.text = $"실시간 승리 확률 · {CompyaUiKit.ShortName(s.Home.Team)} {homeWp * 100f:0}% / {CompyaUiKit.ShortName(s.Away.Team)} {(1f - homeWp) * 100f:0}%";

            bool live = !s.IsOver;
            bool user = s.UserTeam != null && live;
            livePower.interactable = user;
            liveTactic.interactable = user;
            livePitching.interactable = user && !s.UserBatting && !OrderUsedThisHalf(true);   // [TASK-GM-19] 하프이닝당 지시 1회
            livePinch.interactable = user && s.UserBatting && !OrderUsedThisHalf(false);
            if (s.UserTeam != null && live)
            {
                float pc = LiveOrderChance(true), ph = LiveOrderChance(false);
                liveOrderInfo.text = $"감독 신뢰도 {s.UserTeam.ManagerTrust} · 지시 수용 확률 투수 교체 {pc * 100f:0}% / 대타 {ph * 100f:0}% · 하프이닝당 1회";
            }
            else liveOrderInfo.text = s.UserTeam != null ? "경기 종료 - 단장 지시 마감" : "중립 경기 - 단장 지시 불가";
            CompyaUiKit.SetButtonText(livePower, s.UserBatting ? "강공" : "강공 (정면 승부)");
            CompyaUiKit.SetButtonText(liveTactic, s.UserBatting ? $"작전 ▶ {GMLiveSeasonSimulator.MatchTacticLabel(OffenseCycle[tacticCycle % OffenseCycle.Length])}" : "작전 ▶ 고의사구");
            liveOneAtBat.interactable = live;
            liveOneInning.interactable = live;
            liveAuto.interactable = live;
            liveToEnd.interactable = live;
            CompyaUiKit.SetButtonText(liveAuto, autoPlay ? "고속 일시정지 ■" : "고속 진행 ▶");
            liveResult.interactable = s.IsOver;
        }

        /// <summary>[TASK-GM-19] 투수 · 타자 분석 - 코스별 피안타율(투수) · 코스별 타율(타자) 히트맵 · 오늘 투구수 · 체력 게이지 · 응원 말풍선.</summary>
        private void BindLiveZone(GMLiveSeasonSimulator.GameSession s, Player pitcher)
        {
            var league = simulator.League;
            var map = GMLiveTactics.ZoneMap(league, pitcher, out bool estimated);
            LiveZoneMap = map;
            LiveZoneEstimated = estimated;
            var batter = s.IsOver ? s.LastStep?.Batter : s.UpcomingBatter;
            var bat = GMLiveTactics.BatterZoneMap(league, batter, out bool batEstimated);
            LiveBatterZoneMap = bat;
            liveZoneTitle.text = $"투수 · 타자 분석 (코스별){(estimated || batEstimated ? " · 추정 포함" : " · 시즌 기록")}";
            livePitcherZoneLabel.text = pitcher?.Template != null ? $"피안타율 · {pitcher.Template.PlayerName}" : "코스별 피안타율";
            liveBatterZoneLabel.text = batter?.Template != null ? $"타율 · {batter.Template.PlayerName}" : "코스별 타율";
            for (int i = 0; i < GMLiveTactics.ZoneCells; i++)
            {
                liveHeat[i].color = pitcher != null ? HeatColor(map[i]) : new Color(1f, 1f, 1f, 0.08f);
                liveHeatText[i].text = pitcher != null ? Baa(map[i]) : "";
                liveBatHeat[i].color = batter != null ? HeatColor(bat[i]) : new Color(1f, 1f, 1f, 0.08f);
                liveBatHeatText[i].text = batter != null ? Baa(bat[i]) : "";
            }
            int np = s.TodayPitchCount(pitcher);
            float ratio = GMLiveTactics.StaminaRatio(pitcher);
            var (bf, hits, walks) = s.TodayFaced(pitcher);
            livePitchCount.text = pitcher != null ? np.ToString() : "-";
            liveStaminaLabel.text = pitcher != null ? $"체력 {ratio * 100f:0}% · {GMLiveTactics.StaminaNote(ratio)}" : "체력 -";
            liveStaminaFill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            var tone = ratio < GMLiveTactics.HookRatio ? new Color(1f, 0.45f, 0.25f) : ratio < GMLiveTactics.TiredRatio ? Gold : Green;
            liveStaminaImage.color = tone;
            liveStaminaLabel.color = ratio < GMLiveTactics.HookRatio ? tone : Muted;
            liveStaminaNote.text = pitcher == null ? "" : $"오늘 {bf}타자 · 피안타 {hits} · 볼넷 {walks}";

            if (liveCheerShout != null)
            {
                var user = s.UserTeam;
                bool userAway = user != null && user == s.Away;
                int mine = user == null ? s.HomeScore : userAway ? s.AwayScore : s.HomeScore, theirs = user == null ? s.AwayScore : userAway ? s.HomeScore : s.AwayScore;
                bool fresh = s.LastStep == null || s.LastStep.HalfInningEnded;
                liveCheerShout.text = s.IsOver ? (mine > theirs ? "승리의 함성!" : "다음엔 이긴다!")
                    : GMLiveTactics.CheerShout(s.UserBatting, !fresh && (s.OnSecond || s.OnThird), mine, theirs, s.UserBatting ? batter?.Template?.PlayerName : null);
            }
        }

        /// <summary>[TASK-GM-19] 오늘 오버레이 치어리더 - 전담 응원 매칭이 있으면 그 치어리더(대상 선수), 없으면 엔트리 1번(응원 리더).</summary>
        public static (Cheerleader cheerleader, Player target) TodayCheerleader(GMTeamState team)
        {
            if (team == null) return (null, null);
            foreach (var d in team.CheerDedications)
            {
                var c = team.CheerleaderPool.FirstOrDefault(x => x != null && x.InstanceId == d.CheerleaderId);
                if (c == null) continue;
                return (c, team.ReservePlayers.FirstOrDefault(x => x.InstanceId == d.PlayerId));
            }
            return (team.CheerEntry.FirstOrDefault(x => x != null && !CheerSquad.IsEmpty(x)), null);
        }

        private void BindLiveCheer(GMLiveSeasonSimulator.GameSession s)
        {
            var team = s.UserTeam ?? s.Home;
            var (c, target) = TodayCheerleader(team);
            if (c == LiveCheerleader && c != null && liveCheerName.text != "") return; // 같은 경기 · 같은 치어리더 = 사진 다시 읽지 않는다
            LiveCheerleader = c;
            if (c == null)
            {
                GMCheerPortraits.SetTexture(liveCheerPhoto, null);
                liveCheerInitial.text = "";
                liveCheerTitle.text = "오늘의 응원단";
                liveCheerName.text = "";
                liveCheerLine.text = "응원단 엔트리가 없습니다";
                liveCheerStats.text = "";
                return;
            }
            var tex = GMCheerPortraits.Photo(c) ?? GMCheerPortraits.Profile(c);
            GMCheerPortraits.SetTexture(liveCheerPhoto, tex);
            string name = c.DisplayName ?? "";
            liveCheerInitial.text = tex == null && name.Length > 0 ? name.Substring(0, 1) : "";
            liveCheerFrame.color = tex == null ? TeamThemePalette.Primary(team.Team) * 0.8f + new Color(0f, 0f, 0f, 0.2f) : new Color(0f, 0f, 0f, 0.35f);
            liveCheerTitle.text = target != null ? "오늘의 전담 응원" : "오늘의 응원 리더";
            liveCheerName.text = name;
            liveCheerLine.text = target != null ? $"전담 → {target.Template.PlayerName}" : $"{NameAliasTable.DisplayTeamName(team.TeamCode)} 단상 1번";
            liveCheerStats.text = $"엔트리 {GMCheerleaderRules.EntryCount(team.CheerEntry)}인 · 리더십 팀워크 +{team.CheerLeadershipBuff}";
        }

        private void BindLiveScore()
        {
            var s = session;
            int innings = Math.Min(MaxInnings, Math.Max(9, s.MaxInningPlayed));
            float teamX1 = 240, innX0 = 250, innX1 = 1600, w = (innX1 - innX0) / innings;
            float[] rx = { 1630, 1720, 1810, 1900 };
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < MaxInnings + 4; c++)
                {
                    var t = liveScore[r, c];
                    var rect = (RectTransform)t.transform;
                    float y0 = LiveLineY0[r], y1 = LiveLineY1[r];
                    bool used = c == 0 || (c <= MaxInnings && c <= innings) || c > MaxInnings;
                    t.gameObject.SetActive(used);
                    if (!used) continue;
                    if (c == 0) CompyaUiKit.SetBox(rect, r == 0 ? 20 : 76, y0, teamX1, y1);
                    else if (c <= MaxInnings) CompyaUiKit.SetBox(rect, innX0 + (c - 1) * w, y0, innX0 + c * w - 2, y1);
                    else CompyaUiKit.SetBox(rect, rx[c - MaxInnings - 1], y0, rx[c - MaxInnings] - 2, y1);
                }
            }
            string[] rhe = { "R", "H", "E" };
            liveScore[0, 0].text = "구단";
            for (int i = 1; i <= innings; i++) liveScore[0, i].text = i.ToString();
            for (int k = 0; k < 3; k++) liveScore[0, MaxInnings + 1 + k].text = rhe[k];
            for (int side = 0; side < 2; side++)
            {
                bool isHome = side == 1;
                var team = isHome ? s.Home : s.Away;
                CompyaUiKit.SetLogo(liveLogos[side], team.Team, 1f);
                liveScore[side + 1, 0].text = CompyaUiKit.ShortName(team.Team);
                for (int i = 1; i <= innings; i++)
                {
                    int runs = s.InningRuns(i, !isHome);
                    liveScore[side + 1, i].text = runs < 0 ? (s.IsOver && isHome && i >= 9 && i == s.MaxInningPlayed ? "X" : "") : runs.ToString();
                }
                liveScore[side + 1, MaxInnings + 1].text = (isHome ? s.HomeScore : s.AwayScore).ToString();
                liveScore[side + 1, MaxInnings + 2].text = (isHome ? s.HomeHits : s.AwayHits).ToString();
                liveScore[side + 1, MaxInnings + 3].text = (isHome ? s.HomeErrors : s.AwayErrors).ToString();
            }
        }
    }
}
