param(
    [string]$Output = '',
    [string]$RimWorldDir = 'C:/Program Files (x86)/Steam/steamapps/common/RimWorld',
    [string]$WildShiftPath = '',
    [string]$WildShift16Path = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskWorkspace = Split-Path $taskRoot -Parent
if (!$Output) { $Output = Join-Path $taskWorkspace '.tmp/wildshift-shared-health-tests' }
if (!$WildShiftPath) { $WildShiftPath = Join-Path $taskRoot '1.7/Assemblies/WildShift.dll' }
if (!$WildShift16Path) { $WildShift16Path = Join-Path $taskRoot '1.6/Assemblies/WildShift.dll' }
$taskDotNet = Join-Path $taskWorkspace '.tools/dotnet10/dotnet.exe'
$taskHarmony = 'C:/Program Files (x86)/Steam/steamapps/workshop/content/294100/2009463077/1.7/Assemblies'
$null = New-Item -ItemType Directory -Force -Path $Output
& $taskDotNet build (Join-Path $PSScriptRoot 'SharedHealthNative/SharedHealthNative.csproj') -c Release -o (Join-Path $Output 'build') "-p:BaseIntermediateOutputPath=$Output/obj/" "-p:WildShiftPath=$WildShiftPath" --nologo
if ($LASTEXITCODE) { throw 'Shared-health native compile failed.' }
& $taskDotNet (Join-Path $Output 'build/SharedHealthNative.dll') $RimWorldDir $taskHarmony $Output 2>&1 | Tee-Object -FilePath (Join-Path $Output 'native-results.txt')
if ($LASTEXITCODE) { throw 'Shared-health native checks failed.' }
Write-Output 'Compile-only check: 1.6 production API consumer against matching 1.6 game references.'
& $taskDotNet build (Join-Path $PSScriptRoot 'SharedHealthNative/Compile16.csproj') -c Release -o (Join-Path $Output 'compile16') "-p:BaseIntermediateOutputPath=$Output/obj16/" "-p:WildShiftPath=$WildShift16Path" --nologo
if ($LASTEXITCODE) { throw 'Shared-health 1.6 compile failed.' }
