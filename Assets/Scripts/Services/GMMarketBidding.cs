using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    public enum GMFaBidState { Open = 0, CounterBid = 1, Won = 2, Lost = 3, Withdrawn = 4 }

    /// <summary>[TASK-GM-17] FA 입찰 경쟁 세션 - 유저 제시 ↔ 포지션이 취약한 AI 구단의 역제안(Counter-bid)을 라운드별로 기록한다.</summary>
    public class GMFaBidSession
    {
        public Player Player;
        public int Demand, Years, UserSalary, UserEffective, RivalBid, RivalMax, Round;
        public bool RoleGuarantee;
        public string RivalCode, RivalNeed = "";
        public GMFaBidState State = GMFaBidState.Open;
        public readonly List<string> Log = new List<string>();
        public GMNegotiationResult Signing;
        public bool HasRival => !string.IsNullOrEmpty(RivalCode);
    }

    /// <summary>
    /// [TASK-GM-17] 시장 정보실 입찰 경쟁(Bidding War) - 스토브리그 3단계 FA 시장 연계.
    ///   - 경쟁 구단 = 그 선수를 원하는(GMFreeAgencyCycle.WantsPlayer) AI 구단 중 해당 포지션이 가장 취약한(니즈 적합 배수 최대) 구단. 시작 입찰 = AI 적정 입찰가.
    ///   - AI 한도(RivalMax) = 입찰가 × (1.10 + 니즈 여유 0~0.15), FA 가용 자금(계약금 기준) · 최고 연봉 이하.
    ///   - 유저 실효 제시 = 제시 연봉 × (1 + 기간 적합 · 보직 보장 +0.08 · 우승 열망 · 친정 +0.05) - OfferContract와 같은 가산.
    ///     실효 제시 ≥ AI 한도 → 영입 확정. 그 미만이고 AI 현재 입찰보다 높으면 AI가 실효 제시 × 1.05(한도 내)로 역제안 → 유저는 [추가 베팅] 또는 [포기].
    ///     AI가 이미 한도면 버틴다(유저가 한도를 넘어야 함). 관심 구단이 없으면 요구액 85% 이상 실효 제시로 바로 영입.
    ///   - [포기] = 경쟁 구단이 현재 입찰가로 영입(자리 · 자금이 되면), 아니면 선수는 시장에 남는다.
    /// </summary>
    public static class GMMarketBidding
    {
        public const float RaiseStep = 0.05f, CounterStep = 1.05f, BaseHeadroom = 1.10f, NoRivalRatio = 0.85f;

        public static GMFaBidSession Open(GMLeagueState league, GMTeamState team, Player p)
        {
            var s = new GMFaBidSession { Player = p };
            if (league == null || p?.Template == null) return s;
            var fo = GMFrontOffice.Ensure(league);
            s.Demand = GMStoveLeagueMarket.FADemand(p, fo.Difficulty);
            s.Years = Math.Min(GMStoveLeagueMarket.MaxFAYears, GMStoveLeagueMarket.PreferredYears(p));
            float bestFit = 0f;
            foreach (var t in league.Teams.Values.Where(t => !t.IsUserTeam).OrderBy(t => t.TeamCode, StringComparer.Ordinal))
            {
                var needs = GMTradeAI.AnalyzeNeeds(league, t);
                if (!GMFreeAgencyCycle.WantsPlayer(league, t, p, needs, out int bid)) continue;
                float fit = GMTradeAI.NeedFit(needs, p, out string note);
                if (fit < bestFit || (fit == bestFit && bid <= s.RivalBid)) continue;
                bestFit = fit;
                s.RivalCode = t.TeamCode;
                s.RivalBid = bid;
                s.RivalNeed = string.IsNullOrEmpty(note) ? "뎁스 보강" : note;
                long money = Math.Max(0, Math.Min(GMFrontOffice.Budget(league, t).MoneyForFA, t.Budget));
                int affordable = (int)Math.Min(Player.MaxSalary, money * 100 / Math.Max(1, s.Years * GMStoveLeagueMarket.FABonusPercent));
                float headroom = BaseHeadroom + Math.Max(0f, Math.Min(0.15f, (fit - 1f) * 0.5f)) + GMFrontOffice.RivalBidBonus(fo.Difficulty);
                s.RivalMax = Math.Max(bid, Math.Min(affordable, Round(bid * headroom)));
            }
            if (s.HasRival)
            {
                // 기존 경쟁 입찰 상한(GMFreeAgencyCycle.MaxRivalBid 135%) + 10%p - 요구액의 145%를 넘는 맞불은 없다
                int cap = Round(s.Demand * (GMFreeAgencyCycle.MaxRivalBid + 0.1f));
                s.RivalMax = Math.Min(s.RivalMax, cap);
                s.RivalBid = Math.Min(s.RivalBid, s.RivalMax);
            }
            if (s.HasRival) s.Log.Add($"{NameAliasTable.DisplayTeamName(s.RivalCode)} 관심 - {s.RivalNeed} · 시작 입찰 {GMDiagnosticFormat.Short(s.RivalBid)}");
            else s.Log.Add($"경쟁 구단 없음 - 요구액({GMDiagnosticFormat.Short(s.Demand)})의 {NoRivalRatio * 100:0}% 이상이면 영입");
            return s;
        }

        /// <summary>OfferContract와 같은 가산(기간 적합 · 보직 보장 · 우승 열망 · 친정)으로 계산한 실효 제시액.</summary>
        public static int Effective(GMLeagueState league, GMTeamState team, Player p, int salary, int years, bool role)
        {
            float f = 1f;
            int pref = Math.Min(GMStoveLeagueMarket.MaxFAYears, GMStoveLeagueMarket.PreferredYears(p));
            f += years >= pref ? 0.04f : -0.05f * (pref - years);
            if (role) f += 0.08f;
            double pct = league.RecordOf(team.TeamCode).G > 0 ? league.RecordOf(team.TeamCode).Pct : GMFrontOffice.LastPct(league, team.TeamCode);
            f += (float)((pct - 0.5) * 0.3);
            if (p.Template.CurrentTeam == team.Team || p.Template.Team == team.Team) f += 0.05f;
            return (int)Math.Round(salary * f);
        }

        /// <summary>유저 입찰 1라운드. 영입 확정 · 역제안 · 실패 중 하나로 상태를 바꾼다.</summary>
        public static GMFaBidSession Bid(GMLeagueState league, GMTeamState team, GMFaBidSession s, int salary, int years, bool role)
        {
            if (s?.Player == null || league == null || team == null) return s;
            if (s.State == GMFaBidState.Won || s.State == GMFaBidState.Lost || s.State == GMFaBidState.Withdrawn) return s;
            var p = s.Player;
            if (!league.FreeAgents.Contains(p)) { s.State = GMFaBidState.Lost; s.Log.Add("이미 시장을 떠난 선수입니다."); return s; }
            if (GMLeagueRules.FreeAgencyLocked(league, team, out var locked)) { s.Log.Add(locked); return s; } // [TASK-GM-18] 7/31 마감 · 예산 하드 락 = 입찰 불가
            s.Round++;
            s.UserSalary = Round(salary);
            s.Years = Math.Max(1, Math.Min(GMStoveLeagueMarket.MaxFAYears, years));
            s.RoleGuarantee = role;
            s.UserEffective = Effective(league, team, p, s.UserSalary, s.Years, role);
            bool commissioner = GMFrontOffice.Manager(league).Commissioner;
            int bar = s.HasRival ? s.RivalMax : (int)Math.Round(s.Demand * NoRivalRatio);
            s.Log.Add($"{s.Round}R 우리 제시 {GMDiagnosticFormat.Short(s.UserSalary)} × {s.Years}년(실효 {GMDiagnosticFormat.Short(s.UserEffective)})");
            if (commissioner || s.UserEffective >= bar)
            {
                s.Signing = GMStoveLeagueMarket.SignFreeAgent(league, team, p, s.Years, s.UserSalary, role);
                s.State = s.Signing.Success ? GMFaBidState.Won : GMFaBidState.Open;
                s.Log.Add(s.Signing.Success ? $"영입 확정 - {(s.HasRival ? $"{NameAliasTable.DisplayTeamName(s.RivalCode)} 한도({GMDiagnosticFormat.Short(s.RivalMax)}) 돌파" : "경쟁 없음")}" : s.Signing.Message);
                return s;
            }
            if (!s.HasRival)
            {
                s.State = GMFaBidState.Open;
                s.Log.Add($"선수 측 거절 - 요구액의 {NoRivalRatio * 100:0}%({GMDiagnosticFormat.Short(bar)}) 이상이 필요합니다.");
                return s;
            }
            if (s.UserEffective > s.RivalBid && s.RivalBid < s.RivalMax)
            {
                s.RivalBid = Math.Min(s.RivalMax, Round(s.UserEffective * CounterStep));
                s.Log.Add($"{NameAliasTable.DisplayTeamName(s.RivalCode)} 역제안 {GMDiagnosticFormat.Short(s.RivalBid)}");
            }
            else s.Log.Add($"{NameAliasTable.DisplayTeamName(s.RivalCode)} 입찰 {GMDiagnosticFormat.Short(s.RivalBid)} 유지");
            s.State = GMFaBidState.CounterBid;
            return s;
        }

        /// <summary>[추가 베팅] 다음 제시액 = 현재 AI 입찰을 넘는 액수(실효 기준 +5%).</summary>
        public static int SuggestedRaise(GMLeagueState league, GMTeamState team, GMFaBidSession s)
        {
            if (s?.Player == null) return 0;
            int eff = Math.Max(s.UserEffective, s.RivalBid);
            double factor = s.UserSalary > 0 && s.UserEffective > 0 ? s.UserEffective / (double)s.UserSalary : 1.0;
            return Round(eff * (1 + RaiseStep) / Math.Max(0.5, factor));
        }

        /// <summary>[포기] - 경쟁 구단이 현재 입찰가로 영입한다(자리 · 자금이 되면). 영입한 구단 코드(없으면 null).</summary>
        public static string Withdraw(GMLeagueState league, GMFaBidSession s)
        {
            if (s?.Player == null || league == null || s.State == GMFaBidState.Won) return null;
            s.State = GMFaBidState.Withdrawn;
            if (!s.HasRival || !league.FreeAgents.Contains(s.Player) || !league.Teams.TryGetValue(s.RivalCode, out var rival)) { s.Log.Add("입찰 포기 - 선수는 시장에 남습니다."); return null; }
            if (rival.Roster.Count >= GMStoveLeagueMarket.RosterMax) { s.Log.Add("입찰 포기 - 경쟁 구단 로스터가 가득 차 선수는 시장에 남습니다."); return null; }
            var p = s.Player;
            long bonus = (long)s.RivalBid * s.Years * GMStoveLeagueMarket.FABonusPercent / 100;
            league.FreeAgents.Remove(p);
            p.Salary = s.RivalBid;
            p.ContractYears = s.Years;
            p.IsCaptain = false;
            p.IsScouted = true;
            p.PersonalMorale = Math.Max(p.PersonalMorale, 75);
            rival.Budget -= bonus;
            rival.Roster.Add(p);
            var pending = GMFaCompensation.OnFreeAgentSigned(league, rival, p);
            if (pending != null) GMFaCompensation.Settle(league, pending);
            s.Log.Add($"입찰 포기 - {rival.DisplayName}이(가) {GMDiagnosticFormat.Short(s.RivalBid)} × {s.Years}년에 영입");
            league.AddNews(new GMNewsItem
            {
                GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Trade, IsUserTeam = true,
                Title = $"{rival.DisplayName}, 입찰 경쟁 끝에 FA {p.Template.PlayerName} 영입",
                Body = $"{s.RivalNeed} - {s.Years}년 · 연봉 {GMDiagnosticFormat.Won(s.RivalBid)}. 우리 구단은 {s.Round}라운드에서 입찰을 포기했다.",
            });
            return rival.TeamCode;
        }

        private static int Round(double v) => Math.Max(Player.MinSalary, Math.Min(Player.MaxSalary, (int)Math.Round(v / 100.0) * 100));
    }
}
