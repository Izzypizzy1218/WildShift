# Native Unity 1.6 shared-state validation (TEST ONLY)

This is a separate **net472 / Unity 1.6** helper and runner. Preparation against cached 1.6 reference assemblies establishes API/build compatibility only. A successful three-process run inside a real 1.6 player is required before claiming gameplay validation. It does not verify graphics or optional mod packs. Never distribute or enable this helper in a real colony.

`Run-SharedState16Tests.ps1` reuses the unchanged `SharedState17/SharedStateRegression.cs`, health probe source and fixture definitions. It generates an external main-source variant with only the JSON namespace, fixture identifier/output directory, version label, input guards and result metadata adapted for 1.6. Every replacement requires a unique anchor and fails if the shared source changes unexpectedly. The generated source, all input hashes and compiled helper hash are recorded in `fixture-build.json`. No 1.7 file is edited.

The shared scenario uses real initialized pawns, production transformations, native skill learning and ingestion, 300-tick map/caravan intervals and native health probes. Three fresh processes save/reload first an active human holding its cached animal, then an active caravan animal holding its human and drug projection references. Pawn/implant identities, custom implant state 731, spouse/memory, canonical skill records/XP, drug progression, one-way ownership, wound reset and exactly 17 silver are checked. The source's interval-tolerant health probe supports both native per-tick and coalesced scheduling; it does not replace native ticking or healing.

The local JSON writer supports only the anonymous objects, primitive values and arrays used by this fixture. It has no external JSON dependency. `JsonShimTests.csproj` verifies its result shapes, Korean/Windows paths, escapes, Unicode, invariant numeric formatting, null/empty arrays and rejection of non-finite values using the .NET 10 JSON parser. This serializer check does not run the game.

Build into a fresh external directory with existing 1.6 API references and a separately built 1.6 production DLL:

```powershell
./Tests/Run-SharedState16Tests.ps1 -BuildOnly `
  -RimWorldManaged C:/existing-1.6-references/net472 `
  -WildShiftPath C:/candidate16/WildShift.dll `
  -Output C:/evidence/shared-state16-build
```

If `RimWorldManaged` is omitted during `BuildOnly`, the runner first checks the selected game's `RimWorldWin64_Data/Managed` folder, then the already cached `krafs.rimworld.ref/1.6.4871` directory. It never downloads references, installs a game, changes Steam branches, stages mods or swaps production DLLs. The project requires the runner-generated source path when built directly.

After a real 1.6 game is available and the helper/production mod are separately staged in its mod search path, close RimWorld and run:

```powershell
./Tests/Run-SharedState16Tests.ps1 -SkipBuild `
  -GameDir C:/existing-RimWorld-1.6 `
  -FixtureDir C:/discoverable-mods/WildShiftSharedState16Validation `
  -WildShiftDir C:/discoverable-mods/WildShift `
  -WildShiftPath C:/candidate16/WildShift.dll `
  -Output C:/evidence/shared-state16-run
```

The expected player is `RimWorldWin64.exe`; `GameExe` can specify its actual existing path. Unity receives `-batchmode -force-d3d11 -screen-fullscreen 0 -logFile <isolated-log> -savedatafolder=<isolated-profile>`. The real graphics device must initialize: the native 1.6 texture-atlas setup throws with `-nographics`. Such a run is rejected rather than filtering its errors or patching the renderer. Only phase 0 receives `-quicktest`. The fixture calls native `Application.Quit()` after each phase, so the runner does not pass Unity's early `-quit` or the managed 1.7 `-Headless` flag.

Native 1.6 source establishes the game-side entry points: `QuickStarter.CheckQuickStart` handles `-quicktest`; `GenFilePaths.SaveDataFolderPath` reads `-savedatafolder=`; `Root_Entry.Start` / `Root_Play.Start` load the isolated `autostart` save; `SaveGameFilesUtility.GetAutostartSaveFile` requires `Prefs.DevMode`. The isolated profile therefore enables DevMode and all five DLCs. Fresh reload phases omit `-quicktest` and use their native autostart save.

Before launch the runner checks the 1.6 version, staged/source production hash and existing game DLL/executable. Inside Unity, the helper independently checks native `VersionControl`, actual loaded game/production/helper/Harmony DLL paths or hashes and the isolated savedata path against `sharedstate16-inputs.xml`. Each phase reports loaded mods, native Verse errors, runtime metadata and DLL hashes. The runner rejects mismatches and preserves Unity/stdout/stderr logs, phase JSON and `result.json`.

Normal user configuration is inventoried before and after, including added/deleted files. Saves, preferences, mod selection, logs and generated sources stay under the external output directory. An already running game prevents launch; timeout cleanup can terminate only the process created by this runner. Build preparation never launches the game. Exclude the complete helper and fixture definitions from runtime packages and Workshop uploads.
