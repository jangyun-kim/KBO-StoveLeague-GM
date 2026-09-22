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

def _resolve_historical(name, team_token, is_pitcher, position):
    # 이름만으로 조회한다(이 표의 선수는 전부 유일하게 식별되는 실존 인물이라 동명이인 위험이
    # 없다) - 연도마다 소속이 달라도 인물 자체(RealPlayerId)는 하나로 유지한다.
    existing = real_player_records.get(name)
    if existing is not None:
        existing.skip_random_cards = True
        return existing
    team_id, team_enum = TEAM_LOOKUP_2026[team_token]
    rec = add_player(team_token, team_id, team_enum, name, is_pitcher, position, 2010, MAX_YEAR, skip_random_cards=True)
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
# 위키백과 "KBO 리그 영구 결번 목록" 실시간 조회(2026-09-22)로 확인해 반영한다 - 이 문서
# 작성 시점 기준 18명 전원(1986년 김영신부터 2025년 오승환까지)이며, 추후 새로 영구결번이
# 지정되면 이 목록에 추가하면 된다. 연도는 "영구결번 지정 연도"를 카드 연도로 삼았다(그 해에
# 구단이 이 선수의 커리어 전체를 기려 번호를 걸었다는 실제 사실이 발생한 시점). 옛 구단명은
# GOLDEN_GLOVE_HISTORY와 동일한 프랜차이즈 승계 매핑을 따른다(해태→KIA, OB→DOOSAN, SK→SSG).
RETIRED_NUMBER_HISTORY = [
    ("이종범", "KIA", 2012),
    ("이병규", "LG", 2017),
    ("양준혁", "SAMSUNG", 2010),
    ("이대호", "LOTTE", 2022),
    ("최동원", "LOTTE", 2011),
    ("선동열", "KIA", 1996),
    ("박철순", "DOOSAN", 2002),
    ("송진우", "HANWHA", 2009),
    ("이만수", "SAMSUNG", 2004),
    ("정민철", "HANWHA", 2009),
    ("박경완", "SSG", 2014),
    ("박용택", "LG", 2022),
    ("장종훈", "HANWHA", 2005),
    ("이승엽", "SAMSUNG", 2017),
    ("김용수", "LG", 1999),
    ("김태균", "HANWHA", 2021),
    ("김영신", "DOOSAN", 1986),
    ("오승환", "SAMSUNG", 2025),
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

def make_card_row(rec, year, grade, team_token_override=None):
    # [TASK-KBO-159] team_token_override - 실제 선수는 이적/FA로 해마다 소속이 달라질 수 있다
    # (예: 최형우 삼성<->KIA, 양의지 두산<->NC) - 그 해의 실제 소속을 카드 ID/파일 배치에
    # 반영하기 위한 인자다. 기본 템플릿(rec.team_token, rec.team_id)은 그대로 두고 카드 한 장
    # 단위로만 다른 구단을 표시할 수 있다.
    team_token = team_token_override or rec.team_token
    meta = GRADE_META[grade]
    ovr_lo, ovr_hi = meta["ovr"]
    card_id = f"{team_token}_{year}_{rec.player_id}_{meta['code']}"
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
print(f"  - 이 중 2025시즌 실제 검증 수상 카드: {real_card_count}장")
print(f"  - 실제 인물로 등록된 누적 총원(2025 수상 + 역대 골든글러브 등): {len(real_player_records)}명")
print(f"  - 이 중 2026년 실제 현역 로스터 LIVE_NORMAL 카드: {roster_2026_card_count}장 ({len(real_2026_records)}명, 감독/코치 제외)")
print(f"  - 이 중 1986~2024 골든글러브 확정 카드: {gg_history_card_count}장 ({len(GOLDEN_GLOVE_HISTORY)}개 시즌, 2004년만 11명)")
print(f"  - 이 중 2013~2024 개인 타이틀 확정 카드: {title_history_card_count}장 (다관왕은 연도당 1장으로 통합)")
print(f"  - 이 중 영구결번(RETIRED_NUMBER) 확정 카드: {retired_number_card_count}장 ({len(RETIRED_NUMBER_HISTORY)}명 전원, 1986~2025)")
print(f"  - 이 중 시그니처(SIGNATURE) 확정 카드: {signature_card_count}장 (KBO 레전드 40인 전원)")
print(f"총 치어리더 카탈로그 수: {len(cheerleaders_rows)}개")
print(f"players.csv 총 줄 수(헤더 포함): {len(players_rows) + 1}")
print(f"cards_*.csv 총 줄 수 합계(헤더 10개 포함): {total_cards + 10}")
print(f"cheerleaders.csv 총 줄 수(헤더 포함): {len(cheerleaders_rows) + 1}")
grand_total_lines = (len(players_rows) + 1) + (total_cards + 10) + (len(cheerleaders_rows) + 1)
print(f"생성된 전체 CSV 줄 수 합계: {grand_total_lines}")
