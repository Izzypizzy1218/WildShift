# Animal Apparel runtime audit — v1.2.15

Date: 2026-09-11. Game: locally installed RimWorld 1.6.4871, Windows/Unity rendering enabled.

## Result

The report that supported plate armor disappears specifically on a WildShift form was **not reproduced**. Actual engine renders show plate armor on ordinary and transformed wargs, horses, grizzly bears, cougars and thrumbos. This is a bounded result for the tested mod versions and fresh pawns, not proof that every save or mod combination is unaffected.

A separate compatibility defect was found: `AA_ChameleonYak` and `AA_SandProwler` use `VEF_GraphicByTerrainRenderTree`, which Animal Apparel: Framework does not patch. They had no animal-apparel render root. v1.2.15 adds that root only when the Framework tag exists and the tree has no such root already. This is a one-time XML patch with no per-tick scanning or per-draw Harmony hook.

Those two races also lack the tested armor artwork. The patch enables their apparel rendering pipeline but **does not make missing plate textures appear**. It is not presented as a fix for the unidentified original plate report.

## Exact mods

| Mod | Package ID | Workshop ID |
| --- | --- | --- |
| Animal Apparel: Framework | Ingendum.AnimalApparelFramework | 3513825850 |
| Animal Apparel: Basic Armor | Ingendum.AnimalArmorBasic | 3513849448 |
| Animal Apparel: Universal Basic Armor | Ingendum.AnimalArmorUniversal | 3524467381 |

These are different from the older Animal Gear / Animal Equipment packages named in the initial report. Framework explicitly declares incompatibility with `Dylan.AnimalGear`; both should not be enabled together. Giddy-up was not enabled in this audit.

## Scope and method

- Separate temporary save-data profiles; the user's original configuration and saves were not edited or loaded.
- Minimal profile: Core, installed DLCs, Harmony, the three Animal Apparel mods, WildShift and an isolated audit helper.
- Expanded profile: the user's 71 active packages plus the three Animal Apparel packages and the audit helper. Mod settings used fresh defaults, not the user's personal settings.
- 337 distinct animal ThingDefs, represented by one loaded PawnKindDef each. All six Basic Armor definitions were exercised on ordinary player-faction animals.
- The 179 races eligible under default WildShift form rules were also exercised via the real `TransformUtility.TransformToAnimal` path. Race-affinity-only forms, every life stage, every alternate PawnKind and existing saves are outside this matrix.
- Final baseline: **3,096 race/form/armor combinations**. The helper relied on the normal apparel-change notification, without forcing a graphics refresh. It checked successful wear, apparel nodes, graphics and draw requests in four directions in both map and portrait contexts, plus drafted state on transformed animals.
- PNG samples were generated through RimWorld's `PawnCacheRenderer` with graphics enabled. The full matrix verifies render requests; it is not a pixel inspection of all combinations.

## Baseline findings

- **310 combinations with species textures:** all produced an apparel node, graphic and draw request in all eight direction/context checks; all applicable drafted checks passed.
- **18 combinations could not be worn:** some Alpha Animals lack the body parts required for the tested helmets. These are not graphics failures.
- **24 worn combinations lacked an apparel node:** six armors on the two VEF terrain-tree races, in both ordinary and transformed states. This is the defect addressed here.
- The remaining invisible combinations had no matching species artwork. Universal Basic Armor deliberately sets `AnimalFallbackInvisible`, so they retain equipment effects without a graphic.

Visible armor coverage for ordinary animals in this installed set:

| Armor | Races with artwork and a draw request | Total tested races |
| --- | ---: | ---: |
| Animal plate armor | 51 | 337 |
| Animal simple helmet | 48 | 337 |
| Animal flak jacket | 19 | 337 |
| Animal advanced helmet | 10 | 337 |
| Animal cataphract armor | 36 | 337 |
| Animal cataphract helmet | 30 | 337 |

For example, warg and horse plate armor displayed; muffalo plate armor had no matching species texture. Thus “plate armor” alone is insufficient to determine whether missing graphics are expected.

The expanded batch launch also logged unrelated mod initialization/translation messages, including a RimHUD `OnGUI` initialization exception in one run. The audit completed; it does not certify unrelated mods as error-free.

## Validation after the patch

The same 3,096-combination matrix was rerun with the exact release XML patch loaded in the isolated helper. Worn combinations missing an apparel node fell from 24 to **zero**. All 310 combinations with species artwork still passed all eight direction/context checks and applicable drafted checks; the helper recorded zero exceptions.

Four additional, explicitly synthetic checks supplied a known existing texture to the two affected races in ordinary/transformed states. All four produced graphics and draw requests in every direction and context, confirming the repaired path independently of their absent species artwork. This substitution existed only inside the test helper, was restored immediately, and is not in the release.

Native API/XML verification passed: 31 existing Harmony targets, 10 injected fields, 10 exact enemy signatures and 12 XML files. The prior 352 logic/stability assertions were not rerun because runtime C# did not change.

## Delivery

The runtime DLL is unchanged from v1.2.14. This release adds the optional XML compatibility patch, accurate load-after entries and these findings. Raw per-combination results, mod lists, the audit helper source and rendered samples are retained in the local release verification artifacts.
