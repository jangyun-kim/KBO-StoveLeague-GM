using System;
using System.Collections.Generic;
using KBOManager.Core;

namespace KBOManager.Simulation
{
    /// <summary>[TASK-GM-13] 단장의 약속 1건(세이브 v23 GMFrontOfficeState.Promises) - 상태 기계 · 이행 검사 시즌(DueYear).</summary>
    [Serializable]
    public class GMPromise
    {
        public string Id = "";
        public string TeamCode = "";
        public string PlayerId = "";
        public string PlayerName = "";
        public GMPromiseKind Kind;
        public GMPromiseState State;
        public string TargetPlayerId = "";   // 동료 재계약 보장 대상
        public string TargetPlayerName = "";
        public int MadeYear;
        public int DueYear;                  // 이 시즌 정규시즌 종료 시 이행 여부를 판정
        public string Source = "";           // 계약 협상실 · 라커룸 사건
        public string ResultNote = "";
    }

    /// <summary>[TASK-GM-13] 동적 사건 1건(세이브 v23 GMFrontOfficeState.DynamicEvents) - 팝업 데이터 + 선택 결과.</summary>
    [Serializable]
    public class GMDynamicEvent
    {
        public string Id = "";
        public GMDynamicEventKind Kind;
        public int Year;
        public string TeamCode = "";
        public string PlayerId = "";          // 주인공(연봉 갈등 타자 · 파벌 리더 A)
        public string PlayerName = "";
        public string RivalId = "";           // 파벌 리더 B
        public string RivalName = "";
        public string MediatorId = "";        // 주장 · 베테랑 중재자
        public string MediatorName = "";
        public string Title = "";
        public string Body = "";
        public List<string> Choices = new List<string>();
        public List<string> ChoiceHints = new List<string>();
        public bool Resolved;
        public int ChosenIndex = -1;
        public string ResultText = "";
        public long Cost;                     // 즉시 보상금(만 원)
    }

    /// <summary>[TASK-GM-13] 동적 사건 선택 결과.</summary>
    public class GMDynamicEventResult
    {
        public bool Applied;
        public bool Success;
        public string Message = "";
        public int LoyaltyDelta, TrustDelta;
        public long BudgetDelta;
        public KBOManager.Services.GMAudioEvent? Audio;
    }

    /// <summary>[TASK-GM-13] 약속 이행 검사 결과 1건.</summary>
    public class GMPromiseVerdict
    {
        public GMPromise Promise;
        public bool Broken;
        public string Message = "";
    }

    /// <summary>
    /// [TASK-GM-11] 시즌 결산 스냅숏 1줄(세이브 v22 GMFrontOfficeState.LastSeasonReview) - 정규시즌 종료 시점 선수 기록.
    /// 연도 전환(AdvancePhase)에서 Stats가 비워지므로 결산 · 협상 근거는 이 스냅숏을 쓴다. Estimated = 시뮬레이션 기록이 아닌 추정치.
    /// </summary>
    [Serializable]
    public class GMSeasonReviewLine
    {
        public string PlayerId = "";
        public string TeamCode = "";
        public bool IsPitcher;
        public bool Estimated;
        public int G, PA, AB, H, Doubles, Triples, HR, BB, SO, SB;
        public int PG, GS, OutsPitched, ER, PSO, PBB, HA, HRA, SV, HLD;
        public int DefG, Chances, Errors;
        public float War, DefScore;

        public int Singles => Math.Max(0, H - Doubles - Triples - HR);
        public double IP => OutsPitched / 3.0;
    }

    /// <summary>[TASK-GM-11] 리포트 지표 1칸 - 값 + 색상 등급 + 한글 평가.</summary>
    public class GMReportMetric
    {
        public string Label = "";
        public double Value;
        public string ValueText = "";
        public GMReportTone Tone = GMReportTone.Neutral;
        public string Verdict = "";
    }

    /// <summary>[TASK-GM-11] 선수별 성과 · 연봉 효율 리포트(기획서 4절 - 협상 근거).</summary>
    public class GMPlayerReport
    {
        public KBOManager.Models.Player Player;
        public string PositionLabel = "";
        public GMSeasonReviewLine Line;
        public GMReportMetric War = new GMReportMetric { Label = "종합 기여도" };          // 게임용 WAR
        public GMReportMetric Production = new GMReportMetric { Label = "타격 생산성" };   // 타자 = 게임용 wRC+ · 투수 = 투수 독립 기여도(FIP 기반)
        public GMReportMetric Defense = new GMReportMetric { Label = "수비 기여도" };
        public GMReportMetric Efficiency = new GMReportMetric { Label = "연봉 효율" };
        public GMReportMetric Replaceability = new GMReportMetric { Label = "대체 불가성" };
        public GMReportMetric Trend = new GMReportMetric { Label = "최근 추세" };
        public GMReportConfidence Confidence = GMReportConfidence.Low;
        public bool SampleWarning;
        public string ConfidenceLabel = "";
        public string SampleNote = "";

        public IEnumerable<GMReportMetric> Metrics
        {
            get { yield return War; yield return Production; yield return Defense; yield return Efficiency; yield return Replaceability; yield return Trend; }
        }
    }

    /// <summary>[TASK-GM-11] 포지션 약점 1건(데이터분석팀장 코멘트).</summary>
    public class GMPositionWeakness
    {
        public string Position = "";       // C · 1B · … · DH · SP · RP
        public string PositionLabel = "";
        public string PlayerName = "";
        public double Production;          // 타격 생산성 또는 투수 독립 기여도(리그 평균 100)
        public double LeagueProduction;    // 같은 포지션 10구단 주전 평균
        public double War;
        public double Gap => Production - LeagueProduction;
        public GMReportTone Tone = GMReportTone.Neutral;
        public string Comment = "";
    }

    /// <summary>[TASK-GM-11] 시즌 결산 리포트(시즌 결산실 · Turn 1).</summary>
    public class GMSeasonSummary
    {
        public int Year;
        public string Source = "";          // 정규시즌 기록 · 직전 시즌 스냅숏 · 추정
        public bool Estimated;
        public int W, D, L, Rank, GamesPlayed;
        public double Pct;
        public string RecordLine = "";
        public readonly List<string> FinanceLines = new List<string>();
        public readonly List<string> StaffLines = new List<string>();
        public long FinanceBalance;
        public GMReportConfidence Confidence = GMReportConfidence.Low;
        public string ConfidenceLabel = "";
        public readonly List<GMPositionWeakness> Weaknesses = new List<GMPositionWeakness>();
        public readonly List<GMPositionWeakness> AllPositions = new List<GMPositionWeakness>();
        public readonly List<GMPlayerReport> Players = new List<GMPlayerReport>();
    }

    /// <summary>[TASK-GM-11] 협상 카드(근거) 1장.</summary>
    public class GMNegotiationCard
    {
        public string Id = "";
        public GMNegotiationCardCategory Category;
        public string Title = "";
        public string Pitch = "";           // 단장이 테이블에 꺼내는 한 줄
        public float ArchetypeMultiplier = 1f;
        public string ArchetypeReaction = "";
        public int ExtraYears;              // 장기계약 보장 카드 = +1년
    }

    /// <summary>[TASK-GM-11] 협상 예측(카드를 고르기 전 표시) - 진행 가능성 · 결과 분포 · 결렬 위험 · 재무팀장 경고.</summary>
    public class GMNegotiationForecast
    {
        public GMNegotiationCard Card;      // null = 카드 없이 기본 제시
        public float Progress;              // 협상 진행 가능성 0.05~0.95
        public float Bonus;                 // 카드 가산(기본 +10%p × 성향 배수)
        public float BreakRisk => 1f - Progress;
        public readonly float[] Distribution = new float[4]; // GMNegotiationOutcome 순서(합 1)
        public readonly int[] Salaries = new int[4];
        public int Years;
        public GMReportTone FinanceTone = GMReportTone.Neutral;
        public string FinanceWarning = "";
    }

    /// <summary>[TASK-GM-11] 계약 협상실 세션 - 대상 선수 · 후보 카드 풀(5~7) · 제시 카드 3장 · 예측.</summary>
    public class GMNegotiationSession
    {
        public KBOManager.Models.Player Player;
        public GMPlayerReport Report;
        public int CurrentSalary, Demand, Years;
        public readonly List<GMNegotiationCard> Pool = new List<GMNegotiationCard>();
        public readonly List<GMNegotiationCard> Offered = new List<GMNegotiationCard>();
        public GMNegotiationForecast Baseline;
        public readonly List<GMNegotiationForecast> Forecasts = new List<GMNegotiationForecast>();
        public readonly List<string> Bonds = new List<string>();
        public bool OnCooldown;
        public string BlockReason = "";
    }

    /// <summary>[TASK-GM-11] 협상 결과.</summary>
    public class GMNegotiationRoomResult
    {
        public bool Success;
        public bool Broken;
        public GMNegotiationOutcome Outcome;
        public int Salary, Years;
        public string Message = "";
        public GMPromise Promise;            // [TASK-GM-13] 이 계약으로 활성화된 약속(없으면 null)
    }
}
