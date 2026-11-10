# tools/

## wave_sim.py

Simulácia vĺn prototypu (build dokument §4.4). Overuje hard pravidlá WR1, WR2, WR7, WR8.

Parametre číta z **toho istého súboru ako hra**: `Assets/Resources/Config/wave_config.json` (SCRUM-90).
Iný config súbor neexistuje, takže simulácia a hra sa nemôžu rozísť.

```
python3 tools/wave_sim.py                          # z koreňa repa
python3 tools/wave_sim.py cesta/wave_config.json   # napr. override súbor z playtestu
```

- Exit code `0` = všetko OK, `1` = porušenie (dá sa zapojiť do build pipeline, SCRUM-98).
- Stĺpec **Pasívne (Lv1)**: najlepšie statické rozmiestnenie Worker-ov, žiadne presuny, upgrady ani Energy.
- Stĺpec **Plne prip. (Lv3)**: všetky budovy na max leveli, bez Energy.

**Pravidlo:** kto zmení čísla v `wave_config.json`, pustí `wave_sim.py` pred buildom.
