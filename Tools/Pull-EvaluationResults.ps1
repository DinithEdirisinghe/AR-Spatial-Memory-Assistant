param(
    [string]$AdbPath = '',
    [string]$DeviceSerial = '',
    [string]$OutputRoot = ''
)
$ErrorActionPreference = 'Stop'
$evaluationProject = Split-Path -Parent $PSScriptRoot
if (-not $AdbPath) {
    $evaluationAdbCommand = Get-Command adb -ErrorAction SilentlyContinue
    if ($evaluationAdbCommand) { $AdbPath = $evaluationAdbCommand.Source }
    else {
        $evaluationAdbCandidates = @(
            'D:\Unity Editor Version\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
        )
        if ($env:ANDROID_SDK_ROOT) { $evaluationAdbCandidates += Join-Path $env:ANDROID_SDK_ROOT 'platform-tools/adb.exe' }
        if ($env:ANDROID_HOME) { $evaluationAdbCandidates += Join-Path $env:ANDROID_HOME 'platform-tools/adb.exe' }
        $AdbPath = $evaluationAdbCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    }
}
if (-not $AdbPath -or -not (Test-Path -LiteralPath $AdbPath -PathType Leaf)) { throw 'ADB not found. Pass -AdbPath with the adb.exe in your Unity Android SDK.' }
$evaluationSettings = Get-Content -LiteralPath (Join-Path $evaluationProject 'ProjectSettings/ProjectSettings.asset') -Raw
$evaluationPackage = [regex]::Match($evaluationSettings, '(?m)^\s{4}Android:\s+([A-Za-z0-9_.]+)\s*$').Groups[1].Value
if (-not $evaluationPackage) { throw 'Could not read the Android package name from this Unity project.' }
$evaluationDeviceLines = & $AdbPath devices
if ($LASTEXITCODE -ne 0) { throw 'Could not query connected headsets.' }
$evaluationDevices = @($evaluationDeviceLines | ForEach-Object { if ($_ -match '^(\S+)\s+device$') { $Matches[1] } })
if ($DeviceSerial) {
    if ($DeviceSerial -notin $evaluationDevices) { throw 'The selected device is not connected and authorised. Check USB debugging in the headset.' }
}
elseif ($evaluationDevices.Count -eq 1) { $DeviceSerial = $evaluationDevices[0] }
else { throw 'Connect one authorised headset, or pass -DeviceSerial. Allow USB debugging inside the headset if prompted.' }
if (-not $OutputRoot) { $OutputRoot = Join-Path $evaluationProject 'EvaluationResults' }
$evaluationDownload = Join-Path ([System.IO.Path]::GetFullPath($OutputRoot)) ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8))
[void][System.IO.Directory]::CreateDirectory($evaluationDownload)
$evaluationRemote = '/sdcard/Android/data/' + $evaluationPackage + '/files/evaluation'
& $AdbPath -s $DeviceSerial pull $evaluationRemote $evaluationDownload
if ($LASTEXITCODE -ne 0) { throw 'Download failed. Run a trial in the v9 app and export CSV first, then retry. Existing headset files were kept.' }
Write-Output ('Downloaded evaluation data to: ' + $evaluationDownload)
Write-Output 'Open the latest complete-*.txt in evaluation/exports and use its matching summary/events CSV pair. Each export is a cumulative snapshot.'
