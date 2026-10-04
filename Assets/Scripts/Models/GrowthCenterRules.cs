using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Managers;

namespace KBOManager.Models
{
    /// <summary>[TASK-KBO-184] 선수 관리(성장 센터) 4개 성장 탭.</summary>
    public enum GrowthTab
    {
        Enhance = 0,     // 강화(EXP 누적, 재료 최대 5장)
        Awaken = 1,      // 각성(동일 선수 재료) - 핵심 성장 축
        LimitBreak = 2,  // 한계 돌파(10강 후, 재료 1장)
        Training = 3,    // 훈련·특훈(볼 소모)
        Transcend = 4,   // [TASK-KBO-189] 초월(+10강 · 9각 선행, 동일 선수 1장 + 동포지션 2장 + 포인트/트로피)
    }

    /// <summary>
    /// [TASK-KBO-184] 하단 [선수 관리] 탭 = 성장 전용 허브(성장 센터)의 순수 규칙(MonoBehaviour 없음 - UI/단위 테스트 공용).
    ///   - 기본 성장 대상: 라인업 최고 OVR 선수(로비 대표 선수와 같은 기준), 라인업이 비면 보유 최고 OVR.
    ///   - 대상 변경 목록: 보유 선수 전원(라인업 선수 우선 → OVR 내림차순).
    ///   - 탭별 재료 후보: 라인업(1군) 카드와 대상 자신은 제외. 강화 = 아무 보관 카드(낮은 위상·낮은 OVR 순), 각성 = 동일 선수 &amp;
    ///     동일 시즌 등급(같은 연도 +3각 / 다른 연도 +1각), 한계 돌파 = CardGrowthActions.IsValidLimitBreakMaterial, 훈련 = 재료 없음(볼).
    ///   - 미리보기: 대상을 복제해 같은 규칙(UpgradeManager.ApplyEnhance/ApplyAwaken, CardGrowthActions)을 적용한 결과를 비교한다.
    /// </summary>
    public static class GrowthCenterRules
    {
        /// <summary>[TASK-KBO-189] 5탭 - 강화 · 각성 · 초월 · 한계 돌파 · 훈련·특훈.</summary>
        public static readonly GrowthTab[] Tabs = { GrowthTab.Enhance, GrowthTab.Awaken, GrowthTab.Transcend, GrowthTab.LimitBreak, GrowthTab.Training };

        public static string TabName(GrowthTab tab) => tab switch
        {
            GrowthTab.Enhance => "강화",
            GrowthTab.Awaken => "각성",
            GrowthTab.LimitBreak => "한계 돌파",
            GrowthTab.Training => "훈련·특훈",
            GrowthTab.Transcend => "초월",
            _ => tab.ToString()
        };

        public static int MaxMaterials(GrowthTab tab) => tab switch
        {
            GrowthTab.Enhance => UpgradeManager.MaxEnhanceMaterials,
            GrowthTab.Awaken => 10,
            GrowthTab.LimitBreak => 1,
            GrowthTab.Transcend => 1 + TranscendRules.SupportCount,
            _ => 0
        };

        public static Player DefaultTarget(IEnumerable<Player> roster, IEnumerable<Player> inventory)
        {
            var best = (roster ?? Enumerable.Empty<Player>()).Where(p => p?.Template != null)
                .OrderByDescending(p => p.CalculateNeutralOVR()).FirstOrDefault();
            return best ?? (inventory ?? Enumerable.Empty<Player>()).Where(p => p?.Template != null)
                .OrderByDescending(p => p.CalculateNeutralOVR()).FirstOrDefault();
        }

        /// <summary>[대상 선수 변경] 목록 - 보유 선수 전원(라인업 우선, OVR 내림차순). lineupOnly면 라인업만.</summary>
        public static List<Player> TargetCandidates(IEnumerable<Player> inventory, IEnumerable<Player> roster, bool lineupOnly = false)
        {
            var inRoster = new HashSet<Player>(roster ?? Enumerable.Empty<Player>());
            var all = (inventory ?? Enumerable.Empty<Player>()).Concat(inRoster).Where(p => p?.Template != null).Distinct();
            if (lineupOnly) all = all.Where(inRoster.Contains);
            return all.OrderByDescending(inRoster.Contains).ThenByDescending(p => p.CalculateNeutralOVR())
                .ThenBy(p => p.Template.PlayerName, StringComparer.Ordinal).ToList();
        }

        /// <summary>[TASK-KBO-189] 같은 시즌 등급 - 같은 선수(+3각) 또는 같은 포지션 다른 선수(+1각). 다른 포지션 다른 선수는 불가.</summary>
        public static bool IsValidAwakenMaterial(Player target, Player material) => CardGrowthRules.AwakenGainFor(target, material) > 0;

        /// <summary>[TASK-KBO-185] 각성 미리보기 문구 - "0각 → 3각 · OVR +5 점프"(재료 없으면 빈 문자열).</summary>
        public static string AwakenPreview(Player target, IReadOnlyList<Player> materials)
        {
            if (target?.Template == null || materials == null || materials.Count == 0) return "";
            var after = Simulate(GrowthTab.Awaken, target, materials);
            if (after.AwakenLevel == target.AwakenLevel) return "";
            var g = target.Template.Grade;
            int jump = CardGrowthRules.AwakenGrowthFor(g, after.AwakenLevel) - CardGrowthRules.AwakenGrowthFor(g, target.AwakenLevel);
            return $"{StepLabel(g, target.AwakenLevel)} → {StepLabel(g, after.AwakenLevel)} · OVR +{jump} 점프";
        }

        private static string StepLabel(Grade grade, int level) =>
            CardGrowthRules.IsTranscended(grade, level) ? "초월" : $"{CardGrowthRules.ClampAwaken(grade, level)}각";

        /// <summary>탭별 재료 후보(라인업 카드 · 대상 자신 제외). 아까운 카드가 뒤로 가도록 정렬한다.</summary>
        public static List<Player> MaterialCandidates(GrowthTab tab, Player target, IEnumerable<Player> inventory, IEnumerable<Player> roster)
        {
            if (target?.Template == null || tab == GrowthTab.Training) return new List<Player>();
            var locked = new HashSet<Player>(roster ?? Enumerable.Empty<Player>());
            var pool = (inventory ?? Enumerable.Empty<Player>()).Where(p => p?.Template != null && p != target && !locked.Contains(p));
            switch (tab)
            {
                case GrowthTab.Enhance:
                    return pool.OrderBy(p => CardGrowthRules.PowerRank(p.Template.Grade)).ThenBy(p => p.CalculateNeutralOVR())
                        .ThenBy(p => p.GetStatGrowth()).ToList();
                case GrowthTab.Awaken:
                    return pool.Where(p => IsValidAwakenMaterial(target, p))
                        .OrderByDescending(p => CardGrowthRules.AwakenGainFor(target, p)).ThenBy(p => p.GetStatGrowth()).ToList();
                case GrowthTab.Transcend:
                    return pool.Where(p => TranscendRules.IsCoreMaterial(target, p) || TranscendRules.IsSupportMaterial(target, p))
                        .OrderByDescending(p => TranscendRules.IsCoreMaterial(target, p))
                        .ThenBy(p => p.ReinforceLevel >= TranscendRules.SupportEnhancedLevel)
                        .ThenBy(p => p.ReinforceLevel + p.AwakenLevel).ThenBy(p => p.CalculateNeutralOVR()).ToList();
                case GrowthTab.LimitBreak:
                    return pool.Where(p => CardGrowthActions.IsValidLimitBreakMaterial(target, p))
                        .OrderByDescending(p => p.Template.RealPlayerId == target.Template.RealPlayerId)
                        .ThenBy(p => CardGrowthRules.PowerRank(p.Template.Grade)).ThenBy(p => p.CalculateNeutralOVR()).ToList();
                default:
                    return new List<Player>();
            }
        }

        /// <summary>[자동 선택] - 다음 단계(강화 1단계 / 각성 다음 임계점 / 한계 돌파 1장)에 필요한 만큼만 앞에서부터 고른다.</summary>
        public static List<Player> AutoSelect(GrowthTab tab, Player target, IReadOnlyList<Player> candidates)
        {
            var picked = new List<Player>();
            if (target?.Template == null || candidates == null || candidates.Count == 0) return picked;
            int max = MaxMaterials(tab);
            switch (tab)
            {
                case GrowthTab.LimitBreak:
                    picked.Add(candidates[0]);
                    break;
                case GrowthTab.Enhance:
                {
                    int goal = Math.Min(Player.MaxReinforceLevel, target.ReinforceLevel + 1);
                    foreach (var c in candidates)
                    {
                        if (picked.Count >= max) break;
                        picked.Add(c);
                        if (Simulate(tab, target, picked).ReinforceLevel >= goal) break;
                    }
                    break;
                }
                case GrowthTab.Awaken:
                {
                    int next = CardGrowthRules.NextAwakenThreshold(target.Template.Grade, target.AwakenLevel);
                    int cap = CardGrowthRules.MaterialAwakenCapFor(target.Template.Grade); // [TASK-KBO-189] 재료 각성은 9각까지
                    int goal = next > 0 && next < cap ? next : cap;
                    foreach (var c in candidates)
                    {
                        if (picked.Count >= max) break;
                        picked.Add(c);
                        if (Simulate(tab, target, picked).AwakenLevel >= goal) break;
                    }
                    break;
                }
                case GrowthTab.Transcend:
                    picked.AddRange(TranscendRules.AutoAssign(target, candidates, null, 0).Cards);
                    break;
            }
            return picked;
        }

        public static Player Clone(Player source)
        {
            if (source == null) return null;
            return new Player
            {
                InstanceId = source.InstanceId,
                Template = source.Template,
                ReinforceLevel = source.ReinforceLevel,
                ReinforceExp = source.ReinforceExp,
                AwakenLevel = source.AwakenLevel,
                LimitBreakLevel = source.LimitBreakLevel,
                TrainingLevel = source.TrainingLevel,
                StarLevel = source.StarLevel,
                CurrentStarType = source.CurrentStarType,
                AcquiredSkillIds = new List<string>(source.AcquiredSkillIds ?? new List<string>()),
                SkillSlots = (source.SkillSlots ?? new List<PlayerSkillSlot>()).Where(s => s != null).Select(s => s.Clone()).ToList(), // [TASK-KBO-190]
                MaxStamina = source.MaxStamina,
                CurrentStamina = source.CurrentStamina,
                CurrentCondition = source.CurrentCondition,
            };
        }

        /// <summary>대상 복제본에 이번 성장을 적용한 결과(원본은 바꾸지 않는다). 적용 불가면 변화 없는 복제본.</summary>
        public static Player Simulate(GrowthTab tab, Player target, IReadOnlyList<Player> materials, int gold = int.MaxValue)
        {
            var clone = Clone(target);
            if (clone?.Template == null) return clone;
            var list = (materials ?? Array.Empty<Player>()).Where(m => m != null).ToList();
            switch (tab)
            {
                case GrowthTab.Enhance:
                    if (list.Count > 0) UpgradeManager.ApplyEnhance(clone, list);
                    break;
                case GrowthTab.Awaken:
                    if (list.Count > 0) UpgradeManager.ApplyAwaken(clone, list);
                    break;
                case GrowthTab.LimitBreak:
                    CardGrowthActions.TryLimitBreak(clone, list.FirstOrDefault(), out _);
                    break;
                case GrowthTab.Training:
                    CardGrowthActions.TryTrain(clone, gold, out _, out _);
                    break;
                case GrowthTab.Transcend:
                    if (TranscendRules.CanAttempt(clone, out _)) clone.AwakenLevel = CardGrowthRules.TranscendLevel; // 재료 · 재화 검증은 TranscendRules.Validate
                    break;
            }
            return clone;
        }

        /// <summary>탭 설명 + 현재 진행도(성장 센터 상단 안내 문구).</summary>
        public static string Describe(GrowthTab tab, Player target, int gold)
        {
            if (target?.Template == null) return "성장 대상 선수를 선택하십시오.";
            var g = target.Template.Grade;
            switch (tab)
            {
                case GrowthTab.Enhance:
                    return target.ReinforceLevel >= Player.MaxReinforceLevel
                        ? "강화 10강 완료 - [한계 돌파]로 추가 성장할 수 있습니다."
                        : $"강화 {target.ReinforceLevel}강 (EXP {target.ReinforceExp}/{UpgradeConstants.GetRequiredExp(target)}) · 1강 = OVR +1, 최대 +{CardGrowthRules.MaxReinforceGrowth}\n보관 카드(최대 {UpgradeManager.MaxEnhanceMaterials}장)를 재료로 EXP를 쌓습니다.";
                case GrowthTab.Awaken:
                {
                    if (target.ReinforceLevel < CardGrowthRules.AwakenRequiredReinforce)
                        return CardGrowthRules.AwakenLockMessage(target.ReinforceLevel) + "\n[강화 탭으로 이동]해 +10강을 먼저 완료하십시오. · 재료: 같은 선수 +3각 / 같은 포지션 +1각";
                    if (target.AwakenLevel >= CardGrowthRules.MaterialAwakenCapFor(g))
                        return $"각성 {target.AwakenLabel} 완성 · " + (TranscendRules.CanAttempt(target, out _) ? "[초월] 탭에서 복합 재료로 초월하십시오." : CardGrowthRules.AwakenStageNote(g, target.AwakenLevel));
                    int next = CardGrowthRules.NextAwakenThreshold(g, target.AwakenLevel);
                    string nextText = next > 0
                        ? $" · 다음 임계점 {CardGrowthRules.AwakenLabel(g, next)} = OVR +{CardGrowthRules.AwakenGrowthFor(g, next)}"
                        : "";
                    return $"각성 {target.AwakenLabel} (OVR +{target.AwakenGrowth}/{CardGrowthRules.AwakenGrowthCap(g)}){nextText}\n" +
                           $"{CardGrowthRules.AwakenStageNote(g, target.AwakenLevel)} · 재료: 같은 시즌 등급 - 같은 선수 +3각 / 같은 포지션({CardGrowthRules.PositionKey(target)}) +1각";
                }
                case GrowthTab.Transcend:
                    return TranscendRules.CanAttempt(target, out var transcendReason)
                        ? $"초월 준비 완료(+10강 · 9각) · 비용 {TranscendRules.CostLabel(g)}\n핵심: 같은 선수 1장(또는 초월 핵심 대체권) + 보조: 같은 포지션({CardGrowthRules.PositionKey(target)}) 다른 선수 2장(+5강이면 1장)"
                        : $"{transcendReason}\n초월 조건: +10강 · 9각 · 같은 선수 1장 + 같은 포지션 2장(+5강 1장) + {TranscendRules.CostLabel(g)}";
                case GrowthTab.LimitBreak:
                    return CardGrowthActions.CanLimitBreak(target, out var reason)
                        ? $"한계 돌파 {target.LimitBreakLevel}/{CardGrowthRules.LimitBreakCap(g)}단계 · 1단계 = OVR +1\n재료 1장(동일 선수 또는 같은 등급 이상)을 소모합니다."
                        : $"한계 돌파 {target.LimitBreakLevel}/{CardGrowthRules.LimitBreakCap(g)}단계 - {reason}";
                case GrowthTab.Training:
                    return target.TrainingLevel >= CardGrowthRules.TrainingCap(g)
                        ? $"특훈 최대 단계({CardGrowthRules.TrainingCap(g)}단계)입니다."
                        : $"특훈 {target.TrainingLevel}/{CardGrowthRules.TrainingCap(g)}단계 · 1단계 = OVR +1\n비용 볼 {CardGrowthActions.TrainingGoldCost(target.TrainingLevel):N0} (보유 {gold:N0})";
                default:
                    return "";
            }
        }

        /// <summary>세부 스탯 변화 미리보기("파워 70 → 72" 줄 목록). 성장은 전 항목 균등 가산이라 before/after 차이가 같다.</summary>
        public static List<string> StatPreviewLines(Player before, Player after)
        {
            var lines = new List<string>();
            if (before?.Template == null || after?.Template == null) return lines;
            if (before.Template.IsPitcher)
            {
                var a = before.GetEffectivePitcherStats();
                var b = after.GetEffectivePitcherStats();
                lines.Add(Line("구위", a.Stuff, b.Stuff));
                lines.Add(Line("구속", a.Velocity, b.Velocity));
                lines.Add(Line("변화", a.Movement, b.Movement));
                lines.Add(Line("제구", a.Control, b.Control));
                lines.Add(Line("체력", a.Stamina, b.Stamina));
            }
            else
            {
                var a = before.GetEffectiveBatterStats();
                var b = after.GetEffectiveBatterStats();
                lines.Add(Line("파워", a.Power, b.Power));
                lines.Add(Line("정확", a.Contact, b.Contact));
                lines.Add(Line("선구", a.Discipline, b.Discipline));
                lines.Add(Line("주력", a.Speed, b.Speed));
                lines.Add(Line("수비", a.Defense, b.Defense));
            }
            return lines;
        }

        private static string Line(string label, int before, int after) =>
            after == before ? $"{label}  {before}" : $"{label}  {before} → <color=#5EE08A>{after} (+{after - before})</color>";
    }
}
