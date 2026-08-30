# Changelog

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
