using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Controllers;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.EditorTools;
using KBOManager.Managers;
using KBOManager.Models;
using KBOManager.Services;
using KBOManager.Simulation;
using KBOManager.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-GM-05] 치어리더 시스템 고도화 6대 자동 검증(Unity CLI BatchPipelineGM05 1회 실행):
    ///   1) 구단 15인 풀 · 4~6인 엔트리 규칙 · 자동 편성  2) 4대 스탯 · CHEER · 강화/각성 반영  3) 체력 로테이션 · 전담 응원(보직 불만 해소)
    ///   4) 기본 실책률(GM-04 D.1) · 마운드 응원 실책 억제  5) 대시보드 · 전력 비교 → 치어리더 화면 → [X] 복귀  6) UI 무결성(겹침 0 · 15pt+ · Bold 0)
    /// </summary>
    public class GM05VerificationRunner
    {
        private static IReadOnlyList<PlayerTemplate> templates;
        private static List<Cheerleader> cheer;
        private static GameObject dbObject;
        private static GMLiveSeasonSimulator season;
        private static readonly List<GMNewsItem> seasonNews = new List<GMNewsItem>();
        private static readonly Dictionary<string, bool> normalChemistry = new Dictionary<string, bool>();
        private static readonly Dictionary<string, long> startBudget = new Dictionary<string, long>();
        private readonly List<Object> created = new List<Object>();
        private bool sceneOpened;

        [SetUp]
        public void EnsureDatabase()
        {
            if (templates != null && templates.Count > 0 && templates.All(t => t != null)) return;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            dbObject = new GameObject("GM05_PlayerDatabase");
            templates = dbObject.AddComponent<PlayerDatabase>().AllTemplates.ToList();
            cheer = GMRosterLoader.AllCheerleaderTemplates();
            Assert.Greater(templates.Count, 500);
        }

        [OneTimeTearDown]
        public void Unload()
        {
            if (templates != null) foreach (var t in templates) if (t != null) Object.DestroyImmediate(t);
            templates = null;
            season = null;
            seasonNews.Clear();
            normalChemistry.Clear();
            startBudget.Clear();
            if (dbObject != null) Object.DestroyImmediate(dbObject);
        }

        [TearDown]
        public void TearDown()
        {
            GameSettings.UseVirtualNames = false;
            if (templates != null) NameAliasTable.ApplyDisplayNames(templates.Where(t => t != null), false);
            foreach (var obj in created) if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = false;
        }

        private static GMLeagueState NewLeague(GMStartMode mode = GMStartMode.RealCurrent2026, string team = "SAM", int seed = 20260328)
        {
            var league = GMRosterLoader.LoadModeRoster(mode, team, false, templates, cheer);
            league.Seed = seed;
            return league;
        }

        /// <summary>정상 케미스트리 그대로 한 시즌 완주(내 구단 박스스코어 · 소식 수집, 시작 시점 구단별 케미스트리 · 예산 기록).</summary>
        private static GMLiveSeasonSimulator SharedSeason()
        {
            if (season != null) return season;
            var league = NewLeague(seed: 5055);
            foreach (var t in league.Teams.Values)
            {
                normalChemistry[t.TeamCode] = TeamChemistryEngine.EvaluateRoster(t.AvailableRoster, t.PayrollCap, t.CheerLeadershipBuff).ErrorRateMultiplier <= 1f;
                startBudget[t.TeamCode] = t.Budget;
            }
            var sim = new GMLiveSeasonSimulator(league) { RecordAllBoxScores = true };
            var seen = new HashSet<GMNewsItem>();
            sim.OnDayCompleted += () => { foreach (var n in sim.League.News.Take(40)) if (seen.Add(n)) seasonNews.Add(n); };
            Assert.IsTrue(sim.StartRun(GMRunMode.FullSeason));
            sim.RunUntilStop();
            Assert.IsTrue(sim.IsSeasonComplete);
            season = sim;
            return season;
        }

        private static Cheerleader Card(string name, CheerleaderGrade grade, Team team = Team.Samsung, int reinforce = 0, int stars = 1) =>
            new Cheerleader(Guid.NewGuid().ToString(), name, grade, 1, 1f, team: team) { ReinforceLevel = reinforce, StarLevel = stars };

        private GameObject NewCanvas()
        {
            var canvasGo = new GameObject("GM05_Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            created.Add(canvasGo);
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); // [TASK-GM-06] Landscape
            return canvasGo;
        }

        private T NewView<T>(GameObject canvas, string name) where T : MonoBehaviour
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            return go.AddComponent<T>();
        }

        private static string T(Transform root, string path) => root.Find(path).GetComponent<Text>().text;
        private static void Click(Transform root, string path) => root.Find(path).GetComponent<Button>().onClick.Invoke();

        // ================================================================== 1) 15인 풀 · 4~6인 엔트리

        [Test]
        public void T1_TeamPool12to15_Entry4to6_Blocked_AutoArrange()
        {
            Assert.IsTrue(GMFeatureFlags.ENABLE_CHEERLEADER_CORE_SYSTEM, "치어리더 핵심 시스템 고정 ON");
            foreach (var mode in new[] { GMStartMode.RealCurrent2026, GMStartMode.AllTimeDream, GMStartMode.StoryCampaign })
            {
                var league = NewLeague(mode);
                foreach (var team in league.Teams.Values)
                {
                    string label = $"{mode} {team.TeamCode}";
                    Assert.That(team.CheerleaderPool.Count, Is.InRange(GMCheerleaderRoster.MinPool, GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX), label + " 풀 12~15명");
                    Assert.IsTrue(team.CheerleaderPool.All(c => c.Team == team.Team), label + " 구단 소속");
                    Assert.AreEqual(team.CheerleaderPool.Count, team.CheerleaderPool.Select(CheerSquad.PersonKey).Distinct().Count(), label + " 동일 인물 없음");
                    Assert.AreEqual(GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_DEFAULT, team.CheerEntry.Count(), label + " 기본 엔트리 5인");
                    Assert.IsTrue(team.CheerAutoRotate, label + " 자동 로테이션 기본 켬([TASK-GM-06] D.2 - 내 구단 포함)");
                }
            }

            var user = NewLeague().UserTeam;
            var pool = user.CheerleaderPool;
            Assert.IsFalse(GMCheerleaderRoster.TrySetEntry(user, pool.Take(3).ToList(), out var r3), "3명 차단");
            StringAssert.Contains("4~6명", r3);
            Assert.IsFalse(GMCheerleaderRoster.TrySetEntry(user, pool.Take(7).ToList(), out _), "7명 차단");
            Assert.IsFalse(GMCheerleaderRoster.TrySetEntry(user, new List<Cheerleader> { pool[0], pool[0], pool[1], pool[2] }, out _), "중복 차단");
            Assert.IsTrue(GMCheerleaderRoster.TrySetEntry(user, pool.Skip(8).Take(4).ToList(), out _), "4명 허용");
            Assert.AreEqual(4, user.CheerEntrySize);
            CollectionAssert.AreEqual(pool.Skip(0).Take(4).ToList(), user.CheerEntry.ToList());
            Assert.IsFalse(GMCheerleaderRoster.TryRemove(user, user.CheerEntry.First(), out var rMin), "4명에서 해제 차단");
            StringAssert.Contains("최소 4명", rMin);
            Assert.IsTrue(GMCheerleaderRoster.TryAdd(user, pool[10], out _));
            Assert.IsTrue(GMCheerleaderRoster.TryAdd(user, pool[11], out _));
            Assert.AreEqual(6, user.CheerEntrySize);
            Assert.IsFalse(GMCheerleaderRoster.TryAdd(user, pool[12], out var rMax), "6명에서 배치 차단");
            StringAssert.Contains("최대 6명", rMax);
            Assert.IsTrue(GMCheerleaderRoster.TryToggle(user, user.CheerEntry.Last(), out _), "6 → 5명 해제");
            Assert.AreEqual(5, user.CheerEntrySize);
            Assert.AreEqual(GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX, pool.Count, "풀 인원 유지");

            // [최적 컨디션 자동 편성] - 지친 인원(체력 30 미만) 제외 · 5인 · 역할 스탯 순
            var tired = pool.OrderByDescending(GMCheerleaderStats.Cheer).Take(3).ToList();
            foreach (var c in tired) c.Fatigue = 80;
            var auto = GMCheerleaderRoster.AutoArrange(user);
            Assert.AreEqual(5, auto.Count);
            CollectionAssert.AreEqual(auto, user.CheerEntry.ToList());
            Assert.IsTrue(auto.All(c => !GMCheerleaderStats.IsTired(c)), "체력 30 이상만");
            Assert.AreEqual(auto.Max(GMCheerleaderStats.Leadership), GMCheerleaderStats.Leadership(auto[0]), "1.응원단장 = 리더십 최고");
            Assert.AreEqual(auto.Skip(1).Max(GMCheerleaderStats.Batting), GMCheerleaderStats.Batting(auto[1]), "2.타격 응원 = 타격 응원력 최고");

            // UI로 배치 · 해제 차단 메시지
            var canvas = NewCanvas();
            var view = NewView<GMCheerleaderEntryUIController>(canvas, "GM05_Cheer");
            view.Build();
            view.Open(user);
            GMCheerleaderRoster.TrySetEntry(user, pool.Where(c => !tired.Contains(c)).Take(4).ToList(), out _);
            view.Refresh();
            view.SelectSlot(0);
            Click(view.Root, "ToggleEntryButton");
            Assert.AreEqual(4, user.CheerEntrySize, "UI 해제 차단");
            StringAssert.Contains("최소 4명", view.LastMessage);
            view.Select(pool.First(c => !GMCheerleaderRoster.IsInEntry(user, c)));
            Click(view.Root, "ToggleEntryButton");
            Assert.AreEqual(5, user.CheerEntrySize, "UI 배치");
            Assert.AreEqual("빈 슬롯", view.Root.Find("Slot5").GetComponentInChildren<Text>().text);
            Click(view.Root, "AutoArrangeButton");
            Assert.AreEqual(5, user.CheerEntrySize);
            StringAssert.Contains("자동 편성", view.LastMessage);
        }

        // ================================================================== 2) 4대 스탯 · CHEER · 성장 반영

        [Test]
        public void T2_FourStats_Cheer_GrowthRaisesStatsAndBuffs()
        {
            var team = NewLeague().UserTeam;
            foreach (var c in team.CheerleaderPool)
            {
                int[] v = Enumerable.Range(0, 4).Select(k => GMCheerleaderStats.Stat(c, (GMCheerStat)k)).ToArray();
                Assert.IsTrue(v.All(x => x > 0 && x <= 99), c.Name + " 4대 스탯 1~99");
                Assert.AreEqual((int)Math.Round(v.Average(), MidpointRounding.AwayFromZero), GMCheerleaderStats.Cheer(c), "CHEER = 4대 평균");
                Assert.AreEqual(100, GMCheerleaderStats.Stamina(c), "시작 체력 100");
            }

            var target = Card("성장테스트", CheerleaderGrade.LIVE_NORMAL);
            var mates = new[] { Card("동료가", CheerleaderGrade.LIVE_NORMAL), Card("동료나", CheerleaderGrade.LIVE_NORMAL), Card("동료다", CheerleaderGrade.LIVE_NORMAL), Card("동료라", CheerleaderGrade.LIVE_NORMAL) };
            var entry = new List<Cheerleader> { mates[0], target, mates[3], mates[1], mates[2] }; // 2.타격 응원 = target
            int[] before = Enumerable.Range(0, 4).Select(k => GMCheerleaderStats.Stat(target, (GMCheerStat)k)).ToArray();
            int cheerBefore = GMCheerleaderStats.Cheer(target);
            int batBefore = GMCheerleaderRoster.BuildMatchEffects(entry, Team.Samsung, true, 0).BatterContactDiscipline;
            float clutchBefore = GMCheerleaderRoster.ClutchBonus(entry);
            int leadBefore = GMCheerleaderRoster.LeadershipTeamworkBonus(entry);

            target.ReinforceLevel = CheerGrowth.MaxReinforce; // +10강
            target.StarLevel = CheerGrowth.MaxStars;          // ★5
            int[] after = Enumerable.Range(0, 4).Select(k => GMCheerleaderStats.Stat(target, (GMCheerStat)k)).ToArray();
            for (int k = 0; k < 4; k++) Assert.IsTrue(after[k] > before[k] || after[k] == GMCheerleaderStats.MaxStat, $"{GMCheerleaderStats.StatLabels[k]} 상승");
            Assert.Greater(GMCheerleaderStats.Cheer(target), cheerBefore, "CHEER 상승");
            Assert.Greater(GMCheerleaderRoster.BuildMatchEffects(entry, Team.Samsung, true, 0).BatterContactDiscipline, batBefore, "타격 응원 버프 상승(+10강 · ★5 티어 도약)");
            Assert.Greater(GMCheerleaderRoster.ClutchBonus(entry), clutchBefore, "타격 응원력 클러치 상승");
            Assert.GreaterOrEqual(GMCheerleaderRoster.LeadershipTeamworkBonus(entry), leadBefore, "리더십 팀워크");
            Assert.That(GMCheerleaderRoster.LeadershipTeamworkBonus(entry), Is.InRange(1, 12), "팀워크 +1~+12");
            Assert.AreEqual(0, GMCheerleaderRoster.LeadershipTeamworkBonus(entry.Take(3)), "엔트리 4명 미만 = 효과 없음");

            // ★ 각성 테두리 구분 · 기존 성장 규칙 보존(강화 비용 · 동일 인물 +2★ 각성)
            Assert.AreNotEqual(GMCheerleaderStats.StarFrameColor(Card("a", CheerleaderGrade.LIVE_NORMAL, stars: 1)), GMCheerleaderStats.StarFrameColor(Card("b", CheerleaderGrade.LIVE_NORMAL, stars: 3)));
            Assert.AreEqual("프리즘 테두리", GMCheerleaderStats.StarFrameLabel(target));
            Assert.AreEqual("골드 테두리", GMCheerleaderStats.StarFrameLabel(Card("c", CheerleaderGrade.ICON, stars: 3)));
            Assert.Greater(CheerGrowth.ReinforceCost(0), 0);
            var self = Card("동일인", CheerleaderGrade.LIVE_NORMAL);
            var material = Card("동일인", CheerleaderGrade.ICON);
            Assert.IsTrue(CheerGrowth.CanAwaken(self, material, null, out _));
            Assert.AreEqual(2, CheerGrowth.ApplyAwaken(self, material), "동일 인물 +2★");
            Assert.AreEqual(2, CheerGrowth.EffectiveTier(self), "LIVE 1 + ★3 도약 1");
        }

        // ================================================================== 3) 체력 로테이션 · 전담 응원

        [Test]
        public void T3_StaminaRotation_DedicatedCheer_ResolvesRoleConflict()
        {
            var league = NewLeague(team: "LG", seed: 3033);
            var sim = new GMLiveSeasonSimulator(league);
            var user = league.UserTeam;
            user.CheerAutoRotate = false; // [TASK-GM-06] 기본은 켬(D.2) - 단장이 수동 로테이션으로 바꾼 경우를 검증한다
            var entry = user.CheerEntry.ToList();
            var bench = user.CheerleaderPool.Skip(user.CheerEntrySize).ToList();
            foreach (var c in bench) c.Fatigue = 50;
            sim.StartRun(GMRunMode.SingleGame);
            sim.RunUntilStop();
            CollectionAssert.AreEqual(entry, user.CheerEntry.ToList(), "유저가 자동 로테이션을 끄면 엔트리 유지");
            Assert.IsTrue(entry.All(c => GMCheerleaderStats.Stamina(c) == 100 - GMCheerleaderStats.DrainPerGame), "단상 출전 체력 -9");
            Assert.IsTrue(bench.All(c => GMCheerleaderStats.Stamina(c) == 50 + GMCheerleaderStats.RecoverPerGame), "벤치 휴식 체력 +14");

            sim.StartRun(GMRunMode.FullSeason);
            for (int d = 0, guard = 0; d < 9 && guard < 200; guard++)
            {
                if (sim.PendingInterrupt != null) { sim.ResolveInterrupt(GMInterruptChoice.AutoCallUp); continue; }
                if (sim.StepGameDay()) d++;
            }
            while (sim.PendingInterrupt != null) sim.ResolveInterrupt(GMInterruptChoice.AutoCallUp);
            sim.Stop();
            Assert.AreEqual(10, sim.GamesPlayed);
            Assert.IsTrue(user.CheerEntry.All(GMCheerleaderStats.IsTired), "10경기 연속 단상 → 체력 30 미만");
            Assert.AreEqual(0.5f, GMCheerleaderStats.Efficiency(user.CheerEntry.First()));
            // 같은 5인을 체력만 회복시킨 경우와 비교 - 지친 엔트리는 리더십 · 실책 억제 효율 저하
            var tiredEntry = user.CheerEntry.ToList();
            int leadTired = GMCheerleaderRoster.LeadershipTeamworkBonus(tiredEntry);
            float errTired = GMCheerleaderRoster.ErrorReduction(tiredEntry);
            var saved = tiredEntry.Select(c => c.Fatigue).ToList();
            foreach (var c in tiredEntry) c.Fatigue = 0;
            Assert.Less(leadTired, GMCheerleaderRoster.LeadershipTeamworkBonus(tiredEntry), "지친 엔트리 리더십 팀워크 저하");
            Assert.Less(errTired, GMCheerleaderRoster.ErrorReduction(tiredEntry), "지친 엔트리 실책 억제 저하");
            for (int i = 0; i < tiredEntry.Count; i++) tiredEntry[i].Fatigue = saved[i];
            foreach (var ai in league.Teams.Values.Where(t => !t.IsUserTeam))
                Assert.IsFalse(ai.CheerEntry.Any(GMCheerleaderStats.IsTired), ai.TeamCode + " AI 자동 로테이션");
            GMCheerleaderRoster.AutoArrange(user);
            Assert.IsTrue(user.CheerEntry.All(c => !GMCheerleaderStats.IsTired(c)), "[최적 컨디션 자동 편성] = 휴식 인원 투입");

            // 전담 응원 - Ego 4 타자 6명(중심타선 4자리 초과 2명 불만) → 에이스/리더 2명 매칭 → 보직 충돌 해소 · 팀워크 상승
            var team = NewLeague(team: "KIA", seed: 9).UserTeam;
            foreach (var p in team.Roster) { p.HasRoleConcessionBonus = false; p.EgoLevel = Math.Min(p.EgoLevel, p.IsPitcher ? 4 : 3); }
            foreach (var p in team.Roster.Where(x => !x.IsPitcher).Take(6)) p.EgoLevel = 4;
            var report0 = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.CheerLeadershipBuff);
            Assert.IsTrue(report0.Has(AllStarOverloadPenalty.LineupRoleConflict), "보직 자존심 충돌 발동");
            Assert.AreEqual(2, TeamChemistryEngine.GetDissatisfiedStars(team.AvailableRoster).Count);
            var cheerEntry = team.CheerEntry.ToList();
            cheerEntry[1].Grade = CheerleaderGrade.ICON; // 에이스
            cheerEntry[2].Grade = CheerleaderGrade.LIVE_NORMAL; cheerEntry[2].StarLevel = 1;
            var lowEgo = team.Roster.First(p => p.EgoLevel < 4);
            Assert.IsFalse(GMCheerleaderRoster.TryDedicate(team, cheerEntry[0], lowEgo, out _), "Ego 4 미만 대상 불가");
            var target1 = GMCheerleaderRoster.DedicationTarget(team);
            int morale1 = target1.PersonalMorale;
            Assert.IsFalse(GMCheerleaderRoster.TryDedicate(team, cheerEntry[2], target1, out var notAce), "에이스/리더가 아니면 불가");
            StringAssert.Contains("에이스", notAce);
            Assert.IsTrue(GMCheerleaderRoster.TryDedicate(team, cheerEntry[0], target1, out _), "1번 응원단장(리더) 전담");
            Assert.AreEqual(Math.Min(100, morale1 + 15), target1.PersonalMorale, "만족도 +15");
            Assert.IsTrue(target1.HasRoleConcessionBonus, "보직 양보 인센티브 동급");
            var target2 = GMCheerleaderRoster.DedicationTarget(team);
            Assert.AreNotSame(target1, target2);
            Assert.IsTrue(GMCheerleaderRoster.TryDedicate(team, cheerEntry[1], target2, out _), "ICON 에이스 전담");
            Assert.IsFalse(GMCheerleaderRoster.TryDedicate(team, cheerEntry[3], team.Roster.First(p => p.EgoLevel >= 4 && p != target1 && p != target2), out _), "최대 2쌍");
            var report1 = TeamChemistryEngine.EvaluateRoster(team.AvailableRoster, team.PayrollCap, team.CheerLeadershipBuff);
            Assert.IsFalse(report1.Has(AllStarOverloadPenalty.LineupRoleConflict), "보직 충돌 해소");
            Assert.Greater(report1.TeamworkScore, report0.TeamworkScore, "팀워크 상승");
            StringAssert.Contains(target1.Template.PlayerName, GMCheerleaderRoster.DedicationLabel(team));

            // 리더를 엔트리에서 빼면 매칭 해제 · 효과 회수
            GMCheerleaderRoster.TryAdd(team, team.CheerleaderPool[team.CheerEntrySize], out _);
            Assert.IsTrue(GMCheerleaderRoster.TryRemove(team, cheerEntry[0], out _));
            Assert.AreEqual(1, team.CheerDedications.Count, "엔트리 이탈 = 매칭 해제");
            Assert.IsFalse(target1.HasRoleConcessionBonus, "매칭으로 준 효과 회수");
        }

        // ================================================================== 4) 기본 실책률 · 마운드 응원 억제

        [Test]
        public void T4_BaseErrorRate_InNormalChemistry_MoundCheerReducesErrors()
        {
            var sim = SharedSeason();
            var league = sim.League;
            var normal = normalChemistry.Where(p => p.Value).Select(p => p.Key).ToList();
            Assert.IsNotEmpty(normal, "정상 케미스트리 구단");
            foreach (var code in normal)
            {
                int errors = league.Stats.Values.Where(s => s.TeamCode == code).Sum(s => s.Errors);
                Assert.GreaterOrEqual(errors, 1, code + " 정상 케미스트리에서도 기본 실책 발생");
            }
            string me = league.SelectedTeamCode;
            Assert.AreEqual(sim.AllUserBoxScores.Sum(b => b.HomeCode == me ? b.HomeE : b.AwayE), league.Stats.Values.Where(s => s.TeamCode == me).Sum(s => s.Errors), "개인 실책 합 = 팀 실책");
            Assert.Greater(league.Stats.Values.Sum(s => s.Errors), 50, "리그 전체 실책");

            // 확률 함수: 마운드 응원력이 높을수록 실책 확률 감소(최대 35%), 수비력이 높을수록 기본 실책률 감소
            var low = new List<Cheerleader> { Card("가", CheerleaderGrade.LIVE_NORMAL), Card("나", CheerleaderGrade.LIVE_NORMAL), Card("다", CheerleaderGrade.LIVE_NORMAL), Card("라", CheerleaderGrade.LIVE_NORMAL) };
            var high = Enumerable.Range(0, 6).Select(i => Card("상" + i, CheerleaderGrade.LEGEND, reinforce: 10, stars: 5)).ToList();
            float rLow = GMCheerleaderRoster.ErrorReduction(low), rHigh = GMCheerleaderRoster.ErrorReduction(high);
            Assert.Greater(rLow, 0f);
            Assert.Greater(rHigh, rLow, "마운드 응원력 높을수록 억제");
            Assert.LessOrEqual(rHigh, GMCheerleaderRoster.MaxErrorReduction + 1e-6f, "최대 35%");
            Assert.AreEqual(0.35f, rHigh, 1e-4f, "최상위 엔트리 = 35%");
            var chemLow = new GMChemistryModifiers { CheerErrorReduction = rLow };
            var chemHigh = new GMChemistryModifiers { CheerErrorReduction = rHigh };
            var chemNone = new GMChemistryModifiers();
            Assert.AreEqual(GMChemistryModifiers.DefaultBaseErrorRate, chemNone.ErrorChance, 1e-6, "치어리더 없음 = 기본 실책률([TASK-GM-06] 2.2%)");
            Assert.Less(chemHigh.ErrorChance, chemLow.ErrorChance);
            Assert.Less(chemLow.ErrorChance, chemNone.ErrorChance);
            Assert.AreEqual(GMChemistryModifiers.DefaultBaseErrorRate * 0.65, chemHigh.ErrorChance, 1e-6, "35% 억제");
            Assert.Less(GMChemistryModifiers.BaseErrorRateFor(85), GMChemistryModifiers.BaseErrorRateFor(50), "수비력 높을수록 기본 실책률 감소");
            var split = new GMChemistryModifiers { ErrorRateMultiplier = 2f };
            Assert.Greater(split.ErrorChance, chemNone.ErrorChance, "파벌 분열 실책 2배 추가분 유지");

            // 홈 흥행: 예산 증가 · 결과 화면 요약 · 최신 소식
            foreach (var t in league.Teams.Values) Assert.Greater(t.Budget, startBudget[t.TeamCode], t.TeamCode + " 홈 관중 수익");
            var homeBox = sim.AllUserBoxScores.First(b => b.HomeCode == me);
            StringAssert.Contains("홈 흥행 보너스", homeBox.CheerSummary);
            StringAssert.Contains("팀워크 +", homeBox.CheerSummary);
            Assert.That(homeBox.CheerEntryNames.Count, Is.InRange(4, 6));
            Assert.IsTrue(seasonNews.Any(n => n.Kind == GMNewsKind.Cheer && n.Body.Contains("열띤 단상 응원")), "응원단 소식");
        }

        // ================================================================== 5) 화면 동선

        [Test]
        public void T5_Navigation_DashboardAndPreGame_OpenCheerView_CloseReturns()
        {
            var sim = new GMLiveSeasonSimulator(NewLeague(team: "NC"));
            var canvas = NewCanvas();
            var dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM05_Dashboard");
            dash.Build();
            var cheerView = NewView<GMCheerleaderEntryUIController>(canvas, "GM05_Cheer");
            cheerView.Build();
            cheerView.gameObject.SetActive(false);
            dash.CheerView = cheerView;
            dash.Bind(sim);

            Assert.IsNotNull(dash.Root.Find("CheerEntryButton"), "대시보드 [치어리더 엔트리 (4~6인)]");
            StringAssert.Contains("치어리더 엔트리", dash.Root.Find("CheerEntryButton").GetComponentInChildren<Text>().text);
            Click(dash.Root, "CheerEntryButton");
            Assert.IsTrue(cheerView.gameObject.activeSelf, "대시보드 → 치어리더 화면");
            Assert.AreSame(sim.League.UserTeam, cheerView.CurrentTeam);
            Assert.IsTrue(dash.IsPaused, "관리 중 진행 일시정지");
            Click(cheerView.Root, "CloseButton");
            Assert.IsFalse(cheerView.gameObject.activeSelf, "[X] 닫기");
            Assert.IsTrue(dash.gameObject.activeSelf, "대시보드 복귀");
            Assert.IsFalse(dash.IsPaused, "닫으면 이어서 진행");

            var prePost = NewView<GMMatchPrePostUIController>(canvas, "GM05_PrePost");
            prePost.Build();
            prePost.CheerView = cheerView;
            Assert.IsTrue(prePost.ShowPreGameView(sim));
            Click(prePost.PreRoot, "CheckButton");
            Assert.IsTrue(cheerView.gameObject.activeSelf, "전력 비교 [라인업/치어리더 점검] → 치어리더 화면");
            Assert.AreEqual(cheerView.transform.parent.childCount - 1, cheerView.transform.GetSiblingIndex(), "최상단");
            Click(cheerView.Root, "AutoArrangeButton");
            string leader = sim.League.UserTeam.CheerEntry.First().DisplayName;
            Click(cheerView.Root, "BackButton");
            Assert.IsFalse(cheerView.gameObject.activeSelf);
            Assert.IsTrue(prePost.gameObject.activeSelf && prePost.PreRoot.gameObject.activeSelf, "전력 비교 복귀");
            bool userHome = prePost.CurrentPreview.Home.Code == "NC";
            StringAssert.Contains(leader, T(prePost.PreRoot, userHome ? "HomeCheer" : "AwayCheer"), "바뀐 엔트리 반영");
            StringAssert.Contains("실책 -", T(prePost.PreRoot, userHome ? "HomeCheer" : "AwayCheer"));

            // 경기 시작 → 결과 화면 응원 요약
            Assert.IsTrue(prePost.StartMatch());
            StringAssert.Contains("오늘의 응원단", T(prePost.PostRoot, "CheerSummary"));
            StringAssert.Contains("열띤 단상 응원", T(prePost.PostRoot, "CheerSummary"));
        }

        // ================================================================== 6) UI 무결성

        [Test]
        public void T6_CheerEntryUI_NoOverlap_Min15pt_NoBold_Scene()
        {
            var sim = SharedSeason();
            var canvas = NewCanvas();
            var view = NewView<GMCheerleaderEntryUIController>(canvas, "GM05_Cheer");
            view.Build();
            var team = sim.League.UserTeam;
            view.Open(team);
            Assert.AreEqual(GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX, Enumerable.Range(0, GMCheerleaderEntryUIController.PoolRows).Count(i => view.Root.Find($"Pool{i}").gameObject.activeSelf), "15인 목록");
            StringAssert.StartsWith("CHEER ", T(view.Root, "DetailCheer"));
            for (int k = 0; k < 4; k++) Assert.AreEqual(GMCheerleaderStats.StatLabels[k], T(view.Root, $"GaugeLabel{k}"));
            Assert.Greater(int.Parse(T(view.Root, "GaugeValue0")), 0);
            CheckLayer(view.Root, "치어리더 엔트리 5인");
            GMCheerleaderRoster.TrySetEntry(team, team.CheerleaderPool.Take(4).ToList(), out _);
            view.Refresh();
            CheckLayer(view.Root, "치어리더 엔트리 4인(빈 슬롯 2)");
            GMCheerleaderRoster.TrySetEntry(team, team.CheerleaderPool.Take(6).ToList(), out _);
            view.SelectPool(14);
            CheckLayer(view.Root, "치어리더 엔트리 6인 · 벤치 선택");

            var prePost = NewView<GMMatchPrePostUIController>(canvas, "GM05_PrePost");
            prePost.Build();
            prePost.Attach(sim);
            prePost.ShowPostGameBoxScoreView(sim.League.LastUserMatchBoxScore);
            CheckLayer(prePost.PostRoot, "결과 화면(응원 요약)");
            Assert.IsTrue(prePost.ShowPreGameView(new GMLiveSeasonSimulator(NewLeague(team: "HAN"))));
            CheckLayer(prePost.PreRoot, "전력 비교(응원 효과)");
            var dash = NewView<GMLiveLeagueDashboardUIController>(canvas, "GM05_Dashboard");
            dash.Build();
            dash.Bind(new GMLiveSeasonSimulator(NewLeague(team: "KT")));
            CheckLayer(dash.Root, "대시보드(치어리더 바로가기)");

            OpenScene();
            var sceneCheer = Object.FindAnyObjectByType<GMCheerleaderEntryUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(sceneCheer, "씬에 치어리더 엔트리 화면");
            Assert.IsFalse(sceneCheer.gameObject.activeSelf);
            Assert.IsNotNull(sceneCheer.transform.Find(GMCheerleaderEntryUIController.RootName));
            var sceneDash = Object.FindAnyObjectByType<GMLiveLeagueDashboardUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(sceneDash.transform.Find(GMLiveLeagueDashboardUIController.RootName).Find("CheerEntryButton"), "씬 대시보드 바로가기");
            var scenePrePost = Object.FindAnyObjectByType<GMMatchPrePostUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(scenePrePost.transform.Find(GMMatchPrePostUIController.PostRootName).Find("CheerSummary"), "씬 결과 화면 응원 요약");
            Assert.IsNotNull(Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include), "기존 치어리더 관리(보유 · 편성) 화면 보존");
            var bold = SetupTask191.SceneTexts().Where(x => x.fontStyle == FontStyle.Bold || x.fontStyle == FontStyle.BoldAndItalic).Select(x => x.name).ToList();
            Assert.IsEmpty(bold, "씬 전체 FontStyle.Bold 0건");
        }

        /// <summary>layer 직속 자식 중 활성 텍스트/버튼 영역 겹침 0 · 15pt 이상 · Normal.</summary>
        private static void CheckLayer(Transform layer, string label)
        {
            var items = new List<Transform>();
            foreach (Transform child in layer)
            {
                if (!child.gameObject.activeSelf) continue;
                if (child.GetComponent<Text>() != null || child.GetComponent<Button>() != null) items.Add(child);
            }
            var overlaps = new List<string>();
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                    if (Task191Report.Overlap(items[i], items[j])) overlaps.Add($"{items[i].name} ↔ {items[j].name}");
            Assert.IsEmpty(overlaps, $"{label} 텍스트 겹침 0건");
            foreach (var t in layer.GetComponentsInChildren<Text>(true))
            {
                Assert.AreEqual(FontStyle.Normal, t.fontStyle, t.name);
                Assert.GreaterOrEqual(TextTidy.EffectiveSize(t), 15, $"{label} {t.name} 15pt 이상");
            }
        }

        private void OpenScene()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
        }
    }
}
