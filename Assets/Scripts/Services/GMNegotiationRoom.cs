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
    /// [TASK-GM-11] 계약 협상실 - 3지선다 조건부 협상(기획서 6절 1단계).
    ///   ① 후보 풀: 선수 성과 리포트 · 나이 · 유대 · 구단 재정으로 유효한 협상 카드(근거) 5~7장을 고른다(모자라면 범용 카드로 채움).
    ///   ② 제시: 풀에서 서로 다른 분류(성과 · 재정 · 관계 · 보상)를 우선해 3장을 무작위로(재현 가능 시드) 추린다.
    ///   ③ 확률: 협상 진행 가능성 = 베이스라인(만족도 · 충성도 · 선수단 신뢰도 · 팀 분위기 · Ego · 난이도) + 카드 가산(기본 +10%p × 에이전트 성향 배수 0.5~1.5).
    ///      예상 결과 분포(요구액 수용 / 소폭 인상 / 동결 / 삭감)는 성과 등급 기준선에 카드별 이동량(× 성향 배수)을 더해 정규화한다.
    ///   [TASK-GM-17] 협상 모델 개편(사용자 피드백 - "카드가 협상의 전부 · 한 번 결렬하면 끝"):
    ///   ⑤ 단장 제시액(슬라이더, 요구액의 70~120%) - 진행 가능성의 뼈대 = 요구액 대비 제시액 갭(100% = 80% · 90% = 54% · 80% = 28%) + 선수 · 라커룸 보정(베이스라인 - 45%p).
    ///      카드는 그 위에 +10%p × 성향 배수를 더하는 "협상 근거"다. 타결 연봉 = 제시액.
    ///   ⑥ 협상 실패 = 결렬 위기(기회 3회): 선수 측이 요구액을 제시액과의 차이 35%만큼 양보하고 다시 테이블에 앉는다(실패 1회당 진행 가능성 -4%p).
    ///      3회 실패 = 최종 결렬 → 선수는 즉시 FA 시장(원 소속 = 내 구단)으로 나간다(쿨다운 대신 시장 이동).
    /// </summary>
    public static class GMNegotiationRoom
    {
        public const float CardBaseBonus = 0.10f;          // 카드 기본 +10%p
        public const float MinArchetypeMultiplier = 0.5f, MaxArchetypeMultiplier = 1.5f;
        public const float MinProgress = 0.03f, MaxProgress = 0.97f;
        public const int PoolMin = 5, PoolMax = 7, OfferCount = 3;
        public const int MinRaisePercent = 5;               // 협상 테이블 요구액은 현재 연봉 +5% 이상
        public const int MaxStrikes = 3;                    // [TASK-GM-17] 협상 기회 3회
        public const float ConcessionRate = 0.35f, StrikePenalty = 0.04f, MinOfferRatio = 0.7f, MaxOfferRatio = 1.2f, DefaultOfferRatio = 0.95f;

        /// <summary>[TASK-GM-17] 요구액 대비 제시액 갭 → 진행 가능성 뼈대(선수 · 라커룸 보정 전).</summary>
        public static float GapProgress(int offer, int demand)
        {
            double r = offer / (double)Math.Max(1, demand);
            double v = r >= 1.0 ? 0.80 + (r - 1.0) * 1.2 : 0.80 - (1.0 - r) * 2.6;
            return (float)Math.Max(MinProgress, Math.Min(0.92, v));
        }

        public static int ClampOffer(int demand, int offer) => Round(Math.Max(demand * MinOfferRatio, Math.Min(demand * MaxOfferRatio, offer)));

        /// <summary>제시액 기준 결과 분류.</summary>
        public static GMNegotiationOutcome Classify(int current, int demand, int offer) =>
            offer >= demand ? GMNegotiationOutcome.AcceptDemand : offer > current ? GMNegotiationOutcome.SmallRaise : offer >= current * 0.95 ? GMNegotiationOutcome.Freeze : GMNegotiationOutcome.Cut;

        /// <summary>이번 스토브리그 협상 진행 기록(없으면 null).</summary>
        public static GMNegotiationTalk TalkOf(GMLeagueState league, Player p) =>
            league == null || p == null ? null : GMFrontOffice.Ensure(league).NegotiationTalks.FirstOrDefault(t => t.PlayerId == p.InstanceId && t.Year == league.SeasonYear);

        private static GMNegotiationTalk EnsureTalk(GMLeagueState league, Player p, int demand)
        {
            var t = TalkOf(league, p);
            if (t != null) return t;
            var fo = GMFrontOffice.Ensure(league);
            fo.NegotiationTalks.RemoveAll(x => x.PlayerId == p.InstanceId);
            t = new GMNegotiationTalk { PlayerId = p.InstanceId, Year = league.SeasonYear, Demand = demand };
            fo.NegotiationTalks.Add(t);
            return t;
        }

        /// <summary>제시액을 바꾸고 기본 · 카드별 예측을 다시 계산한다.</summary>
        public static void SetOffer(GMLeagueState league, GMTeamState team, GMNegotiationSession s, int offer)
        {
            if (s?.Player == null || league == null || team == null) return;
            s.Offer = ClampOffer(s.Demand, offer);
            s.Baseline = Forecast(league, team, s, null);
            for (int i = 0; i < s.Forecasts.Count; i++) s.Forecasts[i] = Forecast(league, team, s, s.Forecasts[i].Card);
        }

        public static string OutcomeLabel(GMNegotiationOutcome o) => o == GMNegotiationOutcome.AcceptDemand ? "요구액 수용" : o == GMNegotiationOutcome.SmallRaise ? "소폭 인상" : o == GMNegotiationOutcome.Freeze ? "동결" : "삭감";
        public static string CategoryLabel(GMNegotiationCardCategory c) => c == GMNegotiationCardCategory.Performance ? "성과 근거" : c == GMNegotiationCardCategory.Finance ? "재정 근거" : c == GMNegotiationCardCategory.Relationship ? "관계 근거" : "보상 조건";

        public static string ArchetypeLabel(GMAgentArchetype a)
        {
            switch (a)
            {
                case GMAgentArchetype.Loyal: return "충성형";
                case GMAgentArchetype.LongTermSeeker: return "장기계약 선호";
                case GMAgentArchetype.MoneyFirst: return "금전 우선";
                case GMAgentArchetype.RoleSeeker: return "보직 중시";
                case GMAgentArchetype.WinNow: return "우승 지향";
                default: return "현실형";
            }
        }

        // ================================================================== 카드 정의

        private sealed class Ctx
        {
            public GMLeagueState League;
            public GMTeamState Team;
            public Player Player;
            public GMPlayerReport Report;
            public List<(GMBondKind kind, Player partner)> Bonds;
            public GMFrontOffice.BudgetInfo Budget;
            public int LastRank;
        }

        private sealed class CardDef
        {
            public string Id, Title;
            public GMNegotiationCardCategory Category;
            public float[] Shift;                       // 요구액 수용 · 소폭 인상 · 동결 · 삭감 이동량
            public Dictionary<GMAgentArchetype, float> Affinity;
            public Func<Ctx, bool> Valid;
            public Func<Ctx, string> Pitch;
            public int ExtraYears;
        }

        private static Dictionary<GMAgentArchetype, float> A(params (GMAgentArchetype a, float m)[] items) => items.ToDictionary(x => x.a, x => x.m);

        private static readonly CardDef[] Defs =
        {
            new CardDef { Id = "PERF_DECLINE", Category = GMNegotiationCardCategory.Performance, Title = "성적 하락 근거 제시",
                Shift = new[] { -0.15f, -0.04f, 0.11f, 0.08f },
                Affinity = A((GMAgentArchetype.Realist, 1.3f), (GMAgentArchetype.Loyal, 0.6f), (GMAgentArchetype.RoleSeeker, 0.6f), (GMAgentArchetype.MoneyFirst, 0.8f)),
                Valid = c => c.Report.Trend.Tone == GMReportTone.Weak || c.Report.War.Tone == GMReportTone.Weak || c.Report.Production.Tone == GMReportTone.Weak,
                Pitch = c => $"종합 기여도 {c.Report.War.ValueText} · {c.Report.Production.Label} {c.Report.Production.ValueText} 자료로 동결을 제안합니다." },
            new CardDef { Id = "PERF_VALUE", Category = GMNegotiationCardCategory.Performance, Title = "기여도 인정 · 시장가 비교",
                Shift = new[] { -0.06f, 0.12f, -0.04f, -0.02f },
                Affinity = A((GMAgentArchetype.Realist, 1.5f), (GMAgentArchetype.MoneyFirst, 1.2f)),
                Valid = c => c.Report.War.Tone != GMReportTone.Weak,
                Pitch = c => $"팀 기여도(WAR {c.Report.War.ValueText})는 인정하되 리그 1WAR당 시장가에 맞춘 인상을 제시합니다." },
            new CardDef { Id = "FIN_CAP", Category = GMNegotiationCardCategory.Finance, Title = "샐러리캡 · 예산 한계 공개",
                Shift = new[] { -0.10f, 0.03f, 0.06f, 0.01f },
                Affinity = A((GMAgentArchetype.MoneyFirst, 0.5f), (GMAgentArchetype.Loyal, 1.2f), (GMAgentArchetype.Realist, 1.2f)),
                Valid = c => c.Budget.MoneyForFA < c.Team.PayrollCap / 10 || c.Team.Payroll >= c.Team.PayrollCap * 85L / 100,
                Pitch = c => $"재무팀장 예산표 - 페이롤 {GMDiagnosticFormat.Short(c.Team.Payroll)} / 캡 {GMDiagnosticFormat.Short(c.Team.PayrollCap)}, 여유가 없다고 설명합니다." },
            new CardDef { Id = "FIN_EFFICIENCY", Category = GMNegotiationCardCategory.Finance, Title = "연봉 효율 비교표",
                Shift = new[] { -0.12f, -0.02f, 0.08f, 0.06f },
                Affinity = A((GMAgentArchetype.Realist, 1.4f), (GMAgentArchetype.WinNow, 0.8f), (GMAgentArchetype.MoneyFirst, 0.7f)),
                Valid = c => c.Report.Efficiency.Tone == GMReportTone.Weak || (c.Report.Efficiency.Tone == GMReportTone.Neutral && c.Player.Salary >= 10000),
                Pitch = c => $"연봉 효율 {c.Report.Efficiency.ValueText}({c.Report.Efficiency.Verdict}) - 같은 포지션 1WAR당 연봉 비교표를 내밉니다." },
            new CardDef { Id = "REL_BOND", Category = GMNegotiationCardCategory.Relationship, Title = "핵심 동료와의 유대 강조",
                Shift = new[] { -0.06f, 0.06f, 0.02f, -0.02f },
                Affinity = A((GMAgentArchetype.Loyal, 1.5f), (GMAgentArchetype.MoneyFirst, 0.6f), (GMAgentArchetype.Realist, 0.8f)),
                Valid = c => c.Bonds.Count > 0,
                Pitch = c => c.Bonds.Count == 0 ? "동료들과의 호흡을 이어 가자고 설득합니다."
                    : $"{c.Bonds[0].partner.Template.PlayerName}와(과)의 {GMPlayerBonds.KindLabel(c.Bonds[0].kind)} 호흡을 계속 이어 가자고 설득합니다." },
            new CardDef { Id = "REL_FRANCHISE", Category = GMNegotiationCardCategory.Relationship, Title = "프랜차이즈 상징 대우",
                Shift = new[] { -0.08f, 0.08f, 0.02f, -0.02f },
                Affinity = A((GMAgentArchetype.Loyal, 1.5f), (GMAgentArchetype.LongTermSeeker, 1.2f), (GMAgentArchetype.MoneyFirst, 0.7f)),
                Valid = c => c.Player.Loyalty >= 60 || c.Player.IsCaptain || c.Player.RoleArchetype == LockerRoomRole.DugoutLeader || c.Player.RoleArchetype == LockerRoomRole.AlphaDog,
                Pitch = c => $"구단 충성도 {c.Player.Loyalty} - 구단의 얼굴로 대우하겠다는 상징성을 강조합니다." },
            new CardDef { Id = "REL_WINNOW", Category = GMNegotiationCardCategory.Relationship, Title = "우승 목표 호소",
                Shift = new[] { -0.07f, 0.04f, 0.04f, -0.01f },
                Affinity = A((GMAgentArchetype.WinNow, 1.5f), (GMAgentArchetype.Loyal, 1.1f), (GMAgentArchetype.Realist, 0.8f)),
                Valid = c => c.LastRank <= 5 || c.Player.Age >= 30,
                Pitch = c => "다음 시즌 우승 도전 청사진을 보여 주며 함께 가자고 호소합니다." },
            new CardDef { Id = "REW_LONGTERM", Category = GMNegotiationCardCategory.Reward, Title = "장기계약 보장(+1년)", ExtraYears = 1,
                Shift = new[] { -0.10f, 0.09f, 0.02f, -0.01f },
                Affinity = A((GMAgentArchetype.LongTermSeeker, 1.5f), (GMAgentArchetype.RoleSeeker, 1.1f), (GMAgentArchetype.MoneyFirst, 0.8f)),
                Valid = c => c.Player.Age <= 32,
                Pitch = c => "계약 기간을 1년 늘려 안정성을 주는 대신 연평균 금액을 낮춥니다." },
            new CardDef { Id = "REW_ROLE", Category = GMNegotiationCardCategory.Reward, Title = "주전 · 보직 보장",
                Shift = new[] { -0.08f, 0.05f, 0.03f, 0f },
                Affinity = A((GMAgentArchetype.RoleSeeker, 1.5f), (GMAgentArchetype.WinNow, 1.1f), (GMAgentArchetype.Realist, 0.9f)),
                Valid = c => c.Player.EgoLevel >= 3 || c.Player.RoleArchetype == LockerRoomRole.Prospect || c.Player.RoleArchetype == LockerRoomRole.Ambitious,
                Pitch = c => "다음 시즌 주전 · 보직을 약속합니다(약속 트래커 등록 - 위반 시 충성도 -25 · 선수단 신뢰도 -10)." },
            new CardDef { Id = "REW_OPTION", Category = GMNegotiationCardCategory.Reward, Title = "성적 옵션(인센티브) 계약",
                Shift = new[] { -0.12f, 0.06f, 0.05f, 0.01f },
                Affinity = A((GMAgentArchetype.MoneyFirst, 1.3f), (GMAgentArchetype.LongTermSeeker, 0.5f), (GMAgentArchetype.Realist, 1.1f)),
                Valid = c => c.Player.EgoLevel >= 2 || c.Report.Trend.Tone != GMReportTone.Strong,
                Pitch = c => "보장 연봉은 낮추고 성적 달성 옵션으로 차액을 채우는 구조를 제시합니다." },
        };

        private static readonly string[] Fallback = { "PERF_VALUE", "REW_OPTION", "REL_WINNOW", "REW_LONGTERM", "FIN_CAP" };

        /// <summary>카드 · 성향 배수(0.5~1.5, 정의 없으면 1.0).</summary>
        public static float ArchetypeMultiplier(string cardId, GMAgentArchetype a)
        {
            var def = Defs.FirstOrDefault(d => d.Id == cardId);
            float m = def != null && def.Affinity.TryGetValue(a, out var v) ? v : 1f;
            return Math.Max(MinArchetypeMultiplier, Math.Min(MaxArchetypeMultiplier, m));
        }

        public static IEnumerable<string> AllCardIds => Defs.Select(d => d.Id);

        // ================================================================== 대상 · 세션

        /// <summary>협상 대상 = 1군 + 퓨처스 중 잔여 계약 1년 이하(연봉 재협상 · 재계약), OVR 순.</summary>
        public static List<Player> Targets(GMTeamState team) =>
            team == null ? new List<Player>() : team.ReservePlayers.Where(p => p?.Template != null && p.ContractYears <= 1).OrderByDescending(p => p.BaseOverall).ToList();

        public static bool IsOnCooldown(GMLeagueState league, Player p) => p != null && GMFrontOffice.Ensure(league).NegotiationCooldownIds.Contains(p.InstanceId);

        public static int DemandOf(GMLeagueState league, Player p)
        {
            int demand = GMStoveLeagueMarket.ExtensionDemand(p, GMFrontOffice.Ensure(league).Difficulty);
            return Round(Math.Max(demand, p.Salary * (100 + MinRaisePercent) / 100.0));
        }

        /// <summary>협상 테이블 열기 - 리포트 · 유대 · 카드 풀(5~7) · 제시 3장 · 카드별 예측.</summary>
        public static GMNegotiationSession Open(GMLeagueState league, GMTeamState team, Player p, int years = 0, int reroll = 0, int offer = 0)
        {
            var s = new GMNegotiationSession { Player = p };
            if (league == null || team == null || p?.Template == null || !team.ReservePlayers.Contains(p)) { s.BlockReason = "우리 구단 선수를 선택하십시오."; return s; }
            var fo = GMFrontOffice.Ensure(league);
            var ctx = new Ctx
            {
                League = league, Team = team, Player = p, Report = GMSeasonReview.Report(league, p),
                Bonds = GMPlayerBonds.For(team, p), Budget = GMFrontOffice.Budget(league, team), LastRank = GMFrontOffice.LastRank(league, team.TeamCode),
            };
            s.Report = ctx.Report;
            s.CurrentSalary = p.Salary;
            s.BaseDemand = DemandOf(league, p);
            var talk = TalkOf(league, p); // [TASK-GM-17] 실패할 때마다 양보한 요구액 · 남은 기회
            s.Demand = talk != null && talk.Demand > 0 ? talk.Demand : s.BaseDemand;
            s.Strikes = talk?.Strikes ?? 0;
            s.Offer = ClampOffer(s.Demand, offer > 0 ? offer : (int)Math.Round(s.Demand * DefaultOfferRatio));
            s.Years = years > 0 ? Math.Max(1, Math.Min(Player.MaxContractYears, years)) : GMStoveLeagueMarket.PreferredYears(p);
            s.Bonds.AddRange(ctx.Bonds.Select(b => $"{GMPlayerBonds.KindLabel(b.kind)} · {b.partner.Template.PlayerName}"));
            s.OnCooldown = IsOnCooldown(league, p) || s.Strikes >= MaxStrikes;
            if (s.OnCooldown) s.BlockReason = $"{p.Template.PlayerName} 측과 협상이 최종 결렬되었습니다(협상 기회 {MaxStrikes}회 소진).";

            // ① 후보 풀 5~7장
            var rng = new Random(Seed(league, p, reroll));
            var valid = Defs.Where(d => d.Valid(ctx)).OrderBy(_ => rng.Next()).ToList();
            foreach (var id in Fallback)
            {
                if (valid.Count >= PoolMin) break;
                var d = Defs.First(x => x.Id == id);
                if (!valid.Contains(d)) valid.Add(d);
            }
            foreach (var d in valid.Take(PoolMax)) s.Pool.Add(MakeCard(d, ctx));

            // ② 서로 다른 분류 우선 3장
            var shuffled = s.Pool.OrderBy(_ => rng.Next()).ToList();
            foreach (var c in shuffled)
                if (s.Offered.Count < OfferCount && s.Offered.All(o => o.Category != c.Category)) s.Offered.Add(c);
            foreach (var c in shuffled)
                if (s.Offered.Count < OfferCount && !s.Offered.Contains(c)) s.Offered.Add(c);

            // ③ 예측
            s.Baseline = Forecast(league, team, s, null);
            foreach (var c in s.Offered) s.Forecasts.Add(Forecast(league, team, s, c));
            return s;
        }

        private static GMNegotiationCard MakeCard(CardDef d, Ctx ctx)
        {
            var a = ctx.Player.AgentArchetype;
            float m = ArchetypeMultiplier(d.Id, a);
            return new GMNegotiationCard
            {
                Id = d.Id, Category = d.Category, Title = d.Title, Pitch = d.Pitch(ctx), ArchetypeMultiplier = m, ExtraYears = d.ExtraYears,
                ArchetypeReaction = m >= 1.2f ? $"{ArchetypeLabel(a)} 선호(×{m:0.0})" : m <= 0.8f ? $"{ArchetypeLabel(a)} 반감(×{m:0.0})" : $"{ArchetypeLabel(a)} 무난(×{m:0.0})",
            };
        }

        /// <summary>협상 진행 가능성 베이스라인 0.15~0.85.</summary>
        public static float BaselineProgress(GMLeagueState league, GMTeamState team, Player p)
        {
            int teamwork = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff).TeamworkScore;
            double v = 0.45 + (p.PersonalMorale - 70) * 0.004 + (p.Loyalty - 50) * 0.003 + (team.LockerRoomTrust - 60) * 0.002 + (teamwork - 75) * 0.002
                       - (p.EgoLevel - 3) * 0.03 - (GMFrontOffice.DemandMultiplier(GMFrontOffice.Ensure(league).Difficulty) - 1f) * 0.3;
            return (float)Math.Max(0.15, Math.Min(0.85, v));
        }

        public static GMNegotiationForecast Forecast(GMLeagueState league, GMTeamState team, GMNegotiationSession s, GMNegotiationCard card)
        {
            var f = new GMNegotiationForecast { Card = card };
            var p = s.Player;
            float m = card?.ArchetypeMultiplier ?? 1f;
            f.Bonus = card == null ? 0f : CardBaseBonus * m;
            // [TASK-GM-17] 진행 가능성 = 제시액 갭 뼈대 + 선수 · 라커룸 보정(베이스라인 - 0.45) - 실패 횟수 × 4%p + 카드 가산
            int offer = s.Offer > 0 ? s.Offer : ClampOffer(s.Demand, (int)Math.Round(s.Demand * DefaultOfferRatio));
            float core = GapProgress(offer, s.Demand) + (BaselineProgress(league, team, p) - 0.45f) - s.Strikes * StrikePenalty;
            f.Progress = Math.Max(MinProgress, Math.Min(MaxProgress, core + f.Bonus));
            f.Salary = offer;
            f.Outcome = Classify(s.CurrentSalary, s.Demand, offer);
            for (int i = 0; i < 4; i++) f.Distribution[i] = i == (int)f.Outcome ? 1f : 0f;

            f.Years = Math.Max(1, Math.Min(Player.MaxContractYears, s.Years + (card?.ExtraYears ?? 0)));
            f.Salaries[(int)GMNegotiationOutcome.AcceptDemand] = s.Demand;
            f.Salaries[(int)GMNegotiationOutcome.SmallRaise] = Round(Math.Max(s.CurrentSalary + 100, s.CurrentSalary + (s.Demand - s.CurrentSalary) * 0.4));
            f.Salaries[(int)GMNegotiationOutcome.Freeze] = s.CurrentSalary;
            f.Salaries[(int)GMNegotiationOutcome.Cut] = Round(s.CurrentSalary * 0.9);

            // 재무팀장 - 제시액 기준 샐러리캡 · 운영 자금(계약금)
            long payrollAfter = (long)team.Payroll - (team.Roster.Contains(p) ? s.CurrentSalary : 0) + offer;
            long bonus = (long)offer * f.Years * GMStoveLeagueMarket.ExtensionBonusPercent / 100;
            if (payrollAfter > team.PayrollCap)
            {
                f.FinanceTone = GMReportTone.Risk;
                f.FinanceWarning = $"재무팀장 경고: 제시액 타결 시 페이롤 {GMDiagnosticFormat.Short(payrollAfter)} - 샐러리캡 {GMDiagnosticFormat.Short(payrollAfter - team.PayrollCap)} 초과";
            }
            else if (team.Budget < bonus)
            {
                f.FinanceTone = GMReportTone.Risk;
                f.FinanceWarning = $"재무팀장 경고: 계약금 {GMDiagnosticFormat.Short(bonus)} - 운영 자금 부족(잔여 {GMDiagnosticFormat.Short(team.Budget)})";
            }
            else if (payrollAfter > team.PayrollCap * 90L / 100)
            {
                f.FinanceTone = GMReportTone.Weak;
                f.FinanceWarning = $"재무팀장 주의: 타결 시 캡 여유 {GMDiagnosticFormat.Short(team.PayrollCap - payrollAfter)}(10% 미만) · 계약금 {GMDiagnosticFormat.Short(bonus)}";
            }
            else
            {
                f.FinanceTone = GMReportTone.Neutral;
                f.FinanceWarning = $"재무팀장: 타결 시 캡 여유 {GMDiagnosticFormat.Short(team.PayrollCap - payrollAfter)} · 계약금 {GMDiagnosticFormat.Short(bonus)} · 예산 {GMDiagnosticFormat.Short(team.Budget)} → {GMDiagnosticFormat.Short(team.Budget - bonus)}";
            }
            return f;
        }

        public static string ProgressText(GMNegotiationForecast f) => $"협상 진행 가능성 {f.Progress * 100:0}% · 결렬 위험 {f.BreakRisk * 100:0}%";

        /// <summary>[TASK-GM-17] 타결 시 조건 - 제시액 · 기간 · 결과 분류.</summary>
        public static string DistributionText(GMNegotiationForecast f) =>
            $"타결 시 {f.Years}년 · 연봉 {GMDiagnosticFormat.Short(f.Salary)}({OutcomeLabel(f.Outcome)})";

        // ================================================================== 결과

        /// <summary>카드(제시 3장 중 index, -1 = 카드 없이) 선택 → 진행 판정 → 결과 분포 추첨 → 연봉 · 계약 · 충성도 반영.</summary>
        public static GMNegotiationRoomResult Resolve(GMLeagueState league, GMTeamState team, GMNegotiationSession s, int cardIndex, double? rollOverride = null)
        {
            var r = new GMNegotiationRoomResult();
            var p = s?.Player;
            if (league == null || team == null || p == null || !team.ReservePlayers.Contains(p)) { r.Message = "우리 구단 선수를 선택하십시오."; return r; }
            if (IsOnCooldown(league, p)) { r.Message = s.BlockReason != "" ? s.BlockReason : "재협상 쿨다운 중입니다."; return r; }
            var f = cardIndex >= 0 && cardIndex < s.Forecasts.Count ? s.Forecasts[cardIndex] : s.Baseline;
            var fo = GMFrontOffice.Ensure(league);
            var rng = new Random(Seed(league, p, 1000 + fo.NegotiationAttempts));
            fo.NegotiationAttempts++;
            string name = p.Template.PlayerName;
            string cardText = f.Card != null ? $"[{f.Card.Title}]" : "[카드 없이 기본 제시]";
            // [TASK-GM-13] [주전 · 보직 보장] 카드 = 약속 제안(PROPOSED) - 타결 시 ACTIVE, 결렬 · 자금 부족 시 폐기
            var promise = f.Card != null && f.Card.Id == GMPromiseSystem.RoleCardId && team.IsUserTeam
                ? GMPromiseSystem.Propose(league, team, p, GMPromiseKind.StarterGuarantee, "계약 협상실") : null;

            double roll = rng.NextDouble();
            if (rollOverride.HasValue) roll = rollOverride.Value; // [TASK-GM-17] 검증 · 툴 - 판정 고정
            if (roll >= f.Progress)
            {
                // [TASK-GM-17] 실패 = 결렬 위기 - 요구액 양보 후 다음 기회, 3회째 = 최종 결렬 → FA 시장
                GMPromiseSystem.Cancel(league, promise, "협상 불발");
                var talk = EnsureTalk(league, p, s.Demand);
                talk.Strikes++;
                talk.LastOffer = f.Salary;
                p.PersonalMorale = Math.Max(0, p.PersonalMorale - 3);
                p.Loyalty = p.Loyalty - 2;
                r.ChancesLeft = Math.Max(0, MaxStrikes - talk.Strikes);
                if (talk.Strikes < MaxStrikes)
                {
                    int conceded = f.Salary < s.Demand ? Round(Math.Max(s.CurrentSalary, s.Demand - (s.Demand - f.Salary) * ConcessionRate)) : s.Demand;
                    talk.Demand = conceded;
                    r.Stalled = true;
                    r.NewDemand = conceded;
                    r.Message = $"{name} 측 거절 {cardText} - 제시 {GMDiagnosticFormat.Short(f.Salary)} · 진행 가능성 {f.Progress * 100:0}%에서 불발. " +
                                $"에이전트가 요구액을 {GMDiagnosticFormat.Short(s.Demand)} → {GMDiagnosticFormat.Short(conceded)}로 낮춰 다시 협상하자고 합니다(남은 기회 {r.ChancesLeft}회 · 만족도 -3 · 충성도 -2).";
                    return r;
                }
                r.Broken = true;
                fo.NegotiationCooldownIds.Add(p.InstanceId);
                team.LockerRoomTrust = Math.Max(0, team.LockerRoomTrust - 2);
                bool tradeRisk = p.EgoLevel >= 4 && p.Loyalty < 45;
                var bonded = GMSalaryChain.ApplyDeparture(league, team, p, "최종 결렬");
                r.MovedToFA = GMFaCompensation.DeclareFreeAgent(league, team, p) != null; // FA 시장 방출 훅(원 소속 = 내 구단)
                league.PriorityNegotiationIds.Remove(p.InstanceId);
                GMFuturesMeeting.Prune(league);
                r.ChainEffects = bonded;
                r.Message = $"{name} 측 최종 결렬 {cardText} - 협상 기회 {MaxStrikes}회 소진, {(r.MovedToFA ? "FA 시장으로 나갔습니다" : "구단을 떠났습니다")}(선수단 신뢰도 -2)." +
                            GMSalaryChain.Summary(bonded) + (tradeRisk ? " 동료들 사이에 동요가 있습니다." : "");
                league.AddNews(new GMNewsItem
                {
                    GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Trade, IsUserTeam = true,
                    Title = $"[FA] {name}, {CompyaName(team.TeamCode)}와 협상 최종 결렬 - 시장 출격",
                    Body = $"세 차례 협상이 모두 무산됐다. 에이전트는 다른 구단의 관심을 언급했다(최종 요구 {GMDiagnosticFormat.Short(s.Demand)} · 구단 제시 {GMDiagnosticFormat.Short(f.Salary)}).",
                });
                return r;
            }

            var outcome = f.Outcome;
            int salary = f.Salary; // [TASK-GM-17] 타결 연봉 = 단장 제시액
            long bonus = (long)salary * f.Years * GMStoveLeagueMarket.ExtensionBonusPercent / 100;
            if (team.Budget < bonus) { GMPromiseSystem.Cancel(league, promise, "운영 자금 부족"); r.Message = $"운영 자금 부족 - 계약금 {GMDiagnosticFormat.Won(bonus)}이 필요합니다(협상 보류)."; return r; }
            team.Budget -= bonus;
            p.Salary = salary;
            p.ContractYears = f.Years;
            int morale = outcome == GMNegotiationOutcome.AcceptDemand ? 8 : outcome == GMNegotiationOutcome.SmallRaise ? 4 : outcome == GMNegotiationOutcome.Freeze ? -2 : -6;
            int loyalty = outcome == GMNegotiationOutcome.AcceptDemand ? 4 : outcome == GMNegotiationOutcome.SmallRaise ? 2 : outcome == GMNegotiationOutcome.Freeze ? 0 : -5;
            if (f.Card != null && f.Card.ArchetypeMultiplier <= 0.8f) loyalty -= 2; // 싫어하는 근거를 들이밀었다
            p.PersonalMorale = Math.Max(0, Math.Min(100, p.PersonalMorale + morale));
            p.Loyalty = p.Loyalty + loyalty;
            team.LockerRoomTrust = Math.Min(100, team.LockerRoomTrust + 1);
            if (GMPromiseSystem.Activate(league, promise)) r.Promise = promise; // [TASK-GM-13] 계약 체결 = 약속 활성
            r.ChainEffects = GMSalaryChain.Apply(league, team, p, s.CurrentSalary, salary, outcome); // [TASK-GM-14] 동료 연봉 연쇄
            fo.NegotiationTalks.RemoveAll(t => t.PlayerId == p.InstanceId); // [TASK-GM-17] 타결 = 협상 기록 종료
            r.Success = true;
            r.Outcome = outcome;
            r.Salary = salary;
            r.Years = f.Years;
            r.Message = $"{name} 협상 타결 {cardText} - {OutcomeLabel(outcome)}: {f.Years}년 · 연봉 {GMDiagnosticFormat.Won(salary)}(이전 {GMDiagnosticFormat.Short(s.CurrentSalary)}) · 계약금 {GMDiagnosticFormat.Short(bonus)} · " +
                        $"만족도 {(morale >= 0 ? "+" : "")}{morale} · 충성도 {(loyalty >= 0 ? "+" : "")}{loyalty}" + GMSalaryChain.Summary(r.ChainEffects);
            league.AddNews(new GMNewsItem
            {
                GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Trade, IsUserTeam = true,
                Title = $"{name} 연봉 협상 타결({OutcomeLabel(outcome)})", Body = r.Message,
            });
            return r;
        }

        private static int Seed(GMLeagueState league, Player p, int salt) =>
            league.Seed ^ GMFrontOffice.Hash(p.InstanceId) ^ (league.SeasonYear * 7919) ^ (salt * 104729);

        private static int Round(double v) => Math.Max(Player.MinSalary, Math.Min(Player.MaxSalary, (int)Math.Round(v / 100.0) * 100));
        private static string CompyaName(string code) => NameAliasTable.DisplayTeamName(code);
    }
}
