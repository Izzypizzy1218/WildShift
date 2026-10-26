# Shared state expansion — v1.4.0-beta.2

This local candidate expands [beta.1's preserved-body model](SharedState-v1.4.0-beta.1.md). The original human remains the authority for personal state; the real animal Pawn is the active physical body. It does not turn the human into an animal-looking human or copy the human's final stats onto another anatomy.

## Behavior

| State | Animal-form behavior |
| --- | --- |
| Identity and skills | Retain the exact original identity, relations, memories, skill records, passions and XP. Animal-form learning updates the original records once. |
| Gene and trait stats | Apply active gene and unsuppressed trait offsets/factors through the animal's native stat calculation. Matching native effects already on the active body are excluded from the projection. |
| Conditional gene modifiers | Evaluate each applicable predicate against the active body. Clothes, sunlight, roof, location and other native conditions can enable both bonuses and penalties. Condition changes invalidate that body's queried stat cache in the same tick; the stat explanation uses the same projected modifiers. |
| Supported gene abilities | Expose the original human-owned ability objects to the active body's ability interface. The animal becomes their caster; cooldowns, charges, component state and saved identities remain canonical. Returning restores their human caster. |
| Hemogen | Keep the native resource gene and supported drain on the human. Animal ability costs, bloodfeeding/resource changes and the resource display use that same pool. No separate animal hemogen gene or pool is created. |
| Drugs and reviewed buffs | Keep supported source Hediffs, progression and timers on the human. Separate projections apply their current effects to the animal. Applying a supported buff to the animal updates the canonical source; returning does not reset its duration. |
| Animal body and gear | Reuse the same cached animal Pawn, retaining prosthetics and serialized component state. Ordinary animal wounds, unrelated missing parts and blood loss reset on the next shift. Animal clothing/equipment/cargo still unload on return, with the existing human-inventory fallback. |

Human injuries and implants remain on the human. The existing native drug/dependency bridge and luciferium healing of the active body continue; the hidden human does not run a second complete AI/needs/environment tick. Other biological needs and arbitrary gene execution are not added by this expansion.

## Gene abilities

The reviewed native gene-granted set is:

| Group | Abilities |
| --- | --- |
| Attacks and sprays | Fire spew (`FireSpew`), acid spray (`AcidSpray`), foam spray (`FoamSpray`), piercing spine (`PiercingSpine`) |
| Hemogen utility | Bloodfeed (`Bloodfeed`), coagulate (`Coagulate`), longjump (`Longjump`), resurrect (`Resurrect`) |
| Animal control | Animal warcall (`AnimalWarcall`) |

An ability must belong to an active original gene and use the reviewed native ability, verb, gizmo and component types. Native targeting, resource, cooldown and charge requirements still apply. A suppressed/removed source gene cannot keep casting through a borrowed button. The animal's own native abilities are retained; a matching borrowed ability is exposed once. Saved ability objects remain deep-owned by the human rather than serialized as a second animal copy.

Xenogerm implantation (`ReimplantXenogerm`), psycasts, world-targeted abilities, externally managed cooldowns, custom ability subclasses/components and arbitrary mod resource systems are outside this bridge. Mod gene abilities can qualify when they use the same supported native structures; this is not universal mod-ability compatibility. Arbitrary custom gene ticks are not executed on the stored human.

## Reviewed timed buffs

Both the definition name and originating package must match the reviewed entry:

| Source package | Supported definition |
| --- | --- |
| Kiiro — `ancot.kiirorace` | `Kiiro_AteCatmint` |
| Miho — `miho.fortifiedoutremer` | `Miho_Sandevistan_Hediff` |
| Ratkin — `solaris.ratkinracemod` | `RK_Hediff_LanceChargeFocus`, `RK_Hediff_LanceChargeExhaustion` |
| Ideology — `ludeon.rimworld.ideology` | `WorkDrive`, `PreachHealth` |
| Royalty — `ludeon.rimworld.royalty` | `PsychicAnesthesia` |

These definitions must still pass the compatibility gate. The supported state is declarative `Hediff`/`HediffWithComps` with only the reviewed native severity/disappearance components. The bridge retains the actual source severity and timers, including negative effects such as lance-charge exhaustion. It does not rebuild a buff from only its definition or reroll its duration when shifting.

Miho's `Miho_Sandevistan_Hediff` entry preserves legacy/existing save state. No current application route was found in the installed modern Miho version, so current native activation there is not claimed as tested.

Ordinary supported buffs must be body-wide. The two native Ideology effects, work drive and preach health, may instead target the pawn's exact brain; that attachment maps to the original human's exact brain. This narrow exception does not map prosthetics, other body parts or arbitrary part-bound mod effects.

### Opt-in for compatible definitions

A mod author can add this extension to a compatible HediffDef:

```xml
<modExtensions>
  <li Class="WildShift.SharedFormBuffExtension" />
</modExtensions>
```

Opt-in permits evaluation; it does not bypass the gate. Both the definition and actual runtime instance must use the exact supported native classes. Allowed component classes are `HediffComp_SeverityPerDay`, `HediffComp_Disappears` and `HediffComp_DisappearsOnDeath`; custom subclasses or injected components are rejected. Implants/default installation parts, chemical-need creation, removal-spawn behavior and unsupported disappearance conditions are excluded. Opted-in buffs remain body-wide; the native Ideology brain exception does not extend to other definitions.

Adding the extension does not enable arbitrary regeneration components, oxygen systems, aura emitters or custom state machines. Those systems need separate reviewed integration. The existing native drug/dependency bridge has its own supported-class checks and is not replaced by this buff gate.

## Persistence and validation scope

Only one body owns the other at a time. The original human saves canonical ability, gene/resource and source-buff objects once. Animal projections save references to those sources; cached inactive animals do not tick another copy. Existing animal-body reuse, implant retention, belongings preservation, death rules, awakening and caravan protections remain in effect.

The final candidate also fixes a world-generation error found during the mod-pack UI run: control, selection, draft and training guards now inspect the pawn's own faction without querying a player-faction singleton before that faction exists. Ordinary faction leaders and animals retain their native classification.

Completed candidate validation is recorded outside the runtime package under `outputs/wildshift-1.4.0-beta.2`:

| Evidence level | Completed result |
| --- | --- |
| Matching 1.6 / 1.7 production builds | Both pass with zero warnings/errors. |
| Final production DLL against native 1.7 APIs | **469 checks pass:** 299 runtime/identity including the world-generation regression, 65 shared stats, 56 health/buffs and 49 abilities. These use isolated native object graphs. |
| Initialized 1.7.4985 rev1269 game | **259 checks pass** across three fresh processes and both ownership-direction saves. |
| Actual Unity 1.6.4871 rev590 | **259 checks pass** across the same three-process lifecycle. |
| Actual 1.6 with Animal Apparel: Framework, Basic Armor, Universal Basic Armor and Draftable Animals | All **259 gameplay checks pass**, but the strict runner result remains **failed** for one appended CRLF in normal `Prefs.xml`. XML settings are unchanged; the read-only audit preserves the original failure. |
| Actual Windows 1.7 graphical mod-pack session | **Selected manual checks pass** with the original 75 packages, three armor packs and test helper (79 loaded). Loaded IDs, final DLL and unchanged normal configuration are verified. The unfiltered automated finalizer remains **failed** for five external/other-mod ERROR messages. |

The two accepted final-candidate engine runs use all five official DLCs and report unchanged normal user configuration: `engine17-release/result.json` and `engine16-release-core/result.json`. They cover native ingestion/learning/ticks, real ability casts and resource costs, brain-bound buffs, implants, caravan return, and persistence of canonical ability/buff IDs, cooldowns, charges and source references. `engine16-release/result.json` preserves the gear/Draftable run's successful gameplay phases and failed configuration-preservation result. Older candidate and diagnostic runs remain separate from these final-candidate results.

Final production DLL SHA256 values are `B4DADA5214EAA7B607BE917956A1DD33B0664251809317610949BD8ACF16B211` (1.7) and `F811AE8193D729F7B2BB1EF2E23C6BC33C9EB0823EDD936E0FE8AD83E16B1F1F` (1.6). Native test logs and engine records retain their exact input hashes. Test-only helpers are excluded from release packages.

## Scoped graphical verification

The final-candidate session was observed live in the actual Windows 1.7.4985 rev1269 player. `operator-final-ui.md` and `modpack78-ko-gear-release/result.json` retain the operator verdict and unfiltered automated result.

| Manual interaction | Observed result |
| --- | --- |
| Ordinary/transformed wargs and thrumbos wearing steel animal plate | Armor is visible on the map; transformed colonist portraits also display it. Visibility was assessed on screen, not inferred from apparel trackers. |
| Return the armored warg | The original human and clothing return. After moving the human away, the ground armor was directly selected: normal quality, 290/290 HP. |
| Mixed human/thrumbo selection and drafting, then human-to-warg transformation | Native drag selection and grouped drafting work. The other pawn remains selected; the same reused warg remains drafted and unarmored, with its armor preserved on the ground. |
| Native Kiiro catmint-food consumption, human → cougar → original human | The actual food stack decreases from 3 to 2. The catmint effect remains visible in all three native state records; the exact original human and its three original clothes return. |

The state records retain warg `Thing_Warg2574` and original Kiiro `Thing_Kiiro_Race2586`. Evidence is under `modpack78-ko-gear-release/profile/ModPackVisualResults`, including the prepared record and manual records after food, cougar transformation, human return and final forms. Earlier fire spew and animal warcall UI casts are documented separately in `operator-pre-fix-ui.md`; the final candidate's casting lifecycle is covered by the accepted engine/native suites.

The final graphical session logs two missing external Vulkan-overlay JSON registrations (Epic/Rockstar), two Kiiro Story Events Expanded texture extension/content mismatches and one missing Shushu faction leader. Its raw `AutomatedEvidencePassed=false` is retained. The earlier wrong-language-alias/name-table diagnostic is separate; the final session does not repeat the captured WildShift early-player-faction error or the helper's indestructible-monolith error. The selected WildShift interactions passed, while the entire mod pack remains outside an error-free certification.

This verification covers the selected animals, armor and interactions in a fresh isolated profile. It is not a long-running real-colony test or proof of every race/equipment pairing. The 1.6 gear run's exact trailing-newline difference and unchanged XML settings are recorded in `prefs16-release-normalization-audit.json`; that audit neither rewrites the raw failure nor attributes the file write to a process.

This is a local candidate. No Workshop or GitHub publication is claimed here.
