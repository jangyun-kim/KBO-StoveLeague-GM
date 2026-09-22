[TASK-KBO-147, TASK-KBO-152 갱신] 선수 초상화 리소스 폴더

파일명 규칙(TASK-152부터): {TemplateId}_{CurrentStarType}.png (또는 .jpg 등 Unity가 지원하는 이미지 포맷)
예: PLY_0001_PLATINUM.png (시그니처 등급 구자욱), PLY_0001_NORMAL.png (시즌/라이브 등급 구자욱),
    PLY_0008_GOLD.png (골든글러브 등급 원태인)

- TemplateId는 PlayerDatabase가 CSV의 player_id를 그대로 채운 값이다(cards.csv 조인 전까지는
  선수 1명당 값 1개 - DCL-057 참고). players.csv의 player_id 컬럼과 1:1로 대응한다.
- CurrentStarType은 뽑기 시 등급에 따라 ScoutManager.ApplyInitialGradeRule()이 개별 카드
  인스턴스마다 실제로 채우는 값이다 - 아래처럼 매핑된다(Player.cs/PlayerCardUI.cs 참고):
    SEASON, LIVE_NORMAL, LIVE_EPIC        -> NORMAL
    ALLSTAR                                -> PURPLE
    TITLE_HOLDER                           -> SILVER
    GOLDEN_GLOVE                           -> GOLD
    SIGNATURE                              -> PLATINUM
    DYNASTY                                -> TEAM_COLOR
  즉 같은 선수(TemplateId)라도 뽑힌 등급이 다르면 오늘 시점에도 이미 다른 파일명으로 로드된다.
- 이 폴더에 해당 파일명의 이미지를 넣기만 하면 PlayerCardUI.Setup()이 자동으로 불러온다
  (코드 수정 불필요, Assets/Scripts/UI/PlayerCardUI.cs의 SetupPortrait() 참고).
- 이미지가 없는 조합은 PlayerCardUI 프리팹의 fallbackPortraitSprite로 자동 대체된다.
- [알려진 한계] PlayerDatabase가 아직 cards.csv를 조인하지 않아, "24년 라이브 구자욱"과
  "24년 골든글러브 구자욱"은 현재 동일한 TemplateId("PLY_0001")를 공유한다 - 이번 키 변경
  덕분에 CurrentStarType이 달라 사진 자체는 이미 구분되지만("PLY_0001_NORMAL.png" vs
  "PLY_0001_GOLD.png"), 카드 앞면 텍스트(이름/스탯 등)는 여전히 동일한 원본 데이터를 공유한다
  (별도 후속 작업 대상, DCL-057).

이 파일은 Unity가 빈 폴더를 git에 추적하지 않아 자리를 잡아두는 용도이며, 실제 이미지가
추가되면 삭제해도 무방하다.
