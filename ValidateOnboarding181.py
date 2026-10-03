# -*- coding: utf-8 -*-
"""
[TASK-KBO-181] 정식 온보딩 데이터 검증 - 실제 cards_*.csv / players.csv로 C# 규칙을 재현한다.

  1) 정착 지원 선물 4종(구자욱 '24 · 김도영 '24 · 하트 '24 · 로하스 '24)이 2024 GOLDEN_GLOVE 행으로 존재하는지 + 세부 스탯 표
  2) 10개 구단 각각: 2026 LIVE_NORMAL 전원 지급(OnboardingRules.SelectStarterTemplates) → RosterManager.AutoSetRoster 재현
     (주전 포지션/보직 OVR 우선 → 후보 6 → 같은 그룹 우선 폴백[TASK-181 수정]) → 타자 15 / 투수 13 = 28인
  3) LineupView 재현: 주전 9칸 전부 채움(DH 대체) · BENCH 6 · 1~5선발 · 불펜 8칸 빈칸 0
  4) 선물 4종 각각을 구단 10개에 넣었을 때 주전/선발 편성 여부(DescribeGiftPlacement)
카드 OVR = cards_*.csv base_ovr(PlayerDatabase가 스탯을 base_ovr에 맞춰 이동 - TASK-174). 동점은 C# 안정 정렬처럼 입력 순서 유지.
사용: python ValidateOnboarding181.py   (실패 시 종료 코드 1)
"""
import csv, glob, io, os, sys
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ValidatePlayerStats180 import load, card_stats, DATA

GIFTS = ["SAMSUNG_2024_PLY_004038_GG", "KIA_2024_PLY_004368_GG", "NC_2024_PLY_004366_GG", "KT_2024_PLY_004369_GG"]
BATTER_POS = ["C", "1B", "2B", "3B", "SS", "LF", "CF", "RF", "DH"]
ROLE = {"SP": "SP", "CP": "CP", "LR": "LR", "MR": "MR", "RP": "RP"}  # RP = 승리조(WinningReliever)
QUOTA = [("SP", 5), ("RP", 2), ("MR", 4), ("LR", 1), ("CP", 1)]
TEAMS = ["SAMSUNG", "KIA", "LG", "DOOSAN", "KT", "SSG", "LOTTE", "HANWHA", "NC", "KIWOOM"]
SD = {"LIVE_NORMAL": 8, "GOLDEN_GLOVE": 6}  # 세트덱 기여(자팀 LIVE 8 / GG 구단 무관 6) - 후보 정렬용

players, cards = load(DATA)
# C# PlayerDatabase는 card_id를 딕셔너리 키로 써서 같은 행이 두 번 있으면 1장만 등록한다 - 동일하게 중복 제거.
_raw_count = len(cards)
cards = list({r["card_id"]: r for r in cards}.values())
DUPLICATE_ROWS = _raw_count - len(cards)
fails = []


def card(row):
    p = players[row["player_id"]]
    pos = p["position"]
    return {"id": row["card_id"], "name": p["name"], "pos": pos, "pitcher": pos in ROLE, "ovr": int(row["base_ovr"]),
            "grade": row["grade_name"], "team": row["card_id"].split("_")[0], "year": row["year"], "person": row["player_id"]}


def starter_pack(team):
    """OnboardingManager.GenerateStarterPack 재현 - 2026 LIVE_NORMAL 전원 + 투수/타자 정원 미달 시 절차 생성 신인(OVR 50) 보충."""
    pack = [card(r) for r in cards if r["card_id"].startswith(team + "_2026_") and r["grade_name"] == "LIVE_NORMAL" and r["year"] == "2026"]
    pitchers = sum(c["pitcher"] for c in pack)
    for role, n in QUOTA:
        missing = n - sum(1 for c in pack if c["pitcher"] and ROLE[c["pos"]] == role)
        for i in range(max(0, missing)):
            if pitchers >= 13: break
            pack.append({"id": f"STARTER_{team}_{role}{i}", "name": f"{team} 신인({role})", "pos": role, "pitcher": True, "ovr": 50,
                         "grade": "LIVE_NORMAL", "team": team, "year": "2026", "person": f"STARTER_{team}_{role}{i}"})
            pitchers += 1
    return pack


def contribution(c, deck):
    if c["grade"] == "GOLDEN_GLOVE": return 6
    return SD.get(c["grade"], 0) if c["team"] == deck else 0


def auto_roster(inv, deck):
    """[TASK-KBO-182] RosterManager.AutoSetRoster 재현 - 선택 구단 최우선 + 동일 인물(player_id) 1장."""
    pool = list(inv)
    used = set()
    slots = [("B", p) for p in BATTER_POS] + [("P", r) for r, n in QUOTA for _ in range(n)] + [("BENCH", None)] * 6
    assigned = [None] * len(slots)
    pref = lambda c: c["team"] == deck
    exact = lambda c, k, need: (k == "B" and not c["pitcher"] and c["pos"] == need) or (k == "P" and c["pitcher"] and ROLE[c["pos"]] == need) or (k == "BENCH" and not c["pitcher"])
    avail = lambda: [c for c in pool if c["person"] not in used]

    def assign(i, c):
        assigned[i] = c; pool.remove(c); used.add(c["person"])

    def starter_pass(flt, exact_role, pitchers_only=False):
        for i, (k, need) in enumerate(slots):
            if k == "BENCH" or assigned[i] or (pitchers_only and k != "P"): continue
            cands = [c for c in avail() if flt(c) and (exact(c, k, need) if exact_role else c["pitcher"] == (k == "P"))]
            if cands: assign(i, max(cands, key=lambda c: (c["ovr"], SD.get(c["grade"], 0))))
    starter_pass(pref, True)
    starter_pass(pref, False, True)
    starter_pass(lambda c: True, True)
    for i, (k, _) in enumerate(slots):  # 빈 주전 타자 칸(DH) 먼저
        if k != "B" or assigned[i]: continue
        cands = [c for c in avail() if not c["pitcher"]]
        if cands: assign(i, max(cands, key=lambda c: (pref(c), c["ovr"], SD.get(c["grade"], 0))))
    for i, (k, _) in enumerate(slots):
        if k != "BENCH" or assigned[i]: continue
        cands = [c for c in avail() if not c["pitcher"]]
        if cands: assign(i, max(cands, key=lambda c: (pref(c), contribution(c, deck), c["ovr"])))
    for i, (k, need) in enumerate(slots):
        if assigned[i]: continue
        cands = avail()
        if not cands: break
        want = k == "P"
        assign(i, max(cands, key=lambda c: (c["pitcher"] == want, pref(c), exact(c, k, need), contribution(c, deck), c["ovr"])))
    return [c for c in assigned if c]


def lineup_view(roster):
    batters = sorted([c for c in roster if not c["pitcher"]], key=lambda c: -c["ovr"])
    starters = {}
    for pos in BATTER_POS:
        pick = next((c for c in batters if c["pos"] == pos and c not in starters.values()), None)
        if pick: starters[pos] = pick
    remaining = [c for c in batters if c not in starters.values()]
    lineup = []
    for pos in BATTER_POS:
        if pos in starters: lineup.append((pos, starters[pos], False))
        elif remaining: lineup.append((pos, remaining.pop(0), True))
        else: lineup.append((pos, None, False))
    pitchers = sorted([c for c in roster if c["pitcher"]], key=lambda c: -c["ovr"])
    sp = [c for c in pitchers if ROLE[c["pos"]] == "SP"]
    overflow = sp[5:]
    rotation = sp[:5] + [None] * (5 - len(sp[:5]))
    bullpen = []
    for role, n in [("RP", 2), ("MR", 4), ("LR", 1), ("CP", 1)]:
        of = [c for c in pitchers if ROLE[c["pos"]] == role]
        bullpen += of[:n] + [None] * (n - len(of[:n])); overflow += of[n:]
    overflow.sort(key=lambda c: -c["ovr"])
    for arr in (bullpen, rotation):
        for i, v in enumerate(arr):
            if v is None and overflow: arr[i] = overflow.pop(0)
    return lineup, remaining, rotation, bullpen, overflow


by_id = {r["card_id"]: r for r in cards}
print("== 1) 정착 지원 선물 2024 골든글러브 4종")
print(f"{'card_id':30} {'선수':6} {'구단':8} {'포지션':4} {'OVR':>4}  세부 스탯")
gift_cards = []
for gid in GIFTS:
    row = by_id.get(gid)
    if row is None or row["grade_name"] != "GOLDEN_GLOVE" or row["year"] != "2024":
        fails.append(f"선물 카드 누락/불일치: {gid}"); continue
    c = card(row); gift_cards.append(c)
    names, stats, ovr = card_stats(players, row)
    print(f"{gid:30} {c['name']:6} {c['team']:8} {c['pos']:4} {c['ovr']:>4}  " + " / ".join(f"{n} {s}" for n, s in zip(names, stats)))
    if len(set(stats)) == 1: fails.append(f"{gid} 세부 스탯 평준화")

print("\n== 2~3) 구단별 2026 LIVE_NORMAL 지급 + 오토 라인업 + 라인업 탭 슬롯")
for team in TEAMS:
    inv = starter_pack(team)
    roster = auto_roster(inv, team)
    nb = sum(not c["pitcher"] for c in roster); np_ = sum(c["pitcher"] for c in roster)
    lineup, bench, rotation, bullpen, extra = lineup_view(roster)
    empty = sum(v is None for _, v, _ in lineup) + sum(v is None for v in rotation) + sum(v is None for v in bullpen)
    fill = [pos for pos, _, f in lineup if f]
    ok = nb == 15 and np_ == 13 and len(bench) == 6 and empty == 0 and not extra
    print(f"{team:8} 지급 {len(inv):2}장(타 {sum(not c['pitcher'] for c in inv):2}/투 {sum(c['pitcher'] for c in inv):2}) → 1군 타자 {nb} / 투수 {np_}"
          f" · BENCH {len(bench)} · 빈칸 {empty} · 대체 {fill} · 보관 {len(inv) - len(roster)}장  {'OK' if ok else 'FAIL'}")
    if not ok: fails.append(f"{team} 로스터 구성 실패")

print("\n== 4) 선물 카드 편성(구단 10개 x 선물 4종)")
for g in gift_cards:
    started = []
    for team in TEAMS:
        inv = starter_pack(team) + [g]
        roster = auto_roster(inv, team)
        lineup, bench, rotation, bullpen, _ = lineup_view(roster)
        if g not in roster: started.append(f"{team}:보관"); continue
        if g["pitcher"]:
            started.append(f"{team}:{rotation.index(g) + 1}선발" if g in rotation else f"{team}:불펜")
        else:
            slot = next((p for p, c, _ in lineup if c is g), None)
            started.append(f"{team}:{slot}주전" if slot else f"{team}:후보")
        if team == g["team"] and not (started[-1].endswith("주전") or started[-1].endswith("선발")):
            fails.append(f"{g['name']} 선물이 자기 구단({team}) 선택 시 주전/선발에 들어가지 않음")
        if len({c["person"] for c in roster}) != len(roster): fails.append(f"{team}+{g['name']} 동일 인물 중복 편성")
        if any(c["team"] != team for c in roster if c is not g): fails.append(f"{team}+{g['name']} 타 구단 선수 편성")
    print(f"{g['name']} '24 ({g['team']} {g['pos']}, OVR {g['ovr']}): " + ", ".join(started))

# [TASK-KBO-182] 선택 구단 최우선 + 동일 인물 1장: 10개 구단 x 2026 LIVE 전원 + 선물 4종 + 같은 인물 다른 카드(GG/AS 등) 전부 보유 시
print("\n== 5) 선택 구단 최우선 + 동일 player_id 중복 금지(구단 2026 LIVE + 선물 4종 + 자기 구단 상위 카드 전부 보유)")
for team in TEAMS:
    inv = [card(r) for r in cards if r["card_id"].startswith(team + "_") and (r["year"] == "2026" or r["grade_name"] != "LIVE_NORMAL")]
    inv += [g for g in gift_cards if g["team"] != team]
    roster = auto_roster(inv, team)
    persons = [c["person"] for c in roster]
    foreign = [c["name"] for c in roster if c["team"] != team]
    dup = len(persons) - len(set(persons))
    nb = sum(not c["pitcher"] for c in roster); np_ = sum(c["pitcher"] for c in roster)
    ok = dup == 0 and not foreign and nb == 15 and np_ == 13
    print(f"{team:8} 보유 {len(inv):3}장(동일 인물 다장 {len(inv) - len({c['person'] for c in inv})}) → 1군 {nb}/{np_} · 타 구단 {len(foreign)} · 중복 {dup}  {'OK' if ok else 'FAIL'}")
    if not ok: fails.append(f"{team} 선택 구단 우선/중복 금지 실패")

print("\n결과:", "PASS" if not fails else "FAIL")
for f in fails: print(" -", f)
sys.exit(1 if fails else 0)
