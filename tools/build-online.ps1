param([string]$Editor='D:\ZoneZTools\Editors\6000.3.15f1\Editor\Unity.exe')
$ErrorActionPreference='Stop'
$onlineRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$onlineLog=Join-Path $onlineRoot 'artifacts\network-build.log'
$onlineOutput=Join-Path $onlineRoot 'Builds\Online\ZoneZ.exe'
if(-not(Test-Path -LiteralPath $Editor)){throw "Unity editor not found: $Editor"}
New-Item -ItemType Directory -Force -Path (Join-Path $onlineRoot 'artifacts') | Out-Null
if(Test-Path -LiteralPath 'D:\ZoneZTools\Temp'){$env:TEMP='D:\ZoneZTools\Temp';$env:TMP='D:\ZoneZTools\Temp'}
if(Test-Path -LiteralPath 'D:\ZoneZTools'){$env:UPM_CACHE_ROOT='D:\ZoneZTools\PackageCache'}
$onlineArguments=@('-batchmode','-nographics','-projectPath',(Join-Path $onlineRoot 'UnityProject'),'-executeMethod','ZoneZ.Editor.BuildTasks.BuildWindows','-zonez-output',$onlineOutput,'-quit','-logFile',$onlineLog)
$onlineQuoted=$onlineArguments | ForEach-Object { '"'+$_.Replace('"','\"')+'"' }
$onlineProcess=Start-Process -FilePath $Editor -ArgumentList ($onlineQuoted -join ' ') -WindowStyle Hidden -PassThru
$onlineProcess.WaitForExit()
Get-Content -LiteralPath $onlineLog -Tail 30
if($onlineProcess.ExitCode -ne 0){throw "Unity build exit $($onlineProcess.ExitCode). See $onlineLog"}
