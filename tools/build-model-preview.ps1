$ErrorActionPreference='Stop'
$repoPath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editorPath='D:\ZoneZTools\Editors\6000.3.15f1\Editor\Unity.exe'
$env:TEMP='D:\ZoneZTools\Temp';$env:TMP='D:\ZoneZTools\Temp';$env:UPM_CACHE_ROOT='D:\ZoneZTools\PackageCache'
$logPath=Join-Path $repoPath 'artifacts\models-build-latest.log'
$runArgs=@('-batchmode','-nographics','-projectPath',(Join-Path $repoPath 'UnityProject'),'-executeMethod','ZoneZ.Editor.BuildTasks.BuildWindows','-zonez-output',(Join-Path $repoPath 'Builds\ModelsPreview\ZoneZ.exe'),'-quit','-logFile',$logPath)
$quoted=$runArgs|ForEach-Object{'"'+$_+'"'}
$buildProcess=Start-Process -FilePath $editorPath -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
$buildProcess.WaitForExit()
Select-String -LiteralPath $logPath -Pattern 'ZONEZ_.*OK|ZONEZ_BUILD_RESULT|error CS|Exception:' | ForEach-Object {$_.Line}
if($buildProcess.ExitCode -ne 0){throw "Unity build exited $($buildProcess.ExitCode). See $logPath"}
