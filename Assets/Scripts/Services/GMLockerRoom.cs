using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-13] 약속 시스템(상태 기계) - PROPOSED → ACTIVE(계약 체결) → FULFILLED / BROKEN, 계약 불발 = Cancelled.
    ///   ① 계약 협상실 [주전 · 보직 보장](REW_ROLE) 카드 선택 = 제안 → 협상 타결 = 활성 / 결렬 · 자금 부족 = 폐기
    ///   ② 라커룸 파벌 사건 [측근 잔류 약속] = 동료 재계약(잔류) 보장 - 합의 즉시 활성
    ///   이행 검사 = 정규시즌 144경기 종료(GMFrontOffice.OnSeasonCompleted) - DueYear 시즌의 약속만 판정한다.
    ///   주전 보장: 타자 규정 타석(446)의 50% · 선발 투수 규정 이닝(144이닝)의 50% · 불펜 25경기 이상 등판. 동료 잔류: 대상이 보류선수 명단에 남아 있음.
    ///   위반 = 해당 선수 충성도 -25 · 선수단 신뢰도 -10 · 긴장 BGM / 이행 = 충성도 +8 · 선수단 신뢰도 +5 · 환희 BGM.
    /// </summary>
    public static class GMPromiseSystem
    {
        public const int BrokenLoyaltyPenalty = 25, BrokenTrustPenalty = 10, FulfilledLoyaltyBonus = 8, FulfilledTrustBonus = 5;
        public const int QualifiedPA = 446, QualifiedOuts = 432, ReliefStarterGames = 25;
        public const double StarterShare = 0.5;
        public const string RoleCardId = "REW_ROLE";

        public static string KindLabel(GMPromiseKind k) => k == GMPromiseKind.StarterGuarantee ? "주전 보장" : "동료 잔류 보장";

        public static string StateLabel(GMPromiseState s) =>
            s == GMPromiseState.Proposed ? "제안" : s == GMPromiseState.Active ? "진행 중" : s == GMPromiseState.Fulfilled ? "이행" : s == GMPromiseState.Broken ? "위반" : "폐기";

        public static string Describe(GMPromise p) => p == null ? "" : p.Kind == GMPromiseKind.StarterGuarantee
            ? $"{p.PlayerName}: {p.DueYear} 시즌 주전 보장"
            : $"{p.PlayerName}: 동료 {p.TargetPlayerName} 잔류 보장";

        /// <summary>판정 시즌 - 시즌 시작 전(스토브리그)이면 올해, 시즌 중 · 종료 후면 다음 해.</summary>
        public static int DueYearFor(GMLeagueState league) => league.GamesPlayed == 0 ? league.SeasonYear : league.SeasonYear + 1;

        public static string RemainingText(GMLeagueState league, GMPromise p)
        {
            if (p.DueYear <= league.SeasonYear)
                return league.GamesPlayed >= GMSeasonReview.SeasonGames ? "판정 대기(시즌 종료)" : $"남은 {GMSeasonReview.SeasonGames - league.GamesPlayed}경기 · {p.DueYear} 시즌 종료 판정";
            return $"{p.DueYear} 시즌 종료 판정 · 유예 {p.DueYear - league.SeasonYear}년";
        }

        public static IEnumerable<GMPromise> All(GMLeagueState league) => GMFrontOffice.Ensure(league).Promises;
        public static List<GMPromise> Active(GMLeagueState league) => All(league).Where(p => p.State == GMPromiseState.Active).ToList();
        public static List<GMPromise> Of(GMLeagueState league, Player player) => player == null ? new List<GMPromise>() : All(league).Where(p => p.PlayerId == player.InstanceId).ToList();

        public static GMPromise Propose(GMLeagueState league, GMTeamState team, Player player, GMPromiseKind kind, string source, Player target = null)
        {
            if (league == null || team == null || player?.Template == null) return null;
            var fo = GMFrontOffice.Ensure(league);
            var promise = new GMPromise
            {
                Id = $"PR{++fo.PromiseSeq}", TeamCode = team.TeamCode, PlayerId = player.InstanceId, PlayerName = player.Template.PlayerName,
                Kind = kind, State = GMPromiseState.Proposed, MadeYear = league.SeasonYear, DueYear = DueYearFor(league), Source = source ?? "",
                TargetPlayerId = target?.InstanceId ?? "", TargetPlayerName = target?.Template?.PlayerName ?? "",
            };
            fo.Promises.Add(promise);
            return promise;
        }

        /// <summary>PROPOSED → ACTIVE(계약 체결 · 합의). 같은 선수의 같은 종류 활성 약속이 있으면 새 약속으로 대체한다.</summary>
        public static bool Activate(GMLeagueState league, GMPromise promise)
        {
            if (promise == null || promise.State != GMPromiseState.Proposed) return false;
            foreach (var old in All(league).Where(p => p != promise && p.State == GMPromiseState.Active && p.PlayerId == promise.PlayerId && p.Kind == promise.Kind).ToList())
            {
                old.State = GMPromiseState.Cancelled;
                old.ResultNote = "새 약속으로 대체";
            }
            promise.State = GMPromiseState.Active;
            return true;
        }

        public static bool Cancel(GMLeagueState league, GMPromise promise, string note = "계약 불발")
        {
            if (promise == null || promise.State != GMPromiseState.Proposed) return false;
            promise.State = GMPromiseState.Cancelled;
            promise.ResultNote = note;
            return true;
        }

        /// <summary>주전 보장 조건 - 이번 시즌 기록(없으면 0)으로 판정. shortfall = 부족분 설명.</summary>
        public static bool StarterConditionMet(GMLeagueState league, Player player, out string detail)
        {
            league.Stats.TryGetValue(player.InstanceId, out var s);
            if (!player.IsPitcher)
            {
                int need = (int)Math.Ceiling(QualifiedPA * StarterShare), pa = s?.PA ?? 0;
                detail = $"{pa}타석 / 기준 {need}타석(규정 타석 50%)";
                return pa >= need;
            }
            if (player.Template.PitcherRole == PitcherRole.StartingPitcher)
            {
                int need = (int)Math.Ceiling(QualifiedOuts * StarterShare), outs = s?.OutsPitched ?? 0;
                detail = $"{outs / 3}이닝 / 기준 {need / 3}이닝(규정 이닝 50%)";
                return outs >= need;
            }
            int g = s?.PG ?? 0;
            detail = $"{g}경기 등판 / 기준 {ReliefStarterGames}경기";
            return g >= ReliefStarterGames;
        }

        /// <summary>정규시즌 종료 이행 검사 - DueYear가 올해 이하인 활성 약속을 판정하고 보상 · 페널티 · 소식 · BGM을 적용한다.</summary>
        public static List<GMPromiseVerdict> Evaluate(GMLeagueState league, bool playAudio = true)
        {
            var verdicts = new List<GMPromiseVerdict>();
            if (league == null) return verdicts;
            foreach (var promise in Active(league).Where(p => p.DueYear <= league.SeasonYear))
            {
                league.Teams.TryGetValue(promise.TeamCode, out var team);
                var player = team?.ReservePlayers.FirstOrDefault(x => x.InstanceId == promise.PlayerId);
                bool ok;
                string detail;
                if (player == null) { ok = false; detail = "약속 대상 선수가 팀을 떠났습니다"; }
                else if (promise.Kind == GMPromiseKind.StarterGuarantee) ok = StarterConditionMet(league, player, out detail);
                else
                {
                    ok = team.ReservePlayers.Any(x => x.InstanceId == promise.TargetPlayerId);
                    detail = ok ? $"{promise.TargetPlayerName} 잔류" : $"{promise.TargetPlayerName} 이탈";
                }
                verdicts.Add(ok ? Fulfill(league, team, promise, player, detail) : Break(league, team, promise, player, detail));
            }
            if (playAudio && verdicts.Count > 0 && verdicts.Any(v => v.Promise.TeamCode == league.SelectedTeamCode))
                GMAudioManager.Ensure().PlayEventBgm(verdicts.Any(v => v.Broken) ? GMAudioEvent.Tension : GMAudioEvent.PositiveResult, league.SelectedTeamCode);
            return verdicts;
        }

        /// <summary>약속 위반 - 선수 충성도 -25 · 선수단 신뢰도 -10.</summary>
        public static GMPromiseVerdict Break(GMLeagueState league, GMTeamState team, GMPromise promise, Player player, string detail)
        {
            promise.State = GMPromiseState.Broken;
            promise.ResultNote = detail ?? "";
            if (player != null)
            {
                player.Loyalty = player.Loyalty - BrokenLoyaltyPenalty;
                player.PersonalMorale = Math.Max(0, player.PersonalMorale - 10);
            }
            if (team != null) team.LockerRoomTrust = Math.Max(0, team.LockerRoomTrust - BrokenTrustPenalty);
            string msg = $"약속 위반 - {Describe(promise)} ({detail}) · 충성도 -{BrokenLoyaltyPenalty} · 선수단 신뢰도 -{BrokenTrustPenalty}";
            league.AddNews(new GMNewsItem
            {
                GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 시즌 종료", Kind = GMNewsKind.Trade, IsUserTeam = promise.TeamCode == league.SelectedTeamCode, IsMajor = true,
                Title = $"[라커룸] {promise.PlayerName}, 단장 약속 불이행에 불만", Body = msg,
            });
            return new GMPromiseVerdict { Promise = promise, Broken = true, Message = msg };
        }

        public static GMPromiseVerdict Fulfill(GMLeagueState league, GMTeamState team, GMPromise promise, Player player, string detail)
        {
            promise.State = GMPromiseState.Fulfilled;
            promise.ResultNote = detail ?? "";
            if (player != null) player.Loyalty = player.Loyalty + FulfilledLoyaltyBonus;
            if (team != null) team.LockerRoomTrust = Math.Min(100, team.LockerRoomTrust + FulfilledTrustBonus);
            string msg = $"약속 이행 - {Describe(promise)} ({detail}) · 충성도 +{FulfilledLoyaltyBonus} · 선수단 신뢰도 +{FulfilledTrustBonus}";
            league.AddNews(new GMNewsItem
            {
                GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 시즌 종료", Kind = GMNewsKind.Trade, IsUserTeam = promise.TeamCode == league.SelectedTeamCode,
                Title = $"[라커룸] 단장, {promise.PlayerName}와(과)의 약속 지켰다", Body = msg,
            });
            return new GMPromiseVerdict { Promise = promise, Broken = false, Message = msg };
        }
    }

    /// <summary>
    /// [TASK-GM-13] 1티어 동적 사건 생성기(내 구단 · 같은 종류는 해마다 1회).
    ///   ① 중심 타선 연봉 갈등 - 직전 시즌 타자 WAR 1위가 팀 연봉 상위 5위 밖이고 충성도 '높음'(70) 미만 → 에이전트 언론 플레이.
    ///      [즉시 보상금] 예산 차감 · 충성도 +15 · 환희 / [언론 반박] 충성도 -20 · 40 미만이면 트레이드 요청(블록 등재 위험) · 긴장 /
    ///      [주장 면담] 단장 신뢰도 · 주장 능력치 기반 확률(10~85%) - 성공 충성도 +8 · 환희, 실패 충성도 -8 · 긴장.
    ///   ② 라커룸 파벌 갈등 - Ego 4 이상 3명 이상 · 선수단 신뢰도 40 미만 → 파벌 표면화(긴장 BGM).
    ///      [베테랑 개입] 확률 중재 / [파벌 리더 방출 경고] 기강 · 리더 충성도 급락 / [측근 잔류 약속] 동료 잔류 보장 약속(활성) 생성.
    ///   사건 생성 시 소식 + 긴장 BGM, 선택 결과는 GMDynamicEventResult.Audio(null = 이벤트 BGM 종료 후 화면 풀 재개).
    /// </summary>
    public static class GMDynamicEventEngine
    {
        public const int HighLoyalty = 70, SalaryTopN = 5, FactionEgo = 4, FactionMinCount = 3, FactionTrustBelow = 40;
        public const long MinBonus = 3000, MaxBonus = 50000;

        public static string KindLabel(GMDynamicEventKind k) => k == GMDynamicEventKind.SalaryDispute ? "중심 타선 연봉 갈등" : "라커룸 파벌 갈등";

        public static IEnumerable<GMDynamicEvent> Pending(GMLeagueState league) =>
            league == null ? Enumerable.Empty<GMDynamicEvent>() : GMFrontOffice.Ensure(league).DynamicEvents.Where(e => !e.Resolved && e.TeamCode == league.SelectedTeamCode);

        // ================================================================== 조건

        /// <summary>연봉 갈등 후보 - 직전 시즌(스냅숏 · 추정 포함) 타자 WAR 1위가 연봉 상위 5위 밖 · 충성도 70 미만이면 그 선수, 아니면 null.</summary>
        public static Player SalaryDisputeCandidate(GMLeagueState league, GMTeamState team, out double war)
        {
            war = 0;
            if (league == null || team == null) return null;
            int source = GMSeasonReview.SourceKind(league);
            var batters = team.Roster.Where(p => !p.IsPitcher && p.Template != null).ToList();
            if (batters.Count == 0) return null;
            var top = batters.Select(p => (p, war: (double)GMSeasonReview.LineOf(league, p, source, team.TeamCode).War)).OrderByDescending(x => x.war).First();
            war = top.war;
            var salaryRank = team.Roster.OrderByDescending(p => p.Salary).ToList();
            if (salaryRank.IndexOf(top.p) < SalaryTopN) return null;
            return top.p.Loyalty < HighLoyalty ? top.p : null;
        }

        public static List<Player> FactionMembers(GMTeamState team) =>
            team == null ? new List<Player>() : team.Roster.Where(p => p.Template != null && p.EgoLevel >= FactionEgo).OrderByDescending(p => p.EgoLevel).ThenByDescending(p => p.BaseOverall).ToList();

        public static bool FactionCondition(GMTeamState team) => team != null && team.LockerRoomTrust < FactionTrustBelow && FactionMembers(team).Count >= FactionMinCount;

        // ================================================================== 생성

        /// <summary>조건을 만족한 사건을 만든다(같은 종류는 해마다 1회). 새로 만든 사건 목록.</summary>
        public static List<GMDynamicEvent> Generate(GMLeagueState league, bool playAudio = true)
        {
            var created = new List<GMDynamicEvent>();
            var team = league?.UserTeam;
            if (team == null) return created;
            var fo = GMFrontOffice.Ensure(league);
            bool Fired(GMDynamicEventKind k) => fo.DynamicEvents.Any(e => e.Kind == k && e.Year == league.SeasonYear && e.TeamCode == team.TeamCode);
            if (!Fired(GMDynamicEventKind.SalaryDispute))
            {
                var p = SalaryDisputeCandidate(league, team, out double war);
                if (p != null) created.Add(CreateSalaryDispute(league, team, p, war));
            }
            if (!Fired(GMDynamicEventKind.LockerRoomFaction) && FactionCondition(team)) created.Add(CreateFaction(league, team));
            if (playAudio && created.Count > 0) GMAudioManager.Ensure().PlayEventBgm(GMAudioEvent.Tension, team.TeamCode); // 갈등 사건 = 긴장 BGM 하이재킹
            return created;
        }

        public static long BonusFor(Player p) => Math.Max(MinBonus, Math.Min(MaxBonus, (long)Math.Round(p.Salary * 0.3)));

        public static GMDynamicEvent CreateSalaryDispute(GMLeagueState league, GMTeamState team, Player p, double war)
        {
            var fo = GMFrontOffice.Ensure(league);
            var captain = Captain(team);
            string name = p.Template.PlayerName;
            var ev = new GMDynamicEvent
            {
                Id = $"EV{++fo.EventSeq}", Kind = GMDynamicEventKind.SalaryDispute, Year = league.SeasonYear, TeamCode = team.TeamCode,
                PlayerId = p.InstanceId, PlayerName = name, MediatorId = captain?.InstanceId ?? "", MediatorName = captain?.Template?.PlayerName ?? "",
                Cost = BonusFor(p),
                Title = $"[연봉 갈등] {name} 에이전트, 언론에 \"팀 기여도 1위인데 대우는 하위권\"",
                Body = $"{name}(WAR {war:0.0} · 팀 타자 1위)의 연봉 {GMDiagnosticFormat.Short(p.Salary)}은 팀 상위 {SalaryTopN}위 밖입니다. 에이전트가 언론 인터뷰로 즉각적인 연장 계약 또는 보상을 요구했습니다. " +
                       $"충성도 {p.Loyalty} · 자존심 {p.EgoLevel} · 선수단 신뢰도 {team.LockerRoomTrust}.",
            };
            ev.Choices.AddRange(new[] { "즉시 보상금 지급", "언론을 통한 반박", "주장 면담으로 무마" });
            ev.ChoiceHints.AddRange(new[]
            {
                $"예산 -{GMDiagnosticFormat.Short(ev.Cost)} · 충성도 +15 · 만족도 +10",
                "충성도 -20 · 만족도 -12 · 신뢰도 -3 · 충성도 40 미만이면 트레이드 요청",
                $"성공 확률 {MediationChance(league, team, ev) * 100:0}% ({(captain != null ? $"주장 {captain.Template.PlayerName}" : "주장 없음")}) · 성공 충성도 +8 / 실패 -8",
            });
            Post(league, team, ev);
            return ev;
        }

        public static GMDynamicEvent CreateFaction(GMLeagueState league, GMTeamState team)
        {
            var fo = GMFrontOffice.Ensure(league);
            var members = FactionMembers(team);
            var a = members[0];
            var b = members.Skip(1).FirstOrDefault(x => x.IsPitcher != a.IsPitcher) ?? members[1];
            var mediator = Veteran(team, members);
            var ev = new GMDynamicEvent
            {
                Id = $"EV{++fo.EventSeq}", Kind = GMDynamicEventKind.LockerRoomFaction, Year = league.SeasonYear, TeamCode = team.TeamCode,
                PlayerId = a.InstanceId, PlayerName = a.Template.PlayerName, RivalId = b.InstanceId, RivalName = b.Template.PlayerName,
                MediatorId = mediator?.InstanceId ?? "", MediatorName = mediator?.Template?.PlayerName ?? "",
                Title = $"[파벌 갈등] {a.Template.PlayerName} 측과 {b.Template.PlayerName} 측, 라커룸 신경전 표면화",
                Body = $"자존심 4 이상 선수 {members.Count}명이 두 갈래로 나뉘었습니다. 선수단의 단장 신뢰도는 {team.LockerRoomTrust}(40 미만)로, 단장의 중재 능력을 의심하는 목소리가 나옵니다. " +
                       $"{a.Template.PlayerName}(자존심 {a.EgoLevel}) ↔ {b.Template.PlayerName}(자존심 {b.EgoLevel}).",
            };
            ev.Choices.AddRange(new[] { "베테랑 개입 요청", "파벌 리더 방출 경고", "측근 잔류 약속으로 달래기" });
            var partner = RetentionPartner(team, a, b);
            ev.ChoiceHints.AddRange(new[]
            {
                $"성공 확률 {MediationChance(league, team, ev) * 100:0}% ({(mediator != null ? $"베테랑 {mediator.Template.PlayerName}" : "중재자 없음")}) · 성공 신뢰도 +8 · 팀워크 +2 / 실패 신뢰도 -3",
                $"{a.Template.PlayerName} 충성도 -15 · 신뢰도 +4 · 팀워크 +3 · 트레이드 요청 위험",
                $"약속 생성: {a.Template.PlayerName} - 동료 {partner?.Template?.PlayerName ?? "-"} 잔류 보장 · 충성도 +6 · 신뢰도 +2",
            });
            Post(league, team, ev);
            return ev;
        }

        private static void Post(GMLeagueState league, GMTeamState team, GMDynamicEvent ev)
        {
            GMFrontOffice.Ensure(league).DynamicEvents.Add(ev);
            league.AddNews(new GMNewsItem
            {
                GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Trade, IsUserTeam = true, IsMajor = true,
                Title = ev.Title, Body = ev.Body,
            });
        }

        public static Player Captain(GMTeamState team) => team?.Roster.FirstOrDefault(p => p.IsCaptain);

        /// <summary>파벌 밖 베테랑 중재자 - 더그아웃 리더 31세 이상 우선, 없으면 31세 이상 최고 OVR.</summary>
        public static Player Veteran(GMTeamState team, List<Player> faction) =>
            team.Roster.Where(p => p.Template != null && !faction.Contains(p) && p.Age >= 31).OrderByDescending(p => p.RoleArchetype == LockerRoomRole.DugoutLeader).ThenByDescending(p => p.BaseOverall).FirstOrDefault();

        /// <summary>측근 - 리더의 유대 상대(배터리 · 키스톤 · 멘토), 없으면 같은 유형(타자/투수) 최고 OVR 동료.</summary>
        public static Player RetentionPartner(GMTeamState team, Player leader, Player rival)
        {
            var bond = GMPlayerBonds.For(team, leader).Select(x => x.partner).FirstOrDefault(x => x != rival);
            return bond ?? team.Roster.Where(p => p != leader && p != rival && p.Template != null && p.IsPitcher == leader.IsPitcher).OrderByDescending(p => p.BaseOverall).FirstOrDefault();
        }

        private static Player Find(GMTeamState team, string id) => string.IsNullOrEmpty(id) ? null : team.ReservePlayers.FirstOrDefault(p => p.InstanceId == id);

        /// <summary>확률 선택지(주장 면담 · 베테랑 개입) 성공 확률 10~85%.</summary>
        public static double MediationChance(GMLeagueState league, GMTeamState team, GMDynamicEvent ev)
        {
            var m = Find(team, ev.MediatorId);
            double v;
            if (ev.Kind == GMDynamicEventKind.SalaryDispute)
            {
                v = 0.25 + (team.LockerRoomTrust - 50) * 0.01;
                v += m != null ? (m.BaseOverall - 60) * 0.008 + (m.RoleArchetype == LockerRoomRole.DugoutLeader ? 0.12 : 0) + (m.Loyalty - 50) * 0.003 : -0.10;
            }
            else
            {
                v = 0.30 + (team.LockerRoomTrust - 40) * 0.01;
                v += m != null ? (m.Age - 30) * 0.03 + (m.RoleArchetype == LockerRoomRole.DugoutLeader ? 0.15 : 0) + (m.Loyalty - 50) * 0.004 : -0.15;
            }
            return Math.Max(0.10, Math.Min(0.85, v));
        }

        // ================================================================== 선택

        /// <summary>선택지 적용(0~2). rollOverride(0~1)를 주면 확률 판정에 그 값을 쓴다(테스트 · 재현용).</summary>
        public static GMDynamicEventResult Resolve(GMLeagueState league, GMDynamicEvent ev, int choice, double? rollOverride = null, bool playAudio = true)
        {
            var r = new GMDynamicEventResult();
            if (league == null || ev == null || ev.Resolved || choice < 0 || choice >= ev.Choices.Count) { r.Message = "처리할 사건이 없습니다."; return r; }
            if (!league.Teams.TryGetValue(ev.TeamCode, out var team)) { r.Message = "구단을 찾을 수 없습니다."; return r; }
            var p = Find(team, ev.PlayerId);
            if (p == null) { Close(ev, choice, "당사자가 팀을 떠나 사건이 종결됐습니다."); r.Applied = true; r.Message = ev.ResultText; return r; }
            double roll = rollOverride ?? new Random(GMFrontOffice.Hash(ev.Id + league.Seed) ^ league.SeasonYear).NextDouble();
            int trustBefore = team.LockerRoomTrust, loyaltyBefore = p.Loyalty;
            long budgetBefore = team.Budget;
            string name = p.Template.PlayerName;

            if (ev.Kind == GMDynamicEventKind.SalaryDispute)
            {
                switch (choice)
                {
                    case 0:
                        if (team.Budget < ev.Cost) { r.Message = $"운영 자금 부족 - 보상금 {GMDiagnosticFormat.Won(ev.Cost)}이 필요합니다."; return r; }
                        team.Budget -= ev.Cost;
                        p.Loyalty += 15;
                        p.PersonalMorale = Math.Min(100, p.PersonalMorale + 10);
                        team.LockerRoomTrust = Math.Min(100, team.LockerRoomTrust + 2);
                        r.Success = true;
                        r.Audio = GMAudioEvent.PositiveResult;
                        r.Message = $"{name}에게 보상금 {GMDiagnosticFormat.Short(ev.Cost)} 지급 - 에이전트가 인터뷰를 철회했습니다.";
                        break;
                    case 1:
                        p.Loyalty -= 20;
                        p.PersonalMorale = Math.Max(0, p.PersonalMorale - 12);
                        team.LockerRoomTrust = Math.Max(0, team.LockerRoomTrust - 3);
                        bool block = p.Loyalty < 40;
                        if (block) team.TradeRequestPlayerId = p.Template.RealPlayerId;
                        r.Audio = GMAudioEvent.Tension;
                        r.Message = $"구단이 언론으로 반박 - {name} 측이 강하게 반발했습니다." + (block ? " 선수 측 트레이드 요청(트레이드 블록 등재 위험)." : "");
                        break;
                    default:
                        bool ok = roll < MediationChance(league, team, ev);
                        p.Loyalty += ok ? 8 : -8;
                        team.LockerRoomTrust = Math.Max(0, Math.Min(100, team.LockerRoomTrust + (ok ? 3 : -4)));
                        r.Success = ok;
                        r.Audio = ok ? GMAudioEvent.PositiveResult : GMAudioEvent.Tension;
                        r.Message = ok ? $"주장 {(ev.MediatorName != "" ? ev.MediatorName : "대행")} 면담 성공 - {name}이(가) 시즌 후 재협상에 동의했습니다."
                                       : $"주장 면담 실패 - {name} 측은 \"말뿐인 약속\"이라며 불만을 키웠습니다.";
                        break;
                }
            }
            else
            {
                var rival = Find(team, ev.RivalId);
                switch (choice)
                {
                    case 0:
                        bool ok = roll < MediationChance(league, team, ev);
                        if (ok)
                        {
                            team.LockerRoomTrust = Math.Min(100, team.LockerRoomTrust + 8);
                            p.Loyalty += 3;
                            if (rival != null) rival.Loyalty += 3;
                            team.AgendaTeamworkBonus = Math.Min(20, team.AgendaTeamworkBonus + 2);
                        }
                        else team.LockerRoomTrust = Math.Max(0, team.LockerRoomTrust - 3);
                        r.Success = ok;
                        r.Audio = ok ? GMAudioEvent.PositiveResult : GMAudioEvent.Tension;
                        r.Message = ok ? $"베테랑 {(ev.MediatorName != "" ? ev.MediatorName : "선수단")}의 중재로 두 파벌이 화해했습니다."
                                       : "베테랑 중재 실패 - 갈등이 수면 아래로 가라앉았을 뿐입니다.";
                        break;
                    case 1:
                        p.Loyalty -= 15;
                        p.PersonalMorale = Math.Max(0, p.PersonalMorale - 10);
                        team.LockerRoomTrust = Math.Min(100, team.LockerRoomTrust + 4);
                        team.AgendaTeamworkBonus = Math.Min(20, team.AgendaTeamworkBonus + 3);
                        bool request = p.EgoLevel >= 5 || p.Loyalty < 40;
                        if (request) team.TradeRequestPlayerId = p.Template.RealPlayerId;
                        r.Success = true;
                        r.Audio = GMAudioEvent.Tension;
                        r.Message = $"{name}에게 방출 경고 - 기강은 잡혔지만 당사자의 불만이 큽니다." + (request ? " 트레이드 요청이 들어왔습니다." : "");
                        break;
                    default:
                        var partner = RetentionPartner(team, p, rival);
                        if (partner != null)
                        {
                            var promise = GMPromiseSystem.Propose(league, team, p, GMPromiseKind.TeammateRetention, "라커룸 사건", partner);
                            GMPromiseSystem.Activate(league, promise);
                        }
                        p.Loyalty += 6;
                        team.LockerRoomTrust = Math.Min(100, team.LockerRoomTrust + 2);
                        r.Success = true;
                        r.Audio = null; // 합의 - 긴장 BGM을 끝내고 화면 풀로 복귀
                        r.Message = $"{name}에게 측근 {partner?.Template?.PlayerName ?? "동료"} 잔류를 약속했습니다(약속 트래커 등록).";
                        break;
                }
            }

            r.Applied = true;
            r.LoyaltyDelta = p.Loyalty - loyaltyBefore;
            r.TrustDelta = team.LockerRoomTrust - trustBefore;
            r.BudgetDelta = team.Budget - budgetBefore;
            Close(ev, choice, r.Message);
            if (playAudio && ev.TeamCode == league.SelectedTeamCode)
            {
                var audio = GMAudioManager.Ensure();
                if (r.Audio.HasValue) audio.PlayEventBgm(r.Audio.Value, ev.TeamCode);
                else audio.EndEvent();
            }
            return r;
        }

        private static void Close(GMDynamicEvent ev, int choice, string text)
        {
            ev.Resolved = true;
            ev.ChosenIndex = choice;
            ev.ResultText = text ?? "";
        }
    }

    /// <summary>
    /// [TASK-GM-14] 동료 연봉 연쇄 반응(기획서 6절 8항 · 2단계) - 연봉 협상 · 재계약 타결 결과가 라커룸으로 번진다.
    ///   ① 대폭 인상(+20% 이상이면서 +5,000만 원 이상): 유대 동료(배터리 · 키스톤 · 멘토, 최대 2명) 충성도 +2 /
    ///      유대가 없는 같은 유형(타자/투수) 동급 동료(OVR -3 이내 · 새 연봉보다 적게 받는 선수, 최대 3명) = 상대적 박탈감 충성도 -3(자존심 4+ -5) · 만족도 -3.
    ///   ② 삭감 타결: 유대 동료 최대 2명 충성도 -2(동료 홀대 반감). 동결 · 소폭 인상은 연쇄 없음.
    /// 반환 = 연쇄 효과 문구(없으면 빈 목록). 효과가 있으면 소식 1건.
    /// </summary>
    public static class GMSalaryChain
    {
        public const double BigRaiseRatio = 0.20;
        public const int BigRaiseMin = 5000, MaxBonded = 2, MaxPeers = 3, PeerOvrGap = 3;
        public const int BondJoy = 2, PeerEnvy = 3, EgoPeerEnvy = 5, CutResentment = 2;

        public static bool IsBigRaise(int oldSalary, int newSalary) => newSalary - oldSalary >= Math.Max(BigRaiseMin, oldSalary * BigRaiseRatio);

        public static List<string> Apply(GMLeagueState league, GMTeamState team, Player p, int oldSalary, int newSalary, GMNegotiationOutcome? outcome)
        {
            var effects = new List<string>();
            if (league == null || team == null || p?.Template == null) return effects;
            var bonded = GMPlayerBonds.For(team, p).Select(b => b.partner).Where(x => x?.Template != null).Distinct().Take(MaxBonded).ToList();
            if (IsBigRaise(oldSalary, newSalary))
            {
                foreach (var b in bonded)
                {
                    b.Loyalty += BondJoy;
                    effects.Add($"유대 동료 {b.Template.PlayerName} 충성도 +{BondJoy}");
                }
                var peers = team.Roster.Where(q => q != p && q.Template != null && !bonded.Contains(q) && q.IsPitcher == p.IsPitcher && q.Salary < newSalary && q.BaseOverall >= p.BaseOverall - PeerOvrGap)
                    .OrderByDescending(q => q.BaseOverall).ThenBy(q => q.InstanceId).Take(MaxPeers).ToList();
                foreach (var q in peers)
                {
                    int d = q.EgoLevel >= 4 ? EgoPeerEnvy : PeerEnvy;
                    q.Loyalty -= d;
                    q.PersonalMorale = Math.Max(0, q.PersonalMorale - 3);
                    effects.Add($"{q.Template.PlayerName} 충성도 -{d}(상대적 박탈감)");
                }
            }
            else if (outcome == GMNegotiationOutcome.Cut)
            {
                foreach (var b in bonded)
                {
                    b.Loyalty -= CutResentment;
                    effects.Add($"유대 동료 {b.Template.PlayerName} 충성도 -{CutResentment}(동료 삭감 반감)");
                }
            }
            if (effects.Count > 0)
                league.AddNews(new GMNewsItem
                {
                    GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Trade, IsUserTeam = team.IsUserTeam,
                    Title = $"[라커룸] {p.Template.PlayerName} 연봉 {GMDiagnosticFormat.Short(newSalary)} - 동료들 반응", Body = string.Join(" · ", effects),
                });
            return effects;
        }

        public static string Summary(List<string> effects) => effects == null || effects.Count == 0 ? "" : " / 라커룸 연쇄: " + string.Join(" · ", effects);
    }

    /// <summary>[TASK-GM-13] 선수단 회의실 요약 - 평균 충성도 · 충성도 등급 · 선수별 불만 사항.</summary>
    public static class GMLockerRoom
    {
        public static string LoyaltyLabel(int v) => v >= GMDynamicEventEngine.HighLoyalty ? "높음" : v >= 40 ? "보통" : "낮음";

        public static double AverageLoyalty(GMTeamState team) => team == null || team.Roster.Count == 0 ? 0 : team.Roster.Average(p => p.Loyalty);

        /// <summary>주전(라인업 9 · 선발 5 · 필승조 6) 집합.</summary>
        public static HashSet<Player> Regulars(GMTeamState team)
        {
            var set = new HashSet<Player>(LineupAssignment.AssignStarters(team.Roster, team.Lineup).Select(s => s.Player).Where(p => p != null));
            foreach (var p in team.Roster.Where(x => x.IsPitcher && x.Template.PitcherRole == PitcherRole.StartingPitcher).OrderByDescending(x => x.BaseOverall).Take(5)) set.Add(p);
            foreach (var p in team.Roster.Where(x => x.IsPitcher && x.Template.PitcherRole != PitcherRole.StartingPitcher).OrderByDescending(x => x.BaseOverall).Take(6)) set.Add(p);
            return set;
        }

        /// <summary>선수 불만 사항(없으면 빈 목록) - 연봉 · 출전 시간 · 약속 위반 · 이적 고려 · 만족도 · 파벌.</summary>
        public static List<string> Complaints(GMLeagueState league, GMTeamState team, Player p, HashSet<Player> regulars, double medianSalary, int source)
        {
            var list = new List<string>();
            if (p?.Template == null) return list;
            double war = GMSeasonReview.LineOf(league, p, source, team.TeamCode).War;
            if (war >= 2.0 && p.Salary < medianSalary) list.Add("연봉 불만");
            if (!regulars.Contains(p) && (p.EgoLevel >= 3 || p.RoleArchetype == LockerRoomRole.Ambitious || (p.RoleArchetype == LockerRoomRole.Prospect && p.BaseOverall >= 65))) list.Add("출전 시간 부족");
            if (GMPromiseSystem.Of(league, p).Any(x => x.State == GMPromiseState.Broken && x.DueYear >= league.SeasonYear - 1)) list.Add("약속 위반 여파");
            if (GMDynamicEventEngine.Pending(league).Any(e => e.PlayerId == p.InstanceId || e.RivalId == p.InstanceId)) list.Add("갈등 진행 중");
            if (p.Loyalty < 35) list.Add("이적 고려");
            if (p.PersonalMorale < 45) list.Add("만족도 저하");
            return list;
        }

        public static double MedianSalary(GMTeamState team)
        {
            var s = team.Roster.Select(p => p.Salary).OrderBy(x => x).ToList();
            return s.Count == 0 ? 0 : s[s.Count / 2];
        }
    }
}
