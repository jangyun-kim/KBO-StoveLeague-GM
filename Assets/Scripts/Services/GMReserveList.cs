using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-10] 스토브리그 타임라인 고도화 - 원 소속 우선 협상 · 11월 25일 보류명단(KBO 규약 뼈대).
    ///   ① 우선 협상(유저): 시즌이 끝나 연도가 넘어갈 때 내 구단 계약 만료자는 바로 FA로 나가지 않고 우선 협상 명단에 남는다.
    ///      [연봉·재계약]에서 재계약하면 잔류, [우선 협상 마감](또는 정규시즌 개막 준비)에서 남은 선수는 FA 공시된다. AI 구단 만료자는 바로 FA.
    ///   ② 보류명단 제출(AI): 소속 인원(1군 + 퓨처스 + 육성 슬롯) 68명 한도를 맞추고, 연봉 대비 OVR이 심각하게 낮은 베테랑 · 성장 한계에 달한
    ///      유망주를 보류명단에서 제외(방출) - 자유계약(보상 없음)으로 FA 풀에 푼다. 구단당 최소 1명 · 최대 3명, 보류 가치(OVR · 잠재력 - 연봉) 최하위부터.
    /// </summary>
    public static class GMReserveList
    {
        public const int ReserveLimit = 68;
        public const int MinReleases = 1, MaxReleases = 3;
        public const int VeteranAge = 33, MinVeteranSalary = 20000; // 2억 원
        public const double OverpaidRatio = 1.6;                    // 연봉 ÷ 성적 기준 연봉
        public const int ProspectCeilingAge = 24, ProspectCeilingGap = 3;

        public static int ReserveCount(GMTeamState t) => t == null ? 0 : t.Roster.Count + t.Futures.Count + t.DevelopmentSlots;

        /// <summary>
        /// 보류 가치(가성비 · OVR) - OVR과 잠재력 반반에서 연봉 2억당 1점을 뺀다. 낮을수록 보류 제외 1순위.
        /// (연봉 나눗셈 방식은 최고 연봉 스타를 최하위로 잘못 골랐다 - 하네스 실측)
        /// </summary>
        public static double ValueScore(Player p) => p.BaseOverall * 0.5 + p.Potential * 0.5 - p.Salary / 20000.0;

        /// <summary>연봉 대비 성적 미달 베테랑(33세 이상 · 연봉 2억 이상 · 연봉 ÷ 성적 기준 연봉 ≥ 1.6).</summary>
        public static bool IsOverpaidVeteran(Player p)
        {
            if (p?.Template == null || p.Age < VeteranAge || p.Salary < MinVeteranSalary) return false;
            int fair = Player.ComputeSalary(p.BaseOverall, p.EgoLevel, p.CareerAwardIds?.Count(Player.IsMajorAward) ?? 0); // 자존심 · 수상 경력까지 반영한 성적 기준 연봉(수상 직후 인상분을 저효율로 오판하지 않게)
            return p.Salary >= fair * OverpaidRatio;
        }

        /// <summary>성장 한계 유망주(퓨처스 · 24세 이상 · 잠재력 - OVR ≤ 3).</summary>
        public static bool IsStalledProspect(GMTeamState t, Player p) => t.Futures.Contains(p) && p.Age >= ProspectCeilingAge && p.Potential - p.BaseOverall <= ProspectCeilingGap;

        /// <summary>보류 제외 1건.</summary>
        public sealed class Release
        {
            public string TeamCode, Reason;
            public Player Player;
        }

        /// <summary>보류명단 제외 후보(사유 포함, 우선순위 순) - 68명 초과분 → 고액 저효율 베테랑 → 성장 한계 유망주 → 가성비 최하위.</summary>
        public static List<Release> Plan(GMLeagueState league, GMTeamState team)
        {
            var plan = new List<Release>();
            if (team == null) return plan;
            var pool = team.ReservePlayers.Where(p => p?.Template != null && !p.IsCaptain
                                                     && !league.FASignedThisYear.Contains(p.InstanceId) && !league.RookiesThisYear.Contains(p.InstanceId)).ToList();
            void Add(Player p, string reason) { if (p != null && plan.All(x => x.Player != p)) plan.Add(new Release { TeamCode = team.TeamCode, Player = p, Reason = reason }); }
            int over = ReserveCount(team) - ReserveLimit;
            foreach (var p in pool.OrderBy(ValueScore).Take(Math.Max(0, over))) Add(p, $"소속 {ReserveLimit}명 한도 초과");
            foreach (var p in pool.Where(IsOverpaidVeteran).OrderBy(ValueScore)) Add(p, "연봉 대비 성적 미달 베테랑");
            foreach (var p in pool.Where(p => IsStalledProspect(team, p)).OrderBy(ValueScore)) Add(p, "성장 한계 유망주");
            foreach (var p in pool.OrderBy(ValueScore).ThenBy(p => p.BaseOverall)) { if (plan.Count >= MinReleases) break; Add(p, "가성비 · OVR 최하위권"); }
            return plan.Take(Math.Max(MaxReleases, Math.Max(0, over))).ToList();
        }

        /// <summary>
        /// 11월 25일 보류명단 제출 - AI 구단(내 구단 제외)이 Plan대로 방출한다. 1군이 20명 미만으로 줄지 않게 지킨다. 방출 선수는 자유계약(원 소속 · 보상 없음) FA.
        /// </summary>
        public static List<Release> SubmitAiReserveLists(GMLeagueState league)
        {
            var done = new List<Release>();
            if (league == null) return done;
            foreach (var team in league.Teams.Values.Where(t => !t.IsUserTeam))
            {
                foreach (var r in Plan(league, team))
                {
                    if (!team.Futures.Contains(r.Player) && team.Roster.Count <= GMStoveLeagueMarket.MinRosterAfterRelease) continue;
                    ReleaseToMarket(league, team, r.Player);
                    done.Add(r);
                }
            }
            league.LastReserveReleases.Clear();
            league.LastReserveReleases.AddRange(done);
            if (done.Count > 0)
                league.AddNews(new GMNewsItem
                {
                    GameIndex = league.GamesPlayed, DateLabel = $"11/25/{league.SeasonYear - 1}", Kind = GMNewsKind.Trade, IsMajor = true,
                    Title = $"보류명단 제출 - 자유계약 방출 {done.Count}명",
                    Body = string.Join(" · ", done.Take(8).Select(r => $"{KBOManager.UI.CompyaUiKit.ShortName(NameAliasTable.ToTeam(r.TeamCode))} {r.Player.Template.PlayerName}({r.Reason})")) + (done.Count > 8 ? " 외" : ""),
                });
            return done;
        }

        private static void ReleaseToMarket(GMLeagueState league, GMTeamState team, Player p)
        {
            if (!team.Roster.Remove(p)) team.Futures.Remove(p);
            string id = p.InstanceId;
            team.Lineup.Starters.RemoveAll(x => x.InstanceId == id);
            team.Lineup.Roles.RemoveAll(x => x.InstanceId == id);
            team.Lineup.PitcherSlots.RemoveAll(x => x.InstanceId == id);
            GMCheerleaderRoster.RefreshDedications(team);
            p.IsCaptain = false;
            p.ContractYears = 0;
            league.FreeAgents.Add(p);
            league.FAOrigins.Remove(id); // 자유계약 = 보상 없음
        }

        // ================================================================== 우선 협상

        /// <summary>연도 전환 직후 - 내 구단 계약 만료자를 우선 협상 명단에 올린다(FA 공시 보류). 올린 선수 수.</summary>
        public static int OpenPriorityNegotiation(GMLeagueState league)
        {
            if (league?.UserTeam == null) return 0;
            league.PriorityNegotiationIds.Clear();
            league.PriorityNegotiationIds.AddRange(league.UserTeam.ReservePlayers.Where(p => p.ContractYears <= 0).Select(p => p.InstanceId));
            return league.PriorityNegotiationIds.Count;
        }

        public static bool IsPriorityOpen(GMLeagueState league) => league != null && league.PriorityNegotiationIds.Count > 0;

        /// <summary>우선 협상 대상 중 아직 재계약하지 않은 선수.</summary>
        public static List<Player> PendingPriority(GMLeagueState league) =>
            league?.UserTeam == null ? new List<Player>() :
                league.UserTeam.ReservePlayers.Where(p => league.PriorityNegotiationIds.Contains(p.InstanceId) && p.ContractYears <= 0).ToList();

        /// <summary>[우선 협상 마감] - 재계약하지 않은 선수를 FA 공시(원 소속 = 내 구단)하고 명단을 닫는다. FA로 나간 선수.</summary>
        public static List<Player> ClosePriorityNegotiation(GMLeagueState league)
        {
            var gone = new List<Player>();
            if (!IsPriorityOpen(league)) return gone;
            var kept = league.UserTeam.ReservePlayers.Count(p => league.PriorityNegotiationIds.Contains(p.InstanceId) && p.ContractYears > 0);
            foreach (var p in PendingPriority(league))
                if (GMFaCompensation.DeclareFreeAgent(league, league.UserTeam, p) != null) gone.Add(p);
            league.PriorityNegotiationIds.Clear();
            league.AddNews(new GMNewsItem
            {
                GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Trade, IsUserTeam = true,
                Title = $"원 소속 우선 협상 마감 - 잔류 {kept}명 · FA {gone.Count}명",
                Body = gone.Count > 0 ? $"FA 시장으로: {string.Join(" · ", gone.Select(p => p.Template.PlayerName))}" : "계약 만료자 전원과 재계약했습니다.",
            });
            return gone;
        }
    }
}
