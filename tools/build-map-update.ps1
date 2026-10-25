param([string]$Editor='D:\ZoneZTools\Editors\6000.3.15f1\Editor\Unity.exe')
$ErrorActionPreference='Stop'
$mapProjectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$mapReport=Join-Path $mapProjectRoot 'artifacts\map-update-2026-10-10'
New-Item -ItemType Directory -Force -Path $mapReport | Out-Null
$env:TEMP='D:\ZoneZTools\Temp';$env:TMP='D:\ZoneZTools\Temp';$env:UPM_CACHE_ROOT='D:\ZoneZTools\PackageCache'
$mapArguments=@('-batchmode','-nographics','-projectPath',(Join-Path $mapProjectRoot 'UnityProject'),
    '-executeMethod','ZoneZ.Editor.BuildTasks.BuildWindows','-zonez-output',(Join-Path $mapProjectRoot 'Builds\MapUpdate\ZoneZ.exe'),
    '-quit','-logFile',(Join-Path $mapReport 'build.log'))
$mapQuoted=$mapArguments | ForEach-Object {'"'+$_.Replace('"','\"')+'"'}
$mapBuild=Start-Process -FilePath $Editor -ArgumentList ($mapQuoted -join ' ') -WindowStyle Hidden -PassThru
$mapBuild.WaitForExit()
Select-String -LiteralPath (Join-Path $mapReport 'build.log') -Pattern 'ZONEZ_.*OK|ZONEZ_AMBIENT_ROUTE_CHECKS|ZONEZ_ART_BUDGETS|ZONEZ_BUILD_RESULT|error CS|Exception:' | ForEach-Object {$_.Line}
if($mapBuild.ExitCode -ne 0){throw "Unity map build failed with exit code $($mapBuild.ExitCode)."}
