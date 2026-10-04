using System;
using System.Collections.Generic;
using KBOManager.Models;
using UnityEngine;

namespace KBOManager.Managers
{
    /// <summary>시즌 1개의 아카이빙된 요약 기록. 살아있는 Player/TeamInfo 객체를 참조하지 않고, 전부 값
    /// 타입(string/int/Team)으로만 구성된 독립 스냅샷이다 - SeasonStatManager.ResetSeason()이 원본
    /// 딕셔너리를 비워도 이미 여기 문자열로 굳어진 기록은 전혀 영향을 받지 않는다.
    /// [TASK-KBO-055] ChampionTeam은 원래 Team?(Nullable)이었으나, [Serializable] 클래스의 Nullable
    /// 필드는 Unity가 직렬화를 지원하지 않아(UAC1001) Team.None을 "우승팀 미확정" 대용으로 쓰는
    /// PostSeasonSaveData/HallOfFameEntrySaveData와 동일한 관례로 통일했다.</summary>
    [Serializable]
    public class HallOfFameEntry
    {
        public int SeasonYear;
        public Team ChampionTeam = Team.None;
        public string BattingAverageLeader;
        public string HomeRunLeader;
        public string WinsLeader;
        public string EraLeader;
    }

    /// <summary>
    /// 스토브리그(AI 자동 성장 등)가 끝난 뒤 다음 시즌 개막까지의 롤오버를 담당하는 싱글톤.
    /// 1) 이번 시즌 타이틀 홀더/우승팀을 명예의 전당에 아카이빙 2) SeasonStatManager/PostSeasonManager
    /// 리셋 3) LeagueCalendar 연도 +1 4) LeagueManager.AdvanceToNextSeason()(AI 성장 + 스케줄/순위표
    /// 리셋 - 기존 로직을 그대로 재사용하며 중복 구현하지 않는다) 순서로 실행한다.
    /// </summary>
    public class SeasonRollover : MonoBehaviour
    {
        public static SeasonRollover Instance { get; private set; }

        [SerializeField] private LeagueManager leagueManager;
        [SerializeField] private SeasonStatManager seasonStatManager;
        [SerializeField] private PostSeasonManager postSeasonManager;
        [Tooltip("[TASK-KBO-190] 새 시즌 전환 시 결산 상태(중복 지급 방지 플래그)를 비운다. 비어 있으면 SeasonRewardManager.Instance.")]
        [SerializeField] private SeasonRewardManager seasonRewardManager;

        private readonly List<HallOfFameEntry> hallOfFame = new List<HallOfFameEntry>();
        public IReadOnlyList<HallOfFameEntry> HallOfFame => hallOfFame;

        /// <summary>SaveManager 전용 복원 진입점. 로드 시 저장된 명예의 전당 기록으로 통째로 교체한다.</summary>
        public void ReplaceHallOfFame(IEnumerable<HallOfFameEntry> entries)
        {
            hallOfFame.Clear();
            if (entries != null) hallOfFame.AddRange(entries);
        }

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

        /// <summary>스토브리그 처리가 모두 끝난 뒤 UI(예: "다음 시즌 시작" 버튼)가 호출하는 단일 진입점.</summary>
        /// <remarks>
        /// [TASK-KBO-190] 승격 판정(정규시즌 1위 또는 한국시리즈 우승 - SeasonCycle.ShouldPromote)을 포스트시즌 브래킷을 비우기 전에 끝내
        /// AdvanceToNextSeason(promote)로 넘긴다(예전에는 정규시즌 1위만 승격). 새 시즌은 승격된 리그의 권장 OVR 분포로 AI 9개 구단을 재조정하고
        /// 승·무·패 · 경기일(1/144) · 시즌 개인 기록 · 포스트시즌 · 결산 플래그만 비운다 - 유저 카드 · 성장 · 라인업 · 재화 · 치어리더는 그대로다. 끝나면 저장한다.
        /// </remarks>
        public void RolloverToNextSeason()
        {
            if (leagueManager == null) return;

            bool promote = SeasonCycle.ShouldPromote(leagueManager.UserFinalRank, postSeasonManager != null ? postSeasonManager.ChampionTeam : null,
                leagueManager.UserTeam, leagueManager.CurrentTier);

            ArchiveCurrentSeason();

            // 아카이빙을 먼저 끝낸 뒤에야 초기화한다 - 순서가 바뀌면 이번 시즌 기록이 리셋된 뒤라
            // 명예의 전당에 빈 값(0/무기록)만 남게 된다.
            seasonStatManager?.ResetSeason();
            postSeasonManager?.ResetForNextSeason();
            (seasonRewardManager != null ? seasonRewardManager : SeasonRewardManager.Instance)?.ResetForNextSeason();

            int nextYear = (LeagueCalendar.Instance != null ? LeagueCalendar.Instance.CurrentDate.Year : DateTime.Now.Year) + 1;
            LeagueCalendar.Instance?.InitializeSeason(nextYear);

            leagueManager.AdvanceToNextSeason(promote);
            SaveManager.Instance?.TrySaveCareer();
        }

        private void ArchiveCurrentSeason()
        {
            if (seasonStatManager == null || leagueManager == null) return;

            int teamGamesPlayed = leagueManager.PlayedGameCount;

            var entry = new HallOfFameEntry
            {
                SeasonYear = LeagueCalendar.Instance != null ? LeagueCalendar.Instance.CurrentDate.Year : DateTime.Now.Year,
                ChampionTeam = postSeasonManager?.ChampionTeam ?? Team.None,
                BattingAverageLeader = FirstLeaderLabel(seasonStatManager.GetBattingAverageLeaders(teamGamesPlayed, 1),
                    s => $"{s.BattingAverage:F3}"),
                HomeRunLeader = FirstLeaderLabel(seasonStatManager.GetHomeRunLeaders(1), s => $"{s.HomeRuns}개"),
                WinsLeader = FirstLeaderLabel(seasonStatManager.GetWinLeaders(1), s => $"{s.Wins}승"),
                EraLeader = FirstLeaderLabel(seasonStatManager.GetEraLeaders(teamGamesPlayed, 1), s => $"{s.EarnedRunAverage:F2}"),
            };

            hallOfFame.Add(entry);
        }

        private static string FirstLeaderLabel<TStats>(List<(Player Player, TStats Stats)> leaders, Func<TStats, string> formatValue)
        {
            if (leaders == null || leaders.Count == 0) return "기록 없음";

            var (player, stats) = leaders[0];
            string playerName = player?.Template != null ? player.Template.PlayerName : "알 수 없음";
            return $"{playerName} ({formatValue(stats)})";
        }
    }
}
