# Optional animal-pack integration — v1.1.4

## What changed

The existing AnimalPool already discovers all loaded eligible PawnKindDefs. Ordinary animal forms from these packs do not need one-by-one registration, and the integration does not override size/predator restrictions. Optional load-after hints now cover the four packs and VEF.

An actual gender conflict was found in the installed VEF 1.6 `CompFixedGender.CompTick`: its first mapped tick sets the pawn's gender to the component's configured value. Alpha Animals applies it to `AA_BlizzariskClutchMother`, `AA_DunealiskClutchMother`, and `AA_FeraliskClutchMother`, all of which pass the normal WildShift filter. A soft-reference Harmony prefix now skips this component only on WildShift transformed animals, preserving generation-time gender. Ordinary animals still execute the original code. The hook is installed once at startup and adds no map-wide scan or new tick scheduler; it executes on the existing fixed-gender component callback.

There are no redistributed third-party textures, Defs, or assemblies in WildShift. VEF and the animal packs remain optional. Neither Steam subscriptions nor the player's active mod list are changed by this release.

## Examined sources

| Pack | Examined build/source | Definition records | Default candidates | With insectoids enabled |
| --- | --- | ---: | ---: | ---: |
| [Vanilla Animals Expanded](https://steamcommunity.com/sharedfiles/filedetails/?id=2871933948) | [Creator repository](https://github.com/Vanilla-Expanded/VanillaAnimalsExpanded), commit `16efd0272064427a3476d287d26eccdc0ca7828d`, 1.6 with Odyssey | 51 | 29 | 29 |
| [Megafauna](https://steamcommunity.com/sharedfiles/filedetails/?id=1055485938) | [Creator repository](https://github.com/juanosarg/Megafauna), commit `3d781e1e6a038eabcdd58e87ac03135052cddf64`, 1.6 | 38 | 35 | 37 |
| [Alpha Animals](https://steamcommunity.com/sharedfiles/filedetails/?id=1541721856) | Installed Workshop 1.6 files, inspected 2026-09-06 | 139 | 71 | 91 |
| [Dinosauria](https://steamcommunity.com/sharedfiles/filedetails/?id=1136958577) | Installed Workshop 1.6 files downloaded 2026-09-06 | 32 | 28 | 28 |

VAE without Odyssey also loads `1.6NotOdyssey`: the combined source has 63 PawnKind records, of which 39 pass the default filter. With Odyssey, some animals are supplied by the DLC rather than the pack; the table is not a count of all forms available in the game. Counts are PawnKind definitions, not necessarily distinct species. The [candidate list](AnimalModForms.md) includes labels and exact Def names.

These are source-level results: the inspector resolves XML parent chains and runs the actual AnimalPool eligibility code against minimal engine objects. It does not apply XML PatchOperations, optional compatibility subfolders, per-mod settings, or C# runtime mutations. Final loaded-game candidates may differ.

## Reproduce the definition inspection

```powershell
pwsh -NoProfile -File Tests/Inspect-AnimalModDefs.ps1 -ModRoot 'C:\path\to\AnimalMod'
pwsh -NoProfile -File Tests/Inspect-AnimalModDefs.ps1 -ModRoot 'C:\path\to\VanillaAnimalsExpanded' -WithoutOdyssey
```

Use `-GameRoot` if RimWorld is not installed at the default Steam path. Run logic and stability tests in fresh PowerShell processes as documented in [Stability.md](Stability.md). The optional-hook tests cover missing framework, missing method, declared-target selection, ordinary animals, transformed animals, and non-player faction forms. Actual Harmony execution with VEF still needs a game session.

## Setup and in-game verification

1. Back up a save and enable Harmony, the packs' required dependencies, the desired animal packs, and WildShift. Use the game's mod-order warnings/auto-sort. VAE and Alpha Animals require Vanilla Expanded Framework.
2. Generate new shapeshifter candidates: installing a pack does not reroll already assigned animal forms. Confirm an expected Def from the candidate list is available.
3. Test transform, portrait selection, draft/undraft, movement, melee attack, human reversion, and save/reload while transformed for each pack.
4. With Alpha Animals, test a male original pawn assigned one of the three clutch-mother forms; check gender after waiting and after save/reload. A normal non-WildShift clutch mother should remain subject to VEF's original fixed-female behavior.
5. Repeat with VEF and all four packs disabled. Ordinary WildShift behavior must still work without missing-type errors.

## Limits and warnings

- This is basic form-pool integration plus a targeted gender fix, not a blanket guarantee that every special power is controllable. Existing WildShift direct attacks are melee orders, not a new ranged/ability command system.
- The reviewed VEF metamorphosis component spawns a replacement pawn and destroys the old one without transferring WildShift's hidden human. The four Alpha larval forms using it currently fail the normal eligibility rules; they were not whitelisted. Do not force them into the pool without a dedicated ownership-transfer implementation.
- Alpha's mature fleshbeast has its own swallowed-pawn container. Its source ejects contents during mapped destruction, but off-map reversion with swallowed contents has not been validated. Avoid that combination pending a dedicated holder audit.
- Health-cleanup classification, special initial hediffs, generated resources, aura effects, reproduction, and custom animal AI need live testing with the chosen animals. No reproduction/prosthetics/armor redesign is included.
- Disabling an animal's wild spawn in its original mod does not necessarily remove its loaded PawnKindDef from WildShift's pool.
- Predator/large-animal limits, the blacklist, optional insectoid forms, 20% racial preferences, 1% starting-candidate chance, and 5% normal-join chance are unchanged.
- Enemy shapeshifter generation/attack AI and automatic enemy reversion on downing are not part of this release.
