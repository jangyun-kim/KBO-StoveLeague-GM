using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using KBOManager.Controllers;
using KBOManager.Data;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.UI;
using UnityEditor;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>[TASK-KBO-188] 완료 보고용 검증 리포트(Logs/Task188Report.txt).</summary>
    public static class Task188Report
    {
        private static readonly List<UnityEngine.Object> Temp = new List<UnityEngine.Object>();

        [MenuItem("KBO Manager/Debug/TASK-188 Report")]
        public static void RunMenu() => Debug.Log(Build());

        public static void RunBatch()
        {
            string report = Build();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/Task188Report.txt", report, Encoding.UTF8);
            Debug.Log(report);
        }

        public static string Build()
        {
            var sb = new StringBuilder();
            var previous = LineupAssignment.Active;
            try
            {
                LineupAssignment.Active = null;
                BuildAwaken(sb);
                BuildSeasonStats(sb);
                BuildPortraits(sb);
                BuildLayout(sb);
                BuildQuickSeries(sb, 10);
            }
            catch (Exception e)
            {
                sb.AppendLine("[리포트 오류] " + e);
            }
            finally
            {
                LineupAssignment.Active = previous;
                foreach (var o in Temp) if (o != null) UnityEngine.Object.DestroyImmediate(o);
                Temp.Clear();
                Task183Report.CleanupTemp();
                PortraitResolver.ResetCache();
            }
            return sb.ToString();
        }

        private static PlayerTemplate T(Grade grade, string person, int year, string templateId = null)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            Temp.Add(t);
            t.TemplateId = templateId ?? $"{person}_{grade}_{year}_{Guid.NewGuid():N}";
            t.RealPlayerId = person;
            t.PlayerName = person;
            t.SeasonYear = year;
            t.Team = Team.Samsung;
            t.Grade = grade;
            t.BatterPosition = BatterPosition.RightField;
            t.BatterStats = StatProfiles.SpreadBatter(80, BatterPosition.RightField, 7);
            return t;
        }

        private static Player P(Grade grade, string person, int year, int reinforce = 0, int awaken = 0) =>
            new Player(Guid.NewGuid().ToString(), T(grade, person, year)) { ReinforceLevel = reinforce, AwakenLevel = awaken };

        private static void BuildAwaken(StringBuilder sb)
        {
            sb.AppendLine("=== [1] 각성 재료 규칙(같은 시즌 · 같은 선수 +3각 / 같은 시즌 · 다른 선수 +1각) · 각성 표기 전환 ===");
            foreach (var grade in new[] { Grade.LIVE_NORMAL, Grade.ALLSTAR, Grade.TITLE_HOLDER, Grade.GOLDEN_GLOVE, Grade.SIGNATURE })
            {
                var target = P(grade, "구자욱", 2024);
                var same = P(grade, "구자욱", 2021);
                var other = P(grade, "김지찬", 2024);
                var otherGrade = P(grade == Grade.SIGNATURE ? Grade.GOLDEN_GLOVE : Grade.SIGNATURE, "구자욱", 2024);
                sb.AppendLine($"{CardGrowthRules.DisplayName(grade),-6} 같은 선수(다른 연도) +{CardGrowthRules.AwakenGainFor(target, same)}각 {CardGrowthRules.AwakenMaterialBadge(target, same)} | " +
                              $"다른 선수 +{CardGrowthRules.AwakenGainFor(target, other)}각 {CardGrowthRules.AwakenMaterialBadge(target, other)} | 다른 시즌 등급 +{CardGrowthRules.AwakenGainFor(target, otherGrade)}각");
            }
            var gg = P(Grade.GOLDEN_GLOVE, "구자욱", 2024, reinforce: 10);
            var steps = new List<string> { $"0각/10강 → 배지 \"{CardGrowthRules.GrowthBadgeLabel(gg)}\"" };
            UpgradeManager.ApplyAwaken(gg, new List<Player> { P(Grade.GOLDEN_GLOVE, "김지찬", 2023) });
            steps.Add($"다른 선수 1장 → {gg.AwakenLevel}각 배지 \"{CardGrowthRules.GrowthBadgeLabel(gg)}\"");
            UpgradeManager.ApplyAwaken(gg, new List<Player> { P(Grade.GOLDEN_GLOVE, "구자욱", 2021) });
            steps.Add($"같은 선수 1장 → {gg.AwakenLevel}각 배지 \"{CardGrowthRules.GrowthBadgeLabel(gg)}\"");
            gg.AwakenLevel = 10;
            steps.Add($"초월 → 배지 \"{CardGrowthRules.GrowthBadgeLabel(gg)}\"(퍼플)");
            sb.AppendLine("골든글러브 10강 카드: " + string.Join(" / ", steps));

            var target2 = P(Grade.GOLDEN_GLOVE, "구자욱", 2024);
            var lineup = P(Grade.GOLDEN_GLOVE, "강민호", 2024);
            var inventory = new List<Player> { target2, P(Grade.GOLDEN_GLOVE, "구자욱", 2023), P(Grade.GOLDEN_GLOVE, "김영웅", 2024), lineup, P(Grade.ALLSTAR, "구자욱", 2024) };
            var candidates = GrowthCenterRules.MaterialCandidates(GrowthTab.Awaken, target2, inventory, new[] { lineup, target2 });
            sb.AppendLine("성장 센터 [각성] 재료 후보(라인업 강민호 · 다른 등급 제외): " +
                          string.Join(", ", candidates.Select(c => $"{CardDisplay.TargetLine1(c)} {CardGrowthRules.AwakenMaterialBadge(target2, c)}")));
            sb.AppendLine();
        }

        private static void BuildSeasonStats(StringBuilder sb)
        {
            sb.AppendLine("=== [2] 중계 화면 시즌 누적 성적(시즌 기록 + 오늘 진행분 실시간 합산) ===");
            var season = new BatterSeasonStats { AtBats = 280, Hits = 88, HomeRuns = 14, Walks = 30, RunsBattedIn = 52 };
            var today = new CompyaGameTracker.BatLine();
            sb.AppendLine($"경기 전 AT BAT: {CompyaGameTracker.CombineBatting(season, today).Summary}");
            today.AtBats = 2; today.Hits = 2; today.HomeRuns = 1; today.RunsBattedIn = 3;
            sb.AppendLine($"오늘 2타수 2안타 1홈런 3타점 후: {CompyaGameTracker.CombineBatting(season, today).Summary} (타순표 시즌 타율 {CompyaGameTracker.FormatAverage(CompyaGameTracker.CombineBatting(season, today).Average)})");
            var pitch = new PitcherSeasonStats { OutsRecorded = 300, EarnedRuns = 35, Wins = 7, Losses = 3 };
            sb.AppendLine($"ON THE MOUND 경기 전: {CompyaGameTracker.CombinePitching(pitch, new CompyaGameTracker.PitchLine()).Summary} → " +
                          $"오늘 6이닝 1실점 후: {CompyaGameTracker.CombinePitching(pitch, new CompyaGameTracker.PitchLine { Outs = 18, Runs = 1 }).Summary}");
            sb.AppendLine();
        }

        private static void BuildPortraits(StringBuilder sb)
        {
            sb.AppendLine("=== [3-1] 구자욱(PLY_004038) 카드 초상화 해석(정확 → 같은 등급 가까운 연도 → 같은 선수 아무 등급) ===");
            PortraitResolver.ResetCache();
            string csv = "Assets/Resources/Data/cards_SAMSUNG.csv";
            if (!File.Exists(csv)) { sb.AppendLine("cards_SAMSUNG.csv 없음"); sb.AppendLine(); return; }
            int shown = 0, resolved = 0;
            foreach (var line in File.ReadAllLines(csv).Where(l => l.Contains(",PLY_004038,")))
            {
                var cols = line.Split(',');
                if (cols.Length < 4 || !Enum.TryParse(cols[3], out Grade grade)) continue;
                int.TryParse(cols[cols.Length - 1], out int year);
                var t = T(grade, "PLY_004038", year, cols[0]);
                string path = PortraitResolver.Resolve(t);
                bool exact = path == PortraitResolver.ExactPath(t.TemplateId);
                bool loaded = path != null && (Resources.Load<Sprite>(path) != null || Resources.LoadAll<Sprite>(path).Length > 0);
                if (loaded) resolved++;
                sb.AppendLine($"{cols[0],-30} {CardGrowthRules.DisplayName(grade),-6} → {(path ?? "없음")}{(exact ? " (카드 고유)" : " (같은 선수 폴백)")} 스프라이트 {(loaded ? "OK" : "없음")}");
                shown++;
            }
            sb.AppendLine($"구자욱 카드 {shown}장 중 초상화 표시 {resolved}장");
            sb.AppendLine();
        }

        private static void BuildLayout(StringBuilder sb)
        {
            sb.AppendLine("=== [3-2] 카드 3단 · 성장 센터 대상 2줄 · 중계 타순표 열 분리 ===");
            sb.AppendLine($"PlayerCardUI: 헤더 y {PlayerCardUI.HeaderBottom:0.00}~1.00(좌 OVR / 우 구단 로고 · SD) | 일러스트 y {PlayerCardUI.NamePlateTop:0.00}~{PlayerCardUI.HeaderBottom:0.00}(하단 다크 비네팅) | " +
                          $"네임플레이트 y 0~{PlayerCardUI.NamePlateTop:0.00} #{ColorUtility.ToHtmlStringRGB(PlayerCardUI.NamePlateColor)} \"[배지] 포지션 이름'연도\"");
            var p = P(Grade.GOLDEN_GLOVE, "구자욱", 2024, reinforce: 10, awaken: 3);
            sb.AppendLine($"네임플레이트 예: [{CardGrowthRules.GrowthBadgeLabel(p)}] {CardDisplay.NamePlate(p)}");
            sb.AppendLine($"[대상 선수 변경] 행: 1줄 \"{CardDisplay.TargetLine1(p)}\"(24pt Bold) / 2줄 \"{CardDisplay.TargetLine2(p, p.CalculateNeutralOVR())}\"");
            sb.AppendLine($"타순표 열(px): 뱃지 {CompyaMatchView.LineupBadgeX0}~{CompyaMatchView.LineupBadgeX1} | 포지션 {CompyaMatchView.LineupPosX0}~{CompyaMatchView.LineupPosX1} | " +
                          $"이름'연도 {CompyaMatchView.LineupNameX0}~{CompyaMatchView.LineupNameX1} | 시즌 타율 {CompyaMatchView.LineupAvgX0}~{CompyaMatchView.LineupAvgX1} | OVR {CompyaMatchView.LineupOvrX0}~{CompyaMatchView.LineupOvrX1}");
            sb.AppendLine();
        }

        private static void SetInstance(Type type, object value) =>
            type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetSetMethod(true)?.Invoke(null, new[] { value });

        /// <summary>[4] 빠른 진행 N경기 실측 - 실제 PlayBallController.StartMatch → FinishMatch 경로(중계 없음 = 스킵과 동일)를 QuickSeriesRunner로 연속 실행.</summary>
        private static void BuildQuickSeries(StringBuilder sb, int games)
        {
            sb.AppendLine($"=== [4] 경기 진행 방식 - 빠른 진행 {games}경기 연속 실측 · 하이라이트(구 풀 플레이) · 풀 플레이(매 타석 지휘) ===");
            var types = new[] { typeof(GameManager), typeof(LeagueManager), typeof(SeasonStatManager) };
            var saved = types.Select(t => t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)).ToArray();
            var go = new GameObject("Task188ReportManagers") { hideFlags = HideFlags.HideAndDontSave };
            Temp.Add(go);
            try
            {
                var gm = go.AddComponent<GameManager>();
                SetInstance(typeof(GameManager), gm);
                gm.FavoriteTeam = Team.Samsung;
                foreach (var p in Task183Report.ProceduralTeam(Team.Samsung, 70, "R188"))
                {
                    gm.AddPlayerToInventory(p);
                    gm.AddPlayerToRoster(p);
                }
                var league = go.AddComponent<LeagueManager>();
                SetInstance(typeof(LeagueManager), league);
                league.InitializeLeague(Team.Samsung, LeagueTier.Amateur);
                var stats = go.AddComponent<SeasonStatManager>();
                SetInstance(typeof(SeasonStatManager), stats);
                var reward = go.AddComponent<MatchRewardManager>();
                var playBall = go.AddComponent<PlayBallController>();

                Player starter = null;
                int ticketsBefore = gm.LiveNormalTicket;
                playBall.OnMatchCompleted += stats.RecordMatchCompleted;
                playBall.OnMatchCompleted += r =>
                {
                    var granted = reward.GrantRewardForMatch(r);
                    var others = league.LastRoundOtherFixtures;
                    string result = r.WinnerTeamName == null ? "무" : r.WinnerTeamName == Team.Samsung.ToString() ? "승" : "패";
                    sb.AppendLine($"  {league.PlayedGameCount,3}경기 {r.AwayTeamName} {r.AwayTotalScore}:{r.HomeTotalScore} {r.HomeTeamName} → {result} | 우리 선발 {starter?.Template.PlayerName} " +
                                  $"| 타 구장 {others.Count}경기({string.Join(", ", others.Select(f => $"{f.AwayTeam} {f.Result.AwayTotalScore}:{f.Result.HomeTotalScore} {f.HomeTeam}"))}) | 보상 +{granted?.LiveNormalTicketGained ?? 0}");
                };
                var runner = new QuickSeriesRunner(
                    () => { starter = league.GetNextStartingPitcher(Team.Samsung); playBall.StartMatch(); },
                    () => league.PeekNextFixture() != null,
                    null,
                    () => gm.LiveNormalTicket,
                    () => gm.GameGold);
                playBall.OnMatchCompleted += r => runner.HandleMatchCompleted(r, Team.Samsung);
                runner.Begin(games, MatchModeRules.RemainingRegularGames(league.PlayedGameCount, LeagueManager.TotalUserGames, true, true));

                var summary = runner.Summary;
                var user = league.GetStandings().First(t => t.Team == Team.Samsung);
                sb.AppendLine($"결과 요약: {summary.Label} (순위표 {user.Wins}승 {user.Draws}무 {user.Losses}패, 보상 합계 {gm.LiveNormalTicket - ticketsBefore})");
                sb.AppendLine($"10개 구단 소화 경기 수: {string.Join(", ", league.GetStandings().Select(t => $"{t.Team} {t.Wins + t.Draws + t.Losses}"))}");
                var rotation = StartingRotation.RotationOf(gm.Roster);
                sb.AppendLine("우리 1~5선발 시즌 기록: " + string.Join(" | ", rotation.Select(p =>
                {
                    var s = stats.GetPitcherStats(p);
                    return s == null ? $"{p.Template.PlayerName} -" : $"{p.Template.PlayerName} {s.Wins}승 {s.Losses}패 {CompyaGameTracker.FormatInnings(s.OutsRecorded)}이닝 ERA {s.EarnedRunAverage:0.00}";
                })));
                var topBat = gm.Roster.Where(p => !p.Template.IsPitcher).Select(p => (p, s: stats.GetBatterStats(p))).Where(x => x.s != null)
                    .OrderByDescending(x => x.s.PlateAppearances).Take(3);
                sb.AppendLine("우리 타자 시즌 기록(타석 상위 3): " + string.Join(" | ", topBat.Select(x =>
                    $"{x.p.Template.PlayerName} {x.s.AtBats}타수 {x.s.Hits}안타 {CompyaGameTracker.FormatAverage(x.s.BattingAverage)} {x.s.HomeRuns}홈런 {x.s.RunsBattedIn}타점")));
                sb.AppendLine($"남은 경기 클램프: 140경기 진행 후 10경기 요청 → {MatchModeRules.ClampQuickCount(10, MatchModeRules.RemainingRegularGames(140, LeagueManager.TotalUserGames, true, true))}경기");
            }
            finally
            {
                for (int i = 0; i < types.Length; i++) SetInstance(types[i], saved[i] is UnityEngine.Object o && o != null ? saved[i] : null);
            }
            sb.AppendLine("하이라이트 개입 규칙(= 구 풀 플레이): 득점권(공격·수비) 승부처, 최대 " + MatchModeRules.HighlightMaxInterventions + "회 · 템포 = 구 풀 플레이 0.8초×0.67");
            sb.AppendLine("풀 플레이 개입 규칙: 우리 팀 매 타석(주자 무관) + 주자 출루 수비 타석, 횟수 제한 없음 · 승부처 안내 없이 작전 패널 직행 · [하이라이트 전환]/[▶▶ 스킵] 토글");
            sb.AppendLine();
        }
    }
}
