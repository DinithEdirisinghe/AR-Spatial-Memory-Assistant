param([string]$UnityEditorData = 'D:\Unity Editor Version\Editor\Data')
$ErrorActionPreference = 'Stop'
$multiroomProject = Split-Path -Parent $PSScriptRoot
$multiroomRuntime = Join-Path $UnityEditorData 'NetCoreRuntime'
$multiroomDotnet = Join-Path $multiroomRuntime 'dotnet.exe'
$multiroomCompiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$multiroomFramework = Get-ChildItem -LiteralPath (Join-Path $multiroomRuntime 'shared/Microsoft.NETCore.App') -Directory | Sort-Object Name -Descending | Select-Object -First 1
if (-not $multiroomFramework) { throw 'Unity bundled .NET runtime not found. Set -UnityEditorData.' }
$multiroomOutput = Join-Path $multiroomProject '.utmp/multiroom-tests'
[void][System.IO.Directory]::CreateDirectory($multiroomOutput)
$multiroomRsp = @('-nologo','-target:exe','-nostdlib+',('-out:"' + (Join-Path $multiroomOutput 'multiroom-tests.dll') + '"'))
$multiroomRsp += Get-ChildItem -LiteralPath $multiroomFramework.FullName -Filter '*.dll' | Where-Object {
    try { [void][System.Reflection.AssemblyName]::GetAssemblyName($_.FullName); $true } catch { $false }
} | ForEach-Object { '-r:"' + $_.FullName + '"' }
$multiroomRsp += @('Tools/Test-MultiRoom.cs','Assets/_Project/Dinith_Navigation/Scripts/RoomConnectionGraph.cs','Assets/_Project/Dinith_Navigation/Scripts/DepthMeshRecord.cs','Assets/_Project/Dinith_Navigation/Scripts/SpatialMeshAccumulator.cs','Assets/_Project/Dinith_Navigation/Scripts/ScanNavigationGrid.cs','Assets/_Project/Dinith_Navigation/Scripts/SavedObjectRecord.cs') | ForEach-Object { '"' + (Join-Path $multiroomProject $_) + '"' }
$multiroomRspPath = Join-Path $multiroomOutput 'tests.rsp'
[System.IO.File]::WriteAllLines($multiroomRspPath,$multiroomRsp,[System.Text.UTF8Encoding]::new($false))
& $multiroomDotnet $multiroomCompiler ('@' + $multiroomRspPath)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$multiroomConfig = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name='Microsoft.NETCore.App'; version=$multiroomFramework.Name } } } | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $multiroomOutput 'multiroom-tests.runtimeconfig.json'),$multiroomConfig,[System.Text.UTF8Encoding]::new($false))
& $multiroomDotnet (Join-Path $multiroomOutput 'multiroom-tests.dll') $multiroomOutput
exit $LASTEXITCODE
