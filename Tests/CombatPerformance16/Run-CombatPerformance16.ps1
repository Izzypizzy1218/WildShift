<# TEST ONLY. Actual Unity/Mono 1.6 battle variant of the shared combat fixture.
Generates adapted sources only into external output. Never loads a 1.7 save,
installs mods or replaces normal runtime packages. Batch still initializes D3D11;
-Headless/-nographics are not used because native 1.6 needs its graphics device. #>
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$GameDir,
 [Parameter(Mandatory)][string]$Output,
 [ValidateSet('Human','Transformed','Animal')][string]$ActorKind='Transformed',
 [ValidateSet('rifle','jump','unarmed')][string]$Loadout='jump',
 [ValidateSet(0,20,60,120,240,400)][int]$Enemies=60,
 [ValidateSet(1,5)][int]$Actors=1,
 [ValidateRange(0,60)][double]$WarmupSeconds=2,
 [ValidateRange(2,120)][double]$MeasureSeconds=8,
 [switch]$BlockedLOS,
 [switch]$Diagnostic,
 [switch]$RichGenes,
 [switch]$EnemyAssault,
 [ValidateSet('unarmed','rifle')][string]$EnemyLoadout='unarmed',
 [ValidateSet('basic','FullNative')][string]$GearPreset='basic',
 [ValidateSet('Native','Batch')][string]$GraphicsMode='Batch',
 [switch]$SelectActors,
 [switch]$BuildOnly,
 [switch]$SkipBuild,
 [string]$FixtureDir='',
 [string]$WildShiftDir='',
 [string]$WildShiftPath='',
 [string]$HarmonyPath='',
 [string]$DotNet='',
 [string]$RimWorldManaged=''
)
$ErrorActionPreference='Stop'
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Package([string]$path){([xml](Get-Content -LiteralPath (Join-Path $path 'About\About.xml') -Raw)).ModMetaData.packageId.ToLowerInvariant()}
function Games(){@(Get-Process -Name RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue)}
function Inventory([string]$path){@(if(Test-Path -LiteralPath $path){Get-ChildItem -LiteralPath $path -File -Recurse | ForEach-Object {[pscustomobject]@{Path=$_.FullName;SHA256=(Hash $_.FullName)}}})}
$repoRoot=[IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)).TrimEnd('\','/')
$workspace=Split-Path $repoRoot -Parent
$Output=[IO.Path]::GetFullPath($Output).TrimEnd('\','/')
if($Output -eq $repoRoot -or $Output.StartsWith($repoRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Output must be outside source repository.'}
if(Test-Path -LiteralPath $Output){throw 'Fresh external output required; previous evidence is preserved.'}
$null=New-Item -ItemType Directory -Path $Output
if($BuildOnly -and $SkipBuild){throw 'BuildOnly and SkipBuild are mutually exclusive.'}
if(!$DotNet){$DotNet=Join-Path $workspace '.tools\dotnet10\dotnet.exe'}
if(!$WildShiftDir){$WildShiftDir=Join-Path $GameDir 'Mods\WildShift'}
if(!$WildShiftPath){$WildShiftPath=Join-Path $WildShiftDir '1.6\Assemblies\WildShift.dll'}
if(!$HarmonyPath){$HarmonyPath='C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.6\Assemblies\0Harmony.dll'}
if(!$RimWorldManaged){$RimWorldManaged=Join-Path $GameDir 'RimWorldWin64_Data\Managed'}
$gameAssembly=Join-Path $RimWorldManaged 'Assembly-CSharp.dll'
$exe=Join-Path $GameDir 'RimWorldWin64.exe'
$version=(Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -Raw).Trim()
if($version -notmatch '^1\.6\.'){throw 'This fixture requires the actual Unity 1.6 engine.'}
foreach($file in @($DotNet,$gameAssembly,$exe,$WildShiftPath,$HarmonyPath)){if(!(Test-Path -LiteralPath $file -PathType Leaf)){throw "Missing input: $file"}}
if(!$SkipBuild){
 $built=Join-Path $Output 'fixture-mod\WildShiftCombatPerformance16'
 $null=New-Item -ItemType Directory -Path (Join-Path $built 'About') -Force
 $obj=(Join-Path $Output 'obj')+'\'
 $shared=Join-Path (Split-Path $PSScriptRoot -Parent) 'CombatPerformance'
 $generated=Join-Path $Output 'compat-sources'
 $null=New-Item -ItemType Directory -Path $generated
 $sourceInputs=@()
 foreach($name in @('CombatProbe','FrameProbe','ForceSpeedObserver','SharedRefreshProbe','StressLoadout')){
  $path=Join-Path $shared ($name+'.cs');$originalHash=Hash $path
  $text=[IO.File]::ReadAllText($path)
  $text=$text.Replace('using System.Text.Json;','using WildShiftCombatPerformance.Json;')
  $text=$text.Replace('GC.GetAllocatedBytesForCurrentThread()','Allocation16.Current()')
  $text=$text.Replace('GC.GetTotalAllocatedBytes(false)','Allocation16.Total()')
  if($name -eq 'CombatProbe'){
   $text=$text.Replace('private long allocatedAtStart;','private long? allocatedAtStart;')
   $text=$text.Replace('map.TileInfo.PrimaryBiome.animalDensity','map.Biome.animalDensity')
   $text=$text.Replace('"wildshift.tests.combatperformance"','"wildshift.tests.combatperformance16"')
   $anchor='Directory.CreateDirectory(Output);'
   if(([regex]::Matches($text,[regex]::Escape($anchor))).Count -ne 1){throw 'Shared boot changed: review 1.6 validation adaptation.'}
   $text=$text.Replace($anchor,$anchor+"`n            Allocation16.Validate();")
   $anchor='Passed=passed,Exception=error?.ToString(),GameVersion='
   if(([regex]::Matches($text,[regex]::Escape($anchor))).Count -ne 1){throw 'Shared result changed: review 1.6 metadata adaptation.'}
   $text=$text.Replace($anchor,'Passed=passed,Exception=error?.ToString(),CurrentThreadAllocationSupported=Allocation16.CurrentSupported,ProcessTotalAllocationSupported=Allocation16.TotalSupported,UnityVersion=Application.unityVersion,UnityBatchMode=Application.isBatchMode,ManagedRuntime=Environment.Version.ToString(),GameVersion=')
  }
  $destination=Join-Path $generated ($name+'16.cs')
  [IO.File]::WriteAllText($destination,$text,[Text.UTF8Encoding]::new($false))
  if((Hash $path) -ne $originalHash){throw ('Shared source changed while snapshotting: '+$name)}
  $sourceInputs+=[pscustomobject]@{Source=$path;SHA256=$originalHash;Generated=$destination;GeneratedSHA256=(Hash $destination)}
 }
 & $DotNet build (Join-Path $PSScriptRoot 'WildShift.CombatPerformance16.csproj') -c Release -o (Join-Path $built 'Assemblies') "-p:BaseIntermediateOutputPath=$obj" "-p:RimWorldManaged=$RimWorldManaged" "-p:GeneratedSourceDir=$generated" "-p:HarmonyPath=$HarmonyPath" "-p:WildShiftPath=$WildShiftPath" --nologo -v:minimal
 if($LASTEXITCODE){throw 'Combat fixture build failed.'}
 Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'About.xml') -Destination (Join-Path $built 'About\About.xml')
 $record=[pscustomobject]@{FixtureDir=$built;FixtureSHA256=(Hash (Join-Path $built 'Assemblies\WildShiftCombatPerformance16.dll'));ProductionSHA256=(Hash $WildShiftPath);GameVersion=$version;GameAssemblySHA256=(Hash $gameAssembly);GeneratedSources=$sourceInputs;Sources=@(Get-ChildItem -LiteralPath $PSScriptRoot -File | ForEach-Object {[pscustomobject]@{Path=$_.FullName;SHA256=(Hash $_.FullName)}});TestOnly=$true;Scope='Shared native combat fixture adapted only for Mono JSON, allocation API availability, 1.6 biome access and native graphics/bootstrap. No live 1.7 save is loaded.'}
 $record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'fixture-build.json')
 if($BuildOnly){$record | ConvertTo-Json -Depth 5;return}
 if(!$FixtureDir){throw 'Build complete; stage this test-only helper separately, then run SkipBuild.'}
 if((Hash (Join-Path $FixtureDir 'Assemblies\WildShiftCombatPerformance16.dll')) -ne $record.FixtureSHA256){throw 'Staged helper differs from just-built fixture.'}
}
if((Games).Count){throw 'Another RimWorld is running; refusing concurrent launch.'}
if(!$FixtureDir -or (Package $FixtureDir) -ne 'wildshift.tests.combatperformance16' -or (Package $WildShiftDir) -ne 'wildshift.mod'){throw 'Unexpected test fixture or production package.'}
$fixture=Join-Path $FixtureDir 'Assemblies\WildShiftCombatPerformance16.dll'
$profile=Join-Path $Output 'profile'
$config=Join-Path $profile 'Config'
$null=New-Item -ItemType Directory -Path $config -Force
$dlcs=@(foreach($name in @('Royalty','Ideology','Biotech','Anomaly','Odyssey')){$path=Join-Path $GameDir ('Data\'+$name);if(Test-Path -LiteralPath (Join-Path $path 'About\About.xml')){Package $path}})
if($dlcs.Count -ne 5){throw 'All five official DLCs required.'}
$mods=@('brrainz.harmony','ludeon.rimworld')+$dlcs+@('wildshift.mod','wildshift.tests.combatperformance16')
$li=($mods | ForEach-Object {'<li>'+[Security.SecurityElement]::Escape($_)+'</li>'}) -join ''
('<ModsConfigData><version>'+[Security.SecurityElement]::Escape($version)+'</version><activeMods>'+$li+'</activeMods></ModsConfigData>') | Set-Content -LiteralPath (Join-Path $config 'ModsConfig.xml') -Encoding utf8
'<PrefsData><langFolderName>English</langFolderName><devMode>True</devMode><screenWidth>1280</screenWidth><screenHeight>720</screenHeight><fullscreen>False</fullscreen><runInBackground>True</runInBackground><autosaveIntervalDays>1000</autosaveIntervalDays></PrefsData>' | Set-Content -LiteralPath (Join-Path $config 'Prefs.xml') -Encoding utf8
$inputXml=[Xml.XmlDocument]::new();$root=$inputXml.CreateElement('CombatInputs');$null=$inputXml.AppendChild($root)
$values=[ordered]@{ActorKind=$ActorKind;Loadout=$Loadout;Enemies=$Enemies;Actors=$Actors;ArenaSize=$(if($Enemies -gt 120){95}else{75});WarmupSeconds=$WarmupSeconds;MeasureSeconds=$MeasureSeconds;BlockedLOS=$BlockedLOS.IsPresent.ToString().ToLowerInvariant();Diagnostic=$Diagnostic.IsPresent.ToString().ToLowerInvariant();RichGenes=$RichGenes.IsPresent.ToString().ToLowerInvariant();EnemyAssault=$EnemyAssault.IsPresent.ToString().ToLowerInvariant();EnemyLoadout=$EnemyLoadout;GearPreset=$GearPreset;GraphicsMode=$GraphicsMode;SelectActors=$SelectActors.IsPresent.ToString().ToLowerInvariant();GameAssemblySHA256=(Hash $gameAssembly);WildShiftSHA256=(Hash $WildShiftPath);FixtureSHA256=(Hash $fixture)}
foreach($key in $values.Keys){$node=$inputXml.CreateElement($key);$node.InnerText=[string]$values[$key];$null=$root.AppendChild($node)}
$node=$inputXml.CreateElement('Mods');foreach($id in $mods){$child=$inputXml.CreateElement('li');$child.InnerText=$id;$null=$node.AppendChild($child)};$null=$root.AppendChild($node)
$inputXml.Save((Join-Path $profile 'combat-inputs.xml'))
$normal=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config'
$normalRoot=Split-Path $normal -Parent
function NormalProfileInventory(){@(Inventory $normal;Inventory (Join-Path $normalRoot 'Saves'))}
$before=NormalProfileInventory
ConvertTo-Json -InputObject $before -Depth 3 | Set-Content -LiteralPath (Join-Path $Output 'normal-config-before.json')
if(Test-Path -LiteralPath (Join-Path $normal 'Prefs.xml')){Copy-Item -LiteralPath (Join-Path $normal 'Prefs.xml') -Destination (Join-Path $Output 'normal-Prefs-before.xml')}
foreach($name in @('Player.log','Player-prev.log')){
 $legacy=Join-Path $normalRoot $name
 if(Test-Path -LiteralPath $legacy -PathType Leaf){Copy-Item -LiteralPath $legacy -Destination (Join-Path $Output ('legacy-'+$name))}
}
$inputFiles=@($exe,$gameAssembly,$WildShiftPath,$fixture) | ForEach-Object {[pscustomobject]@{Path=$_;SHA256=(Hash $_)}}
$result=[ordered]@{Passed=$false;GameVersion=$version;ActorKind=$ActorKind;Loadout=$Loadout;Enemies=$Enemies;Actors=$Actors;BlockedLOS=$BlockedLOS.IsPresent;Diagnostic=$Diagnostic.IsPresent;RichGenes=$RichGenes.IsPresent;EnemyAssault=$EnemyAssault.IsPresent;EnemyLoadout=$EnemyLoadout;GearPreset=$GearPreset;GraphicsMode=$GraphicsMode;SelectActors=$SelectActors.IsPresent;RequestedMods=$mods;InputFiles=$inputFiles;NormalConfigUnchanged=$false;Errors=@();NativeErrors=@();NativeResult=$null}
$stdout=Join-Path $Output 'stdout.log';$stderr=Join-Path $Output 'stderr.log'
$unityLog=Join-Path $Output 'unity.log'
$arguments=@('-force-d3d11','-screen-fullscreen','0','-logFile',('"'+$unityLog+'"'),'-quicktest',('-savedatafolder="'+$profile+'"'))
if($GraphicsMode -eq 'Batch'){$arguments+='-batchmode'}
$process=$null
try {
 if((Games).Count){throw 'Another game started; refusing launch.'}
 $process=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $GameDir -Environment @{SteamAppId='294100';SteamGameId='294100'} -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
 $process.Id | Set-Content -LiteralPath (Join-Path $Output 'pid.txt')
 $timer=[Diagnostics.Stopwatch]::StartNew()
 while(!$process.WaitForExit(1000)){if($timer.Elapsed.TotalSeconds -gt (300+$WarmupSeconds+$MeasureSeconds)){throw 'Owned combat probe timed out.'}}
 $process.WaitForExit();$process.Refresh()
 if($process.ExitCode -ne 0){throw ('Owned game exited '+$process.ExitCode)}
 $nativePath=Join-Path $profile 'CombatResults\result.json'
 if(!(Test-Path -LiteralPath $nativePath)){throw 'No in-process combat measurement result.'}
 $native=Get-Content -LiteralPath $nativePath -Raw | ConvertFrom-Json
 $result.NativeResult=$native
 if(!$native.Passed){throw 'Native combat scenario failed; inspect exception/state/activity.'}
 if($native.WildShiftSHA256 -ne (Hash $WildShiftPath) -or $native.FixtureSHA256 -ne (Hash $fixture)){throw 'Actual loaded combat inputs differ.'}
} catch {$result.Errors+=@($_.Exception.Message)}
finally {
 if($process){if(!$process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
 $after=NormalProfileInventory
 ConvertTo-Json -InputObject $after -Depth 3 | Set-Content -LiteralPath (Join-Path $Output 'normal-config-after.json')
 $result.NormalConfigUnchanged=(@(Compare-Object @($before | ForEach-Object {$_.Path+'|'+$_.SHA256}) @($after | ForEach-Object {$_.Path+'|'+$_.SHA256})).Count -eq 0)
 if(!$result.NormalConfigUnchanged){$result.Errors+=@('Normal user configuration changed.')}
 foreach($file in $inputFiles){if((Hash $file.Path) -ne $file.SHA256){$result.Errors+=@('Runtime input changed: '+$file.Path)}}
 foreach($path in @($stdout,$stderr,$unityLog)){if(Test-Path -LiteralPath $path){$result.NativeErrors+=@(Select-String -LiteralPath $path -Pattern 'Fatal error|Unhandled exception|Crash!!!|Critical error in root' | ForEach-Object Line)}}
 if($result.NativeErrors.Count){$result.Errors+=@('Native error diagnostics; inspect raw logs.')}
 $result.Passed=($result.Errors.Count -eq 0)
 $result | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $Output 'result.json') -Encoding utf8
}
$result | ConvertTo-Json -Depth 12
if(!$result.Passed){throw ('Combat performance probe failed: '+(Join-Path $Output 'result.json'))}
