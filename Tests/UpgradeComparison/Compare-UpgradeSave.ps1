<#
.SYNOPSIS
Read-only comparison of original RWS state and a paused UpgradeProbe snapshot.
.DESCRIPTION
Writes one fresh external JSON report. It never starts a game, repairs a save,
normalizes input files or claims equality of arbitrary custom serialized comps.
Missing/ambiguous fields are reported rather than inferred from current defs.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$SavePath,
    [Parameter(Mandatory=$true)][string]$SnapshotPath,
    [Parameter(Mandatory=$true)][string]$Output,
    [ValidateRange(0,1)][double]$FloatTolerance=0.00001
)
$ErrorActionPreference='Stop'
$SavePath=[IO.Path]::GetFullPath($SavePath)
$SnapshotPath=[IO.Path]::GetFullPath($SnapshotPath)
$Output=[IO.Path]::GetFullPath($Output)
$repo=[IO.Path]::GetFullPath((Split-Path (Split-Path $PSScriptRoot -Parent) -Parent)).TrimEnd('\','/')
if (Test-Path -LiteralPath $Output) { throw 'Output must be a fresh external file.' }
if ($Output.Equals($SavePath,[StringComparison]::OrdinalIgnoreCase) -or
    $Output.Equals($SnapshotPath,[StringComparison]::OrdinalIgnoreCase) -or
    $Output.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output cannot replace an input or be inside the source repository.'
}
foreach($path in @($SavePath,$SnapshotPath)) { if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing input: $path" } }
$saveHash=(Get-FileHash -LiteralPath $SavePath -Algorithm SHA256).Hash
$snapshotHash=(Get-FileHash -LiteralPath $SnapshotPath -Algorithm SHA256).Hash
$script:passed=0
$script:counts=[ordered]@{}
$script:mismatches=[Collections.Generic.List[object]]::new()
$script:unsupported=[Collections.Generic.List[object]]::new()
$script:notes=[Collections.Generic.List[object]]::new()
$culture=[Globalization.CultureInfo]::InvariantCulture

function LoadXml([string]$path) {
    $settings=[Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver=$null
    $reader=[Xml.XmlReader]::Create($path,$settings)
    try { $document=[Xml.XmlDocument]::new(); $document.XmlResolver=$null; $document.Load($reader); return ,$document }
    finally { $reader.Dispose() }
}
function Field($node,[string]$path,[string]$default='') {
    if ($null -eq $node) { return $default }
    $value=$node.SelectSingleNode($path)
    if ($null -eq $value -or $value.GetAttribute('IsNull') -eq 'True') { return $default }
    return $value.InnerText
}
function LoadId([string]$raw,[string]$prefix) {
    if (!$raw -or $raw -eq 'null') { return '' }
    if ($raw.StartsWith($prefix,[StringComparison]::Ordinal)) { return $raw }
    return $prefix+$raw
}
function Unsupported([string]$category,[string]$pawn,[string]$field,[string]$reason) {
    $script:unsupported.Add([pscustomobject]@{Category=$category;Pawn=$pawn;Field=$field;Reason=$reason})
}
function Note([string]$pawn,[string]$reason) { $script:notes.Add([pscustomobject]@{Pawn=$pawn;Reason=$reason}) }
function Check([string]$category,[string]$pawn,[string]$field,$expected,$actual,[switch]$Numeric,[switch]$Float32) {
    $ok=$false
    if ($Numeric) {
        $left=0.0; $right=0.0
        $validLeft=[double]::TryParse([string]$expected,[Globalization.NumberStyles]::Float,$culture,[ref]$left)
        $validRight=[double]::TryParse([string]$actual,[Globalization.NumberStyles]::Float,$culture,[ref]$right)
        if (!$validLeft -or !$validRight -or [double]::IsNaN($left) -or [double]::IsNaN($right) -or
            [double]::IsInfinity($left) -or [double]::IsInfinity($right)) {
            Unsupported $category $pawn $field 'A numeric source/snapshot value is missing, invalid or non-finite.'
            return
        }
        # Native severity/XP/resource fields are System.Single. Mono's saved
        # decimal text and .NET's shortest R text can differ while representing
        # the exact same binary float. Do not apply this to tick/count integers.
        if ($Float32) {
            try { $left=[double][single]$left; $right=[double][single]$right }
            catch { Unsupported $category $pawn $field 'Value cannot be represented by the native System.Single field.'; return }
            if ([double]::IsInfinity($left) -or [double]::IsInfinity($right)) {
                Unsupported $category $pawn $field 'Value overflows the native System.Single field.'
                return
            }
        }
        $ok=[Math]::Abs($left-$right) -le $FloatTolerance
    } else { $ok=[string]::Equals([string]$expected,[string]$actual,[StringComparison]::Ordinal) }
    if (!$script:counts.Contains($category)) { $script:counts[$category]=[ordered]@{Passed=0;Mismatches=0} }
    if ($ok) { $script:passed++; $script:counts[$category].Passed++ }
    else {
        $script:counts[$category].Mismatches++
        $script:mismatches.Add([pscustomobject]@{Category=$category;Pawn=$pawn;Field=$field;Expected=$expected;Actual=$actual})
    }
}
function AttrCheck([string]$category,[string]$pawn,[string]$field,$expected,$node,[string]$attribute,[switch]$Numeric) {
    if ($null -eq $node -or !$node.HasAttribute($attribute)) {
        Unsupported $category $pawn $field "Native snapshot attribute '$attribute' is unavailable."
        return
    }
    $floatAttribute=$Numeric -and $attribute -in @('severity','xp','dailyXp','resourceValue')
    Check $category $pawn $field $expected ($node.GetAttribute($attribute)) -Numeric:$Numeric -Float32:$floatAttribute
}
function NodeMap($nodes,[string]$attribute,[string]$pawn,[string]$category) {
    $map=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach($node in $nodes) {
        $id=$node.GetAttribute($attribute)
        if (!$id) { Unsupported $category $pawn $attribute 'Native entry has no stable key.'; continue }
        if ($map.ContainsKey($id)) { Check $category $pawn ($id+'.UniqueKey') 'one' 'duplicate'; continue }
        $map.Add($id,$node)
    }
    return ,$map
}
function SourceThings($pawn) {
    $result=[Collections.Generic.List[object]]::new()
    foreach($spec in @(
        @('apparel','apparel/wornApparel/innerList/li'),
        @('equipment','equipment/equipment/innerList/li'),
        @('inventory','inventory/innerContainer/innerList/li'),
        @('carried','carryTracker/innerContainer/innerList/li'))) {
        foreach($thing in $pawn.SelectNodes($spec[1])) { $result.Add([pscustomobject]@{Slot=$spec[0];Node=$thing}) }
    }
    return ,$result
}

$source=LoadXml $SavePath
$snapshot=LoadXml $SnapshotPath
if ($source.DocumentElement.LocalName -ne 'savegame' -or $snapshot.DocumentElement.LocalName -ne 'Snapshot') {
    throw 'Expected a native savegame document and an UpgradeProbe Snapshot document.'
}
$sourcePawns=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
foreach($hediff in $source.SelectNodes('//healthTracker/hediffSet/hediffs/li[def="WildShift_Shapeshifter" or def="WildShift_Transformed"]')) {
    $pawn=$hediff.ParentNode.ParentNode.ParentNode.ParentNode
    $id=LoadId (Field $pawn 'id') 'Thing_'
    if (!$id) { Unsupported 'Identity' '' 'SourcePawnId' 'Relevant source pawn has no serialized id.'; continue }
    if ($sourcePawns.ContainsKey($id) -and ![object]::ReferenceEquals($sourcePawns[$id],$pawn)) {
        Check 'Identity' $id 'SourceUniquePawn' 'one' 'duplicate serialized objects'
    } else { $sourcePawns[$id]=$pawn }
    foreach($held in $hediff.SelectNodes('storedPawns/innerList/li|animalFormStorage/forms/innerList/li')) {
        $heldId=LoadId (Field $held 'id') 'Thing_'
        if ($heldId -and !$sourcePawns.ContainsKey($heldId)) { $sourcePawns[$heldId]=$held }
    }
}
if ($sourcePawns.Count -eq 0) { throw 'No relevant original/form pawn exists in the source save.' }
$nativePawns=NodeMap ($snapshot.SelectNodes('/Snapshot/Pawns/Pawn')) 'id' '' 'Identity'
$sourceTick=Field $source.DocumentElement 'game/tickManager/ticksGame'
$snapshotTick=$snapshot.DocumentElement.GetAttribute('tick')
Check 'Clock' '' 'PausedNativeTicks' $sourceTick $snapshotTick
$originalFor=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
foreach($entry in $sourcePawns.GetEnumerator()) {
    $held=@($entry.Value.SelectNodes('healthTracker/hediffSet/hediffs/li[def="WildShift_Transformed"]/storedPawns/innerList/li'))
    if ($held.Count -gt 1) { Unsupported 'Ownership' $entry.Key 'StoredOriginal' 'More than one stored original is serialized.' }
    if ($held.Count -eq 1) { $originalFor[$entry.Key]=LoadId (Field $held[0] 'id') 'Thing_' }
}

foreach($id in @($sourcePawns.Keys | Sort-Object)) {
    $pawn=$sourcePawns[$id]
    if (!$nativePawns.ContainsKey($id)) {
        Check 'Identity' $id 'PawnPresent' 'present' 'missing (including dead/corpse originals)'
        continue
    }
    $actual=$nativePawns[$id]
    Check 'Identity' $id 'PawnPresent' 'present' 'present'
    AttrCheck 'Identity' $id 'Race' (Field $pawn 'def') $actual 'def'
    AttrCheck 'Identity' $id 'Kind' (Field $pawn 'kindDef') $actual 'kind'
    AttrCheck 'Identity' $id 'Faction' (LoadId (Field $pawn 'faction') '') $actual 'faction'
    $state=Field $pawn 'healthTracker/healthState' 'Mobile'
    AttrCheck 'Identity' $id 'Dead' ($state -eq 'Dead').ToString().ToLowerInvariant() $actual 'dead'
    AttrCheck 'Identity' $id 'Downed' ($state -eq 'Down').ToString().ToLowerInvariant() $actual 'downed'
    $shifter=$pawn.SelectSingleNode('healthTracker/hediffSet/hediffs/li[def="WildShift_Shapeshifter"]')
    if ($shifter) {
        AttrCheck 'Ownership' $id 'AssignedKind' (Field $shifter 'assignedKind') $actual 'assignedKind'
        $cache=@($shifter.SelectNodes('animalFormStorage/forms/innerList/li'))
        if ($cache.Count -gt 1) { Unsupported 'Ownership' $id 'CachedForm' 'More than one cached form is serialized.' }
        $expectedCache=if($cache.Count){LoadId (Field $cache[0] 'id') 'Thing_'}else{''}
        AttrCheck 'Ownership' $id 'CachedForm' $expectedCache $actual 'cachedForm'
        if (!$shifter.SelectSingleNode('animalFormStorage')) { Note $id 'An absent old animalFormStorage may initialize empty; no cached body is invented for source comparison.' }
    }
    $transformed=$pawn.SelectSingleNode('healthTracker/hediffSet/hediffs/li[def="WildShift_Transformed"]')
    if ($transformed) {
        $expectedOriginal=if($originalFor.ContainsKey($id)){$originalFor[$id]}else{''}
        AttrCheck 'Ownership' $id 'StoredOriginal' $expectedOriginal $actual 'original'
        $oldFlag=Field $transformed 'generatedHealthSanitized'
        AttrCheck 'Ownership' $id 'SanitizedFlagAfterPostLoad' 'True' $actual 'generatedHealthSanitized'
        if ($oldFlag -ne 'True') { Note $id 'The old missing/false sanitation flag is initialized; all preexisting health entries are still independently compared.' }
    }

    $actualHealth=NodeMap ($actual.SelectNodes('Health/Hediff')) 'id' $id 'Health'
    $sourceHealth=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($hediff in $pawn.SelectNodes('healthTracker/hediffSet/hediffs/li')) {
        $healthId=LoadId (Field $hediff 'loadID') 'Hediff_'
        if (!$healthId) { Unsupported 'Health' $id 'SourceHediffId' ('Missing id for '+(Field $hediff 'def')); continue }
        $null=$sourceHealth.Add($healthId)
        if (!$actualHealth.ContainsKey($healthId)) {
            $moved=$null
            if ($originalFor.ContainsKey($id) -and $nativePawns.ContainsKey($originalFor[$id])) {
                $moved=$nativePawns[$originalFor[$id]].SelectSingleNode('Health/Hediff[@id="'+$healthId+'"]')
            }
            if ($moved) { Unsupported 'Health' $id $healthId 'Source health object moved to the linked original; ownership migration needs explicit review, not a disappearance pass.' }
            else { Check 'Health' $id ($healthId+'.Present') 'present' 'missing' }
            continue
        }
        $native=$actualHealth[$healthId]
        AttrCheck 'Health' $id ($healthId+'.Def') (Field $hediff 'def') $native 'def'
        AttrCheck 'Health' $id ($healthId+'.Age') (Field $hediff 'ageTicks' '0') $native 'age' -Numeric
        $severity=$hediff.SelectSingleNode('severity')
        if ($severity) { AttrCheck 'Health' $id ($healthId+'.Severity') $severity.InnerText $native 'severity' -Numeric }
        else { Unsupported 'Health' $id ($healthId+'.Severity') 'Source severity omitted; current definition/default severity is not inferred.' }
        $part=$hediff.SelectSingleNode('part')
        if (!$part -or $part.GetAttribute('IsNull') -eq 'True') { AttrCheck 'Health' $id ($healthId+'.PartIndex') '-1' $native 'part' -Numeric }
        elseif ($part.SelectSingleNode('index')) { AttrCheck 'Health' $id ($healthId+'.PartIndex') (Field $part 'index') $native 'part' -Numeric }
        else { Unsupported 'Health' $id ($healthId+'.PartIndex') 'Serialized part has no index; body-name/path mapping cannot be inferred.' }
        if ($hediff.SelectSingleNode('ticksToDisappear')) { AttrCheck 'Health' $id ($healthId+'.DisappearsTicks') (Field $hediff 'ticksToDisappear') $native 'disappearsTicks' -Numeric }
    }
    foreach($healthId in $actualHealth.Keys) {
        if ($sourceHealth.Contains($healthId)) { continue }
        $extra=$actualHealth[$healthId]
        if ($extra.GetAttribute('def') -eq 'WildShift_SharedFormDrugEffect' -and $originalFor.ContainsKey($id)) {
            Note $id ('Allowed new disposable active-animal projection '+$healthId)
        } else { Unsupported 'Health' $id $healthId ('Additional native health entry: '+$extra.GetAttribute('def')+'; no source state is silently substituted.') }
    }

    $actualThings=NodeMap ($actual.SelectNodes('Possessions/Thing')) 'id' $id 'Possessions'
    $sourceThingIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($entry in (SourceThings $pawn)) {
        $thing=$entry.Node; $thingId=LoadId (Field $thing 'id') 'Thing_'
        if (!$thingId) { Unsupported 'Possessions' $id $entry.Slot 'Source thing has no stable id.'; continue }
        $null=$sourceThingIds.Add($thingId)
        if (!$actualThings.ContainsKey($thingId)) { Check 'Possessions' $id ($thingId+'.Present') 'present in '+$entry.Slot 'missing'; continue }
        $native=$actualThings[$thingId]
        AttrCheck 'Possessions' $id ($thingId+'.Slot') $entry.Slot $native 'slot'
        AttrCheck 'Possessions' $id ($thingId+'.Def') (Field $thing 'def') $native 'def'
        AttrCheck 'Possessions' $id ($thingId+'.Stuff') (Field $thing 'stuff') $native 'stuff'
        AttrCheck 'Possessions' $id ($thingId+'.Count') (Field $thing 'stackCount' '1') $native 'count' -Numeric
        if ($thing.SelectSingleNode('health')) { AttrCheck 'Possessions' $id ($thingId+'.HP') (Field $thing 'health') $native 'hp' -Numeric }
        else { Unsupported 'Possessions' $id ($thingId+'.HP') 'Source hit points omitted; current MaxHitPoints/default is not inferred.' }
    }
    foreach($thingId in $actualThings.Keys) { if (!$sourceThingIds.Contains($thingId)) { Unsupported 'Possessions' $id $thingId 'Additional native possession has no corresponding source item in this pawn.' } }

    $skillsOwner=$pawn
    if ($originalFor.ContainsKey($id)) {
        if ($sourcePawns.ContainsKey($originalFor[$id])) { $skillsOwner=$sourcePawns[$originalFor[$id]]; Note $id ('Active-animal skill interface intentionally uses original '+$originalFor[$id]+'; old animal raw records are not treated as canonical.') }
        else { Unsupported 'Skills' $id 'CanonicalOriginal' 'Stored original source node unavailable.' }
    }
    $actualSkills=NodeMap ($actual.SelectNodes('Skills/Skill')) 'def' $id 'Skills'
    $sourceSkillDefs=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($skill in $skillsOwner.SelectNodes('skills/skills/li')) {
        $def=Field $skill 'def'
        $null=$sourceSkillDefs.Add($def)
        if (!$actualSkills.ContainsKey($def)) { Check 'Skills' $id ($def+'.Present') 'present' 'missing'; continue }
        $native=$actualSkills[$def]
        AttrCheck 'Skills' $id ($def+'.RawLevel') (Field $skill 'level' '0') $native 'level' -Numeric
        AttrCheck 'Skills' $id ($def+'.Passion') (Field $skill 'passion' 'None') $native 'passion'
        AttrCheck 'Skills' $id ($def+'.XP') (Field $skill 'xpSinceLastLevel' '0') $native 'xp' -Numeric
        AttrCheck 'Skills' $id ($def+'.DailyXP') (Field $skill 'xpSinceMidnight' '0') $native 'dailyXp' -Numeric
    }
    foreach($def in $actualSkills.Keys) { if (!$sourceSkillDefs.Contains($def)) { Unsupported 'Skills' $id $def 'Additional native skill has no source canonical record.' } }

    $actualGenes=NodeMap ($actual.SelectNodes('Genes/Gene')) 'id' $id 'Genes'
    $sourceGeneIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($gene in $pawn.SelectNodes('genes/xenogenes/li|genes/endogenes/li')) {
        $geneId=LoadId (Field $gene 'loadID') 'Gene_'
        if (!$geneId) { Unsupported 'Genes' $id 'SourceGeneId' 'Source gene has no stable id.'; continue }
        $null=$sourceGeneIds.Add($geneId)
        if (!$actualGenes.ContainsKey($geneId)) { Check 'Genes' $id ($geneId+'.Present') 'present' 'missing'; continue }
        $native=$actualGenes[$geneId]
        AttrCheck 'Genes' $id ($geneId+'.Def') (Field $gene 'def') $native 'def'
        if ($native.GetAttribute('resourceValue') -ne '') {
            $resource=$gene.SelectSingleNode('cur')
            if ($resource) { AttrCheck 'Genes' $id ($geneId+'.Resource') $resource.InnerText $native 'resourceValue' -Numeric }
            else { Unsupported 'Genes' $id ($geneId+'.Resource') 'Native gene is a resource but serialized cur is omitted; its Scribe/default or custom initial value is not inferred.' }
        }
    }
    foreach($geneId in $actualGenes.Keys) { if (!$sourceGeneIds.Contains($geneId)) { Unsupported 'Genes' $id $geneId 'Additional native gene has no source ID; definition regeneration is not a preservation pass.' } }

    $actualAbilities=NodeMap ($actual.SelectNodes('Abilities/Ability')) 'id' $id 'Abilities'
    $sourceAbilityIds=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($ability in $pawn.SelectNodes('abilities/abilities/li')) {
        $abilityId=LoadId (Field $ability 'Id') 'Ability_'
        if (!$abilityId) { Unsupported 'Abilities' $id 'SourceAbilityId' 'Source ability has no native serialized Id.'; continue }
        $null=$sourceAbilityIds.Add($abilityId)
        if (!$actualAbilities.ContainsKey($abilityId)) { Check 'Abilities' $id ($abilityId+'.Present') 'present' 'missing'; continue }
        $native=$actualAbilities[$abilityId]
        AttrCheck 'Abilities' $id ($abilityId+'.Def') (Field $ability 'def') $native 'def'
        $inCooldown=Field $ability 'inCooldown' 'False'
        if ($inCooldown -eq 'True' -and !$ability.SelectSingleNode('cooldownEndTick')) {
            Unsupported 'Abilities' $id ($abilityId+'.Cooldown') 'inCooldown is true but cooldownEndTick is absent.'
        } else {
            $remaining=0L
            if ($inCooldown -eq 'True') { $remaining=[Math]::Max(0L,([long](Field $ability 'cooldownEndTick'))-[long]$snapshotTick) }
            AttrCheck 'Abilities' $id ($abilityId+'.CooldownRemaining') ([string]$remaining) $native 'cooldown' -Numeric
        }
        if ($ability.SelectSingleNode('charges')) { AttrCheck 'Abilities' $id ($abilityId+'.Charges') (Field $ability 'charges') $native 'charges' -Numeric }
        else { Unsupported 'Abilities' $id ($abilityId+'.Charges') 'Serialized charges omitted; current AbilityDef maximum/default charges are not inferred.' }
    }
    foreach($abilityId in $actualAbilities.Keys) {
        if ($sourceAbilityIds.Contains($abilityId)) { continue }
        $alias=$null
        if ($originalFor.ContainsKey($id) -and $nativePawns.ContainsKey($originalFor[$id])) {
            $alias=$nativePawns[$originalFor[$id]].SelectSingleNode('Abilities/Ability[@id="'+$abilityId+'"]')
        }
        if ($alias) {
            $native=$actualAbilities[$abilityId]
            AttrCheck 'Abilities' $id ($abilityId+'.BorrowedDef') $alias.GetAttribute('def') $native 'def'
            AttrCheck 'Abilities' $id ($abilityId+'.BorrowedCooldown') $alias.GetAttribute('cooldown') $native 'cooldown' -Numeric
            AttrCheck 'Abilities' $id ($abilityId+'.BorrowedCharges') $alias.GetAttribute('charges') $native 'charges' -Numeric
            AttrCheck 'Abilities' $id ($abilityId+'.BorrowedCaster') $id $native 'caster'
            Note $id ('Allowed borrowed canonical original ability alias '+$abilityId)
        } else { Unsupported 'Abilities' $id $abilityId 'Additional native ability is not a linked-original alias.' }
    }
}
foreach($id in $nativePawns.Keys) { if (!$sourcePawns.ContainsKey($id)) { Unsupported 'Identity' $id 'AdditionalRelevantPawn' 'Native snapshot contains a relevant pawn not serialized in the original graph.' } }
Check 'Inputs' '' 'SourceSaveUnchanged' $saveHash ((Get-FileHash -LiteralPath $SavePath -Algorithm SHA256).Hash)
Check 'Inputs' '' 'SnapshotUnchanged' $snapshotHash ((Get-FileHash -LiteralPath $SnapshotPath -Algorithm SHA256).Hash)
$report=[ordered]@{
    Completed=$true
    Passed=($script:mismatches.Count -eq 0 -and $script:unsupported.Count -eq 0)
    ComparedFieldsPassed=($script:mismatches.Count -eq 0)
    ComparedFieldsComplete=($script:unsupported.Count -eq 0)
    SavePath=$SavePath; SaveSHA256=$saveHash; SavedGameVersion=(Field $source.DocumentElement 'meta/gameVersion')
    SnapshotPath=$SnapshotPath; SnapshotSHA256=$snapshotHash; SourceTick=$sourceTick; SnapshotTick=$snapshotTick
    SourceRelevantPawnCount=$sourcePawns.Count; NativeRelevantPawnCount=$nativePawns.Count
    PassedChecks=$script:passed; MismatchCount=$script:mismatches.Count; UnsupportedCount=$script:unsupported.Count
    FloatTolerance=$FloatTolerance; FloatSemantics='Known native float fields severity/xp/dailyXp/resourceValue are parsed as System.Single before absolute tolerance; ticks/counts/indices/raw levels are not rounded to Single.'
    Counts=$script:counts; Mismatches=@($script:mismatches); UnsupportedComparisons=@($script:unsupported); IntentionalMappings=@($script:notes)
    Scope='Original serialized source -> native paused phase-0 snapshot; exact relevant pawn/owner, health ID/def/index/severity/age, item identity/slot/def/stuff/count/hp, canonical raw skills, gene IDs/defs/explicit cur and ability IDs/relative cooldowns/explicit charges. Missing dead rows fail. No values are repaired or skipped as a preservation pass.'
    SourceDefaultsUsed=@('omitted native healthState=Mobile','omitted skill level/XP/dailyXP=0 and passion=None','omitted Hediff ageTicks=0','absent whole-body part=-1','omitted item stackCount=1','omitted ability inCooldown=False')
    ExcludedScope=@('arbitrary custom comp or gene fields','Hediff custom payloads and private component timers beyond explicit ticksToDisappear','relations/memories/needs/ideology and other trackers not exported by the probe','quality/style/color and other item comps','native body-definition/anatomy equivalence beyond serialized part index','Gene.Active/override execution semantics','maxCharges and other ability component fields not exported by the probe','post-cycle snapshots with intentional wound reset and item unloading')
}
$parent=Split-Path $Output -Parent
if (!(Test-Path -LiteralPath $parent)) { $null=New-Item -ItemType Directory -Path $parent }
$file=[IO.File]::Open($Output,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try { $writer=[IO.StreamWriter]::new($file,[Text.UTF8Encoding]::new($false)); try { $writer.Write(($report | ConvertTo-Json -Depth 12)) } finally { $writer.Dispose() } } finally { $file.Dispose() }
[pscustomobject]@{Completed=$true;Passed=$report.Passed;ComparedFieldsPassed=$report.ComparedFieldsPassed;PassedChecks=$report.PassedChecks;Mismatches=$report.MismatchCount;Unsupported=$report.UnsupportedCount;Report=$Output}
