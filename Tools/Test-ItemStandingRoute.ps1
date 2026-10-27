$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/DepthMeshRecord.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/SpatialMeshAccumulator.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/ScanNavigationGrid.cs')
)
function P([float]$x,[float]$y,[float]$z) { [SpatialSense.RoomScan.MeshPoint]::new($x,$y,$z) }
function Assert($condition,$reason) { if (-not $condition) { throw $reason } }
function Geometry { @{ Points=[System.Collections.Generic.List[SpatialSense.RoomScan.MeshPoint]]::new(); Indices=[System.Collections.Generic.List[int]]::new() } }
function Quad($g,$a,$b,$c,$d) {
    $offset=$g.Points.Count
    foreach ($p in @($a,$b,$c,$d)) { $g.Points.Add($p) }
    foreach ($i in @(0,1,2,0,2,3)) { $g.Indices.Add($offset+$i) }
}
function Room {
    $g=Geometry
    Quad $g (P 0 0 0) (P 6 0 0) (P 6 0 6) (P 0 0 6)
    return $g
}
function Build($g) { [SpatialSense.RoomScan.ScanNavigationGrid]::Build($g.Points,$g.Indices,0,0.03,[Threading.CancellationToken]::None,$true,6) }
function Find($map,[float]$sx,[float]$sz,[float]$x,[float]$z) {
    $path=[System.Collections.Generic.List[SpatialSense.RoomScan.MeshPoint]]::new()
    $standing=P 0 0 0; [string]$reason=''
    $success=$map.TryRouteToItem($sx,$sz,$x,$z,[ref]$path,[ref]$standing,[ref]$reason,[Threading.CancellationToken]::None)
    @{ Success=$success; Path=$path; Standing=$standing; Reason=$reason }
}
$table=Room
Quad $table (P 2.5 0.8 2.5) (P 3.5 0.8 2.5) (P 3.5 0.8 3.5) (P 2.5 0.8 3.5)
$map=Build $table
$result=Find $map 1 1 3 3
Assert $result.Success 'No standing route near a tabletop item.'
$s=$result.Standing
$distance=[Math]::Sqrt(($s.X-3)*($s.X-3)+($s.Z-3)*($s.Z-3))
Assert ($distance -ge 0.25 -and $distance -le 1.1) 'Standing point is outside the item inspection range.'
Assert ($distance -lt 0.65) 'Tabletop guidance stopped early instead of reaching the table edge.'
Assert (-not ($s.X -gt 2.5 -and $s.X -lt 3.5 -and $s.Z -gt 2.5 -and $s.Z -lt 3.5)) 'Standing point is under the table.'
foreach ($p in $result.Path) { Assert ($map.Cells[$map.Index($p.X,$p.Z)] -eq 2) 'Item route crossed blocked furniture.' }
Assert (-not (Find $map 1 1 9 9).Success) 'Off-map item produced a made-up standing point.'
$partition=Room
Quad $partition (P 3 0 0) (P 3 1.8 0) (P 3 1.8 6) (P 3 0 6)
Assert (-not (Find (Build $partition) 1 3 5 3).Success) 'Standing point was chosen across an impassable wall.'
$open=Build (Room)
$floorItem=Find $open 1 1 3 3
Assert $floorItem.Success 'Floor item had no nearby standing point.'
foreach ($approach in @(@(1,1),@(5,1),@(1,5),@(5,5),@(3,2.1))) {
    $result=Find $open $approach[0] $approach[1] 3 3
    Assert $result.Success 'A clear approach to a floor item failed.'
    $s=$result.Standing
    $distance=[Math]::Sqrt(($s.X-3)*($s.X-3)+($s.Z-3)*($s.Z-3))
    Assert ($distance -ge 0.25 -and $distance -lt 0.34) "Floor-item guidance stopped $distance metres short."
    Assert ($result.Path.Count -gt 1) 'Guidance said arrived while still almost a metre from the item.'
}
$blocked=Build (Room)
for ($i=0; $i -lt $blocked.CellCount; $i++) {
    $p=$blocked.Centre($i)
    if (($p.X-3)*($p.X-3)+($p.Z-3)*($p.Z-3) -lt 1.4*1.4) { $blocked.Cells[$i]=1 }
}
Assert (-not (Find $blocked 1 1 3 3).Success) 'No reachable item neighbourhood was reported as success.'
$snapshot=$open.RoutingSnapshot()
for ($i=0; $i -lt $open.CellCount; $i++) { $open.Cells[$i]=1 }
Assert ((Find $snapshot 1 1 3 3).Success) 'Worker snapshot changed with the live map.'
Assert (-not (Find $open 1 1 3 3).Success) 'Blocked live map retained a route.'
$cancel=[Threading.CancellationTokenSource]::new(); $cancel.Cancel(); $cancelled=$false
try {
    $path=[System.Collections.Generic.List[SpatialSense.RoomScan.MeshPoint]]::new(); $standing=P 0 0 0; [string]$reason=''
    [void]$snapshot.TryRouteToItem(1,1,3,3,[ref]$path,[ref]$standing,[ref]$reason,$cancel.Token)
} catch { $cancelled=$_.Exception.InnerException -is [OperationCanceledException] }
Assert $cancelled 'Planning cancellation was ignored.'
$cancel.Dispose()
'PASS: tabletop/floor item standing routes, no path through furniture, unreachable/off-map targets, blocked neighbourhoods, snapshot isolation and cancellation.'
