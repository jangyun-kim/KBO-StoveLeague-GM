# -*- coding: utf-8 -*-
"""
[TASK-KBO-154] KBO 마스터 DB(선수/카드/치어리더) 자동 생성 스크립트.

이 스크립트는 Assets/Resources/Data/ 아래에 다음 CSV들을 "일괄 덮어쓰기"로 생성한다(명령서 4항):
  - players.csv            : 선수 물리 데이터 SSOT. PlayerDatabase.ParsePlayersCsv()가 기대하는
                              16컬럼 스키마(player_id,team_id,name,year,position,pa_ip,z_contact,
                              z_eye,z_power,z_speed,z_def,z_stamina,active,z_stuff,z_control,
                              z_movement)를 그대로 따른다 - 이 스키마는 이번 작업 범위(ParseCardsCsv만
                              수정)에 포함되지 않으므로 절대 바꾸지 않는다.
  - cards_{TEAM}.csv (x10) : 구단별 카드 CSV. 헤더는 기존 cards.csv의 9컬럼에 `year`(카드가 발급된
                              실제 시즌 연도)를 10번째 컬럼으로 추가한 것이다 - PlayerDatabase.
                              ParseCardsCsv()도 이 10컬럼을 읽도록 함께 개편했다(같은 커밋).
  - cheerleaders.csv       : [사실 정정] 기존 파일은 CheerleaderCatalog.cs가 실제로 기대하는 스키마
                              (CatalogId,Name,Grade,ConditionBuff,EconomicBonusRate,ClutchMultiplier,
                              SentimentDefense - 7컬럼)와 전혀 다른 5컬럼(cheer_id,name,team,
                              season_name,cheer_power)으로 되어 있어, 사실상 모든 행이 파싱 단계에서
                              컬럼 수 부족으로 통째로 스킵되는 죽은 데이터였다(CheerleaderCatalog.cs
                              ExpectedColumnCount=7 참고). 이번 재생성에서 실제 파서 스키마에 맞춰
                              올바르게 작성한다 - CheerleaderCatalog.cs 자체는 프론트엔드/기존 시스템이라
                              건드리지 않았다(명령서 5항).

원본 데이터가 옳고 그름을 검증할 수 없는 40년치 실제 KBO 전체 로스터를 완벽히 재현하는 것은
불가능하므로(그런 정밀도의 사료를 이 세션이 신뢰성 있게 갖고 있지 않음), 명령서 3항이 명시한
두 "왕조" 로스터(1986~1989 해태/KIA, 2011~2014 삼성)는 실제 인물명을 그대로 사용하고, 그 외
방대한 물량은 "가상의(또는 실제) 주력 선수 풀"이라는 명령서의 허용 범위를 따라 조합형으로 생성한
가상 인물로 채운다 - docs/13_decision_change_log.md의 이번 작업 DCL 항목에 이 설계 결정을
명시했다.
"""

import csv
import os
import random
from collections import Counter

random.seed(20260922)  # 재현 가능한 결과(재실행해도 동일 DB) - 명령서에 시드 요구는 없으나 디버깅 편의상 고정.

# ---------------------------------------------------------------------------
# 0. 경로
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
# [TASK-KBO-172] 환경 변수 KBO_DB_OUTPUT_DIR로 출력 경로를 바꿀 수 있다(기본값은 기존과 동일) -
# 실제 Data 폴더를 덮어쓰기 전에 임시 폴더로 생성해 기존 CSV와 diff 검증하기 위함.
OUTPUT_DIR = os.environ.get("KBO_DB_OUTPUT_DIR") or os.path.join(SCRIPT_DIR, "Assets", "Resources", "Data")

# ---------------------------------------------------------------------------
# 1. 구단 정의 - PlayerDatabase.cs의 TeamIdMapping과 정확히 1:1 대응시킨다.
# ---------------------------------------------------------------------------
# (team_id, Team enum 이름, cards_{TOKEN}.csv 파일명 토큰)
TEAMS = [
    ("TEM_001", "KIA", "KIA"),
    ("TEM_002", "Samsung", "SAMSUNG"),
    ("TEM_003", "LG", "LG"),
    ("TEM_004", "Doosan", "DOOSAN"),
    ("TEM_005", "KT", "KT"),
    ("TEM_006", "SSG", "SSG"),
    ("TEM_007", "Lotte", "LOTTE"),
    ("TEM_008", "Hanwha", "HANWHA"),
    ("TEM_009", "NC", "NC"),
    ("TEM_010", "Kiwoom", "KIWOOM"),
]

MIN_YEAR = 1986
MAX_YEAR = 2026

# ---------------------------------------------------------------------------
# 2. 카드 등급(Grade) 메타데이터
# ---------------------------------------------------------------------------
# [TASK-KBO-155, 사용자 직접 지시] SEASON 전면 삭제, RETIRED_NUMBER(영구결번) 신설을 반영했다 -
# DYNASTY와 RETIRED_NUMBER는 아래 "필수 로스터" 전용이라 일반 확률 풀에서는 절대 뽑히지 않는다
# (역대 우승 주역/영구결번이라는 설정을 코드로도 지킨다 - RETIRED_NUMBER의 실제 후보 명단은 다음
# 단계(실제 선수 데이터 리서치)에서 채워질 예정이라 이 스크립트는 아직 후보를 만들지 않는다).
# base_ovr 범위와 salary_cost/max_enhance/max_awaken/is_droppable은 docs/04_card_grade_policy.md
# (등급 서열)와 Models/Player.cs(MaxReinforceLevel=10/MaxAwakenLevel=10, CanAwaken 규칙)에 맞춰
# 이번 스크립트가 직접 정의했다 - PM 문서에 정확한 수치가 없어 자체 설계한 값이라는 점을 DCL에
# 명시했다.
# [TASK-KBO-172] max_awaken = 각성 한계. 각성은 1~9각 + 초월(내부 값 10)로 개편됐다(기획 고도화 자료.pdf
# "기존 10각을 초월로 명칭 변경"). 초월 가능 등급(LIVE_NORMAL/LIVE_EPIC/GOLDEN_GLOVE/SIGNATURE/DYNASTY)은
# 10, 9각 한계 등급(ALLSTAR/FRANCHISE/TITLE_HOLDER)은 9다 - [TASK-KBO-174] RETIRED_NUMBER는 초월 가능(10)으로 격상 - C# SetDeckRules.MaxAwakenLevelFor()와
# 반드시 일치해야 한다. LIVE는 이전(0 = 각성 불가)과 달리 초월까지 성장 가능하지만, 실전 OVR은
# ALLSTAR 9각 동급으로 상한이 걸린다(Player.cs의 LIVE 성장 상한 참고).
# FRANCHISE(프랜차이즈, TASK-KBO-172 신설)는 ALLSTAR와 TITLE_HOLDER 사이의 중상위~상위 등급이다.
GRADE_META = {
    "LIVE_NORMAL":    {"code": "LN",   "ovr": (55, 68), "salary": 12, "max_enhance": 10, "max_awaken": 10, "droppable": "TRUE"},
    "LIVE_EPIC":      {"code": "EPIC", "ovr": (65, 76), "salary": 18, "max_enhance": 10, "max_awaken": 10, "droppable": "TRUE"},
    "ALLSTAR":        {"code": "AS",   "ovr": (74, 83), "salary": 24, "max_enhance": 10, "max_awaken": 9,  "droppable": "TRUE"},
    "FRANCHISE":      {"code": "FRA",  "ovr": (78, 86), "salary": 27, "max_enhance": 10, "max_awaken": 9,  "droppable": "FALSE"},
    "TITLE_HOLDER":   {"code": "TH",   "ovr": (80, 87), "salary": 30, "max_enhance": 10, "max_awaken": 9,  "droppable": "FALSE"},
    "RETIRED_NUMBER": {"code": "RN",   "ovr": (85, 91), "salary": 33, "max_enhance": 10, "max_awaken": 10, "droppable": "FALSE"},
    "GOLDEN_GLOVE":   {"code": "GG",   "ovr": (83, 90), "salary": 35, "max_enhance": 10, "max_awaken": 10, "droppable": "FALSE"},
    "SIGNATURE":      {"code": "SIG",  "ovr": (87, 94), "salary": 40, "max_enhance": 10, "max_awaken": 10, "droppable": "FALSE"},
    "DYNASTY":        {"code": "DYN",  "ovr": (92, 99), "salary": 50, "max_enhance": 10, "max_awaken": 10, "droppable": "FALSE"},
}
# [TASK-KBO-173, Salary ↔ 세트덱 스코어 일원화] salary_cost 컬럼 = 등급 기본(명함) 세트덱 스코어. 역전 밸런스
# (종결 카드일수록 낮고 LIVE가 가장 높음) - C# CardGrowthRules.BaseSetDeckScore()와 반드시 일치해야 한다(불일치 시
# PlayerDatabase가 경고). 위 GRADE_META의 salary 값(구 샐러리 12~50)은 아래 루프가 이 값으로 덮어쓴다.
SETDECK_BASE_SCORE = {
    "LIVE_NORMAL": 4, "LIVE_EPIC": 4, "ALLSTAR": 4, "FRANCHISE": 3, "TITLE_HOLDER": 3,
    "RETIRED_NUMBER": 2,  # [TASK-KBO-174 확정] 기본 2 -> 초월 6(초월 +4) - 구단 성골 우대
    "GOLDEN_GLOVE": 2, "SIGNATURE": 1, "DYNASTY": 1,
}
for _grade, _meta in GRADE_META.items():
    _meta["salary"] = SETDECK_BASE_SCORE[_grade]

# Types.cs의 Grade enum 정수값과 동일한 서열. [TASK-KBO-172] FRANCHISE(4)를 ALLSTAR와 TITLE_HOLDER
# 사이에 끼워 넣으며 TITLE_HOLDER 이상이 한 칸씩 밀렸다(LIVE_NORMAL=1 ~ DYNASTY=9).
GRADE_ID = {
    "LIVE_NORMAL": 1, "LIVE_EPIC": 2, "ALLSTAR": 3, "FRANCHISE": 4, "TITLE_HOLDER": 5,
    "RETIRED_NUMBER": 6, "SIGNATURE": 7, "GOLDEN_GLOVE": 8, "DYNASTY": 9,
}

# [TASK-KBO-172] 기존 난수 스트림(random.seed(20260922))을 1바이트도 흔들지 않기 위해, 이번 작업에서
# 새로 생기는 선수 등록/카드 발급(FRANCHISE, TITLE_HOLDER 1986~2012 확장, DYNASTY 25장(TASK-174), SIGNATURE
# 쿼터 보강)은 전부 이 전용 RNG만 쓴다 - 그래야 기존 선수 ID(예: 구자욱 PLY_004038 초상화 매핑)와
# 기존 카드 base_ovr가 그대로 보존된다.
rng172 = random.Random(20261001)

# 일반 확률 풀(DYNASTY 제외) - 누적 100%.
RANDOM_GRADE_WEIGHTS = [
    ("LIVE_NORMAL", 38), ("LIVE_EPIC", 27), ("ALLSTAR", 16),
    ("TITLE_HOLDER", 10), ("GOLDEN_GLOVE", 5), ("SIGNATURE", 4),
]

def roll_grade():
    r = random.uniform(0, 100)
    cumulative = 0.0
    for grade, weight in RANDOM_GRADE_WEIGHTS:
        cumulative += weight
        if r <= cumulative:
            return grade
    return RANDOM_GRADE_WEIGHTS[-1][0]

# ---------------------------------------------------------------------------
# 3. 왕조(Dynasty) 필수 로스터 - 명령서 3항 원문 그대로, 누락 없이 사용한다(AC-02).
# ---------------------------------------------------------------------------
DYNASTY_ROSTERS = {
    # (team_token, 시작연도, 끝연도): [(이름, 포지션), ...]
    "SAMSUNG": {
        "years": (2011, 2014),
        "batters": [
            ("채태인", "1B"), ("박석민", "3B"), ("김상수", "SS"), ("최형우", "LF"),
            ("박한이", "RF"), ("이승엽", "DH"), ("야마이코 나바로", "2B"),
        ],
        "pitchers": [
            ("윤성환", "SP"), ("장원삼", "SP"), ("릭 밴덴헐크", "SP"), ("안지만", "RP"),
            ("권혁", "RP"), ("오승환", "CP"), ("차우찬", "SP"),
        ],
    },
    "KIA": {
        "years": (1986, 1989),
        "batters": [
            ("김성한", "1B"), ("이순철", "SS"), ("한대화", "3B"), ("김종모", "RF"),
            ("김봉연", "DH"), ("장채근", "CF"), ("서정환", "2B"), ("백인호", "C"),
        ],
        "pitchers": [
            ("선동열", "SP"), ("김정수", "SP"), ("문희수", "SP"), ("차동철", "RP"),
            ("신동수", "RP"), ("이강철", "SP"), ("조계현", "RP"),
        ],
    },
}
# [TASK-KBO-172] 위 DYNASTY_ROSTERS는 이제 "왕조 시기 실존 인물 등록 순서"만 담당한다(선수 ID 시퀀스
# PLY_000001~ 보존 - 여기서 인원을 빼거나 넣으면 이후 모든 player_id가 밀려 구자욱 초상화 매핑이 깨진다).
# 실제 DYNASTY 카드 발급은 아래 DYNASTY_CARDS(1인 1연도 정예 25장 - 삼성 13 + 해태 12, TASK-174)만 따른다 - 이전의 "왕조 구간 4개
# 연도 전부 발급(29명 x 4 = 116장)" 방식은 같은 선수의 DYN이 4장씩 겹쳐 DB 충돌을 일으켜 폐기했다.
# 사용자 확정 명단(명령서 STEP 3-4) 그대로이며, 타 구단은 DYNASTY가 없다.
# (이름, team_token, 대표 연도, is_pitcher, 신규 등록 시 포지션)
DYNASTY_CARDS = [
    # 삼성 2011~2014 (13인)
    ("최형우", "SAMSUNG", 2014, False, "LF"), ("박석민", "SAMSUNG", 2014, False, "3B"),
    ("김상수", "SAMSUNG", 2014, False, "SS"), ("박해민", "SAMSUNG", 2014, False, "CF"),
    ("채태인", "SAMSUNG", 2013, False, "1B"), ("야마이코 나바로", "SAMSUNG", 2014, False, "2B"),
    ("오승환", "SAMSUNG", 2011, True, "CP"), ("차우찬", "SAMSUNG", 2011, True, "SP"),
    ("심창민", "SAMSUNG", 2013, True, "RP"), ("권오준", "SAMSUNG", 2012, True, "RP"),
    ("정현욱", "SAMSUNG", 2011, True, "RP"), ("안지만", "SAMSUNG", 2014, True, "RP"),
    ("윤성환", "SAMSUNG", 2014, True, "SP"),
    # 해태(KIA) 1986~1989 (12인 = 타선 7 + 투수 5) - [TASK-KBO-174] 역사 고증 교정. 이종범(1993년 입단이라
    # 1986~1989 왕조 시대에 없음)·송유석·김상진을 빼고 김봉연·박철우·이강철·차동철을 넣었다. 네 명 모두
    # 이미 등록된 인물(왕조 시기 로스터/1989 골든글러브)이라 신규 player_id가 생기지 않는다.
    ("김성한", "KIA", 1988, False, "1B"), ("장채근", "KIA", 1988, False, "C"),
    ("한대화", "KIA", 1989, False, "3B"), ("김종모", "KIA", 1986, False, "RF"),
    ("김봉연", "KIA", 1986, False, "DH"), ("이순철", "KIA", 1988, False, "CF"),
    ("박철우", "KIA", 1989, False, "DH"),
    ("선동열", "KIA", 1986, True, "SP"), ("김정수", "KIA", 1987, True, "SP"),
    ("이강철", "KIA", 1989, True, "SP"), ("문희수", "KIA", 1988, True, "SP"),
    ("차동철", "KIA", 1987, True, "RP"),
]

# ---------------------------------------------------------------------------
# 4. 가상 선수 이름 생성기(그 외 로스터 물량용) - 조합형이라 실존 인물 특정과 무관하다.
# ---------------------------------------------------------------------------
SURNAMES = [
    "김", "이", "박", "최", "정", "강", "조", "윤", "장", "임", "한", "오", "서", "신", "권",
    "황", "안", "송", "전", "홍", "유", "고", "문", "양", "손", "배", "백", "허", "남", "심",
]
GIVEN_SYLLABLES = [
    "민준", "서준", "도윤", "시우", "주원", "하준", "지호", "준서", "건우", "현우",
    "지훈", "동현", "성민", "재현", "우진", "승민", "준영", "영훈", "태양", "광수",
    "정훈", "수현", "민재", "진우", "현준", "동욱", "성훈", "재원", "승우", "태민",
    "윤호", "경수", "용준", "상현", "동원", "재영", "성진", "민수", "찬희", "동민",
]

_used_names = set()

def make_player_name():
    for _ in range(50):  # 충돌 시 재시도, 50회 넘게 겹치면 그냥 허용(조합 공간이 충분히 넓음)
        name = random.choice(SURNAMES) + random.choice(GIVEN_SYLLABLES)
        if name not in _used_names:
            _used_names.add(name)
            return name
    return random.choice(SURNAMES) + random.choice(GIVEN_SYLLABLES)

BATTER_POSITIONS = ["1B", "2B", "3B", "SS", "LF", "CF", "RF", "C", "DH"]
PITCHER_POSITIONS = ["SP", "RP", "CP"]

# ---------------------------------------------------------------------------
# 5. 선수 풀 구성 - 각 팀마다 (왕조 필수 로스터가 있다면 그것 + 대량의 가상 선수)
# ---------------------------------------------------------------------------
SYNTHETIC_PLAYERS_PER_TEAM = 400

class PlayerRecord:
    __slots__ = ("player_id", "team_token", "team_id", "team_enum", "name", "is_pitcher",
                 "position", "career_start", "career_end", "z_value", "pa_ip", "is_dynasty_member",
                 "dynasty_years", "skip_random_cards")

player_records = []
_player_seq = 0

def next_player_id():
    global _player_seq
    _player_seq += 1
    return f"PLY_{_player_seq:06d}"

def add_player(team_token, team_id, team_enum, name, is_pitcher, position,
                career_start, career_end, is_dynasty_member=False, dynasty_years=None,
                skip_random_cards=False, rng=random):
    # [TASK-KBO-172] rng - TASK-172 신규 등록 인물은 rng172를 넘겨 기존 전역 난수 스트림을 보존한다.
    rec = PlayerRecord()
    rec.player_id = next_player_id()
    rec.team_token = team_token
    rec.team_id = team_id
    rec.team_enum = team_enum
    rec.name = name
    rec.is_pitcher = is_pitcher
    rec.position = position
    rec.career_start = career_start
    rec.career_end = career_end
    # [TASK-KBO-153 관례 계승] 기존 sample players.csv는 한 선수의 z_* 6개 컬럼에 동일한 값을
    # 반복해 채웠다(예: 구자욱 행 전부 2.133) - 이번 대량 생성은 선수마다 "재능치" 하나를 뽑아
    # 6개 컬럼에 소폭의 독립 잡음을 더해 반복하는 방식으로 그 관례를 자연스럽게 확장했다(완전히
    # 같은 값을 반복하는 것보다 게임 데이터로서 약간 더 자연스럽고, 컬럼 의미 자체는 그대로다).
    rec.z_value = rng.gauss(1.0, 0.6)
    rec.pa_ip = rng.randint(80, 180) if is_pitcher and position == "SP" else \
        (rng.randint(30, 70) if is_pitcher else rng.randint(200, 600))
    rec.is_dynasty_member = is_dynasty_member
    rec.dynasty_years = dynasty_years
    # [TASK-KBO-156] 실제 검증된 선수(아래 "리서치 2단계" 절)는 확률 기반 무작위 카드를 받지
    # 않는다 - 실제 수상 이력만으로 정확히 구성된 카드만 갖는다(가짜 카드가 섞이는 것을 방지).
    rec.skip_random_cards = skip_random_cards
    player_records.append(rec)
    return rec

for team_id, team_enum, team_token in TEAMS:
    # -- 왕조 필수 로스터(있는 팀만) --
    dynasty = DYNASTY_ROSTERS.get(team_token)
    if dynasty is not None:
        dyn_start, dyn_end = dynasty["years"]
        # 왕조 시기 앞뒤로도 몇 년씩 커리어를 넓혀 일반 카드도 함께 생성되도록 한다(1986년 미만은
        # 데이터셋 최소 연도라 자를 수 없다).
        career_start = max(MIN_YEAR, dyn_start - 5)
        career_end = min(MAX_YEAR, dyn_end + 5)
        for name, pos in dynasty["batters"]:
            add_player(team_token, team_id, team_enum, name, False, pos,
                       career_start, career_end, is_dynasty_member=True, dynasty_years=(dyn_start, dyn_end))
        for name, pos in dynasty["pitchers"]:
            add_player(team_token, team_id, team_enum, name, True, pos,
                       career_start, career_end, is_dynasty_member=True, dynasty_years=(dyn_start, dyn_end))

    # -- 그 외 대량 가상 로스터 --
    for _ in range(SYNTHETIC_PLAYERS_PER_TEAM):
        is_pitcher = random.random() < 0.4
        position = random.choice(PITCHER_POSITIONS) if is_pitcher else random.choice(BATTER_POSITIONS)
        career_start = random.randint(MIN_YEAR, 2023)
        career_end = min(MAX_YEAR, career_start + random.randint(3, 20))
        add_player(team_token, team_id, team_enum, make_player_name(), is_pitcher, position,
                   career_start, career_end)

# ---------------------------------------------------------------------------
# 5-B. [TASK-KBO-156/158] 리서치 2단계(사용자 지시 "최근 시즌부터 단계적으로") - 실제 2025시즌
# KBO 개인 타이틀/골든글러브 수상자를 실제 인물로 반영한다. statiz.co.kr/koreabaseball.com/
# 나무위키 실시간 조회(WebFetch/WebSearch, 2026-09-22 기준)로 확인한 사실만 담았다.
# [TASK-KBO-158 갱신] DCL-129 시점에는 타자 8개 부문 중 4개(안타/득점/출루율/장타율)와 투수
# 6개 부문 중 2개(세이브/홀드)가 미확인이었으나, 이번에 나무위키 시상식 표를 다시 조회해 14개
# 부문 전부 확인을 완료했다 - 박영현(KT, 세이브왕)과 노경은(SSG, 홀드왕)을 신규 실제 인물로
# 추가했다. team_token은 "수상 당시" 소속 구단이다 - 이후 트레이드로 소속이 바뀌었더라도 카드
# 자체는 그 시즌 그 구단 소속으로 발급되는 것이 실제 스포츠 카드 관행과 일치한다.
# [TASK-KBO-158, 트레이드/이적 확인] 폰세(토론토 블루제이스)·송성문(샌디에이고 파드리스,
# 4년 222억원)은 2025시즌 종료 후 메이저리그로 진출한 것이 확인되어 2026년 KBO 카드를 받지
# 않는다(아래 ROSTER_2026에도 없음 - 이번엔 "확인 못 함"이 아니라 "확인된 부재"). 김주원은
# 여전히 NC 다이노스 소속임이 확인되어 ROSTER_2026의 NC 내야수 목록에 추가했다(DCL-129 당시
# 조회 누락 - RegisterAll.aspx 요약이 긴 목록에서 일부를 빠뜨렸을 가능성).
REAL_PLAYERS_2025 = [
    # (이름, team_token, team_id, team_enum, is_pitcher, position, z_value 근사치)
    ("디아즈", "SAMSUNG", "TEM_002", "Samsung", False, "1B", 2.6),   # 50홈런/158타점/.644장타율, 타자 3관왕
    ("양의지", "DOOSAN", "TEM_004", "Doosan", False, "C", 2.3),      # 타율왕, 포수 최초 복수 타율왕
    ("박해민", "LG", "TEM_003", "LG", False, "CF", 2.0),             # 도루왕(7년 만에 탈환)
    ("폰세", "HANWHA", "TEM_008", "Hanwha", True, "SP", 2.8),        # 다승/평균자책점(1.89)/탈삼진(252)/승률(.944) 투수 4관왕, MVP, 시즌 후 토론토 블루제이스 이적
    ("신민재", "LG", "TEM_003", "LG", False, "2B", 1.8),             # 골든글러브 2루수
    ("송성문", "KIWOOM", "TEM_010", "Kiwoom", False, "3B", 1.8),     # 골든글러브 3루수, 시즌 후 샌디에이고 파드리스 이적
    ("김주원", "NC", "TEM_009", "NC", False, "SS", 1.8),             # 골든글러브 유격수, 2026년에도 NC 소속 유지 확인
    ("안현민", "KT", "TEM_005", "KT", False, "RF", 1.9),             # 골든글러브 외야수, 신인왕, 출루율왕(.448)
    ("구자욱", "SAMSUNG", "TEM_002", "Samsung", False, "RF", 1.9),   # 골든글러브 외야수, 득점왕(106득점)
    ("레이예스", "LOTTE", "TEM_007", "Lotte", False, "LF", 1.8),     # 골든글러브 외야수(빅터 레예스), 최다안타왕(187안타)
    ("박영현", "KT", "TEM_005", "KT", True, "CP", 2.1),              # [TASK-KBO-158 신규] 세이브왕(35세이브), 2026년에도 KT 마무리로 활동 확인
    ("노경은", "SSG", "TEM_006", "SSG", True, "RP", 1.7),            # [TASK-KBO-158 신규] 홀드왕(35홀드)
]

# 등급 = 그 선수가 실제로 받은 상. TITLE_HOLDER는 세부 부문(홈런왕/타율왕 등)을 카드 스키마가
# 아직 구분하지 않으므로(TASK-KBO-155 범위 밖) "그 해 타이틀 홀더였다"는 사실만 카드 1장으로
# 반영한다. 한 선수가 같은 해에 TITLE_HOLDER와 GOLDEN_GLOVE를 모두 받았으면 카드 2장을 받는다
# (실제 카드 수집 게임처럼 같은 해 다른 상 = 다른 카드 SKU).
# [TASK-KBO-158] 아래로 2025시즌 KBO 개인 타이틀 14개 부문(타자 8 + 투수 6) 전부가 실제 확인된
# 사실로 채워졌다 - 타율(양의지)/홈런·타점·장타율(디아즈)/최다안타(레이예스)/득점(구자욱)/
# 도루(박해민)/출루율(안현민) + 다승·평균자책점·탈삼진·승률(폰세)/세이브(박영현)/홀드(노경은).
REAL_AWARDS_2025 = {
    "디아즈": ["TITLE_HOLDER", "GOLDEN_GLOVE"],
    "양의지": ["TITLE_HOLDER", "GOLDEN_GLOVE"],
    "박해민": ["TITLE_HOLDER"],
    "폰세": ["TITLE_HOLDER", "GOLDEN_GLOVE"],
    "신민재": ["GOLDEN_GLOVE"],
    "송성문": ["GOLDEN_GLOVE"],
    "김주원": ["GOLDEN_GLOVE"],
    "안현민": ["TITLE_HOLDER", "GOLDEN_GLOVE"],
    "구자욱": ["TITLE_HOLDER", "GOLDEN_GLOVE"],
    "레이예스": ["TITLE_HOLDER", "GOLDEN_GLOVE"],
    "박영현": ["TITLE_HOLDER"],
    "노경은": ["TITLE_HOLDER"],
}

real_player_records = {}

# [TASK-KBO-161, 버그 수정] 왕조 로스터(해태/삼성, 명령서 3항)의 실존 인물 29명 전원을 여기서
# 미리 등록해 둔다 - 이전에는 "최형우"만 예외적으로 등록해 그 선수만 후속 골든글러브/타이틀
# 역대 기록과 정확히 병합됐고, 나머지 28명(김상수/차우찬/안지만/오승환 등)은 등록되지 않아
# 실제로는 동일 인물인데도 새로운 중복 인물이 생성되는 결함이 있었다(예: 왕조 로스터의
# "김상수"(1루수 아님, 유격수, 2011~2014)가 실제로는 2014년 도루왕과 동일 인물인데 병합되지
# 않았음 - 이번에 발견해 수정).
for rec in player_records:
    if rec.is_dynasty_member:
        real_player_records[rec.name] = rec

for name, team_token, team_id, team_enum, is_pitcher, position, z_value in REAL_PLAYERS_2025:
    # 최형우처럼 이미 왕조 로스터로 등록된 실존 인물이면 새 player_id를 또 만들지 않고 기존
    # 레코드를 재사용한다(동일 인물은 RealPlayerId/PlayerId가 하나여야 각성 재료 판정이 맞다).
    existing = next((r for r in player_records if r.name == name and r.team_token == team_token), None)
    if existing is not None:
        rec = existing
        rec.skip_random_cards = True  # 왕조 로스터는 원래 이 값이 False였으므로 명시적으로 덮어쓴다.
    else:
        rec = add_player(team_token, team_id, team_enum, name, is_pitcher, position,
                          2015, MAX_YEAR, skip_random_cards=True)
        rec.z_value = z_value
    real_player_records[name] = rec

# [TASK-KBO-155 신설 - 사용자 직접 지시] 최형우는 왕조(2011~2014) 로스터에도 있지만, 2025년
# 골든글러브(지명타자) 수상은 별개의 실제 사실이라 별도로 반영한다.
existing_choi = next((r for r in player_records if r.name == "최형우" and r.team_token == "SAMSUNG"), None)
if existing_choi is not None:
    existing_choi.skip_random_cards = True
    real_player_records["최형우"] = existing_choi
    REAL_AWARDS_2025["최형우"] = ["GOLDEN_GLOVE"]

# ---------------------------------------------------------------------------
# 5-D. [TASK-KBO-157, 사용자 직접 지시] 2026-09-22 기준 KBO 10개 구단 실제 현재 등록 선수 명단
# (koreabaseball.com/Player/RegisterAll.aspx, WebFetch 실시간 조회) - 감독/코치는 제외하고
# 선수만 담았다. 카테고리(투수/포수/내야수/외야수)는 사이트가 그대로 제공한 실제 사실이다.
# [사실 정정/한계] 사이트는 "내야수"/"외야수"/투수 내 세부 포지션(1루~유격, 좌중우, 선발~마무리)을
# 별도로 표시하지 않는다 - 아래 INFIELD_CYCLE/OUTFIELD_CYCLE/PITCHER_CYCLE로 목록 순서에 따라
# 순환 배정했다(이름·소속 구단·투타 대분류까지는 100% 실제 확인 사실, 세부 포지션은 검증되지
# 않은 합리적 추정치임을 완료 보고서에 명시한다). 카드는 이번 조사 시점 기준 "현재 소속"으로
# 발급하며, 2025시즌 수상 당시와 소속이 다른 3명(폰세/송성문/김주원, DCL-128 참고)은 이 로스터에
# 없어 2026년 카드를 추가하지 않는다(트레이드/방출 여부를 추가로 확인하지 못했기 때문 - 다음
# 회차 과제).
ROSTER_2026 = {
    "KT": {
        # [TASK-KBO-158] 박영현(2025 세이브왕, 마무리) 추가 - 2026년에도 KT 마무리로 활동 확인.
        # [TASK-KBO-161] "김민수"는 LG 포수와, "김현수"는 KIA 투수와 동명이인이라 서로 다른
        # 실존 인물임을 구분하기 위해 접미사를 붙였다(둘 다 골든글러브/타이틀 역대 기록에 나오는
        # "그" 김현수/김상수와도 무관한 별개 인물).
        "pitchers": ["고영표", "스기모토", "우규민", "문용익", "배제성", "이정현", "김민수(KT)", "전용주", "대니엘", "주권", "손동현", "로건", "김정운", "장민호", "박영현"],
        "catchers": ["장성우", "조대현", "한승택", "강현우"],
        "infielders": ["허경민", "힐리어드", "오윤석", "권동진", "장준원", "손민석", "김상수", "류현인"],
        "outfielders": ["김현수(KT)", "안현민", "최원준", "이정훈", "장진혁", "김민혁", "유준규", "안치영"],
    },
    "SAMSUNG": {
        "pitchers": ["원태인", "최원태", "이승현", "김태훈", "이승민", "임기영", "양창섭", "이승현", "페덱", "장찬희", "김재윤", "김백산", "사토시", "후라도"],
        "catchers": ["김도환", "장승현", "강민호"],
        "infielders": ["디아즈", "류지혁", "이해승", "이창용", "양우현", "김상준", "심재훈", "전병우", "박계범"],
        "outfielders": ["이성규", "김태훈", "김헌곤", "최형우", "김성윤", "김현준", "구자욱", "박승규"],
    },
    "LG": {
        "pitchers": ["임찬규", "고우석", "우강훈", "이우찬", "손주영", "톨허스트", "이정용", "김강률", "이상영", "이종준", "김진수", "박시원", "카라스코", "양우진", "케네디"],
        "catchers": ["박동원", "김민수", "이주헌"],
        "infielders": ["오지환", "손용준", "오스틴", "추세현", "신민재", "천성호", "문정빈", "구본혁", "이영빈"],
        "outfielders": ["박해민", "함창건", "최원영", "이재원", "송찬의"],
    },
    "KIA": {
        # [TASK-KBO-161] "김현수(KIA)" - KT 투수, 골든글러브 역대 기록의 그 "김현수"(두산->LG)와는 별개 인물.
        "pitchers": ["곽도규", "김태형", "조상우", "김현수(KIA)", "올러", "최지민", "네일", "황동하", "이태양", "이의리", "김범수", "전상현", "양현종", "한재승", "정해영", "시라카와"],
        "catchers": ["주효상", "한준수", "김태군"],
        "infielders": ["정현창", "김규성", "윤도현", "변우혁", "김선빈", "하주석", "황대인", "이호연"],
        "outfielders": ["박정우", "카스트로", "김호령", "한승연", "김민규", "나성범"],
    },
    "DOOSAN": {
        "pitchers": ["박치국", "타카다", "김정우", "김영현", "최승용", "이병헌", "잭로그", "서준오", "이용찬", "벤자민", "이영하", "김택연", "윤태호", "김한중"],
        "catchers": ["김기연", "양의지", "윤준호", "류현준"],
        "infielders": ["이유찬", "강승호", "임종성", "세베리노", "박지훈", "양석환", "오명진", "안재석", "박찬호"],
        "outfielders": ["류승민", "김민석", "손아섭", "정수빈", "김대한", "조수행", "전다민"],
    },
    "NC": {
        "pitchers": ["토다", "라일리", "클레빈저", "이준혁", "신영우", "송명기", "손주환", "이용준", "이재학", "김진호", "전사민", "구창모", "김태경", "배재환", "최우석", "최요한"],
        "catchers": ["안중열", "김형준", "이희성"],
        # [TASK-KBO-158] 김주원(2025 골든글러브 유격수) 추가 - DCL-129 조회 당시 누락됐던 것을
        # 사용자 확인 후 재추가했다(계속 NC 소속).
        "infielders": ["홍종표", "김한별", "최정원", "도태훈", "박민우", "윤준혁", "김휘집", "블레인", "오태양", "신재인", "김주원"],
        "outfielders": ["천재환", "권희동", "박건우", "이우성", "오장한"],
    },
    "SSG": {
        # [TASK-KBO-158] 노경은(2025 홀드왕) 추가 - 2026년 소속 재확인은 못 했으나(다음 회차),
        # 이적/은퇴 등 반대 정보를 찾지 못해 그대로 포함했다.
        "pitchers": ["신상연", "김민", "윤태현", "이건욱", "타케다", "전영준", "최민준", "아빌라", "한두솔", "김건우", "김민준", "문승원", "박시후", "백승건", "이로운", "노경은"],
        "catchers": ["신범수", "이지영"],
        "infielders": ["안상현", "고명준", "박성한", "김요셉", "전의산", "안재연", "홍대인"],
        "outfielders": ["채현우", "에레디아", "김재환", "한유섬", "오태곤", "김정민", "최지훈", "임근우", "오시후"],
    },
    "LOTTE": {
        "pitchers": ["김태균", "현도훈", "박세웅", "구승민", "비슬리", "로드리게스", "김원중", "박정민", "이이무라", "이영재", "박세진", "나균안", "이진하", "이준서", "윤성빈"],
        # [TASK-KBO-161] "박건우(포수)" - NC 외야수(골든글러브 2023 수상자)와 동명이인인 별개 인물.
        "catchers": ["유강남", "손성빈", "박건우(포수)"],
        "infielders": ["전민재", "고승민", "한동희", "이호준", "정대선", "김세민", "나승엽", "노진혁", "박승욱", "한태양"],
        "outfielders": ["황성빈", "조세진", "레이예스", "김동혁", "장두성", "전준우"],
    },
    "HANWHA": {
        "pitchers": ["짐머맨", "이상규", "화이트", "장유호", "황준서", "김종수", "박재규", "김서현", "원종혁", "강재민", "조동욱", "주현상", "박준영", "하동준", "류현진"],
        "catchers": ["최재훈", "장규현", "허인서"],
        # [TASK-KBO-161] "최원준(한화)" - KT 외야수와 동명이인인 별개 인물.
        "infielders": ["정민규", "최원준(한화)", "정은원", "이도윤", "강백호", "박정현", "심우준", "최유빈", "황영묵"],
        "outfielders": ["권광민", "김태연", "페라자", "유로결", "한지윤", "이원석", "최인호"],
    },
    "KIWOOM": {
        "pitchers": ["이강준", "박준현", "김윤하", "조영건", "박지성", "박진형", "안우진", "원종현", "유토", "김선기", "하영민", "전준표", "임진묵", "윤석원"],
        "catchers": ["김재현", "김시앙", "김동헌"],
        "infielders": ["권혁빈", "김웅빈", "서건창", "히우라", "데이비슨", "최재영", "염승원", "안치홍", "어준서", "여동욱"],
        "outfielders": ["임병욱", "추재현", "이형종", "박찬혁", "박주홍"],
    },
}

TEAM_LOOKUP_2026 = {token: (tid, tenum) for tid, tenum, token in TEAMS}
INFIELD_CYCLE = ["1B", "2B", "3B", "SS"]
OUTFIELD_CYCLE = ["LF", "CF", "RF"]
PITCHER_CYCLE = ["SP", "SP", "SP", "SP", "SP", "SP", "RP", "RP", "RP", "RP", "CP"]

_roster_2026_name_seen = {}

def _unique_roster_name(name, team_token):
    # [사실 관계] 실제로 한 구단 안에 동명이인이 존재한다(예: 삼성 투수 "이승현"이 두 명) - 등번호
    # 정보를 CSV 스키마가 담지 않으므로, 두 번째 등장부터 "(2)"를 붙여 서로 다른 실존 인물임을
    # 구분한다(이름 자체를 지어내거나 한쪽을 누락하지 않기 위함).
    key = (name, team_token)
    count = _roster_2026_name_seen.get(key, 0)
    _roster_2026_name_seen[key] = count + 1
    return name if count == 0 else f"{name}({count + 1})"

real_2026_records = []
for team_token, groups in ROSTER_2026.items():
    team_id, team_enum = TEAM_LOOKUP_2026[team_token]

    def _resolve(raw_name, is_pitcher, position):
        existing = real_player_records.get(raw_name)
        if existing is not None and existing.team_token == team_token:
            existing.skip_random_cards = True
            return existing
        name = _unique_roster_name(raw_name, team_token)
        rec = add_player(team_token, team_id, team_enum, name, is_pitcher, position,
                          2018, MAX_YEAR, skip_random_cards=True)
        # [TASK-KBO-161, 버그 수정] 동명이인 접미사가 붙지 않았고(=이 팀 안에서는 유일한 이름),
        # 아직 다른 실존 인물이 그 이름을 선점하지도 않았을 때만 real_player_records에 등록한다.
        # 이렇게 해야 이후 처리되는 골든글러브/개인 타이틀 역대 데이터가 이 선수를 새 인물로
        # 중복 생성하지 않고 정확히 재사용한다(수정 전: 2026 로스터에서 새로 만든 선수는 여기
        # 등록되지 않아, 나중에 같은 이름이 역대 기록에 나오면 완전히 별개의 중복 인물이 생성되는
        # 결함이 있었다). 두 번째 조건(`raw_name not in real_player_records`)은 왕조 로스터 등
        # "먼저 등록된 실존 인물"과 다른 구단 소속의 동명이인이 2026 로스터에 있을 때(예: 왕조
        # 로스터의 김상수와 별개일 수도 있는 2026년 KT 소속 김상수) 먼저 등록된 쪽을 보존하고
        # 이 새 레코드는 등록을 건너뛴다 - 실존 인물 정보가 불확실할 때 "등록하지 않음"(즉 이후
        # 역대 기록과 자동 병합되지 않음)이 "잘못 병합함"보다 안전한 기본값이라고 판단했다.
        if name == raw_name and raw_name not in real_player_records:
            real_player_records[raw_name] = rec
        return rec

    for raw_name in groups["catchers"]:
        real_2026_records.append(_resolve(raw_name, False, "C"))
    for idx, raw_name in enumerate(groups["infielders"]):
        real_2026_records.append(_resolve(raw_name, False, INFIELD_CYCLE[idx % len(INFIELD_CYCLE)]))
    for idx, raw_name in enumerate(groups["outfielders"]):
        real_2026_records.append(_resolve(raw_name, False, OUTFIELD_CYCLE[idx % len(OUTFIELD_CYCLE)]))
    for idx, raw_name in enumerate(groups["pitchers"]):
        real_2026_records.append(_resolve(raw_name, True, PITCHER_CYCLE[idx % len(PITCHER_CYCLE)]))

# ---------------------------------------------------------------------------
# 5-F. [TASK-KBO-159, 사용자 직접 지시 "2024년 이전 시즌으로 확장"] 2013~2024년(10구단 체제
# 확립 이후) KBO 골든글러브 수상자 전체를 실제 인물로 반영한다.
# `koreabaseball.com/Player/Awards/GoldenGlove.aspx` 실시간 조회(2026-09-22)로 확인한 사실 -
# 매년 정확히 10명(투수/포수/1루/2루/3루/유격/외야x3/지명타자)이며 12개 시즌 × 10명 = 120건
# 전부 실명이다. 과거 팀 명칭(넥센 히어로즈->키움, SK 와이번스->SSG)은 동일 프랜차이즈의 연속
# 정체성으로 보고 현재 team_token으로 매핑했다(해태->KIA와 동일한 기존 관례, DCL-126 참고).
# 선수가 연도별로 실제 다른 구단에 있었던 경우(예: 최형우는 삼성<->KIA를 오갔고, 양의지는
# 두산<->NC를 오갔다 - 둘 다 실제 FA/트레이드 이력)에도 인물 자체(RealPlayerId)는 하나로
# 유지하고, 카드만 `make_card_row(..., team_token_override=...)`로 그 해의 실제 소속을 반영한다.
# 포지션 순서 고정: P, C, 1B, 2B, 3B, SS, OF, OF, OF, DH.
GOLDEN_GLOVE_HISTORY = {
    2024: [("하트", "NC"), ("강민호", "SAMSUNG"), ("오스틴", "LG"), ("김혜성", "KIWOOM"), ("김도영", "KIA"),
           ("박찬호", "KIA"), ("구자욱", "SAMSUNG"), ("레이예스", "LOTTE"), ("로하스", "KT"), ("최형우", "KIA")],
    2023: [("페디", "NC"), ("양의지", "DOOSAN"), ("오스틴", "LG"), ("김혜성", "KIWOOM"), ("노시환", "HANWHA"),
           ("오지환", "LG"), ("구자욱", "SAMSUNG"), ("박건우", "NC"), ("홍창기", "LG"), ("손아섭", "NC")],
    2022: [("안우진", "KIWOOM"), ("양의지", "DOOSAN"), ("박병호", "KT"), ("김혜성", "KIWOOM"), ("최정", "SSG"),
           ("오지환", "LG"), ("나성범", "KIA"), ("이정후", "KIWOOM"), ("피렐라", "SAMSUNG"), ("이대호", "LOTTE")],
    2021: [("미란다", "DOOSAN"), ("강민호", "SAMSUNG"), ("강백호", "KT"), ("정은원", "HANWHA"), ("최정", "SSG"),
           ("김혜성", "KIWOOM"), ("구자욱", "SAMSUNG"), ("이정후", "KIWOOM"), ("홍창기", "LG"), ("양의지", "NC")],
    2020: [("알칸타라", "DOOSAN"), ("양의지", "NC"), ("강백호", "KT"), ("박민우", "NC"), ("황재균", "KT"),
           ("김하성", "KIWOOM"), ("김현수", "LG"), ("로하스", "KT"), ("이정후", "KIWOOM"), ("최형우", "KIA")],
    2019: [("린드블럼", "DOOSAN"), ("양의지", "NC"), ("박병호", "KIWOOM"), ("박민우", "NC"), ("최정", "SSG"),
           ("김하성", "KIWOOM"), ("로하스", "KT"), ("샌즈", "KIWOOM"), ("이정후", "KIWOOM"), ("페르난데스", "DOOSAN")],
    2018: [("린드블럼", "DOOSAN"), ("양의지", "DOOSAN"), ("박병호", "KIWOOM"), ("안치홍", "KIA"), ("허경민", "DOOSAN"),
           ("김하성", "KIWOOM"), ("김재환", "DOOSAN"), ("이정후", "KIWOOM"), ("전준우", "LOTTE"), ("이대호", "LOTTE")],
    2017: [("양현종", "KIA"), ("강민호", "SAMSUNG"), ("이대호", "LOTTE"), ("안치홍", "KIA"), ("최정", "SSG"),
           ("김선빈", "KIA"), ("버나디나", "KIA"), ("손아섭", "LOTTE"), ("최형우", "KIA"), ("박용택", "LG")],
    2016: [("니퍼트", "DOOSAN"), ("양의지", "DOOSAN"), ("테임즈", "NC"), ("서건창", "KIWOOM"), ("최정", "SSG"),
           ("김재호", "DOOSAN"), ("김재환", "DOOSAN"), ("김주찬", "KIA"), ("최형우", "KIA"), ("김태균", "HANWHA")],
    2015: [("해커", "NC"), ("양의지", "DOOSAN"), ("테임즈", "NC"), ("나바로", "SAMSUNG"), ("박석민", "NC"),
           ("김재호", "DOOSAN"), ("김현수", "DOOSAN"), ("나성범", "NC"), ("유한준", "KT"), ("이승엽", "SAMSUNG")],
    2014: [("밴헤켄", "KIWOOM"), ("양의지", "DOOSAN"), ("박병호", "KIWOOM"), ("서건창", "KIWOOM"), ("박석민", "SAMSUNG"),
           ("강정호", "KIWOOM"), ("나성범", "NC"), ("손아섭", "LOTTE"), ("최형우", "SAMSUNG"), ("이승엽", "SAMSUNG")],
    2013: [("손승락", "KIWOOM"), ("강민호", "LOTTE"), ("박병호", "KIWOOM"), ("정근우", "HANWHA"), ("최정", "SSG"),
           ("강정호", "KIWOOM"), ("박용택", "LG"), ("손아섭", "LOTTE"), ("최형우", "SAMSUNG"), ("이병규", "LG")],
    # [TASK-KBO-162, 사용자 직접 지시 "2024년 이전 시즌으로 확장"] 1986~2012년(10구단 체제 이전 -
    # KT 창단은 2013년이라 이 구간엔 존재하지 않는다) 골든글러브 27개 시즌을 추가했다. 옛 구단명은
    # 실제 프랜차이즈 승계 관계에 따라 현재 토큰으로 매핑했다(해태→KIA는 기존 관례와 동일) -
    # MBC 청룡→LG(1990년 LG그룹 인수), OB 베어스→DOOSAN(1999년 두산그룹으로 개칭), 빙그레
    # 이글스→HANWHA(1994년 개칭), 태평양 돌핀스/현대 유니콘스→KIWOOM(태평양→현대→우리/서울
    # 히어로즈→넥센→키움으로 이어지는 프랜차이즈 계보), 쌍방울 레이더스→SSG(1999년 해체 후
    # 2000년 SK가 그 자리를 승계 - 일반적으로 통용되는 계보 취급, 다만 OB/MBC만큼 명확한 소유권
    # 승계는 아니라는 점을 밝혀 둔다).
    2012: [("장원삼", "SAMSUNG"), ("강민호", "LOTTE"), ("박병호", "KIWOOM"), ("서건창", "KIWOOM"), ("최정", "SSG"),
           ("강정호", "KIWOOM"), ("박용택", "LG"), ("손아섭", "LOTTE"), ("이용규", "KIA"), ("이승엽", "SAMSUNG")],
    2011: [("윤석민", "KIA"), ("강민호", "LOTTE"), ("이대호", "LOTTE"), ("안치홍", "KIA"), ("최정", "SSG"),
           ("이대수", "HANWHA"), ("손아섭", "LOTTE"), ("이용규", "KIA"), ("최형우", "SAMSUNG"), ("홍성흔", "LOTTE")],
    2010: [("류현진", "HANWHA"), ("조인성", "LG"), ("최준석", "DOOSAN"), ("조성환", "LOTTE"), ("이대호", "LOTTE"),
           ("강정호", "KIWOOM"), ("김강민", "SSG"), ("김현수", "DOOSAN"), ("이종욱", "DOOSAN"), ("홍성흔", "LOTTE")],
    2009: [("로페즈", "KIA"), ("김상훈", "KIA"), ("최희섭", "KIA"), ("정근우", "SSG"), ("김상현", "KIA"),
           ("손시헌", "DOOSAN"), ("김현수", "DOOSAN"), ("박용택", "LG"), ("이택근", "KIWOOM"), ("홍성흔", "LOTTE")],
    2008: [("김광현", "SSG"), ("강민호", "LOTTE"), ("김태균", "HANWHA"), ("조성환", "LOTTE"), ("김동주", "DOOSAN"),
           ("박기혁", "LOTTE"), ("가르시아", "LOTTE"), ("김현수", "DOOSAN"), ("이종욱", "DOOSAN"), ("홍성흔", "LOTTE")],
    2007: [("리오스", "DOOSAN"), ("박경완", "SSG"), ("이대호", "LOTTE"), ("고영민", "DOOSAN"), ("김동주", "DOOSAN"),
           ("박진만", "SAMSUNG"), ("심정수", "SAMSUNG"), ("이대형", "LG"), ("이종욱", "DOOSAN"), ("양준혁", "SAMSUNG")],
    2006: [("류현진", "HANWHA"), ("진갑용", "SAMSUNG"), ("이대호", "LOTTE"), ("정근우", "SSG"), ("이범호", "HANWHA"),
           ("박진만", "SAMSUNG"), ("박한이", "SAMSUNG"), ("이용규", "KIA"), ("이택근", "KIWOOM"), ("양준혁", "SAMSUNG")],
    2005: [("손민한", "LOTTE"), ("진갑용", "SAMSUNG"), ("김태균", "HANWHA"), ("안경현", "DOOSAN"), ("이범호", "HANWHA"),
           ("손시헌", "DOOSAN"), ("데이비스", "HANWHA"), ("서튼", "KIWOOM"), ("이병규", "LG"), ("김재현", "SSG")],
    2004: [("배영수", "SAMSUNG"), ("홍성흔", "DOOSAN"), ("양준혁", "SAMSUNG"), ("박종호", "SAMSUNG"), ("김한수", "SAMSUNG"),
           ("박진만", "SAMSUNG"), ("박한이", "SAMSUNG"), ("브룸바", "KIWOOM"), ("이병규", "LG"), ("이진영", "SSG"), ("김기태", "SSG")],
    2003: [("정민태", "KIWOOM"), ("김동수", "KIWOOM"), ("이승엽", "SAMSUNG"), ("안경현", "DOOSAN"), ("김한수", "SAMSUNG"),
           ("홍세완", "KIA"), ("심정수", "KIWOOM"), ("양준혁", "SAMSUNG"), ("이종범", "KIA"), ("김동주", "DOOSAN")],
    2002: [("송진우", "HANWHA"), ("진갑용", "SAMSUNG"), ("이승엽", "SAMSUNG"), ("김종국", "KIA"), ("김한수", "SAMSUNG"),
           ("브리또", "SAMSUNG"), ("송지만", "HANWHA"), ("심정수", "KIWOOM"), ("이종범", "KIA"), ("마해영", "SAMSUNG")],
    2001: [("신윤호", "LG"), ("홍성흔", "DOOSAN"), ("이승엽", "SAMSUNG"), ("안경현", "DOOSAN"), ("김한수", "SAMSUNG"),
           ("박진만", "KIWOOM"), ("심재학", "DOOSAN"), ("이병규", "LG"), ("정수근", "DOOSAN"), ("양준혁", "LG")],
    2000: [("임선동", "KIWOOM"), ("박경완", "KIWOOM"), ("이승엽", "SAMSUNG"), ("박종호", "KIWOOM"), ("김동주", "DOOSAN"),
           ("박진만", "KIWOOM"), ("박재홍", "KIWOOM"), ("송지만", "HANWHA"), ("이병규", "LG"), ("우즈", "DOOSAN")],
    1999: [("정민태", "KIWOOM"), ("김동수", "SAMSUNG"), ("이승엽", "SAMSUNG"), ("박정태", "LOTTE"), ("김한수", "SAMSUNG"),
           ("류지현", "LG"), ("이병규", "LG"), ("정수근", "DOOSAN"), ("호세", "LOTTE"), ("로마이어", "HANWHA")],
    1998: [("정민태", "KIWOOM"), ("박경완", "KIWOOM"), ("이승엽", "SAMSUNG"), ("박정태", "LOTTE"), ("김한수", "SAMSUNG"),
           ("류지현", "LG"), ("김재현", "LG"), ("박재홍", "KIWOOM"), ("전준호", "KIWOOM"), ("양준혁", "SAMSUNG")],
    1997: [("이대진", "KIA"), ("김동수", "LG"), ("이승엽", "SAMSUNG"), ("최태원", "SSG"), ("홍현우", "KIA"),
           ("이종범", "KIA"), ("박재홍", "KIWOOM"), ("양준혁", "SAMSUNG"), ("이병규", "LG"), ("박재용", "KIA")],
    1996: [("구대성", "HANWHA"), ("박경완", "SSG"), ("김경기", "KIWOOM"), ("박정태", "LOTTE"), ("홍현우", "KIA"),
           ("이종범", "KIA"), ("김응국", "LOTTE"), ("박재홍", "KIWOOM"), ("양준혁", "SAMSUNG"), ("박재용", "KIA")],
    1995: [("이상훈", "LG"), ("김동수", "LG"), ("장종훈", "HANWHA"), ("이명수", "DOOSAN"), ("홍현우", "KIA"),
           ("김민호", "DOOSAN"), ("김광림", "SSG"), ("김상호", "DOOSAN"), ("전준호", "LOTTE"), ("김형석", "DOOSAN")],
    1994: [("정명원", "KIWOOM"), ("김동수", "LG"), ("서용빈", "LG"), ("박종호", "LG"), ("한대화", "LG"),
           ("이종범", "KIA"), ("김재현", "LG"), ("박노준", "SSG"), ("윤덕규", "KIWOOM"), ("김기태", "SSG")],
    1993: [("선동열", "KIA"), ("김동수", "LG"), ("김성래", "SAMSUNG"), ("강기웅", "SAMSUNG"), ("한대화", "LG"),
           ("이종범", "KIA"), ("김광림", "SSG"), ("이순철", "KIA"), ("전준호", "LOTTE"), ("김기태", "SSG")],
    1992: [("염종석", "LOTTE"), ("장채근", "KIA"), ("장종훈", "HANWHA"), ("박정태", "LOTTE"), ("송구홍", "LG"),
           ("박계원", "LOTTE"), ("김응국", "LOTTE"), ("이순철", "KIA"), ("이정훈", "HANWHA"), ("김기태", "SSG")],
    1991: [("선동열", "KIA"), ("장채근", "KIA"), ("김성한", "KIA"), ("박정태", "LOTTE"), ("한대화", "KIA"),
           ("류중일", "SAMSUNG"), ("이순철", "KIA"), ("이정훈", "HANWHA"), ("이호성", "KIA"), ("장종훈", "HANWHA")],
    1990: [("선동열", "KIA"), ("김동수", "LG"), ("김상훈", "LG"), ("강기웅", "SAMSUNG"), ("한대화", "KIA"),
           ("장종훈", "HANWHA"), ("이강돈", "HANWHA"), ("이정훈", "HANWHA"), ("이호성", "KIA"), ("박승호", "SAMSUNG")],
    1989: [("선동열", "KIA"), ("유승안", "HANWHA"), ("김성한", "KIA"), ("강기웅", "SAMSUNG"), ("한대화", "KIA"),
           ("김재박", "LG"), ("고원부", "HANWHA"), ("김일권", "KIWOOM"), ("이강돈", "HANWHA"), ("박철우", "KIA")],
    1988: [("선동열", "KIA"), ("장채근", "KIA"), ("김성한", "KIA"), ("김성래", "SAMSUNG"), ("한대화", "KIA"),
           ("장종훈", "HANWHA"), ("이강돈", "HANWHA"), ("이순철", "KIA"), ("이정훈", "HANWHA"), ("김용철", "LOTTE")],
    1987: [("김시진", "SAMSUNG"), ("이만수", "SAMSUNG"), ("김성한", "KIA"), ("김성래", "SAMSUNG"), ("한대화", "KIA"),
           ("류중일", "SAMSUNG"), ("김종모", "KIA"), ("이광은", "LG"), ("장효조", "SAMSUNG"), ("유승안", "HANWHA")],
    1986: [("선동열", "KIA"), ("이만수", "SAMSUNG"), ("김성한", "KIA"), ("김성래", "SAMSUNG"), ("한대화", "KIA"),
           ("김재박", "LG"), ("김종모", "KIA"), ("이광은", "LG"), ("장효조", "SAMSUNG"), ("김봉연", "KIA")],
}

def _gg_position_for_index(i):
    # [TASK-KBO-162] 2004년처럼 외야수가 4명으로 집계된 해(원본 위키 표의 드문 예외)에도 안전하게
    # 동작하도록, 고정 zip 대신 인덱스 기반으로 포지션을 매긴다 - 10번째를 넘는 인덱스는 전부 DH로
    # 폴백한다(신규 인물 생성 시의 기본값일 뿐이므로 실제 게임 데이터 정확도에 영향 없음).
    base = ["SP", "C", "1B", "2B", "3B", "SS", "LF", "CF", "RF", "DH"]
    return base[i] if i < len(base) else "DH"

def _resolve_historical(name, team_token, is_pitcher, position, rng=random, career=(2010, MAX_YEAR),
                        mark_existing_skip=True):
    # 이름만으로 조회한다(이 표의 선수는 전부 유일하게 식별되는 실존 인물이라 동명이인 위험이
    # 없다) - 연도마다 소속이 달라도 인물 자체(RealPlayerId)는 하나로 유지한다.
    # [TASK-KBO-172] rng/career - TASK-172 신규 인물은 rng172와 실제 활동 연도 근사치를 넘긴다.
    # mark_existing_skip=False - 이미 등록된 인물의 skip_random_cards를 건드리지 않는다. TASK-172 조회가
    # 이 플래그를 켜면 그 인물(예: 왕조 로스터의 서정환/문희수)의 확률 카드가 사라지며 전역 난수 스트림이
    # 밀려 기존 카드 수만 장이 전부 바뀌므로, 기존 DB 보존을 위해 끈다.
    existing = real_player_records.get(name)
    if existing is not None:
        if mark_existing_skip:
            existing.skip_random_cards = True
        return existing
    team_id, team_enum = TEAM_LOOKUP_2026[team_token]
    rec = add_player(team_token, team_id, team_enum, name, is_pitcher, position, career[0], career[1],
                     skip_random_cards=True, rng=rng)
    real_player_records[name] = rec
    return rec

golden_glove_cards_to_issue = []  # (rec, year, team_token) - 7-D 절에서 make_card_row로 발급
for year in sorted(GOLDEN_GLOVE_HISTORY.keys(), reverse=True):  # 최신 연도부터 처리 -> 신규 인물의 기본 소속이 최근 팀이 된다
    for i, (name, team_token) in enumerate(GOLDEN_GLOVE_HISTORY[year]):
        position = _gg_position_for_index(i)
        is_pitcher = position == "SP"
        rec = _resolve_historical(name, team_token, is_pitcher, position)
        golden_glove_cards_to_issue.append((rec, year, team_token))

# ---------------------------------------------------------------------------
# 5-G. [TASK-KBO-161, 사용자 직접 지시 "(1) 진행해봅시다"] 2013~2024년 KBO 개인 타이틀
# 14개 부문(타자 8: 타율/최다안타/홈런/타점/득점/도루/출루율/장타율, 투수 6: 다승/평균자책점/
# 탈삼진/세이브/홀드/승률) 전수를 나무위키 "KBO 리그/역대 타이틀홀더/타자·투수" 실시간 조회
# (2026-09-22)로 확인해 반영한다. 한 해에 여러 부문을 동시 석권한 선수(예: 2013 박병호가
# 홈런·타점·득점·장타율 4개 부문)는 카드 스키마가 부문을 구분하지 않으므로(TASK-155 범위 밖)
# 연도당 1장으로 중복 제거했다. 공동 수상(예: 2017 다승 양현종·노에시)은 두 명 모두 카드를
# 받는다. 이미 GOLDEN_GLOVE_HISTORY/2025시즌/2026로스터/왕조 로스터에 등록된 실존 인물과
# 이름이 겹치면(양의지/최형우/구자욱/손아섭/최정 등 다수) `_resolve_historical()`이 자동으로
# 재사용한다 - 이름 문자열을 그 기존 등록 표기와 정확히 맞췄다(예: "에릭 테임즈"가 아니라
# GOLDEN_GLOVE_HISTORY와 동일한 "테임즈"). 넥센→키움, SK→SSG 매핑은 기존 관례와 동일하다.
TITLE_HOLDER_HISTORY = {
    2013: [("이병규", "LG"), ("손아섭", "LOTTE"), ("박병호", "KIWOOM"), ("김종호", "NC"), ("김태균", "HANWHA"),
           ("배영수", "SAMSUNG"), ("쉬렉", "NC"), ("리즈", "LG"), ("손승락", "KIWOOM"), ("한현희", "KIWOOM"), ("류제국", "LG")],
    2014: [("서건창", "KIWOOM"), ("박병호", "KIWOOM"), ("김상수", "SAMSUNG"), ("김태균", "HANWHA"), ("강정호", "KIWOOM"),
           ("밴헤켄", "KIWOOM"), ("밴덴헐크", "SAMSUNG"), ("손승락", "KIWOOM"), ("한현희", "KIWOOM"), ("소사", "KIWOOM")],
    2015: [("테임즈", "NC"), ("유한준", "KIWOOM"), ("박병호", "KIWOOM"), ("박해민", "SAMSUNG"), ("해커", "NC"),
           ("양현종", "KIA"), ("차우찬", "SAMSUNG"), ("임창용", "SAMSUNG"), ("안지만", "SAMSUNG")],
    2016: [("최형우", "SAMSUNG"), ("테임즈", "NC"), ("최정", "SSG"), ("정근우", "HANWHA"), ("박해민", "SAMSUNG"),
           ("김태균", "HANWHA"), ("니퍼트", "DOOSAN"), ("보우덴", "DOOSAN"), ("김세현", "KIWOOM"), ("이보근", "KIWOOM")],
    2017: [("김선빈", "KIA"), ("손아섭", "LOTTE"), ("최정", "SSG"), ("러프", "SAMSUNG"), ("버나디나", "KIA"),
           ("박해민", "SAMSUNG"), ("최형우", "KIA"), ("양현종", "KIA"), ("노에시", "KIA"), ("피어밴드", "KT"),
           ("메릴 켈리", "SSG"), ("손승락", "LOTTE"), ("진해수", "LG")],
    2018: [("김현수", "LG"), ("전준우", "LOTTE"), ("김재환", "DOOSAN"), ("박해민", "SAMSUNG"), ("박병호", "KIWOOM"),
           ("후랭코프", "DOOSAN"), ("린드블럼", "DOOSAN"), ("샘슨", "HANWHA"), ("정우람", "HANWHA"), ("오현택", "LOTTE")],
    2019: [("양의지", "NC"), ("페르난데스", "DOOSAN"), ("박병호", "KIWOOM"), ("샌즈", "KIWOOM"), ("김하성", "KIWOOM"),
           ("박찬호", "KIA"), ("린드블럼", "DOOSAN"), ("양현종", "KIA"), ("하재훈", "SSG"), ("김상수(투수)", "KIWOOM")],
    2020: [("최형우", "KIA"), ("페르난데스", "DOOSAN"), ("로하스", "KT"), ("심우준", "KT"), ("박석민", "NC"),
           ("알칸타라", "DOOSAN"), ("요키시", "KIWOOM"), ("스트레일리", "LOTTE"), ("조상우", "KIWOOM"), ("주권", "KT")],
    2021: [("이정후", "KIWOOM"), ("전준우", "LOTTE"), ("최정", "SSG"), ("양의지", "NC"), ("구자욱", "SAMSUNG"),
           ("김혜성", "KIWOOM"), ("홍창기", "LG"), ("뷰캐넌", "SAMSUNG"), ("요키시", "KIWOOM"), ("미란다", "DOOSAN"),
           ("오승환", "SAMSUNG"), ("장현식", "KIA"), ("수아레즈", "LG")],
    2022: [("이정후", "KIWOOM"), ("박병호", "KT"), ("피렐라", "SAMSUNG"), ("박찬호", "KIA"), ("케이시 켈리", "LG"),
           ("안우진", "KIWOOM"), ("고우석", "LG"), ("정우영", "LG"), ("엄상백", "KT")],
    2023: [("손아섭", "NC"), ("노시환", "HANWHA"), ("홍창기", "LG"), ("정수빈", "DOOSAN"), ("최정", "SSG"),
           ("페디", "NC"), ("서진용", "SSG"), ("박영현", "KT"), ("쿠에바스", "KT")],
    2024: [("에레디아", "SSG"), ("레이예스", "LOTTE"), ("데이비슨", "NC"), ("오스틴", "LG"), ("김도영", "KIA"),
           ("조수행", "DOOSAN"), ("홍창기", "LG"), ("곽빈", "DOOSAN"), ("원태인", "SAMSUNG"), ("네일", "KIA"),
           ("하트", "NC"), ("정해영", "KIA"), ("노경은", "SSG"), ("박영현", "KT")],
}
# 이번 회차에 처음 등장하는 투수만 골라 둔다(이미 등록된 인물은 _resolve_historical이 기존
# is_pitcher/포지션을 그대로 유지하므로 아래 집합은 "신규 등록 시"에만 참조된다).
TITLE_HOLDER_NEW_PITCHERS = {
    "배영수", "쉬렉", "리즈", "한현희", "류제국", "밴헤켄", "밴덴헐크", "소사", "차우찬", "임창용", "안지만",
    "니퍼트", "보우덴", "김세현", "이보근", "노에시", "피어밴드", "메릴 켈리", "진해수", "후랭코프", "린드블럼",
    "샘슨", "정우람", "오현택", "하재훈", "김상수(투수)", "알칸타라", "요키시", "스트레일리", "조상우", "주권",
    "뷰캐넌", "미란다", "오승환", "장현식", "수아레즈", "케이시 켈리", "안우진", "고우석", "정우영", "엄상백",
    "페디", "서진용", "박영현", "쿠에바스", "네일", "하트", "정해영", "노경은",
}

title_holder_cards_to_issue = []  # (rec, year) - 7-E 절에서 make_card_row로 발급(팀은 rec.team_token 그대로)
for year in sorted(TITLE_HOLDER_HISTORY.keys(), reverse=True):
    seen_this_year = set()  # (name) - 한 해 다관왕을 카드 1장으로 축약
    for name, team_token in TITLE_HOLDER_HISTORY[year]:
        if name in seen_this_year:
            continue
        seen_this_year.add(name)
        is_pitcher = name in TITLE_HOLDER_NEW_PITCHERS
        position = "SP" if is_pitcher else "DH"  # 신규 등록 시에만 쓰이는 기본값(기존 인물은 무시됨)
        rec = _resolve_historical(name, team_token, is_pitcher, position)
        title_holder_cards_to_issue.append((rec, year, team_token))

# ---------------------------------------------------------------------------
# 5-H. [TASK-KBO-163, 사용자 직접 지시 "RETIRED_NUMBER는 전체 반영"] KBO 영구결번 전체를
# 위키백과 "KBO 리그 영구 결번 목록" 실시간 조회(2026-09-22)로 확인해 반영한다 - 18명 전원(1986년
# 김영신부터 2025년 오승환까지). 옛 구단명은 GOLDEN_GLOVE_HISTORY와 동일한 프랜차이즈 승계 매핑.
# [TASK-KBO-174] 카드 연도를 "영구결번 지정 연도"(대부분 은퇴 이후라 실제 활약 시즌이 아님)에서 "그 구단
# 대표 커리어 하이 시즌"으로 바꿨다(1인·구단당 1장). 가능한 한 이 스크립트의 실제 수상 기록(타이틀/골든
# 글러브)으로 교차검증되는 시즌을 골랐고, SIGNATURE와 같은 해는 피했다(박철순·김영신은 대안 시즌이 없어
# 예외). 주석의 "(TH)"/"(GG)"는 교차검증 근거.
# [TASK-KBO-175] 아래 18장의 대표 연도를 정식 확정했다(사용자 확정, DCL-146) - 이후 변경 시 card_id가 바뀌므로
# (`{TEAM}_{연도}_{player_id}_RN`) 세이브 호환을 함께 검토해야 한다.
RETIRED_NUMBER_HISTORY = [
    ("이종범", "KIA", 1993),      # 데뷔 시즌 해태 우승·한국시리즈 MVP, 득점(TH) - SIG는 1994
    ("이병규", "LG", 2005),       # 타율·최다안타(TH) - SIG는 1999
    ("양준혁", "SAMSUNG", 1998),  # 타율·최다안타(TH) - SIG는 1996
    ("이대호", "LOTTE", 2010),    # 타격 7관왕(TH)
    ("최동원", "LOTTE", 1987),    # 탈삼진(TH) - SIG는 1984
    ("선동열", "KIA", 1993),      # 평균자책점 0.78(TH) - SIG/DYN은 1986
    ("박철순", "DOOSAN", 1982),   # 22승 무패 - 대안 시즌 없음(SIG와 같은 해)
    ("송진우", "HANWHA", 2002),   # 골든글러브 투수(GG) - SIG는 2009
    ("이만수", "SAMSUNG", 1987),  # 타점(TH)·골든글러브(GG) - SIG는 1984
    ("정민철", "HANWHA", 1994),   # 평균자책점·탈삼진(TH) - SIG는 1999
    ("박경완", "SSG", 2004),      # 홈런왕(TH, SK) - SIG는 2007
    ("박용택", "LG", 2005),       # 득점·도루(TH) - SIG는 2009
    ("장종훈", "HANWHA", 1991),   # 홈런·타점·득점·장타율(TH), 시즌 MVP - SIG는 1992
    ("이승엽", "SAMSUNG", 1999),  # 54홈런(TH) - SIG는 2003
    ("김용수", "LG", 1989),       # 세이브(TH, MBC) - SIG는 1990
    ("김태균", "HANWHA", 2012),   # 타율·출루율(TH) - SIG는 2008
    ("김영신", "DOOSAN", 1986),   # OB 포수, 1985~1986 활동 - 대안 시즌 없음
    ("오승환", "SAMSUNG", 2012),  # 세이브(TH) - SIG 2006, DYN 2011
]

# 이 중 5명(최동원/박철순/정민철/김용수/김영신)은 이번이 첫 등장이라 실제로 신규 등록되므로
# 투타/포지션을 정확히 확인해 뒀다(WebSearch로 김영신이 포수였음을 재확인 - 통상 영구결번
# 투수로 오인하기 쉬운 이름이라 직접 조사하지 않았다면 잘못 등록할 뻔했다). 나머지는 이미
# GOLDEN_GLOVE_HISTORY 등에 등록돼 있어 아래 값은 무시된다.
RETIRED_NUMBER_NEW_PLAYER_INFO = {
    "최동원": (True, "SP"),
    "박철순": (True, "SP"),
    "정민철": (True, "SP"),
    "김용수": (True, "CP"),
    "김영신": (False, "C"),
}

retired_number_cards_to_issue = []  # (rec, year, team_token) - 7-F 절에서 make_card_row로 발급
for name, team_token, year in RETIRED_NUMBER_HISTORY:
    is_pitcher, position = RETIRED_NUMBER_NEW_PLAYER_INFO.get(name, (False, "DH"))
    rec = _resolve_historical(name, team_token, is_pitcher, position)
    retired_number_cards_to_issue.append((rec, year, team_token))

# ---------------------------------------------------------------------------
# 5-I. [TASK-KBO-164, 사용자 직접 지시] SIGNATURE(시그니처) 등급 - "선수 개인의 커리어
# 하이/상징적 시즌"은 공식 수상 기록표가 없어 순수 리서치만으로는 채울 수 없다. 사용자 지시
# ("Claude가 잘 알려진 명장면을 선정하고, 직접 탐색하여 구단 팬들이 선정한 커리어 하이/상징적
# 시즌을 선정해 제작")에 따라, KBO 40주년 "레전드 40인"(2022년 KBO 공식 팬투표+전문가 투표로
# 선정, WebSearch로 40명 전원 확보)을 모집단으로 삼고, 각 인물의 대표 시즌은 (a) 이미 이
# 스크립트에 등록된 실제 골든글러브/타이틀/영구결번 연도와 최대한 일치시키거나(가장 객관적인
# 교차검증 수단), (b) 그런 교차검증이 불가능한 경우(예: 최동원의 1984년 한국시리즈 4승,
# 백인천의 1982년 타율 0.412, 김재박의 1982년 결승 스퀴즈번트, 박철순의 1982년 22승 무패 -
# 전부 골든글러브 제도가 아직 없었거나 그 선수 개인의 골든글러브 수상 연도가 아닌, 그 자체로
# 이미 KBO 최다 회자 "명장면/전설"인 사실)는 Claude의 KBO 역사 지식으로 직접 선정했다 - 40명
# 전원을 개별 웹 검증하지는 않았음을 명시한다(사용자가 "Claude가 선정"이라고 명시적으로
# 위임한 범위로 판단).
SIGNATURE_HISTORY = [
    ("선동열", "KIA", 1986),      # 평균자책점 0.99, KBO 역대 최고 시즌 중 하나로 회자
    ("최동원", "LOTTE", 1984),    # 한국시리즈 4승, KBO 역대 최고의 포스트시즌 투구로 꼽힘
    ("이종범", "KIA", 1994),      # 타율 .393/196안타/84도루/113득점, "바람의 아들" 전성기
    ("이승엽", "SAMSUNG", 2003),  # 56홈런, 당시 아시아 신기록
    ("박철순", "DOOSAN", 1982),   # 22승 0패, KBO 원년 전설
    ("이만수", "SAMSUNG", 1984),  # 한국시리즈 만루홈런 등 대표 시즌
    ("백인천", "KIWOOM", 1982),   # 타율 0.412, KBO 역대 한 시즌 최고 타율(삼미->청보->태평양->현대->히어로즈 계보)
    ("김성한", "KIA", 1988),      # 해태 왕조 핵심, 원년 KBO 최초 그랜드슬램의 주인공이기도 함
    ("이상훈", "LG", 1994),       # LG 우승 원년 에이스
    ("박정태", "LOTTE", 1992),    # 롯데 우승 한국시리즈 MVP
    ("니퍼트", "DOOSAN", 2016),   # 두산 우승 에이스, 그 해 골든글러브 투수 부문 수상과 일치
    ("배영수", "SAMSUNG", 2004),  # 삼성 에이스 시절 정점, 그 해 골든글러브 투수 부문 수상과 일치
    ("장효조", "SAMSUNG", 1987),  # "타격의 달인", 그 해 골든글러브 외야수 부문 수상과 일치
    ("김시진", "SAMSUNG", 1987),  # 그 해 골든글러브 투수 부문 수상과 일치
    ("한대화", "KIA", 1988),      # 해태 왕조 3루수 핵심, 그 해 골든글러브 3루수 부문 수상과 일치
    ("김재박", "LG", 1982),       # 한국시리즈 결승 스퀴즈번트("개구리 번트"), KBO 최다 회자 명장면
    ("이강철", "KIA", 1991),      # 해태 왕조 선발 에이스 전성기
    ("정민철", "HANWHA", 1999),   # 한화 유일 우승 시즌 에이스, 영구결번 지정 연도와 일치
    ("정민태", "KIWOOM", 2000),   # 현대 왕조 에이스, 그 해 골든글러브 투수 부문 수상과 일치
    ("조계현", "KIA", 1989),      # 해태 왕조 투수진 핵심
    ("김태균", "HANWHA", 2008),   # 그 해 골든글러브 1루수 부문 수상과 일치
    ("박재홍", "KIWOOM", 1996),   # 데뷔 시즌 30-30 클럽 최연소 달성, 그 해 골든글러브 수상과 일치
    ("박경완", "SSG", 2007),      # SK 왕조 안방마님, 그 해 골든글러브 포수 부문 수상과 일치
    ("홍성흔", "LOTTE", 2011),    # 그 해 골든글러브 지명타자 부문 수상과 일치
    ("전준호", "KIWOOM", 1998),   # 그 해 골든글러브 외야수 부문 수상과 일치
    ("이순철", "KIA", 1993),      # 해태 왕조 외야수 핵심, 그 해 골든글러브 외야수 부문 수상과 일치
    ("정근우", "SSG", 2009),      # SK 왕조 2루수, 그 해 골든글러브 2루수 부문 수상과 일치
    ("박진만", "SAMSUNG", 2006),  # 삼성 왕조 유격수, 그 해 골든글러브 유격수 부문 수상과 일치
    ("양준혁", "SAMSUNG", 1996),  # 그 해 골든글러브 외야수 부문 수상과 일치
    ("박용택", "LG", 2009),       # 그 해 골든글러브 외야수 부문 수상과 일치
    ("이병규", "LG", 1999),       # 그 해 골든글러브 외야수 부문 수상과 일치
    ("김기태", "SSG", 2004),      # 그 해 골든글러브 지명타자 부문 수상과 일치
    ("장종훈", "HANWHA", 1992),   # "홈런왕" 전성기, 그 해 골든글러브 1루수 부문 수상과 일치
    ("김동주", "DOOSAN", 2007),   # 그 해 골든글러브 3루수 부문 수상과 일치
    ("심정수", "KIWOOM", 2003),   # 현대 왕조 거포, 그 해 골든글러브 외야수 부문 수상과 일치
    ("우즈", "DOOSAN", 2000),     # 그 해 골든글러브 지명타자 부문 수상과 일치
    ("송진우", "HANWHA", 2009),   # 영구결번 지정 연도와 일치
    ("구대성", "HANWHA", 1996),   # 그 해 골든글러브 투수 부문 수상과 일치
    ("김용수", "LG", 1990),       # LG 창단 첫 우승 마무리 투수
    ("임창용", "SAMSUNG", 2015),  # 그 해 개인 타이틀 세이브왕과 일치
]
SIGNATURE_NEW_PLAYER_INFO = {
    "백인천": (False, "1B"),
}

signature_cards_to_issue = []  # (rec, year, team_token) - 7-G 절에서 make_card_row로 발급
for name, team_token, year in SIGNATURE_HISTORY:
    is_pitcher, position = SIGNATURE_NEW_PLAYER_INFO.get(name, (False, "DH"))
    rec = _resolve_historical(name, team_token, is_pitcher, position)
    signature_cards_to_issue.append((rec, year, team_token))

# ---------------------------------------------------------------------------
# 5-J. [TASK-KBO-165, 사용자 직접 지시 "우선 2014~2025까지"] ALLSTAR(올스타) 등급 - 나무위키
# 연도별 "KBO 올스타전/{연도}년"(2014년만 "한국프로야구 올스타전/2014년") 12개 페이지를
# 실시간 조회(2026-09-22)해 확보했다. 올스타전은 매년 팬투표+선수단투표로 뽑힌 "BEST 12"
# (포지션당 1명: 선발/중간/마무리 투수, 포수, 1~3루, 유격수, 외야 3, 지명타자) x 2개 팀
# (2015년부터 "드림/나눔", 2014년은 "동군/서군")으로 구성된다 - 감독 추천 후보/예비 선수까지
# 포함하면 연도당 30명 안팎으로 늘어나 이번 1차 반영에서는 **가장 공식적이고 명확한 단위인
# BEST 12(팬+선수단 투표 선정)만** 담았다(감독 추천 후보는 다음 회차 확장 대상). 2021년은
# 코로나19로 실제 경기가 취소됐지만 BEST 12 선정 자체는 발표됐으므로 그대로 포함했다.
# 포지션 순서 고정: SP, RP, CP, C, 1B, 2B, 3B, SS, OF, OF, OF, DH.
ALLSTAR_POSITION_SLOTS = ["SP", "RP", "CP", "C", "1B", "2B", "3B", "SS", "LF", "CF", "RF", "DH"]

ALLSTAR_HISTORY = {
    2025: {
        "DREAM": [("원태인", "SAMSUNG"), ("배찬승", "SAMSUNG"), ("김원중", "LOTTE"), ("강민호", "SAMSUNG"),
                  ("디아즈", "SAMSUNG"), ("류지혁", "SAMSUNG"), ("최정", "SSG"), ("전민재", "LOTTE"),
                  ("구자욱", "SAMSUNG"), ("김지찬", "SAMSUNG"), ("레이예스", "LOTTE"), ("전준우", "LOTTE")],
        "NANUM": [("폰세", "HANWHA"), ("박상원", "HANWHA"), ("김서현", "HANWHA"), ("박동원", "LG"),
                  ("채은성", "HANWHA"), ("박민우", "NC"), ("송성문", "KIWOOM"), ("박찬호", "KIA"),
                  ("박건우", "NC"), ("이주형", "KIWOOM"), ("박해민", "LG"), ("문현빈", "HANWHA")],
    },
    2024: {
        "DREAM": [("원태인", "SAMSUNG"), ("김택연", "DOOSAN"), ("오승환", "SAMSUNG"), ("양의지", "DOOSAN"),
                  ("맥키넌", "SAMSUNG"), ("류지혁", "SAMSUNG"), ("최정", "SSG"), ("이재현", "SAMSUNG"),
                  ("정수빈", "DOOSAN"), ("윤동희", "LOTTE"), ("에레디아", "SSG"), ("구자욱", "SAMSUNG")],
        "NANUM": [("류현진", "HANWHA"), ("전상현", "KIA"), ("주현상", "HANWHA"), ("박동원", "LG"),
                  ("이우성", "NC"), ("김혜성", "KIWOOM"), ("김도영", "KIA"), ("박찬호", "KIA"),
                  ("페라자", "HANWHA"), ("나성범", "KIA"), ("도슨", "KIWOOM"), ("최형우", "KIA")],
    },
    2023: {
        "DREAM": [("박세웅", "LOTTE"), ("구승민", "LOTTE"), ("김원중", "LOTTE"), ("양의지", "DOOSAN"),
                  ("박병호", "KT"), ("안치홍", "LOTTE"), ("한동희", "LOTTE"), ("노진혁", "LOTTE"),
                  ("구자욱", "SAMSUNG"), ("피렐라", "SAMSUNG"), ("김민석", "LOTTE"), ("전준우", "LOTTE")],
        "NANUM": [("양현종", "KIA"), ("최지민", "KIA"), ("고우석", "LG"), ("박동원", "LG"),
                  ("채은성", "HANWHA"), ("김혜성", "KIWOOM"), ("노시환", "HANWHA"), ("김주원", "NC"),
                  ("이정후", "KIWOOM"), ("브리토", "KIA"), ("박건우", "NC"), ("최형우", "KIA")],
    },
    2022: {
        "DREAM": [("김광현", "SSG"), ("이승현", "SAMSUNG"), ("오승환", "SAMSUNG"), ("김태군", "SAMSUNG"),
                  ("박병호", "KT"), ("김지찬", "SAMSUNG"), ("최정", "SSG"), ("박성한", "SSG"),
                  ("피렐라", "SAMSUNG"), ("한유섬", "SSG"), ("구자욱", "SAMSUNG"), ("이대호", "LOTTE")],
        "NANUM": [("양현종", "KIA"), ("정우영", "LG"), ("정해영", "KIA"), ("양의지", "NC"),
                  ("황대인", "KIA"), ("김선빈", "KIA"), ("류지혁", "KIA"), ("오지환", "LG"),
                  ("이정후", "KIWOOM"), ("나성범", "KIA"), ("김현수", "LG"), ("최형우", "KIA")],
    },
    2021: {  # 코로나19로 실제 경기는 취소됐으나 BEST 12 선정은 발표됨
        "DREAM": [("원태인", "SAMSUNG"), ("우규민", "SAMSUNG"), ("오승환", "SAMSUNG"), ("강민호", "SAMSUNG"),
                  ("오재일", "SAMSUNG"), ("김상수", "SAMSUNG"), ("이원석", "SAMSUNG"), ("김지찬", "SAMSUNG"),
                  ("구자욱", "SAMSUNG"), ("추신수", "SSG"), ("박해민", "SAMSUNG"), ("피렐라", "SAMSUNG")],
        "NANUM": [("수아레즈", "LG"), ("정우영", "LG"), ("고우석", "LG"), ("양의지", "NC"),
                  ("박병호", "KIWOOM"), ("정은원", "HANWHA"), ("노시환", "HANWHA"), ("오지환", "LG"),
                  ("이정후", "KIWOOM"), ("김현수", "LG"), ("홍창기", "LG"), ("채은성", "LG")],
    },
    2020: {
        "DREAM": [("스트레일리", "LOTTE"), ("구승민", "LOTTE"), ("김원중", "LOTTE"), ("강민호", "SAMSUNG"),
                  ("강백호", "KT"), ("김상수", "SAMSUNG"), ("최정", "SSG"), ("마차도", "LOTTE"),
                  ("로하스", "KT"), ("손아섭", "LOTTE"), ("김재환", "DOOSAN"), ("페르난데스", "DOOSAN")],
        "NANUM": [("구창모", "NC"), ("박준표", "KIA"), ("조상우", "KIWOOM"), ("양의지", "NC"),
                  ("강진성", "NC"), ("김선빈", "KIA"), ("김민성", "LG"), ("김하성", "KIWOOM"),
                  ("이정후", "KIWOOM"), ("김현수", "LG"), ("터커", "KIA"), ("나성범", "NC")],
    },
    2019: {
        "DREAM": [("김광현", "SSG"), ("김택형", "SSG"), ("하재훈", "SSG"), ("강민호", "SAMSUNG"),
                  ("로맥", "SSG"), ("김상수", "SAMSUNG"), ("최정", "SSG"), ("김재호", "DOOSAN"),
                  ("고종욱", "SSG"), ("구자욱", "SAMSUNG"), ("강백호", "KT"), ("페르난데스", "DOOSAN")],
        "NANUM": [("윌슨", "LG"), ("정우영", "LG"), ("고우석", "LG"), ("양의지", "NC"),
                  ("박병호", "KIWOOM"), ("박민우", "NC"), ("김민성", "LG"), ("김하성", "KIWOOM"),
                  ("김현수", "LG"), ("이정후", "KIWOOM"), ("이천웅", "LG"), ("이형종", "LG")],
    },
    2018: {
        "DREAM": [("린드블럼", "DOOSAN"), ("박치국", "DOOSAN"), ("함덕주", "DOOSAN"), ("양의지", "DOOSAN"),
                  ("이대호", "LOTTE"), ("오재원", "DOOSAN"), ("최정", "SSG"), ("김재호", "DOOSAN"),
                  ("손아섭", "LOTTE"), ("박건우", "DOOSAN"), ("김재환", "DOOSAN"), ("최주환", "DOOSAN")],
        "NANUM": [("소사", "LG"), ("서균", "HANWHA"), ("정우람", "HANWHA"), ("유강남", "LG"),
                  ("박병호", "KIWOOM"), ("안치홍", "KIA"), ("송광민", "HANWHA"), ("오지환", "LG"),
                  ("김현수", "LG"), ("호잉", "HANWHA"), ("이형종", "LG"), ("박용택", "LG")],
    },
    2017: {
        "DREAM": [("니퍼트", "DOOSAN"), ("이현승", "DOOSAN"), ("김재윤", "KT"), ("양의지", "DOOSAN"),
                  ("이대호", "LOTTE"), ("최주환", "DOOSAN"), ("최정", "SSG"), ("김재호", "DOOSAN"),
                  ("구자욱", "SAMSUNG"), ("민병헌", "DOOSAN"), ("손아섭", "LOTTE"), ("이승엽", "SAMSUNG")],
        "NANUM": [("양현종", "KIA"), ("김윤동", "KIA"), ("임창민", "NC"), ("김민식", "KIA"),
                  ("로사리오", "HANWHA"), ("안치홍", "KIA"), ("이범호", "KIA"), ("김선빈", "KIA"),
                  ("최형우", "KIA"), ("버나디나", "KIA"), ("이정후", "KIWOOM"), ("김태균", "HANWHA")],
    },
    2016: {
        "DREAM": [("니퍼트", "DOOSAN"), ("정재훈", "DOOSAN"), ("이현승", "DOOSAN"), ("양의지", "DOOSAN"),
                  ("구자욱", "SAMSUNG"), ("오재원", "DOOSAN"), ("허경민", "DOOSAN"), ("김재호", "DOOSAN"),
                  ("민병헌", "DOOSAN"), ("최형우", "SAMSUNG"), ("김문호", "LOTTE"), ("이승엽", "SAMSUNG")],
        "NANUM": [("신재영", "KIWOOM"), ("송창식", "HANWHA"), ("정우람", "HANWHA"), ("박동원", "KIWOOM"),
                  ("테임즈", "NC"), ("정근우", "HANWHA"), ("박석민", "NC"), ("김하성", "KIWOOM"),
                  ("이용규", "HANWHA"), ("나성범", "NC"), ("김주찬", "KIA"), ("로사리오", "HANWHA")],
    },
    2015: {
        "DREAM": [("김광현", "SSG"), ("정우람", "SSG"), ("임창용", "SAMSUNG"), ("강민호", "LOTTE"),
                  ("구자욱", "SAMSUNG"), ("나바로", "SAMSUNG"), ("황재균", "LOTTE"), ("김상수", "SAMSUNG"),
                  ("최형우", "SAMSUNG"), ("김현수", "DOOSAN"), ("민병헌", "DOOSAN"), ("이승엽", "SAMSUNG")],
        "NANUM": [("양현종", "KIA"), ("박정진", "HANWHA"), ("권혁", "HANWHA"), ("김태군", "NC"),
                  ("테임즈", "NC"), ("정근우", "HANWHA"), ("김민성", "KIWOOM"), ("김하성", "KIWOOM"),
                  ("이용규", "HANWHA"), ("김주찬", "KIA"), ("유한준", "KIWOOM"), ("이호준", "NC")],
    },
    2014: {  # 이 해만 팀명이 "동군/서군"이었다(드림/나눔은 2015년부터) - 나무위키 원문에 예비선수 명단이 없어 12명 미만인 자리는 비워 둔다.
        "EASTERN": [("김광현", "SSG"), ("임창용", "SAMSUNG"), ("이재원", "SSG"), ("칸투", "DOOSAN"),
                    ("오재원", "DOOSAN"), ("박석민", "SAMSUNG"), ("김상수", "SAMSUNG"), ("손아섭", "LOTTE"),
                    ("민병헌", "DOOSAN"), ("김현수", "DOOSAN"), ("히메네스", "LOTTE")],
        "WESTERN": [("양현종", "KIA"), ("봉중근", "LG"), ("김태군", "NC"), ("박병호", "KIWOOM"),
                    ("서건창", "KIWOOM"), ("모창민", "NC"), ("강정호", "KIWOOM"), ("나성범", "NC"),
                    ("피에", "HANWHA"), ("이종욱", "NC"), ("나지완", "KIA")],
    },
}

def _allstar_position_for_index(i):
    base = ALLSTAR_POSITION_SLOTS
    return base[i] if i < len(base) else "DH"

allstar_cards_to_issue = []  # (rec, year, team_token) - 7-H 절에서 make_card_row로 발급
for year in sorted(ALLSTAR_HISTORY.keys(), reverse=True):
    for squad_name, entries in ALLSTAR_HISTORY[year].items():
        for i, (name, team_token) in enumerate(entries):
            position = _allstar_position_for_index(i)
            is_pitcher = position in ("SP", "RP", "CP")
            rec = _resolve_historical(name, team_token, is_pitcher, position)
            allstar_cards_to_issue.append((rec, year, team_token))

# ---------------------------------------------------------------------------
# 5-K. [TASK-KBO-172] 시즌 카드 체제 개편 - 이 절에서 새로 등록되는 인물은 전부 rng172를 쓰고 기존
# 등록(섹션 5~5-J) 뒤에 덧붙기만 하므로, 기존 player_id 시퀀스(구자욱 PLY_004038 등)는 그대로다.
# ---------------------------------------------------------------------------

# (1) TITLE_HOLDER 1986~2012 확장(명령서 STEP 3-2 "2013년 이후 제한 해제"). 나무위키 "KBO 리그/역대
# 타이틀홀더/타자·투수" 실시간 조회(2026-10-01)로 확보한 타자 8부문(타율/최다안타/홈런/타점/득점/도루/
# 출루율/장타율) + 투수 6부문(다승/평균자책점/탈삼진/세이브/홀드[2000년 신설]/승률) 수상자다. 옛 구단명은
# GOLDEN_GLOVE_HISTORY와 같은 프랜차이즈 승계 매핑(해태->KIA, MBC->LG, OB->DOOSAN, 빙그레->HANWHA,
# 청보/태평양/현대/넥센->KIWOOM, 쌍방울/SK->SSG)을 따른다. 이름 표기는 기존 등록 표기와 맞췄다
# (타이론 우즈->"우즈", 펠릭스 호세->"호세", 제이 데이비스->"데이비스"). 공동 수상은 모두 발급하고,
# 한 해 다관왕은 기존 2013~2024 규칙과 동일하게 연도당 1장으로 축약한다.
# 형식: (이름, team_token, 부문) - 부문은 신규 등록 시 투타/포지션 판정에만 쓰인다.
_TH_PITCHING = {"다승", "평균자책점", "탈삼진", "세이브", "홀드", "승률"}
TITLE_HOLDER_HISTORY_1986_2012 = {
    1986: [("장효조", "SAMSUNG", "타율"), ("이광은", "LG", "안타"), ("김봉연", "KIA", "홈런"), ("김재박", "LG", "득점"),
           ("서정환", "KIA", "도루"), ("선동열", "KIA", "다승"), ("김용수", "LG", "세이브"), ("최일언", "DOOSAN", "승률")],
    1987: [("장효조", "SAMSUNG", "타율"), ("이정훈", "HANWHA", "안타"), ("김성래", "SAMSUNG", "홈런"), ("이만수", "SAMSUNG", "타점"),
           ("이광은", "LG", "득점"), ("이해창", "KIWOOM", "도루"), ("김시진", "SAMSUNG", "다승"), ("선동열", "KIA", "평균자책점"),
           ("최동원", "LOTTE", "탈삼진"), ("김용수", "LG", "세이브")],
    1988: [("김상훈", "LG", "타율"), ("김성한", "KIA", "홈런"), ("이순철", "KIA", "득점"), ("김성래", "SAMSUNG", "출루율"),
           ("윤학길", "LOTTE", "다승"), ("선동열", "KIA", "평균자책점"), ("이상군", "HANWHA", "세이브"), ("윤석환", "DOOSAN", "승률")],
    1989: [("고원부", "HANWHA", "타율"), ("이강돈", "HANWHA", "안타"), ("김성한", "KIA", "홈런"), ("유승안", "HANWHA", "타점"),
           ("김일권", "KIWOOM", "도루"), ("한대화", "KIA", "출루율"), ("선동열", "KIA", "다승"), ("김용수", "LG", "세이브")],
    1990: [("한대화", "KIA", "타율"), ("이강돈", "HANWHA", "안타"), ("장종훈", "HANWHA", "홈런"), ("김일권", "KIWOOM", "도루"),
           ("선동열", "KIA", "다승"), ("송진우", "HANWHA", "세이브")],
    1991: [("이정훈", "HANWHA", "타율"), ("장종훈", "HANWHA", "홈런"), ("이순철", "KIA", "도루"), ("장효조", "LOTTE", "출루율"),
           ("선동열", "KIA", "다승"), ("조규제", "SSG", "세이브")],
    1992: [("이정훈", "HANWHA", "타율"), ("이순철", "KIA", "안타"), ("장종훈", "HANWHA", "홈런"), ("김기태", "SSG", "출루율"),
           ("송진우", "HANWHA", "다승"), ("염종석", "LOTTE", "평균자책점"), ("이강철", "KIA", "탈삼진"), ("오봉옥", "SAMSUNG", "승률")],
    1993: [("양준혁", "SAMSUNG", "타율"), ("김형석", "DOOSAN", "안타"), ("김성래", "SAMSUNG", "홈런"), ("이종범", "KIA", "득점"),
           ("전준호", "LOTTE", "도루"), ("조계현", "KIA", "다승"), ("선동열", "KIA", "평균자책점"), ("김상엽", "SAMSUNG", "탈삼진"),
           ("정민철", "HANWHA", "승률")],
    1994: [("이종범", "KIA", "타율"), ("김기태", "SSG", "홈런"), ("양준혁", "SAMSUNG", "타점"), ("이상훈", "LG", "다승"),
           ("조계현", "KIA", "다승"), ("정민철", "HANWHA", "평균자책점"), ("정명원", "KIWOOM", "세이브"), ("김홍집", "KIWOOM", "승률")],
    1995: [("김광림", "SSG", "타율"), ("최태원", "SSG", "안타"), ("김상호", "DOOSAN", "홈런"), ("전준호", "LOTTE", "득점"),
           ("장종훈", "HANWHA", "출루율"), ("이상훈", "LG", "다승"), ("조계현", "KIA", "평균자책점"), ("이대진", "KIA", "탈삼진"),
           ("선동열", "KIA", "세이브")],
    1996: [("양준혁", "SAMSUNG", "타율"), ("박재홍", "KIWOOM", "홈런"), ("이종범", "KIA", "득점"), ("홍현우", "KIA", "출루율"),
           ("구대성", "HANWHA", "다승"), ("주형광", "LOTTE", "다승"), ("정명원", "KIWOOM", "세이브")],
    1997: [("김기태", "SSG", "타율"), ("이승엽", "SAMSUNG", "홈런"), ("이종범", "KIA", "득점"), ("김현욱", "SSG", "다승"),
           ("정민철", "HANWHA", "탈삼진"), ("이상훈", "LG", "세이브")],
    1998: [("양준혁", "SAMSUNG", "타율"), ("우즈", "DOOSAN", "홈런"), ("이승엽", "SAMSUNG", "득점"), ("정수근", "DOOSAN", "도루"),
           ("김용수", "LG", "다승"), ("정명원", "KIWOOM", "평균자책점"), ("이대진", "KIA", "탈삼진"), ("임창용", "KIA", "세이브"),
           ("김수경", "KIWOOM", "승률")],
    1999: [("마해영", "LOTTE", "타율"), ("이병규", "LG", "안타"), ("이승엽", "SAMSUNG", "홈런"), ("정수근", "DOOSAN", "도루"),
           ("정민태", "KIWOOM", "다승"), ("임창용", "SAMSUNG", "평균자책점"), ("김수경", "KIWOOM", "탈삼진"), ("문동환", "LOTTE", "승률")],
    2000: [("박종호", "KIWOOM", "타율"), ("이병규", "LG", "안타"), ("장원진", "DOOSAN", "안타"), ("박경완", "KIWOOM", "홈런"),
           ("박재홍", "KIWOOM", "타점"), ("이승엽", "SAMSUNG", "득점"), ("정수근", "DOOSAN", "도루"), ("장성호", "KIA", "출루율"),
           ("송지만", "HANWHA", "장타율"), ("김수경", "KIWOOM", "다승"), ("임선동", "KIWOOM", "다승"), ("정민태", "KIWOOM", "다승"),
           ("구대성", "HANWHA", "평균자책점"), ("진필중", "DOOSAN", "세이브"), ("조웅천", "KIWOOM", "홀드"), ("송진우", "HANWHA", "승률")],
    2001: [("양준혁", "LG", "타율"), ("이병규", "LG", "안타"), ("이승엽", "SAMSUNG", "홈런"), ("우즈", "DOOSAN", "타점"),
           ("정수근", "DOOSAN", "도루"), ("호세", "LOTTE", "출루율"), ("손민한", "LOTTE", "다승"), ("신윤호", "LG", "다승"),
           ("박석진", "LOTTE", "평균자책점"), ("에르난데스", "SSG", "탈삼진"), ("진필중", "DOOSAN", "세이브"), ("차명주", "DOOSAN", "홀드")],
    2002: [("장성호", "KIA", "타율"), ("마해영", "SAMSUNG", "안타"), ("이승엽", "SAMSUNG", "홈런"), ("김종국", "KIA", "도루"),
           ("키퍼", "KIA", "다승"), ("엘비라", "SAMSUNG", "평균자책점"), ("김진우", "KIA", "탈삼진"), ("진필중", "DOOSAN", "세이브"),
           ("차명주", "DOOSAN", "홀드"), ("김현욱", "SAMSUNG", "승률")],
    2003: [("김동주", "DOOSAN", "타율"), ("박한이", "SAMSUNG", "안타"), ("이승엽", "SAMSUNG", "홈런"), ("이종범", "KIA", "도루"),
           ("심정수", "KIWOOM", "출루율"), ("정민태", "KIWOOM", "다승"), ("바워스", "KIWOOM", "평균자책점"), ("이승호", "LG", "탈삼진"),
           ("이상훈", "LG", "세이브"), ("조웅천", "SSG", "세이브"), ("이상열", "KIWOOM", "홀드"), ("차명주", "DOOSAN", "홀드")],
    2004: [("브룸바", "KIWOOM", "타율"), ("홍성흔", "DOOSAN", "안타"), ("박경완", "SSG", "홈런"), ("이호준", "SSG", "타점"),
           ("이종범", "KIA", "득점"), ("전준호", "KIWOOM", "도루"), ("레스", "DOOSAN", "다승"), ("리오스", "KIA", "다승"),
           ("박명환", "DOOSAN", "평균자책점"), ("임창용", "SAMSUNG", "세이브"), ("임경완", "LOTTE", "홀드"), ("배영수", "SAMSUNG", "승률")],
    2005: [("이병규", "LG", "타율"), ("서튼", "KIWOOM", "홈런"), ("박용택", "LG", "득점"), ("데이비스", "HANWHA", "득점"),
           ("김재현", "SSG", "출루율"), ("손민한", "LOTTE", "다승"), ("리오스", "DOOSAN", "탈삼진"), ("배영수", "SAMSUNG", "탈삼진"),
           ("정재훈", "DOOSAN", "세이브"), ("이재우", "DOOSAN", "홀드"), ("오승환", "SAMSUNG", "승률")],
    2006: [("이대호", "LOTTE", "타율"), ("이용규", "KIA", "안타"), ("박한이", "SAMSUNG", "득점"), ("이종욱", "DOOSAN", "도루"),
           ("양준혁", "SAMSUNG", "출루율"), ("류현진", "HANWHA", "다승"), ("오승환", "SAMSUNG", "세이브"), ("권오준", "SAMSUNG", "홀드"),
           ("전준호(투수)", "KIWOOM", "승률")],  # 현대 투수 전준호 - 도루왕 외야수 전준호와 동명이인(김상수(투수) 관례)
    2007: [("이현곤", "KIA", "타율"), ("심정수", "SAMSUNG", "홈런"), ("고영민", "DOOSAN", "득점"), ("이대형", "LG", "도루"),
           ("김동주", "DOOSAN", "출루율"), ("이대호", "LOTTE", "장타율"), ("리오스", "DOOSAN", "다승"), ("류현진", "HANWHA", "탈삼진"),
           ("오승환", "SAMSUNG", "세이브"), ("류택현", "LG", "홀드")],
    2008: [("김현수", "DOOSAN", "타율"), ("김태균", "HANWHA", "홈런"), ("가르시아", "LOTTE", "타점"), ("이종욱", "DOOSAN", "득점"),
           ("이대형", "LG", "도루"), ("김광현", "SSG", "다승"), ("윤석민", "KIA", "평균자책점"), ("오승환", "SAMSUNG", "세이브"),
           ("정우람", "SSG", "홀드"), ("채병용", "SSG", "승률")],
    2009: [("박용택", "LG", "타율"), ("김현수", "DOOSAN", "안타"), ("김상현", "KIA", "홈런"), ("정근우", "SSG", "득점"),
           ("최희섭", "KIA", "득점"), ("이대형", "LG", "도루"), ("페타지니", "LG", "출루율"), ("로페즈", "KIA", "다승"),
           ("윤성환", "SAMSUNG", "다승"), ("조정훈", "LOTTE", "다승"), ("김광현", "SSG", "평균자책점"), ("류현진", "HANWHA", "탈삼진"),
           ("이용찬", "DOOSAN", "세이브"), ("애킨스", "LOTTE", "세이브"), ("권혁", "SAMSUNG", "홀드")],
    2010: [("이대호", "LOTTE", "타율"), ("이대형", "LG", "도루"), ("김광현", "SSG", "다승"), ("류현진", "HANWHA", "평균자책점"),
           ("손승락", "KIWOOM", "세이브"), ("정재훈", "DOOSAN", "홀드"), ("차우찬", "SAMSUNG", "승률")],
    2011: [("이대호", "LOTTE", "타율"), ("최형우", "SAMSUNG", "홈런"), ("전준우", "LOTTE", "득점"), ("오재원", "DOOSAN", "도루"),
           ("윤석민", "KIA", "다승"), ("오승환", "SAMSUNG", "세이브"), ("정우람", "SSG", "홀드")],
    2012: [("김태균", "HANWHA", "타율"), ("손아섭", "LOTTE", "안타"), ("박병호", "KIWOOM", "홈런"), ("이용규", "KIA", "득점"),
           ("장원삼", "SAMSUNG", "다승"), ("나이트", "KIWOOM", "평균자책점"), ("류현진", "HANWHA", "탈삼진"), ("오승환", "SAMSUNG", "세이브"),
           ("박희수", "SSG", "홀드"), ("탈보트", "SAMSUNG", "승률")],
}
# 김상엽은 TASK-KBO-168 강제 주입 인물(PLY_900001, 섹션 7-I)이라 여기서 새 인물로 만들지 않고 발급
# 단계에서 그 고정 레코드에 붙인다(동일 인물 중복 생성 방지).
_TH_FORCED_ALIAS = {"김상엽"}


def _th_new_player_position(category):
    if category == "세이브":
        return True, "CP"
    if category == "홀드":
        return True, "RP"
    if category in _TH_PITCHING:
        return True, "SP"
    return False, "DH"


title_holder_1986_2012_to_issue = []  # (rec 또는 alias 이름, year, team_token)
for year in sorted(TITLE_HOLDER_HISTORY_1986_2012.keys()):
    seen_this_year = set()
    for name, team_token, category in TITLE_HOLDER_HISTORY_1986_2012[year]:
        if name in seen_this_year:
            continue
        seen_this_year.add(name)
        if name in _TH_FORCED_ALIAS:
            title_holder_1986_2012_to_issue.append((name, year, team_token))
            continue
        is_pitcher, position = _th_new_player_position(category)
        rec = _resolve_historical(name, team_token, is_pitcher, position, rng=rng172, mark_existing_skip=False,
                                  career=(max(MIN_YEAR, year - 6), min(MAX_YEAR, year + 8)))
        title_holder_1986_2012_to_issue.append((rec, year, team_token))

# (2) SIGNATURE 선정 기준 정립(명령서 STEP 3-3). 기준: (a) 동일 선수는 구단별로 커리어 하이 최대
# SIGNATURE_MAX_YEARS_PER_PLAYER_TEAM(2)개 연도, (b) 구단 쿼터 - 10개 구단 모두 최소
# SIGNATURE_MIN_PER_TEAM(3)장, 그중 투수/야수 각 1장 이상, (c) 리그 전체 불펜(RP/CP) SIGNATURE
# 최소 SIGNATURE_MIN_BULLPEN(5)장. 레전드 40인 원 목록만으로는 NC/KT가 0장, SSG가 투수 0장이라
# (b)(c)를 채우지 못해, 이 스크립트에 이미 실제 수상 기록(골든글러브/타이틀)으로 교차검증되는 시즌만
# 골라 보강했다 - 각 줄 주석이 그 근거다. 검증은 아래 _validate_signature_quota()가 수행한다.
SIGNATURE_MAX_YEARS_PER_PLAYER_TEAM = 2
SIGNATURE_MIN_PER_TEAM = 3
SIGNATURE_MIN_BULLPEN = 5
# 불펜(RP/CP) 보직으로 SIGNATURE를 받은 시즌 - 등록 포지션(골든글러브 투수 슬롯은 일괄 "SP")으로는
# 보직을 판별할 수 없어 명시한다. (이름, 연도)
SIGNATURE_BULLPEN_SEASONS = {
    ("김용수", 1990), ("임창용", 2015), ("박영현", 2024), ("손승락", 2013), ("정우람", 2018), ("오승환", 2006),
}
SIGNATURE_QUOTA_ADDITIONS = [
    ("테임즈", "NC", 2015),    # 2015 골든글러브 1루수 + 타이틀(NC), KBO 최초 40-40
    ("페디", "NC", 2023),      # 2023 골든글러브 투수 + 타이틀(NC), 투수 트리플크라운
    ("양의지", "NC", 2020),    # 2020 골든글러브 포수(NC), NC 창단 첫 통합우승 주역
    ("로하스", "KT", 2020),    # 2020 골든글러브 외야수 + 타이틀(KT), 시즌 MVP
    ("강백호", "KT", 2021),    # 2021 골든글러브 1루수(KT), KT 창단 첫 통합우승
    ("박영현", "KT", 2024),    # 2024 타이틀(세이브, KT) - 불펜 쿼터
    ("김광현", "SSG", 2008),   # 2008 골든글러브 투수 + 타이틀(다승/탈삼진, SK) - SSG 투수 쿼터
    ("손승락", "KIWOOM", 2013),  # 2013 골든글러브 투수 + 타이틀(세이브, 넥센) - 불펜 쿼터
    ("정우람", "HANWHA", 2018),  # 2018 타이틀(세이브, 한화) - 불펜 쿼터
    ("오승환", "SAMSUNG", 2006),  # 2006 타이틀(세이브 47, 당시 아시아 신기록) - 불펜 쿼터
]
signature_quota_cards_to_issue = []
for name, team_token, year in SIGNATURE_QUOTA_ADDITIONS:
    rec = _resolve_historical(name, team_token, True, "CP", rng=rng172, mark_existing_skip=False)  # 전원 기존 등록 인물(신규 생성 없음)
    signature_quota_cards_to_issue.append((rec, year, team_token))

# (3) DYNASTY 1인 1연도 정예 25장(TASK-172 STEP 3-4, TASK-174 해태 12인 교정). 명단에서 처음 등장하는 인물(심창민/정현욱/
# 권오준은 TH로 먼저 등록)만 신규 등록된다.
dynasty_cards_to_issue = []
for name, team_token, year, is_pitcher, position in DYNASTY_CARDS:
    rec = _resolve_historical(name, team_token, is_pitcher, position, rng=rng172, mark_existing_skip=False,
                              career=(max(MIN_YEAR, year - 6), min(MAX_YEAR, year + 8)))
    dynasty_cards_to_issue.append((rec, year, team_token))

# ---------------------------------------------------------------------------
# 5-L. [TASK-KBO-175, DCL-146] 실존 선수 기본 능력치 2단계 산정 - 1단계(이 절): 성적/위상 기반 기본 OVR.
# 실존 인물의 z_value는 지금까지 add_player()의 무작위 gauss(1.0, 0.6)였다 - 그래서 오승환(기본 OVR 54)·
# 최형우(49)·김태균(54)처럼 커리어 레전드가 +10 종결 카드를 받아도 가상 선수 LIVE 중앙값(65)보다 약한 역전이
# 생겼다(DCL-145 후속). 저장소에 실존 선수의 시즌 원성적(스탯티즈 원자료)은 없고, 이 스크립트가 실시간 조회로
# 확정한 "수상 장부"(골든글러브/개인 타이틀/올스타 BEST 12/영구결번/시그니처/왕조)가 유일한 검증된 성적
# 기준이라 그 장부로 산정한다:
#   기본 OVR = 최고 위상 하한(STATURE_FLOOR) + 누적 수상 가산(나머지 수상 포인트의 제곱근 x 1.0, 반올림)
#              -> REAL_OVR_CAP(95)으로 상한. 2단계(make_card_row)에서 GRADE_OVR_BONUS가 그 위에 가산된다.
#   - 하한 근거: SIG/DYN/RN 88 + 10 = 98 = 가상 선수 LIVE 최댓값과 동률 이상 -> 종결 카드가 어떤 가상 LIVE에도
#     밀리지 않는다. GG 82(가상 LIVE 상위 약 3%), TH 78, AS 73(상위 약 10~15%).
#   - REAL_PLAYERS_2025의 수기 z 근사치(디아즈 2.6 등, 2025 성적을 직접 조사해 넣은 값)가 더 높으면 그 값을 쓴다.
#   - 수상 이력이 없는 실존 인물(2026 로스터 일반 선수 등)은 기존 값을 유지한다(근거 성적이 없어 바꾸지 않음).
# 난수를 전혀 쓰지 않고 rec.z_value만 덮어쓰므로 player_id/card_id/카드 연도·등급 추첨은 1바이트도 바뀌지 않는다
# (FRANCHISE 후보 정렬은 가상 선수 z만 쓰므로 영향 없음). 같은 선수 등급 서열도 2단계 보정이 그대로 보장한다.
# ---------------------------------------------------------------------------
STATURE_FLOOR = {
    "RETIRED_NUMBER": 88, "SIGNATURE": 88, "DYNASTY": 88,
    "GOLDEN_GLOVE": 82, "TITLE_HOLDER": 78, "ALLSTAR": 73,
}
STATURE_POINTS = {
    "RETIRED_NUMBER": 3.0, "SIGNATURE": 3.0, "DYNASTY": 3.0,
    "GOLDEN_GLOVE": 2.0, "TITLE_HOLDER": 1.5, "ALLSTAR": 0.5,
}
STATURE_ACCUMULATION_SCALE = 1.0
REAL_OVR_CAP = 95
KIM_SANGYEOP_ID = "PLY_900001"  # 섹션 7-I 강제 주입 인물 - 레코드가 그 절에서 만들어져 ID로 장부를 연결한다.

# player_id -> {(year, grade)} - 실제로 발급될 수상 카드 전부(섹션 7-B~7-J와 1:1).
award_ledger = {}


def _ledger_add(player_id, year, grade):
    award_ledger.setdefault(player_id, set()).add((int(year), grade))


for _name, _grades in REAL_AWARDS_2025.items():
    for _grade in _grades:
        _ledger_add(real_player_records[_name].player_id, 2025, _grade)
for _rec, _year, _ in golden_glove_cards_to_issue:
    _ledger_add(_rec.player_id, _year, "GOLDEN_GLOVE")
for _rec, _year, _ in title_holder_cards_to_issue:
    _ledger_add(_rec.player_id, _year, "TITLE_HOLDER")
for _rec, _year, _ in retired_number_cards_to_issue:
    _ledger_add(_rec.player_id, _year, "RETIRED_NUMBER")
for _rec, _year, _ in signature_cards_to_issue + signature_quota_cards_to_issue:
    _ledger_add(_rec.player_id, _year, "SIGNATURE")
for _rec, _year, _ in allstar_cards_to_issue:
    _ledger_add(_rec.player_id, _year, "ALLSTAR")
for _rec, _year, _ in dynasty_cards_to_issue:
    _ledger_add(_rec.player_id, _year, "DYNASTY")
for _rec_or_alias, _year, _ in title_holder_1986_2012_to_issue:
    _pid = KIM_SANGYEOP_ID if _rec_or_alias == "김상엽" else _rec_or_alias.player_id
    _ledger_add(_pid, _year, "TITLE_HOLDER")
_ledger_add(KIM_SANGYEOP_ID, 1995, "GOLDEN_GLOVE")                       # 7-I 강제 주입
_ledger_add(real_player_records["구자욱"].player_id, 2026, "SIGNATURE")  # 7-I 강제 주입


def stature_base_overall(player_id):
    """수상 장부 기반 기본 OVR(1단계). 장부가 없으면 None(=기존 값 유지)."""
    seasons = award_ledger.get(player_id)
    if not seasons:
        return None
    top = max(seasons, key=lambda s: (STATURE_FLOOR[s[1]], STATURE_POINTS[s[1]]))
    rest_points = sum(STATURE_POINTS[g] for _, g in seasons) - STATURE_POINTS[top[1]]
    bonus = round(STATURE_ACCUMULATION_SCALE * rest_points ** 0.5)
    return min(REAL_OVR_CAP, STATURE_FLOOR[top[1]] + bonus)


def overall_to_z(ovr):
    """기본 OVR -> players.csv z(3자리). player_base_overall()의 z*15+50 환산이 정확히 ovr로 돌아오게 한다."""
    z = round((ovr - 50) / 15.0, 3)
    assert max(1, min(100, round(float(f"{z:.3f}") * 15 + 50))) == ovr
    return z


_REAL_2025_HAND_Z = {name: z for name, *_, z in REAL_PLAYERS_2025}
stat_recalibration_report = []  # (이름, player_id, 보정 전 기본 OVR, 보정 후 기본 OVR, 수상 시즌 수)
for rec in player_records:
    target = stature_base_overall(rec.player_id)
    if target is None:
        continue
    before = max(1, min(100, round(float(f"{rec.z_value:.3f}") * 15 + 50)))
    hand_z = _REAL_2025_HAND_Z.get(rec.name)
    if hand_z is not None:
        target = max(target, round(hand_z * 15 + 50))
    rec.z_value = overall_to_z(target)
    stat_recalibration_report.append((rec.name, rec.player_id, before, target, len(award_ledger[rec.player_id])))

# ---------------------------------------------------------------------------
# 6. players.csv 행 생성 (16컬럼 - PlayerDatabase.ParsePlayersCsv() 고정 스키마)
# ---------------------------------------------------------------------------
PLAYERS_HEADER = [
    "player_id", "team_id", "name", "year", "position", "pa_ip",
    "z_contact", "z_eye", "z_power", "z_speed", "z_def", "z_stamina",
    "active", "z_stuff", "z_control", "z_movement",
]

players_rows = []
for rec in player_records:
    z = f"{rec.z_value:.3f}"
    zero = "0.000"
    active = "TRUE" if rec.career_start <= MAX_YEAR <= rec.career_end else "FALSE"
    if rec.is_pitcher:
        row = [rec.player_id, rec.team_id, rec.name, rec.career_start, rec.position, rec.pa_ip,
               zero, zero, zero, z, zero, z, active, z, z, z]
    else:
        row = [rec.player_id, rec.team_id, rec.name, rec.career_start, rec.position, rec.pa_ip,
               z, z, z, z, z, z, active, zero, zero, zero]
    players_rows.append(row)

# ---------------------------------------------------------------------------
# 7. cards_{TEAM}.csv 행 생성 (10컬럼 - card_id 뒤에 year 추가)
# ---------------------------------------------------------------------------
CARDS_HEADER = [
    "card_id", "player_id", "grade_id", "grade_name", "base_ovr",
    "salary_cost", "max_enhance", "max_awaken", "is_droppable", "year",
]

MIN_CARDS_PER_PLAYER = 2
MAX_CARDS_PER_PLAYER = 12

# [TASK-KBO-174, 실전 능력치 연동] base_ovr = 그 선수의 기본 OVR(players.csv z값 -> PlayerDatabase.
# ConvertZScoreToStat과 같은 z*15+50 환산) + 등급 보정. 결정론이라 "동일 선수 기준" 등급 서열
# LIVE_NORMAL < LIVE_EPIC < ALLSTAR < FRANCHISE <= TITLE_HOLDER < GOLDEN_GLOVE < SIGNATURE = DYNASTY = RETIRED_NUMBER
# 가 항상 보장된다(예전 등급별 무작위 범위는 범위가 서로 겹쳐 같은 선수의 TH가 GG보다 높게 나올 수 있었다).
# C# CardGrowthRules.GradeOvrBonus()와 반드시 일치해야 한다. 런타임(PlayerDatabase)은 카드 세부 스탯을
# (base_ovr - 선수 기본 OVR)만큼 균등 이동시켜 카드 OVR = base_ovr로 맞춘다.
GRADE_OVR_BONUS = {
    "LIVE_NORMAL": 0, "LIVE_EPIC": 1, "ALLSTAR": 3, "FRANCHISE": 4, "TITLE_HOLDER": 5,
    "GOLDEN_GLOVE": 7, "SIGNATURE": 10, "DYNASTY": 10, "RETIRED_NUMBER": 10,
}
BASE_OVR_MIN, BASE_OVR_MAX = 1, 120  # 상단 클램프가 등급 서열을 깨지 않도록 넉넉하게(최고 선수 약 92 + 10)


def player_base_overall(rec):
    """PlayerDatabase.ConvertZScoreToStat(z) = clamp(round(z*15+50), 1, 100) - players.csv에 쓰인 3자리 z 기준.
    한 선수의 주요 스탯 z가 전부 같은 값이라 기본 OVR = 이 환산값 하나다."""
    z = float(f"{rec.z_value:.3f}")
    return max(1, min(100, round(z * 15 + 50)))


def make_card_row(rec, year, grade, team_token_override=None, rng=random):
    # [TASK-KBO-159] team_token_override - 실제 선수는 이적/FA로 해마다 소속이 달라질 수 있다
    # (예: 최형우 삼성<->KIA, 양의지 두산<->NC) - 그 해의 실제 소속을 카드 ID/파일 배치에
    # 반영하기 위한 인자다. 기본 템플릿(rec.team_token, rec.team_id)은 그대로 두고 카드 한 장
    # 단위로만 다른 구단을 표시할 수 있다.
    team_token = team_token_override or rec.team_token
    meta = GRADE_META[grade]
    ovr_lo, ovr_hi = meta["ovr"]
    card_id = f"{team_token}_{year}_{rec.player_id}_{meta['code']}"
    # [TASK-KBO-174] 예전 base_ovr 난수(등급별 범위 randint)는 값은 버리고 호출만 유지한다 - 이 소비를 빼면
    # 뒤따르는 카드들의 연도/등급 추첨이 전부 밀려 기존 카드 ID가 바뀐다(스트림 보존 전용).
    rng.randint(ovr_lo, ovr_hi)
    base_ovr = max(BASE_OVR_MIN, min(BASE_OVR_MAX, player_base_overall(rec) + GRADE_OVR_BONUS[grade]))
    return [
        card_id, rec.player_id, GRADE_ID[grade], grade,
        base_ovr, meta["salary"], meta["max_enhance"],
        meta["max_awaken"], meta["droppable"], year,
    ]

cards_by_team = {token: [] for _, _, token in TEAMS}
discarded_random_cards = []  # [TASK-KBO-179] 확률 발급분(스트림 보존용으로만 계산, CSV 미출력)

for rec in player_records:
    issued_year_grade = set()

    # [TASK-KBO-172] 예전에는 여기서 왕조 구간 4개 연도 전부 DYNASTY를 발급했다(1인 4장). 이제 DYNASTY는
    # 섹션 7-J의 DYNASTY_CARDS(1인 1연도 25장)만 발급한다. 다만 예전 발급이 소비하던 base_ovr 난수
    # (연도당 randint 1회)는 그대로 소비해 버린다 - 이걸 빼면 뒤따르는 가상 선수 수만 장의 카드
    # 연도/등급/OVR이 통째로 밀려 기존 DB와의 비교 검증이 불가능해지기 때문이다(스트림 보존 전용).
    if rec.is_dynasty_member:
        dyn_start, dyn_end = rec.dynasty_years
        for year in range(dyn_start, dyn_end + 1):
            random.randint(*GRADE_META["DYNASTY"]["ovr"])  # 스트림 보존용 소비(발급하지 않음)
            issued_year_grade.add((year, "DYNASTY"))

    # [TASK-KBO-156] 실제 검증된 선수는 확률 기반 무작위 카드를 받지 않는다 - 왕조 카드(위에서
    # 이미 발급됨)는 그대로 유지하고, 아래 "실제 수상 카드" 절에서 검증된 카드만 별도로 받는다.
    if rec.skip_random_cards:
        continue

    # 왕조 구간 밖(또는 왕조 로스터가 아닌 선수)의 일반 카드 - 확률적으로 여러 장.
    card_count = random.randint(MIN_CARDS_PER_PLAYER, MAX_CARDS_PER_PLAYER)
    span = max(1, rec.career_end - rec.career_start + 1)
    for _ in range(card_count):
        year = random.randint(rec.career_start, rec.career_end)
        if rec.is_dynasty_member:
            dyn_start, dyn_end = rec.dynasty_years
            if dyn_start <= year <= dyn_end:
                continue  # 이미 100% DYNASTY로 확정된 구간과 중복 발급하지 않는다.
        grade = roll_grade()
        key = (year, grade)
        if key in issued_year_grade:
            continue  # 동일 (연도, 등급) 카드 중복 방지(간단한 디듀프, 충돌 시 그냥 건너뜀)
        issued_year_grade.add(key)
        # [TASK-KBO-172] SIGNATURE 선정 기준 "동일 선수 구단별 커리어 하이 최대 2개 연도" - 확률 풀이 3번째
        # 이상 SIGNATURE를 뽑으면 발급하지 않는다(base_ovr 난수는 스트림 보존을 위해 그대로 소비).
        if grade == "SIGNATURE" and sum(1 for (_, g) in issued_year_grade if g == "SIGNATURE") > 2:
            random.randint(*GRADE_META[grade]["ovr"])
            continue
        # [TASK-KBO-179, P0 DB 정화] 확률(roll_grade) 카드는 더 이상 발급하지 않는다. 이 루프의 대상은 성 30 x 이름 40
        # 조합 생성기(make_player_name)로 만든 가상 인물(팀당 400명)과 수상 기록이 없는 왕조 로스터 인물인데, 조합
        # 이름이 실존 2군/육성 선수(예: 2013~2017 롯데 "안준영"(개명 후 안우택))와 우연히 겹쳐 "1군 기록이 없는
        # 선수가 SIGNATURE/GOLDEN_GLOVE/TITLE_HOLDER를 가진" 것처럼 보이는 결함이 있었다(SIG 1,149장 중 약 1,100장이
        # 이 경로). make_card_row()는 그대로 호출해 난수 스트림(=기존 player_id/card_id)을 보존하고 결과만 폐기한다.
        discarded_random_cards.append(make_card_row(rec, year, grade))

# ---------------------------------------------------------------------------
# 7-B. [TASK-KBO-156] 실제 검증된 2025시즌 수상 카드 생성 - 확률(roll_grade)이 아니라 REAL_AWARDS_2025에
# 적어 둔 사실 그대로 100% 확정 발급한다(왕조 카드와 동일한 "확정 발급" 취급).
# ---------------------------------------------------------------------------
REAL_AWARD_YEAR = 2025
real_card_count = 0
for name, grades in REAL_AWARDS_2025.items():
    rec = real_player_records[name]
    for grade in grades:
        cards_by_team[rec.team_token].append(make_card_row(rec, REAL_AWARD_YEAR, grade))
        real_card_count += 1

# ---------------------------------------------------------------------------
# 7-C. [TASK-KBO-157] 2026년 현재 로스터 선수 전원에게 LIVE_NORMAL 카드 1장씩 확정 발급한다
# (새 등급표 정의 "10구단 체제 이후 해당 연도 정규시즌 종료 직후 성적으로 1인당 1카드"를 그대로
# 따른다 - 2026시즌은 아직 진행 중이므로 엄밀히는 "현재까지 성적" 기준). 2025시즌 수상으로 이미
# 카드를 받은 선수(디아즈 등)는 여기서 또 다른 카드(연도가 다름)를 하나 더 받는다 - 같은 인물의
# 서로 다른 시즌 카드이므로 중복이 아니다.
roster_2026_card_count = 0
for rec in real_2026_records:
    cards_by_team[rec.team_token].append(make_card_row(rec, MAX_YEAR, "LIVE_NORMAL"))
    roster_2026_card_count += 1

# ---------------------------------------------------------------------------
# 7-D. [TASK-KBO-159/162] 1986~2024 골든글러브 수상 카드를 100% 확정 발급한다 - 연도별 실제 소속
# 구단으로 발급하므로(`team_token_override`) 같은 선수라도 해에 따라 다른 cards_{TEAM}.csv에
# 카드가 나뉘어 들어갈 수 있다(예: 최형우는 삼성/KIA 양쪽 파일에, 양의지는 두산/NC 양쪽 파일에).
gg_history_card_count = 0
for rec, year, team_token in golden_glove_cards_to_issue:
    cards_by_team[team_token].append(make_card_row(rec, year, "GOLDEN_GLOVE", team_token_override=team_token))
    gg_history_card_count += 1

# ---------------------------------------------------------------------------
# 7-E. [TASK-KBO-161] 2013~2024 개인 타이틀 카드를 100% 확정 발급한다. 골든글러브와 마찬가지로
# `team_token_override`를 반드시 써야 한다 - 예를 들어 손승락은 2013~2014년 키움 소속으로
# 타이틀을 받았지만 2017년엔 롯데 소속으로 또 타이틀을 받았다(실제 FA 이적) - 처리 순서상
# 기본 템플릿의 team_token은 둘 중 하나로 고정되므로, 그 값을 그대로 쓰면 다른 한쪽 연도의
# 카드가 잘못된 구단으로 표시된다.
title_history_card_count = 0
for rec, year, team_token in title_holder_cards_to_issue:
    cards_by_team[team_token].append(make_card_row(rec, year, "TITLE_HOLDER", team_token_override=team_token))
    title_history_card_count += 1

# ---------------------------------------------------------------------------
# 7-F. [TASK-KBO-163] 영구결번 18명 전원에게 RETIRED_NUMBER 카드를 100% 확정 발급한다 - 카드
# 연도는 그 선수의 번호가 실제로 걸린(영구결번 지정) 연도다.
retired_number_card_count = 0
for rec, year, team_token in retired_number_cards_to_issue:
    cards_by_team[team_token].append(make_card_row(rec, year, "RETIRED_NUMBER", team_token_override=team_token))
    retired_number_card_count += 1

# ---------------------------------------------------------------------------
# 7-G. [TASK-KBO-164] KBO 레전드 40인의 시그니처 시즌 카드를 100% 확정 발급한다.
signature_card_count = 0
for rec, year, team_token in signature_cards_to_issue:
    cards_by_team[team_token].append(make_card_row(rec, year, "SIGNATURE", team_token_override=team_token))
    signature_card_count += 1

# ---------------------------------------------------------------------------
# 7-H. [TASK-KBO-165] 2014~2025 올스타 BEST 12 카드를 100% 확정 발급한다.
allstar_card_count = 0
for rec, year, team_token in allstar_cards_to_issue:
    cards_by_team[team_token].append(make_card_row(rec, year, "ALLSTAR", team_token_override=team_token))
    allstar_card_count += 1

# ---------------------------------------------------------------------------
# 7-I. [TASK-KBO-168, 강제 주입 - PM 직접 지시] 김상엽'95(골든글러브)·구자욱'26(시그니처) 확정
# 카드 발급. 김상엽은 기존 GOLDEN_GLOVE_HISTORY/REAL_PLAYERS_2025/ROSTER_2026/왕조 로스터
# 어디에도 없는 신규 실존 인물이라 새 player_id가 필요하지만, 일반 add_player()를 쓰면 그 호출이
# 소비하는 random.gauss/randint 2회가 이 시점 이전(섹션 6/7 시작 전)에 끼어들어 이후 모든 선수의
# 카드 난수 시퀀스가 통째로 밀린다(실제로 한 번 이렇게 했다가 10개 구단 카드 CSV가 전부
# 달라지는 것을 확인하고 되돌렸다) - 그래서 add_player()를 거치지 않고 random을 전혀 쓰지 않는
# 간단한 객체를 만들어 players_rows/cards_by_team에 직접 append한다(이 시점은 이미 모든
# make_card_row() 호출 - base_ovr 추첨 포함 - 뒤라 여기서 추가로 소비하는 random은 이후 아무
# 코드에도 영향을 주지 않는다). player_id는 정상 시퀀스(next_player_id())가 절대 도달하지 않는
# 고정값(PLY_900001)을 써서 충돌을 피했다.
class _ForcedPlayer:
    pass

kim_sangyeop = _ForcedPlayer()
kim_sangyeop.player_id = KIM_SANGYEOP_ID
kim_sangyeop.team_token = "SAMSUNG"
kim_sangyeop.name = "김상엽"
# [TASK-KBO-175] 기존 수기 1.7(OVR 76) 대신 다른 실존 인물과 같은 수상 장부 기반 산정(섹션 5-L)을 쓴다.
_kim_before = round(1.7 * 15 + 50)
kim_sangyeop.z_value = overall_to_z(stature_base_overall(KIM_SANGYEOP_ID))
stat_recalibration_report.append(("김상엽", KIM_SANGYEOP_ID, _kim_before, player_base_overall(kim_sangyeop),
                                  len(award_ledger[KIM_SANGYEOP_ID])))
_kim_z = f"{kim_sangyeop.z_value:.3f}"
players_rows.append([
    kim_sangyeop.player_id, "TEM_002", "김상엽", 1990, "SP", 170,
    "0.000", "0.000", "0.000", _kim_z, "0.000", _kim_z, "FALSE",
    _kim_z, _kim_z, _kim_z,
])

forced_card_count = 0
cards_by_team["SAMSUNG"].append(make_card_row(kim_sangyeop, 1995, "GOLDEN_GLOVE"))
forced_card_count += 1
cards_by_team["SAMSUNG"].append(make_card_row(real_player_records["구자욱"], 2026, "SIGNATURE"))
forced_card_count += 1

# ---------------------------------------------------------------------------
# 7-J. [TASK-KBO-172] 시즌 카드 체제 개편분 발급 - 모든 난수는 rng172(기존 스트림 비간섭).
# ---------------------------------------------------------------------------
_legacy_card_id_counts = Counter(row[0] for rows in cards_by_team.values() for row in rows)

# (1) TITLE_HOLDER 1986~2012
title_1986_2012_card_count = 0
for rec_or_alias, year, team_token in title_holder_1986_2012_to_issue:
    rec = kim_sangyeop if rec_or_alias == "김상엽" else rec_or_alias
    cards_by_team[team_token].append(make_card_row(rec, year, "TITLE_HOLDER", team_token_override=team_token, rng=rng172))
    title_1986_2012_card_count += 1

# (2) SIGNATURE 쿼터 보강
for rec, year, team_token in signature_quota_cards_to_issue:
    cards_by_team[team_token].append(make_card_row(rec, year, "SIGNATURE", team_token_override=team_token, rng=rng172))

# (3) DYNASTY 1인 1연도 25장
dynasty_card_count = 0
for rec, year, team_token in dynasty_cards_to_issue:
    cards_by_team[team_token].append(make_card_row(rec, year, "DYNASTY", team_token_override=team_token, rng=rng172))
    dynasty_card_count += 1

# (4) FRANCHISE(프랜차이즈) - [TASK-KBO-179 재정의] 예전(TASK-172)에는 구단-연도마다 "가상 로스터" 선수 5명
# (RP2/CP1/C1/SS·2B1)에게 발급해 1,670장 전부가 1군 기록이 없는 조합형 인물이었다. 이제 "검증된 1군 실존 인물"
# 안에서만 발급한다:
#   - 후보: 같은 구단에서 검증 수상 시즌(ALLSTAR BEST12/TH/GG/RN/SIG/DYN)이 FRANCHISE_MIN_VERIFIED_SEASONS(2)개
#     이상인 실존 인물(= 그 구단의 검증된 1군 핵심 주전).
#   - 연도: 그 구단에서의 검증 시즌 중 "올스타(BEST12)만 받고 TH/GG/RN/SIG/DYN은 없는" 가장 이른 시즌 - 팬/선수단
#     투표로 1군 주전임이 검증됐지만 개인 타이틀은 없던 시즌이라 "무관이지만 구단 핵심 주전" 정의와 일치한다.
#   - 동일 (선수, 구단)당 1장.
FRANCHISE_MIN_VERIFIED_SEASONS = 2
_AWARD_GRADES = {"ALLSTAR", "TITLE_HOLDER", "RETIRED_NUMBER", "SIGNATURE", "GOLDEN_GLOVE", "DYNASTY"}
_TOP_AWARD_GRADES = _AWARD_GRADES - {"ALLSTAR"}
_real_ids = {r.player_id for r in real_player_records.values()}
_rec_by_id = {r.player_id: r for r in player_records}
_verified_seasons = {}  # (player_id, team) -> {year: {grade}}
for _team, _rows in cards_by_team.items():
    for _row in _rows:
        if _row[3] in _AWARD_GRADES and _row[1] in _rec_by_id:
            _verified_seasons.setdefault((_row[1], _team), {}).setdefault(int(_row[9]), set()).add(_row[3])

franchise_card_count = 0
for (_pid, _team), _seasons in sorted(_verified_seasons.items()):
    if len(_seasons) < FRANCHISE_MIN_VERIFIED_SEASONS:
        continue
    _as_only_years = sorted(y for y, gs in _seasons.items() if not (gs & _TOP_AWARD_GRADES))
    if not _as_only_years:
        continue
    cards_by_team[_team].append(make_card_row(_rec_by_id[_pid], _as_only_years[0], "FRANCHISE",
                                              team_token_override=_team, rng=rng172))
    franchise_card_count += 1

# (4-B) [TASK-KBO-179] LIVE_EPIC(라이브 에픽) - 확률 풀 폐지로 비게 된 등급을 검증 풀에서 채운다. 2026 현역 1군
# 등록 선수(ROSTER_2026) 중 검증 수상 이력(수상 장부)이 하나라도 있는 선수 = "검증된 현역 1군 주전"에게 2026
# LIVE_EPIC 1장을 추가 발급한다(기존 2026 LIVE_NORMAL 카드 ID는 그대로 보존 - 같은 시즌 다른 SKU).
live_epic_card_count = 0
_epic_seen = set()
for rec in real_2026_records:
    if rec.player_id in _epic_seen or not award_ledger.get(rec.player_id):
        continue
    _epic_seen.add(rec.player_id)
    cards_by_team[rec.team_token].append(make_card_row(rec, MAX_YEAR, "LIVE_EPIC", rng=rng172))
    live_epic_card_count += 1

# (5) 발급 결과 무결성 검증 - 카드 ID 중복, SIGNATURE 선정 기준, DYNASTY 1인 1장.
_card_id_counts = Counter(row[0] for rows in cards_by_team.values() for row in rows)
_dupe_ids = {cid for cid, n in _card_id_counts.items() if n > _legacy_card_id_counts.get(cid, 1)}
_legacy_dupe_ids = sorted(cid for cid, n in _legacy_card_id_counts.items() if n > 1)
if _legacy_dupe_ids:
    # TASK-172 이전부터 있던 중복(2026 로스터 동명이인 처리 한계 - 김태훈/이승현). 이번 범위 밖이라 경고만 한다.
    print(f"[경고] TASK-172 이전부터 존재하던 중복 card_id {len(_legacy_dupe_ids)}건: {_legacy_dupe_ids}")
if _dupe_ids:
    raise ValueError(f"[TASK-KBO-172] 중복 card_id 발생: {sorted(_dupe_ids)[:10]}")

_player_is_pitcher = {r.player_id: r.is_pitcher for r in player_records}
_player_is_pitcher[kim_sangyeop.player_id] = True
_sig_rows = [(team, row) for team, rows in cards_by_team.items() for row in rows if row[3] == "SIGNATURE"]
_sig_per_player_team = {}
for team, row in _sig_rows:
    _sig_per_player_team[(row[1], team)] = _sig_per_player_team.get((row[1], team), 0) + 1
_sig_violations = [k for k, v in _sig_per_player_team.items() if v > SIGNATURE_MAX_YEARS_PER_PLAYER_TEAM]
# 쿼터(b)(c)는 실존 인물 큐레이션 SIGNATURE에만 적용한다 - 확률 풀이 가상 선수에게 주는 SIGNATURE는
# 쿼터 계산에서 제외(가상 선수 물량이 쿼터를 "자동 충족"시켜 검증이 무의미해지는 것을 막는다).
_real_ids_with_forced = _real_ids | {kim_sangyeop.player_id}
_curated_sig_rows = [(team, row) for team, row in _sig_rows if row[1] in _real_ids_with_forced]
signature_quota_report = {}
for _, _, team_token in TEAMS:
    team_rows = [row for team, row in _curated_sig_rows if team == team_token]
    pitchers = sum(1 for row in team_rows if _player_is_pitcher.get(row[1]))
    signature_quota_report[team_token] = (len(team_rows), pitchers, len(team_rows) - pitchers)
    if len(team_rows) < SIGNATURE_MIN_PER_TEAM or pitchers < 1 or len(team_rows) - pitchers < 1:
        _sig_violations.append(("TEAM_QUOTA", team_token, signature_quota_report[team_token]))
_player_name = {r.player_id: r.name for r in player_records}
signature_bullpen_count = sum(1 for _, row in _curated_sig_rows
                              if (_player_name.get(row[1]), int(row[9])) in SIGNATURE_BULLPEN_SEASONS)
if signature_bullpen_count < SIGNATURE_MIN_BULLPEN:
    _sig_violations.append(("BULLPEN_QUOTA", signature_bullpen_count))
if _sig_violations:
    raise ValueError(f"[TASK-KBO-172] SIGNATURE 선정 기준 위반: {_sig_violations}")

_dyn_players = [row[1] for rows in cards_by_team.values() for row in rows if row[3] == "DYNASTY"]
if len(_dyn_players) != len(DYNASTY_CARDS) or len(set(_dyn_players)) != len(_dyn_players):
    raise ValueError(f"[TASK-KBO-172] DYNASTY 1인 1연도 규칙 위반: {len(_dyn_players)}장 / 고유 {len(set(_dyn_players))}명")

# ---------------------------------------------------------------------------
# 8. cheerleaders.csv 행 생성 (CheerleaderCatalog.cs 파서 스키마 7컬럼 + Team/ActivePeriod 2컬럼 = 총 9컬럼)
# ---------------------------------------------------------------------------
# [TASK-KBO-175, DCL-146] 치어리더 "단일 대표 연도" 폐지 -> "소속 구단 + 활동 기간" 체제. 치어리더 카드는 선수
# 시즌 카드처럼 연도('24)로 쪼개지 않는다. 한 사람이 여러 구단을 거쳤으면 구단 이력마다 카드가 따로 있고, 각 카드는
# 그 활동 기간에 소속했던 구단(Team)과만 세트덱 시너지가 발동한다(예: 이아영 2020~2021 = KIA, 2022~2023 = NC).
# 스키마: 9번째 컬럼 Year(TASK-171~174, C# 미사용) -> ActivePeriod로 교체. 0~6번 컬럼 위치/의미는 그대로라
# 구버전 파서도 그대로 읽는다. C# CheerleaderCatalog가 Team/ActivePeriod를 읽어 Cheerleader 모델에 싣는다.
# ActivePeriod 표기: "시작~끝"(종료), "시작~"(현역 진행 중), "연도"(단일 시즌), 복수 구간은 "/"로 연결.
CHEERLEADERS_HEADER = [
    "CatalogId", "Name", "Grade", "ConditionBuff", "EconomicBonusRate",
    "ClutchMultiplier", "SentimentDefense", "Team", "ActivePeriod",
]

# docs/16_shop_and_gacha_policy.md 2절에 PM이 이미 확정한 등급별 수치표를 그대로 가져다 썼다
# (스크립트가 임의로 지어낸 값이 아니다). SEASON_LIMITED(TASK-KBO-168)은 당시 문서에는 없었으나
# Cheerleader.cs의 CheerleaderGrade enum에는 이미 6번 값으로 존재했다 - 같은 문서 2절 표를
# 그대로 가져다 썼다(+5/1.20/1.25/4). 현재는 LIVE_NORMAL/ICON/LEGEND만 실제로 쓰이지만,
# 다른 등급이 언젠가 다시 필요해질 수 있어 표 전체를 그대로 유지한다.
CHEER_GRADE_META = {
    "LIVE_NORMAL":    {"buff": 1, "clutch": 1.00, "economic": 1.05, "sentiment": 0},
    "LIVE_EPIC":      {"buff": 2, "clutch": 1.05, "economic": 1.10, "sentiment": 1},
    "ICON":           {"buff": 3, "clutch": 1.10, "economic": 1.15, "sentiment": 2},
    "LEGEND":         {"buff": 4, "clutch": 1.15, "economic": 1.20, "sentiment": 3},
    "SEASON_LIMITED": {"buff": 5, "clutch": 1.20, "economic": 1.25, "sentiment": 4},
}

# 1) CHEER_LIVE_2026: 2026년 현재 10개 구단 소속 현역 치어리더 124명(구단 -> 이름, 기획자 확정 원본 그대로 -
#    TASK-KBO-171 v4.0). 전원 활동 기간 "2026~", LIVE(LIVE_NORMAL) 1장씩 확정 생성(확률 분기 없음).
CHEER_LIVE_PERIOD = "2026~"
CHEER_LIVE_2026 = {
    "LG": ["차영현", "고예지", "김태희", "박예은", "서여진", "신서윤", "양효주", "우혜준", "임혜진", "장로나", "진수화", "이서우"],
    "HANWHA": ["하지원", "김연정", "감서윤", "김보미", "김이현", "우수한", "유진경", "이호은", "전은비", "지아영", "최석화", "최홍라"],
    "SSG": ["배수현", "안지현", "이수진", "김도아", "김현영", "유보영", "이연진", "이정윤", "이지원", "임은비", "정설아", "조다정", "허수미"],
    "SAMSUNG": ["천소윤", "남화륜", "문가은", "박소영", "박지영", "박혜인", "신비", "오서율", "유세빈", "이규리", "장유빈", "최소윤", "최희원", "한지은"],
    "NC": ["이주희", "강지유", "김나연", "김수현", "김시엘", "노가현", "배한비", "송민주", "안수연", "원민주", "윤가영", "황별님"],
    "KT": ["신세희", "권가영", "계유진", "김가현", "김민지", "김진아", "김한슬", "김해리", "이서윤", "이예빈", "정희정"],
    "LOTTE": ["목나경", "박담비", "김가현", "김나현", "공서윤", "박수연", "박예빈", "이윤서", "설유진", "심영원", "이정원", "이조은"],
    "KIA": ["유세리", "박성은", "신혜령", "고가빈", "조다빈", "이예은", "오의주", "문채원", "이은혜", "김민서", "최지원", "임채빈", "강민지", "김예원", "윤라경"],
    "DOOSAN": ["서현숙", "박기량", "정다혜", "안혜지", "문혜진", "류현주", "주예지", "김주선", "백지혜", "김인영", "나지원", "조나현", "정아련", "황래경"],
    "KIWOOM": ["용경아", "송민교", "강수경", "정차연", "홍예빈", "차예나", "서예은", "최혜린", "이채원"],
}

# 2) CHEER_ICON_LEGEND: 1~4세대 구단 이력 46건 = ICON 46장 + LEGEND 15장(TASK-KBO-175 사용자 확정본 그대로).
#    형식: (이름, 소속 구단, 활동 기간, [티어]). LEGEND 기준: 당대 레전드급 인기 + 은퇴 또는 타 구단 이적으로
#    2026년 현재 그 구단에 남아 있지 않은 경우만 - 아래 _validate_cheer_legend()가 기계적으로 검증한다
#    (서현숙/이주희/하지원은 2026 현역이라 LEGEND에서 빠졌고, 남궁혜미/이연주/김한나(KIA)/김이서가 추가됐다).
CHEER_ICON_LEGEND = [
    # 1세대 (4건)
    ("배수현", "SSG", "2003~", ["ICON"]),
    ("노숙희", "SAMSUNG", "2000~2012", ["ICON", "LEGEND"]),
    ("이미경", "NC", "2012~2014", ["ICON", "LEGEND"]),
    ("강보경", "HANWHA", "2009~2013", ["ICON", "LEGEND"]),
    # 2세대 (14건)
    ("박기량", "LOTTE", "2009~2022", ["ICON", "LEGEND"]),
    ("박기량", "DOOSAN", "2024~", ["ICON"]),
    ("김연정", "NC", "2013~2016", ["ICON", "LEGEND"]),
    ("김연정", "HANWHA", "2009~2011/2017~", ["ICON"]),
    ("남궁혜미", "LG", "2012~2020", ["ICON", "LEGEND"]),
    ("금보아", "HANWHA", "2011~2015", ["ICON"]),
    ("이엄지", "KIWOOM", "2019~2022", ["ICON"]),
    ("이연주", "SAMSUNG", "2012~2018", ["ICON", "LEGEND"]),
    ("이수진", "SAMSUNG", "2013~2024", ["ICON", "LEGEND"]),
    ("이수진", "SSG", "2025~", ["ICON"]),
    ("김한나", "KIWOOM", "2017~2019", ["ICON"]),
    ("김한나", "KIA", "2020~2025", ["ICON", "LEGEND"]),
    ("김진아", "LOTTE", "2014~2015", ["ICON"]),
    ("김진아", "KT", "2017~", ["ICON"]),
    # 3세대 (19건)
    ("김한슬", "KT", "2015~", ["ICON"]),
    ("서현숙", "DOOSAN", "2016~", ["ICON"]),             # 2026 두산 현역 -> LEGEND 제외
    ("이아영", "KIA", "2020~2021", ["ICON", "LEGEND"]),
    ("이아영", "NC", "2022~2023", ["ICON"]),
    ("이나경", "DOOSAN", "2017~2023", ["ICON", "LEGEND"]),
    ("안지현", "KIWOOM", "2017~2018", ["ICON", "LEGEND"]),
    ("안지현", "LOTTE", "2019~2022", ["ICON"]),
    ("안지현", "SSG", "2025~", ["ICON"]),
    ("이주희", "NC", "2018~2021/2025~", ["ICON"]),        # 2026 NC 현역 -> LEGEND 제외
    ("김이서", "LG", "2023~2024", ["ICON", "LEGEND"]),
    ("이하윤", "HANWHA", "2017~2020", ["ICON"]),
    ("하지원", "HANWHA", "2023~", ["ICON"]),              # 2026 한화 현역 -> LEGEND 제외
    ("하지원", "LG", "2018~2021", ["ICON"]),
    ("고정현", "SAMSUNG", "2019~2023", ["ICON"]),
    ("이다혜", "KIA", "2019~2022", ["ICON", "LEGEND"]),
    ("차영현", "LG", "2018~", ["ICON"]),
    ("신세희", "KT", "2020~", ["ICON"]),
    ("목나경", "LOTTE", "2024~", ["ICON"]),
    ("유세리", "KIA", "2024~", ["ICON"]),
    # 4세대 (9건)
    ("박소영", "SAMSUNG", "2025~", ["ICON"]),
    ("우수한", "HANWHA", "2023~", ["ICON"]),
    ("정희정", "DOOSAN", "2020~2024", ["ICON"]),
    ("정희정", "KT", "2025~", ["ICON"]),
    ("이연진", "SSG", "2024~", ["ICON"]),
    ("이주은", "KIA", "2024", ["ICON", "LEGEND"]),
    ("유세빈", "SAMSUNG", "2026~", ["ICON"]),
    ("천소윤", "SAMSUNG", "2026~", ["ICON"]),
    ("용경아", "KIWOOM", "2025~", ["ICON"]),
]
CHEER_EXPECTED_COUNTS = {"LIVE_NORMAL": 124, "ICON": 46, "LEGEND": 15}


def cheer_period_token(active_period):
    """CatalogId용 활동 기간 토큰. "~"는 "-", 열린 끝은 "NOW", 복수 구간 구분자 "/"는 "+"로 바꾼다
    (예: "2009~2011/2017~" -> "2009-2011+2017-NOW", "2024" -> "2024"). C# CheerleaderActivePeriod.ToIdToken()과
    반드시 같은 규칙이어야 한다."""
    segments = []
    for segment in active_period.split("/"):
        start, sep, end = segment.partition("~")
        segments.append(f"{start}-{end or 'NOW'}" if sep else start)
    return "+".join(segments)


def _validate_cheer_legend():
    """LEGEND 기준(은퇴/이적으로 2026년 현재 그 구단에 없음)을 기계적으로 검증한다 - 진행 중 활동 기간("~"로
    끝남)이거나 2026 현역 명단에 같은 구단으로 있으면 위반."""
    violations = []
    for name, team, period, tiers in CHEER_ICON_LEGEND:
        if "LEGEND" not in tiers:
            continue
        if period.endswith("~") or name in CHEER_LIVE_2026.get(team, []):
            violations.append((name, team, period))
    if violations:
        raise ValueError(f"[TASK-KBO-175] LEGEND 선정 기준 위반(2026 현재 해당 구단 소속): {violations}")


_validate_cheer_legend()

cheerleaders_rows = []
_seen_cheer_ids = set()


def _add_cheer_row(name, team, active_period, tier_token, grade):
    """CatalogId = `{Team}_{활동기간 토큰}_CHR_{Name}_{Tier}`(TASK-KBO-175). 이름+구단+활동 기간+티어가 곧 카드의
    정체성이다. `tier_token`은 ID에 박히는 문자열(LIVE 풀은 "LIVE"), `grade`는 실제 CheerleaderGrade enum 값이다
    (CheerleaderGrade에 "LIVE"가 없어 LIVE는 LIVE_NORMAL로 매핑 - TASK-KBO-171 판단 유지). 중복 ID는 원본 데이터
    오류이므로 조용히 덮어쓰지 않고 즉시 예외를 던진다."""
    catalog_id = f"{team}_{cheer_period_token(active_period)}_CHR_{name}_{tier_token}"
    if catalog_id in _seen_cheer_ids:
        raise ValueError(f"[cheerleaders] 중복 CatalogId: {catalog_id}")
    _seen_cheer_ids.add(catalog_id)

    meta = CHEER_GRADE_META[grade]
    cheerleaders_rows.append([
        catalog_id, name, grade, meta["buff"], meta["economic"], meta["clutch"], meta["sentiment"],
        team, active_period,
    ])


for _team, _names in CHEER_LIVE_2026.items():
    for _name in _names:
        _add_cheer_row(_name, _team, CHEER_LIVE_PERIOD, "LIVE", "LIVE_NORMAL")

for _name, _team, _active_period, _tiers in CHEER_ICON_LEGEND:
    for _tier in _tiers:
        _add_cheer_row(_name, _team, _active_period, _tier, _tier)

_cheer_counts = Counter(row[2] for row in cheerleaders_rows)
if dict(_cheer_counts) != CHEER_EXPECTED_COUNTS:
    raise ValueError(f"[TASK-KBO-175] 치어리더 카드 수 불일치: {dict(_cheer_counts)} (기대 {CHEER_EXPECTED_COUNTS})")

# ---------------------------------------------------------------------------
# 9. 파일 출력 (utf-8-sig - 명령서 6항, 엑셀/유니티 한글 깨짐 방지)
# ---------------------------------------------------------------------------
def save_csv(path, header, rows):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(header)
        writer.writerows(rows)

# [TASK-KBO-179] 1군 검증 인물 전용 DB - 출력 직전 정화/검증.
#   (1) 모든 카드의 player_id는 검증 실존 인물(real_player_records + 강제 주입 김상엽)이어야 한다 - 가상 인물이나
#       수상 기록 없는 왕조 로스터 인물의 카드가 하나라도 섞이면 즉시 실패시킨다.
#   (2) players.csv에는 카드가 1장 이상 있는 검증 인물만 남긴다(가상 인물 4,000명은 난수 스트림 보존용으로 메모리에서만
#       생성되고 출력되지 않는다 - player_id 시퀀스에 빈 번호가 생기지만 기존 ID(구자욱 PLY_004038 등)는 그대로다).
VERIFIED_PLAYER_IDS = _real_ids | {r.player_id for r in real_2026_records} | {KIM_SANGYEOP_ID}  # 2026 KBO 등록 명단(동명이인 접미사 포함)도 실명 검증 인물
_unverified_cards = [row[0] for rows in cards_by_team.values() for row in rows if row[1] not in VERIFIED_PLAYER_IDS]
if _unverified_cards:
    raise ValueError(f"[TASK-KBO-179] 비검증 인물 카드 발견 {len(_unverified_cards)}장: {_unverified_cards[:10]}")
_carded_player_ids = {row[1] for rows in cards_by_team.values() for row in rows}
purged_player_rows = [row for row in players_rows if row[0] not in _carded_player_ids]
players_rows = [row for row in players_rows if row[0] in _carded_player_ids]
for _pid, _label in (("PLY_004038", "구자욱"), (KIM_SANGYEOP_ID, "김상엽")):
    if _pid not in _carded_player_ids:
        raise ValueError(f"[TASK-KBO-179] ID 보존 위반: {_label}({_pid}) 누락")

os.makedirs(OUTPUT_DIR, exist_ok=True)

save_csv(os.path.join(OUTPUT_DIR, "players.csv"), PLAYERS_HEADER, players_rows)

total_cards = 0
for _, _, team_token in TEAMS:
    rows = cards_by_team[team_token]
    save_csv(os.path.join(OUTPUT_DIR, f"cards_{team_token}.csv"), CARDS_HEADER, rows)
    total_cards += len(rows)

save_csv(os.path.join(OUTPUT_DIR, "cheerleaders.csv"), CHEERLEADERS_HEADER, cheerleaders_rows)

# 이제 병합 파싱 방식(cards_*.csv)으로 완전히 대체되었으므로, 옛 단일 cards.csv는 삭제한다
# (PlayerDatabase.cs가 더는 이 경로를 읽지 않음 - 남겨두면 아무도 읽지 않는 죽은 파일이 된다).
legacy_cards_path = os.path.join(OUTPUT_DIR, "cards.csv")
if os.path.exists(legacy_cards_path):
    os.remove(legacy_cards_path)

# ---------------------------------------------------------------------------
# 10. 요약 출력 (완료 보고서 D항에 그대로 인용)
# ---------------------------------------------------------------------------
print("=" * 60)
print("[GenerateKBODatabase] KBO 마스터 DB 생성 완료")
print("=" * 60)
print(f"총 선수 수(출력, 1군 검증 인물만): {len(players_rows)}명 / 정화 제외 {len(purged_player_rows)}명")
print(f"  - [TASK-179] 폐기된 확률 발급 카드(가상/비검증 인물): {len(discarded_random_cards)}장 "
      f"{dict(Counter(r[3] for r in discarded_random_cards))}")
print(f"  - [TASK-179] 검증 풀 LIVE_EPIC(2026 현역 수상 경력자): {live_epic_card_count}장")
_out_ids = {row[0] for row in players_rows}
for _, _, team_token in TEAMS:
    team_player_count = sum(1 for r in player_records if r.team_token == team_token and r.player_id in _out_ids)
    print(f"  - {team_token}: 선수 {team_player_count}명, 카드 {len(cards_by_team[team_token])}장")
print(f"총 카드 수(전 구단 합계): {total_cards}장")
print(f"  - 이 중 2025시즌 실제 검증 수상 카드: {real_card_count}장")
print(f"  - 실제 인물로 등록된 누적 총원(2025 수상 + 역대 골든글러브 등): {len(real_player_records)}명")
print(f"  - 이 중 2026년 실제 현역 로스터 LIVE_NORMAL 카드: {roster_2026_card_count}장 ({len(real_2026_records)}명, 감독/코치 제외)")
print(f"  - 이 중 1986~2024 골든글러브 확정 카드: {gg_history_card_count}장 ({len(GOLDEN_GLOVE_HISTORY)}개 시즌, 2004년만 11명)")
print(f"  - 이 중 2013~2024 개인 타이틀 확정 카드: {title_history_card_count}장 (다관왕은 연도당 1장으로 통합)")
print(f"  - 이 중 영구결번(RETIRED_NUMBER) 확정 카드: {retired_number_card_count}장 ({len(RETIRED_NUMBER_HISTORY)}명 전원, 1986~2025)")
print(f"  - 이 중 시그니처(SIGNATURE) 확정 카드: {signature_card_count}장 (KBO 레전드 40인 전원)")
print(f"  - 이 중 올스타(ALLSTAR) 확정 카드: {allstar_card_count}장 (2014~2025 BEST 12, 감독 추천 후보 제외)")
print(f"  - 이 중 강제 주입 카드(김상엽'95 GG, 구자욱'26 SIG): {forced_card_count}장")
print(f"  - [TASK-172] 1986~2012 개인 타이틀 확정 카드: {title_1986_2012_card_count}장 (TITLE_HOLDER 전 연도 확대)")
print(f"  - [TASK-172] SIGNATURE 쿼터 보강 카드: {len(signature_quota_cards_to_issue)}장, 실존 불펜 SIG {signature_bullpen_count}장")
print(f"  - [TASK-172] 실존 인물 SIGNATURE 구단별 (전체/투수/야수): {signature_quota_report}")
print(f"  - [TASK-172] DYNASTY 1인 1연도 정예 카드: {dynasty_card_count}장")
print(f"  - [TASK-172] FRANCHISE 카드: {franchise_card_count}장 (TASK-179: 동일 구단 검증 시즌 2개 이상 실존 인물, 올스타 단독 시즌)")
_grade_totals = Counter(row[3] for rows in cards_by_team.values() for row in rows)
print(f"  - [TASK-172] 등급별 총계: {dict(sorted(_grade_totals.items(), key=lambda kv: GRADE_ID[kv[0]]))}")
_raised = [r for r in stat_recalibration_report if r[3] > r[2]]
print(f"  - [TASK-175] 수상 장부 기반 기본 OVR 산정: 실존 {len(stat_recalibration_report)}명 "
      f"(상향 {len(_raised)}명, 보정 전 평균 {sum(r[2] for r in stat_recalibration_report) / len(stat_recalibration_report):.1f} -> "
      f"보정 후 {sum(r[3] for r in stat_recalibration_report) / len(stat_recalibration_report):.1f})")

_cheer_grade_counts = {}
_cheer_unique_names = set()
for _row in cheerleaders_rows:
    _cheer_grade_counts[_row[2]] = _cheer_grade_counts.get(_row[2], 0) + 1
    _cheer_unique_names.add(_row[1])
print(f"총 치어리더 카탈로그 수: {len(cheerleaders_rows)}장 (TASK-KBO-175 확정 로스터 - "
      f"고유 인물 {len(_cheer_unique_names)}명, 소속 구단 + 활동 기간 단위 발급)")
for _grade in ("LIVE_NORMAL", "LIVE_EPIC", "ICON", "LEGEND", "SEASON_LIMITED"):
    if _grade in _cheer_grade_counts:
        print(f"  - {_grade}: {_cheer_grade_counts[_grade]}장")
print(f"players.csv 총 줄 수(헤더 포함): {len(players_rows) + 1}")
print(f"cards_*.csv 총 줄 수 합계(헤더 10개 포함): {total_cards + 10}")
print(f"cheerleaders.csv 총 줄 수(헤더 포함): {len(cheerleaders_rows) + 1}")
grand_total_lines = (len(players_rows) + 1) + (total_cards + 10) + (len(cheerleaders_rows) + 1)
print(f"생성된 전체 CSV 줄 수 합계: {grand_total_lines}")
