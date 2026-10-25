# Shared personal state and preserved animal bodies — v1.4.0-beta.1

This local beta keeps WildShift's real animal Pawn and preserved original human. The human is the single authority for supported personal state; the animal remains the active physical body. It does not replace the architecture with an animal-looking human.

## Behavior

| State | Behavior while transformed |
| --- | --- |
| Identity, relations and memories | The same original human is retained. Its existing memory clock advances; the hidden human does not run another complete Pawn/AI/needs tick. |
| Skills, passions and experience | The animal's separate skill tracker exposes the original human-owned records. Native learning changes those records once; they are serialized through the human only. |
| Gene and trait stats | Active genes' plain `statOffsets/statFactors` and unsuppressed trait-degree modifiers compose with the animal's native stat calculation. Matching effects already native to the animal are not counted twice. Source changes invalidate affected stat caches. |
| Drugs, addiction and chemical dependencies | Supported native whole-body drug Hediffs and chemical needs remain on the human and advance once. Separate effect projections apply their stages to the animal. Drug ingestion updates the canonical human state; nutrition remains physical-body state. |
| Luciferium wound healing | The original native healing timer acts on the active body. It does not also heal the inactive human. |
| Animal implants and modifications | The exact animal Pawn is cached after return, retaining its implant Hediffs and component state. Next transformation reuses that body rather than generating a replacement. |
| Animal wounds | Following the chosen existing balance, ordinary injuries, non-prosthetic missing parts and blood loss reset on the next transformation. Existing generated-disease cleanup and acute-hazard reset also apply. Missing-child markers required by artificial limbs are retained. |
| Human implants, injuries and equipment | Remain on the original human. Body-specific implant performance is not mapped to unrelated animal anatomy. |
| Animal apparel and belongings | Existing return behavior continues: drop nearby, or preserve in human inventory when placement fails/off-map. Cached bodies do not automatically re-equip dropped gear. |

Conditional gene stat affecters (including conditional penalties), gene abilities, arbitrary custom mod Hediffs/comps, and all custom race mechanics are not generically shared. The drug bridge accepts supported native classes/comps and body-wide drug/dependency state; it is not a universal Hediff copier. Other human biological needs, aging and hidden-body environmental simulation are not added here.

## Ownership and safety

- Only one body owns the other at a time. While transformed, the animal's transformed component deeply owns the human. After return, the human's `AnimalFormStorage` deeply owns the inactive animal; that animal no longer owns the human. Inactive contents do not tick.
- No broad hidden-pawn enumeration patch is added. Weak identity links and per-carrier state avoid a per-tick scan of the colony or world.
- Awakening a latent wild/purchased animal adopts its actual original animal body. Failed adoption rolls back; successful adoption consumes the latent marker.
- Original species information survives a marked Sapient Animals body replacement. Its identity and skill proxy are rebound to the new active body. A cached species mismatch refuses checkout rather than deleting the preserved body.
- Existing saves without a cached form create one during the next normal transformation. Previously destroyed animal implants cannot be reconstructed.
- Ordinary animal death uses the existing configured human death chance; slaughter retains its guaranteed-death rule. A supported lethal shared drug state kills the original human and cannot escape through the animal death survival roll.
- If fatal ingestion returns/caches the animal mid-callback, remaining drug outcomes and drug component callbacks skip that inactive body. This prevents later addiction, overdose, Hediff or need effects being applied to a cached animal.

## Validation scope

Validation records and exact input hashes are stored outside the runtime package in `outputs/wildshift-1.4.0-beta.1`. The local installation is backed up before replacement. No Workshop or GitHub upload is part of this beta implementation.

Final results are recorded in the accompanying local validation summary. Tests distinguish logic/stub checks, isolated native assembly checks, and full initialized headless 1.7 gameplay. Both version-specific production builds use their matching game/Harmony APIs. Actual 1.6 gameplay and visual/third-party mod-pack verification are not claimed.

Verified on 2026-10-26 (KST), with actual RimWorld **1.7.4985 rev1269**:

| Check | Result |
| --- | --- |
| Production 1.6 and 1.7 builds | Both pass, zero warnings/errors; 1.6 uses 1.6.4871 reference assemblies |
| Logic, storage/recovery, cache, training and revenge stub suites | 527 assertions pass |
| Production DLL with native 1.7 API fixtures | 267 general/identity + 37 shared-stat + 31 shared-health checks pass |
| Full shared-state engine fixture, all five DLCs | 126 checks pass across three fresh processes and both ownership-direction saves |
| Existing Core gameplay regression | 25 checks pass across save/reload, including caravan survivor, draft and training behavior |
| Temporary-site lifecycle, all five DLCs | 32 checks pass across native save/reload, map retention and legitimate departure/cleanup |
| Startup, all five DLCs | Completed initialization and clean exit; no ERROR or fatal stderr |
| Normal user configuration | Full before/after file inventory and hashes unchanged |

The final production DLL SHA256 values are `8B8F324CB7C1C79C02A1EDE27E01F63EB219A935EB4B8474FA44498AA1B48271` (1.6) and `62DD480264AD91ACCA4C310B9CB6CD5F2E52CDA4156E0FE2F8E85B75A860695F` (1.7). The accepted engine records are `engine-shared-verified/result.json`, `engine-review-final/result.json`, `engine-site-final/result.json`, and `engine-startup-final/result.json` under the output directory. Native non-fatal shutdown/allocation warnings are retained in logs; a clean error check does not mean no warnings were emitted.

The full-engine shared-state fixture checks real ingestion, learning, live gene/trait changes, native ticks, caravans, ordinary and drug deaths, scar healing, possessions, and complete process-separated saves with both the cached-animal and active-animal ownership arrangements. It uses all five official DLCs. Existing Core gameplay and all-DLC temporary-site/startup regressions are checked separately.

Rejected setup attempts are retained as diagnostic evidence: an initial run without an initialized Steam connection did not load Harmony correctly; a test-only drug definition originally omitted its native drug component; and a one-tick healing assertion did not account for native 1.7 tick batching. None is counted as a successful game validation. Historical intermittent CLR/JIT startup behavior remains an unproven separate issue.
