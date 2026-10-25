param(
    [string]$Executable,
    [string]$ReportDirectory
)
$ErrorActionPreference = 'Stop'
$arsenalRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Executable) { $Executable = Join-Path $arsenalRepo 'Builds\ArtComplete\ZoneZ.exe' }
if (-not $ReportDirectory) { $ReportDirectory = Join-Path $arsenalRepo 'artifacts\arsenal-art-review' }
$Executable = [IO.Path]::GetFullPath($Executable)
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Build the game first. Executable not found: $Executable" }
New-Item -ItemType Directory -Force -Path $ReportDirectory | Out-Null
$arsenalProfile = Join-Path $ReportDirectory ('profile-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $arsenalProfile | Out-Null
$arsenalLog = Join-Path $ReportDirectory 'player.log'
$arsenalArgs = @(
    '-zonez-arsenal-review', '-zonez-profile', $arsenalProfile,
    '-zonez-report', $ReportDirectory, '-screen-fullscreen', '0',
    '-screen-width', '1600', '-screen-height', '1000', '-logFile', $arsenalLog
)
# Rendering is required: do not add -batchmode or -nographics.
$arsenalQuoted = $arsenalArgs | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }
$arsenalStarted = [DateTime]::UtcNow
$arsenalProcess = Start-Process -FilePath $Executable -ArgumentList ($arsenalQuoted -join ' ') -WindowStyle Hidden -PassThru
if (-not $arsenalProcess.WaitForExit(900000)) {
    throw "Arsenal review exceeded 15 minutes (PID $($arsenalProcess.Id)). See $arsenalLog. The process was not terminated."
}
$arsenalJson = Join-Path $ReportDirectory 'arsenal-art-review.json'
if (-not (Test-Path -LiteralPath $arsenalJson -PathType Leaf)) { throw "Review did not produce JSON (exit $($arsenalProcess.ExitCode)). See $arsenalLog" }
if ((Get-Item -LiteralPath $arsenalJson).LastWriteTimeUtc -lt $arsenalStarted) { throw "JSON belongs to an older run. See $arsenalLog" }
$arsenalResult = Get-Content -Raw -LiteralPath $arsenalJson | ConvertFrom-Json
if ($arsenalProcess.ExitCode -ne 0 -or -not $arsenalResult.passed) {
    $arsenalResult.errors | Select-Object -First 15 | ForEach-Object { Write-Output $_ }
    if ($arsenalResult.failure) { Write-Output $arsenalResult.failure }
    throw "Arsenal art review failed (exit $($arsenalProcess.ExitCode)). See $arsenalJson"
}
if ($arsenalResult.weaponCount -ne 39 -or @($arsenalResult.equipment).Count -ne 9) {
    throw "Incomplete review: expected 39 weapon presentations and 9 equipment models. See $arsenalJson"
}
[pscustomobject]@{
    Passed = $arsenalResult.passed
    Weapons = $arsenalResult.weaponCount
    Equipment = @($arsenalResult.equipment).Count
    Captures = @($arsenalResult.views).Count
    Warnings = @($arsenalResult.warnings).Count
    Index = Join-Path $ReportDirectory 'index.html'
    Report = $arsenalJson
    Profile = $arsenalProfile
}
