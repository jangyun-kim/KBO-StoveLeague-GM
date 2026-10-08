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
    /// [TASK-GM-11] 시즌 결산실(스토브리그 Turn 1) - 선수별 성과 · 연봉 효율 리포트 + 구단 포지션 약점 분석기 + 재정 수지 요약.
    ///   기록 출처 3단계: ① 진행 중/종료된 정규시즌 기록(league.Stats) ② 직전 시즌 스냅숏(연도 전환 후 - GMFrontOfficeState.LastSeasonReview)
    ///   ③ 추정(2026 시작처럼 시뮬레이션 기록이 없을 때 - 세부 능력치 기반 PerformanceData + OVR 예상 WAR → 신뢰도 낮음 · 표본 부족).
    ///   세이버 대체 지표(기획서 4.2): 종합 기여도 = 간이 WAR(GMLiveSeasonSimulator.UpdateWar) · 타격 생산성 = 게임용 wRC+(wOBA 비, 리그 100) ·
    ///   투수 독립 기여도 = FIP 비(리그 100) · 수비 기여도 = 경기당 수비 점수 비(리그 100).
    ///   색상 등급(4.1): 빨강 = 강점 · 초록 = 보통 · 파랑 = 약점 · 주황 = 위험/추가 확인.
    /// </summary>
    public static class GMSeasonReview
    {
        public const int HighConfidencePA = 300, MediumConfidencePA = 120;
        public const double HighConfidenceIP = 80, MediumConfidenceIP = 25, RelieverHighIP = 40;
        public const double LeagueFip = 4.30;
        public const int SeasonGames = 144;

        public static string ToneLabel(GMReportTone t) => t == GMReportTone.Strong ? "강점" : t == GMReportTone.Weak ? "약점" : t == GMReportTone.Risk ? "위험" : "보통";
        /// <summary>[TASK-GM-16] 화면 문자열에는 쓰지 않는다(색 이름 직접 노출 금지 - 화면은 색 · 막대로만 표시). 로그 · 문서용.</summary>
        public static string ToneColorName(GMReportTone t) => t == GMReportTone.Strong ? "빨강" : t == GMReportTone.Weak ? "파랑" : t == GMReportTone.Risk ? "주황" : "초록";
        public static string ConfidenceText(GMReportConfidence c) => c == GMReportConfidence.High ? "신뢰도 높음" : c == GMReportConfidence.Medium ? "신뢰도 보통" : "신뢰도 낮음";

        // ================================================================== 기록 스냅숏

        public static GMSeasonReviewLine FromStats(GMPlayerSeasonStats s) => new GMSeasonReviewLine
        {
            PlayerId = s.PlayerId, TeamCode = s.TeamCode, IsPitcher = s.IsPitcher,
            G = s.G, PA = s.PA, AB = s.AB, H = s.H, Doubles = s.Doubles, Triples = s.Triples, HR = s.HR, BB = s.BB, SO = s.SO, SB = s.SB,
            PG = s.PG, GS = s.GS, OutsPitched = s.OutsPitched, ER = s.ER, PSO = s.PSO, PBB = s.PBB, HA = s.HA, HRA = s.HRA, SV = s.SV, HLD = s.HLD,
            DefG = s.DefG, Chances = s.Chances, Errors = s.Errors, War = s.WAR, DefScore = s.DefensiveScore,
        };

        /// <summary>추정 기록 - 원 기록(실제 CSV 또는 세부 능력치 추정)을 그대로 한 시즌으로 보고, WAR는 OVR 기반 예상치.</summary>
        public static GMSeasonReviewLine Estimate(Player p, string teamCode)
        {
            var perf = p.Performance;
            var line = new GMSeasonReviewLine { PlayerId = p.InstanceId, TeamCode = teamCode ?? "", IsPitcher = p.IsPitcher, Estimated = true };
            if (p.IsPitcher)
            {
                bool starter = p.Template.PitcherRole == PitcherRole.StartingPitcher;
                double scale = starter ? 1.0 : 0.4; // 불펜은 상대 타자 수가 적다
                line.PG = starter ? 28 : 55;
                line.GS = starter ? 28 : 0;
                line.OutsPitched = (int)Math.Round(perf.IPOuts * scale);
                line.HA = (int)Math.Round(perf.PH * scale);
                line.HRA = (int)Math.Round(perf.PHR * scale);
                line.PBB = (int)Math.Round(perf.PBB * scale);
                line.PSO = (int)Math.Round(perf.PSO * scale);
                line.ER = (int)Math.Round(line.OutsPitched / 27.0 * LeagueFip);
            }
            else
            {
                line.G = 130;
                line.PA = perf.PA; line.AB = perf.AB; line.H = perf.H; line.HR = perf.HR; line.BB = perf.BB; line.SO = perf.SO;
                line.Doubles = (int)Math.Round(Math.Max(0, perf.H - perf.HR) * 0.21);
                line.Triples = (int)Math.Round(Math.Max(0, perf.H - perf.HR) * 0.02);
                line.DefG = 120;
                line.DefScore = (float)(120 * 0.25 * (Math.Max(20, p.DefenseStat) / 70.0));
            }
            line.War = (float)ProjectedWar(p, 1.0);
            return line;
        }

        /// <summary>OVR 기반 한 시즌 예상 WAR(GMFrontOffice.WarOf 예상식과 같은 기울기) × 출전 비중.</summary>
        public static double ProjectedWar(Player p, double share) => Math.Max(0.1, (p.BaseOverall - 58) * (p.IsPitcher ? 0.11 : 0.13)) * share;

        /// <summary>정규시즌 종료(시상식 결산) 시 전 구단 선수 기록을 스냅숏으로 남긴다 - 연도 전환 뒤 Turn 1 결산 · 협상 근거.</summary>
        public static void TakeSnapshot(GMLeagueState league)
        {
            if (league == null || league.Stats.Count == 0) return;
            var fo = GMFrontOffice.Ensure(league);
            fo.LastSeasonReview = league.Stats.Values.Where(s => !string.IsNullOrEmpty(s.PlayerId)).Select(FromStats).ToList();
            fo.LastSeasonReviewYear = league.SeasonYear;
        }

        /// <summary>기록 출처: 0 = 이번 정규시즌 · 1 = 직전 시즌 스냅숏 · 2 = 추정.</summary>
        public static int SourceKind(GMLeagueState league)
        {
            if (league.GamesPlayed > 0 && league.Stats.Count > 0) return 0;
            var fo = GMFrontOffice.Ensure(league);
            if (fo.LastSeasonReview != null && fo.LastSeasonReview.Count > 0 && fo.LastSeasonReviewYear >= league.SeasonYear - 1) return 1;
            return 2;
        }

        /// <summary>출처 기준 시즌 진행 비중(0.2~1) - WAR 기준선을 시즌 중반에도 맞추려고 쓴다.</summary>
        public static double SeasonShare(GMLeagueState league, int source) => source == 0 ? Math.Max(0.2, Math.Min(1.0, league.GamesPlayed / (double)SeasonGames)) : 1.0;

        public static GMSeasonReviewLine LineOf(GMLeagueState league, Player p, int source, string teamCode = null)
        {
            if (source == 0 && league.Stats.TryGetValue(p.InstanceId, out var s) && (s.PA > 0 || s.OutsPitched > 0)) return FromStats(s);
            if (source == 1)
            {
                var snap = GMFrontOffice.Ensure(league).LastSeasonReview.FirstOrDefault(l => l.PlayerId == p.InstanceId && (l.PA > 0 || l.OutsPitched > 0));
                if (snap != null) return snap;
            }
            return Estimate(p, teamCode ?? league.TeamCodeOf(p));
        }

        // ================================================================== 리그 기준선

        /// <summary>리그 기준선(가중 wOBA · FIP 원값 · 1WAR당 연봉 · 경기당 수비 점수) - 같은 출처의 전 구단 선수로 계산.</summary>
        public sealed class LeagueContext
        {
            public int Source;
            public double Share = 1.0;
            public double LgWoba = 0.32, LgRawFip = 1.2, FipConstant = 3.1, CostPerWar = 15000, LgDefPerGame = 0.3;
            public readonly Dictionary<string, GMSeasonReviewLine> Lines = new Dictionary<string, GMSeasonReviewLine>();
        }

        public static LeagueContext Context(GMLeagueState league)
        {
            var ctx = new LeagueContext { Source = SourceKind(league) };
            ctx.Share = SeasonShare(league, ctx.Source);
            foreach (var team in league.Teams.Values)
                foreach (var p in team.ReservePlayers)
                    if (p?.Template != null && !ctx.Lines.ContainsKey(p.InstanceId)) ctx.Lines[p.InstanceId] = LineOf(league, p, ctx.Source, team.TeamCode);
            var lines = ctx.Lines.Values.ToList();
            var bat = lines.Where(l => !l.IsPitcher && l.PA > 0).ToList();
            int pa = bat.Sum(l => l.PA);
            if (pa > 0) ctx.LgWoba = Math.Max(0.2, bat.Sum(WobaNumerator) / pa);
            var pit = lines.Where(l => l.IsPitcher && l.OutsPitched > 0).ToList();
            double ip = pit.Sum(l => l.IP);
            if (ip > 0)
            {
                ctx.LgRawFip = pit.Sum(l => 13.0 * l.HRA + 3.0 * l.PBB - 2.0 * l.PSO) / ip;
                ctx.FipConstant = LeagueFip - ctx.LgRawFip;
            }
            var paid = league.Teams.Values.SelectMany(t => t.ReservePlayers).Where(p => p?.Template != null && ctx.Lines.TryGetValue(p.InstanceId, out var l) && l.War > 0.3 * ctx.Share).ToList();
            double war = paid.Sum(p => (double)ctx.Lines[p.InstanceId].War);
            if (war > 0) ctx.CostPerWar = Math.Max(3000, paid.Sum(p => (double)p.Salary) / war);
            var def = lines.Where(l => !l.IsPitcher && l.DefG >= 10).ToList();
            if (def.Count > 0) ctx.LgDefPerGame = def.Sum(l => (double)l.DefScore) / Math.Max(1, def.Sum(l => l.DefG));
            return ctx;
        }

        private static double WobaNumerator(GMSeasonReviewLine l) => 0.69 * l.BB + 0.89 * l.Singles + 1.27 * l.Doubles + 1.62 * l.Triples + 2.10 * l.HR;

        /// <summary>게임용 타격 생산성(리그 평균 100) = 100 + 200 × (wOBA ÷ 리그 wOBA − 1). 0~250.</summary>
        public static double BattingProduction(GMSeasonReviewLine l, LeagueContext ctx) =>
            l.PA <= 0 ? 100 : Clamp(100 + 200 * (WobaNumerator(l) / l.PA / ctx.LgWoba - 1), 0, 250);

        /// <summary>게임용 투수 독립 기여도(리그 평균 100) = 100 + 200 × (리그 FIP − FIP) ÷ 리그 FIP. FIP = (13HR + 3BB − 2K) ÷ 이닝 + 상수.</summary>
        public static double PitchingProduction(GMSeasonReviewLine l, LeagueContext ctx)
        {
            if (l.OutsPitched <= 0) return 100;
            double fip = (13.0 * l.HRA + 3.0 * l.PBB - 2.0 * l.PSO) / l.IP + ctx.FipConstant;
            return Clamp(100 + 200 * (LeagueFip - fip) / LeagueFip, 0, 250);
        }

        public static double Production(GMSeasonReviewLine l, LeagueContext ctx) => l.IsPitcher ? PitchingProduction(l, ctx) : BattingProduction(l, ctx);

        // ================================================================== 선수 리포트

        public static string PositionLabelOf(Player p)
        {
            if (p?.Template == null) return "-";
            if (!p.IsPitcher) return GMFrontOffice.PositionLabel(GMFrontOffice.PositionCode(p.Template.BatterPosition));
            switch (p.Template.PitcherRole)
            {
                case PitcherRole.StartingPitcher: return "선발";
                case PitcherRole.Closer: return "마무리";
                default: return "불펜";
            }
        }

        private static bool IsStarterPitcher(Player p) => p.IsPitcher && p.Template.PitcherRole == PitcherRole.StartingPitcher;

        public static GMPlayerReport Report(GMLeagueState league, Player p) => Report(league, p, Context(league));

        public static GMPlayerReport Report(GMLeagueState league, Player p, LeagueContext ctx)
        {
            var team = league.Teams.Values.FirstOrDefault(t => t.ReservePlayers.Contains(p));
            if (!ctx.Lines.TryGetValue(p.InstanceId, out var line)) ctx.Lines[p.InstanceId] = line = LineOf(league, p, ctx.Source, team?.TeamCode);
            var r = new GMPlayerReport { Player = p, Line = line, PositionLabel = PositionLabelOf(p) };
            double f = line.Estimated ? 1.0 : ctx.Share;
            bool reliever = p.IsPitcher && !IsStarterPitcher(p);

            // 신뢰도 · 표본
            if (line.Estimated)
            {
                r.Confidence = GMReportConfidence.Low;
                r.SampleWarning = true;
                r.SampleNote = "표본 부족 - 시뮬레이션 기록 없음, 세부 능력치 추정치(오판 가능성 높음)";
            }
            else if (p.IsPitcher)
            {
                double hi = reliever ? RelieverHighIP : HighConfidenceIP;
                r.Confidence = line.IP >= hi * f ? GMReportConfidence.High : line.IP >= MediumConfidenceIP * f ? GMReportConfidence.Medium : GMReportConfidence.Low;
                r.SampleWarning = r.Confidence == GMReportConfidence.Low;
                r.SampleNote = r.SampleWarning ? $"표본 부족 - {line.IP:0.0}이닝(오판 가능성)" : $"{line.IP:0.0}이닝";
            }
            else
            {
                r.Confidence = line.PA >= HighConfidencePA * f ? GMReportConfidence.High : line.PA >= MediumConfidencePA * f ? GMReportConfidence.Medium : GMReportConfidence.Low;
                r.SampleWarning = r.Confidence == GMReportConfidence.Low;
                r.SampleNote = r.SampleWarning ? $"표본 부족 - {line.PA}타석(오판 가능성)" : $"{line.PA}타석";
            }
            r.ConfidenceLabel = ConfidenceText(r.Confidence) + (r.SampleWarning ? " · 표본 부족" : "");

            // ① 종합 기여도(WAR - 0이면 공격력이 아니라 종합 기여도가 대체 선수 수준이라는 뜻)
            double war = line.War;
            double hiWar = (reliever ? 1.5 : 3.0) * f, loWar = (reliever ? 0.3 : 1.0) * f;
            r.War.Value = war;
            r.War.ValueText = $"{war:0.0}";
            r.War.Tone = war >= hiWar ? GMReportTone.Strong : war >= loWar ? GMReportTone.Neutral : GMReportTone.Weak;
            r.War.Verdict = war <= 0.2 * f ? "대체 선수 수준" : r.War.Tone == GMReportTone.Strong ? "팀 기여도 높음" : r.War.Tone == GMReportTone.Weak ? "기여도 낮음" : "평균권";

            // ② 타격 생산성 / 투수 독립 기여도
            double prod = Production(line, ctx);
            r.Production.Label = p.IsPitcher ? "투수 독립 기여도" : "타격 생산성";
            r.Production.Value = prod;
            r.Production.ValueText = $"{prod:0}";
            r.Production.Tone = prod >= 115 ? GMReportTone.Strong : prod >= 90 ? GMReportTone.Neutral : GMReportTone.Weak;
            r.Production.Verdict = prod >= 115 ? "리그 평균 이상" : prod >= 90 ? "리그 평균권" : "리그 평균 이하";

            // ③ 수비 기여도
            if (p.IsPitcher)
            {
                r.Defense.ValueText = "-";
                r.Defense.Verdict = "투수 해당 없음";
            }
            else
            {
                double dpg = line.DefG > 0 ? line.DefScore / line.DefG : 0;
                double def = line.DefG <= 0 ? 100 : Clamp(100 + 100 * (dpg - ctx.LgDefPerGame) / Math.Max(0.1, Math.Abs(ctx.LgDefPerGame)), 0, 200);
                r.Defense.Value = def;
                r.Defense.ValueText = $"{def:0}";
                r.Defense.Tone = def >= 115 ? GMReportTone.Strong : def >= 85 ? GMReportTone.Neutral : GMReportTone.Weak;
                r.Defense.Verdict = line.Errors > 0 ? $"실책 {line.Errors}" : "실책 억제";
            }

            // ④ 연봉 효율(가성비) = 기여도 시장가(WAR × 리그 1WAR당 연봉) ÷ 연봉
            double value = Math.Max(0, war) * ctx.CostPerWar / Math.Max(0.2, f);
            double ratio = value / Math.Max(Player.MinSalary, p.Salary);
            r.Efficiency.Value = ratio;
            r.Efficiency.ValueText = $"{ratio:0.00}배";
            if (p.Salary <= Player.MinSalary * 5 / 3 && ratio < 0.7) { r.Efficiency.Tone = GMReportTone.Neutral; r.Efficiency.Verdict = "최저 연봉권"; }
            else
            {
                r.Efficiency.Tone = ratio >= 1.3 ? GMReportTone.Strong : ratio >= 0.7 ? GMReportTone.Neutral : GMReportTone.Weak;
                r.Efficiency.Verdict = ratio >= 1.3 ? "가성비 우수" : ratio >= 0.7 ? "적정" : "연봉 효율 낮음";
            }

            // ⑤ 대체 불가성 = 내 WAR − 최선의 대안(같은 포지션 · 보직의 구단 백업 · FA 시장) WAR
            var alt = BestAlternative(league, team, p, ctx, f);
            double irr = war - alt.war;
            r.Replaceability.Value = irr;
            r.Replaceability.ValueText = $"{(irr >= 0 ? "+" : "")}{irr:0.0}";
            r.Replaceability.Tone = irr >= 1.5 * f ? GMReportTone.Strong : irr >= 0.5 * f ? GMReportTone.Neutral : GMReportTone.Weak;
            r.Replaceability.Verdict = (r.Replaceability.Tone == GMReportTone.Strong ? "대체 불가" : r.Replaceability.Tone == GMReportTone.Neutral ? "대안 있음" : "대체 가능") +
                                       (alt.name != null ? $" (대안 {alt.name})" : "");

            // ⑥ 최근 추세 = 실제 WAR − 출전 비중 반영 예상 WAR
            if (line.Estimated)
            {
                r.Trend.ValueText = "-";
                r.Trend.Verdict = "판단 보류(표본 없음)";
                r.Trend.Tone = GMReportTone.Neutral;
            }
            else
            {
                double share = p.IsPitcher ? line.IP / (reliever ? 60.0 : 150.0) : line.PA / 550.0;
                double diff = war - ProjectedWar(p, Math.Min(1.2, share));
                r.Trend.Value = diff;
                r.Trend.ValueText = $"{(diff >= 0 ? "+" : "")}{diff:0.0}";
                r.Trend.Tone = diff >= 0.8 ? GMReportTone.Strong : diff <= -0.8 ? GMReportTone.Weak : GMReportTone.Neutral;
                r.Trend.Verdict = diff >= 0.8 ? "상승" : diff <= -0.8 ? "하락" : "유지";
            }
            if (r.Trend.Tone != GMReportTone.Strong && p.Age >= 34) { r.Trend.Tone = GMReportTone.Risk; r.Trend.Verdict = "노쇠 위험(추가 확인)"; }
            if (p.InjuryRemainingDays > 0) { r.Trend.Tone = GMReportTone.Risk; r.Trend.Verdict = $"부상 변수({p.InjuryRemainingDays}일)"; }
            return r;
        }

        private static (double war, string name) BestAlternative(GMLeagueState league, GMTeamState team, Player p, LeagueContext ctx, double f)
        {
            bool Same(Player x) => x != p && x?.Template != null && x.IsPitcher == p.IsPitcher &&
                                   (p.IsPitcher ? IsStarterPitcher(x) == IsStarterPitcher(p) : x.Template.BatterPosition == p.Template.BatterPosition);
            var best = (war: double.MinValue, name: (string)null);
            if (team != null)
                foreach (var x in team.ReservePlayers.Where(Same))
                {
                    double w = ctx.Lines.TryGetValue(x.InstanceId, out var l) && !l.Estimated ? l.War : ProjectedWar(x, f) * 0.6; // 백업은 출전 비중 60% 가정
                    if (w > best.war) best = (w, x.Template.PlayerName);
                }
            foreach (var x in league.FreeAgents.Where(Same))
            {
                double w = ProjectedWar(x, f);
                if (w > best.war) best = (w, x.Template.PlayerName + "(FA)");
            }
            return best.name == null ? (0.0, null) : best;
        }

        // ================================================================== 포지션 약점 분석기

        private static readonly BatterPosition[] FieldPositions =
        {
            BatterPosition.Catcher, BatterPosition.FirstBase, BatterPosition.SecondBase, BatterPosition.ThirdBase, BatterPosition.ShortStop,
            BatterPosition.LeftField, BatterPosition.CenterField, BatterPosition.RightField, BatterPosition.DesignatedHitter,
        };

        private static List<Player> Rotation(GMTeamState team) =>
            team.Roster.Where(IsStarterPitcher).OrderByDescending(p => p.BaseOverall).Take(5).ToList();

        private static List<Player> Bullpen(GMTeamState team) =>
            team.Roster.Where(p => p.IsPitcher && !IsStarterPitcher(p)).OrderByDescending(p => p.BaseOverall).Take(7).ToList();

        private static (double prod, double war, string name) Slot(IEnumerable<Player> players, LeagueContext ctx)
        {
            var list = players.Where(p => p != null).ToList();
            if (list.Count == 0) return (60, 0, "-");
            var lines = list.Select(p => ctx.Lines.TryGetValue(p.InstanceId, out var l) ? l : null).Where(l => l != null).ToList();
            if (lines.Count == 0) return (60, 0, "-");
            double prod = lines.Average(l => Production(l, ctx));
            return (prod, lines.Sum(l => (double)l.War), list.Count == 1 ? list[0].Template.PlayerName : $"{list[0].Template.PlayerName} 외 {list.Count - 1}명");
        }

        /// <summary>10구단 같은 포지션 주전과 비교한 포지션별 생산성 · WAR(9 포지션 + 선발진 + 불펜).</summary>
        public static List<GMPositionWeakness> AnalyzePositions(GMLeagueState league, GMTeamState team, LeagueContext ctx)
        {
            var result = new List<GMPositionWeakness>();
            if (team == null) return result;
            var starters = league.Teams.Values.ToDictionary(t => t.TeamCode, t => LineupAssignment.AssignStarters(t.Roster, t.Lineup));
            foreach (var pos in FieldPositions)
            {
                Player Of(string code) => starters[code].FirstOrDefault(s => s.Position == pos)?.Player;
                var mine = Slot(new[] { Of(team.TeamCode) }, ctx);
                double lg = league.Teams.Keys.Select(c => Slot(new[] { Of(c) }, ctx).prod).Average();
                string code = GMFrontOffice.PositionCode(pos);
                result.Add(new GMPositionWeakness { Position = code, PositionLabel = GMFrontOffice.PositionLabel(code), PlayerName = mine.name, Production = mine.prod, LeagueProduction = lg, War = mine.war });
            }
            var sp = Slot(Rotation(team), ctx);
            result.Add(new GMPositionWeakness { Position = "SP", PositionLabel = "선발진", PlayerName = sp.name, Production = sp.prod, War = sp.war,
                LeagueProduction = league.Teams.Values.Select(t => Slot(Rotation(t), ctx).prod).Average() });
            var rp = Slot(Bullpen(team), ctx);
            result.Add(new GMPositionWeakness { Position = "RP", PositionLabel = "불펜", PlayerName = rp.name, Production = rp.prod, War = rp.war,
                LeagueProduction = league.Teams.Values.Select(t => Slot(Bullpen(t), ctx).prod).Average() });

            double f = ctx.Source == 2 ? 1.0 : ctx.Share;
            foreach (var w in result)
            {
                bool pitching = w.Position == "SP" || w.Position == "RP";
                string metric = pitching ? "투수 독립 기여도" : "타격 생산성";
                double replacement = (w.Position == "SP" ? 2.0 : w.Position == "RP" ? 1.0 : 0.3) * f;
                if (w.War <= replacement)
                {
                    w.Tone = GMReportTone.Weak;
                    w.Comment = $"{w.PositionLabel} 포지션의 종합 기여도가 대체 선수 수준입니다 - {w.PlayerName} WAR {w.War:0.0} · {metric} {w.Production:0}.";
                }
                else if (w.Production < 90 || w.Gap <= -10)
                {
                    w.Tone = GMReportTone.Weak;
                    w.Comment = $"{w.PositionLabel} 포지션의 {metric}이 {w.Production:0}으로 10구단 같은 포지션 평균({w.LeagueProduction:0})보다 낮습니다 - {w.PlayerName}.";
                }
                else if (w.Gap >= 15)
                {
                    w.Tone = GMReportTone.Strong;
                    w.Comment = $"{w.PositionLabel} 포지션은 {metric} {w.Production:0}으로 리그 상위권입니다 - {w.PlayerName}.";
                }
                else
                {
                    w.Tone = GMReportTone.Neutral;
                    w.Comment = $"{w.PositionLabel} 포지션은 {metric} {w.Production:0}으로 평균권입니다.";
                }
            }
            return result;
        }

        // ================================================================== 결산 리포트

        public static GMSeasonSummary Build(GMLeagueState league)
        {
            var summary = new GMSeasonSummary();
            var team = league?.UserTeam;
            if (team == null) return summary;
            var fo = GMFrontOffice.Ensure(league);
            var ctx = Context(league);
            summary.Estimated = ctx.Source == 2;
            summary.Year = ctx.Source == 1 ? fo.LastSeasonReviewYear : league.SeasonYear;
            summary.Source = ctx.Source == 0 ? $"{league.SeasonYear} 정규시즌 기록({league.GamesPlayed}/{SeasonGames}경기)" : ctx.Source == 1 ? $"{fo.LastSeasonReviewYear} 시즌 결산 스냅숏" : "시뮬레이션 기록 없음 - 세부 능력치 추정";
            summary.Confidence = ctx.Source == 2 ? GMReportConfidence.Low : ctx.Source == 1 ? GMReportConfidence.High
                : league.GamesPlayed >= 100 ? GMReportConfidence.High : league.GamesPlayed >= 40 ? GMReportConfidence.Medium : GMReportConfidence.Low;
            summary.ConfidenceLabel = ConfidenceText(summary.Confidence) + (summary.Confidence == GMReportConfidence.Low ? " · 표본 부족" : "");

            // 팀 성적
            if (ctx.Source == 0)
            {
                var rec = league.RecordOf(team.TeamCode);
                summary.W = rec.W; summary.D = rec.D; summary.L = rec.L; summary.GamesPlayed = rec.W + rec.D + rec.L;
                summary.Rank = league.Teams.Keys.Select(league.RecordOf).OrderByDescending(r => Pct(r.W, r.L)).ThenByDescending(r => r.W).ToList().FindIndex(r => r.TeamCode == team.TeamCode) + 1;
            }
            else
            {
                var h = fo.History.Where(x => x.TeamCode == team.TeamCode).OrderByDescending(x => x.Year).FirstOrDefault();
                if (h != null) { summary.W = h.W; summary.D = h.D; summary.L = h.L; summary.Rank = h.Rank; summary.GamesPlayed = h.W + h.D + h.L; }
            }
            summary.Pct = Pct(summary.W, summary.L);
            summary.RecordLine = summary.GamesPlayed > 0
                ? $"{summary.Year} {CompyaTeamName(team.TeamCode)} {summary.W}승 {summary.D}무 {summary.L}패 · 승률 {summary.Pct:.000} · {summary.Rank}위"
                : $"{league.SeasonYear} 개막 전 - 직전 시즌 기록 없음(전력 추정)";

            // 재정 수지
            var b = GMFrontOffice.Budget(league, team);
            summary.FinanceBalance = b.MoneyForFA;
            summary.FinanceLines.Add($"예상 시즌 총예산 {GMDiagnosticFormat.Won(b.ProjectedBudget)}");
            summary.FinanceLines.Add($"총 지출 예상 {GMDiagnosticFormat.Won(b.TotalExpenses)} (보장 연봉 {GMDiagnosticFormat.Short(b.GuaranteedPayroll)} · 재계약 {GMDiagnosticFormat.Short(b.ReSignEstimate)})");
            summary.FinanceLines.Add($"페이롤 {GMDiagnosticFormat.Won(team.Payroll)} / 샐러리캡 {GMDiagnosticFormat.Won(team.PayrollCap)} ({team.Payroll * 100L / Math.Max(1, team.PayrollCap)}%)");
            summary.FinanceLines.Add($"재정 수지(FA 가용 자금) {(b.MoneyForFA >= 0 ? "+" : "-")}{GMDiagnosticFormat.Won(Math.Abs(b.MoneyForFA))}");

            // 포지션 약점
            summary.AllPositions.AddRange(AnalyzePositions(league, team, ctx));
            var weak = summary.AllPositions.Where(w => w.Tone == GMReportTone.Weak).OrderBy(w => w.Gap).ThenBy(w => w.War).Take(4).ToList();
            if (weak.Count == 0)
            {
                var lowest = summary.AllPositions.OrderBy(w => w.Gap).First();
                lowest.Tone = GMReportTone.Neutral;
                lowest.Comment = $"뚜렷한 약점은 없지만 {lowest.PositionLabel} 포지션이 상대적으로 가장 낮습니다 - 생산성 {lowest.Production:0} · 리그 {lowest.LeagueProduction:0}.";
                weak.Add(lowest);
            }
            if (summary.Confidence == GMReportConfidence.Low)
                foreach (var w in weak) w.Comment += " [표본 부족 / 신뢰도 낮음]";
            summary.Weaknesses.AddRange(weak);

            // 선수 리포트(1군)
            summary.Players.AddRange(team.Roster.Where(p => p?.Template != null).Select(p => Report(league, p, ctx))
                .OrderBy(r => r.Player.IsPitcher ? 1 : 0).ThenByDescending(r => r.War.Value));

            // 직원 3인 보고(MVP 축소판)
            var top = weak[0];
            summary.StaffLines.Add($"[데이터분석팀장 · {summary.ConfidenceLabel}] {top.Comment}");
            summary.StaffLines.Add(b.OverBudget || team.Payroll > team.PayrollCap
                ? $"[재무팀장 · 경고] 지출이 예산을 넘습니다 - 재정 수지 {GMDiagnosticFormat.Won(b.MoneyForFA)}. 고액 재계약은 동결 · 옵션 계약을 권고합니다."
                : $"[재무팀장] 캡 여유 {GMDiagnosticFormat.Won(Math.Max(0, team.PayrollCap - team.Payroll))} · FA 가용 자금 {GMDiagnosticFormat.Won(Math.Max(0, b.MoneyForFA))}.");
            int teamwork = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.TeamworkBuff).TeamworkScore;
            double loyalty = team.Roster.Count == 0 ? 0 : team.Roster.Average(p => p.Loyalty);
            summary.StaffLines.Add($"[선수관리팀장] 선수단의 단장 신뢰도 {team.LockerRoomTrust} · 평균 구단 충성도 {loyalty:0} · 팀 분위기(팀워크) {teamwork}.");
            return summary;
        }

        private static double Pct(int w, int l) => w + l == 0 ? 0 : (double)w / (w + l);
        private static string CompyaTeamName(string code) => NameAliasTable.DisplayTeamName(code);
        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>
    /// [TASK-GM-11] 선수 간 유대(Player Bond) - 실존 인물 관계를 하드코딩하지 않고 현재 로스터의 규칙으로 매번 만든다(저장 없음).
    ///   배터리 = 주전 포수 ↔ 1~5선발 / 키스톤 = 주전 2루수 ↔ 주전 유격수 / 멘토 = 더그아웃 리더(31세+) ↔ 같은 투타 유망주(최대 2명).
    ///   경기 OVR 보정은 없고 협상(관계 근거 카드) · 이후 2단계 사건의 재료로만 쓴다.
    /// </summary>
    public static class GMPlayerBonds
    {
        public static string KindLabel(GMBondKind k) => k == GMBondKind.Battery ? "배터리" : k == GMBondKind.Keystone ? "키스톤 콤비" : "멘토-멘티";

        public static List<(GMBondKind kind, Player partner)> For(GMTeamState team, Player p)
        {
            var list = new List<(GMBondKind, Player)>();
            if (team == null || p?.Template == null) return list;
            var starters = LineupAssignment.AssignStarters(team.Roster, team.Lineup);
            Player At(BatterPosition pos) => starters.FirstOrDefault(s => s.Position == pos)?.Player;
            var catcher = At(BatterPosition.Catcher);
            var rotation = team.Roster.Where(x => x.IsPitcher && x.Template.PitcherRole == PitcherRole.StartingPitcher).OrderByDescending(x => x.BaseOverall).Take(5).ToList();
            if (p == catcher) list.AddRange(rotation.Take(2).Select(x => (GMBondKind.Battery, x)));
            else if (rotation.Contains(p) && catcher != null) list.Add((GMBondKind.Battery, catcher));
            var second = At(BatterPosition.SecondBase);
            var shortstop = At(BatterPosition.ShortStop);
            if (p == second && shortstop != null) list.Add((GMBondKind.Keystone, shortstop));
            else if (p == shortstop && second != null) list.Add((GMBondKind.Keystone, second));
            var pool = team.ReservePlayers.Where(x => x?.Template != null && x != p && x.IsPitcher == p.IsPitcher).ToList();
            if (p.RoleArchetype == LockerRoomRole.DugoutLeader && p.Age >= 31)
                list.AddRange(pool.Where(x => x.RoleArchetype == LockerRoomRole.Prospect).OrderByDescending(x => x.BaseOverall).Take(2).Select(x => (GMBondKind.Mentor, x)));
            else if (p.RoleArchetype == LockerRoomRole.Prospect)
            {
                var mentor = pool.Where(x => x.RoleArchetype == LockerRoomRole.DugoutLeader && x.Age >= 31).OrderByDescending(x => x.BaseOverall).FirstOrDefault();
                if (mentor != null) list.Add((GMBondKind.Mentor, mentor));
            }
            return list;
        }
    }
}
