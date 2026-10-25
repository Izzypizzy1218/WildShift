# Native gameplay review regression (TEST ONLY)

This fixture runs production WildShift methods inside an initialized RimWorld 1.7 game, with native maps, pawns, training, death, caravans and save loading. The runner uses headless mode; these results do not establish visual correctness, optional-mod compatibility or 1.6 runtime coverage.

## Required profile

Always specify `-CoreOnly`. The active list must be Harmony, Core, WildShift and `wildshift.tests.review17`. Odyssey gives Wargs special training that can hide the training-decay regression; the fixture rejects that condition.

## Red/green contract

| Defect ID | Required behavior in the fixed build |
| --- | --- |
| `draft-return` | A drafted human transforms into a drafted animal and returns as the same drafted human. |
| `training-wildness` | A transformed Warg retains Tameness and player ownership; an ordinary Warg still loses both through native decay. |
| `mixed-caravan-loss` | A mixed caravan survives native death of its last ordinary human while its transformed colonist remains alive. Survival and mobility checks do not change ordinary animal trade ownership. |

For a baseline run, `-ExpectFailure` requires all three exact defect IDs. A crash, missing result, unexpected error or different failure is insufficient. Wrapper `Passed: true` together with `ExpectedFailure: true` means the baseline reproduced the expected defects, not that its gameplay passed.

Training decay is accelerated by calling native `Debug_MakeDegradeHappenSoon()` and `TrainingTrackerTickRare()` once per Tameness step. No native decay method is replaced, and months of simulated time are unnecessary. After recording a baseline training failure, the fixture restores only its test pawn's faction and Tameness so the independent caravan case can proceed.

A green run saves the real game, exits, then starts a second process to load that save. It verifies exact human identity, one surviving animal form, caravan survival over additional native ticks, off-map reversion, native world registration and the original 17-silver inventory.

## Running

From the repository root, build the helper into an external evidence directory:

```powershell
pwsh -NoProfile -File Tests/Run-Review17Tests.ps1 -BuildOnly -Output C:/evidence/review-build
```

Stage the generated helper separately in a game-discoverable mod folder, close RimWorld, then run:

```powershell
pwsh -NoProfile -File Tests/Run-Review17Tests.ps1 -SkipBuild -CoreOnly -FixtureDir C:/test-mods/WildShiftReviewValidation -WildShiftDir C:/test-mods/WildShift -Output C:/evidence/review-fixed
```

Adjust paths to actual discoverable folders. Select baseline DLLs externally for red runs and restore them in `finally`. The runner neither installs mods nor swaps production DLLs. Each run requires a fresh output directory outside the repository, uses its own `-savedatafolder`, verifies normal user configuration hashes, and checks actual loaded DLL hashes and mod IDs. It refuses an existing game and can terminate only its own launched process. Exclude this helper from production packages and Workshop uploads.

## Recorded verification

On RimWorld **1.7.4985 rev1269**, `engine-review-before-core/result.json` reproduced all three baseline defects. `engine-review-after-core/result.json` passed **25 checks** across the initial save phase and fresh-process reload. Both reported no runner errors and unchanged normal user configuration. These evidence folders are under `outputs/wildshift-1.3.0-beta.5` outside the source repository.
