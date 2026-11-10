# WildShift native performance probe (TEST ONLY)

This helper measures a deliberately controlled workload in actual RimWorld 1.6 or 1.7. It is not shipped in the runtime mod and must never be enabled in a real colony. `Run-Performance.ps1` builds/runs it only with explicit isolated paths; stage the built helper separately before native execution.

## Comparison

Create one native quicktest seed/save per engine/preset, then load byte-identical copies in fresh processes for the three conditions:

| Active transformed forms | Canonical humans | Spawned human bodies | Spawned animal bodies |
| --- | --- | --- | --- |
| 0 | 50 | 50 | 0 |
| 20 | 50 | 30 | 20 |
| 50 | 50 | 0 | 50 |

The total remains 50 people and 50 spawned bodies. Active animal forms additionally preserve their original human body. Comparing 0 against 20/50 therefore measures the transformed-body/shared-state workload under this fixed population; it is not a comparison against an empty map. Every original is a Baseliner with one assigned Warg form. The exact original IDs, native source save/hash, tick and RNG state are recorded. Generation of condition-specific animal bodies runs through production transformation under `Rand.PushState/PopState`, so it does not advance the restored common world RNG state.

`FeaturePreset=basic` uses otherwise normal Baseliners. `abilities` additionally gives all 50 originals native FireSpew, AnimalWarcall, Hemogenic, HemogenDrain and MoveSpeed_Quick genes, plus a long-lived native WorkDrive buff. Use its own common baseline; do not compare it to a basic baseline as if only the form count changed.

## Controlled workload and time

All 50 active bodies are drafted and stationary on the same cleared 75x75 native map. Native ticks, health, genes, abilities, memories, needs, map and world systems continue normally. Storyteller incidents, random mental breaks/disease and fast ecology are disabled through native debug settings. New ordinary wild spawns are prevented by setting the isolated world's current tile biome animal density to zero and checking native `AnimalEcosystemFull`; both tested spawner APIs recompute density without a cache. `DebugSettings.noAnimals` remains **false**, because native Pawn ticking destroys existing spawned animal bodies when that flag is true. Once each **wall second**, the fixture refills each original's needs and the active animal's needs; the abilities preset also maintains canonical hemogen. It calls the active body's ordinary native `AutoUndrafter.Notify_Drafted()` to refresh its inactivity stamp, preventing the native 10,000-game-tick automatic release of stationary `Wait_Combat` pawns. This neither toggles `Drafted` nor replaces jobs or patches native tick/undraft logic; an unexpectedly undrafted pawn still fails before this keepalive. Maintenance/keepalive calls and time are reported, and remain included in wall-clock TPS. This transparent maintenance prevents a many-game-day test from turning into starvation, exhaustion or a different population. Population/ownership/draft/health state is checked each metric sample; drift fails with the exact pawn/health/map/job state.

The probe selects native `TimeSpeed.Ultrafast`, leaving the scheduler unchanged. On the tested native map API its normal target is 900 TPS. The effective multiplier is recorded in CSV/progress; a flat result at the native ceiling means no measured saturation at that target, **not** unlimited capacity. Native `DoSingleTick` is observed by allocation-free timing hooks; no manual tick loop, time scaling patch or forced scheduler runs are used.

Main runs use `GraphicsMode=Native` and native `Application.targetFrameRate=60`, with actual hidden graphical players on both versions. They request `QualitySettings.vSyncCount=0` and record the observed value separately: its 1.7 compatibility setter is a no-op stub, so a zero VSync readback is not claimed or required. Native engine frame counts/FPS and read-back settings are recorded; the Native result fails if average FPS exceeds the cap tolerance. Lower FPS remains valid saturation evidence. The 1.7 Waylith Headless loop does not call its native frame throttle, so `GraphicsMode=Headless` is a separately labelled diagnostic: the same target property does **not** pace it, and its high per-frame CPU/allocation churn must not be attributed wholly to WildShift.

The user-requested comparison uses **30 wall seconds warmup and 120 wall seconds measured time per condition**, the defaults in `Run-Series.ps1`. The series accepts warmup from 0 seconds and measured windows from 60 seconds while retaining its duration, population, identity, frame-cap and diagnostic guards. This is a short steady-state comparison, not a long-duration memory/leak study. Longer windows remain configurable and must be identified by their actual recorded duration. `Smoke=true` results are setup/instrumentation checks and remain separate from accepted comparison results. Startup, generation, transformation and loading lie outside the measured steady-state window.

## Metrics

`PerformanceResults/samples.csv` records each sample (normally every 10 seconds): native tick count/window and overall TPS; cumulative tick mean/p50/p95; working set and private bytes; managed heap without collection; cumulative allocated bytes; process CPU expressed as percent of one logical core; system busy percent; available physical RAM; interval page faults; cumulative generation 0/1/2 collections; and native rate multiplier.

Native tick latency uses one fixed 1-microsecond histogram, allocated before measurement. A bin at 200 ms collects longer ticks and the overflow count is reported. Constant probe overhead applies to each condition. Working set is resident process RAM; private bytes are committed private process memory; managed heap is a different metric and need not equal either. Allocation **rate** and collections can reveal churn even when live heap stays flat. On modern 1.7, allocation counters cover all managed threads. On Mono 1.6, reflection selects the runtime-supported counter (potentially main-thread only), or explicitly reports it unsupported; compare only like scopes.

No forced GC runs during warmup or measurement. After timing ends, the fixture pauses, records pre-GC managed heap, performs a full collection and records retained managed heap as a **separate post-measurement diagnostic**. This is not a timed TPS sample or a guarantee that retained growth is a leak. The original 10-second time series remains the main evidence for growth/churn.

The isolated profile uses the native autosave interval of 1,000 game days, which remains safely within the native integer tick range and beyond any timed case. The fixture verifies that value and non-permadeath mode. This keeps autosave serialization/IO outside the steady-state benchmark; normal user preferences are not changed.

Result/progress files retain actual loaded production/helper/game hashes and paths, ordered mod IDs, canonical IDs, duration, exact tick/frame counts, native errors, maintenance settings and population checks. The different 1.7 Waylith and 1.6 Unity engines/runtimes/renderers still have different baseline costs; do not attribute their absolute difference to WildShift. This minimal-profile stationary benchmark does not establish hauling/combat AI, graphical full-mod-pack or arbitrary colony performance. Failures also attempt a native `failure-snapshot.rws` save **inside the isolated profile only**, with expected-body versus production-cache identity diagnostics; the stored reference never substitutes for a failed production lookup.

## Runner input contract

The isolated profile contains `performance-inputs.xml` with root containing `Phase` (`seed` or `measure`), `ActiveForms`, `TotalPawns` (50), `WarmupSeconds`, `MeasureSeconds`, `SampleSeconds`, `FeaturePreset`, `Smoke`, `GraphicsMode` (`Native` or diagnostic `Headless`), `TargetFPS` (60), `GameVersion` (native first version token), `GameAssemblyPath`, `GameAssemblySHA256`, `WildShiftPath`, `WildShiftSHA256`, `FixtureSHA256`, and `Mods/li` (actual expected IDs/order). No account settings or credentials are copied.

Seed writes native `Saves/autostart.rws` and `PerformanceResults/seed-result.json`. Measurement loads a fresh copy of that native baseline and writes `samples.csv`, `progress.json` and `result.json`. Preserve failed/smoke/long results in separate output folders. Rebuild with explicit matching `RimWorldVersion`, `RimWorldDir`, `HarmonyPath`, `WildShiftPath`, external output and intermediate paths; both engine variants use this same C# source without generated-source replacements.
