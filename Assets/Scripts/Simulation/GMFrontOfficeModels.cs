using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>
    /// [TASK-GM-07] 감독 설정(OOTP 27 「감독 설정」) - 단장 프로필 + 플레이 모드 옵션 5종.
    ///   커미셔너 모드: 트레이드 가치 · FA 경쟁 입찰 판정을 건너뛴다(편집 권한) / 해임당하지 않음: 구단주 신임도 하한 25 /
    ///   거래 하드 모드: AI 단장 요구 가치 +10% / 페넌트 레이스 모드: 중요한 결정만 - 대기록 팝업 없이 진행 /
    ///   챌린지 모드: 연간 FA · 트레이드 1회로 고정.
    /// </summary>
    [Serializable]
    public class GMManagerProfile
    {
        public string Name = "단장";
        public int Role;              // 0 단장 · 1 단장 및 감독
        public bool Commissioner;
        public bool NoFiring;
        public bool HardTrade;
        public bool PennantMode;
        public bool Challenge;

        public string RoleLabel => Role == 1 ? "단장 및 감독" : "단장";
    }

    /// <summary>[TASK-GM-06] 프런트 오피스 상태(세이브 v17 GMLeagueSaveData.FrontOffice).</summary>
    [Serializable]
    public class GMFrontOfficeState
    {
        public GMOwnerProfile Owner = new GMOwnerProfile();
        public GMManagerProfile Manager = new GMManagerProfile(); // [TASK-GM-07] 감독 설정(세이브 v18)
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
        // [TASK-GM-11] 시즌 결산실 - 정규시즌 종료 시 전 구단 선수 기록 스냅숏(연도 전환 후에도 Turn 1 결산 · 협상 근거로 쓴다) · 결산실 첫 열람 연도
        public List<GMSeasonReviewLine> LastSeasonReview = new List<GMSeasonReviewLine>();
        public int LastSeasonReviewYear;
        public int SeasonReviewSeenYear;
        // [TASK-GM-11] 계약 협상실 - 결렬 후 재협상 쿨다운(이번 스토브리그 동안 불가, 해 넘김 시 초기화) · 협상 시도 횟수(카드 재추첨 시드)
        public List<string> NegotiationCooldownIds = new List<string>();
        public int NegotiationAttempts;
        // [TASK-GM-13] 선수단 회의실 - 약속(상태 기계) · 1티어 동적 사건(연봉 갈등 · 라커룸 파벌) · 일련번호
        public List<GMPromise> Promises = new List<GMPromise>();
        public List<GMDynamicEvent> DynamicEvents = new List<GMDynamicEvent>();
        public int PromiseSeq, EventSeq;
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
        public int CashSubsidy;      // [TASK-GM-08] 연봉 보조(만 원)
        public float CashValue;      // [TASK-GM-08] 연봉 보조의 트레이드 가치 환산(받는 가치에 포함)
    }

    public enum GMOfferSort { Ovr = 0, Potential = 1, Salary = 2, Age = 3, Position = 4 }

    // ================================================================== [TASK-GM-08] FA 등급 · 보상 · 보호 명단 · 1:N 역제안

    /// <summary>[TASK-GM-08] KBO FA 등급(2026 규약) - A: 보호 20인 외 1명 + 연봉 200%(또는 300%) · B: 보호 25인 외 1명 + 100%(또는 200%) · C: 연봉 150%.</summary>
    public enum GMFaGrade { A = 0, B = 1, C = 2 }

    /// <summary>[TASK-GM-08] FA 시장 선수의 원 소속 구단 · 등급 · 전년도 연봉(세이브 v19).</summary>
    [Serializable]
    public class GMFaOriginEntry
    {
        public string PlayerId = "";   // Player.InstanceId
        public string TeamCode = "";   // 원 소속 구단(보상을 받는 쪽)
        public GMFaGrade Grade = GMFaGrade.C;
        public int PrevSalary;         // 전년도 연봉(만 원)
    }

    /// <summary>[TASK-GM-08] 정산 대기 중인 FA 보상 1건(영입 구단 → 원 소속 구단).</summary>
    [Serializable]
    public class GMPendingCompensation
    {
        public string PlayerId = "";
        public string PlayerName = "";
        public string FromTeam = "";   // 원 소속(보상 수령)
        public string ToTeam = "";     // 영입 구단(보호 명단 제출 · 보상 지급)
        public GMFaGrade Grade = GMFaGrade.C;
        public int PrevSalary;
        public int Year;
    }

    /// <summary>[TASK-GM-08] 보호 명단 1명 - 10대 가중치 세부 점수(①~⑤ 0~100, ⑥ 노선 배수 반영 후 가중합, ⑦ 희소성 가산, ⑩ 단장 성향 편차).</summary>
    public class GMProtectionEntry
    {
        public KBOManager.Models.Player Player;
        public float Irreplaceability, Potential, Overall, RoleEthic, ContractEfficiency; // ①~⑤
        public float Weighted;       // ①~⑤ × ⑥ 노선 가중치
        public float Scarcity;       // ⑦ 희소성 가산
        public float Personality;    // ⑩ 단장 성향 편차(±3~5)
        public float Score;          // 최종 점수
        public bool AutoProtected;   // ⑧ 자동 보호(당해 신인 · 외국인 · FA 계약 당사자) - 명단 인원에서 제외
        public bool Protected;
        public string Tags = "";     // 희소성 · 자동 보호 사유
    }

    /// <summary>[TASK-GM-08] 구단 보호 명단(20인 또는 25인) 산출 결과.</summary>
    public class GMProtectionList
    {
        public string TeamCode = "";
        public int Size;
        public bool WinNow;                 // ⑥ 우승 도전(true) / 리빌딩(false)
        public string PersonalityLabel = "";
        public int PersonalitySwing;        // ⑩ 3~5
        public float[] Weights = new float[5]; // ①~⑤ 실제 가중치(%)
        public readonly List<GMProtectionEntry> Entries = new List<GMProtectionEntry>();
        public readonly List<string> BalanceNotes = new List<string>(); // ⑨ 최소 전력 검수 교체 기록

        public IEnumerable<GMProtectionEntry> Protected => Entries.Where(e => e.Protected && !e.AutoProtected);
        public IEnumerable<GMProtectionEntry> AutoProtected => Entries.Where(e => e.AutoProtected);
        public IEnumerable<GMProtectionEntry> Unprotected => Entries.Where(e => !e.Protected && !e.AutoProtected);
        public bool IsProtected(KBOManager.Models.Player p) => Entries.Any(e => e.Player == p && (e.Protected || e.AutoProtected));
    }

    /// <summary>[TASK-GM-08] FA 보상 정산 결과.</summary>
    public class GMCompensationResult
    {
        public bool Success;
        public bool CashOnly;
        public KBOManager.Models.Player CompensationPlayer;
        public long Cash;
        public string Message = "";
    }

    /// <summary>[TASK-GM-08] AI 구단 니즈 1건(취약 포지션 · 장타자 부재 · 샐러리캡 감축 등).</summary>
    public class GMTeamNeed
    {
        public string Kind = "";      // SP · RP · C · CF · SS · 2B · POWER · PROSPECT · PAYROLL
        public string Label = "";     // 화면 문구 - "선발투수 부족"
        public float Severity;        // 0~1
    }

    /// <summary>[TASK-GM-08] AI 단장 1:N 역제안 - 상대 핵심 선수 1명 ↔ 내 선수 1~3명(+ 연봉 보조).</summary>
    public class GMTradeCounterOffer
    {
        public string PartnerCode = "";
        public KBOManager.Models.Player Target;
        public readonly List<KBOManager.Models.Player> Requested = new List<KBOManager.Models.Player>();
        public int CashSubsidy;       // 연봉 보조(만 원, 내 구단 예산 → 상대 구단)
        public readonly List<GMTeamNeed> Needs = new List<GMTeamNeed>();
        public string Pitch = "";     // 상대 단장 대사
        public GMTradeEvaluation Evaluation;
        public bool Valid => Target != null && Requested.Count > 0;
    }
}
