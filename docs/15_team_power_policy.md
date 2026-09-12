---
문서명: 구단 전력 보정 정책 (Team Power Policy)
버전: v0.1
상태: Active
최종 수정일: 2026-09-12
담당자: 김장윤
관련 파일: GameManager.cs, MatchEngine.cs, LeagueManager.cs
---

# 1. 목적

- 구단 전력을 "선수 스탯 순수 합"과 "표시용 지표"와 "실제 경기 판정에 쓰이는 값"으로 명확히 분리해, 세 층위가 서로 다른 목적을 가진 별개의 숫자임을 명시한다.
- 과거 3갈래로 파편화되어 있던 세트덱 판정(MatchEngine 5명/배율 1.15배, GameManager.CheckSetDeckBonus 5명/배율 1.15배, GameManager.CalculateTeamSynergy 15명/+12 유저 전용)을 단일 기준으로 통폐합한다.

# 2. 최종 팀 전력 3개 지표

| 지표 | 정의 | 구현 위치 |
| :--- | :--- | :--- |
| ① 로스터 OVR | 주전 15인 평균 OVR × 0.8 + 후보 10인 평균 OVR × 0.2 (순수 선수 스탯 합, 반올림) | `GameManager.CalculateTeamOVR()` 내부 계산(시너지 가산 전 단계) |
| ② 표시 팀 OVR | ① + 상시 시너지(세트덱 등) | `GameManager.CalculateTeamOVR()`의 반환값 |
| ③ 경기 적용 전력 | ② + 조건부 버프(홈 어드밴티지, 치어리더 등 - 경기마다 달라질 수 있음) | `MatchEngine`에 주입되는 `TeamPowerModifiers`(`SynergyBuff` + `ConditionBuff` = `TotalBuff`) |

- ①과 ②는 `GameManager.CalculateTeamOVR()` 하나가 순서대로 계산해 반환한다(주전/후보 평균 반올림 → 시너지 가산).
- ③은 `GameManager.CalculateTeamOVR()`과는 별개로, 경기를 시작하는 호출부(`LeagueManager`, `PlayBallController`, `PostSeasonManager`)가 매 경기 직전에 `TeamPowerModifiers`를 구성해 `MatchEngine` 생성자로 주입한다. `MatchEngine`은 이 값을 그대로 세부 스탯에 가산할 뿐, 스스로 계산하지 않는다.

# 3. 세트덱 통폐합 정책

- **단일 기준**: 28인 로스터 중 특정 구단 소속 선수가 **15명 이상**이면 **+12**를 가산한다. 15명 미만이면 0.
- **판정 기준 구단**:
  - 유저 로스터: `GameManager.Instance.FavoriteTeam`(선호 구단)과 일치하는 인원을 센다. `FavoriteTeam`이 `Team.None`(온보딩 이전 등 미지정 상태)이면 최다 구단 기준으로 대체한다(아래 AI 규칙과 동일).
  - AI 로스터: `FavoriteTeam` 개념이 없으므로, 로스터 내 가장 많은 비중을 차지하는 구단의 인원수를 기준으로 삼는다.
  - 두 경로 모두 `Team.None`(구단 미지정) 선수는 집계에서 제외한다.
- **구현**: `GameManager.CalculateSynergy(List<Player> roster, string favoriteTeam = null)` 정적 유틸리티 하나로 통합했다. `favoriteTeam`을 지정하면 그 구단 기준, 생략(null)하면 최다 구단 기준.
- **폐기된 과거 공식**:
  - `MatchEngine.EvaluateSetDeckBonus()` — 로스터 내 최다 구단 5명 이상이면 세부 스탯에 **배율 1.15배**를 곱하던 로직. 실제 경기 판정에 반영되고 있었으나 **완전히 삭제**하고 아래 4절의 가산 방식으로 대체했다.
  - `GameManager.CalculateTeamSynergy()`(TASK-KBO-034, private, 유저 전용) — `CalculateSynergy()`로 대체되어 삭제됨.
  - `GameManager.CheckSetDeckBonus()`(5명 기준, 배율 1.15배)는 **삭제하지 않고 보존**했다 — `RosterUIController`가 로스터 화면의 게이지/텍스트 표시에 실제로 사용 중이며, 이 UI는 실제 경기 판정과 무관한 순수 표시 용도라 이번 정책 통합의 계산 충돌 대상이 아니다. 다만 이 메서드가 여전히 구식(5명/배율) 기준을 쓴다는 점은 알려진 불일치이며, UI 쪽을 15명/+12 기준으로 갱신하는 작업은 별도 후속 작업으로 남아 있다.

# 4. 경기 적용 전력 분배 원칙

- 팀의 총합 버프(`TeamPowerModifiers.TotalBuff = SynergyBuff + ConditionBuff`)가 **+N**이면, `MatchEngine`은 그 팀 소속 선수의 **세부 스탯 6개 항목 각각에 균등하게 +N을 가산**한다.
  - 타자: Power, Contact, Discipline
  - 투수: Stuff, Velocity, Movement, Control
- 가산은 **엔진 내부 런타임 계산에서만** 일어난다(`ResolveEffectiveBatterStats`/`ResolveEffectivePitcherStats`의 마지막 단계). `Player` 객체의 저장된 스탯(`ReinforceLevel`, `AwakenLevel` 등)이나 CalculateOVR() 등 다른 경로의 계산에는 전혀 영향을 주지 않는다 - 매 타석 계산 시 임시로 만들어지는 지역 값에만 더해진다.
- 가산 순서: Base+Growth → 스킬 효과(강화/각성/스킬) → 팀 버프(맨 마지막). 세트덱이 배율에서 가산으로 바뀌었으므로 더 이상 "배율이 스킬 보너스까지 부풀리는" 복리 문제가 없다.
- **방어적 클램핑**: 버프가 음수(향후 페널티 도입 시)여도 최종 세부 스탯 값이 1 미만으로 떨어지지 않도록 `Mathf.Max(1, stat + buff)`로 하한을 둔다.
- **[TASK-KBO-039] ConditionBuff 기본값**: 매치 생성 시 홈팀 `ConditionBuff`에 `TeamPowerModifiers.HomeAdvantageConditionBuff`(**+2**)가 기본 부여된다(원정팀은 0). 치어리더로 인한 추가 `ConditionBuff` 가산은 실제 치어리더 데이터 시스템이 아직 없어 미구현 상태다(후속 작업 범위).

# 5. 로지스틱 승률 공식과의 관계

- `06_match_engine_formula.md`가 언급하는 로지스틱 승률 공식(`1/(1+10^(-(타자OVR-투수OVR)/30))`)은 `KBOManager.Broadcast`(CSV/Z-score 기반 병행 시스템) 쪽 스펙이며, 실제로 모든 경기에 쓰이는 `KBOManager.Engine.MatchEngine`은 OVR이 아닌 세부 스탯 매치업 차이 기반의 가중 룰렛 방식을 쓴다(TASK-KBO-036 조사에서 확인). 본 문서의 "경기 적용 전력"(③) 가산도 이 세부 스탯 매치업 계산에 반영되며, 로지스틱 공식 자체와는 무관하다.

# 6. B+C 하이브리드 - 클러치(Clutch) 배율 [TASK-KBO-039]

- `TeamPowerModifiers`에 `SynergyBuff`/`ConditionBuff`(B안 - 세부 스탯 가산, 4절)와 별개로 **`ClutchMultiplier`**(C안 - 확률 가중치 배율, `float`, 기본값 **1.0f** = 효과 없음)를 추가했다. `TotalBuff`(`SynergyBuff + ConditionBuff`) 계산식에는 포함되지 않는다 - 가산 채널과 배율 채널은 완전히 분리되어 있다.
- **득점권 판정**: `MatchState.HasRunnerInScoringPosition`(2루 또는 3루에 주자가 있는지)을 그대로 쓴다.
- **적용 대상**: 득점권 상황에서 **타석에 들어선 타자가 속한 팀**의 `ClutchMultiplier`만 사용한다. `MatchEngine.OutcomeTable`의 8개 결과 후보 중 타자에게 유리한 5개(볼넷, 안타, 2루타, 3루타, 홈런)의 가중치(`weight`)에만 곱해진다 - 삼진/땅볼/뜬공(타자에게 불리한 3개)에는 곱해지지 않는다. 수비 팀(투수)의 위기 탈출 배율은 이번 스코프에 포함되지 않는다.
- **개입 지점**: 이미 계산된 `weight`(기존 로지스틱/랜덤 판정 로직으로 산출된 값)에 `weight *= ClutchMultiplier`로 단순 곱셈만 한다 - `SimulateAtBat()`의 확률 계산 근간(스탯 매치업 diff, 정규화, 랜덤 룰렛) 자체는 전혀 바뀌지 않았다.
- **안전장치**: `ClutchMultiplier > 1f`일 때만 곱셈을 수행하므로, 값이 실수로 0에 가깝게 설정되더라도 안타 확률이 증발하는 버그가 발생하지 않는다. 기본값(1.0f)은 생성자 기본 파라미터와 `TeamPowerModifiers.None` 양쪽에서 보장된다.
- 치어리더 스킬로 인한 실제 `ClutchMultiplier` 값 산출(1.0f보다 큰 값을 실제로 채워 넣는 로직)은 치어리더 데이터 시스템이 아직 없어 미구현 상태다(후속 작업 범위) - 현재 모든 호출부가 기본값(1.0f)만 전달한다.
