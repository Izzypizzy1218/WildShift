# WildShift

WildShift is a RimWorld 1.6 mod about colonists who can transform into an assigned animal form and later return to their preserved human body.

## Features

- Transform a shapeshifter into one eligible animal form before or after drafting.
- Keep transformed animals visible and selectable in the colonist bar.
- Draft and directly control the transformed animal.
- Use the normal draft hotkey (`R`) for transformed animals.
- Order a melee attack by right-clicking a target; no extra move/attack menu is shown.
- Return to the stored human body with the revert command.
- Prevent a false game over while a lone colonist is transformed.
- Remove randomly generated diseases and injuries from temporary animal bodies.
- Discover latent shapeshifters by taming eligible wild animals.
- Every normal human colonist join has a 5% chance to reveal a shapeshifter instead of triggering a separate join event.
- Supported animal-eared races and xenotypes have a 50% preference roll for a matching animal when first assigned a shapeshifter form. The other branch uses the normal pool; existing forms are not rerolled.
- Includes the Lone Beastkin starting scenario and configurable mod settings.
- English and Korean keyed translations are included.

## Requirements

- RimWorld 1.6
- Harmony

## Racial animal forms

| Race | Preferred animal |
| --- | --- |
| Ratkin / NewRatkinPlus | Rat |
| Kiiro, Nyaron | Cat |
| Kurin HAR | Red or arctic fox |
| Miho | Red fox; arctic and desert xenotypes use arctic and fennec foxes |
| Revia | Red fox |
| Rabbie, Yuran | Hare or snowhare |
| Bori | Husky or Labrador retriever |
| Sheepawn (Bori) | Sheep |

The preference is rolled **after** a pawn becomes a shapeshifter; it does not change the 1% starting-candidate or 5% colonist-join chances. Matching racial forms bypass the ordinary size/predator/blacklist filters only for the matching pawn. Uninstalled race mods require no configuration or extra dependencies. Missing animal definitions fall back to the normal pool. Animals revealed by taming keep their original form.

These mappings identify exact race or xenotype Def names, not cosmetic ears on arbitrary custom xenotypes. Existing shapeshifters keep their assigned forms. See [integration notes and test scope](Docs/RacialAffinity.md).

## Installation

1. Download the latest release ZIP.
2. Extract the `WildShift` mod folder into RimWorld's `Mods` directory.
3. Enable Harmony before WildShift in the mod list.

The repository itself also uses RimWorld's standard mod folder layout and includes the compiled assembly under `Assemblies`.

## Building

`WildShift.csproj` targets .NET Framework 4.7.2. If RimWorld or Harmony is installed elsewhere, override the `RimWorldManaged` and `HarmonyAssemblies` MSBuild properties.

## Current version

`v1.1`

See [CHANGELOG.md](CHANGELOG.md) for details.
