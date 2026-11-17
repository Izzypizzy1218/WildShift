<# TEST ONLY. Loads an unchanged copy of the actual colony through native
autostart (never quicktest). The copied full mod profile remains loaded, with
network-generating features explicitly disabled in that private copy. Credentials
are never printed. Requires an already discoverable, separately staged helper.
No runtime packages, normal configuration or source saves are replaced. #>
[CmdletBinding()]
param(
 [Parameter(Mandatory)][string]$GameDir,
 [Parameter(Mandatory)][string]$FixtureDir,
 [Parameter(Mandatory)][string]$WildShiftDir,
 [Parameter(Mandatory)][string]$Output,
 [string]$SavePath='',
 [string]$UserData=(Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'),
 [string]$SourceConfigDir='',
 [string]$LiveActorId='Human957337',
 [ValidateSet('Human','Transformed')][string]$ActorKind='Transformed',
 [string]$FormKind='Warg',
 [bool]$Drafted=$true,
 [ValidateRange(0,60)][int]$Enemies=60,
 [ValidateRange(0,60)][double]$WarmupSeconds=1,
 [ValidateRange(2,120)][double]$MeasureSeconds=8,
 [ValidateSet('Native','Headless')][string]$GraphicsMode='Native',
 [switch]$SelectActors,
 [switch]$Diagnostic,
 [switch]$SelectionUiDiagnostics,
 [switch]$SelectionRunning,
 [switch]$SelectionRunningOnly,
 [switch]$SelectionNativeSerums,
 [switch]$SelectionGenerationDiagnostics,
 [ValidateSet('None','Health')][string]$SelectionInspectTab='Health',
 [switch]$SelectionSyntheticHealthHover,
 [ValidateRange(0.1,10)][double]$SelectionSeconds=2,
 [ValidateRange(0.1,2)][double]$SelectionRunningSeconds=0.5,
 [switch]$AwaitManagedSampler,
 [ValidateRange(0,3600)][int]$LivePrewarmTicks=120,
 [ValidateRange(0,3600)][int]$LiveEnemyPrewarmTicks=30,
 [ValidateRange(0,3600)][int]$LiveArmedPrewarmTicks=15,
 [ValidateRange(60,1800)][int]$TimeoutSeconds=900,
 [switch]$PrepareOnly
)
$ErrorActionPreference='Stop'
function Hash([string]$path){(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
function Package([string]$path){([xml](Get-Content -LiteralPath (Join-Path $path 'About\About.xml') -Raw)).ModMetaData.packageId.ToLowerInvariant()}
function Games(){@(Get-Process -Name RimWorld,RimWorldWin64 -ErrorAction SilentlyContinue)}
function Inventory([string]$path){@(if(Test-Path -LiteralPath $path){Get-ChildItem -LiteralPath $path -File -Recurse | Sort-Object FullName | ForEach-Object {[pscustomobject]@{Path=$_.FullName;SHA256=(Hash $_.FullName)}}})}
function NormalInventory(){@(foreach($section in @('Config','Saves','HugsLib','CameraPlus')){Inventory (Join-Path $UserData $section)})}
function SetPrivateSetting([string]$file,[string]$key,[string]$value,[bool]$optional=$false){
 $path=Join-Path $config $file
 if(!(Test-Path -LiteralPath $path)){if($optional){return};throw ('Expected copied settings are absent: '+$file)}
 [xml]$xml=Get-Content -LiteralPath $path -Raw
 $settings=$xml.SelectSingleNode('/SettingsBlock/ModSettings')
 if(!$settings){throw ('Unexpected settings structure: '+$file)}
 $node=$settings.SelectSingleNode($key)
 if(!$node){$node=$xml.CreateElement($key);$null=$settings.AppendChild($node)}
 $node.InnerText=$value;$xml.Save($path)
}
$repo=[IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)).TrimEnd('\','/')
$Output=[IO.Path]::GetFullPath($Output).TrimEnd('\','/')
if($Output -eq $repo -or $Output.StartsWith($repo+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fresh external output is required.'}
if(Test-Path -LiteralPath $Output){throw 'Output already exists; preserve earlier evidence.'}
if((Games).Count){throw 'Another RimWorld is running; refusing concurrent preparation/launch.'}
if(!$SourceConfigDir){$SourceConfigDir=Join-Path $UserData 'Config'}
if(!$SavePath){$SavePath=(Get-ChildItem -LiteralPath (Join-Path $UserData 'Saves') -Filter '*.rws' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).FullName}
$SavePath=[IO.Path]::GetFullPath($SavePath)
$version=(Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -Raw).Trim()
if($version -notmatch '^1\.7\.'){throw 'Live combat fixture currently targets actual 1.7 only.'}
if((Package $FixtureDir) -ne 'wildshift.tests.combatperformance' -or (Package $WildShiftDir) -ne 'wildshift.mod'){throw 'Unexpected staged package identities.'}
$exe=Join-Path $GameDir 'RimWorld.exe';$gameAssembly=Join-Path $GameDir 'Assembly-CSharp.dll'
$production=Join-Path $WildShiftDir '1.7\Assemblies\WildShift.dll';$fixture=Join-Path $FixtureDir 'Assemblies\WildShiftCombatPerformance.dll'
foreach($path in @($SavePath,$exe,$gameAssembly,$production,$fixture,(Join-Path $SourceConfigDir 'ModsConfig.xml'))){if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw ('Missing input: '+$path)}}
$sourceHash=Hash $SavePath
[xml]$source=Get-Content -LiteralPath $SavePath -Raw
if([string]$source.savegame.meta.gameVersion -notmatch '^1\.7\.'){throw 'Use an actual 1.7 colony save.'}
[xml]$modsConfig=Get-Content -LiteralPath (Join-Path $SourceConfigDir 'ModsConfig.xml') -Raw
$mods=@($modsConfig.ModsConfigData.activeMods.li | ForEach-Object {([string]$_).ToLowerInvariant()})
$saveMods=@($source.savegame.meta.modIds.li | ForEach-Object {([string]$_).ToLowerInvariant()})
if(($mods -join '|') -ne ($saveMods -join '|')){throw 'Current active profile differs from saved profile; explicitly select matching copied config.'}
if('wildshift.mod' -notin $mods -or 'brrainz.harmony' -notin $mods){throw 'Actual profile lacks WildShift/Harmony.'}
if(@($mods | Where-Object {$_ -like 'wildshift.tests.*'}).Count){throw 'Choose a real source profile without a test helper.'}
$originalMods=@($mods);$mods+=@('wildshift.tests.combatperformance')
$before=NormalInventory
$null=New-Item -ItemType Directory -Path $Output
ConvertTo-Json -InputObject $before -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'normal-profile-before.json')
$profile=Join-Path $Output 'profile';$config=Join-Path $profile 'Config'
$null=New-Item -ItemType Directory -Path $profile,(Join-Path $profile 'Saves')
Copy-Item -LiteralPath $SourceConfigDir -Destination $config -Recurse
$auxiliaryDirectories=@()
foreach($name in @('HugsLib','CameraPlus')){
 $sourceAuxiliary=Join-Path $UserData $name
 if(Test-Path -LiteralPath $sourceAuxiliary -PathType Container){
  Copy-Item -LiteralPath $sourceAuxiliary -Destination (Join-Path $profile $name) -Recurse
  $auxiliaryDirectories+=$name
 }
}
Copy-Item -LiteralPath $SavePath -Destination (Join-Path $Output 'source-copy.rws')
Copy-Item -LiteralPath $SavePath -Destination (Join-Path $profile 'Saves\autostart.rws')
if((Hash (Join-Path $profile 'Saves\autostart.rws')) -ne $sourceHash){throw 'Source-save copy hash differs.'}
$li=$modsConfig.CreateElement('li');$li.InnerText='wildshift.tests.combatperformance';$null=$modsConfig.ModsConfigData.activeMods.AppendChild($li)
$modsConfig.Save((Join-Path $config 'ModsConfig.xml'))
# Preserve all gameplay mods and settings except these explicitly recorded
# private network gates. Their CPU work is also gated; this is a limitation.
if('cj.rimtalk' -in $mods){SetPrivateSetting 'Mod_3551203752_Settings.xml' 'isEnabled' 'False'}
if('rp.rimtalk.personadirector' -in $mods){foreach($key in @('experimentalFeaturesEnabled','autoGenEnabled','globalAutoEvolveEnabled')){SetPrivateSetting 'Mod_3619548407_DirectorMod.xml' $key 'False'}}
if('seohyeon.autotranslation' -in $mods){foreach($key in @('AutoTranslation_EnableAutoTranslation','AutoTranslation_AutoTranslateNewMods')){SetPrivateSetting 'Mod_3278005460_AutoTranslation.xml' $key 'False'}}
[xml]$prefs=Get-Content -LiteralPath (Join-Path $config 'Prefs.xml') -Raw
foreach($pair in @(@('devMode','True'),@('fullscreen','False'),@('runInBackground','True'),@('autosaveIntervalDays','1000'),@('screenWidth','1280'),@('screenHeight','720'))){
 $node=$prefs.DocumentElement.SelectSingleNode($pair[0]);if(!$node){$node=$prefs.CreateElement($pair[0]);$null=$prefs.DocumentElement.AppendChild($node)};$node.InnerText=$pair[1]
}
$prefs.Save((Join-Path $config 'Prefs.xml'))
$inputXml=[Xml.XmlDocument]::new();$root=$inputXml.CreateElement('CombatInputs');$null=$inputXml.AppendChild($root)
$values=[ordered]@{LiveActorId=$LiveActorId;FormKind=$FormKind;Drafted=$Drafted.ToString().ToLowerInvariant();ActorKind=$ActorKind;Loadout='preserved';Enemies=$Enemies;Actors=1;WarmupSeconds=$WarmupSeconds;MeasureSeconds=$MeasureSeconds;BlockedLOS='false';Diagnostic=$Diagnostic.IsPresent.ToString().ToLowerInvariant();RichGenes='false';EnemyAssault='true';EnemyLoadout='rifle';GearPreset='ExistingSave';GraphicsMode=$GraphicsMode;SelectActors=$SelectActors.IsPresent.ToString().ToLowerInvariant();SourceSaveSHA256=$sourceHash;GameAssemblySHA256=(Hash $gameAssembly);WildShiftSHA256=(Hash $production);FixtureSHA256=(Hash $fixture)}
$values['LivePrewarmTicks']=$LivePrewarmTicks
$values['LiveEnemyPrewarmTicks']=$LiveEnemyPrewarmTicks
$values['LiveArmedPrewarmTicks']=$LiveArmedPrewarmTicks
$values['LiveSelectionSequence']='true'
$values['SelectionUiDiagnostics']=$SelectionUiDiagnostics.IsPresent.ToString().ToLowerInvariant()
$values['SelectionRunning']=$SelectionRunning.IsPresent.ToString().ToLowerInvariant()
$values['SelectionRunningOnly']=$SelectionRunningOnly.IsPresent.ToString().ToLowerInvariant();$values['SelectionNativeSerums']=$SelectionNativeSerums.IsPresent.ToString().ToLowerInvariant();$values['SelectionGenerationDiagnostics']=$SelectionGenerationDiagnostics.IsPresent.ToString().ToLowerInvariant()
$values['SelectionInspectTab']=$SelectionInspectTab;$values['SelectionSyntheticHealthHover']=$SelectionSyntheticHealthHover.IsPresent.ToString().ToLowerInvariant()
if($SelectionRunningOnly -and !$PSBoundParameters.ContainsKey('SelectionRunningSeconds')){$SelectionRunningSeconds=1}
$values['SelectionSeconds']=$SelectionSeconds;$values['SelectionRunningSeconds']=$SelectionRunningSeconds
$managedSamplerReadyFile=if($AwaitManagedSampler){Join-Path $Output 'managed-sampler-ready.txt'}else{$null}
if($AwaitManagedSampler){$values['ManagedSamplerReadyFile']=$managedSamplerReadyFile}
foreach($key in $values.Keys){$node=$inputXml.CreateElement($key);$node.InnerText=[Convert]::ToString($values[$key],[Globalization.CultureInfo]::InvariantCulture);$null=$root.AppendChild($node)}
$node=$inputXml.CreateElement('Mods');foreach($id in $mods){$child=$inputXml.CreateElement('li');$child.InnerText=$id;$null=$node.AppendChild($child)};$null=$root.AppendChild($node)
$inputXml.Save((Join-Path $profile 'combat-inputs.xml'))
$inputFiles=@($exe,$gameAssembly,$production,$fixture,$SavePath,$PSCommandPath) | ForEach-Object {[pscustomobject]@{Path=$_;SHA256=(Hash $_)}}
$arguments=@('-NoCrashDialog','-NoFileLog',('-savedatafolder="'+$profile+'"'))
if($GraphicsMode -eq 'Headless'){$arguments+='-Headless'}
$result=[ordered]@{Passed=$false;Prepared=$true;GameVersion=$version;SourceSave=$SavePath;SourceSaveSHA256=$sourceHash;LiveActorId=$LiveActorId;ActorKind=$ActorKind;FormKind=$FormKind;Drafted=$Drafted;Enemies=$Enemies;GraphicsMode=$GraphicsMode;SelectActors=$SelectActors.IsPresent;OriginalMods=$originalMods;OriginalModCount=$originalMods.Count;RequestedMods=$mods;CopiedAuxiliaryProfileDirectories=$auxiliaryDirectories;InputFiles=$inputFiles;Process=$null;NormalProfileUnchanged=$false;SourceSaveUnchanged=$false;NativeResult=$null;Errors=@();NetworkGates='PRIVATE COPY ONLY: RimTalk global disabled; RPD experimental/autogen/autoevolve disabled; AutoTranslation enabled/newmods disabled. Their usual CPU work is also omitted; not an exact CPU replay of network-enabled profile.';Scope='Native autostart existing colony, all original mod IDs/order plus test helper; Config and optional HugsLib/CameraPlus profile settings copied; no quicktest. Real damage and casualties, no global colony/map cleanup, no invulnerability. Source files retained.'}
$result['AwaitManagedSampler']=$AwaitManagedSampler.IsPresent;$result['ManagedSamplerReadyFile']=$managedSamplerReadyFile
$result['LivePrewarmTicks']=$LivePrewarmTicks
$result['LiveEnemyPrewarmTicks']=$LiveEnemyPrewarmTicks;$result['AutoloadDevMode']=$true;$result['BenchmarkDevMode']=$false
$result['LiveArmedPrewarmTicks']=$LiveArmedPrewarmTicks
$result['LiveSelectionSequence']=$true;$result['SelectionUiDiagnostics']=$SelectionUiDiagnostics.IsPresent;$result['SelectionRunning']=$SelectionRunning.IsPresent
$result['SelectionRunningOnly']=$SelectionRunningOnly.IsPresent;$result['SelectionNativeSerums']=$SelectionNativeSerums.IsPresent;$result['SelectionGenerationDiagnostics']=$SelectionGenerationDiagnostics.IsPresent;$result['SelectionRunningSeconds']=$SelectionRunningSeconds
$prepared=[pscustomobject]@{Profile=$profile;Executable=$exe;WorkingDirectory=$GameDir;Arguments=$arguments;InputFiles=$inputFiles;RequestedMods=$mods;NetworkGates=$result.NetworkGates;AwaitManagedSampler=$AwaitManagedSampler.IsPresent;ManagedSamplerReadyFile=$managedSamplerReadyFile;LivePrewarmTicks=$LivePrewarmTicks;LiveEnemyPrewarmTicks=$LiveEnemyPrewarmTicks;LiveArmedPrewarmTicks=$LiveArmedPrewarmTicks;AutoloadDevMode=$true;BenchmarkDevMode=$false}
$prepared | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $Output 'prepared.json')
if($PrepareOnly){$prepared | ConvertTo-Json -Depth 5;return}
$stdout=Join-Path $Output 'stdout.log';$stderr=Join-Path $Output 'stderr.log';$process=$null
try{
 if((Games).Count){throw 'Another game started; refusing concurrent launch.'}
 $process=Start-Process -FilePath $exe -ArgumentList $arguments -WorkingDirectory $GameDir -Environment @{SteamAppId='294100';SteamGameId='294100'} -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
 $process.Id | Set-Content -LiteralPath (Join-Path $Output 'pid.txt')
 $result.Process=[pscustomobject]@{PID=$process.Id;Executable=$exe;SHA256=(Hash $exe);Arguments=$arguments}
 $timer=[Diagnostics.Stopwatch]::StartNew()
 while(!$process.WaitForExit(1000)){if($timer.Elapsed.TotalSeconds -gt $TimeoutSeconds){throw 'Owned live combat process timed out.'}}
 $process.WaitForExit();$process.Refresh()
 if($process.ExitCode -ne 0){throw ('Owned game exited '+$process.ExitCode)}
 $nativePath=Join-Path $profile 'CombatResults\result.json'
 if(!(Test-Path -LiteralPath $nativePath)){throw 'No in-process live combat result.'}
 $native=Get-Content -LiteralPath $nativePath -Raw | ConvertFrom-Json;$result.NativeResult=$native
 if(!$native.Passed){$result.Errors+=@('Native live scenario did not pass; inspect actor survival, population drift and existing-profile errors before comparing timings.')}
 if($native.WildShiftSHA256 -ne (Hash $production) -or $native.FixtureSHA256 -ne (Hash $fixture)){$result.Errors+=@('Actual loaded hashes differ.')}
 if(!(Test-Path -LiteralPath (Join-Path $profile 'CombatResults\live-preflight.json'))){$result.Errors+=@('Existing-save extension did not record its exact actor state.')}
}catch{$result.Errors+=@($_.Exception.Message)}
finally{
 if($process){if(!$process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}
 $after=NormalInventory
 ConvertTo-Json -InputObject $after -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'normal-profile-after.json')
 $result.NormalProfileUnchanged=(@(Compare-Object @($before | ForEach-Object {$_.Path+'|'+$_.SHA256}) @($after | ForEach-Object {$_.Path+'|'+$_.SHA256})).Count -eq 0)
 $result.SourceSaveUnchanged=((Hash $SavePath) -eq $sourceHash)
 if(!$result.NormalProfileUnchanged){$result.Errors+=@('Normal save/config hashes changed.')}
 foreach($file in $inputFiles){if((Hash $file.Path) -ne $file.SHA256){$result.Errors+=@('Runtime/source input changed: '+$file.Path)}}
 $result.Passed=($result.Errors.Count -eq 0)
 $result | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $Output 'result.json')
}
$result | ConvertTo-Json -Depth 15
if(!$result.Passed){throw ('Live combat probe did not pass: '+(Join-Path $Output 'result.json'))}
