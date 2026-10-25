# Slovník projektu Multikulti

**Stav: NÁVRH (SCRUM-91).** Pojmy sú prevzaté z Jira úloh a z `Assets/Resources/Config/wave_config.json`. Zoltán ich skontroluje a doplní.

## Pravidlá
- Názvy v kóde a v UI používajú len pojmy z tohto slovníka.
- C#: typy, metódy, vlastnosti a hodnoty enumov v PascalCase (`WavePaused`), lokálne premenné a parametre v camelCase (`waveTimer`). Pravidlá sú v `.editorconfig`.
- Kľúče v konfiguračných JSON súboroch sú v snake_case (`wave_timer`), rovnako ako v `Assets/Resources/Config/wave_config.json`.
- Stavy zapísané v Jire veľkými písmenami (`WAVE_PAUSED`) sa v C# píšu ako hodnota enumu v PascalCase (`WavePaused`).
- Texty v UI sú v angličtine a idú cez Localization tabuľku `UI`.

## Zakázané slová
| Slovo | Prečo | Čo použiť namiesto neho |
| --- | --- | --- |
| idle | zadanie SCRUM-91: nepoužíva sa ani ako názov stavu | *doplní Zoltán* (napr. budova bez Workera = „empty“, čakanie na vlnu = `AwaitingWave`) |

Kontrolu robí `Assets/Editor/Naming/BannedTermsCheck.cs`: pred každým buildom a z menu **Multikulti → Check Banned Terms**.

## Stavy vlny (SCRUM-26, SCRUM-27)
| Jira | C# enum `WaveState` | UI (EN) | Význam |
| --- | --- | --- | --- |
| AWAITING_WAVE | `AwaitingWave` | — | medzi vlnami, hráč pripravuje rezort |
| WAVE_INCOMING | `WaveIncoming` | — | príchod lode s hosťami |
| WAVE_ACTIVE | `WaveActive` | — | príchod a obsluha hostí |
| WAVE_RESOLVING | `WaveResolving` | — | dobeh: už neprichádzajú noví hostia |
| WAVE_COMPLETE | `WaveComplete` | Wave Complete | vyhodnotenie a odmeny |
| UPGRADE | `Upgrade` | Upgrade | návrh a nákup upgradu |
| WAVE_PAUSED | `WavePaused` | — | aplikácia na pozadí, timery zmrazené |

## Stavy needu (SCRUM-32, SCRUM-34)
| Jira | C# enum `NeedState` | UI (EN) | Význam |
| --- | --- | --- | --- |
| OK | `Ok` | — | rad je v norme |
| BOTTLENECK | `Bottleneck` | warning | rad presiahol `bottleneck_threshold` |
| RECOVERING | `Recovering` | „… is catching up“ | rad sa zmenšuje |
| SATISFIED | `Satisfied` | — | všetky jednotky needu obslúžené |
| LOCKED | `Locked` | — | budova ešte nie je kúpená |

## Stavy VIP requestu (SCRUM-62)
| Jira | C# enum `RequestState` | Význam |
| --- | --- | --- |
| MISSING | `Missing` | chýbajú Materials |
| READY | `Ready` | dá sa spustiť |
| IN_PROGRESS | `InProgress` | beží |
| COMPLETED | `Completed` | splnený |

## Budovy a needy
| Budova | C# | Need | C# |
| --- | --- | --- | --- |
| Reception | `Reception` | Rooms | `Rooms` |
| Restaurant | `Restaurant` | Food | `Food` |
| Beach Bar | `BeachBar` | Drinks | `Drinks` |
| Pool | `Pool` | Relax | `Relax` |
| Bungalows | `Bungalows` | Ocean View | `OceanView` |

## Pojmy
| Pojem | V kóde | Význam | Zdroj |
| --- | --- | --- | --- |
| wave | `Wave` | vlna hostí | SCRUM-12 |
| need | `Need` | potreba hostí, ktorú obsluhuje budova | SCRUM-13 |
| demand | `Demand` | počet jednotiek needu vo vlne | SCRUM-24 |
| unit | `Unit` | jedna jednotka needu (jeden hosť s potrebou) | SCRUM-42 |
| backlog | `Backlog` | jednotky, ktoré ešte čakajú na obsluhu | SCRUM-25 |
| Worker | `Worker` | pracovník, ktorý zrýchľuje budovu | SCRUM-14 |
| capacity | `Capacity` | počet miest obsluhy v budove | SCRUM-30 |
| utilization | `Utilization` | rýchlosť budovy (1,0 s Workerom) | SCRUM-31, SCRUM-38 |
| service time | `ServiceTime` | čas obsluhy jednej jednotky | SCRUM-31 |
| Wave Satisfaction | `WaveSatisfaction` | spokojnosť vlny 0–100 % | SCRUM-43 |
| rating | `Rating` | 1★–5★ alebo Perfect | SCRUM-44 |
| Coins | `Coins` | mena za vlny | SCRUM-45 |
| Energy | `Energy` | mena na boosty | SCRUM-57 |
| Resort Value | `ResortValue` | hodnota rezortu, len rastie | SCRUM-56 |
| Reputation | `Reputation` | skóre za VIP, nikdy neklesá | SCRUM-66 |
| Materials | `Materials` | mena za VIP a vlny 4★+ | SCRUM-69 |
| Gems | `Gems` | platená mena | SCRUM-69 |
| Speed Up | `SpeedUp` | Energy boost obsluhy | SCRUM-57 |
| Sudden rush | `SuddenRush` | mini-event vo vlne 2 | SCRUM-60 |
| Area Unlock | `AreaUnlock` | odomknutie Pool/Bungalows | SCRUM-51 |
| VIP | `Vip` | špeciálny hosť s requestami | SCRUM-61 |
| Guest Request Board | `GuestRequestBoard` | požiadavky VIP hosťa | SCRUM-62 |

## Zjednotené
- Utilization budovy s Workerom: `utilization_worker` (od SCRUM-96 existuje len jeden config, `tools/config.json` bol zmazaný).
