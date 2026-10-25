#!/usr/bin/env python3
"""
playtest_report.py — vyhodnotenie metrik playtestu (SCRUM-93, build dokument §4.12).

Cita .jsonl subory, ktore hra zapisuje do Application.persistentDataPath/playtest/
(Assets/Scripts/Diagnostics/PlaytestMetrics.cs), a spocita:

  wave_complete_rate             dokoncene vlny / zacate vlny
  worker_move_before_prompt_rate podiel session, kde hrac presunul Worker-a skor,
                                 nez sa prvy raz ukazal bottleneck warning
  worker_moves_pre_wave          priemerny pocet presunov pred startom vlny (podla vlny)
  worker_moves_in_wave           priemerny pocet presunov pocas vlny (podla vlny)
  drain_duration_s               dlzka dobehu po wave_timer (podla vlny)
  rating_distribution            rozlozenie ratingov, zvlast pre vlny bez presunov pocas vlny
  upgrade_take_rate              kupene upgrady / ponuknute upgrady
  session_length                 dlzka session v minutach

Energy usage sa zamerne nemeria (§4.12).

Pouzitie:
  python3 tools/playtest_report.py priecinok_so_subormi/
  python3 tools/playtest_report.py subor1.jsonl subor2.jsonl
  python3 tools/playtest_report.py data/ --json      # strojovo citatelny vystup
"""

import json
import statistics
import sys
from collections import Counter, defaultdict
from pathlib import Path

RATING_ORDER = ["1★", "2★", "3★", "4★", "5★", "Perfect"]


def load_events(paths):
    files = []
    for p in map(Path, paths):
        files += sorted(p.rglob("*.jsonl")) if p.is_dir() else [p]
    events, bad = [], 0
    for f in files:
        for line in f.read_text(encoding="utf-8").splitlines():
            if not line.strip():
                continue
            try:
                events.append(json.loads(line))
            except json.JSONDecodeError:
                bad += 1   # napr. posledny riadok useknuty pri pade appky
    return files, events, bad


def ratio(a, b):
    return a / b if b else None


def summarize(values):
    if not values:
        return None
    return {"n": len(values), "mean": statistics.fmean(values), "median": statistics.median(values),
            "min": min(values), "max": max(values)}


def compute(events):
    sessions = defaultdict(list)
    for e in events:
        sessions[e.get("session_id")].append(e)
    for evs in sessions.values():
        evs.sort(key=lambda e: e.get("t", 0))

    started = completed = 0
    moves_pre = defaultdict(list)      # wave -> [pocet za session-vlnu]
    moves_in = defaultdict(list)
    drain = defaultdict(list)
    ratings_all = Counter()
    ratings_no_moves = Counter()
    ratings_with_moves = Counter()
    offered = bought = 0
    lengths_min = []
    anticipated = eligible = 0

    for evs in sessions.values():
        by_wave_pre, by_wave_in = Counter(), Counter()
        first_move_t = first_warning_t = None
        waves_started = set()

        for e in evs:
            kind, wave, t = e.get("event"), e.get("wave"), e.get("t", 0)
            if kind == "wave_started":
                started += 1
                waves_started.add(wave)
            elif kind == "worker_moved":
                (by_wave_in if e.get("phase") == "in_wave" else by_wave_pre)[wave] += 1
                if first_move_t is None:
                    first_move_t = t
            elif kind == "bottleneck_shown":
                if first_warning_t is None:
                    first_warning_t = t
            elif kind == "wave_completed":
                completed += 1
                if e.get("drain_s") is not None:
                    drain[wave].append(e["drain_s"])
                r = e.get("rating")
                ratings_all[r] += 1
                (ratings_with_moves if by_wave_in[wave] else ratings_no_moves)[r] += 1
            elif kind == "upgrade_offered":
                offered += 1
            elif kind == "upgrade_bought":
                bought += 1

        for w in waves_started:
            moves_pre[w].append(by_wave_pre[w])
            moves_in[w].append(by_wave_in[w])

        if first_move_t is not None or first_warning_t is not None:
            eligible += 1
            if first_move_t is not None and (first_warning_t is None or first_move_t < first_warning_t):
                anticipated += 1

        lengths_min.append(max((e.get("t", 0) for e in evs), default=0) / 60)

    def per_wave(d):
        return {w: statistics.fmean(v) for w, v in sorted(d.items()) if v}

    return {
        "sessions": len(sessions),
        "wave_complete_rate": ratio(completed, started),
        "waves_started": started,
        "waves_completed": completed,
        "worker_move_before_prompt_rate": ratio(anticipated, eligible),
        "worker_moves_pre_wave": per_wave(moves_pre),
        "worker_moves_in_wave": per_wave(moves_in),
        "drain_duration_s": {w: summarize(v) for w, v in sorted(drain.items())},
        "rating_distribution": {
            "all": dict(ratings_all),
            "without_in_wave_moves": dict(ratings_no_moves),
            "with_in_wave_moves": dict(ratings_with_moves),
        },
        "upgrade_take_rate": ratio(bought, offered),
        "upgrades_offered": offered,
        "upgrades_bought": bought,
        "session_length_min": summarize(lengths_min),
    }


def pct(x):
    return "—" if x is None else f"{x:.0%}"


def print_report(r, files, bad):
    print(f"Playtest report — {r['sessions']} session(s) zo {len(files)} súborov"
          + (f", {bad} poškodených riadkov preskočených" if bad else ""))
    print()
    print(f"wave_complete_rate              {pct(r['wave_complete_rate'])}  "
          f"({r['waves_completed']}/{r['waves_started']} vĺn)")
    print(f"worker_move_before_prompt_rate  {pct(r['worker_move_before_prompt_rate'])}  "
          f"(session, kde hráč presunul Worker-a pred prvým warningom)")
    print(f"upgrade_take_rate               {pct(r['upgrade_take_rate'])}  "
          f"({r['upgrades_bought']}/{r['upgrades_offered']} ponúk)")
    sl = r["session_length_min"]
    if sl:
        print(f"session_length                  medián {sl['median']:.1f} min "
              f"(min {sl['min']:.1f}, max {sl['max']:.1f}, n={sl['n']})")
    print()

    waves = sorted(set(r["worker_moves_pre_wave"]) | set(r["drain_duration_s"]))
    if waves:
        print(f"{'Vlna':<6}{'presuny pred':>14}{'presuny počas':>15}{'dobeh medián':>15}")
        print("-" * 50)
        for w in waves:
            pre = r["worker_moves_pre_wave"].get(w)
            inw = r["worker_moves_in_wave"].get(w)
            d = r["drain_duration_s"].get(w)
            pre_s = "—" if pre is None else f"{pre:.1f}"
            in_s = "—" if inw is None else f"{inw:.1f}"
            drain_s = "—" if not d else f"{d['median']:.0f} s"
            print(f"{w:<6}{pre_s:>14}{in_s:>15}{drain_s:>15}")
        print()

    rd = r["rating_distribution"]
    print(f"{'Rating':<10}{'všetky':>8}{'bez presunov':>14}{'s presunmi':>12}")
    print("-" * 44)
    for name in RATING_ORDER:
        print(f"{name:<10}{rd['all'].get(name, 0):>8}{rd['without_in_wave_moves'].get(name, 0):>14}"
              f"{rd['with_in_wave_moves'].get(name, 0):>12}")



def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if not args:
        print(__doc__)
        sys.exit(2)
    files, events, bad = load_events(args)
    if not events:
        print("Nenašiel som žiadne udalosti (.jsonl).")
        sys.exit(1)
    report = compute(events)
    if "--json" in sys.argv:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print_report(report, files, bad)


if __name__ == "__main__":
    main()
