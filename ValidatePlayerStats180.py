# -*- coding: utf-8 -*-
"""
[TASK-KBO-180] 세부 능력치 편차 전수 검증 - GenerateKBODatabase.py 재생성 후 실행한다.

C# 런타임과 같은 규칙으로 모든 카드의 최종 세부 스탯을 재현해 검사한다:
  - PlayerDatabase.ConvertZScoreToStat(z) = clamp(round(z*15+50), 1, 100)
  - 타자: 파워=z_power, 정확=z_contact, 선구=z_eye, 주력=z_speed, 수비=z_def
    투수: 구위=z_stuff, 구속=z_speed, 변화=z_movement, 제구=z_control, 체력=z_stamina
  - PlayerTemplate.GetBaseOverall() = 타자 (파워+정확+선구)/3, 투수 (구위+구속+변화+제구)/4 (Mathf.RoundToInt)
  - 카드 = 선수 스탯 전 항목 + (base_ovr - 선수 기본 OVR) (PlayerDatabase.ApplyStatShift)
검사 항목:
  1) 5개 세부 스탯이 전부 같은 카드 0장
  2) 카드 최종 OVR(같은 공식) == cards_*.csv base_ovr (전 카드)
  3) (선택) 이전 DB 폴더를 인자로 주면 player_id·card_id·등급·연도가 1개도 바뀌지 않았는지(+ base_ovr 변경 내역 보고)
사용: python ValidatePlayerStats180.py [이전 Data 폴더]
"""
import csv
import glob
import os
import sys

ROOT = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(ROOT, "Assets", "Resources", "Data")


def bankers_round(x):
    """C# Mathf.RoundToInt(= Math.Round, 짝수 반올림)."""
    f = int(x // 1)
    r = x - f
    if abs(r - 0.5) < 1e-9:
        return f if f % 2 == 0 else f + 1
    return int(f + (1 if r > 0.5 else 0))


def to_stat(z):
    return max(1, min(100, bankers_round(float(z) * 15 + 50)))


def load(folder):
    players = {r["player_id"]: r for r in csv.DictReader(open(os.path.join(folder, "players.csv"), encoding="utf-8-sig"))}
    cards = []
    for path in sorted(glob.glob(os.path.join(folder, "cards_*.csv"))):
        cards.extend(csv.DictReader(open(path, encoding="utf-8-sig")))
    return players, cards


def player_stats(row):
    pitcher = row["position"] in ("SP", "RP", "CP")
    if pitcher:
        names = ["구위", "구속", "변화", "제구", "체력"]
        stats = [to_stat(row["z_stuff"]), to_stat(row["z_speed"]), to_stat(row["z_movement"]),
                 to_stat(row["z_control"]), to_stat(row["z_stamina"])]
        ovr = bankers_round(sum(stats[:4]) / 4)
    else:
        names = ["파워", "정확", "선구", "주력", "수비"]
        stats = [to_stat(row["z_power"]), to_stat(row["z_contact"]), to_stat(row["z_eye"]),
                 to_stat(row["z_speed"]), to_stat(row["z_def"])]
        ovr = bankers_round(sum(stats[:3]) / 3)
    return pitcher, names, stats, ovr


def card_stats(players, card):
    pitcher, names, stats, base = player_stats(players[card["player_id"]])
    shift = int(card["base_ovr"]) - base
    final = [max(1, s + shift) for s in stats]
    ovr = bankers_round(sum(final[:4]) / 4) if pitcher else bankers_round(sum(final[:3]) / 3)
    return names, final, ovr


def main():
    players, cards = load(DATA)
    flat_players = [pid for pid, row in players.items() if len(set(player_stats(row)[2])) == 1]
    flat_cards, ovr_mismatch = [], []
    for card in cards:
        names, final, ovr = card_stats(players, card)
        if len(set(final)) == 1:
            flat_cards.append(card["card_id"])
        if ovr != int(card["base_ovr"]):
            ovr_mismatch.append((card["card_id"], ovr, card["base_ovr"]))
    print(f"선수 {len(players)}명 / 카드 {len(cards)}장")
    print(f"[1] 5개 세부 스탯 전부 동일: 선수 {len(flat_players)}명, 카드 {len(flat_cards)}장 {flat_cards[:5]}")
    print(f"[2] 카드 OVR != base_ovr: {len(ovr_mismatch)}장 {ovr_mismatch[:5]}")
    ok = not flat_players and not flat_cards and not ovr_mismatch

    if len(sys.argv) > 1:
        old_players, old_cards = load(sys.argv[1])
        old_map = {c["card_id"]: (c["player_id"], c["grade_name"], c["year"]) for c in old_cards}
        new_map = {c["card_id"]: (c["player_id"], c["grade_name"], c["year"]) for c in cards}
        id_ok = old_map == new_map and set(old_players) == set(players)
        print(f"[3] card_id/player_id/등급/연도 보존: {'OK' if id_ok else 'CHANGED'} (카드 {len(old_map)} -> {len(new_map)})")
        old_ovr = {c["card_id"]: c["base_ovr"] for c in old_cards}
        changed = [(c["card_id"], old_ovr.get(c["card_id"]), c["base_ovr"]) for c in cards if old_ovr.get(c["card_id"]) != c["base_ovr"]]
        print(f"    base_ovr 변경(의도된 밸런스 보정만 허용 - 보고용): {len(changed)}장 {changed[:5]}")
        ok = ok and id_ok

    print("ALL PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
