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
    /// <summary>[TASK-GM-18] 시즌 중 단장 개입 사건 10종(MVP).</summary>
    public enum GMSeasonEventKind
    {
        StarInjury = 0,      // 핵심 선수 장기 부상 - 안전 회복 · 재활 가속 · 퓨처스 콜업
        AceSlump = 1,        // 에이스 부진 - 로테이션 유지 · 휴식 · 투수코치 집중 지도
        ProspectBoom = 2,    // 유망주 급성장 - 콜업 · 육성 유지 · 트레이드 카드
        LosingStreak = 3,    // 5연패 - 선수단 미팅 · 감독 전권 · 응원단 특별 응원전
        PennantRace = 4,     // 선두권 · 가을야구 접전 - 즉시 보강 · 현상 유지 · 유망주 기회
        TradeOffer = 5,      // AI 구단 1:1 트레이드 제안(7/31 이전) - 수락 · 거절 · 재협상
        ContractAnxiety = 6, // 핵심 선수 재계약 불안 - 조기 연장 · 시즌 후 협상 · 트레이드 검토
        FanDrop = 7,         // 팬심 하락 - 치어리더 테마 이벤트 · 티켓 프로모션 · 무대응
        OwnerCheck = 8,      // 구단주 중간 점검(목표 달성률) - 보강 약속 · 재정 절감 · 육성 기조
        ManagerConflict = 9, // 감독 갈등 - 감독 지지 · 단장 개입 · 코치진 교체
        // [TASK-GM-19] 2티어 사건
        ManagerDemotion = 10, // 감독과의 갈등 - 5연패 중 감독이 부진한 고액 연봉자 2군행 통보(감독 지지 · 1군 유지 강제 · 면담 중재)
        RivalSigning = 11,    // 라이벌 구단의 S급 영입 - 팬덤 동요(맞불 영입 예고 · 유망주 육성 천명 · 대규모 치어리더 이벤트)
    }

    /// <summary>[TASK-GM-18] 사건 우선순위 - P0 시즌 방향 · P1 전력/재정/계약 · P2 일반 운영 · P3 정보(뉴스로만).</summary>
    public enum GMEventPriority { P0 = 0, P1 = 1, P2 = 2, P3 = 3 }

    public class GMSeasonEventChoice
    {
        public string Id = "", Label = "", Effect = "";
        public bool Available = true;
        public string BlockReason = "";
    }

    /// <summary>[TASK-GM-18] 주간 종료 후 팝업 1건(인터럽트로 전달, 세이브하지 않음 - 결정 결과는 GMSeasonDecisionLog로 남는다).</summary>
    public class GMSeasonEvent
    {
        public GMSeasonEventKind Kind;
        public GMEventPriority Priority;
        public int Year, Week, Day;
        public string Title = "", Situation = "", WeekSummary = "";
        public readonly List<string> Facts = new List<string>();
        public readonly List<GMSeasonEventChoice> Choices = new List<GMSeasonEventChoice>();
        public int DefaultChoice;
        public Player Player, Partner;
        public string PartnerTeam;
        public bool Resolved;
        public string ResultText = "";
    }

    public class GMSeasonEventResult
    {
        public bool Applied, Success;
        public string Message = "";
        public GMAudioEvent? Audio;
        public string Sfx;
    }

    /// <summary>
    /// [TASK-GM-18] 시즌 중 단장 개입(정규시즌을 "의사결정 구간"으로 끊는다).
    ///   - 주간 = 개막 주말 2경기 → 이후 6경기(화~일) 단위. 주간 시뮬레이션 종료(경기일 마감) 직후에만 사건을 판정한다(경기 중간 팝업 없음).
    ///   - 판정: 게임 상태 트리거(부상 · 연패 · 순위 · 계약 · 팬심 · 구단주 · 트레이드 시장) → 쿨다운 · 시즌 상한 필터 → P0는 항상, P1 55% · P2 35%(사건 전용 난수열),
    ///     조용한 주가 3주 이어지면 최우선 후보 1건 강제(연속 3주 이상 사건 없음 방지), 주당 최대 3건 · 정규시즌 최대 12건. 페넌트 레이스 모드 = P0 · P1만.
    ///   - 선택지마다 트레이드오프(단기 전력 ↔ 장기 · 재정 ↔ 성적 · 선수 만족 ↔ 통제)가 있고, 결과는 즉시 팀 상태(부상 · 로스터 · 예산 · 신뢰도 · 팬심 · 팀워크)에 반영되어
    ///     다음 주 경기 · 시즌 결산 · 스토브리그로 이어진다. 결정은 GMFrontOfficeState.SeasonDecisions에 로그로 남는다.
    ///   - 자동 진행(테스트 · RunUntilStop) = DefaultChoice(경기력에 영향을 주지 않는 보수적 선택).
    ///   - BGM: 중요 성공(콜업 = Jump up Lions · 트레이드/재계약 = 환희 · 구단주 승인 = 승리를 위해) · 위기(갈등 · 부상 강행 실패) = 긴장, 일반 = 효과음만.
    ///     고속 진행 중에는 GMAudioManager.SimulationMode가 하이재킹을 막는다.
    /// </summary>
    public static class GMSeasonEvents
    {
        public const int OpeningGames = 2, WeekGames = 6;
        public const int MaxPerWeek = 3, MaxPerSeason = 12, ForceAfterQuietWeeks = 3, FirstEventWeek = 1;
        public const double P1Chance = 0.55, P2Chance = 0.35, ProspectBoomChance = 0.3, ManagerConflictChance = 0.25;
        public const int StarInjuryDays = 14, AceSlumpMinOuts = 45, PennantWeek = 10, ContractWeek = 8, FanWeek = 5, ConflictWeek = 6, TradeWeek = 3;
        public const double AceSlumpEra = 5.5;
        public const int OwnerCheckGames = 70;
        public const int TeamworkMin = -5, TeamworkMax = 5;
        public const long CoachCost = 10000, BoostCost = 50000, TicketCost = 20000, SavingsRefund = 30000;
        public const long CheerRallyCost = 3000, CheerThemeCost = 5000;
        public const double RehabSetbackChance = 0.3, RenegotiateChance = 0.5;
        public const int RenegotiateCash = 10000;

        // ================================================================== 주간

        /// <summary>경기일 마감 후 치른 경기 수가 주간 경계인지(개막 주말 2경기 · 이후 6경기마다, 144경기 제외).</summary>
        public static bool IsWeekEnd(int gamesPlayed) =>
            gamesPlayed > 0 && gamesPlayed < GMLiveSeasonSimulator.SeasonGames && (gamesPlayed == OpeningGames || (gamesPlayed > OpeningGames && (gamesPlayed - OpeningGames) % WeekGames == 0));

        /// <summary>주차(개막 주말 = 0주차).</summary>
        public static int WeekOf(int gamesPlayed) => gamesPlayed <= OpeningGames ? 0 : (gamesPlayed - OpeningGames + WeekGames - 1) / WeekGames;

        /// <summary>다음 주간 경계(이번 주 마지막 경기 수) - [주간 진행] 목표.</summary>
        public static int NextWeekEnd(int gamesPlayed)
        {
            if (gamesPlayed < OpeningGames) return OpeningGames;
            int k = (gamesPlayed - OpeningGames) / WeekGames + 1;
            return Math.Min(GMLiveSeasonSimulator.SeasonGames, OpeningGames + k * WeekGames);
        }

        public static bool Enabled(GMLeagueState league) =>
            GMFeatureFlags.IsInSeasonEventsEnabled && league?.UserTeam != null && league.Phase == GMSeasonPhase.RegularSeason;

        public static string KindLabel(GMSeasonEventKind k)
        {
            switch (k)
            {
                case GMSeasonEventKind.StarInjury: return "핵심 선수 부상";
                case GMSeasonEventKind.AceSlump: return "에이스 부진";
                case GMSeasonEventKind.ProspectBoom: return "유망주 급성장";
                case GMSeasonEventKind.LosingStreak: return "연패 위기";
                case GMSeasonEventKind.PennantRace: return "순위 경쟁";
                case GMSeasonEventKind.TradeOffer: return "트레이드 제안";
                case GMSeasonEventKind.ContractAnxiety: return "재계약 불안";
                case GMSeasonEventKind.FanDrop: return "팬심 하락";
                case GMSeasonEventKind.OwnerCheck: return "구단주 중간 점검";
                case GMSeasonEventKind.ManagerDemotion: return "감독과의 갈등";
                case GMSeasonEventKind.RivalSigning: return "라이벌 S급 영입";
                default: return "감독 갈등";
            }
        }

        public static string PriorityLabel(GMEventPriority p) => p == GMEventPriority.P0 ? "P0 · 긴급" : p == GMEventPriority.P1 ? "P1 · 중요" : p == GMEventPriority.P2 ? "P2 · 운영" : "P3 · 정보";

        private static int Cooldown(GMSeasonEventKind k)
        {
            switch (k)
            {
                case GMSeasonEventKind.StarInjury: return 4;
                case GMSeasonEventKind.AceSlump: return 5;
                case GMSeasonEventKind.ProspectBoom: return 6;
                case GMSeasonEventKind.LosingStreak: return 3;
                case GMSeasonEventKind.PennantRace: return 6;
                case GMSeasonEventKind.TradeOffer: return 4;
                case GMSeasonEventKind.ContractAnxiety: return 8;
                case GMSeasonEventKind.FanDrop: return 6;
                case GMSeasonEventKind.OwnerCheck: return 99;
                case GMSeasonEventKind.ManagerDemotion: return 4;
                case GMSeasonEventKind.RivalSigning: return 3;
                default: return 8;
            }
        }

        private static int SeasonMax(GMSeasonEventKind k)
        {
            switch (k)
            {
                case GMSeasonEventKind.StarInjury: return 3;
                case GMSeasonEventKind.LosingStreak: return 3;
                case GMSeasonEventKind.TradeOffer: return 4;
                case GMSeasonEventKind.OwnerCheck: return 1;
                default: return 2;
            }
        }

        // ================================================================== 주간 종료 처리

        /// <summary>주간 종료 - 사건 팀워크 보정의 남은 주를 줄이고(0이면 소멸), 시즌이 바뀌었으면 카운터를 초기화한다.</summary>
        public static void OnWeekEnd(GMLeagueState league)
        {
            var fo = GMFrontOffice.Ensure(league);
            if (fo.SeasonEventYear != league.SeasonYear) { fo.SeasonEventYear = league.SeasonYear; fo.SeasonEventCount = 0; fo.SeasonEventQuietWeeks = 0; }
            foreach (var t in league.Teams.Values)
            {
                if (t.SeasonEventTeamworkWeeks <= 0) continue;
                t.SeasonEventTeamworkWeeks--;
                if (t.SeasonEventTeamworkWeeks <= 0) t.SeasonEventTeamwork = 0;
            }
        }

        private static GMSeasonEventCooldown CooldownOf(GMFrontOfficeState fo, GMSeasonEventKind k, int year)
        {
            string key = k.ToString();
            var c = fo.SeasonEventCooldowns.FirstOrDefault(x => x.Kind == key);
            if (c == null) { c = new GMSeasonEventCooldown { Kind = key, Year = year, LastWeek = -99 }; fo.SeasonEventCooldowns.Add(c); }
            if (c.Year != year) { c.Year = year; c.Count = 0; c.LastWeek = -99; }
            return c;
        }

        public static bool Ready(GMLeagueState league, GMSeasonEventKind k, int week)
        {
            var c = CooldownOf(GMFrontOffice.Ensure(league), k, league.SeasonYear);
            return c.Count < SeasonMax(k) && week - c.LastWeek >= Cooldown(k);
        }

        /// <summary>
        /// 주간 종료 사건 판정(0~3건, 우선순위 순). 사건마다 쿨다운 · 시즌 횟수를 기록하고 소식(Decision)을 남긴다. 사건이 없어도 조용한 주 카운터를 갱신한다.
        /// </summary>
        public static List<GMSeasonEvent> Generate(GMLiveSeasonSimulator sim)
        {
            var result = new List<GMSeasonEvent>();
            var league = sim?.League;
            if (!Enabled(league)) return result;
            int played = league.GamesPlayed;
            if (!IsWeekEnd(played)) return result;
            var fo = GMFrontOffice.Ensure(league);
            if (fo.SeasonEventYear != league.SeasonYear) { fo.SeasonEventYear = league.SeasonYear; fo.SeasonEventCount = 0; fo.SeasonEventQuietWeeks = 0; }
            int week = WeekOf(played);
            if (week < FirstEventWeek || fo.SeasonEventCount >= MaxPerSeason) return result;
            var rng = new Random(league.Seed ^ (league.SeasonYear * 7919) ^ (week * 104729) ^ 0x5EA5);
            bool pennant = fo.Manager?.PennantMode ?? false;
            var candidates = Candidates(sim, week, rng).Where(e => !pennant || e.Priority <= GMEventPriority.P1).OrderBy(e => e.Priority).ToList();
            foreach (var ev in candidates)
            {
                if (result.Count >= MaxPerWeek || fo.SeasonEventCount + result.Count >= MaxPerSeason) break;
                double roll = rng.NextDouble();
                bool take = ev.Priority == GMEventPriority.P0 || (ev.Priority == GMEventPriority.P1 ? roll < P1Chance : ev.Priority == GMEventPriority.P2 && roll < P2Chance);
                if (take) result.Add(ev);
            }
            if (result.Count == 0 && candidates.Count > 0 && fo.SeasonEventQuietWeeks + 1 >= ForceAfterQuietWeeks) result.Add(candidates[0]); // 연속 3주 무사건 방지
            fo.SeasonEventQuietWeeks = result.Count == 0 ? fo.SeasonEventQuietWeeks + 1 : 0;
            string summary = WeekSummary(sim, week);
            foreach (var ev in result)
            {
                var c = CooldownOf(fo, ev.Kind, league.SeasonYear);
                c.LastWeek = week;
                c.Count++;
                fo.SeasonEventCount++;
                ev.WeekSummary = summary;
                league.AddNews(new GMNewsItem
                {
                    GameIndex = Math.Max(0, played - 1), DateLabel = sim.DayLabel(Math.Max(0, played - 1)), Kind = GMNewsKind.Decision, IsUserTeam = true, IsMajor = ev.Priority <= GMEventPriority.P1,
                    Title = $"[단장 개입] {ev.Title}", Body = ev.Situation,
                });
            }
            return result;
        }

        /// <summary>이번 주 결산 한 줄 - "N주차 결산 · 이번 주 3승 3패 · 현재 4위(승차 2.5)".</summary>
        public static string WeekSummary(GMLiveSeasonSimulator sim, int week)
        {
            var league = sim.League;
            int end = league.GamesPlayed, start = week <= 0 ? 0 : end - WeekGames;
            var games = league.UserResults.Where(r => r.Day >= start && r.Day < end).ToList();
            int w = games.Count(g => g.My > g.Their), l = games.Count(g => g.My < g.Their), d = games.Count - w - l;
            var standings = sim.Standings();
            int rank = standings.FindIndex(r => r.TeamCode == league.SelectedTeamCode) + 1;
            var me = league.RecordOf(league.SelectedTeamCode);
            double gb = standings.Count > 0 ? GMLiveSeasonSimulator.GamesBehind(standings[0], me) : 0;
            return $"{week}주차 결산 · 이번 주 {w}승 {l}패{(d > 0 ? $" {d}무" : "")} · 시즌 {me.W}승 {me.D}무 {me.L}패 · 현재 {rank}위(승차 {GMLiveSeasonSimulator.GamesBehindLabel(gb)})";
        }

        // ================================================================== 후보 판정

        private static List<GMSeasonEvent> Candidates(GMLiveSeasonSimulator sim, int week, Random rng)
        {
            var league = sim.League;
            var team = league.UserTeam;
            var list = new List<GMSeasonEvent>();
            void Add(GMSeasonEventKind kind, Func<GMSeasonEvent> build)
            {
                if (!Ready(league, kind, week)) return; // 쿨다운 · 시즌 상한 - 판정 비용(트레이드 평가 등)을 아낀다
                var e = build();
                if (e != null) list.Add(e);
            }
            Add(GMSeasonEventKind.StarInjury, () => StarInjury(league, team, week));
            Add(GMSeasonEventKind.AceSlump, () => AceSlump(league, team, week));
            Add(GMSeasonEventKind.ProspectBoom, () => ProspectBoom(league, team, week, rng));
            Add(GMSeasonEventKind.LosingStreak, () => LosingStreak(league, team, week));
            Add(GMSeasonEventKind.PennantRace, () => PennantRace(sim, team, week));
            Add(GMSeasonEventKind.TradeOffer, () => TradeOffer(league, team, week, rng));
            Add(GMSeasonEventKind.ContractAnxiety, () => ContractAnxiety(league, team, week));
            Add(GMSeasonEventKind.FanDrop, () => FanDrop(league, team, week));
            Add(GMSeasonEventKind.OwnerCheck, () => OwnerCheck(sim, team, week));
            Add(GMSeasonEventKind.ManagerConflict, () => ManagerConflict(league, team, week, rng));
            Add(GMSeasonEventKind.ManagerDemotion, () => ManagerDemotion(league, team, week)); // [TASK-GM-19] 2티어
            Add(GMSeasonEventKind.RivalSigning, () => RivalSigning(sim, team, week));
            return list;
        }

        private static GMSeasonEvent New(GMLeagueState league, GMSeasonEventKind kind, GMEventPriority pr, int week, string title, string situation)
        {
            var e = new GMSeasonEvent { Kind = kind, Priority = pr, Year = league.SeasonYear, Week = week, Day = league.GamesPlayed, Title = title, Situation = situation };
            return e;
        }

        private static void Choice(GMSeasonEvent e, string id, string label, string effect, bool isDefault = false, string block = "")
        {
            if (isDefault) e.DefaultChoice = e.Choices.Count;
            e.Choices.Add(new GMSeasonEventChoice { Id = id, Label = label, Effect = effect, Available = block == "", BlockReason = block });
        }

        private static string Name(Player p) => p?.Template?.PlayerName ?? "-";
        private static string Pos(Player p) => p == null ? "" : GMFrontOffice.PositionLabel(p.Position);
        private static int OvrRank(GMTeamState team, Player p) => team.Roster.OrderByDescending(x => x.BaseOverall).ToList().IndexOf(p) + 1;

        private static Player BestFutures(GMTeamState team, bool pitcher) =>
            team.Futures.Where(x => x?.Template != null && x.IsPitcher == pitcher && x.InjuryRemainingDays <= 0).OrderByDescending(x => x.BaseOverall).ThenByDescending(x => x.Potential).FirstOrDefault();

        private static GMSeasonEvent StarInjury(GMLeagueState league, GMTeamState team, int week)
        {
            var p = team.Roster.Where(x => x.InjuryRemainingDays >= StarInjuryDays && OvrRank(team, x) <= 8).OrderByDescending(x => x.BaseOverall).FirstOrDefault();
            if (p == null) return null;
            var e = New(league, GMSeasonEventKind.StarInjury, GMEventPriority.P0, week, $"{Name(p)}의 장기 부상 - 복귀 계획을 정해야 합니다",
                $"구단 의료진 보고: {Pos(p)} {Name(p)}(OVR {p.BaseOverall} · 팀 내 {OvrRank(team, p)}위)이(가) 약 {p.InjuryRemainingDays}일 결장 예정입니다. 감독은 대체 선수 콜업을 요구하고 있습니다.");
            e.Player = p;
            var sub = BestFutures(team, p.IsPitcher);
            e.Partner = sub;
            e.Facts.Add($"선수: {Pos(p)} {Name(p)} · OVR {p.BaseOverall} · 잔여 {p.InjuryRemainingDays}일");
            e.Facts.Add($"퓨처스 대체 후보: {(sub != null ? $"{Pos(sub)} {Name(sub)} OVR {sub.BaseOverall}" : "없음")}");
            Choice(e, "SAFE", "안전 회복 (의료진 권고)", "복귀 일정 유지 · 재부상 위험 없음 · 선수 만족 +2", true);
            Choice(e, "RUSH", "재활 가속 · 조기 복귀", $"결장 기간 약 45% 단축 · {RehabSetbackChance * 100:0}% 확률로 재발(+7일) · 만족 -3");
            Choice(e, "CALLUP", "퓨처스 대체 선수 콜업", sub == null ? "" : $"{Name(sub)} 1군 콜업(1군이 가득 차면 부상자를 퓨처스로 이관) · 유망주 충성도 +5",
                block: sub == null ? "콜업할 퓨처스 선수가 없습니다." : "");
            return e;
        }

        private static GMSeasonEvent AceSlump(GMLeagueState league, GMTeamState team, int week)
        {
            var rotation = StartingRotation.RotationOf(team.Roster, team.Lineup).Where(x => x != null && x.InjuryRemainingDays <= 0).ToList();
            foreach (var p in rotation.OrderByDescending(x => x.BaseOverall).Take(3))
            {
                if (!league.Stats.TryGetValue(p.InstanceId, out var s) || s.OutsPitched < AceSlumpMinOuts) continue;
                double era = s.ER * 27.0 / Math.Max(1, s.OutsPitched);
                if (era < AceSlumpEra) continue;
                var e = New(league, GMSeasonEventKind.AceSlump, GMEventPriority.P1, week, $"선발 {Name(p)} 부진 - 평균자책점 {era:0.00}",
                    $"{Name(p)}(OVR {p.BaseOverall})이(가) {s.OutsPitched / 3}이닝 평균자책점 {era:0.00}으로 흔들리고 있습니다. 투수코치는 휴식을, 선수 본인은 등판 유지를 원합니다.");
                e.Player = p;
                e.Facts.Add($"시즌 {s.W}승 {s.L}패 · {s.OutsPitched / 3}.{s.OutsPitched % 3}이닝 · 평균자책점 {era:0.00} · 개인 만족도 {p.PersonalMorale}");
                Choice(e, "TRUST", "로테이션 유지 (믿고 맡긴다)", "선수 만족 +3 · 부진이 이어질 위험", true);
                Choice(e, "REST", "열흘 휴식 · 컨디션 조정", "6경기 결장(로테이션 제외) · 만족 +5 · 다음 등판 체력 회복");
                Choice(e, "COACH", "투수코치 집중 지도 (1억 원)", "운영 예산 -1억 · 만족 +8 · 충성도 +3 · 선수단 신뢰도 +1",
                    block: team.Budget < CoachCost ? "운영 예산이 부족합니다." : "");
                return e;
            }
            return null;
        }

        private static GMSeasonEvent ProspectBoom(GMLeagueState league, GMTeamState team, int week, Random rng)
        {
            var p = team.Futures.Where(x => x?.Template != null && x.Age <= 24 && x.Potential - x.BaseOverall >= 8 && x.InjuryRemainingDays <= 0)
                .OrderByDescending(x => x.Potential).ThenByDescending(x => x.BaseOverall).FirstOrDefault();
            if (p == null || rng.NextDouble() >= ProspectBoomChance) return null;
            var e = New(league, GMSeasonEventKind.ProspectBoom, GMEventPriority.P1, week, $"퓨처스 {Name(p)} 폭발 - 1군 콜업 요청",
                $"퓨처스 코칭스태프 보고: {p.Age}세 {Pos(p)} {Name(p)}(OVR {p.BaseOverall} · 잠재력 {p.Potential})이(가) 2군 무대를 폭격하고 있습니다.");
            e.Player = p;
            var swap = SwapCandidate(team, p.IsPitcher);
            e.Partner = swap;
            e.Facts.Add($"잠재력 {GMFuturesMeeting.PotentialMin(p)}~{GMFuturesMeeting.PotentialMax(p)} · {GMFuturesMeeting.GrowthTypeLabel(GMFuturesMeeting.TypeOf(p))}");
            if (team.Roster.Count >= GMRosterTiers.FirstTeamMax) e.Facts.Add($"1군 가득 - 콜업 시 {(swap != null ? $"{Name(swap)}(OVR {swap.BaseOverall})" : "맞바꿀 선수 없음")}와(과) 스왑");
            bool canCall = team.Roster.Count < GMRosterTiers.FirstTeamMax || swap != null;
            Choice(e, "CALLUP", "1군 콜업", "즉시 1군 합류 · 유망주 만족 +8 · 충성도 +5 · 스왑 대상 만족 -5", block: canCall ? "" : "맞바꿀 1군 선수가 없습니다.");
            Choice(e, "DEVELOP", "퓨처스 육성 유지", "만족 +5 · 충성도 +3 · 연말 성장 판정은 육성 회의실 기준 그대로", true);
            Choice(e, "SHOWCASE", "트레이드 카드로 활용", "트레이드 시장 공개(다음 제안 대상) · 유망주 충성도 -10");
            return e;
        }

        private static Player SwapCandidate(GMTeamState team, bool pitcher) =>
            team.Roster.Where(x => x.IsPitcher == pitcher && !x.IsCaptain).OrderBy(x => x.BaseOverall).ThenBy(x => x.InstanceId, StringComparer.Ordinal).FirstOrDefault();

        private static GMSeasonEvent LosingStreak(GMLeagueState league, GMTeamState team, int week)
        {
            var rec = league.RecordOf(team.TeamCode);
            if (rec.Streak > -5) return null;
            var e = New(league, GMSeasonEventKind.LosingStreak, GMEventPriority.P1, week, $"{-rec.Streak}연패 - 선수단 분위기가 가라앉았습니다",
                $"{-rec.Streak}연패 수렁. 라커룸이 조용해졌고 언론은 단장의 대응을 주목하고 있습니다.");
            e.Facts.Add($"시즌 {rec.W}승 {rec.D}무 {rec.L}패 · 선수단 신뢰도 {team.LockerRoomTrust} · 마케팅 예산 {GMDiagnosticFormat.Short(team.MarketingBudget)}");
            Choice(e, "MEETING", "단장 주재 선수단 미팅", "선수단 신뢰도 +3 · 1군 만족 +5 · 팀워크 +2(3주)");
            Choice(e, "TRUST_MANAGER", "감독에게 전권 · 지켜보기", "변화 없음 - 연패가 길어지면 다음 주 다시 판단", true);
            Choice(e, "CHEER_RALLY", "응원단 특별 응원전 (마케팅 예산 3,000만)", "마케팅 예산 -3,000만 · 팬 지지율 +3 · 팀워크 +1(2주)",
                block: team.MarketingBudget < CheerRallyCost ? "마케팅 예산이 부족합니다." : "");
            return e;
        }

        private static GMSeasonEvent PennantRace(GMLiveSeasonSimulator sim, GMTeamState team, int week)
        {
            if (week < PennantWeek) return null;
            var league = sim.League;
            var standings = sim.Standings();
            int rank = standings.FindIndex(r => r.TeamCode == team.TeamCode) + 1;
            if (rank <= 0 || standings.Count < 6) return null;
            var me = standings[rank - 1];
            double gbFirst = GMLiveSeasonSimulator.GamesBehind(standings[0], me);
            double gbFifth = rank > 5 ? GMLiveSeasonSimulator.GamesBehind(standings[4], me) : 0;
            bool race = (rank > 1 && gbFirst <= 3) || (rank > 5 && gbFifth <= 2);
            if (!race) return null;
            string goal = rank > 5 ? $"5위와 {GMLiveSeasonSimulator.GamesBehindLabel(gbFifth)}경기 차 - 가을야구 경쟁" : $"1위와 {GMLiveSeasonSimulator.GamesBehindLabel(gbFirst)}경기 차 - 선두 경쟁";
            var e = New(league, GMSeasonEventKind.PennantRace, GMEventPriority.P1, week, $"{goal}", $"현재 {rank}위. 남은 {GMLiveSeasonSimulator.SeasonGames - league.GamesPlayed}경기 - 승부처에서 프런트의 결단이 필요합니다.");
            e.Facts.Add($"운영 예산 {GMDiagnosticFormat.Short(team.Budget)} · 구단주 신임도 {GMFrontOffice.Ensure(league).OwnerTrust}");
            Choice(e, "BOOST", "즉시 보강 투자 (예산 5억)", "운영 예산 -5억 · 팀워크 +3(4주) · 구단주 신임도 +2", block: team.Budget < BoostCost ? "운영 예산이 부족합니다." : "");
            Choice(e, "HOLD", "현 전력 유지", "변화 없음 · 재정 보존", true);
            Choice(e, "YOUTH", "유망주에게 기회 부여", "팀워크 -1(2주) · 퓨처스 유망주 충성도 +5");
            return e;
        }

        private static GMSeasonEvent TradeOffer(GMLeagueState league, GMTeamState team, int week, Random rng)
        {
            if (week < TradeWeek || GMLeagueRules.IsPastTradeDeadline(league)) return null;
            var fo = GMFrontOffice.Ensure(league);
            var partners = league.Teams.Values.Where(t => !t.IsUserTeam).OrderBy(t => t.TeamCode, StringComparer.Ordinal).ToList();
            if (partners.Count == 0) return null;
            var block = string.IsNullOrEmpty(fo.SeasonTradeBlockId) ? null : team.ReservePlayers.FirstOrDefault(x => x.InstanceId == fo.SeasonTradeBlockId);
            int start = rng.Next(partners.Count);
            for (int k = 0; k < partners.Count; k++)
            {
                var partner = partners[(start + k) % partners.Count];
                var needs = GMTradeAI.AnalyzeNeeds(league, partner);
                var pool = block != null ? new List<Player> { block } :
                    team.Roster.Where(x => !x.IsCaptain && x.InjuryRemainingDays <= 0).OrderByDescending(x => GMTradeAI.NeedFit(needs, x, out _)).ThenByDescending(x => x.BaseOverall).Take(4).ToList();
                foreach (var mine in pool)
                {
                    float value = GMStoveLeagueMarket.TradeValue(mine);
                    var theirs = partner.Roster.Where(x => x.InjuryRemainingDays <= 0 && !x.IsCaptain)
                        .Where(x => GMStoveLeagueMarket.Evaluate(league, team, new[] { mine }, partner, new[] { x }).Acceptable && GMStoveLeagueMarket.TradeValue(x) >= value * 0.8f)
                        .OrderByDescending(x => x.BaseOverall).FirstOrDefault();
                    if (theirs == null) continue;
                    var e = New(league, GMSeasonEventKind.TradeOffer, GMEventPriority.P1, week, $"{partner.DisplayName}의 1:1 트레이드 제안",
                        $"{partner.DisplayName} 단장이 전화를 걸어 왔습니다: \"{Name(mine)}을(를) 주시면 {Name(theirs)}을(를) 보내겠습니다.\" ({GMTradeAI.NeedsLabel(needs)})");
                    e.Player = mine;
                    e.Partner = theirs;
                    e.PartnerTeam = partner.TeamCode;
                    e.Facts.Add($"보냄: {Pos(mine)} {Name(mine)} · OVR {mine.BaseOverall} · {mine.Age}세 · 연봉 {GMDiagnosticFormat.Short(mine.Salary)}");
                    e.Facts.Add($"받음: {Pos(theirs)} {Name(theirs)} · OVR {theirs.BaseOverall} · {theirs.Age}세 · 연봉 {GMDiagnosticFormat.Short(theirs.Salary)}");
                    e.Facts.Add($"마감: {league.SeasonYear}년 7월 31일(KBO 규정)");
                    Choice(e, "ACCEPT", "수락", "즉시 트레이드 성사 · 로스터 재구성");
                    Choice(e, "DECLINE", "거절", "변화 없음", true);
                    Choice(e, "RENEGOTIATE", "재협상 (현금 1억 추가 요구)", $"{RenegotiateChance * 100:0}% 확률로 성사 + 상대 구단 1억 지급 · 실패 시 제안 철회");
                    return e;
                }
            }
            return null;
        }

        private static GMSeasonEvent ContractAnxiety(GMLeagueState league, GMTeamState team, int week)
        {
            if (week < ContractWeek) return null;
            var p = team.Roster.Where(x => x.ContractYears <= 1 && x.BaseOverall >= 72 && x.Loyalty < 70 && x.InjuryRemainingDays <= 0).OrderByDescending(x => x.BaseOverall).FirstOrDefault();
            if (p == null) return null;
            int demand = GMNegotiationRoom.DemandOf(league, p);
            int years = GMStoveLeagueMarket.PreferredYears(p);
            long bonus = (long)demand * years * GMStoveLeagueMarket.ExtensionBonusPercent / 100;
            var e = New(league, GMSeasonEventKind.ContractAnxiety, GMEventPriority.P2, week, $"{Name(p)} 측, 재계약 지연에 불만",
                $"계약 마지막 해인 {Name(p)}(OVR {p.BaseOverall} · 충성도 {p.Loyalty})의 에이전트가 \"시즌 중 결론이 없으면 시장 평가를 받겠다\"고 통보했습니다.");
            e.Player = p;
            e.Facts.Add($"요구: {GMDiagnosticFormat.Short(demand)} × {years}년 · 계약금 {GMDiagnosticFormat.Short(bonus)} · 운영 예산 {GMDiagnosticFormat.Short(team.Budget)}");
            Choice(e, "EXTEND", "조기 연장 계약 제안 (요구액 수용)", $"{years}년 연장 · 연봉 {GMDiagnosticFormat.Short(demand)} · 충성도 +6", block: team.Budget < bonus ? "계약금을 낼 운영 예산이 부족합니다." : "");
            Choice(e, "WAIT", "시즌 후 협상으로 미룸", "충성도 -2 · 스토브리그 계약 협상실에서 처리", true);
            Choice(e, "SHOP", "트레이드 검토 통보", "충성도 -12 · 만족 -10 · 선수단 신뢰도 -2 · 트레이드 시장 공개");
            return e;
        }

        private static GMSeasonEvent FanDrop(GMLeagueState league, GMTeamState team, int week)
        {
            if (week < FanWeek) return null;
            var rec = league.RecordOf(team.TeamCode);
            if (team.FanSupport >= 45 && !(rec.G >= 30 && rec.Pct < 0.42)) return null;
            var e = New(league, GMSeasonEventKind.FanDrop, GMEventPriority.P2, week, "홈 관중 감소 · 팬심 이탈 경고",
                $"마케팅팀 보고: 팬 지지율 {team.FanSupport} · 승률 {GMTeamRecord.PctLabel(rec.Pct)}. 홈경기 빈자리가 늘고 있습니다.");
            e.Facts.Add($"마케팅 예산 {GMDiagnosticFormat.Short(team.MarketingBudget)} · 운영 예산 {GMDiagnosticFormat.Short(team.Budget)}");
            Choice(e, "CHEER_THEME", "치어리더 테마 데이 (마케팅 예산 5,000만)", "마케팅 예산 -5,000만 · 팬 지지율 +6 · 1군 만족 +2", block: team.MarketingBudget < CheerThemeCost ? "마케팅 예산이 부족합니다." : "");
            Choice(e, "TICKET", "티켓 할인 프로모션 (운영 예산 2억)", "운영 예산 -2억 · 팬 지지율 +4", block: team.Budget < TicketCost ? "운영 예산이 부족합니다." : "");
            Choice(e, "IGNORE", "대응하지 않음", "팬 지지율 -2", true);
            return e;
        }

        private static GMSeasonEvent OwnerCheck(GMLiveSeasonSimulator sim, GMTeamState team, int week)
        {
            var league = sim.League;
            if (league.GamesPlayed < OwnerCheckGames) return null;
            var fo = GMFrontOffice.Ensure(league);
            var standings = sim.Standings();
            int rank = standings.FindIndex(r => r.TeamCode == team.TeamCode) + 1;
            int target = Math.Max(1, fo.Owner?.ExpectedRank ?? 5);
            int rate = Math.Max(0, Math.Min(150, (int)Math.Round(100.0 * (11 - rank) / Math.Max(1, 11 - target))));
            var e = New(league, GMSeasonEventKind.OwnerCheck, GMEventPriority.P1, week, $"구단주 중간 점검 - 목표 달성률 {rate}%",
                $"{(string.IsNullOrEmpty(fo.Owner?.OwnerName) ? "구단주가" : fo.Owner.OwnerName + " 구단주가")} 반환점 보고를 요구했습니다. 시즌 목표 {target}위 · 현재 {rank}위({league.GamesPlayed}경기).");
            e.Facts.Add($"목표 달성률 {rate}% · 구단주 신임도 {fo.OwnerTrust} · 운영 예산 {GMDiagnosticFormat.Short(team.Budget)} · 페이롤 {GMDiagnosticFormat.Short(team.Payroll)}/{GMDiagnosticFormat.Short(team.PayrollCap)}");
            e.Facts.Add(rate >= 100 ? "구단주: \"이대로만 가 주시오.\"" : rate >= 70 ? "구단주: \"아직 기회는 있소. 방법을 가져오시오.\"" : "구단주: \"이 성적으로는 곤란하오.\"");
            Choice(e, "PLEDGE", $"목표 재확인 · {target}위 이내 약속", "구단주 신임도 +3 · 시즌 종료 시 목표 미달이면 -8(기록된 약속)");
            Choice(e, "SAVINGS", "재정 절감 보고", "운영 예산 +3억(운영비 환원) · 구단주 성향이 흑자형이면 신임도 +2, 아니면 -2");
            Choice(e, "DEVELOP", "육성 기조 보고", "변화 없음 · 내년 기대치는 그대로", true);
            return e;
        }

        private static GMSeasonEvent ManagerConflict(GMLeagueState league, GMTeamState team, int week, Random rng)
        {
            if (week < ConflictWeek) return null;
            var rec = league.RecordOf(team.TeamCode);
            bool trigger = (rec.G >= 20 && rec.Pct < 0.45 && team.LockerRoomTrust < 55) || (rec.Streak <= -3 && rng.NextDouble() < ManagerConflictChance);
            if (!trigger) return null;
            var e = New(league, GMSeasonEventKind.ManagerConflict, GMEventPriority.P2, week, "감독, 선수 기용에 대한 프런트 개입에 불만",
                $"감독이 \"현장은 현장에 맡겨 달라\"며 공개적으로 불만을 드러냈습니다. 승률 {GMTeamRecord.PctLabel(rec.Pct)} · 선수단 신뢰도 {team.LockerRoomTrust}.");
            e.Facts.Add($"구단주 신임도 {GMFrontOffice.Ensure(league).OwnerTrust} · 운영 예산 {GMDiagnosticFormat.Short(team.Budget)}");
            Choice(e, "BACK_MANAGER", "감독 지지 (전권 위임)", "선수단 신뢰도 +1 · 프런트 영향력 유지", true);
            Choice(e, "INTERVENE", "단장 직접 개입 (기용 지시)", "팀워크 -2(3주) · 구단주 신임도 +2 · 선수단 신뢰도 -4");
            Choice(e, "COACHES", "코치진 교체 (1억 원)", "운영 예산 -1억 · 팀워크 +2(3주) · 선수단 신뢰도 +2", block: team.Budget < CoachCost ? "운영 예산이 부족합니다." : "");
            return e;
        }

        // ================================================================== [TASK-GM-19] 2티어 사건

        public const int DemotionStreak = 5, DemotionSalaryRank = 5;
        public const double DemotionMaxWar = 1.0;
        public const int SupportLoyalty = -20, SupportMorale = -15, SupportManagerTrust = 6, OverruleManagerTrust = -20, OverruleLoyalty = 5;
        public const int MediateManagerTrust = 2, MediateFailManagerTrust = -6, MediateLoyalty = 3, MediateFailLoyalty = -6;
        public const int SClassOvr = 80, CounterTargetOvr = 75, CounterPledgeFan = 4, CounterFailTrust = -8, CounterFailFan = -4;
        public const long CheerBigEventCost = 8000;
        public const int CheerBigEventFan = 5, YouthFan = -2, YouthLoyalty = 6, YouthTrust = 2;

        /// <summary>전통 라이벌(잠실 · 낙동강 · 영호남 · 통신사 계보 · 그 밖).</summary>
        public static string TraditionalRival(string code)
        {
            switch (code)
            {
                case "LG": return "DOO";
                case "DOO": return "LG";
                case "LOT": return "NC";
                case "NC": return "LOT";
                case "SAM": return "KIA";
                case "KIA": return "SAM";
                case "SSG": return "KT";
                case "KT": return "SSG";
                case "HAN": return "KIW";
                case "KIW": return "HAN";
                default: return "";
            }
        }

        /// <summary>라이벌 = 전통 라이벌 + 현재 순위 ±2 이내 구단.</summary>
        public static bool IsRival(GMLiveSeasonSimulator sim, string userCode, string otherCode)
        {
            if (string.IsNullOrEmpty(otherCode) || otherCode == userCode) return false;
            if (TraditionalRival(userCode) == otherCode) return true;
            var standings = sim?.Standings();
            if (standings == null || standings.Count == 0) return false;
            int me = standings.FindIndex(r => r.TeamCode == userCode), them = standings.FindIndex(r => r.TeamCode == otherCode);
            return me >= 0 && them >= 0 && Math.Abs(me - them) <= 2;
        }

        public static bool IsSClass(Player p) => p?.Template != null && (GMStarterDeck.TierOf(p) == GMStarterTier.S || p.BaseOverall >= SClassOvr);

        /// <summary>부진 판정용 시즌 WAR(기록 없음 = 0).</summary>
        private static double SeasonWar(GMLeagueState league, Player p) => league.Stats.TryGetValue(p.InstanceId, out var s) ? s.WAR : 0.0;

        /// <summary>[감독과의 갈등] 대상 - 1군 연봉 상위 5명 중 시즌 WAR 최저(1.0 미만) 선수(부상 · 주장 제외).</summary>
        public static Player DemotionTarget(GMLeagueState league, GMTeamState team) =>
            team.Roster.Where(x => x?.Template != null && x.InjuryRemainingDays <= 0 && !x.IsCaptain)
                .OrderByDescending(x => x.Salary).ThenBy(x => x.InstanceId, StringComparer.Ordinal).Take(DemotionSalaryRank)
                .Where(x => SeasonWar(league, x) < DemotionMaxWar)
                .OrderBy(x => SeasonWar(league, x)).ThenBy(x => x.BaseOverall).FirstOrDefault();

        private static GMSeasonEvent ManagerDemotion(GMLeagueState league, GMTeamState team, int week)
        {
            var rec = league.RecordOf(team.TeamCode);
            if (rec.Streak > -DemotionStreak) return null;
            var p = DemotionTarget(league, team);
            if (p == null) return null;
            league.Stats.TryGetValue(p.InstanceId, out var s);
            string line = s == null ? "시즌 기록 없음" : p.IsPitcher ? $"{s.OutsPitched / 3}이닝 평균자책점 {(s.OutsPitched > 0 ? s.ER * 27.0 / s.OutsPitched : 0):0.00} · WAR {s.WAR:0.0}"
                : $"{s.PA}타석 타율 {GMTeamRecord.PctLabel(s.AVG)} · {s.HR}홈런 · WAR {s.WAR:0.0}";
            var e = New(league, GMSeasonEventKind.ManagerDemotion, GMEventPriority.P0, week, $"감독 \"{Name(p)}, 2군으로 내리겠습니다\"",
                $"{-rec.Streak}연패 끝에 감독이 단장실을 찾았습니다. 연봉 {GMDiagnosticFormat.Short(p.Salary)}의 {Pos(p)} {Name(p)}을(를) 퓨처스로 내려 분위기를 바꾸겠다는 통보입니다.");
            e.Player = p;
            e.Facts.Add($"대상: {Pos(p)} {Name(p)} · OVR {p.BaseOverall} · 연봉 {GMDiagnosticFormat.Short(p.Salary)}(팀 내 상위 {DemotionSalaryRank}위 이내) · {line}");
            e.Facts.Add($"감독 신뢰도 {team.ManagerTrust} · 선수 충성도 {p.Loyalty} · 자존심 {p.EgoLevel} · 선수단 신뢰도 {team.LockerRoomTrust}");
            bool canDown = team.Futures.Count < GMRosterTiers.FuturesMax;
            Choice(e, "SUPPORT", "감독 지지 (2군 강등 수용)", $"{Name(p)} 퓨처스 강등 · 충성도 {SupportLoyalty} · 만족 {SupportMorale} · 감독 신뢰도 +{SupportManagerTrust}",
                block: canDown ? "" : "퓨처스 풀이 가득 차 강등할 수 없습니다.");
            Choice(e, "OVERRULE", "단장 권한으로 1군 유지 강제", $"감독 신뢰도 {OverruleManagerTrust} · 선수 충성도 +{OverruleLoyalty} · 팀워크 -2(3주)");
            Choice(e, "MEDIATE", "감독 · 선수 면담 중재", $"성공 확률 {MediateChance(team) * 100:0}% - 성공: 1군 유지 · 감독 신뢰도 +{MediateManagerTrust} · 충성도 +{MediateLoyalty} / 실패: 감독 신뢰도 {MediateFailManagerTrust} · 충성도 {MediateFailLoyalty}", true);
            return e;
        }

        /// <summary>면담 중재 성공 확률 = 0.45 + (선수단 · 감독 신뢰도 평균 - 50) × 1%p (20~85%).</summary>
        public static double MediateChance(GMTeamState team) =>
            Math.Max(0.2, Math.Min(0.85, 0.45 + ((team.LockerRoomTrust + team.ManagerTrust) / 2.0 - 50) * 0.01));

        /// <summary>올해 라이벌 구단이 영입한 S급 선수(아직 사건으로 다루지 않은) - 커리어 타임라인의 FA · 트레이드 기록으로 찾는다.</summary>
        public static (Player player, string teamCode, string via) FindRivalSigning(GMLiveSeasonSimulator sim)
        {
            var league = sim.League;
            var fo = GMFrontOffice.Ensure(league);
            string user = league.SelectedTeamCode;
            foreach (var team in league.Teams.Values.Where(t => !t.IsUserTeam).OrderBy(t => t.TeamCode, StringComparer.Ordinal))
            {
                if (!IsRival(sim, user, team.TeamCode)) continue;
                foreach (var p in team.Roster.Where(IsSClass).OrderByDescending(x => x.BaseOverall).ThenBy(x => x.InstanceId, StringComparer.Ordinal))
                {
                    var ev = (p.CareerHistory ?? new List<GMCareerEvent>()).LastOrDefault(x => x != null && x.Year == league.SeasonYear && x.TeamCode == team.TeamCode &&
                                                                   (x.Kind == (int)GMCareerEventKind.FaSigning || x.Kind == (int)GMCareerEventKind.Trade));
                    if (ev == null || fo.RivalSigningSeen.Contains($"{league.SeasonYear}|{p.InstanceId}")) continue;
                    return (p, team.TeamCode, ev.Kind == (int)GMCareerEventKind.FaSigning ? "FA" : "트레이드");
                }
            }
            return (null, null, null);
        }

        private static GMSeasonEvent RivalSigning(GMLiveSeasonSimulator sim, GMTeamState team, int week)
        {
            var league = sim.League;
            var (p, code, via) = FindRivalSigning(sim);
            if (p == null) return null;
            var fo = GMFrontOffice.Ensure(league);
            fo.RivalSigningSeen.Add($"{league.SeasonYear}|{p.InstanceId}");
            while (fo.RivalSigningSeen.Count > 60) fo.RivalSigningSeen.RemoveAt(0);
            string rival = NameAliasTable.DisplayTeamName(code);
            bool traditional = TraditionalRival(team.TeamCode) == code;
            var e = New(league, GMSeasonEventKind.RivalSigning, GMEventPriority.P1, week, $"{(traditional ? "전통의 라이벌" : "순위 경쟁 구단")} {rival}, S급 {Name(p)} {via} 영입",
                $"{rival}이(가) {via}로 {Pos(p)} {Name(p)}(OVR {p.BaseOverall})을(를) 품었습니다. 팬 커뮤니티에는 \"우리 프런트는 뭐 하냐\"는 글이 쏟아지고 있습니다.");
            e.Player = p;
            e.PartnerTeam = code;
            bool deadlinePassed = GMLeagueRules.IsPastTradeDeadline(league);
            e.Facts.Add($"영입 선수: {Pos(p)} {Name(p)} · OVR {p.BaseOverall} · {p.Age}세 · 연봉 {GMDiagnosticFormat.Short(p.Salary)}");
            e.Facts.Add($"팬 지지율 {team.FanSupport} · 마케팅 예산 {GMDiagnosticFormat.Short(team.MarketingBudget)} · 구단주 신임도 {fo.OwnerTrust}");
            Choice(e, "COUNTER", "언론에 맞불 영입 예고", $"팬 지지율 +{CounterPledgeFan} · {league.SeasonYear}년 7월 31일까지 OVR {CounterTargetOvr}+ 선수(FA · 트레이드) 영입 약속 - 미달 시 구단주 신임도 {CounterFailTrust} · 팬 지지율 {CounterFailFan}",
                block: deadlinePassed ? "트레이드 · 영입 마감(7/31)이 지나 공언할 수 없습니다." : fo.CounterPledgeYear == league.SeasonYear ? "이미 올해 맞불 영입을 공언했습니다." : "");
            Choice(e, "YOUTH", "유망주 육성 천명", $"팬 지지율 {YouthFan} · 퓨처스 유망주 충성도 +{YouthLoyalty} · 선수단 신뢰도 +{YouthTrust}", true);
            Choice(e, "CHEER", $"대규모 치어리더 이벤트 (마케팅 예산 {GMDiagnosticFormat.Short(CheerBigEventCost)})", $"마케팅 예산 -{GMDiagnosticFormat.Short(CheerBigEventCost)} · 팬 지지율 +{CheerBigEventFan} · 1군 만족 +2 · 시선 분산",
                block: team.MarketingBudget < CheerBigEventCost ? "마케팅 예산이 부족합니다." : "");
            return e;
        }

        /// <summary>[TASK-GM-19] 검증 · 툴 - 2티어 사건을 판정 조건 그대로 만든다(쿨다운 · 시즌 상한 무시, 조건 미충족 = null).</summary>
        public static GMSeasonEvent BuildManagerDemotion(GMLeagueState league, GMTeamState team, int week) => league == null || team == null ? null : ManagerDemotion(league, team, week);
        public static GMSeasonEvent BuildRivalSigning(GMLiveSeasonSimulator sim, int week) => sim?.League?.UserTeam == null ? null : RivalSigning(sim, sim.League.UserTeam, week);

        private static void ManagerTrust(GMTeamState t, int delta) => t.ManagerTrust = Math.Max(0, Math.Min(100, t.ManagerTrust + delta));

        /// <summary>맞불 영입 공언 이행 여부 - 공언 이후 7/31 마감 안에 OVR 75+ 선수를 FA · 트레이드로 데려왔는지(커리어 타임라인 기준).</summary>
        public static bool CounterPledgeMet(GMLeagueState league)
        {
            var fo = GMFrontOffice.Ensure(league);
            var team = league.UserTeam;
            if (team == null || fo.CounterPledgeYear != league.SeasonYear) return false;
            int deadline = GMLeagueRules.TradeDeadlineGameDay(league);
            return team.ReservePlayers.Any(p => p.BaseOverall >= CounterTargetOvr && (p.CareerHistory ?? new List<GMCareerEvent>()).Any(x => x != null && x.Year == fo.CounterPledgeYear &&
                x.TeamCode == team.TeamCode && x.Day >= fo.CounterPledgeDay && x.Day <= deadline && (x.Kind == (int)GMCareerEventKind.FaSigning || x.Kind == (int)GMCareerEventKind.Trade)));
        }

        // ================================================================== 결과 반영

        private static void Teamwork(GMTeamState team, int delta, int weeks)
        {
            team.SeasonEventTeamwork = Math.Max(TeamworkMin, Math.Min(TeamworkMax, team.SeasonEventTeamwork + delta));
            team.SeasonEventTeamworkWeeks = Math.Max(team.SeasonEventTeamworkWeeks, weeks);
            if (team.SeasonEventTeamwork == 0) team.SeasonEventTeamworkWeeks = 0;
        }

        private static void Morale(Player p, int delta) { if (p != null) p.PersonalMorale = Math.Max(0, Math.Min(100, p.PersonalMorale + delta)); }
        private static void Trust(GMTeamState t, int delta) => t.LockerRoomTrust = Math.Max(0, Math.Min(100, t.LockerRoomTrust + delta));
        private static void Owner(GMLeagueState league, int delta)
        {
            var fo = GMFrontOffice.Ensure(league);
            int floor = fo.Manager != null && fo.Manager.NoFiring ? GMFrontOffice.NoFiringTrustFloor : 0;
            fo.OwnerTrust = Math.Max(floor, Math.Min(GMFrontOffice.MaxTrust, fo.OwnerTrust + delta));
        }

        /// <summary>
        /// 선택지 반영. choiceIndex가 범위 밖이거나 잠긴 선택지면 DefaultChoice로 처리한다(자동 진행 · 잘못된 입력 방지). 결정 로그 · 소식을 남긴다.
        /// </summary>
        public static GMSeasonEventResult Resolve(GMLeagueState league, GMSeasonEvent ev, int choiceIndex, double? rollOverride = null)
        {
            var r = new GMSeasonEventResult();
            if (league == null || ev == null || ev.Resolved) { r.Message = "이미 처리한 사건입니다."; return r; }
            var team = league.UserTeam;
            if (team == null) { r.Message = "내 구단이 없습니다."; return r; }
            if (choiceIndex < 0 || choiceIndex >= ev.Choices.Count || !ev.Choices[choiceIndex].Available) choiceIndex = ev.DefaultChoice;
            var choice = ev.Choices[choiceIndex];
            var rng = new Random(league.Seed ^ (ev.Year * 31) ^ (ev.Week * 977) ^ ((int)ev.Kind * 7907));
            double roll = rollOverride ?? rng.NextDouble();
            var p = ev.Player;
            r.Applied = true;
            r.Success = true;
            r.Sfx = TeamAudioProfile.SynthDeal;
            switch (ev.Kind)
            {
                case GMSeasonEventKind.StarInjury:
                    if (choice.Id == "RUSH" && p != null)
                    {
                        int before = p.InjuryRemainingDays;
                        if (roll < RehabSetbackChance) { p.InjuryRemainingDays = before + 7; r.Success = false; r.Audio = GMAudioEvent.Tension; r.Sfx = TeamAudioProfile.SynthFail; r.Message = $"재활 가속 중 통증 재발 - {Name(p)} 결장 {before}일 → {p.InjuryRemainingDays}일(만족 -3)."; }
                        else { p.InjuryRemainingDays = Math.Max(1, (int)Math.Ceiling(before * 0.55)); r.Message = $"재활 가속 성공 - {Name(p)} 결장 {before}일 → {p.InjuryRemainingDays}일(만족 -3)."; }
                        Morale(p, -3);
                    }
                    else if (choice.Id == "CALLUP" && ev.Partner != null && team.Futures.Contains(ev.Partner))
                    {
                        string msg;
                        bool ok = team.Roster.Count < GMRosterTiers.FirstTeamMax
                            ? GMRosterTiers.CallUp(league, team, ev.Partner, out msg)
                            : p != null && team.Roster.Contains(p) && GMRosterTiers.SendDown(team, p, out msg) && GMRosterTiers.CallUp(league, team, ev.Partner, out msg);
                        if (ok) { ev.Partner.Loyalty += 5; r.Audio = GMAudioEvent.ProspectBoom; r.Message = $"{Name(ev.Partner)} 1군 콜업 - 부상 공백을 메웁니다{(team.Futures.Contains(p) ? $"({Name(p)}은(는) 퓨처스에서 재활)" : "")}."; }
                        else { r.Success = false; r.Sfx = TeamAudioProfile.SynthFail; r.Message = "콜업 실패 - 1군 · 퓨처스 자리가 맞지 않습니다(안전 회복으로 처리)."; }
                    }
                    else { Morale(p, 2); r.Message = $"안전 회복 - {Name(p)}은(는) 의료진 일정대로 {p?.InjuryRemainingDays ?? 0}일 뒤 복귀합니다(만족 +2)."; }
                    break;

                case GMSeasonEventKind.AceSlump:
                    if (choice.Id == "REST" && p != null) { p.InjuryRemainingDays = Math.Max(p.InjuryRemainingDays, WeekGames); Morale(p, 5); r.Message = $"{Name(p)} 열흘 휴식 - {WeekGames}경기 로테이션 제외(만족 +5)."; }
                    else if (choice.Id == "COACH" && p != null) { team.Budget -= CoachCost; Morale(p, 8); p.Loyalty += 3; Trust(team, 1); r.Message = $"투수코치 집중 지도 - {Name(p)} 만족 +8 · 충성도 +3 · 운영 예산 -1억."; }
                    else { Morale(p, 3); r.Message = $"{Name(p)}에게 계속 맡깁니다(만족 +3)."; }
                    break;

                case GMSeasonEventKind.ProspectBoom:
                    if (choice.Id == "CALLUP" && p != null && team.Futures.Contains(p))
                    {
                        string msg;
                        bool ok = team.Roster.Count < GMRosterTiers.FirstTeamMax ? GMRosterTiers.CallUp(league, team, p, out msg) : GMFuturesMeeting.CallUp(league, p, ev.Partner, out msg);
                        if (ok) { Morale(p, 8); p.Loyalty += 5; Morale(ev.Partner, -5); r.Audio = GMAudioEvent.ProspectBoom; r.Message = $"{Name(p)} 1군 콜업! {msg}"; }
                        else { r.Success = false; r.Sfx = TeamAudioProfile.SynthFail; r.Message = "콜업 실패 - " + msg; }
                    }
                    else if (choice.Id == "SHOWCASE" && p != null) { p.Loyalty -= 10; GMFrontOffice.Ensure(league).SeasonTradeBlockId = p.InstanceId; r.Message = $"{Name(p)} 트레이드 시장 공개 - 다음 트레이드 제안의 대상이 됩니다(충성도 -10)."; }
                    else if (p != null) { Morale(p, 5); p.Loyalty += 3; r.Message = $"{Name(p)} 퓨처스 육성 유지 - 만족 +5 · 충성도 +3(연간 성장 상한은 그대로)."; } // OVR은 연도 전환 성장(GrowthCap)으로만 바뀐다
                    break;

                case GMSeasonEventKind.LosingStreak:
                    if (choice.Id == "MEETING") { Trust(team, 3); foreach (var x in team.Roster) Morale(x, 5); Teamwork(team, 2, 3); r.Message = "단장 주재 선수단 미팅 - 선수단 신뢰도 +3 · 1군 만족 +5 · 팀워크 +2(3주)."; }
                    else if (choice.Id == "CHEER_RALLY") { team.MarketingBudget -= CheerRallyCost; team.FanSupport = Math.Min(100, team.FanSupport + 3); Teamwork(team, 1, 2); r.Message = "응원단 특별 응원전 - 마케팅 예산 -3,000만 · 팬 지지율 +3 · 팀워크 +1(2주)."; }
                    else r.Message = "감독에게 전권을 맡기고 지켜봅니다.";
                    break;

                case GMSeasonEventKind.PennantRace:
                    if (choice.Id == "BOOST") { team.Budget -= BoostCost; Teamwork(team, 3, 4); Owner(league, 2); r.Audio = GMAudioEvent.OwnerApproval; r.Message = "즉시 보강 투자 - 운영 예산 -5억 · 팀워크 +3(4주) · 구단주 신임도 +2."; }
                    else if (choice.Id == "YOUTH") { Teamwork(team, -1, 2); foreach (var x in team.Futures) x.Loyalty += 5; r.Message = "유망주에게 기회 - 팀워크 -1(2주) · 퓨처스 충성도 +5."; }
                    else r.Message = "현 전력으로 승부합니다.";
                    break;

                case GMSeasonEventKind.TradeOffer:
                    r = ResolveTrade(league, team, ev, choice, roll, r);
                    break;

                case GMSeasonEventKind.ContractAnxiety:
                    if (choice.Id == "EXTEND" && p != null && team.ReservePlayers.Contains(p))
                    {
                        int demand = GMNegotiationRoom.DemandOf(league, p), years = GMStoveLeagueMarket.PreferredYears(p);
                        long bonus = (long)demand * years * GMStoveLeagueMarket.ExtensionBonusPercent / 100;
                        if (team.Budget < bonus) { r.Success = false; r.Sfx = TeamAudioProfile.SynthFail; p.Loyalty -= 2; r.Message = "계약금 부족 - 시즌 후 협상으로 미뤘습니다(충성도 -2)."; break; }
                        int old = p.Salary;
                        team.Budget -= bonus;
                        p.Salary = demand;
                        p.ContractYears = years + 1; // 진행 중인 올해 + 연장 기간(연도 전환에서 1년 차감)
                        p.Loyalty += 6;
                        GMSalaryChain.Apply(league, team, p, old, demand, GMNegotiationOutcome.AcceptDemand);
                        r.Audio = GMAudioEvent.PositiveResult;
                        r.Message = $"{Name(p)} 조기 연장 - {years}년 · 연봉 {GMDiagnosticFormat.Short(demand)}(이전 {GMDiagnosticFormat.Short(old)}) · 계약금 {GMDiagnosticFormat.Short(bonus)} · 충성도 +6.";
                    }
                    else if (choice.Id == "SHOP" && p != null) { p.Loyalty -= 12; Morale(p, -10); Trust(team, -2); GMFrontOffice.Ensure(league).SeasonTradeBlockId = p.InstanceId; r.Audio = GMAudioEvent.Tension; r.Message = $"{Name(p)}에게 트레이드 검토를 통보 - 충성도 -12 · 만족 -10 · 선수단 신뢰도 -2."; }
                    else { if (p != null) p.Loyalty -= 2; r.Message = $"{Name(p)} 계약은 시즌 후 협상실에서 다룹니다(충성도 -2)."; }
                    break;

                case GMSeasonEventKind.FanDrop:
                    if (choice.Id == "CHEER_THEME") { team.MarketingBudget -= CheerThemeCost; team.FanSupport = Math.Min(100, team.FanSupport + 6); foreach (var x in team.Roster) Morale(x, 2); r.Message = "치어리더 테마 데이 - 마케팅 예산 -5,000만 · 팬 지지율 +6 · 1군 만족 +2."; }
                    else if (choice.Id == "TICKET") { team.Budget -= TicketCost; team.FanSupport = Math.Min(100, team.FanSupport + 4); r.Message = "티켓 할인 프로모션 - 운영 예산 -2억 · 팬 지지율 +4."; }
                    else { team.FanSupport = Math.Max(0, team.FanSupport - 2); r.Message = "대응하지 않았습니다 - 팬 지지율 -2."; }
                    break;

                case GMSeasonEventKind.OwnerCheck:
                {
                    var fo = GMFrontOffice.Ensure(league);
                    if (choice.Id == "PLEDGE") { Owner(league, 3); fo.OwnerPledgeYear = league.SeasonYear; fo.OwnerPledgeRank = Math.Max(1, fo.Owner?.ExpectedRank ?? 5); r.Audio = GMAudioEvent.OwnerApproval; r.Message = $"구단주에게 {fo.OwnerPledgeRank}위 이내를 약속 - 신임도 +3(시즌 종료 시 미달이면 -8)."; }
                    else if (choice.Id == "SAVINGS") { team.Budget += SavingsRefund; int d = (fo.Owner?.Priority ?? 0) == 1 ? 2 : -2; Owner(league, d); r.Message = $"재정 절감 보고 - 운영 예산 +3억 · 구단주 신임도 {(d > 0 ? "+" : "")}{d}."; }
                    else r.Message = "육성 기조를 보고했습니다 - 구단주는 말없이 고개를 끄덕였습니다.";
                    break;
                }

                case GMSeasonEventKind.ManagerDemotion:
                    if (choice.Id == "SUPPORT" && p != null && team.Roster.Contains(p) && GMRosterTiers.SendDown(team, p, out var downMsg))
                    {
                        p.Loyalty = p.Loyalty + SupportLoyalty;
                        Morale(p, SupportMorale);
                        ManagerTrust(team, SupportManagerTrust);
                        GMCareerTimeline.Record(league, p, GMCareerEventKind.BondConflict, team.TeamCode, "감독 요청으로 2군 강등 - 단장이 감독 편에", $"충성도 {SupportLoyalty} · {downMsg}");
                        r.Audio = GMAudioEvent.Tension;
                        r.Message = $"감독을 지지했습니다 - {Name(p)} 퓨처스 강등 · 충성도 {SupportLoyalty} · 만족 {SupportMorale} · 감독 신뢰도 +{SupportManagerTrust}.";
                    }
                    else if (choice.Id == "OVERRULE" && p != null)
                    {
                        ManagerTrust(team, OverruleManagerTrust);
                        p.Loyalty = p.Loyalty + OverruleLoyalty;
                        Teamwork(team, -2, 3);
                        r.Audio = GMAudioEvent.Tension;
                        r.Message = $"단장 권한으로 {Name(p)} 1군 유지 - 감독 신뢰도 {OverruleManagerTrust} · 선수 충성도 +{OverruleLoyalty} · 팀워크 -2(3주). 감독은 굳은 얼굴로 단장실을 나섰습니다.";
                    }
                    else if (p != null)
                    {
                        bool ok = roll < MediateChance(team);
                        r.Success = ok;
                        if (ok) { ManagerTrust(team, MediateManagerTrust); p.Loyalty = p.Loyalty + MediateLoyalty; Morale(p, 3); r.Message = $"면담 중재 성공 - {Name(p)}은(는) 1군에서 반등을 약속했고 감독도 수긍했습니다(감독 신뢰도 +{MediateManagerTrust} · 충성도 +{MediateLoyalty})."; }
                        else { ManagerTrust(team, MediateFailManagerTrust); p.Loyalty = p.Loyalty + MediateFailLoyalty; r.Sfx = TeamAudioProfile.SynthFail; r.Audio = GMAudioEvent.Tension; r.Message = $"면담 중재 실패 - 양측 모두 불만을 품은 채 끝났습니다(감독 신뢰도 {MediateFailManagerTrust} · 충성도 {MediateFailLoyalty}). {Name(p)}은(는) 1군에 남습니다."; }
                    }
                    else { r.Success = false; r.Message = "대상 선수가 없어 사건이 종료되었습니다."; }
                    break;

                case GMSeasonEventKind.RivalSigning:
                {
                    var fo = GMFrontOffice.Ensure(league);
                    if (choice.Id == "COUNTER")
                    {
                        team.FanSupport = GMTeamFan.Clamp(team.FanSupport + CounterPledgeFan);
                        fo.CounterPledgeYear = league.SeasonYear;
                        fo.CounterPledgeDay = league.GamesPlayed;
                        fo.CounterPledgeRival = ev.PartnerTeam ?? "";
                        fo.CounterPledgeTarget = Name(p);
                        r.Audio = GMAudioEvent.OwnerApproval;
                        r.Message = $"언론에 맞불 영입을 예고했습니다 - 팬 지지율 +{CounterPledgeFan}. 7월 31일까지 OVR {CounterTargetOvr}+ 선수를 데려오지 못하면 구단주 신임도 {CounterFailTrust} · 팬 지지율 {CounterFailFan}.";
                    }
                    else if (choice.Id == "CHEER")
                    {
                        team.MarketingBudget -= CheerBigEventCost;
                        team.FanSupport = GMTeamFan.Clamp(team.FanSupport + CheerBigEventFan);
                        foreach (var x in team.Roster) Morale(x, 2);
                        r.Message = $"대규모 치어리더 이벤트 - 마케팅 예산 -{GMDiagnosticFormat.Short(CheerBigEventCost)} · 팬 지지율 +{CheerBigEventFan} · 1군 만족 +2. 팬들의 시선이 응원단 단상으로 쏠렸습니다.";
                    }
                    else
                    {
                        team.FanSupport = GMTeamFan.Clamp(team.FanSupport + YouthFan);
                        foreach (var x in team.Futures) x.Loyalty = x.Loyalty + YouthLoyalty;
                        Trust(team, YouthTrust);
                        r.Message = $"유망주 육성을 천명했습니다 - 팬 지지율 {YouthFan} · 퓨처스 충성도 +{YouthLoyalty} · 선수단 신뢰도 +{YouthTrust}.";
                    }
                    break;
                }

                default:
                    if (choice.Id == "INTERVENE") { Teamwork(team, -2, 3); Owner(league, 2); Trust(team, -4); r.Audio = GMAudioEvent.Tension; r.Message = "단장 직접 개입 - 팀워크 -2(3주) · 구단주 신임도 +2 · 선수단 신뢰도 -4."; }
                    else if (choice.Id == "COACHES") { team.Budget -= CoachCost; Teamwork(team, 2, 3); Trust(team, 2); r.Message = "코치진 교체 - 운영 예산 -1억 · 팀워크 +2(3주) · 선수단 신뢰도 +2."; }
                    else { Trust(team, 1); r.Message = "감독을 지지했습니다 - 선수단 신뢰도 +1."; }
                    break;
            }
            ev.Resolved = true;
            ev.ResultText = r.Message;
            var log = GMFrontOffice.Ensure(league).SeasonDecisions;
            log.Add(new GMSeasonDecisionLog { Year = ev.Year, Week = ev.Week, Day = ev.Day, Priority = (int)ev.Priority, Kind = ev.Kind.ToString(), Title = ev.Title, Choice = choice.Label, Result = r.Message });
            while (log.Count > 80) log.RemoveAt(0);
            league.AddNews(new GMNewsItem
            {
                GameIndex = Math.Max(0, league.GamesPlayed - 1), DateLabel = GMLiveSeasonSimulator.DateLabel(Math.Max(0, league.GamesPlayed - 1), league.SeasonYear),
                Kind = GMNewsKind.Decision, IsUserTeam = true, IsMajor = ev.Priority <= GMEventPriority.P1, Title = $"[단장 결정] {choice.Label}", Body = r.Message,
            });
            return r;
        }

        private static GMSeasonEventResult ResolveTrade(GMLeagueState league, GMTeamState team, GMSeasonEvent ev, GMSeasonEventChoice choice, double roll, GMSeasonEventResult r)
        {
            var mine = ev.Player;
            var theirs = ev.Partner;
            var partner = ev.PartnerTeam != null && league.Teams.TryGetValue(ev.PartnerTeam, out var t) ? t : null;
            if (choice.Id == "DECLINE" || partner == null || mine == null || theirs == null) { r.Message = "제안을 정중히 거절했습니다."; return r; }
            if (choice.Id == "RENEGOTIATE" && roll >= RenegotiateChance)
            {
                r.Success = false;
                r.Sfx = TeamAudioProfile.SynthFail;
                r.Message = $"{partner.DisplayName} 단장: \"현금까지는 곤란합니다.\" - 제안이 철회되었습니다.";
                return r;
            }
            var done = GMStoveLeagueMarket.ExecuteTrade(league, team, new[] { mine }, partner, new[] { theirs });
            if (!done.Success) { r.Success = false; r.Sfx = TeamAudioProfile.SynthFail; r.Message = "트레이드 불발 - " + done.Message; return r; }
            if (choice.Id == "RENEGOTIATE")
            {
                partner.Budget -= RenegotiateCash;
                team.Budget += RenegotiateCash;
            }
            var fo = GMFrontOffice.Ensure(league);
            if (fo.SeasonTradeBlockId == mine.InstanceId) fo.SeasonTradeBlockId = "";
            r.Audio = GMAudioEvent.PositiveResult;
            r.Message = done.Message + (choice.Id == "RENEGOTIATE" ? $" · 현금 {GMDiagnosticFormat.Short(RenegotiateCash)} 수령" : "");
            return r;
        }

        // ================================================================== 시즌 종료 평가

        /// <summary>정규시즌 종료 - 구단주 중간 점검 약속 판정(미달 = 신임도 -8) · 사건 팀워크 보정 소멸. 판정 메시지(없으면 빈 문자열).</summary>
        public static string EvaluateSeason(GMLeagueState league, IReadOnlyList<string> regularSeasonRanks)
        {
            var fo = GMFrontOffice.Ensure(league);
            foreach (var t in league.Teams.Values) { t.SeasonEventTeamwork = 0; t.SeasonEventTeamworkWeeks = 0; }
            fo.SeasonTradeBlockId = "";
            string counter = EvaluateCounterPledge(league); // [TASK-GM-19] 맞불 영입 공언 판정
            if (fo.OwnerPledgeYear != league.SeasonYear || league.UserTeam == null) return counter;
            fo.OwnerPledgeYear = 0;
            int rank = regularSeasonRanks.ToList().IndexOf(league.UserTeam.TeamCode) + 1;
            string sep = counter == "" ? "" : " · ";
            if (rank > 0 && rank <= fo.OwnerPledgeRank) return counter + sep + $"구단주 중간 점검 약속 이행({rank}위 ≤ {fo.OwnerPledgeRank}위)";
            Owner(league, -8);
            return counter + sep + $"구단주 중간 점검 약속 미달({rank}위 > {fo.OwnerPledgeRank}위) - 신임도 -8";
        }

        /// <summary>[TASK-GM-19] 맞불 영입 공언 판정(정규시즌 종료) - 미달 = 구단주 신임도 -8 · 팬 지지율 -4. 판정 메시지(공언 없으면 빈 문자열).</summary>
        public static string EvaluateCounterPledge(GMLeagueState league)
        {
            var fo = GMFrontOffice.Ensure(league);
            var team = league.UserTeam;
            if (team == null || fo.CounterPledgeYear != league.SeasonYear) return "";
            bool met = CounterPledgeMet(league);
            fo.CounterPledgeYear = 0;
            if (met) return "맞불 영입 공언 이행";
            Owner(league, CounterFailTrust);
            team.FanSupport = GMTeamFan.Clamp(team.FanSupport + CounterFailFan);
            league.AddNews(new GMNewsItem
            {
                GameIndex = Math.Max(0, league.GamesPlayed - 1), DateLabel = $"{league.SeasonYear} 시즌 종료", Kind = GMNewsKind.Decision, IsUserTeam = true, IsMajor = true,
                Title = "[단장 공언] 맞불 영입 약속, 끝내 지키지 못했다", Body = $"{NameAliasTable.DisplayTeamName(fo.CounterPledgeRival)}의 {fo.CounterPledgeTarget} 영입에 맞서 예고한 보강이 무산됐습니다. 구단주 신임도 {CounterFailTrust} · 팬 지지율 {CounterFailFan}.",
            });
            return $"맞불 영입 공언 미달 - 구단주 신임도 {CounterFailTrust} · 팬 지지율 {CounterFailFan}";
        }

        /// <summary>올해 단장 개입 결정 수.</summary>
        public static int DecisionsThisYear(GMLeagueState league) => GMFrontOffice.Ensure(league).SeasonDecisions.Count(d => d.Year == league.SeasonYear);
    }
}
