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
- Includes the Lone Beastkin starting scenario and configurable mod settings.
- English and Korean keyed translations are included.

## Requirements

- RimWorld 1.6
- Harmony

## Installation

1. Download the latest release ZIP.
2. Extract the `WildShift` mod folder into RimWorld's `Mods` directory.
3. Enable Harmony before WildShift in the mod list.

The repository itself also uses RimWorld's standard mod folder layout and includes the compiled assembly under `Assemblies`.

## Building

`WildShift.csproj` targets .NET Framework 4.7.2. If RimWorld or Harmony is installed elsewhere, override the `RimWorldManaged` and `HarmonyAssemblies` MSBuild properties.

## Current version

`v2026.09.02.3`

See [CHANGELOG.md](CHANGELOG.md) for details.
