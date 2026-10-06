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
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace KBOManager.EditorTests
{
    /// <summary>
    /// [TASK-GM-01] 『스토브리그: 단장의 시간』 전환 5대 자동 검증(Unity CLI BatchPipelineGM01 1회 실행):
    ///   1) 10개 구단 계보 · 실명/가상명 전환  2) 3대 시작 모드 로스터(10구단 28인 · 치어리더 풀/엔트리 · 2026 시작 → 2027 해 넘김)
    ///   3) 슈퍼스타 과밀 6대 부작용  4) 수집형 RPG 비활성화 · 치어리더 보존  5) UI 규격(진단 화면 겹침 0 · Bold 0 · [TASK-GM-06] 1920×1080 · 치어리더 동선)
    /// </summary>
    public class GM01VerificationRunner
    {
        private readonly List<Object> created = new List<Object>();
        private GameObject dbObject;
        private List<PlayerTemplate> dbTemplates;
        private bool sceneOpened;

        [TearDown]
        public void TearDown()
        {
            GameSettings.UseVirtualNames = false;
            foreach (var obj in created) if (obj != null) Object.DestroyImmediate(obj);
            created.Clear();
            if (dbTemplates != null) foreach (var t in dbTemplates) if (t != null) Object.DestroyImmediate(t);
            dbTemplates = null;
            if (dbObject != null) Object.DestroyImmediate(dbObject);
            if (sceneOpened) EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = false;
        }

        private IReadOnlyList<PlayerTemplate> LoadDatabase()
        {
            dbObject = new GameObject("GM01_PlayerDatabase");
            var db = dbObject.AddComponent<PlayerDatabase>();
            dbTemplates = db.AllTemplates.ToList();
            return dbTemplates;
        }

        private PlayerTemplate Template(string id, string name, string position, int level, int defense = 60, int power = -1)
        {
            var t = ScriptableObject.CreateInstance<PlayerTemplate>();
            created.Add(t);
            t.TemplateId = id;
            t.RealPlayerId = id;
            t.PlayerName = name;
            t.RealName = name;
            t.SeasonYear = 2026;
            t.Team = Team.Samsung;
            t.CurrentTeam = Team.Samsung;
            t.IsActive = true;
            t.Grade = Grade.LIVE_NORMAL;
            t.IsPitcher = position == "SP" || position == "RP" || position == "CP";
            if (t.IsPitcher)
            {
                t.PitcherRole = position == "SP" ? PitcherRole.StartingPitcher : position == "CP" ? PitcherRole.Closer : PitcherRole.WinningReliever;
                t.PitcherStats = new PitcherStats(level, level, level, level, level);
            }
            else
            {
                t.BatterPosition = position switch
                {
                    "C" => BatterPosition.Catcher, "1B" => BatterPosition.FirstBase, "2B" => BatterPosition.SecondBase, "3B" => BatterPosition.ThirdBase,
                    "SS" => BatterPosition.ShortStop, "LF" => BatterPosition.LeftField, "CF" => BatterPosition.CenterField, "RF" => BatterPosition.RightField,
                    _ => BatterPosition.DesignatedHitter
                };
                t.BatterStats = new BatterStats(power < 0 ? level : power, level, level, 60, defense);
            }
            return t;
        }

        private Player Make(string position, int level, int ego, LockerRoomRole role, int salary = 10000, int defense = 60)
        {
            var p = new Player(Guid.NewGuid().ToString(), Template("T_" + Guid.NewGuid().ToString("N"), "선수", position, level, defense))
            {
                EgoLevel = ego,
                RoleArchetype = role,
                Salary = salary,
                Age = 28,
            };
            return p;
        }

        /// <summary>28인: Ego 5 알파독 4명(1B · LF · RF · DH) + 센터라인 수비 80 + 살림꾼 · 투수 13명(Ego 1). 연봉은 균등.</summary>
        private List<Player> AlphaDogRoster()
        {
            var roster = new List<Player>();
            foreach (var pos in new[] { "1B", "LF", "RF", "DH" }) roster.Add(Make(pos, 82, 5, LockerRoomRole.AlphaDog));
            foreach (var pos in new[] { "C", "SS", "CF" }) roster.Add(Make(pos, 70, 2, LockerRoomRole.UnsungHero, defense: 80));
            foreach (var pos in new[] { "2B", "3B", "C", "SS", "CF", "1B", "LF", "RF" }) roster.Add(Make(pos, 65, 1, LockerRoomRole.Prospect));
            for (int i = 0; i < 13; i++) roster.Add(Make(i < 5 ? "SP" : "RP", 68, 1, LockerRoomRole.Prospect));
            return roster;
        }

        private static Cheerleader Cheer(string name, CheerleaderGrade grade) =>
            new Cheerleader(Guid.NewGuid().ToString(), name, grade, 1, 1f, team: Team.Samsung);

        // ================================================================== 1) 계보 · 실명/가상명

        [Test]
        public void T1_TeamLineage_And_RealVirtualNameSwitch()
        {
            var expected = new Dictionary<string, string>
            {
                { "삼성 라이온즈", "SAM" }, { "MBC 청룡", "LG" }, { "LG 트윈스", "LG" }, { "빙그레 이글스", "HAN" }, { "한화 이글스", "HAN" },
                { "NC 다이노스", "NC" }, { "KT wiz", "KT" }, { "해태 타이거즈", "KIA" }, { "KIA 타이거즈", "KIA" }, { "롯데 자이언츠", "LOT" },
                { "OB 베어스", "DOO" }, { "두산 베어스", "DOO" }, { "넥센 히어로즈", "KIW" }, { "삼미 슈퍼스타즈", "KIW" }, { "현대 유니콘스", "KIW" },
                { "키움 히어로즈", "KIW" }, { "SK 와이번스", "SSG" }, { "쌍방울 레이더스", "SSG" }, { "SSG 랜더스", "SSG" }, { "TEM_010", "KIW" }, { "Samsung", "SAM" },
            };
            foreach (var pair in expected) Assert.AreEqual(pair.Value, NameAliasTable.ResolveCanonicalTeamCode(pair.Key), pair.Key);
            Assert.IsNull(NameAliasTable.ResolveCanonicalTeamCode("없는 구단"));
            Assert.AreEqual(10, NameAliasTable.CanonicalTeamCodes.Length);
            Assert.AreEqual(10, NameAliasTable.CanonicalTeamCodes.Select(NameAliasTable.ToTeam).Where(t => t != Team.None).Distinct().Count(), "10개 코드 ↔ 10개 Team");
            foreach (var code in NameAliasTable.CanonicalTeamCodes) Assert.AreEqual(code, NameAliasTable.ToCode(NameAliasTable.ToTeam(code)));
            Assert.AreEqual("키움 히어로즈", NameAliasTable.DisplayTeamName("KIW"));

            // 실명 ↔ 가상명: 기록(ID · 능력치)은 그대로, 이름만 즉시 바뀐다.
            var koo = Template("PLY_KOO", "구자욱", "RF", 85);
            var lee = Template("PLY_LEE", "이승엽", "1B", 92);
            var stats = koo.BatterStats;
            NameAliasTable.ApplyDisplayNames(new[] { koo, lee }, true);
            Assert.IsTrue(GameSettings.UseVirtualNames);
            Assert.AreNotEqual("구자욱", koo.PlayerName);
            StringAssert.StartsWith("구", koo.PlayerName, "성은 유지");
            Assert.AreEqual(3, koo.PlayerName.Length);
            StringAssert.StartsWith("이", lee.PlayerName);
            Assert.AreEqual("구자욱", koo.RealName, "실명 원본 보존");
            Assert.AreEqual("PLY_KOO", koo.TemplateId);
            Assert.AreEqual(stats.Power, koo.BatterStats.Power);
            Assert.AreEqual(stats.Defense, koo.BatterStats.Defense);
            string first = koo.PlayerName;
            NameAliasTable.ApplyDisplayNames(new[] { koo }, true);
            Assert.AreEqual(first, koo.PlayerName, "같은 playerId → 항상 같은 가상명");
            NameAliasTable.ApplyDisplayNames(new[] { koo, lee }, false);
            Assert.AreEqual("구자욱", koo.PlayerName);
            Assert.AreEqual("이승엽", lee.PlayerName);
            Assert.AreNotEqual("박지영", NameAliasTable.GetDisplayCheerleaderName("박지영", true));
            Assert.AreEqual("박지영", NameAliasTable.GetDisplayCheerleaderName("박지영", false));

            // 실제 DB 전체: 가상명 전환 시 실명 그대로 남는 한글 이름 0명, 되돌리면 전부 실명.
            var templates = LoadDatabase();
            Assert.Greater(templates.Count, 500);
            NameAliasTable.ApplyDisplayNames(templates, true);
            Assert.AreEqual(0, templates.Count(t => t.PlayerName == t.RealName), "가상명 전환 누락");
            NameAliasTable.ApplyDisplayNames(templates, false);
            Assert.IsTrue(templates.All(t => t.PlayerName == t.RealName), "실명 복귀");
        }

        // ================================================================== 2) 3대 시작 모드

        [Test]
        public void T2_ThreeStartModes_TenTeams28_CheerPool_Start2026_RollTo2027()
        {
            var templates = LoadDatabase();
            var cheer = GMRosterLoader.AllCheerleaderTemplates();
            Assert.IsNotEmpty(cheer, "치어리더 카탈로그");

            foreach (GMStartMode mode in Enum.GetValues(typeof(GMStartMode)))
            {
                var state = GMRosterLoader.LoadModeRoster(mode, "삼성 라이온즈", false, templates, cheer);
                Assert.IsNotNull(state, mode.ToString());
                Assert.AreEqual(2026, state.SeasonYear, $"{mode} 시작 연도");
                Assert.AreEqual(GMSeasonPhase.StoveLeague, state.Phase);
                Assert.AreEqual("SAM", state.SelectedTeamCode);
                Assert.IsTrue(state.UserTeam.IsUserTeam);
                Assert.AreEqual(10, state.Teams.Count, $"{mode} 10개 구단");
                foreach (var team in state.Teams.Values)
                {
                    string label = $"{mode}/{team.TeamCode}";
                    Assert.AreEqual(28, team.Roster.Count, label + " 28인");
                    Assert.AreEqual(GMRosterLoader.BatterCount, team.Batters.Count(), label + " 타자 15");
                    Assert.AreEqual(GMRosterLoader.PitcherCount, team.Pitchers.Count(), label + " 투수 13");
                    Assert.IsTrue(team.Roster.All(p => p.Age >= Player.MinAge && p.Age <= Player.MaxAge), label + " 나이");
                    Assert.IsTrue(team.Roster.All(p => p.Salary >= Player.MinSalary && p.Salary <= Player.MaxSalary), label + " 연봉");
                    Assert.IsTrue(team.Roster.All(p => p.EgoLevel >= 1 && p.EgoLevel <= 5), label + " 자존심");
                    Assert.IsTrue(team.Roster.All(p => p.ContractYears >= 0 && p.ContractYears <= 5), label + " 계약");
                    Assert.That(team.CheerleaderPool.Count, Is.InRange(GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN, GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX), label + " 치어리더 풀 4~15");
                    Assert.IsTrue(team.CheerleaderPool.All(c => c.Team == team.Team), label + " 소속 구단 치어리더");
                    Assert.AreEqual(team.CheerleaderPool.Count, team.CheerleaderPool.Select(c => c.Name).Distinct().Count(), label + " 동일 인물 중복 없음");
                    Assert.IsTrue(GMCheerleaderRules.IsValidEntryCount(team.CheerEntry.Count()), label + " 엔트리 4~6");
                    Assert.Greater(team.Budget, 0);
                }
                var ids = state.Teams.Values.SelectMany(t => t.Roster).Select(p => p.Template.RealPlayerId).ToList();
                Assert.AreEqual(ids.Count, ids.Distinct().Count(), $"{mode} 같은 선수가 두 구단에 없음");

                if (mode == GMStartMode.RealCurrent2026)
                    Assert.Greater(state.UserTeam.Roster.Count(p => p.Template.IsActive), 20, "현역 위주 로스터");
                if (mode == GMStartMode.AllTimeDream)
                {
                    Assert.IsNotEmpty(state.FreeAgents, "드림 FA 시장");
                    Assert.IsTrue(state.FreeAgents.All(p => p.ContractYears == 0));
                    Assert.Greater(state.Teams.Values.SelectMany(t => t.Roster).Count(p => p.EgoLevel == 5), 30, "드림 모드 슈퍼스타 다수");
                }
                if (mode == GMStartMode.StoryCampaign)
                {
                    var user = state.UserTeam;
                    Assert.AreEqual(4, user.ConsecutiveLastPlaceSeasons);
                    Assert.IsTrue(user.OwnerPostseasonPressure);
                    Assert.AreEqual((long)GMRosterLoader.DefaultPayrollCap * GMRosterLoader.BudgetToCapPercent / 100 * GMRosterLoader.StoryBudgetPercent / 100, user.Budget, "예산 20% 삭감");
                    var veteran = user.Roster.Single(p => p.Template.RealPlayerId == user.TradeRequestPlayerId);
                    Assert.AreEqual(35, veteran.Age);
                    Assert.AreEqual(5, veteran.EgoLevel);
                    Assert.IsFalse(veteran.IsPitcher);
                    Assert.IsTrue(state.Teams.Values.Where(t => !t.IsUserTeam).All(t => t.ConsecutiveLastPlaceSeasons == 0));
                }

                // 시즌 → 시상식 → 해 넘김(2027)
                var sample = state.UserTeam.Roster[0];
                int age = sample.Age, contract = sample.ContractYears;
                for (int i = 0; i < 3; i++) Assert.IsFalse(state.AdvancePhase());
                Assert.AreEqual(GMSeasonPhase.AwardsCeremony, state.Phase);
                Assert.IsTrue(state.AdvancePhase(), "시상식 종료 → 해 넘김");
                Assert.AreEqual(2027, state.SeasonYear);
                Assert.AreEqual(GMSeasonPhase.StoveLeague, state.Phase);
                Assert.AreEqual(Math.Min(Player.MaxAge, age + 1), sample.Age);
                Assert.AreEqual(Math.Max(0, contract - 1), sample.ContractYears);
            }

            // 가상명 모드로 로드해도 로스터 · 기록은 같고 이름만 바뀐다.
            var virtualState = GMRosterLoader.LoadModeRoster(GMStartMode.RealCurrent2026, "SAM", true, templates, cheer);
            Assert.IsTrue(virtualState.UserTeam.Roster.All(p => p.Template.PlayerName != p.Template.RealName || string.IsNullOrEmpty(p.Template.RealName)));
            NameAliasTable.ApplyDisplayNames(templates, false);
        }

        // ================================================================== 3) 슈퍼스타 과밀 6대 부작용

        [Test]
        public void T3_AllStarOverload_SixPenalties_AndLeaderCaptainCheerFix()
        {
            // ① 알파독 4명 + 리더 0명
            var roster = AlphaDogRoster();
            var split = TeamChemistryEngine.EvaluateRoster(roster, GMRosterLoader.DefaultPayrollCap);
            Assert.IsTrue(split.Has(AllStarOverloadPenalty.AlphaDogFactionSplit), "파벌 분열 발동");
            Assert.AreEqual(-0.15f, split.ClutchHitModifier, 1e-4f, "클러치 타율 -15%");
            Assert.AreEqual(2.0f, split.ErrorRateMultiplier, 1e-4f, "실책 2배");
            Assert.AreEqual(TeamMoraleState.Slump, split.MoraleState);
            Assert.AreEqual(AllStarOverloadPenalty.AlphaDogFactionSplit, split.ActivePenalties, "다른 부작용은 없음(통제된 로스터)");
            Assert.IsNotEmpty(split.DiagnosticMessages);

            // 해결: 주장 임명 + 더그아웃 리더 배치 + 치어리더 리더십 버프
            roster[4].IsCaptain = true;
            roster[5].RoleArchetype = LockerRoomRole.DugoutLeader;
            var entry = new[] { Cheer("가", CheerleaderGrade.ICON), Cheer("나", CheerleaderGrade.ICON), Cheer("다", CheerleaderGrade.LIVE_NORMAL), Cheer("라", CheerleaderGrade.LIVE_NORMAL), Cheer("마", CheerleaderGrade.LEGEND) };
            int buff = GMCheerleaderRules.LeadershipBuff(entry);
            Assert.AreEqual(9, buff, "ICON 2+2 · LIVE 1+1 · LEGEND 3");
            Assert.AreEqual(0, GMCheerleaderRules.LeadershipBuff(entry.Take(3)), "엔트리 4명 미만이면 버프 없음");
            var fixedReport = TeamChemistryEngine.EvaluateRoster(roster, GMRosterLoader.DefaultPayrollCap, buff);
            Assert.IsFalse(fixedReport.Has(AllStarOverloadPenalty.AlphaDogFactionSplit), "파벌 분열 해제");
            Assert.Greater(fixedReport.TeamworkScore, split.TeamworkScore, "팀워크 회복");
            Assert.AreEqual(1.0f, fixedReport.ErrorRateMultiplier, 1e-4f);
            Assert.AreEqual(0f, fixedReport.ClutchHitModifier, 0.081f);
            Assert.Greater(fixedReport.EffectivePowerMultiplier, split.EffectivePowerMultiplier);
            Assert.That(fixedReport.EffectivePowerMultiplier, Is.InRange(0.80f, 1.15f));

            // ② 타순 · 보직 자존심 충돌 - Ego 4+ 타자 6명(중심 4자리 초과 2명), 양보 인센티브 1명은 면제
            var conflict = new List<Player>();
            for (int i = 0; i < 6; i++) conflict.Add(Make(new[] { "1B", "LF", "RF", "DH", "2B", "3B" }[i], 80, 4, LockerRoomRole.UnsungHero));
            foreach (var pos in new[] { "C", "SS", "CF" }) conflict.Add(Make(pos, 70, 1, LockerRoomRole.UnsungHero, defense: 80));
            conflict[5].HasRoleConcessionBonus = true;
            var r2 = TeamChemistryEngine.EvaluateRoster(conflict, 0);
            Assert.IsTrue(r2.Has(AllStarOverloadPenalty.LineupRoleConflict));
            // [TASK-GM-02] 평가는 순수 계산 - 만족도 변동은 경기 틱(ApplyMatchChemistryTick)에서만 일어난다.
            Assert.AreEqual(Player.DefaultPersonalMorale, conflict[4].PersonalMorale, "평가만으로는 만족도 불변");
            TeamChemistryEngine.ApplyMatchChemistryTick(conflict);
            Assert.AreEqual(Player.DefaultPersonalMorale - TeamChemistryEngine.TickMoralePenalty, conflict[4].PersonalMorale, "밀려난 스타 경기 틱 만족도 하락");
            Assert.AreEqual(Player.DefaultPersonalMorale, conflict[5].PersonalMorale, "양보 인센티브 수령자는 불만 없음");

            // ③ Hero Ball - 야망가 4명 · 살림꾼 1명, 규율 우선이면 해제
            var hero = new List<Player>();
            for (int i = 0; i < 4; i++) hero.Add(Make(new[] { "1B", "LF", "RF", "DH" }[i], 75, 3, LockerRoomRole.Ambitious));
            foreach (var pos in new[] { "C", "SS", "CF" }) hero.Add(Make(pos, 70, 1, pos == "C" ? LockerRoomRole.UnsungHero : LockerRoomRole.Prospect, defense: 80));
            Assert.IsTrue(TeamChemistryEngine.EvaluateRoster(hero, 0).Has(AllStarOverloadPenalty.HeroBallDoublePlay));
            Assert.AreEqual(1.35f, TeamChemistryEngine.EvaluateRoster(hero, 0).DoublePlayRiskMultiplier, 1e-4f);
            Assert.IsFalse(TeamChemistryEngine.EvaluateRoster(hero, 0, 0, isTacticalDisciplineMode: true).Has(AllStarOverloadPenalty.HeroBallDoublePlay));

            // ④ 센터라인 수비 붕괴(포수 · 유격수 · 중견수 수비 50)
            var slugger = new List<Player>();
            foreach (var pos in new[] { "C", "SS", "CF", "1B" }) slugger.Add(Make(pos, 75, 1, LockerRoomRole.UnsungHero, defense: 50));
            var r4 = TeamChemistryEngine.EvaluateRoster(slugger, 0);
            Assert.IsTrue(r4.Has(AllStarOverloadPenalty.DefenseImbalance));
            Assert.AreEqual(0.85f, r4.PitcherEraPenalty, 1e-4f);

            // ⑤ 페이롤 폭발(캡 초과)
            var payroll = AlphaDogRoster();
            roster.ForEach(p => p.IsCaptain = false);
            payroll[5].RoleArchetype = LockerRoomRole.DugoutLeader;
            payroll.Take(12).ToList().ForEach(p => p.Salary = 150000);
            Assert.IsTrue(TeamChemistryEngine.EvaluateRoster(payroll, GMRosterLoader.DefaultPayrollCap).Has(AllStarOverloadPenalty.PayrollDepthCollapse));

            // ⑥ 스타 군단의 방심 - 평균 OVR 85+ · 팀워크 50 미만
            var stars = new List<Player>();
            for (int i = 0; i < 6; i++) stars.Add(Make(new[] { "1B", "LF", "RF", "DH", "2B", "3B" }[i], 90, 5, LockerRoomRole.AlphaDog));
            foreach (var pos in new[] { "C", "SS", "CF" }) stars.Add(Make(pos, 90, 5, LockerRoomRole.Ambitious, defense: 50));
            var r6 = TeamChemistryEngine.EvaluateRoster(stars, 0);
            Assert.Less(r6.TeamworkScore, 50);
            Assert.IsTrue(r6.Has(AllStarOverloadPenalty.UnderdogUpsetVulnerability));
            Assert.AreEqual(0.20f, r6.UpsetVulnerabilityChance, 1e-4f);
            Assert.AreEqual(0.80f, r6.EffectivePowerMultiplier, 1e-4f, "실효 전력 하한");

            // 자동 산출 규칙(지시서 2.3): 자존심 · 연봉 범위
            Assert.AreEqual(5, Player.ComputeEgoLevel(90, false));
            Assert.AreEqual(5, Player.ComputeEgoLevel(60, true), "주요 수상 경력자");
            Assert.AreEqual(4, Player.ComputeEgoLevel(85, false));
            Assert.AreEqual(3, Player.ComputeEgoLevel(80, false));
            Assert.AreEqual(2, Player.ComputeEgoLevel(72, false));
            Assert.AreEqual(1, Player.ComputeEgoLevel(60, false));
            Assert.AreEqual(Player.MinSalary, Player.ComputeSalary(45, 1, 0));
            Assert.AreEqual(Player.MaxSalary, Player.ComputeSalary(100, 5, 9));
        }

        // ================================================================== 4) 수집형 RPG 비활성화 · 치어리더 보존

        [Test]
        public void T4_CollectibleRpgDisabled_CheerleaderSystemPreserved()
        {
            Assert.IsFalse(GMFeatureFlags.IsCardGrowthEnabled, "강화 · 각성 · 초월 OFF");
            Assert.IsFalse(GMFeatureFlags.IsSetDeckEnabled, "세트덱 OFF");
            Assert.IsFalse(GMFeatureFlags.IsPlayerGachaEnabled, "선수 가챠 OFF");
            Assert.IsTrue(GMFeatureFlags.IsCheerleaderCoreEnabled, "치어리더 ON");
            Assert.AreEqual(15, GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX);
            Assert.AreEqual(4, GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN);
            Assert.AreEqual(6, GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MAX);
            Assert.AreEqual(2026, GMFeatureFlags.DEFAULT_START_YEAR);

            var t = Template("PLY_GROWN", "성장카드", "RF", 70);
            t.Grade = Grade.SIGNATURE;
            var grown = new Player(Guid.NewGuid().ToString(), t) { ReinforceLevel = 10, AwakenLevel = 9, LimitBreakLevel = 5, TrainingLevel = 5 };
            Assert.Greater(grown.GetStatGrowth(), 0, "구 세이브의 성장 수치는 데이터로 남아 있다");
            Assert.AreEqual(grown.BaseOverall, grown.GetEffectiveOverall(), "단장 모드 OVR = 순수 시즌 성적(성장 0)");
            Assert.AreEqual(70, grown.GetEffectiveOverall());
            Assert.Greater(grown.CalculateOVR(true, 1.3f), grown.GetEffectiveOverall(), "세트덱 · 성장 보너스는 GetEffectiveOverall에 포함되지 않는다");

            Assert.IsFalse(GMCheerleaderRules.IsValidEntryCount(3));
            for (int n = 4; n <= 6; n++) Assert.IsTrue(GMCheerleaderRules.IsValidEntryCount(n));
            Assert.IsFalse(GMCheerleaderRules.IsValidEntryCount(7));
            Assert.AreEqual(6, GMCheerleaderRules.ClampEntrySize(10));
            Assert.AreEqual(4, GMCheerleaderRules.ClampEntrySize(1));
            Assert.AreEqual(GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MAX, CheerSquad.SlotCount, "엔트리 상한 = 6인 역할 슬롯");

            // 씬: 치어리더 영입 · 관리 · 성장은 그대로 남아 있다.
            OpenScene();
            var hub = Object.FindAnyObjectByType<ScoutHubUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(hub);
            var so = new SerializedObject(hub);
            Assert.IsNotNull(so.FindProperty("cheerleaderTabButton").objectReferenceValue, "응원단 영입 탭");
            Assert.IsNotNull(so.FindProperty("cheerleaderSection").objectReferenceValue, "응원단 영입 섹션");
            Assert.IsNotNull(Object.FindAnyObjectByType<CheerleaderShopUIController>(FindObjectsInactive.Include), "치어리더 영입");
            Assert.IsNotNull(Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include), "치어리더 관리");
            Assert.IsNotNull(Object.FindAnyObjectByType<CheerGrowthView>(FindObjectsInactive.Include), "응원단 성장");
        }

        // ================================================================== 5) UI 규격

        [Test]
        public void T5_UiIntegrity_Landscape1920x1080_NoOverlap_NoBold_CheerRoute()
        {
            OpenScene();

            var scaler = Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include).FirstOrDefault(s => s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize);
            Assert.IsNotNull(scaler);
            Assert.AreEqual(new Vector2(1920, 1080), scaler.referenceResolution, "[TASK-GM-06] 1920×1080 Landscape");

            // 진단 화면 - 조립 · 바인딩 · 겹침 0 · Normal · 15pt 이상
            var hubCtrl = Object.FindAnyObjectByType<PlayerManagementUIController>(FindObjectsInactive.Include);
            Assert.IsNotNull(hubCtrl);
            var view = hubCtrl.GetComponent<GMDiagnosticView>();
            Assert.IsNotNull(view, "진단 화면 부착");
            Assert.AreSame(view, new SerializedObject(hubCtrl).FindProperty("diagnosticView").objectReferenceValue);
            Assert.IsNotNull(hubCtrl.transform.Find(GMDiagnosticView.RootName), "씬에 조립된 진단 루트");
            view.Build();
            view.Render(AlphaDogRoster(), new[] { Cheer("가", CheerleaderGrade.ICON), Cheer("나", CheerleaderGrade.LIVE_NORMAL), Cheer("다", CheerleaderGrade.LIVE_NORMAL), Cheer("라", CheerleaderGrade.LIVE_NORMAL) }, 12);
            Assert.AreEqual(view.Root.parent.childCount - 1, view.Root.GetSiblingIndex(), "성장 센터 위(최상단)");
            StringAssert.Contains("팀워크", view.Root.Find("TeamworkScore").GetComponent<Text>().text);
            StringAssert.Contains("파벌 분열", view.Root.Find("Messages").GetComponent<Text>().text);
            StringAssert.Contains("엔트리 4/6명", view.Root.Find("CheerBody").GetComponent<Text>().text);
            StringAssert.Contains("억", view.Root.Find("PayrollBody").GetComponent<Text>().text);

            var parts = new[] { "BackButton", "Title", "CloseButton", "PayrollTitle", "PayrollBody", "TeamworkTitle", "TeamworkScore", "TeamworkMeta",
                                "Messages", "CheerTitle", "CheerBody", "CheerManageButton", "CloseBottomButton", "Note" };
            var overlaps = new List<string>();
            for (int i = 0; i < parts.Length; i++)
                for (int j = i + 1; j < parts.Length; j++)
                    if (Task191Report.Overlap(view.Root.Find(parts[i]), view.Root.Find(parts[j]))) overlaps.Add($"{parts[i]} ↔ {parts[j]}");
            Assert.IsEmpty(overlaps, "진단 화면 텍스트 겹침 0건");
            foreach (var text in view.Root.GetComponentsInChildren<Text>(true))
            {
                Assert.AreEqual(FontStyle.Normal, text.fontStyle, text.name);
                Assert.GreaterOrEqual(TextTidy.EffectiveSize(text), 15, text.name + " 15pt 이상");
            }

            // 씬 전체 Bold 0건
            var bold = SetupTask191.SceneTexts().Where(x => x.fontStyle == FontStyle.Bold || x.fontStyle == FontStyle.BoldAndItalic).Select(x => x.name).ToList();
            Assert.IsEmpty(bold, "FontStyle.Bold 0건");

            // 로비 타일: 세트덱 → 진단, 선수단 강화 → 치어리더 관리
            var home = Object.FindAnyObjectByType<LobbyHome181>(FindObjectsInactive.Include);
            Assert.IsNotNull(home);
            Assert.AreEqual(SetupTaskGM01.DiagnosticTileTitle, home.transform.Find("MenuTile1_Title").GetComponent<Text>().text);
            Assert.AreEqual(SetupTaskGM01.CheerTileTitle, home.transform.Find("MenuTile2_Title").GetComponent<Text>().text);
            var tile1 = home.transform.Find("MenuTile1").GetComponent<LobbyButtonRelay>();
            var tile2 = home.transform.Find("MenuTile2").GetComponent<LobbyButtonRelay>();
            Assert.IsFalse(tile1.OpensSetDeckBuffs, "세트덱 팝업 직행 해제");
            Assert.AreEqual((int)ScreenType.Inventory, new SerializedObject(tile1).FindProperty("screen").intValue, "진단(선수 관리 허브) 경유");
            Assert.AreEqual((int)ScreenType.CheerleaderInventory, new SerializedObject(tile2).FindProperty("screen").intValue, "치어리더 관리 진입");
            Assert.IsFalse(home.GetComponentsInChildren<Text>(true).Any(x => x.text.Contains("세트덱 & 버프")), "세트덱 타일 문구 제거");

            // 치어리더 관리 진입(로비 하단 탭 · 타일) → 화면 등록 → 닫기([X]) 바인딩
            var route = new SetupTaskGM01.Result();
            SetupTaskGM01.CheckCheerleaderRoute(route);
            Assert.IsTrue(route.CheerNavBound, "로비 하단 [응원단] 탭 바인딩");
            Assert.IsTrue(route.CheerScreenRegistered, "UIManager에 치어리더 관리 화면 등록");
            Assert.IsTrue(route.CheerCloseBound, "닫기([X]) 버튼 바인딩");
            var inventory = Object.FindAnyObjectByType<CheerleaderInventoryUIController>(FindObjectsInactive.Include);
            var close = (Button)new SerializedObject(inventory).FindProperty("closeButton").objectReferenceValue;
            Assert.IsTrue(close.transform.IsChildOf(inventory.transform), "닫기 버튼이 치어리더 관리 화면 안에 있다");
            Assert.IsTrue(close.interactable && close.targetGraphic != null && close.targetGraphic.raycastTarget, "닫기 버튼 클릭 가능");
        }

        private void OpenScene()
        {
            EditorSceneManager.OpenScene(SampleSceneGuard.ScenePath, OpenSceneMode.Single);
            sceneOpened = true;
        }
    }
}
