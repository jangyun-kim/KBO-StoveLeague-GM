using System;
using System.Collections.Generic;
using System.Linq;

namespace KBOManager.Models
{
    /// <summary>
    /// [TASK-KBO-183] 기획서 12단계 리그 체계(루키 + 11단계, 권장 구단 OVR 57~144). 정수값은 세이브(GameSaveData.LeagueTier)와
    /// 1:1이라 순서를 바꾸지 않는다.
    /// </summary>
    public enum LeagueTier
    {
        Rookie = 0,           // 루키 리그(튜토리얼, ~56)
        Amateur = 1,          // 1단계 아마추어 57~64 - 온보딩 직후 초기 구단 안착 구간
        Futures = 2,          // 2단계 퓨처스 65~72
        Pennant = 3,          // 3단계 페넌트 73~80
        Major = 4,            // 4단계 메이저 81~88
        AllStar = 5,          // 5단계 올스타 89~96
        Franchise = 6,        // 6단계 프랜차이즈(구 챌린저) 97~104
        TitleHolder = 7,      // 7단계 타이틀홀더 105~112
        GoldenGlove = 8,      // 8단계 골든글러브 113~120
        WorldClass = 9,       // 9단계 월드클래스 121~128
        SignatureDynasty = 10,// 10단계 시그니처·왕조 129~136
        HallOfFame = 11,      // 11단계 영구결번 137~144
    }

    /// <summary>[TASK-KBO-183] 리그 한 단계의 표시명과 권장 구단 OVR 구간(Min ~ Min+7).</summary>
    public readonly struct LeagueTierInfo
    {
        public readonly LeagueTier Tier;
        public readonly string Name;
        public readonly int MinOvr;
        public readonly int MaxOvr;
        public readonly string Note;

        public LeagueTierInfo(LeagueTier tier, string name, int minOvr, int maxOvr, string note)
        {
            Tier = tier; Name = name; MinOvr = minOvr; MaxOvr = maxOvr; Note = note;
        }

        public string RangeLabel => Tier == LeagueTier.Rookie ? $"~{MaxOvr}" : $"{MinOvr}~{MaxOvr}";
        public bool Contains(int teamOvr) => teamOvr >= MinOvr && teamOvr <= MaxOvr;
    }

    /// <summary>
    /// [TASK-KBO-183] 12단계 리그 마스터 테이블 - 권장 OVR 간격 +8, 리그 내 폭 7(Min ~ Min+7). 1단계 아마추어 57부터 11단계 영구결번 144까지.
    /// 각 리그의 AI 상대 9개 구단 팀 OVR은 권장 구간 안에 하위권(Min~Min+1) 3 / 중위권(Min+2~Min+4) 4 / 우승 경쟁 보스(Min+6~Min+7) 2로 분포한다.
    /// </summary>
    public static class LeagueTierTable
    {
        public const int FirstTierMinOvr = 57;
        public const int TierStep = 8;
        public const int TierWidth = 7;

        /// <summary>AI 9개 구단의 팀 OVR = 리그 Min + 이 오프셋(하위권 0·1·1 / 중위권 2·3·3·4 / 보스 6·7).</summary>
        public static readonly IReadOnlyList<int> AiTeamOvrOffsets = new[] { 0, 1, 1, 2, 3, 3, 4, 6, 7 };

        private static readonly LeagueTierInfo[] Tiers = BuildTiers();

        private static LeagueTierInfo[] BuildTiers()
        {
            var names = new (LeagueTier tier, string name, string note)[]
            {
                (LeagueTier.Rookie, "루키 리그", "튜토리얼 수준"),
                (LeagueTier.Amateur, "아마추어 리그", "온보딩 직후 초기 구단 안착 구간 - 선수 영입 0회"),
                (LeagueTier.Futures, "퓨처스 리그", "초기 영입 진행 · 기초 강화"),
                (LeagueTier.Pennant, "페넌트 리그", "LIVE 에픽 · 초기 정착 선물 활용 구간"),
                (LeagueTier.Major, "메이저 리그", "올스타 · 프랜차이즈 편입"),
                (LeagueTier.AllStar, "올스타 리그", "올스타 풀성장 · 세트덱 중반"),
                (LeagueTier.Franchise, "프랜차이즈 리그", "구 챌린저 리그"),
                (LeagueTier.TitleHolder, "타이틀홀더 리그", "타이틀 홀더 풀성장"),
                (LeagueTier.GoldenGlove, "골든글러브 리그", "골든글러브 편입 · 세트덱 150P+"),
                (LeagueTier.WorldClass, "월드클래스 리그", "상위 시즌 풀성장 + 세트덱 185P+"),
                (LeagueTier.SignatureDynasty, "시그니처·왕조 리그", "종결 카드 + 200P 세트덱"),
                (LeagueTier.HallOfFame, "영구결번 리그", "종결 카드 풀성장 + 풀시너지(최종 144)"),
            };
            var list = new LeagueTierInfo[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int min = i == 0 ? FirstTierMinOvr - TierStep : FirstTierMinOvr + (i - 1) * TierStep;
                list[i] = new LeagueTierInfo(names[i].tier, names[i].name, min, min + TierWidth, names[i].note);
            }
            return list;
        }

        public static IReadOnlyList<LeagueTierInfo> All => Tiers;
        public static LeagueTier Highest => LeagueTier.HallOfFame;

        public static LeagueTierInfo Get(LeagueTier tier)
        {
            int i = (int)tier;
            return Tiers[i < 0 ? 0 : i >= Tiers.Length ? Tiers.Length - 1 : i];
        }

        public static string DisplayName(LeagueTier tier) => Get(tier).Name;

        /// <summary>구단 OVR이 속하는 권장 리그(57 미만 = 루키, 144 초과 = 영구결번).</summary>
        public static LeagueTier RecommendedFor(int teamOvr)
        {
            for (int i = Tiers.Length - 1; i >= 1; i--)
            {
                if (teamOvr >= Tiers[i].MinOvr) return Tiers[i].Tier;
            }
            return LeagueTier.Rookie;
        }

        /// <summary>리그 AI 9개 구단의 목표 팀 OVR(오름차순).</summary>
        public static int[] AiTeamOvrTargets(LeagueTier tier)
        {
            int min = Get(tier).MinOvr;
            return AiTeamOvrOffsets.Select(o => min + o).ToArray();
        }

        public static LeagueTier Next(LeagueTier tier) => tier >= Highest ? Highest : tier + 1;
    }

    /// <summary>
    /// [TASK-KBO-183] 구단 OVR에 더해지는 팀 시너지(세트덱 최대 +13 · 치어리더 6인 최대 +4 = +17)와 최종 OVR 상한(144).
    /// 세트덱 버프 구간표(SetDeckBuffTable, 30P~200P)는 경기 엔진의 세부 스탯 버프로 그대로 쓰이고, 구단/카드 OVR 표기에는
    /// 아래 스코어 → OVR 환산표를 쓴다 - 기본 세트덱(2026 LIVE 27인 ≈ 104~108P)은 +1, 200P 풀 세트덱은 +13이다.
    /// 치어리더는 기본적으로 경기 조건부 효과(CheerSquadEffects)지만, 세트덱 기준 구단과 시너지가 맞는 6인 편성의 등급 단계 합
    /// (LIVE 1 / ICON 2 / LEGEND·시즌 한정 3, 최대 18)을 구단 OVR +0~4로 환산해 반영한다(6인 LEGEND = +4).
    /// </summary>
    public static class TeamSynergyRules
    {
        public const int MaxSetDeckOvr = 13;
        public const int MaxCheerOvr = 4;
        public const int MaxSynergyOvr = MaxSetDeckOvr + MaxCheerOvr; // 17
        public const int MaxFinalOvr = 144;

        /// <summary>세트덱 스코어 → 구단 OVR 가산 단계(도달 구간 수 = 가산치). GenerateKBODatabase.py SETDECK_OVR_THRESHOLDS와 동일.</summary>
        public static readonly IReadOnlyList<int> SetDeckOvrThresholds = new[] { 100, 110, 120, 130, 140, 150, 160, 170, 180, 185, 190, 195, 200 };

        public static int SetDeckOvrBonus(int setDeckScore) => SetDeckOvrThresholds.Count(t => setDeckScore >= t);

        public static int CheerSquadOvrBonus(IReadOnlyList<Cheerleader> slots, Team deckTeam)
        {
            int tierSum = CheerSquad.Filled(slots).Where(c => CheerleaderSynergy.IsActive(c, deckTeam)).Sum(c => CheerSquad.Tier(c.Grade));
            int maxSum = CheerSquad.SlotCount * CheerSquad.Tier(CheerleaderGrade.LEGEND);
            return Math.Min(MaxCheerOvr, tierSum * MaxCheerOvr / maxSum);
        }

        public static int ClampFinal(int ovr) => ovr > MaxFinalOvr ? MaxFinalOvr : ovr;
    }

    /// <summary>
    /// [TASK-KBO-183] 구단 OVR 계산(유저/AI 공용, GameManager.CalculateTeamOVR의 SSOT).
    /// 구단 OVR = round(주전 15인 평균 x 0.8 + 후보 10인 평균 x 0.2) + 세트덱 OVR(+0~13) + 치어리더 OVR(+0~4), 상한 144.
    /// 주전 15 = 포지션별 최고 OVR 타자 9 + 선발투수 + 마무리 / 후보 10 = 나머지 타자 상위 4 + 나머지 구원 상위 6(TASK-031 공식 그대로).
    /// 선수 OVR은 컨디션 중립(Player.CalculateNeutralOVR = 기본 + 성장)이다 - 구단 OVR은 리그 배정과 격차 법칙의 "기본 OVR"이라 일일 컨디션에 흔들리지 않는다.
    /// </summary>
    public static class TeamOvrCalculator
    {
        private const int BenchBatterQuota = 4;
        private const int BenchReliefQuota = 6;

        public static int BaseOvr(IReadOnlyList<Player> roster)
        {
            var valid = (roster ?? Array.Empty<Player>()).Where(p => p?.Template != null).ToList();
            var batters = valid.Where(p => !p.Template.IsPitcher).ToList();
            var pitchers = valid.Where(p => p.Template.IsPitcher).ToList();

            var starterBatters = new List<Player>();
            foreach (BatterPosition position in Enum.GetValues(typeof(BatterPosition)))
            {
                var pick = batters.Where(p => p.Template.BatterPosition == position && !starterBatters.Contains(p))
                    .OrderByDescending(p => p.CalculateNeutralOVR()).FirstOrDefault();
                if (pick != null) starterBatters.Add(pick);
            }
            var benchBatters = batters.Except(starterBatters).OrderByDescending(p => p.CalculateNeutralOVR()).Take(BenchBatterQuota);
            var startingPitchers = pitchers.Where(p => p.Template.PitcherRole == PitcherRole.StartingPitcher).ToList();
            var closers = pitchers.Where(p => p.Template.PitcherRole == PitcherRole.Closer).ToList();
            var benchRelief = pitchers.Except(startingPitchers).Except(closers).OrderByDescending(p => p.CalculateNeutralOVR()).Take(BenchReliefQuota);

            var starters = starterBatters.Concat(startingPitchers).Concat(closers).ToList();
            var bench = benchBatters.Concat(benchRelief).ToList();
            double starterAvg = starters.Count > 0 ? starters.Average(p => p.CalculateNeutralOVR()) : 0;
            double benchAvg = bench.Count > 0 ? bench.Average(p => p.CalculateNeutralOVR()) : 0;
            return (int)Math.Round(starterAvg * 0.8 + benchAvg * 0.2, MidpointRounding.AwayFromZero);
        }

        public readonly struct Breakdown
        {
            public readonly int Base, SetDeck, Cheer, SetDeckScore;
            public Breakdown(int b, int s, int c, int score) { Base = b; SetDeck = s; Cheer = c; SetDeckScore = score; }
            public int Synergy => SetDeck + Cheer;
            public int Total => TeamSynergyRules.ClampFinal(Base + Synergy);
        }

        public static Breakdown Calculate(IReadOnlyList<Player> roster, string favoriteTeam = null, IReadOnlyList<Cheerleader> cheerSlots = null,
            SetDeckSelection selection = null)
        {
            var list = (roster ?? Array.Empty<Player>()).Where(p => p?.Template != null).ToList();
            if (list.Count == 0) return new Breakdown(0, 0, 0, 0);
            var setDeck = SetDeckEvaluator.Evaluate(list, favoriteTeam, selection);
            int cheer = cheerSlots != null ? TeamSynergyRules.CheerSquadOvrBonus(cheerSlots, setDeck.DeckTeam) : 0;
            return new Breakdown(BaseOvr(list), TeamSynergyRules.SetDeckOvrBonus(setDeck.Score), cheer, setDeck.Score);
        }
    }

    /// <summary>
    /// [TASK-KBO-183] 'OVR 7 격차 법칙'. 두 팀의 구단 OVR 차이가 7 이내(ΔOVR ≤ 7)면 가변 승부 구간 - 컨디션, 치어리더 6인 조건부 버프(홈/연패/위기),
    /// 승부처 수동 작전으로 약팀이 강팀을 넘을 수 있다. 8 이상(ΔOVR ≥ 8)이면 체급 구간 - 상위 OVR 구단에 체급 우위 보정(세부 스탯 전 항목 가산)이
    /// 걸려 확실한 우위를 가져간다. MatchEngine이 경기 시작 시 양 팀 구단 OVR(TeamPowerModifiers.TeamOvr)로 판정한다.
    /// </summary>
    public static class OvrGapLaw
    {
        public const int VariableZoneMaxGap = 7;
        public const int DecisiveGap = VariableZoneMaxGap + 1;
        /// <summary>체급 우위 진입 시(Δ8) 상위 구단 세부 스탯 가산 - Δ가 1 늘 때마다 PerExtraGap씩 더하고 MaxBonus에서 멈춘다.</summary>
        public const int EntryStatBonus = 10;
        public const int PerExtraGap = 2;
        public const int MaxStatBonus = 24;

        public static bool IsDecisive(int ovrGap) => Math.Abs(ovrGap) >= DecisiveGap;

        /// <summary>myOvr 팀이 받을 체급 우위 세부 스탯 가산(상위 구단이고 Δ ≥ 8일 때만 양수, 그 외 0).</summary>
        public static int ClassAdvantageBonus(int myOvr, int opponentOvr)
        {
            int gap = myOvr - opponentOvr;
            if (gap < DecisiveGap) return 0;
            int bonus = EntryStatBonus + (gap - DecisiveGap) * PerExtraGap;
            return bonus > MaxStatBonus ? MaxStatBonus : bonus;
        }

        public static string Describe(int myOvr, int opponentOvr)
        {
            int gap = myOvr - opponentOvr;
            if (!IsDecisive(gap)) return $"가변 승부 구간(ΔOVR {Math.Abs(gap)} ≤ {VariableZoneMaxGap}) - 컨디션·치어리더·작전으로 뒤집을 수 있습니다.";
            return gap > 0
                ? $"체급 우위(ΔOVR {gap} ≥ {DecisiveGap}) - 세부 스탯 +{ClassAdvantageBonus(myOvr, opponentOvr)}"
                : $"체급 열세(ΔOVR {-gap} ≥ {DecisiveGap}) - 상대 세부 스탯 +{ClassAdvantageBonus(opponentOvr, myOvr)}";
        }
    }
}
