param([string]$UnityEditorData = 'D:\Unity Editor Version\Editor\Data')
$ErrorActionPreference = 'Stop'
$evaluationProject = Split-Path -Parent $PSScriptRoot
$evaluationRuntime = Join-Path $UnityEditorData 'NetCoreRuntime'
$evaluationDotnet = Join-Path $evaluationRuntime 'dotnet.exe'
$evaluationCompiler = Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'
$evaluationFramework = Get-ChildItem -LiteralPath (Join-Path $evaluationRuntime 'shared/Microsoft.NETCore.App') -Directory | Sort-Object Name -Descending | Select-Object -First 1
if (-not $evaluationFramework) { throw 'Unity bundled .NET runtime not found. Set -UnityEditorData.' }
$evaluationOutput = Join-Path $evaluationProject '.utmp/evaluation-tests'
[void][System.IO.Directory]::CreateDirectory($evaluationOutput)
$evaluationRsp = @('-nologo','-target:exe','-nostdlib+',('-out:"' + (Join-Path $evaluationOutput 'evaluation-tests.dll') + '"'))
$evaluationRsp += Get-ChildItem -LiteralPath $evaluationFramework.FullName -Filter '*.dll' | Where-Object {
    try { [void][System.Reflection.AssemblyName]::GetAssemblyName($_.FullName); $true } catch { $false }
} | ForEach-Object { '-r:"' + $_.FullName + '"' }
$evaluationRsp += @('Tools/Test-Evaluation.cs','Assets/_Project/Dinith_Navigation/Scripts/NavigationTrial.cs','Assets/_Project/Dinith_Navigation/Scripts/NavigationTrialStore.cs','Assets/_Project/Dinith_Navigation/Scripts/SavedObjectRecord.cs') | ForEach-Object { '"' + (Join-Path $evaluationProject $_) + '"' }
$evaluationRspPath = Join-Path $evaluationOutput 'tests.rsp'
[System.IO.File]::WriteAllLines($evaluationRspPath,$evaluationRsp,[System.Text.UTF8Encoding]::new($false))
& $evaluationDotnet $evaluationCompiler ('@' + $evaluationRspPath)
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$evaluationConfig = @{ runtimeOptions = @{ tfm = 'net6.0'; framework = @{ name='Microsoft.NETCore.App'; version=$evaluationFramework.Name } } } | ConvertTo-Json -Depth 5
[System.IO.File]::WriteAllText((Join-Path $evaluationOutput 'evaluation-tests.runtimeconfig.json'),$evaluationConfig,[System.Text.UTF8Encoding]::new($false))
& $evaluationDotnet (Join-Path $evaluationOutput 'evaluation-tests.dll') $evaluationOutput
exit $LASTEXITCODE
