using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-19] 프랜차이즈 애착 시스템 1 - 선수 커리어 타임라인.
    ///   - 기록: 신인 드래프트 지명 · FA 영입 · 트레이드 · 첫 1군 콜업 · 데뷔 첫 안타/홈런/승리 · MVP · 골든글러브 · 통합 우승 기여 · 연봉 타결/결렬/갈등 · 약속 이행/파기 · 은퇴 · 영구결번.
    ///     이벤트는 Player.CareerHistory(세이브 v29)에 쌓이고, 게임 시작 전 수상(카드 CareerAwardIds 태그)은 [실제 KBO 기록]으로 타임라인에 함께 보여 준다(중복 제거).
    ///   - 근속 · 통산 WAR: 처음 접근할 때 입단 전 경력을 추정하고(근속 = 프로 경력 × 비율, 통산 WAR = OVR 기준 시즌 WAR × 경력 × 0.6),
    ///     연도 전환(OnSeasonCompleted)마다 현 구단 근속 +1 · 그 시즌 WAR을 더한다. 구단이 바뀌면 근속은 0부터 다시 센다.
    ///   - 데뷔 첫 기록은 드래프트 · 첫 콜업 이력이 있는 선수(게임 안에서 데뷔한 선수)만 판정한다(기존 선수의 "통산 첫 안타"를 새로 만들지 않는다).
    /// </summary>
    public static class GMCareerTimeline
    {
        public const int MaxEvents = 60;
        public const int RookieAge = 21;
        public const float WarPerOvrPoint = 0.25f, WarBaselineOvr = 55f, CareerWarFactor = 0.6f;
        public const double TenureShareSameTeam = 0.6;
        public const int ChampionMinGames = 10, ChampionMinPitcherGames = 5;

        public static string KindLabel(GMCareerEventKind k)
        {
            switch (k)
            {
                case GMCareerEventKind.Draft: return "드래프트";
                case GMCareerEventKind.FaSigning: return "FA 영입";
                case GMCareerEventKind.Trade: return "트레이드";
                case GMCareerEventKind.FirstCallUp: return "첫 1군 콜업";
                case GMCareerEventKind.FirstHit: return "데뷔 첫 안타";
                case GMCareerEventKind.FirstHomeRun: return "데뷔 첫 홈런";
                case GMCareerEventKind.FirstWin: return "데뷔 첫 승리";
                case GMCareerEventKind.Mvp: return "MVP";
                case GMCareerEventKind.GoldenGlove: return "골든글러브";
                case GMCareerEventKind.Championship: return "우승";
                case GMCareerEventKind.Contract: return "계약";
                case GMCareerEventKind.ContractBreakdown: return "협상 결렬";
                case GMCareerEventKind.SalaryConflict: return "연봉 갈등";
                case GMCareerEventKind.BondConflict: return "유대 갈등";
                case GMCareerEventKind.PromiseKept: return "약속 이행";
                case GMCareerEventKind.Retirement: return "은퇴";
                case GMCareerEventKind.RetiredNumber: return "영구결번";
                case GMCareerEventKind.Joined: return "구단 합류";
                default: return "수상";
            }
        }

        /// <summary>타임라인 점 색 그룹 - 0 입단/이적 · 1 성장/데뷔 · 2 영광(수상 · 우승 · 영구결번) · 3 계약 · 4 갈등 · 5 은퇴.</summary>
        public static int ToneOf(GMCareerEventKind k)
        {
            switch (k)
            {
                case GMCareerEventKind.Draft:
                case GMCareerEventKind.FaSigning:
                case GMCareerEventKind.Trade:
                case GMCareerEventKind.Joined: return 0;
                case GMCareerEventKind.FirstCallUp:
                case GMCareerEventKind.FirstHit:
                case GMCareerEventKind.FirstHomeRun:
                case GMCareerEventKind.FirstWin: return 1;
                case GMCareerEventKind.Mvp:
                case GMCareerEventKind.GoldenGlove:
                case GMCareerEventKind.Championship:
                case GMCareerEventKind.Award:
                case GMCareerEventKind.RetiredNumber: return 2;
                case GMCareerEventKind.Contract:
                case GMCareerEventKind.PromiseKept: return 3;
                case GMCareerEventKind.ContractBreakdown:
                case GMCareerEventKind.SalaryConflict:
                case GMCareerEventKind.BondConflict: return 4;
                default: return 5;
            }
        }

        // ================================================================== 기록

        /// <summary>이벤트 1건 적재(같은 연도 · 종류 · 제목은 한 번만). 상한을 넘으면 가장 오래된 계약 · 갈등 기록부터 지운다(영광 · 데뷔 기록 보존).</summary>
        public static GMCareerEvent Record(GMLeagueState league, Player p, GMCareerEventKind kind, string teamCode, string title, string detail = "", bool sim = true, int? year = null)
        {
            if (p == null) return null;
            if (p.CareerHistory == null) p.CareerHistory = new List<GMCareerEvent>();
            int y = year ?? league?.SeasonYear ?? GMFeatureFlags.DEFAULT_START_YEAR;
            title = title ?? KindLabel(kind);
            var dup = p.CareerHistory.FirstOrDefault(e => e != null && e.Year == y && e.Kind == (int)kind && e.Title == title);
            if (dup != null) return dup;
            var ev = new GMCareerEvent
            {
                Year = y, Day = league?.GamesPlayed ?? 0, Kind = (int)kind, TeamCode = teamCode ?? "", Title = title, Detail = detail ?? "", Sim = sim,
            };
            p.CareerHistory.Add(ev);
            while (p.CareerHistory.Count > MaxEvents)
            {
                int idx = p.CareerHistory.FindIndex(e => e == null || ToneOf(e.EventKind) == 3 || ToneOf(e.EventKind) == 4);
                p.CareerHistory.RemoveAt(idx >= 0 ? idx : 0);
            }
            return ev;
        }

        public static bool Has(Player p, GMCareerEventKind kind) => p?.CareerHistory != null && p.CareerHistory.Any(e => e != null && e.Kind == (int)kind);

        public static IEnumerable<GMCareerEvent> EventsOf(Player p, GMCareerEventKind kind) =>
            p?.CareerHistory == null ? Enumerable.Empty<GMCareerEvent>() : p.CareerHistory.Where(e => e != null && e.Kind == (int)kind);

        private static string Name(Player p) => p?.Template?.PlayerName ?? "-";
        private static string TeamName(string code) => string.IsNullOrEmpty(code) ? "FA" : NameAliasTable.DisplayTeamName(code);

        // ---- 거래 · 입단 훅

        public static void OnDrafted(GMLeagueState league, Player p, string teamCode, string round)
        {
            if (p == null) return;
            ResetTenure(p, teamCode);
            Record(league, p, GMCareerEventKind.Draft, teamCode, $"{TeamName(teamCode)} {round} 지명", $"{p.Age}세 · {GMFrontOffice.PositionLabel(p.Position)} · 잠재력 {p.Potential}");
        }

        public static void OnFreeAgentSigned(GMLeagueState league, Player p, string teamCode, string fromTeam)
        {
            if (p == null) return;
            ResetTenure(p, teamCode);
            Record(league, p, GMCareerEventKind.FaSigning, teamCode, $"{TeamName(teamCode)} FA 입단",
                $"{(string.IsNullOrEmpty(fromTeam) ? "자유계약" : TeamName(fromTeam) + "에서 이적")} · {p.ContractYears}년 · 연봉 {GMDiagnosticFormat.Short(p.Salary)}");
        }

        public static void OnTraded(GMLeagueState league, Player p, string fromTeam, string toTeam)
        {
            if (p == null) return;
            ResetTenure(p, toTeam);
            Record(league, p, GMCareerEventKind.Trade, toTeam, $"{TeamName(fromTeam)} → {TeamName(toTeam)} 트레이드", $"OVR {p.BaseOverall} · {p.Age}세");
        }

        /// <summary>퓨처스 → 1군 첫 콜업(이미 1군 기록이 있는 선수는 남기지 않는다).</summary>
        public static void OnCalledUp(GMLeagueState league, Player p, string teamCode)
        {
            if (p == null || Has(p, GMCareerEventKind.FirstCallUp)) return;
            Record(league, p, GMCareerEventKind.FirstCallUp, teamCode, $"{TeamName(teamCode)} 첫 1군 콜업", $"{p.Age}세 · OVR {p.BaseOverall}");
        }

        public static void OnContract(GMLeagueState league, Player p, string teamCode, string outcomeLabel, int salary, int oldSalary, int years, bool conflict)
        {
            if (p == null) return;
            string detail = $"{years}년 · 연봉 {GMDiagnosticFormat.Short(oldSalary)} → {GMDiagnosticFormat.Short(salary)}";
            if (conflict) Record(league, p, GMCareerEventKind.SalaryConflict, teamCode, $"연봉 {outcomeLabel} - 단장과 갈등", detail + $" · 충성도 {p.Loyalty}");
            else Record(league, p, GMCareerEventKind.Contract, teamCode, $"재계약 타결 ({outcomeLabel})", detail);
        }

        public static void OnContractBreakdown(GMLeagueState league, Player p, string teamCode, int demand, int offer)
        {
            if (p == null) return;
            Record(league, p, GMCareerEventKind.ContractBreakdown, teamCode, $"{TeamName(teamCode)}와 협상 최종 결렬",
                $"요구 {GMDiagnosticFormat.Short(demand)} · 제시 {GMDiagnosticFormat.Short(offer)} → FA 시장");
        }

        public static void OnPromise(GMLeagueState league, Player p, string teamCode, bool kept, string label)
        {
            if (p == null) return;
            if (kept) Record(league, p, GMCareerEventKind.PromiseKept, teamCode, "단장 약속 이행 - 유대 강화", label);
            else Record(league, p, GMCareerEventKind.BondConflict, teamCode, "단장 약속 파기 - 유대 갈등", label);
        }

        // ---- 수상

        /// <summary>시상 훅(GMAwardEvaluator.Award) - MVP · 골든글러브 · 신인상 · 타이틀 홀더만 타임라인에 남긴다(월간 · 올스타 · 수비상은 수상 이력 태그로만).</summary>
        public static void RecordAward(GMLeagueState league, Player p, string awardId, string awardName, string teamCode, int year)
        {
            if (p == null || string.IsNullOrEmpty(awardId)) return;
            GMCareerEventKind kind;
            if (awardId == "MVP" || awardId == "KS_MVP") kind = GMCareerEventKind.Mvp;
            else if (awardId.StartsWith("GG_", StringComparison.Ordinal) || awardId.StartsWith("GOLDEN_GLOVE", StringComparison.Ordinal)) kind = GMCareerEventKind.GoldenGlove;
            else if (awardId == "ROOKIE" || awardId.StartsWith("TITLE_", StringComparison.Ordinal)) kind = GMCareerEventKind.Award;
            else return;
            var s = league != null && league.Stats.TryGetValue(p.InstanceId, out var st) ? st : null;
            string line = s == null ? "" : s.IsPitcher || p.IsPitcher ? $"{s.W}승 {s.L}패 · WAR {s.WAR:0.0}" : $"{s.HR}홈런 {s.RBI}타점 · WAR {s.WAR:0.0}";
            Record(league, p, kind, teamCode, string.IsNullOrEmpty(awardName) ? KindLabel(kind) : awardName, line, true, year);
        }

        // ---- 데뷔 첫 기록

        /// <summary>게임 안에서 데뷔한 선수(드래프트 · 첫 콜업 이력)이고 아직 남길 데뷔 기록이 있는지.</summary>
        public static bool TracksDebut(Player p)
        {
            if (p?.CareerHistory == null || p.CareerHistory.Count == 0) return false;
            if (!Has(p, GMCareerEventKind.Draft) && !Has(p, GMCareerEventKind.FirstCallUp)) return false;
            return p.IsPitcher ? !Has(p, GMCareerEventKind.FirstWin) : !Has(p, GMCareerEventKind.FirstHit) || !Has(p, GMCareerEventKind.FirstHomeRun);
        }

        /// <summary>정규시즌 경기 반영 훅 - 오늘 안타 · 홈런 · 승리가 데뷔 첫 기록이면 남긴다.</summary>
        public static void CheckDebut(GMLeagueState league, Player p, string teamCode, string dateLabel, string opponentCode, int hits, int homeRuns, bool win)
        {
            if (!TracksDebut(p)) return;
            string vs = string.IsNullOrEmpty(opponentCode) ? dateLabel : $"{dateLabel} vs {TeamName(opponentCode)}";
            if (!p.IsPitcher)
            {
                if (hits > 0 && !Has(p, GMCareerEventKind.FirstHit)) Record(league, p, GMCareerEventKind.FirstHit, teamCode, "데뷔 첫 안타", vs);
                if (homeRuns > 0 && !Has(p, GMCareerEventKind.FirstHomeRun)) Record(league, p, GMCareerEventKind.FirstHomeRun, teamCode, "데뷔 첫 홈런", vs);
            }
            else if (win && !Has(p, GMCareerEventKind.FirstWin)) Record(league, p, GMCareerEventKind.FirstWin, teamCode, "데뷔 첫 승리", vs);
        }

        // ================================================================== 근속 · 통산 WAR

        public static int ProYears(Player p) => p == null ? 0 : Math.Max(0, p.Age - RookieAge);

        /// <summary>입단 전 통산 WAR 추정 = OVR 기준 시즌 WAR(55 초과분 × 0.25) × 프로 경력 × 0.6(전성기 이전 · 이후 할인).</summary>
        public static float EstimatePriorWar(Player p)
        {
            if (p?.Template == null) return 0f;
            float perSeason = Math.Max(0f, (p.BaseOverall - WarBaselineOvr) * WarPerOvrPoint);
            return (float)Math.Round(perSeason * ProYears(p) * CareerWarFactor, 1);
        }

        /// <summary>입단 전 현 구단 근속 추정 - 영구결번 · 프랜차이즈 카드 = 프로 경력 전부, 같은 계보 카드 = 60%, 다른 구단 카드 = 1년 이하.</summary>
        public static int EstimateTenure(Player p, string teamCode)
        {
            if (p?.Template == null || string.IsNullOrEmpty(teamCode)) return 0;
            int years = ProYears(p);
            var origin = GMRosterTiers.OriginalOf(p.Template);
            if (origin.Grade == Grade.RETIRED_NUMBER || origin.Grade == Grade.FRANCHISE) return years;
            if (NameAliasTable.ToCode(origin.Team) == teamCode) return (int)Math.Round(years * TenureShareSameTeam);
            return Math.Min(years, 1);
        }

        /// <summary>근속 · 통산 WAR 추정치를 처음 한 번만 채운다(teamCode = 현재 소속, null = FA).</summary>
        public static void EnsureCareer(Player p, string teamCode)
        {
            if (p?.Template == null) return;
            if (p.CareerWarEstimate < 0f) p.CareerWarEstimate = EstimatePriorWar(p);
            if (string.IsNullOrEmpty(p.TenureTeam) && !string.IsNullOrEmpty(teamCode))
            {
                p.TenureTeam = teamCode;
                p.TenureYears = EstimateTenure(p, teamCode);
            }
        }

        private static void ResetTenure(Player p, string teamCode)
        {
            EnsureCareer(p, null);
            if (string.IsNullOrEmpty(teamCode) || p.TenureTeam == teamCode) return;
            p.TenureTeam = teamCode;
            p.TenureYears = 0;
        }

        public static string TeamCodeOf(GMLeagueState league, Player p)
        {
            if (league == null || p == null) return null;
            foreach (var t in league.Teams.Values) if (t.Roster.Contains(p) || t.Futures.Contains(p)) return t.TeamCode;
            return null;
        }

        /// <summary>
        /// 연도 전환(AdvanceToNextSeasonYear, 기록 초기화 전) - 전 구단 보류선수 근속 +1 · 시즌 WAR 누적, 우승 구단 기여 선수(10경기 · 투수 5경기 이상) 우승 이벤트.
        /// ranks = 정규시즌 순위(1위 = 우승 구단이면 통합 우승).
        /// </summary>
        public static void OnSeasonCompleted(GMLeagueState league, int year, IReadOnlyList<string> ranks, string championCode)
        {
            if (league == null) return;
            bool integrated = ranks != null && ranks.Count > 0 && ranks[0] == championCode;
            foreach (var team in league.Teams.Values)
            {
                foreach (var p in team.ReservePlayers.ToList())
                {
                    if (p?.Template == null) continue;
                    EnsureCareer(p, team.TeamCode);
                    if (p.TenureTeam != team.TeamCode) { p.TenureTeam = team.TeamCode; p.TenureYears = 0; }
                    p.TenureYears++;
                    league.Stats.TryGetValue(p.InstanceId, out var s);
                    if (s != null) p.CareerWarSim = (float)Math.Round(p.CareerWarSim + s.WAR, 1);
                    if (team.TeamCode != championCode || s == null || !team.Roster.Contains(p)) continue;
                    bool contributed = p.IsPitcher ? s.PG >= ChampionMinPitcherGames : s.G >= ChampionMinGames;
                    if (!contributed) continue;
                    Record(league, p, GMCareerEventKind.Championship, team.TeamCode, integrated ? $"{year} 통합 우승 기여" : $"{year} 한국시리즈 우승 기여",
                        p.IsPitcher ? $"{s.PG}경기 {s.W}승 {s.SV}세이브 · WAR {s.WAR:0.0}" : $"{s.G}경기 타율 {GMTeamRecord.PctLabel(s.AVG)} {s.HR}홈런 · WAR {s.WAR:0.0}", true, year);
                }
            }
            foreach (var p in league.FreeAgents)
            {
                if (p?.Template == null) continue;
                EnsureCareer(p, null);
                if (league.Stats.TryGetValue(p.InstanceId, out var s)) p.CareerWarSim = (float)Math.Round(p.CareerWarSim + s.WAR, 1);
            }
        }

        // ================================================================== 타임라인 화면

        /// <summary>
        /// 타임라인 표시 목록(오래된 순) - 저장 이벤트 + 카드 수상 태그(실제 KBO 기록, 같은 연도 · 종류 저장 이벤트가 있으면 생략) + 구단 합류(추정).
        /// </summary>
        public static List<GMCareerEvent> Timeline(GMLeagueState league, Player p)
        {
            var list = new List<GMCareerEvent>();
            if (p?.Template == null) return list;
            string team = TeamCodeOf(league, p);
            EnsureCareer(p, team);
            var stored = (p.CareerHistory ?? new List<GMCareerEvent>()).Where(e => e != null).ToList();
            list.AddRange(stored);
            foreach (var tag in p.CareerAwardIds ?? new List<string>())
            {
                var ev = FromTag(tag);
                if (ev == null) continue;
                if (stored.Any(e => e.Year == ev.Year && e.Kind == ev.Kind) || list.Any(e => !e.Sim && e.Year == ev.Year && e.Kind == ev.Kind && e.Title == ev.Title)) continue;
                list.Add(ev);
            }
            bool hasEntry = stored.Any(e => e.Kind == (int)GMCareerEventKind.Draft || e.Kind == (int)GMCareerEventKind.FaSigning || e.Kind == (int)GMCareerEventKind.Trade);
            if (!hasEntry && !string.IsNullOrEmpty(p.TenureTeam) && p.TenureYears > 0)
            {
                int now = league?.SeasonYear ?? GMFeatureFlags.DEFAULT_START_YEAR;
                list.Add(new GMCareerEvent
                {
                    Year = now - p.TenureYears, Kind = (int)GMCareerEventKind.Joined, TeamCode = p.TenureTeam, Sim = false,
                    Title = $"{TeamName(p.TenureTeam)} 합류 (추정)", Detail = $"게임 시작 전 경력 추정 · 근속 {p.TenureYears}년째",
                });
            }
            return list.OrderBy(e => e.Year).ThenBy(e => e.Sim ? 1 : 0).ThenBy(e => e.Day).ToList();
        }

        /// <summary>카드 수상 태그 → 실제 기록 이벤트(MVP · 골든글러브 · 한국시리즈 우승/MVP · 신인상 · 타이틀 홀더). 그 밖의 태그는 null.</summary>
        public static GMCareerEvent FromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            int year = 0;
            foreach (var token in tag.Split('_'))
                if (token.Length == 4 && int.TryParse(token, out int y) && y >= 1982 && y <= 2200) { year = y; break; }
            if (year == 0) return null;
            GMCareerEventKind kind;
            string title;
            if (tag.StartsWith("KS_MVP", StringComparison.Ordinal)) { kind = GMCareerEventKind.Mvp; title = "한국시리즈 MVP"; }
            else if (tag.StartsWith("MVP", StringComparison.Ordinal)) { kind = GMCareerEventKind.Mvp; title = "KBO MVP"; }
            else if (tag.StartsWith("GOLDEN_GLOVE", StringComparison.Ordinal)) { kind = GMCareerEventKind.GoldenGlove; title = "골든글러브"; }
            else if (tag.StartsWith("KS_CHAMPION", StringComparison.Ordinal)) { kind = GMCareerEventKind.Championship; title = "한국시리즈 우승"; }
            else if (tag.StartsWith("ROOKIE", StringComparison.Ordinal)) { kind = GMCareerEventKind.Award; title = "KBO 신인상"; }
            else if (tag.StartsWith("TITLE_HOLDER", StringComparison.Ordinal)) { kind = GMCareerEventKind.Award; title = "타이틀 홀더"; }
            else return null;
            return new GMCareerEvent { Year = year, Kind = (int)kind, Title = title, Detail = "", Sim = false };
        }

        /// <summary>요약 한 줄 - 근속 · 통산 WAR(추정 + 시뮬레이션) · 영구결번 자격.</summary>
        public static string Summary(GMLeagueState league, Player p)
        {
            if (p?.Template == null) return "";
            EnsureCareer(p, TeamCodeOf(league, p));
            bool eligible = GMRetirement.QualifiesForRetiredNumber(p);
            string tenure = string.IsNullOrEmpty(p.TenureTeam) ? "소속 없음" : $"{TeamName(p.TenureTeam)} 근속 {p.TenureYears}년";
            return $"{tenure} · 통산 WAR {p.CareerWar:0.0} (입단 전 추정 {Math.Max(0f, p.CareerWarEstimate):0.0} + 시뮬레이션 {p.CareerWarSim:0.0})" +
                   (eligible ? $" · 영구결번 자격(근속 {GMRetirement.RetiredNumberTenure}년 · WAR {GMRetirement.RetiredNumberWar:0} 이상)" : "");
        }
    }
}
