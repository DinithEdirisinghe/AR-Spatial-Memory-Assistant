param([string]$UnityEditorData = 'D:\Unity Editor Version\Editor\Data')
$ErrorActionPreference = 'Stop'
$homemapProject = Split-Path -Parent $PSScriptRoot
$homemapRuntime = Join-Path $UnityEditorData 'NetCoreRuntime'
$homemapDotnet = Join-Path $homemapRuntime 'dotnet.exe'
$homemapCompiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$homemapFramework = Get-ChildItem -LiteralPath (Join-Path $homemapRuntime 'shared/Microsoft.NETCore.App') -Directory | Sort-Object Name -Descending | Select-Object -First 1
if (-not $homemapFramework) { throw 'Unity bundled .NET runtime not found. Set -UnityEditorData.' }
$homemapOutput = Join-Path $homemapProject '.utmp/homemap-tests'
[void][System.IO.Directory]::CreateDirectory($homemapOutput)
$homemapRsp = @('-nologo','-target:exe','-nostdlib+',('-out:"' + (Join-Path $homemapOutput 'homemap-tests.dll') + '"'))
$homemapRsp += Get-ChildItem -LiteralPath $homemapFramework.FullName -Filter '*.dll' | Where-Object {
    try { [void][System.Reflection.AssemblyName]::GetAssemblyName($_.FullName); $true } catch { $false }
} | ForEach-Object { '-r:"' + $_.FullName + '"' }
$homemapRsp += @('Tools/Test-HomeMap.cs','Assets/_Project/Dinith_Navigation/Scripts/HomeMap.cs','Assets/_Project/Dinith_Navigation/Scripts/HomeLiveUpdates.cs','Assets/_Project/Dinith_Navigation/Scripts/HomeRoutePlanner.cs','Assets/_Project/Dinith_Navigation/Scripts/LiveObstacleLayer.cs','Assets/_Project/Dinith_Navigation/Scripts/DepthPatchSampler.cs','Assets/_Project/Dinith_Navigation/Scripts/DepthMeshRecord.cs','Assets/_Project/Dinith_Navigation/Scripts/SpatialMeshAccumulator.cs','Assets/_Project/Dinith_Navigation/Scripts/ScanNavigationGrid.cs','Assets/_Project/Dinith_Navigation/Scripts/SavedObjectRecord.cs') | ForEach-Object { '"' + (Join-Path $homemapProject $_) + '"' }
$homemapRspPath = Join-Path $homemapOutput 'tests.rsp'
[System.IO.File]::WriteAllLines($homemapRspPath,$homemapRsp,[System.Text.UTF8Encoding]::new($false))
& $homemapDotnet $homemapCompiler ('@' + $homemapRspPath)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$homemapConfig = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name='Microsoft.NETCore.App'; version=$homemapFramework.Name } } } | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $homemapOutput 'homemap-tests.runtimeconfig.json'),$homemapConfig,[System.Text.UTF8Encoding]::new($false))
& $homemapDotnet (Join-Path $homemapOutput 'homemap-tests.dll') $homemapOutput
exit $LASTEXITCODE
