[TASK-KBO-147] 선수 초상화 리소스 폴더

파일명 규칙: {TemplateId}.png (또는 .jpg 등 Unity가 지원하는 이미지 포맷)
예: PLY_0001.png  (구자욱), PLY_0008.png (원태인)

- TemplateId는 PlayerDatabase가 CSV의 player_id를 그대로 채운 값이다(cards.csv 조인 전까지는
  카드 등급과 무관하게 선수 1명당 값 1개 - DCL-057 참고). players.csv의 player_id 컬럼과
  1:1로 대응한다.
- 이 폴더에 해당 파일명의 이미지를 넣기만 하면 PlayerCardUI.Setup()이 자동으로 불러온다
  (코드 수정 불필요, Assets/Scripts/UI/PlayerCardUI.cs의 SetupPortrait() 참고).
- 이미지가 없는 선수는 PlayerCardUI 프리팹의 fallbackPortraitSprite로 자동 대체된다.

이 파일은 Unity가 빈 폴더를 git에 추적하지 않아 자리를 잡아두는 용도이며, 실제 이미지가
추가되면 삭제해도 무방하다.
