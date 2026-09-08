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

        // 타석 결과 기본 확률표(총합 1.0)와, 히트성 결과 여부. OVR 밸런스 확정 전까지의 임시값이다.
        private static readonly (AtBatResult result, float baseWeight, bool isHitOutcome)[] OutcomeTable =
        {
            (AtBatResult.Strikeout, 0.22f, false),
            (AtBatResult.Groundout, 0.23f, false),
            (AtBatResult.Flyout,    0.20f, false),
            (AtBatResult.Walk,      0.08f, true),
            (AtBatResult.Single,    0.16f, true),
            (AtBatResult.Double,    0.06f, true),
            (AtBatResult.Triple,    0.01f, true),
            (AtBatResult.HomeRun,   0.04f, true),
        };

        private const float SkillInfluence = 0.6f; // 타/투 OVR 차이가 결과 분포를 얼마나 흔드는지(임시 계수)
        private const float OvrDiffNormalizer = 50f; // 이 OVR 차이를 skill=±1(최대 보정)로 정규화

        // GameManager.CheckSetDeckBonus()의 기본값과 동일한 임시 세트덱 규칙.
        // MatchEngine은 씬의 GameManager 싱글톤에 의존하지 않는 순수 C# 클래스이므로 값만 복제해 둔다.
        private const int SetDeckActivationThreshold = 5;
        private const float SetDeckBonusMultiplier = 1.15f;

        private static readonly BatterPosition[] StarterBatterPositions =
            (BatterPosition[])Enum.GetValues(typeof(BatterPosition));

        private readonly SkillDB skillDB;
        private readonly Random random;
        private readonly Dictionary<Player, int> effectiveOvrCache = new Dictionary<Player, int>();

        /// <summary>
        /// skillDB는 선택 사항이다(없으면 스킬 OVR 보정 없이 진행). randomSeed를 지정하면 결과 재현이 가능하다.
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
        /// 타자와 투수의 최종 OVR(강화/각성/세트덱/스킬 보정 포함)을 비교해 확률적으로 타석 결과를 산출한다.
        /// PlayFullMatch가 미리 채워 둔 세트덱 보너스 캐시를 사용하며, 캐시에 없는 대상(단독 호출 등)은
        /// 세트덱 보너스 없이(스킬 보정만 반영해) 즉석 계산한다.
        /// </summary>
        public AtBatResult SimulateAtBat(Player batter, Player pitcher)
        {
            if (batter?.Template == null || pitcher?.Template == null) return AtBatResult.Groundout;

            int diff = GetEffectiveOvr(batter) - GetEffectiveOvr(pitcher);
            float skill = Mathf.Clamp(diff / OvrDiffNormalizer, -1f, 1f);

            var weights = new float[OutcomeTable.Length];
            float total = 0f;
            for (int i = 0; i < OutcomeTable.Length; i++)
            {
                var (_, baseWeight, isHit) = OutcomeTable[i];
                float multiplier = isHit ? (1f + skill * SkillInfluence) : (1f - skill * SkillInfluence);
                multiplier = Mathf.Max(multiplier, 0.05f); // 확률이 0 이하로 붕괴하지 않도록 하한 보장
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

        // ----- 팀 상태 구성 -----

        private TeamGameState BuildTeamState(List<Player> roster, string teamName)
        {
            var valid = (roster ?? new List<Player>()).Where(p => p?.Template != null).ToList();
            var (isSetDeckActive, multiplier) = EvaluateSetDeckBonus(valid);

            foreach (var player in valid)
            {
                effectiveOvrCache[player] = CalculateFinalOvr(player, isSetDeckActive, multiplier);
            }

            return new TeamGameState
            {
                TeamName = teamName,
                BattingOrder = BuildBattingOrder(valid),
                Starters = valid.Where(p => p.Template.IsPitcher && p.Template.PitcherRole == PitcherRole.StartingPitcher)
                    .OrderByDescending(GetEffectiveOvr).ToList(),
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

        // ----- OVR 계산/캐시 -----

        private int CalculateFinalOvr(Player player, bool isSetDeckActive, float multiplier)
        {
            int baseOvr = player.CalculateOVR(isSetDeckActive, multiplier);

            // TODO: 파워/정확/선구/구위/구속/변화/제구 등 세부 스탯 시스템이 만들어지기 전까지,
            // 스킬 효과는 티어에 비례한 임시 flat OVR 보정으로 단순화한다.
            int skillBonus = 0;
            if (skillDB != null)
            {
                foreach (var skillName in player.AcquiredSkillIds)
                {
                    var tier = skillDB.FindTier(skillName);
                    if (tier.HasValue) skillBonus += SkillTierOvrBonus(tier.Value);
                }
            }

            return baseOvr + skillBonus;
        }

        private int GetEffectiveOvr(Player player)
        {
            if (player == null) return 0;
            if (effectiveOvrCache.TryGetValue(player, out var cached)) return cached;

            return CalculateFinalOvr(player, false, 1f);
        }

        private static int SkillTierOvrBonus(SkillTier tier) => tier switch
        {
            SkillTier.S_PLUS => 8,
            SkillTier.S => 5,
            SkillTier.A => 3,
            SkillTier.B => 2,
            SkillTier.C => 1,
            _ => 0
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
