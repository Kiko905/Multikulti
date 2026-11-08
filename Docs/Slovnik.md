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
| worker roster | `WorkerRoster` | ktorý Worker stojí na ktorej budove; Workerov je vždy o 1 menej než budov | SCRUM-39 |
| worker manager | `WorkerManager` | prepája `WorkerRoster` s budovami v scéne | SCRUM-39 |
| slot | — | miesto pre Workera v budove; každá budova má 1 slot | SCRUM-39 |
| has worker | `HasWorker` | na budove stojí Worker | SCRUM-39 |
| empty building | `EmptyBuilding` | jediná budova bez Workera (namiesto zakázaného „idle“) | SCRUM-39 |
| worker moved | `WorkerMoved` | udalosť: Worker sa presunul alebo pribudol | SCRUM-39, SCRUM-36 |
| move | `Move` | presun Workera na prázdnu budovu: zadarmo, okamžite, bez potvrdenia | SCRUM-36 |
| move target | `ChooseTarget` | výber budovy, kam Worker pôjde; dnes vždy jediná prázdna budova | SCRUM-36 |
| can move | `CanMove` | či je presun Workera práve dostupný (podľa stavu vlny) | SCRUM-36, SCRUM-37 |
| move allowed | `IsMoveAllowed` | stavy vlny, v ktorých sa dá Worker presunúť: `AwaitingWave`, `WaveIncoming`, `WaveActive` (príchod aj dobeh); vo `WavePaused` nie | SCRUM-37 |
| tap | `WorkerTapInput` | ťuknutie na budovu s Workerom presunie Workera na prázdnu budovu | SCRUM-36 |
| capacity | `Capacity` | počet miest obsluhy v budove | SCRUM-30 |
| building queue | `BuildingQueue` | miesta a rad čakajúcich jednotiek v budove | SCRUM-30 |
| arrived / served | `ArrivedCount` / `ServedCount` | koľko jednotiek do budovy prišlo / koľko bolo obslúžených | SCRUM-35 |
| all served | `IsAllServed`, `AllServed` | budova obslúžila všetky jednotky, ktoré prišli (namiesto zakázaného „idle“) | SCRUM-35 |
| utilization | `Utilization` | rýchlosť budovy: 1,0 s Workerom, 0,33 bez neho, nikdy 0 | SCRUM-31, SCRUM-38, SCRUM-86 |
| utilization rules | `UtilizationRules` | jediné miesto, ktoré určuje utilization budovy podľa toho, či má Workera | SCRUM-38 |
| service time | `ServiceTime` | čas obsluhy jednej jednotky | SCRUM-31 |
| service progress | `ServiceProgress` | koľko z obsluhy jednotky je hotové (0–1) | SCRUM-31 |
| speed multiplier | `SpeedMultiplier` | násobok rýchlosti obsluhy (Energy boost) | SCRUM-31, SCRUM-57 |
| wave state machine | `WaveStateMachine` | povolené prechody medzi stavmi vlny, čisté C# | SCRUM-26 |
| wave loop | `WaveLoop` | spúšťa `WaveStateMachine` v scéne, hlási stav do crash reportingu a metrík | SCRUM-26 |
| wave time | `WaveTime` | sekundy od Start Wave; počas `WavePaused` stojí | SCRUM-26 |
| arrival | `IsArrivalOpen`, `ArrivalTime` | fáza príchodu: prvých `wave_timer` sekúnd `WaveActive`, prichádzajú noví hostia | SCRUM-24, SCRUM-25 |
| drain | `IsArrivalClosed`, `DrainSeconds` | dobeh: príchod je uzavretý, budovy obsluhujú zvyšok radov | SCRUM-25 |
| paused | `IsPaused` | vlna je v `WavePaused`; budova vtedy neobsluhuje | SCRUM-26 |
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
- Utilization budovy bez Workera: `utilization_empty` (namiesto zakázaného „idle“).
