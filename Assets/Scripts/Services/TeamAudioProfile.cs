using System;
using System.Collections.Generic;
using KBOManager.Data;
using KBOManager.Engine;

namespace KBOManager.Services
{
    /// <summary>[TASK-GM-08] 구단 오디오 큐 - 프런트 오피스 BGM · 실시간 이닝 경기 응원가 · 아웃송 · 치어리더 단상 믹스.</summary>
    public enum GMAudioCue
    {
        FrontOfficeBgm = 0,  // 메인 홈 / 프런트 오피스 배경음(루프)
        MatchAmbience = 1,   // 경기장 앰비언스(루프)
        ChanceSong = 2,      // 공격 - 득점권 진출 · 득점 찬스 타석(찬스송 · 선수 응원가)
        HighlightSong = 3,   // 공격 - 적시타 · 홈런 하이라이트 응원가(+ 환호 효과음)
        ComebackSong = 4,    // 공격 - 동점 · 역전 적시타
        OutSong = 5,         // 수비 - 삼진(아웃송 · 삼진송)
        InningEndSong = 6,   // 수비 - 무실점 이닝 종료(공식 아웃송)
        WinSong = 7,         // 경기 승리 직후
        CheerleaderMix = 8,  // 치어리더 4~6인 단상 응원 버프 믹스 레이어
        CrowdCheer = 9,      // 환호 효과음
    }

    /// <summary>
    /// [TASK-GM-08] 구단별 오디오 프로필(범용 구조). 삼성 라이온즈(SAM)는 Assets/Resources/Audio/SAM의 구단 공식 BGM · 응원가 · 아웃송 17곡을 쓰고,
    /// 클립이 없는 구단은 기본 앰비언스(GMAudioSynth 합성 관중 소리 "synth:*")로 대체한다. 새 구단 음원은 Profiles에 한 줄씩 추가하면 된다.
    ///   삼성 배치(파일명 메모 반영): 리그 홈 1 = 라인업송 · 리그 홈 2 = 환희 / 찬스(8회 이후) = 엘도라도(사기 진작), 그 밖 = 나의 라이온즈 · Jump up Lions · 지중해 · 아파트 /
    ///   하이라이트 = 환희(분위기 좋을 때), 동점 · 역전 · 승리 직후 = 승리를 위해 · 승리의 라이온즈 / 삼진 = 아웃송 7종(라타타 · 럼블 · 베토벤 · 얼쑤 · 오토바이 · wait up · 짧은 아웃송) /
    ///   무실점 이닝 종료 = 공식 아웃송.
    /// </summary>
    public sealed class TeamAudioProfile
    {
        public const string SynthPrefix = "synth:";
        public const string SynthCrowd = "synth:crowd", SynthCheer = "synth:cheer", SynthClap = "synth:clap", SynthChant = "synth:chant";

        public string TeamCode = "";
        public bool HasClubAudio;
        public string[] HomeBgm = Array.Empty<string>();
        public string[] ChanceSongs = Array.Empty<string>();
        public string[] LateChanceSongs = Array.Empty<string>();
        public string[] HighlightSongs = Array.Empty<string>();
        public string[] ComebackSongs = Array.Empty<string>();
        public string[] OutSongs = Array.Empty<string>();
        public string[] InningEndSongs = Array.Empty<string>();
        public string[] WinSongs = Array.Empty<string>();

        public const int LateInning = 8;

        private static readonly Dictionary<string, TeamAudioProfile> Profiles = new Dictionary<string, TeamAudioProfile>
        {
            [NameAliasTable.SAM] = new TeamAudioProfile
            {
                TeamCode = NameAliasTable.SAM,
                HasClubAudio = true,
                HomeBgm = new[] { "SAM/sam_bgm_home1", "SAM/sam_bgm_home2_hwanhui" },
                ChanceSongs = new[] { "SAM/sam_cheer_my_lions", "SAM/sam_cheer_jump_up", "SAM/sam_cheer_jijunghae", "SAM/sam_cheer_apt" },
                LateChanceSongs = new[] { "SAM/sam_chance_eldorado" },
                HighlightSongs = new[] { "SAM/sam_bgm_home2_hwanhui" },
                ComebackSongs = new[] { "SAM/sam_highlight_for_victory", "SAM/sam_highlight_victory_lions" },
                OutSongs = new[] { "SAM/sam_out_latata", "SAM/sam_out_rumble", "SAM/sam_out_beethoven", "SAM/sam_out_eolssu", "SAM/sam_out_motorcycle", "SAM/sam_out_wait_up", "SAM/sam_out_short" },
                InningEndSongs = new[] { "SAM/sam_out_main" },
                WinSongs = new[] { "SAM/sam_highlight_victory_lions", "SAM/sam_highlight_for_victory" },
            },
        };

        /// <summary>클립 없는 구단의 기본 앰비언스 프로필.</summary>
        public static TeamAudioProfile Generic(string teamCode) => new TeamAudioProfile
        {
            TeamCode = teamCode ?? "",
            HasClubAudio = false,
            HomeBgm = new[] { SynthCrowd },
            ChanceSongs = new[] { SynthChant },
            LateChanceSongs = new[] { SynthChant },
            HighlightSongs = new[] { SynthCheer },
            ComebackSongs = new[] { SynthCheer },
            OutSongs = new[] { SynthClap },
            InningEndSongs = new[] { SynthClap },
            WinSongs = new[] { SynthCheer },
        };

        public static TeamAudioProfile For(string teamCode)
        {
            string code = NameAliasTable.ResolveCanonicalTeamCode(teamCode) ?? teamCode;
            return code != null && Profiles.TryGetValue(code, out var p) ? p : Generic(code);
        }

        public static bool HasClubAudioFor(string teamCode) => For(teamCode).HasClubAudio;

        /// <summary>큐 → 클립 키(Resources/Audio 기준 경로 또는 synth:*). variant로 여러 곡을 돌려 쓴다. inning은 찬스송(8회 이후 엘도라도) 판정용.</summary>
        public string ClipFor(GMAudioCue cue, int variant = 0, int inning = 0)
        {
            string[] list;
            switch (cue)
            {
                case GMAudioCue.FrontOfficeBgm: list = HomeBgm; break;
                case GMAudioCue.MatchAmbience: return SynthCrowd;
                case GMAudioCue.ChanceSong: list = inning >= LateInning && LateChanceSongs.Length > 0 ? LateChanceSongs : ChanceSongs; break;
                case GMAudioCue.HighlightSong: list = HighlightSongs; break;
                case GMAudioCue.ComebackSong: list = ComebackSongs; break;
                case GMAudioCue.OutSong: list = OutSongs; break;
                case GMAudioCue.InningEndSong: list = InningEndSongs; break;
                case GMAudioCue.WinSong: list = WinSongs; break;
                case GMAudioCue.CheerleaderMix: return SynthChant;
                case GMAudioCue.CrowdCheer: return SynthCheer;
                default: list = null; break;
            }
            if (list == null || list.Length == 0) return SynthCrowd;
            return list[((variant % list.Length) + list.Length) % list.Length];
        }

        /// <summary>프로필이 쓰는 클립 키 전체(중복 제거).</summary>
        public IEnumerable<string> AllClipKeys()
        {
            var seen = new HashSet<string>();
            foreach (var arr in new[] { HomeBgm, ChanceSongs, LateChanceSongs, HighlightSongs, ComebackSongs, OutSongs, InningEndSongs, WinSongs })
                foreach (var k in arr) if (seen.Add(k)) yield return k;
        }
    }

    /// <summary>
    /// [TASK-GM-08] 실시간 이닝 경기 사운드 연출 판정(순수 로직). 내 구단 기준:
    ///   공격 - 적시타 · 홈런(득점) = 하이라이트(동점 · 역전이면 승리 응원가) + 환호 / 새로 득점권에 주자가 나가면 찬스송(8회 이후 엘도라도)
    ///   수비 - 무실점으로 이닝을 끝내면 공식 아웃송 / 삼진이면 아웃송 · 삼진송 / 경기 승리 직후 = 승리 응원가.
    /// </summary>
    public static class GMLiveAudioDirector
    {
        public static bool IsHit(AtBatResult r) => r == AtBatResult.Single || r == AtBatResult.Double || r == AtBatResult.Triple || r == AtBatResult.HomeRun;

        /// <summary>
        /// step(이 타석) · userBatting(이 타석에 내 구단이 공격했는지) · rispBefore(타석 전 득점권 주자) · runsInHalf(이 하프이닝 상대 득점, 수비 판정용) ·
        /// userScoreBefore/oppScoreBefore(타석 전 점수) · userWon(경기 종료 시 승리). 연출할 큐가 없으면 null.
        /// </summary>
        public static GMAudioCue? CueFor(AtBatStepResult step, bool userBatting, bool rispBefore, int runsInHalf, int userScoreBefore, int oppScoreBefore, bool userWon)
        {
            if (step == null) return null;
            if (step.GameEnded && userWon) return GMAudioCue.WinSong;
            if (userBatting)
            {
                if (step.RunsScoredThisPlay > 0 && IsHit(step.Result) && !step.IsError)
                {
                    int after = userScoreBefore + step.RunsScoredThisPlay;
                    bool comeback = userScoreBefore <= oppScoreBefore && after >= oppScoreBefore;
                    return comeback ? GMAudioCue.ComebackSong : GMAudioCue.HighlightSong;
                }
                if (!step.HalfInningEnded && step.State != null && step.State.HasRunnerInScoringPosition && !rispBefore) return GMAudioCue.ChanceSong;
                return null;
            }
            if (step.HalfInningEnded && runsInHalf == 0) return GMAudioCue.InningEndSong;
            if (step.Result == AtBatResult.Strikeout) return GMAudioCue.OutSong;
            return null;
        }

        /// <summary>큐 우선순위(높을수록 재생 중인 곡을 끊고 들어간다).</summary>
        public static int Priority(GMAudioCue cue)
        {
            switch (cue)
            {
                case GMAudioCue.WinSong: return 5;
                case GMAudioCue.ComebackSong: return 4;
                case GMAudioCue.HighlightSong: return 3;
                case GMAudioCue.InningEndSong: return 3;
                case GMAudioCue.ChanceSong: return 2;
                case GMAudioCue.OutSong: return 1;
                default: return 0;
            }
        }
    }
}
