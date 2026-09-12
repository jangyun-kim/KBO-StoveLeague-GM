---
문서명: 의사결정 및 변경 로그 (Decision Change Log)
버전: v0.1
상태: Active
최종 수정일: 2026-09-12
담당자: 김장윤
관련 파일: -
---

# 1. 목적

- "왜 수치를 이렇게 바꿨지?", "왜 이 기능을 뒤로 미뤘지?"에 대한 과거의 기획 의도를 추적하여 개발 낭비를 막는다.

# 2. 변경 로그 템플릿 및 예시

| 결재 ID   | 날짜       | 대상 버전 | 주제        | 변경 내용 (이전 → 신규)                                             | 변경 사유 (근거)                                                                         |
| :-------- | :--------- | :-------- | :---------- | :------------------------------------------------------------------ | :--------------------------------------------------------------------------------------- |
| `DCL-001` | 2026-09-12 | v0.1      | 스코프 조정 | 인게임 작전 통제(수동) → 철저한 매치 시뮬레이션 관전(텍스트+미니맵) | 1인 개발 리소스 한계 및 단장(GM) 시뮬레이션이라는 핵심 정체성에 집중하기 위함.           |
| `DCL-002` | 2026-09-12 | v0.1      | 등급 제한   | 8등급 활성화 → 3등급(시즌, 라이브일반, 라이브에픽) 활성화           | 경제 밸런스 붕괴 방지 및 출시 후 업데이트 콘텐츠 분배 목적. (코드 스키마에는 8등급 유지) |
| `DCL-003` | 2026-09-12 | v0.1      | 샐러리캡    | `Player.GradeBaseCostFor()` 잠정(placeholder) 수치(SEASON~DYNASTY 5~30) → GDD v4.0 확정 수치(SEASON 5 / LIVE_NORMAL 5 / LIVE_EPIC 8 / ALLSTAR 12 / TITLE_HOLDER 15 / SIGNATURE 20 / GOLDEN_GLOVE 25 / DYNASTY 35) | TASK-KBO-030에서 밸런스 확정 전 임시로 넣어둔 값을 TASK-KBO-031에서 기획 확정 수치로 동기화. |
| `DCL-004` | 2026-09-12 | v0.1      | 인게임 UX   | PlayMode(빠른 진행/하이라이트 개입/풀 플레이) 3방식 → 단일 논스톱 관전 방식 1종 | DCL-001에서 결정된 "수동 개입 배제"를 TASK-KBO-031에서 코드에 실제 반영. `SubstitutionUIController`/`InterventionController`/`HighlightConditions`/`HighlightConfig` 삭제, `PlayBallController`를 단일 흐름으로 단순화. |
| `DCL-005` | 2026-09-12 | v0.1      | Team OVR    | 28인 중 잉여 3인(타자 2명 + 투수 1명) 제외 후 25인만 평균 반영하는 방식을 공식 스펙으로 확정 | TASK-KBO-031에서 GDD v4.0 확정. 코드(`GameManager.CalculateTeamOVR()`)는 TASK-KBO-030에서 이미 이 방식으로 구현되어 있었고, 이번엔 공식 스펙 확인만 진행(로직 변경 없음). |
| `DCL-006` | 2026-09-12 | v0.1      | Grade Enum 정수값 | `LIVE_NORMAL=0 ~ DYNASTY=6, SEASON=7`(TASK-KBO-031 잠정) → `SEASON=0, LIVE_NORMAL=1, LIVE_EPIC=2, ALLSTAR=3, TITLE_HOLDER=4, SIGNATURE=5, GOLDEN_GLOVE=6, DYNASTY=7`(04_card_grade_policy.md 확정 서열과 완전 일치) | TASK-KBO-032(사전 조사)에서 "Grade는 JSON 세이브에 저장되지 않는다"(TemplateId 참조 구조)는 사실을 확인해, TASK-KBO-031 당시 세이브 호환을 이유로 미뤘던 정수값 재배치의 전제가 사라짐. TASK-KBO-032-IMPLEMENT에서 GDD 확정 서열로 정비. `(int)Grade`가 곧 랭크가 되어 `ScoutManager.RollGradeAtLeast()`의 등급 대소 비교(`>=`)가 안전해짐. **[Inspector 직렬화 주의]** 로컬 에디터에 이미 값이 지정된 씬/프리팹/`.asset`이 있다면 재확인 필요(자세한 내용은 `Types.cs`의 `Grade` enum 주석 참고). |
| `DCL-007` | 2026-09-12 | v0.1      | 가챠 확률표 | `ScoutManager.gradeDropRates`: LIVE_NORMAL 60%/LIVE_EPIC 20%/ALLSTAR 10%/TITLE_HOLDER 5%/GOLDEN_GLOVE 3%/SIGNATURE 1.5%/DYNASTY 0.5%(임시, SEASON 미포함) → SEASON 70%/LIVE_NORMAL 25%/LIVE_EPIC 5%(총 100%, v0.1 활성 등급 3종만) | TASK-KBO-032-IMPLEMENT에서 v0.1 확정 스펙(04_card_grade_policy.md의 "v0.1 활성 등급 3종")에 맞춰 정비. ALLSTAR 이상(v0.5/v2.0 예정)은 확률표에서 제거. |
| `DCL-008` | 2026-09-12 | v0.1      | 레거시 클린업 | `MatchLogger.BuildSubstitutionLog()`/`SubstitutionColor` 삭제 | TASK-KBO-031에서 `InterventionController` 삭제로 호출부를 잃은 죽은 코드를 TASK-KBO-032-IMPLEMENT에서 정리. |
| `DCL-009` | 2026-09-12 | v0.1      | 가챠 기본 등급 | `ScoutManager.RollGrade()`의 확률 총합 0 폴백 및 `ApplyInitialGradeRule()`의 암묵적 default 분기 → 둘 다 `Grade.SEASON`을 명시적 기본값으로 처리(SEASON은 LIVE_NORMAL과 동일 규칙: NORMAL 색상, 1~3성 무작위) | TASK-KBO-032-IMPLEMENT 보고서가 남긴 관찰 사항(SEASON이 이제 가장 흔한 등급인데 폴백은 여전히 LIVE_NORMAL이었음)을 TASK-KBO-033에서 정비. |
| `DCL-010` | 2026-09-12 | v0.1      | 구단 OVR 시너지 | `구단 OVR = 주전15 평균*0.8 + 후보10 평균*0.2` → `... (반올림) + 시너지 합산`. `GameManager.CalculateTeamOVR()` 반환형 `float` → `int`, 반올림은 시너지 가산 "전"에 수행. `CalculateTeamSynergy()`는 세트덱(+12)/감독(+2)/치어리더(+3, 최대 +17)를 위한 자리표시자로 현재 항상 0 반환 | GDD v4.0 구단 OVR 공식의 마지막 단계(시너지 가산)를 TASK-KBO-033에서 구조적으로 이식. 실제 세트덱/감독/치어리더 데이터 매핑은 후속 작업으로 제외. |
