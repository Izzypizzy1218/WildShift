# Inventory fallback and Animal Equipment — v1.2.14

## Return behavior

An animal-to-human return first tries to drop the animal's apparel (including locked apparel), equipment, inventory and carried item nearby. If any items cannot be dropped, only those remaining items move into the preserved human's inventory and the return continues. Items already placed on the ground are left there. Animal clothing is never equipped on the human; the human's original worn clothing is retained.

No-map returns, including caravans, use the human's inventory directly. If human placement fails after item transfer, the items stay with the stored human for retry. A return stops only if remaining items cannot be preserved in either destination or the human body cannot safely be placed. This keeps the loss/duplication protections while avoiding cancellation just because ground placement failed.

This updates transformation return behavior. Initial latent awakening still follows its existing possession-placement rules.

## Optional supported apparel

Animal Gear (`Dylan.AnimalGear`) and Animal Equipment (`Owlchemist.AnimalGear.Equipment`) are optional integrations for preserving animal-form clothing. WildShift now declares that it loads after these packages. Install their required dependencies separately; WildShift does not install, bundle or require either mod.

Animal Equipment supplies animal apparel through the Animal Gear framework. Its published XML uses native apparel definitions and animal/species tags, so the shared native apparel-owner drop/transfer path preserves those objects without recreating them or requiring a separate item conversion. The repository also declares Vanilla Expanded Framework as a dependency. Use versions of the framework and content suitable for your game.

Support scope: retain the equipped objects when returning, drop them nearby when possible, and move leftovers to the original human inventory otherwise. Supported animal species and visible artwork are controlled by the equipment definitions and rendering framework. This release does not claim the earlier Animal Gear/Giddy-up display report is reproduced or fixed.

The Animal Equipment author's FAQ explicitly describes universal/placeholder armor as usable without custom artwork and therefore invisible. Such an item can be working as intended even when armor does not appear on the animal. A species-specific item with artwork that fails to draw still needs investigation with its exact animal/item and mod versions.

Sources checked:

- [Animal Equipment Workshop description and FAQ](https://steamcommunity.com/sharedfiles/filedetails/?id=2568865984)
- [Animal Equipment metadata](https://github.com/Owlchemist/ohgodspidersno-rimworld-vanilla-animal-armor/blob/master/About/About.xml)
- [Animal Equipment definitions](https://github.com/Owlchemist/ohgodspidersno-rimworld-vanilla-animal-armor/tree/master/Content/Defs)

## Validation

246 stability assertions, 89 existing logic assertions and 17 awakening-race assertions pass: 352 total. Eight additional assertions cover map inventory fallback, partial ground placement, a drop exception, failure of both destinations, placement rollback/retry and lethal return. The production assembly builds against the installed RimWorld 1.6 libraries; native API/XML checks pass.

These are code/model validations. Animal Equipment source definitions were inspected but its full live rendering combination is not installed in the current game profile.
