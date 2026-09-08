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
            ContactQuality, // 타자 정확 vs 투수 구위 (범타로 죽느냐, 안타가 되느냐)
            Power           // 타자 파워 vs 투수 변화 (장타가 되느냐)
        }

        // 타석 결과 기본(fallback) 확률표. EngineConfig가 있으면 결과별 BaseWeight는 그쪽 값을 우선 사용하고,
        // driver/direction 관계(어떤 스탯 매치업이 무엇을 좌우하는지)는 물리적 의미가 있는 구조이므로 코드에 고정한다.
        private static readonly (AtBatResult result, float defaultBaseWeight, OutcomeDriver driver, float direction)[] OutcomeTable =
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

        // config가 없을 때 쓰는 기본값. EngineConfig.PopulateDefaults()의 값과 동일하게 맞춰 둔다.
        private const float DefaultSkillInfluence = 0.6f;
        private const float DefaultStatDiffNormalizer = 50f;
        private const int DefaultSetDeckActivationThreshold = 5;
        private const float DefaultSetDeckBonusMultiplier = 1.15f;

        private static readonly BatterPosition[] StarterBatterPositions =
            (BatterPosition[])Enum.GetValues(typeof(BatterPosition));

        private readonly SkillDB skillDB;
        private readonly EngineConfig config;
        private readonly Random random;

        private float SkillInfluence => config != null ? config.SkillInfluence : DefaultSkillInfluence;
        private float StatDiffNormalizer => config != null ? config.StatDiffNormalizer : DefaultStatDiffNormalizer;
        private int SetDeckActivationThreshold => config != null ? config.SetDeckActivationThreshold : DefaultSetDeckActivationThreshold;
        private float SetDeckBonusMultiplier => config != null ? config.SetDeckBonusMultiplier : DefaultSetDeckBonusMultiplier;

        // 스킬이 섞이지 않은 순수 OVR(강화/각성/세트덱만 반영). 투수 로테이션 정렬과, "패기"류 스킬의
        // OVR 비교 조건(GDD: 상대 스킬 효과로 인한 OVR 증가 제외)에 사용한다.
        private readonly Dictionary<Player, int> baseOvrCache = new Dictionary<Player, int>();
        private readonly Dictionary<Player, (bool isActive, float multiplier)> setDeckContext = new Dictionary<Player, (bool, float)>();

        /// <summary>
        /// skillDB/config는 선택 사항이다(없으면 각각 스킬 보정 없이, 코드 기본 상수로 진행한다).
        /// randomSeed를 지정하면 결과 재현이 가능하다.
        /// </summary>
        public MatchEngine(SkillDB skillDB = null, EngineConfig config = null, int? randomSeed = null)
        {
            this.skillDB = skillDB;
            this.config = config;
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

            var batterStats = ResolveEffectiveBatterStats(batter, pitcher);
            var pitcherStats = ResolveEffectivePitcherStats(pitcher, batter);

            // 삼진: 투수 구위/구속 평균 vs 타자 정확/선구 평균 (동일 가중치)
            float strikeoutSkill = NormalizeDiff(
                (pitcherStats.Stuff + pitcherStats.Velocity) / 2f
                - (batterStats.Contact + batterStats.Discipline) / 2f);

            // 볼넷: 타자 선구 vs 투수 제구
            float walkSkill = NormalizeDiff(batterStats.Discipline - pitcherStats.Control);

            // 안타 여부(맞은 공이 범타로 죽느냐/안타가 되느냐): 타자 정확 vs 투수 구위
            float contactSkill = NormalizeDiff(batterStats.Contact - pitcherStats.Stuff);

            // 장타(2루타 이상) 여부: 타자 파워 vs 투수 변화
            float powerSkill = NormalizeDiff(batterStats.Power - pitcherStats.Movement);

            var weights = new float[OutcomeTable.Length];
            float total = 0f;
            for (int i = 0; i < OutcomeTable.Length; i++)
            {
                var (result, defaultBaseWeight, driver, direction) = OutcomeTable[i];
                float baseWeight = config != null ? config.GetBaseWeight(result, defaultBaseWeight) : defaultBaseWeight;
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

        private float NormalizeDiff(float diff) => Mathf.Clamp(diff / StatDiffNormalizer, -1f, 1f);

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

        private (bool isActive, float multiplier) EvaluateSetDeckBonus(List<Player> teamRoster)
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

        /// <summary>
        /// 최종 스탯 = (Base + Growth) * SetDeckMultiplier + SkillBonus.
        /// 세트덱 배율은 원본 성장분까지만 곱하고, 스킬 보정은 그 뒤에 가산한다 - 스킬 보너스가
        /// 세트덱 배율의 영향을 받아 함께 부풀려지는 복리 현상을 방지하기 위함이다.
        /// </summary>
        private BatterStats ResolveEffectiveBatterStats(Player batter, Player pitcher)
        {
            var stats = batter.GetEffectiveBatterStats(); // Base + Growth
            var (isActive, multiplier) = GetSetDeckContext(batter);
            if (isActive && multiplier > 1f)
            {
                stats = Scale(stats, multiplier); // (Base + Growth) * SetDeckMultiplier
            }

            // 본인이 보유한 Target=Self 스킬
            stats = ApplyBatterSkills(stats, batter, EffectTarget.Self, self: batter, opponent: pitcher);
            // 상대 투수가 보유한 Target=Opponent 스킬(나를 겨냥한 효과) - 조건 판정은 스킬 소유자(투수) 기준
            stats = ApplyBatterSkills(stats, pitcher, EffectTarget.Opponent, self: pitcher, opponent: batter);

            return stats;
        }

        /// <summary>ResolveEffectiveBatterStats와 동일한 연산 순서를 투수 스탯에 적용한다.</summary>
        private PitcherStats ResolveEffectivePitcherStats(Player pitcher, Player batter)
        {
            var stats = pitcher.GetEffectivePitcherStats(); // Base + Growth
            var (isActive, multiplier) = GetSetDeckContext(pitcher);
            if (isActive && multiplier > 1f)
            {
                stats = Scale(stats, multiplier); // (Base + Growth) * SetDeckMultiplier
            }

            stats = ApplyPitcherSkills(stats, pitcher, EffectTarget.Self, self: pitcher, opponent: batter);
            stats = ApplyPitcherSkills(stats, batter, EffectTarget.Opponent, self: batter, opponent: pitcher);

            return stats;
        }

        private static BatterStats Scale(BatterStats stats, float multiplier) => new BatterStats(
            Mathf.RoundToInt(stats.Power * multiplier),
            Mathf.RoundToInt(stats.Contact * multiplier),
            Mathf.RoundToInt(stats.Discipline * multiplier));

        private static PitcherStats Scale(PitcherStats stats, float multiplier) => new PitcherStats(
            Mathf.RoundToInt(stats.Stuff * multiplier),
            Mathf.RoundToInt(stats.Velocity * multiplier),
            Mathf.RoundToInt(stats.Movement * multiplier),
            Mathf.RoundToInt(stats.Control * multiplier));

        /// <summary>
        /// skillOwner가 보유한 스킬 중 지정한 wantedTarget(Self/Opponent)에 해당하고 조건을 만족하는 것만
        /// 골라 stats(타자 스탯)에 적용한다. 조건은 항상 스킬 소유자(self) 기준으로 평가한다.
        /// </summary>
        private BatterStats ApplyBatterSkills(BatterStats stats, Player skillOwner, EffectTarget wantedTarget, Player self, Player opponent)
        {
            if (skillDB == null || skillOwner == null) return stats;

            foreach (var skillName in skillOwner.AcquiredSkillIds)
            {
                var effect = skillDB.FindSkill(skillName)?.Effect;
                if (effect == null || effect.Target != wantedTarget) continue;
                if (!IsConditionMet(effect.Condition, self, opponent)) continue;

                foreach (var modifier in effect.Modifiers)
                {
                    stats = ApplyModifier(stats, modifier.Stat, modifier.Value);
                }
            }

            return stats;
        }

        private PitcherStats ApplyPitcherSkills(PitcherStats stats, Player skillOwner, EffectTarget wantedTarget, Player self, Player opponent)
        {
            if (skillDB == null || skillOwner == null) return stats;

            foreach (var skillName in skillOwner.AcquiredSkillIds)
            {
                var effect = skillDB.FindSkill(skillName)?.Effect;
                if (effect == null || effect.Target != wantedTarget) continue;
                if (!IsConditionMet(effect.Condition, self, opponent)) continue;

                foreach (var modifier in effect.Modifiers)
                {
                    stats = ApplyModifier(stats, modifier.Stat, modifier.Value);
                }
            }

            return stats;
        }

        private static BatterStats ApplyModifier(BatterStats stats, StatType stat, int value) => stat switch
        {
            StatType.Power => new BatterStats(stats.Power + value, stats.Contact, stats.Discipline),
            StatType.Contact => new BatterStats(stats.Power, stats.Contact + value, stats.Discipline),
            StatType.Discipline => new BatterStats(stats.Power, stats.Contact, stats.Discipline + value),
            _ => stats // 투수 전용 StatType이 잘못 설정된 경우 무시
        };

        private static PitcherStats ApplyModifier(PitcherStats stats, StatType stat, int value) => stat switch
        {
            StatType.Stuff => new PitcherStats(stats.Stuff + value, stats.Velocity, stats.Movement, stats.Control),
            StatType.Velocity => new PitcherStats(stats.Stuff, stats.Velocity + value, stats.Movement, stats.Control),
            StatType.Movement => new PitcherStats(stats.Stuff, stats.Velocity, stats.Movement + value, stats.Control),
            StatType.Control => new PitcherStats(stats.Stuff, stats.Velocity, stats.Movement, stats.Control + value),
            _ => stats // 타자 전용 StatType이 잘못 설정된 경우 무시
        };

        /// <summary>
        /// 조건은 항상 스킬 "소유자"(self) 기준으로 평가한다. RunnerOnBase 계열은 SimulateAtBat의 공개
        /// 시그니처가 주자 상태를 전달받지 않아 아직 실제로 평가할 수 없으므로 항상 false를 반환한다.
        /// </summary>
        private bool IsConditionMet(SkillCondition condition, Player self, Player opponent) => condition switch
        {
            SkillCondition.Always => true,
            SkillCondition.SelfOvrLowerThanOpponent => GetBaseOvr(self) < GetBaseOvr(opponent),
            SkillCondition.SelfOvrHigherThanOpponent => GetBaseOvr(self) > GetBaseOvr(opponent),
            _ => false // RunnerOnBase / RunnerInScoringPosition: 후속 과제 (SimulateAtBat 시그니처 확장 필요)
        };

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
