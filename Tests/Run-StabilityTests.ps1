$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content "$PSScriptRoot/StabilityTests.cs" -Raw
$source += @'
namespace WildShift { public static class GeneticsCompatibility { public static void Remember(Verse.Pawn animal, Verse.Pawn human) { } } }
namespace WildShift { public static class SoldShapeshifterRevenge { public static bool IsPackAnimal(Verse.Pawn pawn) { return false; } public static bool IsRevengePawn(Verse.Pawn pawn) { return false; } } }
'@
# Compile production implementations against fault-injecting engine stubs.
foreach ($name in @('FormTransferUtility', 'Patch_TransformedColonistBar', 'Patch_TransformedDeath', 'AnimalModCompatibility', 'EnemyShapeshifterUtility', 'MapComponent_EnemyShapeshifters', 'Patch_EnemyShapeshifters', 'Patch_TransformedAnimalDraftThinkTree')) {
    $source += "`n" + ((Get-Content "$root/Source/WildShift/$name.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
}
$source += "`n" + ((Get-Content "$root/Source/WildShift/FormPossessions.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
$source += "`n" + (Get-Content "$PSScriptRoot/FormPossessionsTests.cs" -Raw)
$transform = Get-Content "$root/Source/WildShift/TransformUtility.cs" -Raw
$attackStart = $transform.IndexOf('        public static bool TryOrderMeleeAttack(')
$attackEnd = $transform.IndexOf('        public static HediffComp_Transformed TryGetTransformedComp(', $attackStart)
if ($attackStart -lt 0 -or $attackEnd -le $attackStart) { throw 'Cannot locate production melee order method.' }
$source += "`nnamespace WildShift { public static partial class TransformUtility {`n" + $transform.Substring($attackStart, $attackEnd - $attackStart) + "`n} }"
$taming = Get-Content "$root/Source/WildShift/Patch_Taming.cs" -Raw
$tamingStart = $taming.IndexOf('    public static class Patch_Taming')
$tamingEnd = $taming.LastIndexOf('}')
if ($tamingStart -lt 0 -or $tamingEnd -le $tamingStart) { throw 'Cannot locate production taming class.' }
$source += "`nnamespace WildShift {`n" + $taming.Substring($tamingStart, $tamingEnd - $tamingStart) + "`npublic static class TamingTestBridge { public static void Run(Pawn p) { Patch_Taming.RevealTamedShapeshifter(p); } } }"
$source += "`n" + ((Get-Content "$root/Source/WildShift/MedicalAwakening.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
$source += "`n" + (Get-Content "$PSScriptRoot/MedicalAwakeningTests.cs" -Raw)
$source += "`n" + ((Get-Content "$root/Source/WildShift/OwnedAnimalAwakening.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
$source += "`n" + ((Get-Content "$root/Source/WildShift/AwakeningPossessions.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
$source += "`n" + (Get-Content "$PSScriptRoot/OwnedAnimalAwakeningTests.cs" -Raw)
$source += "`n" + ((Get-Content "$root/Source/WildShift/TraderShapeshifterTestMode.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
$source += "`n" + (Get-Content "$PSScriptRoot/TraderTestModeTests.cs" -Raw)
$source += "`n" + ((Get-Content "$root/Source/WildShift/AwakeningRecovery.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
$source += "`n" + (Get-Content "$PSScriptRoot/RecoveryRegressionTests.cs" -Raw)
$source += "`n" + (Get-Content "$PSScriptRoot/PerformanceCompatibilityTests.cs" -Raw)
$start = $transform.IndexOf('        public static Pawn RevertToHuman(')
$end = $transform.IndexOf('        private static bool CanTransformToAnimal(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Cannot locate production reversion methods.' }
$source += "`nnamespace WildShift { public static partial class TransformUtility {`n" + $transform.Substring($start, $end - $start) + "`n} }"
$source += "`n" + (Get-Content "$PSScriptRoot/EnemyTests.cs" -Raw)
$source += "`n" + (Get-Content "$PSScriptRoot/ReviewTests.cs" -Raw)
$source += "`n" + ((Get-Content "$root/Source/WildShift/ThinkNode_ConditionalEnemyDutyReady.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
Add-Type -TypeDefinition $source
[WildShift.Tests.StabilityTests]::Run()
