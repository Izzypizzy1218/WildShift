<#
.SYNOPSIS
Builds or runs an isolated full-engine temporary-map regression for RimWorld 1.7.
.DESCRIPTION
BuildOnly creates a TEST ONLY mod outside the repository. This script never installs
it: stage that folder in the game's mod search path separately, then pass FixtureDir
with SkipBuild. The runner refuses a currently running game, writes only its own
savedata profile, launches its own process and may terminate only that process.
For a red check, temporarily select the known baseline DLL externally and use
ExpectFailure. The runner requires the real map-loss plus Game Over evidence.
#>
[CmdletBinding()]
param(
    [string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$WildShiftDir='',
    [string]$FixtureDir='',
    [string]$Output='',
    [string]$DotNet='',
    [ValidateSet('Site','Camp')][string]$TemporaryMapKind='Site',
    [ValidateRange(30,1800)][int]$TimeoutSeconds=240,
    [switch]$BuildOnly,
    [switch]$SkipBuild,
    [switch]$ExpectFailure,
    [switch]$CoreOnly
)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\','/')
$workspace=Split-Path $repoRoot -Parent
if (!$DotNet) { $DotNet=Join-Path $workspace '.tools\dotnet10\dotnet.exe' }
if (!$WildShiftDir) { $WildShiftDir=Join-Path $GameDir 'Mods\WildShift' }
if (!$Output) { $Output=Join-Path $workspace ('.tmp\wildshift-temporary-map17-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)) }
$Output=[IO.Path]::GetFullPath($Output).TrimEnd('\','/')
if ($Output.Equals($repoRoot,[StringComparison]::OrdinalIgnoreCase) -or $Output.StartsWith($repoRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside the source repository.' }
if ((Test-Path -LiteralPath $Output) -and !$BuildOnly) { throw 'Refusing an existing run output.' }
$null=New-Item -ItemType Directory -Force -Path $Output

function PackageId([string]$path) {
    [xml]$xml=Get-Content -LiteralPath (Join-Path $path 'About\About.xml') -Raw
    ([string]$xml.ModMetaData.packageId).Trim().ToLowerInvariant()
}
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }

if (!$SkipBuild) {
    $builtMod=Join-Path $Output 'fixture-mod\WildShiftTemporaryMapValidation'
    $builtDll=Join-Path $builtMod 'Assemblies\WildShiftTemporaryMapValidation.dll'
    $null=New-Item -ItemType Directory -Force -Path (Join-Path $builtMod 'About')
    $obj=(Join-Path $Output 'fixture-obj')+[IO.Path]::DirectorySeparatorChar
    & $DotNet build (Join-Path $PSScriptRoot 'TemporaryMap17\WildShift.TemporaryMap17Tests.csproj') -c Release -o (Join-Path $builtMod 'Assemblies') "-p:BaseIntermediateOutputPath=$obj" "-p:RimWorldDir=$GameDir" "-p:HarmonyPath=$HarmonyPath" --nologo -v:minimal
    if ($LASTEXITCODE) { throw "Fixture build failed: $LASTEXITCODE" }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TemporaryMap17\About.xml') -Destination (Join-Path $builtMod 'About\About.xml')
    $buildRecord=[pscustomobject]@{FixtureDir=$builtMod;Dll=$builtDll;SHA256=(Hash $builtDll);SourceSHA256=(Hash (Join-Path $PSScriptRoot 'TemporaryMap17\TemporaryMapRegression.cs'));TestOnly=$true}
    $buildRecord | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'fixture-build.json')
    if ($BuildOnly) { $buildRecord | ConvertTo-Json; return }
    if (!$FixtureDir) { throw 'Fixture built. Stage its test-only mod separately, then run with SkipBuild and FixtureDir; the runner does not install mods.' }
    if ((Hash (Join-Path $FixtureDir 'Assemblies\WildShiftTemporaryMapValidation.dll')) -ne $buildRecord.SHA256) { throw 'Staged fixture differs from the just-built fixture.' }
}
if (!$FixtureDir) { throw 'Specify the separately staged test-only FixtureDir.' }
if (@(Get-Process -Name RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue).Count) { throw 'Close the running RimWorld before running this test.' }
if ((PackageId $FixtureDir) -ne 'wildshift.tests.temporarymap17' -or (PackageId $WildShiftDir) -ne 'wildshift.mod') { throw 'Unexpected fixture or WildShift package ID.' }
$version=(Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -Raw).Trim()
if ($version -notmatch '^1\.7\.') { throw "Expected game 1.7, found $version." }
$expectedDll=Join-Path $WildShiftDir '1.7\Assemblies\WildShift.dll'
$expectedFixture=Join-Path $FixtureDir 'Assemblies\WildShiftTemporaryMapValidation.dll'
$expectedHash=Hash $expectedDll
$expectedFixtureHash=Hash $expectedFixture
$profile=Join-Path $Output 'profile'
$config=Join-Path $profile 'Config'
$null=New-Item -ItemType Directory -Force -Path $config
$mods=@('brrainz.harmony','ludeon.rimworld')
$dlcs=@()
if (!$CoreOnly) {
    foreach ($name in @('Royalty','Ideology','Biotech','Anomaly','Odyssey')) {
        $path=Join-Path $GameDir ('Data\'+$name)
        if (Test-Path -LiteralPath (Join-Path $path 'About\About.xml')) { $dlcs+=PackageId $path }
    }
}
$mods+=$dlcs
$mods+=@('wildshift.mod','wildshift.tests.temporarymap17')
$li=($mods | ForEach-Object { '<li>'+[Security.SecurityElement]::Escape($_)+'</li>' }) -join ''
$dlcLi=($dlcs | ForEach-Object { '<li>'+[Security.SecurityElement]::Escape($_)+'</li>' }) -join ''
('<ModsConfigData><version>'+[Security.SecurityElement]::Escape($version)+'</version><activeMods>'+$li+'</activeMods><knownExpansions>'+$dlcLi+'</knownExpansions></ModsConfigData>') | Set-Content -LiteralPath (Join-Path $config 'ModsConfig.xml') -Encoding utf8
'<PrefsData><langFolderName>English</langFolderName><devMode>True</devMode><screenWidth>1280</screenWidth><screenHeight>720</screenHeight><fullscreen>False</fullscreen><runInBackground>True</runInBackground></PrefsData>' | Set-Content -LiteralPath (Join-Path $config 'Prefs.xml') -Encoding utf8
$userConfig=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config'
$normalConfig=@(if (Test-Path -LiteralPath $userConfig) { foreach($file in Get-ChildItem -LiteralPath $userConfig -File -Recurse) { [pscustomobject]@{Path=$file.FullName;SHA256=(Hash $file.FullName)} } })
$normalConfig | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'normal-config-before.json')
$legacyLogRoot=Split-Path $userConfig -Parent
foreach ($name in @('Player.log','Player-prev.log')) { $path=Join-Path $legacyLogRoot $name; if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $Output ('legacy-'+$name)) } }
$result=[ordered]@{Passed=$false;ExpectedFailure=[bool]$ExpectFailure;MapKind=$TemporaryMapKind;GameVersion=$version;RequestedMods=$mods;WildShiftSHA256=$expectedHash;FixtureSHA256=$expectedFixtureHash;Phases=@();Errors=@();NormalConfigUnchanged=$false;Scope='Full native initialized-game regression; no visual or optional-mod compatibility claim.'}

function RunPhase([int]$number,[bool]$quickTest) {
    if (@(Get-Process -Name RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue).Count) { throw 'Another game is running; refusing concurrent launch.' }
    $stdout=Join-Path $Output ('phase-'+$number+'-stdout.log')
    $stderr=Join-Path $Output ('phase-'+$number+'-stderr.log')
    $arguments=@('-Headless','-NoCrashDialog','-NoFileLog',('-savedatafolder="'+$profile+'"'),('-TemporaryMapKind='+$TemporaryMapKind))
    if ($quickTest) { $arguments+='-quicktest' }
    $process=$null
    try {
        $process=Start-Process -FilePath (Join-Path $GameDir 'RimWorld.exe') -ArgumentList $arguments -WorkingDirectory $GameDir -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        $process.Id | Set-Content -LiteralPath (Join-Path $Output ('phase-'+$number+'-pid.txt'))
        $timer=[Diagnostics.Stopwatch]::StartNew()
        while (!$process.WaitForExit(1000)) { if ($timer.Elapsed.TotalSeconds -gt $TimeoutSeconds) { throw "Phase $number timed out after $TimeoutSeconds seconds." } }
        $process.WaitForExit()
        $process.Refresh()
        if ($process.ExitCode -ne 0) { throw "Phase $number exited with $($process.ExitCode)." }
    } finally {
        if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        if ($process) { $process.Dispose() }
    }
    $errors=@(Select-String -LiteralPath $stdout -Pattern '\bERROR\b' | ForEach-Object Line)
    $fatal=@(Select-String -LiteralPath $stderr -Pattern 'Fatal error|Internal CLR error|Unhandled exception' | ForEach-Object Line)
    if ($errors.Count -or $fatal.Count) { throw "Phase $number logged native errors; inspect its stdout/stderr." }
    $path=Join-Path $profile ('TemporaryMapResults\phase-'+$number+'.json')
    if (!(Test-Path -LiteralPath $path)) { throw "Phase $number did not write an in-process result." }
    $phase=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if ($phase.WildShiftSHA256 -ne $expectedHash -or $phase.FixtureSHA256 -ne $expectedFixtureHash) { throw 'Actual loaded input hash differs from the expected staged DLL.' }
    if (($phase.Mods -join '|') -ne ($mods -join '|')) { throw 'Actual loaded mod list differs from the isolated profile.' }
    $phase
}

try {
    $phase0=RunPhase 0 $true
    $result.Phases+=@($phase0)
    if ($ExpectFailure) {
        if ($phase0.Passed -or !$phase0.BaselineDefectObserved -or $phase0.MapRetained -or !$phase0.CaravanLostLetter -or !$phase0.GameOverLetter) { throw 'Expected baseline map-loss and Game Over defect was not reproduced.' }
    } else {
        if (!$phase0.Passed -or $phase0.Phase -ne 'save-ready') { throw 'Initial transform/retention/save phase failed.' }
        if (!(Test-Path -LiteralPath (Join-Path $profile 'Saves\autostart.rws'))) { throw 'Native autostart save missing.' }
        $phase1=RunPhase 1 $false
        $result.Phases+=@($phase1)
        if (!$phase1.Passed -or $phase1.Phase -ne 'complete') { throw 'Native reload/reversion/departure phase failed.' }
    }
} catch { $result.Errors+= $_.Exception.Message }
finally {
    $changed=@($normalConfig | Where-Object { !(Test-Path -LiteralPath $_.Path) -or (Hash $_.Path) -ne $_.SHA256 })
    $result.NormalConfigUnchanged=($changed.Count -eq 0)
    if (!$result.NormalConfigUnchanged) { $result.Errors+='Normal user config changed.' }
    if ((Hash $expectedDll) -ne $expectedHash -or (Hash $expectedFixture) -ne $expectedFixtureHash) { $result.Errors+='Staged inputs changed during execution.' }
    $result.Passed=($result.Errors.Count -eq 0)
    $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Output 'result.json')
}
$result | ConvertTo-Json -Depth 10
if (!$result.Passed) { throw "Temporary map regression failed; see $(Join-Path $Output 'result.json')." }
