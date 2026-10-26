$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $projectRoot 'Assets/_Project/Core/Interfaces/ObservationPolicy.cs')
$validationNow = [DateTime]::UtcNow
$roomId = [Guid]::NewGuid().ToString('D')
$frameId = [Guid]::NewGuid().ToString('D')

function New-Sighting {
    return @{
        ObjectId='wallet'; ObservationId=[Guid]::NewGuid().ToString('D'); X=1; Y=1; Z=1
        Identity=0.95; Position=0.9; Observed=$validationNow; Room=$roomId; Frame=$frameId
    }
}
function Test-Sighting($values, $expected, $label) {
    [string]$reason = $null
    $actual = [Project.Core.Integration.ObservationPolicy]::Validate(
        $values.ObjectId,$values.ObservationId,$values.X,$values.Y,$values.Z,$values.Identity,$values.Position,
        $values.Observed,$validationNow,$values.Room,$roomId,$values.Frame,$frameId,[ref]$reason)
    if ($actual -ne $expected) { throw "Observation policy failed: $label ($reason)" }
    if (-not $expected -and [string]::IsNullOrWhiteSpace($reason)) { throw "Rejected input has no explanation: $label" }
}
foreach ($id in @('keys','wallet','remote')) {
    $sighting = New-Sighting
    $sighting.ObjectId = $id
    Test-Sighting $sighting $true $id
    if ([Project.Core.Integration.ObservationPolicy]::IdFor([Project.Core.Integration.ObservationPolicy]::NameFor($id)) -ne $id) { throw 'Object identity mapping did not round-trip.' }
}
$cases = @(
    @{Name='unknown object'; Change={param($s) $s.ObjectId='another-wallet'}},
    @{Name='missing observation ID'; Change={param($s) $s.ObservationId=''}},
    @{Name='empty observation UUID'; Change={param($s) $s.ObservationId=[Guid]::Empty.ToString('D')}},
    @{Name='NaN position'; Change={param($s) $s.X=[float]::NaN}},
    @{Name='infinite position'; Change={param($s) $s.Z=[float]::PositiveInfinity}},
    @{Name='identity too weak'; Change={param($s) $s.Identity=0.79}},
    @{Name='position too weak'; Change={param($s) $s.Position=0.59}},
    @{Name='confidence above one'; Change={param($s) $s.Identity=1.01}},
    @{Name='NaN confidence'; Change={param($s) $s.Position=[float]::NaN}},
    @{Name='stale sighting'; Change={param($s) $s.Observed=$validationNow.AddSeconds(-31)}},
    @{Name='future sighting'; Change={param($s) $s.Observed=$validationNow.AddSeconds(6)}},
    @{Name='unspecified clock'; Change={param($s) $s.Observed=[DateTime]::SpecifyKind($validationNow,[DateTimeKind]::Unspecified)}},
    @{Name='wrong room'; Change={param($s) $s.Room=[Guid]::NewGuid().ToString('D')}},
    @{Name='expired coordinate frame'; Change={param($s) $s.Frame=[Guid]::NewGuid().ToString('D')}}
)
foreach ($case in $cases) {
    $sighting = New-Sighting
    & $case.Change $sighting
    Test-Sighting $sighting $false $case.Name
}
$sighting = New-Sighting
$sighting.Identity = 0.8; $sighting.Position = 0.6
Test-Sighting $sighting $true 'threshold boundaries'
$sighting = New-Sighting
$sighting.Observed = $validationNow.AddSeconds(-30)
Test-Sighting $sighting $true 'freshness boundary'
$sighting = New-Sighting
$sighting.Observed = $validationNow.AddSeconds(5)
Test-Sighting $sighting $true 'clock-tolerance boundary'
$sighting = New-Sighting
$sighting.Room = $roomId.ToUpperInvariant(); $sighting.Frame = $frameId.ToUpperInvariant()
Test-Sighting $sighting $true 'equivalent UUID formatting'
'PASS: observation identity mapping, confidence/coordinate/time validation, stale-room/frame rejection, and valid boundaries.'
