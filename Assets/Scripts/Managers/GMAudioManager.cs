using System;
using System.Collections.Generic;
using KBOManager.Core;
using KBOManager.Services;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>
    /// [TASK-GM-08] 구단 오디오 매니저 - BGM(루프) · 응원가/아웃송(곡) · 효과음(환호) · 치어리더 단상 믹스 4채널.
    ///   - 클립: TeamAudioProfile 키 → Resources/Audio/{키}(삼성 17곡, ogg) 또는 GMAudioSynth 합성 앰비언스(synth:*)
    ///   - 응원가가 나오는 동안 BGM은 30%로 줄고(덕킹), 끝나면 되돌린다. 더 높은 우선순위 큐만 재생 중인 곡을 끊는다.
    ///   - 볼륨: BGM · SFX 0~1, PlayerPrefs(GM_BGM_VOLUME · GM_SFX_VOLUME) 저장 - 감독 설정 모달 슬라이더와 연결.
    ///   - 치어리더 4~6인 엔트리 단상 버프가 켜지면 응원 믹스 레이어(합성 구호)를 경기 중 깔고 응원가 볼륨을 인원당 +4% 올린다.
    /// 씬에 없으면 Ensure()가 만든다(에디터 비실행 상태에서는 저장되지 않는 숨김 오브젝트).
    /// </summary>
    public class GMAudioManager : MonoBehaviour
    {
        public const string BgmPrefKey = "GM_BGM_VOLUME", SfxPrefKey = "GM_SFX_VOLUME";
        public const float DefaultBgmVolume = 0.6f, DefaultSfxVolume = 0.8f;
        public const float DuckFactor = 0.3f;
        public const float CheerMixPerMember = 0.04f;
        public const string ResourceRoot = "Audio/";
        public const int MaxHistory = 64;

        private static GMAudioManager instance;
        public static GMAudioManager Instance => instance != null ? instance : instance = FindAnyObjectByType<GMAudioManager>(FindObjectsInactive.Include);

        private AudioSource bgm, song, sfx, mix;
        private readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        private readonly List<(GMAudioCue cue, string key)> history = new List<(GMAudioCue, string)>();
        private readonly List<(GMAudioCue cue, string key)> requests = new List<(GMAudioCue, string)>();
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
        public AudioSource BgmSource => Sources().bgm;
        public AudioSource SongSource => Sources().song;

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
            if (instance == null) instance = this;
            Sources();
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private (AudioSource bgm, AudioSource song, AudioSource sfx, AudioSource mix) Sources()
        {
            if (bgm == null)
            {
                // 씬에 저장된 매니저(Setup이 배치)는 이미 붙은 AudioSource를 순서대로 재사용한다(중복 추가 방지)
                var existing = GetComponents<AudioSource>();
                if (existing.Length > 0) bgm = existing[0];
                if (existing.Length > 1) song = existing[1];
                if (existing.Length > 2) sfx = existing[2];
                if (existing.Length > 3) mix = existing[3];
                if (bgm != null) bgm.loop = true;
                if (mix != null) mix.loop = true;
            }
            if (bgm == null) { bgm = gameObject.AddComponent<AudioSource>(); bgm.loop = true; bgm.playOnAwake = false; }
            if (song == null) { song = gameObject.AddComponent<AudioSource>(); song.loop = false; song.playOnAwake = false; }
            if (sfx == null) { sfx = gameObject.AddComponent<AudioSource>(); sfx.loop = false; sfx.playOnAwake = false; }
            if (mix == null) { mix = gameObject.AddComponent<AudioSource>(); mix.loop = true; mix.playOnAwake = false; }
            return (bgm, song, sfx, mix);
        }

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
            bool ducking = s.song.isPlaying;
            s.bgm.volume = BgmVolume * (ducking ? DuckFactor : 1f);
            s.song.volume = Mathf.Clamp01(SfxVolume * CheerBoost);
            s.sfx.volume = SfxVolume;
            s.mix.volume = Mathf.Clamp01(SfxVolume * 0.35f * CheerBoost);
        }

        private void Update()
        {
            if (bgm != null && song != null) bgm.volume = BgmVolume * (song.isPlaying ? DuckFactor : 1f);
            if (song != null && !song.isPlaying) songCue = null;
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

        public void ClearHistory() { history.Clear(); requests.Clear(); LastCue = null; LastClipKey = null; }

        // ================================================================== 재생

        /// <summary>프런트 오피스 · 메인 홈 구단 테마 BGM(루프). 같은 구단 BGM이 이미 나오고 있으면 그대로 둔다.</summary>
        public string PlayTeamBgm(string teamCode)
        {
            var profile = TeamAudioProfile.For(teamCode);
            if (CurrentBgmTeam == profile.TeamCode && CurrentBgmKey != null && (bgm == null || bgm.isPlaying || !Application.isPlaying)) return CurrentBgmKey;
            string key = profile.ClipFor(GMAudioCue.FrontOfficeBgm, variant++);
            PlayLoop(GMAudioCue.FrontOfficeBgm, key, profile.TeamCode);
            return key;
        }

        /// <summary>실시간 경기 앰비언스(관중 소리 루프) - 구단 BGM 대신 깐다.</summary>
        public void PlayMatchAmbience(string teamCode)
        {
            PlayLoop(GMAudioCue.MatchAmbience, TeamAudioProfile.SynthCrowd, null);
        }

        private void PlayLoop(GMAudioCue cue, string key, string team)
        {
            var s = Sources();
            CurrentBgmKey = key;
            CurrentBgmTeam = team;
            Record(cue, key);
            var clip = LoadClip(key);
            s.bgm.clip = clip;
            ApplyVolumes();
            if (clip != null) s.bgm.Play();
        }

        /// <summary>경기 큐 재생 - 응원가(곡 채널)는 우선순위가 같거나 높을 때만 재생 중인 곡을 끊는다. 하이라이트 · 승리는 환호 효과음을 겹친다. 재생한 클립 키(무시되면 null).</summary>
        public string PlayCue(GMAudioCue cue, string teamCode, int inning = 0)
        {
            var s = Sources();
            var profile = TeamAudioProfile.For(teamCode);
            if (cue == GMAudioCue.FrontOfficeBgm) return PlayTeamBgm(teamCode);
            if (cue == GMAudioCue.MatchAmbience) { PlayMatchAmbience(teamCode); return CurrentBgmKey; }
            if (cue == GMAudioCue.CrowdCheer) { PlayOneShot(cue, TeamAudioProfile.SynthCheer); return TeamAudioProfile.SynthCheer; }
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
            if (clip != null) s.song.Play();
            if (cue == GMAudioCue.HighlightSong || cue == GMAudioCue.ComebackSong || cue == GMAudioCue.WinSong) PlayOneShot(GMAudioCue.CrowdCheer, TeamAudioProfile.SynthCheer);
            return key;
        }

        private void PlayOneShot(GMAudioCue cue, string key)
        {
            var s = Sources();
            var clip = LoadClip(key);
            history.Add((cue, key));
            if (history.Count > MaxHistory) history.RemoveAt(0);
            if (clip != null) s.sfx.PlayOneShot(clip, SfxVolume);
        }

        /// <summary>치어리더 단상 응원 버프 - 엔트리 4~6인이고 버프가 켜져 있으면 응원 믹스 레이어를 깔고 응원가 볼륨을 올린다.</summary>
        public void SetCheerleaderMix(int entryCount, bool buffActive)
        {
            var s = Sources();
            CheerMixActive = buffActive && GMCheerleaderRules.IsValidEntryCount(entryCount);
            CheerMixMembers = CheerMixActive ? entryCount : 0;
            if (CheerMixActive)
            {
                var clip = LoadClip(TeamAudioProfile.SynthChant);
                if (s.mix.clip != clip) s.mix.clip = clip;
                Record(GMAudioCue.CheerleaderMix, TeamAudioProfile.SynthChant);
                if (clip != null && !s.mix.isPlaying) s.mix.Play();
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
            Sources().bgm.Stop();
            CurrentBgmKey = null;
            CurrentBgmTeam = null;
        }
    }

    /// <summary>
    /// [TASK-GM-08] 기본 앰비언스 합성기 - 구단 음원이 없을 때 쓰는 관중 소리(필터드 노이즈). 결정적 난수(같은 키 = 같은 파형).
    ///   synth:crowd 4초 루프(웅성임) · synth:cheer 1.6초 환호(상승 → 감쇠) · synth:clap 0.9초 박수 · synth:chant 2초 루프(박자 구호).
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
                default: return Build(TeamAudioProfile.SynthCrowd, 4.0f, 7, (t, n, d) => n * (0.55f + 0.15f * Mathf.Sin(t * Mathf.PI * 0.5f)) * 0.5f);
            }
        }

        private static float Envelope(float x, float attack) => x < attack ? x / attack : Mathf.Exp(-(x - attack) * 3.2f);
        private static float Clap(float t) { float local = t % 0.3f; return Mathf.Exp(-local * 38f); }
        private static float Beat(float t, float period) { float local = t % period; return Mathf.Exp(-local * 9f); }

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
