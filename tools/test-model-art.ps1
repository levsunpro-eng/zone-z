param(
    [ValidateSet('Review','Smoke','Benchmark')][string]$Action='Review',
    [string]$BuildFolder='ModelsPreview',
    [string]$ReportFolder='model-art'
)
$ErrorActionPreference='Stop'
$repoPath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$executable=Join-Path $repoPath ('Builds\'+$BuildFolder+'\ZoneZ.exe')
if(-not(Test-Path -LiteralPath $executable)){throw "Missing player: $executable"}
$reportPath=Join-Path $repoPath ('artifacts\'+$ReportFolder)
$profilePath=Join-Path $reportPath ('profile-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $reportPath,$profilePath | Out-Null
$flag=@{Review='-zonez-art-review';Smoke='-zonez-smoke';Benchmark='-zonez-benchmark'}[$Action]
$resultName=@{Review='model-art-review.json';Smoke='runtime-smoke.json';Benchmark='benchmark.json'}[$Action]
$launchArgs=@($flag,'-zonez-profile',$profilePath,'-zonez-report',$reportPath,'-screen-fullscreen','0','-screen-width','1600','-screen-height','1000','-logFile',(Join-Path $reportPath 'player.log'))
if($Action -eq 'Smoke'){$launchArgs+='-zonez-offscreen-capture'}
$quoted=$launchArgs | ForEach-Object {'"'+$_+'"'}
$testProcess=Start-Process -FilePath $executable -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
$testProcess.WaitForExit()
$result=Get-Content -Raw -LiteralPath (Join-Path $reportPath $resultName) | ConvertFrom-Json
[pscustomobject]@{Action=$Action;Passed=$result.passed;Checks=$result.checks.Count;Views=$result.views.Count;Errors=$result.errors;Failure=$result.failure;Report=$reportPath}|ConvertTo-Json -Depth 4
if($testProcess.ExitCode -ne 0 -or -not$result.passed){throw "$Action failed"}
