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
    /// [TASK-GM-15] 2차 드래프트(스토브리그 Turn 3 · 격년 홀수 해, KBO 2023 재도입 규정 단순화).
    ///   ① 보호 명단: 보류선수(1군 + 퓨처스) 중 자동 보호(1~3년 차 신인 · 당해 FA 계약자 · 외국인)를 뺀 선수에서 25인을 단장이 직접 고른다
    ///      (처음 열면 10대 가중치 추천 25인으로 채워 둔다). AI 구단은 같은 가중치(GMFaCompensation.ScorePlayer) 상위 25인을 묶는다.
    ///   ② 지명: 라운드 1~3, 양도금 1R 4억 · 2R 3억 · 3R 2억(지명 구단 → 원 소속 구단). 구단당 최대 3명 지명 · 최대 4명 유출.
    ///      단장은 Turn 3 동안 예산으로 타 구단 비보호 선수를 지명하고, Turn 3를 넘기면(또는 [AI 지명 진행]) 직전 시즌 하위 구단부터 AI가 니즈 점수로 지명한다.
    ///   ③ 짝수 해는 열리지 않는다(GMStoveTurns가 Turn 3을 건너뛴다).
    ///   게임 로스터 보정: 보류선수(약 40명)가 KBO 실제(60명 안팎)보다 적어 자동 보호를 빼면 후보가 25명 안팎이므로, 보호 상한 = min(25, 후보 - 5)로 비보호 최소 5명을 보장한다(ProtectLimit).
    ///   1~3년 차 판정: 당해 신인(RookiesThisYear) 또는 퓨처스 유망주 21세 이하(고졸 19세 입단 기준 1~3년 차).
    ///   1군 선수 나이는 로더 추정치(21세 + 카드 연수 - 2026 카드뿐인 현역은 21세)라 연차 판정에 쓰지 않는다.
    /// </summary>
    public static class GMSecondaryDraft
    {
        public const int ProtectSize = 25, MinExposed = 5, Rounds = 3, MaxLossPerTeam = 4, RookieMaxAge = 21;
        public const float AiMinScore = 38f;
        public static readonly long[] RoundFees = { 40000, 30000, 20000 }; // 만 원

        public static long FeeFor(int round) => RoundFees[Math.Max(1, Math.Min(Rounds, round)) - 1];

        public static bool IsDraftYear(int year) => GMStoveTurns.SecondDraftYear(year);

        public static bool IsHeld(GMLeagueState league) => league != null && GMFrontOffice.Ensure(league).SecondDraftHeldYear == league.SeasonYear;

        /// <summary>지명이 열려 있는지 - 홀수 해 · 개막 전 · 아직 시행 전 · (8 Turn 통제 중이면 Turn 3).</summary>
        public static bool IsOpen(GMLeagueState league) =>
            league != null && IsDraftYear(league.SeasonYear) && league.GamesPlayed == 0 && !IsHeld(league)
            && (!GMStoveTurns.IsGating(league) || GMStoveTurns.Current(league) == 3);

        public static string StatusText(GMLeagueState league)
        {
            if (league == null) return "";
            if (!IsDraftYear(league.SeasonYear)) return $"{league.SeasonYear}년은 2차 드래프트가 없는 해입니다(격년 · 홀수 해 시행) - 다음 시행 {league.SeasonYear + 1}년.";
            if (IsHeld(league)) return $"{league.SeasonYear} 2차 드래프트 종료 - 지명 {PicksThisYear(league).Count}건.";
            if (league.GamesPlayed > 0) return "정규시즌이 시작되어 2차 드래프트를 열 수 없습니다.";
            if (GMStoveTurns.IsGating(league) && GMStoveTurns.Current(league) < 3) return "Turn 3에서 열립니다.";
            return $"{league.SeasonYear} 2차 드래프트 진행 중 - 보호 {ProtectSize}인 지정 · 타 구단 비보호 선수 지명(양도금 1R 4억 · 2R 3억 · 3R 2억).";
        }

        // ================================================================== 보호 명단

        /// <summary>자동 보호 사유(없으면 null) - 1~3년 차 신인 · 당해 FA 계약자 · 외국인.</summary>
        public static string AutoProtectReason(GMLeagueState league, Player p)
        {
            if (p?.Template == null) return null;
            if (league != null && league.FASignedThisYear.Contains(p.InstanceId)) return "FA 계약자";
            if (GMFaCompensation.IsForeign(p)) return "외국인";
            if (league != null && (league.RookiesThisYear.Contains(p.InstanceId) || p.Age <= RookieMaxAge && league.Teams.Values.Any(t => t.Futures.Contains(p)))) return "1~3년 차";
            return null;
        }

        /// <summary>보호 명단 후보(자동 보호 제외 보류선수).</summary>
        public static List<Player> Candidates(GMLeagueState league, GMTeamState team) =>
            team == null ? new List<Player>() : team.ReservePlayers.Where(p => p?.Template != null && AutoProtectReason(league, p) == null).ToList();

        /// <summary>보호 상한 = min(25, 후보 - 비보호 최소 5명).</summary>
        public static int ProtectLimit(GMLeagueState league, GMTeamState team) => Math.Max(0, Math.Min(ProtectSize, Candidates(league, team).Count - MinExposed));

        /// <summary>10대 가중치 추천 보호 명단(상한까지, 점수순).</summary>
        public static List<Player> Recommended(GMLeagueState league, GMTeamState team)
        {
            var pool = team?.ReservePlayers.Where(p => p?.Template != null).ToList() ?? new List<Player>();
            if (team == null) return pool;
            bool winNow = GMFaCompensation.IsWinNow(league, team);
            return Candidates(league, team).Select(p => (p, s: GMFaCompensation.ScorePlayer(league, team.TeamCode, pool, p, winNow, 0).Score))
                .OrderByDescending(x => x.s).ThenBy(x => x.p.InstanceId, StringComparer.Ordinal).Take(ProtectLimit(league, team)).Select(x => x.p).ToList();
        }

        /// <summary>내 구단 보호 명단 - 올해 처음이면 추천 25인으로 채운다. 떠난 선수 · 자동 보호로 바뀐 선수는 뺀다.</summary>
        public static List<string> UserProtectedIds(GMLeagueState league)
        {
            var fo = GMFrontOffice.Ensure(league);
            var team = league.UserTeam;
            if (team == null) return fo.SecondDraftProtectedIds;
            if (fo.SecondDraftProtectYear != league.SeasonYear)
            {
                fo.SecondDraftProtectedIds.Clear();
                fo.SecondDraftProtectedIds.AddRange(Recommended(league, team).Select(p => p.InstanceId));
                fo.SecondDraftProtectYear = league.SeasonYear;
            }
            var valid = new HashSet<string>(Candidates(league, team).Select(p => p.InstanceId));
            fo.SecondDraftProtectedIds.RemoveAll(id => !valid.Contains(id));
            int limit = ProtectLimit(league, team);
            if (fo.SecondDraftProtectedIds.Count > limit) fo.SecondDraftProtectedIds.RemoveRange(limit, fo.SecondDraftProtectedIds.Count - limit);
            return fo.SecondDraftProtectedIds;
        }

        /// <summary>보호 체크박스 토글 - 25인 상한. 시행 후에는 바꿀 수 없다.</summary>
        public static bool ToggleProtect(GMLeagueState league, Player p, out string message)
        {
            message = "";
            var team = league?.UserTeam;
            if (team == null || p?.Template == null || !team.ReservePlayers.Contains(p)) { message = "내 구단 선수를 고르십시오."; return false; }
            if (IsHeld(league)) { message = "2차 드래프트가 끝나 보호 명단을 바꿀 수 없습니다."; return false; }
            string auto = AutoProtectReason(league, p);
            if (auto != null) { message = $"{p.Template.PlayerName}은(는) 자동 보호({auto}) 대상입니다."; return false; }
            var ids = UserProtectedIds(league);
            int limit = ProtectLimit(league, team);
            if (ids.Remove(p.InstanceId)) { message = $"{p.Template.PlayerName} 보호 해제 - 보호 {ids.Count}/{limit} · 타 구단 지명 대상"; return true; }
            if (ids.Count >= limit) { message = $"보호 명단 {limit}인이 가득 찼습니다(최대 {ProtectSize}인 · 비보호 최소 {MinExposed}명) - 먼저 다른 선수를 해제하십시오."; return false; }
            ids.Add(p.InstanceId);
            message = $"{p.Template.PlayerName} 보호 - 보호 {ids.Count}/{limit}";
            return true;
        }

        public static HashSet<string> ProtectedIds(GMLeagueState league, GMTeamState team) =>
            team == null ? new HashSet<string>() : team == league.UserTeam ? new HashSet<string>(UserProtectedIds(league)) : new HashSet<string>(Recommended(league, team).Select(p => p.InstanceId));

        /// <summary>지명 대상(비보호) 선수.</summary>
        public static List<Player> Exposed(GMLeagueState league, GMTeamState team)
        {
            var prot = ProtectedIds(league, team);
            return Candidates(league, team).Where(p => !prot.Contains(p.InstanceId)).ToList();
        }

        public static bool IsExposed(GMLeagueState league, GMTeamState team, Player p) => p != null && Exposed(league, team).Contains(p);

        // ================================================================== 지명

        public static List<GMSecondDraftPick> PicksThisYear(GMLeagueState league) =>
            GMFrontOffice.Ensure(league).SecondDraftPicks.Where(x => x.Year == league.SeasonYear).ToList();

        public static int PicksBy(GMLeagueState league, string code) => PicksThisYear(league).Count(x => x.PickerCode == code);
        public static int LossesOf(GMLeagueState league, string code) => PicksThisYear(league).Count(x => x.FromCode == code);

        public static int NextRound(GMLeagueState league, string code) => PicksBy(league, code) + 1;

        /// <summary>내 구단이 지명할 수 있는 타 구단 비보호 선수(유출 한도가 남은 구단만) - OVR순.</summary>
        public static List<(Player player, GMTeamState from)> Pool(GMLeagueState league, string pickerCode)
        {
            var list = new List<(Player, GMTeamState)>();
            if (league == null) return list;
            foreach (var team in league.Teams.Values.Where(t => t.TeamCode != pickerCode).OrderBy(t => t.TeamCode, StringComparer.Ordinal))
            {
                if (LossesOf(league, team.TeamCode) >= MaxLossPerTeam) continue;
                foreach (var p in Exposed(league, team)) list.Add((p, team));
            }
            return list.OrderByDescending(x => x.Item1.BaseOverall).ThenBy(x => x.Item1.InstanceId, StringComparer.Ordinal).ToList();
        }

        /// <summary>단장 지명 - 예산(양도금) 차감 · 원 소속 가산 · 1군(자리 없으면 퓨처스) 합류.</summary>
        public static GMSecondDraftPick UserPick(GMLeagueState league, Player p, out string message)
        {
            message = "";
            var user = league?.UserTeam;
            if (user == null || p?.Template == null) { message = "지명할 선수를 고르십시오."; return null; }
            if (!IsOpen(league)) { message = StatusText(league); return null; }
            var from = league.Teams.Values.FirstOrDefault(t => t != user && t.ReservePlayers.Contains(p));
            if (from == null) { message = "타 구단 선수만 지명할 수 있습니다."; return null; }
            if (!IsExposed(league, from, p)) { message = $"{p.Template.PlayerName}은(는) {NameAliasTable.DisplayTeamName(from.TeamCode)} 보호 선수입니다."; return null; }
            int round = NextRound(league, user.TeamCode);
            if (round > Rounds) { message = $"구단당 최대 {Rounds}명까지 지명할 수 있습니다."; return null; }
            if (LossesOf(league, from.TeamCode) >= MaxLossPerTeam) { message = $"{NameAliasTable.DisplayTeamName(from.TeamCode)}은(는) 유출 한도 {MaxLossPerTeam}명을 채웠습니다."; return null; }
            long fee = FeeFor(round);
            if (user.Budget < fee) { message = $"예산 부족 - {round}R 양도금 {GMDiagnosticFormat.Won(fee)} · 가용 {GMDiagnosticFormat.Won(user.Budget)}"; return null; }
            if (user.Roster.Count >= GMRosterTiers.FirstTeamMax && user.Futures.Count >= GMRosterTiers.FuturesMax) { message = "1군 · 퓨처스가 모두 가득 찼습니다."; return null; }
            var pick = Transfer(league, user, from, p, round, fee);
            message = $"{round}R 지명 - {p.Template.PlayerName}({NameAliasTable.DisplayTeamName(from.TeamCode)} · OVR {pick.Ovr}) · 양도금 {GMDiagnosticFormat.Won(fee)} 지급 · 잔여 예산 {GMDiagnosticFormat.Won(user.Budget)}";
            return pick;
        }

        private static GMSecondDraftPick Transfer(GMLeagueState league, GMTeamState picker, GMTeamState from, Player p, int round, long fee)
        {
            if (from.Roster.Contains(p)) GMRosterTiers.Detach(from, p);
            else from.Futures.Remove(p);
            p.IsCaptain = false;
            p.LoyaltyRaw = -1; // 새 구단 충성도는 성향 기본값부터
            if (picker.Roster.Count < GMRosterTiers.FirstTeamMax) picker.Roster.Add(p);
            else picker.Futures.Add(p);
            picker.Budget -= fee;
            from.Budget += fee;
            var pick = new GMSecondDraftPick
            {
                Year = league.SeasonYear, Round = round, PickerCode = picker.TeamCode, FromCode = from.TeamCode,
                PlayerId = p.InstanceId, PlayerName = p.Template.PlayerName, Fee = fee, Ovr = p.BaseOverall,
            };
            GMFrontOffice.Ensure(league).SecondDraftPicks.Add(pick);
            if (from == league.UserTeam) GMFuturesMeeting.Prune(league);
            if (picker == league.UserTeam || from == league.UserTeam)
                league.AddNews(new GMNewsItem
                {
                    GameIndex = 0, DateLabel = $"{league.SeasonYear} 2차 드래프트", Kind = GMNewsKind.Trade, IsUserTeam = true,
                    Title = picker == league.UserTeam ? $"[2차 드래프트] {round}R {p.Template.PlayerName} 지명" : $"[2차 드래프트] {p.Template.PlayerName}, {NameAliasTable.DisplayTeamName(picker.TeamCode)}행",
                    Body = $"{NameAliasTable.DisplayTeamName(picker.TeamCode)} {round}라운드 지명 · {NameAliasTable.DisplayTeamName(from.TeamCode)}에 양도금 {GMDiagnosticFormat.Won(fee)}.",
                });
            return pick;
        }

        /// <summary>AI 지명 순서 - 직전 시즌 하위 구단부터(이력이 없으면 1군 평균 OVR 낮은 순).</summary>
        public static List<GMTeamState> DraftOrder(GMLeagueState league)
        {
            var fo = GMFrontOffice.Ensure(league);
            int lastYear = league.SeasonYear - 1;
            int RankOf(GMTeamState t) => fo.History.FirstOrDefault(h => h.TeamCode == t.TeamCode && h.Year == lastYear)?.Rank ?? 0;
            return league.Teams.Values
                .OrderByDescending(RankOf)
                .ThenBy(t => t.Roster.Count == 0 ? 0 : t.Roster.Average(p => p.BaseOverall))
                .ThenBy(t => t.TeamCode, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// 2차 드래프트 마감 - AI 구단 라운드별 지명(니즈 점수 = 받는 구단 보류선수 기준 10대 가중치 점수 ≥ 38 · 예산 · 유출 한도 · 정원)을 진행하고 시행 처리한다.
        /// 내 구단은 이미 한 지명만 반영된다. 반환 = 이번 마감에서 AI가 한 지명.
        /// </summary>
        public static List<GMSecondDraftPick> Conclude(GMLeagueState league)
        {
            var picks = new List<GMSecondDraftPick>();
            if (league == null || !IsDraftYear(league.SeasonYear) || IsHeld(league) || league.GamesPlayed > 0) return picks;
            UserProtectedIds(league); // 내 보호 명단 확정(처음이면 추천 25인)
            var order = DraftOrder(league).Where(t => t != league.UserTeam).ToList();
            // 구단별 비보호 명단은 마감 시점에 한 번 확정한다(지명으로 보류선수가 바뀌어도 보호 명단은 그대로)
            var exposed = league.Teams.Values.ToDictionary(t => t.TeamCode, t => Exposed(league, t));
            for (int round = 1; round <= Rounds; round++)
            {
                foreach (var team in order)
                {
                    if (PicksBy(league, team.TeamCode) >= round) continue;
                    long fee = FeeFor(round);
                    if (team.Budget < fee || team.Roster.Count >= GMRosterTiers.FirstTeamMax && team.Futures.Count >= GMRosterTiers.FuturesMax) continue;
                    var pool = team.ReservePlayers.Where(p => p?.Template != null).ToList();
                    bool winNow = GMFaCompensation.IsWinNow(league, team);
                    var best = exposed.Where(e => e.Key != team.TeamCode && LossesOf(league, e.Key) < MaxLossPerTeam)
                        .SelectMany(e => e.Value.Select(p => (p, from: e.Key)))
                        .Where(x => league.Teams[x.from].ReservePlayers.Contains(x.p))
                        .Select(x => (x.p, x.from, s: GMFaCompensation.ScorePlayer(league, team.TeamCode, pool.Concat(new[] { x.p }).ToList(), x.p, winNow, 0).Score))
                        .OrderByDescending(x => x.s).ThenBy(x => x.p.InstanceId, StringComparer.Ordinal).FirstOrDefault();
                    if (best.p == null || best.s < AiMinScore) continue;
                    picks.Add(Transfer(league, team, league.Teams[best.from], best.p, round, fee));
                }
            }
            GMFrontOffice.Ensure(league).SecondDraftHeldYear = league.SeasonYear;
            var all = PicksThisYear(league);
            var user = league.UserTeam;
            long net = user == null ? 0 : all.Where(x => x.FromCode == user.TeamCode).Sum(x => x.Fee) - all.Where(x => x.PickerCode == user.TeamCode).Sum(x => x.Fee);
            league.AddNews(new GMNewsItem
            {
                GameIndex = 0, DateLabel = $"{league.SeasonYear} 2차 드래프트", Kind = GMNewsKind.Trade, IsUserTeam = true, IsMajor = true,
                Title = $"{league.SeasonYear} 2차 드래프트 종료 - {all.Count}명 이동",
                Body = (user == null ? "" : $"내 구단 지명 {all.Count(x => x.PickerCode == user.TeamCode)}명 · 유출 {all.Count(x => x.FromCode == user.TeamCode)}명 · 양도금 수지 {(net >= 0 ? "+" : "")}{GMDiagnosticFormat.Won(net)}. ")
                       + string.Join(" · ", all.Take(8).Select(x => $"{x.Round}R {x.PlayerName}({NameAliasTable.DisplayTeamName(x.FromCode)}→{NameAliasTable.DisplayTeamName(x.PickerCode)})")),
            });
            return picks;
        }
    }
}
