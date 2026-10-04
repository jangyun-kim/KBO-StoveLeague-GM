using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KBOManager.Managers;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.EditorTools
{
    /// <summary>
    /// [TASK-KBO-190] 시즌 완주 루프 실측용 하네스(Task190Report · Task190Tests 공용). 매니저 6종(GameManager · LeagueManager · SeasonStatManager ·
    /// PostSeasonManager · SeasonRewardManager · SeasonRollover)을 한 오브젝트에 붙여 싱글톤/참조를 배선하고, 절차 생성 유저 구단(지정 OVR)으로
    /// 헤드리스 144경기 → 포스트시즌 → 결산 · 시상식 → 다음 시즌 전환을 실제 게임 코드 그대로 돌린다.
    /// </summary>
    public sealed class SeasonCycleHarness : IDisposable
    {
        public GameObject Go;
        public GameManager Gm;
        public LeagueManager League;
        public SeasonStatManager Stats;
        public PostSeasonManager Post;
        public SeasonRewardManager Reward;
        public SeasonRollover Rollover;

        private static readonly Type[] SingletonTypes =
        {
            typeof(GameManager), typeof(LeagueManager), typeof(SeasonStatManager), typeof(PostSeasonManager),
            typeof(SeasonRewardManager), typeof(SeasonRollover), typeof(SaveManager), typeof(LeagueCalendar),
        };

        public static SeasonCycleHarness Create(int userTeamOvr, LeagueTier tier, Team userTeam = Team.Samsung, string tag = "S190")
        {
            foreach (var t in SingletonTypes) SetInstance(t, null);
            var h = new SeasonCycleHarness { Go = new GameObject("Task190SeasonHarness") };
            h.Gm = h.Go.AddComponent<GameManager>();
            SetInstance(typeof(GameManager), h.Gm);
            h.Gm.FavoriteTeam = userTeam;
            foreach (var p in Task183Report.ProceduralTeam(userTeam, userTeamOvr, tag))
            {
                h.Gm.AddPlayerToInventory(p);
                h.Gm.AddPlayerToRoster(p);
            }
            h.League = h.Go.AddComponent<LeagueManager>();
            SetInstance(typeof(LeagueManager), h.League);
            h.League.InitializeLeague(userTeam, tier);
            h.Stats = h.Go.AddComponent<SeasonStatManager>();
            SetInstance(typeof(SeasonStatManager), h.Stats);
            h.Post = h.Go.AddComponent<PostSeasonManager>();
            SetInstance(typeof(PostSeasonManager), h.Post);
            SetField(h.Post, "leagueManager", h.League);
            h.Reward = h.Go.AddComponent<SeasonRewardManager>();
            SetInstance(typeof(SeasonRewardManager), h.Reward);
            SetField(h.Reward, "leagueManager", h.League);
            SetField(h.Reward, "postSeasonManager", h.Post);
            SetField(h.Reward, "seasonStatManager", h.Stats);
            h.Rollover = h.Go.AddComponent<SeasonRollover>();
            SetInstance(typeof(SeasonRollover), h.Rollover);
            SetField(h.Rollover, "leagueManager", h.League);
            SetField(h.Rollover, "seasonStatManager", h.Stats);
            SetField(h.Rollover, "postSeasonManager", h.Post);
            SetField(h.Rollover, "seasonRewardManager", h.Reward);
            return h;
        }

        /// <summary>AI 9개 구단 OVR(오름차순).</summary>
        public int[] AiTeamOvrs() => League.GetStandings().Where(t => !t.IsUserTeam).Select(t => League.GetTeamOvr(t.Team)).OrderBy(x => x).ToArray();

        public static void SetInstance(Type type, object value)
        {
            var setter = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetSetMethod(true);
            setter?.Invoke(null, new[] { value });
        }

        public static void SetField(object target, string name, object value)
        {
            var field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }

        public void Dispose()
        {
            foreach (var t in SingletonTypes) SetInstance(t, null);
            if (Go != null) UnityEngine.Object.DestroyImmediate(Go);
            Task183Report.CleanupTemp();
        }
    }
}
