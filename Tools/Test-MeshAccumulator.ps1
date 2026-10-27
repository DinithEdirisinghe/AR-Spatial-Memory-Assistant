$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/DepthMeshRecord.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/SpatialMeshAccumulator.cs')
)
function P([float]$x, [float]$y, [float]$z) { [SpatialSense.RoomScan.MeshPoint]::new($x, $y, $z) }
function Assert($condition, $reason) { if (-not $condition) { throw $reason } }
$mesh = [SpatialSense.RoomScan.SpatialMeshAccumulator]::new()
$a = P 0.019 0 0; $b = P 0.219 0 0; $c = P 0.019 0.2 0
Assert ($mesh.AddTriangle($a,$b,$c)) 'Initial surface rejected.'
for ($i = 0; $i -lt 200; $i++) {
    # Same physical triangle with 2 mm noise, across spatial hash boundaries.
    [void]$mesh.AddTriangle((P 0.021 0 0), (P 0.221 0 0), (P 0.021 0.2 0))
}
Assert ($mesh.Points.Count -eq 3 -and $mesh.Indices.Count -eq 3) 'Repeated observations grew duplicate geometry.'
Assert ($mesh.DuplicateFaces -eq 200) 'Duplicate count is wrong.'
Assert ([Math]::Abs($mesh.Points[0].X - 0.019) -lt 0.000001) 'Repeated jitter moved the established surface.'
Assert (-not $mesh.AddTriangle($b,$c,$a)) 'Cyclic vertex ordering bypassed deduplication.'
# Adjacent triangle shares two points; only the new corner should be added.
Assert ($mesh.AddTriangle($b,(P 0.219 0.2 0),$c)) 'Adjacent surface rejected.'
Assert ($mesh.Points.Count -eq 4 -and $mesh.Indices.Count -eq 6) 'Adjacent faces did not share their edge.'
# A separate parallel surface outside weld tolerance must remain separate.
Assert ($mesh.AddTriangle((P 0.019 0 0.05),(P 0.219 0 0.05),(P 0.019 0.2 0.05))) 'Separate surface rejected.'
Assert ($mesh.Points.Count -eq 7) 'Separated surfaces were welded together.'
# Even within 2 cm, an opposite facing thin surface must not weld to the front.
Assert ($mesh.AddTriangle((P 0.019 0 0.01),(P 0.019 0.2 0.01),(P 0.219 0 0.01))) 'Back surface rejected.'
Assert ($mesh.Points.Count -eq 10) 'Opposite facing surfaces were merged.'
$oldPoints = $mesh.Points.Count; $oldIndices = $mesh.Indices.Count
Assert (-not $mesh.AddTriangle((P ([float]::NaN) 0 0),$b,$c)) 'Nonfinite point accepted.'
Assert (-not $mesh.AddTriangle($a,$a,$c)) 'Zero area triangle accepted.'
# Triangle is valid before welding but collapses after snapping two points.
Assert (-not $mesh.AddTriangle((P 0.018 0 0), (P 0.02 0 0), (P 0.019 0.2 0))) 'Collapsed welded triangle accepted.'
Assert ($mesh.Points.Count -eq $oldPoints -and $mesh.Indices.Count -eq $oldIndices) 'Rejected input changed mesh buffers.'
$corner = [SpatialSense.RoomScan.SpatialMeshAccumulator]::new()
[void]$corner.AddTriangle((P 0 0 0),(P 0.2 0 0),(P 0 0.2 0))
[void]$corner.AddTriangle((P 0 0 0),(P 0 0.2 0),(P 0 0 0.2))
Assert ($corner.Points.Count -eq 6) 'A sharp right-angle corner was smoothed together.'
# Exercise bounded accumulation with distinct triangles until the vertex limit.
$bounded = [SpatialSense.RoomScan.SpatialMeshAccumulator]::new()
for ($i = 0; $i -lt 5400; $i++) {
    $x = ($i % 100) * 0.1 - 5
    $y = [Math]::Floor($i / 100) * 0.1 - 3
    [void]$bounded.AddTriangle((P $x $y 0),(P ($x+0.03) $y 0),(P $x ($y+0.03) 0))
}
Assert ($bounded.AtCapacity) 'Capacity was not enforced.'
Assert ($bounded.Points.Count -le 16000 -and $bounded.Indices.Count -le 84000) 'Mesh exceeded persisted format limits.'
foreach ($index in $bounded.Indices) { Assert ($index -ge 0 -and $index -lt $bounded.Points.Count) 'Out of bounds exported index.' }
'PASS: repeated-view deduplication, cross-cell welding, shared edges, separated/opposite surfaces, collapse rejection, unchanged buffers on rejection, and bounded export.'
