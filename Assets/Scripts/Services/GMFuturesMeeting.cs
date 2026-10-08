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
    /// [TASK-GM-15] 육성 회의실(스토브리그 Turn 6) - 퓨처스 핵심 유망주의 훈련 방향 · 1군 콜업/이관(스왑) · 베테랑 멘토링(최대 3쌍).
    ///   - 잠재력 범위(PotentialMin~Max)와 성장 유형(조기 완성형 · 표준형 · 대기만성형)은 나이 · 기존 잠재력(Player.Potential) · 선수 해시로 정한다(같은 선수는 항상 같은 값).
    ///   - 연간 성장(연도 전환 AdvanceToNextSeasonYear) = 기본(잠재력 여유 × 유형 비율) + 훈련 방향 적합도(맞음 +1 · 어긋남 -1 · 균형 0)
    ///     + 멘토(더그아웃 리더) +1 · 변동 하한 0(안정성) + 연도 해시 변동(-1~+1), 0 ~ (상한 - 현재 OVR)로 자른다.
    ///   - 성장은 퓨처스 유망주 보정치(Player.ProspectStatShift - 원본 카드 대비 전 세부 스탯 이동)로 반영한다. 능력치는 여전히 BaseOverall 하나다(강화 · 각성 미사용).
    ///   - 대상: 퓨처스 풀 전원(AI 구단은 균형형 · 멘토 없음) + 내 구단 육성 계획이 있는 1군 콜업 유망주(27세 이하).
    /// </summary>
    public static class GMFuturesMeeting
    {
        public const int MaxMentorSlots = 3, MaxGrowthAge = 27;

        public static readonly GMTrainingFocus[] BatterFocuses = { GMTrainingFocus.Balanced, GMTrainingFocus.Contact, GMTrainingFocus.Power, GMTrainingFocus.DefenseSpeed };
        public static readonly GMTrainingFocus[] PitcherFocuses = { GMTrainingFocus.Balanced, GMTrainingFocus.Control, GMTrainingFocus.Stuff, GMTrainingFocus.Stamina };

        public static GMTrainingFocus[] FocusesFor(Player p) => p != null && p.IsPitcher ? PitcherFocuses : BatterFocuses;

        public static string FocusLabel(GMTrainingFocus f)
        {
            switch (f)
            {
                case GMTrainingFocus.Contact: return "컨택형";
                case GMTrainingFocus.Power: return "파워형";
                case GMTrainingFocus.DefenseSpeed: return "수비·주루형";
                case GMTrainingFocus.Control: return "제구형";
                case GMTrainingFocus.Stuff: return "구위형";
                case GMTrainingFocus.Stamina: return "체력형";
                default: return "균형형";
            }
        }

        // ================================================================== 잠재력 · 성장 유형

        public enum GrowthType { Early = 0, Standard = 1, LateBloomer = 2 }

        public static string GrowthTypeLabel(GrowthType t) => t == GrowthType.Early ? "조기 완성형" : t == GrowthType.LateBloomer ? "대기만성형" : "표준형";

        public static GrowthType TypeOf(Player p)
        {
            if (p?.Template == null) return GrowthType.Standard;
            int h = GMFrontOffice.Hash($"{p.Template.RealPlayerId}_growth") % 10;
            if (p.Age <= 21) return h < 4 ? GrowthType.LateBloomer : h < 7 ? GrowthType.Standard : GrowthType.Early;
            return h < 2 ? GrowthType.LateBloomer : h < 6 ? GrowthType.Standard : GrowthType.Early;
        }

        /// <summary>잠재력 상한 = Player.Potential + 유형 보정(대기만성 +3 · 조기 -2), 현재 OVR 이상 · 99 이하.</summary>
        public static int PotentialMax(Player p)
        {
            if (p?.Template == null) return 0;
            var t = TypeOf(p);
            int max = p.Potential + (t == GrowthType.LateBloomer ? 3 : t == GrowthType.Early ? -2 : 0);
            return Math.Min(99, Math.Max(p.BaseOverall, max));
        }

        /// <summary>잠재력 하한 = 현재 OVR + 여유분의 1/3(최소한 도달할 것으로 보는 값).</summary>
        public static int PotentialMin(Player p) => p?.Template == null ? 0 : p.BaseOverall + (PotentialMax(p) - p.BaseOverall) / 3;

        public static double TypeRate(GrowthType t) => t == GrowthType.Early ? 0.45 : t == GrowthType.LateBloomer ? 0.25 : 0.35;

        // ================================================================== 계획(훈련 방향 · 멘토)

        public static GMFuturesPlan PlanOf(GMLeagueState league, Player p) =>
            p == null || league == null ? null : GMFrontOffice.Ensure(league).FuturesPlans.FirstOrDefault(x => x.PlayerId == p.InstanceId);

        private static GMFuturesPlan EnsurePlan(GMLeagueState league, Player p)
        {
            var plan = PlanOf(league, p);
            if (plan != null) return plan;
            plan = new GMFuturesPlan { PlayerId = p.InstanceId };
            GMFrontOffice.Ensure(league).FuturesPlans.Add(plan);
            return plan;
        }

        public static GMTrainingFocus FocusOf(GMLeagueState league, Player p) => PlanOf(league, p)?.Focus ?? GMTrainingFocus.Balanced;

        /// <summary>훈련 방향 지정(타자 · 투수에 맞는 방향만).</summary>
        public static bool SetFocus(GMLeagueState league, Player p, GMTrainingFocus focus, out string message)
        {
            message = "";
            var team = league?.UserTeam;
            if (team == null || p?.Template == null || !team.ReservePlayers.Contains(p)) { message = "내 구단 선수를 고르십시오."; return false; }
            if (!FocusesFor(p).Contains(focus)) { message = $"{(p.IsPitcher ? "투수" : "타자")}에게 맞지 않는 훈련 방향입니다."; return false; }
            EnsurePlan(league, p).Focus = focus;
            message = $"{p.Template.PlayerName} 훈련 방향 → {FocusLabel(focus)} · 적합도 {FitLabel(p, focus)} · 예상 성장 +{ExpectedGrowth(league, p)}";
            return true;
        }

        /// <summary>훈련 방향 적합도 - 선수의 강점과 같은 방향 +1 · 어긋나면 -1 · 균형형 0.</summary>
        public static int FocusFit(Player p, GMTrainingFocus f)
        {
            if (p?.Template == null || f == GMTrainingFocus.Balanced) return 0;
            if (p.IsPitcher)
            {
                var s = p.Template.PitcherStats;
                switch (f)
                {
                    case GMTrainingFocus.Control: return s.Control >= s.Stuff ? 1 : -1;
                    case GMTrainingFocus.Stuff: return s.Stuff >= s.Control ? 1 : -1;
                    case GMTrainingFocus.Stamina: return p.Template.PitcherRole == PitcherRole.StartingPitcher ? 1 : -1;
                    default: return -1;
                }
            }
            var b = p.Template.BatterStats;
            switch (f)
            {
                case GMTrainingFocus.Contact: return b.Contact >= b.Power ? 1 : -1;
                case GMTrainingFocus.Power: return b.Power >= b.Contact ? 1 : -1;
                case GMTrainingFocus.DefenseSpeed: return Math.Max(b.Defense, b.Speed) >= Math.Max(b.Contact, b.Power) ? 1 : -1;
                default: return -1;
            }
        }

        public static string FitLabel(Player p, GMTrainingFocus f)
        {
            int fit = FocusFit(p, f);
            return fit > 0 ? "적합(+1)" : fit < 0 ? "부적합(-1)" : "균형(0)";
        }

        // ================================================================== 멘토링

        public static bool IsMentorCandidate(GMTeamState team, Player p) => team != null && p?.Template != null && team.Roster.Contains(p) && p.RoleArchetype == LockerRoomRole.DugoutLeader;

        public static List<Player> MentorCandidates(GMTeamState team) =>
            team == null ? new List<Player>() : team.Roster.Where(p => IsMentorCandidate(team, p)).OrderByDescending(p => p.Age).ThenByDescending(p => p.BaseOverall).ToList();

        /// <summary>유효한 멘토링 쌍(멘토가 1군 더그아웃 리더 · 유망주가 내 구단 보류선수).</summary>
        public static List<(Player prospect, Player mentor)> Mentorships(GMLeagueState league)
        {
            var list = new List<(Player, Player)>();
            var team = league?.UserTeam;
            if (team == null) return list;
            foreach (var plan in GMFrontOffice.Ensure(league).FuturesPlans.Where(x => !string.IsNullOrEmpty(x.MentorId)))
            {
                var prospect = team.ReservePlayers.FirstOrDefault(p => p.InstanceId == plan.PlayerId);
                var mentor = team.Roster.FirstOrDefault(p => p.InstanceId == plan.MentorId);
                if (prospect != null && IsMentorCandidate(team, mentor)) list.Add((prospect, mentor));
            }
            return list;
        }

        public static Player MentorOf(GMLeagueState league, Player prospect) => Mentorships(league).FirstOrDefault(m => m.prospect == prospect).mentor;

        /// <summary>멘토링 배정(1:1 · 최대 3쌍). 이미 멘토가 있는 유망주는 새 멘토로 바꾼다.</summary>
        public static bool AssignMentor(GMLeagueState league, Player prospect, Player mentor, out string message)
        {
            message = "";
            var team = league?.UserTeam;
            if (team == null || prospect?.Template == null || !team.ReservePlayers.Contains(prospect)) { message = "멘토링할 내 구단 유망주를 고르십시오."; return false; }
            if (prospect.Age > MaxGrowthAge) { message = $"{MaxGrowthAge}세 이하 유망주만 멘토링 대상입니다."; return false; }
            if (!IsMentorCandidate(team, mentor)) { message = "멘토는 1군 더그아웃 리더(DugoutLeader) 성향 베테랑만 맡을 수 있습니다."; return false; }
            if (mentor == prospect) { message = "자기 자신은 멘토가 될 수 없습니다."; return false; }
            var pairs = Mentorships(league);
            if (pairs.Any(m => m.mentor == mentor && m.prospect != prospect)) { message = $"{mentor.Template.PlayerName}은(는) 이미 다른 유망주를 맡고 있습니다(1:1)."; return false; }
            bool replacing = pairs.Any(m => m.prospect == prospect);
            if (!replacing && pairs.Count >= MaxMentorSlots) { message = $"멘토링 슬롯 {MaxMentorSlots}개가 모두 찼습니다 - 먼저 해제하십시오."; return false; }
            EnsurePlan(league, prospect).MentorId = mentor.InstanceId;
            message = $"멘토링 배정 - {mentor.Template.PlayerName} → {prospect.Template.PlayerName} · 성장 +1 · 변동 하한 0(안정성) · 슬롯 {Mentorships(league).Count}/{MaxMentorSlots}";
            return true;
        }

        public static bool ClearMentor(GMLeagueState league, Player prospect)
        {
            var plan = PlanOf(league, prospect);
            if (plan == null || string.IsNullOrEmpty(plan.MentorId)) return false;
            plan.MentorId = "";
            return true;
        }

        // ================================================================== 콜업 · 이관

        /// <summary>[콜업] 퓨처스 → 1군. 1군이 가득이면 swapOut(1군 선수)을 퓨처스로 내리며 맞바꾼다.</summary>
        public static bool CallUp(GMLeagueState league, Player prospect, Player swapOut, out string message)
        {
            message = "";
            var team = league?.UserTeam;
            if (team == null || prospect == null || !team.Futures.Contains(prospect)) { message = "퓨처스 풀의 유망주를 고르십시오."; return false; }
            if (team.Roster.Count < GMRosterTiers.FirstTeamMax && swapOut == null) return GMRosterTiers.CallUp(league, team, prospect, out message);
            if (swapOut == null || !team.Roster.Contains(swapOut)) { message = $"1군 {GMRosterTiers.FirstTeamMax}명 가득 - 맞바꿀 1군 선수를 고르십시오."; return false; }
            if (swapOut.IsCaptain) { message = "주장은 퓨처스로 내릴 수 없습니다."; return false; }
            team.Futures.Remove(prospect);
            if (!GMRosterTiers.SendDown(team, swapOut, out message)) { team.Futures.Add(prospect); return false; }
            team.Roster.Add(prospect);
            message = $"스왑 - {prospect.Template.PlayerName} 1군 콜업 ↔ {swapOut.Template.PlayerName} 퓨처스 이관 · {GMRosterTiers.Summary(team)}";
            return true;
        }

        /// <summary>[이관] 1군 → 퓨처스. 멘토였다면 멘토링이 해제된다(1군 더그아웃 리더만 멘토).</summary>
        public static bool SendDown(GMLeagueState league, Player regular, out string message)
        {
            var team = league?.UserTeam;
            bool ok = GMRosterTiers.SendDown(team, regular, out message);
            if (ok) foreach (var plan in GMFrontOffice.Ensure(league).FuturesPlans.Where(x => x.MentorId == regular.InstanceId)) plan.MentorId = "";
            return ok;
        }

        // ================================================================== 연간 성장

        /// <summary>연도 해시 변동 -1 ~ +1.</summary>
        public static int Roll(Player p, int year) => p?.Template == null ? 0 : GMFrontOffice.Hash($"{p.Template.RealPlayerId}_{year}_futures") % 3 - 1;

        public static int BaseGrowth(Player p)
        {
            int room = PotentialMax(p) - p.BaseOverall;
            if (room <= 0 || p.Age > MaxGrowthAge) return 0;
            return Math.Max(1, (int)Math.Round(room * TypeRate(TypeOf(p))));
        }

        /// <summary>한 해 성장치(OVR) - year 연도 변동 포함. mentor가 있으면 +1 · 변동 하한 0.</summary>
        public static int GrowthFor(Player p, GMTrainingFocus focus, bool mentored, int year)
        {
            if (p?.Template == null || p.Age > MaxGrowthAge) return 0;
            int room = PotentialMax(p) - p.BaseOverall;
            if (room <= 0) return 0;
            int roll = Roll(p, year);
            if (mentored) roll = Math.Max(0, roll) + 1;
            return Math.Max(0, Math.Min(room, BaseGrowth(p) + FocusFit(p, focus) + roll));
        }

        /// <summary>화면 표시용 예상 성장(변동 0 기준).</summary>
        public static int ExpectedGrowth(GMLeagueState league, Player p)
        {
            if (p?.Template == null || p.Age > MaxGrowthAge) return 0;
            int room = PotentialMax(p) - p.BaseOverall;
            bool mentored = MentorOf(league, p) != null;
            return Math.Max(0, Math.Min(room, BaseGrowth(p) + FocusFit(p, FocusOf(league, p)) + (mentored ? 1 : 0)));
        }

        /// <summary>
        /// 연도 전환 성장 - 10구단 퓨처스 + 내 구단 육성 계획 대상(27세 이하)의 OVR을 성장치만큼 올린다(유망주 보정치 이동).
        /// 전 세부 스탯이 같은 폭으로 오르므로 OVR 상승 = 보정치 상승. 반환 = (선수, 성장치) 목록(0 제외).
        /// </summary>
        public static List<(Player player, int growth)> ApplySeasonGrowth(GMLeagueState league, int year)
        {
            var grown = new List<(Player, int)>();
            if (league == null) return grown;
            var fo = GMFrontOffice.Ensure(league);
            var user = league.UserTeam;
            var mentored = new HashSet<Player>(Mentorships(league).Select(m => m.prospect));
            foreach (var team in league.Teams.Values)
            {
                var targets = team.Futures.ToList();
                if (team == user) targets.AddRange(team.Roster.Where(p => fo.FuturesPlans.Any(x => x.PlayerId == p.InstanceId)));
                foreach (var p in targets.Distinct())
                {
                    if (p?.Template == null) continue;
                    var plan = team == user ? PlanOf(league, p) : null;
                    int g = GrowthFor(p, plan?.Focus ?? GMTrainingFocus.Balanced, mentored.Contains(p), year);
                    if (plan != null) { plan.LastGrowth = g; plan.LastGrowthYear = year; }
                    if (g <= 0) continue;
                    int before = p.BaseOverall;
                    GMRosterTiers.ApplyProspectShift(p, p.ProspectStatShift + g);
                    int actual = p.BaseOverall - before;
                    if (plan != null) plan.LastGrowth = actual;
                    if (actual > 0) grown.Add((p, actual));
                }
            }
            Prune(league);
            var mine = grown.Where(x => user != null && user.ReservePlayers.Contains(x.Item1)).OrderByDescending(x => x.Item2).ToList();
            if (mine.Count > 0)
                league.AddNews(new GMNewsItem
                {
                    GameIndex = 0, DateLabel = $"{year} 시즌 종료", Kind = GMNewsKind.Scouting, IsUserTeam = true,
                    Title = $"[육성] 퓨처스 유망주 {mine.Count}명 성장",
                    Body = string.Join(" · ", mine.Take(6).Select(x => $"{x.Item1.Template.PlayerName} OVR +{x.Item2}")),
                });
            return grown;
        }

        /// <summary>내 구단을 떠난 선수의 계획 · 무효 멘토를 정리한다.</summary>
        public static void Prune(GMLeagueState league)
        {
            var user = league?.UserTeam;
            if (user == null) return;
            var fo = GMFrontOffice.Ensure(league);
            var ids = new HashSet<string>(user.ReservePlayers.Select(p => p.InstanceId));
            fo.FuturesPlans.RemoveAll(x => !ids.Contains(x.PlayerId));
            foreach (var plan in fo.FuturesPlans.Where(x => !string.IsNullOrEmpty(x.MentorId) && !IsMentorCandidate(user, user.Roster.FirstOrDefault(p => p.InstanceId == x.MentorId)))) plan.MentorId = "";
        }
    }
}
