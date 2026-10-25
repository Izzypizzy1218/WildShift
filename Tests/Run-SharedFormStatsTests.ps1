param(
    [string]$RimWorldDir = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$WildShiftPath = '',
    [string]$DotNet = '',
    [string]$Output = '',
    [switch]$ExpectFailure,
    [switch]$Production
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$workspace = Split-Path $root -Parent
if (!$WildShiftPath) { $WildShiftPath = Join-Path $root '1.7/Assemblies/WildShift.dll' }
if (!$DotNet) { $DotNet = Join-Path $workspace '.tools/dotnet10/dotnet.exe' }
if (!$Output) { $Output = Join-Path $workspace ('.tmp/wildshift-shared-stats-' + [guid]::NewGuid().ToString('N')) }
$Output = [IO.Path]::GetFullPath($Output)
if ($Output.StartsWith([IO.Path]::GetFullPath($root) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside the source repository.' }
if (Test-Path -LiteralPath $Output) { throw 'Use a fresh isolated output directory.' }
$null = New-Item -ItemType Directory -Path $Output
$build = Join-Path $Output 'build'
$obj = (Join-Path $Output 'obj') + [IO.Path]::DirectorySeparatorChar
& $DotNet build (Join-Path $PSScriptRoot 'SharedFormStats/WildShift.SharedFormStatsTests.csproj') -c Release -o $build "-p:BaseIntermediateOutputPath=$obj" "-p:RimWorldManaged=$RimWorldDir" "-p:HarmonyPath=$HarmonyPath" "-p:WildShiftPath=$WildShiftPath" "-p:UseProductionSharedStats=$($Production.IsPresent.ToString().ToLowerInvariant())" --nologo
if ($LASTEXITCODE) { throw "Shared-stat fixture build failed: $LASTEXITCODE" }
$mode = if ($ExpectFailure) { 'baseline' } else { 'fixed' }
& $DotNet (Join-Path $build 'WildShift.SharedFormStatsTests.dll') $RimWorldDir (Split-Path $HarmonyPath -Parent) $Output $mode 2>&1 | Tee-Object -FilePath (Join-Path $Output 'results.txt')
if ($LASTEXITCODE) { throw "Native shared-stat checks failed: $LASTEXITCODE" }
