# Shared state — v1.4.0-beta.6

This release retains WildShift's separate human and animal Pawns. The original human owns personal state; the active animal uses non-owning views and narrowly scoped native calculations. Returning restores the original objects rather than reconstructing their saved values.

## What follows the person

| Area | Behavior while transformed |
| --- | --- |
| Genes and traits | Active native genes and unsuppressed traits participate in stats, capacities, metabolism, native rules and supported periodic effects. Conditional effects use the active animal's environment. |
| Health effects | Native drugs, alcohol/hangovers, Anomaly serums, neural supercharge and supported systemic buffs supply both bonuses and penalties. Their canonical timers advance once. |
| Implants and prostheses | Systemic implant effects apply. Installed-part performance can improve the animal's corresponding capacities; stronger existing animal prosthetics are retained without multiplying the same improvement twice. Human parts remain physically installed on the human. |
| Needs and mood | Personal food/rest/mood/chemical needs retain their levels. Personal relationships, ideology and relevant metadata are supplied during native thought queries, while surroundings are observed at the active body's location. |
| Abilities | Supported native abilities and psycasts use the original ability objects, cooldowns, charges and resources. Native targeting, focus, heat and other costs still apply. A player-controlled psychic animal can use **Meditate** to run the native meditation job at its current position. |
| Personal systems | Inspiration, mental states, applicable ideology role/precept modifiers and mechanitor control follow the active body. |
| Human gear | Original apparel and equipment supply native bonuses, armor, shields, weapon verbs and supported actions. Their original ownership, condition, energy and charges are preserved. |

Gene capacity modifiers retain native offset/factor/maximum semantics. Systemic health stages retain capacity, pain and damage modifiers rather than copying only a final stat number. Consequently, a penalty such as alcohol impairment follows the person too.

Installed-part scaling runs at the native raw-capacity step, before health/gene offsets and factors. For example, two 150% archotech arms and native elongated fingers (×1.1) produce 165%; adding a −20 percentage-point manipulation effect produces `(1.5 − 0.2) × 1.1 = 1.43`, rather than scaling that offset by the prosthesis ratio again.

Borrowed human apparel supplies gameplay effects without inventing animal textures. Apparel physically worn by the animal remains separate and keeps its existing species-specific rendering support. Only the animal body's own possessions are unloaded on return; borrowed human gear stays with its original owner unless explicitly dropped.

Meditation focus permissions follow the original person's backstory, traits, titles and implants. The animal's current environment determines whether the order is safe and which nearby focus is usable. This adds an explicit order rather than replacing the animal's entire AI with a human work tree.

## Body-specific behavior

The animal still has its species' body, attacks, capacities and physical wounds. Human wounds are not copied onto a healthy animal. As previously configured, ordinary animal wounds reset when entering that cached form again; animal prosthetics and serialized custom component state remain. Human and animal installed improvements are compared rather than blindly multiplied.

Native animal bonding, taming and trading relationships remain animal-specific outside the short personal-thought context. Ordinary animals do not receive the shared views.

## Compatibility boundaries

- Declarative gene/trait fields and reviewed native systems are supported. Arbitrary custom mod gene tick callbacks require an explicit compatibility opt-in; attaching every mod state machine to another species is not assumed safe.
- Native abilities and reviewed components retain their own eligibility and resource checks. A custom ability, body requirement or mod-defined tracker may need a separate adapter.
- Sharing an effect does not grant missing species textures, human anatomy or every human work behavior. Animal-only and human-only activities still have their native physical requirements unless a targeted bridge is documented.
- `GeneDef.marketValueFactor` changes a gene pack's price in vanilla, not a pawn's price. This candidate does not invent an animal price bonus for that field. Native trait pawn-price effects remain applicable.

## Validation

The frozen integration11 candidate passes **531 initialized-game checks on each of RimWorld 1.6.4871 rev590 and 1.7.5005 rev1082**, with all five DLCs. Each version uses three fresh processes: save a human with its cached animal, reload and save an active caravan form, then reload and return off-map. Normal user configuration hashes remain unchanged.

The checks include native ingestion of the four Anomaly serums, alcohol/hangover progression, gene capacity modifiers, clotting/healing/fertility, original prostheses and armor/shields, actual Skip/Invisibility and meditation, native jump-pack landing/charge use/reloading, mechanitor control, personal thought/precept creation, canonical timers and exact saved identity/ownership.

The same runtime DLL passes 100 isolated native stat checks, 102 native health checks and 71 native ability/system checks. Metadata verification resolves 158 named Harmony targets and 24 injected fields against both versions; dynamic targets are also exercised by actual game startup. The separate assignment and storage/fault harnesses pass 179 and 291 assertions respectively; those are engine-stub checks, not additional gameplay runs.

Validation records are retained outside the runtime package under `outputs/wildshift-all-shared-implementation-20261111`, including failed intermediate runs and their diagnoses. Fixture corrections (UI input context, native adjusted jump destinations and legacy food/rest expectations) are distinguished from product fixes. An earlier final-candidate startup timeout remains preserved; the unchanged DLL subsequently passes the full run.

A build or a headless result alone does not establish visual compatibility with every mod. These runs do not claim exhaustive gameplay coverage for all installed mods or a long-run memory-leak guarantee.

### Short performance comparison

Actual 1.7 headless engine; the same saved population of 50 people, stationary/drafted, with the abilities preset. Each case has 10 seconds of warmup and 60 seconds of measurement. All retain the configured approximately 900 TPS speed ceiling.

| Active animal forms | TPS | Mean native tick | Final process private memory |
| --- | ---: | ---: | ---: |
| 0 | 900.2 | 0.252 ms | 794.1 MiB |
| 20 | 900.6 | 0.404 ms | 809.2 MiB |
| 50 | 901.5 | 0.592 ms | 807.8 MiB |

Memory is the whole game process, not memory attributed only to WildShift. The cases use separate processes; small non-monotonic memory differences can reflect native loading and garbage collection. This is a short controlled comparison, not a working-colony, combat, rendering or long-run leak benchmark. The common baseline and normal user configurations retain their hashes.
