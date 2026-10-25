param(
    [ValidateSet('Setup','Validate','Build','Smoke','Soak','Benchmark')][string]$Action='Build',
    [string]$Editor='D:\ZoneZTools\Editors\6000.3.15f1\Editor\Unity.exe'
)
$ErrorActionPreference='Stop'
$projectRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project=Join-Path $projectRoot 'UnityProject'
$artifactDir=Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
function Invoke-ZoneZProcess([string]$FilePath,[string[]]$Arguments,[switch]$Visible) {
    # Windows GUI executables otherwise return before Unity has exited.
    $quoted=$Arguments | ForEach-Object { '"'+$_.Replace('"','\"')+'"' }
    $windowStyle=if($Visible){'Normal'}else{'Hidden'}
    $process=Start-Process -FilePath $FilePath -ArgumentList ($quoted -join ' ') -WindowStyle $windowStyle -PassThru
    # Wait only for the editor/player, not persistent compiler/licensing children.
    $process.WaitForExit()
    if($process.ExitCode -ne 0){throw "Процесс завершился с ошибкой $($process.ExitCode): $FilePath"}
}
if($Action -in @('Smoke','Soak','Benchmark')) {
    $executable=Join-Path $projectRoot 'Builds\Animations\ZoneZ.exe'
    if(-not(Test-Path -LiteralPath $executable)){throw 'Сначала соберите Windows-версию: -Action Build'}
    $report=Join-Path $artifactDir $Action.ToLowerInvariant()
    $profile=Join-Path ([IO.Path]::GetTempPath()) ('ZoneZ-Smoke-'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $report,$profile | Out-Null
    $flag='-zonez-'+$Action.ToLowerInvariant()
    $width=if($Action -eq 'Benchmark'){1920}else{1600}
    $height=if($Action -eq 'Benchmark'){1080}else{900}
    Invoke-ZoneZProcess $executable @($flag,'-zonez-profile',$profile,'-zonez-report',$report,'-screen-fullscreen','0','-screen-width',"$width",'-screen-height',"$height",'-logFile',(Join-Path $report 'player.log')) -Visible
    $file=if($Action -eq 'Smoke'){'runtime-smoke.json'}else{$Action.ToLowerInvariant()+'.json'}
    $result=Get-Content -Raw -LiteralPath (Join-Path $report $file) | ConvertFrom-Json
    if(-not $result.passed){throw 'Runtime smoke did not pass'}
    $result
    return
}
if(-not(Test-Path -LiteralPath $Editor)){throw "Редактор не найден: $Editor. Установите Unity 6000.3.15f1 либо задайте -Editor."}
if(Test-Path -LiteralPath 'D:\ZoneZTools\Temp'){$env:TEMP='D:\ZoneZTools\Temp';$env:TMP='D:\ZoneZTools\Temp'}
if(Test-Path -LiteralPath 'D:\ZoneZTools'){$env:UPM_CACHE_ROOT='D:\ZoneZTools\PackageCache'}
$method=@{Setup='Setup';Validate='Validate';Build='BuildWindows'}[$Action]
$log=Join-Path $artifactDir ($Action.ToLowerInvariant()+'.log')
Invoke-ZoneZProcess $Editor @('-batchmode','-nographics','-projectPath',$project,'-executeMethod',('ZoneZ.Editor.BuildTasks.'+$method),'-quit','-logFile',$log)
Get-Content -LiteralPath $log -Tail 20
