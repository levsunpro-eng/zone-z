$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$transportSource = Join-Path $projectRoot 'UnityProject/Assets/ZoneZ/Scripts/Network/NetworkTransport.cs'
$testSource = Join-Path $PSScriptRoot 'NetworkTransportSelfTest.cs'
Add-Type -Path $transportSource, $testSource
[NetworkTransportSelfTest]::Run()
