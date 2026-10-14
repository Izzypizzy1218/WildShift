# WildShift

WildShift is a RimWorld 1.6 mod about colonists who can transform into an assigned animal form and later return to their preserved human body.

## Features

- Transform a shapeshifter into one eligible animal form before or after drafting.
- Keep transformed animals visible and selectable in the colonist bar.
- Draft and directly control the transformed animal.
- Enter animal form with the species' supported special training and required prerequisites completed. Special-capable forms retain their training, including after loading existing saves; hive queens spew eggs and sludge only when commanded.
- Use the normal draft hotkey (`R`) for transformed animals.
- Use the single native melee attack command (`H` by default); transformed forms do not add a second attack button. Apparel skills such as NewRatkin's shield-facing command remain available.
- Use the native right-click movement, attack and interaction menus for transformed animals, including mixed selections with human colonists. Use the explicit attack command for deliberate attacks on colony objects.
- Drag-select player-owned transformed animals together with colonists; ordinary livestock retain the normal selection rules.
- Keep one grouped draft button on transformed animals when Draftable Animals - Releashed is active, including attack-trained forms and its no-training setting. See `Docs/DraftableAnimals-v1.2.23.md` for the tested 1.6 binary and the incompatible legacy 1.5 download.
- Return to the stored human body with the revert command.
- Drop the animal form's worn apparel, equipment, inventory and carried items nearby before returning. Anything that cannot be dropped goes to the original human's inventory; off-map returns keep all items there. Original human clothing remains worn.
- Preserve Animal Gear / Animal Equipment apparel during return, including inventory fallback when nearby ground cannot accept it. Visuals remain subject to the equipment's species support and textures.
- Prevent a false game over while a lone colonist is transformed.
- Remove randomly generated diseases and injuries from temporary animal bodies.
- Discover latent shapeshifters through successful normal taming, or through medical-bond taming if the animal has no recorded player-inflicted harm. Normal taming does not require a bonded relationship.
- Select a purchased or already owned latent animal on a map and use **Awaken as human**. Purchases ignore past damage; belongings are automatically placed nearby.
- Every normal human colonist join has a 5% chance to reveal a shapeshifter instead of triggering a separate join event.
- Supported animal-eared races and xenotypes have a 20% preference roll for a matching animal when first assigned a shapeshifter form. The other branch uses the normal pool; existing forms are not rerolled.
- New animal forms match the original pawn's gender. Genderless animal species remain genderless.
- Optional Vanilla Genetics Expanded support preserves hybrid quality and genetic-disease progress through awakening, repeated transformations and save/load.
- Optional Big and Small - Sapient Animals support recognizes each sapient animal's source species and preserves original-pawn storage and possessions through Cogni-Fi. See [compatibility behavior and tests](Docs/Compatibility-v1.2.18.md).
- Eligible humanlike enemy raiders have a configurable 5% chance to become shapeshifters. They transform for a normal colony assault; downing the animal returns the original human to the raid, without a second transformation in that encounter. Player downing behavior is unchanged.
- Selling a colonist in animal form can bring the same shapeshifter back with an army of animals and other shapeshifters. The default chance is 50%, scheduled 120–150 days after sale. The leader, ordinary animals and allies share a combat-power budget equal to twice the home map's baseline raid points at arrival. Allies consume roughly 30% of the companion budget, have their own animal forms and retain separate human bodies. Buyback or recovery cancels pending revenge. See [mixed revenge rules and validation](Docs/MixedRevenge-v1.2.21.md).
- Includes the Lone Beastkin starting scenario and configurable mod settings.
- Moving a scenario-designated starting candidate out of the first slot and back rerolls its animal form. Redraws alone do not reroll; preexisting non-scenario shapeshifters are preserved.
- English and Korean keyed translations are included.

## Requirements

- RimWorld 1.6
- Harmony

## Awakening latent shapeshifters

New awakenings choose the player faction's basic race with a **50%** roll. The other **50%** chooses uniformly among eligible alternative body races and human xenotypes supplied by vanilla and active mods. Each body race gets one slot regardless of how many pawn jobs it defines; with Biotech, each permitted human xenotype gets one slot. The current player race is excluded from the alternative body-race pool. Existing awakened pawns and assigned animal forms are unchanged. See [race selection rules and validation](Docs/AwakeningRaces-v1.2.12.md).

An eligible wild animal with the latent shapeshifter marker awakens in human form when a colonist successfully completes a normal taming interaction. Any colonist who meets the animal's normal taming requirements can do this; no bonded relationship or special colonist role is required. Unmarked animals do not awaken.

Medical care can also awaken a marked wild animal when a newly formed bond automatically recruits it to the colony. Injuries inflicted by wild animals or enemies do not prevent this. Actual damage attributed to a player-owned pawn or object (including colony animals, turrets or traps) blocks medical awakening. A medically recruited animal rejected for this reason also receives a separate manual-awakening block. A subsequent purchase clears that medical-origin block without erasing its damage history. Normal taming remains available. The game does not distinguish deliberate abuse from friendly fire or self-defense: all recorded player-owned damage counts. Completely absorbed attacks do not count.

The harm record survives saving, death and resurrection. History begins with v1.2.5: earlier damage cannot be reconstructed. Existing owned animals are not automatically converted or retried on later treatment. Damage without a player-owned instigator cannot be attributed reliably and is not counted. See [medical awakening behavior and verification](Docs/MedicalAwakening.md).

### Purchased and already owned animals

Select an eligible colony-owned animal with the latent shapeshifter marker and press **Awaken as human**. This includes animals purchased before the update; no purchase-history record or re-taming is required. The animal's species becomes the new human's assigned animal form. Purchases do not automatically replace a pawn during trade.

For caravan or orbital purchases, wait until the animal has arrived and is spawned on a map. Downed or mentally unstable animals cannot use this command. Cargo, carried items, equipment and apparel are automatically dropped nearby; no manual unloading is needed. If an item cannot be safely placed, awakening stops and preserves the animal and remaining items for retry. Purchased animals are not blocked by damage history. Only a recorded player-harm medical recruitment blocks the manual route, and a subsequent purchase clears that block. Before v1.2.7, acquisition routes were not saved, so older owned animals are not presumed medically recruited from damage alone. See [manual awakening and verification](Docs/OwnedAnimalAwakening.md).

### Trader test mode

The default latent chance is **3% for eligible wild animals and 3% for eligible trader animals**. Both use the same ordinary animal-generation setting; trader stock does not receive a second roll. The current local setting is also 3%, and the trader 100% test override is off.

The setting **Test: eligible trader animals are 100% latent shapeshifters** can override newly generated eligible animal stock from the native animal stock generator. It does not change wild-animal odds, eligibility, stock quantity or existing stock. Switch it off to resume the shared normal generation chance. Already generated animals retain their existing markers; lowering the chance does not remove test-generated shapeshifters. The distributed default is off.

## Enemy shapeshifters

Enemy transformation currently supports ordinary raids using the native **AssaultColony** duty. Siege preparation, sapping and breaching remain human tasks; eligible raiders can shift when they switch to a normal assault. Scripted quest-tagged actors are excluded. Custom raid workers that bypass the native generation/assault methods are not automatically supported.

The animal uses native raid combat and retreat duties. Nonlethal downing triggers a safe-point return before the map's next Lord tick, preserving the original raider, equipment and raid membership. Human injuries already present before transformation remain. Lethal hits still use the existing human-body death chance; slaughter still guarantees human death. Allied shapeshifters do not automatically return when downed.

The setting controls new random assignments, not already assigned forms. Setting it to 100% is useful for testing new ordinary raids on a disposable save. No per-tick map-wide pawn scan is added. See [enemy behavior, limitations and test checklist](Docs/EnemyShapeshifters.md).

## Racial animal forms

| Race | Preferred animal |
| --- | --- |
| Ratkin / NewRatkinPlus | Rat (10%) or Hamstrox (10%) |
| Kiiro, Nyaron | Cat |
| Kurin HAR | Red or arctic fox |
| Miho | Red fox; arctic and desert xenotypes use arctic and fennec foxes |
| Revia | Red fox |
| Rabbie, Yuran | Hare or snowhare |
| Bori | Husky or Labrador retriever |
| Sheepawn (Bori) | Sheep |

The preference is rolled **after** a pawn becomes a shapeshifter; it does not change the 5% starting-candidate or 5% colonist-join chances. Matching racial forms bypass the ordinary size/predator/blacklist filters only for the matching pawn. Uninstalled race mods require no configuration or extra dependencies. A missing animal's probability slot falls back to the normal pool instead of being reassigned to another preferred animal. Animals revealed by taming keep their original form.

Ratkin's hamster is NewRatkinPlus's Hamstrox (`Ratkin_KingHamster`). It may also occur in the ordinary animal pool, so its final frequency can exceed the dedicated 10% branch. Existing animal bodies are not changed mid-transformation: revert and transform again to apply gender matching.

These mappings identify exact race or xenotype Def names, not cosmetic ears on arbitrary custom xenotypes. Existing shapeshifters keep their assigned forms. See [integration notes and test scope](Docs/RacialAffinity.md).

## Installation

### Optional animal packs

Vanilla Animals Expanded, Megafauna, Alpha Animals, and Dinosauria feed eligible predator/large-animal definitions into the existing automatic form pool. No animal-pack DLL is bundled or required. Subscribe to the desired packs and their dependencies separately; this mod does not install or enable them for you.

The optional VEF compatibility hook prevents fixed-female animal components from overwriting a WildShift form's generation-time gender; ordinary animals retain their native behavior. See [integration scope and setup](Docs/AnimalModIntegration.md) and the [source-level candidate list](Docs/AnimalModForms.md). Special abilities and full runtime compatibility are not universally guaranteed.

### Install WildShift

1. Download the latest release ZIP.
2. Extract the `WildShift` mod folder into RimWorld's `Mods` directory.
3. Enable Harmony before WildShift in the mod list.

The repository uses RimWorld's standard mod layout. Shared definitions, translations and textures remain at the root; `LoadFolders.xml` selects the compiled DLL in `1.6/Assemblies` or `1.7/Assemblies`. When upgrading a manual installation, replace the old WildShift folder instead of merging it: an old root `Assemblies/WildShift.dll` would load twice.

RimWorld 1.7 support is a beta tested against **1.7.4977 rev1248 (unstable)**. The preserved 1.6 DLL is the v1.2.24 release. Optional mods such as HAR, Releashed and Animal Apparel require compatible releases of their own; the historical 1.6 integration reports below are not 1.7 validation.

## Building

Install a .NET 10 SDK, RimWorld 1.7 and Harmony, then run:

```powershell
dotnet build WildShift.csproj -c Release
```

This builds `1.7/Assemblies/WildShift.dll`. Game and Harmony binaries are references only and are not redistributed. Override `RimWorldDir`, `RimWorldManaged` or `HarmonyAssemblies` for non-default installation paths. See [1.7 build and verification notes](Docs/RimWorld-1.7.md).

The existing 1.6 DLL is preserved rather than rebuilt with 1.7 APIs. To rebuild it, supply real 1.6 references and `-p:RimWorldVersion=1.6`; that target uses .NET Framework 4.7.2.

## Current version

`v1.3.0-beta.1`

See [CHANGELOG.md](CHANGELOG.md) for details.

See [special training and discovery-letter validation](Docs/SpecialTraining-v1.2.17.md) for automatic species training, existing-save migration and the hive-queen fix.

See [inventory fallback and Animal Equipment support](Docs/AnimalApparel-v1.2.14.md) for current clothing preservation, optional load order, and rendering limitations.

See [Animal Apparel runtime audit and terrain-tree compatibility](Docs/AnimalApparel-v1.2.15.md) for the current Framework / Basic Armor / Universal Basic Armor results. Visible armor still requires artwork for that species; this integration does not create missing textures.

See [apparel refresh recovery and live map validation](Docs/AnimalApparel-v1.2.16.md) for the missed-notification repair. The original reporter's mod conflict has not been identified; the verified recovery condition is documented separately.

See [stability changes and validation scope](Docs/Stability.md) for rollback safeguards, regression tests, and the remaining in-game checklist.

See [the v1.2.3 code review](Docs/Review-v1.2.3.md) for confirmed fixes, hot-path optimization and remaining compatibility/design risks.

See [v1.2.10 recovery fixes](Docs/Recovery-v1.2.10.md) for caravan rollback and awakening cleanup safeguards identified in the v1.2.9 review.

See [the v1.2.11 performance and compatibility pass](Docs/FinalPass-v1.2.11.md) for measured operation counts, optional VEF validation and remaining integration limits.
