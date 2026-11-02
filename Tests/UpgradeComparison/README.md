# Serialized-source upgrade comparison

`Compare-UpgradeSave.ps1` is a pure read-only comparator for a native source RWS and `UpgradeProbe`'s **phase-0-loaded.xml**, before its optional form cycle. It starts no game and writes only one fresh external JSON report.

```powershell
./Tests/UpgradeComparison/Compare-UpgradeSave.ps1 `
  -SavePath C:/evidence/source-copy.rws `
  -SnapshotPath C:/evidence/profile/UpgradeResults/phase-0-loaded.xml `
  -Output C:/evidence/source-comparison.json
```

All source pawns bearing human/form WildShift Hediffs, plus their stored originals/cached forms, must be present. Missing corpse/dead rows fail rather than silently shrinking coverage. Checks cover IDs/species/kind/faction/dead/downed, original/cache ownership, every old health ID/definition/severity/age/part index, possessions in their original slots, original raw skill values/passions/XP, gene IDs/definitions/explicit resource values, and native ability IDs/definitions/relative cooldowns/explicit charge values.

Known native float32 fields are parsed as `System.Single` before applying absolute `1e-5` tolerance: Mono may serialize `8999.99707` while .NET's round-trip formatter emits `8999.997` for the **same float bits**. Integer ticks, indices, counts and raw levels are not rounded to Single. `observedLevel` is deliberately not substituted for raw skill level. Active-animal skills compare with the preserved original's source skill records. Linked canonical ability aliases and new disposable animal projections are identified as intentional mappings.

`Passed` requires zero mismatches **and** zero unsupported comparisons. `ComparedFieldsPassed` separately describes the fields that could be compared. Missing severity/hit-point/resource/charge defaults are not inferred from updated mod definitions. Health objects found on a different linked owner require review and are not labeled a disappearance pass. Additional unknown native entries are reported. Source/snapshot hashes are checked again before finishing; existing outputs and writes inside the repository are refused.

This is not arbitrary serialized-component equality: item comps/quality/style, private Hediff payloads, gene execution, relations/memories/needs and ability fields absent from the probe remain outside scope. Native part indices are compared, not the equivalence of every mod's anatomy definition. Do not compare a post-cycle snapshot as if ordinary animal wounds/gear were required to remain in their original places after an intentionally resetting form cycle.

`Test-UpgradeComparison.ps1 -Output C:/fresh-external-test-dir` creates tiny synthetic XML inputs and proves that old health/item loss, missing dead rows, raw skill/resource changes and one-tick cooldown changes are detected, while documented aliases/canonical skills/float formatting pass. It also checks that omitted ambiguous defaults are explicit unsupported comparisons. These tests exercise only the comparison tool, not gameplay.
