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
    /// [TASK-GM-06] OOTP 27식 프런트 오피스 규칙(순수 로직 - UI는 GMOotpFrontOfficeUIController).
    ///   - 구단주 정보(모기업 · 인내심 · 재정 성향 · 개입도 · 최우선 가치 · 기분 · 기대치) · 신임도(0~100)
    ///   - 구단주 목표 5~6개(승률/순위 · 취약 포지션 · 골든글러브/타이틀 · 홈 관중 · 페이롤 · 장기 우승) + [건의(DISCUSS)]
    ///   - 시즌 이력(승률 · 관중), 가성비 선수(WAR당 연봉) TOP 10, 예산 · 연장계약 가용 자금
    ///   - 4단계 난이도 · 하우스 룰(연간 FA/트레이드 한도), 스토리 안건 10종 · 4종 멀티 엔딩
    /// 금액 단위는 모두 만 원이다.
    /// </summary>
    public static class GMFrontOffice
    {
        public const int HistorySeasons = 9;          // 시작 시점 과거 이력(2017~2025)
        public const int HomeGames = 72;
        public const int MaxTrust = 100;
        public const int DiscussTrustCost = 8;        // 목표 완화
        public const int ExtraBudgetTrustCost = 12;   // 추가 예산
        public const int LiftTradeTrustCost = 10;     // 트레이드 한도 해제
        public const int DiscussMinTrust = 30;        // 이 미만이면 구단주가 건의를 거절한다
        public const int ExtraBudgetPercent = 5;      // 샐러리캡의 5%

        // ================================================================== 난이도 · 하우스 룰

        public static readonly GMDifficulty[] Difficulties = { GMDifficulty.Minors, GMDifficulty.Majors, GMDifficulty.AllStar, GMDifficulty.HallOfFame };

        public static string DifficultyLabel(GMDifficulty d)
        {
            switch (d)
            {
                case GMDifficulty.Minors: return "퓨처스(이지)";
                case GMDifficulty.AllStar: return "올스타(하드)";
                case GMDifficulty.HallOfFame: return "명예의 전당(전설)";
                default: return "KBO 정규(노멀)";
            }
        }

        /// <summary>FA · 재계약 요구액 배수(이지 0.85 ~ 전설 1.30).</summary>
        public static float DemandMultiplier(GMDifficulty d) => d == GMDifficulty.Minors ? 0.85f : d == GMDifficulty.AllStar ? 1.15f : d == GMDifficulty.HallOfFame ? 1.3f : 1f;

        /// <summary>AI 단장이 트레이드에서 요구하는 가치 배수(이지 0.9 ~ 전설 1.25).</summary>
        public static float TradeMargin(GMDifficulty d) => d == GMDifficulty.Minors ? 0.9f : d == GMDifficulty.AllStar ? 1.12f : d == GMDifficulty.HallOfFame ? 1.25f : 1f;

        /// <summary>FA 경쟁 구단 최고 입찰가 가산(이지 -0.04 ~ 전설 +0.09).</summary>
        public static float RivalBidBonus(GMDifficulty d) => d == GMDifficulty.Minors ? -0.04f : d == GMDifficulty.AllStar ? 0.05f : d == GMDifficulty.HallOfFame ? 0.09f : 0f;

        public static int StartingTrust(GMDifficulty d) => d == GMDifficulty.Minors ? 75 : d == GMDifficulty.AllStar ? 50 : d == GMDifficulty.HallOfFame ? 40 : 60;

        /// <summary>하우스 룰 순환 값(0 = 제한 없음).</summary>
        public static readonly int[] HouseRuleSteps = { 0, 3, 1 };
        public static string HouseRuleLabel(int max) => max <= 0 ? "제한 없음" : $"연 {max}회";

        /// <summary>[TASK-GM-07] 해임당하지 않음 옵션의 구단주 신임도 하한.</summary>
        public const int NoFiringTrustFloor = 25;

        public static GMManagerProfile Manager(GMLeagueState league) => league == null ? new GMManagerProfile() : (Ensure(league).Manager ?? (Ensure(league).Manager = new GMManagerProfile()));

        /// <summary>[TASK-GM-07] 감독 설정 적용 - 프로필 · 플레이 모드 옵션 5종 + 난이도(챌린지 모드는 연간 FA · 트레이드 1회로 고정).</summary>
        public static void ApplyManagerSetup(GMLeagueState league, GMManagerProfile profile, GMDifficulty difficulty)
        {
            if (league == null || profile == null) return;
            var fo = Ensure(league);
            fo.Manager = profile;
            ApplySettings(league, difficulty, profile.Challenge ? 1 : fo.HouseRuleMaxFA, profile.Challenge ? 1 : fo.HouseRuleMaxTrades);
        }

        /// <summary>새 시즌 설정 - 난이도 · 하우스 룰을 적용한다(신임도는 난이도 시작값으로 다시 맞춘다).</summary>
        public static void ApplySettings(GMLeagueState league, GMDifficulty difficulty, int maxFA, int maxTrades)
        {
            if (league == null) return;
            var fo = Ensure(league);
            fo.Difficulty = difficulty;
            if (fo.Manager != null && fo.Manager.Challenge) { maxFA = 1; maxTrades = 1; } // [TASK-GM-07] 챌린지 모드
            fo.HouseRuleMaxFA = Math.Max(0, maxFA);
            fo.HouseRuleMaxTrades = Math.Max(0, maxTrades);
            fo.OwnerTrust = StartingTrust(difficulty) + (league.Mode == GMStartMode.StoryCampaign ? -10 : 0);
            RefreshGoals(league);
        }

        public static bool CanSignFreeAgent(GMLeagueState league, out string reason)
        {
            var fo = Ensure(league);
            reason = null;
            if (fo.HouseRuleMaxFA > 0 && fo.FASigningsThisYear >= fo.HouseRuleMaxFA) { reason = $"하우스 룰 - 올해 FA 영입 한도({fo.HouseRuleMaxFA}회)를 모두 썼습니다."; return false; }
            return true;
        }

        public static bool CanTrade(GMLeagueState league, out string reason)
        {
            var fo = Ensure(league);
            reason = null;
            int limit = fo.HouseRuleMaxTrades > 0 ? fo.HouseRuleMaxTrades + fo.ExtraTradeAllowance : 0;
            if (limit > 0 && fo.TradesThisYear >= limit) { reason = $"하우스 룰 - 올해 트레이드 한도({limit}회)를 모두 썼습니다. 구단주에게 한도 해제를 건의하십시오."; return false; }
            return true;
        }

        // ================================================================== 초기화

        public static GMFrontOfficeState Ensure(GMLeagueState league)
        {
            if (league.FrontOffice == null) league.FrontOffice = new GMFrontOfficeState();
            if (!league.FrontOffice.Initialized) Initialize(league);
            return league.FrontOffice;
        }

        /// <summary>새 리그(로스터 로더) 또는 구버전 세이브에서 1회 - 구단주 · 과거 이력 · 목표 · 안건을 만든다.</summary>
        public static void Initialize(GMLeagueState league)
        {
            if (league == null) return;
            var fo = league.FrontOffice ?? (league.FrontOffice = new GMFrontOfficeState());
            fo.Initialized = true;
            var user = league.UserTeam;
            fo.Owner = BuildOwner(league.SelectedTeamCode ?? NameAliasTable.SAM, league.Mode);
            fo.OwnerTrust = StartingTrust(fo.Difficulty) + (league.Mode == GMStartMode.StoryCampaign ? -10 : 0);
            fo.History.Clear();
            foreach (var code in league.Teams.Keys) fo.History.AddRange(SeedHistory(code, league.SeasonYear, league.Mode == GMStartMode.StoryCampaign && code == league.SelectedTeamCode));
            RankSeededHistory(fo.History);
            if (user != null && league.Mode == GMStartMode.StoryCampaign)
            {
                fo.Owner.ExpectedRank = 5;
                fo.Owner.ExpectedPct = 0.5f;
            }
            fo.Agendas.Clear();
            OpenAgendas(league);
            RefreshGoals(league);
        }

        private static readonly Dictionary<string, string> Companies = new Dictionary<string, string>
        {
            { "SAM", "삼성 그룹" }, { "LG", "LG 그룹" }, { "HAN", "한화 그룹" }, { "NC", "엔씨소프트" }, { "KT", "KT 그룹" },
            { "KIA", "기아 · 현대자동차그룹" }, { "LOT", "롯데 그룹" }, { "DOO", "두산 그룹" }, { "KIW", "서울 히어로즈(키움증권)" }, { "SSG", "신세계 그룹" },
        };

        private static readonly string[] Surnames = { "김", "이", "박", "정", "최", "윤", "한", "강", "조", "신" };
        private static readonly string[] Given = { "태성", "도윤", "현석", "승우", "재민", "민호", "성훈", "준혁", "영수", "동건" };

        public static GMOwnerProfile BuildOwner(string teamCode, GMStartMode mode)
        {
            int h = Hash(teamCode + "_owner");
            var o = new GMOwnerProfile
            {
                Company = Companies.TryGetValue(teamCode, out var c) ? c : "모기업",
                OwnerName = $"{Surnames[h % Surnames.Length]}{Given[(h / 7) % Given.Length]} 구단주",
                Patience = h % 3,
                Fiscal = (h / 3) % 3,
                Involvement = (h / 9) % 3,
                Priority = (h / 27) % 3,
                ExpectedPct = 0.5f,
                ExpectedRank = 5,
            };
            if (mode == GMStartMode.StoryCampaign)
            {
                o.Patience = 0;      // 4년 연속 최하위 - 참을성 없음
                o.Fiscal = 0;        // 예산 20% 삭감 - 긴축
                o.Involvement = 2;
                o.Priority = 0;      // 포스트시즌 압박 - 성적
            }
            return o;
        }

        public static string PatienceLabel(int v) => v <= 0 ? "참을성 없음" : v >= 2 ? "인내" : "보통";
        public static string FiscalLabel(int v) => v <= 0 ? "긴축" : v >= 2 ? "아낌없는 투자" : "보통";
        public static string InvolvementLabel(int v) => v <= 0 ? "낮음" : v >= 2 ? "높음" : "보통";
        public static string PriorityLabel(int v) => v == 1 ? "흑자 경영" : v == 2 ? "팬심" : "성적";

        /// <summary>구단주 기분(신임도 기반) - 텍스트 이모티콘 + 상태.</summary>
        public static string MoodLabel(int trust) =>
            trust >= 80 ? "(^o^) 환호" : trust >= 62 ? "(^_^) 만족" : trust >= 45 ? "(-_-) 보통" : trust >= 25 ? "(>_<) 불만" : "(ToT) 격노";

        public static bool MoodIsGood(int trust) => trust >= 45;

        public static string ExpectationLabel(GMOwnerProfile o, GMStartMode mode) =>
            mode == GMStartMode.StoryCampaign ? "포스트시즌 진출(5위 이내) - 최후통첩" : $"승률 {o.ExpectedPct:.000} 이상 · {o.ExpectedRank}위 이내";

        // ================================================================== 시즌 이력

        /// <summary>구단 시장 규모별 기본 관중(경기당).</summary>
        public static int MarketBase(string code)
        {
            switch (code)
            {
                case "LG": return 15500;
                case "DOO": return 15000;
                case "LOT": return 14500;
                case "KIA": return 14000;
                case "SAM": return 13500;
                case "SSG": return 12500;
                case "HAN": return 12000;
                case "KT": return 10500;
                case "NC": return 9500;
                default: return 9000;
            }
        }

        public static int AttendanceFor(string code, double pct, int fanSupport, int cheerHomeRevenue)
        {
            double v = MarketBase(code) * (0.72 + pct * 0.56) * (0.85 + fanSupport / 333.0) + Math.Min(4000, cheerHomeRevenue * 2);
            return (int)Math.Round(v / 10.0) * 10;
        }

        private static List<GMSeasonHistoryEntry> SeedHistory(string code, int startYear, bool storyLastPlace)
        {
            var list = new List<GMSeasonHistoryEntry>();
            for (int k = HistorySeasons; k >= 1; k--)
            {
                int year = startYear - k;
                int h = Hash($"{code}_{year}");
                double pct = 0.39 + (h % 220) / 1000.0; // .390 ~ .610
                if (storyLastPlace && k <= 4) pct = 0.36 + (h % 40) / 1000.0;
                int decisions = 140;
                int d = h % 4;
                int w = (int)Math.Round(pct * decisions);
                list.Add(new GMSeasonHistoryEntry
                {
                    TeamCode = code, Year = year, W = w, D = d, L = decisions - w, Rank = 0,
                    AttendancePerGame = AttendanceFor(code, pct, 50, 0) - (year <= 2021 && year >= 2020 ? 6000 : 0),
                });
            }
            return list;
        }

        /// <summary>시드 이력의 연도별 순위를 승률 순으로 매긴다(스토리 캠페인 최하위 4년은 그대로 10위가 된다).</summary>
        private static void RankSeededHistory(List<GMSeasonHistoryEntry> history)
        {
            foreach (var year in history.Select(h => h.Year).Distinct().ToList())
            {
                var rows = history.Where(h => h.Year == year).OrderByDescending(h => h.Pct).ToList();
                for (int i = 0; i < rows.Count; i++)
                {
                    rows[i].Rank = i + 1;
                    rows[i].Postseason = i < 5;
                    rows[i].Champion = i == 0 && Hash($"{rows[i].TeamCode}_{year}_ks") % 2 == 0;
                }
            }
        }

        public static List<GMSeasonHistoryEntry> HistoryOf(GMLeagueState league, string teamCode) =>
            Ensure(league).History.Where(h => h.TeamCode == teamCode).OrderBy(h => h.Year).ToList();

        /// <summary>RECORD HISTORY 막대 - 최근 maxSeasons개 시즌 + (진행 중이면) 현재 시즌.</summary>
        public static List<(int year, double pct, bool current)> RecordBars(GMLeagueState league, string teamCode, int maxSeasons = 10)
        {
            var bars = HistoryOf(league, teamCode).Select(h => (h.Year, h.Pct, false)).ToList();
            var rec = league.RecordOf(teamCode);
            if (rec.G > 0) bars.Add((league.SeasonYear, rec.Pct, true));
            return bars.Skip(Math.Max(0, bars.Count - maxSeasons)).ToList();
        }

        /// <summary>ATTENDANCE HISTORY 막대 - 과거 시즌 + 올해 예상(현재 승률 · 팬 지지율 · 응원단 홈 흥행).</summary>
        public static List<(int year, int attendance, bool current)> AttendanceBars(GMLeagueState league, string teamCode, int maxSeasons = 10)
        {
            var bars = HistoryOf(league, teamCode).Select(h => (h.Year, h.AttendancePerGame, false)).ToList();
            bars.Add((league.SeasonYear, CurrentAttendance(league, teamCode), true));
            return bars.Skip(Math.Max(0, bars.Count - maxSeasons)).ToList();
        }

        public static int CurrentAttendance(GMLeagueState league, string teamCode)
        {
            if (!league.Teams.TryGetValue(teamCode, out var team)) return 0;
            var rec = league.RecordOf(teamCode);
            double pct = rec.G > 0 ? rec.Pct : LastPct(league, teamCode);
            return AttendanceFor(teamCode, pct, team.FanSupport, GMCheerleaderRoster.HomeRevenue(team.CheerEntry));
        }

        public static double LastPct(GMLeagueState league, string teamCode)
        {
            var last = HistoryOf(league, teamCode).LastOrDefault();
            return last != null ? last.Pct : 0.5;
        }

        public static int LastRank(GMLeagueState league, string teamCode) => HistoryOf(league, teamCode).LastOrDefault()?.Rank ?? 5;

        // ================================================================== 가성비 · 재정

        /// <summary>WAR(시즌 10경기 이상 진행 시 실제 누적 WAR, 그 전에는 OVR 기반 예상 WAR).</summary>
        public static double WarOf(GMLeagueState league, Player p)
        {
            if (league.Stats.TryGetValue(p.InstanceId, out var st) && (st.G + st.PG) >= 10) return Math.Round(st.WAR, 1);
            double proj = (p.BaseOverall - 58) * (p.IsPitcher ? 0.11 : 0.13);
            return Math.Round(Math.Max(0.1, proj), 1);
        }

        /// <summary>COST EFFICIENT PLAYERS - WAR 0.1 이상 선수를 1WAR당 연봉(만 원)이 낮은 순으로.</summary>
        public static List<(Player player, double war, int perWar)> CostEfficient(GMLeagueState league, GMTeamState team, int top = 10)
        {
            if (team == null) return new List<(Player, double, int)>();
            return team.Roster.Select(p => (p, war: WarOf(league, p)))
                .Where(x => x.war >= 0.1)
                .Select(x => (x.p, x.war, (int)Math.Round(x.p.Salary / x.war)))
                .OrderBy(x => x.Item3).ThenByDescending(x => x.war).Take(top).ToList();
        }

        public sealed class BudgetInfo
        {
            public long ProjectedBudget, GuaranteedPayroll, ReSignEstimate, CheerRevenue, OtherExpenses, TotalExpenses, MoneyForFA, MoneyForExtensions;
            public bool OverBudget => TotalExpenses > ProjectedBudget;
        }

        /// <summary>
        /// EXTENSION &amp; BUDGET INFORMATION.
        ///   예상 시즌 총예산 = 운영 자금(Budget) + 모기업 연봉 보전 한도(샐러리캡) + 응원단 · 홈 마케팅 예상 수익(남은 홈경기)
        ///   총 지출 예상 = 보장 연봉(잔여 계약 1년 이상) + 재계약 예상 총액(계약 만료자 요구액) + 기타 운영비(캡 15%)
        ///   FA 영입 가용 자금 = 총예산 - 총 지출, 다년 연장계약 가용 자금 = 총예산 - 보장 연봉 - 기타 운영비
        /// </summary>
        public static BudgetInfo Budget(GMLeagueState league, GMTeamState team)
        {
            var info = new BudgetInfo();
            if (team == null) return info;
            var diff = Ensure(league).Difficulty;
            int remainingHome = Math.Max(0, HomeGames - league.RecordOf(team.TeamCode).G / 2);
            info.CheerRevenue = (long)GMCheerleaderRoster.HomeRevenue(team.CheerEntry) * remainingHome;
            info.ProjectedBudget = team.Budget + team.PayrollCap + info.CheerRevenue;
            info.GuaranteedPayroll = team.Roster.Where(p => p.ContractYears > 0).Sum(p => (long)p.Salary);
            info.ReSignEstimate = team.Roster.Where(p => p.ContractYears <= 0).Sum(p => (long)GMStoveLeagueMarket.ExtensionDemand(p, diff));
            info.OtherExpenses = team.PayrollCap * 15L / 100;
            info.TotalExpenses = info.GuaranteedPayroll + info.ReSignEstimate + info.OtherExpenses;
            info.MoneyForFA = info.ProjectedBudget - info.TotalExpenses;
            info.MoneyForExtensions = info.ProjectedBudget - info.GuaranteedPayroll - info.OtherExpenses;
            return info;
        }

        // ================================================================== 구단주 목표

        /// <summary>목표 5~6개를 (없으면) 만들고 진행 상황을 갱신한다.</summary>
        public static void RefreshGoals(GMLeagueState league)
        {
            var fo = league.FrontOffice ?? (league.FrontOffice = new GMFrontOfficeState());
            var team = league.UserTeam;
            if (team == null) return;
            if (fo.Goals.Count == 0 || !fo.Goals.Any(g => g.Kind == GMGoalKind.WinningRecord && g.TargetYear >= league.SeasonYear)) BuildGoals(league);
            foreach (var g in fo.Goals) UpdateGoalProgress(league, team, g);
        }

        private static void BuildGoals(GMLeagueState league)
        {
            var fo = league.FrontOffice;
            var team = league.UserTeam;
            int y = league.SeasonYear;
            var o = fo.Owner;
            bool story = league.Mode == GMStartMode.StoryCampaign;
            fo.Goals.RemoveAll(g => g.TargetYear < y);
            var weak = WeakestPosition(team);
            var list = new List<GMOwnerGoal>
            {
                new GMOwnerGoal { Id = "G_RECORD", Kind = GMGoalKind.WinningRecord, YearIssued = y, TargetYear = y,
                    Priority = story ? GMGoalPriority.VeryHigh : o.Priority == 0 ? GMGoalPriority.High : GMGoalPriority.Average,
                    Category = "팀 성적", Description = story ? "포스트시즌 진출(5위 이내)" : $"승률 {o.ExpectedPct:.000} 이상 · {o.ExpectedRank}위 이내",
                    TargetValue = (int)Math.Round(o.ExpectedPct * 1000) },
                new GMOwnerGoal { Id = "G_UPGRADE", Kind = GMGoalKind.UpgradePosition, YearIssued = y, TargetYear = y, Priority = GMGoalPriority.Average,
                    Category = "포지션 보강", TargetPosition = weak.position, TargetValue = Math.Min(90, weak.ovr + 6),
                    Description = weak.position == "C" ? $"ABS 수비형 포수 보강(OVR {Math.Min(90, weak.ovr + 6)}+)" : $"{PositionLabel(weak.position)} 보강(OVR {Math.Min(90, weak.ovr + 6)}+)" },
                new GMOwnerGoal { Id = "G_AWARD", Kind = GMGoalKind.AwardWinner, YearIssued = y, TargetYear = y, Priority = GMGoalPriority.Average,
                    Category = "수상", Description = "골든글러브 또는 타이틀 홀더 1명 이상 배출" },
                new GMOwnerGoal { Id = "G_ATTEND", Kind = GMGoalKind.Attendance, YearIssued = y, TargetYear = y + 2,
                    Priority = o.Priority == 2 ? GMGoalPriority.VeryHigh : GMGoalPriority.Average, Category = "홈 관중",
                    TargetValue = (int)Math.Round(MarketBase(team.TeamCode) * 1.1 / 100.0) * 100 },
                new GMOwnerGoal { Id = "G_PAYROLL", Kind = GMGoalKind.Payroll, YearIssued = y, TargetYear = y,
                    Priority = o.Priority == 1 || o.Fiscal == 0 ? GMGoalPriority.High : GMGoalPriority.Low, Category = "페이롤",
                    TargetValue = team.PayrollCap * (o.Fiscal == 2 ? 2 : o.Fiscal == 1 ? 3 : 1) / (o.Fiscal == 1 ? 2 : 1) },
                new GMOwnerGoal { Id = "G_TITLE", Kind = GMGoalKind.Championship, YearIssued = y, TargetYear = y + 3, Priority = GMGoalPriority.Low,
                    Category = "장기 목표", Description = "한국시리즈 우승" },
            };
            list[3].Description = $"홈 평균 관중 {list[3].TargetValue:N0}명(응원단 홈 흥행)";
            list[4].Description = $"페이롤 {GMDiagnosticFormat.Won(list[4].TargetValue)} 이하 유지";
            foreach (var g in list) if (!fo.Goals.Any(x => x.Id == g.Id)) fo.Goals.Add(g);
            fo.Goals = fo.Goals.OrderBy(g => (int)g.Kind).ToList();
        }

        /// <summary>센터라인(포수 · 유격수 · 중견수) 우선으로 주전 OVR이 가장 낮은 포지션.</summary>
        public static (string position, int ovr) WeakestPosition(GMTeamState team)
        {
            var starters = LineupAssignment.AssignStarters(team.AvailableRoster, team.Lineup).Where(s => s.Player != null && s.Position != BatterPosition.DesignatedHitter).ToList();
            if (starters.Count == 0) return ("C", 60);
            var worst = starters.OrderBy(s => s.Player.BaseOverall - (IsCenterLine(s.Position) ? 3 : 0)).First();
            return (PositionCode(worst.Position), worst.Player.BaseOverall);
        }

        private static bool IsCenterLine(BatterPosition p) => p == BatterPosition.Catcher || p == BatterPosition.ShortStop || p == BatterPosition.CenterField;

        public static string PositionCode(BatterPosition p)
        {
            switch (p)
            {
                case BatterPosition.Catcher: return "C";
                case BatterPosition.FirstBase: return "1B";
                case BatterPosition.SecondBase: return "2B";
                case BatterPosition.ThirdBase: return "3B";
                case BatterPosition.ShortStop: return "SS";
                case BatterPosition.LeftField: return "LF";
                case BatterPosition.CenterField: return "CF";
                case BatterPosition.RightField: return "RF";
                default: return "DH";
            }
        }

        /// <summary>포지션 코드 → KBO 한글 약칭(화면용 - 영문 enum 노출 금지).</summary>
        public static string PositionLabel(string code)
        {
            switch (code)
            {
                case "C": return "포수";
                case "1B": return "1루수";
                case "2B": return "2루수";
                case "3B": return "3루수";
                case "SS": return "유격수";
                case "LF": return "좌익수";
                case "CF": return "중견수";
                case "RF": return "우익수";
                case "DH": return "지명타자";
                case "SP": return "선발";
                case "CP": return "마무리";
                case "LR": return "롱릴리프";
                case "MR": return "추격조";
                case "RP": return "불펜";
                default: return code ?? "";
            }
        }

        public static string PriorityText(GMGoalPriority p) => p == GMGoalPriority.Low ? "낮음" : p == GMGoalPriority.High ? "높음" : p == GMGoalPriority.VeryHigh ? "매우 높음" : "보통";

        private static void UpdateGoalProgress(GMLeagueState league, GMTeamState team, GMOwnerGoal g)
        {
            var rec = league.RecordOf(team.TeamCode);
            switch (g.Kind)
            {
                case GMGoalKind.WinningRecord:
                {
                    int rank = RankOf(league, team.TeamCode);
                    g.Progress = rec.G > 0 ? $"{rec.W}승 {rec.D}무 {rec.L}패 {GMTeamRecord.PctLabel(rec.Pct)} · {rank}위" : $"개막 전 · 지난 시즌 {LastRank(league, team.TeamCode)}위";
                    bool story = league.Mode == GMStartMode.StoryCampaign;
                    g.OnTrack = rec.G > 0 ? (story ? rank <= 5 : rec.Pct * 1000 >= g.TargetValue - 0.5) : LastPct(league, team.TeamCode) * 1000 >= g.TargetValue - 0.5;
                    break;
                }
                case GMGoalKind.UpgradePosition:
                {
                    var best = team.Roster.Where(p => p.Position == g.TargetPosition).OrderByDescending(p => p.BaseOverall).FirstOrDefault();
                    int ovr = best?.BaseOverall ?? 0;
                    g.Progress = best != null ? $"현재 최고 {best.Template.PlayerName} OVR {ovr}" : $"{PositionLabel(g.TargetPosition)} 없음";
                    g.OnTrack = ovr >= g.TargetValue;
                    break;
                }
                case GMGoalKind.AwardWinner:
                {
                    var winners = (league.Awards?.KboCeremony ?? new List<GMAwardWinner>()).Concat(league.Awards?.GoldenGlove ?? new List<GMAwardWinner>())
                        .Where(a => a != null && a.TeamCode == team.TeamCode).ToList();
                    if (winners.Count > 0) { g.Progress = $"수상 {winners.Count}건 · {winners[0].PlayerName}"; g.OnTrack = true; break; }
                    var top = team.Roster.OrderByDescending(p => WarOf(league, p)).FirstOrDefault();
                    g.Progress = top != null ? $"유력 후보 {top.Template.PlayerName} (WAR {WarOf(league, top):0.0})" : "후보 없음";
                    g.OnTrack = top != null && WarOf(league, top) >= 4.0;
                    break;
                }
                case GMGoalKind.Attendance:
                {
                    int now = CurrentAttendance(league, team.TeamCode);
                    g.Progress = $"현재 {now:N0}명/경기";
                    g.OnTrack = now >= g.TargetValue;
                    break;
                }
                case GMGoalKind.Payroll:
                    g.Progress = $"현재 {GMDiagnosticFormat.Won(team.Payroll)}";
                    g.OnTrack = team.Payroll <= g.TargetValue;
                    break;
                case GMGoalKind.Championship:
                {
                    int titles = Ensure(league).History.Count(h => h.TeamCode == team.TeamCode && h.Champion && h.Year >= g.YearIssued);
                    g.Progress = titles > 0 ? $"우승 {titles}회 달성" : $"{g.TargetYear}년까지";
                    g.OnTrack = titles > 0;
                    break;
                }
            }
        }

        public static int RankOf(GMLeagueState league, string teamCode)
        {
            var rows = league.Teams.Keys.Select(league.RecordOf).OrderByDescending(r => r.Pct).ThenByDescending(r => r.W).ToList();
            int i = rows.FindIndex(r => r.TeamCode == teamCode);
            return i < 0 ? 0 : i + 1;
        }

        /// <summary>목표가 [건의] 가능한지(아직 달성 전 · 올해 건의 전 · 장기 목표 제외).</summary>
        public static bool CanDiscuss(GMOwnerGoal g) => g != null && !g.OnTrack && !g.Discussed && g.Kind != GMGoalKind.Championship;

        /// <summary>
        /// [건의(DISCUSS)] - 신임도를 소모해 목표를 완화하거나(우선순위 한 단계 ↓ · 기준 완화) 추가 예산(샐러리캡 5%) · 트레이드 한도 +2를 얻는다.
        /// 신임도가 30 미만이면 구단주가 거절하고 신임도 -3. 결과 메시지를 돌려준다.
        /// </summary>
        public static bool Discuss(GMLeagueState league, GMOwnerGoal goal, GMDiscussOption option, out string message)
        {
            var fo = Ensure(league);
            var team = league.UserTeam;
            message = "";
            if (goal == null || team == null) { message = "건의할 목표를 고르십시오."; return false; }
            if (goal.Discussed) { message = "이 목표는 올해 이미 건의했습니다."; return false; }
            if (goal.Kind == GMGoalKind.Championship) { message = "장기 목표는 건의 대상이 아닙니다."; return false; }
            goal.Discussed = true;
            int threshold = DiscussMinTrust + (fo.Owner.Patience == 0 ? 8 : 0);
            if (fo.OwnerTrust < threshold)
            {
                fo.OwnerTrust = Math.Max(0, fo.OwnerTrust - 3);
                message = $"{fo.Owner.OwnerName}: \"지금 그런 말을 할 처지가 아니네.\" - 건의 거절 · 신임도 -3 (현재 {fo.OwnerTrust})";
                return false;
            }
            switch (option)
            {
                case GMDiscussOption.ExtraBudget:
                {
                    long extra = team.PayrollCap * (long)ExtraBudgetPercent / 100;
                    team.Budget += extra;
                    fo.OwnerTrust = Math.Max(0, fo.OwnerTrust - ExtraBudgetTrustCost);
                    message = $"추가 예산 {GMDiagnosticFormat.Won(extra)} 확보 · 구단주 신임도 -{ExtraBudgetTrustCost} (현재 {fo.OwnerTrust})";
                    break;
                }
                case GMDiscussOption.LiftTradeLimit:
                    fo.ExtraTradeAllowance += 2;
                    fo.OwnerTrust = Math.Max(0, fo.OwnerTrust - LiftTradeTrustCost);
                    message = $"트레이드 한도 +2회 해제 · 구단주 신임도 -{LiftTradeTrustCost} (현재 {fo.OwnerTrust})";
                    break;
                default:
                    if (goal.Priority > GMGoalPriority.Low) goal.Priority--;
                    switch (goal.Kind)
                    {
                        case GMGoalKind.WinningRecord:
                            goal.TargetValue = Math.Max(400, goal.TargetValue - 30);
                            goal.Description = league.Mode == GMStartMode.StoryCampaign ? "탈꼴찌 · 승률 .470 이상" : $"승률 {goal.TargetValue / 1000.0:.000} 이상";
                            if (league.Mode == GMStartMode.StoryCampaign) goal.TargetValue = 470;
                            break;
                        case GMGoalKind.UpgradePosition:
                            goal.TargetValue = Math.Max(55, goal.TargetValue - 4);
                            goal.Description = $"{PositionLabel(goal.TargetPosition)} 보강(OVR {goal.TargetValue}+)";
                            break;
                        case GMGoalKind.Attendance:
                            goal.TargetValue = Math.Max(5000, goal.TargetValue * 90 / 100);
                            goal.Description = $"홈 평균 관중 {goal.TargetValue:N0}명(응원단 홈 흥행)";
                            break;
                        case GMGoalKind.Payroll:
                            goal.TargetValue = goal.TargetValue * 110 / 100;
                            goal.Description = $"페이롤 {GMDiagnosticFormat.Won(goal.TargetValue)} 이하 유지";
                            break;
                        case GMGoalKind.AwardWinner:
                            goal.TargetYear += 1;
                            goal.Description = "골든글러브 또는 타이틀 홀더 1명 이상 배출(1년 유예)";
                            break;
                    }
                    fo.OwnerTrust = Math.Max(0, fo.OwnerTrust - DiscussTrustCost);
                    message = $"목표 완화 합의: {goal.Description} · 구단주 신임도 -{DiscussTrustCost} (현재 {fo.OwnerTrust})";
                    break;
            }
            UpdateGoalProgress(league, team, goal);
            return true;
        }

        // ================================================================== 스토리 안건

        public sealed class AgendaOption
        {
            public string Label;
            public long Budget;      // 만 원
            public int Fan, Trust, Teamwork;
            public Action<GMLeagueState, GMTeamState> Extra;
            public string Note;
        }

        public sealed class AgendaDef
        {
            public string Id, Title, Body;
            public bool StoryOnly;
            public AgendaOption[] Options;
        }

        private static Player TradeRequester(GMTeamState t) => t?.Roster.FirstOrDefault(p => p.Template.RealPlayerId == t.TradeRequestPlayerId)
                                                               ?? t?.Roster.Where(p => !p.IsPitcher).OrderByDescending(p => p.EgoLevel).ThenByDescending(p => p.Age).FirstOrDefault();

        public static readonly AgendaDef[] AgendaDefs =
        {
            new AgendaDef { Id = "A_TRADE_DEMAND", StoryOnly = true, Title = "간판 4번 타자의 트레이드 요구",
                Body = "35세 Ego 5 노장 4번 타자가 \"우승할 수 있는 팀으로 보내 달라\"며 공개 트레이드를 요구했습니다.",
                Options = new[]
                {
                    new AgendaOption { Label = "설득 + 주장 완장", Teamwork = 6, Trust = -2, Note = "주장 임명 · 만족도 +20",
                        Extra = (l, t) => { var p = TradeRequester(t); if (p == null) return; foreach (var x in t.Roster) x.IsCaptain = false; p.IsCaptain = true; p.PersonalMorale = Math.Min(100, p.PersonalMorale + 20); t.TradeRequestPlayerId = null; } },
                    new AgendaOption { Label = "트레이드 승인", Teamwork = 3, Fan = -8, Trust = 2, Note = "트레이드 탭 매물 등록" },
                    new AgendaOption { Label = "요구 거절", Teamwork = -8, Note = "만족도 -20",
                        Extra = (l, t) => { var p = TradeRequester(t); if (p != null) p.PersonalMorale = Math.Max(0, p.PersonalMorale - 20); } },
                } },
            new AgendaDef { Id = "A_MANAGER_VETERAN", Title = "감독의 노장 재계약 고집",
                Body = "감독이 \"벤치 분위기는 베테랑이 잡는다\"며 38세 노장 투수와의 1년 재계약을 강하게 요구합니다.",
                Options = new[]
                {
                    new AgendaOption { Label = "감독 요구 수용", Budget = -30000, Teamwork = 4, Trust = -3 },
                    new AgendaOption { Label = "세대교체 고수", Teamwork = -5, Trust = 3 },
                } },
            new AgendaDef { Id = "A_FA_OVERPAY", Title = "FA 오버페이 추경 요청",
                Body = "시장 최대어 FA 영입전이 과열됐습니다. 구단주에게 추가경정 예산을 요청하시겠습니까?",
                Options = new[]
                {
                    new AgendaOption { Label = "추경 요청(+10억)", Budget = 100000, Trust = -10 },
                    new AgendaOption { Label = "기존 예산 유지", Trust = 3, Fan = -3 },
                } },
            new AgendaDef { Id = "A_FACTION", Title = "라커룸 파벌 중재",
                Body = "알파독 스타들과 베테랑 그룹 사이 신경전이 언론에 보도됐습니다. 단장이 직접 중재에 나서야 합니다.",
                Options = new[]
                {
                    new AgendaOption { Label = "주장 중심 선수단 미팅", Budget = -5000, Teamwork = 8 },
                    new AgendaOption { Label = "알파독 편들기", Teamwork = -4, Note = "Ego 5 만족도 +10",
                        Extra = (l, t) => { foreach (var p in t.Roster.Where(x => x.EgoLevel >= 5)) p.PersonalMorale = Math.Min(100, p.PersonalMorale + 10); } },
                    new AgendaOption { Label = "방관", Teamwork = -8 },
                } },
            new AgendaDef { Id = "A_CHEER_INVEST", Title = "응원단 · 홈 마케팅 투자",
                Body = "응원단장이 홈 개막 시리즈 대형 단상 이벤트와 신규 응원가 제작 예산을 요청했습니다.",
                Options = new[]
                {
                    new AgendaOption { Label = "투자 승인(-2억)", Budget = -20000, Fan = 6, Teamwork = 2 },
                    new AgendaOption { Label = "보류", Fan = -2 },
                } },
            new AgendaDef { Id = "A_YOUTH", Title = "유망주 육성 vs 즉시 전력",
                Body = "코칭스태프가 유망주 3명의 1군 고정 기용을 요청했습니다. 구단주는 즉시 전력을 원합니다.",
                Options = new[]
                {
                    new AgendaOption { Label = "유망주 육성", Trust = -2, Fan = 2, Teamwork = 2, Note = "유망주 만족도 +15",
                        Extra = (l, t) => { foreach (var p in t.Roster.Where(x => x.RoleArchetype == LockerRoomRole.Prospect)) p.PersonalMorale = Math.Min(100, p.PersonalMorale + 15); } },
                    new AgendaOption { Label = "즉시 전력 우선", Trust = 3, Teamwork = -2 },
                } },
            new AgendaDef { Id = "A_ABS_CATCHER", Title = "ABS 시대 포수 전략",
                Body = "ABS 도입으로 프레이밍 가치가 사라졌습니다. 포수진 블로킹 · 도루저지 특훈에 투자하시겠습니까?",
                Options = new[]
                {
                    new AgendaOption { Label = "블로킹 특훈(-1억)", Budget = -10000, Teamwork = 1, Note = "포수 ABS +6",
                        Extra = (l, t) => { foreach (var p in t.Roster.Where(x => x.Position == "C")) p.AbsTrainingBonus += 6; } },
                    new AgendaOption { Label = "현행 유지" },
                } },
            new AgendaDef { Id = "A_OWNER_PROFIT", StoryOnly = true, Title = "구단주의 흑자 경영 압박",
                Body = "모기업 실적 악화로 구단주가 \"내년엔 반드시 흑자\"를 주문했습니다.",
                Options = new[]
                {
                    new AgendaOption { Label = "연봉 구조조정 약속", Trust = 6, Teamwork = -4 },
                    new AgendaOption { Label = "성적 우선 고수", Trust = -6, Fan = 2 },
                } },
            new AgendaDef { Id = "A_COMMUNITY", Title = "팬 사인회 · 지역 사회 공헌",
                Body = "지역 아동센터 방문과 팬 사인회 일정이 잡혔습니다. 선수단 휴식일과 겹칩니다.",
                Options = new[]
                {
                    new AgendaOption { Label = "실시(-3,000만)", Budget = -3000, Fan = 5, Teamwork = 2 },
                    new AgendaOption { Label = "생략", Fan = -2 },
                } },
            new AgendaDef { Id = "A_SCOUT_TRIP", Title = "해외 스카우트 파견",
                Body = "스카우트 팀이 FA 시장 전원에 대한 정밀 리포트를 위해 해외 파견을 요청했습니다.",
                Options = new[]
                {
                    new AgendaOption { Label = "파견(-1억 5천)", Budget = -15000, Note = "FA 전원 스카우팅",
                        Extra = (l, t) => { foreach (var p in l.FreeAgents) p.IsScouted = true; } },
                    new AgendaOption { Label = "생략" },
                } },
        };

        public static AgendaDef DefOf(string id) => AgendaDefs.FirstOrDefault(a => a.Id == id);

        /// <summary>스토리 캠페인은 10종 전부, 그 밖의 모드는 공통 안건 4종을 연다(시즌마다 다시 열린다).</summary>
        public static void OpenAgendas(GMLeagueState league)
        {
            var fo = league.FrontOffice;
            bool story = league.Mode == GMStartMode.StoryCampaign;
            var ids = story ? AgendaDefs.Select(a => a.Id) : new[] { "A_MANAGER_VETERAN", "A_FA_OVERPAY", "A_CHEER_INVEST", "A_COMMUNITY", "A_ABS_CATCHER" }.AsEnumerable();
            fo.Agendas.RemoveAll(a => a.Resolved);
            foreach (var id in ids)
                if (!fo.Agendas.Any(a => a.Id == id)) fo.Agendas.Add(new GMAgendaState { Id = id, YearRaised = league.SeasonYear });
        }

        public static List<GMAgendaState> OpenAgendaStates(GMLeagueState league) => Ensure(league).Agendas.Where(a => !a.Resolved && DefOf(a.Id) != null).ToList();

        /// <summary>안건 선택지 실행 - 예산 · 팬 지지율 · 구단주 신임도 · 팀워크(안건 보정)를 바꾸고 결과 메시지를 돌려준다.</summary>
        public static bool ResolveAgenda(GMLeagueState league, string agendaId, int option, out string message)
        {
            var fo = Ensure(league);
            var team = league.UserTeam;
            var state = fo.Agendas.FirstOrDefault(a => a.Id == agendaId && !a.Resolved);
            var def = DefOf(agendaId);
            message = "";
            if (state == null || def == null || team == null) { message = "진행 중인 안건이 아닙니다."; return false; }
            if (option < 0 || option >= def.Options.Length) { message = "선택지를 고르십시오."; return false; }
            var o = def.Options[option];
            team.Budget += o.Budget;
            team.FanSupport = GMTeamFan.Clamp(team.FanSupport + o.Fan);
            fo.OwnerTrust = Math.Max(0, Math.Min(MaxTrust, fo.OwnerTrust + o.Trust));
            team.AgendaTeamworkBonus = Math.Max(-20, Math.Min(20, team.AgendaTeamworkBonus + o.Teamwork));
            o.Extra?.Invoke(league, team);
            state.Resolved = true;
            state.ChosenOption = option;
            var parts = new List<string>();
            if (o.Budget != 0) parts.Add($"예산 {(o.Budget > 0 ? "+" : "-")}{GMDiagnosticFormat.Won(Math.Abs(o.Budget))}");
            if (o.Fan != 0) parts.Add($"팬 지지율 {o.Fan:+0;-0}");
            if (o.Trust != 0) parts.Add($"구단주 신임도 {o.Trust:+0;-0}");
            if (o.Teamwork != 0) parts.Add($"팀워크 {o.Teamwork:+0;-0}");
            if (!string.IsNullOrEmpty(o.Note)) parts.Add(o.Note);
            message = $"[{def.Title}] {o.Label} - {(parts.Count > 0 ? string.Join(" · ", parts) : "변화 없음")}";
            league.AddNews(new GMNewsItem { GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Season, Title = $"단장 결단: {def.Title}", Body = message, IsUserTeam = true });
            return true;
        }

        // ================================================================== 시즌 결산 · 엔딩

        /// <summary>
        /// 4종 멀티 엔딩 판정(순수 함수):
        ///   ① 적자 해임 - 운영 자금 적자(Budget &lt; 0)이고 신임도 50 미만, 또는 신임도 15 이하이면서 적자
        ///   ② 기적의 가을야구 - 포스트시즌(5위 이내) 진출
        ///   ③ 성공적 리빌딩 - 가을야구 실패지만 신임도 35 이상이고 (순위 상승 또는 젊은 선수단 평균 27세 이하)
        ///   ④ 성적 부진 해임 - 그 외
        /// </summary>
        public static GMEnding EvaluateEnding(bool postseason, long budget, int trust, bool rankImproved, double rosterAvgAge)
        {
            if (budget < 0 && trust < 50) return GMEnding.DeficitFired;
            if (postseason) return GMEnding.MiracleAutumn;
            if (trust >= 35 && (rankImproved || rosterAvgAge <= 27.0)) return GMEnding.SuccessfulRebuild;
            return GMEnding.PerformanceFired;
        }

        public static string EndingLabel(GMEnding e)
        {
            switch (e)
            {
                case GMEnding.MiracleAutumn: return "엔딩 ① 기적의 가을야구";
                case GMEnding.SuccessfulRebuild: return "엔딩 ② 성공적 리빌딩";
                case GMEnding.DeficitFired: return "엔딩 ③ 적자 해임";
                case GMEnding.PerformanceFired: return "엔딩 ④ 성적 부진 해임";
                default: return "엔딩 판정 전";
            }
        }

        public static string EndingStory(GMEnding e)
        {
            switch (e)
            {
                case GMEnding.MiracleAutumn: return "4년 연속 꼴찌 구단이 가을야구 무대에 섰습니다. 구장은 응원단의 함성으로 가득 찼고, 구단주는 단장의 재계약서에 서명했습니다.";
                case GMEnding.SuccessfulRebuild: return "가을야구는 놓쳤지만 젊은 선수단과 건강한 재정이 남았습니다. 구단주는 \"내년이 기대된다\"며 신임을 이어 갑니다.";
                case GMEnding.DeficitFired: return "성적과 무관하게 적자가 쌓였습니다. 모기업 감사 끝에 단장은 경질 통보를 받았습니다.";
                case GMEnding.PerformanceFired: return "또다시 하위권. 참을성이 바닥난 구단주가 새 단장을 선임했습니다.";
                default: return "";
            }
        }

        /// <summary>엔딩 전망(시즌 중 · 스토브리그) - 현재 순위 · 예산 · 신임도로 판정한다.</summary>
        public static GMEnding ForecastEnding(GMLeagueState league)
        {
            var team = league.UserTeam;
            if (team == null) return GMEnding.None;
            var fo = Ensure(league);
            int rank = league.RecordOf(team.TeamCode).G > 0 ? RankOf(league, team.TeamCode) : LastRank(league, team.TeamCode);
            return EvaluateEnding(rank <= 5, team.Budget, fo.OwnerTrust, rank < LastRank(league, team.TeamCode), team.Roster.Count > 0 ? team.Roster.Average(p => p.Age) : 30);
        }

        /// <summary>
        /// 시즌 결산(연도 전환 직전) - 10구단 시즌 이력 · 관중 기록, 목표 달성 판정 · 구단주 신임도 갱신, (스토리 캠페인) 엔딩 판정.
        /// finalRanks = 포스트시즌 최종 순위(없으면 정규시즌 순위), championCode = 한국시리즈 우승 구단.
        /// </summary>
        public static void OnSeasonCompleted(GMLeagueState league, IReadOnlyList<string> regularSeasonRanks, string championCode)
        {
            var fo = Ensure(league);
            var team = league.UserTeam;
            GMSeasonReview.TakeSnapshot(league); // [TASK-GM-11] 연도 전환(기록 초기화) 전에 시즌 결산 스냅숏
            int prevRank = team != null ? LastRank(league, team.TeamCode) : 5;
            for (int i = 0; i < regularSeasonRanks.Count; i++)
            {
                string code = regularSeasonRanks[i];
                var rec = league.RecordOf(code);
                fo.History.RemoveAll(h => h.TeamCode == code && h.Year == league.SeasonYear);
                fo.History.Add(new GMSeasonHistoryEntry
                {
                    TeamCode = code, Year = league.SeasonYear, W = rec.W, D = rec.D, L = rec.L, Rank = i + 1,
                    AttendancePerGame = CurrentAttendance(league, code), Postseason = i < 5, Champion = code == championCode,
                });
            }
            if (team == null) return;
            GMPromiseSystem.Evaluate(league); // [TASK-GM-13] 약속 이행 검사(시즌 기록이 남아 있는 연도 전환 전) - 위반 = 충성도 -25 · 신뢰도 -10 · 긴장 BGM
            foreach (var g in fo.Goals) UpdateGoalProgress(league, team, g);
            int rank = regularSeasonRanks.ToList().IndexOf(team.TeamCode) + 1;
            int delta = 0;
            if (rank > 0 && rank <= 5) delta += 18;
            if (rank > 0 && rank < prevRank) delta += 8;
            if (rank == regularSeasonRanks.Count) delta -= 15;
            if (team.Budget < 0) delta -= 10;
            foreach (var g in fo.Goals.Where(g => g.TargetYear <= league.SeasonYear))
            {
                g.Met = g.OnTrack;
                if (g.Met) delta += 4;
                else if (g.Priority >= GMGoalPriority.High) delta -= 4;
            }
            if (championCode == team.TeamCode) delta += 15;
            fo.OwnerTrust = Math.Max(fo.Manager != null && fo.Manager.NoFiring ? NoFiringTrustFloor : 0, Math.Min(MaxTrust, fo.OwnerTrust + delta)); // [TASK-GM-07] 해임당하지 않음
            GMPressBriefing.EvaluateSeason(league, regularSeasonRanks); // [TASK-GM-15] 기록된 발언(순위 공언) 판정 - 실패 = 구단주 신임도 대폭 하락
            if (league.Mode == GMStartMode.StoryCampaign)
            {
                fo.Ending = EvaluateEnding(rank > 0 && rank <= 5, team.Budget, fo.OwnerTrust, rank > 0 && rank < prevRank, team.Roster.Average(p => p.Age));
                fo.EndingYear = league.SeasonYear;
                fo.EndingNote = EndingStory(fo.Ending);
                league.AddNews(new GMNewsItem
                {
                    GameIndex = league.GamesPlayed, DateLabel = $"12/31/{league.SeasonYear}", Kind = GMNewsKind.Season,
                    Title = $"「꼴찌 구단의 겨울」 {EndingLabel(fo.Ending)}", Body = fo.EndingNote, IsUserTeam = true, IsMajor = true,
                });
            }
        }

        /// <summary>새 해 스토브리그 - 연간 카운터 초기화 · 목표 재생성 · 안건 재오픈.</summary>
        public static void OnNewSeason(GMLeagueState league)
        {
            var fo = Ensure(league);
            fo.FASigningsThisYear = fo.TradesThisYear = fo.DraftPicksThisYear = fo.ExtraTradeAllowance = 0;
            fo.NegotiationCooldownIds.Clear(); // [TASK-GM-11] 결렬 쿨다운은 한 스토브리그 한정
            fo.NegotiationTalks.Clear();       // [TASK-GM-17] 협상 기회 · 양보 요구액도 한 스토브리그 한정
            foreach (var g in fo.Goals) g.Discussed = false;
            fo.Goals.RemoveAll(g => g.TargetYear < league.SeasonYear);
            if (league.UserTeam != null) league.UserTeam.AgendaTeamworkBonus /= 2;
            OpenAgendas(league);
            RefreshGoals(league);
            GMStoveTurns.Begin(league); // [TASK-GM-14] 새 스토브리그 - 8 Turn 진행 통제 시작(Turn 1 시즌 결산)
            GMDynamicEventEngine.Generate(league); // [TASK-GM-13] 새 스토브리그 - 1티어 동적 사건(연봉 갈등 · 라커룸 파벌)
        }

        // ================================================================== 유틸

        public static int Hash(string s)
        {
            int h = 23;
            foreach (char c in s ?? "") h = (h * 31 + c) & 0x7FFFFFFF;
            return h;
        }
    }

    /// <summary>[TASK-GM-06] 금액 표기(GMDiagnosticView.FormatWon과 같은 규칙 - Services에서 Controllers를 참조하지 않도록 분리).</summary>
    public static class GMDiagnosticFormat
    {
        /// <summary>만 원 단위 금액 → "15억 2,000만 원" / "3,000만 원" / 음수는 "-" 접두.</summary>
        public static string Won(long manwon)
        {
            if (manwon < 0) return "-" + Won(-manwon);
            long eok = manwon / 10000, rest = manwon % 10000;
            if (eok == 0) return $"{rest:N0}만 원";
            return rest == 0 ? $"{eok:N0}억 원" : $"{eok:N0}억 {rest:N0}만 원";
        }

        /// <summary>표 칸용 짧은 금액 - "15.2억" / "3,000만".</summary>
        public static string Short(long manwon)
        {
            if (manwon < 0) return "-" + Short(-manwon);
            return manwon >= 10000 ? $"{manwon / 10000.0:0.0}억" : $"{manwon:N0}만";
        }
    }
}
