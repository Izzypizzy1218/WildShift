# RimWorld 1.7 support — v1.3.0-beta.1

This page records the original beta.1 port. For the current beta.2 custom-form feature and rebuilt 1.6/1.7 binaries, see [optional race and xenotype rules](CustomRacialForms.md).

Build target: RimWorld **1.7.4977 rev1248**, Steam's `unstable` branch, checked on 2026-10-15.

The unchanged release binary was revalidated against **1.7.4982 rev1280** on 2026-10-19. Current startup and native-assembly checks pass; the original intermittent CLR/JIT crash remains unproven as fixed. See the [startup investigation and reproducible checks](Startup-1.7-2026-10-19.md).

## Version selection

The root contains shared Defs, Languages, Patches and Textures. `LoadFolders.xml` adds only the selected version's directory:

| Game | Mod assembly | Runtime |
| --- | --- | --- |
| 1.6 | `1.6/Assemblies/WildShift.dll` | Original v1.2.24, .NET Framework 4.7.2 |
| 1.7 | `1.7/Assemblies/WildShift.dll` | .NET 10 / game's Waylith runtime |

The preserved 1.6 DLL has SHA-256 `76076D9C3F1C32C179D263FA611AB6AC8DC899E70F6F2BB4473D98A016C3C5B7`.
Do not leave an old `Assemblies/WildShift.dll` at the root when manually updating. Replace the old mod directory with the packaged directory; do not merge an old installation into it. Workshop updates use the existing WildShift item, 3796025407.

No new save fields or gameplay rules are introduced by this port. Definitions, package ID, class names and saved original-human ownership remain the same. Prosthetic persistence is still outside the implemented scope.

## Build

Install the .NET 10 SDK and the matching game/Harmony versions:

```powershell
dotnet build WildShift.csproj -c Release
```

Non-default paths can be supplied with `-p:RimWorldDir=...`, `-p:RimWorldManaged=...` and `-p:HarmonyAssemblies=...`. For 1.7 the game references are at the game's root, including `Assembly-CSharp.dll` and `UnityStubs.dll`. The game and Harmony DLLs are not distributed in WildShift.

Rebuilding 1.6 requires real 1.6 game references plus `-p:RimWorldVersion=1.6`; the 1.7 API is not a substitute. The shipped 1.6 binary was preserved without rebuilding.

## Validation

The Release build uses the installed 1.7 game and Harmony binaries. Existing production-logic tests cover 385 assertions: 89 form-selection, 17 awakening-race, 243 fault-injection/stability, 18 special-training and 18 revenge-budget checks. These use engine stubs and do not substitute for runtime checks.

`Tests/Run-Runtime17Tests.ps1` builds and runs separate checks against the real 1.7 assemblies. Its output states the exact checked scope; isolated fixtures do not establish full graphical gameplay or third-party mod compatibility.

The release candidate passed **143 native-assembly assertions**, including registration of 40 Harmony patch classes on 37 methods, native player-control/drag checks, and native Scribe round-trip of the stored body's minimal identity subgraph. The Release build completed with zero warnings and errors.

Separate actual-game boot checks used `-Headless -NoCrashDialog -NoFileLog -QuitAfterBoot` and dedicated `-savedatafolder` profiles. Core plus Harmony/WildShift exited with code 0 and no error log entries. All five DLC plus Harmony/WildShift also exited successfully in two fresh-profile reruns. The normal user's configuration hashes remained unchanged.

An earlier all-DLC boot **did crash** with exit code `0xC0000005` and `Internal CLR error (0x80131506)` in the MonoMod/CoreCLR JIT hook on a Waylith worker thread. Harmony-only and a separate patched-mod comparison boot succeeded, as did both subsequent WildShift reruns. The cause is unresolved; those later passes do not establish that the crash is fixed or unrelated to WildShift. This observation is one reason the release remains a beta. Headless texture warnings and unrelated installed-mod metadata warnings were also recorded; no visual rendering claim is made.

```powershell
pwsh -NoProfile -File Tests/Run-Runtime17Tests.ps1 -DotNet 'C:\path\to\dotnet.exe'
```

`Tests/Verify-GameApi.ps1` is the historical Windows PowerShell / .NET Framework metadata check for 1.6. Use the new runtime runner for 1.7.

## Optional mods

The existing optional hooks remain. Installed VEF's 1.7 fixed-gender API retains the expected method and field. Locally installed Releashed and HAR still advertise 1.6; their older compatibility results cannot be presented as 1.7 validation. Use a compatible 1.7 release of each optional mod. Animal Apparel rendering, HAR races, VGE and Big & Small integrations need their own full game testing with matching dependencies.

This is a beta for an unstable game build. Full visual validation, prolonged colony play and migration of an entire 1.6 colony into 1.7 remain outside the automated checks.
