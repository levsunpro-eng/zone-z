param()
$ErrorActionPreference='Stop'
$packageRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source=Join-Path $packageRoot 'Builds\Online'
$stage=Join-Path $packageRoot ('artifacts\package-'+[guid]::NewGuid().ToString('N'))
$archive=Join-Path $packageRoot 'Builds\ZoneZ-0.2.0-online.zip'
if(-not(Test-Path -LiteralPath (Join-Path $source 'ZoneZ.exe'))){throw 'Run tools/build-online.ps1 first.'}
New-Item -ItemType Directory -Force -Path $stage | Out-Null
foreach($entry in @('ZoneZ.exe','ZoneZ_Data','UnityPlayer.dll','UnityCrashHandler64.exe','MonoBleedingEdge')) {
    $path=Join-Path $source $entry
    if(-not(Test-Path -LiteralPath $path)){throw "Required player file is missing: $path"}
    Copy-Item -LiteralPath $path -Destination $stage -Recurse
}
if(Test-Path -LiteralPath (Join-Path $source 'D3D12')) {Copy-Item -LiteralPath (Join-Path $source 'D3D12') -Destination $stage -Recurse}
foreach($license in @('WEAPON_ASSET_LICENSE.md','HANDS_ASSET_LICENSE.md','CHARACTER_ASSET_LICENSE.md')) {
    $licensePath=Join-Path $packageRoot ('docs\'+$license)
    if(Test-Path -LiteralPath $licensePath){Copy-Item -LiteralPath $licensePath -Destination $stage}
}
if(Test-Path -LiteralPath (Join-Path $source 'online-server.txt')) {Copy-Item -LiteralPath (Join-Path $source 'online-server.txt') -Destination $stage}
Set-Content -LiteralPath (Join-Path $stage 'ИГРАТЬ.cmd') -Encoding ascii -Value '@echo off','start "" "%~dp0ZoneZ.exe"'
Set-Content -LiteralPath (Join-Path $stage 'ПРОЧИТАЙ.txt') -Encoding utf8 -Value @'
ЗОНА Z — версия 0.2.0 для Windows

Распакуйте весь архив и запустите ИГРАТЬ.cmd или ZoneZ.exe.
Оставьте ZoneZ_Data, MonoBleedingEdge и DLL рядом с игрой.

Боты: Играть → Локально. Интернет не требуется.
Друзья: Играть → Онлайн → Лобби и поиск.
Для лобби нужен адрес размещённого общего сервера. В этой поставке
публичный сервер ещё не настроен. Код комнаты сам по себе сервер не создаёт.
Лидер создаёт лобби и передаёт код друзьям; соперники должны искать
матч на том же сервере в другом лобби. Поиск не заменяет людей ботами.
Игра своей группой с ботами запускается отдельной кнопкой.

По адресу: один игрок создаёт матч, остальные вводят IP, порт и код.
Через интернет этому варианту нужен открытый TCP-порт хоста или общая VPN.
Создатель матча должен держать игру запущенной. Esc не останавливает онлайн-бой.

Всем нужна одна версия игры. Личные сохранения в архив не включены.
'@
$files=Get-ChildItem -LiteralPath $stage | Select-Object -ExpandProperty FullName
Compress-Archive -LiteralPath $files -DestinationPath $archive -Force
Get-Item -LiteralPath $archive | Select-Object FullName,Length
Get-FileHash -LiteralPath $archive -Algorithm SHA256 | Format-List
