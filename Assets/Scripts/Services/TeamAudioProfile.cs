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
        BigInningSong = 10,  // [TASK-GM-12] 공격 - 빅이닝(한 하프이닝 4득점 이상, 하프이닝당 1회) - 삼성 = 아파트(일반 찬스송 풀에서 제외)
        LateCloseSong = 11,  // [TASK-GM-12] 공격 - 7~9회 2점 차 이내 접전(경기당 1회) - 삼성 = Jump up Lions
        OutCheer = 12,       // [TASK-GM-12] 아웃 효과음(짧은 박수) - 득점 환호는 CrowdCheer
        EventBgm = 13,       // [TASK-GM-12] 이벤트 전용곡(BGM 채널 하이재킹) 기록용
        UiSfx = 14,          // [TASK-GM-14] 화면 효과음(연봉 협상 타결 · 결렬)
    }

    /// <summary>[TASK-GM-12] BGM 화면 그룹 - 화면마다 로테이션 풀이 다르고, 같은 GroupId 화면끼리는 곡을 끊지 않는다.</summary>
    public enum GMAudioScreen
    {
        Hub = 0,         // 메인 허브 · 스토브리그 허브(체류 시간 김)
        Market = 1,      // 시장 정보실(FA · 트레이드 · 연봉 · 보호 명단 · 계약 협상실)
        Squad = 2,       // 선수단 · 육성 회의실(로스터 · 케미스트리 · 드래프트)
        OwnerReport = 3, // 구단주 보고실(구단주 건의)
        Match = 4,       // 한 경기 진행(전력 분석 → 실시간 이닝 → 결과) - BGM 대신 관중 앰비언스
    }

    /// <summary>[TASK-GM-12] 화면 BGM을 끊고 들어가는 이벤트 전용곡(체인이 끝나면 현재 화면 풀에서 새 곡으로 재개).</summary>
    public enum GMAudioEvent
    {
        PositiveResult = 0, // 환희 - 일반 FA 계약 · 재계약 · 트레이드 성사
        MajorResult = 1,    // 엘도라도 - 대형(S급) FA · 프랜차이즈 잔류 재계약 · 대형 발표
        OwnerApproval = 2,  // 승리를 위해 → 엘도라도 - 구단주 목표 승인 · 우승
        ProspectBoom = 3,   // Jump up Lions - 유망주 콜업 · 성장 폭발
        Tension = 4,        // 공통 긴장 BGM - 계약 결렬 · 삭감 협상 · 예산 경고 · 라커룸 파벌 갈등
        MatchWin = 5,       // 승리의 라이온즈 - 한 경기 승리 · 포스트시즌 시리즈 승리
        BigWin = 6,         // 승리의 라이온즈 → 엘도라도 - 5점 차 이상 대승
        Defeat = 7,         // 공통 결과(패배) BGM
        Lineup = 8,         // 라인업송 - 경기 전 라인업 출력 시 1회
    }

    /// <summary>[TASK-GM-12] 가중치 BGM 풀(같은 GroupId 화면끼리는 곡을 이어 간다).</summary>
    public sealed class GMAudioPlaylist
    {
        public string Id = "";
        public string GroupId = "";
        public (string key, int weight)[] Tracks = Array.Empty<(string, int)>();

        public bool Contains(string key) { foreach (var t in Tracks) if (t.key == key) return true; return false; }
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
        /// <summary>[TASK-GM-12] 공통 긴장 · 패배 BGM - 전용 음원이 들어오기 전까지 합성 저음 패드를 쓴다(전 구단 공통).</summary>
        public const string SynthTension = "synth:tension", SynthDefeat = "synth:defeat";
        /// <summary>[TASK-GM-14] 연봉 협상 결과 짧은 효과음 - 타결(2음 차임) · 결렬(하강 저음).</summary>
        public const string SynthDeal = "synth:deal", SynthFail = "synth:fail";

        // [TASK-GM-12] 삼성 곡 키(Resources/Audio 기준)
        public const string SamMyLions = "SAM/sam_cheer_my_lions", SamJijunghae = "SAM/sam_cheer_jijunghae", SamHwanhui = "SAM/sam_bgm_home2_hwanhui",
            SamForVictory = "SAM/sam_highlight_for_victory", SamJumpUp = "SAM/sam_cheer_jump_up", SamEldorado = "SAM/sam_chance_eldorado",
            SamVictoryLions = "SAM/sam_highlight_victory_lions", SamApt = "SAM/sam_cheer_apt", SamLineup = "SAM/sam_bgm_home1";

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
        public string[] BigInningSongs = Array.Empty<string>();   // [TASK-GM-12]
        public string[] LateCloseSongs = Array.Empty<string>();   // [TASK-GM-12]
        public string[] LineupSongs = Array.Empty<string>();      // [TASK-GM-12] 경기 전 라인업송
        /// <summary>[TASK-GM-12] 화면 그룹별 BGM 로테이션 풀.</summary>
        public Dictionary<GMAudioScreen, GMAudioPlaylist> Playlists = new Dictionary<GMAudioScreen, GMAudioPlaylist>();
        /// <summary>[TASK-GM-12] 이벤트 전용곡 체인(앞에서부터 차례로 재생한 뒤 화면 풀로 복귀).</summary>
        public Dictionary<GMAudioEvent, string[]> EventTracks = new Dictionary<GMAudioEvent, string[]>();

        public const int LateInning = 8;

        private static readonly Dictionary<string, TeamAudioProfile> Profiles = new Dictionary<string, TeamAudioProfile>
        {
            [NameAliasTable.SAM] = new TeamAudioProfile
            {
                TeamCode = NameAliasTable.SAM,
                HasClubAudio = true,
                // [TASK-GM-12] 프런트 오피스 BGM = 허브 로테이션 풀 곡 목록(실제 선택은 Playlists 가중치) · 라인업송은 경기 전 전용곡으로 이동
                HomeBgm = new[] { SamHwanhui, SamMyLions, SamJijunghae, SamForVictory, SamJumpUp },
                LineupSongs = new[] { SamLineup },
                // [TASK-GM-12] 아파트는 일반 찬스송 풀에서 빼고 빅이닝 전용
                ChanceSongs = new[] { SamMyLions, SamJumpUp, SamJijunghae },
                LateChanceSongs = new[] { "SAM/sam_chance_eldorado" },
                HighlightSongs = new[] { "SAM/sam_bgm_home2_hwanhui" },
                ComebackSongs = new[] { "SAM/sam_highlight_for_victory", "SAM/sam_highlight_victory_lions" },
                OutSongs = new[] { "SAM/sam_out_latata", "SAM/sam_out_rumble", "SAM/sam_out_beethoven", "SAM/sam_out_eolssu", "SAM/sam_out_motorcycle", "SAM/sam_out_wait_up", "SAM/sam_out_short" },
                InningEndSongs = new[] { "SAM/sam_out_main" },
                WinSongs = new[] { "SAM/sam_highlight_victory_lions", "SAM/sam_highlight_for_victory" },
                BigInningSongs = new[] { SamApt },
                LateCloseSongs = new[] { SamJumpUp },
                Playlists = new Dictionary<GMAudioScreen, GMAudioPlaylist>
                {
                    [GMAudioScreen.Hub] = List("SAM.hub", "SAM.office", (SamMyLions, 30), (SamJijunghae, 20), (SamHwanhui, 20), (SamForVictory, 15), (SamJumpUp, 15)),
                    [GMAudioScreen.Market] = List("SAM.market", "SAM.office", (SamJijunghae, 30), (SamForVictory, 25), (SamMyLions, 25), (SamHwanhui, 20)),
                    [GMAudioScreen.Squad] = List("SAM.squad", "SAM.office", (SamMyLions, 40), (SamForVictory, 30), (SamHwanhui, 30)),
                    [GMAudioScreen.OwnerReport] = List("SAM.owner", "SAM.owner", (SamForVictory, 40), (SamMyLions, 35), (SamEldorado, 25)),
                    [GMAudioScreen.Match] = List("SAM.match", "SAM.match", (SynthCrowd, 1)),
                },
                EventTracks = new Dictionary<GMAudioEvent, string[]>
                {
                    [GMAudioEvent.PositiveResult] = new[] { SamHwanhui },
                    [GMAudioEvent.MajorResult] = new[] { SamEldorado },
                    [GMAudioEvent.OwnerApproval] = new[] { SamForVictory, SamEldorado },
                    [GMAudioEvent.ProspectBoom] = new[] { SamJumpUp },
                    [GMAudioEvent.Tension] = new[] { SynthTension },
                    [GMAudioEvent.MatchWin] = new[] { SamVictoryLions },
                    [GMAudioEvent.BigWin] = new[] { SamVictoryLions, SamEldorado },
                    [GMAudioEvent.Defeat] = new[] { SynthDefeat },
                    [GMAudioEvent.Lineup] = new[] { SamLineup },
                },
            },
        };

        private static GMAudioPlaylist List(string id, string group, params (string key, int weight)[] tracks) => new GMAudioPlaylist { Id = id, GroupId = group, Tracks = tracks };

        /// <summary>클립 없는 구단의 기본 앰비언스 프로필.</summary>
        public static TeamAudioProfile Generic(string teamCode)
        {
            string code = teamCode ?? "";
            var office = List(code + ".office", code + ".office", (SynthCrowd, 1));
            return new TeamAudioProfile
            {
            TeamCode = code,
            HasClubAudio = false,
            HomeBgm = new[] { SynthCrowd },
            ChanceSongs = new[] { SynthChant },
            LateChanceSongs = new[] { SynthChant },
            HighlightSongs = new[] { SynthCheer },
            ComebackSongs = new[] { SynthCheer },
            OutSongs = new[] { SynthClap },
            InningEndSongs = new[] { SynthClap },
            WinSongs = new[] { SynthCheer },
            BigInningSongs = new[] { SynthChant },
            LateCloseSongs = new[] { SynthChant },
            Playlists = new Dictionary<GMAudioScreen, GMAudioPlaylist>
            {
                [GMAudioScreen.Hub] = office, [GMAudioScreen.Market] = office, [GMAudioScreen.Squad] = office, [GMAudioScreen.OwnerReport] = office,
                [GMAudioScreen.Match] = List(code + ".match", code + ".match", (SynthCrowd, 1)),
            },
            EventTracks = new Dictionary<GMAudioEvent, string[]>
            {
                [GMAudioEvent.PositiveResult] = new[] { SynthCheer }, [GMAudioEvent.MajorResult] = new[] { SynthCheer },
                [GMAudioEvent.OwnerApproval] = new[] { SynthCheer }, [GMAudioEvent.ProspectBoom] = new[] { SynthCheer },
                [GMAudioEvent.Tension] = new[] { SynthTension }, [GMAudioEvent.MatchWin] = new[] { SynthCheer },
                [GMAudioEvent.BigWin] = new[] { SynthCheer }, [GMAudioEvent.Defeat] = new[] { SynthDefeat }, [GMAudioEvent.Lineup] = new[] { SynthClap },
            },
            };
        }

        /// <summary>[TASK-GM-12] 화면 그룹 BGM 풀(정의가 없으면 허브 풀).</summary>
        public GMAudioPlaylist PlaylistFor(GMAudioScreen screen) =>
            Playlists.TryGetValue(screen, out var p) ? p : Playlists.TryGetValue(GMAudioScreen.Hub, out var hub) ? hub : List(TeamCode + ".office", TeamCode + ".office", (SynthCrowd, 1));

        /// <summary>[TASK-GM-12] 이벤트 전용곡 체인(정의가 없으면 환호 효과음 1곡).</summary>
        public string[] EventChain(GMAudioEvent ev) => EventTracks.TryGetValue(ev, out var c) && c.Length > 0 ? c : new[] { SynthCheer };

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
                case GMAudioCue.OutCheer: return SynthClap;
                case GMAudioCue.BigInningSong: list = BigInningSongs; break;
                case GMAudioCue.LateCloseSong: list = LateCloseSongs; break;
                default: list = null; break;
            }
            if (list == null || list.Length == 0) return SynthCrowd;
            return list[((variant % list.Length) + list.Length) % list.Length];
        }

        /// <summary>프로필이 쓰는 클립 키 전체(중복 제거).</summary>
        public IEnumerable<string> AllClipKeys()
        {
            var seen = new HashSet<string>();
            foreach (var arr in new[] { HomeBgm, ChanceSongs, LateChanceSongs, HighlightSongs, ComebackSongs, OutSongs, InningEndSongs, WinSongs, BigInningSongs, LateCloseSongs, LineupSongs })
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

        public const int BigInningRuns = 4, LateCloseInning = 7, LateCloseMargin = 2, BigWinMargin = 5;

        public static bool IsOut(AtBatResult r) => r == AtBatResult.Strikeout || r == AtBatResult.Groundout || r == AtBatResult.Flyout;

        /// <summary>
        /// [TASK-GM-12] 기본 큐(CueFor) 다음에 붙는 연출 - 우선순위 순:
        ///   빅이닝(내 공격 하프이닝 득점이 이 타석으로 4점 이상이 됨) = 아파트 / 7~9회 2점 차 이내 접전 첫 공격 타석 = Jump up Lions(경기당 1회, lateCloseUsed) /
        ///   기본 큐가 없을 때 - 내 득점 = 관중 환호 효과음, 수비 아웃 = 짧은 박수 효과음. halfRunsAfter = 이 타석 후 하프이닝 득점. 없으면 null.
        /// </summary>
        public static GMAudioCue? ExtraCueFor(AtBatStepResult step, bool userBatting, GMAudioCue? baseCue, int halfRunsAfter, int inning, int userAfter, int oppAfter, bool lateCloseUsed)
        {
            if (step == null || step.GameEnded) return null;
            if (userBatting && step.RunsScoredThisPlay > 0 && halfRunsAfter >= BigInningRuns && halfRunsAfter - step.RunsScoredThisPlay < BigInningRuns) return GMAudioCue.BigInningSong;
            if (userBatting && !lateCloseUsed && inning >= LateCloseInning && Math.Abs(userAfter - oppAfter) <= LateCloseMargin && !step.HalfInningEnded) return GMAudioCue.LateCloseSong;
            if (baseCue.HasValue) return null;
            if (userBatting && step.RunsScoredThisPlay > 0) return GMAudioCue.CrowdCheer;
            if (!userBatting && IsOut(step.Result)) return GMAudioCue.OutCheer;
            return null;
        }

        /// <summary>
        /// [TASK-GM-18] 경기 중 효과음(SFX) 판정 - BGM은 건드리지 않는다. 내 공격 득점 · 안타 = 관중 환호, 내 수비 아웃 = 짧은 박수, 그 밖 · 경기 종료 타석 = 없음.
        /// </summary>
        public static GMAudioCue? SfxFor(AtBatStepResult step, bool userBatting)
        {
            if (step == null || step.GameEnded) return null;
            if (userBatting) return step.RunsScoredThisPlay > 0 || (IsHit(step.Result) && !step.IsError) ? GMAudioCue.CrowdCheer : (GMAudioCue?)null;
            return IsOut(step.Result) ? GMAudioCue.OutCheer : (GMAudioCue?)null;
        }

        /// <summary>[TASK-GM-12] 경기 종료 BGM 이벤트 - 승리 = 승리의 라이온즈(5점 차 이상 대승 = → 엘도라도 연계), 패배 = 공통 패배 BGM, 무승부 = null.</summary>
        public static GMAudioEvent? ResultEventFor(int userRuns, int oppRuns)
        {
            if (userRuns > oppRuns) return userRuns - oppRuns >= BigWinMargin ? GMAudioEvent.BigWin : GMAudioEvent.MatchWin;
            if (userRuns < oppRuns) return GMAudioEvent.Defeat;
            return null;
        }

        /// <summary>큐 우선순위(높을수록 재생 중인 곡을 끊고 들어간다).</summary>
        public static int Priority(GMAudioCue cue)
        {
            switch (cue)
            {
                case GMAudioCue.WinSong: return 5;
                case GMAudioCue.ComebackSong: return 4;
                case GMAudioCue.BigInningSong: return 4;
                case GMAudioCue.LateCloseSong: return 2;
                case GMAudioCue.HighlightSong: return 3;
                case GMAudioCue.InningEndSong: return 3;
                case GMAudioCue.ChanceSong: return 2;
                case GMAudioCue.OutSong: return 1;
                default: return 0;
            }
        }
    }

    /// <summary>
    /// [TASK-GM-12] BGM 로테이션 선택기(순수 로직) - 가중치 무작위 + 반복 방지.
    ///   ① 최근 재생 2곡(recentTracks)은 다음 후보에서 무조건 제외 ② 같은 화면 풀에서 90초 안에 틀었던 곡 제외(쿨다운)
    ///   ③ 후보가 비면 쿨다운만 풀고, 그래도 비면(풀 2곡 이하) 직전 곡만 빼고 고른다 - 1곡 풀은 그 곡을 반복한다.
    /// 이벤트 전용곡도 MarkPlayed로 최근 목록에 넣어, 이벤트가 끝난 뒤 같은 곡으로 재개하지 않는다.
    /// </summary>
    public sealed class GMBgmRotation
    {
        public const int RecentExclude = 2;
        public const float SameScreenCooldown = 90f;

        private readonly Queue<string> recentTracks = new Queue<string>();
        private readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>(); // "풀 Id|곡" → 재생 시각(초)
        private readonly System.Random rng;

        public GMBgmRotation(int seed = 0) { rng = seed != 0 ? new System.Random(seed) : new System.Random(); }

        public IEnumerable<string> RecentTracks => recentTracks;
        public string LastTrack { get; private set; }

        public void Clear() { recentTracks.Clear(); lastPlayed.Clear(); LastTrack = null; }

        /// <summary>곡 재생 기록 - 최근 2곡 큐 · 화면 쿨다운 갱신.</summary>
        public void MarkPlayed(string key, string playlistId, float now)
        {
            if (string.IsNullOrEmpty(key)) return;
            LastTrack = key;
            recentTracks.Enqueue(key);
            while (recentTracks.Count > RecentExclude) recentTracks.Dequeue();
            if (!string.IsNullOrEmpty(playlistId)) lastPlayed[playlistId + "|" + key] = now;
        }

        public bool IsRecent(string key) => recentTracks.Contains(key);

        public bool OnCooldown(string key, string playlistId, float now) =>
            !string.IsNullOrEmpty(playlistId) && lastPlayed.TryGetValue(playlistId + "|" + key, out float t) && now - t < SameScreenCooldown;

        /// <summary>풀에서 다음 곡을 고른다(빈 풀이면 null).</summary>
        public string Pick(GMAudioPlaylist playlist, float now)
        {
            if (playlist == null || playlist.Tracks.Length == 0) return null;
            if (playlist.Tracks.Length == 1) return playlist.Tracks[0].key;
            var candidates = new List<(string key, int weight)>();
            foreach (var t in playlist.Tracks) if (!IsRecent(t.key) && !OnCooldown(t.key, playlist.Id, now)) candidates.Add(t);
            if (candidates.Count == 0) foreach (var t in playlist.Tracks) if (!IsRecent(t.key)) candidates.Add(t);
            if (candidates.Count == 0) foreach (var t in playlist.Tracks) if (t.key != LastTrack) candidates.Add(t);
            if (candidates.Count == 0) return playlist.Tracks[0].key;
            int total = 0;
            foreach (var c in candidates) total += Math.Max(1, c.weight);
            int roll = rng.Next(total);
            foreach (var c in candidates)
            {
                roll -= Math.Max(1, c.weight);
                if (roll < 0) return c.key;
            }
            return candidates[candidates.Count - 1].key;
        }
    }
}
