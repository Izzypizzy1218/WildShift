# Optional transformed-colonist slaughter and sale settings

Included in v1.4.0-beta.7 for RimWorld 1.6 and 1.7.

WildShift settings contain two independent checkboxes:

- **Allow slaughter of transformed colonists**
- **Allow sale of transformed colonists**

Both default to **enabled**, including existing configuration files without these keys. Reset restores both to enabled. Players who want protection can disable either option without changing the other.

## Slaughter protection

Disabling slaughter prevents designator/animal-table designation, automatic slaughter and slaughterer mental-state targeting for player-owned active forms holding their living original player colonist. Existing slaughter designations are removed on the settings change and after loading; the automatic-slaughter cache is refreshed once. Running or forced slaughter jobs receive a live cancellation condition, including jobs that ignore designations, with a final guard before the native slaughter execution.

The option does not provide general damage immunity or alter unrelated executions. When slaughter is allowed, the existing rule that slaughter also kills the preserved human remains.

## Sale protection

Disabling sale removes these forms from native trader offers. A trade or trader gift prepared before the change is rejected before currency, goodwill or ownership changes if it still includes a protected form; remove that form from the deal or enable sale and confirm again. The native pawn trader, orbital ship, settlement and caravan handoff paths also reject the protected form before splitting or transferring it.

Buying back a form is allowed. Ordinary animals, latent animals that have not awakened, enemy-owned forms and humans in their normal form retain native behavior. When sale is enabled, existing sold-shapeshifter revenge rules remain applicable. Transport-pod gifts and custom mod transfer systems are outside this trader-sale option.

The protection checks ownership rather than healthy player-control eligibility, so downed or mentally broken colonists do not lose protection. There is no recurring map-wide pawn scan.

## Validation

The protection-only candidate was validated on November 13. The combined November 15 candidate and expanded tests are documented in [the user report recheck](UserReports-v1.4.0-beta.7.md).

Results and exact tested DLL hashes are retained in `outputs/wildshift-animal-protection-20261113`. The accepted runs are `fullengine16-v2` and `fullengine17-v2`: each passes 586 checks, including 55 protection checks, across three fresh-process save/reload phases with all five DLCs. Both report zero failures or native errors and unchanged normal user configuration. Coverage includes native manual/automatic/mental-state slaughter, running ignore-designation jobs, the final execution guard, nonzero silver transactions and gift mode, all four trader handoffs, allowed-sale revenge scheduling and the existing allowed-slaughter death rule.

Both production builds complete with zero warnings/errors. API validation resolves 168 named Harmony targets and 24 injected fields against each version; the separate native 1.7 object-subgraph harness passes 467 assertions, including new/legacy/reset configuration round-trips. These are bounded isolated scenarios, not exhaustive optional-mod or visual compatibility tests.

The first 1.6 launch failed before gameplay because the private engine copy lacked its `MonoBleedingEdge` runtime directory. Its raw logs and diagnosis remain preserved; adding the missing runtime junction allowed the unchanged production and fixture DLLs to pass the accepted run. Published beta.6 runtime files remain unchanged.
