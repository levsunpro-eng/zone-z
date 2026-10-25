#Requires -Version 7.0
<#
.SYNOPSIS
Compiles and runs the real catalogue, profile persistence, reward and kill-charge
code using PowerShell's bundled C# compiler. This is not a Unity engine test.
.EXAMPLE
pwsh -NoProfile -File .\tools\validate-data.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryPath = Split-Path -Parent $PSScriptRoot
$runtimePath = Split-Path -Parent (Get-Process -Id $PID).Path
$referencesPath = Join-Path $runtimePath 'ref'

if (([System.Management.Automation.PSTypeName]'ZoneZ.Editor.ProfileValidation').Type) {
    throw 'Start a fresh PowerShell process: pwsh -NoProfile -File tools/validate-data.ps1. Previously compiled C# cannot be replaced safely in the same process.'
}
if (-not (Test-Path -LiteralPath $referencesPath -PathType Container)) {
    throw 'PowerShell 7 reference assemblies are unavailable beside the running executable.'
}

Write-Output 'Standalone C# rules/profile validation. Unity is not running; graphics, physics and gameplay are not tested here.'
$sourcePaths = @(
    '.validation/ProfileRuntimeStub.cs',
    'UnityProject/Assets/ZoneZ/Scripts/Data/GameCatalog.cs',
    'UnityProject/Assets/ZoneZ/Scripts/Data/BotDifficultySettings.cs',
    'UnityProject/Assets/ZoneZ/Scripts/Data/GameInput.cs',
    'UnityProject/Assets/ZoneZ/Scripts/Data/ProfileStore.cs',
    'UnityProject/Assets/ZoneZ/Scripts/Core/CombatRules.cs',
    'UnityProject/Assets/ZoneZ/Editor/CoreValidation.cs',
    'UnityProject/Assets/ZoneZ/Editor/ProfileValidation.cs'
) | ForEach-Object { Join-Path $repositoryPath $_ }
$referencePaths = @((Get-ChildItem -LiteralPath $referencesPath -Filter '*.dll').FullName) + [Newtonsoft.Json.JsonConvert].Assembly.Location

# PS's bundled Newtonsoft can target an older System.Runtime. Only the reference
# identity compatibility warning is suppressed; compilation errors still fail.
Add-Type -Path $sourcePaths -ReferencedAssemblies $referencePaths -CompilerOptions @('/define:UNITY_EDITOR', '/nowarn:1701') -ErrorAction Stop
[UnityEngine.Resources]::Root = Join-Path $repositoryPath 'UnityProject/Assets/ZoneZ/Resources'
[ZoneZ.Editor.CoreValidation]::Run()
[ZoneZ.Editor.ProfileValidation]::Run()
Write-Output 'Standalone validation passed. Unity Editor compilation and runtime verification remain separate checks.'
