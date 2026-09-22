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

random.seed(20260922)  # 재현 가능한 결과(재실행해도 동일 DB) - 명령서에 시드 요구는 없으나 디버깅 편의상 고정.

# ---------------------------------------------------------------------------
# 0. 경로
# ---------------------------------------------------------------------------
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
OUTPUT_DIR = os.path.join(SCRIPT_DIR, "Assets", "Resources", "Data")

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
GRADE_META = {
    "LIVE_NORMAL":    {"code": "LN",   "ovr": (55, 68), "salary": 12, "max_enhance": 10, "max_awaken": 0,  "droppable": "TRUE"},
    "LIVE_EPIC":      {"code": "EPIC", "ovr": (65, 76), "salary": 18, "max_enhance": 10, "max_awaken": 0,  "droppable": "TRUE"},
    "ALLSTAR":        {"code": "AS",   "ovr": (74, 83), "salary": 24, "max_enhance": 10, "max_awaken": 10, "droppable": "TRUE"},
    "TITLE_HOLDER":   {"code": "TH",   "ovr": (80, 87), "salary": 30, "max_enhance": 10, "max_awaken": 10, "droppable": "FALSE"},
    "RETIRED_NUMBER": {"code": "RN",   "ovr": (85, 91), "salary": 33, "max_enhance": 10, "max_awaken": 10, "droppable": "FALSE"},
    "GOLDEN_GLOVE":   {"code": "GG",   "ovr": (83, 90), "salary": 35, "max_enhance": 10, "max_awaken": 10, "droppable": "FALSE"},
    "SIGNATURE":      {"code": "SIG",  "ovr": (87, 94), "salary": 40, "max_enhance": 10, "max_awaken": 10, "droppable": "FALSE"},
    "DYNASTY":        {"code": "DYN",  "ovr": (92, 99), "salary": 50, "max_enhance": 10, "max_awaken": 10, "droppable": "FALSE"},
}
# Types.cs의 Grade enum 정수값(TASK-KBO-155 재배치 이후 LIVE_NORMAL=1 ~ DYNASTY=8)과 동일한 서열.
GRADE_ID = {
    "LIVE_NORMAL": 1, "LIVE_EPIC": 2, "ALLSTAR": 3, "TITLE_HOLDER": 4,
    "RETIRED_NUMBER": 5, "SIGNATURE": 6, "GOLDEN_GLOVE": 7, "DYNASTY": 8,
}

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
                skip_random_cards=False):
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
    rec.z_value = random.gauss(1.0, 0.6)
    rec.pa_ip = random.randint(80, 180) if is_pitcher and position == "SP" else \
        (random.randint(30, 70) if is_pitcher else random.randint(200, 600))
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
# 5-B. [TASK-KBO-156] 리서치 2단계(사용자 지시 "최근 시즌부터 단계적으로") - 실제 2025시즌
# KBO 개인 타이틀/골든글러브 수상자를 실제 인물로 반영한다. statiz.co.kr/koreabaseball.com
# 실시간 조회(WebFetch/WebSearch, 2026-09-22 기준)로 확인한 사실만 담았다 - 확인하지 못한
# 나머지 타이틀 부문(최다안타/득점/출루율/장타율/세이브/홀드/승률왕 등)은 추측으로 채우지 않고
# 다음 조사 회차로 미룬다(완료 보고서 참고). team_token은 "수상 당시" 소속 구단이다 - 이후
# 트레이드로 소속이 바뀌었더라도 카드 자체는 그 시즌 그 구단 소속으로 발급되는 것이 실제 스포츠
# 카드 관행과 일치한다(2026-09-22 기준 최신 로스터와 다를 수 있음).
REAL_PLAYERS_2025 = [
    # (이름, team_token, team_id, team_enum, is_pitcher, position, z_value 근사치)
    ("디아즈", "SAMSUNG", "TEM_002", "Samsung", False, "1B", 2.6),   # 50홈런/158타점, 타자 3관왕
    ("양의지", "DOOSAN", "TEM_004", "Doosan", False, "C", 2.3),      # 타율왕, 포수 최초 복수 타율왕
    ("박해민", "LG", "TEM_003", "LG", False, "CF", 2.0),             # 도루왕(7년 만에 탈환)
    ("폰세", "HANWHA", "TEM_008", "Hanwha", True, "SP", 2.8),        # 다승/평균자책점(1.89)/탈삼진(252) 투수 4관왕, MVP
    ("신민재", "LG", "TEM_003", "LG", False, "2B", 1.8),             # 골든글러브 2루수
    ("송성문", "KIWOOM", "TEM_010", "Kiwoom", False, "3B", 1.8),     # 골든글러브 3루수
    ("김주원", "NC", "TEM_009", "NC", False, "SS", 1.8),             # 골든글러브 유격수
    ("안현민", "KT", "TEM_005", "KT", False, "RF", 1.9),             # 골든글러브 외야수, 신인왕
    ("구자욱", "SAMSUNG", "TEM_002", "Samsung", False, "RF", 1.9),   # 골든글러브 외야수
    ("레이예스", "LOTTE", "TEM_007", "Lotte", False, "LF", 1.8),     # 골든글러브 외야수(빅터 레예스)
]

# 등급 = 그 선수가 실제로 받은 상. TITLE_HOLDER는 세부 부문(홈런왕/타율왕 등)을 카드 스키마가
# 아직 구분하지 않으므로(TASK-KBO-155 범위 밖) "그 해 타이틀 홀더였다"는 사실만 카드 1장으로
# 반영한다. 한 선수가 같은 해에 TITLE_HOLDER와 GOLDEN_GLOVE를 모두 받았으면 카드 2장을 받는다
# (실제 카드 수집 게임처럼 같은 해 다른 상 = 다른 카드 SKU).
REAL_AWARDS_2025 = {
    "디아즈": ["TITLE_HOLDER", "GOLDEN_GLOVE"],
    "양의지": ["TITLE_HOLDER", "GOLDEN_GLOVE"],
    "박해민": ["TITLE_HOLDER"],
    "폰세": ["TITLE_HOLDER", "GOLDEN_GLOVE"],
    "신민재": ["GOLDEN_GLOVE"],
    "송성문": ["GOLDEN_GLOVE"],
    "김주원": ["GOLDEN_GLOVE"],
    "안현민": ["GOLDEN_GLOVE"],
    "구자욱": ["GOLDEN_GLOVE"],
    "레이예스": ["GOLDEN_GLOVE"],
}

real_player_records = {}
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

def make_card_row(rec, year, grade):
    meta = GRADE_META[grade]
    ovr_lo, ovr_hi = meta["ovr"]
    card_id = f"{rec.team_token}_{year}_{rec.player_id}_{meta['code']}"
    return [
        card_id, rec.player_id, GRADE_ID[grade], grade,
        random.randint(ovr_lo, ovr_hi), meta["salary"], meta["max_enhance"],
        meta["max_awaken"], meta["droppable"], year,
    ]

cards_by_team = {token: [] for _, _, token in TEAMS}

for rec in player_records:
    issued_year_grade = set()

    # 왕조 필수 로스터: 지정된 왕조 연도 구간은 100% DYNASTY 카드로 확정 생성(명령서 3항).
    if rec.is_dynasty_member:
        dyn_start, dyn_end = rec.dynasty_years
        for year in range(dyn_start, dyn_end + 1):
            cards_by_team[rec.team_token].append(make_card_row(rec, year, "DYNASTY"))
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
        cards_by_team[rec.team_token].append(make_card_row(rec, year, grade))

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
# 8. cheerleaders.csv 행 생성 (7컬럼 - CheerleaderCatalog.cs 실제 파서 스키마)
# ---------------------------------------------------------------------------
CHEERLEADERS_HEADER = [
    "CatalogId", "Name", "Grade", "ConditionBuff", "EconomicBonusRate",
    "ClutchMultiplier", "SentimentDefense",
]

# docs/16_shop_and_gacha_policy.md 2절에 PM이 이미 확정한 등급별 수치표를 그대로 가져다 썼다
# (스크립트가 임의로 지어낸 값이 아니다).
CHEER_GRADE_META = {
    "LIVE_NORMAL": {"buff": 1, "clutch": 1.00, "economic": 1.05, "sentiment": 0},
    "LIVE_EPIC":   {"buff": 2, "clutch": 1.05, "economic": 1.10, "sentiment": 1},
    "ICON":        {"buff": 3, "clutch": 1.10, "economic": 1.15, "sentiment": 2},
    "LEGEND":      {"buff": 4, "clutch": 1.15, "economic": 1.20, "sentiment": 3},
}
CHEER_SURNAMES = ["이", "김", "박", "최", "정", "한", "윤", "강", "임", "송"]
CHEER_GIVEN = [
    "수진", "한나", "기량", "지현", "서연", "하윤", "지우", "은서", "예린", "다혜",
    "소율", "채원", "유나", "가은", "민서",
]
_used_cheer_names = set()

def make_cheer_name():
    for _ in range(50):
        name = random.choice(CHEER_SURNAMES) + random.choice(CHEER_GIVEN)
        if name not in _used_cheer_names:
            _used_cheer_names.add(name)
            return name
    return random.choice(CHEER_SURNAMES) + random.choice(CHEER_GIVEN)

cheerleaders_rows = []
# [사실 정정] Cheerleader 모델(Models/Cheerleader.cs)에는 Year 필드가 없다 - "연도별로 생성"
# 요구사항은 연도마다 별도의 카탈로그 항목을 만드는 방식으로 물량을 충족하되, 그 연도는
# CatalogId에만 흔적으로 남을 뿐 게임 로직에는 쓰이지 않는다(기존에도 쓰인 적 없음, 이 스크립트가
# 새로 만든 한계가 아니다).
for year in range(MIN_YEAR, MAX_YEAR + 1):
    for grade, meta in CHEER_GRADE_META.items():
        catalog_id = f"CHR_{year}_{grade}"
        name = make_cheer_name()
        cheerleaders_rows.append([
            catalog_id, name, grade, meta["buff"], meta["economic"], meta["clutch"], meta["sentiment"],
        ])

# ---------------------------------------------------------------------------
# 9. 파일 출력 (utf-8-sig - 명령서 6항, 엑셀/유니티 한글 깨짐 방지)
# ---------------------------------------------------------------------------
def save_csv(path, header, rows):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(header)
        writer.writerows(rows)

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
print(f"총 선수 수: {len(player_records)}명")
for _, _, team_token in TEAMS:
    team_player_count = sum(1 for r in player_records if r.team_token == team_token)
    print(f"  - {team_token}: 선수 {team_player_count}명, 카드 {len(cards_by_team[team_token])}장")
print(f"총 카드 수(전 구단 합계): {total_cards}장")
print(f"  - 이 중 2025시즌 실제 검증 수상 카드: {real_card_count}장 ({len(real_player_records)}명)")
print(f"총 치어리더 카탈로그 수: {len(cheerleaders_rows)}개")
print(f"players.csv 총 줄 수(헤더 포함): {len(players_rows) + 1}")
print(f"cards_*.csv 총 줄 수 합계(헤더 10개 포함): {total_cards + 10}")
print(f"cheerleaders.csv 총 줄 수(헤더 포함): {len(cheerleaders_rows) + 1}")
grand_total_lines = (len(players_rows) + 1) + (total_cards + 10) + (len(cheerleaders_rows) + 1)
print(f"생성된 전체 CSV 줄 수 합계: {grand_total_lines}")
