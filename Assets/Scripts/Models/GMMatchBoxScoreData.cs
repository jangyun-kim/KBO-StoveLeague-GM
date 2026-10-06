using System;
using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>[TASK-GM-03] 박스스코어 타자 한 줄(타순 · 경기 기록 · 시즌 누적).</summary>
    [Serializable]
    public class BatterBoxScoreLine
    {
        public string PlayerId;
        public string Name;
        public string Position;   // KBO 약칭(포 · 1 · 2 · 3 · 유 · 좌 · 중 · 우 · 지)
        public int Order;         // 타순 1~9
        public int AB, R, H, RBI, BB, SO, Doubles, Triples, HR, SB, GIDP;
        // 시즌 누적(경기 반영 후)
        public double SeasonAVG;
        public int SeasonHR, SeasonRBI, SeasonDoubles, SeasonTriples, SeasonSB;
    }

    /// <summary>[TASK-GM-03] 박스스코어 투수 한 줄. Decision = "W" · "L" · "S" · "H" 또는 빈 문자열.</summary>
    [Serializable]
    public class PitcherBoxScoreLine
    {
        public string PlayerId;
        public string Name;
        public string Decision = "";
        public int Outs, H, R, ER, BB, SO, HR, NP;
        public double SeasonERA;
        public int SeasonW, SeasonL, SeasonSV, SeasonHLD;

        public string IPLabel => Outs % 3 == 0 ? $"{Outs / 3}" : $"{Outs / 3} {Outs % 3}/3";
    }

    /// <summary>[TASK-GM-03] 승리 확률 그래프 한 점(홈 팀 기준 0~1). Index 0 = 경기 전(0.5).</summary>
    [Serializable]
    public class WPAPoint
    {
        public int PlateAppearance;
        public int Inning;
        public bool IsTop;
        public float HomeWinProbability;
    }

    /// <summary>[TASK-GM-03] 결정적 플레이(타석) - DeltaWPA는 공격 팀 기준 승리 확률 변화.</summary>
    [Serializable]
    public class WPAKeyPlay
    {
        public int PlateAppearance;
        public int Inning;
        public bool IsTop;
        public string TeamCode;
        public string BatterName;
        public string Situation;     // "8회말 1사 1,3루"
        public string ResultLabel;   // "2루타" · "홈런" ...
        public int RBI;
        public float DeltaWPA;

        public string Label => $"{Inning}회{(IsTop ? "초" : "말")} - {BatterName}, {Situation} {ResultLabel}{(RBI > 0 ? $" {RBI}타점" : "")} (WPA {(DeltaWPA >= 0 ? "+" : "")}{DeltaWPA * 100f:0.0}%)";
    }

    /// <summary>[TASK-GM-03] 자동 생성 경기 기사(헤드라인 + 4문단).</summary>
    [Serializable]
    public class GameRecapArticle
    {
        public string Headline = "";
        public string Paragraph1 = ""; // 선발 · 승패 투수
        public string Paragraph2 = ""; // 결정적 승부처
        public string Paragraph3 = ""; // 수훈 선수 소감
        public string Paragraph4 = ""; // 차전 예고

        public string Body => string.Join("\n\n", new[] { Paragraph1, Paragraph2, Paragraph3, Paragraph4 }.Where(p => !string.IsNullOrEmpty(p)));
    }

    /// <summary>
    /// [TASK-GM-03] 한 경기 박스스코어(기획서 2.2.2): 이닝별 득점(-1 = 'X', 끝내기/말 공격 생략) · R/H/E · 타자/투수 라인 · 승리 확률 그래프 · 결정적 플레이 · 기사.
    /// 정합성: 이닝 득점 합 = R = 그 팀 타자 득점 합 = 상대 투수 실점 합, H = 타자 안타 합 = 상대 투수 피안타 합, 타자 BB/SO 합 = 상대 투수 BB/SO 합.
    /// </summary>
    [Serializable]
    public class GMMatchBoxScoreData
    {
        public int GameIndex;
        public int SeasonYear;
        public string DateLabel;
        public string Stadium;
        public string HomeCode, AwayCode;
        public string WinnerCode; // 무승부면 빈 문자열
        public List<int> HomeInningRuns = new List<int>();
        public List<int> AwayInningRuns = new List<int>();
        public int HomeR, HomeH, HomeE, AwayR, AwayH, AwayE;
        public List<BatterBoxScoreLine> HomeBatters = new List<BatterBoxScoreLine>();
        public List<BatterBoxScoreLine> AwayBatters = new List<BatterBoxScoreLine>();
        public List<PitcherBoxScoreLine> HomePitchers = new List<PitcherBoxScoreLine>();
        public List<PitcherBoxScoreLine> AwayPitchers = new List<PitcherBoxScoreLine>();
        public List<WPAPoint> WpaPoints = new List<WPAPoint>();
        public List<WPAKeyPlay> KeyPlays = new List<WPAKeyPlay>();
        public GameRecapArticle Recap = new GameRecapArticle();
        /// <summary>[TASK-GM-05] 오늘 단상에 오른 4~6인 응원단 이름 · 활약 요약(내 구단).</summary>
        public List<string> CheerEntryNames = new List<string>();
        public string CheerSummary = "";

        public int Innings => Math.Max(HomeInningRuns.Count, AwayInningRuns.Count);
        public bool IsTie => string.IsNullOrEmpty(WinnerCode);
        public List<BatterBoxScoreLine> BattersOf(bool home) => home ? HomeBatters : AwayBatters;
        public List<PitcherBoxScoreLine> PitchersOf(bool home) => home ? HomePitchers : AwayPitchers;
    }
}
