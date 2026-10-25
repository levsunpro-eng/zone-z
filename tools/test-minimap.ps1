param([ValidateSet('Review','Smoke')][string]$Action='Review')
$ErrorActionPreference='Stop'
$miniProjectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$miniReport=Join-Path $miniProjectRoot ('artifacts\minimap-2026-10-10\'+$Action.ToLowerInvariant())
$miniProfile=Join-Path $miniReport ('qa-profile-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $miniReport,$miniProfile | Out-Null
$miniFlag=if($Action -eq 'Review'){'-zonez-minimap-review'}else{'-zonez-smoke'}
$miniArguments=@($miniFlag,'-zonez-profile',$miniProfile,'-zonez-report',$miniReport,
    '-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',(Join-Path $miniReport 'player.log'))
if($Action -eq 'Smoke'){$miniArguments+='-zonez-offscreen-capture'}
$miniQuoted=$miniArguments | ForEach-Object {'"'+$_.Replace('"','\"')+'"'}
$miniProcess=Start-Process -FilePath (Join-Path $miniProjectRoot 'Builds\Minimap\ZoneZ.exe') -ArgumentList ($miniQuoted -join ' ') -WindowStyle Hidden -PassThru
if(-not $miniProcess.WaitForExit(240000)){$miniProcess.Kill();throw 'The isolated minimap review exceeded its four-minute timeout.'}
$miniResultFile=if($Action -eq 'Review'){'minimap-review.json'}else{'runtime-smoke.json'}
$miniResult=Get-Content -LiteralPath (Join-Path $miniReport $miniResultFile) -Raw | ConvertFrom-Json
$miniResult | Select-Object passed,@{n='checkCount';e={$_.checks.Count}},errors,failure,device
if($miniProcess.ExitCode -ne 0 -or -not $miniResult.passed){throw "Minimap $Action failed; see $miniReport"}
