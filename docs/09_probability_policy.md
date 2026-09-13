---
문서명: 게임 내 확률 정책
버전: v0.1
상태: Active
최종 수정일: YYYY-MM-DD
담당자: 김장윤
관련 파일: 10_probability_tables.md, probability_tables.csv
---

# 1. 목적

- 게임 내 존재하는 모든 확률(가챠, FA 시장, 매치 엔진 등)을 중앙 통제하고, 투명한 운영 및 법적 가이드라인 준수를 위한 내부 정책을 정의한다.

# 2. 확률 요소 목록 및 유형 분류

1. **상점 / 스카우트 (공개 필수):** 유료 재화(또는 유료로 전환 가능한 재화)를 사용하는 카드 뽑기.
2. **FA 시장 등장 (공개/내부):** 스토브리그 FA 마켓 갱신 시 특정 등급 선수가 매대에 올라올 확률.
3. **경기 시뮬레이션 난수 (내부용):** 매치 엔진의 타석 결과(Hit/Out/HR 등).
4. **스킬 리롤 (공개 필수):** 스킬 변경권 사용 시 S~F 등급 스킬이 등장할 확률.

# 3. 확률 통제 및 천장(Pity) 규칙

- **하드 피티 (천장):** `N`회 연속 시도 시 최고 등급(또는 픽업 대상)을 100% 확정 지급하는 로직을 CSV의 `pity_rule_id`로 연결하여 팩토리 클래스에서 강제 적용한다.
- **풀(Pool) 오염 방지:** 천장 도달 시 발동하는 확정 슬롯(예: 10연뽑의 마지막 1장)은 원본 난수 테이블(9장)의 확률에 영향을 주지 않고 독립적으로 렌더링되어야 한다.
- **미활성 등급 배제:** v0.1에서 왕조(Dynasty) 등급은 코드에 존재하더라도 `active_flag = false` 처리되어 절대 확률 풀(Draw Box)에 편입되지 않는다.

# 4. 확률 변경 프로세스 (Patch Process)

- **변경 제안:** 기획자(단장) 발의 → `12_balance_test_log.md`에 시뮬레이션 10,000회 결과 첨부.
- **버전 확정:** `probability_tables.csv` 데이터 업데이트 및 `change_reason` 작성.
- **공개표 반영:** 런칭 버전(v1.0) 이후부터는 업데이트 노트 및 인게임 확률 정보 UI와 CSV가 1:1로 동기화되도록 연동한다.

# 5. 치어리더 가챠 확률 검증 [TASK-KBO-073]

- **목표 확률**(`CheerleaderGachaService.RollGrade()`, `docs/16_shop_and_gacha_policy.md` 3절): `NORMAL` 70% / `RARE` 22% / `EPIC` 7% / `LEGEND` 1%(합계 100%).
- **검증 도구**: `Assets/Scripts/Editor/GachaSimulationMenu.cs`의 `KBO Manager/Debug/Simulate 10,000x Gacha` 메뉴. 실제 `CheerleaderGachaService`/`GameManager`를 전혀 호출하지 않는 독립 Mocking 방식이라 유저의 `PremiumCurrency`/`OwnedCheerleaders`는 전혀 건드리지 않고, 확률 판정 로직만 동일하게 복사해 10,000회 표본에서 등급별 비율이 목표치에 수렴하는지 콘솔 로그로 확인한다.
- **[Draft, 실제 실행 전 예시 - 실측값 아님]** 아래는 메뉴를 실제로 실행하기 전 참고용으로 작성한 더미 출력 형식 예시다:
  ```
  [GachaSimulationMenu] Total: 10000, NORMAL: 6980 (69.8%), RARE: 2205 (22.1%), EPIC: 705 (7.1%), LEGEND: 110 (1.1%)
  ```
- **판정 기준**: 표본 10,000 기준 목표 확률과의 오차가 대략 ±1~2%p 이내면 정상으로 간주한다(엄격한 통계적 유의성 검정은 이 문서 범위 밖). 오차가 지속적으로 크게 벗어나면 `CheerleaderGachaService.RollGrade()`의 누적 임계값과 `GachaSimulationMenu`의 임계값이 실제로 일치하는지부터 재확인할 것 - 두 값은 코드가 분리되어 있어(명령서 4항이 런타임 코드 침범 최소화를 위해 독립 Mocking을 지시) 한쪽만 바뀌면 조용히 어긋날 수 있다.
- **[TBD]** 실제 시뮬레이션 실행 결과는 아직 기록되지 않았다 - Unity Editor에서 메뉴를 실행한 뒤 이 절을 실측값으로 갱신할 것.
