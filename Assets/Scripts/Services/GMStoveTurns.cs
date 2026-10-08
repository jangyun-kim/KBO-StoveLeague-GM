using System;
using System.Linq;
using KBOManager.Core;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-14] 스토브리그 8 Turn 진행 통제(기획서 2절 표) - 단계별 해금 · 진행 차단(Phase Gating).
    ///   [TASK-GM-15] Turn 3을 넘기면 2차 드래프트를 마감한다(AI 지명 · GMSecondaryDraft.Conclude). Turn 6 대표 방 = 육성 회의실.
    ///   Turn 1 시즌 결산 → 2 선수단 정리 → 3 2차 드래프트(격년 · 홀수 해, 그 밖 해는 자동 통과) → 4 계약 협상 → 5 시장 협상 → 6 육성 회의 → 7 드래프트 → 8 최종 구성 → 개막 준비 완료.
    ///   - 시작: 새 게임(감독 설정 · 새 시즌 모달)과 연도 전환(GMFrontOffice.OnNewSeason). 세이브 v24(StoveTurn · StoveTurnYear · StoveTurnVisited).
    ///     시작하지 않은 리그(구버전 세이브 · 직접 만든 테스트 리그)는 통제하지 않는다(기존 자유 진행).
    ///   - 각 Turn은 대표 방을 한 번 연 뒤에만 넘길 수 있고, Turn 2는 미해결 라커룸 사건 · 1군 정원 초과, Turn 8은 1군 정원 초과가 남아 있으면 막는다.
    ///   - 방 해금은 누적이다(Turn 1이 끝나야 Turn 4 계약 협상실이 열린다). 해금 전 방은 허브가 잠금 화면 + 실행 차단으로 막는다.
    /// </summary>
    public static class GMStoveTurns
    {
        public const int TurnCount = 8, Completed = TurnCount + 1;

        public static readonly string[] Names = { "시즌 결산", "선수단 정리", "2차 드래프트", "계약 협상", "시장 협상", "육성 회의", "드래프트", "최종 구성" };
        public static readonly string[] Rooms = { "시즌 결산실 · 구단주 보고실", "선수단 회의실 · 보류명단(보호 명단)", "2차 드래프트실(보호 25인 · 지명)", "계약 협상실(재계약 · 연봉)", "시장 정보실(FA · 트레이드)", "육성 회의실(퓨처스 · 콜업 · 멘토링)", "신인 드래프트", "구단주 보고실(최종 로스터 · 목표)" };

        public static string Name(int turn) => turn >= 1 && turn <= TurnCount ? Names[turn - 1] : turn > TurnCount ? "개막 준비 완료" : "-";
        public static string Label(int turn) => turn >= 1 && turn <= TurnCount ? $"Turn {turn}/{TurnCount} {Names[turn - 1]}" : turn > TurnCount ? "스토브리그 완료 · 개막 준비" : "";

        /// <summary>2차 드래프트는 격년(홀수 해 - 2027, 2029 …).</summary>
        public static bool SecondDraftYear(int year) => year % 2 == 1;

        /// <summary>올해 스토브리그 Turn 통제가 켜져 있는지(시작했고 아직 개막 전).</summary>
        public static bool IsActive(GMLeagueState league)
        {
            if (league == null || league.GamesPlayed > 0) return false;
            var fo = GMFrontOffice.Ensure(league);
            return fo.StoveTurnYear == league.SeasonYear && fo.StoveTurn >= 1;
        }

        /// <summary>Turn 1~8 진행 중(방 잠금 · 개막 차단 대상).</summary>
        public static bool IsGating(GMLeagueState league) => IsActive(league) && GMFrontOffice.Ensure(league).StoveTurn <= TurnCount;

        public static int Current(GMLeagueState league) => IsActive(league) ? GMFrontOffice.Ensure(league).StoveTurn : 0;

        public static void Begin(GMLeagueState league)
        {
            if (league == null) return;
            var fo = GMFrontOffice.Ensure(league);
            fo.StoveTurn = 1;
            fo.StoveTurnYear = league.SeasonYear;
            fo.StoveTurnVisited = false;
        }

        /// <summary>이번 Turn의 대표 방을 열었다(허브가 호출).</summary>
        public static void MarkVisited(GMLeagueState league)
        {
            if (IsGating(league)) GMFrontOffice.Ensure(league).StoveTurnVisited = true;
        }

        /// <summary>지금 Turn을 넘길 수 없는 이유(넘길 수 있으면 빈 문자열).</summary>
        public static string BlockReason(GMLeagueState league)
        {
            if (!IsGating(league)) return "";
            var fo = GMFrontOffice.Ensure(league);
            int turn = fo.StoveTurn;
            if (!fo.StoveTurnVisited) return $"Turn {turn} {Names[turn - 1]} - {Rooms[turn - 1]}을(를) 먼저 확인하십시오.";
            var team = league.UserTeam;
            if (team == null) return "";
            if (turn == 2)
            {
                int pending = GMDynamicEventEngine.Pending(league).Count();
                if (pending > 0) return $"Turn 2 선수단 정리 - 라커룸 사건 {pending}건에 먼저 대응하십시오(선수단 회의실).";
            }
            if ((turn == 2 || turn == TurnCount) && team.Roster.Count > GMStoveLeagueMarket.RosterMax)
                return $"Turn {turn} {Names[turn - 1]} - 1군 {team.Roster.Count}명, 정원 {GMStoveLeagueMarket.RosterMax}명 이하로 정리하십시오.";
            return "";
        }

        /// <summary>다음 Turn으로 넘긴다(2차 드래프트가 없는 해는 Turn 3을 건너뛴다). 넘겼으면 true.</summary>
        public static bool Advance(GMLeagueState league, out string message)
        {
            message = "";
            if (!IsGating(league)) { message = IsActive(league) ? "스토브리그 8 Turn을 모두 마쳤습니다 - 개막전으로 진행하십시오." : ""; return false; }
            string reason = BlockReason(league);
            if (reason != "") { message = reason; return false; }
            var fo = GMFrontOffice.Ensure(league);
            int from = fo.StoveTurn;
            string concluded = "";
            if (from == 3 && GMSecondaryDraft.IsDraftYear(league.SeasonYear) && !GMSecondaryDraft.IsHeld(league)) // [TASK-GM-15] Turn 3 마감 = AI 지명 진행 · 시행 처리
                concluded = $" · 2차 드래프트 마감(AI 지명 {GMSecondaryDraft.Conclude(league).Count}건)";
            fo.StoveTurn++;
            string skipped = "";
            if (fo.StoveTurn == 3 && !SecondDraftYear(league.SeasonYear))
            {
                fo.StoveTurn = 4;
                skipped = " (Turn 3 2차 드래프트는 격년 - 올해는 열리지 않습니다)";
            }
            fo.StoveTurnVisited = false;
            message = fo.StoveTurn > TurnCount
                ? $"Turn {from} {Names[from - 1]} 완료 - 스토브리그 8 Turn 종료, 개막 준비 완료."
                : $"Turn {from} {Names[from - 1]} 완료{concluded} → {Label(fo.StoveTurn)}{skipped} · 활성: {Rooms[fo.StoveTurn - 1]}";
            league.AddNews(new GMNewsItem
            {
                GameIndex = 0, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Season, IsUserTeam = true,
                Title = fo.StoveTurn > TurnCount ? $"{league.SeasonYear} 스토브리그 종료 - 개막 준비" : $"스토브리그 {Label(fo.StoveTurn)} 시작", Body = message,
            });
            return true;
        }
    }
}
