# Combat and first-transform latency — local v1.4.0-beta.8 candidate

This candidate is local only. Steam Workshop and GitHub remain at the published beta.7 release.

## Confirmed behavior defect

Native automatic combat uses `!Pawn.IsColonist` when deciding whether it may select manual-only weapons or apparel verbs. WildShift's player-controlled animal still has the native animal classification. When its original person wears a charged jump pack and has no usable primary weapon, the native selection order can therefore pick the manual jump before the animal's natural melee attack.

This was reproduced in actual RimWorld 1.7.5011 without Search and Destroy or Flashing Blades. Five drafted forms automatically jumped from their starting positions near x=15 to hostile positions near x=35, using one jump-pack charge each. They then began close combat. A separate diagnostic captured `PawnFlyer.RespawnPawn` → drafted `JobDriver_Wait` → `Pawn_MeleeVerbs.TryMeleeAttack` → `Verb.TryStartCastOn` → `TimeSlower.SignalForceNormalSpeed`; 37 normal-speed signals were observed across warmup and measurement. The jump verb's own `forceNormalTimeSpeed` flag is false: the observed speed limit followed the unintended landing and melee, not a special slowdown rule on the jump itself.

The fix scopes the colonist automatic-verb policy to `Pawn.CurrentEffectiveVerb` and `Pawn.TryStartAttack` for a live player-controlled transformed animal. Inside those entry points, the same actor's `TryGetAttackVerb` excludes manual-only actions. Nested queries and exceptions restore the previous scope through Harmony finalizers, without a per-call scope allocation.

Explicit `TryGetAttackVerb(target, true)` calls and manual jump orders retain their native behavior. Ordinary animals, NPCs, uncontrolled mental-state forms and the original turret branch retain their existing policy. The change does not alter genes, traits, health-effect sharing or native combat speed restrictions.

## Bounded combat comparisons

The native test profiles use Harmony, Core, all five DLCs, WildShift and a test-only observer. A cleared 75×75 arena contains five actors and the requested hostile population. Damage, downing, movement and native AI remain enabled. Target populations, jobs and actual firing are recorded rather than assumed fixed. Each table row below is a short independent process, not a long-duration or full-mod-pack performance guarantee.

| Case | Human mean tick | Transformed mean tick | Observation |
| --- | ---: | ---: | --- |
| Rifle, 120 initial hostiles, 2s warmup + 8s measured | 1.365 ms | 1.618 ms | Both fired 30 shots; native target speed was 60 TPS. Target counts were 120→120 and 120→119. |
| Native assault, 60 initial Hussars; Sanguophage actors; 1s warmup + 4s measured | 2.515 ms | 2.415 ms | Real AssaultColony duties, an identical extra human objective, 30 shots each; native target speed was 60 TPS. |

The large report-specific CPU stall was not reproduced by these rifle scenarios. The reporter's exact animal, equipment and mod profile were unavailable.

The same-helper jump-pack comparison used five transformed actors, 120 hostiles, 1s warmup and 4s measurement:

| Observation | Published beta.7 | Local candidate |
| --- | ---: | ---: |
| Automatic effective verb | Manual jump | Natural melee |
| Explicit manual lookup | Jump retained | Jump retained |
| Jump-pack charges at measurement start | 4 each after unsolicited warmup jumps | 5 each |
| Positions at measurement start | At the hostile group | Original starting positions |
| Mean whole-game native tick | 2.038 ms | 1.050 ms |
| Observed TPS / native target | 59.98 / 60 | 804.71 / 900 |

These rows have different resulting combat states: the fix prevents the unsolicited jump and resulting melee. The TPS increase therefore combines changed combat work with removal of a native forced-normal-speed condition. It is not a claim of a 13× CPU or FPS optimization. The diagnostic candidate recorded no speed signals and no charge use; ordinary intentional combat still retains native slow-time behavior.

The probe's activity counters reset at the measurement boundary. A zero `NativeJumps` count after warmup does not establish that no jump happened during setup/warmup; positions, charge values and the separate armed-before-setup signal observer establish the transition above. Inclusive method profiling also includes instrumentation and nested work and is kept separate from the uninstrumented timing rows.

## Validation and preservation

The earlier automatic-verb-only evidence, raw failures, build inputs and accepted results are retained outside the repository under `outputs/wildshift-combat-20261116`. Both production builds had zero warnings/errors. That candidate's API validation resolved 171 named Harmony targets and 24 injected fields on 1.6 and 1.7.5011.

The accepted full native runs are `fullengine17-v2` (actual 1.7.5011) and `fullengine16-v2` (actual 1.6.4871). Each passes **744 checks**, including 22 automatic-combat checks, across three fresh-process save/reload phases with all five DLCs and Dinosauria. Both report zero failures/native errors and unchanged normal configuration. These cover automatic/manual selection, ordinary/NPC/mental-state controls, nested getter scope and exception cleanup. Existing actual native jump execution, ownership and original state-sharing checks remain enabled. Exact runtime hashes are recorded in the output result.

Intermediate fixture failures remain separate: the initial arena tried to destroy a non-destroyable monolith; an ordinary-Warg apparel control assumed a human jump pack could be worn without its required waist body part. Those test prerequisites were corrected without changing gameplay methods. One 1.6 startup timed out before checks; its unchanged production/helper DLL retry is retained separately.

## CPU investigation: November 16–17

The follow-up separates a confirmed first-use stall from sustained combat tick cost. Missing B&S types caused `SapientAnimalsCompatibility` initialization to fall through Harmony's short-name search and enumerate types across all loaded assemblies. Whole managed-stack sampling attributed about 141 ms to that lookup and 39 ms to the subsequent transformation in the warmed colony. `OptionalModTypes.FindExact` now calls case-sensitive `Assembly.GetType` on loaded assemblies only. It retains the known fully qualified names and optional-mod APIs, skips expected reflection/load failures, and does not load assemblies or cache missing results.

Shared apparel getters are read once per modifier pass. Native stat-cache versions and projected-stage/health versions are separate: visible armor HP/quality and persona traits still invalidate cached animal stats in the same tick, but do not rebuild an unchanged projected stage or dirty every capacity. No whole-tick state cache was introduced; conditional and gene changes remain immediate.

A separate combat compatibility defect affected roaming species such as Muffalo and Dinosauria's Triceratops. Native roamer protection checks combat jobs rather than drafting itself. Raiders could ignore a freshly drafted form before its first natural `Wait_Combat` job. The new patch removes only that roamer exemption for a living, controllable, drafted person-form; ordinary livestock, mental states and the outer downed/invisibility rules remain native.

### Matched first-transform comparison

Both rows use the same helper, colony save, original 87-mod order plus the observer, existing Ratkin with 14 genes/24 hediffs and original gear, Triceratops form, and 60 rifle hostiles. The baseline is a source-audited copy of the final candidate with **only** optional type resolution changed back to the legacy Harmony lookup. All other fixes are identical. Neither row enables method timing patches or EventPipe sampling.

The fixture first runs 120 ordinary colony ticks and 30 unarmed NPC ticks, then arms the hostiles and starts their native assault Lord against the still-human actor for 15 ticks. It then transforms/drafts through production code and records two seconds. The player's native ThinkTree chooses its combat job; no forced combat wait, invulnerability or direct game/component ticks are used. Developer mode is enabled only for native autoload, then disabled before prewarming. Temporary NPC warm jobs are released, and debug-log windows are closed while retaining all error records.

| Metric | Legacy lookup baseline | Exact lookup candidate |
| --- | ---: | ---: |
| Transformation plus shapeshifter setup | 294.91 ms | 41.01 ms |
| Main-thread allocations in that transition | 52,220,256 bytes | 2,881,472 bytes |
| Draft toggle | 1.48 ms | 1.44 ms |
| Native tick mean | 8.36 ms | 7.83 ms |
| Native tick maximum | 38.21 ms | 48.49 ms |
| Enemy shots / actor bullet hits | 111 / 33 | 111 / 27 |
| Actors alive and standing, start/end | 1 / 1 | 1 / 1 |
| Enemies alive, start/end | 60 / 60 | 60 / 60 |

The confirmed improvement is approximately **86% less first-transform latency and 94% less transition allocation** in this profile. The steady tick means differ modestly, while maximum ticks and hit counts vary; this is not evidence of a general sustained TPS/FPS improvement. The original severe continuing combat stall was not reproduced in these bounded cases.

The two-second endpoint avoids casualty bias: a separate five-second run recorded 111 shots/57 hits and the actor became **downed**, remained transformed, and stayed on the map around three seconds. Its later downed phase is retained for diagnosis, not treated as constant-population active combat.

### Current checks, limits and evidence

- Actual 1.6.4871 and 1.7.5011 each pass **717 checks** across three fresh-process save/reload phases (`fullengine16-cpu4-v1`, `fullengine17-cpu4-v2`). These all-DLC regression profiles omit optional Dinosauria, unlike the earlier 744-check profile above. The new modifier-cache and real Muffalo threat tests exercise same-tick changes, long-lived caches, mental states, anesthesia and return.
- Both production builds have zero warnings/errors. Metadata validation resolves **172 Harmony targets / 24 injected fields** per engine, and production-linked optional-type checks pass **23/23**. The exact lookup tests include missing/existing types, late dynamic assembly loading, expected reflection failures and absence of target static initialization; installed B&S/VGE gameplay was not revalidated in this task.
- The real colony already records Kiiro texture-format errors, NewRatkin save dictionary errors and a FactionTraits world-pawn ownership error. They remain visible in the strict result, which therefore is not an overall mod-pack pass. Both matched windows have no newly recorded setup/measurement errors and preserve the normal Config, Saves, HugsLib and CameraPlus files and source save hashes.
- RimTalk generation, Persona Director generation/evolution and AutoTranslation requests are disabled only in private copied settings. This also omits some normal CPU work; the result does not exclude interactions with those features in another profile.
- Earlier fresh-load sampling found a roughly two-second Facial Animation initialization across world/map pawns. Installed FA initializes each component's dictionary once; no repeated story-proxy initialization was established. The final comparison warms this cost before the transition. Earlier forced-wait or zero-shot cases do not establish real drafted-combat performance.
- The first CPU4 1.7 regression attempt failed during an unrelated native quicktest ideology cleanup before the new threat tests; the unchanged candidate's retry passed. Raw logs and the earlier 1.6 startup timeout remain preserved, without claiming those native failures were fixed.

Evidence is retained under `outputs/wildshift-combat-cpu-20261116`: matched `live-triceratops60-legacy-v11-plain2` / `live-triceratops60-cpu4-v11-plain2`, `cpu4-legacy-optional-ablation-20261117` with the one-method source diff/manifests, and the separate sampled call trees. Managed stack samples include waiting/safe-point bias and are kept distinct from native tick stopwatches and OS thread CPU counters. Selective Harmony-inclusive method timings also overlap and include observer overhead.

The subsequent report specifically identifies selection as the trigger. Its separate paused GUI controls, real-hit selected/deselected combat comparison, health-generation counters and actual 1.6 GUI retry are recorded in [Selection-UI-20261117.md](Selection-UI-20261117.md). They confirm the cache split's behavior but do not yet establish reproduction or resolution of the severe selected-only slowdown. Earlier unselected TPS results must not be used as validation of that symptom.
