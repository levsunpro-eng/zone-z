param(
    [string]$Executable,
    [string]$Python = 'python',
    [ValidateSet('team', 'deathmatch')][string[]]$Modes = @('team', 'deathmatch'),
    [int]$Port = 17780,
    [int]$TimeoutSeconds = 230,
    [string]$ReportDirectory,
    [string]$ServerEndpoint
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$useExternalServer = $PSBoundParameters.ContainsKey('ServerEndpoint')
$scope = if ($useExternalServer) { 'external' } else { 'local' }
if ($useExternalServer) {
    $serverUri = $null
    if ([string]::IsNullOrWhiteSpace($ServerEndpoint) -or
        -not [Uri]::TryCreate($ServerEndpoint.Trim(), [UriKind]::Absolute, [ref]$serverUri) -or
        $serverUri.Scheme -ne 'tls' -or [string]::IsNullOrWhiteSpace($serverUri.Host) -or
        $serverUri.Port -lt 1 -or $serverUri.Port -gt 65535 -or
        -not [string]::IsNullOrEmpty($serverUri.UserInfo) -or
        $serverUri.AbsolutePath -notin @('', '/') -or
        -not [string]::IsNullOrEmpty($serverUri.Query) -or -not [string]::IsNullOrEmpty($serverUri.Fragment)) {
        throw 'ServerEndpoint must be tls://HOST:PORT without credentials, a path, a query, or a fragment.'
    }
    $endpoint = $ServerEndpoint.Trim()
} else {
    if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Port must be from 1024 through 65535.' }
    $endpoint = "tcp://127.0.0.1:$Port"
}
if ([string]::IsNullOrWhiteSpace($Executable)) { $Executable = Join-Path $projectRoot 'Builds\Windows\ZoneZ.exe' }
$Executable = [IO.Path]::GetFullPath($Executable)
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw "Build the game first: $Executable" }
if (-not $useExternalServer) {
    $pythonCommand = Get-Command $Python -ErrorAction Stop
    $pythonPath = $pythonCommand.Source
}
if ([string]::IsNullOrWhiteSpace($ReportDirectory)) { $ReportDirectory = Join-Path $projectRoot 'artifacts\lobby-smoke' }
$runDirectory = Join-Path ([IO.Path]::GetFullPath($ReportDirectory)) ([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$profileRoot = Join-Path ([IO.Path]::GetTempPath()) ('ZoneZ-LobbySmoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory, $profileRoot -Force | Out-Null
function Quote-Arguments([string[]]$Values) { ($Values | ForEach-Object { '"' + $_.Replace('"', '\"') + '"' }) -join ' ' }
function Start-LobbyPlayer([string]$Role, [string]$Name, [string]$Mode, [string]$Reports) {
    $profile = Join-Path $profileRoot ($Mode + '-' + $Name)
    New-Item -ItemType Directory -Path $profile -Force | Out-Null
    $arguments = @('-batchmode', '-nographics', '-zonez-lobby-test', $Role, '-zonez-lobby-mode', $Mode,
        '-zonez-lobby-server', $endpoint, '-zonez-profile', $profile, '-zonez-report', $Reports,
        '-screen-fullscreen', '0', '-screen-width', '640', '-screen-height', '360', '-logFile', (Join-Path $Reports ($Name + '.log')))
    Start-Process -FilePath $Executable -ArgumentList (Quote-Arguments $arguments) -WindowStyle Hidden -PassThru
}
$players = @()
$service = $null
$results = @()
try {
    if (-not $useExternalServer) {
        $serviceArguments = @('-u', (Join-Path $projectRoot 'server\lobby_server.py'), '--host', '127.0.0.1', '--port', "$Port")
        $service = Start-Process -FilePath $pythonPath -ArgumentList (Quote-Arguments $serviceArguments) -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $runDirectory 'service.stdout.log') -RedirectStandardError (Join-Path $runDirectory 'service.stderr.log')
        $ready = $false
        $readyDeadline = [DateTime]::UtcNow.AddSeconds(15)
        while (-not $ready -and [DateTime]::UtcNow -lt $readyDeadline) {
            if ($service.HasExited) { throw "Lobby service exited with $($service.ExitCode). See $runDirectory" }
            $probe = [Net.Sockets.TcpClient]::new()
            try { $probe.Connect('127.0.0.1', $Port); $ready = $true } catch { Start-Sleep -Milliseconds 200 } finally { $probe.Dispose() }
        }
        if (-not $ready) { throw "Lobby service did not listen on $endpoint" }
    }
    foreach ($mode in $Modes) {
        $reports = Join-Path $runDirectory $mode
        New-Item -ItemType Directory -Path $reports -Force | Out-Null
        $players = @()
        try {
            $players += Start-LobbyPlayer 'host' 'Host' $mode $reports
            $players += Start-LobbyPlayer 'friend' 'Friend' $mode $reports
            $players += Start-LobbyPlayer 'rival' 'Rival' $mode $reports
            $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
            while (@($players | Where-Object { -not $_.HasExited }).Count -gt 0) {
                if ($null -ne $service -and $service.HasExited) { throw "Lobby service stopped during $mode. See $runDirectory" }
                foreach ($player in $players) {
                    if ($player.HasExited -and $player.ExitCode -ne 0) { throw "Lobby $mode player $($player.Id) exited with $($player.ExitCode). Reports: $reports" }
                }
                if ([DateTime]::UtcNow -gt $deadline) { throw "Lobby $mode exceeded $TimeoutSeconds seconds. Reports: $reports" }
                Start-Sleep -Milliseconds 300
            }
            foreach ($name in @('Host', 'Friend', 'Rival')) {
                $path = Join-Path $reports ($name + '.json')
                if (-not (Test-Path -LiteralPath $path)) { throw "Missing lobby report: $path" }
                $result = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
                if (-not $result.passed) { throw "Lobby $mode $name failed: $($result.failure). Report: $path" }
                $results += $result
            }
            Write-Output "PASS $mode ($scope server $endpoint) : party, matchmaking, relay, explicit bots; reports $reports"
        }
        finally {
            foreach ($player in $players) {
                if (-not $player.HasExited) { Stop-Process -Id $player.Id -Force -ErrorAction SilentlyContinue }
                $player.Dispose()
            }
            $players = @()
        }
    }
}
finally {
    foreach ($player in $players) { if (-not $player.HasExited) { Stop-Process -Id $player.Id -Force -ErrorAction SilentlyContinue }; $player.Dispose() }
    if ($null -ne $service) {
        if (-not $service.HasExited) { Stop-Process -Id $service.Id -Force -ErrorAction SilentlyContinue }
        $service.Dispose()
    }
}
$summary = [ordered]@{ passed = $true; scope = $scope; serverManagedByTest = -not $useExternalServer; modes = $Modes; endpoint = $endpoint; reports = $runDirectory; profiles = $profileRoot; processes = $results }
$summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runDirectory 'summary.json') -Encoding utf8
$summary
