$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path @(
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/DepthMeshRecord.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/SpatialMeshAccumulator.cs'),
    (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/ScanNavigationGrid.cs')
)
function P([float]$x,[float]$y,[float]$z) { [SpatialSense.RoomScan.MeshPoint]::new($x,$y,$z) }
function Assert($condition,$reason) { if (-not $condition) { throw $reason } }
function New-Geometry { @{ Points = [System.Collections.Generic.List[SpatialSense.RoomScan.MeshPoint]]::new(); Indices = [System.Collections.Generic.List[int]]::new() } }
function Quad($g,$a,$b,$c,$d) {
    $offset = $g.Points.Count
    $g.Points.Add($a); $g.Points.Add($b); $g.Points.Add($c); $g.Points.Add($d)
    foreach ($i in @(0,1,2,0,2,3)) { $g.Indices.Add($offset+$i) }
}
function Floor($g,[float]$x0,[float]$z0,[float]$x1,[float]$z1,[float]$y=0) {
    Quad $g (P $x0 $y $z0) (P $x1 $y $z0) (P $x1 $y $z1) (P $x0 $y $z1)
}
function Build($g,[float]$radius=0.2,[float]$y=0,[bool]$strict=$false) { [SpatialSense.RoomScan.ScanNavigationGrid]::Build($g.Points,$g.Indices,$y,$radius,[Threading.CancellationToken]::None,$strict) }
function Route($map,[float]$sx,[float]$sz,[float]$gx,[float]$gz) {
    $route = [System.Collections.Generic.List[SpatialSense.RoomScan.MeshPoint]]::new(); [string]$reason = ''
    $success = $map.TryRoute($sx,$sz,$gx,$gz,[ref]$route,[ref]$reason)
    @{ Success=$success; Points=$route; Reason=$reason }
}
$open = New-Geometry; Floor $open 0 0 6 6
$map = Build $open
$direct = Route $map 1 1 5 1
Assert $direct.Success 'Open observed floor did not yield a route.'
Assert ($map.ObservedCount -gt $map.WalkableCount) 'Unknown boundaries were not kept away from the route area.'
$room = New-Geometry; Floor $room 0 0 6 6
Quad $room (P 3 0 0) (P 3 1.5 0) (P 3 1.5 4.5) (P 3 0 4.5)
$around = Build $room
$detour = Route $around 1 1 5 1
Assert $detour.Success 'Could not route around the end of a captured wall.'
Assert ($detour.Points.Count -gt $direct.Points.Count) 'Wall did not cause a detour.'
foreach ($p in $detour.Points) {
    Assert (-not ($p.X -gt 2.75 -and $p.X -lt 3.25 -and $p.Z -lt 4.7)) 'Route crossed captured wall/clearance.'
}
$split = New-Geometry; Floor $split 0 0 2.5 6; Floor $split 3.5 0 6 6
$splitMap = Build $split
Assert ($splitMap.Cells[$splitMap.Index(3,3)] -eq 0) 'Unscanned gap was declared observed floor.'
Assert (-not (Route $splitMap 1 1 5 1).Success) 'Route crossed an unscanned stripe.'
Assert ($splitMap.CoverageEdgeCount -gt 0) 'Coverage gaps were not identified separately.'
Assert ($splitMap.ObstacleCount -eq 0) 'Unknown floor edges were labelled as obstacles.'
Assert ($around.ObstacleCount -gt 0) 'Captured wall did not produce red obstacle cells.'
$thin = Build $room 0.03
$zero = Build $room 0
Assert ($thin.WalkableCount -gt $around.WalkableCount) 'Thin margin did not recover route area.'
Assert ($around.MarginCount -gt $thin.MarginCount) 'Margin reduction did not remove buffer cells.'
Assert ($around.ObstacleEvidenceCount -eq $thin.ObstacleEvidenceCount) 'Changing margin altered the captured obstacles.'
Assert ($zero.Cells[$zero.Index(3,2)] -eq 1) 'Zero extra margin removed the actual wall.'
Assert ($around.Cells[$around.Index(2.85,2)] -eq 4) 'Extra margin was not distinct from obstacle evidence.'
Assert ((Route $thin 1 1 5 1).Success) 'Thin-margin room lost the wall detour.'
# A 2 cm mesh crack catches one inset corner of a cell; its centre and three
# corners remain directly observed. Normal coverage tolerates that corner only.
$seam = New-Geometry
Floor $seam 0 0 1.943 6
Floor $seam 1.963 0 6 6
Floor $seam 1.943 0 1.963 1.943
Floor $seam 1.943 1.963 1.963 6
$normalSeam = Build $seam
$strictSeam = Build $seam 0.2 0 $true
Assert ($normalSeam.WalkableCount -gt $strictSeam.WalkableCount) 'Normal coverage did not tolerate a missed corner.'
Assert ($strictSeam.CoverageEdgeCount -gt $normalSeam.CoverageEdgeCount) 'Strict coverage did not expose corner evidence gaps.'
Assert ($normalSeam.ObstacleCount -eq 0 -and $strictSeam.ObstacleCount -eq 0) 'Mesh seams became physical obstacles.'
# The route centre is 22.5 cm from this obstacle cell's edge; 20 cm clearance
# must not silently include another half-cell radius and reject it.
$clearanceCell = $around.Index(2.625,1.575)
Assert ($around.Cells[$clearanceCell] -eq 2) 'Clearance was expanded by an extra half-cell around the route centre.'
Assert (-not (Route $map -1 -1 2 2).Success) 'Outside start accepted.'
Assert (-not (Route $map 2 2 8 8).Success) 'Outside destination accepted.'
$wide = Build $room 0.4
Assert ($wide.WalkableCount -lt $around.WalkableCount) 'Increasing clearance did not reduce route area.'
$overhead = New-Geometry; Floor $overhead 0 0 6 6; Floor $overhead 0 0 6 6 2.5
Assert ((Route (Build $overhead) 1 1 5 1).Success) 'Overhead surface blocked floor routing.'
$table = New-Geometry; Floor $table 0 0 6 6 0.8
$rejected = $false
try { $null = Build $table } catch { $rejected = $true }
Assert $rejected 'Table height was treated as the selected floor.'
$shifted = New-Geometry
foreach ($p in $room.Points) { $shifted.Points.Add((P ($p.X+9) ($p.Y+1) ($p.Z-3))) }
foreach ($i in $room.Indices) { $shifted.Indices.Add($i) }
Assert ((Route (Build $shifted 0.2 1) 10 -2 14 -2).Success) 'Translated/raised anchored geometry lost routing.'
$corners = Build $open 0
for ($i=0; $i -lt $corners.Cells.Length; $i++) { $corners.Cells[$i] = 0 }
$startIndex = $corners.Index(2,2); $endIndex = $startIndex + $corners.Width + 1
$corners.Cells[$startIndex] = 2; $corners.Cells[$endIndex] = 2
$sp = $corners.Centre($startIndex); $ep = $corners.Centre($endIndex)
Assert (-not (Route $corners $sp.X $sp.Z $ep.X $ep.Z).Success) 'Diagonal path cut through blocked orthogonal cells.'
$large = New-Geometry; Floor $large 0 0 25 25
$rejected = $false
try { $null = Build $large } catch { $rejected = $true }
Assert $rejected 'Oversized map was not bounded.'
'PASS: observed-floor routes, wall detour, unknown gaps, bounds, clearance, overhead/table discrimination, coordinate translation, diagonal corner blocking and map size limits.'
