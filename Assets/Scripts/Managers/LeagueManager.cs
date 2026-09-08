using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Data;
using KBOManager.Engine;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>한 팀(유저 팀 또는 AI 팀)의 시즌 성적/로스터.</summary>
    public class TeamInfo
    {
        public Team Team;
        public bool IsUserTeam;
        public int Wins;
        public int Draws;
        public int Losses;
        public List<Player> Roster = new List<Player>();

        /// <summary>KBO 승률 규칙: 승 / (승+패). 무승부는 분모에서 제외한다.</summary>
        public float WinRate => (Wins + Losses) == 0 ? 0f : (float)Wins / (Wins + Losses);
    }

    /// <summary>유저 팀 시즌 캘린더의 경기 한 건.</summary>
    public class MatchFixture
    {
        public int GameNumber; // 1~144
        public Team HomeTeam;
        public Team AwayTeam;
        public LeaguePhase Phase; // GameNumber 1~72 = REGULAR_OPEN, 73~144 = REGULAR_LOCKED
        public bool IsPlayed;
        public MatchResult Result;
    }

    /// <summary>
    /// 유저 팀 1개 + KBO 나머지 9개 구단(AI)으로 리그를 구성하고, 유저 팀 기준 144경기(9개 상대 x 16경기)
    /// 스케줄을 생성/진행하며 각 팀의 승/무/패와 순위를 관리하는 싱글톤.
    ///
    /// 범위 한정: 마스터 GDD가 "경기(Match)"를 "유저의 로스터와 AI 팀 간의 대결"로 정의하고 있어(AI 팀 간의
    /// 대결은 게임 정의에 없음), 이 매니저는 유저 팀이 참여하는 144경기만 MatchEngine으로 실제 시뮬레이션한다.
    /// 따라서 AI 팀들의 순위표 성적은 유저와 치른 경기 결과만 반영하며, AI 팀 간의 가상 대결까지 포함한
    /// "완전한" 10팀 리그 시뮬레이션은 아니다.
    /// </summary>
    public class LeagueManager : MonoBehaviour
    {
        public static LeagueManager Instance { get; private set; }

        public const int OpponentCount = 9;
        public const int GamesPerOpponent = 16;                              // 9 x 16 = 144
        public const int TotalUserGames = OpponentCount * GamesPerOpponent;  // 144
        public const int RegularOpenGames = 72;                              // 1~72경기
        // 73~144경기 = RegularLockedGames (TotalUserGames - RegularOpenGames)

        // 상대 팀 1개당 시리즈 구성: 3연전 x 5 + 단판 1경기 = 16경기. (144를 9개 상대에게 균등 배분하기 위한 근사치.
        // 실제 KBO는 3연전 위주에 일부 다른 길이의 시리즈를 섞어 16경기를 맞춘다 - 여기서는 그 스타일을 단순화했다.)
        private static readonly int[] SeriesLengths = { 3, 3, 3, 3, 3, 1 };

        [Header("User Team")]
        [SerializeField] private Team userTeam = Team.Doosan;

        [Header("Match Simulation")]
        [Tooltip("MatchEngine에 전달할 SkillDB (선택 사항)")]
        [SerializeField] private SkillDB skillDB;

        private readonly Dictionary<Team, TeamInfo> standings = new Dictionary<Team, TeamInfo>();
        private readonly List<MatchFixture> schedule = new List<MatchFixture>();
        private int nextFixtureIndex;

        public Team UserTeam => userTeam;
        public IReadOnlyList<MatchFixture> Schedule => schedule;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// 유저 팀 + KBO 나머지 9개 구단으로 리그를 초기화하고 유저의 144경기 스케줄을 생성한다.
        /// 유저 팀의 로스터는 GameManager.Roster를 그대로 참조한다(스냅샷 아님 - 매 경기 시뮬레이션 시점의 현재 로스터를 사용).
        /// </summary>
        public void InitializeLeague(Team userTeamValue)
        {
            userTeam = userTeamValue;
            standings.Clear();
            schedule.Clear();
            nextFixtureIndex = 0;

            foreach (Team team in Enum.GetValues(typeof(Team)))
            {
                if (team == Team.None) continue;
                standings[team] = new TeamInfo { Team = team, IsUserTeam = team == userTeam };
            }

            BuildUserSchedule();
        }

        /// <summary>
        /// AI 팀(또는 유저 팀)의 28인 로스터를 등록한다. AI 팀 로스터 자동 생성기는 아직 없으므로,
        /// 호출부(예: 향후 AI 팀 빌더)가 PlayerDatabase 등에서 구성한 로스터를 여기로 넘겨줘야 한다.
        /// 유저 팀은 매 경기 시뮬레이션 시점에 GameManager.Roster를 직접 참조하므로 보통 호출할 필요가 없다.
        /// </summary>
        public void SetTeamRoster(Team team, List<Player> roster)
        {
            if (standings.TryGetValue(team, out var info))
            {
                info.Roster = roster ?? new List<Player>();
            }
        }

        public TeamInfo GetTeamInfo(Team team) => standings.TryGetValue(team, out var info) ? info : null;

        /// <summary>승률(무승부 제외) 내림차순, 동률 시 승수 내림차순으로 정렬한 순위표.</summary>
        public List<TeamInfo> GetStandings()
        {
            return standings.Values
                .OrderByDescending(t => t.WinRate)
                .ThenByDescending(t => t.Wins)
                .ToList();
        }

        /// <summary>
        /// 유저 팀 기준 144경기 스케줄을 생성한다. 9개 상대 팀과 각각 16경기(3연전 x 5 + 단판 1경기)씩 만나며,
        /// 같은 상대와의 시리즈가 연속으로 몰리지 않도록 상대팀 순서를 라운드로빈으로 섞는다.
        /// </summary>
        private void BuildUserSchedule()
        {
            var opponents = standings.Keys.Where(t => t != userTeam).ToList(); // 9개 팀
            var seriesIndex = new int[opponents.Count];
            int gameNumber = 0;
            bool anyLeft = true;

            while (anyLeft)
            {
                anyLeft = false;
                for (int i = 0; i < opponents.Count; i++)
                {
                    if (seriesIndex[i] >= SeriesLengths.Length) continue;
                    anyLeft = true;

                    int length = SeriesLengths[seriesIndex[i]];
                    bool userIsHome = seriesIndex[i] % 2 == 0; // 시리즈 인덱스가 짝수면 유저 홈, 홀수면 원정
                    var opponent = opponents[i];
                    seriesIndex[i]++;

                    for (int g = 0; g < length; g++)
                    {
                        gameNumber++;
                        schedule.Add(new MatchFixture
                        {
                            GameNumber = gameNumber,
                            HomeTeam = userIsHome ? userTeam : opponent,
                            AwayTeam = userIsHome ? opponent : userTeam,
                            Phase = gameNumber <= RegularOpenGames ? LeaguePhase.REGULAR_OPEN : LeaguePhase.REGULAR_LOCKED,
                        });
                    }
                }
            }
        }

        /// <summary>스케줄의 다음 미진행 경기를 시뮬레이션하고 결과를 기록한다. 더 진행할 경기가 없으면 null.</summary>
        public MatchFixture PlayNextMatch()
        {
            if (nextFixtureIndex >= schedule.Count) return null;

            var fixture = schedule[nextFixtureIndex];
            nextFixtureIndex++;
            SimulateFixture(fixture);

            return fixture;
        }

        /// <summary>지정한 GameNumber까지(포함) 남은 스케줄을 한 번에 시뮬레이션한다. (빠른 진행용)</summary>
        public void PlayUntil(int gameNumber)
        {
            while (nextFixtureIndex < schedule.Count && schedule[nextFixtureIndex].GameNumber <= gameNumber)
            {
                PlayNextMatch();
            }
        }

        private void SimulateFixture(MatchFixture fixture)
        {
            var home = standings[fixture.HomeTeam];
            var away = standings[fixture.AwayTeam];

            var homeRoster = fixture.HomeTeam == userTeam && GameManager.Instance != null
                ? GameManager.Instance.Roster.ToList()
                : home.Roster;
            var awayRoster = fixture.AwayTeam == userTeam && GameManager.Instance != null
                ? GameManager.Instance.Roster.ToList()
                : away.Roster;

            var engine = new MatchEngine(skillDB);
            var result = engine.PlayFullMatch(homeRoster, awayRoster, fixture.HomeTeam.ToString(), fixture.AwayTeam.ToString());

            fixture.Result = result;
            fixture.IsPlayed = true;

            RecordResult(home, away, result);
        }

        private static void RecordResult(TeamInfo home, TeamInfo away, MatchResult result)
        {
            if (result.WinnerTeamName == null)
            {
                home.Draws++;
                away.Draws++;
            }
            else if (result.WinnerTeamName == home.Team.ToString())
            {
                home.Wins++;
                away.Losses++;
            }
            else
            {
                away.Wins++;
                home.Losses++;
            }
        }
    }
}
