using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;

namespace KBOManager.Simulation
{
    /// <summary>[TASK-GM-02] 리그 플레이 진행 방식(기획서 2절): 한 경기 | 전반기(→ 후반기) | 한 시즌.</summary>
    public enum GMRunMode
    {
        SingleGame = 0,  // 1경기(하루 5경기 중 내 구단 경기 포함 1일)
        FirstHalf = 1,   // 개막 ~ 올스타 브레이크(72경기)
        SecondHalf = 2,  // 73 ~ 144경기
        FullSeason = 3,  // 1 ~ 144경기
    }

    /// <summary>[TASK-GM-02] 구단 시즌 성적(순위표 한 행).</summary>
    [Serializable]
    public class GMTeamRecord
    {
        public string TeamCode;
        public int G, W, D, L, RunsScored, RunsAllowed, Streak; // Streak: +연승 / -연패
        public int TeamHomeRuns;
        /// <summary>[TASK-GM-06] ABS 시즌 누적 - 보더라인 스트라이크 콜 획득(수비) · 루킹 삼진(수비) · 포수 블로킹 세이브 · 볼넷 출루(공격).</summary>
        public int AbsBorderlineCalls, AbsLookingStrikeouts, AbsBlockSaves, AbsWalksDrawn;
        /// <summary>[TASK-GM-03] 최근 경기 흐름(최대 10경기, 오래된 순) - 'W' 승 · 'L' 패 · 'D' 무.</summary>
        public string Recent = "";

        public void PushRecent(char result)
        {
            Recent = (Recent ?? "") + result;
            if (Recent.Length > 10) Recent = Recent.Substring(Recent.Length - 10);
        }

        /// <summary>최근 n경기 흐름 표기 "승 승 패 무 승"(오래된 → 최근).</summary>
        public string RecentLabel(int n = 5)
        {
            string r = Recent ?? "";
            if (r.Length == 0) return "-";
            r = r.Length > n ? r.Substring(r.Length - n) : r;
            return string.Join(" ", r.Select(c => c == 'W' ? "승" : c == 'L' ? "패" : "무"));
        }

        /// <summary>KBO 승률 = 승 / (승 + 패) - 무승부 제외.</summary>
        public double Pct => W + L == 0 ? 0 : (double)W / (W + L);
        public static string PctLabel(double pct) => pct >= 1 ? "1.000" : pct.ToString(".000", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>[TASK-GM-02] 선수 시즌 누적 기록(타자 · 투수 공용). PlayerId = Player.InstanceId.</summary>
    [Serializable]
    public class GMPlayerSeasonStats
    {
        public string PlayerId;
        public string TeamCode;
        public bool IsPitcher;

        // 타격
        public int G, PA, AB, H, Doubles, Triples, HR, RBI, R, SB, BB, SO;
        public int CurrentHitStreak, MaxHitStreak, CurrentHrStreak;
        // 투구
        public int PG, GS, W, L, SV, HLD, OutsPitched, ER, PSO, PBB, HA, HRA;
        // 시뮬레이터가 갱신 시 계산(리그 평균 기반)
        public float BatterWAR, PitcherWAR;

        // [TASK-GM-04] 개인 수비 - 수비 출전 경기 · 처리 기회(실책 제외) · 개인 실책 · 호수비, 포지션별 출전(0~8 = C~DH, 9 = 투수), 수비 기여 점수
        public int DefG, Chances, Errors, FinePlays;
        public int DefRating;
        public int[] PosGames = new int[PositionSlots];
        public float DefensiveScore;
        // [TASK-GM-04] 월간 시상 구간 시작 시점 누적값(이번 달 = 현재 - 스냅샷)
        public float MoWAR;
        public int MoPA, MoH, MoHR, MoRBI, MoSB, MoW, MoSV, MoHLD, MoPSO, MoOuts, MoDefG, MoChances, MoErrors, MoFine;

        public const int PositionSlots = 10;
        public const int PitcherSlot = 9;
        public const int DesignatedHitterSlot = 8;

        public float WAR => IsPitcher ? PitcherWAR : BatterWAR;
        public double TotalChances => Chances + Errors;
        public double FieldingPct => Chances + Errors == 0 ? 1.0 : (double)Chances / (Chances + Errors);

        public int[] Positions => PosGames != null && PosGames.Length == PositionSlots ? PosGames : (PosGames = Resize(PosGames));
        /// <summary>가장 많이 출전한 수비 위치(0~8 = BatterPosition, 9 = 투수). 기록이 없으면 -1.</summary>
        public int PrimarySlot
        {
            get
            {
                var g = Positions;
                int best = -1;
                for (int i = 0; i < g.Length; i++) if (g[i] > 0 && (best < 0 || g[i] > g[best])) best = i;
                return best;
            }
        }

        /// <summary>월간 구간 시작 - 현재 누적값을 스냅샷으로 남긴다.</summary>
        public void SnapshotMonth()
        {
            MoWAR = WAR; MoPA = PA; MoH = H; MoHR = HR; MoRBI = RBI; MoSB = SB; MoW = W; MoSV = SV; MoHLD = HLD; MoPSO = PSO; MoOuts = OutsPitched;
            MoDefG = DefG; MoChances = Chances; MoErrors = Errors; MoFine = FinePlays;
        }

        private static int[] Resize(int[] old)
        {
            var a = new int[PositionSlots];
            if (old != null) Array.Copy(old, a, Math.Min(old.Length, PositionSlots));
            return a;
        }

        public int Singles => H - Doubles - Triples - HR;
        public int TotalBases => Singles + Doubles * 2 + Triples * 3 + HR * 4;
        public double AVG => AB == 0 ? 0 : (double)H / AB;
        public double OBP => PA == 0 ? 0 : (double)(H + BB) / PA;
        public double SLG => AB == 0 ? 0 : (double)TotalBases / AB;
        public double IP => OutsPitched / 3.0;
        public double ERA => OutsPitched == 0 ? 99.99 : ER * 27.0 / OutsPitched;
        public double WHIP => OutsPitched == 0 ? 9.99 : (HA + PBB) * 3.0 / OutsPitched;
        /// <summary>이닝 표기 "152 1/3".</summary>
        public string IPLabel => OutsPitched % 3 == 0 ? $"{OutsPitched / 3}" : $"{OutsPitched / 3} {OutsPitched % 3}/3";
    }

    public enum GMNewsKind { Record = 0, Injury = 1, Return = 2, Weekly = 3, Monthly = 4, Scouting = 5, Trade = 6, Milestone = 7, Season = 8, Award = 9, Postseason = 10, Cheer = 11 }

    /// <summary>[TASK-GM-02] 최신 소식 피드 한 줄(날짜 + 제목 + 본문).</summary>
    [Serializable]
    public class GMNewsItem
    {
        public int GameIndex;
        public string DateLabel;   // "MM/DD/2026"
        public GMNewsKind Kind;
        public string Title;
        public string Body;
        public bool IsUserTeam;
        public bool IsMajor;
    }

    /// <summary>[TASK-GM-02] 개인 성적 TOP 3 한 칸.</summary>
    public class GMLeaderEntry
    {
        public string PlayerId;
        public string Name;
        public string TeamCode;
        public double Value;
        public string ValueLabel;
    }

    /// <summary>[TASK-GM-02] 개인 성적 부문 - 타자 8 · 투수 7(기획서 2.1).</summary>
    public enum GMLeaderCategory
    {
        AVG = 0, HR = 1, RBI = 2, SB = 3, OBP = 4, SLG = 5, BatterWAR = 6, HitStreak = 7,
        ERA = 8, Wins = 9, Strikeouts = 10, Saves = 11, Holds = 12, WHIP = 13, PitcherWAR = 14,
        OPS = 15, // [TASK-GM-08] 출루+장타(포스트시즌 트리 KBO 리더 패널)
    }

    public static class GMLeaderCategories
    {
        public static readonly GMLeaderCategory[] Batter =
            { GMLeaderCategory.AVG, GMLeaderCategory.HR, GMLeaderCategory.RBI, GMLeaderCategory.SB, GMLeaderCategory.OBP, GMLeaderCategory.SLG, GMLeaderCategory.BatterWAR, GMLeaderCategory.HitStreak };
        public static readonly GMLeaderCategory[] Pitcher =
            { GMLeaderCategory.ERA, GMLeaderCategory.Wins, GMLeaderCategory.Strikeouts, GMLeaderCategory.Saves, GMLeaderCategory.Holds, GMLeaderCategory.WHIP, GMLeaderCategory.PitcherWAR };

        public static bool IsPitching(GMLeaderCategory c) => c >= GMLeaderCategory.ERA && c <= GMLeaderCategory.PitcherWAR;
        /// <summary>낮을수록 좋은 부문(ERA · WHIP).</summary>
        public static bool LowerIsBetter(GMLeaderCategory c) => c == GMLeaderCategory.ERA || c == GMLeaderCategory.WHIP;
        /// <summary>규정 타석 · 규정 이닝이 필요한 비율 부문.</summary>
        public static bool NeedsQualification(GMLeaderCategory c) =>
            c == GMLeaderCategory.AVG || c == GMLeaderCategory.OBP || c == GMLeaderCategory.SLG || c == GMLeaderCategory.OPS || c == GMLeaderCategory.ERA || c == GMLeaderCategory.WHIP;

        public static string Label(GMLeaderCategory c)
        {
            switch (c)
            {
                case GMLeaderCategory.AVG: return "타율";
                case GMLeaderCategory.HR: return "홈런";
                case GMLeaderCategory.RBI: return "타점";
                case GMLeaderCategory.SB: return "도루";
                case GMLeaderCategory.OBP: return "출루율";
                case GMLeaderCategory.SLG: return "장타율";
                case GMLeaderCategory.BatterWAR: return "타자 WAR";
                case GMLeaderCategory.HitStreak: return "연속 안타";
                case GMLeaderCategory.ERA: return "평균자책점";
                case GMLeaderCategory.Wins: return "승리";
                case GMLeaderCategory.Strikeouts: return "탈삼진";
                case GMLeaderCategory.Saves: return "세이브";
                case GMLeaderCategory.Holds: return "홀드";
                case GMLeaderCategory.WHIP: return "WHIP";
                case GMLeaderCategory.OPS: return "출루+장타(OPS)";
                default: return "투수 WAR";
            }
        }

        public static double Value(GMLeaderCategory c, GMPlayerSeasonStats s)
        {
            switch (c)
            {
                case GMLeaderCategory.AVG: return s.AVG;
                case GMLeaderCategory.HR: return s.HR;
                case GMLeaderCategory.RBI: return s.RBI;
                case GMLeaderCategory.SB: return s.SB;
                case GMLeaderCategory.OBP: return s.OBP;
                case GMLeaderCategory.SLG: return s.SLG;
                case GMLeaderCategory.BatterWAR: return s.BatterWAR;
                case GMLeaderCategory.HitStreak: return s.MaxHitStreak;
                case GMLeaderCategory.ERA: return s.ERA;
                case GMLeaderCategory.Wins: return s.W;
                case GMLeaderCategory.Strikeouts: return s.PSO;
                case GMLeaderCategory.Saves: return s.SV;
                case GMLeaderCategory.Holds: return s.HLD;
                case GMLeaderCategory.WHIP: return s.WHIP;
                case GMLeaderCategory.OPS: return s.OBP + s.SLG;
                default: return s.PitcherWAR;
            }
        }

        public static string Format(GMLeaderCategory c, double v)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            switch (c)
            {
                case GMLeaderCategory.AVG:
                case GMLeaderCategory.OBP:
                case GMLeaderCategory.SLG:
                case GMLeaderCategory.OPS: return v >= 1 ? v.ToString("0.000", inv) : v.ToString(".000", inv);
                case GMLeaderCategory.ERA:
                case GMLeaderCategory.WHIP:
                case GMLeaderCategory.BatterWAR:
                case GMLeaderCategory.PitcherWAR: return v.ToString("0.00", inv);
                default: return ((int)Math.Round(v)).ToString(inv);
            }
        }
    }

    /// <summary>[TASK-GM-02] 인터럽트 팝업 종류 - 대기록 뉴스 / 내 구단 주전 부상.</summary>
    public enum GMInterruptKind { Record = 0, Injury = 1 }

    public enum GMInterruptChoice { Continue = 0, AutoCallUp = 1, ManualLineup = 2 }

    /// <summary>[TASK-GM-02] 시뮬레이션을 멈추고 띄우는 팝업 1건.</summary>
    public class GMSimInterrupt
    {
        public GMInterruptKind Kind;
        public GMNewsItem News;
        public Player Player;                       // 부상 선수(부상 팝업)
        public string TeamCode;
        public BatterPosition Position;             // 부상 타자 포지션(직접 관리 시 대체 자리)
        public readonly List<Player> ReplacementCandidates = new List<Player>();
    }

    /// <summary>
    /// [TASK-GM-02] TeamChemistryReport 6대 역학 계수를 MatchEngine이 쓰는 형태로 옮긴 값.
    /// TeamPowerModifiers.Chemistry로 전달되며, 엔진은 아래 규칙으로 타석 판정에 반영한다.
    ///   - PowerBonus: 실효 전력 계수(0.80~1.15) → 전 스탯 가산(-4 ~ +3)
    ///   - ClutchHitModifier: 7회 이후 2점 차 이내 득점권 타자 긍정 결과 가중치 × (1 + 값)
    ///   - ErrorRateMultiplier: 수비 팀 실책(범타 → 출루) 확률 = 1.2% × (배수 - 1)
    ///   - DoublePlayRiskMultiplier: 공격 팀 병살 확률 · 득점권 삼진 가중치 배수
    ///   - PitcherEraPenalty: 투수 구위 · 변화 감산(ERA +0.85 → -3)
    ///   - UpsetVulnerabilityChance: 약팀 상대(구단 OVR 3 이상 우위) 경기 전 방심 판정 확률 → 발동 시 그 경기 전 스탯 -4
    /// </summary>
    public class GMChemistryModifiers
    {
        public int PowerBonus;
        public float ClutchHitModifier;
        public float ErrorRateMultiplier = 1f;
        public float DoublePlayRiskMultiplier = 1f;
        public int PitcherStatPenalty;
        public float UpsetVulnerabilityChance;

        public const float BaseErrorChance = 0.012f;

        // [TASK-GM-05] 기본 실책률(GM-04 D.1) - 케미스트리 페널티가 없어도 범타의 일정 비율(수비력 높을수록 감소)이 실책 출루, 치어리더 마운드 응원력이 최대 35% 억제.
        // [TASK-GM-06] GM-05 D.1 현실화 - 0.7% → 2.2%(KBO 팀당 시즌 실책 80~105개 스케일).
        // [TASK-GM-07] 지시서 값 6.2%는 GM-07 로스터(2026 고정)에서 팀당 144경기 119.6개로 실측돼(Logs/uGM07.log 1차) 목표 75~110개를 넘었다
        //             → 실측 비례 보정 4.8%(예상 약 93개). 지시서 의도(팀당 75~110개)를 기준으로 맞췄다(DCL-162).
        public const float DefaultBaseErrorRate = 0.048f;
        public float BaseErrorRate = DefaultBaseErrorRate;
        public float CheerErrorReduction;

        /// <summary>범타 1건이 실책 출루가 될 확률 = 기본 실책률 × (1 - 응원 억제율) + 케미스트리 추가분 1.2% × (배수 - 1).</summary>
        public double ErrorChance => Math.Max(0.0, BaseErrorRate * (1.0 - Math.Max(0f, Math.Min(0.35f, CheerErrorReduction))) + BaseErrorChance * Math.Max(0f, ErrorRateMultiplier - 1f));

        /// <summary>수비 주전 평균 수비력 → 기본 실책률(수비 65 = 2.2%, 수비력 1점당 1/80 증감, 0.6~1.4배).</summary>
        public static float BaseErrorRateFor(double averageDefense) =>
            DefaultBaseErrorRate * (float)Math.Max(0.6, Math.Min(1.4, 1.0 + (65.0 - averageDefense) / 80.0));
        /// <summary>[TASK-GM-06] 이 팀 주전 포수의 ABS 블로킹 · 도루저지 가치(1~99, 기본 50) - 수비 시 볼넷 · 안타 억제.</summary>
        public int AbsCatcherSkill = 50;
        public const int UpsetStatPenalty = 4;
        public const int UpsetOvrMargin = 3;
        /// <summary>[TASK-GM-08] 리그 타격 밸런스(센터링 기준 · 환경 정규화 배수) - 단장 모드 경기면 시뮬레이터가 넣는다. null이면 레거시 확률표.</summary>
        public KBOManager.Engine.GMBattingBalance Batting;

        public static GMChemistryModifiers From(TeamChemistryReport report)
        {
            if (report == null) return null;
            return new GMChemistryModifiers
            {
                PowerBonus = (int)Math.Round((report.EffectivePowerMultiplier - 1f) * 20f, MidpointRounding.AwayFromZero),
                ClutchHitModifier = report.ClutchHitModifier,
                ErrorRateMultiplier = report.ErrorRateMultiplier,
                DoublePlayRiskMultiplier = report.DoublePlayRiskMultiplier,
                PitcherStatPenalty = (int)Math.Round(report.PitcherEraPenalty * 4f, MidpointRounding.AwayFromZero),
                UpsetVulnerabilityChance = report.UpsetVulnerabilityChance,
            };
        }
    }

    // ================================================================== 세이브(v14)

    [Serializable]
    public class GMTeamSaveData
    {
        public string TeamCode;
        public bool IsUserTeam;
        public long Budget;
        public int PayrollCap;
        public List<PlayerSaveData> Roster = new List<PlayerSaveData>();
        public List<Cheerleader> CheerleaderPool = new List<Cheerleader>();
        public int CheerEntrySize = GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_DEFAULT;
        public int ConsecutiveLastPlaceSeasons;
        public bool OwnerPostseasonPressure;
        public string TradeRequestPlayerId;
        public LineupAssignment Lineup = new LineupAssignment();
        public int FanSupport = GMTeamFan.DefaultSupport; // [TASK-GM-04]
        // [TASK-GM-05] 치어리더 자동 로테이션 · 전담 응원 · 홈 흥행 누적
        public bool CheerAutoRotate;
        public List<GMCheerDedication> CheerDedications = new List<GMCheerDedication>();
        public int CheerFanPoints;
        // [TASK-GM-08] v19 - 퓨처스 핵심 유망주 풀 · 익명 육성 슬롯(구버전 세이브는 빈 목록 → 로드 시 GMRosterTiers.BackfillFutures)
        public List<PlayerSaveData> Futures = new List<PlayerSaveData>();
        public int DevelopmentSlots = GMRosterTiers.DefaultDevelopmentSlots;
    }

    /// <summary>[TASK-GM-07] 내 구단 정규시즌 경기 결과 한 줄(시즌 일정 캘린더 - 날짜 칸에 결과 스코어 표시).</summary>
    [Serializable]
    public class GMGameResultEntry
    {
        public int Day;
        public string OpponentCode;
        public bool Home;
        public int My, Their;

        public string Label => My > Their ? $"승 {My}-{Their}" : My < Their ? $"패 {My}-{Their}" : $"무 {My}-{Their}";
    }

    /// <summary>[TASK-GM-04] 구단 팬 지지율(0~100) - 내 구단 선수 월간 수상 시 보너스.</summary>
    public static class GMTeamFan
    {
        public const int DefaultSupport = 50;
        public const int MonthlyAwardBonus = 3;
        public static int Clamp(int v) => Math.Max(0, Math.Min(100, v));
    }

    /// <summary>[TASK-GM-02] 단장 모드 리그 저장 데이터(GameSaveData.GMLeague). HasData가 false면 단장 모드 미시작(구버전 세이브 포함).</summary>
    [Serializable]
    public class GMLeagueSaveData
    {
        public bool HasData;
        public GMStartMode Mode;
        public int SeasonYear = GMFeatureFlags.DEFAULT_START_YEAR;
        public GMSeasonPhase Phase;
        public string SelectedTeamCode;
        public bool UseVirtualNames;
        public int GamesPlayed;
        public int Seed;
        public List<GMTeamSaveData> Teams = new List<GMTeamSaveData>();
        public List<PlayerSaveData> FreeAgents = new List<PlayerSaveData>();
        public List<GMTeamRecord> Records = new List<GMTeamRecord>();
        public List<GMPlayerSeasonStats> Stats = new List<GMPlayerSeasonStats>();
        public List<GMNewsItem> News = new List<GMNewsItem>();
        public List<GMMatchBoxScoreData> RecentUserBoxScores = new List<GMMatchBoxScoreData>(); // [TASK-GM-03] 최근 10경기(최신순)
        public SeasonAwardCeremonyBundle Awards = new SeasonAwardCeremonyBundle();                       // [TASK-GM-04] 이번 시즌 시상
        public List<SeasonAwardCeremonyBundle> AwardsHistory = new List<SeasonAwardCeremonyBundle>();   // [TASK-GM-04] 역대 시즌 수상 기록
        public GMFrontOfficeState FrontOffice = new GMFrontOfficeState();                               // [TASK-GM-06] 프런트 오피스
        public List<PlayerSaveData> DraftPool = new List<PlayerSaveData>();                             // [TASK-GM-06] 신인 드래프트 유망주
        public List<GMGameResultEntry> UserResults = new List<GMGameResultEntry>();                     // [TASK-GM-07] 내 구단 경기 결과(시즌 일정)
        // [TASK-GM-08] v19 - FA 원 소속 · 등급, 보상 정산 대기, 자동 보호(올해 FA 계약 · 신인), 단장 수동 보호 명단
        public List<GMFaOriginEntry> FAOrigins = new List<GMFaOriginEntry>();
        public List<GMPendingCompensation> PendingCompensations = new List<GMPendingCompensation>();
        public List<string> FASignedThisYear = new List<string>();
        public List<string> RookiesThisYear = new List<string>();
        public List<string> UserProtectedIds = new List<string>();
        public List<GMTournamentRecord> Tournaments = new List<GMTournamentRecord>(); // [TASK-GM-09] v20 글로벌 대회
    }
}
