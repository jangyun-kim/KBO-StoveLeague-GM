---
문서명: 데이터 사전 (Data Dictionary)
버전: v0.1
상태: Active
최종 수정일: 2026-09-12
담당자: 김장윤
관련 파일: 모든 data/*.csv 파일
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
