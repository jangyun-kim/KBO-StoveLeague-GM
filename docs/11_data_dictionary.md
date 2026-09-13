---
문서명: 데이터 사전 (Data Dictionary)
버전: v0.1
상태: Active
최종 수정일: 2026-09-13
담당자: 김장윤
관련 파일: 모든 data/*.csv 파일, Cheerleader.cs, GameManager.cs, MatchRewardManager.cs
---

# 1. 목적

- CSV와 JSON 파일의 스키마를 정의하고, Naming Convention(명명 규칙)을 통일하여 코드 파싱(C#) 단계의 휴먼 에러를 방지한다.

# 2. 식별자(ID) 명명 규칙

모든 Primary Key(ID)는 직관적인 접두사(Prefix)를 가진다.

- `PLY_0001` : 원본 선수(Player) ID (예: 23년 구자욱)
- `CRD_0001` : 발행된 카드(Card) 인스턴스 ID
- `TEM_001` : 구단(Team) ID (예: TEM_001 = 삼성 라이온즈)
- `CHR_001` : 치어리더(Cheerleader) ID
- `SKL_001` : 스킬(Skill) ID

# 3. 주요 CSV 스키마 명세

_※ 각 CSV 문서 내 헤더 및 자료형을 설명함._

### A. players.csv

- `player_id` (String): PLY\_ 접두사 고유 키
- `team_id` (String): 소속 팀 (TEM_001)
- `name` (String): 선수명
- `year` (Int): 시즌 연도
- `position` (String): SP, RP, CP, C, 1B, 2B, 3B, SS, LF, CF, RF, DH
- `z_contact, z_power...` (Float): 베이지안 K 보정이 끝난 Z-Score 수치 (소수점 4자리 권장)

### B. cards.csv

- `card_id` (String): CRD\_ 접두사
- `player_id` (String): 외래키 (players.csv 참조)
- `grade_id` (Int): 0(시즌)~7(왕조) Enum 값
- `base_ovr` (Int): 공식에 의해 산출된 해당 카드의 명함 초기 OVR (Clamp 40~99)
- `salary_cost` (Int): 샐러리캡 소모 비용 계산값

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
- **`CheerleaderGrade` enum**(선수 카드 등급 `Grade`와 완전히 분리된 별도 체계, 혼용 금지): `NONE = 0`, `TEST = 1`. **[TBD]** v0.1 시점에는 실제 등급 서열/획득 방식/개수가 기획 확정되지 않아 개발·테스트용 값만 존재한다. 후속 작업에서 정식 등급을 추가할 때는 `Grade` enum이 JsonUtility 정수 직렬화 문제로 명시적 정수값을 고정했던 선례(DCL-006)를 참고해 신중히 배치할 것 - 다만 치어리더는 아직 세이브/로드 대상이 아니므로(범위 제외) TASK-KBO-048 시점에는 직렬화 안정성 이슈가 발생하지 않는다.
- 이번 작업의 개발용 더미 데이터(`GameManager.InitializeDevOnlyTestCheerleader()`, 에디터 전용)는 `InstanceId`로 `DEV_TEST_CHEER_001`을 쓴다 - 의도적으로 `CHR_` 접두사를 쓰지 않았다(실제 카탈로그 데이터가 아직 없는 상태에서 향후 정식 `CHR_XXX` ID와 혼동되지 않도록 구분하기 위함).
- CSV 스키마(`cheerleaders.csv` 등)는 아직 존재하지 않는다 - 가챠/획득 시스템이 이번 작업 범위에서 제외되었기 때문이며, 현재는 `GameManager.EquippedCheerleader` 필드에 코드/인스펙터로 직접 값을 채우는 방식만 지원한다.

# 8. 치어리더 B안(상시 경제/멘탈) 필드 및 유저 결산 스탯 [TASK-KBO-049]

- **`Cheerleader.EconomicBonusRate`**(float, 기본 1.0f): 유저 팀 홈 승리 시 경기 보상(재화)에 곱해지는 배율. A/C안(`ConditionBuff`/`ClutchMultiplier`, TASK-KBO-048)과 달리 `MatchEngine`에는 전달되지 않고, `MatchRewardManager.GrantRewardForMatch()`(경기 결산 단계)에서만 읽는다.
- **`Cheerleader.SentimentDefense`**(int, 기본 0): 유저 팀 연패 시 팬심 하락폭을 방어하는 수치. 동일하게 `MatchRewardManager`에서만 읽는다.
- **`GameManager.FanSentiment`**(int, 기본 0, `Mathf.Max(0, value)`로 0 미만 방지): 신규 유저 스탯. **[TBD]** 실제 시작값/상한/구간별 의미는 기획 미확정 - 현재는 연패 시 하락만 구현되어 있고 상승 요인/다른 소모처는 없다.
- **`GameManager.LosingStreak`**(int, 기본 0, 0 미만 방지): 유저 팀의 현재 연속 패배 횟수. 승리 또는 무승부 시 0으로 리셋된다.
- 위 4개 필드 모두 CSV/JSON 스키마 없이 코드에만 존재한다(가챠/획득/세이브 시스템이 범위 제외 - 4절의 "역호환성" 마이그레이션 규칙이 실제로 적용될 시점은 세이브 대상이 되는 후속 작업부터다).
- **[TASK-KBO-057 시점 갱신 안내]** 위 문장 중 "`FanSentiment`(기본 0)"과 "세이브 시스템 범위 제외" 서술은 TASK-KBO-050/051(`DCL-027`/`DCL-028`)에서 이미 각각 기본값 100/`Mathf.Clamp(0,100)`으로 확정, `SaveManager` 연동 완료로 바뀌었으나 이 문서 8절 자체는 그때 갱신되지 않았다. 이번 작업(TASK-KBO-057)의 지시 범위는 9절 신설이라 8절 본문은 임의로 고치지 않고 이 안내문만 남긴다 - 8절 갱신은 별도 결정 필요.

# 9. 치어리더 세이브 연동(EquippedCheerleader/OwnedCheerleaders) [TASK-KBO-057]

- **`GameSaveData.EquippedCheerleader`**(`Cheerleader`, SaveVersion 5부터): `GameManager.EquippedCheerleader`(장착 슬롯, 여전히 v0.1 기준 단일 슬롯)를 그대로 저장한다.
- **`GameSaveData.OwnedCheerleaders`**(`List<Cheerleader>`, 기본값 빈 리스트, SaveVersion 5부터): `GameManager.OwnedCheerleaders`(유저가 영구 보유한 치어리더 전체 목록, 장착 여부와 무관)를 그대로 저장한다.
- **[중요, JsonUtility 한계]** `JsonUtility`는 null 참조 필드를 JSON `null`이 아니라 "필드가 전부 기본값으로 채워진 인스턴스"로 직렬화한다(실측 확인: `EquippedCheerleader`가 `null`이어도 저장 시 `{"InstanceId": "", ...}` 형태의 non-null 객체가 됨). 그 결과 `SaveManager.ApplySaveData()`는 `data.EquippedCheerleader.InstanceId`가 비어 있는지로 "실제 장착된 치어리더가 있었는지"를 판별한다 - 실제 치어리더는 모든 생성 경로(`GameManager.InitializeDevOnlyTestCheerleader()` 등)에서 `InstanceId`를 항상 채우므로 안전한 기준이다. 향후 가챠/획득 시스템을 붙일 때도 이 불변식(치어리더 인스턴스는 항상 비어 있지 않은 `InstanceId`를 가진다)을 반드시 지켜야 한다.
- `GameManager.AddCheerleader(Cheerleader)`가 `OwnedCheerleaders`에 새 치어리더를 추가하는 유일한 정식 진입점이다(null 인자는 무시). 아직 가챠/획득 UI가 없어(범위 제외) 실제 호출부는 없다.
- CSV 스키마(`cheerleaders.csv` 등)는 여전히 존재하지 않는다 - 7절에서 이미 밝힌 대로 카탈로그 데이터/가챠 시스템 자체가 아직 범위 밖이며, 이번 작업은 "유저가 이미 보유한 치어리더 인스턴스"를 세이브에 영속화하는 것만 다룬다.
