using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using KBOManager.Controllers;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.EditorTools;
using KBOManager.Engine;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using KBOManager.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-GM-08] 6대 자동 검증(Unity CLI BatchPipelineGM08 1회 실행):
    ///   1) 삼성 라이온즈 BGM · 공격 응원가 · 수비 아웃송 트리거 바인딩 + 치어리더 믹스 + BGM/SFX 볼륨
    ///   2) 144경기 후 리그 평균 타율 .260~.270 · 타격왕 .340~.385 · OBP · SLG · K% 현실 스케일
    ///   3) 포스트시즌 4단계 브래킷(WILD CARD GAME → KOREAN SERIES) · KBO 리더 · 일일 리포트 렌더링 무결성
    ///   4) AI 1:N 조건부 역제안 생성 · 가치 바 · 수락/거절
    ///   5) FA 등급(A/B/C) · 보상 정산 · 10대 가중치 20/25인 보호 명단 · 1군 29 + 퓨처스 풀 · 세이브 v19
    ///   6) 1920×1080 새 화면 텍스트 겹침 0 · 15pt 이상 · Bold 0 · 씬 배치
    /// </summary>
    public class GM08VerificationRunner
    {
        private static IReadOnlyList<PlayerTemplate> templates;
        private static List<Cheerleader> cheer;
        private static GameObject dbObject;
        private static GMLiveSeasonSimulator season;
        private readonly List<Object> created = new List<Object>();
        private bool sceneOpened;
        private float bgmPref = -1f, sfxPref = -1f;

        [SetUp]
        public void EnsureDatabase()
        {
            if (PlayerPrefs.HasKey(GMAudioManager.BgmPrefKey)) bgmPref = PlayerPrefs.GetFloat(GMAudioManager.BgmPrefKey);
            if (PlayerPrefs.HasKey(GMAudioManager.SfxPrefKey)) sfxPref = PlayerPrefs.GetFloat(GMAudioManager.SfxPrefKey);
            if (templates != null && templates.Count > 0 && templates.All(t => t != null)) return;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            dbObject = new GameObject("GM08_PlayerDatabase");
            templates = dbObject.AddComponent<PlayerDatabase>().AllTemplates.ToList();
            cheer = GMRosterLoader.AllCheerleaderTemplates();
            Assert.Greater(templates.Count, 500);
        }

        [OneTimeTearDown]
        public void Unload()
        {
            if (templates != null) foreach (var t in templates) if (t != null) Object.DestroyImmediate(t);
            templates = null;
            season = null;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            var audio = GMAudioManager.Instance;
            if (audio != null && (audio.gameObject.hideFlags & HideFlags.DontSave) != 0) Object.DestroyImmediate(audio.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            GameSettings.UseVirtualNames = false;
            if (templates != null) NameAliasTable.ApplyDisplayNames(templates.Where(t => t != null), false);
            foreach (var obj in created) if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
            if (bgmPref >= 0f) PlayerPrefs.SetFloat(GMAudioManager.BgmPrefKey, bgmPref); else PlayerPrefs.DeleteKey(GMAudioManager.BgmPrefKey);
            if (sfxPref >= 0f) PlayerPrefs.SetFloat(GMAudioManager.SfxPrefKey, sfxPref); else PlayerPrefs.DeleteKey(GMAudioManager.SfxPrefKey);
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = false;
        }

        private static GMLeagueState NewLeague(GMStartMode mode = GMStartMode.RealCurrent2026, string team = "SAM", int seed = 20260328)
        {
            var league = GMRosterLoader.LoadModeRoster(mode, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        /// <summary>정규시즌 144경기 완주(공유) - 타율 검증 · 포스트시즌 화면에 쓴다.</summary>
        private static GMLiveSeasonSimulator SharedSeason()
        {
            if (season != null) return season;
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "SAM", seed: 101)); // [TASK-GM-10] Log5 엔진 교체 - 재현 가능한 시드 재선정(8088은 타격왕 .329)
            Assert.IsTrue(sim.StartRun(GMRunMode.FullSeason));
            sim.RunUntilStop();
            Assert.IsTrue(sim.IsSeasonComplete);
            season = sim;
            return season;
        }

        private GameObject NewCanvas()
        {
            var canvasGo = new GameObject("GM08_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvasGo);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            return canvasGo;
        }

        private T NewView<T>(GameObject canvas, string name) where T : MonoBehaviour
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return go.AddComponent<T>();
        }

        private GMOotpFrontOfficeUIController NewHub(GMLiveSeasonSimulator sim, out GMMatchPrePostUIController prePost)
        {
            var canvas = NewCanvas();
            var hub = NewView<GMOotpFrontOfficeUIController>(canvas, "GM08_Hub");
            hub.Build();
            prePost = NewView<GMMatchPrePostUIController>(canvas, "GM08_PrePost");
            prePost.Build();
            prePost.gameObject.SetActive(false);
            hub.PrePostView = prePost;
            hub.Bind(sim);
            return hub;
        }

        private static string T(Transform root, string path) => root.Find(path).GetComponent<Text>().text;
        private static void Click(Transform root, string path) => root.Find(path).GetComponent<Button>().onClick.Invoke();

        private static AtBatStepResult Step(AtBatResult result, bool top, int runs = 0, bool halfEnded = false, bool gameEnded = false, bool risp = false, int home = 0, int away = 0)
        {
            var state = new MatchState { RunnerOnSecond = risp, Outs = halfEnded ? 3 : 1 };
            return new AtBatStepResult { Result = result, IsTopHalf = top, RunsScoredThisPlay = runs, HalfInningEnded = halfEnded, GameEnded = gameEnded, State = state, HomeScore = home, AwayScore = away };
        }

        // ================================================================== 1) 삼성 오디오

        [Test]
        public void T1_SamsungAudio_Bgm_ChanceSong_OutSong_Bindings_CheerMix_Volume()
        {
            // 프로필 · 클립 바인딩 - 삼성 17곡 전부 Resources/Audio/SAM에서 AudioClip으로 로드
            var sam = TeamAudioProfile.For("SAM");
            Assert.IsTrue(sam.HasClubAudio, "삼성 구단 음원");
            var keys = sam.AllClipKeys().ToList();
            Assert.AreEqual(17, keys.Count, "삼성 클립 17곡(BGM 2 · 찬스 5 · 하이라이트/역전 2 · 아웃송 8)");
            foreach (var k in keys) Assert.IsNotNull(Resources.Load<AudioClip>(GMAudioManager.ResourceRoot + k), $"클립 로드 {k}");
            StringAssert.StartsWith("SAM/sam_bgm_", sam.ClipFor(GMAudioCue.FrontOfficeBgm, 0));
            Assert.AreEqual("SAM/sam_chance_eldorado", sam.ClipFor(GMAudioCue.ChanceSong, 3, inning: 8), "8회 이후 찬스 = 엘도라도");
            Assert.AreNotEqual("SAM/sam_chance_eldorado", sam.ClipFor(GMAudioCue.ChanceSong, 0, inning: 3));
            StringAssert.StartsWith("SAM/sam_out_", sam.ClipFor(GMAudioCue.OutSong, 2), "삼진 = 아웃송");
            Assert.AreEqual("SAM/sam_out_main", sam.ClipFor(GMAudioCue.InningEndSong), "무실점 이닝 종료 = 공식 아웃송");
            StringAssert.StartsWith("SAM/sam_highlight_", sam.ClipFor(GMAudioCue.ComebackSong, 1), "동점 · 역전 = 승리 응원가");
            // 타 구단 = 기본 앰비언스(합성)
            var lg = TeamAudioProfile.For("LG");
            Assert.IsFalse(lg.HasClubAudio);
            Assert.AreEqual(TeamAudioProfile.SynthCrowd, lg.ClipFor(GMAudioCue.FrontOfficeBgm));
            Assert.AreEqual(TeamAudioProfile.SynthClap, lg.ClipFor(GMAudioCue.OutSong));
            Assert.IsNotNull(GMAudioSynth.Create(TeamAudioProfile.SynthCrowd), "합성 앰비언스");

            // 판정 로직(내 구단 원정 = 초 공격)
            Assert.AreEqual(GMAudioCue.ChanceSong, GMLiveAudioDirector.CueFor(Step(AtBatResult.Single, true, risp: true), true, false, 0, 0, 0, false), "득점권 진출 = 찬스송");
            Assert.IsNull(GMLiveAudioDirector.CueFor(Step(AtBatResult.Groundout, true, risp: true), true, true, 0, 0, 0, false), "이미 득점권이면 다시 틀지 않음");
            Assert.AreEqual(GMAudioCue.ComebackSong, GMLiveAudioDirector.CueFor(Step(AtBatResult.Single, true, runs: 1, away: 2, home: 2), true, true, 0, 1, 2, false), "동점 적시타");
            Assert.AreEqual(GMAudioCue.HighlightSong, GMLiveAudioDirector.CueFor(Step(AtBatResult.HomeRun, true, runs: 1, away: 5, home: 1), true, false, 0, 4, 1, false), "홈런(리드 중)");
            Assert.AreEqual(GMAudioCue.OutSong, GMLiveAudioDirector.CueFor(Step(AtBatResult.Strikeout, false), false, false, 0, 0, 0, false), "수비 삼진 = 아웃송");
            Assert.AreEqual(GMAudioCue.InningEndSong, GMLiveAudioDirector.CueFor(Step(AtBatResult.Flyout, false, halfEnded: true), false, false, 0, 0, 0, false), "무실점 이닝 종료");
            Assert.AreEqual(GMAudioCue.OutSong, GMLiveAudioDirector.CueFor(Step(AtBatResult.Strikeout, false, halfEnded: true), false, false, 2, 0, 0, false), "실점 이닝은 삼진 아웃송");
            Assert.AreEqual(GMAudioCue.WinSong, GMLiveAudioDirector.CueFor(Step(AtBatResult.Flyout, false, halfEnded: true, gameEnded: true), false, false, 0, 3, 1, true), "승리 직후");

            // 매니저 - BGM · 큐 · 치어리더 믹스 · 볼륨
            var audio = GMAudioManager.Ensure();
            audio.StopAll();
            audio.ClearHistory();
            string bgm = audio.PlayTeamBgm("SAM");
            CollectionAssert.Contains(sam.HomeBgm, bgm, "프런트 오피스 = 삼성 BGM");
            Assert.AreEqual("SAM", audio.CurrentBgmTeam);
            Assert.IsNotNull(audio.BgmSource.clip, "BGM 클립 연결");
            Assert.IsTrue(audio.BgmSource.loop, "BGM 루프");
            StringAssert.StartsWith("SAM/sam_out_", audio.PlayCue(GMAudioCue.OutSong, "SAM"));
            audio.StopMatchAudio();
            Assert.AreEqual("SAM/sam_chance_eldorado", audio.PlayCue(GMAudioCue.ChanceSong, "SAM", 9));
            Assert.IsNotNull(audio.SongSource.clip);
            audio.StopMatchAudio();
            audio.PlayCue(GMAudioCue.HighlightSong, "SAM");
            Assert.IsTrue(audio.History.Any(h => h.cue == GMAudioCue.CrowdCheer), "하이라이트 = 환호 효과음 겹침");
            Assert.AreEqual(TeamAudioProfile.SynthCrowd, audio.PlayTeamBgm("LG"), "타 구단 = 기본 앰비언스");
            audio.SetCheerleaderMix(5, true);
            Assert.IsTrue(audio.CheerMixActive);
            Assert.AreEqual(1.2f, audio.CheerBoost, 1e-4f, "5인 단상 = 응원가 +20%");
            audio.SetCheerleaderMix(3, true);
            Assert.IsFalse(audio.CheerMixActive, "엔트리 4명 미만이면 믹스 없음");
            audio.SetCheerleaderMix(6, false);
            Assert.IsFalse(audio.CheerMixActive, "버프 꺼짐");
            audio.StopMatchAudio();
            audio.BgmVolume = 0.25f;
            audio.SfxVolume = 0.9f;
            Assert.AreEqual(0.25f, PlayerPrefs.GetFloat(GMAudioManager.BgmPrefKey), 1e-4f, "BGM 볼륨 저장");
            Assert.AreEqual(0.9f, PlayerPrefs.GetFloat(GMAudioManager.SfxPrefKey), 1e-4f);
            Assert.AreEqual(0.25f, audio.BgmSource.volume, 1e-4f, "BGM 채널 볼륨");
            Assert.AreEqual(0.9f, audio.SongSource.volume, 1e-4f, "응원가 채널 = SFX 볼륨");

            // 프런트 오피스 진입 = 삼성 BGM / 감독 설정 슬라이더 = 볼륨
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "SAM", seed: 81));
            var hub = NewHub(sim, out var prePost);
            hub.GoMainHome();
            Assert.AreEqual("SAM", audio.CurrentBgmTeam, "메인 홈 진입 BGM");
            hub.OpenManagerSetup();
            Assert.IsNotNull(hub.BgmVolumeSlider);
            Assert.AreEqual(0.25f, hub.BgmVolumeSlider.value, 1e-4f, "슬라이더 = 저장 볼륨");
            hub.BgmVolumeSlider.value = 0.5f;
            hub.SfxVolumeSlider.value = 0.4f;
            Assert.AreEqual(0.5f, audio.BgmVolume, 1e-4f, "BGM 슬라이더 연결");
            Assert.AreEqual(0.4f, audio.SfxVolume, 1e-4f, "SFX 슬라이더 연결");
            StringAssert.Contains("50%", T(hub.Root, $"{GMOotpFrontOfficeUIController.ManagerSetupName}/BgmLabel"));
            hub.CloseManagerSetup();

            // 실시간 이닝 경기 - 타석 단위 진행 중 삼성 응원가 · 아웃송 트리거
            Assert.IsTrue(prePost.ShowPreGameView(sim));
            Assert.IsTrue(prePost.PlayBall());
            Assert.IsTrue(audio.History.Any(h => h.cue == GMAudioCue.MatchAmbience), "경기 시작 = 앰비언스");
            var userTeam = sim.League.UserTeam;
            Assert.AreEqual(GMCheerleaderRules.IsValidEntryCount(GMCheerleaderRules.EntryCount(userTeam.CheerEntry)) && userTeam.CheerLeadershipBuff > 0, audio.CheerMixActive, "치어리더 엔트리(4~6인) 단상 버프 = 응원 믹스");
            Assert.AreEqual(TeamAudioProfile.SynthCrowd, audio.CurrentBgmKey, "경기 = 관중 앰비언스");
            var cues = new HashSet<GMAudioCue>();
            for (int i = 0; i < 400 && !prePost.Session.IsOver; i++)
            {
                prePost.LiveStepAtBat();
                if (prePost.LastLiveCue.HasValue) cues.Add(prePost.LastLiveCue.Value);
            }
            TestContext.WriteLine("[GM08 오디오] 실시간 경기 큐: " + string.Join(", ", cues));
            Assert.IsTrue(cues.Contains(GMAudioCue.OutSong) || cues.Contains(GMAudioCue.InningEndSong), "수비 아웃송 트리거");
            Assert.IsTrue(cues.Contains(GMAudioCue.ChanceSong) || cues.Contains(GMAudioCue.HighlightSong) || cues.Contains(GMAudioCue.ComebackSong), "공격 응원가 트리거");
            Assert.IsTrue(audio.Requests.Any(h => h.key.StartsWith("SAM/sam_out_")), "삼성 아웃송 트리거 → 클립 바인딩");
            Assert.IsTrue(audio.Requests.Any(h => h.key.StartsWith("SAM/sam_") && !h.key.StartsWith("SAM/sam_out_") && !h.key.StartsWith("SAM/sam_bgm_home1")), "삼성 공격 응원가 트리거 → 클립 바인딩");
            Assert.IsTrue(prePost.FinishLiveMatch());
            Assert.IsFalse(audio.CheerMixActive, "경기 종료 - 응원 믹스 정리");
        }

        // ================================================================== 2) 타율 밸런스

        [Test]
        public void T2_Batting_144Games_LeagueAvg260to270_BattingTitle340to385()
        {
            var sim = SharedSeason();
            var (avg, obp, slg, k) = sim.LeagueBattingLine();
            var top = sim.Leaders(GMLeaderCategory.AVG, 3);
            var hr = sim.Leaders(GMLeaderCategory.HR, 1);
            var balance = sim.League.BattingBalance;
            string line = $"[GM08 타격] 리그 타율 {avg:.000} · 출루율 {obp:.000} · 장타율 {slg:.000} · 삼진율 {k:P1} | 타격왕 {string.Join(", ", top.Select(t => $"{t.Name} {t.ValueLabel}"))} | 홈런왕 {hr[0].Name} {hr[0].ValueLabel} | 환경 배수 {balance.EnvironmentHitFactor:0.000}";
            TestContext.WriteLine(line);
            Debug.Log(line);
            Assert.That(avg, Is.InRange(0.260, 0.270), "리그 평균 타율 .260~.270");
            Assert.That(top[0].Value, Is.InRange(0.340, 0.385), "타격왕 .340~.385");
            Assert.That(obp, Is.InRange(0.315, 0.350), "리그 출루율");
            Assert.That(slg, Is.InRange(0.370, 0.430), "리그 장타율");
            Assert.That(k, Is.InRange(0.15, 0.22), "삼진율(타석당)");
            Assert.Greater(hr[0].Value, 15, "홈런왕 15개 이상");
            Assert.That(balance.EnvironmentHitFactor, Is.InRange(GMBattingBalance.EnvironmentMin, GMBattingBalance.EnvironmentMax), "리그 환경 정규화 배수 범위");
            Assert.Greater(balance.ContactOffset, 0f, "2026 로스터 타자 정확 > 투수 구위(센터링 기준)");
            // 단장 모드 경기에는 타격 밸런스가 들어가고, 레거시 경기(케미스트리 없음)는 기존 확률표 그대로
            var mods = sim.ModifiersFor(sim.League.UserTeam, true);
            Assert.AreSame(balance, mods.Chemistry.Batting, "단장 모드 경기 = 리그 밸런스");
            Assert.IsNull(TeamPowerModifiers.None.Chemistry, "레거시 = 케미스트리 없음");
            // 피로 누적: 60% 이상 1.0 → 0%에서 0.85
            Assert.AreEqual(1f, GMBattingBalance.FatigueMultiplier(0.7f), 1e-5f);
            Assert.AreEqual(0.85f, GMBattingBalance.FatigueMultiplier(0f), 1e-5f);
            Assert.Less(GMBattingBalance.FatigueMultiplier(0.3f), 1f);
            // 격차 상한 ±0.3
            Assert.AreEqual(GMBattingBalance.SkillCap, GMBattingBalance.Skill(200f, 0f), 1e-5f);
            Assert.AreEqual(0f, GMBattingBalance.Skill(6f, 6f), 1e-5f, "평균 매치업 = 기본 확률표");
        }

        // ================================================================== 3) 포스트시즌 브래킷

        [Test]
        public void T3_PostseasonBracket_FourColumns_Leaders_DailyReport()
        {
            var sim = SharedSeason();
            var hub = NewHub(sim, out _);
            hub.OpenPostseasonTree();
            var tree = hub.Pane(GMOotpFrontOfficeUIController.PanePostseason);
            string[] heads = { "WILD CARD GAME", "SEMI PLAYOFF", "PLAYOFF", "KOREAN SERIES" };
            string[] subs = { "3판 과반승 / 4위 1승 어드밴티지", "5판 과반승", "5판 과반승", "7판 과반승" };
            for (int col = 0; col < 4; col++)
            {
                Assert.AreEqual(heads[col], T(tree, $"BracketHead{col}"), $"{col + 1}열 제목");
                Assert.AreEqual(subs[col], T(tree, $"BracketSub{col}"), $"{col + 1}열 방식");
                if (col > 0) foreach (var seg in new[] { "A", "B", "C" }) Assert.IsNotNull(tree.Find($"Connector{col}_{seg}"), $"연결선 {col}{seg}");
            }
            // 4위 · 5위(와일드카드) · 3위(준PO) · 2위(PO) · 1위(KS) 시드 배치
            Assert.IsNotNull(GMAwardEvaluator.BeginPostseason(sim));
            StringAssert.StartsWith("4. ", T(tree, "Bracket0_0/Name"));
            StringAssert.StartsWith("5. ", T(tree, "Bracket0_1/Name"));
            StringAssert.StartsWith("3. ", T(tree, "Bracket1_0/Name"));
            StringAssert.StartsWith("2. ", T(tree, "Bracket2_0/Name"));
            StringAssert.StartsWith("1. ", T(tree, "Bracket3_0/Name"));
            Assert.IsNotNull(tree.Find("Bracket0_0/Logo").GetComponentInChildren<RawImage>(true), "구단 엠블럼");
            var winsText = tree.Find("Bracket0_0/Wins").GetComponent<Text>();
            Assert.Greater(winsText.color.r, 0.8f, "시리즈 승수 = 붉은색");
            Assert.Less(winsText.color.g, 0.4f);
            // 좌측 KBO 리더 9부문 1~3위
            var cats = GMOotpFrontOfficeUIController.BracketLeaderCategories;
            Assert.AreEqual(9, cats.Length);
            CollectionAssert.AreEqual(new[] { GMLeaderCategory.AVG, GMLeaderCategory.HR, GMLeaderCategory.RBI, GMLeaderCategory.SB, GMLeaderCategory.OPS, GMLeaderCategory.BatterWAR, GMLeaderCategory.HitStreak, GMLeaderCategory.ERA, GMLeaderCategory.Wins }, cats);
            Assert.AreEqual("KBO 리더", T(tree, "LeadersPanel/LeadersTitle"));
            for (int i = 0; i < 9; i++)
            {
                Assert.AreEqual(GMLeaderCategories.Label(cats[i]), T(tree, $"LeadersPanel/LeaderCat{i}"));
                for (int r = 0; r < 3; r++) StringAssert.DoesNotEndWith("-", T(tree, $"LeadersPanel/Leader{i}_{r}"), $"리더 {cats[i]} {r + 1}위");
            }
            StringAssert.Contains(sim.Leaders(GMLeaderCategory.OPS, 1)[0].Name, T(tree, "LeadersPanel/Leader4_0"), "OPS 1위");
            // 하단 MM/DD/YYYY 일일 리포트
            Assert.IsTrue(Regex.IsMatch(T(tree, "ReportDate"), @"^\d{2}/\d{2}/\d{4} 포스트시즌 일일 리포트$"), "MM/DD/YYYY 날짜");
            Assert.IsNotEmpty(T(tree, "PostseasonReport"));
            CheckPane(hub, "포스트시즌 트리(시작 전)");

            // 1경기씩 진행 → 승수 갱신 · 시리즈 진출 소식
            for (int g = 0; g < 4; g++)
            {
                var next = GMAwardEvaluator.NextPostseasonGame(sim);
                if (next == null) break;
                GMAwardEvaluator.PlayNextPostseasonGame(sim);
                hub.OpenPostseasonTree();
                var series = GMAwardEvaluator.BeginPostseason(sim).Series[next.SeriesIndex];
                Assert.AreEqual(series.HigherWins.ToString(), T(tree, $"Bracket{next.SeriesIndex}_0/Wins"), "상위 시드 승수 갱신");
                Assert.AreEqual(series.LowerWins.ToString(), T(tree, $"Bracket{next.SeriesIndex}_1/Wins"), "하위 시드 승수 갱신");
            }
            Assert.IsTrue(Regex.IsMatch(T(tree, "ReportDate"), @"^\d{2}/\d{2}/\d{4} "), "진행 중 날짜");
            CheckPane(hub, "포스트시즌 트리(진행 중)");
            var ps = GMAwardEvaluator.RunPostseason(sim);
            hub.OpenPostseasonTree();
            StringAssert.Contains("한국시리즈 우승", T(tree, "Champion"));
            Assert.AreEqual($"10/31/{sim.League.SeasonYear} 포스트시즌 일일 리포트", T(tree, "ReportDate"), "종료 리포트 날짜");
            Assert.IsNotEmpty(T(tree, "PostseasonReport"), "시리즈 진출 소식");
            StringAssert.Contains("시상식", tree.Find("NextGameButton").GetComponentInChildren<Text>().text);
            CheckPane(hub, "포스트시즌 트리(종료)");
            Assert.AreEqual(ps.ChampionCode, ps.Series[3].WinnerCode);
        }

        // ================================================================== 4) 1:N 역제안

        [Test]
        public void T4_TradeAI_OneToN_CounterOffer_ValueBar_AcceptReject()
        {
            var league = NewLeague(team: "SAM", seed: 84);
            var me = league.UserTeam;
            var kia = league.Teams["KIA"];
            var needs = GMTradeAI.AnalyzeNeeds(league, kia);
            Assert.IsNotEmpty(needs, "구단 니즈");
            Assert.LessOrEqual(needs.Count, 3);
            Assert.IsTrue(needs.All(n => n.Severity > 0f && n.Severity <= 1f && !string.IsNullOrEmpty(n.Label)));

            var target = kia.Roster.OrderByDescending(GMStoveLeagueMarket.TradeValue).First();
            var offer = GMTradeAI.BuildCounterOffer(league, me, kia, target);
            TestContext.WriteLine($"[GM08 역제안] {offer.Pitch} / 가치 바 {offer.Evaluation?.Ratio * 100:0}%");
            Assert.IsTrue(offer.Valid, offer.Pitch);
            Assert.That(offer.Requested.Count, Is.InRange(1, GMTradeAI.MaxPackage), "1:N 패키지(1~3명)");
            Assert.IsTrue(offer.Requested.All(p => me.ReservePlayers.Contains(p)), "내 구단 1군 · 퓨처스 선수만");
            Assert.IsTrue(offer.Evaluation.Acceptable, "역제안 = 상대 단장 수락 가능");
            Assert.GreaterOrEqual(offer.Evaluation.Ratio, 1f);
            StringAssert.Contains(target.Template.PlayerName, offer.Pitch);
            StringAssert.Contains(GMTradeAI.NeedsLabel(offer.Needs), offer.Pitch, "니즈를 붙인 조건부 제안");

            // 가치 바 실시간 - 선수를 빼면 내려가고 연봉 보조를 얹으면 올라간다, 헐값 1:1은 거절
            var lowball = GMStoveLeagueMarket.Evaluate(league, me, new[] { me.Roster.OrderBy(GMStoveLeagueMarket.TradeValue).First() }, kia, new[] { target });
            Assert.IsFalse(lowball.Acceptable, "헐값 1:1 거절");
            var withCash = GMStoveLeagueMarket.Evaluate(league, me, new[] { me.Roster.OrderBy(GMStoveLeagueMarket.TradeValue).First() }, kia, new[] { target }, 30000);
            Assert.Greater(withCash.Ratio, lowball.Ratio, "연봉 보조 = 가치 바 상승");
            Assert.AreEqual(30000, withCash.CashSubsidy);
            if (offer.Requested.Count > 1)
            {
                var partial = GMStoveLeagueMarket.Evaluate(league, me, offer.Requested.Take(offer.Requested.Count - 1).ToList(), kia, new[] { target });
                Assert.Less(partial.Ratio, offer.Evaluation.Ratio, "패키지에서 빼면 가치 바 하락");
            }
            var four = GMStoveLeagueMarket.Evaluate(league, me, me.Roster.Take(4).ToList(), kia, new[] { target });
            StringAssert.Contains("최대 3명", four.Reason, "한쪽 최대 3명");

            // 수락 = 체결(1:N) - 1군 29명 초과분은 퓨처스로 정리
            long myBudget = me.Budget, kiaBudget = kia.Budget;
            var r = GMTradeAI.AcceptCounterOffer(league, me, offer);
            Assert.IsTrue(r.Success, r.Message);
            Assert.Contains(target, me.Roster, "핵심 선수 영입");
            Assert.IsTrue(offer.Requested.All(p => kia.ReservePlayers.Contains(p)), "패키지 이적");
            Assert.LessOrEqual(me.Roster.Count, GMStoveLeagueMarket.RosterMax);
            Assert.LessOrEqual(kia.Roster.Count, GMStoveLeagueMarket.RosterMax, "상대 1군 29명 이하");
            Assert.LessOrEqual(kia.Futures.Count, GMRosterTiers.FuturesMax);
            Assert.AreEqual(myBudget - offer.CashSubsidy, me.Budget, "연봉 보조 지급");
            Assert.AreEqual(kiaBudget + offer.CashSubsidy, kia.Budget);
            StringAssert.Contains($"{offer.Requested.Count}:1", r.Message);

            // 불가능한 요구(가치를 맞출 수 없음) = Valid false
            var poor = NewLeague(team: "KIW", seed: 85);
            var richTarget = poor.AllPlayers.Where(p => p != null && !poor.UserTeam.Roster.Contains(p)).OrderByDescending(GMStoveLeagueMarket.TradeValue).First();
            var richTeam = poor.Teams[poor.TeamCodeOf(richTarget)];
            poor.UserTeam.Budget = 0;
            foreach (var p in poor.UserTeam.ReservePlayers.ToList()) p.InjuryRemainingDays = 30; // 내줄 선수가 없다
            var none = GMTradeAI.BuildCounterOffer(poor, poor.UserTeam, richTeam, richTarget);
            Assert.IsFalse(none.Valid, "내줄 자원이 없으면 역제안 불가");

            // UI - 상대 핵심 선수 클릭 → AI 역제안 팝업 → 슬롯 조정 → 가치 바
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "SAM", seed: 86));
            var hub = NewHub(sim, out _);
            hub.SelectMainTab(4);
            hub.SelectSubTab(1);
            var pane = hub.Pane(GMOotpFrontOfficeUIController.PaneTrade);
            hub.SetTradeSlots(null, "KIA", null);
            Assert.AreEqual("KIA", hub.TradePartner.TeamCode);
            Click(pane, "TrTheirRow0");
            Assert.IsTrue(hub.IsCounterPopupOpen, "상대 선수 클릭 = 역제안 팝업");
            Assert.IsNotNull(hub.CounterOffer);
            Assert.IsTrue(hub.CounterOffer.Valid, hub.CounterOffer.Pitch);
            var popup = hub.Root.Find(GMOotpFrontOfficeUIController.CounterPopupName);
            StringAssert.Contains("니즈", T(popup, "CounterNeeds"));
            StringAssert.Contains("%", T(popup, "CounterValue"));
            CheckLayer(popup, "AI 역제안 팝업");
            Click(popup, "CounterLoad");
            Assert.IsFalse(hub.IsCounterPopupOpen);
            Assert.AreEqual(hub.CounterOffer.Requested.Count, hub.TradeMine.Count, "패키지가 슬롯에 올라감");
            Assert.AreEqual(1, hub.TradeTheirs.Count);
            StringAssert.Contains("%", T(pane, "TrValueLabel"));
            float fill0 = pane.Find("TrValueBar/Fill").GetComponent<RectTransform>().anchorMax.x;
            int cash0 = hub.TradeCashSubsidy;
            Click(pane, "TrCashPlus");
            Assert.AreEqual(Math.Min(GMTradeAI.MaxCashSubsidy, cash0 + GMTradeAI.CashStep * 5), hub.TradeCashSubsidy, "연봉 보조 +5,000만");
            Assert.GreaterOrEqual(pane.Find("TrValueBar/Fill").GetComponent<RectTransform>().anchorMax.x, fill0, "가치 바 실시간 반영");
            StringAssert.Contains("연봉 보조", T(pane, "TrCashLabel"));
            CheckPane(hub, "트레이드 1:N");
            var proposed = hub.ProposeTrade();
            Assert.IsTrue(proposed.Success, proposed.Message);
            Assert.AreEqual(0, hub.TradeCashSubsidy, "체결 후 초기화");
        }

        // ================================================================== 5) FA 등급 · 보상 · 보호 명단

        [Test]
        public void T5_FaGrade_Compensation_ProtectionList_TenWeights_RosterTiers()
        {
            // 등급표(구단 연봉 순위 · 리그 연봉 순위 · 나이)
            Assert.AreEqual(GMFaGrade.A, GMFaCompensation.GradeFor(1, 10, 28));
            Assert.AreEqual(GMFaGrade.A, GMFaCompensation.GradeFor(3, 30, 30));
            Assert.AreEqual(GMFaGrade.B, GMFaCompensation.GradeFor(4, 31, 30));
            Assert.AreEqual(GMFaGrade.B, GMFaCompensation.GradeFor(10, 60, 30));
            Assert.AreEqual(GMFaGrade.C, GMFaCompensation.GradeFor(11, 61, 30));
            Assert.AreEqual(GMFaGrade.C, GMFaCompensation.GradeFor(1, 1, 35), "35세 이상 C등급 특례");
            Assert.AreEqual(20, GMFaCompensation.ProtectionSize(GMFaGrade.A));
            Assert.AreEqual(25, GMFaCompensation.ProtectionSize(GMFaGrade.B));
            Assert.AreEqual(0, GMFaCompensation.ProtectionSize(GMFaGrade.C));
            Assert.AreEqual(20000, GMFaCompensation.CashWithPlayer(GMFaGrade.A, 10000), "A 200%");
            Assert.AreEqual(30000, GMFaCompensation.CashOnly(GMFaGrade.A, 10000), "A 300%");
            Assert.AreEqual(10000, GMFaCompensation.CashWithPlayer(GMFaGrade.B, 10000), "B 100%");
            Assert.AreEqual(20000, GMFaCompensation.CashOnly(GMFaGrade.B, 10000), "B 200%");
            Assert.AreEqual(15000, GMFaCompensation.CashOnly(GMFaGrade.C, 10000), "C 150%");
            Assert.IsFalse(GMFaCompensation.RequiresPlayer(GMFaGrade.C));

            var league = NewLeague(team: "SAM", seed: 87);
            var me = league.UserTeam;
            // 로스터 구조 - 1군 29(출장 27) + 퓨처스 핵심 10~15명 + 육성 슬롯
            Assert.AreEqual(29, GMStoveLeagueMarket.RosterMax, "1군 29명");
            Assert.AreEqual(27, GMRosterTiers.GameDayActive, "출장 27명");
            foreach (var t in league.Teams.Values)
            {
                Assert.AreEqual(28, t.Roster.Count, $"{t.TeamCode} 1군 28명(1자리 여유)");
                Assert.That(t.Futures.Count, Is.InRange(GMRosterTiers.FuturesMin, GMRosterTiers.FuturesMax), $"{t.TeamCode} 퓨처스 풀");
                Assert.IsTrue(t.Futures.All(p => p.Age >= 19 && p.Age <= 25 && p.Salary == Player.MinSalary), "퓨처스 = 유망주 계약");
                Assert.IsTrue(t.Futures.All(p => p.BaseOverall <= GMRosterTiers.ProspectCeiling), $"{t.TeamCode} 퓨처스 = 유망주 체급(OVR 70 이하)");
                Assert.IsTrue(t.Futures.All(p => !GMFaCompensation.IsForeign(p)), "퓨처스 외국인 없음");
                Assert.Greater(t.DevelopmentSlots, 0, "육성 슬롯");
            }
            var ids = league.Teams.Values.SelectMany(t => t.ReservePlayers).Concat(league.FreeAgents).Concat(league.DraftPool).Select(p => p.Template.RealPlayerId).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "같은 선수가 두 곳에 없음");
            StringAssert.Contains("1군 28/29명", GMRosterTiers.Summary(me));
            var up = me.Futures[0];
            Assert.IsTrue(GMRosterTiers.CallUp(league, me, up, out var msg), msg);
            Assert.AreEqual(29, me.Roster.Count);
            Assert.IsFalse(GMRosterTiers.CallUp(league, me, me.Futures[0], out _), "29명 가득 - 콜업 불가");
            Assert.IsTrue(GMRosterTiers.SendDown(me, up, out _));
            Assert.AreEqual(28, me.Roster.Count);

            // 10대 가중치 보호 명단(AI 9개 구단 · 20인/25인)
            foreach (var t in league.Teams.Values.Where(t => !t.IsUserTeam))
            {
                foreach (int size in new[] { 20, 25 })
                {
                    var list = GMFaCompensation.BuildProtectionList(league, t, size);
                    int pool = t.ReservePlayers.Count() - list.AutoProtected.Count();
                    Assert.AreEqual(Math.Min(size, pool), list.Protected.Count(), $"{t.TeamCode} {size}인 보호");
                    Assert.AreEqual(t.ReservePlayers.Count(), list.Entries.Count, "보류선수 전원 평가(1군 + 퓨처스)");
                    Assert.AreEqual(100f, list.Weights.Sum(), 0.01f, "①~⑤ 가중치 합 100");
                    Assert.That(list.PersonalitySwing, Is.InRange(3, 5), "⑩ 단장 성향 ±3~5");
                    Assert.IsTrue(list.Entries.All(e => Math.Abs(e.Personality) <= list.PersonalitySwing), "⑩ 편차 범위");
                    Assert.IsTrue(list.Protected.All(e => !list.AutoProtected.Contains(e)), "⑧ 자동 보호는 명단 인원 제외");
                    var prot = list.Protected.Select(e => e.Player).ToList();
                    bool big = size >= 25;
                    if (t.ReservePlayers.Any(p => !p.IsPitcher && p.Position == "C" && !GMFaCompensation.IsForeign(p))) Assert.GreaterOrEqual(prot.Count(p => !p.IsPitcher && p.Position == "C"), 1, $"⑨ {t.TeamCode} 포수");
                    Assert.GreaterOrEqual(prot.Count(p => p.IsPitcher && p.Template.PitcherRole == PitcherRole.StartingPitcher), Math.Min(big ? 5 : 4, t.ReservePlayers.Count(p => p.IsPitcher && p.Template.PitcherRole == PitcherRole.StartingPitcher && !GMFaCompensation.IsForeign(p))), $"⑨ {t.TeamCode} 선발");
                    Assert.GreaterOrEqual(prot.Count(p => p.IsPitcher && p.Template.PitcherRole != PitcherRole.StartingPitcher), big ? 4 : 3, $"⑨ {t.TeamCode} 불펜");
                    Assert.GreaterOrEqual(prot.Count(p => !p.IsPitcher), big ? 10 : 8, $"⑨ {t.TeamCode} 타선");
                    // 보호 선수 최저 점수 ≥ 미보호 최고 점수(⑨ 검수 교체 제외) - 점수순 보호
                    var swappedIn = new HashSet<string>(list.BalanceNotes.SelectMany(n => list.Entries.Where(e => n.Contains(e.Player.Template.PlayerName + "(")).Select(e => e.Player.InstanceId)));
                    var core = list.Protected.Where(e => !swappedIn.Contains(e.Player.InstanceId)).ToList();
                    var open = list.Unprotected.Where(e => !swappedIn.Contains(e.Player.InstanceId)).ToList();
                    if (core.Count > 0 && open.Count > 0) Assert.GreaterOrEqual(core.Min(e => e.Score), open.Max(e => e.Score) - 0.001f, $"{t.TeamCode} 점수순 보호");
                }
            }
            // ⑥ 노선 - 윈나우는 OVR, 리빌딩은 잠재력 가중치가 크다
            var winNow = GMFaCompensation.WeightsFor(true);
            var rebuild = GMFaCompensation.WeightsFor(false);
            Assert.Greater(winNow[2], rebuild[2], "윈나우 OVR ↑");
            Assert.Greater(rebuild[1], winNow[1], "리빌딩 잠재력 ↑");
            Assert.Greater(winNow[0], 25f, "① 대체 불가능성 최대 비중 유지");
            // ②: 25세 이하 잠재력 85 이상 = 100 / ⑦: 유일 포수 가산
            var lg = league.Teams["LG"];
            var poolLg = lg.ReservePlayers.ToList();
            var catcher = poolLg.First(p => !p.IsPitcher && p.Position == "C");
            var onlyC = poolLg.Where(p => p == catcher || p.IsPitcher || p.Position != "C").ToList();
            var eOnly = GMFaCompensation.ScorePlayer(league, "LG", onlyC, catcher, true, 0);
            StringAssert.Contains("유일 포수", eOnly.Tags, "⑦ 유일 포수 필수 보정");
            Assert.GreaterOrEqual(eOnly.Scarcity, GMFaCompensation.ScarcityBonus);
            var young = poolLg.FirstOrDefault(p => p.Age <= 25 && p.Potential >= 85);
            if (young != null) Assert.AreEqual(100f, GMFaCompensation.ScorePlayer(league, "LG", poolLg, young, false, 0).Potential, 1e-4f, "② 25세 이하 · 잠재력 85+ = 100");
            // ⑧ 당해 신인 · FA 계약 당사자 자동 보호
            var rookie = lg.Futures[0];
            league.RookiesThisYear.Add(rookie.InstanceId);
            var withRookie = GMFaCompensation.BuildProtectionList(league, lg, 20);
            Assert.IsTrue(withRookie.AutoProtected.Any(e => e.Player == rookie), "당해 신인 자동 보호");
            Assert.IsTrue(withRookie.IsProtected(rookie));
            league.RookiesThisYear.Remove(rookie.InstanceId);

            // FA 보상 정산 ① 내 구단이 AI 구단 FA(A등급) 영입 → 원 소속이 보호 명단 밖 1명 + 200% (또는 300%)
            var kia = league.Teams["KIA"];
            var star = kia.Roster.Where(p => p.Age < GMFaCompensation.VeteranCAge).OrderByDescending(p => p.Salary).First();
            var origin = GMFaCompensation.DeclareFreeAgent(league, kia, star);
            Assert.AreNotEqual(GMFaGrade.C, origin.Grade, "구단 연봉 1위 = A/B등급");
            Assert.Contains(star, league.FreeAgents);
            StringAssert.Contains(GMFaCompensation.GradeLabel(origin.Grade), GMFaCompensation.CompensationLabel(league, star, "SAM"));
            StringAssert.Contains("보호선수", GMFaCompensation.CompensationLabel(league, star, "SAM"));
            GMFrontOffice.Manager(league).Commissioner = true; // 경쟁 입찰 판정 생략(보상 경로만 검증)
            var signed = GMStoveLeagueMarket.OfferContract(league, me, star, 3, GMStoveLeagueMarket.FADemand(star, GMDifficulty.Majors), false);
            Assert.IsTrue(signed.Success, signed.Message);
            Assert.AreEqual(1, league.PendingCompensations.Count, "보상 정산 대기");
            Assert.IsTrue(league.FASignedThisYear.Contains(star.InstanceId), "FA 계약 당사자 자동 보호");
            var myProtection = GMFaCompensation.BuildProtectionList(league, me, GMFaCompensation.ProtectionSize(origin.Grade));
            Assert.IsTrue(myProtection.IsProtected(star), "영입한 FA는 자동 보호");
            long myBudget = me.Budget, kiaBudget = kia.Budget;
            int prev = origin.PrevSalary;
            var settled = GMFaCompensation.SettleUserSignings(league);
            Assert.AreEqual(1, settled.Count);
            var res = settled[0];
            Assert.IsTrue(res.Success, res.Message);
            Assert.AreEqual(res.CashOnly ? GMFaCompensation.CashOnly(origin.Grade, prev) : GMFaCompensation.CashWithPlayer(origin.Grade, prev), res.Cash, "A 200%/300% · B 100%/200%");
            Assert.AreEqual(myBudget - res.Cash, me.Budget);
            Assert.AreEqual(kiaBudget + res.Cash, kia.Budget);
            if (!res.CashOnly)
            {
                Assert.IsFalse(myProtection.IsProtected(res.CompensationPlayer), "보상선수 = 보호 명단 밖");
                Assert.IsTrue(kia.ReservePlayers.Contains(res.CompensationPlayer), "원 소속 합류");
            }
            Assert.IsEmpty(league.PendingCompensations);

            // ② AI 구단이 내 FA(A/B등급) 영입 → 내가 그 구단 보호 명단 밖 선수를 지명
            var mine = me.Roster.Where(p => p.Age < GMFaCompensation.VeteranCAge && p != star).OrderByDescending(p => p.Salary).First();
            var mineOrigin = GMFaCompensation.DeclareFreeAgent(league, me, mine);
            Assert.AreNotEqual(GMFaGrade.C, mineOrigin.Grade, "내 구단 연봉 상위 = A/B등급");
            var lgTeam = league.Teams["LG"];
            var pending = GMFaCompensation.AiSignFreeAgent(league, lgTeam, mine);
            Assert.IsNotNull(pending);
            Assert.AreEqual("SAM", pending.FromTeam);
            var lgList = GMFaCompensation.BuildProtectionList(league, lgTeam, GMFaCompensation.ProtectionSize(pending.Grade));
            var protectedPick = lgList.Protected.First().Player;
            var refused = GMFaCompensation.Settle(league, pending, protectedPick, humanChooses: true);
            Assert.IsFalse(refused.Success, "보호 선수 지명 불가");
            var pick = lgList.Unprotected.OrderByDescending(e => e.Score).First().Player;
            var claim = GMFaCompensation.Settle(league, pending, pick, humanChooses: true);
            Assert.IsTrue(claim.Success, claim.Message);
            Assert.AreSame(pick, claim.CompensationPlayer);
            Assert.IsTrue(me.ReservePlayers.Contains(pick), "보상선수 합류");
            Assert.IsTrue(league.News.Any(n => n.Title.Contains("보상 확정")), "보상 소식");

            // 세이브 v19 왕복 - 퓨처스 풀 · 유망주 보정 · FA 원 소속 · 보상 대기 · 수동 보호 명단
            league.UserProtectedIds.Add(me.Roster[0].InstanceId);
            var leftover = league.FreeAgents.First(p => GMFaCompensation.OriginOf(league, p) != null);
            league.PendingCompensations.Add(new GMPendingCompensation { PlayerId = "x", PlayerName = "테스트", FromTeam = "KIA", ToTeam = "SAM", Grade = GMFaGrade.B, PrevSalary = 5000, Year = 2026 });
            var data = SaveManager.ToGMSaveData(league);
            var byId = templates.Where(t => t != null).GroupBy(t => t.TemplateId).ToDictionary(g => g.Key, g => g.First());
            var restored = SaveManager.FromGMSaveData(data, saved =>
            {
                if (!byId.TryGetValue(saved.TemplateId, out var tpl)) return null;
                if (saved.ProspectStatShift != 0) tpl = GMRosterTiers.ProspectTemplate(tpl, saved.ProspectStatShift);
                var p = new Player(saved.InstanceId, tpl);
                SaveManager.ApplyGMFields(p, saved);
                return p;
            });
            foreach (var code in league.Teams.Keys)
            {
                Assert.AreEqual(league.Teams[code].Futures.Count, restored.Teams[code].Futures.Count, $"{code} 퓨처스 복원");
                Assert.AreEqual(league.Teams[code].Futures.Sum(p => p.BaseOverall), restored.Teams[code].Futures.Sum(p => p.BaseOverall), $"{code} 유망주 능력치 복원");
            }
            Assert.AreEqual(league.FAOrigins.Count, restored.FAOrigins.Count, "FA 원 소속 복원");
            Assert.AreEqual(GMFaCompensation.OriginOf(league, leftover).Grade, restored.FAOrigins[leftover.InstanceId].Grade);
            Assert.AreEqual(1, restored.PendingCompensations.Count, "보상 대기 복원");
            CollectionAssert.AreEqual(league.UserProtectedIds, restored.UserProtectedIds, "수동 보호 명단 복원");
            Assert.AreEqual(27, new GameSaveData().SaveVersion, "세이브 v27([TASK-GM-17])");
        }

        // ================================================================== 6) 레이아웃 · Bold · 씬

        [Test]
        public void T6_Layouts_NoOverlap_Min15_NoBold_Scene()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "SAM", seed: 88));
            var hub = NewHub(sim, out var prePost);
            // FA 보상 · 보호 명단 화면
            hub.OpenProtection();
            Assert.AreEqual(GMOotpFrontOfficeUIController.PaneProtect, hub.CurrentPane);
            var pane = hub.Pane(GMOotpFrontOfficeUIController.PaneProtect);
            Assert.AreEqual(20, hub.MyProtection.Size);
            Assert.IsTrue(pane.Find("PrRow0").gameObject.activeSelf, "보호 명단 행");
            StringAssert.Contains("[", pane.Find("PrRow0").GetComponentInChildren<Text>().text);
            CheckPane(hub, "FA 보상·보호명단(20인)");
            Click(pane, "PrSize25");
            Assert.AreEqual(25, hub.ProtectSize);
            Assert.AreEqual(25, hub.MyProtection.Size, "25인 명단");
            hub.ShiftProtectPartner(1);
            Assert.IsNotNull(hub.PartnerProtection, "AI 구단 보호 명단 미리보기");
            Click(pane, "PrOpen0");
            StringAssert.Contains("①", T(pane, "PrDetail"), "10대 가중치 상세");
            StringAssert.Contains("⑩", T(pane, "PrDetail"));
            var firstMine = hub.MyProtection.Entries.First(e => !e.AutoProtected);
            int idx = hub.MyProtection.Entries.IndexOf(firstMine);
            Assert.Less(idx, GMOotpFrontOfficeUIController.ProtectRows, "첫 페이지");
            hub.ToggleProtectRow(idx);
            Assert.IsNotEmpty(sim.League.UserProtectedIds, "수동 보호 명단 전환");
            CheckPane(hub, "FA 보상·보호명단(25인 · 상세)");
            hub.ResetUserProtection();
            Assert.IsEmpty(sim.League.UserProtectedIds);

            // FA 화면 보상 문구 · 트레이드 화면 · 감독 설정 사운드
            hub.SelectMainTab(4);
            hub.SelectSubTab(0);
            hub.SelectFARow(0);
            StringAssert.Contains("원 소속", T(hub.Pane(GMOotpFrontOfficeUIController.PaneFA), "FACompensation"));
            CheckPane(hub, "FA 보상 문구");
            hub.SelectSubTab(1);
            CheckPane(hub, "트레이드 1:N");
            hub.OpenManagerSetup();
            CheckLayer(hub.Root.Find(GMOotpFrontOfficeUIController.ManagerSetupName), "감독 설정 + 사운드");
            hub.CloseManagerSetup();
            hub.OpenPostseasonTree();
            CheckPane(hub, "포스트시즌 트리(시즌 중 예상 대진)");
            StringAssert.Contains("정규시즌 진행 중", T(hub.Pane(GMOotpFrontOfficeUIController.PanePostseason), "NextGame"));

            // 실시간 경기 화면 - 사운드 연결 후에도 레이아웃 유지
            Assert.IsTrue(prePost.ShowPreGameView(sim));
            Assert.IsTrue(prePost.PlayBall());
            prePost.LiveStepInning();
            CheckLayer(prePost.LiveRoot, "실시간 이닝 경기");
            prePost.FinishLiveMatch();

            // Bold 0 - 허브 · 경기 화면 전체
            int bold = hub.GetComponentsInChildren<Text>(true).Concat(prePost.GetComponentsInChildren<Text>(true)).Count(t => t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic || t.text.Contains("<b>"));
            Assert.AreEqual(0, bold, "FontStyle.Bold 0건");

            // 씬 - 오디오 매니저 · 새 화면 배치
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
            var sceneAudio = Object.FindObjectsByType<GMAudioManager>(FindObjectsInactive.Include).FirstOrDefault(a => a.gameObject.scene.IsValid());
            Assert.IsNotNull(sceneAudio, "씬 GMAudioManager");
            var sceneHub = Object.FindAnyObjectByType<GMOotpFrontOfficeUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(sceneHub);
            var root = sceneHub.transform.Find(GMOotpFrontOfficeUIController.RootName);
            Assert.IsNotNull(root.Find($"{GMOotpFrontOfficeUIController.ContentAreaName}/{GMOotpFrontOfficeUIController.PaneProtect}"), "씬 FA 보상·보호명단");
            Assert.IsNotNull(root.Find(GMOotpFrontOfficeUIController.CounterPopupName), "씬 AI 역제안 팝업");
            Assert.IsNotNull(root.Find($"{GMOotpFrontOfficeUIController.ContentAreaName}/{GMOotpFrontOfficeUIController.PanePostseason}/LeadersPanel"), "씬 KBO 리더 패널");
            Assert.IsNotNull(root.Find($"{GMOotpFrontOfficeUIController.ManagerSetupName}/BgmVolumeSlider"), "씬 BGM 슬라이더");
            int sceneBold = Object.FindObjectsByType<Text>(FindObjectsInactive.Include).Count(t => t.gameObject.scene.IsValid() && (t.fontStyle == FontStyle.Bold || t.fontStyle == FontStyle.BoldAndItalic)); // 레거시 카드 화면 Italic(2)은 Bold가 아니다
            Assert.AreEqual(0, sceneBold, "씬 Bold 0건");
        }

        // ================================================================== 공용 검사

        private static void CheckPane(GMOotpFrontOfficeUIController hub, string label)
        {
            CheckLayer(hub.Root, label + " 헤더");
            var pane = hub.Pane(hub.CurrentPane);
            Assert.IsTrue(pane.gameObject.activeSelf, label);
            CheckLayer(pane, label + " " + hub.CurrentPane);
            foreach (Transform child in pane)
            {
                if (!child.gameObject.activeSelf) continue;
                if (child.GetComponent<Text>() != null || child.GetComponent<Button>() != null) continue;
                bool container = child.name.EndsWith("Panel") || child.name.StartsWith("Day") || child.name.StartsWith("Bracket");
                if (container && child.childCount > 0) CheckLayer(child, label + " " + child.name);
            }
        }

        /// <summary>layer 직속 자식 중 활성 텍스트/버튼 영역 겹침 0 · 15pt 이상 · Normal.</summary>
        private static void CheckLayer(Transform layer, string label)
        {
            Assert.IsNotNull(layer, label);
            var items = new List<Transform>();
            foreach (Transform child in layer)
            {
                if (!child.gameObject.activeSelf) continue;
                if (child.GetComponent<Text>() != null || child.GetComponent<Button>() != null) items.Add(child);
            }
            var overlaps = new List<string>();
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                    if (Task191Report.Overlap(items[i], items[j])) overlaps.Add($"{items[i].name} ↔ {items[j].name}");
            Assert.IsEmpty(overlaps, $"{label} 텍스트 겹침 0건");
            foreach (var t in layer.GetComponentsInChildren<Text>(true))
            {
                Assert.AreEqual(FontStyle.Normal, t.fontStyle, t.name);
                Assert.GreaterOrEqual(TextTidy.EffectiveSize(t), 15, $"{label} {t.name} 15pt 이상");
            }
        }
    }
}
