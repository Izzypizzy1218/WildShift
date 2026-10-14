# Enemy shapeshifters — v1.2.1

## Behavior

- Successful native enemy raid generation rolls once per eligible humanlike raider. Default chance: 5%; settings allow 0–100%. Existing shapeshifters keep their assigned form.
- A marked raider transforms when its Lord receives native `AssaultColony` duty. Siege construction, breaching and sapping are not forcibly replaced with animal behavior.
- The animal inherits the raider's faction, gender rules, assigned form and Lord membership. Native raid duties choose attacks and retreat. This feature does not change melee verb cooldowns or animation.
- Nonlethal downing of a hostile combat form returns the original human before the map's next Lord update. It resumes normal AI with the weapons/apparel and injuries it already had. Animal injuries are not copied by this nonlethal return.
- Transformation is spent once per raid encounter, including a failed generation attempt. Repeated duty updates and save/load cannot provide endless animal bodies. If the same human later participates in a newly generated raid, it may transform again without rerolling its form.
- A truly lethal hit still uses the configured human-body death chance. Slaughter/execution still kills the human. Allied/player downing behavior is unchanged.
- Captured, hosted, enslaved, recruited or no-longer-hostile forms stop using enemy-only AI/downing logic.

## Safety and performance

Raid generation and assault-duty callbacks are event hooks. A per-map component holds deduplicated transient queues. `MapPostTick` processes queued work before native Lord ticking, outside health/job iterations. Empty queues do not allocate batches or enumerate pawns. One map-pawn scan reconstructs queues during load initialization.

The pending flag is set before native downing notifications. Only `Incapped` loss and the paired Lord downed callback are deferred while a human is stored and a return is pending. Kills, capture and other loss conditions are not suppressed. Failed placement rolls back and releases normal raid-loss notification; it cannot indefinitely keep an incapacitated raider counted as active.

After destination placement succeeds, the source's slot in `Lord.ownedPawns` is replaced, together with the `lord` pointer and duty. This avoids increasing raid-size counters or ending a solo raid during the swap. The stored human is not a second active raider.

Native downing clears `mindState.duty`. Starting in v1.2.1, a missing duty after the swap is reassigned by the current LordToil; an obsolete assault order is not forced over a retreat. The enemy duty subtrees check actual duty presence during the pending-return gap. Loading a v1.2.0 save repairs missing duties on hostile marked humans and animal forms once, without resetting their one-use state. Third-party toil exceptions are logged but do not interrupt cleanup of an already-committed body swap.

The health hook temporarily sets `overrideDeathOnDownedChance` to zero only on eligible enemy forms. A Harmony finalizer restores it even with nested health calls or exceptions. Native vital-organ destruction and true lethal checks are not bypassed. Kinds with `forceDeathOnDowned` are not automatically transformed.

## Scope and limitations

- New native ordinary raids are supported; existing enemies are not retroactively rolled.
- Quest-tagged actors are excluded to avoid breaking signals tied to a specific human. This is not full quest, anomaly, ambush or custom raid-worker compatibility.
- Nonstandard AI bypassing `AssaultColony` does not initiate transformation. Siege/sapper/breacher raiders can transform if they switch to normal assault.
- Native retreat remains possible. Extra animal bodies change raid difficulty without recalculating storyteller raid points.
- Optional animal-pack special abilities and destructive lifecycle components retain the limitations in `AnimalModIntegration.md`. Prosthetics, reproduction, animal armor and generic traversal of the hidden human holder remain deferred.
- Setting assignment chance to zero prevents new random assignments, not existing shapeshifters' abilities.

## Verification performed

- Production C# build against installed RimWorld 1.6.4871/Harmony assemblies.
- 89 existing logic assertions and 88 combined stability assertions using fault-injecting engine stubs. Checks cover assignment filters, faction guards, both AI trees, Lord swaps, solo raid counters, retry prevention, drop-pod arrival, pending downing, true lethal damage, failed placement, nested health override restoration, queue reconstruction and 10,000 empty safe points with no pawn scans. v1.2.1 tests explicitly clear duty on downing, check current-toil restoration/retreat, guard duty execution during pending return and repair saved missing-duty pawns. The corrected regression failed against v1.2.0 before the fix.
- `Tests/Verify-GameApi.ps1`: reflection-only verification of 10 enemy/AI Harmony targets and parameter names, 4 fields, 11 XML files and referenced native think-node/subtree names.
- Stubs do not simulate Unity rendering, full combat, actual Scribe serialization or all mods' callbacks. Pawn generation is a mocked boundary in queue tests. Live gameplay has not been performed.

## In-game checklist (disposable save)

1. Set enemy assignment chance to 100% and generate a small ordinary immediate-attack human raid. Confirm one transformation and colony approach/attacks.
2. Nonlethally down an enemy animal. Confirm the original named human returns with its original weapon, remains hostile and can fight. No second animal body should appear.
3. Repeat with a one-person raid. The swap must not count as raid defeat; downing/killing the returned human must still count normally.
4. Save/reload while transformed, then down the animal. Save/reload after human return and confirm no repeat transformation. Check `Player.log` for reference or Lord errors.
5. Test truly lethal damage at 0% and 100% human death chance, and slaughter separately. Nonlethal downing is a separate path.
6. Test retreat/map exit, siege switching to assault and capture/recruitment. Captured/player forms must not inherit enemy-only downed return or AI.
7. Down an allied/player animal form to confirm the previously rolled-back auto-return behavior has not returned. Verify drafting/R and right-click attacks still work.
8. Restore the desired enemy assignment chance (default 5%) after testing.
9. Load a disposable v1.2.0 save with a returned human showing `ThinkNode_DutyConstant with no duty`. Verify its current raid duty is restored and new errors stop. Test return while the raid is retreating too; it should not force another assault.
