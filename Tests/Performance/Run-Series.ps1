[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDir,
    [Parameter(Mandatory)][string]$FixtureDir,
    [Parameter(Mandatory)][string]$BaselineSave,
    [Parameter(Mandatory)][string]$Output,
    [Parameter(Mandatory)][string]$ExpectedProductionSHA256,
    [ValidateSet('baseline','candidate')][string]$Label='baseline',
    [int[]]$Forms=@(0,20,50),
    [ValidateRange(0,600)][int]$WarmupSeconds=30,
    [ValidateRange(60,7200)][int]$MeasureSeconds=120
)
$ErrorActionPreference='Stop'
$Output=[IO.Path]::GetFullPath($Output)
if(Test-Path -LiteralPath $Output){throw 'Use a fresh series directory.'}
if(@($Forms|Select-Object -Unique).Count -ne $Forms.Count -or @($Forms|Where-Object {$_ -notin @(0,20,50)}).Count){throw 'Invalid or repeated case.'}
$null=New-Item -ItemType Directory -Path $Output
$baselineHash=(Get-FileHash -LiteralPath $BaselineSave).Hash
$cases=[Collections.Generic.List[object]]::new()
$referenceIds=$null
foreach($count in $Forms){
    $caseOutput=Join-Path $Output ('case-'+$count)
    $log=Join-Path $Output ('case-'+$count+'-console.log')
    try{
        & (Join-Path $PSScriptRoot 'Run-Performance.ps1') -SkipBuild -Phase measure -ActiveForms $count -WarmupSeconds $WarmupSeconds -MeasureSeconds $MeasureSeconds -SampleSeconds 10 -GraphicsMode Native -GameDir $GameDir -FixtureDir $FixtureDir -BaselineSave $BaselineSave -Output $caseOutput *> $log
    }catch{
        # The raw strict result retains external loader errors. Acceptance below
        # checks state separately and names only already-observed startup cases.
        if(!(Test-Path -LiteralPath (Join-Path $caseOutput 'result.json'))){throw}
    }
    $raw=Get-Content -LiteralPath (Join-Path $caseOutput 'result.json') -Raw|ConvertFrom-Json
    $native=$raw.NativeResult
    if(!$raw.StatePassed -or !$native.Passed -or $native.Smoke -or $native.MeasuredSeconds -lt $MeasureSeconds){throw ('Invalid measured state: '+$caseOutput)}
    if($native.WildShiftSHA256 -ne $ExpectedProductionSHA256 -or $raw.BaselineSHA256 -ne $baselineHash -or !$raw.BaselineUnchanged -or !$raw.NormalConfigUnchanged){throw 'Series production/baseline/config identity changed.'}
    if(!$native.NativeFrameCapVerified -or $native.ActiveForms -ne $count -or $native.TotalCanonicalPawns -ne 50 -or $native.SpawnedBodies -ne 50 -or $native.NoAnimalsDestructionFlag -or $native.NewWildAnimalDensity -ne 0){throw 'Fixed workload/frame/animal-density guard failed.'}
    $unknown=@($raw.NativeErrors|Where-Object {
        $_ -notmatch 'loader_get_json: Failed to open JSON file .*EOSOverlayVkLayer-Win64\.json$' -and
        $_ -notmatch 'loader_get_json: Failed to open JSON file .*SocialClubVulkanLayer\.json$' -and
        $_ -notmatch '^Fossilize ERROR: Failed to parse ApplicationInfoFilter, letting recording go through\.$' -and
        $_ -notmatch '^\S+\s+at Brutal\.Logging\.LogCategory\.Error\('
    })
    if($unknown.Count){throw ('Unexpected native diagnostics: '+($unknown -join ' | '))}
    $ids=@($native.OriginalIds)-join '|'
    if($null -eq $referenceIds){$referenceIds=$ids}elseif($referenceIds -cne $ids){throw 'Canonical identities differ across cases.'}
    $acceptance=[pscustomobject]@{Accepted=$true;Label=$Label;ActiveForms=$count;NativeStatePassed=$true;RawStrictPassed=$raw.Passed;NativeErrorFree=$raw.NativeErrorFree;KnownExternalStartupDiagnostics=@($raw.NativeErrors);Output=$caseOutput;BaselineSHA256=$baselineHash;ProductionSHA256=$ExpectedProductionSHA256;MeasuredSeconds=$native.MeasuredSeconds;Tps=$native.Tps;FramesPerSecond=$native.FramesPerSecond;TickMeanMicroseconds=$native.TickMeanMicroseconds;TickP95Microseconds=$native.TickP95Microseconds;Scope='Actual controlled native 60 FPS workload; accepted state does not erase raw external startup diagnostics.'}
    $acceptance|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $caseOutput 'acceptance.json')
    $cases.Add($acceptance)
    [pscustomobject]@{CompletedCases=$cases.Count;PlannedCases=$Forms.Count;Last=$acceptance}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $Output 'series-progress.json')
}
[pscustomobject]@{Accepted=$true;Label=$Label;Cases=$cases.ToArray();BaselineSHA256=$baselineHash;ProductionSHA256=$ExpectedProductionSHA256;StrictAllNativeErrorFree=(@($cases|Where-Object {!$_.NativeErrorFree}).Count -eq 0);Scope='Per-version fixed-population 0/20/50 native comparison. No unqualified whole-mod-pack or real working-colony claim.'}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath (Join-Path $Output 'series-result.json')
