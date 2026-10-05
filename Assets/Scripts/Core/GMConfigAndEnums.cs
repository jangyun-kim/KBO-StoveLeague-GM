using System;
using System.Collections.Generic;
using System.Linq;
using KBOManager.Models;

namespace KBOManager.Core
{
    /// <summary>
    /// [TASK-GM-01] 『스토브리그: 단장의 시간』 기능 플래그. 선수 수집형 RPG 요소(강화 · 각성 · 초월 · 세트덱 · 선수 가챠)는
    /// 코드를 지우지 않고 이 상수로 떼어 둔다(UI 진입점 숨김 + 능력치 계산 제외). 치어리더 시스템은 기획서 3절에 따라 축소하지 않는다.
    /// </summary>
    public static class GMFeatureFlags
    {
        // 선수 수집형 RPG 요소 전면 비활성화
        public const bool ENABLE_PLAYER_CARD_ENHANCE = false;
        public const bool ENABLE_PLAYER_CARD_AWAKENING = false;
        public const bool ENABLE_PLAYER_CARD_TRANSCEND = false;
        public const bool ENABLE_SET_DECK_200P = false;
        public const bool ENABLE_PLAYER_GACHA_SCOUT = false;

        // 치어리더 시스템은 축소 없이 핵심 축으로 유지 (구단 ~15명, 경기 엔트리 4~6명)
        public const bool ENABLE_CHEERLEADER_CORE_SYSTEM = true;
        public const int CHEERLEADER_TEAM_ROSTER_MAX = 15;
        public const int CHEERLEADER_MATCH_ENTRY_MIN = 4;
        public const int CHEERLEADER_MATCH_ENTRY_MAX = 6;
        public const int CHEERLEADER_MATCH_ENTRY_DEFAULT = 5;

        // 기본 시작 연도 (2026년 부임 기준)
        public const int DEFAULT_START_YEAR = 2026;

        // 상수를 if 문에 바로 쓰면 도달 불가 코드 경고(CS0162)가 나므로 호출부는 아래 속성으로 판정한다.
        /// <summary>선수 카드 성장(강화 · 각성 · 초월) 중 하나라도 켜져 있는지.</summary>
        public static bool IsCardGrowthEnabled => ENABLE_PLAYER_CARD_ENHANCE || ENABLE_PLAYER_CARD_AWAKENING || ENABLE_PLAYER_CARD_TRANSCEND;
        public static bool IsSetDeckEnabled => ENABLE_SET_DECK_200P;
        public static bool IsPlayerGachaEnabled => ENABLE_PLAYER_GACHA_SCOUT;
        public static bool IsCheerleaderCoreEnabled => ENABLE_CHEERLEADER_CORE_SYSTEM;
    }

    /// <summary>
    /// [TASK-GM-01] 유저 설정. UseVirtualNames - false(개인 플레이 기본값) = 실명, true(외부 배포 · 기획서 1절) = 가상명.
    /// 실제 적용(PlayerTemplate.PlayerName 갱신)은 NameAliasTable.ApplyDisplayNames()가 맡는다.
    /// </summary>
    public static class GameSettings
    {
        public static bool UseVirtualNames { get; set; } = false;
    }

    public enum GMStartMode
    {
        RealCurrent2026 = 0,     // 모드 1: 2026 현역 스토브리그 모드
        AllTimeDream = 1,        // 모드 2: 1986~2026 올타임 드림 스토브리그 모드
        StoryCampaign = 2        // 모드 3: 스토리 캠페인 모드 (꼴찌 구단의 겨울)
    }

    public enum LockerRoomRole
    {
        AlphaDog = 0,            // 프랜차이즈 황제 (알파독): 1선발/3·4번 타자/최고연봉 요구, 동급 스타 견제
        Ambitious = 1,           // 야망가 (스포트라이트 추구형): 개인 타이틀/중심 보직 집착, 하위타선·불펜 강등 시 불만
        DugoutLeader = 2,        // 더그아웃 리더 (캡틴형): 알파독 갈등 중재, 팀워크 보너스 제공
        UnsungHero = 3,          // 헌신형 살림꾼 (언성 히어로): 번트·진루타·수비 백업·마당쇠 수용, 케미스트리 상승
        Prospect = 4             // 유망주 (멘티): 베테랑 멘토링 시 급성장, 출전 기회 박탈 시 성장 정체
    }

    public enum TeamMoraleState
    {
        Slump = 0,               // 침체 (승률 및 클러치 페널티)
        Normal = 1,              // 보통
        Boosted = 2              // 고무 (단기전 및 접전 포텐셜 상승)
    }

    [Flags]
    public enum AllStarOverloadPenalty
    {
        None = 0,
        AlphaDogFactionSplit = 1 << 0,     // ① 왕좌의 게임: Ego 5 알파독 3명 이상 + 리더 부재 -> 클러치 타율 -15%, 실책 2배
        LineupRoleConflict = 1 << 1,       // ② 타순·보직 자존심 충돌: 중심타선/1선발 밀려난 스타 태업 (컨디션 나쁨 고정)
        HeroBallDoublePlay = 1 << 2,       // ③ 개인 기록 탐욕(Hero Ball): 야망가 과밀 -> 팀배팅 거부, 득점권 병살·삼진 급증
        DefenseImbalance = 1 << 3,         // ④ 수비 기피 및 포지션 중복: 거포 편중, 센터라인(C/SS/CF) 수비 붕괴 -> 투수 ERA 폭등 및 투타 불화
        PayrollDepthCollapse = 1 << 4,     // ⑤ 샐러리캡 폭발 및 벤치·불펜 뎁스 붕괴: 주전 85% 연봉 편중 -> 부상/후반기 과부하 연패
        UnderdogUpsetVulnerability = 1 << 5 // ⑥ 스타 군단의 방심: 고전력 저팀워크(50 미만) -> 약팀 상대 업셋 패배 확률 증가
    }

    /// <summary>[TASK-GM-01] 시즌 진행 단계. 시상식까지 끝나면 다음 해 스토브리그로 넘어간다(GMLeagueState.AdvancePhase).</summary>
    public enum GMSeasonPhase
    {
        StoveLeague = 0,     // 스토브리그(로스터 · 계약)
        RegularSeason = 1,   // 정규시즌 144경기
        PostSeason = 2,      // 포스트시즌
        AwardsCeremony = 3,  // 시상식(기획서 1.1~1.7)
    }

    /// <summary>
    /// [TASK-GM-01] 치어리더 구단 풀(최대 15명) · 경기 엔트리(4~6명) 규칙. 엔트리는 기존 6인 역할 편성 슬롯(CheerSquad)을 그대로 쓰고,
    /// 채워진 슬롯 수가 4~6명이면 유효하다. 리더십 버프는 TeamChemistryEngine의 cheerleaderLeadershipBuff로 들어간다.
    /// </summary>
    public static class GMCheerleaderRules
    {
        public const int MaxLeadershipBuff = 10;

        public static bool IsValidEntryCount(int count) =>
            count >= GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN && count <= GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MAX;

        public static int ClampEntrySize(int size) =>
            Math.Max(GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN, Math.Min(GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MAX, size));

        public static int EntryCount(IEnumerable<Cheerleader> entry) => (entry ?? Enumerable.Empty<Cheerleader>()).Count(c => !CheerSquad.IsEmpty(c));

        /// <summary>엔트리 인원의 등급 단계(LIVE 1 / ICON 2 / LEGEND · 시즌 한정 3, ★각성 도약 포함) 합계, 상한 +10. 4명 미만이면 0(엔트리 미충족).</summary>
        public static int LeadershipBuff(IEnumerable<Cheerleader> entry)
        {
            var filled = (entry ?? Enumerable.Empty<Cheerleader>()).Where(c => !CheerSquad.IsEmpty(c)).ToList();
            if (filled.Count < GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN) return 0;
            return Math.Min(MaxLeadershipBuff, filled.Sum(CheerGrowth.EffectiveTier));
        }

        /// <summary>UI 요약 한 줄 - "엔트리 5/6명 (4~6명) · 구단 풀 12/15명".</summary>
        public static string Summary(int entryCount, int poolCount) =>
            $"엔트리 {entryCount}/{GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MAX}명 ({GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MIN}~{GMFeatureFlags.CHEERLEADER_MATCH_ENTRY_MAX}명) · " +
            $"구단 풀 {poolCount}/{GMFeatureFlags.CHEERLEADER_TEAM_ROSTER_MAX}명";
    }
}
