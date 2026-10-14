param(
    [string]$Managed = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed',
    [string]$VefAssembly = ''
)
$ErrorActionPreference = 'Stop'
# Run under Windows PowerShell/.NET Framework. Reflection-only: do not execute
# game static constructors, Unity native calls or the mod's Harmony patches.
$resolve = [System.ResolveEventHandler] {
    param($sender, $eventArgs)
    $name = [System.Reflection.AssemblyName]::new($eventArgs.Name).Name
    $path = Join-Path $Managed ($name + '.dll')
    if (Test-Path -LiteralPath $path) { return [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($path) }
    return [System.Reflection.Assembly]::ReflectionOnlyLoad($eventArgs.Name)
}
[AppDomain]::CurrentDomain.add_ReflectionOnlyAssemblyResolve($resolve)
try {
    $game = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom((Join-Path $Managed 'Assembly-CSharp.dll'))
    $flags = [System.Reflection.BindingFlags]'Public,NonPublic,Instance,Static'
    if ($VefAssembly) {
        $vef = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($VefAssembly)
        $type = $vef.GetType('VEF.AnimalBehaviours.CompFixedGender',$true)
        $method = $type.GetMethod('CompTick',($flags -bor [System.Reflection.BindingFlags]::DeclaredOnly))
        $field = $type.GetField('changeGenderOnce',$flags)
        if ($null -eq $method -or $method.GetParameters().Count -ne 0 -or $method.ReturnType.FullName -ne 'System.Void' -or $null -eq $field -or $field.FieldType.FullName -ne 'System.Boolean' -or $field.IsStatic -or $field.IsInitOnly) {
            throw 'Installed VEF fixed-gender API changed'
        }
        'PASS: optional installed VEF CompFixedGender method and writable bool field (metadata only).'
    }
    $methods = @(
        @('RimWorld.IncidentWorker_Raid','TryGenerateRaidInfo','parms,pawns,debugTest'),
        @('RimWorld.LordToil_AssaultColony','UpdateAllDuties',''),
        @('Verse.Map','MapPostTick',''),
        @('Verse.Pawn','SpawnSetup','map,respawningAfterLoad'),
        @('Verse.Pawn_HealthTracker','CheckForStateChange','dinfo,hediff'),
        @('Verse.Pawn_HealthTracker','MakeDowned','dinfo,hediff'),
        @('Verse.AI.Group.Lord','Notify_PawnLost','pawn,cond,dinfo'),
        @('Verse.AI.Group.Lord','Notify_PawnDowned','pawn'),
        @('Verse.AI.Pawn_Thinker','get_MainThinkTree',''),
        @('Verse.AI.Pawn_Thinker','get_ConstantThinkTree','')
    )
    foreach ($entry in $methods) {
        $method = $game.GetType($entry[0],$true).GetMethod($entry[1],$flags)
        if ($null -eq $method -or (($method.GetParameters() | ForEach-Object Name) -join ',') -ne $entry[2]) {
            throw "Missing/changed Harmony target: $($entry[0]).$($entry[1])"
        }
    }
    foreach ($entry in @(@('Verse.Pawn_HealthTracker','pawn'), @('Verse.Pawn_HealthTracker','overrideDeathOnDownedChance'), @('Verse.Pawn','lord'), @('Verse.AI.Group.Lord','ownedPawns'))) {
        if ($null -eq $game.GetType($entry[0],$true).GetField($entry[1],$flags)) { throw "Missing field: $entry" }
    }
    foreach ($entry in @(@('Verse.Pawn','PostApplyDamage','dinfo,totalDamageDealt'), @('RimWorld.TendUtility','DoTend','doctor,patient,medicine'))) {
        $method = $game.GetType($entry[0],$true).GetMethod($entry[1],$flags)
        if ($null -eq $method -or (($method.GetParameters() | ForEach-Object Name) -join ',') -ne $entry[2]) {
            throw "Missing/changed medical-awakening target: $($entry[0]).$($entry[1])"
        }
    }
    $root = Split-Path $PSScriptRoot -Parent
    # Audit every declared Harmony target, not just the recent enemy feature.
    $targetCount = 0
    $injectedFields = @{}
    foreach ($file in Get-ChildItem -LiteralPath "$root/Source/WildShift" -Filter '*.cs') {
        $code = Get-Content -LiteralPath $file.FullName -Raw
        $patches = [regex]::Matches($code, '\[HarmonyPatch\(typeof\((\w+)\),\s*"([^"]+)"')
        for ($i=0; $i -lt $patches.Count; $i++) {
            $patch=$patches[$i]; $short=$patch.Groups[1].Value; $methodName=$patch.Groups[2].Value
            $type=$null
            foreach ($ns in @('Verse','RimWorld','Verse.AI','Verse.AI.Group','RimWorld.Planet')) {
                $type=$game.GetType("$ns.$short")
                if ($null -ne $type) { break }
            }
            if ($null -eq $type) { throw "Missing patch type: $short" }
            $candidates=@($type.GetMethods($flags) | Where-Object Name -eq $methodName)
            if ($short -eq 'PawnGenerator' -and $methodName -eq 'GeneratePawn') {
                $candidates=@($candidates | Where-Object { $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.FullName -eq 'Verse.PawnGenerationRequest' })
            }
            if ($candidates.Count -ne 1) { throw "Missing or ambiguous target: $short.$methodName" }
            $end=$code.Length
            if ($i+1 -lt $patches.Count) { $end=$patches[$i+1].Index }
            $body=$code.Substring($patch.Index,$end-$patch.Index)
            foreach ($match in [regex]::Matches($body,'\b___(\w+)\b')) {
                $fieldName=$match.Groups[1].Value
                if ($null -eq $type.GetField($fieldName,$flags)) { throw "Missing injected field: $short.$fieldName" }
                $injectedFields["$short.$fieldName"]=$true
            }
            $targetCount++
        }
    }
    # The drag-box predicate is a compiler-generated local function. Resolve
    # its semantic name and signature without depending on the numeric suffix.
    $dragTargets=@($game.GetType('RimWorld.Selector',$true).GetMethods($flags) | Where-Object {
        $_.Name.StartsWith('<SelectInsideDragBox>g__IsColonist|') -and $_.IsStatic -and
        $_.ReturnType.FullName -eq 'System.Boolean' -and $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.FullName -eq 'Verse.Thing'
    })
    if ($dragTargets.Count -ne 1) { throw 'Missing or ambiguous native drag-box colonist predicate' }
    $targetCount++
    $xmls = @(Get-ChildItem -LiteralPath "$root/Defs","$root/About","$root/Languages","$root/Patches" -Filter '*.xml' -Recurse)
    foreach ($file in $xmls) { $null = [xml](Get-Content -LiteralPath $file.FullName -Raw) }
    $tree = [xml](Get-Content -LiteralPath "$root/Defs/ThinkTreeDefs.xml" -Raw)
    foreach ($node in $tree.SelectNodes('//*[@Class]')) {
        $name = $node.GetAttribute('Class')
        if ($name.StartsWith('WildShift.')) { continue } # compiled against real ThinkNode API
        $type = $game.GetType("Verse.AI.$name")
        if ($null -eq $type) { $type=$game.GetType("RimWorld.$name") }
        if ($null -eq $type) { throw "Unknown think-node class: $name" }
    }
    $core = Join-Path (Split-Path (Split-Path $Managed -Parent) -Parent) 'Data/Core/Defs/ThinkTreeDefs'
    $names = @{}
    foreach ($file in Get-ChildItem -LiteralPath $core -Filter '*.xml' -Recurse) {
        $doc = [xml](Get-Content -LiteralPath $file.FullName -Raw)
        foreach ($def in $doc.SelectNodes('/Defs/ThinkTreeDef/defName')) { $names[$def.InnerText]=$true }
    }
    foreach ($node in $tree.SelectNodes('//treeDef')) {
        if (!$names.ContainsKey($node.InnerText)) { throw "Missing native subtree: $($node.InnerText)" }
    }
    $defNames=@{}
    foreach ($file in Get-ChildItem -LiteralPath "$root/Defs" -Filter '*.xml') {
        $doc=[xml](Get-Content -LiteralPath $file.FullName -Raw)
        foreach ($def in $doc.SelectNodes('/Defs/*/defName')) {
            if ($defNames.ContainsKey($def.InnerText)) { throw "Duplicate mod Def: $($def.InnerText)" }
            $defNames[$def.InnerText]=$true
        }
    }
    foreach ($lang in @('English','Korean')) {
        $doc=[xml](Get-Content -LiteralPath "$root/Languages/$lang/Keyed/WildShift.xml" -Raw)
        $keys=@{}
        foreach ($node in $doc.SelectNodes('/LanguageData/*')) {
            if ($keys.ContainsKey($node.Name)) { throw "Duplicate $lang keyed entry: $($node.Name)" }
            $keys[$node.Name]=$true
        }
    }
    "PASS: all $targetCount Harmony targets and $($injectedFields.Count) injected fields; 10 exact enemy signatures; $($xmls.Count) XML files, native tree references and duplicate Def/Keyed checks (metadata only)."
} finally {
    [AppDomain]::CurrentDomain.remove_ReflectionOnlyAssemblyResolve($resolve)
}
