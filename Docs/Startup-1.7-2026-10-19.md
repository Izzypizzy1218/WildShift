# RimWorld 1.7 startup crash investigation — 2026-10-19

This records the beta.1 investigation before the beta.2 feature update. Current custom-rule validation is documented [separately](CustomRacialForms.md); the historical crash is not claimed fixed by that feature.

**Status: the historical crash is not reproduced on the current installation. Its exact cause and a corrective gameplay change are not established.** The existing `v1.3.0-beta.1` WildShift DLL passes the current checks. This is a validation update, not a crash-fix release.

## Observed failure

The earlier all-DLC startup exited with `0xC0000005` (`-1073741819`). Its standard error contained `Fatal error` / `Internal CLR error (0x80131506)`. The recorded stack entered MonoMod's `V60.InvokeCompileMethod` and `Core60Runtime.JitHookDelegateHolder.CompileMethodHook` while a Waylith worker initialized `ThreadLocal` storage through `ConcurrentBag.TryTake`, `Lock`, and `SpinWaitInternal`.

The matching Windows crash dump was recovered: `RimWorld.exe.16492.dmp`, matching the original test PID and 2026-10-15 00:26 startup time. Windows event 1000 and the dump exception stream both identify an access violation at `coreclr.dll+0x1DBAE0`; the dump records an attempted read at address `0x59` on thread 2468. Dump SHA-256: `70415BB4D090B0A419732AEB098B846440DE414F518E89DEBC0DB217C39C9EB8`. The exception-stream context points into `KERNELBASE!RaiseFailFastException`; a separate original context recovered from the thread stack has the fault RIP and `RCX=0x51`. The captured instruction `mov rax,[rcx+8]` explains the invalid read at `0x59`. This confirms an actual invalid memory access before fail-fast. It still does not establish how that invalid value originated; the exact old native image and symbols are missing.

This locates the failure in native runtime/JIT-hook processing. It does not establish which component corrupted runtime state or exclude an indirect WildShift trigger. There is no managed WildShift exception in that stack. Catching an exception around `WildShiftMod` construction would not recover the recorded process-level fatal error.

The historical helper reported zero stdout error entries even for this failed process. Exit status and stderr must be checked independently; `Errors=0` alone is not a successful boot.

## Environment comparison

| Component | Original failed boot | Current validation |
| --- | --- | --- |
| RimWorld | 1.7.4977 rev1248 | 1.7.4982 rev1280 |
| Game's custom CLR | 10.0.10-waylithclr.dev | 10.0.13-waylithclr.dev |
| Harmony mod wrapper | 3.0.0.0 | 3.0.1.0 |
| Harmony library | 3.0.0.0-rc.2 | 3.0.0.0-rc.3 |
| WildShift 1.7 DLL | v1.3.0-beta.1 | Same binary |

WildShift SHA-256: `D2C0A3728092515DA80D4E9886F602415DED5B382C312AE4A23DA51A6DBF523C`.

Current Harmony SHA-256: `C8EF7856B2BE3CF24B345C9B5C2F197F68FCF92649635064FCE7B3CCC8F77CB0`.

Current coreclr SHA-256: `9986655EE9485EB8668FB27224A31797CE90E7CC201DC863C91D8F6D70488A74`.

## Dependency audit

The official old Harmony package was recovered into scratch and its published ZIP checksum verified. Its rc.2 JIT-hook implementation was compared with installed rc.3. Resolved IL for `Core60Runtime`, including the nested hook, is identical after removing RVA comments. The shared normalized IL SHA-256 is `A73BE7461E77DA3D729F8EDA75958193C678A46ECD0C6FFADC72604312B1F120`.

The `Core60Runtime` frame is expected on .NET 10: the selected `Core100Runtime` inherits that implementation. It does not indicate incorrect runtime-version detection.

[Harmony rc.3's release notes](https://github.com/pardeike/Harmony/releases/tag/v3.0.0-rc.3) describe patch-generation performance and validation changes. They do not establish a fix for this failure, and the relevant hook did not change.

[MonoMod issue 283](https://github.com/MonoMod/MonoMod/issues/283) reports similar top stack frames under a .NET 10 debugger. Its [upstream change](https://github.com/MonoMod/MonoMod/commit/f16d7850f6706c77e077555c38e9dd1e39df2d33) replaces a thread-local JIT wrapper with a stack-local wrapper. Neither inspected Harmony binary contains that change. The original game boot had no established debugger involvement, and the exact crash was not reproduced, so that upstream issue cannot be treated as the proven cause or its patch as a verified WildShift remedy.

The game corelib exposes `WaylithClr.MethodBodyUpdater`, but Harmony still uses its MonoMod detour backend. Replacing that backend would require a separate framework integration and compatibility validation. No shared Harmony/runtime files or global JIT settings were modified.

## Revalidation

- Three fresh-profile actual-game boots with Core, all five DLC, Harmony and WildShift: exit 0, zero stdout errors and zero fatal stderr entries.
- One equivalent Harmony-only baseline: exit 0, zero stdout errors and zero fatal stderr entries.
- Existing native-assembly suite: **143 assertions passed**, including all 40 patch classes / 37 patched methods. This suite uses the SDK .NET host with game assemblies; it is distinct from actual-game startup.
- A separate stress host ran on the actual installed **Waylith CLR**, with eight workers exercising dynamic JIT compilation, thread-local initialization, locks and concurrent bags:

| Stress condition | Duration | Completed dynamic method iterations | Exit |
| --- | --- | ---: | ---: |
| No Harmony | 5 s | 334,107 | 0 |
| rc.2, workers concurrent with hook installation | 12 s | 857,099 | 0 |
| rc.3, workers concurrent with hook installation | 12 s | 853,291 | 0 |
| rc.2, cold workers started after hook installation | 12 s | 766,507 | 0 |
| rc.3, cold workers started after hook installation | 12 s | 743,369 | 0 |

These bounded synthetic checks did not reproduce the fatal error. They do not recreate the old CLR or all original startup scheduling conditions. The old game runtime is unavailable locally, preventing an exact before/after runtime comparison.

The normal user configuration was hash-checked and preserved. Headless texture warnings and unrelated installed-mod metadata warnings are retained in logs; these runs do not validate graphics, an existing colony, prolonged play, or every optional mod.

## Repeatable startup validation

`Tests/Run-Startup17Tests.ps1` launches the installed game with a new isolated settings directory and redirected stdout/stderr. It records environment hashes and treats nonzero exit, timeout, logged errors, fatal stderr, or missing boot evidence as failures. It does not install a candidate automatically; the installed WildShift binary must match the repository candidate.

The runner adds `-MemoryReportAfterBoot` and requires its native post-load log marker. Six process fixtures verified successful startup, fatal stderr with zero stdout errors, nonzero exit, logged errors, incomplete startup and timeout cleanup. The original failed log is also rejected. These fixture checks mock process launch and do not count as actual game boots. A final real launch attempt was refused when the normal game started during preflight; that process was left running. The three actual WildShift boots above used the earlier isolated helper, so the final runner's complete real-launch path remains to be exercised while the game is closed.

```powershell
pwsh -NoProfile -File Tests/Run-Startup17Tests.ps1
pwsh -NoProfile -File Tests/Run-Startup17Tests.ps1 -WithoutWildShift
```

Local evidence is under the workspace's `outputs/wildshift-startup-2026-10-19`: native regression output, isolated boot logs/results, environment hashes and the stress report/reproducer. The original failure remains under `outputs/wildshift-1.3.0-beta.1/boot-all-dlc`.

The next causal test requires an actual recurring failure or the original custom runtime. Current passes justify reporting that this installation starts successfully; they do not justify relabeling the historical failure as fixed.
