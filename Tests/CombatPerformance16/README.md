# Native 1.6 combat fixture — TEST ONLY

This project adapts the shared `CombatPerformance` scenario into an external
source snapshot and compiles against the actual Unity 1.6 managed assemblies.
It never loads the user's 1.7 save and is excluded from runtime distributions.

`Run-CombatPerformance16.ps1 -GameDir <native-1.6-dir> -Output <fresh-external-dir>
-BuildOnly` creates the helper without launching or installing anything. Stage
its separate mod in a private engine, then use `-SkipBuild -FixtureDir <helper>`
with a new output directory. No production DLL swapping is performed by this
runner; the expected WildShift hash must match the actual staged version.

Use `-Actors 5 -ActorKind Transformed -Loadout rifle -Enemies 60` or `240` for
the requested basic cases. `-GearPreset FullNative -RichGenes` adds native
cataphract armor, archotech installations, supported serums and gene presets.
`-EnemyAssault -EnemyLoadout rifle` enables real native assault AI and incoming
fire. Actors and enemies take real damage and can die; inspect population drift
before treating timings as comparable. Use matching human controls and identical
fixture hashes for before/after comparisons.

`-GraphicsMode Native` runs the native graphics path; `Batch` adds Unity's
`-batchmode` while retaining a real D3D11 graphics device. Neither passes
`-Headless` or `-nographics`. Batch timings are not display-FPS validation.
Tick CPU, root-update CPU, frame intervals and native forced-speed caps remain
separate measurements. `-Diagnostic` adds instrumentation overhead and separate
transformation/drafting method profiles; compare uninstrumented runs for speed.

The selected native Mono exposes its real current-thread allocation counter.
The fixture verifies this and fails explicitly if it cannot run. The missing
process-total allocation API produces `null` with a support flag, never a heap
size substitute or a fabricated zero. The JSON shim comes from `SharedState16`,
with dictionary/enum support added for combat result objects.

All source adaptations and input hashes are recorded. Normal Config/Saves are
hashed before and after, existing Unity logs are backed up, and the runner can
terminate only its own process. Actual native gameplay still needs to be run;
compilation alone is not a combat validation result.

`Run-SelectionArena16.ps1` and `WildShift.SelectionArena16.csproj` are a separate,
synthetic paused-selection UI control. They generate one native FullNative,
Sanguophage actor and rifle NPCs by default, then reuse the selected/deselected/
reselected sequence through the explicit ArenaSelectionSequence input. Native
simulation remains paused during comparison; forced arena Wait_Combat jobs do
not establish actual combat AI validation. No user save/profile is loaded.
Optional `-AdditionalMods jaxe.rimhud` requires RimHUD already staged in the
private engine's Mods directory and copies the actual Config/RimHUD folder to
the disposable profile when available. The old combat runner is unchanged.

The native 1.6 selection observer uses the verified managed
RimWorld.UIRoot_Play.UIRootOnGUI entry, since the first native-message Root.OnGUI
observer returned zero calls despite valid screen dimensions. The exact entry
name is recorded; this excludes Root's outer UI-scale/fader wrapper. Explicit
1280×720 command-line dimensions supplement private Prefs. A zero GUI/Repaint
count remains invalid regardless of Root update frequency.
