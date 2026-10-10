using System;

namespace KBOManager.Models
{
    /// <summary>[TASK-GM-19] 선수 커리어 타임라인 이벤트 종류(프랜차이즈 애착 시스템).</summary>
    public enum GMCareerEventKind
    {
        Draft = 0,             // 신인 드래프트 지명(1차 · 2차)
        FaSigning = 1,         // FA 영입
        Trade = 2,             // 트레이드 이적
        FirstCallUp = 3,       // 첫 1군 콜업
        FirstHit = 4,          // 데뷔 첫 안타
        FirstHomeRun = 5,      // 데뷔 첫 홈런
        FirstWin = 6,          // 데뷔 첫 승리
        Mvp = 7,               // KBO MVP · 한국시리즈 MVP
        GoldenGlove = 8,       // 골든글러브
        Championship = 9,      // 통합 우승 · 한국시리즈 우승 기여
        Contract = 10,         // 연봉 · 재계약 타결
        ContractBreakdown = 11,// 협상 최종 결렬
        SalaryConflict = 12,   // 단장과의 연봉 갈등(삭감 · 동결 타결)
        BondConflict = 13,     // 단장과의 유대 갈등(약속 파기 · 강등 통보)
        PromiseKept = 14,      // 약속 이행(유대 강화)
        Retirement = 15,       // 은퇴
        RetiredNumber = 16,    // 영구결번 지정
        Joined = 17,           // 구단 합류(게임 시작 전 경력 추정)
        Award = 18,            // 그 밖의 주요 수상(신인상 · 타이틀 홀더)
    }

    /// <summary>[TASK-GM-19] 커리어 타임라인 한 줄(선수 세이브에 함께 저장된다). Sim = 인게임 진행 중 생긴 기록(false = 실제 기록 · 추정).</summary>
    [Serializable]
    public class GMCareerEvent
    {
        public int Year;
        public int Day;          // 기록 시점 경기 진행 수(league.GamesPlayed) - 시즌 중 약속 기한 판정용
        public int Kind;
        public string TeamCode = "";
        public string Title = "";
        public string Detail = "";
        public bool Sim = true;

        public GMCareerEventKind EventKind => (GMCareerEventKind)Kind;

        public GMCareerEvent Clone() => new GMCareerEvent { Year = Year, Day = Day, Kind = Kind, TeamCode = TeamCode, Title = Title, Detail = Detail, Sim = Sim };
    }
}
