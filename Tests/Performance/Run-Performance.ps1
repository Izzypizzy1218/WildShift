<# TEST ONLY: isolated native seed or one timed measurement. Never installs a mod,
changes a normal save/config, or starts a second RimWorld process. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$Output,
    [ValidateSet('seed','measure')][string]$Phase='measure',
    [ValidateSet(0,20,50)][int]$ActiveForms=0,
    [ValidateRange(1,100)][int]$TotalPawns=50,
    [ValidateRange(0,600)][int]$WarmupSeconds=60,
    [ValidateRange(10,7200)][int]$MeasureSeconds=600,
    [ValidateRange(1,30)][int]$SampleSeconds=10,
    [ValidateSet('basic','abilities')][string]$FeaturePreset='basic',
    [ValidateSet('Native','Headless')][string]$GraphicsMode='Native',
    [string]$BaselineSave='',
    [string]$WildShiftDir='',
    [string]$WildShiftPath='',
    [string]$FixtureDir='',
    [string]$HarmonyPath='',
    [string]$DotNet='',
    [switch]$Smoke,
    [switch]$BuildOnly,
    [switch]$SkipBuild
)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)).TrimEnd('\','/')
$workspace=Split-Path $repo -Parent
$Output=[IO.Path]::GetFullPath($Output).TrimEnd('\','/')
$GameDir=[IO.Path]::GetFullPath($GameDir).TrimEnd('\','/')
if($Output.Equals($repo,[StringComparison]::OrdinalIgnoreCase) -or $Output.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Evidence must be outside the repository.'}
if(Test-Path -LiteralPath $Output){throw 'Use fresh output; previous evidence is immutable.'}
if($BuildOnly -and $SkipBuild){throw 'BuildOnly and SkipBuild conflict.'}
if($ActiveForms -gt $TotalPawns){throw 'ActiveForms exceeds the fixed population.'}
$null=New-Item -ItemType Directory -Path $Output
function Hash([string]$p){(Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash}
function Package([string]$p){([string]([xml](Get-Content -LiteralPath (Join-Path $p 'About\About.xml') -Raw)).ModMetaData.packageId).Trim().ToLowerInvariant()}
function Inventory([string]$p){if(Test-Path -LiteralPath $p){foreach($f in Get-ChildItem -LiteralPath $p -Recurse -File|Sort-Object FullName){[pscustomobject]@{Path=$f.FullName;SHA256=(Hash $f.FullName)}}}}
function Games(){@(Get-CimInstance Win32_Process|Where-Object {$_.Name -match '^RimWorld.*\.exe$'})}
$version=(Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -Raw).Trim()
if($version -notmatch '^(1\.[67])\.'){throw ('Unsupported actual engine: '+$version)}
$minor=$Matches[1]
$managed=if($minor -eq '1.7'){$GameDir}else{Join-Path $GameDir 'RimWorldWin64_Data\Managed'}
$gameAssembly=Join-Path $managed 'Assembly-CSharp.dll'
$exe=Join-Path $GameDir $(if($minor -eq '1.7'){'RimWorld.exe'}else{'RimWorldWin64.exe'})
if(!$DotNet){$DotNet=Join-Path $workspace '.tools\dotnet10\dotnet.exe'}
if(!$WildShiftDir){$WildShiftDir=Join-Path $GameDir 'Mods\WildShift'}
if(!$WildShiftPath){$WildShiftPath=Join-Path $WildShiftDir ($minor+'\Assemblies\WildShift.dll')}
if(!$HarmonyPath){
    $candidates=@(Get-ChildItem -LiteralPath (Join-Path $GameDir 'Mods') -Directory|Where-Object {(Test-Path -LiteralPath (Join-Path $_.FullName 'About\About.xml')) -and (Package $_.FullName) -eq 'brrainz.harmony'})
    if($candidates.Count -ne 1){throw 'Supply one explicit matching HarmonyPath.'}
    $HarmonyPath=Join-Path $candidates[0].FullName ($minor+'\Assemblies\0Harmony.dll')
}
foreach($p in @($exe,$gameAssembly,$WildShiftPath,$HarmonyPath,$DotNet)){if(!(Test-Path -LiteralPath $p -PathType Leaf)){throw ('Missing input: '+$p)}}
if(!$SkipBuild){
    $built=Join-Path $Output 'fixture-mod\WildShiftPerformanceProbe'
    $null=New-Item -ItemType Directory -Path (Join-Path $built 'About') -Force
    $obj=(Join-Path $Output 'obj')+'\'
    & $DotNet build (Join-Path $PSScriptRoot 'WildShift.Performance.csproj') -c Release -o (Join-Path $built 'Assemblies') "-p:BaseIntermediateOutputPath=$obj" "-p:RimWorldVersion=$minor" "-p:RimWorldDir=$GameDir" "-p:RimWorldManaged=$managed" "-p:HarmonyPath=$HarmonyPath" "-p:WildShiftPath=$WildShiftPath" --nologo -v:minimal
    if($LASTEXITCODE){throw 'Performance helper compilation failed.'}
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'About.xml') -Destination (Join-Path $built 'About\About.xml')
    $build=[pscustomobject]@{FixtureDir=$built;SHA256=(Hash (Join-Path $built 'Assemblies\WildShiftPerformanceProbe.dll'));ProductionSHA256=(Hash $WildShiftPath);GameVersion=$version;GameAssemblySHA256=(Hash $gameAssembly);Sources=@(Get-ChildItem -LiteralPath $PSScriptRoot -File|ForEach-Object {[pscustomobject]@{Path=$_.FullName;SHA256=(Hash $_.FullName)}});TestOnly=$true}
    $build|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $Output 'fixture-build.json')
    if($BuildOnly){$build|ConvertTo-Json -Depth 5;return}
    if(!$FixtureDir){throw 'Build completed; stage the test-only helper separately, then run with SkipBuild.'}
    if((Hash (Join-Path $FixtureDir 'Assemblies\WildShiftPerformanceProbe.dll')) -ne $build.SHA256){throw 'Staged helper differs from this build.'}
}
if((Games).Count){throw 'Another RimWorld is running; no concurrent performance tests.'}
if(!$FixtureDir -or (Package $FixtureDir) -ne 'wildshift.tests.performance' -or (Package $WildShiftDir) -ne 'wildshift.mod'){throw 'Unexpected fixture or production package.'}
$fixture=Join-Path $FixtureDir 'Assemblies\WildShiftPerformanceProbe.dll'
if(!(Test-Path -LiteralPath $fixture)){throw 'Fixture DLL missing.'}
if($Phase -eq 'measure' -and !(Test-Path -LiteralPath $BaselineSave -PathType Leaf)){throw 'A copied native baseline save is required.'}
$profile=Join-Path $Output 'profile'
$config=Join-Path $profile 'Config'
$null=New-Item -ItemType Directory -Path $config -Force
$dlcs=@(foreach($name in @('Royalty','Ideology','Biotech','Anomaly','Odyssey')){$p=Join-Path $GameDir ('Data\'+$name);if(Test-Path -LiteralPath (Join-Path $p 'About\About.xml')){Package $p}})
if($dlcs.Count -ne 5){throw 'This benchmark requires all five official DLCs.'}
$mods=@('brrainz.harmony','ludeon.rimworld')+$dlcs+@('wildshift.mod','wildshift.tests.performance')
$li=($mods|ForEach-Object {'<li>'+[Security.SecurityElement]::Escape($_)+'</li>'})-join ''
('<ModsConfigData><version>'+[Security.SecurityElement]::Escape($version)+'</version><activeMods>'+$li+'</activeMods></ModsConfigData>')|Set-Content -LiteralPath (Join-Path $config 'ModsConfig.xml') -Encoding utf8
'<PrefsData><langFolderName>English</langFolderName><devMode>True</devMode><screenWidth>1280</screenWidth><screenHeight>720</screenHeight><fullscreen>False</fullscreen><runInBackground>True</runInBackground><autosaveIntervalDays>1000</autosaveIntervalDays></PrefsData>'|Set-Content -LiteralPath (Join-Path $config 'Prefs.xml') -Encoding utf8
$baselineHash=''
if($Phase -eq 'measure'){
    $BaselineSave=[IO.Path]::GetFullPath($BaselineSave)
    $baselineHash=Hash $BaselineSave
    $null=New-Item -ItemType Directory -Path (Join-Path $profile 'Saves')
    Copy-Item -LiteralPath $BaselineSave -Destination (Join-Path $profile 'Saves\autostart.rws')
}
$inputs=[Xml.XmlDocument]::new();$root=$inputs.CreateElement('PerformanceInputs');$null=$inputs.AppendChild($root)
$values=[ordered]@{Phase=$Phase;ActiveForms=$ActiveForms;TotalPawns=$TotalPawns;WarmupSeconds=$WarmupSeconds;MeasureSeconds=$MeasureSeconds;SampleSeconds=$SampleSeconds;FeaturePreset=$FeaturePreset;Smoke=$Smoke.IsPresent.ToString().ToLowerInvariant();GraphicsMode=$GraphicsMode;TargetFPS=60;GameVersion=($version -split '\s+')[0];SaveDataPath=$profile;GameAssemblyPath=$gameAssembly;GameAssemblySHA256=(Hash $gameAssembly);WildShiftPath=$WildShiftPath;WildShiftSHA256=(Hash $WildShiftPath);FixtureSHA256=(Hash $fixture);BaselineSHA256=$baselineHash}
foreach($key in $values.Keys){$node=$inputs.CreateElement($key);$node.InnerText=[string]$values[$key];$null=$root.AppendChild($node)}
$node=$inputs.CreateElement('Mods');foreach($id in $mods){$child=$inputs.CreateElement('li');$child.InnerText=$id;$null=$node.AppendChild($child)};$null=$root.AppendChild($node)
$inputs.Save((Join-Path $profile 'performance-inputs.xml'))
$normal=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config'
$before=@(Inventory $normal)
ConvertTo-Json -InputObject $before -Depth 4|Set-Content -LiteralPath (Join-Path $Output 'normal-config-before.json')
$inputFiles=@($exe,$gameAssembly,$WildShiftPath,$fixture,$PSCommandPath)|ForEach-Object {[pscustomobject]@{Path=$_;SHA256=(Hash $_)}}
$result=[ordered]@{Passed=$false;StatePassed=$false;NativeErrorFree=$false;Phase=$Phase;GameVersion=$version;ActiveForms=$ActiveForms;TotalPawns=$TotalPawns;FeaturePreset=$FeaturePreset;GraphicsMode=$GraphicsMode;TargetFPS=60;Smoke=$Smoke.IsPresent;WarmupSeconds=$WarmupSeconds;MeasureSeconds=$MeasureSeconds;RequestedMods=$mods;InputFiles=@($inputFiles);BaselineSave=$BaselineSave;BaselineSHA256=$baselineHash;BaselineUnchanged=$true;NormalConfigUnchanged=$false;NativeErrors=@();Errors=@();NativeResult=$null;Scope='Controlled stationary/drafted population in an actual initialized game, native 60 FPS target. No normal-colony-AI or full mod-pack claim; hidden native windows do not establish visible rendering performance.'}
$stdout=Join-Path $Output 'stdout.log';$stderr=Join-Path $Output 'stderr.log';$unity=Join-Path $Output 'unity.log'
$arguments=@('-NoCrashDialog','-NoFileLog',('-savedatafolder="'+$profile+'"'))
if($minor -eq '1.7'){
    if($GraphicsMode -eq 'Headless'){$arguments+='-Headless'}
}else{
    if($GraphicsMode -eq 'Headless'){$arguments+='-batchmode'}
    $arguments+=@('-force-d3d11','-screen-fullscreen','0','-logFile',$unity)
}
if($Phase -eq 'seed'){$arguments+='-quicktest'}
$result.Arguments=$arguments
$process=$null
try{
    if((Games).Count){throw 'Another game started; refusing launch.'}
    $process=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $GameDir -Environment @{SteamAppId='294100';SteamGameId='294100'} -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $process.Id|Set-Content -LiteralPath (Join-Path $Output 'pid.txt')
    $timer=[Diagnostics.Stopwatch]::StartNew()
    $limit=if($Phase -eq 'seed'){900}else{$WarmupSeconds+$MeasureSeconds+900}
    while(!$process.WaitForExit(1000)){
        if($timer.Elapsed.TotalSeconds -gt $limit){throw 'Owned benchmark process timed out.'}
    }
    $process.WaitForExit();$process.Refresh()
    if($process.ExitCode -ne 0){throw ('Owned benchmark exited '+$process.ExitCode)}
    $name=if($Phase -eq 'seed'){'seed-result.json'}else{'result.json'}
    $path=Join-Path $profile ('PerformanceResults\'+$name)
    if(!(Test-Path -LiteralPath $path)){throw 'No native performance result.'}
    $native=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
    $result.NativeResult=$native
    if(!$native.Passed){throw 'Native benchmark state/measurement guard failed.'}
    if($null -ne $native.NativeErrors -and @($native.NativeErrors).Count){throw 'Native benchmark retained in-game errors.'}
    if($Phase -eq 'seed' -and !(Test-Path -LiteralPath (Join-Path $profile 'Saves\autostart.rws'))){throw 'Native baseline save missing.'}
}catch{$result.Errors+=@($_.Exception.Message)}
finally{
    if($process){if(!$process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
    $after=@(Inventory $normal)
    ConvertTo-Json -InputObject $after -Depth 4|Set-Content -LiteralPath (Join-Path $Output 'normal-config-after.json')
    $result.NormalConfigUnchanged=(@(Compare-Object @($before|ForEach-Object {$_.Path+'|'+$_.SHA256}) @($after|ForEach-Object {$_.Path+'|'+$_.SHA256})).Count -eq 0)
    if(!$result.NormalConfigUnchanged){$result.Errors+=@('Normal user configuration changed.')}
    foreach($inputFile in $inputFiles){if((Hash $inputFile.Path) -ne $inputFile.SHA256){$result.Errors+=@('Runtime input changed: '+$inputFile.Path)}}
    if($baselineHash){$result.BaselineUnchanged=((Hash $BaselineSave) -eq $baselineHash);if(!$result.BaselineUnchanged){$result.Errors+=@('Copied source baseline changed.')}}
    $result.StatePassed=($null -ne $result.NativeResult -and $result.NativeResult.Passed -and $result.Errors.Count -eq 0)
    foreach($log in @($stdout,$stderr,$unity)){if(Test-Path -LiteralPath $log){$result.NativeErrors+=@(Select-String -LiteralPath $log -Pattern '\bERROR\b|^Error:|Fatal error|Internal CLR error|Unhandled exception|Crash!!!'|ForEach-Object Line)}}
    $result.NativeErrorFree=($result.NativeErrors.Count -eq 0)
    if($result.NativeErrors.Count){$result.Errors+=@('Native error/fatal diagnostics; inspect raw logs.')}
    $result.Passed=($result.Errors.Count -eq 0)
    $result|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $Output 'result.json') -Encoding utf8
}
$result|ConvertTo-Json -Depth 12
if(!$result.Passed){throw ('Performance runner failed; inspect '+(Join-Path $Output 'result.json'))}
