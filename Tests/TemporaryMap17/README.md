# Native temporary-map regression (TEST ONLY)

This helper is a separate test mod, never part of the runtime release. It uses the actual installed RimWorld 1.7 engine, Harmony and WildShift DLL. There are no mocked map/pawn/world/game-ending methods or test patches to those methods. The sole fixture patch reduces native quicktest map size; fixture setup removes stock pawns and scenery and attaches the fully initialized map to an actual native `Site` or `Camp` parent. `SiteMaker.MakeSite` supplies a real `AncientComplex` part when available, or a Core `PreciousLump` part otherwise, so native labels and save/load cross-references are valid. This tests map-retention and cleanup behavior, not ruins content generation, rendering or arbitrary external camping mods.

The first process creates a real native one-human caravan, enters the temporary map through `CaravanEnterMapUtility`, calls production `TransformToAnimal`, and advances 600 native ticks. The fixed build must retain the map and original identity without Caravan lost/Game Over. It saves a complete native game to its isolated `autostart.rws` and exits. A second process loads that game through the engine's autostart path, advances another 600 ticks, calls production reversion, and checks the same human and inventory. Native caravan departure then leaves the map, native cleanup removes the empty temporary map, and 600 more native ticks verify the caravan survives.

For a known-bad DLL, `-ExpectFailure` requires actual native map removal plus both Caravan lost and Game Over letters. It does not count an arbitrary crash, missing result, logged engine exception or mere predicate failure as a successful red test. The failing run advances additional ticks if necessary to observe the real countdown letter.

Build separately, outside the repository:

```powershell
pwsh -NoProfile -File Tests/Run-TemporaryMap17Tests.ps1 -BuildOnly -Output C:/absolute/evidence/fixture-build
```

Stage the resulting `fixture-mod/WildShiftTemporaryMapValidation` folder in the game's normal mod search path separately. Close the user's game before execution, then run against the selected, separately staged production build:

```powershell
pwsh -NoProfile -File Tests/Run-TemporaryMap17Tests.ps1 -SkipBuild -FixtureDir C:/absolute/discoverable/WildShiftTemporaryMapValidation -WildShiftDir C:/absolute/discoverable/WildShift -TemporaryMapKind Site -Output C:/absolute/evidence/site-fixed
```

Use `Camp` for the native camp variant, `-CoreOnly` to omit optional DLCs, or `-ExpectFailure` for the red run with the old production DLL selected externally. Restore the selected production DLL in the external caller's `finally` block. This runner never installs, swaps or deletes game mods and never terminates an already-running user game. It may terminate only a process it created when that test times out or fails.

The runner checks actual in-process WildShift/fixture hashes and loaded package IDs, collects stdout/stderr, preserves existing legacy log files and verifies normal user-config file hashes. It uses a fresh savedata profile and output directory for each run. Core-only/all-DLC native checks do not establish third-party mod compatibility, visual behavior or full 1.6 gameplay.
