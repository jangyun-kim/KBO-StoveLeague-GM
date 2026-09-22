---
문서명: 구단 전력 보정 정책 (Team Power Policy)
버전: v0.1
상태: Active
최종 수정일: 2026-09-13
담당자: 김장윤
관련 파일: GameManager.cs, MatchEngine.cs, LeagueManager.cs, PlayBallController.cs, PostSeasonManager.cs, Cheerleader.cs, MatchRewardManager.cs
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
- **[TASK-KBO-166 신설] 왕조(DYNASTY) 세트덱 보너스**: 위 일반 세트덱과 별개로, 로스터 안의 `Grade == DYNASTY` 카드만 구단별로 묶어 **5명 이상**이면 **+15**를 추가로 가산한다(일반 세트덱과 스택 - 총 +27까지 가능). 왕조 로스터는 삼성 14명/해태(KIA) 15명뿐이라 일반 세트덱의 15명 기준을 왕조 카드만으로 채우는 것이 사실상 불가능해(삼성은 원천 불가, KIA도 전원 필요) 별도의 낮은 임계값을 뒀다. `favoriteTeam`과 무관하게 로스터 안에서 가장 많이 모인 왕조를 기준으로 판정한다(유저의 선호 구단이 왕조 소속이 아니어도 왕조 카드를 충분히 모으면 발동). 같은 선수의 서로 다른 연도 왕조 카드(예: 2011년 카드와 2013년 카드)를 섞어도 전부 동일한 `Team`으로 집계되므로(TASK-KBO-160이 카드별 실제 발급 구단을 정확히 고친 결과), "왕조는 시즌 연도 구분 없이 왕조 전체 기간을 아우르는 세트덱 점수를 준다"는 요구사항을 그대로 만족한다. 임계값(5)과 보너스(+15) 수치는 기획 확정값이 없어 이번에 직접 정했다 - 왕조 등급이 "최고 희소성"인 만큼 일반 세트덱(+12)보다 소폭 높게 잡았다.
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
- **[TASK-KBO-039] ConditionBuff 기본값**: 매치 생성 시 홈팀 `ConditionBuff`에 `TeamPowerModifiers.HomeAdvantageConditionBuff`(**+2**)가 기본 부여된다(원정팀은 0). **[TASK-KBO-048]** 유저 팀의 홈 경기라면 여기에 장착된 `Cheerleader.ConditionBuff`가 추가로 가산된다(7절 참고) - AI 팀이거나 유저 팀이라도 원정 경기면 가산되지 않는다.

# 5. 로지스틱 승률 공식과의 관계

- `06_match_engine_formula.md`가 언급하는 로지스틱 승률 공식(`1/(1+10^(-(타자OVR-투수OVR)/30))`)은 `KBOManager.Broadcast`(CSV/Z-score 기반 병행 시스템) 쪽 스펙이며, 실제로 모든 경기에 쓰이는 `KBOManager.Engine.MatchEngine`은 OVR이 아닌 세부 스탯 매치업 차이 기반의 가중 룰렛 방식을 쓴다(TASK-KBO-036 조사에서 확인). 본 문서의 "경기 적용 전력"(③) 가산도 이 세부 스탯 매치업 계산에 반영되며, 로지스틱 공식 자체와는 무관하다.

# 6. B+C 하이브리드 - 클러치(Clutch) 배율 [TASK-KBO-039]

- `TeamPowerModifiers`에 `SynergyBuff`/`ConditionBuff`(B안 - 세부 스탯 가산, 4절)와 별개로 **`ClutchMultiplier`**(C안 - 확률 가중치 배율, `float`, 기본값 **1.0f** = 효과 없음)를 추가했다. `TotalBuff`(`SynergyBuff + ConditionBuff`) 계산식에는 포함되지 않는다 - 가산 채널과 배율 채널은 완전히 분리되어 있다.
- **득점권 판정**: `MatchState.HasRunnerInScoringPosition`(2루 또는 3루에 주자가 있는지)을 그대로 쓴다.
- **적용 대상**: 득점권 상황에서 **타석에 들어선 타자가 속한 팀**의 `ClutchMultiplier`만 사용한다. `MatchEngine.OutcomeTable`의 8개 결과 후보 중 타자에게 유리한 5개(볼넷, 안타, 2루타, 3루타, 홈런)의 가중치(`weight`)에만 곱해진다 - 삼진/땅볼/뜬공(타자에게 불리한 3개)에는 곱해지지 않는다. 수비 팀(투수)의 위기 탈출 배율은 이번 스코프에 포함되지 않는다.
- **개입 지점**: 이미 계산된 `weight`(기존 로지스틱/랜덤 판정 로직으로 산출된 값)에 `weight *= ClutchMultiplier`로 단순 곱셈만 한다 - `SimulateAtBat()`의 확률 계산 근간(스탯 매치업 diff, 정규화, 랜덤 룰렛) 자체는 전혀 바뀌지 않았다.
- **안전장치**: `ClutchMultiplier > 1f`일 때만 곱셈을 수행하므로, 값이 실수로 0에 가깝게 설정되더라도 안타 확률이 증발하는 버그가 발생하지 않는다. 기본값(1.0f)은 생성자 기본 파라미터와 `TeamPowerModifiers.None` 양쪽에서 보장된다.
- **[TASK-KBO-048]** 치어리더 스킬로 인한 실제 `ClutchMultiplier` 값 산출이 구현되었다 - 유저 팀의 홈 경기이고 치어리더가 장착되어 있으면 `Cheerleader.ClutchMultiplier`가 전달되고, 그 외(AI 팀/원정/미장착)에는 기본값(1.0f)이 전달된다(7절 참고).

# 7. 치어리더 데이터 모델 및 경기 조건부 적용 전력(MatchConditionModifier) [TASK-KBO-048]

- **데이터 모델**: `Cheerleader`(`Assets/Scripts/Models/Cheerleader.cs`)가 `ConditionBuff`(int)와 `ClutchMultiplier`(float, 기본 1.0f)를 들고 있다. `CheerleaderGrade` enum(`NONE=0`, `TEST=1`)은 선수 카드 등급(`Grade` enum)과 완전히 분리된 별도 체계이며 혼용하지 않는다. **[v0.1 마감/TASK-KBO-062]** 등급 서열/개수는 여전히 `NONE`/`TEST` 2종뿐이고, 획득 방식(가챠)도 여전히 없다 - **v0.1 현재 상태를 유지하며, 폴리싱 및 밸런싱 단계인 v0.2에서 확정할 것**으로 공식 확정한다.
- **장착 슬롯 및 보유 인벤토리**: `GameManager.EquippedCheerleader`(단일 슬롯, `Cheerleader` 또는 `null`)와 `GameManager.OwnedCheerleaders`(`List<Cheerleader>`, 유저가 영구 보유한 전체 목록)가 모두 구현되었다(TASK-KBO-057). `EquipCheerleader()`/`UnequipCheerleader()`/`AddCheerleader()` 정식 API(TASK-KBO-056/057)와 세이브 연동(`SaveVersion` 5, TASK-KBO-057), 인벤토리 UI(`CheerleaderInventoryUIController`/`CheerleaderSlotUI`, TASK-KBO-058~061)까지 v0.1 범위에서 전부 완료되었다. **다만 가챠/획득 시스템 자체는 v0.1 범위 밖으로 남아 있어, 현재 유저가 치어리더를 얻을 수 있는 유일한 경로는 에디터 QA 메뉴(`KBO Manager/Debug/Add Dummy Cheerleader to Inventory`, TASK-KBO-059)뿐이다** - 정식 획득 경로(가챠 팩토리 등) 설계는 v0.2 과제로 이관한다.
- **"경기 조건부 적용 전력(MatchConditionModifier)" 정의**: 치어리더의 두 버프는 ①·②(로스터 OVR/표시 팀 OVR)에 절대 영구 반영되지 않는다 - 오직 경기 시작 직전 각 매니저의 `BuildTeamPowerModifiers()`류 헬퍼가 `GameManager.ResolveCheerleaderConditionBuff()`/`ResolveCheerleaderClutchMultiplier()`를 통해 `TeamPowerModifiers`에 일회성으로 실어 `MatchEngine`에 전달하는 값 - 즉 ③(경기 적용 전력) 층위에만 속한다.
- **적용 조건**: "유저 팀이면서 홈 경기"(`isUserTeamHome`)일 때만 합산된다. AI 팀이거나, 유저 팀이라도 원정 경기면 치어리더의 어떠한 버프도 적용되지 않는다. `LeagueManager`/`PlayBallController`/`PostSeasonManager` 3개 호출부 모두 동일한 규칙을 따른다.
  - **[v0.1 마감/TASK-KBO-062] 포스트시즌 중립 구장**: `PostSeasonManager`는 시리즈 내내 `HigherSeed`를 고정적으로 "home"에 배정하는 기존 설계를 그대로 따른다(중립 구장이나 시리즈 중 홈/원정 교대 개념 자체가 엔진에 없음) - 유저 팀이 `HigherSeed`인 시리즈 내내 치어리더 홈 버프가 계속 적용된다는 뜻이며, 실제 KBO 한국시리즈 룰(고정 홈/원정 교대)과 다를 수 있다. **v0.1 현재 상태(고정 HigherSeed=home)를 유지하며, 폴리싱 및 밸런싱 단계인 v0.2에서 확정할 것**으로 공식 확정한다.
- **ClutchMultiplier 방어(Sanitize)**: `GameManager.ResolveCheerleaderClutchMultiplier()`가 `MatchEngine`에 전달되기 직전 비정상 입력을 중립값(1.0f)으로 억제한다 - `NaN`/`Infinity`는 별도로 걸러내고(단순 `Mathf.Max`로는 걸러지지 않음), 그 외 0 이하 값은 `Mathf.Max(1.0f, value)`로 끌어올린다. `Cheerleader` 필드 자체가 `null`일 수 있는 경우(미장착)도 이 함수가 함께 방어한다.
- **[v0.1 마감/TASK-KBO-062] 중첩/상한 미확정**: 치어리더 `ClutchMultiplier`가 코치 등 향후 추가될 다른 시너지와 중첩될 때의 합산 방식(가산? 곱셈? 최댓값?)이나 상한선은 아직 기획 확정 전이다. 현재 구현은 치어리더 단독 값만 정규화해서 반환하며, 상한 로직 자체가 없다(무한대만 방어). **v0.1 현재 상태(치어리더 단독, 상한 없음)를 유지하며, 폴리싱 및 밸런싱 단계인 v0.2에서 확정할 것**으로 공식 확정한다 - 코치 등 신규 시너지 소스가 실제로 추가되기 전까지는 중첩 문제 자체가 발생하지 않는다.

# 8. [치어리더 B안: 상시 경제/멘탈 효과 및 팬심 시스템] [TASK-KBO-049/050]

- **성격 구분**: 6·7절의 `ConditionBuff`/`ClutchMultiplier`(A/C안)는 `MatchEngine`에 전달되는 "경기 조건부 적용 전력"이지만, 이번 절의 `EconomicBonusRate`/`SentimentDefense`(B안)는 `MatchEngine`에 전혀 전달되지 않는다 - "스토브리그 로비 연산"(경기 결산 단계)에서만 쓰이는 값이며, 구현 위치는 `Assets/Scripts/Managers/MatchRewardManager.cs`의 `GrantRewardForMatch()`/`ApplyCheerleaderEconomicBonus()`/`UpdateLosingStreakAndFanSentiment()`다.
- **① 홈 경기 승리 시 경제 증폭**: 유저 팀이 **홈 경기에서 승리**했을 때만 장착된 `Cheerleader.EconomicBonusRate`(float, 기본 1.0f)가 기본 보상(스카우트 리포트)에 곱해진다. 미장착이거나 조건 미충족(AI 팀/원정/패배/무승부)이면 배율 1.0f로 동작한다.
- **② 연패 시 팬심 하락과 치어리더 방어**: 유저 팀의 연속 패배 횟수(`GameManager.LosingStreak`)가 **3연패 이상**이 되면, 패배할 때마다 팬심(`GameManager.FanSentiment`)이 하락한다. 하락폭은 `Mathf.Max(0, 기본 하락치(5) - Cheerleader.SentimentDefense)`로 계산되어, 장착된 치어리더의 `SentimentDefense`(int, 기본 0)만큼 방어된다(방어가 하락치를 초과해도 팬심이 오히려 오르지는 않는다). 승리 또는 무승부 시 `LosingStreak`은 0으로 리셋된다. **[v0.1 마감/TASK-KBO-062]** "3연패 이상"이 정확히 몇 회차마다 발동하는지(3연패째 1회만 vs 매 패배마다 반복) GDD에 명시가 없어, 현재 구현은 임계치 도달 이후 매 패배(3연패째, 4연패째, ...)마다 반복 적용한다. **v0.1 현재 상태(임계치 도달 후 매 패배 반복 적용)를 유지하며, 폴리싱 및 밸런싱 단계인 v0.2에서 확정할 것**으로 공식 확정한다.
- **③ 팬심 하락에 따른 홈 관중 수익 페널티 [TASK-KBO-050]**: `GameManager.FanSentiment`는 **0~100** 범위로 정규화되며(`Mathf.Clamp`), 기본값은 **100**이다. 팬심이 **50 미만**으로 떨어지면, 유저 팀의 **모든 홈 경기**(승/무/패 무관) 기본 보상에 **0.8배(20% 감소)** 페널티가 적용된다(정확히 50이면 페널티 미적용 - 경계값은 페널티 없음 쪽으로 귀속).
- **연산 순서(고정)**: `최종 보상 = Mathf.RoundToInt((기본 보상 × 팬심 페널티 배율) × 치어리더 경제 증폭 배율)`. 두 배율을 먼저 곱한 뒤 단 한 번만 반올림한다 - 중간에 별도로 반올림하지 않아 오차 누적이나 재화 증발이 없다. 팬심 페널티(③)는 홈 경기라면 승/무/패와 무관하게 적용 여부가 결정되고, 치어리더 경제 증폭(①)은 그중에서도 "승리"일 때만 1.0f보다 커질 수 있다 - 두 조건은 서로 독립적으로 판정된 뒤 곱셈으로만 결합된다.
- **"연패 중 팬심 하락을 막는 치어리더 효과" 검토 결과**: TASK-KBO-050 명령서가 검토를 요청한 "연패 중 치어리더 효과로 팬심 하락을 방어하는 스킬"은 이미 위 ②의 `Cheerleader.SentimentDefense`가 정확히 그 역할을 하고 있다 - 별도의 새 스킬/효과 체계를 추가로 구현하지 않았다(TASK-KBO-050 범위 제외: 신규 기능 추가 금지, 완료 보고서 F 섹션 참고).
- **[v0.1 마감/TASK-KBO-062] 팬심 회복 수단 부재**: v0.1 시점에는 팬심을 다시 끌어올리는 이벤트/아이템/승리 보상 등이 전혀 없다(범위 제외) - 한 번 50 미만으로 떨어지면 연승으로 `LosingStreak`만 리셋될 뿐, `FanSentiment` 자체는 별도 회복 수단이 생기기 전까지 낮게 유지된다. **v0.1 현재 상태(회복 수단 없음, 하락 전용 스탯)를 유지하며, 폴리싱 및 밸런싱 단계인 v0.2에서 확정할 것**으로 공식 확정한다 - 구체적인 회복 수치/이벤트/아이템 설계는 이번 문서에서 임의로 창작하지 않는다.

# 9. v0.1 "치어리더 B+C 하이브리드" 마일스톤 마감 및 v0.2 이관 사항 [TASK-KBO-062]

TASK-KBO-039~061로 구현된 치어리더 B+C 하이브리드 시스템(인게임 조건부 버프, 스토브리그 경제/팬심 결산, 유저 인벤토리·장착·화면 전환 UI 자동화)을 v0.1 범위에서 최종 마감한다. 아래는 지금까지 `[TBD]`로 남아있던 항목들을 한 곳에 모은 v0.2 후보 목록이다 - 구체적인 수치나 공식은 이 문서에서 확정하지 않으며, 모두 "v0.2 기획 확정 필요" 상태로만 표시한다.

| # | 항목 | v0.1 현재 상태(유지) | 근거 절 |
| :-- | :--- | :--- | :--- |
| 1 | 치어리더 획득 경로(가챠 등) | 없음 - 에디터 QA 메뉴로만 인벤토리에 추가 가능 | 7절 |
| 2 | 치어리더 등급 서열/개수 확장 | `NONE`/`TEST` 2종뿐 | 7절 |
| 3 | 포스트시즌 중립 구장/홈-원정 교대 | `HigherSeed` 시리즈 내내 고정 home | 7절 |
| 4 | `ClutchMultiplier` 중첩/상한 정책 | 치어리더 단독 값만 정규화(상한 없음, 무한대만 방어) | 7절 |
| 5 | 연패 팬심 하락 발동 주기 규칙 | 임계치(3연패) 도달 후 매 패배마다 반복 적용 | 8절 |
| 6 | 팬심 회복 수단 | 전무(하락 전용 스탯) | 8절 |

각 항목은 "v0.1 현재 상태를 그대로 유지하며, 폴리싱 및 밸런싱 단계인 v0.2에서 확정할 것"이라는 공식 스탠스로 처리하며, 이번 작업(TASK-KBO-062)에서 코드나 수치는 전혀 변경하지 않았다.
