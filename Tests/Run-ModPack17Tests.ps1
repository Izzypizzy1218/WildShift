<#
.SYNOPSIS
Runs the isolated original 75-mod startup baseline or launches its visual test map.
.DESCRIPTION
ModsConfigSource and SourceWildShiftDir must be explicit on launch. Only active
IDs/order and known expansions are read from the snapshot; mod settings and API
credentials are never copied. Startup owns/waits for its process. Visual returns
immediately and never waits for or kills the GUI. After manually closing that
game, call this script with -Mode Visual -FinalizeVisual -Output <same output>
to compare normal configuration and collect the helper's actual loaded inputs.
This script never installs, builds or replaces any game/mod/helper files.
#>
[CmdletBinding()]
param(
    [ValidateSet('Startup','Visual')][string]$Mode='Startup',
    [string]$GameDir='C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$GameExe='',
    [string]$ModsConfigSource='',
    [string]$SourceWildShiftDir='',
    [string]$InstalledWildShiftDir='',
    [string]$ExpectedWildShiftSHA256='',
    [string]$FixtureDir='',
    [string[]]$AdditionalMods=@(),
    [ValidateSet('English','Korean','Korean (한국어)')][string]$Language='English',
    [string]$Output='',
    [ValidateRange(30,1800)][int]$TimeoutSeconds=600,
    [switch]$FinalizeVisual,
    [switch]$AllowIsolatedParallel
)
$ErrorActionPreference='Stop'
$repoRoot=[IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\','/')
$workspace=Split-Path $repoRoot -Parent
if (!$Output) {
    if ($FinalizeVisual) { throw 'FinalizeVisual requires the original run Output directory.' }
    $Output=Join-Path $workspace ('.tmp\wildshift-modpack17-'+$Mode.ToLowerInvariant()+'-'+[guid]::NewGuid().ToString('N'))
}
$Output=[IO.Path]::GetFullPath($Output).TrimEnd('\','/')
if ($Output.Equals($repoRoot,[StringComparison]::OrdinalIgnoreCase) -or
    $Output.StartsWith($repoRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output must be outside the source repository.'
}
$resultPath=Join-Path $Output 'result.json'

function Hash([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
function NormalizeId([string]$id) { $id.Trim().ToLowerInvariant() }
function IsUnderPath([string]$path,[string]$directory) {
    $full=[IO.Path]::GetFullPath($path).TrimEnd('\','/')
    $parent=[IO.Path]::GetFullPath($directory).TrimEnd('\','/')
    return $full.StartsWith($parent+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
}
function CheckOtherGames {
    if ($AllowIsolatedParallel -and !(IsUnderPath $GameDir (Join-Path $workspace 'outputs'))) {
        throw 'AllowIsolatedParallel requires a private GameDir under this workspace outputs directory.'
    }
    $games=@(Get-CimInstance Win32_Process -Filter "Name = 'RimWorld.exe' OR Name = 'RimWorldWin64.exe'")
    if (!$games.Count) { return }
    if (!$AllowIsolatedParallel) { throw 'Close the running game before launching an isolated profile.' }
    $normalData=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData'
    foreach ($game in $games) {
        $command=[string]$game.CommandLine
        if (!$command -or $command -notmatch '(?i)(?:^|\s)"?-(?:Headless|batchmode)"?(?=\s|$)') {
            throw "Background game $($game.ProcessId) is interactive or its command line is unreadable."
        }
        $saveArguments=[Regex]::Matches($command,'(?i)(?:^|\s)(?:"-savedatafolder=(?<outer>[^"]+)"|-savedatafolder=(?:"(?<inner>[^"]+)"|(?<bare>[^\s"]+)))')
        if ($saveArguments.Count -ne 1) { throw "Background game $($game.ProcessId) has no unambiguous isolated save-data argument." }
        $profilePath=@('outer','inner','bare') | ForEach-Object { $saveArguments[0].Groups[$_].Value } | Where-Object { $_ }
        $profilePath=[IO.Path]::GetFullPath([string]$profilePath)
        if (!(IsUnderPath $profilePath $workspace) -or (IsUnderPath $profilePath $normalData) -or
            !(Test-Path -LiteralPath (Join-Path $profilePath 'Config\ModsConfig.xml') -PathType Leaf)) {
            throw "Background game $($game.ProcessId) does not have a verified workspace-isolated profile."
        }
        [pscustomobject]@{ProcessId=[int]$game.ProcessId;ExecutablePath=[string]$game.ExecutablePath;
            Profile=$profilePath;HeadlessOrBatch=$true;ProfileConfig=(Join-Path $profilePath 'Config\ModsConfig.xml')}
    }
}
function ConfigInventory([string]$directory) {
    if (Test-Path -LiteralPath $directory) {
        foreach ($file in Get-ChildItem -LiteralPath $directory -File -Recurse | Sort-Object FullName) {
            [pscustomobject]@{Path=$file.FullName;SHA256=(Hash $file.FullName)}
        }
    }
}
function CompareNormalConfig([string]$directory) {
    $before=@(Get-Content -LiteralPath (Join-Path $Output 'normal-config-before.json') -Raw | ConvertFrom-Json)
    $after=@(ConfigInventory $directory)
    ConvertTo-Json -InputObject $after -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'normal-config-after.json') -Encoding utf8
    $difference=@(Compare-Object -ReferenceObject @($before | ForEach-Object { $_.Path+'|'+$_.SHA256 }) -DifferenceObject @($after | ForEach-Object { $_.Path+'|'+$_.SHA256 }))
    ConvertTo-Json -InputObject $difference -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'normal-config-diff.json') -Encoding utf8
    return $difference.Count -eq 0
}
function ReadActiveIds([string]$path) {
    [xml]$xml=Get-Content -LiteralPath $path -Raw
    foreach ($node in $xml.ModsConfigData.activeMods.li) { NormalizeId ([string]$node) }
}
function GetLogIssues([string[]]$paths) {
    foreach ($path in $paths) {
        if (!(Test-Path -LiteralPath $path)) { continue }
        foreach ($line in Get-Content -LiteralPath $path) {
            if ($line -cmatch '^\d{2}:\d{2}:\d{2}\s+ERROR\b|^\s*ERROR\b|^\s*\[ERROR\]' -or
                $line -match 'Fatal error|Internal CLR error|Unhandled exception|Crash!!!|Critical error in root') {
                [pscustomobject]@{Path=$path;Line=$line}
            }
        }
    }
}
function VerifyInputs($record) {
    foreach ($input in $record.InputFiles) {
        if (!(Test-Path -LiteralPath $input.Path) -or (Hash $input.Path) -ne $input.SHA256) {
            throw "An input changed or disappeared: $($input.Path)"
        }
    }
}
function SaveResult($record) {
    $record | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $resultPath -Encoding utf8
}
function Summary($record) {
    [pscustomobject]@{Mode=$record.Mode;State=$record.State;Passed=$record.Passed;ProcessId=$record.ProcessId;
        Profile=$record.Profile;Stdout=$record.Stdout;Stderr=$record.Stderr;Result=$resultPath;
        ExpectedModCount=@($record.ExpectedActiveIds).Count;NormalConfigUnchanged=$record.NormalConfigUnchanged}
}

if ($FinalizeVisual) {
    if ($Mode -ne 'Visual') { throw 'FinalizeVisual is only valid with Mode Visual.' }
    if (!(Test-Path -LiteralPath $resultPath)) { throw 'Visual launch record is missing.' }
    $record=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($record.Mode -ne 'Visual') { throw 'Output does not contain a visual launch.' }
    $stillRunning=Get-Process -Id $record.ProcessId -ErrorAction SilentlyContinue
    if ($stillRunning) {
        try {
            if ([Math]::Abs(($stillRunning.StartTime.ToUniversalTime()-[DateTime]::Parse($record.ProcessStartUtc).ToUniversalTime()).TotalSeconds) -lt 2) {
                throw 'Close the visual game manually before finalizing; this script never stops it.'
            }
        } finally { $stillRunning.Dispose() }
    }
    $errors=New-Object 'System.Collections.Generic.List[string]'
    try { VerifyInputs $record } catch { $errors.Add($_.Exception.Message) }
    $record.NormalConfigUnchanged=CompareNormalConfig $record.NormalConfigDirectory
    if (!$record.NormalConfigUnchanged) { $errors.Add('Normal user configuration changed.') }
    $issues=@(GetLogIssues @($record.Stdout,$record.Stderr))
    if ($issues.Count) { $errors.Add('Native ERROR/fatal output was logged; inspect LogIssues.') }
    $record.LogIssues=$issues
    try {
        $evidence=Join-Path $record.Profile 'ModPackVisualResults'
        if (!(Test-Path -LiteralPath (Join-Path $evidence 'ready.txt')) -or (Test-Path -LiteralPath (Join-Path $evidence 'error.txt'))) {
            throw 'Visual helper did not finish its native map preparation successfully.'
        }
        $loaded=@(Get-Content -LiteralPath (Join-Path $evidence 'loaded-mods.tsv') | ForEach-Object { NormalizeId (($_ -split "`t",2)[0]) })
        if (($loaded -join '|') -ne (@($record.ExpectedActiveIds) -join '|')) { throw 'Actual visual loaded mod IDs/order differ from the isolated profile.' }
        $actualProduction=(Get-Content -LiteralPath (Join-Path $evidence 'production-dll.txt') -Raw).Trim()
        if ((Hash $actualProduction) -ne $record.ExpectedWildShiftSHA256) { throw 'Visual helper loaded an unexpected production DLL.' }
        $record.ActualLoadedIds=$loaded
        $record.ActualProductionPath=$actualProduction
        $record.ActualProductionSHA256=Hash $actualProduction
        $record.LoadedInputsVerified=$true
    } catch { $errors.Add($_.Exception.Message) }
    $record.Errors=@($errors)
    $record.State=if ($errors.Count) { 'visual-session-evidence-failed' } else { 'visual-session-closed-awaiting-operator-verdict' }
    $record.AutomatedEvidencePassed=($errors.Count -eq 0)
    $record.CompletedUtc=[DateTime]::UtcNow.ToString('O')
    # A process/loaded-input check cannot decide whether armor was visible or
    # whether the operator's UI interaction succeeded. Keep Passed unset.
    SaveResult $record
    Summary $record
    if ($errors.Count) { throw "Visual session evidence failed; see $resultPath" }
    return
}

if (!$ModsConfigSource -or !$SourceWildShiftDir) { throw 'Launch requires explicit ModsConfigSource and SourceWildShiftDir.' }
if (Test-Path -LiteralPath $Output) { throw 'Use a fresh external output directory.' }
if ($ExpectedWildShiftSHA256 -notmatch '^[0-9a-fA-F]{64}$') { throw 'ExpectedWildShiftSHA256 must be an explicit SHA256.' }
$backgroundGames=@(CheckOtherGames)
$version=(Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -Raw).Trim()
if ($version -notmatch '^1\.7\.') { throw "Expected actual RimWorld 1.7, found $version." }
$stockGameExe=[IO.Path]::GetFullPath((Join-Path $GameDir 'RimWorld.exe'))
if (!$GameExe) { $GameExe=$stockGameExe }
elseif (![IO.Path]::IsPathFullyQualified($GameExe)) { throw 'GameExe must be an absolute path.' }
$GameExe=[IO.Path]::GetFullPath($GameExe)
if (!$GameExe.Equals($stockGameExe,[StringComparison]::OrdinalIgnoreCase)) {
    if (!(IsUnderPath $GameDir (Join-Path $workspace 'outputs')) -or !(IsUnderPath $GameExe $GameDir)) {
        throw 'A renamed GameExe is allowed only inside a private GameDir under workspace outputs.'
    }
    if ((Hash $GameExe) -ne (Hash $stockGameExe)) { throw 'Renamed GameExe must be byte-identical to this private game stock RimWorld.exe.' }
}
$gameAssembly=Join-Path $GameDir 'Assembly-CSharp.dll'
if (!(Test-Path -LiteralPath $gameExe) -or !(Test-Path -LiteralPath $gameAssembly)) { throw 'Managed RimWorld 1.7 runtime files are missing.' }
if (!$InstalledWildShiftDir) { $InstalledWildShiftDir=Join-Path $GameDir 'Mods\WildShift' }
$sourceDll=Join-Path $SourceWildShiftDir '1.7\Assemblies\WildShift.dll'
$installedDll=Join-Path $InstalledWildShiftDir '1.7\Assemblies\WildShift.dll'
foreach ($dll in @($sourceDll,$installedDll)) {
    if ((Hash $dll) -ne $ExpectedWildShiftSHA256) { throw "WildShift is not the expected final 1.7 candidate: $dll" }
}
[xml]$sourceXml=Get-Content -LiteralPath $ModsConfigSource -Raw
$ids=@(ReadActiveIds $ModsConfigSource)
if ($ids.Count -ne 75 -or @($ids | Select-Object -Unique).Count -ne 75 -or $ids -contains '') {
    throw 'ModsConfigSource must contain the original 75 unique active package IDs in their original order.'
}
if ($ids -notcontains 'wildshift.mod') { throw 'Original profile does not contain WildShift.' }
if (@($ids | Where-Object { $_ -like 'wildshift.tests.*' -or $_ -eq 'wildshift.modpackvisualdemo' }).Count) {
    throw 'Original 75-mod startup snapshot must not already contain a WildShift test fixture.'
}
$baseIds=@($ids)
foreach ($extra in $AdditionalMods) {
    $id=NormalizeId $extra
    if (!$id -or $ids -contains $id -or $id -like 'wildshift.tests.*' -or $id -eq 'wildshift.modpackvisualdemo') {
        throw 'AdditionalMods must contain distinct, non-fixture package IDs absent from the original profile.'
    }
    $ids+=@($id)
}
$fixtureDll=$null
if ($Mode -eq 'Visual') {
    if (!$FixtureDir) { throw 'Visual mode requires a separately staged FixtureDir.' }
    [xml]$fixtureXml=Get-Content -LiteralPath (Join-Path $FixtureDir 'About\About.xml') -Raw
    if ((NormalizeId ([string]$fixtureXml.ModMetaData.packageId)) -ne 'wildshift.modpackvisualdemo') { throw 'Unexpected visual fixture package.' }
    $fixtureDll=Join-Path $FixtureDir 'Assemblies\WildShiftModPackVisual.dll'
    if (!(Test-Path -LiteralPath $fixtureDll)) { throw 'Separately built visual helper DLL is missing.' }
    $ids+=@('wildshift.modpackvisualdemo')
} elseif ($FixtureDir) { throw 'Startup mode uses the original profile without a fixture; omit FixtureDir.' }

$null=New-Item -ItemType Directory -Path $Output
$profile=Join-Path $Output 'profile'
$config=Join-Path $profile 'Config'
$null=New-Item -ItemType Directory -Path $config -Force
$xml=New-Object Xml.XmlDocument
$root=$xml.CreateElement('ModsConfigData'); $null=$xml.AppendChild($root)
$versionNode=$xml.CreateElement('version'); $versionNode.InnerText=$version; $null=$root.AppendChild($versionNode)
$active=$xml.CreateElement('activeMods'); $null=$root.AppendChild($active)
foreach ($id in $ids) { $li=$xml.CreateElement('li'); $li.InnerText=$id; $null=$active.AppendChild($li) }
$known=$xml.CreateElement('knownExpansions'); $null=$root.AppendChild($known)
foreach ($expansion in $sourceXml.ModsConfigData.knownExpansions.li) {
    $li=$xml.CreateElement('li'); $li.InnerText=[string]$expansion; $null=$known.AppendChild($li)
}
$profileConfig=Join-Path $config 'ModsConfig.xml'; $xml.Save($profileConfig)
$languageFolder = if ($Language -eq 'Korean') { 'Korean (한국어)' } else { $Language }
('<PrefsData><langFolderName>'+$languageFolder+'</langFolderName><devMode>True</devMode><screenWidth>1600</screenWidth><screenHeight>900</screenHeight><fullscreen>False</fullscreen><runInBackground>True</runInBackground></PrefsData>') |
    Set-Content -LiteralPath (Join-Path $config 'Prefs.xml') -Encoding utf8
$normalConfig=Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config'
$before=@(ConfigInventory $normalConfig)
ConvertTo-Json -InputObject $before -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'normal-config-before.json') -Encoding utf8
$inputPaths=@($ModsConfigSource,$GameExe,$stockGameExe,$gameAssembly,(Join-Path $GameDir 'Version.txt'),$sourceDll,$installedDll,$PSCommandPath)
if ($fixtureDll) { $inputPaths+=@($fixtureDll,(Join-Path $FixtureDir 'About\About.xml')) }
$inputFiles=@($inputPaths | Select-Object -Unique | ForEach-Object { [pscustomobject]@{Path=[IO.Path]::GetFullPath($_);SHA256=(Hash $_)} })
$stdout=Join-Path $Output 'stdout.log'; $stderr=Join-Path $Output 'stderr.log'
$record=[ordered]@{Mode=$Mode;State='prepared';Passed=$null;AutomatedEvidencePassed=$null;ProcessId=$null;ProcessStartUtc=$null;
    GameVersion=$version;GameDir=[IO.Path]::GetFullPath($GameDir);GameExe=$GameExe;GameExeSHA256=(Hash $GameExe);Profile=$profile;Stdout=$stdout;Stderr=$stderr;
    Snapshot=[IO.Path]::GetFullPath($ModsConfigSource);BaseActiveIds=$baseIds;AdditionalMods=@($AdditionalMods);ExpectedActiveIds=$ids;
    ExpectedWildShiftSHA256=$ExpectedWildShiftSHA256;ExpectedInstalledWildShiftPath=$installedDll;InputFiles=$inputFiles;
    NormalConfigDirectory=$normalConfig;NormalConfigUnchanged=$null;NormalConfigUnchangedAtLaunch=$null;
    LoadedInputsVerified=$false;ActualLoadedIds=@();ActualProductionPath=$null;ActualProductionSHA256=$null;
    BootFinished=$false;ExitCode=$null;LogIssues=@();Errors=@();CompletedUtc=$null;
    AllowIsolatedParallel=$AllowIsolatedParallel.IsPresent;ObservedBackgroundGames=$backgroundGames;
    Scope=if ($Mode -eq 'Startup') { 'Original mod-pack startup without fixture. Expected IDs and staged inputs are recorded; no in-process loaded-ID/hash probe or visual/gameplay success is claimed.' }
        else { 'Visual GUI session remains under operator control. Fixture records loaded IDs and production path; screenshots and manual interactions require a separate operator verdict.' }}
SaveResult $record
$arguments=@('-NoCrashDialog','-NoFileLog',('-savedatafolder="'+$profile+'"'))
if ($Mode -eq 'Startup') { $arguments+=@('-Headless','-QuitAfterBoot','-MemoryReportAfterBoot') }
else { $arguments+=@('-quicktest') }
$process=$null
try {
    # A visible window is intentional for this operator-controlled UI session.
    $windowStyle=if ($Mode -eq 'Visual') { 'Normal' } else { 'Hidden' }
    $process=Start-Process -FilePath $gameExe -ArgumentList $arguments -WorkingDirectory $GameDir -WindowStyle $windowStyle `
        -Environment @{SteamAppId='294100';SteamGameId='294100'} -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $record.ProcessId=$process.Id; $record.ProcessStartUtc=$process.StartTime.ToUniversalTime().ToString('O')
    $record.State=if ($Mode -eq 'Visual') { 'running-awaiting-manual-verification' } else { 'running-startup' }
    SaveResult $record
    if ($Mode -eq 'Visual') {
        $record.NormalConfigUnchangedAtLaunch=CompareNormalConfig $normalConfig
        SaveResult $record
        Summary $record
        return
    }
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while (!$process.WaitForExit(1000)) {
        if ($timer.Elapsed.TotalSeconds -gt $TimeoutSeconds) { throw "Startup timed out after $TimeoutSeconds seconds." }
    }
    $process.WaitForExit(); $process.Refresh(); $record.ExitCode=$process.ExitCode
    if ($process.ExitCode -ne 0) { throw "Startup exited with $($process.ExitCode)." }
    $log=Get-Content -LiteralPath $stdout -Raw
    $record.BootFinished=($log -match '\bINFO Process memory\b' -and $log -match 'Bootstrap\.(RunAfterBootSwitches|RaiseBootCompleteOnce)')
    if (!$record.BootFinished) { throw 'Exit lacks the native after-boot memory report/boot-complete evidence.' }
    if (($log -notmatch [Regex]::Escape('RimWorld '+$version))) { throw 'Native log does not identify the expected actual 1.7 game version.' }
    $record.LogIssues=@(GetLogIssues @($stdout,$stderr))
    if ($record.LogIssues.Count) { throw 'Native ERROR/fatal output was logged; inspect LogIssues.' }
    $afterIds=@(ReadActiveIds $profileConfig)
    if (($afterIds -join '|') -ne ($ids -join '|')) { throw 'Isolated active mod IDs/order changed during boot.' }
    VerifyInputs $record
} catch {
    $record.Errors+=@($_.Exception.Message)
} finally {
    if ($Mode -eq 'Startup') {
        if ($process -and !$process.HasExited) { $process.Kill(); $process.WaitForExit() }
        $record.NormalConfigUnchanged=CompareNormalConfig $normalConfig
        if (!$record.NormalConfigUnchanged) { $record.Errors+=@('Normal user configuration changed.') }
        $record.LogIssues=@(GetLogIssues @($stdout,$stderr))
        if ($record.LogIssues.Count -and $record.Errors -notcontains 'Native ERROR/fatal output was logged; inspect LogIssues.') {
            $record.Errors+=@('Native ERROR/fatal output was logged; inspect LogIssues.')
        }
        try { VerifyInputs $record } catch { $record.Errors+=@($_.Exception.Message) }
        $record.Passed=($record.Errors.Count -eq 0 -and $record.BootFinished)
        $record.AutomatedEvidencePassed=$record.Passed
        $record.State=if ($record.Passed) { 'startup-passed' } else { 'startup-failed' }
        $record.CompletedUtc=[DateTime]::UtcNow.ToString('O')
    } elseif ($record.Errors.Count) { $record.State='visual-launch-failed'; $record.CompletedUtc=[DateTime]::UtcNow.ToString('O') }
    SaveResult $record
    if ($process) { $process.Dispose() }
}
Summary $record
if ($record.Errors.Count) { throw "Mod-pack $Mode failed; see $resultPath" }
