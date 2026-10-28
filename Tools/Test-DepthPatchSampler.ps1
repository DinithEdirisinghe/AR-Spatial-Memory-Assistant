param([string]$UnityEditorData = 'D:\Unity Editor Version\Editor\Data')
$ErrorActionPreference = 'Stop'
$depthpatchProject = Split-Path -Parent $PSScriptRoot
$depthpatchRuntime = Join-Path $UnityEditorData 'NetCoreRuntime'
$depthpatchDotnet = Join-Path $depthpatchRuntime 'dotnet.exe'
$depthpatchCompiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$depthpatchFramework = Get-ChildItem -LiteralPath (Join-Path $depthpatchRuntime 'shared/Microsoft.NETCore.App') -Directory | Sort-Object Name -Descending | Select-Object -First 1
if (-not $depthpatchFramework) { throw 'Unity bundled .NET runtime not found. Set -UnityEditorData.' }
$depthpatchOutput = Join-Path $depthpatchProject '.utmp/depthpatch-tests'
[void][System.IO.Directory]::CreateDirectory($depthpatchOutput)
$depthpatchRsp = @('-nologo','-target:exe','-nostdlib+',('-out:"' + (Join-Path $depthpatchOutput 'depthpatch-tests.dll') + '"'))
$depthpatchRsp += Get-ChildItem -LiteralPath $depthpatchFramework.FullName -Filter '*.dll' | Where-Object {
    try { [void][System.Reflection.AssemblyName]::GetAssemblyName($_.FullName); $true } catch { $false }
} | ForEach-Object { '-r:"' + $_.FullName + '"' }
$depthpatchRsp += @('Tools/Test-DepthPatchSampler.cs','Assets/_Project/Dinith_Navigation/Scripts/DepthPatchSampler.cs','Assets/_Project/Dinith_Navigation/Scripts/DepthMeshRecord.cs','Assets/_Project/Dinith_Navigation/Scripts/SpatialMeshAccumulator.cs','Assets/_Project/Dinith_Navigation/Scripts/ScanNavigationGrid.cs','Assets/_Project/Dinith_Navigation/Scripts/SavedObjectRecord.cs') | ForEach-Object { '"' + (Join-Path $depthpatchProject $_) + '"' }
$depthpatchRspPath = Join-Path $depthpatchOutput 'tests.rsp'
[System.IO.File]::WriteAllLines($depthpatchRspPath,$depthpatchRsp,[System.Text.UTF8Encoding]::new($false))
& $depthpatchDotnet $depthpatchCompiler ('@' + $depthpatchRspPath)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$depthpatchConfig = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name='Microsoft.NETCore.App'; version=$depthpatchFramework.Name } } } | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $depthpatchOutput 'depthpatch-tests.runtimeconfig.json'),$depthpatchConfig,[System.Text.UTF8Encoding]::new($false))
& $depthpatchDotnet (Join-Path $depthpatchOutput 'depthpatch-tests.dll') $depthpatchOutput
exit $LASTEXITCODE
