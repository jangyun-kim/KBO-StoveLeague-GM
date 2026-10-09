using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>[TASK-GM-16] 백분위 → 화면 구간(OOTP 백분위 바 색 구간). 색 이름을 글자로 쓰지 않는다 - 화면은 바 길이 · 색으로만 보여 준다.</summary>
    public enum GMPercentileBand { Elite = 0, Plus = 1, Average = 2, Minus = 3, Poor = 4 }

    /// <summary>
    /// [TASK-GM-16] 선수 스카우팅 리포트(「프로야구 스카우팅 리포트」 지면 스타일) - 선수 상세 팝업 · 계약 협상실이 쓴다.
    ///   헤드라인(한 줄 캐치프레이즈) · 스카우트 코멘트(성향 · 강점 · 약점 · 나이 곡선) · 시즌 기록 줄 · 수상 이력 · 20-80 스카우팅 등급 · 스타터 덱 등급.
    /// </summary>
    public static class GMScoutingReport
    {
        public static GMPercentileBand BandOf(int percentile) =>
            percentile >= 80 ? GMPercentileBand.Elite : percentile >= 60 ? GMPercentileBand.Plus : percentile >= 40 ? GMPercentileBand.Average : percentile >= 20 ? GMPercentileBand.Minus : GMPercentileBand.Poor;

        /// <summary>20-80 스카우팅 등급(5 단위) - 백분위 50 = 50.</summary>
        public static int ScoutGrade(int percentile) => Math.Max(20, Math.Min(80, 20 + (int)Math.Round(Math.Max(0, Math.Min(100, percentile)) * 0.6 / 5.0) * 5));

        public static string GradeWord(int grade) => grade >= 70 ? "플러스플러스" : grade >= 60 ? "플러스" : grade >= 45 ? "평균" : grade >= 35 ? "평균 이하" : "약점";

        public static string AwardLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            int cut = id.LastIndexOf('_');
            string year = cut > 0 && int.TryParse(id.Substring(cut + 1), out _) ? id.Substring(cut + 1) : "";
            string key = year != "" ? id.Substring(0, cut) : id;
            string name;
            switch (key)
            {
                case "GOLDEN_GLOVE": name = "골든글러브"; break;
                case "TITLE_HOLDER": name = "타이틀 홀더"; break;
                case "KS_CHAMPION": name = "한국시리즈 우승"; break;
                case "KS_MVP": name = "한국시리즈 MVP"; break;
                default:
                    name = key.StartsWith("MVP", StringComparison.Ordinal) ? "정규시즌 MVP" : key.Replace('_', ' ');
                    break;
            }
            return year != "" ? $"{year} {name}" : name;
        }

        /// <summary>수상 이력(최근순 · 최대 max개).</summary>
        public static string AwardsLine(Player p, int max = 6)
        {
            var ids = p?.CareerAwardIds ?? new List<string>();
            if (ids.Count == 0) return "수상 이력 없음 - 첫 트로피를 기다리는 선수";
            var list = ids.Select(AwardLabel).Where(s => s != "").Reverse().ToList();
            return string.Join(" · ", list.Take(max)) + (list.Count > max ? $" 외 {list.Count - max}회" : "");
        }

        /// <summary>[TASK-GM-18] 기록 출처 태그 - 실제 KBO 기록(카드 시즌 원 기록 · 2025년까지 누적) / 게임 내 시뮬레이션 기록(2026년~ 진행 시즌 · 결산).</summary>
        public const string RealRecordTag = "[실제 KBO 기록]", SimRecordTag = "[시뮬레이션 기록]";
        public static readonly string RealTagColor = "#D9481F", SimTagColor = "#1F6FD9";
        public static string Tagged(string tag, string color, string text) => $"<color={color}>{tag}</color> {text}";

        /// <summary>
        /// 시즌 기록 한 줄 - 이번 시즌 기록이 있으면 그것, 없으면 직전 시즌 결산 스냅숏, 그것도 없으면 카드 시즌 능력치.
        /// [TASK-GM-18] 각 머리줄에 출처 태그 - 진행 시즌 · 결산 = [시뮬레이션 기록](파랑 견본), 카드 시즌 = [실제 KBO 기록](주황 견본).
        /// </summary>
        public static List<string> StatLines(GMLeagueState league, Player p)
        {
            var lines = new List<string>();
            if (p?.Template == null) return lines;
            if (league != null && league.Stats.TryGetValue(p.InstanceId, out var s) && (s.PA > 0 || s.OutsPitched > 0))
            {
                lines.Add(Tagged(SimRecordTag, SimTagColor, $"{league.SeasonYear} 시즌(진행 중)"));
                lines.Add(p.IsPitcher ? PitchLine(s.PG, s.W, s.L, s.SV, s.HLD, s.OutsPitched, s.ER, s.PSO, s.PBB, s.WAR) : BatLine(s.G, s.PA, s.AB, s.H, s.HR, s.RBI, s.SB, s.BB, s.SO, s.Doubles, s.Triples, s.WAR));
            }
            var fo = league != null ? GMFrontOffice.Ensure(league) : null;
            var last = fo?.LastSeasonReview?.FirstOrDefault(l => l.PlayerId == p.InstanceId && !l.Estimated);
            if (last != null)
            {
                lines.Add(Tagged(SimRecordTag, SimTagColor, $"{fo.LastSeasonReviewYear} 시즌(결산)"));
                lines.Add(p.IsPitcher ? PitchLine(last.PG, -1, -1, last.SV, last.HLD, last.OutsPitched, last.ER, last.PSO, last.PBB, last.War) : BatLine(last.G, last.PA, last.AB, last.H, last.HR, -1, last.SB, last.BB, last.SO, last.Doubles, last.Triples, last.War));
            }
            var t = GMRosterTiers.OriginalOf(p.Template);
            if (p.IsPitcher)
            {
                var ps = p.Template.PitcherStats;
                lines.Add(Tagged(RealRecordTag, RealTagColor, $"{t.SeasonYear} 시즌 카드") + $" · 구위 {ps.Stuff} · 구속 {ps.Velocity} · 무브먼트 {ps.Movement} · 제구 {ps.Control} · 체력 {ps.Stamina}");
            }
            else
            {
                var bs = p.Template.BatterStats;
                lines.Add(Tagged(RealRecordTag, RealTagColor, $"{t.SeasonYear} 시즌 카드") + $" · 파워 {bs.Power} · 컨택 {bs.Contact} · 선구 {bs.Discipline} · 스피드 {bs.Speed} · 수비 {bs.Defense}");
            }
            return lines;
        }

        private static string BatLine(int g, int pa, int ab, int h, int hr, int rbi, int sb, int bb, int so, int d2, int d3, float war)
        {
            double avg = ab > 0 ? (double)h / ab : 0, obp = pa > 0 ? (double)(h + bb) / pa : 0;
            double slg = ab > 0 ? (double)(h + d2 + 2 * d3 + 3 * hr) / ab : 0;
            return $"{g}경기 · 타율 {avg:.000} · 출루율 {obp:.000} · 장타율 {slg:.000} · {hr}홈런" + (rbi >= 0 ? $" · {rbi}타점" : "") + $" · {sb}도루 · WAR {war:0.0}";
        }

        private static string PitchLine(int g, int w, int l, int sv, int hld, int outs, int er, int so, int bb, float war)
        {
            double ip = outs / 3.0, era = outs > 0 ? er * 27.0 / outs : 0;
            return $"{g}경기" + (w >= 0 ? $" · {w}승 {l}패" : "") + $" · {sv}세이브 · {hld}홀드 · {(int)ip}.{outs % 3}이닝 · 평균자책점 {era:0.00} · 탈삼진 {so} · WAR {war:0.0}";
        }

        /// <summary>헤드라인 - 스카우팅 리포트 지면의 굵은 한 줄(Bold 없이 크기 · 색으로 강조).</summary>
        public static string Headline(GMLeagueState league, Player p, IReadOnlyList<GMPercentileRow> rows)
        {
            if (p?.Template == null) return "";
            var tier = GMStarterDeck.TierOf(p);
            var top = rows?.Where(r => r.Label != "OVR" && r.Label != "잠재력").OrderByDescending(r => r.Percentile).FirstOrDefault();
            string strength = top != null ? $"{top.Label} 상위 {Math.Max(1, 100 - top.Percentile)}%" : "";
            int majors = p.CareerAwardIds?.Count(Player.IsMajorAward) ?? 0;
            if (majors >= 3) return $"트로피 {majors}개의 레전드 - {strength}의 클래스는 영원하다";
            if (tier == GMStarterTier.S) return $"{GMRosterTiers.OriginalOf(p.Template).SeasonYear}년의 그 선수가 돌아왔다 - {strength}";
            if (p.Age <= 23 && p.Potential - p.BaseOverall >= 8) return $"잠재력 {p.Potential}의 원석 - 다음 시대의 얼굴";
            if (p.Age >= GMAgingCurve.DeclineAge) return $"{p.Age}세 베테랑, 노련함으로 버티는 {strength}";
            if (top != null && top.Percentile >= 90) return $"리그가 인정한 {strength} - 상대 벤치가 먼저 계산하는 선수";
            if (p.RoleArchetype == LockerRoomRole.UnsungHero) return "보이지 않는 곳에서 팀을 받치는 살림꾼";
            return $"{GMFrontOffice.PositionLabel(p.Position)}의 한 축 - {strength}";
        }

        /// <summary>스카우트 코멘트(3~4문장).</summary>
        public static string Narrative(GMLeagueState league, Player p, IReadOnlyList<GMPercentileRow> rows)
        {
            if (p?.Template == null) return "";
            var sorted = (rows ?? new List<GMPercentileRow>()).Where(r => r.Label != "OVR" && r.Label != "잠재력").OrderByDescending(r => r.Percentile).ToList();
            var best = sorted.FirstOrDefault();
            var worst = sorted.LastOrDefault();
            var parts = new List<string>();
            var origin = GMRosterTiers.OriginalOf(p.Template);
            parts.Add($"{origin.SeasonYear} 시즌 카드 기준 {GMStarterDeck.TierLabel(GMStarterDeck.TierOf(p))} 선수.");
            if (best != null) parts.Add($"{best.Label}은(는) KBO 상위 {Math.Max(1, 100 - best.Percentile)}% 수준으로 {GradeWord(ScoutGrade(best.Percentile))} 등급({ScoutGrade(best.Percentile)}).");
            if (worst != null && worst != best) parts.Add($"반면 {worst.Label}은(는) 백분위 {worst.Percentile}%로 {(worst.Percentile < 35 ? "보완이 필요한 약점" : "평균권")}이다.");
            string age = p.Age <= GMFuturesMeeting.YoungMaxAge ? $"성장 구간(연 +2~+3, 잠재력 {p.Potential})에 있다."
                : p.Age <= GMFuturesMeeting.MaxGrowthAge ? "전성기 진입 구간 - 큰 폭의 성장보다 유지에 가깝다."
                : p.Age < GMAgingCurve.DeclineAge ? "기량이 정점에서 머무는 시기 - 해마다 동결 또는 소폭 하락."
                : $"에이징 커브 구간 - 매년 OVR -1~-3{(GMAgingCurve.IsLegend(p) || GMAgingCurve.HasBond(p) ? "(레전드 · 유대 완화 적용)" : "")}.";
            parts.Add(age);
            string mood = p.EgoLevel >= 4 ? $"자존심(Ego {p.EgoLevel})이 강해 보직 · 연봉에 민감하다." : p.RoleArchetype == LockerRoomRole.DugoutLeader ? "더그아웃 리더 - 후배 멘토링에 적합하다." : "";
            if (mood != "") parts.Add(mood);
            return string.Join(" ", parts);
        }

        /// <summary>성향 칩 한 줄.</summary>
        public static string Traits(Player p) => p == null ? "" :
            $"Ego {p.EgoLevel} · {RoleLabel(p.RoleArchetype)} · 에이전트 {GMNegotiationRoom.ArchetypeLabel(p.AgentArchetype)} · 구단 충성도 {p.Loyalty} · 만족도 {p.PersonalMorale}" + (p.IsCaptain ? " · 주장" : "");

        public static string RoleLabel(LockerRoomRole r) => r == LockerRoomRole.AlphaDog ? "알파독" : r == LockerRoomRole.Ambitious ? "야망가" : r == LockerRoomRole.DugoutLeader ? "더그아웃 리더" : r == LockerRoomRole.Prospect ? "유망주" : "살림꾼";

        /// <summary>계약 협상실 6대 지표 → 0~1 막대 길이(지표별 스케일).</summary>
        public static float MetricFill(int index, GMReportMetric m)
        {
            if (m == null || m.ValueText == "-") return 0f;
            double v = m.Value;
            double f;
            switch (index)
            {
                case 0: f = v / 6.0; break;               // 종합 기여도(WAR) 0~6
                case 1: f = (v - 50) / 100.0; break;      // 생산성 50~150
                case 2: f = v / 200.0; break;             // 수비 0~200
                case 3: f = v / 2.0; break;               // 연봉 효율 0~2배
                case 4: f = (v + 2.0) / 5.0; break;       // 대체 불가성 -2~+3
                default: f = (v + 2.0) / 4.0; break;      // 최근 추세 -2~+2
            }
            return (float)Math.Max(0.03, Math.Min(1.0, f));
        }
    }
}
