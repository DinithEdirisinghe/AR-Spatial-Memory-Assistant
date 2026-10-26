$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
Add-Type -Path (Join-Path $projectRoot 'Assets/_Project/Dinith_Navigation/Scripts/SavedObjectRecord.cs')
$names = [SpatialSense.Persistence.SavedObjectCatalog]::Names
if ($names.Count -ne 3) { throw 'Expected exactly three named slots.' }
$files = @($names | ForEach-Object { [SpatialSense.Persistence.SavedObjectCatalog]::FileNameFor($_) })
if (@($files | Select-Object -Unique).Count -ne 3) { throw 'Object slots share a filename.' }
if ($files[0] -ne 'spatialsense-my-keys-v1.json') { throw 'Legacy Keys filename changed.' }
foreach ($name in $names) {
    $record = New-Object SpatialSense.Persistence.SavedObjectRecord
    $record.version = 1
    $record.objectName = $name
    $record.anchorUuid = [Guid]::NewGuid().ToString('D')
    $record.roomUuid = [Guid]::NewGuid().ToString('D')
    $record.savedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    if (-not $record.IsValid()) { throw "Record rejected: $name" }
    $record.forgotten = $true
    $record.anchorUuid = $null
    $record.roomUuid = $null
    if (-not $record.IsValid()) { throw "Forget marker rejected: $name" }
    $record.forgotten = $false
    if ($record.IsValid()) { throw 'An anchorless normal record was accepted.' }
}
$badNameRejected = $false
try { [SpatialSense.Persistence.SavedObjectCatalog]::FileNameFor('../another-file') } catch { $badNameRejected = $true }
if (-not $badNameRejected) { throw 'An unsupported object name became a file path.' }

$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\')
$testDirectory = Join-Path $temporaryRoot ('SpatialSenseCatalogTests-' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($testDirectory) | Out-Null
try {
    for ($i = 0; $i -lt 3; $i++) {
        [SpatialSense.Persistence.AtomicAnchorRecordStore]::Write((Join-Path $testDirectory $files[$i]), "original-slot-$i")
    }
    [SpatialSense.Persistence.AtomicAnchorRecordStore]::Write((Join-Path $testDirectory $files[1]), 'updated-wallet')
    if ([System.IO.File]::ReadAllText((Join-Path $testDirectory $files[0])) -ne 'original-slot-0' -or
        [System.IO.File]::ReadAllText((Join-Path $testDirectory $files[2])) -ne 'original-slot-2') { throw 'Updating Wallet affected another object.' }
    [SpatialSense.Persistence.AtomicAnchorRecordStore]::Write((Join-Path $testDirectory $files[0]), '{"version":1,"objectName":"My keys","forgotten":true}')
    if ([System.IO.File]::ReadAllText((Join-Path $testDirectory $files[1])) -ne 'updated-wallet' -or
        [System.IO.File]::ReadAllText((Join-Path $testDirectory $files[2])) -ne 'original-slot-2') { throw 'Forgetting Keys affected another object.' }
    $forgotten = [System.IO.File]::ReadAllText((Join-Path $testDirectory $files[0])) | ConvertFrom-Json
    if (-not $forgotten.forgotten) { throw 'The forgotten slot was not persisted as removed.' }
    'PASS: unique object slots, legacy Keys compatibility, record/tombstone validation, filename restrictions, and update/forget isolation.'
}
finally {
    $resolvedTestDirectory = [System.IO.Path]::GetFullPath($testDirectory)
    if ([System.IO.Path]::GetDirectoryName($resolvedTestDirectory) -ne $temporaryRoot -or
        -not [System.IO.Path]::GetFileName($resolvedTestDirectory).StartsWith('SpatialSenseCatalogTests-')) {
        throw 'Refusing cleanup outside the named temporary test directory.'
    }
    Remove-Item -LiteralPath $resolvedTestDirectory -Recurse -Force
}
