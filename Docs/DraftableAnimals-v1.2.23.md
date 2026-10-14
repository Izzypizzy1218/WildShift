# Draftable Animals compatibility audit — v1.2.23

## Installed legacy download

The installed Workshop item `2590957984`, package `Wolfcub05.DraftableAnimals`, supports RimWorld 1.3–1.5. RimWorld 1.6.4871 rev591 falls back to its `1.5/Assemblies/DraftableAnimals.dll`. The binary identifies itself in the log as "Draftable Animals 1.5.4 saz ve ut".

Separate isolated startup probes, with and without WildShift v1.2.22, reproduce the same failure: `DraftableAnimals.FloatMenuMakerMap_ChoicesAtFor_Patch.Postfix` targets a method that no longer exists. `Harmony.PatchAll` aborts and the recorded patch inventory contains no patches owned by `DraftableAnimals.Mod`. This is an independent 1.5-to-1.6 API incompatibility. Loading WildShift alongside the broken binary is not evidence of successful compatibility.

Legacy DLL SHA-256: `8382BE5A8E6BBE2B442A78631342A124E8468A704088BC74F48C6F967BB97A1E`.

## Releashed version used for the joint test

The maintainer publishes the 1.6 continuation as [Draftable Animals - Releashed](https://steamcommunity.com/sharedfiles/filedetails/?id=3534629428), with its [source repository](https://bitbucket.org/alexeiroschak/draftable-animals-releashed).

The final joint tests use the actual downloaded Workshop item `3534629428`, `1.6/Assemblies/DraftableAnimals.dll`, SHA-256 `F4D7DBB6E6196589B5D2549DE220D55FE5B794C9EFDFB3870FCCB74AAD968A6C`, with its 1.6 XML patch. The temporary test copy changes only its package ID to `wildshift.audit.releashed` to select that precise binary independently of the installed legacy item, which uses the same package ID. The DLL and patch XML are unmodified.

For source inspection and initial reproduction the audit also builds repository commit `728db5e905c1f9ea1d8981344c4f7d3764a99a1b` against the installed game/Harmony assemblies, preserving the upstream exclusion of obsolete `Patches/ChoicesAtForlast.cs`. That source-build DLL has SHA-256 `C5AA62789CABC50F87317F49B4F0088D74144E5CE1CD2236E0345FD29637ECA7`; it is distinguished from the final Steam binary. Releashed retains the old 1.5.4 startup message, so identification uses the actual file and patch inventory rather than that message alone.

## Reproduced issue and change

On v1.2.22 an attack-trained transformed warg produces two draft toggles: Releashed's `Draft animal` and WildShift's grouped `Draft`. Releashed only filters the vanilla description, leaving WildShift's differently described toggle. Its added toggle also uses `groupKey` instead of the native `groupKeyIgnoreContent`, so it does not group with the human colonist button.

v1.2.23 filters only draft-hotkey toggles whose action originates in the `DraftableAnimals` assembly, and only for player-owned bodies retaining their original pawn. WildShift's grouped command remains. Ordinary animals and unrelated commands pass through. There is no compile-time dependency on the other mod.

The filter is an iterator-returning Harmony postfix ordered after `DraftableAnimals.Mod`. This matters because a void/ref-result postfix would run before Releashed's iterator postfix, regardless of a late priority. The integration adds no ticking, map scans, save fields or migrations.

Releashed's own rule that animals use melee even when an equipment tracker contains a ranged weapon remains in effect. WildShift still preserves the native ranged decision when Releashed is absent. This audit does not port the obsolete 1.5 mod or change Releashed's rescue/arrest features.

## Verification method

Tests run the actual RimWorld executable with isolated save/config directories. The engine fixture exercises the native drag selector, portrait entries, actual pawn gizmos, native menu providers/actions, real game ticks and a separate process save reload. As documented in `Controls-v1.2.22.md`, batch tests initialize UI dimensions and skip menu font measurement only; they do not verify rendered GUI pixels.

The joint fixture checks that Releashed patches really loaded, trains the warg through native training calls, and verifies exactly one draft toggle with training required and disabled. It also verifies ordinary-animal drafting, movement and undrafting, the retained human-return button, mixed selection, grouped draft/undraft, friendly-object opening, group movement, queued movement, hostile attack, and original identity/selection across return and transformation after reload.

Movement checks wait for observed arrival with a bounded timeout. The fixture clears all roofs before removing generated map structures. Earlier fixture runs removed roof supports but left roofs: the saved battle log confirmed that a ceiling collapse killed the additional selection-test colonist, invalidating a later reload assertion. Initial movement assertions also failed during fixture development; those runs lacked enough pawn-state evidence to attribute each failure separately. Final fixtures prevent roof collapse and retain movement/job-state diagnostics. These failed runs remain separate from final passing evidence and are not presented as production fixes.

## Code and performance review

The final audit reviews transformation/storage rollback, belongings transfer, death recovery, medical-harm records, purchase awakening, race selection, optional apparel/genetics/sapient integrations, enemy queues, revenge scheduling and the control patches. No additional confirmed production defect was identified in that review beyond the duplicate draft toggle.

The existing empty enemy queues perform no pawn scan/allocation; revenge checks pending records every 2,500 ticks; colonist-bar scanning occurs on cache invalidation; animal-form choices are cached; optional fixed-gender handling consumes its one-time flag. These are code-level observations, not a measured TPS guarantee for an arbitrary mod list. Optional mods beyond the specific profiles were not all rerun in this audit.

Final logic checks: 89 form-selection, 17 awakening-race, 243 stability/fault-injection, 18 special-training and 18 revenge-budget assertions (385 total). Native metadata checks cover 40 Harmony targets, 10 injected fields, 10 exact enemy signatures and 14 XML files. The modeled optional-Harmony test retains native callbacks and uses one transformed-state lookup across 10,000 gender callbacks. These stub/metadata checks are distinguished from the engine runs above.

Runtime results and installation hashes are recorded with the release evidence under `wildshift-releas/verification/DraftableAnimals-v1.2.23`. The release ZIP contains neither third-party test binaries nor the regression fixture. Normal user saves and mod configuration remain untouched, as do the 1.6 Workshop binary and XML contents. The legacy-folder backup is recorded below.

## Final engine results

On RimWorld 1.6.4871 rev591, all three final profiles pass a fresh game and a separate process reload:

| Profile | Mod order | Assertion executions |
| --- | --- | ---: |
| Core + actual Releashed DLL | Releashed before WildShift | 84 |
| All five DLC + actual Releashed DLL | WildShift before Releashed | 84 |
| All five DLC + WildShift | Releashed absent | 66 |

These 234 assertions pass without engine exceptions or float-menu worker errors in the final logs. The tested WildShift DLL SHA-256 is `F0915E8D2D9D974BF6FDCB74379B2A03B93BC808701A4F6EED0FEFC65BD043F4`.

After the new Releashed item arrived, the game reported duplicate `Wolfcub05.DraftableAnimals` package IDs. The obsolete `2590957984` directory was moved intact outside the Workshop loader to `wildshift-releas/backups/DraftableAnimals-legacy-2590957984-20260913-220645`, with its DLL hash verified. The 1.6 Workshop item is retained. No Steam subscription or normal save/config was changed. The final all-DLC runs after this move no longer report that duplicate-package warning.
