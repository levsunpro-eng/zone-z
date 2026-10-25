#Requires -Version 7.0
param([string]$EditorData='D:\ZoneZTools\Editors\6000.3.15f1\Editor\Data')
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output=Join-Path $repo 'artifacts\source-preflight'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$arguments=[Collections.Generic.List[string]]::new()
foreach($option in @('-nostdlib+','-target:library','-langversion:9','-define:UNITY_EDITOR,UNITY_STANDALONE_WIN,UNITY_6000_0_OR_NEWER',('-out:"'+(Join-Path $output 'SourcePreflight.dll')+'"'))){$arguments.Add($option)}
$referencePaths=@((Get-ChildItem -LiteralPath (Join-Path $EditorData 'Managed\UnityEngine') -Filter '*.dll').FullName)
$referencePaths+=@((Get-ChildItem -LiteralPath (Join-Path $EditorData 'Managed\UnityEditor') -Filter '*.dll' -ErrorAction SilentlyContinue).FullName)
$referencePaths+=@('Managed\Newtonsoft.Json.dll','NetStandard\ref\2.1.0\netstandard.dll') | ForEach-Object { Join-Path $EditorData $_ }
foreach($reference in $referencePaths){if($reference){$arguments.Add('-r:"'+$reference+'"')}}
foreach($source in (Get-ChildItem -LiteralPath (Join-Path $repo 'UnityProject\Assets\ZoneZ') -Filter '*.cs' -Recurse)){$arguments.Add('"'+$source.FullName+'"')}
$arguments.Add('"'+(Join-Path $repo '.validation\PackageApiStub.cs')+'"')
$response=Join-Path $output 'compile.rsp'
[IO.File]::WriteAllLines($response,$arguments)
Write-Output 'C# preflight against installed Unity engine APIs. URP/AI Navigation signatures are placeholders; this does not validate package implementation, physics or rendering.'
& (Join-Path $EditorData 'NetCoreRuntime\dotnet.exe') (Join-Path $EditorData 'DotNetSdkRoslyn\csc.dll') ('@'+$response)
if($LASTEXITCODE -ne 0){throw 'Source preflight failed'}
Write-Output 'SOURCE_PREFLIGHT_OK (Unity Editor and runtime verification remain required)'
