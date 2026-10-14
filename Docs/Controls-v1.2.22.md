# Mixed selection and native orders — v1.2.22

Drafted transformed animals now use RimWorld's native right-click menu generation. WildShift no longer intercepts `Selector.HandleMapClicks` to force a melee job on the first object under the cursor. Native providers decide whether to move, attack, open an object or offer another supported interaction. In particular, a friendly object does not become an automatic attack merely because only transformed animals are selected. The existing explicit melee-attack gizmo remains available for deliberate targets.

Native rules still govern reachability, manipulation, violence restrictions, drafted/undrafted availability and multiple-pawn support. This change does not grant all human work capabilities to animal bodies. Other mods' native menu providers remain in the menu-generation path.

Native attack menus also assume an equipment tracker exists when deciding between ranged and melee attacks. A narrow `FloatMenuUtility.UseRangedAttack` prefix returns false for transformed animals whose equipment tracker is null. Forms with an equipment tracker retain the native ranged/melee decision, as do ordinary pawns. This avoids adding synthetic equipment trackers or bypassing native attack eligibility.

Map drag selection adds player-owned animal forms with a stored original to the native colonist-priority predicate. It preserves the original rectangle calculation, portrait handling, Shift-add behavior, sorting, selection limit and other priority branches. Ordinary livestock and hostile/released forms are not granted colony selection priority. A form by itself also takes priority over nearby ordinary animals, just as a colonist does.

The patch resolves the compiler-generated `IsColonist` local function inside `Selector.SelectInsideDragBox` by semantic name and exact `bool(Thing)` signature, without its numeric compiler suffix. An unavailable or ambiguous target disables this patch with a warning instead of patching an unrelated selection function. The native API verifier checks the installed game's target as part of release validation. No global `Humanlike`/`Animal` classification override or per-tick scan is added.

The existing custom animal draft button already uses the same native grouping key as the colonist draft button. Its grouping behavior and selection preservation are validated with native gizmos; no parallel drafting system is introduced. Transformation and return continue to replace the selected body while retaining the other selected pawns.

Implant/prosthetic persistence is outside this update. No save migration or new saved state is required for these control changes.

## Verification scope

The isolated engine fixture supplies a rectangle and modifier state to the game's actual drag-selection method, enumerates actual pawn gizmos and native menus, dispatches native menu actions and runs real game ticks. It uses separate test profiles and candidate/helper mods. These fixtures are not included in the release ZIP, and they do not modify the user's normal save or mod configuration. Tests initialize UI dimensions and skip only float-menu font measurement because batch mode has no normal OnGUI skin/font context. This is an engine behavior test, not a screenshot-based rendering check.

The checks cover mixed human/warg/horse selection; exclusion and independent selection of ordinary livestock; enemy-form exclusion; portrait rectangle selection; Shift-add selection; grouped draft/undraft; native opening, group movement, queued movement and hostile attack orders; deliberate attack-gizmo availability; and selection/body identity across transformation, return and save/load.

## Results

RimWorld 1.6.4871 rev591 passed the isolated Core-only and all-five-DLC profiles, including separate process reloads. The evidence contains 63 Core and 66 all-DLC assertion executions (including the repeated checks after reload). The three additional equipped/unarmed/single-form attack checks were added before the Core reload and both all-DLC runs. Final logs contain no exception or float-menu worker errors.

The native engine confirmed that a drafted warg opens a loaded friendly cryptosleep casket without damaging it; the human, warg and horse move under native group orders; a second movement order remains queued; and native hostile attack orders reach both single and mixed selections. Temporarily equipping a form with a ranged weapon also retains the native ranged decision. The test's equipment is generated only in its isolated profile.

The existing logic/race suites passed 89/17 assertions and stability passed 243. Three old stability assertions requiring the removed forced-right-click attack were retired; native engine tests cover the replacement behavior. The API audit passed 39 Harmony targets (including the resolved local predicate), 10 injected fields, 10 exact enemy signatures and 14 XML files. Source diff whitespace checks passed.

During fixture development, batch UI initialization/font measurement and a roaming test target required fixture adjustments. Attack-path inspection also exposed the genuine null equipment-tracker issue fixed in this release. Final passing profiles use the final production DLL; earlier diagnostic runs are not presented as passing evidence.

Draftable Animals is not installed in the test profiles. Compatibility with that specific mod is not claimed from these tests; its interactions should be checked with the exact installed version before making a joint-runtime compatibility claim.
