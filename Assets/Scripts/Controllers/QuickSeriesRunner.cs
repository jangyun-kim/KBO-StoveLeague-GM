using System;
using KBOManager.Engine;
using KBOManager.Models;

namespace KBOManager.Controllers
{
    /// <summary>
    /// [TASK-KBO-188] 빠른 진행 N경기 연속 진행기(순수 C# - CompyaMatchView와 EditMode 실측 테스트가 같은 코드를 쓴다).
    /// 경기 1건 = startOne()(실전: PlayBallController.StartMatch → 중계 스킵 → FinishMatch) → 완료 이벤트를 HandleMatchCompleted로 받아
    /// 전적을 쌓고, 남았으면 schedule(다음 경기 시작)을 부른다. 실전 뷰는 schedule을 "한 프레임 뒤"로 넘겨 보상 등 다른 완료 구독자가
    /// 모두 끝난 뒤 다음 경기를 시작한다. 경기마다 1경기 진행과 100% 같은 경로라 선발 로테이션 · 타 구장 4경기 · 시즌 기록 · 보상이 동일하다.
    /// </summary>
    public sealed class QuickSeriesRunner
    {
        private readonly Action startOne;
        private readonly Func<bool> canContinue;
        private readonly Action<Action> schedule;
        private readonly Func<int> readTickets;
        private readonly Func<int> readGold;
        private int ticketStart, goldStart;

        public QuickSeriesSummary Summary { get; private set; }
        public bool IsRunning => Summary != null && Summary.IsActive;

        /// <summary>연속 진행이 끝났을 때(요청 경기 수 소화 또는 시즌 종료로 조기 종료) 1회.</summary>
        public event Action<QuickSeriesSummary> Finished;

        public QuickSeriesRunner(Action startOne, Func<bool> canContinue, Action<Action> schedule, Func<int> readTickets, Func<int> readGold)
        {
            this.startOne = startOne;
            this.canContinue = canContinue;
            this.schedule = schedule ?? (next => next());
            this.readTickets = readTickets;
            this.readGold = readGold;
        }

        /// <summary>요청 경기 수를 남은 경기 수로 클램프해 시작한다. 진행할 경기가 없으면 false.</summary>
        public bool Begin(int requested, int remainingGames)
        {
            int count = MatchModeRules.ClampQuickCount(requested, remainingGames);
            if (count <= 0) return false;
            Summary = new QuickSeriesSummary { Requested = count };
            ticketStart = readTickets != null ? readTickets() : 0;
            goldStart = readGold != null ? readGold() : 0;
            startOne?.Invoke();
            return true;
        }

        /// <summary>진행 중이 아니면 false(일반 경기 완료) - 호출부가 평소 결과 처리를 한다.</summary>
        public bool HandleMatchCompleted(MatchResult result, Team userTeam)
        {
            if (!IsRunning) return false;
            bool draw = result?.WinnerTeamName == null;
            Summary.Record(!draw && result.WinnerTeamName == userTeam.ToString(), draw);
            RefreshTotals();
            if (Summary.IsActive && (canContinue == null || canContinue()))
            {
                schedule(() =>
                {
                    if (canContinue == null || canContinue()) startOne?.Invoke();
                    else Stop();
                });
                return true;
            }
            Stop();
            return true;
        }

        /// <summary>누적 자금(영입권 · 볼) 재계산 - 보상 지급이 완료 이벤트보다 늦게 와도 맞춘다.</summary>
        public void RefreshTotals()
        {
            if (Summary == null) return;
            if (readTickets != null) Summary.TicketsGained = readTickets() - ticketStart;
            if (readGold != null) Summary.GoldGained = readGold() - goldStart;
        }

        private void Stop()
        {
            if (Summary == null) return;
            Summary.Requested = Summary.Played; // 시즌 종료 등으로 조기 종료한 경우 요청 수를 실제 소화 수로
            RefreshTotals();
            Finished?.Invoke(Summary);
        }

        /// <summary>다른 방식으로 경기를 시작할 때 이전 집계를 버린다.</summary>
        public void Reset() => Summary = null;
    }
}
