using System;
using System.Collections;
using System.Collections.Generic;
using KBOManager.Core;
using KBOManager.Services;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// [TASK-GM-08 → TASK-GM-12] 구단 오디오 매니저 - BGM 2채널(A/B 크로스페이드) · 응원가/아웃송(곡) · 효과음(환호) · 치어리더 단상 믹스.
    ///   - 클립: TeamAudioProfile 키 → Resources/Audio/{키}(삼성 17곡, ogg) 또는 GMAudioSynth 합성 앰비언스(synth:*)
    ///   - [GM-12] 단일 인스턴스: Awake에서 중복(씬 재로드로 생긴 두 번째 매니저)을 즉시 파괴하고 DontDestroyOnLoad로 유지한다.
    ///   - [GM-12] 재생은 상태 변경 트리거(화면 진입 · 이벤트 · 타석)에서만 1회 호출한다. 같은 BGM 그룹 화면으로 옮기면 곡을 끊지 않는다(Keep Playing) -
    ///     예전에는 프런트 오피스 Refresh마다 AudioSource.isPlaying(스트리밍 클립은 로드 직후 잠시 false)을 보고 곡을 다시 틀어 1초 만에 재시작 · 겹침 잡음이 났다.
    ///     이제 "재생 중" 판단은 논리 상태(IsBgmPlaying)로 한다. Update는 곡 종료 감지(1회 플래그)와 볼륨 추종만 한다.
    ///   - [GM-12] 화면 그룹 로테이션(GMBgmRotation: 가중치 · 최근 2곡 제외 · 같은 화면 90초 쿨다운) · 4초 크로스페이드 · 이벤트 전용곡 하이재킹(끝나면 화면 풀로 재개).
    ///   - [GM-12] 소리를 실제로 내는 모든 메서드(EmitPlay · EmitOneShot · 크로스페이드) 최상단에 Application.isBatchMode 가드 -
    ///     CLI 테스트 중에는 선곡 · 클립 연결 · 기록 같은 논리만 돌고 AudioSource는 한 번도 재생되지 않는다(SuppressedOutputCount로 확인).
    ///   - 응원가가 나오는 동안 BGM은 30%로 줄고(덕킹), 경기 화면 앰비언스는 60%로 깐다. 볼륨: BGM · SFX 0~1, PlayerPrefs 저장.
    ///   - 치어리더 4~6인 엔트리 단상 버프가 켜지면 응원 믹스 레이어(합성 구호)를 경기 중 깔고 응원가 볼륨을 인원당 +4% 올린다.
    /// 씬에 없으면 Ensure()가 만든다(에디터 비실행 상태에서는 저장되지 않는 숨김 오브젝트).
    /// </summary>
    public class GMAudioManager : MonoBehaviour
    {
        public const string BgmPrefKey = "GM_BGM_VOLUME", SfxPrefKey = "GM_SFX_VOLUME";
        public const float DefaultBgmVolume = 0.6f, DefaultSfxVolume = 0.8f;
        public const float DuckFactor = 0.3f;
        public const float MatchBgmFactor = 0.6f;
        public const float CheerMixPerMember = 0.04f;
        public const float CrossfadeSeconds = 4f;   // 화면 · 로테이션 전환(지시서 3~5초)
        public const float EventFadeSeconds = 3f;   // 이벤트 하이재킹 · 재개
        public const string ResourceRoot = "Audio/";
        public const int MaxHistory = 64;

        private static GMAudioManager instance;
        public static GMAudioManager Instance => instance != null ? instance : instance = FindAnyObjectByType<GMAudioManager>(FindObjectsInactive.Include);

        /// <summary>[GM-12] 실제 소리 출력 허용 여부 - CLI Batchmode(자동 테스트)에서는 항상 false.</summary>
        public static bool SoundOutputEnabled => !Application.isBatchMode;

        private AudioSource bgmA, bgmB, song, sfx, mix;
        private int activeBgm;                 // 0 = A, 1 = B
        private float fadeA = 1f, fadeB;       // 크로스페이드 배수
        private Coroutine fadeRoutine;
        private bool fading, advanceRequested, bgmActive;
        private float trackStartedAt;
        private readonly GMBgmRotation rotation = new GMBgmRotation();
        private readonly Queue<string> eventChain = new Queue<string>();
        private TeamAudioProfile currentProfile;
        private readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        private readonly List<(GMAudioCue cue, string key)> history = new List<(GMAudioCue, string)>();
        private readonly List<(GMAudioCue cue, string key)> requests = new List<(GMAudioCue, string)>();
        private readonly List<GMAudioEvent> eventHistory = new List<GMAudioEvent>();
        private float bgmVolume = -1f, sfxVolume = -1f;
        private int variant;
        private GMAudioCue? songCue;

        public string CurrentBgmKey { get; private set; }
        public string CurrentBgmTeam { get; private set; }
        public GMAudioCue? LastCue { get; private set; }
        public string LastClipKey { get; private set; }
        public bool CheerMixActive { get; private set; }
        public int CheerMixMembers { get; private set; }
        public IReadOnlyList<(GMAudioCue cue, string key)> History => history;
        /// <summary>경기 큐 요청 기록(재생 중인 더 높은 우선순위 곡 때문에 건너뛴 요청 포함) - 트리거 → 클립 바인딩 확인용.</summary>
        public IReadOnlyList<(GMAudioCue cue, string key)> Requests => requests;
        public bool IsDucking => song != null && song.isPlaying;
        /// <summary>현재 BGM을 내는 채널(크로스페이드 A/B 중 활성 쪽).</summary>
        public AudioSource BgmSource => ActiveSource;
        public AudioSource SongSource => Sources().song;

        // [GM-12] 로테이션 · 화면 그룹 · 이벤트 상태
        public GMAudioScreen? CurrentScreen { get; private set; }
        public GMAudioPlaylist CurrentPlaylist { get; private set; }
        public string CurrentGroupId { get; private set; }
        public GMAudioEvent? ActiveEvent { get; private set; }
        public IReadOnlyList<GMAudioEvent> EventHistory => eventHistory;
        /// <summary>논리 BGM 재생 상태(곡이 걸려 있고 정지되지 않음) - 화면 전환 Keep Playing 판정 기준.</summary>
        public bool IsBgmPlaying => bgmActive && CurrentBgmKey != null;
        /// <summary>새 BGM 곡을 시작한 횟수 - 같은 그룹 화면 이동에서 늘지 않아야 한다.</summary>
        public int BgmTrackStarts { get; private set; }
        /// <summary>Batchmode 가드로 막힌 출력 호출 수.</summary>
        public int SuppressedOutputCount { get; private set; }
        public GMBgmRotation Rotation => rotation;
        public int PendingEventTracks => eventChain.Count;

        /// <summary>씬의 매니저를 찾고, 없으면 만든다.</summary>
        public static GMAudioManager Ensure()
        {
            if (Instance != null) return instance;
            var go = new GameObject("GMAudioManager");
            if (Application.isPlaying) DontDestroyOnLoad(go);
            else go.hideFlags = HideFlags.HideAndDontSave;
            instance = go.AddComponent<GMAudioManager>();
            return instance;
        }

        private void Awake()
        {
            // [GM-12] 단일 인스턴스 - 씬을 다시 열 때 생긴 두 번째 매니저는 소리를 내기 전에 파괴한다(BGM 2중 재생 방지)
            if (instance != null && instance != this)
            {
                if (Application.isPlaying) { Destroy(gameObject); return; }
            }
            instance = this;
            if (Application.isPlaying)
            {
                if (transform.parent != null) transform.SetParent(null, false);
                DontDestroyOnLoad(gameObject);
            }
            Sources();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private (AudioSource bgm, AudioSource song, AudioSource sfx, AudioSource mix) Sources()
        {
            if (bgmA == null)
            {
                // 씬에 저장된 매니저(Setup이 배치)는 이미 붙은 AudioSource를 순서대로 재사용한다(중복 추가 방지) - [0] BGM A [1] 곡 [2] 효과음 [3] 믹스 [4] BGM B
                var existing = GetComponents<AudioSource>();
                if (existing.Length > 0) bgmA = existing[0];
                if (existing.Length > 1) song = existing[1];
                if (existing.Length > 2) sfx = existing[2];
                if (existing.Length > 3) mix = existing[3];
                if (existing.Length > 4) bgmB = existing[4];
                if (bgmA != null) { bgmA.loop = true; bgmA.playOnAwake = false; }
                if (mix != null) mix.loop = true;
            }
            if (bgmA == null) { bgmA = gameObject.AddComponent<AudioSource>(); bgmA.loop = true; bgmA.playOnAwake = false; }
            if (song == null) { song = gameObject.AddComponent<AudioSource>(); song.loop = false; song.playOnAwake = false; }
            if (sfx == null) { sfx = gameObject.AddComponent<AudioSource>(); sfx.loop = false; sfx.playOnAwake = false; }
            if (mix == null) { mix = gameObject.AddComponent<AudioSource>(); mix.loop = true; mix.playOnAwake = false; }
            if (bgmB == null) { bgmB = gameObject.AddComponent<AudioSource>(); bgmB.loop = true; bgmB.playOnAwake = false; }
            return (ActiveSourceRaw, song, sfx, mix);
        }

        private AudioSource ActiveSourceRaw => activeBgm == 0 ? bgmA : bgmB;
        private AudioSource ActiveSource { get { Sources(); return ActiveSourceRaw; } }
        private AudioSource IdleSource => activeBgm == 0 ? bgmB : bgmA;
        private static float Now => Time.realtimeSinceStartup;

        // ================================================================== 볼륨

        public float BgmVolume
        {
            get { if (bgmVolume < 0f) bgmVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(BgmPrefKey, DefaultBgmVolume)); return bgmVolume; }
            set { bgmVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(BgmPrefKey, bgmVolume); ApplyVolumes(); }
        }

        public float SfxVolume
        {
            get { if (sfxVolume < 0f) sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(SfxPrefKey, DefaultSfxVolume)); return sfxVolume; }
            set { sfxVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(SfxPrefKey, sfxVolume); ApplyVolumes(); }
        }

        /// <summary>응원가 볼륨 배수 - 치어리더 단상 믹스가 켜져 있으면 인원당 +4%(4~6인 = +16~24%).</summary>
        public float CheerBoost => CheerMixActive ? 1f + CheerMixPerMember * CheerMixMembers : 1f;

        public void ApplyVolumes()
        {
            var s = Sources();
            float baseBgm = BgmVolume * (s.song.isPlaying ? DuckFactor : 1f) * (CurrentScreen == GMAudioScreen.Match && !ActiveEvent.HasValue ? MatchBgmFactor : 1f);
            bgmA.volume = baseBgm * fadeA;
            bgmB.volume = baseBgm * fadeB;
            s.song.volume = Mathf.Clamp01(SfxVolume * CheerBoost);
            s.sfx.volume = SfxVolume;
            s.mix.volume = Mathf.Clamp01(SfxVolume * 0.35f * CheerBoost);
        }

        /// <summary>볼륨 추종 · 곡 종료 감지만 한다(재생 호출은 종료 감지 1회 플래그로만 - 매 프레임 재생 금지).</summary>
        private void Update()
        {
            if (bgmA == null || song == null) return;
            ApplyVolumes();
            if (!song.isPlaying) songCue = null;
            if (!SoundOutputEnabled || !bgmActive || fading || advanceRequested) return;
            var src = ActiveSourceRaw;
            if (src == null || src.clip == null) return;
            bool hasNext = ActiveEvent.HasValue || (CurrentPlaylist != null && CurrentPlaylist.Tracks.Length > 1);
            if (!hasNext) return; // 1곡 풀(합성 앰비언스)은 루프
            float lead = Mathf.Min(CrossfadeSeconds, src.clip.length * 0.25f);
            // 스트리밍 클립은 Play 직후 잠시 isPlaying = false - 시작 1.5초 안의 정지는 종료로 보지 않는다
            bool ended = src.isPlaying ? src.clip.length - src.time <= lead : Now - trackStartedAt > 1.5f;
            if (!ended) return;
            advanceRequested = true;
            AdvanceTrack();
        }

        // ================================================================== 클립

        public AudioClip LoadClip(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (cache.TryGetValue(key, out var clip) && clip != null) return clip;
            clip = key.StartsWith(TeamAudioProfile.SynthPrefix, StringComparison.Ordinal) ? GMAudioSynth.Create(key) : Resources.Load<AudioClip>(ResourceRoot + key);
            cache[key] = clip;
            return clip;
        }

        private void Record(GMAudioCue cue, string key)
        {
            LastCue = cue;
            LastClipKey = key;
            history.Add((cue, key));
            if (history.Count > MaxHistory) history.RemoveAt(0);
        }

        public void ClearHistory() { history.Clear(); requests.Clear(); eventHistory.Clear(); LastCue = null; LastClipKey = null; }

        // ================================================================== 출력(Batchmode 가드)

        private void EmitPlay(AudioSource source)
        {
            if (Application.isBatchMode) { SuppressedOutputCount++; return; }
            if (source != null && source.clip != null) source.Play();
        }

        private void EmitOneShot(AudioClip clip, float volume)
        {
            if (Application.isBatchMode) { SuppressedOutputCount++; return; }
            if (clip != null) sfx.PlayOneShot(clip, volume);
        }

        // ================================================================== [GM-12] 화면 BGM · 로테이션 · 크로스페이드

        /// <summary>
        /// 화면 진입 트리거 - 화면 그룹 풀에서 곡을 고른다. 같은 구단 · 같은 그룹 BGM이 이미 나오면 그대로 이어 간다(Keep Playing).
        /// 이벤트 전용곡이 나오는 중이면 화면만 기억하고, 이벤트가 끝날 때 새 화면 풀로 재개한다. 반환 = 현재 BGM 키.
        /// </summary>
        public string EnterScreen(GMAudioScreen screen, string teamCode)
        {
            var profile = TeamAudioProfile.For(teamCode);
            var playlist = profile.PlaylistFor(screen);
            bool keep = IsBgmPlaying && CurrentBgmTeam == profile.TeamCode && CurrentGroupId == playlist.GroupId;
            currentProfile = profile;
            CurrentScreen = screen;
            CurrentPlaylist = playlist;
            CurrentBgmTeam = profile.TeamCode;
            if (ActiveEvent.HasValue && IsBgmPlaying) { CurrentGroupId = playlist.GroupId; ApplyVolumes(); return CurrentBgmKey; }
            ApplyVolumes();
            if (keep) return CurrentBgmKey;
            CurrentGroupId = playlist.GroupId;
            return StartFromPlaylist(CrossfadeSeconds);
        }

        /// <summary>프런트 오피스 · 메인 홈 구단 테마 BGM(허브 그룹 로테이션). 같은 구단 · 같은 그룹이면 그대로 둔다.</summary>
        public string PlayTeamBgm(string teamCode) => EnterScreen(GMAudioScreen.Hub, teamCode);

        /// <summary>실시간 경기 앰비언스(관중 소리 루프) - 구단 BGM 대신 60% 볼륨으로 깐다.</summary>
        public void PlayMatchAmbience(string teamCode) => EnterScreen(GMAudioScreen.Match, teamCode);

        private string StartFromPlaylist(float fade)
        {
            if (CurrentPlaylist == null) return null;
            string key = rotation.Pick(CurrentPlaylist, Now);
            StartTrack(key, CurrentPlaylist.Id, true, CurrentScreen == GMAudioScreen.Match ? GMAudioCue.MatchAmbience : GMAudioCue.FrontOfficeBgm, fade);
            return key;
        }

        /// <summary>새 곡을 쉬는 채널에 올리고 크로스페이드로 교체한다(비실행 · Batchmode = 즉시 교체).</summary>
        private void StartTrack(string key, string playlistId, bool loop, GMAudioCue cue, float fade)
        {
            Sources();
            CurrentBgmKey = key;
            bgmActive = key != null;
            advanceRequested = false;
            trackStartedAt = Now;
            BgmTrackStarts++;
            Record(cue, key);
            rotation.MarkPlayed(key, playlistId, Now);
            if (fadeRoutine != null) { StopCoroutine(fadeRoutine); fadeRoutine = null; fading = false; }
            var from = ActiveSourceRaw;
            var to = IdleSource;
            to.Stop();
            to.clip = LoadClip(key);
            to.loop = loop;
            activeBgm = 1 - activeBgm;
            if (fade <= 0f || !Application.isPlaying || !SoundOutputEnabled || !from.isPlaying)
            {
                from.Stop();
                SetFade(to, 1f);
                SetFade(from, 0f);
                ApplyVolumes();
                EmitPlay(to);
                return;
            }
            fadeRoutine = StartCoroutine(Crossfade(from, to, fade));
        }

        private void SetFade(AudioSource source, float value)
        {
            if (source == bgmA) fadeA = value; else fadeB = value;
        }

        private float FadeOf(AudioSource source) => source == bgmA ? fadeA : fadeB;

        private IEnumerator Crossfade(AudioSource from, AudioSource to, float seconds)
        {
            if (Application.isBatchMode) { SuppressedOutputCount++; yield break; }
            fading = true;
            float fromStart = FadeOf(from);
            SetFade(to, 0f);
            ApplyVolumes();
            EmitPlay(to);
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / seconds);
                SetFade(to, k);
                SetFade(from, fromStart * (1f - k));
                ApplyVolumes();
                yield return null;
            }
            SetFade(to, 1f);
            SetFade(from, 0f);
            from.Stop();
            ApplyVolumes();
            fading = false;
            fadeRoutine = null;
        }

        /// <summary>곡이 끝났을 때 - 이벤트 체인 다음 곡 → 체인 종료면 화면 풀로 재개 → 일반 로테이션이면 다음 곡.</summary>
        private void AdvanceTrack()
        {
            if (ActiveEvent.HasValue)
            {
                if (eventChain.Count > 0) { NextEventTrack(); return; }
                ActiveEvent = null;
                StartFromPlaylist(EventFadeSeconds);
                return;
            }
            if (CurrentPlaylist != null && CurrentPlaylist.Tracks.Length > 1) StartFromPlaylist(CrossfadeSeconds);
            else advanceRequested = false;
        }

        /// <summary>곡 종료를 바로 처리한다(테스트 · 디버그 - 실행 중에는 Update가 곡 끝 4초 전에 1회 호출).</summary>
        public void SimulateTrackEnd() => AdvanceTrack();

        // ================================================================== [GM-12] 이벤트 전용곡 하이재킹

        /// <summary>
        /// 중요 이벤트 - 현재 BGM을 페이드아웃하고 이벤트 전용곡 체인(예: 승리를 위해 → 엘도라도)을 차례로 튼다.
        /// 체인이 끝나거나 EndEvent()가 불리면 현재 화면 풀에서 새 곡(최근 2곡 제외)으로 재개한다. 반환 = 첫 곡 키.
        /// </summary>
        public string PlayEvent(GMAudioEvent ev, string teamCode = null)
        {
            var profile = teamCode != null ? TeamAudioProfile.For(teamCode) : currentProfile ?? TeamAudioProfile.For(CurrentBgmTeam);
            if (currentProfile == null) currentProfile = profile;
            if (CurrentPlaylist == null) { CurrentScreen = GMAudioScreen.Hub; CurrentPlaylist = profile.PlaylistFor(GMAudioScreen.Hub); CurrentGroupId = CurrentPlaylist.GroupId; CurrentBgmTeam = profile.TeamCode; }
            eventChain.Clear();
            foreach (var k in profile.EventChain(ev)) eventChain.Enqueue(k);
            ActiveEvent = ev;
            eventHistory.Add(ev);
            if (eventHistory.Count > MaxHistory) eventHistory.RemoveAt(0);
            return NextEventTrack();
        }

        private string NextEventTrack()
        {
            if (eventChain.Count == 0) return null;
            string key = eventChain.Dequeue();
            StartTrack(key, null, false, GMAudioCue.EventBgm, EventFadeSeconds);
            return key;
        }

        /// <summary>이벤트 창이 닫힘 - 남은 체인을 버리고 현재 화면 풀에서 새 곡으로 재개한다.</summary>
        public void EndEvent()
        {
            if (!ActiveEvent.HasValue) return;
            ActiveEvent = null;
            eventChain.Clear();
            StartFromPlaylist(EventFadeSeconds);
        }

        // ================================================================== 경기 큐

        /// <summary>경기 큐 재생 - 응원가(곡 채널)는 우선순위가 같거나 높을 때만 재생 중인 곡을 끊는다. 하이라이트 · 승리는 환호 효과음을 겹친다. 재생한 클립 키(무시되면 null).</summary>
        public string PlayCue(GMAudioCue cue, string teamCode, int inning = 0)
        {
            var s = Sources();
            var profile = TeamAudioProfile.For(teamCode);
            if (cue == GMAudioCue.FrontOfficeBgm) return PlayTeamBgm(teamCode);
            if (cue == GMAudioCue.MatchAmbience) { PlayMatchAmbience(teamCode); return CurrentBgmKey; }
            if (cue == GMAudioCue.CrowdCheer || cue == GMAudioCue.OutCheer)
            {
                string fx = profile.ClipFor(cue);
                PlayOneShot(cue, fx);
                return fx;
            }
            string key = profile.ClipFor(cue, variant++, inning);
            requests.Add((cue, key));
            if (requests.Count > MaxHistory) requests.RemoveAt(0);
            if (s.song.isPlaying && songCue.HasValue && GMLiveAudioDirector.Priority(cue) < GMLiveAudioDirector.Priority(songCue.Value)) return null;
            Record(cue, key);
            songCue = cue;
            var clip = LoadClip(key);
            s.song.Stop();
            s.song.clip = clip;
            ApplyVolumes();
            EmitPlay(s.song);
            if (cue == GMAudioCue.HighlightSong || cue == GMAudioCue.ComebackSong || cue == GMAudioCue.WinSong || cue == GMAudioCue.BigInningSong) PlayOneShot(GMAudioCue.CrowdCheer, TeamAudioProfile.SynthCheer);
            return key;
        }

        private void PlayOneShot(GMAudioCue cue, string key)
        {
            Sources();
            var clip = LoadClip(key);
            history.Add((cue, key));
            if (history.Count > MaxHistory) history.RemoveAt(0);
            EmitOneShot(clip, SfxVolume);
        }

        /// <summary>치어리더 단상 응원 버프 - 엔트리 4~6인이고 버프가 켜져 있으면 응원 믹스 레이어를 깔고 응원가 볼륨을 올린다.</summary>
        public void SetCheerleaderMix(int entryCount, bool buffActive)
        {
            var s = Sources();
            bool wasActive = CheerMixActive;
            CheerMixActive = buffActive && GMCheerleaderRules.IsValidEntryCount(entryCount);
            CheerMixMembers = CheerMixActive ? entryCount : 0;
            if (CheerMixActive)
            {
                var clip = LoadClip(TeamAudioProfile.SynthChant);
                if (s.mix.clip != clip) s.mix.clip = clip;
                if (!wasActive) Record(GMAudioCue.CheerleaderMix, TeamAudioProfile.SynthChant);
                if (!s.mix.isPlaying) EmitPlay(s.mix);
            }
            else s.mix.Stop();
            ApplyVolumes();
        }

        /// <summary>경기 사운드 정지(곡 · 효과음 · 응원 믹스) - 경기 결과 화면으로 넘어갈 때.</summary>
        public void StopMatchAudio()
        {
            var s = Sources();
            s.song.Stop();
            s.sfx.Stop();
            s.mix.Stop();
            songCue = null;
            CheerMixActive = false;
            CheerMixMembers = 0;
            ApplyVolumes();
        }

        public void StopAll()
        {
            StopMatchAudio();
            if (fadeRoutine != null) { StopCoroutine(fadeRoutine); fadeRoutine = null; }
            fading = false;
            advanceRequested = false;
            bgmA.Stop();
            bgmB.Stop();
            fadeA = 1f;
            fadeB = 0f;
            activeBgm = 0;
            bgmActive = false;
            eventChain.Clear();
            ActiveEvent = null;
            CurrentBgmKey = null;
            CurrentBgmTeam = null;
            CurrentScreen = null;
            CurrentPlaylist = null;
            CurrentGroupId = null;
            currentProfile = null;
            ApplyVolumes();
        }
    }

    /// <summary>
    /// [TASK-GM-08] 기본 앰비언스 합성기 - 구단 음원이 없을 때 쓰는 관중 소리(필터드 노이즈). 결정적 난수(같은 키 = 같은 파형).
    ///   synth:crowd 4초 루프(웅성임) · synth:cheer 1.6초 환호(상승 → 감쇠) · synth:clap 0.9초 박수 · synth:chant 2초 루프(박자 구호).
    ///   [TASK-GM-12] synth:tension 8초 공통 긴장 BGM(저음 맥박 패드) · synth:defeat 6초 공통 패배 BGM(하강 저음) - 전용 음원이 들어오면 프로필 키만 바꾼다.
    /// </summary>
    public static class GMAudioSynth
    {
        public const int SampleRate = 22050;

        public static AudioClip Create(string key)
        {
            switch (key)
            {
                case TeamAudioProfile.SynthCheer: return Build(key, 1.6f, 11, (t, n, d) => n * Envelope(t / d, 0.25f) * 0.9f);
                case TeamAudioProfile.SynthClap: return Build(key, 0.9f, 23, (t, n, d) => n * Clap(t) * 0.8f);
                case TeamAudioProfile.SynthChant: return Build(key, 2.0f, 31, (t, n, d) => n * (0.35f + 0.65f * Beat(t, 0.5f)) * 0.55f);
                case TeamAudioProfile.SynthTension: return Build(key, 8.0f, 41, (t, n, d) => (Tone(t, 55f) * 0.5f + Tone(t, 58.3f) * 0.4f + n * 0.08f) * (0.35f + 0.65f * Beat(t, 0.85f)) * 0.6f);
                case TeamAudioProfile.SynthDefeat: return Build(key, 6.0f, 43, (t, n, d) => (Tone(t, 110f - 40f * t / d) * 0.6f + Tone(t, 82.4f - 30f * t / d) * 0.4f) * Mathf.Exp(-t * 0.35f) * 0.6f);
                default: return Build(TeamAudioProfile.SynthCrowd, 4.0f, 7, (t, n, d) => n * (0.55f + 0.15f * Mathf.Sin(t * Mathf.PI * 0.5f)) * 0.5f);
            }
        }

        private static float Envelope(float x, float attack) => x < attack ? x / attack : Mathf.Exp(-(x - attack) * 3.2f);
        private static float Clap(float t) { float local = t % 0.3f; return Mathf.Exp(-local * 38f); }
        private static float Beat(float t, float period) { float local = t % period; return Mathf.Exp(-local * 9f); }
        private static float Tone(float t, float hz) => Mathf.Sin(2f * Mathf.PI * hz * t);

        private static AudioClip Build(string name, float seconds, int seed, Func<float, float, float, float> shape)
        {
            int samples = Mathf.Max(1, (int)(SampleRate * seconds));
            var data = new float[samples];
            var rng = new System.Random(seed);
            float lp = 0f;
            for (int i = 0; i < samples; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.18f * (white - lp); // 1차 저역 통과 - 관중 웅성임 톤
                data[i] = Mathf.Clamp(shape(i / (float)SampleRate, lp * 2.2f, seconds), -1f, 1f);
            }
            // 루프 이음매 페이드(앞뒤 20ms)
            int fade = Mathf.Min(samples / 4, SampleRate / 50);
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; data[i] *= k; data[samples - 1 - i] *= k; }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
