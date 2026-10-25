param(
    [string]$Executable,
    [int]$Port = 18779,
    [int]$TimeoutSeconds = 120,
    [string]$ReportDirectory,
    [string]$LobbyEndpoint,
    [switch]$Visible
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($Executable)) { $Executable = Join-Path $projectRoot 'Builds\Online\ZoneZ.exe' }
$Executable = [IO.Path]::GetFullPath($Executable)
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Build the graphical game first: $Executable" }
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Port must be from 1024 through 65535.' }
if ($TimeoutSeconds -lt 20 -or $TimeoutSeconds -gt 600) { throw 'TimeoutSeconds must be from 20 through 600.' }
if ([string]::IsNullOrWhiteSpace($ReportDirectory)) { $ReportDirectory = Join-Path $projectRoot 'artifacts\network-ui' }
$runDirectory = Join-Path ([IO.Path]::GetFullPath($ReportDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$profileDirectory = Join-Path ([IO.Path]::GetTempPath()) ('ZoneZ-NetworkUI-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory, $profileDirectory -Force | Out-Null
$arguments = @('-zonez-net-ui', '-zonez-ui-port', "$Port", '-zonez-profile', $profileDirectory,
    '-zonez-report', $runDirectory, '-screen-fullscreen', '0', '-screen-width', '1600', '-screen-height', '900',
    '-logFile', (Join-Path $runDirectory 'network-ui.log'))
if (-not [string]::IsNullOrWhiteSpace($LobbyEndpoint)) { $arguments += @('-zonez-ui-lobby-server', $LobbyEndpoint) }
# No -batchmode/-nographics: OnGUI and WaitForEndOfFrame need real rendering.
$quoted = $arguments | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }
$player = $null
$failure = $null
$runtime = $null
try {
    $windowStyle=if($Visible){'Normal'}else{'Hidden'}
    $player = Start-Process -FilePath $Executable -ArgumentList ($quoted -join ' ') -WindowStyle $windowStyle -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while (-not $player.HasExited) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "UI capture exceeded $TimeoutSeconds seconds. See $runDirectory" }
        Start-Sleep -Milliseconds 250
    }
    if ($player.ExitCode -ne 0) { throw "Graphical UI process exited with $($player.ExitCode). See $runDirectory" }
    $reportPath = Join-Path $runDirectory 'network-ui.json'
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "Missing runtime report: $reportPath" }
    $runtime = Get-Content -Raw -LiteralPath $reportPath | ConvertFrom-Json
    if (-not $runtime.passed) { throw "UI runtime failed: $($runtime.failure). See $reportPath" }
    $expected = @('01-local', '02-lobby-connect', '03-direct-host', '04-direct-join', '05-host-pause')
    if (-not [string]::IsNullOrWhiteSpace($LobbyEndpoint)) { $expected += @('06-lobby-room', '07-lobby-search') }
    foreach ($name in $expected) {
        $path = Join-Path $runDirectory ($name + '.png')
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing screenshot: $path" }
        if ((Get-Item -LiteralPath $path).Length -lt 1024) { throw "Screenshot is unexpectedly small: $path" }
        $shot = @($runtime.screenshots | Where-Object { $_.name -eq $name })
        if ($shot.Count -ne 1 -or $shot[0].width -ne 1600 -or $shot[0].height -ne 900 -or $shot[0].sampledColors -lt 8) {
            throw "Screenshot dimensions or pixel checks failed: $path"
        }
    }
}
catch { $failure = $_.Exception.Message }
finally {
    # Only this launcher's own process may be stopped; never kill by name.
    if ($null -ne $player) {
        if (-not $player.HasExited) { Stop-Process -Id $player.Id -Force -ErrorAction SilentlyContinue }
        $player.Dispose()
    }
}
$summary = [ordered]@{ passed = ($null -eq $failure); reports = $runDirectory; profile = $profileDirectory;
    endpoint = $LobbyEndpoint; failure = $failure; runtime = $runtime;
    visualInspectionRequired = $true }
$summary | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath (Join-Path $runDirectory 'summary.json') -Encoding utf8
if ($null -ne $failure) { throw "$failure Summary: $(Join-Path $runDirectory 'summary.json')" }
Write-Output "PASS graphical UI: $($runtime.screenshots.Count) rendered screenshots. Inspect layout in $runDirectory"
$summary
