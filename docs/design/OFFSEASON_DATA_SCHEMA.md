# Offseason Data Schema

스토브리그 데이터의 **논리 스키마**(YAML 표기)다. 실제 C# 클래스 이름 · 위치는 사전 조사 보고서 7절의 매핑을 따르며,
새 클래스를 무조건 만들지 않고 기존 구조(`Player`, `GMTeamState`, `GMLeagueState`, `GMFrontOfficeState`, `GMLeagueSaveData`)를 확장하는 것을 우선한다.

## 0. 공통 규칙

- **원화 금액은 만 원 단위 정수**(DECISION-007 제안, 기존 코드 규칙과 동일). 예: 143억 9,723만 원 = `1439723`.
- **달러 금액은 USD 정수** + 시즌 환율 상수로 환산(외국인 상한 · 포스팅 이적료).
- 반올림 정책: 원문 미기재 → `GAME_DEFINED`(만 원 미만 반올림).
- 공식 규정 값은 코드 상수가 아니라 **연도별 규정 테이블**(아래 1절)에서 읽는다 - 2027 · 2028 값이 바뀌기 때문이다.

## 1. 연도별 규정 테이블 (공식 값 · 데이터)

```yaml
league_rules:
  season: 2026
  source_rule_ids: [KBO-OFFSEASON-001, 002, 003, 008, 009, 013]
  roster:
    team_max_players: 68          # 보류선수 포함(2026 개정)
    reserve_list_max: 63          # 군보류 · 육성보류 제외
    active_roster: 29             # 9/1 이후 34
    active_roster_september: 34
    game_eligible: 27             # 9/1 이후 32
    postseason_registered: 30
    postseason_eligible: 28
  salary:
    min_salary_manwon: 3000       # 2027~ 3300
  competitive_balance_tax:
    cap_manwon: 1439723           # 2027: 1511709, 2028: 1587294
    floor_manwon: null            # 2027부터 적용, 금액 미공시
    top_n: 40
    exception_player_ratio: 0.5
    exception_min_seasons: 7
    penalty_ratio_by_streak: [0.30, 0.50, 1.00]
    third_streak_first_round_drop: 9
  free_agency:
    service_seasons: 8
    service_seasons_college4: 7
    service_days_per_season: 145
    regain_seasons: 4
    grade_a: { team_rank: [1, 3], league_rank: [1, 30], protect: 20, cash_with_player: 2.0, cash_only: 3.0 }
    grade_b: { team_rank: [4, 10], league_rank: [31, 60], protect: 25, cash_with_player: 1.0, cash_only: 2.0 }
    grade_c: { team_rank: [11, null], league_rank: [61, null], cash_only: 1.5 }
    age35_cash_only: 1.5
    external_signing_limit_by_filers: [[10, 1], [20, 2], [30, 3], [999, 4]]
    unsigned_to_free_contract_years: 3
  secondary_draft:
    held_this_year: false         # 2027 true
    protect: 35
    rounds: 3
    bottom3_extra_picks: 2
    max_picks_from_one_team: 4
    transfer_fee_manwon: [40000, 30000, 20000, 10000]   # 1R, 2R, 3R, 4R 이하
    mandatory_days: [50, 30, 0]
  foreign:
    max_foreign: 3
    max_asia_quota: 1
    max_simultaneous_play: 3      # 아시아쿼터 보유 시 4
    new_player_cap_usd: 1000000
    total_cap_usd: 4000000
    renewal_cap_increment_usd: 100000
    asia_cap_usd: 200000
  rookie_draft:
    rounds: 11
    college_min_picks: 1
  trade:
    window: { open: POSTSEASON_END_PLUS_1, close: "07-31" }
    max_draft_picks_in_trade: 2
    moving_allowance_manwon_each_team: 100
  posting:
    min_seasons: 7
    per_team_per_year: 1
    window: { open: "11-01", close: "12-05" }
    fee_brackets_usd: [[25000000, 0.20], [50000000, 0.175], [null, 0.15]]
```

## 2. 구단 (기존 `GMTeamState` 확장)

```yaml
team:
  team_id: "SAM"                  # 기존 TeamCode
  budget_manwon: 0                # 기존 Budget
  payroll_cbt_manwon: 0           # 신규 - 상위 40명 경쟁균형세 기준 금액(외국인 · 신인 제외, 예외 선수 50%)
  cbt_exception_player_id: ""     # 신규
  cbt_consecutive_over: 0         # 신규 - 연속 초과 횟수
  cbt_consecutive_under: 0        # 신규 - 2027~ 하한 미달 횟수
  foreign_slots: { foreign_used: 0, asia_used: 0, foreign_spent_usd: 0, extra_registrations_used: 0 }
  external_fa_signed_this_year: 0 # 기존 GMFrontOfficeState.FASigningsThisYear와 통합 검토(하우스 룰과 규정 한도 분리)
  draft_picks:                    # 신규 - 트레이드 가능한 지명권
    - { season: 2027, round: 1, original_team: "SAM", owner_team: "SAM", position_drop: 0 }
```

## 3. 계약 (현재 `Player.Salary` · `Player.ContractYears` 두 필드만 존재 → 분리 제안)

```yaml
contract:
  contract_id: ""
  player_id: ""                   # Player.InstanceId
  team_id: ""
  type: "ROOKIE | RESERVE | FA | NON_FA_MULTIYEAR | FOREIGN | ASIA_QUOTA | DEVELOPMENT"
  start_year: 2026
  end_year: 2026
  annual_salary_manwon: 0
  signing_bonus_total_manwon: 0   # 2회 분할 지급(제81조 ③)
  options_manwon: 0               # 실지급액은 시즌 종료 후 확정
  amount_usd: 0                   # 외국인 · 아시아쿼터
  is_guaranteed: true
  cbt_annual_hit_manwon: 0        # (총 연봉 + 계약금 총액) ÷ 연수 + 옵션
  no_trade_until: null            # 외부 FA · 2차 드래프트 · 지명 신인 1년 양도 금지 만료일
```

## 4. 선수 서비스 · 신상 (FA · 2차 드래프트 · 외국인 판정용, 현재 없음)

```yaml
player_career:
  player_id: ""
  birth_year: 0                   # 만 35세 FA 특례(출생연도 기준)
  nationality: "KOR"              # 외국인 · 아시아쿼터 판정
  is_college4_graduate: false     # FA 7시즌 단축
  first_registered_year: 0        # 2006 이후면 등록일수 기준만 적용
  pro_entry_year: 0               # 2차 드래프트 1~3년차 자동 제외
  service_seasons: 0              # 인정된 정규시즌 수
  service_days_current: 0         # 이번 시즌 등록일수(부상자 명단 포함)
  fa_count: 0                     # 0 신규 · 1 재자격 · 2+ 세 번째 이상
  fa_grade_initial: null          # 신규 FA 때 등급(C면 재자격도 C 기준)
  salary_history_manwon: { 2024: 0, 2025: 0, 2026: 0 }   # FA 등급 산정(최근 3년 평균)
  affiliation: "UNDER_CONTRACT"   # PLAYER_TRANSACTION_STATE.md 1절
  roster_status: "ACTIVE_ROSTER"
  transaction_status: "NONE"
  eligibility: "ELIGIBLE"
  restrictions: [ { kind: "NO_TRADE_FA", until_year: 2027 } ]
```

## 5. 거래 이력 (현재는 `GMNewsItem` 뉴스 문장만 남음 → 구조화 제안)

```yaml
transaction_history:
  transaction_id: ""
  type: "FA_SIGNING | FA_COMPENSATION | TRADE | SECONDARY_DRAFT | ROOKIE_DRAFT | POSTING | RELEASE | WAIVER | FOREIGN_SIGNING | RESERVE_LIST | ARBITRATION"
  season: 2026
  turn: 2
  player_ids: []
  team_before: ""
  team_after: ""
  contract_before: {}
  contract_after: {}
  finance_delta_manwon: { "SAM": 0, "LG": 0 }
  draft_picks_moved: []
  rule_ids: ["KBO-OFFSEASON-001"]
  note: ""                        # 화면 표기 문장(뉴스와 공유)
```
