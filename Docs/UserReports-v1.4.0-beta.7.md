# November 15 user report recheck — v1.4.0-beta.7

This document describes the passive-health additions and report verification included in v1.4.0-beta.7 for RimWorld 1.6 and 1.7.

## Optional slaughter and sale

The two independent **Allow slaughter of transformed colonists** and **Allow sale of transformed colonists** checkboxes remain enabled by default, including older settings files. Unchecking either protects active player-owned forms carrying their original player person. The earlier designation, auto-slaughter, mental-state, queued-job and atomic trader-transaction safeguards remain. See [the protection behavior](AnimalProtection-v1.4.0-beta.7.md).

## Dinosauria version selection

The installed `spincrus.dinosauria` package has 1.6 and 1.7 folders; all nine XML files in those folders are byte-identical. The game's native loader selects the current version folder, and WildShift selects from the current `DefDatabase<PawnKindDef>` and generates through the native `PawnGenerator`.

The author's [October 25 official comment](https://steamcommunity.com/sharedfiles/filedetails/comments/1136958577?ctp=1&l=english#comment_568175240219329438) explains that the 1.7 update added the supported-version declaration. A fresh official HTTP response, its timestamp and the installed-file hashes are retained under `outputs/wildshift-dinosauria-audit-20261115`.

The offline source-inspection helper previously fixed its input path to 1.6. It now detects the game version or accepts an explicit 1.6/1.7 selection, preserving its documented limits around custom load-folder conditions and XML patches. This helper is not part of runtime transformation.

## Passive health effects

The unchanged previous local runtime was measured before applying the new fix. Native Luciferium already supplied its seven capacity modifiers, pain reduction and applicable stat effects to the animal. For example, the clean Warg's consciousness changed from 1.00 to 1.10, moving from 1.00 to 1.10 and sight from 1.00 to 1.15, matching a natively dosed ordinary Warg. These are native calculated values; capacity dependencies can make them differ from simply adding an XML offset.

The audit and baseline did identify seven personal effects omitted from the supported native state list:

| Native effect | Shared behavior added |
| --- | --- |
| NoPain | Persona weapon painlessness |
| SpeedBoost | Persona weapon movement bonus |
| NeuralHeatRecoveryGain | Persona weapon neural-heat recovery bonus |
| HungerMaker | Bonded weapon's increased hunger |
| NeurosisPulse | Work, joy and mental-break modifiers |
| PleasurePulse | Work penalty and native lovin-interval component |
| PsychicHangover | Capacity, hunger and rest penalties |

These native definitions now use the existing non-owning health projection. Sources, native components and timers retain their original human owner; both benefits and penalties follow the person. The change does not automatically import arbitrary mod state machines, physical diseases or anatomical injuries.

Neural cooling retains native pain postprocessing. With the combined test weapon, a 0.54 base rate and 5% pain produce 0.621 before equipping it. The raw +0.15 cooling bonus and NoPain together produce 0.690, because removing pain also removes its native cooling multiplier. The regression uses the configured native pain factor rather than assuming every final value increases by exactly 0.15.

## Validation records

Evidence is retained under `outputs/wildshift-report-recheck-20261115`. The baseline is preserved separately from final candidate results. It confirms the Luciferium numbers and missing personal effects, but also contains separate fixture issues: an incapable test negotiator and drafting an unspawned test pawn. The first candidate run also exposed incorrect cooling expectations and a view-cleanup assertion made before the normal health query. Those fixture issues are corrected without replacing native game methods or manually invalidating the active animal's tested stat cache.

The accepted final runs are `fullengine16-v1` and `fullengine17-v3`: each passes **722 checks** through three fresh-process save/reload phases on actual 1.6.4871 and 1.7.5005, with all five DLCs and Dinosauria loaded. This includes 55 protection, 75 passive-health and 61 Dinosauria checks. Both report zero failures/native errors and unchanged normal configuration during the accepted run. Builds complete without warnings/errors; metadata validation resolves all 168 named Harmony targets and 24 injected fields on each version.

The new tests compare all core capacities and relevant stats with ordinary native controls, verify same-tick state changes/removal and persona weapon equip/bond ownership, and inspect the actual version folder, all 32 pack-owned kinds, the 28 eligible forms and fresh/cached Triceratops generation. These are isolated native-game scenarios, not every mod combination or a visual compatibility guarantee. The native PleasurePulse component is reviewed and projected; an actual romance job is not part of these checks.

`fullengine17-v2` also passed all 722 gameplay checks but was rejected by the byte-level normal-config preservation guard. Its sole file difference was one trailing CRLF in `Prefs.xml`; an exact pre-change backup was located and every XML value is identical. The writer is unclassified. The unchanged runtime/fixture then passed `fullengine17-v3`, including byte-level config preservation. Raw runs, the changed-copy backup and `outputs/wildshift-passive-health-audit-20261115/prefs-byte-diagnosis.json` retain this evidence.
