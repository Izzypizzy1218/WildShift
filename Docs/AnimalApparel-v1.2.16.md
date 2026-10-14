# Animal Apparel refresh recovery — v1.2.16

2026-09-11 · RimWorld 1.6.4871 · Animal Apparel: Framework / Basic Armor / Universal Basic Armor.

## What this fixes

A transformed pawn can keep a stale, already-resolved render tree if another Harmony prefix suppresses `Pawn_ApparelTracker.Notify_ApparelChanged`. The apparel tracker still contains the equipped armor and its effects, but the renderer can keep the earlier unclothed tree.

v1.2.16 checks transformed animals after `Notify_ApparelAdded` and `Notify_ApparelRemoved`. If the normal notification already invalidated the tree, the repair returns immediately. Otherwise it compares worn apparel with apparel references in the render nodes and calls the native `SetAllGraphicsDirty` only when they differ. A deferred check also runs after save loading completes.

The repair requires Animal Apparel: Framework, only affects WildShift animal forms, and preserves `AnimalInvisible`, missing-art fallback, and the framework's own drawing code. It adds no map-tick polling and no render-time Harmony patch. It neither generates missing textures nor replaces graphics with those of another species.

## Original report remains distinct

The original reporter's conflicting mod/settings/save state has **not** been identified. The local normal-notification runs did not reproduce transformed-only disappearance. This release is a verified repair of the missed-notification condition, not confirmation that the original report has that cause.

The installed Steam subscription was v1.2.11, while the local mod was v1.2.15. Their paths and assemblies were inspected separately. A Steam-version launch did not yield valid map-draw observations and is not counted as a graphics pass.

## Actual map validation

Unlike the v1.2.15 matrix, these tests ran without `-batchmode`, observed the native map renderer, and captured the real map camera. Early batch-mode lifecycle runs yielded no map-draw evidence and were discarded for graphics conclusions.

Five ordinary animals and five actual WildShift forms were compared: warg, horse, cougar, grizzly bear and thrumbo. All wore the same `Apparel_PlateArmor_Animal` definition. The fixture generated a separate colony and used isolated configuration/save folders.

| Condition | Ordinary animals | Transformed animals |
| --- | --- | --- |
| v1.2.15, normal notifications, minimal mod set | 5/5 visible | 5/5 visible |
| v1.2.15, normal notifications, expanded mod set before reload | 5/5 visible | 5/5 visible |
| v1.2.15, deliberately suppressed change notification on forms | 5/5 visible | **0/5 visible**, despite 5/5 wearing armor |
| v1.2.16 candidate, same deliberate suppression | 5/5 visible | **5/5 visible** |

The suppressed-notification comparison also covered 600 simulation ticks and drafting: the old version remained invisible on the five forms, while the repair remained visible. After restarting into an isolated save, all ten candidate subjects displayed armor; re-equipping and reverting/retransforming also displayed armor on all ten subjects. The candidate completed without a test exception. Fresh native trees after load also displayed armor in the old version, so this test does not claim save loading itself caused the defect.

The suppression was an explicit test-only Harmony prefix, not an identified behavior of one of the reporter's installed mods. It was never added to the release assembly. Early fixture images before the teleport-render position was reset were not used for visual comparison; the retained comparison shows distinct left/right subjects.

The expanded profile used the user's 71-package list, minus AutoTranslation to avoid unrelated network requests, plus the three Animal Apparel mods and the fixture. Existing settings for Custom Portraits, Show Me Your Hands and Head Apparel Tweaker were copied into the isolated profile. An expanded save-load attempt did not complete and is not counted as a successful save-load check; successful restart/load validation above used the minimal profile.

## Checks and delivery

- Production build passed against installed RimWorld/Harmony assemblies.
- 246 existing stability assertions passed.
- Native verification passed: 33 Harmony targets, 10 injected fields, 10 enemy signatures and 12 XML files.
- No original user save or configuration was edited. Trial assemblies used a separate candidate mod before local installation.
- The release contains the recovery code, existing v1.2.15 terrain-tree compatibility patch and updated documentation. Local evidence retains raw observations, actual camera captures and the isolated fixture source.

On an affected existing animal, removing and re-equipping its armor triggers the new consistency check. A later successful save reload also schedules a check. The original report still requires confirmation on the reporter's environment.
