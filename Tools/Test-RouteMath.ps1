$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path (Split-Path -Parent $PSScriptRoot) 'Assets/_Project/Dinith_Navigation/Scripts/RouteMath.cs')
$cases = @(
    @{Name='Crosses through furniture'; A=@(-2,0,2,0); Expected=$true},
    @{Name='Clear parallel path'; A=@(-2,2,2,2); Expected=$false},
    @{Name='Starts inside furniture'; A=@(0,0,3,3); Expected=$true},
    @{Name='Ends inside furniture'; A=@(3,0,0,0); Expected=$true},
    @{Name='Touching a corner'; A=@(-2,0,0,2); Expected=$true},
    @{Name='Stationary point inside'; A=@(0,0,0,0); Expected=$true},
    @{Name='Stationary point outside'; A=@(2,2,2,2); Expected=$false},
    @{Name='Clear vertical segment'; A=@(2,-3,2,3); Expected=$false},
    @{Name='Boundary travel'; A=@(-2,1,2,1); Expected=$true},
    @{Name='Almost touching but clear'; A=@(-2,1.01,2,1.01); Expected=$false}
)
foreach ($case in $cases) {
    $a = $case.A
    $actual = [SpatialSense.Navigation.RouteMath]::SegmentIntersectsRectangle($a[0],$a[1],$a[2],$a[3],-1,-1,1,1)
    if ($actual -ne $case.Expected) { throw "Failed: $($case.Name)" }
}
$random = New-Object System.Random(42)
$clearanceCases = @(
    @{Name='Clear rounded corner'; A=@(1.25,1.25,1.25,1.25); Expected=$false},
    @{Name='Too close to rounded corner'; A=@(1.2,1.2,1.2,1.2); Expected=$true},
    @{Name='Valid segment around rounded corner'; A=@(1.4,1.1,1.1,1.4); Expected=$false},
    @{Name='Segment cutting rounded corner'; A=@(1.3,1.1,1.1,1.3); Expected=$true},
    @{Name='Too close alongside edge'; A=@(-0.5,1.2,0.5,1.2); Expected=$true},
    @{Name='Clear alongside edge'; A=@(-0.5,1.4,0.5,1.4); Expected=$false},
    @{Name='Crossing actual furniture'; A=@(-2,0,2,0); Expected=$true}
)
foreach ($case in $clearanceCases) {
    $a = $case.A
    $actual = [SpatialSense.Navigation.RouteMath]::SegmentViolatesClearance($a[0],$a[1],$a[2],$a[3],-1,-1,1,1,0.3)
    if ($actual -ne $case.Expected) { throw "Failed: $($case.Name)" }
}
$carvingWidth = [SpatialSense.Navigation.RouteMath]::CarvingWidth(1,0.03)
if ([Math]::Abs($carvingWidth - 1.06) -gt 0.00001) { throw 'Scan padding regression: body clearance was added to carving width.' }
$gapBlocked = [SpatialSense.Navigation.RouteMath]::SegmentViolatesClearance(0,-0.5,0,0.5,-2,-1,-0.42,1,0.3)
$gapBlocked = $gapBlocked -or [SpatialSense.Navigation.RouteMath]::SegmentViolatesClearance(0,-0.5,0,0.5,0.42,-1,2,1,0.3)
if ($gapBlocked) { throw 'A 0.9m clear corridor was falsely blocked by duplicate padding.' }
$narrowBlocked = [SpatialSense.Navigation.RouteMath]::SegmentViolatesClearance(0,-0.5,0,0.5,-2,-1,-0.22,1,0.3)
if (-not $narrowBlocked) { throw 'Insufficient-clearance corridor was incorrectly allowed.' }
for ($i = 0; $i -lt 1000; $i++) {
    $a = @((($random.NextDouble()-0.5)*8), (($random.NextDouble()-0.5)*8), (($random.NextDouble()-0.5)*8), (($random.NextDouble()-0.5)*8))
    $forward = [SpatialSense.Navigation.RouteMath]::SegmentIntersectsRectangle($a[0],$a[1],$a[2],$a[3],-1,-1,1,1)
    $reverse = [SpatialSense.Navigation.RouteMath]::SegmentIntersectsRectangle($a[2],$a[3],$a[0],$a[1],-1,-1,1,1)
    if ($forward -ne $reverse) { throw 'Direction symmetry failed' }
    $translated = [SpatialSense.Navigation.RouteMath]::SegmentIntersectsRectangle(($a[0]+10),($a[1]+10),($a[2]+10),($a[3]+10),9,9,11,11)
    if ($forward -ne $translated) { throw 'Translation invariance failed' }
    $roundedForward = [SpatialSense.Navigation.RouteMath]::SegmentViolatesClearance($a[0],$a[1],$a[2],$a[3],-1,-1,1,1,0.3)
    $roundedReverse = [SpatialSense.Navigation.RouteMath]::SegmentViolatesClearance($a[2],$a[3],$a[0],$a[1],-1,-1,1,1,0.3)
    if ($roundedForward -ne $roundedReverse) { throw 'Rounded-clearance direction symmetry failed' }
    $roundedTranslated = [SpatialSense.Navigation.RouteMath]::SegmentViolatesClearance(($a[0]+10),($a[1]+10),($a[2]+10),($a[3]+10),9,9,11,11,0.3)
    if ($roundedForward -ne $roundedTranslated) { throw 'Rounded-clearance translation invariance failed' }
}
'PASS: 17 collision cases, 3 padding/corridor regressions, 4000 symmetry/translation checks.'
