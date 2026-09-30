# -*- coding: utf-8 -*-
"""
[TASK-KBO-172] 과금 영역별 27인 세트덱 스코어 시뮬레이터.

docs/기획 고도화 자료.pdf의 "과금 영역별 27인 구성 한계"와 명령서 STEP 2의 확정 개인 스코어 규격
(Assets/Scripts/Models/CardGrowthRules.cs와 1:1 동일)을 합산해, 각 과금 영역이 PDF 버프 구간
(최소 목표 150P / 핵심 185P·190P / 최종 200P)에 어떻게 맞물리는지 검증한다. 결과표는
docs/04_card_grade_policy.md 8절에 그대로 옮겼다. 실행: python SimulateSetDeckTiers.py

[해석 규칙 - PDF 원문이 모호한 부분]
1) 등급 인원은 "그 등급 이상" 누적 상한으로 해석한다(ALL_STAR 26 = AS 이상 26명, TITLE_HOLDER 20 =
   TH 이상 20명 ...). 합이 27을 넘는 상위 영역(중과금 이상)은 누적 해석 말고는 성립하지 않기 때문이다.
   서열: ALL_STAR < TITLE_HOLDER(=FRANCHISE 버킷) < SIGNATURE < GOLDEN_GLOVE < DYNASTY.
2) DYNASTY 성장 단계 인원도 "그 단계 이상" 누적으로 해석한다(초핵 "4/6각 6/3각 10/명함 10" = 풀성장 4,
   6각 이상 6, 3각 이상 10, 명함 이상 10 -> 총 10명). 더해서 30명이 되는 가산 해석은 27인을 넘는다.
3) 언급 없는 등급 카드는 PDF 규칙대로 "풀성장"(9각 한계 등급은 9각, 초월 가능 등급은 초월).
4) 남는 슬롯은 LIVE(LIVE_NORMAL/LIVE_EPIC)로 채우며, LIVE 육성 단계는 시나리오 변수다.
"""

# CardGrowthRules.cs와 동일
BASE = {"LIVE": 4, "AS": 3, "FRA": 4, "TH": 4, "GG": 4, "SIG": 5, "DYN": 5}
TRANSCEND = {"LIVE", "GG", "SIG", "DYN"}
STAGE_LEVEL = {"명함": 0, "3각": 3, "6각": 6, "9각": 9, "초월": 10}


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


# (영역, 월 과금, AS이상, TH이상, SIG이상, GG이상, DYN [(단계, 그 단계 이상 누적 인원)...])
TIERS = [
    ("무과금", "0원", 10, 6, 4, 0, []),
    ("무소과금", "3,300원~10만 원", 15, 10, 9, 1, []),
    ("소과금", "10만~50만 원", 20, 15, 12, 4, []),
    ("중과금", "50만~100만 원", 26, 20, 15, 8, [("3각", 2), ("명함", 1)]),
    ("핵과금", "수백만~1천만 원", 26, 26, 20, 12, [("6각", 1), ("3각", 2), ("명함", 3)]),
    ("초핵과금", "수천만~억 원", 26, 26, 26, 20, [("초월", 4), ("6각", 6), ("3각", 10), ("명함", 10)]),
]


def dynasty_cards(spec):
    """누적 상한 -> 단계별 실제 인원. 예: [(초월,4),(6각,6),(3각,10),(명함,10)] -> 초월4, 6각2, 3각4."""
    cards, taken = [], 0
    for stage, at_least in spec:
        n = max(0, at_least - taken)
        cards += [stage] * n
        taken += n
    return cards


def build_deck(tier, live_stage):
    name, _, as_up, th_up, sig_up, gg_up, dyn_spec = tier
    dyn = dynasty_cards(dyn_spec)
    deck = [("DYN", st) for st in dyn]
    deck += [("GG", full("GG"))] * (gg_up - len(dyn))
    deck += [("SIG", full("SIG"))] * (sig_up - gg_up)
    deck += [("TH", full("TH"))] * (th_up - sig_up)
    deck += [("AS", full("AS"))] * (as_up - th_up)
    deck += [("LIVE", live_stage)] * (27 - as_up)
    assert len(deck) == 27, (name, len(deck))
    return deck


def deck_score(deck):
    return sum(score(g, st) for g, st in deck)


def milestone(s):
    if s >= 200:
        return "최종 200P"
    if s >= 190:
        return "핵심 190P"
    if s >= 185:
        return "핵심 185P"
    if s >= 150:
        return "최소 150P"
    return "미달(<150P)"


def required_live_avg(tier, target):
    deck = build_deck(tier, "명함")
    premium = deck_score([c for c in deck if c[0] != "LIVE"])
    live_slots = sum(1 for c in deck if c[0] == "LIVE")
    return (target - premium) / live_slots, premium, live_slots


def main():
    print("[개인 스코어 규격] " + ", ".join(f"{g} {BASE[g]}->{score(g, full(g))}" for g in BASE))
    print()
    print("A. LIVE 육성 단계별 27인 합산 스코어 (프리미엄 등급은 PDF대로 풀성장)")
    header = "영역".ljust(8) + "".join(f"LIVE {st}".rjust(12) for st in ("명함", "3각", "6각", "9각", "초월"))
    print(header)
    for tier in TIERS:
        row = tier[0].ljust(8)
        for st in ("명함", "3각", "6각", "9각", "초월"):
            s = deck_score(build_deck(tier, st))
            row += f"{s:>6}({milestone(s)[:2]})".rjust(12)
        print(row)
    print()
    print("B. 목표 구간 도달에 필요한 LIVE 슬롯 평균 개인 스코어 (LIVE 명함 4 ~ 초월 8)")
    for tier in TIERS:
        parts = []
        for target in (150, 185, 190, 200):
            need, premium, slots = required_live_avg(tier, target)
            flag = "자동" if need <= 4 else ("불가" if need > 8 else f"{need:.2f}")
            parts.append(f"{target}P:{flag}")
        _, premium, slots = required_live_avg(tier, 150)
        print(f"{tier[0].ljust(8)} 프리미엄 {27 - slots:>2}장 합 {premium:>3}P + LIVE {slots:>2}슬롯 -> " + ", ".join(parts))
    print()
    print("C. 동일 영역 내부 차등 예시 - 소과금 10만 원 vs 49만 원(PDF 원문 예시)")
    # 49만 원 = 10만 원 덱 + 추가 골든글러브 4장(6각 2 + 명함 2)이 OVR이 가장 낮은 ALL_STAR 4장을 대체.
    low = build_deck(TIERS[2], "6각")
    high = list(low)
    for replacement in [("GG", "6각"), ("GG", "6각"), ("GG", "명함"), ("GG", "명함")]:
        high[high.index(("AS", "9각"))] = replacement
    assert len(high) == 27
    print(f"  10만 원(골글 풀성장 4, LIVE 6각): {deck_score(low)}P / "
          f"49만 원(+골글 6각 2·명함 2가 AS 9각 4장 대체): {deck_score(high)}P "
          f"-> 스코어는 {deck_score(high) - deck_score(low):+d}P, 차이는 OVR(골글 > 올스타)로 난다")


if __name__ == "__main__":
    main()
