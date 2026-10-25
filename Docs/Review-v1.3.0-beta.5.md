# Gameplay review — v1.3.0-beta.5 (2026-10-25)

## Confirmed defects and changes

| Trigger | Previous result | Correction |
| --- | --- | --- |
| The last ordinary human in a mixed caravan dies while a transformed colonist survives. | Native owner counting ignores the animal form and destroys the caravan, losing both bodies. | Preserve the existing caravan and remove the dead member through the native surviving-owner operations. |
| A caravan has only healthy, sane transformed colonists left. | Native all-owners-downed and all-owners-mental-break queries can both return true and prevent travel. | Count the surviving human-backed forms for these two survival/mobility queries. |
| Native tameness decay runs on a transformed species with no special trainables, such as a Core-only warg. | The form can go wild, losing player ownership and its return command while retaining the hidden human. | Skip training decay for every active transformed form. Ordinary animals retain native decay. |
| A drafted animal returns to its human body on a map. | Despawning/recreating the human's native drafter silently clears the draft order. | Restore the animal's draft state after successful human placement, with native incapacity and faction guards. |

The first two rows are one caravan-owner defect and its related mobility behavior. The review produced three independently reproduced regressions overall.

## Scope and implementation

`Patch_TransformedCaravanOwner.cs` checks actual members of an existing player caravan. A qualifying form must be a living, undestroyed player animal with no host faction and must hold its actual living, undestroyed original human. The original human must pass native `CaravanUtility.IsOwner`, retaining faction, prisoner, slave and mutant restrictions. Empty shells and dead/released/hosted bodies do not qualify.

The death prefix handles only the missed surviving-form case. When an ordinary native owner remains, it leaves the original method in charge. Its replacement performs the native surviving-owner actions: strip the dead member and remove it. Two postfixes correct only otherwise-true mobility results when a qualifying form is actually able to act. Global `IsOwner`, animal sale eligibility, negotiator selection and caravan formation are unchanged. There is no new global scan, cache, timer or saved state; member checks are linear, without redundant membership scans.

The training fix removes the old special-trainables prerequisite from the existing rare training callback. It does not grant unrelated special abilities to ordinary forms or stop ordinary livestock from becoming wild. Draft restoration happens only after successful on-map human placement and requires a live, upright, sane player human with a drafter. Off-map returns remain undrafted; rollback/retry paths keep their existing item and identity safeguards.

## Automated verification

Both Release builds pass with zero warnings/errors. The 1.6 target uses RimRef 1.6.4871 and matching Harmony; metadata confirms the three new caravan patch targets. No complete 1.6 game is installed, so this establishes compilation/API compatibility, not actual 1.6 gameplay.

| Suite | Passing assertions |
| --- | ---: |
| Assignment/customization/awakening logic | 179 |
| Stability and return rollback | 256 |
| Cache lifetime | 23 |
| Training | 22 |
| Revenge budget | 18 |
| Native 1.7 APIs, Harmony registration and pawn subgraphs | 224 |
| Total | 722 |

The first five suites use engine stubs. The native console suite uses installed game assemblies, with bounded fixtures for uninitialized engine boundaries; it does not itself execute a complete game. New checks cover negative ownership cases, native animal trade/negotiator behavior, actual incapacity/mental-break results, non-special tameness retention, unchanged ordinary livestock and draft restoration/rollback guards.

## Actual 1.7 engine regressions

The installed engine is **1.7.4985 rev1269**. Fresh isolated profiles use real quicktest-initialized worlds/maps and the production transformation methods. They do not replace the gameplay methods under test.

- **Core-only baseline/correction:** beta.4 reproduces all three exact defects: lost draft on return, warg tameness/faction loss and mixed-caravan destruction on native companion death. The corrected DLL passes the same fixture, then saves and reloads the complete game in a separate process. Native world ticks preserve the caravan and exact stored human; off-map return restores that same world-registered human and the exact inventory. Direct native mobility queries pass while global animal trade ownership remains unchanged.
- **Training scheduling:** the fixture invokes native `Debug_MakeDegradeHappenSoon` and `TrainingTrackerTickRare` enough times to exhaust tameness. This accelerates scheduling without replacing degradation or faking the loss of ownership. A separate ordinary warg still becomes wild. The baseline fixture restores only the already-failed form's faction/training afterward so the independent caravan case can run.
- **Existing hotfix regression, all five DLCs:** a native caravan enters a real temporary Site containing an AncientComplex part. The only colonist transforms, runs 600 ticks, saves/reloads, runs another 600 ticks, returns, departs through native caravan code and allows normal empty-map cleanup. Another 600 ticks preserve the departing caravan. No Caravan lost/Game Over or native errors are logged.
- **Startup, all five DLCs:** initialization completes with exit code 0, no fatal stderr and no logged native errors. The historical intermittent CLR/JIT crash remains unproven; this release does not claim to repair it.

Actual loaded hashes and mod lists are checked in the engine fixtures. The production 1.7 DLL SHA256 is `408652D15B0697B600CD44532B10981CFB04B55BB9AEB02FE08335E4B34695E2`; the same review fixture DLL is used for baseline and correction (`38BE0A0F94F0A5B862329006C7279C4D453FC1A5D82FC606EE09E68AC18CDDEB`). Evidence is retained outside the repository in `outputs/wildshift-1.3.0-beta.5`.

An initial profile unintentionally auto-enabled installed expansions because its `knownExpansions` list was empty. That run was rejected. The runner now records all installed expansions separately from active mods and verifies the actual loaded list; the accepted Core-only baseline and correction both contain only Core, Harmony, WildShift and the review fixture. The review build record also initially hashed the unrelated temporary-map source in its `SourceSHA256` metadata field; the durable runner now hashes the review source. In-process production/fixture DLL hash checks were correct in the accepted runs.

## Limits

These are headless engine tests, not visual checks or a full third-party mod-pack run. They do not add solo animal caravan formation, recover caravans already destroyed, or certify every optional integration. A code-path candidate involving transformation during caravan formation remains unconfirmed in a full engine scenario and is not presented as a fixed defect. Use a save from before any existing loss to validate recovery. Test-only helper mods are excluded from the runtime package and removed from the normal mod directory after verification. Normal user-configuration hashes remain unchanged.
