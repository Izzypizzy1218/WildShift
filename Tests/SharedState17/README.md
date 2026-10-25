# Native shared-state validation (TEST ONLY)

This helper runs inside an initialized RimWorld 1.7 quicktest game with Harmony, Core, **all five DLCs**, WildShift and `wildshift.tests.sharedstate17`. Its only fixture Harmony patch selects a 75-cell map. It does not replace native gameplay results. Headless execution verifies state and lifecycle behavior; it does not verify rendering or optional-mod compatibility.

The first process creates an original human, spouse relation and marriage memory, a level-8 Melee record with major passion, and exactly 17 silver. Native luciferium ingestion seeds the person's drug effect and chemical need. Production transformation must bind the animal's native skill lookups to that original record and apply a learning event once. Native source gene/trait additions and removals must affect the animal's movement stat in the same tick.

A fixture implant is a real `Hediff_AddedPart` with a custom component serialized as state **731**. Separate limbs receive a native Cut and missing-part condition, plus blood loss. Production return must preserve the original human and cache the same animal with its implant, without making the inactive animal own the active human. The real game is saved with this inactive body deep inside the production holder.

The second process loads that save and verifies both pawn IDs, the implant ID, exact body part, component state, spouse/memory and XP. Production transformation must reuse that body, preserve its implant, clear ordinary injury/blood loss/unrelated missing parts, and reconnect shared skills and drug projections. Native map and caravan ticks check single source drug progression, chemical-need decrease and memory aging. Native ingestion by the animal must replenish canonical human drug state without separate animal addiction. Repeated cycles check ownership and body reuse. Three independent native health probes also cover ordinary animal death at zero body-death chance, deterministic lethal-drug ingestion, and luciferium healing only the active animal scar. The scar probe accelerates the native healing timer without replacing the healing code.

The second process saves again with the animal active in a caravan and the original human inside it. The third process reloads this ownership direction and verifies pawn/implant IDs, state 731, canonical skill identity, source-drug identity and exact projection references. Another 300 native caravan ticks precede an off-map return, cached-body ownership checks and the exact 17-silver inventory check. Both storage directions therefore pass through a real full save and a fresh process.

Build outside the repository against the candidate production DLL:

```powershell
./Tests/Run-SharedState17Tests.ps1 -BuildOnly -WildShiftPath C:/candidate/WildShift.dll -Output C:/evidence/shared-state-build
```

Stage the generated helper folder separately in the game's mod search path, then close RimWorld and run:

```powershell
./Tests/Run-SharedState17Tests.ps1 -SkipBuild -FixtureDir C:/discoverable-mods/WildShiftSharedStateValidation -WildShiftDir C:/discoverable-mods/WildShift -Output C:/evidence/shared-state-run
```

Use actual game-discoverable paths and a fresh output directory. The runner never installs helpers, swaps production DLLs, publishes, or changes normal mod selection. It isolates savedata/config, compares the full normal configuration file/hash inventory before and after, verifies actual loaded DLL hashes and mod IDs, and refuses an existing game. Only its own launched process can be terminated on timeout. `result.json`, phase logs and `profile/SharedStateResults` contain the evidence.

Exclude this helper and its fixture definition from production packages and Workshop uploads. Compile success alone does not establish that these gameplay checks passed.
