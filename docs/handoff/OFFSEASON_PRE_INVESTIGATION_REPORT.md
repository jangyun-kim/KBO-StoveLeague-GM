# 규정 반영 사전 조사 보고서

## 1. 조사 범위

- **조사일:** 2026-10-07
- **프로젝트 루트:** `C:\Users\PC\KBO_Manager_GAME` (Unity, 브랜치 `master`, 최신 커밋 `e9d740d` TASK-GM-06)
- **조사한 디렉터리:** `Assets/Scripts/{Core, Models, Services, Simulation, Managers, Controllers, Data, Editor/Tests}`, `Assets/Resources/Data`, `docs/`, `docs/KBO_books`
- **스크립트 총수:** `Assets/Scripts` 아래 `.cs` 243개
- **정독한 파일(12):** `Core/GMConfigAndEnums.cs`, `Services/GMRosterLoader.cs`(GMTeamState · GMLeagueState 포함), `Services/GMStoveLeagueMarket.cs`,
  `Services/GMFrontOffice.cs`, `Simulation/GMFrontOfficeModels.cs`, `Simulation/GMSeasonData.cs`(세이브 v14~17 모델), `Models/Player.cs`,
  `Data/PlayerTemplate.cs`, `Simulation/GMAwardEvaluator.cs`(연도 전환부), `Managers/SaveManager.cs`(PlayerSaveData · GM 저장부), `Models/StoveLeagueRules.cs`(레거시 앞부분),
  `Assets/Resources/Data/players.csv`(헤더)
- **키워드 검색으로 확인한 파일:** `GMLiveSeasonSimulator.cs`(부상 · 단계 전환 · 소식), `GMOotpFrontOfficeUIController.cs`(협상 호출부 · 진행 버튼), `Models/Types.cs`(LeaguePhase),
  `Managers/StoveLeagueManager.cs` · `SeasonRollover.cs` · `LeagueManager.cs`(레거시 카드 시즌 순환), `GMDiagnosticView.cs`
- **테스트 파일 수:** 20개(`Editor/Tests`: GM01~GM06 VerificationRunner 6, Task181~193 Tests 13, SampleSceneTestFixture 1), `[Test]` 194개
- **조사하지 못한 영역:** Unity 씬 · 프리팹 직렬화 값, 테스트 실제 실행 결과(이번 단계에서 Unity Batchmode 미실행), 레거시 카드 모드 UI 전체, 레코드북 · 가이드북 전 페이지

## 2. 현재 구조 및 확인된 파일

| 도메인 | 실제 파일 | 클래스·모듈 | 함수·메서드 | 현재 역할 | 저장 여부 | 확인 상태 |
|---|---|---|---|---|---|---|
| 시즌 단계 | `Core/GMConfigAndEnums.cs` | `GMSeasonPhase` | - | StoveLeague → RegularSeason → PostSeason → AwardsCeremony (4단계) | O (`GMLeagueSaveData.Phase`) | 실제 존재 |
| 리그 상태 · 연도 전환 | `Services/GMRosterLoader.cs` | `GMLeagueState` | `AdvancePhase()` | 시상식 후 연도 +1, 나이 +1, 잔여 계약 -1, 부상 초기화, 기록 리셋 | O | 실제 존재 |
| 시즌 결산 오케스트레이션 | `Simulation/GMAwardEvaluator.cs` | `GMAwardEvaluator` | `AdvanceToNextSeasonYear()` | PS · 시상식 마무리 → 프런트 오피스 결산 → `AdvancePhase` → `OnNewSeason` | - | 실제 존재 |
| 구단 상태 · 재정 | `Services/GMRosterLoader.cs` | `GMTeamState` | `Payroll`, `Budget`, `PayrollCap` | 28인 로스터, 운영 자금(만 원), 캡 137만(=137억) | O (`GMTeamSaveData`) | 실제 존재 |
| 선수 계약 | `Models/Player.cs` | `Player` | `Salary`, `ContractYears`, `ComputeSalary()` | 연봉(만 원) · 잔여 계약 0~5년 두 필드뿐 | O (`PlayerSaveData` v14) | 유사 구조 |
| 재계약 · 방출 | `Services/GMStoveLeagueMarket.cs` | `GMStoveLeagueMarket` | `Extend()`, `Release()`, `AppointCaptain()` | 요구액 비율 수락, 계약금 10%, 방출 위약금 50% | 결과만 저장 | 실제 존재 |
| FA | 같은 파일 | 같은 클래스 | `FAMarket()`, `OfferContract()`, `FADemand()`, `RivalBid()`, `Scout()` | 상위 15명 노출, 점수 ≥ 경쟁 입찰이면 영입, 계약금 25% | FA 풀 저장 | 실제 존재 |
| 트레이드 | 같은 파일 | 같은 클래스 | `Evaluate()`, `ExecuteTrade()`, `ShopPlayer()`, `AcceptOffer()` | 1:1 · 2:2, 가치 비교, 난이도 배수 | 결과만 | 실제 존재 |
| 신인 드래프트 | 같은 파일 + `GMRosterLoader.BuildDraftPool()` | | `Draft()`, `MakeRookie()`, `RookieBonus()` | 풀 10명, 연 2명, 최저연봉 · 5년 | 풀 저장 | 실제 존재 |
| 하우스 룰 · 난이도 · 구단주 | `Services/GMFrontOffice.cs` | `GMFrontOffice` | `CanSignFreeAgent()`, `CanTrade()`, `Budget()`, `OnSeasonCompleted()`, `OnNewSeason()` | 연간 FA · 트레이드 횟수 제한(게임 오리지널), 예산 정보 | O (`GMFrontOfficeState`) | 실제 존재 |
| 거래 기록 | `Simulation/GMSeasonData.cs` | `GMNewsItem` (Kind = Trade/Scouting) | `GMLeagueState.AddNews()` | 문장형 소식, 최대 120건 | O(최근 120건) | 유사 구조 |
| 부상 | `Simulation/GMLiveSeasonSimulator.cs` | | `RollInjury()`, `Injure()`, `TickInjuries()` | `InjuryRemainingDays` 감소, 인터럽트 팝업 | O | 유사 구조(명단 개념 없음) |
| 저장 | `Managers/SaveManager.cs`, `Simulation/GMSeasonData.cs` | `GMLeagueSaveData`, `GMTeamSaveData`, `PlayerSaveData` | ToSaveData/Restore | SaveVersion 17, JsonUtility 필드 존재 기반 하위 호환 | - | 실제 존재 |
| 선수 원본 데이터 | `Data/PlayerTemplate.cs`, `Resources/Data/players.csv` | `PlayerTemplate` | `GetBaseOverall()` | 시즌별 Z-score 스탯 · 포지션 · 현역 여부 · 현 소속 | 에셋 | 실제 존재 |
| 레거시 스토브리그(카드 모드) | `Models/StoveLeagueRules.cs`, `Managers/StoveLeagueManager.cs`, `Controllers/StoveLeagueView.cs` | `StoveLeagueState`, `StoveLeagueRules` | | 포인트 기반 FA 6명 · 트레이드 제안 3건 · 신인 1장 · 외국인 1명(카드 등급 단위) | O(레거시) | 실제 존재 - 단장 모드와 별개 |
| 레거시 리그 단계 | `Models/Types.cs` | `LeaguePhase` | | STOVE_LEAGUE / REGULAR_OPEN(1~72) / REGULAR_LOCKED(73~, 트레이드 마감) / POST_PREP / POST_SEASON | O(레거시) | 실제 존재 - 단장 모드 미사용 |

**예시 구조 판정 (6절 요구)**

| 예시 이름 | 판정 | 실제 대응 |
|---|---|---|
| `GameManager` | 실제 존재(레거시 카드 모드 중심) | `Managers/GameManager.cs` |
| `SeasonManager` | 기능은 있으나 이름이 다름 · 분산 구현 | `GMLeagueState.AdvancePhase` + `GMAwardEvaluator.AdvanceToNextSeasonYear` + `GMFrontOffice.OnNewSeason` |
| `RosterManager` | 실제 존재(레거시) / 단장 모드는 `GMTeamState.Roster` | `Managers/RosterManager.cs` |
| `ContractManager` | 존재하지 않음 | 계약 로직은 `GMStoveLeagueMarket`에 분산 |
| `TransactionManager` | 존재하지 않음 | `GMStoveLeagueMarket` 실행 함수 + 뉴스 |
| `EventLogger` | 유사 구조 | `GMLeagueState.AddNews` |
| `SeasonState` | 유사 구조 | `GMLeagueState`(Phase · SeasonYear · GamesPlayed) |
| `PlayerState` | 유사 구조 | `Player` GM 필드(Age · Salary · ContractYears · 부상 등) |

## 3. 현재 시즌·스토브리그 흐름

```
GMRosterLoader.LoadModeRoster(mode, team)          Phase = StoveLeague, SeasonYear = 2026
  └ 10구단 28인 · FA 30명 · 드래프트 풀 10명 · 구단주/목표 생성
[스토브리그] 허브 서브 탭에서 Extend / Release / OfferContract / ExecuteTrade / AcceptOffer / Draft 를 **순서 제약 없이** 호출
[✔ 진행하기] GMOotpFrontOfficeUIController.Continue() 또는 GMLiveSeasonSimulator.StartRun()
  └ Phase StoveLeague → RegularSeason (GMLiveSeasonSimulator.cs:138 / 허브 :1926)
144경기 진행 → Phase = PostSeason (GMLiveSeasonSimulator.cs:204)
GMAwardEvaluator.AdvanceToNextSeasonYear(sim)
  ├ CompleteSeasonEvents: 포스트시즌 → KBO 시상식 → 골든글러브
  ├ 스토리: 최하위 연속 · 구단주 압박 갱신
  ├ GMFrontOffice.OnSeasonCompleted: 10구단 이력 · 신임도 · 엔딩
  ├ Phase = AwardsCeremony → GMLeagueState.AdvancePhase(): 연도 +1, 나이 +1, ContractYears -1, 부상 0, 기록 리셋
  └ GMFrontOffice.OnNewSeason: FA · 트레이드 · 드래프트 연간 카운터 0, 목표 · 안건 재오픈
```

- **확인된 사실:** 협상 함수 어디에도 `league.Phase` 검사가 없다 → 정규시즌 중에도 FA 영입 · 트레이드 · 지명이 가능하다(허브 UI도 막지 않음).
- 연도 전환 시 `ContractYears`가 0이 된 선수는 **그대로 로스터에 남는다**(FA 시장으로 나가지 않음, `GMDiagnosticView.cs:172`는 개수만 표시).
- AI 구단은 스토브리그 거래를 하지 않는다. 시즌 중 "트레이드 소문"은 뉴스 문구뿐이다(`GMLiveSeasonSimulator.cs:993~997`).
- FA 풀 · 드래프트 풀은 **리그 생성 시 1회만** 만든다. 새 시즌에 다시 채우는 코드가 없다(`OnNewSeason`에 없음).

## 4. 규정 매핑표

| Rule ID | 제어 대상 | 게임 단순화 규정 | 매핑 상태 | 근거 파일 | 충돌 여부 | 비고 |
|---|---|---|---|---|---|---|
| 001 FA 자격 | 8 · 7시즌, 145일, 다년 유보, 재취득 | 자동 판정 | **MISSING** | `Player.cs:292` (`ContractYears` 0 = "FA 대상"이라는 주석뿐) | - | 서비스 타임 · 등록일수 데이터 없음 |
| 001 FA 등급 · 보상 | A/B/C, 보호 20·25, 35세 특례, 재자격 | 자동 산정 + 보상 방식 선택 | **MISSING** | `GMStoveLeagueMarket.OfferContract()` | - | 영입 시 원 소속 보상 없음 |
| 001 FA 교섭 | 전 구단 동시 교섭, 경쟁 | 경쟁 입찰 | **PARTIAL** | `GMStoveLeagueMarket.RivalBid()` (해시 0.88~1.08) | - | 실제 AI 구단 입찰 아님, AI는 FA를 영입하지 않음 |
| 001 FA 획득 한도 | 행사자 수별 1~4명 | BLOCKED | **CONFLICT** | `GMFrontOffice.CanSignFreeAgent()` · `HouseRuleMaxFA`(0 · 3 · 1) | C-04 | 하우스 룰(게임 오리지널)과 규정 한도가 다른 축 |
| 001 FA 1년 양도 금지 | 외부 FA 1년 | BLOCKED | **MISSING** | `GMStoveLeagueMarket.Evaluate()` | - | |
| 001 계약 만료 → FA 시장 | 만료자 시장 이동 | 자동 | **MISSING** | `GMLeagueState.AdvancePhase()` | C-06 | 0년 계약자가 로스터에 남음 |
| 002 경쟁균형세 산정 | 상위 40명 · 외국인/신인 제외 · 예외 선수 | 자동 | **CONFLICT** | `GMTeamState.Payroll`(28인 전원 합), `GMRosterLoader.DefaultPayrollCap = 1370000` | C-01, C-02 | 캡이 2025년 값 · 28인 전원 기준 |
| 002 초과 제재 | 30/50/100% + 1R 9단계 | PENALTY | **MISSING** | `GMFrontOffice.Budget()` | - | 캡은 "예산 보전 한도"로만 쓰임 |
| 002 하한액(2027~) | 미달 제재 | PENALTY | **MISSING** | - | - | |
| 003 2차 드래프트 | 격년 · 35인 보호 · 양도금 · 의무 등록 | R1 지명 / R2 정산 | **MISSING** | - | - | 로스터 28인으로는 35인 보호 불가 → **NEEDS_DESIGN**(DECISION-004) |
| 004 포스팅 | 7시즌 · 연 1명 · 이적료 구간 | 이벤트 | **MISSING** | - | - | 소속 `OVERSEAS` 개념 없음 |
| 005 트레이드 기본 | 선수 교환 | R1 | **PARTIAL** | `GMStoveLeagueMarket.Evaluate/ExecuteTrade` | - | 1:1 · 2:2만, 지명권 · 이사비 · 연봉 승계 없음(연봉은 선수에 붙어 이동) |
| 005 트레이드 기간 | 포스트시즌 종료 다음 날 ~ 7/31 | BLOCKED | **CONFLICT** | 위 함수에 Phase · 날짜 검사 없음 / 레거시 `LeaguePhase.REGULAR_LOCKED`(73경기~) | C-03 | 단장 모드는 시즌 내내 가능 |
| 005 트레이드 횟수 | 규정상 제한 없음 | - | **CONFLICT(경미)** | `GMFrontOffice.CanTrade()` 하우스 룰 | C-04 | 하우스 룰은 선택 옵션이라 유지 가능 |
| 006 신인드래프트 | 11라운드 · 성적 역순 · 대졸 의무 | R1 + 자동 위임 | **PARTIAL** | `GMStoveLeagueMarket.Draft()`, `DraftPoolSize = 10`, `MaxDraftPicksPerYear = 2` | C-05 | 순번 · 라운드 · AI 지명 없음, 풀 재생성 없음 |
| 006 신인 1년 양도 금지 | | BLOCKED | **MISSING** | | | |
| 007 보류선수 · 방출 | 11/25 명단 · 11/30 자유계약 · 1년 재등록 금지 | R1 | **PARTIAL** | `GMStoveLeagueMarket.Release()` | C-07 | 위약금 50%(게임 오리지널), 방출 선수 즉시 FA 풀(규정상 자유계약 · 보상 없음과 구분 안 됨), 재영입 제한 없음 |
| 008 외국인 · 아시아쿼터 | 3 + 1 슬롯, USD 상한 | R1 + BLOCKED/PENALTY | **MISSING**(단장 모드) | 레거시 `StoveLeagueRules`(이름이 한국 성씨가 아니면 외국인으로 추정) | - | 국적 데이터 없음 |
| 009 선수 정원 · 1군 | 68 / 29(27) / 9월 34(32) | R2 | **CONFLICT** | `GMRosterLoader.BatterCount 15 · PitcherCount 13`, `GMStoveLeagueMarket.RosterMax = 28` | C-02 | 1군/퓨처스 구분 없음 |
| 009 부상자 명단 | 10 · 15 · 30일, 시즌 30일, 등록일수 인정 | R2 | **PARTIAL** | `GMLiveSeasonSimulator.Injure()` | - | 일수 카운트만 있음 |
| 010 시즌 전환 | 결산 → 8 Turn → 개막 | R2 | **PARTIAL** | `GMLeagueState.AdvancePhase()`, `GMSeasonPhase` | C-08 | 스토브리그가 단일 단계 |
| 011 웨이버 | 7일 · 300만 원 | R2 | **MISSING** | - | - | |
| 012 자유계약 · 임의해지 | | R2 | **MISSING** | - | - | |
| 013 최저연봉 | 2026 3,000만 / 2027~ 3,300만 | R2 | **PARTIAL** | `Player.MinSalary = 3000` | C-09 | 연도별 변경 없음 |
| 013 최고연봉 · 계약 연수 | 규정상 상한 없음 | - | **NEEDS_DESIGN** | `Player.MaxSalary = 200000`, `MaxContractYears = 5`, `MaxFAYears = 4` | - | 게임 밸런스 상수, 현실 다년계약(6년+)과 차이 |
| 014 연봉 중재 | 1/10 신청 · 3년 이상 | R2 | **MISSING** | - | - | |
| 015 PS 엔트리 | 7/31 소속 · 30명(28명) | R2 | **UNKNOWN** | `GMAwardEvaluator.RunPostseason` 미정독 | - | 다음 단계에서 확인 |
| 016 육성선수 | 정원 외 | - | **NEEDS_DESIGN** | - | - | DECISION-004와 함께 |

## 5. 데이터 구조 발견 결과

- **실제 존재:** 나이(`Age`, 데뷔 연도로 추정 21세 + 경력), 연봉(만 원), 잔여 계약(0~5), 자존심 · 성향 · 만족도 · 주장, 부상 잔여 일수, 수상 이력,
  구단 운영 자금 · 캡, 연간 FA/트레이드/드래프트 횟수, 구단 시즌 이력(승률 · 순위 · 관중 · 우승).
- **유사 데이터:** 소속(로스터 리스트 소속 여부로만 표현), 거래 이력(뉴스 문장), 서비스 타임(데뷔 연도로 나이만 추정).
- **존재하지 않는 데이터:** 생년(출생연도), 국적(외국인 · 아시아쿼터), 4년제 대졸 여부, 최초 등록 연도 · 입단 연차, 시즌별 등록일수 · 인정 시즌 수,
  FA 횟수 · 등급, 최근 3년 연봉 이력, 계약 유형(FA/비FA 다년/신인/외국인), 계약금 · 옵션 분리, 1군/퓨처스 등록 상태, 양도 금지 · 재등록 금지 타이머,
  신인 지명권(소유 · 순번), 경쟁균형세 연속 초과 횟수 · 예외 선수, 구조화된 거래 이력.
- **저장되지 않는 중요 상태:** 거래 이력(120건 뉴스 한도 밖은 사라짐), FA 영입 시 원 소속 정보(영입 후 추적 불가).
- **데이터 출처 후보:** `docs/KBO_books/2026_KBO_가이드북.pdf` 선수 프로필(생년월일 · 입단 · 계약금 · FA 계약 현황 · 3년 연봉 · 국적 · 지명 순위)과
  구단별 IN/OUT(2025 오프시즌 FA · 2차 드래프트 · 자유계약 이동). `players.csv`는 621행, 성적 Z-score 중심이라 계약 · 신상 필드가 없다.

## 6. 기존 코드 충돌

| 충돌 ID | 기존 로직 | 새 규정 요구 | 충돌 내용 | 영향 범위 | 해결 방향 |
|---|---|---|---|---|---|
| C-01 | `DefaultPayrollCap = 1370000`(137억, 2025년 값) | 2026 143억 9,723만 → 연도별 | 연도 고정 · 값 구버전 | 재정 화면, 구단주 페이롤 목표, 스토리 예산 | 연도별 규정 테이블에서 읽기 |
| C-02 | `Payroll` = 28인 전원 연봉 합, 로스터 28인 | 상위 40명(외국인 · 신인 제외), 1군 29 · 소속 68 | 대상 집합 · 인원 불일치 | TeamChemistryEngine 페이롤 편중(⑤), 재정, 로더, 경기 엔진 라인업 | DECISION-004 결정 후 "경쟁균형세 기준액"을 별도 계산값으로 분리 |
| C-03 | 협상 함수에 Phase 검사 없음 | 트레이드 7/31 마감, FA · 보류 · 드래프트는 오프시즌 | 시즌 중 FA 영입 · 지명 가능 | 허브 UI, GM06 테스트 | Turn/날짜 게이트 함수 1곳에서 판정 |
| C-04 | 하우스 룰 연간 FA · 트레이드 한도 | 규정 FA 획득 한도(1~4명), 트레이드 횟수 무제한 | 두 한도의 의미가 섞일 수 있음 | `GMFrontOffice.CanSignFreeAgent` | 규정 한도(항상 적용) + 하우스 룰(선택 추가 제한) 2층으로 분리 |
| C-05 | 드래프트 풀 10명 · 연 2명 · 순번 없음 | 11라운드 · 성적 역순 · 대졸 의무 | 규모 · 순서 불일치 | 드래프트 탭, 풀 생성 | 라운드 · 순번 모델 + 자동 위임 |
| C-06 | 연도 전환 시 `ContractYears` -1만, 만료자 잔류 | 만료자 = FA 자격 또는 보류(재계약 교섭) | 시장 순환이 없음 | 2년차 이후 FA 시장 고갈 | 결산 Turn에서 만료자 분류(FA 자격 / 보류 / 자유계약) |
| C-07 | 방출 위약금 = 잔여 연봉 50%, 즉시 FA 풀 | 보류 제외 → 11/30 자유계약(보상 없음), 원 소속 1년 재등록 금지 | 비용 · 신분 · 시점 차이 | 방출 버튼 | 시점(Turn 4)과 신분(`FREE_CONTRACT`) 분리, 위약금은 다년 보장 잔여분 기준으로 재정의 |
| C-08 | `GMSeasonPhase.StoveLeague` 단일 단계 | 8 Turn | 저장 enum 확장 필요 | 세이브, 허브 진행 버튼 | 스토브리그 하위 Turn 필드 추가(enum 값 재배치 금지) |
| C-09 | `Player.MinSalary = 3000` 상수 | 2027부터 3,300 | 연도 고정 | 재계약 · 신인 · 연봉 산식 | 규정 테이블 |
| C-10 | `MaxFAYears = 4`, `MaxContractYears = 5` | 규정상 상한 없음(현실 4+2, 6년 등) | 표현 범위 제한 | FA 협상 | NEEDS_DESIGN(밸런스 결정) |
| C-11 | 레거시 `LeaguePhase.REGULAR_LOCKED`(73경기~ 트레이드 마감) | 7/31 | 레거시 카드 모드 규칙 | 레거시 모드만 | 단장 모드에서는 쓰지 않음(수정 대상 아님) |

## 7. 설계 수준 변경안 (코드 작성 금지)

- **규정 테이블(신규 데이터):** 연도별 공식 값 한 곳. 코드 상수(`DefaultPayrollCap`, `MinSalary`, `RosterMax`)는 이 테이블을 읽도록 단계적으로 대체.
- **선수(`Player` 확장 · 재사용):** 신상 · 서비스 블록(출생연도 · 국적 · 대졸 · 최초 등록 연도 · 인정 시즌 · 시즌 등록일수 · FA 횟수/등급 · 3년 연봉) 추가.
  상태 4축 + 제약 타이머는 `Player`에 작은 필드로 추가하고, 소속은 기존 "어느 리스트에 있는가"를 1차 진실로 유지(이중 진실 방지).
- **계약(분리 검토):** 현재 `Salary` · `ContractYears`를 유지하되, 계약 유형 · 계약금 총액 · 옵션 · 양도 금지 만료를 묶은 계약 레코드를 선수에 1개 붙이는 방식 권장(별도 매니저 클래스 불필요).
- **구단(`GMTeamState` 확장):** 경쟁균형세 기준액(계산값) · 예외 선수 · 연속 초과 횟수 · 외국인 슬롯 · 지명권 목록. 퓨처스/뎁스 풀은 DECISION-004 결과에 따름.
- **거래(재사용 + 신규 기록):** 실행은 `GMStoveLeagueMarket`에 두고, 성공 시 구조화된 거래 이력 1건을 남긴다(뉴스는 그 이력에서 생성).
- **시즌(재사용):** `GMSeasonPhase`는 유지, 스토브리그 안에 `OffseasonTurn`(1~8) 하위 필드 추가. 결산은 `AdvanceToNextSeasonYear` 흐름 안에 Turn 1 처리로 편입.
  게이트 판정(이 행동이 지금 가능한가)은 한 함수로 모아 UI · AI · 테스트가 공유.
- **AI 구단:** FA 입찰 · 보상선수 선택 · 2차/신인 지명 · 보류 결정을 단순 규칙으로 수행(현재 없음). "경쟁 구단 최고 입찰" 해시를 실제 AI 입찰로 대체.

## 8. 저장·마이그레이션 위험

- 현재 세이브 v17, JsonUtility **필드 존재 기반** 하위 호환(`SaveManager.cs:154` 주석). 새 필드는 기본값으로 채워지므로 추가는 비교적 안전하다.
- **위험 1:** `GMSeasonPhase` enum 정수 값이 저장된다 - 중간에 값을 끼워 넣으면 기존 세이브 단계가 어긋난다 → 새 값은 뒤에 추가하거나 별도 필드 사용.
- **위험 2:** 구 세이브 선수에 서비스 타임 · 생년 · 국적이 없다 → 로드 시 추정(데뷔 연도 · 이름 규칙)하고 "추정치" 플래그를 남겨야 한다.
- **위험 3:** 로스터를 28 → 29(+ 퓨처스)로 넓히면 구 세이브는 인원이 모자란다 → 로드 시 보충 규칙 필요.
- **위험 4:** 경쟁균형세 연속 초과 횟수 · 지명권 하락은 해를 넘겨 누적되는 상태라 저장 누락 시 제재가 초기화된다.
- **위험 5:** 거래 이력을 무제한 저장하면 세이브 크기가 커진다 → 시즌 단위 요약 보관 정책 필요.

## 9. 테스트 현황

- **위치 · 형식:** `Assets/Scripts/Editor/Tests`(EditMode, `KBOManager.EditorTests`), 20개 파일 · `[Test]` 194개.
- **관련 테스트:** `GM06VerificationRunner.T2_StoveNegotiations_Extension_FA_ShopTrade_Draft`(재계약 · FA · Shop 트레이드 · 드래프트),
  같은 파일 :501~512(새 시즌 이후 방출 · FA · 하우스 룰 트레이드), `GM01VerificationRunner`(Phase 전환 · 해 넘김 :225~227),
  `GM04VerificationRunner`(2027 시즌 전환 버튼), `Task190Tests`(레거시 시즌 순환), `Task193Tests`(레거시 스토브리그).
- **실행 방법:** Unity CLI Batchmode EditMode(프로젝트 규칙: 태스크당 1회). 이번 조사에서는 **실행하지 않았다** - 현재 통과/실패 상태 미확인.
- **CI:** `.github` 없음(CI 미구성).
- **규정 테스트 공백:** FA 자격 · 등급 · 보상, 경쟁균형세, 트레이드 기간, 외국인 슬롯, 2차 드래프트 - 해당 테스트 없음(기능 자체가 없음).
- **주의:** 협상 게이트(C-03)를 넣으면 정규시즌 중 협상을 호출하는 기존 테스트가 깨질 수 있다 → 테스트 픽스처의 단계 설정 확인 필요.

## 10. 위험 요소

1. **로스터 규모 결정(DECISION-004)이 거의 모든 규정의 전제다.** 보호 인원(20 · 25 · 35), 정원(68 · 63), 엔트리(29)가 여기에 묶여 있다.
2. **데이터 부족:** FA 등급 · 서비스 타임 · 외국인 판정에 필요한 필드가 없다. 가이드북 프로필을 표로 옮기는 작업량이 크다(10구단 × 60여 명).
3. **올타임 드림 모드:** 시대가 섞인 선수에게 "2026 서비스 타임 · FA 등급"을 매기기 어렵다 → 모드별 규정 적용 범위 결정 필요.
4. **밸런스:** 현실 보상 · 경쟁균형세를 넣으면 현재 FA 영입 체감(점수 비교 한 번)이 크게 무거워진다 → 자동 처리 · 추천안이 반드시 같이 들어가야 피로도가 오르지 않는다.
5. **치어리더 축 보존:** 예산 구조(운영 자금 · 응원단 홈 수익)를 경쟁균형세와 섞을 때 치어리더 예산이 줄어드는 방향의 변경은 하지 않는다(프로젝트 CLAUDE.md 7절).
6. **레거시 코드 혼동:** `StoveLeagueRules` · `StoveLeagueManager` · `LeaguePhase`는 카드 모드 시스템이다. 단장 모드 규정 구현 근거로 착각하지 않도록 한다.

## 11. 기획자 결정 필요 항목

1. **로스터 규모**(DECISION-004): (A) 1군 29 + 퓨처스 68 / (B) 29 + 보호 인원 비율 축소 / (C) 1군 29 + 퓨처스 핵심 뎁스 풀 + 익명 육성 슬롯 - 권장 (C)
2. **8 Turn 순서**(DECISION-005): 현실 달력 순(FA 공시 → FA → 2차 드래프트 → 보류 → 외국인 → 계약 · 경쟁균형세 → 중재 → 등록) 채택 여부
3. **신인드래프트 시기**(DECISION-006): 시즌 중 9월 팝업 vs 스토브리그 턴
4. **경쟁균형세 부과 시점**(DECISION-003 개정): 계약 즉시 차감 vs 즉시 표시 · 결산 부과
5. **하우스 룰 존속:** 규정 FA 한도 위에 기존 하우스 룰(연간 FA · 트레이드 횟수)을 선택 옵션으로 남길지
6. **방출 위약금:** 현재 잔여 연봉 50%를 유지할지, 다년 보장 잔여분만 부담하는 현실 방식으로 바꿀지
7. **올타임 드림 모드 적용 범위:** 서비스 타임 · FA 등급 · 외국인 규정을 그대로 적용할지, 간이 규칙으로 둘지
8. **선수 데이터 확장 출처**(DECISION-008): 가이드북 프로필 수작업 정리 범위(현역 2026 모드만?)
9. **계약 기간 · 연봉 상한(C-10):** FA 4년 · 계약 5년 · 연봉 20억 상한 유지 여부
10. **AI 구단 오프시즌 행동 수준:** FA 입찰 · 보상선수 · 지명을 AI가 실제로 할지(현실감 ↑, 시뮬레이션 비용 ↑)

## 12. 구현 순서 제안 (코드 수정 없이 순서만)

1. 연도별 규정 테이블 + 상수 대체(C-01 · C-09) - 다른 모든 작업의 기반
2. 선수 신상 · 서비스 타임 데이터 확장 + 구 세이브 추정 로드(세이브 v18)
3. 로스터 규모 확장(DECISION-004 결과) + 1군 29명 엔트리 · 부상자 명단
4. 스토브리그 8 Turn + 협상 게이트 함수(C-03 · C-08) + 만료자 시장 순환(C-06)
5. FA 자격 · 등급 · 보상 · 획득 한도 · AI 입찰(001)
6. 경쟁균형세 산정 · 예외 선수 · 결산 제재(002)
7. 보류명단 · 자유계약 · 재등록 금지(007 · 012) + 거래 이력 구조화
8. 트레이드 기간 · 지명권 교환 · 개별 양도 금지 · 이사비(005)
9. 외국인 · 아시아쿼터(008)
10. 신인드래프트 11라운드 · 순번 · AI 지명(006)
11. 2차 드래프트(003, 첫 실시 2027 오프시즌)
12. 포스팅 이벤트(004) → 연봉 중재(014) → 웨이버(011)

각 단계는 프로젝트 규칙대로 TASK-GM-NN 지시서 1건 · Batchmode 검증 1회 단위로 끊는다.

## 13. 작업 범위 판정

| 영역 | 판정 |
|---|---|
| 규정 테이블 · 연도별 상수 · 트레이드 기간 게이트 · 최저연봉 | **현재 구조로 구현 가능** |
| FA 자격 · 등급 · 보상, 경쟁균형세, 외국인, 보류 · 자유계약, 거래 이력 | **데이터 확장 필요** |
| 1군/퓨처스 로스터, 2차 드래프트, 8 Turn 스토브리그, AI 구단 오프시즌 | **재설계 필요**(DECISION-004 · 005 결정 후) |
| 포스트시즌 엔트리 자격 | **판단 불가**(관련 코드 미정독) |

## 14. 조사 한계

- 테스트를 실행하지 않아 현재 통과 여부를 모른다.
- `GMOotpFrontOfficeUIController.cs`(2,016줄) · `GMLiveSeasonSimulator.cs`(1,086줄) · `GMAwardEvaluator.cs`(1,002줄)는 관련 구간만 읽었다.
- 씬 · 프리팹에 직렬화된 값(인스펙터 오버라이드)은 확인하지 않았다.
- 규약 PDF 일부 페이지는 텍스트 추출 시 서체 문제로 문자가 깨졌다. 판정에 쓴 조항은 모두 정상 추출된 본문으로 재확인했지만, 표 형식(부록 양식)은 확인하지 않았다.
- 시즌별 공시 날짜(FA 공시일 · 2차 드래프트일 · 신인드래프트일 · 개막일)는 규정집에 없어 `season_announced`로 남겼다.

## 15. 승인 대기

사전 조사 완료.
코드 수정 없음.
신규 코드 파일 생성 없음(규정 · 설계 · 인계 문서 10종과 이 보고서만 작성).
사용자 승인 대기.
