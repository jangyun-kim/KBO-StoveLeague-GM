---
문서명: 데이터 사전 (Data Dictionary)
버전: v0.1
상태: Active
최종 수정일: 2026-09-14
담당자: 김장윤
관련 파일: 모든 data/*.csv 파일, Cheerleader.cs, GameManager.cs, MatchRewardManager.cs
---

# 1. 목적

- CSV와 JSON 파일의 스키마를 정의하고, Naming Convention(명명 규칙)을 통일하여 코드 파싱(C#) 단계의 휴먼 에러를 방지한다.

# 2. 식별자(ID) 명명 규칙

모든 Primary Key(ID)는 직관적인 접두사(Prefix)를 가진다.

- `PLY_0001` : 원본 선수(Player) ID (예: 23년 구자욱)
- `CRD_0001` : 발행된 카드(Card) 인스턴스 ID
- `TEM_001` : 구단(Team) ID - 정확한 10개 구단 매핑은 아래 "Team ID 매핑표" 참고
- `CHR_001` : 치어리더(Cheerleader) ID
- `SKL_001` : 스킬(Skill) ID

### Team ID 매핑표 (KBO 10개 구단, TASK-KBO-086 공식 확정)

**[결정 필요 - 해소됨]** TASK-085 시점에는 `PlayerDatabase.cs`에 3개 구단(`TEM_001`=SSG/`TEM_002`=KIA/
`TEM_003`=LG)만 임시로 하드코딩되어 있었고, 그 값이 이 문서의 기존 예시("TEM_001 = 삼성 라이온즈")와
서로 달라 `docs/13_decision_change_log.md`의 DCL-055가 [결정 필요]로 남겼던 불일치입니다. 아래 표가 그
불일치를 해소하는 **공식 매핑**이며, `Assets/Scripts/Managers/PlayerDatabase.cs`의
`TeamIdMapping` 딕셔너리가 이 표와 정확히 동기화되어 있습니다(TASK-086).

| team_id | 구단(Team enum) |
|---|---|
| `TEM_001` | KIA |
| `TEM_002` | 삼성 (Samsung) |
| `TEM_003` | LG |
| `TEM_004` | 두산 (Doosan) |
| `TEM_005` | KT |
| `TEM_006` | SSG |
| `TEM_007` | 롯데 (Lotte) |
| `TEM_008` | 한화 (Hanwha) |
| `TEM_009` | NC |
| `TEM_010` | 키움 (Kiwoom) |

`Assets/Scripts/Models/Types.cs`의 `Team` enum(`None` + 10개 구단)을 전수 확인한 결과 위 10개 구단이
모두 존재해, `Team.None`으로 예외 처리해야 하는 누락 구단은 없었다(TASK-086 정적 확인 완료).

# 3. 주요 CSV 스키마 명세 - 선수 데이터 SSOT

_※ 각 CSV 문서 내 헤더 및 자료형을 설명함._

**[TASK-KBO-081 PM 확정]** `players.csv`/`cards.csv`는 v0.3 선수 카드 시스템의 공식 Single Source of
Truth(SSOT)입니다. 게임 실행 시 이 두 CSV를 파싱해 `Assets/Scripts/Data/PlayerTemplate.cs`
(`ScriptableObject`) 인스턴스를 **런타임에 동적으로 생성**하며, 에디터에서 `.asset` 파일을 수동으로 미리
구워 두는 방식은 사용하지 않습니다 - `PlayerTemplate`은 더 이상 "에디터가 저장하는 정적 에셋"이 아니라
"CSV 파싱 결과를 담는 메모리 캐싱용 브릿지 모델"입니다. 치어리더 시스템의 `cheerleaders.csv →
CheerleaderCatalog.Initialize()` 패턴과 동일한 방향입니다. 세부 필드 설명과 조사 근거는
`docs/18_player_schema_policy.md`를 참고하십시오.

### A. players.csv

**[TASK-KBO-088]** 총 16컬럼 스키마로 확정: `player_id,team_id,name,year,position,pa_ip,z_contact,z_eye,
z_power,z_speed,z_def,z_stamina,active,z_stuff,z_control,z_movement`. 기존 13컬럼(아래 목록)은 그대로 보존했고,
투수 전용 Z-score 3종(`z_stuff`/`z_control`/`z_movement`)을 맨 뒤에 추가했다 - 컬럼 순서 변경 없음(TASK-085/
DCL-055 당시 확인된 실제 인덱스와 완전히 동일하게 유지, 신규 컬럼은 `columns[13]`~`columns[15]`).

- `player_id` (String): PLY\_ 접두사 고유 키
- `team_id` (String): 소속 팀 (예: `TEM_001` = KIA. 정확한 10개 구단 전체 매핑은 2절 "Team ID 매핑표" 참고)
- `name` (String): 선수명 (KBO 실명 - 본 프로젝트는 비상업적 팬 메이드 포트폴리오로 규정되어 실명 데이터를
  그대로 사용한다. `docs/18_player_schema_policy.md` 0절 참고)
- `year` (Int): 시즌 연도
- `pa_ip` (Int): 타석수(타자) 또는 이닝수(투수) - 현재 `PlayerDatabase.ParseCsv()`가 읽지는 않는다(**[TBD]**).
- `position` (String): SP, RP, CP, C, 1B, 2B, 3B, SS, LF, CF, RF, DH
- `z_contact, z_eye, z_power, z_speed, z_def, z_stamina` (Float): 베이지안 K 보정이 끝난 타자/공통 Z-Score
  수치 (소수점 4자리 권장). D절 확정 공식/매핑표 참고.
- `active` (Bool): 현재 `PlayerDatabase.ParseCsv()`가 읽지는 않는다(**[TBD]**).
- `z_stuff, z_control, z_movement` (Float): **[TASK-KBO-088 신규]** 투수 전용 Z-score 3종. 타자 행에는 의미
  없는 값(`0.000`)이 채워진다. D절 확정 공식/매핑표 참고.

### B. cards.csv

- `card_id` (String): CRD\_ 접두사
- `player_id` (String): 외래키 (players.csv 참조)
- `grade_id` (Int): 0(시즌)~7(왕조) Enum 값 - `Assets/Scripts/Models/Types.cs`의 `Grade` enum 정수값과
  정확히 일치하도록 설계되어, 브릿지 구현 시 별도 매핑표 없이 그대로 캐스팅할 수 있다.
- `base_ovr` (Int): 공식에 의해 산출된 해당 카드의 명함 초기 OVR (Clamp 40~99)
- `salary_cost` (Int): **[TASK-KBO-173]** 등급 기본(명함) 개인 세트덱 스코어 = 카드 Salary(LIVE/AS 4, FRA/TH 3, RN/GG 2, SIG/DYN 1). 샐러리 캡은 폐기됐고 `CardGrowthRules.BaseSetDeckScore()`와 반드시 일치해야 한다(`PlayerDatabase` 로드 시 불일치 경고).
- **[TASK-KBO-172 갱신]** 실제 `cards_{TEAM}.csv`의 `grade_id`는 `LIVE_NORMAL=1 ~ DYNASTY=9`(FRANCHISE=4 삽입 후 서열)이며,
  `max_awaken`은 초월 가능 등급(LIVE/GOLDEN_GLOVE/SIGNATURE/DYNASTY) 10(=초월), 9각 한계 등급(ALLSTAR/FRANCHISE/
  TITLE_HOLDER/RETIRED_NUMBER) 9다 - 런타임 한계는 `CardGrowthRules.MaxAwakenLevelFor()`가 같은 값을 코드로 보장한다
  (`docs/04_card_grade_policy.md` 4절).
- `max_enhance` / `max_awaken` (Int): 강화/각성 상한 - 현재 코드(`Player.MaxReinforceLevel`/
  `Player.MaxAwakenLevel`)는 이 값을 상수(둘 다 10)로 고정하고 있으며, CSV 컬럼값을 실제로 읽어오는 로직은
  아직 없다(**[TBD]**, `docs/18_player_schema_policy.md` 4-2절 참고).
- `is_droppable` (Bool): 가챠로 뽑힐 수 있는 카드인지 여부.

### C. `PlayerTemplate` 런타임 인스턴스화 아키텍처 (SSOT → 게임 내 카드)

- **데이터 흐름**: `players.csv`(선수 원본 스탯) + `cards.csv`(등급별 카드 변형, `player_id` 외래키로 조인)
  → 게임 시작 시 파싱 → `PlayerTemplate` 인스턴스를 런타임에 생성 → `PlayerDatabase.allTemplates`에 등록
  → `ScoutManager`(가챠)가 이 템플릿을 참조해 유저 소유 카드(`Player.cs` 인스턴스, `InstanceId` 발급)를
  발행한다.
- **주의(스탯 모델 차이, TASK-KBO-088로 해소)**: `players.csv`의 세부 스탯은 `z_contact`/`z_eye`/`z_power`/
  `z_speed`/`z_def`/`z_stamina`/`z_stuff`/`z_control`/`z_movement` 9종(Z-score, 실수)이고,
  `PlayerTemplate.BatterStats`는 `Power`/`Contact`/`Discipline`/`Speed`/`Defense`(타자 5종), `PitcherStats`는
  `Stuff`/`Velocity`/`Movement`/`Control`/`Stamina`(투수 5종, 정수)이다. `Assets/Scripts/Models/Types.cs`에
  `Speed`/`Defense`/`Stamina` 필드가 TASK-088에서 신설되어 두 스탯 모델이 1:1로 대응하며, 실제 변환 공식도
  `PlayerDatabase.cs`의 `ConvertZScoreToStat()`으로 구현이 완료되었다(아래 D절 참고).
- **주의(포지션/팀 값 차이, 매핑 완료)**: `players.csv`의 `position`(문자열, 예: `"SP"`/`"3B"`)과
  `team_id`(예: `"TEM_001"`)는 `PlayerTemplate`의 `BatterPosition`/`PitcherRole`/`Team` enum과 표기
  체계가 달라 매핑표가 필요했다 - `PlayerDatabase.cs`의 `ParsePitcherRole()`/`ParseBatterPosition()`
  (TASK-082)과 `TeamIdMapping`(TASK-085/086, 2절의 "Team ID 매핑표"와 동기화)이 이미 구현되어 실제
  파싱 코드에 반영되어 있다.
- **구현 현황**: 포지션/팀 매핑, 세부 스탯(Z-score → 정수) 변환 코드 모두 `PlayerDatabase.cs`에 구현
  완료되었다(TASK-088). **[TBD, 이번 태스크 범위 밖]** 다만 `Player.GetEffectiveBatterStats()`/
  `GetEffectivePitcherStats()`와 `MatchEngine.cs`의 `AddTeamBuff()`/`Scale()`/`ApplyModifier()`는 여전히
  기존 3-인자(`BatterStats`)/4-인자(`PitcherStats`) 생성자만 사용해 반환값을 새로 조립한다 - 이 생성자들은
  하위호환을 위해 `Speed`/`Defense`/`Stamina`를 항상 0으로 초기화하므로, `PlayerDatabase.cs`가 파싱한
  세 필드 값은 강화/각성/스킬/팀버프 연산 체인을 통과하는 순간 0으로 리셋된다 - 즉 `MatchEngine`이 실제
  타석 판정에 쓰는 "유효 스탯"에는 아직 `Speed`/`Defense`/`Stamina`가 반영되지 않는다(**[결정 필요]**,
  `Player.cs`/`MatchEngine.cs`는 TASK-088 포함 범위 밖이라 손대지 않았다).

### D. 스탯 Z-score 변환 규칙 (TASK-KBO-088 확정 및 구현 완료)

`players.csv`의 9종 Z-score(`z_contact`/`z_eye`/`z_power`/`z_speed`/`z_def`/`z_stamina`/`z_stuff`/`z_control`/
`z_movement`)를 `PlayerTemplate.BatterStats`/`PitcherStats`(정수, 1~100 범위 인게임 스탯)로 환산하는 공식이다.
일반적인 종형 곡선(정규분포) 기반 Z-score 환산 관례를 따라, 평균(Z=0)을 50점에 대응시키고 표준편차 1당
15점씩 움직이도록 설계했다 - 상한/하한을 1~100으로 clamp해 극단값에서도 스탯 범위를 벗어나지 않는다.
`PlayerDatabase.cs`의 `ConvertZScoreToStat(float zScore)`가 이 공식을 그대로 구현한다.

| 항목 | 값 |
|---|---|
| 변환 공식 | `스탯 정수 = Max(1, Min(100, Round(z_score * 15 + 50)))` |
| 기준점(Z=0) | 50점 (평균적인 선수) |
| 계수(1 표준편차당) | ±15점 |
| 결과 범위 | 1 ~ 100 (Clamp) |

| CSV 컬럼 | 대응 `PlayerTemplate` 필드 | 비고 |
|---|---|---|
| `z_contact` | `BatterStats.Contact` | 타자 전용 |
| `z_power` | `BatterStats.Power` | 타자 전용 |
| `z_eye` | `BatterStats.Discipline` | 타자 전용(선구안 → 선구) |
| `z_def` | `BatterStats.Defense` | 타자 전용(**[TASK-KBO-088 신규 필드]**) |
| `z_stuff` | `PitcherStats.Stuff` | 투수 전용(**[TASK-KBO-088 신규 컬럼]** - `z_stuff` 컬럼 자체가 이번 태스크에서 `players.csv`에 추가됨) |
| `z_control` | `PitcherStats.Control` | 투수 전용(**[TASK-KBO-088 신규 컬럼]**) |
| `z_movement` | `PitcherStats.Movement` | 투수 전용(**[TASK-KBO-088 신규 컬럼]**) |
| `z_stamina` | `PitcherStats.Stamina` | 투수 전용(**[TASK-KBO-088 신규 필드]**, `Player.MaxStamina`(롤 기준 상수)와는 별개 값) |
| `z_speed` | 타자: `BatterStats.Speed`(**[TASK-KBO-088 신규 필드]**) / 투수: `PitcherStats.Velocity` | **[TASK-KBO-088 PM 확정]** 동일 컬럼을 포지션에 따라 다형성 매핑 - 타자는 주력, 투수는 구속으로 해석한다. |

**[결정 필요, TASK-KBO-088에서 새로 발견]** 위 파싱/변환 자체는 `PlayerDatabase.cs`에 구현이 끝났으나,
`Player.cs`(`GetEffectiveBatterStats()`/`GetEffectivePitcherStats()`)와 `MatchEngine.cs`(`AddTeamBuff()`/
`Scale()`/`ApplyModifier()`)가 여전히 `Speed`/`Defense`/`Stamina`를 반영하지 않는 구형 생성자 호출로
`BatterStats`/`PitcherStats`를 재조립하고 있어, 파싱된 값이 실제 매치 판정까지 전달되지 않는다. 이 세 필드를
게임플레이에 실제로 반영할지(반영한다면 `MatchEngine`의 어느 확률 계산에 연결할지)는 별도 기획/구현
결정이 필요하다.

# 4. 저장 데이터 마이그레이션 규칙 (역호환성)

- JSON 세이브 파일에 존재하지 않는 신규 컬럼(예: 치어리더 호감도)이 업데이트로 추가될 경우, C# 클래스에는 반드시 기본값(`DefaultValue`) 어트리뷰트를 선언하여 구버전 세이브 로드 시 크래시를 방지한다.

# 5. 등급별 기본 코스트 (GradeBaseCost)

_※ TASK-KBO-031에서 GDD v4.0 확정 수치로 동기화됨. 구현 위치: `Assets/Scripts/Models/Player.cs`의 `GradeBaseCostFor()`(private, 등급 이름으로 매칭)._

샐러리 캡 코스트 공식: `등급 기본 코스트 + (최종 OVR - 60) + (각성 단계 * 1.5)` (구현: `Player.CalculateSalaryCost()`)

| 등급명 (Enum) | 기본 코스트 |
| :------------ | ----------: |
| SEASON         | 5  |
| LIVE_NORMAL    | 5  |
| LIVE_EPIC      | 8  |
| ALLSTAR        | 12 |
| TITLE_HOLDER   | 15 |
| SIGNATURE      | 20 |
| GOLDEN_GLOVE   | 25 |
| DYNASTY        | 35 |

- 28인 엔트리 샐러리 캡 상한선: **1350** (구현: `RosterManager.FullRosterSalaryCap`).
- `PlayerTemplate.Cost`(구 v3.1 고정 코스트 필드)는 더 이상 샐러리 캡 계산에 쓰이지 않는다 - 레거시 필드로만 남아 있다.

# 6. v0.1 마일스톤 마감 정합성 확인 (TASK-KBO-045)

- 위 3절(`grade_id`)과 5절(GradeBaseCost 표)의 `SEASON=0 ~ DYNASTY=7` 서열은 `Assets/Scripts/Models/Types.cs`의 `Grade` enum 실제 정수값(DCL-006 확정)과 재대조해 여전히 일치함을 확인했다. 이번 작업에서 CSV/JSON 스키마 자체의 변경 사항은 없다.
- 데이터 사전이 다루지 않는 런타임 전용 구조체(`TeamPowerModifiers`, `PlayEvent`/`PlayEventType`)는 CSV/JSON으로 직렬화되지 않으므로 본 문서의 스키마 범위 밖이며, 관련 정책은 `docs/15_team_power_policy.md`에서 별도로 관리한다.

# 7. 치어리더(Cheerleader) 등급 체계 [TASK-KBO-048]

- 2절의 `CHR_001` 접두사 규칙은 이번 작업에서 실제 코드 모델(`Assets/Scripts/Models/Cheerleader.cs`)로 처음 구현되었다. `Cheerleader.InstanceId`(string)가 이 ID를 담을 필드다.
- **`CheerleaderGrade` enum**(선수 카드 등급 `Grade`와 완전히 분리된 별도 체계, 혼용 금지): `NONE = 0`, `TEST = 1`. **[v0.1 마감/TASK-KBO-062]** 실제 등급 서열/획득 방식/개수는 여전히 기획 확정 전이라 개발·테스트용 값만 존재한다 - **v0.1 현재 상태를 유지하며, 폴리싱 및 밸런싱 단계인 v0.2에서 확정할 것**으로 공식 확정한다. **[중요, 정정]** 이 단락은 원래 "치어리더는 아직 세이브/로드 대상이 아니므로 직렬화 안정성 이슈가 발생하지 않는다"고 서술했으나, TASK-KBO-057부터 `Cheerleader`가 `GameSaveData`를 통해 실제로 JsonUtility로 직렬화되고 있어 더 이상 사실이 아니다 - 오히려 지금부터는 `Grade` enum이 JsonUtility 정수 직렬화 문제로 명시적 정수값을 고정했던 선례(DCL-006)와 동일한 주의가 `CheerleaderGrade`에도 그대로 적용된다. **v0.2에서 정식 등급을 추가할 때는 반드시 기존 `NONE=0`/`TEST=1` 값을 유지한 채 새 값을 끝에만 추가할 것** - 중간 삽입이나 재배치는 이미 저장된 세이브 파일의 치어리더 등급을 손상시킨다.
- 이번 작업의 개발용 더미 데이터(`GameManager.InitializeDevOnlyTestCheerleader()`, 에디터 전용)는 `InstanceId`로 `DEV_TEST_CHEER_001`을 쓴다 - 의도적으로 `CHR_` 접두사를 쓰지 않았다(실제 카탈로그 데이터가 아직 없는 상태에서 향후 정식 `CHR_XXX` ID와 혼동되지 않도록 구분하기 위함).
- CSV 스키마(`cheerleaders.csv` 등)는 아직 존재하지 않는다 - 가챠/획득 시스템이 여전히 범위 밖이기 때문이다. **[v0.1 마감/TASK-KBO-062 정정]** "현재는 `GameManager.EquippedCheerleader` 필드에 코드/인스펙터로 직접 값을 채우는 방식만 지원한다"는 이 절의 원래 서술은 더 이상 정확하지 않다 - TASK-KBO-056/057부터 `EquipCheerleader()`/`UnequipCheerleader()`/`AddCheerleader()` 정식 API가 생겼고, TASK-KBO-058~061에서 UI까지 연결되었다(9절 참고).

# 8. 치어리더 B안(상시 경제/멘탈) 필드 및 유저 결산 스탯 [TASK-KBO-049, v0.1 마감 시점 최종 정정 TASK-KBO-062]

- **`Cheerleader.EconomicBonusRate`**(float, 기본 1.0f): 유저 팀 홈 승리 시 경기 보상(재화)에 곱해지는 배율. A/C안(`ConditionBuff`/`ClutchMultiplier`, TASK-KBO-048)과 달리 `MatchEngine`에는 전달되지 않고, `MatchRewardManager.GrantRewardForMatch()`(경기 결산 단계)에서만 읽는다.
- **`Cheerleader.SentimentDefense`**(int, 기본 0): 유저 팀 연패 시 팬심 하락폭을 방어하는 수치. 동일하게 `MatchRewardManager`에서만 읽는다.
- **`GameManager.FanSentiment`**(int, **기본값 100**, `Mathf.Clamp(value, 0, 100)`로 0~100 범위 고정): 신규 유저 스탯. TASK-KBO-050(`DCL-027`)에서 기본값 100/`Mathf.Clamp`로 확정되었고, TASK-KBO-057(`DCL-030`)에서 세이브 연동까지 완료되었다(아래 9절 참고). **[v0.1 마감/TASK-KBO-062]** 팬심을 끌어올리는 회복 수단은 여전히 없다(하락 전용 스탯) - **v0.1 현재 상태를 유지하며, 폴리싱 및 밸런싱 단계인 v0.2에서 확정할 것**(자세한 배경은 `docs/15_team_power_policy.md` 9절 참고).
- **`GameManager.LosingStreak`**(int, 기본 0, 0 미만 방지): 유저 팀의 현재 연속 패배 횟수. 승리 또는 무승부 시 0으로 리셋된다.
- **[v0.1 마감 정정]** 이전 버전의 이 절은 "위 4개 필드 모두 CSV/JSON 스키마 없이 코드에만 존재한다(세이브 시스템 범위 제외)"라고 서술했으나, 이는 TASK-KBO-051(`FanSentiment`/`LosingStreak`, `SaveVersion` 4)과 TASK-KBO-057(`EquippedCheerleader`/`OwnedCheerleaders`, `SaveVersion` 5)을 거치며 더 이상 사실이 아니게 되었다 - 현재는 치어리더 관련 4개 필드(`FanSentiment`/`LosingStreak`/`EquippedCheerleader`/`OwnedCheerleaders`) 모두 `GameSaveData`를 통해 세이브/로드된다(아래 9절 참고). 이 정정으로 TASK-KBO-057 당시 남겨둔 "8절 갱신은 별도 결정 필요" 메모를 해소한다.

# 9. 치어리더 세이브 연동(EquippedCheerleader/OwnedCheerleaders) [TASK-KBO-057]

- **`GameSaveData.EquippedCheerleader`**(`Cheerleader`, SaveVersion 5부터): `GameManager.EquippedCheerleader`(장착 슬롯, 여전히 v0.1 기준 단일 슬롯)를 그대로 저장한다.
- **`GameSaveData.OwnedCheerleaders`**(`List<Cheerleader>`, 기본값 빈 리스트, SaveVersion 5부터): `GameManager.OwnedCheerleaders`(유저가 영구 보유한 치어리더 전체 목록, 장착 여부와 무관)를 그대로 저장한다.
- **[중요, JsonUtility 한계]** `JsonUtility`는 null 참조 필드를 JSON `null`이 아니라 "필드가 전부 기본값으로 채워진 인스턴스"로 직렬화한다(실측 확인: `EquippedCheerleader`가 `null`이어도 저장 시 `{"InstanceId": "", ...}` 형태의 non-null 객체가 됨). 그 결과 `SaveManager.ApplySaveData()`는 `data.EquippedCheerleader.InstanceId`가 비어 있는지로 "실제 장착된 치어리더가 있었는지"를 판별한다 - 실제 치어리더는 모든 생성 경로(`GameManager.InitializeDevOnlyTestCheerleader()` 등)에서 `InstanceId`를 항상 채우므로 안전한 기준이다. 향후 가챠/획득 시스템을 붙일 때도 이 불변식(치어리더 인스턴스는 항상 비어 있지 않은 `InstanceId`를 가진다)을 반드시 지켜야 한다.
- `GameManager.AddCheerleader(Cheerleader)`가 `OwnedCheerleaders`에 새 치어리더를 추가하는 유일한 정식 진입점이다(null 인자는 무시). **[v0.1 마감/TASK-KBO-062 정정]** 정식 가챠/획득 UI를 통한 호출부는 여전히 없으나, TASK-KBO-059에서 만든 에디터 QA 메뉴(`KBO Manager/Debug/Add Dummy Cheerleader to Inventory`)가 플레이 모드에서 이 메서드를 실제로 호출한다 - 정식 획득 경로는 v0.2 과제로 이관.
- CSV 스키마(`cheerleaders.csv` 등)는 여전히 존재하지 않는다 - 7절에서 이미 밝힌 대로 카탈로그 데이터/가챠 시스템 자체가 아직 범위 밖이며, 이번 작업은 "유저가 이미 보유한 치어리더 인스턴스"를 세이브에 영속화하는 것만 다룬다.
- **[v0.1 마감/TASK-KBO-062]** 장착/해제/화면 전환 UI(`CheerleaderInventoryUIController`/`CheerleaderSlotUI`, `LeagueDashboardUIController`의 "치어리더 관리" 버튼, `UIManager.ScreenType.CheerleaderInventory`)까지 TASK-KBO-058~061에서 완료되어, v0.1 기준 치어리더 시스템(획득 경로 제외)의 데이터·백엔드·UI 전 층위가 연결되었다.

# 10. 치어리더 정식 등급/CatalogId/중복 획득 처리 [TASK-KBO-064]

- **`CheerleaderGrade` enum 확정 값 추가**: 기존 `NONE=0`/`TEST=1`은 그대로 두고 `NORMAL=2`/`RARE=3`/`EPIC=4`/`LEGEND=5`를 끝에 추가했다(`docs/16_shop_and_gacha_policy.md` 2절 제안 반영). 7절이 경고한 "세이브 직렬화 대상이라 재배치 금지" 원칙을 그대로 지켰다 - 값을 재배치하지 않고 순차적으로만 이어 붙였다.
- **`Cheerleader.CatalogId`**(string, 신규): "어떤 종류의 치어리더인가"를 나타내는 원본(카탈로그) 식별자. `InstanceId`(발급된 개체 고유값, 9절의 세이브 null 판별 불변식에 쓰이는 값)와 완전히 별개이며, 이 불변식 자체는 전혀 바뀌지 않았다 - `CatalogId`가 비어 있어도 `InstanceId`는 여전히 모든 생성 경로에서 채워져야 한다. 카탈로그 데이터(`cheerleaders.csv` 등)는 여전히 존재하지 않으므로, `CatalogId`는 당분간 코드/디버그 툴에서 수동으로 채워 넣는 문자열일 뿐이다.
- **`GameManager.AddCheerleader(Cheerleader)` 중복 처리(`docs/16_shop_and_gacha_policy.md` 4절 A안 구현)**: 인자로 들어온 치어리더의 `CatalogId`가 채워져 있고, 이미 같은 `CatalogId`를 가진 치어리더를 `OwnedCheerleaders`에 보유 중이면 인벤토리에 추가하지 않고 `GameManager.PremiumCurrency`를 등급별로 지급한다(`NORMAL` 10 / `RARE` 50 / `EPIC` 200 / `LEGEND` 1000, `NONE`/`TEST` 등 그 외 등급은 `NORMAL`과 동일하게 10 - 모두 **[Draft]**, v0.2 밸런싱에서 조정 가능). `CatalogId`가 비어 있으면(카탈로그 도입 이전 더미 데이터 등) 기존과 동일하게 중복 검사 없이 그냥 추가한다.
- **디버그 툴 반영**: `SetupCheerleaderUI.AddDummyCheerleaderToInventory()`(TASK-KBO-059)가 주입하는 더미 치어리더에도 고정된 `CatalogId`(`DEV_TEST_CHEER_CATALOG_001`)를 채웠다 - 이 메뉴를 두 번째 클릭부터는 중복 변환 경로가 실제로 발동해, 신규 추가/중복 변환 두 경로를 QA가 같은 메뉴로 반복 테스트할 수 있다.
- **[TBD, v0.2로 이관]** 실제 가챠 확률 엔진, 상점 UI, `cheerleaders.csv` 카탈로그 스키마는 이번 작업에서 다루지 않았다(`docs/16_shop_and_gacha_policy.md` 7절이 이미 범위 밖으로 명시).
