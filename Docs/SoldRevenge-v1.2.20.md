# Sold shapeshifter revenge — v1.2.20

## Schedule and strength

Revenge is now scheduled **120–150 game days after sale**. The default probability remains **50% per original pawn**, adjustable in WildShift settings. The sold original returns with ordinary animals of its assigned species.

Immediately before the force is generated, WildShift reads `StorytellerUtility.DefaultThreatPointsNow(targetHomeMap)` and gives the whole force a budget of **twice that value**. This is the current native baseline, incorporating colony wealth, pawns, difficulty and adaptation. It is not an average of past raid events, a value frozen at sale, or a new random raid-strength roll. Vanilla home maps use a constant incident-points random factor of one.

The original leader counts as one animal of its assigned kind. The total number is `round(2 × current baseline points / animal combat power)`, with a minimum of two (the leader plus one companion). Half-animal ties round upward. The companions are the remaining count. For example, 1,000 baseline points and 100 combat power produce a 2,000-point force: one original leader plus 19 companions.

There is **no fixed five-animal upper limit** and no artificial 100-point floor on an animal's combat power. Strong species require fewer animals; weaker species require more. Whole animals mean the resulting combat power can differ from the budget by up to half an animal, except when the two-pawn minimum applies. Invalid nonfinite/nonpositive points or animal power reject that arrival attempt instead of generating an invalid count.

The budget is recomputed if an arrival has been delayed. Peaceful difficulty, high map danger, missing home maps, downed/mental-state pawns and live trader/caravan/container ownership still delay an arrival. The existing three-day separation between revenge arrivals and one-day retry after failed placement remain in place. The 120–150 day range is the scheduled date, not a forced extraction from another active game object.

## Existing saves

Pending v1.2.19 records are upgraded once on load. Their original scheduled date moves **105 days later**, preserving the original random roll: 15 becomes 120 days, and 45 becomes 150. Expiry moves by the same 105 days and remains 90 days after the new original due date. Because v1.2.19 did not save a sale timestamp and could overwrite the next-attempt date, migration recovers the original due date from its fixed expiry timestamp. A saved schedule-version field prevents repeated postponement on subsequent loads.

New sales use the new schedule immediately. Already completed raids are not replaced or resized. Buyback, player ownership/captivity, death, expiry and the 0% setting keep their cancellation behavior. Identity, assigned species, possessions handling and temporary world-GC protection use the existing implementation.

## Validation

The production budget helper has boundary checks for leader cost, growth after sale, species combat power, removal of small fixed caps, whole-animal rounding and invalid values. Isolated RimWorld 1.6 profiles exercise new-sale scheduling, early-arrival rejection, changed threat points at arrival, actual army size/power, save/load, native combat and return to the same original pawn. A separate profile loads a real v1.2.19 pending-save record, checks the 105-day migration, saves it, then reloads to check that the date does not move a second time.

The player's normal configuration and saves are not used for these tests. Engine checks cover logic and AI; they do not constitute a new visual or full optional-mod compatibility audit.

Release validation passed **58 engine checks with Core, 58 with all five DLCs, and 41 through the real v1.2.19 migration (157 total)**. The fresh profiles spawned 13 and 20 total wargs, respectively, matching their arrival-time doubled budgets within whole-animal rounding. Native attack attempts, pack movement and return to the same original human succeeded after another save/load. The legacy profile also completed its migrated raid and preserved the original pawn.

Separate production-helper tests passed 15 budget assertions; existing logic/race/stability suites passed 89 + 17 + 246 assertions. Build and native API/XML validation passed. Evidence is retained under `wildshift-releas/verification/SoldRevenge-v1.2.20` in the development workspace.
