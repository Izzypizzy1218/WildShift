$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content "$PSScriptRoot/StabilityTests.cs" -Raw
# Compile production implementations against fault-injecting engine stubs.
foreach ($name in @('FormTransferUtility', 'Patch_TransformedColonistBar', 'Patch_TransformedDeath', 'AnimalModCompatibility')) {
    $source += "`n" + ((Get-Content "$root/Source/WildShift/$name.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
}
$transform = Get-Content "$root/Source/WildShift/TransformUtility.cs" -Raw
$start = $transform.IndexOf('        public static Pawn RevertToHuman(')
$end = $transform.IndexOf('        private static bool CanTransformToAnimal(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Cannot locate production reversion methods.' }
$source += "`nnamespace WildShift { public static partial class TransformUtility {`n" + $transform.Substring($start, $end - $start) + "`n} }"
$draft = Get-Content "$root/Source/WildShift/Patch_TransformedAnimalDraftUI.cs" -Raw
$start = $draft.IndexOf('    public static class Patch_TransformedAnimalDirectRightClickAttack')
$end = $draft.IndexOf('    // Successful direct attacks', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Cannot locate production direct attack patch.' }
if ($draft.Contains('class Patch_TransformedAnimalSuppressAttackMenu')) { throw 'Obsolete fallback-suppressing patch returned.' }
$source += "`nnamespace WildShift {`n" + $draft.Substring($start, $end - $start) + "`n}"
Add-Type -TypeDefinition $source
[WildShift.Tests.StabilityTests]::Run()
