using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-05] 구단 치어리더 15인 풀 · 경기당 4~6인 선발 엔트리 · 체력 로테이션 · 전담 응원 매칭 · 4대 스탯 경기 효과(기획서 3절).
    ///   - 엔트리 = 풀 앞쪽 CheerEntrySize명(순서 = 6인 역할 슬롯 1.응원단장 ~ 6.위기 응원). 4명 미만 · 6명 초과 변경은 차단한다.
    ///   - 체력: 단상 출전 경기당 -9, 벤치 +14. 체력 30 미만이면 응원 효율 50%(역할 효과 · 4대 스탯 기여 모두).
    ///   - 4대 스탯 → ① 리더십: 팀워크 +1~+12 ② 타격 응원력: 후반 클러치 +0~5% ③ 마운드 응원력: 실책 확률 최대 -35% ④ 홈 흥행력: 홈경기 관중 수익(예산) · 팬 지지율.
    ///   - 전담 응원: 엔트리의 에이스(ICON 이상 · ★3 도약) 또는 리더(1번 응원단장) 치어리더가 Ego 4+ 스타를 맡으면 보직 양보 인센티브 동급 효과 + 만족도 +15(최대 2쌍).
    /// </summary>
    public static class GMCheerleaderRoster
    {
        public const int MinPool = 12;
        /// <summary>[TASK-GM-06] GM-05 D.2 - 내 구단 치어리더 자동 로테이션 기본값(켬). 한 시즌 고속 진행 중 체력 고갈로 효율이 떨어지지 않게 한다.</summary>
        public const bool AutoRotateUserCheerleaders = true;
        public const int MaxDedications = 2;
        public const int DedicationMoraleBonus = 15;
        public const float MaxErrorReduction = 0.35f;
        public const float MaxClutchBonus = 0.05f;
        public const int HomeRevenuePerDraw = 4;    // 홈 흥행력 1점당 관중 수익(만 원)
        public const int FanPointsPerSupport = 4000; // 누적 관중 수익 4,000만 원마다 팬 지지율 +1

        // ================================================================== 엔트리

        public static List<Cheerleader> Entry(GMTeamState team) => team?.CheerEntry.ToList() ?? new List<Cheerleader>();
        public static int EntryIndexOf(GMTeamState team, Cheerleader c)
        {
            if (team == null || c == null) return -1;
            int i = team.CheerleaderPool.IndexOf(c);
            return i >= 0 && i < team.CheerEntrySize ? i : -1;
        }
        public static bool IsInEntry(GMTeamState team, Cheerleader c) => EntryIndexOf(team, c) >= 0;

        public static bool TryAdd(GMTeamState team, Cheerleader c, out string reason)
        {
            reason = null;
            if (team == null || c == null || !team.CheerleaderPool.Contains(c)) { reason = "구단 치어리더 풀에 없는 인원입니다."; return false; }
            if (IsInEntry(team, c)) { reason = $"{c.DisplayName}은(는) 이미 엔트리에 있습니다."; return false; }
            if (team.CheerEntrySize >= GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MAX) { reason = $"경기 엔트리는 최대 {GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MAX}명입니다. 먼저 한 명을 해제하십시오."; return false; }
            team.CheerleaderPool.Remove(c);
            team.CheerleaderPool.Insert(team.CheerEntrySize, c);
            team.CheerEntrySize++;
            RefreshDedications(team);
            return true;
        }

        public static bool TryRemove(GMTeamState team, Cheerleader c, out string reason)
        {
            reason = null;
            if (!IsInEntry(team, c)) { reason = "엔트리에 없는 인원입니다."; return false; }
            if (team.CheerEntrySize <= GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN) { reason = $"경기 엔트리는 최소 {GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN}명입니다. 다른 인원을 먼저 배치하십시오."; return false; }
            team.CheerleaderPool.Remove(c);
            team.CheerEntrySize--;
            team.CheerleaderPool.Insert(team.CheerEntrySize, c); // 벤치 맨 앞
            RefreshDedications(team);
            return true;
        }

        public static bool TryToggle(GMTeamState team, Cheerleader c, out string reason) =>
            IsInEntry(team, c) ? TryRemove(team, c, out reason) : TryAdd(team, c, out reason);

        /// <summary>엔트리를 통째로 지정(순서 = 역할 슬롯). 4~6명 · 풀 소속 · 동일 인물 중복 없음일 때만 적용.</summary>
        public static bool TrySetEntry(GMTeamState team, IList<Cheerleader> selection, out string reason)
        {
            reason = null;
            var list = (selection ?? new List<Cheerleader>()).Where(c => !CheerSquad.IsEmpty(c)).ToList();
            if (!GMCheerleaderRules.IsValidEntryCount(list.Count))
            {
                reason = $"경기 엔트리는 {GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN}~{GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MAX}명이어야 합니다(선택 {list.Count}명).";
                return false;
            }
            if (team == null || list.Any(c => !team.CheerleaderPool.Contains(c))) { reason = "구단 치어리더 풀에 없는 인원이 있습니다."; return false; }
            if (list.Select(CheerSquad.PersonKey).Distinct().Count() != list.Count) { reason = "동일 인물을 중복 배치할 수 없습니다."; return false; }
            var rest = team.CheerleaderPool.Where(c => !list.Contains(c)).ToList();
            team.CheerleaderPool.Clear();
            team.CheerleaderPool.AddRange(list);
            team.CheerleaderPool.AddRange(rest);
            team.CheerEntrySize = list.Count;
            RefreshDedications(team);
            return true;
        }

        /// <summary>
        /// [최적 컨디션 자동 편성] - 체력 30 이상 우선, 열정도 × 효율 + 체력 가중으로 size명을 뽑고, 역할 슬롯에 맞는 스탯 순으로 배치한다
        /// (응원단장 = 리더십, 타격 응원 = 타격, 투수 응원 = 마운드, 분위기 메이커 = 리더십, 홈 응원 = 홈 흥행, 위기 응원 = 타격).
        /// </summary>
        public static List<Cheerleader> AutoArrange(GMTeamState team, int size = GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_DEFAULT)
        {
            if (team == null) return new List<Cheerleader>();
            size = Math.Min(GMCheerleaderRules.ClampEntrySize(size), team.CheerleaderPool.Count);
            var picked = team.CheerleaderPool.Where(c => !CheerSquad.IsEmpty(c))
                .OrderByDescending(c => GMCheerleaderStats.IsTired(c) ? 0 : 1)
                .ThenByDescending(c => GMCheerleaderStats.Cheer(c) * GMCheerleaderStats.Efficiency(c) + GMCheerleaderStats.Stamina(c) * 0.2)
                .ThenBy(c => c.InstanceId)
                .Take(size).ToList();
            var roleStat = new[] { GMCheerStat.Leadership, GMCheerStat.Batting, GMCheerStat.Mound, GMCheerStat.Leadership, GMCheerStat.HomeDraw, GMCheerStat.Batting };
            var ordered = new List<Cheerleader>();
            for (int i = 0; i < size; i++)
            {
                var best = picked.Where(c => !ordered.Contains(c)).OrderByDescending(c => GMCheerleaderStats.Stat(c, roleStat[i])).ThenBy(c => c.InstanceId).First();
                ordered.Add(best);
            }
            if (ordered.Count >= GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN) TrySetEntry(team, ordered, out _);
            return ordered;
        }

        // ================================================================== [TASK-GM-17] 응원단 육성(강화) - 클래식 로비 성장 화면 대체

        /// <summary>강화 비용(만 원) = 기존 응원 포인트 강화 비용표(CheerGrowth.ReinforceCost)를 운영 예산으로 지불.</summary>
        public static int ReinforceCost(Cheerleader c) => CheerGrowth.ReinforceCost(CheerGrowth.Reinforce(c));

        /// <summary>[응원단 육성 +1강] - 운영 예산을 써서 강화 단계 +1(최대 +10강) → 4대 스탯 +2 · 역할 효과 상승(CheerGrowth).</summary>
        public static bool TryReinforce(GMTeamState team, Cheerleader c, out string message)
        {
            message = "";
            if (team == null || c == null || !team.CheerleaderPool.Contains(c)) { message = "구단 응원단 인원을 고르십시오."; return false; }
            if (CheerGrowth.Reinforce(c) >= CheerGrowth.MaxReinforce) { message = $"{c.DisplayName}은(는) 이미 최대 +{CheerGrowth.MaxReinforce}강입니다."; return false; }
            int cost = ReinforceCost(c);
            if (team.Budget < cost) { message = $"운영 예산 부족 - 육성비 {GMDiagnosticFormat.Short(cost)} 필요(잔여 {GMDiagnosticFormat.Short(team.Budget)})."; return false; }
            int before = GMCheerleaderStats.Cheer(c);
            team.Budget -= cost;
            c.ReinforceLevel = CheerGrowth.Reinforce(c) + 1;
            message = $"{c.DisplayName} 응원단 육성 +{c.ReinforceLevel}강 - 육성비 {GMDiagnosticFormat.Short(cost)} · CHEER {before} → {GMCheerleaderStats.Cheer(c)} · 예산 {GMDiagnosticFormat.Short(team.Budget)}";
            return true;
        }

        // ================================================================== 4대 스탯 → 경기 · 구단 효과

        private static double Sum(IEnumerable<Cheerleader> entry, GMCheerStat stat) =>
            (entry ?? Enumerable.Empty<Cheerleader>()).Where(c => !CheerSquad.IsEmpty(c)).Sum(c => GMCheerleaderStats.Stat(c, stat) * (double)GMCheerleaderStats.Efficiency(c));

        private static bool Valid(IEnumerable<Cheerleader> entry) => GMCheerleaderRules.EntryCount(entry) >= GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN;

        /// <summary>① 단장 리더십 → 팀워크 +1~+12(리더십 × 효율 합 / 32). 엔트리 4명 미만이면 0.</summary>
        public static int LeadershipTeamworkBonus(IEnumerable<Cheerleader> entry)
        {
            var list = entry?.ToList();
            if (!Valid(list)) return 0;
            return Math.Max(1, Math.Min(GMCheerleaderRules.MaxLeadershipBuff, (int)Math.Round(Sum(list, GMCheerStat.Leadership) / 32.0, MidpointRounding.AwayFromZero)));
        }

        /// <summary>③ 마운드 응원력 → 수비 실책 확률 감소율 0~35%(마운드 × 효율 합 450에서 상한).</summary>
        public static float ErrorReduction(IEnumerable<Cheerleader> entry)
        {
            var list = entry?.ToList();
            if (!Valid(list)) return 0f;
            return (float)(MaxErrorReduction * Math.Min(1.0, Sum(list, GMCheerStat.Mound) / 450.0));
        }

        /// <summary>② 타격 응원력 → 7회 이후 접전 득점권 클러치 가중치 +0~5%.</summary>
        public static float ClutchBonus(IEnumerable<Cheerleader> entry)
        {
            var list = entry?.ToList();
            if (!Valid(list)) return 0f;
            return (float)(MaxClutchBonus * Math.Min(1.0, Sum(list, GMCheerStat.Batting) / 450.0));
        }

        /// <summary>④ 홈 흥행력 → 홈경기 관중 수익(만 원) = 홈 흥행력 × 효율 합 × 4.</summary>
        public static int HomeRevenue(IEnumerable<Cheerleader> entry)
        {
            var list = entry?.ToList();
            if (!Valid(list)) return 0;
            return (int)Math.Round(Sum(list, GMCheerStat.HomeDraw) * HomeRevenuePerDraw);
        }

        /// <summary>
        /// 경기 역할 효과(기존 6인 역할 슬롯 · 강화 · ★각성 수치 그대로) + 체력 30 미만 인원 역할 효과 절반. 4명 미만이면 null.
        /// </summary>
        public static CheerSquadEffects BuildMatchEffects(IReadOnlyList<Cheerleader> entry, Team team, bool isHome, int losingStreak)
        {
            if (!Valid(entry)) return null;
            var fx = CheerSquad.BuildEffects(entry, team, isHome, losingStreak, 0);
            for (int i = 0; i < entry.Count && i < CheerSquad.SlotCount; i++)
            {
                if (!GMCheerleaderStats.IsTired(entry[i])) continue;
                switch ((CheerRole)i)
                {
                    case CheerRole.Leader: fx.SetDeckAmplifyPercent /= 2; fx.SetDeckAmplifyBonus /= 2; break;
                    case CheerRole.Batting: fx.BatterContactDiscipline /= 2; break;
                    case CheerRole.Pitching: fx.PitcherControlStuff /= 2; break;
                    case CheerRole.MoodMaker: fx.LosingStreakBonus /= 2; fx.TrailingBatterBonus /= 2; break;
                    case CheerRole.Home: fx.HomeAllStatsBonus /= 2; fx.OpponentControlPenalty /= 2; break;
                    default:
                        fx.CloseLateMultiplier = 1f + (fx.CloseLateMultiplier - 1f) / 2f;
                        fx.LateRispMultiplier = 1f + (fx.LateRispMultiplier - 1f) / 2f;
                        break;
                }
                fx.Lines.Add($"{i + 1}.{CheerSquad.RoleName((CheerRole)i)} {entry[i].DisplayName}: 체력 {GMCheerleaderStats.Stamina(entry[i])} - 응원 효율 50%");
            }
            return fx;
        }

        /// <summary>홈경기 관중 수익을 구단 예산에 더하고, 누적 수익 4,000만 원마다 팬 지지율 +1. 더한 수익(만 원)을 돌려준다.</summary>
        public static int ApplyHomeGate(GMTeamState team)
        {
            int revenue = HomeRevenue(team?.CheerEntry);
            if (revenue <= 0) return 0;
            team.Budget += revenue;
            team.CheerFanPoints += revenue;
            while (team.CheerFanPoints >= FanPointsPerSupport)
            {
                team.CheerFanPoints -= FanPointsPerSupport;
                team.FanSupport = Math.Min(100, team.FanSupport + 1);
            }
            return revenue;
        }

        /// <summary>오늘 단상 활약 요약 - "응원단 5인 열띤 단상 응원! 홈 흥행 보너스 +1,200만 원 & 팀워크 +8".</summary>
        public static string MatchSummary(GMTeamState team, bool isHome, int revenue)
        {
            var entry = Entry(team);
            if (!Valid(entry)) return "응원단 엔트리 미충족(4명 미만) - 단상 응원 효과 없음";
            int tired = entry.Count(GMCheerleaderStats.IsTired);
            string gate = isHome ? $"홈 흥행 보너스 +{revenue:N0}만 원 & " : "원정 응원 · ";
            return $"응원단 {entry.Count}인 열띤 단상 응원! {gate}팀워크 +{LeadershipTeamworkBonus(entry)} · 실책 -{ErrorReduction(entry) * 100f:0}%" +
                   (tired > 0 ? $" (체력 저하 {tired}명 효율 50%)" : "");
        }

        // ================================================================== 체력 로테이션

        /// <summary>경기일 1회: 엔트리 체력 소모 · 벤치 회복, 자동 로테이션 구단은 체력 30 미만 인원이 생기면 같은 인원수로 재편성, 전담 응원 정리.</summary>
        public static void TickDay(GMTeamState team)
        {
            if (team == null) return;
            var entry = new HashSet<Cheerleader>(team.CheerEntry);
            foreach (var c in team.CheerleaderPool) GMCheerleaderStats.TickStamina(c, entry.Contains(c));
            if (team.CheerAutoRotate && team.CheerEntry.Any(GMCheerleaderStats.IsTired)) AutoArrange(team, team.CheerEntrySize);
            RefreshDedications(team);
        }

        // ================================================================== 전담 응원 매칭

        /// <summary>에이스(유효 티어 2 이상 = ICON 이상 또는 ★3 도약) 또는 리더(엔트리 1번 응원단장).</summary>
        public static bool IsAceOrLeader(GMTeamState team, Cheerleader c) =>
            IsInEntry(team, c) && (EntryIndexOf(team, c) == 0 || CheerGrowth.EffectiveTier(c) >= 2);

        /// <summary>전담 응원이 필요한 스타 - 보직 충돌로 불만인 Ego 4+ 선수 먼저, 없으면 만족도가 가장 낮은 Ego 4+ 선수(이미 매칭된 선수 제외).</summary>
        public static Player DedicationTarget(GMTeamState team)
        {
            if (team == null) return null;
            var taken = new HashSet<string>(team.CheerDedications.Select(d => d.PlayerId));
            var unhappy = TeamChemistryEngine.GetDissatisfiedStars(team.AvailableRoster).Where(p => !taken.Contains(p.InstanceId)).ToList();
            if (unhappy.Count > 0) return unhappy.OrderBy(p => p.PersonalMorale).ThenBy(p => p.InstanceId).First();
            return team.Roster.Where(p => p.EgoLevel >= 4 && !taken.Contains(p.InstanceId)).OrderBy(p => p.PersonalMorale).ThenBy(p => p.InstanceId).FirstOrDefault();
        }

        public static bool TryDedicate(GMTeamState team, Cheerleader cheerleader, Player player, out string reason)
        {
            reason = null;
            if (team == null || cheerleader == null || player == null) { reason = "치어리더와 선수를 선택하십시오."; return false; }
            if (!IsAceOrLeader(team, cheerleader)) { reason = "전담 응원은 엔트리의 에이스(ICON 이상 · ★3) 또는 1번 응원단장만 맡을 수 있습니다."; return false; }
            if (!team.Roster.Contains(player)) { reason = "우리 구단 선수가 아닙니다."; return false; }
            if (player.EgoLevel < 4) { reason = $"{player.Template.PlayerName}은(는) 자존심 {player.EgoLevel}단계 - 전담 응원은 Ego 4 이상 스타 대상입니다."; return false; }
            if (team.CheerDedications.Any(d => d.PlayerId == player.InstanceId)) { reason = $"{player.Template.PlayerName}은(는) 이미 전담 응원을 받고 있습니다."; return false; }
            if (team.CheerDedications.Any(d => d.CheerleaderId == cheerleader.InstanceId)) { reason = $"{cheerleader.DisplayName}은(는) 이미 다른 선수를 전담 중입니다."; return false; }
            if (team.CheerDedications.Count >= MaxDedications) { reason = $"전담 응원은 최대 {MaxDedications}쌍입니다."; return false; }
            var d = new GMCheerDedication { CheerleaderId = cheerleader.InstanceId, PlayerId = player.InstanceId, GrantedConcession = !player.HasRoleConcessionBonus };
            player.HasRoleConcessionBonus = true; // 보직 양보 인센티브 동급 - 보직 충돌(②) 불만 대상에서 빠진다
            player.PersonalMorale = Math.Min(100, player.PersonalMorale + DedicationMoraleBonus);
            team.CheerDedications.Add(d);
            return true;
        }

        /// <summary>[TASK-GM-06] GM-05 D.3 - 전담 응원 대상으로 지정할 수 있는 우리 구단 Ego 4+ 스타(불만 스타 우선 · 만족도 낮은 순).</summary>
        public static List<Player> DedicationCandidates(GMTeamState team)
        {
            if (team == null) return new List<Player>();
            var unhappy = new HashSet<Player>(TeamChemistryEngine.GetDissatisfiedStars(team.AvailableRoster));
            return team.Roster.Where(p => p.EgoLevel >= 4)
                .OrderByDescending(p => unhappy.Contains(p)).ThenBy(p => p.PersonalMorale).ThenBy(p => p.InstanceId).ToList();
        }

        /// <summary>[TASK-GM-06] 이 치어리더가 지금 전담 중인 선수(없으면 null).</summary>
        public static Player DedicatedPlayerOf(GMTeamState team, Cheerleader cheerleader)
        {
            if (team == null || cheerleader == null) return null;
            var d = team.CheerDedications.FirstOrDefault(x => x.CheerleaderId == cheerleader.InstanceId);
            return d == null ? null : team.Roster.FirstOrDefault(p => p.InstanceId == d.PlayerId);
        }

        /// <summary>[TASK-GM-06] 전담 응원 해제 - 매칭으로 준 보직 양보 효과를 되돌린다.</summary>
        public static bool ClearDedication(GMTeamState team, Cheerleader cheerleader)
        {
            if (team == null || cheerleader == null) return false;
            var d = team.CheerDedications.FirstOrDefault(x => x.CheerleaderId == cheerleader.InstanceId);
            if (d == null) return false;
            var p = team.Roster.FirstOrDefault(x => x.InstanceId == d.PlayerId);
            if (p != null && d.GrantedConcession) p.HasRoleConcessionBonus = false;
            team.CheerDedications.Remove(d);
            return true;
        }

        /// <summary>
        /// [TASK-GM-06] GM-05 D.3 전담 응원 대상 직접 지정 - 이 치어리더의 기존 매칭을 풀고 player로 다시 매칭한다(다른 치어리더가 맡던 선수면 실패).
        /// 실패하면 기존 매칭을 되돌린다.
        /// </summary>
        public static bool TryReassignDedication(GMTeamState team, Cheerleader cheerleader, Player player, out string reason)
        {
            reason = null;
            if (team == null || cheerleader == null || player == null) { reason = "치어리더와 선수를 선택하십시오."; return false; }
            var previous = DedicatedPlayerOf(team, cheerleader);
            if (previous == player) { reason = $"{cheerleader.DisplayName}은(는) 이미 {player.Template.PlayerName}을(를) 전담 중입니다."; return false; }
            bool hadConcession = previous != null && team.CheerDedications.First(x => x.CheerleaderId == cheerleader.InstanceId).GrantedConcession;
            ClearDedication(team, cheerleader);
            if (TryDedicate(team, cheerleader, player, out reason)) return true;
            if (previous != null)
            {
                team.CheerDedications.Add(new GMCheerDedication { CheerleaderId = cheerleader.InstanceId, PlayerId = previous.InstanceId, GrantedConcession = hadConcession });
                if (hadConcession) previous.HasRoleConcessionBonus = true;
            }
            return false;
        }

        /// <summary>[TASK-GM-06] [전담 대상 변경 ▶] - 후보 목록에서 지금 대상의 다음 선수로 순환 지정한다(다른 치어리더가 맡은 선수는 건너뛴다). 성공하면 새 대상.</summary>
        public static Player CycleDedication(GMTeamState team, Cheerleader cheerleader, out string reason)
        {
            reason = null;
            var candidates = DedicationCandidates(team);
            if (candidates.Count == 0) { reason = "전담 응원이 필요한 Ego 4 이상 스타가 없습니다."; return null; }
            var current = DedicatedPlayerOf(team, cheerleader);
            int start = current != null ? candidates.IndexOf(current) : -1;
            for (int k = 1; k <= candidates.Count; k++)
            {
                var next = candidates[(start + k + candidates.Count) % candidates.Count];
                if (next == current) continue;
                if (team.CheerDedications.Any(d => d.PlayerId == next.InstanceId && d.CheerleaderId != cheerleader.InstanceId)) continue;
                if (TryReassignDedication(team, cheerleader, next, out reason)) return next;
                if (reason != null && (reason.Contains("에이스") || reason.Contains("최대"))) return null;
            }
            reason = reason ?? "지정할 수 있는 다른 Ego 4+ 스타가 없습니다.";
            return null;
        }

        /// <summary>엔트리에서 빠진 치어리더 · 떠난 선수의 매칭을 해제하고, 매칭으로 준 보직 양보 효과를 되돌린다.</summary>
        public static void RefreshDedications(GMTeamState team)
        {
            if (team == null || team.CheerDedications.Count == 0) return;
            for (int i = team.CheerDedications.Count - 1; i >= 0; i--)
            {
                var d = team.CheerDedications[i];
                var c = team.CheerleaderPool.FirstOrDefault(x => x.InstanceId == d.CheerleaderId);
                var p = team.Roster.FirstOrDefault(x => x.InstanceId == d.PlayerId);
                if (c != null && p != null && IsInEntry(team, c)) continue;
                if (p != null && d.GrantedConcession) p.HasRoleConcessionBonus = false;
                team.CheerDedications.RemoveAt(i);
            }
        }

        public static string DedicationLabel(GMTeamState team)
        {
            if (team == null || team.CheerDedications.Count == 0) return "전담 응원 매칭 없음 - 에이스/리더 치어리더를 골라 Ego 4+ 불만 스타를 맡기십시오.";
            return "전담 응원: " + string.Join(" · ", team.CheerDedications.Select(d =>
            {
                var c = team.CheerleaderPool.FirstOrDefault(x => x.InstanceId == d.CheerleaderId);
                var p = team.Roster.FirstOrDefault(x => x.InstanceId == d.PlayerId);
                return $"{c?.DisplayName ?? "-"} → {p?.Template?.PlayerName ?? "-"}(Ego {p?.EgoLevel ?? 0})";
            }));
        }

        // ================================================================== 15인 풀 충원

        /// <summary>
        /// 구단 풀이 target명(기본 15) 미만이면 구단 응원단 신입(LIVE · 구단 소속 · 가상 인물명)으로 채운다 - 카탈로그에 실존 인원이 적은 구단도 12~15명 보장.
        /// </summary>
        public static int FillPool(GMTeamState team, int seasonYear, int target = GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX)
        {
            if (team == null) return 0;
            int added = 0;
            var names = new HashSet<string>(team.CheerleaderPool.Select(CheerSquad.PersonKey));
            for (int n = 1; team.CheerleaderPool.Count < target && n < 200; n++)
            {
                string name = NameAliasTable.GenerateVirtualCheerleaderName($"{team.TeamCode}_ROOKIE_{n}");
                if (string.IsNullOrEmpty(name) || !names.Add(name)) continue;
                team.CheerleaderPool.Add(new Cheerleader(
                    instanceId: GMRosterLoader.StableId($"{team.TeamCode}|CHEER_ROOKIE|{seasonYear}|{n}"), name: name, grade: CheerleaderGrade.LIVE_NORMAL, conditionBuff: 1, clutchMultiplier: 1f,
                    economicBonusRate: 1f, sentimentDefense: 0, catalogId: $"GM_{team.TeamCode}_ROOKIE_{n}", team: team.Team,
                    activePeriod: seasonYear > 0 ? $"{seasonYear}~" : null));
                added++;
            }
            return added;
        }
    }
}
