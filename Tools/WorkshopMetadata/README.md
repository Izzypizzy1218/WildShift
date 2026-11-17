# WildShift Workshop metadata CLI

This release tool targets only RimWorld app `294100`, WildShift item `3796025407`, and its signed-in owner. It is excluded from the runtime package. It updates title/description metadata only; it does not upload content, tags or previews.

The previous description-only `koreana` update left its localized title empty. Every update now requires an explicit language, title and full description, then calls `SetItemUpdateLanguage`, `SetItemTitle`, and `SetItemDescription` in that order. Blank/NUL input and Steam UTF-8 buffer overflow are rejected before Steam initialization. No language defaults are inferred from the Steam UI.

## Build and configuration

Requires a .NET 10 SDK and the installed RimWorld Steamworks.NET assembly. The project defaults to `$(MSBuildProgramFiles32)\Steam\steamapps\common\RimWorld`; override `-p:RimWorldDir=...` or `-p:SteamworksPath=...` when building. Copy only this tool's source and its dependency into an external release work directory before building, so generated files stay out of the repository.

```powershell
dotnet build WorkshopMetadata.csproj -c Release -o ../metadata `
  -p:SteamworksPath=../metadata-deps/com.rlabrecque.steamworks.net.dll
```

Run `WorkshopMetadata.dll` using a .NET 10 `dotnet` executable, not an apphost relying on the system runtime. Runtime `RIMWORLD_DIR` optionally overrides the installed directory containing `steam_api64.dll`; its default is the usual Program Files (x86) Steam installation. Steam must be running and signed in to the item's owner. Queries validate the app, item and owner and reject cached responses.

## Inspect before uploading

```powershell
dotnet metadata/WorkshopMetadata.dll inspect english steam-before-english.json
dotnet metadata/WorkshopMetadata.dll inspect koreana steam-before-koreana.json
```

Inspect is read-only remotely. Each JSON preserves the complete API description, title, language, cache status, update time and normalized-description SHA256. A blank title is recorded as `TitlePresent=false`; an optional expected title turns a mismatch into exit code 2 while still preserving the JSON. Keep both before snapshots and preserve the underlying description when replacing the current-update block. Steam permits fewer than 129 UTF-8 bytes for a title and fewer than 8000 for a description, including space for the terminating NUL.

## Explicitly authorized metadata update

An update submits remote changes. Run it only after the release and exact title/description files are authorized. Both languages use `WildShift` for this release. `-` omits a change note, allowing a separate content uploader to publish the bilingual changelog once.

```powershell
dotnet metadata/WorkshopMetadata.dll update english WildShift `
  steam-description-english.txt - steam-update-english.json
dotnet metadata/WorkshopMetadata.dll update koreana WildShift `
  steam-description-koreana.txt - steam-update-koreana.json
```

After a successful submission, the CLI makes new uncached queries and requires an exact title and full description match (normalizing CRLF/LF only). It retains a result even if submission succeeded but verification failed: in that case the remote may already have changed, and the result must be checked before retrying.

## Independent final readback

```powershell
dotnet metadata/WorkshopMetadata.dll inspect english steam-final-english.json WildShift
dotnet metadata/WorkshopMetadata.dll inspect koreana steam-final-koreana.json WildShift
```

Require exit code 0, `Cached=false`, `TitlePresent=true` and `TitleMatchesExpected=true` in both files, and match the full description against the prepared language file. JSON outputs use create-new semantics; choose a distinct filename for retries instead of replacing earlier evidence.

Steamworks API reference: [SetItemUpdateLanguage, SetItemTitle, SetItemDescription and SetLanguage](https://partner.steamgames.com/doc/api/ISteamUGC#SetItemUpdateLanguage).
