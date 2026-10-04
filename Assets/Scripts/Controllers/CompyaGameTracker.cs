using System.Collections.Generic;
using KBOManager.Engine;
using KBOManager.Models;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-179] 컴프야V26 중계 화면용 경기 집계기. MatchEngine이 이미 계산해 둔 PlayEvent 목록을 "지금까지 재생된 지점"까지
    /// 다시 읽어 전광판(이닝별 득점·R/H/E/B), 타자별 타석 결과/타율, 투수별 이닝·투구수·탈삼진, 아웃/주자/카운트, 승리·패전 투수를
    /// 계산한다. 엔진 상태를 전혀 건드리지 않는 순수 함수형 계산이라 재생 위치(커서) 어디에서든 다시 만들 수 있다.
    ///
    /// 집계 규칙(엔진이 주지 않는 값의 처리):
    ///   - 아웃: 삼진/땅볼/뜬공 = 1아웃, RunnerAdvance(ToBase == -1) = 병살 추가 아웃(PlayEvent.Outs 스냅샷 대신 결과로 센다).
    ///   - 투구수: 연출용 볼/스트라이크(Balls+Strikes) + 인플레이 타구 1구(삼진/볼넷은 마지막 공이 이미 카운트에 포함).
    ///   - E(실책)·도루: 엔진에 개념이 없어 항상 0. B = 볼넷.
    ///   - 승리/패전 투수: 마지막 리드 변경 시점의 (리드를 잡은 팀 현재 투수 / 그 타석 상대 투수). 선발 5이닝 규정은 적용하지 않는다.
    /// </summary>
    public class CompyaGameTracker
    {
        public class BatLine
        {
            public int AtBats, Hits, Walks, HomeRuns, Sacrifices;
            public readonly List<AtBatResult> Results = new List<AtBatResult>();
            /// <summary>[TASK-KBO-180] 뱃지 표기(희생번트 등 작전 결과 포함)와 색(출루/진루 = true).</summary>
            public readonly List<(string Label, bool Positive)> Badges = new List<(string, bool)>();
            /// <summary>[TASK-KBO-184] 현재 하프이닝의 타석 결과 뱃지만 - 공수 교대(HalfInningEnd)마다 비운다. 중계 화면(타순/AT BAT)은
            /// 이 목록만 그리고, 경기 전체 누적(Badges/Results)은 기록 팝업에서만 쓴다.</summary>
            public readonly List<(string Label, bool Positive)> InningBadges = new List<(string, bool)>();
            public float Average => AtBats == 0 ? 0f : (float)Hits / AtBats;
        }

        public class PitchLine
        {
            public int Outs, Pitches, Strikeouts, Runs;
        }

        public class TeamLine
        {
            public readonly List<int> Runs = new List<int>();
            public int R, H, B, HR, K, DP;
            public int SB, CS; // [TASK-KBO-180] 도루 성공/실패(승부처 도루 작전)
        }

        public readonly TeamLine Away = new TeamLine();
        public readonly TeamLine Home = new TeamLine();
        public readonly Dictionary<Player, BatLine> Bat = new Dictionary<Player, BatLine>();
        public readonly Dictionary<Player, PitchLine> Pitch = new Dictionary<Player, PitchLine>();
        public readonly List<Player> AwayPitchers = new List<Player>();
        public readonly List<Player> HomePitchers = new List<Player>();

        public int Inning = 1;
        public bool IsTopHalf = true;
        public int Outs;
        public int Balls, Strikes;
        public readonly bool[] Bases = new bool[4];
        public int AwayScore, HomeScore;
        public PlayEvent LastAtBat;
        public bool GameOver;
        public Player WinningPitcher, LosingPitcher;

        public BatLine BatOf(Player player)
        {
            if (player == null) return new BatLine();
            if (!Bat.TryGetValue(player, out var line)) Bat[player] = line = new BatLine();
            return line;
        }

        public PitchLine PitchOf(Player player)
        {
            if (player == null) return new PitchLine();
            if (!Pitch.TryGetValue(player, out var line)) Pitch[player] = line = new PitchLine();
            return line;
        }

        public bool HasRunnerInScoringPosition => Bases[2] || Bases[3];

        public static bool IsHit(AtBatResult r) => r == AtBatResult.Single || r == AtBatResult.Double || r == AtBatResult.Triple || r == AtBatResult.HomeRun;
        public static bool IsOut(AtBatResult r) => r == AtBatResult.Strikeout || r == AtBatResult.Groundout || r == AtBatResult.Flyout;

        /// <summary>events[0..lastIndexInclusive]를 집계한다(lastIndexInclusive &lt; 0이면 경기 시작 전 상태).</summary>
        public static CompyaGameTracker Build(IReadOnlyList<PlayEvent> events, int lastIndexInclusive)
        {
            var t = new CompyaGameTracker();
            if (events == null) return t;

            int leader = 0; // -1 원정 리드, 1 홈 리드, 0 동점
            Player currentPitcher = null;
            Player awayCurrentPitcher = null, homeCurrentPitcher = null;
            bool halfEnded = false;

            for (int i = 0; i <= lastIndexInclusive && i < events.Count; i++)
            {
                var e = events[i];
                if (e == null) continue;

                if (halfEnded && e.Type != PlayEventType.GameEnd)
                {
                    // 공수 교대 후 첫 이벤트에서 다음 하프이닝으로 넘어간다.
                    halfEnded = false;
                    t.Outs = 0;
                    t.Balls = t.Strikes = 0;
                    for (int b = 0; b < 4; b++) t.Bases[b] = false;
                }

                switch (e.Type)
                {
                    case PlayEventType.AtBatResult:
                    {
                        t.Inning = e.Inning;
                        t.IsTopHalf = e.IsTopHalf;
                        var team = e.IsTopHalf ? t.Away : t.Home;
                        while (team.Runs.Count < e.Inning) team.Runs.Add(0);
                        team.Runs[e.Inning - 1] += e.RunsScoredThisPlay;
                        team.R += e.RunsScoredThisPlay;

                        var bat = t.BatOf(e.Batter);
                        bat.Results.Add(e.Result);
                        bool sacrifice = e.Tactic == MatchTactic.Bunt && e.Result == AtBatResult.Groundout;
                        var badge = sacrifice ? ("희생번트", true) : (ResultLabel(e.Result), IsPositive(e.Result));
                        bat.Badges.Add(badge);
                        bat.InningBadges.Add(badge);
                        if (e.Result == AtBatResult.Walk) { bat.Walks++; team.B++; }
                        else if (sacrifice) bat.Sacrifices++; // 희생번트는 타수에 넣지 않는다
                        else bat.AtBats++;
                        if (IsHit(e.Result)) { bat.Hits++; team.H++; }
                        if (e.Result == AtBatResult.HomeRun) { bat.HomeRuns++; team.HR++; }
                        if (e.Result == AtBatResult.Strikeout) team.K++;

                        currentPitcher = e.Pitcher;
                        if (e.Pitcher != null)
                        {
                            var list = e.IsTopHalf ? t.HomePitchers : t.AwayPitchers;
                            if (!list.Contains(e.Pitcher)) list.Add(e.Pitcher);
                            if (e.IsTopHalf) homeCurrentPitcher = e.Pitcher; else awayCurrentPitcher = e.Pitcher;
                        }
                        var pitch = t.PitchOf(e.Pitcher);
                        pitch.Pitches += e.Balls + e.Strikes + (e.Result == AtBatResult.Strikeout || e.Result == AtBatResult.Walk ? 0 : 1);
                        if (e.Result == AtBatResult.Strikeout) pitch.Strikeouts++;
                        pitch.Runs += e.RunsScoredThisPlay;
                        if (IsOut(e.Result)) { pitch.Outs++; t.Outs++; }

                        t.Balls = e.Balls;
                        t.Strikes = e.Strikes;
                        t.AwayScore = e.AwayScore;
                        t.HomeScore = e.HomeScore;
                        t.LastAtBat = e;

                        int newLeader = e.AwayScore > e.HomeScore ? -1 : e.AwayScore < e.HomeScore ? 1 : 0;
                        if (newLeader != leader)
                        {
                            leader = newLeader;
                            if (newLeader == 0)
                            {
                                t.WinningPitcher = t.LosingPitcher = null;
                            }
                            else
                            {
                                // 리드를 잡은 팀 = 지금 공격한 팀. 그 팀의 "현재 투수"가 아직 없으면(1회초 득점 등) 나중에 첫 투수로 채운다.
                                t.WinningPitcher = newLeader == -1 ? awayCurrentPitcher : homeCurrentPitcher;
                                t.LosingPitcher = e.Pitcher;
                            }
                        }
                        break;
                    }

                    case PlayEventType.RunnerAdvance:
                        if (e.FromBase >= 1 && e.FromBase <= 3) t.Bases[e.FromBase] = false;
                        if (e.ToBase >= 1 && e.ToBase <= 3) t.Bases[e.ToBase] = true;
                        if (e.IsSteal)
                        {
                            // [TASK-KBO-180] 타석 전 도루: 성공 = 도루, 실패 = 도루자(아웃, 병살 아님). 아직 이 타석 투수가 정해지기 전이라
                            // 같은 타석 번호의 AtBatResult에서 투수를 찾아 아웃을 준다.
                            var team = e.IsTopHalf ? t.Away : t.Home;
                            if (e.ToBase == -1)
                            {
                                t.Outs++;
                                team.CS++;
                                t.PitchOf(FindPitcherOfPlateAppearance(events, i, e.PlateAppearance) ?? currentPitcher).Outs++;
                            }
                            else team.SB++;
                        }
                        else if (e.ToBase == -1)
                        {
                            t.Outs++;
                            t.PitchOf(currentPitcher).Outs++;
                            (e.IsTopHalf ? t.Away : t.Home).DP++;
                        }
                        break;

                    case PlayEventType.HalfInningEnd:
                    {
                        var team = e.IsTopHalf ? t.Away : t.Home;
                        while (team.Runs.Count < e.Inning) team.Runs.Add(0);
                        halfEnded = true;
                        // [TASK-KBO-184] 이닝(공수 교대) 종료 - 이전 하프이닝 타석 결과 잔상(삼진/뜬공/안타 뱃지)을 비운다.
                        foreach (var line in t.Bat.Values) line.InningBadges.Clear();
                        break;
                    }

                    case PlayEventType.GameEnd:
                        t.GameOver = true;
                        t.AwayScore = e.AwayScore;
                        t.HomeScore = e.HomeScore;
                        break;
                }
            }

            if (halfEnded && !t.GameOver)
            {
                // 마지막으로 읽은 이벤트가 공수 교대면 다음 하프이닝 시작 상태(주자/아웃/카운트 초기화)로 보여 준다.
                t.Outs = 0;
                t.Balls = t.Strikes = 0;
                for (int b = 0; b < 4; b++) t.Bases[b] = false;
            }

            if (t.WinningPitcher == null && leader != 0)
            {
                var winners = leader == -1 ? t.AwayPitchers : t.HomePitchers;
                if (winners.Count > 0) t.WinningPitcher = winners[0];
            }
            return t;
        }

        private static Player FindPitcherOfPlateAppearance(IReadOnlyList<PlayEvent> events, int from, int plateAppearance)
        {
            for (int j = from + 1; j < events.Count && j < from + 6; j++)
            {
                if (events[j].Type == PlayEventType.AtBatResult && events[j].PlateAppearance == plateAppearance) return events[j].Pitcher;
            }
            return null;
        }

        /// <summary>이닝 표기 "2/3", "5", "5 1/3"(레퍼런스 ON THE MOUND 이닝 칸).</summary>
        public static string FormatInnings(int outs)
        {
            int whole = outs / 3, rest = outs % 3;
            if (rest == 0) return whole.ToString();
            return whole == 0 ? $"{rest}/3" : $"{whole} {rest}/3";
        }

        /// <summary>타율 ".750" / "1.000" 표기.</summary>
        public static string FormatAverage(float average)
        {
            if (average >= 1f) return "1.000";
            return "." + UnityEngine.Mathf.RoundToInt(average * 1000f).ToString("000");
        }

        public static string ResultLabel(AtBatResult result)
        {
            switch (result)
            {
                case AtBatResult.Single: return "안타";
                case AtBatResult.Double: return "2루타";
                case AtBatResult.Triple: return "3루타";
                case AtBatResult.HomeRun: return "홈런";
                case AtBatResult.Walk: return "볼넷";
                case AtBatResult.Strikeout: return "삼진";
                case AtBatResult.Groundout: return "땅볼";
                case AtBatResult.Flyout: return "뜬공";
                default: return "";
            }
        }

        /// <summary>레퍼런스 결과 뱃지 색: 출루(안타/장타/볼넷) 파랑, 아웃(삼진/땅볼/뜬공) 빨강.</summary>
        public static bool IsPositive(AtBatResult result) => !IsOut(result);
    }
}
