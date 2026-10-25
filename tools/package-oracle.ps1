param()
$ErrorActionPreference = 'Stop'
$oraclePackageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$oracleSource = Join-Path $oraclePackageRoot 'server\oracle'
$oracleStage = Join-Path $oraclePackageRoot ('artifacts\oracle-package-' + [guid]::NewGuid().ToString('N'))
$oracleArchive = Join-Path $oraclePackageRoot 'Builds\ZoneZ-Oracle-server.zip'
$oracleRequired = @('install.sh', 'zonez-lobby.service', 'install-certificate.py', 'renew-certificate.sh', 'README.md')
foreach ($oracleFile in $oracleRequired) {
    if (-not (Test-Path -LiteralPath (Join-Path $oracleSource $oracleFile) -PathType Leaf)) {
        throw "Oracle deployment file is missing: $oracleFile"
    }
}
New-Item -ItemType Directory -Force -Path (Join-Path $oracleStage 'server\oracle'), (Join-Path $oraclePackageRoot 'Builds') | Out-Null
Copy-Item -LiteralPath (Join-Path $oraclePackageRoot 'server\lobby_server.py') -Destination (Join-Path $oracleStage 'server')
foreach ($oracleFile in (Get-ChildItem -LiteralPath $oracleSource -File)) {
    if ($oracleFile.Name -like 'test_*' -or $oracleFile.Name -like '*.pyc') { continue }
    Copy-Item -LiteralPath $oracleFile.FullName -Destination (Join-Path $oracleStage 'server\oracle')
}
Copy-Item -LiteralPath (Join-Path $oraclePackageRoot 'docs\ORACLE.md') -Destination (Join-Path $oracleStage 'ORACLE.md')
$oracleEntries = Get-ChildItem -LiteralPath $oracleStage | Select-Object -ExpandProperty FullName
Compress-Archive -LiteralPath $oracleEntries -DestinationPath $oracleArchive -Force
Get-Item -LiteralPath $oracleArchive | Select-Object FullName, Length
Get-FileHash -LiteralPath $oracleArchive -Algorithm SHA256
