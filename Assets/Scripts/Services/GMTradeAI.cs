using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-08] AI 구단장 니즈 기반 1:N 조건부 역제안.
    ///   ① 구단 니즈: 선발투수 부족 · 불펜 보강 · 주전 포수 구멍 · 센터라인 수비 · 장타자 부재 · 샐러리캡 감축 · 리빌딩 유망주 확보(리그 평균 대비 격차 = 심각도 0~1)
    ///   ② 역제안: 유저가 상대 핵심 선수 A 1명을 요구하면, AI가 내 구단 1군 + 퓨처스에서 니즈에 맞는 1~3명(B 유망주 + C 불펜 …)을 골라
    ///      요구 가치(A 가치 × 난이도 배수 + 거래 하드 모드)를 넘기는 가장 알맞은 패키지를 만든다. 선수만으로 모자라면 연봉 보조(최대 5억)를 얹는다.
    ///   ③ 트레이드 가치 바: GMStoveLeagueMarket.Evaluate가 니즈 적합도 · 연봉 보조를 받는 가치에 실시간 반영한다(1:N · 최대 3:3).
    /// </summary>
    public static class GMTradeAI
    {
        public const int MaxPackage = 3;
        public const int MaxCashSubsidy = 50000;    // 연봉 보조 상한 5억 원
        public const int CashStep = 1000;           // 1,000만 원 단위
        public const float CashPerValue = 2000f;    // 연봉 보조 2,000만 원 = 트레이드 가치 1
        public const int CandidatePool = 24;

        /// <summary>연봉 보조(만 원) → 트레이드 가치.</summary>
        public static float CashValue(int cash) => Math.Max(0, Math.Min(MaxCashSubsidy, cash)) / CashPerValue;

        // ================================================================== ① 니즈

        private static double TopAvg(IEnumerable<Player> players, int n, Func<Player, double> f)
        {
            var list = players.Select(f).OrderByDescending(x => x).Take(n).ToList();
            return list.Count == 0 ? 0 : list.Average();
        }

        private static bool IsSP(Player p) => p.IsPitcher && p.Template.PitcherRole == PitcherRole.StartingPitcher;
        private static bool IsRP(Player p) => p.IsPitcher && p.Template.PitcherRole != PitcherRole.StartingPitcher;
        private static bool IsCenter(Player p) => !p.IsPitcher && (p.Position == "SS" || p.Position == "2B" || p.Position == "CF");

        /// <summary>구단 니즈(심각도순 최대 3개, 없으면 "뎁스 보강" 1개).</summary>
        public static List<GMTeamNeed> AnalyzeNeeds(GMLeagueState league, GMTeamState team)
        {
            var needs = new List<GMTeamNeed>();
            if (league == null || team == null) return needs;
            var teams = league.Teams.Values.ToList();
            void Add(string kind, string label, double gap, double scale)
            {
                if (gap <= 0) return;
                needs.Add(new GMTeamNeed { Kind = kind, Label = label, Severity = (float)Math.Min(1.0, gap / scale) });
            }
            var roster = team.Roster.Where(p => p?.Template != null).ToList();
            double LeagueAvg(Func<GMTeamState, double> f) => teams.Count == 0 ? 0 : teams.Average(f);
            Add("SP", "선발투수 부족", LeagueAvg(t => TopAvg(t.Roster.Where(IsSP), 5, p => p.BaseOverall)) - TopAvg(roster.Where(IsSP), 5, p => p.BaseOverall), 6);
            Add("RP", "불펜 보강 필요", LeagueAvg(t => TopAvg(t.Roster.Where(IsRP), 6, p => p.BaseOverall)) - TopAvg(roster.Where(IsRP), 6, p => p.BaseOverall), 6);
            Add("C", "주전 포수 구멍", LeagueAvg(t => TopAvg(t.Roster.Where(p => !p.IsPitcher && p.Position == "C"), 1, p => p.BaseOverall)) - TopAvg(roster.Where(p => !p.IsPitcher && p.Position == "C"), 1, p => p.BaseOverall) - 1, 8);
            Add("CENTER", "센터라인 수비 보강", LeagueAvg(t => TopAvg(t.Roster.Where(IsCenter), 3, p => p.DefenseStat)) - TopAvg(roster.Where(IsCenter), 3, p => p.DefenseStat), 8);
            Add("POWER", "장타자 부재", LeagueAvg(t => TopAvg(t.Batters, 3, p => p.Template.BatterStats.Power)) - TopAvg(roster.Where(p => !p.IsPitcher), 3, p => p.Template.BatterStats.Power), 8);
            if (team.PayrollCap > 0 && team.Payroll > team.PayrollCap * 0.9)
                needs.Add(new GMTeamNeed { Kind = "PAYROLL", Label = "샐러리캡 감축 필요", Severity = (float)Math.Min(1.0, (team.Payroll - team.PayrollCap * 0.9) / (team.PayrollCap * 0.2)) });
            if (!GMFaCompensation.IsWinNow(league, team))
                needs.Add(new GMTeamNeed { Kind = "PROSPECT", Label = "리빌딩 - 유망주 확보", Severity = 0.7f });
            needs = needs.OrderByDescending(n => n.Severity).Take(3).ToList();
            if (needs.Count == 0) needs.Add(new GMTeamNeed { Kind = "DEPTH", Label = "뎁스 보강", Severity = 0.2f });
            return needs;
        }

        public static string NeedsLabel(IEnumerable<GMTeamNeed> needs) => string.Join(" · ", (needs ?? Enumerable.Empty<GMTeamNeed>()).Select(n => n.Label));

        /// <summary>니즈 적합도 배수(1.0 ~ 1.25) - 들어오는 선수가 상대 니즈를 채우면 받는 가치가 오른다.</summary>
        public static float NeedFit(IEnumerable<GMTeamNeed> needs, Player p, out string note)
        {
            note = "";
            if (p?.Template == null || needs == null) return 1f;
            float best = 1f;
            foreach (var n in needs)
            {
                bool fit = n.Kind switch
                {
                    "SP" => IsSP(p),
                    "RP" => IsRP(p),
                    "C" => !p.IsPitcher && p.Position == "C",
                    "CENTER" => IsCenter(p) && p.DefenseStat >= 60,
                    "POWER" => !p.IsPitcher && p.Template.BatterStats.Power >= 68,
                    "PAYROLL" => p.Salary <= 10000,
                    "PROSPECT" => p.Age <= 24 && p.Potential >= 72,
                    _ => false,
                };
                if (!fit) continue;
                float m = 1f + 0.25f * Math.Max(0.4f, n.Severity);
                if (m > best) { best = m; note = n.Label; }
            }
            return best;
        }

        // ================================================================== ② 1:N 역제안

        /// <summary>
        /// 상대 핵심 선수 target 1명에 대한 AI 단장 역제안 - 내 구단 1군 + 퓨처스(부상자 제외)에서 니즈 적합 선수 위주로 1~3명 패키지를 고른다.
        /// 받는 가치(니즈 · 기존 포지션 니즈 배수 포함)가 요구 가치의 100~115%에 가장 가깝고, 니즈를 많이 채우고, 인원이 적을수록 좋은 패키지를 택한다.
        /// 모든 패키지가 모자라면 가치 최대 패키지에 연봉 보조(최대 5억)를 얹는다. 그래도 모자라면 Valid = false.
        /// </summary>
        public static GMTradeCounterOffer BuildCounterOffer(GMLeagueState league, GMTeamState mine, GMTeamState partner, Player target)
        {
            var offer = new GMTradeCounterOffer { PartnerCode = partner?.TeamCode ?? "", Target = target };
            if (league == null || mine == null || partner == null || target == null || !partner.ReservePlayers.Contains(target)) { offer.Pitch = "상대 구단 선수를 고르십시오."; return offer; }
            offer.Needs.AddRange(AnalyzeNeeds(league, partner));
            float required = GMStoveLeagueMarket.TradeValue(target) * GMStoveLeagueMarket.RequiredMargin(league);

            var pool = mine.ReservePlayers.Where(p => p?.Template != null && p.InjuryRemainingDays <= 0)
                .Select(p => (p, v: ReceiveValue(partner, offer.Needs, p), fit: NeedFit(offer.Needs, p, out _) > 1f))
                .Where(x => x.v <= required * 1.6f)               // 요구 가치를 크게 넘는 핵심 선수는 패키지 후보에서 뺀다
                .OrderByDescending(x => x.fit).ThenByDescending(x => x.v)
                .Take(CandidatePool).ToList();
            if (pool.Count == 0) { offer.Pitch = "내줄 수 있는 선수가 없습니다."; return offer; }

            List<int> best = null;
            double bestScore = double.MaxValue;
            List<int> richest = null;
            float richestTotal = 0f;
            int n = pool.Count;
            void Consider(List<int> idx)
            {
                float total = idx.Sum(i => pool[i].v);
                if (total > richestTotal) { richestTotal = total; richest = idx; }
                if (total < required) return;
                int fits = idx.Count(i => pool[i].fit);
                double score = Math.Abs(total / required - 1.05) + 0.01 * (idx.Count - 1) - 0.06 * fits;
                if (score < bestScore) { bestScore = score; best = idx; }
            }
            for (int a = 0; a < n; a++)
            {
                Consider(new List<int> { a });
                for (int b = a + 1; b < n; b++)
                {
                    Consider(new List<int> { a, b });
                    for (int c = b + 1; c < n; c++) Consider(new List<int> { a, b, c });
                }
            }

            int cash = 0;
            if (best == null && richest != null)
            {
                float gap = required - richestTotal;
                cash = (int)Math.Ceiling(gap * CashPerValue / CashStep) * CashStep;
                if (cash <= MaxCashSubsidy && mine.Budget >= cash) best = richest;
                else cash = 0;
            }
            if (best == null)
            {
                offer.Pitch = $"{partner.DisplayName} 단장: \"{target.Template.PlayerName}은(는) 우리 핵심입니다. 그 가치에 맞는 카드가 보이지 않네요.\"";
                offer.Evaluation = GMStoveLeagueMarket.Evaluate(league, mine, new List<Player>(), partner, new List<Player> { target });
                return offer;
            }
            foreach (var i in best) offer.Requested.Add(pool[i].p);
            offer.CashSubsidy = cash;
            offer.Evaluation = GMStoveLeagueMarket.Evaluate(league, mine, offer.Requested, partner, new List<Player> { target }, cash);
            string parts = string.Join(" + ", offer.Requested.Select(p => $"{p.Template.PlayerName}({Role(p)})"));
            offer.Pitch = $"{partner.DisplayName} 단장: \"{NeedsLabel(offer.Needs)} 상황입니다. {target.Template.PlayerName}을(를) 드리는 대신 {parts}" +
                          (cash > 0 ? $"에 연봉 보조 {GMDiagnosticFormat.Won(cash)}을 얹어 주시면" : "을(를) 주시면") + " 바로 사인하죠.\"";
            return offer;
        }

        private static string Role(Player p)
        {
            if (p.Age <= 24 && p.Potential >= 72) return "유망주";
            if (IsSP(p)) return "선발";
            if (IsRP(p)) return "불펜";
            return GMFrontOffice.PositionLabel(p.Position);
        }

        /// <summary>상대 구단이 이 선수를 받을 때의 가치 = 트레이드 가치 × max(포지션 니즈 배수, 니즈 적합도).</summary>
        public static float ReceiveValue(GMTeamState partner, IEnumerable<GMTeamNeed> needs, Player p) =>
            GMStoveLeagueMarket.TradeValue(p) * Math.Max(GMStoveLeagueMarket.NeedMultiplier(partner, p, out _), NeedFit(needs, p, out _));

        /// <summary>역제안 수락 - 패키지 그대로 체결(1:N + 연봉 보조).</summary>
        public static GMNegotiationResult AcceptCounterOffer(GMLeagueState league, GMTeamState mine, GMTradeCounterOffer offer)
        {
            if (offer == null || !offer.Valid || !league.Teams.TryGetValue(offer.PartnerCode, out var partner)) return new GMNegotiationResult { Message = "유효한 역제안이 없습니다." };
            return GMStoveLeagueMarket.ExecuteTrade(league, mine, offer.Requested, partner, new List<Player> { offer.Target }, offer.CashSubsidy);
        }
    }
}
