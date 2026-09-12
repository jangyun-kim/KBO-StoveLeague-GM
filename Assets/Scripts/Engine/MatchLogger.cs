using System;
using System.Collections.Generic;
using KBOManager.Models;

namespace KBOManager.Engine
{
    /// <summary>
    /// 타석 결과를 "[3회초 2사 1,3루] 홍길동 헛스윙 삼진" 같은 중계 텍스트로 변환한다.
    /// 순수 텍스트 생성만 담당하며 시뮬레이션 결과(확률/승패)에는 전혀 관여하지 않는다 - 그래서 방향
    /// 플레이버(좌월/중월/우월) 추첨용 난수는 MatchEngine의 결과 결정용 random과 완전히 분리된 별도
    /// 인스턴스를 쓴다.
    ///
    /// 리치 텍스트(&lt;color&gt;/&lt;b&gt;/&lt;i&gt;)는 Unity UI Text/TextMeshProUGUI 양쪽 모두 동일한
    /// 태그 문법을 지원하므로, InGameUIController가 어느 컴포넌트를 쓰든 그대로 렌더링된다.
    /// </summary>
    public static class MatchLogger
    {
        private static readonly string[] Directions = { "좌월", "중월", "우월" };
        private static readonly Random cosmeticRandom = new Random();

        // 색상은 모바일 화면(밝은/어두운 배경 모두)에서 무난한 대비를 갖는 Material Design 팔레트에서 골랐다.
        // 색만으로 의미를 구분하지 않도록 굵게/기울임 + 텍스트 자체("병살타" 등)로도 항상 이중 전달한다.
        private const string HighlightColor = "#FF5252"; // 홈런/장타/득점
        private const string OutColor = "#448AFF";        // 삼진/병살타

        /// <summary>
        /// situationBeforePlay(타석 진입 시점의 이닝/아웃/주자 상황)와 결과를 조합해 한 줄짜리 중계 로그를 만든다.
        /// isDoublePlay는 결과가 Groundout이면서 실제로 2아웃이 동시에 발생했는지(MatchEngine이 계산)를 전달한다.
        /// </summary>
        public static string BuildLog(MatchState situationBeforePlay, bool isTopHalf, Player batter,
            AtBatResult result, int runsScored, bool isDoublePlay)
        {
            string body = BuildPlainBody(situationBeforePlay, isTopHalf, batter, result, runsScored, isDoublePlay);

            bool isBigPlay = result == AtBatResult.HomeRun || result == AtBatResult.Double
                || result == AtBatResult.Triple || runsScored > 0;
            bool isBigOut = result == AtBatResult.Strikeout || isDoublePlay;

            if (isBigPlay) return Colorize(body, HighlightColor, bold: true, italic: false);
            if (isBigOut) return Colorize(body, OutColor, bold: false, italic: false);
            return body;
        }

        private static string BuildPlainBody(MatchState situationBeforePlay, bool isTopHalf, Player batter,
            AtBatResult result, int runsScored, bool isDoublePlay)
        {
            string inningLabel = $"{situationBeforePlay.Inning}회{(isTopHalf ? "초" : "말")}";
            string outsLabel = $"{situationBeforePlay.Outs}사";
            string runnersLabel = DescribeRunners(situationBeforePlay);
            string situation = runnersLabel != null ? $"{inningLabel} {outsLabel} {runnersLabel}" : $"{inningLabel} {outsLabel}";

            string batterName = batter?.Template?.PlayerName ?? "선수";
            string action = DescribeResult(result, isDoublePlay);

            string body = $"[{situation}] {batterName} {action}";
            if (runsScored > 0 && IsHitOutcome(result))
            {
                body += runsScored == 1 ? " (1타점)" : $" ({runsScored}타점)";
            }

            return body;
        }

        private static string DescribeResult(AtBatResult result, bool isDoublePlay) => result switch
        {
            AtBatResult.Strikeout => "헛스윙 삼진",
            AtBatResult.Groundout => isDoublePlay ? "병살타" : "땅볼 아웃",
            AtBatResult.Flyout => "뜬공 아웃",
            AtBatResult.Walk => "볼넷 출루",
            AtBatResult.Single => "안타",
            AtBatResult.Double => $"{RandomDirection()} 2루타",
            AtBatResult.Triple => $"{RandomDirection()} 3루타",
            AtBatResult.HomeRun => $"{RandomDirection()} 홈런!",
            _ => "결과 없음"
        };

        private static string DescribeRunners(MatchState state)
        {
            var bases = new List<string>();
            if (state.RunnerOnFirst) bases.Add("1");
            if (state.RunnerOnSecond) bases.Add("2");
            if (state.RunnerOnThird) bases.Add("3");

            return bases.Count > 0 ? string.Join(",", bases) + "루" : null;
        }

        private static bool IsHitOutcome(AtBatResult result) =>
            result == AtBatResult.Single || result == AtBatResult.Double
            || result == AtBatResult.Triple || result == AtBatResult.HomeRun;

        private static string RandomDirection() => Directions[cosmeticRandom.Next(Directions.Length)];

        private static string Colorize(string text, string hexColor, bool bold, bool italic)
        {
            string wrapped = text;
            if (bold) wrapped = $"<b>{wrapped}</b>";
            if (italic) wrapped = $"<i>{wrapped}</i>";
            return $"<color={hexColor}>{wrapped}</color>";
        }
    }
}
