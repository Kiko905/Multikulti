# Metriky playtestu (SCRUM-93)

Metriky z build dokumentu §4.12. Hra ich zapisuje **lokálne na zariadení** ako súbor `.jsonl` (jedna udalosť = jeden riadok). Nič sa neposiela na internet, neukladajú sa mená ani IP adresy, `session_id` je náhodné ID jedného spustenia hry. Energy usage sa zámerne nemeria.

Kód: `Assets/Scripts/Diagnostics/PlaytestMetrics.cs`. Vyhodnotenie: `tools/playtest_report.py`.

## Metriky

| Metrika | Ako sa počíta |
|---|---|
| `wave_complete_rate` | dokončené vlny / začaté vlny |
| `worker_move_before_prompt_rate` | podiel session, v ktorých hráč presunul Worker-a skôr, než sa prvýkrát ukázal bottleneck warning (prvé exit kritérium §4.12) |
| `worker_moves_pre_wave` | priemerný počet presunov v `AwaitingWave` pred danou vlnou |
| `worker_moves_in_wave` | priemerný počet presunov počas danej vlny |
| `drain_duration_s` | dĺžka dobehu po `wave_timer`, kým sa nevyprázdnia rady |
| `rating_distribution` | rozloženie ratingov; zvlášť pre vlny **bez** a **s** presunmi počas vlny |
| `upgrade_take_rate` | kúpené upgrady / ponúknuté upgrady |
| `session_length` | čas od spustenia po poslednú udalosť session |

Subjektívne „chcem ešte jednu vlnu“ sa nemeria v hre, patrí do dotazníka po playteste.

## Kde v kóde volať (pre programátora wave loopu)

| Kedy | Volanie |
|---|---|
| hráč ťukne Start Wave | `PlaytestMetrics.WaveStarted(wave)` |
| presun Worker-a (pred vlnou aj počas nej) | `PlaytestMetrics.WorkerMoved(wave, inWave, from, to, targetNeedState)` |
| need prejde do `Bottleneck` a ukáže sa warning | `PlaytestMetrics.BottleneckShown(wave, need)` |
| zobrazí sa Wave Complete | `PlaytestMetrics.WaveCompleted(wave, rating, satisfaction, drainSeconds, durationSeconds)` |
| obrazovka Upgrade ponúkne upgrade | `PlaytestMetrics.UpgradeOffered(wave, building)` |
| hráč kúpi upgrade | `PlaytestMetrics.UpgradeBought(wave, building, newLevel)` |
| hráč kúpi Area Unlock | `PlaytestMetrics.AreaUnlockBought(wave, building)` |

Názvy budov, needov a stavov podľa `Docs/Slovnik.md` (`BeachBar`, `Drinks`, …; stav needu ako v Jire: `OK`, `BOTTLENECK`, `RECOVERING`). Rating ako `"1★"` … `"5★"` alebo `"Perfect"`.

Začiatok session, pauza aplikácie a koniec sa zapisujú samé.

## Zber dát po playteste

Súbory sú v priečinku `playtest` v `Application.persistentDataPath`:

- **Editor (Mac):** Multikulti → Playtest → Open metrics folder
- **Android:** `adb pull /sdcard/Android/data/com.multikulti.multikulti/files/playtest`
- **iOS:** Xcode → Window → Devices and Simulators → aplikácia Multikulti → Download Container → `AppData/Documents/playtest`

Súbory zo všetkých zariadení daj do jedného priečinka (do repa ich **necommituj**) a spusti:

```
python3 tools/playtest_report.py cesta/k/priecinku
python3 tools/playtest_report.py cesta/k/priecinku --json   # na ďalšie spracovanie
```

## Overenie bez hotovej hry

V Play mode: **Multikulti → Playtest → Write sample wave** zapíše jednu vymyslenú vlnu. Potom **Open metrics folder** a nad priečinkom spusti `tools/playtest_report.py`.

Spoločné `session_id` s crash reportingom (SCRUM-100): crash v Sentry má tag `session_id`, podľa ktorého nájdeš súbor metrík tej istej session.
