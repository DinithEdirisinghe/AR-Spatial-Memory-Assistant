$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/DepthMeshRecord.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/SpatialMeshAccumulator.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/ScanNavigationGrid.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/LiveObstacleLayer.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/BaselineFurnitureLayer.cs')
)
function P([float]$x,[float]$y,[float]$z) { [SpatialSense.RoomScan.MeshPoint]::new($x,$y,$z) }
function Assert($condition,$message) { if (-not $condition) { throw $message } }
function Rays { ,([System.Collections.Generic.List[SpatialSense.RoomScan.DepthObservationRay]]::new()) }
function Ray($list,$from,$to,$normal) {
    if ($null -eq $normal) { $list.Add([SpatialSense.RoomScan.DepthObservationRay]::new($from,$to)) }
    else { $list.Add([SpatialSense.RoomScan.DepthObservationRay]::new($from,$to,$normal)) }
}
function Build([bool]$classify=$true,[bool]$overlapWall=$false,[float]$floorY=0) {
    $points=[System.Collections.Generic.List[SpatialSense.RoomScan.MeshPoint]]::new()
    foreach ($p in @((P 0 $floorY 0),(P 6 $floorY 0),(P 6 $floorY 6),(P 0 $floorY 6),
        (P 2.5 ($floorY+0.8) 2.5),(P 3.5 ($floorY+0.8) 2.5),(P 3.5 ($floorY+0.8) 3.5),(P 2.5 ($floorY+0.8) 3.5),
        (P 5 $floorY 0),(P 5 ($floorY+1.8) 0),(P 5 ($floorY+1.8) 6),(P 5 $floorY 6))) { $points.Add($p) }
    $indices=[System.Collections.Generic.List[int]]::new()
    foreach ($i in @(0,1,2,0,2,3,4,5,6,4,6,7,8,9,10,8,10,11)) { $indices.Add($i) }
    $flags=[System.Collections.Generic.List[bool]]::new()
    foreach ($f in @($false,$false,$classify,$classify,$false,$false)) { $flags.Add($f) }
    if ($overlapWall) {
        $offset=$points.Count
        foreach ($p in @((P 3 $floorY 0),(P 3 ($floorY+1.8) 0),(P 3 ($floorY+1.8) 6),(P 3 $floorY 6))) { $points.Add($p) }
        foreach ($i in @(0,1,2,0,2,3)) { $indices.Add($offset+$i) }
        $flags.Add($false); $flags.Add($false)
    }
    [SpatialSense.RoomScan.ScanNavigationGrid]::Build($points,$indices,$floorY,0.03,[Threading.CancellationToken]::None,$true,6,$flags.ToArray())
}
function Evidence($map,[bool]$floor=$true,[float]$level=0.8,[bool]$normal=$true) {
    $r=Rays
    for ($i=0;$i -lt $map.CellCount;$i++) {
        if (-not $map.CanRefreshFurniture($i)) { continue }
        $p=$map.Centre($i)
        Ray $r (P 1 ($map.FloorY+$level) $p.Z) (P 4.5 ($map.FloorY+$level) $p.Z) $null
        if ($floor) {
            $n=if ($normal) { P 0 1 0 } else { $null }
            Ray $r (P $p.X ($map.FloorY+1.6) $p.Z) (P $p.X $map.FloorY $p.Z) $n
        }
    }
    return ,$r
}
function Apply($map,$live) {
    $map.RebuildFurnitureBaseline(0.03)
    $map.ApplyLiveObstacles($live.ActivePoints(),0.03)
}
$map=Build; $layer=[SpatialSense.RoomScan.BaselineFurnitureLayer]::new($map)
$live=[SpatialSense.RoomScan.LiveObstacleLayer]::new($map)
$middle=$map.Index(3,3); $wall=$map.Index(5,3)
Assert ($layer.CandidateCount -gt 100) 'Furniture classification did not reach the baseline map.'
for ($candidate=0;$candidate -lt $layer.CandidateCount;$candidate++) {
    $count=$layer.ProbeCount($candidate)
    Assert ($count -ge 2 -and $count -le 8) 'Furniture probe group is unbounded or missing floor verification.'
    Assert ($layer.ProbePoint($candidate,$count-1).Y -eq $map.FloorY) 'Last furniture probe does not target the floor.'
    $cell=$layer.CandidateCell($candidate)
    for ($slot=0;$slot -lt $count-1;$slot++) {
        $height=$layer.ProbePoint($candidate,$slot).Y
        Assert ($height -ge $map.FurnitureLow($cell)-0.001 -and $height -le $map.FurnitureHigh($cell)+0.001) 'Targeted probe left the old occupied height interval.'
    }
}
Assert ($map.Cells[$middle] -eq 1 -and -not $map.CanRefreshFurniture($wall)) 'Furniture and protected walls were not separated.'
$empty=Rays
[void]$layer.Observe($empty,1)
Assert (-not $map.FurnitureCleared($middle)) 'Missing depth cleared baseline furniture.'
$side=Evidence $map $false
foreach ($t in @(2,2.3,2.7)) { [void]$layer.Observe($side,$t) }
Assert (-not $map.FurnitureCleared($middle)) 'Free old volume without floor evidence cleared furniture.'
$noNormal=Evidence $map $true 0.8 $false
foreach ($t in @(3,3.3,3.7)) { [void]$layer.Observe($noNormal,$t) }
Assert (-not $map.FurnitureCleared($middle)) 'Unverified surface normal counted as floor proof.'

# Repeated evidence through the old surface AND a currently visible floor are
# required. Multiple rays in one batch cannot replace separate observations.
$proof=Evidence $map
Assert (-not $layer.Observe($proof,5)) 'A single full evidence batch opened furniture.'
Assert (-not $layer.Observe($proof,5.3)) 'Furniture opened before three batches / the minimum time span.'
Assert $layer.Observe($proof,5.7) 'Repeated complete evidence did not reopen furniture.'
Apply $map $live
Assert ($map.Cells[$middle] -eq 2 -and $map.OriginalCell($middle) -eq 1) 'Corrected floor or immutable original state is wrong.'
Assert ($map.Cells[$wall] -eq 1) 'Furniture correction erased a permanent wall.'
Assert ($layer.ClearedLastBatch -eq $layer.ClearedCount) 'Reopened-cell counts are wrong.'
$path=[System.Collections.Generic.List[SpatialSense.RoomScan.MeshPoint]]::new(); [string]$reason=''
Assert ($map.TryRoute(1,3,4,3,[ref]$path,[ref]$reason)) 'Corrected floor was not usable by the route planner.'
foreach ($p in $path) { Assert ([Math]::Abs($p.Z-3) -lt 0.1) 'Old furniture still forced a detour after correction.' }
$snapshot=$map.RoutingSnapshot()

# Positive reoccupation restores the old restriction immediately. New live
# obstacles can also occupy reopened baseline cells.
$hit=Rays; $p=$map.Centre($middle)
Ray $hit (P 1 1 $p.Z) (P $p.X 1 $p.Z) $null
Assert $layer.Observe($hit,6) 'Reoccupied old furniture space was not restored.'
Apply $map $live
Assert ($map.Cells[$middle] -eq 1 -and $layer.RestoredLastBatch -eq 1) 'Reoccupation did not reblock the baseline cell.'
Assert ($snapshot.Cells[$middle] -eq 2) 'Baseline updates mutated a worker route snapshot.'
foreach ($t in @(7,7.3,7.7)) { [void]$layer.Observe($proof,$t) }
Apply $map $live
[void]$live.Observe($hit,8); [void]$live.Observe($hit,8.1); Apply $map $live
Assert ($map.Cells[$middle] -eq 1) 'A new live obstacle was ignored on reopened floor.'

# Under-table evidence must not remove the still-present tabletop.
$underMap=Build; $under=[SpatialSense.RoomScan.BaselineFurnitureLayer]::new($underMap)
$underRays=Evidence $underMap $false 0.3
for ($i=0;$i -lt $underMap.CellCount;$i++) {
    if (-not $underMap.CanRefreshFurniture($i)) { continue }
    $p=$underMap.Centre($i)
    Ray $underRays (P $p.X 0.5 $p.Z) (P $p.X 0 $p.Z) (P 0 1 0)
}
foreach ($t in @(10,10.3,10.7)) { [void]$under.Observe($underRays,$t) }
Assert ($under.ClearedCount -eq 0) 'Under-table rays cleared a higher baseline surface.'
$stillPresent=Rays; $p=$underMap.Centre($middle)
Ray $stillPresent (P $p.X 1.6 $p.Z) (P $p.X 0.8 $p.Z) (P 0 1 0)
foreach ($t in @(11,11.3,11.7)) { [void]$under.Observe($stillPresent,$t) }
Assert ($under.ClearedCount -eq 0) 'The old furniture surface itself counted as free floor.'

# Unlabelled geometry and any wall/furniture overlap stay protected.
$unknownMap=Build $false
Assert (([SpatialSense.RoomScan.BaselineFurnitureLayer]::new($unknownMap)).CandidateCount -eq 0) 'Unclassified furniture was assumed movable.'
$overlap=Build $true $true; $overlapLayer=[SpatialSense.RoomScan.BaselineFurnitureLayer]::new($overlap)
$overlapProof=Evidence $overlap
foreach ($t in @(12,12.3,12.7)) { [void]$overlapLayer.Observe($overlapProof,$t) }
Apply $overlap ([SpatialSense.RoomScan.LiveObstacleLayer]::new($overlap))
Assert ($overlap.Cells[$overlap.Index(3,3)] -eq 1) 'A permanent wall overlapping furniture was cleared.'

# App scan frames may have negative floor heights. Sparse/unsupported floor
# must remain unknown instead of being invented beneath removed furniture.
$raised=Build $true $false -1.6; $raisedLayer=[SpatialSense.RoomScan.BaselineFurnitureLayer]::new($raised)
$raisedProof=Evidence $raised
foreach ($t in @(14,14.3,14.7)) { [void]$raisedLayer.Observe($raisedProof,$t) }
Apply $raised ([SpatialSense.RoomScan.LiveObstacleLayer]::new($raised))
Assert ($raised.Cells[$raised.Index(3,3)] -eq 2) 'Anchored app scan floor height broke furniture verification.'

# An app mesh may have no captured floor under the furniture. Semantic labels
# are not permission to invent that missing floor.
$holePoints=[SpatialSense.RoomScan.MeshPoint[]]@(
    (P 0 0 0),(P 2.4 0 0),(P 2.4 0 6),(P 0 0 6),
    (P 3.6 0 0),(P 6 0 0),(P 6 0 6),(P 3.6 0 6),
    (P 2.5 0.8 2.5),(P 3.5 0.8 2.5),(P 3.5 0.8 3.5),(P 2.5 0.8 3.5))
$hole=[SpatialSense.RoomScan.ScanNavigationGrid]::Build($holePoints,[int[]]@(0,1,2,0,2,3,4,5,6,4,6,7,8,9,10,8,10,11),0,0.03,[Threading.CancellationToken]::None,$false,-1,[bool[]]@($false,$false,$false,$false,$true,$true))
Assert (-not $hole.CanRefreshFurniture($hole.Index(3,3))) 'A label invented missing floor beneath an app-scan obstacle.'

# Previously accumulated, stale evidence cannot complete a new verification
# immediately after a long observation gap.
$staleMap=Build; $stale=[SpatialSense.RoomScan.BaselineFurnitureLayer]::new($staleMap); $staleProof=Evidence $staleMap
[void]$stale.Observe($staleProof,20)
[void]$stale.Observe($staleProof,30)
[void]$stale.Observe($staleProof,30.1)
Assert ($stale.ClearedCount -eq 0) 'Stale evidence was combined with a fresh short burst.'
[void]$stale.Observe($staleProof,30.7)
Assert ($stale.ClearedCount -gt 0) 'Fresh evidence after a long gap never recovered.'

# A tall old furniture surface requires every occupied height band. Seeing
# floor from underneath and only a higher free band is insufficient.
$tallPoints=[SpatialSense.RoomScan.MeshPoint[]]@(
    (P 0 0 0),(P 6 0 0),(P 6 0 6),(P 0 0 6),
    (P 3 0 2.5),(P 3 0.8 2.5),(P 3 0.8 3.5),(P 3 0 3.5))
$tallMap=[SpatialSense.RoomScan.ScanNavigationGrid]::Build($tallPoints,[int[]]@(0,1,2,0,2,3,4,5,6,4,6,7),0,0.03,[Threading.CancellationToken]::None,$true,6,[bool[]]@($false,$false,$true,$true))
$tall=[SpatialSense.RoomScan.BaselineFurnitureLayer]::new($tallMap); $upper=Rays; $all=Rays
for ($i=0;$i -lt $tallMap.CellCount;$i++) {
    if (-not $tallMap.CanRefreshFurniture($i)) { continue }
    $p=$tallMap.Centre($i)
    Ray $upper (P $p.X 0.1 $p.Z) (P $p.X 0 $p.Z) (P 0 1 0)
    Ray $upper (P 1 0.8 $p.Z) (P 4.5 0.8 $p.Z) $null
    foreach ($height in @(0.2,0.5,0.8)) { Ray $all (P 1 $height $p.Z) (P 4.5 $height $p.Z) $null }
    Ray $all (P $p.X 0.1 $p.Z) (P $p.X 0 $p.Z) (P 0 1 0)
}
foreach ($t in @(40,40.3,40.7)) { [void]$tall.Observe($upper,$t) }
Assert ($tall.ClearedCount -eq 0) 'Partial height-band proof opened a tall furniture restriction.'
foreach ($t in @(41,41.3,41.7)) { [void]$tall.Observe($all,$t) }
Assert ($tall.ClearedCount -gt 0) 'Complete height-band proof did not reopen old furniture.'
'PASS: protected baseline provenance, complete free-volume plus normal-verified floor evidence, repeat/time thresholds, reopening and route shortening, reoccupation, live-obstacle precedence, wall/unknown protection, snapshots and negative floor heights.'
