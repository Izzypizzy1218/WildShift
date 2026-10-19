<#
.SYNOPSIS
Runs an installed RimWorld 1.7 build with a fresh, isolated startup-test profile.
.DESCRIPTION
No installation, publishing, source changes, or changes to the user's ModsConfig
are performed. This checks startup only, not maps, rendering, or gameplay. The
game itself may clean up old Player.log files in its normal Windows log folder.
Input hashes identify requested files; they are not an in-process module inventory.
#>
[CmdletBinding()]
param(
    [string]$GameDir = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [string]$HarmonyPath = 'C:\Program Files (x86)\Steam\steamapps\workshop\content\294100\2009463077\1.7\Assemblies\0Harmony.dll',
    [string]$WildShiftDir = '',
    [string]$Output = '',
    [ValidateRange(10, 1800)][int]$TimeoutSeconds = 120,
    [switch]$WithoutWildShift
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent)).TrimEnd('\', '/')
if (!$WildShiftDir) { $WildShiftDir = Join-Path $GameDir 'Mods\WildShift' }
if (!$Output) {
    $runName = 'wildshift-startup-17-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $Output = Join-Path (Split-Path $repoRoot -Parent) ('.tmp\' + $runName)
}
$Output = [IO.Path]::GetFullPath($Output).TrimEnd('\', '/')
if ($Output.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase) -or
    $Output.StartsWith($repoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output must be outside the source repository.'
}
if (Test-Path -LiteralPath $Output) { throw "Refusing existing output: $Output" }
if (@(Get-Process -Name 'RimWorld', 'RimWorldWin64' -ErrorAction SilentlyContinue).Count) {
    throw 'Close the running RimWorld process before starting this test.'
}
$null = New-Item -ItemType Directory -Path $Output

function Get-PackageId([string]$ModDir) {
    [xml]$about = Get-Content -LiteralPath (Join-Path $ModDir 'About\About.xml') -Raw
    $packageId = [string]$about.ModMetaData.packageId
    if ([string]::IsNullOrWhiteSpace($packageId)) { throw "Missing package ID: $ModDir" }
    return $packageId.Trim().ToLowerInvariant()
}

function Get-SelectedDll([string]$ModDir, [string]$FileName) {
    [xml]$folders = Get-Content -LiteralPath (Join-Path $ModDir 'LoadFolders.xml') -Raw
    $versionFolders = @($folders.SelectNodes('/loadFolders/v1.7/li'))
    if (!$versionFolders.Count) { throw "No explicit 1.7 load folders: $ModDir" }
    $matches = @(
        foreach ($folder in $versionFolders) {
            if ($folder.Attributes.Count) { throw "Conditional load folder requires manual review: $ModDir" }
            $relative = $folder.InnerText.Trim().Trim('/', '\')
            $folderPath = if ($relative) { Join-Path $ModDir $relative } else { $ModDir }
            $candidate = Join-Path $folderPath ('Assemblies\' + $FileName)
            if (Test-Path -LiteralPath $candidate -PathType Leaf) { [IO.Path]::GetFullPath($candidate) }
        }
    )
    if ($matches.Count -ne 1) { throw "Expected exactly one selected $FileName for 1.7 in $ModDir; found $($matches.Count)." }
    return $matches[0]
}

function Get-InputRecord([string]$Label, [string]$Path) {
    $file = Get-Item -LiteralPath $Path
    if ($file.PSIsContainer) { throw "Expected file: $Path" }
    [pscustomobject]@{
        Label = $Label
        Path = $file.FullName
        SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        Length = $file.Length
        ProductVersion = $file.VersionInfo.ProductVersion
    }
}

function Save-Profile([string]$Path, [string]$Version, [string[]]$ActiveMods, [string[]]$Expansions) {
    $document = New-Object System.Xml.XmlDocument
    $null = $document.AppendChild($document.CreateXmlDeclaration('1.0', 'utf-8', $null))
    $rootNode = $document.CreateElement('ModsConfigData')
    $null = $document.AppendChild($rootNode)
    $versionNode = $document.CreateElement('version')
    $versionNode.InnerText = $Version
    $null = $rootNode.AppendChild($versionNode)
    foreach ($listName in @('activeMods', 'knownExpansions')) {
        $list = $document.CreateElement($listName)
        $null = $rootNode.AppendChild($list)
        $values = if ($listName -eq 'activeMods') { $ActiveMods } else { $Expansions }
        foreach ($value in $values) {
            $item = $document.CreateElement('li')
            $item.InnerText = $value
            $null = $list.AppendChild($item)
        }
    }
    $document.Save($Path)
}

$result = [ordered]@{
    Passed = $false
    StartedUtc = [DateTime]::UtcNow.ToString('o')
    FinishedUtc = $null
    Output = $Output
    WithoutWildShift = [bool]$WithoutWildShift
    GameVersion = $null
    RuntimeFramework = $null
    RequestedActiveMods = @()
    Inputs = @()
    ProcessId = $null
    Arguments = @()
    ExitCode = $null
    TimedOut = $false
    StdoutErrors = @()
    StderrFatalErrors = @()
    WarningCount = 0
    BootCompletionEvidence = @()
    HarmonyEvidence = @()
    LegacyLogsBackedUp = @()
    ProfilePreserved = $false
    Failures = @()
    Scope = 'Startup only. Disk hashes and load-folder checks identify requested inputs, not the actual loaded module paths. Duplicate installed package IDs require separate review.'
}
$bootProcess = $null
$configPath = $null
$stdout = Join-Path $Output 'stdout.log'
$stderr = Join-Path $Output 'stderr.log'

try {
    $GameDir = (Resolve-Path -LiteralPath $GameDir).Path
    $HarmonyPath = (Resolve-Path -LiteralPath $HarmonyPath).Path
    $executable = Join-Path $GameDir 'RimWorld.exe'
    $result.GameVersion = (Get-Content -LiteralPath (Join-Path $GameDir 'Version.txt') -Raw).Trim()
    if ($result.GameVersion -notmatch '^1\.7\.') { throw "Expected RimWorld 1.7, found $($result.GameVersion)." }
    $harmonyRoot = Split-Path (Split-Path (Split-Path $HarmonyPath -Parent) -Parent) -Parent
    if ((Get-PackageId $harmonyRoot) -ne 'brrainz.harmony') { throw 'HarmonyPath is not inside the Harmony package.' }
    if ((Get-SelectedDll $harmonyRoot '0Harmony.dll') -ne $HarmonyPath) { throw 'HarmonyPath is not the sole DLL selected by its 1.7 load folders.' }

    $inputRecords = @(
        Get-InputRecord 'Game executable' $executable
        Get-InputRecord 'Game assembly' (Join-Path $GameDir 'Assembly-CSharp.dll')
        Get-InputRecord 'Bootstrap assembly' (Join-Path $GameDir 'RimWorld.dll')
        Get-InputRecord 'Engine assembly' (Join-Path $GameDir 'Waylith.dll')
        Get-InputRecord 'CLR runtime' (Join-Path $GameDir 'coreclr.dll')
        Get-InputRecord 'JIT compiler' (Join-Path $GameDir 'clrjit.dll')
        Get-InputRecord 'Core library' (Join-Path $GameDir 'System.Private.CoreLib.dll')
        Get-InputRecord 'Runtime configuration' (Join-Path $GameDir 'RimWorld.runtimeconfig.json')
        Get-InputRecord 'Requested Harmony' $HarmonyPath
        Get-InputRecord 'Harmony mod wrapper' (Join-Path (Split-Path $HarmonyPath -Parent) 'HarmonyMod.dll')
    )
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $GameDir 'RimWorld.runtimeconfig.json') -Raw | ConvertFrom-Json
    $result.RuntimeFramework = $runtimeConfig.runtimeOptions.includedFrameworks
    if (!$result.RuntimeFramework) { $result.RuntimeFramework = $runtimeConfig.runtimeOptions.framework }
    if (!$WithoutWildShift) {
        $WildShiftDir = (Resolve-Path -LiteralPath $WildShiftDir).Path
        if ((Get-PackageId $WildShiftDir) -ne 'wildshift.mod' -or (Get-PackageId $repoRoot) -ne 'wildshift.mod') {
            throw 'Installed and source package IDs must both be wildshift.mod.'
        }
        $installedDll = Get-SelectedDll $WildShiftDir 'WildShift.dll'
        $expected = Get-InputRecord 'Expected source WildShift' (Join-Path $repoRoot '1.7\Assemblies\WildShift.dll')
        $installed = Get-InputRecord 'Requested installed WildShift' $installedDll
        $inputRecords += @($expected, $installed)
        $result.Inputs = $inputRecords
        if ($expected.SHA256 -ne $installed.SHA256) { throw 'Installed WildShift DLL differs from the source repository 1.7 build.' }
    }
    $result.Inputs = $inputRecords
    if ((Get-PackageId (Join-Path $GameDir 'Data\Core')) -ne 'ludeon.rimworld') { throw 'Invalid Core package.' }
    $expansions = @(
        foreach ($dlc in @('Royalty', 'Ideology', 'Biotech', 'Anomaly', 'Odyssey')) {
            $dlcDir = Join-Path $GameDir ('Data\' + $dlc)
            if (Test-Path -LiteralPath (Join-Path $dlcDir 'About\About.xml') -PathType Leaf) {
                $packageId = Get-PackageId $dlcDir
                if ($packageId -ne ('ludeon.rimworld.' + $dlc.ToLowerInvariant())) { throw "Unexpected DLC package: $dlcDir" }
                $packageId
            }
        }
    )
    $activeMods = @('brrainz.harmony', 'ludeon.rimworld') + $expansions
    if (!$WithoutWildShift) { $activeMods += 'wildshift.mod' }
    $result.RequestedActiveMods = $activeMods
    $configDir = Join-Path $Output 'Config'
    $null = New-Item -ItemType Directory -Path $configDir
    $configPath = Join-Path $configDir 'ModsConfig.xml'
    Save-Profile $configPath $result.GameVersion $activeMods $expansions
    '<PrefsData><langFolderName>English</langFolderName><devMode>True</devMode><screenWidth>1280</screenWidth><screenHeight>720</screenHeight><fullscreen>False</fullscreen></PrefsData>' |
        Set-Content -LiteralPath (Join-Path $configDir 'Prefs.xml') -Encoding UTF8

    # The engine deletes legacy Unity logs even with an isolated savedatafolder.
    # Preserve any existing logs here, without writing to the normal profile.
    $legacyLogDir = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'
    foreach ($logName in @('Player.log', 'Player-prev.log')) {
        $legacyLog = Join-Path $legacyLogDir $logName
        if (Test-Path -LiteralPath $legacyLog -PathType Leaf) {
            Copy-Item -LiteralPath $legacyLog -Destination (Join-Path $Output ('legacy-' + $logName))
            $result.LegacyLogsBackedUp += $legacyLog
        }
    }

    # At post-boot frame 120 this emits a Process memory report; QuitAfterBoot
    # exits at frame 180. Both require PlayDataLoader.Loaded and no pending long events.
    $launchArguments = @('-Headless', '-NoCrashDialog', '-NoFileLog', '-MemoryReportAfterBoot', '-QuitAfterBoot', ('-savedatafolder="' + $Output + '"'))
    $result.Arguments = $launchArguments
    if (@(Get-Process -Name 'RimWorld', 'RimWorldWin64' -ErrorAction SilentlyContinue).Count) {
        throw 'RimWorld started during preflight; refusing concurrent launch.'
    }
    $bootProcess = Start-Process -FilePath $executable -ArgumentList $launchArguments -WorkingDirectory $GameDir -WindowStyle Hidden `
        -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru
    $result.ProcessId = $bootProcess.Id
    $bootProcess.Id | Set-Content -LiteralPath (Join-Path $Output 'pid.txt') -Encoding ASCII
    if (!$bootProcess.WaitForExit($TimeoutSeconds * 1000)) {
        $result.TimedOut = $true
        $result.Failures += "Startup exceeded $TimeoutSeconds seconds."
        # This Process object is the only process this script may terminate.
        if (!$bootProcess.HasExited) { $bootProcess.Kill() }
    }
    $bootProcess.WaitForExit()
    $bootProcess.Refresh()
    $result.ExitCode = $bootProcess.ExitCode
    if ($null -eq $result.ExitCode -or $result.ExitCode -ne 0) { $result.Failures += "Game exit code: $($result.ExitCode)." }
}
catch {
    $result.Failures += $_.Exception.Message
}
finally {
    if ($null -ne $bootProcess -and !$bootProcess.HasExited) {
        # An exception after launch must not leave our test process running.
        try {
            $bootProcess.Kill()
            $bootProcess.WaitForExit()
        }
        catch { $result.Failures += "Could not stop owned test process: $($_.Exception.Message)" }
    }
    if (Test-Path -LiteralPath $stdout) {
        $result.StdoutErrors = @(Select-String -LiteralPath $stdout -Pattern '\bERROR\b' | ForEach-Object Line)
        $result.WarningCount = @(Select-String -LiteralPath $stdout -Pattern '\bWARN\b').Count
        $result.BootCompletionEvidence = @(Select-String -LiteralPath $stdout -Pattern 'Process memory \d{4}-\d{2}-\d{2} .*: resident ' | ForEach-Object Line)
        $result.HarmonyEvidence = @(Select-String -LiteralPath $stdout -Pattern 'Harmony Mod v.*Harmony library v' | ForEach-Object Line)
    }
    if (Test-Path -LiteralPath $stderr) {
        $result.StderrFatalErrors = @(Select-String -LiteralPath $stderr -Pattern 'Fatal error|Internal CLR error|Unhandled exception' | ForEach-Object Line)
    }
    if ($result.StdoutErrors.Count) { $result.Failures += "stdout contains $($result.StdoutErrors.Count) ERROR lines." }
    if ($result.StderrFatalErrors.Count) { $result.Failures += "stderr contains $($result.StderrFatalErrors.Count) fatal error lines." }
    if (!$result.BootCompletionEvidence.Count) { $result.Failures += 'No post-boot memory report: startup completion was not verified.' }
    if (!$result.HarmonyEvidence.Count) { $result.Failures += 'Harmony initialization was not found in stdout.' }
    if ($configPath -and (Test-Path -LiteralPath $configPath)) {
        try {
            [xml]$finalConfig = Get-Content -LiteralPath $configPath -Raw
            $actualMods = @($finalConfig.ModsConfigData.activeMods.li | ForEach-Object { ([string]$_).ToLowerInvariant() })
            $result.ProfilePreserved = (($actualMods -join '|') -eq ($result.RequestedActiveMods -join '|'))
        }
        catch { $result.Failures += "Could not inspect final ModsConfig: $($_.Exception.Message)" }
    }
    if (!$result.ProfilePreserved) { $result.Failures += 'The final active-mod list does not match the requested profile.' }
    $result.FinishedUtc = [DateTime]::UtcNow.ToString('o')
    $result.Passed = ($result.Failures.Count -eq 0)
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $Output 'result.json') -Encoding UTF8
    if ($null -ne $bootProcess) { $bootProcess.Dispose() }
}

$result | ConvertTo-Json -Depth 8
if (!$result.Passed) { throw "Startup check failed; see $(Join-Path $Output 'result.json')." }
