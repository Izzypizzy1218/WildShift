$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot '../Source/WildShift/SoldRevengeBalance.cs')
$checks=0
function Assert-Count([float]$points,[float]$power,[int]$expected,[string]$label) {
    $actual=[WildShift.SoldRevengeBalance]::CompanionCount($points,$power)
    if($actual -ne $expected) { throw "$label expected $expected companions, got $actual" }
    $script:checks++
}
Assert-Count 1000 100 19 'Leader included in 2000-point total'
Assert-Count 3000 100 59 'Arrival-time growth increases force'
Assert-Count 1000 200 9 'Stronger species needs fewer companions'
Assert-Count 1000 20 99 'Small species uses actual combat power'
Assert-Count 10000 50 399 'No five-animal or hundred-animal cap'
Assert-Count 525 100 10 'Half animal rounds upward'
Assert-Count 520 100 9 'Fractional force rounds to nearest animal'
Assert-Count 1 1000 1 'Leader plus one companion minimum'
Assert-Count 0 100 0 'Zero threat does not generate a force'
Assert-Count -100 100 0 'Negative threat rejected'
Assert-Count 1000 0 0 'Zero combat power rejected'
Assert-Count 1000 -1 0 'Negative combat power rejected'
Assert-Count ([float]::NaN) 100 0 'NaN threat rejected'
Assert-Count 1000 ([float]::PositiveInfinity) 0 'Infinite combat power rejected'
Assert-Count ([float]::MaxValue) ([float]::Epsilon) 0 'Overflowing force rejected'
if([WildShift.SoldRevengeBalance]::CompanionCount(1000,100,600) -ne 13) { throw 'Additional shifters did not consume the shared budget' }; $checks++
if([WildShift.SoldRevengeBalance]::CompanionCount(1000,100,650) -ne 13) { throw 'Mixed-force rounding changed' }; $checks++
if([WildShift.SoldRevengeBalance]::CompanionCount(1000,100,[float]::NaN) -ne 0) { throw 'Invalid ally budget accepted' }; $checks++
"PASS: $checks revenge-budget assertions (production balance helper)."
