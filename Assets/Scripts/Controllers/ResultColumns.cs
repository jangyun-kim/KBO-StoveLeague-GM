using KBOManager.Models;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-186] 경기 종료 결과 화면의 좌/우 열 규칙(순수 로직). 좌측 열 = AWAY 팀, 우측 열 = HOME 팀을 위(전광판 · 점수 카드)부터
    /// 아래(경기 결과 막대 · 승/패 투수 카드)까지 일관되게 쓴다. verdict: -1 = AWAY 승, 1 = HOME 승, 0 = 무승부.
    /// </summary>
    public static class ResultColumns
    {
        public static bool LeftWins(int verdict) => verdict == -1;
        public static bool RightWins(int verdict) => verdict == 1;

        /// <summary>그 열 팀의 결정 투수(이긴 팀 = 승리 투수, 진 팀 = 패전 투수).</summary>
        public static Player PitcherFor(bool awayColumn, int verdict, Player winningPitcher, Player losingPitcher)
        {
            if (verdict == 0) return null;
            bool columnWon = awayColumn ? LeftWins(verdict) : RightWins(verdict);
            return columnWon ? winningPitcher : losingPitcher;
        }

        public static string SeasonRecord(int wins, int losses) => $"시즌 {wins}승 {losses}패";
    }
}
