param(
    [string]$Executable = '',
    [string]$Report = ''
)
$ErrorActionPreference = 'Stop'
$zoneRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $Executable) { $Executable = Join-Path $zoneRoot 'Builds\Animations\ZoneZ.exe' }
if (-not $Report) { $Report = Join-Path $zoneRoot 'artifacts\animation-review' }
$Executable = [IO.Path]::GetFullPath($Executable)
$Report = [IO.Path]::GetFullPath($Report)
if (-not (Test-Path -LiteralPath $Executable)) { throw "Game executable not found: $Executable" }
$zoneProfile = Join-Path $Report ('profile-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $Report, $zoneProfile | Out-Null
$zoneArgs = @('-zonez-animation-review', '-zonez-profile', $zoneProfile, '-zonez-report', $Report,
    '-screen-fullscreen', '0', '-screen-width', '960', '-screen-height', '540', '-logFile', (Join-Path $Report 'player.log'))
$zoneQuoted = $zoneArgs | ForEach-Object { '"' + $_ + '"' }
# The review explicitly submits render requests; it does not need a visible game window.
$zoneProcess = Start-Process -FilePath $Executable -ArgumentList ($zoneQuoted -join ' ') -WindowStyle Hidden -PassThru
$zoneProcess.WaitForExit()
if ($zoneProcess.ExitCode -ne 0) { throw "Animation review exited $($zoneProcess.ExitCode); see $Report" }
$zoneResult = Get-Content -Raw -LiteralPath (Join-Path $Report 'animation-review.json') | ConvertFrom-Json
if (-not $zoneResult.passed) { throw "Animation review failed; see $Report" }
Write-Output "Animation review passed: $($zoneResult.sequences.Count) sequences, $($zoneResult.checks.Count) checks."
Write-Output "Inspect PNG contact sheets in $Report"
