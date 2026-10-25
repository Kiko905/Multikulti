# tools/

## wave_sim.py

Simulácia vĺn prototypu (build dokument §4.4). Overuje hard pravidlá WR1, WR2, WR7, WR8.

```
python3 tools/wave_sim.py                  # číta tools/config.json
python3 tools/wave_sim.py iny_config.json
```

- Exit code `0` = všetko OK, `1` = porušenie (dá sa zapojiť do build pipeline, SCRUM-98).
- Stĺpec **Pasívne (Lv1)**: najlepšie statické rozmiestnenie Worker-ov, žiadne presuny, upgrady ani Energy.
- Stĺpec **Plne prip. (Lv3)**: všetky budovy na max leveli, bez Energy.

## config.json

Všetky laditeľné parametre wave systému + tabuľka vĺn. Hodnoty sú východiskové TUNING čísla z build dokumentu.
Ten istý súbor má čítať aj Unity (SCRUM-90), aby sa simulácia a hra nerozišli.

**Pravidlo:** kto zmení čísla v `config.json`, pustí `wave_sim.py` pred buildom.
