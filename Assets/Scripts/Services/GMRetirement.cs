using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>[TASK-GM-19] 영구결번 자격 은퇴식 - 단장의 선택 3지선다.</summary>
    public enum GMCeremonyChoice { RetireNumber = 0, Ceremony = 1, QuietRelease = 2 }

    public class GMCeremonyResult
    {
        public bool Applied;
        public string Message = "";
    }

    /// <summary>
    /// [TASK-GM-19] 프랜차이즈 애착 시스템 2 - 은퇴 · 영구결번(연도 전환 훅 AdvanceToNextSeasonYear, 나이 +1 · 계약 -1 직후 · FA 공시 전).
    ///   - 은퇴 판정: 36세 이상이면서 (OVR 60 미만 · 잔여 계약 종료 · 1군 로스터 밖[퓨처스 · FA 시장]) 중 하나라도 해당하면
    ///     나이별 확률(36세 35% · 37세 50% · 38세 65% · 39세 80% · 40세 이상 100%, OVR 60 미만 +20%p)로 은퇴를 선언한다(선수별 결정적 난수).
    ///   - 영구결번 이벤트: 내 구단 은퇴 선수가 '근속 10년 이상 & 통산 WAR 40 이상'이면 은퇴식 대기 큐(PendingCeremonies)에 넣고 허브가 팝업으로 묻는다.
    ///     [영구결번 지정] 마케팅 예산 1.5억 · 팬 지지율 +12 · 영구 하한(결번 1개당 +5) · 이름 황금색 고정 · 선수단 신뢰도 +3
    ///     [일반 은퇴식] 마케팅 예산 3,000만 · 팬 지지율 +3 · 신뢰도 +1   [조용히 방출] 비용 없음 · 팬 지지율 -2 · 신뢰도 -2
    ///   - 은퇴 선수는 로스터 · 퓨처스 · FA 시장에서 빠지고(전담 응원 · 라인업 정리), 커리어 타임라인에 [은퇴]가 남는다.
    /// </summary>
    public static class GMRetirement
    {
        public const int MinAge = 36, OvrThreshold = 60;
        public const int RetiredNumberTenure = 10;
        public const float RetiredNumberWar = 40f;
        public const double LowOvrBonus = 0.2;
        public const long RetireNumberCost = 15000, CeremonyCost = 3000;
        public const int RetireNumberFan = 12, CeremonyFan = 3, QuietFan = -2, FanFloorPerNumber = 5;
        public const int RetireNumberTrust = 3, CeremonyTrust = 1, QuietTrust = -2;
        public const int MaxLog = 60;
        public const string GoldHex = "#FFC83D";

        public static double AgeChance(int age)
        {
            if (age < MinAge) return 0;
            switch (age)
            {
                case 36: return 0.35;
                case 37: return 0.5;
                case 38: return 0.65;
                case 39: return 0.8;
                default: return 1.0;
            }
        }

        /// <summary>은퇴 후보 조건(36세 이상 + OVR 60 미만 · 계약 종료 · 1군 밖 중 하나).</summary>
        public static bool IsCandidate(Player p, bool onFirstTeam) =>
            p?.Template != null && p.Age >= MinAge && (p.BaseOverall < OvrThreshold || p.ContractYears <= 0 || !onFirstTeam);

        public static double RetireChance(Player p, bool onFirstTeam)
        {
            if (!IsCandidate(p, onFirstTeam)) return 0;
            return Math.Min(1.0, AgeChance(p.Age) + (p.BaseOverall < OvrThreshold ? LowOvrBonus : 0));
        }

        public static bool QualifiesForRetiredNumber(Player p) => p != null && p.TenureYears >= RetiredNumberTenure && p.CareerWar >= RetiredNumberWar;

        public static string ReasonOf(Player p, bool onFirstTeam) =>
            p.BaseOverall < OvrThreshold ? $"기량 하락(OVR {p.BaseOverall})" : p.ContractYears <= 0 ? "계약 만료" : !onFirstTeam ? "1군 전력 외" : "";

        /// <summary>
        /// 연도 전환 은퇴 처리(newYear = 넘어간 새 연도). 은퇴 선수 목록을 돌려준다. rollOverride가 있으면 모든 후보에 그 난수를 쓴다(검증 · 툴).
        /// </summary>
        public static List<Player> Process(GMLeagueState league, int newYear, double? rollOverride = null)
        {
            var retired = new List<Player>();
            if (league == null) return retired;
            var fo = GMFrontOffice.Ensure(league);
            var candidates = new List<(Player p, GMTeamState team, bool first)>();
            foreach (var team in league.Teams.Values.OrderBy(t => t.TeamCode, StringComparer.Ordinal))
            {
                foreach (var p in team.Roster) if (IsCandidate(p, true)) candidates.Add((p, team, true));
                foreach (var p in team.Futures) if (IsCandidate(p, false)) candidates.Add((p, team, false));
            }
            foreach (var p in league.FreeAgents) if (IsCandidate(p, false)) candidates.Add((p, null, false));

            foreach (var (p, team, first) in candidates)
            {
                double chance = RetireChance(p, first);
                double roll = rollOverride ?? new Random(league.Seed ^ (newYear * 7919) ^ GMFrontOffice.Hash(p.InstanceId ?? p.Template.RealPlayerId) ^ 0x2E71).NextDouble();
                if (roll >= chance) continue;
                string code = team?.TeamCode ?? "";
                string reason = ReasonOf(p, first);
                GMCareerTimeline.EnsureCareer(p, team?.TeamCode);
                bool honor = team != null && team.IsUserTeam && QualifiesForRetiredNumber(p);
                Remove(league, team, p);
                retired.Add(p);
                GMCareerTimeline.Record(league, p, GMCareerEventKind.Retirement, code, $"{p.Age}세 현역 은퇴",
                    $"{reason} · {(team != null ? $"{team.DisplayName} 근속 {p.TenureYears}년" : "FA 신분")} · 통산 WAR {p.CareerWar:0.0}", true, newYear);
                fo.Retirements.Add(new GMRetirementLog { PlayerName = p.Template.PlayerName, TeamCode = code, Reason = reason, Year = newYear, Age = p.Age, Ovr = p.BaseOverall, TenureYears = p.TenureYears, CareerWar = p.CareerWar });
                while (fo.Retirements.Count > MaxLog) fo.Retirements.RemoveAt(0);
                if (honor)
                {
                    fo.PendingCeremonies.Add(new GMRetirementCeremony
                    {
                        PlayerId = p.InstanceId ?? "", RealPlayerId = p.Template.RealPlayerId ?? "", PlayerName = p.Template.PlayerName, TeamCode = code,
                        Position = GMFrontOffice.PositionLabel(p.Position), Year = newYear, Age = p.Age, TenureYears = p.TenureYears, Ovr = p.BaseOverall, CareerWar = p.CareerWar,
                    });
                }
                if (team != null && (team.IsUserTeam || honor || p.BaseOverall >= 70 || p.CareerWar >= 30f))
                {
                    league.AddNews(new GMNewsItem
                    {
                        GameIndex = 0, DateLabel = $"{newYear} 스토브리그", Kind = GMNewsKind.Season, IsUserTeam = team.IsUserTeam, IsMajor = honor,
                        Title = honor ? $"[은퇴] {p.Template.PlayerName}, {team.DisplayName}에서만 {p.TenureYears}년 - 영구결번 논의" : $"[은퇴] {team.DisplayName} {p.Template.PlayerName} 현역 은퇴",
                        Body = $"{p.Age}세 · {reason} · 통산 WAR {p.CareerWar:0.0}. " + (honor ? "구단은 은퇴식 형식을 두고 단장의 결단을 기다리고 있습니다." : "구단은 그동안의 헌신에 감사를 전했습니다."),
                    });
                }
            }
            Remember(retired);
            return retired;
        }

        private static void Remove(GMLeagueState league, GMTeamState team, Player p)
        {
            league.FreeAgents.Remove(p);
            league.FAOrigins.Remove(p.InstanceId);
            league.PriorityNegotiationIds.Remove(p.InstanceId);
            if (team == null) return;
            p.IsCaptain = false;
            if (team.Roster.Contains(p)) GMRosterTiers.Detach(team, p);
            team.Futures.Remove(p);
            if (team.TradeRequestPlayerId == p.Template.RealPlayerId) team.TradeRequestPlayerId = null;
        }

        // ================================================================== 은퇴식 큐

        public static GMRetirementCeremony NextCeremony(GMLeagueState league) =>
            league == null ? null : GMFrontOffice.Ensure(league).PendingCeremonies.FirstOrDefault(c => c != null && !c.Resolved);

        public static bool CanAfford(GMLeagueState league, GMCeremonyChoice choice)
        {
            var team = league?.UserTeam;
            if (team == null) return false;
            return choice == GMCeremonyChoice.RetireNumber ? team.MarketingBudget >= RetireNumberCost : choice != GMCeremonyChoice.Ceremony || team.MarketingBudget >= CeremonyCost;
        }

        public static string ChoiceLabel(GMCeremonyChoice c) =>
            c == GMCeremonyChoice.RetireNumber ? "영구결번 지정" : c == GMCeremonyChoice.Ceremony ? "일반 은퇴식" : "조용히 방출";

        public static string ChoiceEffect(GMCeremonyChoice c) =>
            c == GMCeremonyChoice.RetireNumber ? $"마케팅 예산 -{GMDiagnosticFormat.Short(RetireNumberCost)} · 팬 지지율 +{RetireNumberFan}(영구 하한 +{FanFloorPerNumber}) · 이름 황금색 고정 · 선수단 신뢰도 +{RetireNumberTrust}"
            : c == GMCeremonyChoice.Ceremony ? $"마케팅 예산 -{GMDiagnosticFormat.Short(CeremonyCost)} · 팬 지지율 +{CeremonyFan} · 선수단 신뢰도 +{CeremonyTrust}"
            : $"비용 없음 · 팬 지지율 {QuietFan} · 선수단 신뢰도 {QuietTrust}";

        /// <summary>은퇴식 선택 반영. 예산이 모자라면 적용하지 않는다.</summary>
        public static GMCeremonyResult Resolve(GMLeagueState league, GMRetirementCeremony c, GMCeremonyChoice choice)
        {
            var r = new GMCeremonyResult();
            var team = league?.UserTeam;
            if (team == null || c == null || c.Resolved) { r.Message = "이미 처리한 은퇴식입니다."; return r; }
            if (!CanAfford(league, choice)) { r.Message = "마케팅 예산이 부족합니다."; return r; }
            var fo = GMFrontOffice.Ensure(league);
            switch (choice)
            {
                case GMCeremonyChoice.RetireNumber:
                    team.MarketingBudget -= RetireNumberCost;
                    fo.RetiredNumbers.Add(new GMRetiredNumber
                    {
                        RealPlayerId = c.RealPlayerId, PlayerId = c.PlayerId, PlayerName = c.PlayerName, TeamCode = c.TeamCode, Position = c.Position,
                        Year = c.Year, TenureYears = c.TenureYears, CareerWar = c.CareerWar,
                    });
                    team.FanSupport = GMTeamFan.Clamp(Math.Max(team.FanSupport + RetireNumberFan, FanFloor(league, team)));
                    team.LockerRoomTrust = Math.Min(100, team.LockerRoomTrust + RetireNumberTrust);
                    r.Message = $"{c.PlayerName} 영구결번 지정 - 홈구장에 그의 번호가 영원히 걸립니다(마케팅 예산 -{GMDiagnosticFormat.Short(RetireNumberCost)} · 팬 지지율 +{RetireNumberFan} · 선수단 신뢰도 +{RetireNumberTrust}).";
                    break;
                case GMCeremonyChoice.Ceremony:
                    team.MarketingBudget -= CeremonyCost;
                    team.FanSupport = GMTeamFan.Clamp(team.FanSupport + CeremonyFan);
                    team.LockerRoomTrust = Math.Min(100, team.LockerRoomTrust + CeremonyTrust);
                    r.Message = $"{c.PlayerName} 은퇴식 거행 - 홈 팬들이 기립 박수로 배웅했습니다(마케팅 예산 -{GMDiagnosticFormat.Short(CeremonyCost)} · 팬 지지율 +{CeremonyFan}).";
                    break;
                default:
                    team.FanSupport = GMTeamFan.Clamp(Math.Max(team.FanSupport + QuietFan, FanFloor(league, team)));
                    team.LockerRoomTrust = Math.Max(0, team.LockerRoomTrust + QuietTrust);
                    r.Message = $"{c.PlayerName} 조용히 방출 - 팬들 사이에 아쉬움이 남았습니다(팬 지지율 {QuietFan} · 선수단 신뢰도 {QuietTrust}).";
                    break;
            }
            c.Resolved = true;
            c.Choice = ChoiceLabel(choice);
            c.Result = r.Message;
            r.Applied = true;
            var p = FindPlayer(league, c.PlayerId);
            if (p != null && choice == GMCeremonyChoice.RetireNumber)
                GMCareerTimeline.Record(league, p, GMCareerEventKind.RetiredNumber, c.TeamCode, $"{NameAliasTable.DisplayTeamName(c.TeamCode)} 영구결번", $"근속 {c.TenureYears}년 · 통산 WAR {c.CareerWar:0.0}", true, c.Year);
            league.AddNews(new GMNewsItem
            {
                GameIndex = 0, DateLabel = $"{c.Year} 스토브리그", Kind = GMNewsKind.Season, IsUserTeam = true, IsMajor = choice == GMCeremonyChoice.RetireNumber,
                Title = choice == GMCeremonyChoice.RetireNumber ? $"[영구결번] {c.PlayerName}의 번호, 영원히 {NameAliasTable.DisplayTeamName(c.TeamCode)}에" : $"[은퇴식] {c.PlayerName} - {c.Choice}",
                Body = r.Message,
            });
            return r;
        }

        /// <summary>은퇴 선수 객체는 리그에서 빠지므로 은퇴 직후 세션 안에서만 찾는다(없으면 null - 기록은 영구결번 목록에 남는다).</summary>
        private static Player FindPlayer(GMLeagueState league, string id) => RetiredPlayer(id);
        public static Player RetiredPlayer(string id) => retiredCache.TryGetValue(id ?? "", out var p) ? p : null;
        private static readonly Dictionary<string, Player> retiredCache = new Dictionary<string, Player>();

        /// <summary>은퇴 처리 직후 은퇴 선수 객체를 잠시 보관한다(은퇴식 타임라인 기록용).</summary>
        public static void Remember(IEnumerable<Player> retired)
        {
            foreach (var p in retired) if (p?.InstanceId != null) retiredCache[p.InstanceId] = p;
            while (retiredCache.Count > 200) retiredCache.Remove(retiredCache.Keys.First());
        }

        /// <summary>영구결번 팬 지지율 영구 하한(결번 1개당 +5).</summary>
        public static int FanFloor(GMLeagueState league, GMTeamState team) =>
            league == null || team == null ? 0 : GMFrontOffice.Ensure(league).RetiredNumbers.Count(n => n.TeamCode == team.TeamCode) * FanFloorPerNumber;

        /// <summary>연도 전환마다 영구결번 하한을 다시 적용한다.</summary>
        public static void ApplyFanFloors(GMLeagueState league)
        {
            if (league == null) return;
            foreach (var t in league.Teams.Values)
            {
                int floor = FanFloor(league, t);
                if (floor > 0 && t.FanSupport < floor) t.FanSupport = GMTeamFan.Clamp(floor);
            }
        }

        // ================================================================== 황금색 이름

        public static bool IsRetiredNumber(GMLeagueState league, string realPlayerId) =>
            league != null && !string.IsNullOrEmpty(realPlayerId) && GMFrontOffice.Ensure(league).RetiredNumbers.Any(n => n.RealPlayerId == realPlayerId);

        public static bool IsRetiredNumber(GMLeagueState league, Player p) => p?.Template != null && IsRetiredNumber(league, p.Template.RealPlayerId);

        /// <summary>영구결번 선수 이름 = 황금색 리치 텍스트(게임 내내 고정), 아니면 그대로.</summary>
        public static string NameRich(GMLeagueState league, Player p)
        {
            string name = p?.Template?.PlayerName ?? "-";
            return IsRetiredNumber(league, p) ? Gold(name) : name;
        }

        public static string Gold(string text) => $"<color={GoldHex}>{text}</color>";
    }
}
