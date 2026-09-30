# -*- coding: utf-8 -*-
"""
[TASK-KBO-172 신설 / TASK-KBO-173 역전 밸런스로 전면 갱신] 27인 세트덱 스코어 시뮬레이터.

개인 스코어 규격은 Assets/Scripts/Models/CardGrowthRules.cs와 1:1 동일하다(TASK-KBO-173 역전 테이블 -
종결 카드일수록 낮고 LIVE가 가장 높음). 버프 구간은 Models/SetDeck.cs SetDeckBuffTable(기획 고도화 자료.pdf)
중 "대상 전원" 효과만 합산해 전력 비교에 쓴다. 결과표는 docs/04_card_grade_policy.md 8·10절에 옮겼다.
실행: python SimulateSetDeckTiers.py

[PDF 과금 영역 해석 규칙] 등급 인원은 "그 등급 이상" 누적 상한(ALL_STAR 26 = AS 이상 26명 ...), DYNASTY
성장 단계 인원도 "그 단계 이상" 누적으로 해석한다(TASK-KBO-172와 동일 - 04 문서 8-1절).
"""

# CardGrowthRules.cs와 동일 (TASK-KBO-173)
BASE = {"LIVE": 4, "AS": 4, "FRA": 3, "TH": 3, "RN": 2, "GG": 2, "SIG": 1, "DYN": 1}
TRANSCEND = {"LIVE", "GG", "SIG", "DYN"}
STAGE_LEVEL = {"명함": 0, "3각": 3, "6각": 6, "9각": 9, "초월": 10}

# SetDeckBuffTable 중 대상 전원 효과(구간: 가산값) - 모든 능력치 / 타자 전 스탯 / 투수 전 스탯
ALL_STATS = {30: 1, 40: 1, 50: 1, 90: 2, 125: 1, 130: 1, 145: 1, 160: 1, 170: 1, 180: 2, 195: 1, 200: 3}
BATTER_ALL = {60: 1, 110: 1}
PITCHER_ALL = {70: 1, 105: 1}
THRESHOLDS = [30, 40, 50, 60, 70, 80, 90, 100, 105, 110, 115, 120, 125, 130, 135, 140, 145,
              150, 155, 160, 165, 170, 175, 180, 185, 190, 195, 200]


def score(grade, stage):
    level = STAGE_LEVEL[stage]
    if grade not in TRANSCEND:
        level = min(level, 9)
    s = BASE[grade] + sum(1 for step in (3, 6, 9) if level >= step)
    if level >= 10 and grade in TRANSCEND:
        s += 1
    return s


def full(grade):
    return "초월" if grade in TRANSCEND else "9각"


def deck_score(deck):
    return sum(score(g, st) for g, st in deck)


def buffs(total):
    flat = sum(v for k, v in ALL_STATS.items() if total >= k)
    bat = flat + sum(v for k, v in BATTER_ALL.items() if total >= k)
    pit = flat + sum(v for k, v in PITCHER_ALL.items() if total >= k)
    reached = sum(1 for t in THRESHOLDS if total >= t)
    return flat, bat, pit, reached


def milestone(s):
    if s >= 200:
        return "최종 200P"
    if s >= 190:
        return "핵심 190P"
    if s >= 185:
        return "핵심 185P"
    if s >= 150:
        return "1차 150P"
    return "목표 미달"


def deck(*parts):
    """parts: (grade, stage, count) ..."""
    out = []
    for grade, stage, count in parts:
        out += [(grade, stage)] * count
    return out


def report(label, starters, bench):
    d = starters + bench
    assert len(starters) == 21 and len(bench) == 6, (label, len(starters), len(bench))
    total = deck_score(d)
    flat, bat, pit, reached = buffs(total)
    comp = {}
    for g, st in starters:
        comp[f"{g} {st}"] = comp.get(f"{g} {st}", 0) + 1
    comp_s = ", ".join(f"{k} {v}" for k, v in comp.items())
    bench_s = ", ".join(sorted({f"{g} {st}" for g, st in bench}))
    print(f"  {label}: {total}P [{milestone(total)}] 구간 {reached}개 / 모든 능력치 +{flat}, "
          f"타자 전 스탯 +{bat}, 투수 전 스탯 +{pit}")
    print(f"      주전 21 = {comp_s} | 후보 6 = {bench_s}({deck_score(bench)}P)")
    return total


# (영역, AS이상, TH이상, SIG이상, GG이상, DYN [(단계, 그 단계 이상 누적)])
TIERS = [
    ("무과금", 10, 6, 4, 0, []),
    ("무소과금", 15, 10, 9, 1, []),
    ("소과금", 20, 15, 12, 4, []),
    ("중과금", 26, 20, 15, 8, [("3각", 2), ("명함", 1)]),
    ("핵과금", 26, 26, 20, 12, [("6각", 1), ("3각", 2), ("명함", 3)]),
    ("초핵과금", 26, 26, 26, 20, [("초월", 4), ("6각", 6), ("3각", 10), ("명함", 10)]),
]


def tier_max_premium(tier, live_stage="초월"):
    name, as_up, th_up, sig_up, gg_up, dyn_spec = tier
    dyn, taken = [], 0
    for stage, at_least in dyn_spec:
        n = max(0, at_least - taken)
        dyn += [("DYN", stage)] * n
        taken += n
    d = dyn + [("GG", "초월")] * (gg_up - len(dyn)) + [("SIG", "초월")] * (sig_up - gg_up) \
        + [("TH", "9각")] * (th_up - sig_up) + [("AS", "9각")] * (as_up - th_up) + [("LIVE", live_stage)] * (27 - as_up)
    assert len(d) == 27
    return d


def main():
    print("[개인 세트덱 스코어 = Salary] " + ", ".join(
        f"{g} {BASE[g]}->{score(g, full(g))}" for g in BASE))
    print()

    print("A. '왕조 13 + 골글 13 + 시그 1' 도배 페널티 검증 (27인 전원 종결 카드)")
    for stage in ("명함", "6각", "9각", "초월"):
        d = deck(("DYN", stage, 13), ("GG", stage, 13), ("SIG", stage, 1))
        total = deck_score(d)
        flat, bat, pit, reached = buffs(total)
        print(f"  전원 {stage}: {total}P [{milestone(total)}] 구간 {reached}개 / 모든 능력치 +{flat}, "
              f"타자 +{bat}, 투수 +{pit}")
    print()

    print("B. 정상 로스터 - 후보 6인 LIVE 초월(48P) + 주전 21인 전략 배분")
    live_bench = deck(("LIVE", "초월", 6))
    report("B1 최종 200P", deck(("DYN", "초월", 1), ("SIG", "초월", 1), ("GG", "초월", 1), ("TH", "9각", 1),
                              ("AS", "9각", 6), ("LIVE", "초월", 11)), live_bench)
    report("B2 핵심 190P", deck(("DYN", "초월", 2), ("SIG", "초월", 1), ("GG", "초월", 2), ("FRA", "9각", 2),
                              ("AS", "9각", 6), ("LIVE", "초월", 8)), live_bench)
    report("B3 핵심 185P", deck(("DYN", "초월", 3), ("SIG", "초월", 2), ("GG", "초월", 3), ("FRA", "9각", 2),
                              ("AS", "9각", 5), ("LIVE", "초월", 6)), live_bench)
    report("B4 1차 150P", deck(("DYN", "초월", 5), ("SIG", "초월", 3), ("GG", "초월", 6), ("TH", "9각", 3),
                              ("AS", "6각", 2), ("LIVE", "6각", 2)), deck(("LIVE", "6각", 6)))
    print("  -- 같은 주전(B2)에서 후보 6인만 바꿨을 때 --")
    starters_b2 = deck(("DYN", "초월", 2), ("SIG", "초월", 1), ("GG", "초월", 2), ("FRA", "9각", 2),
                       ("AS", "9각", 6), ("LIVE", "초월", 8))
    report("B2' 후보 AS 9각", starters_b2, deck(("AS", "9각", 6)))
    report("B2'' 후보 GG 초월", starters_b2, deck(("GG", "초월", 6)))
    print()

    print("C. 목표 구간별 '종결 카드' 기용 상한 (나머지는 AS 9각/LIVE 초월 최대치 가정, 27인 LIVE 초월 = 216P 기준)")
    cost = {"DYN/SIG 초월(5)": 3, "GG 초월(6)": 2, "FRA/TH 9각(6)": 2, "AS 9각(7)": 1}
    for target in (200, 190, 185, 150):
        budget = 216 - target
        parts = ", ".join(f"{k} 최대 {min(27, budget // v)}장" for k, v in cost.items())
        print(f"  {target}P: 감점 여유 {budget}P -> {parts}")
    print()

    print("D. PDF 과금 영역 - 보유 한계까지 프리미엄을 전부 기용(풀성장, 남는 자리 LIVE 초월)했을 때")
    for tier in TIERS:
        d = tier_max_premium(tier)
        total = deck_score(d)
        flat, bat, pit, reached = buffs(total)
        premium = sum(1 for g, _ in d if g != "LIVE")
        print(f"  {tier[0].ljust(6)} 프리미엄 {premium:>2}장 + LIVE {27 - premium:>2}장: {total}P [{milestone(total)}] "
              f"모든 능력치 +{flat}")


if __name__ == "__main__":
    main()
