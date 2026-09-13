---
문서명: 치어리더 상점/가챠(뽑기) 정책 [Draft]
버전: v0.2 (Draft)
상태: Draft
최종 수정일: 2026-09-13
담당자: 김장윤
관련 파일: Cheerleader.cs, GameManager.cs, ShopUIController.cs, ScoutManager.cs, docs/11_data_dictionary.md, docs/13_decision_change_log.md, docs/15_team_power_policy.md
---

# 0. 문서 성격

- 이 문서는 **[Draft]** 상태의 기획안이다. 여기 적힌 수치(등급별 버프량, 중복 처리 마일리지 등)는 대부분 확정이 아니며, 실제 밸런싱은 v0.2 구현 단계에서 시뮬레이션/플레이테스트를 거쳐 조정될 예정이다.
- **[TASK-KBO-066 갱신, 예외]** 3절(가챠 확률 및 재화)만은 예외다 - TASK-KBO-065/066에서 실제 `CheerleaderGachaService`/`CheerleaderShopUIController` 코드로 구현되어, 이제는 "제안"이 아니라 "코드와 1:1로 일치하는 현재 사양"이다. 이 문서 최초 작성(TASK-KBO-063) 시점에 제안했던 GameGold 이원화/할인/확정 슬롯 구조는 실제로 채택되지 않았고, 대신 더 단순한 단일 재화 규칙이 구현되었다 - 자세한 내용은 3절 참고.
- v0.1에서 이미 확정된 치어리더 B+C 하이브리드 엔진 정책(`docs/15_team_power_policy.md` 6~8절)과 세이브 아키텍처(`docs/11_data_dictionary.md` 7·9절)를 절대 깨지 않는 범위 안에서만 설계했다 - 자세한 정합성 확인은 6절 참고.

# 1. 목적

- v0.1에서 데이터·백엔드·UI까지 전부 완성됐지만 정작 "얻는 방법"이 없던 치어리더 시스템(`docs/13_decision_change_log.md` DCL-033 참고)에 정식 획득처(상점/가챠)를 부여한다.
- 과금 유도가 아니라 **스토브리그 로비를 통한 재화 선순환**에 초점을 맞춘다 - 신규 유료 재화를 새로 만들지 않고 기존 재화를 그대로 재사용한다. **[TASK-KBO-066 갱신]** 최초 초안(TASK-KBO-063)은 `GameGold`/`PremiumCurrency` 이원화를 제안했으나, 실제 구현(3절)은 `PremiumCurrency` 단일 재화로 단순화되었다 - `GameGold`는 치어리더 가챠에 관여하지 않는다.

# 2. 치어리더 정식 등급 체계 [Draft]

- 현재 `CheerleaderGrade` enum은 `NONE=0`, `TEST=1` 두 값뿐이다(`docs/11_data_dictionary.md` 7절). 아래는 v0.2에서 추가를 검토할 정식 등급 4종 제안이다.
- **[중요, 세이브 호환 주의]** `docs/11_data_dictionary.md` 7절이 이미 경고했듯, `Cheerleader`는 TASK-KBO-057부터 `GameSaveData`로 직렬화되는 대상이 되었다. 새 등급을 추가할 때는 기존 `NONE=0`/`TEST=1`을 절대 바꾸지 않고 **반드시 enum 끝에만 추가**해야 한다(`DCL-006`이 겪은 것과 동일한 재배치 사고 방지). 아래 ID 열은 이 제약을 반영해 2번부터 이어서 부여했다.

| ID [Draft] | 등급명 (Enum 제안) | 컨셉 | `ConditionBuff` [Draft] | `ClutchMultiplier` [Draft] | `EconomicBonusRate` [Draft] | `SentimentDefense` [Draft] |
| :-- | :--- | :--- | :-- | :-- | :-- | :-- |
| 2 | `NORMAL` (일반) | 가챠 최다 물량, 입문용 | +1 | 1.00 | 1.05 | 0 |
| 3 | `RARE` (희귀) | 무과금 주력, 성장 체감 시작 | +2 | 1.05 | 1.10 | 1 |
| 4 | `EPIC` (에픽) | 확정팩 기준선, 눈에 띄는 성능 | +3 | 1.10 | 1.15 | 2 |
| 5 | `LEGEND` (전설) | 최상위 희소성, 프리미엄 10연뽑 확정 대상 | +4 | 1.15 | 1.20 | 3 |

- 수치 설계 근거(**[Draft], 확정 아님**): 기존 개발용 더미 치어리더들(`GameManager.InitializeDevOnlyTestCheerleader()`의 `ConditionBuff=1`/`ClutchMultiplier=1.05`/`EconomicBonusRate=1.2`, `SetupCheerleaderUI.AddDummyCheerleaderToInventory()`의 `ConditionBuff=2`/`EconomicBonusRate=1.1`)이 이미 이 범위 안에 있어, 급격한 파워 인플레이션 없이 자연스럽게 이어지도록 설계했다.
- **[TBD, v0.2 확정 필요]** 정식 등급명(`NORMAL`/`RARE`/`EPIC`/`LEGEND`)과 개수(4단계)는 예시 제안이며, 실제 팀 브랜딩/카피라이팅에 맞춰 바뀔 수 있다.

# 3. 가챠 확률 및 재화 (v0.2 프로토타입 구현체 기준 - `[Draft]` 아님)

- **[TASK-KBO-066 갱신, SSOT 통일]** 이 절은 최초 작성(TASK-KBO-063) 당시 `GameGold`(일반)/`PremiumCurrency`(프리미엄) 이원화 + 10연뽑 할인 + 확정 슬롯 구조를 제안했으나, 실제 v0.2 프로토타입(`CheerleaderGachaService.RollGacha()`, TASK-KBO-065/066)은 훨씬 단순한 **단일 재화** 규칙으로 구현되었다. 아래 표는 그 실제 구현체와 1:1로 일치하도록 갱신한 것이며, 더 이상 "제안"이 아니라 "코드가 곧 사양"이다.

| 상품 | 소모 재화 | 비용 | 등급별 확률 | 비고 |
| :--- | :--- | :-- | :--- | :--- |
| 1회 뽑기 | `PremiumCurrency` | 100 | `NORMAL` 70% / `RARE` 22% / `EPIC` 7% / `LEGEND` 1% | 확률 합계 100%. `CheerleaderGachaService.RollGacha(1)` |
| 10회 뽑기 | `PremiumCurrency` | 1000 (할인 없음, `count * 100`과 동일 - `CheerleaderShopUIController`의 `roll10xButton`) | 위 확률표를 10회 독립 적용 | 확정(보장) 슬롯 없음. `CheerleaderGachaService.RollGacha(10)` |

- **확률 총합 검증**: 70 + 22 + 7 + 1 = 정확히 100%. 이 확률표는 TASK-KBO-065/066 양쪽 명령서 모두에서 "절대 변경 금지"로 명시되었으며, 실제 코드(`CheerleaderGachaService.RollGrade()`)와 동일하다.
- **[폐기됨] GameGold 이원화 및 확정 슬롯 구조**: 최초 초안이 제안했던 "일반 뽑기(`GameGold`)/프리미엄 뽑기(`PremiumCurrency`, 확정 슬롯 포함)" 2단계 구조와 10연뽑 10% 할인은 채택되지 않았다. `GameGold`는 치어리더 가챠에 전혀 관여하지 않는다(기존 스킬 변경권 구매 등 다른 용도는 그대로 유지). `ShopUIController.guaranteedPackagePrice`(300)/`premiumTenPullPrice`(1000)와의 가격 스케일 통일이라는 최초 의도도, 실제로는 10회 뽑기 비용이 우연히 `premiumTenPullPrice`(1000)와 같아지는 정도로만 남았다.
- **[TBD, v0.2 확정 필요]** 천장(하드 피티), 할인, 보장 슬롯 등은 여전히 미도입 상태다 - `09_probability_policy.md` 3절이 전사 공통 정책으로 이런 장치의 필요성을 열어 두고 있으나, 이번 프로토타입에는 포함되지 않았다. 도입 여부와 구체적 규칙은 후속 밸런싱 단계에서 재검토한다.

# 4. 중복 획득 처리 방안 [Draft]

- **문제 정의**: `Cheerleader`는 현재 원본(카탈로그) 참조 구조가 없다 - 선수 카드가 `PlayerTemplate`(원본, 불변) + `Player`(발급된 인스턴스, `InstanceId`)로 분리된 것과 달리, `Cheerleader`는 `InstanceId`/`Name`/버프 필드가 전부 한 클래스에 있다(`docs/11_data_dictionary.md` 7절). 따라서 "같은 치어리더를 또 뽑았다"를 판별하려면 v0.2에서 **원본 식별자(가칭 `CatalogId`, 예: `CHR_001`)를 `Cheerleader`에 새로 추가**해, `InstanceId`(발급된 개체 고유값, 세이브 null 판별에 쓰이는 기존 불변식)와 분리해야 한다. 이 문서는 그 분리를 전제로 아래 두 방안을 제안한다.
- **[불변식 재확인]** `docs/11_data_dictionary.md` 9절의 "치어리더 인스턴스는 항상 비어 있지 않은 `InstanceId`를 가진다" 불변식은 `CatalogId` 추가와 무관하게 그대로 유지되어야 한다 - 중복 판정은 `CatalogId` 비교로만 하고, `InstanceId`는 계속 개체별로 고유하게 새로 발급한다(세이브 로직 변경 불필요).

| 방안 [Draft] | 동작 | 장점 | 단점 |
| :--- | :--- | :--- | :--- |
| A안: 마일리지 전환 (권장) | 이미 보유한 `CatalogId`가 다시 뽑히면 `Cheerleader` 인스턴스를 인벤토리에 추가하지 않고, 등급별로 정해진 마일리지(가칭 `CheerleaderMileage`, 재화 성격)로 자동 환급한다. 마일리지는 추후 상점에서 특정 치어리더와 교환 가능. | 구현 범위가 작다(기존 `List<Cheerleader> OwnedCheerleaders`/`AddCheerleader()` 구조를 그대로 두고, 중복 판정 후 마일리지 재화만 추가하면 됨). 선수 카드 강화 재료(`ItemCategory.EnhanceMaterial`)와 유사한 기존 패턴 재사용 가능. | 마일리지 교환 상점 UI를 별도로 또 만들어야 한다(v0.2 후속 작업). |
| B안: 한계 돌파(Limit Break) | 이미 보유한 `CatalogId`가 다시 뽑히면 해당 치어리더 인스턴스의 버프(`ConditionBuff`/`ClutchMultiplier`/`EconomicBonusRate`/`SentimentDefense`)를 소폭 강화하는 "돌파 단계"를 부여한다. | 치어리더 육성 요소가 생겨 장기 리텐션에 유리하다. | `Cheerleader`에 돌파 단계 필드 추가, 돌파 시 버프 재계산 로직, UI 표시까지 필요해 구현 범위가 A안보다 크다. `docs/15_team_power_policy.md` 7절의 "치어리더 버프는 경기 조건부 값일 뿐 영구 스탯이 아니다"라는 원칙과 계속 정합적으로 유지하려면(돌파로 버프 수치 자체는 커지되, 그 값이 적용되는 방식은 여전히 ①·②(로스터/표시 OVR)가 아니라 ③(경기 적용 전력) 층위여야 함) 설계 시 각별한 주의가 필요하다. |

- **[Draft 권장안]** A안(마일리지 전환)을 v0.2 1차 목표로, B안(한계 돌파)은 v0.2 이후 스트레치 목표로 제안한다 - A안이 기존 아키텍처 변경 폭이 작고, 이미 있는 "강화 재료" 패턴과 개념적으로 유사해 학습 비용이 낮다.
- **[구현 완료, TASK-KBO-064]** A안이 실제로 구현되었다 - 다만 별도의 신규 `CheerleaderMileage` 재화를 만들지 않고, 기존 `PremiumCurrency`를 그대로 "마일리지 대용"으로 재사용했다(`GameManager.AddCheerleader()`가 중복 `CatalogId` 감지 시 등급별로 `NORMAL` 10/`RARE` 50/`EPIC` 200/`LEGEND` 1000을 `PremiumCurrency`에 직접 지급). 별도의 마일리지 교환 상점 UI도 아직 없다 - 자동 환급만 구현되어 있다.
- **[TBD, v0.2 확정 필요]** 등급별 전환량(10/50/200/1000)의 최종 밸런싱은 확정하지 않았다(모두 `[Draft]`). B안(한계 돌파)은 아직 구현되지 않았다.

# 5. 상점 UI 배치 제안 [Draft]

- 기존 `ShopUIController`(선수 카드 확정 패키지/프리미엄 10연뽑/스킬 변경권 3종 상품 구조, `UIManager.ScreenType.Shop`)에 치어리더 가챠 상품을 같은 화면 내 별도 탭 또는 섹션으로 추가하는 것을 제안했었다 - 새 화면(`ScreenType`)을 또 만들지 않고 기존 상점 화면을 확장하는 편이, TASK-KBO-060에서 이미 `ScreenType.Inventory`(선수 카드)와 `ScreenType.CheerleaderInventory`(치어리더)를 별도로 분리했던 선례와 달리 "상점"은 하나의 개념적 장소로 묶는 것이 더 자연스럽기 때문이다.
- **[TASK-KBO-066 갱신]** 실제로는 이 제안과 다르게, 기존 `ShopUIController`를 확장하지 않고 별도의 `CheerleaderShopUIController`(신규 화면 컨트롤러, `Assets/Scripts/Controllers`)를 만들었다 - 상품 개수도 4종(3절 갱신 이후 2종: 1회/10회 뽑기)뿐이라 별도 컨트롤러로 관리해도 복잡하지 않았고, `CheerleaderInventoryUIController`와 마찬가지로 치어리더 전용 화면을 독립적으로 두는 편이 TASK-KBO-060의 기존 분리 선례와도 더 일관적이었다. 다만 이 컨트롤러가 어떤 `ScreenType`에 연결될지, `ShopUIController`와 화면을 공유할지 별도 화면으로 둘지는 여전히 씬 배치 단계(사람 QA)의 결정 사항으로 남아 있다.
- **[TBD, v0.2 확정 필요]** 실제 탭 UI 구조나 최종 화면 배치는 이 문서의 범위 밖이며, UI 구현 단계에서 별도로 설계한다.

# 6. v0.1 정책과의 정합성 확인

이번 기획안이 `docs/15_team_power_policy.md`에 이미 확정된 v0.1 치어리더 정책과 충돌하지 않음을 아래와 같이 확인한다.

| v0.1 확정 원칙 | 근거 절 | 이번 기획안이 지키는 방식 |
| :--- | :--- | :--- |
| 치어리더 버프는 ①·②(로스터 OVR/표시 팀 OVR)에 영구 반영되지 않고, ③(경기 적용 전력) 층위에만 존재한다 | `15_team_power_policy.md` 7절 | 2절에서 제안한 등급별 수치는 기존 `ConditionBuff`/`ClutchMultiplier` 필드 값만 키우는 것이며, `GameManager.CalculateTeamOVR()`이나 `Player`의 영구 스탯 계산 경로에는 전혀 관여하지 않는다. |
| 치어리더 버프는 "유저 팀이면서 홈 경기"일 때만 적용된다(조건부 발동) | `15_team_power_policy.md` 7절 | 등급이 올라가도 이 적용 조건(`isUserTeamHome`) 자체는 바뀌지 않는다 - 등급은 "조건이 충족됐을 때 얼마나 센가"만 바꾸지, "언제 발동하는가"는 건드리지 않는다. |
| `EconomicBonusRate`/`SentimentDefense`(B안)는 `MatchEngine`에 전달되지 않고 스토브리그 로비 연산(`MatchRewardManager`)에서만 쓰인다 | `15_team_power_policy.md` 8절 | 2절 표의 `EconomicBonusRate`/`SentimentDefense` 값도 동일하게 `MatchRewardManager` 결산 로직에서만 소비되는 것을 전제로 설계했다 - 새로운 전달 경로를 제안하지 않는다. |
| `Cheerleader` 인스턴스는 항상 비어 있지 않은 `InstanceId`를 가진다(세이브 null 판별 불변식) | `11_data_dictionary.md` 9절 | 4절에서 제안한 `CatalogId`는 `InstanceId`와 별개의 신규 필드이며, 기존 `InstanceId` 발급 규칙이나 세이브 로직을 변경하지 않는다. |
| `CheerleaderGrade` enum은 세이브 직렬화 대상이라 값 재배치가 금지된다(DCL-006식 사고 방지) | `11_data_dictionary.md` 7절 | 2절의 등급 제안은 기존 `NONE=0`/`TEST=1`을 그대로 두고 `NORMAL=2`부터 이어 붙이는 형태로만 설계했다. |

# 7. 이번 작업에서 다루지 않은 것

- **[TASK-KBO-066 갱신]** 최초 작성(TASK-KBO-063) 시점에는 여기 나열된 항목들이 전부 "문서 범위 밖"이었으나, `CheerleaderGrade` enum 확장/`CatalogId` 필드/`CheerleaderGachaService`/`CheerleaderShopUIController`는 TASK-KBO-064~066에서 이미 실제 코드로 구현되었다(3·4절 참고). 아래는 그중에서도 여전히 미구현/미확정으로 남은 항목이다.
- `cheerleaders.csv` 카탈로그 정식 전환(현재는 `CheerleaderCatalog.cs`에 하드코딩), 천장 규칙의 구체적인 횟수, 등급별 버프 수치(2절)와 마일리지 전환량(4절)의 최종 밸런싱은 확정하지 않았다(모두 **[Draft]**/**[TBD]** 표기).
- 신규 유료(실결제) 재화나 결제 연동은 제안하지 않았다 - 기존 게임 내 재화(`PremiumCurrency`) 순환만으로 설계했다(`GameGold`는 치어리더 가챠에 관여하지 않음 - 3절 참고).
