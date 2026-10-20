$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
# Run in a fresh PowerShell process. Compile the real callbacks against small
# engine stubs, so GC checks test ownership rather than private field shapes.
$source = Get-Content "$PSScriptRoot/CacheLifetimeTests.cs" -Raw
foreach ($name in @('Patch_GameEnder', 'Patch_PawnGizmos')) {
    $source += "`n" + ((Get-Content "$root/Source/WildShift/$name.cs" -Raw) -replace '(?m)^using [^;]+;\r?\n', '')
}
Add-Type -TypeDefinition $source
[WildShift.Tests.CacheLifetimeTests]::Run()
