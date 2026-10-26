<#
.SYNOPSIS
Builds or runs an isolated full-engine shared-state regression for RimWorld 1.6 with all five DLCs.
.DESCRIPTION
BuildOnly writes a TEST ONLY helper outside the repository; it never installs it.
Stage the helper separately, then use SkipBuild and FixtureDir. The runner refuses
an existing game, isolates all savedata/config, validates loaded input hashes and
mod IDs, and may terminate only its own process. It never swaps production DLLs.
#>
[CmdletBinding()]
param(
    [string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$GameExe='',
    [string]$RimWorldManaged='',
    [string]$HarmonyPath='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.6\Assemblies\0Harmony.dll',
    [string]$WildShiftDir='',
    [string]$WildShiftPath='',
    [string]$FixtureDir='',
    [string]$Output='',
    [string]$DotNet='',
    [string[]]$AdditionalMods=@(),
    [ValidateRange(30,1800)][int]$TimeoutSeconds=240,
    [switch]$BuildOnly,
    [switch]$SkipBuild
)
$ErrorActionPreference='Stop'
if ($BuildOnly -and $SkipBuild) { throw 'BuildOnly and SkipBuild are mutually exclusive; BuildOnly never launches a game.' }
$repoRoot=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\','/')
$workspace=Split-Path $repoRoot -Parent
if (!$DotNet) { $DotNet=Join-Path $workspace '.tools\dotnet10\dotnet.exe' }
if (!$WildShiftDir) { $WildShiftDir=Join-Path $GameDir 'Mods\WildShift' }
if (!$WildShiftPath) { $WildShiftPath=Join-Path $(if ($BuildOnly) { $repoRoot } else { $WildShiftDir }) '1.6\Assemblies\WildShift.dll' }
if (!$RimWorldManaged) {
    $RimWorldManaged=Join-Path $GameDir 'RimWorldWin64_Data\Managed'
    if ($BuildOnly -and !(Test-Path -LiteralPath (Join-Path $RimWorldManaged 'Assembly-CSharp.dll'))) {
        $RimWorldManaged=Join-Path ([Environment]::GetFolderPath('UserProfile')) '.nuget\packages\krafs.rimworld.ref\1.6.4871\ref\net472'
    }
}
if (!$Output) { $Output=Join-Path $workspace ('.tmp\wildshift-sharedstate16-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8)) }
$Output=[IO.Path]::GetFullPath($Output).TrimEnd('\','/')
if ($Output.Equals($repoRoot,[StringComparison]::OrdinalIgnoreCase) -or $Output.StartsWith($repoRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside the source repository.' }
if (Test-Path -LiteralPath $Output) { throw 'Use a fresh external output directory; previous build/run evidence is preserved.' }
$null=New-Item -ItemType Directory -Force -Path $Output

function PackageId([string]$path) {
    [xml]$xml=Get-Content -LiteralPath (Join-Path $path 'About\About.xml') -Raw
    ([string]$xml.ModMetaData.packageId).Trim().ToLowerInvariant()
}
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function ReplaceOnce([string]$source,[string]$before,[string]$after) {
    $position=$source.IndexOf($before,[StringComparison]::Ordinal)
    if ($position -lt 0 -or $source.IndexOf($before,$position+$before.Length,[StringComparison]::Ordinal) -ge 0) {
        throw 'Shared 1.7 fixture changed: a 1.6 adaptation anchor is missing or ambiguous. Review before building.'
    }
    $source.Remove($position,$before.Length).Insert($position,$after)
}

if (!$SkipBuild) {
    $referenceVersion=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $RimWorldManaged 'Assembly-CSharp.dll')).Version
    if ($referenceVersion.Major -ne 1 -or $referenceVersion.Minor -ne 6) { throw 'Build references must be RimWorld 1.6 assemblies.' }
    $builtMod=Join-Path $Output 'fixture-mod\WildShiftSharedState16Validation'
    $builtDll=Join-Path $builtMod 'Assemblies\WildShiftSharedState16Validation.dll'
    $null=New-Item -ItemType Directory -Force -Path (Join-Path $builtMod 'About')
    # Reuse the unchanged 1.7 scenario. Only adapt fixture infrastructure and
    # version labels; native gameplay operations remain the shared source.
    $sharedSource=Join-Path $PSScriptRoot 'SharedState17\SharedStateRegression.cs'
    $generatedDir=Join-Path $Output 'compat-sources'
    $null=New-Item -ItemType Directory -Force -Path $generatedDir
    $generatedSource=Join-Path $generatedDir 'SharedStateRegression16.cs'
    $source=[IO.File]::ReadAllText($sharedSource)
    $source=ReplaceOnce $source 'using System.Text.Json;' 'using WildShiftSharedStateValidation.Json;'
    $source=ReplaceOnce $source '"SharedStateResults"' '"SharedState16Results"'
    $source=ReplaceOnce $source '"wildshift.tests.sharedstate17"' '"wildshift.tests.sharedstate16"'
    $source=ReplaceOnce $source 'Directory.CreateDirectory(Output);' "Directory.CreateDirectory(Output);`n            Engine16Context.Validate();"
    $source=ReplaceOnce $source 'Actual initialized 1.7 game' 'Actual initialized 1.6 game'
    $source=ReplaceOnce $source 'Phase = phase, Passed = failures == 0, Failures = failures,' @'
Phase = phase, Passed = failures == 0, Failures = failures,
                GameVersion = Engine16Context.GameVersion, GameAssemblyPath = Engine16Context.GameAssemblyPath,
                GameAssemblySHA256 = Engine16Context.GameAssemblySHA256, HarmonySHA256 = Engine16Context.HarmonySHA256,
                SaveDataPath = GenFilePaths.SaveDataFolderPath, UnityVersion = Application.unityVersion,
                NativeErrors = Log.Messages.Where(m => m.type == LogMessageType.Error).Select(m => m.text).ToArray(),
'@
    [IO.File]::WriteAllText($generatedSource,$source,[Text.UTF8Encoding]::new($false))
    $obj=(Join-Path $Output 'fixture-obj')+[IO.Path]::DirectorySeparatorChar
    & $DotNet build (Join-Path $PSScriptRoot 'SharedState16\WildShift.SharedState16Tests.csproj') -c Release -o (Join-Path $builtMod 'Assemblies') "-p:BaseIntermediateOutputPath=$obj" "-p:RimWorldManaged=$RimWorldManaged" "-p:SharedRegressionSource=$generatedSource" "-p:HarmonyPath=$HarmonyPath" "-p:WildShiftPath=$WildShiftPath" --nologo -v:minimal
    if ($LASTEXITCODE) { throw "Fixture build failed: $LASTEXITCODE" }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SharedState16\About.xml') -Destination (Join-Path $builtMod 'About\About.xml')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SharedState17\Defs') -Destination $builtMod -Recurse
    $sourceInputs=@($sharedSource,(Join-Path $PSScriptRoot 'SharedState17\SharedHealthEngineChecks.cs'),
        (Join-Path $PSScriptRoot 'SharedState16\Engine16Context.cs'),(Join-Path $PSScriptRoot 'SharedState16\JsonSerializer.cs'),
        (Join-Path $PSScriptRoot 'SharedState16\WildShift.SharedState16Tests.csproj'),(Join-Path $PSScriptRoot 'SharedState16\About.xml'))
    $sourceInputs+=@(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'SharedState17\Defs') -File | ForEach-Object FullName)
    foreach ($optional in @('SharedAbilitiesEngineChecks.cs','SharedBuffEngineChecks.cs')) {
        $path=Join-Path $PSScriptRoot ('SharedState17\'+$optional)
        if (Test-Path -LiteralPath $path) { $sourceInputs+=$path }
    }
    $buildRecord=[pscustomobject]@{FixtureDir=$builtMod;Dll=$builtDll;SHA256=(Hash $builtDll);GeneratedSource=$generatedSource;
        GeneratedSourceSHA256=(Hash $generatedSource);SourceInputs=@($sourceInputs | ForEach-Object { [pscustomobject]@{Path=$_;SHA256=(Hash $_)} });
        ProductionPath=$WildShiftPath;ProductionSHA256=(Hash $WildShiftPath);ReferenceAssembly=(Join-Path $RimWorldManaged 'Assembly-CSharp.dll');
        ReferenceSHA256=(Hash (Join-Path $RimWorldManaged 'Assembly-CSharp.dll'));ReferenceVersion=$referenceVersion.ToString();
        TestOnly=$true;PreparedOnly=$BuildOnly.IsPresent}
    $buildRecord | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $Output 'fixture-build.json')
    if ($BuildOnly) { $buildRecord | ConvertTo-Json -Depth 6; return }
    if (!$FixtureDir) { throw 'Fixture built. Stage its test-only mod separately, then run with SkipBuild and FixtureDir; the runner does not install mods.' }
    if ((Hash (Join-Path $FixtureDir 'Assemblies\WildShiftSharedState16Validation.dll')) -ne $buildRecord.SHA256) { throw 'Staged fixture differs from the just-built fixture.' }
}
if (!$FixtureDir) { throw 'Specify the separately staged test-only FixtureDir.' }
if (@(Get-Process -Name RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue).Count) { throw 'Close the running RimWorld before running this test.' }
if ((PackageId $FixtureDir) -ne 'wildshift.tests.sharedstate16' -or (PackageId $WildShiftDir) -ne 'wildshift.mod') { throw 'Unexpected fixture or WildShift package ID.' }
$version=(Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -Raw).Trim()
if ($version -notmatch '^1\.6\.') { throw "Expected game 1.6, found $version." }
if ($version -notmatch '^(1\.6\.\d+)') { throw 'Unable to parse actual 1.6 Version.txt.' }
$runtimeVersion=$Matches[1]
if (!$GameExe) { $GameExe=Join-Path $GameDir 'RimWorldWin64.exe' }
if (!(Test-Path -LiteralPath $GameExe)) { throw 'Specify an actual Unity RimWorld 1.6 executable with GameExe.' }
$expectedGameAssembly=Join-Path $GameDir 'RimWorldWin64_Data\Managed\Assembly-CSharp.dll'
if (!(Test-Path -LiteralPath $expectedGameAssembly)) { throw 'Actual Unity 1.6 managed game assembly is missing.' }
$expectedGameHash=Hash $expectedGameAssembly
$expectedDll=Join-Path $WildShiftDir '1.6\Assemblies\WildShift.dll'
$expectedFixture=Join-Path $FixtureDir 'Assemblies\WildShiftSharedState16Validation.dll'
$expectedHash=Hash $expectedDll
$expectedFixtureHash=Hash $expectedFixture
if ((Hash $WildShiftPath) -ne $expectedHash) { throw 'Staged WildShift DLL differs from the specified source candidate DLL.' }
$expectedHarmonyHash=Hash $HarmonyPath
$profile=Join-Path $Output 'profile'
$config=Join-Path $profile 'Config'
$null=New-Item -ItemType Directory -Force -Path $config
$inputValues=[ordered]@{GameVersion=$runtimeVersion;SaveDataPath=$profile;GameAssemblyPath=$expectedGameAssembly;
    GameAssemblySHA256=$expectedGameHash;WildShiftPath=$expectedDll;WildShiftSHA256=$expectedHash;
    FixturePath=$expectedFixture;FixtureSHA256=$expectedFixtureHash;HarmonySHA256=$expectedHarmonyHash}
$inputXml='<SharedState16Inputs>'
foreach ($key in $inputValues.Keys) { $inputXml+='<'+$key+'>'+[Security.SecurityElement]::Escape([string]$inputValues[$key])+'</'+$key+'>' }
$inputXml+='</SharedState16Inputs>'
$inputXml | Set-Content -LiteralPath (Join-Path $profile 'sharedstate16-inputs.xml') -Encoding utf8
$mods=@('brrainz.harmony','ludeon.rimworld')
$installedDlcs=@()
foreach ($name in @('Royalty','Ideology','Biotech','Anomaly','Odyssey')) {
    $path=Join-Path $GameDir ('Data\'+$name)
    if (Test-Path -LiteralPath (Join-Path $path 'About\About.xml')) { $installedDlcs+=PackageId $path }
}
if ($installedDlcs.Count -ne 5) { throw 'All five DLCs are required by this shared-state fixture.' }
$mods+=$installedDlcs
$mods+=@($AdditionalMods | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ -and $_ -notin $mods } | Select-Object -Unique)
$mods+=@('wildshift.mod','wildshift.tests.sharedstate16')
$li=($mods | ForEach-Object { '<li>'+[Security.SecurityElement]::Escape($_)+'</li>' }) -join ''
$dlcLi=($installedDlcs | ForEach-Object { '<li>'+[Security.SecurityElement]::Escape($_)+'</li>' }) -join ''
('<ModsConfigData><version>'+[Security.SecurityElement]::Escape($version)+'</version><activeMods>'+$li+'</activeMods><knownExpansions>'+$dlcLi+'</knownExpansions></ModsConfigData>') | Set-Content -LiteralPath (Join-Path $config 'ModsConfig.xml') -Encoding utf8
'<PrefsData><langFolderName>English</langFolderName><devMode>True</devMode><screenWidth>1280</screenWidth><screenHeight>720</screenHeight><fullscreen>False</fullscreen><runInBackground>True</runInBackground></PrefsData>' | Set-Content -LiteralPath (Join-Path $config 'Prefs.xml') -Encoding utf8
$userConfig=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config'
$normalConfig=@(if (Test-Path -LiteralPath $userConfig) { foreach($file in Get-ChildItem -LiteralPath $userConfig -File -Recurse) { [pscustomobject]@{Path=$file.FullName;SHA256=(Hash $file.FullName)} } })
$normalConfig | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'normal-config-before.json')
$legacyLogRoot=Split-Path $userConfig -Parent
foreach ($name in @('Player.log','Player-prev.log')) { $path=Join-Path $legacyLogRoot $name; if (Test-Path -LiteralPath $path) { Copy-Item -LiteralPath $path -Destination (Join-Path $Output ('legacy-'+$name)) } }
$result=[ordered]@{Passed=$false;Scenario='Shared state 1.6';GameVersion=$version;GameExe=$GameExe;GameExeSHA256=(Hash $GameExe);
    GameAssemblySHA256=$expectedGameHash;SourceWildShiftPath=$WildShiftPath;RequestedMods=$mods;WildShiftSHA256=$expectedHash;
    FixtureSHA256=$expectedFixtureHash;HarmonySHA256=$expectedHarmonyHash;Phases=@();Errors=@();NormalConfigUnchanged=$false;
    Scope='Full native Unity 1.6 initialized-game shared-state regression with all five DLCs; no visual or optional-mod compatibility claim.'}

function RunPhase([int]$number,[bool]$quickTest) {
    if (@(Get-Process -Name RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue).Count) { throw 'Another game is running; refusing concurrent launch.' }
    $stdout=Join-Path $Output ('phase-'+$number+'-stdout.log')
    $stderr=Join-Path $Output ('phase-'+$number+'-stderr.log')
    $unityLog=Join-Path $Output ('phase-'+$number+'-unity.log')
    # Initialize the real graphics device even in batch mode. Unity 1.6's
    # native texture-atlas setup throws with -nographics; do not suppress those
    # errors or replace its rendering methods just to make the fixture pass.
    # Do not pass the managed 1.7 Headless switch or -quit before fixture save.
    $arguments=@('-batchmode','-force-d3d11','-screen-fullscreen','0','-logFile',('"'+$unityLog+'"'),('-savedatafolder="'+$profile+'"'))
    if ($quickTest) { $arguments+='-quicktest' }
    $process=$null
    try {
        $process=Start-Process -FilePath $GameExe -ArgumentList $arguments -WorkingDirectory $GameDir -Environment @{SteamAppId='294100';SteamGameId='294100'} -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
        $process.Id | Set-Content -LiteralPath (Join-Path $Output ('phase-'+$number+'-pid.txt'))
        $timer=[Diagnostics.Stopwatch]::StartNew()
        while (!$process.WaitForExit(1000)) {
            if ($timer.Elapsed.TotalSeconds -gt $TimeoutSeconds) { throw "Phase $number timed out after $TimeoutSeconds seconds." }
        }
        $process.WaitForExit()
        $process.Refresh()
        if ($process.ExitCode -ne 0) { throw "Phase $number exited with $($process.ExitCode)." }
    } finally {
        if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        if ($process) { $process.Dispose() }
    }
    $fatal=@(foreach ($log in @($stdout,$stderr,$unityLog)) { if (Test-Path -LiteralPath $log) {
        Select-String -LiteralPath $log -Pattern 'Fatal error|Internal CLR error|Unhandled exception|Crash!!!|Critical error in root' | ForEach-Object Line
    } })
    if ($fatal.Count) { throw "Phase $number logged a fatal native error; inspect its logs." }
    $path=Join-Path $profile ('SharedState16Results\phase-'+$number+'.json')
    if (!(Test-Path -LiteralPath $path)) { throw "Phase $number did not write an in-process result." }
    $phase=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    if (@($phase.NativeErrors).Count) { throw "Phase $number logged native Verse errors; inspect the in-process result and Unity log." }
    if ($phase.GameVersion -ne $runtimeVersion -or $phase.GameAssemblySHA256 -ne $expectedGameHash -or
        $phase.HarmonySHA256 -ne $expectedHarmonyHash) { throw 'Actual loaded 1.6 game/Harmony differs from the expected runtime.' }
    if ([IO.Path]::GetFullPath($phase.SaveDataPath) -ne [IO.Path]::GetFullPath($profile)) { throw 'Actual 1.6 save data path is not the isolated profile.' }
    if ($phase.WildShiftSHA256 -ne $expectedHash -or $phase.FixtureSHA256 -ne $expectedFixtureHash) { throw 'Actual loaded input hash differs from the expected staged DLL.' }
    if (($phase.Mods -join '|') -ne ($mods -join '|')) { throw 'Actual loaded mod list differs from the isolated profile.' }
    $phase
}

try {
    $phase0=RunPhase 0 $true
    $result.Phases+=@($phase0)
    if (!$phase0.Passed -or $phase0.Phase -ne 'save-ready') { throw 'Initial shared-state/save phase failed.' }
    if (!(Test-Path -LiteralPath (Join-Path $profile 'Saves\autostart.rws'))) { throw 'Native autostart save missing.' }
    $phase1=RunPhase 1 $false
    $result.Phases+=@($phase1)
    if (!$phase1.Passed -or $phase1.Phase -ne 'active-save-ready') { throw 'Native inactive-body reload/active-form save phase failed.' }
    if (!(Test-Path -LiteralPath (Join-Path $profile 'Saves\autostart.rws'))) { throw 'Native active-form save missing.' }
    $phase2=RunPhase 2 $false
    $result.Phases+=@($phase2)
    if (!$phase2.Passed -or $phase2.Phase -ne 'complete') { throw 'Native active-form reload/off-map return phase failed.' }
} catch { $result.Errors+= $_.Exception.Message }
finally {
    $normalAfter=@(if (Test-Path -LiteralPath $userConfig) { foreach($file in Get-ChildItem -LiteralPath $userConfig -File -Recurse) { [pscustomobject]@{Path=$file.FullName;SHA256=(Hash $file.FullName)} } })
    $normalAfter | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'normal-config-after.json')
    $changed=@(Compare-Object -ReferenceObject @($normalConfig | ForEach-Object { $_.Path+'|'+$_.SHA256 }) -DifferenceObject @($normalAfter | ForEach-Object { $_.Path+'|'+$_.SHA256 }))
    $result.NormalConfigUnchanged=($changed.Count -eq 0)
    if (!$result.NormalConfigUnchanged) { $result.Errors+='Normal user config changed.' }
    if ((Hash $expectedDll) -ne $expectedHash -or (Hash $expectedFixture) -ne $expectedFixtureHash) { $result.Errors+='Staged inputs changed during execution.' }
    if ((Hash $expectedGameAssembly) -ne $expectedGameHash -or (Hash $GameExe) -ne $result.GameExeSHA256) { $result.Errors+='Game runtime inputs changed during execution.' }
    $result.Passed=($result.Errors.Count -eq 0)
    $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $Output 'result.json')
}
$result | ConvertTo-Json -Depth 10
if (!$result.Passed) { throw "Shared state regression failed; see $(Join-Path $Output 'result.json')." }
