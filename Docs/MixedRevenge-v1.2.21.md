# Mixed revenge force — v1.2.21

Revenge now brings the sold original, ordinary animals of its species, and additional shapeshifters. Additional allies are generated as independent adult humanlike pawns capable of violence. Each receives a supported animal form, transforms through the existing enemy queue, and keeps its own original body for return, capture and save/load.

The total budget remains **the home map's native baseline raid points at arrival ×2**. The leader's animal form counts toward this budget. Roughly **30% of the remaining companion budget** is allocated to other shapeshifters; their actual assigned-form combat power is subtracted before ordinary animals fill the remaining budget. Ordinary animals retain the leader's species. The number of animals is rounded to whole pawns, with the same half-animal rounding tolerance as v1.2.20.

At least one ordinary animal is reserved. Eligible ally forms are deduplicated by species and exclude forms configured for automatic death on downing. Affordable species different from the leader's are preferred. The first ally may exceed the 30% target if necessary to produce a mixed force, but cannot spend the leader/ordinary-animal reservation. If no supported ally fits the available budget, the force uses ordinary animals. The 30% figure is a combat-power target, not a fixed fraction of pawn count. Allies can share a species with one another.

The native assault, downing/return and retreat behaviors apply to the new people. Generated allies do not replace the sold original or any existing world pawn. Preparation failures clean up only newly generated unplaced companions; the sold original is extracted only after force preparation. A generated original already stored inside a spawned form is not destroyed by cleanup.

The sale probability, 120–150 day schedule, v1.2.19 migration, arrival delays, cancellation and temporary GC retention are unchanged. Existing pending events use the mixed composition when they arrive. Existing on-map raids are not retroactively edited.

## Validation and visible demonstration

The mixed-force regression checks the shared budget using the actual spawned kinds, ordinary animals alongside transformed allies, independent body ownership, different assigned species, persistence across a separate save/load process, native combat and return to the same allied human while retaining raid membership. Existing sale and scheduling controls are retained.

The visible demo uses an isolated test map, actual native orbital sale and the production revenge method. Its test-only panel pauses before transformation, runs the production transformation queue, resumes real game ticks for combat, and calls the production return method to display an ally's original body. It advances time and arranges the test pawns for visibility. The panel and fixture are not included in the production mod or release ZIP.

The recorded screen demonstration reached arrival: the sold Rook, three other shapeshifters (toxalope, scimitar cat and pig forms), and four ordinary wargs. The baseline was 516.6, doubled budget 1033.2 and actual assigned-form power 1045 after whole-animal rounding. Screen control was stopped before transformation/combat/return were visually confirmed. Those behaviors were checked by the automated engine regression, not by the arrival screenshot.

Final review of the demonstration log found an Odyssey training-command exception: a non-player transformed animal can learn AttackTarget without having player master settings, which the native gizmo enumerator assumes exist. A narrow training-gizmo prefix suppresses these player-only commands for non-player transformed animals. Player-owned forms and ordinary animals retain native behavior. This also covers existing transformed animals after load.

The Core-only and all-DLC engine suites passed the sale/schedule/composition/combat/return checks across separate save/load processes. Their actual armies used 3145/3200 and 3105/3114.402 combat power respectively, within the whole-animal rounding tolerance. The Core combat fixture initially placed a target outside the map; clamping that fixture position resolved the test-only failure without a product change.

The final Odyssey gizmo regression passed 13 engine checks across a separate save/load process. Temporarily removing only the new prefix reproduced the native NullReferenceException on a hostile trained warg; restoring it made full pawn gizmo enumeration safe. After reload, the same animal remained safe to inspect, and recruiting it restored the native player attack command. The final DLL also passed 11 combat/load/return checks in each of the existing Core and all-DLC raid saves, with 19 and 21 native attack attempts respectively. Eighteen special-training checks, eighteen revenge-budget checks and the native API/XML audit (38 Harmony targets, 10 injected fields, 10 enemy signatures, 14 XML files) also passed. Earlier logic/race/stability suites passed 89/17/246 checks.

No global rendering patch or new tick scan was added. This is not a full optional-mod visual compatibility audit.
