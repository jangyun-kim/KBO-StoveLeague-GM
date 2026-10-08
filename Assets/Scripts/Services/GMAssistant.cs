using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>[TASK-GM-16] 조력자 개입 사유 한 건.</summary>
    public enum GMAssistantIssueKind { ExpiredContracts = 0, SecondDraftNoPick = 1, RookieDraftNoPick = 2, NoCaptain = 3, ShortRoster = 4 }

    public class GMAssistantIssue
    {
        public GMAssistantIssueKind Kind;
        public string Key = "";
        public string Title = "";
        /// <summary>조력자 대사(스토리형 대화 - 한 줄씩 넘긴다).</summary>
        public readonly List<string> Lines = new List<string>();
        /// <summary>[제가 직접 처리하겠습니다] - 돌아갈 방(허브 Pane 이름).</summary>
        public string Pane = "";
        public readonly List<Player> Players = new List<Player>();
    }

    /// <summary>[TASK-GM-16] 조력자 프로필 - 단장 성별의 반대(남 단장 = 여 팀장, 여 단장 = 남 팀장).</summary>
    public class GMAssistantProfile
    {
        public string Name = "";
        public string Title = "운영팀장";
        public bool Female;
        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1);
        public string Plate => $"{Title} {Name}";
    }

    /// <summary>
    /// [TASK-GM-16] 조력자(운영팀장) 턴 가이드 - 스토브리그 8 Turn 중 필수 행동을 하지 않고 [진행하기]를 누르면 대화형 모달로 개입한다.
    ///   개입 사유: ① 계약 만료자(잔여 0년) 미계약(Turn 4 이후) ② 2차 드래프트 지명 0명(Turn 3 · 홀수 해) ③ 신인 드래프트 지명 0명(Turn 7)
    ///             ④ 주장 미임명(Turn 8) ⑤ 1군 28명 미만(Turn 8).
    ///   선택지: [제가 직접 처리하겠습니다(돌아가기)] = 해당 방으로 / [팀장 선에서 적당히 처리해 주세요(AI 위임)] = Delegate 후 Turn 진행 /
    ///          [괜찮으니 그냥 진행하세요(무시)] = Ignore(그해 같은 경고는 다시 묻지 않음) 후 Turn 진행.
    ///   하드 차단(GMStoveTurns.BlockReason - 라커룸 사건 · 정원 초과)은 그대로 두고, 이 가이드는 "넘어갈 수는 있지만 놓치면 손해"인 행동만 다룬다.
    /// </summary>
    public static class GMAssistant
    {
        public const string FemaleName = "한서윤", MaleName = "강도윤";
        public const int MinRosterForOpening = GMRosterLoader.BatterCount + GMRosterLoader.PitcherCount; // 28

        public static GMAssistantProfile ProfileFor(GMLeagueState league)
        {
            var manager = league != null ? GMFrontOffice.Manager(league) : null;
            bool female = !(manager?.Female ?? false); // 단장과 반대 성별
            return new GMAssistantProfile { Female = female, Name = female ? FemaleName : MaleName };
        }

        private static string Key(GMLeagueState league, GMAssistantIssueKind kind) => $"{league.SeasonYear}|{kind}";

        public static bool IsIgnored(GMLeagueState league, GMAssistantIssueKind kind) =>
            league != null && GMFrontOffice.Ensure(league).AssistantIgnored.Contains(Key(league, kind));

        /// <summary>계약 만료(잔여 0년)인데 아직 재계약하지 않은 내 구단 보류선수.</summary>
        public static List<Player> ExpiredUnsigned(GMLeagueState league) =>
            league?.UserTeam == null ? new List<Player>() :
                league.UserTeam.ReservePlayers.Where(p => p?.Template != null && p.ContractYears <= 0).OrderByDescending(p => p.BaseOverall).ToList();

        /// <summary>지금 Turn을 넘기기 전에 조력자가 짚을 사항(무시한 사항 제외). Turn 통제 밖이면 빈 목록.</summary>
        public static List<GMAssistantIssue> PendingIssues(GMLeagueState league)
        {
            var list = new List<GMAssistantIssue>();
            if (!GMStoveTurns.IsGating(league) || league.UserTeam == null) return list;
            var team = league.UserTeam;
            var fo = GMFrontOffice.Ensure(league);
            int turn = fo.StoveTurn;
            string boss = BossName(league);

            if (turn >= 4 && !IsIgnored(league, GMAssistantIssueKind.ExpiredContracts))
            {
                var expired = ExpiredUnsigned(league);
                if (expired.Count > 0)
                {
                    var issue = New(league, GMAssistantIssueKind.ExpiredContracts, $"계약 만료자 {expired.Count}명 미계약", "GMNegotiationRoomView");
                    issue.Players.AddRange(expired);
                    var star = expired[0];
                    issue.Lines.Add($"계약이 끝난 선수가 아직 {expired.Count}명 남아 있습니다 - {Names(expired, 4)}.");
                    issue.Lines.Add($"특히 {star.Template.PlayerName} 선수(OVR {star.BaseOverall} · {star.Age}세)는 우리 팀 핵심 전력이에요. 이대로 개막을 맞으면 우선 협상이 마감되고 전원 FA 시장으로 나갑니다.");
                    issue.Lines.Add(turn == 4
                        ? "지금이 계약 협상실이 열려 있는 Turn입니다. 놓치면 다른 구단이 바로 채어 갈 거예요."
                        : $"{boss}, 재계약 없이 벌써 Turn {turn}까지 왔습니다. 남길 선수와 보낼 선수를 정해 주셔야 합니다.");
                    list.Add(issue);
                }
            }
            if (turn == 3 && GMSecondaryDraft.IsDraftYear(league.SeasonYear) && !GMSecondaryDraft.IsHeld(league) && !IsIgnored(league, GMAssistantIssueKind.SecondDraftNoPick)
                && GMSecondaryDraft.PicksBy(league, team.TeamCode) == 0 && GMSecondaryDraft.Pool(league, team.TeamCode).Count > 0)
            {
                var issue = New(league, GMAssistantIssueKind.SecondDraftNoPick, "2차 드래프트 지명 0명", "GMSecondaryDraftView");
                var best = GMSecondaryDraft.Pool(league, team.TeamCode).First();
                issue.Lines.Add($"2차 드래프트 보호 명단을 확정하고 지명을 한 명도 하지 않으셨어요. 이번 Turn을 넘기면 다른 구단이 지명을 마감합니다.");
                issue.Lines.Add($"시장에 {best.Item1.Template.PlayerName}(OVR {best.Item1.BaseOverall}) 같은 선수가 풀려 있습니다. 양도금 1R {GMDiagnosticFormat.Won(GMSecondaryDraft.FeeFor(1))}이면 데려올 수 있어요.");
                list.Add(issue);
            }
            if (turn == 7 && fo.DraftPicksThisYear == 0 && league.DraftPool.Count > 0 && !IsIgnored(league, GMAssistantIssueKind.RookieDraftNoPick))
            {
                var issue = New(league, GMAssistantIssueKind.RookieDraftNoPick, "신인 드래프트 지명 0명", "Pane_Draft");
                var top = league.DraftPool.OrderByDescending(p => p.Potential).First();
                issue.Lines.Add($"올해 신인 지명권을 아직 쓰지 않으셨습니다. 스카우트팀은 {top.Template.PlayerName}(잠재력 {top.Potential})을 1순위로 추천하고 있어요.");
                issue.Lines.Add("지명권은 해가 넘어가면 사라집니다. 미래 전력을 위해 한 명이라도 데려오시는 게 좋겠습니다.");
                list.Add(issue);
            }
            if (turn == GMStoveTurns.TurnCount)
            {
                if (!team.Roster.Any(p => p.IsCaptain) && !IsIgnored(league, GMAssistantIssueKind.NoCaptain))
                {
                    var issue = New(league, GMAssistantIssueKind.NoCaptain, "주장 미임명", "Pane_Salaries");
                    issue.Lines.Add("새 시즌 주장이 아직 정해지지 않았습니다. 라커룸이 구심점 없이 시즌을 시작하게 돼요.");
                    issue.Lines.Add("더그아웃 리더 성향의 베테랑을 세우면 팀워크가 오르고 슈퍼스타 파벌도 눌러 줄 수 있습니다.");
                    list.Add(issue);
                }
                if (team.Roster.Count < MinRosterForOpening && !IsIgnored(league, GMAssistantIssueKind.ShortRoster))
                {
                    var issue = New(league, GMAssistantIssueKind.ShortRoster, $"1군 {team.Roster.Count}명(개막 권장 {MinRosterForOpening}명)", "GMFuturesMeetingView");
                    issue.Lines.Add($"1군이 {team.Roster.Count}명뿐입니다. 개막 엔트리를 꾸리려면 최소 {MinRosterForOpening}명이 필요해요.");
                    issue.Lines.Add($"퓨처스에 {team.Futures.Count}명이 대기 중입니다. 콜업하거나 FA로 채워 주세요.");
                    list.Add(issue);
                }
            }
            return list;
        }

        private static GMAssistantIssue New(GMLeagueState league, GMAssistantIssueKind kind, string title, string pane) =>
            new GMAssistantIssue { Kind = kind, Key = Key(league, kind), Title = title, Pane = pane };

        private static string Names(List<Player> players, int max) =>
            string.Join(" · ", players.Take(max).Select(p => p.Template.PlayerName)) + (players.Count > max ? $" 외 {players.Count - max}명" : "");

        private static string BossName(GMLeagueState league)
        {
            var m = GMFrontOffice.Manager(league);
            string name = m?.Name;
            return string.IsNullOrEmpty(name) || name == "단장" ? "단장님" : $"{name} 단장님";
        }

        /// <summary>대화 전체 - 인사 → 사유별 대사 → 선택 안내.</summary>
        public static List<string> Script(GMLeagueState league, IReadOnlyList<GMAssistantIssue> issues)
        {
            var a = ProfileFor(league);
            var lines = new List<string>
            {
                $"{BossName(league)}, 잠시만요! {a.Plate}입니다. {GMStoveTurns.Label(GMStoveTurns.Current(league))}을(를) 넘기기 전에 확인하실 게 {issues.Count}건 있습니다.",
            };
            foreach (var i in issues) lines.AddRange(i.Lines);
            lines.Add("어떻게 할까요? 직접 하시겠다면 해당 방으로 모시고, 맡겨 주시면 제 선에서 무리 없이 정리해 두겠습니다.");
            return lines;
        }

        // ================================================================== 처리

        /// <summary>[팀장 선에서 적당히 처리해 주세요] - 사유별 자동 처리. 처리 내역 문장 목록.</summary>
        public static List<string> Delegate(GMLeagueState league, IReadOnlyList<GMAssistantIssue> issues)
        {
            var done = new List<string>();
            var team = league?.UserTeam;
            if (team == null || issues == null) return done;
            var fo = GMFrontOffice.Ensure(league);
            foreach (var issue in issues)
            {
                switch (issue.Kind)
                {
                    case GMAssistantIssueKind.ExpiredContracts: done.AddRange(ResolveExpired(league, team)); break;
                    case GMAssistantIssueKind.SecondDraftNoPick:
                    {
                        var best = GMSecondaryDraft.Pool(league, team.TeamCode).Select(x => x.Item1).FirstOrDefault();
                        var pick = best != null ? GMSecondaryDraft.UserPick(league, best, out var msg) : null;
                        done.Add(pick != null ? $"2차 드래프트 1R {pick.PlayerName} 지명" : "2차 드래프트 - 조건에 맞는 지명 대상이 없어 보류");
                        break;
                    }
                    case GMAssistantIssueKind.RookieDraftNoPick:
                    {
                        var top = league.DraftPool.OrderByDescending(p => p.Potential).ThenByDescending(p => p.BaseOverall).FirstOrDefault();
                        var r = top != null ? GMStoveLeagueMarket.Draft(league, team, top) : null;
                        done.Add(r != null && r.Success ? $"신인 {top.Template.PlayerName}(잠재력 {top.Potential}) 지명" : $"신인 지명 보류 - {r?.Message}");
                        break;
                    }
                    case GMAssistantIssueKind.NoCaptain:
                    {
                        var c = team.Roster.Where(p => p.RoleArchetype == LockerRoomRole.DugoutLeader).OrderByDescending(p => p.Age).ThenByDescending(p => p.BaseOverall).FirstOrDefault()
                                ?? team.Roster.Where(p => !p.IsPitcher).OrderByDescending(p => p.Age).FirstOrDefault();
                        if (c != null) { GMStoveLeagueMarket.AppointCaptain(league, team, c); done.Add($"주장 {c.Template.PlayerName} 임명"); }
                        break;
                    }
                    case GMAssistantIssueKind.ShortRoster:
                    {
                        var up = new List<string>();
                        while (team.Roster.Count < MinRosterForOpening && team.Futures.Count > 0)
                        {
                            var best = team.Futures.OrderByDescending(p => p.BaseOverall).First();
                            if (!GMRosterTiers.CallUp(league, team, best, out _)) break;
                            up.Add(best.Template.PlayerName);
                        }
                        done.Add(up.Count > 0 ? $"퓨처스 콜업 {up.Count}명({string.Join(" · ", up.Take(4))})" : "콜업할 퓨처스 선수가 없습니다");
                        break;
                    }
                }
            }
            fo.AssistantDelegations++;
            var a = ProfileFor(league);
            fo.AssistantLastNote = $"{a.Plate} 위임 처리 - {string.Join(" / ", done)}";
            league.AddNews(new GMNewsItem
            {
                GameIndex = 0, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Season, IsUserTeam = true,
                Title = $"[{a.Plate}] 단장 위임 업무 처리", Body = string.Join(" · ", done),
            });
            return done;
        }

        /// <summary>
        /// 계약 만료자 정리 - 1군 상위 전력(1군 OVR 상위 20위 이내 - 2) · 28세 이하는 요구액 × 희망 기간으로 재계약, 나머지는 FA 공시.
        /// 퓨처스 만료자는 최저연봉 2년. 예산이 모자라 재계약에 실패하면 FA 공시한다.
        /// </summary>
        public static List<string> ResolveExpired(GMLeagueState league, GMTeamState team)
        {
            var done = new List<string>();
            var expired = ExpiredUnsigned(league);
            if (expired.Count == 0) return done;
            var ovrs = team.Roster.Select(p => p.BaseOverall).OrderByDescending(v => v).ToList();
            int bar = ovrs.Count >= 20 ? ovrs[19] - 2 : 0;
            var diff = GMFrontOffice.Ensure(league).Difficulty;
            var kept = new List<string>();
            var gone = new List<string>();
            foreach (var p in expired)
            {
                bool keep = p.BaseOverall >= bar || p.Age <= 28;
                if (keep && team.Futures.Contains(p))
                {
                    p.ContractYears = 2;
                    p.Salary = Math.Max(Player.MinSalary, p.Salary);
                    kept.Add(p.Template.PlayerName);
                    continue;
                }
                if (keep)
                {
                    var r = GMStoveLeagueMarket.Extend(league, team, p, GMStoveLeagueMarket.PreferredYears(p), (int)Math.Round(GMStoveLeagueMarket.ExtensionDemand(p, diff) * 1.03), false); // 요구액 +3%(수락 기준 여유)
                    if (r.Success) { kept.Add(p.Template.PlayerName); continue; }
                }
                if (GMFaCompensation.DeclareFreeAgent(league, team, p) != null) gone.Add(p.Template.PlayerName);
                league.PriorityNegotiationIds.Remove(p.InstanceId);
            }
            if (kept.Count > 0) done.Add($"재계약 {kept.Count}명({string.Join(" · ", kept.Take(5))})");
            if (gone.Count > 0) done.Add($"FA 공시 {gone.Count}명({string.Join(" · ", gone.Take(5))})");
            return done;
        }

        /// <summary>[괜찮으니 그냥 진행하세요] - 그해 같은 경고는 다시 묻지 않는다.</summary>
        public static void Ignore(GMLeagueState league, IReadOnlyList<GMAssistantIssue> issues)
        {
            if (league == null || issues == null) return;
            var fo = GMFrontOffice.Ensure(league);
            foreach (var i in issues) if (!fo.AssistantIgnored.Contains(i.Key)) fo.AssistantIgnored.Add(i.Key);
            fo.AssistantIgnores++;
            fo.AssistantLastNote = $"단장 판단으로 진행 - {string.Join(" · ", issues.Select(i => i.Title))}";
        }
    }
}
