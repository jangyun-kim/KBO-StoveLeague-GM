using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;
using Random = System.Random; // UnityEngine.Random과의 이름 충돌 방지

namespace KBOManager.Engine
{
    /// <summary>타석 결과.</summary>
    public enum AtBatResult
    {
        Strikeout,
        Groundout,
        Flyout,
        Walk,
        Single,
        Double,
        Triple,
        HomeRun
    }

    /// <summary>한 경기의 스코어보드 결과.</summary>
    public class MatchResult
    {
        public string HomeTeamName;
        public string AwayTeamName;

        public List<int> HomeInningScores = new List<int>(); // 끝내기로 말 공격이 생략되면 마지막 이닝 값이 비어 있을 수 있다
        public List<int> AwayInningScores = new List<int>();

        public int HomeTotalScore;
        public int AwayTotalScore;

        public bool IsExtraInnings;
        public string WinnerTeamName; // null이면 무승부 (연장 상한 도달)
    }

    /// <summary>
    /// 1이닝 1구(타석) 단위로 진행되는 순수 C# 시뮬레이션 엔진. MonoBehaviour를 상속하지 않아
    /// 씬/프레임 오버헤드 없이 다수의 경기를 즉시(백그라운드) 계산할 수 있다.
    /// </summary>
    public class MatchEngine
    {
        private const int RegulationInnings = 9;
        private const int MaxInnings = 12; // KBO 정규시즌 연장 상한. 도달 시 무승부로 종료

        /// <summary>타석 결과가 세부 스탯 중 어떤 "매치업"에 의해 좌우되는지.</summary>
        private enum OutcomeDriver
        {
            Strikeout,      // 투수 구위/구속 vs 타자 정확/선구
            Walk,           // 타자 선구 vs 투수 제구
            ContactQuality, // 타자 정확 vs 투수 변화 (범타로 죽느냐, 안타가 되느냐)
            Power           // 타자 파워 vs 투수 변화 (장타가 되느냐)
        }

        // 타석 결과 기본 확률표(총합 1.0). direction=+1이면 해당 driver의 skill 값이 클수록 확률이 커지고,
        // -1이면 작아진다. OVR/세부 스탯 밸런스가 확정되지 않은 임시값이다.
        private static readonly (AtBatResult result, float baseWeight, OutcomeDriver driver, float direction)[] OutcomeTable =
        {
            (AtBatResult.Strikeout, 0.22f, OutcomeDriver.Strikeout,      +1f),
            (AtBatResult.Walk,      0.08f, OutcomeDriver.Walk,           +1f),
            (AtBatResult.Groundout, 0.23f, OutcomeDriver.ContactQuality, -1f),
            (AtBatResult.Flyout,    0.20f, OutcomeDriver.ContactQuality, -1f),
            (AtBatResult.Single,    0.16f, OutcomeDriver.ContactQuality, +1f),
            (AtBatResult.Double,    0.06f, OutcomeDriver.Power,          +1f),
            (AtBatResult.Triple,    0.01f, OutcomeDriver.Power,          +1f),
            (AtBatResult.HomeRun,   0.04f, OutcomeDriver.Power,          +1f),
        };

        private const float SkillInfluence = 0.6f;     // 매치업 격차가 결과 분포를 얼마나 흔드는지(임시 계수)
        private const float StatDiffNormalizer = 50f;  // 이 스탯 격차를 skill=±1(최대 보정)로 정규화(임시 스케일)

        // 삼진 판정에서 정확/선구가 각각 기여하는 비중(임시값, 총합 1.0)
        private const float ContactWeightInStrikeout = 0.6f;
        private const float DisciplineWeightInStrikeout = 0.4f;

        // GameManager.CheckSetDeckBonus()의 기본값과 동일한 임시 세트덱 규칙.
        // MatchEngine은 씬의 GameManager 싱글톤에 의존하지 않는 순수 C# 클래스이므로 값만 복제해 둔다.
        private const int SetDeckActivationThreshold = 5;
        private const float SetDeckBonusMultiplier = 1.15f;

        private static readonly BatterPosition[] StarterBatterPositions =
            (BatterPosition[])Enum.GetValues(typeof(BatterPosition));

        private readonly SkillDB skillDB;
        private readonly Random random;

        // 스킬이 섞이지 않은 순수 OVR(강화/각성/세트덱만 반영). 투수 로테이션 정렬과, "패기"류 스킬의
        // OVR 비교 조건(GDD: 상대 스킬 효과로 인한 OVR 증가 제외)에 사용한다.
        private readonly Dictionary<Player, int> baseOvrCache = new Dictionary<Player, int>();
        private readonly Dictionary<Player, (bool isActive, float multiplier)> setDeckContext = new Dictionary<Player, (bool, float)>();

        /// <summary>
        /// skillDB는 선택 사항이다(없으면 스킬 스탯 보정 없이 진행). randomSeed를 지정하면 결과 재현이 가능하다.
        /// </summary>
        public MatchEngine(SkillDB skillDB = null, int? randomSeed = null)
        {
            this.skillDB = skillDB;
            random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
        }

        private class TeamGameState
        {
            public string TeamName;
            public List<Player> BattingOrder = new List<Player>(); // 최대 9명
            public int NextBatterIndex;

            public List<Player> Starters = new List<Player>();     // OVR 내림차순, [0] = 오늘의 선발
            public List<Player> LongRelief = new List<Player>();
            public List<Player> WinningRelief = new List<Player>();
            public List<Player> MopUpRelief = new List<Player>();
            public List<Player> Closers = new List<Player>();
            public HashSet<Player> UsedPitchers = new HashSet<Player>();

            public int RunsScored;
        }

        /// <summary>
        /// 양 팀의 28인 로스터로 1회부터 9회까지(동점 시 연장 최대 12회) 경기를 즉시 시뮬레이션한다.
        /// </summary>
        public MatchResult PlayFullMatch(List<Player> homeRoster, List<Player> awayRoster,
            string homeTeamName = "Home", string awayTeamName = "Away")
        {
            var home = BuildTeamState(homeRoster, homeTeamName);
            var away = BuildTeamState(awayRoster, awayTeamName);

            var result = new MatchResult { HomeTeamName = homeTeamName, AwayTeamName = awayTeamName };

            for (int inning = 1; inning <= MaxInnings; inning++)
            {
                int awayRuns = SimulateHalfInning(away, home, inning);
                result.AwayInningScores.Add(awayRuns);
                result.AwayTotalScore += awayRuns;

                // 9회 이후, 말 공격 전에 이미 홈이 앞서 있으면 끝내기와 동일하게 말 공격을 생략한다.
                bool skipBottom = inning >= RegulationInnings && result.HomeTotalScore > result.AwayTotalScore;
                if (!skipBottom)
                {
                    int homeRuns = SimulateHalfInning(home, away, inning);
                    result.HomeInningScores.Add(homeRuns);
                    result.HomeTotalScore += homeRuns;
                }

                bool decided = inning >= RegulationInnings && result.HomeTotalScore != result.AwayTotalScore;
                if (decided) break;
            }

            result.IsExtraInnings = result.AwayInningScores.Count > RegulationInnings;
            result.WinnerTeamName = result.HomeTotalScore == result.AwayTotalScore
                ? null
                : (result.HomeTotalScore > result.AwayTotalScore ? homeTeamName : awayTeamName);

            return result;
        }

        /// <summary>
        /// 타자/투수의 세부 스탯(강화·각성·세트덱·스킬 효과 모두 반영)을 매치업별로 비교해
        /// 확률적으로 타석 결과를 산출한다. PlayFullMatch가 미리 채워 둔 세트덱 컨텍스트를 사용하며,
        /// 캐시에 없는 대상(단독 호출 등)은 세트덱 보너스 없이 계산한다.
        /// </summary>
        public AtBatResult SimulateAtBat(Player batter, Player pitcher)
        {
            if (batter?.Template == null || pitcher?.Template == null) return AtBatResult.Groundout;
            if (batter.Template.IsPitcher || !pitcher.Template.IsPitcher) return AtBatResult.Groundout;

            var batterStats = ResolveEffectiveBatterStats(batter);
            var pitcherStats = ResolveEffectivePitcherStats(pitcher, batter);

            float strikeoutSkill = NormalizeDiff(
                (pitcherStats.Stuff + pitcherStats.Velocity) / 2f
                - (batterStats.Contact * ContactWeightInStrikeout + batterStats.Discipline * DisciplineWeightInStrikeout));

            float walkSkill = NormalizeDiff(batterStats.Discipline - pitcherStats.Control);
            float contactSkill = NormalizeDiff(batterStats.Contact - pitcherStats.Movement);
            float powerSkill = NormalizeDiff(batterStats.Power - pitcherStats.Movement);

            var weights = new float[OutcomeTable.Length];
            float total = 0f;
            for (int i = 0; i < OutcomeTable.Length; i++)
            {
                var (_, baseWeight, driver, direction) = OutcomeTable[i];
                float skill = driver switch
                {
                    OutcomeDriver.Strikeout => strikeoutSkill,
                    OutcomeDriver.Walk => walkSkill,
                    OutcomeDriver.ContactQuality => contactSkill,
                    OutcomeDriver.Power => powerSkill,
                    _ => 0f
                };

                float multiplier = Mathf.Max(1f + direction * skill * SkillInfluence, 0.05f); // 확률 붕괴 방지 하한
                weights[i] = baseWeight * multiplier;
                total += weights[i];
            }

            double roll = random.NextDouble() * total;
            double cumulative = 0;
            for (int i = 0; i < OutcomeTable.Length; i++)
            {
                cumulative += weights[i];
                if (roll <= cumulative) return OutcomeTable[i].result;
            }

            return OutcomeTable[OutcomeTable.Length - 1].result;
        }

        private static float NormalizeDiff(float diff) => Mathf.Clamp(diff / StatDiffNormalizer, -1f, 1f);

        // ----- 팀 상태 구성 -----

        private TeamGameState BuildTeamState(List<Player> roster, string teamName)
        {
            var valid = (roster ?? new List<Player>()).Where(p => p?.Template != null).ToList();
            var (isSetDeckActive, multiplier) = EvaluateSetDeckBonus(valid);

            foreach (var player in valid)
            {
                setDeckContext[player] = (isSetDeckActive, multiplier);
                baseOvrCache[player] = player.CalculateOVR(isSetDeckActive, multiplier); // 스킬 미포함 순수 OVR
            }

            return new TeamGameState
            {
                TeamName = teamName,
                BattingOrder = BuildBattingOrder(valid),
                Starters = valid.Where(p => p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.StartingPitcher)
                    .OrderByDescending(GetBaseOvr).ToList(),
                LongRelief = valid.Where(p => p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.LongReliever).ToList(),
                WinningRelief = valid.Where(p => p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.WinningReliever).ToList(),
                MopUpRelief = valid.Where(p => p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.MopUpReliever).ToList(),
                Closers = valid.Where(p => p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.Closer).ToList(),
            };
        }

        private static List<Player> BuildBattingOrder(List<Player> teamRoster)
        {
            var batters = teamRoster.Where(p => !p.Template.IsPitcher).ToList();
            var order = new List<Player>(9);

            // 포지션당 1명, OVR 최우선으로 우선 배정
            foreach (var position in StarterBatterPositions)
            {
                var pick = batters.Where(p => p.Template.BatterPosition == position && !order.Contains(p))
                    .OrderByDescending(p => p.CalculateOVR(false))
                    .FirstOrDefault();
                if (pick != null) order.Add(pick);
            }

            // [Fallback] 포지션 후보가 없어 9자리가 안 채워지면 남은 타자 중 OVR 상위로 채운다.
            foreach (var extra in batters.Where(p => !order.Contains(p)).OrderByDescending(p => p.CalculateOVR(false)))
            {
                if (order.Count >= 9) break;
                order.Add(extra);
            }

            return order;
        }

        private static (bool isActive, float multiplier) EvaluateSetDeckBonus(List<Player> teamRoster)
        {
            int maxCount = teamRoster
                .Where(p => p.Template.Team != Team.None)
                .GroupBy(p => p.Template.Team)
                .Select(g => g.Count())
                .DefaultIfEmpty(0)
                .Max();

            bool isActive = maxCount >= SetDeckActivationThreshold;
            return (isActive, isActive ? SetDeckBonusMultiplier : 1f);
        }

        // ----- 세부 스탯 계산(세트덱 + 스킬 효과 반영) -----

        private (bool isActive, float multiplier) GetSetDeckContext(Player player)
        {
            return setDeckContext.TryGetValue(player, out var context) ? context : (false, 1f);
        }

        private int GetBaseOvr(Player player)
        {
            if (player == null) return 0;
            return baseOvrCache.TryGetValue(player, out var cached) ? cached : player.CalculateOVR(false);
        }

        /// <summary>강화/각성(Player) + 세트덱(팀) + 스킬(FlatStatBonus) 보정을 모두 반영한 타자 세부 스탯.</summary>
        private BatterStats ResolveEffectiveBatterStats(Player batter)
        {
            var stats = batter.GetEffectiveBatterStats();
            var (isActive, multiplier) = GetSetDeckContext(batter);
            if (isActive && multiplier > 1f)
            {
                stats = new BatterStats(
                    Mathf.RoundToInt(stats.Power * multiplier),
                    Mathf.RoundToInt(stats.Contact * multiplier),
                    Mathf.RoundToInt(stats.Discipline * multiplier));
            }

            if (skillDB != null)
            {
                foreach (var skillName in batter.AcquiredSkillIds)
                {
                    var skill = skillDB.FindSkill(skillName);
                    if (skill?.Effect == null) continue;

                    // 배터용 UnderdogStatBonus 스킬은 GDD에 구체 수치가 없어 아직 미구현이다(TODO).
                    if (skill.Effect.EffectType == SkillEffectType.FlatStatBonus)
                    {
                        int v = skill.Effect.Value;
                        stats = new BatterStats(stats.Power + v, stats.Contact + v, stats.Discipline + v);
                    }
                }
            }

            return stats;
        }

        /// <summary>강화/각성(Player) + 세트덱(팀) + 스킬(Flat/Underdog) 보정을 모두 반영한 투수 세부 스탯.</summary>
        private PitcherStats ResolveEffectivePitcherStats(Player pitcher, Player opposingBatter)
        {
            var stats = pitcher.GetEffectivePitcherStats();
            var (isActive, multiplier) = GetSetDeckContext(pitcher);
            if (isActive && multiplier > 1f)
            {
                stats = new PitcherStats(
                    Mathf.RoundToInt(stats.Stuff * multiplier),
                    Mathf.RoundToInt(stats.Velocity * multiplier),
                    Mathf.RoundToInt(stats.Movement * multiplier),
                    Mathf.RoundToInt(stats.Control * multiplier));
            }

            if (skillDB != null)
            {
                // "패기" 판정은 스킬 효과가 섞이지 않은 기본 OVR로 비교한다.
                // (GDD: "상대 타자의 스킬 효과로 인한 OVR 증가 제외")
                int pitcherOvr = GetBaseOvr(pitcher);
                int batterOvr = GetBaseOvr(opposingBatter);

                foreach (var skillName in pitcher.AcquiredSkillIds)
                {
                    var skill = skillDB.FindSkill(skillName);
                    if (skill?.Effect == null) continue;

                    switch (skill.Effect.EffectType)
                    {
                        case SkillEffectType.FlatStatBonus:
                            stats = AddFlat(stats, skill.Effect.Value);
                            break;
                        case SkillEffectType.UnderdogStatBonus:
                            if (pitcherOvr < batterOvr) stats = AddFlat(stats, skill.Effect.Value);
                            break;
                    }
                }
            }

            return stats;
        }

        private static PitcherStats AddFlat(PitcherStats stats, int value) =>
            new PitcherStats(stats.Stuff + value, stats.Velocity + value, stats.Movement + value, stats.Control + value);

        // ----- 이닝/타석 진행 -----

        private int SimulateHalfInning(TeamGameState batting, TeamGameState pitching, int inning)
        {
            if (batting.BattingOrder.Count == 0) return 0;

            var pitcher = SelectPitcherForInning(pitching, batting, inning);
            if (pitcher == null) return 0;

            bool onFirst = false, onSecond = false, onThird = false;
            int outs = 0;
            int runs = 0;

            while (outs < 3)
            {
                var batter = GetNextBatter(batting);
                if (batter == null) break;

                var result = SimulateAtBat(batter, pitcher);
                bool isOut = result == AtBatResult.Strikeout || result == AtBatResult.Groundout || result == AtBatResult.Flyout;

                if (isOut)
                {
                    // 단순화된 '주자 생환' 처리: 삼진이 아닌 아웃이고 아웃카운트 2 미만이며 3루 주자가 있으면 1점(희생플라이/땅볼 성격)
                    if (onThird && outs < 2 && result != AtBatResult.Strikeout)
                    {
                        runs++;
                        onThird = false;
                    }
                    outs++;
                }
                else
                {
                    runs += AdvanceRunners(result, ref onFirst, ref onSecond, ref onThird);
                }
            }

            batting.RunsScored += runs;
            return runs;
        }

        private static Player GetNextBatter(TeamGameState team)
        {
            if (team.BattingOrder.Count == 0) return null;

            var batter = team.BattingOrder[team.NextBatterIndex % team.BattingOrder.Count];
            team.NextBatterIndex++;
            return batter;
        }

        /// <summary>
        /// 간이 불펜 운용 규칙(1~5회 선발, 6회 롱릴리프, 7~8회 승리/추격조, 9회+ 마무리/추격조).
        /// 실제 투구수/피로도 기반 교체나 유저 개입(하이라이트/풀 플레이)은 후속 과제.
        /// </summary>
        private static Player SelectPitcherForInning(TeamGameState pitchingTeam, TeamGameState battingTeam, int inning)
        {
            int scoreDiff = pitchingTeam.RunsScored - battingTeam.RunsScored; // 0 이상이면 투수팀이 동점 이상

            List<Player> preferred;
            if (inning <= 5 && pitchingTeam.Starters.Count > 0)
            {
                preferred = new List<Player> { pitchingTeam.Starters[0] };
            }
            else if (inning == 6)
            {
                preferred = pitchingTeam.LongRelief;
            }
            else if (inning >= RegulationInnings)
            {
                preferred = scoreDiff >= 0 ? pitchingTeam.Closers : pitchingTeam.MopUpRelief;
            }
            else // 7~8회
            {
                preferred = scoreDiff >= 0 ? pitchingTeam.WinningRelief : pitchingTeam.MopUpRelief;
            }

            var pick = preferred.FirstOrDefault(p => !pitchingTeam.UsedPitchers.Contains(p)) ?? preferred.FirstOrDefault();

            if (pick == null)
            {
                // [Fallback] 선호 롤에 가용 투수가 없으면 전체 투수 풀에서 미사용 -> 재사용 순으로 배정한다.
                var allPitchers = pitchingTeam.Starters
                    .Concat(pitchingTeam.LongRelief)
                    .Concat(pitchingTeam.WinningRelief)
                    .Concat(pitchingTeam.MopUpRelief)
                    .Concat(pitchingTeam.Closers)
                    .ToList();

                pick = allPitchers.FirstOrDefault(p => !pitchingTeam.UsedPitchers.Contains(p)) ?? allPitchers.FirstOrDefault();
            }

            if (pick != null) pitchingTeam.UsedPitchers.Add(pick);
            return pick;
        }

        /// <summary>타석 결과에 따라 주자를 이동시키고 이번 타석에서 발생한 득점 수를 반환한다.</summary>
        private static int AdvanceRunners(AtBatResult result, ref bool onFirst, ref bool onSecond, ref bool onThird)
        {
            int runs = 0;

            switch (result)
            {
                case AtBatResult.Walk:
                    bool forcedHome = onFirst && onSecond && onThird;
                    bool forceToThird = onFirst && onSecond;
                    bool forceToSecond = onFirst;
                    if (forcedHome) runs++;
                    if (forceToThird) onThird = true;
                    if (forceToSecond) onSecond = true;
                    onFirst = true;
                    break;

                case AtBatResult.Single:
                    if (onThird) runs++;
                    onThird = onSecond;
                    onSecond = onFirst;
                    onFirst = true;
                    break;

                case AtBatResult.Double:
                    if (onThird) runs++;
                    if (onSecond) runs++;
                    onThird = onFirst; // 단순화: 1루 주자는 3루에서 멈춘다고 가정
                    onSecond = true;
                    onFirst = false;
                    break;

                case AtBatResult.Triple:
                    if (onThird) runs++;
                    if (onSecond) runs++;
                    if (onFirst) runs++;
                    onThird = true;
                    onSecond = false;
                    onFirst = false;
                    break;

                case AtBatResult.HomeRun:
                    runs = 1 + (onFirst ? 1 : 0) + (onSecond ? 1 : 0) + (onThird ? 1 : 0);
                    onFirst = onSecond = onThird = false;
                    break;
            }

            return runs;
        }
    }
}
