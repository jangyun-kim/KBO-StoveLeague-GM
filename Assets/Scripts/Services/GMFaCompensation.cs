using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Core;
using KBOManager.Data;
using KBOManager.Models;
using KBOManager.Simulation;

namespace KBOManager.Services
{
    /// <summary>
    /// [TASK-GM-08] KBO 규약 기반 FA 등급 · 보상제 + AI 구단 보호 명단(20/25인) 10대 가중치 알고리즘(순수 로직 - UI는 GMOotpFrontOfficeUIController 「FA 보상·보호명단」).
    ///   등급(원 소속 구단 연봉 순위 · 리그 연봉 순위): A = 구단 3위 이내 + 리그 30위 이내 / B = 구단 10위 이내 + 리그 60위 이내 / C = 그 밖, 35세 이상은 C(특례).
    ///   보상: A = 보호 20인 외 1명 + 전년도 연봉 200%(또는 보상선수 없이 300%) · B = 보호 25인 외 1명 + 100%(또는 200%) · C = 150%(보상선수 없음).
    ///   보호 명단은 영입 구단이 1군 + 퓨처스 보류선수 전체에서 작성하고, 원 소속 구단이 명단 밖 1명(또는 현금만)을 고른다.
    ///   10대 가중치: ① 포지션 대체 불가능성 30% ② 잠재력 25% ③ 현재 OVR 20% ④ 팀 역할 · 워크에식 15% ⑤ 계약 효율성 10%
    ///                ⑥ 윈나우(OVR ×1.3 · 잠재력 ×0.75) / 리빌딩(잠재력 ×1.3 · OVR ×0.8) 노선 보정 ⑦ 유일 포수 · 1선발 · 마무리 · 좌완 불펜 필수 보정
    ///                ⑧ 당해 신인 · 외국인 · FA 계약 당사자 자동 보호 ⑨ 타선 · 선발 · 불펜 · 유망주 최소 전력 검수 ⑩ 단장 성향 ±3~5 편차.
    /// 좌완 여부는 선수 DB에 투타 정보가 없어 RealPlayerId 해시로 추정한다(약 30%, KBO 좌완 투수 비율 근사).
    /// </summary>
    public static class GMFaCompensation
    {
        public const int ProtectA = 20, ProtectB = 25;
        public const int AWithPlayerPercent = 200, ACashOnlyPercent = 300;
        public const int BWithPlayerPercent = 100, BCashOnlyPercent = 200;
        public const int CCashPercent = 150;
        public const int VeteranCAge = 35;
        public const float ScarcityBonus = 12f, ScarcityCap = 20f;
        public const float AiPickPlayerMinScore = 40f; // 명단 밖 최고 선수가 이 점수 이상이면 AI는 보상선수를 지명한다(미만이면 현금만)
        public static readonly float[] BaseWeights = { 30f, 25f, 20f, 15f, 10f };
        public static readonly string[] WeightLabels = { "① 포지션 대체 불가능성", "② 잠재력", "③ 현재 OVR", "④ 팀 역할·워크에식", "⑤ 계약 효율성" };
        private static readonly string[] PersonalityLabels = { "데이터 분석형", "스카우트 직감형", "올드스쿨형" };

        // ================================================================== 등급 · 보상 규정

        public static string GradeLabel(GMFaGrade g) => g == GMFaGrade.A ? "A등급" : g == GMFaGrade.B ? "B등급" : "C등급";

        /// <summary>등급 판정 - teamRank(원 소속 구단 연봉 순위 1~) · leagueRank(리그 전체 연봉 순위 1~) · 나이.</summary>
        public static GMFaGrade GradeFor(int teamRank, int leagueRank, int age)
        {
            if (age >= VeteranCAge) return GMFaGrade.C;
            if (teamRank <= 3 && leagueRank <= 30) return GMFaGrade.A;
            if (teamRank <= 10 && leagueRank <= 60) return GMFaGrade.B;
            return GMFaGrade.C;
        }

        /// <summary>선수 등급 - 원 소속 구단 로스터(+ 본인) 연봉 순위와 리그 전체(10구단 1군 + FA) 연봉 순위로 판정.</summary>
        public static GMFaGrade GradeOf(GMLeagueState league, Player p, string originCode)
        {
            if (league == null || p == null) return GMFaGrade.C;
            int salary = p.Salary;
            var team = originCode != null && league.Teams.TryGetValue(originCode, out var t) ? t : null;
            int teamRank = 1 + (team?.Roster.Count(x => x != p && x.Salary > salary) ?? 0);
            int leagueRank = 1 + league.AllPlayers.Concat(league.FreeAgents).Count(x => x != p && x.Salary > salary);
            return GradeFor(teamRank, leagueRank, p.Age);
        }

        public static int ProtectionSize(GMFaGrade g) => g == GMFaGrade.A ? ProtectA : g == GMFaGrade.B ? ProtectB : 0;
        public static bool RequiresPlayer(GMFaGrade g) => g != GMFaGrade.C;
        public static long CashWithPlayer(GMFaGrade g, int prevSalary) => (long)prevSalary * (g == GMFaGrade.A ? AWithPlayerPercent : g == GMFaGrade.B ? BWithPlayerPercent : CCashPercent) / 100;
        public static long CashOnly(GMFaGrade g, int prevSalary) => (long)prevSalary * (g == GMFaGrade.A ? ACashOnlyPercent : g == GMFaGrade.B ? BCashOnlyPercent : CCashPercent) / 100;

        public static string TermsLabel(GMFaGrade g) => g == GMFaGrade.A
            ? $"보호선수 {ProtectA}인 외 1명 + 전년도 연봉 {AWithPlayerPercent}% (또는 보상선수 없이 {ACashOnlyPercent}%)"
            : g == GMFaGrade.B
                ? $"보호선수 {ProtectB}인 외 1명 + 전년도 연봉 {BWithPlayerPercent}% (또는 보상선수 없이 {BCashOnlyPercent}%)"
                : $"보상선수 없이 전년도 연봉 {CCashPercent}%";

        /// <summary>FA 시장 선수의 원 소속(카드 발급 구단 계보) · 등급 · 전년도 연봉을 기록한다(이미 있으면 건너뜀). 방출 선수는 원 소속이 없다(자유계약).</summary>
        public static void RegisterMarketOrigins(GMLeagueState league)
        {
            if (league == null) return;
            foreach (var p in league.FreeAgents)
            {
                if (p?.Template == null || league.FAOrigins.ContainsKey(p.InstanceId)) continue;
                string code = NameAliasTable.ToCode(p.Template.Team != Team.None ? p.Template.Team : p.Template.CurrentTeam);
                if (code == null || !league.Teams.ContainsKey(code)) continue;
                league.FAOrigins[p.InstanceId] = new GMFaOriginEntry { PlayerId = p.InstanceId, TeamCode = code, Grade = GradeOf(league, p, code), PrevSalary = p.Salary };
            }
        }

        /// <summary>
        /// 계약 만료 선수 FA 공시 - 구단 로스터(1군 · 퓨처스)에서 FA 시장으로 옮기고, 옮기기 전 연봉 순위로 등급을 매겨 원 소속을 기록한다(C-06 만료자 순환의 기본 단위).
        /// </summary>
        public static GMFaOriginEntry DeclareFreeAgent(GMLeagueState league, GMTeamState team, Player p)
        {
            if (league == null || team == null || p == null || !team.ReservePlayers.Contains(p)) return null;
            var entry = new GMFaOriginEntry { PlayerId = p.InstanceId, TeamCode = team.TeamCode, Grade = GradeOf(league, p, team.TeamCode), PrevSalary = p.Salary };
            if (!team.Roster.Remove(p)) team.Futures.Remove(p);
            string id = p.InstanceId;
            team.Lineup.Starters.RemoveAll(x => x.InstanceId == id);
            team.Lineup.Roles.RemoveAll(x => x.InstanceId == id);
            team.Lineup.PitcherSlots.RemoveAll(x => x.InstanceId == id);
            GMCheerleaderRoster.RefreshDedications(team);
            p.IsCaptain = false;
            p.ContractYears = 0;
            league.FreeAgents.Insert(0, p);
            league.FAOrigins[p.InstanceId] = entry;
            return entry;
        }

        public static GMFaOriginEntry OriginOf(GMLeagueState league, Player p) =>
            league != null && p != null && league.FAOrigins.TryGetValue(p.InstanceId, out var e) ? e : null;

        /// <summary>FA 화면 한 줄 - "원 소속 KIA · A등급 · 보호선수 20인 외 1명 + …".</summary>
        public static string CompensationLabel(GMLeagueState league, Player p, string signerCode)
        {
            var o = OriginOf(league, p);
            if (o == null) return "자유계약 선수 - 보상 없음";
            if (o.TeamCode == signerCode) return $"원 소속 {CompyaShort(o.TeamCode)} 복귀 · {GradeLabel(o.Grade)} - 보상 없음";
            return $"원 소속 {CompyaShort(o.TeamCode)} · {GradeLabel(o.Grade)} · {TermsLabel(o.Grade)}";
        }

        private static string CompyaShort(string code) => KBOManager.UI.CompyaUiKit.ShortName(NameAliasTable.ToTeam(code));

        /// <summary>FA 영입 직후 호출 - 원 소속이 다른 구단이면 보상 정산 대기 1건을 만든다. FA 계약 당사자는 올해 자동 보호.</summary>
        public static GMPendingCompensation OnFreeAgentSigned(GMLeagueState league, GMTeamState signer, Player p)
        {
            if (league == null || signer == null || p == null) return null;
            if (!league.FASignedThisYear.Contains(p.InstanceId)) league.FASignedThisYear.Add(p.InstanceId);
            var o = OriginOf(league, p);
            GMCareerTimeline.OnFreeAgentSigned(league, p, signer.TeamCode, o?.TeamCode); // [TASK-GM-19] 커리어 타임라인 · 근속 초기화(유저 · AI 공통 FA 계약 지점)
            league.FAOrigins.Remove(p.InstanceId);
            if (o == null || o.TeamCode == signer.TeamCode || !league.Teams.ContainsKey(o.TeamCode)) return null;
            var pending = new GMPendingCompensation
            {
                PlayerId = p.InstanceId, PlayerName = p.Template.PlayerName, FromTeam = o.TeamCode, ToTeam = signer.TeamCode,
                Grade = o.Grade, PrevSalary = o.PrevSalary, Year = league.SeasonYear,
            };
            league.PendingCompensations.Add(pending);
            return pending;
        }

        // ================================================================== 보호 명단 10대 가중치

        /// <summary>⑥ 노선 - 최근 성적(진행 중 승률 → 지난 시즌 승률)과 주전 전력으로 윈나우/리빌딩을 판정한다.</summary>
        public static bool IsWinNow(GMLeagueState league, GMTeamState team)
        {
            if (league == null || team == null) return true;
            var rec = league.RecordOf(team.TeamCode);
            double pct = rec.G >= 20 ? rec.Pct : GMFrontOffice.LastPct(league, team.TeamCode);
            double myCore = TeamCore(team);
            var cores = league.Teams.Values.Select(TeamCore).OrderBy(x => x).ToList();
            double median = cores.Count == 0 ? myCore : cores[cores.Count / 2];
            return pct >= 0.52 || pct >= 0.47 && myCore >= median;
        }

        private static double TeamCore(GMTeamState t) => t.Roster.Count == 0 ? 0 : t.Roster.OrderByDescending(p => p.BaseOverall).Take(20).Average(p => p.BaseOverall);

        /// <summary>①~⑤ 실제 가중치(합계 100) - ⑥ 노선 배수를 적용해 다시 정규화한다.</summary>
        public static float[] WeightsFor(bool winNow)
        {
            var w = (float[])BaseWeights.Clone();
            if (winNow) { w[2] *= 1.3f; w[1] *= 0.75f; }
            else { w[1] *= 1.3f; w[2] *= 0.8f; }
            float sum = w.Sum();
            for (int i = 0; i < w.Length; i++) w[i] = w[i] * 100f / sum;
            return w;
        }

        /// <summary>⑩ 단장 성향 - 구단 코드 해시로 성향 3종 · 편차 폭 3~5를 정한다.</summary>
        public static (string label, int swing) Personality(string teamCode)
        {
            int h = GMFrontOffice.Hash($"{teamCode}_gm_personality");
            return (PersonalityLabels[h % PersonalityLabels.Length], 3 + (h / 7) % 3);
        }

        /// <summary>좌완 추정(투타 데이터 없음) - RealPlayerId 해시 약 30%.</summary>
        public static bool IsLeftHandedEstimate(Player p) => p?.Template != null && GMFrontOffice.Hash($"{p.Template.RealPlayerId}_throws") % 100 < 30;

        public static bool IsForeign(Player p) => p?.Template != null && StoveLeagueRules.IsForeignName(string.IsNullOrEmpty(p.Template.RealName) ? p.Template.PlayerName : p.Template.RealName);

        /// <summary>⑧ 자동 보호 사유(없으면 null) - 당해 신인 · 외국인 · 올해 FA 계약 당사자.</summary>
        public static string AutoProtectReason(GMLeagueState league, Player p)
        {
            if (p == null) return null;
            if (league != null && league.RookiesThisYear.Contains(p.InstanceId)) return "당해 신인";
            if (league != null && league.FASignedThisYear.Contains(p.InstanceId)) return "FA 계약 당사자";
            if (IsForeign(p)) return "외국인 선수";
            return null;
        }

        private static float Clamp(float v) => Math.Max(0f, Math.Min(100f, v));

        private static string Group(Player p)
        {
            if (p.IsPitcher) return p.Template.PitcherRole == PitcherRole.StartingPitcher ? "SP" : "RP";
            return p.Position;
        }

        private static bool IsCenterLine(Player p) => !p.IsPitcher && (p.Position == "C" || p.Position == "SS" || p.Position == "2B" || p.Position == "CF");

        /// <summary>한 선수의 ①~⑤ · ⑦ · ⑩ 점수(pool = 그 구단 보류선수 전체).</summary>
        public static GMProtectionEntry ScorePlayer(GMLeagueState league, string teamCode, IReadOnlyList<Player> pool, Player p, bool winNow, int swing)
        {
            var e = new GMProtectionEntry { Player = p };
            if (p?.Template == null) return e;
            string g = Group(p);
            int ovr = p.BaseOverall;
            var same = pool.Where(x => x != p && x.Template != null && Group(x) == g).ToList();
            int alternatives = same.Count(x => x.BaseOverall >= ovr - 4);
            bool bestAtGroup = same.All(x => x.BaseOverall <= ovr);
            // ① 대체 불가능성: 비슷한 수준(OVR -4 이내) 대체 자원이 적을수록, 그 자리 최고 선수일수록 높다
            e.Irreplaceability = Clamp(100f / (1 + alternatives) + (bestAtGroup ? 20f : 0f) + (IsCenterLine(p) ? 8f : 0f));
            // ② 잠재력: 25세 이하 · 잠재력 85 이상 = 100
            int pot = p.Potential;
            e.Potential = p.Age <= 25 && pot >= 85 ? 100f : Clamp((pot - 55) / 35f * 100f * (p.Age <= 25 ? 0.9f : p.Age <= 28 ? 0.55f : 0.25f));
            // ③ 현재 OVR
            e.Overall = Clamp((ovr - 45) / 45f * 100f);
            // ④ 팀 역할 · 워크에식: 주장 · 더그아웃 리더 · 클러치 에이스(구단 OVR 상위 3 · 1선발 · 마무리)
            float role = p.RoleArchetype == LockerRoomRole.DugoutLeader ? 45f : p.RoleArchetype == LockerRoomRole.UnsungHero ? 25f : p.RoleArchetype == LockerRoomRole.AlphaDog ? 30f
                : p.RoleArchetype == LockerRoomRole.Ambitious ? 15f : 10f;
            if (p.IsCaptain) role += 40f;
            bool topThree = pool.Where(x => x.Template != null).OrderByDescending(x => x.BaseOverall).Take(3).Contains(p);
            bool ace = p.IsPitcher && g == "SP" && bestAtGroup;
            bool closer = p.IsPitcher && p.Template.PitcherRole == PitcherRole.Closer && pool.Where(x => x.IsPitcher && x.Template.PitcherRole == PitcherRole.Closer).All(x => x.BaseOverall <= ovr);
            if (topThree || ace || closer) role += 30f;
            e.RoleEthic = Clamp(role);
            // ⑤ 계약 효율성: WAR 1당 연봉(시즌 기록이 충분하면 실제 WAR, 아니면 OVR 추정) - 고액 저효율 에이징커브는 감점
            float war = Math.Max(0.2f, (ovr - 50) / 8f);
            if (league != null && league.Stats.TryGetValue(p.InstanceId, out var st) && (st.PA >= 150 || st.OutsPitched >= 150)) war = Math.Max(0.2f, st.WAR);
            float costPerWar = p.Salary / war;
            float eff = 100f * (1f - costPerWar / 40000f);
            if (p.Age >= 33 && p.Salary >= 50000 && ovr < 80) eff -= 30f;
            e.ContractEfficiency = Clamp(eff);

            var w = WeightsFor(winNow);
            e.Weighted = (e.Irreplaceability * w[0] + e.Potential * w[1] + e.Overall * w[2] + e.RoleEthic * w[3] + e.ContractEfficiency * w[4]) / 100f;

            // ⑦ 희소성: 팀 내 유일한 포수 · 1선발 · 마무리 · 좌완 불펜(상위 2명)
            var tags = new List<string>();
            float scarcity = 0f;
            if (!p.IsPitcher && p.Position == "C" && pool.Count(x => !x.IsPitcher && x.Position == "C") == 1) { scarcity += ScarcityBonus; tags.Add("유일 포수"); }
            if (ace) { scarcity += ScarcityBonus; tags.Add("1선발"); }
            if (closer) { scarcity += ScarcityBonus; tags.Add("마무리"); }
            if (p.IsPitcher && g == "RP" && IsLeftHandedEstimate(p) && pool.Where(x => x.IsPitcher && Group(x) == "RP" && IsLeftHandedEstimate(x)).OrderByDescending(x => x.BaseOverall).Take(2).Contains(p))
            { scarcity += ScarcityBonus; tags.Add("좌완 불펜"); }
            e.Scarcity = Math.Min(ScarcityCap, scarcity);
            // ⑩ 단장 성향 편차(±swing, 선수 · 구단 · 연도 해시)
            int h = GMFrontOffice.Hash($"{teamCode}_{p.Template.RealPlayerId}_{league?.SeasonYear ?? 0}_protect");
            e.Personality = swing == 0 ? 0f : (h % (swing * 2 + 1)) - swing;
            e.Score = e.Weighted + e.Scarcity + e.Personality;
            e.Tags = string.Join(" · ", tags);
            return e;
        }

        /// <summary>
        /// 보호 명단 산출 - 보류선수 전체(1군 + 퓨처스)를 10대 가중치로 점수화 → ⑧ 자동 보호 분리 → 상위 size명 → ⑨ 최소 전력 검수 교체.
        /// manualIds가 있으면(내 구단 수동 명단) 그 선수들을 먼저 보호하고 남는 자리를 점수순으로 채운다.
        /// </summary>
        public static GMProtectionList BuildProtectionList(GMLeagueState league, GMTeamState team, int size, IEnumerable<string> manualIds = null)
        {
            var list = new GMProtectionList { TeamCode = team?.TeamCode ?? "", Size = size };
            if (team == null) return list;
            var pool = team.ReservePlayers.Where(p => p?.Template != null).ToList();
            list.WinNow = IsWinNow(league, team);
            var (label, swing) = Personality(team.TeamCode);
            list.PersonalityLabel = label;
            list.PersonalitySwing = swing;
            list.Weights = WeightsFor(list.WinNow);
            foreach (var p in pool)
            {
                var e = ScorePlayer(league, team.TeamCode, pool, p, list.WinNow, swing);
                string auto = AutoProtectReason(league, p);
                if (auto != null) { e.AutoProtected = true; e.Tags = string.IsNullOrEmpty(e.Tags) ? auto : $"{auto} · {e.Tags}"; }
                list.Entries.Add(e);
            }
            list.Entries.Sort((a, b) => b.Score.CompareTo(a.Score));
            var candidates = list.Entries.Where(e => !e.AutoProtected).ToList();
            var manual = new HashSet<string>(manualIds ?? Enumerable.Empty<string>());
            foreach (var e in candidates.Where(e => manual.Contains(e.Player.InstanceId)).Take(size)) e.Protected = true;
            foreach (var e in candidates.Where(e => !e.Protected)) { if (candidates.Count(x => x.Protected) >= size) break; e.Protected = true; }
            if (manual.Count == 0) EnforceBalance(list, candidates, size);
            return list;
        }

        /// <summary>⑨ 최소 전력 검수 - 보호 인원 중 포수 1 · 선발 4(25인 5) · 불펜 3(4) · 타자 8(10) · 유망주(23세 이하) 2(3)를 맞춘다.</summary>
        private static void EnforceBalance(GMProtectionList list, List<GMProtectionEntry> candidates, int size)
        {
            bool big = size >= ProtectB;
            var rules = new (string label, Func<Player, bool> f, int min)[]
            {
                ("포수", p => !p.IsPitcher && p.Position == "C", 1),
                ("선발", p => p.IsPitcher && p.Template.PitcherRole == PitcherRole.StartingPitcher, big ? 5 : 4),
                ("불펜", p => p.IsPitcher && p.Template.PitcherRole != PitcherRole.StartingPitcher, big ? 4 : 3),
                ("타선", p => !p.IsPitcher, big ? 10 : 8),
                ("유망주", p => p.Age <= 23, big ? 3 : 2),
            };
            for (int guard = 0; guard < 40; guard++)
            {
                bool changed = false;
                foreach (var (label, f, min) in rules)
                {
                    int have = candidates.Count(e => e.Protected && f(e.Player));
                    if (have >= min) continue;
                    var add = candidates.Where(e => !e.Protected && f(e.Player)).OrderByDescending(e => e.Score).FirstOrDefault();
                    if (add == null) continue;
                    // 빼도 다른 기준이 깨지지 않는 최저 점수 보호 선수와 교체
                    var drop = candidates.Where(e => e.Protected && !f(e.Player) && rules.All(r => !r.f(e.Player) || candidates.Count(x => x.Protected && r.f(x.Player)) > r.min))
                        .OrderBy(e => e.Score).FirstOrDefault();
                    if (drop == null) continue;
                    drop.Protected = false;
                    add.Protected = true;
                    list.BalanceNotes.Add($"⑨ {label} 최소 {min}명 - {add.Player.Template.PlayerName}({add.Score:0.0}) 보호 ↔ {drop.Player.Template.PlayerName}({drop.Score:0.0}) 해제");
                    changed = true;
                }
                if (!changed) break;
            }
        }

        // ================================================================== 정산

        /// <summary>보상 측 AI의 지명 점수 - 받는 구단 보류선수 기준으로 그 선수를 평가한다(받는 구단 니즈 반영).</summary>
        private static float PickScore(GMLeagueState league, GMTeamState receiver, Player p)
        {
            var pool = receiver.ReservePlayers.Where(x => x?.Template != null).ToList();
            pool.Add(p);
            return ScorePlayer(league, receiver.TeamCode, pool, p, IsWinNow(league, receiver), 0).Score;
        }

        /// <summary>
        /// 정산 1건 - 영입 구단(ToTeam)이 보호 명단을 제출하고 원 소속(FromTeam)이 명단 밖 1명 + 현금 또는 현금만 받는다.
        /// pick: 사람(내 구단이 보상 수령 측)이 고른 선수(null이면 현금만). AI가 수령 측이면 PickScore 최고 선수가 AiPickPlayerMinScore 이상일 때 지명.
        /// 내 구단이 영입 측이면 단장 수동 보호 명단(UserProtectedIds)을 쓴다(비었으면 10대 가중치 추천 명단).
        /// </summary>
        public static GMCompensationResult Settle(GMLeagueState league, GMPendingCompensation pending, Player pick = null, bool humanChooses = false)
        {
            var r = new GMCompensationResult();
            if (league == null || pending == null || !league.Teams.TryGetValue(pending.FromTeam, out var receiver) || !league.Teams.TryGetValue(pending.ToTeam, out var payer))
            { r.Message = "정산할 보상이 없습니다."; return r; }
            Player chosen = null;
            if (RequiresPlayer(pending.Grade))
            {
                var protection = BuildProtectionList(league, payer, ProtectionSize(pending.Grade), payer.IsUserTeam ? league.UserProtectedIds : null);
                var open = protection.Unprotected.Select(e => e.Player).ToList();
                if (humanChooses) chosen = pick != null && open.Contains(pick) ? pick : null;
                else
                {
                    var best = open.Select(p => (p, s: PickScore(league, receiver, p))).OrderByDescending(x => x.s).FirstOrDefault();
                    if (best.p != null && best.s >= AiPickPlayerMinScore) chosen = best.p;
                }
                if (humanChooses && pick != null && chosen == null) { r.Message = $"{pick.Template.PlayerName}은(는) 보호 명단(또는 자동 보호) 선수라 지명할 수 없습니다."; return r; }
            }
            r.CashOnly = chosen == null;
            r.Cash = chosen != null ? CashWithPlayer(pending.Grade, pending.PrevSalary) : CashOnly(pending.Grade, pending.PrevSalary);
            payer.Budget -= r.Cash;
            receiver.Budget += r.Cash;
            if (chosen != null)
            {
                if (payer.Futures.Contains(chosen)) payer.Futures.Remove(chosen);
                else
                {
                    payer.Roster.Remove(chosen);
                    string id = chosen.InstanceId;
                    payer.Lineup.Starters.RemoveAll(x => x.InstanceId == id);
                    payer.Lineup.Roles.RemoveAll(x => x.InstanceId == id);
                    payer.Lineup.PitcherSlots.RemoveAll(x => x.InstanceId == id);
                    GMCheerleaderRoster.RefreshDedications(payer);
                }
                chosen.IsCaptain = false;
                if (receiver.Roster.Count < GMRosterTiers.FirstTeamMax) receiver.Roster.Add(chosen); else receiver.Futures.Add(chosen);
                GMRosterTiers.EnforceLimits(league, receiver);
                r.CompensationPlayer = chosen;
            }
            league.PendingCompensations.Remove(pending);
            r.Success = true;
            string from = CompyaShort(pending.FromTeam), to = CompyaShort(pending.ToTeam);
            r.Message = chosen != null
                ? $"FA {pending.PlayerName}({GradeLabel(pending.Grade)}) 보상 - {from}이(가) {to}의 보호 명단 밖 {chosen.Template.PlayerName}({GMFrontOffice.PositionLabel(chosen.Position)} · OVR {chosen.BaseOverall})과 현금 {GMDiagnosticFormat.Won(r.Cash)}을 받았습니다."
                : $"FA {pending.PlayerName}({GradeLabel(pending.Grade)}) 보상 - {from}이(가) {to}로부터 현금 {GMDiagnosticFormat.Won(r.Cash)}을 받았습니다(보상선수 없음).";
            league.AddNews(new GMNewsItem
            {
                GameIndex = league.GamesPlayed, DateLabel = $"{league.SeasonYear} 스토브리그", Kind = GMNewsKind.Trade,
                Title = $"FA {pending.PlayerName} 보상 확정", Body = r.Message, IsUserTeam = receiver.IsUserTeam || payer.IsUserTeam,
            });
            return r;
        }

        /// <summary>내 구단이 영입 측인 정산 대기를 모두 처리한다(AI가 보상 선택). 정규시즌 시작 전 [진행하기]에서 부른다.</summary>
        public static List<GMCompensationResult> SettleUserSignings(GMLeagueState league)
        {
            var results = new List<GMCompensationResult>();
            if (league == null) return results;
            foreach (var pending in league.PendingCompensations.Where(c => c.ToTeam == league.SelectedTeamCode).ToList())
                results.Add(Settle(league, pending));
            return results;
        }

        /// <summary>
        /// AI 구단이 FA를 영입했을 때(원 소속이 내 구단이면 내가 보상선수를 고른다) - FA 시장에서 AI 구단 1군(가득이면 퓨처스)으로 옮기고 정산 대기를 만든다.
        /// </summary>
        public static GMPendingCompensation AiSignFreeAgent(GMLeagueState league, GMTeamState aiTeam, Player fa, int years = 3)
        {
            if (league == null || aiTeam == null || fa == null || !league.FreeAgents.Contains(fa)) return null;
            league.FreeAgents.Remove(fa);
            fa.ContractYears = Math.Max(1, Math.Min(GMStoveLeagueMarket.MaxFAYears, years));
            if (aiTeam.Roster.Count < GMRosterTiers.FirstTeamMax) aiTeam.Roster.Add(fa); else aiTeam.Futures.Add(fa);
            GMRosterTiers.EnforceLimits(league, aiTeam);
            return OnFreeAgentSigned(league, aiTeam, fa);
        }
    }
}
