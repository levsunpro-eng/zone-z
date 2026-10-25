param([string]$BuildFolder='ArtComplete')
$ErrorActionPreference='Stop'
if($BuildFolder -notmatch '^[A-Za-z0-9_-]+$'){throw 'BuildFolder must be a simple directory name.'}
$repoPath=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$editorPath='D:\ZoneZTools\Editors\6000.3.15f1\Editor\Unity.exe'
$env:TEMP='D:\ZoneZTools\Temp';$env:TMP='D:\ZoneZTools\Temp';$env:UPM_CACHE_ROOT='D:\ZoneZTools\PackageCache'
$logPath=Join-Path $repoPath 'artifacts\full-art-build-latest.log'
# Other chats use this checkout too; never terminate or contend with their editor.
$deadline=[DateTime]::UtcNow.AddMinutes(12)
while(Get-Process Unity -ErrorAction SilentlyContinue){
    if([DateTime]::UtcNow -gt $deadline){throw 'Another Unity Editor is still open. No process was stopped.'}
    Start-Sleep -Seconds 5
}
$runArgs=@('-batchmode','-nographics','-projectPath',(Join-Path $repoPath 'UnityProject'),'-executeMethod','ZoneZ.Editor.BuildTasks.BuildWindows','-zonez-output',(Join-Path $repoPath ('Builds\'+$BuildFolder+'\ZoneZ.exe')),'-quit','-logFile',$logPath)
$quoted=$runArgs|ForEach-Object{'"'+$_+'"'}
$buildProcess=Start-Process -FilePath $editorPath -ArgumentList ($quoted -join ' ') -WindowStyle Hidden -PassThru
$buildProcess.WaitForExit()
Select-String -LiteralPath $logPath -Pattern 'ZONEZ_.*OK|ZONEZ_BUILD_RESULT|error CS|Exception:' | ForEach-Object {$_.Line}
if($buildProcess.ExitCode -ne 0){throw "Unity build exited $($buildProcess.ExitCode). See $logPath"}
if(-not(Select-String -LiteralPath $logPath -SimpleMatch 'ZONEZ_BUILD_RESULT Succeeded' -Quiet)){throw "Unity did not confirm successful build. See $logPath"}
if(Select-String -LiteralPath $logPath -SimpleMatch 'does not have a valid GUID' -Quiet){throw "Unity ignored an asset with an invalid GUID. See $logPath"}
$initializers=Get-Content -Raw -LiteralPath (Join-Path $repoPath ('Builds\'+$BuildFolder+'\ZoneZ_Data\RuntimeInitializeOnLoads.json')) | ConvertFrom-Json
if(-not($initializers.root | Where-Object {$_.className -eq 'ArsenalArtReview' -and $_.methodName -eq 'Boot'})){throw 'Arsenal visual review was not included in the compiled player.'}
