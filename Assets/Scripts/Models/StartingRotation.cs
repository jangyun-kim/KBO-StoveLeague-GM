using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-187] 1~5선발 순차 로테이션(유저 · AI 구단 공통). 선발 = 라인업 투수 탭 1~5선발 칸 순서(LineupView.BuildPitchers - 유저 고정 자리 반영),
    /// 오늘 등판 = rotation[치른 경기 수 % 선발 수] → 1경기일 1선발, 2경기일 2선발 … 6경기일 다시 1선발.
    /// NEXT MATCH 예고(결과 화면 · 로비 · 경기 유형 선택)와 실제 경기 엔진 선발(MatchEngine.Home/AwayDesignatedStarter)이 모두
    /// LeagueManager.GetNextStartingPitcher()를 통해 이 규칙 하나만 쓴다.
    /// </summary>
    public static class StartingRotation
    {
        public static List<Player> RotationOf(IEnumerable<Player> roster, LineupAssignment assignment = null)
        {
            var (rotation, _) = LineupView.BuildPitchers(roster, assignment);
            return rotation.Where(e => e.Player != null).Select(e => e.Player).ToList();
        }

        public static int RotationIndex(int gamesPlayed, int rotationSize) =>
            rotationSize <= 0 ? -1 : ((gamesPlayed % rotationSize) + rotationSize) % rotationSize;

        public static Player PickFor(IEnumerable<Player> roster, int gamesPlayed, LineupAssignment assignment = null)
        {
            var rotation = RotationOf(roster, assignment);
            int index = RotationIndex(gamesPlayed, rotation.Count);
            return index < 0 ? null : rotation[index];
        }
    }
}
