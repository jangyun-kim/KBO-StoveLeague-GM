[TASK-KBO-147, TASK-KBO-152, TASK-KBO-153 갱신] 선수 초상화 리소스 폴더

파일명 규칙(TASK-153부터): {TemplateId}.png (또는 .jpg 등 Unity가 지원하는 이미지 포맷)
- cards.csv에 등록된 선수: TemplateId = card_id (예: CRD_0001.png = 구자욱 SEASON,
  CRD_0002.png = 구자욱 LIVE_EPIC, CRD_0003.png = 구자욱 GOLDEN_GLOVE)
- cards.csv에 아직 등록되지 않은 선수: TemplateId = player_id (예: PLY_0003.png = 김지찬,
  등급 구분 없이 사진 1장만 공유 - 아래 [알려진 한계] 참고)

- PlayerDatabase가 이제 players.csv(선수 물리 데이터)와 cards.csv(카드별 등급 변형)를 조인한다.
  cards.csv 헤더: card_id,player_id,grade_id,grade_name,base_ovr,salary_cost,max_enhance,
  max_awaken,is_droppable. 이 조인으로 TemplateId 자체가 "카드 고유 ID"가 되어(TASK-082 설계
  의도 실현), 동일 선수의 다른 등급 카드가 서로 다른 TemplateId를 갖게 됐다.
- 이 폴더에 해당 파일명의 이미지를 넣기만 하면 PlayerCardUI.Setup()이 자동으로 불러온다
  (코드 수정 불필요, Assets/Scripts/UI/PlayerCardUI.cs의 SetupPortrait() 참고).
- 이미지가 없는 조합은 PlayerCardUI 프리팹의 fallbackPortraitSprite로 자동 대체된다.
- [알려진 한계] 현재 cards.csv 샘플 데이터는 11명 중 2명(구자욱/강민호)만 카드를 등록해 뒀다
  (Assets/Resources/Data/cards.csv 참고). 나머지 선수는 cards.csv에 행을 추가하는 즉시(코드
  수정 없이) 등급별로 구분된다 - 데이터 입력(기획) 과제이지 코드 과제가 아니다(DCL-125 참고).
- base_ovr/salary_cost/max_enhance/max_awaken/is_droppable 컬럼은 아직 강화/샐러리 시스템에
  연동되지 않았다(TASK-153 범위 밖 - PlayerDatabase.cs 상단 주석 참고).

이 파일은 Unity가 빈 폴더를 git에 추적하지 않아 자리를 잡아두는 용도이며, 실제 이미지가
추가되면 삭제해도 무방하다.
