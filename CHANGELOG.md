# Changelog

## v2026.09.02.7

- Replaced the Lone Beastkin opening narration with the finalized shapeshifter origin story.

## v2026.09.02.6

- Added a 1% chance for each humanlike starting-pawn candidate in every other scenario to be generated as a shapeshifter.
- Kept the random candidate's shapeshifter condition visible in character creation, so it can be selected before starting.

## v2026.09.02.5

- Marked the designated Lone Beastkin starting pawn as a shapeshifter in the character-creation screen before the game starts.
- Kept the preview marker aligned with the first selected starting-pawn slot when candidates are reordered or rerolled.

## v2026.09.02.4

- Made slaughtering a transformed animal kill the stored human with no survival roll.
- Kept the stored human's faction synchronized when a transformed animal is released to the wild.
- Made drafted transformed animals use the standard drafted-pawn order flow instead of animal wandering AI.
- Renamed the visible transformed health condition to `shapeshifter`.

## v2026.09.02.3

- Standardized all descriptive text, tooltips, and scenario narrative in English, including when the game language is Korean.
- Kept Korean translations for names, command labels, settings, and gameplay messages.

## v2026.09.02.2

- Expanded shapeshifter lore with their uncertain origins, preserved human bodies, and the rumored many-skinned shapeshifters.
- Added separate lore descriptions for active beast-forms and latent wild shapeshifters.
- Added complete Korean labels and descriptions for all WildShift health conditions.

## v2026.09.02.1

- Kept transformed animals visible and selectable in the colonist bar on maps and in caravans.
- Added the vanilla colonist draft hotkey (`R`) to transformed animals.
- Made transformation available before drafting and placed its command immediately after the draft command.
- Preserved colonist-bar display order across transformation and reversion.

## v2026.09.01.2

- Added the new reversible shapeshifting artwork to both the transform and return-to-human command buttons.

## v2026.09.01.1

- Fixed the one-frame southward snap between melee attack stances for drafted transformed animals.
- Restored RimWorld's native animal melee lunge so transformed attacks have the same visible impact motion as ordinary animals.
- Kept transformed animals facing their active melee target during the drafted-idle frame without altering native attack cooldowns or verb selection.

## v2026.08.30.7

- Fixed permanent death so the stored human dies instead of remaining trapped inside a dead animal form.
- Added safe reversion and game-over protection for caravans and travelling transporters.
- Preserved ordinary colonist attack options during mixed human/transformed selections.
- Limited generated-health cleanup to injuries, missing parts, and harmful generated health conditions.
- Made the Lone Beastkin scenario part drive startup assignment instead of comparing the scenario's Korean name.
- Changed the join setting to a real MTB check and removed the hidden 20-day minimum refire delay.
- Restricted latent shapeshifter reveals to successful tame/recruit interactions instead of every player faction change.
- Completed Korean keyed translations and moved the base scenario text to English with Korean DefInjected translations.

## v2026.08.30.6

- Cached eligible animal kinds instead of repeatedly scanning all pawn kind definitions.
- Removed recurring whole-map scans after Lone Beastkin startup initialization.
- Removed repeated legacy ability cleanup and per-tick melee-facing correction.
- Initialized transformed-animal control at creation/load time instead of every 250 ticks.
- Cached the transformed-colonist game-over safeguard and invalidated it on transform, revert, and death.
- Removed temporary LINQ/list allocations from direct right-click attacks.
- Reduced the Lone Beastkin recovery UI check to once per game.

## v2026.08.30.5

- Removed the southward positional jump from transformed-animal melee attacks.
- Made drafted transformed animals attack immediately when an attackable target is right-clicked.
- Added melee order feedback and player-forced melee jobs.

## v2026.08.30.1–v2026.08.30.4

- Added generated animal health cleanup for new and existing saves.
- Added transformed-animal melee facing correction and iterated on attack rendering.
- Added safe drafted controls and right-click melee ordering.
