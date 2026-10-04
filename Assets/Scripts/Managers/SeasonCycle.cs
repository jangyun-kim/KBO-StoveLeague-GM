using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;

namespace KBOManager.Managers
{
    /// <summary>[TASK-KBO-190] 개인 타이틀 1건(부문 · 수상자 · 기록 · 우리 구단 소속 여부).</summary>
    public sealed class SeasonTitle
    {
        public string Category;   // "타격왕" 등
        public Player Player;
        public string PlayerName;
        public string TeamLabel;
        public string ValueLabel; // ".352" / "41홈런" ...
        public bool IsUserPlayer;

        public string Line => $"{Category}  {PlayerName}{(string.IsNullOrEmpty(TeamLabel) ? "" : $" ({TeamLabel})")}  {ValueLabel}" + (IsUserPlayer ? "  ★ 우리 구단" : "");
    }

    /// <summary>
    /// [TASK-KBO-190] 정규시즌 개인 타이틀 시상식(순수 규칙 - SeasonRewardManager · 단위 테스트 공용). 실제 누적된 시즌 기록(SeasonStatManager)으로 6개 부문 1위를 뽑는다.
    ///   타자: 타격왕(타율 - 규정타석 = 팀 경기 수 × 3.1), 홈런왕, 타점왕
    ///   투수: 다승왕, 평균자책점 1위(규정이닝 = 팀 경기 수 × 1.0), 탈삼진 1위
    /// 동률은 보조 기록(안타 · 타점 · 홈런 · 이닝 · 승) → 이름 순으로 결정적으로 가른다. 기록이 0인 부문(홈런 0개 등)은 수상자 없음.
    /// 우리 구단 선수 수상 1건마다 트로피 +1 · 성장 코인 +100 · 포인트 +30,000.
    /// </summary>
    public static class SeasonAwardRules
    {
        public const int TitleTrophy = 1;
        public const int TitleGrowthCoin = 100;
        public const int TitlePoints = 30000;

        public static readonly string[] Categories = { "타격왕", "홈런왕", "타점왕", "다승왕", "평균자책점", "탈삼진" };

        public static List<SeasonTitle> Compute(IReadOnlyDictionary<Player, BatterSeasonStats> batters, IReadOnlyDictionary<Player, PitcherSeasonStats> pitchers,
            int teamGamesPlayed, Func<Player, bool> isUserPlayer)
        {
            var titles = new List<SeasonTitle>();
            var b = (batters ?? new Dictionary<Player, BatterSeasonStats>()).Where(kv => kv.Key?.Template != null && kv.Value != null).ToList();
            var p = (pitchers ?? new Dictionary<Player, PitcherSeasonStats>()).Where(kv => kv.Key?.Template != null && kv.Value != null).ToList();
            float minPa = teamGamesPlayed * SeasonStatManager.QualifyingPlateAppearancesPerGame;
            float minIp = teamGamesPlayed * SeasonStatManager.QualifyingInningsPerGame;

            Add(titles, "타격왕", b.Where(kv => kv.Value.PlateAppearances >= minPa && kv.Value.AtBats > 0)
                    .OrderByDescending(kv => kv.Value.BattingAverage).ThenByDescending(kv => kv.Value.Hits).ThenBy(kv => Name(kv.Key), StringComparer.Ordinal)
                    .Select(kv => (kv.Key, $"타율 {kv.Value.BattingAverage:.000} ({kv.Value.Hits}안타 / {kv.Value.AtBats}타수)")).FirstOrDefault(), isUserPlayer);
            Add(titles, "홈런왕", b.Where(kv => kv.Value.HomeRuns > 0)
                    .OrderByDescending(kv => kv.Value.HomeRuns).ThenByDescending(kv => kv.Value.RunsBattedIn).ThenBy(kv => Name(kv.Key), StringComparer.Ordinal)
                    .Select(kv => (kv.Key, $"{kv.Value.HomeRuns}홈런")).FirstOrDefault(), isUserPlayer);
            Add(titles, "타점왕", b.Where(kv => kv.Value.RunsBattedIn > 0)
                    .OrderByDescending(kv => kv.Value.RunsBattedIn).ThenByDescending(kv => kv.Value.HomeRuns).ThenBy(kv => Name(kv.Key), StringComparer.Ordinal)
                    .Select(kv => (kv.Key, $"{kv.Value.RunsBattedIn}타점")).FirstOrDefault(), isUserPlayer);
            Add(titles, "다승왕", p.Where(kv => kv.Value.Wins > 0)
                    .OrderByDescending(kv => kv.Value.Wins).ThenBy(kv => kv.Value.Losses).ThenBy(kv => Name(kv.Key), StringComparer.Ordinal)
                    .Select(kv => (kv.Key, $"{kv.Value.Wins}승 {kv.Value.Losses}패")).FirstOrDefault(), isUserPlayer);
            Add(titles, "평균자책점", p.Where(kv => kv.Value.InningsPitched >= minIp && kv.Value.OutsRecorded > 0)
                    .OrderBy(kv => kv.Value.EarnedRunAverage).ThenByDescending(kv => kv.Value.OutsRecorded).ThenBy(kv => Name(kv.Key), StringComparer.Ordinal)
                    .Select(kv => (kv.Key, $"ERA {kv.Value.EarnedRunAverage:F2} ({InningsLabel(kv.Value.OutsRecorded)}이닝)")).FirstOrDefault(), isUserPlayer);
            Add(titles, "탈삼진", p.Where(kv => kv.Value.Strikeouts > 0)
                    .OrderByDescending(kv => kv.Value.Strikeouts).ThenByDescending(kv => kv.Value.OutsRecorded).ThenBy(kv => Name(kv.Key), StringComparer.Ordinal)
                    .Select(kv => (kv.Key, $"{kv.Value.Strikeouts}탈삼진")).FirstOrDefault(), isUserPlayer);
            return titles;
        }

        /// <summary>우리 구단 수상 보너스(수상 1건당 트로피 +1 · 성장 코인 +100 · 포인트 +30,000).</summary>
        public static LeagueMaterialReward UserBonus(IEnumerable<SeasonTitle> titles)
        {
            int n = (titles ?? Enumerable.Empty<SeasonTitle>()).Count(t => t != null && t.IsUserPlayer);
            return new LeagueMaterialReward { Trophy = TitleTrophy * n, GrowthCoin = TitleGrowthCoin * n, Points = TitlePoints * n };
        }

        public static string InningsLabel(int outs) => outs % 3 == 0 ? $"{outs / 3}" : $"{outs / 3} {outs % 3}/3";

        private static string Name(Player p) => p?.Template != null ? p.Template.PlayerName : "";

        private static void Add(List<SeasonTitle> titles, string category, (Player Player, string Value) winner, Func<Player, bool> isUserPlayer)
        {
            if (winner.Player?.Template == null) return;
            titles.Add(new SeasonTitle
            {
                Category = category,
                Player = winner.Player,
                PlayerName = winner.Player.Template.PlayerName,
                TeamLabel = winner.Player.Template.Team.ToString(),
                ValueLabel = winner.Value,
                IsUserPlayer = isUserPlayer != null && isUserPlayer(winner.Player),
            });
        }
    }

    /// <summary>
    /// [TASK-KBO-190] 시즌 완주 루프(144경기 → 포스트시즌 → 시상식 · 결산 → 12단계 리그 승격 → 새 시즌) 진행 규칙. UI(SeasonCycleView)와
    /// 단위 테스트가 같은 경로를 쓴다 - 이벤트 구독 순서(Start)에 기대지 않고 순서대로 직접 호출한다.
    /// </summary>
    public static class SeasonCycle
    {
        /// <summary>정규시즌 1위 또는 한국시리즈 우승이면 다음 리그로 승격(최상위 영구결번 리그는 유지).</summary>
        public static bool ShouldPromote(int? regularSeasonRank, Team? champion, Team userTeam, LeagueTier tier) =>
            tier < LeagueTierTable.Highest && (regularSeasonRank == 1 || (champion.HasValue && champion.Value == userTeam && userTeam != Team.None));

        /// <summary>정규시즌 144경기가 끝나 결산/다음 시즌 전환을 기다리는 상태인지.</summary>
        public static bool IsSeasonOver(LeagueManager league) =>
            league != null && league.PeekNextFixture() == null && league.PlayedGameCount >= LeagueManager.TotalUserGames;

        /// <summary>유저 진출 포스트시즌이 아직 남았는지(브래킷 시작 전 POST_PREP 포함).</summary>
        public static bool HasPendingPostSeason(LeagueManager league, PostSeasonManager post) =>
            league != null && post != null && league.IsUserPlayoffEligible && !post.ChampionTeam.HasValue;

        /// <summary>포스트시즌(와일드카드 → 준PO → PO → 한국시리즈)을 우승팀이 정해질 때까지 진행한다(유저 미진출이면 아무것도 하지 않음).</summary>
        public static Team? PlayPostSeason(LeagueManager league, PostSeasonManager post)
        {
            if (!HasPendingPostSeason(league, post)) return post?.ChampionTeam;
            if (!post.IsPostSeasonActive) post.BeginPostSeason();
            for (int guard = 0; guard < 64 && post.IsPostSeasonActive && !post.ChampionTeam.HasValue; guard++)
                post.PlayCurrentSeriesToCompletion();
            return post.ChampionTeam;
        }

        /// <summary>정규시즌 종료 후: 포스트시즌(진출 시) → 시즌 결산 · 타이틀 시상식(보상 1회 지급). 이미 결산했으면 그 리포트를 돌려준다.</summary>
        public static SeasonEndReport CompleteSeason(LeagueManager league, PostSeasonManager post, SeasonRewardManager reward)
        {
            if (!IsSeasonOver(league)) return null;
            PlayPostSeason(league, post);
            return reward != null ? reward.GrantSeasonEndRewardOnce() : null;
        }
    }
}
