param(
    [string]$Executable,
    [ValidateSet('deathmatch', 'team')][string[]]$Modes = @('deathmatch', 'team'),
    [int]$Port = 17777,
    [int]$TimeoutSeconds = 200,
    [string]$ReportDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($Executable)) { $Executable = Join-Path $projectRoot 'Builds\Windows\ZoneZ.exe' }
$Executable = [IO.Path]::GetFullPath($Executable)
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Build the game first; executable is missing: $Executable" }
if ($Port -lt 1024 -or $Port -gt 65525) { throw 'Port must be from 1024 through 65525.' }
if ([string]::IsNullOrWhiteSpace($ReportDirectory)) { $ReportDirectory = Join-Path $projectRoot 'artifacts\network-smoke' }
# A unique run prevents stale stage acknowledgements from making a failed run pass.
$runDirectory = Join-Path ([IO.Path]::GetFullPath($ReportDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$profileRoot = Join-Path ([IO.Path]::GetTempPath()) ('ZoneZ-NetworkSmoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory, $profileRoot -Force | Out-Null

function Start-TestPlayer([string]$Role, [string]$Name, [string]$Mode, [int]$TestPort, [string]$Reports) {
    $profile = Join-Path $profileRoot ($Mode + '-' + $Name)
    New-Item -ItemType Directory -Path $profile -Force | Out-Null
    $arguments = @('-batchmode', '-nographics', '-zonez-net-test', $Role,
        '-zonez-net-name', $Name, '-zonez-net-mode', $Mode, '-zonez-port', "$TestPort",
        '-zonez-profile', $profile, '-zonez-report', $Reports,
        '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '360',
        '-logFile', (Join-Path $Reports ($Name + '.log')))
    $quoted = $arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }
    Start-Process -FilePath $Executable -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
}

$results = @()
$modeIndex = 0
foreach ($mode in $Modes) {
    $reports = Join-Path $runDirectory $mode
    New-Item -ItemType Directory -Path $reports -Force | Out-Null
    $testPort = $Port + $modeIndex
    $modeIndex++
    $players = @()
    try {
        $players += Start-TestPlayer 'host' 'Host' $mode $testPort $reports
        $players += Start-TestPlayer 'client' 'Client1' $mode $testPort $reports
        $players += Start-TestPlayer 'client' 'Client2' $mode $testPort $reports
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        while (@($players | Where-Object { -not $_.HasExited }).Count -gt 0) {
            foreach ($player in $players) {
                if ($player.HasExited -and $player.ExitCode -ne 0) { throw "Network $mode process $($player.Id) exited with $($player.ExitCode). Reports: $reports" }
            }
            if ([DateTime]::UtcNow -gt $deadline) { throw "Network $mode timed out after $TimeoutSeconds seconds. Reports: $reports" }
            Start-Sleep -Milliseconds 300
        }
        foreach ($name in @('Host', 'Client1', 'Client2')) {
            $path = Join-Path $reports ($name + '.json')
            if (-not (Test-Path -LiteralPath $path)) { throw "Missing runtime report: $path" }
            $result = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
            if (-not $result.passed) { throw "Network $mode $name failed: $($result.failure). Report: $path" }
            $results += $result
        }
        Write-Output "PASS $mode : host plus two clients; reports $reports"
    }
    finally {
        foreach ($player in $players) {
            if (-not $player.HasExited) { Stop-Process -Id $player.Id -Force -ErrorAction SilentlyContinue }
            $player.Dispose()
        }
    }
}
$summary = [ordered]@{ passed = $true; modes = $Modes; reports = $runDirectory; profiles = $profileRoot; processes = $results }
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'summary.json') -Encoding utf8
$summary
