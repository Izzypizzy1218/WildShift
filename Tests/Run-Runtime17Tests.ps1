param(
    [string]$RimWorldDir = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$WildShiftPath = '',
    [string]$DotNet = '',
    [string]$Output = ''
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$workspace = Split-Path $root -Parent
if (!$WildShiftPath) { $WildShiftPath = Join-Path $root '1.7/Assemblies/WildShift.dll' }
if (!$DotNet) { $DotNet = Join-Path $workspace '.tools/dotnet10/dotnet.exe' }
if (!$Output) { $Output = Join-Path $workspace '.tmp/wildshift-17-tests' }
foreach ($required in @($DotNet, $WildShiftPath, $HarmonyPath, (Join-Path $RimWorldDir 'Assembly-CSharp.dll'))) {
    if (!(Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing required input: $required" }
}
$Output = [IO.Path]::GetFullPath($Output)
$null = New-Item -ItemType Directory -Force -Path $Output
$build = Join-Path $Output 'build'
$obj = Join-Path $Output 'obj/'
& $DotNet build (Join-Path $PSScriptRoot 'Runtime17/WildShift.Runtime17Tests.csproj') -c Release -o $build "-p:BaseIntermediateOutputPath=$obj" "-p:RimWorldDir=$RimWorldDir" "-p:HarmonyPath=$HarmonyPath" "-p:WildShiftPath=$WildShiftPath" --nologo
if ($LASTEXITCODE) { throw "Runtime regression build failed: $LASTEXITCODE" }
# Run a console host against the actual game assemblies. It never starts the
# game or loads/writes the user's configuration, maps, saves or Workshop data.
& $DotNet (Join-Path $build 'WildShift.Runtime17Tests.dll') $RimWorldDir (Split-Path $HarmonyPath -Parent) $root $Output 2>&1 | Tee-Object -FilePath (Join-Path $Output 'runtime-results.txt')
if ($LASTEXITCODE) { throw "Runtime regression checks failed: $LASTEXITCODE" }
