# ISEKAI creature drafting compatibility

Included in v1.4.0-beta.9 (2026-11-18). The compatibility fix and initial validation were completed on 2026-11-17.

## Cause

The actual ISEKAI CREATURES ADD-ON (`Poupun.IsekaiCreaturesAddon`, Workshop item `3757560461`) appends its own draft toggle to player creatures with a draft controller. WildShift already supplies a draft toggle for the transformed colonist. In English both commands have the same label, icon, hotkey and default group key, so native `Command.GroupsWith` puts them in one group.

Native `GizmoGridDrawer` dispatches the inherited command first, then the group's representative. Both callbacks toggle the same pawn: `false -> true -> false`, or `true -> false -> true`. This makes the visible draft/undraft command appear ineffective. The base ISEKAI RPG LEVELING mod (`JellyCreative.IsekaiLeveling`, item `3657580708`) preserves the existing command list and is not the direct producer of this duplicate.

## Fix

Extend the existing transformed-animal duplicate filter and order it after the add-on's Harmony owner. Remove only a draft-hotkey `Command_Toggle` whose callback belongs to the exact `IsekaiCreatures.Taming.Pawn_GetGizmos_Pet_Patch` producer, including its compiler-generated closure. Preserve the existing Draftable Animals rule.

The filter remains limited to player-owned forms containing their stored human. Ordinary tamed ISEKAI companions retain their own draft command. Other commands pass through unchanged. No hard assembly dependency, optional-type search, pawn ticking, taming flags or ISEKAI source changes are added.

## Actual-game validation

All five DLCs were enabled in the isolated compatibility profiles. The two real Workshop packages and their original DLLs were loaded alongside WildShift.

| Engine | Published beta.8 | Local fix |
| --- | --- | --- |
| 1.6.4871 | 12 cases completed; 60 expected-behavior failures | 12 cases completed; zero failures |
| 1.7.5011 | 12 cases completed; 60 expected-behavior failures | 12 cases completed; zero failures |

Each run covers Warg plus eligible large slime, hobgoblin, kobold chieftain, orc and wyvern forms, with the human initially drafted and undrafted. It enumerates real `Pawn.GetGizmos`, replays the native mouse/key command-group dispatch using actual `GroupsWith`, `MergeWith`, `ProcessInput` and `ProcessGroupInput`, observes five ordinary native ticks, and returns the same human. Fixed forms have one draft toggle; draft, undraft and redraft work. Five ordinary tamed ISEKAI control species retain their add-on toggle and command behavior. Non-draft command counts remain unchanged.

A separate accepted 1.7 run loads WildShift before the creature add-on; all 12 cases also pass. Accepted runs have zero startup/new native errors, no fixture exceptions and unchanged normal config hashes. The 1.7 isolated API regression passes 473 assertions; metadata checks pass all 172 Harmony targets and 24 injected fields on each engine.

Evidence: `outputs/wildshift-isekai-draft-20261117/{baseline17-v3,candidate17-v3,baseline16-v3,candidate16-v3,candidate17-first-v4}`. The first two fixture revisions are retained but excluded: they selected some ineligible small forms and/or did not handle an initial quicktest pause. The first reverse-order run passed behavior but is excluded because normal `Prefs.xml` changed during that run; its evidence is preserved and it was rerun with a full config snapshot. The cause of that preference-file change was not established; it was not automatically reverted.

This is real initialized-game command-dispatch validation, not a physical GUI click/key-capture test. It does not establish long-term save/load, combat, arbitrary ISEKAI ability execution or the health of an entire mod pack.
