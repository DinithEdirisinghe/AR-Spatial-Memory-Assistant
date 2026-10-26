$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/SavedObjectRecord.cs')

function New-ValidRecord {
    $record = New-Object SpatialSense.Persistence.SavedObjectRecord
    $record.version = 1
    $record.objectName = 'My keys'
    $record.anchorUuid = [Guid]::NewGuid().ToString('D')
    $record.roomUuid = [Guid]::NewGuid().ToString('D')
    $record.savedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    return $record
}

$valid = New-ValidRecord
if (-not $valid.IsValid()) { throw 'A valid anchor record was rejected.' }
foreach ($test in @('badVersion','badName','badAnchor','emptyAnchor','badRoom','emptyRoom','badTimestamp')) {
    $record = New-ValidRecord
    switch ($test) {
        'badVersion' { $record.version = 99 }
        'badName' { $record.objectName = 'Unrelated object' }
        'badAnchor' { $record.anchorUuid = 'not-a-guid' }
        'emptyAnchor' { $record.anchorUuid = [Guid]::Empty.ToString('D') }
        'badRoom' { $record.roomUuid = 'not-a-room' }
        'emptyRoom' { $record.roomUuid = [Guid]::Empty.ToString('D') }
        'badTimestamp' { $record.savedAtUtc = 'not-a-date' }
    }
    if ($record.IsValid()) { throw "Invalid record accepted: $test" }
}

$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')
$testDirectory = Join-Path $temporaryRoot ('SpatialSenseAnchorTests-' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    $path = Join-Path $testDirectory 'record.json'
    [SpatialSense.Persistence.AtomicAnchorRecordStore]::Write($path, '{"record":"first"}')
    if ([System.IO.File]::ReadAllText($path) -ne '{"record":"first"}') { throw 'Initial write failed.' }
    [SpatialSense.Persistence.AtomicAnchorRecordStore]::Write($path, '{"record":"second"}')
    if ([System.IO.File]::ReadAllText($path) -ne '{"record":"second"}') { throw 'Replacement failed.' }
    if ([System.IO.File]::ReadAllText($path + '.bak') -ne '{"record":"first"}') { throw 'Previous record was not preserved during replacement.' }
    [System.IO.File]::WriteAllText($path + '.tmp', 'interrupted incomplete write')
    if ([System.IO.File]::ReadAllText($path) -ne '{"record":"second"}') { throw 'Interrupted staging write affected the live record.' }
    [SpatialSense.Persistence.AtomicAnchorRecordStore]::Write($path, '{"record":"third"}')
    if ([System.IO.File]::ReadAllText($path) -ne '{"record":"third"}') { throw 'Recovery from abandoned staging file failed.' }
    $rejected = $false
    try { [SpatialSense.Persistence.AtomicAnchorRecordStore]::Write($path, '') } catch { $rejected = $true }
    if (-not $rejected -or [System.IO.File]::ReadAllText($path) -ne '{"record":"third"}') { throw 'An invalid replacement damaged the live record.' }

    $blockedPath = Join-Path $testDirectory 'blocked.json'
    [System.IO.File]::WriteAllText($blockedPath, 'previous valid record')
    [System.IO.Directory]::CreateDirectory($blockedPath + '.bak') | Out-Null
    $failed = $false
    try { [SpatialSense.Persistence.AtomicAnchorRecordStore]::Write($blockedPath, 'replacement') } catch { $failed = $true }
    if (-not $failed -or [System.IO.File]::ReadAllText($blockedPath) -ne 'previous valid record') { throw 'A failed commit did not preserve the old record.' }
    'PASS: record validation, atomic replacement, interrupted-write recovery, and failed-replacement preservation.'
}
finally {
    $resolvedTestDirectory = [System.IO.Path]::GetFullPath($testDirectory)
    if ([System.IO.Path]::GetDirectoryName($resolvedTestDirectory) -ne $temporaryRoot -or
        -not [System.IO.Path]::GetFileName($resolvedTestDirectory).StartsWith('SpatialSenseAnchorTests-')) {
        throw 'Refusing cleanup outside the named temporary test directory.'
    }
    Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
}
