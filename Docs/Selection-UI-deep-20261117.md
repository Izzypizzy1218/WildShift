# Selected-form code investigation: repeated health invalidation

This continues `Selection-UI-20261117.md` without requiring the reporter's save. A redundant invalidation mechanism is reproduced and a focused source fix is prepared. **This does not yet establish that the reporter's severe combat slowdown is fully resolved.**

## Confirmed mechanism

1. `SharedFormThoughts.QueryScope` temporarily exposes the preserved human's ideology on the active animal for personal thought queries, then restores the physical animal's tracker.
2. `SharedFormSystems.ModifierVersion` includes the source/body ideology identities in its signatures.
3. CPU4's `SharedFormStats.State.Refresh` previously treated that signature change as a projected health change even when the actual modifier data was identical.
4. A capacity or pain query reached `PrepareHealth`, which called native `HediffSet.DirtyCache`. Native code invalidates wound/portrait/atlas caches as well as health, capacity, summary and meditation caches.

An isolated program referencing the actual CPU4 DLL and actual 1.7 engine APIs reproduced **40 full health invalidations in 20 alternating personal/physical query cycles at the same game tick**. The source ideology had no precepts or modifiers; capacity remained 1.1 and pain remained 0.125. Stable-context control queries caused zero invalidations. This is a demonstrated redundant call path, separate from a measured gameplay FPS result.

## Focused correction

`SharedFormStats` now compares the actual declarative payload after the inexpensive projected-systems signature changes. Stable queries do not allocate or rebuild the payload. Identical supported payloads retain their existing stage and health caches while native stat-generation invalidation remains immediate.

The comparison distinguishes native inspiration from native ideology/role modifiers, because their offsets apply at different positions in native stat calculation. It also retains source ideology, inspiration, role and precept identities, plus source apparel identity/order and primary weapon identity. Real source/item replacements therefore retain conservative invalidation; only the temporary body-ideology identity is eligible for the no-op path.

The optimization declines to declare equivalence for conditional ideology affecters, hidden/fallback gear, or native-only physical scopes. Those paths retain conservative existing invalidation. Source gene/trait changes, actual anatomy changes and genuine modifier changes also retain their health/cache updates. No global per-tick stat cache or broad suppression of native invalidation is introduced.

The same isolated 1.7 checks with the revised candidate record **40 → 0** redundant invalidations, with unchanged capacity and pain. **194 isolated native shared-stat checks pass.** They exercise real managed game APIs and isolated pawn subgraphs; they are not a launched game, GUI or full-colony performance benchmark.

Review identified and reproduced two counterexamples in the first, uninstalled implementation: moving an equal numeric offset from inspiration to ideology under a gene factor could refresh the stat to 22 while a custom dependent capacity stayed at 21; replacing native-visible bonus gear could refresh the stat to 24 while capacity stayed at 22. The revised implementation passes both at 22/22 and 24/24. Separate application categories and conservative source/item identity guards address those cases. Their negative and corrected results remain preserved separately.

## Other selected-UI work located

| Path | Actual repeated work | Status |
| --- | --- | --- |
| Native health tab | Capacity display reads `GetLevel`, then directly calculates capacity again for the label | Code confirmed; cost not yet attributed |
| Shared anatomy | Cache validation scans source/body hediff lists before checking the installation signature | Code confirmed; cost not yet attributed |
| Native health gizmos | Controlled forms enumerate each injury/hediff and its comp gizmos every frame | Code confirmed; cost not yet attributed |
| RimHUD health hover | Grouping, sorting and text generation occur through the real health-tooltip getter | Code confirmed; current fixture can isolate this workload |
| Abilities and quest/comp callbacks | Command enumeration and enabled/disabled checks can invoke repeated native or external callbacks | Candidate boundaries; no new all-enemies loop established |

No speculative filters are added to those paths: removing seemingly empty injury/comp gizmos could hide mod commands, and caching only hediff counts could miss same-tick replacements.

## Validation and remaining work

- Production candidates and native regression helpers compile for both 1.6 and 1.7 in external output. API metadata validation resolves **172 Harmony targets and 24 injected fields per engine**.
- New initialized-game checks cover no-op personal contexts, actual precept changes, long-lived stat caches, custom stat-dependent capacities, conditional effects, native capacity recursion guards, pain genes and real bionic anatomy. Building the helpers is distinct from executing them.
- The post-hit selection tool first runs real combat and requires actual shots, hits and injury/armor wear. It then pauses and measures selected/deselected/reselected UI against the exact same wounded state, camera and game tick. Coarse HUD, gizmo, health-tab, rendering and modifier buckets are separately optional. This removes the earlier selected-phase/volley timing confound.
- The initial attempt to launch that new tool was refused while the user's ordinary RimWorld was running. After the user saved and closed it, the initialized-game and post-hit checks below were completed in private profiles.
- Candidate builds, snapshots, console results, API logs and fixture manifests are retained under `C:/Users/집/Desktop/codex/outputs/wildshift-selection-deep-20261117`. The previous CPU4 source and DLLs are preserved in `before-cpu4`.

No Steam/GitHub upload is performed. A source-level fix and isolated checks must not be presented as a verified resolution of the original selected-only combat slowdown.

## Completed native checks and an additional clock fix

The first actual 1.6 run passed its 735 assertions but failed the strict wrapper after a restored world form entered `Pawn_InteractionsTracker.TryInteractRandomly` with `Map=null`. Native Pawn code in both versions already calls interaction intervals only while spawned; `SharedFormBiology` redundantly called them again without that guard. The extra manual call and its unused clock field are removed. This prevents duplicate map intervals when a tracker exists and map-only social calls on restored off-map forms. It is a demonstrated runtime/clock defect, separate from the unconfirmed selected-combat FPS report.

The final candidate passes **751 checks on each of actual 1.6.4871 and 1.7.5011**, with all five DLCs, through three fresh-process save/reload phases. Both strict results have zero failures/native errors and preserve normal configuration. New map/world checks observe 20 native intervals with 20 social intervals on the map, then 20 native intervals with zero social intervals off-map; reviewed gene callbacks, canonical age and food progression continue. Optional Dinosauria and the full 87-mod profile are not part of these two regression profiles.

The clock fixture initially assumed the source's pre-despawn interaction tracker survives native despawn. Diagnostics established that native despawn clears it and spawn creates the body's tracker. The corrected fixture asserts that lifecycle, then explicitly restores a retained native source tracker as a labeled legacy input and invokes the actual production binding. It uses real map/caravan ticks and counts callbacks without replacing their results. Initial fixture failures are preserved. A separate dangling `IdeoRoleLost` memory on a removed temporary test role was cleaned up on that test pawn, without suppressing log messages.

Accepted native runs are `native17-v3-trackerinput` and `native16-v3-trackerinput`. The failing 1.6 `native16-v2`, tracker-precondition failures and original candidate manifests remain unchanged for diagnosis. Final production hashes are recorded in `semantic-cache-candidate-manifest-v3.json`; the isolated 194-check result tests the unchanged stat-fix source.

## Post-hit GUI comparison

The same helper first allows 3.5 seconds of real native combat against 60 rifle hostiles, requires shots, hits and injury/armor wear, and then freezes the resulting exact state for three three-second phases. The actor remains alive, standing and drafted, all 60 hostiles remain alive, all measured tick deltas are zero, and raw state/camera remain identical within each run.

| Build | Actual warmup hits / injury sum | Selected FPS | Deselected FPS | Reselected FPS | Mean OnGUI call, selected / deselected / reselected |
| --- | ---: | ---: | ---: | ---: | --- |
| Previous CPU4, plain | 78 / 287 | 60.3 | 60.0 | 60.0 | 1.116 / 0.434 / 0.546 ms |
| Final v3 candidate, plain | 64 / 215.5 | 57.6 | 60.0 | 60.0 | 1.601 / 0.840 / 0.940 ms |

Both bounded sequences pass with no new native errors and unchanged normal profiles/source saves. Damage and resulting wound complexity differ across processes, so these numbers are not a controlled speedup percentage. The reselected phase does not exhibit the reported severe collapse in this local profile. An earlier two-second attempt produced shots but no damage before freezing, correctly failed its prerequisite and is excluded.

A separate CPU4 coarse diagnostic run also passes at about 60 FPS throughout. Its Health tab and gizmo methods account for repeated selected work: shared stat refreshes occur roughly 17,800 times per selected phase versus 1,275 while deselected, but the shared stage builder runs **zero times in every frozen phase**. Native health-tab allocation is about 54–55 MB per selected three-second phase. Method buckets overlap and can include work outside the measured Root.OnGUI wrapper, so their inclusive times must not be summed or presented as a partition of GUI time. This is attribution evidence, not proof that native health UI or RimHUD alone caused the reporter's problem.

The copied colony already has Kiiro content, NewRatkin save-dictionary and FactionTraits world-pawn errors, retained in its strict wrapper failures. Bounded GUI passes do not certify that whole mod pack. Network-generating features remain disabled only in the private copied settings. The source fix is verified; full resolution of the reporter's extreme selection-only slowdown remains unconfirmed.

The final tested 1.6/1.7 DLLs and local documentation are reflected in the repository and normal local mod installation. Previous runtime files are backed up under `before-local-install-v3`; unrelated installation files are preserved. No game is left running and no remote publication is performed. `local-install-v3.json` and `runtime-archive-v3.json` record the installed/package hashes.
