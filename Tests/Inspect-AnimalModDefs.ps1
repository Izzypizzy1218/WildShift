param(
    [Parameter(Mandatory=$true)][string]$ModRoot,
    [string]$GameRoot = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [switch]$WithoutOdyssey
)
$ErrorActionPreference = 'Stop'
# Read-only, source-level inspection. No XML PatchOperations, mod-setting changes,
# third-party C# Def mutations, or full RimWorld loader are simulated here.
if (-not ('WildShift.AnimalPool' -as [type])) { & "$PSScriptRoot/Run-LogicTests.ps1" -CompileOnly }
$named = @{}
$races = @{}
$kinds = [System.Collections.Generic.List[object]]::new()
function Read-Defs([string]$directory, [bool]$target) {
    if (-not (Test-Path -LiteralPath $directory)) { return }
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -Filter *.xml) {
        [xml]$doc = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($node in $doc.SelectNodes('/Defs/*')) {
            if ($node.HasAttribute('Name')) { $named[$node.GetAttribute('Name')] = $node }
            if ($node.LocalName -eq 'ThingDef' -and $node.SelectSingleNode('defName')) {
                $races[$node.defName] = $node
            }
            if ($target -and $node.LocalName -eq 'PawnKindDef' -and $node.SelectSingleNode('defName')) {
                $kinds.Add([pscustomobject]@{ Node=$node; File=$file.FullName })
            }
        }
    }
}
function Get-Chain($node) {
    $seen = [System.Collections.Generic.HashSet[string]]::new()
    while ($null -ne $node) {
        $node
        $parent = $node.GetAttribute('ParentName')
        if (-not $parent) { break }
        if (-not $seen.Add($parent)) { throw "Cyclic XML inheritance: $parent" }
        if (-not $named.ContainsKey($parent)) { throw "Unresolved XML parent: $parent" }
        $node = $named[$parent]
    }
}
function Get-Scalar($chain, [string]$xpath, [string]$fallback) {
    foreach ($node in $chain) {
        $value = $node.SelectSingleNode($xpath)
        if ($null -ne $value) { return $value.InnerText }
        $container = $node.SelectSingleNode($xpath.Split('/')[0])
        if ($container -and $container.GetAttribute('Inherit') -eq 'False') { break }
    }
    return $fallback
}
function Get-CompClasses($chain) {
    foreach ($node in $chain) {
        foreach ($attribute in $node.SelectNodes('comps/li/@Class')) { $attribute.Value }
        $container = $node.SelectSingleNode('comps')
        if ($container -and $container.GetAttribute('Inherit') -eq 'False') { break }
    }
}
Read-Defs (Join-Path $GameRoot 'Data/Core/Defs') $false
# These four packs' core 1.6 definitions are self-contained apart from Core bases.
# Optional compatibility subfolders and conditional patches are intentionally excluded.
Read-Defs (Join-Path $ModRoot 'Defs') $true
Read-Defs (Join-Path $ModRoot '1.6/Defs') $true
if ($WithoutOdyssey) { Read-Defs (Join-Path $ModRoot '1.6NotOdyssey/Defs') $true }
foreach ($item in $kinds) {
    $kindChain = @(Get-Chain $item.Node)
    $raceName = Get-Scalar $kindChain 'race' ''
    if (-not $races.ContainsKey($raceName)) { throw "Unresolved race: $raceName" }
    $raceChain = @(Get-Chain $races[$raceName])
    $intelligence = Get-Scalar $raceChain 'race/intelligence' 'Animal'
    $flesh = Get-Scalar $raceChain 'race/fleshType' 'Normal'
    # The reviewed definitions use these organic flesh types. Do not silently
    # misclassify future mechanical/Anomaly/custom flesh types as animals.
    if ($flesh -notin @('Normal', 'Insectoid', 'AA_HumanMeat')) {
        throw "Unreviewed FleshType $flesh in $raceName; inspect isOrganic and IsAnomalyEntity before extending this fixture."
    }
    $props = [Verse.RaceProperties]::new()
    $props.Animal = $intelligence -eq 'Animal'
    $props.Humanlike = $intelligence -eq 'Humanlike'
    $props.IsMechanoid = $flesh -eq 'Mechanoid'
    $props.predator = (Get-Scalar $raceChain 'race/predator' 'false') -eq 'true'
    $props.baseBodySize = [float]::Parse((Get-Scalar $raceChain 'race/baseBodySize' '1'), [cultureinfo]::InvariantCulture)
    $props.FleshType = if ($flesh -eq 'Insectoid') { [RimWorld.FleshTypeDefOf]::Insectoid } else { $flesh }
    $race = [Verse.ThingDef]::new(); $race.defName=$raceName; $race.race=$props
    $kind = [Verse.PawnKindDef]::new(); $kind.defName=$item.Node.defName; $kind.race=$race
    [WildShift.WildShiftMod]::Settings.allowInsectoids=$false
    $eligible = [WildShift.AnimalPool]::IsEligible($kind)
    [WildShift.WildShiftMod]::Settings.allowInsectoids=$true
    $withInsects = [WildShift.AnimalPool]::IsEligible($kind)
    [pscustomobject]@{
        Kind=$kind.defName
        Label=(Get-Scalar $kindChain 'label' (Get-Scalar $raceChain 'label' $kind.defName))
        Race=$raceName
        BodySize=$props.baseBodySize
        Predator=$props.predator
        Flesh=$flesh
        Eligible=$eligible
        WithInsectoids=$withInsects
        PawnClass=(Get-Scalar $raceChain 'thingClass' 'Pawn')
        Comps=(@(Get-CompClasses $raceChain | Sort-Object -Unique) -join ';')
        Source=$item.File
    }
}
