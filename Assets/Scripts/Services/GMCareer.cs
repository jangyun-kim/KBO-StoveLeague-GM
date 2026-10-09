using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Simulation;
using UnityEngine;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-18] 단장 커리어(임기제) - 끝없이 늘어나는 연도(2300 시즌 문제)를 이야기 구조로 통제한다.
    ///   - 임기: 부임 연도부터 3년 단위 계약. 임기 마지막 시즌이 끝나면 구단주 신임도(OwnerTrust)로 재계약/해임을 판정한다(임기 중 해임 없음).
    ///     신임도 40 이상 = 재계약(다음 3년), 미만 = 해임 엔딩. [해임당하지 않음] 옵션은 언제나 재계약.
    ///   - 최대 30시즌(2026~2055): 30번째 시즌이 끝나면 은퇴 엔딩. 엔딩 이후에는 연도 전환을 막는다(새 커리어 = 뉴게임+).
    ///   - 레거시: 우승 · 가을야구 · 시즌 수 · 평균 순위로 점수를 내고, 명예의 전당(점수 60 이상) · 해금(칭호 · 뉴게임+ 보너스)을 기록한다.
    ///     해금은 커리어 상태와 함께 세이브되고, 실행 중(배치 모드 제외) PlayerPrefs에도 누적해 다음 커리어 [뉴게임+]에 쓴다.
    /// </summary>
    public static class GMCareer
    {
        public const int TermYears = 3, MaxSeasons = 30, RenewTrust = 40, HallOfFameScore = 60;
        public const string UnlockPrefKey = "GM_CAREER_UNLOCKS";
        public const long NewGamePlusBudgetPerUnlock = 20000; // 해금 1개당 뉴게임+ 시작 운영 예산 +2억

        public static GMCareerState Ensure(GMLeagueState league)
        {
            var fo = GMFrontOffice.Ensure(league);
            if (fo.Career == null) fo.Career = new GMCareerState();
            var c = fo.Career;
            if (c.StartYear <= 0)
            {
                c.StartYear = league.SeasonYear;
                c.Term = 1;
                c.TermEndYear = league.SeasonYear + TermYears - 1;
            }
            return c;
        }

        public static int FinalYear(GMCareerState c) => c.StartYear + MaxSeasons - 1;
        public static int SeasonsServed(GMLeagueState league) { var c = Ensure(league); return Math.Max(1, league.SeasonYear - c.StartYear + 1); }

        public static string StatusLabel(GMLeagueState league)
        {
            if (league == null) return "";
            var c = Ensure(league);
            if (c.Ended) return $"커리어 종료 · {EndingLabel(c.Ending)}({c.EndYear})";
            return $"단장 {c.Term}기 임기 {c.TermEndYear}년까지 · 커리어 {SeasonsServed(league)}/{MaxSeasons}시즌";
        }

        public static string EndingLabel(GMCareerEnding e) => e == GMCareerEnding.Retired ? "은퇴 엔딩" : e == GMCareerEnding.Dismissed ? "해임 엔딩" : "진행 중";

        /// <summary>
        /// 시즌 종료(연도 전환 직전) - 30번째 시즌 = 은퇴, 임기 마지막 시즌 = 재계약/해임 판정. 커리어가 끝났으면 true(연도 전환 중단).
        /// </summary>
        public static bool OnSeasonCompleted(GMLeagueState league, int completedYear)
        {
            var c = Ensure(league);
            if (c.Ended) return true;
            var fo = GMFrontOffice.Ensure(league);
            if (completedYear >= FinalYear(c))
            {
                Finish(league, GMCareerEnding.Retired, completedYear);
                return true;
            }
            if (completedYear < c.TermEndYear) return false;
            bool noFiring = fo.Manager != null && fo.Manager.NoFiring;
            if (noFiring || fo.OwnerTrust >= RenewTrust)
            {
                c.Term++;
                c.TermEndYear = Math.Min(FinalYear(c), completedYear + TermYears);
                league.AddNews(new GMNewsItem
                {
                    GameIndex = league.GamesPlayed, DateLabel = $"{completedYear} 시즌 결산", Kind = GMNewsKind.Decision, IsUserTeam = true, IsMajor = true,
                    Title = $"단장 재계약 - {c.Term}기 임기({completedYear + 1}~{c.TermEndYear})",
                    Body = $"구단주 신임도 {fo.OwnerTrust}{(noFiring ? "(해임당하지 않음)" : "")} - 구단주가 재계약서에 서명했습니다. 커리어 {completedYear - c.StartYear + 1}/{MaxSeasons}시즌.",
                });
                return false;
            }
            Finish(league, GMCareerEnding.Dismissed, completedYear);
            return true;
        }

        /// <summary>엔딩 확정 - 레거시 점수 · 명예의 전당 · 해금 기록.</summary>
        public static void Finish(GMLeagueState league, GMCareerEnding ending, int year)
        {
            var c = Ensure(league);
            var fo = GMFrontOffice.Ensure(league);
            string code = league.SelectedTeamCode;
            var mine = fo.History.Where(h => h.TeamCode == code && h.Year >= c.StartYear && h.Year <= year).ToList();
            c.Ended = true;
            c.Ending = ending;
            c.EndYear = year;
            c.Seasons = Math.Max(1, year - c.StartYear + 1);
            c.Titles = mine.Count(h => h.Champion);
            c.Postseasons = mine.Count(h => h.Postseason);
            double avgRank = mine.Count > 0 ? mine.Average(h => h.Rank) : 6;
            c.LegacyScore = LegacyScore(c.Titles, c.Postseasons, c.Seasons, avgRank, ending);
            c.HallOfFame = c.LegacyScore >= HallOfFameScore;
            foreach (var u in UnlocksFor(c)) if (!c.Unlocks.Contains(u)) c.Unlocks.Add(u);
            c.Note = Summary(league);
            PersistUnlocks(c.Unlocks);
            league.AddNews(new GMNewsItem
            {
                GameIndex = league.GamesPlayed, DateLabel = $"{year} 시즌 결산", Kind = GMNewsKind.Decision, IsUserTeam = true, IsMajor = true,
                Title = ending == GMCareerEnding.Retired ? $"단장 은퇴 - {c.Seasons}시즌 커리어 마감" : $"단장 해임 - {c.Term}기 임기 만료(구단주 신임도 {fo.OwnerTrust})",
                Body = c.Note,
            });
        }

        /// <summary>레거시 점수 = 우승 × 15 + 가을야구 × 3 + 시즌 × 0.5 + (6 - 평균 순위) × 4 (+ 은퇴 5), 0~150.</summary>
        public static int LegacyScore(int titles, int postseasons, int seasons, double avgRank, GMCareerEnding ending) =>
            Math.Max(0, Math.Min(150, (int)Math.Round(titles * 15 + postseasons * 3 + seasons * 0.5 + (6 - avgRank) * 4 + (ending == GMCareerEnding.Retired ? 5 : 0))));

        /// <summary>해금(칭호) - 다음 커리어 [뉴게임+]에서 해금 1개당 시작 운영 예산 +2억.</summary>
        public static List<string> UnlocksFor(GMCareerState c)
        {
            var list = new List<string>();
            if (c.Titles >= 1) list.Add("우승 단장");
            if (c.Titles >= 3) list.Add("왕조 설계자");
            if (c.Postseasons >= 10) list.Add("가을의 단골");
            if (c.Ending == GMCareerEnding.Retired) list.Add("30년 근속");
            if (c.HallOfFame) list.Add("명예의 전당");
            return list;
        }

        public static string Summary(GMLeagueState league)
        {
            var c = Ensure(league);
            return $"{NameAliasTable.DisplayTeamName(league.SelectedTeamCode)} 단장 {c.StartYear}~{c.EndYear}({c.Seasons}시즌 · {c.Term}기) · 우승 {c.Titles}회 · 가을야구 {c.Postseasons}회 · " +
                   $"레거시 {c.LegacyScore}점{(c.HallOfFame ? " · 명예의 전당 헌액" : "")}" + (c.Unlocks.Count > 0 ? $" · 해금: {string.Join(", ", c.Unlocks)}" : "");
        }

        // ================================================================== 해금 저장 · 뉴게임+

        private static void PersistUnlocks(IEnumerable<string> unlocks)
        {
            if (Application.isBatchMode) return; // CLI 테스트는 기기 저장소를 건드리지 않는다
            var all = new HashSet<string>(StoredUnlocks());
            foreach (var u in unlocks) all.Add(u);
            PlayerPrefs.SetString(UnlockPrefKey, string.Join("|", all));
            PlayerPrefs.Save();
        }

        public static List<string> StoredUnlocks()
        {
            string raw = PlayerPrefs.GetString(UnlockPrefKey, "");
            return raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
        }

        /// <summary>[뉴게임+] - 새 리그에 이전 커리어 해금을 물려준다(해금 1개당 운영 예산 +2억). 적용한 해금 수.</summary>
        public static int ApplyNewGamePlus(GMLeagueState league, IReadOnlyCollection<string> unlocks)
        {
            if (league?.UserTeam == null || unlocks == null || unlocks.Count == 0) return 0;
            var c = Ensure(league);
            foreach (var u in unlocks) if (!c.Unlocks.Contains(u)) c.Unlocks.Add(u);
            league.UserTeam.Budget += NewGamePlusBudgetPerUnlock * unlocks.Count;
            league.AddNews(new GMNewsItem
            {
                GameIndex = 0, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Decision, IsUserTeam = true, IsMajor = true,
                Title = "뉴게임+ - 전설의 단장이 돌아왔다",
                Body = $"이전 커리어 해금 {unlocks.Count}개({string.Join(", ", unlocks)}) - 시작 운영 예산 +{GMDiagnosticFormat.Short(NewGamePlusBudgetPerUnlock * unlocks.Count)}.",
            });
            return unlocks.Count;
        }
    }
}
