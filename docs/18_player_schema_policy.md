---
문서명: 선수 데이터 스키마 정책 (Player Schema Policy)
버전: v0.3 (Draft)
상태: Draft
최종 수정일: 2026-09-14
담당자: 김장윤
관련 파일: 11_data_dictionary.md, 17_v03_roadmap.md, 13_decision_change_log.md
---

# 선수 데이터 스키마 정책

## 0. 시작하며 - PM 확정 결정: CSV 기반(체계 B) SSOT 통합

TASK-KBO-080에서 코드베이스를 전수 스캔한 결과 발견한 **서로 완전히 단절된 두 개의 "선수" 데이터 체계**에 대해,
기획 PM이 아래와 같이 공식 확정했습니다(TASK-KBO-081 명령서 3항).

> **[확정]** `players.csv`/`cards.csv` 기반 체계(구 "체계 B")를 v0.3의 공식 Single Source of Truth(SSOT)로
> 채택합니다. 구 "체계 A"의 `PlayerTemplate`(ScriptableObject)은 에디터에서 `.asset`으로 수동으로 구워 두는
> 방식을 폐기하고, **런타임에 `players.csv`/`cards.csv`를 파싱해 동적으로 인스턴스화하는 "메모리 캐싱용
> 브릿지 모델"**로 용도를 변경합니다 - 즉 `PlayerTemplate` C# 클래스 자체(필드 구조)는 계속 쓰이지만, 그
> 인스턴스는 더 이상 `Assets/`에 `.asset` 파일로 저장되지 않고 게임 시작 시 CSV로부터 메모리에만 생성됩니다.
> 치어리더 시스템이 이미 구현한 `cheerleaders.csv → CheerleaderCatalog.Initialize()` 패턴과 동일한
> 아키텍처 방향입니다.
>
> **[확정]** 본 프로젝트는 비상업적 팬 메이드 포트폴리오로 규정되므로, `players.csv`의 KBO 실명 데이터는
> 게임 몰입감을 위해 그대로 유지합니다. 구 "체계 B" 2-1절의 실명/초상권 [결정 필요/리스크] 항목은 이 결정으로
> 해소되었습니다(아래 2-1절에 확정 사실로 반영).

아래 표는 TASK-KBO-080이 조사한 두 체계의 원래 모습을 그대로 보존한 참고 자료입니다 - **"소비자"/"데이터 소스"
열은 위 확정 이전의 상태**이며, 확정 이후에는 `PlayerDatabase.allTemplates`의 수동 `.asset` 등록 방식이
폐기되고 CSV 파싱 브릿지로 대체될 예정이라는 점에 유의해 읽어 주십시오(구체적인 구현은 이번 문서 작업 범위 밖 -
아래 F항 참고).

| | 구 체계 A - 카드/가챠 시스템 (`PlayerTemplate`은 브릿지 모델로 존속) | 확정 SSOT: 체계 B - CSV 기반 |
|---|---|---|
| 네임스페이스 | `KBOManager.Models` / `KBOManager.Data` / `KBOManager.Managers` | `KBOManager.Broadcast.Data` / `KBOManager.Broadcast.Managers` |
| 핵심 클래스 | `Player.cs`, `PlayerTemplate.cs`(ScriptableObject → 브릿지 모델로 용도 변경), `PlayerDatabase.cs` | `PlayerModel.cs`, `CardModel.cs`, `DataManager.cs` |
| 데이터 소스 | (변경 전) `PlayerDatabase.allTemplates`에 `.asset` 수동 드래그 → (확정 후) CSV 파싱 결과를 런타임에 채워 넣는 방식으로 전환 예정 | `Assets/Resources/Data/players.csv`, `cards.csv`(CSV 파서로 자동 로드) - **SSOT로 확정** |
| 소비자 | `ScoutManager`(가챠), `RosterManager`(로스터), `UpgradeManager`(강화/각성) - 이 소비자들은 그대로 유지, 데이터 소스만 CSV로 전환 | `Broadcast.Managers.DataManager` (그 이상의 소비자는 코드베이스에서 확인되지 않음) |
| 스탯 모델 | `BatterStats{Power,Contact,Discipline}` / `PitcherStats{Stuff,Velocity,Movement,Control}` (정수) | `ZContact/ZEye/ZPower/ZSpeed/ZDef/ZStamina` (베이지안 보정 Z-score, 실수) |
| 선수 이름 | `PlayerTemplate.PlayerName`(자유 문자열, 실제 등록된 `.asset`은 0건) | `players.csv`에 **실제 KBO 선수 실명**(구자욱/원태인/오승환/노시환) 4건 - **팬 프로젝트로 실명 유지 확정** |

**두 체계를 잇는 브릿지 코드는 TASK-KBO-080 조사 시점 기준 전수 검색 결과 존재하지 않았습니다** -
`PlayerModel`/`CardModel`/`DataManager`를 참조하는 코드는 `Assets/Scripts/Broadcast/` 폴더 밖에 단 한 곳도
없었습니다. 위 SSOT 확정에 따라 이 브릿지를 새로 만드는 것이 v0.3의 실제 구현 과제입니다(문서 작업만 하는
이번 태스크의 범위 밖 - 아래 F항 참고).

---

## 1. 현재 구현된 선수 데이터 스키마 명세 (`PlayerTemplate` 브릿지 모델 - 카드/가챠 시스템)

아래 클래스 구조 자체(필드명/타입)는 0절의 SSOT 확정과 무관하게 그대로 유지됩니다 - 바뀌는 것은
"이 필드들을 누가 채우는가"입니다(기존: 에디터에서 `.asset`을 수동 제작 / 확정 후: CSV 파싱 결과로 런타임에
자동 채움). 아래 설명 중 `PlayerDatabase`의 `.asset` 수동 등록 방식 서술(1-4절)은 SSOT 확정 이전의 상태를
그대로 기록한 것이며, 실제 코드 변경(CSV 파싱 로더 추가)은 이번 문서 작업 범위 밖입니다.

### 1-1. `PlayerTemplate.cs` (`ScriptableObject`, 불변 원본 데이터)

| 필드 | 타입 | 설명 |
|---|---|---|
| `TemplateId` | string | 카드 고유 식별자 (예: `"GG_KOOJASOOK_2024"`) |
| `RealPlayerId` | string | 실제 선수 식별자 - 카드 등급/연도가 달라도 동일 선수면 동일 값(각성 재료 판정용) |
| `PlayerName` | string | 카드 표기 이름 |
| `SeasonYear` | int | 기준 시즌 연도 |
| `Team` | `Team` (enum) | 소속 구단 |
| `Grade` | `Grade` (enum) | 카드 등급 |
| `IsPitcher` | bool | 투수/타자 구분 |
| `BatterPosition` | `BatterPosition` (enum) | `IsPitcher == false`일 때만 유효 |
| `PitcherRole` | `PitcherRole` (enum) | `IsPitcher == true`일 때만 유효 |
| `BatterStats` | `BatterStats` (struct) | `IsPitcher == false`일 때만 유효 |
| `PitcherStats` | `PitcherStats` (struct) | `IsPitcher == true`일 때만 유효 |
| `Cost` | int | **[레거시]** GDD v3.1 시절 고정 코스트. 현재 `RosterManager`는 이 값을 쓰지 않고 `Player.CalculateSalaryCost()`(런타임 공식)를 사용 |
| `PresetSkillTier` | `SkillTier` (enum) | 스킬 프리셋(선택) |
| `PresetSkillName` | string | 스킬 프리셋(선택) |
| `GetBaseOverall()` | method | 강화/각성/세트덱 반영 전 순수 원본 OVR(세부 스탯 평균) |

### 1-2. `Player.cs` (순수 C# 클래스, 유저가 실제 보유한 카드 인스턴스 - 런타임 육성 상태)

`Template`을 참조하고, **강화/각성/스킬 등 유저의 육성 진행 상태만** 별도로 들고 있습니다(원본 스탯 자체를 복제하지 않음).

| 필드/프로퍼티 | 타입 | 설명 |
|---|---|---|
| `InstanceId` | string | 유저 보유 카드 고유 ID (GUID) |
| `Template` | `PlayerTemplate` | 원본 데이터 참조 |
| `ReinforceLevel` | int | 0~10강 (`MaxReinforceLevel = 10`) |
| `AwakenLevel` | int | 0~10각 (`MaxAwakenLevel = 10`, ALLSTAR 이상 등급만 유효 - `CanAwaken` 참고) |
| `StarLevel` | int | 1~6성 (`MinStarLevel`~`MaxStarLevel`), 뽑기 시 등급별로 결정되는 초기값 |
| `CurrentStarType` | `StarType` (enum) | 카드 성급의 시각화 타입(등급에 1:1 대응) |
| `AcquiredSkillIds` | `List<string>` | 보유 스킬 ID 목록 |
| `MaxStamina` / `CurrentStamina` | int | 투수 카드에만 의미 있음(타자는 항상 0). 롤별 기본 최대치: 선발 100 / 불펜 40 |
| `CurrentCondition` | `PlayerCondition` (enum) | 5단계 일일 컨디션. 타자/투수 공통 적용 |
| `GetEffectiveBatterStats()` / `GetEffectivePitcherStats()` | method | 강화/각성 성장치(레벨당 +1, 현재 TODO 임시값)가 반영된 세부 스탯 |
| `CalculateOVR(bool, float)` | method | 세트덱 배율 + 일일 컨디션 배율(±5%)까지 반영한 최종 OVR |
| `CalculateSalaryCost(bool, float)` | method | GDD v4.0 공식: 등급 기본 코스트 + (최종 OVR - 60) + (각성 단계 × 1.5) |
| `IsLowStamina` | bool (계산) | 체력 30% 미만 시 true → `MatchEngine`이 최종 스탯 -15% 페널티 적용 |
| `CanAwaken` | bool (계산) | `SEASON`/`LIVE_NORMAL`/`LIVE_EPIC` 등급은 각성 불가 |

### 1-3. 공용 Enum/Struct (`Assets/Scripts/Models/Types.cs`)

| 이름 | 종류 | 값 |
|---|---|---|
| `BatterStats` | struct | `Power`(장타력) / `Contact`(정확·삼진 감소) / `Discipline`(선구·볼넷) - 정수 3종 |
| `PitcherStats` | struct | `Stuff`(구위) / `Velocity`(구속) / `Movement`(변화) / `Control`(제구) - 정수 4종 |
| `Grade` | enum | `SEASON=0` / `LIVE_NORMAL=1` / `LIVE_EPIC=2` / `ALLSTAR=3` / `TITLE_HOLDER=4` / `SIGNATURE=5` / `GOLDEN_GLOVE=6` / `DYNASTY=7`(정수값 = 희귀도 랭크로 고정) |
| `StarType` | enum | `NORMAL` / `PURPLE` / `SILVER` / `GOLD` / `PLATINUM` / `TEAM_COLOR` |
| `SkillTier` | enum | `S_PLUS` / `S` / `A` / `B` / `C` / `D` / `F` |
| `PlayerCondition` | enum | `Poor` / `BelowAverage` / `Normal` / `Good` / `Excellent`(선언 순서가 곧 등급 순서) |
| `BatterPosition` | enum | 9자리: `Catcher`/`FirstBase`/`SecondBase`/`ThirdBase`/`ShortStop`/`LeftField`/`CenterField`/`RightField`/`DesignatedHitter` |
| `PitcherRole` | enum | 5자리: `StartingPitcher`/`WinningReliever`/`MopUpReliever`/`LongReliever`/`Closer` |
| `Team` | enum | `None` + KBO 10개 구단(`Doosan`/`LG`/`KT`/`SSG`/`NC`/`Kiwoom`/`KIA`/`Samsung`/`Lotte`/`Hanwha`) |

### 1-4. `PlayerDatabase.cs` (선수 템플릿 DB, 싱글톤) - **현재 코드 상태(변경 전)**

- `[SerializeField] List<PlayerTemplate> allTemplates` - **에디터 Inspector에 `.asset` 파일을 수동으로 드래그하여 등록**하는 구조. CSV나 다른 자동 로더가 없습니다.
- `GetTemplateById(string)` / `CreatePlayerInstance(string)` 2개 메서드만 제공.
- **[확인된 사실]** `find Assets -iname "*.asset" | xargs grep -l "PlayerTemplate"`로 프로젝트 전체를 조사한 결과, 실제로 생성된 `PlayerTemplate` `.asset` 인스턴스가 **0건**입니다. 즉 `PlayerDatabase.allTemplates`는 현재 완전히 빈 리스트로 추정되며, `ScoutManager.Roll1()`/`Roll10()`을 지금 호출하면 뽑을 카드가 없어 실패할 가능성이 높습니다.
- **[0절 SSOT 확정에 따른 향후 방향, 이번 문서 작업 범위 밖]** `allTemplates`를 Inspector 수동 등록 대신, `Awake()`(또는 별도 `Initialize()`)에서 `players.csv`+`cards.csv`를 파싱해 `PlayerTemplate` 인스턴스를 런타임에 생성해 채우는 방식으로 교체될 예정입니다 - `CheerleaderCatalog.Initialize()`(`cheerleaders.csv` 파싱)와 동일한 패턴입니다. 실제 코드 구현은 이번 태스크(순수 문서 작업)의 범위가 아닙니다.

### 1-5. `SkillDB.cs` (스킬 정의)

- `SkillCategory`(`Batter`/`StartingPitcher`/`BullpenPitcher`), `EffectTarget`(`Self`/`Opponent`), `StatType`(타자 3종 + 투수 4종이 하나의 enum에 공존, 대상이 아닌 StatType 지정 시 적용 단계에서 무시됨) - 3개 enum으로 스킬 효과를 기술하는 구조까지 이미 존재합니다.

---

## 2. `players.csv` / `cards.csv` 데이터 현황 (확정 SSOT)

TASK-KBO-080 명령서 7항의 "CSV가 없으면 [결정 필요]"와 달리, **CSV는 존재하며, 0절 PM 확정에 따라 이제
v0.3의 공식 SSOT입니다.** 아래 내용은 TASK-KBO-080이 조사한 원본 현황이며, "1절의 가챠 시스템과 무관한
별도 스키마"였던 상태는 0절의 확정 결정으로 "1절 `PlayerTemplate`이 이 데이터를 런타임에 읽어 채우는 대상"
으로 관계가 재정의되었습니다.

### 2-1. `Assets/Resources/Data/players.csv` (420 bytes, 헤더 포함 4개 데이터 행)

```
player_id,team_id,name,year,position,pa_ip,z_contact,z_eye,z_power,z_speed,z_def,z_stamina,active
PLY_0001,TEM_001,구자욱,2023,RF,450,1.542,0.821,1.211,0.450,0.300,1.100,TRUE
PLY_0002,TEM_001,원태인,2023,SP,150,0.850,0.920,1.050,0.000,0.150,1.400,TRUE
PLY_0003,TEM_001,오승환,2023,CP,50,1.200,1.100,0.950,0.000,0.200,0.500,TRUE
PLY_0004,TEM_002,노시환,2023,3B,500,1.100,0.950,2.100,0.100,0.600,1.200,TRUE
```

- **가상 데이터가 아닙니다.** `구자욱`/`원태인`/`오승환`/`노시환`은 실제 KBO 소속 선수의 실명이며, `year=2023` 시즌
  기록을 베이지안 보정한 Z-score로 추정됩니다. **[확정]** 본 프로젝트는 비상업적 팬 메이드 포트폴리오로
  규정되어, 게임 몰입감을 위해 이 실명 데이터를 그대로 유지하기로 PM이 확정했습니다(TASK-KBO-081 명령서
  3항). 별도 법무/사업 검토나 가상 선수명 교체는 진행하지 않습니다.
- `team_id`는 `TEM_001`/`TEM_002` 형식의 플레이스홀더 코드로, 1절 `PlayerTemplate.Team`의 `Team` enum
  (`Doosan`/`LG`/...) 값과 이름 체계가 다릅니다 - CSV → `PlayerTemplate` 브릿지를 구현하려면
  `team_id ↔ Team enum` 매핑표가 별도로 필요합니다(**[TBD]**, 이번 문서 작업 범위 밖).
- `position`은 문자열(`SP`/`CP`/`RF`/`3B` 등)로 저장되며, `PlayerModel.IsPitcher`는 `"SP"`/`"CP"`/`"RP"` 세
  값만으로 판정합니다(1절 `BatterPosition`/`PitcherRole` enum과 값 체계가 다름 - 이 역시 브릿지 구현 시
  매핑표가 필요합니다, **[TBD]**).
- 소비자는 `Assets/Scripts/Broadcast/Managers/DataManager.cs`(`LoadPlayers()`) 단 한 곳이며, 이 데이터를
  실제로 화면에 노출하거나 가챠에 사용하는 코드는 확인되지 않았습니다.
- `PlayerModel.cs` 클래스 주석이 `StatCalculator.GetOVR()`을 "이 Z-score들의 입력값"으로 언급하지만,
  **`StatCalculator`라는 클래스는 코드베이스 전체에 존재하지 않습니다**(전수 검색 결과 0건) - 주석만 남은
  미구현/삭제된 참조로 추정됩니다.

### 2-2. `Assets/Resources/Data/cards.csv` (헤더 포함 4개 데이터 행)

```
card_id,player_id,grade_id,grade_name,base_ovr,salary_cost,max_enhance,max_awaken,is_droppable
CRD_0001,PLY_0001,0,SEASON,68,15,10,0,TRUE
CRD_0002,PLY_0001,2,LIVE_EPIC,74,18,10,0,TRUE
CRD_0003,PLY_0001,6,GOLDEN_GLOVE,79,35,10,10,FALSE
CRD_0004,PLY_0002,0,SEASON,65,12,10,0,TRUE
```

- `player_id`로 `players.csv`를 참조하는 외래키 구조 - "한 선수가 여러 등급의 카드를 가질 수 있다"는 설계는
  1절(`PlayerTemplate` 1개 = 카드 1장, `RealPlayerId`로 동일 선수 판정)과 **개념적으로 동일**합니다 - 0절의
  SSOT 확정으로 이제 이 구조가 `PlayerTemplate` 브릿지 모델의 실제 생성 로직 설계로 채택됩니다.
- `grade_id`(0/2/6) 값이 1절 `Grade` enum 정수값(`SEASON=0`/`LIVE_EPIC=2`/`GOLDEN_GLOVE=6`)과 **정확히
  일치**합니다 - 우연이 아니라 원래 같은 등급 체계를 공유하도록 설계된 것으로 추정되며, 브릿지 구현 시
  별도 매핑 없이 정수값을 그대로 캐스팅할 수 있을 것으로 보입니다.
- `max_enhance`/`max_awaken` 컬럼은 1절 `Player.MaxReinforceLevel`(10)/`Player.MaxAwakenLevel`(10) 상수와
  대응되는 개념이나, 코드에서는 상수로 고정되어 있고 CSV 컬럼값을 읽어오는 코드는 아직 없습니다(**[TBD]**,
  브릿지 구현 시 상수를 CSV 값으로 대체할지 여부는 별도 결정 필요).
- **[본 태스크(TASK-KBO-081)로 해소]** `docs/11_data_dictionary.md` 3절이 이 두 CSV의 스키마를 문서화하고
  있어, TASK-KBO-080 조사 시점에는 "GDD는 CSV 기반을 문서화했지만 실제 가챠 코드는 무관한 ScriptableObject
  수동 등록 방식으로 구현되어 있다"는 문서-코드 불일치가 있었습니다. 0절의 PM 확정(CSV를 SSOT로 채택)으로
  이 불일치는 방향이 정해졌고, `docs/11_data_dictionary.md` 3절도 이번 태스크에서 함께 갱신해 두 문서가
  같은 설계를 가리키도록 정리했습니다.

---

## 3. v0.3 UI 고도화를 위한 추가 필드 제안

아래는 스카우트 상점 UI/`PlayerCardUI` 프리팹에 카드를 보기 좋게 출력하기 위해 **추가로 검토가 필요한 필드
제안**입니다. `Assets/Scripts/UI/PlayerCardUI.cs`를 직접 읽어 현재 실제로 구현된 필드(`nameText`/`teamText`/
`positionText`/`ovrText`/`frameImage`/`starIcons`/스태미나 바/컨디션 아이콘)를 확인한 뒤, 그중 없는 것만
제안합니다. 수치나 확정 스펙이 아니라 "어떤 데이터가 더 필요한가"에 대한 개요이며, 최종 채택 여부와 정확한
타입은 기획 확정이 필요합니다.

- **구단 심볼/엠블럼 리소스 참조** - 현재 `teamText`는 `player.Template.Team.ToString()`(영문 enum 이름)을
  그대로 텍스트로 출력할 뿐, 로고 `Sprite`를 그리는 `Image` 필드가 없습니다. `Team → Sprite` 매핑 테이블
  (또는 `PlayerTemplate`에 직접 `Sprite TeamLogo` 필드)이 필요합니다. (참고: 치어리더 시스템도 아직 동일한
  스프라이트 매핑이 없어 텍스트로만 표시하고 있어, 선수/치어리더 공용 해결책으로 묶어 검토할 수 있습니다.)
- **선수 얼굴/일러스트 이미지 참조** - `PlayerTemplate`/`PlayerCardUI` 어디에도 초상 이미지(`Sprite`) 필드가
  없습니다. 2-1절의 실명/초상권 리스크와 함께 검토가 필요합니다.
- **연봉(샐러리 코스트) UI 노출** - `Player.CalculateSalaryCost()`가 값은 계산하지만, `PlayerCardUI.Setup()`은
  이 값을 어디에도 표시하지 않습니다(현재 노출 필드는 `ovrText`뿐) - 카드 UI에 "연봉 XX" 형태로 노출할지
  결정 필요.
- **스킬 아이콘/설명 텍스트** - `PlayerCardUI`에는 스킬 관련 필드가 전혀 없습니다. `AcquiredSkillIds`(문자열
  ID 목록)를 실제로 화면에 보여주려면 아이콘(`Sprite`)/설명 텍스트 필드 추가가 필요하며, `SkillDB`(아이콘·
  설명 텍스트 보유 여부는 이번 조사에서 `SkillDB.cs` 전체를 정독하지 못해 미확인)와의 연동 설계가 함께
  필요합니다.
- **등급별 카드 프레임 - 스프라이트 vs 색상 틴트** - `frameImage` 필드는 이미 존재하지만, 실제로는
  `frameImage.color = GetStarTypeColor(...)`로 **단색 틴트**만 입히는 방식입니다(등급별 전용 프레임
  스프라이트 이미지는 없음). 더 화려한 카드 UI를 위해 `StarType`별 프레임 스프라이트를 별도 제작할지는
  아트 리소스 투입 여부에 달린 기획 판단입니다.
- **포지션 한글 표기/아이콘** - `positionText`는 이미 `DescribePosition()`이라는 자체 변환 메서드를 쓰고
  있어(내부 구현은 이번 조사에서 상세 확인하지 못함) 한글 표기가 이미 되어 있을 가능성이 있습니다 - 실제
  출력 문자열이 기획 의도와 일치하는지 확인만 필요합니다.
- **(선택) `players.csv`/`cards.csv`를 실제 `PlayerTemplate` 생성 파이프라인으로 승격** - 치어리더 시스템이
  `cheerleaders.csv → CheerleaderCatalog.Initialize()`로 이미 구현한 것과 동일한 패턴을, 현재 방치된
  `players.csv`/`cards.csv`에도 적용해 "CSV 편집만으로 카드 콘텐츠 추가"가 가능하게 할지는 본 문서 0절의
  [결정 필요]에 대한 답이 정해진 뒤에 검토할 사안입니다.

---

## 4. 요약 - PM 확정 사항 및 남은 결정 지점

### 4-1. TASK-KBO-081에서 확정된 사항

1. ~~체계 A(ScriptableObject `PlayerTemplate`)와 체계 B(CSV `PlayerModel`/`CardModel`) 중 v0.3 스카우트
   상점의 실제 데이터 소스를 무엇으로 할지~~ → **확정: 체계 B(CSV)를 SSOT로 채택, `PlayerTemplate`은
   런타임 브릿지 모델로 용도 변경** (0절)
2. ~~실명 기반 `players.csv` 콘텐츠의 라이선스/초상권 처리 방향~~ → **확정: 비상업적 팬 프로젝트로 규정,
   실명 데이터 그대로 유지** (0절, 2-1절)
3. ~~`docs/11_data_dictionary.md`(CSV 스키마 공식 문서화)와 실제 `PlayerTemplate` 코드 구현 중 어느 쪽을
   기준 문서로 갱신할지~~ → **확정: `docs/11_data_dictionary.md` 3절을 CSV 기반 SSOT 설계로 갱신**
   (본 태스크에서 실행 완료, `docs/11_data_dictionary.md` 참고)

### 4-2. 아직 남은 결정/구현 지점

4. 3절에 나열한 UI 고도화 추가 필드들의 최종 채택 여부(변경 없음, 본 태스크 범위 밖으로 그대로 보존).
5. **[TBD]** `team_id ↔ Team enum`, `position 문자열 ↔ BatterPosition/PitcherRole enum` 매핑표 설계
   (2-1절).
6. **[TBD]** `max_enhance`/`max_awaken` CSV 컬럼을 실제로 읽어와 상수를 대체할지 여부(2-2절).
7. **[TBD]** `players.csv`/`cards.csv` → `PlayerTemplate` 런타임 생성 로직(브릿지 코드) 자체의 실제 구현 -
   이번 태스크는 순수 문서 작업이라 코드는 작성하지 않았습니다. 다음 구현 태스크의 대상입니다.
