using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-09] 스토브리그 순환 완결(사전 조사 보고서 C-06):
    ///   ① 계약 만료자 FA 공시 - 시즌이 끝나 연도가 넘어갈 때(잔여 계약 -1 직후), 10구단 1군 + 퓨처스에서 ContractYears ≤ 0인 선수 전원을 FA 시장으로(원 소속 · 등급 기록).
    ///   ② AI 구단 FA 입찰 - 정규시즌 개막 직전, 28명이 안 되는 AI 구단이 FA 영입 가용 자금(Money for FA) 안에서 니즈(GMTradeAI 7종)에 맞는 FA를 영입한다.
    ///      모자라면 퓨처스에서 콜업한다. 영입으로 생긴 보상은 즉시 정산한다(내 구단이 보상 수령 측이면 AI 추천으로 대신 지명).
    ///   ③ 경쟁 입찰 - 유저가 FA에 계약을 제시하면, 그 선수가 필요한 AI 구단들이 적정 연봉(Base Expected Salary = OVR · 나이 기반 요구액) × (0.90~1.15 무작위 오차) × 니즈 배수로
    ///      맞불 입찰한다. 최고 입찰(요구액 대비 지수)을 넘어야 영입된다 - 헐값 영입 차단 · 거액 베팅 딜레마.
    /// </summary>
    public static class GMFreeAgencyCycle
    {
        public const int AiTargetRoster = 28;
        public const int MaxAiSigningsPerTeam = 4;
        public const float BidErrorMin = 0.90f, BidErrorRange = 0.25f;
        public const float NoInterestBid = 0.85f;   // 관심 구단이 없을 때 기본 시장 입찰 지수
        public const float MaxRivalBid = 1.35f;     // 경쟁 입찰 지수 상한(요구액의 135%)

        /// <summary>
        /// 시즌 종료 FA 공시 - 공시한 선수 목록. ContractYears는 "이번 시즌 포함 잔여 연수"라 연도 전환(-1) 직후 0년인 선수 = 지난 시즌으로 계약이 끝난 선수다.
        /// seasonYear = 끝난 시즌(소식 날짜용, 0이면 현재 연도).
        /// </summary>
        public static List<Player> DeclareExpiredContracts(GMLeagueState league, int seasonYear = 0)
        {
            var declared = new List<Player>();
            if (league == null) return declared;
            foreach (var team in league.Teams.Values)
                foreach (var p in team.ReservePlayers.Where(p => p.ContractYears <= 0).ToList())
                    if (GMFaCompensation.DeclareFreeAgent(league, team, p) != null) declared.Add(p);
            if (declared.Count > 0)
            {
                var grades = declared.GroupBy(p => GMFaCompensation.OriginOf(league, p)?.Grade ?? GMFaGrade.C).OrderBy(g => g.Key).Select(g => $"{GMFaCompensation.GradeLabel(g.Key)} {g.Count()}명");
                league.AddNews(new GMNewsItem
                {
                    GameIndex = league.GamesPlayed, DateLabel = $"11/05/{(seasonYear > 0 ? seasonYear : league.SeasonYear)}", Kind = GMNewsKind.Trade, IsMajor = true,
                    IsUserTeam = declared.Any(p => GMFaCompensation.OriginOf(league, p)?.TeamCode == league.SelectedTeamCode),
                    Title = $"{(seasonYear > 0 ? seasonYear : league.SeasonYear)} 시즌 종료 FA 공시 {declared.Count}명",
                    Body = $"계약이 끝난 선수 {declared.Count}명이 FA 시장에 나왔습니다({string.Join(" · ", grades)}). " +
                           $"내 구단 {declared.Count(p => GMFaCompensation.OriginOf(league, p)?.TeamCode == league.SelectedTeamCode)}명 포함.",
                });
            }
            return declared;
        }

        /// <summary>적정 연봉(Base Expected Salary) - OVR · 나이 · 수상 기반 요구액(KBO 정규 난이도 기준).</summary>
        public static int ExpectedSalary(Player p) => GMStoveLeagueMarket.ExtensionDemand(p, GMDifficulty.Majors);

        private static float Unit(string key) => (GMFrontOffice.Hash(key) % 1000) / 1000f;

        /// <summary>AI 구단의 입찰 연봉 = 적정 연봉 × (0.90~1.15 오차, 선수 · 구단 · 연도 해시) × 니즈 배수.</summary>
        public static int AiBidSalary(GMLeagueState league, GMTeamState team, Player p, IEnumerable<GMTeamNeed> needs = null)
        {
            if (league == null || team == null || p == null) return 0;
            float err = BidErrorMin + BidErrorRange * Unit($"{p.Template?.RealPlayerId ?? p.InstanceId}_{team.TeamCode}_{league.SeasonYear}_bid");
            float fit = GMTradeAI.NeedFit(needs ?? GMTradeAI.AnalyzeNeeds(league, team), p, out _);
            return (int)Math.Max(Player.MinSalary, Math.Min(Player.MaxSalary, Math.Round(ExpectedSalary(p) * err * fit / 100.0) * 100));
        }

        /// <summary>AI 구단이 그 FA를 원하는가 - 니즈 적합 · 28명 미만 · 그 포지션 최약 주전보다 OVR 3 이상 높음 중 하나 + 계약금을 낼 자금.</summary>
        public static bool WantsPlayer(GMLeagueState league, GMTeamState team, Player p, IList<GMTeamNeed> needs, out int bid)
        {
            bid = 0;
            if (team == null || team.IsUserTeam || p?.Template == null) return false;
            bool fit = GMTradeAI.NeedFit(needs, p, out _) > 1f;
            var samePos = team.Roster.Where(x => x.IsPitcher == p.IsPitcher && (p.IsPitcher || x.Position == p.Position)).ToList();
            bool upgrade = samePos.Count == 0 || p.BaseOverall >= samePos.Min(x => x.BaseOverall) + 3;
            if (!fit && team.Roster.Count >= AiTargetRoster && !upgrade) return false;
            bid = AiBidSalary(league, team, p, needs);
            long bonus = (long)bid * GMStoveLeagueMarket.PreferredYears(p) * GMStoveLeagueMarket.FABonusPercent / 100;
            var budget = GMFrontOffice.Budget(league, team);
            return bonus <= budget.MoneyForFA && bonus <= team.Budget;
        }

        /// <summary>
        /// 유저 FA 계약 제시에 맞선 AI 경쟁 입찰 - 관심 구단 중 최고 입찰 지수(입찰 연봉 / 요구액 + 난이도 가산, 상한 1.35). 관심 구단이 없으면 0.85(시장 기본).
        /// </summary>
        public static float BestRivalBid(GMLeagueState league, Player p, out string bidderCode)
        {
            bidderCode = null;
            if (league == null || p == null) return NoInterestBid;
            var fo = GMFrontOffice.Ensure(league);
            int demand = Math.Max(1, GMStoveLeagueMarket.FADemand(p, fo.Difficulty));
            float best = NoInterestBid;
            foreach (var team in league.Teams.Values.Where(t => !t.IsUserTeam))
            {
                var needs = GMTradeAI.AnalyzeNeeds(league, team);
                if (!WantsPlayer(league, team, p, needs, out int bid)) continue;
                float ratio = bid / (float)demand;
                if (ratio > best) { best = ratio; bidderCode = team.TeamCode; }
            }
            return Math.Min(MaxRivalBid, best) + GMFrontOffice.RivalBidBonus(fo.Difficulty);
        }

        /// <summary>AI 구단 FA 영입 1건 기록.</summary>
        public sealed class AiSigning
        {
            public string TeamCode;
            public Player Player;
            public int Salary, Years;
            public long Bonus, MoneyForFABefore;
            public string NeedNote = "";
            public GMCompensationResult Compensation;
        }

        /// <summary>
        /// AI 구단 FA 입찰 라운드 - 성적 역순(직전 시즌 승률 낮은 구단 우선)으로 1명씩 돌아가며 28명이 될 때까지(구단당 최대 4명).
        /// 후보 = 원하는(WantsPlayer) FA 중 니즈 적합 → OVR 순. 계약금(연봉 × 기간 × 25%)은 FA 영입 가용 자금 · 운영 자금 안에서만.
        /// 영입하면 보상을 즉시 정산하고, 그래도 28명이 안 되면 퓨처스에서 콜업한다.
        /// </summary>
        public static List<AiSigning> RunAiFreeAgency(GMLeagueState league)
        {
            var log = new List<AiSigning>();
            if (league == null) return log;
            var order = league.Teams.Values.Where(t => !t.IsUserTeam).OrderBy(t => GMFrontOffice.LastPct(league, t.TeamCode)).ThenBy(t => t.TeamCode).ToList();
            var count = order.ToDictionary(t => t.TeamCode, t => 0);
            for (int round = 0; round < MaxAiSigningsPerTeam; round++)
            {
                bool any = false;
                foreach (var team in order)
                {
                    if (team.Roster.Count >= AiTargetRoster || count[team.TeamCode] >= MaxAiSigningsPerTeam) continue;
                    var needs = GMTradeAI.AnalyzeNeeds(league, team);
                    Player pick = null; int bid = 0; string note = "";
                    foreach (var fa in league.FreeAgents.OrderByDescending(f => GMTradeAI.NeedFit(needs, f, out _)).ThenByDescending(f => f.BaseOverall).ThenBy(f => f.InstanceId).ToList())
                    {
                        if (!WantsPlayer(league, team, fa, needs, out int b)) continue;
                        pick = fa; bid = b; GMTradeAI.NeedFit(needs, fa, out note);
                        break;
                    }
                    if (pick == null) continue;
                    var money = GMFrontOffice.Budget(league, team).MoneyForFA;
                    int years = Math.Min(GMStoveLeagueMarket.MaxFAYears, GMStoveLeagueMarket.PreferredYears(pick));
                    long bonus = (long)bid * years * GMStoveLeagueMarket.FABonusPercent / 100;
                    league.FreeAgents.Remove(pick);
                    pick.Salary = bid;
                    pick.ContractYears = years;
                    pick.IsCaptain = false;
                    pick.IsScouted = true;
                    pick.PersonalMorale = Math.Max(pick.PersonalMorale, 75);
                    team.Budget -= bonus;
                    team.Roster.Add(pick);
                    var pending = GMFaCompensation.OnFreeAgentSigned(league, team, pick);
                    var entry = new AiSigning { TeamCode = team.TeamCode, Player = pick, Salary = bid, Years = years, Bonus = bonus, MoneyForFABefore = money, NeedNote = string.IsNullOrEmpty(note) ? "뎁스 보강" : note };
                    if (pending != null) entry.Compensation = GMFaCompensation.Settle(league, pending);
                    log.Add(entry);
                    count[team.TeamCode]++;
                    any = true;
                    league.AddNews(new GMNewsItem
                    {
                        GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Trade,
                        IsUserTeam = entry.Compensation != null && pending != null && pending.FromTeam == league.SelectedTeamCode,
                        Title = $"{team.DisplayName} FA {pick.Template.PlayerName} 영입",
                        Body = $"{entry.NeedNote} - {years}년 · 연봉 {GMDiagnosticFormat.Won(bid)} · 계약금 {GMDiagnosticFormat.Won(bonus)}" + (entry.Compensation != null ? $" / {entry.Compensation.Message}" : ""),
                    });
                }
                if (!any) break;
            }
            // 28명 보장 - 퓨처스 콜업(잠재력 순)
            foreach (var team in league.Teams.Values.Where(t => !t.IsUserTeam))
                while (team.Roster.Count < AiTargetRoster && team.Futures.Count > 0)
                    GMRosterTiers.CallUp(league, team, team.Futures.OrderByDescending(p => p.BaseOverall).ThenByDescending(p => p.Potential).First(), out _);
            return log;
        }

        /// <summary>
        /// 정규시즌 개막 직전 1회(시뮬레이터 StartRun) - AI FA 입찰 + 남은 보상 정산(내 구단 관련 포함) + 내 구단 28명 미만이면 퓨처스 자동 콜업.
        /// </summary>
        public static List<AiSigning> PrepareOpeningDay(GMLeagueState league)
        {
            var log = RunAiFreeAgency(league);
            foreach (var pending in league.PendingCompensations.ToList()) GMFaCompensation.Settle(league, pending);
            var user = league.UserTeam;
            if (user != null)
                while (user.Roster.Count < AiTargetRoster && user.Futures.Count > 0)
                    GMRosterTiers.CallUp(league, user, user.Futures.OrderByDescending(p => p.BaseOverall).First(), out _);
            return log;
        }
    }
}
