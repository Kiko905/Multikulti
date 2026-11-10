# Crash reporting (SCRUM-100)

Nástroj: **Sentry** (`io.sentry.unity` 4.11.0, inštaluje sa cez `Packages/manifest.json`).

## Čo sa posiela

Každý crash a neošetrená výnimka (C# aj natívne na Androide a iOS) s týmito údajmi:

| Údaj | Odkiaľ |
|---|---|
| `release` | `multikulti@<verzia buildu>` (Project Settings → Player → Version) |
| `environment` | `editor` / `development` (development build) / `playtest` |
| `session_id` | `PlaytestSession.Id`, rovnaké ako v metrikách playtestu (SCRUM-93) |
| `build_version` | verzia buildu |
| `game_state`, `wave_index` | `CrashContext.SetGameState(...)`, `CrashContext.SetWave(...)` |
| screenshot | v momente chyby |

Neposiela sa IP adresa ani meno hráča (`SendDefaultPii = false`).

## Pre programátora wave loopu

Pri každej zmene stavu zavolaj:

```csharp
CrashContext.SetGameState("WAVE_ACTIVE");   // názvy stavov z build dokumentu §4.3
CrashContext.SetWave(waveIndex);
```

## Nastavenie (raz, robí to vlastník Sentry účtu)

1. Na sentry.io vytvor projekt typu **Unity** a skopíruj **DSN**.
2. V Unity: **Tools → Sentry** → vlož DSN.
3. V Project okne: pravý klik v `Assets/Resources/Sentry` → **Create → Multikulti → Sentry Config**.
4. V okne **Tools → Sentry**, záložka **Options Config**: do poľa pre options configuration priraď vytvorený `MultikultiSentryConfig` (nevytváraj nový skript, už existuje).
5. Commitni `Assets/Resources/Sentry/` a `Packages/packages-lock.json`.

## Overenie

V Play mode: **Multikulti → Sentry → Send test message** a **Send test exception**. Obe udalosti sa do minúty objavia na sentry.io; výnimka má tagy `game_state = WAVE_ACTIVE` a `wave_index = 2`.

Natívne crashe na zariadení sa overia na Android builde z GitHub Actions (SCRUM-98).
