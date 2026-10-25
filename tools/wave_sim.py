#!/usr/bin/env python3
"""
wave_sim.py — simulacia vln prototypu Resort Empire.

Pocita presne model z build dokumentu §4.4:
  - rovnomerny prichod demandu pocas wave_timer, potom dobeh do prazdnych radov
  - budova ma `capacity` miest; obsluha trva service_time / utilization
  - cakanie = cas od prichodu po usadenie (cas obsluhy sa nepocita)
  - prispevok jednotky: 100 % do patience_grace, linearne na unit_floor
    do patience_floor_after, potom unit_floor
  - Wave Satisfaction = max(wave_floor, vazeny priemer prispevkov)
  - mini-event Sudden rush na neobsadenu budovu (§4.6)

Simulacia zamerne nemodeluje presuny pocas vlny ani Energy (pasivny hrac).

Kontroluje hard pravidla:
  WR1  unit_floor < wave_floor < hranica 3★
  WR2  utilization_empty > 0
  WR7  od Wave 3 (okrem debutovych vln) najlepsie staticke rozmiestnenie
       na Lv1 bez presunov, upgradov a Energy NESMIE dosiahnut 5★
  WR8  plne pripraveny rezort (vsetky budovy na max leveli) MUSI dosiahnut
       5★ v kazdej vlne bez Energy

Parametre cita z toho isteho suboru ako hra (SCRUM-90):
  Assets/Resources/Config/wave_config.json

Pouzitie (z korena repa):
  python3 tools/wave_sim.py                       # cita wave_config.json hry
  python3 tools/wave_sim.py cesta/wave_config.json  # napr. override z playtestu

Exit code 0 = vsetky pravidla OK, 1 = porusenie (vhodne do build pipeline).
"""

import heapq
import json
import sys
from itertools import combinations
from pathlib import Path


# ---------------------------------------------------------------- config

DEFAULT_CONFIG = Path(__file__).resolve().parent.parent / "Assets" / "Resources" / "Config" / "wave_config.json"

# Pevne veci z build dokumentu (spec, nie tuning)
BUILDINGS = {               # budova -> need (§4.5)
    "Reception": "Rooms",
    "Restaurant": "Food",
    "BeachBar": "Drinks",
    "Pool": "Relax",
    "Bungalows": "OceanView",
}
WAVE_KEYS = {"rooms": "Rooms", "drinks": "Drinks", "food": "Food",
             "relax": "Relax", "ocean_view": "OceanView"}
WR7_FROM_WAVE = 3           # WR7 plati od Wave 3 (§4.8)
RATING_NAMES = ["1", "2", "3", "4", "5", "Perfect"]


def load_config(path):
    """Nacita wave_config.json hry a prevedie ho na tvar, s ktorym pocita simulacia."""
    raw = json.loads(Path(path).read_text(encoding="utf-8"))
    sat = raw["satisfaction"]
    rush = raw["sudden_rush"]
    unlocks = raw["unlocks"]

    waves = []
    for w in raw["waves"]:
        row = {"wave": w["wave"]}
        for key, need in WAVE_KEYS.items():
            if w.get(key, 0) > 0:
                row[need] = w[key]
        waves.append(row)

    weights = {need: 1.0 for need in BUILDINGS.values()}
    weights["OceanView"] = sat["ocean_view_weight"]

    return {
        "wave_timer": raw["wave"]["wave_timer"],
        "service_time": raw["service"]["service_time"],
        "utilization_staffed": raw["service"]["utilization_worker"],
        "utilization_empty": raw["service"]["utilization_empty"],
        "capacity_by_level": raw["buildings"]["capacity_by_level"],
        "max_level_prototype": len(raw["buildings"]["capacity_by_level"]),
        "patience_grace": sat["patience_grace_s"],
        "patience_floor_after": sat["patience_floor_after_s"],
        "unit_floor": sat["unit_floor"],
        "wave_floor": sat["wave_floor"],
        "rating_thresholds": dict(zip(RATING_NAMES, sat["rating_thresholds"])),
        "need_weights": weights,
        "buildings": BUILDINGS,
        "rush": {
            "wave": rush["wave"],
            "time_s": rush["time_s"],
            "units": rush["extra_units"],
            "duration_s": rush["duration_s"],
        },
        # debutove vlny = prva vlna po unlocku (§4.8, pravidlo 2)
        "debut_waves": [unlocks["pool_after_wave"] + 1, unlocks["bungalows_after_wave"] + 1],
        "wr7_from_wave": WR7_FROM_WAVE,
        "waves": waves,
    }


# ---------------------------------------------------------------- model

def unit_contribution(wait, cfg):
    """Prispevok jednej jednotky needu podla cakania (prichod -> usadenie)."""
    grace = cfg["patience_grace"]
    floor_after = cfg["patience_floor_after"]
    floor = cfg["unit_floor"]
    if wait <= grace:
        return 1.0
    if wait >= floor_after:
        return floor
    frac = (wait - grace) / (floor_after - grace)
    return 1.0 - frac * (1.0 - floor)


def arrival_times(demand, cfg, rush_units=0):
    """Rovnomerny prichod + pripadny naval (Sudden rush)."""
    timer = cfg["wave_timer"]
    times = [i * timer / demand for i in range(demand)] if demand > 0 else []
    if rush_units:
        r = cfg["rush"]
        step = r["duration_s"] / r["units"]
        times += [r["time_s"] + j * step for j in range(rush_units)]
    return sorted(times)


def simulate_building(arrivals, capacity, utilization, cfg):
    """
    Model miest pri pulte. Rychlost obsluhy je pocas vlny konstantna
    (ziadne presuny, ziadna Energy), takze kazda obsluha trva presne
    service_time / utilization. Vracia (zoznam cakani, cas posledneho konca).
    """
    duration = cfg["service_time"] / utilization
    seats = [0.0] * capacity  # cas, kedy sa miesto uvolni
    heapq.heapify(seats)
    waits = []
    last_end = 0.0
    for t in arrivals:
        free_at = heapq.heappop(seats)
        seated = max(t, free_at)
        end = seated + duration
        waits.append(seated - t)
        heapq.heappush(seats, end)
        last_end = max(last_end, end)
    return waits, last_end


def rating_for(sat, cfg):
    th = cfg["rating_thresholds"]
    eps = 1e-9
    if sat >= th["Perfect"] - eps:
        return "Perfect"
    for stars in ("5", "4", "3", "2", "1"):
        if sat >= th[stars] - eps:
            return stars + "★"
    return "1★"  # wave_floor garantuje aspon 1★


def is_five_star(rating):
    return rating in ("5★", "Perfect")


def simulate_wave(wave, staffed, level, cfg):
    """
    wave    : riadok z tabulky vln
    staffed : set budov s Worker-om
    level   : level vsetkych budov (1..max)
    """
    capacity = cfg["capacity_by_level"][level - 1]
    weights = cfg["need_weights"]
    rush = cfg["rush"]

    active = active_buildings(wave, cfg)
    empty = [b for b in active if b not in staffed]

    num = 0.0
    den = 0.0
    last_end = 0.0
    per_need = {}
    for b in active:
        need = cfg["buildings"][b]
        demand = wave[need]
        rush_units = rush["units"] if (wave["wave"] == rush["wave"] and b in empty) else 0
        arr = arrival_times(demand, cfg, rush_units)
        util = cfg["utilization_staffed"] if b in staffed else cfg["utilization_empty"]
        waits, end = simulate_building(arr, capacity, util, cfg)
        contribs = [unit_contribution(w, cfg) for w in waits]
        w = weights[need]
        num += sum(contribs) * w
        den += len(contribs) * w
        last_end = max(last_end, end)
        per_need[need] = sum(contribs) / len(contribs) if contribs else 1.0

    raw = num / den if den else 1.0
    sat = max(cfg["wave_floor"], raw)
    return {
        "satisfaction": sat,
        "rating": rating_for(sat, cfg),
        "drain_s": max(0.0, last_end - cfg["wave_timer"]),
        "empty": empty,
        "per_need": per_need,
    }


def active_buildings(wave, cfg):
    """Budovy, ktorych need je v tabulke vlny (WR9: nekupena budova sa nespawnuje)."""
    return [b for b, need in cfg["buildings"].items() if wave.get(need, 0) > 0]


def best_static(wave, level, cfg):
    """Najlepsie staticke rozmiestnenie: Worker-ov = budov - 1, vyskusa vsetky."""
    active = active_buildings(wave, cfg)
    workers = len(active) - 1
    best = None
    for combo in combinations(active, workers):
        res = simulate_wave(wave, set(combo), level, cfg)
        if best is None or res["satisfaction"] > best["satisfaction"]:
            best = res
    return best


# ---------------------------------------------------------------- checks

def check_static_rules(cfg):
    out = []
    th3 = cfg["rating_thresholds"]["3"]
    uf, wf = cfg["unit_floor"], cfg["wave_floor"]
    ok = uf < wf < th3
    out.append(("WR1", ok, f"unit_floor {uf:.0%} < wave_floor {wf:.0%} < 3★ {th3:.0%}"))
    ue = cfg["utilization_empty"]
    out.append(("WR2", ue > 0, f"utilization_empty = {ue}"))
    return out


def main():
    cfg_path = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_CONFIG
    try:
        cfg = load_config(cfg_path)
    except KeyError as missing:
        print(f"wave_sim.py — config: {cfg_path}")
        print(f"V configu chýba kľúč {missing} — simulácia sa nedá spustiť.")
        sys.exit(1)
    max_lv = cfg["max_level_prototype"]

    if cfg["service_time"] <= 0:
        print(f"wave_sim.py — config: {cfg_path}")
        print("service.service_time je 0 (placeholder) — simulacia sa neda spustit.")
        sys.exit(1)

    print(f"wave_sim.py — config: {cfg_path}")
    print(f"service_time {cfg['service_time']} s · utilization_empty {cfg['utilization_empty']} · "
          f"wave_timer {cfg['wave_timer']} s · capacity {cfg['capacity_by_level']}\n")

    violations = []

    # --- WR1, WR2
    print("Pravidla parametrov")
    for rid, ok, msg in check_static_rules(cfg):
        print(f"  {rid}  {'OK ' if ok else 'PORUŠENÉ'}  {msg}")
        if not ok:
            violations.append(f"{rid}: {msg}")
    print()

    # --- tabulka vln
    hdr = (f"{'Vlna':<5}{'Pasívne (Lv1)':<15}{'Sat.':>6}{'Dobeh':>8}  "
           f"{'Prázdna budova':<16}{'Plne prip. (Lv' + str(max_lv) + ')':<18}{'Sat.':>6}  {'WR7':<10}{'WR8':<8}")
    print(hdr)
    print("-" * len(hdr))

    for wave in cfg["waves"]:
        n = wave["wave"]
        passive = best_static(wave, 1, cfg)
        full = best_static(wave, max_lv, cfg)

        # WR7
        if n < cfg["wr7_from_wave"]:
            wr7 = "—"
        elif n in cfg["debut_waves"]:
            wr7 = "debut"
        elif is_five_star(passive["rating"]):
            wr7 = "PORUŠENÉ"
            violations.append(f"WR7: vlna {n} — pasívna hra dá {passive['rating']} "
                              f"({passive['satisfaction']:.0%})")
        else:
            wr7 = "OK"

        # WR8
        if is_five_star(full["rating"]):
            wr8 = "OK"
        else:
            wr8 = "PORUŠENÉ"
            violations.append(f"WR8: vlna {n} — plne pripravený rezort dá len {full['rating']} "
                              f"({full['satisfaction']:.0%})")

        rush_mark = " +rush" if n == cfg["rush"]["wave"] else ""
        print(f"{n:<5}{passive['rating'] + rush_mark:<15}{passive['satisfaction']:>6.0%}"
              f"{passive['drain_s']:>7.0f}s  {', '.join(passive['empty']):<16}"
              f"{full['rating']:<18}{full['satisfaction']:>6.0%}  {wr7:<10}{wr8:<8}")

    print()
    if violations:
        print(f"VÝSLEDOK: {len(violations)} porušení")
        for v in violations:
            print(f"  ✗ {v}")
        sys.exit(1)
    print("VÝSLEDOK: všetky pravidlá OK (WR1, WR2, WR7, WR8)")
    sys.exit(0)


if __name__ == "__main__":
    main()
