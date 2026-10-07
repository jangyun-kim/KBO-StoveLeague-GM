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
            return true;
        }

        // ================================================================== [TASK-GM-08] 구단 사운드 연출

        /// <summary>경기 시작 - 관중 앰비언스 · 치어리더 단상 믹스(엔트리 4~6인 + 리더십 버프) · 타석 이벤트 구독.</summary>
        private void StartLiveAudio()
        {
            if (session == null) return;
            DetachLiveAudio();
            var audio = GMAudioManager.Ensure();
            var user = session.UserTeam;
            audio.PlayMatchAmbience(user?.TeamCode);
            if (user != null) audio.SetCheerleaderMix(GMCheerleaderRules.EntryCount(user.CheerEntry), user.CheerLeadershipBuff > 0);
            audioSession = session;
            audioSession.OnStepped += OnLiveStep;
            LastLiveCue = null;
        }

        private void DetachLiveAudio()
        {
            if (audioSession != null) audioSession.OnStepped -= OnLiveStep;
            audioSession = null;
            var audio = GMAudioManager.Instance;
            if (audio != null) audio.SetCheerleaderMix(0, false);
        }

        /// <summary>타석 1회 → 내 구단 기준 큐 판정(GMLiveAudioDirector) → 구단 프로필 클립 재생(삼성 = 공식 응원가 · 아웃송, 그 밖 = 기본 앰비언스).</summary>
        private void OnLiveStep(AtBatStepResult step, GMLiveSeasonSimulator.StepContext ctx)
        {
            if (liveAudioMuted || audioSession == null || step == null || ctx == null) return;
            var user = audioSession.UserTeam;
            if (user == null) return;
            bool userAway = user == audioSession.Away;
            bool userBatting = step.IsTopHalf == userAway;
            int userBefore = userAway ? ctx.AwayBefore : ctx.HomeBefore, oppBefore = userAway ? ctx.HomeBefore : ctx.AwayBefore;
            int userAfter = userAway ? step.AwayScore : step.HomeScore, oppAfter = userAway ? step.HomeScore : step.AwayScore;
            LastLiveCue = GMLiveAudioDirector.CueFor(step, userBatting, ctx.RispBefore, ctx.HalfRuns, userBefore, oppBefore, step.GameEnded && userAfter > oppAfter);
            if (LastLiveCue.HasValue) GMAudioManager.Ensure().PlayCue(LastLiveCue.Value, user.TeamCode, ctx.Inning);
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

            // 투타 매치업
            var match = CompyaUiKit.Fill(liveRoot, "MatchupPanel");
            CompyaUiKit.Box(match, "Bg", 12, 230, 640, 700, Panel);
            L(match, "MatchupTitle", "현재 투타 매치업", 24, 234, 630, 272, BodyPt + 2, TextAnchor.MiddleLeft, Gold);
            var diamond = CompyaUiKit.Place(match, "Diamond", 430, 284, 610, 420);
            liveBases[1] = BaseSquare(diamond, "Base2", 0.4f, 0.62f);
            liveBases[0] = BaseSquare(diamond, "Base1", 0.72f, 0.32f);
            liveBases[2] = BaseSquare(diamond, "Base3", 0.08f, 0.32f);
            BaseSquare(diamond, "Home", 0.4f, 0.02f).color = new Color(1f, 1f, 1f, 0.35f);
            liveOuts = L(match, "LiveOuts", "", 24, 280, 420, 420, BodyPt, TextAnchor.UpperLeft, White);
            liveOuts.lineSpacing = 1.1f;
            liveBatter = L(match, "LiveBatter", "", 24, 430, 630, 560, BodyPt, TextAnchor.UpperLeft, White);
            livePitcher = L(match, "LivePitcher", "", 24, 566, 630, 696, BodyPt, TextAnchor.UpperLeft, White);
            liveBatter.lineSpacing = livePitcher.lineSpacing = 1.1f;

            // ABS 탄착군
            var abs = CompyaUiKit.Fill(liveRoot, "AbsZonePanel");
            CompyaUiKit.Box(abs, "Bg", 652, 230, 1100, 700, Panel);
            L(abs, "AbsTitle", "ABS 탄착군 (마지막 타석)", 664, 234, 1090, 272, BodyPt + 2, TextAnchor.MiddleLeft, Gold);
            var area = CompyaUiKit.Place(abs, "PitchArea", 706, 286, 1046, 626);
            CompyaUiKit.Paint(area, new Color(0f, 0f, 0f, 0.35f));
            var zone = CompyaUiKit.Norm(area, "StrikeZone", 1f / 6f, 1f / 6f, 5f / 6f, 5f / 6f);
            CompyaUiKit.Paint(zone, new Color(1f, 1f, 1f, 0.08f));
            CompyaUiKit.Outline(zone.GetComponent<Image>(), new Color(1f, 1f, 1f, 0.75f), 2f);
            for (int i = 0; i < LiveDots; i++)
            {
                var dot = CompyaUiKit.Norm(area, $"Pitch{i}", 0.5f, 0.5f, 0.5f, 0.5f);
                dot.sizeDelta = new Vector2(22f, 22f);
                liveDots[i] = CompyaUiKit.Paint(dot, Color.white);
                dot.gameObject.SetActive(false);
            }
            liveAbsSummary = L(abs, "AbsSummary", "", 664, 634, 1090, 696, SmallPt, TextAnchor.MiddleLeft, White);

            // 문자 중계
            var log = CompyaUiKit.Fill(liveRoot, "PlayByPlayPanel");
            CompyaUiKit.Box(log, "Bg", 1112, 230, 1908, 700, Panel);
            L(log, "LogTitle", "실시간 문자 중계", 1124, 234, 1896, 272, BodyPt + 2, TextAnchor.MiddleLeft, Gold);
            liveLog = L(log, "LiveLog", "", 1124, 278, 1896, 696, SmallPt, TextAnchor.UpperLeft, White);
            liveLog.lineSpacing = 1.05f;
            liveLog.verticalOverflow = VerticalWrapMode.Truncate;

            // 승리 확률
            var wp = CompyaUiKit.Fill(liveRoot, "WinProbabilityPanel");
            CompyaUiKit.Box(wp, "Bg", 12, 708, 1100, 948, Panel);
            liveWpaLabel = L(wp, "WpaLabel", "", 24, 712, 1090, 748, BodyPt, TextAnchor.MiddleLeft, Gold);
            var wArea = CompyaUiKit.Box(wp, "WpaArea", 30, 752, 1080, 940, new Color(0f, 0f, 0f, 0.3f));
            var mid = CompyaUiKit.Norm(wArea.transform, "Baseline", 0f, 0.497f, 1f, 0.503f);
            CompyaUiKit.Paint(mid, new Color(1f, 1f, 1f, 0.45f));
            var lineRect = CompyaUiKit.Fill(wArea.transform, "WpaLine");
            liveWpa = lineRect.gameObject.AddComponent<WpaLineGraphic>();
            liveWpa.color = new Color(0.4f, 0.85f, 1f);

            // 전술 개입
            var tac = CompyaUiKit.Fill(liveRoot, "TacticsPanel");
            CompyaUiKit.Box(tac, "Bg", 1112, 708, 1908, 948, Panel);
            L(tac, "TacticsTitle", "전술 개입 (다음 타석)", 1124, 712, 1896, 748, BodyPt + 2, TextAnchor.MiddleLeft, Gold);
            livePower = Btn(tac, "PowerButton", "강공", 1124, 756, 1312, 816, new Color(0.7f, 0.25f, 0.2f), ButtonPt);
            liveTactic = Btn(tac, "TacticButton", "작전 ▶", 1322, 756, 1510, 816, new Color(0.25f, 0.45f, 0.75f), ButtonPt);
            livePitching = Btn(tac, "PitchingButton", "투수 교체", 1520, 756, 1708, 816, new Color(0.2f, 0.55f, 0.4f), ButtonPt);
            livePinch = Btn(tac, "PinchButton", "대타", 1718, 756, 1896, 816, new Color(0.55f, 0.4f, 0.15f), ButtonPt);
            livePower.onClick.AddListener(() => LivePowerTactic());
            liveTactic.onClick.AddListener(() => LiveSpecialTactic());
            livePitching.onClick.AddListener(() => LivePitchingChange());
            livePinch.onClick.AddListener(() => LivePinchHit());
            liveMessage = L(tac, "LiveMessage", "", 1124, 824, 1896, 944, SmallPt + 1, TextAnchor.UpperLeft, Green);

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
                liveBatter.text = $"타자 {batter.Template.PlayerName} ({GMLiveSeasonSimulator.PositionShort(batter.Template.BatterPosition)} · OVR {batter.GetEffectiveOverall()})\n" +
                                  $"시즌 {(st != null ? $"타율 {(st.AVG >= 1 ? st.AVG.ToString("0.000") : st.AVG.ToString(".000"))} · {st.HR}홈런 · {st.RBI}타점" : "기록 없음")}\n{s.TodayBatting(batter)}";
            }
            else liveBatter.text = "타자 -";
            if (pitcher != null)
            {
                league.Stats.TryGetValue(pitcher.InstanceId, out var pt);
                livePitcher.text = $"투수 {pitcher.Template.PlayerName} (OVR {pitcher.GetEffectiveOverall()} · ABS {pitcher.ABSZoneSkill})\n" +
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
            liveAbsSummary.text = pitches.Count == 0 ? "첫 투구 전 - 빨강 스트라이크 · 파랑 볼"
                : $"투구 {pitches.Count}개 · 스트라이크 {strikes} · 볼 {pitches.Count - strikes} (빨강 S · 파랑 B)";

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
            livePitching.interactable = user && !s.UserBatting;
            livePinch.interactable = user && s.UserBatting;
            CompyaUiKit.SetButtonText(livePower, s.UserBatting ? "강공" : "강공 (정면 승부)");
            CompyaUiKit.SetButtonText(liveTactic, s.UserBatting ? $"작전 ▶ {GMLiveSeasonSimulator.MatchTacticLabel(OffenseCycle[tacticCycle % OffenseCycle.Length])}" : "작전 ▶ 고의사구");
            liveOneAtBat.interactable = live;
            liveOneInning.interactable = live;
            liveAuto.interactable = live;
            liveToEnd.interactable = live;
            CompyaUiKit.SetButtonText(liveAuto, autoPlay ? "고속 일시정지 ■" : "고속 진행 ▶");
            liveResult.interactable = s.IsOver;
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
