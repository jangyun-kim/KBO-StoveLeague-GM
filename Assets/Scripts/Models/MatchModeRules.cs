namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-188] 경기 진행 방식 3종의 규칙 SSOT(단위 테스트 대상).
    ///   - 빠른 진행: 중계 없이 N경기(1/3/5/10 또는 -/+ 지정, 남은 정규시즌 경기 수로 클램프)를 연속 자동 진행한다.
    ///                매 경기는 1경기 진행과 같은 경로(PlayBallController.StartMatch → 스킵 → FinishMatch)를 그대로 탄다 -
    ///                선발 로테이션 · 타 구장 4경기 · 시즌 기록 · 보상이 경기마다 똑같이 처리된다.
    ///   - 하이라이트: (구 풀 플레이 흐름) 전체 이닝 중계 + 승부처(우리 팀 공격·수비의 득점권 주자 상황)에서만 작전 개입, 최대 12회.
    ///   - 풀 플레이: 우리 팀의 매 타석(공격) + 주자가 나간 수비 타석마다 작전 패널(강공/컨택/번트/도루 · 투수 교체/고의4구)을 띄워 전 경기를 직접 지휘.
    ///               경기 중 [하이라이트 전환] / [▶▶ 스킵] 토글로 언제든 바꿀 수 있다.
    /// </summary>
    public static class MatchModeRules
    {
        public enum Mode { Quick = 0, Highlight = 1, Full = 2 }

        /// <summary>빠른 진행 경기 수 프리셋 버튼.</summary>
        public static readonly int[] QuickCountPresets = { 1, 3, 5, 10 };

        /// <summary>하이라이트(구 풀 플레이) 승부처 최대 개입 횟수.</summary>
        public const int HighlightMaxInterventions = 12;

        /// <summary>요청 경기 수를 1 ~ 남은 경기 수로 클램프한다(남은 경기가 없으면 0).</summary>
        public static int ClampQuickCount(int requested, int remainingGames)
        {
            if (remainingGames <= 0) return 0;
            if (requested < 1) return 1;
            return requested > remainingGames ? remainingGames : requested;
        }

        /// <summary>정규시즌 남은 유저 경기 수(포스트시즌 등 정규시즌 밖이면 다음 1경기만).</summary>
        public static int RemainingRegularGames(int playedGames, int totalGames, bool isRegularSeason, bool hasNextFixture)
        {
            if (!hasNextFixture) return 0;
            if (!isRegularSeason) return 1;
            int remaining = totalGames - playedGames;
            return remaining < 0 ? 0 : remaining;
        }

        /// <summary>
        /// 이 타석에서 작전 개입 패널을 띄울지. userInGame = 우리 팀 경기, userBatting = 우리 팀 공격,
        /// scoringPosition = 득점권 주자, anyRunner = 주자 1명 이상, resolved = 이미 개입(또는 건너뛴) 타석 수.
        /// </summary>
        public static bool ShouldIntervene(Mode mode, bool userInGame, bool userBatting, bool scoringPosition, bool anyRunner, int resolved)
        {
            if (!userInGame) return false;
            switch (mode)
            {
                case Mode.Highlight:
                    return resolved < HighlightMaxInterventions && scoringPosition;
                case Mode.Full:
                    return userBatting || anyRunner;
                default:
                    return false;
            }
        }

        public static string ModeName(Mode mode) => mode switch
        {
            Mode.Quick => "빠른 진행",
            Mode.Highlight => "하이라이트",
            _ => "풀 플레이"
        };

        public static string Description(Mode mode) => mode switch
        {
            Mode.Quick => "빠른 진행은 중계 없이 선택한 경기 수만큼 연속으로 결과를 확인합니다.\n선발 로테이션 · 타 구장 · 시즌 기록 · 보상은 경기마다 동일하게 처리됩니다.",
            Mode.Highlight => "하이라이트는 전체 이닝 중계를 보며,\n득점권 승부처에서만 직접 작전으로 개입합니다.",
            _ => "풀 플레이는 우리 팀 매 타석 · 주자 상황 수비마다 작전을 직접 지휘합니다.\n경기 중 [하이라이트 전환] / [▶▶ 스킵]으로 언제든 바꿀 수 있습니다."
        };
    }

    /// <summary>[TASK-KBO-188] 빠른 진행 N경기 연속 진행 집계.</summary>
    public class QuickSeriesSummary
    {
        public int Requested;
        public int Played;
        public int Wins, Draws, Losses;
        public int GoldGained;
        public int TicketsGained;

        public bool IsActive => Played < Requested;

        public void Record(bool won, bool draw)
        {
            Played++;
            if (draw) Draws++;
            else if (won) Wins++;
            else Losses++;
        }

        /// <summary>"5경기 4승 1패 · 획득 자금 +430 영입권" (볼 변동이 있으면 " · 볼 +1,200" 추가).
        /// 경기 보상(관중 수익 = 홈 응원·팬심 배율 적용 영입권)은 MatchRewardManager가 경기마다 지급한 값의 합이다.</summary>
        public string Label
        {
            get
            {
                string record = $"{Played}경기 {Wins}승" + (Draws > 0 ? $" {Draws}무" : "") + $" {Losses}패";
                string gold = GoldGained != 0 ? $" · 볼 {(GoldGained > 0 ? "+" : "")}{GoldGained:N0}" : "";
                return $"{record} · 획득 자금 +{TicketsGained:N0} 영입권{gold}";
            }
        }
    }
}
