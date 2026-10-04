using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;

namespace KBOManager.Managers
{
    /// <summary>[TASK-KBO-193] 리그 기록실 순위표 1행.</summary>
    public sealed class RecordRow
    {
        public int Rank;
        public Player Player;
        public string Name;
        public Team Team;
        public string Value;
        public bool IsUser;
    }

    public enum BatterRecord { Average, HomeRuns, RunsBattedIn, Hits }
    public enum PitcherRecord { Wins, Era, Strikeouts, Innings }

    /// <summary>
    /// [TASK-KBO-193] 로비 [리그 기록실] 규칙(순수 - LeagueRecordsView · SeasonRollover · 단위 테스트 공용).
    ///   타자 TOP 10: 타율(규정타석 = 팀 경기 수 × 3.1) · 홈런 · 타점 · 안타 / 투수 TOP 10: 다승 · 평균자책점(규정이닝 = 팀 경기 수 × 1.0) · 탈삼진 · 이닝.
    ///   시즌 MVP: 타자 = 안타 + 홈런 × 3 + 타점, 투수 = 승 × 8 + 탈삼진 × 0.5 + 이닝 - 자책점 × 0.5 중 최고 점수.
    ///   명예의 전당: 시즌 완주(다음 시즌 시작) 때마다 시즌 번호 · 리그 단계 · 최종 순위/전적 · 한국시리즈 우승 · MVP · 타이틀 6부문을 1건 누적.
    /// 동률은 보조 기록 → 이름 순으로 결정적으로 가른다. 기록이 0인 선수는 누적 부문(홈런 · 타점 · 안타 · 다승 · 탈삼진 · 이닝)에서 뺀다.
    /// </summary>
    public static class LeagueRecordsRules
    {
        public const int TopCount = 10;

        public static readonly string[] BatterLabels = { "타율", "홈런", "타점", "안타" };
        public static readonly string[] PitcherLabels = { "다승", "평균자책점", "탈삼진", "이닝" };

        private static string Name(Player p) => p?.Template != null ? p.Template.PlayerName : "";

        public static List<RecordRow> TopBatters(IReadOnlyDictionary<Player, BatterSeasonStats> stats, BatterRecord record, int teamGamesPlayed,
            Func<Player, bool> isUser, int top = TopCount)
        {
            var list = (stats ?? new Dictionary<Player, BatterSeasonStats>()).Where(kv => kv.Key?.Template != null && kv.Value != null).ToList();
            float minPa = teamGamesPlayed * SeasonStatManager.QualifyingPlateAppearancesPerGame;
            IOrderedEnumerable<KeyValuePair<Player, BatterSeasonStats>> ordered;
            Func<BatterSeasonStats, string> value;
            switch (record)
            {
                case BatterRecord.Average:
                    ordered = list.Where(kv => kv.Value.AtBats > 0 && kv.Value.PlateAppearances >= minPa)
                        .OrderByDescending(kv => kv.Value.BattingAverage).ThenByDescending(kv => kv.Value.Hits);
                    value = s => s.BattingAverage.ToString(".000");
                    break;
                case BatterRecord.HomeRuns:
                    ordered = list.Where(kv => kv.Value.HomeRuns > 0).OrderByDescending(kv => kv.Value.HomeRuns).ThenByDescending(kv => kv.Value.RunsBattedIn);
                    value = s => $"{s.HomeRuns}홈런";
                    break;
                case BatterRecord.RunsBattedIn:
                    ordered = list.Where(kv => kv.Value.RunsBattedIn > 0).OrderByDescending(kv => kv.Value.RunsBattedIn).ThenByDescending(kv => kv.Value.HomeRuns);
                    value = s => $"{s.RunsBattedIn}타점";
                    break;
                default:
                    ordered = list.Where(kv => kv.Value.Hits > 0).OrderByDescending(kv => kv.Value.Hits).ThenByDescending(kv => kv.Value.BattingAverage);
                    value = s => $"{s.Hits}안타";
                    break;
            }
            return Rows(ordered.ThenBy(kv => Name(kv.Key), StringComparer.Ordinal)
                .Select(kv => (kv.Key, value(kv.Value))), isUser, top);
        }

        public static List<RecordRow> TopPitchers(IReadOnlyDictionary<Player, PitcherSeasonStats> stats, PitcherRecord record, int teamGamesPlayed,
            Func<Player, bool> isUser, int top = TopCount)
        {
            var list = (stats ?? new Dictionary<Player, PitcherSeasonStats>()).Where(kv => kv.Key?.Template != null && kv.Value != null).ToList();
            float minIp = teamGamesPlayed * SeasonStatManager.QualifyingInningsPerGame;
            IOrderedEnumerable<KeyValuePair<Player, PitcherSeasonStats>> ordered;
            Func<PitcherSeasonStats, string> value;
            switch (record)
            {
                case PitcherRecord.Wins:
                    ordered = list.Where(kv => kv.Value.Wins > 0).OrderByDescending(kv => kv.Value.Wins).ThenBy(kv => kv.Value.Losses);
                    value = s => $"{s.Wins}승 {s.Losses}패";
                    break;
                case PitcherRecord.Era:
                    ordered = list.Where(kv => kv.Value.OutsRecorded > 0 && kv.Value.InningsPitched >= minIp)
                        .OrderBy(kv => kv.Value.EarnedRunAverage).ThenByDescending(kv => kv.Value.OutsRecorded);
                    value = s => s.EarnedRunAverage.ToString("0.00");
                    break;
                case PitcherRecord.Strikeouts:
                    ordered = list.Where(kv => kv.Value.Strikeouts > 0).OrderByDescending(kv => kv.Value.Strikeouts).ThenByDescending(kv => kv.Value.OutsRecorded);
                    value = s => $"{s.Strikeouts}K";
                    break;
                default:
                    ordered = list.Where(kv => kv.Value.OutsRecorded > 0).OrderByDescending(kv => kv.Value.OutsRecorded).ThenBy(kv => kv.Value.EarnedRuns);
                    value = s => $"{SeasonAwardRules.InningsLabel(s.OutsRecorded)}이닝";
                    break;
            }
            return Rows(ordered.ThenBy(kv => Name(kv.Key), StringComparer.Ordinal).Select(kv => (kv.Key, value(kv.Value))), isUser, top);
        }

        private static List<RecordRow> Rows(IEnumerable<(Player Player, string Value)> ordered, Func<Player, bool> isUser, int top) =>
            ordered.Take(top).Select((x, i) => new RecordRow
            {
                Rank = i + 1,
                Player = x.Player,
                Name = CardDisplay.NameWithYear(x.Player.Template),
                Team = x.Player.Template.Team,
                Value = x.Value,
                IsUser = isUser != null && isUser(x.Player),
            }).ToList();

        // ================================================================== 시즌 MVP

        public static float BatterMvpScore(BatterSeasonStats s) => s == null ? 0f : s.Hits + s.HomeRuns * 3f + s.RunsBattedIn;
        public static float PitcherMvpScore(PitcherSeasonStats s) => s == null ? 0f : s.Wins * 8f + s.Strikeouts * 0.5f + s.InningsPitched - s.EarnedRuns * 0.5f;

        /// <summary>시즌 MVP 문구 "구자욱 (Samsung) · 타율 .352 41홈런 120타점" - 기록이 없으면 null.</summary>
        public static string SeasonMvp(IReadOnlyDictionary<Player, BatterSeasonStats> batters, IReadOnlyDictionary<Player, PitcherSeasonStats> pitchers)
        {
            var best = (batters ?? new Dictionary<Player, BatterSeasonStats>()).Where(kv => kv.Key?.Template != null && kv.Value != null)
                .Select(kv => (kv.Key, Score: BatterMvpScore(kv.Value), Label: $"타율 {kv.Value.BattingAverage:.000} {kv.Value.HomeRuns}홈런 {kv.Value.RunsBattedIn}타점"))
                .Concat((pitchers ?? new Dictionary<Player, PitcherSeasonStats>()).Where(kv => kv.Key?.Template != null && kv.Value != null)
                    .Select(kv => (kv.Key, Score: PitcherMvpScore(kv.Value), Label: $"{kv.Value.Wins}승 {kv.Value.Losses}패 ERA {kv.Value.EarnedRunAverage:0.00} {kv.Value.Strikeouts}K")))
                .Where(x => x.Score > 0f)
                .OrderByDescending(x => x.Score).ThenBy(x => Name(x.Key), StringComparer.Ordinal)
                .FirstOrDefault();
            return best.Key?.Template == null ? null : $"{best.Key.Template.PlayerName} ({best.Key.Template.Team}) · {best.Label}";
        }

        // ================================================================== 명예의 전당

        /// <summary>시즌 완주 기록 1건을 만든다(값 스냅샷 - 원본 기록이 초기화돼도 그대로 남는다).</summary>
        public static HallOfFameEntry BuildEntry(int seasonNumber, int seasonYear, LeagueTier tier, int finalRank, int regularRank,
            int wins, int draws, int losses, Team champion, Team userTeam, string mvp, IEnumerable<SeasonTitle> titles)
        {
            return new HallOfFameEntry
            {
                SeasonNumber = seasonNumber,
                SeasonYear = seasonYear,
                TierName = LeagueTierTable.DisplayName(tier),
                FinalRank = finalRank,
                RegularSeasonRank = regularRank,
                Wins = wins,
                Draws = draws,
                Losses = losses,
                ChampionTeam = champion,
                KoreanSeriesWon = userTeam != Team.None && champion == userTeam,
                Mvp = string.IsNullOrEmpty(mvp) ? "-" : mvp,
                Titles = (titles ?? Enumerable.Empty<SeasonTitle>()).Where(t => t != null)
                    .Select(t => $"{t.Category} {t.PlayerName} {t.ValueLabel}{(t.IsUserPlayer ? " ★" : "")}").ToList(),
            };
        }

        /// <summary>기록실 명예의 전당 1건 표시 문구(2~3줄).</summary>
        public static string EntryText(HallOfFameEntry e)
        {
            if (e == null) return "";
            string season = e.SeasonNumber > 0 ? $"시즌 {e.SeasonNumber}" : "시즌";
            string tier = string.IsNullOrEmpty(e.TierName) ? "" : $" · {e.TierName}";
            string rank = e.FinalRank > 0 ? $"최종 {e.FinalRank}위 (정규 {(e.RegularSeasonRank > 0 ? e.RegularSeasonRank + "위" : "-")}) · {e.Wins}승 {e.Draws}무 {e.Losses}패" : "기록 없음";
            string ks = e.KoreanSeriesWon ? "<color=#FFD54A>🏆 한국시리즈 우승</color>"
                : $"KS 우승 {(e.ChampionTeam != Team.None ? e.ChampionTeam.ToString() : "-")}";
            string titles = e.Titles != null && e.Titles.Count > 0 ? string.Join(" · ", e.Titles) : $"타격 {e.BattingAverageLeader} · 홈런 {e.HomeRunLeader} · 다승 {e.WinsLeader} · ERA {e.EraLeader}";
            return $"<color=#7CD3FF>{season} ({e.SeasonYear}){tier}</color>  {rank}  {ks}\nMVP {(string.IsNullOrEmpty(e.Mvp) ? "-" : e.Mvp)}\n<color=#C9D3EA>{titles}</color>";
        }
    }
}
