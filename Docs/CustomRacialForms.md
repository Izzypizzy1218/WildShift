# Optional race and xenotype form rules — v1.3.0-beta.2

WildShift's ordinary assignment rules remain the default. Players may enable **Custom animal forms by race** in the mod settings and open the rule editor. The editor is available only through mod settings; there is no new form-selection command on individual pawns.

## Using the editor

1. Enable the custom-rule option in WildShift's mod settings.
2. Add an installed humanlike race, or an installed xenotype when Biotech is active.
3. Search for and select one or more allowed animal forms, then apply the selection.
4. Set the rule's probability from 0% to 100%.

For example, selecting **Ratkin → Thrumbo → 100%** assigns a thrumbo to newly assigned Ratkin shapeshifters. It does not make every Ratkin a shapeshifter. Selecting several animals gives each currently available selected kind an equal chance after the custom probability roll succeeds.

A failed custom roll uses the original racial-preference and ordinary-pool logic. Thus the displayed probability is the chance to use the custom selection branch, not necessarily the animal's final overall frequency: that animal may also occur in the original pool.

An exact xenotype rule takes priority over a body-race rule. If its roll fails or its selected animals are unavailable, the original assignment rules apply; the custom body-race rule is not rolled a second time. Existing preset racial preferences are unchanged when customization is disabled or no custom rule matches.

## Scope and persistence

- Rules apply when a new form is assigned. They do not reroll existing shapeshifters, including after rules are edited, removed or disabled.
- Custom forms from the supported small racial animals retain a saved reference identifying the exact custom assignment. This permits, for example, a custom cat form to survive removal of its original rule without unlocking arbitrary other forms.
- Normal insect restrictions and unsafe-form exclusions still apply. Enable insectoid forms before configuring insect choices. As with existing forms, subsequently removing an animal's mod or disabling a required safety option can invalidate that form.
- The selector offers the ordinary eligible pool and the already supported small racial forms. It excludes mechanoids, humanlike bodies, anomaly entities, drones and unsafe genetic hybrids. It does not install additional animal packs.
- Awakening a latent animal keeps that animal's original species. Big and Small - Sapient Animals also retains its source-species assignment. These existing identity-preserving paths take precedence over random custom rules.
- With customization enabled, newly generated revenge companions follow the same rule-aware assignment path; their actual form's combat power is charged to the raid budget. Existing sold leaders retain their assigned forms.
- Settings store exact definition names as strings, preserving rules for temporarily disabled race or animal mods. Missing or currently blocked animals are skipped during assignment; an empty usable list falls back to the original rules. Missing selections remain visible and can be cleared or replaced.
- The custom reset button confirms, clears custom rules and turns customization off. The existing reset-all-settings button restores all WildShift defaults.

Rules are examined only during assignment. Definition lists are collected when the editor opens and search results update when the search text changes. No new pawn tick or map-wide scan is added.

## Validation

Both 1.6 and 1.7 binaries build with zero warnings and errors. The 1.6 build uses the cached, permission-published RimRef 1.6.4871 reference assemblies and matching installed Harmony; it is not a full 1.6 gameplay run. The 1.7 build uses the installed 1.7.4982 game and Harmony binaries.

The reviewed beta.3 suite includes 73 custom-rule checks, 89 original form checks, 17 awakening checks, 243 stability checks, 18 special-training checks, 18 revenge-budget checks and 23 cache-lifetime checks: **481 engine-stub assertions pass**. The native 1.7 suite passes **163 assertions**, including actual settings Scribe round-trips, absent-mod names, stored custom-form provenance, legacy settings with no new keys and mental-state control restrictions. Engine-stub tests do not substitute for native saving or graphical testing. See the [review findings](Review-v1.3.0-beta.3.md).

The installed beta.2 completes an isolated actual-game startup with Core, all five DLC and Harmony: exit 0, no logged errors or fatal stderr, and the native post-load completion marker. Separate real-renderer captures verify the default-off settings page, rule editor and searchable animal selector in English, plus the settings page and rule editor in Korean. A temporary diagnostic mod opens the windows automatically and is removed after validation; it is excluded from the package. The graphical checks validate display and initialization, not prolonged colony play or all optional mods.

The prior 1.7 CLR/JIT incident remains a separate unresolved historical issue. This feature does not claim to fix it; see [the startup investigation](Startup-1.7-2026-10-19.md).
