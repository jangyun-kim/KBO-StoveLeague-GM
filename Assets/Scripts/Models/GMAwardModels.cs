using System;
using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>[TASK-GM-04] 7대 시상 체계(기획서 1.1~1.7).</summary>
    public enum GMAwardCategory
    {
        KboCeremony = 0,  // 1.1 정규시즌 최고 영예(MVP · 신인상)
        TitleHolder = 1,  // 1.2 타이틀 홀더(투수 6 · 타자 8)
        Defense = 2,      // 1.3 KBO 수비상(9포지션 + 유틸리티)
        GoldenGlove = 3,  // 1.4 골든글러브(10부문)
        Special = 4,      // 1.5 정규시즌 특별상(페어플레이 · 상벌위원회 특별상)
        AllStar = 5,      // 1.6 올스타전 시상
        Monthly = 6,      // 1.7 월간 시상
    }

    /// <summary>[TASK-GM-04] 수상자 한 명(한 부문). PlayerId = Player.InstanceId(승리감독상은 빈 문자열).</summary>
    [Serializable]
    public class GMAwardWinner
    {
        public string AwardId;     // "MVP" · "TITLE_HR" · "DEF_SS" · "GG_OF" · "MONTHLY_MVP" ...
        public string AwardName;   // "KBO MVP" · "홈런상" ...
        public GMAwardCategory Category;
        public string Section;     // 기획서 절 번호 "1.2.2"
        public string PlayerId = "";
        public string PlayerName = "";
        public string TeamCode = "";
        public string Position = "";   // KBO 한글 약칭(투 · 포 · 1 · 2 · 3 · 유 · 좌 · 중 · 우 · 지)
        public double Value;
        public string ValueLabel = ""; // "타율 .345" · "득표율 78.4%"
        public string Note = "";
        public string CareerTag = "";  // CareerAwardIds에 들어간 태그(예: "MVP_2026")

        public bool HasWinner => !string.IsNullOrEmpty(PlayerName);
    }

    /// <summary>[TASK-GM-04] 월간 시상 1회(24경기 구간) - 1.7.1 월간 MVP · 1.7.2 월간 캡스플레이상.</summary>
    [Serializable]
    public class GMMonthlyAwardData
    {
        public int MonthIndex;    // 1~6
        public string MonthLabel; // "3·4월" · "5월" ...
        public int StartGame, EndGame; // 1-기반 경기 번호(1~24, 25~48 ...)
        public GMAwardWinner Mvp = new GMAwardWinner();
        public GMAwardWinner CapsPlay = new GMAwardWinner();
    }

    /// <summary>[TASK-GM-04] 7월 올스타전(드림 vs 나눔) 결과와 1.6 시상 10종.</summary>
    [Serializable]
    public class GMAllStarGameData
    {
        public bool Played;
        public string DateLabel = "";
        public List<string> DreamTeams = new List<string>();
        public List<string> NanumTeams = new List<string>();
        public int DreamScore, NanumScore;
        public bool DreamWon;
        public string Decider = "";    // 동점 시 승부치기(홈런 스윙오프) 설명
        public string Summary = "";
        public List<GMAwardWinner> Awards = new List<GMAwardWinner>();

        public string WinnerLabel => DreamWon ? "드림 올스타" : "나눔 올스타";
        public GMAwardWinner Find(string awardId) => Awards.FirstOrDefault(a => a.AwardId == awardId);
    }

    /// <summary>[TASK-GM-04] 포스트시즌 시리즈 한 개.</summary>
    [Serializable]
    public class GMPostseasonSeries
    {
        public string Round;        // "와일드카드 결정전" ...
        public string HigherCode, LowerCode;
        public int HigherNeeds, LowerNeeds; // 시리즈 승리에 필요한 승수(와일드카드는 4위 1승 어드밴티지)
        public int HigherWins, LowerWins;
        public string WinnerCode = "";
        public List<string> Games = new List<string>(); // "1차전 LG 5 : 3 SAM"

        public string LoserCode => WinnerCode == HigherCode ? LowerCode : HigherCode;
    }

    /// <summary>[TASK-GM-04] 포스트시즌 요약(와일드카드 → 준플레이오프 → 플레이오프 → 한국시리즈).</summary>
    [Serializable]
    public class PostseasonSummaryData
    {
        public bool Completed;
        public List<string> SeedCodes = new List<string>();       // 정규시즌 1~5위
        public List<GMPostseasonSeries> Series = new List<GMPostseasonSeries>();
        public string ChampionCode = "";
        public string RunnerUpCode = "";
        public string KoreanSeriesMvp = "";
        public List<string> FinalRankCodes = new List<string>();  // 최종 순위 1~10위
    }

    /// <summary>[TASK-GM-04] 시상 결과로 바뀐 단장 데이터(Ego · 연봉) 한 줄.</summary>
    [Serializable]
    public class GMAwardDynamicsEntry
    {
        public string PlayerId;
        public string PlayerName;
        public string TeamCode;
        public int EgoBefore, EgoAfter;
        public int SalaryBefore, SalaryAfter;
        public int RaisePercent;
        public string Reason = "";
    }

    /// <summary>
    /// [TASK-GM-04] 한 시즌 시상 묶음 - 인시즌(월간 6회 · 7월 올스타) + 포스트시즌 + 11월 KBO 시상식(MVP · 신인 · 타이틀 14 · 수비 10 · 특별 2)
    /// + 12월 골든글러브(10) + 단장 역학 반영 기록. 세이브(GMLeagueSaveData.Awards)로 직렬화된다.
    /// </summary>
    [Serializable]
    public class SeasonAwardCeremonyBundle
    {
        public int SeasonYear;
        public List<GMMonthlyAwardData> Monthly = new List<GMMonthlyAwardData>();
        public GMAllStarGameData AllStar = new GMAllStarGameData();
        public PostseasonSummaryData Postseason = new PostseasonSummaryData();
        public bool KboCeremonyHeld;
        public List<GMAwardWinner> KboCeremony = new List<GMAwardWinner>();
        public bool GoldenGloveHeld;
        public List<GMAwardWinner> GoldenGlove = new List<GMAwardWinner>();
        public bool DynamicsApplied;
        public List<GMAwardDynamicsEntry> Dynamics = new List<GMAwardDynamicsEntry>();

        public bool IsComplete => Postseason != null && Postseason.Completed && KboCeremonyHeld && GoldenGloveHeld;
        public bool HasAny => (Monthly?.Count ?? 0) > 0 || (AllStar?.Played ?? false) || (Postseason?.Completed ?? false) || KboCeremonyHeld || GoldenGloveHeld;

        public IEnumerable<GMAwardWinner> OfCategory(GMAwardCategory category) => (KboCeremony ?? new List<GMAwardWinner>()).Where(a => a.Category == category);

        /// <summary>시즌 전체 수상(월간 · 올스타 · KBO 시상식 · 골든글러브).</summary>
        public IEnumerable<GMAwardWinner> All()
        {
            foreach (var m in Monthly ?? new List<GMMonthlyAwardData>()) { yield return m.Mvp; yield return m.CapsPlay; }
            foreach (var a in AllStar?.Awards ?? new List<GMAwardWinner>()) yield return a;
            foreach (var a in KboCeremony ?? new List<GMAwardWinner>()) yield return a;
            foreach (var a in GoldenGlove ?? new List<GMAwardWinner>()) yield return a;
        }

        public GMAwardWinner Find(string awardId) =>
            (KboCeremony ?? new List<GMAwardWinner>()).Concat(GoldenGlove ?? new List<GMAwardWinner>()).FirstOrDefault(a => a.AwardId == awardId);
    }
}
