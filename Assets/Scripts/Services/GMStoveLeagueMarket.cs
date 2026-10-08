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
    /// [TASK-GM-06] 스토브리그 5대 협상(순수 로직 - UI는 GMOotpFrontOfficeUIController 서브 탭).
    ///   ① 연봉 · 재계약(1~5년 × 연봉 + 보직 양보 인센티브) · 주장 임명 · 방출
    ///   ② FA 영입(스카우팅 리포트 · 1~4년 × 제시 연봉 × 보직 보장 → 충성도 · 우승 열망 · 난이도 · 경쟁 구단 최고 입찰가 비교)
    ///   ③ 트레이드(1:1 / 2:2 직접 협상 · 트레이드 가치 바 · 구단 니즈) + Shop a Player(9개 구단 교환 후보 · 정렬 · 원클릭 체결)
    ///   ④ 신인 드래프트(유망주 10명 · 연 2명 지명) + 백분위 랭킹(1~99%)
    /// 로스터 상한은 28인(타자 · 투수 구분 없이 합계)이다. 금액 단위는 만 원.
    /// </summary>
    public static class GMStoveLeagueMarket
    {
        public const int RosterMax = GMRosterTiers.FirstTeamMax; // [TASK-GM-08] 1군 29명(출장 27명) - 구 28인
        public const int MinRosterAfterRelease = 20;
        public const int FAMarketShown = 15;
        public const int DraftPoolSize = 10;
        public const int MaxDraftPicksPerYear = 2;
        public const int ScoutCost = 500;           // 스카우팅 리포트 1건 500만 원
        public const int ExtensionBonusPercent = 10; // 재계약 계약금 = 총액의 10%
        public const int FABonusPercent = 25;        // FA 계약금 = 총액의 25%
        public const int ReleaseBuyoutPercent = 50;  // 방출 위약금 = 잔여 연봉의 50%
        public const int MaxFAYears = 4;
        public const float HardTradeExtraMargin = 0.1f; // [TASK-GM-07] 거래 하드 모드 - AI 요구 가치 +10%

        // ================================================================== 공통

        public static int PreferredYears(Player p) => p.Age <= 26 ? 4 : p.Age <= 30 ? 3 : p.Age <= 33 ? 2 : 1;

        private static int RoundSalary(double v) => Math.Max(Player.MinSalary, Math.Min(Player.MaxSalary, (int)Math.Round(v / 100.0) * 100));

        /// <summary>재계약 · 연장 요구 연봉(성적 기반 기준 연봉 × 나이 보정 × 난이도 배수, 현재 연봉의 90% 이상).</summary>
        public static int ExtensionDemand(Player p, GMDifficulty difficulty)
        {
            if (p?.Template == null) return Player.MinSalary;
            int majors = p.CareerAwardIds?.Count(Player.IsMajorAward) ?? 0;
            double baseSalary = Player.ComputeSalary(p.BaseOverall, p.EgoLevel, majors);
            double age = p.Age >= 35 ? 0.75 : p.Age >= 32 ? 0.9 : p.Age <= 24 ? 0.85 : 1.0;
            double v = Math.Max(baseSalary * age, p.Salary * 0.9) * GMFrontOffice.DemandMultiplier(difficulty);
            return RoundSalary(v);
        }

        /// <summary>FA 요구 연봉(재계약 요구액 × 1.1 - 시장 프리미엄).</summary>
        public static int FADemand(Player p, GMDifficulty difficulty) => RoundSalary(ExtensionDemand(p, difficulty) * 1.1);

        public static string DemandLabel(Player p, GMDifficulty d, bool freeAgent) =>
            $"{GMDiagnosticFormat.Short(freeAgent ? FADemand(p, d) : ExtensionDemand(p, d))} × {PreferredYears(p)}년";

        private static int Teamwork(GMTeamState team) => TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff).TeamworkScore;

        private static void News(GMLeagueState league, GMNewsKind kind, string title, string body) =>
            league.AddNews(new GMNewsItem { GameIndex = league.GamesPlayed, DateLabel = league.GamesPlayed > 0 ? GMLiveSeasonSimulator.DateLabel(Math.Max(0, league.GamesPlayed - 1), league.SeasonYear) : $"{league.SeasonYear} 스토브리그", Kind = kind, Title = title, Body = body, IsUserTeam = true });

        // ================================================================== ① 재계약 · 주장 · 방출

        public static bool IsExtensionTarget(Player p) => p != null && p.ContractYears <= 1;

        /// <summary>
        /// 재계약/연장 협상 - years(1~5) × salary. 수락 기준 = 요구액 × (0.95, 보직 양보 인센티브 시 0.9, Ego 5 +0.05, 선호 기간보다 2년 이상 짧으면 +0.05).
        /// 성공 시 연봉 · 계약연수 · 만족도(+8, 인센티브 +12) 즉시 갱신, 계약금(총액 10%)을 예산에서 차감, 팀워크 전후 값을 돌려준다.
        /// </summary>
        public static GMNegotiationResult Extend(GMLeagueState league, GMTeamState team, Player p, int years, int salary, bool roleConcession)
        {
            var r = new GMNegotiationResult();
            if (team == null || p == null || !team.Roster.Contains(p)) { r.Message = "우리 구단 선수를 선택하십시오."; return r; }
            years = Math.Max(1, Math.Min(Player.MaxContractYears, years));
            salary = RoundSalary(salary);
            var diff = GMFrontOffice.Ensure(league).Difficulty;
            r.Demand = ExtensionDemand(p, diff);
            r.TeamworkBefore = Teamwork(team);
            float factor = roleConcession ? 0.9f : 0.95f;
            if (p.EgoLevel >= 5) factor += 0.05f;
            if (years <= PreferredYears(p) - 2) factor += 0.05f;
            r.Score = salary / (float)r.Demand;
            r.RivalBid = factor;
            long bonus = (long)salary * years * ExtensionBonusPercent / 100;
            if (r.Score < factor)
            {
                p.PersonalMorale = Math.Max(0, p.PersonalMorale - 3);
                r.TeamworkAfter = Teamwork(team);
                r.Message = $"{p.Template.PlayerName} 측 거절 - 요구 {GMDiagnosticFormat.Won(r.Demand)} × {PreferredYears(p)}년, 제시액이 {factor * 100:0}% 미만입니다. (만족도 -3)";
                return r;
            }
            if (team.Budget < bonus) { r.TeamworkAfter = r.TeamworkBefore; r.Message = $"운영 자금 부족 - 계약금 {GMDiagnosticFormat.Won(bonus)}이 필요합니다."; return r; }
            team.Budget -= bonus;
            int oldSalary = p.Salary;
            p.Salary = salary;
            p.ContractYears = years;
            p.PersonalMorale = Math.Min(100, p.PersonalMorale + (roleConcession ? 12 : 8));
            if (roleConcession) p.HasRoleConcessionBonus = true;
            r.Success = true;
            r.TeamworkAfter = Teamwork(team);
            var chain = team.IsUserTeam ? GMSalaryChain.Apply(league, team, p, oldSalary, salary, null) : new List<string>(); // [TASK-GM-14] 동료 연봉 연쇄
            r.Message = $"{p.Template.PlayerName} {years}년 · 연봉 {GMDiagnosticFormat.Won(salary)} 재계약{(roleConcession ? " + 보직 양보 인센티브" : "")} · 계약금 {GMDiagnosticFormat.Won(bonus)} · 팀워크 {r.TeamworkBefore} → {r.TeamworkAfter}" + GMSalaryChain.Summary(chain);
            News(league, GMNewsKind.Trade, $"{p.Template.PlayerName} 재계약 합의", r.Message);
            return r;
        }

        /// <summary>[주장(Captain) 임명] - 기존 주장 해제 후 임명, 만족도 +10. 팀워크 전후 값을 돌려준다.</summary>
        public static GMNegotiationResult AppointCaptain(GMLeagueState league, GMTeamState team, Player p)
        {
            var r = new GMNegotiationResult();
            if (team == null || p == null || !team.Roster.Contains(p)) { r.Message = "우리 구단 선수를 선택하십시오."; return r; }
            r.TeamworkBefore = Teamwork(team);
            foreach (var x in team.Roster) x.IsCaptain = false;
            p.IsCaptain = true;
            p.PersonalMorale = Math.Min(100, p.PersonalMorale + 10);
            r.TeamworkAfter = Teamwork(team);
            r.Success = true;
            r.Message = $"{p.Template.PlayerName} 주장 임명 · 만족도 +10 · 팀워크 {r.TeamworkBefore} → {r.TeamworkAfter}";
            News(league, GMNewsKind.Trade, $"{p.Template.PlayerName} 새 주장", r.Message);
            return r;
        }

        /// <summary>[방출(Release)] - 잔여 연봉의 50%(최소 1년분)를 위약금으로 내고 FA 시장으로 보낸다. 로스터 20인 미만이 되면 막는다.</summary>
        public static GMNegotiationResult Release(GMLeagueState league, GMTeamState team, Player p)
        {
            var r = new GMNegotiationResult();
            if (team == null || p == null || !team.Roster.Contains(p)) { r.Message = "우리 구단 선수를 선택하십시오."; return r; }
            if (team.Roster.Count <= MinRosterAfterRelease) { r.Message = $"로스터 최소 {MinRosterAfterRelease}인 - 더 방출할 수 없습니다."; return r; }
            long buyout = (long)p.Salary * Math.Max(1, p.ContractYears) * ReleaseBuyoutPercent / 100;
            r.TeamworkBefore = Teamwork(team);
            RemoveFromTeam(team, p);
            team.Budget -= buyout;
            p.ContractYears = 0;
            p.IsCaptain = false;
            p.HasRoleConcessionBonus = false;
            league.FreeAgents.Insert(0, p);
            league.FAOrigins.Remove(p.InstanceId); // [TASK-GM-08] 방출 = 자유계약(보상 없음)
            r.TeamworkAfter = Teamwork(team);
            r.Success = true;
            r.Message = $"{p.Template.PlayerName} 방출 - 위약금 {GMDiagnosticFormat.Won(buyout)} · 로스터 {team.Roster.Count}/{RosterMax}인";
            News(league, GMNewsKind.Trade, $"{p.Template.PlayerName} 방출", r.Message);
            return r;
        }

        private static void RemoveFromTeam(GMTeamState team, Player p)
        {
            if (!team.Roster.Remove(p)) { team.Futures.Remove(p); return; } // [TASK-GM-08] 퓨처스 선수(트레이드 · 보상)
            string id = p.InstanceId;
            team.Lineup.Starters.RemoveAll(x => x.InstanceId == id);
            team.Lineup.Roles.RemoveAll(x => x.InstanceId == id);
            team.Lineup.PitcherSlots.RemoveAll(x => x.InstanceId == id);
            GMCheerleaderRoster.RefreshDedications(team);
        }

        private static void AddToTeam(GMTeamState team, Player p)
        {
            p.IsCaptain = false;
            team.Roster.Add(p);
        }

        public static List<Player> SortRoster(IEnumerable<Player> roster, string key, bool descending)
        {
            Func<Player, double> f;
            switch (key)
            {
                case "OVR": f = p => p.BaseOverall; break;
                case "POT": f = p => p.Potential; break;
                case "SAL": f = p => p.Salary; break;
                case "YRS": f = p => p.ContractYears; break;
                case "AGE": f = p => p.Age; break;
                case "ABS": f = p => p.ABSZoneSkill; break;
                default: f = p => PositionOrder(p); break;
            }
            var list = (roster ?? Enumerable.Empty<Player>()).ToList();
            return (descending ? list.OrderByDescending(f) : list.OrderBy(f)).ThenBy(p => p.Template?.PlayerName).ToList();
        }

        public static int PositionOrder(Player p)
        {
            if (p?.Template == null) return 99;
            if (p.IsPitcher) return 20 + (int)p.Template.PitcherRole;
            return (int)p.Template.BatterPosition;
        }

        // ================================================================== ② FA

        /// <summary>FA 시장 매물(OVR 상위 15명).</summary>
        public static List<Player> FAMarket(GMLeagueState league) => league.FreeAgents.OrderByDescending(p => p.BaseOverall).Take(FAMarketShown).ToList();

        /// <summary>[스카우팅 리포트 열람] - 500만 원으로 정확한 OVR · 잠재력 · 숨은 성향 · ABS 적응도 공개.</summary>
        public static bool Scout(GMLeagueState league, GMTeamState team, Player p, out string message)
        {
            message = "";
            if (p == null || team == null) { message = "선수를 선택하십시오."; return false; }
            if (p.IsScouted) { message = $"{p.Template.PlayerName} 리포트는 이미 열람했습니다."; return true; }
            if (team.Budget < ScoutCost) { message = "스카우팅 예산이 부족합니다."; return false; }
            team.Budget -= ScoutCost;
            GMFrontOffice.Ensure(league).ScoutingSpent += ScoutCost;
            p.IsScouted = true;
            message = $"{p.Template.PlayerName} 스카우팅 리포트 - {ScoutReport(p)} (-{ScoutCost:N0}만 원)";
            return true;
        }

        /// <summary>스카우팅 전에는 OVR · 잠재력을 ±범위로, 성향 · ABS 적응도는 "?"로 보여 준다.</summary>
        public static string OvrLabel(Player p)
        {
            if (p.IsScouted) return $"{p.BaseOverall}/{p.Potential}";
            int fuzz = 3 + GMFrontOffice.Hash(p.InstanceId) % 3;
            return $"{p.BaseOverall - fuzz}~{p.BaseOverall + fuzz}/?";
        }

        public static string ScoutReport(Player p) =>
            $"OVR {p.BaseOverall} · 잠재력 {p.Potential} · 성향 {RoleLabel(p.RoleArchetype)}(Ego {p.EgoLevel}) · ABS {p.ABSZoneSkill}({p.AbsRoleLabel}, 숨은 적응도 {p.HiddenAbsAdaptation:+0;-0;0})";

        public static string RoleLabel(LockerRoomRole r)
        {
            switch (r)
            {
                case LockerRoomRole.AlphaDog: return "알파독";
                case LockerRoomRole.Ambitious: return "야망가";
                case LockerRoomRole.DugoutLeader: return "더그아웃 리더";
                case LockerRoomRole.Prospect: return "유망주";
                default: return "살림꾼";
            }
        }

        /// <summary>
        /// 경쟁 구단 최고 입찰가(요구액 대비 비율) + 난이도 가산. [TASK-GM-09] 해시 고정값(0.88~1.08) → 실제 AI 구단 입찰(GMFreeAgencyCycle.BestRivalBid):
        /// 그 선수가 필요한 AI 구단이 적정 연봉 × (0.90~1.15) × 니즈 배수로 맞불 입찰, 관심 구단이 없으면 0.85.
        /// </summary>
        public static float RivalBid(GMLeagueState league, Player p) => GMFreeAgencyCycle.BestRivalBid(league, p, out _);

        /// <summary>
        /// [계약 제시(Offer Contract)] - 점수 = 제시액/요구액 + 기간 적합(선호 기간 ±) + 보직 보장(+0.08) + 우승 열망(내 구단 전력 · 승률) + 충성도(친정 구단 +0.05).
        /// 점수가 경쟁 구단 최고 입찰가 이상이면 영입: 로스터 합류 · 페이롤 증가 · 계약금(총액 25%) 예산 차감 · 하우스 룰 카운트.
        /// </summary>
        public static GMNegotiationResult OfferContract(GMLeagueState league, GMTeamState team, Player p, int years, int salary, bool roleGuarantee)
        {
            var r = new GMNegotiationResult();
            if (team == null || p == null || !league.FreeAgents.Contains(p)) { r.Message = "FA 시장의 선수를 선택하십시오."; return r; }
            if (!GMFrontOffice.CanSignFreeAgent(league, out var rule)) { r.Message = rule; return r; }
            if (team.Roster.Count >= RosterMax) { r.Message = $"로스터 {RosterMax}인 가득 - 먼저 [방출]로 자리를 비우십시오."; return r; }
            years = Math.Max(1, Math.Min(MaxFAYears, years));
            salary = RoundSalary(salary);
            var fo = GMFrontOffice.Ensure(league);
            r.Demand = FADemand(p, fo.Difficulty);
            float score = salary / (float)r.Demand;
            int pref = Math.Min(MaxFAYears, PreferredYears(p));
            score += years >= pref ? 0.04f : -0.05f * (pref - years);
            if (roleGuarantee) score += 0.08f;
            double pct = league.RecordOf(team.TeamCode).G > 0 ? league.RecordOf(team.TeamCode).Pct : GMFrontOffice.LastPct(league, team.TeamCode);
            score += (float)((pct - 0.5) * 0.3);                                                  // 우승 열망
            if (p.Template.CurrentTeam == team.Team || p.Template.Team == team.Team) score += 0.05f; // 충성도(친정)
            r.Score = score;
            r.RivalBid = GMFreeAgencyCycle.BestRivalBid(league, p, out var bidder);
            long bonus = (long)salary * years * FABonusPercent / 100;
            if (score < r.RivalBid && !GMFrontOffice.Manager(league).Commissioner) // [TASK-GM-07] 커미셔너 모드 - 경쟁 입찰 판정 건너뜀
            {
                r.Message = $"{p.Template.PlayerName} 영입 실패 - 경쟁 구단{(bidder != null ? $" {NameAliasTable.DisplayTeamName(bidder)}" : "")} 최고 입찰(지수 {r.RivalBid:0.00})이 우리 제안(지수 {score:0.00})보다 좋습니다. 요구 {GMDiagnosticFormat.Won(r.Demand)} × {pref}년";
                return r;
            }
            if (team.Budget < bonus) { r.Message = $"운영 자금 부족 - 계약금 {GMDiagnosticFormat.Won(bonus)}이 필요합니다."; return r; }
            r.TeamworkBefore = Teamwork(team);
            league.FreeAgents.Remove(p);
            team.Budget -= bonus;
            p.Salary = salary;
            p.ContractYears = years;
            p.IsScouted = true;
            p.PersonalMorale = Math.Min(100, 75 + (roleGuarantee ? 10 : 0));
            p.HasRoleConcessionBonus = roleGuarantee;
            AddToTeam(team, p);
            fo.FASigningsThisYear++;
            var pending = GMFaCompensation.OnFreeAgentSigned(league, team, p); // [TASK-GM-08] 원 소속 보상(정산 대기) · FA 계약 당사자 자동 보호
            r.TeamworkAfter = Teamwork(team);
            r.Success = true;
            r.Message = $"{p.Template.PlayerName} FA 영입! {years}년 · 연봉 {GMDiagnosticFormat.Won(salary)}{(roleGuarantee ? " · 보직 보장" : "")} · 계약금 {GMDiagnosticFormat.Won(bonus)} · 팀워크 {r.TeamworkBefore} → {r.TeamworkAfter}" +
                        (pending != null ? $" · 원 소속 {KBOManager.Data.NameAliasTable.DisplayTeamName(pending.FromTeam)} {GMFaCompensation.GradeLabel(pending.Grade)} 보상 정산 대기" : "");
            News(league, GMNewsKind.Trade, $"FA {p.Template.PlayerName} 영입", r.Message);
            return r;
        }

        // ================================================================== ③ 트레이드

        /// <summary>트레이드 가치 - (OVR-45)^1.6 × 나이 보정 + 잠재력 여유 - 고연봉 부담. 최소 1.</summary>
        public static float TradeValue(Player p)
        {
            if (p?.Template == null) return 0f;
            double v = Math.Pow(Math.Max(1, p.BaseOverall - 45), 1.6);
            double age = p.Age <= 25 ? 1.2 : p.Age <= 29 ? 1.0 : p.Age <= 32 ? 0.85 : 0.65;
            v = v * age + Math.Max(0, p.Potential - p.BaseOverall) * 2.0 - p.Salary / 5000.0 + p.FameBonus * 0.5; // [TASK-GM-09] 팬덤 가치
            return (float)Math.Max(1.0, v);
        }

        /// <summary>상대 구단 니즈 - 그 포지션 주전보다 OVR이 높으면 +15%, 투수진이 약하면 투수 +10%.</summary>
        public static float NeedMultiplier(GMTeamState partner, Player incoming, out string note)
        {
            note = "";
            if (partner == null || incoming == null) return 1f;
            if (incoming.IsPitcher)
            {
                double teamAvg = partner.Pitchers.Select(x => x.BaseOverall).DefaultIfEmpty(60).Average();
                if (incoming.BaseOverall > teamAvg + 3) { note = $"{partner.DisplayName} 투수진 보강 니즈(+10%)"; return 1.1f; }
                return 1f;
            }
            var bestAtPos = partner.Roster.Where(x => !x.IsPitcher && x.Position == incoming.Position).Select(x => x.BaseOverall).DefaultIfEmpty(0).Max();
            if (incoming.BaseOverall > bestAtPos) { note = $"{partner.DisplayName} {GMFrontOffice.PositionLabel(incoming.Position)} 주전 보강 니즈(+15%)"; return 1.15f; }
            return 1f;
        }

        /// <summary>[TASK-GM-08] 상대 단장 요구 가치 배수 = 난이도 배수 + 거래 하드 모드(+10%).</summary>
        public static float RequiredMargin(GMLeagueState league) =>
            GMFrontOffice.TradeMargin(GMFrontOffice.Ensure(league).Difficulty) + (GMFrontOffice.Manager(league).HardTrade ? HardTradeExtraMargin : 0f);

        public const int MaxTradeSide = 3; // [TASK-GM-08] 1:N · 최대 3:3

        /// <summary>
        /// 직접 트레이드 평가 - 상대 단장 기준(받는 가치 × 니즈 + 연봉 보조 ≥ 주는 가치 × 난이도 배수면 수락).
        /// [TASK-GM-08] 1:1 / 2:2 → 1:N(양쪽 1~3명, 인원 불일치 허용 - 1군 29명을 넘으면 퓨처스로 정리) · 퓨처스 선수 포함 · AI 니즈 적합도 · 연봉 보조(만 원).
        /// </summary>
        public static GMTradeEvaluation Evaluate(GMLeagueState league, GMTeamState mine, IList<Player> myOut, GMTeamState partner, IList<Player> theirIn, int cashSubsidy = 0)
        {
            var e = new GMTradeEvaluation();
            myOut = myOut ?? new List<Player>();
            theirIn = theirIn ?? new List<Player>();
            if (mine == null || partner == null || mine == partner) { e.Reason = "상대 구단을 고르십시오."; return e; }
            if (myOut.Count == 0 || theirIn.Count == 0) { e.Reason = "양쪽에 1명 이상 올리십시오."; return e; }
            if (myOut.Count > MaxTradeSide || theirIn.Count > MaxTradeSide) { e.Reason = $"트레이드는 한쪽 최대 {MaxTradeSide}명(1:N)까지입니다."; return e; }
            if (!myOut.All(mine.ReservePlayers.Contains) || !theirIn.All(partner.ReservePlayers.Contains)) { e.Reason = "소속이 맞지 않는 선수가 있습니다."; return e; }
            if (mine.Roster.Count - myOut.Count(mine.Roster.Contains) + theirIn.Count < MinRosterAfterRelease) { e.Reason = $"트레이드 후 1군이 {MinRosterAfterRelease}명 미만이 됩니다."; return e; }
            cashSubsidy = Math.Max(0, Math.Min(GMTradeAI.MaxCashSubsidy, cashSubsidy));
            if (cashSubsidy > mine.Budget) { e.Reason = "연봉 보조를 낼 운영 자금이 부족합니다."; return e; }
            var notes = new List<string>();
            var needs = GMTradeAI.AnalyzeNeeds(league, partner);
            foreach (var p in myOut)
            {
                float need = NeedMultiplier(partner, p, out var note);
                float fit = GMTradeAI.NeedFit(needs, p, out var fitNote);
                if (fit > need) { need = fit; note = $"{partner.DisplayName} {fitNote} 니즈(+{(fit - 1f) * 100:0}%)"; }
                if (!string.IsNullOrEmpty(note)) notes.Add(note);
                e.ReceiveValue += TradeValue(p) * need;
            }
            e.CashSubsidy = cashSubsidy;
            e.CashValue = GMTradeAI.CashValue(cashSubsidy);
            e.ReceiveValue += e.CashValue;
            e.GiveValue = theirIn.Sum(TradeValue);
            var manager = GMFrontOffice.Manager(league);
            e.Required = e.GiveValue * RequiredMargin(league); // [TASK-GM-07] 거래 하드 모드
            e.NeedsNote = notes.Count > 0 ? string.Join(" · ", notes.Distinct()) : $"{partner.DisplayName} 니즈: {GMTradeAI.NeedsLabel(needs)}";
            e.Acceptable = e.ReceiveValue >= e.Required || manager.Commissioner; // [TASK-GM-07] 커미셔너 모드 - 가치 판정 건너뜀
            e.Reason = e.Acceptable ? $"{partner.DisplayName} 단장: \"좋습니다, 받아들이죠.\"" : $"{partner.DisplayName} 단장: \"가치가 부족합니다({e.Ratio * 100:0}%).\"";
            return e;
        }

        /// <summary>트레이드 실행(평가 통과 시) - 로스터 교환 · 주장/라인업 핀 · 전담 응원 정리 · 하우스 룰 카운트 · 소식.</summary>
        public static GMNegotiationResult ExecuteTrade(GMLeagueState league, GMTeamState mine, IList<Player> myOut, GMTeamState partner, IList<Player> theirIn, int cashSubsidy = 0)
        {
            var r = new GMNegotiationResult();
            if (!GMFrontOffice.CanTrade(league, out var rule)) { r.Message = rule; return r; }
            var e = Evaluate(league, mine, myOut, partner, theirIn, cashSubsidy);
            if (!e.Acceptable) { r.Message = e.Reason; return r; }
            r.TeamworkBefore = Teamwork(mine);
            var outs = myOut.ToList();
            var ins = theirIn.ToList();
            foreach (var p in outs) { RemoveFromTeam(mine, p); AddToTeam(partner, p); }
            foreach (var p in ins) { RemoveFromTeam(partner, p); AddToTeam(mine, p); }
            // [TASK-GM-08] 연봉 보조 이전 · 1군 29명 초과분 퓨처스 정리
            mine.Budget -= e.CashSubsidy;
            partner.Budget += e.CashSubsidy;
            var moved = GMRosterTiers.EnforceLimits(league, mine).Concat(GMRosterTiers.EnforceLimits(league, partner)).ToList();
            if (outs.Any(p => p.Template.RealPlayerId == mine.TradeRequestPlayerId)) mine.TradeRequestPlayerId = null;
            GMFrontOffice.Ensure(league).TradesThisYear++;
            r.TeamworkAfter = Teamwork(mine);
            r.Success = true;
            string give = string.Join(" · ", outs.Select(p => p.Template.PlayerName));
            string get = string.Join(" · ", ins.Select(p => p.Template.PlayerName));
            r.Message = $"트레이드 성사({outs.Count}:{ins.Count}) - {give} ↔ {partner.DisplayName} {get}" +
                        (e.CashSubsidy > 0 ? $" · 연봉 보조 {GMDiagnosticFormat.Won(e.CashSubsidy)}" : "") +
                        (moved.Count > 0 ? $" · 로스터 정리 {string.Join(", ", moved)}" : "") + $" · 팀워크 {r.TeamworkBefore} → {r.TeamworkAfter}";
            News(league, GMNewsKind.Trade, $"{mine.DisplayName} ↔ {partner.DisplayName} 트레이드", r.Message);
            return r;
        }

        /// <summary>
        /// [Shop a Player] - 내 선수 1명을 매물로 올리면 9개 구단이 각자 1:1로 내줄 수 있는 최선의 선수(평가 통과 · OVR 최고)를 제시한다.
        /// </summary>
        public static List<GMTradeOffer> ShopPlayer(GMLeagueState league, GMTeamState mine, Player target, GMOfferSort sort = GMOfferSort.Ovr)
        {
            var offers = new List<GMTradeOffer>();
            if (mine == null || target == null || !mine.Roster.Contains(target)) return offers;
            float margin = GMFrontOffice.TradeMargin(GMFrontOffice.Ensure(league).Difficulty);
            foreach (var partner in league.Teams.Values.Where(t => t != mine))
            {
                float receive = TradeValue(target) * NeedMultiplier(partner, target, out _);
                var best = partner.Roster
                    .Where(p => p.InjuryRemainingDays <= 0 && TradeValue(p) * margin <= receive && TradeValue(p) >= receive * 0.45f)
                    .OrderByDescending(p => p.BaseOverall).ThenByDescending(p => p.Potential).FirstOrDefault();
                if (best == null) continue;
                offers.Add(new GMTradeOffer { TeamCode = partner.TeamCode, Player = best, Value = TradeValue(best), Ratio = receive / Math.Max(1f, TradeValue(best) * margin) });
            }
            return SortOffers(offers, sort);
        }

        public static List<GMTradeOffer> SortOffers(IEnumerable<GMTradeOffer> offers, GMOfferSort sort)
        {
            var list = (offers ?? Enumerable.Empty<GMTradeOffer>()).ToList();
            switch (sort)
            {
                case GMOfferSort.Potential: return list.OrderByDescending(o => o.Player.Potential).ThenByDescending(o => o.Player.BaseOverall).ToList();
                case GMOfferSort.Salary: return list.OrderBy(o => o.Player.Salary).ToList();
                case GMOfferSort.Age: return list.OrderBy(o => o.Player.Age).ToList();
                case GMOfferSort.Position: return list.OrderBy(o => PositionOrder(o.Player)).ThenByDescending(o => o.Player.BaseOverall).ToList();
                default: return list.OrderByDescending(o => o.Player.BaseOverall).ThenByDescending(o => o.Player.Potential).ToList();
            }
        }

        public static string SortLabel(GMOfferSort s) => s == GMOfferSort.Potential ? "잠재력" : s == GMOfferSort.Salary ? "연봉" : s == GMOfferSort.Age ? "나이" : s == GMOfferSort.Position ? "포지션" : "OVR";

        /// <summary>Shop a Player 원클릭 체결(1:1).</summary>
        public static GMNegotiationResult AcceptOffer(GMLeagueState league, GMTeamState mine, Player target, GMTradeOffer offer)
        {
            if (offer == null || !league.Teams.TryGetValue(offer.TeamCode, out var partner)) return new GMNegotiationResult { Message = "제안을 선택하십시오." };
            return ExecuteTrade(league, mine, new List<Player> { target }, partner, new List<Player> { offer.Player });
        }

        // ================================================================== ④ 드래프트 · 백분위

        /// <summary>신인 계약으로 재설정 - 나이 · 최저연봉 · 5년 · 유망주 · Ego 1.</summary>
        public static void MakeRookie(Player p, int age)
        {
            p.Age = Math.Max(Player.MinAge, Math.Min(22, age));
            p.Salary = Player.MinSalary;
            p.ContractYears = Player.MaxContractYears;
            p.RoleArchetype = LockerRoomRole.Prospect;
            p.EgoLevel = 1;
            p.IsCaptain = false;
            p.PersonalMorale = 80;
        }

        public static int RookieBonus(Player p) => RoundSalary(3000 + Math.Max(0, p.Potential - 60) * 400);

        /// <summary>[지명] - 드래프트 풀 → 내 구단 로스터(28인 이하 · 연 2명), 계약금(잠재력 비례) 예산 차감.</summary>
        public static GMNegotiationResult Draft(GMLeagueState league, GMTeamState team, Player p)
        {
            var r = new GMNegotiationResult();
            var fo = GMFrontOffice.Ensure(league);
            if (team == null || p == null || !league.DraftPool.Contains(p)) { r.Message = "드래프트 풀의 선수를 선택하십시오."; return r; }
            if (fo.DraftPicksThisYear >= MaxDraftPicksPerYear) { r.Message = $"올해 지명권({MaxDraftPicksPerYear}명)을 모두 썼습니다."; return r; }
            if (team.Roster.Count >= RosterMax) { r.Message = $"로스터 {RosterMax}인 가득 - 먼저 [방출]로 자리를 비우십시오."; return r; }
            int bonus = RookieBonus(p);
            if (team.Budget < bonus) { r.Message = "운영 자금 부족 - 계약금을 낼 수 없습니다."; return r; }
            r.TeamworkBefore = Teamwork(team);
            league.DraftPool.Remove(p);
            team.Budget -= bonus;
            p.IsScouted = true;
            AddToTeam(team, p);
            fo.DraftPicksThisYear++;
            if (!league.RookiesThisYear.Contains(p.InstanceId)) league.RookiesThisYear.Add(p.InstanceId); // [TASK-GM-08] 당해 신인 = 보상 자동 보호
            r.TeamworkAfter = Teamwork(team);
            r.Success = true;
            r.Message = $"{league.SeasonYear} 신인 드래프트 {fo.DraftPicksThisYear}순위 지명: {p.Template.PlayerName}({GMFrontOffice.PositionLabel(p.Position)} · {p.Age}세 · 잠재력 {p.Potential}) · 계약금 {GMDiagnosticFormat.Won(bonus)}";
            News(league, GMNewsKind.Scouting, $"신인 {p.Template.PlayerName} 지명", r.Message);
            return r;
        }

        /// <summary>
        /// 백분위 랭킹(1~99%) - KBO 전체(10구단 로스터 + FA + 드래프트 풀)의 같은 유형(타자/투수) 선수 중 위치.
        /// 타자: 타격 · 장타 · 선구안(ABS) · 수비 · 주루 · OVR · 잠재력 / 투수: 구위 · 구속 · 제구(ABS 보더라인) · 변화 · 체력 · OVR · 잠재력.
        /// </summary>
        public static List<GMPercentileRow> Percentiles(GMLeagueState league, Player p)
        {
            var rows = new List<GMPercentileRow>();
            if (p?.Template == null) return rows;
            var pool = league.AllPlayers.Concat(league.FreeAgents).Concat(league.DraftPool).Where(x => x?.Template != null && x.IsPitcher == p.IsPitcher).Distinct().ToList();
            if (!pool.Contains(p)) pool.Add(p);
            var metrics = p.IsPitcher
                ? new (string, Func<Player, int>)[]
                {
                    ("구위", x => x.Template.PitcherStats.Stuff), ("구속", x => x.Template.PitcherStats.Velocity), ("제구(ABS 보더라인)", x => x.ABSZoneSkill),
                    ("변화", x => x.Template.PitcherStats.Movement), ("체력", x => x.Template.PitcherStats.Stamina), ("OVR", x => x.BaseOverall), ("잠재력", x => x.Potential),
                }
                : new (string, Func<Player, int>)[]
                {
                    ("타격", x => x.Template.BatterStats.Contact), ("장타", x => x.Template.BatterStats.Power), ("선구안(ABS)", x => x.ABSZoneSkill),
                    ("수비", x => x.Template.BatterStats.Defense), ("주루", x => x.Template.BatterStats.Speed), ("OVR", x => x.BaseOverall), ("잠재력", x => x.Potential),
                };
            foreach (var (label, f) in metrics)
            {
                int v = f(p);
                int below = pool.Count(x => f(x) < v), equal = pool.Count(x => f(x) == v);
                double frac = pool.Count <= 1 ? 0.5 : (below + 0.5 * (equal - 1)) / (pool.Count - 1);
                rows.Add(new GMPercentileRow { Label = label, RawValue = v, Percentile = Math.Max(1, Math.Min(99, (int)Math.Round(1 + frac * 98))) });
            }
            return rows;
        }
    }
}
