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
| OK | `Ok` | — | rad je pod `bottleneck_threshold`, alebo nad ním ešte kratšie než `bottleneck_hysteresis_s` |
| BOTTLENECK | `Bottleneck` | „queue growing“ (oranžový riadok), toast „{Building} queue growing“ | rad je ≥ `bottleneck_threshold` aspoň `bottleneck_hysteresis_s` sekúnd |
| RECOVERING | `Recovering` | „catching up“ (zelený riadok), toast „{Building} is catching up“ | po BOTTLENECK rad klesol pod prah; po `bottleneck_hysteresis_s` pod prahom → OK, nad prahom hneď späť BOTTLENECK |
| SATISFIED | `Satisfied` | — | prišiel celý demand needu vo vlne a všetky jednotky sú obslúžené; do konca vlny sa nemení |
| LOCKED | `Locked` | — | budova needu (Pool → Relax, Bungalows → Ocean View) ešte nie je kúpená: need nemá demand, vlna jeho jednotky neposiela a na Wave Needs Paneli sa nezobrazuje; platí aj v stave staveniska Available |

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
| add building | `WorkerRoster.AddBuilding`, `WorkerManager.AddBuilding` | pridá budovu a jedného Workera, ktorý sa objaví priamo na novej budove; prázdna budova ostáva prázdna | SCRUM-39, SCRUM-52 |
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
| worker layout | `Layout` | rozmiestnenie Workerov: pre každého Workera index budovy, na ktorej stojí | SCRUM-40 |
| layout kept between waves | `OnWaveStateChanged`, `SameLayout` | rozmiestnenie sa medzi vlnami nemení; pri štarte vlny (`WaveIncoming`) sa porovná s posledným známym rozmiestnením a zaloguje „Wave N starts with Workers on […]“; zmena bez presunu je chyba (`Debug.Assert`) | SCRUM-40 |
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
| demand release | `DemandRelease` | rovnomerné uvoľňovanie demandu počas príchodu: každý need rýchlosťou demand_total ÷ wave_timer, jednotka i príde v čase i × wave_timer ÷ demand_total | SCRUM-24 |
| clock | `Building.Clock`, `Building.Now` | hodiny budovy pre časy príchodu a usadenia jednotiek; `WaveLoop` ich nastaví na `WaveTime` | SCRUM-24 |
| drain | `IsArrivalClosed`, `DrainSeconds` | dobeh: príchod je uzavretý, budovy obsluhujú zvyšok radov | SCRUM-25 |
| paused | `IsPaused` | vlna je v `WavePaused`; budova vtedy neobsluhuje | SCRUM-26 |
| waiting | `WaitingSeconds`, `WaveWaitLog`, `UnitWait` | čakanie jednotky od príchodu po usadenie; čas obsluhy sa nepočíta, počas `WavePaused` nerastie | SCRUM-42 |
| Start Wave | `StartWaveButton` | tlačidlo, ktoré spustí vlnu (`AwaitingWave` → `WaveIncoming`); viditeľné len keď `CanStartWave` | SCRUM-23 |
| ship arrival | `ShipArrival`, `IncomingProgress` | loď s hosťami pripláva počas `WaveIncoming`, pri móle je presne pri prechode do `WaveActive` | SCRUM-23 |
| Home | `HomeHud` | domovská obrazovka (UI handoff S1): top bar s Resort Value a menami, stav rezortu a značky budov a stavenísk nad mapou | SCRUM-21 |
| island map | `IslandMap` | mapa ostrova: more, ostrov, mólo, budovy zo scény a staveniská | SCRUM-21 |
| island camera | `IslandCamera` | izometrická (ortografická) kamera; zarámuje celý ostrov na každom pomere strán 4:3 – 21:9 a nechá voľné miesto pre top bar | SCRUM-21 |
| construction site | `ConstructionSite`, `VisibleSites` | stavenisko budovy, ktorá ešte nie je postavená (Pool → Relax, Bungalows → Ocean View); vidno ho, kým v scéne nie je budova s jeho needom | SCRUM-21, SCRUM-53 |
| resort status | `ResortStatus`, `ResortStatusRules` | stav rezortu na Home: Ready for Guests, Wave in progress, Wave paused, Wave complete, All waves done | SCRUM-21 |
| balances | `ResortBalances` | zostatky Coins, Energy a Resort Value pre top bar; odkiaľ sa berú, riešia SCRUM-45, SCRUM-55, SCRUM-56 a SCRUM-59 | SCRUM-21 |
| move feedback | `WorkerMoveFeedback` | spätná väzba presunu v tom istom frame: token Workera preskočí zo zdrojovej budovy na cieľovú, cieľová zabliká nazeleno a zväčší sa, zdrojová zabliká načerveno a stmavne; budova bez Workera ostáva tmavšia | SCRUM-41 |
| service meter | `ServiceMeter`, `SpeedPips` | merač obsluhy pred budovou: pás pre každé miesto sa plní postupom obsluhy jednotky (s Workerom asi 3× rýchlejšie); body rýchlosti: 3 s Workerom, 1 bez neho; zelený s Workerom, oranžový bez neho | SCRUM-41 |
| Wave Satisfaction | `WaveSatisfaction` | spokojnosť vlny 0–100 % | SCRUM-43 |
| rating | `Rating` | 1★–5★ alebo Perfect | SCRUM-44 |
| Coins | `Coins` | mena za vlny | SCRUM-45 |
| Energy | `Energy` | mena na boosty | SCRUM-57 |
| Resort Value | `ResortValue` | hodnota rezortu, len rastie | SCRUM-56 |
| Reputation | `Reputation` | skóre za VIP, nikdy neklesá | SCRUM-66 |
| Materials | `Materials` | mena za VIP a vlny 4★+ | SCRUM-69 |
| Gems | `Gems` | platená mena | SCRUM-69 |
| Speed Up | `SpeedUp` | Energy boost obsluhy: za `energy_cost` Energy (3) skráti `service_time` preťaženej budovy na `service_time_multiplier` (polovicu) na `duration_s` (15 s); najviac `max_per_building_per_wave` (1) boost na budovu za vlnu | SCRUM-57 |
| speed up rules | `SpeedUpRules`, `SpeedFactor`, `ResetWave`, `EndAll` | pravidlá Speed Up, čisté C#: počíta použitia a trvanie boostu na čase `WaveTime` (vo `WavePaused` stojí); boost nastaví `SpeedMultiplier` na 1 / `service_time_multiplier`, takže zrýchli aj práve obsluhované jednotky | SCRUM-57 |
| speed up service | `SpeedUpService`, `TrySpeedUp`, `Offers`, `IsOverloaded` | Speed Up v scéne: preťažená budova = need v stave BOTTLENECK alebo budova so Sudden rush; `TrySpeedUp` odpočíta Energy a spustí boost; nová vlna zabudne použitia, koniec vlny ukončí bežiace boosty; udalosti `Started`, `Ended` | SCRUM-57 |
| speed up block | `SpeedUpBlock` | prečo sa Speed Up nedá použiť: `None`, `NotInWave`, `NotOverloaded`, `Active` (už beží), `UsedThisWave`, `NotEnoughEnergy` (ponuka sa ukáže s chýbajúcou sumou) | SCRUM-57 |
| overloaded | `IsOverloaded` | preťažená budova, pri ktorej hra ponúkne Speed Up | SCRUM-57 |
| Sudden rush | `SuddenRush` | mini-event vo vlne 2 | SCRUM-60 |
| Area Unlock | `AreaUnlock` | odomknutie Pool/Bungalows: oblasť, need, od ktorej vlny je dostupné a cena v Coins (`unlocks` v `wave_config.json`) | SCRUM-51, SCRUM-53 |
| area unlock state | `AreaUnlockState` | stav staveniska: Locked (ešte nie je po vlne `*_after_wave`), Available (sprístupnené, ale nekúpené), Bought (kúpené cez `TryBuy`, alebo v scéne už je budova s jeho needom) | SCRUM-53, SCRUM-51 |
| area unlocks | `AreaUnlocks` | stavy všetkých odomknutí a počet dokončených vĺn, čisté C#; Area Unlock sa dá otvoriť len v Available a v `AwaitingWave` | SCRUM-53 |
| area unlock service | `AreaUnlockService`, `Requested`, `TryOpen` | v scéne drží `AreaUnlocks`; kúpené je to, čo sa kúpilo, alebo čo už stojí na `IslandMap` (chýbajúca budova kúpu nikdy nezruší); udalosť `Requested` otvára obrazovku Area Unlock (SCRUM-112), pri odchode z `AwaitingWave` sa zavrie | SCRUM-53 |
| site tap | `AreaUnlockSiteInput` | ťuknutie na stavenisko alebo na jeho značku „+ …“ v HUD otvorí Area Unlock; ťuknutie na budovu ostáva presunu Workera | SCRUM-53 |
| Area Unlock debug window | `AreaUnlockDebugWindow` | dočasné popisy pod staveniskom (len editor a development build): „available · cena“ alebo „od vlny N“; vlastné okno nákupu kreslí len vtedy, keď v scéne nie je S9 | SCRUM-53, SCRUM-112 |
| S9 Area Unlock | `AreaUnlockScreen`, `OnUnlock`, `OnLater` | modálna obrazovka nákupu oblasti: „New Area Available“, názov oblasti, „Adds: {need} demand“, „Higher satisfaction potential“, „Includes: 1 Worker“, „Cost: … Coins“; CTA Unlock / Later; pri nedostatku Coins Unlock nejde a ukáže „Missing: … Coins“. Jeden layout pre Pool aj Bungalows, líši sa len akcentová farba, ikona a veta o oblasti. Sama sa otvorí raz, keď sa po vlne unlock sprístupní a hra sa vráti do `AwaitingWave`; po Later ju otvorí ťuk na stavenisko | SCRUM-112 |
| Later | `AreaUnlockScreen.OnLater` | zavrie S9 bez nákupu (aj Escape / späť na Androide); stavenisko ostane available | SCRUM-112 |
| VIP | `Vip` | špeciálny hosť s requestami | SCRUM-61 |
| Guest Request Board | `GuestRequestBoard` | požiadavky VIP hosťa | SCRUM-62 |
| Wave Needs Panel | `WaveNeedsPanel` | panel vedľa ostrova počas vlny (`WaveIncoming`, `WaveActive`, `WavePaused`): pre každý aktívny need obslúžené / celkové jednotky vlny a značka Worker, ak na budove needu stojí Worker | SCRUM-29 |
| active need | — | need, ktorý má vo vlne demand > 0, alebo doň aj tak prišli jednotky; len taký need má riadok vo Wave Needs Panel | SCRUM-29 |
| need progress | `NeedProgress`, `WaveNeedsProgress` | obslúžené a celkové jednotky needu od Start Wave; celkové = demand z `wave_config.json`, alebo viac, ak prišlo viac jednotiek (Sudden rush) | SCRUM-29 |
| need state | `NeedState` | stav needu vo vlne: OK, BOTTLENECK, RECOVERING, SATISFIED, LOCKED | SCRUM-32, SCRUM-34 |
| need state tracker | `NeedStateTracker` | prechody stavov jedného needu s hysteréziou, čisté C# | SCRUM-32 |
| need state board | `NeedStateBoard` | v scéne posúva stavy všetkých needov; rad needu = súčet backlogov všetkých jeho budov; na Start Wave vráti needy na OK; udalosť `StateChanged` | SCRUM-32 |
| need warning | `WaveNeedsPanel.ShownState` | farebný riadok needu vo Wave Needs Paneli: BOTTLENECK oranžový s „queue growing“, RECOVERING zelený s „catching up“ (nesmie vyzerať ako hotové); stĺpec stavu má pevnú šírku, riadok sa neposúva | SCRUM-33 |
| guest feedback toast | `GuestFeedbackToasts` | krátka správa v S2 dole v strede v tom istom frame ako zmena stavu needu: BOTTLENECK „{Building} queue growing“ + veta hosťa (napr. „Food takes a bit too long.“), RECOVERING „{Building} is catching up“ + „That's more like it!“; jeden toast na need (nový stav ho nahradí), max 3 naraz, 3,5 s + 0,4 s fade, rovnaký toast needu znova až po 8 s (cooldown); po konci vlny zmiznú; komponent sa pridá za behu na `NeedStateBoard` | SCRUM-33 |
| hysteresis | `bottleneck_hysteresis_s` | koľko sekúnd musí podmienka platiť, kým need prejde OK → BOTTLENECK alebo RECOVERING → OK | SCRUM-32 |
| level | `Level`, `MaxLevel`, `IsMaxLevel` | level budovy 1–3; určuje capacity podľa `capacity_by_level` (3 → 5 → 7) | SCRUM-50 |
| level up | `LevelUp` | zvýši level budovy o 1 a hneď zväčší capacity; čakajúce jednotky sa posadia na nové miesta | SCRUM-50 |
| upgrade service | `UpgradeService`, `PriceOf`, `CanUpgrade`, `TryUpgrade` | nákup upgradu za Coins: cena z `upgrade_prices`, odpočíta Coins a zavolá `LevelUp`; udalosť `Upgraded`; budova sama o peniazoch nevie | SCRUM-50 |
| currency wallet | `CurrencyWallet`, `Balance`, `Add`, `TrySpend`, `CanAfford`, `Missing` | zostatok jednej meny, čisté C#; meniť ho dá len cez `Add` (napr. odmena za vlnu) a `TrySpend` (upgrade, unlock), nikdy neklesne pod 0; udalosť `Changed` (starý zostatok, nový zostatok, dôvod); rovnakú triedu použije aj Energy | SCRUM-55 |
| energy wallet | `ResortBalances.EnergyWallet` | peňaženka Energy v scéne (rovnaká `CurrencyWallet` ako Coins); Speed Up míňa Energy cez `EnergyWallet.TrySpend` | SCRUM-57 |
| coin wallet | `ResortBalances.CoinWallet` | peňaženka Coins v scéne; `Coins` sa dá už len čítať, pripisuje sa cez `CoinWallet.Add` (SCRUM-45) | SCRUM-55 |
| missing coins | `Missing`, `PurchaseRules.MissingCoins`, `UpgradeService.MissingCoins`, `AreaUnlockService.MissingCoins`, `economy.missing_coins` | koľko Coins chýba na cenu; text v UI „Missing: {0} Coins“ z tabuľky `UI` | SCRUM-55 |
| between waves | `WaveState.IsBetweenWaves()` | stavy vlny, v ktorých sa dá nakupovať (upgrade aj Area Unlock, cez `PurchaseRules`): `AwaitingWave` a `Upgrade`; počas vlny, na obrazovke výsledku ani vo `WavePaused` nie | SCRUM-49, SCRUM-51 |
| upgrade available | `UpgradeService.IsUpgradeAvailable`, `AvailabilityChanged` | či je upgrade práve dostupný podľa stavu vlny; udalosť `AvailabilityChanged(bool)` pri zmene, aby UI ovládací prvok upgradu skrylo (`false`) alebo ukázalo (`true`) | SCRUM-49 |
| purchase block | `PurchaseBlock`, `UpgradeService.UpgradeBlockOf`, `AreaUnlockService.BlockOf` | prečo sa niečo nedá kúpiť (upgrade aj Area Unlock): `None` (dá sa), `BetweenWavesOnly` (prebieha vlna → prvok skryť), `NotAvailableYet` (Area Unlock pred jeho vlnou), `AlreadyOwned` (Area Unlock už kúpený), `MaxLevel` (ukázať MAX), `NotEnoughCoins` (ukázať chýbajúcu sumu); nahrádza `UpgradeBlock` | SCRUM-49, SCRUM-51 |
| purchase offer | `PurchaseOffer`, `OfferFor` | jedna vec na kúpu: názov pre log Coins, cena a vlastný dôvod predávajúceho, prečo sa nedá kúpiť | SCRUM-51 |
| purchase rules | `PurchaseRules`, `IsShopOpen`, `BlockOf`, `TryBuy` | jediné miesto s pravidlami nákupu za Coins pre upgrady aj Area Unlock, čisté C#: najprv stav vlny (len medzi vlnami), potom dôvod predávajúceho, potom Coins; Coins sa odpočítajú a až potom sa vec odovzdá | SCRUM-51 |
| area unlock purchase | `AreaUnlockService.TryBuy`, `Bought` | kúpa Pool/Bungalows za Coins: odpočíta cenu, označí Bought, zavrie Area Unlock, postaví budovu (`Build`) a až potom vyvolá udalosť `Bought` | SCRUM-51, SCRUM-52 |
| unlock building | `AreaUnlockService.Build`, `buildingTemplate` | po kúpe postaví na mieste staveniska budovu s needom odomknutia (Pool → Relax, Bungalows → Ocean View) na leveli 1, pridá ju do `WorkerManager` aj s novým Workerom a obnoví `IslandMap` (stavenisko zmizne); vzor budovy je `buildingTemplate`, inak prvá budova `WorkerManager`; need dostane demand od ďalšej vlny | SCRUM-52 |
| building clone | `Building.CloneFor` | kópia budovy s iným needom, menom a pozíciou na leveli 1; need sa nastaví ešte pred `Awake`, takže rad budovy vznikne už pre nový need | SCRUM-52 |
| site position | `IslandMap.TryGetSite` | pozícia staveniska needu na zemi ostrova | SCRUM-52 |

## Zjednotené
- Utilization budovy s Workerom: `utilization_worker` (od SCRUM-96 existuje len jeden config, `tools/config.json` bol zmazaný).
- Utilization budovy bez Workera: `utilization_empty` (namiesto zakázaného „idle“).

## Debug klávesy
Pred pridaním nového debug klávesu over, že tu ešte nie je, a zapíš ho sem.

| Kláves | Skript | Čo robí | Jira |
| --- | --- | --- | --- |
| Space | `BuildingDebugInput` | +1 jednotka do budovy | SCRUM-30 |
| S | `BuildingDebugInput` | dokončí obsluhu prvej sediacej jednotky | SCRUM-30 |
| W | `BuildingDebugInput` | +20 jednotiek do budovy | SCRUM-35 |
| E | `BuildingDebugInput` | rýchlosť obsluhy ×2 / ×1 | SCRUM-38 |
| P | `WaveLoopDebugInput` | pauza / pokračovanie vlny | SCRUM-26 |
| L | `WaveLoopDebugInput` | posunie vlnu do ďalšieho stavu | SCRUM-26 |
| N | `WorkerManagerDebugInput` | pridá budovu a Workera | SCRUM-38 |
| U | `UpgradeServiceDebugInput` | upgrade všetkých budov; pri nedostatku vypíše „Missing: … Coins“, počas vlny „only between waves“, na L3 „max level“; pri zmene stavu vlny sám vypíše „Upgrade available/hidden“ | SCRUM-50, SCRUM-55, SCRUM-49 |
| C | `UpgradeServiceDebugInput` | +300 Coins (náhrada odmeny za vlnu) | SCRUM-55 |
