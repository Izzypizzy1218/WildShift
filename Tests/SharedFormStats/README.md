# Shared form stat regression

`Run-SharedFormStatsTests.ps1` compiles the production `SharedFormStats.cs` into an isolated console fixture and runs it against the actual installed RimWorld 1.7 assemblies. It uses real `Gene.Active`, `Trait.Suppressed`, `Hediff.CurStage`, `StatWorker` calculation/explanation and native stat caches, with isolated pawn subgraphs. It does not launch the game or validate rendering, optional mods, or a fully initialized save.

Run from the repository root with a fresh output path outside the repository:

```powershell
./Tests/Run-SharedFormStatsTests.ps1 -ExpectFailure -Output C:/evidence/shared-stats-before
./Tests/Run-SharedFormStatsTests.ps1 -Output C:/evidence/shared-stats-after
```

The baseline omits the new Harmony patches and must reproduce the exact native value 26 instead of the expected shared value 96. The fixed fixture checks native offset/factor ordering, suppression, duplicate effects, ownership, same-tick source replacement, death, reentrancy, zero/negative modifiers, native tooltip data, and temporary/immutable cache invalidation. Formula example: `(animal base 10 + animal health 3 + human gene 2 + human trait 1) * animal health factor 2 * gene factor 1.5 * trait factor 2 = 96`. The human's separate health modifier of +1000 is excluded.

After building the complete production DLL elsewhere, use `-Production -WildShiftPath C:/isolated-build/WildShift.dll` to test the classes from that exact DLL instead of linking the source. The console prints the implementation assembly path. Native full-game transformation and save/reload validation remain separate checks.

The project also supports a compile-only `-p:RimWorldVersion=1.6` check when given matching `RimWorldManaged`, `HarmonyPath` and `WildShiftPath` references. That is API/build validation, not a 1.6 gameplay test.

The feature shares only unconditional `GeneDef.statOffsets/statFactors` and unsuppressed trait degree stat modifiers. Conditional affecters, capacities, abilities, custom gene execution and final human stats are outside this bridge. Definitions are treated as immutable native data. Runtime gene/trait membership, activation, suppression and original-pawn changes are observed on each stat query; no timer or global pawn scan is added. A modded transformed-carrier stage with severity/stat multiplier scaling is preserved without projection rather than applying an incorrect scaled gene effect. Health effects use a separate projection.

The runner uses a separate savedata path and writes only its external build/evidence directory. Exclude the fixture from runtime packages and Workshop uploads.
