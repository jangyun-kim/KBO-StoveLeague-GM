using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-15] 언론 브리핑실 - 대외 브리핑 3지선다(전면 리빌딩 · 우승 도전 선언 · 노코멘트).
    ///   주제 ① 스토브리그 출사표(개막 전, 해마다 1회) ② 시즌 중간 브리핑(40경기 이후 정규시즌 중, 해마다 1회).
    ///   즉시 효과: 팬 지지율(FanSupport) 변동 · 유망주(25세 이하) / 베테랑(32세 이상) 충성도(Loyalty) 버프 · 디버프 · 구단주 기대 순위 갱신.
    ///   기록된 발언: 순위 약속(우승 도전 = 3위 · 가을야구 = 5위 이내)은 정규시즌 종료(GMFrontOffice.OnSeasonCompleted)에 판정 -
    ///   지키면 구단주 신임도 +, 못 지키면 대폭 -(무리한 우승 선언 -15). 리빌딩 · 노코멘트는 순위 약속이 없다.
    /// </summary>
    public static class GMPressBriefing
    {
        public const int MidSeasonGames = 40, YoungAge = 25, VeteranAge = 32;

        public static string TopicLabel(GMPressTopic t) => t == GMPressTopic.StoveVision ? "스토브리그 출사표" : "시즌 중간 브리핑";
        public static string ChoiceLabel(GMPressChoice c) => c == GMPressChoice.Rebuild ? "전면 리빌딩" : c == GMPressChoice.WinNow ? "우승 도전 선언" : "노코멘트";

        /// <summary>지금 열 수 있는 주제(없으면 null).</summary>
        public static GMPressTopic? CurrentTopic(GMLeagueState league)
        {
            if (league?.UserTeam == null) return null;
            if (league.GamesPlayed == 0) return GMPressTopic.StoveVision;
            if (league.GamesPlayed >= MidSeasonGames && league.GamesPlayed < GMSeasonReview.SeasonGames) return GMPressTopic.MidSeason;
            return null;
        }

        public static GMPressStatement StatementOf(GMLeagueState league, GMPressTopic topic) =>
            league == null ? null : GMFrontOffice.Ensure(league).PressStatements.FirstOrDefault(s => s.Year == league.SeasonYear && s.Topic == topic);

        public static bool CanBrief(GMLeagueState league, out string reason)
        {
            reason = "";
            var topic = CurrentTopic(league);
            if (topic == null) { reason = league != null && league.GamesPlayed >= GMSeasonReview.SeasonGames ? "정규시즌이 끝났습니다 - 다음 스토브리그에 브리핑하십시오." : $"시즌 중간 브리핑은 {MidSeasonGames}경기 이후에 열립니다."; return false; }
            if (StatementOf(league, topic.Value) != null) { reason = $"올해 {TopicLabel(topic.Value)}는 이미 발표했습니다."; return false; }
            return true;
        }

        /// <summary>주제별 3지선다.</summary>
        public static List<GMPressOption> Options(GMPressTopic topic)
        {
            bool stove = topic == GMPressTopic.StoveVision;
            var list = new List<GMPressOption>
            {
                new GMPressOption
                {
                    Choice = GMPressChoice.Rebuild, Label = "1. \"이번 시즌은 전면 리빌딩입니다\"",
                    Quote = stove ? "이번 시즌은 전면 리빌딩입니다. 젊은 선수들에게 확실히 기회를 주겠습니다." : "순위보다 미래를 보겠습니다. 남은 경기는 젊은 선수들에게 맡기겠습니다.",
                    FanDelta = stove ? -4 : -3, YoungLoyalty = stove ? 5 : 3, VeteranLoyalty = stove ? -4 : -3,
                },
                new GMPressOption
                {
                    Choice = GMPressChoice.WinNow, Label = stove ? "2. \"우승을 향해 FA에 투자하겠습니다\"" : "2. \"반드시 가을야구에 가겠습니다\"",
                    Quote = stove ? "우승을 향해 FA에 과감히 투자하겠습니다. 올해 목표는 정상입니다." : "지금 순위는 의미 없습니다. 반드시 가을야구에 가겠습니다.",
                    FanDelta = stove ? 6 : 4, YoungLoyalty = -2, VeteranLoyalty = 3,
                    TargetRank = stove ? 3 : 5, TrustOnSuccess = stove ? 6 : 4, TrustOnFail = stove ? -15 : -10,
                },
                new GMPressOption
                {
                    Choice = GMPressChoice.NoComment, Label = "3. \"노코멘트\"",
                    Quote = "노코멘트. 결과로 말씀드리겠습니다.", FanDelta = -1,
                },
            };
            foreach (var o in list) o.Effect = EffectText(o);
            return list;
        }

        public static string EffectText(GMPressOption o)
        {
            var parts = new List<string> { $"팬 지지율 {Signed(o.FanDelta)}" };
            if (o.YoungLoyalty != 0 || o.VeteranLoyalty != 0) parts.Add($"충성도 유망주 {Signed(o.YoungLoyalty)} · 베테랑 {Signed(o.VeteranLoyalty)}");
            parts.Add(o.TargetRank > 0 ? $"기록된 발언: {o.TargetRank}위 이내 - 달성 신임도 {Signed(o.TrustOnSuccess)} · 실패 {Signed(o.TrustOnFail)}" : "순위 약속 없음(신임도 판정 없음)");
            return string.Join(" · ", parts);
        }

        private static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

        /// <summary>현재 전망 순위 - 시즌 중이면 순위표, 개막 전이면 1군 평균 OVR 순위.</summary>
        public static int ProjectedRank(GMLeagueState league)
        {
            var user = league?.UserTeam;
            if (user == null) return 0;
            if (league.GamesPlayed > 0)
            {
                var order = league.Records.Values.OrderByDescending(r => r.W + r.L == 0 ? 0 : (double)r.W / (r.W + r.L)).ThenBy(r => r.TeamCode, StringComparer.Ordinal).Select(r => r.TeamCode).ToList();
                int i = order.IndexOf(user.TeamCode);
                if (i >= 0) return i + 1;
            }
            double Strength(GMTeamState t) => t.Roster.Count == 0 ? 0 : t.Roster.OrderByDescending(p => p.BaseOverall).Take(GMRosterTiers.GameDayActive).Average(p => p.BaseOverall);
            var ranked = league.Teams.Values.OrderByDescending(Strength).ThenBy(t => t.TeamCode, StringComparer.Ordinal).ToList();
            return ranked.IndexOf(user) + 1;
        }

        /// <summary>선택지의 구단주 신임도 기대치(현재 전망 순위로 판정했을 때).</summary>
        public static int ExpectedTrustDelta(GMLeagueState league, GMPressOption o)
        {
            if (o == null || o.TargetRank <= 0) return 0;
            int rank = ProjectedRank(league);
            return rank > 0 && rank <= o.TargetRank ? o.TrustOnSuccess : o.TrustOnFail;
        }

        /// <summary>브리핑 실행 - 팬 지지율 · 충성도 · 구단주 기대 순위를 바꾸고 발언을 기록한다.</summary>
        public static GMPressResult Brief(GMLeagueState league, GMPressChoice choice)
        {
            var result = new GMPressResult();
            if (!CanBrief(league, out var reason)) { result.Message = reason; return result; }
            var topic = CurrentTopic(league).Value;
            var o = Options(topic).First(x => x.Choice == choice);
            var team = league.UserTeam;
            var fo = GMFrontOffice.Ensure(league);
            result.FanBefore = team.FanSupport;
            team.FanSupport = GMTeamFan.Clamp(team.FanSupport + o.FanDelta);
            result.FanAfter = team.FanSupport;
            int young = 0, vets = 0;
            foreach (var p in team.ReservePlayers.Where(p => p?.Template != null))
            {
                if (p.Age <= YoungAge && o.YoungLoyalty != 0) { p.Loyalty = p.Loyalty + o.YoungLoyalty; young++; }
                else if (p.Age >= VeteranAge && o.VeteranLoyalty != 0) { p.Loyalty = p.Loyalty + o.VeteranLoyalty; vets++; }
            }
            if (o.TargetRank > 0) fo.Owner.ExpectedRank = Math.Min(fo.Owner.ExpectedRank, o.TargetRank);
            else if (choice == GMPressChoice.Rebuild) fo.Owner.ExpectedRank = Math.Min(10, fo.Owner.ExpectedRank + 2);
            var st = new GMPressStatement
            {
                Year = league.SeasonYear, Topic = topic, Choice = choice, Quote = o.Quote, FanDelta = o.FanDelta,
                TargetRank = o.TargetRank, TrustOnSuccess = o.TrustOnSuccess, TrustOnFail = o.TrustOnFail,
            };
            fo.PressStatements.Add(st);
            result.Applied = true;
            result.Statement = st;
            result.Message = $"[{TopicLabel(topic)}] {ChoiceLabel(choice)} - 팬 지지율 {result.FanBefore} → {result.FanAfter}" +
                             (young + vets > 0 ? $" · 충성도 유망주 {young}명 {Signed(o.YoungLoyalty)} · 베테랑 {vets}명 {Signed(o.VeteranLoyalty)}" : "") +
                             (o.TargetRank > 0 ? $" · 기록된 발언({o.TargetRank}위 이내) - 현재 전망 {ProjectedRank(league)}위 · 신임도 기대치 {Signed(ExpectedTrustDelta(league, o))}" : "") +
                             $" · 구단주 기대 순위 {fo.Owner.ExpectedRank}위";
            league.AddNews(new GMNewsItem
            {
                GameIndex = league.GamesPlayed, DateLabel = league.GamesPlayed == 0 ? $"{league.SeasonYear} 스토브리그" : $"{league.SeasonYear} 시즌", Kind = GMNewsKind.Season,
                IsUserTeam = true, IsMajor = choice == GMPressChoice.WinNow,
                Title = $"[브리핑] 단장 \"{ShortQuote(o.Quote)}\"", Body = result.Message,
            });
            return result;
        }

        private static string ShortQuote(string q) => q.Split('.')[0];

        /// <summary>정규시즌 종료 - 올해 순위 약속 발언을 판정하고 구단주 신임도를 반영한다(판정 결과 목록).</summary>
        public static List<GMPressStatement> EvaluateSeason(GMLeagueState league, IReadOnlyList<string> regularSeasonRanks)
        {
            var done = new List<GMPressStatement>();
            var team = league?.UserTeam;
            if (team == null || regularSeasonRanks == null) return done;
            var fo = GMFrontOffice.Ensure(league);
            int rank = regularSeasonRanks.ToList().IndexOf(team.TeamCode) + 1;
            foreach (var st in fo.PressStatements.Where(s => s.Year == league.SeasonYear && !s.Evaluated))
            {
                st.Evaluated = true;
                if (st.TargetRank <= 0 || rank <= 0) { st.Kept = true; st.ResultNote = "순위 약속 없음"; continue; }
                st.Kept = rank <= st.TargetRank;
                int delta = st.Kept ? st.TrustOnSuccess : st.TrustOnFail;
                int before = fo.OwnerTrust;
                fo.OwnerTrust = Math.Max(fo.Manager != null && fo.Manager.NoFiring ? GMFrontOffice.NoFiringTrustFloor : 0, Math.Min(GMFrontOffice.MaxTrust, fo.OwnerTrust + delta));
                st.TrustApplied = fo.OwnerTrust - before;
                st.ResultNote = $"{rank}위 / 약속 {st.TargetRank}위 이내 - {(st.Kept ? "달성" : "실패")} · 구단주 신임도 {Signed(st.TrustApplied)}";
                done.Add(st);
                league.AddNews(new GMNewsItem
                {
                    GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 시즌 종료", Kind = GMNewsKind.Season, IsUserTeam = true, IsMajor = !st.Kept,
                    Title = st.Kept ? $"[브리핑] 단장의 {TopicLabel(st.Topic)}, 약속 지켰다" : $"[브리핑] \"{ShortQuote(st.Quote)}\" - 공언 실패, 구단주 신임 급락",
                    Body = st.ResultNote,
                });
            }
            return done;
        }
    }
}
