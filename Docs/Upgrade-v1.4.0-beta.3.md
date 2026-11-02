# Old-save state preservation — v1.4.0-beta.3

Local candidate, 2026-11-03. Actual older-save state probes pass on both versions, and the controlled full-engine regression passes **294 checks on each version**. Original saves and normal configuration remain unchanged. This is a local validation release; no Workshop or GitHub publication is claimed.

This candidate retains [beta.2's preserved-body/shared-state design](SharedState-v1.4.0-beta.2.md). It adds a narrow migration for supported native drug state already saved on an active animal by versions predating the shared-state bridge. It does not execute another complete human Pawn tick or add a recurring pawn scan.

## Cached-body ownership and inactive skills

An actual older 1.7 colony's first transformation/return exposed two differences on fresh reload. The animal-storage constructor runs while its enclosing Hediff's pawn is still unset during native `LoadingVars`, so its captured human owner remained null. The owning comp now rebinds the storage during subsequent native serialization phases and normal access. The inactive animal retains the original human's parent-holder chain and held map after loading.

Returning also immediately rebinds the animal's shared skill tracker to its own preserved body records. Previously its public list could temporarily retain the human's records until another bind/load, although the human's canonical records themselves were preserved. The strict real-save snapshot comparison remains unchanged; fields were not excluded to turn this failure into a pass. The full-engine fixture additionally checks actual parent ownership before any repairing getter, held maps and inactive skill-list identity.

## Legacy drug ownership

Previously, a saved animal-owned high, tolerance, addiction or chemical need could remain on the physical animal while the bridge projected separate human state. Returning to the original then exposed a different drug state. Initialization now reconciles the reviewed legacy objects before building projections:

| Saved state | Migration rule |
| --- | --- |
| Supported native effect exists only on the animal | Move the exact Hediff object to the human, retaining its load ID, native component state, severity, age and timers. Do not replay `PostAdd`, removal or ingestion. |
| Matching supported effect already exists on the human | Preserve the human source object's identity. Take the larger saved severity, age and disappearance time; never sum doses or reset age. Other canonical component state stays authoritative except for the explicit healing-clock rule below. |
| Both old bodies have native luciferium healing clocks | Keep the smaller saved `ticksToHeal`, preserving the nearer pending heal. Migration neither performs a heal nor rerolls the next interval. |
| Native chemical need exists only on the animal | Recreate the exact native need for the original pawn, retaining its saved level. Native need ownership is readonly and has a body cache, so the animal's need object is not reused. Preserve random state and avoid withdrawal notifications. |
| Both bodies have matching native chemical needs | Keep the human need object and the lower saved level, without granting a refill. Remove the migrated animal need and refresh native tracker fields/caches. |
| State cannot be safely reconciled | Leave it on its original body rather than guessing a missing level or consuming another system's need. |

Qualification requires exact native `Hediff`, `HediffWithComps`, `Hediff_High` or `Hediff_Addiction` classes already accepted by the existing drug bridge. Reviewed component classes are exact native Disappears, SeverityPerDay, DrugEffectFactor and HealPermanentWounds. Custom classes/components, part-bound effects, implants, unreviewed visual components, custom needs and gene-dependent dependency/craving state are outside this migration. The original human's gene-backed effects retain their existing bridge.

The migration is idempotent: reinitializing or reloading does not create another dose, increase the merged maximum duration, refill a chemical need, replace canonical IDs or reset a pending healing clock.

## Evidence and limits

Evidence is retained outside the runtime package at `outputs/wildshift-upgrade-20261102`. Raw failure results and diagnostics are preserved, including failures unrelated to the state comparisons.

| Evidence | Result / status |
| --- | --- |
| Final 1.6 / 1.7 production builds | Both compile with zero warnings/errors. |
| Final candidate against current native 1.7.4995 APIs | **504 checks pass:** 299 runtime, 65 stats, 91 health and 49 abilities. These are isolated native object graphs, not complete games. |
| Fault-injecting storage/recovery harness | **291 assertions pass**, including six new owner and skill-binding boundary checks. This is stub coverage, separate from actual-engine evidence. |
| Final candidate, actual 1.6 existing-save copy and native transform/return cycle | **22 probe checks pass** across load/resave/fresh reload with final `917953A3` DLL. Original save and normal configuration remain unchanged. Raw overall `Passed=false` retains the mod-pack diagnostics. |
| Immutable 1.6 source XML → final candidate's first native snapshot | **288 comparisons pass**, no mismatches or unsupported comparisons within the exported fields. |
| Actual older 1.7 save baseline with beta.2 | **18 probe checks pass**; source comparison has **1,358 passing comparisons**, no mismatches, **two unsupported comparisons**. This uses the older B4DADA beta.2 DLL, not the final beta.3 DLL. |
| Final candidate in current actual 1.7.4995 engine | **21 probe checks pass:** actual old-save load, production transform/return, native resave and exact fresh-process snapshot equality. Original save and normal configuration remain unchanged. Raw overall `Passed=false` retains native mod-pack diagnostics. |
| Immutable 1.7 source XML → final candidate's first native snapshot | **1,358 comparisons pass**, no mismatches, **two unsupported comparisons**. `ComparedFieldsPassed=true`, `ComparedFieldsComplete=false`; overall comparison remains `Passed=false`. |
| Final candidate full-engine controlled legacy-drug replay, 1.6 / 1.7 | **294 checks pass on each version**, all three fresh-process phases and normal-configuration preservation pass. Previous runs retain four failed assertions from unsupported fixture input. This controlled fixture is separate from actual old-colony saves. |

### Actual old saves

The September 13 source save was written by 1.6.4871 rev591 and loaded in the preserved native 1.6.4871 rev590 player. Its active animal contains its original human. Both the earlier candidate and final-DLL GUI-mode repeat retain the recorded three relevant Pawn identities, native health state, exported belongings/skills/genes, source clock and ownership. The old animal returns the exact original human and reuses that same animal on the next shift. Native loading, resaving, fresh reload and production cycling operate only on external copies.

The 1.6 profile retains **72 of the original 74 package IDs**, plus the test helper. Two unavailable packages, `olli.butterplusplus` and `dev.soeur.imageopt`, are explicitly excluded. `SameModProfile=false` remains in the result; no replacement identities were fabricated. This is not an exact original-mod-profile test.

The October 25 source save and its baseline player both use 1.7.4985 rev1269. That baseline retains all **75 original package IDs**, plus the helper, and contains nine human shapeshifters, including two dead Pawns; it has no active animal form. The final candidate also loads this copied save in actual **1.7.4995 rev309**, keeps all 75 original package IDs plus the helper, transforms and returns the selected living original `Ratkin4331924`, then passes exact paused snapshot comparison after native saving and a fresh-process reload. This includes the cached animal's parent-holder/map and inactive skill state that failed before the ownership fix. Original-save and normal-configuration hashes remain unchanged in both runs. This is a real old-human-save upgrade and cycle; it is not an old active-animal legacy-drug input.

The same two unsupported source comparisons remain in the baseline and final-current-engine runs: an ability whose charges were omitted from the save and a resource gene whose current value was omitted. Their current definition/default values were not guessed. The comparator therefore retains `ComparedFieldsPassed=true` and `ComparedFieldsComplete=false`, with overall comparison `Passed=false`.

Final raw records are `prebeta16-final-gui/result.json`, `comparison16-final.json`, `prebeta17-final-current-cycle/result.json` and `comparison17-final-current.json`. Earlier candidate and beta.2 baseline records remain separate. Known native Single fields use an explicit float comparison policy; raw skill levels, ticks, counts and part indices are not rounded into floats. The active animal's public skill interface intentionally resolves to the original human's canonical records; an inactive cached animal uses its own records. An absent old animal-storage field may initialize empty without inventing a body.

### Controlled full-engine replay

The extended three-process shared-state fixture retains the previous lifecycle checks. Immediately before the active-form save, it explicitly seeds real native animal-owned WakeUp high/addiction, AlcoholTolerance and a WakeUp chemical need at `.37`, plus two luciferium healing clocks at `600000` and `731`. The next fresh process must let production postload adopt those saved objects and verify exact IDs, single ownership, unchanged decay/age/need state, one projection per source and the nearer pending healing time. It then advances real native ticks and returns through the production path. The fixture also checks actual cached-body ownership and inactive skills through return and reload.

The first replay in each engine retained **four failed assertions**. Its manually seeded `WakeUpTolerance` is a leftover native definition not referenced by WakeUp's `ChemicalDef.toleranceHediff` or an ingestion outcome in either tested version; it is outside the existing production drug-discovery gate. The corrected fixture uses Alcohol's genuinely referenced `AlcoholTolerance` and explicitly requires `SharedFormHealth.IsSupported(tolerance)` before saving. Exact-ID, decay, projection and return assertions remain strict. No production gate was widened to accommodate unused input. The original failed outputs remain in `engine16-final-replay` and `engine17-final-replay`.

This fixture pauses before queued postload work completes so exact saved state can be checked before ticks. Its evidence declares `ControlledFixture=true` and `ActualOldSave=false`; manually constructed historical input is not represented as a user's actual old save. Source code, helper and production input hashes are recorded separately. Accepted corrected records are `engine16-final-corrected/result.json` and `engine17-final-corrected/result.json`: **294 checks per version**, zero failed assertions, no native ERROR verdict and unchanged normal configuration.

### Startup diagnostics

The first 1.6 batch-mode attempt crashed before the probe reached its component. The native stack enters Unity resource loading from `Verse.Text` and RimHUD theme/layout initialization on the load worker. The original runner, result and logs are preserved in `prebeta16-b4-cycle`. A later non-batch GUI-mode run, still using an isolated profile and hidden owned window, completed the state probe. No RimHUD or Unity method was mocked or replaced. This distinguishes successful testing under that mode from proof of an exclusive crash cause or a generally fixed third-party startup issue.

Full-profile runs retain external diagnostics. The 1.6 source already contains three unresolved human relation references; its other diagnostic categories include Mono fallback, Manito sound, radio HTTP 415 and translation-thread shutdown. In current 1.7, an observational generation trace attributes newly unresolved `Human6342556` to `RimTalk.Data.Cache.InitializePlayerPawn` during `LoadedGame`; WildShift's cow generation is a separate call. RimTalk was not modified. Kiiro image-format errors and the other raw diagnostics remain failures in the full-profile overall verdict, even though the bounded state comparisons pass.

### Remaining scope

Exported field preservation does not certify arbitrary custom Hediff/gene/ability payloads, private component timers beyond the explicit probes, every relationship/need/ideology tracker, item quality/style/color, or anatomy equivalence after a race definition changes. The controlled replay supplements these boundaries for the reviewed drug/chemical/healing state; it does not make arbitrary custom systems supported. No long-duration 0/20/50-form performance result is claimed here.

Frozen final production SHA256 values are `A4A74508B2C7F570DD4454ADF5FCAFF72DE7A8C21E6A329A3B3DE90DC275817F` (1.7) and `917953A3C4267824DE4E3E555C2C41C33AA040F968F0CC38BA806AD162981257` (1.6). Local package/installation identity and rollback are recorded separately in `outputs/wildshift-upgrade-20261102/release-record.json`; test helpers are excluded from the runtime archive.
