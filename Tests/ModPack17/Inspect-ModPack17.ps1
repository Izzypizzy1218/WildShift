param(
    [string]$Output = 'C:/Users/집/Desktop/codex/outputs/wildshift-1.4.0-beta.2/modpack-ui-preparation',
    [string]$Game17 = 'C:/Users/집/Desktop/codex/outputs/wildshift-1.4.0-beta.2/engine17-backup',
    [string]$Config = 'C:/Users/집/AppData/LocalLow/Ludeon Studios/RimWorld by Ludeon Studios/Config/ModsConfig.xml'
)
$ErrorActionPreference = 'Stop'
$null = New-Item -ItemType Directory -Force -Path (Join-Path $Output 'user-75/Config')
$snapshot = Join-Path $Output 'user-75/Config/ModsConfig.xml'
if (!(Test-Path -LiteralPath $snapshot)) { Copy-Item -LiteralPath $Config -Destination $snapshot }
[xml]$profile = Get-Content -LiteralPath $snapshot
$active = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($id in $profile.ModsConfigData.activeMods.li) { $null = $active.Add($id) }
$roots = @('C:/Program Files (x86)/Steam/steamapps/workshop/content/294100',
    'C:/Program Files (x86)/Steam/steamapps/common/RimWorld/Mods', (Join-Path $Game17 'Data'))
$mods = @(foreach ($root in $roots) {
    foreach ($directory in Get-ChildItem -LiteralPath $root -Directory) {
        $about = Join-Path $directory.FullName 'About/About.xml'
        if (!(Test-Path -LiteralPath $about)) { continue }
        try { [xml]$meta = Get-Content -LiteralPath $about } catch { continue }
        if (!$meta.ModMetaData.packageId) { continue }
        [pscustomobject]@{ Package = [string]$meta.ModMetaData.packageId; Name = [string]$meta.ModMetaData.name;
            Path = $directory.FullName; Meta = $meta.ModMetaData }
    }
})
function Should-Load($node) {
    foreach ($id in ([string]$node.IfModActive).Split(',', [StringSplitOptions]::RemoveEmptyEntries)) {
        if (!$active.Contains($id.Trim())) { return $false }
    }
    foreach ($id in ([string]$node.IfModNotActive).Split(',', [StringSplitOptions]::RemoveEmptyEntries)) {
        if ($active.Contains($id.Trim())) { return $false }
    }
    return $true
}
function Selected-Folders($mod) {
    $xmlPath = Join-Path $mod.Path 'LoadFolders.xml'
    if (Test-Path -LiteralPath $xmlPath) {
        [xml]$xml = Get-Content -LiteralPath $xmlPath
        $choices = @($xml.loadFolders.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })
        $choice = $choices | Where-Object Name -eq 'v1.7' | Select-Object -First 1
        if (!$choice) {
            $choice = $choices | Where-Object { $_.Name -match '^v\d+\.\d+$' -and [version]$_.Name.Substring(1) -le [version]'1.7' } |
                Sort-Object { [version]$_.Name.Substring(1) } -Descending | Select-Object -First 1
        }
        if (!$choice) { $choice = $choices | Where-Object Name -eq 'default' | Select-Object -First 1 }
        if ($choice) {
            foreach ($node in $choice.SelectNodes('li')) {
                if (Should-Load $node) { Join-Path $mod.Path $node.InnerText.Trim().Trim('/', '\') }
            }
            return
        }
    }
    $versionFolder = Get-ChildItem -LiteralPath $mod.Path -Directory | Where-Object {
        $_.Name -match '^\d+\.\d+$' -and [version]$_.Name -le [version]'1.7'
    } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if ($versionFolder) { $versionFolder.FullName }
    if (Test-Path -LiteralPath (Join-Path $mod.Path 'Common')) { Join-Path $mod.Path 'Common' }
    $mod.Path
}
function Assembly-Info([string]$path) {
    $stream = [IO.File]::OpenRead($path)
    $pe = $null
    try {
        $pe = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        if (!$pe.HasMetadata) { return $null }
        $reader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        $definition = $reader.GetAssemblyDefinition()
        $refs = @(foreach ($handle in $reader.AssemblyReferences) {
            $reference = $reader.GetAssemblyReference($handle)
            [pscustomobject]@{ Name = $reader.GetString($reference.Name); Version = $reference.Version.ToString() }
        })
        [pscustomobject]@{ Name = $reader.GetString($definition.Name); Version = $definition.Version.ToString();
            Path = $path; SHA256 = (Get-FileHash -LiteralPath $path).Hash; References = $refs }
    }
    finally { if ($pe) { $pe.Dispose() }; $stream.Dispose() }
}
$inventory = @(foreach ($id in $profile.ModsConfigData.activeMods.li) {
    $matching = @($mods | Where-Object Package -eq $id)
    foreach ($mod in $matching) {
        $folders = @(Selected-Folders $mod)
        $dlls = @(foreach ($folder in $folders) {
            $assemblyFolder = Join-Path $folder 'Assemblies'
            if (Test-Path -LiteralPath $assemblyFolder) {
                foreach ($dll in Get-ChildItem -LiteralPath $assemblyFolder -Recurse -Filter '*.dll' -File) {
                    try { Assembly-Info $dll.FullName } catch { }
                }
            }
        })
        [pscustomobject]@{ Package = $id; Name = $mod.Name; Path = $mod.Path;
            DuplicatePackagePaths = @($matching.Path); Declares17 = (@($mod.Meta.supportedVersions.li) -contains '1.7');
            LoadFolders17 = $folders; DeclaredDependencies = @($mod.Meta.modDependencies.li.packageId);
            MissingActiveDependencies = @($mod.Meta.modDependencies.li.packageId | Where-Object { $_ -and !$active.Contains($_) });
            Assemblies = $dlls }
    }
})
$available = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($dll in Get-ChildItem -LiteralPath $Game17 -Filter '*.dll' -File) { $null = $available.Add($dll.BaseName) }
foreach ($assembly in $inventory.Assemblies) { if ($assembly) { $null = $available.Add($assembly.Name) } }
$unresolved = @(foreach ($mod in $inventory) {
    foreach ($assembly in $mod.Assemblies) {
        foreach ($reference in $assembly.References) {
            if (!$available.Contains($reference.Name) -and $reference.Name -notlike 'UnityEngine*') {
                [pscustomobject]@{ Package = $mod.Package; Assembly = $assembly.Name; RequiredName = $reference.Name;
                    RequiredVersion = $reference.Version; Path = $assembly.Path }
            }
        }
    }
})
$report = [ordered]@{
    Verification = 'Static PE metadata and load-folder prediction only. No mod DLL execution, game launch, installation, live Config mutation, download, or Steam branch change.'
    Snapshot = $snapshot; SnapshotSHA256 = (Get-FileHash -LiteralPath $snapshot).Hash
    ActiveCount = $active.Count; TargetVersion = (Get-Content -LiteralPath (Join-Path $Game17 'Version.txt') -Raw).Trim()
    RuntimeRoot = $Game17; Inventory = $inventory; UnresolvedNonUnityReferences = $unresolved
    Caveats = @('Missing references are static candidates, not confirmed TypeLoad/runtime errors.',
        'UnityEngine references are classified separately because the 1.7 runtime supplies compatibility aliases.',
        'Duplicate package paths are retained instead of guessing which native ModLister instance wins.',
        'Load-folder IfModActive/IfModNotActive uses comma-separated package predicates; runtime inventory must be captured from the actual game.',
        'Do not infer missing gameplay support solely from a missing 1.7 declaration.')
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Output 'static-modpack17-audit.json') -Encoding utf8
[pscustomobject]@{ ActiveCount = $active.Count; InstalledEntries = $inventory.Count;
    MetadataWithout17Declaration = @($inventory | Where-Object { !$_.Declares17 }).Count;
    StaticUnresolvedReferences = $unresolved.Count; Output = $Output }
