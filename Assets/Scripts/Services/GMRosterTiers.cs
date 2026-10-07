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
    /// [TASK-GM-08] 로스터 구조 확장(DCL-163 · 사전 조사 보고서 DECISION-004 (C)안):
    ///   1군 현역 29명(경기 출장 27명) + 퓨처스 핵심 유망주 풀 10~15명(기본 12명, 실제 선수) + 익명 육성 슬롯(인원 수만 관리).
    ///   - 1군 상한 = GMStoveLeagueMarket.RosterMax(29). 로더는 지금처럼 28인(타자 15 · 투수 13)을 채우고 1자리를 비워 둔다(FA · 트레이드 · 콜업 여유).
    ///   - 퓨처스 풀은 아직 어느 구단 · FA · 드래프트 풀에도 없는 선수의 가장 낮은 OVR 카드를 유망주 계약(19~25세 · 최저연봉 · 3~5년)으로 재설정해 채운다.
    ///     (세이브가 TemplateId로 선수를 복원하므로 가상 선수를 만들지 않는다 - 드래프트 풀과 같은 방식.)
    ///   - 트레이드 · 콜업으로 1군이 29명을 넘으면 트레이드 가치가 가장 낮은 선수가 퓨처스로 내려가고, 퓨처스도 15명을 넘으면 최하위가 자유계약(FA 시장, 보상 없음)이 된다.
    ///   - FA 보상선수 보호 명단(20/25인)은 1군 + 퓨처스 풀 전체(보류선수)를 대상으로 한다(GMFaCompensation).
    /// </summary>
    public static class GMRosterTiers
    {
        public const int FirstTeamMax = 29;      // 1군 엔트리
        public const int GameDayActive = 27;     // 경기 출장
        public const int FuturesMin = 10, FuturesMax = 15, FuturesDefault = 12;
        public const int DefaultDevelopmentSlots = 10; // 익명 육성 슬롯(보호 명단 · 경기에 쓰지 않는 인원 수)
        public const int FuturesMinAge = 19, FuturesMaxAge = 25;

        public static string Summary(GMTeamState team) => team == null ? "" :
            $"1군 {team.Roster.Count}/{FirstTeamMax}명(출장 {GameDayActive}) · 퓨처스 핵심 {team.Futures.Count}/{FuturesMax}명 · 육성 슬롯 {team.DevelopmentSlots}명";

        /// <summary>유망주 계약으로 재설정 - 19~25세 · 최저연봉 · 3~5년 · 유망주 성향 · Ego 1.</summary>
        public static void MakeFuturesProspect(Player p, int age)
        {
            p.Age = Math.Max(FuturesMinAge, Math.Min(FuturesMaxAge, age));
            p.Salary = Player.MinSalary;
            p.ContractYears = 3 + GMFrontOffice.Hash(p.Template?.RealPlayerId ?? p.InstanceId) % 3;
            p.RoleArchetype = LockerRoomRole.Prospect;
            p.EgoLevel = 1;
            p.IsCaptain = false;
            p.PersonalMorale = 75;
            p.InjuryRemainingDays = 0;
        }

        /// <summary>
        /// 10구단 퓨처스 풀 생성(기본 12명 = 타자 6 · 투수 6). 후보 = taken(1군 · FA · 드래프트)에 없는 인물(외국인 제외).
        ///   ① 리그 기준 연도(현역 모드 2026) 카드가 남은 선수(같은 구단 우선) - 실제 퓨처스 체급(OVR 55~70)이라 그대로 쓴다.
        ///   ② 그 밖(과거 수상 시즌 카드뿐 - OVR 75 이상)은 가장 낮은 카드를 유망주 체급(OVR 52~66)으로 낮춘 유망주 템플릿으로 만든다
        ///      (Player.ProspectStatShift로 저장 · 복원 - 세이브는 원본 TemplateId + 보정치만 남긴다).
        /// 같은 구단 계보 · 카드가 적은(덜 알려진) · 최근 데뷔 선수를 우선한다.
        /// </summary>
        public static void BuildFuturesPools(GMLeagueState state, Dictionary<string, List<PlayerTemplate>> byPerson, ICollection<string> taken,
            Dictionary<string, int> debutYear, int perTeam = FuturesDefault)
        {
            if (state == null || byPerson == null) return;
            var used = new HashSet<string>(taken ?? Array.Empty<string>());
            foreach (var p in state.FreeAgents.Concat(state.DraftPool).Concat(state.Teams.Values.SelectMany(t => t.ReservePlayers))) if (p?.Template != null) used.Add(p.Template.RealPlayerId);
            perTeam = Math.Max(FuturesMin, Math.Min(FuturesMax, perTeam));
            int baseYear = state.Mode == GMStartMode.AllTimeDream ? 0 : state.SeasonYear;
            int ageSeed = 0;
            foreach (var code in NameAliasTable.CanonicalTeamCodes)
            {
                if (!state.Teams.TryGetValue(code, out var team)) continue;
                team.Futures.Clear();
                for (int i = 0; i < perTeam; i++)
                {
                    bool pitcher = i % 2 == 1;
                    var pick = PickProspect(byPerson, used, code, pitcher, debutYear, baseYear) ?? PickProspect(byPerson, used, code, !pitcher, debutYear, baseYear);
                    if (pick == null) break;
                    used.Add(pick.RealPlayerId);
                    int ovr = pick.GetBaseOverall();
                    int target = ProspectMinOvr + GMFrontOffice.Hash($"{pick.RealPlayerId}_prospect") % (ProspectMaxOvr - ProspectMinOvr + 1);
                    int shift = ovr > ProspectCeiling ? target - ovr : 0;
                    var p = new Player(Guid.NewGuid().ToString(), shift != 0 ? ProspectTemplate(pick, shift) : pick) { ProspectStatShift = shift };
                    p.InitializeGMAttributesFromStats(state.SeasonYear, debutYear != null && debutYear.TryGetValue(pick.RealPlayerId, out int d) ? d : state.SeasonYear);
                    MakeFuturesProspect(p, FuturesMinAge + (ageSeed++ % (FuturesMaxAge - FuturesMinAge + 1)));
                    team.Futures.Add(p);
                }
            }
        }

        public const int ProspectMinOvr = 52, ProspectMaxOvr = 66, ProspectCeiling = 70;

        /// <summary>테스트 · 툴에서 바꿀 수 있는 템플릿 생성기(기본 ScriptableObject.CreateInstance).</summary>
        public static Func<PlayerTemplate> TemplateFactory = () => UnityEngine.ScriptableObject.CreateInstance<PlayerTemplate>();
        private static readonly Dictionary<string, PlayerTemplate> prospectCache = new Dictionary<string, PlayerTemplate>(); // 키 = TemplateId|RealPlayerId|보정치(UnityEngine.Object 동등성 비교를 피한다)

        /// <summary>
        /// 유망주 템플릿 - 원본 카드의 전 세부 스탯을 shift만큼 옮긴 복제본(이름 · 선수 ID · TemplateId · 포지션 유지, 등급 LIVE_NORMAL).
        /// 같은 (원본, 보정치)는 같은 복제본을 돌려준다(세이브 복원 시 재사용).
        /// </summary>
        public static PlayerTemplate ProspectTemplate(PlayerTemplate src, int shift)
        {
            if (src == null || shift == 0) return src;
            string key = $"{src.TemplateId}|{src.RealPlayerId}|{shift}";
            if (prospectCache.TryGetValue(key, out var cached) && !ReferenceEquals(cached, null) && cached) return cached;
            var t = TemplateFactory();
            t.TemplateId = src.TemplateId;
            t.RealPlayerId = src.RealPlayerId;
            t.PlayerName = src.PlayerName;
            t.RealName = src.RealName;
            t.SeasonYear = src.SeasonYear;
            t.Team = src.Team;
            t.CurrentTeam = src.CurrentTeam;
            t.IsActive = src.IsActive;
            t.IsPitcher = src.IsPitcher;
            t.BatterPosition = src.BatterPosition;
            t.PitcherRole = src.PitcherRole;
            t.Grade = Grade.LIVE_NORMAL;
            t.PresetSkillTier = src.PresetSkillTier;
            t.PresetSkillName = src.PresetSkillName;
            var b = src.BatterStats;
            t.BatterStats = new BatterStats(Math.Max(1, b.Power + shift), Math.Max(1, b.Contact + shift), Math.Max(1, b.Discipline + shift), Math.Max(1, b.Speed + shift), Math.Max(1, b.Defense + shift));
            var pi = src.PitcherStats;
            t.PitcherStats = new PitcherStats(Math.Max(1, pi.Stuff + shift), Math.Max(1, pi.Velocity + shift), Math.Max(1, pi.Movement + shift), Math.Max(1, pi.Control + shift), Math.Max(1, pi.Stamina + shift));
            prospectCache[key] = t;
            return t;
        }

        /// <summary>구버전 세이브 보충 - 모든 구단 퓨처스가 비어 있을 때만 선수 DB로 풀을 만들고 FA 원 소속을 기록한다. 보충했으면 true.</summary>
        public static bool BackfillFutures(GMLeagueState league, IReadOnlyList<PlayerTemplate> templates)
        {
            if (league == null || templates == null || league.Teams.Count == 0 || league.Teams.Values.Any(t => t.Futures.Count > 0)) return false;
            var byPerson = templates.Where(t => t != null && !string.IsNullOrEmpty(t.RealPlayerId)).GroupBy(t => t.RealPlayerId).ToDictionary(g => g.Key, g => g.ToList());
            if (byPerson.Count == 0) return false;
            var debut = byPerson.ToDictionary(p => p.Key, p => p.Value.Min(t => t.SeasonYear));
            BuildFuturesPools(league, byPerson, new HashSet<string>(), debut);
            GMFaCompensation.RegisterMarketOrigins(league);
            return league.Teams.Values.Any(t => t.Futures.Count > 0);
        }

        private static PlayerTemplate PickProspect(Dictionary<string, List<PlayerTemplate>> byPerson, HashSet<string> used, string teamCode, bool pitcher, Dictionary<string, int> debutYear, int baseYear)
        {
            PlayerTemplate best = null;
            double bestScore = double.MinValue;
            foreach (var person in byPerson)
            {
                if (used.Contains(person.Key) || person.Value.Count == 0 || person.Value[0].IsPitcher != pitcher) continue;
                var first = person.Value[0];
                if (StoveLeagueRules.IsForeignName(string.IsNullOrEmpty(first.RealName) ? first.PlayerName : first.RealName)) continue; // 외국인은 퓨처스 유망주가 아니다
                var current = baseYear > 0 ? person.Value.Where(t => t.SeasonYear == baseYear).ToList() : new List<PlayerTemplate>();
                var pool = current.Count > 0 ? current : person.Value;
                var own = pool.Where(t => NameAliasTable.ToCode(t.Team != Team.None ? t.Team : t.CurrentTeam) == teamCode).ToList();
                var card = (own.Count > 0 ? own : pool).OrderBy(t => t.GetBaseOverall()).ThenByDescending(t => t.SeasonYear).First();
                double score = (current.Count > 0 ? 100 : 0) + (own.Count > 0 ? 30 : 0) - 2.0 * person.Value.Count - Math.Abs(card.GetBaseOverall() - 60) * 0.5
                               + (debutYear != null && debutYear.TryGetValue(person.Key, out int d) ? Math.Max(0, d - 1990) * 0.3 : 0);
                if (score > bestScore || score == bestScore && best != null && string.CompareOrdinal(person.Key, best.RealPlayerId) < 0) { best = card; bestScore = score; }
            }
            return best;
        }

        /// <summary>[콜업] 퓨처스 → 1군(29명 미만일 때).</summary>
        public static bool CallUp(GMLeagueState league, GMTeamState team, Player p, out string message)
        {
            message = "";
            if (team == null || p == null || !team.Futures.Contains(p)) { message = "퓨처스 풀의 선수를 고르십시오."; return false; }
            if (team.Roster.Count >= FirstTeamMax) { message = $"1군 {FirstTeamMax}명 가득 - 먼저 1군 선수를 퓨처스로 내리십시오."; return false; }
            team.Futures.Remove(p);
            team.Roster.Add(p);
            message = $"{p.Template.PlayerName} 1군 콜업 - {Summary(team)}";
            return true;
        }

        /// <summary>[퓨처스 이관] 1군 → 퓨처스(퓨처스 15명 미만, 주장 제외). 라인업 핀 · 전담 응원은 정리한다.</summary>
        public static bool SendDown(GMTeamState team, Player p, out string message)
        {
            message = "";
            if (team == null || p == null || !team.Roster.Contains(p)) { message = "1군 선수를 고르십시오."; return false; }
            if (team.Futures.Count >= FuturesMax) { message = $"퓨처스 풀 {FuturesMax}명 가득입니다."; return false; }
            Detach(team, p);
            p.IsCaptain = false;
            team.Futures.Add(p);
            message = $"{p.Template.PlayerName} 퓨처스 이관 - {Summary(team)}";
            return true;
        }

        private static void Detach(GMTeamState team, Player p)
        {
            team.Roster.Remove(p);
            string id = p.InstanceId;
            team.Lineup.Starters.RemoveAll(x => x.InstanceId == id);
            team.Lineup.Roles.RemoveAll(x => x.InstanceId == id);
            team.Lineup.PitcherSlots.RemoveAll(x => x.InstanceId == id);
            GMCheerleaderRoster.RefreshDedications(team);
        }

        /// <summary>
        /// 1군 상한 정리 - 29명을 넘으면 트레이드 가치가 가장 낮은 선수(주장 제외)를 퓨처스로 내리고, 퓨처스가 15명을 넘으면 최하위를 자유계약(FA 시장)으로 푼다.
        /// 내려간 선수 이름 목록을 돌려준다.
        /// </summary>
        public static List<string> EnforceLimits(GMLeagueState league, GMTeamState team)
        {
            var moved = new List<string>();
            if (team == null) return moved;
            while (team.Roster.Count > FirstTeamMax)
            {
                var low = team.Roster.Where(p => !p.IsCaptain).OrderBy(GMStoveLeagueMarket.TradeValue).ThenBy(p => p.BaseOverall).FirstOrDefault();
                if (low == null) break;
                Detach(team, low);
                team.Futures.Add(low);
                moved.Add($"{low.Template.PlayerName}(퓨처스)");
            }
            while (team.Futures.Count > FuturesMax)
            {
                var low = team.Futures.OrderBy(GMStoveLeagueMarket.TradeValue).ThenBy(p => p.Potential).First();
                team.Futures.Remove(low);
                low.ContractYears = 0;
                league?.FreeAgents.Add(low);
                league?.FAOrigins.Remove(low.InstanceId); // 자유계약 = 보상 없음
                moved.Add($"{low.Template.PlayerName}(자유계약)");
            }
            return moved;
        }
    }
}
