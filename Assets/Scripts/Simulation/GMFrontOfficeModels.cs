using System;
using System.Collections.Generic;

namespace KBOManager.Simulation
{
    /// <summary>[TASK-GM-06] OOTP 27식 4단계 난이도 - FA 요구액 배수 · AI 트레이드 요구 가치 · 구단주 시작 신임도가 달라진다.</summary>
    public enum GMDifficulty
    {
        Minors = 0,      // 퓨처스(이지)
        Majors = 1,      // KBO 정규(노멀, 기본)
        AllStar = 2,     // 올스타(하드)
        HallOfFame = 3,  // 명예의 전당(전설)
    }

    public enum GMGoalPriority { Low = 0, Average = 1, High = 2, VeryHigh = 3 }

    /// <summary>[TASK-GM-06] 구단주 목표 6종(기획 지시서 1.1-2).</summary>
    public enum GMGoalKind
    {
        WinningRecord = 0,   // ① 시즌 목표 승률/순위
        UpgradePosition = 1, // ② 취약 포지션(ABS 수비형 포수 · 센터라인) 업그레이드
        AwardWinner = 2,     // ③ 골든글러브 · 타이틀 홀더 배출
        Attendance = 3,      // ④ 홈 평균 관중(치어리더 홈 흥행력 직결)
        Payroll = 4,         // ⑤ 페이롤 예산 준수
        Championship = 5,    // ⑥ 장기 한국시리즈 우승
    }

    /// <summary>[TASK-GM-06] 구단주 [건의(DISCUSS)] 선택지 - 신임도를 소모해 목표 완화 · 추가 예산 · 트레이드 한도 해제를 얻는다.</summary>
    public enum GMDiscussOption { RelaxGoal = 0, ExtraBudget = 1, LiftTradeLimit = 2 }

    /// <summary>[TASK-GM-06] 스토리 캠페인 4종 멀티 엔딩.</summary>
    public enum GMEnding
    {
        None = 0,
        MiracleAutumn = 1,      // 기적의 가을야구
        SuccessfulRebuild = 2,  // 성공적 리빌딩
        DeficitFired = 3,       // 적자 해임
        PerformanceFired = 4,   // 성적 부진 해임
    }

    /// <summary>[TASK-GM-06] 구단주 정보(OWNER INFORMATION). 0~2 단계 값은 GMFrontOffice의 라벨 함수로 표시한다.</summary>
    [Serializable]
    public class GMOwnerProfile
    {
        public string Company = "";      // 모기업
        public string OwnerName = "";    // 구단주(가상 인물)
        public int Patience = 1;         // 0 참을성 없음 · 1 보통 · 2 인내
        public int Fiscal = 1;           // 0 긴축 · 1 보통 · 2 아낌없는 투자
        public int Involvement = 1;      // 0 낮음 · 1 보통 · 2 높음
        public int Priority;             // 0 성적 · 1 흑자 · 2 팬심
        public float ExpectedPct = 0.5f; // 시즌 기대 승률
        public int ExpectedRank = 5;     // 시즌 기대 순위
    }

    /// <summary>[TASK-GM-06] 구단주 목표 한 줄(OWNER GOALS 테이블).</summary>
    [Serializable]
    public class GMOwnerGoal
    {
        public string Id = "";
        public GMGoalKind Kind;
        public int YearIssued;
        public int TargetYear;
        public GMGoalPriority Priority;
        public string Category = "";      // 목표 분류
        public string Description = "";   // 상세 목표 내용
        public string Progress = "";      // 현재 진행 상황
        public bool OnTrack;              // 초록(달성 · 순항) / 빨강(미달)
        public bool Met;                  // 목표 연도 결산 시 달성
        public bool Discussed;            // 올해 이미 건의함
        public int TargetValue;           // 승률(x1000) · 관중 · 페이롤(만 원) · OVR 등
        public string TargetPosition = ""; // 업그레이드 대상 포지션(C · SS · CF ...)
    }

    /// <summary>[TASK-GM-06] 구단 시즌 이력(RECORD HISTORY · ATTENDANCE HISTORY).</summary>
    [Serializable]
    public class GMSeasonHistoryEntry
    {
        public string TeamCode = "";
        public int Year;
        public int W, D, L;
        public int Rank;
        public int AttendancePerGame;
        public bool Postseason;
        public bool Champion;

        public double Pct => W + L == 0 ? 0 : (double)W / (W + L);
    }

    /// <summary>[TASK-GM-06] 스토리 안건 진행 상태(정의는 GMFrontOffice.AgendaDefs).</summary>
    [Serializable]
    public class GMAgendaState
    {
        public string Id = "";
        public int YearRaised;
        public bool Resolved;
        public int ChosenOption = -1;
    }

    /// <summary>[TASK-GM-06] 프런트 오피스 상태(세이브 v17 GMLeagueSaveData.FrontOffice).</summary>
    [Serializable]
    public class GMFrontOfficeState
    {
        public GMOwnerProfile Owner = new GMOwnerProfile();
        public int OwnerTrust = 60;  // 구단주 신임도 0~100
        public List<GMOwnerGoal> Goals = new List<GMOwnerGoal>();
        public GMDifficulty Difficulty = GMDifficulty.Majors;
        public int HouseRuleMaxFA;      // 연간 FA 영입 한도(0 = 제한 없음)
        public int HouseRuleMaxTrades;  // 연간 트레이드 한도(0 = 제한 없음)
        public int FASigningsThisYear, TradesThisYear, DraftPicksThisYear, ExtraTradeAllowance;
        public long ScoutingSpent;
        public List<GMSeasonHistoryEntry> History = new List<GMSeasonHistoryEntry>();
        public List<GMAgendaState> Agendas = new List<GMAgendaState>();
        public GMEnding Ending = GMEnding.None;
        public string EndingNote = "";
        public int EndingYear;
        public bool Initialized;
    }

    /// <summary>[TASK-GM-06] 백분위 랭킹(Percentile Rankings) 한 줄 - KBO 전체 같은 유형(타자/투수) 대비 1~99%.</summary>
    public class GMPercentileRow
    {
        public string Label;
        public int RawValue;
        public int Percentile;
    }

    /// <summary>[TASK-GM-06] 계약 협상 결과.</summary>
    public class GMNegotiationResult
    {
        public bool Success;
        public string Message = "";
        public int TeamworkBefore, TeamworkAfter;
        public int Demand;
        public float Score, RivalBid;
    }

    /// <summary>[TASK-GM-06] Shop a Player 교환 제안 한 줄 / 직접 트레이드 평가.</summary>
    public class GMTradeOffer
    {
        public string TeamCode;
        public KBOManager.Models.Player Player;
        public float Value;
        public float Ratio;
    }

    public class GMTradeEvaluation
    {
        public float GiveValue;      // 상대 구단이 내주는 가치
        public float ReceiveValue;   // 상대 구단이 받는 가치(니즈 보정 포함)
        public float Required;       // 상대 단장이 요구하는 가치(= GiveValue × 난이도 배수)
        public float Ratio => Required <= 0 ? 0f : ReceiveValue / Required; // 1 이상이면 수락
        public bool Acceptable;
        public string Reason = "";
        public string NeedsNote = "";
    }

    public enum GMOfferSort { Ovr = 0, Potential = 1, Salary = 2, Age = 3, Position = 4 }
}
