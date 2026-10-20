# Changelog

## v1.3.0-beta.3 — Review fixes (2026-10-20)

- Apply enabled custom race/xenotype form rules to newly generated revenge companions and charge their actual form's combat power to the raid budget. Preserve the old companion assignment when customization is disabled.
- Respect native spawned and mental-state restrictions when classifying transformed animals as player-controlled, preventing inappropriate command menus during mental breaks.
- Stop two static caches from retaining retired Game instances after returning to the menu. Preserve cache expiry, explicit invalidation and per-game scenario recovery behavior.
- Pass 644 logic/native assertions and both version-specific builds. See `Docs/Review-v1.3.0-beta.3.md` for findings, regression coverage and validation limits.

## v1.3.0-beta.2 — Optional custom race forms (2026-10-20)

- Add an optional mod-settings editor for choosing one or more animal forms and a 0–100% custom assignment probability per humanlike race or xenotype. The feature defaults off and the existing assignment rules remain the default.
- Apply rules to new form assignments only. Preserve existing forms after customization is disabled or a rule changes; keep awakening and Sapient Animals source-species behavior.
- Provide searchable race/xenotype and animal lists, equal selection among chosen usable forms, xenotype priority, rule removal and a confirmed custom-settings reset. Retain unavailable mod names in saved settings and fall back safely when no configured choice can be used.
- Include English and Korean UI text, both 1.6 and 1.7 builds, new production-rule checks and native settings/legacy-save round-trip checks. See `Docs/CustomRacialForms.md`.
- Add reproducible 1.7 startup validation that checks exit codes, fatal stderr and completed initialization. Document analysis of the historical CLR/JIT crash; its exact cause remains unproven and this release does not claim to fix it.

## v1.3.0-beta.1 — RimWorld 1.7 (2026-10-15)

- Add a dedicated .NET 10 assembly for RimWorld 1.7.4977 rev1248 (unstable), built against the actual 1.7 game, UnityStubs and Harmony assemblies.
- Keep the v1.2.24 RimWorld 1.6 assembly unchanged under `1.6/Assemblies`. `LoadFolders.xml` selects exactly one version-specific assembly alongside the shared definitions, translations and textures.
- Preserve the current transformation, medical/purchased awakening, revenge, grouped controls, native H melee command and optional integration code. This release does not change gameplay settings or add prosthetic persistence.
- Update the build project and add 1.7 runtime regression checks. External mods need their own compatible 1.7 releases; earlier 1.6 integration results do not establish 1.7 compatibility.
- Pass 385 production-logic assertions and 143 native-assembly assertions. Core startup and two all-DLC startup reruns complete successfully. One earlier all-DLC run hit an unresolved CLR/JIT crash; retain the beta label and document that failure rather than counting it as a pass.
- Publish the previously local v1.2.x source history as part of this GitHub release. See `Docs/RimWorld-1.7.md` for verification and limitations.

## v1.2.24

- Remove WildShift's extra melee-attack gizmo from transformed animals. Keep RimWorld's existing grouped melee command and its native `H` hotkey (Misc2), including mixed selection with human colonists.
- Preserve unrelated apparel commands, including NewRatkin's shield-facing skill, even when they use the same fist icon. No save migration or new Harmony patch is needed.

## v1.2.23

- Remove Draftable Animals - Releashed's duplicate draft toggle from player-owned transformed bodies. Keep WildShift's native-grouped draft button, so attack-trained forms still draft/undraft together with human colonists. Ordinary animals retain Releashed's commands; unrelated gizmos remain available.
- Order the compatibility filter after Releashed's iterator postfix, independent of mod load order. No per-tick work, new required dependency or saved state is added.
- Validate the original Draftable Animals download separately: its 1.5 binary fails to initialize on RimWorld 1.6 even without WildShift. The joint compatibility check uses Releashed's actual 1.6 Workshop binary. See `Docs/DraftableAnimals-v1.2.23.md` for evidence and limits.

## v1.2.22

- Restore native right-click orders for drafted animal forms. Clicking friendly objects no longer bypasses interaction menus to force a melee attack. Native movement, hostile attack, mixed-selection and queued-order handling remain available; the explicit attack command remains available for deliberate targets.
- Handle transformed animals without an equipment tracker as unarmed in native attack menus, avoiding the native ranged-attack lookup's null-reference error. Equipped forms keep the native weapon decision.
- Include player-owned transformed animals in the native map drag-box colonist selection priority. Preserve the game's portrait selection, Shift-add behavior, ordering and selection limit without changing animal race classification or ordinary livestock selection.
- See `Docs/Controls-v1.2.22.md` for implementation and verification scope. Implant/prosthetic persistence is not part of this release.

## v1.2.21

- Mix additional shapeshifters into sold-shapeshifter revenge. They are newly generated people with separate preserved bodies, assigned animal forms and native assault/return behavior. The sold leader remains the actual original pawn.
- Spend roughly 30% of the companion combat-power budget on these allies, preferring forms different from the leader's when affordable. Reserve the leader and at least one ordinary animal, and deduct each ally's actual assigned-form combat power before filling the remaining animal force. The whole army still uses one arrival-time baseline ×2 budget.
- Update English/Korean revenge letters to report both ordinary animals and additional shapeshifters. Existing 120–150 day scheduling, 50% default chance, migration, buyback/cancellation and save protection remain in effect.
- Fix an Odyssey selection error for non-player transformed animals with special attack training. Suppress their player-master training commands; player-owned forms and ordinary animals retain native commands.
- Add mixed-budget and original-body checks, and an isolated visible game demonstration. See `Docs/MixedRevenge-v1.2.21.md` for scope and evidence.

## v1.2.20

- Delay sold-shapeshifter revenge to 120–150 days after sale, replacing the previous 15–45 day interval.
- Replace the 2–5 companion limit with an arrival-time combat-power budget of twice the target home map's native baseline threat points. Include the original leader in the budget, use the assigned species' actual combat power and round to whole animals, with at least one companion. No fixed upper pack-count cap is applied.
- Migrate pending v1.2.19 events by moving their original scheduled dates and expiry dates 105 days later, once only. Recover the original schedule from the fixed expiry date so retry timestamps do not distort it. Existing completed raids are unaffected.
- Keep the 50% default sale chance, buyback/recovery cancellation, world-pawn protection and existing arrival-delay rules. See `Docs/SoldRevenge-v1.2.20.md` for current behavior and test scope.

## v1.2.19

- Add revenge by sold shapeshifters. An eligible sale has a configurable 50% default chance to schedule the actual original pawn's return 15–45 days later, accompanied by 2–5 ordinary animals of its assigned species.
- Retain the original identity, race, skills and assigned form across sale, world travel and save/load. Use the existing enemy transformation and assault behavior, including return to the original human body. Preserve old recruitment quest tags without letting them block the revenge transformation.
- Wait until the trader has actually released the pawn to the world. Delay arrivals during peaceful difficulty or high map danger, and separate revenge arrivals by at least three days. The trader's faction is not made hostile by the revenge event.
- Cancel pending revenge after buyback, recovery into player ownership/captivity, death or expiry. Setting the revenge chance to 0% also cancels pending events. Roll only once per original pawn while the feature is enabled; repeated sales cannot create repeated revenge events.
- Save pending records and protect their live pawns from world garbage collection while needed. Process the pending list once per game hour, with no full pawn scan. See `Docs/SoldRevenge-v1.2.19.md` for rules and verification.

## v1.2.18

- Add optional Vanilla Genetics Expanded integration. Awakening and repeat transformations retain hybrid quality, genetic-disease timers and the four native genetic diseases. Save the state with the original shapeshifter; never reroll quality on transformation.
- Exclude VGE genetic failures, expiring/reset-dependent forms and mechanical hybrids from normal animal-form selection and latent-animal generation. Other VGE animals keep the existing size/predator and insect settings.
- Recognize Big and Small sapient animals by the framework's species mapping. Newly assigned sapient-animal shapeshifters use their own animal species, including small animals, and return to their original sapient pawn.
- Integrate Cogni-Fi with latent and transformed pawns: preserve the stored original pawn, return command, draft status and belongings. A latent animal made sapient retains that new identity and gains its species-bound shapeshifter form; it is not replaced by another random human.
- When both mods are loaded, carry VGE quality and disease state through Cogni-Fi conversion and register the corresponding native components on generated sapient races for save/load support.
- See `Docs/Compatibility-v1.2.18.md` for behavior, test coverage and limits.

## v1.2.17

- Start transformed animals with their species-supported special training and valid prerequisites completed. Reapply this when loading existing transformed pawns, and keep special-capable forms from losing training through native decay.
- Stop uncommanded hive-queen egg/sludge spewing by completing its native special training. Keep manual abilities and their normal requirements; ordinary animals still require training.
- Suppress discovery letters from transformed animal bodies, including repeated hive-queen discoveries. Preserve native reveal state, codex discovery, quest signals and ordinary wild-animal discovery letters.
- Validate legacy-save migration, repeat transformations, forced decay, normal-animal controls and all 22 eligible vanilla species with special training through the actual game engine. See the special-training validation document for test scope and limitations.

## v1.2.16

- Recover stale animal-apparel render trees on transformed pawns when apparel is added or removed but another patch suppresses the normal apparel-change notification. Compare actual worn items with render-node ownership and invalidate only a resolved, inconsistent tree.
- Check transformed apparel after save loading has completed. Keep the native Animal Apparel renderer, artwork selection and invisible-item rules; add no tick scan or draw-time Harmony patch.
- Verify the stale-notification condition through real graphical map rendering: injected notification suppression hides plate armor on transformed animals in v1.2.15 while ordinary animals remain visible; the repair restores all five tested species. This is a defensive compatibility repair, not proof that the unidentified original report has the same cause.

## v1.2.15

- Add the optional Animal Apparel render root to VEF's terrain-dependent animal tree when the Framework tag exists and no apparel root is already present. This covers Alpha Animals' Chameleon Yak and Sand Prowler; it does not supply their missing armor textures.
- Add optional load-after entries for Animal Apparel: Framework, Basic Armor and Universal Basic Armor. Keep apparel graphics and item handling under their native implementations, with no tick or draw-time Harmony patch.
- Document live RimWorld 1.6 checks across 337 animal races, 179 eligible WildShift forms and six armor definitions. Before this change, all 310 tested combinations with species artwork generated draws in every facing, map/portrait context and applicable drafted state. The reported disappearance of supported plate armor was not reproduced.

## v1.2.14

- If a map cannot accept dropped animal-form apparel, equipment or cargo, transfer the remaining items to the preserved human's inventory and continue return. Already dropped items remain on the ground. Keep the existing off-map inventory behavior.
- Preserve rollback only when both destinations reject the remaining items or the human body itself cannot be placed. Cover partial drops, drop exceptions, inventory rejection, placement retries and lethal return with blocked ground.
- Add optional load-after entries for Animal Gear and Animal Equipment, and document Animal Equipment apparel preservation through native apparel trackers. Neither becomes a required dependency and no third-party mod is bundled.
- Clarify that Animal Equipment's universal/placeholder armor may intentionally lack visible artwork. The unresolved visual report is not represented as fixed.
- Pass 246 stability, 89 logic and 17 race-selection assertions (352 total), production compilation and native API/XML validation.

## v1.2.13

- Preserve animal-form apparel, equipment, inventory and carried items before reverting to the original human. On-map returns drop belongings nearby, including locked apparel; off-map returns transfer them to the human's inventory without equipping animal clothing.
- Cancel return when unloading fails, preserve partially dropped/transferred items, and retain the original human's own clothing. Restore detached items when a native container-transfer callback throws, and avoid stack merging during off-map transfers.
- Refuse emergency destruction of a transformed shell holding possessions. If lethal return requires emergency human recovery and unloading still fails, allow native death to retain the remaining items with the animal corpse instead of leaving a living empty shell.
- Update English/Korean return-command text and failure feedback. Animal Gear/Giddy-up rendering is under investigation, not claimed fixed: the reported versions are unavailable locally and no unsupported rendering override is installed.

## v1.2.12

- Give new latent awakenings a 50% player-faction origin branch and a 50% alternative-race branch. Apply the same selection to normal taming, medical awakening and the manual command.
- Deduplicate alternative body races by race definition, include permitted vanilla/active-mod human xenotypes with Biotech, and exclude the player's basic body race (or default human xenotype). Preserve normal faction genetics on the player branch; fall back to that branch if no alternatives exist.
- Generate alternative races without a player faction, then join them to the colony. This prevents HAR's player-Colonist replacement and native faction genes from overriding the selection. Preserve the animal if another hook unexpectedly replaces the selected body race.
- Keep existing awakened pawns, original animal forms, damage rules and belongings safeguards unchanged. Scan candidates only during awakening, with no per-tick work or global definition edits.
- Pass 89 existing logic, 17 race-selection and 220 stability assertions (326 total), production compilation and native API/XML verification. A 20,000-draw modeled distribution yields 9,979 player-race selections; live gameplay with all active mods remains separate validation.

## v1.2.11

- Resolve the enemy map component through weak map registrations instead of scanning the native component list on every map tick. Preserve the existing pre-Lord safe point and empty-queue behavior.
- Contain errors from external raid-loss callbacks per returned pawn, so other queued returns can continue. Guard the map prefix against escaping exceptions that would abort native MapPostTick, and deduplicate these error logs.
- Consume VEF's pending fixed-gender rewrite for a transformed animal on its first mapped tick. Keep the original callback running and skip transformation lookups after that rewrite. Validate the optional writable bool field before patching and isolate optional installation errors.
- Skip harm-component lookups for nonanimal victims and avoid adding a gizmo iterator wrapper to ordinary pawn selections.
- Validate 89 logic + 218 stability assertions (307 total), actual Harmony private-field injection on a modeled VEF callback, installed VEF metadata, production build and 31 native Harmony targets/XML references. Operation-count tests are not live TPS/FPS measurements.

## v1.2.10

- Keep a transformed caravan/container member in its original holder when human insertion fails. Do not turn a failed holder swap into a successful loose-world return; retry can replace the same member after the fault clears.
- Persist completed latent awakenings so failed animal cleanup, repeated clicks, save/load and resurrection of an obsolete animal cannot generate another human. A remaining animal offers **Finish awakening**, which only unloads remaining possessions and retries cleanup.
- Retain failed candidate cleanup across saves before allowing another generation attempt. Detached candidates are saved deeply; map/world/container-owned candidates use references. Successfully destroyed rollback candidates are removed before saving.
- Share one reentrancy guard across normal taming, medical revelation and manual awakening. Keep damage/purchase rules and 3% animal probabilities unchanged.
- Add failure and serialization-branch regressions, including checks that restoring either reviewed defect makes the new suite fail. Real-game compilation and Harmony/XML checks passed; live Scribe and optional-mod gameplay remain separate validation.

## v1.2.9

- Raise the shared ordinary latent-animal default from 1% to 3%, applying to eligible wild and trader animals. Update initialization, saved-setting fallback, reset and generation fallback consistently. Trader stock does not get a second roll.
- Set the user's current animal chance to 3% and turn off the 100% trader test override. Preserve all other current settings, including the user's spillover damage choice.
- Clarify English/Korean settings labels to show that the chance covers both wild and trader animals. Existing animals and stock retain their markers.

## v1.2.8

- Add a saved trader-only 100% latent-shapeshifter test toggle, default off. The local test configuration enables it; wild-animal chance remains 1%.
- Wrap native animal-stock generation lazily, marking every eligible new stock animal without duplicating markers or changing stock order/count. Existing stock and ineligible species remain unchanged. Disabling the option restores the ordinary generation behavior for future stock.
- Include English/Korean settings labels. Validation: 89 logic + 173 stability assertions (262 total), real-game compilation, 31 Harmony targets and XML checks. Live stock generation still requires testing in game.

## v1.2.7

- Remove damage-history and manual-unloading restrictions from purchased-animal awakening. Track rejected medical recruitment separately from harm; only that medical-origin rejection blocks the button. Buying the animal clears the rejection without erasing harm history. Legacy owned animals are not presumed medically recruited from old harm alone.
- Automatically place inventory, carried items, equipment and apparel nearby before completing awakening. Failed or partial placement preserves the original animal and remaining items, removes the generated human and allows retry. The same preservation protects normal/medical latent revelation.
- Keep normal taming and the player-harm medical restriction. No purchase-time pawn replacement or new tick polling. Persist the medical-origin rejection alongside the existing harm set.
- Update English/Korean tooltips and documentation. Validation: 89 logic + 164 stability assertions (253), production compilation and 30 Harmony targets. Live gameplay/save-load verification remains separate.

## v1.2.6

- Add an **Awaken as human** command for eligible player-owned latent animals, including existing purchases. Use it after map arrival; trading, caravan contents and delivery containers are not replaced mid-transfer.
- Preserve the animal species as the human's assigned form. Reuse safe human placement and retain the original animal if placement fails. Guard reentrant or repeated commands.
- Disable manual awakening for recorded player harm to preserve the medical anti-abuse rule, and for off-map/downed/mental-state animals. Require unloading cargo, carried items, equipment and apparel to prevent item loss.
- Update English/Korean button text, disabled reasons and the latent-marker tooltip. No probabilities or normal-taming conditions change.
- Validate 89 logic + 156 stability assertions (245 total), production compilation and 29 Harmony target checks. Live UI/gameplay validation remains required.

## v1.2.5

- Replace v1.2.4's blanket medical-awakening exclusion with player-harm attribution. A newly medically bonded/recruited latent wild animal can awaken if it has no recorded player-inflicted damage.
- Record actual damage from player-owned instigators, including pawns, animals and facilities. Persist marked-animal IDs through saving, death and resurrection; zero-damage hits do not count. Normal taming is unaffected by harm history and does not require a bond.
- Reveal only after the entire native treatment finishes, preserving native treatment/bond/recruitment behavior. Recheck harm history before deferred revelation; existing owned animals are not automatically converted.
- Keep legacy latent hediff types intact for save compatibility. Old saves begin with empty harm history; earlier attackers cannot be reconstructed. No tick polling or recurring pawn scans.
- Update English/Korean descriptions. Validate with 89 logic + 131 stability assertions, production compilation and 28 Harmony target checks. Engine-stub serialization tests do not replace an in-game Scribe round-trip.

## v1.2.4

- Formalize normal taming as the only supported awakening route for latent wild shapeshifters. Medical-bond automatic taming intentionally does not awaken them; a bonded relationship is not required for normal taming.
- Replace the ambiguous bond wording in the English latent-marker description and add its Korean description. Any colonist meeting native taming requirements can awaken an eligible marked animal.
- Document the intentional interaction-level hook in source and the route distinction in README. Runtime behavior, probabilities, compiled DLL and existing pawns are unchanged; this is a description/documentation release.

## v1.2.3

- Review all 33 C# source files and runtime definitions. Preserve all existing probabilities and gameplay defaults.
- Fix tamed shapeshifter revelation destroying the source animal before human placement. Commit human placement first; generation/assignment/placement failures retain the animal and clean up the unsuccessful candidate.
- Fix reversion inside world-registered holders (including caravans) failing to register the human as a world pawn. Mirror the source's registration before committing; failed registration rolls back the owner and stored human. This prevents native caravan cleanup from removing an unregistered replacement.
- Reject direct melee orders for downed/nonplayer attackers and null, destroyed, despawned, self or different-map targets. This is input validation, not a confirmed fix for the previously reported path-node exhaustion.
- Skip transformed-hediff lookup on ordinary factionless/player/dead animal checks in the enemy hot path. No new recurring pawn scan or speculative cache was introduced.
- Expand tests to compile actual tame-reveal and direct-order methods rather than mock those workflows. 89 logic + 111 stability assertions pass, along with a real-game build, all 26 Harmony target checks, 10 injected-field checks, 11 XML files and duplicate Def/Keyed checks. See Docs/Review-v1.2.3.md for remaining risks and live test limitations.

## v1.2.2

- Increase the random shapeshifter chance for newly generated starting candidates in ordinary scenarios from 1% to 5%.
- Keep the Lone Beastkin scenario's guaranteed starter, existing candidates/pawns, colony-join chance, latent animal chance, racial form preferences and death settings unchanged. The user's local enemy-raider setting remains 10%; release enemy default remains 5%.
- Rebuild against installed RimWorld/Harmony and retain the enemy duty restoration fixes from v1.2.1.

## v1.2.1

- Fix enemy downed-return copying a null duty: native MakeDowned clears MindState.duty before WildShift swaps the human back. After membership transfer, ask the current LordToil to reassign missing orders, preserving current assault/retreat behavior and raid counters.
- Gate the enemy main/constant duty subtrees on the pawn's actual duty, not only the Lord's AssignsDuties flag, so the brief pending-return state cannot invoke a duty node without orders.
- Repair missing duties on saved v1.2.0 hostile shapeshifters during map initialization. Do not reset their spent transformation flag or reroll forms. No recurring map-pawn scan is added.
- Correct the engine stub regression to clear duty on downing. It failed against v1.2.0, then passed after the fix. Add current-toil, pending-state, save-repair, faction and cleanup-failure checks.
- Validation: 89 logic plus 88 stability assertions and production compilation against installed RimWorld/Harmony passed. Actual in-game behavior still requires verification. User's local 100% enemy test setting is unchanged; release default stays 5%.

## v1.2.0

- Add enemy shapeshifters to eligible ordinary humanlike raids, with a configurable 5% assignment chance (0–100%). Starting, colony-join and racial affinity probabilities are unchanged.
- Transform when the raider's Lord enters native AssaultColony duty. Dedicated main/constant think trees follow native attack, approach, retreat and exit duties. Native melee animation and attack timing are unchanged.
- Nonlethal downing of an enemy combat form queues a return before the map's Lord tick. Temporarily bypass random enemy death-on-downing; genuine lethal damage and slaughter retain existing death handling. Player/allied forms do not auto-return when downed.
- Transfer Lord membership and duty in place, preserving raid counters and preventing an animal-to-human swap from counting as the last raider's defeat. The stored human keeps its original weapons, apparel and health state.
- Serialize the one-use-per-raid flag and pending return marker; reconstruct transient queues once on load. Failed generation is not retried repeatedly; failed return preserves the stored human and releases temporary raid-loss suppression.
- Exclude quest-tagged actors and forced-death-on-downed forms from automatic combat transformation. Siege/sapper/breacher roles wait for normal assault duty. Bespoke third-party raid systems remain unverified.
- Validation: 89 logic and 77 stability assertions with engine stubs, real-game C# compilation, and metadata/XML verification. Live gameplay, rendering and actual save/load remain to be tested.

## v1.1.4

- Reviewed the 1.6 definition sets of Vanilla Animals Expanded, Megafauna, Alpha Animals, and Dinosauria against the existing automatic animal-form eligibility logic. Added a reproducible source-level inspector and candidate list; no blanket whitelist or new mandatory dependency.
- Added optional load-order hints for the four packs and Vanilla Expanded Framework.
- Prevent VEF's fixed-gender component from overwriting the original pawn's gender on WildShift forms. This addresses Alpha Animals' blizzarisk, dunealisk, and feralisk clutch-mother forms. Ordinary animals are unchanged; the hook is absent when VEF is absent.
- Keep all pool restrictions, racial preference chances, starting/join chances, and existing assignments. Enemy transformation AI, reproduction, prosthetics, and armor behavior are unchanged.
- Passed 89 logic and 39 stability assertions with engine stubs and compiled against installed RimWorld/Harmony. Reviewed 260 source PawnKind records (plus VAE's no-Odyssey layout); this is not a live gameplay or full special-ability compatibility certification.

## v1.1.3

- Changed the Lone Beastkin preview behavior by request: moving a scenario-designated candidate out of the first starting slot and back now assigns a fresh random animal instead of restoring its previous form.
- Reroll both the normal pool and the racial-preference branch; do not reuse the scenario's fixed fallback animal. Random selection may produce the same animal again.
- Redraws alone do not reroll, and preexisting shapeshifters not created by the scenario preview remain unchanged. In-game assignments and all v1.1.2 transfer/attack/cache safeguards are unchanged.
- Passed 89 logic assertions and 31 stability assertions with engine stubs, plus compilation against installed RimWorld/Harmony assemblies. Live character-creation validation remains required.

## v1.1.2

- Only scan maps/caravans for transformed colonist-bar entries when vanilla rebuilds its dirty cache, rather than on every Entries access.
- Remove the redundant attack-menu suppression patch. Successful animal-only direct attacks still consume the click; mixed selections and failed direct orders retain vanilla's full selection context and fallback options.
- Preserve scenario-preview form assignments when candidates move out of and back into the designated starting slot. Do not remove preexisting non-scenario shapeshifter markers; use weak references for discarded candidates.
- Prepare the destination body before removing the source. Commit reversion only after human placement succeeds, roll back failed map/container transfers, and refuse to discard an occupied animal form.
- Guard against reentrant reversion/death handling. When lethal-damage reversion cannot place the human, attempt emergency world-pawn retention before removing the animal; slaughter still kills the recovered human. If recovery also fails, preserve the occupied form instead of deleting its stored human.
- Passed 85 assignment/gender/preview assertions and 31 stability assertions using engine stubs, plus compilation against installed RimWorld/Harmony assemblies. Actual in-game callbacks, combat, and save/load still require testing.
- No new tick polling. Animal prosthetics, equipment preservation, reproduction, and generic traversal of the hidden human holder are unchanged.

## v1.1.1

- Reduced the racial animal-form preference from 50% to 20%.
- Split the Ratkin race/xenotype preference into 10% rat and 10% Hamstrox (`Ratkin_KingHamster`); missing target slots fall back to the normal pool rather than increasing another preferred animal's share.
- Generate a fresh animal form with the original pawn's gender specified before generation. Genderless species remain genderless. Existing transformed bodies use the new behavior after reverting and transforming again.
- Taming reveals preserve the animal's gender in the new human body; genderless origins allow normal human gender generation.
- Preserve already assigned forms and the existing starting/join probabilities. Prosthetic, armor, and reproduction behavior is unchanged.
- Passed 81 isolated logic assertions and compilation against installed RimWorld/Harmony assemblies. Live in-game validation remains required.

## v1.1

- Added a 50% racial animal-form preference for Ratkin, Kiiro, Nyaron, Kurin HAR, Miho, Revia, Rabbie, Yuran, Bori, and Sheepawn shapeshifters.
- Recognize supported Biotech xenotypes, including arctic and desert Miho variants, without a dependency on race-mod assemblies.
- Allow matching racial animals to bypass ordinary animal-pool restrictions during assignment and transformation. The general pool and wild-animal discovery rules are unchanged.
- Preserve existing valid assigned forms and explicit taming forms; avoid duplicate assignment rolls during hediff initialization and repeated scenario-preview redraws.
- Keep starting-candidate (1%) and normal-join (5%) shapeshifter chances unchanged. No additional tick polling.
- Added isolated selection, assignment, persistence-reference, and transformation-guard regression tests. Full in-game testing with each race mod remains necessary.

## v1.0

- Official 1.0 release of WildShift.
- Includes the complete reversible shapeshifting system, drafted transformed-animal control, natural shapeshifter discovery, stability safeguards, and the final performance pass.

## v0.9.4

- Reduced repeated health checks by filtering ordinary human pawns before transformed-animal checks in the game-over, colonist-bar, and drafted-think-tree patches.

## v0.9.3

- Prevented latent-animal generation from querying the player faction before it exists during world generation.

## v0.9.2

- Marked the command-texture cache for main-thread startup, removing RimWorld's missing `StaticConstructorOnStartup` warning.

## v0.9.1

- Removed WildShift's separate 60-tick shapeshifter-join check and its dedicated join incident.
- Added a 5% chance for each normal human colonist join to be a shapeshifter, covering storyteller wanderers, recruitment, and permanent quest joiners.

## v0.9.0

- First Steam-ready release of WildShift.
- Includes playable shapeshifters, reversible animal forms, drafted animal control, direct melee orders, starting-scenario support, random starting candidates, English lore, and Korean UI translations.

## v2026.09.02.8

- Used the finalized shapeshifter origin story in the Lone Beastkin scenario-selection description.

## v2026.09.02.7

- Replaced the Lone Beastkin opening narration with the finalized shapeshifter origin story.

## v2026.09.02.6

- Added a 1% chance for each humanlike starting-pawn candidate in every other scenario to be generated as a shapeshifter.
- Kept the random candidate's shapeshifter condition visible in character creation, so it can be selected before starting.

## v2026.09.02.5

- Marked the designated Lone Beastkin starting pawn as a shapeshifter in the character-creation screen before the game starts.
- Kept the preview marker aligned with the first selected starting-pawn slot when candidates are reordered or rerolled.

## v2026.09.02.4

- Made slaughtering a transformed animal kill the stored human with no survival roll.
- Kept the stored human's faction synchronized when a transformed animal is released to the wild.
- Made drafted transformed animals use the standard drafted-pawn order flow instead of animal wandering AI.
- Renamed the visible transformed health condition to `shapeshifter`.

## v2026.09.02.3

- Standardized all descriptive text, tooltips, and scenario narrative in English, including when the game language is Korean.
- Kept Korean translations for names, command labels, settings, and gameplay messages.

## v2026.09.02.2

- Expanded shapeshifter lore with their uncertain origins, preserved human bodies, and the rumored many-skinned shapeshifters.
- Added separate lore descriptions for active beast-forms and latent wild shapeshifters.
- Added complete Korean labels and descriptions for all WildShift health conditions.

## v2026.09.02.1

- Kept transformed animals visible and selectable in the colonist bar on maps and in caravans.
- Added the vanilla colonist draft hotkey (`R`) to transformed animals.
- Made transformation available before drafting and placed its command immediately after the draft command.
- Preserved colonist-bar display order across transformation and reversion.

## v2026.09.01.2

- Added the new reversible shapeshifting artwork to both the transform and return-to-human command buttons.

## v2026.09.01.1

- Fixed the one-frame southward snap between melee attack stances for drafted transformed animals.
- Restored RimWorld's native animal melee lunge so transformed attacks have the same visible impact motion as ordinary animals.
- Kept transformed animals facing their active melee target during the drafted-idle frame without altering native attack cooldowns or verb selection.

## v2026.08.30.7

- Fixed permanent death so the stored human dies instead of remaining trapped inside a dead animal form.
- Added safe reversion and game-over protection for caravans and travelling transporters.
- Preserved ordinary colonist attack options during mixed human/transformed selections.
- Limited generated-health cleanup to injuries, missing parts, and harmful generated health conditions.
- Made the Lone Beastkin scenario part drive startup assignment instead of comparing the scenario's Korean name.
- Changed the join setting to a real MTB check and removed the hidden 20-day minimum refire delay.
- Restricted latent shapeshifter reveals to successful tame/recruit interactions instead of every player faction change.
- Completed Korean keyed translations and moved the base scenario text to English with Korean DefInjected translations.

## v2026.08.30.6

- Cached eligible animal kinds instead of repeatedly scanning all pawn kind definitions.
- Removed recurring whole-map scans after Lone Beastkin startup initialization.
- Removed repeated legacy ability cleanup and per-tick melee-facing correction.
- Initialized transformed-animal control at creation/load time instead of every 250 ticks.
- Cached the transformed-colonist game-over safeguard and invalidated it on transform, revert, and death.
- Removed temporary LINQ/list allocations from direct right-click attacks.
- Reduced the Lone Beastkin recovery UI check to once per game.

## v2026.08.30.5

- Removed the southward positional jump from transformed-animal melee attacks.
- Made drafted transformed animals attack immediately when an attackable target is right-clicked.
- Added melee order feedback and player-forced melee jobs.

## v2026.08.30.1–v2026.08.30.4

- Added generated animal health cleanup for new and existing saves.
- Added transformed-animal melee facing correction and iterated on attack rendering.
- Added safe drafted controls and right-click melee ordering.
