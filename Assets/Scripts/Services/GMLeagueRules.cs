using System;
using KBOManager.Core;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-18] KBO 규정 락 · 예산 하드 락.
    ///   - 트레이드 · 영입 마감(KBO 리그규정): 정규시즌 7월 31일이 지나면(다음 경기일이 8월 1일 이후) 시즌 종료(포스트시즌 포함)까지
    ///     [트레이드 제안] · [역제안] · [매물 등록] · 외부 FA [계약 제시]가 잠긴다. 스토브리그(연도 전환 후)에 다시 열린다.
    ///   - 예산 하드 락: 내 구단 페이롤이 샐러리캡을 넘거나 운영 예산이 적자(0 미만)면 외부 FA 영입 버튼 자체가 눌리지 않는다.
    ///   - 개막 페널티: 정규시즌 개막(스토브리그 → 정규시즌 전환) 시 초과 상태면 구단주 신임도 -20(해마다 1회).
    /// </summary>
    public static class GMLeagueRules
    {
        public const int DeadlineMonth = 7, DeadlineDay = 31;
        public const int OpeningOverBudgetTrustPenalty = 20;

        public static DateTime TradeDeadline(int year) => new DateTime(year, DeadlineMonth, DeadlineDay);

        /// <summary>리그 달력의 "오늘"(다음 경기일). 정규시즌 전 · 스토브리그는 null.</summary>
        public static DateTime? CurrentDate(GMLeagueState league)
        {
            if (league == null || league.Phase != GMSeasonPhase.RegularSeason) return null;
            int day = Math.Max(0, Math.Min(GMLiveSeasonSimulator.SeasonGames - 1, league.GamesPlayed));
            return GMLiveSeasonSimulator.DateOf(day, league.SeasonYear);
        }

        /// <summary>7/31 트레이드 · 영입 마감이 지났는지 - 다음 경기일이 8/1 이후거나 정규시즌 144경기 종료 · 포스트시즌.</summary>
        public static bool IsPastTradeDeadline(GMLeagueState league)
        {
            if (league == null) return false;
            if (league.Phase == GMSeasonPhase.PostSeason) return true;
            if (league.Phase != GMSeasonPhase.RegularSeason) return false;
            if (league.GamesPlayed >= GMLiveSeasonSimulator.SeasonGames) return true;
            var today = CurrentDate(league);
            return today.HasValue && today.Value.Date > TradeDeadline(league.SeasonYear);
        }

        public static string DeadlineMessage(GMLeagueState league) =>
            $"KBO 규정 - {league?.SeasonYear ?? GMFeatureFlags.DEFAULT_START_YEAR}년 7월 31일 트레이드 · 영입 마감이 지났습니다. 시즌이 끝난 뒤 스토브리그에서 다시 열립니다.";

        /// <summary>내 구단 예산 초과 - 페이롤 > 샐러리캡 또는 운영 예산 적자.</summary>
        public static bool IsOverBudget(GMTeamState team) => team != null && (team.Payroll > team.PayrollCap || team.Budget < 0);

        public static string OverBudgetMessage(GMTeamState team)
        {
            if (team == null) return "";
            if (team.Payroll > team.PayrollCap)
                return $"예산 하드 락 - 페이롤 {GMDiagnosticFormat.Short(team.Payroll)}이(가) 샐러리캡 {GMDiagnosticFormat.Short(team.PayrollCap)}을(를) {GMDiagnosticFormat.Short(team.Payroll - team.PayrollCap)} 넘었습니다. 방출 · 트레이드로 정리하기 전에는 외부 FA를 영입할 수 없습니다.";
            return $"예산 하드 락 - 운영 예산 적자({GMDiagnosticFormat.Short(team.Budget)}). 적자를 메우기 전에는 외부 FA를 영입할 수 없습니다.";
        }

        /// <summary>트레이드 잠금(7/31 마감). 잠기면 true + 사유.</summary>
        public static bool TradeLocked(GMLeagueState league, out string reason)
        {
            reason = IsPastTradeDeadline(league) ? DeadlineMessage(league) : "";
            return reason != "";
        }

        /// <summary>외부 FA 영입 잠금 - 7/31 마감 · 내 구단 예산 하드 락(AI 구단에는 적용하지 않는다). 잠기면 true + 사유.</summary>
        public static bool FreeAgencyLocked(GMLeagueState league, GMTeamState team, out string reason)
        {
            reason = "";
            if (IsPastTradeDeadline(league)) reason = DeadlineMessage(league);
            else if (team != null && team.IsUserTeam && IsOverBudget(team)) reason = OverBudgetMessage(team);
            return reason != "";
        }

        /// <summary>정규시즌 개막 - 내 구단이 예산 초과면 구단주 신임도 -20(해마다 1회). 적용했으면 true.</summary>
        public static bool ApplyOpeningBudgetPenalty(GMLeagueState league)
        {
            var team = league?.UserTeam;
            if (team == null) return false;
            var fo = GMFrontOffice.Ensure(league);
            if (fo.BudgetPenaltyYear == league.SeasonYear || !IsOverBudget(team)) return false;
            fo.BudgetPenaltyYear = league.SeasonYear;
            int floor = fo.Manager != null && fo.Manager.NoFiring ? GMFrontOffice.NoFiringTrustFloor : 0;
            int before = fo.OwnerTrust;
            fo.OwnerTrust = Math.Max(floor, fo.OwnerTrust - OpeningOverBudgetTrustPenalty);
            league.AddNews(new GMNewsItem
            {
                GameIndex = 0, DateLabel = GMLiveSeasonSimulator.DateLabel(0, league.SeasonYear), Kind = GMNewsKind.Decision, IsUserTeam = true, IsMajor = true,
                Title = "구단주 격노 - 예산 초과 상태로 개막",
                Body = $"{OverBudgetMessage(team)} 구단주 신임도 {before} → {fo.OwnerTrust}(-{before - fo.OwnerTrust}).",
            });
            return true;
        }

        // ================================================================== 마케팅 예산(응원단 전용 재화)

        public const int GoodsPerFanSupport = 5;      // 홈경기 굿즈 수익 = 팬 지지율 × 5만 원
        public const int MarketingGateSharePercent = 50; // 관중 흥행 수익 중 마케팅 예산 배정 50%

        /// <summary>홈경기 1회 마케팅 수입(만 원) - 관중 흥행 수익의 50% + 굿즈(팬 지지율 × 5만 원).</summary>
        public static int MarketingIncome(GMTeamState team, int gateRevenue) =>
            team == null ? 0 : Math.Max(0, gateRevenue) * MarketingGateSharePercent / 100 + Math.Max(0, team.FanSupport) * GoodsPerFanSupport;
    }
}
