using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-16] 에이징 커브(연도 전환 AdvanceToNextSeasonYear - 나이 +1 전, 그 시즌을 뛴 나이 기준).
    ///   - 19~29세 성장은 GMFuturesMeeting(19~24세 +2~+3 · 멘토링 +4 / 25~29세 +0~+1, 잠재력 도달 시 유지)이 맡는다.
    ///   - 30~33세: 동결 또는 미세 하락(-1, 선수 · 연도 해시 50%).
    ///   - 34세 이상: 매년 -1~-3 강제 하락(37세 이상 -1 가중). 완화 = 레전드 특성(원 카드 S · A급 또는 주요 수상 3회 이상) -1 ·
    ///     단장 유대(구단 충성도 80 이상 · 주장 · 더그아웃 리더) -1, 최소 -1(강제 하락은 사라지지 않는다).
    ///   - 대상: 10구단 1군 · 퓨처스 + FA 시장. 하락은 유망주 보정치(Player.ProspectStatShift)로 전 세부 스탯을 같은 폭으로 낮춘다(BaseOverall 원칙 · 세이브 그대로).
    /// </summary>
    public static class GMAgingCurve
    {
        public const int PlateauAge = 30, DeclineAge = 34, SteepAge = 37, MaxDecline = 3;

        /// <summary>올해 하락폭(양수 = OVR 하락). 30세 미만 0.</summary>
        public static int DeclineFor(Player p, int year)
        {
            if (p?.Template == null || p.Age < PlateauAge) return 0;
            int h = GMFrontOffice.Hash($"{p.Template.RealPlayerId}_{year}_aging");
            if (p.Age < DeclineAge) return h % 2; // 동결 또는 -1
            int d = 1 + h % 3 + (p.Age >= SteepAge ? 1 : 0);
            if (IsLegend(p)) d--;
            if (HasBond(p)) d--;
            return Math.Max(1, Math.Min(MaxDecline, d));
        }

        /// <summary>레전드 특성 - 원 카드 S · A급(골든글러브 · 왕조 · 타이틀 홀더 · 프랜차이즈 등) 또는 주요 수상 3회 이상.</summary>
        public static bool IsLegend(Player p) =>
            p?.Template != null && (GMStarterDeck.TierOf(p) <= GMStarterTier.A || (p.CareerAwardIds?.Count(Player.IsMajorAward) ?? 0) >= 3);

        /// <summary>단장 유대 - 구단 충성도 80 이상 · 주장 · 더그아웃 리더.</summary>
        public static bool HasBond(Player p) => p != null && (p.Loyalty >= 80 || p.IsCaptain || p.RoleArchetype == LockerRoomRole.DugoutLeader);

        /// <summary>연도 전환 적용 - (선수, 실제 OVR 변화) 목록(0 제외).</summary>
        public static List<(Player player, int delta)> Apply(GMLeagueState league, int year)
        {
            var changed = new List<(Player, int)>();
            if (league == null) return changed;
            foreach (var p in league.Teams.Values.SelectMany(t => t.ReservePlayers).Concat(league.FreeAgents).Distinct().ToList())
            {
                int d = DeclineFor(p, year);
                if (d <= 0) continue;
                int moved = GMRosterTiers.ApplyOvrDelta(p, -d);
                if (moved != 0) changed.Add((p, moved));
            }
            var user = league.UserTeam;
            var mine = changed.Where(x => user != null && user.ReservePlayers.Contains(x.Item1)).OrderBy(x => x.Item2).ToList();
            if (mine.Count > 0)
                league.AddNews(new GMNewsItem
                {
                    GameIndex = 0, DateLabel = $"{year} 시즌 종료", Kind = GMNewsKind.Season, IsUserTeam = true,
                    Title = $"[에이징 커브] 베테랑 {mine.Count}명 기량 하락",
                    Body = string.Join(" · ", mine.Take(6).Select(x => $"{x.Item1.Template.PlayerName}({x.Item1.Age}세) OVR {x.Item2}")),
                });
            return changed;
        }
    }
}
