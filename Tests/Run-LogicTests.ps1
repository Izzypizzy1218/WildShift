$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Compile the production selection/validation code against small engine stubs.
# Include the real AddOrGet method, not a reimplementation of its lifecycle.
$transform = Get-Content "$root/Source/WildShift/TransformUtility.cs" -Raw
$start = $transform.IndexOf('        public static HediffComp_Shapeshifter AddOrGetShapeshifter(')
$end = $transform.IndexOf('        public static bool IsShapeshifter(', $start)
if ($start -lt 0 -or $end -le $start) { throw 'Cannot locate production assignment method.' }
$source = Get-Content "$PSScriptRoot/RacialAnimalFormsTests.cs" -Raw
$source += "`nnamespace WildShift { public static partial class TransformUtility {`n" + $transform.Substring($start, $end - $start) + "`n} }"
$validationStart = $transform.IndexOf('        private static bool CanTransformToAnimal(')
$validationEnd = $transform.LastIndexOf("`n    }")
if ($validationStart -lt 0 -or $validationEnd -le $validationStart) { throw 'Cannot locate production transformation guard.' }
$source += "`nnamespace WildShift { public static partial class TransformUtility {`n" + $transform.Substring($validationStart, $validationEnd - $validationStart) + "`n} }"
# Shared using directives are already in the harness header.
foreach ($name in @('AnimalPool', 'RacialAnimalForms', 'HediffComp_Shapeshifter')) {
    $source += "`n" + ((Get-Content "$root/Source/WildShift/$name.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
}
Add-Type -TypeDefinition $source
[WildShift.Tests.RacialAnimalFormsTests]::Run()
