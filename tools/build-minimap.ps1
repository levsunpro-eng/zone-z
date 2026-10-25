param([string]$Editor='C:\Program Files\Unity\Hub\Editor\6000.3.15f1\Editor\Unity.exe')
$ErrorActionPreference='Stop'
$miniProjectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$miniReport=Join-Path $miniProjectRoot 'artifacts\minimap-2026-10-10'
New-Item -ItemType Directory -Force -Path $miniReport | Out-Null
if (-not (Test-Path -LiteralPath $Editor)) { throw 'Unity Editor не найден. Укажите путь через параметр -Editor.' }
$miniArguments=@('-batchmode','-nographics','-projectPath',(Join-Path $miniProjectRoot 'UnityProject'),
    '-executeMethod','ZoneZ.Editor.BuildTasks.BuildWindows','-zonez-output',(Join-Path $miniProjectRoot 'Builds\Minimap\ZoneZ.exe'),
    '-quit','-logFile',(Join-Path $miniReport 'build.log'))
$miniQuoted=$miniArguments | ForEach-Object {'"'+$_.Replace('"','\"')+'"'}
$miniBuild=Start-Process -FilePath $Editor -ArgumentList ($miniQuoted -join ' ') -WindowStyle Hidden -PassThru
$miniBuild.WaitForExit()
Select-String -LiteralPath (Join-Path $miniReport 'build.log') -Pattern 'ZONEZ_.*OK|ZONEZ_BUILD_RESULT|error CS|Exception:' | ForEach-Object {$_.Line}
if($miniBuild.ExitCode -ne 0){throw "Unity minimap build failed with exit code $($miniBuild.ExitCode)."}
