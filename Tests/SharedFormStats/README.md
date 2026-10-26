# Shared form stat regression

`Run-SharedFormStatsTests.ps1` compiles the production `SharedFormStats.cs` into an isolated console fixture and runs it against the actual installed RimWorld 1.7 assemblies. It uses real `Gene.Active`, `Trait.Suppressed`, `Hediff.CurStage`, `StatWorker` calculation/explanation and native stat caches, with isolated pawn subgraphs. It does not launch the game or validate rendering, optional mods, or a fully initialized save.

Run from the repository root with a fresh output path outside the repository:

```powershell
./Tests/Run-SharedFormStatsTests.ps1 -ExpectFailure -Output C:/evidence/shared-stats-before
./Tests/Run-SharedFormStatsTests.ps1 -Output C:/evidence/shared-stats-after
```

The baseline omits the new Harmony patches and must reproduce the exact native value 26 instead of the expected shared value 96. The fixed fixture checks native offset/factor ordering, suppression, duplicate effects, ownership, same-tick source replacement, death, reentrancy, zero/negative modifiers, native tooltip data, and temporary/immutable cache invalidation. Formula example: `(animal base 10 + animal health 3 + human gene 2 + human trait 1) * animal health factor 2 * gene factor 1.5 * trait factor 2 = 96`. The human's separate health modifier of +1000 is excluded.

Conditional coverage uses the native clothed/unclothed and sunlight affecters. It checks positive and negative offsets/factors, duplicate modifier list entries, suppressed/duplicate genes, native genes already on the active body, and same-tick dress/undress, roof and sky-glow transitions. A clothed stored human with an unclothed active animal must give `(10 + 2 + 3) * 1.5 * 1.2 = 27`; the previous unconditional-only implementation gives 18. Clothing the animal changes the value to `(10 + 2 - 4) * 1.5 * 0.8 = 9.6`. An isolated native map/roof/sky graph exercises `ConditionalStatAffecter_InSunlight` without replacing its methods. A custom affecter checks active-body position, recursive stage queries and native immutable-cache invalidation. These are native subgraph tests, not full-map spawning or visual tests.

After building the complete production DLL elsewhere, use `-Production -WildShiftPath C:/isolated-build/WildShift.dll` to test the classes from that exact DLL instead of linking the source. The console prints the implementation assembly path. Native full-game transformation and save/reload validation remain separate checks.

The project also supports a compile-only `-p:RimWorldVersion=1.6` check when given matching `RimWorldManaged`, `HarmonyPath` and `WildShiftPath` references. That is API/build validation, not a 1.6 gameplay test.

The feature shares active genes' `GeneDef.statOffsets/statFactors`, their applicable `conditionalStatAffecters`, and unsuppressed trait degree stat modifiers. Each conditional predicate receives `StatRequest.For(activeBody)`, so the actual animal's environment, age/race and apparel determine its result. Capacities, abilities, arbitrary gene execution and final human stats are outside this stat bridge. Definitions are treated as immutable native data. Runtime gene/trait membership, activation, suppression, original-pawn changes and conditional predicate results are observed on stat queries; changed conditions clear only that body's queried stat-worker cache in the same tick. No timer or global pawn scan is added. Unchanged inputs reuse the projected stage. A modded transformed-carrier stage with severity/stat multiplier scaling is preserved without projection rather than applying an incorrect scaled gene effect. Health effects use a separate projection.

The runner uses a separate savedata path and writes only its external build/evidence directory. Exclude the fixture from runtime packages and Workshop uploads.
