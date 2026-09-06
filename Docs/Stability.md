# v1.1.2 stability pass

## Scope

The transfer path now prepares the destination before discarding the source. A failed human placement restores the human's animal-side storage; a failed container replacement attempts to restore both the animal and its human. An exception after destination registration is treated as a committed transfer, avoiding a second copy of an already placed pawn. Cleanup refuses to destroy an animal that still contains its human.

If lethal-damage reversion fails, the death patch attempts to retain the human as a world pawn. This is emergency preservation, not a guarantee of immediate return to the map. Slaughter/death decisions still apply to that recovered human. If world registration also fails, the occupied animal is retained and errors are logged. Arbitrary failures in third-party callbacks are not guaranteed recoverable.

Colonist-bar scans run only when its vanilla entry cache was dirty. No new tick callback is introduced. Successful animal-only direct melee clicks remain menu-free; mixed human/animal selections use the unchanged vanilla attack context. The obsolete menu-removal patch has been deleted, so unsuccessful direct orders can also fall back normally.

The Lone Beastkin scenario remembers the scenario-created hediff for each candidate in a weak-key cache. Reordering a candidate back into the designated slot restores its existing form instead of rerolling it. Preexisting shapeshifters not created by that preview are never removed by it. Preview-only cache state is not serialized; the active pawn hediff continues to use the existing save format.

## Automated verification

Run each suite in a fresh process:

```powershell
pwsh -NoProfile -File Tests/Run-LogicTests.ps1
pwsh -NoProfile -File Tests/Run-StabilityTests.ps1
```

- Logic suite: 85 assertions, including real assignment/gender/preview logic with engine stubs.
- Stability suite: 31 assertions using real transfer helpers, reversion methods, death patch, direct-click patch, and colonist-bar patch. Storage, world registry, rendering, and other engine services are fault-injecting stubs.
- Injected cases: preflight rejection, storage failure, spawn failure before/after registration, container insertion failure before/after registration, world transfer failure, retry, occupied-shell deletion refusal, reentry, and slaughter recovery.
- UI checks: mixed selections are left unchanged; successful direct attacks consume the click; failed direct orders allow fallback; 1,000 clean-cache calls perform zero pawn-list scans; dirty recache inserts one entry without duplication.
- Production compilation uses the installed RimWorld 1.6 and Harmony assemblies. Stub assertions do not replace a real game session or measure FPS/TPS.

## In-game checklist

1. Back up a save. Transform/revert repeatedly, save while transformed, reload, and revert. Confirm one original human, correct faction, health, gender, and assigned form.
2. Check the portrait in a solo-colonist map and a caravan, including map changes and portrait reordering.
3. Select a drafted human and transformed animal together; order an attack. Separately confirm animal-only right-click attacks still show the direct melee feedback marker without an extra menu.
4. Reorder a Lone Beastkin candidate away from the first selected slot and back. Its animal form should stay the same. Check preexisting shapeshifter candidates remain marked.
5. Test reversion from a transport holder and a caravan. Confirm the original human replaces the animal without duplication.
6. Test lethal combat and slaughter on a disposable save. Combat uses the configured death chance; slaughter kills the human. Downing alone still does not automatically revert.
7. Review Player.log for transfer/rollback errors, especially with mods that alter spawn, faction, container, or death callbacks.

## Intentionally unchanged

- Prosthetics and animal equipment preservation, and animal reproduction behavior remain deferred design decisions.
- The stored human still serializes through `HediffComp_Transformed`'s private ThingOwner. Generic `Pawn.GetChildHolders` traversal does not enumerate this health-comp holder. Exposing it globally without auditing all consumers could cause the two bodies to be counted together by colonist, mass, or caravan logic; this pass does not claim to solve that integration issue.
- Existing animal forms, 20% racial preference (Ratkin 10% rat / 10% Hamstrox), 1% starting-candidate chance, and 5% normal-join chance are unchanged.
