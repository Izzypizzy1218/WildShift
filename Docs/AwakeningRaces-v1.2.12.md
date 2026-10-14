# Awakening race selection — v1.2.12

Normal taming, medical awakening and the owned-animal command share this selection for newly generated humans. Existing awakened pawns retain their race. The original animal remains the assigned transformation species.

## Selection

1. A 50% roll uses the player faction's basic member kind, preserving that faction's normal genetic rules. For the current Ratkin player faction, this produces Ratkin. If the faction lacks an eligible basic kind, use the vanilla Colonist kind.
2. The other 50% draws uniformly from eligible alternatives loaded by the game. Each nonhuman humanlike body race has one slot; multiple soldier/trader jobs do not increase its weight. The player's nonhuman body race is excluded.
3. With Biotech, each permitted human XenotypeDef has a slot, including Baseliner, vanilla xenotypes and active-mod xenotypes. HAR's race restrictions are respected, so race-specific restricted genes cannot leak into ordinary humans. When the player's basic body is Human, its highest-weight default xenotype is excluded from this alternative pool. A faction with mixed genetics retains that mixture on the player branch, so final individual xenotype percentages can differ from the 50% branch split.
4. Without Biotech, ordinary Human has a single body-race slot, subject to the same player-race exclusion. If no alternative exists, fall back to the player branch.

Only active definitions are considered. Installed but disabled mods and saved custom xenotype presets that are not XenotypeDefs do not enter the pool. Mechanoid, anomaly entity, drone, mutant, boss and faction-leader kinds are excluded. Each eligible body race needs a valid humanlike pawn kind. Representative kinds prefer player-faction kinds, civilian kinds, then lower combat power and a stable definition-name tie break. Alternative body races use their highest-weight permitted default xenotype, falling back to Baseliner when allowed.

## Generation and safeguards

Alternative candidates are generated with no faction and an explicit selected/default xenotype, then assigned to the player faction before placement. This avoids HAR's rewrite of player Colonists into the faction's basic race and native player-faction gene weighting. If an external hook changes the selected body race, awakening rolls back and preserves the original animal rather than silently accepting the wrong race.

Existing sex handling, possession preservation, harm history, purchase exceptions and duplicate-awakening recovery remain in effect. Candidate scans run only for an awakening's alternative branch; there is no added tick work or global definition mutation.

## Validation

- 89 existing logic assertions, 17 race-selection assertions and 220 stability assertions pass: 326 total.
- The deterministic 20,000-draw test fixture yielded Ratkin/player 9,979; Kiiro 3,343; Human/Pigskin 3,268; Human/Baseliner 3,410. Fifty extra Kiiro soldier kinds do not add race slots.
- Tests cover faction-free alternative requests, explicit xenotypes, HAR restriction filtering, gender/fresh generation, no-alternative fallback, Biotech-disabled selection, colony assignment and rollback on unexpected body replacement.
- The production assembly builds against the installed RimWorld 1.6 libraries. Native Harmony/API/XML checks pass. The installed HAR restriction method was verified in its decompiled implementation.

Distribution tests use a controlled model, not mass generation with every installed mod. Live awakening, graphics and unusual mod-specific pawn-generation behavior still need gameplay confirmation.
