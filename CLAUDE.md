# CLAUDE.md — 『스토브리그: 단장의 시간』 프로젝트 규칙

> **최우선 규칙 문서.** 이 파일은 2026-10-05부로 확정된 프로젝트 개편 방향을 정의한다.
> `docs/` 아래 기존 문서(특히 TASK-KBO-168~193 시기의 카드 강화·각성·세트덱 규칙)와 내용이 충돌하면
> **항상 이 파일이 우선한다.** 전역 규칙(`~/.claude/CLAUDE.md`, Git 자동 커밋 등)은 그대로 함께 적용된다.

## 1. 프로젝트 정체성 (개편)

- **게임명:** 『스토브리그: 단장의 시간』 (舊 가제 "스토브리그: 단장의 계절"은 폐기)
- **장르:** KBO 단장(GM) 시뮬레이션. **카드 수집형 RPG가 아니다.**
- **플레이어 역할:** 실명 KBO 구단의 단장으로서 스토브리그 영입·트레이드·예산 운영으로 로스터를 짜고,
  144경기 정규시즌을 실시간 대시보드로 지켜보며 결과로 평가받는다.
- **핵심 루프:** 스토브리그(로스터 구성) → 144경기 정규시즌(실시간 대시보드 관전) → 포스트시즌
  → 7대 시상식 → 다음 시즌 스토브리그.
- 엔진: Unity (기존 프로젝트 유지). 폰트: `Assets/Fonts/KBODiaGothic`.

## 2. 핵심 시스템 (새 기준)

### 2.1 KBO 실명 DB
- 선수·구단은 KBO 실명 데이터베이스(`players.csv` 등 기존 CSV 파이프라인, `PlayerDatabase`)를 기반으로 한다.
- 선수 능력치는 실제 기록 기반(Z-score 변환 등)으로 산출되며, **유저 행동(강화·각성 등)으로 능력치를
  인위적으로 올리는 구조는 두지 않는다.**

### 2.2 단장 스토브리그
- 비시즌의 중심 콘텐츠. FA 영입, 트레이드, 신인 드래프트, 외국인 선수 영입, 예산/샐러리 운영을 다룬다.
- TASK-KBO-193의 스토브리그 4대 시스템(`StoveLeagueRules`/`StoveLeagueView`)은 이 방향과 일치하므로
  **재사용 대상**이다. 단, 그 안에 남아 있는 카드 등급 조건(라이브 에픽~타이틀 홀더 등)·"카드 장수" 표현은
  선수 단위 개념으로 정리해야 한다.

### 2.3 144경기 실시간 대시보드
- 정규시즌은 144경기 체제. 유저는 경기를 직접 조작하지 않고 **실시간 대시보드**(순위표, 일정, 진행 중 경기
  스코어, 개인/팀 기록, 구단 재정 등)로 시즌 흐름을 관전·관리한다.
- 경기 시뮬레이션은 기존 `MatchEngine`/`LeagueManager`를 계속 사용한다.

### 2.4 7대 시상식
- 시즌 종료 후 7개 부문 시상식을 진행한다. 기존 "타이틀 시상식"(TASK-KBO-190)을 이 체계로 대체·확장한다.
- 7개 부문의 정확한 구성은 기획 확정 시 이 절에 기재한다. **임의로 부문을 정하지 말고 필요 시 사용자에게 확인할 것.**

### 2.5 치어리더 15인 / 경기 엔트리 4~6인
- 구단 치어리더 풀은 **15인 체제**. 경기마다 **4~6인을 엔트리**로 선택해 운용한다.
- 치어리더는 가챠로 뽑는 카드가 아니라 구단 소속 인원이다(치어리더 가챠·응원봉 재화 체계는 비활성화).

## 3. 비활성화(폐기)된 시스템 — 다시 만들거나 확장하지 말 것

| 시스템 | 상태 | 관련 코드/문서(참고용) |
| --- | --- | --- |
| 선수 강화(+10강, EXP·카드 재료) | ❌ 비활성화 | `UpgradeManager.TryEnhance()`, `UpgradeConstants.cs`, `UpgradeProbabilityDB.cs`, `EnhanceUIController`, `MaterialSelectUIController` |
| 선수 각성(1~10각, 9각 등 각성 규칙) | ❌ 비활성화 | `UpgradeManager.TryAwaken()`, `GrowthCenterView` 각성 사다리 |
| 선수 초월 | ❌ 비활성화 | TASK-KBO-189 초월 복합 재료 |
| 선수/치어리더 가챠(스카우트 뽑기) | ❌ 비활성화 | `ScoutManager`, `ScoutDropTables.cs`, `CheerleaderGachaService`, `ScoutUIController`, `ScoutHubUIController`, `CheerleaderShopUIController`, 가챠 재화(영입권·트로피·싸인볼·응원봉) |
| 세트덱(구단 통일) 보너스 | ❌ 비활성화 | `Player.CalculateOVR(isSetDeckBonusActive, …)`, `SetDeckOptionUIController`, `TeamSynergyUIController` |
| 카드 등급 기반 수집(라이브~왕조 8등급), 특별 영입·포지션 재조합·방출 재료화 | ❌ 비활성화 | `docs/04_card_grade_policy.md`, `SpecialRecruitView`, TASK-KBO-189 재조합/교환소 |
| 스킬 변경권·레벨업 등 카드 RPG 성장 재화 | ❌ 비활성화 | TASK-KBO-190 스킬 변경권 상점·보상 |

- "비활성화"는 **기능을 게임 흐름에서 제거(진입점 차단·UI 숨김)**한다는 뜻이다. 실제 코드 삭제·정리는
  사용자가 지시한 TASK에서만 수행한다(세이브 호환·테스트 영향 확인 후).
- 위 시스템에 대한 버그 수정·밸런스 조정·UI 개선 요청이 오면, 개편 방향과 충돌한다는 점을 먼저 알리고 확인받는다.

## 4. UI / 폰트 규칙

- **Bold 금지.** 모든 UI 텍스트는 `FontStyle.Normal`(TMP는 `FontStyles.Normal`)을 유지한다.
  - `FontStyle.Bold`/`BoldAndItalic` 지정, 리치 텍스트 `<b>` 태그, Bold 서체 에셋 사용 모두 금지.
  - 강조가 필요하면 크기·색상·배경으로 구분한다.
- 기존 텍스트 정리 기준(TASK-KBO-191~193 `TextTidy`/`TextFit193`)을 유지한다: 크기 계층(보조 16~18 · 본문 19~21 ·
  탭 20~22 · 섹션 24~26 · 타이틀/실행 버튼 26~30 · 대형 수치 34~42), Best Fit 최소 15pt, 자간 2.0.
- 유저 노출 문자열에 영문 enum(예: `SecondBase`)을 그대로 쓰지 말고 KBO 한글 약칭을 사용한다.

## 5. 기존 문서 취급 (레거시)

아래 문서는 카드 수집형 RPG 시절 기준으로 작성되었으며 **역사 기록으로만 보존**한다. 새 작업의 근거로 쓰지 않는다.

- `docs/04_card_grade_policy.md` — 카드 등급·강화·각성 규칙 (레거시)
- `docs/09_probability_policy.md`, `docs/10_probability_tables.md` — 가챠/강화 확률 (레거시)
- `docs/16_shop_and_gacha_policy.md` — 상점·가챠 (레거시)
- `docs/15_team_power_policy.md` — 세트덱 보너스 관련 부분 (레거시)
- `docs/00_project_overview.md` 4.5~4.7절, 5절의 강화/각성/가챠/세트덱 항목 (레거시)
- `docs/13_decision_change_log.md` — TASK-KBO-168~193의 강화(+10)·각성(9각)·초월·세트덱·가챠 결정 (레거시)

여전히 유효하게 참고할 수 있는 것: 선수 데이터 파이프라인(`docs/11_data_dictionary.md`, `docs/18_player_schema_policy.md`의
스키마·Z-score 부분), 매치 엔진(`docs/06_match_engine_formula.md`), 리그 페이즈 구조(`LeaguePhase`), 28인 로스터·샐러리 캡.

새 방향의 결정 사항은 `docs/13_decision_change_log.md`에 새 DCL 항목으로 추가하고, 이 파일도 함께 갱신한다.

## 6. 작업 방식

- 응답·완료 보고는 한국어 존댓말로 작성한다.
- 작업 단위는 기존처럼 `TASK-KBO-NNN` 번호를 사용한다(다음 번호: 194).
- 의미 있는 작업 단위가 끝나면 conventional commits 형식으로 커밋한다(push는 요청 시에만).
