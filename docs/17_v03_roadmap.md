---
문서명: v0.3 마일스톤 로드맵 (선수 카드 스토브리그 & 인게임 UI)
버전: v0.3 (Draft)
상태: Draft
최종 수정일: 2026-09-14
담당자: 김장윤
관련 파일: 13_decision_change_log.md, 00_release_notes.md
---

# v0.3 마일스톤 로드맵

## 0. 시작하며 - 실측 조사 결과 (중요)

이 로드맵을 작성하기 전, 명령서(TASK-KBO-079) 3항이 전제한 "확인된 사실"을 실제 코드베이스와 대조했습니다.
그 결과 **명령서의 전제와 실제 코드 상태가 다르다는 점을 확인**했습니다. 임의로 판단하지 않고 아래에
[결정 필요]로 명시합니다.

> **[결정 필요]** 명령서는 "선수는 단순 ID/더미 스탯이고, 가챠·라인업 배치 시스템이 부족하다"고 전제했지만,
> 실제로는 선수 카드 데이터 모델, 가챠(스카우트), 28인 로스터 자동 편성, 강화/각성, 포스트시즌, 시즌 결산
> 보상까지 **백엔드와 UI 컨트롤러 코드가 이미 광범위하게 존재**합니다(아래 표). 다만 이 화면들은 현재 씬
> (`SampleScene.unity`)에 패널로 배선되어 있지 않고, `UIManager.screens`에도 등록되어 있지 않아 유저가
> 실제로 접근할 수 없는 "죽은 코드" 상태로 추정됩니다. 즉 v0.3의 핵심 과제는 "0에서 시스템을 새로 만드는
> 것"이 아니라 **"이미 존재하는 시스템을 씬에 배선하고, 진입 동선을 연결하고, 실제로 동작하는지 검증"**하는
> 것에 훨씬 가깝습니다. 아래 로드맵은 이 실측 결과를 반영해 작성했습니다 - 이 전제가 맞는지, 그리고
> 페이즈 순서/범위가 이 전제 위에서 여전히 타당한지 기획 확인이 필요합니다.

### 실측으로 확인된 기존 코드 자산

| 영역 | 확인된 파일/클래스 | 상태(추정) |
|---|---|---|
| 선수 데이터 모델 | `Assets/Scripts/Models/Player.cs`, `Assets/Scripts/Data/PlayerTemplate.cs` | 완성(강화/각성/성급/스킬/스태미나/OVR 계산까지 구현) |
| 선수 가챠 | `Assets/Scripts/Managers/ScoutManager.cs`, `Assets/Scripts/Controllers/ScoutUIController.cs`, `Assets/Scripts/UI/PlayerCardUI.cs` | 완성(코드) / 씬 미배선 |
| 로스터/라인업 | `Assets/Scripts/Managers/RosterManager.cs`(28인 자동 편성), `Assets/Scripts/Controllers/RosterUIController.cs`, `Assets/Scripts/Controllers/GameActionController.cs`, `Assets/Scripts/Controllers/InventoryUIController.cs` | 완성(코드, 단 자동 편성 위주) / 씬 미배선 |
| 강화/각성 | `Assets/Scripts/Managers/UpgradeManager.cs` | 완성(코드) / 연결 UI 미확인 |
| 인게임 매치 UI | `Assets/Scripts/Controllers/InGameUIController.cs`(스코어보드/중계 로그/결과 패널), `Assets/Scripts/Controllers/BroadcastUIManager.cs`(PlayEvent 재생), `Assets/Scripts/Controllers/PlayBallController.cs`, `Assets/Scripts/UI/MatchStatusUI.cs` | 완성(코드) / `InGamePanel` 내부 UI 요소(텍스트 그리드/스크롤뷰 등) 씬 배선 미확인 |
| 시즌 결산/포스트시즌 | `Assets/Scripts/Managers/SeasonRewardManager.cs`, `Assets/Scripts/Controllers/SeasonEndReportUIController.cs`, `Assets/Scripts/Managers/PostSeasonManager.cs`, `Assets/Scripts/Managers/LeagueCalendar.cs` | 완성(코드) / 씬 미배선 |
| 씬 배선 자동화 툴 | `Assets/Scripts/Editor/SceneInitializer.cs`(Onboarding/Lobby/InGame/Stats/Shop 5개 패널만 커버) | Roster/Scout/Inventory/SeasonEnd 패널용 Editor 자동화 스크립트는 아직 **없음**(TASK-KBO-075~077에서 만든 `SetupUIManager.cs`/`FixDuplicateManagers.cs`도 이 4개 패널은 다루지 않음) |

`UIManager.ScreenType` enum에는 `Roster`/`Scout`/`Inventory` 값이 이미 예약되어 있으나(`Assets/Scripts/Managers/UIManager.cs`),
현재 `UIManager.screens`에는 등록되어 있지 않고 코드 어디에서도 `ScreenType.Roster`/`ScreenType.Scout`을 참조하지 않습니다(전수 검색 확인).

---

## Phase 1: 선수 데이터 및 카드 가챠 시스템

이미 존재하는 `ScoutManager`/`ScoutUIController`/`PlayerCardUI`를 씬에 연결하고 실제 콘텐츠로 채우는 단계.

- `ScoutPanel` 씬 배선 - 기존 `SetupCheerleaderUI.cs`/`SetupShopUI.cs` 선례를 참고해 `Assets/Scripts/Editor/`에 신규 Editor 자동화 스크립트 작성(패널 생성 + `ScoutUIController` 필드 바인딩)
- `UIManager.screens`에 `ScreenType.Scout` 등록(기존 `SetupUIManager.cs`의 `Bindings` 배열 확장 여지 검토)
- 로비 화면(`LeagueDashboardUIController`)에 "선수 스카우트" 진입 버튼 연결 - 기존 `gachaShopButton`/`manageCheerleaderButton` 패턴 재사용
- `PlayerDatabase`/`players.csv`(`Assets/Resources/Data/players.csv`)에 실제 KBO 선수 기반 콘텐츠가 채워져 있는지, 아니면 플레이스홀더 상태인지 데이터 점검
- 기존 GDD(`docs/04_card_grade_policy.md`)의 등급/확률 정책과 `ScoutManager.gradeDropRates`(현재 코드상 SEASON 70% / LIVE_NORMAL 25% / LIVE_EPIC 5%)가 일치하는지 문서-코드 대조

## Phase 2: 로스터 및 라인업 관리 UI

이미 존재하는 `RosterManager`(28인 자동 편성)/`RosterUIController`/`GameActionController`/`InventoryUIController`를 씬에 연결하는 단계.

- `RosterPanel`/`InventoryPanel` 씬 배선 Editor 자동화 스크립트 작성
- `UIManager.screens`에 `ScreenType.Roster`(및 필요 시 `Inventory`) 등록, 로비 진입 버튼 연결
- **[결정 필요]** 현재 `RosterManager.AutoSetRoster()`는 "샐러리 캡 이내 최고 OVR 자동 편성"만 지원한다 - 유저가 타순/포지션을 수동으로 직접 바꾸는 UI까지 v0.3 범위에 포함할지, 아니면 자동 편성 결과를 확인만 하는 화면으로 우선 출시할지 기획 확인 필요
- `UpgradeManager`(강화/각성)를 실제로 호출하는 UI 컨트롤러가 존재하는지 추가 조사, 없다면 이번 로드맵의 별도 Task로 분리할지 검토

## Phase 3: 인게임 매치 UI 연동

이미 존재하는 `InGameUIController`(스코어보드/중계 로그/결과 패널)와 `BroadcastUIManager`(PlayEvent 재생), `PlayBallController`(오케스트레이션)를 `InGamePanel`에 실제로 배선하는 단계.

- `InGamePanel` 내부에 이닝별 스코어보드 텍스트 그리드, 중계 로그 스크롤뷰, 경기 종료 결과 패널 등 실제 UI 요소를 생성하고 `InGameUIController`의 직렬화 필드에 바인딩하는 Editor 자동화 스크립트 작성(`SceneInitializer.cs`는 현재 빈 `InGamePanel` 루트만 만들고 내부는 비어 있음)
- `InGameUIController.RefreshScoreboard()` 코드 주석이 이미 명시한 기존 한계("완전한 실시간 순차 공개는 `MatchEngine`/`PlayEvent` 스키마 확장 없이는 불가능") 해소를 v0.3 범위에 포함할지 **[결정 필요]**
- 로비 → "관전 시작" 버튼(`LeagueDashboardUIController.quickPlayButton` → `PlayBallController.StartMatch()`) → 실제 경기 1건 관전까지 엔드투엔드 QA 검증

## Phase 4: 144경기 시즌 결산

이미 존재하는 `SeasonRewardManager`/`SeasonEndReportUIController`/`PostSeasonManager`/`LeagueCalendar`를 씬에 연결하고 전체 순위 구간을 검증하는 단계.

- `SeasonEndReport` 팝업 UI 씬 배선 Editor 자동화 스크립트 작성(로비 화면 위 오버레이로 배치)
- 포스트시즌 진출(1~5위, `PostSeasonManager.OnChampionDecided` 경로)과 미진출(6~10위, `LeagueManager.OnSeasonFinalized` 경로) 두 갈래 모두 보상 지급이 정상 동작하는지 엔드투엔드 검증
- 기존 "144경기 헤드리스 스킵" 디버그 기능(`DebugPanelUI`)과 정식 시즌 진행 흐름 사이의 정합성 확인
- **[결정 필요]** 시즌 종료 후 다음 시즌으로 넘어갈 때 로스터/재화/선수 성장치를 그대로 이월할지, 스토브리그 리셋(방출/재계약 등)을 적용할지는 기획 미정 - v0.3에서 결정 필요

---

## 기술 방향성 참고

- 신규 화면은 모두 기존 `GameManager`/`UIManager` 싱글톤 패턴과 `ScreenType` enum + `UIManager.screens` 리스트 등록 방식을 그대로 따른다(이미 모든 기존 화면이 이 패턴을 사용 중).
- 씬 배선은 매번 수작업 대신 `Assets/Scripts/Editor/` 하위에 `Setup*.cs` Editor 자동화 스크립트를 만드는 기존 관례(`SetupCheerleaderUI.cs`, `SetupShopUI.cs`, `SetupUIManager.cs` 등)를 그대로 따른다.
- 이번 로드맵에서 언급한 클래스들은 모두 현재 코드베이스에 실재하는 파일이며(2026-09-14 기준 조사), 신규 가상 클래스/메서드는 제안하지 않았다.

## Next Steps 순서 근거

일반적인 모바일 야구 매니지먼트 게임의 코어 루프(카드 수집 → 라인업 편성 → 경기 관전 → 보상 획득 → 재수집)를 따라
Phase 1(가챠) → Phase 2(로스터) → Phase 3(관전) → Phase 4(시즌 보상) 순서로 배치했다. 다만 위 실측 결과에 따라
각 Phase는 "신규 구축"이 아니라 "기존 코드의 씬 배선·검증"이 중심이므로, 실제 작업량은 예상보다 적고 병렬 진행도
가능할 수 있다 - 이 부분도 착수 전 기획 확인이 필요하다.
