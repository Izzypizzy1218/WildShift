$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath "$PSScriptRoot/SpecialTrainingTests.cs" -Raw
$source += "`n" + ((Get-Content -LiteralPath "$root/Source/WildShift/AnimalFormTraining.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
Add-Type -TypeDefinition $source
[WildShift.Tests.SpecialTrainingTests]::Run()
