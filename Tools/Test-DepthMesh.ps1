$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/DepthMeshRecord.cs')
function New-MeshRecord {
    $record = [SpatialSense.RoomScan.DepthMeshRecord]::new()
    $record.anchorUuid = [Guid]::NewGuid().ToString('D')
    $record.capturedAtUtc = [DateTime]::UtcNow.ToString('O')
    $record.vertices = [float[]]@(0,0,0, 1,0,0, 0,1,0)
    $record.triangles = [int[]]@(0,1,2)
    return $record
}
if (-not (New-MeshRecord).IsValid()) { throw 'Valid mesh rejected.' }
$withFloor = New-MeshRecord; $withFloor.hasFloor = $true; $withFloor.floorY = -1.6
if (-not $withFloor.IsValid()) { throw 'Anchored floor height rejected.' }
$withFloor.floorY = [float]::NaN
if ($withFloor.IsValid()) { throw 'Nonfinite saved floor height accepted.' }
foreach ($case in @('missingAnchor','invalidCoordinate','badIndex','duplicateIndex','partialTriangle','unsupportedVersion','oversized','wrongSource','invalidTime')) {
    $record = New-MeshRecord
    switch ($case) {
        missingAnchor { $record.anchorUuid = [Guid]::Empty.ToString() }
        invalidCoordinate { $record.vertices[0] = [float]::NaN }
        badIndex { $record.triangles[2] = 3 }
        duplicateIndex { $record.triangles[2] = 1 }
        partialTriangle { $record.triangles = [int[]]@(0,1) }
        unsupportedVersion { $record.version = 2 }
        oversized { $record.vertices = [float[]]::new(48003) }
        wrongSource { $record.source = 'SavedSceneModel' }
        invalidTime { $record.capturedAtUtc = 'not a timestamp' }
    }
    if ($record.IsValid()) { throw "Invalid mesh accepted: $case" }
}
$policy = [SpatialSense.RoomScan.DepthTrianglePolicy]
if (-not $policy::Accept($true,$true,$true,0.01,0.01,0.02,0.0001,0.45)) { throw 'Small valid surface rejected.' }
if ($policy::Accept($true,$false,$true,0.01,0.01,0.02,0.0001,0.45)) { throw 'Missing depth sample joined.' }
if ($policy::Accept($true,$true,$true,4,0.01,4,0.04,0.45)) { throw 'Depth discontinuity bridged.' }
if ($policy::Accept($true,$true,$true,0.01,0.01,0.04,0,0.45)) { throw 'Degenerate triangle accepted.' }
if ($policy::Accept($true,$true,$true,[float]::NaN,0.01,0.02,0.0001,0.45)) { throw 'Nonfinite geometry accepted.' }
'PASS: anchored mesh record validation, corruption/size limits, missing-depth gaps, discontinuities and degenerate geometry.'
