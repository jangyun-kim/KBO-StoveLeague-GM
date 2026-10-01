---
문서명: 치어리더 상점/가챠(뽑기) 정책 [Draft]
버전: v0.2 (Draft)
상태: Draft
최종 수정일: 2026-09-21 (TASK-KBO-129, 3절 GDD 원문 4-카테고리 재화 구조로 전면 갱신)
담당자: 김장윤
관련 파일: Cheerleader.cs, GameManager.cs, CheerleaderShopUIController.cs, CheerleaderGachaService.cs, ScoutManager.cs, docs/11_data_dictionary.md, docs/13_decision_change_log.md, docs/15_team_power_policy.md
---

# 0. 문서 성격

- 이 문서는 **[Draft]** 상태의 기획안이다. 여기 적힌 수치(등급별 버프량, 중복 처리 마일리지 등)는 대부분 확정이 아니며, 실제 밸런싱은 v0.2 구현 단계에서 시뮬레이션/플레이테스트를 거쳐 조정될 예정이다.
- **[TASK-KBO-129 갱신, 예외]** 3절(가챠 확률 및 재화)만은 예외다 - GDD "스토브리그: 단장의 시간" 원문 "뽑기(가챠) &gt; 치어리더 영입"/"재화" 절을 그대로 반영해 전면 재구현했다(`CheerleaderGachaService.cs`, `DCL-101`). TASK-KBO-065/066이 구현했던 단일 재화(`PremiumCurrency`→`CheerStick`) 1회/10회 뽑기 구조는 **완전히 폐기**되었다 - GDD에 없는 양산형 모바일 가챠 관습이었다는 것이 TASK-KBO-129에서 확인되었다. 실제 사양은 3절 참고.
- v0.1에서 이미 확정된 치어리더 B+C 하이브리드 엔진 정책(`docs/15_team_power_policy.md` 6~8절)과 세이브 아키텍처(`docs/11_data_dictionary.md` 7·9절)를 절대 깨지 않는 범위 안에서만 설계했다 - 자세한 정합성 확인은 6절 참고.

# 1. 목적

- v0.1에서 데이터·백엔드·UI까지 전부 완성됐지만 정작 "얻는 방법"이 없던 치어리더 시스템(`docs/13_decision_change_log.md` DCL-033 참고)에 정식 획득처(상점/가챠)를 부여한다.
- 과금 유도가 아니라 **스토브리그 로비를 통한 재화 선순환**에 초점을 맞춘다 - 신규 유료 재화를 새로 만들지 않고 기존 재화를 그대로 재사용한다. **[TASK-KBO-066 갱신]** 최초 초안(TASK-KBO-063)은 `GameGold`/`PremiumCurrency` 이원화를 제안했으나, 실제 구현(3절)은 `PremiumCurrency` 단일 재화로 단순화되었다 - `GameGold`는 치어리더 가챠에 관여하지 않는다.

# 2. 치어리더 정식 등급 체계 [TASK-KBO-127, PM 확정]

- **[TASK-KBO-127 갱신]** PM(단장)이 5단계 정식 등급 체계를 확정했다 - 아래 4종 [Draft] 제안(NORMAL/RARE/EPIC/LEGEND)은 폐기되었다. 성능 순서(낮음→높음): `LIVE_NORMAL` < `LIVE_EPIC` < `ICON` < `LEGEND` < `SEASON_LIMITED`.
- **[중요, 세이브 호환 주의]** `docs/11_data_dictionary.md` 7절이 이미 경고했듯, `Cheerleader`는 TASK-KBO-057부터 `GameSaveData`로 직렬화되는 대상이 되었다. 새 등급을 추가할 때는 기존 `NONE=0`/`TEST=1`을 절대 바꾸지 않고 **반드시 enum 끝에만 추가**해야 한다(`DCL-006`이 겪은 것과 동일한 재배치 사고 방지). 아래 ID 열은 이 제약을 반영해 2번부터 이어서 부여했다(TASK-KBO-064의 4단계 제안이 쓰던 2~5번을 그대로 재사용하고, 신설된 `SEASON_LIMITED`만 6번을 새로 부여했다 - 개발 빌드라 값 재해석에 따른 구버전 세이브 데이터 유실은 허용됨).

| ID | 등급명 (Enum) | 컨셉 | `ConditionBuff` | `ClutchMultiplier` | `EconomicBonusRate` | `SentimentDefense` |
| :-- | :--- | :--- | :-- | :-- | :-- | :-- |
| 2 | `LIVE_NORMAL` (라이브 일반) | 가챠 최다 물량, 입문용 | +1 | 1.00 | 1.05 | 0 |
| 3 | `LIVE_EPIC` (라이브 에픽) | 무과금 주력, 성장 체감 시작 | +2 | 1.05 | 1.10 | 1 |
| 4 | `ICON` (아이콘) | 확정팩 기준선, 눈에 띄는 성능 | +3 | 1.10 | 1.15 | 2 |
| 5 | `LEGEND` (레전드) | 최상위 희소성 | +4 | 1.15 | 1.20 | 3 |
| 6 | `SEASON_LIMITED` (시즌한정) | 최상위 한정판, 프리미엄 10연뽑 확정 대상 | +5 | 1.20 | 1.25 | 4 |

- 수치 설계 근거: 기존 4단계 제안의 선형 스케일(`ConditionBuff`/`SentimentDefense` +1씩, `ClutchMultiplier`/`EconomicBonusRate` +0.05씩)을 5번째 등급까지 그대로 연장했다 - 급격한 파워 인플레이션 없이 자연스럽게 이어지도록 하기 위함이며, 밸런싱 확정 전까지는 여전히 조정 가능하다.

# 3. 가챠 확률 및 재화 (v0.2 → TASK-KBO-129로 GDD 원문 구조로 전면 교체 - `[Draft]` 아님)

- **[TASK-KBO-129 갱신, SSOT 통일, 재폐기]** TASK-KBO-065/066이 구현했던 단일 재화(`CheerStick`) 1회/10회
  뽑기 구조는 GDD 원문에 없는 양산형 모바일 가챠 관습으로 확인되어 **완전히 폐기**되었다. GDD "뽑기(가챠) &gt;
  치어리더 영입"/"재화" 절이 실제로 정의한 4개 카테고리 전용 재화로 교체했다(`CheerleaderGachaService.cs`).
  아래 표는 그 실제 구현체와 1:1로 일치하며, "제안"이 아니라 "코드가 곧 사양"이다. **[TASK-KBO-178]** 확률표 SSOT는
  `Models/CheerleaderDropTables.cs`이며 화면/로그의 티어 표기는 `LIVE`/`ICON`/`LEGEND`로 통일했다(내부 enum LIVE_NORMAL/LIVE_EPIC → "LIVE").

| 카테고리(GDD 분류) | 메서드 | 소모 재화 | 비용(1회) | 등급 판정 | 비고 |
| :--- | :--- | :--- | :-- | :--- | :--- |
| 일반 영입 &gt; 라이브 | `RollLive(count)` | `LiveCheerStick`(라이브 응원봉) | 100 | **[TASK-178]** `LIVE` 100% (구 LIVE_NORMAL 62.5 / LIVE_EPIC 37.5 - LIVE_EPIC 카탈로그 0장이라 폐지) | `CheerleaderShopUIController.liveButton` |
| ~~일반 영입 &gt; 한정~~ | ~~`RollLimited`~~ | `LimitedCheerStick` | - | **[TASK-178] 상품 폐지** - `SEASON_LIMITED` 카탈로그 0장(치어리더 DB는 LIVE/ICON/LEGEND뿐). 재화 필드는 세이브 호환용으로만 유지 | - |
| 픽업·프리미엄 영입 &gt; 아이콘 | `RollIcon(count)` | `StarCheerStick`(스타 응원봉) | 100 | **[TASK-144→178]** `ICON` 0.5% / `LIVE` 99.5% | `iconButton`. 픽업/프리미엄 구분 전용 재화가 GDD에 없어 통합(4절 참고) |
| 픽업·프리미엄 영입 &gt; 레전드 | `RollLegend(count)` | `LegendCheerStick`(레전드 응원봉) | 100 | **[TASK-144→178]** `LEGEND` 0.5% / `ICON` 1.5% / `LIVE` 98.0% | `legendButton`. 위와 동일 사유로 통합 |

- **[UI 변경]** `CheerleaderShopUIController`는 이제 "1회/10회 뽑기" 2버튼이 아니라 위 4개 카테고리 버튼을
  각각 count=1로 호출한다(TASK-KBO-129 - 10연뽑 개념 자체를 폐지, GDD가 픽업의 10/40/80회를 "1회 클릭 10연출"이
  아니라 "누적 뽑기 횟수"로 쓰는 것과의 혼동을 없앴다). `RollLive()`/`RollLimited()`/`RollIcon()`/`RollLegend()`
  전부 `count` 매개변수를 받아 여러 장 뽑기를 지원하지만, 현재 UI는 1을 고정 전달한다.
- **확률 총합 검증**: `RollLive()`만 확률 기반(62.5+37.5=100%), 나머지 3개는 각각 단일 등급 100% 확정이다.
- **[폐기됨] GameGold 이원화 및 확정 슬롯 구조**: 최초 초안(TASK-KBO-063)이 제안했던 "일반 뽑기(`GameGold`)/
  프리미엄 뽑기(확정 슬롯 포함)" 2단계 구조는 이번에도 채택되지 않았다. `GameGold`는 치어리더 가챠에 전혀
  관여하지 않는다.
- **[TBD, v0.2 확정 필요]** 천장(하드 피티), 할인, 보장 슬롯 등은 여전히 미도입 상태다 - `09_probability_policy.md`
  3절이 전사 공통 정책으로 이런 장치의 필요성을 열어 두고 있으나, 이번 구현에는 포함되지 않았다. 도입 여부와
  구체적 규칙은 후속 밸런싱 단계에서 재검토한다.

# 4. 중복 획득 처리 방안 [Draft]

- **문제 정의**: `Cheerleader`는 현재 원본(카탈로그) 참조 구조가 없다 - 선수 카드가 `PlayerTemplate`(원본, 불변) + `Player`(발급된 인스턴스, `InstanceId`)로 분리된 것과 달리, `Cheerleader`는 `InstanceId`/`Name`/버프 필드가 전부 한 클래스에 있다(`docs/11_data_dictionary.md` 7절). 따라서 "같은 치어리더를 또 뽑았다"를 판별하려면 v0.2에서 **원본 식별자(가칭 `CatalogId`, 예: `CHR_001`)를 `Cheerleader`에 새로 추가**해, `InstanceId`(발급된 개체 고유값, 세이브 null 판별에 쓰이는 기존 불변식)와 분리해야 한다. 이 문서는 그 분리를 전제로 아래 두 방안을 제안한다.
- **[불변식 재확인]** `docs/11_data_dictionary.md` 9절의 "치어리더 인스턴스는 항상 비어 있지 않은 `InstanceId`를 가진다" 불변식은 `CatalogId` 추가와 무관하게 그대로 유지되어야 한다 - 중복 판정은 `CatalogId` 비교로만 하고, `InstanceId`는 계속 개체별로 고유하게 새로 발급한다(세이브 로직 변경 불필요).

| 방안 [Draft] | 동작 | 장점 | 단점 |
| :--- | :--- | :--- | :--- |
| A안: 마일리지 전환 (권장) | 이미 보유한 `CatalogId`가 다시 뽑히면 `Cheerleader` 인스턴스를 인벤토리에 추가하지 않고, 등급별로 정해진 마일리지(가칭 `CheerleaderMileage`, 재화 성격)로 자동 환급한다. 마일리지는 추후 상점에서 특정 치어리더와 교환 가능. | 구현 범위가 작다(기존 `List<Cheerleader> OwnedCheerleaders`/`AddCheerleader()` 구조를 그대로 두고, 중복 판정 후 마일리지 재화만 추가하면 됨). 선수 카드 강화 재료(`ItemCategory.EnhanceMaterial`)와 유사한 기존 패턴 재사용 가능. | 마일리지 교환 상점 UI를 별도로 또 만들어야 한다(v0.2 후속 작업). |
| B안: 한계 돌파(Limit Break) | 이미 보유한 `CatalogId`가 다시 뽑히면 해당 치어리더 인스턴스의 버프(`ConditionBuff`/`ClutchMultiplier`/`EconomicBonusRate`/`SentimentDefense`)를 소폭 강화하는 "돌파 단계"를 부여한다. | 치어리더 육성 요소가 생겨 장기 리텐션에 유리하다. | `Cheerleader`에 돌파 단계 필드 추가, 돌파 시 버프 재계산 로직, UI 표시까지 필요해 구현 범위가 A안보다 크다. `docs/15_team_power_policy.md` 7절의 "치어리더 버프는 경기 조건부 값일 뿐 영구 스탯이 아니다"라는 원칙과 계속 정합적으로 유지하려면(돌파로 버프 수치 자체는 커지되, 그 값이 적용되는 방식은 여전히 ①·②(로스터/표시 OVR)가 아니라 ③(경기 적용 전력) 층위여야 함) 설계 시 각별한 주의가 필요하다. |

- **[Draft 권장안]** A안(마일리지 전환)을 v0.2 1차 목표로, B안(한계 돌파)은 v0.2 이후 스트레치 목표로 제안한다 - A안이 기존 아키텍처 변경 폭이 작고, 이미 있는 "강화 재료" 패턴과 개념적으로 유사해 학습 비용이 낮다.
- **[구현 완료, TASK-KBO-064/127/128/129 갱신]** A안이 실제로 구현되었다 - 다만 별도의 신규 `CheerleaderMileage` 재화를 만들지 않고, 등급이 실제로 속한 응원봉 재화를 그대로 "마일리지 대용"으로 재사용한다(`GameManager.ApplyCheerleaderDuplicateConversion()`, TASK-KBO-129 - 구 단일 `CheerStick` 폐기로 4종 응원봉에 각각 지급하도록 라우팅). 환급량은 `GameManager.ResolveCheerleaderDuplicateConversionValue()`: `LIVE_NORMAL`/`LIVE_EPIC` 10/30(→`LiveCheerStick`), `ICON` 50(→`StarCheerStick`), `LEGEND` 100(→`LegendCheerStick`), `SEASON_LIMITED` 300(→`LimitedCheerStick`, TASK-KBO-128이 10연뽑 무한 증식 버그를 하향 조정한 값). 별도의 마일리지 교환 상점 UI도 아직 없다 - 자동 환급만 구현되어 있다. **[결정 필요, TASK-KBO-129]** 이 중복 자동 변환 메커니즘 자체가 GDD 원문에 근거가 없는 AI 고안 장치임이 확인되었다(`GameManager.AddCheerleader()` 주석 참고) - 존치/폐기는 사용자 확인이 필요하다.
- **[TBD, v0.2 확정 필요]** 등급별 전환량(10/50/200/1000/3000)의 최종 밸런싱은 확정하지 않았다(모두 `[Draft]`). B안(한계 돌파)은 아직 구현되지 않았다.

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
| `CheerleaderGrade` enum은 세이브 직렬화 대상이라 값 재배치가 금지된다(DCL-006식 사고 방지) | `11_data_dictionary.md` 7절 | 2절의 등급 체계는 기존 `NONE=0`/`TEST=1`을 그대로 두고 `LIVE_NORMAL=2`부터 이어 붙이는 형태로만 설계했다(TASK-KBO-127). |

# 7. 이번 작업에서 다루지 않은 것

- **[TASK-KBO-066 갱신]** 최초 작성(TASK-KBO-063) 시점에는 여기 나열된 항목들이 전부 "문서 범위 밖"이었으나, `CheerleaderGrade` enum 확장/`CatalogId` 필드/`CheerleaderGachaService`/`CheerleaderShopUIController`는 TASK-KBO-064~066에서 이미 실제 코드로 구현되었다(3·4절 참고). 아래는 그중에서도 여전히 미구현/미확정으로 남은 항목이다.
- `cheerleaders.csv` 카탈로그 정식 전환(현재는 `CheerleaderCatalog.cs`에 하드코딩), 천장 규칙의 구체적인 횟수, 등급별 버프 수치(2절)와 마일리지 전환량(4절)의 최종 밸런싱은 확정하지 않았다(모두 **[Draft]**/**[TBD]** 표기).
- 신규 유료(실결제) 재화나 결제 연동은 제안하지 않았다 - 기존 게임 내 재화(`PremiumCurrency`) 순환만으로 설계했다(`GameGold`는 치어리더 가챠에 관여하지 않음 - 3절 참고).
