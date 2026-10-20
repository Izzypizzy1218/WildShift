# Full source review — v1.3.0-beta.3 (2026-10-20)

The review found and corrected three actionable issues. Existing transformation storage, possession transfer, awakening, source-species compatibility and save recovery were also inspected. No additional confirmed defect was identified in those reviewed paths; this is not a guarantee of compatibility with every external mod.

## Corrected findings

### Custom rules were skipped for newly generated revenge companions

`RevengeCompanions.Prepare` generated fresh human allies but supplied an explicit preselected form through the non-custom assignment route. Consequently, Human → Cat at 100% could still produce a different animal form during a revenge raid.

When customization is enabled, new companions now use the rule-aware assignment path before their actual animal combat power is charged to the raid budget. The original sold leader keeps its existing identity and form. With customization disabled, the previous explicit assignment and random-call sequence are preserved. Unaffordable or unusable choices still reject and clean up the generated body, removing attempted fallback candidates so rejection remains finite.

Nine regression assertions execute the production companion builder. A new guaranteed-cat test failed against the preceding implementation and passes after the correction. Coverage includes actual budget charges, custom provenance, normal fallback, disabled behavior, failed probability, missing animal definitions and bounded rejection of over-budget choices.

### Mental-state and unspawned forms could be classified as player-controlled

The transformed-animal player-control override omitted the native getter's `Spawned` and `MentalStateDef == null` conditions. A transformed animal in a mental state could therefore receive order menus and queued player commands that an equivalent human would not receive.

The override now retains both native conditions. It preserves already-true native results and the native classification of downed pawns. The original `Pawn.GetGizmos` checks this getter before requesting draft gizmos; the existing Releashed duplicate-draft filter remains in place.

Ten additional checks use actual 1.7 Pawn, Map, health and mental-state objects. They cover normal player forms, mental breaks, recovery, unspawned/despawned forms, order eligibility, downed classification and normal human colonists.

### Static caches retained retired Game instances

`Patch_GameEnder.cachedGame` and `Patch_PawnGizmos.checkedGame` held strong static references to the last Game. Returning to the menu did not clear those mod fields, so the retired Game and remaining reachable managed state could stay alive until later callbacks. The native menu transition already dismantles some Map/World state; this finding does not imply an entire intact colony was retained.

Both caches now use explicitly qualified `System.WeakReference<Game>`. No-current-game calls skip scans and recovery work. The 120-tick cache, explicit invalidation, game identity isolation and once-per-game scenario recovery are preserved.

A production-callback GC harness returned 20 passes and 3 failures before this correction, then 23 passes and 0 failures afterward. It tests whether retired Game objects are collectible without another callback, rather than inspecting the private field types. It also verifies cache expiry, explicit invalidation, different games at the same tick, and scenario recovery retry/reset behavior.

## Verification

| Check | Result |
| --- | --- |
| Form selection and awakening race tests | 89 + 73 + 17 assertions pass |
| Fault-injection/stability tests | 243 assertions pass |
| Special training and revenge budget tests | 18 + 18 assertions pass |
| Cache lifetime and behavior tests | 23 assertions pass |
| Native 1.7 assembly/API/save/control tests | 163 assertions pass |
| Total assertions | **644 pass** |
| 1.6 and 1.7 Release builds | Zero warnings and errors |
| Actual 1.7 startup, all five DLCs + Harmony + WildShift | Initialization completed; exit 0; no logged errors or fatal stderr |
| Optional-integration Harmony callback harness | Private-field injection and original callbacks preserved; 10,000 callbacks reuse one state lookup |

The 1.6 build uses the cached, permission-published RimRef 1.6.4871 signatures with matching installed Harmony. The installed game is 1.7.4982 rev1280; native runtime and startup verification use that version. Build and engine-stub checks do not establish full 1.6 gameplay or long-term colony behavior.

The optional-integration harness uses real Harmony with modeled VEF/engine callbacks; it is not a complete third-party-mod gameplay test and its callback loop is not added to the assertion total. The actual startup check uses an isolated save/config profile. All 45 normal user-configuration file hashes remained unchanged.

Local test output and installation manifests are retained outside the source repository under `outputs/wildshift-1.3.0-beta.3`. The previous installation, source snapshot and versioned beta.2 archives are preserved for rollback.

## Remaining boundary

The historical 1.7 CLR/JIT startup crash remains a separate unresolved observation. These corrections do not claim to fix that native crash. See [the recorded investigation](Startup-1.7-2026-10-19.md). Third-party direct destruction, arbitrary external holder traversal and full migration of an entire 1.6 colony still require their own integration testing.
