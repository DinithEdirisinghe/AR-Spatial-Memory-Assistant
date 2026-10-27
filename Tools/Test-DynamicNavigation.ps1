$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/DepthMeshRecord.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/SpatialMeshAccumulator.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/ScanNavigationGrid.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/LiveObstacleLayer.cs')
)
function P([float]$x,[float]$y,[float]$z) { [SpatialSense.RoomScan.MeshPoint]::new($x,$y,$z) }
function Assert($condition,$message) { if (-not $condition) { throw $message } }
function Room {
    $points=[SpatialSense.RoomScan.MeshPoint[]]@((P 0 0 0),(P 6 0 0),(P 6 0 6),(P 0 0 6))
    [SpatialSense.RoomScan.ScanNavigationGrid]::Build($points,[int[]]@(0,1,2,0,2,3),0,0.03,[Threading.CancellationToken]::None,$true,6)
}
function Rays { ,([System.Collections.Generic.List[SpatialSense.RoomScan.DepthObservationRay]]::new()) }
function Add-Ray($rays,$from,$to) { $rays.Add([SpatialSense.RoomScan.DepthObservationRay]::new($from,$to)) }
function Route($map) {
    $path=[System.Collections.Generic.List[SpatialSense.RoomScan.MeshPoint]]::new(); [string]$reason=''
    $ok=$map.TryRoute(1,3,5,3,[ref]$path,[ref]$reason)
    @{ Success=$ok; Path=$path; Reason=$reason }
}
function Apply($map,$layer) { $map.ApplyLiveObstacles($layer.ActivePoints(),0.03) }

# An obstruction added after the baseline scan forces a detour. Missing data
# retains it; three positive observations through its old volume remove it.
$map=Room; $layer=[SpatialSense.RoomScan.LiveObstacleLayer]::new($map)
Assert (Route $map).Success 'Baseline route failed.'
$hits=Rays; $clears=Rays
for ($z=2.1; $z -lt 4; $z+=0.15) {
    $p=$map.Centre($map.Index(3,$z))
    Add-Ray $hits (P 1 1 $p.Z) (P $p.X 1 $p.Z)
    Add-Ray $clears (P 1 1 $p.Z) (P 6.1 1 $p.Z)
}
Assert (-not $layer.Observe($hits,10)) 'Single batch created a route restriction.'
Assert $layer.Observe($hits,10.16) 'Repeat obstruction was not confirmed.'
Assert ([Math]::Abs($layer.LastConfirmationDelayMs-160) -lt 0.01) 'Observation-to-confirmation timing is incorrect.'
Apply $map $layer
$detour=Route $map
Assert $detour.Success 'New obstruction prevented an available detour.'
$goesAround=$false
foreach ($p in $detour.Path) {
    Assert ($map.Cells[$map.Index($p.X,$p.Z)] -eq 2) 'Updated path crossed a blocked cell.'
    if ([Math]::Abs($p.Z-3) -gt 0.85) { $goesAround=$true }
}
Assert $goesAround 'Path threaded between sparse hits on the obstruction.'
$before=$layer.ActiveCount; $empty=Rays
for ($i=0;$i -lt 30;$i++) { [void]$layer.Observe($empty,11+$i*0.08) }
Assert ($layer.ActiveCount -eq $before) 'Missing/out-of-view data removed confirmed obstacles.'
Assert (-not $layer.Observe($clears,15)) 'One free batch cleared the obstacle.'
Assert (-not $layer.Observe($clears,15.1)) 'Two free batches cleared the obstacle.'
Assert $layer.Observe($clears,15.2) 'Repeated free-space evidence did not remove the obstacle.'
Assert ($layer.ActiveCount -eq 0 -and $layer.ClearedLastBatch -eq $before) 'Removal counters differ from removed evidence.'
Apply $map $layer
foreach ($p in (Route $map).Path) { Assert ([Math]::Abs($p.Z-3) -lt 0.1) 'Route did not return to the open corridor.' }

# A complete barrier reports no path; removal restores routing, without a map
# rebuild or editing the baseline cells.
$barrier=Rays; $barrierFree=Rays
for ($z=0.04;$z -lt 6;$z+=0.075) {
    $p=$map.Centre($map.Index(3,$z))
    if ($map.BaseCell($map.Index($p.X,$p.Z)) -ne 2) { continue }
    Add-Ray $barrier (P 1 1 $p.Z) (P $p.X 1 $p.Z)
    Add-Ray $barrierFree (P 1 1 $p.Z) (P 6.1 1 $p.Z)
}
[void]$layer.Observe($barrier,20); [void]$layer.Observe($barrier,20.1); Apply $map $layer
Assert (-not (Route $map).Success) 'Complete dynamic barrier retained a route.'
for ($i=0;$i -lt 3;$i++) { [void]$layer.Observe($barrierFree,21+$i*0.1) }
Apply $map $layer
Assert (Route $map).Success 'Route failed to recover after barrier removal.'

# Near-neighbour depth jitter is corroborated across batches, while duplicate
# rays within one batch cannot manufacture temporal confirmation.
$jitter=[SpatialSense.RoomScan.LiveObstacleLayer]::new($map)
$a=$map.Centre($map.Index(2,2)); $batch=Rays
for ($i=0;$i -lt 12;$i++) { Add-Ray $batch (P 1 1 $a.Z) (P $a.X 1 $a.Z) }
Assert (-not $jitter.Observe($batch,30)) 'Duplicate rays counted as separate time observations.'
$adjacent=Rays; Add-Ray $adjacent (P 1 1 $a.Z) (P ($a.X+0.075) 1 $a.Z)
Assert $jitter.Observe($adjacent,30.1) 'Head-motion jitter prevented corroborating nearby depth.'

# If the original item endpoint is obstructed, another nearby reachable point
# can be chosen without inventing a path through that obstruction.
$itemMap=Room; $path=[System.Collections.Generic.List[SpatialSense.RoomScan.MeshPoint]]::new()
$standing=P 0 0 0; [string]$reason=''
Assert ($itemMap.TryRouteToItem(1,3,5,3,[ref]$path,[ref]$standing,[ref]$reason,[Threading.CancellationToken]::None)) 'Initial item approach failed.'
$original=$standing; $endpoint=[SpatialSense.RoomScan.LiveObstacleLayer]::new($itemMap); $endpointHit=Rays
Add-Ray $endpointHit (P 1 1 $standing.Z) (P $standing.X 1 $standing.Z)
[void]$endpoint.Observe($endpointHit,40); [void]$endpoint.Observe($endpointHit,40.1); Apply $itemMap $endpoint
Assert ($itemMap.TryRouteToItem(1,3,5,3,[ref]$path,[ref]$standing,[ref]$reason,[Threading.CancellationToken]::None)) 'No alternative standing point was found.'
Assert ($itemMap.Index($standing.X,$standing.Z) -ne $itemMap.Index($original.X,$original.Z)) 'Blocked item endpoint was reused.'
foreach ($p in $path) { Assert ($itemMap.Cells[$itemMap.Index($p.X,$p.Z)] -eq 2) 'New item approach crossed a live block.' }

# Unconfirmed noise expires, and free rays cannot erase baseline furniture.
$noise=[SpatialSense.RoomScan.LiveObstacleLayer]::new($itemMap)
[void]$noise.Observe($endpointHit,50)
for ($i=0;$i -lt 20;$i++) { [void]$noise.Observe($empty,50.1+$i*0.1) }
Assert ($noise.TrackedCount -eq 0) 'Single unconfirmed noise hit did not expire.'
$staticPoints=[SpatialSense.RoomScan.MeshPoint[]]@(
    (P 0 0 0),(P 6 0 0),(P 6 0 6),(P 0 0 6),
    (P 3 0 0),(P 3 1.8 0),(P 3 1.8 6),(P 3 0 6))
$staticMap=[SpatialSense.RoomScan.ScanNavigationGrid]::Build($staticPoints,[int[]]@(0,1,2,0,2,3,4,5,6,4,6,7),0,0.03,[Threading.CancellationToken]::None,$true,6)
$staticLayer=[SpatialSense.RoomScan.LiveObstacleLayer]::new($staticMap)
for ($i=0;$i -lt 4;$i++) { [void]$staticLayer.Observe($barrierFree,60+$i*0.1) }
Apply $staticMap $staticLayer
Assert (-not (Route $staticMap).Success) 'Free observations erased a wall from the original scan.'
'PASS: dynamic detour, sparse-hit surface continuity, no-path barrier, positive-evidence recovery, missing-data retention, timing/counters, jitter corroboration, and alternate item endpoint.'
