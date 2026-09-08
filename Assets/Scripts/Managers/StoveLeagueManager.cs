using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Models;
using UnityEngine;
using Random = UnityEngine.Random;

namespace KBOManager.Managers
{
    /// <summary>
    /// 스토브리그(POST_PREP 종료 ~ 다음 시즌 개막 사이) 동안 AI 9개 구단의 로스터를 자동으로
    /// 성장시키는 순수 C# 유틸리티. MonoBehaviour를 상속하지 않는다 - 시즌 전환 시 LeagueManager가
    /// 이 클래스의 정적 메서드를 직접 호출하는 것만으로 충분하고, 씬에 배치될 상태를 갖지 않는다.
    ///
    /// GDD 원문 시그니처는 ProcessStoveLeague(List&lt;TeamGameState&gt; aiTeams)로 명시돼 있으나,
    /// TeamGameState는 MatchEngine 내부에 private으로 중첩된 "한 경기 동안의" 타순/투수 로테이션
    /// 상태 클래스라 이 용도(시즌을 넘어 유지되는 로스터 성장)로는 접근할 수도, 의미상 맞지도 않는다.
    /// 실제로 시즌 간 유지되는 로스터를 들고 있는 타입은 LeagueManager.TeamInfo이므로, 이 메서드는
    /// List&lt;TeamInfo&gt;를 받도록 시그니처를 대체했다 - LeagueManager.GetStandings()에서 유저 팀을
    /// 제외한 나머지 9개를 뽑아 그대로 넘기면 된다.
    /// </summary>
    public static class StoveLeagueManager
    {
        // GDD: "강화 수치 +1~+3"
        private const int MinReinforceBoost = 1;
        private const int MaxReinforceBoost = 3; // inclusive

        // 로스터 중 OVR 상위 몇 명까지를 "핵심 선수"(신규 스킬 부여 후보)로 볼지. 나머지는 강화만 적용된다.
        private const int CorePlayerCount = 8;

        // 선수 1명당 이번 스토브리그에 실제로 뭔가(강화 or 스킬)가 적용될 확률. 100%로 두면 로스터
        // 전원이 매 시즌 성장해 AI가 지나치게 빨리 강해지므로, 일부만 성장하도록 완화한 값이다.
        // TODO: 밸런스 확정 전까지의 임시값 - 시즌 수십 회 시뮬레이션 후 유저 대비 AI 평균 OVR
        // 추이를 보고 조정할 것.
        private const float PerPlayerGrowthChance = 0.6f;

        // 핵심 선수가 강화 대신 스킬 부여를 받을 확률(핵심 선수가 아니면 항상 강화만 적용).
        private const float CorePlayerSkillGrantChance = 0.5f;

        /// <summary>이번 ProcessStoveLeague() 호출 1회의 처리 결과 요약. 로그/리포트용.</summary>
        public struct StoveLeagueReport
        {
            public int TeamsProcessed;
            public int PlayersReinforced;
            public int SkillsGranted;
            public int ReinforceSkipsAtCap;      // MaxReinforceLevel(10강)에 막혀 증가분이 깎인 횟수
            public int SkillGrantSkipsDuplicate; // 이미 보유한 스킬이 다시 뽑혀 스킵된 횟수
        }

        /// <summary>
        /// POST_PREP(가을야구 종료) -&gt; STOVE_LEAGUE -&gt; 다음 시즌 개막 전환 시 LeagueManager가
        /// 호출한다. 각 AI 팀 로스터의 선수 일부에게 무작위로 강화 수치(+1~+3) 또는 신규 스킬 부여
        /// 중 하나를 적용해, 시즌이 거듭될수록 AI 평균 OVR이 유저를 따라 상승하도록 만든다.
        /// skillDB가 null이면 스킬을 뽑을 방법이 없으므로 모든 성장이 강화 쪽으로 대체된다.
        /// </summary>
        public static StoveLeagueReport ProcessStoveLeague(List<TeamInfo> aiTeams, SkillDB skillDB)
        {
            var report = new StoveLeagueReport();
            if (aiTeams == null) return report;

            foreach (var team in aiTeams)
            {
                if (team?.Roster == null || team.Roster.Count == 0) continue;

                report.TeamsProcessed++;
                ProcessTeam(team, skillDB, ref report);
            }

            return report;
        }

        private static void ProcessTeam(TeamInfo team, SkillDB skillDB, ref StoveLeagueReport report)
        {
            // OVR 내림차순 상위 CorePlayerCount명만 스킬 부여 후보로 간주한다(주전급 선수 위주 성장).
            var coreInstanceIds = new HashSet<string>(
                team.Roster
                    .OrderByDescending(p => p.CalculateOVR(false))
                    .Take(CorePlayerCount)
                    .Select(p => p.InstanceId));

            foreach (var player in team.Roster)
            {
                if (player == null) continue;
                if (Random.value > PerPlayerGrowthChance) continue; // 이번 스토브리그엔 성장 없음

                bool isCore = coreInstanceIds.Contains(player.InstanceId);
                bool grantSkill = isCore && skillDB != null && Random.value < CorePlayerSkillGrantChance;

                if (grantSkill)
                {
                    ApplySkillGrant(player, skillDB, ref report);
                }
                else
                {
                    ApplyReinforceBoost(player, ref report);
                }
            }
        }

        /// <summary>
        /// +1~+3 강화를 적용하되, Player.MaxReinforceLevel(10강)을 절대 넘지 않도록 Mathf.Min으로
        /// 클램프한다. 이미 10강이면 증가분이 0이 되어 사실상 아무 일도 일어나지 않는다 - 이 캡 덕분에
        /// 수십 시즌이 반복돼도 강화 수치가 오버플로우될 수 없다(리포트 참고).
        /// </summary>
        private static void ApplyReinforceBoost(Player player, ref StoveLeagueReport report)
        {
            int boost = Random.Range(MinReinforceBoost, MaxReinforceBoost + 1);
            int before = player.ReinforceLevel;
            player.ReinforceLevel = Mathf.Min(player.ReinforceLevel + boost, Player.MaxReinforceLevel);

            if (player.ReinforceLevel > before) report.PlayersReinforced++;
            if (player.ReinforceLevel - before < boost) report.ReinforceSkipsAtCap++;
        }

        /// <summary>
        /// 신규 스킬 1개를 부여한다. 이미 보유한 스킬(AcquiredSkillIds에 이름이 이미 있는 경우)이
        /// 다시 뽑히면 중복 추가하지 않고 스킵한다 - 그렇지 않으면 같은 스킬이 리스트에 여러 번 쌓여
        /// AcquiredSkillIds가 시즌을 거듭할수록 무한히 길어지는 문제가 생긴다. 뽑을 스킬 자체가 없으면
        /// (해당 카테고리 풀이 비어 있으면) 강화 부여로 대체해 "이번 스토브리그엔 아무 일도 없었다"는
        /// 상태를 피한다.
        /// </summary>
        private static void ApplySkillGrant(Player player, SkillDB skillDB, ref StoveLeagueReport report)
        {
            var skill = skillDB.GetRandomSkill(player.Template);
            if (skill == null)
            {
                ApplyReinforceBoost(player, ref report);
                return;
            }

            if (player.AcquiredSkillIds.Contains(skill.SkillName))
            {
                report.SkillGrantSkipsDuplicate++;
                return;
            }

            player.AcquiredSkillIds.Add(skill.SkillName);
            report.SkillsGranted++;
        }
    }
}
