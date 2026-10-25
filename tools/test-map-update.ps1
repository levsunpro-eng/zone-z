param([ValidateSet('Review','Smoke')][string]$Action='Review')
$ErrorActionPreference='Stop'
$mapProjectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$mapReport=Join-Path $mapProjectRoot ('artifacts\map-update-2026-10-10\'+$Action.ToLowerInvariant())
$mapProfile=Join-Path $mapReport ('qa-profile-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $mapReport,$mapProfile | Out-Null
$mapFlag=if($Action -eq 'Review'){'-zonez-world-review'}else{'-zonez-smoke'}
$mapArguments=@($mapFlag,'-zonez-profile',$mapProfile,'-zonez-report',$mapReport,
    '-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',(Join-Path $mapReport 'player.log'))
if($Action -eq 'Smoke'){$mapArguments+='-zonez-offscreen-capture'}
$mapQuoted=$mapArguments | ForEach-Object {'"'+$_.Replace('"','\"')+'"'}
$mapProcess=Start-Process -FilePath (Join-Path $mapProjectRoot 'Builds\MapUpdate\ZoneZ.exe') -ArgumentList ($mapQuoted -join ' ') -WindowStyle Hidden -PassThru
if(-not $mapProcess.WaitForExit(240000)){$mapProcess.Kill();throw 'The isolated map review exceeded its four-minute timeout.'}
$mapResultFile=if($Action -eq 'Review'){'world-review.json'}else{'runtime-smoke.json'}
$mapResult=Get-Content -LiteralPath (Join-Path $mapReport $mapResultFile) -Raw | ConvertFrom-Json
$mapResult | Select-Object passed,@{n='checkCount';e={$_.checks.Count}},errors,failure,device
if($mapProcess.ExitCode -ne 0 -or -not $mapResult.passed){throw "Map $Action failed; see $mapReport"}
