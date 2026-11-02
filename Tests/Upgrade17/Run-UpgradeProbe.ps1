<#
Loads only an isolated COPY of an explicit existing save. Does not build or
stage mods, modify the original save, or install production files. The native
autostart loader opens profile/Saves/autostart.rws in two fresh processes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$SavePath,
    [Parameter(Mandatory)][string]$WildShiftDir,
    [Parameter(Mandatory)][string]$FixtureDir,
    [Parameter(Mandatory)][string]$ExpectedWildShiftSHA256,
    [Parameter(Mandatory)][string]$Output,
    [string]$GameExe='',
    [string]$CycleTargetId='',
    [string[]]$ExcludedMods=@(),
    [ValidateSet('Batch','GUI')][string]$GraphicsMode='Batch',
    [ValidateRange(60,1800)][int]$TimeoutSeconds=900,
    [switch]$AllowCrossVersion
)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)).TrimEnd('\','/')
$workspace=Split-Path $repoRoot -Parent
$Output=[IO.Path]::GetFullPath($Output).TrimEnd('\','/')
if ($Output.Equals($repoRoot,[StringComparison]::OrdinalIgnoreCase) -or $Output.StartsWith($repoRoot+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be outside the source repository.' }
if (Test-Path -LiteralPath $Output) { throw 'Use a new external output directory; previous evidence is immutable.' }
if (@(Get-CimInstance Win32_Process | Where-Object {$_.Name -match '^RimWorld.*\.exe$'}).Count) { throw 'Close running RimWorld processes before this probe.' }
function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function Package([string]$dir) { ([xml](Get-Content -LiteralPath (Join-Path $dir 'About\About.xml') -Raw)).ModMetaData.packageId.ToLowerInvariant() }
function Inventory([string]$dir) {
    if (Test-Path -LiteralPath $dir) { foreach($file in Get-ChildItem -LiteralPath $dir -Recurse -File | Sort-Object FullName) { [pscustomobject]@{Path=$file.FullName;SHA256=(Hash $file.FullName)} } }
}
$version=(Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -Raw).Trim()
if($version -notmatch '^1\.(6|7)\.') { throw 'Only actual 1.6 and 1.7 engines are supported.' }
$minor=$Matches[1]
$nativeVersion=($version -split '\s+')[0]
if(!$GameExe) { $GameExe=Join-Path $GameDir $(if($minor -eq '7'){'RimWorld.exe'}else{'RimWorldWin64.exe'}) }
$GameExe=[IO.Path]::GetFullPath($GameExe)
$SavePath=[IO.Path]::GetFullPath($SavePath)
$sourceHash=Hash $SavePath
[xml]$source=Get-Content -LiteralPath $SavePath -Raw
$saveVersion=[string]$source.savegame.meta.gameVersion
if (!$AllowCrossVersion -and $saveVersion -notmatch ('^1\.'+$minor+'\.')) { throw 'A different save minor version requires explicit AllowCrossVersion.' }
$mods=@($source.savegame.meta.modIds.li | ForEach-Object {([string]$_).ToLowerInvariant()})
if('wildshift.mod' -notin $mods -or 'brrainz.harmony' -notin $mods) { throw 'Source save does not contain WildShift and Harmony.' }
if('wildshift.tests.upgrade' -in $mods) { throw 'Source already contains this test fixture; choose an original old save.' }
$originalMods=@($mods)
$excluded=@($ExcludedMods | ForEach-Object { $_.Trim().ToLowerInvariant() })
if(@($excluded | Select-Object -Unique).Count -ne $excluded.Count){throw 'ExcludedMods contains duplicates.'}
foreach($id in $excluded){
    if($id -notin $originalMods){throw ('Excluded mod is not an original save ID: '+$id)}
    if($id -eq 'wildshift.mod' -or $id -eq 'brrainz.harmony' -or $id -match '^ludeon\.rimworld(?:\.|$)'){throw ('Cannot exclude essential game/probe dependency: '+$id)}
}
$mods=@($mods | Where-Object {$_ -notin $excluded})
if((Package $FixtureDir) -ne 'wildshift.tests.upgrade' -or (Package $WildShiftDir) -ne 'wildshift.mod') { throw 'Unexpected staged package identities.' }
$mods+=@('wildshift.tests.upgrade')
$gameAssembly=Join-Path $GameDir $(if($minor -eq '7'){'Assembly-CSharp.dll'}else{'RimWorldWin64_Data\Managed\Assembly-CSharp.dll'})
$production=Join-Path $WildShiftDir ('1.'+$minor+'\Assemblies\WildShift.dll')
$fixture=Join-Path $FixtureDir 'Assemblies\WildShiftUpgradeProbe.dll'
if($ExpectedWildShiftSHA256 -notmatch '^[A-Fa-f0-9]{64}$' -or (Hash $production) -ne $ExpectedWildShiftSHA256) { throw 'Production DLL does not match the explicit expected hash.' }
$null=New-Item -ItemType Directory -Path $Output
$profile=Join-Path $Output 'profile'
$config=Join-Path $profile 'Config'
$saves=Join-Path $profile 'Saves'
$null=New-Item -ItemType Directory -Path $config,$saves
$copy=Join-Path $Output 'source-copy.rws'
Copy-Item -LiteralPath $SavePath -Destination $copy
Copy-Item -LiteralPath $copy -Destination (Join-Path $saves 'autostart.rws')
if((Hash $copy) -ne $sourceHash) { throw 'Copied save differs from the original source.' }
$known=@($mods | Where-Object {$_ -match '^ludeon\.rimworld\.'})
$settings=[Xml.XmlDocument]::new(); $root=$settings.CreateElement('ModsConfigData'); $null=$settings.AppendChild($root)
foreach($pair in @(@('version',@($version)),@('activeMods',$mods),@('knownExpansions',$known))){$node=$settings.CreateElement($pair[0]); if($pair[0] -eq 'version'){$node.InnerText=$version}else{foreach($id in $pair[1]){$li=$settings.CreateElement('li');$li.InnerText=$id;$null=$node.AppendChild($li)}};$null=$root.AppendChild($node)}
$settings.Save((Join-Path $config 'ModsConfig.xml'))
'<PrefsData><langFolderName>Korean (한국어)</langFolderName><devMode>True</devMode><fullscreen>False</fullscreen><runInBackground>True</runInBackground></PrefsData>' | Set-Content -LiteralPath (Join-Path $config 'Prefs.xml') -Encoding utf8
$normalDir=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config'
$normalBefore=@(Inventory $normalDir)
ConvertTo-Json -InputObject $normalBefore -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'normal-config-before.json') -Encoding utf8
$inputs=@($SavePath,$GameExe,$gameAssembly,$production,$fixture,$PSCommandPath) | ForEach-Object {[pscustomobject]@{Path=$_;SHA256=(Hash $_)}}
$record=[ordered]@{Passed=$false;StatePassed=$false;NativeErrorFree=$false;OriginalSaveUnchanged=$false;NormalConfigUnchanged=$false;SourceSave=$SavePath;SourceSHA256=$sourceHash;SourceGameVersion=$saveVersion;GameVersion=$version;CycleTargetId=$CycleTargetId;RequestedGraphicsMode=$GraphicsMode;EffectiveGraphicsMode=$(if($minor -eq '7'){'Headless'}else{$GraphicsMode});PhaseLaunches=@();OriginalRequestedMods=$originalMods;ExcludedMods=$excluded;SameModProfile=($excluded.Count -eq 0);ProfileMeaning='Original IDs/order preserved plus one test helper, except explicitly recorded exclusions. No substitute package IDs are fabricated.';RequestedMods=$mods;InputFiles=@($inputs);Phases=@();Errors=@();NativeErrors=@();Scope='Actual old-save copy load, paused native snapshot, resave and fresh-process reload. Serialized-source comparison must be performed separately; this runner never claims that a first-load snapshot proves nothing was removed during migration.'}
try {
    foreach($phase in @(0,1)) {
        if(@(Get-CimInstance Win32_Process | Where-Object {$_.Name -match '^RimWorld.*\.exe$'}).Count){throw 'Another RimWorld is running; refusing concurrent launch.'}
        $probeInput=[Xml.XmlDocument]::new();$root=$probeInput.CreateElement('UpgradeInputs');$null=$probeInput.AppendChild($root)
        $values=[ordered]@{Phase=$phase;GameVersion=$nativeVersion;GameAssemblyPath=$gameAssembly;GameAssemblySHA256=(Hash $gameAssembly);WildShiftPath=$production;WildShiftSHA256=(Hash $production);FixtureSHA256=(Hash $fixture);CycleTargetId=$CycleTargetId}
        foreach($key in $values.Keys){$node=$probeInput.CreateElement($key);$node.InnerText=[string]$values[$key];$null=$root.AppendChild($node)}
        $node=$probeInput.CreateElement('Mods');foreach($id in $mods){$li=$probeInput.CreateElement('li');$li.InnerText=$id;$null=$node.AppendChild($li)};$null=$root.AppendChild($node)
        $probeInput.Save((Join-Path $profile 'upgrade-inputs.xml'))
        $stdout=Join-Path $Output ('phase-'+$phase+'-stdout.log');$stderr=Join-Path $Output ('phase-'+$phase+'-stderr.log')
        $arguments=@('-NoCrashDialog','-NoFileLog',('-savedatafolder="'+$profile+'"'))
        if($minor -eq '7'){$arguments+='-Headless'}else{
            if($GraphicsMode -eq 'Batch'){$arguments+='-batchmode'}
            $arguments+=@('-force-d3d11','-screen-fullscreen','0','-logFile',(Join-Path $Output ('phase-'+$phase+'-unity.log')))
        }
        $record.PhaseLaunches+=@([pscustomobject]@{Phase=$phase;GameExe=$GameExe;Arguments=@($arguments);WindowStyle='Hidden';GraphicsMode=$record.EffectiveGraphicsMode})
        $process=$null
        try {
            $process=Start-Process -FilePath $GameExe -ArgumentList $arguments -WorkingDirectory $GameDir -WindowStyle Hidden -Environment @{SteamAppId='294100';SteamGameId='294100'} -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
            $process.Id | Set-Content -LiteralPath (Join-Path $Output ('phase-'+$phase+'-pid.txt'))
            if(!$process.WaitForExit($TimeoutSeconds*1000)){throw ('Owned probe phase '+$phase+' timed out.')}
            $process.WaitForExit();$process.Refresh();if($process.ExitCode -ne 0){throw ('Owned probe exited '+$process.ExitCode)}
        } finally {if($process){if(!$process.HasExited){$process.Kill();$process.WaitForExit()};$process.Dispose()}}
        $resultPath=Join-Path $profile ('UpgradeResults\phase-'+$phase+'-result.xml')
        if(!(Test-Path -LiteralPath $resultPath)){throw ('No in-process probe result for phase '+$phase)}
        [xml]$result=Get-Content -LiteralPath $resultPath -Raw
        $record.Phases+=@([pscustomobject]@{Phase=$phase;StatePassed=([string]$result.Result.statePassed -eq 'true');Checks=@($result.Result.Checks.li);Exception=[string]$result.Result.Exception;Result=$resultPath})
        if([string]$result.Result.statePassed -ne 'true'){throw ('Native state probe phase '+$phase+' failed.')}
    }
    $record.StatePassed=$true
}catch{$record.Errors+=@($_.Exception.Message)}
finally {
    $record.OriginalSaveUnchanged=((Hash $SavePath) -eq $sourceHash)
    if(!$record.OriginalSaveUnchanged){$record.Errors+=@('Original source save changed during the probe.')}
    $normalAfter=@(Inventory $normalDir)
    ConvertTo-Json -InputObject $normalAfter -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'normal-config-after.json') -Encoding utf8
    $changes=@(Compare-Object @($normalBefore|ForEach-Object{$_.Path+'|'+$_.SHA256}) @($normalAfter|ForEach-Object{$_.Path+'|'+$_.SHA256}))
    $record.NormalConfigUnchanged=($changes.Count -eq 0)
    if(!$record.NormalConfigUnchanged){$record.Errors+=@('Normal user configuration bytes changed.')}
    foreach($path in Get-ChildItem -LiteralPath $Output -File | Where-Object {$_.Name -match '^phase-.*-(stdout|stderr|unity)\.log$'}){
        foreach($line in Get-Content -LiteralPath $path.FullName){if($line -match '\bERROR\b|^Error:|Exception|Could not load|Could not resolve|Fatal error|Internal CLR error|Crash!!!'){$record.NativeErrors+=@([pscustomobject]@{Path=$path.FullName;Line=$line})}}
    }
    $record.NativeErrorFree=($record.NativeErrors.Count -eq 0)
    foreach($file in $inputs){if((Hash $file.Path) -ne $file.SHA256){$record.Errors+=@('A runtime input changed: '+$file.Path)}}
    $record.Passed=($record.StatePassed -and $record.NativeErrorFree -and $record.Errors.Count -eq 0)
    $record | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'result.json') -Encoding utf8
}
$record | ConvertTo-Json -Depth 8
if(!$record.Passed){throw ('Probe retains failed overall verdict; inspect '+(Join-Path $Output 'result.json'))}
