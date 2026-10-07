using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>[TASK-GM-09] 글로벌 대회 종류.</summary>
    public enum GMTournamentKind { WBC = 0, AsianGames = 1, Premier12 = 2 }

    /// <summary>[TASK-GM-09] 대회가 끼어드는 시즌 구간 - 스토브리그 종료 직후(3월) · 정규시즌 중(9월, 리그 중단) · 포스트시즌 직후(11월).</summary>
    public enum GMTournamentWindow { PreSeason = 0, MidSeason = 1, PostSeason = 2 }

    /// <summary>[TASK-GM-09] 대회 1건(세이브 v20 GMLeagueSaveData.Tournaments).</summary>
    [Serializable]
    public class GMTournamentRecord
    {
        public GMTournamentKind Kind;
        public GMTournamentWindow Window;
        public int Year, Month;
        public string Name = "";
        public bool Triggered, Completed;
        public int Finish;                 // 1 = 우승(금) · 2 = 준우승(은) · 3 = 3위(동) · 4+ = 탈락 단계
        public string Result = "";
        public string Summary = "";
        public List<string> RosterIds = new List<string>();
        public bool LiveViewRequired = true; // 대회 경기는 실시간 이닝 중계(LiveMatchInningView) 대상
    }

    /// <summary>
    /// [TASK-GM-09] 글로벌 대회 캘린더 · 국가대표 차출(시뮬레이션 전용 뼈대).
    ///   - WBC: 2026년부터 4년 주기(2026 · 2030 …) 3월 - 스토브리그 종료 직후(정규시즌 개막 직전)
    ///   - 아시안게임: 짝수 해 중 4의 배수가 아닌 해(2026 · 2030 …) 9월 - 정규시즌 중단
    ///   - WBSC 프리미어 12: 2027년부터 4년 주기(2027 · 2031 …) 11월 - 포스트시즌 직후
    ///     (지시서 문구 "4로 나누어 떨어지는 해 다음 해" · "홀수 해"는 예시 연도와 어긋나 예시 연도(2026 · 2030 / 2027 · 2031)를 기준으로 삼았다.)
    ///   - 차출: 10구단 1군 중 외국인 · 부상자를 뺀 BaseOVR 상위 투수 13 · 타자 15 = 대한민국 국가대표 28인
    ///   - 결과: 국가대표 평균 OVR 대 대회별 상대국 전력으로 단계별 승부(시드 재현) → 우승(금) · 준우승(은) · 3위(동) · 탈락
    ///   - 효과: 차출 선수 피로(투수 체력 -35%, 부상 위험 8% · 5~12일) / 우승(금) = 만족도 +15 · 팬덤 가치(FameBonus) +10, 은 +6/+5, 동 +4/+3, 탈락 = 만족도 -3
    ///   - 대회 경기는 실시간 이닝 중계 대상(LiveViewRequired) - 국가대표 경기 화면은 다음 단계(현재는 결과 시뮬레이션 + 소식)
    /// </summary>
    public static class GMGlobalTournamentManager
    {
        public const int NationalPitchers = 13, NationalBatters = 15;
        public const double InjuryChance = 0.08;
        public const float PitcherFatigue = 0.35f;

        public static bool IsWbcYear(int year) => year >= 2026 && (year - 2026) % 4 == 0;
        public static bool IsAsianGamesYear(int year) => year % 2 == 0 && year % 4 != 0;
        public static bool IsPremier12Year(int year) => year >= 2027 && (year - 2027) % 4 == 0;

        public static string KindLabel(GMTournamentKind k) => k == GMTournamentKind.WBC ? "WBC (월드 베이스볼 클래식)" : k == GMTournamentKind.AsianGames ? "아시안게임" : "WBSC 프리미어 12";

        /// <summary>그해 대회 일정(월 순).</summary>
        public static List<GMTournamentRecord> ScheduleFor(int year)
        {
            var list = new List<GMTournamentRecord>();
            if (IsWbcYear(year)) list.Add(new GMTournamentRecord { Kind = GMTournamentKind.WBC, Window = GMTournamentWindow.PreSeason, Year = year, Month = 3, Name = $"{year} WBC" });
            if (IsAsianGamesYear(year)) list.Add(new GMTournamentRecord { Kind = GMTournamentKind.AsianGames, Window = GMTournamentWindow.MidSeason, Year = year, Month = 9, Name = $"{year} 아시안게임" });
            if (IsPremier12Year(year)) list.Add(new GMTournamentRecord { Kind = GMTournamentKind.Premier12, Window = GMTournamentWindow.PostSeason, Year = year, Month = 11, Name = $"{year} WBSC 프리미어 12" });
            return list;
        }

        /// <summary>리그에 그해 일정을 (없으면) 넣는다.</summary>
        public static List<GMTournamentRecord> EnsureSchedule(GMLeagueState league)
        {
            if (league == null) return new List<GMTournamentRecord>();
            foreach (var t in ScheduleFor(league.SeasonYear))
                if (!league.Tournaments.Any(x => x.Year == t.Year && x.Kind == t.Kind)) league.Tournaments.Add(t);
            return league.Tournaments.Where(t => t.Year == league.SeasonYear).OrderBy(t => t.Month).ToList();
        }

        public static GMTournamentRecord Pending(GMLeagueState league, GMTournamentWindow window) =>
            EnsureSchedule(league).FirstOrDefault(t => t.Window == window && !t.Triggered);

        /// <summary>9월 진입 판정 - 오늘(경기 일차) 날짜가 9월이고 어제는 9월 전이면 true.</summary>
        public static bool EntersSeptember(int year, int gamesPlayed) =>
            gamesPlayed > 0 && GMLiveSeasonSimulator.DateOf(gamesPlayed, year).Month >= 9 && GMLiveSeasonSimulator.DateOf(gamesPlayed - 1, year).Month < 9;

        /// <summary>국가대표 28인 - 10구단 1군에서 외국인 · 부상자를 빼고 BaseOVR 상위 투수 13 · 타자 15.</summary>
        public static List<Player> SelectNationalTeam(GMLeagueState league)
        {
            var pool = league.Teams.Values.SelectMany(t => t.Roster).Where(p => p?.Template != null && p.InjuryRemainingDays <= 0 && !GMFaCompensation.IsForeign(p)).ToList();
            var pitchers = pool.Where(p => p.IsPitcher).OrderByDescending(p => p.BaseOverall).ThenBy(p => p.InstanceId).Take(NationalPitchers);
            var batters = pool.Where(p => !p.IsPitcher).OrderByDescending(p => p.BaseOverall).ThenBy(p => p.InstanceId).Take(NationalBatters);
            return pitchers.Concat(batters).ToList();
        }

        /// <summary>대회별 단계 상대 전력(OVR 환산) - 일본 · 미국 · 대만 등 대표팀 근사치.</summary>
        private static (string stage, string opponent, int ovr)[] Bracket(GMTournamentKind k)
        {
            switch (k)
            {
                case GMTournamentKind.WBC: return new[] { ("1라운드", "호주", 62), ("1라운드", "대만", 66), ("8강", "도미니카공화국", 74), ("4강", "일본", 77), ("결승", "미국", 78) };
                case GMTournamentKind.AsianGames: return new[] { ("조별리그", "홍콩", 40), ("슈퍼라운드", "대만", 63), ("슈퍼라운드", "일본(사회인)", 60), ("결승", "대만", 65) };
                default: return new[] { ("오프닝라운드", "쿠바", 63), ("오프닝라운드", "도미니카공화국", 66), ("슈퍼라운드", "대만", 66), ("슈퍼라운드", "미국", 70), ("결승", "일본", 74) };
            }
        }

        /// <summary>단계 승리 확률 - 국가대표 평균 OVR 대비 상대 전력 로지스틱(10점 차 ≈ 76%).</summary>
        public static double WinChance(double koreaOvr, int opponentOvr) => 1.0 / (1.0 + Math.Exp(-(koreaOvr - opponentOvr) / 8.7));

        /// <summary>
        /// 대회 실행 - 차출 · 단계별 승부 · 피로/부상 · 성과 보상 · 소식. 이미 치렀으면 그대로 돌려준다.
        /// </summary>
        public static GMTournamentRecord Run(GMLeagueState league, GMTournamentRecord t, int seed)
        {
            if (league == null || t == null || t.Completed) return t;
            t.Triggered = true;
            var squad = SelectNationalTeam(league);
            t.RosterIds = squad.Select(p => p.InstanceId).ToList();
            var rng = new Random(seed ^ (t.Year * 397) ^ ((int)t.Kind * 7919));
            double ovr = squad.Count == 0 ? 50 : squad.Average(p => p.BaseOverall);
            var bracket = Bracket(t.Kind);
            int finalIndex = bracket.Length - 1, lostAt = -1;
            var lines = new List<string>();
            for (int i = 0; i < bracket.Length; i++)
            {
                var (stage, opponent, oppOvr) = bracket[i];
                bool win = rng.NextDouble() < WinChance(ovr, oppOvr);
                lines.Add($"{stage} vs {opponent} {(win ? "승" : "패")}");
                if (!win) { lostAt = i; break; }
            }
            t.Finish = lostAt < 0 ? 1 : lostAt == finalIndex ? 2 : lostAt == finalIndex - 1 ? 3 : 4 + (finalIndex - 1 - lostAt);
            t.Result = t.Finish == 1 ? (t.Kind == GMTournamentKind.AsianGames ? "금메달" : "우승") : t.Finish == 2 ? (t.Kind == GMTournamentKind.AsianGames ? "은메달" : "준우승")
                     : t.Finish == 3 ? (t.Kind == GMTournamentKind.AsianGames ? "동메달" : "4강") : $"{bracket[lostAt].stage} 탈락";
            int morale = t.Finish == 1 ? 15 : t.Finish == 2 ? 6 : t.Finish == 3 ? 4 : -3;
            int fame = t.Finish == 1 ? 10 : t.Finish == 2 ? 5 : t.Finish == 3 ? 3 : 0;
            int injured = 0;
            foreach (var p in squad)
            {
                p.PersonalMorale = Math.Max(0, Math.Min(100, p.PersonalMorale + morale));
                p.FameBonus += fame;
                if (p.IsPitcher && p.MaxStamina > 0) p.CurrentStamina = Math.Max(0, (int)(p.CurrentStamina - p.MaxStamina * PitcherFatigue));
                if (rng.NextDouble() < InjuryChance) { p.InjuryRemainingDays = Math.Max(p.InjuryRemainingDays, 5 + rng.Next(0, 8)); injured++; }
            }
            int mine = squad.Count(p => league.UserTeam != null && league.UserTeam.Roster.Contains(p));
            t.Summary = $"{t.Name} 대한민국 {t.Result} (국가대표 평균 OVR {ovr:0.0}) - {string.Join(" · ", lines)}. 차출 {squad.Count}명(내 구단 {mine}명) · 대회 후 부상 {injured}명.";
            t.Completed = true;
            league.AddNews(new GMNewsItem
            {
                GameIndex = league.GamesPlayed,
                DateLabel = $"{t.Month:00}/{(t.Window == GMTournamentWindow.MidSeason ? 20 : 15)}/{t.Year}",
                Kind = GMNewsKind.Season, IsMajor = true, IsUserTeam = mine > 0,
                Title = $"{t.Name} 대한민국 {t.Result}",
                Body = t.Summary + (t.Finish == 1 && t.Kind == GMTournamentKind.AsianGames ? " 금메달 - 병역 특례 대상." : ""),
            });
            return t;
        }

        /// <summary>구간 훅 - 그 구간에 치를 대회가 있으면 실행해 돌려준다(없으면 null).</summary>
        public static GMTournamentRecord Trigger(GMLeagueState league, GMTournamentWindow window, int seed)
        {
            var t = Pending(league, window);
            return t == null ? null : Run(league, t, seed);
        }
    }

    /// <summary>[TASK-GM-09] 경기 화면 라우팅 플래그 - 포스트시즌 · 글로벌 대회 경기는 100% 실시간 이닝 중계(LiveMatchInningView)로 진행한다.</summary>
    public static class GMMatchRouting
    {
        public static bool ForceLiveForPostseason = true;
        public static bool ForceLiveForTournament = true;

        public static bool RequiresLiveView(bool isPostseason, bool isTournament) =>
            (isPostseason && ForceLiveForPostseason) || (isTournament && ForceLiveForTournament);
    }
}
