# Selected-form combat performance follow-up

Status: **the reported severe selected-only slowdown is not yet reproduced or confirmed fixed**. This follow-up tests the new report that combat runs normally until the transformed pawn is selected. Earlier unselected combat/TPS results do not validate that condition.

No production code or installed runtime was changed by this follow-up. CPU4 remains the unpublished local beta.8 candidate described in `Combat-v1.4.0-beta.8.md`. All new helpers are test-only and outside runtime packages.

## Reproducible inputs

- Actual 1.7.5011 engine, original colony copied into a private profile, original 87-mod order plus the test observer.
- Existing Ratkin523418 with 14 genes, 24 initial hediffs, original armor and shield. The source has no psylink. Native production transformation/drafting and ordinary ThinkTree combat remain active.
- Public beta.7 DLL: `21909CBD426378C14ABC8432C9F0F5284D422837A9915051EB62AA04F48CE3A1`.
- CPU4 DLL: `83C8084F051FBD9310B8ED51947061FCF681EF34C5DE338751D07A7A9F3BA0DA`.
- Normal saves/configuration are hashed before and after. Private RimTalk generation, Persona Director generation/evolution and translation requests are disabled. This also omits some CPU work and does not exclude interactions with those features.
- RimHUD 1.17.5 is installed and its HUD executes on this 1.7 engine. CM Color Coded Mood Bar is not installed locally; its current implementation is not represented by these tests.

Evidence root: `C:/Users/집/Desktop/codex/outputs/wildshift-selection-ui-20261117`, outside the repository.

## Frozen-state GUI control

The public beta.7 Warg form is paused, selected/deselected/reselected for six seconds per phase, after an unmeasured selected preroll. Raw pawn state and camera must remain identical. No EventPipe or broad method profiling is enabled. Each phase has about 360 Root updates and Repaints, and no game ticks. The physical mouse is outside the HUD/gizmos; hover and information tabs are not tested by this control.

| Enemies | Phase | Mean Root update | Mean OnGUI call | Observed frame interval rate |
| ---: | --- | ---: | ---: | ---: |
| 0 | Selected | 1.83 ms | 1.04 ms | 60.0/s |
| 0 | Deselected | 1.49 ms | 0.33 ms | 60.0/s |
| 0 | Reselected | 1.42 ms | 0.52 ms | 60.0/s |
| 60 | Selected | 4.83 ms | 0.95 ms | 60.0/s |
| 60 | Deselected | 3.42 ms | 0.31 ms | 60.0/s |
| 60 | Reselected | 3.27 ms | 0.49 ms | 60.0/s |

Both bounded selection sequences pass with no new native errors. Selection has a measurable GUI cost, but this control does not reproduce the reported collapse. A paused control cannot exclude hit-driven health/portrait invalidation.

## Real-hit combat and generation counters

The Thrumbo form faces 20 native rifle hostiles. The source receives real native MindNumbSerum and Metalblood before production transformation, with before/after state recorded. The game runs a one-second native combat preroll, then selected/deselected/reselected phases of two seconds each at native Normal speed. Actual damage, armor HP changes and injuries remain active; no invulnerability, forced ticks, forced player combat wait or damage overrides are used. The actor remains alive, standing and drafted in every phase.

The same v15 helper (`F30CA24A46433FB1230F529A9A411D8FFFB75C9A1BE9D186E65A8475AF14CCCD`) observes existing primitive generation fields and DirtyCache calls. It does not reevaluate source signatures, conditional affecters or CurStage. Diagnostic overhead is included; these are not uninstrumented performance claims.

| Build | Phase | Actor damage events | Stat generation changes | Health generation changes | Actor DirtyCache calls, all callers | Mean OnGUI call | Frame interval rate |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Public beta.7 | Selected | 20 | 20 | 20 | 29 | 2.14 ms | 52.1/s |
| Public beta.7 | Deselected | 2 | 10 | 10 | 9 | 1.68 ms | 53.5/s |
| Public beta.7 | Reselected | 35 | 32 | 32 | 53 | 2.31 ms | 43.8/s |
| CPU4 | Selected | 29 | 23 | 4 | 19 | 2.06 ms | 53.9/s |
| CPU4 | Deselected | 4 | 12 | 8 | 12 | 1.73 ms | 52.9/s |
| CPU4 | Reselected | 36 | 29 | 9 | 25 | 2.27 ms | 44.9/s |

Both bounded sequences pass with no new native errors. CPU4 avoids mapping every native-stat gear change to a health generation, as intended. The GUI/frame results remain similar and do not establish a fix for the severe report. Hits and resulting combat states differ, so reductions in total DirtyCache calls or frame timings are not a controlled causal speedup percentage.

Public beta.7's full gear signature includes armor HP. Its next health preparation can dirty wound, portrait/atlas, capacity, summary and meditation caches even when only native-visible armor state changed. CPU4 splits native stat and projected-health signatures. Native-visible armor still immediately invalidates pawn stat caches, while an unchanged projected stage need not dirty all health. Capacity UI queries reach this preparation through the `PawnCapacitiesHandler.GetLevel` patch.

Generation changes and DirtyCache calls are distinct: the observer counts all native and WildShift DirtyCache callers, and a health version change is only prepared when the relevant getter executes. CPU4's projected-only version is not directly observed by this common baseline-compatible helper. Remaining context/projection changes must not be called armor-driven merely because the full systems version also changed.

The separate one-second plain beta.7 run records 57.6/43.6/45.9 frames per second for selected/deselected/reselected, with 1/26/0 actor damage events. It also does not reproduce a selected-only collapse; its uneven hit distribution is why the longer counter comparison is retained separately.

## Validation limits

- Strict 1.7 copied-profile wrappers remain failed because the original save/mod pack already logs Kiiro texture incompatibilities, NewRatkin null dictionary keys and a FactionTraits world-pawn Lord ownership error. Bounded selection passes are not claims that the full mod pack is error-free.
- Native Root update, OnGUI and tick stopwatches are separate. Frame-start interval rates include rendering and pacing; they are neither pure CPU timings nor TPS. Nested method timings must not be summed.
- Earlier EventPipe runs had approximately four frames per second in every selection phase. They are retained as perturbed diagnostic observations, not evidence of the user's selected-only slowdown.
- Two actual 1.6.4871 attempts (`native16-cpu4-selection60-v14`, `...-v16`) recorded zero GUI callbacks/Repaints, despite Root updates and reported 1280x720 dimensions. They fail the GUI sampling requirements and are excluded from UI performance conclusions. The managed GUI entry variant and explicit screen dimensions did not repair that fixture condition.
- Read-only audits of native gizmos, melee commands, meditation, ability refresh, RimHUD's configured docked layout and the inspected installed overlays did not establish a new per-selected-form all-enemies loop. That negative audit does not establish compatibility with the reporter's unknown mod profile or HUD layout.

The previous production regression checks remain as recorded in the CPU4 report; no new production build was required here. No Steam/GitHub upload was performed.

## Actual 1.6 GUI retry

`native16-cpu4-selection60-v16-visible-rimhud` uses the same CPU4 and v16 helper, but displays the owned native test window and loads the installed RimHUD with a private copy of its actual configuration. Unlike the earlier attempts, each six-second phase records 360 Root updates, 722 managed GUI calls and 361 Repaints. Window visibility and optional-mod configuration both differ; this is not a single-variable demonstration of why the earlier attempts recorded no GUI. The strict wrapper and bounded sequence both pass, with no native errors and unchanged normal configuration.

| Phase | Mean Root update | Mean managed GUI call | Frame interval rate |
| --- | ---: | ---: | ---: |
| Selected | 0.95 ms | 1.24 ms | 60.0/s |
| Deselected | 1.06 ms | 0.70 ms | 60.0/s |
| Reselected | 0.96 ms | 1.23 ms | 60.0/s |

The GUI entry is `RimWorld.UIRoot_Play.UIRootOnGUI`, excluding the outer native Root scale/fader wrapper; it is not directly interchangeable with 1.7's Root.OnGUI stopwatch. This is a paused synthetic cleared-arena GUI workload with a native generated gene/gear-rich actor and 60 rifle NPCs. It does not establish real 1.6 combat-AI/TPS performance or reproduce the reporter's save/mod profile.

The first 1.7 health-tab/tooltip attempt (`public7-thrumbo20-realhits-v17-health-hover`) opens the actual Health tab and invokes the actual RimHUD health-tooltip getter 192 times during its first selected phase. It is nevertheless **failed**, because the test-only details capture did not handle `CurTabs=null` after deselection. The v18 helper fixes only that null enumeration; the failed attempt is retained separately and is not counted as a passing sequence.

## Actual Health tab and tooltip getter

The public beta.7 v18 retry (`public7-thrumbo20-realhits-v18-health-hover`) passes the bounded sequence, with the actual native Health tab open during both selected phases and closed during deselection. The actual RimHUD `HealthTooltip.Get` runs 178/0/140 times. Only each relevant GUI event's mouse position is temporarily set inside the real tooltip rectangle and restored in a finalizer; the physical cursor and clicks are unchanged. This validates the getter workload, not OS hover or visible tooltip rendering.

| Phase | Frame interval rate | Actual native tick rate | Mean OnGUI call | Actor damage events |
| --- | ---: | ---: | ---: | ---: |
| Selected | 44.2/s | 56.6/s | 2.29 ms | 23 |
| Deselected | 57.7/s | 59.6/s | 1.64 ms | 4 |
| Reselected | 34.6/s | 53.4/s | 2.55 ms | 31 |

The selected phases are slower in this case. They also contain most damage/injury/armor work, so the difference cannot be attributed solely to selection, the tab, or RimHUD. The native tick rates are distinct from display frame intervals; this result is not evidence of a massive tick-AI slowdown. It preserves a concrete UI-plus-combat workload for the next comparison instead of dismissing the report.

The identical-helper CPU4 comparison (`cpu4-thrumbo20-realhits-v18-health-hover`) also passes, with the Health tab applied, 182/0/140 real tooltip-getter calls, all actors standing and no new native errors. Its selected/deselected/reselected frame rates are **45.4/55.5/35.0**, native tick rates **56.9/59.3/52.9**, and mean OnGUI calls **2.37/1.70/2.67 ms**. Damage events are 28/5/35, differing from the beta.7 run. The existing cache fix therefore does not remove this measured frame-rate difference, and these results do not establish resolution of the reporter's symptom.

At the end of this initial follow-up, all 74 production C# files remained byte-identical to the preserved pre-follow-up source, and installed/repository CPU4 DLL hashes matched on both versions. The subsequent source investigation and candidate correction are recorded separately in [Selection-UI-deep-20261117.md](Selection-UI-deep-20261117.md); the earlier measurements here do not validate that newer candidate.

## Larger real-hit workload: incomplete sequence

`cpu4-thrumbo60-realhits-v18-health-hover` retains the same native damage and UI workload with 60 rifle hostiles. The first selected phase records 73 damage events, about 31 frame intervals/s and 48.5 native ticks/s; the deselected phase records 2 damage events, 40.2 frames/s and 55.4 ticks/s. The form is standing at both boundaries. In the last selected phase the active form dies after about 1.84 seconds, so the required survival assertion fails and the sequence is **not a passing comparison**. All 60 enemies remain alive, no new native errors are recorded, and the original profile/save remain unchanged.

This case illustrates the selection/damage confound: rifle volleys arrive predominantly during the selected phases, and the last phase includes a casualty. Neither the incomplete frame-rate difference nor total health invalidation counts establish a selected-only root cause. A reporter save/mod list and matching HUD configuration are needed to reproduce their immediate selection-toggle behavior without conflating it with different combat work.
