# Code review and optimization — v1.2.3

Reviewed on 2026-09-06 against the local RimWorld 1.6.4871 rev591 installation. Scope: all 33 production C# files, runtime XML/translation structure, ownership transitions, AI/duty lifecycle, cache/tick paths and test coverage. Earlier uncommitted v1.2.0–v1.2.2 work was retained. No game saves, probability settings, animal compatibility rules or combat animation/cooldowns were redesigned.

## Confirmed findings fixed

| Priority | Finding | Fix and evidence |
| --- | --- | --- |
| P1 | Taming revelation destroyed the original animal before spawning the generated human. A failing spawn callback could leave no original body on the map. | `Patch_Taming.RevealTamedShapeshifter` now prepares and validates the human first, uses transactional placement and only then removes the animal. Injected generation, null-return, assignment and pre/post-registration failures are covered by tests of the production method. Deferred execution also rechecks faction/form state. |
| P1 | Reversion through a caravan/world holder transferred the human to the owner but omitted world registration. Native `Caravan.CheckAnyNonWorldPawns` removes such members on its next tick. | `TransformUtility.TryReplaceInHoldingOwner` now mirrors the original animal's world registration through `FormTransferUtility.TryRegisterWorldPawn`. A registration failure restores the original animal and stored human. Successful, failed, retried and post-registration exception cases are tested. Ordinary non-world holders are unchanged. |
| P2 | Direct melee submission did not reject despawned/destroyed/different-map targets or an incapacitated/nonplayer attacker. Deferred target selection could therefore submit a stale job. | `TransformUtility.TryOrderMeleeAttack` now validates these conditions before creating the native job. Tests compile the actual method, check rejection and verify native direct-attack flags. This does not establish that stale submission caused the user's earlier path-node error. |

## Optimization and tick review

- `EnemyShapeshifterUtility.IsCombatForm` now rejects factionless, player-owned and dead/destroyed animals before looking for the transformed hediff. A stub counter confirms 30,000 such checks perform zero transformed-hediff lookups. Eligible hostile transformed animals keep their behavior.
- The enemy map component checks two pending sets at its safe point; it does not enumerate all map pawns every tick. It allocates a batch only when there is queued work. Load-time repair scans pawns once. Existing tests exercise 10,000 idle safe points without pawn enumeration.
- Colonist-bar scans remain gated by vanilla's dirty cache. Existing tests exercise 1,000 clean-cache calls without pawn enumeration.
- Game-over protection uses a 120-tick cache, explicitly invalidated during transformations/returns. It checks player pawns across maps, caravans and traveling transporters rather than every map animal.
- Lone Beastkin startup fallback checks every 30 ticks only until its startup assignment/recovery completes; subsequent ticks return immediately. This is not the removed periodic join-chance system. Empty/custom no-candidate scenarios can keep retrying, so this is not a claim that every unusual initialization is scan-free.
- Animal-kind eligibility is cached by definition count and insect setting; racial assignment is event-driven. No new permanent pawn-reference cache was added because invalidation across faction/health/save transitions is error-prone.
- No live FPS/TPS or memory profiler was run. Reduced lookups are verified structurally and with operation counters, not a claimed percentage performance gain.

## Remaining compatibility risks / design decisions

1. **Hidden-human holder visibility (compatibility risk):** the human is deep-scribed through the transformed hediff's ThingOwner, but native `Pawn.GetChildHolders` enumerates inventory/carry/equipment/apparel, not this health-comp holder. Generic third-party holder traversal can overlook the stored human or its equipment. The registry correction above does not solve that broader traversal/mass/quest integration question. Exposing it globally could also double-count bodies, so that requires a separate integration design.
2. **Direct destruction and exotic animal lifecycle (compatibility risk):** WildShift protects its own cleanup and intercepts `Pawn.Kill`; it does not universally intercept every third-party `Destroy`, metamorphosis, swallowing, replacement or special-ability callback. A mod destroying an occupied form outside the supported flow is not certified safe. This pass does not globally suppress destruction or change death semantics.
3. **Manual shift while a human is downed (confirmed behavior; design decision):** human shift availability and the core transform guard check alive/spawned state but do not reject `Downed`. A manually commanded downed human can therefore use a fresh animal body if other conditions allow. Whether this is an escape ability or an exploit needs a gameplay decision; it was not silently disabled here.
4. **Prosthetics, reproduction and animal armor:** remain the user's deferred design topics. Fresh animal bodies and original human-body preservation do not imply bidirectional persistence of all animal equipment/health changes.
5. **Path-node exhaustion:** the reported `Thrumbo43097 ... ran out of path nodes` remains unattributed. Native pathing and attacks already in progress are not rewritten by the input guard. Do not treat the absence of a new log entry during static testing as proof of a fix.
6. **Existing support boundaries:** quest-tagged actors and custom raid workers/toils remain outside guaranteed enemy-auto-shift support. Ordinary raids using AssaultColony are the tested code path. Runtime animal special abilities and other mods' callbacks still require live integration tests.

## Verification

- Production DLL compiled against installed game/Unity/Harmony references.
- 89 logic assertions plus 111 stability assertions passed (200 total). These include real production methods with fault-injecting engine stubs, not a complete RimWorld simulation.
- Metadata-only inspection confirms all 26 declared Harmony targets and 10 injected private fields exist; 10 enemy/AI method signatures are checked exactly. The overloaded PawnGenerator target is resolved by its PawnGenerationRequest parameter.
- 11 runtime XML files parse; native think-node/subtree references resolve; duplicate mod Def names and English/Korean Keyed entries are checked.
- Local enemy setting remains 10%; compiled ordinary-starting probability remains 5%. Other existing probability values are unchanged.
- No live gameplay, real Scribe save/load round-trip, caravan journey or optional-mod matrix was executed. Packaging/installation hashes are checked separately during release installation.

## Recommended live checks

Use a disposable save: tame a marked animal; return a transformed member inside a caravan and advance several world ticks; repeat with a traveling transporter; save/reload both states. Verify the human remains a member and no `not a world pawn` or missing-reference errors appear. Then test enemy animal downing and human combat/retreat, ordinary draft/R/right-click controls and single-colonist game-over protection. Keep the unresolved direct-destruction/exotic-animal cases separate from the ordinary gameplay result.
