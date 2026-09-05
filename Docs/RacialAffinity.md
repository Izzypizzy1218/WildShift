# Racial affinity integration notes (v1.1)

## Behavior

New shapeshifter assignments roll a 50% preference for a matching racial animal. Otherwise they use the existing normal pool (or the Lone Beastkin scenario's existing fallback form). The ordinary pool may already contain some preferred animals, so the final population ratio can exceed 50%. A pawn has one assigned form, not a fresh roll on every transformation.

Valid existing forms are retained. Assignment uses the existing `assignedKind` saved Def reference. Assignment and the transformation guard both recognize racial exceptions. Taming retains the revealed animal's original form and does not use racial preference. No additional ticking component, generation-wide scan, Harmony patch, or hard mod dependency was added.

This does not infer species from cosmetic genes, translated names, or package-name substrings. A custom xenotype that merely copies ears is not automatically recognized. Xenotype rules take precedence over race rules. Changing a pawn's race/xenotype later can invalidate a racial-only form; this is not a system for accumulating permanent transformation unlocks.

## Identifier evidence

Exact Def names were checked on 2026-09-06. Installed XML or public mod/compatibility source establishes the mapping, not full runtime compatibility with every release or fork.

| Integration | Race Def / xenotype Def | Evidence |
| --- | --- | --- |
| Ratkin | `Ratkin`; `RK_XenoType_Ratkin` | Installed NewRatkinPlus 1.6 race and Biotech XML, Workshop 1578693166 |
| Kiiro | `Kiiro_Race` | Installed Kiiro 1.6 race XML, Workshop 2988200143 |
| Nyaron | `Alien_Nyaron` | [Nyaron race source](https://github.com/Farmradish/Nyaron/blob/main/1.4/Defs/ThingDefs_Races/Race_Nyaron.xml) |
| Kurin HAR | `Kurin_Race` | [Kurin HAR source](https://github.com/Seioch/Kurin-HAR), `1.5/Defs/Race/Race.xml` |
| Revia | `ReviaRaceAlien` | [Revia 1.6 source](https://github.com/FoxWithAShotgun/RimWorld-ReviaRaceMod), `1.6/Defs/RaceDefs/ReviaRace.xml` |
| Legacy Miho HAR | `Alien_Miho` | [Combat Extended integration source](https://github.com/CombatExtended-Continued/CombatExtended), `ModPatches/Miho Race/Patches/Miho Race/ThingDefs_Races/AlienRace_Miho.xml` |
| Current Miho | `Xeno_CelestialMiho` and `_Arctic`, `_Desert`, `_Highland`, `_Highmate`, `_Voidborn` | Installed Miho xenotype XML, Workshop 2816826107 |
| Rabbie | `Rabbie` | [Rabbie source in English patch repository](https://github.com/kyubix/Rabbie-The-Moon-Race-English-Patch), `1.2/Defs/Rabbielike/GeneralRace.xml` |
| Yuran | `Yuran_Race`; `YuranXenotype` | [Toddlers integration source](https://github.com/cyanobot/Toddlers/blob/master/1.4/Patches/HAR_Races.xml); xenotype naming also seen in Yuran quest definitions |
| Bori / Sheepawn | `Alien_Bori` / `Alien_SP` | Installed Toddlers `1.4/Patches/HAR_Races.xml`, Workshop 2903359152; [integration source](https://github.com/cyanobot/Toddlers) |

All target animal PawnKind names were verified in the installed RimWorld Core XML. Missing target kinds are resolved silently and skipped; non-animal and mechanoid replacements are rejected. Bori uses vanilla dogs, not an additional shepherd mod dependency.

## Verification

Run `pwsh -NoProfile -File Tests/Run-LogicTests.ps1` in a fresh PowerShell process. The harness compiles the real selection, pool, hediff-comp, assignment-method, and transformation-guard code against minimal engine stubs. It checks exact mappings, fallback behavior, roughly 50% selection, scoped exceptions, retained existing assignments, explicit taming assignment, and no repeated roll during simulated hediff callbacks/redraw. The saved Def reference test uses a stub serializer; it is not an actual RimWorld save/load test.

The mod must also be compiled against the installed RimWorld/Harmony assemblies. Full in-game tests are still needed:

1. Generate new shapeshifters of each supported race or xenotype. Existing v1.0 forms must stay unchanged.
2. Confirm the assigned animal in the health entry, transform, draft, move, attack, and revert. Verify the original alien body/genes return.
3. Save and reload in both human and animal form, then transform/revert again.
4. For Miho, check arctic/desert forms separately. For small animals, verify command usability and melee animation.
5. Test without race mods and verify ordinary shapeshifters and taming still behave as before.

No claim is made here that all race-mod runtime callbacks, graphics, or real save/load combinations have been exercised.
