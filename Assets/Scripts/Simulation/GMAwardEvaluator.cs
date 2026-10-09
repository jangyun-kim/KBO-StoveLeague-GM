using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Models;
using KBOManager.Services;

namespace KBOManager.Simulation
{
    /// <summary>
    /// [TASK-GM-04] KBO 7대 시상(기획서 1.1~1.7) · 포스트시즌 · 연도 전환 평가기. 결과는 GMLeagueState.Awards(SeasonAwardCeremonyBundle)에 쌓인다.
    ///   - 인시즌: 24경기마다 월간 MVP(월간 WAR 70% + 기자단/팬 투표 30%) · 월간 캡스플레이상(월간 수비 기여 점수), 72경기 직후 7월 올스타전(드림 vs 나눔 + 이벤트 3종)
    ///   - 시즌 후: 포스트시즌(와일드카드 → 준PO → PO → 한국시리즈) → 11월 KBO 시상식(MVP · 신인 · 타이틀 14 · 수비 10 · 특별 2) → 12월 골든글러브(10)
    ///   - 단장 역학: MVP · 골든글러브 · 타이틀 홀더 수상자 Ego +1(최대 5) · 연봉 15~30% 인상 → 차기 시즌 팀워크 · 샐러리캡 과제
    ///   - AdvanceToNextSeasonYear: 남은 단계를 마저 치르고 연도 +1 · 나이 +1 · 계약 -1 · 부상 초기화 · 시즌 기록 리셋(수상 이력 · 성향 보존) → 0/144
    /// 모든 수상은 선수 CareerAwardIds에 태그로 남는다(예: "MVP_2026", "GOLDEN_GLOVE_2026_SS", "TITLE_HOLDER_2026_HR").
    /// </summary>
    public static class GMAwardEvaluator
    {
        public const int MonthGames = 24;
        public const int MonthCount = GMLiveSeasonSimulator.SeasonGames / MonthGames; // 6
        public static readonly string[] MonthLabels = { "3·4월", "5월", "6월", "7월", "8월", "9월" };
        public static readonly string[] DreamTeams = { NameAliasTable.SAM, NameAliasTable.DOO, NameAliasTable.KT, NameAliasTable.SSG, NameAliasTable.LOT };
        public static readonly string[] NanumTeams = { NameAliasTable.LG, NameAliasTable.NC, NameAliasTable.KIA, NameAliasTable.HAN, NameAliasTable.KIW };

        public const int RookieMaxAge = 23;
        public const int MinRaisePercent = 15, MaxRaisePercent = 30;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ================================================================== 수비 기여 점수

        /// <summary>
        /// 개인 수비 기여 점수 = 출전 × 0.25 × (수비력/70) + 처리 기회 × 0.05 + 호수비 × 1.5 - 실책 × 2.0
        /// + (수비율 - .950) × 출전 × 0.5 + 무실책 보너스(10경기 이상 실책 0이면 +4).
        /// </summary>
        public static float DefensiveScore(int games, int chances, int errors, int finePlays, int rating)
        {
            if (games <= 0) return 0f;
            double fpct = chances + errors == 0 ? 1.0 : (double)chances / (chances + errors);
            double score = games * 0.25 * (Math.Max(20, rating) / 70.0) + chances * 0.05 + finePlays * 1.5 - errors * 2.0
                           + (fpct - 0.95) * games * 0.5 + (errors == 0 && games >= 10 ? 4.0 : 0.0);
            return (float)score;
        }

        public static string SlotLabel(int slot)
        {
            if (slot == GMPlayerSeasonStats.PitcherSlot) return "투";
            if (slot < 0) return "-";
            return GMLiveSeasonSimulator.PositionShort((BatterPosition)slot);
        }

        public static string SlotName(int slot)
        {
            switch (slot)
            {
                case 9: return "투수";
                case 0: return "포수";
                case 1: return "1루수";
                case 2: return "2루수";
                case 3: return "3루수";
                case 4: return "유격수";
                case 5: return "좌익수";
                case 6: return "중견수";
                case 7: return "우익수";
                case 8: return "지명타자";
                default: return "-";
            }
        }

        private static readonly string[] SlotCodes = { "C", "1B", "2B", "3B", "SS", "LF", "CF", "RF", "DH", "P" };

        // ================================================================== 공통

        private sealed class Ctx
        {
            public GMLiveSeasonSimulator Sim;
            public GMLeagueState League;
            public Dictionary<string, Player> Players;
            public int Year => League.SeasonYear;
            public Player P(string id) => id != null && Players.TryGetValue(id, out var p) ? p : null;
        }

        private static Ctx ContextOf(GMLiveSeasonSimulator sim)
        {
            var players = new Dictionary<string, Player>();
            foreach (var p in sim.League.AllPlayers) if (p?.InstanceId != null) players[p.InstanceId] = p;
            return new Ctx { Sim = sim, League = sim.League, Players = players };
        }

        private static SeasonAwardCeremonyBundle Bundle(GMLeagueState league)
        {
            if (league.Awards == null) league.Awards = new SeasonAwardCeremonyBundle { SeasonYear = league.SeasonYear };
            if (league.Awards.SeasonYear == 0) league.Awards.SeasonYear = league.SeasonYear;
            return league.Awards;
        }

        private static string Team(string code) => NameAliasTable.DisplayTeamName(code);
        private static string ShortTeam(string code) => KBOManager.UI.CompyaUiKit.ShortName(NameAliasTable.ToTeam(code));

        /// <summary>수상자 1명을 만들고 CareerAwardIds에 태그를 남긴다(중복 태그 방지).</summary>
        private static GMAwardWinner Award(Ctx c, string awardId, string name, GMAwardCategory cat, string section, Player p, string teamCode, string position,
            double value, string valueLabel, string tag, string note = "")
        {
            var w = new GMAwardWinner
            {
                AwardId = awardId, AwardName = name, Category = cat, Section = section,
                PlayerId = p?.InstanceId ?? "", PlayerName = p?.Template?.PlayerName ?? "", TeamCode = teamCode ?? (p != null ? c.League.TeamCodeOf(p) : "") ?? "",
                Position = position ?? "", Value = value, ValueLabel = valueLabel ?? "", Note = note ?? "", CareerTag = tag ?? "",
            };
            if (p != null && !string.IsNullOrEmpty(tag))
            {
                if (p.CareerAwardIds == null) p.CareerAwardIds = new List<string>();
                if (!p.CareerAwardIds.Contains(tag)) p.CareerAwardIds.Add(tag);
            }
            return w;
        }

        private static string PositionOf(Ctx c, GMPlayerSeasonStats s)
        {
            int slot = s.PrimarySlot;
            if (slot >= 0) return SlotLabel(slot);
            var p = c.P(s.PlayerId);
            return p == null ? "-" : p.IsPitcher ? "투" : GMLiveSeasonSimulator.PositionShort(p.Template.BatterPosition);
        }

        /// <summary>값 목록을 0~1로 정규화(모두 같으면 1).</summary>
        private static Dictionary<T, double> Normalize<T>(IEnumerable<(T key, double v)> items)
        {
            var list = items.ToList();
            var result = new Dictionary<T, double>();
            if (list.Count == 0) return result;
            double min = list.Min(x => x.v), max = list.Max(x => x.v);
            foreach (var (k, v) in list) result[k] = max - min < 1e-9 ? 1.0 : (v - min) / (max - min);
            return result;
        }

        /// <summary>상위 후보 점수로 득표율(%)을 만든다 - 상위 5명 (점수 - 6위 수준)의 1.5제곱 비례.</summary>
        private static double VoteShare(IList<double> orderedScores)
        {
            if (orderedScores.Count == 0) return 0;
            if (orderedScores.Count == 1) return 100;
            var top = orderedScores.Take(5).ToList();
            double floor = orderedScores.Count > 5 ? orderedScores[5] : top.Min() - Math.Max(0.5, Math.Abs(top.Min()) * 0.2);
            var w = top.Select(x => Math.Pow(Math.Max(0.01, x - floor), 1.5)).ToList();
            return w[0] / w.Sum() * 100.0;
        }

        // ================================================================== 1.7 월간 시상

        /// <summary>
        /// 1.7 월간 시상(24경기마다, 시즌 6회). 월간 MVP = 월간 WAR(70%) + 기자단/팬 투표(30% - 홈런 · 타점 · 안타 · 도루 / 승 · 세이브 · 홀드 · 탈삼진),
        /// 월간 캡스플레이상 = 월간 수비 기여 점수(출전 · 처리 기회 · 호수비 · 무실책). 최신 소식 등록, 내 구단 수상자 컨디션 · 만족도 · 팬 지지율 상승.
        /// </summary>
        public static GMMonthlyAwardData EvaluateMonthlyAwards(GMLiveSeasonSimulator sim, int day)
        {
            var c = ContextOf(sim);
            var bundle = Bundle(c.League);
            int index = c.League.GamesPlayed / MonthGames;
            if (index < 1 || index > MonthCount || bundle.Monthly.Any(m => m.MonthIndex == index)) return bundle.Monthly.FirstOrDefault(m => m.MonthIndex == index);
            var month = new GMMonthlyAwardData { MonthIndex = index, MonthLabel = MonthLabels[index - 1], StartGame = (index - 1) * MonthGames + 1, EndGame = index * MonthGames };
            var stats = c.League.Stats.Values.Where(s => c.P(s.PlayerId) != null).ToList();

            // 1.7.1 월간 MVP
            var pool = stats.Where(s => s.IsPitcher ? s.OutsPitched - s.MoOuts >= 30 : s.PA - s.MoPA >= 50).ToList();
            if (pool.Count == 0) pool = stats.Where(s => s.PA - s.MoPA > 0 || s.OutsPitched - s.MoOuts > 0).ToList();
            if (pool.Count == 0) pool = stats;
            var war = Normalize(pool.Select(s => (s.PlayerId, (double)(s.WAR - s.MoWAR))));
            var vote = Normalize(pool.Select(s => (s.PlayerId, s.IsPitcher
                ? (s.W - s.MoW) * 0.25 + (s.SV - s.MoSV) * 0.12 + (s.HLD - s.MoHLD) * 0.06 + (s.PSO - s.MoPSO) * 0.01
                : (s.HR - s.MoHR) * 0.12 + (s.RBI - s.MoRBI) * 0.03 + (s.H - s.MoH) * 0.02 + (s.SB - s.MoSB) * 0.03)));
            var mvpOrder = pool.Select(s => (s, score: war[s.PlayerId] * 0.7 + vote[s.PlayerId] * 0.3)).OrderByDescending(x => x.score).ThenBy(x => x.s.PlayerId).ToList();
            if (mvpOrder.Count > 0)
            {
                var s = mvpOrder[0].s;
                var p = c.P(s.PlayerId);
                string line = s.IsPitcher
                    ? $"{s.W - s.MoW}승 {s.SV - s.MoSV}세이브 {s.PSO - s.MoPSO}탈삼진"
                    : $"{s.H - s.MoH}안타 {s.HR - s.MoHR}홈런 {s.RBI - s.MoRBI}타점";
                month.Mvp = Award(c, "MONTHLY_MVP", "KBO 월간 MVP", GMAwardCategory.Monthly, "1.7.1", p, s.TeamCode, PositionOf(c, s),
                    s.WAR - s.MoWAR, $"월간 WAR {(s.WAR - s.MoWAR).ToString("0.00", Inv)} · {line}", $"MONTHLY_MVP_{c.Year}_M{index}",
                    $"투표 점수 {(mvpOrder[0].score * 100).ToString("0.0", Inv)}");
            }

            // 1.7.2 월간 캡스플레이상
            var dpool = stats.Where(s => !s.IsPitcher && s.DefG - s.MoDefG >= 10).ToList();
            if (dpool.Count == 0) dpool = stats.Where(s => s.DefG - s.MoDefG > 0).ToList();
            if (dpool.Count == 0) dpool = stats;
            var capsOrder = dpool.Select(s => (s, score: DefensiveScore(s.DefG - s.MoDefG, s.Chances - s.MoChances, s.Errors - s.MoErrors, s.FinePlays - s.MoFine, s.DefRating)))
                .OrderByDescending(x => x.score).ThenBy(x => x.s.PlayerId).ToList();
            if (capsOrder.Count > 0)
            {
                var (s, score) = capsOrder[0];
                var p = c.P(s.PlayerId);
                int e = s.Errors - s.MoErrors;
                month.CapsPlay = Award(c, "MONTHLY_CAPSPLAY", "월간 캡스플레이상", GMAwardCategory.Monthly, "1.7.2", p, s.TeamCode, PositionOf(c, s),
                    score, $"수비 기여 {score.ToString("0.0", Inv)} · 호수비 {s.FinePlays - s.MoFine} · {(e == 0 ? "무실책" : $"실책 {e}")}",
                    $"MONTHLY_CAPSPLAY_{c.Year}_M{index}");
            }

            bundle.Monthly.Add(month);
            foreach (var s in c.League.Stats.Values) s.SnapshotMonth();

            // 소식 · 내 구단 보너스
            foreach (var w in new[] { month.Mvp, month.CapsPlay })
            {
                if (!w.HasWinner) continue;
                bool mine = w.TeamCode == c.League.SelectedTeamCode;
                sim.PostNews(day, null, GMNewsKind.Monthly, $"{month.MonthLabel} {w.AwardName}: {w.PlayerName}",
                    $"{Team(w.TeamCode)} {w.PlayerName}({w.Position})이(가) {c.Year} KBO {month.MonthLabel} {w.AwardName}을(를) 수상했습니다. {w.ValueLabel}.", mine, mine);
                if (!mine) continue;
                var p = c.P(w.PlayerId);
                if (p != null)
                {
                    p.ShiftCondition(true);
                    p.PersonalMorale = Math.Min(100, p.PersonalMorale + 5);
                }
                var team = c.League.UserTeam;
                if (team != null) team.FanSupport = GMTeamFan.Clamp(team.FanSupport + GMTeamFan.MonthlyAwardBonus);
            }
            return month;
        }

        // ================================================================== 1.6 올스타전

        /// <summary>
        /// 1.6 7월 올스타전(전반기 72경기 직후). 드림(SAM · DOO · KT · SSG · LOT) vs 나눔(LG · NC · KIA · HAN · KIW) 단판 + 이벤트 3종.
        /// 시상 10종: 미스터 올스타 · 우수투수 · 우수타자 · 승리감독 · 패전투수 · 감투 · 홈런더비 우승/준우승 · 홈런레이스 · 퍼펙트피처.
        /// 결과는 최신 소식 + 일시정지 팝업([올스타전 결과 & 시상 리포트]).
        /// </summary>
        public static GMAllStarGameData HoldAllStarGame(GMLiveSeasonSimulator sim, int day)
        {
            var c = ContextOf(sim);
            var bundle = Bundle(c.League);
            if (bundle.AllStar != null && bundle.AllStar.Played) return bundle.AllStar;
            var data = new GMAllStarGameData { Played = true, DreamTeams = DreamTeams.ToList(), NanumTeams = NanumTeams.ToList() };
            int seed = c.League.Seed * 13 + c.Year * 101 + 72;
            var rng = new Random(seed);
            data.DateLabel = new DateTime(c.Year, 7, 12).ToString("MM/dd/yyyy", Inv);

            var dream = AllStarRoster(c, DreamTeams);
            var nanum = AllStarRoster(c, NanumTeams);
            bool dreamHome = c.Year % 2 == 0;
            var game = PlayExhibition(sim, dreamHome ? dream : nanum, dreamHome ? nanum : dream, dreamHome ? "드림 올스타" : "나눔 올스타",
                dreamHome ? "나눔 올스타" : "드림 올스타", null, null, seed, false);
            data.DreamScore = dreamHome ? game.HomeRuns : game.AwayRuns;
            data.NanumScore = dreamHome ? game.AwayRuns : game.HomeRuns;
            if (data.DreamScore == data.NanumScore)
            {
                // 승부치기 - 양 팀 최고 거포 홈런 스윙오프(5스윙)
                var (dPick, dHr) = SwingOff(dream, rng);
                var (nPick, nHr) = SwingOff(nanum, rng);
                while (dHr == nHr) { dHr += rng.NextDouble() < 0.5 ? 1 : 0; nHr += rng.NextDouble() < 0.5 ? 1 : 0; }
                data.DreamWon = dHr > nHr;
                data.Decider = $"9회 {data.DreamScore}:{data.NanumScore} 동점 - 홈런 스윙오프 {dPick?.Template.PlayerName} {dHr}개 vs {nPick?.Template.PlayerName} {nHr}개";
            }
            else data.DreamWon = data.DreamScore > data.NanumScore;

            var winRoster = data.DreamWon ? dream : nanum;
            var loseRoster = data.DreamWon ? nanum : dream;
            var winSet = new HashSet<Player>(winRoster);
            Player Best(IEnumerable<Player> players, Func<Player, double> score) => players.OrderByDescending(score).ThenBy(p => p.InstanceId).FirstOrDefault();
            string TeamOfP(Player p) => c.League.TeamCodeOf(p);
            string Pos(Player p) => p.IsPitcher ? "투" : GMLiveSeasonSimulator.PositionShort(p.Template.BatterPosition);
            string Line(Player p) => game.LineOf(p);

            var played = game.Batting.Keys.Concat(game.Pitching.Keys).Distinct().ToList();
            var mvp = Best(played.Where(winSet.Contains), game.GameScore) ?? winRoster.First();
            data.Awards.Add(Award(c, "ALLSTAR_MVP", "미스터 올스타", GMAwardCategory.AllStar, "1.6.1", mvp, TeamOfP(mvp), Pos(mvp), game.GameScore(mvp), Line(mvp), $"ALLSTAR_MVP_{c.Year}"));
            var pitchers = game.Pitching.Keys.Where(p => p != mvp).ToList();
            var bestP = Best(pitchers, game.GameScore) ?? Best(winRoster.Where(p => p.IsPitcher && p != mvp), p => p.BaseOverall);
            data.Awards.Add(Award(c, "ALLSTAR_BEST_PITCHER", "올스타전 우수투수상", GMAwardCategory.AllStar, "1.6.2", bestP, TeamOfP(bestP), "투", game.GameScore(bestP), Line(bestP), $"ALLSTAR_BEST_PITCHER_{c.Year}"));
            var batters = game.Batting.Keys.Where(p => p != mvp && !p.IsPitcher).ToList();
            var bestB = Best(batters, game.GameScore) ?? Best(winRoster.Where(p => !p.IsPitcher && p != mvp), p => p.BaseOverall);
            data.Awards.Add(Award(c, "ALLSTAR_BEST_BATTER", "올스타전 우수타자상", GMAwardCategory.AllStar, "1.6.2", bestB, TeamOfP(bestB), Pos(bestB), game.GameScore(bestB), Line(bestB), $"ALLSTAR_BEST_BATTER_{c.Year}"));
            // 승리감독상 - 승리 팀 전반기 최고 승률 구단 감독
            var winTeams = data.DreamWon ? DreamTeams : NanumTeams;
            string managerTeam = c.Sim.Standings().First(r => winTeams.Contains(r.TeamCode)).TeamCode;
            data.Awards.Add(new GMAwardWinner
            {
                AwardId = "ALLSTAR_WIN_MANAGER", AwardName = "올스타전 승리감독상", Category = GMAwardCategory.AllStar, Section = "1.6.2",
                PlayerName = $"{ShortTeam(managerTeam)} 감독", TeamCode = managerTeam, Position = "감독",
                ValueLabel = $"{data.WinnerLabel} 지휘 · 전반기 {c.League.RecordOf(managerTeam).W}승", Note = "전반기 승률 1위 구단 감독",
            });
            // 패전투수상 · 감투상
            var loseP = (game.LosingPitcher != null && !winSet.Contains(game.LosingPitcher) ? game.LosingPitcher : null)
                        ?? game.Pitching.Keys.Where(p => !winSet.Contains(p)).OrderByDescending(p => game.Pitching[p].R).ThenBy(p => game.Pitching[p].Outs).FirstOrDefault()
                        ?? loseRoster.First(p => p.IsPitcher);
            data.Awards.Add(Award(c, "ALLSTAR_LOSING_PITCHER", "올스타전 패전투수상", GMAwardCategory.AllStar, "1.6.3", loseP, TeamOfP(loseP), "투", 0, Line(loseP), $"ALLSTAR_LOSING_PITCHER_{c.Year}"));
            var fighter = Best(played.Where(p => !winSet.Contains(p) && p != loseP), game.GameScore) ?? loseRoster.First(p => p != loseP);
            data.Awards.Add(Award(c, "ALLSTAR_FIGHTING_SPIRIT", "올스타전 감투상", GMAwardCategory.AllStar, "1.6.3", fighter, TeamOfP(fighter), Pos(fighter), game.GameScore(fighter), Line(fighter), $"ALLSTAR_FIGHTING_SPIRIT_{c.Year}"));

            // 1.6.4 이벤트 경기
            HoldEventContests(c, data, dream.Concat(nanum).ToList(), rng);

            data.Summary = $"{data.DateLabel} 올스타전 - 드림 올스타 {data.DreamScore} : {data.NanumScore} 나눔 올스타, {data.WinnerLabel} 승리" +
                           (string.IsNullOrEmpty(data.Decider) ? "" : $" ({data.Decider})");
            bundle.AllStar = data;

            bool mine = data.Awards.Any(a => a.TeamCode == c.League.SelectedTeamCode && !string.IsNullOrEmpty(a.PlayerId));
            var mvpAward = data.Find("ALLSTAR_MVP");
            sim.PostNews(day, data.DateLabel, GMNewsKind.Award, $"{c.Year} 올스타전: {data.WinnerLabel} 승리 · 미스터 올스타 {mvpAward.PlayerName}",
                $"{data.Summary}. 미스터 올스타 {mvpAward.PlayerName}({ShortTeam(mvpAward.TeamCode)}) {mvpAward.ValueLabel}. 홈런더비 우승 {data.Find("ALLSTAR_HR_DERBY")?.PlayerName}.",
                mine, true, pause: true);
            return data;
        }

        /// <summary>올스타 28인 - 포지션별 팬 투표(시즌 WAR + OVR) 베스트 9 + 벤치 6, 선발 5 + 불펜 8.</summary>
        private static List<Player> AllStarRoster(Ctx c, string[] teams)
        {
            var pool = teams.Where(c.League.Teams.ContainsKey).SelectMany(t => c.League.Teams[t].Roster).Where(p => p.InjuryRemainingDays <= 0).ToList();
            double Fan(Player p)
            {
                c.League.Stats.TryGetValue(p.InstanceId, out var s);
                return (s?.WAR ?? 0) * 3 + p.BaseOverall * 0.1;
            }
            var roster = new List<Player>();
            foreach (BatterPosition pos in Enum.GetValues(typeof(BatterPosition)))
            {
                var pick = pool.Where(p => !p.IsPitcher && p.Template.BatterPosition == pos && !roster.Contains(p)).OrderByDescending(Fan).FirstOrDefault()
                           ?? pool.Where(p => !p.IsPitcher && !roster.Contains(p)).OrderByDescending(Fan).FirstOrDefault();
                if (pick != null) roster.Add(pick);
            }
            roster.AddRange(pool.Where(p => !p.IsPitcher && !roster.Contains(p)).OrderByDescending(Fan).Take(GMRosterLoader.BatterCount - roster.Count));
            roster.AddRange(pool.Where(p => p.IsPitcher && p.Template.PitcherRole == PitcherRole.StartingPitcher).OrderByDescending(Fan).Take(GMRosterLoader.StartingPitcherCount));
            roster.AddRange(pool.Where(p => p.IsPitcher && !roster.Contains(p)).OrderByDescending(Fan).Take(GMRosterLoader.PitcherCount - roster.Count(p => p.IsPitcher)));
            return roster;
        }

        private static (Player, int) SwingOff(List<Player> roster, Random rng)
        {
            var slugger = roster.Where(p => !p.IsPitcher).OrderByDescending(p => p.Template.BatterStats.Power).FirstOrDefault();
            int hr = 0;
            if (slugger != null) for (int i = 0; i < 5; i++) if (rng.NextDouble() < 0.15 + slugger.Template.BatterStats.Power / 250.0) hr++;
            return (slugger, hr);
        }

        /// <summary>1.6.4 이벤트 경기 - 홈런더비(8인 → 4강 → 결승, 우승 · 준우승), 컴투스프로야구 홈런레이스(구단 대표 10인 · 60초), 퍼펙트피처(구단 대표 투수 10인 · 표적 10구).</summary>
        private static void HoldEventContests(Ctx c, GMAllStarGameData data, List<Player> allStars, Random rng)
        {
            int Swings(Player p, int swings, double bonus = 0)
            {
                int hr = 0;
                double chance = Math.Min(0.75, 0.12 + p.Template.BatterStats.Power / 230.0 + bonus);
                for (int i = 0; i < swings; i++) if (rng.NextDouble() < chance) hr++;
                return hr;
            }
            double SeasonHr(Player p) => c.League.Stats.TryGetValue(p.InstanceId, out var s) ? s.HR : 0;

            // 홈런더비 - 전반기 홈런 상위 8인(올스타 타자 우선)
            var entrants = c.League.AllPlayers.Where(p => !p.IsPitcher && p.InjuryRemainingDays <= 0)
                .OrderByDescending(p => allStars.Contains(p) ? 1 : 0).ThenByDescending(SeasonHr).ThenByDescending(p => p.Template.BatterStats.Power).Take(8).ToList();
            var round1 = entrants.Select(p => (p, hr: Swings(p, 10))).OrderByDescending(x => x.hr).ThenByDescending(x => x.p.Template.BatterStats.Power).ToList();
            var semis = round1.Take(4).Select(x => (x.p, hr: Swings(x.p, 10))).OrderByDescending(x => x.hr).ThenByDescending(x => x.p.Template.BatterStats.Power).ToList();
            var finalists = semis.Take(2).ToList();
            var final = finalists.Select(x => (x.p, hr: Swings(x.p, 12, 0.03))).ToList();
            while (final.Count == 2 && final[0].hr == final[1].hr) final = final.Select(x => (x.p, hr: x.hr + Swings(x.p, 3))).ToList();
            final = final.OrderByDescending(x => x.hr).ToList();
            if (final.Count >= 1)
                data.Awards.Add(Award(c, "ALLSTAR_HR_DERBY", "홈런더비 우승", GMAwardCategory.AllStar, "1.6.4", final[0].p, c.League.TeamCodeOf(final[0].p),
                    GMLiveSeasonSimulator.PositionShort(final[0].p.Template.BatterPosition), final[0].hr, $"결승 {final[0].hr}홈런", $"ALLSTAR_HR_DERBY_{c.Year}"));
            if (final.Count >= 2)
                data.Awards.Add(Award(c, "ALLSTAR_HR_DERBY_RUNNER_UP", "홈런더비 준우승", GMAwardCategory.AllStar, "1.6.4", final[1].p, c.League.TeamCodeOf(final[1].p),
                    GMLiveSeasonSimulator.PositionShort(final[1].p.Template.BatterPosition), final[1].hr, $"결승 {final[1].hr}홈런", $"ALLSTAR_HR_DERBY_RUNNER_UP_{c.Year}"));

            // 컴투스프로야구 홈런레이스 - 구단별 대표 거포 1인(10인), 60초 제한(최대 14스윙)
            var racers = NameAliasTable.CanonicalTeamCodes.Where(c.League.Teams.ContainsKey)
                .Select(code => c.League.Teams[code].Roster.Where(p => !p.IsPitcher && p.InjuryRemainingDays <= 0).OrderByDescending(p => p.Template.BatterStats.Power).FirstOrDefault())
                .Where(p => p != null).ToList();
            var race = racers.Select(p => (p, hr: Swings(p, 14))).OrderByDescending(x => x.hr).ThenByDescending(x => x.p.Template.BatterStats.Contact).ThenBy(x => x.p.InstanceId).ToList();
            if (race.Count > 0)
                data.Awards.Add(Award(c, "ALLSTAR_HR_RACE", "컴투스프로야구 홈런레이스 우승", GMAwardCategory.AllStar, "1.6.4", race[0].p, c.League.TeamCodeOf(race[0].p),
                    GMLiveSeasonSimulator.PositionShort(race[0].p.Template.BatterPosition), race[0].hr, $"60초 {race[0].hr}홈런", $"ALLSTAR_HR_RACE_{c.Year}"));

            // 퍼펙트피처 - 구단별 제구 1위 투수, 표적 10구(제구 비례 명중)
            var throwers = NameAliasTable.CanonicalTeamCodes.Where(c.League.Teams.ContainsKey)
                .Select(code => c.League.Teams[code].Roster.Where(p => p.IsPitcher && p.InjuryRemainingDays <= 0).OrderByDescending(p => p.Template.PitcherStats.Control).FirstOrDefault())
                .Where(p => p != null).ToList();
            var perfect = throwers.Select(p =>
            {
                int hits = 0;
                double chance = Math.Min(0.85, 0.2 + p.Template.PitcherStats.Control / 200.0);
                for (int i = 0; i < 10; i++) if (rng.NextDouble() < chance) hits++;
                return (p, hits);
            }).OrderByDescending(x => x.hits).ThenByDescending(x => x.p.Template.PitcherStats.Control).ThenBy(x => x.p.InstanceId).ToList();
            if (perfect.Count > 0)
                data.Awards.Add(Award(c, "ALLSTAR_PERFECT_PITCHER", "퍼펙트피처 우승", GMAwardCategory.AllStar, "1.6.4", perfect[0].p, c.League.TeamCodeOf(perfect[0].p),
                    "투", perfect[0].hits, $"표적 {perfect[0].hits}/10 명중", $"ALLSTAR_PERFECT_PITCHER_{c.Year}"));
        }

        // ================================================================== 단판 경기(올스타 · 포스트시즌)

        public sealed class ExhibitionBatLine { public int PA, AB, H, HR, RBI, R, BB, SO; }
        public sealed class ExhibitionPitchLine { public int Outs, R, H, BB, SO; }

        /// <summary>시즌 기록에 반영되지 않는 단판 경기 결과(선수별 라인 · 패전 투수 후보).</summary>
        public sealed class ExhibitionResult
        {
            public int HomeRuns, AwayRuns;
            public readonly Dictionary<Player, ExhibitionBatLine> Batting = new Dictionary<Player, ExhibitionBatLine>();
            public readonly Dictionary<Player, ExhibitionPitchLine> Pitching = new Dictionary<Player, ExhibitionPitchLine>();
            public Player LosingPitcher;

            /// <summary>경기 활약 점수 - 타자: 안타 + 홈런 2.5 + 타점 1.2 + 득점 0.6 + 볼넷 0.4 - 삼진 0.2, 투수: 이닝 1.2 + 탈삼진 0.5 - 실점 1.5 - 피안타 0.3.</summary>
            public double GameScore(Player p)
            {
                if (p == null) return 0;
                double score = 0;
                if (Batting.TryGetValue(p, out var b)) score += b.H + b.HR * 2.5 + b.RBI * 1.2 + b.R * 0.6 + b.BB * 0.4 - b.SO * 0.2;
                if (Pitching.TryGetValue(p, out var q)) score += q.Outs / 3.0 * 1.2 + q.SO * 0.5 - q.R * 1.5 - q.H * 0.3;
                return score;
            }

            public string LineOf(Player p)
            {
                if (p == null) return "-";
                if (Pitching.TryGetValue(p, out var q)) return $"{q.Outs / 3}{(q.Outs % 3 > 0 ? $" {q.Outs % 3}/3" : "")}이닝 {q.R}실점 {q.SO}탈삼진";
                if (Batting.TryGetValue(p, out var b)) return $"{b.AB}타수 {b.H}안타{(b.HR > 0 ? $" {b.HR}홈런" : "")} {b.RBI}타점";
                return "출전 기록 없음";
            }
        }

        /// <summary>
        /// MatchEngine 단판 경기 - 시즌 누적 · 순위에 반영하지 않는다. 출전 선수 체력 · 컨디션은 경기 전 상태로 되돌린다.
        /// 라인업이 null이면 빈 지정(기본 OVR 편성)을 쓴다(유저 구단 LineupAssignment.Active를 끌어오지 않도록).
        /// </summary>
        public static ExhibitionResult PlayExhibition(GMLiveSeasonSimulator sim, List<Player> home, List<Player> away, string homeName, string awayName,
            TeamPowerModifiers? homeMods, TeamPowerModifiers? awayMods, int seed, bool isPostSeason, LineupAssignment homeLineup = null, LineupAssignment awayLineup = null,
            Player homeStarter = null, Player awayStarter = null)
        {
            var everyone = home.Concat(away).Distinct().ToList();
            var saved = everyone.Select(p => (p, p.CurrentStamina, p.CurrentCondition)).ToList();
            var result = new ExhibitionResult();
            try
            {
                var engine = new MatchEngine(home, away, homeMods ?? TeamPowerModifiers.None, awayMods ?? TeamPowerModifiers.None, sim?.SkillDB, sim?.Config, seed)
                {
                    HomeAssignment = homeLineup ?? new LineupAssignment(),
                    AwayAssignment = awayLineup ?? new LineupAssignment(),
                    HomeDesignatedStarter = homeStarter ?? StartingRotation.PickFor(home, 0, homeLineup ?? new LineupAssignment()),
                    AwayDesignatedStarter = awayStarter ?? StartingRotation.PickFor(away, 0, awayLineup ?? new LineupAssignment()),
                };
                engine.BeginMatch(homeName, awayName, isPostSeason);
                var bases = new Player[4];
                (int inning, bool top) half = (0, false);
                int outsInHalf = 0, guard = 0;
                Player loseCandHome = null, loseCandAway = null;
                while (!engine.IsGameOver && guard++ < 400)
                {
                    var step = engine.PlayNextAtBat();
                    if (step.Batter == null || step.Pitcher == null || step.State == null) continue;
                    bool top = step.IsTopHalf;
                    if (half.inning != step.State.Inning || half.top != top)
                    {
                        half = (step.State.Inning, top);
                        Array.Clear(bases, 0, bases.Length);
                        outsInHalf = 0;
                    }
                    if (!result.Batting.TryGetValue(step.Batter, out var b)) result.Batting[step.Batter] = b = new ExhibitionBatLine();
                    if (!result.Pitching.TryGetValue(step.Pitcher, out var q)) result.Pitching[step.Pitcher] = q = new ExhibitionPitchLine();
                    b.PA++;
                    if (step.IsError) b.AB++;
                    else
                    {
                        switch (step.Result)
                        {
                            case AtBatResult.Walk: b.BB++; q.BB++; break;
                            case AtBatResult.Strikeout: b.AB++; b.SO++; q.SO++; break;
                            case AtBatResult.Single:
                            case AtBatResult.Double:
                            case AtBatResult.Triple: b.AB++; b.H++; q.H++; break;
                            case AtBatResult.HomeRun: b.AB++; b.H++; b.HR++; q.H++; break;
                            default: b.AB++; break;
                        }
                        b.RBI += step.RunsScoredThisPlay;
                    }
                    q.R += step.RunsScoredThisPlay;

                    var before = (Player[])bases.Clone();
                    foreach (var m in step.RunnerMovements) if (m.FromBase >= 1 && m.FromBase <= 3) bases[m.FromBase] = null;
                    foreach (var m in step.RunnerMovements)
                    {
                        var runner = m.FromBase == 0 ? step.Batter : (m.FromBase >= 1 && m.FromBase <= 3 ? before[m.FromBase] : null);
                        if (runner == null) continue;
                        if (m.ToBase == 4)
                        {
                            if (!result.Batting.TryGetValue(runner, out var rl)) result.Batting[runner] = rl = new ExhibitionBatLine();
                            rl.R++;
                        }
                        else if (m.ToBase >= 1 && m.ToBase <= 3) bases[m.ToBase] = runner;
                    }
                    int outsNow = Math.Min(3, step.State.Outs);
                    if (outsNow > outsInHalf) { q.Outs += outsNow - outsInHalf; outsInHalf = outsNow; }

                    int battingAfter = top ? step.AwayScore : step.HomeScore, fielding = top ? step.HomeScore : step.AwayScore;
                    int battingBefore = battingAfter - step.RunsScoredThisPlay;
                    if (battingBefore <= fielding && battingAfter > fielding)
                    {
                        if (top) loseCandHome = step.Pitcher; else loseCandAway = step.Pitcher;
                    }
                }
                if (isPostSeason && !engine.IsGameOver) engine.DebugForceEndGame(homeName); // [TASK-GM-14] 포스트시즌 무승부 없음
                result.HomeRuns = engine.Result?.HomeTotalScore ?? 0;
                result.AwayRuns = engine.Result?.AwayTotalScore ?? 0;
                if (result.HomeRuns > result.AwayRuns) result.LosingPitcher = loseCandAway;
                else if (result.AwayRuns > result.HomeRuns) result.LosingPitcher = loseCandHome;
            }
            finally
            {
                foreach (var (p, stamina, condition) in saved) { p.CurrentStamina = stamina; p.CurrentCondition = condition; }
            }
            return result;
        }

        // ================================================================== 포스트시즌

        /// <summary>[TASK-GM-07] 포스트시즌 다음 경기 한 건(브래킷 1경기 진행 · 내 구단 경기 3단계 플로우 공용).</summary>
        public sealed class GMPostseasonGame
        {
            public int SeriesIndex;
            public string Round;
            public string HomeCode, AwayCode;
            public int GameNumber;      // 시리즈 몇 차전(무승부 재경기는 같은 번호)
            public int Seed;
            public bool IsUserGame;
            public Player HomeStarter, AwayStarter;
            public int GameIndex;       // 박스스코어 경기 번호(정규시즌 144 다음부터)
            public string DateLabel;
            public string Title => $"{Round} {GameNumber}차전";
        }

        /// <summary>라운드 정의: (이름, 상위 시드 인덱스, 상위 필요 승수, 하위 필요 승수, 하위 팀 홈 경기 차수).</summary>
        public static readonly (string round, int higherSeed, int higherNeeds, int lowerNeeds, int[] lowerHome)[] PostseasonRounds =
        {
            ("와일드카드 결정전", 3, 1, 2, new int[0]),
            ("준플레이오프", 2, 3, 3, new[] { 3, 4 }),
            ("플레이오프", 1, 3, 3, new[] { 3, 4 }),
            ("한국시리즈", 0, 4, 4, new[] { 3, 4, 5 }),
        };

        private static int PostseasonSeedBase(Ctx c) => c.League.Seed * 7 + c.Year * 977;

        /// <summary>
        /// [TASK-GM-07] 포스트시즌 시작(정규시즌 상위 5개 구단 시드 · 와일드카드 시리즈 개설). 이미 시작했거나 끝났으면 그 상태를 돌려준다.
        /// 정규시즌이 끝나지 않았으면 null.
        /// </summary>
        public static PostseasonSummaryData BeginPostseason(GMLiveSeasonSimulator sim)
        {
            var c = ContextOf(sim);
            var bundle = Bundle(c.League);
            if (bundle.Postseason != null && (bundle.Postseason.Completed || bundle.Postseason.Series.Count > 0)) return bundle.Postseason;
            if (!sim.IsSeasonComplete) return null;
            var ps = new PostseasonSummaryData();
            ps.SeedCodes = sim.Standings().Take(5).Select(r => r.TeamCode).ToList();
            if (ps.SeedCodes.Count < 5) return null;
            ps.Series.Add(NewSeries(ps, 0, ps.SeedCodes[PostseasonRounds[0].higherSeed + 1]));
            bundle.Postseason = ps;
            return ps;
        }

        private static GMPostseasonSeries NewSeries(PostseasonSummaryData ps, int index, string lowerCode)
        {
            var r = PostseasonRounds[index];
            return new GMPostseasonSeries { Round = r.round, HigherCode = ps.SeedCodes[r.higherSeed], LowerCode = lowerCode, HigherNeeds = r.higherNeeds, LowerNeeds = r.lowerNeeds };
        }

        /// <summary>[TASK-GM-07] 다음에 치를 포스트시즌 경기(시리즈 · 차전 · 홈/원정 · 선발 · 시드). 포스트시즌이 끝났으면 null.</summary>
        public static GMPostseasonGame NextPostseasonGame(GMLiveSeasonSimulator sim)
        {
            var ps = BeginPostseason(sim);
            if (ps == null || ps.Completed || ps.Series.Count == 0) return null;
            var c = ContextOf(sim);
            int index = ps.Series.Count - 1;
            var series = ps.Series[index];
            if (!string.IsNullOrEmpty(series.WinnerCode)) return null;
            int g = series.HigherWins + series.LowerWins + 1;
            bool lowerHome = PostseasonRounds[index].lowerHome.Contains(g);
            var homeT = c.League.Teams[lowerHome ? series.LowerCode : series.HigherCode];
            var awayT = c.League.Teams[lowerHome ? series.HigherCode : series.LowerCode];
            string me = c.League.SelectedTeamCode;
            return new GMPostseasonGame
            {
                SeriesIndex = index, Round = series.Round, HomeCode = homeT.TeamCode, AwayCode = awayT.TeamCode, GameNumber = g,
                Seed = PostseasonSeedBase(c) + (ps.GamesSimulated + 1) * 7919,
                IsUserGame = homeT.TeamCode == me || awayT.TeamCode == me,
                HomeStarter = StartingRotation.PickFor(homeT.AvailableRoster, g - 1, homeT.Lineup),
                AwayStarter = StartingRotation.PickFor(awayT.AvailableRoster, g - 1, awayT.Lineup),
                GameIndex = GMLiveSeasonSimulator.SeasonGames + ps.GamesSimulated,
                DateLabel = $"10/{Math.Min(31, 6 + index * 6 + g):00}/{c.Year}",
            };
        }

        /// <summary>[TASK-GM-07] 다음 포스트시즌 경기 1경기를 바로 시뮬레이션해 브래킷에 반영한다(타 구단 경기 · 자동 진행). 진행한 경기(없으면 null).</summary>
        public static GMPostseasonGame PlayNextPostseasonGame(GMLiveSeasonSimulator sim)
        {
            var g = NextPostseasonGame(sim);
            if (g == null) return null;
            var league = sim.League;
            var homeT = league.Teams[g.HomeCode];
            var awayT = league.Teams[g.AwayCode];
            var r = PlayExhibition(sim, homeT.AvailableRoster, awayT.AvailableRoster, homeT.TeamCode, awayT.TeamCode, sim.ModifiersFor(homeT, true), sim.ModifiersFor(awayT, false),
                g.Seed, true, homeT.Lineup, awayT.Lineup, g.HomeStarter, g.AwayStarter);
            RecordPostseasonGame(sim, g, r);
            return g;
        }

        /// <summary>[TASK-GM-07] 내 구단 포스트시즌 경기를 타석 세션으로 연다(전력 분석 → 실시간 이닝 경기 → 경기 결과).</summary>
        public static GMLiveSeasonSimulator.GameSession BeginPostseasonSession(GMLiveSeasonSimulator sim, GMPostseasonGame g)
        {
            if (sim == null || g == null) return null;
            var league = sim.League;
            return sim.CreateExhibitionSession(league.Teams[g.HomeCode], league.Teams[g.AwayCode], g.Seed, g.HomeStarter, g.AwayStarter, g.GameIndex, g.DateLabel, g.Title);
        }

        /// <summary>[TASK-GM-07] 세션으로 치른 포스트시즌 경기를 마무리하고 브래킷에 반영한다. 박스스코어를 돌려준다.</summary>
        public static GMMatchBoxScoreData CompletePostseasonSession(GMLiveSeasonSimulator sim, GMPostseasonGame g, GMLiveSeasonSimulator.GameSession session)
        {
            if (sim == null || g == null || session == null) return null;
            var box = session.Finish();
            RecordPostseasonGame(sim, g, session.ToExhibitionResult());
            return box;
        }

        /// <summary>경기 1건 반영 - 무승부는 재경기, 시리즈가 끝나면 다음 라운드를 열고, 한국시리즈가 끝나면 우승 · MVP · 최종 순위를 확정한다.</summary>
        private static void RecordPostseasonGame(GMLiveSeasonSimulator sim, GMPostseasonGame g, ExhibitionResult r)
        {
            var c = ContextOf(sim);
            var ps = Bundle(c.League).Postseason;
            if (ps == null || ps.Completed || g.SeriesIndex != ps.Series.Count - 1) return;
            var series = ps.Series[g.SeriesIndex];
            ps.GamesSimulated++;
            string label = $"{g.GameNumber}차전 {ShortTeam(g.AwayCode)} {r.AwayRuns} : {r.HomeRuns} {ShortTeam(g.HomeCode)}";
            if (r.HomeRuns == r.AwayRuns) { series.Games.Add(label + " (무승부 · 재경기)"); return; }
            string winner = r.HomeRuns > r.AwayRuns ? g.HomeCode : g.AwayCode;
            if (winner == series.HigherCode) series.HigherWins++; else series.LowerWins++;
            series.Games.Add(label);
            if (g.SeriesIndex == PostseasonRounds.Length - 1)
            {
                foreach (var p in r.Batting.Keys.Concat(r.Pitching.Keys).Distinct())
                {
                    var tally = ps.KsTally.FirstOrDefault(t => t.PlayerId == p.InstanceId);
                    if (tally == null) ps.KsTally.Add(tally = new GMPostseasonTally { PlayerId = p.InstanceId });
                    tally.Score += r.GameScore(p);
                }
            }
            if (series.HigherWins < series.HigherNeeds && series.LowerWins < series.LowerNeeds) return;
            series.WinnerCode = series.HigherWins >= series.HigherNeeds ? series.HigherCode : series.LowerCode;
            if (g.SeriesIndex < PostseasonRounds.Length - 1) ps.Series.Add(NewSeries(ps, g.SeriesIndex + 1, series.WinnerCode));
            else FinalizePostseason(sim, c, ps);
        }

        private static void FinalizePostseason(GMLiveSeasonSimulator sim, Ctx c, PostseasonSummaryData ps)
        {
            var standings = sim.Standings();
            var wc = ps.Series[0]; var semi = ps.Series[1]; var po = ps.Series[2]; var ks = ps.Series[3];
            ps.ChampionCode = ks.WinnerCode;
            ps.RunnerUpCode = ks.LoserCode;
            var champ = c.League.Teams[ps.ChampionCode];
            var ksMvp = ps.KsTally.Select(t => (player: c.P(t.PlayerId), t.Score)).Where(x => x.player != null && champ.Roster.Contains(x.player))
                .OrderByDescending(x => x.Score).ThenBy(x => x.player.InstanceId).Select(x => x.player).FirstOrDefault();
            ps.KoreanSeriesMvp = ksMvp?.Template?.PlayerName ?? "";
            if (ksMvp != null)
            {
                string tag = $"KS_MVP_{c.Year}";
                if (!ksMvp.CareerAwardIds.Contains(tag)) ksMvp.CareerAwardIds.Add(tag);
            }
            foreach (var p in champ.Roster)
            {
                string tag = $"KS_CHAMPION_{c.Year}";
                if (p.CareerAwardIds == null) p.CareerAwardIds = new List<string>();
                if (!p.CareerAwardIds.Contains(tag)) p.CareerAwardIds.Add(tag);
            }
            ps.FinalRankCodes = new List<string> { ks.WinnerCode, ks.LoserCode, po.LoserCode, semi.LoserCode, wc.LoserCode };
            ps.FinalRankCodes.AddRange(standings.Skip(5).Select(r => r.TeamCode));
            ps.Completed = true;
            if (c.League.Phase < GMSeasonPhase.AwardsCeremony) c.League.Phase = GMSeasonPhase.AwardsCeremony;

            string me = c.League.SelectedTeamCode;
            int gi = GMLiveSeasonSimulator.SeasonGames - 1;
            foreach (var s in ps.Series)
                sim.PostNews(gi, $"10/{Math.Min(31, 6 + ps.Series.IndexOf(s) * 6):00}/{c.Year}", GMNewsKind.Postseason, $"{s.Round}: {ShortTeam(s.WinnerCode)} 진출",
                    $"{Team(s.HigherCode)} {s.HigherWins}승 - {s.LowerWins}승 {Team(s.LowerCode)}. {Team(s.WinnerCode)}이(가) 시리즈를 가져갔습니다.",
                    s.HigherCode == me || s.LowerCode == me, false);
            sim.PostNews(gi, $"10/31/{c.Year}", GMNewsKind.Postseason, $"{c.Year} 한국시리즈 우승: {Team(ps.ChampionCode)}",
                $"{Team(ps.ChampionCode)}이(가) {Team(ps.RunnerUpCode)}를 {(ks.WinnerCode == ks.HigherCode ? ks.HigherWins : ks.LowerWins)}승 {(ks.WinnerCode == ks.HigherCode ? ks.LowerWins : ks.HigherWins)}패로 꺾고 우승했습니다. 한국시리즈 MVP {ps.KoreanSeriesMvp}.",
                ps.ChampionCode == me, true);
        }

        /// <summary>
        /// 정규시즌 상위 5개 구단 포스트시즌: 와일드카드(4위 1승 어드밴티지 · 2선승) → 준플레이오프(3선승) → 플레이오프(3선승) → 한국시리즈(4선승).
        /// 무승부는 재경기. [TASK-GM-07] 남은 경기를 1경기씩(PlayNextPostseasonGame) 모두 진행한다 - 브래킷에서 일부 진행한 상태도 이어서 끝낸다.
        /// </summary>
        public static PostseasonSummaryData RunPostseason(GMLiveSeasonSimulator sim)
        {
            if (BeginPostseason(sim) == null) return null;
            int guard = 0;
            while (guard++ < 60 && PlayNextPostseasonGame(sim) != null) { }
            var ps = Bundle(sim.League).Postseason;
            return ps != null && ps.Completed ? ps : null;
        }

        // ================================================================== 1.2 타이틀 홀더

        private sealed class TitleSpec
        {
            public string Id, Name, Section, Unit;
            public bool Pitching, LowerIsBetter;
            public Func<GMPlayerSeasonStats, double> Value;
            public Func<GMPlayerSeasonStats, int, bool> Qualified; // (stats, 팀 경기 수)
            public Func<GMPlayerSeasonStats, double> Volume;       // 동률 · 대체 표본 기준(타석 · 아웃)
            public Func<double, string> Format;
        }

        /// <summary>규정 타석 = 팀 경기 수 × 3.1, 규정 이닝 = 팀 경기 수 × 1(= 아웃 × 3).</summary>
        public static bool QualifiedBatter(GMPlayerSeasonStats s, int games) => s.PA >= games * 3.1;
        public static bool QualifiedPitcher(GMPlayerSeasonStats s, int games) => s.OutsPitched >= games * 3;
        public const int WinPctMinWins = 10;

        private static readonly TitleSpec[] Titles =
        {
            // 1.2.1 투수 6
            new TitleSpec { Id = "W", Name = "승리상(다승왕)", Section = "1.2.1", Unit = "승", Pitching = true, Value = s => s.W, Volume = s => s.OutsPitched, Format = v => $"{v:0}승" },
            new TitleSpec { Id = "ERA", Name = "평균자책점상", Section = "1.2.1", Unit = "", Pitching = true, LowerIsBetter = true, Value = s => s.ERA, Qualified = QualifiedPitcher, Volume = s => s.OutsPitched,
                Format = v => $"평균자책점 {v.ToString("0.00", Inv)}" },
            new TitleSpec { Id = "SO", Name = "탈삼진상", Section = "1.2.1", Unit = "", Pitching = true, Value = s => s.PSO, Volume = s => s.OutsPitched, Format = v => $"{v:0}탈삼진" },
            new TitleSpec { Id = "WPCT", Name = "승률상", Section = "1.2.1", Unit = "", Pitching = true, Value = s => s.W + s.L == 0 ? 0 : (double)s.W / (s.W + s.L),
                Qualified = (s, g) => s.W >= WinPctMinWins, Volume = s => s.W, Format = v => $"승률 {GMTeamRecord.PctLabel(v)}" },
            new TitleSpec { Id = "SV", Name = "세이브상", Section = "1.2.1", Unit = "", Pitching = true, Value = s => s.SV, Volume = s => s.PG, Format = v => $"{v:0}세이브" },
            new TitleSpec { Id = "HLD", Name = "홀드상", Section = "1.2.1", Unit = "", Pitching = true, Value = s => s.HLD, Volume = s => s.PG, Format = v => $"{v:0}홀드" },
            // 1.2.2 타자 8
            new TitleSpec { Id = "AVG", Name = "타율상(타격왕)", Section = "1.2.2", Value = s => s.AVG, Qualified = QualifiedBatter, Volume = s => s.PA, Format = v => $"타율 {GMLeaderCategories.Format(GMLeaderCategory.AVG, v)}" },
            new TitleSpec { Id = "HR", Name = "홈런상", Section = "1.2.2", Value = s => s.HR, Volume = s => s.PA, Format = v => $"{v:0}홈런" },
            new TitleSpec { Id = "RBI", Name = "타점상", Section = "1.2.2", Value = s => s.RBI, Volume = s => s.PA, Format = v => $"{v:0}타점" },
            new TitleSpec { Id = "H", Name = "안타상", Section = "1.2.2", Value = s => s.H, Volume = s => s.PA, Format = v => $"{v:0}안타" },
            new TitleSpec { Id = "R", Name = "득점상", Section = "1.2.2", Value = s => s.R, Volume = s => s.PA, Format = v => $"{v:0}득점" },
            new TitleSpec { Id = "SB", Name = "도루상", Section = "1.2.2", Value = s => s.SB, Volume = s => s.PA, Format = v => $"{v:0}도루" },
            new TitleSpec { Id = "OBP", Name = "출루율상", Section = "1.2.2", Value = s => s.OBP, Qualified = QualifiedBatter, Volume = s => s.PA, Format = v => $"출루율 {GMLeaderCategories.Format(GMLeaderCategory.OBP, v)}" },
            new TitleSpec { Id = "SLG", Name = "장타율상", Section = "1.2.2", Value = s => s.SLG, Qualified = QualifiedBatter, Volume = s => s.PA, Format = v => $"장타율 {GMLeaderCategories.Format(GMLeaderCategory.SLG, v)}" },
        };

        /// <summary>타이틀 14개 부문 id(TITLE_ 접두어 없이) - 테스트 · UI가 쓴다.</summary>
        public static IReadOnlyList<string> TitleIds => Titles.Select(t => t.Id).ToList();
        public static bool IsPitchingTitle(string id) => Titles.First(t => t.Id == id).Pitching;
        public static bool IsLowerBetterTitle(string id) => Titles.First(t => t.Id == id).LowerIsBetter;
        public static double TitleValue(string id, GMPlayerSeasonStats s) => Titles.First(t => t.Id == id).Value(s);

        /// <summary>부문 후보 풀 - 규정(타석 · 이닝 · 10승)을 채운 선수, 아무도 없으면 표본(타석 · 이닝 · 승) 상위로 대체.</summary>
        public static List<GMPlayerSeasonStats> TitlePool(string id, IEnumerable<GMPlayerSeasonStats> stats, int games)
        {
            var spec = Titles.First(t => t.Id == id);
            var pool = stats.Where(s => spec.Pitching ? s.PG > 0 && s.IsPitcher : s.PA > 0 && !s.IsPitcher).ToList();
            if (spec.Qualified == null) return pool;
            var q = pool.Where(s => spec.Qualified(s, games)).ToList();
            if (q.Count > 0) return q;
            double maxVol = pool.Count == 0 ? 0 : pool.Max(spec.Volume);
            return pool.Where(s => spec.Volume(s) >= maxVol * 0.5).ToList();
        }

        // ================================================================== 11월 KBO 시상식(1.1 · 1.2 · 1.3 · 1.5)

        /// <summary>11월 KBO 시상식 - MVP · 신인상(1.1), 타이틀 홀더 14(1.2), 수비상 10(1.3), 특별상 2(1.5). 포스트시즌을 아직 안 치렀으면 먼저 치른다.</summary>
        public static List<GMAwardWinner> HoldKboAwardsCeremony(GMLiveSeasonSimulator sim)
        {
            var c = ContextOf(sim);
            var bundle = Bundle(c.League);
            if (bundle.KboCeremonyHeld) return bundle.KboCeremony;
            if (!sim.IsSeasonComplete) return null;
            if (!bundle.Postseason.Completed) RunPostseason(sim);
            var list = new List<GMAwardWinner>();
            var stats = c.League.Stats.Values.Where(s => c.P(s.PlayerId) != null).ToList();
            int games = Math.Max(1, c.League.GamesPlayed);

            // 1.2 타이틀 홀더 14
            var titleWins = new Dictionary<string, int>();
            foreach (var spec in Titles)
            {
                var pool = TitlePool(spec.Id, stats, games);
                if (pool.Count == 0) pool = stats.Where(s => s.IsPitcher == spec.Pitching).ToList();
                var ordered = spec.LowerIsBetter
                    ? pool.OrderBy(spec.Value).ThenByDescending(spec.Volume).ThenBy(s => s.PlayerId)
                    : pool.OrderByDescending(spec.Value).ThenByDescending(spec.Volume).ThenBy(s => s.PlayerId);
                var top = ordered.FirstOrDefault();
                if (top == null) continue;
                var p = c.P(top.PlayerId);
                double v = spec.Value(top);
                list.Add(Award(c, "TITLE_" + spec.Id, spec.Name, GMAwardCategory.TitleHolder, spec.Section, p, top.TeamCode, PositionOf(c, top), v, spec.Format(v),
                    $"TITLE_HOLDER_{c.Year}_{spec.Id}"));
                titleWins.TryGetValue(top.PlayerId, out int n);
                titleWins[top.PlayerId] = n + 1;
            }

            // 1.1.1 MVP - 기자단 투표(WAR · 타이틀 · 팀 성적 · 주요 기록)
            var rankOf = c.Sim.Standings().Select((r, i) => (r.TeamCode, i)).ToDictionary(x => x.TeamCode, x => x.i);
            double TeamBonus(string code) => rankOf.TryGetValue(code, out int r) ? (r == 0 ? 6 : r <= 2 ? 4 : r <= 4 ? 2 : 0) : 0;
            double MvpScore(GMPlayerSeasonStats s) => s.WAR * 10 + (titleWins.TryGetValue(s.PlayerId, out int t) ? t * 4 : 0) + TeamBonus(s.TeamCode)
                                                     + (s.IsPitcher ? s.W * 0.3 + s.SV * 0.1 : s.HR * 0.2 + s.RBI * 0.03);
            var mvpPool = stats.Where(s => s.IsPitcher ? s.OutsPitched >= games * 1.5 || s.SV >= 20 : s.PA >= games * 2).ToList();
            if (mvpPool.Count == 0) mvpPool = stats;
            var mvpOrder = mvpPool.Select(s => (s, score: MvpScore(s))).OrderByDescending(x => x.score).ThenBy(x => x.s.PlayerId).ToList();
            if (mvpOrder.Count > 0)
            {
                var s = mvpOrder[0].s;
                double share = VoteShare(mvpOrder.Select(x => x.score).ToList());
                list.Insert(0, Award(c, "MVP", "KBO MVP", GMAwardCategory.KboCeremony, "1.1.1", c.P(s.PlayerId), s.TeamCode, PositionOf(c, s), share,
                    $"득표율 {share.ToString("0.0", Inv)}% · WAR {s.WAR.ToString("0.00", Inv)}", $"MVP_{c.Year}", MainLine(s)));
            }

            // 1.1.2 신인상 - 만 23세 이하 최고 WAR
            var rookies = stats.Where(s => (c.P(s.PlayerId)?.Age ?? 99) <= RookieMaxAge && (s.PA >= 100 || s.OutsPitched >= 90)).ToList();
            if (rookies.Count == 0) rookies = stats.Where(s => (c.P(s.PlayerId)?.Age ?? 99) <= RookieMaxAge && (s.PA > 0 || s.OutsPitched > 0)).ToList();
            if (rookies.Count == 0) rookies = stats.Where(s => s.PA > 0 || s.OutsPitched > 0).OrderBy(s => c.P(s.PlayerId).Age).Take(20).ToList();
            var rookieOrder = rookies.OrderByDescending(s => s.WAR).ThenBy(s => s.PlayerId).ToList();
            if (rookieOrder.Count > 0)
            {
                var s = rookieOrder[0];
                var p = c.P(s.PlayerId);
                double share = VoteShare(rookieOrder.Select(x => (double)x.WAR * 10).ToList());
                list.Insert(Math.Min(1, list.Count), Award(c, "ROOKIE", "KBO 신인상", GMAwardCategory.KboCeremony, "1.1.2", p, s.TeamCode, PositionOf(c, s), share,
                    $"득표율 {share.ToString("0.0", Inv)}% · {p.Age}세 · WAR {s.WAR.ToString("0.00", Inv)}", $"ROOKIE_{c.Year}", MainLine(s)));
            }

            // 1.3 수비상 10 - 수비 지표 75% + 감독 · 코치 · 기자단 투표 25%
            var defenseWinners = new HashSet<string>();
            foreach (int slot in new[] { 9, 0, 1, 2, 3, 4, 5, 6, 7 })
            {
                var cand = DefenseCandidates(stats, slot);
                if (cand.Count == 0) continue;
                var metric = Normalize(cand.Select(s => (s.PlayerId, (double)s.DefensiveScore)));
                var vote = Normalize(cand.Select(s => (s.PlayerId, s.DefRating * 0.6 + c.League.RecordOf(s.TeamCode).Pct * 40 + s.Positions[slot] * 0.1)));
                var best = cand.Select(s => (s, score: metric[s.PlayerId] * 75 + vote[s.PlayerId] * 25)).OrderByDescending(x => x.score).ThenBy(x => x.s.PlayerId).First();
                defenseWinners.Add(best.s.PlayerId);
                list.Add(Award(c, "DEF_" + SlotCodes[slot], $"수비상 {SlotName(slot)}", GMAwardCategory.Defense, "1.3.1", c.P(best.s.PlayerId), best.s.TeamCode, SlotLabel(slot),
                    best.score, DefenseLine(best.s, slot), $"DEFENSE_{c.Year}_{SlotCodes[slot]}", $"합산 {best.score.ToString("0.0", Inv)}점"));
            }
            // 1.3.2 유틸리티 - 2개 포지션 이상(각 3경기+) 소화 중 최고 수비 기여
            var multi = stats.Where(s => !s.IsPitcher && !defenseWinners.Contains(s.PlayerId) && Enumerable.Range(0, 8).Count(i => s.Positions[i] >= 3) >= 2).ToList();
            var utilPool = multi.Count > 0 ? multi : stats.Where(s => !s.IsPitcher && !defenseWinners.Contains(s.PlayerId) && s.DefG > 0).ToList();
            if (utilPool.Count == 0) utilPool = stats.Where(s => !s.IsPitcher && !defenseWinners.Contains(s.PlayerId)).ToList();
            var util = utilPool.OrderByDescending(s => s.DefensiveScore + 2 * Enumerable.Range(0, 8).Count(i => s.Positions[i] >= 3)).ThenBy(s => s.PlayerId).FirstOrDefault();
            if (util != null)
            {
                int posCount = Enumerable.Range(0, 8).Count(i => util.Positions[i] >= 3);
                string positions = string.Join("·", Enumerable.Range(0, 8).Where(i => util.Positions[i] >= 3).Select(SlotLabel));
                list.Add(Award(c, "DEF_UTIL", "수비상 유틸리티", GMAwardCategory.Defense, "1.3.2", c.P(util.PlayerId), util.TeamCode, PositionOf(c, util), util.DefensiveScore,
                    posCount >= 2 ? $"{positions} {posCount}개 포지션 · 수비 기여 {util.DefensiveScore.ToString("0.0", Inv)}" : $"전천후 수비 기여 {util.DefensiveScore.ToString("0.0", Inv)}",
                    $"DEFENSE_{c.Year}_UTIL"));
            }

            // 1.5.1 페어플레이상 - 더그아웃 리더 · 살림꾼 + 주전 출전
            bool Regular(GMPlayerSeasonStats s) => s.IsPitcher ? s.PG >= 40 || s.OutsPitched >= games * 3 : s.G >= games * 0.7;
            var fairPool = stats.Where(s => Regular(s) && c.P(s.PlayerId) is Player p && (p.RoleArchetype == LockerRoomRole.DugoutLeader || p.RoleArchetype == LockerRoomRole.UnsungHero)).ToList();
            if (fairPool.Count == 0) fairPool = stats.Where(Regular).ToList();
            if (fairPool.Count == 0) fairPool = stats;
            double Fair(GMPlayerSeasonStats s) { var p = c.P(s.PlayerId); return p.PersonalMorale + (p.IsCaptain ? 15 : 0) + (p.RoleArchetype == LockerRoomRole.DugoutLeader ? 8 : 4) + Math.Max(s.G, s.PG) * 0.15 - s.Errors * 1.5; }
            var fair = fairPool.OrderByDescending(Fair).ThenBy(s => s.PlayerId).First();
            var fairP = c.P(fair.PlayerId);
            list.Add(Award(c, "FAIR_PLAY", "KBO 페어플레이상", GMAwardCategory.Special, "1.5.1", fairP, fair.TeamCode, PositionOf(c, fair), Fair(fair),
                $"{RoleLabel(fairP.RoleArchetype)}{(fairP.IsCaptain ? " · 주장" : "")} · {(fair.IsPitcher ? $"{fair.PG}경기 등판" : $"{fair.G}경기 출전")}", $"FAIR_PLAY_{c.Year}"));

            // 1.5.2 상벌위원회 특별상 - 시즌 대기록, 없으면 리그 발전 기여 베테랑
            var records = new List<(GMPlayerSeasonStats s, double weight, string note)>();
            foreach (var s in stats)
            {
                if (s.MaxHitStreak >= 25) records.Add((s, s.MaxHitStreak / 25.0, $"{s.MaxHitStreak}경기 연속 안타"));
                if (s.HR >= 45) records.Add((s, s.HR / 45.0, $"시즌 {s.HR}홈런"));
                if (s.SB >= 60) records.Add((s, s.SB / 60.0, $"시즌 {s.SB}도루"));
                if (s.W >= 20) records.Add((s, s.W / 20.0, $"시즌 {s.W}승"));
                if (s.PSO >= 220) records.Add((s, s.PSO / 220.0, $"시즌 {s.PSO}탈삼진"));
                if (s.SV >= 42) records.Add((s, s.SV / 42.0, $"시즌 {s.SV}세이브"));
                if (s.H >= 200) records.Add((s, s.H / 200.0, $"시즌 {s.H}안타"));
            }
            var pick = records.OrderByDescending(r => r.weight).ThenBy(r => r.s.PlayerId).FirstOrDefault();
            string specialNote;
            GMPlayerSeasonStats special;
            if (pick.s != null) { special = pick.s; specialNote = $"시즌 대기록 - {pick.note}"; }
            else
            {
                var vets = stats.Where(s => (c.P(s.PlayerId)?.Age ?? 0) >= 35 && (s.PA > 0 || s.OutsPitched > 0)).ToList();
                special = (vets.Count > 0 ? vets.OrderByDescending(s => s.WAR) : stats.OrderByDescending(s => c.P(s.PlayerId).Age).ThenByDescending(s => s.WAR)).ThenBy(s => s.PlayerId).First();
                specialNote = $"리그 발전 기여 베테랑 - {c.P(special.PlayerId).Age}세 · WAR {special.WAR.ToString("0.00", Inv)}";
            }
            list.Add(Award(c, "KBO_SPECIAL", "KBO 상벌위원회 특별상", GMAwardCategory.Special, "1.5.2", c.P(special.PlayerId), special.TeamCode, PositionOf(c, special), 0,
                specialNote, $"KBO_SPECIAL_{c.Year}"));

            bundle.KboCeremony = list;
            bundle.KboCeremonyHeld = true;
            if (c.League.Phase < GMSeasonPhase.AwardsCeremony) c.League.Phase = GMSeasonPhase.AwardsCeremony;

            string me = c.League.SelectedTeamCode;
            var mvp = list.FirstOrDefault(a => a.AwardId == "MVP");
            var roy = list.FirstOrDefault(a => a.AwardId == "ROOKIE");
            int gi = GMLiveSeasonSimulator.SeasonGames - 1;
            if (mvp != null)
                sim.PostNews(gi, $"11/24/{c.Year}", GMNewsKind.Award, $"{c.Year} KBO MVP: {mvp.PlayerName}", $"{Team(mvp.TeamCode)} {mvp.PlayerName}이(가) 기자단 투표 {mvp.ValueLabel}로 정규시즌 MVP에 올랐습니다.",
                    mvp.TeamCode == me, true);
            if (roy != null)
                sim.PostNews(gi, $"11/24/{c.Year}", GMNewsKind.Award, $"{c.Year} KBO 신인상: {roy.PlayerName}", $"{Team(roy.TeamCode)} {roy.PlayerName} - {roy.ValueLabel}.", roy.TeamCode == me, false);
            int mine = list.Count(a => a.TeamCode == me && !string.IsNullOrEmpty(a.PlayerId));
            sim.PostNews(gi, $"11/24/{c.Year}", GMNewsKind.Award, "KBO 시상식 · 타이틀 홀더 14 · 수비상 10 · 특별상 2",
                $"{c.Year} KBO 시상식이 열렸습니다. 우리 구단 수상 {mine}건.", mine > 0, false);
            return list;
        }

        private static List<GMPlayerSeasonStats> DefenseCandidates(List<GMPlayerSeasonStats> stats, int slot)
        {
            bool pitcher = slot == GMPlayerSeasonStats.PitcherSlot;
            var pool = stats.Where(s => s.IsPitcher == pitcher).ToList();
            var primary = pool.Where(s => s.PrimarySlot == slot && (pitcher ? s.OutsPitched >= 150 : s.Positions[slot] >= 40)).ToList();
            if (primary.Count > 0) return primary;
            primary = pool.Where(s => s.PrimarySlot == slot && s.Positions[slot] > 0).ToList();
            if (primary.Count > 0) return primary;
            return pool.Where(s => s.Positions[slot] > 0).OrderByDescending(s => s.Positions[slot]).Take(5).ToList();
        }

        private static string DefenseLine(GMPlayerSeasonStats s, int slot) =>
            $"{s.Positions[slot]}경기 · 실책 {s.Errors} · 수비율 {s.FieldingPct.ToString(".000", Inv)} · 호수비 {s.FinePlays}";

        private static string MainLine(GMPlayerSeasonStats s) => s.IsPitcher
            ? $"{s.W}승 {s.L}패 {s.SV}세이브 · 평균자책점 {s.ERA.ToString("0.00", Inv)} · {s.PSO}탈삼진"
            : $"타율 {GMLeaderCategories.Format(GMLeaderCategory.AVG, s.AVG)} · {s.HR}홈런 · {s.RBI}타점 · {s.SB}도루";

        public static string RoleLabel(LockerRoomRole role)
        {
            switch (role)
            {
                case LockerRoomRole.AlphaDog: return "알파독";
                case LockerRoomRole.Ambitious: return "야망가";
                case LockerRoomRole.DugoutLeader: return "더그아웃 리더";
                case LockerRoomRole.Prospect: return "유망주";
                default: return "살림꾼";
            }
        }

        // ================================================================== 12월 골든글러브(1.4)

        /// <summary>12월 골든글러브(10부문) - 투 · 포 · 1 · 2 · 3 · 유 · 외야 3(좌중우 구분 없음) · 지명타자. 공 · 수 · 주 종합 + 득표율. 끝나면 단장 역학을 반영한다.</summary>
        public static List<GMAwardWinner> HoldGoldenGloveCeremony(GMLiveSeasonSimulator sim)
        {
            var c = ContextOf(sim);
            var bundle = Bundle(c.League);
            if (bundle.GoldenGloveHeld) return bundle.GoldenGlove;
            if (!sim.IsSeasonComplete) return null;
            if (!bundle.KboCeremonyHeld) HoldKboAwardsCeremony(sim);
            var stats = c.League.Stats.Values.Where(s => c.P(s.PlayerId) != null).ToList();
            int games = Math.Max(1, c.League.GamesPlayed);
            var titleCount = bundle.KboCeremony.Where(a => a.Category == GMAwardCategory.TitleHolder).GroupBy(a => a.PlayerId).ToDictionary(g => g.Key, g => g.Count());
            double Score(GMPlayerSeasonStats s)
            {
                titleCount.TryGetValue(s.PlayerId, out int t);
                return s.IsPitcher
                    ? s.PitcherWAR * 10 + s.W * 0.4 + s.SV * 0.15 + s.HLD * 0.1 + t * 2
                    : s.BatterWAR * 10 + s.DefensiveScore * 0.15 + s.HR * 0.15 + s.AVG * 20 + s.SB * 0.05 + t * 2;
            }
            var list = new List<GMAwardWinner>();
            var used = new HashSet<string>();

            void Give(string code, string name, List<GMPlayerSeasonStats> cand, int take, string posLabel)
            {
                var ordered = cand.Where(s => !used.Contains(s.PlayerId)).Select(s => (s, score: Score(s))).OrderByDescending(x => x.score).ThenBy(x => x.s.PlayerId).ToList();
                for (int k = 0; k < take && k < ordered.Count; k++)
                {
                    var s = ordered[k].s;
                    used.Add(s.PlayerId);
                    var rest = ordered.Skip(k).Select(x => x.score).ToList();
                    double share = VoteShare(rest);
                    list.Add(Award(c, take > 1 ? $"GG_{code}{k + 1}" : "GG_" + code, name, GMAwardCategory.GoldenGlove, "1.4.1", c.P(s.PlayerId), s.TeamCode,
                        posLabel ?? PositionOf(c, s), share, $"득표율 {share.ToString("0.0", Inv)}% · {MainLine(s)}", $"GOLDEN_GLOVE_{c.Year}_{code}"));
                }
            }

            List<GMPlayerSeasonStats> AtSlots(params int[] slots)
            {
                bool pitcher = slots.Contains(GMPlayerSeasonStats.PitcherSlot);
                var pool = stats.Where(s => s.IsPitcher == pitcher).ToList();
                if (pitcher)
                {
                    var q = pool.Where(s => s.OutsPitched >= games * 3 || s.W >= 10 || s.SV >= 30 || s.HLD >= 30).ToList();
                    return q.Count > 0 ? q : pool.Where(s => s.OutsPitched > 0).ToList();
                }
                foreach (int min in new[] { games / 2, 20, 1 })
                {
                    var q = pool.Where(s => slots.Contains(s.PrimarySlot) && slots.Sum(i => s.Positions[i]) >= min).ToList();
                    if (q.Count >= (slots.Length > 1 ? 3 : 1)) return q;
                }
                return pool.Where(s => slots.Sum(i => s.Positions[i]) > 0).ToList();
            }

            Give("P", "골든글러브 투수", AtSlots(9), 1, "투");
            Give("C", "골든글러브 포수", AtSlots(0), 1, "포");
            Give("1B", "골든글러브 1루수", AtSlots(1), 1, "1");
            Give("2B", "골든글러브 2루수", AtSlots(2), 1, "2");
            Give("3B", "골든글러브 3루수", AtSlots(3), 1, "3");
            Give("SS", "골든글러브 유격수", AtSlots(4), 1, "유");
            Give("OF", "골든글러브 외야수", AtSlots(5, 6, 7), 3, null);
            var dh = AtSlots(8);
            if (dh.Count(s => !used.Contains(s.PlayerId)) == 0) dh = stats.Where(s => !s.IsPitcher && s.PA > 0).ToList();
            Give("DH", "골든글러브 지명타자", dh, 1, "지");
            // 외야 3명 보장(후보가 모자라면 남은 타자로 채움)
            int of = list.Count(a => a.AwardId.StartsWith("GG_OF", StringComparison.Ordinal));
            if (of < 3) Give("OF", "골든글러브 외야수", stats.Where(s => !s.IsPitcher && s.PA > 0).ToList(), 3 - of, null);
            for (int k = 0, n = 0; k < list.Count; k++)
                if (list[k].AwardId.StartsWith("GG_OF", StringComparison.Ordinal)) list[k].AwardId = $"GG_OF{++n}";

            bundle.GoldenGlove = list;
            bundle.GoldenGloveHeld = true;
            ApplyAwardDynamics(c.League);

            string me = c.League.SelectedTeamCode;
            int mine = list.Count(a => a.TeamCode == me);
            sim.PostNews(GMLiveSeasonSimulator.SeasonGames - 1, $"12/09/{c.Year}", GMNewsKind.Award, $"{c.Year} 골든글러브 10인 발표",
                string.Join(", ", list.Select(a => $"{a.Position} {a.PlayerName}({ShortTeam(a.TeamCode)})")) + $". 우리 구단 {mine}명 수상.", mine > 0, true);
            return list;
        }

        // ================================================================== 단장 역학 · 연도 전환

        /// <summary>
        /// MVP · 골든글러브 · 타이틀 홀더 수상자: Ego +1(최대 5), 요구 연봉 15% + 추가 주요 수상 1건당 5%(최대 30%) 인상(최대 20억).
        /// 한 시즌에 한 번만 반영된다(DynamicsApplied).
        /// </summary>
        public static List<GMAwardDynamicsEntry> ApplyAwardDynamics(GMLeagueState league)
        {
            var bundle = Bundle(league);
            if (bundle.DynamicsApplied) return bundle.Dynamics;
            var major = bundle.All().Where(a => !string.IsNullOrEmpty(a.PlayerId) &&
                                                (a.AwardId == "MVP" || a.Category == GMAwardCategory.TitleHolder || a.Category == GMAwardCategory.GoldenGlove))
                .GroupBy(a => a.PlayerId).ToList();
            var players = league.AllPlayers.Where(p => p.InstanceId != null).GroupBy(p => p.InstanceId).ToDictionary(g => g.Key, g => g.First());
            foreach (var g in major)
            {
                if (!players.TryGetValue(g.Key, out var p)) continue;
                int count = g.Count();
                int pct = RaisePercentFor(count);
                var e = new GMAwardDynamicsEntry
                {
                    PlayerId = p.InstanceId, PlayerName = p.Template?.PlayerName, TeamCode = league.TeamCodeOf(p),
                    EgoBefore = p.EgoLevel, SalaryBefore = p.Salary, RaisePercent = pct,
                    Reason = string.Join(" · ", g.Select(a => a.AwardName).Distinct()),
                };
                p.EgoLevel = Math.Min(5, p.EgoLevel + 1);
                p.Salary = (int)Math.Min(Player.MaxSalary, Math.Round(p.Salary * (100 + pct) / 100.0));
                e.EgoAfter = p.EgoLevel;
                e.SalaryAfter = p.Salary;
                bundle.Dynamics.Add(e);
            }
            bundle.DynamicsApplied = true;
            return bundle.Dynamics;
        }

        public static int RaisePercentFor(int majorAwardCount) => Math.Min(MaxRaisePercent, MinRaisePercent + Math.Max(0, majorAwardCount - 1) * 5);

        /// <summary>포스트시즌 → 11월 KBO 시상식 → 12월 골든글러브 중 남은 단계를 순서대로 모두 치른다. 시즌이 안 끝났으면 false.</summary>
        public static bool CompleteSeasonEvents(GMLiveSeasonSimulator sim)
        {
            if (sim == null || !sim.IsSeasonComplete) return false;
            RunPostseason(sim);
            HoldKboAwardsCeremony(sim);
            HoldGoldenGloveCeremony(sim);
            return Bundle(sim.League).IsComplete;
        }

        /// <summary>
        /// [2027년 새 시즌 진입] - 남은 포스트시즌 · 시상식을 마저 치른 뒤 연도 +1, 전원 나이 +1 · 잔여 계약 -1 · 부상 초기화 · 시즌 기록 리셋
        /// (수상 이력 · 성향 · Ego · 연봉은 보존) → 0/144 준비(스토브리그). 스토리 캠페인 최하위 연속 기록 · 구단주 압박도 갱신한다.
        /// 반환 = 연도가 넘어갔는지. 호출 측은 새 GMLiveSeasonSimulator를 만들어 붙인다.
        /// </summary>
        public static bool AdvanceToNextSeasonYear(GMLiveSeasonSimulator sim)
        {
            if (sim?.League != null && GMCareer.Ensure(sim.League).Ended) return false; // [TASK-GM-18] 커리어 엔딩 이후 연도 전환 없음(뉴게임+)
            if (!CompleteSeasonEvents(sim)) return false;
            var league = sim.League;
            var user = league.UserTeam;
            var bundle = Bundle(league);
            if (user != null)
            {
                var standings = sim.Standings();
                bool last = standings.Count > 0 && standings[standings.Count - 1].TeamCode == user.TeamCode;
                user.ConsecutiveLastPlaceSeasons = last ? user.ConsecutiveLastPlaceSeasons + 1 : 0;
                if (bundle.Postseason.SeedCodes.Contains(user.TeamCode)) user.OwnerPostseasonPressure = false;
            }
            int oldYear = league.SeasonYear;
            // [TASK-GM-06] 프런트 오피스 결산 - 10구단 시즌 이력 · 관중, 구단주 목표 · 신임도, 스토리 캠페인 4종 엔딩
            GMFrontOffice.OnSeasonCompleted(league, sim.Standings().Select(r => r.TeamCode).ToList(), bundle.Postseason.ChampionCode);
            if (GMCareer.OnSeasonCompleted(league, oldYear)) return false; // [TASK-GM-18] 30시즌 은퇴 · 임기 말 재계약 실패(해임) = 커리어 엔딩
            GMFuturesMeeting.ApplySeasonGrowth(league, oldYear); // [TASK-GM-15] 육성 회의실 - 훈련 방향 · 멘토링 반영 퓨처스 연간 성장(나이 +1 전)
            GMAgingCurve.Apply(league, oldYear);                 // [TASK-GM-16] 에이징 커브 - 30~33세 동결/-1 · 34세 이상 -1~-3(레전드 · 유대 완화)
            // [TASK-GM-09] 11월 프리미어 12(해당 연도) → 연도 전환 → 계약 만료자 FA 공시
            GMGlobalTournamentManager.Trigger(league, GMTournamentWindow.PostSeason, league.Seed);
            league.Phase = GMSeasonPhase.AwardsCeremony;
            if (!league.AdvancePhase()) return false;
            GMPromiseSystem.RecoverPenalties(league, oldYear); // [TASK-GM-15] 약속 위반 페널티 기간제 회복(과거 위반 1건당 신뢰도 +5 · 충성도 +10, 원래 페널티까지)
            // [TASK-GM-10] 내 구단 만료자 = 원 소속 우선 협상 명단 → AI 구단 만료자 FA 공시 → 11/25 보류명단(AI 방출)
            GMReserveList.OpenPriorityNegotiation(league);
            GMFreeAgencyCycle.DeclareExpiredContracts(league, oldYear, skipUserTeam: true); // 잔여 계약 -1 직후 0년 = 이번 시즌으로 계약이 끝난 선수 FA 공시
            GMReserveList.SubmitAiReserveLists(league);
            GMFrontOffice.OnNewSeason(league); // [TASK-GM-06] 연간 FA · 트레이드 · 드래프트 카운터 초기화 · 목표 재생성 · 안건 재오픈
            var champion = bundle.Postseason.ChampionCode;
            league.AddNews(new GMNewsItem
            {
                GameIndex = 0, DateLabel = $"01/02/{league.SeasonYear}", Kind = GMNewsKind.Season, Title = $"{league.SeasonYear} 시즌 준비 시작",
                Body = $"{oldYear} 시즌({Team(champion)} 우승)이 막을 내렸습니다. 선수단 나이 +1 · 계약 -1, 수상자 Ego · 연봉 상승을 확인하고 스토브리그를 준비하십시오.",
                IsUserTeam = true, IsMajor = true,
            });
            return true;
        }
    }
}
