param([switch]$CompileOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Compile the production selection/validation code against small engine stubs.
# Include the real AddOrGet method, not a reimplementation of its lifecycle.
$transform = Get-Content "$root/Source/WildShift/TransformUtility.cs" -Raw
$start = $transform.IndexOf('        public static HediffComp_Shapeshifter AddOrGetShapeshifter(')
$end = $transform.IndexOf('        public static bool IsShapeshifter(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Cannot locate production assignment method.' }
$source = Get-Content "$PSScriptRoot/RacialAnimalFormsTests.cs" -Raw
# Optional integrations are exercised against the actual game/mod assemblies in
# Compatibility-v1.2.18. These unit tests continue to cover the no-mod path.
$source += @'
namespace Verse { public static class Scribe_Deep { public static void Look<T>(ref T v, string k) { } } }
namespace WildShift {
 public sealed class GeneticFormState { }
 public static class GeneticsCompatibility {
  public static System.Collections.Generic.HashSet<string> ExcludedNames = new System.Collections.Generic.HashSet<string>();
  public static bool Excluded(Verse.ThingDef race) { return race != null && ExcludedNames.Contains(race.defName); }
 }
 public static class SapientAnimalsCompatibility {
  public static Verse.Pawn TestPawn;
  public static Verse.PawnKindDef TestOriginalKind;
  public static Verse.PawnKindDef OriginalKind(Verse.Pawn pawn) { return pawn == TestPawn ? TestOriginalKind : null; }
 }
}
'@
$source += "`nnamespace WildShift { public static partial class TransformUtility {`n" + $transform.Substring($start, $end - $start) + "`n} }"
$validationStart = $transform.IndexOf('        private static bool CanTransformToAnimal(')
$validationEnd = $transform.LastIndexOf("`n    }")
if ($validationStart -lt 0 -or $validationEnd -le $validationStart) { throw 'Cannot locate production transformation guard.' }
$source += "`nnamespace WildShift { public static partial class TransformUtility {`n" + $transform.Substring($validationStart, $validationEnd - $validationStart) + "`n} }"
# Shared using directives are already in the harness header.
foreach ($name in @('AnimalPool', 'RacialAnimalForms', 'CustomRacialForms', 'AnimalFormGender', 'AwakeningRaceSelection', 'HediffComp_Shapeshifter', 'StartingFormPreviewCache', 'SoldRevengeBalance', 'RevengeCompanions')) {
    $source += "`n" + ((Get-Content "$root/Source/WildShift/$name.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
}
$source += "`n" + (Get-Content "$PSScriptRoot/AwakeningRaceSelectionTests.cs" -Raw)
$source += "`n" + (Get-Content "$PSScriptRoot/CustomRacialFormsTests.cs" -Raw)
Add-Type -TypeDefinition $source
if (-not $CompileOnly) {
    [WildShift.Tests.RacialAnimalFormsTests]::Run()
    [WildShift.Tests.CustomRacialFormsTests]::Run()
    [WildShift.Tests.AwakeningRaceSelectionTests]::Run()
}
