<#
.SYNOPSIS
Runs the isolated native 1.7 render-scope regression without starting RimWorld.
.DESCRIPTION
Requires a fresh output directory outside the repository and an explicit expected
production DLL hash. Build files and results stay in that directory. The console
patches only the native render-tree method and checks nested/default/exception
scope behavior. It performs no timing workload, installation or publication.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Output,
    [Parameter(Mandatory=$true)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedWildShiftSHA256,
    [string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$WildShiftPath='',
    [string]$DotNet=''
)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\','/')
$workspace=Split-Path $repoRoot -Parent
if (!$DotNet) { $DotNet=Join-Path $workspace '.tools\dotnet10\dotnet.exe' }
if (!$WildShiftPath) { $WildShiftPath=Join-Path $repoRoot '1.7\Assemblies\WildShift.dll' }
$Output=[IO.Path]::GetFullPath($Output).TrimEnd('\','/')
$GameDir=[IO.Path]::GetFullPath($GameDir).TrimEnd('\','/')
$WildShiftPath=[IO.Path]::GetFullPath($WildShiftPath)
$HarmonyPath=[IO.Path]::GetFullPath($HarmonyPath)
$DotNet=[IO.Path]::GetFullPath($DotNet)
if ($Output.Equals($repoRoot,[StringComparison]::OrdinalIgnoreCase) -or $Output.StartsWith($repoRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output must be outside the source repository.'
}
if (Test-Path -LiteralPath $Output) { throw 'Refusing an existing output directory.' }
$nativePath=Join-Path $GameDir 'Assembly-CSharp.dll'
$stubsPath=Join-Path $GameDir 'UnityStubs.dll'
foreach ($path in @($DotNet,$nativePath,$stubsPath,$HarmonyPath,$WildShiftPath)) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Input file missing: $path" }
}
$expected=$ExpectedWildShiftSHA256.ToUpperInvariant()
$before=(Get-FileHash -LiteralPath $WildShiftPath -Algorithm SHA256).Hash
if ($before -ne $expected) { throw 'Production DLL does not match ExpectedWildShiftSHA256.' }
$nativeBefore=(Get-FileHash -LiteralPath $nativePath -Algorithm SHA256).Hash
$harmonyBefore=(Get-FileHash -LiteralPath $HarmonyPath -Algorithm SHA256).Hash
$null=New-Item -ItemType Directory -Path $Output
$project=Join-Path $PSScriptRoot 'RenderScope17\WildShift.RenderScope17Tests.csproj'
$bin=Join-Path $Output 'bin'
$obj=Join-Path $Output 'obj'
$run=Join-Path $Output 'run'
$harmonyDir=Split-Path $HarmonyPath -Parent
$buildArguments=@(
    'build',$project,'-c','Release','--nologo','-v:q',
    ('-p:RimWorldDir='+$GameDir),('-p:HarmonyDir='+$harmonyDir),
    ('-p:OutputPath='+($bin.Replace('\','/')+'/')),
    ('-p:BaseIntermediateOutputPath='+($obj.Replace('\','/')+'/')),
    ('-p:MSBuildProjectExtensionsPath='+($obj.Replace('\','/')+'/'))
)
& $DotNet @buildArguments *> (Join-Path $Output 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Regression build failed; see $Output\build.log" }
$executable=Join-Path $bin 'WildShift.RenderScope17Tests.dll'
# These are the console's four arguments; production is dynamically loaded.
& $DotNet $executable $GameDir $harmonyDir $WildShiftPath $run *> (Join-Path $Output 'run.log')
$runExit=$LASTEXITCODE
$after=(Get-FileHash -LiteralPath $WildShiftPath -Algorithm SHA256).Hash
$nativeAfter=(Get-FileHash -LiteralPath $nativePath -Algorithm SHA256).Hash
$harmonyAfter=(Get-FileHash -LiteralPath $HarmonyPath -Algorithm SHA256).Hash
if ($runExit -ne 0) { throw "Render-scope regression failed (exit $runExit); see $Output\run.log" }
$resultPath=Join-Path $run 'result.json'
if (!(Test-Path -LiteralPath $resultPath -PathType Leaf)) { throw 'Regression result is missing.' }
$result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if (!$result.Passed -or $result.MeasurementsPerformed -or $result.Checks -lt 26 -or $result.AppearanceChecks -le 0 -or @($result.AppearanceTargets).Count -eq 0) { throw 'Regression result did not satisfy its expected checks.' }
if ($result.ProductionSHA256 -ne $expected -or $after -ne $expected) { throw 'Executed production DLL hash did not match or changed during the run.' }
if ($result.NativeAssemblySHA256 -ne $nativeBefore -or $nativeAfter -ne $nativeBefore -or $harmonyAfter -ne $harmonyBefore) {
    throw 'Native or Harmony inputs did not match or changed during the run.'
}
$summary=[ordered]@{
    Passed=$true;Checks=$result.Checks;MeasurementsPerformed=$false;GameLaunched=$false
    ProductionPath=$WildShiftPath;ExpectedWildShiftSHA256=$expected
    ProductionSHA256Before=$before;ProductionSHA256After=$after
    NativeSHA256=$nativeBefore;HarmonySHA256=$harmonyBefore
    ResultPath=$resultPath
    Scope='Isolated actual-native 1.7 method regression; no initialized game, FPS/TPS, 1.6 Unity or rendered-gameplay claim.'
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'runner-result.json') -Encoding UTF8
$summary | ConvertTo-Json -Depth 5
