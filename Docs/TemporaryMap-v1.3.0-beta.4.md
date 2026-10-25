# Temporary-map retention — v1.3.0-beta.4 (2026-10-25)

## Report and cause

A player reported that transforming the only colonist on a temporary caravan/ruin map immediately produced **Caravan lost**, followed by **Game over**. The screenshot did not identify the RimWorld version. The user clarified that this should be investigated as a vanilla caravan/temporary-site case, without requiring a camping mod, and requested compatibility checks for both supported game versions.

Native temporary-map retirement asks `MapPawns.AnyPawnBlockingMapRemoval`. Its private static `IsValidColonyPawn(Pawn)` predicate recognizes ordinary colonists and supported colony subhumans, but not WildShift's active animal form. The original human is stored in a HediffComp-owned ThingOwner; native `Pawn.GetChildHolders` does not expose that holder to the map's generic pawn traversal.

Consequently, the final animal-only map can appear abandoned. Site/camp retirement calls `MapParent.CheckRemoveMapNow`, which deinitializes the map. `MapDeiniter.PassPawnsToWorld` issues `LetterLabelPawnsLostBecauseMapClosed_Caravan` for the remaining player animals. Preventing a game-over notification alone cannot preserve a map already removed this way.

## Change

One narrow postfix extends `MapPawns.IsValidColonyPawn` only when its original result is false. A living, undestroyed, player-owned animal form with no host faction retains the map when it actually holds its original living, undestroyed player colonist. The human's native `IsColonist` result remains authoritative, including secure-slave behavior. Downed and mentally broken forms retain their map just as living human colonists do.

Ordinary animals, empty shells, dead/destroyed bodies, hostile/released/hosted forms and forms holding non-player originals do not gain this protection. The patch neither changes global Humanlike/Animal classification nor exposes hidden humans to general pawn enumeration. It adds no global pawn scan, tick loop, cache or save field.

This fixes retirement of an occupied temporary map. It does not add animal-only caravan ownership or change native solo departure/reformation requirements. Return to human form before using the native solo caravan departure flow.

## Verification

- Both 1.6 and 1.7 Release builds pass with zero warnings and errors.
- 481 production-logic/stub assertions pass unchanged.
- 195 native 1.7 assertions pass, including 31 new behavioral assertions and the new Harmony patch registration check. The actual native map-retention getter and Site removal decision fail with the preceding beta.3 DLL, and pass with the correction.
- Native console cases cover downed/mental states, dead/destroyed forms and originals, normal animals, empty shells, changed factions, hosted forms, secure/unsecured slaves, other-map isolation and normal removal eligibility after departure.
- The console fixture models only nearby-caravan lookup, unspawned-holder enumeration and the absence of incoming transporters. It is not a full game-map lifecycle run.
- Full initialized-game validation uses real native `Site` and `Camp` parents with all five DLCs, Harmony and WildShift. A real one-person caravan enters the temporary map, calls the production transformation, runs 600 native ticks, saves the complete game, reloads in a second process, runs another 600 ticks, returns to the same human, departs through native caravan code and removes the now-empty map. A further 600 ticks verify that the departed caravan remains alive. Identity and inventory are preserved and no Caravan lost/Game Over appears in the corrected runs.
- The preceding beta.3 DLL actually removes the Site map and produces both Caravan lost and Game Over under the same initialized-game fixture. The runner requires those concrete results to accept a baseline reproduction; an arbitrary exception does not count.
- The fixture records actual loaded DLL hashes and mod lists and verifies normal user-configuration hashes. Its map is fully initialized by native quicktest, then attached to a real temporary parent; this is not a test of procedural ruins content or visual rendering. `SiteMaker.MakeSite` supplies a real site part. An early fixture omitted that required part and logged a native load error; that run was rejected, the fixture was repaired, and fresh valid runs are retained separately.

The actual installed native assembly is **RimWorld 1.7.4985 rev1269**. The 1.6 build uses RimRef 1.6.4871 signatures with matching Harmony, and metadata inspection confirms the map-retention target. The reflection-only full API checker cannot execute this reference-only assembly because its methods have no RVA; this is a checker/reference-input limitation, not a completed 1.6 runtime test.

Evidence is retained outside the repository in `outputs/wildshift-1.3.0-beta.4`. Full 1.6 gameplay and third-party camping-mod combinations are not claimed as runtime-tested. Already removed maps/caravans are not automatically reconstructed; validate existing games from a save before the loss. The historical CLR/JIT startup observation is not declared fixed by this hotfix.
