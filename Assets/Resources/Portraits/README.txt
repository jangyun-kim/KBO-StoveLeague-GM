[TASK-KBO-147, TASK-KBO-152, TASK-KBO-153, TASK-KBO-154, TASK-KBO-169 경로 개편 갱신] 선수 초상화
리소스 폴더

⚠ TASK-KBO-169부터 이 폴더는 평평(flat)하지 않다 - 아래 [경로 규칙]을 반드시 지킬 것. 2만 6천 장
규모라 한 폴더에 다 넣으면 안 되고, 파일명만 card_id로 지어서 이 폴더 바로 아래 던져 넣으면
`PlayerCardUI.BuildPortraitResourcePath()`가 절대 찾지 못한다(TASK-170에서 실사 검증 중 실제로
이 실수로 초상화 6장이 전부 로드 실패하는 것을 발견/수정함 - Assets/Scripts/UI/PlayerCardUI.cs
클래스 요약 TASK-170 항목 참고).

[경로 규칙] Portraits/{Team}/{Year}/{TemplateId}.png (또는 .jpg 등 Unity가 지원하는 이미지 포맷)
- {Team}/{Year}는 TemplateId(=card_id) 앞 두 `_` 조각을 그대로 폴더명으로 쓴다.
- cards_{TEAM}.csv에 등록된 선수: TemplateId = card_id, 예: SAMSUNG_2024_PLY_000416_LN
  (형식은 {구단}_{연도}_{선수ID}_{등급코드} - GenerateKBODatabase.py가 생성하는 실제 값을 그대로
  파일명으로 쓰면 된다). 최종 경로 예: `Portraits/SAMSUNG/2024/SAMSUNG_2024_PLY_000416_LN.png`.
- 듀얼 포트레이트(배경 인물) 이미지는 같은 경로에 `_BG` 접미사만 붙인다: `Portraits/SAMSUNG/2024/
  SAMSUNG_2024_PLY_000416_LN_BG.png`.
- 카드가 없는 선수(현재는 없음, 아래 [현황] 참고): TemplateId = player_id로 폴백(이 경우 앞 두
  조각이 Team/Year가 아니게 되어 중첩 경로가 실제 폴더와 안 맞을 수 있음 - PlayerCardUI.cs 주석
  참고, 크래시 없이 fallbackPortraitSprite로 안전하게 처리됨).
- 텍스처 임포트 시 Sprite Mode가 "Multiple"로 자동 지정되는 경우가 있는데(이 프로젝트 임포터
  기본값 - 서브 스프라이트가 파일명과 다르게 생성될 수 있음), `PlayerCardUI.LoadSprite()`가
  `Resources.LoadAll`로 폴백하므로 Single로 수동 변경하지 않아도 정상 로드된다.

- PlayerDatabase가 players.csv(선수 물리 데이터)와 cards_{TEAM}.csv 10장(구단별 카드별 등급
  변형, TASK-154에서 단일 cards.csv를 대체)을 조인한다. 카드 CSV 헤더: card_id,player_id,
  grade_id,grade_name,base_ovr,salary_cost,max_enhance,max_awaken,is_droppable,year. 이 조인으로
  TemplateId 자체가 "카드 고유 ID"가 되고(TASK-082 설계 의도 실현), 같은 선수라도 연도/등급이
  다르면 TemplateId와 SeasonYear가 함께 달라진다.
- 이 폴더에 해당 파일명의 이미지를 넣기만 하면 PlayerCardUI.Setup()이 자동으로 불러온다
  (코드 수정 불필요, Assets/Scripts/UI/PlayerCardUI.cs의 SetupPortrait() 참고).
- 이미지가 없는 조합은 PlayerCardUI 프리팹의 fallbackPortraitSprite로 자동 대체된다.
- [현황, TASK-154] `GenerateKBODatabase.py`가 1986~2026년 10개 구단 전체를 대상으로 대량의
  카드를 생성했다(선수/카드 수는 docs/13_decision_change_log.md DCL-126 참고) - 사실상 모든
  선수가 카드를 최소 1장 이상 보유하므로, player_id 폴백 경로는 이번 생성 데이터 기준으로는
  거의 발생하지 않는다. 다만 실제 초상화 이미지 파일 자체는 이번 작업 범위 밖이라(데이터
  생성/파서 개편만 수행) 아직 하나도 채워지지 않았다 - 지금은 모든 카드가
  fallbackPortraitSprite로 표시된다.
- base_ovr/salary_cost/max_enhance/max_awaken/is_droppable 컬럼은 아직 강화/샐러리 시스템에
  연동되지 않았다(TASK-153/154 범위 밖 - PlayerDatabase.cs 상단 주석 참고).

이 파일은 Unity가 빈 폴더를 git에 추적하지 않아 자리를 잡아두는 용도이며, 실제 이미지가
추가되면 삭제해도 무방하다.
