[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference='Stop'
$Output=[IO.Path]::GetFullPath($Output)
$repo=[IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)).TrimEnd('\','/')
if ((Test-Path -LiteralPath $Output) -or $Output.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use a fresh external output directory.'
}
$null=New-Item -ItemType Directory -Path $Output
$comparator=Join-Path $PSScriptRoot 'Compare-UpgradeSave.ps1'
$script:assertions=0
function Assert([bool]$ok,[string]$label) { if(!$ok){throw $label}; $script:assertions++ }
$sourceText=@'
<savegame><meta><gameVersion>1.7.test</gameVersion></meta><game><tickManager><ticksGame>200</ticksGame></tickManager><maps><li><things>
<thing Class="Pawn"><def>Cow</def><id>Cow2</id><faction>Faction_1</faction><kindDef>Cow</kindDef><healthTracker><hediffSet><hediffs>
<li Class="HediffWithComps"><def>WildShift_Transformed</def><loadID>2</loadID><severity>0.5</severity><ageTicks>3</ageTicks><storedPawns><innerList>
<li><def>Human</def><id>Human1</id><faction>Faction_1</faction><kindDef>Colonist</kindDef><healthTracker><hediffSet><hediffs>
<li Class="HediffWithComps"><def>WildShift_Shapeshifter</def><loadID>1</loadID><severity>0.5</severity><assignedKind>Cow</assignedKind></li>
<li Class="Hediff_Injury"><def>Gunshot</def><loadID>4</loadID><severity>3.5</severity><ageTicks>9</ageTicks><part><body>Human</body><index>2</index></part></li>
</hediffs></hediffSet></healthTracker><apparel><wornApparel><innerList><li><def>Shirt</def><id>Shirt3</id><stuff>Cloth</stuff><stackCount>1</stackCount><health>50</health></li></innerList></wornApparel></apparel>
<skills><skills><li><def>Mining</def><level>4</level><xpSinceLastLevel>8999.99707</xpSinceLastLevel></li></skills></skills>
<genes><endogenes><li Class="Gene_Hemogen"><def>Hemogenic</def><loadID>11</loadID><cur>0.25</cur></li></endogenes></genes>
<abilities><abilities><li><def>FireSpew</def><Id>12</Id><inCooldown>True</inCooldown><cooldownEndTick>260</cooldownEndTick><charges>1</charges></li></abilities></abilities>
</li></innerList></storedPawns></li>
</hediffs></hediffSet></healthTracker><skills><skills><li><def>Mining</def><level>1</level></li></skills></skills></thing>
<thing Class="Corpse"><innerPawn><def>Human</def><id>HumanDead3</id><faction>Faction_1</faction><kindDef>Colonist</kindDef><healthTracker><healthState>Dead</healthState><hediffSet><hediffs><li Class="HediffWithComps"><def>WildShift_Shapeshifter</def><loadID>3</loadID><severity>0.5</severity><assignedKind>Cow</assignedKind></li></hediffs></hediffSet></healthTracker></innerPawn></thing>
</things></li></maps></game></savegame>
'@
$snapshotText=@'
<Snapshot tick="200"><Pawns>
<Pawn id="Thing_Cow2" def="Cow" kind="Cow" faction="Faction_1" dead="false" downed="false" original="Thing_Human1" cachedForm="" assignedKind="" generatedHealthSanitized="True">
<Health><Hediff id="Hediff_2" def="WildShift_Transformed" severity="0.5" age="3" part="-1"/><Hediff id="Hediff_100" def="WildShift_SharedFormDrugEffect" severity="0.5" age="0" part="-1"/></Health><Possessions/>
<Skills><Skill def="Mining" level="4" observedLevel="8" passion="None" xp="8999.997" dailyXp="0"/></Skills><Genes/>
<Abilities><Ability id="Ability_12" def="FireSpew" caster="Thing_Cow2" cooldown="60" charges="1"/></Abilities></Pawn>
<Pawn id="Thing_Human1" def="Human" kind="Colonist" faction="Faction_1" dead="false" downed="false" original="" cachedForm="" assignedKind="Cow" generatedHealthSanitized="">
<Health><Hediff id="Hediff_1" def="WildShift_Shapeshifter" severity="0.5" age="0" part="-1"/><Hediff id="Hediff_4" def="Gunshot" severity="3.5" age="9" part="2"/></Health>
<Possessions><Thing id="Thing_Shirt3" slot="apparel" def="Shirt" stuff="Cloth" count="1" hp="50"/></Possessions>
<Skills><Skill def="Mining" level="4" observedLevel="8" passion="None" xp="8999.997" dailyXp="0"/></Skills>
<Genes><Gene id="Gene_11" def="Hemogenic" active="true" resourceValue="0.25"/></Genes><Abilities><Ability id="Ability_12" def="FireSpew" caster="Thing_Cow2" cooldown="60" charges="1"/></Abilities></Pawn>
<Pawn id="Thing_HumanDead3" def="Human" kind="Colonist" faction="Faction_1" dead="true" downed="false" original="" cachedForm="" assignedKind="Cow" generatedHealthSanitized=""><Health><Hediff id="Hediff_3" def="WildShift_Shapeshifter" severity="0.5" age="0" part="-1"/></Health><Possessions/><Skills/><Genes/><Abilities/></Pawn>
</Pawns><Caravans/></Snapshot>
'@
function RunCase([string]$name,[string]$source,[string]$snapshot) {
    $case=Join-Path $Output $name; $null=New-Item -ItemType Directory -Path $case
    $save=Join-Path $case 'source.rws';$native=Join-Path $case 'snapshot.xml';$report=Join-Path $case 'report.json'
    [IO.File]::WriteAllText($save,$source,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($native,$snapshot,[Text.UTF8Encoding]::new($false))
    $null=& $comparator -SavePath $save -SnapshotPath $native -Output $report
    return (Get-Content -LiteralPath $report -Raw | ConvertFrom-Json)
}
$baseline=RunCase 'baseline' $sourceText $snapshotText
Assert $baseline.Passed 'Known native fields, float32 roundtrip, canonical skills, corpse and alias baseline must pass.'
Assert ($baseline.UnsupportedCount -eq 0) 'The explicit baseline has no ambiguous source defaults.'
Assert (@($baseline.IntentionalMappings | Where-Object Reason -Like '*original Thing_Human1*').Count -gt 0) 'Animal skills use original human records.'
Assert (@($baseline.IntentionalMappings | Where-Object Reason -Like '*projection*').Count -gt 0) 'A disposable projection is explicitly allowed.'

$mutated=[xml]$snapshotText
$node=$mutated.SelectSingleNode('//Pawn[@id="Thing_Human1"]/Health/Hediff[@id="Hediff_4"]');$null=$node.ParentNode.RemoveChild($node)
$result=RunCase 'lost-hediff' $sourceText $mutated.OuterXml
Assert (!$result.ComparedFieldsPassed -and @($result.Mismatches | Where-Object Field -EQ 'Hediff_4.Present').Count -eq 1) 'An old missing injury is detected.'

$mutated=[xml]$snapshotText
$node=$mutated.SelectSingleNode('//Possessions/Thing');$null=$node.ParentNode.RemoveChild($node)
$result=RunCase 'lost-item' $sourceText $mutated.OuterXml
Assert (!$result.ComparedFieldsPassed -and @($result.Mismatches | Where-Object Category -EQ 'Possessions').Count -gt 0) 'An original clothing item cannot silently disappear.'

$mutated=[xml]$snapshotText
$node=$mutated.SelectSingleNode('//Pawn[@id="Thing_HumanDead3"]');$null=$node.ParentNode.RemoveChild($node)
$result=RunCase 'missing-corpse' $sourceText $mutated.OuterXml
Assert (!$result.Passed -and @($result.Mismatches | Where-Object Pawn -EQ 'Thing_HumanDead3').Count -eq 1) 'An unenumerated dead original is a missing-pawn failure.'

$mutated=[xml]$snapshotText
$mutated.SelectSingleNode('//Pawn[@id="Thing_Human1"]/Skills/Skill').SetAttribute('level','5')
$result=RunCase 'changed-raw-level' $sourceText $mutated.OuterXml
Assert (!$result.Passed -and @($result.Mismatches | Where-Object Field -EQ 'Mining.RawLevel').Count -eq 1) 'Raw skill changes are caught independently of observed aptitude level.'

$mutated=[xml]$snapshotText
$mutated.SelectSingleNode('//Pawn[@id="Thing_Human1"]/Abilities/Ability').SetAttribute('cooldown','59')
$result=RunCase 'one-tick-cooldown' $sourceText $mutated.OuterXml
Assert (!$result.Passed -and @($result.Mismatches | Where-Object Field -EQ 'Ability_12.CooldownRemaining').Count -eq 1) 'A one-tick cooldown mutation is not rounded away.'

$mutated=[xml]$sourceText
$node=$mutated.SelectSingleNode('//abilities/abilities/li/charges');$null=$node.ParentNode.RemoveChild($node)
$result=RunCase 'omitted-charges' $mutated.OuterXml $snapshotText
Assert (!$result.Passed -and $result.ComparedFieldsPassed) 'Ambiguous charges block full comparison, without discarding other passing fields.'
Assert (@($result.UnsupportedComparisons | Where-Object Field -EQ 'Ability_12.Charges').Count -eq 1) 'Omitted charges are reported rather than inferred from current defs.'

$mutated=[xml]$sourceText
$node=$mutated.SelectSingleNode('//genes/endogenes/li/cur');$null=$node.ParentNode.RemoveChild($node)
$result=RunCase 'omitted-resource' $mutated.OuterXml $snapshotText
Assert (!$result.Passed -and @($result.UnsupportedComparisons | Where-Object Field -EQ 'Gene_11.Resource').Count -eq 1) 'Omitted resource values are explicit unsupported comparisons.'

$mutated=[xml]$snapshotText
$mutated.SelectSingleNode('//Pawn[@id="Thing_Human1"]/Genes/Gene').SetAttribute('resourceValue','0.250001')
$result=RunCase 'within-float-tolerance' $sourceText $mutated.OuterXml
Assert $result.Passed 'An actual native float difference within 1e-5 is accepted.'

$mutated=[xml]$snapshotText
$mutated.SelectSingleNode('//Pawn[@id="Thing_Human1"]/Genes/Gene').SetAttribute('resourceValue','0.26')
$result=RunCase 'changed-resource' $sourceText $mutated.OuterXml
Assert (!$result.Passed -and @($result.Mismatches | Where-Object Field -EQ 'Gene_11.Resource').Count -eq 1) 'A material resource change is detected.'

$rejected=$false
try { $null=& $comparator -SavePath (Join-Path $Output 'baseline/source.rws') -SnapshotPath (Join-Path $Output 'baseline/snapshot.xml') -Output (Join-Path $Output 'baseline/report.json') }
catch { $rejected=$true }
Assert $rejected 'A previous report cannot be overwritten.'
$summary=[pscustomobject]@{Passed=$true;Assertions=$script:assertions;Scope='Synthetic XML comparator regression only; no game or production path is mocked or executed.';Output=$Output}
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Output 'result.json') -Encoding utf8
$summary
