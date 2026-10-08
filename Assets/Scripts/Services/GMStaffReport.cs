using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>[TASK-GM-17] 위험 등급(색 구간) - 화면은 색 · 막대로 보여 주고 글자는 "안전 · 주의 · 위험"만 쓴다.</summary>
    public enum GMRiskLevel { Safe = 0, Caution = 1, Danger = 2 }

    public class GMStaffPositionRow
    {
        public string Position = "", PositionLabel = "", PlayerName = "";
        public double Production, LeagueProduction, War;
        public int Percentile;          // 10구단 같은 포지션 주전 대비 백분위(1~99)
    }

    public class GMStaffCandidate
    {
        public Player Player;
        public string Source = "";      // "FA 시장" · 구단명(트레이드)
        public bool IsFreeAgent;
        public int OvrGain;             // 현재 주전 대비 OVR 증가
        public string Line = "";
    }

    public class GMStaffValueRow
    {
        public Player Player;
        public double Ratio;            // 연봉 효율(기여도 시장가 ÷ 연봉)
        public string Line = "";
    }

    public class GMStaffRiskRow
    {
        public Player Player;
        public int Loyalty;
        public string Reason = "";
    }

    public class GMStaffBondRow
    {
        public Player A, B;
        public GMBondKind Kind;
        public int TrustDrop;
        public string Line = "";
    }

    /// <summary>[TASK-GM-17] 프런트 직원 3인 리포트 묶음.</summary>
    public class GMStaffReportBundle
    {
        // 데이터분석팀장
        public readonly List<GMStaffPositionRow> WeakPositions = new List<GMStaffPositionRow>();
        public readonly List<GMStaffCandidate> Candidates = new List<GMStaffCandidate>();
        public string DataHeadline = "";
        public int DataReliability;
        // 재무팀장
        public long Payroll, Cap, CapRoom, Budget, MoneyForFA;
        public float CapUsage;
        public GMRiskLevel TaxRisk;
        public string TaxLine = "", FinanceHeadline = "";
        public readonly List<GMStaffValueRow> BestValue = new List<GMStaffValueRow>();
        public readonly List<GMStaffValueRow> WorstValue = new List<GMStaffValueRow>();
        public int FinanceReliability;
        // 선수관리팀장
        public int Trust;
        public double AvgLoyalty;
        public readonly List<GMStaffRiskRow> LoyaltyRisks = new List<GMStaffRiskRow>();
        public readonly List<GMStaffBondRow> Bonds = new List<GMStaffBondRow>();
        public string RelationsHeadline = "";
        public int RelationsReliability;
        // 운영팀장(조력자) 한 줄 정리
        public string AssistantLine = "";
    }

    /// <summary>
    /// [TASK-GM-17] 프런트 직원 리포트(GMStaffReportView 데이터) - 데이터분석팀장 · 재무팀장 · 선수관리팀장 + 운영팀장(조력자) 정리.
    ///   ① 데이터분석: 시즌 결산(GMSeasonReview) 포지션 생산성 → 10구단 같은 포지션 대비 백분위 · 가장 취약한 4개 포지션, 그 포지션 시장 대체 후보 Top 3(FA 시장 + AI 구단 트레이드).
    ///   ② 재무: 페이롤 / 샐러리캡 게이지 · 캡 여유 · 경쟁균형세 위험도(캡 90% 미만 안전 · 90~100% 주의 · 초과 위험) · 연봉 효율 최고 / 최악 5명.
    ///   ③ 선수관리: 충성도 40 미만 위험군 · 유대(배터리 · 키스톤 · 멘토) 파괴 시 예상 선수단 신뢰도 하락(계약 만료 · 충성도 위험이 걸린 유대 우선).
    ///   보고 신뢰도(%) = 표본량 - 데이터분석(타석 · 이닝 신뢰도 높음 90 · 보통 70 · 낮음 45) · 재무 95(장부) · 선수관리 60 + 진행 경기 비율 × 30.
    /// </summary>
    public static class GMStaffReport
    {
        public const int LoyaltyRiskLine = 40, TopCandidates = 3, ValueRows = 5, RiskRows = 8, BondRows = 8;
        public const float CautionUsage = 0.90f;

        public static GMRiskLevel RiskOf(float capUsage) => capUsage > 1f ? GMRiskLevel.Danger : capUsage >= CautionUsage ? GMRiskLevel.Caution : GMRiskLevel.Safe;
        public static string RiskLabel(GMRiskLevel r) => r == GMRiskLevel.Danger ? "위험" : r == GMRiskLevel.Caution ? "주의" : "안전";

        /// <summary>포지션 코드 매칭(투수는 선발 / 불펜으로 묶는다).</summary>
        private static bool SamePosition(Player p, string pos)
        {
            if (p?.Template == null) return false;
            if (pos == "SP") return p.IsPitcher && p.Template.PitcherRole == PitcherRole.StartingPitcher;
            if (pos == "RP") return p.IsPitcher && p.Template.PitcherRole != PitcherRole.StartingPitcher;
            if (pos == "DH") return !p.IsPitcher;
            return !p.IsPitcher && p.Position == pos;
        }

        public static GMStaffReportBundle Build(GMLeagueState league)
        {
            var b = new GMStaffReportBundle();
            var team = league?.UserTeam;
            if (team == null) return b;
            var sm = GMSeasonReview.Build(league);

            // ① 데이터분석팀장
            var all = sm.AllPositions.Count > 0 ? sm.AllPositions : sm.Weaknesses;
            foreach (var w in all.OrderBy(x => x.Production - x.LeagueProduction).ThenBy(x => x.War).Take(4))
                b.WeakPositions.Add(new GMStaffPositionRow
                {
                    Position = w.Position, PositionLabel = w.PositionLabel, PlayerName = w.PlayerName, Production = w.Production, LeagueProduction = w.LeagueProduction, War = w.War,
                    Percentile = Math.Max(1, Math.Min(99, (int)Math.Round(50 + (w.Production - w.LeagueProduction) / Math.Max(1, w.LeagueProduction) * 100))),
                });
            var weakest = b.WeakPositions.FirstOrDefault();
            if (weakest != null)
            {
                // 기준 = 그 포지션 주전(결산 보고의 선수) - 지명타자처럼 여러 포지션이 겹치는 자리도 그 선수 OVR과 비교한다
                var starter = team.ReservePlayers.FirstOrDefault(p => p.Template.PlayerName == weakest.PlayerName && SamePosition(p, weakest.Position))
                              ?? team.Roster.Where(p => SamePosition(p, weakest.Position)).OrderByDescending(p => p.BaseOverall).FirstOrDefault();
                int baseOvr = starter?.BaseOverall ?? 0;
                var pool = league.FreeAgents.Where(p => SamePosition(p, weakest.Position)).Select(p => (p, src: "FA 시장", fa: true))
                    .Concat(league.Teams.Values.Where(t => !t.IsUserTeam).SelectMany(t => t.Roster.Where(p => SamePosition(p, weakest.Position)).Select(p => (p, src: t.DisplayName, fa: false))))
                    .Where(x => x.p.BaseOverall > baseOvr)
                    .OrderByDescending(x => x.fa).ThenByDescending(x => x.p.BaseOverall).ThenBy(x => x.p.Salary).ThenBy(x => x.p.InstanceId, StringComparer.Ordinal)
                    .ToList();
                // FA 우선 2명 + 트레이드 1명(없으면 남는 쪽으로 채움)
                var picks = pool.Where(x => x.fa).Take(2).Concat(pool.Where(x => !x.fa).Take(TopCandidates)).Take(TopCandidates).ToList();
                if (picks.Count < TopCandidates) picks = picks.Concat(pool.Where(x => !picks.Contains(x))).Take(TopCandidates).ToList();
                foreach (var x in picks.OrderByDescending(x => x.p.BaseOverall))
                    b.Candidates.Add(new GMStaffCandidate
                    {
                        Player = x.p, Source = x.src, IsFreeAgent = x.fa, OvrGain = x.p.BaseOverall - baseOvr,
                        Line = $"{x.p.Template.PlayerName} · {GMFrontOffice.PositionLabel(x.p.Position)} · {x.p.Age}세 · OVR {x.p.BaseOverall}(+{x.p.BaseOverall - baseOvr}) · " +
                               $"{(x.fa ? $"FA 요구 {GMStoveLeagueMarket.DemandLabel(x.p, GMFrontOffice.Ensure(league).Difficulty, true)}" : $"{x.src} 트레이드 · 연봉 {GMDiagnosticFormat.Short(x.p.Salary)}")}",
                    });
                b.DataHeadline = $"가장 취약한 포지션: {weakest.PositionLabel}({weakest.PlayerName}) - 생산성 {weakest.Production:0} · 10구단 같은 포지션 {weakest.LeagueProduction:0} · 백분위 {weakest.Percentile}%";
            }
            else b.DataHeadline = "포지션 생산성 자료가 아직 없습니다.";
            b.DataReliability = sm.Confidence == GMReportConfidence.High ? 90 : sm.Confidence == GMReportConfidence.Medium ? 70 : 45;

            // ② 재무팀장
            var budget = GMFrontOffice.Budget(league, team);
            b.Payroll = team.Payroll;
            b.Cap = team.PayrollCap;
            b.CapRoom = b.Cap - b.Payroll;
            b.Budget = team.Budget;
            b.MoneyForFA = budget.MoneyForFA;
            b.CapUsage = b.Cap > 0 ? b.Payroll / (float)b.Cap : 0f;
            b.TaxRisk = RiskOf(b.CapUsage);
            b.TaxLine = $"경쟁균형세 위험도 {RiskLabel(b.TaxRisk)} - 페이롤 {GMDiagnosticFormat.Short(b.Payroll)} / 샐러리캡 {GMDiagnosticFormat.Short(b.Cap)}({b.CapUsage * 100:0}%)" +
                        (b.TaxRisk == GMRiskLevel.Danger ? $" · 초과 {GMDiagnosticFormat.Short(-b.CapRoom)} - 경쟁균형세 부과 대상" : b.TaxRisk == GMRiskLevel.Caution ? " · 캡 90% 이상 - 고액 계약 신중" : " · 여유 있음");
            b.FinanceHeadline = $"운영 예산 {GMDiagnosticFormat.Short(b.Budget)} · FA 가용 {GMDiagnosticFormat.Short(b.MoneyForFA)} · 캡 여유 {GMDiagnosticFormat.Short(b.CapRoom)}";
            var reports = sm.Players.Where(r => r.Player != null && team.Roster.Contains(r.Player)).ToList();
            foreach (var r in reports.OrderByDescending(r => r.Efficiency.Value).ThenBy(r => r.Player.InstanceId, StringComparer.Ordinal).Take(ValueRows))
                b.BestValue.Add(new GMStaffValueRow { Player = r.Player, Ratio = r.Efficiency.Value, Line = $"{r.Player.Template.PlayerName} · 연봉 {GMDiagnosticFormat.Short(r.Player.Salary)} · WAR {r.War.ValueText} · 효율 {r.Efficiency.ValueText}" });
            foreach (var r in reports.Where(r => r.Player.Salary > Player.MinSalary * 2).OrderBy(r => r.Efficiency.Value).ThenBy(r => r.Player.InstanceId, StringComparer.Ordinal).Take(ValueRows))
                b.WorstValue.Add(new GMStaffValueRow { Player = r.Player, Ratio = r.Efficiency.Value, Line = $"{r.Player.Template.PlayerName} · 연봉 {GMDiagnosticFormat.Short(r.Player.Salary)} · WAR {r.War.ValueText} · 효율 {r.Efficiency.ValueText}" });
            b.FinanceReliability = 95;

            // ③ 선수관리팀장
            b.Trust = team.LockerRoomTrust;
            b.AvgLoyalty = GMLockerRoom.AverageLoyalty(team);
            foreach (var p in team.ReservePlayers.Where(p => p?.Template != null && p.Loyalty < LoyaltyRiskLine).OrderBy(p => p.Loyalty).ThenByDescending(p => p.BaseOverall).Take(RiskRows))
                b.LoyaltyRisks.Add(new GMStaffRiskRow
                {
                    Player = p, Loyalty = p.Loyalty,
                    Reason = p.ContractYears <= 0 ? "계약 만료 - 이탈 위험" : p.Loyalty < 25 ? "이적 요청 가능" : p.PersonalMorale < 50 ? "만족도 저하" : "충성도 낮음",
                });
            var seen = new HashSet<string>();
            foreach (var p in team.Roster.Where(p => p?.Template != null))
                foreach (var (kind, partner) in GMPlayerBonds.For(team, p))
                {
                    if (partner?.Template == null) continue;
                    string key = string.CompareOrdinal(p.InstanceId, partner.InstanceId) < 0 ? p.InstanceId + partner.InstanceId : partner.InstanceId + p.InstanceId;
                    if (!seen.Add(key + kind)) continue;
                    bool atRisk = p.ContractYears <= 1 || partner.ContractYears <= 1 || p.Loyalty < LoyaltyRiskLine || partner.Loyalty < LoyaltyRiskLine;
                    int drop = (kind == GMBondKind.Mentor ? 2 : 3) + (atRisk ? 2 : 0);
                    b.Bonds.Add(new GMStaffBondRow
                    {
                        A = p, B = partner, Kind = kind, TrustDrop = drop,
                        Line = $"{GMPlayerBonds.KindLabel(kind)} {p.Template.PlayerName} - {partner.Template.PlayerName} · 파괴 시 신뢰도 -{drop}{(atRisk ? " · 계약/충성도 위험" : "")}",
                    });
                }
            var sorted = b.Bonds.OrderByDescending(x => x.TrustDrop).ThenBy(x => x.Line, StringComparer.Ordinal).Take(BondRows).ToList();
            b.Bonds.Clear();
            b.Bonds.AddRange(sorted);
            b.RelationsHeadline = $"선수단의 단장 신뢰도 {b.Trust} · 평균 구단 충성도 {b.AvgLoyalty:0} · 위험군(충성도 {LoyaltyRiskLine} 미만) {team.ReservePlayers.Count(p => p.Loyalty < LoyaltyRiskLine)}명";
            b.RelationsReliability = Math.Min(95, 60 + (int)Math.Round(30.0 * Math.Min(1, league.GamesPlayed / (double)GMLiveSeasonSimulator.SeasonGames)) + (GMFrontOffice.Ensure(league).LastSeasonReview.Count > 0 ? 5 : 0));

            // 운영팀장 정리
            var a = GMAssistant.ProfileFor(league);
            string first = b.TaxRisk == GMRiskLevel.Danger ? "샐러리캡 초과(경쟁균형세)" : b.LoyaltyRisks.Count > 0 ? $"충성도 위험군 {b.LoyaltyRisks.Count}명" : weakest != null ? $"{weakest.PositionLabel} 보강" : "특이 사항 없음";
            b.AssistantLine = $"{a.Plate}: \"세 팀장의 보고를 정리했습니다. 가장 급한 건 {first}입니다.\"";
            return b;
        }
    }
}
