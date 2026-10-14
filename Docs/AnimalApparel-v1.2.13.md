# Animal-form clothing preservation — v1.2.13

## Return behavior

The animal body used to be destroyed after releasing its preserved human without unloading its possessions. Animal clothing could disappear with that temporary body.

Before any normal, forced or lethal return commits a human destination, the animal now unloads its native apparel, equipment, inventory and carry trackers. On a map, belongings are dropped nearby with locked apparel included and ordinary drops unforbidden. The human's original worn apparel remains on the stored human and is not dropped or replaced.

Away from a map (including caravans and containers), animal possessions move to the original human's inventory. Animal clothing is cargo, not human apparel. A failed return keeps the original form and its stored human. Successful partial drops remain on the ground; successful partial transfers remain in the stored human's inventory. Retries do not create replacement items. Off-map transfers disable stack merging and attempt to restore a detached original item when a transfer callback throws.

An emergency cleanup also refuses to delete a transformed shell that still owns items. After emergency human recovery on lethal damage, a retained shell proceeds through native animal death, preserving the remaining apparel on/in its corpse. As with native death, other mods that explicitly destroy or consume items can alter this behavior.

## Animal Gear / Giddy-up visual report

The reported Animal Gear and Giddy-up versions are not installed or enabled in the current development profile. Display failure has not been reproduced. WildShift currently does not replace animal render trees, graphics or apparel render nodes. Native apparel-change notifications already invalidate graphics; adding another blind per-frame redraw would not establish a fix.

References inspected, separately from the local production code:

- Animal Gear's author lists version 1.6.0.1 on its [Workshop page](https://steamcommunity.com/sharedfiles/filedetails/?id=1541438907).
- The public [Owlchemist Animal Gear fork](https://github.com/Owlchemist/animal-gear) targets 1.4, so its old rendering patches are not evidence of the current 1.6 implementation.
- The [MemeGoddess GiddyUp2 source](https://github.com/MemeGoddess/GiddyUp2) has rendering integration for Animal Apparel: Framework. That is a separate package from Dylan's Animal Gear; one cannot assume that integration applies to both.

Still needed for a targeted visual fix: exact Workshop variants/versions, the animal species and armor item, whether the failure affects map rendering or only the colonist portrait, and ideally the relevant Player.log or a reproduction save. No rendering fix or full compatibility claim is made in this release.

## Verification

The fault-injecting suite exercises ground drops, original human clothing retention, silent drop rejection, human placement failure after unloading, caravan transfer, all four possession trackers, silent inventory rejection, container swap rollback, exceptions before and after item addition, retries and lethal emergency cleanup. Production compilation uses the installed RimWorld 1.6 libraries. These model tests do not run Unity rendering or replace live mod-combination testing.

Results: 238 stability assertions (including 18 new possession assertions), 89 existing logic assertions and 17 awakening-race assertions pass: 344 total. The production build and all 31 native Harmony targets, 10 injected fields and 11 XML files pass validation.
