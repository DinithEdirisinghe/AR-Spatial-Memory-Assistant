$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/DepthMeshRecord.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/SpatialMeshAccumulator.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/ScanNavigationGrid.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/LiveObstacleLayer.cs')
)
function P([float]$x,[float]$y,[float]$z) { [SpatialSense.RoomScan.MeshPoint]::new($x,$y,$z) }
function Assert($condition,$reason) { if (-not $condition) { throw $reason } }
$points = [System.Collections.Generic.List[SpatialSense.RoomScan.MeshPoint]]::new()
foreach ($p in @((P 0 0 0),(P 6 0 0),(P 6 0 6),(P 0 0 6))) { $points.Add($p) }
$indices = [System.Collections.Generic.List[int]]::new()
foreach ($i in @(0,1,2,0,2,3)) { $indices.Add($i) }
# A non-floor semantic surface at floor height outside the actual room must not
# expand the navigation floor, even though its geometry is flat.
foreach ($p in @((P 6 0 6),(P 9 0 6),(P 9 0 9),(P 6 0 9))) { $points.Add($p) }
foreach ($i in @(4,5,6,4,6,7)) { $indices.Add($i) }
$map = [SpatialSense.RoomScan.ScanNavigationGrid]::Build($points,$indices,0,0.2,[Threading.CancellationToken]::None,$true,6)
Assert ($map.Index(7,7) -eq -1) 'Non-floor geometry expanded Meta floor boundary.'
Assert ($map.WalkableCount -gt 900) 'Semantic room floor was unnecessarily fragmented.'
$centre = $map.Centre($map.Index(3,3))
$origin = P $centre.X 1 1
$hit = P $centre.X 1 $centre.Z
$ray = [SpatialSense.RoomScan.DepthObservationRay]::new($origin,$hit)
$observations = [System.Collections.Generic.List[SpatialSense.RoomScan.DepthObservationRay]]::new(); $observations.Add($ray)
$empty = [System.Collections.Generic.List[SpatialSense.RoomScan.DepthObservationRay]]::new()
$layer = [SpatialSense.RoomScan.LiveObstacleLayer]::new($map)
Assert (-not $layer.Observe($observations)) 'One noisy batch immediately blocked floor.'
Assert ($layer.Observe($observations)) 'Second positive batch did not confirm live obstacle.'
Assert ($layer.ActiveCount -eq 1) 'Live obstacle count is wrong.'
$map.ApplyLiveObstacles($layer.ActivePoints(),0.2)
Assert ($map.Cells[$map.Index($centre.X,$centre.Z)] -eq 1) 'Confirmed live obstacle did not restrict room floor.'
$beforeMissing = $map.WalkableCount
Assert (-not $layer.Observe($empty)) 'Missing depth removed a confirmed block.'
Assert ($layer.ActiveCount -eq 1) 'Unobserved obstacle was forgotten.'
$lowRay = [SpatialSense.RoomScan.DepthObservationRay]::new((P $centre.X 0.05 1),(P $centre.X 0.05 5))
$low = [System.Collections.Generic.List[SpatialSense.RoomScan.DepthObservationRay]]::new(); $low.Add($lowRay)
for ($i=0; $i -lt 4; $i++) { [void]$layer.Observe($low) }
Assert ($layer.ActiveCount -eq 1) 'A ray below the old obstacle incorrectly cleared it.'
# Hit the excluded boundary band beyond the old volume, so the ray does not
# simultaneously introduce a second live obstacle in the room interior.
$clearRay = [SpatialSense.RoomScan.DepthObservationRay]::new($origin,(P $centre.X 1 5.95))
$clear = [System.Collections.Generic.List[SpatialSense.RoomScan.DepthObservationRay]]::new(); $clear.Add($clearRay)
Assert (-not $layer.Observe($clear)) 'One free ray cleared a confirmed obstacle.'
Assert (-not $layer.Observe($clear)) 'Two free batches cleared too early.'
Assert ($layer.Observe($clear)) 'Three rays through the old volume did not clear it.'
Assert ($layer.ActiveCount -eq 0) 'Cleared obstacle retained a vote.'
$map.ApplyLiveObstacles($layer.ActivePoints(),0.2)
Assert ($map.Cells[$map.Index($centre.X,$centre.Z)] -eq 2 -and $map.WalkableCount -gt $beforeMissing) 'Free evidence did not restore the underlying Meta floor.'
# Floor-level hits are never interpreted as new furniture; absent live data
# never modifies the base room at all.
$floorLayer = [SpatialSense.RoomScan.LiveObstacleLayer]::new($map)
for ($i=0; $i -lt 5; $i++) { [void]$floorLayer.Observe($low) }
Assert ($floorLayer.ActiveCount -eq 0) 'Floor hits became live obstacles.'
$invalid = [System.Collections.Generic.List[SpatialSense.RoomScan.DepthObservationRay]]::new()
$invalid.Add([SpatialSense.RoomScan.DepthObservationRay]::new($origin,(P ([float]::NaN) 1 3)))
[void]$floorLayer.Observe($invalid)
Assert ($floorLayer.ActiveCount -eq 0) 'Invalid depth became an obstacle.'
'PASS: semantic floor authority, nonfragmented native room base, temporal obstacle confirmation, missing-data preservation, height-aware free evidence and floor/noise rejection.'
