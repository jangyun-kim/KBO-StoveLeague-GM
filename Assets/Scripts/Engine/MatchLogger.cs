using System;
using KBOManager.Models;

namespace KBOManager.Engine
{
    /// <summary>
    /// 타석 결과를 "9회초 홍길동, 좌월 홈런!" 같은 중계 텍스트로 변환한다.
    /// 순수 텍스트 생성만 담당하며 시뮬레이션 결과(확률/승패)에는 전혀 관여하지 않는다 - 그래서 방향
    /// 플레이버(좌월/중월/우월) 추첨용 난수는 MatchEngine의 결과 결정용 random과 완전히 분리된 별도
    /// 인스턴스를 쓴다.
    /// </summary>
    public static class MatchLogger
    {
        private static readonly string[] Directions = { "좌월", "중월", "우월" };
        private static readonly Random cosmeticRandom = new Random();

        /// <summary>
        /// inning/isTopHalf/batter/result/runsScored로 한 줄짜리 중계 로그를 만든다.
        /// batter나 그 Template이 없으면 "선수"로 대체해 예외 없이 항상 문자열을 반환한다.
        /// </summary>
        public static string BuildLog(int inning, bool isTopHalf, Player batter, AtBatResult result, int runsScored)
        {
            string inningLabel = $"{inning}회{(isTopHalf ? "초" : "말")}";
            string batterName = batter?.Template?.PlayerName ?? "선수";
            string action = DescribeResult(result);

            string log = $"{inningLabel} {batterName}, {action}";

            if (runsScored > 0 && IsHitOutcome(result))
            {
                log += runsScored == 1 ? " (1타점)" : $" ({runsScored}타점)";
            }

            return log;
        }

        private static string DescribeResult(AtBatResult result) => result switch
        {
            AtBatResult.Strikeout => "삼진",
            AtBatResult.Groundout => "땅볼 아웃",
            AtBatResult.Flyout => "뜬공 아웃",
            AtBatResult.Walk => "볼넷 출루",
            AtBatResult.Single => "안타",
            AtBatResult.Double => $"{RandomDirection()} 2루타",
            AtBatResult.Triple => $"{RandomDirection()} 3루타",
            AtBatResult.HomeRun => $"{RandomDirection()} 홈런!",
            _ => "결과 없음"
        };

        private static bool IsHitOutcome(AtBatResult result) =>
            result == AtBatResult.Single || result == AtBatResult.Double
            || result == AtBatResult.Triple || result == AtBatResult.HomeRun;

        private static string RandomDirection() => Directions[cosmeticRandom.Next(Directions.Length)];
    }
}
