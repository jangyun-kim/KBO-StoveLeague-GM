using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-GM-12] 자동 검증(Unity CLI BatchPipelineGM12 1회 실행):
    ///   1) Batchmode 가드 - 재생 호출이 논리(선곡 · 클립 연결 · 기록)만 돌고 AudioSource는 재생되지 않음 · 단일 인스턴스
    ///   2) 로테이션 - 가중치 풀 100회 추출 시 최근 2곡 중복 0건 · 같은 화면 90초 쿨다운 · 3곡 풀도 반복 없음
    ///   3) 화면 그룹 - 스토브리그 허브 → 시장 정보실(FA) 이동 시 곡 유지(IsBgmPlaying · 시작 횟수 불변) · Refresh 반복 재시작 0 · 다른 그룹은 새 곡
    ///   4) 이벤트 하이재킹 - 대형 FA = 엘도라도 · 구단주 승인 체인(승리를 위해 → 엘도라도) → 화면 풀 재개(최근 2곡 제외) · 경기 라인업송 · 결과 BGM · 빅이닝/접전 판정
    /// </summary>
    public class GM12VerificationRunner
    {
        private static IReadOnlyList<PlayerTemplate> templates;
        private static List<Cheerleader> cheer;
        private static GameObject dbObject;
        private readonly List<Object> created = new List<Object>();

        [SetUp]
        public void EnsureDatabase()
        {
            GMAudioManager.SimulationMode = false; // [TASK-GM-14] 다른 테스트가 남긴 고속 진행 플래그 초기화
            var audio = GMAudioManager.Ensure();
            audio.StopAll();
            audio.ClearHistory();
            audio.Rotation.Clear();
            if (templates != null && templates.Count > 0 && templates.All(t => t != null)) return;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            dbObject = new GameObject("GM12_PlayerDatabase");
            templates = dbObject.AddComponent<PlayerDatabase>().AllTemplates.ToList();
            cheer = GMRosterLoader.AllCheerleaderTemplates();
            Assert.Greater(templates.Count, 500);
        }

        [OneTimeTearDown]
        public void Unload()
        {
            if (templates != null) foreach (var t in templates) if (t != null) Object.DestroyImmediate(t);
            templates = null;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            var audio = GMAudioManager.Instance;
            if (audio != null) audio.StopAll();
            if (audio != null && (audio.gameObject.hideFlags & HideFlags.DontSave) != 0) Object.DestroyImmediate(audio.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in created) if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
            var audio = GMAudioManager.Instance;
            if (audio != null) audio.StopAll();
        }

        private static GMLeagueState NewLeague(string team = "SAM", int seed = 1212)
        {
            var league = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim, out GMMatchPrePostUIController prePost)
        {
            var canvasGo = new GameObject("GM12_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvasGo);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var hub = NewView<GMOotpFrontOfficeUIController>(canvasGo, "GM12_Hub");
            hub.Build();
            prePost = NewView<GMMatchPrePostUIController>(canvasGo, "GM12_PrePost");
            prePost.Build();
            prePost.gameObject.SetActive(false);
            hub.PrePostView = prePost;
            hub.Bind(sim);
            return hub;
        }

        private static T NewView<T>(GameObject canvas, string name) where T : MonoBehaviour
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return go.AddComponent<T>();
        }

        // ================================================================== 1) Batchmode 가드 · 단일 인스턴스

        [Test]
        public void T1_BatchModeGuard_NoAudioOutput_SingleInstance()
        {
            var audio = GMAudioManager.Ensure();
            Assert.AreSame(audio, GMAudioManager.Ensure(), "Ensure 반복 호출 = 같은 인스턴스");
            Assert.AreSame(audio, GMAudioManager.Instance);
            Assert.AreEqual(!Application.isBatchMode, GMAudioManager.SoundOutputEnabled, "Batchmode = 출력 금지");

            int before = audio.SuppressedOutputCount;
            audio.EnterScreen(GMAudioScreen.Hub, "SAM");
            audio.PlayCue(GMAudioCue.OutSong, "SAM");
            audio.PlayCue(GMAudioCue.CrowdCheer, "SAM");
            audio.SetCheerleaderMix(5, true);
            audio.PlayEvent(GMAudioEvent.MajorResult, "SAM");
            // 논리는 그대로 돈다(선곡 · 클립 연결 · 기록)
            Assert.IsNotNull(audio.CurrentBgmKey);
            Assert.IsNotNull(audio.BgmSource.clip, "클립 연결(논리)");
            Assert.IsTrue(audio.IsBgmPlaying, "논리 재생 상태");
            if (Application.isBatchMode)
            {
                Assert.Greater(audio.SuppressedOutputCount, before, "출력 호출이 가드에서 막힘");
                Assert.IsFalse(audio.BgmSource.isPlaying, "BGM 실제 재생 없음");
                Assert.IsFalse(audio.SongSource.isPlaying, "응원가 실제 재생 없음");
            }
            TestContext.WriteLine($"[GM12 가드] Batchmode {Application.isBatchMode} · 막힌 출력 {audio.SuppressedOutputCount - before}건");
            audio.StopAll();
            Assert.IsFalse(audio.IsBgmPlaying);
        }

        // ================================================================== 2) 로테이션

        [Test]
        public void T2_Rotation_100Picks_NoRecentTwoRepeat_Cooldown90s()
        {
            var sam = TeamAudioProfile.For("SAM");
            foreach (var screen in new[] { GMAudioScreen.Hub, GMAudioScreen.Market, GMAudioScreen.Squad, GMAudioScreen.OwnerReport })
            {
                var pool = sam.PlaylistFor(screen);
                Assert.GreaterOrEqual(pool.Tracks.Length, 3, $"{screen} 풀 3곡 이상");
                Assert.IsFalse(pool.Contains(TeamAudioProfile.SamApt), "아파트는 일반 풀에서 제외");
                foreach (var t in pool.Tracks) Assert.IsNotNull(Resources.Load<AudioClip>(GMAudioManager.ResourceRoot + t.key), $"클립 로드 {t.key}");

                var rot = new GMBgmRotation(1212 + (int)screen);
                var picks = new List<string>();
                float now = 0f;
                int recentDup = 0, cooldownHit = 0;
                var lastAt = new Dictionary<string, float>();
                for (int i = 0; i < 100; i++)
                {
                    now += 40f; // 곡 길이보다 짧은 간격 - 쿨다운도 함께 검증
                    string k = rot.Pick(pool, now);
                    int n = picks.Count;
                    if ((n >= 1 && picks[n - 1] == k) || (n >= 2 && picks[n - 2] == k)) recentDup++;
                    if (lastAt.TryGetValue(k, out float t) && now - t < GMBgmRotation.SameScreenCooldown && pool.Tracks.Length > 3) cooldownHit++;
                    rot.MarkPlayed(k, pool.Id, now);
                    lastAt[k] = now;
                    picks.Add(k);
                }
                var freq = picks.GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
                TestContext.WriteLine($"[GM12 로테이션] {screen}: " + string.Join(", ", pool.Tracks.Select(x => $"{x.key.Replace("SAM/sam_", "")}({x.weight}) {(freq.TryGetValue(x.key, out int c) ? c : 0)}회")));
                Assert.AreEqual(0, recentDup, $"{screen} 최근 2곡 중복 0건");
                Assert.AreEqual(0, cooldownHit, $"{screen} 90초 쿨다운 위반 0건");
                Assert.AreEqual(pool.Tracks.Length, freq.Count, $"{screen} 풀의 모든 곡이 돌아간다");
            }

            // 가중치 - 제외 규칙이 없을 때(1회 추출 반복) 나의 라이온즈(30)가 jump up(15)보다 많이 뽑힌다
            var hub = sam.PlaylistFor(GMAudioScreen.Hub);
            var fresh = new GMBgmRotation(77);
            int myLions = 0, jumpUp = 0;
            for (int i = 0; i < 2000; i++)
            {
                string k = fresh.Pick(hub, 0f);
                if (k == TeamAudioProfile.SamMyLions) myLions++;
                if (k == TeamAudioProfile.SamJumpUp) jumpUp++;
            }
            Assert.Greater(myLions, jumpUp, "가중치 30% > 15%");
            Assert.That(myLions / 2000f, Is.InRange(0.25f, 0.35f), "나의 라이온즈 약 30%");

            // 1곡 풀(합성 앰비언스)은 그 곡을 반복
            Assert.AreEqual(TeamAudioProfile.SynthCrowd, fresh.Pick(TeamAudioProfile.For("LG").PlaylistFor(GMAudioScreen.Hub), 0f));
        }

        // ================================================================== 3) 화면 그룹 Keep Playing

        [Test]
        public void T3_ScreenGroup_StoveHubToMarket_KeepsPlaying_NoRestart()
        {
            var audio = GMAudioManager.Ensure();
            string first = audio.EnterScreen(GMAudioScreen.Hub, "SAM");
            int starts = audio.BgmTrackStarts;
            Assert.IsTrue(audio.IsBgmPlaying);
            Assert.AreEqual("SAM.office", audio.CurrentGroupId);

            Assert.AreEqual(first, audio.EnterScreen(GMAudioScreen.Market, "SAM"), "허브 → 시장 정보실 = 같은 곡");
            Assert.AreEqual(starts, audio.BgmTrackStarts, "곡 재시작 없음");
            Assert.IsTrue(audio.IsBgmPlaying, "IsPlaying 유지");
            Assert.AreEqual("SAM.market", audio.CurrentPlaylist.Id, "다음 곡은 시장 정보실 풀에서");
            for (int i = 0; i < 10; i++) audio.EnterScreen(i % 2 == 0 ? GMAudioScreen.Market : GMAudioScreen.Squad, "SAM");
            Assert.AreEqual(starts, audio.BgmTrackStarts, "Refresh 반복(같은 그룹) = 재시작 0");

            // 곡이 끝나면 현재 화면 풀에서 다음 곡(직전 곡 제외)
            audio.EnterScreen(GMAudioScreen.Market, "SAM");
            audio.SimulateTrackEnd();
            Assert.AreEqual(starts + 1, audio.BgmTrackStarts);
            Assert.AreNotEqual(first, audio.CurrentBgmKey, "로테이션 다음 곡 = 다른 곡");
            Assert.IsTrue(audio.CurrentPlaylist.Contains(audio.CurrentBgmKey));

            // 다른 그룹(구단주 보고실)은 새 곡으로 크로스페이드
            audio.EnterScreen(GMAudioScreen.OwnerReport, "SAM");
            Assert.AreEqual(starts + 2, audio.BgmTrackStarts, "다른 그룹 = 새 곡");
            Assert.AreEqual("SAM.owner", audio.CurrentGroupId);

            // 실제 프런트 오피스 - 메인 허브(구단주 대시보드) → FA 영입 협상(시장 정보실) 탭 이동
            audio.StopAll();
            var sim = new GMLiveSeasonSimulator(NewLeague());
            var hub = NewHub(sim, out _);
            hub.GoMainHome();
            Assert.AreEqual(GMAudioScreen.Hub, hub.CurrentAudioScreen);
            string hubKey = audio.CurrentBgmKey;
            int hubStarts = audio.BgmTrackStarts;
            Assert.IsNotNull(hubKey, "허브 진입 BGM");
            Assert.IsTrue(sam(hubKey), "삼성 허브 풀");
            hub.SelectSubTab(2);
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneFA, hub.CurrentPane);
            Assert.AreEqual(GMAudioScreen.Market, hub.CurrentAudioScreen, "FA 영입 협상 = 시장 정보실");
            hub.Refresh();
            hub.Refresh();
            Assert.AreEqual(hubKey, audio.CurrentBgmKey, "허브 → 시장 정보실 곡 유지");
            Assert.AreEqual(hubStarts, audio.BgmTrackStarts, "탭 이동 · Refresh 반복 재시작 0");
            Assert.IsTrue(audio.IsBgmPlaying);
            Assert.AreEqual(GMAudioScreen.Squad, GMOotpFrontOfficeUIController.AudioScreenFor(GMOotpFrontOfficeUIController.PaneRoster));
            Assert.AreEqual(GMAudioScreen.Market, GMOotpFrontOfficeUIController.AudioScreenFor(GMOotpFrontOfficeUIController.PaneNegotiation));

            bool sam(string key) => TeamAudioProfile.For("SAM").PlaylistFor(GMAudioScreen.Hub).Contains(key);
        }

        // ================================================================== 4) 이벤트 하이재킹

        [Test]
        public void T4_EventHijack_MajorFA_Eldorado_OwnerChain_Resume_MatchFlow()
        {
            var audio = GMAudioManager.Ensure();
            var league = NewLeague();
            var all = league.Teams.Values.SelectMany(t => t.AvailableRoster).ToList();
            var star = all.OrderByDescending(p => p.BaseOverall).First();
            var bench = all.Where(p => !p.IsCaptain && p.Loyalty < 80 && p.BaseOverall < 70 && p.Salary < GMOotpFrontOfficeUIController.MajorSigningSalary).OrderBy(p => p.BaseOverall).First();
            Assert.GreaterOrEqual(star.BaseOverall, GMOotpFrontOfficeUIController.MajorSigningOvr, "S급 선수 존재");
            Assert.AreEqual(GMAudioEvent.MajorResult, GMOotpFrontOfficeUIController.ContractEventFor(star, false), "대형 FA = 엘도라도 이벤트");
            Assert.AreEqual(GMAudioEvent.PositiveResult, GMOotpFrontOfficeUIController.ContractEventFor(bench, false), "일반 FA = 환희 이벤트");

            // 대형 FA 계약 성공 트리거 → 엘도라도로 교체, 화면 전환은 이벤트를 끊지 않는다
            audio.EnterScreen(GMAudioScreen.Market, "SAM");
            Assert.AreEqual(TeamAudioProfile.SamEldorado, audio.PlayEvent(GMOotpFrontOfficeUIController.ContractEventFor(star, false), "SAM"));
            Assert.AreEqual(TeamAudioProfile.SamEldorado, audio.CurrentBgmKey, "BGM = 엘도라도");
            Assert.AreEqual(GMAudioEvent.MajorResult, audio.ActiveEvent);
            Assert.IsFalse(audio.BgmSource.loop, "이벤트 곡은 1회");
            audio.EnterScreen(GMAudioScreen.Hub, "SAM");
            Assert.AreEqual(TeamAudioProfile.SamEldorado, audio.CurrentBgmKey, "이벤트 중 화면 이동 = 이벤트 유지");
            audio.SimulateTrackEnd();
            Assert.IsNull(audio.ActiveEvent, "이벤트 종료");
            Assert.IsTrue(audio.CurrentPlaylist.Contains(audio.CurrentBgmKey), "허브 풀에서 재개");
            Assert.AreNotEqual(TeamAudioProfile.SamEldorado, audio.CurrentBgmKey);
            Assert.IsTrue(audio.BgmSource.loop);

            // 구단주 승인 체인 - 승리를 위해 → 엘도라도 → 구단주 보고실 풀 재개(최근 2곡 = 체인 두 곡 제외 → 나의 라이온즈)
            audio.EnterScreen(GMAudioScreen.OwnerReport, "SAM");
            Assert.AreEqual(TeamAudioProfile.SamForVictory, audio.PlayEvent(GMAudioEvent.OwnerApproval, "SAM"));
            audio.SimulateTrackEnd();
            Assert.AreEqual(TeamAudioProfile.SamEldorado, audio.CurrentBgmKey, "체인 2곡째 = 엘도라도");
            audio.SimulateTrackEnd();
            Assert.IsNull(audio.ActiveEvent);
            Assert.AreEqual(TeamAudioProfile.SamMyLions, audio.CurrentBgmKey, "재개 = 최근 2곡 제외 후 남은 곡");

            // 이벤트 창 닫힘(EndEvent) = 즉시 재개 · 긴장 BGM = 합성 패드(공통)
            audio.PlayEvent(GMAudioEvent.Tension, "SAM");
            Assert.AreEqual(TeamAudioProfile.SynthTension, audio.CurrentBgmKey);
            Assert.IsNotNull(audio.BgmSource.clip, "긴장 BGM 합성 클립");
            audio.EndEvent();
            Assert.IsNull(audio.ActiveEvent);
            Assert.AreNotEqual(TeamAudioProfile.SynthTension, audio.CurrentBgmKey);

            // 경기 결과 · 빅이닝 · 접전 판정
            Assert.AreEqual(GMAudioEvent.BigWin, GMLiveAudioDirector.ResultEventFor(9, 2));
            Assert.AreEqual(GMAudioEvent.MatchWin, GMLiveAudioDirector.ResultEventFor(3, 2));
            Assert.AreEqual(GMAudioEvent.Defeat, GMLiveAudioDirector.ResultEventFor(1, 4));
            Assert.IsNull(GMLiveAudioDirector.ResultEventFor(2, 2));
            CollectionAssert.AreEqual(new[] { TeamAudioProfile.SamVictoryLions, TeamAudioProfile.SamEldorado }, TeamAudioProfile.For("SAM").EventChain(GMAudioEvent.BigWin), "대승 = 승리의 라이온즈 → 엘도라도");
            var bigInning = new AtBatStepResult { Result = AtBatResult.Double, IsTopHalf = true, RunsScoredThisPlay = 2, State = new MatchState() };
            Assert.AreEqual(GMAudioCue.BigInningSong, GMLiveAudioDirector.ExtraCueFor(bigInning, true, GMAudioCue.HighlightSong, 5, 3, 6, 1, false), "하프이닝 3 → 5점 = 빅이닝(아파트)");
            Assert.IsNull(GMLiveAudioDirector.ExtraCueFor(bigInning, true, GMAudioCue.HighlightSong, 7, 3, 8, 1, false), "이미 4점 넘은 뒤 = 다시 틀지 않음");
            Assert.AreEqual(TeamAudioProfile.SamApt, TeamAudioProfile.For("SAM").ClipFor(GMAudioCue.BigInningSong));
            var late = new AtBatStepResult { Result = AtBatResult.Walk, IsTopHalf = true, State = new MatchState() };
            Assert.AreEqual(GMAudioCue.LateCloseSong, GMLiveAudioDirector.ExtraCueFor(late, true, null, 0, 8, 3, 4, false), "8회 1점 차 = Jump up Lions");
            Assert.IsNull(GMLiveAudioDirector.ExtraCueFor(late, true, null, 0, 8, 3, 4, true), "경기당 1회");
            var groundout = new AtBatStepResult { Result = AtBatResult.Groundout, IsTopHalf = false, State = new MatchState() };
            Assert.AreEqual(GMAudioCue.OutCheer, GMLiveAudioDirector.ExtraCueFor(groundout, false, null, 0, 2, 0, 0, false), "수비 아웃 = 박수 효과음");

            // 한 경기 3단계 - 전력 분석 = 라인업송 → 플레이 볼 = 관중 앰비언스(라인업송 페이드아웃) → 결과 = 승리/패배 BGM
            audio.StopAll();
            var sim = new GMLiveSeasonSimulator(league);
            var hub = NewHub(sim, out var prePost);
            Assert.IsTrue(prePost.ShowPreGameView(sim));
            Assert.AreEqual(GMAudioEvent.Lineup, audio.ActiveEvent, "경기 전 = 라인업송");
            Assert.AreEqual(TeamAudioProfile.SamLineup, audio.CurrentBgmKey);
            Assert.IsTrue(prePost.PlayBall());
            Assert.IsNull(audio.ActiveEvent, "플레이 볼 = 라인업송 종료");
            Assert.AreEqual(TeamAudioProfile.SynthCrowd, audio.CurrentBgmKey, "경기 중 = 관중 앰비언스");
            Assert.AreEqual(GMAudioScreen.Match, audio.CurrentScreen);
            var extras = new HashSet<GMAudioCue>();
            for (int i = 0; i < 400 && !prePost.Session.IsOver; i++)
            {
                prePost.LiveStepAtBat();
                if (prePost.LastExtraCue.HasValue) extras.Add(prePost.LastExtraCue.Value);
            }
            TestContext.WriteLine("[GM12 경기] 추가 연출 큐: " + string.Join(", ", extras));
            Assert.IsTrue(extras.Contains(GMAudioCue.OutCheer) || extras.Contains(GMAudioCue.CrowdCheer), "득점 · 아웃 효과음");
            Assert.IsTrue(prePost.FinishLiveMatch());
            if (prePost.LastResultEvent.HasValue) // 무승부는 결과 BGM 없음
            {
                Assert.AreEqual(prePost.LastResultEvent, audio.ActiveEvent, "결과 BGM 이벤트 재생");
                string expected = TeamAudioProfile.For("SAM").EventChain(prePost.LastResultEvent.Value)[0];
                Assert.AreEqual(expected, audio.CurrentBgmKey);
            }
            TestContext.WriteLine($"[GM12 경기] 결과 이벤트 {prePost.LastResultEvent} → {audio.CurrentBgmKey}");
            prePost.CloseAll();
            hub.GoMainHome();
            Assert.AreEqual(GMAudioScreen.Hub, audio.CurrentScreen, "결과 후 허브 복귀");
        }
    }
}
